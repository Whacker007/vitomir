// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The edit field's two keys beyond typing: **Ctrl+V** pastes the clipboard's first line,
/// control characters dropped and the field's character mode not asked; **Tab** moves the keyboard
/// round the pane's focus ring of edit fields, skipping hidden ones, with Shift+Tab's odd start.
/// </summary>
public class EditFieldTests
{
    static FrontEnd Menus()
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        return new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
    }

    [Fact]
    public void Paste_takes_the_first_line_and_drops_control_characters()
    {
        var menus = Menus();
        menus.Input("t", "ab", 8);
        menus.Frame(1);
        menus.Paste("c\rd\te\nnot this");
        var edit = menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"];
        Assert.Equal("abcde", edit.Text);
        menus.Paste("0123456789");                                 // looks at no more than the limit, 8
        Assert.Equal("abcde012", edit.Text);
    }

    [Fact]
    public void Paste_ignores_a_digits_only_field_s_mode()
    {
        var menus = Menus();
        menus.Input("t", "", 511, accepts: EditAccepts.Digits);
        menus.Frame(1);
        menus.Type('x');
        menus.Paste("56cd");
        Assert.Equal("56cd", menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"].Text);
    }

    [Fact]
    public void Tab_walks_the_pane_s_fields_in_file_order_and_skips_a_hidden_one()
    {
        var pane = Menus().Pane("mpCreateGame");
        Assert.Equal("mpcgNameEdit", pane.Focus, ignoreCase: true);                    // the ring's first has the keyboard
        Assert.True(pane.Tab(shift: false));
        Assert.Equal("mpcgPW", pane.Focus, ignoreCase: true);
        pane.Tab(shift: false);
        Assert.Equal("mpcgPWConfirm", pane.Focus, ignoreCase: true);
        pane.Tab(shift: false);
        Assert.Equal(MostPlayersOption.Edit, pane.Focus, ignoreCase: true);            // ours, the ring's last
        pane.Tab(shift: false);
        Assert.Equal("mpcgNameEdit", pane.Focus, ignoreCase: true);                    // round again

        pane.Hidden.Add("mpcgPW");
        pane.Tab(shift: false);
        Assert.Equal("mpcgPWConfirm", pane.Focus, ignoreCase: true);
    }

    /// <summary>The tab order starts from the next field and only then walks back, so Shift+Tab goes forward past nothing hidden.</summary>
    [Fact]
    public void Shift_tab_starts_from_the_next_field()
    {
        var pane = Menus().Pane("mpCreateGame");
        pane.Tab(shift: true);
        Assert.Equal("mpcgPW", pane.Focus, ignoreCase: true);                          // forward, as 1.1 does

        pane.Focus = "mpcgNameEdit";
        pane.Hidden.Add("mpcgPW");
        Assert.False(pane.Tab(shift: true));                         // next hidden, back to itself: no move
        Assert.Equal("mpcgNameEdit", pane.Focus, ignoreCase: true);
    }

    [Fact]
    public void Tab_in_a_pane_with_one_field_keeps_it()
    {
        var menus = Menus();
        menus.Input("t", "ab", 8);
        menus.Frame(1);
        menus.Type('\t');
        var box = menus.Pane(FrontEnd.InputBoxFile);
        Assert.Equal("ibEdit", box.Focus, ignoreCase: true);
        Assert.Equal("ab", box.Edits["ibEdit"].Text);
    }
}
