// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The binding layer's focus change and its word on a swallowed key (after a play-test:
// F10 minimized the bar, but after an Alt+Tab it became F11).
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Platform.Modules;

namespace Vitomir;

static partial class KeyInput
{
    /// <summary>
    /// **The window gained or lost the focus: nothing stays held** (ours; <see cref="HeldInputs{T}"/>).
    /// Every input the binding layer holds is let go — its releases handed on, so the swivel stops and a held
    /// click ends as if the hand had let go — the harness's held keys and buttons are dropped, the modifiers
    /// a chord used up and a flag click's flags are forgotten, and the host's own keyboard is reset with its
    /// modifiers (the lock keys' kept): SDL lets go of its keys when the focus goes, but not when it comes
    /// back, and a modifier it missed would ride on every key after. Returns what was let go, by name, for
    /// the console. 1.1 keeps a key down across a deactivation until its next event.
    /// </summary>
    public static List<string> LetGo()
    {
        var names = new List<string>();
        foreach (var (input, made) in down.LetGoAll())
        {
            names.Add(new KeyChord(input).ToString());
            Release(made, null);
        }
        foreach (var input in HarnessHeld)
            if (!names.Contains(new KeyChord(input).ToString())) names.Add(new KeyChord(input).ToString());
        HarnessHeld.Clear();
        consumed = KeyMods.None;
        forced = InputModifier.None;
        modifierAlone = -1;
        ateLeft = false;
        InputTape.Live.ResetKeyboard();
        InputTape.SetMods(InputTape.Mods() & (LoopMods.Caps | LoopMods.Num | LoopMods.Scroll | LoopMods.Mode));
        return names;
    }

    /// <summary>Whether a key event is the harness's <paramref name="what"/> (<see cref="HarnessKeys"/>: Ctrl on the quick items' keys, with <c>--debug</c>).</summary>
    public static bool Harness(in LoopInput e, HarnessKey what) =>
        HarnessKeys.Is(what, Scan(e), Mods(e.Mods), Vitomir.Engine.Diagnostics.Diag.Enabled, e.Repeat);

    /// <summary>Whether a key event is any of the harness's keys (with <c>--debug</c>); the quick-slot handler leaves those alone.</summary>
    public static bool IsHarness(in LoopInput e) =>
        HarnessKeys.Any(Scan(e), Mods(e.Mods), Vitomir.Engine.Diagnostics.Diag.Enabled, e.Repeat);

    /// <summary>Whether a key event gets past a shop's or the vault's hold on the keyboard because it is the harness's (<see cref="HarnessKeys.PassesPane"/>).</summary>
    public static bool HarnessPasses(in LoopInput e) =>
        HarnessKeys.PassesPane(Scan(e), Mods(e.Mods), Vitomir.Engine.Diagnostics.Diag.Enabled, e.Repeat);

    static KeyMods Mods(LoopMods m) =>
        ((m & LoopMods.Ctrl) != 0 ? KeyMods.Ctrl : 0) | ((m & LoopMods.Shift) != 0 ? KeyMods.Shift : 0)
        | ((m & LoopMods.Alt) != 0 ? KeyMods.Alt : 0);

    /// <summary>
    /// One of 1.1's keys came down and its command is no longer on it (ours): said on the console with
    /// where the command went — the one case in which the binding layer eats a key without a word, and the
    /// first thing to rule out when a key seems to have moved. Only a press command's own key.
    /// </summary>
    static void Swallowed(int input, KeyMods held, in LoopInput raw)
    {
        if (raw.Kind != LoopInputKind.KeyDown) return;
        foreach (var info in KeyBindings.All)
        {
            if (info.Kind != CommandKind.Press || !info.Defaults.Any(d => d.Input == input && (d.Mods & ~held) == 0)) continue;
            var now = Bindings[info.Command];
            if (now.Any(b => b.Input == input)) continue;         // still its own: off here for another reason
            Console.WriteLine($"  keys: {new KeyChord(input, held)} does nothing: {info.Name} is "
                              + (now.Count == 0 ? "bound to nothing" : "on " + string.Join(", ", now)));
            return;
        }
    }
}
