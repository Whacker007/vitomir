// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The gib spray — `rpgSFX::SprayGib` and the three callers that lean on it. The
/// rest is the gore; everything asserted here came out of
/// the original game or out of the shipped templates.
///
/// Most of it needs no install, because the gates, the roll and the launch are arithmetic. What
/// needs one is whether a chunk really arrives in the world with the numbers its template gives it.
/// </summary>
public class GoreTests
{
    static WorldThing Character(Actor actor) => new()
    {
        Index = 0, Guid = 1, Scene = new NsdThing(), Template = "test",
        Type = ThingType.Actor, Actor = actor,
    };

    static Actor Body(int gibClass = 2, uint flags = 0)
    {
        var a = new Actor { Type = ActorType.Human, Flags = flags, GibClass = gibClass };
        a.Health = a.MaxHealth = 100;
        return a;
    }

    /// <summary>The seed whose very first draw is `RAND_MAX`, so `Unit()` is exactly 1.</summary>
    const int Certain = 30091;
    /// <summary>And the seed whose first draw is 0.</summary>
    const int Impossible = 20057;

    [Fact]
    public void The_two_seeds_the_rest_of_this_file_relies_on_are_what_they_claim()
    {
        Assert.Equal(1f, new Rng(Certain).Unit());
        Assert.Equal(0f, new Rng(Impossible).Unit());
    }

    // ---- the random direction ---------------------------------------------------------

    /// <summary>
    /// Rejection sampling: a point out of the cube, thrown away unless it fell inside the unit
    /// sphere, then normalised. So every answer is a unit vector — and the rejection is what makes
    /// the directions even rather than crowded towards the cube's corners.
    /// </summary>
    [Fact]
    public void A_random_direction_is_always_a_unit_vector()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 20_000; i++)
            Assert.Equal(1f, Physics.Direction(rng).Length(), 4);
    }

    /// <summary>
    /// And it reaches everywhere: all eight octants, in roughly equal numbers. A sampler that had
    /// dropped the rejection would still pass the length test above and fail this one's spread.
    /// </summary>
    [Fact]
    public void A_random_direction_fills_every_octant_evenly()
    {
        var rng = new Rng(11);
        var seen = new int[8];
        const int draws = 40_000;
        for (int i = 0; i < draws; i++)
        {
            var v = Physics.Direction(rng);
            seen[(v.X < 0 ? 1 : 0) | (v.Y < 0 ? 2 : 0) | (v.Z < 0 ? 4 : 0)]++;
        }
        foreach (int count in seen)
            Assert.InRange(count, draws / 8 * 9 / 10, draws / 8 * 11 / 10);
    }

    // ---- the bone pick ----------------------------------------------------------------

    /// <summary>
    /// `(int)(rand01 × count)`, and the engine clamps the top at **count**, not count − 1. `rand01`
    /// reaches 1.0 exactly, so one draw in 32,768 asks for a bone one past the end of the array and
    /// the engine reads whatever is there. Vitomir takes the last bone instead; this is the test
    /// that records the difference rather than hiding it.
    /// </summary>
    [Fact]
    public void The_top_of_the_random_bone_range_is_the_engines_own_off_by_one()
    {
        Assert.Equal(19, Gore.RandomBone(20, new Rng(Certain)));   // the engine would ask for 20
        Assert.Equal(0, Gore.RandomBone(20, new Rng(Impossible)));
    }

    [Fact]
    public void A_random_bone_is_always_one_the_skeleton_has()
    {
        var rng = new Rng(3);
        for (int i = 0; i < 10_000; i++) Assert.InRange(Gore.RandomBone(44, rng), 0, 43);
    }

    // ---- turning a bone's matrix back into three angles (ours) -----------------------------------

    [Fact]
    public void An_orientation_survives_the_trip_through_a_matrix_and_back()
    {
        var rng = new Rng(5);
        for (int i = 0; i < 500; i++)
        {
            var want = new Vector3(rng.Unit() * 160f - 80f, rng.Unit() * 360f - 180f, rng.Unit() * 360f - 180f);
            var thing = new WorldThing
            {
                Index = 0, Guid = 1, Scene = new NsdThing(), Template = "t", Orientation = want,
            };
            var again = WorldThing.OrientationOf(thing.Rotation);
            var rebuilt = new WorldThing
            {
                Index = 0, Guid = 1, Scene = new NsdThing(), Template = "t", Orientation = again,
            };
            // The angles may differ; the rotation they produce may not.
            Same(thing.Rotation, rebuilt.Rotation, $"{want} became {again}");
        }
    }

    /// <summary>Two rotations agree, compared by how far apart they are rather than by rounding.</summary>
    static void Same(Matrix4x4 a, Matrix4x4 b, string because)
    {
        float worst = 0f;
        foreach (var (x, y) in new[]
                 {
                     (a.M11, b.M11), (a.M12, b.M12), (a.M13, b.M13),
                     (a.M21, b.M21), (a.M22, b.M22), (a.M23, b.M23),
                     (a.M31, b.M31), (a.M32, b.M32), (a.M33, b.M33),
                 })
            worst = MathF.Max(worst, MathF.Abs(x - y));
        Assert.True(worst < 1e-3f, $"{because}: off by {worst}");
    }

    /// <summary>A thing's MODELSCALE rides along in its world matrix and is not a rotation.</summary>
    [Fact]
    public void A_scaled_matrix_reads_as_the_same_orientation()
    {
        var plain = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateRotationX(0.3f);
        var scaled = Matrix4x4.CreateScale(4f) * plain;
        var a = WorldThing.OrientationOf(plain);
        var b = WorldThing.OrientationOf(scaled);
        Assert.True((a - b).Length() < 1e-3f, $"{a} vs {b}");
    }

    // ---- the gates on the two damage paths --------------------------------------------------------

    /// <summary>
    /// Only a melee blow or an explosion sheds anything. A shot and an effect never do, which is the
    /// same test decapitation makes and one notch looser — a behead needs melee alone.
    /// </summary>
    [Theory]
    [InlineData(HitKind.Melee, true)]
    [InlineData(HitKind.Explosion, true)]
    [InlineData(HitKind.Projectile, false)]
    [InlineData(HitKind.Effect, false)]
    public void Only_two_kinds_of_blow_shed_a_chunk(HitKind kind, bool sheds)
    {
        Assert.Equal(sheds, Damage.SpraysOnHit(Body(), kind, 50f, new Rng(Impossible)));
        Assert.Equal(sheds ? 2 : 0, Damage.SpraysOnDeath(Body(), kind, 50f));
    }

    [Fact]
    public void The_unnamed_flag_that_ninety_six_templates_carry_refuses_every_chunk()
    {
        var quiet = Body(flags: Actor.Flag.NoGibSpray);
        Assert.False(Damage.SpraysOnHit(quiet, HitKind.Melee, 1000f, new Rng(Impossible)));
        Assert.Equal(0, Damage.SpraysOnDeath(quiet, HitKind.Melee, 1000f));
    }

    [Fact]
    public void An_actor_with_no_gib_class_has_nothing_to_shed()
    {
        Assert.False(Damage.SpraysOnHit(Body(gibClass: 0), HitKind.Melee, 1000f, new Rng(Impossible)));
        Assert.Equal(0, Damage.SpraysOnDeath(Body(gibClass: 0), HitKind.Melee, 1000f));
    }

    /// <summary>
    /// The hit path is a **roll**, not a threshold: `rand01 × health &lt; 2 × damage`. The health is
    /// what is left after the blow, so the same blow sheds a chunk more and more readily as the
    /// victim weakens.
    /// </summary>
    [Fact]
    public void An_ordinary_hit_rolls_against_what_health_is_left()
    {
        var hale = Body();                       // 100 health
        Assert.False(Damage.SpraysOnHit(hale, HitKind.Melee, 10f, new Rng(Certain)));   // 100 >= 20
        Assert.True(Damage.SpraysOnHit(hale, HitKind.Melee, 10f, new Rng(Impossible))); //   0 <  20

        // Worn down to five, the same ten-point blow is certain rather than unlikely.
        var failing = Body();
        failing.Health = 5f;
        Assert.True(Damage.SpraysOnHit(failing, HitKind.Melee, 10f, new Rng(Certain)));  //   5 <  20
    }

    /// <summary>
    /// The death path is the pair of thresholds — and this is the distinction the first reading of
    /// the gib spray's callers got wrong, folding the roll and the thresholds into one rule.
    /// </summary>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(5f, 0)]
    [InlineData(5.01f, 1)]
    [InlineData(19f, 1)]
    [InlineData(19.01f, 2)]
    [InlineData(1000f, 2)]
    public void A_killing_blow_sheds_one_chunk_over_five_and_two_over_nineteen(float damage, int chunks) =>
        Assert.Equal(chunks, Damage.SpraysOnDeath(Body(), HitKind.Melee, damage));

    /// <summary>The setting refuses everything, and it is checked before anything else is.</summary>
    [Fact]
    public void The_no_gore_setting_refuses_a_chunk_outright()
    {
        var thing = Character(Body());
        try
        {
            Gore.NoBlood = true;
            Assert.Null(Gore.Spray(thing, "gib_2_0", Gore.HeadTag, new Rng(1)));
        }
        finally { Gore.NoBlood = false; }
    }

    /// <summary>
    /// The launch is a **jitter**, not the launch: 20 units a second, in a direction that is random
    /// except that its Z is taken as an absolute value. Gore is never kicked downwards.
    /// </summary>
    [Fact]
    public void A_launch_is_twenty_units_a_second_and_never_points_down()
    {
        var rng = new Rng(23);
        bool anyUp = false, anyLeft = false, anyRight = false;
        for (int i = 0; i < 10_000; i++)
        {
            var v = Gore.Launch(rng);
            Assert.Equal(Gore.LaunchSpeed, v.Length(), 2);
            Assert.True(v.Z >= 0f, $"thrown downwards: {v}");
            anyUp |= v.Z > 1f; anyLeft |= v.X < -1f; anyRight |= v.X > 1f;
        }
        Assert.True(anyUp && anyLeft && anyRight, "the jitter should still go every way but down");
    }

    // ---- what a chunk is, once the install is there ------------------------------------------------

    /// <summary>
    /// The shipped gib templates are the proof that the physics tags are read: a chunk is a
    /// **projectile** that **moves by physics**, with gravity of its own (300 for flesh, 500 for the
    /// golem's stone), an elasticity well under 1, a launch velocity that already points upward, a
    /// spin, and five seconds to live.
    /// </summary>
    /// <summary>One thing built straight from its template, with no level around it.</summary>
    static WorldThing FromTemplate(TemplateSet templates, string name, int now = 0) =>
        Location.Build(NsdThing.Placed(name, Vector3.Zero, Vector3.Zero, 0), templates,
                       index: -1, guid: 1, now: now);

    [Theory]
    [InlineData("gib_2_0", 300f, 0.3f, -80f, 0f, 250f)]
    [InlineData("gib_3_4", 300f, 0.3f, 25f, 100f, 200f)]
    [InlineData("gib_4_7", 500f, 0.7f, -150f, 0f, 400f)]
    public void A_gib_template_arrives_with_its_own_physics(string template, float gravity,
                                                            float elasticity, float vx, float vy, float vz)
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        Assert.SkipUnless(templates.Has(template), $"{template} is not in this install");
        var chunk = FromTemplate(templates, template, now: 1_000);

        Assert.Equal(ThingType.Projectile, chunk.Type);
        Assert.Equal(WorldThing.MovePhysics, chunk.MoveType);
        Assert.Equal(Physics.Gravity, chunk.PhysicsFlags & Physics.Gravity);
        Assert.Equal(gravity, chunk.Gravity);
        Assert.Equal(elasticity, chunk.Restitution, 4);
        // Still the template's own numbers, because this built the thing without entering it into a
        // world — and it is entering that rotates the velocity out of the thing's frame
        // (Location.LaunchVelocity). Read the tag here; see ProjectileTests for the rotation.
        Assert.Equal(new Vector3(vx, vy, vz), chunk.Velocity);
        Assert.NotEqual(Vector3.Zero, chunk.RotVelocity);
        // TIMER 5000, turned into an absolute stamp the moment the template was applied.
        Assert.Equal(6_000, chunk.ExpireAt);
    }

    /// <summary>
    /// A thing that is not `MOVETYPE 3` keeps the constructor's numbers whatever its template says,
    /// because the setter refuses every physics tag before it looks at which one it has. That rule
    /// is why a dropped sword stays where it is put: **no item, weapon or armour template in the
    /// game sets `MOVETYPE` or a single `PHYSICS_*` tag**, so nothing an actor carries can fall.
    /// </summary>
    [Theory]
    [InlineData("BroadSword")]     // a weapon
    [InlineData("silverBracelet")] // an item
    [InlineData("bronzeArmor")]    // and armour
    public void Nothing_an_actor_carries_is_a_physics_thing(string template)
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        Assert.SkipUnless(templates.Has(template), $"{template} is not in this install");
        var item = FromTemplate(templates, template);

        Assert.NotEqual(WorldThing.MovePhysics, item.MoveType);
        Assert.Equal(200f, item.Gravity);          // the constructor's, not a template's
        Assert.Equal(1f, item.Restitution);
        Assert.Equal(0u, item.PhysicsFlags);
        Assert.Equal(Vector3.Zero, item.Velocity);
    }

    /// <summary>
    /// The launch itself, in the order the engine does it: the chunk enters the world, which rotates
    /// its template velocity out of the bone's frame and into the world, and *then* the
    /// spray adds its random 20 on top — in world space, because the thing already exists.
    ///
    /// So what this can check is the remainder: subtract the rotated template velocity and what is
    /// left must be exactly the jitter, 20 units long and never pointing down.
    /// </summary>
    [Fact]
    public void A_sprayed_chunk_is_launched_on_top_of_the_velocity_its_template_gave_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var thrower = place.Player!.Thing;
        var rng = new Rng(17);
        for (int i = 0; i < 50; i++)
        {
            var chunk = Gore.Spray(thrower, "gib_2_0", Gore.NoTag, rng);
            Assert.SkipWhen(chunk is null, "gib_2_0 is not in this install");
            var launched = Vector3.TransformNormal(new Vector3(-80f, 0f, 250f), chunk!.Rotation);
            var added = chunk.Velocity - launched;
            Assert.Equal(Gore.LaunchSpeed, added.Length(), 2);
            Assert.True(added.Z >= 0f, $"a chunk was thrown downwards: {added}");
            Assert.Equal(thrower.Guid, chunk.SpawnedBy);
        }
    }
}
