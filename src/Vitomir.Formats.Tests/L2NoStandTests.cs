// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **the addition to L2**: the collision's <see cref="L2Surface.NoStand"/>
/// bit (8) comes back from <c>COLL</c> 1's bytes beside Solid, Walk and NoClimb, with no new version; a chunk without it
/// reads as before; an area's <c>stand</c> is kept among its unknown keys and written back.
/// </summary>
public class L2NoStandTests
{
    [Fact]
    public void The_no_stand_bit_comes_back_from_a_collision_chunk_and_a_chunk_without_it_reads_as_before()
    {
        var chunk = new L2Collision
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 2)],
            Indices = [0, 1, 2, 0, 2, 3],
            Surfaces = [L2Surface.Solid | L2Surface.NoStand, L2Surface.Solid | L2Surface.NoClimb | L2Surface.NoStand],
            Materials = [0, 3],
            Nodes = [new(new(0, 0, 0), new(1, 1, 2), 0, 2)],
        };
        var bytes = chunk.Encode();
        Assert.Equal(L2Collision.Version1, bytes.Version);
        var back = L2Collision.Decode(bytes);
        Assert.Equal(chunk.Surfaces, back.Surfaces);
        Assert.Equal((byte)9, (byte)back.Surfaces[0]);
        Assert.Equal((byte)13, (byte)back.Surfaces[1]);

        // The same chunk with no NoStand bit: the same bytes but the flags, which read back as they were written.
        var before = new L2Collision
        {
            Vertices = chunk.Vertices, Indices = chunk.Indices, Materials = chunk.Materials, Nodes = chunk.Nodes,
            Surfaces = [L2Surface.Solid | L2Surface.Walk, L2Surface.Solid],
        };
        var plain = L2Collision.Decode(before.Encode());
        Assert.Equal(before.Surfaces, plain.Surfaces);
        Assert.All(plain.Surfaces, s => Assert.True((s & L2Surface.NoStand) == 0));
        Assert.Equal(before.Encode().Payload.Length, bytes.Payload.Length);
    }

    [Fact]
    public void An_areas_stand_is_read_and_written_back_and_an_area_without_it_has_none()
    {
        const string Json = """
            { "format": "vitomir-l2", "version": 2, "name": "t",
              "areas": [ { "name": "roof", "kind": "zone", "stand": "none", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [1, 1, 1] } },
                         { "name": "yard", "kind": "zone", "box": { "origin": [5, 0, 0], "rotZ": 0, "size": [1, 1, 1] } } ] }
            """;
        var m = L2Manifest.Read(Json);
        Assert.Equal("none", m.Areas![0].Unknown!["stand"].GetString());
        var again = L2Manifest.Read(m.Write());
        Assert.Equal("none", again.Areas![0].Unknown!["stand"].GetString());
        Assert.True(again.Areas[1].Unknown is null || !again.Areas[1].Unknown!.ContainsKey("stand"));
    }
}
