// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The rooms in the light probes (ours; <see cref="ModernProbeRoom"/>):
/// a surface inside an interior reads the probes where all eight round the point stand in the
/// room's own air, a surface in the street is left alone, and the doorway's reveals slide from one to the other.
/// The shader's <c>ProbeRoomPoint</c> is the same arithmetic.
/// </summary>
public class ModernProbeRoomTests
{
    const float Spacing = 1.5f;

    /// <summary>A room 8 m by 6 m by 5 m with its corner at (10, 20, 0), square to the grid.</summary>
    static readonly ModernProbeRoom Square = new(new Vector3(10, 20, 0), 0f, new Vector3(8, 6, 5));

    [Fact]
    public void A_levels_interiors_are_its_rooms_and_other_areas_are_not()
    {
        var areas = new List<L2Area>
        {
            new() { Name = "smithy", Kind = L2Area.Interior, Box = new L2AreaBox { Origin = [10, 20, 0], RotZ = 0.5f, Size = [8, 6, 5] } },
            new() { Name = "market", Kind = "sound", Box = new L2AreaBox { Origin = [0, 0, 0], Size = [40, 40, 10] } },
        };
        var room = Assert.Single(ModernProbeRoom.From(areas));
        Assert.Equal((new Vector3(10, 20, 0), 0.5f, new Vector3(8, 6, 5)), (room.Origin, room.RotZ, room.Size));
        Assert.Empty(ModernProbeRoom.From(null));
        var many = Enumerable.Range(0, 7).Select(i => new L2Area { Name = $"r{i}", Box = new L2AreaBox { Origin = [i * 10, 0, 0], Size = [4, 4, 3] } }).ToList();
        Assert.Equal(ModernProbeRoom.Most, ModernProbeRoom.From(many).Count);
    }

    [Fact]
    public void A_floor_at_a_walls_foot_reads_a_whole_spacing_inside_the_room()
    {
        // The floor 10 cm from the wall at x = 10, read half a spacing up: without the room it would stand between
        // a probe in the street and one indoors.
        var surface = new Vector3(10.1f, 23f, 0f);
        var read = Square.Read(surface, surface + new Vector3(0, 0, 0.75f), Spacing);
        Assert.Equal(new Vector3(11.5f, 23f, 1.5f), read);
        // Every probe round the read point (the grid's cell it is in, wherever the grid's lines fall) is in the box.
        foreach (float phase in new[] { 0f, 0.4f, 0.9f, 1.3f })
            for (int axis = 0; axis < 3; axis++)
            {
                float v = read[axis], lo = MathF.Floor((v - phase) / Spacing) * Spacing + phase, hi = lo + Spacing;
                float min = Square.Origin[axis], max = min + Square.Size[axis];
                Assert.True(lo >= min - 1e-4f && (hi <= max + 1e-4f || v == lo), $"axis {axis}, phase {phase}: {lo}..{hi} outside {min}..{max}");
            }
    }

    [Fact]
    public void The_middle_of_a_room_reads_where_it_is_and_the_street_is_left_alone()
    {
        var middle = new Vector3(14f, 23f, 2.5f);
        Assert.Equal(middle + new Vector3(0, 0.75f, 0), Square.Read(middle, middle + new Vector3(0, 0.75f, 0), Spacing));
        // The outer face of a 0.42 m wall and the cobbles before it: outside the rise, untouched.
        var wall = new Vector3(14f, 19.58f, 1.5f);
        Assert.Equal(wall - new Vector3(0, 0.75f, 0), Square.Read(wall, wall - new Vector3(0, 0.75f, 0), Spacing));
        var street = new Vector3(14f, 17f, 0f);
        Assert.Equal(street + new Vector3(0, 0, 0.75f), Square.Read(street, street + new Vector3(0, 0, 0.75f), Spacing));
    }

    [Fact]
    public void A_doorways_reveal_slides_from_the_streets_point_to_the_rooms()
    {
        // The threshold half way through the wall (0.2 m outside the box, half the rise): half way between.
        var sill = new Vector3(14f, 20f - ModernProbeRoom.Rise / 2, 0f);
        var lifted = sill + new Vector3(0, 0, 0.75f);
        var held = new Vector3(14f, 21.5f, 1.5f);
        var read = Square.Read(sill, lifted, Spacing);
        Assert.Equal(Vector3.Lerp(lifted, held, 0.5f).Y, read.Y, 4);
        Assert.Equal(Vector3.Lerp(lifted, held, 0.5f).Z, read.Z, 4);
        Assert.Equal(held, Square.Read(new Vector3(14f, 20f, 0f), new Vector3(14f, 20f, 0.75f), Spacing));
    }

    [Fact]
    public void A_turned_room_and_a_small_one_keep_their_read_points_inside()
    {
        // Turned half round (the Old Town's smithy): the box runs back from its corner along -x and -y.
        var turned = new ModernProbeRoom(new Vector3(-3.92f, 16.7078f, 0.05f), MathF.PI, new Vector3(8.16f, 6.16f, 5.85f));
        var foot = new Vector3(-8f, 16.6f, 0.05f);                       // the floor just inside the door's wall
        var read = turned.Read(foot, foot + new Vector3(0, 0, 0.75f), Spacing);
        Assert.Equal(-8f, read.X, 3);
        Assert.Equal(16.7078f - 1.5f, read.Y, 3);
        Assert.Equal(1.55f, read.Z, 3);
        // A room narrower than two spacings reads on its middle plane.
        var closet = new ModernProbeRoom(Vector3.Zero, 0f, new Vector3(2f, 2f, 2.4f));
        Assert.Equal(new Vector3(1f, 1f, 1.2f), closet.Read(new Vector3(0.1f, 0.3f, 0f), new Vector3(0.1f, 0.3f, 0.75f), Spacing));
    }
}
