// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Storyteller's scene index on a host (the scene pane's index and count, and the two script natives
// that read and write the first). Earlier, the natives answered −1 and did nothing on a host too, and the
// scene pane kept its own step, which a script never moved and a place change put back to −1.
namespace Vitomir.Engine.Net;

/// <summary>
/// **The step of the chronicle's `.nss` the story has reached**, with the number of steps
/// listed, one pair for the whole game — the host's, since only a host has the scene pane:
/// <list type="bullet">
/// <item>**Reading the list** (called by the chronicle's metafile reader for its `scene` line)
/// sets the count and puts the index back to −1 (<see cref="Read"/>); so does the pane's reset.</item>
/// <item>**The scripts** move it: `SetNSSIndex(i)` on a host (net mode 2) when
/// 0 ≤ i &lt; the count, and nothing otherwise; `GetNSSIndex()` answers it on a host and −1 anywhere
/// else. The two multiplayer chronicles call `SetNSSIndex` as their steps happen (`MP_CTDChronicle` 0 to 2,
/// `MP_LOTChronicle` 0 to 7).</item>
/// <item>**The scene pane** moves it too: Advance one on, as far as the slot past the list — so it can
/// equal the count — and Skip To to the row chosen.</item>
/// <item>**The pane greys** every row at or before it (the pane's show hook: a row is drawn plainly only when the
/// index is below it, `index < i`), and those rows cannot be chosen (<see cref="Greyed"/>).</item>
/// </list>
/// A player's machine and single player have no scene pane and answer −1 (`CodexScenes.Progress.cs`).
/// </summary>
public sealed class SceneIndex
{
    /// <summary>The index before anything is reached (`0xffffffff`).</summary>
    public const int None = -1;

    /// <summary>The steps listed (at most 64).</summary>
    public int Count { get; private set; }

    /// <summary>The step reached, −1 for none; the pane's buttons may take it to <see cref="Count"/>.</summary>
    public int Reached { get; set; } = None;

    /// <summary>The chronicle whose list was read — ours: the pane is made again on every place change, and reads its list again.</summary>
    string? chronicle;

    /// <summary>
    /// The chronicle's list read: the count, and the index back to −1 — once per chronicle. **Ours**: 1.1
    /// reads the list as it reads the chronicle; Vitomir's pane reads it each time the Storyteller is made
    /// (every place change of the host's), so the same chronicle's list read again keeps the index.
    /// </summary>
    public void Read(string chronicleName, int count)
    {
        if (chronicle == chronicleName && Count == count) return;
        chronicle = chronicleName;
        Count = count;
        Reached = None;
    }

    /// <summary>`SetNSSIndex(index)` on a host: taken only when 0 ≤ index &lt; the count (unsigned compare).</summary>
    public void Set(int index)
    {
        if (index >= 0 && index < Count) Reached = index;
    }

    /// <summary>Whether the scene pane draws step <paramref name="row"/> grey and out of the choosing (`!(index &lt; row)`).</summary>
    public bool Greyed(int row) => row <= Reached;
}
