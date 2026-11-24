// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// **A list's rows reordered by the pointer and the keyboard** (a list's <c>reorder</c> and a row's <c>handle</c>): a press on the handle
/// and a move past a few pixels drags the row, the drop line between the rows it would land between, and the release fires the list's
/// action with the row's index and its new one; a move shorter than that does nothing, as a press and release on the handle does;
/// Alt+Up and Alt+Down on a row's checkbox move the row, the focus going with it; the handle is no stop of the keyboard's focus; a list
/// without <c>reorder</c>, as every game screen's, is not dragged.
/// </summary>
public sealed class DragTests
{
    const string File = """
        { "root": { "type": "column", "gap": 10, "children": [
            { "type": "list", "id": "mods", "items": "mods", "gap": 6, "reorder": "move", "template":
              { "type": "row", "gap": 8, "children": [
                { "type": "handle", "id": "grip" },
                { "type": "checkbox", "id": "on", "bind": "on", "text": "{name}", "action": "toggle", "arg": "{id}", "width": 500 } ] } },
            { "type": "list", "id": "plain", "items": "plain", "gap": 6, "template":
              { "type": "row", "children": [ { "type": "handle", "id": "loose" }, { "type": "button", "text": "{name}", "action": "x" } ] } }
        ] } }
        """;

    static (Screen Screen, ScreenData Data, ScreenLoader Loader) Make()
    {
        var data = new ScreenData();
        foreach (var n in new[] { "first", "second", "third", "fourth" }) data.Add("mods").Set("id", n).Set("name", n).Set("on", true);
        data.Add("plain").Set("name", "only");
        var loader = new ScreenLoader();
        var s = loader.Parse(File, "drag", data);
        s.Layout(new UiMetrics(1600, 1440));
        return (s, data, loader);
    }

    static ListPiece Mods(Screen s) => (ListPiece)s.Find("mods")!;
    static Piece Grip(Screen s, int row) => Mods(s).Children[row].Walk().First(p => p is DragHandle);
    static Piece Box(Screen s, int row) => Mods(s).Children[row].Walk().First(p => p is Checkbox);

    static void At(Screen s, float x, float y) => s.PointerMove(x * s.Metrics.Pixel, y * s.Metrics.Pixel);

    [Fact]
    public void The_file_reads_and_the_handle_is_no_stop_of_the_keyboards_focus()
    {
        var (s, _, loader) = Make();
        Assert.Empty(loader.Problems);
        Assert.Equal("move", Mods(s).Reorder);
        Assert.DoesNotContain(s.Worked, p => p is DragHandle);
        Assert.True(Grip(s, 0).Interactive);
        Assert.Same(Grip(s, 0), s.HitTest(Fixtures.Middle(s, Grip(s, 0)).X, Fixtures.Middle(s, Grip(s, 0)).Y));
        s.SetFocus(Box(s, 0));
        s.Key(UiKey.Tab);
        Assert.Same(Box(s, 1), s.Focus);                                              // from one row's checkbox to the next's, past its handle
    }

    [Fact]
    public void A_row_dragged_by_its_handle_past_a_few_pixels_is_dropped_where_the_line_is()
    {
        var (s, _, _) = Make();
        var grip = Grip(s, 0).Rect;
        float x = grip.X + grip.Width * 0.5f, y = grip.Y + grip.Height * 0.5f;
        At(s, x, y);
        s.PointerDown();
        Assert.Null(s.Drag);
        At(s, x, y + 2f);                                                             // under a few pixels: no drag yet
        Assert.Null(s.Drag);
        var third = Mods(s).Children[2].Rect;
        At(s, x, third.Y + third.Height * 0.75f);                                     // the lower half of the third row: after it
        var d = Assert.IsType<DragState>(s.Drag);
        Assert.Equal((0, 2), (d.From, d.To));
        Assert.Equal((third.Bottom + Mods(s).Children[3].Rect.Y) * 0.5f, d.LineY, 3);
        var list = new DrawList();
        s.Draw(list);                                                                 // the drop line and the lifted row drawn over the screen
        s.PointerUp();
        Assert.Null(s.Drag);
        var a = Assert.Single(s.TakeActions());
        Assert.Equal(("move", "0 2", "first"), (a.Name, a.Argument, a.Scope.Text("id")));
        Assert.True(((Checkbox)Box(s, 0)).Checked);                                   // the drop clicked nothing

        // To the top: the line above the first row.
        var last = Grip(s, 3).Rect;
        At(s, last.X + 4f, last.Y + 4f);
        s.PointerDown();
        At(s, last.X + 4f, Mods(s).Children[0].Rect.Y + 2f);
        Assert.Equal((3, 0), (s.Drag!.From, s.Drag.To));
        Assert.Equal(Mods(s).Children[0].Rect.Y - 3f, s.Drag.LineY, 3);
        s.PointerUp();
        Assert.Equal("3 0", Assert.Single(s.TakeActions()).Argument);
    }

    [Fact]
    public void A_press_and_release_on_the_handle_or_a_drop_where_it_was_does_nothing()
    {
        var (s, _, _) = Make();
        Fixtures.Click(s, Grip(s, 1));
        Assert.Empty(s.TakeActions());
        var grip = Grip(s, 1).Rect;
        At(s, grip.X + 4f, grip.Y + 4f);
        s.PointerDown();
        At(s, grip.X + 4f, grip.Y + 14f);                                             // dragged within its own place
        Assert.Equal((1, 1), (s.Drag!.From, s.Drag.To));
        s.PointerUp();
        Assert.Empty(s.TakeActions());
        // A list that does not reorder: its handle drags nothing.
        Fixtures.Click(s, s.Find("loose")!);
        var loose = s.Find("loose")!.Rect;
        At(s, loose.X + 4f, loose.Y + 4f);
        s.PointerDown();
        At(s, loose.X + 4f, loose.Y - 200f);
        Assert.Null(s.Drag);
        s.PointerUp();
        Assert.Empty(s.TakeActions());
    }

    [Fact]
    public void Alt_up_and_down_move_the_focused_row_and_the_focus_goes_with_it()
    {
        var (s, data, _) = Make();
        s.SetFocus(Box(s, 1));
        s.Key(UiKey.AltDown);
        Assert.Equal("1 2", Assert.Single(s.TakeActions()).Argument);
        // The host moves its items (as the launcher's list is filled again), and the next layout puts the focus on the row's new place.
        var items = data.Items("mods");
        (items[1], items[2]) = (items[2], items[1]);
        s.Layout(s.Metrics);
        Assert.Same(Box(s, 2), s.Focus);
        s.Key(UiKey.AltUp);
        Assert.Equal("2 1", Assert.Single(s.TakeActions()).Argument);
        s.SetFocus(Box(s, 0));
        s.Key(UiKey.AltUp);                                                           // first already: nothing
        s.SetFocus(Box(s, 3));
        s.Key(UiKey.AltDown);                                                         // last already: nothing
        Assert.Empty(s.TakeActions());
        s.SetFocus(s.Worked.OfType<Button>().Single());
        s.Key(UiKey.AltUp);                                                           // a row of a list that does not reorder: nothing
        Assert.Empty(s.TakeActions());
    }
}
