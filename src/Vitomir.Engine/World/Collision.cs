// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What is solid.
//
// Every sector in a level carries a **collision tree** — the `u16 n, u16[4][n]` block the format
// notes called "BSP nodes (collision only)" and left undecoded until now. It is a binary space
// partition over the sector's own surface planes: an internal node splits on a plane, a leaf says
// whether the space there is solid.
//
// The trees are per sector, and sectors are convex volumes bounded by their own planes, with their
// normals pointing inward. Sampled 48 units above every walkable surface in the game, a point is
// inside exactly one sector's volume 99.7% of the time, and in the rare case it is inside two they
// agree about it. So the model is simple: find the sector a point is in, then ask that sector's
// tree.
//
// This is what stops a character standing inside a wall. It is not what keeps them on the ground —
// the walk map does that (`WalkMap`), because the collision tree is perfectly happy for you to walk
// off a ledge and there is no gravity yet to make that a fall.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>A level's solid space, as the engine's own collision trees describe it.</summary>
public sealed class Collision(NilLevel level)
{
    /// <summary>
    /// How much a point may be outside a sector's boundary planes and still count as inside it.
    /// The planes and the surfaces that meet them are the same numbers, so this only absorbs the
    /// rounding.
    /// </summary>
    public const float Slack = 0.25f;

    /// <summary>
    /// The heights above a walker's feet at which its body is tested. A wall runs the whole height
    /// of a room, so what matters is that something is tested between knee and shoulder; these are
    /// ours, not the engine's.
    /// </summary>
    public static readonly float[] Body = [16f, 48f, 80f];

    public NilLevel Level { get; } = level;

    /// <summary>
    /// The sector whose volume contains a point, or −1. Sectors are convex and very nearly
    /// disjoint, so the one a walker was in last is nearly always the one it is in now — which is
    /// what <paramref name="hint"/> is for, and what keeps this from being a search.
    /// </summary>
    public int SectorAt(Vector3 p, int hint = -1)
    {
        if (hint >= 0 && hint < Level.Sectors.Length && Inside(Level.Sectors[hint], p)) return hint;
        // Then the sectors next door, through the hint's portals.
        if (hint >= 0 && hint < Level.Sectors.Length)
            foreach (var portal in Level.Sectors[hint].Portals)
                if (portal.NeighbourSector < (uint)Level.Sectors.Length &&
                    Inside(Level.Sectors[portal.NeighbourSector], p)) return (int)portal.NeighbourSector;
        for (int i = 0; i < Level.Sectors.Length; i++)
            if (i != hint && Inside(Level.Sectors[i], p)) return i;
        return -1;
    }

    /// <summary>
    /// How far inside a sector's volume a point is: the least of its distances in front of the sector's
    /// planes (negative outside).
    /// </summary>
    public float Depth(int sector, Vector3 p)
    {
        if (sector < 0 || sector >= Level.Sectors.Length) return float.NegativeInfinity;
        float least = float.PositiveInfinity;
        foreach (var plane in Level.Sectors[sector].SectorPlanes)
            least = MathF.Min(least, Vector3.Dot(plane.Normal, p) - plane.Dist);
        return least;
    }

    /// <summary>Whether a point is in a sector's volume (with <see cref="Slack"/>).</summary>
    public bool Contains(int sector, Vector3 p) => sector >= 0 && sector < Level.Sectors.Length && Inside(Level.Sectors[sector], p);

    static bool Inside(NilSector sector, Vector3 p)
    {
        foreach (var plane in sector.SectorPlanes)
            if (Vector3.Dot(plane.Normal, p) - plane.Dist < -Slack) return false;
        return true;
    }

    /// <summary>
    /// Whether a point is inside something. A point in **no** sector is not called solid: sectors
    /// do not quite tile the world — 0.3% of points a body's height above a walkable floor fall
    /// outside every sector's planes, and most of those are in doorways, where the two sectors meet
    /// — and calling those solid walls people out of doorways. Staying inside the level is the walk
    /// map's job, and it does it by keeping the walker on known ground.
    /// </summary>
    public bool Solid(Vector3 p, int hint = -1)
    {
        int sector = SectorAt(p, hint);
        return sector >= 0 && Solid(Level.Sectors[sector], p);
    }

    /// <summary>Whether one sector's tree calls a point solid.</summary>
    public static bool Solid(NilSector sector, Vector3 p)
    {
        int count = sector.BspCount;
        if (count == 0) return false;                       // no tree: nothing in this sector is solid
        for (int at = 0, guard = 0; guard < 256; guard++)
        {
            var node = sector.Bsp(at);
            if (node.IsLeaf) return node.IsSolid;
            if (node.Plane >= sector.SurfacePlanes.Length) return false;
            var plane = sector.SurfacePlanes[node.Plane];
            int next = Vector3.Dot(plane.Normal, p) - plane.Dist > 0 ? node.Front : node.Back;
            // The tree is stored in pre-order, so a child is always further along than its parent.
            if (next <= at || next >= count) return false;
            at = next;
        }
        return false;
    }

    /// <summary>
    /// Whether a walker standing with its feet at <paramref name="feet"/> is clear of everything
    /// solid. Its body is sampled at <see cref="Body"/> heights, at the centre and at eight points
    /// around it, which is a cheap stand-in for sweeping a real capsule through the trees.
    /// </summary>
    public bool Fits(Vector3 feet, float radius, int hint = -1)
    {
        foreach (float h in Body)
        {
            var at = feet with { Z = feet.Z + h };
            if (Solid(at, hint)) return false;
            if (radius <= 0f) continue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.Tau / 8f;
                if (Solid(at + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0), hint)) return false;
            }
        }
        return true;
    }

    /// <summary>How many of the level's sectors have a collision tree at all.</summary>
    public int SectorsWithTrees => Level.Sectors.Count(s => s.BspCount > 0);

    /// <summary>
    /// **The flier's sight ray**, reached only through the flier's line test and only by a flier choosing
    /// its next perch. True when nothing stops a ray from
    /// <paramref name="from"/>, in <paramref name="sector"/>, along <paramref name="direction"/>, for
    /// <paramref name="reach"/> units of the direction's own length.
    ///
    /// It walks the sector graph rather than the level: a sector's surfaces first, then every
    /// neighbour whose portal **plane** the ray crosses from the front — the portal's corners are not
    /// looked at, so the walk goes wider than the ray does — each sector once (the stamp at
    /// a stamp on each sector, bumped per call). A surface stops the ray only when the ray meets
    /// it **from its front** and the point where it meets the plane is inside the surface;
    /// sky included, which is what matters out of doors.
    /// </summary>
    public bool Clear(Vector3 from, int sector, Vector3 direction, float reach)
    {
        if (Levels.Cells.CellLevel.Of(Level) is { } cells) return Levels.Cells.CellSceneSweeps.Clear(cells, from, direction, reach);   // a cooked level's triangles (ours)
        if (sector < 0 || sector >= Level.Sectors.Length) return true;
        return Clear(from, sector, direction, reach, new HashSet<int>());
    }

    bool Clear(Vector3 from, int index, Vector3 direction, float reach, HashSet<int> seen)
    {
        seen.Add(index);
        var sector = Level.Sectors[index];
        for (int i = 0; i < sector.Surfaces.Length; i++)
        {
            var surface = sector.Surfaces[i];
            var plane = sector.SurfacePlanes[surface.Plane];
            if (!Crosses(Vector3.Dot(plane.Normal, from) - plane.Dist, plane.Normal, direction, reach, out float t))
                continue;
            if (surface.NumVerts >= 3 && Pick.Inside(sector, surface, plane, from + direction * t)) return false;
        }
        foreach (var portal in sector.Portals)
        {
            int next = (int)portal.NeighbourSector;
            if (next < 0 || next >= Level.Sectors.Length || seen.Contains(next) || portal.Quad.Length == 0) continue;
            // The portal's plane only.
            if (!Crosses(Vector3.Dot(from - portal.Quad[0], portal.Normal), portal.Normal, direction, reach, out _))
                continue;
            if (!Clear(from, next, direction, reach, seen)) return false;
        }
        return true;
    }

    /// <summary>The plane test both halves share (for a surface and for a portal).</summary>
    static bool Crosses(float distance, Vector3 normal, Vector3 direction, float reach, out float t)
    {
        t = 0f;
        if (MathF.Abs(distance) <= PlaneEpsilon) distance = 0f;
        else if (distance < 0f) return false;                          // behind it: one-sided
        if (distance > reach) return false;
        float closing = -Vector3.Dot(direction, normal);
        if (closing <= 0f) return false;
        if (distance > 0f)
        {
            if (closing * reach < distance) return false;
            t = MathF.Max(distance / closing, 0f);
        }
        return true;
    }

    /// <summary>How close to a plane counts as on it, for the trace above.</summary>
    public const float PlaneEpsilon = 1e-5f;
}
