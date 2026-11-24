// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Assets.OneOne;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The four directions' wind-ups and ready poses as data (<c>Data/combat/windups.json</c>, read by the session into
/// <see cref="OneOneWindUps"/>): ENGINE_SANDBOX, each direction once, read without a problem, and the platform's own numbers
/// (<see cref="OneOneWindUps.Defaults"/>) are the file's; the direction table names each wind-up first, before the blows it is made
/// from; and a broken entry is said, not played.
/// </summary>
public sealed class WindUpsDataTests
{
    [Fact]
    public void The_wind_ups_read_clean_and_are_the_platform_s_own_numbers()
    {
        string json = File.ReadAllText(TestData.File("combat/windups.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ENGINE_SANDBOX", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(["left", "right", "overhead", "thrust"], doc.RootElement.GetProperty("blows").EnumerateArray().Select(b => b.GetProperty("direction").GetString()));
        var problems = new List<string>();
        var shapes = OneOneWindUps.Read(json, "windups", problems);
        Assert.Empty(problems);
        Assert.Equal(OneOneWindUps.Defaults, shapes);
    }

    [Fact]
    public void The_direction_table_plays_each_wind_up_first()
    {
        var text = File.ReadAllText(TestData.File("combat/directions.json"));
        foreach (var side in new[] { "left", "right" })
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, $"\"{side}\": \"stitched:{side}\\|").Count);
        Assert.Contains("\"overhead\": \"stitched:overhead\"", text);
        Assert.Contains("\"thrust\": \"stitched:thrust\"", text);
    }

    [Fact]
    public void Each_wind_up_has_its_own_blow_quality_references()
    {
        var q = TestData.Sandbox().Quality!;
        foreach (var d in new[] { "left", "right", "overhead", "thrust" })
        {
            string clip = OneOneWindUps.ClipName(d);
            Assert.Equal(("BroadSword", "Christof"), (q.Find(clip, "sword_one_hand", "Christof", "BroadSword")!.Weapon, q.Find(clip, "sword_one_hand", "Christof", "BroadSword")!.Character));
            Assert.Equal("Rapier", q.Find(clip, "sword_one_hand", "Christof", "Rapier")!.Weapon);
            Assert.InRange(q.Find(clip, "sword_one_hand", "KnightOfStJohn_torch", "weapTorchsword")!.QRef, 0.1, 0.8);   // the fallback
            if (d != "left") Assert.Equal("rudolfGuard", q.Find(clip, "sword_one_hand", "rudolfGuard", "Rapier")!.Character);
        }
    }

    [Fact]
    public void A_broken_wind_up_is_said_and_keeps_the_platform_s()
    {
        var problems = new List<string>();
        var shapes = OneOneWindUps.Read("""{ "blows": [ { "direction": "left", "join_at_frame": 2, "strike_from_frame": 5 }, { "direction": "sideways" }, { "direction": "right", "guard": [1, 2] } ] }""", "bad", problems);
        Assert.Equal(3, problems.Count);
        Assert.Equal(OneOneWindUps.Defaults, shapes);
    }
}
