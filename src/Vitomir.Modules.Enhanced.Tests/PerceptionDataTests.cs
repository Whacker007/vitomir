// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The enhanced module's perception (Data/perception/): the world's numbers and act table
/// and the ten kinds of observer read from the module's folder (SANDBOX) and given to the platform's perception, each
/// 1.1 character of the townsfolk mapped to a kind, the kinds' traits telling them apart, the night's sight of the
/// design's examples; and a field missing, unknown, out of range or of the wrong kind refused by name.
/// </summary>
public sealed class PerceptionDataTests
{
    static PerceptionConfig Registered(EnhancedModule? module = null)
    {
        var m = module ?? TestData.Sandbox();
        var b = new WorldBuilder(1);
        PerceptionServices.Register(b);
        m.Register(b);
        return b.Build().Service<PerceptionConfig>();
    }

    [Fact]
    public void The_world_s_numbers_the_act_table_and_ten_kinds_are_read_and_given_to_the_platform_s_perception()
    {
        var p = Registered();
        Assert.True(p.Sandbox);
        Assert.Equal(["beggar", "drunk", "guard", "merchant", "monk", "nightcitizen", "oldman", "townsman", "watchman", "younglady"], p.Kinds.Select(k => k.Name).Order(StringComparer.Ordinal));
        Assert.All(p.Kinds, k => Assert.True(k.Sandbox));
        Assert.Equal((1f, 0.3f), (p.Numbers.DayLight, p.Numbers.NightLight));
        Assert.Equal((0.5f, 0.6f, 0.8f, 1f, 1.3f), (p.Numbers.MotionStill, p.Numbers.MotionCrouched, p.Numbers.MotionWalk, p.Numbers.MotionRun, p.Numbers.MotionSprint));
        Assert.Equal(15f, p.Numbers.ShoutRadius);
        Assert.Equal(new ActResponse(0.9f, 0.5f, Awareness.Alarmed), p.Act(MasqueradeAct.Jump));
        Assert.Equal(new ActResponse(0.6f, 0.15f, Awareness.Alerted), p.Act(MasqueradeAct.Speed));
        Assert.Equal("townsman", p.DefaultKind);
        // The ladder's waits and the calming down (the proposal), the least time on a rung, the challenge that holds.
        var guard = p.Kinds[p.IndexOf("guard")].Numbers;
        Assert.Equal((0.4f, 1.5f, 2f, 30f, 240f), (guard.NoticeSeconds, guard.MakeOutSeconds, guard.ConcludeSeconds, guard.HoldSeconds, guard.CoolDownSeconds));
        var drunk = p.Kinds[p.IndexOf("drunk")].Numbers;
        Assert.Equal((1.5f, 5f, 6f, 10f, 60f), (drunk.NoticeSeconds, drunk.MakeOutSeconds, drunk.ConcludeSeconds, drunk.HoldSeconds, drunk.CoolDownSeconds));
        Assert.Equal(10f, p.Numbers.MinRungSeconds);
        Assert.Equal((2.5f, 6f, 10f), (p.Numbers.ChallengeMetres, p.Numbers.ChallengeWalkMetres, p.Numbers.ChallengeRunMetres));
    }

    [Fact]
    public void Each_of_the_townsfolk_s_characters_is_of_a_kind_and_the_keepers_of_the_peace_are_the_guard_and_the_watchman()
    {
        var p = Registered();
        string KindOf(string character) => p.Kinds[p.KindOf(character)].Name;
        Assert.Equal("oldman", KindOf("OldMan"));
        Assert.Equal("merchant", KindOf("Merchant"));
        Assert.Equal("monk", KindOf("Monk"));
        Assert.Equal("younglady", KindOf("YoungLady"));
        Assert.Equal("townsman", KindOf("Civ12male"));
        Assert.Equal("nightcitizen", KindOf("Civ11female"));
        Assert.Equal("drunk", KindOf("Civ07drunk"));
        Assert.Equal("beggar", KindOf("Civ02beggar"));
        Assert.Equal("watchman", KindOf("KnightOfStJohn_torch"));
        Assert.Equal("guard", KindOf("rudolfGuard"));
        Assert.Equal("townsman", KindOf("Christof"));                 // no kind names him: the default
        var keepers = p.Kinds.Select((k, i) => (k.Name, Role: p.RoleOf(i))).Where(k => k.Role == ObserverRole.Guard).Select(k => k.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["guard", "watchman"], keepers);
    }

    [Fact]
    public void At_night_a_guard_sees_a_running_body_12_m_off_one_in_a_bush_a_few_metres_and_by_day_30_m()
    {
        var p = Registered();
        int guard = p.IndexOf("guard"), old = p.IndexOf("oldman"), watch = p.IndexOf("watchman");
        Assert.Equal(12.15f, p.Sight(guard, p.Numbers.NightLight), 2);
        Assert.Equal(2.43f, p.Sight(guard, p.Numbers.NightLight) * 0.2f, 2);              // in a bush (conceal 0.2): "very close"
        Assert.Equal(30f, p.Sight(guard, p.Numbers.DayLight), 3);
        Assert.Equal(6f, p.Sight(guard, p.Numbers.DayLight) * 0.2f, 3);                    // the same bush by day
        Assert.True(p.Sight(old, p.Numbers.NightLight) < 4f);                             // the old man's poor eyes
        Assert.True(p.Sight(watch, p.Numbers.NightLight) > p.Sight(guard, p.Numbers.NightLight));   // the watchman's night eyes
    }

    [Fact]
    public void The_kinds_are_told_apart_by_their_traits()
    {
        var p = Registered();
        ObserverNumbers K(string name) => p.Kinds[p.IndexOf(name)].Numbers;
        Assert.True(K("drunk").Attention < 0.5f && K("drunk").Excitability < 0.3f && K("drunk").MakeOutSeconds >= 2f);
        Assert.True(K("beggar").Eyesight >= 0.9f && K("beggar").Bravery <= 0.2f && K("beggar").Voice <= 0.3f);
        Assert.True(K("nightcitizen").Excitability > K("townsman").Excitability && K("nightcitizen").Attention > K("townsman").Attention);
        Assert.True(K("watchman").NightEyes > K("guard").NightEyes && K("watchman").Duty >= 0.5f);
        Assert.True(K("guard").Bravery > K("townsman").Bravery && K("oldman").Eyesight < K("townsman").Eyesight);
        // No two kinds alike.
        Assert.Equal(p.Kinds.Count, p.Kinds.Select(k => NumberFields<ObserverNumbers>.Words(k.Numbers)).Distinct().Count());
    }

    [Fact]
    public void A_missing_an_unknown_a_wrong_and_an_out_of_range_field_are_each_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("perception/guard.json", s => s.Replace("\"Bravery\": 0.9,", "\"Braverie\": 0.9,"));
        copy.Edit("perception/drunk.json", s => s.Replace("\"Attention\": 0.25,", "\"Attention\": 1.5,"));
        copy.Edit("perception/sight.json", s => s.Replace("\"highest\": \"alerted\"", "\"highest\": \"furious\"").Replace("\"NightLight\": 0.3,", "\"NightLight\": \"dim\","));
        copy.Edit("perception/beggar.json", s => s.Replace("\"status\": \"SANDBOX\"", "\"status\": \"APPROVED\""));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:perception/guard: numbers: unknown number 'Braverie'");
        Has("enhanced:perception/guard: numbers: 'Bravery' is missing");
        Has("enhanced:perception/drunk: numbers: 'Attention' is 1.5; it is a share, 0 to 1");
        Has("enhanced:perception/sight: acts: 'Speed': highest is 'furious'");
        Has("enhanced:perception/sight: numbers: 'NightLight' must be a finite number");
        Has("enhanced:perception/beggar: status is 'APPROVED'");
        Assert.Equal(6, refused.Reasons.Count);
    }

    [Fact]
    public void A_ladder_that_does_not_rise_and_a_default_kind_that_is_none_are_refused()
    {
        string Refused(Func<string, string> edit)
        {
            using var copy = new TestData.Copy();
            copy.Edit("perception/sight.json", edit);
            return Assert.Single(Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder)).Reasons);
        }
        Assert.Contains("the ladder must rise", Refused(s => s.Replace("\"AlertedAt\": 0.4,", "\"AlertedAt\": 1.2,")), StringComparison.Ordinal);
        Assert.Contains("the challenge's distances must rise", Refused(s => s.Replace("\"ChallengeRunMetres\": 10.0,", "\"ChallengeRunMetres\": 5.0,")), StringComparison.Ordinal);
        Assert.Contains("defaultKind 'knight' is no kind", Refused(s => s.Replace("\"defaultKind\": \"townsman\"", "\"defaultKind\": \"knight\"")), StringComparison.Ordinal);
    }
}
