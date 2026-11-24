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
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>the low things at a run</b> — <c>hurdle_low</c> over a thin top 0.35–0.6 m and <c>vault_speed</c> over a low
/// deep top (0.4–0.65 m high, 0.3–0.75 m deep: a trough, a bench), by themselves at a run or a sprint as before run vaults are (the cone, the hold to the
/// left footfall, in and out in phase), played at the speed met; in place of the earlier build's step-over and the sprint vault's stand-in where they reach; a walk stops
/// (a tap hops up the bench); the earlier build's behind the switch. The player driven by its keys. Skips without the models repository's files, or.
/// </summary>
public sealed class LowVaultTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    static OverPaths Paths(CharacterAssets assets, int kind)
    {
        var paths = assets.Kind(kind).Person!.Over?.Paths;
        Assert.SkipUnless(paths?.HasLow == true && assets.Kind(kind).Person!.Ledge is not null, "the models repository's files are too old (no low vaults)");
        return paths!;
    }

    /// <summary>The player's body driven by its keys with the moves', the ledge's and the commit point's clips and tier 1's moves (<paramref name="moves"/>; none: the earlier build's); the floor at 0 and <paramref name="blocks"/>.</summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonMovesSettings? moves, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: new PersonPrelandingSettings(), commit: new PersonCommitSettings(), moves: moves);
        b.Service(new PersonHandsSettings());
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, new Vector3(0f, 0f, 0.01f), 0f, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        if (w.TryStore<PersonLedge>(out _)) w.Set(e, new PersonLedge());
        if (w.TryStore<PersonCommit>(out _)) w.Set(e, new PersonCommit());
        return (w, e);
    }

    sealed record Row(string Clip, float Time, string Previous, TraversalKind Move, float Seconds, bool Ground, Vector3 Feet, Vector3 Velocity, int Pending, float Phase,
                      Vector3 Pelvis, Vector3 KnuckleL, Vector3 KnuckleR);

    static List<Row> Drive(World w, EntityId e, CharacterAssets assets, Func<int, ActionFrame> frame, int ticks)
    {
        for (int i = 0; i < 60; i++) MovementServices.Step(w, ActionFrame.Idle);
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(w.Get<CharacterAnimation>(e).Kind).Person!.Rig;
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, frame(i));
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var over = w.TryStore<PersonOver>(out var overs) && overs.Has(e) ? overs.Get(e).Move : default;
            float d = a.Clip >= 0 ? assets.Clip(a.Clip).Duration : 0f;
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", w.Get<Traversal>(e).Kind,
                             w.Get<Traversal>(e).Seconds, body.State == BodyState.Ground, body.Feet, body.Velocity, over.Pending, d > 0f ? a.Time / d - MathF.Floor(a.Time / d) : 0f,
                             Bone(w, e, assets, rig.Pelvis), Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip)));
        }
        return rows;
    }

    static float Pop(List<Row> rows, Func<Row, Vector3> of, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    static ActionFrame Ahead(Buttons held = Buttons.None) => new(new Vector2(0f, 1f), default, held, default);

    /// <summary>A top <paramref name="h"/> high and <paramref name="d"/> deep across +X at x 12, 6 m wide.</summary>
    static (Vector3, Vector3) Top(float h, float d) => (new Vector3(12f, -3f, 0f), new Vector3(12f + d, 3f, h));

    [Fact]
    public void The_notes_are_read()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var low = paths.Clips.Where(c => c.RunOnly).ToArray();
        Assert.Equal(2, low.Length);
        var hurdle = low.Single(c => c.Hurdle);
        var speed = low.Single(c => !c.Hurdle);
        Assert.Equal("hurdle_low", hurdle.Name);
        Assert.Equal("vault_speed", speed.Name);
        Assert.Equal(0.35f, hurdle.Low, 2); Assert.Equal(0.6f, hurdle.High, 2);
        Assert.Equal(0.4f, speed.Low, 2); Assert.Equal(0.65f, speed.High, 2);
        Assert.Equal(0.3f, speed.DepthLow, 2); Assert.Equal(0.75f, speed.DepthHigh, 2);
        Assert.Empty(hurdle.Hands);
        Assert.NotEmpty(speed.Hands);
        foreach (var c in low)
        {
            Assert.True(c.SpeedIn > 0f && c.ExitSpeed > 0f && !float.IsNaN(c.EnterPhase) && !float.IsNaN(c.ExitPhase));
            Assert.True(c.WarpTo > c.WarpFrom, c.Name + ": a warp window");
            output.WriteLine($"{c.Name}: {F(c.Low)}-{F(c.High)} m{(c.Hurdle ? " thin" : $", {F(c.DepthLow)}-{F(c.DepthHigh)} m deep")}, made at {F(c.TopM)} m with its face {F(c.FaceM)} m ahead, "
                             + $"{F(c.Duration, "0.000")} s at {F(c.SpeedIn)} m/s, in at the run's {F(c.EnterPhase)} and out at {F(c.ExitPhase)}; the warp {F(c.WarpFrom)}-{F(c.WarpTo)} s");
        }
        // The vault's own never: a tap's, the motor's own detection, the hands' test take the vault clips' bands.
        Assert.Equal("vault_low", paths.For(TraversalKind.Vault, 0.62f)!.Name);
        Assert.Null(paths.For(TraversalKind.Vault, 0.5f));
        Assert.Equal("hurdle_low", paths.LowFor(0.5f, 0.1f, thin: true)!.Name);
        Assert.Equal("vault_speed", paths.LowFor(0.45f, 0.4f, thin: false)!.Name);
        Assert.Equal("vault_speed", paths.LowFor(0.6f, 0.7f, thin: false)!.Name);
        Assert.Null(paths.LowFor(0.8f, 0.5f, thin: false));
        Assert.Null(paths.LowFor(0.5f, 0.9f, thin: false));
    }

    [Fact]
    public void A_run_and_a_sprint_take_the_low_things_by_themselves_on_their_clips()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        foreach (var (what, h, d, clip) in new[] { ("a rail 0.5 m", 0.5f, 0.1f, "hurdle_low"), ("a rail 0.4 m", 0.4f, 0.1f, "hurdle_low"), ("the trough 0.6 x 0.7", 0.6f, 0.7f, "vault_speed"),
                                                   ("the bench 0.45 x 0.4", 0.45f, 0.4f, "vault_speed") })
            foreach (var (gait, held) in new[] { ("run", Buttons.None), ("sprint", Buttons.Sprint) })
            {
                var c = paths.Clips.First(x => x.Name == clip);
                var (w, e) = Make(assets, kind, new PersonMovesSettings(), Top(h, d));
                var rows = Drive(w, e, assets, _ => Ahead(held), 700);
                int start = rows.FindIndex(r => r.Move != TraversalKind.None);
                if (start < 0) for (int i = 0; i < rows.Count; i += 10) output.WriteLine($"   {i} {rows[i].Clip} x {F(rows[i].Feet.X)} z {F(rows[i].Feet.Z)} held {rows[i].Pending}");
                Assert.True(start > 0, $"{what} at a {gait}: a move");
                int held0 = rows.FindIndex(r => r.Pending == 2);
                int end = rows.FindIndex(start, r => r.Move == TraversalKind.None);
                float rate = c.Duration / rows[start].Seconds, speedIn = rows[start - 1].Velocity.X;
                float pin = Pop(rows, r => r.Pelvis, start - 3, start + 4), pout = Pop(rows, r => r.Pelvis, end - 3, end + 4), pall = Pop(rows, r => r.Pelvis, start - 3, end + 4);
                output.WriteLine($"{what} at a {gait}: {rows[start].Clip} ({rows[start].Move}) by itself, held from {F(12f - rows[held0].Feet.X)} m, begun {F(12f - rows[start].Feet.X)} m short "
                                 + $"(its own {F(c.FaceM)}) on the run's phase {F(rows[start - 1].Phase)} (its enter_phase {F(c.EnterPhase)}), met at {F(speedIn)} m/s: rate {F(rate, "0.000")}, "
                                 + $"{F((end - start) * Tick)} s, out at {F(rows[end].Velocity.X)} m/s into {rows[end].Clip} ({(rows[end].Previous == "-" ? "no fade" : "fading")}); "
                                 + $"the pelvis's pop in {F(pin * 1000f, "0.0")} mm, out {F(pout * 1000f, "0.0")}, through {F(pall * 1000f, "0.0")}");
                Assert.Equal(TraversalKind.Vault, rows[start].Move);
                Assert.Equal(clip, rows[start].Clip);
                Assert.True(held0 >= 0 && held0 < start, "held for the footfall by itself, no press");
                Assert.True(end > start && rows[end].Feet.X > 12f + d + 0.3f && rows[end].Ground, "over it, on the ground beyond");
                Assert.InRange(rate, BodyMotor.VaultRate(c, speedIn) - 0.01f, BodyMotor.VaultRate(c, speedIn) + 0.01f);
                if (gait == "run") Assert.InRange(rate, 1f, 1.05f); else Assert.InRange(rate, 1.6f, BodyMotor.MostVaultRate);
                Assert.InRange(pin, 0f, 0.03f);
                Assert.InRange(pout, 0f, 0.03f);
                Assert.DoesNotContain(rows, r => r.Move is TraversalKind.Hurdle or TraversalKind.StepOver);
            }
    }

    [Fact]
    public void At_a_walk_nothing_changes_and_the_stand_ins_are_on_the_switch()
    {
        var (assets, kind, _) = Player();
        Paths(assets, kind);
        // A walk stops at the bench; a tap hops up onto it (it can be stood on).
        {
            var (w, e) = Make(assets, kind, new PersonMovesSettings(), Top(0.45f, 0.4f));
            var rows = Drive(w, e, assets, _ => Ahead(Buttons.Walk), 1200);
            Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
            Assert.InRange(rows[^1].Feet.X, 11f, 12f);
            int tap = rows.Count;
            var more = Drive(w, e, assets, i => new ActionFrame(new Vector2(0f, 1f), default, i == 0 ? Buttons.Walk | Buttons.Jump : Buttons.Walk, default), 200);
            Assert.Contains(more, r => r.Move == TraversalKind.HopUp);
            output.WriteLine($"a walk: stopped at {F(rows[^1].Feet.X)}; a tap: {string.Join(", ", more.Select(r => r.Move).Distinct())}, on it at {F(more[^1].Feet.Z)} m");
        }
        // The run's step-over at a 0.5 m rail, the sprint vault at the bench and the trough, a run stopped at them.
        foreach (var (what, h, d, gait, held, expect) in new[] { ("a rail 0.5 m", 0.5f, 0.1f, "run", Buttons.None, TraversalKind.StepOver),
                                                                 ("the bench", 0.45f, 0.4f, "sprint", Buttons.Sprint, TraversalKind.Hurdle),
                                                                 ("the trough", 0.6f, 0.7f, "sprint", Buttons.Sprint, TraversalKind.Hurdle),
                                                                 ("the bench", 0.45f, 0.4f, "run", Buttons.None, TraversalKind.None) })
        {
            var (w, e) = Make(assets, kind, null, Top(h, d));
            var rows = Drive(w, e, assets, _ => Ahead(held), 500);
            var moves = rows.Select(r => r.Move).Where(m => m != TraversalKind.None).Distinct().ToList();
            output.WriteLine($"the moves off, {what} at a {gait}: {(moves.Count == 0 ? "stopped" : string.Join(", ", moves))}");
            if (expect == TraversalKind.None) Assert.Empty(moves); else Assert.Equal(expect, moves.Single());
        }
    }

    [Fact]
    public void What_the_low_clips_do_not_serve_keeps_the_sprint_vault_s_stand_in()
    {
        var (assets, kind, _) = Player();
        Paths(assets, kind);
        // A deep top 0.8 m high, 0.5 m deep at a sprint: above the speed vault's band, below the sprint vault's 1.0 m.
        var (w, e) = Make(assets, kind, new PersonMovesSettings(), Top(0.8f, 0.5f));
        var rows = Drive(w, e, assets, _ => Ahead(Buttons.Sprint), 500);
        var moves = rows.Select(r => r.Move).Where(m => m != TraversalKind.None).Distinct().ToList();
        output.WriteLine($"a deep top 0.8 x 0.5 at a sprint: {string.Join(", ", moves)}");
        Assert.Equal(TraversalKind.Hurdle, moves.Single());
    }

    [Fact]
    public void The_speed_vault_s_hand_lies_on_the_real_top()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var c = paths.Clips.First(x => x.Name == "vault_speed");
        var hand = c.Hands[0];
        Assert.Equal(c.Hands.Length, c.HandDrop.Length);
        output.WriteLine($"vault_speed's hands lowered by the IK at their holds: {string.Join(", ", c.HandDrop.Select(x => F(x * 100f, "0.0") + " cm"))}");
        // the same without the IK's lowering, for the hand's pop as the clip's own (the drop zeroed for the while)
        var own = new Dictionary<float, float>();
        var drop = c.HandDrop;
        try
        {
            c.HandDrop = new float[drop.Length];
            foreach (var (h, d) in new[] { (0.45f, 0.4f), (0.6f, 0.7f), (0.64f, 0.5f) }) own[h] = Pop(h, d).Jerk;
        }
        finally { c.HandDrop = drop; }
        (float Step, float Jerk) Pop(float h, float d)
        {
            var (w, e) = Make(assets, kind, new PersonMovesSettings(), Top(h, d));
            var rows = Drive(w, e, assets, _ => Ahead(), 700);
            int start = rows.FindIndex(r => r.Clip == c.Name), end = rows.FindIndex(start, r => r.Clip != c.Name);
            if (end < 0) end = rows.Count;
            Vector3 K(int i) => hand.Left ? rows[i].KnuckleL : rows[i].KnuckleR;
            float step = 0f, jerk = 0f;
            for (int i = start + 1; i < end; i++)
            {
                step = MathF.Max(step, Vector3.Distance(K(i), K(i - 1)));
                if (i > start + 1) jerk = MathF.Max(jerk, ((K(i) - K(i - 1)) - (K(i - 1) - K(i - 2))).Length());
            }
            return (step, jerk);
        }
        foreach (var (h, d) in new[] { (0.45f, 0.4f), (0.6f, 0.7f), (0.64f, 0.5f) })
        {
            var (w, e) = Make(assets, kind, new PersonMovesSettings(), Top(h, d));
            var rows = Drive(w, e, assets, _ => Ahead(), 700);
            int start = rows.FindIndex(r => r.Clip == c.Name);
            Assert.True(start > 0);
            int end = rows.FindIndex(start, r => r.Clip != c.Name);
            if (end < 0) end = rows.Count;
            int at = rows.FindIndex(start, r => r.Clip == c.Name && r.Time >= hand.ContactS);
            Vector3 K(int i) => hand.Left ? rows[i].KnuckleL : rows[i].KnuckleR;
            var k = K(at);
            float lowest = Enumerable.Range(start, end - start).Where(i => rows[i].Time >= hand.ContactS && rows[i].Time <= hand.ReleaseS).Min(i => K(i).Z) - h;
            // the hand's pop: the most the knuckles move in a tick over the vault, and the most their tick-to-tick step changes (a jump on the pace), against the clip's own.
            var (step, jerk) = Pop(h, d);
            output.WriteLine($"the speed vault over {F(h)} x {F(d)}: the {(hand.Left ? "left" : "right")} knuckles at the contact {F((k.X - 12f) * 100f, "0.0")} cm past the face, "
                             + $"{F((k.Z - h) * 100f, "0.0")} cm over the top, at their lowest while down {F(lowest * 100f, "0.0")} cm; the most in a tick {F(step * 100f, "0.00")} cm, "
                             + $"its most change from a tick to the next {F(jerk * 100f, "0.00")} cm (the clip's own hand {F(own[h] * 100f, "0.00")})");
            for (int i = at - 6; i < at + 16; i += 2) output.WriteLine($"   {rows[i].Clip} {F(rows[i].Time, "0.000")} knuckles {F(K(i).X, "0.000")},{F(K(i).Z, "0.000")} feet {F(rows[i].Feet.X, "0.000")},{F(rows[i].Feet.Z, "0.000")}");
            Assert.InRange(k.X - 12f, -0.02f, d);
            // The earlier build's rule on the tops: the hand on the real top at its hold. vault_speed's own lays its left knuckles some 24 cm over its own top at the contact,
            // so the hands' IK lowers the hand by that (OverClip.HandDrop) while it holds: the knuckles at the top, no pop for it.
            Assert.InRange(k.Z - h, -0.01f, 0.04f);
            Assert.InRange(lowest, -0.015f, 0.045f);
            Assert.True(jerk < own[h] + 0.01f, $"the hand pops: {jerk * 100f:0.00} cm change of its step in a tick (the clip's own {own[h] * 100f:0.00})");
        }
    }

    [Fact]
    public void The_low_vaults_hands_in_the_clips_own_pose()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var skeleton = assets.ModelOf(kind).Skeleton;
        var rig = assets.Kind(kind).Person!.Rig;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        float scale = assets.Kind(kind).Scale;
        foreach (var name in new[] { "vault_low", "vault_speed" })
        {
            var c = paths.Clips.First(x => x.Name == name);
            var notes = ((INotedClip)assets.Clip(c.Clip)).Notes.Traverse!;
            foreach (var h in notes.Hands)
            {
                assets.Clip(c.Clip).Sample(h.ContactS, skeleton, local);
                PoseEvaluator.ToModel(skeleton, local, model);
                int tip = h.Hand == "l" ? rig.LeftArm.Tip : rig.RightArm.Tip, wrist = h.Hand == "l" ? rig.LeftArm.End : rig.RightArm.End;
                var (f, u) = c.At(h.ContactS);
                Vector3 At(int b) => new(model[b].Translation.Y * scale, -model[b].Translation.X * scale, model[b].Translation.Z * scale);
                var k = At(tip); var wr = At(wrist);
                output.WriteLine($"{name} the {h.Hand} hand at its contact {F(h.ContactS)} s: the notes' point {F(h.AtForward - f, "0.000")} ahead, {F(h.AtUp - u, "0.000")} up of the capsule "
                                 + $"({F(h.Left, "0.000")} left); the knuckles {F(k.X, "0.000")} ahead, {F(k.Z, "0.000")} up, {F(k.Y, "0.000")} left; the wrist {F(wr.X, "0.000")}, {F(wr.Z, "0.000")}, {F(wr.Y, "0.000")}");
            }
        }
    }
}
