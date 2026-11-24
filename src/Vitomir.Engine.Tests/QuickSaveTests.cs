// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The quick save and quick load** (ours). 1.1 has the save and load screens
/// and the autosave and no key for either. Vitomir gives them two keys, the keypad's +
/// and −, that save to and load from the slot last used since the program started, and open the save or
/// load screen when there is none yet. These tests hold the slot (remembered across a save, a load and a
/// new game, per kind of game, never the autosave), the gates (the escape menu's own, a network player's
/// greyed Save, no load in a network game), the screens when there is no slot, and the keys — which took
/// the keypad's pair off 1.1's zoom by default. The slot is the process's, so the class runs alone.
/// </summary>
[Collection(nameof(QuickSaveTests))]
public class QuickSaveTests
{
    [CollectionDefinition(nameof(QuickSaveTests), DisableParallelization = true)]
    public sealed class Serial;

    static readonly Func<int, bool> Every = _ => true;

    [Fact]
    public void With_no_slot_yet_the_keys_open_the_save_and_load_screens()
    {
        QuickSave.Forget();
        Assert.Null(QuickSave.Slot(network: false));
        Assert.Equal(QuickAct.SaveScreen, QuickSave.Decide(KeyCommand.QuickSave, true, false, false, Every));
        Assert.Equal(QuickAct.LoadScreen, QuickSave.Decide(KeyCommand.QuickLoad, true, false, false, Every));
    }

    [Fact]
    public void The_slot_is_the_one_last_saved_to_or_loaded_from_and_outlives_a_load_and_a_new_game()
    {
        QuickSave.Forget();
        QuickSave.Asked(MenuRequest.Save, 4, network: false);
        Assert.Equal(4, QuickSave.Slot(false));
        Assert.Equal(QuickAct.Save, QuickSave.Decide(KeyCommand.QuickSave, true, false, false, Every));
        QuickSave.Asked(MenuRequest.Restore, 7, network: false);
        Assert.Equal(7, QuickSave.Slot(false));
        Assert.Equal(QuickAct.Load, QuickSave.Decide(KeyCommand.QuickLoad, true, false, false, Every));
        // The main menu, an import: not a slot of ours, so the slot is kept (the process's).
        QuickSave.Asked(MenuRequest.MainMenu, 0, network: false);
        QuickSave.Asked(MenuRequest.Import, 2, network: false);
        Assert.Equal(7, QuickSave.Slot(false));
        // The autosave cannot be saved into, so loading it leaves the slot as it was.
        QuickSave.Asked(MenuRequest.Restore, 0, network: false);
        Assert.Equal(7, QuickSave.Slot(false));
        // A network game's host keeps its own: its saves are in another folder.
        Assert.Null(QuickSave.Slot(network: true));
        QuickSave.Asked(MenuRequest.Save, 3, network: true);
        Assert.Equal(3, QuickSave.Slot(true));
        Assert.Equal(7, QuickSave.Slot(false));
        // A new game forgets the single-player slot: its first quick save asks where, rather than going
        // into the last play-through's slot.
        QuickSave.Asked(MenuRequest.NewGame, 0, network: false);
        Assert.Null(QuickSave.Slot(false));
        Assert.Equal(QuickAct.SaveScreen, QuickSave.Decide(KeyCommand.QuickSave, true, false, false, Every));
        Assert.Equal(3, QuickSave.Slot(true));
    }

    [Fact]
    public void Neither_key_does_anything_where_the_escape_menu_would_not_open()
    {
        QuickSave.Forget();
        QuickSave.Asked(MenuRequest.Save, 5, network: false);
        Assert.Equal(QuickAct.Nothing, QuickSave.Decide(KeyCommand.QuickSave, escapeMenuOpens: false, false, false, Every));
        Assert.Equal(QuickAct.Nothing, QuickSave.Decide(KeyCommand.QuickLoad, escapeMenuOpens: false, false, false, Every));
        // Nor any other command.
        Assert.Equal(QuickAct.Nothing, QuickSave.Decide(KeyCommand.Regroup, true, false, false, Every));
    }

    [Fact]
    public void In_a_network_game_only_the_host_quick_saves_and_nobody_quick_loads()
    {
        QuickSave.Forget();
        Assert.Equal(QuickAct.Nothing, QuickSave.Decide(KeyCommand.QuickSave, true, network: true, host: false, Every));   // Save greyed
        Assert.Equal(QuickAct.SaveScreen, QuickSave.Decide(KeyCommand.QuickSave, true, network: true, host: true, Every));
        QuickSave.Asked(MenuRequest.Save, 2, network: true);
        Assert.Equal(QuickAct.Save, QuickSave.Decide(KeyCommand.QuickSave, true, network: true, host: true, Every));
        Assert.Equal(QuickAct.Nothing, QuickSave.Decide(KeyCommand.QuickLoad, true, network: true, host: true, Every));      // no Load in escapeMenuMP
    }

    [Fact]
    public void A_remembered_slot_whose_save_has_gone_opens_the_load_screen()
    {
        QuickSave.Forget();
        QuickSave.Asked(MenuRequest.Save, 6, network: false);
        Assert.Equal(QuickAct.LoadScreen, QuickSave.Decide(KeyCommand.QuickLoad, true, false, false, s => s != 6));
    }

    [Fact]
    public void The_keys_are_the_keypad_s_plus_and_minus_and_the_zoom_keeps_minus_and_equals()
    {
        var keys = new KeyBindings();
        Assert.Equal([new KeyChord(KeyNames.KeypadPlus)], keys[KeyCommand.QuickSave]);
        Assert.Equal([new KeyChord(KeyNames.KeypadMinus)], keys[KeyCommand.QuickLoad]);
        Assert.Equal([new KeyChord(KeyNames.EqualsKey)], keys[KeyCommand.ZoomIn]);
        Assert.Equal([new KeyChord(KeyNames.Minus)], keys[KeyCommand.ZoomOut]);
        Assert.Equal([KeyCommand.QuickSave], keys.Pressed(KeyNames.KeypadPlus, KeyMods.None, driving: true).Select(f => f.Command));
        Assert.Equal([KeyCommand.QuickLoad], keys.Pressed(KeyNames.KeypadMinus, KeyMods.None, driving: true).Select(f => f.Command));
        Assert.True(KeyBindings.IsActed(KeyCommand.QuickSave) && KeyBindings.IsActed(KeyCommand.QuickLoad));
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.QuickSave) && g.Commands.Contains(KeyCommand.QuickLoad));
        // Bound back on the key page, the keypad zooms as 1.1's: held, it holds 1.1's own key down.
        keys.Set(KeyCommand.ZoomIn, [new KeyChord(KeyNames.EqualsKey), new KeyChord(KeyNames.KeypadPlus)]);
        Assert.True(keys.Held(KeyCommand.ZoomIn, i => i == KeyNames.KeypadPlus, KeyMods.None));
    }

    [Fact]
    public void With_the_keys_switch_off_1_1_s_own_keys_are_in_force_the_keypad_zooms_and_there_are_no_quick_keys()
    {
        var original = KeyBindings.Original();
        Assert.Empty(original[KeyCommand.QuickSave]);
        Assert.Empty(original[KeyCommand.QuickLoad]);
        Assert.Equal([new KeyChord(KeyNames.EqualsKey), new KeyChord(KeyNames.KeypadPlus)], original[KeyCommand.ZoomIn]);
        Assert.Equal([new KeyChord(KeyNames.Minus), new KeyChord(KeyNames.KeypadMinus)], original[KeyCommand.ZoomOut]);
        Assert.Empty(original.Pressed(KeyNames.KeypadPlus, KeyMods.None, driving: true));
        // Everything else is the defaults (the other switches' own commands among them).
        foreach (var info in KeyBindings.All)
            if (info.Command is not (KeyCommand.QuickSave or KeyCommand.QuickLoad or KeyCommand.ZoomIn or KeyCommand.ZoomOut
                                     or KeyCommand.ShowObjects))   // on nothing too (ShowObjectsTests)
                Assert.Equal(info.Defaults, original[info.Command]);
    }

    [Fact]
    public void A_save_on_the_save_screen_and_a_load_on_the_load_screen_are_the_slot_from_then_on()
    {
        using var files = Install.Open();
        QuickSave.Forget();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var slots = new Slots(3);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = FrontEnd.StateGame, SaveSlots = slots, Describe = () => "The Convent (00:24:25)",
        };
        menus.OpenSave();
        menus.Pane(FrontEnd.SaveFile).Lists["spSaveList"].Select(4);           // slot 5, unused
        Click(menus, FrontEnd.SaveFile, "spSaveSaveButton");
        Click(menus, FrontEnd.InputBoxFile, "ibOkButton");
        Assert.Equal((MenuRequest.Save, 5), menus.TakeRequest());
        Assert.Equal(5, QuickSave.Slot(false));

        menus.OpenLoad();                                                      // slot 3 is the only row
        Click(menus, FrontEnd.LoadFile, "spLoadLoadButton");
        Assert.Equal((MenuRequest.Restore, 3), menus.TakeRequest());
        Assert.Equal(3, QuickSave.Slot(false));
    }

    sealed class Slots(params int[] saved) : ISaveSlots
    {
        public bool Exists(int slot) => saved.Contains(slot);
        public string? Description(int slot) => Exists(slot) ? "a save" : null;
    }

    static void Click(FrontEnd menus, string file, string control)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(control)!;
        menus.PointerAt(pane.X + e.Pos[0] + e.Size[0] / 2f, pane.Y + e.Pos[1] + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }
}
