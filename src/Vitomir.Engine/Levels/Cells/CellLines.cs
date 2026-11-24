// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours.** The surfaces a line meets in a cell level. 1.1's sweep (<see cref="WorldSweep.Line"/>,
/// the line test) finds a level's surfaces in its sectors' collision trees, and a cell level's shell has
/// none; so for a cell level the sweep's things are found as ever and its surfaces are the collision
/// triangles (<c>COLL</c>) the line crosses, merged in by distance. Everything that asks the sweep about
/// surfaces then meets the new ground and walls: a falling or bouncing thing (<see cref="Physics"/> —
/// dropped loot, gibs, anything pushed), a projectile's flight and its hitscan, the sight line and an
/// explosion's reach.
/// </summary>
public static class CellLines
{
    /// <summary>
    /// Adds to <paramref name="found"/> (the sweep's things, nearest first) every solid triangle the
    /// segment from <paramref name="start"/> along the unit <paramref name="direction"/> meets within
    /// <paramref name="length"/>, as a surface contact: the distance and point where it is crossed, the
    /// triangle's normal turned to face the line's start (both faces count), whether it is walkable, and
    /// the shell's piece of ground under it for a walkable one (sector 0, its surface; −1 for a wall, which
    /// the sound lookup takes for no material). Kept in order of distance, a thing before a surface at the
    /// same distance, one contact where the line crosses several triangles at once through their shared
    /// edge or corner, and no more than the query's <see cref="WorldSweep.MaxResults"/>.
    /// </summary>
    public static void AddSurfaces(CellLevel cells, List<Contact> found, Vector3 start, Vector3 direction, float length)
    {
        var hits = new List<CellCollision.Hit>();
        Crossings(cells.Collision, start, direction, length, hits);
        if (hits.Count == 0) return;
        Contact? last = null;
        foreach (var h in hits)
        {
            var n = h.Normal;
            if (Vector3.Dot(n, direction) > 0f) n = -n;
            // A line through a shared edge or corner crosses every triangle there at once: one contact.
            if (last is { } was && MathF.Abs(was.Distance - h.Distance) < 1e-3f && Vector3.Dot(was.Normal, n) > 0.999f) continue;
            bool walk = (cells.Collision.Surfaces[h.Triangle] & L2Surface.Walk) != 0;
            int surface = walk ? CellSweep.SurfaceUnder(cells, h.Point) : -1;
            last = new Contact(ContactKind.Surface, h.Distance, h.Point, null, 0, surface, n, walk);
            found.Add(last.Value);
        }
        // A stable sort: the things were already in order, and a thing keeps its place before a surface at the same distance.
        var ordered = found.Select((c, i) => (c, i)).OrderBy(x => x.c.Distance).ThenBy(x => x.c.Kind == ContactKind.Surface ? 1 : 0).ThenBy(x => x.i)
                           .Select(x => x.c).Take(WorldSweep.MaxResults).ToList();
        found.Clear();
        found.AddRange(ordered);
    }

    /// <summary>Every solid triangle the segment crosses, nearest first (both faces).</summary>
    public static void Crossings(CellCollision c, Vector3 origin, Vector3 direction, float length, List<CellCollision.Hit> into)
    {
        if (c.Nodes.Length == 0 || length <= 0f) return;
        Span<int> stack = stackalloc int[128];
        int top = 0; stack[top++] = 0;
        while (top > 0)
        {
            int at = stack[--top];
            var node = c.Nodes[at];
            if (!Reaches(origin, direction, node.Min, node.Max, length)) continue;
            if (!node.IsLeaf) { stack[top++] = node.Right; stack[top++] = at + 1; continue; }
            for (int t = node.First; t < node.First + node.Count; t++)
            {
                if ((c.Surfaces[t] & L2Surface.Solid) == 0) continue;
                var (a, b, cc) = c.Corners(t);
                if (Crosses(origin, direction, a, b, cc, out float d) && d <= length)
                    into.Add(new CellCollision.Hit(d, t, origin + direction * d, c.Normal(t)));
            }
        }
        into.Sort((x, y) => x.Distance.CompareTo(y.Distance));
    }

    static bool Reaches(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float length)
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

    /// <summary>Möller–Trumbore, both faces, from the start on.</summary>
    static bool Crosses(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
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
