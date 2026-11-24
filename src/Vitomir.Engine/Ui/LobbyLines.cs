// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two lines the lobby's session writes into the lobby's own chat on making or joining a game, read
// against the lobby's modes — one of them 1.1 never shows.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **What the lobby says on hosting and on joining**. Both lines go to the lobby's
/// chat output, and only when it is set — which the modes decide: in the LAN
/// room (mode 1) and the WON room (3) it is null, and in a game (2, 4) it is the games
/// list turned into the game's chat (`mpChatGame`). The mode follows the session's state a frame late, from the
/// lobby's frame hook (not in the lobby, mode 2 — 4 for a WON game).
/// <list type="bullet">
/// <item>**"You are hosting the game 'name'."** (`UI_NET_YOUAREHOSTING` in `"%s '%s'."`) is
/// written by Create Game's OK (and Load Saved's) while the lobby is still in
/// the room, so the chat output is null and **the line is never shown** — 1.1's own dead line. Vitomir put a bare
/// `UI_NET_YOUAREHOSTING` in the game's chat after the mode had changed until then, which took it out; **this puts it
/// back, as ours** (*"No let's keep it. Log messages are good to keep."*), in 1.1's own words and format
/// (<see cref="Hosting"/>) — shown where 1.1's would have been had the output been set.</item>
/// <item>**"You have joined the game name"** (`UI_NET_YOUJOINEDGAME` in `"%s %s"`, the game's name) is
/// written when the host's answer to a join arrives — by then the Join click
/// has moved the session out of the room and the frame hook has made the list the game's chat, so **it is
/// shown**. Vitomir did not write it.</item>
/// </list>
/// </summary>
public static class LobbyLines
{
    /// <summary>
    /// The hosting line (`"%s '%s'."` of `UI_NET_YOUAREHOSTING` and the game's name) —
    /// **ours to show**: 1.1 writes it into the room's null chat output and it never appears; Vitomir's lobby puts it
    /// in the game's chat as the lobby goes to the game.
    /// </summary>
    public static string Hosting(string game, Func<string, string> text) => $"{text("UI_NET_YOUAREHOSTING")} '{game}'.";

    /// <summary>The joined line (the join's answer, case 0: `"%s %s"` of `UI_NET_YOUJOINEDGAME` and the game's name).</summary>
    public static string Joined(string game, Func<string, string> text) => $"{text("UI_NET_YOUJOINEDGAME")} {game}";
}
