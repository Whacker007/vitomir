// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The lobby's Start in a game, read: lit for the host, and for a player once the game is under way.
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The lobby's Start button in a game**. The lobby's frame hook greys it unless this machine is the
/// host (network mode 2) or the session's stage (<see cref="SessionStage"/>) is
/// <see cref="SessionStage.UnderWay"/> — which on a player's machine it is once the host's place is loaded in a started
/// game (message 0x33). The host's Start starts the game; a
/// player's asks to be let into it (network mode 1: message 0x40, answered with
/// the place to load). So in 1.1 **every player comes into the game by pressing Start** after the host's place is up —
/// those who were in the room when the host started and those who join later alike; nothing takes a player in by
/// itself. Earlier, Vitomir's Start was the host's alone and a player went in as soon as the host started or, joining
/// a game under way, at once.
/// </summary>
public static class LobbyStart
{
    /// <summary>
    /// Whether Start is lit: for the host, and for a player in the game's room once the game is under way — and (ours) only
    /// when <paramref name="ready"/>: for the host, every player has a character it accepted and the chronicle
    /// (<see cref="GameServer.ReadyToStart"/>); for a player, its own character accepted and the chronicle here
    /// (<see cref="GameClient.CanStart"/>).
    /// </summary>
    public static bool Lit(bool hosting, byte stage, bool ready) => ready && (hosting || stage == SessionStage.UnderWay);

    /// <summary>The Start button of the lobby's pane greyed or given back, touched only when that changes so a hovered or pressed button keeps its look.</summary>
    public static void Show(MenuPane pane, bool lit)
    {
        const string start = "mpChatStartButton";
        if (pane.Buttons.TryGetValue(start, out var now) && (now == ButtonState.Greyed) != !lit) pane.Grey(start, !lit);
    }
}
