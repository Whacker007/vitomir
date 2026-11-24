// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// The Controls page: a cell clicked waits for the next key, button or wheel turn and binds it there, taking it
/// off the action that had it (lit and named); Escape while it waits clears it; Defaults, Cancel and OK.
/// </summary>
public sealed class ControlsPageTests
{
    static string[] Keys(ControlsPage p, string action) => p.Values.Bindings[action];

    [Fact]
    public void A_cell_clicked_waits_and_the_next_key_is_bound_there_and_taken_off_the_action_that_had_it()
    {
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        Fixtures.Click(s, page.Cell("sprint", 0)!);
        foreach (var a in s.TakeActions()) page.Handle(a);
        Assert.Equal(("sprint", 0), page.Waiting);
        Assert.Equal("waiting", page.Cell("sprint", 0)!.StateNow);
        Assert.Contains("for Sprint", s.Data.Text("hint"));

        page.Press(InputSource.Key(224));        // Left Ctrl, sprint in one scheme, which walk had
        Assert.Null(page.Waiting);
        // The key works Walk, so the page asks; Move takes it (as earlier).
        Assert.Equal(["walk"], page.Asking!.Value.Others);
        Assert.True(s.Data.Flag("asking"));
        page.Answer(false);
        Assert.False(s.Data.Flag("asking"));
        Assert.Equal(["Left Ctrl"], Keys(page, "sprint"));
        Assert.Empty(Keys(page, "walk"));
        Assert.Equal("walk", page.Lost);
        Assert.Equal("Left Ctrl was taken from Walk and now works Sprint.", s.Data.Text("message"));
        var walkRow = s.Data.Items("actions").Single(r => r.Text("action") == "walk");
        Assert.Equal("lost", walkRow.Text("rowFill"));
        Assert.Equal("", walkRow.Text("key0"));
    }

    [Fact]
    public void The_second_cell_keeps_the_first_and_a_mouse_button_and_a_wheel_turn_bind_by_their_names()
    {
        var page = Fixtures.Page();
        page.Wait("jump", 1);
        page.Press(InputSource.Mouse(BindingNames.SdlRight));
        page.Answer(false);                      // moved (taken from block)
        Assert.Equal(["Space", "Mouse Right"], Keys(page, "jump"));
        Assert.Equal("block", page.Lost);
        Assert.Empty(Keys(page, "block"));

        page.Wait("use", 1);
        page.Press(InputSource.Wheel(-1));
        Assert.Equal(["E", "Wheel Down"], Keys(page, "use"));
        Assert.Null(page.Lost);
        Assert.Equal("Wheel Down works Use.", page.Message);
    }

    [Fact]
    public void Escape_while_a_cell_waits_clears_that_cell_and_the_second_key_moves_up()
    {
        var page = Fixtures.Page();
        page.Wait("jump", 1);
        page.Press(InputSource.Key(27));          // X
        page.Wait("jump", 0);
        page.ClearWaiting();
        Assert.Null(page.Waiting);
        Assert.Equal(["X"], Keys(page, "jump"));
        Assert.Equal("Space no longer works Jump.", page.Message);
        var row = page.Screen.Data.Items("actions").Single(r => r.Text("action") == "jump");
        Assert.Equal("X", row.Text("key0"));
        Assert.Equal("", row.Text("key1"));
    }

    [Fact]
    public void A_key_pressed_into_its_own_actions_other_cell_moves_there()
    {
        var page = Fixtures.Page();
        page.Wait("jump", 1);
        page.Press(InputSource.Key(27));          // X: Space, X
        page.Wait("jump", 1);
        page.Press(InputSource.Key(44));          // Space into the second cell: X, Space
        Assert.Equal(["X", "Space"], Keys(page, "jump"));
        Assert.Null(page.Lost);
    }

    [Fact]
    public void The_swing_arc_slider_shows_its_opacity_off_at_nothing_and_OK_takes_it()
    {
        // ("An option in the menu to make it lighter or darker").
        var page = Fixtures.LaidPage(2560, 1440, current: Fixtures.Defaults() with { SwingArcOpacity = 25f });
        var s = page.Screen;
        Assert.Equal("25 %", s.Data.Text("swingArcText"));
        var arc = (Slider)s.Find("swingArcOpacity")!;
        Assert.Equal((0f, 100f), (arc.Min, arc.Max));
        s.SetFocus(arc);
        s.Key(UiKey.Right);
        foreach (var a in s.TakeActions()) page.Handle(a);
        Assert.Equal("30 %", s.Data.Text("swingArcText"));
        for (int i = 0; i < 30; i++) s.Key(UiKey.Left);
        foreach (var a in s.TakeActions()) page.Handle(a);
        Assert.Equal("Off", s.Data.Text("swingArcText"));
        s.Key(UiKey.Right);
        s.Key(UiKey.Right);
        foreach (var a in s.TakeActions()) page.Handle(a);
        page.Handle(new UiAction("ok", "", s.Data, null));
        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        Assert.Equal(10f, page.Values.SwingArcOpacity);
        // Its toggle is listed with the other overlays, unbound.
        Assert.Contains(ControlsPage.Order, o => o.Action == "swingArc");
    }

    [Fact]
    public void The_new_rows_show_their_values_and_OK_takes_them()
    {
        // Melee framing (off), the crosshair in melee (20 %), the combat log (on, top right;
        // off or a corner), the attack direction (automatic, mouse, keys), the camera's action sway and follow.
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        Assert.Equal("Off", s.Data.Text("meleeFraming"));
        Assert.Equal("20 %", s.Data.Text("meleeReticleText"));
        Assert.Equal("Top Right", s.Data.Text("combatLog"));
        Assert.Equal("Keys", s.Data.Text("attackDirection"));   // the movement keys by default
        Assert.Equal("4 cm", s.Data.Text("actionSwayText"));
        Assert.Equal("0.08 s", s.Data.Text("followText"));
        void Nudge(string id, int times, UiKey key = UiKey.Right)
        {
            s.SetFocus(s.Find(id)!);
            for (int i = 0; i < times; i++) s.Key(key);
            foreach (var a in s.TakeActions()) page.Handle(a);
        }
        Nudge("meleeFraming", 1);
        Nudge("meleeReticle", 2);              // 20 → 30 %
        Nudge("combatLog", 3);                 // Top Right → Top Left → Bottom Right → Bottom Left
        Nudge("attackDirection", 1, UiKey.Left);   // Keys → Mouse
        Nudge("actionSway", 6);                // 4 → 10 cm
        Nudge("follow", 8, UiKey.Left);        // 0.08 → 0 s
        Assert.Equal("On", s.Data.Text("meleeFraming"));
        Assert.Equal("30 %", s.Data.Text("meleeReticleText"));
        Assert.Equal("Bottom Left", s.Data.Text("combatLog"));
        Assert.Equal("Mouse", s.Data.Text("attackDirection"));
        Assert.Equal("10 cm", s.Data.Text("actionSwayText"));
        Assert.Equal("Rigid", s.Data.Text("followText"));
        page.Handle(new UiAction("ok", "", s.Data, null));
        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        var v = page.Values;
        Assert.True(v.MeleeFraming);
        Assert.Equal(30f, v.MeleeReticleOpacity);
        Assert.True(v.ShowCombatLog);
        Assert.Equal(Presentation.LogCorner.BottomLeft, v.CombatLogCorner);
        Assert.Equal("mouse", v.AttackDirection);
        Assert.Equal(10f, v.ActionSway);
        Assert.Equal(0f, v.ThirdPersonFollow);
        // The log off keeps its corner for when it is on again.
        var off = Fixtures.LaidPage(2560, 1440, current: Fixtures.Defaults() with { ShowCombatLog = false, CombatLogCorner = Presentation.LogCorner.TopLeft });
        Assert.Equal("Off", off.Screen.Data.Text("combatLog"));
        off.Handle(new UiAction("ok", "", off.Screen.Data, null));
        Assert.False(off.Values.ShowCombatLog);
        Assert.Equal(Presentation.LogCorner.TopLeft, off.Values.CombatLogCorner);
        // Its toggle action is listed with the other overlays, unbound; an attack direction the page does not know shows as the default, Keys.
        Assert.Contains(ControlsPage.Order, o => o.Action == "combatLog");
        Assert.Equal("Keys", Fixtures.LaidPage(2560, 1440, current: Fixtures.Defaults() with { AttackDirection = "sideways" }).Screen.Data.Text("attackDirection"));
    }

    [Fact]
    public void The_head_motion_and_field_of_view_sliders_show_their_values_and_OK_takes_them()
    {
        var page = Fixtures.LaidPage(2560, 1440, current: Fixtures.Defaults() with { HeadMotion = 50f, FieldOfView = 80f });
        var s = page.Screen;
        Assert.Equal("50 %", s.Data.Text("headMotionText"));
        Assert.Equal("80°", s.Data.Text("fieldOfViewText"));
        var motion = (Slider)s.Find("headMotion")!;
        var field = (Slider)s.Find("fieldOfView")!;
        Assert.Equal((0f, 100f), (motion.Min, motion.Max));
        Assert.Equal((60f, 110f), (field.Min, field.Max));
        s.SetFocus(motion);
        s.Key(UiKey.Left);
        s.SetFocus(field);
        s.Key(UiKey.Right);
        foreach (var a in s.TakeActions()) page.Handle(a);
        Assert.Equal("45 %", s.Data.Text("headMotionText"));
        Assert.Equal("81°", s.Data.Text("fieldOfViewText"));
        // All the way down: no head motion at all is a value of its own, not "unset".
        s.SetFocus(motion);
        for (int i = 0; i < 30; i++) s.Key(UiKey.Left);
        foreach (var a in s.TakeActions()) page.Handle(a);
        page.Handle(new UiAction("ok", "", s.Data, null));
        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        Assert.Equal(0f, page.Values.HeadMotion);
        Assert.Equal(81f, page.Values.FieldOfView);
    }

    [Fact]
    public void OK_takes_the_cells_the_slider_and_the_toggles_and_Cancel_leaves_the_players_own()
    {
        var mine = Fixtures.Defaults();
        var page = Fixtures.LaidPage(2560, 1440, current: mine);
        var s = page.Screen;
        page.Wait("crouch", 0);
        page.Press(InputSource.Key(27));          // X, crouch in one scheme
        s.SetFocus(s.Find("invert"));
        s.Key(UiKey.Enter);
        s.SetFocus(s.Find("facing"));
        s.Key(UiKey.Right);
        s.SetFocus(s.Find("sensitivity"));
        s.Key(UiKey.Right);
        foreach (var a in s.TakeActions()) page.Handle(a);
        page.Handle(new UiAction("ok", "", s.Data, null));

        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        Assert.Equal(["X"], page.Values.Bindings["crouch"]);
        Assert.True(page.Values.InvertY);
        Assert.Equal(ThirdPersonFacing.View, page.Values.Facing);
        Assert.Equal(0.0023f, page.Values.MouseSensitivity, 0.000001f);
        // The player's own values were not touched: the page works on a copy until the host takes it.
        Assert.Equal(["C"], mine.Bindings["crouch"]);
        Assert.False(mine.InvertY);

        var other = Fixtures.Page(mine);
        other.Wait("crouch", 0);
        other.Press(InputSource.Key(27));
        other.Handle(new UiAction("cancel", "", other.Screen.Data, null));
        Assert.Equal(PageOutcome.Cancelled, other.Outcome);
        Assert.Equal(["C"], mine.Bindings["crouch"]);
    }

    [Fact]
    public void Defaults_put_every_binding_back()
    {
        var mine = Fixtures.Defaults();
        mine.Bindings["jump"] = ["Mouse Right"];
        mine.Bindings["block"] = [];
        var page = Fixtures.Page(mine);
        page.Handle(new UiAction("defaults", "", page.Screen.Data, null));
        Assert.Equal(["Space"], page.Values.Bindings["jump"]);
        Assert.Equal(["Mouse Right"], page.Values.Bindings["block"]);
        Assert.Contains("default", page.Message);
    }

    [Fact]
    public void Cancel_shares_a_key_with_crouch_and_neither_takes_it_from_the_other()
    {
        // X both crouch and cancel.
        var page = Fixtures.LaidPage(2560, 1440, current: Fixtures.Defaults());
        page.Wait("crouch", 0);
        page.Press(InputSource.Key(27));          // X
        page.Wait("cancel", 0);
        page.Press(InputSource.Key(27));          // X again, for cancel
        Assert.Equal("X works Cancel as well as Crouch.", page.Message);
        Assert.Null(page.Lost);
        Assert.Equal(["X"], page.Values.Bindings["crouch"]);
        Assert.Equal(["X"], page.Values.Bindings["cancel"]);
        // Jump given X takes it from crouch, not from cancel.
        page.Wait("jump", 0);
        page.Press(InputSource.Key(27));
        Assert.Equal(["crouch"], page.Asking!.Value.Others);     // asked about crouch only, not cancel
        page.Answer(false);
        Assert.Equal("Crouch", page.Lost is { } lost ? ControlsPage.Order.First(o => o.Action == lost).Label : null);
        Assert.Equal(["X"], page.Values.Bindings["cancel"]);
    }

    [Fact]
    public void Aim_is_on_Q_and_Dodge_on_Mouse_4_and_there_is_no_Swap_Shoulder_row()
    {
        // Q is aim, Mouse 4 or 5 is dodge, and swap shoulder is
        // gone: Switch View (V) cycles first person, the right shoulder and the left.
        var page = Fixtures.LaidPage(2560, 1440);
        var rows = page.Screen.Data.Items("actions");
        string Key0(string action) => rows.Single(r => r.Text("action") == action).Text("key0");
        Assert.Equal("Q", Key0("aim"));
        Assert.Equal("Aim", rows.Single(r => r.Text("action") == "aim").Text("label"));
        Assert.Equal("Mouse 4", Key0("dodge"));
        Assert.Equal("V", Key0("switchView"));
        Assert.DoesNotContain(rows, r => r.Text("action") == "swapShoulder");
        Assert.DoesNotContain(rows, r => r.Text("label").Contains("Shoulder", StringComparison.Ordinal));
        // Q and Mouse 4 each work one action: giving Q to Dodge asks (Share, Move or Cancel), as any taken key does.
        page.Wait("dodge", 1);
        page.Press(InputSource.Key(20));            // Q
        Assert.Equal(["aim"], page.Asking!.Value.Others);
    }

    [Fact]
    public void Every_action_is_shown_by_its_name_and_a_new_one_by_its_identifier_in_words()
    {
        var mine = Fixtures.Defaults();
        mine.Bindings["castSpell"] = ["Mouse 4"];
        var page = Fixtures.Page(mine);
        var labels = page.Screen.Data.Items("actions").Select(r => r.Text("label")).ToList();
        Assert.Equal(ControlsPage.Order.Select(o => o.Label).Append("Cast Spell"), labels);
        Assert.Equal("Move Forward", labels[0]);
        Assert.Equal("Mouse 4", page.Screen.Data.Items("actions")[^1].Text("key0"));
    }

    [Fact]
    public void The_quick_slots_are_a_group_of_eight_rows_under_their_heading_on_the_number_keys()
    {
        // "Quick Slot 1" … "Quick Slot 8", in the page's form (two cells), after the other actions.
        var page = Fixtures.LaidPage(2560, 1440);
        var d = page.Screen.Data;
        Assert.Equal(QuickSlotButtons.Actions, page.QuickSlotActions());
        var rows = d.Items("slotActions");
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"Quick Slot {n}"), rows.Select(r => r.Text("label")));
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"{n}"), rows.Select(r => r.Text("key0")));
        Assert.All(rows, r => Assert.Equal("", r.Text("key1")));
        Assert.DoesNotContain(d.Items("actions"), r => r.Text("action").StartsWith("slot", StringComparison.Ordinal));
        Assert.True(page.Screen.Find("quickSlotsHeading")!.Parent!.Shown);
        Assert.Equal("Quick Slots", ((TextPiece)page.Screen.Find("quickSlotsHeading")!).Words);
        Assert.Equal("HUD", ControlsPage.Label("hud"));
        Assert.Contains("hud", page.MainActions());
        // A file with no slots (none to show) has no heading.
        var none = Fixtures.Defaults();
        foreach (var a in QuickSlotButtons.Actions) none.Bindings.Remove(a);
        var bare = Fixtures.LaidPage(2560, 1440, current: none);
        Assert.False(bare.Screen.Find("quickSlotsHeading")!.Parent!.Shown);
    }

    [Fact]
    public void A_number_key_given_to_another_action_is_taken_from_its_slot_and_a_slot_takes_any_key()
    {
        var page = Fixtures.LaidPage(2560, 1440);
        page.Wait("jump", 1);
        page.Press(InputSource.Key(30));          // 1, slot 1's
        page.Answer(false);                       // moved
        Assert.Equal(["Space", "1"], Keys(page, "jump"));
        Assert.Empty(Keys(page, "slot1"));
        Assert.Equal("slot1", page.Lost);
        Assert.Equal("1 was taken from Quick Slot 1 and now works Jump.", page.Message);
        var slotRow = page.Screen.Data.Items("slotActions")[0];
        Assert.Equal("lost", slotRow.Text("rowFill"));

        // A slot's cell, chosen (Enter; it is below the fold, the list scrolls to it), takes G (F swaps the weapons); and a slot given another
        // slot's key takes it from that slot.
        page.Screen.SetFocus(page.Cell("slot3", 0));
        page.Screen.Key(UiKey.Enter);
        foreach (var a in page.Screen.TakeActions()) page.Handle(a);
        Assert.Equal(("slot3", 0), page.Waiting);
        page.Press(InputSource.Key(10));          // G
        Assert.Equal(["G"], Keys(page, "slot3"));
        page.Wait("slot8", 1);
        page.Press(InputSource.Key(36));          // 7
        page.Answer(false);
        Assert.Equal(["8", "7"], Keys(page, "slot8"));
        Assert.Empty(Keys(page, "slot7"));
        page.Handle(new UiAction("ok", "", page.Screen.Data, null));
        Assert.Equal(["G"], page.Values.Bindings["slot3"]);
    }

    [Fact]
    public void Always_run_is_on_by_default_beside_inverting_the_view_and_the_page_turns_it_off()
    {
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        Assert.True(page.Values.AlwaysRun);
        Assert.Equal("On", s.Data.Text("alwaysRun"));
        // The free-pointer key's row is gone (a held and a toggled action now). the view's column is grouped, and
        // Always Run is the first row under the Movement heading.
        Assert.Null(s.Find("pointerKey"));
        var heading = s.Find("movementHeading")!.Rect;
        var always = s.Find("alwaysRun")!.Rect;
        Assert.True(always.Y > heading.Y && always.Y - heading.Y < 3 * heading.Height, $"Always Run at {always}, the Movement heading at {heading}");
        s.SetFocus(s.Find("alwaysRun"));
        s.Key(UiKey.Enter);
        foreach (var a in s.TakeActions()) page.Handle(a);
        page.Handle(new UiAction("ok", "", s.Data, null));
        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        Assert.False(page.Values.AlwaysRun);
    }

    [Fact]
    public void Switch_mode_is_not_listed_and_the_pointers_click_is_listed_sharing_attacks_button()
    {
        // DI: click mode is not the enhanced game's; a file's Tab on switchMode is kept but not shown.
        var mine = Fixtures.Defaults();
        mine.Bindings["switchMode"] = ["Tab"];
        var page = Fixtures.Page(mine);
        Assert.DoesNotContain("switchMode", page.Actions());
        Assert.Contains("pointerClick", page.Actions());
        Assert.DoesNotContain(ControlsPage.Order, o => o.Action == "switchMode");
        Assert.Equal(["Tab"], page.Values.Bindings["switchMode"]);

        // The click and attack share Mouse Left: binding one there does not take it from the other.
        page.Wait("pointerClick", 0);
        page.Press(InputSource.Mouse(BindingNames.SdlLeft));
        Assert.Null(page.Lost);
        Assert.Equal(["Mouse Left"], Keys(page, "attack"));
        Assert.Equal(["Mouse Left"], Keys(page, "pointerClick"));
        Assert.Equal("Mouse Left works Click the Pointer as well as Attack.", page.Message);
    }

    [Fact]
    public void Each_two_state_action_has_a_toggled_row_after_its_held_one_empty_while_unbound()
    {
        // Crouch, walk and the freed pointer each held and toggled; the toggled ones unbound by default.
        var page = Fixtures.LaidPage(2560, 1440);
        var rows = page.Screen.Data.Items("actions");
        var actions = rows.Select(r => r.Text("action")).ToList();
        foreach (var (held, toggled) in new[] { ("crouch", "crouchToggle"), ("walk", "walkToggle"), ("freePointer", "freePointerToggle") })
        {
            Assert.Equal(actions.IndexOf(held) + 1, actions.IndexOf(toggled));
            var row = rows[actions.IndexOf(toggled)];
            Assert.EndsWith(" (Toggle)", row.Text("label"));
            Assert.StartsWith(ControlsPage.Label(held), row.Text("label"));
            Assert.Equal("", row.Text("key0"));
            Assert.Equal("", row.Text("key1"));
            Assert.NotNull(page.Cell(toggled, 0));
        }
        // A key given to a toggle is taken from its held action like any other (only cancel and the click share).
        page.Wait("crouchToggle", 0);
        page.Press(InputSource.Key(6));            // C, crouch's and cancel's by default
        page.Answer(false);                        // moved
        Assert.Equal("Crouch", page.Lost is { } lost ? ControlsPage.Label(lost) : null);
        Assert.Equal(["C"], Keys(page, "crouchToggle"));
        Assert.Empty(Keys(page, "crouch"));
        Assert.Equal(["C"], Keys(page, "cancel"));
        Assert.Equal("C was taken from Crouch and now works Crouch (Toggle).", page.Message);
    }

    [Fact]
    public void Cancel_shares_a_key_with_crouch_toggle_and_neither_takes_it_from_the_other()
    {
        // X as crouch toggle and cancel: with X bound to both, both work.
        var page = Fixtures.LaidPage(2560, 1440);
        page.Wait("crouchToggle", 0);
        page.Press(InputSource.Key(27));          // X
        page.Wait("cancel", 0);
        page.Press(InputSource.Key(27));
        Assert.Null(page.Lost);
        Assert.Equal("X works Cancel as well as Crouch (Toggle).", page.Message);
        Assert.Equal(["X"], Keys(page, "crouchToggle"));
        Assert.Equal(["X"], Keys(page, "cancel"));
        var table = BindingTable.Build(page.Values.Bindings, Fixtures.Code, Fixtures.Name);
        Assert.Empty(table.Problems);
        Assert.True(table.TryGet(InputSource.Key(27), out var action) && action == "crouchToggle");
        Assert.True(table.Cancels(InputSource.Key(27)));
    }

    [Fact]
    public void The_field_of_view_is_vertical_or_across_each_kept_on_its_own_and_OK_takes_both()
    {
        // The slider in vertical degrees (60 to 110) or across (70 to 140), switching without drift.
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        Assert.Equal(FieldOfViewAxis.Vertical, page.Values.FieldOfViewAxis);
        Assert.Equal("Vertical", s.Data.Text("fieldOfViewAxis"));
        Assert.True(s.Find("fieldOfView")!.Shown);
        Assert.False(s.Find("fieldOfViewHorizontal")!.Shown);
        Assert.Equal("80°", s.Data.Text("fieldOfViewText"));

        void Choose(string id, UiKey key, int times = 1)
        {
            s.SetFocus(s.Find(id));
            for (int i = 0; i < times; i++) s.Key(key);
            foreach (var a in s.TakeActions()) page.Handle(a);
        }
        Choose("fieldOfViewAxis", UiKey.Enter);
        Assert.Equal(FieldOfViewAxis.Horizontal, page.Values.FieldOfViewAxis);
        Assert.False(s.Find("fieldOfView")!.Shown);
        var across = (Slider)s.Find("fieldOfViewHorizontal")!;
        Assert.True(across.Shown);
        Assert.Equal((70f, 140f), (across.Min, across.Max));
        Assert.Equal("112° across", s.Data.Text("fieldOfViewText"));
        Choose("fieldOfViewHorizontal", UiKey.Right, 3);
        Assert.Equal("115° across", s.Data.Text("fieldOfViewText"));
        // Back and forth: each axis keeps its own value.
        for (int i = 0; i < 4; i++) Choose("fieldOfViewAxis", UiKey.Enter);
        Assert.Equal("115° across", s.Data.Text("fieldOfViewText"));
        Choose("fieldOfViewAxis", UiKey.Enter);
        Assert.Equal("80°", s.Data.Text("fieldOfViewText"));
        Choose("fieldOfViewAxis", UiKey.Enter);
        page.Handle(new UiAction("ok", "", s.Data, null));
        Assert.Equal(PageOutcome.Accepted, page.Outcome);
        Assert.Equal((FieldOfViewAxis.Horizontal, 80f, 115f), (page.Values.FieldOfViewAxis, page.Values.FieldOfView, page.Values.FieldOfViewHorizontal));

        // Opened again with those values (the settings' round trip through the page), it shows them.
        var again = Fixtures.LaidPage(2560, 1440, current: page.Values);
        Assert.Equal("Horizontal", again.Screen.Data.Text("fieldOfViewAxis"));
        Assert.Equal("115° across", again.Screen.Data.Text("fieldOfViewText"));
        again.Handle(new UiAction("ok", "", again.Screen.Data, null));
        Assert.Equal((FieldOfViewAxis.Horizontal, 80f, 115f), (again.Values.FieldOfViewAxis, again.Values.FieldOfView, again.Values.FieldOfViewHorizontal));
        // Defaults put the vertical 80 back, with 112 across kept for when the axis is switched.
        again.Handle(new UiAction("defaults", "", again.Screen.Data, null));
        Assert.Equal((FieldOfViewAxis.Vertical, 80f, 112f), (again.Values.FieldOfViewAxis, again.Values.FieldOfView, again.Values.FieldOfViewHorizontal));
    }
}
