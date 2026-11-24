// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **The additions to L2**: the collision's
/// <see cref="L2Surface.NoClimb"/> bit comes back from <c>COLL</c> 1's bytes beside Solid and Walk, a byte for each
/// triangle as before; the manifest's <c>navigation</c> (<see cref="L2Navigation"/>) is read and written back, and a
/// manifest without it is written without it; an area's <c>climb</c> is kept among its unknown keys.
/// </summary>
public class L2NoClimbTests
{
    [Fact]
    public void The_no_climb_bit_comes_back_from_a_collision_chunk_as_one_more_bit_of_the_flags_byte()
    {
        var chunk = new L2Collision
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 2)],
            Indices = [0, 1, 2, 0, 2, 3],
            Surfaces = [L2Surface.Solid | L2Surface.Walk | L2Surface.NoClimb, L2Surface.Solid | L2Surface.NoClimb],
            Materials = [0, 3],
            Nodes = [new(new(0, 0, 0), new(1, 1, 2), 0, 2)],
        };
        var bytes = chunk.Encode();
        Assert.Equal(L2Collision.Version1, bytes.Version);
        var back = L2Collision.Decode(bytes);
        Assert.Equal(chunk.Surfaces, back.Surfaces);
        Assert.Equal((byte)7, (byte)back.Surfaces[0]);
        Assert.True((back.Surfaces[1] & L2Surface.Walk) == 0 && (back.Surfaces[1] & L2Surface.Solid) != 0);
    }

    [Fact]
    public void The_manifests_navigation_and_an_areas_climb_are_read_and_written_back_and_absent_stay_absent()
    {
        const string Json = """
            { "format": "vitomir-l2", "version": 2, "name": "t", "navigation": { "ledges": "stairs" },
              "areas": [ { "name": "roof", "kind": "zone", "climb": "none", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [1, 1, 1] } } ] }
            """;
        var m = L2Manifest.Read(Json);
        Assert.Equal(L2Navigation.StairLedges, m.Navigation?.Ledges);
        Assert.Equal("none", m.Areas![0].Unknown!["climb"].GetString());
        var again = L2Manifest.Read(m.Write());
        Assert.Equal("stairs", again.Navigation?.Ledges);
        Assert.Equal("none", again.Areas![0].Unknown!["climb"].GetString());
        var plain = new L2Manifest { Name = "t" };
        Assert.DoesNotContain("navigation", plain.Write());
        Assert.Null(L2Manifest.Read(plain.Write()).Navigation);
    }
}
