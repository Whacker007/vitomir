// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Foliage in the cook (ours): a tree's
/// leaf cards and a patch of grass cooked into a cell, its second level of detail and its block's far
/// version. The cards are kept or dropped whole, never collapsed, and the crown covers as much of the
/// view at a distance as it does near, from the side and from above.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellFoliageTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    const int Ground = 0, Bark = 1, Leaves = 2, Grass = 3;
    static readonly Vector3 CrownCentre = new(24, 24, 5.5f);
    static readonly Vector3 CrownRadii = new(3.2f, 3.2f, 2.6f);

    /// <summary>A quad card: corners a, b (bottom), c, d (top), sway <paramref name="sway0"/> at the bottom and <paramref name="sway1"/> at the top.</summary>
    static void Card(List<L2Vertex> v, List<uint> idx, Vector3 bottom, Vector3 right, Vector3 up, Vector3 normal, float sway0, float sway1, uint tint)
    {
        uint n = (uint)v.Count;
        uint C(float sway) => (tint & 0x00ffffffu) | ((uint)Math.Clamp(MathF.Round(sway * 255f), 0, 255) << 24);
        var t = new Vector4(Vector3.Normalize(right), 1f);
        v.Add(new L2Vertex { Position = bottom - right, Normal = normal, Tangent = t, Uv0 = new(0, 1), Color = C(sway0) });
        v.Add(new L2Vertex { Position = bottom + right, Normal = normal, Tangent = t, Uv0 = new(1, 1), Color = C(sway0) });
        v.Add(new L2Vertex { Position = bottom - right + up, Normal = normal, Tangent = t, Uv0 = new(0, 0), Color = C(sway1) });
        v.Add(new L2Vertex { Position = bottom + right + up, Normal = normal, Tangent = t, Uv0 = new(1, 0), Color = C(sway1) });
        idx.AddRange([n, n + 1, n + 3, n, n + 3, n + 2]);
    }

    /// <summary>
    /// The scene's render meshes (metres): a 48 m square of ground (4 m squares), a trunk, a crown of 1,400
    /// leaf cards 0.6 m across (most in its outer shell, each facing out of it and up, as the town's trees'
    /// are), and 1,500 grass cards rooted on a 10 m patch (sway 0 at the root).
    /// </summary>
    static List<CookMesh> Meshes()
    {
        var rng = new Random(9);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var meshes = new List<CookMesh>();

        var gv = new List<L2Vertex>(); var gi = new List<uint>();
        for (int y = 0; y <= 12; y++)
            for (int x = 0; x <= 12; x++) gv.Add(new L2Vertex { Position = new(x * 4, y * 4, 0), Normal = Vector3.UnitZ, Tangent = new(1, 0, 0, 1), Uv0 = new(x, y), Color = 0xffffffff });
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++) { uint a = (uint)(y * 13 + x); gi.AddRange([a, a + 1, a + 14, a, a + 14, a + 13]); }
        meshes.Add(new CookMesh("ground", Ground, [.. gv], [.. gi]));

        var bv = new List<L2Vertex>(); var bi = new List<uint>();
        for (int s = 0; s < 4; s++)
        {
            var d = new Vector3(MathF.Cos(s * MathF.PI / 2), MathF.Sin(s * MathF.PI / 2), 0);
            var r = new Vector3(-d.Y, d.X, 0);
            Card(bv, bi, new Vector3(24, 24, 0) + d * 0.25f, r * 0.25f, new Vector3(0, 0, 4.5f), d, 0, 0.1f, 0xff405060);
        }
        meshes.Add(new CookMesh("trunk", Bark, [.. bv], [.. bi]));

        var lv = new List<L2Vertex>(); var li = new List<uint>();
        int cards = 0;
        while (cards < 1400)
        {
            var rel = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1));
            float depth = rel.Length();
            if (depth > 1 || depth < 0.15f || (depth < 0.55f && rng.NextDouble() < 0.7)) continue;
            var p = CrownCentre + rel * CrownRadii;
            var outward = Vector3.Normalize(rel / CrownRadii + new Vector3(R(-0.25f, 0.25f), R(-0.25f, 0.25f), R(-0.2f, 0.2f)));
            var upv = Vector3.Normalize(outward * 0.6f + Vector3.UnitZ * 0.5f + new Vector3(R(-0.3f, 0.3f), R(-0.3f, 0.3f), R(-0.3f, 0.3f)));
            var face = Vector3.Normalize(outward - upv * Vector3.Dot(outward, upv));
            float s = 0.6f * R(0.85f, 1.2f);
            var right = Vector3.Normalize(Vector3.Cross(upv, face)) * s / 2;
            var normal = Vector3.Normalize(0.25f * face + 0.75f * Vector3.Normalize(rel));
            Card(lv, li, p - upv * s * 0.12f, right, upv * s, normal, 0.5f, 0.8f, 0xff306040);
            cards++;
        }
        meshes.Add(new CookMesh("crown", Leaves, [.. lv], [.. li]));

        var sv = new List<L2Vertex>(); var si = new List<uint>();
        for (int c = 0; c < 1500; c++)
        {
            float yaw = R(0, MathF.Tau), w = R(0.12f, 0.2f), h = R(0.3f, 0.5f);
            var right = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0) * w;
            var n = new Vector3(-right.Y, right.X, 0.45f * w);
            Card(sv, si, new Vector3(R(5, 15), R(5, 15), -0.03f), right, new Vector3(0, 0, h), Vector3.Normalize(n), 0, 1, 0xff40a060);
        }
        meshes.Add(new CookMesh("grass", Grass, [.. sv], [.. si]));
        return meshes;
    }

    /// <summary>Cooks the scene (one 48 m cell, its block's far version) and returns its cell's two levels of detail and the far version.</summary>
    static (L2MeshLod Lod0, L2MeshLod Lod1, L2MeshLod Far) Cook()
    {
        var ground = Meshes()[0];
        var root = Path.Combine(Path.GetTempPath(), "vitomir-foliagetest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = L2Package.PathFor(root, "foliagetest");
            CellCook.Cook(new CellCookInput
            {
                Name = "foliagetest", Vertices = [.. ground.Vertices.Select(v => v.Position * M)], Indices = [.. ground.Indices.Select(i => (int)i)],
                TriangleMaterials = new int[ground.Indices.Length / 3], Materials = ["ground", "bark", "leaves", "grass"],
                Foliage = [false, false, true, true],
                Ground = m => m == "ground", Passable = _ => false, Meshes = Meshes(), CellSize = 48, BlockCells = 2,
            }, package);
            var manifest = L2Package.Open(package).Manifest;
            var cell = Assert.Single(manifest.Cells);
            var mesh = L2MeshBuffers.Decode(L2Cell.ReadFile(Path.Combine(package, cell.File), t => t == L2Mesh.Tag).Find(L2Mesh.Tag)!);
            var far = L2MeshBuffers.Decode(L2Cell.ReadFile(Path.Combine(package, Assert.Single(manifest.Far!).File), t => t == L2Mesh.Tag).Find(L2Mesh.Tag)!);
            return (mesh.Lods[0], mesh.Lods[1], far.Lods[0]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>A draw's triangles as corner triples.</summary>
    static List<(Vector3 A, Vector3 B, Vector3 C)> Triangles(L2MeshLod lod, int material)
    {
        var list = new List<(Vector3, Vector3, Vector3)>();
        foreach (var d in lod.Draws.Where(d => d.Material == material))
            for (int i = d.FirstIndex; i < d.FirstIndex + d.IndexCount; i += 3)
                list.Add((lod.Vertices[lod.Indices[i]].Position, lod.Vertices[lod.Indices[i + 1]].Position, lod.Vertices[lod.Indices[i + 2]].Position));
        return list;
    }

    /// <summary>A draw's pieces (triangles joined by shared vertices): each piece's triangle count, vertex count and area.</summary>
    static List<(int Triangles, int Vertices, float Area)> Pieces(L2MeshLod lod, int material)
    {
        var parent = new Dictionary<uint, uint>();
        uint Find(uint x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var tris = new List<(uint, uint, uint)>();
        foreach (var d in lod.Draws.Where(d => d.Material == material))
            for (int i = d.FirstIndex; i < d.FirstIndex + d.IndexCount; i += 3)
            {
                var (a, b, c) = (lod.Indices[i], lod.Indices[i + 1], lod.Indices[i + 2]);
                foreach (var x in new[] { a, b, c }) parent.TryAdd(x, x);
                parent[Find(b)] = Find(a); parent[Find(c)] = Find(a);
                tris.Add((a, b, c));
            }
        return [.. tris.GroupBy(t => Find(t.Item1)).Select(g =>
        {
            var verts = g.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).Distinct().Count();
            float area = g.Sum(t => Vector3.Cross(lod.Vertices[t.Item2].Position - lod.Vertices[t.Item1].Position,
                                                  lod.Vertices[t.Item3].Position - lod.Vertices[t.Item1].Position).Length() / 2);
            return (g.Count(), verts, area);
        })];
    }

    /// <summary>
    /// How many cells of a <paramref name="n"/> × <paramref name="n"/> grid over a square of
    /// <paramref name="half"/> metres round <paramref name="centre"/>, seen along <paramref name="view"/>
    /// (orthographic), have a triangle over their middle; and which.
    /// </summary>
    static bool[] Cover(List<(Vector3 A, Vector3 B, Vector3 C)> tris, Vector3 view, Vector3 centre, float half, int n)
    {
        view = Vector3.Normalize(view);
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(view.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ, view));
        var w = Vector3.Cross(view, u);
        var hit = new bool[n * n];
        float px = 2 * half / n;
        Vector2 P(Vector3 p) => new((Vector3.Dot(p - centre, u) + half) / px, (Vector3.Dot(p - centre, w) + half) / px);
        foreach (var (a3, b3, c3) in tris)
        {
            var (a, b, c) = (P(a3), P(b3), P(c3));
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (MathF.Abs(area) < 1e-9f) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), x1 = Math.Min(n - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), y1 = Math.Min(n - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float e0 = (b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X);
                    float e1 = (c.X - b.X) * (q.Y - b.Y) - (c.Y - b.Y) * (q.X - b.X);
                    float e2 = (a.X - c.X) * (q.Y - c.Y) - (a.Y - c.Y) * (q.X - c.X);
                    if (area > 0 ? e0 >= 0 && e1 >= 0 && e2 >= 0 : e0 <= 0 && e1 <= 0 && e2 <= 0) hit[y * n + x] = true;
                }
        }
        return hit;
    }

    /// <summary>The views the crown is measured from: four from the side, two from 35° above, one from straight above.</summary>
    static readonly Vector3[] Views =
    [
        new(1, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, -1, 0), new(1, 0.3f, -0.7f), new(-0.4f, 1, -0.75f), new(0.01f, 0, -1),
    ];

    [Fact]
    public void A_trees_crown_covers_as_much_at_the_second_level_of_detail_and_in_the_far_version_as_near()
    {
        var (lod0, lod1, far) = Cook();
        var near = Triangles(lod0, Leaves);
        Assert.Equal(2800, near.Count);
        float half = CrownRadii.X + 1f;
        foreach (var (name, lod, low, high) in new[] { ("LOD1", lod1, 0.9f, 1.15f), ("far", far, 0.85f, 1.2f) })
        {
            var tris = Triangles(lod, Leaves);
            // Per view: the cover against the full crown's, and how much of the full crown's cover is still covered.
            var measured = Views.Select(view =>
            {
                var c0 = Cover(near, view, CrownCentre, half, 128);
                var c1 = Cover(tris, view, CrownCentre, half, 128);
                int n0 = c0.Count(x => x), n1 = c1.Count(x => x), both = c0.Zip(c1).Count(p => p.First && p.Second);
                return (Ratio: n1 / (float)n0, Kept: both / (float)n0);
            }).ToList();
            string report = $"{name}: cover against the full crown's, per view: {string.Join(", ", measured.Select(m => $"{m.Ratio:P0} ({m.Kept:P0} kept)"))}";
            Assert.True(measured.All(m => m.Ratio >= low && m.Ratio <= high), report);
            Assert.True(measured.All(m => m.Kept >= 0.8f), report);
        }
        Assert.True(Triangles(lod1, Leaves).Count <= near.Count * 0.6f, $"LOD1 keeps {Triangles(lod1, Leaves).Count} of {near.Count} leaf triangles");
        Assert.True(Triangles(far, Leaves).Count <= near.Count * 0.25f, $"the far version keeps {Triangles(far, Leaves).Count} of {near.Count} leaf triangles");
        // The trunk and the ground are simplified as before: there is still a trunk.
        Assert.NotEmpty(Triangles(far, Bark));
    }

    [Fact]
    public void Leaf_and_grass_cards_are_kept_or_dropped_whole_and_never_collapsed()
    {
        var (lod0, lod1, far) = Cook();
        foreach (int material in new[] { Leaves, Grass })
        {
            var cards = Pieces(lod0, material);
            float smallest = cards.Min(c => c.Area);
            Assert.All(cards, c => Assert.Equal((2, 4), (c.Triangles, c.Vertices)));
            foreach (var (name, lod) in new[] { ("LOD1", lod1), ("far", far) })
            {
                var pieces = Pieces(lod, material);
                Assert.NotEmpty(pieces);
                Assert.All(pieces, p => Assert.True(p.Triangles == 2 && p.Vertices == 4 && p.Area >= smallest * 0.99f,
                    $"{name}: a piece of {p.Triangles} triangles, {p.Vertices} vertices, {p.Area:0.000} m² (the smallest card is {smallest:0.000} m²)"));
            }
        }
        // Grass thins with distance, by whole cards: at least two in five stay at LOD1, fewer in the far version.
        int grass0 = Pieces(lod0, Grass).Count, grass1 = Pieces(lod1, Grass).Count, grassFar = Pieces(far, Grass).Count;
        Assert.True(grass1 >= grass0 * 0.4f && grassFar < grass1, $"grass cards: {grass0} near, {grass1} at LOD1, {grassFar} far");
    }
}
