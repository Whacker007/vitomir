// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// **The floor under a point, as `SpawnThingNear` looks for it** (called
    /// with the point 60 above the anchor's feet and 60 as the reach). It starts
    /// from what the **anchor** stands on, not from the point, and looks in at most two sectors:
    /// <list type="number">
    /// <item><b>the anchor's own surface</b> (the walk surface its feet last
    /// found): the point over that polygon is on it, whatever the height (the probe's first loop);</item>
    /// <item><b>the other walkable faces of that surface's sector</b> (flag 0x1000): one under the
    /// point, its plane 0 to 128 below it, is the floor (the probe's second loop);</item>
    /// <item><b>the sector the point is in</b> — the anchor's sector if its planes hold the point,
    /// else the first sector of the world, in order, whose planes do — but
    /// only when that is <b>not</b> the sector already searched: its walkable faces, by a ray straight
    /// down 60 + 128 long.</item>
    /// </list>
    /// Null when none of it finds a floor. So a point over the floor of a <b>third</b> sector is
    /// refused, and the monastery shows it (Walk the Abyss, as in 1.1):
    /// its floors are thin sectors under taller room sectors, a point 60 up is in the room sector,
    /// which has no floor of its own, and so the search round an anchor finds only the floor of the
    /// sector the anchor's own surface is in. Round Christof it found the portal's place; round the
    /// portal — which, made at run time, has no surface (<see cref="WorldThing.Unsurfaced"/>) — it
    /// found nothing in any of the eight directions, and the party came back <b>on the portal</b>,
    /// both of them on the one spot, facing its way.
    ///
    /// <paramref name="reach"/> is the caller's (the probe's fourth argument): 60 from
    /// `SpawnThingNear`, 100 from the AI's stray (<c>Minds.Stray</c>).
    ///
    /// Vitomir's walk nodes stand for the walkable faces (a node's <see cref="WalkNode.Sector"/> is
    /// the face's sector); among several under the point the first in the list is taken, as the
    /// engine takes the first face in its sector's list.
    /// </summary>
    public Vector3? FloorUnder(Vector3 point, WorldThing anchor, float reach = ProbeReach)
    {
        if (Walk is not { } map) return null;
        var xy = new Vector2(point.X, point.Y);
        // What the anchor stands on: a walker's node. A thing with no walker has found no surface.
        var standing = anchor.Unsurfaced ? null
                     : ReferenceEquals(Player?.Thing, anchor) ? Player : anchor.Mind?.Walker ?? anchor.Walker;
        int surface = standing is { Node: >= 0 } s && map.IsGround(s.Node) ? s.Node : -1;
        // A walker with no walker of ours yet — a character with no mind — was stood on its surface
        // when its scene was loaded (the scene's finalize grounds every move type 1 thing); a prop
        // never was, and a thing made at run time has not been yet.
        if (standing is null && !anchor.Unsurfaced && anchor.MoveType == WorldThing.MoveWalker)
            surface = map.Find(anchor.Position);
        int searched = -1;
        if (surface >= 0)
        {
            var own = map.Nodes[surface];
            if (own.Contains(xy)) return point with { Z = own.Height(xy.X, xy.Y) };
            searched = own.Sector;
            if (First(map, searched, xy, point.Z, ProbeBelow) is { } same) return same;
        }
        int sector = SectorHolding(point, (int)anchor.Sector);
        if (sector < 0 || sector == searched) return null;
        return First(map, sector, xy, point.Z, reach + ProbeBelow);
    }

    /// <summary>How far below the point a face of the anchor's sector may be, and the ray's length on top of it (`SpawnThingNear` passes 60).</summary>
    public const float ProbeBelow = 128f, ProbeReach = 60f;

    static Vector3? First(WalkMap map, int sector, Vector2 xy, float z, float below)
    {
        foreach (var node in map.Nodes)
        {
            if (node.Sector != sector || node.Outline.Length == 0 || !node.Contains(xy)) continue;
            float h = node.Height(xy.X, xy.Y);
            if (h <= z && z - h <= below) return new Vector3(xy.X, xy.Y, h);
        }
        return null;
    }

    /// <summary>
    /// The sector whose planes hold a point, the way the probe asks: the hint if it does
    /// (no slack), else the first in the world's order — a sector with no planes holds
    /// everything — else the hint unchanged.
    /// </summary>
    int SectorHolding(Vector3 p, int hint)
    {
        var sectors = Level.Sectors;
        if (hint >= 0 && hint < sectors.Length && Holds(sectors[hint], p)) return hint;
        for (int i = 0; i < sectors.Length; i++)
            if (Holds(sectors[i], p)) return i;
        return hint;
    }

    static bool Holds(NilSector sector, Vector3 p)
    {
        foreach (var plane in sector.SectorPlanes)
            if (Vector3.Dot(plane.Normal, p) - plane.Dist < 0f) return false;
        return true;
    }
}
