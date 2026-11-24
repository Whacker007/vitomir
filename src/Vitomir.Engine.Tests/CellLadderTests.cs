// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// <b>The cook's ladders</b> (ours): a layout's <c>traversal.ladders</c> are written
/// into the package's <c>level.json</c> as its <c>traversal</c>, and a cook whose layout has none writes the same bytes as
/// a cook before (the other traversal keys never reach the manifest).
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellLadderTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>Cooks a floor of 1 m squares and a block 4 m high with <paramref name="traversal"/>, answering the package.</summary>
    static string Cooked(L2Traversal? traversal, string root)
    {
        var v = new List<Vector3>();
        var i = new List<int>();
        var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            i.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]); mat.AddRange([m, m]);
        }
        for (int x = -4; x < 4; x++)
            for (int y = -4; y < 4; y++)
                Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
        Quad(new(1, -1, 4), new(3, -1, 4), new(3, 1, 4), new(1, 1, 4), 0);
        Quad(new(1, 1, 0), new(1, -1, 0), new(1, -1, 4), new(1, 1, 4), 1);
        var input = new CellCookInput
        {
            Name = "ladder", Vertices = [.. v], Indices = [.. i], TriangleMaterials = [.. mat],
            Materials = ["ground", "wall"], Ground = m => m == "ground", Passable = _ => false, CellSize = 0f, Nav = NavSettings.Default,
            Traversal = traversal,
        };
        string package = L2Package.PathFor(root, "ladder");
        CellCook.Cook(input, package);
        return package;
    }

    static string Root() => Path.Combine(Path.GetTempPath(), "vitomir-ladder-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_layout_s_ladders_are_in_level_json_and_a_cook_without_them_writes_the_same_bytes_as_before()
    {
        var root = Root();
        try
        {
            var layout = JsonNode.Parse("""
                { "traversal": { "noStandMeshes": ["roof"],
                                 "ladders": [ { "name": "wall", "foot": [1, 0, 0], "top": [1, 0, 4], "width": 0.5, "facing": 180 } ] } }
                """);
            var with = Cooked(L2Traversal.FromLayout(layout), Path.Combine(root, "with"));
            var m = L2Package.Open(with).Manifest;
            var ladder = Assert.Single(m.Traversal!.Ladders!);
            Assert.Equal("wall", ladder.Name);
            Assert.Equal([1f, 0f, 4f], ladder.Top);
            Assert.Equal(180f, ladder.Facing);

            // Without the key (or with only the keys the cook turns into flags): the bytes of a cook before.
            var none = Cooked(null, Path.Combine(root, "none"));
            var other = Cooked(L2Traversal.FromLayout(JsonNode.Parse("""{ "traversal": { "noStandMeshes": ["roof"] } }""")), Path.Combine(root, "other"));
            Assert.DoesNotContain("traversal", File.ReadAllText(Path.Combine(none, L2Package.ManifestFile)));
            var files = Directory.GetFiles(none, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(none, f)).Order().ToList();
            Assert.Equal(files, Directory.GetFiles(other, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(other, f)).Order());
            foreach (var f in files)
                Assert.True(File.ReadAllBytes(Path.Combine(none, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(other, f))), f);
            // With the ladder only level.json differs.
            foreach (var f in files.Where(f => f != L2Package.ManifestFile))
                Assert.True(File.ReadAllBytes(Path.Combine(none, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(with, f))), f);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
