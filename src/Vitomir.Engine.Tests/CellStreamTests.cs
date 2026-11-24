// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Streaming (ours, Phase 3): a generated level cut into cells of
/// 15 m — no collision triangle lost or doubled, a query by a border finding the neighbour's triangles,
/// a route across cells over the tiled navigation mesh, cells taken in and let go at the top of a frame,
/// Christof walking across several cells as they stream; the streamers' decisions as pure logic; the
/// simplifier's second level of detail and far versions.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellStreamTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    const float Cell = 15f;   // 100 navigation voxels of 0.15 m

    /// <summary>
    /// A 60 m × 30 m floor of 1 m squares (material 0, "ground"), from (0, 0); a wall 3 m high along
    /// y = 20 from x 12 to 18, across the border at x = 15 (material 1, "wall", both faces); and one
    /// triangle 2 km wide far below (material 2, "far"), which belongs to no cell. Engine units.
    /// </summary>
    static (List<Vector3> V, List<int> I, List<int> Mat) Scene()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            mat.AddRange([m, m]);
        }
        for (int x = 0; x < 60; x++)
            for (int y = 0; y < 30; y++)
                Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
        Quad(new(12, 20, 0), new(18, 20, 0), new(18, 20, 3), new(12, 20, 3), 1);
        Quad(new(18, 20, 0), new(12, 20, 0), new(12, 20, 3), new(18, 20, 3), 1);
        int far = v.Count;
        v.AddRange([new Vector3(-1000, -1000, -50) * M, new Vector3(1000, -1000, -50) * M, new Vector3(0, 1000, -50) * M]);
        idx.AddRange([far, far + 1, far + 2]); mat.Add(2);
        return (v, idx, mat);
    }

    /// <summary>Render meshes for the same floor (metres), one per 10 m strip, so the cells have MESH buffers.</summary>
    static List<CookMesh> Meshes()
    {
        var list = new List<CookMesh>();
        for (int strip = 0; strip < 6; strip++)
        {
            var verts = new List<L2Vertex>(); var idx = new List<uint>();
            for (int x = strip * 10; x < strip * 10 + 10; x++)
                for (int y = 0; y < 30; y++)
                {
                    uint at = (uint)verts.Count;
                    foreach (var (px, py) in new[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) })
                        verts.Add(new L2Vertex { Position = new(px, py, 0), Normal = Vector3.UnitZ, Tangent = new(1, 0, 0, 1), Uv0 = new(px, py), Color = 0xffffffff });
                    idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
                }
            list.Add(new CookMesh($"floor{strip}", 0, [.. verts], [.. idx]));
        }
        return list;
    }

    static (string Root, string Package, CellCookResult Result) Cook(string name = "streamtest")
    {
        var (v, idx, mat) = Scene();
        var root = Path.Combine(Path.GetTempPath(), "vitomir-streamtest-" + Guid.NewGuid().ToString("N"));
        var package = L2Package.PathFor(root, name);
        var result = CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall", "far"],
            Ground = m => m == "ground", Passable = _ => false, Meshes = Meshes(), CellSize = Cell, BlockCells = 2,
            Anchors = [new L2Anchor { Id = $"{name}/starts/0", Kind = "start", Scene = name, Name = "playerstart00",
                                      Position = [5, 7, 0], Rotation = [0, 0, 0], Group = 0 }],
        }, package);
        return (root, package, result);
    }

    [Fact]
    public void Cutting_a_level_into_cells_loses_no_collision_triangle_and_doubles_none()
    {
        var (root, package, result) = Cook();
        try
        {
            var manifest = L2Package.Open(package).Manifest;
            Assert.Equal(L2Manifest.Version2, manifest.Version);
            Assert.Equal(Cell, manifest.Grid!.CellSize, 3);
            Assert.Equal((4, 2), (manifest.Grid.Columns, manifest.Grid.Rows));
            Assert.Equal(8, result.Cells);
            var resident = Assert.Single(manifest.Cells, c => c.Resident == true);
            Assert.Equal(CellCook.ResidentId, resident.Id);

            // Every triangle of every cell, as its three corners rounded to a millimetre, against the scene's.
            var (v, idx, _) = Scene();
            static string Key(Vector3 a, Vector3 b, Vector3 c) =>
                string.Join(';', new[] { a, b, c }.Select(p => $"{MathF.Round(p.X * 1000)},{MathF.Round(p.Y * 1000)},{MathF.Round(p.Z * 1000)}").Order());
            var expected = new List<string>();
            for (int t = 0; t < idx.Count / 3; t++) expected.Add(Key(v[idx[3 * t]] / M, v[idx[3 * t + 1]] / M, v[idx[3 * t + 2]] / M));
            var found = new List<string>();
            foreach (var c in manifest.Cells)
            {
                var cell = L2Cell.ReadFile(Path.Combine(package, c.File), t => t == L2Collision.Tag);
                if (cell.Find(L2Collision.Tag) is not { } chunk) continue;
                var coll = L2Collision.Decode(chunk);
                for (int t = 0; t < coll.Triangles; t++)
                    found.Add(Key(coll.Vertices[coll.Indices[3 * t]], coll.Vertices[coll.Indices[3 * t + 1]], coll.Vertices[coll.Indices[3 * t + 2]]));
            }
            Assert.Equal(expected.Count, found.Count);
            Assert.Equal(expected.Order(), found.Order());
            // The 2 km triangle is the resident cell's, not a gridded cell's.
            var residentColl = L2Collision.Decode(L2Cell.ReadFile(Path.Combine(package, resident.File), t => t == L2Collision.Tag).Find(L2Collision.Tag)!);
            Assert.Equal(1, residentColl.Triangles);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_query_by_a_cell_s_border_finds_the_neighbour_s_triangles()
    {
        var (root, package, _) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { Everything = true });
            Assert.Equal(level.Stream.Cells.Count, level.Stream.Loaded.Count);
            // The wall across x = 15: its two faces' triangles have their middles in the cells either side.
            var ray = level.Collision.Ray(new Vector3(14.9f, 10, 1.5f) * M, Vector3.UnitY, 20 * M);
            Assert.NotNull(ray);
            Assert.Equal(10 * M, ray!.Value.Distance, 1);
            var ray2 = level.Collision.Ray(new Vector3(15.1f, 10, 1.5f) * M, Vector3.UnitY, 20 * M);
            Assert.Equal(10 * M, ray2!.Value.Distance, 1);
            // The floor right on the border, and a body standing by the wall on the far side of the border.
            Assert.Equal(0f, level.Collision.Floor(15 * M, 7 * M, 5f, 40f)!.Value, 2);
            Assert.False(level.Collision.Open(new Vector3(15.05f, 19.9f, 0) * M, 0.25f * M, 0.4f * M, 1.7f * M));
            // The resident triangle under everything.
            Assert.NotNull(level.Collision.Ray(new Vector3(200, 200, 0) * M, -Vector3.UnitZ, 100 * M));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_route_crosses_cells_on_the_tiled_navigation_mesh()
    {
        var (root, package, _) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { Everything = true });
            var walk = new WalkMap(level.Shell);
            var from = new Vector3(3, 3, 0) * M; var to = new Vector3(56, 26, 0) * M;
            int a = walk.Find(from), b = walk.Find(to);
            Assert.True(a >= 0 && b >= 0, "no ground at the route's ends");
            Assert.NotEqual(level.Stream.CellOfSlot(a), level.Stream.CellOfSlot(b));
            Assert.Equal(a, level.Stream.NodeUnder(from, CellLevel.FloorWindow));
            var result = level.Routes!.Find(from, a, to, b, out var path);
            Assert.NotEqual(PathResult.None, result);
            // Round the wall's end, not through it: from below the wall to above it.
            int c = walk.Find(new Vector3(15, 17, 0) * M), d = walk.Find(new Vector3(15, 23, 0) * M);
            Assert.Equal(PathResult.Path, level.Routes.Find(new Vector3(15, 17, 0) * M, c, new Vector3(15, 23, 0) * M, d, out var round));
            Assert.All(round, w => Assert.True(w.Point.X < 12 * M || w.Point.X > 18 * M, $"a corner at x {w.Point.X / M} m, within the wall's span"));
            // The border polygons are joined across it: a walk node on each side of x = 15 lists the other.
            int left = walk.Find(new Vector3(14.8f, 7, 0) * M), right = walk.Find(new Vector3(15.2f, 7, 0) * M);
            Assert.NotEqual(level.Stream.CellOfSlot(left), level.Stream.CellOfSlot(right));
            Assert.Contains(right, Reachable(walk, left));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
    public void Cells_come_in_and_go_at_the_top_of_a_frame_and_the_ground_with_them()
    {
        var (root, package, _) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { SimRadius = 5, Hysteresis = 3, Wait = true });
            var walk = new WalkMap(level.Shell);
            var start = new Vector3(5, 7, 0) * M;
            Assert.True(walk.Find(start) >= 0, "no ground at the start");
            Assert.True(walk.Find(new Vector3(55, 7, 0) * M) < 0, "ground far off before it is loaded");
            int before = level.Stream.Loaded.Count;
            level.Stream.Update(new Vector3(55, 7, 0) * M);
            Assert.True(walk.Find(new Vector3(55, 7, 0) * M) >= 0, "no ground where the focus went");
            Assert.True(walk.Find(start) < 0, "the start's ground was kept after its cell went");
            Assert.True(level.Stream.UnloadCount >= 1 && level.Stream.LoadCount > before);
            Assert.Null(level.Collision.Floor(5 * M, 7 * M, 0, 40f));                 // its collision went with it
            Assert.NotNull(level.Collision.Floor(55 * M, 7 * M, 0, 40f));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Christof_walks_across_three_cell_borders_as_the_cells_stream()
    {
        using var files = Install.Open();
        var (root, _, _) = Cook("streamtestplay");
        var was = L2Levels.Folder;
        var env = Environment.GetEnvironmentVariable("VITOMIR_SIM_RADIUS");
        try
        {
            L2Levels.Folder = root;
            Environment.SetEnvironmentVariable("VITOMIR_SIM_RADIUS", "12");
            var place = Location.Load(files, "streamtestplay", player: "Christof");
            var cells = CellLevel.Of(place.Level)!;
            cells.Stream.Options.Wait = true;
            cells.Stream.Options.Hysteresis = 3;
            Assert.True(cells.Stream.Loaded.Count < cells.Stream.Cells.Count, "every cell loaded at once");
            var player = place.Player!;
            var sim = new Simulation(place);
            var crossed = new HashSet<int>();
            float lowest = 0;
            for (int i = 0; i < 60 * 40 && player.Position.X < 52 * M; i++)
            {
                player.Move(Vector2.UnitX, 1f / 60f);
                sim.Step(1f / 60f);
                crossed.Add(cells.Stream.CellOfSlot(player.Node));
                lowest = MathF.Min(lowest, player.Position.Z);
            }
            Assert.True(player.Position.X >= 52 * M, $"Christof stopped at x {player.Position.X / M} m");
            Assert.True(player.Node >= 0, "Christof is on no ground");
            Assert.Equal(0f, lowest, 1);
            Assert.True(crossed.Count(c => c >= 0) >= 4, $"only {crossed.Count} cells crossed");
            Assert.True(cells.Stream.UnloadCount > 0, "no cell was let go behind him");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VITOMIR_SIM_RADIUS", env);
            L2Levels.Folder = was;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_plan_wants_near_cells_keeps_them_within_the_hysteresis_and_lets_far_ones_go()
    {
        var cells = new List<CellStreamPlan.Cell>
        {
            new(new(0, 0), new(10, 10)), new(new(10, 0), new(20, 10)), new(new(20, 0), new(30, 10)), new(new(30, 0), new(40, 10)),
            new(new(-1000, -1000), new(1000, 1000), Resident: true),
        };
        var none = new HashSet<int>();
        var near = CellStreamPlan.Wanted(new(5, 5), cells, none, 6, 4).ToHashSet();
        Assert.Equal(new HashSet<int> { 0, 1, 4 }, near);                         // within 6 m: its own cell and the next (5 m off); the resident always
        // Walked to x = 13: cell 2 is 7 m off — not wanted anew, but kept if it was in (within 6 + 4).
        Assert.DoesNotContain(2, CellStreamPlan.Wanted(new(13, 5), cells, near, 6, 4));
        Assert.Contains(2, CellStreamPlan.Wanted(new(13, 5), cells, new HashSet<int> { 2 }, 6, 4));
        Assert.DoesNotContain(2, CellStreamPlan.Wanted(new(9, 5), cells, new HashSet<int> { 2 }, 6, 4)); // 11 m off: let go
        var (load, unload) = CellStreamPlan.Changes([1, 2, 4], new HashSet<int> { 0, 1, 4 });
        Assert.Equal([2], load);
        Assert.Equal([0], unload);
        Assert.Equal(0f, CellStreamPlan.Distance(new(5, 5), new(0, 0), new(10, 10)));
        Assert.Equal(5f, CellStreamPlan.Distance(new(13, 14), new(0, 0), new(10, 10)), 4);
    }

    [Fact]
    public void A_level_of_detail_keeps_its_band_within_the_hysteresis()
    {
        float[] from = [0, 90, 200];
        Assert.Equal(0, CellStreamPlan.Lod(50, -1, from, 10));
        Assert.Equal(1, CellStreamPlan.Lod(95, -1, from, 10));
        Assert.Equal(0, CellStreamPlan.Lod(95, 0, from, 10));     // drawn at 0 and only 5 m past: kept
        Assert.Equal(1, CellStreamPlan.Lod(101, 0, from, 10));    // 11 m past: changed
        Assert.Equal(1, CellStreamPlan.Lod(85, 1, from, 10));     // back 5 m inside: kept
        Assert.Equal(0, CellStreamPlan.Lod(79, 1, from, 10));
        Assert.Equal(2, CellStreamPlan.Lod(500, 1, from, 10));
    }

    [Fact]
    public void The_second_level_of_detail_halves_a_rough_ground_and_keeps_its_open_edge()
    {
        // A 30 × 30 grid, 0.5 m squares, gently rough (2 cm), one vertex per corner.
        var verts = new List<L2Vertex>(); var idx = new List<uint>();
        const int N = 30;
        for (int y = 0; y <= N; y++)
            for (int x = 0; x <= N; x++)
                verts.Add(new L2Vertex { Position = new(x * 0.5f, y * 0.5f, 0.02f * MathF.Sin(x * 1.7f) * MathF.Cos(y * 2.3f)), Normal = Vector3.UnitZ, Uv0 = new(x, y) });
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                uint a = (uint)(y * (N + 1) + x);
                idx.AddRange([a, a + 1, a + (N + 1) + 1, a, a + (N + 1) + 1, a + (N + 1)]);
            }
        var v = verts.ToArray();
        var reduced = MeshSimplify.Simplify(v, [.. idx], 0.35f, 0.25f, out float error);
        Assert.True(reduced.Length / 3 <= idx.Count / 3 / 2, $"only down to {reduced.Length / 3} of {idx.Count / 3}");
        Assert.True(error <= 0.25f);
        // Every corner of the square is still used, and nothing on the edge has moved off it.
        var used = reduced.Select(i => v[i].Position).ToHashSet();
        foreach (var corner in new[] { new Vector2(0, 0), new Vector2(15, 0), new Vector2(0, 15), new Vector2(15, 15) })
            Assert.Contains(used, p => new Vector2(p.X, p.Y) == corner);
        // Pinned to the cut, the edge keeps every vertex it had.
        var pinned = MeshSimplify.Simplify(v, [.. idx], 0.35f, 0.25f, out _, p => p.X < 0.01f || p.X > 14.99f || p.Y < 0.01f || p.Y > 14.99f);
        var pinnedUsed = pinned.Select(i => v[i].Position).ToHashSet();
        Assert.Equal(4 * N, v.Count(p => (p.Position.X == 0 || p.Position.X == 15 || p.Position.Y == 0 || p.Position.Y == 15) && pinnedUsed.Contains(p.Position)));

        // Clustered at 2 m, the far version is a couple of hundred triangles and none folded flat.
        var (cv, ci) = MeshSimplify.Cluster(v, [.. idx], 2f);
        Assert.True(ci.Length / 3 < 200 && ci.Length > 0, $"{ci.Length / 3} triangles clustered");
        for (int t = 0; t < ci.Length / 3; t++) Assert.True(ci[3 * t] != ci[3 * t + 1] && ci[3 * t + 1] != ci[3 * t + 2] && ci[3 * t] != ci[3 * t + 2]);
        Assert.True(cv.Length < v.Length / 4);
    }

    [Fact]
    public void A_cooked_cell_carries_two_levels_of_detail_and_its_block_a_far_version()
    {
        var (root, package, result) = Cook();
        try
        {
            var manifest = L2Package.Open(package).Manifest;
            Assert.Equal(2, manifest.Far!.Count);                                      // 4 × 2 cells in blocks of 2 × 2
            var entry = manifest.Cells.First(c => c.Id == L2Grid.CellId(1, 0));
            Assert.Equal("far/f0_0.cell", entry.FarFile);
            var mesh = L2MeshBuffers.Decode(L2Cell.ReadFile(Path.Combine(package, entry.File), t => t == L2Mesh.Tag).Find(L2Mesh.Tag)!);
            Assert.Equal(2, mesh.Lods.Count);
            Assert.Equal(15 * 15 * 2, mesh.Lods[0].Triangles);                          // the cell's own 15 m × 15 m of floor
            Assert.True(mesh.Lods[1].Triangles < mesh.Lods[0].Triangles);
            Assert.All(mesh.Lods[0].Draws, d => Assert.Equal(1f, d.UvDensity, 3));     // a texture repeat a metre
            Assert.True(result.FarTriangles <= result.Lod0Triangles);
            var far = L2MeshBuffers.Decode(L2Cell.ReadFile(Path.Combine(package, "far/f0_0.cell"), _ => true).Find(L2Mesh.Tag)!);
            Assert.Single(far.Lods);
            Assert.True(far.Lods[0].Triangles > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
