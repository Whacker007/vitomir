// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Ai;

/// <summary>
/// **The AI main flags are one word for the whole game**, and this reads where the
/// party-AI options go into it. Vitomir keeps the word on each place's <see cref="Minds.MainFlags"/>;
/// this is what that copy starts at.
///
/// <list type="bullet">
/// <item>**At start-up** the options are read and folded in at once: the two
/// quick-slot filters set when `UseQuickDisc` / `UseQuickItems` are on, the four no-party-cast bits
/// set when `CastHealing` / `CastBuff` / `CastDamage` / `CastSpecial` are off
/// (<see cref="GameOptions.FoldInto"/>).</item>
/// <item>**A load tears the game down first**, and the teardown writes the word
/// to **zero** — and nothing folds the options in again: the only other caller of the fold
/// is the Controls page's apply. So in 1.1, after loading a save, the party casts every
/// kind of discipline and takes any item from the pack **whatever the options say**, until the
/// Controls page is opened and applied. **Quitting to the main menu tears down the same way**
/// (with the game running), so a new game begun from the menu after a
/// quit starts from zero too, whatever the boxes say — unless the Controls page is applied at the menu
/// first, which folds the options into the zero (<see cref="Applied"/>). Only the first new game of a
/// run gets the start-up fold. Vitomir keeps the word between games here (<see cref="TornDown"/>).
/// **Repaired** (`keepcontrols`): Vitomir folds the options back in after every teardown, so
/// the boxes hold after a load and a quit; <see cref="AtStartAsShipped"/> is 1.1's own word.</item>
/// <item>**Travel leaves the word alone**: nothing on the way from one place to the next writes it,
/// so a script's `AISetMainFlags` bit — `AIOff`'s pause among them — goes with the party into the
/// next place, and so do the options. (Vitomir folded the options in afresh at every door until then,
/// which dropped a script's bits and put the options back after a load.)</item>
/// </list>
/// </summary>
public static class MainFlagsWord
{
    /// <summary>
    /// The word between games (the global word with no game running): null while it is still the start-up
    /// fold of whatever the options are, a number once a teardown has written it.
    /// One for the process, as 1.1's is: Vitomir's quit to the menu is a new run in the same process.
    /// </summary>
    static uint? between;

    /// <summary>
    /// The word a game's first place starts with: the start-up fold for the first game of a run, or
    /// whatever the last teardown left (zero) — so after a load, and after a quit to the menu,
    /// zero — with any Controls apply at the menu since folded in (<see cref="Applied"/>).
    /// </summary>
    public static uint AtStart(GameOptions options, bool loaded)
    {
        if (loaded) between = 0u;
        // Repair `keepcontrols`: 1.1 starts from the teardown's bare zero, so after
        // every load and every quit the party ignores the Control Options until the Controls page is
        // applied again. Vitomir folds them back into whatever the teardown left, always.
        return options.FoldInto(between ?? 0u);
    }

    /// <summary>
    /// The word 1.1 itself would start the game with — the teardown's zero with nothing folded in — kept
    /// so the register can say exactly what the repair changes.
    /// </summary>
    public static uint AtStartAsShipped(GameOptions options, bool loaded)
        => loaded ? 0u : between ?? options.FoldInto(0u);

    /// <summary>
    /// A quit to the main menu with a game running: the teardown writes the word to zero
    /// (the word set to 0).
    /// </summary>
    public static void TornDown() => between = 0u;

    /// <summary>
    /// The Controls page applied: the options folded into the word. In a game it
    /// is that place's word (the options host folds it); at the menu it is the word between games.
    /// </summary>
    public static void Applied(GameOptions options)
    {
        if (between is { } word) between = options.FoldInto(word);
    }

    /// <summary>A fresh process's word (the tests' reset).</summary>
    public static void Reset() => between = null;

    /// <summary>The word the next place starts with: the one the last place ended with.</summary>
    public static uint Across(uint word) => word;
}
