// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours.** The two sweeps the game casts through the level that a cell level answers from its
/// collision triangles, 1.1's collision trees having nothing in them: where the mouse pointer's ray
/// meets the world (the click to walk, <c>WorldSweep.Pointer</c>) and how far behind the player the
/// game camera can stand (<c>CameraSweep.GameCamera</c>).
/// </summary>
public static class CellSweep
{
    /// <summary>
    /// The first solid triangle the pointer's ray meets, and — when it is ground — the walk map's
    /// piece of ground under that point (the shell's surface and path node, which are one index).
    /// </summary>
    public static Hit? Pointer(CellLevel cells, Vector3 origin, Vector3 direction)
    {
        var d = direction.LengthSquared() > 0 ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        if (cells.Collision.Ray(origin, d, CellShell.FarClip) is not { } met) return null;
        int node = (cells.Collision.Surfaces[met.Triangle] & Formats.L2.L2Surface.Walk) != 0 ? SurfaceUnder(cells, met.Point) : -1;
        return new Hit(met.Point, 0, node, node, node >= 0, met.Distance);
    }

    /// <summary>
    /// The walkable piece of ground under a point: the loaded navigation-mesh polygon whose outline
    /// holds it with its floor within <see cref="CellLevel.FloorWindow"/> of the point's height (Detour's
    /// nearest-polygon search over the loaded tiles); −1 when none does.
    /// </summary>
    public static int SurfaceUnder(CellLevel cells, Vector3 point) => cells.Stream.NodeUnder(point, CellLevel.FloorWindow);

    /// <summary>
    /// The game camera's pull-in on a cell level: how far along <paramref name="direction"/> from
    /// <paramref name="origin"/> the eye can sit — the centre of a sphere of <paramref name="radius"/>
    /// (the camera's own, <see cref="CameraSweep.Radius"/>) swept out towards where the camera wants to
    /// be, stopped where it first touches a solid triangle (<see cref="CellSphereSweep"/>), as 1.1's fat
    /// ray stops on its sector trees (the distance is the centre's, nothing further taken off).
    /// Null when nothing is in the way.
    /// </summary>
    public static float? Camera(CellLevel cells, Vector3 origin, Vector3 direction, float length, float radius)
    {
        var d = direction.LengthSquared() > 0 ? Vector3.Normalize(direction) : Vector3.UnitZ;
        return CellSphereSweep.Sweep(cells.Collision, origin, d, length, radius) is { } met ? Math.Clamp(met.Distance, 0f, length) : null;
    }
}
