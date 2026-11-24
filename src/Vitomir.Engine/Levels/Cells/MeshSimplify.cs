// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: the cook's mesh simplifier** (Phase 3, levels of detail), in C# so the cook needs nothing
/// else. Two methods, after meshoptimizer's pair (Arseny Kapoulkine's <c>meshopt_simplify</c> and
/// <c>meshopt_simplifySloppy</c>), written afresh:
///
/// <list type="bullet">
/// <item><see cref="Simplify"/>, for a cell's second level of detail: edge collapses ranked by the
/// quadric error metric (Garland and Heckbert, 1997), each moving a vertex onto a neighbour, never
/// moving a vertex on the mesh's open edge (so a cell's border, where its neighbour's triangles meet
/// it, stays where it is and levels of detail side by side leave no crack), moving a vertex on a
/// texture seam or hard edge only along it (each of its copies must have one on the other end to go
/// to), and refusing a collapse that would turn a triangle over.</item>
/// <item><see cref="Cluster"/>, for a block's far version: vertex clustering on a grid — every vertex
/// in a grid cell becomes one, at their mean — which reduces anything, open edges and all, to what can
/// be seen of it from a distance.</item>
/// </list>
/// </summary>
public static class MeshSimplify
{
    /// <summary>
    /// The indices of a simplified <paramref name="indices"/> over the same <paramref name="vertices"/>:
    /// collapses go on, cheapest first, until the triangles are down to <paramref name="ratio"/> of
    /// what they were or the next would move the surface more than <paramref name="maxError"/> (in the
    /// vertices' own units). <paramref name="error"/> is the largest error a collapse made.
    /// </summary>
    public static uint[] Simplify(L2Vertex[] vertices, uint[] indices, float ratio, float maxError, out float error,
                                  Func<Vector3, bool>? pinned = null)
    {
        error = 0f;
        var clock = System.Diagnostics.Stopwatch.StartNew();   // for VITOMIR_SIMPLIFY_PROFILE
        int triangles = indices.Length / 3;
        int target = Math.Max(1, (int)(triangles * ratio));
        if (triangles <= target || triangles < 4) return (uint[])indices.Clone();

        // 1. One "place" per distinct position; seams are positions with more than one vertex.
        var placeOf = new int[vertices.Length];
        var places = new List<Vector3>();
        var byPosition = new Dictionary<Vector3, int>();
        foreach (var t in UsedVertices(indices, vertices.Length))
        {
            var p = vertices[t].Position;
            if (!byPosition.TryGetValue(p, out int at))
            {
                at = places.Count; byPosition[p] = at; places.Add(p);
            }
            placeOf[t] = at;
        }
        int n = places.Count;
        var tri = new int[indices.Length];                    // places
        var idx = (uint[])indices.Clone();                   // vertices
        for (int i = 0; i < idx.Length; i++) tri[i] = placeOf[idx[i]];
        var alive = new bool[triangles];
        var around = new List<int>[n];
        for (int i = 0; i < n; i++) around[i] = [];
        for (int t = 0; t < triangles; t++)
        {
            if (tri[3 * t] == tri[3 * t + 1] || tri[3 * t + 1] == tri[3 * t + 2] || tri[3 * t] == tri[3 * t + 2]) continue;
            alive[t] = true;
            for (int k = 0; k < 3; k++) around[tri[3 * t + k]].Add(t);
        }

        // 2. Open edges and non-manifold edges lock their ends.
        // Keyed by the pair, not by a packed long: a long's hash is its halves exclusive-ored, which for
        // neighbouring vertex numbers is a handful of values, and the dictionary would crawl.
        var edgeUse = new Dictionary<(int, int), int>();
        for (int t = 0; t < triangles; t++)
        {
            if (!alive[t]) continue;
            for (int k = 0; k < 3; k++)
            {
                int a = tri[3 * t + k], b = tri[3 * t + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                edgeUse[key] = edgeUse.GetValueOrDefault(key) + 1;
            }
        }
        var locked = new bool[n];
        var border = new bool[n];
        foreach (var (key, uses) in edgeUse)
        {
            var (a, b) = key;
            if (uses > 2) locked[a] = locked[b] = true;
            else if (uses == 1) border[a] = border[b] = true;
        }
        if (pinned is not null)
            for (int i = 0; i < n; i++) if (border[i] && pinned(places[i])) locked[i] = true;
        // A place a great many triangles meet at (a fan's hub, blades of grass rooted in one point) is left
        // where it is: weighing its collapses would cost the square of its fan, and it is rarely worth it.
        for (int i = 0; i < n; i++) if (around[i].Count > MaxFan) locked[i] = true;

        // 3. Each place's quadric: the planes of its triangles, weighted by their area.
        var quadric = new Quadric[n];
        for (int t = 0; t < triangles; t++)
        {
            if (!alive[t]) continue;
            var (a, b, c) = (places[tri[3 * t]], places[tri[3 * t + 1]], places[tri[3 * t + 2]]);
            var cross = Vector3.Cross(b - a, c - a);
            float area = cross.Length();
            if (area <= 0) continue;
            var normal = cross / area;
            var q = Quadric.Plane(normal, a, area);
            for (int k = 0; k < 3; k++) quadric[tri[3 * t + k]].Add(q);
            // An open edge also holds its ends to the plane standing on it, so a border that moves stays on its line.
            for (int k = 0; k < 3; k++)
            {
                int pa = tri[3 * t + k], pb = tri[3 * t + (k + 1) % 3];
                if (edgeUse[pa < pb ? (pa, pb) : (pb, pa)] != 1) continue;
                var along = places[pb] - places[pa];
                var side = Vector3.Cross(along, normal);
                float len = side.Length();
                if (len <= 0) continue;
                var bq = Quadric.Plane(side / len, places[pa], along.LengthSquared() * BorderWeight);
                quadric[pa].Add(bq); quadric[pb].Add(bq);
            }
        }

        // 4. Collapses, cheapest first; a place's candidates are refreshed when it or a neighbour moves.
        var scratch = new Dictionary<uint, uint>();
        var version = new int[n];
        var gone = new bool[n];
        var heap = new PriorityQueue<(int From, int To, int Version), float>();
        var candidates = new List<(float Cost, int To)>();
        void Consider(int u)
        {
            if (locked[u] || gone[u]) return;
            candidates.Clear();
            foreach (int t in around[u])
            {
                if (!alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tri[3 * t + k];
                    if (v == u || candidates.Exists(c => c.To == v)) continue;
                    var q = quadric[u]; q.Add(quadric[v]);
                    candidates.Add((q.Error(places[v]), v));
                }
            }
            candidates.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            foreach (var (cost, v) in candidates)
                if (Allowed(u, v)) { heap.Enqueue((u, v, version[u]), cost); return; }
        }

        // A vertex on an open edge moves only along it (to a vertex it shares an open edge with); any other anywhere.
        bool Allowed(int u, int v)
        {
            if (border[u])
            {
                int shared = 0;
                foreach (int t in around[u])
                    if (alive[t] && (tri[3 * t] == v || tri[3 * t + 1] == v || tri[3 * t + 2] == v)) shared++;
                if (shared != 1) return false;
            }
            return Wedges(u, v, null);
        }
        long tSetup = clock.ElapsedTicks;
        for (int u = 0; u < n; u++) Consider(u);
        long tConsider = clock.ElapsedTicks;
        int collapses = 0, requeues = 0, refusals = 0;

        int live = alive.Count(a => a);
        var wedge = new Dictionary<uint, uint>();
        float limit = maxError * maxError;
        var touched = new HashSet<int>();
        while (live > target && heap.TryDequeue(out var c, out float cost))
        {
            if (cost > limit) break;
            var (u, v, ver) = c;
            if (gone[u] || gone[v] || ver != version[u]) continue;
            // v's quadric may have grown since this was queued: ask again, and queue it again if it costs more now.
            var now = quadric[u]; now.Add(quadric[v]);
            float fresh = now.Error(places[v]);
            if (fresh > cost * 1.01f + 1e-9f) { heap.Enqueue((u, v, ver), fresh); requeues++; continue; }
            if (!Allowed(u, v) || !Collapsible(u, v)) { version[u]++; refusals++; continue; }
            collapses++;

            // Each of u's vertices (a seam has several) goes to v's vertex beside it across the edge (u, v).
            wedge.Clear();
            if (!Wedges(u, v, wedge)) { version[u]++; continue; }

            touched.Clear();
            foreach (int t in around[u])
            {
                if (!alive[t]) continue;
                bool hasV = tri[3 * t] == v || tri[3 * t + 1] == v || tri[3 * t + 2] == v;
                for (int k = 0; k < 3; k++) touched.Add(tri[3 * t + k]);
                if (hasV) { alive[t] = false; live--; continue; }
                for (int k = 0; k < 3; k++) if (tri[3 * t + k] == u) { tri[3 * t + k] = v; idx[3 * t + k] = wedge[idx[3 * t + k]]; }
                around[v].Add(t);
            }
            gone[u] = true;
            around[u].Clear();
            quadric[v].Add(quadric[u]);
            error = MathF.Max(error, MathF.Sqrt(MathF.Max(cost, 0f)));
            touched.Remove(u);
            foreach (int w in touched) { version[w]++; around[w].RemoveAll(t => !alive[t]); Consider(w); }
        }

        if (Environment.GetEnvironmentVariable("VITOMIR_SIMPLIFY_PROFILE") is not null)
            Console.Error.WriteLine($"simplify: {triangles} tris {n} places locked {locked.Count(x => x)} border {border.Count(x => x)} setup {Ms(tSetup):0.0}ms consider {Ms(tConsider - tSetup):0.0}ms loop {Ms(clock.ElapsedTicks - tConsider):0.0}ms collapses {collapses} requeues {requeues} refusals {refusals} left {live} heap {heap.Count}");
        static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var result = new List<uint>(live * 3);
        for (int t = 0; t < triangles; t++)
            if (alive[t]) { result.Add(idx[3 * t]); result.Add(idx[3 * t + 1]); result.Add(idx[3 * t + 2]); }
        return [.. result];

        // Whether every vertex of u (one per wedge of its fan: several on a texture seam or a hard edge) has a
        // vertex of v to go to — the one a triangle holding both uses, and only one such. A box's corner
        // (three wedges) collapsing along one of its edges fails: its third face does not touch v, so
        // corners and hard edges keep their shape, while a seam collapses along itself (meshoptimizer's rule).
        bool Wedges(int u, int v, Dictionary<uint, uint>? map)
        {
            map ??= scratch; map.Clear();
            foreach (int t in around[u])
            {
                if (!alive[t]) continue;
                int ku = -1, kv = -1;
                for (int k = 0; k < 3; k++) { if (tri[3 * t + k] == u) ku = k; else if (tri[3 * t + k] == v) kv = k; }
                if (kv < 0) continue;
                uint from = idx[3 * t + ku], to = idx[3 * t + kv];
                if (map.TryGetValue(from, out uint was) && was != to) return false;
                map[from] = to;
            }
            bool whole = map.Count > 0;
            foreach (int t in around[u])
            {
                if (!alive[t]) continue;
                for (int k = 0; k < 3; k++) if (tri[3 * t + k] == u && !map.ContainsKey(idx[3 * t + k])) whole = false;
            }
            if (whole) return true;
            // A faceted mesh (a vertex per face, as the towns' cut stone and timber are) has no wedge that
            // reaches across: each of u's vertices goes instead to the vertex of v whose normal is nearest its
            // own, if it is near enough (a face going on across the edge), and a corner still keeps its shape.
            map.Clear();
            var mine = new List<uint>(); var theirs = new List<uint>();
            foreach (int t in around[u])
                if (alive[t]) for (int k = 0; k < 3; k++) if (tri[3 * t + k] == u && !mine.Contains(idx[3 * t + k])) mine.Add(idx[3 * t + k]);
            foreach (int t in around[v])
                if (alive[t]) for (int k = 0; k < 3; k++) if (tri[3 * t + k] == v && !theirs.Contains(idx[3 * t + k])) theirs.Add(idx[3 * t + k]);
            foreach (uint a in mine)
            {
                float best = FacetDot; uint to = uint.MaxValue;
                foreach (uint b in theirs)
                {
                    float d = Vector3.Dot(vertices[a].Normal, vertices[b].Normal);
                    if (d > best) { best = d; to = b; }
                }
                if (to == uint.MaxValue) return false;
                map[a] = to;
            }
            return map.Count > 0;
        }

        // Moving u onto v may not turn any of u's other triangles over, nor fold one to nothing.
        bool Collapsible(int u, int v)
        {
            foreach (int t in around[u])
            {
                if (!alive[t]) continue;
                int a = tri[3 * t], b = tri[3 * t + 1], cc = tri[3 * t + 2];
                if (a == v || b == v || cc == v) continue;
                var before = Vector3.Cross(places[b] - places[a], places[cc] - places[a]);
                var pa = a == u ? places[v] : places[a];
                var pb = b == u ? places[v] : places[b];
                var pc = cc == u ? places[v] : places[cc];
                var after = Vector3.Cross(pb - pa, pc - pa);
                if (Vector3.Dot(before, after) <= 0.2f * before.Length() * after.Length()) return false;
            }
            return true;
        }
    }

    static IEnumerable<int> UsedVertices(uint[] indices, int count)
    {
        var used = new bool[count];
        foreach (var i in indices) used[i] = true;
        for (int v = 0; v < count; v++) if (used[v]) yield return v;
    }

    /// <summary>
    /// A clustered stand-in for a mesh: its vertices merged on a grid of <paramref name="size"/> (every
    /// vertex in one grid cell becomes one, at their mean position, with their mean normal and the first's
    /// texture coordinates), then the triangles that folded to nothing and the repeats dropped.
    /// </summary>
    public static (L2Vertex[] Vertices, uint[] Indices) Cluster(L2Vertex[] vertices, uint[] indices, float size)
    {
        var clusterOf = new Dictionary<(int, int, int), int>();
        var sum = new List<(Vector3 P, Vector3 N, int Count, L2Vertex First)>();
        var map = new int[vertices.Length];
        Array.Fill(map, -1);
        foreach (var i in indices)
        {
            if (map[i] >= 0) continue;
            var v = vertices[i];
            // The grid's layers are centred on whole multiples of the size in height, so ground near a level (z ≈ 0)
            // lies in the middle of a layer and is not split between two by its own small roughness.
            var key = ((int)MathF.Floor(v.Position.X / size), (int)MathF.Floor(v.Position.Y / size), (int)MathF.Floor(v.Position.Z / size + 0.5f));
            if (!clusterOf.TryGetValue(key, out int c)) { c = sum.Count; clusterOf[key] = c; sum.Add((Vector3.Zero, Vector3.Zero, 0, v)); }
            var s = sum[c];
            sum[c] = (s.P + v.Position, s.N + v.Normal, s.Count + 1, s.First);
            map[i] = c;
        }
        var outV = new L2Vertex[sum.Count];
        for (int c = 0; c < sum.Count; c++)
        {
            var s = sum[c];
            var v = s.First;
            v.Position = s.P / s.Count;
            v.Normal = s.N.LengthSquared() > 1e-12f ? Vector3.Normalize(s.N) : s.First.Normal;
            outV[c] = v;
        }
        var seen = new HashSet<(int, int, int)>();
        var outI = new List<uint>(indices.Length / 4);
        for (int t = 0; t < indices.Length / 3; t++)
        {
            int a = map[indices[3 * t]], b = map[indices[3 * t + 1]], c = map[indices[3 * t + 2]];
            if (a == b || b == c || a == c) continue;
            // The same triangle once, whichever corner it starts at (the other winding is the other face, kept).
            var key = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
            if (!seen.Add(key)) continue;
            outI.Add((uint)a); outI.Add((uint)b); outI.Add((uint)c);
        }
        return (outV, [.. outI]);
    }

    /// <summary>
    /// Drops the vertices no index uses and renumbers the rest in the order they are first used, which
    /// is also a fair order for the GPU's vertex cache.
    /// </summary>
    public static (L2Vertex[] Vertices, uint[] Indices) Compact(L2Vertex[] vertices, uint[] indices)
    {
        var map = new int[vertices.Length];
        Array.Fill(map, -1);
        var outV = new List<L2Vertex>();
        var outI = new uint[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            ref int m = ref map[indices[i]];
            if (m < 0) { m = outV.Count; outV.Add(vertices[indices[i]]); }
            outI[i] = (uint)m;
        }
        return ([.. outV], outI);
    }

    /// <summary>
    /// How many texture repeats a metre of the mesh holds (UV units per metre): the median over its
    /// triangles of √(UV area ÷ area), in the vertices' units. The texture streamer's measure of which
    /// mip a draw needs at a distance. 0 for a mesh with no area.
    /// </summary>
    public static float UvDensity(L2Vertex[] vertices, uint[] indices, int first, int count)
    {
        var d = new List<float>(count / 3);
        for (int t = first; t < first + count; t += 3)
        {
            var (a, b, c) = (vertices[indices[t]], vertices[indices[t + 1]], vertices[indices[t + 2]]);
            float area = Vector3.Cross(b.Position - a.Position, c.Position - a.Position).Length();
            if (area < 1e-8f) continue;
            var u1 = b.Uv0 - a.Uv0; var u2 = c.Uv0 - a.Uv0;
            float uv = MathF.Abs(u1.X * u2.Y - u1.Y * u2.X);
            d.Add(MathF.Sqrt(uv / area));
        }
        if (d.Count == 0) return 0f;
        d.Sort();
        return d[d.Count / 2];
    }

    /// <summary>How alike (the cosine of the angle) two faces' normals must be for a faceted vertex to move from one to the other.</summary>
    const float FacetDot = 0.8f;

    /// <summary>The most triangles a place may be shared by and still move.</summary>
    const int MaxFan = 24;

    /// <summary>How much an open edge's own plane weighs against the surface's, per unit of the edge's length squared.</summary>
    const float BorderWeight = 10f;

    /// <summary>A symmetric 4×4 quadric, its ten distinct terms.</summary>
    struct Quadric
    {
        float a2, ab, ac, ad, b2, bc, bd, c2, cd, d2;

        public static Quadric Plane(Vector3 n, Vector3 p, float weight)
        {
            float d = -Vector3.Dot(n, p);
            return new Quadric
            {
                a2 = n.X * n.X * weight, ab = n.X * n.Y * weight, ac = n.X * n.Z * weight, ad = n.X * d * weight,
                b2 = n.Y * n.Y * weight, bc = n.Y * n.Z * weight, bd = n.Y * d * weight,
                c2 = n.Z * n.Z * weight, cd = n.Z * d * weight, d2 = d * d * weight,
            };
        }

        public void Add(in Quadric q)
        {
            a2 += q.a2; ab += q.ab; ac += q.ac; ad += q.ad; b2 += q.b2; bc += q.bc; bd += q.bd; c2 += q.c2; cd += q.cd; d2 += q.d2;
        }

        /// <summary>The weighted sum of squared distances from <paramref name="p"/> to the planes, per unit of weight.</summary>
        public readonly float Error(Vector3 p)
        {
            float x = p.X, y = p.Y, z = p.Z;
            float e = a2 * x * x + 2 * ab * x * y + 2 * ac * x * z + 2 * ad * x
                    + b2 * y * y + 2 * bc * y * z + 2 * bd * y
                    + c2 * z * z + 2 * cd * z + d2;
            float w = a2 + b2 + c2;   // the total weight (each plane's n·n is 1)
            return w > 0 ? MathF.Max(e, 0f) / w : 0f;
        }
    }
}
