// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quick save and quick load (ours). 1.1 has the save and load screens
// and the autosave, and no key for either.
namespace Vitomir.Engine.Ui;

/// <summary>What a quick save or quick load press does.</summary>
public enum QuickAct
{
    /// <summary>Nothing: saving (or loading) is not to be had here — a cutscene, a conversation, a network game.</summary>
    Nothing,
    /// <summary>Save into the remembered slot, with no question.</summary>
    Save,
    /// <summary>No slot yet: the save screen, to pick one.</summary>
    SaveScreen,
    /// <summary>Load the remembered slot — with no question unless the Gameplay screen's confirm quick load is on (<see cref="QuickLoadConfirm"/>).</summary>
    Load,
    /// <summary>No slot yet: the load screen, to pick one.</summary>
    LoadScreen,
}

/// <summary>
/// **The quick save and quick load** (ours): they use the last slot the player saved to or loaded,
/// and if none has been used yet in this session (since the load), they open the save or load screen
/// so the player can pick. The keys are <see cref="KeyCommand.QuickSave"/> and
/// <see cref="KeyCommand.QuickLoad"/>, the keypad's + and − by default.
/// <list type="bullet">
/// <item>**The slot** is the one the player last saved to or loaded from, on the save and load screens or
/// by these keys, **since the program started** — kept for the process, so it outlives a load (which is a
/// new run of the program's body) — **until a new game**, which forgets it, so a
/// fresh play-through's first quick save asks for its slot rather than going into the last one's. Single player and a network
/// game's host keep one each, as their saves are in different folders. The autosave (slot 0) is never it:
/// it cannot be saved into, so loading it leaves the slot as it was.</item>
/// <item>**The quick save goes wherever the escape menu's Save would**: it is refused wherever the escape
/// menu is refused (a cutscene, a camera path, the first-person look, a conversation, the advancement
/// screen, a shop, a document, a vault in the game camera) and on a network game's
/// player, whose Save is greyed (<see cref="NetEscape"/>). It posts "Game saved." before the save
/// is written, as the save screen does, and names the save as the name box would have been
/// filled — the place and the time played.</item>
/// <item>**The quick load asks nothing** by default; the Gameplay screen's <i>confirm quick load</i> (off by
/// default, <see cref="QuickLoadConfirm"/>) makes it ask OK or Cancel first. It is refused where the quick save is and
/// in any network game, which has no Load (<c>escapeMenuMP</c>). A remembered slot whose save has gone
/// opens the load screen instead.</item>
/// </list>
/// </summary>
public static class QuickSave
{
    static readonly int?[] last = new int?[2];

    /// <summary>The slot remembered for single player or a network game's host; null when there is none yet.</summary>
    public static int? Slot(bool network) => last[network ? 1 : 0];

    /// <summary>
    /// A request the menus made (<see cref="FrontEnd.Ask"/>): a save or a load of a slot of ours — not the
    /// autosave, not an import of a 1.1 save — is the slot from now on.
    /// </summary>
    public static void Asked(MenuRequest request, int slot, bool network)
    {
        if (request is MenuRequest.Save or MenuRequest.Restore && slot > 0) last[network ? 1 : 0] = slot;
        // A new game forgets the single-player slot (the): the first quick save of a fresh
        // play-through would otherwise go, unasked, into the last play-through's slot.
        else if (request is MenuRequest.NewGame) last[0] = null;
    }

    /// <summary>Forgets both slots: the program's start, and a test's.</summary>
    public static void Forget() => Array.Clear(last);

    /// <summary>
    /// What a press does. <paramref name="escapeMenuOpens"/>: the escape menu would open now (the host's
    /// test for Escape); <paramref name="network"/>, <paramref name="host"/>: a network game, and whether
    /// this machine hosts it; <paramref name="exists"/>: whether a slot has a save (for the quick load).
    /// </summary>
    public static QuickAct Decide(KeyCommand command, bool escapeMenuOpens, bool network, bool host, Func<int, bool> exists)
    {
        if (!escapeMenuOpens) return QuickAct.Nothing;
        var slot = Slot(network);
        switch (command)
        {
            case KeyCommand.QuickSave:
                if (network && !host) return QuickAct.Nothing;
                return slot is not null ? QuickAct.Save : QuickAct.SaveScreen;
            case KeyCommand.QuickLoad:
                if (network) return QuickAct.Nothing;
                return slot is { } s && exists(s) ? QuickAct.Load : QuickAct.LoadScreen;
            default:
                return QuickAct.Nothing;
        }
    }
}
