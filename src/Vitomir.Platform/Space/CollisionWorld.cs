// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Levels/Cells/CellCollision.cs and CellCollision.Merge.cs at b4185ef, reworked for the platform.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Space;

/// <summary>
/// What a query met: how far along it (metres, measured along the query's direction), which triangle,
/// the point of contact on the triangle, the <b>contact normal</b> (the unit vector from the contact
/// towards the query's shape: for a ray or a face touched flat it is the face's own normal turned to
/// the query, at an edge or a corner it points from the edge to the shape) and the triangle's face
/// normal turned the same way.
/// </summary>
public readonly record struct CollisionHit(float Distance, int Triangle, Vector3 Point, Vector3 Normal, Vector3 FaceNormal);

/// <summary>
/// The level's solid space: the collision triangles of every cell of an L2
/// package (<c>COLL</c>), in metres with Z up, each with its <see cref="L2Surface"/> flags and material,
/// and one bounding-volume tree over them; and the questions the simulation asks of it — what a ray
/// meets first (<see cref="Ray"/>), where a sphere or a capsule swept along a line first touches
/// (<see cref="SphereSweep"/>, <see cref="CapsuleSweep"/>), how deep a capsule is inside anything
/// (<see cref="Penetration"/>) and how to push it out (<see cref="Depenetrate"/>), and the walkable floor
/// under a point (<see cref="Floor"/>).
///
/// Both faces of a triangle count everywhere, as in the cook's own queries. The tree is the cook's (each
/// cell's, built once by <see cref="Build"/>), the cells' trees hung under a small tree of their own by
/// <see cref="Merge"/>, so loading is a copy. Every query walks the tree with a small stack on the
/// machine stack and allocates nothing.
/// </summary>
public sealed partial class CollisionWorld
{
    /// <summary>Triangles a leaf of the tree holds at most.</summary>
    public const int LeafSize = 4;

    /// <summary>How deep the trees may be walked (a merged level's depth is the cells' plus a few).</summary>
    const int StackDepth = 128;

    public Vector3[] Vertices { get; }
    public int[] Indices { get; }
    public L2Surface[] Surfaces { get; }
    public ushort[] Materials { get; }
    public L2BvhNode[] Nodes { get; }
    public int Triangles => Indices.Length / 3;

    /// <summary>A cell's <c>COLL</c> chunk as it is: metres, the cook's tree.</summary>
    public CollisionWorld(L2Collision chunk)
        : this(chunk.Vertices, chunk.Indices, chunk.Surfaces, chunk.Materials, chunk.Nodes) { }

    CollisionWorld(Vector3[] vertices, int[] indices, L2Surface[] surfaces, ushort[] materials, L2BvhNode[] nodes)
    {
        Vertices = vertices; Indices = indices; Surfaces = surfaces; Materials = materials; Nodes = nodes;
    }

    /// <summary>An empty space: nothing to stand on or meet.</summary>
    public static CollisionWorld Empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// The level's areas not to be climbed in (ours, <see cref="LevelClimb"/>), kept with the triangles so that
    /// everything that reads lips from this space (<see cref="Bodies.TraversalProbe"/>) obeys them; none unless given by
    /// <see cref="WithClimb"/>. The triangles' own mark is <see cref="L2Surface.NoClimb"/>.
    /// </summary>
    public LevelClimb Climb { get; private init; } = LevelClimb.None;

    /// <summary>This space with <paramref name="climb"/>'s areas (the same triangles and tree, not copied).</summary>
    public CollisionWorld WithClimb(LevelClimb climb) => new(Vertices, Indices, Surfaces, Materials, Nodes) { Climb = climb, Ladders = Ladders };

    /// <summary>
    /// The level's ladders (ours, <see cref="LevelLadders"/>), kept with the triangles so that the motor, which is
    /// given this space, finds them (<c>BodyMotor.Ladder.cs</c>); none unless given by <see cref="WithLadders"/>.
    /// </summary>
    public LevelLadders Ladders { get; private init; } = LevelLadders.None;

    /// <summary>This space with <paramref name="ladders"/> (the same triangles and tree, not copied).</summary>
    public CollisionWorld WithLadders(LevelLadders ladders) => new(Vertices, Indices, Surfaces, Materials, Nodes) { Climb = Climb, Ladders = ladders };

    /// <summary>
    /// Whether a lip whose top is triangle <paramref name="triangle"/> at <paramref name="point"/> may not be climbed:
    /// the triangle carries <see cref="L2Surface.NoClimb"/>, the point is inside an area not to be climbed in, or
    /// it lies on a ladder's climbing face (<see cref="LevelLadders.OnFace"/>: a rung or a rail, which use climbs, never a
    /// jump's moves), so a level need not mark its ladders' meshes.
    /// </summary>
    public bool NoClimb(int triangle, Vector3 point) =>
        (triangle >= 0 && triangle < Surfaces.Length && (Surfaces[triangle] & L2Surface.NoClimb) != 0) || Climb.NoClimbAt(point)
        || Ladders.OnFace(point);

    /// <summary>
    /// Whether no body is to stand on triangle <paramref name="triangle"/> (ours):
    /// it carries <see cref="L2Surface.NoStand"/>. The cook sets it from a layout's meshes and its areas marked
    /// <c>"stand": "none"</c> alike, so the triangle is the whole answer;
    /// a triangle out of range (−1: none, another body) is not marked.
    /// </summary>
    public bool NoStand(int triangle) =>
        triangle >= 0 && triangle < Surfaces.Length && (Surfaces[triangle] & L2Surface.NoStand) != 0;

    /// <summary>
    /// A space made from triangles in memory (tests, generated levels): <paramref name="indices"/>' triangles
    /// sorted into a tree by <see cref="Build"/>. Each triangle is <see cref="L2Surface.Solid"/>, and also
    /// <see cref="L2Surface.Walk"/> when it faces up at least as steeply as <paramref name="walkNormalZ"/>
    /// (the cook's test), unless <paramref name="surfaces"/> are given.
    /// </summary>
    public static CollisionWorld FromTriangles(Vector3[] vertices, int[] indices, L2Surface[]? surfaces = null, ushort[]? materials = null,
                                               float walkNormalZ = 0.7f)
    {
        int count = indices.Length / 3;
        if (surfaces is null)
        {
            surfaces = new L2Surface[count];
            for (int t = 0; t < count; t++)
            {
                var (a, b, c) = (vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                float l = n.Length();
                surfaces[t] = L2Surface.Solid | (l > 0 && MathF.Abs(n.Z / l) >= walkNormalZ ? L2Surface.Walk : 0);
            }
        }
        return new CollisionWorld(Build(vertices, indices, surfaces, materials ?? new ushort[count]));
    }

    /// <summary>
    /// The chunk the cook writes: <paramref name="indices"/>' triangles sorted into a bounding-volume
    /// tree (split at the median of the longest axis of the triangles' centres, <see cref="LeafSize"/>
    /// to a leaf), with their flags and materials carried along. Taken from the cook's
    /// <c>CellCollision.Build</c> unchanged, so a tree built here is the tree a package holds.
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

    /// <summary>
    /// The cells' collision as one: their triangles and trees laid end to end (indices and nodes moved
    /// along) and a small tree built over the cells' own root boxes — split at the median of the longest
    /// axis of their middles, as the cook's tree is — with each cell's whole tree hung under it where a
    /// leaf would be. So every query asks every cell whose box it meets and there is no seam at a cell's
    /// border. A triangle's number in the result is its cell's first plus its own.
    /// </summary>
    public static CollisionWorld Merge(IReadOnlyList<CollisionWorld> parts)
    {
        var live = parts.Where(p => p.Nodes.Length > 0).ToList();
        if (live.Count == 0) return Empty;
        if (live.Count == 1) return live[0];
        int vertices = live.Sum(p => p.Vertices.Length), triangles = live.Sum(p => p.Triangles);
        var v = new Vector3[vertices];
        var idx = new int[triangles * 3];
        var surf = new L2Surface[triangles];
        var mats = new ushort[triangles];
        var firstTri = new int[live.Count];
        int vAt = 0, tAt = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            p.Vertices.CopyTo(v, vAt);
            for (int k = 0; k < p.Indices.Length; k++) idx[tAt * 3 + k] = p.Indices[k] + vAt;
            p.Surfaces.CopyTo(surf, tAt);
            p.Materials.CopyTo(mats, tAt);
            firstTri[i] = tAt;
            vAt += p.Vertices.Length; tAt += p.Triangles;
        }
        var nodes = new List<L2BvhNode>(live.Sum(p => p.Nodes.Length) + 2 * live.Count);
        var order = Enumerable.Range(0, live.Count).ToArray();
        void Build(int from, int to)
        {
            if (to - from == 1)
            {
                int i = order[from], nodeBase = nodes.Count;
                foreach (var n in live[i].Nodes)
                    nodes.Add(n.IsLeaf ? n with { First = n.First + firstTri[i] } : n with { First = n.Right + nodeBase });
                return;
            }
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            var cmin = min; var cmax = max;
            for (int k = from; k < to; k++)
            {
                var root = live[order[k]].Nodes[0];
                min = Vector3.Min(min, root.Min); max = Vector3.Max(max, root.Max);
                var c = (root.Min + root.Max) * 0.5f;
                cmin = Vector3.Min(cmin, c); cmax = Vector3.Max(cmax, c);
            }
            var extent = cmax - cmin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(order, from, to - from, Comparer<int>.Create((a, b) =>
                Axis((live[a].Nodes[0].Min + live[a].Nodes[0].Max) * 0.5f, axis).CompareTo(Axis((live[b].Nodes[0].Min + live[b].Nodes[0].Max) * 0.5f, axis))));
            int at = nodes.Count;
            nodes.Add(default);
            int mid = (from + to) / 2;
            Build(from, mid);
            int right = nodes.Count;
            Build(mid, to);
            nodes[at] = new L2BvhNode(min, max, right, 0);
        }
        Build(0, live.Count);
        return new CollisionWorld(v, idx, surf, mats, [.. nodes]);
    }

    /// <summary>The box round every triangle (the root's), or an empty box.</summary>
    public (Vector3 Min, Vector3 Max) Bounds => Nodes.Length > 0 ? (Nodes[0].Min, Nodes[0].Max) : (Vector3.Zero, Vector3.Zero);

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
    /// <paramref name="length"/>; both faces count, and the normal is turned towards the ray's origin.
    /// </summary>
    public CollisionHit? Ray(Vector3 origin, Vector3 direction, float length, L2Surface mask = L2Surface.Solid)
    {
        if (Nodes.Length == 0) return null;
        var inverse = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        float best = length; int bestT = -1;
        Span<int> stack = stackalloc int[StackDepth];
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
        if (bestT < 0) return null;
        var n = Normal(bestT);
        if (Vector3.Dot(n, direction) > 0) n = -n;
        return new CollisionHit(best, bestT, origin + direction * best, n, n);
    }

    /// <summary>
    /// The height of the ground under (<paramref name="x"/>, <paramref name="y"/>) nearest
    /// <paramref name="z"/> within <paramref name="window"/> above or below it, or null: every triangle
    /// carrying <paramref name="mask"/> (by default the cook's <see cref="L2Surface.Walk"/>) that the
    /// vertical line meets in that span is a candidate, so a floor under an awning or a bridge over a
    /// street is found at the height asked about.
    /// </summary>
    public float? Floor(float x, float y, float z, float window, L2Surface mask = L2Surface.Walk)
    {
        if (Nodes.Length == 0) return null;
        float lo = z - window, hi = z + window;
        float? best = null;
        Span<int> stack = stackalloc int[StackDepth];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (x < node.Min.X || x > node.Max.X || y < node.Min.Y || y > node.Max.Y || node.Max.Z < lo || node.Min.Z > hi) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & mask) == 0) continue;
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

    /// <summary>The point of a triangle nearest another point (Ericson, <i>Real-Time Collision Detection</i>, 5.1.5).</summary>
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
