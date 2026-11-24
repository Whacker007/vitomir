// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernBvh.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// A bounding-volume tree over triangles for casting rays on the CPU (ours; the light probes' bake,
/// <see cref="ModernProbeBake"/>). Built top down by the surface-area heuristic over sixteen bins of the
/// triangles' centroids, four triangles or fewer to a leaf, flattened depth first (an inner node's first
/// child is the next node). Rays are cast with Möller and Trumbore's test, both faces counting, the nearer
/// child first; <see cref="Occluded"/> stops at the first hit. Safe to cast from many threads at once.
/// </summary>
internal sealed class ModernBvh
{
    struct Node
    {
        public Vector3 Min, Max;
        public int Index;   // a leaf's first triangle; an inner node's second child
        public int Count;   // 0 for an inner node
    }

    readonly Node[] nodes;
    readonly Vector3[] v0, e1, e2;
    readonly int[] ids;

    /// <summary>Triangles in the tree.</summary>
    public int Triangles => ids.Length;

    /// <summary>Builds the tree over <paramref name="indices"/>' triangles (corners in <paramref name="positions"/>); degenerate triangles are left out.</summary>
    public ModernBvh(Vector3[] positions, int[] indices)
    {
        int n = indices.Length / 3;
        var order = new List<int>(n);
        var cmin = new Vector3[n]; var cmax = new Vector3[n]; var cen = new Vector3[n];
        for (int t = 0; t < n; t++)
        {
            Vector3 a = positions[indices[3 * t]], b = positions[indices[3 * t + 1]], c = positions[indices[3 * t + 2]];
            if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-14f) continue;
            cmin[t] = Vector3.Min(a, Vector3.Min(b, c)); cmax[t] = Vector3.Max(a, Vector3.Max(b, c));
            cen[t] = (cmin[t] + cmax[t]) * 0.5f;
            order.Add(t);
        }
        var tris = order.ToArray();
        var list = new List<Node>(Math.Max(1, tris.Length / 2));
        Build(list, tris, 0, tris.Length, cmin, cmax, cen);
        nodes = [.. list];
        ids = tris;
        v0 = new Vector3[tris.Length]; e1 = new Vector3[tris.Length]; e2 = new Vector3[tris.Length];
        for (int i = 0; i < tris.Length; i++)
        {
            int t = tris[i];
            Vector3 a = positions[indices[3 * t]], b = positions[indices[3 * t + 1]], c = positions[indices[3 * t + 2]];
            v0[i] = a; e1[i] = b - a; e2[i] = c - a;
        }
    }

    const int Bins = 16, LeafSize = 4;

    static int Build(List<Node> list, int[] tris, int first, int count, Vector3[] cmin, Vector3[] cmax, Vector3[] cen)
    {
        int me = list.Count;
        var bmin = new Vector3(float.MaxValue); var bmax = new Vector3(float.MinValue);
        var kmin = new Vector3(float.MaxValue); var kmax = new Vector3(float.MinValue);
        for (int i = first; i < first + count; i++)
        {
            int t = tris[i];
            bmin = Vector3.Min(bmin, cmin[t]); bmax = Vector3.Max(bmax, cmax[t]);
            kmin = Vector3.Min(kmin, cen[t]); kmax = Vector3.Max(kmax, cen[t]);
        }
        list.Add(new Node { Min = bmin, Max = bmax, Index = first, Count = Math.Max(count, 1) });
        if (count <= LeafSize) return me;

        // The best split of sixteen bins along each axis by the surface-area heuristic.
        int bestAxis = -1, bestBin = 0;
        float bestCost = float.MaxValue;
        Span<int> binCount = stackalloc int[Bins];
        Span<float> leftArea = stackalloc float[Bins];
        Span<int> leftCount = stackalloc int[Bins];
        var binMin = new Vector3[Bins]; var binMax = new Vector3[Bins];
        for (int axis = 0; axis < 3; axis++)
        {
            float lo = Get(kmin, axis), hi = Get(kmax, axis);
            if (hi - lo < 1e-6f) continue;
            float scale = Bins / (hi - lo);
            binCount.Clear();
            for (int b = 0; b < Bins; b++) { binMin[b] = new Vector3(float.MaxValue); binMax[b] = new Vector3(float.MinValue); }
            for (int i = first; i < first + count; i++)
            {
                int t = tris[i];
                int b = Math.Min(Bins - 1, (int)((Get(cen[t], axis) - lo) * scale));
                binCount[b]++;
                binMin[b] = Vector3.Min(binMin[b], cmin[t]); binMax[b] = Vector3.Max(binMax[b], cmax[t]);
            }
            var lmin = new Vector3(float.MaxValue); var lmax = new Vector3(float.MinValue);
            int lc = 0;
            for (int b = 0; b < Bins - 1; b++)
            {
                lc += binCount[b];
                if (binCount[b] > 0) { lmin = Vector3.Min(lmin, binMin[b]); lmax = Vector3.Max(lmax, binMax[b]); }
                leftCount[b] = lc; leftArea[b] = lc > 0 ? Area(lmin, lmax) : 0f;
            }
            var rmin = new Vector3(float.MaxValue); var rmax = new Vector3(float.MinValue);
            int rc = 0;
            for (int b = Bins - 1; b > 0; b--)
            {
                rc += binCount[b];
                if (binCount[b] > 0) { rmin = Vector3.Min(rmin, binMin[b]); rmax = Vector3.Max(rmax, binMax[b]); }
                int l = leftCount[b - 1];
                if (l == 0 || rc == 0) continue;
                float cost = leftArea[b - 1] * l + Area(rmin, rmax) * rc;
                if (cost < bestCost) { bestCost = cost; bestAxis = axis; bestBin = b; }
            }
        }
        if (bestAxis < 0 || bestCost >= Area(bmin, bmax) * count && count <= 16) return me;   // a leaf is cheaper (or nothing splits)

        int mid;
        if (bestAxis < 0) mid = first + count / 2;
        else
        {
            float lo = Get(kmin, bestAxis), scale = Bins / (Get(kmax, bestAxis) - lo);
            int i = first, j = first + count - 1;
            while (i <= j)
            {
                int b = Math.Min(Bins - 1, (int)((Get(cen[tris[i]], bestAxis) - lo) * scale));
                if (b < bestBin) i++;
                else { (tris[i], tris[j]) = (tris[j], tris[i]); j--; }
            }
            mid = i;
            if (mid == first || mid == first + count) mid = first + count / 2;
        }
        Build(list, tris, first, mid - first, cmin, cmax, cen);
        int right = Build(list, tris, mid, first + count - mid, cmin, cmax, cen);
        var node = list[me];
        node.Index = right; node.Count = 0;
        list[me] = node;
        return me;
    }

    static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    static float Area(Vector3 min, Vector3 max) { var d = Vector3.Max(max - min, Vector3.Zero); return d.X * d.Y + d.Y * d.Z + d.Z * d.X; }

    /// <summary>A ray's nearest hit: its distance, the triangle (its index in the input) and whether its back was hit (the ray along its normal).</summary>
    public readonly record struct Hit(float Distance, int Triangle, bool Back);

    /// <summary>The nearest triangle along the ray from <paramref name="origin"/> in unit direction <paramref name="dir"/> within <paramref name="far"/>, or null.</summary>
    public Hit? Cast(Vector3 origin, Vector3 dir, float far) => Trace(origin, dir, far, any: false);

    /// <summary>Whether anything lies along the ray within <paramref name="far"/>.</summary>
    public bool Occluded(Vector3 origin, Vector3 dir, float far) => Trace(origin, dir, far, any: true) is not null;

    Hit? Trace(Vector3 o, Vector3 d, float far, bool any)
    {
        if (nodes.Length == 0 || ids.Length == 0) return null;
        var inv = new Vector3(1f / (MathF.Abs(d.X) > 1e-12f ? d.X : 1e-12f), 1f / (MathF.Abs(d.Y) > 1e-12f ? d.Y : 1e-12f), 1f / (MathF.Abs(d.Z) > 1e-12f ? d.Z : 1e-12f));
        Span<int> stack = stackalloc int[96];
        int sp = 0;
        stack[sp++] = 0;
        float best = far;
        int bestTri = -1;
        bool bestBack = false;
        while (sp > 0)
        {
            int ni = stack[--sp];
            ref readonly var node = ref nodes[ni];
            if (!Slab(node.Min, node.Max, o, inv, best)) continue;
            if (node.Count > 0)
            {
                for (int i = node.Index; i < node.Index + node.Count; i++)
                {
                    var p = Vector3.Cross(d, e2[i]);
                    float det = Vector3.Dot(e1[i], p);
                    if (MathF.Abs(det) < 1e-12f) continue;
                    float invDet = 1f / det;
                    var s = o - v0[i];
                    float u = Vector3.Dot(s, p) * invDet;
                    if (u < 0f || u > 1f) continue;
                    var q = Vector3.Cross(s, e1[i]);
                    float v = Vector3.Dot(d, q) * invDet;
                    if (v < 0f || u + v > 1f) continue;
                    float t = Vector3.Dot(e2[i], q) * invDet;
                    if (t <= 1e-4f || t >= best) continue;
                    best = t; bestTri = i; bestBack = det < 0f;
                    if (any) return new Hit(t, ids[i], bestBack);
                }
                continue;
            }
            // The nearer child last on the stack, so it is taken first: the one the ray meets first along the line between their middles.
            int a = ni + 1, b = node.Index;
            var ca = nodes[a].Min + nodes[a].Max;
            var cb = nodes[b].Min + nodes[b].Max;
            if (Vector3.Dot(cb - ca, d) < 0f) (a, b) = (b, a);
            if (sp + 2 > stack.Length) throw new InvalidOperationException("ModernBvh: the tree is deeper than its stack");
            stack[sp++] = b;
            stack[sp++] = a;
        }
        return bestTri < 0 ? null : new Hit(best, ids[bestTri], bestBack);
    }

    static bool Slab(Vector3 min, Vector3 max, Vector3 o, Vector3 inv, float far)
    {
        var t0 = (min - o) * inv;
        var t1 = (max - o) * inv;
        var lo = Vector3.Min(t0, t1);
        var hi = Vector3.Max(t0, t1);
        float enter = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, 0f));
        float exit = MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, far));
        return enter <= exit;
    }
}
