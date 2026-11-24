// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// A cell level's exits and entrances from its generator's layout (ours; <see cref="L2LayoutExits"/>,
/// exits and entrances): the chronicle's exits keep their numbers, a gate gets a volume
/// to walk into and a door does not, every <c>playerstartNN</c> is a start of entrance NN, and an area learns
/// from the layout's interiors which exit it is behind and which place it stands for. The layout here is made
/// up, in the Old Town generator's shape.
/// </summary>
public class L2LayoutExitsTests
{
    const string Layout = """
        { "level": "P1_OTDY",
          "exits": [
            { "number": 0, "to": "(level start)", "toLevel": null, "closed": null, "starts": [[20, 12, 0]], "trackers": [], "door": null },
            { "number": 3, "to": "EastGate", "toLevel": "P1_EGAT", "closed": false, "trackers": [[53.5, -9.5, 1.5]], "door": "gate_3" },
            { "number": 7, "to": "VysMountain", "toLevel": "P2_VYSM", "closed": true, "trackers": [[-19, -30, 1.5]], "door": "gate_7" },
            { "number": 2, "to": "University", "toLevel": "P1_UNIV", "closed": false, "trackers": [[58, 5, 3], [58, 13, 3]], "door": "university_hall" },
            { "number": 9, "to": "SmithyDay", "toLevel": "P1_SMYD", "closed": false, "trackers": [[-8, 17, 1.5]], "door": "smithy" } ],
          "anchors": [
            { "id": "P1_OTDY/things/0", "kind": "prop", "name": "bench1", "position": [22, 3, 0], "rotation": [0, 0, 110] },
            { "id": "P1_OTDY/starts/0", "kind": "start", "name": "playerstart00", "position": [20, 12, 0], "rotation": [0, 0, -180] },
            { "id": "P1_OTDY/starts/36", "kind": "start", "name": "playerstart10", "position": [14.75, 11.25, 0], "rotation": [0, 0, -135] },
            { "id": "P1_OTDY/starts/37", "kind": "start", "name": "playerstart10", "position": [12.75, 9.5, 0], "rotation": [0, 0, -135] } ],
          "interiors": [ { "name": "smithy", "door": { "outside": [-8, 17.75, 0], "threshold": [-8, 17, 0], "inside": [-8, 15.75, 0], "exit": 9 } } ],
          "areas": [ { "name": "smithy", "kind": "interior", "box": { "origin": [-4, 16.75, 0], "rotZ": 3.14159, "size": [8, 6, 5] },
                       "exposure": 2.5, "doors": [ { "position": [-8, 17, 0], "width": 2, "height": 2.25 } ] },
                     { "name": "yard", "kind": "interior", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [2, 2, 2] } } ] }
        """;

    static JsonNode Parsed => JsonNode.Parse(Layout)!;

    [Fact]
    public void The_chronicle_s_exits_keep_their_numbers_and_only_a_gate_gets_a_volume_to_walk_into()
    {
        var exits = L2LayoutExits.Exits(Parsed)!;
        Assert.Equal([3, 7, 2, 9], exits.Select(e => e.Number));               // the level's start (0) is not an exit
        var gate = exits[0];
        Assert.Equal(("EastGate", "P1_EGAT", false, "gate_3"), (gate.To, gate.ToLevel, gate.Closed, gate.Door));
        Assert.Equal([53.5f, -9.5f, 1.5f], gate.At!);
        Assert.NotNull(gate.Volume);
        Assert.Equal([51.5f, -11.5f, -1f], gate.Volume!.Origin);
        Assert.Equal([4f, 4f, 4.5f], gate.Volume.Size);
        Assert.True(exits[1].Closed);
        Assert.Equal([58f, 9f, 3f], exits[2].At!);                              // the middle of its two signs
        Assert.Null(exits[2].Volume);                                          // a door is clicked
        Assert.Null(exits[3].Volume);
        Assert.Null(L2LayoutExits.Exits(JsonNode.Parse("""{ "level": "x" }""")));
    }

    [Fact]
    public void Every_playerstart_is_a_start_of_the_entrance_its_name_ends_in()
    {
        var starts = L2LayoutExits.Starts(Parsed, "oldtown")!;
        Assert.Equal(3, starts.Count);                                         // the bench is not a start
        Assert.Equal(["oldtown/starts/0", "oldtown/starts/1", "oldtown/starts/2"], starts.Select(s => s.Id));
        Assert.Equal([0u, 10u, 10u], starts.Select(s => s.Group!.Value));
        Assert.All(starts, s => Assert.Equal("start", s.Kind));
        Assert.Equal([14.75f, 11.25f, 0f], starts[1].Position);
        Assert.Equal(-135f, starts[1].Rotation[2]);
        Assert.Equal(7u, L2LayoutExits.GroupOf("playerstart07"));
        Assert.Equal(0u, L2LayoutExits.GroupOf("start"));
        Assert.Null(L2LayoutExits.Starts(JsonNode.Parse("""{ "anchors": [] }"""), "x"));
    }

    [Fact]
    public void An_area_learns_its_exit_its_place_and_its_door_s_two_sides_from_the_layout_s_interior()
    {
        var areas = L2Area.FromLayout(Parsed)!;
        var smithy = areas[0];
        Assert.Equal(9, smithy.Exit);
        Assert.Equal("SmithyDay", smithy.Place);
        var door = Assert.Single(smithy.Doors!);
        Assert.Equal([-8f, 15.75f, 0f], door.Inside!);
        Assert.Equal([-8f, 17.75f, 0f], door.Outside!);
        Assert.Equal((2f, 2.25f), (door.Width, door.Height));
        Assert.Equal("smithy (interior, 8 × 6 × 5 m, +2.5 stops, stands for SmithyDay by exit 9)", smithy.ToString());
        // An area no interior speaks for is only a volume.
        Assert.Null(areas[1].Exit);
        Assert.Null(areas[1].Place);
        // And one that says its own keeps it.
        var own = L2Area.FromLayout(JsonNode.Parse("""
            { "exits": [ { "number": 9, "to": "SmithyDay" } ], "interiors": [ { "name": "smithy", "door": { "exit": 9 } } ],
              "areas": [ { "name": "smithy", "place": "Smithy", "exit": 4, "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [1, 1, 1] } } ] }
            """))!.Single();
        Assert.Equal(("Smithy", 4), (own.Place, own.Exit));
    }

    [Fact]
    public void A_cooked_level_s_manifest_is_relaid_from_its_layout_and_what_the_cook_made_is_left_alone()
    {
        var m = new L2Manifest
        {
            Version = L2Manifest.Version2, Name = "oldtown",
            Grid = new L2Grid { Origin = [-48, -48], CellSize = 48, Columns = 3, Rows = 2 },
            Cells = [new L2CellEntry { Id = "c1_1", File = "cells/c1_1.cell" }],
        };
        m.Anchors.Add(new L2Anchor { Id = "oldtown/starts/0", Kind = "start", Name = "playerstart00", Group = 0, Cell = "c1_1" });
        m.Anchors.Add(new L2Anchor { Id = "oldtown/things/7", Kind = "prop", Name = "bench1" });
        string said = L2LayoutExits.Relay(m, Parsed);
        Assert.StartsWith("3 starts at 2 entrances; 4 exits (3 EastGate walk-in, 7 VysMountain walk-in, 2 University, 9 SmithyDay); 2 areas: smithy", said);
        Assert.Equal(["start", "start", "start", "prop"], m.Anchors.Select(a => a.Kind));   // the old start replaced, the bench kept
        Assert.Equal(["c1_1", "c1_1", "c1_1"], m.Anchors.Take(3).Select(a => a.Cell));       // (20, 12), (14.75, 11.25), (12.75, 9.5): column 1, row 1
        Assert.Equal(4, m.Exits.Count);
        Assert.Equal(2, m.Areas!.Count);
        Assert.Single(m.Cells);
        // A layout that names none of them changes nothing.
        Assert.Equal("the layout names no starts, exits, areas or lifts; nothing changed", L2LayoutExits.Relay(m, JsonNode.Parse("""{ "level": "x" }""")));
        Assert.Equal(4, m.Exits.Count);
    }

    [Fact]
    public void Exits_entrances_and_an_area_s_threshold_come_back_from_a_manifest_as_written()
    {
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", Exits = L2LayoutExits.Exits(Parsed)!, Areas = L2Area.FromLayout(Parsed) };
        m.Anchors.AddRange(L2LayoutExits.Starts(Parsed, "oldtown")!);
        var back = L2Manifest.Read(m.Write());
        Assert.Equal(m.Write(), back.Write());
        Assert.Equal(4, back.Exits.Count);
        Assert.Equal("gate_3", back.Exits[0].Door);
        Assert.Equal([4f, 4f, 4.5f], back.Exits[0].Volume!.Size);
        Assert.Equal(9, back.Areas![0].Exit);
        Assert.Equal("SmithyDay", back.Areas[0].Place);
        Assert.Equal(10u, back.Anchors[1].Group);
        // A converted 1.1 level's exit, which has none of the new keys, writes none of them.
        var legacy = new L2Manifest { Name = "P1_OTDY", Exits = [new L2Exit { Chronicle = "Prague", Location = "OldTownDay", Number = 9, To = "SmithyDay", ToLevel = "P1_SMYD" }] };
        Assert.DoesNotContain("\"volume\"", legacy.Write());
        Assert.DoesNotContain("\"door\"", legacy.Write());
        Assert.DoesNotContain("\"at\"", legacy.Write());
    }
}
