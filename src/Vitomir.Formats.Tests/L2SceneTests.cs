// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The 1.1 scene a cell level stands for (ours; <see cref="L2Scene"/>): the
/// manifest's <c>scene</c> written and read back with what this build does not know kept, and a manifest
/// without one carrying none; the cook's <c>--scene</c> over the layout's own; the drop, keep and hide rules
/// by template name, pattern, class and id; and the layout's placements as anchors.
/// </summary>
public class L2SceneTests
{
    [Fact]
    public void A_manifests_scene_comes_back_as_written_and_a_manifest_without_one_has_none()
    {
        var m = new L2Manifest
        {
            Version = L2Manifest.Version2, Name = "oldtown",
            Scene = new L2Scene { Level = "P1_OTDY", Chapter = "OTDY_4_1", Drop = ["class:lantern"], Keep = ["lantern2"] },
        };
        var text = m.Write();
        Assert.Contains("\"scene\"", text);
        var back = L2Manifest.Read(text).Scene!;
        Assert.Equal(("P1_OTDY", "OTDY_4_1"), (back.Level, back.Chapter));
        Assert.Equal(["class:lantern"], back.Drop!);
        Assert.Equal(["lantern2"], back.Keep!);
        Assert.Null(back.Hide);
        // What a later build adds is kept through a read and a write.
        var later = L2Manifest.Read(text.Replace("\"level\": \"P1_OTDY\"", "\"level\": \"P1_OTDY\", \"edition\": \"day\""));
        Assert.Contains("\"edition\": \"day\"", later.Write());
        Assert.Null(L2Manifest.Read(new L2Manifest { Version = L2Manifest.Version2, Name = "bare" }.Write()).Scene);
    }

    [Fact]
    public void The_cooks_option_names_the_chapter_or_the_level_and_the_chapter_over_the_layouts_own()
    {
        var layout = JsonNode.Parse("""{"level": "P1_OTDY", "scene": {"chapter": "OTDY_6_1", "drop": ["barrel*"]}}""");
        var own = L2Scene.FromLayout(layout, null)!;
        Assert.Equal(("P1_OTDY", "OTDY_6_1"), (own.Level, own.Chapter));
        Assert.Equal(["barrel*"], own.Drop!);
        Assert.Equal("OTDY_4_1", L2Scene.FromLayout(layout, "OTDY_4_1")!.Chapter);
        var named = L2Scene.FromLayout(layout, "P2_OLDT/OLDT_18_1", "class:props, bench1")!;
        Assert.Equal(("P2_OLDT", "OLDT_18_1"), (named.Level, named.Chapter));
        Assert.Equal(["class:props", "bench1"], named.Drop!);
        Assert.Null(L2Scene.FromLayout(layout, "P1_OTDY/")!.Chapter);              // the chronicle's own chapter
        // A layout that names only its level carries no scene until the cook is asked; then the default rule drops the lanterns.
        var bare = JsonNode.Parse("""{"level": "P1_OTDY"}""");
        Assert.Null(L2Scene.FromLayout(bare, null));
        Assert.Equal(L2Scene.DefaultDrop, L2Scene.FromLayout(bare, "OTDY_4_1")!.Drop!);
        Assert.Null(L2Scene.FromLayout(JsonNode.Parse("{}"), "OTDY_4_1"));           // no level to stand for
    }

    [Fact]
    public void A_rule_is_a_name_a_pattern_a_class_or_one_thing_by_its_id_and_keep_takes_back()
    {
        var s = new L2Scene { Level = "P1_OTDY", Drop = ["class:lantern", "barrel*", "P1_OTDY/things/7", "CartEmpty"], Keep = ["lantern2", "P1_OTDY/things/3"] };
        Assert.True(s.Drops("P1_OTDY", 30, "lantern1_off"));
        Assert.False(s.Drops("P1_OTDY", 25, "lantern2"));                            // kept by name
        Assert.True(s.Drops("P1_OTDY", 20, "barrel1"));
        Assert.False(s.Drops("P1_OTDY", 3, "barrel3"));                              // kept by id
        Assert.False(s.Drops("P1_OTDY", 10, "container_barrel1"));                   // a pattern is the whole name
        Assert.True(s.Drops("P1_OTDY", 7, "flag_d"));
        Assert.False(s.Drops("OTDY_4_1", 7, "civ04merchant2_pe"));                   // another scene's seventh thing
        Assert.True(s.Drops("P1_OTDY", 24, "cartempty"));                            // names without regard to case
        Assert.False(s.Drops("P1_OTDY", 0, "bench1"));
        Assert.False(s.Drops("P1_OTDY", 0, "bench1", prop: true));                   // class:props is not in the rule
        Assert.True(new L2Scene { Drop = [L2Scene.AllProps] }.Drops("P1_OTDY", 0, "bench1", prop: true));
        Assert.False(new L2Scene { Drop = [L2Scene.AllProps] }.Drops("P1_OTDY", 0, "stranger"));
        Assert.True(new L2Scene { Drop = ["class:sign"] }.Drops("P1_OTDY", 15, "smithysign1"));
        // Hidden: 1.1's click exits unless the manifest says otherwise.
        Assert.True(s.Hides("P1_OTDY", 1, "tracker_thing"));
        Assert.False(s.Hides("P1_OTDY", 0, "bench1"));
        Assert.False(new L2Scene { Hide = [] }.Hides("P1_OTDY", 1, "tracker_thing"));
    }

    [Fact]
    public void The_layouts_placements_become_anchors_and_are_not_kept_twice()
    {
        var layout = JsonNode.Parse("""
            {"level": "P1_OTDY", "scene": {"chapter": "OTDY_4_1", "place": [
                {"id": "P1_OTDY/things/18", "position": [10.973, 12.803, 1.333]},
                {"id": "OTDY_4_1/things/0", "position": [4, -4.5, 0], "rotation": [0, 0, 90]},
                {"id": "P1_OTDY/starts/0", "position": [0, 0, 0]}, {"id": "OTDY_4_1/things/x", "position": [0, 0, 0]},
                {"id": "OTDY_4_1/things/2", "position": [1, "two", 3]}]}}
            """);
        var places = L2Scene.Places(layout);
        Assert.Equal(2, places.Count);
        Assert.Equal((L2Scene.ThingAtAnchor, "P1_OTDY", 18), (places[0].Kind, places[0].Scene, places[0].Index));
        Assert.Equal([0f, 0f, 0f], places[0].Rotation);
        Assert.Equal((L2Scene.ThingAnchor, "OTDY_4_1", 0), (places[1].Kind, places[1].Scene, places[1].Index));
        Assert.Equal([0f, 0f, 90f], places[1].Rotation);
        Assert.Equal(("OTDY_4_1", 12), L2Scene.ThingOf("OTDY_4_1/things/12")!.Value);
        Assert.Null(L2Scene.ThingOf("OTDY_4_1/starts/12"));
        var scene = L2Scene.FromLayout(layout, null)!;
        Assert.True(scene.Unknown is null || !scene.Unknown.ContainsKey("place"));
        Assert.Empty(L2Scene.Places(JsonNode.Parse("""{"level": "P1_OTDY"}""")));
    }
}
