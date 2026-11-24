// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// A cell level's areas (ours; <see cref="L2Area"/>): the manifest's
/// <c>areas</c> written and read back with what this build does not know kept, a manifest without them having
/// none, the generator's layout giving them to the cook, and the exposure an interior asks for rising over its
/// doorway and nowhere else.
/// </summary>
public class L2AreaTests
{
    /// <summary>The Old Town's smithy as the generator writes it: a room 8.16 m by 6.16 m turned half round, its door on the +y side.</summary>
    const string Smithy = """
        { "name": "smithy", "kind": "interior",
          "box": { "origin": [-3.92, 16.7078, 0.05], "rotZ": -3.14159, "size": [8.16, 6.16, 5.8477] },
          "exposure": 2.5, "doors": [ { "position": [-8.0, 16.9178, 0.07], "width": 2.0, "height": 2.3 } ] }
        """;

    static List<L2Area> FromLayout() => L2Area.FromLayout(JsonNode.Parse($$"""{ "level": "P1_OTDY", "areas": [ {{Smithy}} ] }"""))!;

    [Fact]
    public void A_manifests_areas_come_back_as_written_with_what_this_build_does_not_know()
    {
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", Areas = FromLayout() };
        var text = m.Write();
        Assert.Contains("\"areas\"", text);
        Assert.Contains("\"doors\"", text);                       // the generator's doorways go on into the manifest
        var back = L2Manifest.Read(text);
        var a = Assert.Single(back.Areas!);
        Assert.Equal(("smithy", L2Area.Interior, 2.5f), (a.Name, a.Kind, a.Exposure));
        Assert.Equal([8.16f, 6.16f, 5.8477f], a.Box.Size);
        Assert.Equal(-3.14159f, a.Box.RotZ);
        Assert.Contains("\"doors\"", back.Write());
        Assert.Equal("smithy (interior, 8.2 × 6.2 × 5.8 m, +2.5 stops)", a.ToString());
    }

    [Fact]
    public void A_manifest_without_areas_has_none_and_writes_none()
    {
        var text = new L2Manifest { Version = L2Manifest.Version2, Name = "round10" }.Write();
        Assert.DoesNotContain("\"areas\"", text);
        Assert.Null(L2Manifest.Read(text).Areas);
        Assert.Equal(0f, L2Area.ExposureStops(null, new Vector3(1, 2, 3)));
    }

    [Fact]
    public void A_layout_without_sound_areas_gives_none()
    {
        Assert.Null(L2Area.FromLayout(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));
        Assert.Null(L2Area.FromLayout(JsonNode.Parse("""{ "areas": [ { "kind": "interior", "box": { "origin": [0,0,0], "rotZ": 0, "size": [1,1,1] } } ] }""")));   // no name
        Assert.Null(L2Area.FromLayout(JsonNode.Parse("""{ "areas": [ { "name": "flat", "box": { "origin": [0,0,0], "rotZ": 0, "size": [1,0,1] } } ] }""")));        // no depth
        Assert.Null(L2Area.FromLayout(JsonNode.Parse("""{ "areas": [ { "name": "odd", "box": "none" } ] }""")));
        // A sound one beside an unsound one is kept; an area with no exposure asks for none.
        var mixed = L2Area.FromLayout(JsonNode.Parse("""{ "areas": [ { "name": "odd", "box": "none" }, { "name": "tavern", "box": { "origin": [0,0,0], "rotZ": 0, "size": [4,5,3] } } ] }"""))!;
        Assert.Equal(("tavern", L2Area.Interior, 0f), (mixed.Single().Name, mixed.Single().Kind, mixed.Single().Exposure));
    }

    [Fact]
    public void A_point_is_as_deep_in_a_turned_box_as_its_nearest_face_is_far()
    {
        var a = FromLayout().Single();
        // Turned half round, the box runs from x = -12.08 to -3.92 and y = 10.5478 to 16.7078, z = 0.05 to 5.8977.
        Assert.Equal(1.0f, a.Depth(new Vector3(-8f, 15.7078f, 1.7f)), 3);        // a metre in from the door's wall
        Assert.Equal(1.65f, a.Depth(new Vector3(-8f, 13.6f, 1.7f)), 3);         // the middle: the floor is nearest
        Assert.Equal(-0.5f, a.Depth(new Vector3(-8f, 17.2078f, 1.7f)), 3);       // half a metre out in the street
        Assert.Equal(-2f, a.Depth(new Vector3(-1.92f, 13.6f, 1.7f)), 3);         // two metres past the end wall
        Assert.Equal(-MathF.Sqrt(2f), a.Depth(new Vector3(-2.92f, 17.7078f, 1.7f)), 3);   // off a corner
    }

    [Fact]
    public void An_interiors_exposure_rises_over_its_doorway_and_is_nothing_in_the_street()
    {
        var areas = FromLayout();
        float At(float y, float x = -8f) => L2Area.ExposureStops(areas, new Vector3(x, y, 1.7f));
        Assert.Equal(0f, At(21.6f));                                       // the player's start in the street
        Assert.Equal(0f, At(16.7078f + L2Area.BlendOutside));               // the wall's outer face: still the street's light
        Assert.Equal(2.5f, At(16.7078f), 3);                                // the wall's inner face: the eye has opened
        Assert.Equal(2.5f, At(16.57f));                                     // the game's camera pulled in against the wall inside
        Assert.Equal(2.5f, At(13.6f));                                      // the middle of the room
        float half = At(16.7078f + L2Area.BlendOutside / 2);
        Assert.Equal(1.25f, half, 2);                                       // half way through the doorway, half the stops
        Assert.True(At(17.05f) < At(16.9f) && At(16.9f) < At(16.75f));      // rising all the way through
        Assert.Equal(0f, At(13.6f, x: -12.08f - L2Area.BlendOutside));      // outside the end wall
        // Two areas: the greater wins where they meet.
        var two = new List<L2Area>(areas) { new() { Name = "store", Exposure = 1f, Box = new L2AreaBox { Origin = [-9f, 12f, 0f], Size = [2f, 2f, 3f] } } };
        Assert.Equal(2.5f, L2Area.ExposureStops(two, new Vector3(-8f, 13f, 1.5f)));
    }

    [Fact]
    public void The_layouts_lights_stand_beside_the_cooks_torches_and_in_place_of_the_torch_at_their_own_flame()
    {
        var layout = JsonNode.Parse("""
            { "lights": [ { "name": "smithy_forge", "position": [-5.4, 13.58, 1.5], "colour": [1.0, 0.5, 0.2], "intensity": 3.0, "range": 11.0 },
                          { "name": "smithy_lantern_door", "position": [-9.75, 16.2, 1.67], "intensity": 0.9 },
                          { "name": "dark", "position": [0, 0, 0], "intensity": 0 },
                          { "position": [0, 0, 0], "intensity": 1 },
                          { "name": "nowhere", "position": [0, 0], "intensity": 1 } ] }
            """);
        var placed = L2LayoutLights.FromLayout(layout);
        Assert.Equal(["smithy_forge", "smithy_lantern_door"], placed.Select(l => l.Name));
        Assert.Equal((new Vector3(1f, 0.5f, 0.2f), 3f, 11f), (placed[0].Colour, placed[0].Intensity, placed[0].Range));
        Assert.Equal((Vector3.One, 0.9f, L2LayoutLights.DefaultRange), (placed[1].Colour, placed[1].Intensity, placed[1].Range));
        Assert.Empty(L2LayoutLights.FromLayout(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));
        Assert.Empty(L2LayoutLights.FromLayout(null));

        // The cook's torches: the lantern's own flame 0.3 m over its placed light, the forge's flames a metre from
        // theirs, a street lantern far away. Only the first gives way.
        L2Light Torch(string name, Vector3 at) => new(name, L2Light.Point, at, new Vector3(1f, 0.62f, 0.28f), 2f, 8f);
        var torches = new[] { Torch("torch00", new Vector3(-9.75f, 16.2f, 1.97f)), Torch("torch01", new Vector3(-4.42f, 13.58f, 1.2f)), Torch("torch02", new Vector3(20f, -5f, 2.5f)) };
        var all = L2LayoutLights.With(torches, placed);
        Assert.Equal(["torch01", "torch02", "smithy_forge", "smithy_lantern_door"], all.Select(l => l.Name));
    }

    /// <summary>
    /// Ours: by day the street's lanterns are out. The layout says so (<c>lanterns.state</c>, with the
    /// reason and the flames' places), and the cook leaves out a torch at one of those flames; a room's lights and
    /// the other fires stay. A layout that says they burn, or says nothing, changes nothing.
    /// </summary>
    [Fact]
    public void By_day_the_layouts_street_lanterns_are_out_and_no_torch_stands_at_one()
    {
        var day = JsonNode.Parse("""
            { "lanterns": { "state": "out", "lit": false, "reason": "day 243, 10:00: the sun stands 41.2 degrees up",
                            "flames": [ [13.5, 2.1, 2.37], [20.0, -5.0, 2.52], [1, 2], "x" ] } }
            """);
        Assert.Equal("day 243, 10:00: the sun stands 41.2 degrees up", L2LayoutLights.LanternsOut(day));
        var flames = L2LayoutLights.LanternFlames(day);
        Assert.Equal([new Vector3(13.5f, 2.1f, 2.37f), new Vector3(20f, -5f, 2.52f)], flames);

        // The cook's torch stands a hand above its flame's middle; the forge's fire is nowhere near a lantern.
        L2Light Torch(string name, Vector3 at) => new(name, L2Light.Point, at, new Vector3(1f, 0.62f, 0.28f), 2f, 8f);
        var torches = new[] { Torch("torch00", new Vector3(13.5f, 2.1f, 2.47f)), Torch("torch01", new Vector3(-4.42f, 13.58f, 1.2f)), Torch("torch02", new Vector3(20f, -5f, 2.62f)) };
        Assert.Equal(["torch01"], L2LayoutLights.WithoutLanterns(torches, flames).Select(l => l.Name));

        var night = JsonNode.Parse("""{ "lanterns": { "state": "lit", "lit": true, "reason": "day 243, 22:00", "flames": [ [13.5, 2.1, 2.37] ] } }""");
        Assert.Null(L2LayoutLights.LanternsOut(night));
        Assert.Null(L2LayoutLights.LanternsOut(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));
        Assert.Null(L2LayoutLights.LanternsOut(null));
        Assert.Empty(L2LayoutLights.LanternFlames(null));
        Assert.Equal("the layout says so", L2LayoutLights.LanternsOut(JsonNode.Parse("""{ "lanterns": { "state": "out" } }""")));
    }
}
