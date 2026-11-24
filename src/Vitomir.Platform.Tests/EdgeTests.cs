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
/// Edges (the movement design): at a walk the player's body stops at an edge whose drop
/// is more than its safe fall, and running or sprinting goes over; crouch held as it steps off an edge it can hold
/// lowers it to hang; cancel lets go of a hang and undoes a move in its first moments, and on crouch's key it cancels
/// without crouching and crouches when there is nothing to cancel; a body following a route up an open-sided stair at
/// a sprint keeps on the stair.
/// </summary>
public sealed class EdgeTests
{
    /// <summary>A ledge: a block whose top is <paramref name="drop"/> high ending at x 0, its face upright, and the floor below it.</summary>
    static LevelSpace Ledge(float drop) =>
        new TestLevel().Floor(-8, -4, 8, 4).Box(new(-8, -4, 0), new(0, 4, drop)).Build($"ledge {drop}");

    /// <summary>The player's intent along +X at a gait as the control makes it (the safe fall's guard at a walk), crouch held or not.</summary>
    static MoveIntent Player(Gait gait, bool crouch = false)
    {
        var m = Drive.Toward(1, 0, gait);
        m.Crouch = crouch;
        m.Guard = gait == Gait.Walk ? EdgeGuard.SafeFall : EdgeGuard.None;
        return m;
    }

    sealed class Outcome
    {
        public Body Body;
        public Traversal Move;
        public bool Guarded, Landed, Lowered;
        public float Fall;
    }

    static Outcome Go(LevelSpace level, Vector3 from, int ticks, Func<int, Body, Traversal, MoveIntent> intent, Locomotion? numbers = null)
    {
        var r = new Outcome();
        var n = numbers ?? Drive.Numbers;
        var body = Drive.Settle(level, from);
        var move = default(Traversal);
        for (int i = 0; i < ticks; i++)
        {
            var o = BodyMotor.Step(ref body, ref move, n, intent(i, body, move), level.Collision, [], 0, i);
            r.Guarded |= o.Guarded;
            r.Lowered |= o.MoveStarted == TraversalKind.LowerToHang;
            if (o.Landed) { r.Landed = true; r.Fall = MathF.Max(r.Fall, o.FallHeight); }
        }
        r.Body = body;
        r.Move = move;
        return r;
    }

    [Theory]
    [InlineData(1.0f, Gait.Walk, false)]
    [InlineData(2.5f, Gait.Walk, false)]
    [InlineData(3.0f, Gait.Walk, true)]
    [InlineData(6.0f, Gait.Walk, true)]
    [InlineData(1.0f, Gait.Run, false)]
    [InlineData(2.5f, Gait.Run, false)]
    [InlineData(3.0f, Gait.Run, false)]
    [InlineData(6.0f, Gait.Run, false)]
    [InlineData(1.0f, Gait.Sprint, false)]
    [InlineData(2.5f, Gait.Sprint, false)]
    [InlineData(3.0f, Gait.Sprint, false)]
    [InlineData(6.0f, Gait.Sprint, false)]
    public void At_a_walk_a_body_stops_at_a_drop_beyond_its_safe_fall_and_running_or_sprinting_goes_over(float drop, Gait gait, bool stops)
    {
        var level = Ledge(drop);
        var r = Go(level, new(-2f, 0f, drop + 0.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(gait));
        if (stops)
        {
            Assert.True(r.Guarded);
            Assert.Equal(BodyState.Ground, r.Body.State);
            Assert.Equal(drop, r.Body.Feet.Z, 1);
            Assert.InRange(r.Body.Feet.X, -0.2f, 0.03f);      // its middle at the edge (to the 2 cm a crack is bridged by), not over it
            Assert.False(r.Landed);
        }
        else
        {
            Assert.False(r.Guarded);
            Assert.True(r.Landed);
            Assert.Equal(drop, r.Fall, 1);
            Assert.True(r.Body.Feet.Z < 0.05f && r.Body.Feet.X > 0.3f, $"ended at {r.Body.Feet}");
        }
    }

    [Fact]
    public void At_a_walk_the_body_slides_along_an_edge_it_meets_aslant()
    {
        var level = Ledge(4f);
        var way = Vector2.Normalize(new Vector2(1f, 1f));
        var r = Go(level, new(-1f, -2f, 4.05f), 4 * World.TicksPerSecond, (_, _, _) =>
        {
            var m = Drive.Toward(way.X, way.Y, Gait.Walk);
            m.Guard = EdgeGuard.SafeFall;
            return m;
        });
        Assert.True(r.Guarded);
        Assert.Equal(BodyState.Ground, r.Body.State);
        Assert.True(r.Body.Feet.Y > 1.0f, $"slid along to {r.Body.Feet}");
        Assert.InRange(r.Body.Feet.X, -0.2f, 0.03f);
    }

    [Fact]
    public void A_gap_narrower_than_the_body_is_walked_over_and_never_guarded()
    {
        // A slot 0.2 m wide and 3 m deep across the way: the body's footing bridges it.
        var level = new TestLevel().Floor(-8, -4, 8, 4)
            .Box(new(-8, -4, 0), new(0, 4, 3)).Box(new(0.2f, -4, 0), new(8, 4, 3)).Build("slot");
        var r = Go(level, new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Walk));
        Assert.False(r.Guarded);
        Assert.Equal(3f, r.Body.Feet.Z, 1);
        Assert.True(r.Body.Feet.X > 2f);
    }

    [Fact]
    public void Crouch_held_stepping_off_a_ledge_lowers_the_body_to_hang_from_it_and_without_crouch_a_walk_stops_and_a_run_falls()
    {
        var level = Ledge(3f);
        var held = Go(level, new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Walk, crouch: true));
        Assert.True(held.Lowered);
        Assert.Equal(BodyState.Hanging, held.Body.State);
        Assert.Equal(TraversalKind.Hang, held.Move.Kind);
        Assert.Equal(0f, held.Move.Lip.X, 1);
        Assert.Equal(3f, held.Move.Lip.Z, 1);
        Assert.Equal(3f - Drive.Numbers.StandingReach, held.Body.Feet.Z, 1);
        Assert.InRange(held.Body.Feet.X, 0.25f, 0.4f);       // a radius out from the face
        Assert.False(held.Landed);

        var running = Go(level, new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Run, crouch: true));
        Assert.True(running.Lowered);                           // at any pace moving towards it
        Assert.Equal(BodyState.Hanging, running.Body.State);

        var walk = Go(level, new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Walk));
        Assert.True(walk.Guarded);
        Assert.False(walk.Lowered);
        Assert.Equal(3f, walk.Body.Feet.Z, 1);

        var run = Go(level, new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Run));
        Assert.False(run.Lowered);
        Assert.True(run.Landed);
        Assert.True(run.Body.Feet.Z < 0.05f);
    }

    [Fact]
    public void From_the_lowered_hang_crouch_pressed_again_drops_and_jump_pulls_back_up()
    {
        var level = Ledge(3f);
        // Crouch held to the hang (1.5 s), let go, pressed again at 2 s: the body drops to the floor.
        var drop = Go(level, new(-1f, 0f, 3.05f), 3 * World.TicksPerSecond, (i, _, _) =>
            i < 180 ? Player(Gait.Walk, crouch: true) : i < 240 ? new MoveIntent { Gait = Gait.Walk } : new MoveIntent { Crouch = true });
        Assert.True(drop.Landed);
        Assert.True(drop.Body.Feet.Z < 0.05f);
        // Jump pressed from the hang: up onto the ledge again.
        var up = Go(level, new(-1f, 0f, 3.05f), 4 * World.TicksPerSecond, (i, _, _) =>
            i < 180 ? Player(Gait.Walk, crouch: true) : new MoveIntent { Gait = Gait.Walk, Jump = i == 240, JumpHeld = i >= 240 && i < 250 });
        Assert.Equal(BodyState.Ground, up.Body.State);
        Assert.Equal(3f, up.Body.Feet.Z, 1);
        Assert.True(up.Body.Feet.X < 0f);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Crouch_held_over_a_drop_less_than_the_hands_reach_climbs_down_and_ends_standing_at_its_foot(float drop)
    {
        // A walk in play off the gaps station's 2 m platform lowered to a hang with the feet on the ground (z 0.00)
        // and then dropped. Crouch held until the lowering begins, then let go.
        var level = Ledge(drop);
        bool begun = false, hung = false;
        float height = float.NaN;
        var body = Drive.Settle(level, new(-2f, 0f, drop + 0.05f));
        var move = default(Traversal);
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            var o = BodyMotor.Step(ref body, ref move, Drive.Numbers, begun ? new MoveIntent { Gait = Gait.Walk } : Player(Gait.Walk, crouch: true), level.Collision, [], 0, i);
            if (o.MoveStarted == TraversalKind.LowerToHang) { begun = true; height = move.Height; }
            hung |= body.State == BodyState.Hanging;
        }
        Assert.True(begun);
        Assert.False(hung);
        Assert.Equal(drop, height, 1);                              // the lowering's height is the drop (the log said -0.00 m)
        Assert.Equal(BodyState.Ground, body.State);
        Assert.Equal(TraversalKind.None, move.Kind);
        Assert.Equal(0, body.Crouched);
        Assert.True(body.Feet.Z < 0.02f, $"ended at {body.Feet}");
        Assert.InRange(body.Feet.X, 0.25f, 0.4f);                   // a radius out from the face
    }

    [Fact]
    public void Crouch_held_through_a_climb_down_ends_on_the_ground_crouched_and_a_drop_of_3_m_still_hangs()
    {
        var two = Go(Ledge(2f), new(-2f, 0f, 2.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Walk, crouch: true));
        Assert.True(two.Lowered);
        Assert.Equal(BodyState.Ground, two.Body.State);
        Assert.Equal(1, two.Body.Crouched);                         // crouch still held, as anywhere
        Assert.True(two.Body.Feet.Z < 0.02f);
        Assert.False(two.Landed && two.Fall > 0.05f, $"fell {two.Fall:0.00} m");
        var three = Go(Ledge(3f), new(-2f, 0f, 3.05f), 4 * World.TicksPerSecond, (_, _, _) => Player(Gait.Walk, crouch: true));
        Assert.Equal(BodyState.Hanging, three.Body.State);
        Assert.Equal(TraversalKind.Hang, three.Move.Kind);
        Assert.Equal(3f - Drive.Numbers.StandingReach, three.Body.Feet.Z, 1);    // at arm's length, as before
    }

    // ---------------------------------------------------------------- the log's events

    static (World World, EntityId Player) OnLedge(float drop)
    {
        var w = MovementServices.Register(new WorldBuilder(1), Ledge(drop)).Build();
        var p = MovementServices.SpawnControlled(w, new(-1f, 0f, drop + 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, p);
    }

    [Fact]
    public void A_stop_at_an_edge_is_told_once_a_stop_with_the_drop_and_the_safe_fall_not_every_tick()
    {
        var (w, _) = OnLedge(3f);
        var told = new List<EdgeStopped>();
        void Steps(int n, ActionFrame f) { for (int i = 0; i < n; i++) { MovementServices.Step(w, f); told.AddRange(w.Events<EdgeStopped>().ThisTick); } }
        Steps(4 * World.TicksPerSecond, Frame(Buttons.Walk, 1f));    // walked to the edge and held there for seconds
        Assert.Single(told);
        Assert.Equal(EdgeGuard.SafeFall, told[0].Guard);
        Assert.Equal(3f, told[0].Drop, 1);
        Assert.Equal(Locomotion.Sandbox.SafeFall, told[0].Limit, 2);
        Assert.Equal(0f, told[0].Edge.X, 1);
        Steps(World.TicksPerSecond, ActionFrame.Idle);               // let go a second: the next stop is another
        Steps(World.TicksPerSecond, Frame(Buttons.Walk, 1f));
        Assert.Equal(2, told.Count);
    }

    [Theory]
    [InlineData(1f, LandingClass.Soft)]
    [InlineData(3f, LandingClass.Hard)]
    [InlineData(6f, LandingClass.Hurt)]
    public void A_landing_is_told_soft_within_the_safe_fall_hard_to_twice_it_and_hurt_beyond(float drop, LandingClass want)
    {
        var (w, _) = OnLedge(drop);
        var landed = new List<Landed>();
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, Frame(Buttons.None, 1f));       // running: no guard
            landed.AddRange(w.Events<Landed>().ThisTick);
        }
        var fall = Assert.Single(landed, l => l.FallHeight > 0.5f);
        Assert.Equal(want, fall.Class);
        Assert.Equal(want, Landings.Of(drop, Locomotion.Sandbox));
    }

    [Fact]
    public void A_climb_down_is_told_with_where_it_began_the_lip_and_the_drop()
    {
        var (w, _) = OnLedge(2f);
        var climbed = new List<ClimbedDown>();
        var began = new List<TraversalStarted>();
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, Frame(Buttons.Walk | Buttons.Crouch, 1f));
            climbed.AddRange(w.Events<ClimbedDown>().ThisTick);
            began.AddRange(w.Events<TraversalStarted>().ThisTick);
        }
        var c = Assert.Single(climbed);
        Assert.Equal(2f, c.Drop, 1);
        Assert.Equal(2f, c.From.Z, 1);
        Assert.Equal(2f, c.Lip.Z, 1);
        Assert.True(c.Feet.Z < 0.02f);
        Assert.Equal(2f, Assert.Single(began, b => b.Kind == TraversalKind.LowerToHang).Height, 1);
    }

    [Fact]
    public void An_edge_the_hands_cannot_hold_is_not_lowered_from()
    {
        // A top 3 m high falling away at 60° from its edge: no upright face under the lip, nothing to hold.
        float run = 3f / DetMath.Tan(60f * DetMath.DegToRad);
        var level = new TestLevel().Floor(-8, -4, 8, 4)
            .Quad(new(-8, -4, 3), new(0, -4, 3), new(0, 4, 3), new(-8, 4, 3))
            .Quad(new(0, -4, 3), new(run, -4, 0), new(run, 4, 0), new(0, 4, 3)).Build("no lip");
        var r = Go(level, new(-2f, 0f, 3.05f), 3 * World.TicksPerSecond, (_, _, _) => Player(Gait.Run, crouch: true));
        Assert.False(r.Lowered);
        Assert.NotEqual(BodyState.Hanging, r.Body.State);
    }

    [Fact]
    public void Cancel_undoes_a_move_in_its_first_moments_putting_the_body_back_standing_where_it_started()
    {
        // A bench 0.5 m high a little ahead: a tap of jump hops up onto it; cancel two ticks later undoes the hop.
        var level = new TestLevel().Floor(-8, -4, 8, 4).Box(new(0.5f, -2, 0), new(2, 2, 0.5f)).Build("bench");
        Vector3 from = default;
        var r = Go(level, new(0f, 0f, 0.05f), 60, (i, b, m) =>
        {
            if (i == 2) from = b.Feet;
            var intent = Drive.Toward(1, 0, Gait.Walk, jump: i == 2);
            intent.Cancel = i == 4 && BodyMotor.CanCancel(b, m);
            if (i > 4) intent = new MoveIntent { Facing = 0f, Faces = true };
            return intent;
        });
        Assert.Equal(BodyState.Ground, r.Body.State);
        Assert.Equal(TraversalKind.None, r.Move.Kind);
        Assert.Equal(0f, r.Body.Feet.Z, 2);
        Assert.Equal(0, r.Body.Crouched);
        Assert.InRange(r.Body.Feet.X, from.X - 0.05f, from.X + 0.05f);
        // Later than its first 0.15 s the hop cannot be cancelled: it runs on to the top.
        var late = Go(level, new(0f, 0f, 0.05f), 90, (i, b, m) =>
        {
            var intent = i <= 2 ? Drive.Toward(1, 0, Gait.Walk, jump: i == 2) : new MoveIntent { Facing = 0f, Faces = true };
            intent.Cancel = i == 30 && BodyMotor.CanCancel(b, m);
            return intent;
        });
        Assert.Equal(0.5f, late.Body.Feet.Z, 1);
    }

    // ---------------------------------------------------------------- the cancel action through the player's control

    /// <summary>A world with the movement registered, the player standing on a 3 m ledge facing +X.</summary>
    static (World World, EntityId Player) OnLedge()
    {
        var w = MovementServices.Register(new WorldBuilder(1), Ledge(3f)).Build();
        var p = MovementServices.SpawnControlled(w, new(-1f, 0f, 3.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, p);
    }

    static ActionFrame Frame(Buttons held, float forward = 0f) => new(new Vector2(0f, forward), Vector2.Zero, held, new Vector2(0.5f, 0.5f));

    [Fact]
    public void Cancel_on_crouch_s_key_pressed_while_hanging_drops_without_crouching_and_pressed_standing_crouches()
    {
        var (w, p) = OnLedge();
        // Walk forward with crouch held: lowered to hang. Then everything let go.
        for (int i = 0; i < 240; i++) MovementServices.Step(w, Frame(Buttons.Walk | Buttons.Crouch, 1f));
        for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.Equal(BodyState.Hanging, w.Get<Body>(p).State);
        // The shared key goes down (crouch and cancel together) and is held: the hang is let go and the body does not crouch.
        for (int i = 0; i < 60; i++)
        {
            MovementServices.Step(w, Frame(Buttons.Crouch | Buttons.Cancel));
            if (i == 0) Assert.True(w.Get<MoveIntent>(p).Cancel);
            Assert.Equal(0, w.Get<Body>(p).Crouched);
            Assert.False(w.Get<MoveIntent>(p).Crouch);
        }
        Assert.NotEqual(BodyState.Hanging, w.Get<Body>(p).State);
        Assert.True(w.Get<Body>(p).Feet.Z < 0.05f);
        // Let go and pressed again standing on the floor: nothing to cancel, so it crouches.
        MovementServices.Step(w, ActionFrame.Idle);
        for (int i = 0; i < 5; i++) MovementServices.Step(w, Frame(Buttons.Crouch | Buttons.Cancel));
        Assert.False(w.Get<MoveIntent>(p).Cancel);
        Assert.Equal(1, w.Get<Body>(p).Crouched);
    }

    [Fact]
    public void The_player_s_control_asks_for_the_safe_fall_s_guard_at_a_walk_only()
    {
        var (w, p) = OnLedge();
        MovementServices.Step(w, Frame(Buttons.Walk, 1f));
        Assert.Equal(EdgeGuard.SafeFall, w.Get<MoveIntent>(p).Guard);
        MovementServices.Step(w, Frame(Buttons.None, 1f));
        Assert.Equal(EdgeGuard.None, w.Get<MoveIntent>(p).Guard);
        MovementServices.Step(w, Frame(Buttons.Sprint, 1f));
        Assert.Equal(EdgeGuard.None, w.Get<MoveIntent>(p).Guard);
    }

    // ---------------------------------------------------------------- route-followers

    /// <summary>
    /// An open-sided stair: a flight up +X to a landing 2 m high, a turn, and a flight up +Y to 4 m; nothing at the sides
    /// but the floor below. Its route's corners: the foot, the landing's middle, the top.
    /// </summary>
    static (LevelSpace Level, Vector3[] Corners) OpenStair()
    {
        var t = new TestLevel().Floor(-6, -6, 10, 12);
        for (int i = 0; i < 10; i++) t.Box(new(i * 0.3f, 0f, 0f), new(i * 0.3f + 0.3f, 1.2f, (i + 1) * 0.2f));
        t.Box(new(3f, 0f, 0f), new(3.9f, 1.2f, 2f));
        for (int i = 0; i < 10; i++) t.Box(new(3f, 1.2f + i * 0.3f, 0f), new(3.9f, 1.5f + i * 0.3f, 2f + (i + 1) * 0.2f));
        t.Box(new(3f, 4.2f, 0f), new(3.9f, 6f, 4f));
        return (t.Build("open stair"), [new(-1f, 0.6f, 0f), new(3.45f, 0.6f, 2f), new(3.45f, 5f, 4f)]);
    }

    static (bool Arrived, float Fell) Follow(LevelSpace level, Vector3[] corners, Gait gait, bool guard)
    {
        var body = Drive.Settle(level, new(-3f, 0.6f, 0.05f));
        int next = 0;
        float fell = 0f;
        for (int tick = 0; tick < 20 * World.TicksPerSecond; tick++)
        {
            var corner = corners[next];
            bool last = next == corners.Length - 1;
            if (RouteSteer.Across(body.Feet, corner) < (last ? RouteSteer.ArriveRadius : RouteSteer.CornerRadius))
            {
                if (last) return (true, fell);
                next++;
                continue;
            }
            var intent = RouteSteer.Toward(body.Feet, corner, gait);
            if (!guard) intent.Guard = EdgeGuard.None;
            var o = BodyMotor.Step(ref body, Drive.Numbers, intent, level.Collision, [], 0, tick);
            if (o.Landed) fell = MathF.Max(fell, o.FallHeight);
            if (body.State == BodyState.Ground && body.Feet.Z < 0.5f && next >= 2) return (false, fell);
        }
        return (false, fell);
    }

    [Fact]
    public void A_route_up_an_open_sided_stair_at_a_sprint_keeps_on_the_stair()
    {
        var (level, corners) = OpenStair();
        var unguarded = Follow(level, corners, Gait.Sprint, guard: false);
        Assert.True(unguarded.Fell > 1f, $"the stair is a test: an unguarded sprint falls off it (fell {unguarded.Fell:0.00} m)");
        var (arrived, fell) = Follow(level, corners, Gait.Sprint, guard: true);
        Assert.True(arrived);
        Assert.True(fell <= Drive.Numbers.StepHeight + 0.05f, $"fell {fell:0.00} m");
    }

    /// <summary>Follows <paramref name="corners"/> from <paramref name="from"/> at a gait for up to <paramref name="seconds"/>: whether it arrived, the highest fall, whether it was ever in the air, and the body.</summary>
    static (bool Arrived, float Fell, bool Flew, Body Body) Route(LevelSpace level, Vector3 from, Vector3[] corners, Gait gait, int seconds = 12)
    {
        var body = Drive.Settle(level, from);
        int next = 0;
        float fell = 0f;
        bool flew = false;
        for (int tick = 0; tick < seconds * World.TicksPerSecond; tick++)
        {
            bool last = next == corners.Length - 1;
            if (RouteSteer.Across(body.Feet, corners[next]) < (last ? RouteSteer.ArriveRadius : RouteSteer.CornerRadius))
            {
                if (last) return (true, fell, flew, body);
                next++;
                continue;
            }
            var o = BodyMotor.Step(ref body, Drive.Numbers, RouteSteer.Toward(body.Feet, corners[next], gait), level.Collision, [], 0, tick);
            if (o.Landed) fell = MathF.Max(fell, o.FallHeight);
            flew |= body.State == BodyState.Air;
        }
        return (false, fell, flew, body);
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    [InlineData(Gait.Sprint)]
    public void A_route_never_takes_a_body_down_more_than_a_step_and_a_little_wherever_it_goes(Gait gait)
    {
        // "NPCs just walking shouldn't ever ... fall off anything": a ledge of 1.5 m with the
        // route's next corner at its foot beyond it — taken — keeps the body on its top.
        var (arrived, fell, _, body) = Route(Ledge(1.5f), new(-2f, 0f, 1.55f), [new(1.5f, 0f, 0f)], gait, seconds: 4);
        Assert.False(arrived);
        Assert.Equal(0f, fell);
        Assert.Equal(1.5f, body.Feet.Z, 2);
        Assert.Equal(BodyState.Ground, body.State);
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    [InlineData(Gait.Sprint)]
    public void A_step_a_little_more_than_the_body_s_is_stepped_down_where_the_route_crosses_it_and_never_flown_over(Gait gait)
    {
        // A step of 0.38 m (the human steps 0.35 m; walk35's stair has risers of 0.353 m, Old Town a sunken ramp 0.37 m
        // deep): the route's corner beyond it — stepped down, the body never in the air.
        var (arrived, fell, flew, body) = Route(Ledge(0.38f), new(-2f, 0f, 0.43f), [new(1.5f, 0f, 0f)], gait);
        Assert.True(arrived, $"ended at {body.Feet}");
        Assert.Equal(0f, fell);
        Assert.False(flew);
        // The route along its top, never across it: kept on the top.
        (arrived, _, flew, body) = Route(Ledge(0.38f), new(-2f, -3f, 0.43f), [new(-0.5f, 3f, 0.38f)], gait);
        Assert.True(arrived, $"ended at {body.Feet}");
        Assert.False(flew);
        Assert.Equal(0.38f, body.Feet.Z, 2);
    }

    /// <summary>
    /// A landing 2 m high (x −4 to 2, y −4 to 0) and beside it, its north side, a stair going down along −X from x 0 (ten
    /// treads of 0.3 m and 0.2 m risers, y 0 to 1.2) to the floor, with the stair's head at the landing's height (x 0 to 2):
    /// walk35's gate tower's landing at 9 m beside its stair.
    /// </summary>
    static LevelSpace LandingBesideStair()
    {
        var t = new TestLevel().Floor(-8, -6, 4, 4).Box(new(-4, -4, 0), new(2, 0, 2f)).Box(new(0, 0, 0), new(2, 1.2f, 2f));
        for (int i = 0; i < 9; i++) t.Box(new(-0.3f * (i + 1), 0, 0), new(-0.3f * i, 1.2f, 2f - 0.2f * (i + 1)));
        return t.Build("landing beside a stair");
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    [InlineData(Gait.Sprint)]
    public void A_body_on_a_route_held_level_with_a_landing_s_edge_over_a_stair_going_down_steps_down_onto_the_stair(Gait gait)
    {
        // Stood with its middle 0.2 m over the stair's first tread, its disc on the landing's edge (2.0 m): the route
        // to the stair's foot. The earlier guard held it level with the edge as the treads went down beneath it and stopped
        // it short of stepping off (walk35's stops near (−49.06, −309.8) at a sprint).
        var level = LandingBesideStair();
        var start = Drive.Settle(level, new(-0.12f, 0.2f, 2.05f));
        Assert.Equal(2f, start.Feet.Z, 2);                                   // held level with the landing's edge
        var (arrived, fell, _, body) = Route(level, new(-0.12f, 0.2f, 2.05f), [new(-3.6f, 0.6f, 0f)], gait);
        Assert.True(arrived, $"ended at {body.Feet}");
        Assert.True(fell <= Drive.Numbers.StepHeight + 0.05f, $"fell {fell:0.00} m");
    }

    [Fact]
    public void Edges_and_cancel_replay_to_the_same_digest()
    {
        ulong Run()
        {
            var (w, _) = OnLedge();
            for (int i = 0; i < 240; i++) MovementServices.Step(w, Frame(Buttons.Walk | Buttons.Crouch, 1f));
            for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
            for (int i = 0; i < 60; i++) MovementServices.Step(w, Frame(Buttons.Crouch | Buttons.Cancel));
            for (int i = 0; i < 120; i++) MovementServices.Step(w, Frame(Buttons.Walk, 1f));
            return w.Digest();
        }
        Assert.Equal(Run(), Run());
    }
}
