// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Where the strike proper begins (<c>weapons.json</c>'s <c>activeWindow.strikeStarts</c>, ENGINE_SANDBOX:
/// <i>"that's just the wind up. The arc across the body should be the actual swing and damage"</i>): read and given to the
/// platform's contact, the same as the platform's own (<see cref="StrikeStart.Defaults"/>); the switch <c>window-from-windup</c>
/// empties them (the earlier build's windows) and leaves the rest of the rule; a bad row refused by name. Needs no game files.
/// </summary>
public sealed class StrikeStartDataTests
{
    static ContactConfig Registered(EnhancedModule? module = null)
    {
        var m = module ?? TestData.Sandbox();
        var b = new WorldBuilder(1);
        PerceptionServices.Register(b);
        ContactServices.Register(b);
        m.Register(b);
        return b.Build().Service<ContactConfig>();
    }

    static string Row(StrikeStart s) => $"{string.Join("+", s.Clips)} {s.Rule} {s.Metres:0.###}";

    [Fact]
    public void The_strike_starts_are_read_as_the_platform_s_own_and_the_switch_gives_the_windows_from_the_wind_up()
    {
        var window = Registered().Window;
        Assert.Equal(StrikeStart.Defaults.Select(Row), window.StrikeStarts.Select(Row));
        Assert.Equal(StrikeStartRule.Level, StrikeStart.For(window.StrikeStarts, "WINDUP:RIGHT")!.Rule);
        Assert.Equal(StrikeStartRule.Top, StrikeStart.For(window.StrikeStarts, "windup:overhead")!.Rule);
        Assert.Equal(StrikeStartRule.Forward, StrikeStart.For(window.StrikeStarts, "windup:thrust")!.Rule);
        Assert.Null(StrikeStart.For(window.StrikeStarts, "chrisAttackSword"));        // 1.1's own (swings-oneone) never
        Assert.Null(StrikeStart.For(window.StrikeStarts, "flat:chrisAttackSword"));   // nor the earlier build's flattened blows (attack-spin)

        var off = Registered(TestData.Sandbox(null, EnhancedModule.WindowFromWindUpSwitch)).Window;
        Assert.Empty(off.StrikeStarts);
        Assert.Equal(window with { StrikeStarts = [] }, off);
    }

    [Fact]
    public void A_bad_strike_start_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/weapons.json", s => s
            .Replace("\"rule\": \"top\"", "\"rule\": \"peak\"")
            .Replace("\"metres\": 0.1,", "\"metres\": 2,")
            .Replace("{ \"clips\": [\"windup:thrust\"], \"rule\": \"forward\",", "{ \"clips\": [], \"rule\": \"forward\", \"when\": 1,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("activeWindow: strikeStarts[1]: rule is 'peak'; it is level, top, forward");
        Has("activeWindow: strikeStarts[0]: 'metres' is 2; it must be 0 to 0.5");
        Has("activeWindow: strikeStarts[2]: no clips named");
        Has("activeWindow: strikeStarts[2]: unknown field 'when'");
    }
}
