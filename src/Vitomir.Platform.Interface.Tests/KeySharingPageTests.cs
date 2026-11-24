// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// Keys shared on the Controls page (<i>"some commands shouldn't be mutually exclusive to the same key
/// being bound"</i>): a key pressed for a cell that another action has makes the page ask — Share (both work it), Move (taken
/// off the other, lit, as earlier) or Cancel (nothing changes) — its buttons shown only while it asks; cancel and the
/// pointer's click still share without asking; free look's two rows are listed; double tap to dodge and its window are read
/// back on OK.
/// </summary>
public sealed class KeySharingPageTests
{
    static List<string> Keys(ControlsPage page, string action) => [.. page.Values.Bindings[action]];

    [Fact]
    public void Share_keeps_the_key_on_both_and_names_them_all()
    {
        var page = Fixtures.Page();
        page.Wait("jump", 1);
        page.Press(InputSource.Key(8));            // E, use's
        var q = page.Asking!.Value;
        Assert.Equal("jump", q.Action);
        Assert.Equal(["use"], q.Others);
        Assert.True(page.Screen.Data.Flag("asking"));
        Assert.Equal("E works Use. Share it (both work it), move it to Jump, or cancel?", page.Message);
        Assert.Equal(["Space"], Keys(page, "jump"));       // nothing changed while it asks
        page.Handle(new UiAction("share", "", page.Screen.Data, null));
        Assert.Null(page.Asking);
        Assert.False(page.Screen.Data.Flag("asking"));
        Assert.Equal(["Space", "E"], Keys(page, "jump"));
        Assert.Equal(["E"], Keys(page, "use"));
        Assert.Null(page.Lost);
        Assert.Equal("E now works Use and Jump.", page.Message);
        // A third action given E is asked about both.
        page.Wait("dodge", 0);
        page.Press(InputSource.Key(8));
        Assert.Equal(["jump", "use"], page.Asking!.Value.Others.Order());
        page.Answer(true);
        Assert.Equal(["E"], Keys(page, "dodge"));
        Assert.Contains("Dodge", page.Message);
    }

    [Fact]
    public void Move_takes_the_key_off_the_others_and_Cancel_changes_nothing()
    {
        var page = Fixtures.Page();
        page.Wait("jump", 0);
        page.Press(InputSource.Key(8));            // E
        page.Handle(new UiAction("move", "", page.Screen.Data, null));
        Assert.Equal(["E"], Keys(page, "jump"));
        Assert.Empty(Keys(page, "use"));
        Assert.Equal("use", page.Lost);
        Assert.Equal("E was taken from Use and now works Jump.", page.Message);

        var kept = Fixtures.Page();
        kept.Wait("jump", 0);
        kept.Press(InputSource.Key(8));
        kept.Handle(new UiAction("keepKey", "", kept.Screen.Data, null));
        Assert.Equal(["Space"], Keys(kept, "jump"));
        Assert.Equal(["E"], Keys(kept, "use"));
        Assert.Null(kept.Lost);
        Assert.Equal("E was left as it was: it works Use.", kept.Message);
        // A free key asks nothing.
        kept.Wait("jump", 1);
        kept.Press(InputSource.Key(10));           // G (F swaps the weapons)
        Assert.Null(kept.Asking);
        Assert.Equal(["Space", "G"], Keys(kept, "jump"));
    }

    [Fact]
    public void Free_look_has_its_two_rows_and_double_tap_and_its_window_are_read_back()
    {
        var page = Fixtures.Page();
        var labels = page.Screen.Data.Items("actions").Select(r => r.Text("label")).ToList();
        Assert.Contains("Free Look", labels);
        Assert.Equal(labels.IndexOf("Free Look") + 1, labels.IndexOf("Free Look (Toggle)"));
        Assert.Equal("Off", page.Screen.Data.Text("doubleTap"));
        Assert.Equal("0.25 s", page.Screen.Data.Text("doubleTapText"));
        page.Screen.Data.Set("doubleTap", "On");
        page.Screen.Data.Set("doubleTapSeconds", 0.333f);
        page.Handle(new UiAction("ok", "", page.Screen.Data, null));
        Assert.True(page.Values.DoubleTapDodge);
        Assert.Equal(0.33f, page.Values.DoubleTapSeconds, 1e-5f);
    }
}
