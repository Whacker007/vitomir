// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The mesh's assembly is taken from src/Vitomir.Engine/Levels/Cells/CellStream.cs and the routes from
// src/Vitomir.Engine/Levels/Cells/CellRoutes.cs at b4185ef, reworked for the platform (metres, every tile at once).
using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Navigation;

/// <summary>
/// The level's navigation mesh (<i>navigation plans,
/// the body walks</i>): every cell's <c>NAVM</c> tile — Detour's own bytes, baked by the cook from the same
/// collision with the body's numbers — added to one Detour mesh, which links the tiles' border polygons
/// to their neighbours'. It answers routes (a list of corner points in metres, string-pulled through the
/// polygons) and random reachable points, which are drawn from the platform's own seeded streams so they
/// are the same on every run.
///
/// Detour's frame is Recast's: metres, <b>Y up</b> — a point (x, y, z) of the level is (x, z, −y) to it
/// (<see cref="ToRecast"/>). Only polygons the cook marked as ground (flags ≠ 0) are walked: Detour's
/// default filter. A query keeps its buffers, so asking for a route allocates nothing of ours (Detour's
/// nearest-polygon search makes a small object per call).
/// </summary>
public sealed class LevelNavigation
{
    /// <summary>How many polygons a search may pass through, and corners a route may have.</summary>
    public const int MaxPolygons = 4096, MaxCorners = 256;

    /// <summary>How far round a point a polygon is looked for (metres: across, then up and down).</summary>
    public static readonly Vector2 DefaultReach = new(1.0f, 2.0f);

    readonly IDtQueryFilter filter = new DtQueryDefaultFilter();
    readonly long[] polys = new long[MaxPolygons];
    readonly DtStraightPath[] straight = new DtStraightPath[MaxCorners];
    readonly RngForDetour random = new();

    public DtNavMesh Mesh { get; }
    public DtNavMeshQuery Query { get; }
    /// <summary>Tiles in the mesh.</summary>
    public int Tiles { get; }
    /// <summary>Polygons in all its tiles.</summary>
    public int Polygons { get; }
    /// <summary>
    /// The agent the mesh was baked for: the first cell's <c>NAVM</c> record, as <see cref="Space.LevelSpace.Open"/>
    /// reads it; null for a mesh made otherwise. A snag check compares it with the body it drives.
    /// </summary>
    public NavAgent? Agent { get; init; }
    /// <summary>Whether the cells' records disagree (a package put together from cooks for different agents); <see cref="Agent"/> is then the first cell's.</summary>
    public bool MixedAgents { get; init; }

    /// <summary>A mesh of <paramref name="tiles"/> laid out by <paramref name="parameters"/>.</summary>
    public LevelNavigation(DtNavMeshParams parameters, IReadOnlyList<DtMeshData> tiles, int maxVertsPerPoly = 6)
    {
        Mesh = new DtNavMesh();
        var status = Mesh.Init(in parameters, maxVertsPerPoly);
        if (status.Failed()) throw new InvalidOperationException($"the navigation mesh would not start ({status})");
        foreach (var tile in tiles)
        {
            if (Mesh.AddTile(tile, 0, 0, out _).Failed()) continue;
            Tiles++;
            Polygons += tile.header.polyCount;
        }
        Query = new DtNavMeshQuery(Mesh);
    }

    /// <summary>A <c>NAVM</c> tile's bytes back into Detour's model (taken from the earlier build's <c>CellCook.ReadTile</c>).</summary>
    public static DtMeshData ReadTile(byte[] tile, int maxVertsPerPoly = 6)
    {
        using var r = new BinaryReader(new MemoryStream(tile));
        return new DtMeshDataReader().Read(r, maxVertsPerPoly);
    }

    /// <summary>A point of the level (metres, Z up) in Detour's frame (metres, Y up).</summary>
    public static RcVec3f ToRecast(Vector3 m) => new(m.X, m.Z, -m.Y);

    /// <summary>A point in Detour's frame in the level's.</summary>
    public static Vector3 FromRecast(RcVec3f r) => new(r.X, -r.Z, r.Y);

    /// <summary>
    /// The walkable polygon nearest <paramref name="point"/> within <paramref name="reach"/> (across, up and
    /// down; <see cref="DefaultReach"/> when null) and the nearest point on it, or 0.
    /// </summary>
    public long Nearest(Vector3 point, out Vector3 onMesh, Vector2? reach = null)
    {
        var r = reach ?? DefaultReach;
        var status = Query.FindNearestPoly(ToRecast(point), new RcVec3f(r.X, r.Y, r.X), filter, out long found, out var nearest, out _);
        onMesh = found != 0 ? FromRecast(nearest) : point;
        return status.Succeeded() ? found : 0;
    }

    /// <summary>
    /// A route from <paramref name="from"/> to <paramref name="to"/> (metres): the corners to walk to in
    /// order, written into <paramref name="corners"/> — the string-pulled path's points after the start, the
    /// last being the goal's nearest point on the mesh. Returns how many there are (at most the span's
    /// length; a longer route is cut there, to be asked again from its last corner), or −1 when there is
    /// no full route (either end off the mesh, or the goal not reachable: a partial path is none).
    /// </summary>
    public int Route(Vector3 from, Vector3 to, Span<Vector3> corners)
    {
        long start = Nearest(from, out var a), goal = Nearest(to, out var b);
        if (start == 0 || goal == 0) return -1;
        var ra = ToRecast(a); var rb = ToRecast(b);
        int count;
        if (start == goal) { polys[0] = start; count = 1; }
        else
        {
            var status = Query.FindPath(start, goal, ra, rb, filter, polys, out count, MaxPolygons);
            if (status.Failed() || count == 0 || polys[count - 1] != goal) return -1;
        }
        var s = Query.FindStraightPath(ra, rb, polys, count, straight, out int n, MaxCorners, 0);
        if (s.Failed() || n < 1) return -1;
        int written = 0;
        for (int i = 1; i < n && written < corners.Length; i++) corners[written++] = FromRecast(straight[i].pos);
        if (written == 0 && corners.Length > 0) corners[written++] = b;
        return written;
    }

    /// <summary>A route as a new list (for tools and tests; the systems use the span form).</summary>
    public List<Vector3>? Route(Vector3 from, Vector3 to)
    {
        Span<Vector3> buffer = new Vector3[MaxCorners];
        int n = Route(from, to, buffer);
        return n < 0 ? null : [.. buffer[..n]];
    }

    /// <summary>A random point anywhere on the walkable mesh (by area), drawn from <paramref name="rng"/>.</summary>
    public bool RandomPoint(Rng rng, out Vector3 point)
    {
        random.Source = rng;
        var status = Query.FindRandomPoint(filter, random, out long found, out var at);
        point = FromRecast(at);
        return status.Succeeded() && found != 0;
    }

    /// <summary>
    /// A random point reachable over the mesh from <paramref name="centre"/> (the polygons Detour reaches
    /// from the one under it within about <paramref name="radius"/>), drawn from <paramref name="rng"/>.
    /// </summary>
    public bool RandomReachable(Vector3 centre, float radius, Rng rng, out Vector3 point)
    {
        point = centre;
        long start = Nearest(centre, out var on);
        if (start == 0) return false;
        random.Source = rng;
        var status = Query.FindRandomPointAroundCircle(start, ToRecast(on), radius, filter, random, out long found, out var at);
        if (status.Failed() || found == 0) return false;
        point = FromRecast(at);
        return true;
    }

    /// <summary>
    /// The platform's <see cref="Rng"/> as Detour's random source, so its random points are drawn from the
    /// world's seeded streams: the same on every run and machine.
    /// </summary>
    sealed class RngForDetour : IRcRand
    {
        public Rng Source = new(0);
        public float Next() => Source.NextFloat();
        public double NextDouble() => (Source.NextULong() >> 11) * (1.0 / (1UL << 53));
        public int NextInt32() => (int)(Source.NextULong() >> 33);
    }
}
