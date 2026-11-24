// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The physics step: `simThing::MovePhysics` and the contact it runs into
/// (`simThing::OnSurfaceContact`).
///
/// Most of this needs no level and no install, because the integrator and the contact response are
/// arithmetic on one thing. What does need a level is the part that decides whether a move is
/// refused, and that part is Vitomir's own — see <see cref="Physics"/>.
/// </summary>
public class PhysicsTests
{
    static WorldThing Thing(uint moveType, Vector3 at = default) => new()
    {
        Index = -1,
        Guid = 1,
        Scene = NsdThing.Placed("test", at, Vector3.Zero, 0),
        Template = "test",
        MoveType = moveType,
        Position = at,
    };

    /// <summary>A physics thing with gravity switched on, which is what a script or a death does.</summary>
    static WorldThing Falling(Vector3 at = default)
    {
        var thing = Thing(WorldThing.MovePhysics, at);
        thing.PhysicsFlags = Physics.Gravity;
        return thing;
    }

    // ---- the integrator -------------------------------------------------------------------------

    [Fact]
    public void Gravity_is_two_hundred_units_a_second_squared_straight_down()
    {
        var thing = Falling();

        Physics.Integrate(thing, 1f, out _);

        // The direction is a hard-coded unit vector and the strength is the thing's own default.
        Assert.Equal(new Vector3(0f, 0f, -200f), thing.Velocity);
        Assert.Equal(200f, thing.Gravity);
        Assert.Equal(new Vector3(0f, 0f, -1f), Physics.Down);
    }

    [Fact]
    public void The_position_integrates_the_velocity_gravity_has_already_changed()
    {
        var thing = Falling();

        Physics.Integrate(thing, 1f, out var target);

        // Semi-implicit Euler: v is updated first, then p uses the *new* v. Explicit Euler would
        // have left the thing at z = 0 after the first tick instead of z = -200.
        Assert.Equal(-200f, target.Z, 3);
        Assert.NotEqual(0f, target.Z);
    }

    [Fact]
    public void What_is_stored_is_the_frames_delta_v_and_not_an_acceleration()
    {
        var thing = Falling();

        Physics.Integrate(thing, 0.5f, out _);

        // 200 × 0.5 = 100, not 200. The collide sweep subtracts a fraction of this back off, so it
        // has to be in the same units as the velocity.
        Assert.Equal(new Vector3(0f, 0f, -100f), thing.AppliedDeltaV);
    }

    [Fact]
    public void A_physics_thing_does_not_fall_until_the_gravity_flag_is_set()
    {
        var thing = Falling();
        thing.PhysicsFlags = 0;                 // what becoming MOVETYPE 3 actually leaves

        Physics.Integrate(thing, 1f, out var target);

        Assert.Equal(Vector3.Zero, thing.Velocity);
        Assert.Equal(Vector3.Zero, thing.AppliedDeltaV);
        Assert.Equal(Vector3.Zero, target);
    }

    [Fact]
    public void Nothing_but_a_movetype_three_thing_has_physics_at_all()
    {
        var door = Thing(moveType: 1);          // a walker
        door.PhysicsFlags = Physics.Gravity;
        door.Velocity = new Vector3(0f, 0f, 500f);

        Assert.False(Physics.Integrate(door, 1f, out var target));
        Assert.Equal(Vector3.Zero, target);     // and it did not budge
    }

    [Fact]
    public void A_sleeping_thing_is_skipped_before_anything_else_happens()
    {
        var thing = Falling();
        thing.PhysicsFlags |= Physics.Asleep;
        thing.Velocity = new Vector3(10f, 0f, 0f);

        Assert.False(Physics.Integrate(thing, 1f, out _));
        // Gravity did not even accumulate: the flag is tested first, at the top of the function.
        Assert.Equal(new Vector3(10f, 0f, 0f), thing.Velocity);
    }

    [Fact]
    public void Rotational_velocity_is_three_numbers_and_integrates_on_its_own()
    {
        var thing = Falling();
        thing.PhysicsFlags = 0;
        thing.RotVelocity = new Vector3(10f, 20f, 40f);

        Physics.Integrate(thing, 0.25f, out _);

        Assert.Equal(new Vector3(2.5f, 5f, 10f), thing.Orientation);
    }

    // ---- the contact ----------------------------------------------------------------------------

    static readonly Vector3 Floor = new(0f, 0f, 1f);

    [Fact]
    public void A_bounce_is_a_restitution_impulse_plus_the_engines_spare_tenth()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(0f, 0f, -100f);

        Assert.True(Physics.Contact(thing, Floor));

        // v += (-(e+1)·vn + 0.1)·n  with e = 1 and vn = -100  →  +200.1, then × 0.8 damping.
        Assert.Equal((-100f + 200.1f) * 0.8f, thing.Velocity.Z, 3);
    }

    [Fact]
    public void The_damping_is_applied_to_every_axis_not_just_the_normal()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(50f, -30f, -100f);

        Physics.Contact(thing, Floor);

        // The impulse is along the normal; the 0.8 is not.
        Assert.Equal(40f, thing.Velocity.X, 3);
        Assert.Equal(-24f, thing.Velocity.Y, 3);
    }

    [Fact]
    public void A_thing_already_moving_away_fast_enough_is_not_in_contact()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(0f, 0f, 5f);       // rising, faster than the 1-unit gate

        Assert.False(Physics.Contact(thing, Floor));
        Assert.Equal(new Vector3(0f, 0f, 5f), thing.Velocity);
    }

    /// <summary>
    /// The oddest line in the function, and the reason `bounceminimum` exists: a contact slower than
    /// one unit per second is bounced *as if* it were exactly one, so a barely-touching thing is
    /// kicked at a fixed rate rather than a proportional one.
    /// </summary>
    [Fact]
    public void A_grazing_contact_is_given_the_same_kick_as_a_one_unit_one()
    {
        var crawling = Falling();
        crawling.Velocity = new Vector3(0f, 0f, -0.001f);
        var faster = Falling();
        faster.Velocity = new Vector3(0f, 0f, -1f);

        Physics.Contact(crawling, Floor);
        Physics.Contact(faster, Floor);

        // Both were treated as vn = -1: the impulse is identical and only the starting velocity
        // they were added to differs.
        Assert.Equal(2.1f, crawling.Velocity.Z / 0.8f + 0.001f, 3);
        Assert.Equal(2.1f, faster.Velocity.Z / 0.8f + 1f, 3);
    }

    /// <summary>
    /// **Both untextbook additions, applied unconditionally.** They used to be the `bounceminimum`
    /// and `bounceboost` Fixes; both switches were removed, by design: the base game's
    /// own janky physics for things, so the engine always does what 1.1 does and
    /// these are the numbers that proves it.
    /// </summary>
    [Fact]
    public void A_slow_contact_is_bounced_as_if_it_arrived_at_a_full_unit()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(0f, 0f, -0.001f);

        Physics.Contact(thing, Floor);

        // The -1 floor replaces the real -0.001, so the impulse is -(2 x -1) + 0.1 = 2.1 either way.
        Assert.Equal((-0.001f + 2.1f) * 0.8f, thing.Velocity.Z, 4);
    }

    /// <summary>The bare 0.1 is in every impulse, however fast the contact was.</summary>
    [Fact]
    public void Every_bounce_carries_the_engines_spare_tenth()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(0f, 0f, -100f);

        Physics.Contact(thing, Floor);

        Assert.Equal((-100f + 200.1f) * 0.8f, thing.Velocity.Z, 3);
    }

    /// <summary>
    /// The register no longer offers a bounce switch at all — the point of removing them was that
    /// a branch nobody wants is a branch that still has to stay correct.
    /// </summary>
    [Fact]
    public void The_register_offers_no_bounce_switch()
    {
        var fixes = new Fixes();
        Assert.Equal("faithful (no fixes)", fixes.ToString());
        Assert.DoesNotContain(fixes.All, f => f.Name.StartsWith("bounce", StringComparison.Ordinal));
        Assert.False(fixes["bounceminimum"]);
        Assert.False(fixes["bounceboost"]);
    }

    [Fact]
    public void A_contact_is_audible_only_past_ten_units_a_second()
    {
        var quiet = Falling();
        quiet.Velocity = new Vector3(0f, 0f, -9f);
        var loud = Falling();
        loud.Velocity = new Vector3(0f, 0f, -11f);

        Physics.Contact(quiet, Floor);
        Physics.Contact(loud, Floor);

        Assert.False(quiet.Audible);
        Assert.True(loud.Audible);
    }

    /// <summary>
    /// The rest thresholds are not the same on all three axes — 1 for X and Y, **10** for Z, from two
    /// different constants. Easy to miss and easy to "tidy" into one number.
    /// </summary>
    [Fact]
    public void The_vertical_rest_threshold_is_ten_times_the_horizontal_one()
    {
        Assert.Equal(1f, Physics.RestSpeedXY);
        Assert.Equal(10f, Physics.RestSpeedZ);

        var sideways = Falling();
        sideways.Velocity = new Vector3(5f, 0f, -0.5f);      // would sleep on Z, not on X
        Physics.Contact(sideways, Floor);
        Assert.Equal(0u, sideways.PhysicsFlags & Physics.Asleep);
    }

    [Fact]
    public void A_settling_thing_goes_to_sleep_and_stops_being_stepped()
    {
        var thing = Falling();
        thing.Velocity = new Vector3(0f, 0f, -0.5f);

        Physics.Contact(thing, Floor);

        Assert.Equal(Physics.Asleep, thing.PhysicsFlags & Physics.Asleep);
        // And the integrator now refuses it outright, which is what "asleep" is for.
        Assert.False(Physics.Integrate(thing, 1f, out _));
    }

    /// <summary>
    /// The whole point of the sleep: a thing dropped on a floor has to actually stop. Run the two
    /// halves against each other until they settle, with no level in the way.
    /// </summary>
    [Fact]
    public void A_dropped_thing_settles_rather_than_bouncing_for_ever()
    {
        // From 100 units up. The height matters: each bounce keeps about 80% of its speed, so a
        // drop from 500 takes the better part of twenty seconds of game time to damp below the
        // rest threshold, which is the engine's own arithmetic and not something to tune away.
        var thing = Falling(new Vector3(0f, 0f, 100f));

        for (int tick = 0; tick < 900; tick++)
        {
            if (!Physics.Integrate(thing, 1f / 30f, out var target)) break;
            if (target.Z <= 0f) Physics.Contact(thing, Floor);      // a floor at z = 0
            else thing.Position = target;
        }

        Assert.Equal(Physics.Asleep, thing.PhysicsFlags & Physics.Asleep);
    }
}
