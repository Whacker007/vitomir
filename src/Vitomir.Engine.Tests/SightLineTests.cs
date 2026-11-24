// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The AI's line of sight is the engine's sweep: things and surfaces from the
/// looker's eye to the target's middle, where a surface blocks unless it is translucent and a thing
/// blocks only if it carries <c>THING_FLAG_VISBLOCK</c>. Vitomir's own ray, before, was blocked by
/// every surface and by no thing. The two rules that differ are pinned here on the shipped levels.
/// </summary>
public class SightLineTests
{
    static WorldThing Body(Vector3 feet, long sector) => new()
    {
        Index = -1,
        Guid = 1,
        Scene = NsdThing.Placed("test", feet, Vector3.Zero, 0),
        Template = "test",
        Position = feet,
        Sector = sector,
        Collide = CollideShape.Cylinder,
        Radius = 32f,
        Height = 128f,
    };

    /// <summary>
    /// **A translucent surface does not block sight** (the sight test skips groups with `0x6000`). Old
    /// Town's one pane and the first monastery's translucent floor, crossed square on from 40 units
    /// either side: the sweep meets the surface, `Pick.Nearest` meets it too, and the sight line
    /// passes — where the line meets nothing else.
    /// </summary>
    [Fact]
    public void A_translucent_surface_does_not_block_sight()
    {
        using var files = Install.Open();
        int crossed = 0;
        foreach (var name in new[] { "P1_OTDY", "P1_MON1" })
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name + ".nil");
            var solid = new Collision(level);
            for (int si = 0; si < level.Sectors.Length; si++)
            {
                var sector = level.Sectors[si];
                foreach (var group in sector.Groups)
                {
                    if ((group.SurfaceFlags & Sight.SeenThrough) == 0) continue;
                    for (int f = group.FirstSurface; f < group.FirstSurface + group.NumSurfaces; f++)
                    {
                        var surface = sector.Surfaces[f];
                        if (surface.NumVerts < 3) continue;
                        var n = sector.SurfacePlanes[surface.Plane].Normal;
                        var middle = Vector3.Zero;
                        for (int i = 0; i < surface.NumVerts; i++)
                            middle += sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];
                        middle /= surface.NumVerts;
                        var eye = middle + n * 40f;
                        var aim = middle - n * 40f;
                        int start = solid.SectorAt(eye, si);
                        if (start < 0) continue;
                        var line = WorldSweep.Line(level, null, start, eye, -n, 80f, new WorldSweep.Asks(Pick.Tests.Surfaces));
                        // Only the lines whose one surface is the translucent one.
                        if (line.Count != 1 || line[0].Sector != si || line[0].Surface != f) continue;
                        var looker = Body(eye - new Vector3(0f, 0f, 128f * Sight.EyeShare), start);
                        var target = Body(aim - new Vector3(0f, 0f, 128f * Sight.TargetShare), start);
                        Assert.NotNull(Pick.Nearest(level, eye, -n, 80f));      // the old ray stopped on it
                        Assert.True(Sight.Sees(level, looker, target), $"{name}: blocked by translucent {si}/{f}");
                        crossed++;
                    }
                }
            }
        }
        Assert.True(crossed > 0, "no translucent surface crossed");
    }

    /// <summary>
    /// **A thing flagged `VISBLOCK` blocks sight, and nothing else does**. Three of the
    /// monastery's characters in a row across open floor: the one in the middle is seen past until
    /// it carries the flag.
    /// </summary>
    [Fact]
    public void A_thing_flagged_visblock_blocks_sight_and_no_other_thing_does()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var actors = place.Things.Where(t => t.Actor is not null && t.Collide == CollideShape.Cylinder && t.Height >= 100f).Take(3).ToList();
        Assert.Equal(3, actors.Count);
        var (looker, blocker, target) = (actors[0], actors[1], actors[2]);

        // An open run of floor: the walk surface under Christof, along whichever way is clear.
        var christof = place.Player!.Thing.Position;
        foreach (var node in place.Walk!.Nodes)
        {
            if (node.Outline.Length == 0 || node.Max.X - node.Min.X < 260f) continue;
            var c = (node.Min + node.Max) * 0.5f;
            var a = new Vector2(c.X - 120f, c.Y);
            var b = new Vector2(c.X + 120f, c.Y);
            if (!node.Contains(a) || !node.Contains(b) || !node.Contains(c)) continue;
            place.Move(looker, new Vector3(a, node.Height(a.X, a.Y)));
            place.Move(blocker, new Vector3(c, node.Height(c.X, c.Y)));
            place.Move(target, new Vector3(b, node.Height(b.X, b.Y)));
            blocker.Flags &= ~WorldThing.Flag.VisBlock;
            if (!Sight.Sees(place.Level, looker, target)) continue;           // a wall or a pillar in the way
            // The sweep does meet the one in the middle: it is seen past, not missed.
            var from = Sight.Eye(looker);
            var span = Sight.Eye(target, Sight.TargetShare) - from;
            Assert.Contains(WorldSweep.Line(place.Level, place.Things, (int)looker.Sector, from, Vector3.Normalize(span), span.Length(),
                                            new WorldSweep.Asks(Pick.Tests.Things)),
                            m => ReferenceEquals(m.Thing, blocker));
            blocker.Flags |= WorldThing.Flag.VisBlock;
            Assert.False(Sight.Sees(place.Level, looker, target));
            return;
        }
        Assert.Fail("no open run of floor in the monastery");
    }
}
