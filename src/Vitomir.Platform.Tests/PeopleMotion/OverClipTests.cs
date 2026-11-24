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
/// The models repository's clips (<see cref="PersonOverClips"/>, <see cref="PeopleOver"/>, <see cref="PeopleOff"/>, the motor's clip-carried
/// moves <see cref="BodyMotor.OverPress"/>) on the body's own motor and the player's spring: the notes read; the rule's half-cycles and rate against the
/// notes' examples; a vault out of a run on its footfall and back into the run, a climb onto, a mantle and a hop up carried along their clips' paths,
/// the hands against the real edge, the feet on the top, no pop at the clip's start and end; a run-off begun on the run's last footfall before the
/// edge and a walk-off, the air fitted to the fall and the landing by foot and band. Every expectation from the clips' notes. Skips without the models
/// repository's files (and a newer set for the edges).
/// </summary>
public sealed class OverClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);
    const float G = 9.81f;

    static PersonOverClips Over(CharacterAssets assets, int kind)
    {
        var over = assets.Kind(kind).Person!.Over;
        Assert.SkipUnless(over is not null && assets.Kind(kind).Person!.Air is not null, "the models repository's files are too old (no moves over and onto things)");
        return over!;
    }

    static ClipNotes Notes(CharacterAssets assets, int clip) => ((INotedClip)assets.Clip(clip)).Notes;

    static int Clip(CharacterAssets assets, string name)
    {
        for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i).Name == name) return i;
        throw new InvalidOperationException(name);
    }

    /// <summary>A world with the motor (the player's spring), the animation with the air's and these clips (<paramref name="over"/>; null: the earlier build's), one body of the player's kind facing +X at <paramref name="at"/>; the floor at 0 and <paramref name="blocks"/>.</summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonOverSettings? over, Vector3 at, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: over);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        return (w, e);
    }

    static void Ask(World w, EntityId e, float share, Gait gait, bool jump)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = new Vector2(share, 0f);
        intent.Gait = gait;
        intent.Faces = true;
        intent.Facing = 0f;
        intent.Jump = jump;
        intent.JumpHeld = jump;
    }

    /// <summary>A tick as it went: the clip and its time, the fade, the body, the move, and (as drawn, in the world) the pelvis, the left hand and both balls.</summary>
    sealed record Row(string Clip, float Time, string Previous, float Blend, TraversalKind Move, bool Ground, Vector3 Feet, Vector3 Velocity, Vector3 Pelvis, Vector3 HandL,
                      Vector3 HandR, Vector3 BallL, Vector3 BallR, OffMode Off, float Rate, int Half);

    /// <summary>
    /// <paramref name="ticks"/> ticks going ahead at <paramref name="share"/> of <paramref name="gait"/> (after <paramref name="settle"/> the same), jump
    /// pressed on the first tick the feet are at or past <paramref name="pressAt"/> along X (NaN: on the first tick; +∞: never); every tick's row.
    /// </summary>
    static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, float share, Gait gait, float pressAt, int ticks, int settle = 0, int goFor = int.MaxValue)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        for (int i = 0; i < 20; i++) { Ask(w, e, 0f, gait, false); w.Step(); }   // put down on the ground
        for (int i = 0; i < settle; i++) { Ask(w, e, share, gait, false); w.Step(); }
        bool pressed = false;
        for (int i = 0; i < ticks; i++)
        {
            bool press = !pressed && (float.IsNaN(pressAt) || w.Get<Body>(e).Feet.X >= pressAt);
            pressed |= press;
            Ask(w, e, i < goFor ? share : 0f, gait, press);
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var over = w.TryStore<PersonOver>(out var overs) && overs.Has(e) ? overs.Get(e) : default;
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", a.PreviousClip >= 0 ? a.Blend : 1f,
                             w.Get<Traversal>(e).Kind, body.State == BodyState.Ground, body.Feet, body.Velocity, Bone(w, e, assets, rig.Pelvis),
                             Bone(w, e, assets, rig.LeftArm.End), Bone(w, e, assets, rig.RightArm.End), Bone(w, e, assets, rig.LeftLeg.Tip), Bone(w, e, assets, rig.RightLeg.Tip),
                             over.Off, over.Rate, over.Half));
        }
        return rows;
    }

    void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-18} {F(r.Time, "0.000")} {(r.Previous != "-" ? $"<{r.Previous} {F(r.Blend)}" : ""),-24} {r.Move,-9} {(r.Ground ? "G" : "A")} {r.Off,-6} "
                             + $"x {F(r.Feet.X, "0.000")} z {F(r.Feet.Z, "0.000")} v {F(new Vector2(r.Velocity.X, r.Velocity.Y).Length())} pelvis {F(r.Pelvis.Z, "0.000")} "
                             + $"handL {F(r.HandL.X, "0.000")},{F(r.HandL.Z, "0.000")} balls {F(r.BallL.Z, "0.000")},{F(r.BallR.Z, "0.000")}");
        }
    }

    /// <summary>The largest change a tick of the pelvis's change a tick (m: a pop; the body's own motion is a steady change) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static float Pop(List<Row> rows, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((rows[i].Pelvis - rows[i - 1].Pelvis) - (rows[i - 1].Pelvis - rows[i - 2].Pelvis)).Length());
        return pop;
    }

    [Fact]
    public void The_notes_are_read_and_the_rule_gives_the_notes_examples()
    {
        var (assets, kind, _) = Player();
        var over = Over(assets, kind);
        var paths = over.Paths!;
        string N(int c) => assets.Clip(c).Name;
        // The moves: each a path from nought, a row a frame, its band within its move's and the move's bands meeting.
        foreach (var c in paths.Clips.Where(c => !c.RunOnly))   // the low vaults are LowVaultTests'
        {
            var tv = Notes(assets, c.Clip).Traverse!;
            Assert.Equal(0f, c.F[0], 4);
            Assert.Equal(0f, c.U[0], 4);
            Assert.InRange(c.T[^1], c.Duration - 0.02f, c.Duration + 0.02f);
            Assert.True(c.High > c.Low && c.TopM >= c.Low - 1e-3f && c.TopM <= c.High + 1e-3f, $"{c.Name}: its own top within its band");
            Assert.Equal(c.Kind == TraversalKind.Vault, tv.Thin);
            if (c.Kind != TraversalKind.HopUp) Assert.True(c.ContactS > 0f && c.WarpTo >= c.ContactS - 1e-3f, $"{c.Name}: the warp to the hands' contact");
            if (c.Kind == TraversalKind.Vault) Assert.True(c.ExitSpeed > 0f && !float.IsNaN(c.EnterPhase) && !float.IsNaN(c.ExitPhase));
            else Assert.InRange(c.U[^1], c.TopM - 0.05f, c.TopM + 0.05f);   // ends standing on its own top
            output.WriteLine($"{c.Name}: {c.Kind} {F(c.Low)}-{F(c.High)} m, {F(c.Duration)} s, its face {F(c.FaceM)} m ahead, top {F(c.TopM)} m, the path to {F(c.F[^1])},{F(c.U[^1])} m, "
                             + $"the warp {F(c.WarpFrom)}-{F(c.WarpTo)} s, hands at {F(c.ContactS, "0.000")} s");
        }
        foreach (var kindOf in new[] { TraversalKind.Vault, TraversalKind.ClimbOnto, TraversalKind.Mantle })
        {
            var own = paths.Clips.Where(c => c.Kind == kindOf && !c.RunOnly).ToArray();
            for (int i = 1; i < own.Length; i++) Assert.Equal(own[i - 1].High, own[i].Low, 3);
        }
        // The stride carried in the air and the pedalling loops; the landings' lists and the player's own.
        var fr = Notes(assets, over.FallRun).Airborne!;
        Assert.True(fr.Pedals && assets.Clip(over.FallRun).Loops);
        Assert.Equal(0.5f * assets.Clip(over.FallRun).Duration, fr.HalfS, 2);
        foreach (int c in over.RunOffs.Concat(over.WalkOffs)) Assert.True(Notes(assets, c).Airborne!.Strides, N(c));
        Assert.Equal("player", Notes(assets, over.LandHighPlayer).Landing!.Who);
        // The rule against the notes' examples (the man): off 1 m and 2 m running and walking, and the level running jump.
        float A(string clip) { var ab = Notes(assets, Clip(assets, clip)).Airborne!; return ab.AirTo - ab.AirFrom; }
        float hr = fr.HalfS, hw = Notes(assets, over.FallWalk).Airborne!.HalfS;
        var jump = Notes(assets, Clip(assets, "jump_run_l")).Airborne!;
        var examples = new (string What, float A, float H, float T, int N, float Rate)[]
        {
            ("running off 1 m", A("run_off_l"), hr, MathF.Sqrt(2f * 1f / G), 0, 0.89f),
            ("running off 2 m", A("run_off_l"), hr, MathF.Sqrt(2f * 2f / G), 1, 1.20f),
            ("walking off 1 m", A("walk_off_l"), hw, MathF.Sqrt(2f * 1f / G), 0, 0.66f),
            ("walking off 2 m", A("walk_off_l"), hw, MathF.Sqrt(2f * 2f / G), 1, 1.20f),
            ("a level running jump", A("jump_run_l"), hr, 2f * jump.VUpMs / G, 0, 1.00f),
        };
        foreach (var (what, a, h, t, n, rate) in examples)
        {
            var (half, r) = PeopleOver.Rule(a, h, t);
            output.WriteLine($"{what}: A {F(a, "0.000")} s, h {F(h, "0.000")} s, t {F(t, "0.000")} s: n {half}, rate {F(r)} (the notes: n {n}, rate {F(rate)})");
            Assert.Equal(n, half);
            Assert.InRange(r, rate - 0.015f, rate + 0.015f);
        }
        // The settings through a tape's note; none is the earlier build's.
        var tuned = new PersonOverSettings { EnterSeconds = 0.1f, OffFrom = 0.25f };
        Assert.Equal(tuned.Note(), new PersonOverSettings().WithNote(tuned.Note()).Note());
        Assert.Equal(PersonOverSettings.Off, new PersonOverSettings().WithNote(null));
        Assert.Equal(PersonOverSettings.Off, new PersonOverSettings().WithNote("off"));
        Assert.False(PersonOverSettings.Off.Any);
    }

    /// <summary>
    /// A move made against a block (min, max) going +X: the rows, the first tick of the move, the tick of its clip's hands' contact (or −1), its last
    /// tick; the hand(s) against the real edge at the contact (forward past the near face and over the top, m), the feet over the top at the end.
    /// </summary>
    sealed record Made(List<Row> Rows, int Start, int Contact, int End, string Clip, Vector2 HandAt, float FeetOver, float BallsOver, float PopIn, float PopOut);

    Made Move(CharacterAssets assets, int kind, PersonOverSettings? over, float share, Gait gait, float pressAt, Vector3 at, Vector3 min, Vector3 max, int ticks = 600, int settle = 0)
    {
        var (w, e) = Make(assets, kind, over, at, (min, max));
        var rows = Drive(w, e, assets, kind, share, gait, pressAt, ticks, settle);
        int start = rows.FindIndex(r => r.Move != TraversalKind.None);
        if (start < 0) Say("no move", rows, 0, rows.Count);
        Assert.True(start >= 0, "a move started");
        int end = rows.FindIndex(start, r => r.Move == TraversalKind.None);
        if (end < 0) Say("no end", rows, start - 2, rows.Count);
        Assert.True(end > start, "the move ended");
        string clip = rows[start].Clip;
        int contact = -1;
        var person = assets.Kind(kind).Person!;
        if (person.Over?.Paths?.Clips.FirstOrDefault(c => c.Name == clip) is { ContactS: > 0f } oc)
            contact = rows.FindIndex(start, r => r.Clip == clip && r.Time >= oc.ContactS - 0.5f * Tick);
        var hand = contact >= 0 ? rows[contact].HandL : Vector3.Zero;
        float ballsOver = MathF.Min(rows[^1].BallL.Z, rows[^1].BallR.Z) - max.Z;
        Assert.True(Finite(w, e));
        return new Made(rows, start, contact, end, clip, new Vector2(hand.X - min.X, hand.Z - max.Z), rows[^1].Feet.Z - max.Z, ballsOver,
                        Pop(rows, start - 3, start + 4), Pop(rows, end - 3, end + 4));
    }

    [Fact]
    public void A_vault_goes_out_of_a_run_on_its_footfall_with_the_hand_on_the_edge_and_back_into_the_run()
    {
        var (assets, kind, _) = Player();
        var paths = Over(assets, kind).Paths!;
        foreach (var c in paths.Clips.Where(c => c.Kind == TraversalKind.Vault && !c.RunOnly))
        {
            Made? own = null;
            foreach (float h in new[] { c.TopM, c.Low + 0.02f, c.High - 0.02f })
            {
                // A fence 0.25 m deep across the way at x 8, run at from the stand at x 0, jump pressed 2 m short of it.
                var m = Move(assets, kind, new PersonOverSettings(), 1f, Gait.Run, 6f, Vector3.Zero, new Vector3(8f, -3f, 0f), new Vector3(8.25f, 3f, h));
                var rows = m.Rows;
                own ??= m;
                Say($"{c.Name} over {F(h)} m", rows, m.Start - 3, m.Start + 3);
                Assert.Equal(paths.For(TraversalKind.Vault, h)!.Name, m.Clip);
                // Out of the run on the left footfall: the run's phase at the clip's enter_phase, so no fade.
                Assert.Contains("run", rows[m.Start - 1].Clip);
                Assert.Equal("-", rows[m.Start].Previous);
                // The capsule on the clip's path: it goes on at nearly the run's speed, slowing only to the clip's through speed.
                float slowest = rows.Skip(m.Start).Take(m.End - m.Start).Min(r => r.Velocity.X);
                Assert.InRange(slowest, 2.0f, 3.2f);
                // Back into the run at its exit_phase with no fade, on the ground beyond.
                int run = rows.FindIndex(m.End, r => r.Clip.Contains(":run@") || r.Clip.StartsWith("run", StringComparison.Ordinal));
                Say("its end", rows, m.End - 2, m.End + 3);
                Assert.True(run >= m.End && run <= m.End + 1, "the run right after it");
                Assert.Equal("-", rows[run].Previous);
                Assert.True(rows[m.End].Feet.X > 8.25f + 0.3f && rows[m.End].Ground);
                output.WriteLine($"{c.Name} over {F(h)} m: started {F(8f - rows[m.Start].Feet.X)} m short of the face (its own {F(c.FaceM)}), the left hand at the contact "
                                 + $"{F(m.HandAt.X * 100f, "0.0")} cm past the face and {F(m.HandAt.Y * 100f, "0.0")} cm over the top (at its own height {F(own.HandAt.X * 100f, "0.0")}, "
                                 + $"{F(own.HandAt.Y * 100f, "0.0")}: off by {F((m.HandAt.X - own.HandAt.X) * 100f, "0.0")}, {F((m.HandAt.Y - own.HandAt.Y) * 100f, "0.0")} cm); "
                                 + $"slowest {F(slowest)} m/s; {F((m.End - m.Start) * Tick)} s; the pelvis's pop in {F(m.PopIn * 1000f, "0.0")} mm, out {F(m.PopOut * 1000f, "0.0")} mm");
                Assert.InRange(MathF.Abs(m.HandAt.X - own.HandAt.X), 0f, 0.05f);
                Assert.InRange(MathF.Abs(m.HandAt.Y - own.HandAt.Y), 0f, 0.5f * (c.High - c.Low) + 0.02f);
                Assert.InRange(m.PopIn, 0f, 0.02f);
                Assert.InRange(m.PopOut, 0f, 0.02f);
            }
        }
    }

    [Fact]
    public void A_climb_onto_a_mantle_and_a_hop_up_carry_the_capsule_along_their_paths_onto_the_top()
    {
        var (assets, kind, _) = Player();
        var paths = Over(assets, kind).Paths!;
        foreach (var c in paths.Clips.Where(c => c.Kind != TraversalKind.Vault))
        {
            Made? own = null;
            foreach (float h in new[] { c.TopM, c.Low + 0.05f, c.High - 0.02f })
            {
                // A deep block from x 1, the body standing 0.55 m short of its face, facing it, jump tapped (from a step towards it: a hop up needs the way).
                var m = Move(assets, kind, new PersonOverSettings(), c.Kind == TraversalKind.HopUp ? 0.3f : 0f, Gait.Walk, float.NaN, new Vector3(0.45f, 0f, 0f),
                             new Vector3(1f, -3f, 0f), new Vector3(4f, 3f, h), ticks: 300);
                var rows = m.Rows;
                own ??= m;
                Assert.Equal(paths.For(c.Kind, h)!.Name, m.Clip);
                Assert.Equal(0, m.Start);   // on the press, the clip making its own hop
                Say($"{m.Clip} onto {F(h)} m", rows, m.End - 2, m.End + 2);
                // Standing on the top at the end (crouched where it cannot stand), the feet on it.
                Assert.True(rows[^1].Ground);
                Assert.InRange(m.FeetOver, -0.01f, 0.02f);
                float hands = m.Contact >= 0 ? m.HandAt.X - own.HandAt.X : 0f, handsUp = m.Contact >= 0 ? m.HandAt.Y - own.HandAt.Y : 0f;
                output.WriteLine($"{m.Clip} onto {F(h)} m: {F((m.End - m.Start) * Tick)} s; "
                                 + (m.Contact >= 0 ? $"the left hand at the contact {F(m.HandAt.X * 100f, "0.0")} cm past the face, {F(m.HandAt.Y * 100f, "0.0")} cm over the top (off its own height's by {F(hands * 100f, "0.0")}, {F(handsUp * 100f, "0.0")} cm); " : "")
                                 + $"the feet {F(m.FeetOver * 100f, "0.0")} cm over the top at the end, the balls {F(m.BallsOver * 100f, "0.0")} cm; the pelvis's pop in {F(m.PopIn * 1000f, "0.0")} mm, "
                                 + $"out {F(m.PopOut * 1000f, "0.0")} mm");
                Assert.InRange(MathF.Abs(hands), 0f, 0.05f);
                Assert.InRange(m.PopOut, 0f, 0.03f);
            }
        }
    }

    /// <summary>A ledge <paramref name="drop"/> high from x −40 to 0, the body on it from x −8 (walking −4) going ahead; the rows.</summary>
    List<Row> OffEdge(CharacterAssets assets, int kind, PersonOverSettings? over, float drop, Gait gait, out World w, out EntityId e)
    {
        (w, e) = Make(assets, kind, over, new Vector3(gait == Gait.Run ? -8f : -4f, 0f, drop), (new Vector3(-40, -40, 0), new Vector3(0, 40, drop)));
        return Drive(w, e, assets, kind, 1f, gait, float.PositiveInfinity, 600);
    }

    [Fact]
    public void Off_an_edge_running_or_walking_the_clip_leaves_with_the_stride_the_air_fitted_to_the_fall_and_lands_by_foot_and_band()
    {
        var (assets, kind, _) = Player();
        var over = Over(assets, kind);
        Assert.SkipUnless(over.RunOffs.All(c => c >= 0) && over.WalkOffs.All(c => c >= 0) && over.FallRun >= 0 && over.FallWalk >= 0, "the models repository's files are too old");
        var rig = assets.Kind(kind).Person!.Rig;
        foreach (var gait in new[] { Gait.Run, Gait.Walk })
            foreach (float drop in new[] { 1f, 2f })
            {
                var rows = OffEdge(assets, kind, new PersonOverSettings(), drop, gait, out var w, out var e);
                int off = rows.FindIndex(r => !r.Ground);
                int contact = rows.FindIndex(off, r => r.Ground);
                Assert.True(off > 0 && contact > off, "it went off and landed");
                string prefix = gait == Gait.Run ? "run_off_" : "walk_off_";
                int begun = rows.FindIndex(r => r.Clip.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(begun >= 0 && begun <= off, $"{prefix} by the take-off");
                var clip = Clip(assets, rows[begun].Clip);
                var ab = Notes(assets, clip).Airborne!;
                Say($"{rows[begun].Clip} off {F(drop)} m", rows, begun - 2, begun + 3);
                Say("its take-off", rows, off - 2, off + 2);
                // The take-off foot (the clip's enter_foot) against the edge at the clip's take-off.
                int takeoff = rows.FindIndex(begun, r => r.Clip == rows[begun].Clip && r.Time >= ab.TakeoffS - 0.5f * Tick);
                var ball = ab.EnterFoot == "l" ? rows[takeoff].BallL : rows[takeoff].BallR;
                // The rule: n and the rate as the notes' rule gives for the time to contact.
                var row = rows[off];
                float t = (contact - off) * Tick;
                var loop = Notes(assets, gait == Gait.Run ? over.FallRun : over.FallWalk).Airborne!;
                Say("its landing", rows, contact - 3, contact + 3);
                string land = rows[contact].Clip;
                var ln = Notes(assets, Clip(assets, land)).Landing!;
                int footLand = ab.Land.EndsWith('l') ? 1 : 2;
                int expectFoot = row.Half % 2 == 0 ? footLand : 3 - footLand;
                output.WriteLine($"{(gait == Gait.Run ? "running" : "walking")} off {F(drop)} m: {rows[begun].Clip} begun {F((off - begun) * Tick, "0.000")} s before the body left, "
                                 + $"its take-off foot's ball {F(ball.X * 100f, "0.0")} cm past the edge at its take-off; in the air {F(t, "0.000")} s: n {row.Half}, rate {F(row.Rate)}; "
                                 + $"landed {land} ({ln.Onto}, {F(ln.DropLow, "0.0")}-{F(ln.DropHigh, "0.0")} m) from {rows[contact].Previous} at {F(rows[contact - 1].Time, "0.000")} s of {rows[contact - 1].Clip}");
                Assert.Equal(drop >= 1.2f ? 1 : 0, ln.DropLow >= 1.2f - 1e-3f ? 1 : 0);
                Assert.Equal(gait == Gait.Walk, ln.Walking);
                Assert.Equal(expectFoot == 1 ? "l" : "r", ln.Onto);
                Assert.Equal(drop < 1.5f ? 0 : 1, row.Half);   // the notes' examples
                Assert.Equal("-", rows[contact].Previous);       // met at its moment: no blend
                if (gait == Gait.Run) Assert.InRange(ball.X, -0.1f, 0.1f);
                else Assert.Equal("-", rows[begun].Previous);   // the walk paced to the walk-off's phase: no fade
                Assert.True(Finite(w, e));
            }
    }

    [Fact]
    public void A_level_running_jump_takes_the_rule_and_lands_back_to_back()
    {
        var (assets, kind, _) = Player();
        var over = Over(assets, kind);
        Assert.SkipUnless(over.FallRun >= 0, "the models repository's files are too old");
        var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero);
        var rows = Drive(w, e, assets, kind, 1f, Gait.Run, 4f, 480);
        int off = rows.FindIndex(r => !r.Ground && r.Feet.X >= 4f);
        int contact = rows.FindIndex(off, r => r.Ground);
        Say("level running jump", rows, contact - 4, contact + 3);
        Assert.StartsWith("jump_run_", rows[off].Clip);
        var ab = Notes(assets, Clip(assets, rows[off].Clip)).Airborne!;
        Assert.Equal(0, rows[contact - 1].Half);
        Assert.InRange(rows[contact - 1].Rate, 0.95f, 1.05f);
        Assert.Equal(ab.Land, rows[contact].Clip);
        Assert.Equal("-", rows[contact].Previous);
        output.WriteLine($"{rows[off].Clip}: n {rows[contact - 1].Half}, rate {F(rows[contact - 1].Rate, "0.000")}, into {rows[contact].Clip} back to back");
    }

    [Fact]
    public void Off_the_switch_and_without_the_clips_the_world_has_no_store_and_moves_on_the_traversal_s_paths()
    {
        var (assets, kind, _) = Player();
        var (w14, e14) = Make(assets, kind, PersonOverSettings.Off, new Vector3(0.45f, 0f, 0f), (new Vector3(1f, -3f, 0f), new Vector3(4f, 3f, 0.75f)));
        Assert.False(w14.TryStore<PersonOver>(out _));
        var (w, _) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero);
        Assert.Equal(assets.Kind(kind).Person!.Over is not null, w.TryStore<PersonOver>(out _));
        // The earlier build's climb onto: from the tap's jump, on the traversal's own path, its stand-in drawn.
        var rows = Drive(w14, e14, assets, kind, 0.3f, Gait.Walk, float.NaN, 200);
        int start = rows.FindIndex(r => r.Move != TraversalKind.None);
        Assert.True(start > 0);
        Assert.DoesNotContain(rows, r => r.Clip.StartsWith("climb_onto", StringComparison.Ordinal));
    }
}
