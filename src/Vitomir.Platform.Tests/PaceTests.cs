// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The pace: always run on and off; sprint held, tapped and kept until
/// the body stops, walk is pressed or sprint is tapped again; double-tapped, the body's resting supernatural speed (none
/// for the human), a risk in public whatever the mode; always run on the tape; the run's step-over of thin rails to
/// 0.5 m and the sprint vault over things to 1.0 m by 0.7 m, over and never onto, nothing at a walk; the side of a ramp
/// never vaulted onto; a body following a route starting none of them; and a stair's corner, met at any pace and
/// angle, neither vaulted nor stepped over — only a face rising from the ground the feet stand on is.
/// </summary>
public sealed class PaceTests
{
    static readonly Locomotion Human = Locomotion.Sandbox;

    /// <summary>A body held to the human's paces with a resting speed of its own (a neonate in the masquerade).</summary>
    static readonly Locomotion Fast = Locomotion.Sandbox with { RestingSpeed = 7f };

    static World Flat(out EntityId player, Locomotion? numbers = null, bool alwaysRun = true, ulong seed = 1)
    {
        var w = MovementServices.Register(new WorldBuilder(seed), new TestLevel().Floor(-20, -20, 200, 20).Build()).Build();
        w.Service<TickInput>().AlwaysRun = alwaysRun;
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, numbers ?? Human);
        return w;
    }

    /// <summary>What the presses did, tick by tick: the gait asked for, the events, the body's ground speed at the end.</summary>
    sealed class Log
    {
        public readonly List<Gait> Gaits = [];
        public readonly List<EdgeGuard> Guards = [];
        public readonly List<SprintKept> Kept = [];
        public readonly List<SupernaturalSpeed> Super = [];
        public float Speed;
        public Gait Last => Gaits[^1];
    }

    static ActionFrame F(bool move = true, Buttons held = Buttons.None) => new(move ? new Vector2(0, 1) : Vector2.Zero, default, held, default);

    static Log Run(World w, EntityId p, Log log, int ticks, ActionFrame frame)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, frame);
            log.Gaits.Add(w.Get<MoveIntent>(p).Gait);
            log.Guards.Add(w.Get<MoveIntent>(p).Guard);
            log.Kept.AddRange(w.Events<SprintKept>().ThisTick);
            log.Super.AddRange(w.Events<SupernaturalSpeed>().ThisTick);
        }
        var v = w.Get<Body>(p).Velocity;
        log.Speed = new Vector2(v.X, v.Y).Length();
        return log;
    }

    /// <summary>A tap of sprint while moving: down <paramref name="down"/> ticks, then up.</summary>
    static void Tap(World w, EntityId p, Log log, int down = 8) => Run(w, p, log, down, F(held: Buttons.Sprint));

    // ---------------------------------------------------------------- held, tapped, kept

    [Fact]
    public void Sprint_held_sprints_while_held_and_letting_go_goes_back_to_the_run()
    {
        var w = Flat(out var p);
        var log = new Log();
        Run(w, p, log, 30, F());
        Assert.Equal(Gait.Run, log.Last);
        Run(w, p, log, 90, F(held: Buttons.Sprint));
        Assert.All(log.Gaits.Skip(30), g => Assert.Equal(Gait.Sprint, g));
        Assert.Equal(Human.SprintSpeed, log.Speed, 2);
        Run(w, p, log, 30, F());
        Assert.Equal(Gait.Run, log.Last);
        Assert.Equal(Human.RunSpeed, log.Speed, 2);
        Assert.Empty(log.Kept);
    }

    [Fact]
    public void Sprint_tapped_is_kept_until_the_body_stops_moving()
    {
        var w = Flat(out var p);
        var log = new Log();
        Run(w, p, log, 20, F());
        Tap(w, p, log);
        Run(w, p, log, 240, F());
        Assert.Equal(Gait.Sprint, log.Last);
        Assert.Equal(Human.SprintSpeed, log.Speed, 2);
        Assert.Equal([new SprintKept(p, log.Kept[0].Tick, true, SprintLetGo.None, false, log.Kept[0].Feet)], log.Kept);
        Run(w, p, log, 1, F(move: false));
        Assert.Equal(2, log.Kept.Count);
        Assert.Equal((false, SprintLetGo.Stopped), (log.Kept[1].Kept, log.Kept[1].Why));
        Run(w, p, log, 60, F());
        Assert.Equal(Gait.Run, log.Last);
    }

    [Fact]
    public void A_kept_sprint_ends_when_walk_is_pressed_or_sprint_is_tapped_again()
    {
        var w = Flat(out var p);
        var log = new Log();
        Run(w, p, log, 20, F());
        Tap(w, p, log);
        Run(w, p, log, 60, F());
        Run(w, p, log, 10, F(held: Buttons.Walk));
        Assert.Equal(Gait.Walk, log.Last);
        Assert.Equal((false, SprintLetGo.Walk), (log.Kept[^1].Kept, log.Kept[^1].Why));
        Run(w, p, log, 20, F());
        Assert.Equal(Gait.Run, log.Last);

        Tap(w, p, log);
        Run(w, p, log, 60, F());
        Assert.Equal(Gait.Sprint, log.Last);
        Tap(w, p, log);
        Assert.Equal(Gait.Sprint, log.Last);         // down, it still sprints
        Run(w, p, log, 1, F());
        Assert.Equal(Gait.Run, log.Last);
        Assert.Equal((false, SprintLetGo.Tapped), (log.Kept[^1].Kept, log.Kept[^1].Why));
        Assert.Equal(4, log.Kept.Count);
    }

    [Fact]
    public void A_press_held_past_a_quarter_second_is_a_hold_and_is_not_kept()
    {
        var w = Flat(out var p);
        var log = new Log();
        Run(w, p, log, 20, F());
        Run(w, p, log, Pace.TapTicks + 1, F(held: Buttons.Sprint));
        Run(w, p, log, 1, F());
        Assert.Equal(Gait.Run, log.Last);
        Assert.Empty(log.Kept);
    }

    // ---------------------------------------------------------------- double-tapped: supernatural speed

    [Fact]
    public void A_double_tap_kept_moves_the_body_at_its_resting_speed_until_it_stops()
    {
        var w = Flat(out var p, Fast);
        var log = new Log();
        Run(w, p, log, 20, F());
        Tap(w, p, log);
        Run(w, p, log, 10, F());
        Tap(w, p, log);
        Run(w, p, log, 180, F());
        Assert.Equal(Gait.Supernatural, log.Last);
        Assert.Equal(7f, log.Speed, 2);
        Assert.Single(log.Super);
        Assert.True(log.Super[0].On);
        Assert.Equal(7f, log.Super[0].Speed);
        Run(w, p, log, 1, F(move: false));
        Assert.Equal(2, log.Super.Count);
        Assert.False(log.Super[1].On);
        Assert.Equal((false, SprintLetGo.Stopped, true), (log.Kept[^1].Kept, log.Kept[^1].Why, log.Kept[^1].Supernatural));
        Run(w, p, log, 120, F());
        Assert.Equal(Gait.Run, log.Last);
    }

    [Fact]
    public void A_double_tap_held_is_supernatural_speed_while_held_and_the_run_after()
    {
        var w = Flat(out var p, Fast);
        var log = new Log();
        Run(w, p, log, 20, F());
        Tap(w, p, log);
        Run(w, p, log, 10, F());
        Run(w, p, log, 150, F(held: Buttons.Sprint));
        Assert.Equal(Gait.Supernatural, log.Last);
        Assert.Equal(7f, log.Speed, 2);
        Run(w, p, log, 60, F());
        Assert.Equal(Gait.Run, log.Last);
        Assert.Equal([true, false], log.Super.Select(s => s.On));
        Assert.Equal((false, SprintLetGo.Held), (log.Kept[^1].Kept, log.Kept[^1].Why));
    }

    [Fact]
    public void The_human_s_double_tap_is_a_plain_sprint_and_two_taps_apart_end_the_kept_sprint()
    {
        var w = Flat(out var p);
        var log = new Log();
        Run(w, p, log, 20, F());
        Tap(w, p, log);
        Run(w, p, log, 10, F());
        Tap(w, p, log);
        Run(w, p, log, 120, F());
        Assert.Equal(Gait.Sprint, log.Last);
        Assert.Equal(Human.SprintSpeed, log.Speed, 2);
        Assert.Empty(log.Super);

        var fast = Flat(out var q, Fast);
        var apart = new Log();
        Run(fast, q, apart, 20, F());
        Tap(fast, q, apart);
        Run(fast, q, apart, Pace.DoubleTapTicks, F());   // the second press comes too late for a double tap
        Tap(fast, q, apart);
        Run(fast, q, apart, 10, F());
        Assert.Equal(Gait.Run, apart.Last);
        Assert.Empty(apart.Super);
        Assert.Equal((false, SprintLetGo.Tapped), (apart.Kept[^1].Kept, apart.Kept[^1].Why));
    }

    // ---------------------------------------------------------------- always run

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Always_run_on_runs_and_walk_walks_and_off_the_other_way_round_with_the_guard_at_the_walk(bool alwaysRun)
    {
        var w = Flat(out var p, alwaysRun: alwaysRun);
        var plain = Run(w, p, new Log(), 60, F());
        var walkHeld = Run(w, p, new Log(), 60, F(held: Buttons.Walk));
        var (plainGait, walkGait) = alwaysRun ? (Gait.Run, Gait.Walk) : (Gait.Walk, Gait.Run);
        Assert.Equal(plainGait, plain.Last);
        Assert.Equal(Human.SpeedOf(plainGait), plain.Speed, 2);
        Assert.Equal(walkGait, walkHeld.Last);
        Assert.Equal(Human.SpeedOf(walkGait), walkHeld.Speed, 2);
        // The edge guard stays at the walk, whichever key makes it.
        Assert.Equal(plainGait == Gait.Walk ? EdgeGuard.SafeFall : EdgeGuard.None, plain.Guards[^1]);
        Assert.Equal(walkGait == Gait.Walk ? EdgeGuard.SafeFall : EdgeGuard.None, walkHeld.Guards[^1]);
        Assert.Equal(Gait.Sprint, Run(w, p, new Log(), 10, F(held: Buttons.Sprint | Buttons.Walk)).Last);
    }

    /// <summary>Walking and running by turns, with sprint tapped, held and double-tapped now and then: the same frames every time.</summary>
    static ActionFrame Script(int t)
    {
        var held = (t / 200 % 2 == 1 ? Buttons.Walk : Buttons.None)
                 | (t % 300 is >= 100 and < 108 or >= 118 and < 126 ? Buttons.Sprint : Buttons.None)
                 | (t % 500 is >= 400 and < 460 ? Buttons.Sprint : Buttons.None);
        return new ActionFrame(new Vector2(t / 250 % 2 == 0 ? 0.2f : -0.3f, t % 700 < 650 ? 1f : 0f), new Vector2(t / 160 % 2 == 0 ? 0.01f : -0.012f, 0f), held, default);
    }

    [Fact]
    public void Always_run_rides_on_the_tape_and_a_replay_set_from_it_agrees_digest_for_digest()
    {
        var w = Flat(out _, Fast, alwaysRun: false, seed: 9);
        var tape = new Tape { Header = new TapeHeader { Level = "flat", Seed = 9, Notes = new() { [TickInput.AlwaysRunNote] = "false" } } };
        for (int t = 1; t <= 900; t++)
        {
            tape.Frames.Add(Script(t));
            MovementServices.Step(w, Script(t));
            if (t % Tape.DigestEvery == 0) tape.Digests[t] = w.Digest();
        }
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-pace-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            Assert.False(TickInput.AlwaysRunFrom(back.Header.Notes));
            Assert.True(TickInput.AlwaysRunFrom(new Dictionary<string, string>()));   // an older tape ran so
            var replay = Flat(out _, Fast, alwaysRun: TickInput.AlwaysRunFrom(back.Header.Notes), seed: back.Header.Seed);
            var other = Flat(out _, Fast, alwaysRun: true, seed: back.Header.Seed);
            bool parted = false;
            for (int t = 1; t <= back.Frames.Count; t++)
            {
                MovementServices.Step(replay, back.Frames[t - 1]);
                MovementServices.Step(other, back.Frames[t - 1]);
                if (!back.Digests.TryGetValue(t, out ulong d)) continue;
                Assert.True(d == replay.Digest(), $"the replay parted at tick {t}");
                parted |= d != other.Digest();
            }
            Assert.True(parted, "a replay always running should part from a recording made walking");
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------- the masquerade

    [Fact]
    public void Supernatural_speed_in_the_masquerade_in_public_is_a_speed_risk_and_the_held_sprint_is_not()
    {
        var b = new WorldBuilder(3);
        MovementServices.Register(b, new TestLevel().Floor(-20, -20, 300, 20).Build());
        MasqueradeServices.Register(b);
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        var neonate = Locomotion.Sandbox with { RunSpeed = 3.5f, SprintSpeed = 7f, RestingSpeed = 7f };
        profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int n = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/neonate"), "a neonate", neonate, true));
        var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Human);
        profiles.Give(w, p, n);

        var risks = new List<MasqueradeRisk>();
        void Go(int ticks, ActionFrame frame)
        {
            for (int i = 0; i < ticks; i++) { MovementServices.Step(w, frame); risks.AddRange(w.Events<MasqueradeRisk>().ThisTick); }
        }
        Go(150, F(held: Buttons.Sprint));
        Assert.Equal(MasqueradeMode.Masquerade, w.Get<MasqueradeState>(p).Mode);
        Assert.Equal((5.5f, 7f), (w.Get<Locomotion>(p).SprintSpeed, w.Get<Locomotion>(p).RestingSpeed));   // held, the resting speed kept
        Assert.Empty(risks);
        Go(20, F());
        Go(8, F(held: Buttons.Sprint)); Go(10, F()); Go(8, F(held: Buttons.Sprint));
        Go(180, F());
        var speed = Assert.Single(risks);
        Assert.Equal(MasqueradeAct.Speed, speed.Act);
        Assert.Equal(7f, speed.Measured, 2);
    }

    // ---------------------------------------------------------------- the step-over and the sprint vault

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public readonly Dictionary<TraversalKind, int> Ticks = [];
        public float Highest = float.MinValue, StoodHighest = float.MinValue;
        public Body End;
        public override string ToString() => $"started [{string.Join(", ", Started)}], ended at {End.Feet} ({End.State}), highest {Highest:0.00}, stood at {StoodHighest:0.00}";
    }

    static Drove Go(LevelSpace level, Vector3 from, int ticks, Func<Body, MoveIntent> intent)
    {
        var d = new Drove();
        var move = default(Traversal);
        d.End = Drive.Moves(level, Drive.Settle(level, from), ref move, ticks, (_, b) => intent(b), (_, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (m.Kind != TraversalKind.None) d.Ticks[m.Kind] = d.Ticks.GetValueOrDefault(m.Kind) + 1;
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
            if (b.State == BodyState.Ground) d.StoodHighest = MathF.Max(d.StoodHighest, b.Feet.Z);
        });
        return d;
    }

    /// <summary>A thing <paramref name="height"/> high and <paramref name="depth"/> deep across the way, its near face at x 3.</summary>
    static LevelSpace Thing(float height, float depth) =>
        new TestLevel().Floor(-10, -10, 20, 10).Box(new(3, -3, 0), new(3 + depth, 3, height)).Build();

    static Func<Body, MoveIntent> Along(Gait gait) => b => b.Feet.X > 10f ? new MoveIntent { Gait = gait, Facing = 0f, Faces = true } : Drive.Toward(1, 0, gait);

    /// <summary>
    /// An open-sided stair along +X from x 3, as the proving ground's drops station's way up: risers 0.2 m, treads
    /// 0.3 m, 2 m wide (y −1 to 1), fifteen steps to 3 m.
    /// </summary>
    static LevelSpace OpenStair()
    {
        var t = new TestLevel().Floor(-10, -10, 20, 10);
        for (int k = 0; k < 15; k++) t.Box(new(3 + k * 0.3f, -1, 0), new(3 + (k + 1) * 0.3f, 1, (k + 1) * 0.2f));
        return t.Build("open-stair");
    }

    [Fact]
    public void A_stair_s_foot_and_corner_met_at_a_run_or_a_sprint_at_any_angle_is_walked_in_stride_never_vaulted_or_stepped_over()
    {
        // A walk in play: sprinting diagonally across the foot of the drops station's way up, the body vaulted the
        // stair's corner (the second riser, 0.4 m above the floor, its tread 0.3 m deep, the floor beyond the open side).
        // The second riser rises from the first tread, within the stride: a stair, walked in stride.
        var level = OpenStair();
        var seen = new List<string>();
        int runs = 0;
        foreach (var gait in new[] { Gait.Run, Gait.Sprint })
            foreach (float deg in new[] { 0f, 30f, 45f, 60f, -30f, -45f, -60f })
                foreach (float aim in new[] { -1.3f, -0.9f, -0.5f, 0f, 0.5f, 0.9f, 1.3f })
                {
                    var (sin, cos) = DetMath.SinCos(deg * DetMath.DegToRad);
                    var start = new Vector2(3f, aim) - new Vector2(cos, sin) * 5f;
                    var d = Go(level, new(start, 0.01f), 240, _ => Drive.Toward(cos, sin, gait));
                    runs++;
                    foreach (var k in d.Started)
                        if (k is TraversalKind.StepOver or TraversalKind.Hurdle) seen.Add($"{gait} at {deg}° aimed at y {aim}: {k} ({d})");
                }
        Assert.True(seen.Count == 0, $"{seen.Count} of {runs}: " + string.Join("; ", seen));
    }

    [Theory]
    [InlineData(0.40f, Gait.Run, TraversalKind.StepOver)]
    [InlineData(0.50f, Gait.Run, TraversalKind.StepOver)]
    [InlineData(0.55f, Gait.Run, TraversalKind.None)]       // above the step-over's 0.5 m: a stop
    [InlineData(0.40f, Gait.Walk, TraversalKind.None)]      // at a walk: a stop, as ever
    [InlineData(0.50f, Gait.Sprint, TraversalKind.Hurdle)]  // at a sprint: the vault
    [InlineData(0.55f, Gait.Sprint, TraversalKind.Hurdle)]
    public void A_thin_rail_is_stepped_over_at_a_run_to_half_a_metre_vaulted_at_a_sprint_and_stops_a_walk(float height, Gait gait, TraversalKind want)
    {
        var d = Go(Thing(height, 0.1f), new(-6, 0, 0.01f), 600, Along(gait));
        if (want == TraversalKind.None)
        {
            Assert.True(d.Started.Count == 0 && d.End.Feet.X < 3f - 0.25f && d.Highest < 0.05f, d.ToString());
            return;
        }
        Assert.True(d.Started.SequenceEqual([want]) && d.End.Feet.X > 3.4f && d.End.Feet.Z < 0.02f, d.ToString());
        Assert.True(d.StoodHighest < 0.02f, $"stood on it: {d}");                     // over, never onto
        Assert.True(d.Highest > height, d.ToString());
        if (want == TraversalKind.StepOver) Assert.InRange(d.Ticks[want], 42, 60);    // about 0.35 s, at the run's pace
    }

    [Theory]
    [InlineData(0.45f, 0.4f, true)]    // a bench
    [InlineData(0.60f, 0.6f, true)]    // a trough
    [InlineData(1.00f, 0.1f, true)]    // a low fence
    [InlineData(0.80f, 0.8f, false)]   // a block deeper than 0.7 m
    public void A_sprint_vaults_a_bench_a_trough_and_a_fence_never_onto_and_not_a_deeper_block_and_a_run_stops_at_them(float height, float depth, bool vaults)
    {
        var level = Thing(height, depth);
        var d = Go(level, new(-6, 0, 0.01f), 600, Along(Gait.Sprint));
        if (vaults)
        {
            Assert.True(d.Started.SequenceEqual([TraversalKind.Hurdle]) && d.End.Feet.X > 3 + depth + 0.3f && d.End.Feet.Z < 0.02f, d.ToString());
            Assert.True(d.StoodHighest < 0.02f, $"stood on it: {d}");
        }
        else Assert.True(d.Started.Count == 0 && d.End.Feet.X < 2.75f, d.ToString());
        var run = Go(level, new(-6, 0, 0.01f), 600, Along(Gait.Run));
        Assert.True(run.Started.Count == 0 && run.End.Feet.X < 2.75f, run.ToString());
    }

    [Fact]
    public void Supernatural_speed_vaults_as_a_sprint_does()
    {
        var level = Thing(0.45f, 0.4f);
        var move = default(Traversal);
        var started = new List<TraversalKind>();
        var end = Drive.Moves(level, Drive.Settle(level, new(-6, 0, 0.01f)), ref move, 400, (_, b) => Along(Gait.Supernatural)(b),
                              (_, _, _, o) => { if (o.MoveStarted != TraversalKind.None) started.Add(o.MoveStarted); }, Fast);
        Assert.Equal([TraversalKind.Hurdle], started);
        Assert.True(end.Feet.X > 3.7f && end.Feet.Z < 0.02f, end.Feet.ToString());
    }

    [Fact]
    public void The_side_of_a_40_degree_ramp_met_at_a_slant_reads_thin_with_ground_beyond_yet_is_never_vaulted_onto()
    {
        // The finding: a sprint into the side of a 40° ramp hurdled onto it, landing 0.5 m up. The ramp rises along +X
        // from x 0; its side at y −1 is met going +Y and a little −X, where the top falls along the way.
        var level = new TestLevel().Floor(-10, -10, 10, 10).Ramp(0f, 4f, -1f, 1f, 40f).Build();
        var way = Vector2.Normalize(new Vector2(-0.6f, 1f));
        int took = 0;
        for (float x = 0.4f; x <= 2.0f; x += 0.1f)
        {
            var feet = new Vector3(x, -1.6f, 0f);
            if (!TraversalProbe.Read(level.Collision, feet, way, Human, Human.Radius + 0.1f + Human.SprintSpeed * BodyMotor.HurdleLead, 1.25f, out var ob)) continue;
            // What the first hurdle took: a top above the stride to a metre, thin, ground beyond within half a metre of the feet.
            if (ob.HasTop && ob.Top - feet.Z is > 0.35f and <= 1f && ob.Depth < Human.MantleMinDepth && ob.Beyond && MathF.Abs(ob.Over.Z - feet.Z) <= 0.5f) took++;
            Assert.False(BodyMotor.SprintVaultAhead(level.Collision, feet, way, Human.SprintSpeed, Human, out _, out var to), $"vaulted onto the ramp at x {x:0.0}, to {to}");
            Assert.False(BodyMotor.StepOverAhead(level.Collision, feet, way, Human.RunSpeed, Human, out _, out to), $"stepped onto the ramp at x {x:0.0}, to {to}");
        }
        Assert.True(took > 0, "the ramp's side should read as the hurdle took it somewhere");
    }

    [Fact]
    public void A_body_following_a_route_at_a_sprint_vaults_no_bench_and_steps_over_no_rail()
    {
        foreach (var (height, depth) in new[] { (0.45f, 0.4f), (0.45f, 0.1f) })
        {
            var level = Thing(height, depth);
            var corner = new Vector3(8, 0, 0);
            foreach (var gait in new[] { Gait.Sprint, Gait.Run })
            {
                var d = Go(level, new(-6, 0, 0.01f), 600, b => RouteSteer.Toward(b.Feet, corner, gait));
                Assert.True(BodyMotor.FollowsRoute(RouteSteer.Toward(Vector3.Zero, corner, gait)));
                Assert.True(d.Started.Count == 0 && d.End.Feet.X < 2.75f && d.Highest < 0.05f, $"{gait} into {height} x {depth}: {d}");
            }
        }
    }
}
