// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Active Dodge (Bodies/Dodge.cs, BodyMotor.Dodge.cs) on levels built in memory: a press of Dodge carries a human a
/// metre along the move keys' way (straight back with none) in its burst, after its start, and stands it for its
/// recovery; every refusal (a blow playing, dodging, recovering, in the air, on a ladder, in a move, stopped) is told and
/// does nothing; a dodge lowers a raised block and keeps a fighter busy to its end; a wall and another body stop it and it
/// goes down no drop higher than a step; Celerity's body tempo shortens it unbound and not in the masquerade, which also holds
/// its burst to a human's; the same presses make the same world; a dodge is its components' state, so a world given them
/// mid-dodge goes on as the first.
/// </summary>
public sealed class DodgeTests
{
    const int T = World.TicksPerSecond;
    // A human's dodge (DodgeSettings): 0.15 s start, 0.3 s burst, 0.4 s recovery, 1 m.
    const int StartTicks = 18, BurstTicks = 36, RecoverTicks = 48;

    sealed record Rig(World W, EntityId P);

    static LevelSpace Floor(Action<TestLevel>? more = null)
    {
        var t = new TestLevel().Floor(-20, -20, 20, 20, cell: 4f);
        more?.Invoke(t);
        return new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
    }

    static Rig Make(LevelSpace? level = null, Vector3? at = null, bool masquerade = false)
    {
        var b = new WorldBuilder(3);
        MovementServices.Register(b, level ?? Floor());
        b.Component<Combatant>().Component<CharacterAnimation>().Component<Dead>();
        b.Event<BlockLowered>();
        if (masquerade) MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        PowerContract.Register(b);
        var w = b.Build();
        if (masquerade)
        {
            var profiles = w.Service<MovementProfiles>();
            profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Locomotion.Sandbox, true), isHuman: true);
        }
        var p = MovementServices.SpawnControlled(w, at ?? new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        if (masquerade) w.Service<MovementProfiles>().Give(w, p, 0);
        for (int i = 0; i < 12; i++) MovementServices.Step(w, ActionFrame.Idle);   // settled on the floor
        return new Rig(w, p);
    }

    static readonly ActionFrame Left = new(new Vector2(-1, 0), default, Buttons.None, default);
    static ActionFrame Press(ActionFrame f) => f with { Held = f.Held | Buttons.Dodge };

    static Vector3 Feet(Rig r) => r.W.Get<Body>(r.P).Feet;

    /// <summary>Steps <paramref name="ticks"/> idle ticks, gathering the dodge's events.</summary>
    static void Run(Rig r, int ticks, List<DodgeBegun>? begun = null, List<DodgeEnded>? ended = null, List<DodgeRefused>? refused = null, ActionFrame? frame = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(r.W, frame ?? ActionFrame.Idle);
            begun?.AddRange(r.W.Events<DodgeBegun>().ThisTick);
            ended?.AddRange(r.W.Events<DodgeEnded>().ThisTick);
            refused?.AddRange(r.W.Events<DodgeRefused>().ThisTick);
        }
    }

    static void PressOnce(Rig r, ActionFrame keys, List<DodgeBegun>? begun = null, List<DodgeRefused>? refused = null)
    {
        Run(r, 1, begun, null, refused, Press(keys));
    }

    [Fact]
    public void A_press_of_dodge_carries_a_human_a_metre_along_the_move_keys_after_its_start_and_stands_it_for_its_recovery()
    {
        var r = Make();
        var from = Feet(r);
        var begun = new List<DodgeBegun>();
        var ended = new List<DodgeEnded>();
        long pressed = r.W.Tick + 1;
        PressOnce(r, Left, begun);
        Assert.Single(begun);
        var b = begun[0];
        Assert.Equal((0.15f, 0.3f, 0.4f, 1f, false), (b.Ready, b.Move, b.Recover, b.Distance, b.Held));
        Assert.Equal(1f / 0.3f, b.Speed, 3);
        // The view faces +X: left is +Y.
        Assert.Equal(new Vector2(0, 1), b.Way);
        Run(r, StartTicks - 1, ended: ended);
        Assert.True(Vector3.Distance(from, Feet(r)) < 0.01f, $"moved {Vector3.Distance(from, Feet(r)):0.000} m in the start");
        Run(r, BurstTicks, ended: ended);
        var out1 = Feet(r);
        Assert.Equal(1f, out1.Y - from.Y, 2);
        Assert.Equal(0f, out1.X - from.X, 2);
        Run(r, RecoverTicks, ended: ended);
        Assert.Equal(out1.Y, Feet(r).Y, 3);                                          // stood still in the recovery
        Assert.Single(ended);
        Assert.InRange(ended[0].Tick - pressed, StartTicks + BurstTicks + RecoverTicks - 2, StartTicks + BurstTicks + RecoverTicks);
        Assert.False(ended[0].Cut);
        Assert.Equal(1f, ended[0].Moved, 2);
        Assert.False(r.W.Get<Dodging>(r.P).Active);
        Assert.Equal(0f, r.W.Get<Body>(r.P).Yaw, 4);                                // a dodge turns no body
    }

    [Fact]
    public void With_no_move_key_held_the_dodge_goes_straight_back_from_the_facing()
    {
        var r = Make();
        var from = Feet(r);
        PressOnce(r, ActionFrame.Idle);
        Run(r, StartTicks + BurstTicks + RecoverTicks);
        Assert.Equal(-1f, Feet(r).X - from.X, 2);
        Assert.Equal(0f, Feet(r).Y - from.Y, 2);
    }

    [Fact]
    public void A_second_press_while_dodging_or_recovering_is_refused_and_changes_nothing()
    {
        var once = Make();
        var twice = Make();
        PressOnce(once, Left);
        PressOnce(twice, Left);
        var refused = new List<DodgeRefused>();
        Run(once, 30); Run(twice, 29);
        PressOnce(twice, ActionFrame.Idle, refused: refused);                         // mid-burst
        Run(once, 40); Run(twice, 39);
        PressOnce(twice, Left, refused: refused);                                      // in the recovery
        Assert.Equal([DodgeRefusal.Dodging, DodgeRefusal.Recovering], refused.Select(x => x.Why));
        Assert.Equal(once.W.Get<Body>(once.P), twice.W.Get<Body>(twice.P));
        Assert.Equal(once.W.Get<Dodging>(once.P), twice.W.Get<Dodging>(twice.P));
        // Once it has ended, a press begins the next.
        Run(twice, RecoverTicks);
        var begun = new List<DodgeBegun>();
        PressOnce(twice, Left, begun);
        Assert.Single(begun);
        Assert.Equal(2, twice.W.Get<Dodging>(twice.P).Count);
    }

    [Fact]
    public void A_dodge_is_refused_in_the_air_on_a_ladder_in_a_move_and_stopped_and_nothing_comes_of_it()
    {
        // In the air: jump, then press dodge going up; a twin that only jumps is the same.
        var air = Make();
        var jumpOnly = Make();
        var jump = ActionFrame.Idle with { Held = Buttons.Jump };
        Run(air, 1, frame: jump); Run(jumpOnly, 1, frame: jump);
        Run(air, 10); Run(jumpOnly, 10);
        var refused = new List<DodgeRefused>();
        PressOnce(air, Left, refused: refused);
        Run(jumpOnly, 1, frame: Left);
        Assert.Equal(DodgeRefusal.InAir, Assert.Single(refused).Why);
        Assert.Equal(jumpOnly.W.Get<Body>(jumpOnly.P), air.W.Get<Body>(air.P));

        DodgeRefusal Refusal(Action<Rig> set)
        {
            var r = Make();
            set(r);
            var why = new List<DodgeRefused>();
            var begun = new List<DodgeBegun>();
            PressOnce(r, Left, begun, why);
            Assert.Empty(begun);
            Assert.False(r.W.Store<Dodging>().Has(r.P) && r.W.Get<Dodging>(r.P).Active);
            return Assert.Single(why).Why;
        }
        Assert.Equal(DodgeRefusal.Ladder, Refusal(r => r.W.Get<Body>(r.P).State = BodyState.Ladder));
        Assert.Equal(DodgeRefusal.Moving, Refusal(r =>
        {
            r.W.Get<Body>(r.P).State = BodyState.Traversing;
            r.W.Get<Traversal>(r.P) = new Traversal { Kind = TraversalKind.Vault, Seconds = 1f };
        }));
        Assert.Equal(DodgeRefusal.Stopped, Refusal(r => r.W.Set(r.P, new Dead { Death = ActionRole.DeathQuick })));
        // Mid-swing on the ground a dodge is no longer refused — it cancels the swing (SwingCancel); the
        // refusal mid-swing stands where something else refuses too (ControlCancelTests), and the whole path is GuardTests'.
    }

    [Fact]
    public void A_dodge_lowers_a_raised_block_and_keeps_the_fighter_busy_to_its_end()
    {
        var r = Make();
        var c = Combatant.Ready;
        c.Blocking = true;
        c.BlockSeconds = 0.5f;
        r.W.Set(r.P, c);
        r.W.Set(r.P, new CharacterAnimation { Action = ActionRole.Block });
        var lowered = new List<BlockLowered>();
        MovementServices.Step(r.W, Press(Left));
        lowered.AddRange(r.W.Events<BlockLowered>().ThisTick);
        var after = r.W.Get<Combatant>(r.P);
        Assert.False(after.Blocking);
        Assert.Single(lowered);
        Assert.Equal(0.85f, after.Busy, 3);       // the whole dodge (the attacks' system counts it down)
    }

    [Fact]
    public void A_wall_stops_a_dodge_and_so_does_another_body()
    {
        // A wall 0.7 m to the left (+Y): the capsule (radius 0.3) stops at it.
        var walled = Make(Floor(t => t.Box(new(-3, 0.7f, 0), new(3, 0.9f, 2.5f))));
        var ended = new List<DodgeEnded>();
        PressOnce(walled, Left);
        float most = 0f;
        for (int i = 0; i < StartTicks + BurstTicks + RecoverTicks; i++) { Run(walled, 1, ended: ended); most = MathF.Max(most, Feet(walled).Y); }
        Assert.True(most <= 0.7f - 0.3f + 0.01f, $"the body's middle came to y {most:0.000}, its side past the wall's face at 0.7");
        Assert.True(Assert.Single(ended).Moved < 0.45f, $"moved {ended[0].Moved:0.000} m");

        // Another body standing 1.0 m to the left: never within two radii.
        var crowded = Make();
        var other = MovementServices.SpawnBody(crowded.W, new(0, 1.0f, 0.01f), 0f, Locomotion.Sandbox);
        crowded.W.Get<MoveIntent>(other).Gait = Gait.Walk;
        Run(crowded, 5);
        PressOnce(crowded, Left);
        float nearest = float.MaxValue;
        for (int i = 0; i < StartTicks + BurstTicks + RecoverTicks; i++)
        {
            Run(crowded, 1);
            var a = Feet(crowded); var o = crowded.W.Get<Body>(other).Feet;
            nearest = MathF.Min(nearest, new Vector2(a.X - o.X, a.Y - o.Y).Length());
        }
        Assert.True(nearest >= 0.6f - 0.01f, $"came within {nearest:0.000} m of the other body's middle");
    }

    [Fact]
    public void A_dodge_goes_down_no_drop_higher_than_a_step()
    {
        // On a block 1 m high whose edge is 0.5 m to the left: the dodge stops at the edge, on the top.
        var level = Floor(t => t.Box(new(-3, -3, 0), new(3, 0.5f, 1f)));
        var r = Make(level, at: new(0, 0, 1.01f));
        PressOnce(r, Left);
        for (int i = 0; i < StartTicks + BurstTicks + RecoverTicks; i++)
        {
            Run(r, 1);
            Assert.Equal(BodyState.Ground, r.W.Get<Body>(r.P).State);
            Assert.True(Feet(r).Z > 0.99f, $"left the top: z {Feet(r).Z:0.000}");
        }
        // A step 0.3 m down is taken.
        var step = Make(Floor(t => t.Box(new(-3, -3, 0), new(3, 0.5f, 0.3f))), at: new(0, 0, 0.31f));
        PressOnce(step, Left);
        Run(step, StartTicks + BurstTicks + RecoverTicks);
        Assert.Equal(1f, Feet(step).Y, 1);
        Assert.Equal(0f, Feet(step).Z, 2);
    }

    [Fact]
    public void Celerity_s_body_tempo_shortens_a_dodge_unbound_and_the_masquerade_holds_it_to_a_human_s()
    {
        // Unbound (no masquerade at all) under a pace scale of 2: every time halved, the distance kept.
        var fast = Make();
        fast.W.Set(fast.P, new PaceScale { Factor = 2f });
        var begun = new List<DodgeBegun>();
        PressOnce(fast, Left, begun);
        var b = Assert.Single(begun);
        Assert.Equal((0.075f, 0.15f, 0.2f, 1f), (b.Ready, b.Move, b.Recover, b.Distance));
        var from = b.Feet;
        Run(fast, 9 + 18);
        Assert.Equal(1f, Feet(fast).Y - from.Y, 2);

        // In the masquerade's mode the factor is ignored, and the burst is held to the human's.
        var masked = Make(masquerade: true);
        Assert.Equal(MasqueradeMode.Masquerade, masked.W.Get<MasqueradeState>(masked.P).Mode);
        masked.W.Set(masked.P, new PaceScale { Factor = 2f });
        masked.W.Set(masked.P, new DodgeTempo { Ready = 0.1f, Move = 0.2f, Recover = 0.3f, Distance = 1.2f, HumanMove = 0.3f });
        begun.Clear();
        PressOnce(masked, Left, begun);
        b = Assert.Single(begun);
        Assert.Equal((0.1f, 0.3f, 0.3f, 1.2f, true), (b.Ready, b.Move, b.Recover, b.Distance, b.Held));
        // Unbound, the body's own and the factor.
        ref var st = ref masked.W.Get<MasqueradeState>(masked.P);
        st.Mode = MasqueradeMode.Unbound;
        Run(masked, 120);
        begun.Clear();
        PressOnce(masked, Left, begun);
        b = Assert.Single(begun);
        Assert.Equal((0.05f, 0.1f, 0.15f, false), (b.Ready, b.Move, b.Recover, b.Held));
        Assert.Equal(12f, b.Speed, 3);
    }

    [Fact]
    public void The_same_presses_make_the_same_world_and_a_world_given_a_dodge_s_components_mid_dodge_goes_on_as_the_first()
    {
        static Rig Played(int ticks)
        {
            var r = Make();
            PressOnce(r, Left);
            Run(r, ticks);
            return r;
        }
        var a = Played(40);
        var b = Played(40);
        Assert.Equal(a.W.Digest(), b.W.Digest());

        // A "save" mid-burst: a world stepped to the same tick with no dodge, given the dodging body's components.
        var c = Make();
        Run(c, 41);
        Assert.Equal(a.W.Tick, c.W.Tick);
        c.W.Set(c.P, a.W.Get<Body>(a.P));
        c.W.Set(c.P, a.W.Get<Dodging>(a.P));
        c.W.Set(c.P, a.W.Get<DodgeIntent>(a.P));
        c.W.Set(c.P, a.W.Get<MoveIntent>(a.P));
        c.W.Set(c.P, a.W.Get<Controlled>(a.P));
        c.W.Set(c.P, a.W.Get<Traversal>(a.P));
        for (int i = 0; i < 80; i++)
        {
            Run(a, 1); Run(c, 1);
            Assert.Equal(a.W.Get<Body>(a.P), c.W.Get<Body>(c.P));
            Assert.Equal(a.W.Get<Dodging>(a.P), c.W.Get<Dodging>(c.P));
        }
        Assert.Equal(a.W.Digest(), c.W.Digest());
    }

    [Fact]
    public void A_world_where_nobody_dodges_has_no_dodge_on_any_body()
    {
        var r = Make();
        Run(r, 120, frame: Left);
        Assert.Equal(0, r.W.Store<DodgeIntent>().Count);
        Assert.Equal(0, r.W.Store<Dodging>().Count);
    }
}

/// <summary>That a tick of bodies dodging allocates nothing (measured as <see cref="AllocationTests"/> are).</summary>
[Collection(AllocationCollection.Name)]
public sealed class DodgeAllocationTests
{
    [Fact]
    public void A_tick_of_a_body_dodging_again_and_again_allocates_nothing()
    {
        Allocations.None("600 ticks of dodges", () =>
        {
            var b = new WorldBuilder(3);
            MovementServices.Register(b, new LevelSpace("floor", new TestLevel().Floor(-20, -20, 20, 20, cell: 4f).Build("floor").Collision));
            b.Component<Combatant>().Component<CharacterAnimation>();
            var w = b.Build();
            var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
            w.Set(p, Combatant.Ready);
            var input = w.Service<TickInput>();
            ActionFrame Frame(int t) => new(new Vector2((t / 110) % 2 == 0 ? -1 : 1, 0), default, t % 110 == 0 ? Buttons.Dodge : Buttons.None, default);
            for (int t = 0; t < 240; t++) { input.Set(Frame(t)); w.Step(); }
            ISystem[] systems = [new PlayerControlSystem(), new DodgeSystem(), new BodySystem()];
            var queues = Allocations.Warm(w);
            foreach (var s in systems) s.Run(w);
            int dodging = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 240; t < 840; t++)
            {
                input.Set(Frame(t));
                foreach (var q in queues) q.TurnOver();
                foreach (var s in systems) s.Run(w);
                if (w.Get<Dodging>(p).Active) dodging++;
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(dodging > 300, $"only {dodging} ticks dodging");
            return spent;
        });
    }
}
