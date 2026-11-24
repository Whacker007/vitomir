// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The show-objects key's place among the bindings (ours, "If there isn't
// a key already for it, bind it to left alt and make it remappable").
namespace Vitomir.Engine.Ui;

public sealed partial class KeyBindings
{
    /// <summary>
    /// **Held commands the host reads for itself** (ours): they have no 1.1 key behind them, so the
    /// keyboard the game polls (<c>KeyInput.State</c>) never has a key put down or taken up for them.
    /// The show-objects key (<see cref="ShowObjects"/>) is **Left Alt** by default, which is also 1.1's
    /// Alt (bit 0x4 of the modifier mask, and the raw Left Alt the hold reads for a pick-up to the cursor):
    /// the two are read side by side, so holding Left Alt lights the objects **and** an Alt-click is
    /// still 1.1's Alt-click — and moved to another key, holding that key lights the objects and sets
    /// no Alt. With <c>[qol] keys</c> off it is on nothing (<see cref="Original"/>).
    /// </summary>
    public static readonly KeyCommand[] HostHeld = [KeyCommand.ShowObjects];

    /// <summary>Whether a held command's chords stand for its 1.1 keys in the polled keyboard (all but <see cref="HostHeld"/>).</summary>
    public static bool HandsOn(KeyCommand c) => Array.IndexOf(HostHeld, c) < 0;
}
