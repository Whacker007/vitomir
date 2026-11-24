// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// <b>the addition to L2</b>: a layout's <c>traversal.ladders</c> become the manifest's
/// <c>traversal</c> (and only they: the meshes not to be climbed or stood on stay the cook's); what a ladder says that this
/// build does not know is kept; a ladder that is not sound is a fault; a manifest without the key is written as before.
/// </summary>
public class L2LadderTests
{
    [Fact]
    public void A_layout_s_ladders_come_into_the_manifest_and_back_and_its_other_traversal_keys_do_not()
    {
        var layout = JsonNode.Parse("""
            { "traversal": { "noClimbMeshes": ["cart"], "noStandMeshes": ["roof"],
                             "ladders": [ { "name": "tower", "foot": [1, 2, 0], "top": [1, 2, 4], "width": 0.5, "facing": 180, "note": "the yard's" },
                                          { "foot": [5, 0, 1], "top": [5.5, 0, 3], "facing": 0 } ] } }
            """);
        var faults = new List<string>();
        var t = L2Traversal.FromLayout(layout, faults);
        Assert.Empty(faults);
        Assert.NotNull(t);
        Assert.Equal(2, t!.Ladders!.Count);
        Assert.Equal("tower", t.Ladders[0].Name);
        Assert.Equal([1f, 2f, 4f], t.Ladders[0].Top);
        Assert.Equal(0.5f, t.Ladders[1].Width);     // not given: half a metre
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "t", Traversal = t };
        var text = m.Write();
        Assert.DoesNotContain("noClimbMeshes", text);
        var back = L2Manifest.Read(text);
        Assert.Equal(180f, back.Traversal!.Ladders![0].Facing);
        Assert.Equal("the yard's", back.Traversal.Ladders[0].Unknown!["note"].GetString());
        Assert.Equal(text, back.Write());
    }

    [Fact]
    public void A_manifest_without_ladders_is_written_as_before_and_a_layout_without_them_gives_none()
    {
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "t" };
        Assert.DoesNotContain("traversal", m.Write());
        Assert.Null(L2Traversal.FromLayout(JsonNode.Parse("""{ "traversal": { "noStandMeshes": ["roof"] } }""")));
        Assert.Null(L2Traversal.FromLayout(JsonNode.Parse("""{ "anchors": [] }""")));
        Assert.Null(L2Manifest.Read("""{ "format": "vitomir-l2", "version": 2, "name": "t" }""").Traversal);
    }

    [Theory]
    [InlineData("""{ "foot": [0, 0], "top": [0, 0, 3], "facing": 0 }""", "its foot")]
    [InlineData("""{ "foot": [0, 0, 0], "top": [0, 0, 0.2], "facing": 0 }""", "less than")]
    [InlineData("""{ "foot": [0, 0, 0], "top": [3, 0, 2], "facing": 0 }""", "45°")]
    [InlineData("""{ "foot": [0, 0, 0], "top": [0, 0, 3], "width": 0, "facing": 0 }""", "width")]
    public void A_ladder_that_is_not_sound_is_a_fault_and_left_out(string ladder, string said)
    {
        var faults = new List<string>();
        var t = L2Traversal.FromLayout(JsonNode.Parse($$"""{ "traversal": { "ladders": [ {{ladder}} ] } }"""), faults);
        Assert.Null(t);
        Assert.Contains(said, Assert.Single(faults));
    }
}
