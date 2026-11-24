// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A key let go while a menu is up: once "not built", read and matched.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **A key let go while a menu or a text field has the keyboard is let go for the game too**. In 1.1 the
/// key-state array follows the keyboard whatever is up: the main loop reads DirectInput's buffer
/// every frame of the game's sequence before the controls run, a menu or not — the escape
/// menu's own Escape is read from that array — and the world's held keys are read from the array's
/// state (entry <c>+0</c>, bit 0x80) each time they are asked: the swivel's X, the modifier
/// flags. So a key held into a menu and let go there is **up** for the world when the menu goes. What a
/// release does <em>on the frame it comes</em>  is missed by a handler that is not running then —
/// the world's key handling is not with a dialog up — but Vitomir's world has no key that acts on its release, only held keys
/// that stop.
/// <para>
/// **Vitomir** (ours, the host's <c>KeyInput</c>): a menu up gets every event as it came, so until then a key
/// held into it had its release taken by the menu, and the binding layer kept it down — a held X swivelled on
/// after the escape menu until X was pressed again, unless a focus change let go of everything. Now the
/// release still goes to the menu as it came, and what the key was handed on as (<see cref="HeldInputs{T}"/>) is
/// released to the game's handlers after it, as a focus change releases it. Only a key: a mouse button let go
/// over a menu is the menu's click (1.1's button state follows it the same way, but a button held into a menu
/// is the rare case and its release is a place on the menu; unchanged).
/// </para>
/// </summary>
public static class MenuReleases
{
    /// <summary>
    /// A key came up while a menu or a text field had the keyboard: when the binding layer holds it, what it was
    /// handed on as (<paramref name="made"/>), to release for the game after the menu has had the key; it is no
    /// longer held. False for a press, a button, or a key the layer does not hold (one pressed with the menu up).
    /// </summary>
    public static bool Lift<T>(HeldInputs<T> down, int input, bool keyUp, bool button, out List<T> made)
    {
        made = [];
        return keyUp && !button && down.Lift(input, out made);
    }
}
