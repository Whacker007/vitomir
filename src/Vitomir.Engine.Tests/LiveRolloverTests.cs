// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The Fix <c>liverollover</c> (<see cref="LiveRollover"/>). 1.1 writes a rollover when the
/// hover changes and again only for a few events (the hovered thing hurt, an
/// item identified, a pane's own action), so a portrait's blood does not climb while the pointer
/// rests on it; 1.1 does the same, a rendering bug. Off, every rollover keeps
/// 1.1's rule; on, each is written afresh every frame.
/// </summary>
public class LiveRolloverTests
{
    static WorldThing Person(float health = 99.9f) => new()
    {
        Index = -1, Guid = 1, Scene = new NsdThing(), Template = "christof", DescriptionId = "", Type = ThingType.Player,
        Actor = new Actor { Type = ActorType.Vampire, Health = health, MaxHealth = 120f, IsPlayerThing = true },
    };

    [Fact]
    public void Liverollover_is_a_fix_off_by_default_and_turned_on_by_name_or_by_all()
    {
        var complaints = new List<string>();
        Assert.False(Fixes.Parse([], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings).LiveRollover);
        Assert.True(Fixes.Parse(["--fix", "liverollover"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings).LiveRollover);
        Assert.True(Fixes.Parse(["--fix", "all"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings).LiveRollover);
        Assert.Empty(complaints);
        Assert.Contains(new Fixes().All, f => f.Name == "liverollover" && !f.On);
        Assert.Equal(Net.HostSwitches.Governor.Machine, Net.HostSwitches.Of("fixes", "liverollover"));
    }

    [Fact]
    public void A_portrait_s_blood_climbs_under_a_resting_pointer_only_with_the_fix()
    {
        var christof = Person();
        christof.Actor!.Stat[(int)ActorStat.Blood] = 40f;
        var faithful = new Fixes();
        var live = new Fixes { LiveRollover = true };
        var shown = LiveRollover.Keep(null, IconRollover.PartyRow(christof, null), faithful);
        var kept = shown;
        christof.Actor.Stat[(int)ActorStat.Blood] = 43f;                           // three feed ticks, the pointer resting
        shown = LiveRollover.Keep(shown, IconRollover.PartyRow(christof, null), faithful);
        Assert.Equal("Health: 99 Blood: 40 Frenzy: 0", shown!.Value.Small[1].Text);   // 1.1's
        var fresh = LiveRollover.Keep(kept, IconRollover.PartyRow(christof, null), live);
        Assert.Equal("Health: 99 Blood: 43 Frenzy: 0", fresh!.Value.Small[1].Text);   // the fix's
        Assert.Same(kept!.Value.Key, fresh.Value.Key);                             // the large box's wait is not re-armed
        Assert.Null(LiveRollover.Keep(fresh, null, live));                          // off the row, gone either way
    }

    [Fact]
    public void The_world_hover_is_written_when_it_changes_or_is_hurt_and_every_frame_with_the_fix()
    {
        var hurt = Person(30f);
        var hover = new WorldHover();
        uint Tint(bool live) => hover.For(hurt, null, null, null, live).Name!.Value.Colour;
        uint wounded = Tint(false);
        hurt.Actor!.Health = 110f;                                                 // healed under the pointer
        Assert.Equal(wounded, Tint(false));                                        // 1.1: still the colour it arrived with
        hurt.Actor.Health = 100f;                                                  // hurt: written again
        uint rewritten = Tint(false);
        Assert.NotEqual(wounded, rewritten);
        Assert.Equal(Rollover.HealthTint(hurt.Actor), rewritten);
        hurt.Actor.Health = 119f;                                                  // healed again: the fix shows it
        Assert.Equal(Rollover.HealthTint(hurt.Actor), Tint(true));
        var other = Person(10f);                                                   // onto someone else: written afresh
        Assert.Equal(Rollover.HealthTint(other.Actor!), hover.For(other, null, null, null, false).Name!.Value.Colour);
    }

    /// <summary>The page still fits: the lowest row on show sits over the hint box (the page scrolls, <c>FixesScrollTests</c>).</summary>
    [Fact]
    public void The_fixes_page_s_rows_all_sit_above_its_hint_box()
    {
        var page = new FixesPage(new FrontEnd(_ => Vitomir.Formats.NuiPane.Parse(""), _ => throw new InvalidOperationException(), _ => null));
        Assert.True(page.LastRowY >= FixesPage.HintY + FixesPage.HintH,
                    $"the last row at {page.LastRowY}, the hint box's top at {FixesPage.HintY + FixesPage.HintH}");
        Assert.Contains(FixesPage.Rows(), r => r.Name == "liverollover");
    }

    /// <summary>A player's settings file that is not there, so the machine's own (where a Fix may be on) cannot leak into a test.</summary>
    static string NoUserSettings => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings", "user.ini");
}
