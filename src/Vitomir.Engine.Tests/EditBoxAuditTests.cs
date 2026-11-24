// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The two edit-field differences a comparison with 1.1 found (
/// a-ui3's section): a **password field draws a star for each character** (the field's bit 0,
/// set on the create-game screen's two password boxes), and **a text set outright keeps
/// one fewer than the field's limit** (the terminator goes at the limit) while
/// typing fills it. Both pinned here against the shipped panes.
/// </summary>
public class EditBoxAuditTests
{
    static FrontEnd Menus(Vfs.GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        return new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
    }

    [Fact]
    public void A_password_is_drawn_as_stars_and_the_rest_as_typed()
    {
        Assert.Equal("*****", EditMask.Shown("hunts", password: true));
        Assert.Equal("hunts", EditMask.Shown("hunts", password: false));
        Assert.Equal("", EditMask.Shown("", password: true));
    }

    [Fact]
    public void The_create_game_screen_stars_its_two_password_boxes_and_not_the_name()
    {
        using var files = Install.Open();
        var menus = Menus(files);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        var mp = new MpMenus(menus, files, new Strings(files).Find, crypt) { Port = 0, Upnp = false };
        try
        {
            mp.Shot("mpcreategame");
            var pane = menus.Pane(MpMenus.CreateGameFile);
            foreach (var name in new[] { "mpcgPW", "mpcgPWConfirm" })
            {
                var box = pane.Edits[name];
                Assert.True(box.Password);
                foreach (char c in "salubri") box.Type(c);
                Assert.Equal("salubri", box.Text);                               // the text is kept as typed
                Assert.StartsWith("*******", box.Shown);                           // and drawn as stars
                Assert.DoesNotContain("salubri", box.Shown);
            }
            Assert.False(pane.Edits["mpcgNameEdit"].Password);
            Assert.Equal("Anna's game", pane.Edits["mpcgNameEdit"].Shown.TrimEnd(MenuEdit.Caret));
        }
        finally
        {
            mp.Server?.Dispose();
            mp.Client?.Dispose();
            if (Directory.Exists(crypt)) Directory.Delete(crypt, recursive: true);
        }
    }

    [Fact]
    public void A_set_text_keeps_one_fewer_than_the_limit_and_typing_fills_it()
    {
        Assert.Equal("abcd", EditMask.SetCut("abcdefg", 5));
        Assert.Equal("abcd", EditMask.SetCut("abcd", 5));
        Assert.Equal("", EditMask.SetCut("abc", 1));

        using var files = Install.Open();
        var menus = Menus(files);
        menus.Input("t", "abcdefg", 5);
        var edit = menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"];
        Assert.Equal("abcd", edit.Text);                   // The terminator at the limit, 5
        edit.Type('e');
        Assert.Equal("abcde", edit.Text);                  // Typed while shorter than the limit
        edit.Type('f');
        Assert.Equal("abcde", edit.Text);                  // full
    }
}
