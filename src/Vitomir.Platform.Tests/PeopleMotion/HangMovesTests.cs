// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>the hang's corners and leaps</b> — a shimmy into a corner of the lip goes round it
/// (outside, inside; braced, free) where it can be taken, else stops and says why; from the braced hang the leaps to a side, up, down and back to a facing
/// ledge where their lips are; the capsule on the clips' paths, its facing carrying the turn; the earlier build's behind the switch. Skips without the models repository's
/// files, or.
/// </summary>
public sealed class HangMovesTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    internal static readonly PersonHangSettings Leaping = new();

    internal static LedgePaths Paths(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge?.Paths.HasHangMoves == true, "the models repository's files are too old (no hang leaps or corners)");
        return person.Ledge!.Paths;
    }

    /// <summary>A world as the session's for the player (the commit point, tier 1's moves, the hang's: <paramref name="hang"/>, null for the earlier build's), the body at <paramref name="at"/> facing +X, the floor and <paramref name="blocks"/>.</summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonHangSettings? hang, Vector3 at, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: new PersonPrelandingSettings(), commit: new PersonCommitSettings(), moves: new PersonMovesSettings(), hang: hang);
        b.Service(new PersonHandsSettings());
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        if (w.TryStore<PersonLedge>(out _)) w.Set(e, new PersonLedge());
        if (w.TryStore<PersonCommit>(out _)) w.Set(e, new PersonCommit());
        if (w.TryStore<PersonLadder>(out _)) w.Set(e, new PersonLadder());
        if (w.TryStore<PersonHang>(out _)) w.Set(e, new PersonHang());
        return (w, e);
    }

    /// <summary>What is asked on a tick: the wish (x ahead, y to the left of +X), jump pressed, crouch held, use held.</summary>
    internal readonly record struct Ask(Vector2 Wish, bool Jump = false, bool Crouch = false, bool Use = false);

    /// <summary>A tick as it went: the ledge's clip and its time, the hang's move, the body, as drawn the pelvis, knuckles and wrists; what began or was refused.</summary>
    internal sealed record Row(int I, string Ledge, float LedgeTime, HangMoveKind Kind, TraversalKind Move, BodyState State, Vector3 Feet, float Yaw, Vector3 Pelvis,
                               Vector3 KnuckleL, Vector3 KnuckleR, Vector3 WristL, Vector3 WristR, string Clip, string Began, string Refused);

    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks, int settle = 20, Vector3? launch = null)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var paths = assets.Kind(kind).Person!.Ledge?.Paths;
        for (int i = 0; i < settle; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        if (launch is { } at)
        {
            // Put in the air at rest (a fall from there: use held catches the lip it passes).
            ref var body = ref w.Get<Body>(e);
            body.Feet = at;
            body.Velocity = Vector3.Zero;
            body.State = BodyState.Air;
            body.Jumped = 0; body.Rising = 0; body.TakeOffZ = at.Z; body.HighestZ = at.Z; body.AirSeconds = 0.3f; body.Coyote = 0f;
            body.GroundTriangle = -1; body.GroundNormal = Vector3.UnitZ;
        }
        for (int i = 0; i < ticks; i++)
        {
            Set(w, e, plan(i, w.Get<Body>(e)));
            w.Step();
            var body = w.Get<Body>(e);
            var ledge = w.TryStore<PersonLedge>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            var hang = w.TryStore<PersonHang>(out var hs) && hs.Has(e) ? hs.Get(e).Move : default;
            string clip = ledge.Clip > 0 && paths?.At(ledge.Clip - 1) is { } c ? c.Name : "-";
            string began = "", refused = "";
            foreach (var x in w.Events<HangMoveStarted>().ThisTick) if (x.Entity == e) began = $"{x.Kind} {x.Leap} {(x.Outside ? "out" : "in")} {x.Way}";
            foreach (var x in w.Events<HangMoveRefused>().ThisTick) if (x.Entity == e) refused = $"{x.Kind} {x.Leap} {(x.Outside ? "out" : "in")} {x.Way} {x.Why}";
            foreach (var x in w.Events<AirCommitted>().ThisTick) if (x.Entity == e) refused = $"commit {x.Outcome} {x.Why} at {F(x.Seconds, "0.00")}";
            var a = w.Get<CharacterAnimation>(e);
            rows.Add(new Row(i, clip, ledge.Time, hang.Kind, w.Get<Traversal>(e).Kind, body.State, body.Feet, body.Yaw, Bone(w, e, assets, rig.Pelvis),
                             Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip), Bone(w, e, assets, rig.LeftArm.End),
                             Bone(w, e, assets, rig.RightArm.End), assets.Clip(a.Clip).Name, began, refused));
        }
        return rows;
    }

    static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = Gait.Walk;
        intent.Faces = false;
        intent.Jump = ask.Jump;
        intent.JumpHeld = ask.Jump;
        intent.Crouch = ask.Crouch;
        intent.UseHeld = ask.Use;
    }

    void Say(List<Row> rows, int every = 6)
    {
        string last = "";
        foreach (var r in rows)
        {
            bool show = r.Ledge != last || r.Began != "" || r.Refused != "" || r.I % every == 0;
            last = r.Ledge;
            if (!show) continue;
            output.WriteLine($"{r.I,4} {r.Ledge,-30} {F(r.LedgeTime, "0.000")} {r.Kind,-6} {r.Move,-6} {r.State,-10} feet {F(r.Feet.X, "0.000")},{F(r.Feet.Y, "0.000")},{F(r.Feet.Z, "0.000")} "
                             + $"yaw {F(r.Yaw * 180f / MathF.PI, "0.0")} pelvis {F(r.Pelvis.X, "0.000")},{F(r.Pelvis.Y, "0.000")},{F(r.Pelvis.Z, "0.000")} {r.Clip} {r.Began} {r.Refused}");
        }
    }

    /// <summary>The man's grab's own edge height (the standing grab's: from the ground at 0.55 m out of a face at x 1).</summary>
    static float GrabHeight(LedgePaths paths) => MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);

    /// <summary>The ledge clips in order (each change), the hang moves begun and refused.</summary>
    static string Story(List<Row> rows)
    {
        var all = new List<string>();
        foreach (var r in rows)
        {
            if (r.Ledge != "-" && (all.Count == 0 || all.LastOrDefault(x => !x.StartsWith('<')) != r.Ledge)) all.Add(r.Ledge);
            if (r.Began != "") all.Add($"[{r.Began}]");
            if (r.Refused != "" && !all.Contains($"<{r.Refused}>")) all.Add($"<{r.Refused}>");
        }
        var end = rows[^1];
        return string.Join(", ", all) + $" | end {end.State} feet {F(end.Feet.X, "0.000")},{F(end.Feet.Y, "0.000")},{F(end.Feet.Z, "0.000")} yaw {F(end.Yaw * 180f / MathF.PI, "0.0")}";
    }

    // ---------------------------------------------------------------- the scenes

    /// <summary>A scene: its blocks, where the body stands (or falls from, use held), and how it is driven.</summary>
    internal sealed record Scene(string Name, (Vector3 Min, Vector3 Max)[] Blocks, Func<int, Body, Ask> Plan, int Ticks, Vector3? Launch = null);

    static Ask Grab(int i, Vector2 then, int from = 200) => new(i < from ? Vector2.Zero : then, Jump: i == 0);

    /// <summary>The corners' scenes (the man's grab height <paramref name="h"/>, the face at x 1 facing −X): a braced wall's outside and inside corners each way, a free slab's.</summary>
    internal static Scene[] Corners(float h) =>
    [
        new("out braced left", [(new Vector3(1f, -3f, 0f), new Vector3(3f, 1.5f, h))], (i, b) => Grab(i, new Vector2(0f, 1f)), 1100),
        new("out braced right", [(new Vector3(1f, -1.5f, 0f), new Vector3(3f, 3f, h))], (i, b) => Grab(i, new Vector2(0f, -1f)), 1100),
        new("in braced left", [(new Vector3(1f, -3f, 0f), new Vector3(3f, 2f, h)), (new Vector3(-1f, 2f, 0f), new Vector3(3f, 4f, h))], (i, b) => Grab(i, new Vector2(0f, 1f)), 1250),
        new("in braced right", [(new Vector3(1f, -2f, 0f), new Vector3(3f, 3f, h)), (new Vector3(-1f, -4f, 0f), new Vector3(3f, -2f, h))], (i, b) => Grab(i, new Vector2(0f, -1f)), 1250),
        new("out free left", [(new Vector3(1f, -3f, h - 0.2f), new Vector3(3f, 1.5f, h))], (i, b) => Grab(i, new Vector2(0f, 1f)), 1100),
        new("out free right", [(new Vector3(1f, -1.5f, h - 0.2f), new Vector3(3f, 3f, h))], (i, b) => Grab(i, new Vector2(0f, -1f)), 1100),
        new("in free left", [(new Vector3(1f, -3f, h - 0.2f), new Vector3(3f, 2f, h)), (new Vector3(-1f, 2f, h - 0.2f), new Vector3(3f, 4f, h))], (i, b) => Grab(i, new Vector2(0f, 1f)), 1250),
        new("in free right", [(new Vector3(1f, -2f, h - 0.2f), new Vector3(3f, 3f, h)), (new Vector3(-1f, -4f, h - 0.2f), new Vector3(3f, -2f, h))], (i, b) => Grab(i, new Vector2(0f, -1f)), 1250),
    ];

    /// <summary>
    /// The leaps' columns: the one grabbed (y −0.6 to 0.6, top <paramref name="h"/>), one 1.2 m to each side (0.3 m gaps), and over the first one a lip 1.0 m up in
    /// its face, a groove 0.6 m high and 0.15 m deep between (no top to stand on; the upper hang's feet on the face or the groove's back; over the probe's look from the ground).
    /// </summary>
    internal static (Vector3, Vector3)[] Columns(float h) =>
        [(new Vector3(1f, -0.6f, 0f), new Vector3(3f, 0.6f, h)), (new Vector3(1f, 0.9f, 0f), new Vector3(3f, 2.1f, h)),
         (new Vector3(1f, -2.1f, 0f), new Vector3(3f, -0.9f, h)), (new Vector3(1.15f, -0.6f, h), new Vector3(3f, 0.6f, h + 0.6f)),
         (new Vector3(1f, -0.6f, h + 0.6f), new Vector3(3f, 0.6f, h + 1.0f))];

    /// <summary>An upper lip <paramref name="up"/> over a lower one <paramref name="drop"/> m below, the upper face 8 cm behind the lower's (in its plane within the lips' 0.1 m).</summary>
    internal static (Vector3, Vector3)[] Stepped(float up, float drop) =>
        [(new Vector3(1f, -1f, 0f), new Vector3(3f, 1f, up - drop)), (new Vector3(1.08f, -1f, up - drop), new Vector3(3f, 1f, up))];

    /// <summary>An upper lip <paramref name="up"/> over a lower one <paramref name="drop"/> m below in the same face, a groove 0.4 m high and 0.15 m deep over the lower one (the upper hang's feet on its back).</summary>
    internal static (Vector3, Vector3)[] Grooved(float up, float drop) =>
        [(new Vector3(1f, -1f, 0f), new Vector3(3f, 1f, up - drop)), (new Vector3(1.15f, -1f, up - drop), new Vector3(3f, 1f, up - drop + 0.4f)),
         (new Vector3(1f, -1f, up - drop + 0.4f), new Vector3(3f, 1f, up))];

    static Ask Press(int i, Vector2 wish, int at = 250) => i == 0 ? new Ask(Vector2.Zero, Jump: true) : i == at ? new Ask(wish, Jump: true) : new Ask(Vector2.Zero);

    /// <summary>The leaps' scenes: each from the braced hang grabbed (or caught falling, use held) by its control.</summary>
    internal static Scene[] Leaps(float h)
    {
        float up = h + 1.2f;
        var fall = new Vector3(0.6f, 0f, up - 1.85f + 1.0f);
        return
        [
            new("leap left", Columns(h), (i, b) => Press(i, new Vector2(0f, 1f)), 600),
            new("leap right", Columns(h), (i, b) => Press(i, new Vector2(0f, -1f)), 600),
            new("leap up", Columns(h), (i, b) => Press(i, Vector2.Zero), 600),
            new("leap down", Grooved(up, 1.2f), (i, b) => new Ask(Vector2.Zero, Crouch: i >= 300 && i < 320, Use: i < 320), 700, fall),
            new("leap back", [(new Vector3(1f, -3f, 0f), new Vector3(3f, 3f, h)), (new Vector3(-3f, -3f, 0f), new Vector3(-1f, 3f, h))], (i, b) => Press(i, new Vector2(-1f, 0f)), 650),
        ];
    }

    /// <summary>What a corner or a leap did: its clip, the hang it ends in, the knuckles there and at the leading hand's contact, the pops, the facing after it.</summary>
    internal sealed record Taken(string Clip, int Start, int End, Vector3 EndFeet, float EndYaw, Vector2 Knuckles, Vector2 ContactKnuckle, float Pelvis, float Hands, float PelvisAt, float HandsAt)
    {
        /// <summary>The pelvis's pop at the start's seam and at the end's (2 ticks either side), and the hands' there.</summary>
        public float PelvisIn, PelvisOut, HandsIn, HandsOut;
    }

    /// <summary>
    /// The first leap or corner in <paramref name="rows"/>: from where it begins to 0.5 s into the hang after it, the pelvis's and the hands' pops (the
    /// largest change a tick of the change a tick, as drawn), the knuckles (both hands' mean) on the lip it ends at — forward of its face (into the wall
    /// positive) and above its top — at its end, and the leading hand's at its <c>contact_s</c>.
    /// </summary>
    internal static Taken? TakenOf(List<Row> rows, LedgePaths paths, Vector3 lip, Vector2 n)
    {
        int start = rows.FindIndex(r => paths.Clips.FirstOrDefault(c => c.Name == r.Ledge) is { Role: LedgeRole.Leap or LedgeRole.Corner });
        if (start < 0) return null;
        var c = paths.Clips.First(x => x.Name == rows[start].Ledge);
        int end = rows.FindIndex(start, r => r.Ledge != c.Name);
        if (end < 0) return null;
        int to = Math.Min(rows.Count, end + (int)(0.5f / Tick));
        float pelvis = AirCommitTests.Pop(Adapt(rows), start - 2, to, AirCommitTests.PelvisOf), hands = AirCommitTests.Pop(Adapt(rows), start - 2, to, AirCommitTests.HandsOf);
        var e = rows[end];
        Vector2 On(Vector3 k) => new(Vector2.Dot(new Vector2(lip.X - k.X, lip.Y - k.Y), n), k.Z - lip.Z);
        var knuckles = On(0.5f * (e.KnuckleL + e.KnuckleR));
        var contact = Vector2.Zero;
        if (!float.IsNaN(c.ContactS))
        {
            int at = rows.FindIndex(start, r => r.Ledge == c.Name && r.LedgeTime >= c.ContactS - 1e-4f);
            if (at > 0)
            {
                var r = rows[at];
                // The leading hand: the one on the lip (the nearer to its edge, across the face and up).
                var cl = On(r.KnuckleL);
                var cr = On(r.KnuckleR);
                contact = cl.Length() < cr.Length() ? cl : cr;
            }
        }
        var ad = Adapt(rows);
        return new Taken(c.Name, start, end, e.Feet, e.Yaw, knuckles, contact, pelvis, hands,
                         AirCommitTests.ArgPop(ad, start - 2, to, AirCommitTests.PelvisOf) - start, AirCommitTests.ArgPop(ad, start - 2, to, AirCommitTests.HandsOf) - start)
        {
            PelvisIn = AirCommitTests.Pop(ad, start - 2, start + 3, AirCommitTests.PelvisOf), PelvisOut = AirCommitTests.Pop(ad, end - 2, end + 3, AirCommitTests.PelvisOf),
            HandsIn = AirCommitTests.Pop(ad, start - 2, start + 3, AirCommitTests.HandsOf), HandsOut = AirCommitTests.Pop(ad, end - 2, end + 3, AirCommitTests.HandsOf),
        };
    }

    /// <summary>The rows as the commit tests' (for their pop measures).</summary>
    static List<AirCommitTests.Row> Adapt(List<Row> rows) =>
        [.. rows.Select(r => new AirCommitTests.Row(r.Clip, 0f, 0f, "-", 1f, r.Move, r.State, r.Feet, Vector3.Zero, r.Ledge, AirOutcome.None, 0f, false, r.Pelvis,
                                                     r.KnuckleL, r.KnuckleR, r.WristL, r.WristR, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0))];

    internal static List<Row> Play(CharacterAssets assets, int kind, PersonHangSettings? hang, Scene scene)
    {
        var (w, e) = Make(assets, kind, hang, new Vector3(0.55f, 0f, 0f), scene.Blocks);
        return Drive(w, e, assets, kind, scene.Plan, scene.Ticks, launch: scene.Launch);
    }

    /// <summary>Where each scene's move should end: the lip (its point on the face at its top, the hang's middle) and the face's outward normal, and the facing after it (degrees).</summary>
    static (Vector3 Lip, Vector2 N, float Yaw) Expect(string name, float h) => name switch
    {
        "out braced left" or "out free left" => (new Vector3(1.4f, 1.5f, h), new Vector2(0f, 1f), -90f),
        "out braced right" or "out free right" => (new Vector3(1.4f, -1.5f, h), new Vector2(0f, -1f), 90f),
        "in braced left" or "in free left" => (new Vector3(0.5f, 2f, h), new Vector2(0f, -1f), 90f),
        "in braced right" or "in free right" => (new Vector3(0.5f, -2f, h), new Vector2(0f, 1f), -90f),
        "leap left" => (new Vector3(1f, 1.2f, h), new Vector2(-1f, 0f), 0f),
        "leap right" => (new Vector3(1f, -1.2f, h), new Vector2(-1f, 0f), 0f),
        "leap up" => (new Vector3(1f, 0f, h + 1f), new Vector2(-1f, 0f), 0f),
        "leap down" => (new Vector3(1f, 0f, h), new Vector2(-1f, 0f), 0f),
        "leap back" => (new Vector3(-1f, 0f, h), new Vector2(1f, 0f), 180f),
        _ => throw new ArgumentException(name),
    };

    /// <summary>Plays a scene and measures its move (asserting it was taken, ended on its hang with the facing it should), said in the output.</summary>
    Taken Measure(CharacterAssets assets, int kind, LedgePaths paths, string body, Scene scene, float h, string expectClip)
    {
        var rows = Play(assets, kind, Leaping, scene);
        var (lip, n, yaw) = Expect(scene.Name, h);
        var m = TakenOf(rows, paths, lip, n);
        Assert.True(m is not null, $"{body} {scene.Name}: no leap or corner ({Story(rows)})");
        var t = m!;
        float yawOff = MathF.Abs(DetMath.WrapAngle(t.EndYaw - yaw * DetMath.DegToRad)) * 180f / MathF.PI;
        output.WriteLine($"{body}, {scene.Name}: {t.Clip} {F((t.End - t.Start) * Tick, "0.000")} s; the facing after it {F(t.EndYaw * 180f / MathF.PI, "0.0")}° ({F(yawOff, "0.00")}° off the lip's); "
                         + $"the knuckles at its end {F(t.Knuckles.X * 100f, "0.0")} cm into the face, {F(t.Knuckles.Y * 100f, "0.0")} cm over the top"
                         + (t.ContactKnuckle != Vector2.Zero ? $", the leading hand's at the contact {F(t.ContactKnuckle.X * 100f, "0.0")} and {F(t.ContactKnuckle.Y * 100f, "0.0")} cm" : "")
                         + $"; the pelvis's pop {F(t.Pelvis * 1000f, "0.0")} mm (tick {t.PelvisAt}; in {F(t.PelvisIn * 1000f, "0.0")}, out {F(t.PelvisOut * 1000f, "0.0")}), the hands' {F(t.Hands * 1000f, "0.0")} mm (tick {t.HandsAt}; in {F(t.HandsIn * 1000f, "0.0")}, out {F(t.HandsOut * 1000f, "0.0")}); {Story(rows)}");
        Assert.Equal(expectClip, t.Clip);
        Assert.InRange(yawOff, 0f, 0.5f);
        return t;
    }

    [Fact]
    public void A_shimmy_into_a_corner_goes_round_it_outside_and_inside_braced_and_free()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Paths(assets, kind);
            float h = GrabHeight(paths);
            foreach (var scene in Corners(h))
            {
                var parts = scene.Name.Split(' ');
                var t = Measure(assets, kind, paths, body, scene, h, $"hang_corner_{parts[0]}_{parts[1]}_{parts[2]}");
                // The knuckles on the new lip as the hang's own (the free hang's 3.7 and 1.8 cm, the notes'; within a couple of centimetres).
                Assert.InRange(t.Knuckles.X, -0.08f, 0.0f);
                Assert.InRange(t.Knuckles.Y, -0.01f, 0.05f);
                Assert.InRange(t.Pelvis, 0f, 0.03f);
            }
        }
    }

    [Fact]
    public void From_the_braced_hang_the_leaps_to_a_side_up_down_and_back()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Paths(assets, kind);
            float h = GrabHeight(paths);
            foreach (var scene in Leaps(h))
            {
                var t = Measure(assets, kind, paths, body, scene, h, "hang_" + scene.Name.Replace(' ', '_'));
                Assert.InRange(t.Knuckles.X, -0.08f, 0.0f);
                Assert.InRange(t.Knuckles.Y, -0.01f, 0.05f);
                Assert.InRange(t.Pelvis, 0f, 0.05f);
            }
        }
    }

    static List<string> Clips(List<Row> rows)
    {
        var all = new List<string>();
        foreach (var r in rows) if (r.Ledge != "-" && (all.Count == 0 || all[^1] != r.Ledge)) all.Add(r.Ledge);
        return all;
    }

    [Fact]
    public void A_corner_that_cannot_be_taken_stops_the_shimmy_and_says_why()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        foreach (var (name, blocks, why) in new[]
        {
            // A wall 0.3 m thick: its end's other face too short to hang on.
            ("a thin wall's end", new[] { (new Vector3(1f, -3f, 0f), new Vector3(1.3f, 1.5f, h)) }, HangRefusal.ShortLip),
            // A braced wall whose side has nothing under its edge (the wall below stops short of the corner): no wall for the braced corner's end.
            ("a braced corner over nothing", new[] { (new Vector3(1f, -3f, h - 0.3f), new Vector3(3f, 1.5f, h)), (new Vector3(1f, -3f, 0f), new Vector3(1.2f, 1.5f, h - 0.3f)) }, HangRefusal.NoWall),
        })
        {
            var rows = Play(assets, kind, Leaping, new Scene(name, blocks, (i, b) => Grab(i, new Vector2(0f, 1f)), 900));
            output.WriteLine($"{name}: {Story(rows)}");
            Assert.DoesNotContain(Clips(rows), c => c.StartsWith("hang_corner_", StringComparison.Ordinal));
            Assert.Contains(rows, r => r.Refused.EndsWith(" " + why, StringComparison.Ordinal));
            Assert.Equal(BodyState.Hanging, rows[^1].State);
            Assert.InRange(MathF.Abs(rows[^1].Yaw), 0f, 1e-3f);
            Assert.InRange(rows[^1].Feet.Y, 0.9f, 1.25f);   // stopped short of the corner, where the cycle no longer fits (the earlier build's stop)
        }
    }

    [Fact]
    public void A_leap_s_lip_within_the_tolerance_is_leapt_to_warped_and_beyond_it_is_not()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        foreach (var (near, takes) in new[] { (1.1f, true), (1.6f, false) })
        {
            // The grabbed column and one from near to the left (1.2 m wide), its top 0.1 m higher; nothing above them. The nearest middle to the clip's own
            // 1.2 m with both hands on it: near + 0.2 (1.3: 0.1 along; 1.8: 0.6, beyond the tolerance).
            float gap = near + 0.2f;
            var blocks = new[] { (new Vector3(1f, -0.6f, 0f), new Vector3(3f, 0.6f, h)), (new Vector3(1f, near, 0f), new Vector3(3f, near + 1.2f, h + 0.1f)) };
            var rows = Play(assets, kind, Leaping, new Scene($"a lip {gap} m to the left", blocks, (i, b) => Press(i, new Vector2(0f, 1f)), 600));
            output.WriteLine($"a lip {F(gap)} m to the left and 0.1 m up: {Story(rows)}");
            if (takes)
            {
                var t = TakenOf(rows, paths, new Vector3(1f, gap, h + 0.1f), new Vector2(-1f, 0f))!;
                output.WriteLine($"  the knuckles at its end {F(t.Knuckles.X * 100f, "0.0")} cm into the face, {F(t.Knuckles.Y * 100f, "0.0")} over the top; at the contact {F(t.ContactKnuckle.X * 100f, "0.0")} and {F(t.ContactKnuckle.Y * 100f, "0.0")}; the pelvis's pop {F(t.Pelvis * 1000f, "0.0")} mm");
                Assert.Equal("hang_leap_left", t.Clip);
                Assert.InRange(t.Knuckles.X, -0.08f, 0f);
                Assert.InRange(t.Knuckles.Y, -0.01f, 0.05f);
                Assert.InRange(rows[^1].Feet.Y, gap - 0.01f, gap + 0.08f);   // the nearest place with both hands on and the feet on its wall: warped 0.1 to 0.18 along, 0.1 up
                int leap = rows.FindIndex(r => r.Ledge == "hang_leap_left");
                Assert.InRange(rows[^1].Feet.Z - rows[leap - 1].Feet.Z, 0.07f, 0.13f);   // the lip 0.1 m higher (the hang's loop moves the feet a centimetre or two)
            }
            else
            {
                Assert.DoesNotContain(Clips(rows), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
                Assert.Contains(rows, r => r.Refused.EndsWith(" NoLip", StringComparison.Ordinal));
                Assert.Contains("pull_up_braced", Clips(rows));   // as before: jump pulls up where there is a top
            }
        }
    }

    [Fact]
    public void Where_there_is_no_lip_the_controls_do_as_before()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        var column = new[] { (new Vector3(1f, -0.6f, 0f), new Vector3(3f, 0.6f, h)) };
        // Back with jump, nothing behind: the earlier build's push-off.
        var back = Play(assets, kind, Leaping, new Scene("back", column, (i, b) => Press(i, new Vector2(-1f, 0f)), 500));
        output.WriteLine($"back with jump, nothing behind: {Story(back)}");
        Assert.DoesNotContain(Clips(back), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
        Assert.Contains(back, r => r.Refused.Contains("Back") && r.Refused.EndsWith(" NoLip", StringComparison.Ordinal));
        Assert.Contains(back, r => r.Move == TraversalKind.Drop);
        // Jump alone with a top to stand on: the pull-up, as before (no leap looked for).
        var jump = Play(assets, kind, Leaping, new Scene("jump", column, (i, b) => Press(i, Vector2.Zero), 500));
        output.WriteLine($"jump with a top: {Story(jump)}");
        Assert.Contains("pull_up_braced", Clips(jump));
        Assert.DoesNotContain(jump, r => r.Refused != "" && !r.Refused.StartsWith("commit", StringComparison.Ordinal));
        // Crouch with use held, no lip 1.2 m below (one 2.0 m below): the drop, and the lip caught on the way down.
        float up = h + 2.4f;
        var none = Play(assets, kind, Leaping, new Scene("down none", Stepped(up, 2.0f), (i, b) => new Ask(Vector2.Zero, Crouch: i >= 300 && i < 320, Use: i < 120 || i >= 290), 800,
                                                    new Vector3(0.6f, 0f, up - 1.85f + 1.0f)));
        output.WriteLine($"crouch with use held, the next lip 2 m below: {Story(none)}");
        var clips = Clips(none);
        Assert.Contains("drop_braced", clips);
        Assert.Contains(none, r => r.Refused.Contains("Down") && r.Refused.EndsWith(" NoLip", StringComparison.Ordinal));
        Assert.True(clips.IndexOf("drop_braced") < clips.LastIndexOf("grab_fall_braced"), "the lower lip caught after the drop");
        Assert.Equal(BodyState.Hanging, none[^1].State);
        // Crouch alone drops, as before.
        up = h + 1.2f;
        var crouch = Play(assets, kind, Leaping, new Scene("crouch", Grooved(up, 1.2f), (i, b) => new Ask(Vector2.Zero, Crouch: i >= 300 && i < 320, Use: i < 120), 700,
                                                      new Vector3(0.6f, 0f, up - 1.85f + 1.0f)));
        output.WriteLine($"crouch alone over a lip 1.2 m below: {Story(crouch)}");
        Assert.Contains("drop_braced", Clips(crouch));
        Assert.DoesNotContain(Clips(crouch), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
    }

    [Fact]
    public void From_a_free_hang_there_are_no_leaps()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        // Two slabs 1.2 m apart, nothing under their edges: the free hang.
        var slabs = new[] { (new Vector3(1f, -0.6f, h - 0.2f), new Vector3(3f, 0.6f, h)), (new Vector3(1f, 0.9f, h - 0.2f), new Vector3(3f, 2.1f, h)) };
        var rows = Play(assets, kind, Leaping, new Scene("free", slabs, (i, b) => Press(i, new Vector2(0f, 1f)), 500));
        output.WriteLine($"a free hang, jump with left held, a slab 1.2 m to the left: {Story(rows)}");
        Assert.Contains("hang_free", Clips(rows));
        Assert.DoesNotContain(Clips(rows), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Refused.EndsWith(" FreeHang", StringComparison.Ordinal));
    }

    [Fact]
    public void On_a_ledge_that_goes_on_jump_with_a_side_held_pulls_up_or_shimmies_FI1()
    {
        // A side leap only where the lip breaks between the hands and the target; on one long ledge, jump with a side held pulls up where there
        // is a top, else does nothing new (the shimmy goes on while the side is held). The earlier build's (breaks off) leapt along it.
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        var noTop = new[] { (new Vector3(1f, -3f, 0f), new Vector3(3f, 3f, h)), (new Vector3(1.15f, -3f, h), new Vector3(3f, 3f, h + 0.55f)) };
        var top = new[] { (new Vector3(1f, -3f, 0f), new Vector3(3f, 3f, h)) };
        // Jump with left held, and left held on for a second after (the shimmy while the side is held).
        Ask Plan(int i, Body b) => i == 0 ? new Ask(Vector2.Zero, Jump: true) : i == 250 ? new Ask(new Vector2(0f, 1f), Jump: true) : i > 250 && i < 370 ? new Ask(new Vector2(0f, 1f)) : new Ask(Vector2.Zero);
        var none = Play(assets, kind, Leaping, new Scene("long, no top", noTop, Plan, 600));
        output.WriteLine($"a long ledge, no top to stand on, jump with left held: {Story(none)}");
        Assert.DoesNotContain(Clips(none), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
        Assert.Contains(none, r => r.Refused.Contains("Left") && r.Refused.EndsWith(" Continuous", StringComparison.Ordinal));
        Assert.Contains("shimmy_left_braced", Clips(none));
        Assert.Equal(BodyState.Hanging, none[^1].State);
        var pulled = Play(assets, kind, Leaping, new Scene("long, a top", top, Plan, 600));
        output.WriteLine($"a long ledge with a top, jump with left held: {Story(pulled)}");
        Assert.DoesNotContain(Clips(pulled), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
        Assert.Contains("pull_up_braced", Clips(pulled));
        // The earlier build's (a tape without breaks=1): the leap along it.
        var old = Play(assets, kind, Leaping with { Breaks = false }, new Scene("long, no breaks", noTop, (i, b) => Press(i, new Vector2(0f, 1f)), 600));
        output.WriteLine($"no breaks, the same: {Story(old)}");
        Assert.Contains("hang_leap_left", Clips(old));
        Assert.InRange(old[^1].Feet.Y, 1.19f, 1.21f);
    }

    [Fact]
    public void Off_the_switch_the_hang_has_no_corners_no_leaps_and_no_store()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = GrabHeight(paths);
        var (w, _) = Make(assets, kind, null, new Vector3(0.55f, 0f, 0f));
        Assert.False(w.TryStore<PersonHang>(out _));
        Assert.False(w.TryService<PersonHangSettings>(out _));
        var corner = Play(assets, kind, null, Corners(h)[0]);
        var leap = Play(assets, kind, null, Leaps(h)[0]);
        output.WriteLine($"the hang without its moves: {Story(corner)}; {Story(leap)}");
        Assert.DoesNotContain(Clips(corner), c => c.StartsWith("hang_corner_", StringComparison.Ordinal));
        Assert.DoesNotContain(Clips(leap), c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
        Assert.InRange(corner[^1].Feet.Y, 0.9f, 1.25f);   // the shimmy's stop short of the corner
        Assert.DoesNotContain(corner, r => r.Refused != "" && !r.Refused.StartsWith("commit", StringComparison.Ordinal));
    }

    [Fact]
    public void The_note_says_the_settings_and_none_is_the_hang_moves_off()
    {
        Assert.Equal("on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15 breaks=1", Leaping.Note());
        Assert.Equal("off", PersonHangSettings.Off.Note());
        Assert.False(Leaping.WithNote(null).On);
        Assert.False(Leaping.WithNote("off").On);
        // A note (no breaks=) is the earlier build's leaps along a lip that goes on.
        Assert.False(Leaping.WithNote("on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15").Breaks);
        Assert.True(Leaping.WithNote(Leaping.Note()).Breaks);
        Assert.True(Leaping.Rules().Breaks);
        var back = Leaping.WithNote((Leaping with { Leaps = false, LeapTolerance = 0.3f }).Note());
        Assert.Equal((true, true, false, 0.3f), (back.On, back.Corners, back.Leaps, back.LeapTolerance));
        Assert.Equal(15f * DetMath.DegToRad, Leaping.Rules().AngleTolerance, 5);
        Assert.Equal(0.5f, (Leaping with { LeapTolerance = 9f }).Rules().LeapTolerance);
    }
}
