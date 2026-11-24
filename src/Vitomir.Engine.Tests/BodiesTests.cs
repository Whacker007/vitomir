// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Thing collision: the barrels, carts and people a level's own geometry knows nothing about.
/// Every rule here is the original game's collision rule; these tests are what says the
/// reading was right, so the ones that can run over the whole game do.
/// </summary>
public class BodiesTests
{
    static string[] Levels(Vfs.GameFiles files) =>
        [.. files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                 .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];

    [Fact]
    public void Every_template_in_the_game_stays_inside_the_engines_own_limits()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var shapes = new Dictionary<uint, int>();
        int noShape = 0, negative = 0, outOfRange = 0, total = 0;
        foreach (var name in templates.Names)
        {
            total++;
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.Collide, out var c)) { noShape++; continue; }
            if (c.AsUInt() > 3) outOfRange++;                       // the engine would ignore it
            shapes[c.AsUInt()] = shapes.GetValueOrDefault(c.AsUInt()) + 1;
            if (tags.TryGetValue(Tags.Radius, out var r) && r.AsFloat() < 0) negative++;
            if (tags.TryGetValue(Tags.Height, out var h) && h.AsFloat() < 0) negative++;
        }
        Assert.Equal(2_617, total);
        // Nothing in the game sets a shape the engine would refuse. One template does set a
        // negative size — basket3's height is -18.8 — and the engine drops it, leaving a cylinder
        // of no height at all, which still catches anyone whose feet are level with it.
        Assert.Equal(0, outOfRange);
        Assert.Equal(1, negative);
        // 263 templates say nothing about collision at all, and a thing built from one has no body:
        // the constructor zeroes the shape, the radius and the height.
        Assert.Equal(263, noShape);
        Assert.Equal(90, shapes[(uint)CollideShape.None]);
        Assert.Equal(271, shapes[(uint)CollideShape.Sphere]);
        Assert.Equal(1_765, shapes[(uint)CollideShape.Cylinder]);
        Assert.Equal(228, shapes[(uint)CollideShape.Box]);
    }

    [Fact]
    public void Christof_is_a_cylinder_and_a_barrel_is_one_too()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        (CollideShape Shape, float Radius, float Height) Body(string name)
        {
            var t = templates.Resolve(name);
            return ((CollideShape)t[Tags.Collide].AsUInt(), t[Tags.Radius].AsFloat(),
                    t.TryGetValue(Tags.Height, out var h) ? h.AsFloat() : 0f);
        }
        Assert.Equal((CollideShape.Cylinder, 32f, 128f), Body("Christof"));
        Assert.Equal((CollideShape.Cylinder, 22f, 60.63f), Body("barrel2"));
        Assert.Equal((CollideShape.Cylinder, 70f, 90f), Body("cartempty"));
        // The same cart, loaded, is a sphere instead — the shape is per template, not per model.
        Assert.Equal((CollideShape.Sphere, 70f, 90f), Body("cartfull"));
        // The base character is twice Christof's width; anything without its own radius gets that.
        Assert.Equal(CollideShape.Cylinder, Body("_actor").Shape);
        Assert.Equal(64f, Body("_actor").Radius);
    }

    /// <summary>
    /// The engine's search for things in reach steps out through a sector's portals when the walker
    /// is in front of one by less than its radius. That only works if a portal's normal
    /// points back into the sector that owns it, which is not written down anywhere — so check it.
    /// </summary>
    [Fact]
    public void A_portals_normal_points_back_into_its_own_sector()
    {
        using var files = Install.Open();
        long inFront = 0, behind = 0;
        foreach (var name in Levels(files))
            foreach (var sector in NilLevel.Read(files.Read($"levels/{name}.nil"), name).Sectors)
            {
                if (sector.Portals.Length == 0 || sector.Vertices.Length == 0) continue;
                var centre = Vector3.Zero;
                foreach (var v in sector.Vertices) centre += v;
                centre /= sector.Vertices.Length;
                if (sector.SectorPlanes.Any(p => Vector3.Dot(p.Normal, centre) - p.Dist < -Collision.Slack)) continue;
                foreach (var portal in sector.Portals)
                {
                    if (portal.Quad.Length == 0) continue;
                    if (Vector3.Dot(centre - portal.Quad[0], portal.Normal) >= 0) inFront++; else behind++;
                }
            }
        Assert.Equal(0L, behind);
        Assert.Equal(31_465L, inFront);
    }

    [Fact]
    public void Every_box_thing_in_the_game_has_a_box_to_collide_with()
    {
        using var files = Install.Open();
        int boxes = 0, withBounds = 0, fromModel = 0, fromScene = 0;
        foreach (var name in new[] { "P1_OTDY", "L1_EAST", "P1_HAVN", "V1_MAIN" })
        {
            if (!files.TryRead($"levels/{name}.nil", out _)) continue;
            var place = Location.Load(files, name, things: false, sky: false);
            foreach (var t in place.Things.Where(t => t.Collide == CollideShape.Box))
            {
                boxes++;
                if (t.BoxMax == t.BoxMin) continue;
                withBounds++;
                if (t.Scene.Model is not null) fromScene++; else fromModel++;
            }
        }
        Assert.True(boxes > 60, $"expected plenty of box things, found {boxes}");
        Assert.Equal(boxes, withBounds);
        Assert.True(fromModel > 0 && fromScene > 0, $"{fromModel} from models, {fromScene} from inline geometry");
    }

    /// <summary>
    /// The six shape pairings, each against the arithmetic the original does. The two that matter
    /// most: sphere against sphere is the only test measured in three dimensions, and a cylinder
    /// stands on the thing's position rather than being centred on it.
    /// </summary>
    [Fact]
    public void The_shape_tests_measure_what_the_engine_measures()
    {
        var cylinder = Thing(CollideShape.Cylinder, radius: 22, height: 60);
        var sphere = Thing(CollideShape.Sphere, radius: 40);

        // Cylinder against cylinder: distance across the ground only, so height never widens it.
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(53, 0, 0), 32, 128, cylinder, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(55, 0, 0), 32, 128, cylinder, out _));
        // ... and the pair has to overlap in Z. The thing spans 0..60; a walker at 61 misses it.
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, 61), 32, 128, cylinder, out _));
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, 59), 32, 128, cylinder, out _));
        // A walker whose feet are below it still catches it, because the walker is 128 tall.
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, -100), 32, 128, cylinder, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, -129), 32, 128, cylinder, out _));

        // A cylinder walking into a sphere: the sphere is centred on the thing, so it hangs one
        // radius below it as well as standing one above — the walker's head catches it from 168
        // below, and the walker's feet clear it 40 above.
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, -167), 32, 128, sphere, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, -169), 32, 128, sphere, out _));
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, 39), 32, 128, sphere, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 0, 41), 32, 128, sphere, out _));
        // Across the ground it is still a flat measurement, even for a sphere.
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(71, 0, 30), 32, 128, sphere, out float flat));
        Assert.Equal(71f, flat, 3);

        // Sphere against sphere is the one pairing measured in three dimensions.
        Assert.True(Bodies.Hit(CollideShape.Sphere, new Vector3(0, 0, 60), 30, 0, sphere, out float solid));
        Assert.Equal(60f, solid, 3);
        Assert.False(Bodies.Hit(CollideShape.Sphere, new Vector3(0, 0, 71), 30, 0, sphere, out _));

        // A thing with no body is never hit, whatever the mover is.
        var nothing = Thing(CollideShape.None, radius: 100, height: 100);
        Assert.False(Bodies.Hit(CollideShape.Cylinder, Vector3.Zero, 32, 128, nothing, out _));
    }

    [Fact]
    public void A_box_is_measured_in_its_own_frame()
    {
        var box = Thing(CollideShape.Box, radius: 0, height: 0);
        box.BoxMin = new Vector3(-100, -20, 0);
        box.BoxMax = new Vector3(100, 20, 80);
        // Square on: wide across X, narrow across Y.
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 50, 0), 32, 128, box, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 53, 0), 32, 128, box, out _));
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(130, 0, 0), 32, 128, box, out _));
        // Turned a quarter turn about +Z, the long side now runs along Y instead.
        box.Orientation = new Vector3(0, 0, 90);
        Assert.True(Bodies.Hit(CollideShape.Cylinder, new Vector3(0, 130, 0), 32, 128, box, out _));
        Assert.False(Bodies.Hit(CollideShape.Cylinder, new Vector3(53, 0, 0), 32, 128, box, out _));
    }

    /// <summary>
    /// The one that matters: walk at a barrel and stop at it, and stop at exactly the two radii
    /// apart the engine's own arithmetic says. The approach only counts when the walker really
    /// covered the ground — otherwise a walker stuck on a wall would pass this without ever having
    /// met the barrel.
    /// </summary>
    [Fact]
    public void A_walker_stops_two_radii_short_of_the_body_it_walks_into()
    {
        using var files = Install.Open();
        int approached = 0, through = 0, short_ = 0;
        foreach (var name in new[] { "P1_OTDY", "L1_EAST" })
        {
            var place = Location.Load(files, name, things: false, sky: false, player: "Christof");
            var player = place.Player!;
            foreach (var t in place.Things)
            {
                if (t.Collide != CollideShape.Cylinder || ReferenceEquals(t, player.Thing) || !Bodies.Blocks(t)) continue;
                float away = t.Radius + player.Radius + 150f;
                for (int k = 0; k < 24; k++)
                {
                    float a = k * MathF.Tau / 24f;
                    var unit = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    player.Thing.Position = new Vector3(t.Position.X + unit.X * away, t.Position.Y + unit.Y * away, t.Position.Z + 16);
                    // Start on open ground, clear of the level itself and the full distance away.
                    if (!player.Ground() || !player.Fits(player.Position)) continue;
                    if (Bodies.Distance2D(player.Position, t.Position) < away - 30f) continue;
                    var start = player.Position;
                    var towards = Vector2.Normalize(new Vector2(t.Position.X - start.X, t.Position.Y - start.Y));
                    for (int step = 0; step < 180; step++) player.Move(towards, 1f / 60f);
                    if (Vector3.Distance(start, player.Position) < 80f) continue;    // never got going
                    approached++;
                    float gap = Bodies.Distance2D(player.Position, t.Position);
                    float touching = player.Radius + t.Radius;
                    if (gap < touching - 1f) through++;
                    // Stopping right on the barrel — rather than on a wall short of it — is the case
                    // this is really about, so count those separately and insist on plenty of them.
                    else if (gap > touching + 2f) short_++;
                    break;
                }
            }
        }
        Assert.True(approached > 30, $"only {approached} bodies could be walked at");
        Assert.Equal(0, through);
        Assert.True(approached - short_ > 15, $"only {approached - short_} of {approached} walks ended on the body itself");
    }

    /// <summary>
    /// **"Ahead" is measured from where the walker stands, along its move direction** (its position
    /// and its move direction), not from the end of the step it is trying. Standing
    /// inside a barrel whose middle is 20 units east of him and walking a hair east of north,
    /// Christof has the middle ahead of him, so the step is refused — although at the step's end
    /// the middle would already be behind him, which is what Vitomir measured from until then. (A
    /// summoned elemental placed inside Wilhem stood there for good that way, `DisciplineModeTests`.)
    /// </summary>
    [Fact]
    public void A_thing_whose_middle_is_a_fraction_ahead_blocks_a_step_that_would_end_past_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var me = place.Player!.Thing;
        var barrel = place.Things.First(t => t.Collide == CollideShape.Cylinder && Bodies.Blocks(t) && t.Radius >= 10);
        var from = barrel.Position - new Vector3(barrel.Radius * 0.5f, 0f, 0f);
        var way = Vector3.Normalize(new Vector3(0.05f, 1f, 0f));
        var at = from + way * 4f;
        Assert.True(Vector3.Dot(barrel.Position - at, way) < 0f);            // past it from the step's end
        int sector = (int)barrel.Sector;
        Assert.Same(barrel, place.Props!.Blocker(me, at, from, way, sector));
        // The old reading let him on (a neighbour may still be in the way; this barrel is not).
        Assert.NotSame(barrel, place.Props.Blocker(me, at, at, way, sector));
        // Walking away from its middle, he is let out, as the test below has it.
        Assert.NotSame(barrel, place.Props.Blocker(me, from - way * 4f, from, -way, sector));
    }

    /// <summary>
    /// A thing only blocks when it is ahead of the move. It is what lets someone who has
    /// ended up inside a barrel walk out of it — without it they would be stuck there for good.
    /// </summary>
    [Fact]
    public void A_thing_you_are_standing_in_never_blocks_you()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var player = place.Player!;
        int tried = 0, heldByItself = 0, escaped = 0;
        foreach (var t in place.Things)
        {
            if (t.Collide != CollideShape.Cylinder || !Bodies.Blocks(t) || t.Radius < 10) continue;
            for (int k = 0; k < 4; k++)
            {
                float a = k * MathF.Tau / 4f;
                var unit = new Vector2(MathF.Cos(a), MathF.Sin(a));
                player.Thing.Position = new Vector3(t.Position.X + unit.X * t.Radius * 0.5f,
                                                   t.Position.Y + unit.Y * t.Radius * 0.5f, t.Position.Z + 8);
                if (!player.Ground()) continue;
                tried++;
                var start = player.Position;
                for (int step = 0; step < 45; step++) player.Move(unit, 1f / 30f);
                if (ReferenceEquals(player.BlockedBy, t)) heldByItself++;
                if (Vector3.Distance(start, player.Position) > 30f) escaped++;
            }
        }
        Assert.True(tried > 40, $"only {tried} bodies could be stood inside");
        Assert.Equal(0, heldByItself);
        // And most of the time walking out actually works — the ones that do not are held by a
        // wall or by the next barrel along, not by the one being stood in.
        Assert.True(escaped > tried / 4, $"only {escaped} of {tried} walked back out");
    }

    [Fact]
    public void No_patch_of_walkable_ground_is_fenced_in_by_the_props_on_it()
    {
        using var files = Install.Open();
        foreach (var name in new[] { "P1_OTDY", "L1_EAST", "P1_HAVN" })
        {
            var place = Location.Load(files, name, things: false, sky: false, player: "Christof");
            var player = place.Player!;
            int fenced = 0, tested = 0;
            foreach (var node in place.Walk!.Nodes)
            {
                if (node.Outline.Length == 0) continue;
                var c = Vector2.Zero;
                foreach (var v in node.Outline) c += v;
                c /= node.Outline.Length;
                var feet = new Vector3(c, node.Height(c.X, c.Y));
                int sector = place.Solid!.SectorAt(feet with { Z = feet.Z + Collision.Body[0] });
                tested++;
                bool everyWay = true;
                for (int k = 0; k < 8 && everyWay; k++)
                {
                    float a = k * MathF.Tau / 8f;
                    if (place.Props!.Blocker(player.Thing, feet, feet, new Vector3(MathF.Cos(a), MathF.Sin(a), 0f), sector) is null)
                        everyWay = false;
                }
                if (everyWay) fenced++;
            }
            Assert.True(tested > 50, $"{name}: only {tested} patches of ground");
            Assert.Equal(0, fenced);
        }
    }

    [Fact]
    public void A_walker_ignores_the_dropped_swords_and_the_flying_arrows()
    {
        var walker = Thing(CollideShape.Cylinder, 32, 128);
        foreach (var type in new[] { ThingType.Item, ThingType.Projectile, ThingType.Weapon, ThingType.Armor })
            Assert.False(Bodies.Considers(walker, Thing(CollideShape.Cylinder, 40, 40, type)));
        foreach (var type in new[] { ThingType.Actor, ThingType.Prop, ThingType.Player, ThingType.Container })
            Assert.True(Bodies.Considers(walker, Thing(CollideShape.Cylinder, 40, 40, type)));
        // A thing whose collisions are switched off is out too, and so is a thing with no body.
        var off = Thing(CollideShape.Cylinder, 40, 40);
        off.Flags = WorldThing.Flag.DisableCollide;
        Assert.False(Bodies.Considers(walker, off));
        Assert.False(Bodies.Considers(walker, Thing(CollideShape.None, 40, 40)));
        Assert.False(Bodies.Considers(walker, walker));
    }

    [Fact]
    public void The_dead_and_the_ghostly_do_not_stand_in_the_way()
    {
        var solid = Thing(CollideShape.Cylinder, 40, 40);
        Assert.True(Bodies.Blocks(solid));

        var ghost = Thing(CollideShape.Cylinder, 40, 40);
        ghost.Flags = WorldThing.Flag.NoBlock;
        Assert.False(Bodies.Blocks(ghost));

        var corpse = Thing(CollideShape.Cylinder, 40, 40, ThingType.Actor, alive: false);
        Assert.False(Bodies.Blocks(corpse));
        Assert.True(Bodies.Blocks(Thing(CollideShape.Cylinder, 40, 40, ThingType.Actor, alive: true)));
    }

    static WorldThing Thing(CollideShape shape, float radius, float height = 0,
                            ThingType type = ThingType.Prop, bool? alive = null)
    {
        var actor = alive is null ? null : new Actor { Flags = alive.Value ? 0 : Actor.Flag.Dead };
        return new WorldThing
        {
            Index = 0, Guid = 1, Scene = NsdThing.Placed("test", Vector3.Zero, Vector3.Zero, 0),
            Template = "test", Type = type, Collide = shape, Radius = radius, Height = height, Actor = actor,
        };
    }
}
