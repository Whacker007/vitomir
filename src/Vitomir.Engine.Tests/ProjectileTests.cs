// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Firing something: `simCombat::FireProjectile` and the aim the two natives do around
/// it.
///
/// The aim and the launch frame are arithmetic and need no install. What needs one is a projectile
/// actually leaving a character with the speed its template gives it.
/// </summary>
public class ProjectileTests
{
    static WorldThing Thing(Vector3 at = default, Vector3 facing = default,
                            CollideShape shape = CollideShape.None, float radius = 0f, float height = 0f) =>
        new()
        {
            Index = -1, Guid = 1, Scene = NsdThing.Placed("test", at, facing, 0), Template = "test",
            Position = at, Orientation = facing, Collide = shape, Radius = radius, Height = height,
        };

    // ---- which way is forward -------------------------------------------------------------------

    /// <summary>
    /// The whole launch rests on one identity: the orientation <see cref="Projectiles.Facing"/>
    /// builds must turn a thing's **+Y** into the direction asked for. If it does not, every
    /// projectile template's `PHYSICS_VELOCITY 0, s, 0` points somewhere else.
    /// </summary>
    [Fact]
    public void Facing_a_direction_puts_the_forward_axis_along_it()
    {
        var rng = new Rng(31);
        for (int i = 0; i < 2_000; i++)
        {
            var want = Physics.Direction(rng);
            if (want == Vector3.Zero) continue;
            var thing = Thing(facing: Projectiles.Facing(want));
            var got = Vector3.TransformNormal(new Vector3(0f, 1f, 0f), thing.Rotation);
            Assert.True((got - want).Length() < 1e-3f, $"wanted {want}, forward came out {got}");
        }
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]     // due forward
    [InlineData(0f, -1f, 0f)]    // behind
    [InlineData(1f, 0f, 0f)]
    [InlineData(-1f, 0f, 0f)]
    [InlineData(0f, 0f, 1f)]     // straight up, where the yaw stops meaning anything
    [InlineData(0f, 0f, -1f)]
    public void Facing_survives_the_six_axes_including_straight_up(float x, float y, float z)
    {
        var want = new Vector3(x, y, z);
        var thing = Thing(facing: Projectiles.Facing(want));
        var got = Vector3.TransformNormal(new Vector3(0f, 1f, 0f), thing.Rotation);
        Assert.True((got - want).Length() < 1e-4f, $"wanted {want}, forward came out {got}");
    }

    // ---- the velocity frame, which is the bug this closes -----------------------------------------

    /// <summary>
    /// `PHYSICS_VELOCITY` is in the thing's **own** frame and the finalize rotates it into the world
    /// once. A projectile aimed along world −X therefore leaves with its
    /// speed along world −X, not along +Y.
    /// </summary>
    [Fact]
    public void A_physics_things_template_velocity_is_rotated_into_the_world_as_it_enters()
    {
        var shot = new WorldThing
        {
            Index = -1, Guid = 1, Scene = new NsdThing(), Template = "t",
            MoveType = WorldThing.MovePhysics,
            Orientation = Projectiles.Facing(new Vector3(-1f, 0f, 0f)),
            Velocity = new Vector3(0f, 1000f, 0f),      // the shipped shape: forward, and nothing else
        };

        Location.LaunchVelocity(shot);

        Assert.Equal(-1000f, shot.Velocity.X, 1);
        Assert.Equal(0f, shot.Velocity.Y, 1);
        Assert.Equal(0f, shot.Velocity.Z, 1);
    }

    [Fact]
    public void Nothing_but_a_physics_thing_has_its_velocity_touched()
    {
        var walker = new WorldThing
        {
            Index = -1, Guid = 1, Scene = new NsdThing(), Template = "t",
            MoveType = 1, Orientation = new Vector3(0f, 0f, 90f),
            Velocity = new Vector3(0f, 1000f, 0f),
        };
        Location.LaunchVelocity(walker);
        Assert.Equal(new Vector3(0f, 1000f, 0f), walker.Velocity);
    }

    // ---- the muzzle and the aim point ------------------------------------------------------------

    /// <summary>
    /// Half the **height** for a cylinder — which is every character and 1,766 of the 2,619 shipped
    /// templates — and half the **radius** for anything else. A thing with no body is aimed at its
    /// own origin.
    /// </summary>
    [Theory]
    [InlineData(CollideShape.Cylinder, 32f, 72f, 36f)]
    [InlineData(CollideShape.Sphere, 32f, 72f, 16f)]
    [InlineData(CollideShape.Box, 32f, 72f, 16f)]
    [InlineData(CollideShape.None, 0f, 0f, 0f)]
    public void A_shot_is_aimed_at_the_middle_of_a_body(CollideShape shape, float radius,
                                                        float height, float expected)
    {
        var thing = Thing(new Vector3(100f, 200f, 300f), shape: shape, radius: radius, height: height);
        Assert.Equal(expected, Projectiles.MidHeight(thing), 3);
        Assert.Equal(new Vector3(100f, 200f, 300f + expected), Projectiles.Aim(thing));
    }

    /// <summary>
    /// The caller's offset is **body space**: a script says "ten units in front of me" and the
    /// engine rotates it, so the script never has to know which way the character faces.
    /// </summary>
    [Fact]
    public void The_muzzle_offset_is_rotated_into_the_firers_frame()
    {
        var facing = Projectiles.Facing(new Vector3(1f, 0f, 0f));      // pointing along world +X
        var firer = Thing(new Vector3(0f, 0f, 0f), facing, CollideShape.Cylinder, 32f, 72f);

        var muzzle = Projectiles.Muzzle(firer, new Vector3(0f, 10f, 0f));   // ten units forward

        Assert.Equal(10f, muzzle.X, 2);     // which is world +X, because that is where he looks
        Assert.Equal(0f, muzzle.Y, 2);
        Assert.Equal(36f, muzzle.Z, 2);     // and half his height up
    }

    [Fact]
    public void A_zero_offset_leaves_the_muzzle_at_mid_height_wherever_the_firer_faces()
    {
        var firer = Thing(new Vector3(5f, 7f, 9f), new Vector3(10f, 20f, 30f),
                          CollideShape.Cylinder, 32f, 80f);
        Assert.Equal(new Vector3(5f, 7f, 49f), Projectiles.Muzzle(firer, Vector3.Zero));
    }

    // ---- the query itself ------------------------------------------------------------------------

    static WorldThing At(int guid, Vector3 where) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Position = where, Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f,
        Actor = new Actor { Health = 100f, MaxHealth = 100f },
    };

    /// <summary>
    /// The piece three features were waiting on: **every** thing a ray meets, nearest first, rather
    /// than only the nearest. That ordering is what lets a shot walk past its owner and a corpse to
    /// reach whoever is behind them.
    /// </summary>
    [Fact]
    public void A_trace_answers_everything_it_met_in_order()
    {
        var far = At(1, new Vector3(0f, 600f, 0f));
        var near = At(2, new Vector3(0f, 200f, 0f));
        var middle = At(3, new Vector3(0f, 400f, 0f));
        var behind = At(4, new Vector3(0f, -200f, 0f));   // the wrong way down the ray

        var found = World.Pick.Trace(null, [far, near, middle, behind],
                                     new Vector3(0f, 0f, 36f), new Vector3(0f, 1f, 0f), far: 10_000f);

        Assert.Equal([2, 3, 1], found.Select(c => c.Thing!.Guid));
        Assert.All(found, c => Assert.Equal(World.ContactKind.Thing, c.Kind));
        Assert.True(found[0].Distance < found[1].Distance && found[1].Distance < found[2].Distance);
    }

    [Fact]
    public void A_trace_honours_its_exclusion_and_its_range()
    {
        var a = At(1, new Vector3(0f, 200f, 0f));
        var b = At(2, new Vector3(0f, 400f, 0f));
        var origin = new Vector3(0f, 0f, 36f);
        var along = new Vector3(0f, 1f, 0f);

        Assert.Equal([2], World.Pick.Trace(null, [a, b], origin, along, 10_000f, ignore: a)
                                    .Select(c => c.Thing!.Guid));
        // 300 units of range cannot reach the one at 400.
        Assert.Equal([1], World.Pick.Trace(null, [a, b], origin, along, far: 300f)
                                    .Select(c => c.Thing!.Guid));
        // And a query that does not ask for things gets none, whatever is in the way.
        Assert.Empty(World.Pick.Trace(null, [a, b], origin, along, 10_000f,
                                      World.Pick.Tests.Surfaces));
    }

    /// <summary>
    /// The two encodings, asserted so nobody later "tidies" them into one: a **query** asks with
    /// `0x1` things and `0x2` surfaces, a **result** comes back `0x2` thing and `0x4` surface.
    /// </summary>
    [Fact]
    public void The_query_flags_and_the_result_kinds_are_deliberately_different_numbers()
    {
        Assert.Equal(1, (int)World.Pick.Tests.Things);
        Assert.Equal(2, (int)World.Pick.Tests.Surfaces);
        Assert.Equal(2, (int)World.ContactKind.Thing);
        Assert.Equal(4, (int)World.ContactKind.Surface);
    }

    // ---- the damage pool -------------------------------------------------------------------------

    /// <summary>
    /// `PROJECT_DAMAGE` is **ten times a dice count**, not hit points: `floor(value / 10)` rolls of
    /// 1 to 10, plus `rand01 x` the remainder. So `arrow` (40) is four d10 and can never do less
    /// than 4 or more than 40, and `proj_bullet` (65) is six d10 plus up to five.
    /// </summary>
    [Theory]
    [InlineData(40, 4f, 40f)]        // arrow
    [InlineData(65, 6f, 65f)]        // proj_bullet
    [InlineData(250, 25f, 250f)]     // proj_rocket
    [InlineData(5, 0f, 5f)]          // under one die: nothing but the remainder
    [InlineData(1, 0f, 1f)]
    [InlineData(0, 0f, 0f)]
    public void The_damage_is_a_pool_of_ten_sided_dice(int damage, float least, float most)
    {
        var rng = new Rng(41);
        float low = float.MaxValue, high = float.MinValue, sum = 0f;
        const int runs = 20_000;
        for (int i = 0; i < runs; i++)
        {
            float rolled = Projectiles.Roll(damage, rng);
            Assert.InRange(rolled, least, most);
            low = MathF.Min(low, rolled); high = MathF.Max(high, rolled); sum += rolled;
        }
        // The mean of n dice of 1..10 plus half the remainder is about 0.55 of the tag.
        if (damage >= 10) Assert.Equal(damage * 0.55f, sum / runs, damage * 0.03f);
        // And over twenty thousand rolls it should have come close to both ends.
        if (damage > 0) Assert.True(high > (least + most) / 2f, $"never rolled high: {high} of {most}");
    }

    [Fact]
    public void A_single_die_is_one_to_ten_and_reaches_both_ends()
    {
        // Seed 20057's first draw is 0 and seed 30091's is RAND_MAX, so those are the two extremes.
        Assert.Equal(1f, Projectiles.Roll(10, new Rng(20057)), 3);
        Assert.Equal(10f, Projectiles.Roll(10, new Rng(30091)), 3);
    }

    // ---- what a shot passes through, and what stops it --------------------------------------------

    static WorldThing Body(ThingType type, ActorType kind = ActorType.Human, uint flags = 0) => new()
    {
        Index = -1, Guid = 99, Scene = new NsdThing(), Template = "t", Type = type,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f,
        Actor = type is ThingType.Actor or ThingType.Player
            ? new Actor { Type = kind, Flags = flags, Health = 100f, MaxHealth = 100f } : null,
    };

    static WorldThing Shot(uint flags = 0, int damage = 40, uint type = 0) => new()
    {
        Index = -1, Guid = 7, Scene = new NsdThing(), Template = "arrow",
        Type = ThingType.Projectile, MoveType = WorldThing.MovePhysics,
        ProjectileFlags = flags, ProjectileDamage = damage, ProjectileDamageType = type,
    };

    /// <summary>
    /// The three refusals, and the second is the one worth remembering: **you cannot shoot a
    /// corpse.** The shot goes through it and carries on to whatever is behind.
    /// </summary>
    [Fact]
    public void A_shot_passes_through_a_corpse()
    {
        var shot = Shot();
        var corpse = Body(ThingType.Actor);
        corpse.Actor!.Flags |= Actor.Flag.Dead;
        Assert.False(Projectiles.OnThingContact(shot, corpse, new Rng(1)));
    }

    [Fact]
    public void A_shot_never_hits_whoever_fired_it()
    {
        var shot = Shot();
        var firer = Body(ThingType.Player);
        shot.SpawnedBy = firer.Guid;
        Assert.False(Projectiles.OnThingContact(shot, firer, new Rng(1)));
    }

    /// <summary>
    /// Five of the nine thing types are passed straight through — an item, a region, another
    /// projectile, a weapon and armour. So a shot is not stopped by the sword lying on the floor
    /// between you and the archer.
    /// </summary>
    [Theory]
    [InlineData(ThingType.Item)]
    [InlineData(ThingType.Region)]
    [InlineData(ThingType.Projectile)]
    [InlineData(ThingType.Weapon)]
    [InlineData(ThingType.Armor)]
    public void Five_thing_types_are_passed_straight_through(ThingType type) =>
        Assert.False(Projectiles.OnThingContact(Shot(), Body(type), new Rng(1)));

    /// <summary>A shot fired by one party member cannot hit another.</summary>
    [Fact]
    public void A_party_members_shot_cannot_hit_another_party_member()
    {
        var shot = Shot();
        var comrade = Body(ThingType.Player, ActorType.Vampire, Actor.Flag.Party);
        // With no owner in the world there is nobody to be friendly with, so it lands.
        Assert.True(Projectiles.Hurt(shot, comrade, new Rng(1)));
        Assert.True(comrade.Actor!.Health < 100f);
    }

    /// <summary>
    /// `PROJ_BULLET` rewrites the shot's own damage type against the undead: bashing when the
    /// **accuracy margin** is under twenty, lethal at twenty or more (the hit compares the roll's
    /// answer — this test said "points rolled" until then, and so did the code). A human takes
    /// whatever the template said. Forty shots, so both answers come up for the undead.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Vampire, true)]
    [InlineData(ActorType.Ghoul, true)]
    [InlineData(ActorType.Human, false)]
    [InlineData(ActorType.Monster, false)]
    public void A_bullet_is_bashing_against_the_undead_unless_it_was_well_placed(ActorType kind, bool rewritten)
    {
        var seen = new HashSet<uint>();
        var rng = new Rng(3);
        for (int shot = 0; shot < 40; shot++)
        {
            var bullet = Shot(Projectiles.Flag.Bullet, damage: 65, type: (uint)DamageType.Aggravated);
            int margin = int.MinValue;
            Projectiles.Hurt(bullet, Body(ThingType.Actor, kind), rng, landed: s => margin = s.Margin);
            Assert.NotEqual(int.MinValue, margin);
            var expected = !rewritten ? DamageType.Aggravated
                         : margin < Projectiles.BulletLethal ? DamageType.Normal : DamageType.Lethal;
            Assert.Equal((uint)expected, bullet.ProjectileDamageType);
            seen.Add(bullet.ProjectileDamageType);
        }
        Assert.Equal(rewritten ? 2 : 1, seen.Count);
    }

    [Fact]
    public void A_projectile_without_the_bullet_flag_keeps_its_templates_damage_type()
    {
        var shot = Shot(damage: 200, type: (uint)DamageType.Aggravated);
        Projectiles.Hurt(shot, Body(ThingType.Actor, ActorType.Vampire), new Rng(3));
        Assert.Equal((uint)DamageType.Aggravated, shot.ProjectileDamageType);
    }

    /// <summary>Only seventeen templates burst when their timer runs out, and no arrow does.</summary>
    [Fact]
    public void Only_a_projectile_with_the_timer_flag_bursts_when_it_expires()
    {
        Assert.True(Projectiles.BurstsOnTimer(Shot(Projectiles.Flag.ExplodeOnTimer)));
        Assert.False(Projectiles.BurstsOnTimer(Shot(Projectiles.Flag.ExplodeOnWall)));
        Assert.False(Projectiles.BurstsOnTimer(Shot()));
        // And nothing else in the world is asked the question at all.
        Assert.False(Projectiles.BurstsOnTimer(Body(ThingType.Actor)));
    }

    // ---- and the launch itself, once there is a level ---------------------------------------------

    /// <summary>
    /// End to end: a projectile leaves a character aimed at another one, carrying the speed its
    /// template gives it along the line between them, and knowing who fired it.
    /// </summary>
    [Fact]
    public void An_arrow_leaves_its_firer_at_its_templates_speed_along_the_line_of_sight()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var firer = place.Player!.Thing;
        var target = place.Spawn("petRat", firer);
        Assert.SkipWhen(target is null, "petRat is not in this install");
        // Somewhere in front of him, so the line is not degenerate.
        place.Move(target!, firer.Position + new Vector3(0f, 400f, 0f));

        var shot = Projectiles.FireAt(place, firer, target!, "arrow", Vector3.Zero);
        Assert.SkipWhen(shot is null, "arrow is not in this install");

        Assert.Equal(ThingType.Projectile, shot!.Type);
        Assert.Equal(firer.Guid, shot.SpawnedBy);
        // arrow ships PHYSICS_VELOCITY 0,1000,0 — a thousand units a second, forward.
        Assert.Equal(1000f, shot.Velocity.Length(), 1);
        var line = Vector3.Normalize(Projectiles.Aim(target!) - Projectiles.Muzzle(firer, Vector3.Zero));
        Assert.True((Vector3.Normalize(shot.Velocity) - line).Length() < 1e-3f,
                    $"fired along {Vector3.Normalize(shot.Velocity)}, aimed along {line}");
        // TIMER 5000, and PROJECT_MAXDIST 1000 is the other end condition (not built).
        Assert.Equal(place.Milliseconds + 5_000, shot.ExpireAt);
    }

    /// <summary>
    /// A firearm end to end: it never flies. `proj_bullet` carries `PROJ_INSTANT_FIRE`, so the
    /// launch resolves it in the same frame and **the projectile is gone by the time `Fire`
    /// returns** — whatever it hit or did not. What it leaves is its burst where it struck a wall
    /// (`exp_smokepuff`: the hitscan hands the wall to `OnSurfaceContact`, and every
    /// bullet bursts on a wall) and its mark.
    /// </summary>
    [Fact]
    public void A_bullet_is_resolved_and_destroyed_in_the_frame_it_is_fired()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var firer = place.Player!.Thing;
        Assert.SkipUnless(place.Templates!.Has("proj_bullet"), "proj_bullet is not in this install");
        int before = place.Things.Count;

        var shot = Projectiles.FireAtPoint(place, firer, "proj_bullet", Vector3.Zero,
                                           firer.Position + new Vector3(0f, 500f, 40f), new Rng(9));

        Assert.NotNull(shot);
        Assert.True(shot!.Removed, "an instant-fire projectile must not survive its own launch");
        Assert.DoesNotContain(shot, place.Things);
        int puffs = place.Things.Count(t => t.Template.Equals("exp_smokepuff", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(puffs, 0, 1);
        Assert.Equal(before + puffs, place.Things.Count);
    }

    /// <summary>
    /// And an arrow does the opposite: no `PROJ_INSTANT_FIRE`, so it is still in the world after the
    /// launch, flying on its template velocity for the physics step to carry.
    /// </summary>
    [Fact]
    public void An_arrow_is_still_in_the_world_after_it_is_fired()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var firer = place.Player!.Thing;
        Assert.SkipUnless(place.Templates!.Has("arrow"), "arrow is not in this install");

        var shot = Projectiles.FireAtPoint(place, firer, "arrow", Vector3.Zero,
                                            firer.Position + new Vector3(0f, 500f, 40f), new Rng(9));

        Assert.NotNull(shot);
        Assert.False(shot!.Removed);
        Assert.Contains(shot, place.Things);
        Assert.Equal(0u, shot.ProjectileFlags & Projectiles.Flag.InstantFire);
        Assert.Equal(40, shot.ProjectileDamage);      // four d10
        Assert.Equal(1000f, shot.ProjectileRange);
    }

    /// <summary>
    /// Asking for something that is not a projectile leaves **nothing behind**: the engine builds the
    /// thing, sees the type, and calls its destructor. A reimplementation that checked the template
    /// first would answer the same but would not have made and unmade a thing, which is observable
    /// through the GUID counter.
    /// </summary>
    [Fact]
    public void Firing_a_template_that_is_not_a_projectile_leaves_nothing_behind()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var firer = place.Player!.Thing;
        Assert.SkipUnless(place.Templates!.Has("BroadSword"), "BroadSword is not in this install");
        int before = place.Things.Count;

        Assert.Null(Projectiles.FireAtPoint(place, firer, "BroadSword", Vector3.Zero,
                                            firer.Position + new Vector3(0f, 100f, 0f)));

        Assert.Equal(before, place.Things.Count);
    }
}
