// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What each held key or button was handed on as (ours): the binding layer's memory, and the letting go
// of it on a focus change.
namespace Vitomir.Engine.Ui;

/// <summary>
/// The inputs held down now, each with what the binding layer handed it on as (ours: the host's
/// <c>KeyInput</c> keeps SDL events here), so its release and its repeats are handed on as the same.
/// <para>
/// **A focus change lets go of all of them**: <see cref="LetGoAll"/> gives back everything held, in
/// the order it went down, for the host to release, and forgets it. SDL does the same to its own
/// keyboard when the window loses the focus (<c>SDL_ResetKeyboard</c>: a key-up for every key down), but
/// those key-ups can miss the binding layer — a menu up takes them as they come — and a harness's held
/// keys are not SDL's at all; without this a key held when the focus went stayed held for the binding
/// layer, the swivel and the free camera until it was pressed and let go again.
/// </para>
/// <para>
/// 1.1 is not like either: it reads the keyboard through DirectInput's buffer (256 events a
/// frame into its key-state array), and on WM_ACTIVATE only takes the devices back
/// Acquire on both) — it clears nothing, so a key down when the window went inactive stays
/// down in its array until that key's next event comes through the buffer. Vitomir's keyboard has been
/// SDL's since the first build, which lets go on a focus loss; this keeps the binding layer in step with it.
/// </para>
/// </summary>
public sealed class HeldInputs<T>
{
    readonly List<(int Input, List<T> Made)> held = [];

    /// <summary>How many inputs are held.</summary>
    public int Count => held.Count;

    /// <summary>The inputs held, in the order they went down.</summary>
    public IEnumerable<int> Inputs => held.Select(h => h.Input);

    /// <summary>An input went down and was handed on as <paramref name="made"/> (replacing what it had, if it was held).</summary>
    public void Press(int input, List<T> made)
    {
        held.RemoveAll(h => h.Input == input);
        held.Add((input, made));
    }

    /// <summary>What a held input was handed on as; false when it is not held.</summary>
    public bool TryGet(int input, out List<T> made)
    {
        foreach (var h in held)
            if (h.Input == input) { made = h.Made; return true; }
        made = [];
        return false;
    }

    /// <summary>An input came up: what it was handed on as, and it is no longer held; false when it was not held.</summary>
    public bool Lift(int input, out List<T> made)
    {
        int i = held.FindIndex(h => h.Input == input);
        if (i < 0) { made = []; return false; }
        made = held[i].Made;
        held.RemoveAt(i);
        return true;
    }

    /// <summary>Everything held, in the order it went down, and nothing held after (a focus change).</summary>
    public List<(int Input, List<T> Made)> LetGoAll()
    {
        var all = new List<(int, List<T>)>(held);
        held.Clear();
        return all;
    }
}
