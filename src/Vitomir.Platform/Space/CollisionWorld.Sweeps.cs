// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sphere sweep is taken from src/Vitomir.Engine/Levels/Cells/CellSphereSweep.cs at b4185ef, reworked for the platform;
// the capsule sweep, the overlap and the push out are ours.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Space;

/// <summary>
/// A vertical capsule: the sphere of <see cref="Radius"/> swept along the
/// segment from <see cref="Lower"/> (the lower sphere's centre) straight up for <see cref="Segment"/>
/// metres. A body of height h and radius r standing with its feet at f is
/// <c>new Capsule(f + (0, 0, r), h − 2r, r)</c> (<see cref="OfBody"/>).
/// </summary>
public readonly record struct Capsule(Vector3 Lower, float Segment, float Radius)
{
    /// <summary>The upper sphere's centre.</summary>
    public Vector3 Upper => Lower + new Vector3(0, 0, Segment);

    /// <summary>The capsule of a body of <paramref name="height"/> and <paramref name="radius"/> whose feet (its lowest point) are at <paramref name="feet"/>.</summary>
    public static Capsule OfBody(Vector3 feet, float radius, float height) =>
        new(feet + new Vector3(0, 0, radius), MathF.Max(0f, height - 2f * radius), radius);

    /// <summary>The same capsule moved by <paramref name="by"/>.</summary>
    public Capsule Moved(Vector3 by) => this with { Lower = Lower + by };
}

public sealed partial class CollisionWorld
{
    // ---------------------------------------------------------------- sphere

    /// <summary>
    /// The first triangle carrying any of <paramref name="mask"/>'s flags that a sphere of
    /// <paramref name="radius"/>, its centre moving from <paramref name="origin"/> along the unit
    /// <paramref name="direction"/>, touches within <paramref name="length"/>: the distance its centre
    /// has travelled then, the triangle, the point of contact, the contact normal and the face normal
    /// (both turned towards the sphere). Exact: each triangle is met by its face, its edges or its
    /// corners, whichever the sphere reaches first (Ericson, <i>Real-Time Collision Detection</i>, 5.5);
    /// a triangle the sphere already touches at the start counts at once only when the line heads into
    /// it. The cameras' pull-in (the earlier build's <c>CellSweep.Camera</c>, 1.1's fat ray).
    /// </summary>
    public CollisionHit? SphereSweep(Vector3 origin, Vector3 direction, float length, float radius, L2Surface mask = L2Surface.Solid)
    {
        if (Nodes.Length == 0) return null;
        float best = length; int bestT = -1;
        var grow = new Vector3(radius);
        Span<int> stack = stackalloc int[StackDepth];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (!SweepMath.SlabHit(origin, direction, node.Min - grow, node.Max + grow, best)) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & mask) == 0) continue;
                var (a, b, c) = Corners(t);
                if (SweepMath.SphereTriangle(origin, direction, best, radius, a, b, c) is { } d && (d < best || bestT < 0)) { best = d; bestT = t; }
            }
        }
        if (bestT < 0) return null;
        var centre = origin + direction * best;
        var (ta, tb, tc) = Corners(bestT);
        var touch = ClosestOnTriangle(centre, ta, tb, tc);
        var face = Normal(bestT);
        if (Vector3.Dot(centre - touch, face) < 0f) face = -face;
        var gap = centre - touch;
        float gl = gap.Length();
        return new CollisionHit(best, bestT, touch, gl > 1e-6f ? gap / gl : face, face);
    }

    // ---------------------------------------------------------------- capsule

    /// <summary>
    /// The first triangle carrying any of <paramref name="mask"/>'s flags that a vertical
    /// <paramref name="capsule"/> moved along the unit <paramref name="direction"/> touches within
    /// <paramref name="length"/>: how far it travelled, the triangle, the contact point on it, the contact
    /// normal (from the contact towards the capsule's axis) and the face normal turned to the capsule.
    /// Null when it touches nothing.
    ///
    /// <b>Exact</b> (ours): a capsule is the set of points within its radius of its segment, so it first
    /// touches a triangle at one of these features, each solved in closed form and the earliest kept —
    /// (1) either end's sphere against the triangle's face, edges and corners (the sphere sweep);
    /// (2) the segment's inside against each of the triangle's edges: two lines whose distance changes
    /// linearly as the capsule moves, met when it equals the radius with both closest points inside their
    /// segments; (3) the segment's inside against each corner: a point against the moving cylinder. A face
    /// can be touched by the segment's inside only when the segment lies parallel to it, and then its edges
    /// or the end spheres are touched at the same moment, so (1) to (3) are the whole contact. As with the
    /// sphere, a triangle already touched at the start stops the capsule at once only when it moves into it.
    /// </summary>
    public CollisionHit? CapsuleSweep(in Capsule capsule, Vector3 direction, float length, L2Surface mask = L2Surface.Solid)
    {
        if (Nodes.Length == 0) return null;
        var p = capsule.Lower; float h = capsule.Segment, r = capsule.Radius;
        var u = Vector3.UnitZ;
        float best = length; int bestT = -1;
        var below = new Vector3(r, r, h + r); var above = new Vector3(r);
        Span<int> stack = stackalloc int[StackDepth];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (!SweepMath.SlabHit(p, direction, node.Min - below, node.Max + above, best)) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & mask) == 0) continue;
                var (a, b, c) = Corners(t);
                if (SweepMath.CapsuleTriangle(p, u, h, direction, best, r, a, b, c) is { } d && (d < best || bestT < 0)) { best = d; bestT = t; }
            }
        }
        if (bestT < 0) return null;
        var lower = p + direction * best;
        var (ta, tb, tc) = Corners(bestT);
        SweepMath.SegmentTriangle(lower, lower + u * h, ta, tb, tc, out var onSeg, out var onTri);
        var face = Normal(bestT);
        if (Vector3.Dot(onSeg - onTri, face) < 0f) face = -face;
        var gap = onSeg - onTri;
        float gl = gap.Length();
        return new CollisionHit(best, bestT, onTri, gl > 1e-6f ? gap / gl : face, face);
    }

    // ---------------------------------------------------------------- overlap

    /// <summary>
    /// How far the <paramref name="capsule"/> is inside the deepest triangle carrying
    /// <paramref name="mask"/> it overlaps: the radius less the distance from its segment to the triangle,
    /// or 0 when it is clear of them all.
    /// </summary>
    public float Penetration(in Capsule capsule, L2Surface mask = L2Surface.Solid) => Penetration(capsule, mask, out _);

    /// <summary>As <see cref="Penetration(in Capsule, L2Surface)"/>, also naming the deepest triangle (−1 when none).</summary>
    public float Penetration(in Capsule capsule, L2Surface mask, out int triangle)
    {
        triangle = -1;
        if (Nodes.Length == 0) return 0f;
        var p = capsule.Lower; var q = capsule.Upper; float r = capsule.Radius;
        var min = new Vector3(p.X - r, p.Y - r, p.Z - r); var max = new Vector3(q.X + r, q.Y + r, q.Z + r);
        float deepest = 0f;
        Span<int> stack = stackalloc int[StackDepth];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = Nodes[at];
            if (!SweepMath.Overlaps(node.Min, node.Max, min, max)) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((Surfaces[t] & mask) == 0) continue;
                var (a, b, c) = Corners(t);
                float d = MathF.Sqrt(SweepMath.SegmentTriangle(p, q, a, b, c, out _, out _));
                if (r - d > deepest) { deepest = r - d; triangle = t; }
            }
        }
        return deepest;
    }

    /// <summary>Whether the capsule is more than <paramref name="tolerance"/> inside any triangle carrying <paramref name="mask"/>.</summary>
    public bool Overlaps(in Capsule capsule, float tolerance = 0f, L2Surface mask = L2Surface.Solid) => Penetration(capsule, mask) > tolerance;

    /// <summary>
    /// Pushes a capsule out of every triangle carrying <paramref name="mask"/> it is inside (ours): for
    /// each, by the closest points of its segment and the triangle, along the line between them, until it
    /// is <paramref name="skin"/> clear; a segment that passes through a triangle is pushed along the face's
    /// normal to whichever side is nearer. Triangles are taken in the tree's order and each push moves the
    /// capsule at once, so a corner's two walls are both left; up to <paramref name="passes"/> rounds are
    /// made, until one pushes nothing. Returns how far the capsule moved in all, and leaves in
    /// <paramref name="capsule"/> where it ended.
    /// </summary>
    public Vector3 Depenetrate(ref Capsule capsule, float skin, L2Surface mask = L2Surface.Solid, int passes = 4)
    {
        var start = capsule.Lower;
        if (Nodes.Length == 0) return Vector3.Zero;
        float r = capsule.Radius, h = capsule.Segment;
        var u = Vector3.UnitZ;
        Span<int> stack = stackalloc int[StackDepth];
        for (int pass = 0; pass < passes; pass++)
        {
            bool pushed = false;
            var p0 = capsule.Lower;
            var min = new Vector3(p0.X - r - skin, p0.Y - r - skin, p0.Z - r - skin);
            var max = new Vector3(p0.X + r + skin, p0.Y + r + skin, p0.Z + h + r + skin);
            int top = 0; stack[top++] = 0;
            while (top > 0)
            {
                int at = stack[--top];
                var node = Nodes[at];
                if (!SweepMath.Overlaps(node.Min, node.Max, min, max)) continue;
                if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
                for (int t = node.First; t < node.First + node.Count; t++)
                {
                    if ((Surfaces[t] & mask) == 0) continue;
                    var (a, b, c) = Corners(t);
                    var p = capsule.Lower; var q = p + u * h;
                    float d2 = SweepMath.SegmentTriangle(p, q, a, b, c, out var onSeg, out var onTri);
                    if (d2 >= (r - 1e-5f) * (r - 1e-5f)) continue;
                    float d = MathF.Sqrt(d2);
                    Vector3 push;
                    if (d > 1e-6f) push = (onSeg - onTri) / d * (r + skin - d);
                    else
                    {
                        // Through the face: out along its normal, to the nearer side.
                        var n = Normal(t);
                        float sp = Vector3.Dot(p - a, n), sq = Vector3.Dot(q - a, n);
                        float plus = r + skin - MathF.Min(sp, sq), minus = r + skin + MathF.Max(sp, sq);
                        push = plus <= minus ? n * plus : -n * minus;
                    }
                    capsule = capsule.Moved(push);
                    pushed = true;
                }
            }
            if (!pushed) break;
        }
        return capsule.Lower - start;
    }
}

/// <summary>The closed forms the sweeps are made of (ours, after Ericson, <i>Real-Time Collision Detection</i>, chapter 5).</summary>
public static class SweepMath
{
    /// <summary>
    /// A shape already touching something stops at once only when it moves into it by more than this (the
    /// cosine of its way against the surface): sliding along a face it rests on, rounding may not stop it.
    /// </summary>
    public const float Grazing = 1e-5f;

    /// <summary>
    /// A triangle whose doubled area is less than this share of its longest edge squared (its height under a
    /// hundred-thousandth of that edge) is a sliver, met by its edges and corners only.
    /// </summary>
    public const float SliverRatio = 1e-5f;

    /// <summary>
    /// How far a sphere of radius <paramref name="r"/> whose centre moves from <paramref name="o"/> along
    /// the unit <paramref name="d"/> travels, within <paramref name="length"/>, before it touches the
    /// triangle (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>); null if it does not.
    /// The face is tried first (its plane met at distance r, the contact inside the triangle); failing that,
    /// the three edges and the three corners. Taken from the earlier build's <c>CellSphereSweep.Triangle</c>, with its
    /// back-face fault mended.
    /// </summary>
    public static float? SphereTriangle(Vector3 o, Vector3 d, float length, float r, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        float nl = n.Length();
        if (nl < 1e-12f) return null;
        // A sliver — its corners all but in a line, as a cook leaves along a wall's foot or top — has no face
        // to speak of: its normal is rounding, and a face test with it met spheres nowhere near (a body
        // walking in line with one on P1_OTDY was held in the air). Only its edges and corners are met.
        float longest = MathF.Max((b - a).LengthSquared(), MathF.Max((c - b).LengthSquared(), (a - c).LengthSquared()));
        if (nl > SliverRatio * longest)
        {
            n /= nl;
            // Inside is asked with the winding's normal: the earlier build's copy asked it with the normal turned to the
            // sphere, so a sphere coming at a triangle's back face was not met by the face (only, too late, by an edge).
            var winding = n;
            float s0 = Vector3.Dot(o - a, n);
            if (s0 < 0f) { n = -n; s0 = -s0; }                  // the face towards the start: both faces count
            float dn = Vector3.Dot(d, n);
            if (s0 <= r)
            {
                // Touching the plane already: at once, if the touch is on the face and the line heads into it.
                if (dn < -Grazing && Inside(o - n * s0, a, b, c, winding)) return 0f;
            }
            else if (dn < 0f)
            {
                float t = (s0 - r) / -dn;
                if (t > length) return null;                      // the plane itself is out of reach, so is all of it
                if (Inside(o + d * t - n * r, a, b, c, winding)) return t;
            }
        }
        float? best = null;
        Keep(ref best, Edge(o, d, r, a, b));
        Keep(ref best, Edge(o, d, r, b, c));
        Keep(ref best, Edge(o, d, r, c, a));
        Keep(ref best, Corner(o, d, r, a));
        Keep(ref best, Corner(o, d, r, b));
        Keep(ref best, Corner(o, d, r, c));
        return best is { } found && found <= length ? found : null;
    }

    /// <summary>
    /// How far a capsule — the sphere of radius <paramref name="r"/> swept along the segment from
    /// <paramref name="p"/> along the unit <paramref name="u"/> for <paramref name="h"/> — moving along the
    /// unit <paramref name="d"/> travels before it touches a triangle, within <paramref name="length"/>
    /// (see <see cref="CollisionWorld.CapsuleSweep"/> for the features).
    /// </summary>
    public static float? CapsuleTriangle(Vector3 p, Vector3 u, float h, Vector3 d, float length, float r, Vector3 a, Vector3 b, Vector3 c)
    {
        float? best = SphereTriangle(p, d, length, r, a, b, c);
        if (h > 1e-6f)
        {
            float reach = best ?? length;
            Keep(ref best, SphereTriangle(p + u * h, d, reach, r, a, b, c));
            Keep(ref best, SegmentEdge(p, u, h, d, r, a, b));
            Keep(ref best, SegmentEdge(p, u, h, d, r, b, c));
            Keep(ref best, SegmentEdge(p, u, h, d, r, c, a));
            Keep(ref best, SegmentCorner(p, u, h, d, r, a));
            Keep(ref best, SegmentCorner(p, u, h, d, r, b));
            Keep(ref best, SegmentCorner(p, u, h, d, r, c));
        }
        return best is { } found && found <= length ? found : null;
    }

    /// <summary>
    /// The inside of the moving segment (from <paramref name="p"/> along <paramref name="u"/> for
    /// <paramref name="h"/>, moving along <paramref name="d"/>) against the edge from <paramref name="a"/> to
    /// <paramref name="b"/>: the lines' distance is linear in the travel; met at <paramref name="r"/> when
    /// the closest points lie inside both segments. Parallel lines are left to the ends and corners.
    /// </summary>
    static float? SegmentEdge(Vector3 p, Vector3 u, float h, Vector3 d, float r, Vector3 a, Vector3 b)
    {
        var e = b - a;
        var n = Vector3.Cross(u, e);
        float nl = n.Length(), el = e.Length();
        if (el < 1e-9f || nl < 1e-6f * el) return null;
        n /= nl;
        float g0 = Vector3.Dot(p - a, n), gd = Vector3.Dot(d, n);
        if (MathF.Abs(g0) <= r)
        {
            if (!InsideBoth(p, u, h, a, e)) return null;
            return (g0 > 0f ? gd < -Grazing : g0 < 0f ? gd > Grazing : MathF.Abs(gd) > Grazing) ? 0f : null;   // touching already: only when moving in
        }
        if (g0 * gd >= 0f) return null;                        // moving away, or alongside
        float t = (g0 > 0f ? g0 - r : g0 + r) / -gd;
        if (t < 0f) return null;
        return InsideBoth(p + d * t, u, h, a, e) ? t : null;
    }

    /// <summary>Whether the closest points of the line through <paramref name="p"/> along unit <paramref name="u"/> and the line through <paramref name="a"/> along <paramref name="e"/> lie inside the segments [0, h] and [0, 1].</summary>
    static bool InsideBoth(Vector3 p, Vector3 u, float h, Vector3 a, Vector3 e)
    {
        var w0 = p - a;
        float b = Vector3.Dot(u, e), c = Vector3.Dot(e, e), dd = Vector3.Dot(u, w0), ee = Vector3.Dot(e, w0);
        float denom = c - b * b;
        if (denom <= 1e-12f) return false;
        float s = (b * ee - c * dd) / denom, w = (ee - b * dd) / denom;
        return s >= 0f && s <= h && w >= 0f && w <= 1f;
    }

    /// <summary>A corner <paramref name="v"/> against the moving segment's cylinder (its inside only; the ends are the spheres').</summary>
    static float? SegmentCorner(Vector3 p, Vector3 u, float h, Vector3 d, float r, Vector3 v)
    {
        var q0 = v - p;
        var q0p = q0 - Vector3.Dot(q0, u) * u;
        var dp = d - Vector3.Dot(d, u) * u;
        float qq = Vector3.Dot(q0p, q0p), bq = Vector3.Dot(q0p, dp);
        if (qq < r * r)
        {
            float s0 = Vector3.Dot(q0, u);
            if (s0 < 0f || s0 > h) return null;
            return bq > Grazing * MathF.Sqrt(qq) ? 0f : null;  // inside already: only when moving in
        }
        float a = Vector3.Dot(dp, dp);
        if (a < 1e-12f || bq <= 0f) return null;
        float disc = bq * bq - a * (qq - r * r);
        if (disc < 0f) return null;
        float t = (bq - MathF.Sqrt(disc)) / a;
        if (t < 0f) return null;
        float s = Vector3.Dot(v - p - d * t, u);
        return s >= 0f && s <= h ? t : null;
    }

    static void Keep(ref float? best, float? t)
    {
        if (t is { } v && (best is not { } was || v < was)) best = v;
    }

    /// <summary>Whether a point on the triangle's plane lies inside it (each edge's inward side, by the normal).</summary>
    static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        const float E = -1e-4f;
        return Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= E * (b - a).Length()
            && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= E * (c - b).Length()
            && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= E * (a - c).Length();
    }

    /// <summary>The line from <paramref name="o"/> along unit <paramref name="d"/> against the cylinder of radius r round the segment p–q, within the segment's span (inside already and heading in: 0).</summary>
    public static float? Edge(Vector3 o, Vector3 d, float r, Vector3 p, Vector3 q)
    {
        var e = q - p; var m = o - p;
        float ee = Vector3.Dot(e, e), ed = Vector3.Dot(e, d), em = Vector3.Dot(e, m);
        if (ee < 1e-12f) return null;
        float qa = ee - ed * ed;                              // |d| = 1
        if (qa < 1e-9f * ee) return null;                     // along the edge: the corners answer
        float qb = ee * Vector3.Dot(m, d) - em * ed;
        float qc = ee * (Vector3.Dot(m, m) - r * r) - em * em;
        float t;
        if (qc < 0f)
        {
            if (qb >= -Grazing * ee * r) return null;         // inside the cylinder already and leaving it (or grazing)
            t = 0f;
        }
        else
        {
            float disc = qb * qb - qa * qc;
            if (disc < 0f) return null;
            t = (-qb - MathF.Sqrt(disc)) / qa;
            if (t < 0f) return null;
        }
        float s = (em + t * ed) / ee;
        return s >= 0f && s <= 1f ? t : null;
    }

    /// <summary>The line from <paramref name="o"/> along unit <paramref name="d"/> against the sphere of radius r round <paramref name="v"/> (inside already and heading in: 0).</summary>
    public static float? Corner(Vector3 o, Vector3 d, float r, Vector3 v)
    {
        var m = o - v;
        float b = Vector3.Dot(m, d), c = Vector3.Dot(m, m) - r * r;
        if (c < 0f) return b < -Grazing * r ? 0f : null;  // inside already: only when moving in
        float disc = b * b - c;
        if (disc < 0f) return null;
        float t = -b - MathF.Sqrt(disc);
        return t >= 0f ? t : null;
    }

    /// <summary>
    /// The line from <paramref name="o"/> along unit <paramref name="d"/> against a vertical capsule (lower
    /// centre <paramref name="lower"/>, segment <paramref name="segment"/>, radius <paramref name="r"/>):
    /// how far until it touches, or null. Also a capsule swept against a capsule: the moving one's lower
    /// centre against the static one grown by the moving one's radius and lengthened downwards by its
    /// segment (their Minkowski sum).
    /// </summary>
    public static float? RayCapsule(Vector3 o, Vector3 d, float length, Vector3 lower, float segment, float r)
    {
        var top = lower + new Vector3(0, 0, segment);
        float? best = null;
        if (segment > 1e-6f) Keep(ref best, Edge(o, d, r, lower, top));
        Keep(ref best, Corner(o, d, r, lower));
        if (segment > 1e-6f) Keep(ref best, Corner(o, d, r, top));
        return best is { } t && t <= length ? t : null;
    }

    /// <summary>The squared distance between the segment p–q and a triangle, with the closest points (Ericson 5.1.10: a crossing, else an end against the face or the segment against an edge).</summary>
    public static float SegmentTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, out Vector3 onSeg, out Vector3 onTri)
    {
        var pq = q - p;
        if (SegmentMeets(p, pq, a, b, c, out float tx))
        {
            onSeg = onTri = p + pq * tx;
            return 0f;
        }
        onTri = CollisionWorld.ClosestOnTriangle(p, a, b, c); onSeg = p;
        float best = Vector3.DistanceSquared(p, onTri);
        var cq = CollisionWorld.ClosestOnTriangle(q, a, b, c);
        float dq = Vector3.DistanceSquared(q, cq);
        if (dq < best) { best = dq; onSeg = q; onTri = cq; }
        Try(ref best, ref onSeg, ref onTri, p, q, a, b);
        Try(ref best, ref onSeg, ref onTri, p, q, b, c);
        Try(ref best, ref onSeg, ref onTri, p, q, c, a);
        return best;

        static void Try(ref float best, ref Vector3 onSeg, ref Vector3 onTri, Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            float d = SegmentSegment(p, q, a, b, out var c1, out var c2);
            if (d < best) { best = d; onSeg = c1; onTri = c2; }
        }
    }

    /// <summary>Whether the segment from <paramref name="o"/> along <paramref name="d"/> (its whole length) crosses the triangle, and where (0 to 1).</summary>
    static bool SegmentMeets(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
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
        return t >= 0f && t <= 1f;
    }

    /// <summary>The squared distance between segments p1–q1 and p2–q2, with the closest points (Ericson 5.1.9).</summary>
    public static float SegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        const float Eps = 1e-12f;
        var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= Eps && e <= Eps) { s = t = 0; }
        else if (a <= Eps) { s = 0; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Eps) { t = 0; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > Eps ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        return Vector3.DistanceSquared(c1, c2);
    }

    /// <summary>Whether the segment from <paramref name="o"/> along <paramref name="d"/> for <paramref name="length"/> meets a box.</summary>
    public static bool SlabHit(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float length)
    {
        float tmin = 0f, tmax = length;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-12f) { if (oa < lo || oa > hi) return false; continue; }
            float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1); tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        return true;
    }

    public static bool Overlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
        aMax.X >= bMin.X && aMin.X <= bMax.X && aMax.Y >= bMin.Y && aMin.Y <= bMax.Y && aMax.Z >= bMin.Z && aMin.Z <= bMax.Z;
}
