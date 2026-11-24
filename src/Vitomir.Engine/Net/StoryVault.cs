// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The vault in the Storyteller's seat: the vault's opening leaves the seat before it loads the pane.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The haven's vault on the host** (the script native `Vault`). Past the network
/// arm — a host hands a character that is not the control's to its player's machine (<see cref="Hand"/>) —
/// the pane opens only when it is not up and the control is a player's character (type 7,
/// <see cref="Ui.InventoryPane.VaultMayOpen"/>); then the game's own panes close, **the
/// Storyteller's seat is left** (the multiplayer bar back, the target the one
/// steered) and only then is `ui/vaultPane.nui` loaded. So the Storyteller who reaches the vault
/// with the host's own character steered finds himself out of the seat, the bar his player's; with the
/// ghost steered (type 0) nothing opens and the seat stays. Vitomir opened the vault over the seat.
/// </summary>
public static class StoryVault
{
    /// <summary>
    /// Opens the vault as 1.1 does: false when it may not (<see cref="Ui.InventoryPane.VaultMayOpen"/>);
    /// otherwise, in the <paramref name="seat"/>, <paramref name="leaveSeat"/> first, then
    /// <paramref name="show"/> (the pane).
    /// </summary>
    public static bool Open(WorldThing? steered, bool vaultUp, bool seat, Action leaveSeat, Action show)
    {
        if (!Ui.InventoryPane.VaultMayOpen(steered, vaultUp)) return false;
        if (seat) leaveSeat();
        show();
        return true;
    }

    /// <summary>
    /// **The vault's network arm**, with the character the script named (`Vault(clickerGuid)`,
    /// <paramref name="guid"/>): on a **player's machine** a script's call does nothing (mode 1 with no
    /// message's flag — only the host's message opens it there, <see cref="Asked"/>). On the **host**, a character
    /// that is not the control's is looked for among the slots' characters (the slot table's
    /// None, or slot 0 — the host's own — does nothing; another player's has
    /// **message 0x47 sent to that player alone, reliably** (<see cref="Msg.Vault"/>),
    /// and the host opens nothing. True when the arm has done with the call (the pane is not the host's to
    /// open); false when the pane is this machine's (<see cref="Open"/> follows) — single player, or the
    /// control is the character named.
    /// </summary>
    public static bool Hand(GameServer? server, bool client, int guid, WorldThing? control)
    {
        if (client) return true;                                                 // Mode 1, flag 0
        if (server is null || (control?.Guid ?? 0) == guid) return false;        // The control's own
        for (int slot = 0; slot < server.Slots.Count; slot++)
        {
            if (server.Slots[slot] is not { Avatar: { } body } p || body.Guid != guid) continue;
            if (slot >= 1) server.SendVault(p);                                  // 0x47 to that slot
            return true;
        }
        return true;                                                             // nobody's: nothing
    }

    /// <summary>
    /// **A player's machine told to open it** (message 0x47): the pane as <see cref="Open"/> has
    /// it — only when it is not up and the control is a player's character (type 7) — never in the seat (a player's
    /// machine has none). What is then moved in and out of the vault there is the host's to move: each gesture asks
    /// the host (message 0x16, <see cref="VaultMoves"/>).
    /// </summary>
    public static bool Asked(WorldThing? control, bool vaultUp, Action show) =>
        Open(control, vaultUp, seat: false, () => { }, show);
}
