// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A bow's arrow flies**: a ranged weapon's tag looses the attack's
/// projectile instead of rolling the sword's roll, and whatever
/// the arrow meets on its way is what it hits. Measured against
/// a run of 1.1: 1,000 units a second, straight, gone where it struck.
/// </summary>
public class ShotTests
{
    static WorldThing Body(ThingType type, ActorType kind = ActorType.Human, float dexterity = 0f)
    {
        var body = new WorldThing
        {
            Index = -1, Guid = 99, Scene = new NsdThing(), Template = "t", Type = type,
            Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f,
            Actor = type is ThingType.Actor or ThingType.Player
                ? new Actor { Type = kind, Health = 100f, MaxHealth = 100f } : null,
        };
        if (body.Actor is { } a) a.Stat[(int)ActorStat.Dexterity] = a.BaseStat[(int)ActorStat.Dexterity] = dexterity;
        return body;
    }

    static WorldThing Arrow(uint flags = 0, int damage = 40) => new()
    {
        Index = -1, Guid = 7, Scene = new NsdThing(), Template = "arrow",
        Type = ThingType.Projectile, MoveType = WorldThing.MovePhysics,
        ProjectileFlags = flags, ProjectileDamage = damage, ProjectileDamageType = (uint)DamageType.Lethal,
    };

    // ---- the aim ---------------------------------------------------------------------------------

    /// <summary>
    /// **The scatter**: one draw decides whether the shot is off the line at all — a hand
    /// whose Dexterity and <c>WEAPON_ACCURACY</c> reach 100 never is, and takes no more draws — and
    /// otherwise the yaw is drawn before the pitch, each up to 15 degrees times how bad the first
    /// draw was, turned in the aim's own frame.
    /// </summary>
    [Fact]
    public void A_steady_hand_does_not_scatter_and_a_shaky_one_turns_by_what_it_drew()
    {
        var forward = Vector3.Normalize(new Vector3(1f, 2f, 0.3f));
        var steady = new Rng(5);
        Assert.Equal(forward, Projectiles.Scatter(forward, 40f, 60f, steady));
        var check = new Rng(5);
        check.Unit();
        Assert.Equal(check.Unit(), steady.Unit());                  // exactly one draw was taken

        int scattered = 0;
        for (int k = 1; k <= 50; k++)
        {
            int seed = k * 7919;
            var mirror = new Rng(seed);
            float spread = (100f - (mirror.Unit() * 100f + 5f + 20f)) * 0.01f;
            float yaw = 0f, pitch = 0f;
            if (spread > 0f) { yaw = (mirror.Unit() * 30f - 15f) * spread; pitch = (mirror.Unit() * 30f - 15f) * spread; scattered++; }
            var off = Projectiles.Scatter(Vector3.UnitY, 5f, 20f, new Rng(seed));
            // Along +Y the aim's own frame is the world's: the yaw is the turn on the level, the
            // pitch the climb.
            Assert.Equal(yaw, MathF.Atan2(-off.X, off.Y) * 180f / MathF.PI, 3);
            Assert.Equal(pitch, MathF.Asin(off.Z) * 180f / MathF.PI, 3);
            Assert.True(MathF.Abs(yaw) <= 15f && MathF.Abs(pitch) <= 15f);
        }
        Assert.InRange(scattered, 25, 49);          // three in four draws, for a hand at 25
    }

    /// <summary>
    /// **The accuracy roll**: the shooter's Dexterity against the victim's, each times a
    /// draw, with 50 standing in for a side that is not a character — a shot with no owner, or a
    /// crate. The attacker's side is drawn first.
    /// </summary>
    [Fact]
    public void The_accuracy_roll_is_dexterity_against_dexterity_with_fifty_for_anything_else()
    {
        for (int k = 1; k <= 20; k++)
        {
            int seed = k * 7919;
            var mirror = new Rng(seed);
            int expected = (int)(mirror.Unit() * 50f - mirror.Unit() * 40f);
            Assert.Equal(expected, Projectiles.Accuracy(Arrow(), Body(ThingType.Actor, dexterity: 40f), new Rng(seed)));

            mirror = new Rng(seed);
            expected = (int)(mirror.Unit() * 50f - mirror.Unit() * 50f);
            Assert.Equal(expected, Projectiles.Accuracy(Arrow(), Body(ThingType.Container), new Rng(seed)));
        }
    }

    /// <summary>
    /// **An arrow that reaches somebody counts, soaked or not** (the hit answers 1 on every path but
    /// the friendly-fire refusal). Earlier, Vitomir let a shot that did no harm carry on through its
    /// target to the next one.
    /// </summary>
    [Fact]
    public void An_arrow_that_reaches_somebody_counts_even_when_it_is_soaked_away()
    {
        int soakedAway = 0;
        for (int k = 1; k <= 10; k++)
        {
            int seed = k * 7919;
            var knight = Body(ThingType.Actor);
            for (int i = 0; i < Actor.Soaks; i++) knight.Actor!.Soak[i] = knight.Actor.BaseSoak[i] = 100_000f;
            Harm harm = default;
            Assert.True(Projectiles.OnThingContact(Arrow(), knight, new Rng(seed), landed: s => harm = s.Harm));
            if (harm.Taken == 0f) soakedAway++;
        }
        Assert.True(soakedAway >= 8, $"only {soakedAway} of ten were soaked away");
    }

    // ---- the ammunition ----------------------------------------------------------------------------

    static Weapon Bow(int ammo)
    {
        var w = new Weapon { Name = "shortBow" };
        var a = w.Attacks[0];
        a.Flags = WeaponFlag.Ranged; a.AmmoType = 1; a.MaxAmmo = 20; a.Ammo = ammo; a.AmmoUse = 1;
        a.Projectile = "arrow";
        return w;
    }

    /// <summary>
    /// The tag handler's gate: an empty bow reloads from the pack if it can, and if it
    /// cannot there is no shot at all — only its <c>outofammo</c>.
    /// </summary>
    [Fact]
    public void An_empty_bow_reloads_from_the_pack_or_looses_nothing()
    {
        var wilhem = Body(ThingType.Player);
        Assert.False(Projectiles.Loaded(wilhem, Bow(0)));
        Assert.True(Projectiles.Loaded(wilhem, Bow(3)));

        var bow = Bow(0);
        wilhem.Actor!.Inventory.Add(new Item { Name = "arrows", Type = ItemType.Ammo, ItemId = 1, Amount = 12 });
        Assert.True(Projectiles.Loaded(wilhem, bow));
        Assert.Equal(12, bow.Current.Ammo);
        Assert.Empty(wilhem.Actor.Inventory.Items);
    }

    // ---- in the monastery, with the skeleton archer ---------------------------------------------------

    static (Location Place, Minds Minds, WorldThing Archer, WorldThing Christof, Vitomir.Engine.Vfs.GameFiles Files) Range()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files));
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        var christof = place.Player!.Thing;
        foreach (var node in place.Walk!.Nodes)
        {
            if (node.Outline.Length == 0) continue;
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var at = new Vector3(c.X, c.Y, node.Height(c.X, c.Y));
            float d = Vector3.Distance(at, archer.Position);
            if (d < 300f || d > 450f || MathF.Abs(at.Z - archer.Position.Z) > 40f) continue;
            christof.Position = at;
            christof.Sector = node.Sector;
            if (Sight.Sees(place.Level, archer, christof)) return (place, minds, archer, christof, files);
        }
        throw new InvalidOperationException("no spot in the archer's sight at that range");
    }

    /// <summary>
    /// **The archer's arrows, end to end.** Each tag looses the bow's <c>arrow</c> from the archer's
    /// hand at a thousand units a second, within the scatter of the line to Christof's middle; an
    /// arrow that reaches him is a blow credited to the archer, landing about as long after the
    /// loose as the distance at a thousand a second — a run of 1.1 gave 0.1 to 0.43 s — and
    /// every arrow is gone again, stuck in a wall, in Christof, or out of time.
    /// </summary>
    [Fact]
    public void The_archers_arrows_fly_from_his_hand_and_land_where_they_meet_christof()
    {
        var (place, minds, archer, christof, files) = Range();
        using var _f = files;
        var sim = new Simulation(place);
        minds.Aggravate(minds.All.First(m => ReferenceEquals(m.Thing, archer)), christof);
        christof.Actor!.Health = christof.Actor.MaxHealth = 100_000f;       // he has to outlast it
        for (int frame = 0; frame < 30 * 25; frame++) sim.Step(1f / 30f);

        var shots = sim.Shots.Where(s => ReferenceEquals(s.Attacker, archer)).ToList();
        Assert.True(shots.Count >= 5, $"only {shots.Count} arrows in 25 seconds");
        foreach (var s in shots)
        {
            Assert.Equal("arrow", s.Projectile.Template, ignoreCase: true);
            Assert.Equal(1000f, s.Velocity.Length(), 1);
            Assert.True(Bodies.Distance2D(s.From, archer.Position) < 60f, $"loosed from {s.From}, the archer is at {archer.Position}");
            Assert.InRange(s.From.Z - archer.Position.Z, 10f, archer.Height + 10f);
            var line = Vector3.Normalize(Projectiles.Aim(christof) - s.From);
            float off = MathF.Acos(Math.Clamp(Vector3.Dot(line, Vector3.Normalize(s.Velocity)), -1f, 1f)) * 180f / MathF.PI;
            Assert.True(off < 22f, $"an arrow went {off:0.0} degrees off the line");
        }

        var hits = sim.Blows.Where(b => ReferenceEquals(b.Attacker, archer)).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits.Count <= shots.Count);
        foreach (var hit in hits)
        {
            Assert.Same(christof, hit.Target);
            var loosed = shots.Last(s => s.AtMs <= hit.AtMs);
            float flight = (hit.AtMs - loosed.AtMs) / 1000f;
            float expected = Vector3.Distance(loosed.From, Projectiles.Aim(christof)) / 1000f;
            Assert.InRange(flight, expected - 0.12f, expected + 0.12f);
        }
        Assert.DoesNotContain(place.Things, t => t.Type == ThingType.Projectile
                                                 && shots.Any(s => ReferenceEquals(s.Projectile, t) && sim.Milliseconds - s.AtMs > 5_100));
    }

    /// <summary>
    /// **An arrow flies through a corpse** and strikes whoever stands behind it: the
    /// body lying in the way is met, refused, and flown on from.
    /// </summary>
    [Fact]
    public void An_arrow_flies_through_a_corpse_and_strikes_the_one_behind()
    {
        var (place, _, archer, christof, files) = Range();
        using var _f = files;
        var corpse = place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, archer) && !ReferenceEquals(t, christof)
                                             && t.Type == ThingType.Actor);
        corpse.Actor!.Flags |= Actor.Flag.Dead;
        var from = Projectiles.Muzzle(archer, Vector3.Zero);
        var to = Projectiles.Aim(christof);
        corpse.Position = (from + to) * 0.5f - new Vector3(0f, 0f, corpse.Height * 0.5f);
        Assert.NotNull(Pick.Meets(corpse.Position, corpse.Radius, corpse.Height, from, Vector3.Normalize(to - from)));

        var shot = Projectiles.FireAt(place, archer, christof, "arrow", Vector3.Zero, new Rng(4));
        Assert.NotNull(shot);
        WorldThing? struck = null;
        var shots = new Physics.Shots(new Rng(4), place.Milliseconds, place.Motions, s => struck = s.Victim);
        for (int frame = 0; frame < 30 && !shot!.Removed; frame++) Physics.StepAll(place, 1f / 30f, shots);
        Assert.True(shot!.Removed);
        Assert.Same(christof, struck);
    }

    /// <summary>
    /// **A party member's bow spends an arrow a shot**: Christof with a short
    /// bow looses one at the archer, from his own hand, owned by him, and the bow holds one fewer.
    /// </summary>
    [Fact]
    public void A_party_members_shot_is_his_and_costs_an_arrow()
    {
        var (place, _, archer, christof, files) = Range();
        using var _f = files;
        var bow = Weapon.From("shortBow", place.Templates!.Resolve("shortBow"))!;
        christof.Actor!.Weapon = bow;
        christof.Actor.Target = archer;
        Assert.Equal(20, bow.Current.Ammo);
        var shot = Projectiles.FireWeapon(place, christof, new Rng(2));
        Assert.NotNull(shot);
        Assert.Equal(christof.Guid, shot!.SpawnedBy);
        Assert.Equal(19, bow.Current.Ammo);
        // He turned to face what he shot at.
        var toward = archer.Position - christof.Position;
        Assert.Equal(Player.Wrap(MathF.Atan2(-toward.X, toward.Y) * 180f / MathF.PI), christof.Orientation.Z, 2);
    }

    /// <summary>
    /// **No flight limit**: a projectile's `PROJECT_MAXDIST` is
    /// read in exactly one place, the hitscan's ray; nothing that flies a projectile
    /// looks at it. An arrow given a range of 50 still reaches Christof 300-odd units away — only
    /// its `TIMER` ends a miss.
    /// </summary>
    [Fact]
    public void A_flying_arrow_is_not_stopped_by_its_maximum_distance()
    {
        var (place, _, archer, christof, files) = Range();
        using var _f = files;
        var shot = Projectiles.FireAt(place, archer, christof, "arrow", Vector3.Zero, new Rng(4));
        Assert.NotNull(shot);
        shot!.ProjectileRange = 50f;
        WorldThing? struck = null;
        var shots = new Physics.Shots(new Rng(4), place.Milliseconds, place.Motions, s => struck = s.Victim);
        for (int frame = 0; frame < 30 && !shot.Removed; frame++) Physics.StepAll(place, 1f / 30f, shots);
        Assert.Same(christof, struck);
    }

    /// <summary>
    /// **Anything else flies through people**: the base class's thing contact is
    /// a default that answers 0, and ten of the eleven thing classes keep it — only the projectile's
    /// overrides it — so a sword thrown clear of a body at chest height carries on past it.
    /// </summary>
    [Fact]
    public void A_physics_thing_that_is_not_a_projectile_flies_through_a_body()
    {
        var (place, _, archer, christof, files) = Range();
        using var _f = files;
        var from = Projectiles.Muzzle(archer, Vector3.Zero);
        var to = Projectiles.Aim(christof);
        var dir = Vector3.Normalize(to - from);
        // Christof stands in the middle of the line, clear of the wall at its far end.
        christof.Position = (from + to) * 0.5f - new Vector3(0f, 0f, christof.Height * 0.5f);
        Assert.NotNull(Pick.Meets(christof.Position, christof.Radius, christof.Height, from, dir));
        var sword = place.Place("BroadSword", from + dir * 60f, Vector3.Zero, archer.Sector);
        Assert.NotNull(sword);
        sword!.MoveType = WorldThing.MovePhysics;
        sword.PhysicsFlags = 0;                           // awake, and no gravity to drop it short
        sword.Velocity = dir * 1500f;
        float past = float.MinValue;
        for (int frame = 0; frame < 10 && !sword.Removed; frame++)
        {
            Physics.StepAll(place, 1f / 60f);
            past = MathF.Max(past, Vector3.Dot(sword.Position - christof.Position, dir));
        }
        Assert.True(past > christof.Radius + 1f, $"the sword got no further than {past:0} past Christof");
    }
}
