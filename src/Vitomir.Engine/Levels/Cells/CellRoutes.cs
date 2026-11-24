// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: routes on a cell level by Detour** — the design's Improved Pathfinding for new levels
/// (section 7): a search over the navigation mesh's polygons and the string-pulled path through
/// them, with no 5,000-unit cap and no 255-step limit. 1.1's levels keep 1.1's search
/// (<see cref="PathFinder"/>, which hands a cell level's searches here).
///
/// A route comes back the way 1.1's does, so the walker follows it unchanged: the points to cross in
/// order, each with the piece of ground it leads onto, not counting where the walker stands or the
/// goal, which the walker walks at last (<c>Player.Walk</c>). Detour's corners hug the walls at the
/// navigation mesh's own margin (<see cref="NavSettings.Radius"/>).
/// </summary>
public sealed class CellRoutes
{
    /// <summary>How many polygons a search may pass through, and corners a route may have.</summary>
    public const int MaxPolygons = 4096, MaxCorners = 1024;

    readonly CellStream stream;
    readonly IDtQueryFilter filter = new DtQueryDefaultFilter();
    readonly float scale;

    /// <summary>Routes over a streamed level's navigation mesh: its loaded tiles, linked across the cells' borders.</summary>
    public CellRoutes(CellStream stream, float scale)
    {
        this.stream = stream;
        this.scale = scale;
    }

    RcVec3f ToRecast(Vector3 engine) => CellCook.ToRecast(engine / scale);

    /// <summary>
    /// A route from <paramref name="from"/> on walk node <paramref name="fromNode"/> to
    /// <paramref name="to"/> on <paramref name="toNode"/>: <see cref="PathResult.Straight"/> when the
    /// string-pulled path is one straight line, <see cref="PathResult.None"/> when the goal cannot be
    /// reached (a partial path is none, as 1.1's search has no partial answer).
    /// </summary>
    public PathResult Find(Vector3 from, int fromNode, Vector3 to, int toNode, out Waypoint[] path)
    {
        path = [];
        if (fromNode == toNode && fromNode >= 0) return PathResult.Straight;
        long start = stream.RefOf(fromNode), goal = stream.RefOf(toNode);
        if (start == 0 || goal == 0) return PathResult.None;          // off the loaded ground
        var a = ToRecast(from); var b = ToRecast(to);
        Span<long> polys = new long[MaxPolygons];
        var query = stream.Query;
        var status = query.FindPath(start, goal, a, b, filter, polys, out int count, MaxPolygons);
        if (status.Failed() || count == 0 || polys[count - 1] != goal) return PathResult.None;
        Span<DtStraightPath> corners = new DtStraightPath[MaxCorners];
        status = query.FindStraightPath(a, b, polys, count, corners, out int n, MaxCorners, 0);
        if (status.Failed() || n < 2) return PathResult.None;
        if (n == 2) return PathResult.Straight;
        var route = new Waypoint[n - 2];
        for (int i = 1; i < n - 1; i++)
        {
            var c = corners[i];
            int node = stream.SlotOf(c.refs, out bool walk);
            route[i - 1] = new Waypoint(node >= 0 && walk ? node : toNode, CellCook.FromRecast(c.pos.X, c.pos.Y, c.pos.Z, scale));
        }
        path = route;
        return PathResult.Path;
    }
}
