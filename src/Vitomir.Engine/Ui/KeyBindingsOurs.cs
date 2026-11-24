// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The commands that are Vitomir's own and have no 1.1 key to be handed on as — the swivel lock, the quick
// save and the quick load (ours) — and 1.1's own keys, which are
// no longer quite the defaults since the quick save and load took the keypad's + and −.
namespace Vitomir.Engine.Ui;

public sealed partial class KeyBindings
{
    /// <summary>
    /// The commands the host **acts on itself** (ours): they have no key of 1.1's to be handed on as
    /// (<see cref="HandedOn"/>), so the binding layer hands on nothing for them and tells the host
    /// which command it was (<c>KeyInput.TakeAct</c>). The swivel lock is <c>[qol] camera</c>'s (it is
    /// in <see cref="Camera"/>); the quick save and load are the key page's own, live with
    /// <c>[qol] keys</c> (<see cref="Original"/> has them on nothing).
    /// </summary>
    public static readonly KeyCommand[] Acted = [KeyCommand.SwivelLock, KeyCommand.QuickSave, KeyCommand.QuickLoad];

    /// <summary>Whether the host acts on a command itself rather than reading a handed-on 1.1 key (<see cref="Acted"/>).</summary>
    public static bool IsActed(KeyCommand c) => Array.IndexOf(Acted, c) >= 0;

    /// <summary>
    /// **1.1's keypad zoom** (the keypad's − and + beside − and =), which is not
    /// among the defaults (ours): the quick save and load on the keypad's + and −
    /// (<i>"Bind them to numpad+ and numpad- by default"</i>), so by default those two keys are the quick
    /// save's and load's and the zoom is − and = and the wheel. Bound back to the zoom on the key page
    /// they zoom as 1.1's do; with <c>[qol] keys</c> off they are the zoom's again (<see cref="Original"/>).
    /// </summary>
    public static readonly (KeyCommand Command, KeyChord Chord)[] KeypadZoom =
    [
        (KeyCommand.ZoomOut, new KeyChord(KeyNames.KeypadMinus)),
        (KeyCommand.ZoomIn, new KeyChord(KeyNames.KeypadPlus)),
    ];

    /// <summary>
    /// **1.1's own keys**, what <c>[qol] keys</c> off puts in force whatever the file says: the defaults,
    /// with the keypad's − and + back on the zoom (<see cref="KeypadZoom"/>) and the quick save and load
    /// on nothing — 1.1 has only the save and load screens. The other switches' own commands (driving,
    /// the drag swivel, the swivel lock) keep their defaults, as each is read only under its own switch.
    /// </summary>
    public static KeyBindings Original()
    {
        var keys = new KeyBindings();
        foreach (var (command, chord) in KeypadZoom) keys.Set(command, [.. keys[command], chord]);
        keys.Set(KeyCommand.QuickSave, []);
        keys.Set(KeyCommand.QuickLoad, []);
        keys.Set(KeyCommand.ShowObjects, []);   // KeyBindingsShow.cs
        return keys;
    }
}
