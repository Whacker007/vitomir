// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Cell levels** (ours): the collision triangles' tree finds the
/// floor, meets rays and keeps a body its radius off a wall; a scene cooked into a package reads back
/// whole, with a navigation mesh that has ground where the floor is and none inside a building; the
/// walk map made of it stands a walker on the triangles' own height; a route round the house is
/// Detour's, and keeps off its walls; and Christof, put in a cooked
/// level with the game's own loader, stays on his feet as the world is stepped and is stopped by the
/// building's wall; and the level's date is kept in its manifest. The floor inside the house's closed walls is ground too — a room nobody can
/// reach — which is what Recast makes of an enclosed space. Everything is generated here: a 20 m floor and one 4 m × 4 m house on it.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellLevelTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>
    /// A 20 m × 20 m floor of 1 m squares (material 0, "ground") with a 4 m × 4 m × 3 m house on it
    /// from x 2 to 6 m and y −2 to 2 m (material 1, "wall"), in engine units, faces outward.
    /// </summary>
    static (List<Vector3> V, List<int> I, List<int> Mat) Scene()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)   // a b c d counter-clockwise seen from the front
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            mat.AddRange([m, m]);
        }
        for (int x = -10; x < 10; x++)
            for (int y = -10; y < 10; y++)
                Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
        const float X0 = 2, X1 = 6, Y0 = -2, Y1 = 2, H = 3;
        Quad(new(X0, Y0, 0), new(X1, Y0, 0), new(X1, Y0, H), new(X0, Y0, H), 1);   // south wall, facing −y
        Quad(new(X1, Y0, 0), new(X1, Y1, 0), new(X1, Y1, H), new(X1, Y0, H), 1);   // east, +x
        Quad(new(X1, Y1, 0), new(X0, Y1, 0), new(X0, Y1, H), new(X1, Y1, H), 1);   // north, +y
        Quad(new(X0, Y1, 0), new(X0, Y0, 0), new(X0, Y0, H), new(X0, Y1, H), 1);   // west, −x
        Quad(new(X0, Y0, H), new(X1, Y0, H), new(X1, Y1, H), new(X0, Y1, H), 1);   // roof, +z
        return (v, idx, mat);
    }

    static CellCollision Collision()
    {
        var (v, idx, mat) = Scene();
        var surfaces = new L2Surface[mat.Count];
        for (int t = 0; t < surfaces.Length; t++)
            surfaces[t] = L2Surface.Solid | (mat[t] == 0 ? L2Surface.Walk : 0);
        var chunk = CellCollision.Build([.. v.Select(p => p / M)], [.. idx], surfaces, [.. mat.Select(m => (ushort)m)]);
        return new CellCollision(L2Collision.Decode(chunk.Encode()), M);
    }

    /// <summary>Cooks the scene into a package under a fresh temporary folder.</summary>
    static (string Root, CellCookResult Result) Cook(string name = "celltest", L2Date? date = null)
    {
        var (v, idx, mat) = Scene();
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        var result = CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall"],
            Ground = m => m == "ground", Passable = _ => false,
            Lights = [new L2Light("torch00", L2Light.Point, new Vector3(-3, 3, 2), new Vector3(1, 0.6f, 0.3f), 2, 8)],
            Anchors = [new L2Anchor { Id = $"{name}/starts/0", Kind = "start", Scene = name, Name = "playerstart00",
                                      Position = [-5, 0, 1], Rotation = [0, 0, -90], Group = 0 }],
            Date = date,
        }, L2Package.PathFor(root, name));
        return (root, result);
    }

    [Fact]
    public void The_tree_finds_the_floor_under_a_point_and_nothing_where_there_is_none()
    {
        var c = Collision();
        Assert.True(c.Nodes.Length > 1, "a floor of 800 triangles should make a tree, not one leaf");
        Assert.Equal(0f, c.Floor(-5 * M, 3 * M, 10f, 40f)!.Value, 3);
        Assert.Null(c.Floor(-5 * M, 3 * M, 100f, 40f));                  // the floor is out of reach from up there
        Assert.Null(c.Floor(30 * M, 0, 0, 40f));                          // off the floor's edge
        // The roof is solid but not ground: no floor at its height.
        Assert.Null(c.Floor(4 * M, 0, 3 * M, 20f));
    }

    [Fact]
    public void A_ray_meets_the_first_solid_triangle_it_crosses()
    {
        var c = Collision();
        var down = c.Ray(new Vector3(-5 * M, 0, 100), -Vector3.UnitZ, 1000f);
        Assert.NotNull(down);
        Assert.Equal(100f, down.Value.Distance, 2);
        Assert.Equal(1f, down.Value.Normal.Z, 3);
        // Along the ground towards the house, a metre up: its west wall at x = 2 m.
        var across = c.Ray(new Vector3(-5 * M, 0, M), Vector3.UnitX, 20 * M);
        Assert.NotNull(across);
        Assert.Equal(2 * M, across.Value.Point.X, 1);
        // Only walkable triangles, asked for: the wall is passed and nothing is met.
        Assert.Null(c.Ray(new Vector3(-5 * M, 0, M), Vector3.UnitX, 20 * M, L2Surface.Walk));
    }

    [Fact]
    public void A_body_is_kept_its_radius_off_a_wall_and_the_ground_does_not_count()
    {
        var c = Collision();
        const float Radius = 18f, Step = 20f, Height = 128f;
        Assert.True(c.Open(new Vector3(-5 * M, 0, 0), Radius, Step, Height), "standing on open ground");
        Assert.False(c.Open(new Vector3(2 * M - 10f, 0, 0), Radius, Step, Height), "10 units from the west wall with an 18-unit body");
        Assert.True(c.Open(new Vector3(2 * M - 20f, 0, 0), Radius, Step, Height), "20 units from it");
    }

    [Fact]
    public void A_cooked_level_reads_back_whole_with_ground_on_its_floor_and_none_in_its_house()
    {
        var (root, result) = Cook();
        try
        {
            Assert.Equal(800 + 10, result.Triangles);
            Assert.Equal(800, result.Walkable);
            Assert.True(result.NavWalkPolygons > 0, "the navigation mesh has no walkable polygon");
            var package = L2Package.PathFor(root, "celltest");
            Assert.True(CellLevel.IsCellPackage(package));
            var level = CellLevel.Load(package);
            Assert.Same(level, CellLevel.Of(level.Shell));
            Assert.Null(CellLevel.Of(new Formats.NilLevel()));
            Assert.Equal("celltest", level.Name);
            Assert.Equal(result.Triangles, level.Collision.Triangles);
            Assert.NotNull(level.NavTile);
            Assert.Single(level.Lights);
            Assert.Equal(new Vector3(-3, 3, 2), level.Lights[0].Position);
            var start = Assert.Single(level.Scene.Starts);
            Assert.Equal(new Vector3(-5 * M, 0, 0), start.Position);             // put on the floor under the anchor

            // The shell is what the game is handed; its walk map is the navigation mesh.
            var walk = new WalkMap(level.Shell);
            Assert.Equal(result.NavWalkPolygons, walk.Count);
            Assert.True(walk.Find(new Vector3(-5 * M, 0, 0)) >= 0, "no ground at the start");
            Assert.True(walk.Find(new Vector3(-8 * M, -8 * M, 0)) >= 0, "no ground in the floor's corner");
            // The floor inside the house's closed walls is ground — a room — but no walker gets in from outside.
            int inside = walk.Find(new Vector3(4 * M, 0, 0));
            if (inside >= 0) Assert.DoesNotContain(inside, Reachable(walk, walk.Find(new Vector3(-5 * M, 0, 0))));
            Assert.True(walk.Find(new Vector3(4 * M, 0, 3 * M)) < 0, "ground on the house's roof");
            // The body's margin (0.25 m) is taken off round the house: nothing just outside its wall.
            Assert.True(walk.Find(new Vector3(2 * M - 5f, 0, 0)) < 0, "ground within the agent's radius of the wall");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_cooked_level_keeps_its_date_and_one_cooked_without_a_date_stands_on_mid_May()
    {
        // Ours: the generator's date goes through the cook into the manifest, where the renderer's sun reads it.
        var (root, _) = Cook("dated", new L2Date { Day = 243, Hour = 10f });
        var (rootOld, _) = Cook("undated");
        try
        {
            var date = L2Date.Of(CellLevel.Load(L2Package.PathFor(root, "dated")).Manifest);
            Assert.Equal((243, (float?)10f), (date.Day, date.Hour));
            var old = CellLevel.Load(L2Package.PathFor(rootOld, "undated")).Manifest;
            Assert.Null(old.Date);
            Assert.Equal((135, (float?)null), (L2Date.Of(old).Day, L2Date.Of(old).Hour));
        }
        finally { Directory.Delete(root, recursive: true); Directory.Delete(rootOld, recursive: true); }
    }

    /// <summary>Every piece of ground linked, however far, to <paramref name="from"/>.</summary>
    static HashSet<int> Reachable(WalkMap walk, int from)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0)
            foreach (int n in walk.Nodes[queue.Dequeue()].Neighbours)
                if (seen.Add(n)) queue.Enqueue(n);
        return seen;
    }

    [Fact]
    public void A_street_beside_a_flat_yard_keeps_its_ground_and_the_yard_gets_none()
    {
        // A 3 m street (ground) along a 17 m yard of the same height (solid, not ground): Recast can join
        // them, so a polygon's middle can fall in the yard; the street must stay one walkable whole (ours).
        // This small scene passes with or without the cook's corner test (CellCook.BuildNavMesh); the
        // generated Old Town is where the join happened (three exits' routes were cut before it). It
        // stands here as the behaviour to keep.
        var (v, idx, mat) = Scene();
        var floorOnly = idx.Count / 3 - 10;                                   // the floor's triangles come first
        for (int t = 0; t < floorOnly; t++)
        {
            var c = (v[idx[3 * t]] + v[idx[3 * t + 1]] + v[idx[3 * t + 2]]) / 3 / M;
            if (c.Y > -7) mat[t] = 2;
        }
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "yard", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall", "yard"],
                Ground = m => m == "ground", Passable = _ => false,
            }, L2Package.PathFor(root, "yard"));
            var walk = new WalkMap(CellLevel.Load(L2Package.PathFor(root, "yard")).Shell);
            int a = walk.Find(new Vector3(-8 * M, -8.5f * M, 0)), b = walk.Find(new Vector3(0, -8.5f * M, 0)), c2 = walk.Find(new Vector3(8 * M, -8.5f * M, 0));
            Assert.True(a >= 0 && b >= 0 && c2 >= 0, "no ground somewhere along the street");
            var linked = Reachable(walk, a);
            Assert.Contains(b, linked);
            Assert.Contains(c2, linked);
            Assert.True(walk.Find(new Vector3(-5 * M, 6 * M, 0)) < 0, "ground in the middle of the yard");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_narrow_street_along_a_wide_flat_yard_is_never_one_polygon_with_it()
    {
        // A 2 m street (ground) along the floor's south edge, and the other 18 m of the floor a yard of the
        // same height (solid, not ground), the house standing in it. Recast decides walkable by slope, so
        // unless the yard's triangles are rasterised as no area it makes street and yard one region and
        // cuts it into polygons reaching across both, and whichever way such a polygon is then flagged,
        // either the yard gets ground or the street loses some (ours: CellCook.BuildNavMesh).
        var (v, idx, mat) = Scene();
        var floorOnly = idx.Count / 3 - 10;                                   // the floor's triangles come first
        for (int t = 0; t < floorOnly; t++)
        {
            var c = (v[idx[3 * t]] + v[idx[3 * t + 1]] + v[idx[3 * t + 2]]) / 3 / M;
            if (c.Y > -8) mat[t] = 2;
        }
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "lane", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall", "yard"],
                Ground = m => m == "ground", Passable = _ => false,
            }, L2Package.PathFor(root, "lane"));
            var walk = new WalkMap(CellLevel.Load(L2Package.PathFor(root, "lane")).Shell);
            // The street's middle line, end to end, is ground and all of it one piece.
            int first = walk.Find(new Vector3(-9 * M, -9 * M, 0));
            Assert.True(first >= 0, "no ground at the street's west end");
            var linked = Reachable(walk, first);
            for (float x = -9; x <= 9; x += 0.5f)
            {
                int at = walk.Find(new Vector3(x * M, -9 * M, 0));
                Assert.True(at >= 0, $"no ground on the street at x {x} m");
                Assert.Contains(at, linked);
            }
            // Nowhere in the yard, half a metre or more from the street, is there ground.
            for (float y = -7.5f; y <= 9.5f; y += 0.5f)
                for (float x = -9.5f; x <= 9.5f; x += 0.5f)
                    Assert.True(walk.Find(new Vector3(x * M, y * M, 0)) < 0, $"ground in the yard at {x}, {y} m");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_decoration_only_mesh_is_drawn_but_stops_nothing()
    {
        // The house's ten triangles named decoration (CellCookInput.TrianglePassable, ours): the floor under
        // it is ground joined to the rest, as if the house were not there.
        var (v, idx, mat) = Scene();
        int count = idx.Count / 3;
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "deco", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall"],
                Ground = m => m == "ground", Passable = _ => false,
                TrianglePassable = [.. Enumerable.Range(0, count).Select(t => t >= count - 10)],
            }, L2Package.PathFor(root, "deco"));
            var level = CellLevel.Load(L2Package.PathFor(root, "deco"));
            Assert.Equal(0f, level.Collision.Floor(4 * M, 0, 4 * M, 5 * M)!.Value, 1);   // the floor, not the roof
            var walk = new WalkMap(level.Shell);
            int inside = walk.Find(new Vector3(4 * M, 0, 0)), outside = walk.Find(new Vector3(-8 * M, 0, 0));
            Assert.True(inside >= 0, "no ground where the house stood");
            Assert.Contains(inside, Reachable(walk, outside));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_walk_polygon_s_height_is_the_triangles_under_it_not_its_plane()
    {
        // A floor with a 10 cm hump in one square: the navigation mesh flattens it (its voxels are 5 cm
        // and its polygons are planes), the collision does not, and the walk map answers the collision.
        var (v, idx, mat) = Scene();
        for (int i = 0; i < v.Count; i++)
            if (v[i] == new Vector3(-5, 0, 0) * M) v[i] = v[i] with { Z = 0.1f * M };   // every square's corner there
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "hump", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall"],
                Ground = m => m == "ground", Passable = _ => false,
            }, L2Package.PathFor(root, "hump"));
            var level = CellLevel.Load(L2Package.PathFor(root, "hump"));
            var walk = new WalkMap(level.Shell);
            var at = new Vector3(-5 * M, 0, 0);
            int node = walk.Find(at);
            Assert.True(node >= 0);
            Assert.Equal(level.Collision.Floor(at.X, at.Y, 0, 40f)!.Value, walk.Nodes[node].Height(at.X, at.Y), 2);
            Assert.True(walk.Nodes[node].Height(at.X, at.Y) > 0.09f * M, "the hump's top was not found");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_route_round_the_house_is_Detour_s_and_keeps_off_its_walls()
    {
        var (root, _) = Cook("route");
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "route"));
            var walk = new WalkMap(level.Shell);
            var paths = new PathFinder(level.Shell);
            var (from, to) = (new Vector3(-2 * M, 0, 0), new Vector3(9 * M, 0, 0));   // west of the house, east of it
            var result = paths.Find(from, walk.Find(from), to, walk.Find(to), 18f, isActor: false, out var route);
            Assert.Equal(PathResult.Path, result);
            Assert.NotEmpty(route);
            // Every corner is outside the house's footprint by the navigation mesh's margin, near enough.
            foreach (var w in route)
                Assert.False(w.Point.X > 2 * M - 10 && w.Point.X < 6 * M + 10 && MathF.Abs(w.Point.Y) < 2 * M + 10,
                             $"a corner at {w.Point} is in the house");
            Assert.All(route, w => Assert.True(walk.IsGround(w.Node)));
            // Across open ground the path is one straight line.
            var (a, b) = (new Vector3(-8 * M, -8 * M, 0), new Vector3(-8 * M, 8 * M, 0));
            Assert.Equal(PathResult.Straight, paths.Find(a, walk.Find(a), b, walk.Find(b), 18f, isActor: false, out _));
            // Into the house's closed room: no route.
            var inside = new Vector3(4 * M, 0, 0);
            if (walk.Find(inside) is var room and >= 0)
                Assert.Equal(PathResult.None, paths.Find(from, walk.Find(from), inside, room, 18f, isActor: false, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Christof_stands_in_a_cooked_level_steps_without_falling_and_the_house_stops_him()
    {
        using var files = Install.Open();
        var (root, _) = Cook("celltestplay");
        var was = L2Levels.Folder;
        try
        {
            L2Levels.Folder = root;
            var place = Location.Load(files, "celltestplay", player: "Christof");
            Assert.NotNull(CellLevel.Of(place.Level));
            var player = place.Player!;
            Assert.True(player.Node >= 0, "Christof is on no ground");
            Assert.Equal(0f, player.Position.Z, 1);
            var sim = new Simulation(place);
            for (int i = 0; i < 120; i++) sim.Step(1f / 60f);
            Assert.True(player.Node >= 0);
            Assert.Equal(0f, player.Position.Z, 1);
            // Walked east, at the house: he stops at its wall, short of it by the navigation mesh's margin.
            for (int i = 0; i < 60 * 8; i++) { player.Move(Vector2.UnitX, 1f / 60f); sim.Step(1f / 60f); }
            Assert.True(player.Position.X < 2 * M, $"Christof walked into the house, to x {player.Position.X}");
            Assert.True(player.Position.X > 2 * M - 40f, $"Christof stopped short of the house, at x {player.Position.X}");
            Assert.Equal(0f, player.Position.Z, 1);
        }
        finally
        {
            L2Levels.Folder = was;
            Directory.Delete(root, recursive: true);
        }
    }
}
