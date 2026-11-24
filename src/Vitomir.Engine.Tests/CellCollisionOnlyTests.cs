// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Levels.Cells;
using Xunit;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A level can name meshes that are collision and navigation only (ours): the cook keeps
/// their triangles for <c>COLL</c> and <c>NAVM</c> and leaves them out of every cell's <c>MESH</c>. The Old
/// Town's stairs needed it: a walker stood on a smooth flight through the middles of the risers,
/// as on a ramp, while the steps drawn over it stopped nothing — were the flight drawn too it would have shown
/// between the treads. Now a walker stands on the drawn steps and the mesh holds the faces at the
/// flights' sides. These pin how the layout names such meshes and which primitives the cook then draws.
/// </summary>
public class CellCollisionOnlyTests
{
    [Fact]
    public void The_layout_names_its_collision_only_meshes_under_ground()
    {
        var layout = System.Text.Json.Nodes.JsonNode.Parse("""{"ground":{"collisionOnlyMeshes":["ground_flights","cellar_ramp"]}}""");
        Assert.Equal(["ground_flights", "cellar_ramp"], CellCook.CollisionOnlyMeshes(layout));
    }

    [Fact]
    public void A_layout_that_names_none_has_none()
    {
        Assert.Empty(CellCook.CollisionOnlyMeshes(null));
        Assert.Empty(CellCook.CollisionOnlyMeshes(System.Text.Json.Nodes.JsonNode.Parse("""{"ground":{"walkNodes":["ground_walk"]}}""")));
    }

    [Fact]
    public void A_primitive_of_a_collision_only_mesh_is_not_drawn()
    {
        string[] hidden = ["ground_flights"];
        Assert.False(CellCook.Drawn("ground_flights#0", hidden));
        Assert.False(CellCook.Drawn("Ground_Flights#3", hidden));      // names compare as the walk nodes' do
    }

    [Fact]
    public void Every_other_primitive_is_drawn()
    {
        string[] hidden = ["ground_flights"];
        Assert.True(CellCook.Drawn("ground_walk#0", hidden));
        Assert.True(CellCook.Drawn("ground_flights_kerb#0", hidden));  // a whole name, not a prefix
        Assert.True(CellCook.Drawn("house_3#1", []));
    }
}
