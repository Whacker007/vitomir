// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Shots, containers, the party's portal and where a thing is put down, one rule a test: on a network player's machine a shot
/// or a hitscan that meets something is only destroyed; a bullet breaks a barrel, and a container broken by a blow is killed
/// by nobody; the party's one portal is single player's; a projectile with no damage of its own is worth 10; a bounce comes to
/// rest by the sign of its speed; and a thing put down near a spot may go over a corpse, the nearest spot taken.
/// </summary>
public class ShotsBarrelsAndPlacingTests
{
    /// <summary>
    /// **On a network player's machine a shot that meets a character is only destroyed**
    /// Mode 1 jumps to the destroy and answers 1). No accuracy roll, no dice, no harm and
    /// no noise there — the host's shot is the one that hurts. Vitomir rolled on the player's
    /// machine (and then had the harm refused).
    /// </summary>
    [Fact]
    public void A_shot_on_a_players_machine_is_only_destroyed()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var knight = place.Things.First(t => t.Template.StartsWith("convknight", StringComparison.Ordinal));
        var shot = new WorldThing
        {
            Index = -1, Guid = 7, Scene = new NsdThing(), Template = "arrow",
            Type = ThingType.Projectile, MoveType = WorldThing.MovePhysics, ProjectileDamage = 40,
        };
        shot.Place = place;
        NetOrders.Attach(place, NetMode.Client);
        try
        {
            var rng = new Rng(5);
            float health = knight.Actor!.Health;
            Assert.True(Projectiles.Hurt(shot, knight, rng));
            Assert.Equal(new Rng(5).Unit(), rng.Unit());                  // nothing drawn
            Assert.Equal(health, knight.Actor.Health);
        }
        finally { NetOrders.Detach(place); }
    }

    /// <summary>
    /// **A bullet breaks a barrel**: the hitscan hands a container the shot's
    /// raw damage (65 for `proj_bullet`), straight onto its health, and it opens at nothing with no
    /// opener. Vitomir stopped the bullet at the barrel and did it no harm until then.
    /// </summary>
    [Fact]
    public void A_bullet_breaks_the_barrel_it_meets()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");
        var barrel = place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        Assert.Equal(1f, barrel.ContainerHealth);

        var from = barrel.Position + new Vector3(-60f, 0f, 30f);
        Projectiles.Fire(place, place.Player!.Thing, "proj_bullet", from, Projectiles.Facing(Vector3.UnitX), 0);

        Assert.True(barrel.ContainerHealth <= 0f, $"the barrel has {barrel.ContainerHealth} left");
        Assert.NotEqual(0u, barrel.State & Container.Opened);
    }

    /// <summary>The hitscan on a network player's machine: the shot is destroyed before it looks.</summary>
    [Fact]
    public void A_hitscan_on_a_players_machine_is_destroyed_before_it_looks()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");
        var barrel = place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        NetOrders.Attach(place, NetMode.Client);
        try
        {
            var shot = Projectiles.Fire(place, place.Player!.Thing, "proj_bullet", barrel.Position + new Vector3(-60f, 0f, 30f),
                                        Projectiles.Facing(Vector3.UnitX), 0);
            Assert.True(shot is null || shot.Removed);
            Assert.Equal(1f, barrel.ContainerHealth);
            Assert.Empty(place.Decals.Live);
        }
        finally { NetOrders.Detach(place); }
    }

    /// <summary>
    /// **A container broken by damage is opened by nobody**: its
    /// script hears `killed(container, 0)`, whoever swung or shot. Vitomir passed the one who broke
    /// it until then.
    /// </summary>
    [Fact]
    public void A_barrel_broken_by_a_blow_is_killed_by_nobody()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");
        var barrel = place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        var told = new List<(WorldThing, WorldThing?)>();
        place.Killed = (thing, by) => { told.Add((thing, by)); return true; };

        Container.Damage(barrel, 5f, place.Player!.Thing, place.Motions);

        var (killed, killer) = Assert.Single(told);
        Assert.Same(barrel, killed);
        Assert.Null(killer);
    }

    /// <summary>A network game's host as the teleporters see it: every place the chronicle's current one, every player first.</summary>
    sealed class Hosted(Chronicle chronicle) : ITeleportNetwork
    {
        public int IndexOf(Location place) => chronicle.Current;
        public int Order(WorldThing creator) => 1;
        public string? Key(WorldThing who) => null;
        public bool Relay(WorldThing who, int location, int slot, int way) => false;
        public WorldThing? Who(string key) => null;
    }

    /// <summary>
    /// **The party's one portal is single player's** (the network game's word must be
    /// zero before a party member's cast closes the pair any party member made). In a network game
    /// each caster closes only their own — so a companion's cast there leaves the leader's open.
    /// Vitomir shut the party's in either.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_party_members_portal_closes_the_partys_only_in_single_player(bool network)
    {
        using var files = Install.Open();
        var where = ChronicleSet.Load(files).ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = where.Location;
        Location Build(string level, string? scene, int entrance) =>
            Location.Load(files, level, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var here = Build("P1_MON1", where.Entry!.SceneName, -1);
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = here.Place("Wilhem", christof.Position + new Vector3(60, 0, 0), Vector3.Zero, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        var session = new Session { Here = here, Chronicle = chronicle };
        here.Session = session;
        session.Teleports.Campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        if (network) session.Teleports.Network = new Hosted(chronicle);

        Assert.True(session.Teleports.Create(christof, "havenPortal", "havenPortal", 0, 0));
        Assert.True(session.Teleports.Create(wilhem, "havenPortal", "havenPortal", 0, 0));

        int open = session.Teleports.Slots.Count(slot => !slot.Free);
        Assert.Equal(network ? 2 : 1, open);
    }

    /// <summary>
    /// **A projectile the template gives no damage is worth 10** (the projectile's damage is set to 10
    /// before the tags): `woodGib`, the one shipped projectile whose chain names no PROJECT_DAMAGE.
    /// </summary>
    [Fact]
    public void A_projectile_with_no_damage_tag_is_worth_ten()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var anchor = place.Things.First(t => t.InWorld);
        var chip = place.Place("woodGib", anchor.Position + new Vector3(0, 0, 100), Vector3.Zero, anchor.Sector);
        Assert.NotNull(chip);
        Assert.Equal(ThingType.Projectile, chip!.Type);
        Assert.Equal(10, chip.ProjectileDamage);
    }

    /// <summary>
    /// **The bounce's rest test is signed** (`abs(v.x < 1)`, the comparison
    /// inside the abs). A thing leaving a floor contact towards −x and −y at any speed, and not
    /// rising faster than 10, is put to sleep where it touched; the same speed towards +x is not.
    /// Vitomir compared the magnitudes until then.
    /// </summary>
    [Theory]
    [InlineData(-200f, -100f, true)]
    [InlineData(200f, 100f, false)]
    [InlineData(-200f, 100f, false)]
    public void A_bounce_sleeps_by_the_sign_of_the_speed_not_its_size(float x, float y, bool sleeps)
    {
        var chip = new WorldThing
        {
            Index = -1, Guid = 9, Scene = new NsdThing(), Template = "t", Type = ThingType.Projectile,
            MoveType = WorldThing.MovePhysics, Velocity = new Vector3(x, y, -0.5f),
        };
        Assert.True(Physics.Contact(chip, Vector3.UnitZ));
        Assert.Equal(sleeps, (chip.PhysicsFlags & Physics.Asleep) != 0);
    }

    /// <summary>
    /// **A thing can be put down on a corpse** (the room test's query has bit 4 set,
    /// so the query passes over a dead character). `SpawnThingNear` takes the anchor's
    /// forward spot though a body lies there. Vitomir refused the spot and tried the next direction
    /// until then.
    /// </summary>
    [Fact]
    public void A_thing_spawned_near_is_put_down_over_a_corpse()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var knights = place.Things.Where(t => t.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        var (anchor, body) = (knights[0], knights[1]);
        float yaw = anchor.Orientation.Z * (MathF.PI / 180f);
        var forward = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);

        var first = place.SpawnNear("petRat", anchor, 100f, 3)!;
        var spot = first.Position;
        Assert.True(Vector3.Distance(anchor.Position + forward * 100f, spot with { Z = anchor.Position.Z }) < 1f,
                    "the forward spot has no floor here; the test needs another anchor");
        place.Remove(first, drop: false);

        body.Position = spot;
        body.Actor!.Flags |= Actor.Flag.Dead;
        var second = place.SpawnNear("petRat", anchor, 100f, 3)!;
        Assert.Equal(spot, second.Position);

        body.Actor.Flags &= ~Actor.Flag.Dead;                    // alive, the body is in the way
        var third = place.SpawnNear("petRat", anchor, 100f, 3)!;
        Assert.NotEqual(spot, third.Position);
    }

    /// <summary>
    /// **The room test looks at the nearest hit only** (the query's first is taken,
    /// nearest, hit) and takes the spot when it is the anchor: a spot close enough that the anchor
    /// touches the new thing is taken though something else touches it too, further off; something
    /// nearer than the anchor refuses it. Vitomir refused a spot anything but the anchor touched
    /// once. The two knights are made spheres here, because a cylinder-against-cylinder hit records no
    /// real distance (<see cref="Bodies.Hit"/>) and the first met would win.
    /// </summary>
    [Fact]
    public void A_spot_the_anchor_is_nearest_to_is_taken_though_something_else_touches_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var knights = place.Things.Where(t => t.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        var (anchor, other) = (knights[0], knights[1]);
        anchor.Collide = other.Collide = CollideShape.Sphere;
        float yaw = anchor.Orientation.Z * (MathF.PI / 180f);
        var forward = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);
        const float Out = 12f;

        var probe = place.SpawnNear("petRat", anchor, Out, 3)!;
        var spot = probe.Position;
        float reach = probe.Radius + other.Radius;
        Assert.True(Vector3.Distance(anchor.Position + forward * Out, spot with { Z = anchor.Position.Z }) < 1f,
                    "the forward spot was not taken; the test needs another anchor");
        Assert.True(probe.Collide is CollideShape.Sphere or CollideShape.Cylinder);
        Assert.True(probe.Radius + anchor.Radius > Out + 1f && reach - 2f > Out + 1f, $"radii {probe.Radius}, {anchor.Radius}, {other.Radius}");
        place.Remove(probe, drop: false);

        other.Position = spot + forward * (reach - 2f);        // touching the new thing, further off than the anchor
        var taken = place.SpawnNear("petRat", anchor, Out, 3)!;
        Assert.Equal(spot, taken.Position);
        place.Remove(taken, drop: false);

        other.Position = spot + forward * 2f;                  // nearer than the anchor
        Assert.NotEqual(spot, place.SpawnNear("petRat", anchor, Out, 3)!.Position);
    }
}

