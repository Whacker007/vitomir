// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The skyline (ours): the city round a cell level, cooked into parts by
/// tile — small near the level, large far off — each a cell file with one <c>MESH</c> and nothing that
/// collides or is walked; its entry in the manifest kept through a write and a read; the level meshes it
/// replaces read from the generator's note; a tile's near and far versions from what the source names for
/// each; and the city plan the levels generator reads.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellSkylineTests
{
    /// <summary>A flat square of <paramref name="size"/> metres about (x, y) at height z, two triangles, one material.</summary>
    static CookMesh Square(string name, int material, float x, float y, float z, float size)
    {
        float h = size / 2;
        var v = new[] { new Vector3(x - h, y - h, z), new Vector3(x + h, y - h, z), new Vector3(x + h, y + h, z), new Vector3(x - h, y + h, z) }
            .Select(p => new L2Vertex { Position = p, Normal = Vector3.UnitZ, Tangent = new(1, 0, 0, 1), Uv0 = new(p.X, p.Y), Color = 0xffffffff }).ToArray();
        return new CookMesh(name, material, v, [0, 1, 2, 0, 2, 3]);
    }

    [Fact]
    public void A_triangle_goes_to_a_small_tile_near_the_level_and_a_larger_one_farther_off()
    {
        var centre = new Vector2(10, 20);
        Assert.Equal((CellSkyline.NearTile, 0, 0), CellSkyline.Tile(new Vector2(110, 120), centre));
        Assert.Equal((CellSkyline.NearTile, -1, 3), CellSkyline.Tile(new Vector2(-300, 1600), centre));
        Assert.Equal((CellSkyline.FarTile, 1, 0), CellSkyline.Tile(new Vector2(3010, 20), centre));        // 3 km along X: past the near tiles
        Assert.Equal((CellSkyline.FarTile, -1, -2), CellSkyline.Tile(new Vector2(-200, -2990), centre));  // either axis counts
        Assert.Equal((CellSkyline.HorizonTile, -2, 0), CellSkyline.Tile(new Vector2(-9990, 520), centre));
    }

    [Fact]
    public void Cooking_a_skyline_writes_a_part_per_tile_that_draws_and_never_collides_and_the_manifest_keeps_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-skyline-" + Guid.NewGuid().ToString("N"));
        var package = L2Package.PathFor(root, "skytest");
        try
        {
            // The level: a 20 m floor of one material, cooked as one cell.
            var floor = Square("floor", 0, 10, 10, 0, 20);
            const float M = L2Manifest.EngineUnitsPerMetre;
            CellCook.Cook(new CellCookInput
            {
                Name = "skytest", Vertices = [.. floor.Vertices.Select(v => v.Position * M)], Indices = [0, 1, 2, 0, 2, 3], TriangleMaterials = [0, 0],
                Materials = ["ground", "city_land", "water_river"], Ground = m => m == "ground", Passable = _ => false, Meshes = [floor], CellSize = 0,
            }, package);
            var collisionBefore = Directory.GetFiles(Path.Combine(package, L2Package.CellsFolder)).Sum(f => L2Cell.Read(File.ReadAllBytes(f)).Find(L2Collision.Tag) is { } c ? L2Collision.Decode(c).Triangles : 0);

            // The skyline: land beside the level, a river a little farther, a hill 3 km off and the horizon 11 km off.
            var glb = Path.Combine(root, "city.glb");
            File.WriteAllBytes(glb, [1, 2, 3]);
            var sky = new[]
            {
                Square("land", 1, 120, 30, -0.2f, 60), Square("river", 2, 160, 300, -5, 40), Square("land", 1, 3100, 1000, 40, 200), Square("land", 1, -11000, 3000, 90, 800),
            };
            var entry = CellSkyline.Cook(sky, new Vector2(10, 10), glb, 1, 2, ["fields"], package);

            Assert.Equal(3, entry.Parts.Count);                                          // the two near squares share a 500 m tile; whole triangles, by their middles
            Assert.Equal([CellSkyline.NearTile, CellSkyline.FarTile, CellSkyline.HorizonTile], entry.Parts.Select(p => p.Tile).ToArray());
            Assert.Equal(8, entry.Triangles);
            Assert.Equal(-11400, entry.Bounds.Min[0], 1);
            Assert.Equal((1, 2), (entry.FirstMaterial, entry.MaterialCount));
            Assert.True(File.Exists(Path.Combine(package, entry.Asset)));
            var near = L2Cell.Read(File.ReadAllBytes(Path.Combine(package, entry.Parts[0].File)));
            Assert.Equal([L2Mesh.Tag], near.Chunks.Select(c => c.Tag).ToArray());       // drawn only: no COLL, no NAVM
            var lod = Assert.Single(L2MeshBuffers.Decode(near.Find(L2Mesh.Tag)!).Lods);
            Assert.Equal([1, 2], lod.Draws.Select(d => d.Material).ToArray());           // a draw per material, each with its own box
            Assert.Equal(new Vector3(90, 0, -0.2f), lod.Draws[0].Min);
            Assert.Equal(new Vector3(180, 320, -5), lod.Draws[1].Max);

            // Through the manifest and back; the level's own collision is what it was.
            var manifest = L2Package.Open(package).Manifest;
            manifest.Skyline = entry;
            var back = L2Manifest.Read(manifest.Write()).Skyline!;
            Assert.Equal(entry.Parts.Select(p => p.File), back.Parts.Select(p => p.File));
            Assert.Equal(["fields"], back.Replaces);
            Assert.DoesNotContain(L2Package.Open(package).Manifest.Cells, c => c.File.StartsWith(CellSkyline.Folder));
            var collisionAfter = Directory.GetFiles(Path.Combine(package, L2Package.CellsFolder)).Sum(f => L2Cell.Read(File.ReadAllBytes(f)).Find(L2Collision.Tag) is { } c ? L2Collision.Decode(c).Triangles : 0);
            Assert.Equal(2, collisionBefore);
            Assert.Equal(collisionBefore, collisionAfter);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_mesh_s_name_keeps_it_to_the_near_or_the_far_version_or_puts_it_in_both()
    {
        Assert.Equal(0, CellSkyline.Version("houses the Old Town" + CellSkyline.NearSuffix));
        Assert.Equal(1, CellSkyline.Version("woods" + CellSkyline.FarSuffix + "#3"));     // the glTF reader's primitive number is not the name
        Assert.Equal(-1, CellSkyline.Version("terrain#0"));
        Assert.Equal(-1, CellSkyline.Version("a (near) house"));                         // only the name's end counts
        Assert.Equal(-1, CellSkyline.Version(null));
    }

    [Fact]
    public void A_tile_with_near_and_far_meshes_gets_two_versions_each_leaving_out_the_other_s_and_a_tile_without_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-skylods-" + Guid.NewGuid().ToString("N"));
        var package = L2Package.PathFor(root, "skylods");
        try
        {
            Directory.CreateDirectory(package);
            var glb = Path.Combine(root, "city.glb");
            File.WriteAllBytes(glb, [1, 2, 3]);
            var sky = new[]
            {
                Square("terrain", 1, 100, 100, 0, 40),                                       // both versions
                Square("houses" + CellSkyline.NearSuffix, 2, 110, 90, 6, 10),                // near only: textured houses
                Square("houses" + CellSkyline.NearSuffix, 2, 130, 90, 6, 10),
                Square("houses" + CellSkyline.FarSuffix, 1, 120, 90, 6, 30),                 // far only: their row as one mass
                Square("terrain", 1, 3100, 1000, 40, 200),                                   // a far tile with nothing named: one version
            };
            var entry = CellSkyline.Cook(sky, new Vector2(10, 10), glb, 1, 2, [], package, farFrom: 700f);

            Assert.Equal(700f, entry.FarFrom);
            Assert.Equal(2, entry.Parts.Count);
            var near = L2MeshBuffers.Decode(L2Cell.Read(File.ReadAllBytes(Path.Combine(package, entry.Parts[0].File))).Find(L2Mesh.Tag)!);
            Assert.Equal(2, near.Lods.Count);
            Assert.Equal([1, 2], near.Lods[0].Draws.Select(d => d.Material).ToArray());   // near: the land and the houses, each its draw
            Assert.Equal(6, near.Lods[0].Triangles);
            Assert.Equal([1], near.Lods[1].Draws.Select(d => d.Material).ToArray());      // far: the land and the mass, one material, one draw
            Assert.Equal(4, near.Lods[1].Triangles);
            Assert.Equal((6, 4), (entry.Parts[0].Triangles, entry.Parts[0].FarTriangles));
            Assert.Equal((new Vector3(80, 75, 0), new Vector3(135, 120, 6)), (near.Lods[1].Draws[0].Min, near.Lods[1].Draws[0].Max));
            Assert.Equal(135f, entry.Parts[0].Bounds.Max[0], 3);                            // the part's box holds both versions
            var far = L2MeshBuffers.Decode(L2Cell.Read(File.ReadAllBytes(Path.Combine(package, entry.Parts[1].File))).Find(L2Mesh.Tag)!);
            Assert.Single(far.Lods);
            Assert.Equal((2, 2), (entry.Parts[1].Triangles, entry.Parts[1].FarTriangles));
            Assert.Equal((8, 6), (entry.Triangles, entry.FarTriangles));

            var back = L2Manifest.Read(new L2Manifest { Name = "x", Skyline = entry }.Write()).Skyline!;
            Assert.Equal((700f, 6L), (back.FarFrom, back.FarTriangles));
            Assert.Equal(4, back.Parts[0].FarTriangles);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_meshes_a_skyline_replaces_are_read_from_the_note_beside_it_and_none_without_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-skynote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var glb = Path.Combine(root, "city.glb");
            Assert.Empty(CellSkyline.Replaces(glb));
            File.WriteAllText(Path.Combine(root, "city.json"), """{"replaces": ["fields", "horizon"], "triangles": 5}""");
            Assert.Equal(["fields", "horizon"], CellSkyline.Replaces(glb));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// The city plan a skyline is built from, a file of the levels' own tools: named by <c>VITOMIR_CITY_PLAN</c>, and the
    /// test skips without it.
    /// </summary>
    [Fact]
    public void The_city_plan_is_in_the_Old_Town_s_frame_its_river_passes_the_bridge_gate_and_its_districts_close()
    {
        var file = Environment.GetEnvironmentVariable("VITOMIR_CITY_PLAN");
        Assert.SkipUnless(file is { Length: > 0 } && File.Exists(file), "VITOMIR_CITY_PLAN names no city plan");
        var plan = JsonNode.Parse(File.ReadAllText(file!))!;

        Assert.Equal("north", (string?)plan["frame"]!["x"]);
        Assert.Equal("west", (string?)plan["frame"]!["y"]);
        // The river: points of (x, y, width), north along x past the bridge, its east bank at the bridge where the plan says.
        var river = plan["river"]!["centre"]!.AsArray().Select(p => p!.AsArray().Select(v => (float)v!).ToArray()).ToList();
        Assert.All(river, p => Assert.Equal(3, p.Length));
        float bridgeX = (float)plan["bridge"]!["x"]!;
        int k = river.FindIndex(p => p[0] >= bridgeX);
        Assert.True(k > 0, "the river reaches past the bridge");
        float t = (bridgeX - river[k - 1][0]) / (river[k][0] - river[k - 1][0]);
        float centre = river[k - 1][1] + t * (river[k][1] - river[k - 1][1]), width = river[k - 1][2] + t * (river[k][2] - river[k - 1][2]);
        Assert.Equal((float)plan["river"]!["eastBankAtBridge"]!, centre - width / 2, 1);
        Assert.True((float)plan["bridge"]!["endY"]! > centre + width / 2, "the bridge reaches the west bank");
        Assert.True((float)plan["river"]!["water"]! < 0, "the water lies below the Old Town's ground");
        // Every district and every wall that closes has a polygon of three corners or more; a wall drawn round
        // another (the castle's outer wall) names one there is.
        Assert.All(plan["districts"]!.AsArray(), d => Assert.True(d!["polygon"]!.AsArray().Count >= 3));
        var wallNames = plan["walls"]!.AsArray().Select(w => (string?)w!["name"]).ToHashSet();
        Assert.All(plan["walls"]!.AsArray(), w => Assert.True(w!["line"]?.AsArray().Count >= 2 || w["ellipse"] is not null
                                                              || wallNames.Contains((string?)w["offsetOf"])));
        Assert.Contains(plan["landmarks"]!.AsArray(), l => (string?)l!["kind"] == "cathedral");
    }
}
