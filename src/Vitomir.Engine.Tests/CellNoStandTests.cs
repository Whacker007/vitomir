// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// <b>The cook's no-stand marks</b> (ours): the layout's
/// <c>traversal.noStandMeshes</c> are read; an area marked <c>"stand": "none"</c> marks every triangle wholly inside its
/// box and another value is a problem; a cook with marked triangles writes them NoStand and never Walk, and the
/// navigation mesh leaves the marked top out under both bakes (Recast's own ledge filter and <c>--nav-agent</c>'s), while
/// its unmarked twin is navigable; a cook whose marks are all false writes the same bytes as one with none.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellNoStandTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>A floor of 1 m squares from −6 to 6 and a block from x 1 to 4, y −1.5 to 1.5, 0.5 m high; its top's two triangles first after the floor.</summary>
    sealed class Scene
    {
        public readonly List<Vector3> V = [];
        public readonly List<int> I = [];
        public readonly List<int> Mat = [];
        public int TopFirst;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = V.Count;
            V.AddRange([a * M, b * M, c * M, d * M]);
            I.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]); Mat.AddRange([m, m]);
        }

        public Scene()
        {
            for (int x = -6; x < 6; x++)
                for (int y = -6; y < 6; y++)
                    Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
            const float x0 = 1, x1 = 4, y0 = -1.5f, y1 = 1.5f, z1 = 0.5f;
            TopFirst = I.Count / 3;
            Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), 0);
            Quad(new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, z1), new(x0, y0, z1), 1);
            Quad(new(x0, y1, 0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, 0), 1);
            Quad(new(x0, y1, 0), new(x0, y0, 0), new(x0, y0, z1), new(x0, y1, z1), 1);
            Quad(new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, z1), new(x1, y0, z1), 1);
        }

        public int Triangles => I.Count / 3;

        public bool[] TopMarked()
        {
            var marks = new bool[Triangles];
            marks[TopFirst] = marks[TopFirst + 1] = true;
            return marks;
        }
    }

    /// <summary>Cooks the scene with the marks given, and reads back the surfaces and whether a navigation polygon lies on the top.</summary>
    static (L2Surface[] Surfaces, bool OnTop, string Folder) Cooked(Scene s, bool[]? noStand, NavSettings nav, string root)
    {
        var input = new CellCookInput
        {
            Name = "nostand", Vertices = [.. s.V], Indices = [.. s.I], TriangleMaterials = [.. s.Mat],
            Materials = ["ground", "wall"], Ground = m => m == "ground", Passable = _ => false, CellSize = 0f, Nav = nav,
            TriangleNoStand = noStand,
        };
        string package = L2Package.PathFor(root, "nostand");
        CellCook.Cook(input, package);
        using var stream = new CellStream(L2Package.Open(package), new CellStreamOptions { Everything = true });
        stream.LoadNow(Vector3.Zero);
        var half = new DotRecast.Core.Numerics.RcVec3f(0.4f, 0.15f, 0.4f);
        var status = stream.Query.FindNearestPoly(CellCook.ToRecast(new Vector3(2.5f, 0f, 0.5f)), half, new DtQueryDefaultFilter(), out long poly, out _, out _);
        return ([.. stream.Collision.Surfaces], !status.Failed() && poly != 0, package);
    }

    static string Root() => Path.Combine(Path.GetTempPath(), "vitomir-nostand-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Marked_triangles_are_no_stand_and_never_walk_and_the_navigation_mesh_leaves_the_top_out_under_both_bakes()
    {
        var root = Root();
        try
        {
            foreach (var nav in new[] { NavSettings.Default, NavSettings.Human with { StairLedges = true } })
            {
                var plain = Cooked(new Scene(), null, nav, Path.Combine(root, "plain"));
                Assert.True(plain.OnTop, $"unmarked ({nav}): a polygon on the top");
                Assert.DoesNotContain(plain.Surfaces, x => (x & L2Surface.NoStand) != 0);

                var marked = Cooked(new Scene(), new Scene().TopMarked(), nav, Path.Combine(root, "marked"));
                Assert.False(marked.OnTop, $"marked ({nav}): no polygon on the top");
                var flagged = marked.Surfaces.Where(x => (x & L2Surface.NoStand) != 0).ToList();
                Assert.Equal(2, flagged.Count);
                Assert.All(flagged, x => Assert.Equal(L2Surface.Solid | L2Surface.NoStand, x));
                Assert.Equal(plain.Surfaces.Count(x => (x & L2Surface.Walk) != 0) - 2, marked.Surfaces.Count(x => (x & L2Surface.Walk) != 0));
                Directory.Delete(Path.Combine(root, "plain"), recursive: true);
                Directory.Delete(Path.Combine(root, "marked"), recursive: true);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_cook_whose_marks_are_all_false_writes_the_same_bytes_as_one_with_none()
    {
        var root = Root();
        try
        {
            var none = Cooked(new Scene(), null, NavSettings.Human with { StairLedges = true }, Path.Combine(root, "none"));
            var unset = Cooked(new Scene(), new bool[new Scene().Triangles], NavSettings.Human with { StairLedges = true }, Path.Combine(root, "unset"));
            var files = Directory.GetFiles(none.Folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(none.Folder, f)).Order().ToList();
            Assert.Equal(files, Directory.GetFiles(unset.Folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(unset.Folder, f)).Order());
            foreach (var f in files)
                Assert.True(File.ReadAllBytes(Path.Combine(none.Folder, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(unset.Folder, f))), f);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_area_marked_stand_none_marks_the_triangles_wholly_inside_its_box_and_another_value_is_a_problem()
    {
        var layout = JsonNode.Parse("""
            { "areas": [
                { "name": "roof", "kind": "zone", "stand": "none", "box": { "origin": [0.9, -1.6, 0.3], "rotZ": 0, "size": [3.2, 3.2, 0.5] } },
                { "name": "cart", "kind": "zone", "stand": "no", "box": { "origin": [-5, -5, 0], "rotZ": 0, "size": [1, 1, 1] } },
                { "name": "room", "kind": "interior", "box": { "origin": [-5, 3, 0], "rotZ": 0, "size": [2, 2, 2] } } ],
              "traversal": { "noStandMeshes": [ "roof_7", "cart_top" ] } }
            """);
        Assert.Equal(["roof_7", "cart_top"], CellCook.NoStandMeshes(layout));
        Assert.Empty(CellCook.NoStandMeshes(JsonNode.Parse("""{ "traversal": { "noClimbMeshes": [ "x" ] } }""")));
        var problems = new List<string>();
        var areas = CellCook.NoStandAreas(L2Area.FromLayout(layout), problems);
        Assert.Equal(["roof"], areas.Select(a => a.Name));
        Assert.Single(problems);
        Assert.Contains("cart", problems[0]);

        var s = new Scene();
        var marks = new bool[s.Triangles];
        var held = CellCook.MarkNoStandAreas(areas, s.V, s.I, M, marks);
        // The box from 0.3 m to 0.8 m holds the top (at 0.5 m) and none of the sides (down to the floor) or the floor.
        Assert.Equal([2], held);
        Assert.Equal(new[] { s.TopFirst, s.TopFirst + 1 }, Enumerable.Range(0, marks.Length).Where(t => marks[t]));
        Assert.Empty(CellCook.NoStandAreas(null, problems));
    }
}
