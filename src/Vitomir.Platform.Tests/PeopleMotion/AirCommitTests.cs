// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// (From play of the earlier vaults:) <b>the commit point in the air</b> (BodyMotor.Commit.cs, <see cref="PersonCommitSettings"/>) on the
/// body's own motor and the player's spring, with the ledge's clips: the commit its lead before the contact; a catch by intent (a jump taken at the wall by
/// itself, use held, neither: past it); the feet clearing a top land on it; no motor's stand-in grab from the air; braced by a wall with no free hang between;
/// a lip above the arc's reach at 2.85 m; a slow fall's catch played slower; a short fall's prelanding by cutoff or part way and its fade in
/// game time; the switch. Skips without the models repository's files, or.
/// </summary>
public sealed class AirCommitTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonLedgeClips Ledge(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge is not null && person.Over is not null && person.Air is { Prelandings.Length: > 0 },
                          "the models repository's files are too old (no ledge clips or prelandings)");
        return person.Ledge!;
    }

    /// <summary>
    /// A world with the motor (the player's spring), the animation with the air's, the moves', the ledge's clips, the prelanding (<paramref name="pre"/>, the
    /// default's when null) and the commit point (<paramref name="commit"/>; the earlier build's when off), one body of the kind (<paramref name="loco"/>, the sandbox's
    /// human when null) facing +X at <paramref name="at"/>; the floor at 0 and <paramref name="blocks"/>.
    /// </summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonCommitSettings commit, Vector3 at, PersonPrelandingSettings? pre = null,
                                                     Locomotion? loco = null, params (Vector3 Min, Vector3 Max)[] blocks) =>
        Make(assets, kind, commit, at, null, null, pre, loco, blocks);

    /// <summary>As the overload before, with tier 1's moves (<paramref name="moves"/>, null for none: the earlier build's) and the level's ladders (<paramref name="ladders"/>).</summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonCommitSettings commit, Vector3 at, PersonMovesSettings? moves,
                                                     LevelLadders? ladders, PersonPrelandingSettings? pre = null, Locomotion? loco = null,
                                                     params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var collision = t.Build("floor").Collision;
        if (ladders is not null) collision = collision.WithLadders(ladders);
        var level = new LevelSpace("floor", collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        var prelanding = pre ?? (commit.On ? new PersonPrelandingSettings() : new PersonPrelandingSettings().WithoutCommit);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: prelanding, commit: commit, moves: moves);
        b.Service(new PersonHandsSettings());
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, loco ?? Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        if (w.TryStore<PersonLedge>(out _)) w.Set(e, new PersonLedge());
        if (w.TryStore<PersonCommit>(out _)) w.Set(e, new PersonCommit());
        if (w.TryStore<PersonLadder>(out _)) w.Set(e, new PersonLadder());
        return (w, e);
    }

    /// <summary>What is asked on a tick: the wish (x ahead, y to the left of +X), the gait, jump pressed, jump held, use held.</summary>
    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Walk, bool Jump = false, bool Held = false, bool Use = false);

    /// <summary>A tick as it went: the clip and its time, the fade, the move, the body, the ledge's clip, the commit, and as drawn (world) the pelvis, both knuckles, both wrists' and both shoulders' joints.</summary>
    internal sealed record Row(string Clip, float Time, float Rate, string Previous, float Blend, TraversalKind Move, BodyState State, Vector3 Feet, Vector3 Velocity,
                               string Ledge, AirOutcome Committed, float CommitSeconds, bool Auto, Vector3 Pelvis, Vector3 KnuckleL, Vector3 KnuckleR,
                               Vector3 WristL, Vector3 WristR, Vector3 ShoulderL, Vector3 ShoulderR, Vector3 ElbowL, int Catch,
                               CatchRefused Why = CatchRefused.None, float LedgeTime = 0f);

    /// <summary>
    /// <paramref name="ticks"/> ticks asked by <paramref name="plan"/> (the tick, the body), after <paramref name="settle"/> standing; a body put in the air first
    /// by <paramref name="launch"/> (its feet, velocity, whether from a jump). Every tick's row.
    /// </summary>
    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks, int settle = 20,
                                    (Vector3 Feet, Vector3 Velocity, bool Jumped)? launch = null, float launchAir = 0f)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var paths = assets.Kind(kind).Person!.Ledge?.Paths;
        for (int i = 0; i < settle; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        if (launch is { } l)
        {
            ref var body = ref w.Get<Body>(e);
            body.Feet = l.Feet;
            body.Velocity = l.Velocity;
            body.State = BodyState.Air;
            body.Jumped = l.Jumped ? 1 : 0;
            body.Rising = l.Velocity.Z > 0f ? 1 : 0;
            body.TakeOffZ = l.Jumped ? 0f : l.Feet.Z;
            body.HighestZ = l.Feet.Z;
            body.JumpRise = 0.5f;
            body.AirSeconds = launchAir;   // how long it has been in the air (a fall put in it at a jump's top: a while)
            body.Coyote = 0f;
            body.GroundTriangle = -1;
            body.GroundNormal = Vector3.UnitZ;
        }
        var committed = w.Events<AirCommitted>();
        for (int i = 0; i < ticks; i++)
        {
            Set(w, e, plan(i, w.Get<Body>(e)));
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var ledge = w.TryStore<PersonLedge>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            var k = w.TryStore<PersonCommit>(out var ks) && ks.Has(e) ? ks.Get(e).Move : default;
            string clip = ledge.Clip > 0 && paths?.At(ledge.Clip - 1) is { } c ? c.Name : "-";
            var said = committed.ThisTick;
            var what = AirOutcome.None;
            float secs = 0f;
            bool auto = false;
            var why = CatchRefused.None;
            for (int j = 0; j < said.Count; j++) if (said[j].Entity == e) (what, secs, auto, why) = (said[j].Outcome, said[j].Seconds, said[j].Auto, said[j].Why);
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.Rate, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", a.PreviousClip >= 0 ? a.Blend : 1f,
                             w.Get<Traversal>(e).Kind, body.State, body.Feet, body.Velocity, clip, what, secs, auto, Bone(w, e, assets, rig.Pelvis),
                             Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip), Bone(w, e, assets, rig.LeftArm.End),
                             Bone(w, e, assets, rig.RightArm.End), Bone(w, e, assets, rig.LeftArm.Upper), Bone(w, e, assets, rig.RightArm.Upper),
                             Bone(w, e, assets, rig.LeftArm.Lower), k.Catch, why, ledge.Time));
        }
        return rows;
    }

    static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = ask.Gait;
        intent.Faces = false;
        intent.Jump = ask.Jump;
        intent.JumpHeld = ask.Jump || ask.Held;
        intent.Crouch = false;
        intent.UseHeld = ask.Use;
    }

    void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-22} {F(r.Time, "0.000")} x{F(r.Rate, "0.00")} {(r.Previous != "-" ? $"<{r.Previous} {F(r.Blend)}" : ""),-26} {r.Move,-6} {r.State,-10} "
                             + $"{r.Ledge,-14} {(r.Committed != AirOutcome.None ? $"[{r.Committed} {F(r.CommitSeconds, "0.000")}{(r.Auto ? " auto" : "")}]" : ""),-18} "
                             + $"feet {F(r.Feet.X, "0.000")},{F(r.Feet.Z, "0.000")} v {F(r.Velocity.X)},{F(r.Velocity.Z)} pelvis {F(r.Pelvis.X, "0.000")},{F(r.Pelvis.Z, "0.000")} "
                             + $"knuckles {F(0.5f * (r.KnuckleL.X + r.KnuckleR.X), "0.000")},{F(0.5f * (r.KnuckleL.Z + r.KnuckleR.Z), "0.000")}");
        }
    }

    /// <summary>The largest change a tick of a point's change a tick (m: a pop) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static float Pop(List<Row> rows, int from, int to, Func<Row, Vector3> of)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    /// <summary>Where the largest pop is (the tick).</summary>
    internal static int ArgPop(List<Row> rows, int from, int to, Func<Row, Vector3> of)
    {
        float pop = -1f;
        int at = -1;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
        {
            float p = ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length();
            if (p > pop) (pop, at) = (p, i);
        }
        return at;
    }

    internal static Vector3 PelvisOf(Row r) => r.Pelvis;
    internal static Vector3 HandsOf(Row r) => 0.5f * (r.WristL + r.WristR);

    /// <summary>A wall across the way at x 1, its top <paramref name="h"/>, 2 m deep, 6 m wide (a wall under the edge: braced).</summary>
    internal static (Vector3 Min, Vector3 Max) Wall(float h, float y0 = -3f, float y1 = 3f) => (new Vector3(1f, y0, 0f), new Vector3(3f, y1, h));

    /// <summary>The knuckles (both hands' mean) against the edge at x 1, top <paramref name="h"/>: forward of the face (into the wall positive) and above the top.</summary>
    static Vector2 KnucklesAt(Row r, float h) => new(0.5f * (r.KnuckleL.X + r.KnuckleR.X) - 1f, 0.5f * (r.KnuckleL.Z + r.KnuckleR.Z) - h);

    static readonly PersonCommitSettings On = new();

    /// <summary>A run from 8 m short at the wall of <paramref name="h"/> with jump pressed <paramref name="press"/> m short of the face (along a way <paramref name="angle"/>° off square), use held or not.</summary>
    List<Row> RunJump(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, float press, float angle = 0f, bool use = false, int ticks = 560)
    {
        var (s, c) = DetMath.SinCos(angle * DetMath.DegToRad);
        var way = new Vector2(c, s);
        float back = 8f;
        var (w, e) = Make(assets, kind, commit, new Vector3(1f - back * c, -back * s, 0f), blocks: [Wall(h, -12f, 12f)]);
        w.Get<Body>(e).Yaw = DetMath.Atan2(s, c);
        bool pressed = false;
        return Drive(w, e, assets, kind, (i, b) =>
        {
            bool now = !pressed && b.State == BodyState.Ground && 1f - b.Feet.X <= press;
            pressed |= now;
            // The way held running, through the jump's squash and in the air (as a player runs and jumps), nothing once caught.
            return new Ask(b.State is BodyState.Ground or BodyState.Air ? way : Vector2.Zero, Gait.Run, Jump: now, Use: use);
        }, ticks);
    }

    /// <summary>A standing jump (tapped on the first tick) at the wall of <paramref name="h"/> from <paramref name="short"/> m short of its face (the middle), use held or not.</summary>
    List<Row> StandJump(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, float @short, bool use = false, int ticks = 360)
    {
        var (w, e) = Make(assets, kind, commit, new Vector3(1f - @short, 0f, 0f), blocks: [Wall(h)]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Jump: i == 0, Use: use), ticks);
    }

    /// <summary>
    /// A body put in the air facing the wall of <paramref name="h"/>, its middle <paramref name="out_"/> m out from the face, its feet <paramref name="over"/> m over the
    /// falling grab's contact place (its capsule's height at <c>contact_s</c> against that edge), at <paramref name="vz"/> m/s up; use held (from <paramref name="useFrom"/>) or not.
    /// </summary>
    List<Row> Fall(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, float over, float vz = 0f, bool use = false, float out_ = 0.45f, int ticks = 360,
                   int useTo = int.MaxValue, float vx = 0f, float air = 0f)
    {
        var c = Ledge(assets, kind).Paths.Of(LedgeRole.GrabFall)!;
        float contactZ = h - (c.EdgeUp - c.At(c.ContactS).Up);
        var (w, e) = Make(assets, kind, commit, new Vector3(1f - out_, 0f, 0f), blocks: [Wall(h)]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Use: use && i < useTo), ticks,
                     launch: (new Vector3(1f - out_, 0f, contactZ + over), new Vector3(vx, 0f, vz), false), launchAir: air);
    }

    static int First(List<Row> rows, Func<Row, bool> p, int from = 0) => rows.FindIndex(Math.Max(0, from), r => p(r));

    /// <summary>The first tick the motor's own (stand-in) grab carries the body: a grab with no ledge clip.</summary>
    static int StandIn(List<Row> rows) => First(rows, r => r.Move == TraversalKind.Grab && r.Ledge == "-");

    /// <summary>What a catch did: its commit's tick, the clip, its first tick, its contact's tick (the clip's time at its contact_s), and the hang it ended in.</summary>
    sealed record Caught(int Commit, string Clip, int Start, int Contact, string Hang, bool Auto, float Lead);

    static Caught? CatchOf(List<Row> rows, LedgePaths paths)
    {
        int commit = First(rows, r => r.Committed == AirOutcome.Catch);
        if (commit < 0) return null;
        int start = First(rows, r => r.Ledge.StartsWith("grab_", StringComparison.Ordinal), commit);
        if (start < 0) return null;
        string clip = rows[start].Ledge;
        var c = paths.Clips.First(x => x.Name == clip);
        int contact = First(rows, r => r.Ledge == clip && r.Time >= c.ContactS - 1e-3f, start);
        int end = First(rows, r => r.Ledge != clip, start);
        return new Caught(commit, clip, start, contact, end >= 0 ? rows[end].Ledge : "-", rows[commit].Auto, rows[commit].CommitSeconds);
    }

    [Fact]
    public void The_commit_comes_its_lead_before_the_hands_contact()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // Falling past a 3 m wall from 1.6 m over the falling grab's contact, use held: committed 0.3 s before the contact, the clip from then, its contact on the tick.
        var rows = Fall(assets, kind, On, 3f, 1.6f, use: true);
        var g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Say("falling past 3 m, use held", rows, g!.Commit - 3, g.Contact + 3);
        Assert.Equal("grab_fall", g.Clip);
        Assert.False(g.Auto);
        Assert.InRange(g.Lead, On.LeadSeconds - Tick - 1e-4f, On.LeadSeconds + 1e-4f);
        int ticks = (int)MathF.Round(g.Lead / Tick);
        Assert.InRange(g.Contact - g.Commit, ticks - 1, ticks);
        output.WriteLine($"committed {F(g.Lead, "0.000")} s before the contact, the clip's contact {g.Contact - g.Commit} ticks after; {g.Hang}");
        // A shorter lead (a setting): the same, from 0.2 s.
        var shorter = On with { LeadSeconds = 0.2f };
        rows = Fall(assets, kind, shorter, 3f, 1.6f, use: true);
        g = CatchOf(rows, paths)!;
        Assert.InRange(g.Lead, 0.2f - Tick - 1e-4f, 0.2f + 1e-4f);
        // Use let go after the commit does not change it.
        rows = Fall(assets, kind, On, 3f, 1.6f, use: true, useTo: 0);
        Assert.Null(CatchOf(rows, paths));   // let go before: nothing caught
        var at = Fall(assets, kind, On, 3f, 1.6f, use: true);
        int when = CatchOf(at, paths)!.Commit;
        rows = Fall(assets, kind, On, 3f, 1.6f, use: true, useTo: when + 1);
        Assert.NotNull(CatchOf(rows, paths));
        Assert.StartsWith("hang_", rows[^1].Ledge);
    }

    [Fact]
    public void A_jump_at_a_wall_catches_by_itself_use_held_catches_and_neither_falls_past()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // Running square at a 2.2 m wall: caught by itself (the jump was at it), on the running grab, braced.
        var rows = RunJump(assets, kind, On, 2.2f, 1.6f);
        var g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Say("a running jump at 2.2 m", rows, g!.Commit - 2, g.Contact + 2);
        Assert.True(g.Auto, "the jump's own");
        Assert.StartsWith("grab_", g.Clip);   // pressed 1.6 m short the arc comes to the wall past its top: caught falling there (nearer, rising: grab_run)
        Assert.Equal(-1, StandIn(rows));
        Assert.StartsWith("hang_braced", rows[^1].Ledge);
        // At 70° to the wall (beyond the 60° cone, the earlier build's 30°): past it without use, caught with it.
        // Pressed nearer, the arc comes to the wall rising: the running grab's air part.
        rows = RunJump(assets, kind, On, 2.2f, 1.35f);
        g = CatchOf(rows, paths);
        Assert.NotNull(g);
        output.WriteLine($"pressed 1.35 m short: {g!.Clip}, {g.Hang}");
        Assert.Equal("grab_run", g.Clip);
        Assert.Equal(-1, StandIn(rows));
        rows = RunJump(assets, kind, On, 2.2f, 1.0f, angle: 70f);
        Assert.Null(CatchOf(rows, paths));
        Assert.Contains(rows, r => r.Committed == AirOutcome.Past);
        Assert.Equal(-1, StandIn(rows));
        Assert.Equal(BodyState.Ground, rows[^1].State);
        rows = RunJump(assets, kind, On, 2.2f, 1.0f, angle: 70f, use: true);
        g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Assert.False(g!.Auto);
        output.WriteLine($"at 70°: past without use; with use {g.Clip}, {g.Hang}");
        // Falling (no jump) past a lip: past without use (lands), caught with it.
        rows = Fall(assets, kind, On, 3f, 1f);
        Assert.Null(CatchOf(rows, paths));
        Assert.Contains(rows, r => r.Committed == AirOutcome.Past);
        Assert.Equal(-1, StandIn(rows));
        Assert.Equal(BodyState.Ground, rows[^1].State);
        rows = Fall(assets, kind, On, 3f, 1f, use: true);
        Assert.Equal("grab_fall", CatchOf(rows, paths)!.Clip);
        // Jump held (the charged jump's) is not a catch: falling with it held and no use falls past.
        var (w, e) = Make(assets, kind, On, new Vector3(0.55f, 0f, 0f), blocks: [Wall(3f)]);
        var c = paths.Of(LedgeRole.GrabFall)!;
        rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Held: true), 300,
                     launch: (new Vector3(0.55f, 0f, 3f - (c.EdgeUp - c.At(c.ContactS).Up) + 1f), Vector3.Zero, false));
        Assert.Null(CatchOf(rows, paths));
        Assert.Equal(-1, StandIn(rows));
    }

    [Fact]
    public void The_feet_clearing_a_top_land_on_it()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // Thrown up at a 2.4 m wall from 1 m short (as a charged jump would be), the arc clears its top: committed to the top, landed on it, nothing caught.
        var (w, e) = Make(assets, kind, On, new Vector3(0f, 0f, 0f), blocks: [Wall(2.4f)]);
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Gait.Run), 240, launch: (new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 6f), true));
        int top = First(rows, r => r.Committed is AirOutcome.Top or AirOutcome.Ground);
        Say("thrown over 2.4 m", rows, top - 2, top + 3);
        Assert.True(top >= 0, "committed to the landing on the top");
        Assert.Null(CatchOf(rows, paths));
        Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None);
        int landed = First(rows, r => r.State == BodyState.Ground);
        Assert.True(landed > top);
        Assert.InRange(rows[landed].Feet.Z, 2.39f, 2.42f);
        // The same arc is caught by the motor's own grab (its hands at the lip) or mantled.
        var (w0, e0) = Make(assets, kind, PersonCommitSettings.Off, new Vector3(0f, 0f, 0f), blocks: [Wall(2.4f)]);
        var old = Drive(w0, e0, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Gait.Run), 240, launch: (new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 6f), true));
        int moved = First(old, r => r.Move != TraversalKind.None);
        output.WriteLine($"rules 2: landed on the top at {F(rows[landed].Feet.Z, "0.000")} m; off: {(moved >= 0 ? old[moved].Move.ToString() : "nothing")}");
        // A gap jumped across onto a top the feet clear (a 1.2 m platform, a 0.6 m gap, a 1.4 m top): landed on it, its landing's lead-in before.
        (w, e) = Make(assets, kind, On, new Vector3(-4f, 0f, 1.2f), blocks: [(new Vector3(-10f, -3f, 0f), new Vector3(0f, 3f, 1.2f)), (new Vector3(0.6f, -3f, 0f), new Vector3(4f, 3f, 1.4f))]);
        bool pressed = false;
        rows = Drive(w, e, assets, kind, (i, b) =>
        {
            bool now = !pressed && b.State == BodyState.Ground && b.Feet.X >= -0.9f;
            pressed |= now;
            return new Ask(new Vector2(1f, 0f), Gait.Run, Jump: now);
        }, 320);
        landed = First(rows, r => r.State == BodyState.Ground && r.Feet.X > 0.6f);
        if (landed <= 0) Say("across the gap", rows.Where((r, i) => i % 3 == 0).ToList(), 0, rows.Count / 3);
        Assert.True(landed > 0, "landed on the far top");
        Assert.InRange(rows[landed].Feet.Z, 1.39f, 1.42f);
        Assert.Null(CatchOf(rows, paths));
        output.WriteLine($"across the gap: committed to {string.Join(", ", rows.Where(r => r.Committed != AirOutcome.None).Select(r => r.Committed))}, landed on {rows[landed].Clip}");
        Assert.Contains(rows, r => r.Committed is AirOutcome.Top or AirOutcome.Ground);
    }

    [Fact]
    public void Braced_by_a_wall_the_grab_goes_into_the_brace_with_no_free_hang_between()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        var stand = paths.Of(LedgeRole.GrabStand)!;
        float h = MathF.Round(stand.OwnHeight, 2);
        foreach (var commit in new[] { On, PersonCommitSettings.Off })
        {
            var rows = StandJump(assets, kind, commit, h, 0.45f, ticks: 400);
            var seq = new List<string>();
            foreach (var r in rows) if (r.Ledge != "-" && (seq.Count == 0 || seq[^1] != r.Ledge)) seq.Add(r.Ledge);
            int brace = First(rows, r => r.Ledge == "hang_brace_on");
            output.WriteLine($"{Of(commit)} a standing grab at {F(h)} m: {string.Join(" > ", seq)}; the pelvis's pop into the brace {F(Pop(rows, brace - 3, brace + 30, PelvisOf) * 1000f, "0.0")} mm, the hands' {F(Pop(rows, brace - 3, brace + 30, HandsOf) * 1000f, "0.0")} mm");
            Assert.True(brace > 0);
            if (commit.On)
            {
                Assert.Equal(new[] { "grab_jump_up", "hang_brace_on", "hang_braced" }, seq.Take(3));
                Assert.Equal("grab_jump_up", rows[brace].Previous);   // blended from the grab's pose
                Assert.InRange(rows[brace - 1].Time, stand.Duration - On.BraceOverlap - 0.02f, stand.Duration - On.BraceOverlap + 0.02f);
                Assert.InRange(Pop(rows, brace - 3, brace + 30, PelvisOf), 0f, 0.01f);
            }
        }
        // Under a slab (nothing under the edge): the free hang.
        var (w, e) = Make(assets, kind, On, new Vector3(0.55f, 0f, 0f), blocks: [(new Vector3(1f, -3f, h - 0.3f), new Vector3(3f, 3f, h))]);
        var free = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Jump: i == 0), 360);
        Assert.StartsWith("hang_free", free[^1].Ledge);
        Assert.DoesNotContain(free, r => r.Ledge == "hang_brace_on");
    }

    /// <summary>The arm: its stretch (the shoulder to the wrist) at a tick against its reach (the upper and the lower arm's lengths).</summary>
    static (float Stretch, float Reach) Arm(Row r) => ((r.WristL - r.ShoulderL).Length(), (r.ElbowL - r.ShoulderL).Length() + (r.WristL - r.ElbowL).Length());

    [Fact]
    public void A_lip_above_the_arc_s_reach_is_caught_at_its_top_the_arms_reaching_EW2()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        foreach (float h in new[] { 2.6f, 2.8f, 2.85f })
        {
            var rows = StandJump(assets, kind, On, h, 0.5f);
            var g = CatchOf(rows, paths);
            if (g is null) Say($"no catch at {F(h)} m", rows, 0, 120);
            Assert.NotNull(g);
            Assert.Equal("grab_jump_up", g!.Clip);
            Assert.Equal(-1, StandIn(rows));
            Assert.True(g.Auto, "a jump at the wall");
            var at = KnucklesAt(rows[g.Contact], h);
            var (stretch, reach) = Arm(rows[g.Contact]);
            float lowest = rows.Skip(g.Commit).Take(g.Contact - g.Commit + 1).Max(r => r.Feet.Z);
            output.WriteLine($"{F(h)} m: committed {F(g.Lead, "0.000")} s before the contact; at it the knuckles {F(at.X * 100f, "0.0")} cm forward of the edge, {F(at.Y * 100f, "0.0")} cm over it; "
                             + $"the arm {F(stretch * 100f, "0.0")} of its {F(reach * 100f, "0.0")} cm ({F(stretch / reach * 100f, "0")} %); the capsule's top {F(lowest, "0.000")} m; then {g.Hang}; "
                             + $"pops pelvis {F(Pop(rows, g.Commit - 2, g.Contact + 60, PelvisOf) * 1000f, "0.0")} mm, hands {F(Pop(rows, g.Commit - 2, g.Contact + 60, HandsOf) * 1000f, "0.0")} mm");
            Assert.InRange(at.X, -0.08f, 0.02f);
            Assert.InRange(at.Y, -0.04f, 0.08f);
            Assert.StartsWith("hang_braced", rows[^1].Ledge);
        }
    }

    [Fact]
    public void A_slow_fall_s_catch_plays_slower_from_the_commit_EW3()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        var c = paths.Of(LedgeRole.GrabFall)!;
        // Thrown up slowly at a 3 m wall from 0.75 m out (out of the hands' reach until past the arc's top), from the falling grab's contact height: caught
        // falling at about 1 m/s, the clip slower than its own to it.
        var rows = Fall(assets, kind, On, 3f, 0f, vz: 1f, use: true, out_: 0.75f, vx: 1f, air: 0.3f);
        var g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Say("a slow fall", rows, g!.Commit - 2, g.Contact + 3);
        Assert.Equal("grab_fall", g.Clip);
        float fall = -rows[g.Contact - 1].Velocity.Z;
        float mean = (c.ContactS - rows[g.Start].Time) / ((g.Contact - g.Start) * Tick);
        var at = KnucklesAt(rows[g.Contact], 3f);
        output.WriteLine($"falling at {F(fall)} m/s at the contact (the clip's own {F(c.FallSpeed)}): the clip at {F(mean)} of its rate on the whole, from {F(rows[g.Start].Time, "0.000")} s of it; "
                         + $"the knuckles {F(at.X * 100f, "0.0")}, {F(at.Y * 100f, "0.0")} cm; pops pelvis {F(Pop(rows, g.Commit - 2, g.Contact + 60, PelvisOf) * 1000f, "0.0")} mm");
        Assert.True(fall < c.FallSpeed, "slower than the clip's own fall");
        Assert.InRange(mean, 0.2f, 0.95f);
        Assert.InRange(at.X, -0.08f, 0.02f);
        Assert.InRange(at.Y, -0.04f, 0.08f);
    }

    [Fact]
    public void A_short_fall_s_prelanding_by_cutoff_or_part_way_and_its_fade_in_game_time_EY1_EY2()
    {
        var (assets, kind, _) = Player();
        Ledge(assets, kind);
        // A hop down of 0.15 m (put in the air over the floor): cutoff, none (0.17 s of air under 0.2); part way, begun part way in at its own rate.
        foreach (var mode in new[] { ShortFall.Cutoff, ShortFall.Partway, ShortFall.Faster })
        {
            var pre = new PersonPrelandingSettings { Short = mode };
            var (w, e) = Make(assets, kind, On, new Vector3(0f, 0f, 0f), pre);
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero), 80, launch: (new Vector3(0f, 0f, 0.15f), Vector3.Zero, false));
            int p = First(rows, r => r.Clip.StartsWith("prelanding_", StringComparison.Ordinal));
            int land = First(rows, r => r.State == BodyState.Ground);
            output.WriteLine($"{mode}: {(p < 0 ? "no prelanding" : $"{rows[p].Clip} from {F(rows[p].Time, "0.000")} s at {F(rows[p + 1].Rate)}, {land - p} ticks before the contact")}, "
                             + $"landed on tick {land} into {rows[land].Clip} {(rows[land].Previous != "-" ? $"(blended from {rows[land].Previous})" : "(no blend)")}");
            if (mode == ShortFall.Cutoff) Assert.Equal(-1, p);
            else
            {
                Assert.True(p >= 0);
                float lead = 0.3f, left = (land - p) * Tick;
                if (mode == ShortFall.Partway)
                {
                    // At lead_s less the time to the contact, never later than 0.17 s (under 0.13 s left: from there, faster).
                    Assert.InRange(rows[p].Time, MathF.Min(PersonPrelandingSettings.PartwayLatest, lead - left) - Tick - 1e-3f, PersonPrelandingSettings.PartwayLatest + 1e-3f);
                }
                else Assert.InRange(rows[p].Time, 0f, Tick);
            }
        }
        // A fall played faster than its rate (0.45 m) fades in over 0.13 s of game time (of its own time, half that).
        foreach (bool real in new[] { true, false })
        {
            var pre = new PersonPrelandingSettings { Short = ShortFall.Faster, RealFade = real };
            var (w, e) = Make(assets, kind, On, new Vector3(0f, 0f, 0f), pre);
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero), 80, launch: (new Vector3(0f, 0f, 0.45f), Vector3.Zero, false));
            int p = First(rows, r => r.Clip.StartsWith("prelanding_", StringComparison.Ordinal));
            Assert.True(p >= 0);
            int done = First(rows, r => r.Previous == "-" || !r.Clip.StartsWith("prelanding_", StringComparison.Ordinal), p);
            float faded = (done - p) * Tick;
            output.WriteLine($"{(real ? "game time" : "its own time (its own time)")}: the prelanding at {F(rows[p + 1].Rate)}, faded in over {F(faded, "0.000")} s");
            if (real) Assert.InRange(faded, 0.13f - 2f * Tick, 0.13f + 2f * Tick);
        }
    }

    [Fact]
    public void Off_the_switch_the_world_has_no_store_and_the_air_has_no_commit_point()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        var (w, _) = Make(assets, kind, PersonCommitSettings.Off, Vector3.Zero);
        Assert.False(w.TryStore<PersonCommit>(out _));
        Assert.False(w.TryService<PersonCommitSettings>(out _));
        var rows = StandJump(assets, kind, PersonCommitSettings.Off, 2.6f, 0.5f);
        Assert.True(StandIn(rows) >= 0, "the commit point off's stand-in grab from the air");
        Assert.DoesNotContain(rows, r => r.Committed != AirOutcome.None);
        rows = StandJump(assets, kind, On, 2.6f, 0.5f);
        Assert.Equal(-1, StandIn(rows));
        Assert.NotNull(CatchOf(rows, paths));
        // The notes: none or an earlier build is the earlier build's; the numbers through the text.
        Assert.False(new PersonCommitSettings().WithNote(null).On);
        Assert.False(new PersonCommitSettings().WithNote("off").On);
        var noted = new PersonCommitSettings().WithNote("on=1 lead=0.25 auto=0 cone=20 arms=0.1 brace=0.3");
        Assert.Equal((true, 0.25f, false, 20f, 0.1f, 0.3f), (noted.On, noted.LeadSeconds, noted.AutoCatch, noted.AutoConeDegrees, noted.ArmReach, noted.BraceOverlap));
        Assert.Equal(On.Note(), On.WithNote(On.Note()).Note());
        var pre = new PersonPrelandingSettings();
        Assert.Equal((ShortFall.Faster, false), (pre.WithNote("clips=1 from=0.13").Short, pre.WithNote("clips=1 from=0.13").RealFade));   // an older tape
        Assert.Equal(pre.Note(), pre.WithNote(pre.Note()).Note());
        Assert.Equal("clips=1 from=0.13", pre.WithoutCommit.Note());
    }

    // ----------------------------------------------------------------

    static readonly PersonCommitSettings Rules1 = new PersonCommitSettings().AsRules1;

    static string Of(PersonCommitSettings c) => !c.On ? "off" : c.Version >= 2 ? "rules 2" : "rules 1";

    /// <summary>
    /// A run on a top of <paramref name="from"/> (x under 0) toward +X along a way <paramref name="angle"/>° off it, jump pressed as the feet come within
    /// <paramref name="press"/> of its edge; then nothing asked in the air (use held or not); the floor at 0 and <paramref name="blocks"/>.
    /// </summary>
    List<Row> JumpOff(CharacterAssets assets, int kind, PersonCommitSettings commit, float from, float press, float angle, bool use, int ticks,
                      params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var (s, c) = DetMath.SinCos(angle * DetMath.DegToRad);
        var way = new Vector2(c, s);
        float back = 5f;
        var (w, e) = Make(assets, kind, commit, new Vector3(-press - back * c, -back * s, from), blocks: [(new Vector3(-20f, -20f, 0f), new Vector3(0f, 20f, from)), .. blocks]);
        w.Get<Body>(e).Yaw = DetMath.Atan2(s, c);
        bool pressed = false;
        return Drive(w, e, assets, kind, (i, b) =>
        {
            bool now = !pressed && b.State == BodyState.Ground && b.Feet.X >= -press;
            pressed |= now;
            return new Ask(b.State == BodyState.Ground ? way : Vector2.Zero, Gait.Run, Jump: now, Use: use);
        }, ticks);
    }

    [Fact]
    public void A_jump_off_a_top_catches_a_lower_lip_on_the_way_down()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // Off a 2.8 m top, a 2.2 m wall 3 m across (its face toward the jump): the jump falls short of it and is caught on the way down, by itself (it was
        // jumped at) and with use held; the earlier build's caught nothing under its take-off.
        (Vector3, Vector3) lower = (new Vector3(3f, -20f, 0f), new Vector3(6f, 20f, 2.2f));
        foreach (var (commit, name) in new[] { (On, "rules 2"), (Rules1, "rules 1") })
        {
            var rows = JumpOff(assets, kind, commit, 2.8f, 0.3f, 0f, use: true, ticks: 600, lower);
            var g = CatchOf(rows, paths);
            int c = First(rows, r => r.Committed != AirOutcome.None && r.State != BodyState.Ground);
            output.WriteLine($"{name}: off 2.8 m at a 2.2 m lip 3 m across, use held: {(g is null ? "nothing caught" : $"{g.Clip} ({(g.Auto ? "jumped at it" : "use held")}), {g.Hang}")}; "
                             + $"commits {string.Join(", ", rows.Where(r => r.Committed != AirOutcome.None).Select(r => r.Committed))}");
            if (commit.Version >= 2)
            {
                Assert.NotNull(g);
                if (!rows[^1].Ledge.StartsWith("hang_", StringComparison.Ordinal)) Say("after the catch", rows, g!.Contact - 5, g.Contact + 40);
                Assert.Equal("grab_fall", g!.Clip);
                Assert.StartsWith("hang_", rows[^1].Ledge);
                Assert.Equal(-1, StandIn(rows));
            }
            else Assert.Null(g);
        }
        // Along a 0.8 m gap at 75° to the lower wall's face (beyond the cone): past it without use, caught with it.
        (Vector3, Vector3) beside = (new Vector3(0.8f, -20f, 0f), new Vector3(4f, 20f, 2.2f));
        var past = JumpOff(assets, kind, On, 2.8f, 0.3f, 75f, use: false, ticks: 600, beside);
        Assert.Null(CatchOf(past, paths));
        Assert.Contains(past, r => r.Committed == AirOutcome.Past && r.Why == CatchRefused.UseNotHeld);
        var held = JumpOff(assets, kind, On, 2.8f, 0.3f, 75f, use: true, ticks: 600, beside);
        var gh = CatchOf(held, paths);
        Assert.NotNull(gh);
        Assert.False(gh!.Auto);
        output.WriteLine($"at 75° along a 0.8 m gap: without use past ({past.First(r => r.Committed == AirOutcome.Past).Why}), with use {gh.Clip}, {gh.Hang}");
    }

    [Fact]
    public void A_jump_across_a_gap_that_falls_short_is_caught_on_the_far_lip()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // A 3 m platform, a 3 m gap, a 3 m platform (the gaps station's 1 m ones are too low to hang from: the hang's feet would be under the floor).
        (Vector3, Vector3) far = (new Vector3(3f, -20f, 0f), new Vector3(8f, 20f, 3f));
        var rows = JumpOff(assets, kind, On, 3f, 0.3f, 0f, use: false, ticks: 600, far);
        var g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Assert.True(g!.Auto, "jumped at it");
        var at = rows[g.Contact];
        output.WriteLine($"a 3 m gap jumped short: {g.Clip} by itself, the knuckles {F((0.5f * (at.KnuckleL.X + at.KnuckleR.X) - 3f) * 100f, "0.0")} cm, {F((0.5f * (at.KnuckleL.Z + at.KnuckleR.Z) - 3f) * 100f, "0.0")} cm; {g.Hang}");
        // At 75° (not at it), use held.
        rows = JumpOff(assets, kind, On, 3f, 0.3f, 75f, use: true, ticks: 600, (new Vector3(0.8f, -20f, 0f), new Vector3(8f, 20f, 3f)));
        g = CatchOf(rows, paths);
        Assert.NotNull(g);
        Assert.False(g!.Auto);
    }

    [Fact]
    public void The_jump_s_own_moves_keep_the_tops_above_its_take_off_within_the_band()
    {
        var (assets, kind, _) = Player();
        // A running jump at a 1.5 m block (the mantle's band) and a 0.9 m one (the climb's): their moves, no catch.
        foreach (float h in new[] { 0.9f, 1.5f })
        {
            var (w, e) = Make(assets, kind, On, new Vector3(-6f, 0f, 0f), blocks: [Wall(h)]);
            bool pressed = false;
            var rows = Drive(w, e, assets, kind, (i, b) =>
            {
                bool now = !pressed && b.State == BodyState.Ground && 1f - b.Feet.X <= 1.2f;
                pressed |= now;
                return new Ask(b.State is BodyState.Ground or BodyState.Air ? new Vector2(1f, 0f) : Vector2.Zero, Gait.Run, Jump: now);
            }, 400);
            var moves = rows.Select(r => r.Move).Where(m => m != TraversalKind.None).Distinct().ToList();
            output.WriteLine($"{F(h)} m: {string.Join(", ", moves)}");
            Assert.DoesNotContain(rows, r => r.Committed == AirOutcome.Catch);
            Assert.Contains(h > 1.2f ? TraversalKind.Mantle : TraversalKind.ClimbOnto, moves);
        }
    }

    [Fact]
    public void The_cone_is_60_degrees_each_side_of_square()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        foreach (float angle in new[] { 0f, 45f, 59f, 65f })
        {
            var rows = RunJump(assets, kind, On, 2.2f, 1.0f, angle: angle);
            var g = CatchOf(rows, paths);
            output.WriteLine($"{angle}°: {(g is null ? $"not caught ({string.Join(", ", rows.Where(r => r.Committed != AirOutcome.None).Select(r => $"{r.Committed} {r.Why}"))})" : $"{g.Clip} by itself: {g.Auto}")}");
            if (angle < 60f) { Assert.NotNull(g); Assert.True(g!.Auto); }
            else Assert.Null(g);
        }
        // Running along the wall 0.5 m out (parallel), jumping: nothing caught by itself.
        var (w, e) = Make(assets, kind, On, new Vector3(0.5f, -6f, 0f), blocks: [Wall(2.2f, -12f, 12f)]);
        w.Get<Body>(e).Yaw = MathF.PI / 2f;
        var along = Drive(w, e, assets, kind, (i, b) => new Ask(b.State is BodyState.Ground or BodyState.Air ? new Vector2(0f, 1f) : Vector2.Zero, Gait.Run, Jump: i == 90), 300);
        Assert.Null(CatchOf(along, paths));
        output.WriteLine($"parallel: {string.Join(", ", along.Where(r => r.Committed != AirOutcome.None).Select(r => $"{r.Committed} {r.Why}"))}");
    }

    [Fact]
    public void A_catch_committed_at_a_block_s_corner_is_made_or_refused_and_told_never_turned_into_a_landing()
    {
        var (assets, kind, _) = Player();
        _ = Ledge(assets, kind);   // skips without the ledge's clips
        // A play log at 43.02 s: running south off the 2.8 m block's north-east corner (0.1 m east of the east face, 0.8 m north of the north face),
        // a tap jump; an earlier build committed a catch on the probe's corner normal, the body glanced off the corner, the catch's re-look at its start found
        // nothing and it landed, saying nothing. An earlier build squares the lip onto the face the arc goes into, decides the catch whole at its commit and keeps
        // that plan: the catch is made, or refused there and told why.
        (Vector3, Vector3) block = (new Vector3(1f, -1f, 0f), new Vector3(3f, 1f, 2.8f));
        List<Row> Jump(PersonCommitSettings commit, float x, float speed, float angle)
        {
            var way = new Vector2(DetMath.Sin(angle * DetMath.DegToRad), -DetMath.Cos(angle * DetMath.DegToRad));
            var (w, e) = Make(assets, kind, commit, new Vector3(x, 1.8f, 0f), blocks: [block]);
            w.Get<Body>(e).Yaw = DetMath.Atan2(way.Y, way.X);
            return Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero), 200, launch: (new Vector3(x, 1.8f, 0.01f), new Vector3(way * speed, 3.13f), true));
        }
        static string Said(List<Row> rows) =>
            string.Join(", ", rows.Where(r => r.Committed != AirOutcome.None).Select(r => $"{r.Committed}{(r.Why != CatchRefused.None ? $" ({r.Why})" : "")}"))
            + (rows.Any(r => r.Move == TraversalKind.Grab) ? "; grabbed" : "; not grabbed");
        static bool Lapsed(List<Row> rows) => rows.Any(r => r.Committed == AirOutcome.Catch) && !rows.Any(r => r.Move == TraversalKind.Grab);

        // His case: an earlier build lapses, an earlier build catches.
        var cy = Jump(Rules1, 3.1f, 5f, 0f);
        var cz = Jump(On, 3.1f, 5f, 0f);
        output.WriteLine($"his 43.02 s (0.1 m past the east face, 5 m/s south): rules 1 {Said(cy)}; rules 2 {Said(cz)}");
        Assert.True(Lapsed(cy), "rules 1's lapse reproduced");
        Assert.Contains(cz, r => r.Committed == AirOutcome.Catch);
        Assert.Contains(cz, r => r.Move == TraversalKind.Grab);
        // Further past the end than a hang slides: refused at the commit and told.
        var off = Jump(On, 3.2f, 5f, 0f);
        output.WriteLine($"0.2 m past it: rules 2 {Said(off)}");
        Assert.Contains(off, r => r.Committed == AirOutcome.Past && r.Why == CatchRefused.LipEnds);
        Assert.DoesNotContain(off, r => r.Committed == AirOutcome.Catch);
        // About the corner: no catch of the earlier build's committed to and then landed (the earlier build's lapses counted).
        int lapsesCy = 0, caughtCy = 0, caughtCz = 0, runs = 0;
        foreach (var x in new[] { 2.9f, 3.0f, 3.1f, 3.2f })
            foreach (var speed in new[] { 1.5f, 3.5f, 5f })
                foreach (var angle in new[] { 0f, 10f, 25f })
                {
                    var a = Jump(Rules1, x, speed, angle);
                    var b = Jump(On, x, speed, angle);
                    runs++;
                    if (Lapsed(a)) lapsesCy++;
                    if (a.Any(r => r.Move == TraversalKind.Grab)) caughtCy++;
                    if (b.Any(r => r.Move == TraversalKind.Grab)) caughtCz++;
                    Assert.False(Lapsed(b), $"rules 2 at x {x} {speed} m/s {angle}°: {Said(b)}");
                    Assert.True(!a.Any(r => r.Move == TraversalKind.Grab) || b.Any(r => r.Move == TraversalKind.Grab), $"rules 2 catches what rules 1 did: x {x} {speed} m/s {angle}°");
                }
        output.WriteLine($"about the corner, {runs} jumps: rules 1 caught {caughtCy}, committed and landed {lapsesCy}; rules 2 caught {caughtCz}, committed and landed 0");
    }

    [Fact]
    public void A_jump_left_1_6_m_out_meets_the_2_8_m_wall_past_its_top_and_is_caught_at_full_reach()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // A play log at 17.78 s: a tap at a run left the ground 1.59 m out of the 2.8 m face at about 3 m/s, met the wall past the arc's top with the hands
        // under the lip by the clips' own contact, and landed. An earlier build catches it on grab_fall at the hands' full reach, forward held or released.
        foreach (var (commit, name) in new[] { (On, "rules 2"), (Rules1, "rules 1") })
            foreach (float out_ in new[] { 1.6f, 1.75f })
                foreach (bool held in new[] { false, true })
                {
                    var (w, e) = Make(assets, kind, commit, new Vector3(1f - out_, 0f, 0f), blocks: [Wall(2.8f)]);
                    var rows = Drive(w, e, assets, kind, (i, b) => held ? new Ask(new Vector2(1f, 0f), Gait.Run) : new Ask(Vector2.Zero), 360,
                                     launch: (new Vector3(1f - out_, 0f, 0.01f), new Vector3(3f, 0f, 3.13f), true));
                    var g = CatchOf(rows, paths);
                    output.WriteLine($"{name}: left {F(out_)} m out, forward {(held ? "held" : "released")}: {(g is null ? "not caught" : $"{g.Clip}, {g.Hang}")}; " +
                                     string.Join(", ", rows.Where(r => r.Committed != AirOutcome.None).Select(r => $"{r.Committed}{(r.Why != CatchRefused.None ? $" ({r.Why})" : "")}")));
                    if (commit.Version >= 2)
                    {
                        Assert.NotNull(g);
                        Assert.Equal("grab_fall", g!.Clip);
                        Assert.Equal(-1, StandIn(rows));
                    }
                    else Assert.Null(g);
                }
    }

    /// <summary>The numbers for the report: an earlier build beside two others, the largest pelvis and hand pop a tick from the commit (the same moment) through the contact into the hang.</summary>
    [Fact]
    public void Measured()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        var cases = new (string Name, Func<PersonCommitSettings, List<Row>> Run, float H)[]
        {
            ("a running jump at 2.2 m pressed 1.35 m short (rising)", c => RunJump(assets, kind, c, 2.2f, 1.35f), 2.2f),
            ("a running jump at 2.2 m pressed 1.6 m short (falling at the wall)", c => RunJump(assets, kind, c, 2.2f, 1.6f), 2.2f),
            ("a running jump at 2.8 m pressed 1.35 m short", c => RunJump(assets, kind, c, 2.8f, 1.35f), 2.8f),
            ("a standing jump at 2.6 m", c => StandJump(assets, kind, c, 2.6f, 0.5f), 2.6f),
            ("a standing jump at 2.8 m", c => StandJump(assets, kind, c, 2.8f, 0.5f), 2.8f),
            ("falling past 3 m, use held (off: jump held)", c => c.On ? Fall(assets, kind, c, 3f, 1f, use: true)
                : Drive(Make(assets, kind, c, new Vector3(0.55f, 0f, 0f), blocks: [Wall(3f)]) is var m ? m.World : null!, m.Body, assets, kind, (i, b) => new Ask(Vector2.Zero, Held: true), 360,
                        launch: (new Vector3(0.55f, 0f, 3f - (paths.Of(LedgeRole.GrabFall)!.EdgeUp - paths.Of(LedgeRole.GrabFall)!.At(paths.Of(LedgeRole.GrabFall)!.ContactS).Up) + 1f), Vector3.Zero, false)), 3f),
        };
        foreach (var (name, run, h) in cases)
            foreach (var commit in new[] { On, Rules1, PersonCommitSettings.Off })
            {
                var rows = run(commit);
                int start = commit.On ? First(rows, r => r.Committed == AirOutcome.Catch) : First(rows, r => r.Move == TraversalKind.Grab);
                if (start < 0) { output.WriteLine($"{name}, {Of(commit)}: nothing caught"); continue; }
                if (!commit.On) start = Math.Max(2, start - (int)MathF.Round(On.LeadSeconds / Tick));
                int hang = First(rows, r => r.Move == TraversalKind.Hang, start);
                int to = (hang > 0 ? hang : start + 120) + 30;
                int contact = commit.On ? CatchOf(rows, paths)!.Contact : First(rows, r => r.Move == TraversalKind.Hang, start);
                var k = contact >= 0 ? KnucklesAt(rows[contact], h) : default;
                int mid = contact > start ? contact : start + 36;
                output.WriteLine($"  the hands' largest pop at tick {ArgPop(rows, start, to, HandsOf) - start} from the commit ({rows[ArgPop(rows, start, to, HandsOf)].Clip} <{rows[ArgPop(rows, start, to, HandsOf)].Previous} {F(rows[ArgPop(rows, start, to, HandsOf)].Blend)})");
                var caught = commit.On ? CatchOf(rows, paths) : null;
                if (caught is not null) output.WriteLine($"  {caught.Clip}, committed {F(caught.Lead, "0.000")} s before the contact, begun {caught.Start - caught.Commit} ticks after the commit; then {caught.Hang}");
                output.WriteLine($"{name}, {Of(commit)}: pelvis pop {F(Pop(rows, start, to, PelvisOf) * 1000f, "0.0")} mm (at tick {ArgPop(rows, start, to, PelvisOf) - start} from the commit; "
                                 + $"to the contact {F(Pop(rows, start, mid + 1, PelvisOf) * 1000f, "0.0")}, after {F(Pop(rows, mid + 1, to, PelvisOf) * 1000f, "0.0")}), "
                                 + $"hands {F(Pop(rows, start, to, HandsOf) * 1000f, "0.0")} mm a tick² (to the contact {F(Pop(rows, start, mid + 1, HandsOf) * 1000f, "0.0")}, after {F(Pop(rows, mid + 1, to, HandsOf) * 1000f, "0.0")}), "
                                 + $"the knuckles at the contact {F(k.X * 100f, "0.0")}, {F(k.Y * 100f, "0.0")} cm; {(hang > 0 ? rows[hang].Ledge : "-")}");
            }
    }
}
