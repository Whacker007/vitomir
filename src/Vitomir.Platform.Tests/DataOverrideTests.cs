// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json.Nodes;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The loader's data overrides on an asset's bytes — RFC 6901 pointers (escapes, list indices), replace, remove and
/// add, what each did with the asset's hash before and after, the problems named; their note for a tape's head; the light's schedule.
/// </summary>
public sealed class DataOverrideTests
{
    static readonly AssetId Asset = AssetId.Of("enhanced", "combat/x");

    static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Replace_remove_and_add_by_pointer_say_what_they_did_and_leave_the_rest_as_it_was()
    {
        const string text = """
            { "schema": "S", "a/b": { "~c": 1 }, "list": [1, 2, 3], "keep": 0.70, // a comment
              "obj": { "x": true } }
            """;
        var applied = new List<AppliedDataOverride>();
        var after = DataOverrides.Apply(Asset, Bytes(text), [
            new DataOverride(Asset, "/a~1b/~0c", "2"),
            new DataOverride(Asset, "/list/1", null, DataOverrideOp.Remove),
            new DataOverride(Asset, "/list/-", "9", DataOverrideOp.Add),
            new DataOverride(Asset, "/obj/y", "\"new\"", DataOverrideOp.Add),
        ], applied);
        var root = JsonNode.Parse(after)!;
        Assert.Equal(2, root["a/b"]!["~c"]!.GetValue<int>());
        Assert.Equal("[1,3,9]", root["list"]!.ToJsonString());
        Assert.Equal("new", root["obj"]!["y"]!.GetValue<string>());
        Assert.Contains("0.70", Encoding.UTF8.GetString(after), StringComparison.Ordinal);   // an untouched number keeps its text
        Assert.Equal(4, applied.Count);
        Assert.Equal(("1", "2"), (applied[0].Before, applied[0].After));
        Assert.Equal(("2", null), (applied[1].Before, applied[1].After));
        Assert.Equal(DataOverrides.Sha256(Bytes(text)), applied[0].Sha256Before);
        Assert.Equal(applied[0].Sha256After, applied[1].Sha256Before);
        Assert.Equal(DataOverrides.Sha256(after), applied[^1].Sha256After);
    }

    [Fact]
    public void A_pointer_that_names_nothing_or_is_no_pointer_is_refused_by_name()
    {
        var applied = new List<AppliedDataOverride>();
        var e = Assert.Throws<DataOverrideException>(() => DataOverrides.Apply(Asset, Bytes("""{ "a": { "b": 1 } }"""), [new DataOverride(Asset, "/a/c", "2")], applied));
        Assert.Contains("/a/c", e.Message);
        Assert.Throws<DataOverrideException>(() => DataOverrides.Apply(Asset, Bytes("""{ "a": [1] }"""), [new DataOverride(Asset, "/a/5", "2")], applied));
        Assert.Throws<DataOverrideException>(() => DataOverrides.Apply(Asset, Bytes("""{ "a": 1 }"""), [new DataOverride(Asset, "a", "2")], applied));
        Assert.Throws<DataOverrideException>(() => DataOverrides.Apply(Asset, Bytes("""{ "a": 1 }"""), [new DataOverride(Asset, "/a~2", "2")], applied));
        Assert.Empty(applied);
        Assert.True(JsonPointer.TryParse("", out var none, out _) && none.Length == 0);
        Assert.Equal(2, JsonPointer.Existing(JsonNode.Parse("""{ "a": { "b": 1 } }"""), ["a", "b", "c"]));
    }

    [Fact]
    public void The_overrides_note_round_trips_and_every_leaf_has_its_pointer()
    {
        var list = new List<DataOverride>
        {
            new(Asset, "/a/0", "0.25"),
            new(Asset, "/b", null, DataOverrideOp.Remove),
            new(Asset, "/c/-", "{\"k\":[1,2]}", DataOverrideOp.Add),
        };
        var back = DataOverrides.FromNote(DataOverrides.ToNote(list));
        Assert.Equal(list.Count, back.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal((list[i].Asset, list[i].Pointer, list[i].Op), (back[i].Asset, back[i].Pointer, back[i].Op));
            Assert.Equal(list[i].Value is null ? null : JsonNode.Parse(list[i].Value!)!.ToJsonString(), back[i].Value is null ? null : JsonNode.Parse(back[i].Value!)!.ToJsonString());
        }
        Assert.Empty(DataOverrides.FromNote(""));
        var leaves = JsonPointer.Leaves(JsonNode.Parse("""{ "a": [1, { "b/c": "x" }], "e": {}, "f": null }""")).Select(l => l.Pointer).ToList();
        Assert.Equal(["/a/0", "/a/1/b~1c", "/e", "/f"], leaves);
    }

    [Fact]
    public void The_light_given_is_a_level_or_steps_held_until_the_next()
    {
        Assert.True(LightSchedule.TryParse("0.45", out var flat, out _));
        Assert.Equal(0.45f, flat.LevelAt(0));
        Assert.Equal(0.45f, flat.LevelAt(100_000));
        Assert.True(LightSchedule.TryParse("0:0.2;1:0.9;2.5:0", out var steps, out _));
        Assert.Equal(0.2f, steps.LevelAt(119));
        Assert.Equal(0.9f, steps.LevelAt(120));
        Assert.Equal(0f, steps.LevelAt(300));
        Assert.False(LightSchedule.TryParse("1.5", out _, out _));
        Assert.False(LightSchedule.TryParse("0:0.2;0:0.3", out _, out _));
        Assert.False(LightSchedule.TryParse("", out _, out _));
    }
}
