// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Traversal: every row of the controls table on small levels built in memory —
/// a bench stops a body at any pace and a tap of jump close to it hops it up; a tap does every move the
/// obstacle calls for (a wall of a metre climbed onto, a fence vaulted, a lip grabbed); a sprint hurdles a thin fence
/// with ground beyond and nothing else; jump held (the human: no charge) vaults a fence, climbs onto a deep box,
/// mantles onto a ledge (not without room to stand; crouched under a low ceiling); forward or jump held at a grab's
/// end pulls up, nothing held hangs, crouch drops, and from hanging jump or a fresh forward pulls up. Crouch lowers and slows the body and keeps it down under a beam; ground is walked to 40° and stood
/// on to 45°. The moves replay to the same digest (and allocate nothing: AllocationTests), report their events, and the eye
/// follows a crouch.
/// </summary>
public sealed class TraversalTests
{
    /// <summary>What a drive did: the moves begun and ended in order, the highest the feet rose, the grabs and drops, and the body at the end.</summary>
    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [], Ended = [];
        public float Highest = float.MinValue;
        public int Grabs, Drops, Refused;
        public Body End;
        public Traversal LastMove;
        public readonly Dictionary<TraversalKind, int> Ticks = [];
        public override string ToString() => $"started [{string.Join(", ", Started)}], ended at {End.Feet} ({End.State}), highest {Highest:0.000}";
    }

    static Drove Go(LevelSpace level, Vector3 from, int ticks, Func<int, Body, MoveIntent> intent, float yaw = 0f, Locomotion? numbers = null)
    {
        var d = new Drove();
        var body = Drive.Settle(level, from, yaw);
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, ticks, intent, (_, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.MoveEnded != TraversalKind.None) { d.Ended.Add(o.MoveEnded); if (!o.MoveCompleted) d.Refused++; }
            if (o.Grabbed) d.Grabs++;
            if (o.Dropped) d.Drops++;
            if (m.Kind != TraversalKind.None) d.Ticks[m.Kind] = d.Ticks.GetValueOrDefault(m.Kind) + 1;
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
            d.LastMove = m;
        }, numbers);
        return d;
    }

    /// <summary>Going along +X at a gait (or standing still facing it), jump pressed at <paramref name="press"/> and down for <paramref name="hold"/> ticks.</summary>
    static Func<int, Body, MoveIntent> Along(Gait gait, int press = -1, int hold = 1, bool still = false, bool crouch = false, float stop = 6f) => (i, b) =>
    {
        var m = still || b.Feet.X > stop ? new MoveIntent { Facing = 0f, Faces = true, Gait = gait } : Drive.Toward(1, 0, gait);
        m.Jump = i == press;
        m.JumpHeld = press >= 0 && i >= press && i < press + hold;
        m.Crouch = crouch;
        return m;
    };

    static TestLevel Ground() => new TestLevel().Floor(-10, -10, 10, 10);

    /// <summary>A bench 0.45 m high and 0.4 m deep whose near face is at x 1.</summary>
    static LevelSpace Bench() => Ground().Box(new(1, -1.5f, 0), new(1.4f, 1.5f, 0.45f)).Build();

    /// <summary>A deep block (to x 5) of <paramref name="height"/> whose face is at x 1.</summary>
    static TestLevel Ledge(float height) => Ground().Box(new(1, -3, 0), new(5, 3, height));

    /// <summary>A fence 0.1 m thick of <paramref name="height"/> whose near face is at <paramref name="x"/>.</summary>
    static TestLevel Fence(float height, float x = 1f) => Ground().Box(new(x, -3, 0), new(x + 0.1f, 3, height));

    static void Near(float expected, float actual, float within, string what) =>
        Assert.True(MathF.Abs(actual - expected) <= within, $"{what}: {actual} (expected {expected} within {within})");

    // ---------------------------------------------------------------- the stride and the bench

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    public void Moving_into_a_bench_at_a_walk_or_a_run_stops_at_it_sliding_along(Gait gait)
    {
        // At a sprint the bench is vaulted: A_sprint_vault_goes_over_a_bench_never_onto_it.
        var d = Go(Bench(), new(-2, 0, 0.01f), 360, Along(gait));
        Assert.Empty(d.Started);
        Assert.InRange(d.End.Feet.X, 0.69f, 0.7f);
        Assert.True(d.Highest < 0.01f, d.ToString());
    }

    [Fact]
    public void A_tap_of_jump_close_to_a_bench_moving_towards_it_hops_up_onto_it_and_walking_off_drops_to_the_ground()
    {
        var level = Bench();
        float onTop = float.NaN;
        var d = new Drove();
        var body = Drive.Settle(level, new(0.5f, 0, 0.01f));
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, 240, Along(Gait.Walk, press: 6), (i, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (m.Kind == TraversalKind.HopUp) d.Ticks[m.Kind] = d.Ticks.GetValueOrDefault(m.Kind) + 1;
            if (i == 6 + 50) onTop = b.Feet.Z;
        });
        Assert.Equal([TraversalKind.HopUp], d.Started);
        Assert.InRange(d.Ticks[TraversalKind.HopUp], 40, 44);       // 0.35 s
        Near(0.45f, onTop, 0.01f, "on the bench");
        Assert.True(d.End.Feet.X > 1.8f, $"still at {d.End.Feet}");
        Near(0f, d.End.Feet.Z, 0.01f, "walked off");
        Assert.Equal(BodyState.Ground, d.End.State);
    }

    [Fact]
    public void A_tap_beside_a_bench_without_moving_towards_it_is_a_plain_jump()
    {
        var d = Go(Bench(), new(0.5f, 0, 0.01f), 120, Along(Gait.Walk, press: 6, still: true));
        Assert.Empty(d.Started);
        Assert.InRange(d.Highest, 0.45f, 0.55f);
        Near(0f, d.End.Feet.Z, 0.01f, "landed");
    }

    [Fact]
    public void A_tap_at_a_wall_of_a_metre_climbs_onto_it()
    {
        // The obstacle decides; a tap is enough.
        var d = Go(Ledge(1.0f).Build(), new(0.5f, 0, 0.01f), 200, Along(Gait.Run, press: 6));
        Assert.Equal([TraversalKind.ClimbOnto], d.Started);
        Near(1.0f, d.End.Feet.Z, 0.01f, "on the wall's top");
        Assert.Equal(BodyState.Ground, d.End.State);
    }

    // ---------------------------------------------------------------- the hurdle

    [Theory]
    [InlineData(Gait.Sprint, true)]
    [InlineData(Gait.Run, false)]
    [InlineData(Gait.Walk, false)]
    public void Sprinting_into_a_thin_fence_hurdles_it_and_running_or_walking_into_it_stops(Gait gait, bool hurdles)
    {
        var d = Go(Fence(0.9f, x: 3f).Build(), new(-2, 0, 0.01f), 600, Along(gait));
        if (hurdles)
        {
            Assert.Equal([TraversalKind.Hurdle], d.Started);
            Assert.InRange(d.Ticks[TraversalKind.Hurdle], 46, 50);   // 0.4 s
            Assert.True(d.End.Feet.X > 3.5f, d.ToString());
            Assert.True(d.Highest > 0.9f, d.ToString());
            Near(0f, d.End.Feet.Z, 0.01f, "landed beyond");
        }
        else
        {
            Assert.Empty(d.Started);
            Assert.InRange(d.End.Feet.X, 2.69f, 2.7f);
        }
    }

    [Fact]
    public void A_sprint_vault_needs_ground_beyond_a_depth_to_0_7_m_and_a_height_to_a_metre()
    {
        // Over a drop of 2 m; a low wall deeper than the sprint vault's 0.7 m; a fence of 1.1 m.
        var drop = new TestLevel().Floor(-10, -10, 3.1f, 10).Floor(3.1f, -10, 10, 10, -2f).Box(new(3, -3, 0), new(3.1f, 3, 0.9f)).Build();
        var wall = Ground().Box(new(3, -3, 0), new(3.8f, 3, 0.8f)).Build();
        var tall = Fence(1.1f, x: 3f).Build();
        foreach (var level in new[] { drop, wall, tall })
        {
            var d = Go(level, new(-2, 0, 0.01f), 300, Along(Gait.Sprint));
            Assert.Empty(d.Started);
            Assert.InRange(d.End.Feet.X, 2.69f, 2.7f);
        }
    }

    // ---------------------------------------------------------------- vault, climb onto, mantle

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Jump_held_facing_a_fence_of_a_metre_vaults_it_at_any_pace(bool still)
    {
        var d = Go(Fence(1.0f).Build(), new(0.5f, 0, 0.01f), 240, Along(Gait.Walk, press: 6, hold: 60, still: still));
        Assert.Equal([TraversalKind.Vault], d.Started);
        Assert.Equal(0, d.Refused);
        Assert.True(d.End.Feet.X > 1.4f, d.ToString());
        Assert.True(d.Highest > 1.0f, d.ToString());
        Near(0f, d.End.Feet.Z, 0.01f, "down beyond");
    }

    [Fact]
    public void A_tap_at_a_fence_of_a_metre_vaults_it()
    {
        var d = Go(Fence(1.0f).Build(), new(0.5f, 0, 0.01f), 240, Along(Gait.Walk, press: 6, hold: 1));
        Assert.Equal([TraversalKind.Vault], d.Started);
        Assert.True(d.End.Feet.X > 1.4f, d.ToString());
        Near(0f, d.End.Feet.Z, 0.01f, "down beyond");
    }

    [Fact]
    public void Jump_held_at_a_deep_box_of_a_metre_climbs_onto_it()
    {
        var d = Go(Ledge(1.0f).Build(), new(0.5f, 0, 0.01f), 240, Along(Gait.Walk, press: 6, hold: 60));
        Assert.Equal([TraversalKind.ClimbOnto], d.Started);
        Assert.InRange(d.Ticks[TraversalKind.ClimbOnto], 82, 86);   // 0.7 s
        Near(1.0f, d.End.Feet.Z, 0.01f, "on the box");
        Assert.Equal(BodyState.Ground, d.End.State);
    }

    [Fact]
    public void Jump_held_at_a_ledge_of_1_6_m_mantles_onto_it()
    {
        var d = Go(Ledge(1.6f).Build(), new(0.5f, 0, 0.01f), 240, Along(Gait.Walk, press: 6, hold: 60, still: true));
        Assert.Equal([TraversalKind.Mantle], d.Started);
        Assert.InRange(d.Ticks[TraversalKind.Mantle], 106, 110);    // 0.9 s
        Near(1.6f, d.End.Feet.Z, 0.01f, "on the ledge");
        Assert.Equal(BodyState.Ground, d.End.State);
        Assert.Equal(0, d.End.Crouched);
    }

    [Fact]
    public void No_mantle_without_room_to_stand_and_one_under_a_low_ceiling_ends_crouched()
    {
        // 0.9 m over the ledge: less than a crouched body; 1.4 m: a crouched body, not a standing one.
        var none = Go(Ledge(1.6f).Box(new(1, -3, 2.5f), new(5, 3, 3.5f)).Build(), new(0.5f, 0, 0.01f), 200, Along(Gait.Walk, press: 6, hold: 60, still: true));
        Assert.Empty(none.Started);
        Near(0f, none.End.Feet.Z, 0.01f, "back on the ground");
        var low = Go(Ledge(1.6f).Box(new(1, -3, 3.0f), new(5, 3, 4f)).Build(), new(0.5f, 0, 0.01f), 200, Along(Gait.Walk, press: 6, hold: 60, still: true));
        Assert.Equal([TraversalKind.Mantle], low.Started);
        Near(1.6f, low.End.Feet.Z, 0.01f, "on the ledge");
        Assert.Equal(1, low.End.Crouched);
    }

    // ---------------------------------------------------------------- grab, hang, pull up, drop

    [Fact]
    public void Held_through_a_grab_at_a_ledge_of_2_6_m_pulls_up()
    {
        var d = Go(Ledge(2.6f).Build(), new(0.6f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, hold: 120));
        Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], d.Started);
        Assert.Equal(1, d.Grabs);
        Assert.InRange(d.Ticks[TraversalKind.PullUp], 94, 98);      // 0.8 s
        Near(2.6f, d.End.Feet.Z, 0.01f, "on top");
        Assert.Equal(BodyState.Ground, d.End.State);
    }

    [Fact]
    public void A_tap_under_a_low_lip_grabs_it_forward_held_pulls_up_and_nothing_held_hangs()
    {
        // The tap takes the lip of 2.2 m (within the hands' reach at its first tick); at the grab's end
        // forward held pulls up at once, and with nothing held the body hangs.
        var level = Ledge(2.2f).Build();
        var on = Go(level, new(0.6f, 0, 0.01f), 240, Along(Gait.Walk, press: 6, hold: 3));
        Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], on.Started);
        Near(2.2f, on.End.Feet.Z, 0.01f, "on top");
        var still = Go(level, new(0.6f, 0, 0.01f), 200, Along(Gait.Walk, press: 6, hold: 3, still: true));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang], still.Started);
        Assert.Equal(BodyState.Hanging, still.End.State);
    }

    /// <summary>
    /// Jump down from the press until the hands take the lip, then let go; forward until then (or as
    /// <paramref name="forward"/> says); crouch pressed at <paramref name="crouchAt"/>.
    /// </summary>
    static Func<int, Body, MoveIntent> GrabThenLetGo(int crouchAt = -1, Func<int, bool>? forward = null, int jumpAt = -1)
    {
        bool took = false;
        return (i, b) =>
        {
            if (b.State != BodyState.Air && b.State != BodyState.Ground) took = true;
            var m = (forward is null ? !took : forward(i)) ? Drive.Toward(1, 0, Gait.Walk) : new MoveIntent { Facing = 0f, Faces = true };
            m.Jump = i == 6 || i == jumpAt;
            m.JumpHeld = (i >= 6 && !took) || i == jumpAt;
            m.Crouch = crouchAt >= 0 && i >= crouchAt;
            return m;
        };
    }

    [Fact]
    public void Nothing_held_at_the_grab_s_end_hangs_and_crouch_drops()
    {
        var level = Ledge(2.6f).Build();
        var hang = Go(level, new(0.6f, 0, 0.01f), 200, GrabThenLetGo());
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang], hang.Started);
        Assert.Equal(BodyState.Hanging, hang.End.State);
        Near(2.6f - 2.3f, hang.End.Feet.Z, 0.01f, "hanging at arm's length");
        Near(2.6f, hang.LastMove.Lip.Z, 0.01f, "the lip");
        Near(1f, -hang.LastMove.LipNormal.X, 0.01f, "the face looks back");

        var drop = Go(level, new(0.6f, 0, 0.01f), 300, GrabThenLetGo(crouchAt: 150));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang, TraversalKind.Drop], drop.Started);
        Assert.Equal(1, drop.Drops);
        Near(0f, drop.End.Feet.Z, 0.01f, "dropped to the ground");
        Assert.Equal(BodyState.Ground, drop.End.State);
    }

    [Fact]
    public void Forward_held_at_the_grab_s_end_pulls_up_and_hanging_jump_or_a_fresh_forward_pulls_up()
    {
        var level = Ledge(2.6f).Build();
        var jump = Go(level, new(0.6f, 0, 0.01f), 300, GrabThenLetGo(jumpAt: 150));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang, TraversalKind.PullUp], jump.Started);
        Near(2.6f, jump.End.Feet.Z, 0.01f, "on top");

        // Forward held all through pulls up at the grab's end; off by then, the body hangs, and forward pressed
        // again pulls up.
        var held = Go(level, new(0.6f, 0, 0.01f), 300, GrabThenLetGo(forward: _ => true));
        Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], held.Started);
        Near(2.6f, held.End.Feet.Z, 0.01f, "on top");
        var again = Go(level, new(0.6f, 0, 0.01f), 300, GrabThenLetGo(forward: i => i > 150));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang, TraversalKind.PullUp], again.Started);
        Near(2.6f, again.End.Feet.Z, 0.01f, "on top");
    }

    [Fact]
    public void A_tap_towards_a_ledge_of_2_75_m_grabs_it_and_pulls_up_and_one_of_3_m_is_a_plain_jump()
    {
        var d = Go(Ledge(2.75f).Build(), new(0.6f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, hold: 1));
        Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], d.Started);
        Near(2.75f, d.End.Feet.Z, 0.01f, "on top");
        var high = Go(Ledge(3.0f).Build(), new(0.6f, 0, 0.01f), 200, Along(Gait.Walk, press: 6, hold: 1));
        Assert.Empty(high.Started);
        Near(0f, high.End.Feet.Z, 0.01f, "back on the ground");
    }

    [Fact]
    public void A_lip_with_no_room_above_is_held_but_not_pulled_up_onto()
    {
        // A ledge of 2.6 m with a slab 0.8 m over it: the hands hold it; there is no room to pull up.
        var d = Go(Ledge(2.6f).Box(new(1, -3, 3.4f), new(5, 3, 4.5f)).Build(), new(0.6f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, hold: 200));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang], d.Started);
        Assert.Equal(BodyState.Hanging, d.End.State);
    }

    // ---------------------------------------------------------------- crouch and slopes

    [Fact]
    public void Crouching_lowers_and_slows_the_body_and_keeps_it_down_under_a_low_beam()
    {
        // A beam whose underside is 1.3 m up, from x 2 to 3.
        var level = Ground().Box(new(2, -3, 1.3f), new(3, 3, 1.6f)).Build();
        var standing = Go(level, new(0, 0, 0.01f), 240, Along(Gait.Run));
        Assert.InRange(standing.End.Feet.X, 1.69f, 1.7f);

        var level2 = level;
        var body = Drive.Settle(level2, new(0, 0, 0.01f));
        var move = default(Traversal);
        float x60 = 0f, x180 = 0f;
        int stoodUnder = 0;
        bool crouchedAfter = false;
        // Crouch held until x 2.5 (under the beam), then let go.
        body = Drive.Moves(level2, body, ref move, 600, (_, b) => Along(Gait.Sprint, crouch: b.Feet.X < 2.5f)(0, b), (i, b, _, _) =>
        {
            if (i == 60) x60 = b.Feet.X;
            if (i == 180) x180 = b.Feet.X;
            if (b.Feet.X > 1.7f && b.Feet.X < 3.29f && b.Crouched == 0) stoodUnder++;
            if (b.Feet.X > 3.4f && b.Crouched == 0) crouchedAfter = true;
        });
        Near(1.0f, x180 - x60, 0.01f, "a second's crouched walk");
        Assert.Equal(0, stoodUnder);
        Assert.True(crouchedAfter, "never stood up again");
        Assert.True(body.Feet.X > 3.4f, $"stopped at {body.Feet}");
    }

    [Theory]
    [InlineData(39f, true)]
    [InlineData(42f, false)]
    public void Ground_is_walked_up_to_40_degrees_and_stood_on_without_sliding_to_45(float degrees, bool walks)
    {
        var level = Ground().Ramp(2, 4, -3, 3, degrees).Build();
        var d = Go(level, new(0, 0, 0.01f), 480, Along(Gait.Run, stop: 4.5f));
        if (walks) Assert.True(d.End.Feet.Z > 1.5f, d.ToString());
        else Assert.True(d.Highest < 0.36f, d.ToString());
        // Put on the slope and left: it stays.
        float up = 2f * DetMath.Tan(degrees * DetMath.DegToRad);
        var body = Drive.Settle(level, new(4, 0, up + 0.3f));
        var then = body.Feet;
        body = Drive.Run(level, body, 240, (_, _) => new MoveIntent());
        Assert.Equal(BodyState.Ground, body.State);
        Assert.True(Vector3.Distance(then, body.Feet) < 0.005f, $"crept from {then} to {body.Feet}");
    }

    // ---------------------------------------------------------------- 1.1's Old Town (a cooked package; skipped when absent)

    static readonly Lazy<LevelSpace?> OldTown = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_EXITS_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    [Fact]
    public void In_the_old_town_jump_held_at_the_stair_blocks_wall_of_1_33_m_mantles_onto_it()
    {
        Assert.SkipUnless(OldTown.Value is not null, "no cooked Old Town (VITOMIR_EXITS_LEVEL, e.g. P1_OTDY cooked by tools/platform/cook-1-1-level.ps1)");
        // The street at z −1.33 by the stair block's curved wall (x 46.7), whose top is the landing at 0.
        var d = Go(OldTown.Value!, new(45.9f, 6.22f, -1.3f), 240, Along(Gait.Walk, press: 30, hold: 120, still: true));
        Assert.Equal([TraversalKind.Mantle], d.Started);
        Near(0f, d.End.Feet.Z, 0.01f, "on the landing");
        Assert.Equal(BodyState.Ground, d.End.State);
    }

    [Fact]
    public void In_the_old_town_a_tap_walking_at_the_stair_blocks_low_wall_hops_up_onto_it()
    {
        Assert.SkipUnless(OldTown.Value is not null, "no cooked Old Town (VITOMIR_EXITS_LEVEL, e.g. P1_OTDY cooked by tools/platform/cook-1-1-level.ps1)");
        // A landing at z −0.44 and the wall's top at 0 to its west (x 47.56): 0.44 m, as a bench. (1.1's own
        // benches are props the cook leaves out of the collision.)
        var d = Go(OldTown.Value!, new(48.3f, 6.47f, -0.4f), 150, (i, _) =>
        {
            var m = Drive.Toward(-1, 0, Gait.Walk);
            m.Jump = m.JumpHeld = i == 48;
            return m;
        }, yaw: DetMath.Pi);
        Assert.Equal([TraversalKind.HopUp], d.Started);
        Near(0f, d.End.Feet.Z, 0.01f, "on the wall");
    }

    // ---------------------------------------------------------------- the world: events, digests, the eye, allocation

    static LevelSpace Course() => Ground()
        .Box(new(3, -3, 0), new(3.1f, 3, 0.9f))          // a fence to hurdle along +X
        .Box(new(3.8f, 2, 0), new(8, 6, 1.6f))           // past it, a ledge to mantle onto along +Y
        .Build("course");

    internal const int CourseTicks = 700;

    /// <summary>The player's actions: sprint along +X over the fence, turn to +Y, walk to the ledge and hold jump there, then crouch a while.</summary>
    internal static ActionFrame Script(int t)
    {
        var none = new Vector2(0.5f, 0.5f);
        if (t < 160) return new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Sprint, none);
        if (t == 160) return new ActionFrame(Vector2.Zero, new Vector2(DetMath.Pi * 0.5f, 0), Buttons.None, none);   // turn to +Y
        if (t < 200) return ActionFrame.Idle;
        if (t < 420) return new ActionFrame(new Vector2(0, 1), Vector2.Zero, t >= 330 && t < 400 ? Buttons.Jump : Buttons.None, none);
        if (t < 540) return new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Crouch, none);
        return ActionFrame.Idle;
    }

    internal static World CourseWorld(out EntityId player)
    {
        var w = MovementServices.Register(new WorldBuilder(7), Course()).Build();
        player = MovementServices.SpawnControlled(w, new(-2, -1, 0.01f), 0f, Locomotion.Sandbox);
        return w;
    }

    [Fact]
    public void A_course_of_moves_reports_its_events_and_replays_to_the_same_digest_at_every_tick()
    {
        List<ulong> Run(List<string> said)
        {
            var w = CourseWorld(out _);
            var digests = new List<ulong>();
            for (int t = 0; t < CourseTicks; t++)
            {
                MovementServices.Step(w, Script(t));
                digests.Add(w.Digest());
                foreach (var e in w.Events<TraversalStarted>().ThisTick) said.Add($"start {e.Kind}");
                foreach (var e in w.Events<TraversalEnded>().ThisTick) said.Add($"end {e.Kind} {e.Completed}");
            }
            return digests;
        }
        var saidA = new List<string>();
        var saidB = new List<string>();
        var a = Run(saidA);
        var b = Run(saidB);
        Assert.Equal(a, b);
        Assert.Equal(saidA, saidB);
        Assert.Contains("start Hurdle", saidA);
        Assert.Contains("end Hurdle True", saidA);
        Assert.Contains("start Mantle", saidA);
        Assert.Contains("end Mantle True", saidA);
    }

    [Fact]
    public void The_eye_goes_down_with_a_crouch_and_up_again()
    {
        var w = CourseWorld(out var player);
        var none = new Vector2(0.5f, 0.5f);
        for (int t = 0; t < 30; t++) MovementServices.Step(w, ActionFrame.Idle);
        float standing = w.Service<ViewState>().Eyes.Z - w.Get<Body>(player).Feet.Z;
        for (int t = 0; t < 60; t++) MovementServices.Step(w, new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Crouch, none));
        float crouched = w.Service<ViewState>().Eyes.Z - w.Get<Body>(player).Feet.Z;
        for (int t = 0; t < 60; t++) MovementServices.Step(w, ActionFrame.Idle);
        float again = w.Service<ViewState>().Eyes.Z - w.Get<Body>(player).Feet.Z;
        Near(1.65f, standing, 0.01f, "standing eyes");
        Near(1.65f - 0.65f, crouched, 0.01f, "crouched eyes");
        Near(1.65f, again, 0.01f, "stood up");
    }
}
