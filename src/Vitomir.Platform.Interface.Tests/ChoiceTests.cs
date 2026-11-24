// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// The controls a dialog needs: a checkbox (its label worked as much as its mark, its flag written where the name lives), a radio
/// group (one selected, the arrow keys moving and selecting within it, Tab stopping at it once), every control's disabled state with
/// its reason, and the default and cancel buttons (Enter from a checkbox or a radio, Escape before the screen's back).
/// </summary>
public sealed class ChoiceTests
{
    const string File = """
        { "back": "back", "root": { "type": "column", "gap": 10, "children": [
            { "type": "checkbox", "id": "alone", "bind": "alone", "text": "Show the films", "action": "films", "width": 600 },
            { "type": "list", "id": "rows", "items": "rows", "gap": 4, "template":
              { "type": "checkbox", "id": "row", "bind": "selected", "action": "pick", "arg": "{n}", "enabled": "can", "why": "{whyNot}", "width": 600,
                "child": { "type": "column", "children": [ { "type": "text", "text": "{name}", "style": "value" }, { "type": "text", "text": "a note", "style": "hint" } ] } } },
            { "type": "list", "id": "copies", "items": "copies", "gap": 4, "template":
              { "type": "radio", "id": "copy", "bind": "copy", "value": "{n}", "group": "copy", "text": "{name}", "action": "choose", "arg": "{n}", "width": 600 } },
            { "type": "radio", "id": "small", "bind": "size", "value": "small", "text": "Small", "width": 300 },
            { "type": "radio", "id": "large", "bind": "size", "value": "large", "text": "Large", "width": 300, "enabled": "largeOk", "why": "Too large for this screen." },
            { "type": "row", "gap": 10, "children": [
              { "type": "button", "id": "ok", "text": "Replace", "action": "ok", "default": true },
              { "type": "button", "id": "keep", "text": "Keep", "action": "keep", "default": "keepFirst", "quiet": true },
              { "type": "button", "id": "cancel", "text": "Cancel", "action": "cancel", "cancel": true },
              { "type": "toggle", "id": "toggle", "bind": "t", "enabled": "never", "why": "Not now." }
            ] }
        ] } }
        """;

    static (Screen Screen, ScreenData Data, ScreenLoader Loader) Make()
    {
        var data = new ScreenData().Set("alone", false).Set("copy", "1").Set("size", "small").Set("largeOk", false).Set("t", "Off");
        for (int i = 0; i < 3; i++)
            data.Add("rows").Set("n", i.ToString()).Set("name", "SP" + i).Set("selected", false).Set("can", i != 2).Set("whyNot", i == 2 ? "It does not read." : "");
        for (int i = 0; i < 3; i++) data.Add("copies").Set("n", i.ToString()).Set("name", "Copy " + i);
        var loader = new ScreenLoader();
        var s = loader.Parse(File, "choices", data);
        s.Layout(new UiMetrics(1600, 1440));
        return (s, data, loader);
    }

    static Checkbox Row(Screen s, int i) => (Checkbox)s.Worked.OfType<Checkbox>().Where(c => c.Id == "row").ElementAt(i);
    static Radio Copy(Screen s, int i) => s.Worked.OfType<Radio>().Where(r => r.Group == "copy").ElementAt(i);

    [Fact]
    public void The_file_reads_with_nothing_said()
    {
        var (s, _, loader) = Make();
        Assert.Empty(loader.Problems);
        Assert.True(((Button)s.Find("keep")!).Quiet);                   // a secondary button, its style's size kept
        Assert.False(((Button)s.Find("ok")!).Quiet);
        Assert.Equal(((Button)s.Find("ok")!).Style.Em, ((Button)s.Find("keep")!).Style.Em);
    }

    [Fact]
    public void A_checkbox_is_worked_by_its_label_as_by_its_mark_and_writes_its_rows_own_flag()
    {
        var (s, data, _) = Make();
        var alone = (Checkbox)s.Find("alone")!;
        Assert.False(alone.Checked);
        // A click on the words, well right of the mark.
        float u = s.Metrics.Pixel;
        var words = alone.LabelRect;
        Assert.True(words.X >= alone.MarkRect.Right + Choice.Gap - 0.01f);
        Assert.Same(alone, s.HitTest((words.X + 40f) * u, (words.Y + words.Height * 0.5f) * u));
        s.PointerMove((words.X + 40f) * u, (words.Y + words.Height * 0.5f) * u);
        s.PointerDown();
        s.PointerUp();
        Assert.True(alone.Checked);
        Assert.True(data.Flag("alone"));
        // Space turns it over again.
        s.Key(UiKey.Space);
        Assert.False(data.Flag("alone"));
        Assert.Equal(["films", "films"], s.TakeActions().Select(a => a.Name));

        // A row's checkbox (a richer label it holds): its own item's flag written, not the screen's.
        var row = Row(s, 1);
        Fixtures.Click(s, row);
        Assert.True(data.Items("rows")[1].Flag("selected"));
        Assert.False(data.Items("rows")[0].Flag("selected"));
        Assert.False(data.TryGet("selected", out _));
        var a = Assert.Single(s.TakeActions());
        Assert.Equal(("pick", "1"), (a.Name, a.Argument));
        // The mark centred on the label's first line; the lines it holds beside the mark.
        var first = (TextPiece)row.Children[0].Children[0];
        Assert.Equal(first.Rect.Y + first.Rect.Height * 0.5f, row.MarkRect.Y + row.MarkRect.Height * 0.5f, 2);
        Assert.Equal(row.MarkRect.Right + Choice.Gap, first.Rect.X, 2);
        Assert.Equal(Choice.DefaultMark, row.MarkRect.Width);
    }

    [Fact]
    public void A_radio_group_has_one_selected_and_the_arrows_move_and_select_within_it()
    {
        var (s, data, _) = Make();
        Assert.Equal([false, true, false], Enumerable.Range(0, 3).Select(i => Copy(s, i).Checked));
        // A click on another: the screen's field (where the name lives) written, its action fired.
        Fixtures.Click(s, Copy(s, 2));
        Assert.Equal("2", data.Text("copy"));
        var chose = Assert.Single(s.TakeActions());
        Assert.Equal(("choose", "2"), (chose.Name, chose.Argument));
        Assert.Equal([false, false, true], Enumerable.Range(0, 3).Select(i => Copy(s, i).Checked));    // one selected, the others off
        // A click on the selected one changes nothing and fires nothing.
        Fixtures.Click(s, Copy(s, 2));
        Assert.Empty(s.TakeActions());
        // Down and Right to the next, round the end; Up and Left back; each selects.
        s.SetFocus(Copy(s, 2));
        s.Key(UiKey.Down);
        Assert.Same(Copy(s, 0), s.Focus);
        Assert.Equal("0", data.Text("copy"));
        s.Key(UiKey.Right);
        Assert.Equal("1", data.Text("copy"));
        s.Key(UiKey.Up);
        s.Key(UiKey.Left);
        Assert.Equal("2", data.Text("copy"));
        Assert.Same(Copy(s, 2), s.Focus);
        Assert.Equal(4, s.TakeActions().Count(x => x.Name == "choose"));
        // The other group is its own: Small stays selected, and Large, disabled, is passed over by the arrows.
        Assert.Equal("small", data.Text("size"));
        var small = (Radio)s.Find("small")!;
        s.SetFocus(small);
        Assert.False(s.StepRadio(small, +1));
        Assert.Equal("small", data.Text("size"));
    }

    [Fact]
    public void Tab_stops_at_a_radio_group_once_at_its_selected_radio()
    {
        var (s, data, _) = Make();
        data.Set("copy", "2");
        s.Layout(s.Metrics);
        s.SetFocus(Row(s, 1));
        s.Key(UiKey.Tab);
        Assert.Same(Copy(s, 2), s.Focus);                    // into the group at its selected one, not its first
        s.Key(UiKey.Tab);
        Assert.Same(s.Find("small"), s.Focus);               // out of it at once, into the next group
        s.Key(UiKey.BackTab);
        Assert.Same(Copy(s, 2), s.Focus);
        s.Key(UiKey.BackTab);
        Assert.Same(Row(s, 1), s.Focus);
        // A group with nothing selected: Tab stops at its first.
        data.Set("copy", "");
        s.Layout(s.Metrics);
        s.Key(UiKey.Tab);
        Assert.Same(Copy(s, 0), s.Focus);
    }

    [Fact]
    public void Every_kind_of_control_disabled_is_shown_passed_over_and_says_why()
    {
        var (s, data, _) = Make();
        var dead = (Checkbox)s.Find("rows")!.Children[2];
        var large = (Radio)s.Find("large")!;
        var toggle = (Toggle)s.Find("toggle")!;
        foreach (var w in new Worked[] { dead, large, toggle })
        {
            Assert.False(w.Enabled);
            Assert.DoesNotContain(w, s.Worked);
            var (x, y) = Fixtures.Middle(s, w);
            Assert.Null(s.HitTest(x, y));
            Assert.False(string.IsNullOrEmpty(w.WhyNot));
        }
        Assert.Equal("It does not read.", dead.WhyNot);
        Assert.Equal("Too large for this screen.", large.WhyNot);
        Assert.Equal("Not now.", toggle.WhyNot);
        // Enabled, a control says no reason.
        data.Set("largeOk", true);
        s.Layout(s.Metrics);
        Assert.True(large.Enabled);
        Assert.Equal("", large.WhyNot);
        Assert.Contains(large, s.Worked);
        // The theme's look draws a disabled choice's words in the muted colour, the mark's fill too.
        var list = new DrawList();
        s.Draw(list);
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Text && c.Text == "SP2" && c.Colour == Theme.Colours["muted"]);
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Text && c.Text == "SP1" && c.Colour == Theme.Styles["value"].Colour);
    }

    [Fact]
    public void Enter_works_the_default_button_from_a_choice_and_Escape_the_cancel_button_before_back()
    {
        var (s, data, _) = Make();
        var ok = (Button)s.Find("ok")!;
        var keep = (Button)s.Find("keep")!;
        Assert.True(ok.IsDefault);
        Assert.False(keep.IsDefault);
        Assert.True(((Button)s.Find("cancel")!).IsCancel);
        // From a checkbox, Enter is the default button's (Space the checkbox's own).
        s.SetFocus(s.Find("alone"));
        s.Key(UiKey.Enter);
        Assert.Equal(["ok"], s.TakeActions().Select(a => a.Name));
        Assert.False(data.Flag("alone"));
        // On another button, Enter is that button's.
        s.SetFocus(keep);
        s.Key(UiKey.Enter);
        Assert.Equal(["keep"], s.TakeActions().Select(a => a.Name));
        // Escape: the cancel button, not the screen's back.
        s.Key(UiKey.Escape);
        Assert.Equal(["cancel"], s.TakeActions().Select(a => a.Name));
        // A default named by the data follows it.
        data.Set("keepFirst", true);
        Assert.True(keep.IsDefault);
    }

    [Fact]
    public void Without_a_default_or_cancel_button_Enter_and_Escape_are_as_before()
    {
        const string plain = """
            { "back": "back", "root": { "type": "column", "children": [
                { "type": "checkbox", "id": "c", "bind": "c", "text": "On", "action": "c" },
                { "type": "button", "id": "b", "text": "B", "action": "b", "default": false }
            ] } }
            """;
        var loader = new ScreenLoader();
        var data = new ScreenData();
        var s = loader.Parse(plain, "plain", data);
        Assert.Empty(loader.Problems);
        s.Layout(new UiMetrics(1280, 800));
        s.SetFocus(s.Find("c"));
        s.Key(UiKey.Enter);
        Assert.True(data.Flag("c"));                            // the checkbox itself, as Enter always worked the focus
        s.Key(UiKey.Escape);
        Assert.Equal(["c", "back"], s.TakeActions().Select(a => a.Name));
        // A default that is not true nor a name is said.
        var bad = new ScreenLoader();
        bad.Parse("""{ "root": { "type": "button", "default": 3 } }""", "bad");
        Assert.Contains(bad.Problems, p => p.Contains("button.default should be true, false or a data name", StringComparison.Ordinal));
    }
}
