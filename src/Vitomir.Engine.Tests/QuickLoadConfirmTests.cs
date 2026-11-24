// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Confirm quick load** (ours — <i>"Leave it non confirm by default, but
/// … add a feature to turn on a confirm before quickloading, and have it default off"</i>). An Options
/// setting (<see cref="GameOptions.ConfirmQuickLoad"/>) on 1.1's Gameplay screen, off by default, kept in
/// <c>[options]</c> and on a tape's <c>options</c> line; on, the quick load asks OK or Cancel in the game's
/// own message box (the one the New Game's autosave warning uses) and loads only on OK, the game
/// paused while it asks. These tests hold the default, the file, the box on the screen, and the question.
/// </summary>
public class QuickLoadConfirmTests
{
    static FrontEnd Menus(GameFiles files, out Strings text)
    {
        var assets = new UiAssets(files);
        text = new Strings(files);
        return new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
    }

    [Fact]
    public void It_is_off_by_default_kept_in_the_options_section_and_on_the_tape()
    {
        var options = new GameOptions();
        Assert.False(options.ConfirmQuickLoad);
        Assert.Contains(options.Values(), p => p.Key == "confirmQuickLoad" && p.Value == "0");
        options.ConfirmQuickLoad = true;
        Assert.Contains(options.Values(), p => p.Key == "confirmQuickLoad" && p.Value == "1");
        Assert.True(options.Clone().ConfirmQuickLoad);
        var read = new GameOptions();
        read.Apply(IniFile.Parse("[options]\nconfirmQuickLoad = 1\n"));
        Assert.True(read.ConfirmQuickLoad);
        read.Apply(IniFile.Parse("[options]\nconfirmQuickLoad = 0\n"));
        Assert.False(read.ConfirmQuickLoad);
        // The tape's options line is every public primitive property of GameOptions (InputTape.Settings).
        var p = typeof(GameOptions).GetProperty(nameof(GameOptions.ConfirmQuickLoad))!;
        Assert.True(p.CanRead && p.CanWrite && p.PropertyType.IsPrimitive);
    }

    [Fact]
    public void Off_the_quick_load_loads_at_once()
    {
        using var files = Install.Open();
        var menus = Menus(files, out var text);
        Assert.True(QuickLoadConfirm.Load(menus, 4, new GameOptions(), text.Find));
        Assert.Equal((MenuRequest.Restore, 4), (menus.Request, menus.RequestSlot));
        Assert.False(menus.Up);
        Assert.False(menus.Paused);
    }

    [Fact]
    public void On_it_asks_in_the_message_box_and_loads_only_on_ok()
    {
        using var files = Install.Open();
        var menus = Menus(files, out var text);
        var options = new GameOptions { ConfirmQuickLoad = true };

        Assert.True(QuickLoadConfirm.Load(menus, 4, options, text.Find));
        var box = menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(menus.IsUp(box));
        Assert.Equal(MenuRequest.None, menus.Request);            // nothing loaded yet
        Assert.True(menus.Paused);                                // the game stands still while it asks
        Assert.Equal(text.Find("UI_WARNING"), box.Texts["mbTitle"]);
        Assert.Contains(Strings.Format(text.Find, "UI_SPLOADSAVE_SLOT", "%d4"), box.Texts["mbTextBox"]);
        Assert.DoesNotContain("mbOkButton", box.Hidden);
        Assert.DoesNotContain("mbCancelButton", box.Hidden);
        // One box at a time: a second press while it is up asks nothing more.
        Assert.False(QuickLoadConfirm.Load(menus, 4, options, text.Find));

        // Cancel (Escape): nothing loaded, the game going again.
        menus.Drawn();                                            // a key is not the box's until it has been drawn once
        menus.Key(MenuKey.Escape);
        Assert.False(menus.IsUp(box));
        Assert.Equal(MenuRequest.None, menus.Request);
        Assert.False(menus.Paused);

        // OK (Enter): the load.
        Assert.True(QuickLoadConfirm.Load(menus, 4, options, text.Find));
        menus.Drawn();
        menus.Key(MenuKey.Enter);
        Assert.False(menus.IsUp(box));
        Assert.Equal((MenuRequest.Restore, 4), (menus.Request, menus.RequestSlot));
    }

    [Fact]
    public void A_game_the_pause_key_had_stopped_stays_stopped_after_the_question()
    {
        using var files = Install.Open();
        var menus = Menus(files, out var text);
        menus.Paused = true;
        QuickLoadConfirm.Load(menus, 2, new GameOptions { ConfirmQuickLoad = true }, text.Find);
        menus.Drawn();                                            // a key is not the box's until it has been drawn once
        menus.Key(MenuKey.Escape);
        Assert.True(menus.Paused);
    }

    [Fact]
    public void The_gameplay_screen_has_the_box_above_1_1_s_two_and_ok_puts_it_in_force()
    {
        using var files = Install.Open();
        var menus = Menus(files, out var text);
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-confirmload-{Guid.NewGuid():N}.ini");
        var options = new GameOptions();
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = ini };
        menus.Options = screen;
        screen.Open();
        screen.OpenGameplay();
        var pane = menus.Pane(OptionsMenu.GameplayFile);
        var box = pane.Element(QuickLoadConfirm.Box)!;
        Assert.Equal(pane.Element("opgpBloodOn")!.Pos[0], box.Pos[0]);
        Assert.Equal(pane.Element("opgpBloodOn")!.Pos[1] + 25, box.Pos[1]);     // 25 above the blood box, as 1.1's two are spaced
        Assert.Equal(QuickLoadConfirm.Caption, pane.Texts[QuickLoadConfirm.Label]);
        Assert.False(pane.Checks[QuickLoadConfirm.Box]);
        pane.Check(QuickLoadConfirm.Box, true);
        Assert.False(options.ConfirmQuickLoad);                   // nothing kept until OK
        var ok = pane.Element("opgpOKButton")!;
        var (x, y) = pane.At(ok);
        menus.PointerAt(x + ok.Size[0] / 2f, y + ok.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        Assert.True(options.ConfirmQuickLoad);                    // in force at once: the key reads it as it is pressed
        Assert.Equal("1", IniFile.Read(ini)["options.confirmQuickLoad"]);
        File.Delete(ini);
        screen.OpenGameplay();
        Assert.True(pane.Checks[QuickLoadConfirm.Box]);
    }
}
