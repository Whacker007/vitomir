// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The scenario director (<see cref="ScenarioDirector"/>, Think 96): each step writes the intent a player's buttons or the
/// brain write, and the platform's rules admit or refuse it as for anyone — Christof (broadsword) at the origin facing +X and the
/// guard (rudolfGuard) in front of him facing back, on a flat floor, contact and directional attacks on, a duelling brain (or the
/// standing dummy) on each as the arena gives one. The program's JSON round-trips; a scripted world replays digest for digest; the
/// director allocates nothing per tick. Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class ScenarioDirectorTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    internal sealed class Run
    {
        public required World W { get; init; }
        public required EntityId A { get; init; }
        public required EntityId B { get; init; }
        public ScenarioDirector? D { get; init; }
        public readonly List<StepBegan> Began = [];
        public readonly List<StepEnded> Ended = [];
        public readonly List<StepTimedOut> TimedOut = [];
        public readonly List<StepRefused> Refused = [];
        public readonly List<StepLate> Late = [];
        public readonly List<ControlChanged> Control = [];
        public readonly List<Marker> Markers = [];
        public readonly List<TriggerFired> Fired = [];
        public readonly List<ScenarioEnded> Over = [];
        public readonly List<AimedBlow> Aimed = [];
        public readonly List<AttackBegun> Attacks = [];
        public readonly List<AttackRefused> AttackRefusals = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<BlockRaised> Raised = [];
        public readonly List<BlockLowered> Lowered = [];
        public readonly List<SwingCancelled> Cancelled = [];
        public readonly List<DodgeBegun> Dodges = [];
        public readonly List<ulong> Digests = [];
        public readonly List<Vector3> FeetA = [], FeetB = [];
        public readonly List<float> YawA = [];
        public readonly List<CombatIntent> IntentA = [];

        public void Go(float seconds)
        {
            int ticks = (int)MathF.Round(seconds * World.TicksPerSecond);
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(W, ActionFrame.Idle);
                Take(Began); Take(Ended); Take(TimedOut); Take(Refused); Take(Late); Take(Control); Take(Markers); Take(Fired); Take(Over);
                Take(Aimed); Take(Attacks); Take(AttackRefusals); Take(Strikes); Take(Raised); Take(Lowered); Take(Cancelled); Take(Dodges);
                Digests.Add(W.Digest());
                FeetA.Add(W.Get<Body>(A).Feet);
                FeetB.Add(W.Get<Body>(B).Feet);
                YawA.Add(W.Get<Body>(A).Yaw);
                IntentA.Add(W.Get<CombatIntent>(A));
            }
        }

        void Take<T>(List<T> into) where T : struct
        {
            if (W.TryService<EventQueue<T>>(out var q)) into.AddRange(q.ThisTick);
        }

        public float Gap() => Vector2.Distance(Flat(W.Get<Body>(A).Feet), Flat(W.Get<Body>(B).Feet));

        public long TickOf(string mark) => Markers.First(m => m.Text == mark).Tick;
    }

    static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    /// <summary>
    /// Christof at the origin facing +X, the guard <paramref name="distance"/> m along +X facing him, each with the arena's brain
    /// (<paramref name="a"/>, <paramref name="b"/>); the director with <paramref name="program"/> bound to "Christof" and "Guard".
    /// </summary>
    internal static Run Make(ScenarioProgram? program, BrainKind a = BrainKind.Stand, BrainKind b = BrainKind.Stand, float distance = 4f,
        DuelBrainSettings? aNumbers = null, DuelBrainSettings? bNumbers = null, ulong seed = 7, Action<WorldBuilder>? register = null,
        Action<World, EntityId, EntityId>? setup = null)
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var builder = new WorldBuilder(seed);
        MovementServices.Register(builder, level);
        MasqueradeServices.Register(builder, new MasqueradeSettings { AutoSwitch = false });
        builder.Animation(assets);
        builder.HitVolumes();
        BodyMotionSystem.Register(builder);
        CombatServices.Register(builder);
        ContactServices.Register(builder, new ContactConfig());
        DuelBrains.Register(builder);
        GuardSkills.Register(builder);
        builder.Service(new DirectionalAttacks { Enabled = true, Settings = new DirectionSettings() });
        register?.Invoke(builder);
        if (program is not null) ScenarioDirectors.Register(builder, program);
        var w = builder.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var ea = MovementServices.SpawnBody(w, new(0f, 0f, 0.01f), 0f, Locomotion.Sandbox);
        var eb = MovementServices.SpawnBody(w, new(distance, 0f, 0.01f), DetMath.Pi, Locomotion.Sandbox);
        foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        var book = w.Service<DuelBrainBook>();
        float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
        w.Set(ea, DuelBrain.For(a, eb, book.Add(aNumbers ?? new DuelBrainSettings()), ra, rb));
        w.Set(eb, DuelBrain.For(b, ea, book.Add(bNumbers ?? new DuelBrainSettings()), rb, ra));
        setup?.Invoke(w, ea, eb);
        ScenarioDirector? d = null;
        if (program is not null)
        {
            d = w.Service<ScenarioDirector>();
            d.Bind(w, ["Christof", "Guard"], [ea, eb]);
        }
        return new Run { W = w, A = ea, B = eb, D = d };
    }

    static ActorControl Script(string actor, params ScenarioStep[] steps) =>
        new() { Actor = actor, Tracks = [[.. steps]], Then = ScenarioThen.Stand, Foe = actor == "Christof" ? "Guard" : "Christof" };

    static ScenarioProgram Program(params ActorControl[] actors) => new() { Actors = [.. actors] };

    static readonly DuelBrainSettings Calm = new() { BackOffChance = 0f, BlockChance = 0f, ParryChance = 0f, DodgeChance = 0f };

    // ---- the program ----

    [Fact]
    public void A_program_round_trips_through_its_json_with_every_step_kind()
    {
        var steps = new ScenarioStep[]
        {
            new WaitStep { Seconds = 0.5f, Label = "w" }, new StandStep { Seconds = 1f }, new FaceStep { Toward = new() { Actor = "Guard" } },
            new MoveToStep { To = new() { Actor = "Guard", DistanceMetres = 1.3f }, Gait = Gait.Run, FaceWhileMoving = "Guard", WithinMetres = 0.1f, ForSeconds = 6f },
            new KeepDistanceStep { From = "Guard", Metres = 2f, ToleranceMetres = 0.1f, ForSeconds = 0.5f },
            new RetreatStep { From = "Guard", Metres = 6f, Gait = Gait.Run }, new StrafeStep { Side = StepSide.Right, ForSeconds = 1f },
            new CircleStep { Around = "Guard", RadiusMetres = 3f, Direction = StepSide.Left }, new PathStep { Points = [new(1, 2), new(3, 4, 0.5f)], Loop = true },
            new AttackStep { Direction = AttackDirection.Overhead, HoldSeconds = 0.4f, AimAt = new() { Region = AimRegion.UpperArm, Side = StepSide.Left }, AtSeconds = 8f },
            new AttackStep { Strength = AttackStrength.Strong, WaitReady = false, AimAt = new() { PitchDegrees = 20f, YawDegrees = -10f } },
            new BlockStep { Until = new EventSelector("StrikeMoment", By: "Guard") }, new ParryStep { Against = "Guard", LeadSeconds = 0.2f },
            new CancelStep(), new DodgeStep { Way = DodgeWay.Left }, new UsePowerStep { Power = "celerity" }, new UsePowerStep { Slot = 3 },
            new EquipStep { What = EquipWhat.Ranged }, new AimStep { At = new() { Actor = "Guard", Region = AimRegion.Chest } }, new DrawStep { ToShare = 0.8f },
            new LooseStep(), new CancelDrawStep(), new MarkStep { Text = "m" },
            new RepeatStep { UntilStop = true, Steps = [new AttackStep { Direction = AttackDirection.Thrust }, new WaitStep { Seconds = 0.6f }] },
            new HandOverStep { On = new EventSelector("AttackBegun", "Guard", Where: [new WhereFilter("direction", ["overhead", "left"])], FromMarker: "m") , DelaySeconds = 0.2f },
            new EndStep { Reason = "done" },
        };
        var program = new ScenarioProgram
        {
            Actors = [new ActorControl { Actor = "Christof", Ai = ScenarioAi.Duel, Mute = ControlChannels.Attack | ControlChannels.Move, Tracks = [[.. steps], [new WaitStep { Seconds = 1f }]], Then = ScenarioThen.End, Foe = "Guard" }],
            Triggers =
            [
                new ScenarioTrigger { Label = "near", When = new() { Value = new ValueCondition("Guard.distanceTo.Christof", new Comparison(CompareOp.AtMost, 4)) }, Do = [new TriggerAction { Actor = "Christof", Step = new RetreatStep { From = "Guard", Metres = 6f } }] },
                new ScenarioTrigger { When = new() { Count = new CountCondition(new EventSelector("Marker", "Christof"), new Comparison(CompareOp.Between, 2, 3)) }, Do = [new TriggerAction { End = "enough" }], Repeat = true, CooldownSeconds = 1f },
            ],
        };
        string text = program.Write();
        var back = ScenarioProgram.Read(text);
        Assert.Equal(text, back.Write());
        var track = back.Actors[0].Tracks[0];
        Assert.Equal(steps.Length, track.Count);
        for (int i = 0; i < steps.Length; i++) Assert.Equal(steps[i].Kind, track[i].Kind);
        Assert.Contains("\"step\":\"moveTo\"", text);
        Assert.Equal(AimRegion.UpperArm, ((AttackStep)track[9]).AimAt!.Region);
        Assert.Equal(ControlChannels.Attack | ControlChannels.Move, back.Actors[0].Mute);
        Assert.Equal(CompareOp.Between, back.Triggers[1].When.Count!.Compare.Op);
        Assert.Throws<ScenarioProgramException>(() => ScenarioProgram.Read("{\"Actors\":[{\"Actor\":\"A\",\"Tracks\":[[{\"step\":\"fly\"}]]}]}"));
    }

    [Fact]
    public void Binding_refuses_by_name_an_actor_an_event_a_field_or_an_observable_the_world_does_not_have()
    {
        Assert.Contains("no actor is named Nobody", Assert.Throws<ScenarioProgramException>(() =>
            Make(Program(Script("Christof", new FaceStep { Toward = new() { Actor = "Nobody" } })))).Message);
        Assert.Contains("no event is called Explosion", Assert.Throws<ScenarioProgramException>(() =>
            Make(Program(Script("Christof", new MarkStep { Text = "x", On = new EventSelector("Explosion") })))).Message);
        Assert.Contains("cannot read \"colour\"", Assert.Throws<ScenarioProgramException>(() =>
            Make(Program(Script("Christof", new MarkStep { Text = "x", On = new EventSelector("AttackBegun", Where: [new WhereFilter("colour", ["red"])]) })))).Message);
        Assert.Contains("\"sideways\" is not a AttackDirection", Assert.Throws<ScenarioProgramException>(() =>
            Make(Program(Script("Christof", new MarkStep { Text = "x", On = new EventSelector("AttackBegun", Where: [new WhereFilter("direction", ["sideways"])]) })))).Message);
        var bad = new ScenarioProgram
        {
            Actors = [Script("Christof", new WaitStep { Seconds = 1 })],
            Triggers = [new ScenarioTrigger { When = new() { Value = new ValueCondition("Christof.wisdom", new Comparison(CompareOp.Above, 1)) } }],
        };
        Assert.Contains("not an observable a trigger can read", Assert.Throws<ScenarioProgramException>(() => Make(bad)).Message);
    }

    // ---- timing, marks, repeats, ids ----

    [Fact]
    public void Steps_run_in_order_instant_ones_in_the_same_tick_with_ids_parents_and_iterations()
    {
        var run = Make(Program(Script("Christof",
            new WaitStep { Seconds = 0.5f },
            new MarkStep { Text = "a" },
            new RepeatStep { Count = 2, Label = "twice", Steps = [new MarkStep { Text = "b" }, new WaitStep { Seconds = 0.25f }] },
            new MarkStep { Text = "c" },
            new EndStep { Reason = "over" })));
        run.Go(2f);
        Assert.Equal(60, run.TickOf("a"));
        Assert.Equal([60L, 90L], run.Markers.Where(m => m.Text == "b").Select(m => m.Tick));
        Assert.Equal(120, run.TickOf("c"));
        var over = Assert.Single(run.Over);
        Assert.Equal((120L, "over"), (over.Tick, over.Reason));
        Assert.True(run.D!.Ended);
        // Every running of a step has its own id; a repeat's steps name it as their parent and count its rounds.
        Assert.Equal(run.Began.Count, run.Began.Select(b => b.Instance).Distinct().Count());
        var repeat = run.Began.Single(b => b.Kind == ScenarioStepKind.Repeat);
        var inner = run.Began.Where(b => b.Kind == ScenarioStepKind.Mark && b.Tick is 60 or 90 && b.Parent == repeat.Instance).ToList();
        Assert.Equal([0, 1], inner.Select(b => b.Iteration));
        Assert.All(run.Ended.Where(e => e.Kind != ScenarioStepKind.Repeat), e => Assert.Contains(run.Began, b => b.Instance == e.Instance && b.Step == e.Step));
        Assert.Equal(StepOutcome.Done, run.Ended.Single(e => e.Kind == ScenarioStepKind.Repeat).Outcome);
    }

    [Fact]
    public void At_after_and_on_start_a_step_when_they_say_and_a_late_one_says_so()
    {
        var program = new ScenarioProgram
        {
            Actors =
            [
                new ActorControl { Actor = "Christof", Foe = "Guard", Then = ScenarioThen.Stand, Tracks =
                [
                    [new WaitStep { Seconds = 1f }, new MarkStep { Text = "late", AtSeconds = 0.5f }, new MarkStep { Text = "at2", AtSeconds = 2f }],
                    [new MarkStep { Text = "x" }, new MarkStep { Text = "y", AfterSeconds = 0.25f }],
                    [new MarkStep { Text = "go", On = new EventSelector("AttackBegun", By: "Guard"), DelaySeconds = 0.1f }],
                ] },
                Script("Guard", new AttackStep { Direction = AttackDirection.Right, AtSeconds = 0.5f }),
            ],
        };
        var run = Make(program, distance: 3f);
        run.Go(2.5f);
        Assert.Equal(120, run.TickOf("late"));
        var late = Assert.Single(run.Late);
        Assert.Equal(0.5f, late.LateSeconds, 3);
        Assert.Equal(240, run.TickOf("at2"));
        Assert.Equal(run.TickOf("x") + 30, run.TickOf("y"));
        var swing = run.Attacks.First(a => a.Entity == run.B);
        Assert.Equal(swing.Tick + 1 + 12, run.TickOf("go"));
    }

    // ---- moving ----

    [Fact]
    public void Stand_holds_a_duelling_brain_still_and_then_hands_it_every_channel()
    {
        var program = Program(new ActorControl { Actor = "Christof", Ai = ScenarioAi.Duel, Foe = "Guard", Tracks = [[new StandStep { Seconds = 1f }]], Then = ScenarioThen.Ai });
        var run = Make(program, a: BrainKind.Duel, aNumbers: Calm);
        run.Go(1f);
        Assert.InRange(run.Gap(), 3.95f, 4.05f);
        run.Go(3f);
        Assert.True(run.Gap() < 2.5f, $"the brain did not close after the hand-over: {run.Gap():0.00} m");
        Assert.Equal(ControlChannels.All, run.Control[0].Script);
        var last = run.Control[^1];
        Assert.Equal((ControlChannels.None, ControlChannels.All), (last.Script, last.Ai));
    }

    [Fact]
    public void Face_turns_the_body_to_a_bearing_and_ends_when_faced()
    {
        var run = Make(Program(Script("Christof", new FaceStep { Toward = new() { Degrees = 90f } }, new MarkStep { Text = "faced" })));
        run.Go(1f);
        Assert.InRange(run.YawA[^1] * DetMath.RadToDeg, 88f, 92f);
        Assert.True(run.TickOf("faced") < 30, $"faced at tick {run.TickOf("faced")}");
        Assert.Equal(StepOutcome.Done, run.Ended.First(e => e.Kind == ScenarioStepKind.Face).Outcome);
    }

    [Fact]
    public void MoveTo_walks_to_a_point_and_to_a_distance_from_an_actor_facing_it()
    {
        var run = Make(Program(Script("Christof", new MoveToStep { To = new() { Point = new(1.5f, 1.5f) }, WithinMetres = 0.2f, ForSeconds = 8f }, new MarkStep { Text = "there" })));
        run.Go(6f);
        Assert.True(run.Markers.Any(m => m.Text == "there"), "never arrived");
        Assert.True(Vector2.Distance(Flat(run.FeetA[^1]), new(1.5f, 1.5f)) <= 0.3f, $"at {run.FeetA[^1]}");

        var close = Make(Program(Script("Christof", new MoveToStep { To = new() { Actor = "Guard", DistanceMetres = 1.3f }, FaceWhileMoving = "Guard", WithinMetres = 0.1f, ForSeconds = 8f })));
        close.Go(5f);
        Assert.InRange(close.Gap(), 1.15f, 1.45f);
        Assert.InRange(MathF.Abs(close.YawA[^1]) * DetMath.RadToDeg, 0f, 3f);
        Assert.Equal(StepOutcome.Done, close.Ended.Single(e => e.Kind == ScenarioStepKind.MoveTo).Outcome);
    }

    [Fact]
    public void MoveTo_that_cannot_arrive_in_its_time_times_out_and_says_so()
    {
        var run = Make(Program(Script("Christof", new MoveToStep { To = new() { Point = new(-30f, 0f) }, ForSeconds = 1f })));
        run.Go(1.5f);
        var t = Assert.Single(run.TimedOut);
        Assert.Equal(("not arrived", 120L), (t.Why, t.Tick));
        Assert.Equal(StepOutcome.TimedOut, run.Ended.Single().Outcome);
    }

    [Fact]
    public void KeepDistance_holds_a_distance_from_an_actor_and_retreat_backs_away_facing_it()
    {
        var run = Make(Program(Script("Christof", new KeepDistanceStep { From = "Guard", Metres = 2f, ToleranceMetres = 0.2f, ForSeconds = 4f })));
        run.Go(3f);
        Assert.InRange(run.Gap(), 1.7f, 2.3f);
        run.Go(1.5f);
        Assert.Equal(StepOutcome.Done, run.Ended.Single().Outcome);

        var back = Make(Program(Script("Christof", new RetreatStep { From = "Guard", Metres = 4f, ForSeconds = 6f })), distance: 2f);
        back.Go(5f);
        Assert.True(back.Gap() >= 3.9f, $"gap {back.Gap():0.00}");
        Assert.InRange(MathF.Abs(back.YawA[^1]) * DetMath.RadToDeg, 0f, 3f);   // faced the guard while backing away
        Assert.Equal(StepOutcome.Done, back.Ended.Single().Outcome);
    }

    [Fact]
    public void Strafe_goes_sideways_and_circle_goes_round_at_its_radius_both_facing_the_actor()
    {
        var run = Make(Program(Script("Christof", new StrafeStep { Side = StepSide.Left, Facing = "Guard", ForSeconds = 1.5f })));
        run.Go(1.6f);
        Assert.True(run.FeetA[^1].Y > 0.5f, $"left of facing +X is +Y: {run.FeetA[^1]}");
        float toGuard = DetMath.Atan2(run.FeetB[^1].Y - run.FeetA[^1].Y, run.FeetB[^1].X - run.FeetA[^1].X);
        Assert.InRange(MathF.Abs(DetMath.WrapAngle(run.YawA[^1] - toGuard)) * DetMath.RadToDeg, 0f, 3f);

        var round = Make(Program(Script("Christof", new CircleStep { Around = "Guard", RadiusMetres = 3f, Direction = StepSide.Right, ForSeconds = 4f })), distance: 3f);
        round.Go(4f);
        for (int i = 60; i < round.FeetA.Count; i++)
            Assert.InRange(Vector2.Distance(Flat(round.FeetA[i]), Flat(round.FeetB[i])), 2.5f, 3.5f);
        float from = DetMath.Atan2(-round.FeetB[0].Y, -round.FeetB[0].X), to = DetMath.Atan2(round.FeetA[^1].Y - round.FeetB[^1].Y, round.FeetA[^1].X - round.FeetB[^1].X);
        Assert.True(MathF.Abs(DetMath.WrapAngle(to - from)) * DetMath.RadToDeg > 30f, "did not go round");
    }

    [Fact]
    public void Path_goes_through_its_waypoints_in_order()
    {
        var run = Make(Program(Script("Christof", new PathStep { Points = [new(1f, 1.5f), new(2.5f, -1f)], WithinMetres = 0.25f, ForSeconds = 10f })), distance: 6f);
        run.Go(6f);
        Assert.Equal(StepOutcome.Done, run.Ended.Single().Outcome);
        Assert.Contains(run.FeetA, f => Vector2.Distance(Flat(f), new(1f, 1.5f)) <= 0.35f);
        Assert.True(Vector2.Distance(Flat(run.FeetA[^1]), new(2.5f, -1f)) <= 0.35f, $"at {run.FeetA[^1]}");
    }

    // ---- fighting ----

    [Fact]
    public void Attack_presses_from_its_direction_holds_and_releases_and_names_the_swing_it_aimed()
    {
        var run = Make(Program(Script("Christof", new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.3f, AimAt = new() { Region = AimRegion.Chest } })), distance: 1.5f);
        run.Go(2f);
        var begun = Assert.Single(run.Attacks, a => a.Entity == run.A);
        Assert.Equal(AttackDirection.Right, begun.Direction);
        int held = run.IntentA.Count(i => i.Attack);
        Assert.Equal(36, held);
        var aimed = Assert.Single(run.Aimed);
        Assert.Equal((begun.Swing, BodyRegion.Chest, "chest", run.B), (aimed.Swing, aimed.Region, aimed.Asked, aimed.Target));
        Assert.Equal(run.Began.Single().Instance, aimed.Instance);
        Assert.Equal(StepOutcome.Done, run.Ended.Single().Outcome);
    }

    [Fact]
    public void Blows_in_a_row_wait_until_the_body_may_swing_again()
    {
        var run = Make(Program(Script("Christof",
            new AttackStep { Direction = AttackDirection.Right }, new AttackStep { Direction = AttackDirection.Left }, new AttackStep { Direction = AttackDirection.Overhead })), distance: 1.5f);
        run.Go(6f);
        var mine = run.Attacks.Where(a => a.Entity == run.A).ToList();
        Assert.Equal(3, mine.Count);
        // The director wrote each step's direction at its press; the rules resolve one the body has no blow for (here the
        // overhead: no module gives these test bodies one) to the right, as for any fighter.
        var presses = Enumerable.Range(1, run.IntentA.Count - 1).Where(i => run.IntentA[i].Attack && !run.IntentA[i - 1].Attack).Select(i => run.IntentA[i].Direction).ToList();
        if (run.IntentA[0].Attack) presses.Insert(0, run.IntentA[0].Direction);
        Assert.Equal([AttackDirection.Right, AttackDirection.Left, AttackDirection.Overhead], presses);
        Assert.Equal([AttackDirection.Right, AttackDirection.Left], mine.Take(2).Select(a => a.Direction));
        Assert.DoesNotContain(run.AttackRefusals, r => r.Entity == run.A);
        for (int i = 1; i < mine.Count; i++) Assert.True(mine[i].Tick - mine[i - 1].Tick > 30, $"blow {i} began {mine[i].Tick - mine[i - 1].Tick} ticks after the one before");
        Assert.All(run.Ended, e => Assert.Equal(StepOutcome.Done, e.Outcome));
    }

    [Fact]
    public void A_press_at_once_while_a_press_waits_is_refused_by_the_rules_and_says_so()
    {
        var run = Make(Program(Script("Christof",
            new AttackStep { Direction = AttackDirection.Right },
            new AttackStep { Direction = AttackDirection.Left, WaitReady = false },
            new AttackStep { Direction = AttackDirection.Overhead, WaitReady = false })), distance: 1.5f);
        run.Go(3f);
        Assert.Contains(run.AttackRefusals, r => r.Entity == run.A && r.Why == AttackRefusal.MidSwing);
        var refused = Assert.Single(run.Refused);
        Assert.Equal((ScenarioStepKind.Attack, StepRefusal.RulesRefused), (refused.Kind, refused.Why));
    }

    [Fact]
    public void An_aim_at_the_head_is_pitched_higher_than_one_at_the_shin_and_each_names_its_region()
    {
        float Pitch(AimRegion region, out AimedBlow aimed)
        {
            var run = Make(Program(Script("Christof", new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.2f, AimAt = new() { Region = region } })), distance: 1.6f);
            run.Go(1.5f);
            aimed = Assert.Single(run.Aimed);
            return run.IntentA.First(i => i.Attack).AimPitch;
        }
        float head = Pitch(AimRegion.Head, out var h), shin = Pitch(AimRegion.Shin, out var s);
        Assert.True(head > shin + 0.3f, $"head {head * DetMath.RadToDeg:0.0}°, shin {shin * DetMath.RadToDeg:0.0}°");
        Assert.Equal(BodyRegion.Head, h.Region);
        Assert.Contains(s.Region, new[] { BodyRegion.LeftShin, BodyRegion.RightShin });
    }

    [Fact]
    public void An_aim_by_angles_sets_the_pitch_and_turns_the_facing_and_an_aim_at_a_point_faces_it()
    {
        var run = Make(Program(Script("Christof", new WaitStep { Seconds = 0.2f },
            new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.2f, AimAt = new() { PitchDegrees = 20f, YawDegrees = 30f } })), distance: 3f);
        run.Go(1f);
        Assert.Equal(20f, run.IntentA.First(i => i.Attack).AimPitch * DetMath.RadToDeg, 2);
        Assert.InRange(run.YawA[^1] * DetMath.RadToDeg, 28f, 32f);   // 30° left of the facing it had at the step's start, kept after
        Assert.Equal((BodyRegion.None, ""), (Assert.Single(run.Aimed).Region, run.Aimed[0].Asked));

        var point = Make(Program(Script("Christof", new AttackStep { Direction = AttackDirection.Thrust, HoldSeconds = 0.2f, AimAt = new() { Point = new(0f, 3f, 1.2f) } })), distance: 3f);
        point.Go(1f);
        Assert.InRange(point.YawA[^1] * DetMath.RadToDeg, 88f, 92f);   // faced the point, along +Y
    }

    [Fact]
    public void Block_holds_the_block_for_its_time_and_parry_presses_it_just_before_the_foe_s_strike()
    {
        var run = Make(Program(Script("Christof", new BlockStep { ForSeconds = 0.5f })));
        run.Go(1f);
        Assert.Single(run.Raised, r => r.Entity == run.A);
        Assert.InRange(Assert.Single(run.Lowered, l => l.Entity == run.A).HeldSeconds, 0.45f, 0.55f);

        // The guard's blow unparried, for its strike's moment (a parried blow may be stopped before it).
        var open = Make(Program(Script("Guard", new AttackStep { Direction = AttackDirection.Right, AtSeconds = 0.5f })), distance: 3f);
        open.Go(3f);
        long strikeTick = open.Strikes.First(s => s.Entity == open.B).Tick;
        var parry = Make(Program(
            Script("Christof", new ParryStep { Against = "Guard", LeadSeconds = 0.25f, ForSeconds = 3f }),
            Script("Guard", new AttackStep { Direction = AttackDirection.Right, AtSeconds = 0.5f })), distance: 3f);
        parry.Go(3f);
        var swing = Assert.Single(parry.Attacks, a => a.Entity == parry.B);
        var raised = Assert.Single(parry.Raised, r => r.Entity == parry.A);
        Assert.InRange(raised.Tick, swing.Tick + 1, strikeTick);
        Assert.True(strikeTick - raised.Tick <= 0.25f * World.TicksPerSecond + 2, $"raised {strikeTick - raised.Tick} ticks before the strike");
        Assert.Equal(StepOutcome.Done, parry.Ended.Single(e => e.Entity == parry.A).Outcome);
    }

    [Fact]
    public void Cancel_feints_a_blow_in_its_wind_up()
    {
        var run = Make(Program(Script("Christof", new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.05f }, new CancelStep())), distance: 1.5f);
        run.Go(2f);
        var swing = Assert.Single(run.Attacks, a => a.Entity == run.A);
        var cancelled = Assert.Single(run.Cancelled, c => c.Entity == run.A);
        Assert.Equal((swing.Swing, SwingCancelCause.Cancel, true), (cancelled.Swing, cancelled.Cause, cancelled.BeforeStrike));
        Assert.DoesNotContain(run.Strikes, s => s.Entity == run.A);
    }

    [Fact]
    public void Dodge_asks_for_a_dodge_the_rules_begin_back_or_to_a_side()
    {
        // At tick 0 a body just placed is not yet on the ground: the rules refuse (InAir), and the step says so.
        var early = Make(Program(Script("Christof", new DodgeStep { Way = DodgeWay.Left })));
        early.Go(0.2f);
        Assert.Equal((ScenarioStepKind.Dodge, StepRefusal.RulesRefused), (Assert.Single(early.Refused).Kind, early.Refused[0].Why));
        var run = Make(Program(Script("Christof", new WaitStep { Seconds = 0.25f }, new DodgeStep { Way = DodgeWay.Left })));
        run.Go(1.5f);
        var dodge = Assert.Single(run.Dodges, d => d.Entity == run.A);
        Assert.True(dodge.Way.Y > 0.9f, $"left of facing +X: {dodge.Way}");
        Assert.True(run.FeetA[^1].Y > 0.3f, $"moved {run.FeetA[^1]}");
        Assert.Equal(StepOutcome.Done, run.Ended.Single(e => e.Kind == ScenarioStepKind.Dodge).Outcome);
    }

    /// <summary>A stand-in for the module's powers (Act/1): slot 2 shows <c>celerity.active</c>; it records the slot pressed and clears it.</summary>
    sealed class Slots : ISystem, IPowerCatalogue
    {
        public EntityId Who;
        public readonly List<(long Tick, int Slot)> Presses = [];
        public int Count => 1;
        public string Id(int power) => "celerity.active";
        public string Label(int power) => "Celerity";

        public void Run(World world)
        {
            if (world.TryStore<PowerIntent>(out var intents) && intents.Has(Who))
            {
                ref var intent = ref intents.Get(Who);
                if (intent.Slot != 0) Presses.Add((world.Tick, intent.Slot));
                intent.Slot = 0;
            }
            Show(world);
        }

        public void Show(World world)
        {
            var views = new PowerSlots();
            for (int i = 0; i < QuickSlots.Count; i++) views.Slots[i] = i == 1 ? new SlotView { Power = 0, State = SlotState.Ready, Cost = 30 } : SlotView.None;
            world.Set(Who, views);
        }
    }

    [Fact]
    public void UsePower_presses_the_slot_that_shows_the_power_and_is_refused_when_none_does()
    {
        var slots = new Slots();
        var run = Make(Program(Script("Christof", new UsePowerStep { Power = "celerity" }, new UsePowerStep { Power = "potence" })),
            register: b => { PowerContract.Register(b); b.Service<IPowerCatalogue>(slots); b.System(Phase.Act, 1, "test.slots", slots); },
            setup: (w, a, _) => { slots.Who = a; slots.Show(w); });
        run.Go(0.5f);
        Assert.Equal([(0L, 2)], slots.Presses);
        var refused = Assert.Single(run.Refused);
        Assert.Equal((ScenarioStepKind.UsePower, StepRefusal.NoSlot), (refused.Kind, refused.Why));
    }

    // ---- control: mutes, hand-over, then ----

    [Fact]
    public void A_muted_brain_guards_but_never_attacks_or_steps_and_unmuted_it_does_both()
    {
        ScenarioProgram Guarding(ControlChannels mute) => Program(new ActorControl { Actor = "Guard", Ai = ScenarioAi.Duel, Mute = mute, Foe = "Christof", Then = ScenarioThen.Ai });
        var muted = Make(Guarding(ControlChannels.Attack | ControlChannels.Move), b: BrainKind.Duel, bNumbers: Calm, distance: 2.5f);
        muted.Go(4f);
        Assert.DoesNotContain(muted.Attacks, a => a.Entity == muted.B);
        Assert.True(Vector2.Distance(Flat(muted.FeetB[0]), Flat(muted.FeetB[^1])) < 0.05f, "the muted guard stepped");
        var c = Assert.Single(muted.Control);
        Assert.Equal((ControlChannels.None, ControlChannels.Attack | ControlChannels.Move), (c.Script, c.Muted));
        Assert.Equal(ControlChannels.All & ~(ControlChannels.Attack | ControlChannels.Move), c.Ai);

        var free = Make(Guarding(ControlChannels.None), b: BrainKind.Duel, bNumbers: Calm, distance: 2.5f);
        free.Go(4f);
        Assert.Contains(free.Attacks, a => a.Entity == free.B);
    }

    [Fact]
    public void HandOver_ends_the_script_there_and_then_end_ends_the_run_and_then_stand_keeps_still()
    {
        var handed = Make(Program(new ActorControl { Actor = "Christof", Ai = ScenarioAi.Duel, Foe = "Guard", Then = ScenarioThen.Stand,
            Tracks = [[new StandStep { Seconds = 0.5f }, new HandOverStep(), new MarkStep { Text = "never" }], [new WaitStep { Seconds = 5f }]] }), a: BrainKind.Duel, aNumbers: Calm);
        handed.Go(3f);
        Assert.DoesNotContain(handed.Markers, m => m.Text == "never");
        Assert.Contains(handed.Ended, e => e.Kind == ScenarioStepKind.Wait && e.Outcome == StepOutcome.HandedOver);
        Assert.True(handed.Gap() < 2.5f, "the brain did not take over");

        var ends = Make(Program(new ActorControl { Actor = "Christof", Ai = ScenarioAi.Duel, Foe = "Guard", Then = ScenarioThen.End, Tracks = [[new WaitStep { Seconds = 0.2f }]] }), a: BrainKind.Duel);
        ends.Go(1f);
        Assert.Equal((24L, "script"), (Assert.Single(ends.Over).Tick, ends.Over[0].Reason));

        var stands = Make(Program(new ActorControl { Actor = "Christof", Ai = ScenarioAi.Duel, Foe = "Guard", Then = ScenarioThen.Stand, Tracks = [[new WaitStep { Seconds = 0.2f }]] }), a: BrainKind.Duel, aNumbers: Calm);
        stands.Go(2f);
        Assert.InRange(stands.Gap(), 3.95f, 4.05f);
    }

    // ---- triggers ----

    [Fact]
    public void A_trigger_on_a_distance_starts_its_step_once_with_the_firing_as_its_parent()
    {
        var program = new ScenarioProgram
        {
            Actors = [new ActorControl { Actor = "Christof", Foe = "Guard", Then = ScenarioThen.Stand }],
            Triggers = [new ScenarioTrigger { Label = "too near", When = new() { Value = new ValueCondition("Guard.distanceTo.Christof", new Comparison(CompareOp.AtMost, 2.5)) },
                Do = [new TriggerAction { Actor = "Christof", Step = new RetreatStep { From = "Guard", Metres = 6f, Gait = Gait.Run, ForSeconds = 4f } }] }],
        };
        var run = Make(program, b: BrainKind.Duel, bNumbers: Calm, distance: 5f);
        run.Go(5f);
        var fired = Assert.Single(run.Fired);
        Assert.Equal("too near", fired.Label);
        var began = run.Began.Single(b => b.Kind == ScenarioStepKind.Retreat);
        Assert.Equal((-1, 0, fired.Firing, fired.Tick), (began.Track, began.Step, began.Parent, began.Tick));
        Assert.True(run.FeetA.Min(f => f.X) < -0.5f, "Christof did not back away");
    }

    [Fact]
    public void A_trigger_on_an_event_with_a_filter_answers_the_next_tick_and_a_count_ends_the_run()
    {
        var program = new ScenarioProgram
        {
            Actors =
            [
                new ActorControl { Actor = "Christof", Foe = "Guard", Then = ScenarioThen.Stand, Tracks = [[new RepeatStep { UntilStop = true, Steps = [new WaitStep { Seconds = 0.5f }, new MarkStep { Text = "m" }] }]] },
                Script("Guard", new AttackStep { Direction = AttackDirection.Right, AtSeconds = 0.2f }, new AttackStep { Direction = AttackDirection.Right }),
            ],
            Triggers =
            [
                new ScenarioTrigger { When = new() { Event = new EventSelector("AttackBegun", By: "Guard", Where: [new WhereFilter("direction", ["RIGHT"]), new WhereFilter("swing", ["2"])]) },
                    Do = [new TriggerAction { Actor = "Christof", Step = new DodgeStep { Way = DodgeWay.Back } }] },
                new ScenarioTrigger { When = new() { Count = new CountCondition(new EventSelector("Marker", By: "Christof", Where: [new WhereFilter("text", ["m"])]), new Comparison(CompareOp.AtLeast, 5)) },
                    Do = [new TriggerAction { End = "five marks" }] },
            ],
        };
        var run = Make(program, distance: 2f);
        run.Go(4f);
        var overhead = Assert.Single(run.Attacks, a => a.Entity == run.B && a.Swing == 2);
        var dodgeFired = run.Fired.Single(f => f.Trigger == 0);
        Assert.Equal(overhead.Tick + 1, dodgeFired.Tick);
        Assert.Single(run.Dodges, d => d.Entity == run.A);
        var over = Assert.Single(run.Over);
        Assert.Equal("five marks", over.Reason);
        Assert.Equal(run.Markers.Where(m => m.Text == "m").ElementAt(4).Tick + 1, over.Tick);
    }

    // ---- determinism ----

    static ScenarioProgram Busy() => new()
    {
        Actors =
        [
            new ActorControl { Actor = "Christof", Foe = "Guard", Ai = ScenarioAi.Duel, Then = ScenarioThen.Ai, Tracks =
            [[
                new MoveToStep { To = new() { Actor = "Guard", DistanceMetres = 1.4f }, FaceWhileMoving = "Guard", ForSeconds = 4f },
                new RepeatStep { Count = 3, Steps = [new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.2f, AimAt = new() { Region = AimRegion.Chest } }, new WaitStep { Seconds = 0.3f }] },
                new KeepDistanceStep { From = "Guard", Metres = 2f, ForSeconds = 1f },
            ]] },
            new ActorControl { Actor = "Guard", Foe = "Christof", Ai = ScenarioAi.Duel, Mute = ControlChannels.Move, Then = ScenarioThen.Ai },
        ],
        Triggers = [new ScenarioTrigger { When = new() { Event = new EventSelector("StrikeMoment", By: "Guard") }, Repeat = true, CooldownSeconds = 1f, Do = [new TriggerAction { Actor = "Christof", Step = new BlockStep { ForSeconds = 0.3f } }] }],
    };

    [Fact]
    public void A_scripted_world_replays_digest_for_digest_and_the_director_s_state_is_in_the_digest()
    {
        var one = Make(Busy(), a: BrainKind.Duel, b: BrainKind.Duel, distance: 4f, seed: 21);
        var two = Make(ScenarioProgram.Read(Busy().Write()), a: BrainKind.Duel, b: BrainKind.Duel, distance: 4f, seed: 21);
        one.Go(8f);
        two.Go(8f);
        Assert.Equal(one.Digests, two.Digests);
        Assert.True(one.Attacks.Count(a => a.Entity == one.A) >= 3);
        // The same world with another program (one wait longer) differs from the tick its cursor does.
        var other = Busy();
        var changed = new ScenarioProgram { Actors = [other.Actors[0] with { Tracks = [[.. other.Actors[0].Tracks[0].Take(1), new WaitStep { Seconds = 9f }]] }, other.Actors[1]], Triggers = other.Triggers };
        var three = Make(changed, a: BrainKind.Duel, b: BrainKind.Duel, distance: 4f, seed: 21);
        three.Go(8f);
        Assert.NotEqual(one.Digests[^1], three.Digests[^1]);
    }
}

/// <summary>The director allocates nothing per tick, measured as the platform's allocation tests measure.</summary>
[Collection(AllocationCollection.Name)]
public sealed class ScenarioDirectorAllocationTests
{
    [Fact]
    public void A_tick_of_the_director_with_steps_and_triggers_allocates_nothing()
    {
        Allocations.None("600 ticks of the director", () =>
        {
            var program = new ScenarioProgram
            {
                Actors =
                [
                    new ActorControl { Actor = "Christof", Foe = "Guard", Then = ScenarioThen.Stand, Tracks =
                    [
                        [new KeepDistanceStep { From = "Guard", Metres = 2f }],
                        [new RepeatStep { UntilStop = true, Steps = [new WaitStep { Seconds = 0.1f }, new MarkStep { Text = "tick" }] }],
                        [new BlockStep { Until = new EventSelector("Marker", By: "Christof", Where: [new WhereFilter("text", ["never"])]) }],
                    ] },
                    new ActorControl { Actor = "Guard", Foe = "Christof", Ai = ScenarioAi.Duel, Mute = ControlChannels.Attack, Then = ScenarioThen.Ai },
                ],
                Triggers =
                [
                    new ScenarioTrigger { When = new() { Value = new ValueCondition("Guard.distanceTo.Christof", new Comparison(CompareOp.Below, 0.1)) }, Do = [new TriggerAction { End = "never" }] },
                    new ScenarioTrigger { When = new() { Count = new CountCondition(new EventSelector("Marker", By: "Christof", Where: [new WhereFilter("text", ["tick"])]), new Comparison(CompareOp.Below, 0)) }, Do = [new TriggerAction { End = "never" }] },
                ],
            };
            var run = ScenarioDirectorTests.Make(program, b: BrainKind.Duel);
            for (int t = 0; t < 120; t++) MovementServices.Step(run.W, ActionFrame.Idle);
            var director = new ScenarioDirectorSystem();
            var queues = Allocations.Warm(run.W);
            director.Run(run.W);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 600; t++)
            {
                foreach (var q in queues) q.TurnOver();
                director.Run(run.W);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }
}
