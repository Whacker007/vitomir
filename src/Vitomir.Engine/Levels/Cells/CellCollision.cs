// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours.** A cell level's solid space: the <c>COLL</c> chunk's triangles and bounding-volume tree,
/// in engine units, and the questions the game asks of 1.1's collision trees asked of them instead —
/// where the floor is under a point, what a ray meets first, and whether a body standing somewhere is
/// clear of every wall.
///
/// The tree is built once by the cook (<see cref="Build"/>) and stored with the triangles in its leaf
/// order, so loading is a copy and a scale.
/// </summary>
public sealed partial class CellCollision
{
    /// <summary>Triangles a leaf of the tree holds at most.</summary>
    public const int LeafSize = 4;

    public Vector3[] Vertices { get; }
    public int[] Indices { get; }
    public L2Surface[] Surfaces { get; }
    public ushort[] Materials { get; }
    public L2BvhNode[] Nodes { get; }
    public int Triangles => Indices.Length / 3;

    /// <summary>The chunk's triangles, scaled from metres to engine units by <paramref name="scale"/>.</summary>
    public CellCollision(L2Collision chunk, float scale)
    {
        Vertices = [.. chunk.Vertices.Select(v => v * scale)];
        Indices = chunk.Indices;
        Surfaces = chunk.Surfaces;
        Materials = chunk.Materials;
        Nodes = [.. chunk.Nodes.Select(n => n with { Min = n.Min * scale, Max = n.Max * scale })];
    }

    /// <summary>
    /// The chunk the cook writes: <paramref name="indices"/>' triangles sorted into a bounding-volume
    /// tree (split at the median of the longest axis of the triangles' centres, <see cref="LeafSize"/>
    /// to a leaf), with their flags and materials carried along.
    /// </summary>
    public static L2Collision Build(Vector3[] vertices, int[] indices, L2Surface[] surfaces, ushort[] materials)
    {
        int count = indices.Length / 3;
        var order = Enumerable.Range(0, count).ToArray();
        var centre = new Vector3[count];
        var lo = new Vector3[count]; var hi = new Vector3[count];
        for (int t = 0; t < count; t++)
        {
            var (a, b, c) = (vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);
            lo[t] = Vector3.Min(a, Vector3.Min(b, c)); hi[t] = Vector3.Max(a, Vector3.Max(b, c));
            centre[t] = (lo[t] + hi[t]) * 0.5f;
        }
        var nodes = new List<L2BvhNode>(Math.Max(1, count / 2));
        void Split(int from, int to)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            var cmin = min; var cmax = max;
            for (int i = from; i < to; i++)
            {
                int t = order[i];
                min = Vector3.Min(min, lo[t]); max = Vector3.Max(max, hi[t]);
                cmin = Vector3.Min(cmin, centre[t]); cmax = Vector3.Max(cmax, centre[t]);
            }
            int at = nodes.Count;
            if (to - from <= LeafSize) { nodes.Add(new L2BvhNode(min, max, from, to - from)); return; }
            nodes.Add(default);
            var extent = cmax - cmin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            int mid = (from + to) / 2;
            Array.Sort(order, from, to - from, Comparer<int>.Create((p, q) => Axis(centre[p], axis).CompareTo(Axis(centre[q], axis))));
            Split(from, mid);
            int right = nodes.Count;
            Split(mid, to);
            nodes[at] = new L2BvhNode(min, max, right, 0);
        }
        if (count > 0) Split(0, count);
        var sortedIndices = new int[indices.Length];
        var sortedSurfaces = new L2Surface[count];
        var sortedMaterials = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            int t = order[i];
            sortedIndices[3 * i] = indices[3 * t]; sortedIndices[3 * i + 1] = indices[3 * t + 1]; sortedIndices[3 * i + 2] = indices[3 * t + 2];
            sortedSurfaces[i] = surfaces[t]; sortedMaterials[i] = materials[t];
        }
        return new L2Collision { Vertices = vertices, Indices = sortedIndices, Surfaces = sortedSurfaces, Materials = sortedMaterials, Nodes = [.. nodes] };
    }

    static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>What a ray met: how far along it (in the ray's own length units), which triangle, and where.</summary>
    public readonly record struct Hit(float Distance, int Triangle, Vector3 Point, Vector3 Normal);

    /// <summary>A triangle's three corners.</summary>
    public (Vector3 A, Vector3 B, Vector3 C) Corners(int t) =>
        (Vertices[Indices[3 * t]], Vertices[Indices[3 * t + 1]], Vertices[Indices[3 * t + 2]]);

    /// <summary>A triangle's unit normal, by its winding.</summary>
    public Vector3 Normal(int t)
    {
        var (a, b, c) = Corners(t);
        var n = Vector3.Cross(b - a, c - a);
        float l = n.Length();
        return l > 0 ? n / l : Vector3.UnitZ;
    }

    /// <summary>
    /// The first triangle carrying any of <paramref name="mask"/>'s flags that the segment from
    /// <paramref name="origin"/> along <paramref name="direction"/> (a unit vector) meets within
    /// <paramref name="length"/>; both faces count.
    /// </summary>
    public Hit? Ray(Vector3 origin, Vector3 direction, float length, L2Surface mask = L2Surface.Solid)
    {
        if (Nodes.Length == 0) return null;
        var inverse = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        float best = length; int bestT = -1;
        Span<int> stack = stackalloc int[128];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (!Slab(origin, inverse, node.Min, node.Max, best)) continue;
            if (!node.IsLeaf)
            {
                // The first child follows its parent; the second is stored on the node.
                stack[top++] = node.Right; stack[top++] = at + 1;
                continue;
            }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & mask) == 0) continue;
                var (a, b, c) = Corners(t);
                if (Meets(origin, direction, a, b, c, out float d) && d < best) { best = d; bestT = t; }
            }
        }
        return bestT < 0 ? null : new Hit(best, bestT, origin + direction * best, Normal(bestT));
    }

    /// <summary>
    /// The height of the walkable ground under (<paramref name="x"/>, <paramref name="y"/>) nearest
    /// <paramref name="z"/> within <paramref name="window"/> above or below it, or null. Every
    /// walkable triangle the vertical line meets in that span is a candidate, so a floor under an
    /// awning or a bridge over a street is found at the height asked about.
    /// </summary>
    public float? Floor(float x, float y, float z, float window)
    {
        if (Nodes.Length == 0) return null;
        float lo = z - window, hi = z + window;
        float? best = null;
        Span<int> stack = stackalloc int[128];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (x < node.Min.X || x > node.Max.X || y < node.Min.Y || y > node.Max.Y || node.Max.Z < lo || node.Min.Z > hi) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & L2Surface.Walk) == 0) continue;
                var (a, b, c) = Corners(t);
                if (HeightIn(a, b, c, x, y) is not { } h || h < lo || h > hi) continue;
                if (best is not { } was || MathF.Abs(h - z) < MathF.Abs(was - z)) best = h;
            }
        }
        return best;
    }

    /// <summary>The height of a triangle's plane over a point inside its outline seen from above, or null.</summary>
    static float? HeightIn(Vector3 a, Vector3 b, Vector3 c, float x, float y)
    {
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(d) < 1e-9f) return null;
        float l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
        float l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
        float l3 = 1f - l1 - l2;
        const float E = -1e-5f;
        if (l1 < E || l2 < E || l3 < E) return null;
        return l1 * a.Z + l2 * b.Z + l3 * c.Z;
    }

    /// <summary>
    /// Whether a body standing with its feet at <paramref name="feet"/> — a column of
    /// <paramref name="radius"/> from <paramref name="step"/> above the feet to
    /// <paramref name="height"/> — is clear of every solid triangle that is not ground. The part
    /// below <paramref name="step"/> is left out, so the kerb or cobble the feet stand beside does
    /// not count as a wall.
    /// </summary>
    public bool Open(Vector3 feet, float radius, float step, float height)
    {
        if (Nodes.Length == 0) return true;
        var bottom = feet + new Vector3(0, 0, step);
        var top = feet + new Vector3(0, 0, height);
        var min = new Vector3(feet.X - radius, feet.Y - radius, bottom.Z);
        var max = new Vector3(feet.X + radius, feet.Y + radius, top.Z);
        Span<int> stack = stackalloc int[128];
        int sp = 0; stack[sp++] = 0;
        while (sp > 0)
        {
            int at = stack[--sp];
            var node = Nodes[at];
            if (node.Max.X < min.X || node.Min.X > max.X || node.Max.Y < min.Y || node.Min.Y > max.Y || node.Max.Z < min.Z || node.Min.Z > max.Z) continue;
            if (!node.IsLeaf) { stack[sp++] = node.Right; stack[sp++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & L2Surface.Solid) == 0 || (Surfaces[t] & L2Surface.Walk) != 0) continue;
                var (a, b, c) = Corners(t);
                if (SegmentTriangleDistanceSquared(bottom, top, a, b, c) < radius * radius) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The squared distance between a vertical segment and a triangle, sampled: the segment's points
    /// every quarter of the radius-sized steps against the triangle's closest point. Exact enough for a
    /// wall test, and far simpler than the closed form.
    /// </summary>
    static float SegmentTriangleDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
    {
        float best = float.MaxValue;
        const int Samples = 6;
        for (int i = 0; i <= Samples; i++)
        {
            var s = Vector3.Lerp(p, q, i / (float)Samples);
            best = MathF.Min(best, Vector3.DistanceSquared(s, ClosestOnTriangle(s, a, b, c)));
        }
        return best;
    }

    /// <summary>The point of a triangle nearest another point (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
    public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    static bool Slab(Vector3 o, Vector3 inv, Vector3 min, Vector3 max, float length)
    {
        float t1 = (min.X - o.X) * inv.X, t2 = (max.X - o.X) * inv.X;
        float tmin = MathF.Min(t1, t2), tmax = MathF.Max(t1, t2);
        t1 = (min.Y - o.Y) * inv.Y; t2 = (max.Y - o.Y) * inv.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        t1 = (min.Z - o.Z) * inv.Z; t2 = (max.Z - o.Z) * inv.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        return !float.IsNaN(tmin) && !float.IsNaN(tmax) ? tmax >= MathF.Max(tmin, 0f) && tmin <= length : true;
    }

    /// <summary>Möller–Trumbore, both faces.</summary>
    static bool Meets(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a; var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        var s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t >= 0f;
    }
}
