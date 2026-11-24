// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The original's sweep (<see cref="WorldSweep"/>), read whole: the line the
/// physics step, the ground test and the click cast through the world. The body tests are the
/// engine's own arithmetic, odd corners included, so they are pinned on numbers that show the odd
/// corner; the rest runs on the shipped levels, because what the sweep is *for* is agreeing with
/// what 1.1 finds there — the floor under a walker, the surface under the pointer.
/// </summary>
public class SweepTests
{
    // ---- the three bodies, and the plane ---------------------------------------------------------

    /// <summary>
    /// The sphere test records the entry distance only when it lies inside the line; a line that stops
    /// short of a sphere by less than its radius is still a hit, **at distance 0**.
    /// </summary>
    [Fact]
    public void A_sphere_the_line_stops_just_short_of_is_met_at_nought()
    {
        // Along the X axis for 10; a sphere of radius 10 whose centre is 19 along and 8 off.
        Assert.True(WorldSweep.MeetsSphere(Vector3.Zero, Vector3.UnitX, 10f, 0f, new Vector3(19f, 8f, 0f), 10f, out float d));
        Assert.Equal(0f, d);
        // Moved 2 nearer, the entry (7.0) is inside the line and is what is recorded.
        Assert.True(WorldSweep.MeetsSphere(Vector3.Zero, Vector3.UnitX, 10f, 0f, new Vector3(13f, 8f, 0f), 10f, out d));
        Assert.Equal(7f, d, 3);
        // And a sphere wholly beyond the line's end plus its radius is not met.
        Assert.False(WorldSweep.MeetsSphere(Vector3.Zero, Vector3.UnitX, 10f, 0f, new Vector3(25f, 0f, 0f), 10f, out _));
    }

    /// <summary>A line that starts inside a sphere meets it at 0, as it starts inside a box.</summary>
    [Fact]
    public void A_line_that_starts_inside_a_body_meets_it_at_nought()
    {
        Assert.True(WorldSweep.MeetsSphere(Vector3.Zero, Vector3.UnitX, 50f, 0f, new Vector3(1f, 0f, 0f), 10f, out float d));
        Assert.Equal(0f, d);
        Assert.True(WorldSweep.MeetsBox(Vector3.Zero, Vector3.UnitX, 50f, Matrix4x4.Identity, Vector3.Zero,
                                   new Vector3(-5f), new Vector3(5f), out d));
        Assert.Equal(0f, d);
    }

    /// <summary>
    /// The cylinder test does not solve for the closest approach: it measures the line where it crosses the
    /// line through the cylinder's axis square to the start's own bearing on it. So a line that
    /// passes 44.7 units from the axis of a cylinder of radius 50 — inside it, and met by an exact
    /// test — is **missed**, because where it crosses that square line it is 50 off.
    /// </summary>
    [Fact]
    public void A_cylinder_is_measured_where_the_line_crosses_it_square_to_the_start_not_where_it_comes_closest()
    {
        var dir = Vector3.Normalize(new Vector3(1f, 0.5f, 0f));
        var feet = new Vector3(100f, 0f, -50f);
        Assert.False(WorldSweep.MeetsCylinder(Vector3.Zero, dir, 300f, feet, 50f, 100f, out _));
        Assert.NotNull(Pick.Meets(feet, 50f, 100f, Vector3.Zero, dir));          // the exact answer
        // Head on it is the ordinary answer: the near face, 50 short of the axis.
        Assert.True(WorldSweep.MeetsCylinder(Vector3.Zero, Vector3.UnitX, 300f, feet, 50f, 100f, out float d));
        Assert.Equal(50f, d, 3);
        // And a line that passes over the roof does not meet it at all.
        Assert.False(WorldSweep.MeetsCylinder(new Vector3(0f, 0f, 60f), Vector3.UnitX, 300f, feet, 50f, 100f, out _));
    }

    /// <summary>The box test is an exact slab clip against the box's own three axes, turned with the thing.</summary>
    [Fact]
    public void A_box_is_met_where_the_line_enters_it_turned_as_the_thing_is()
    {
        var min = new Vector3(-10f, -40f, 0f);
        var max = new Vector3(10f, 40f, 20f);
        var at = new Vector3(100f, 0f, 0f);
        var start = new Vector3(0f, 30f, 10f);
        // Square on, the line along y = 30 enters the long box at x = 90.
        Assert.True(WorldSweep.MeetsBox(start, Vector3.UnitX, 200f, Matrix4x4.Identity, at, min, max, out float d));
        Assert.Equal(90f, d, 3);
        // Turned a quarter, the box is 20 wide across y, so y = 30 passes it by.
        var turned = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        Assert.False(WorldSweep.MeetsBox(start, Vector3.UnitX, 200f, turned, at, min, max, out _));
        // And a line that stops before it does not reach it.
        Assert.False(WorldSweep.MeetsBox(start, Vector3.UnitX, 80f, Matrix4x4.Identity, at, min, max, out _));
    }

    /// <summary>
    /// A surface is met **from its front only** — a line starting behind a floor and
    /// rising through it does not meet it — and a start within 0.00001 of the plane counts as on it.
    /// </summary>
    [Fact]
    public void A_surface_is_met_only_from_its_front()
    {
        var floor = new NilPlane(Vector3.UnitZ, 0f);
        Assert.True(WorldSweep.MeetsPlane(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, 20f, floor, out float d));
        Assert.Equal(10f, d, 4);
        Assert.False(WorldSweep.MeetsPlane(new Vector3(0f, 0f, -10f), Vector3.UnitZ, 20f, floor, out _));
        Assert.True(WorldSweep.MeetsPlane(new Vector3(0f, 0f, 0.000005f), -Vector3.UnitZ, 20f, floor, out d));
        Assert.Equal(0f, d);
        // Too short to reach it.
        Assert.False(WorldSweep.MeetsPlane(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, 5f, floor, out _));
    }

    // ---- on the shipped levels -------------------------------------------------------------------

    static NilLevel Level(Vfs.GameFiles files, string name) =>
        NilLevel.Read(files.Read($"levels/{name}.nil"), name + ".nil");

    /// <summary>
    /// Every surface's plane has its reverse beside it, at index <c>^ 1</c> — which is why the tree
    /// walk marks a plane's partner with it. Checked over three levels, every pair.
    /// </summary>
    [Fact]
    public void Every_surface_plane_has_its_reverse_at_the_index_one_bit_away()
    {
        using var files = Install.Open();
        foreach (var name in new[] { "P1_OTDY", "P1_MON1", "P1_MON2" })
            foreach (var sector in Level(files, name).Sectors)
                for (int p = 0; p + 1 < sector.SurfacePlanes.Length; p += 2)
                {
                    var a = sector.SurfacePlanes[p];
                    var b = sector.SurfacePlanes[p ^ 1];
                    Assert.True(Vector3.Distance(a.Normal, -b.Normal) < 1e-3f && MathF.Abs(a.Dist + b.Dist) < 1e-2f,
                                $"{name}: plane {p} and {p ^ 1} are not a pair");
                }
    }

    /// <summary>
    /// **The engine's ground ray finds the floor the walk map does.** The ground ray casts the sweep
    /// straight down from 50 over the feet for 140 and takes the first walk-flagged surface;
    /// `WalkMap.Beneath` takes the highest walk surface from 50 above to 90 below. Sampled four
    /// times on every piece of ground, the two agree everywhere in both monasteries, and in Old Town
    /// everywhere but on a handful of floors the engine's own inside test (which assumes
    /// a convex polygon) calls "not over it" — where 1.1 keeps the surface it had, and so does
    /// Vitomir, by never asking unless the feet leave that surface.
    /// </summary>
    [Theory]
    [InlineData("P1_MON1", 1.0)]
    [InlineData("P1_MON2", 1.0)]
    [InlineData("P1_OTDY", 0.99)]
    public void The_ground_ray_finds_the_floor_the_walk_map_does(string name, double atLeast)
    {
        using var files = Install.Open();
        var level = Level(files, name);
        var map = new WalkMap(level);
        var solid = new Collision(level);
        var rng = new Random(1);
        int asked = 0, same = 0;
        foreach (var node in map.Nodes)
        {
            if (node.Outline.Length == 0) continue;
            var c = Vector2.Zero;
            foreach (var v in node.Outline) c += v;
            c /= node.Outline.Length;
            for (int k = 0; k < 4; k++)
            {
                var p = Vector2.Lerp(c, node.Outline[rng.Next(node.Outline.Length)], (float)rng.NextDouble() * 0.9f);
                if (!node.Contains(p)) continue;
                var feet = new Vector3(p, node.Height(p.X, p.Y) + (float)rng.NextDouble() * 20f);
                int hint = solid.SectorAt(feet with { Z = feet.Z + 16f }, node.Sector);
                asked++;
                int ours = map.Beneath(feet);
                var theirs = WorldSweep.Ground(level, hint < 0 ? node.Sector : hint, feet);
                if (ours >= 0 && theirs is { } hit && MathF.Abs(map.Nodes[ours].Height(feet.X, feet.Y) - hit.Point.Z) < 0.5f)
                    same++;
            }
        }
        Assert.True(asked > 1000, $"only {asked} points asked");
        Assert.True(same >= asked * atLeast, $"{name}: the ray and the walk map agree on {same} of {asked}");
    }

    /// <summary>
    /// **The sweep never meets a detail brush** (group flag 0x4, skipped): the
    /// monastery's furniture is detail, and a line cast down on to any of it from just above goes
    /// through to what is under it. `Pick.Nearest`, which is ours, stops on it.
    /// </summary>
    [Fact]
    public void The_sweep_passes_through_detail_brushes()
    {
        using var files = Install.Open();
        var level = Level(files, "P1_MON2");
        var solid = new Collision(level);
        int tried = 0;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var sector = level.Sectors[si];
            foreach (var group in sector.Groups)
            {
                if ((group.SurfaceFlags & NilGroup.FlagDetail) == 0) continue;
                for (int f = group.FirstSurface; f < group.FirstSurface + group.NumSurfaces; f++)
                {
                    var surface = sector.Surfaces[f];
                    var plane = sector.SurfacePlanes[surface.Plane];
                    if (surface.NumVerts < 3 || plane.Normal.Z < 0.9f) continue;       // the tops of things
                    var middle = Vector3.Zero;
                    for (int i = 0; i < surface.NumVerts; i++)
                        middle += sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];
                    middle /= surface.NumVerts;
                    var from = middle + new Vector3(0f, 0f, 8f);
                    int start = solid.SectorAt(from, si);
                    if (start < 0 || Pick.Nearest(level, from, -Vector3.UnitZ, 100f) is not { } ours
                        || ours.Sector != si || ours.Surface != f) continue;
                    tried++;
                    var theirs = WorldSweep.FirstSurface(level, start, from, -Vector3.UnitZ, 100f);
                    Assert.False(theirs is { } t && t.Sector == si && t.Surface == f, $"the sweep met detail surface {si}/{f}");
                }
            }
        }
        Assert.True(tried > 20, $"only {tried} detail tops tried");
    }

    /// <summary>
    /// **`collidePos` agrees with the click Vitomir has, but for detail brushes**: from
    /// cameras put where the game's camera goes — 128 over a walkable spot, pulled in from the
    /// walls — the first surface a line through the view meets is the same one `Pick.Nearest`
    /// finds on every line in Old Town and the first monastery. The second monastery's furniture
    /// is where they part: the sweep goes through it to the floor.
    /// </summary>
    [Theory]
    [InlineData("P1_OTDY")]
    [InlineData("P1_MON1")]
    [InlineData("P1_MON2")]
    public void Collidepos_meets_what_the_click_meets_except_a_detail_brush(string name)
    {
        using var files = Install.Open();
        var level = Level(files, name);
        var map = new WalkMap(level);
        var solid = new Collision(level);
        var rng = new Random(2);
        int asked = 0;
        foreach (var node in map.Nodes)
        {
            if (node.Outline.Length == 0 || rng.Next(3) != 0) continue;
            var c = Vector2.Zero;
            foreach (var v in node.Outline) c += v;
            c /= node.Outline.Length;
            var feet = new Vector3(c, node.Height(c.X, c.Y));
            float yaw = (float)(rng.NextDouble() * Math.Tau), back = 300f + (float)rng.NextDouble() * 200f;
            var pivot = feet + new Vector3(0f, 0f, 128f);
            var outward = Vector3.Normalize(new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0.5f));
            var eye = pivot + outward * ChaseRay.Distance(level, pivot, outward, back);
            var look = feet + new Vector3((float)rng.NextDouble() * 200f - 100f, (float)rng.NextDouble() * 200f - 100f, 0f);
            var dir = Vector3.Normalize(look - eye);
            int sector = solid.SectorAt(eye, node.Sector);
            if (sector < 0 || solid.Solid(eye)) continue;
            var ours = Pick.Nearest(level, eye, dir);
            var theirs = WorldSweep.FirstSurface(level, sector, eye, dir);
            if (ours is null && theirs is null) continue;
            asked++;
            Assert.NotNull(ours);
            Assert.NotNull(theirs);
            if (Vector3.Distance(ours!.Value.Point, theirs!.Value.Point) < 1f) continue;
            var s = level.Sectors[ours.Value.Sector];
            var group = s.Groups[s.Surfaces[ours.Value.Surface].Group];
            Assert.True((group.SurfaceFlags & NilGroup.FlagDetail) != 0 && theirs.Value.Distance > ours.Value.Distance,
                        $"{name}: the click met {ours.Value.Point}, collidePos {theirs.Value.Point}");
        }
        Assert.True(asked > 100, $"only {asked} lines");
    }

    /// <summary>
    /// **A click on the second monastery's furniture lands on the floor beyond it**, as 1.1's does:
    /// the pointer's question (<see cref="WorldSweep.Pointer(NilLevel, Collision?, int, Vector3, Vector3)"/>,
    /// `collidePos`) passes through detail brushes, so where `Pick.Nearest` stops on a pew or a
    /// table top and the click is refused as not walkable, the sweep reaches the walkable floor
    /// behind it. Everywhere else the two answer the same surface, walk node and all.
    /// </summary>
    [Fact]
    public void A_click_on_the_monasterys_furniture_lands_on_the_floor_beyond_it()
    {
        using var files = Install.Open();
        var level = Level(files, "P1_MON2");
        var map = new WalkMap(level);
        var solid = new Collision(level);
        var rng = new Random(2);
        int through = 0, same = 0;
        foreach (var node in map.Nodes)
        {
            if (node.Outline.Length == 0 || rng.Next(3) != 0) continue;
            var c = Vector2.Zero;
            foreach (var v in node.Outline) c += v;
            c /= node.Outline.Length;
            var feet = new Vector3(c, node.Height(c.X, c.Y));
            float yaw = (float)(rng.NextDouble() * Math.Tau), back = 300f + (float)rng.NextDouble() * 200f;
            var pivot = feet + new Vector3(0f, 0f, 128f);
            var outward = Vector3.Normalize(new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0.5f));
            var eye = pivot + outward * ChaseRay.Distance(level, pivot, outward, back);
            var look = feet + new Vector3((float)rng.NextDouble() * 200f - 100f, (float)rng.NextDouble() * 200f - 100f, 0f);
            var dir = Vector3.Normalize(look - eye);
            if (solid.Solid(eye)) continue;
            if (Pick.Nearest(level, eye, dir) is not { } ours || WorldSweep.Pointer(level, solid, node.Sector, eye, dir) is not { } theirs)
                continue;
            if (ours.Sector == theirs.Sector && ours.Surface == theirs.Surface)
            {
                Assert.Equal(ours.PathNode, theirs.PathNode);
                Assert.Equal(ours.Walkable, theirs.Walkable);
                same++;
                continue;
            }
            var s = level.Sectors[ours.Sector];
            Assert.NotEqual(0u, s.Groups[s.Surfaces[ours.Surface].Group].SurfaceFlags & NilGroup.FlagDetail);
            if (!ours.Walkable && theirs.Walkable && theirs.PathNode >= 0) through++;
        }
        Assert.True(same > 200, $"only {same} lines agreed");
        Assert.True(through > 0, "no click went through the furniture to a floor");
    }

    /// <summary>
    /// **A bullet goes through a detail brush** to what lies beyond it: the hitscan is the
    /// sweep with flags 7, and it moves the shot to the first surface it meets. Fired
    /// straight down on to the second monastery's furniture, it ends on the floor under it, where the
    /// ray it used to be (`Pick.Trace`) stopped it on the furniture's top.
    /// </summary>
    [Fact]
    public void A_bullet_goes_through_a_detail_brush_to_what_lies_beyond()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2");
        var level = place.Level;
        int fired = 0;
        for (int si = 0; si < level.Sectors.Length && fired < 5; si++)
        {
            var sector = level.Sectors[si];
            foreach (var group in sector.Groups)
            {
                if ((group.SurfaceFlags & NilGroup.FlagDetail) == 0 || fired >= 5) continue;
                for (int f = group.FirstSurface; f < group.FirstSurface + group.NumSurfaces && fired < 5; f++)
                {
                    var surface = sector.Surfaces[f];
                    var plane = sector.SurfacePlanes[surface.Plane];
                    if (surface.NumVerts < 3 || plane.Normal.Z < 0.9f) continue;
                    var middle = Vector3.Zero;
                    for (int i = 0; i < surface.NumVerts; i++)
                        middle += sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];
                    middle /= surface.NumVerts;
                    var from = middle + new Vector3(0f, 0f, 8f);
                    int start = place.Solid!.SectorAt(from, si);
                    if (start < 0 || WorldSweep.FirstSurface(level, start, from, -Vector3.UnitZ, 300f) is not { } floor) continue;
                    var shot = place.Place("arrow", from, Projectiles.Facing(-Vector3.UnitZ), start);
                    Assert.NotNull(shot);
                    shot!.ProjectileRange = 300f;
                    Projectiles.Hitscan(shot, new Ai.Rng(1));
                    Assert.True(shot.Removed);
                    Assert.True(shot.Position.Z < middle.Z - 1f, $"the shot stopped at {shot.Position}, the furniture's top is at {middle.Z}");
                    Assert.Equal(floor.Point.Z, shot.Position.Z, 1);
                    fired++;
                }
            }
        }
        Assert.True(fired > 0, "no furniture top to fire at");
    }

    /// <summary>
    /// A sphere moving at a plane touches it where its distance falls to the radius; one
    /// already within the radius touches where it starts; one moving away, or starting more than the
    /// radius behind, does not touch it.
    /// </summary>
    [Fact]
    public void A_moving_sphere_touches_a_plane_where_its_distance_falls_to_its_radius()
    {
        var wall = new NilPlane(Vector3.UnitX, 0f);                       // the plane x = 0, facing +x
        Assert.True(WorldSweep.Touches(wall, new Vector3(100f, 0f, 0f), new Vector3(-50f, 0f, 0f), 10f, out var at));
        Assert.Equal(10f, at.X, 3);
        Assert.True(WorldSweep.Touches(wall, new Vector3(5f, 0f, 0f), new Vector3(-50f, 0f, 0f), 10f, out at));
        Assert.Equal(5f, at.X, 3);
        Assert.False(WorldSweep.Touches(wall, new Vector3(5f, 0f, 0f), new Vector3(50f, 0f, 0f), 10f, out _));   // moving away
        Assert.False(WorldSweep.Touches(wall, new Vector3(-20f, 0f, 0f), new Vector3(-50f, 0f, 0f), 10f, out _)); // behind it
        Assert.False(WorldSweep.Touches(wall, new Vector3(100f, 0f, 0f), new Vector3(40f, 0f, 0f), 10f, out _));  // stops short
    }

    /// <summary>
    /// **The fat ray stops a radius short of a wall it meets square on**: from open floor
    /// in the first monastery, horizontally at 64 up, the thin line's first wall at distance D is
    /// the fat ray's first contact at D − 10 for a sphere of radius 10 — the camera's
    /// (`ChaseRay.Clearance`) — and it is never later than the thin line's.
    /// </summary>
    [Fact]
    public void The_fat_ray_stops_a_radius_short_of_a_wall_met_square_on()
    {
        using var files = Install.Open();
        var level = Level(files, "P1_MON1");
        var map = new WalkMap(level);
        var solid = new Collision(level);
        int checkedWalls = 0;
        foreach (var node in map.Nodes.Where(n => n.Outline.Length > 0).Take(300))
        {
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var from = new Vector3(c, node.Height(c.X, c.Y) + 64f);
            int sector = solid.SectorAt(from, node.Sector);
            if (sector < 0 || solid.Solid(from)) continue;
            foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
            {
                if (WorldSweep.FirstSurface(level, sector, from, dir, 2000f) is not { } thin) continue;
                if (Vector3.Dot(thin.Normal, dir) > -0.999f || thin.Distance < 40f) continue;     // square on, not too near
                var fat = WorldSweep.Fat(level, null, sector, from, dir, 2000f, 10f, new WorldSweep.Asks(Pick.Tests.Surfaces));
                Assert.NotEmpty(fat);
                Assert.True(fat[0].Distance <= thin.Distance + 0.05f,
                            $"the fat ray's first contact is at {fat[0].Distance}, the wall at {thin.Distance}");
                // Where the wall's own sector begins within the radius of it, the touch is taken on
                // the line clipped to that sector — its start — which is the engine's arithmetic too.
                if (fat[0].Sector == thin.Sector && fat[0].Surface == thin.Surface
                    && MathF.Abs(fat[0].Distance - (thin.Distance - 10f)) < 0.1f)
                    checkedWalls++;
            }
        }
        Assert.True(checkedWalls > 20, $"only {checkedWalls} walls met square on");
    }

    /// <summary>
    /// **The query's bit `0x20` meets a camera region's hull**: Petrin Hill's
    /// `cameraclip_region`s — the only camera-blocking regions in the Prague scenes tried — are met
    /// where a line from outside comes into their hull, and only when the bit is asked for and the
    /// region carries its camera flag.
    /// </summary>
    [Fact]
    public void A_camera_region_is_met_where_the_line_comes_into_its_hull()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_HILLD", things: true, sky: false, scene: "HILLD_4_1");
        var solid = place.Solid!;
        int met = 0;
        foreach (var region in place.Things.Where(t => t.Type == ThingType.Region
                                                       && (t.RegionFlags & Regions.BlocksCameras) != 0))
        {
            var centre = region.Position;
            if (!Regions.Contains(region, centre)) continue;
            foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
            {
                var from = centre - dir * 400f;
                int sector = solid.SectorAt(from);
                if (sector < 0 || Regions.Contains(region, from)) continue;
                var asked = new WorldSweep.Asks(Pick.Tests.None, Regions: place.Regions);
                var hit = WorldSweep.Line(place.Level, null, sector, from, dir, 400f, asked)
                                    .FirstOrDefault(c => ReferenceEquals(c.Thing, region));
                if (hit.Thing is null) continue;                      // the flood did not reach a sector listing it
                Assert.True(Regions.Contains(region, hit.Point + dir * 0.5f), $"{hit.Point} is not at the hull");
                Assert.False(Regions.Contains(region, hit.Point - dir * 0.5f), $"{hit.Point} is inside the hull");
                Assert.DoesNotContain(WorldSweep.Line(place.Level, null, sector, from, dir, 400f, new WorldSweep.Asks(Pick.Tests.None)),
                                      c => ReferenceEquals(c.Thing, region));
                region.RegionFlags &= ~Regions.BlocksCameras;
                Assert.DoesNotContain(WorldSweep.Line(place.Level, null, sector, from, dir, 400f, asked),
                                      c => ReferenceEquals(c.Thing, region));
                region.RegionFlags |= Regions.BlocksCameras;
                met++;
            }
        }
        Assert.True(met > 0, "no camera region met");
    }

    /// <summary>
    /// **The sector finder never answers "nowhere"**: a point outside every sector's
    /// volume is given back the sector it was asked from.
    /// </summary>
    [Fact]
    public void A_point_in_no_sector_keeps_the_sector_it_was_asked_from()
    {
        using var files = Install.Open();
        var level = Level(files, "P1_MON1");
        var solid = new Collision(level);
        var far = new Vector3(1e6f, 1e6f, 1e6f);
        Assert.Equal(-1, solid.SectorAt(far, 7));
        Assert.Equal(7, WorldSweep.SectorOf(level, solid, far, 7));
    }
}
