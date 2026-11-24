// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Formats;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The status bar as state: where the rows land, which tracks a character gets, what a click on the
/// bar swallows and what it does. None of this needs a GPU, which is the point of keeping it out of
/// the renderer — the row layout is the original bar's formula, and a formula
/// wants a test.
/// </summary>
public class StatusBarTests
{
    static (Location Place, StatusBar Bar, Minds Minds) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        return (place, new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place), minds);
    }

    /// <summary>
    /// A party of one puts its row in the middle of the screen. The pane declares all four rows at
    /// the same place precisely because the layout is what positions them: the block of rows is
    /// centred on 320 and each row is 80 further right than the last.
    /// </summary>
    [Fact]
    public void One_party_member_puts_the_row_in_the_middle()
    {
        using var files = Install.Open();
        var (_, bar, _) = Open(files);

        var row = bar["statusIndicators0"]!;
        Assert.True(row.Shown);
        Assert.Equal(UiAssets.Width / 2, row.X + row.Width / 2);
        Assert.Equal(StatusBar.RowY, row.Y);
        // The hotspot above it shares the column and sits two pixels higher.
        Assert.Equal(row.X, bar["statusHotspot0"]!.X);
        Assert.Equal(StatusBar.HotspotY, bar["statusHotspot0"]!.Y);
        // And the three rows the party has not got are down.
        for (int i = 1; i < StatusBar.Rows; i++) Assert.False(bar[$"statusIndicators{i}"]!.Shown);
    }

    /// <summary>
    /// The bars inside a row move with it, because the engine's own move walks the control's
    /// children and the pane parents all five bars and the pip to the row's backdrop.
    /// </summary>
    [Fact]
    public void A_rows_bars_keep_their_place_inside_it()
    {
        using var files = Install.Open();
        var (_, bar, _) = Open(files);
        var pane = bar.Pane;

        var row = bar["statusIndicators0"]!;
        int dx = row.X - row.Element.Pos[0], dy = row.Y - row.Element.Pos[1];
        Assert.NotEqual(0, dx);
        foreach (var name in new[] { "statusHealth0", "statusBlood0", "statusMana0", "statusFrenzy0", "statusFaith0", "statusParty0" })
        {
            var child = bar[name]!;
            Assert.Equal("statusindicators0", child.Element.Get("parent"));
            Assert.Equal(child.Element.Pos[0] + dx, child.X);
            Assert.Equal(child.Element.Pos[1] + dy, child.Y);
        }
        _ = pane;
    }

    /// <summary>
    /// `ACTOR_TYPE` picks the pair of tracks: a human spends mana and has faith, and
    /// everything else has blood and can frenzy. Christof is human at the start of the game and a
    /// vampire from the second chapter on, and the bar follows him across.
    /// </summary>
    [Fact]
    public void A_human_shows_mana_and_faith_and_a_vampire_blood_and_frenzy()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;

        me.Type = ActorType.Human;
        bar.Update();
        Assert.True(bar["statusMana0"]!.Shown);
        Assert.True(bar["statusFaith0"]!.Shown);
        Assert.False(bar["statusBlood0"]!.Shown);
        Assert.False(bar["statusFrenzy0"]!.Shown);

        me.Type = ActorType.Vampire;
        bar.Update();
        Assert.False(bar["statusMana0"]!.Shown);
        Assert.False(bar["statusFaith0"]!.Shown);
        Assert.True(bar["statusBlood0"]!.Shown);
        Assert.True(bar["statusFrenzy0"]!.Shown);

        // Health is the one track everybody has.
        Assert.True(bar["statusHealth0"]!.Shown);
    }

    /// <summary>
    /// Frenzy and faith are measured against 100, which is what the progress bar's constructor
    /// leaves in it and what the pane sets explicitly for frenzy; blood and mana are
    /// measured against the character's own pool.
    /// </summary>
    [Fact]
    public void The_second_track_is_a_percentage_and_the_first_is_a_pool()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 20f;
        me.Stat[(int)ActorStat.Blood] = 5f;
        me.Stat[(int)ActorStat.Frenzy] = 25f;
        bar.Update();

        Assert.Equal(20f, bar["statusBlood0"]!.Max);
        Assert.Equal(0.25f, bar["statusBlood0"]!.Fraction, 3);
        Assert.Equal(100f, bar["statusFrenzy0"]!.Max);
        Assert.Equal(0.25f, bar["statusFrenzy0"]!.Fraction, 3);
    }

    /// <summary>
    /// The bar swallows a click anywhere on its own artwork, control or no control — the original
    /// settles that at the pane level, testing the pane's quads and nothing else, and
    /// only then lets the world have the click. So the top corners above the bar's
    /// 128-pixel art are *not* the bar, and a click there still reaches the ground.
    /// </summary>
    [Fact]
    public void The_bar_takes_a_click_on_its_art_and_lets_the_corners_through()
    {
        using var files = Install.Open();
        var (_, bar, _) = Open(files);

        Assert.True(bar.Covers(320, 60));        // the middle of the bar
        Assert.True(bar.Covers(0, 0));           // the bottom left corner
        Assert.True(bar.Covers(320, 130));       // the strip above the art, where the message box is
        Assert.False(bar.Covers(20, 130));       // to the left of that strip: the world
        Assert.False(bar.Covers(620, 135));      // and to the right of it
        Assert.False(bar.Covers(320, 200));      // well clear of the whole thing
    }

    /// <summary>
    /// The three stance buttons are a radio group the pane does not declare as one: the engine
    /// checks the one that matches and clears the other two by hand, which is why one of
    /// them is always down. Neutral is where a character starts — every one of the ten shipped
    /// saves stores 1 in all four of the party's stance slots.
    /// </summary>
    [Fact]
    public void One_stance_button_is_always_down_and_it_starts_on_neutral()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        Assert.Equal(PartyStance.Neutral, me.Stance);

        string[] radios = ["sbAttackRadio", "sbNeutralRadio", "sbDefenseRadio"];
        Assert.Equal(1, radios.Count(n => bar[n]!.Checked));
        Assert.True(bar["sbNeutralRadio"]!.Checked);

        foreach (var (name, stance) in radios.Zip((PartyStance[])[PartyStance.Attack, PartyStance.Neutral, PartyStance.Defense]))
        {
            var button = bar[name]!;
            var took = bar.Click(button.X + 1, button.Y + 1, InputModifier.None);
            Assert.True(took.Consumed);
            Assert.Equal(stance, me.Stance);
            Assert.Equal(1, radios.Count(n => bar[n]!.Checked));
            Assert.True(bar[name]!.Checked);
        }
    }

    /// <summary>
    /// A stance is per member (the party's stance array), and the button sets the whole party's
    /// unless shift or control is held, which is the test the button makes before choosing between
    /// the stance's two forms.
    /// </summary>
    [Fact]
    public void A_held_modifier_gives_the_stance_to_the_steered_character_alone()
    {
        using var files = Install.Open();
        var (place, bar, minds) = Open(files);

        var companion = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        companion.Thing.Actor!.Flags |= Actor.Flag.Party;
        bar.Update();
        Assert.Equal(2, bar.Party.Count);

        var attack = bar["sbAttackRadio"]!;
        bar.Click(attack.X + 1, attack.Y + 1, InputModifier.None);
        Assert.Equal(PartyStance.Attack, place.Player!.Thing.Actor!.Stance);
        Assert.Equal(PartyStance.Attack, companion.Thing.Actor.Stance);

        var defence = bar["sbDefenseRadio"]!;
        bar.Click(defence.X + 1, defence.Y + 1, InputModifier.Ctrl);
        Assert.Equal(PartyStance.Defense, place.Player.Thing.Actor.Stance);
        Assert.Equal(PartyStance.Attack, companion.Thing.Actor.Stance);
    }

    /// <summary>
    /// Two members put two rows side by side, and the selected one gets the lit backdrop — but only
    /// because there is somebody to tell it apart from. A party of one gets the plain one
    /// (both are asked for).
    /// </summary>
    [Fact]
    public void The_selected_row_lights_up_only_when_there_is_more_than_one()
    {
        using var files = Install.Open();
        var (_, bar, minds) = Open(files);
        Assert.Equal(0, bar["statusIndicators0"]!.Image);

        var companion = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        companion.Thing.Actor!.Flags |= Actor.Flag.Party;
        bar.Update();

        var first = bar["statusIndicators0"]!;
        var second = bar["statusIndicators1"]!;
        Assert.True(second.Shown);
        Assert.Equal(1, first.Image);
        Assert.Equal(0, second.Image);
        Assert.Equal(StatusBar.RowStep, second.X - first.X);
        // Still centred: two rows straddle the middle.
        Assert.Equal(UiAssets.Width / 2, (first.X + second.X + second.Width) / 2);
    }

    /// <summary>
    /// A click on a row's pip — or on any of its five bars, because the pane hands all six the same
    /// handler — takes the character in and out of the formation.
    /// </summary>
    [Fact]
    public void Clicking_a_row_takes_the_character_in_and_out_of_formation()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        Assert.True((me.Flags2 & Actor.Flag2.InFormation) != 0);

        var pip = bar["statusParty0"]!;
        Assert.True(pip.Checked);
        bar.Click(pip.X + 1, pip.Y + 1, InputModifier.None);
        Assert.True((me.Flags2 & Actor.Flag2.InFormation) == 0);
        Assert.False(bar["statusParty0"]!.Checked);

        var health = bar["statusHealth0"]!;
        bar.Click(health.X + 1, health.Y + 1, InputModifier.None);
        Assert.True((me.Flags2 & Actor.Flag2.InFormation) != 0);
    }

    /// <summary>
    /// Regroup puts everybody back in formation, whatever they were doing (the regroup sets the bit on
    /// every member, not just the ones that had lost it).
    /// </summary>
    [Fact]
    public void Regroup_puts_the_whole_party_back_in_formation()
    {
        using var files = Install.Open();
        var (place, bar, minds) = Open(files);
        var companion = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        companion.Thing.Actor!.Flags |= Actor.Flag.Party;
        bar.Update();

        place.Player!.Thing.Actor!.Flags2 &= ~Actor.Flag2.InFormation;
        companion.Thing.Actor.Flags2 &= ~Actor.Flag2.InFormation;
        bar.Update();
        Assert.All(bar.Party, a => Assert.True((a.Flags2 & Actor.Flag2.InFormation) == 0));

        var button = bar["sbRegroupButton"]!;
        var took = bar.Click(button.X + 1, button.Y + 1, InputModifier.None);
        Assert.True(took.Consumed);
        Assert.All(bar.Party, a => Assert.True((a.Flags2 & Actor.Flag2.InFormation) != 0));
        Assert.True(bar["statusParty0"]!.Checked);
        Assert.True(bar["statusParty1"]!.Checked);
    }

    /// <summary>
    /// The regroup button is two commands, and the second one is not in the manual: with shift held,
    /// or on the right button, it disperses instead (the button picks between regroup and disperse).
    /// </summary>
    [Fact]
    public void Shift_or_the_right_button_turns_regroup_into_disperse()
    {
        using var files = Install.Open();
        var (_, bar, minds) = Open(files);
        var companion = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        companion.Thing.Actor!.Flags |= Actor.Flag.Party;
        companion.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
        bar.Update();

        var button = bar["sbRegroupButton"]!;
        bar.Click(button.X + 1, button.Y + 1, InputModifier.Shift);
        Assert.All(bar.Party, a => Assert.False(a.Has2(Actor.Flag2.InFormation)));

        bar.Click(button.X + 1, button.Y + 1, InputModifier.None);
        Assert.All(bar.Party, a => Assert.True(a.Has2(Actor.Flag2.InFormation)));

        bar.Click(button.X + 1, button.Y + 1, InputModifier.None, right: true);
        Assert.All(bar.Party, a => Assert.False(a.Has2(Actor.Flag2.InFormation)));
    }

    /// <summary>
    /// Ctrl + left click on the ground (the party's move): members keeping formation drop
    /// their fight; one out of formation keeps it; nobody's formation bit changes.
    /// </summary>
    [Fact]
    public void Regroup_to_location_stops_only_the_members_keeping_formation()
    {
        using var files = Install.Open();
        var (_, bar, minds) = Open(files);
        var pair = minds.All.Where(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null)
                        .Take(3).ToArray();
        var (keeping, straying, enemy) = (pair[0], pair[1], pair[2]);
        foreach (var m in new[] { keeping, straying }) { m.Thing.Actor!.Flags |= Actor.Flag.Party; m.Enter(AiMode.Party); }
        keeping.Thing.Actor!.Flags2 |= Actor.Flag2.InFormation;
        straying.Thing.Actor!.Flags2 &= ~Actor.Flag2.InFormation;
        bar.Update();
        keeping.Aim(enemy.Thing, minds.Now);
        straying.Aim(enemy.Thing, minds.Now);

        bar.RegroupHere();

        Assert.Null(keeping.Target);
        Assert.Same(enemy.Thing, straying.Target);
        Assert.True(keeping.Thing.Actor.Has2(Actor.Flag2.InFormation));
        Assert.False(straying.Thing.Actor.Has2(Actor.Flag2.InFormation));
    }

    /// <summary>
    /// The regroup's cancel is the **unforced** one (the cancel with (0, 0), read in the
    /// A comparison with 1.1): a member in the middle of something that keeps it busy — a swing running out — keeps
    /// its order, and one that is free has it cancelled. Vitomir cleared every member's action outright.
    /// </summary>
    [Fact]
    public void Regroup_leaves_a_busy_member_to_finish_what_it_is_doing()
    {
        using var files = Install.Open();
        var (_, bar, minds) = Open(files);
        var two = minds.All.Where(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null)
                       .Take(3).ToArray();
        var (busy, free, enemy) = (two[0], two[1], two[2]);
        foreach (var m in new[] { busy, free }) { m.Thing.Actor!.Flags |= Actor.Flag.Party; m.Enter(AiMode.Party); }
        bar.Update();
        foreach (var m in new[] { busy, free })
        {
            m.Thing.Actor!.Action = Rpg.ActionMode.Swing;
            m.Thing.Actor.Target = enemy.Thing;
        }
        busy.Thing.Actor!.BusyUntil = minds.Now + 5_000;

        bar.Regroup();

        Assert.Equal(Rpg.ActionMode.Swing, busy.Thing.Actor.Action);
        Assert.Same(enemy.Thing, busy.Thing.Actor.Target);
        Assert.Equal(Rpg.ActionMode.None, free.Thing.Actor!.Action);
    }

    /// <summary>
    /// Ctrl on a row's portrait is the group toggle — the manual's "Group Toggle (clicking on the
    /// UI head)". A plain click there selects that character (`SteeringTests`), which on the one
    /// already steered is nothing at all.
    /// </summary>
    [Fact]
    public void Ctrl_on_a_portrait_is_the_group_toggle()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        var hotspot = bar["statusHotspot0"]!;
        // High enough up the hotspot to be clear of the row's bars, which sit at its foot.
        int y = hotspot.Y + hotspot.Height - 1;

        Assert.True(me.Has2(Actor.Flag2.InFormation));
        bar.Click(hotspot.X + 1, y, InputModifier.Ctrl);
        Assert.False(me.Has2(Actor.Flag2.InFormation));

        var plain = bar.Click(hotspot.X + 1, y, InputModifier.None);
        Assert.True(plain.Consumed);
        Assert.Contains("already steering", plain.Did);
        Assert.False(me.Has2(Actor.Flag2.InFormation));
    }

    /// <summary>
    /// Every grid in the game is built from the same **27 x 32** cell, and the quick-use strip is
    /// five of them in a row. The five shipped panes that declare a grid all agree, which is what
    /// makes it a fact rather than a measurement.
    /// </summary>
    [Fact]
    public void Every_shipped_grid_is_built_from_the_same_cell()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        int grids = 0;
        foreach (var name in new[] { StatusBar.PaneName, "inventoryPane" })
            foreach (var element in assets.Pane(name).Elements)
            {
                if (element.Type is not ("icongrid" or "weargrid")) continue;
                if (element.Get("gridsize") is not { } g || NuiPane.Ints(g) is not [var across, var down]) continue;
                Assert.Equal(StatusBar.CellWidth, element.Size[0] / across);
                Assert.Equal(StatusBar.CellHeight, element.Size[1] / down);
                grids++;
            }
        Assert.True(grids >= 10, $"only {grids} grids found");
    }

    /// <summary>
    /// The quick-use strip fills from the character's own quick slots, in inventory order — and the
    /// inventory pushes on the front, so the item put there last is leftmost.
    /// </summary>
    [Fact]
    public void The_quick_use_strip_fills_from_the_quick_slots()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        // **Each item in the cell its own record names, and every cell present whether or not
        // anything is in it.** The strip used to pack items left to right in inventory order and
        // build a cell only where one landed, which drew an item from slot 3 in slot 0 and left an
        // empty belt with no cells at all — so there was nothing to drop onto.
        var first = new Item { Name = "a", Icon = "vitaeIcon", Location = Slot.QuickUse, Cell = (0, 0) };
        var third = new Item { Name = "b", Icon = "ratIcon", Location = Slot.QuickUse, Cell = (3, 0) };
        var packed = new Item { Name = "c", Icon = "vitaeIcon", Location = Slot.Inventory };
        me.Inventory.Add(first);
        me.Inventory.Add(third);
        me.Inventory.Add(packed);
        bar.Update();

        Assert.Equal(5, bar.QuickUse.Count);
        Assert.Same(first, bar.QuickUse[0].Item);
        Assert.Same(third, bar.QuickUse[3].Item);
        foreach (int empty in (ReadOnlySpan<int>)[1, 2, 4]) Assert.Null(bar.QuickUse[empty].Item);

        var grid = bar["sbQuickUse"]!;
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(grid.X + i * StatusBar.CellWidth, bar.QuickUse[i].X);
            Assert.Equal(StatusBar.CellWidth, bar.QuickUse[i].Width);
            Assert.Equal(StatusBar.CellHeight, bar.QuickUse[i].Height);
        }

        // And the hit test finds an empty cell, which is what makes a drop possible.
        Assert.Equal(1, bar.QuickCellAt(grid.X + StatusBar.CellWidth + 2, grid.Y + 2));
        Assert.Null(bar.QuickAt(grid.X + StatusBar.CellWidth + 2, grid.Y + 2));
    }

    /// <summary>
    /// **A click on the strip finds the item under the point and hands it to the use path.** Until
    /// it once the bar answered *"sbquickuse is not wired up yet"* — which showed in play with
    /// Christof's `CuthbertLetter` sitting right there in the first cell.
    /// </summary>
    [Fact]
    public void A_click_on_a_quick_slot_reaches_the_item_in_it()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var me = place.Player!.Thing.Actor!;
        var letter = new Item { Name = "CuthbertLetter", Icon = "vitaeIcon", Location = Slot.QuickUse };
        me.Inventory.Add(letter);
        bar.Update();

        Assert.Equal(5, bar.QuickUse.Count);
        var cell = bar.QuickUse[0];
        Assert.Same(letter, bar.QuickAt(cell.X + 2, cell.Y + 2));
        Assert.Null(bar.QuickAt(cell.X + StatusBar.CellWidth + 2, cell.Y + 2));   // the next cell is empty

        // **The two bindings are not the same thing.** `sbQuickUse` carries grid id 0x200 and
        // The bar registers the grid's drop handler on binding 0, so a **left** click is the grid
        // handler — pick up, or put down — and **right** is the icon's use. An earlier build had the left
        // click using the item, which invented a gesture the original has not and lost the one it
        // has.
        string? used = null, took = null;
        bar.UseQuick = item => { used = item.Name; return $"used {item.Name}"; };
        bar.TakeQuick = at => { took = $"cell {at}"; return $"holding what was in cell {at}"; };

        var left = bar.Click(cell.X + 2, cell.Y + 2);
        Assert.True(left.Consumed);
        Assert.Equal("holding what was in cell 0", left.Did);
        Assert.Equal("cell 0", took);
        Assert.Null(used);

        var rightClick = bar.Click(cell.X + 2, cell.Y + 2, right: true);
        Assert.Equal("used CuthbertLetter", rightClick.Did);
        Assert.Equal("CuthbertLetter", used);

        // Without a handler each says so rather than pretending.
        bar.UseQuick = null;
        bar.TakeQuick = null;
        Assert.Contains("not wired up", bar.Click(cell.X + 2, cell.Y + 2).Did);
        Assert.Contains("not wired up", bar.Click(cell.X + 2, cell.Y + 2, right: true).Did);
    }

    /// <summary>
    /// The message box's scroll bar answers a press — in play a log said
    /// *"sbscrollbar is not wired up yet"* twice. With the shipped pane and font, ten lines leave the
    /// box lifted to the newest, and a press near the top of the bar pages one box-height back.
    /// </summary>
    [Fact]
    public void The_message_boxs_scroll_bar_pages_the_log()
    {
        using var files = Install.Open();
        var (_, bar, _) = Open(files);
        var box = bar["sbTextbox"]!;
        var log = new MessageLog();
        bar.Messages = new MessageBox(log, new UiAssets(files).Font(box.Element.Get("font")!), box,
                                      bar["sbScrollBar"], bar["sbScrollSlider"]);
        for (int i = 0; i < 10; i++) log.Add($"line {i}");
        bar.Update();
        var messages = bar.Messages;
        Assert.Equal(10, messages.Font.LineHeight);
        Assert.Equal(messages.MaxScroll, messages.Scroll);

        var track = bar["sbScrollBar"]!;
        var click = bar.Click(track.X + 2, track.Y + track.Height - 2);
        Assert.True(click.Consumed);
        Assert.DoesNotContain("not wired up", click.Did);
        Assert.Equal(messages.MaxScroll - box.Height, messages.Scroll);
        Assert.Equal("the message box (it has no click)", bar.Click(box.X + 10, box.Y + 10).Did);
    }

    /// <summary>
    /// `ENUM_ICONSIZE` is a table, not arithmetic — 4 is 2x2 and 7 is 2x1 — and a cell count is how
    /// much of a grid an item covers.
    /// </summary>
    [Fact]
    public void An_items_icon_covers_as_many_cells_as_its_icon_size_says()
    {
        Assert.Equal((1, 1), new Item { IconSize = 0 }.IconCells);
        Assert.Equal((1, 2), new Item { IconSize = 1 }.IconCells);
        Assert.Equal((1, 3), new Item { IconSize = 2 }.IconCells);
        Assert.Equal((2, 2), new Item { IconSize = 4 }.IconCells);
        Assert.Equal((2, 3), new Item { IconSize = 5 }.IconCells);
        Assert.Equal((2, 1), new Item { IconSize = 7 }.IconCells);
    }

    /// <summary>
    /// A window pixel becomes a pane pixel: x scales, y flips against 479 — the engine's own
    /// one-off (the hit test subtracts from 479, not from 480).
    /// </summary>
    [Fact]
    public void A_window_pixel_lands_in_the_panes_own_space()
    {
        // 1.1's own conversion is its stretch's (the layout named: the default, the 16:9 stretch, is a shorter sheet
        // on these 4:3 pictures — UiLayoutTests).
        const UiLayoutMode stretch = UiLayoutMode.Stretch;
        Assert.Equal((0, 479), StatusBar.FromWindow(0, 0, 640, 480, stretch));
        Assert.Equal((639, 0), StatusBar.FromWindow(639, 479, 640, 480, stretch));
        // And the same corners in a window twice the size.
        Assert.Equal((0, 479), StatusBar.FromWindow(0, 0, 1280, 960, stretch));
        Assert.Equal((320, 239), StatusBar.FromWindow(640, 480, 1280, 960, stretch));
    }

    /// <summary>
    /// The six discipline slots are not cells of a grid: the pane gives each its own `pos`, and a
    /// discipline knows which slot it is in rather than the slot knowing what is in it — which is
    /// how the bar fills them. So a discipline in slot 4 lands on `sbDiscipline4` wherever the pane
    /// put it, and one in no slot is simply not on the bar.
    /// </summary>
    [Fact]
    public void The_discipline_slots_are_filled_by_what_each_discipline_says_its_slot_is()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");
        var me = place.Player!.Thing.Actor!;

        var fourth = me.Disciplines.Add(powers["Celerity"]!, 0, 4);
        var first = me.Disciplines.Add(powers["Fireball"]!, 0, 0);
        var unslotted = me.Disciplines.Add(powers["BloodHealing"]!, 0);
        bar.Update();

        Assert.Equal(2, bar.Powers.Count);
        Assert.DoesNotContain(unslotted, bar.Powers.Select(p => p.Discipline));
        foreach (var (slot, discipline) in new[] { (4, fourth), (0, first) })
        {
            var drawn = bar.Powers.Single(p => p.Slot == slot);
            var control = bar[$"sbDiscipline{slot}"]!;
            Assert.Same(discipline, drawn.Discipline);
            Assert.Equal((control.X, control.Y, control.Width, control.Height),
                         (drawn.X, drawn.Y, drawn.Width, drawn.Height));
            // The icon comes off `game.ndd` rather than out of the pane.
            Assert.Equal(discipline.Definition.Icon, drawn.Discipline.Definition.Icon);
            // Nothing is selected yet, so both take the plain bitmap.
            Assert.Equal(discipline.Definition.Icon, drawn.Art);
        }

        // `sbCurrentDiscipline` is the selected one and is reported as slot −1, so it can be the same
        // discipline as one of the six without confusing the two.
        me.Disciplines.Current = fourth;
        bar.Update();
        Assert.Equal(3, bar.Powers.Count);
        var current = bar.Powers.Single(p => p.Slot == -1);
        Assert.Same(fourth, current.Discipline);
        Assert.Equal(bar["sbCurrentDiscipline"]!.X, current.X);

        // A slot number outside the six is ignored rather than throwing — the engine indexes its
        // slot controls by it, so out of range means "not on the bar".
        unslotted.QuickSlot = 99;
        bar.Update();
        Assert.Equal(3, bar.Powers.Count);
    }

    /// <summary>
    /// A discipline has **three** bitmaps and the slot picks one (the discipline slot
    /// `sbCurrentDiscipline` draws `icon_L`, authored at exactly its 40 x 48; the one of
    /// the six holding the **selected** discipline draws `icon_R`, the same art with a red ring
    /// round it at exactly a slot's 27 x 32; and everything else draws the plain one. So selecting a
    /// power lights up its slot, which is a thing the bar does that no amount of staring at the
    /// pane file would tell you.
    /// </summary>
    [Fact]
    public void The_selected_discipline_lights_up_its_own_slot_and_fills_the_big_one()
    {
        using var files = Install.Open();
        var (place, bar, _) = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");
        var me = place.Player!.Thing.Actor!;
        var celerity = me.Disciplines.Add(powers["Celerity"]!, 0, 0);
        var potence = me.Disciplines.Add(powers["Potence"]!, 0, 1);

        me.Disciplines.Current = celerity;
        bar.Update();
        Assert.Equal("celerityIcon" + StatusBar.SelectedSuffix, bar.Powers.Single(p => p.Slot == 0).Art);
        Assert.Equal(potence.Definition.Icon, bar.Powers.Single(p => p.Slot == 1).Art);
        Assert.Equal("celerityIcon" + StatusBar.LargeSuffix, bar.Powers.Single(p => p.Slot == -1).Art);

        // Select the other one and the ring moves with it.
        me.Disciplines.Current = potence;
        bar.Update();
        Assert.Equal(celerity.Definition.Icon, bar.Powers.Single(p => p.Slot == 0).Art);
        Assert.Equal(potence.Definition.Icon + StatusBar.SelectedSuffix, bar.Powers.Single(p => p.Slot == 1).Art);
        Assert.Equal(potence.Definition.Icon + StatusBar.LargeSuffix, bar.Powers.Single(p => p.Slot == -1).Art);

        // A discipline the player has not selected never gets the ring, even when its definition is.
        me.Disciplines.Current = null;
        bar.Update();
        Assert.DoesNotContain(bar.Powers, p => p.Art.EndsWith(StatusBar.SelectedSuffix));
        Assert.DoesNotContain(bar.Powers, p => p.Slot == -1);
    }

    /// <summary>
    /// **The pane buttons stay down while their pane is up.** `statusBarSP.nui` declares them as
    /// `checkbox` rather than `button` and gives each a `checkedbitmap` ending in `Dep` —
    /// depressed — and `UiControl.Art` has always preferred it when the control is checked. What
    /// was missing was anything setting the check. It showed in play.
    /// </summary>
    [Fact]
    public void A_pane_button_is_down_while_its_pane_is_open()
    {
        using var files = Install.Open();
        var (_, bar, _) = Open(files);

        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bar.PaneShown = open.Contains;

        bar.Update();
        Assert.All(StatusBar.PaneButtons, n => Assert.False(bar[n]?.Checked ?? true, n));

        open.Add("sbInventoryButton");
        bar.Update();
        Assert.True(bar["sbInventoryButton"]!.Checked);
        Assert.False(bar["sbCharacterButton"]!.Checked);
        // And the art follows, which is the whole point — the file names the depressed bitmap.
        // Lower-cased, because the pane parser folds every line before reading it.
        Assert.Equal("spsbinventorybuttondep.tga", bar["sbInventoryButton"]!.Art);

        open.Clear();
        bar.Update();
        Assert.False(bar["sbInventoryButton"]!.Checked);
        Assert.NotEqual("spsbinventorybuttondep.tga", bar["sbInventoryButton"]!.Art);
    }

    /// <summary>
    /// **An element a player module binds to one of Vitomir's own actions**: a click on a button 1.1's bar does not have runs its
    /// action — one the bar does itself (the stances, regroup and disperse) or the game's (<see cref="StatusBar.Perform"/>: the vault, the
    /// advancement screen) — and a binding of one of 1.1's own elements is left out, so the element keeps what it did.
    /// </summary>
    [Fact]
    public void A_button_a_module_binds_runs_vitomirs_action_and_1_1s_own_elements_keep_theirs()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        Assert.True(files.TryRead("ui/statusBarSP.nui", out var original));
        // A made-up layout: 1.1's bar and two buttons more, as a pack made for a patched program has them (names read in lower case).
        var text = System.Text.Encoding.Latin1.GetString(original)
                   + "\nelement button name=sbVault flags=0x02 pos=72,12 size=26,32\nelement button name=sbLvlUp flags=0x02 pos=72,47 size=26,32\n";
        var bar = new StatusBar(NuiPane.Parse(text, "statusBarSP.nui"), place);
        bar.Update();
        var done = new List<string>();
        bar.Bindings = Vitomir.Platform.PlayerModules.InterfaceActions.Effective(new Dictionary<string, string>
        {
            ["sbVault"] = "open-vault", ["sbLvlUp"] = "stance-defense", ["sbMapButton"] = "open-vault",
        });
        bar.Perform = a => { done.Add(a.Name); return "opened"; };
        Assert.False(bar.Bindings.ContainsKey("sbMapButton"));

        var vault = bar["sbVault"]!;
        Assert.Equal("sbvault: open-vault: opened", bar.Click(vault.X + 1, vault.Y + 1, InputModifier.None).Did);
        Assert.Equal(["open-vault"], done);
        var stance = bar["sbLvlUp"]!;
        Assert.Contains("sblvlup: stance-defense:", bar.Click(stance.X + 1, stance.Y + 1, InputModifier.None).Did, StringComparison.Ordinal);
        Assert.Equal(PartyStance.Defense, place.Player!.Thing.Actor!.Stance);
        // Unbound, the button says so as before; the map button is still the map button (here: no handler for it at all).
        bar.Bindings = new Dictionary<string, Vitomir.Platform.PlayerModules.InterfaceAction>();
        Assert.Equal("sbvault is not wired up yet", bar.Click(vault.X + 1, vault.Y + 1, InputModifier.None).Did);
        Assert.Equal("quick-use slot 3 is empty", bar.Do(Vitomir.Platform.PlayerModules.InterfaceActions.Find("quick-use-3")!));
    }

    /// <summary>
    /// **The multiplayer bar's Storyteller button and head, when a click reaches the bar** (a pane up over the world): the button is
    /// the host's Storyteller button, as its own hook is, and the head has no click (1.1 hooks it for the rollover alone) — neither
    /// says "not wired up yet" any more (seen in play, with the map open).
    /// </summary>
    [Fact]
    public void The_multiplayer_bar_s_storyteller_button_and_head_answer_a_click_that_reaches_the_bar()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        Assert.True(files.TryRead("ui/statusBarMP.nui", out var pane));
        var bar = new StatusBar(NuiPane.Parse(System.Text.Encoding.Latin1.GetString(pane), "statusBarMP.nui"), place);
        bar.Update();
        var st = bar["sbSTButton"]!;
        st.Shown = true;
        st.Greyed = true;
        int taken = 0;
        bar.Storyteller = () => { taken++; return "the Storyteller button"; };
        Assert.Equal("the Storyteller button", bar.Click(st.X + 1, st.Y + 1, InputModifier.None).Did);
        Assert.Equal(1, taken);
        var head = bar["sbHeadHotspot"]!;
        Assert.DoesNotContain("not wired up", bar.Click(head.X + 40, head.Y + 40, InputModifier.None).Did ?? "", StringComparison.Ordinal);
    }
}
