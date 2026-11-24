// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sector a thing is in, found as the engine finds it.
//
// Every move of a thing in the world is `simThing::SetPosition` or `SetTransform`,
// and both do the same with the new point: ask the sector search which sector it is in, handing it
// the sector the thing was in, and move the thing between the sectors' lists when that
// changed. **The point is the thing's own position — its feet.** Not its middle, not a height above
// the ground: `&this->transform.pos`, as it is.
//
// That matters more than it sounds. The sector is what a thing is lit from (the light picker
// starts with its own sector's lights), and the level compiler's sectors do not stop at the floor:
// in the monastery's corridors the boundary between the sector a character stands in and the one
// above it is **4 units over the floor** (sectors 43 and 44 up to z = −248, the floor at −252, sectors
// 169 and 172 above). 1.1's own save says where it put the party (`SaveGames\SP4`, the record's
// sector, as an index into the world's sector array): Christof in 43 and Wilhem in 44, the
// sectors of their feet. Vitomir's walker had put them in the sector of a point 16 units up (ours),
// 169 and 172 — and 172 is a sector with no surfaces and no lights, so Wilhem was drawn black where
// 1.1 lights him from the corridor's torches (as screenshots show).
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **The sector search**: the sector a point is in given the one it was in, exactly: the
/// given sector when the point is inside every one of its boundary planes; otherwise the **first
/// sector in the level's order** that holds it; and, when none does, the given sector again — it
/// never answers "nowhere". The inside test is the engine's own, strict and without slack: a point is out
/// of a sector as soon as it is behind one of its planes by any amount (<c>n·p − d &lt; 0</c>), and
/// on a plane is in. A sector with no boundary planes holds every point.
/// </summary>
/// <remarks>
/// <see cref="Collision.SectorAt"/> asks the same question the way Vitomir's collision wants it —
/// a quarter-unit of slack, the hint's neighbours before the rest, and −1 for nowhere (ours). The
/// thing's own sector, which decides its lights, is this one.
/// </remarks>
public static class SectorSearch
{
    /// <summary>The sector <paramref name="point"/> is in, starting from <paramref name="hint"/> (−1 for none).</summary>
    public static int Find(NilLevel level, Vector3 point, int hint)
    {
        var sectors = level.Sectors;
        if (hint >= 0 && hint < sectors.Length && Holds(sectors[hint], point)) return hint;
        for (int i = 0; i < sectors.Length; i++)
            if (Holds(sectors[i], point)) return i;
        return hint >= 0 && hint < sectors.Length ? hint : -1;
    }

    /// <summary>
    /// The sector a walker is in once it stands at <paramref name="feet"/> — <see cref="Find"/> from
    /// the one it was in, or −1 when the place has no collision loaded (a geometry-less test).
    /// </summary>
    public static int Of(Collision? solid, Vector3 feet, int hint) =>
        solid is null ? -1 : Find(solid.Level, feet, hint);

    /// <summary>A point is in a sector when it is on the inner side of, or on, every one of its boundary planes.</summary>
    public static bool Holds(NilSector sector, Vector3 point)
    {
        foreach (var plane in sector.SectorPlanes)
            if (Vector3.Dot(plane.Normal, point) - plane.Dist < 0f) return false;
        return true;
    }
}
