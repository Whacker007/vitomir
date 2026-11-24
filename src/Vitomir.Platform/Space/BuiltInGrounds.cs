// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: grounds built in code, not cooked levels.
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Vitomir.Platform.Navigation;

namespace Vitomir.Platform.Space;

/// <summary>
/// Grounds built in code: a level name <c>builtin:&lt;name&gt;</c> in place of a package folder opens one of these
/// (<see cref="Open"/>) — collision, navigation, a start and the masquerade's word for the whole — and nothing to draw (no package: the renderer
/// draws cooked levels only: no picture is made on one). <b>The range</b> (<c>builtin:range</c>; a scenario's
/// <c>"ground": "range"</c>): a flat, level, open lane with no walls, x 0 to 170 m and y −6 to 24 m (its middle line y = 9, the free
/// yard's clear line carried on), the floor at z 0, a free place to the masquerade, one start at (40, 3) (the free yard's middle,
/// so actors with no place stand where they do on the free yard), navigation one walkable polygon over the whole, so routes and
/// the reactions find their way; a body that walks off its edge falls (the fall's recovery puts it back).
/// </summary>
public static class BuiltInGrounds
{
    public const string Prefix = "builtin:";
    /// <summary>The range's level name.</summary>
    public const string Range = "builtin:range";

    /// <summary>The range's floor, metres in the level's frame (+Z up): x 0 to 170, y −6 to 24, z 0.</summary>
    public static readonly (float X0, float X1, float Y0, float Y1, float Z) RangeArea = (0f, 170f, -6f, 24f, 0f);

    /// <summary>Whether <paramref name="level"/> names a ground built in code (<c>builtin:…</c>).</summary>
    public static bool Is(string? level) => level is not null && level.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The ground <paramref name="level"/> names; <see cref="IOException"/> for a <c>builtin:</c> name this build does not have.</summary>
    public static LevelSpace Open(string level) => level switch
    {
        Range => MakeRange(),
        _ => throw new IOException($"{level} is no ground built in code (this build has {Range})"),
    };

    static LevelSpace MakeRange()
    {
        var (x0, x1, y0, y1, z) = RangeArea;
        const float Tile = 5f;
        var v = new List<Vector3>();
        var ix = new List<int>();
        for (float x = x0; x < x1 - 1e-3f; x += Tile)
            for (float y = y0; y < y1 - 1e-3f; y += Tile)
            {
                float xe = MathF.Min(x + Tile, x1), ye = MathF.Min(y + Tile, y1);
                int at = v.Count;
                v.Add(new(x, y, z)); v.Add(new(xe, y, z)); v.Add(new(xe, ye, z)); v.Add(new(x, ye, z));
                ix.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        var collision = CollisionWorld.FromTriangles([.. v], [.. ix]);
        LevelNavigation? navigation;
        try { navigation = Navigation(x0, x1, y0, y1, z); }
        catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException) { navigation = null; }
        var anchors = new List<LevelAnchor> { new("range-start", "start", "range", new Vector3(40f, 3f, z), 0f, 0) };
        return new LevelSpace("range (built in code: a flat lane, x 0 to 170 m, y -6 to 24 m)", collision, navigation, anchors)
        {
            Source = Range,
            Bounds = (new Vector3(x0 - 1f, y0 - 1f, z - 20f), new Vector3(x1 + 1f, y1 + 1f, z + 20f)),
            Places = LevelPlaces.All(PlaceKind.Free),
        };
    }

    /// <summary>One walkable polygon over the rectangle (Detour's frame: x, up, −y), as one tile.</summary>
    static LevelNavigation Navigation(float x0, float x1, float y0, float y1, float z)
    {
        const float Cs = 0.5f, Ch = 0.25f;
        const int Nvp = 6, Null = 0xffff;
        var bmin = new RcVec3f(x0, z - 1f, -y1);
        var bmax = new RcVec3f(x1, z + 3f, -y0);
        int Q(float metres, float origin, float cell) => (int)MathF.Round((metres - origin) / cell);
        // The four corners in Detour's frame (x, up, z = −y), quantised from bmin.
        (float X, float Y)[] corners = [(x0, y0), (x0, y1), (x1, y1), (x1, y0)];
        var verts = new int[corners.Length * 3];
        for (int i = 0; i < corners.Length; i++)
        {
            verts[i * 3 + 0] = Q(corners[i].X, bmin.X, Cs);
            verts[i * 3 + 1] = Q(z, bmin.Y, Ch);
            verts[i * 3 + 2] = Q(-corners[i].Y, bmin.Z, Cs);
        }
        var polys = new int[2 * Nvp];
        Array.Fill(polys, Null);
        for (int i = 0; i < corners.Length; i++) polys[i] = i;   // its edges' neighbours stay 0xffff: borders
        var p = new DtNavMeshCreateParams
        {
            verts = verts,
            vertCount = corners.Length,
            polys = polys,
            polyFlags = [1],
            polyAreas = [63],
            polyCount = 1,
            nvp = Nvp,
            walkableHeight = 1.8f,
            walkableRadius = 0.3f,
            walkableClimb = 0.35f,
            bmin = bmin,
            bmax = bmax,
            cs = Cs,
            ch = Ch,
            buildBvTree = true,
            tileX = 0,
            tileZ = 0,
            tileLayer = 0,
        };
        var data = DtNavMeshBuilder.CreateNavMeshData(p) ?? throw new InvalidOperationException("the range's navigation tile was not made");
        var parameters = new DtNavMeshParams
        {
            orig = bmin,
            tileWidth = x1 - x0 + 1f,
            tileHeight = y1 - y0 + 1f,
            maxTiles = 1,
            maxPolys = 2,
        };
        return new LevelNavigation(parameters, [data], Nvp);
    }
}
