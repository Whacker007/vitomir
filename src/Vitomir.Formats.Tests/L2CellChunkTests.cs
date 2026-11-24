// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **A cell level's chunks** (ours): <c>COLL</c>, <c>LITE</c>,
/// <c>MESH</c> and <c>NAVM</c> come back from their bytes as they went in, and a collision chunk whose
/// tree or triangles point outside it, or of a version this build does not know, is refused.
/// </summary>
public class L2CellChunkTests
{
    static L2Collision Tiny() => new()
    {
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 2)],
        Indices = [0, 1, 2, 0, 2, 3],
        Surfaces = [L2Surface.Solid | L2Surface.Walk, L2Surface.Solid],
        Materials = [0, 3],
        Nodes = [new(new(0, 0, 0), new(1, 1, 2), 2, 0), new(new(0, 0, 0), new(1, 1, 0), 0, 1), new(new(0, 0, 0), new(1, 1, 2), 1, 1)],
    };

    [Fact]
    public void A_collision_chunk_comes_back_triangle_for_triangle_and_node_for_node()
    {
        var back = L2Collision.Decode(Tiny().Encode());
        Assert.Equal(Tiny().Vertices, back.Vertices);
        Assert.Equal(Tiny().Indices, back.Indices);
        Assert.Equal(Tiny().Surfaces, back.Surfaces);
        Assert.Equal(Tiny().Materials, back.Materials);
        Assert.Equal(Tiny().Nodes, back.Nodes);
        Assert.Equal(2, back.Triangles);
    }

    [Fact]
    public void A_collision_chunk_that_points_outside_itself_or_is_from_the_future_is_refused()
    {
        var wrongVertex = Tiny();
        var bad = new L2Collision { Vertices = wrongVertex.Vertices, Indices = [0, 1, 9, 0, 2, 3], Surfaces = wrongVertex.Surfaces,
                                    Materials = wrongVertex.Materials, Nodes = wrongVertex.Nodes };
        Assert.Throws<NodFormatException>(() => L2Collision.Decode(bad.Encode()));
        var wrongNode = new L2Collision { Vertices = wrongVertex.Vertices, Indices = wrongVertex.Indices, Surfaces = wrongVertex.Surfaces,
                                          Materials = wrongVertex.Materials, Nodes = [new(Vector3.Zero, Vector3.One, 0, 5)] };
        Assert.Throws<NodFormatException>(() => L2Collision.Decode(wrongNode.Encode()));
        var future = Tiny().Encode() with { Version = 2 };
        Assert.Throws<NodFormatException>(() => L2Collision.Decode(future));
    }

    [Fact]
    public void Lights_a_mesh_reference_and_a_navigation_tile_come_back_as_written()
    {
        var lights = new List<L2Light> { new("torch00", L2Light.Point, new(1, 2, 3), new(1, 0.5f, 0.25f), 2, 8), new("", 0, default, default, 0, 0) };
        Assert.Equal(lights, L2LightChunk.Decode(L2LightChunk.Encode(lights)));

        var mesh = new L2Mesh { Asset = "assets/street.glb", Draws = [new("glb_ground", 100, new(-1, -2, -3), new(4, 5, 6))] };
        var meshBack = L2Mesh.Decode(mesh.Encode());
        Assert.Equal(mesh.Asset, meshBack.Asset);
        Assert.Equal(mesh.Draws, meshBack.Draws);

        var nav = new L2NavMesh { AgentRadius = 0.25f, AgentHeight = 1.7f, AgentClimb = 0.4f, MaxSlope = 45, CellSize = 0.15f, CellHeight = 0.05f, Tile = [1, 2, 3, 4, 5] };
        var navBack = L2NavMesh.Decode(nav.Encode());
        Assert.Equal((nav.AgentRadius, nav.AgentHeight, nav.AgentClimb, nav.MaxSlope, nav.CellSize, nav.CellHeight),
                     (navBack.AgentRadius, navBack.AgentHeight, navBack.AgentClimb, navBack.MaxSlope, navBack.CellSize, navBack.CellHeight));
        Assert.Equal(nav.Tile, navBack.Tile);
    }
}
