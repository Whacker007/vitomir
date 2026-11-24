// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// A cell level's ground lifts (ours; <see cref="L2Lift"/>):
/// where a level made on a 1.1 level's plan has ground that is deliberately not 1.1's, and by how much, so
/// that the things of 1.1's scene rise with it. Pinned here: which cell a point takes
/// its lift from (the one that holds it, the patch laid out from its corner of least x and y, rows going +y,
/// as the generator's plan grid is), that a point outside a patch is not lifted and overlapping patches add
/// up; the manifest's <c>lifts</c> written and read back with the generator's note kept; a manifest without
/// the key saying nothing; and the layout's patches reaching the cook, an unsound one named rather than
/// dropped in silence. The numbers are made up: a patch 3 cells by 2.
/// </summary>
public class L2LiftTests
{
    /// <summary>Three cells along x by two along y, half a metre each, from (10, 20): the row at y 20..20.5 is 0, 1, 2 and the row above it 3, 4, 5.</summary>
    static L2Lift Patch() => new() { Name = "court", X0 = 10f, Y0 = 20f, Cell = 0.5f, Nx = 3, Ny = 2, Values = [0f, 1f, 2f, 3f, 4f, 5f] };

    [Fact]
    public void A_point_takes_the_lift_of_the_cell_that_holds_it_rows_going_up_y()
    {
        var p = Patch();
        Assert.Equal(0f, p.At(10.25f, 20.25f));
        Assert.Equal(1f, p.At(10.75f, 20.25f));
        Assert.Equal(2f, p.At(11.49f, 20.01f));
        Assert.Equal(3f, p.At(10.01f, 20.5f));                    // a cell's lower edge is its own
        Assert.Equal(4f, p.At(10.5f, 20.99f));
        Assert.Equal(5f, p.At(11.25f, 20.75f));
        // No blending between cells: all of a cell has its centre's value.
        Assert.Equal(p.At(10.51f, 20.51f), p.At(10.99f, 20.99f));
    }

    [Fact]
    public void A_point_outside_the_patch_is_not_lifted()
    {
        var p = Patch();
        Assert.Equal(0f, p.At(9.99f, 20.75f));
        Assert.Equal(0f, p.At(11.5f, 20.75f));                    // the far edge is outside
        Assert.Equal(0f, p.At(10.75f, 19.99f));
        Assert.Equal(0f, p.At(10.75f, 21f));
        Assert.Equal(0f, p.At(-1000f, 1000f));
        Assert.Equal(0f, L2Lift.At(null, 10.75f, 20.75f));        // a level without lifts
        Assert.Equal(0f, L2Lift.At([], 10.75f, 20.75f));
    }

    [Fact]
    public void Patches_that_overlap_add_up()
    {
        var second = new L2Lift { Name = "step", X0 = 10.5f, Y0 = 20.5f, Cell = 1f, Nx = 1, Ny = 1, Values = [0.25f] };
        Assert.Equal(4.25f, L2Lift.At([Patch(), second], 10.75f, 20.75f));
        Assert.Equal(1f, L2Lift.At([Patch(), second], 10.75f, 20.25f));      // only the first holds it
        Assert.Equal(0.25f, L2Lift.At([Patch(), second], 11.25f, 21.25f));   // only the second
    }

    [Fact]
    public void A_manifests_lifts_come_back_as_written_with_the_generators_note()
    {
        var layout = JsonNode.Parse("""
            { "level": "P1_OTDY", "lifts": [ { "name": "university_court", "x0": 40, "y0": 2.5, "cell": 0.5, "nx": 2, "ny": 2,
                                               "values": [0, 1.78, 0.5, 1.0], "note": "the court made level with the street" } ] }
            """);
        var lifts = L2Lift.FromLayout(layout)!;
        var text = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", Lifts = lifts }.Write();
        Assert.Contains("\"lifts\"", text);
        Assert.Contains("the court made level with the street", text);
        var back = L2Manifest.Read(text).Lifts!;
        var one = Assert.Single(back);
        Assert.Equal("university_court", one.Name);
        Assert.Equal((40f, 2.5f, 0.5f, 2, 2), (one.X0, one.Y0, one.Cell, one.Nx, one.Ny));
        Assert.Equal([0f, 1.78f, 0.5f, 1.0f], one.Values);
        Assert.Equal(1.78f, L2Lift.At(back, 40.75f, 2.75f));
        Assert.Null(one.Fault());
        Assert.Equal("university_court: 2 × 2 cells of 0.5 m from (40, 2.5), 0 to 1.78 m", one.ToString());
    }

    [Fact]
    public void A_manifest_without_lifts_says_nothing_and_lifts_nothing()
    {
        var text = new L2Manifest { Version = L2Manifest.Version2, Name = "round14" }.Write();
        Assert.DoesNotContain("\"lifts\"", text);
        var back = L2Manifest.Read(text);
        Assert.Null(back.Lifts);
        Assert.Equal(0f, L2Lift.At(back.Lifts, 48f, 16f));
        Assert.Null(L2Lift.FromLayout(null));
        Assert.Null(L2Lift.FromLayout(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));            // the layouts
        Assert.Null(L2Lift.FromLayout(JsonNode.Parse("""{ "lifts": "none" }""")));
        Assert.Null(L2Lift.FromLayout(JsonNode.Parse("""{ "lifts": [] }""")));
    }

    [Fact]
    public void A_patch_that_is_not_sound_is_left_out_and_named()
    {
        var faults = new List<string>();
        var layout = JsonNode.Parse("""
            { "lifts": [ { "name": "short",  "x0": 0, "y0": 0, "cell": 0.5, "nx": 2, "ny": 2, "values": [0, 1, 2] },
                         { "name": "flat",   "x0": 0, "y0": 0, "cell": 0,   "nx": 1, "ny": 1, "values": [1] },
                         { "name": "empty",  "x0": 0, "y0": 0, "cell": 1,   "nx": 0, "ny": 3, "values": [] },
                         { "name": "words",  "x0": 0, "y0": 0, "cell": 1,   "nx": 1, "ny": 1, "values": ["high"] },
                         { "name": "sound",  "x0": 0, "y0": 0, "cell": 1,   "nx": 1, "ny": 1, "values": [1.5] } ] }
            """);
        var lifts = L2Lift.FromLayout(layout, faults)!;
        Assert.Equal("sound", Assert.Single(lifts).Name);
        Assert.Equal(4, faults.Count);
        Assert.Contains("short", faults[0]);
        Assert.Contains("3 values for 2 × 2 = 4 cells", faults[0]);
        Assert.Contains("flat", faults[1]);
        Assert.Contains("empty", faults[2]);
        Assert.Contains("lift 3", faults[3]);                     // it could not be read at all: named by its place in the list
        // Nothing sound at all: no lifts, and the faults still said.
        faults.Clear();
        Assert.Null(L2Lift.FromLayout(JsonNode.Parse("""{ "lifts": [ { "name": "short", "cell": 1, "nx": 1, "ny": 1, "values": [] } ] }"""), faults));
        Assert.Single(faults);
    }

    [Fact]
    public void A_relayout_puts_the_layouts_lifts_into_a_cooked_manifest()
    {
        var manifest = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown" };
        var said = L2LayoutExits.Relay(manifest, JsonNode.Parse("""
            { "lifts": [ { "name": "university_court", "x0": 40, "y0": 2, "cell": 0.5, "nx": 1, "ny": 1, "values": [1.78] },
                         { "name": "short", "x0": 0, "y0": 0, "cell": 0.5, "nx": 2, "ny": 2, "values": [0] } ] }
            """));
        Assert.Equal("university_court", Assert.Single(manifest.Lifts!).Name);
        Assert.Contains("1 ground lift", said);
        Assert.Contains("NOT TAKEN", said);                       // the unsound one is said, not dropped in silence
        // A layout without lifts leaves the manifest's own alone.
        L2LayoutExits.Relay(manifest, JsonNode.Parse("""{ "level": "P1_OTDY" }"""));
        Assert.Single(manifest.Lifts!);
    }
}
