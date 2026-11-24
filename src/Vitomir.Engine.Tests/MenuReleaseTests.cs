// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A key held into a menu and let go there is let go for the game too (`MenuReleases`, once "not built"):
/// 1.1's key-state array follows the keyboard whatever is up (read each frame of the game's
/// sequence) and its held keys are read from that state (the swivel's X), so the key is up when the menu
/// goes. Earlier, Vitomir's menu took the release and the binding layer kept the key down — a held X swivelled
/// on after the escape menu — unless a focus change let go of everything.
/// </summary>
public class MenuReleaseTests
{
    static readonly int X = KeyNames.Letter('X'), Z = KeyNames.Letter('Z');

    [Fact]
    public void A_key_held_into_a_menu_and_let_go_there_is_released_for_the_game()
    {
        var down = new HeldInputs<string>();
        down.Press(X, ["swivel"]);
        down.Press(Z, ["look"]);
        Assert.True(MenuReleases.Lift(down, X, keyUp: true, button: false, out var made));
        Assert.Equal(["swivel"], made);                                                        // released as what it went down as
        Assert.False(down.TryGet(X, out _));                                                   // and no longer held
        Assert.True(down.TryGet(Z, out _));                                                    // the other key still is
        Assert.False(MenuReleases.Lift(down, X, keyUp: true, button: false, out _));           // once
    }

    [Fact]
    public void A_press_a_button_or_a_key_pressed_under_the_menu_is_the_menus_alone()
    {
        var down = new HeldInputs<string>();
        down.Press(X, ["swivel"]);
        down.Press(KeyChord.MouseBase + 1, ["act"]);
        Assert.False(MenuReleases.Lift(down, X, keyUp: false, button: false, out _));          // a press (a repeat) under the menu
        Assert.False(MenuReleases.Lift(down, KeyChord.MouseBase + 1, keyUp: true, button: true, out _));   // a button's release is the menu's click
        Assert.True(down.TryGet(KeyChord.MouseBase + 1, out _));
        Assert.False(MenuReleases.Lift(down, Z, keyUp: true, button: false, out _));           // Z went down with the menu up: never the game's
        Assert.Equal(2, down.Count);
    }
}
