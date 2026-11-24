// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Xunit;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The cook builds its navigation mesh only where people can walk (ours). The generated Old Town carries a
/// 5 km field plane to the horizon; one tile over all of it at a 0.15 m voxel ran the cook out of memory.
/// These pin that the build's box is the walkable ground's plus a margin, and that scenery wholly outside
/// it is left out while a huge triangle crossing it is kept (Recast clips it).
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellNavBoundsTests
{
    static readonly Vector3[] Metres =
    [
        new(0, 0, 0), new(10, 0, 0), new(0, 10, 0),              // a walkable floor, 10 m
        new(-2500, -2500, -1), new(2500, -2500, -1), new(0, 2500, -1),   // the fields: huge, crossing it
        new(900, 900, 0), new(910, 900, 0), new(900, 910, 0),    // a backdrop house far away
    ];
    static readonly int[] Indices = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    static readonly L2Surface[] Surfaces = [L2Surface.Solid | L2Surface.Walk, L2Surface.Solid, L2Surface.Solid];

    [Fact]
    public void The_build_box_is_the_walkable_ground_and_a_margin()
    {
        var (lo, hi) = CellCook.WalkBounds(Metres, Indices, Surfaces);
        Assert.Equal(new Vector3(-CellCook.NavMargin, -CellCook.NavMargin, -CellCook.NavMargin), lo);
        Assert.Equal(new Vector3(10 + CellCook.NavMargin, 10 + CellCook.NavMargin, CellCook.NavMargin + 2), hi);
    }

    [Fact]
    public void Scenery_outside_is_left_out_and_a_huge_crossing_triangle_is_kept()
    {
        var (lo, hi) = CellCook.WalkBounds(Metres, Indices, Surfaces);
        Assert.Equal([0, 1, 2, 3, 4, 5], CellCook.TrianglesOverlapping(Metres, Indices, lo, hi));
    }

    [Fact]
    public void Nothing_walkable_gives_an_empty_box()
    {
        var (lo, hi) = CellCook.WalkBounds(Metres, Indices, [L2Surface.Solid, L2Surface.Solid, L2Surface.Solid]);
        Assert.True(lo.X > hi.X);
    }

    [Fact]
    public void The_level_names_its_walkable_meshes_by_option_list_or_note()
    {
        var layout = System.Text.Json.Nodes.JsonNode.Parse("""{"ground":{"walkNodes":["ground_walk","bridge_deck"]}}""");
        Assert.Equal(["a", "b"], CellCook.WalkNodes("a, b", layout)!);                      // the option wins
        Assert.Equal(["ground_walk", "bridge_deck"], CellCook.WalkNodes(null, layout)!);
        var note = System.Text.Json.Nodes.JsonNode.Parse("""{"ground":{"walkable":"ground_walk (faces over 1.1's walkable cells)"}}""");
        Assert.Equal(["ground_walk"], CellCook.WalkNodes(null, note)!);                    // the generator's older note
        Assert.Null(CellCook.WalkNodes(null, System.Text.Json.Nodes.JsonNode.Parse("{}")));   // unsaid: materials decide
    }
}
