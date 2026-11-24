// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The full-screen toggle's place among the bindings (ours, `FullScreen.cs`). 1.1 has no such key: it is
// full screen or a window for the whole run, as its command line said.
namespace Vitomir.Engine.Ui;

public sealed partial class KeyBindings
{
    /// <summary>
    /// **Commands the host takes before anything else hears the event** (ours): the full-screen toggle,
    /// **Alt+Enter** by default, works over a menu, a film, a conversation and a text field alike, so it is
    /// read ahead of the binding layer and of the menus (<c>KeyInput.FullScreenKey</c>), its press and its
    /// release both eaten. It is never one of 1.1's keys: plain Enter stays 1.1's — the chat line's,
    /// the console's and a text box's — and is not swallowed as a key whose command has moved
    /// (<see cref="AnyDefault"/>). It keeps its key with <c>[qol] keys</c> off (<see cref="Original"/>):
    /// it changes the window, not the game.
    /// </summary>
    public static readonly KeyCommand[] HostFirst = [KeyCommand.FullScreen];

    /// <summary>Whether a command is taken by the host ahead of everything (<see cref="HostFirst"/>).</summary>
    public static bool IsHostFirst(KeyCommand c) => Array.IndexOf(HostFirst, c) >= 0;

    /// <summary>
    /// Whether <paramref name="input"/> pressed with <paramref name="held"/> is <paramref name="command"/>'s:
    /// one of its chords on that input with all the chord's modifiers held (more held is still it).
    /// </summary>
    public bool Is(KeyCommand command, int input, KeyMods held) =>
        bound[command].Any(b => b.Input == input && (b.Mods & ~held) == 0);
}
