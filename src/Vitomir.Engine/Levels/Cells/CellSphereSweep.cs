// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours.** A sphere swept along a line through a cell level's collision triangles (<c>COLL</c>): where
/// its centre is when the sphere first touches one — the game camera's pull-in on a cell level
/// (<see cref="CellSweep.Camera"/>), the fat ray 1.1 casts through its sector trees with the camera's
/// radius of 10 (<c>World.CameraSweep</c>). Exact: each triangle is met by its face, its edges or
/// its corners, whichever the sphere reaches first, so the eye is never nearer than the radius to any
/// surface, a house's corner included (a thin ray passes the corner and lets the picture's near edge
/// into the wall). Both faces count, as in the rest of <c>COLL</c>'s queries; a triangle the sphere
/// already touches at the start counts at once only when the line heads into it.
/// </summary>
public static class CellSphereSweep
{
    /// <summary>
    /// The first triangle carrying any of <paramref name="mask"/>'s flags that a sphere of
    /// <paramref name="radius"/>, its centre moving from <paramref name="origin"/> along the unit
    /// <paramref name="direction"/>, touches within <paramref name="length"/>: the distance its centre
    /// has travelled then, the triangle, the point of contact and the triangle's normal turned towards the
    /// sphere. Null when it touches nothing.
    /// </summary>
    public static CellCollision.Hit? Sweep(CellCollision cells, Vector3 origin, Vector3 direction, float length, float radius,
                                           L2Surface mask = L2Surface.Solid)
    {
        if (cells.Nodes.Length == 0) return null;
        float best = length; int bestT = -1;
        var grow = new Vector3(radius);
        Span<int> stack = stackalloc int[128];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = cells.Nodes[at];
            if (!SlabHit(origin, direction, node.Min - grow, node.Max + grow, best)) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((cells.Surfaces[t] & mask) == 0) continue;
                var (a, b, c) = cells.Corners(t);
                if (Triangle(origin, direction, best, radius, a, b, c) is { } d && (d < best || bestT < 0)) { best = d; bestT = t; }
            }
        }
        if (bestT < 0) return null;
        var centre = origin + direction * best;
        var (ta, tb, tc) = cells.Corners(bestT);
        var touch = CellCollision.ClosestOnTriangle(centre, ta, tb, tc);
        var n = cells.Normal(bestT);
        if (Vector3.Dot(centre - touch, n) < 0f) n = -n;
        return new CellCollision.Hit(best, bestT, touch, n);
    }

    /// <summary>
    /// How far a sphere of radius <paramref name="r"/> whose centre moves from <paramref name="o"/> along
    /// the unit <paramref name="d"/> travels, within <paramref name="length"/>, before it touches the
    /// triangle (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>); null if it does not.
    /// The face is tried first (its plane met at distance r, the contact inside the triangle); failing that,
    /// the three edges (a line at distance r from the edge's segment) and the three corners (a point at
    /// distance r). Ericson, <i>Real-Time Collision Detection</i>, 5.5.
    /// </summary>
    public static float? Triangle(Vector3 o, Vector3 d, float length, float r, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        float nl = n.Length();
        if (nl < 1e-12f) return null;
        n /= nl;
        float s0 = Vector3.Dot(o - a, n);
        if (s0 < 0f) { n = -n; s0 = -s0; }                  // the face towards the start: both faces count
        float dn = Vector3.Dot(d, n);
        if (s0 <= r)
        {
            // Touching the plane already: at once, if the touch is on the face and the line heads into it.
            if (dn < 0f && Inside(o - n * s0, a, b, c, n)) return 0f;
        }
        else if (dn < 0f)
        {
            float t = (s0 - r) / -dn;
            if (t > length) return null;                      // the plane itself is out of reach, so is all of it
            if (Inside(o + d * t - n * r, a, b, c, n)) return t;
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

    /// <summary>The line against the cylinder of radius r round the segment p–q, within the segment's span.</summary>
    static float? Edge(Vector3 o, Vector3 d, float r, Vector3 p, Vector3 q)
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
            if (qb >= 0f) return null;                        // inside the cylinder already and leaving it
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

    /// <summary>The line against the sphere of radius r round a corner.</summary>
    static float? Corner(Vector3 o, Vector3 d, float r, Vector3 v)
    {
        var m = o - v;
        float b = Vector3.Dot(m, d), c = Vector3.Dot(m, m) - r * r;
        if (c < 0f) return b < 0f ? 0f : null;               // inside it already: only when heading in
        float disc = b * b - c;
        if (disc < 0f) return null;
        float t = -b - MathF.Sqrt(disc);
        return t >= 0f ? t : null;
    }

    /// <summary>Whether the segment from <paramref name="o"/> along <paramref name="d"/> for <paramref name="length"/> meets a box.</summary>
    static bool SlabHit(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float length)
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
}
