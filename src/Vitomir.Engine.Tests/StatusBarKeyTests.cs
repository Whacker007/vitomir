// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The status bar's key (`StatusBarKey`): 1.1's F11 (the interface sound 0),
/// read where the key handler is — the game camera, no dialog, vault, shop, advancement screen or chat — once a
/// press, and the `statusbar` binding's, so it goes wherever the player binds it. Pinned because, in
/// a play-test, F10 was the one that minimized the bar, but after an Alt+Tab it became F11,
/// and somehow switched back to F10 at some point. F10 is no key of 1.1's (its entry is read
/// nowhere; checked again for 1.1, where it just changes the status bar, with no `.nui`
/// able to bind one — `NuiKeyTests`), and a stuck Alt — the first suspect — changes neither key.
/// </summary>
public class StatusBarKeyTests
{
    static readonly KeyChord F10 = new(KeyNames.F(10)), F11 = new(KeyNames.F(11));

    [Fact]
    public void F11_toggles_the_bar_in_the_game_camera_once_a_press()
    {
        Assert.True(StatusBarKey.Reads(repeat: false, gameCamera: true));
        Assert.False(StatusBarKey.Reads(repeat: true, gameCamera: true));   // DirectInput has no repeat: one press, one toggle
        Assert.Equal(0, StatusBarKey.Sound);
        Assert.Equal(0x57, StatusBarKey.Dik);                                // DIK_F11
    }

    [Fact]
    public void F11_is_not_read_outside_the_game_camera_or_under_a_pane()
    {
        Assert.False(StatusBarKey.Reads(false, gameCamera: false));
        Assert.Equal("the Cutscene camera", StatusBarKey.NotRead("Cutscene", false, gameCamera: false));
        Assert.False(StatusBarKey.Reads(false, true, dialog: true));
        Assert.False(StatusBarKey.Reads(false, true, vault: true));
        Assert.False(StatusBarKey.Reads(false, true, advancement: true));
        Assert.False(StatusBarKey.Reads(false, true, shop: true));
        Assert.False(StatusBarKey.Reads(false, true, chat: true));
        Assert.Null(StatusBarKey.NotRead("Game", false, true));
    }

    [Fact]
    public void Only_the_status_bar_is_on_F11_and_nothing_of_1_1s_is_on_F10()
    {
        Assert.Equal([KeyCommand.StatusBar], KeyBindings.All.Where(i => i.Defaults.Contains(F11)).Select(i => i.Command));
        Assert.False(KeyBindings.AnyDefault(F10.Input));
        var keys = new KeyBindings();
        Assert.Equal(KeyCommand.StatusBar, Assert.Single(keys.Pressed(F11.Input, KeyMods.None, driving: true)).Command);
        Assert.Empty(keys.Pressed(F10.Input, KeyMods.None, driving: true));   // handed on as it came, to nobody (the vault is off it)
    }

    /// <summary>
    /// The key: hide only a bar that is shown **and in front**; otherwise the show, which shows a
    /// hidden bar and returns at once on a shown one — a bar shown on a layer below is left where it is, not brought
    /// to the front (an earlier build had guessed it would be).
    /// </summary>
    [Fact]
    public void A_shown_bar_is_hidden_only_in_front_and_otherwise_left_as_it_is()
    {
        Assert.Equal(StatusBarKey.BarToggle.Hide, StatusBarKey.Toggle(shown: true, inFront: true));
        Assert.Equal(StatusBarKey.BarToggle.Nothing, StatusBarKey.Toggle(shown: true, inFront: false));
        Assert.Equal(StatusBarKey.BarToggle.Show, StatusBarKey.Toggle(shown: false, inFront: true));
        Assert.Equal(StatusBarKey.BarToggle.Show, StatusBarKey.Toggle(shown: false, inFront: false));   // onto the top layer
        // Twice in front is back where it was; the key is once a press.
        bool shown = true;
        for (int i = 0; i < 2; i++) shown = StatusBarKey.Toggle(shown, inFront: true) == StatusBarKey.BarToggle.Show;
        Assert.True(shown);
    }

    [Fact]
    public void The_status_bar_goes_where_it_is_bound()
    {
        var keys = new KeyBindings();
        keys.Set(KeyCommand.StatusBar, [F10]);
        // F10 is now the status bar, handed on as 1.1's F11 for the handler that tests it ...
        Assert.Equal(KeyCommand.StatusBar, Assert.Single(keys.Pressed(F10.Input, KeyMods.None, driving: true)).Command);
        Assert.Equal(F11, KeyBindings.HandedOn(KeyCommand.StatusBar));
        // ... and F11, one of 1.1's keys whose command has moved, does nothing.
        Assert.Empty(keys.Pressed(F11.Input, KeyMods.None, driving: true));
        Assert.True(KeyBindings.AnyDefault(F11.Input));
    }

    [Fact]
    public void A_stuck_Alt_turns_neither_F10_nor_F11_into_the_other()
    {
        var keys = new KeyBindings();
        Assert.Equal(KeyCommand.StatusBar, Assert.Single(keys.Pressed(F11.Input, KeyMods.Alt, driving: true)).Command);
        Assert.Empty(keys.Pressed(F10.Input, KeyMods.Alt, driving: true));
        Assert.Equal(KeyMods.Alt, KeyHandOn.Mods(KeyCommand.StatusBar, KeyMods.Alt));   // F11 with Alt is still only the bar
    }
}
