// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The developer harness's own keys (ours), gathered so that none of them takes a key of 1.1's.
namespace Vitomir.Engine.Ui;

/// <summary>What one of the harness's keys does (ours).</summary>
public enum HarnessKey
{
    /// <summary>Ctrl+F1: where the player and the camera are, printed.</summary>
    Where,
    /// <summary>Ctrl+F2: the diagnostic overlay on or off.</summary>
    Overlay,
    /// <summary>Ctrl+F3: the overlay's size.</summary>
    OverlaySize,
    /// <summary>Ctrl+F5: the character sheet (beside 1.1's C).</summary>
    Sheet,
}

/// <summary>
/// **The developer harness's keys** (ours). **All of them wait for <c>--debug</c>**, so a
/// player's keyboard is 1.1's alone: the four on the function keys that 1.1 gives the quick items answer
/// only **with Ctrl**, and never on a key's repeat — 1.1 uses a quick item on F1–F5 whatever else is held
/// (the key's frame alone is tested), so without <c>--debug</c> Ctrl+F1 is quick item 1 again.
/// <para>
/// **The vault has no key** (*"The only way to open the vault in the base game is directly
/// through the box at the haven"*): 1.1 opens it through the haven's script (the native <c>Vault()</c>) and,
/// for a developer, its own console command <c>vault</c> (in <c>DevConsole</c>, on the grave key in
/// every game) — which is where the harness's vault now is. F10 is no key of 1.1's (<see cref="StatusBarKey"/>).
/// </para>
/// <para>
/// Earlier, the four F-keys answered **without** Ctrl whenever the quick-slot handler let them by —
/// outside the game camera, with nobody steered, in a shop — so F2 in a cutscene switched the overlay and
/// a quick item moved to J did it too; a fifth, **F4**, toggled the status bar beside 1.1's F11 (an alias
/// from before the quick items came in), which is how a quick item's key could put the bar down; and
/// F10 opened the vault and Ctrl+F1–F5 were the harness's for anybody. For a while F10 was still the vault
/// with <c>--debug</c>, and the vault held the keyboard, so F11 answered nothing until Escape or F10 closed it.
/// </para>
/// </summary>
public static class HarnessKeys
{
    /// <summary>One key: the chord, what it does, and whether it waits for <c>--debug</c>.</summary>
    public readonly record struct Key(HarnessKey What, KeyChord Chord, bool DebugOnly, string Name);

    /// <summary>Every harness key on the function row. (T, the conversation's restart, is no key of 1.1's and is the key handler's own.)</summary>
    public static readonly Key[] All =
    [
        new(HarnessKey.Where, new KeyChord(KeyNames.F(1), KeyMods.Ctrl), true, "where things are"),
        new(HarnessKey.Overlay, new KeyChord(KeyNames.F(2), KeyMods.Ctrl), true, "the diagnostic overlay"),
        new(HarnessKey.OverlaySize, new KeyChord(KeyNames.F(3), KeyMods.Ctrl), true, "the overlay's size"),
        new(HarnessKey.Sheet, new KeyChord(KeyNames.F(5), KeyMods.Ctrl), true, "the character sheet"),
    ];

    /// <summary>A harness key's entry.</summary>
    public static Key Of(HarnessKey what) => All.First(k => k.What == what);

    /// <summary>
    /// Whether a key that came down is the harness's <paramref name="what"/>: its key, its modifiers all held
    /// (Ctrl for the four), not a repeat, and <c>--debug</c> on.
    /// </summary>
    public static bool Is(HarnessKey what, int input, KeyMods held, bool debug, bool repeat)
    {
        var key = Of(what);
        return !repeat && input == key.Chord.Input && (key.Chord.Mods & ~held) == 0 && (debug || !key.DebugOnly);
    }

    /// <summary>Whether a key that came down is any of the harness's (the quick-slot handler leaves those alone).</summary>
    public static bool Any(int input, KeyMods held, bool debug, bool repeat) => All.Any(k => Is(k.What, input, held, debug, repeat));

    /// <summary>
    /// Whether a key gets past a pane that takes the keyboard — a shop or the vault,
    /// whose key hook returns before 1.1's keys — because it is the harness's: Ctrl+F1 to F3 under <c>--debug</c>.
    /// The vault is closed as 1.1 closes it, by Escape (the escape key's vault case); its old F10 is gone.
    /// </summary>
    public static bool PassesPane(int input, KeyMods held, bool debug, bool repeat) =>
        Is(HarnessKey.Where, input, held, debug, repeat) || Is(HarnessKey.Overlay, input, held, debug, repeat)
        || Is(HarnessKey.OverlaySize, input, held, debug, repeat);
}
