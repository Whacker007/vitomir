// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The menu screens' tips** (ours, <see cref="MenuTips"/>): every option on 1.1's Options screens, the multiplayer game
/// setup and character creation says what it does in a box by the pointer, half a second after the pointer rests on it —
/// over a check box's words on its row too — and not while a button is held.
/// </summary>
public class MenuTipsTests
{
    static readonly HashSet<string> Ours = new(StringComparer.OrdinalIgnoreCase)
    {
        OptionsMenu.FixesButton, OptionsMenu.KeysButton, AlwaysRunOption.Box, QuickLoadConfirm.Box, MostPlayersOption.Edit,
    };

    [Fact]
    public void Every_tip_names_a_control_its_screen_has_and_every_option_of_the_options_screens_has_one()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        foreach (var ((pane, control), tip) in MenuTips.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tip));
            if (Ours.Contains(control)) continue;
            Assert.True(assets.Pane(pane).Elements.Any(e => e.Name.Equals(control, StringComparison.OrdinalIgnoreCase)), $"{pane} has no {control}");
        }
        // Every check box of the Options screens and the game setup is tipped.
        foreach (var pane in new[] { OptionsMenu.GameplayFile, OptionsMenu.ControlsFile, OptionsMenu.AdvancedFile, OptionsMenu.SoundFile, MpMenus.CreateGameFile, MpMenus.PlayerCreateFile })
            foreach (var box in assets.Pane(pane).Elements.Where(e => e.Type == "checkbox"))
                Assert.True(MenuTips.Of(pane, box.Name) is not null, $"{pane}'s {box.Name} has no tip");
    }

    [Fact]
    public void A_tip_shows_half_a_second_into_a_rest_on_its_control_or_its_label_and_not_with_a_button_held()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateFrontEnd };
        var pane = menus.Pane(OptionsMenu.ControlsFile);
        menus.PushLayer();
        menus.Show(pane);
        var box = pane.Element("opconRepeatAttack")!;
        var (x, y) = pane.At(box);
        menus.PointerAt(x + 4, y + 4);
        menus.Frame(1000);
        Assert.Null(menus.Tip);                                         // not yet
        menus.Frame(1499);
        Assert.Null(menus.Tip);
        menus.Frame(1500);
        Assert.Equal(MenuTips.Of(OptionsMenu.ControlsFile, "opconrepeatattack"), menus.Tip?.Text);
        // Over its words, painted to the left of the box: the same tip, the rest kept.
        menus.PointerAt(x - 100, y + 4);
        menus.Frame(1600);
        Assert.Equal(MenuTips.Of(OptionsMenu.ControlsFile, "opconrepeatattack"), menus.Tip?.Text);
        // A button held: none.
        menus.LeftHeld = true;
        menus.Frame(2600);
        Assert.Null(menus.Tip);
        menus.LeftHeld = false;
        // Off every control: none.
        menus.PointerAt(2, 2);
        menus.Frame(4000);
        Assert.Null(menus.Tip);
    }
}
