// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The discipline pane: its tabs and rows as the rebuild makes
/// them, the strip and row scrolls, the tab click, and the
/// new-discipline announcement that rides on the rebuild. The pane is the shipped
/// `ui/disciplinePane.nui` and the groups the shipped `game.ndg`.
/// </summary>
public class DisciplinePaneTests
{
    static (DisciplinePane Pane, WorldThing Who, DisciplineDefinitions Powers) Open(GameFiles files, ThingType type = ThingType.Player)
    {
        var powers = new DisciplineDefinitions(files);
        var pane = new DisciplinePane(new UiAssets(files).Pane("disciplinePane.nui")) { Powers = powers };
        var actor = new Actor { Type = ActorType.Vampire };
        var who = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = type, Actor = actor,
        };
        return (pane, who, powers);
    }

    static Discipline Learn(WorldThing who, DisciplineDefinitions powers, string name, int level = 0) =>
        who.Actor!.Disciplines.Add(powers[name]!, level);

    /// <summary>
    /// Tabs come in `game.ndg` order and rows in `game.ndd` order, whatever order the character
    /// learnt them in.
    /// </summary>
    [Fact]
    public void Tabs_follow_the_group_file_and_rows_the_discipline_file()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        Learn(who, powers, "Awe");
        Learn(who, powers, "Celerity");
        Learn(who, powers, "BloodHealing");
        Learn(who, powers, "Feed");
        pane.Rebuild(who, upAndOnTop: false);

        Assert.Equal(["DGRP_Common", "DGRP_Celerity", "DGRP_Presence"], pane.Groups.Select(g => g.Name));
        var rows = pane.Rows();
        Assert.Equal("Feed", rows[0].Discipline!.Name);
        Assert.Equal("BloodHealing", rows[1].Discipline!.Name);
        Assert.Null(rows[2].Discipline);
        Assert.Equal(1, rows[0].Dots);                            // level 0 shows one dot
    }

    [Fact]
    public void A_discipline_that_cannot_be_used_yet_shows_only_while_advancing()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        Learn(who, powers, "Awe", 2);
        Learn(who, powers, "DreadGaze", -1);
        pane.Rebuild(who, upAndOnTop: false);

        var rows = pane.Rows();
        Assert.Equal("Awe", rows[0].Discipline!.Name);
        Assert.Equal(3, rows[0].Dots);
        Assert.Null(rows[1].Discipline);
        Assert.False(rows[0].Learn);

        pane.Advancing = true;
        rows = pane.Rows();
        Assert.Equal("DreadGaze", rows[1].Discipline!.Name);
        Assert.Equal(0, rows[1].Dots);
        Assert.True(rows[1].Learn);
    }

    /// <summary>
    /// The flag is cleared on every rebuild, but a group is only **announced** when the pane is up and
    /// on top at that moment — so a discipline learnt while the pane is shut and rebuilt before it
    /// opens is never announced.
    /// </summary>
    [Fact]
    public void New_disciplines_are_announced_once_and_only_while_the_pane_is_up()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        var awe = Learn(who, powers, "Awe");
        awe.Flags |= Discipline.Unannounced;
        var announced = pane.Rebuild(who, upAndOnTop: true);
        Assert.Equal(["DGRP_Presence"], announced.Select(g => g.Name));
        Assert.Empty(pane.Rebuild(who, upAndOnTop: true));

        var cel = Learn(who, powers, "Celerity");
        cel.Flags |= Discipline.Unannounced;
        Assert.Empty(pane.Rebuild(who, upAndOnTop: false));
        Assert.Equal(0u, cel.Flags & Discipline.Unannounced);
        Assert.Empty(pane.Rebuild(who, upAndOnTop: true));
    }

    /// <summary>
    /// Five tabs show. The strip offset is held so five stay on show; a negative ask is ignored;
    /// clicking an end tab moves the strip a step that way; and a player-type character keeps both.
    /// </summary>
    [Fact]
    public void The_tab_strip_shows_five_and_an_end_tab_click_moves_it()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        foreach (var n in new[] { "Feed", "FeralWhispers", "HeightenedSenses", "Celerity", "CloakOfShadows", "Passion", "Command" })
            Learn(who, powers, n);
        pane.Rebuild(who, upAndOnTop: false);
        Assert.Equal(7, pane.Groups.Count());
        Assert.Equal(5, pane.Tabs().Count());
        Assert.False(pane.CanTabLeft);
        Assert.True(pane.CanTabRight);

        pane.SetOffset(9);                                        // leaves fewer than five: the last five
        Assert.Equal(2, pane.Offset);
        pane.SetOffset(-1);                                       // ignored, not held to 0
        Assert.Equal(2, pane.Offset);

        pane.ClickTab(4);                                         // the right-hand end tab
        Assert.Equal(6, pane.Current);
        Assert.Equal(2, pane.Offset);                             // already at the end
        pane.ClickTab(0);                                         // the left-hand end tab
        Assert.Equal(2, pane.Current);
        Assert.Equal(1, pane.Offset);
        Assert.Equal(2, who.Actor!.DisciplineTab);
        Assert.Equal(1, who.Actor.DisciplineTabOffset);

        pane.ScrollTabs(-1);
        Assert.Equal(0, pane.Offset);
        Assert.Equal(0, who.Actor.DisciplineTabOffset);

        // Reopened, the pane goes back to where a player-type character left it.
        pane.Rebuild(who, upAndOnTop: true);
        Assert.Equal(2, pane.Current);
        Assert.Equal(0, pane.Offset);
    }

    [Fact]
    public void Someone_other_than_a_player_is_not_remembered()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files, ThingType.Actor);
        foreach (var n in new[] { "Feed", "FeralWhispers", "HeightenedSenses", "Celerity", "CloakOfShadows", "Passion" })
            Learn(who, powers, n);
        pane.Rebuild(who, upAndOnTop: true);
        pane.ClickTab(4);
        Assert.Equal(0, who.Actor!.DisciplineTab);
        pane.Rebuild(who, upAndOnTop: true);
        Assert.Equal(0, pane.Current);
    }

    /// <summary>
    /// The common group has seven: the rows scroll one at a time, and a scroll that would leave
    /// fewer than five entries below it is refused rather than clamped.
    /// </summary>
    [Fact]
    public void The_rows_scroll_only_while_five_still_fit_below()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        foreach (var d in powers.InGroup("DGRP_Common")) who.Actor!.Disciplines.Add(d, 0);
        pane.Rebuild(who, upAndOnTop: false);
        Assert.False(pane.CanScrollUp);
        Assert.True(pane.CanScrollDown);

        pane.ScrollRows(2);
        Assert.Equal(2, pane.FirstRow);
        Assert.False(pane.CanScrollDown);
        Assert.True(pane.CanScrollUp);
        Assert.Equal("BloodStrength", pane.Rows()[0].Discipline!.Name);
        pane.ScrollRows(3);                                       // 3 + 5 > 7: refused
        Assert.Equal(2, pane.FirstRow);
    }

    [Fact]
    public void The_pointer_finds_the_learn_button_before_the_row_under_it_only_while_advancing()
    {
        using var files = Install.Open();
        var (pane, who, powers) = Open(files);
        Learn(who, powers, "Celerity");
        pane.Rebuild(who, upAndOnTop: false);
        // advDisc0 sits on slot0text's right-hand end.
        Assert.Equal((DisciplinePane.Part.Text, 0), pane.At(520, 345));
        pane.Advancing = true;
        Assert.Equal((DisciplinePane.Part.Learn, 0), pane.At(520, 345));
        Assert.Equal((DisciplinePane.Part.Slot, 0), pane.At(370, 345));
        Assert.Equal((DisciplinePane.Part.Tab, 0), pane.At(390, 410));
        Assert.Equal((DisciplinePane.Part.None, -1), pane.At(430, 410));   // no second tab
        Assert.True(pane.Covers(500, 300));
        Assert.False(pane.Covers(100, 300));
    }
}
