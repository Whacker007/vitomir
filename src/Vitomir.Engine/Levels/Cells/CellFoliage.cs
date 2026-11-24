// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: foliage in the cook's levels of detail and far versions.** A foliage material
/// (the glTF material's <c>extras.foliage</c>, the contract) is drawn as cut-out cards: leaves,
/// grass, weeds, ivy and moss, each card a quad (or a short strip of quads) of its own vertices. The
/// simplifier (<see cref="MeshSimplify.Simplify"/>) collapses a small card to a triangle and the far
/// versions' vertex clustering (<see cref="MeshSimplify.Cluster"/>) welds a crown's cards into a web of
/// slivers, so a tree thinned out and a lawn went patchy with distance. Here foliage is simplified by
/// <b>whole cards</b> instead:
///
/// <list type="bullet">
/// <item>A draw's <b>pieces</b> are its triangles joined by shared vertices. A piece of at most
/// <see cref="MaxCardTriangles"/> triangles is a card; a bigger one (a hedge made as one sheet) is kept
/// whole, as it is.</item>
/// <item>A card is <b>rooted</b> — grass, a weed, a tuft of moss — when its lowest sway weight (the
/// vertex colour's alpha, 0 at the root by the contract) is about 0; otherwise it hangs in a
/// <b>crown</b> (leaves on a twig, ivy on a wall).</item>
/// <item>The cards are <b>thinned evenly</b>: binned on a grid (<see cref="Rule.Bin"/> metres), the
/// bins taken in a fixed order and the cards of each in a fixed order of their own, a card is kept
/// whenever the running count falls behind <see cref="Rule.Keep"/> of the cards seen so far, so the
/// dropped cards are spread through the crown rather than leaving holes. The order is the cards'
/// places, so a cook is repeatable.</item>
/// <item>Each kept card <b>grows</b> by <see cref="Rule.Grow"/>: a crown's about its middle, by the
/// inverse square root of the share kept, so the crown's cards cover the same area as before — and,
/// thinned at random and grown, they overlap one another as the full set did, so the crown lets as
/// much sky through, near and far; a rooted card about its root (it stays standing on the ground), by
/// less, so grass thins out with distance without turning into bushes.</item>
/// <item>A grown crown card would reach past the crown's edge by as much as it grew, and the crown would
/// swell; so it also moves inward — towards the middle of the crown cards within a few metres of it —
/// by <see cref="Rule.Inset"/> of that reach.</item>
/// </list>
///
/// A grown card keeps its own place, facing, bent normals, colour (the crown's baked shading), sway
/// weights and texture: the crown's silhouette, colour and density are its own at every distance.
/// <c>CellFoliageTests</c> measures the crown's cover from seven views against the full set's.
/// </summary>
public static class CellFoliage
{
    /// <summary>The most triangles a piece of a foliage draw may have and be treated as a card.</summary>
    public const int MaxCardTriangles = 32;

    /// <summary>The bins (metres) the middle of a crown round a card is found in, for moving a grown card inward.</summary>
    public const float InsetBin = 2f;

    /// <summary>The highest sway weight (0–255, the vertex colour's alpha) a rooted card's root may have.</summary>
    public const int RootSway = 5;

    /// <summary>How a level of detail thins one kind of card: the share kept, how much a kept card grows, and the bins (metres) the thinning evens out over.</summary>
    public readonly record struct Rule(float Keep, float Grow, float Bin, float Inset = 0f);

    /// <summary>A cell's second level of detail (from 90 m): half of a crown's cards, each √2 the size; half the grass, a fifth bigger.</summary>
    public static readonly Rule Lod1Crown = new(0.5f, 1.41421356f, 1f, 0.5f);
    public static readonly Rule Lod1Rooted = new(0.5f, 1.19f, 1f);

    /// <summary>A block's far version (from about 120 m to the horizon): a seventh of a crown's cards, each 2.6 times the size; a fifth of the grass, half again the size.</summary>
    public static readonly Rule FarCrown = new(0.15f, 2.58199f, 1.5f, 0.5f);
    public static readonly Rule FarRooted = new(0.2f, 1.5f, 2f);

    /// <summary>
    /// A foliage draw thinned by whole cards: the vertices of the cards kept (grown) and of the pieces too
    /// big to be cards, and their triangles. <paramref name="indices"/> index <paramref name="vertices"/>.
    /// </summary>
    public static (L2Vertex[] Vertices, uint[] Indices) Thin(L2Vertex[] vertices, uint[] indices, Rule crown, Rule rooted)
    {
        var pieces = Pieces(indices);
        var cards = new List<(int Piece, Vector3 Middle, Vector3 Root, bool Rooted)>();
        var keep = new bool[pieces.Count];
        for (int p = 0; p < pieces.Count; p++)
        {
            var tris = pieces[p];
            if (tris.Count > MaxCardTriangles) { keep[p] = true; continue; }
            var middle = Vector3.Zero; float area = 0f;
            int low = 255;
            foreach (int t in tris)
            {
                var (a, b, c) = (vertices[indices[3 * t]].Position, vertices[indices[3 * t + 1]].Position, vertices[indices[3 * t + 2]].Position);
                float s = Vector3.Cross(b - a, c - a).Length() / 2;
                middle += (a + b + c) / 3f * s; area += s;
                for (int k = 0; k < 3; k++) low = Math.Min(low, Sway(vertices[indices[3 * t + k]]));
            }
            if (area <= 0f) continue;                                     // a card with no face: nothing to draw
            middle /= area;
            // The root: the middle of the vertices at the lowest sway (the bottom edge of a standing card, all of a flat one).
            var root = Vector3.Zero; int n = 0;
            foreach (uint v in Corners(tris, indices))
                if (Sway(vertices[v]) <= low + 2) { root += vertices[v].Position; n++; }
            root /= Math.Max(1, n);
            cards.Add((p, middle, root, low <= RootSway));
        }

        // Even thinning: per kind, bins in order, cards in each bin in order of place, a running share.
        foreach (bool isRooted in new[] { false, true })
        {
            var rule = isRooted ? rooted : crown;
            float bin = MathF.Max(0.01f, rule.Bin);
            var bins = cards.Where(c => c.Rooted == isRooted)
                            .GroupBy(c => (X: (int)MathF.Floor(c.Middle.X / bin), Y: (int)MathF.Floor(c.Middle.Y / bin), Z: (int)MathF.Floor(c.Middle.Z / bin)))
                            .OrderBy(g => g.Key.Z).ThenBy(g => g.Key.Y).ThenBy(g => g.Key.X);
            double seen = 0, kept = 0;
            foreach (var g in bins)
                foreach (var c in g.OrderBy(c => Order(c.Middle)))
                {
                    seen++;
                    if (kept < seen * rule.Keep - 1e-9) { keep[c.Piece] = true; kept++; }
                }
        }

        // Where the crown is, round each crown card: the middle of the crown cards in the 3 × 3 × 3 bins of InsetBin metres round it.
        var near = new Dictionary<(int, int, int), (Vector3 Sum, int N)>();
        (int, int, int) Key(Vector3 p) => ((int)MathF.Floor(p.X / InsetBin), (int)MathF.Floor(p.Y / InsetBin), (int)MathF.Floor(p.Z / InsetBin));
        foreach (var c in cards.Where(c => !c.Rooted))
        {
            var k = Key(c.Middle);
            near[k] = near.TryGetValue(k, out var e) ? (e.Sum + c.Middle, e.N + 1) : (c.Middle, 1);
        }
        Vector3 Inward(Vector3 p)
        {
            var (x, y, z) = Key(p);
            var sum = Vector3.Zero; int n = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (near.TryGetValue((x + dx, y + dy, z + dz), out var e)) { sum += e.Sum; n += e.N; }
            var d = n > 0 ? sum / n - p : Vector3.Zero;
            return d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : Vector3.Zero;
        }

        // The kept cards, grown about their middles (a crown's, moved inward by the rule's inset of its reach) or their roots (a rooted one's).
        var grow = new Dictionary<int, (Vector3 About, float By, Vector3 Move)>();
        foreach (var c in cards.Where(c => keep[c.Piece]))
        {
            if (c.Rooted) { grow[c.Piece] = (c.Root, rooted.Grow, Vector3.Zero); continue; }
            float reach = Corners(pieces[c.Piece], indices).Max(v => Vector3.Distance(vertices[v].Position, c.Middle)) * (crown.Grow - 1f);
            grow[c.Piece] = (c.Middle, crown.Grow, Inward(c.Middle) * reach * crown.Inset);
        }
        var at = new Dictionary<uint, uint>();
        var outV = new List<L2Vertex>();
        var outI = new List<uint>();
        for (int p = 0; p < pieces.Count; p++)
        {
            if (!keep[p]) continue;
            var (about, by, move) = grow.TryGetValue(p, out var gr) ? gr : (Vector3.Zero, 1f, Vector3.Zero);
            foreach (int t in pieces[p])
                for (int k = 0; k < 3; k++)
                {
                    uint v = indices[3 * t + k];
                    if (!at.TryGetValue(v, out uint o))
                    {
                        o = (uint)outV.Count; at[v] = o;
                        var vx = vertices[v];
                        vx.Position = about + (vx.Position - about) * by + move;
                        outV.Add(vx);
                    }
                    outI.Add(o);
                }
        }
        return ([.. outV], [.. outI]);
    }

    /// <summary>The pieces of a triangle list (triangles joined by shared vertices), each a list of triangle numbers, in order of their first triangle.</summary>
    public static List<List<int>> Pieces(uint[] indices)
    {
        int triangles = indices.Length / 3;
        var local = new Dictionary<uint, int>();
        foreach (uint v in indices) local.TryAdd(v, local.Count);
        var parent = new int[local.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int t = 0; t < triangles; t++)
        {
            int a = Find(local[indices[3 * t]]);
            for (int k = 1; k < 3; k++) { int b = Find(local[indices[3 * t + k]]); if (a != b) parent[b] = a; }
        }
        var byRoot = new Dictionary<int, List<int>>();
        var pieces = new List<List<int>>();
        for (int t = 0; t < triangles; t++)
        {
            int r = Find(local[indices[3 * t]]);
            if (!byRoot.TryGetValue(r, out var list)) { list = []; byRoot[r] = list; pieces.Add(list); }
            list.Add(t);
        }
        return pieces;
    }

    static IEnumerable<uint> Corners(List<int> tris, uint[] indices) => tris.SelectMany(t => new[] { indices[3 * t], indices[3 * t + 1], indices[3 * t + 2] }).Distinct();

    static int Sway(L2Vertex v) => (int)(v.Color >> 24);

    /// <summary>A card's place in its bin's order: a hash of its middle to a millimetre, so a cook is repeatable and the order unrelated to the card's neighbours'.</summary>
    static uint Order(Vector3 middle)
    {
        uint h = 2166136261u;
        foreach (float f in new[] { middle.X, middle.Y, middle.Z })
        {
            h = (h ^ (uint)(int)MathF.Round(f * 1000f)) * 16777619u;
            h ^= h >> 15;
        }
        return h;
    }
}
