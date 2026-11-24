// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets.OneOne;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The stitched part of the direction table (<c>Data/combat/directions-stitched.json</c>, for the direction work's
/// <c>directions.json</c>): it names the overhead and the thrust as the platform registers them, from the 1.1 clips and with the
/// ready points the platform stitches them with, every entry ENGINE_SANDBOX with its source.
/// </summary>
public sealed class StitchedDirectionsDataTests
{
    [Fact]
    public void The_stitched_directions_name_the_platform_s_clips_their_sources_and_ready_points()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestData.File("combat/directions-stitched.json")),
                                           new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        Assert.Equal("ENGINE_SANDBOX", root.GetProperty("status").GetString());
        var entries = root.GetProperty("directions").EnumerateArray().ToList();
        Assert.Equal([StitchedAttacks.Overhead, StitchedAttacks.Thrust], entries.Select(e => e.GetProperty("direction").GetString()));
        foreach (var e in entries)
        {
            string direction = e.GetProperty("direction").GetString()!;
            bool overhead = direction == StitchedAttacks.Overhead;
            Assert.Equal("onehand", e.GetProperty("carry").GetString());
            Assert.Equal(StitchedAttacks.ClipName(direction), e.GetProperty("clip").GetString());
            Assert.Equal(overhead ? OneOneStitched.OverheadFrom : OneOneStitched.ThrustFrom, e.GetProperty("from").GetString());
            Assert.Equal(overhead ? OneOneStitched.OverheadReadyFrame : OneOneStitched.ThrustReadyFrame, e.GetProperty("ready_frame").GetSingle());
            Assert.Equal("ENGINE_SANDBOX", e.GetProperty("status").GetString());
            Assert.StartsWith("ours", e.GetProperty("source").GetString());
        }
    }

    [Fact]
    public void Each_stitched_blow_has_its_blow_quality_reference_by_weapon_by_body_and_for_any_other()
    {
        var q = TestData.Sandbox().Quality!;
        foreach (var dir in new[] { StitchedAttacks.Overhead, StitchedAttacks.Thrust })
        {
            string clip = StitchedAttacks.ClipName(dir);
            var broad = q.Find(clip, "sword_one_hand", "Christof", "BroadSword");
            var rapier = q.Find(clip, "sword_one_hand", "Christof", "Rapier");
            var guard = q.Find(clip, "sword_one_hand", "rudolfGuard", "Rapier");
            var other = q.Find(clip, "sword_one_hand", "KnightOfStJohn_torch", "weapTorchsword");
            Assert.Equal(("BroadSword", "Christof"), (broad!.Weapon, broad.Character));
            Assert.Equal(("Rapier", "Christof"), (rapier!.Weapon, rapier.Character));
            Assert.Equal("rudolfGuard", guard!.Character);
            Assert.Null(other!.Character);                                          // the clip's fallback
            Assert.All(new[] { broad, rapier, guard, other }, r => Assert.InRange(r.QRef, 0.2, 0.6));
        }
        // The thrust meets with more of its speed into the body than the overhead (the point leads; the overhead often meets early in its arc).
        Assert.True(q.Find("stitched:thrust", null, "Christof", "BroadSword")!.QRef > q.Find("stitched:overhead", null, "Christof", "BroadSword")!.QRef);
    }

    [Fact]
    public void The_flattened_side_blows_have_their_own_references_and_1_1_s_keep_theirs()
    {
        var q = TestData.Sandbox().Quality!;
        foreach (var clip in new[] { "chrisAttackSword", "chrisAttack2Sword" })
        {
            string flat = OneOneStitched.FlatName(clip);
            Assert.Equal(("BroadSword", "Christof"), (q.Find(flat, "sword_one_hand", "Christof", "BroadSword")!.Weapon, q.Find(flat, "sword_one_hand", "Christof", "BroadSword")!.Character));
            Assert.Equal("Rapier", q.Find(flat, "sword_one_hand", "Christof", "Rapier")!.Weapon);
            Assert.InRange(q.Find(flat, "sword_one_hand", "KnightOfStJohn_torch", "weapTorchsword")!.QRef, 0.2, 0.5);   // the fallback (the backhand's 0.25 since the brain aims at the body)
            Assert.NotEqual(0.0, q.Find(clip, "sword_one_hand", "Christof", "BroadSword")!.QRef);   // 1.1's own (swings-oneone) keep theirs
        }
        Assert.Equal("rudolfGuard", q.Find("flat:chrisAttackSword", "sword_one_hand", "rudolfGuard", "Rapier")!.Character);
        Assert.NotNull(q.Find("flat:Christof_attackSword_util", "sword_one_hand", "KnightOfStJohn_torch", "weapTorchsword"));
    }
}
