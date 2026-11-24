// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Joining a game from the lobby (the join's answer),
// read: the checks made before connecting, the password box, and which refusals come back in a box
// and which as a line in the room.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **Join Game**, what `MpMenus` does with the chosen row:
/// <list type="number">
/// <item>the game's protocol is compared with this machine's (against 0x10) — a
/// stranger's is refused with `UI_NET_BADPROTOCOL` in a `UI_NET_CONNECTIONERROR` box;</item>
/// <item>the game's chronicle is looked for in this install (which opens the
/// chronicle's file) — one not found is `UI_NET_BADCHRON` in the same box;</item>
/// <item>a game flagged 0x4 (<see cref="GameFlags.Password"/>) asks `UI_NET_ENTERGAMEPASSWORD` in an input
/// box whose field shows stars (the edit's star bit); its OK joins with what was typed
/// and its Cancel does nothing;</item>
/// <item>any other game is joined at once, with no password.</item>
/// </list>
/// A join refused by the session itself — DirectPlay's own answer — comes back **in an error box**
/// (<see cref="Box"/>): the room full (`UI_NET_NONEWPLAYERS` under `UI_NET_CONNECTIONERROR`), a
/// wrong password (`UI_NET_WRONGGAMEPASSWORD` under `UI_NET_JOINGAMEERROR`), anything else
/// (`UI_NET_BADADDR` under `UI_NET_CONNECTIONERROR`). A join the host's game refuses in its answer
/// `JOINFULL`, `JOINBANNED`, `JOINREJECTED`, `JOINCLOSED`, `JOINDUPLICATE`, `BADDATA`,
/// `BADTEMPLATE`, else `JOINFAILED`) is **a line in the room's list** (`mpChatRoom` in every
/// mode) — not a box.
/// </summary>
public static class LobbyJoin
{
    /// <summary>What the password box takes (ours: the create screen's password field takes 32).</summary>
    public const int PasswordLimit = 32;

    /// <summary>
    /// The checks made before connecting, in its order: the protocol, then the chronicle. The
    /// string key of the refusal (under `UI_NET_CONNECTIONERROR`), or null when the game may be joined.
    /// </summary>
    public static string? Check(GameInfo game, ChronicleSet chronicles) =>
        game.ProtocolVersion != Protocol.Version ? "UI_NET_BADPROTOCOL"
        : chronicles[game.Chronicle] is null ? "UI_NET_BADCHRON"
        : null;

    /// <summary>Whether Join Game asks for the password first (the record's flag 4).</summary>
    public static bool AsksPassword(GameInfo game) => (game.Flags & GameFlags.Password) != 0;

    /// <summary>
    /// The error box a refused join is shown in, as its title's and text's string keys; null for a
    /// refusal the game's answer gives, which is a line in the room. Vitomir's session has no
    /// DirectPlay under it: the host's own checks stand in — its password refusal is DirectPlay's
    /// `DPERR_INVALIDPASSWORD` (0x88770154 → −4), a connection that never came up is the "anything else", and
    /// its protocol refusal (reached only by the address box, ours) is shown as 1.1 shows one.
    /// </summary>
    public static (string Title, string Text)? Box(string? reason) => reason switch
    {
        "UI_NET_WRONGGAMEPASSWORD" => ("UI_NET_JOINGAMEERROR", "UI_NET_WRONGGAMEPASSWORD"),
        "UI_NET_NONEWPLAYERS" => ("UI_NET_CONNECTIONERROR", "UI_NET_NONEWPLAYERS"),
        "UI_NET_BADADDR" => ("UI_NET_CONNECTIONERROR", "UI_NET_BADADDR"),
        "UI_NET_BADPROTOCOL" => ("UI_NET_CONNECTIONERROR", "UI_NET_BADPROTOCOL"),
        _ => null,
    };
}
