// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Features;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The Fixes page in someone else's network game (ours): a switch the host governs
/// (<see cref="HostSwitches.Of"/>) shows the host's setting, greyed, and cannot be changed there,
/// with a line saying why; a switch that is each machine's own stays the player's to change; and
/// what the page keeps and writes is the player's own, which is theirs again when they leave. The
/// host's list is handed to the page rather than installed, because installing is process-wide.
///
/// And the page under the feature registry's rules: a switch that refines another
/// is drawn indented under it and locked while that one is off, and goes off with it; a tick the
/// registry refuses does not take and the hint box says why — pinned with invented features, since the
/// one real case (`matchedgait`) is covered by the lock; and a row's hint says, after its one line,
/// whether the switch changes the rules and whose it is. **The Fixes are two groups**, gameplay and visual, by
/// the registry's axis; and with the pointer on no row the hint box says whether the switches as ticked make a
/// game faithful or modded, and by which.
/// </summary>
public class FixesPageTests
{
    static (FrontEnd Menus, FixesPage Page) Open(IReadOnlyList<HostSwitches.Setting>? host, FeatureRegistry? registry = null)
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var page = new FixesPage(menus, registry)
        {
            Host = () => host,
            SavePath = Path.Combine(Path.GetTempPath(), $"vitomir-fixespage-{Guid.NewGuid():N}.ini"),
        };
        page.Open();
        return (menus, page);
    }

    static readonly HostSwitches.Setting[] Sent =
    [
        new("qol", "driving", false),                                 // the host's
        new("qol", "camera", false),                                  // each machine's own, whatever the host sent
    ];

    [Fact]
    public void A_host_governed_switch_shows_the_host_s_setting_greyed_and_says_why()
    {
        var (_, page) = Open(Sent);
        var pane = page.Pane;
        Assert.False(pane.Checks["fxqol_driving"]);
        Assert.Contains("fxqol_driving", pane.Locked);
        Assert.EndsWith(FixesPage.HostsLabel, pane.Texts["fxqol_drivinglabel"]);
        Assert.Equal(FixesPage.HostsGame, pane.Texts["fxhint"]);
        page.Hover(pane, "fxqol_driving");
        Assert.EndsWith(FixesPage.HostsGame, pane.Texts["fxhint"]);

        // The machine's own switch is untouched by what the host sent, and stays the player's to change.
        Assert.Equal(page.EditingQol.Camera, pane.Checks["fxqol_camera"]);
        Assert.DoesNotContain("fxqol_camera", pane.Locked);
        Assert.Equal("Camera", pane.Texts["fxqol_cameralabel"]);
    }

    [Fact]
    public void The_player_s_own_setting_is_kept_under_the_host_s_and_written_back_unchanged()
    {
        var (_, page) = Open(Sent);
        bool own = page.EditingQol.Driving;
        page.Pane.Checks["fxqol_driving"] = !own;                     // even were the box to move,
        page.Checked(page.Pane, "fxqol_driving");                     // a greyed box changes nothing
        Assert.Equal(own, page.EditingQol.Driving);
        Qol? written = null;
        page.Changed += (_, q) => written = q;
        page.Ok();
        Assert.NotNull(written);
        Assert.Equal(own, written!.Driving);
        File.Delete(page.SavePath!);
    }

    [Fact]
    public void At_home_every_switch_is_the_player_s_again()
    {
        var (_, page) = Open(null);
        var pane = page.Pane;
        Assert.Equal(page.EditingQol.Driving, pane.Checks["fxqol_driving"]);
        Assert.DoesNotContain("fxqol_driving", pane.Locked);
        Assert.Equal("Driving", pane.Texts["fxqol_drivinglabel"]);
        Assert.Equal(page.Standing(), pane.Texts["fxhint"]);              // what these switches make a game, not the host's-game line
        Assert.StartsWith("A game with these switches is ", pane.Texts["fxhint"]);
    }

    /// <summary>
    /// **The Fixes are two groups on the page, by the registry's own axis**: the ones that
    /// change the game, then the ones that change only what is seen or heard — a Fix is in the group its
    /// declaration computes, and nobody writes either list. The registry's own order (the tape's `fixes` line)
    /// is not the page's: `Fixes.All` is as it was.
    /// </summary>
    [Fact]
    public void The_fixes_are_two_groups_by_the_registry_s_axis_gameplay_first()
    {
        var (_, page) = Open(null);
        var rows = FixesPage.Rows().ToList();
        Assert.Equal(["fxhead0", "fxhead1", "fxhead2", "fxhead3"], rows.Where(r => r.Kind == FixesPage.RowKind.Heading).Select(r => r.Id));
        List<FixesPage.Row> Group(int g) => [.. rows.SkipWhile(r => r.Id != "fxhead" + g).Skip(1).TakeWhile(r => r.Kind != FixesPage.RowKind.Heading)];
        var registry = GameFeatures.Registry;

        Assert.Equal(registry.Of(FeatureKind.Fix).Where(f => f.Axis == FeatureAxis.Rules).Select(f => f.Name), Group(0).Select(r => r.Name));
        Assert.Equal(["bodycollision", "shapetorch", "netprops", "berserker", "wakeonhit", "gunreach", "heldattack"], Group(0).Select(r => r.Name));
        Assert.Equal(registry.Of(FeatureKind.Fix).Where(f => f.Axis == FeatureAxis.Presentation).Select(f => f.Name), Group(1).Select(r => r.Name));
        Assert.Equal(["movespeed", "matchedgait", "glowcolours", "liverollover", "soakedvoid"], Group(1).Select(r => r.Name));
        Assert.All(Group(0).Concat(Group(1)), r => Assert.Equal(FixesPage.RowKind.Fix, r.Kind));
        Assert.Equal(registry.Of(FeatureKind.Qol).Select(f => f.Name), Group(2).Select(r => r.Name));
        // No repair is on the page ("let's just remove the 'always applied repairs' section").
        Assert.DoesNotContain(rows, r => r.Kind == FixesPage.RowKind.Repair);
        // The platform's own settings last: streamer mode, which is no feature of the registry.
        Assert.Equal([FixesPage.StreamerRow], Group(3).Select(r => r.Id));
        Assert.Equal(FixesPage.RowKind.Platform, Group(3)[0].Kind);
        // Every switch is on the page once, and the page's rows are the registry's switches, four headings and streamer mode.
        int switches = registry.All.Count(f => f.Kind != FeatureKind.Repair);
        Assert.Equal(switches + 4 + 1, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());

        // The headings say which is which, and each fits its row: one line of the page's font, not clipped.
        var pane = page.Pane;
        Assert.Equal(FixesPage.Headings, Enumerable.Range(0, 4).Select(g => pane.Texts["fxhead" + g]));
        Assert.StartsWith("Gameplay Fixes", FixesPage.Headings[0]);
        Assert.StartsWith("Visual Fixes", FixesPage.Headings[1]);
        var font = new UiAssets(Install.Open()).Font("defaultfont10");
        Assert.All(FixesPage.Headings, h =>
        {
            int width = pane.Element("fxhead0")!.Size[0];
            Assert.Single(TextBoxText.Lines(h, true, width, c => font[c].Advance));
            Assert.True(h.Sum(c => font[c].Advance) <= width, $"\"{h}\" is {h.Sum(c => font[c].Advance)} wide in a row of {width}");
        });

        // The registers' own order is the registry's, not the page's.
        Assert.Equal(registry.Of(FeatureKind.Fix).Select(f => f.Name), new Fixes().All.Select(f => f.Name));
        Assert.Equal("movespeed", new Fixes().All.First().Name);
    }

    /// <summary>
    /// **What the settings as ticked make a game** (ours): while the pointer is on no row the hint box says, from
    /// the registry, that a game with these switches is faithful to 1.1's rules, or that it is modded and by
    /// which switches. It follows the boxes; a visual switch does not move it; and a row's own hint takes the box
    /// while the pointer is on the row.
    /// </summary>
    [Fact]
    public void With_the_pointer_on_no_row_the_hint_box_says_whether_these_switches_make_a_game_faithful_or_modded()
    {
        var (_, page) = Open(null);
        var pane = page.Pane;
        // From whatever was in force to the defaults: every Fix off, the quality-of-life switches as declared.
        foreach (var row in page.PageRows.Where(r => r.Feature is { Switchable: true }).Reverse())   // a refining row before the one it refines
            if (pane.Checks[row.Id] != row.Feature!.Default && !pane.Locked.Contains(row.Id)) Press(page, row.Id, row.Feature.Default);
        page.Hover(pane, null);
        // As it comes, a game is faithful: the quality-of-life switches on by default do not count.
        Assert.Equal("A game with these switches is faithful to 1.1's rules.", pane.Texts["fxhint"]);
        Assert.Equal(FixesPage.FaithfulGame, pane.Texts["fxhint"]);

        // A row's own hint while the pointer is on it; the line again when it leaves — on nothing, a heading, a button.
        page.Hover(pane, "fxqol_soakpie");
        Assert.StartsWith(FixesPage.Says(GameFeatures.Registry.Find("qol", "soakpie")!), pane.Texts["fxhint"]);
        foreach (string? off in new[] { null, "fxhead1", "fxokbutton", FixesPage.BarName, "fxhint" })
        {
            page.Hover(pane, "fxqol_soakpie");
            page.Hover(pane, off);
            Assert.Equal(FixesPage.FaithfulGame, pane.Texts["fxhint"]);
        }

        // No quality-of-life switch moves it, whichever way it is set — the three the host carries out among them
        // ("i think the QoL things should not count towards a modded save").
        foreach (var row in page.PageRows.Where(r => r.Feature is { Kind: FeatureKind.Qol }))
        {
            Press(page, row.Id, !pane.Checks[row.Id]);
            page.Hover(pane, null);
            Assert.Equal(FixesPage.FaithfulGame, pane.Texts["fxhint"]);
        }
        Assert.False(page.EditingQol.Driving || page.EditingQol.QuickEquip || page.EditingQol.DropCells);

        // A visual switch leaves a game faithful, whichever it is — the white flash among them.
        foreach (var row in page.PageRows.Where(r => r.Feature is { Kind: FeatureKind.Fix, Axis: FeatureAxis.Presentation }))
            if (!pane.Locked.Contains(row.Id)) Press(page, row.Id, !pane.Checks[row.Id]);
        Assert.True(page.Editing.SoakedVoid && page.Editing.MoveSpeed);
        page.Hover(pane, null);
        Assert.Equal(FixesPage.FaithfulGame, pane.Texts["fxhint"]);

        // A gameplay Fix makes it modded, by name, and the line follows the boxes.
        Press(page, "fxfix_gunreach", true);
        page.Hover(pane, null);
        Assert.Equal("A game with these switches is modded: Gun Reach changes 1.1's rules.", pane.Texts["fxhint"]);
        Press(page, "fxfix_wakeonhit", true);
        page.Hover(pane, null);
        Assert.Equal("A game with these switches is modded: Wake on Hit and Gun Reach change 1.1's rules.", pane.Texts["fxhint"]);
        Press(page, "fxqol_driving", true);
        page.Hover(pane, null);
        Assert.Equal("A game with these switches is modded: Wake on Hit and Gun Reach change 1.1's rules.", pane.Texts["fxhint"]);

        // In someone else's game the rules are the host's: the host's-game line, where it showed before.
        var (_, away) = Open(Sent);
        away.Hover(away.Pane, "fxqol_camera");
        away.Hover(away.Pane, null);
        Assert.Equal(FixesPage.HostsGame, away.Pane.Texts["fxhint"]);
    }

    /// <summary>A box pressed as the front end presses it: the box moved, then the page told.</summary>
    static void Press(FixesPage page, string box, bool to)
    {
        Assert.DoesNotContain(box, page.Pane.Locked);
        page.Pane.Checks[box] = to;
        page.Checked(page.Pane, box);
    }

    [Fact]
    public void A_switch_that_refines_another_is_drawn_indented_under_it()
    {
        var (_, page) = Open(null);
        var rows = FixesPage.Rows().ToList();
        int at = rows.FindIndex(r => r.Id == "fxfix_matchedgait");
        Assert.Equal("fxfix_movespeed", rows[at - 1].Id);                 // directly under the one it refines
        Assert.Equal("fxfix_movespeed", rows[at].Under);
        Assert.All(rows.Where(r => r.Id != "fxfix_matchedgait"), r => Assert.Null(r.Under));

        var nui = page.Pane;
        var (box, label) = (nui.Element("fxfix_matchedgait")!, nui.Element("fxfix_matchedgaitlabel")!);
        var (over, overLabel) = (nui.Element("fxfix_movespeed")!, nui.Element("fxfix_movespeedlabel")!);
        Assert.Equal(over.Pos[0] + FixesPage.Indent, box.Pos[0]);         // box and label moved right together,
        Assert.Equal(overLabel.Pos[0] + FixesPage.Indent, label.Pos[0]);
        Assert.Equal(overLabel.Pos[0] + overLabel.Size[0], label.Pos[0] + label.Size[0]);   // the label still ending where the others end
        Assert.Equal(over.Pos[1] - FixesPage.RowH, box.Pos[1]);           // one row down
        // And nobody else moved.
        Assert.Equal(over.Pos[0], nui.Element("fxfix_bodycollision")!.Pos[0]);
        Assert.Equal(over.Pos[0], nui.Element("fxqol_driving")!.Pos[0]);
    }

    [Fact]
    public void A_refining_switch_is_locked_while_the_one_it_refines_is_off_and_goes_off_with_it()
    {
        var (_, page) = Open(null);
        var pane = page.Pane;
        const string gait = "fxfix_matchedgait", speed = "fxfix_movespeed";
        Assert.False(pane.Checks[gait]);
        Assert.Contains(gait, pane.Locked);
        Assert.DoesNotContain(speed, pane.Locked);
        page.Hover(pane, gait + "label");
        Assert.EndsWith("Locked until Move Speed is on: it refines it.", pane.Texts["fxhint"]);

        Press(page, speed, true);
        Assert.DoesNotContain(gait, pane.Locked);                         // now it can be ticked
        Press(page, gait, true);
        Assert.True(page.Editing.MoveSpeed && page.Editing.MatchedGait);
        page.Hover(pane, gait);
        Assert.DoesNotContain("Locked", pane.Texts["fxhint"]);

        // Unticking the one it refines unticks it, locks it again, and the hint box says so —
        Press(page, speed, false);
        Assert.False(pane.Checks[gait]);
        Assert.Contains(gait, pane.Locked);
        Assert.False(page.Editing.MatchedGait);
        Assert.Equal("Matched Gait is switched off too: Matched Gait needs Move Speed: it refines it.", pane.Texts["fxhint"]);
        // — for as long as the pointer rests on the box just pressed (the page is asked every frame),
        page.Hover(pane, speed);
        Assert.StartsWith("Matched Gait is switched off too", pane.Texts["fxhint"]);
        // and until it moves to another row and back.
        page.Hover(pane, "fxfix_gunreach");
        page.Hover(pane, speed);
        Assert.StartsWith(FixesPage.Says(GameFeatures.Registry.Find("fixes", "movespeed")!), pane.Texts["fxhint"]);

        // OK writes an allowed pair.
        Fixes? written = null;
        page.Changed += (f, _) => written = f;
        page.Ok();
        Assert.False(written!.MatchedGait);
        File.Delete(page.SavePath!);
    }

    /// <summary>
    /// The general case, with invented features: two that decide the same thing, and one that needs another.
    /// </summary>
    [Fact]
    public void A_tick_the_registry_refuses_does_not_take_and_the_hint_box_says_why()
    {
        var door = new SwitchPoint("Door.Opens", "how a door opens", Rules: true);
        var registry = new FeatureRegistry(
        [
            new Feature(FeatureKind.Fix, "oiledhinge", "a door opens without a creak") { Takes = [door] },
            new Feature(FeatureKind.Fix, "selfclosing", "a door shuts behind you") { Takes = [door] },
            new Feature(FeatureKind.Qol, "doorbell", "a door rings when opened") { Requires = ["fixes.oiledhinge"] },
            new Feature(FeatureKind.Repair, "latch", "a shut door stays shut"),
        ]);
        var (_, page) = Open(null, registry);
        var pane = page.Pane;
        // Both invented Fixes take a rules switch point: the gameplay group, and the visual group's heading over nothing.
        // (And the platform's own settings, streamer mode, under the last heading: an earlier build.)
        Assert.Equal(["fxhead0", "fxfix_oiledhinge", "fxfix_selfclosing", "fxhead1", "fxhead2", "fxqol_doorbell", "fxhead3", FixesPage.StreamerRow], page.PageRows.Select(r => r.Id));
        Assert.DoesNotContain("fxfix_selfclosing", pane.Locked);          // not locked: it refines nothing

        Press(page, "fxfix_oiledhinge", true);
        Press(page, "fxfix_selfclosing", true);
        Assert.False(pane.Checks["fxfix_selfclosing"]);                   // the box went back
        Assert.True(pane.Checks["fxfix_oiledhinge"]);
        Assert.Equal("Not switched on: selfclosing and oiledhinge both decide how a door opens: only one can be on.", pane.Texts["fxhint"]);
        page.Hover(pane, "fxfix_selfclosing");                            // the pointer is still on it
        Assert.StartsWith("Not switched on", pane.Texts["fxhint"]);
        Assert.False(page.Ticks.On(registry.Find("fixes", "selfclosing")!));

        // The other way about works once the first is off.
        Press(page, "fxfix_oiledhinge", false);
        Press(page, "fxfix_selfclosing", true);
        Assert.True(pane.Checks["fxfix_selfclosing"]);

        // What a switch requires: refused without it, and switched off with it.
        Press(page, "fxqol_doorbell", true);
        Assert.False(pane.Checks["fxqol_doorbell"]);
        Assert.Equal("Not switched on: doorbell needs oiledhinge.", pane.Texts["fxhint"]);
        Press(page, "fxfix_selfclosing", false);
        Press(page, "fxfix_oiledhinge", true);
        Press(page, "fxqol_doorbell", true);
        Assert.True(pane.Checks["fxqol_doorbell"]);
        Press(page, "fxfix_oiledhinge", false);
        Assert.False(pane.Checks["fxqol_doorbell"]);
        Assert.Equal("doorbell is switched off too: doorbell needs oiledhinge.", pane.Texts["fxhint"]);
    }

    [Fact]
    public void A_row_s_hint_says_whether_the_switch_changes_the_rules_and_whose_it_is()
    {
        var (_, page) = Open(null);
        var pane = page.Pane;
        Feature F(string register, string name) => GameFeatures.Registry.Find(register, name)!;

        page.Hover(pane, "fxfix_gunreach");
        Assert.Equal(FixesPage.Says(F("fixes", "gunreach")) + ". Rules: the host's in a network game.", pane.Texts["fxhint"]);
        page.Hover(pane, "fxqol_soakpielabel");
        Assert.Equal(FixesPage.Says(F("qol", "soakpie")) + ". Picture, sound or controls: each machine's own.", pane.Texts["fxhint"]);
        // A repair's sentence, were one on the page (none is): what it changes, and whose it is.
        Assert.Equal("Rules: the host's in a network game.", FixesPage.Reach(F("repairs", "blastsight")));
        Assert.Equal("Picture, sound or controls: each machine's own.", FixesPage.Reach(F("repairs", "hoverpicture")));
        // A visual switch that is the host's all the same: the host runs the script that makes the flash.
        page.Hover(pane, "fxfix_soakedvoid");
        Assert.Equal(FixesPage.Says(F("fixes", "soakedvoid")) + ". Picture or sound: the host's in a network game.", pane.Texts["fxhint"]);
        // A quality-of-life switch the host carries out changes how you play, not the rules: it leaves a game faithful.
        page.Hover(pane, "fxqol_driving");
        Assert.Equal(FixesPage.Says(F("qol", "driving")) + ". How you play: the host's in a network game.", pane.Texts["fxhint"]);
        Assert.Equal(["quickequip", "driving", "dropcells", "heldshift", "spawnwalls", "partyorder"], GameFeatures.Registry.All.Where(f => FixesPage.Reach(f).StartsWith("How you play")).Select(f => f.Name));

        // In someone else's game a governed row's hint ends with the host's-game line, which says whose it is.
        var (_, away) = Open(Sent);
        away.Hover(away.Pane, "fxqol_driving");
        Assert.Equal(FixesPage.Says(F("qol", "driving")) + ". " + FixesPage.HostsGame, away.Pane.Texts["fxhint"]);
        away.Hover(away.Pane, "fxqol_camera");                            // each machine's own: as at home
        Assert.Equal(FixesPage.Says(F("qol", "camera")) + ". " + FixesPage.Reach(F("qol", "camera")), away.Pane.Texts["fxhint"]);
    }

    /// <summary>
    /// The hint box is six lines of `defaultFont10` (66 high: what a switch does on and off, plainly, then whose it is; the list box's width less its margins) and is drawn
    /// clipped: a hint that wraps to a fifth line loses it. Every row's hint at home — its one line and what the
    /// registry says of it, and the locked row's longer one — fits; so does everything a press can say.
    /// </summary>
    [Fact]
    public void Every_row_s_hint_fits_the_hint_box()
    {
        var files = Install.Open();
        var font = new UiAssets(files).Font("defaultfont10");
        var (_, page) = Open(null);
        var box = page.Pane.Element("fxhint")!;
        int room = (box.Size[1] + FixesPage.HintSpacing) / (font.LineHeight + FixesPage.HintSpacing);   // a unit between lines
        int Lines(string text) => TextBoxText.Lines(text, true, box.Size[0], c => font[c].Advance).Count;
        Assert.Equal(6, room);
        // Every switch a player can tick says what it does on and what off (FeatureHints), and nothing else is there.
        var switches = GameFeatures.Registry.All.Where(f => f.Switchable).Select(f => f.Id).ToHashSet();
        Assert.Empty(switches.Except(FeatureHints.All.Keys));
        Assert.Empty(FeatureHints.All.Keys.Except(switches));
        Assert.All(page.PageRows.Where(r => r.Feature is { Switchable: true }), r => Assert.StartsWith("On: ", page.Hint(r)));

        var over = page.PageRows.Where(r => r.Feature is not null || r.Kind == FixesPage.RowKind.Platform).Select(r => (r.Name, Lines: Lines(page.Hint(r)))).Where(r => r.Lines > room).ToList();
        Assert.True(over.Count == 0, "too long for the hint box: " + string.Join(", ", over.Select(r => $"{r.Name} ({r.Lines} lines)")));
        var gait = GameFeatures.Registry.Find("fixes", "matchedgait")!;
        var why = GameFeatures.Registry.Blocks(gait, [])!;
        Assert.InRange(Lines(FixesPage.Refused(why)), 1, room);
        Assert.InRange(Lines(FixesPage.AlsoOff(why)), 1, room);
        // And what the box says with the pointer on no row: at its longest, every switch that changes the rules named.
        Assert.InRange(Lines(FixesPage.Standing(GameFeatures.Registry, GameFeatures.Registry.All)), 1, room);
        Assert.InRange(Lines(FixesPage.FaithfulGame), 1, room);
        Assert.InRange(Lines(FixesPage.HostsGame), 1, room);
    }
}
