// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which modifiers a press command's key carries when the binding layer hands it on (ours): found
// by the sweep of every declared command against its reader (`KeyReaders`).
namespace Vitomir.Engine.Ui;

/// <summary>
/// **The modifiers a handed-on key keeps** (ours). The binding layer hands a press command on as its first
/// default key, with whatever modifiers the chord did not use still held — so Ctrl+F1, the harness's, is
/// F1 with Ctrl. But the handlers test 1.1's keys *and* 1.1's modifiers, and one
/// of 1.1's keys read with a modifier is another command: R is regroup and **Shift+R ungroup** (the key handling
/// tests the letter and then the shift). So a regroup moved to J, pressed with Shift, was handed on as
/// Shift+R and ungrouped; and with ungroup moved to U, Shift+R still ungrouped — a key the player had
/// moved away. Until then.
/// <para>
/// The rule: the leftovers go with the key unless, with them, 1.1's own table (the defaults) would read
/// the handed-on key as **a different command**; then the key goes with its own modifiers alone. With 1.1's
/// keys this changes nothing — Shift+R is ungroup's own chord, and nothing else is a leftover that turns
/// one of 1.1's keys into another command.
/// </para>
/// </summary>
public static class KeyHandOn
{
    static readonly KeyBindings Faithful = new();

    /// <summary>The modifiers command <paramref name="c"/>'s handed-on key goes with, given what else is held.</summary>
    public static KeyMods Mods(KeyCommand c, KeyMods leftover)
    {
        var to = KeyBindings.HandedOn(c);
        var with = to.Mods | leftover;
        if (with == to.Mods) return with;
        // How 1.1's table reads the key with the leftovers: the most specific of its own chords.
        var read = Faithful.Pressed(to.Input, with, driving: false);
        return read.Count > 0 && read.All(r => KeyBindings.HandedOn(r.Command) != to) ? to.Mods : with;
    }
}
