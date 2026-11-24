// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The navigation voxels' layers lie on a fixed datum** (ours). The
/// cook used to count its 0.05 m layers up from the level's lowest walkable ground, so digging a ditch
/// 2 cm deeper moved every layer under every floor and stair, and a lowest point a whole number of
/// voxels under the floors put all of them exactly on layer boundaries. Now the count begins on the
/// datum's boundary at or under that lowest point (<see cref="CellCook.NavFloor"/>): every boundary is
/// a quarter of a voxel over a whole number of voxels from height 0, wherever the lowest ground is.
///
/// Pinned: the datum itself and that the origin is never above the lowest ground nor a voxel under it;
/// that a floor at any whole number of millimetres is at least half a millimetre from a boundary, and
/// one at a multiple of 2.5 cm a quarter of a voxel; and, by cooking a floor with a pit in it twice,
/// the pit 5.90 m deep and 5.88 m deep, that the floor's navigation polygons come out at the same
/// height both times (gridded, where the manifest's <c>grid.navOrigin</c> carries the origin, and as
/// one tile).
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellNavDatumTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    const float Voxel = 0.05f;

    /// <summary>How far above the layer boundary under it a height is, in voxels (0 to 1).</summary>
    static double InLayer(double height, float origin) { double v = (height - origin) / (double)Voxel; return v - Math.Floor(v); }

    [Fact]
    public void The_voxels_are_counted_up_from_a_boundary_of_the_datum_at_or_under_the_lowest_ground()
    {
        Assert.Equal(0.05f, NavSettings.Default.CellHeight);
        foreach (float lowest in new[] { -7.9000444f, -7.925f, -7.8776f, -7.9f, 0f, -0.0125f, 0.0125f, 3.3f, -2f, -43.21f, 120.004f })
        {
            float origin = CellCook.NavFloor(lowest, Voxel);
            Assert.True(origin <= lowest + 1e-6f, $"the origin {origin} is above the lowest ground {lowest}");
            Assert.True(origin > lowest - Voxel - 1e-6f, $"the origin {origin} is a voxel or more under the lowest ground {lowest}");
            // Height 0 is three quarters of a voxel over the boundary under it: the boundaries are at (k + 1/4) voxels.
            Assert.Equal(1 - CellCook.NavDatum, InLayer(0, origin), 3);
        }
    }

    [Fact]
    public void A_floor_at_a_round_height_is_never_on_a_layer_boundary()
    {
        float origin = CellCook.NavFloor(-7.9000444f, Voxel);       // the East Gate's: its pads at 0.000 were 158.0009 voxels up
        for (int mm = -8000; mm <= 30000; mm++)
        {
            double at = InLayer(mm / 1000.0, origin), off = Math.Min(at, 1 - at) * Voxel;
            Assert.True(off > 0.00045, $"a floor at {mm} mm is {off * 1000:0.00} mm from a layer boundary");
            if (mm % 25 == 0) Assert.True(off > 0.012, $"a floor at {mm} mm is {off * 1000:0.00} mm from a layer boundary");
        }
    }

    /// <summary>A 12 m × 12 m floor at height 0 with a 2 m square pit in its corner, <paramref name="pit"/> metres deep (its walls are not ground).</summary>
    static (Vector3[] V, int[] I, int[] Mat) FloorWithPit(float pit)
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]); mat.AddRange([m, m]);
        }
        for (int x = -6; x < 6; x++)
            for (int y = -6; y < 6; y++)
            {
                float z = x >= 4 && y >= 4 ? -pit : 0f;
                Quad(new(x, y, z), new(x + 1, y, z), new(x + 1, y + 1, z), new(x, y + 1, z), 0);
            }
        Quad(new(4, 4, -pit), new(4, 6, -pit), new(4, 6, 0), new(4, 4, 0), 1);     // the pit's two walls, facing into it
        Quad(new(6, 4, -pit), new(4, 4, -pit), new(4, 4, 0), new(6, 4, 0), 1);
        return ([.. v], [.. idx], [.. mat]);
    }

    /// <summary>Cooks the floor and answers the heights (metres, rounded to a tenth of a millimetre) of its navigation polygons' corners over the floor proper, and the grid's origin height.</summary>
    static (double[] Heights, float? Origin) Cooked(float pit, float cellSize)
    {
        var (v, idx, mat) = FloorWithPit(pit);
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "datum", Vertices = v, Indices = idx, TriangleMaterials = mat, Materials = ["ground", "wall"],
                Ground = m => m == "ground", Passable = _ => false, CellSize = cellSize,
            }, L2Package.PathFor(root, "datum"));
            using var stream = new CellStream(L2Package.Open(L2Package.PathFor(root, "datum")), new CellStreamOptions { Everything = true });
            stream.LoadNow(Vector3.Zero);
            var heights = new SortedSet<double>();
            for (int slot = 0; slot < stream.Slots; slot++)
            {
                long poly = stream.RefOf(slot);
                if (poly == 0 || stream.Nav.GetTileAndPolyByRef(poly, out var tile, out var p).Failed()) continue;
                for (int k = 0; k < p.vertCount; k++)
                {
                    float x = tile.data.verts[3 * p.verts[k]], y = tile.data.verts[3 * p.verts[k] + 1], z = tile.data.verts[3 * p.verts[k] + 2];
                    if (x < 3.5f || -z < 3.5f) heights.Add(Math.Round(y, 4));     // Recast's frame: x, height, −y; not the pit's own corner
                }
            }
            return ([.. heights], stream.Manifest.Grid?.NavOrigin[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(0f)]      // one cell, one tile
    [InlineData(8f)]      // a grid of 8 m cells
    public void Two_centimetres_of_change_in_the_lowest_ground_leave_every_floor_s_layer_where_it_was(float cellSize)
    {
        // The lowest walkable ground less the margin: 7.90 m under the floor (158 voxels: on a boundary until then) and 7.88 m.
        var (deep, deepOrigin) = Cooked(5.90f, cellSize);
        var (shallow, shallowOrigin) = Cooked(5.88f, cellSize);
        Assert.NotEmpty(deep);
        Assert.Equal(deep, shallow);
        // The floor at 0 is in the layer whose top is the datum's boundary just over it.
        Assert.Equal([Math.Round(CellCook.NavDatum * Voxel, 4)], deep);
        if (cellSize > 0)
        {
            Assert.Equal(CellCook.NavFloor(-5.90f - CellCook.NavMargin, Voxel), deepOrigin!.Value);
            Assert.Equal(CellCook.NavFloor(-5.88f - CellCook.NavMargin, Voxel), shallowOrigin!.Value);
        }
    }
}
