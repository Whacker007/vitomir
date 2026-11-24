// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ctrl+C and Ctrl+V in the in-game text lines: the host's clipboard (ours, always on).
using Vitomir.Engine.Ui;
using Vitomir.Platform.Modules;

namespace Vitomir;

/// <summary>
/// The clipboard half of <see cref="TypedLine"/> (ours): the system clipboard read and written (the host's live side), and the
/// chord read off the key event itself — its own <c>mod</c>, not SDL's keyboard state, so a chord the
/// harness pushes (<c>--ui-click key:Ctrl+V</c>) is read as a hand's is.
/// </summary>
static class ClipboardKeys
{
    /// <summary>Whether a key event is Ctrl with that key.</summary>
    public static bool Is(in LoopInput e, int key) =>
        e.Kind == LoopInputKind.KeyDown && e.Key == key && (e.Mods & LoopMods.Ctrl) != 0;

    /// <summary>The clipboard's text, or null.</summary>
    public static string? Clip() => InputTape.Clipboard(InputTape.Live.Clipboard);   // a tape's; the host's clipboard

    /// <summary>Ctrl+C: the text onto the clipboard (<see cref="TypedLine.Copied"/> chose it); nothing when there is none.</summary>
    public static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        InputTape.Live.SetClipboard(text);
        Console.WriteLine($"  clipboard: copied \"{text}\"");
    }
}
