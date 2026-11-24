// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The lobby's game pane — `mpChatGameStatus`, the lower right of `mpChat` (given
// it by the lobby's modes) — as 1.1 draws it, and (ours) the game's players in the room's players' box. It is cleared
// and drawn afresh each time: in a game, whether the game is forming or under way and what it is; in the join screen, what the games list's
// chosen row is (the list's select hook, outside a game, makes the row's
// game the chosen one, and redraws). 1.1 redraws it on every change it
// knows of — a join answered, a game made or left, a player
// come or gone, a chosen game gone stale, the lobby closed; `MpMenus`
// draws it from the state each frame instead and rewrites the list only when a line differs.
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Ui;

/// <summary>The lines of the lobby's game pane.</summary>
public static class LobbyGamePane
{
    /// <summary>
    /// In a game's room: `UI_NET_FORMING` while the game is forming (stage 1, from the session's making until the place is
    /// loaded in a started game, which sets 2), else `UI_NET_INPROGRESS` — 1.1's first line — and then (ours) what the game is,
    /// as the join screen describes a chosen game (<see cref="Chosen"/>): its name, chronicle and options, which the host's Game
    /// Setup may change. 1.1's pane lists the players here; ours lists them in the room's players' box (<see cref="Players"/>).
    /// </summary>
    public static List<string> InGame(bool forming, GameInfo? game, Func<string, string> text) =>
        [text(forming ? "UI_NET_FORMING" : "UI_NET_INPROGRESS"), .. Chosen(game, text)];

    /// <summary>
    /// The game's players (ours, in the room's players' box): `UI_NET_PLAYERSINGAME`, then every slot in use of the eight as
    /// 1.1's game pane writes one, `"%s %d : %s"` — `UI_NET_PLAYER`, the slot counted from 1, the name — the name being the
    /// character's, or <see cref="MpSetup.Choosing"/> while its player has none, and <see cref="MpSetup.NoChronicle"/> after a
    /// player whose install lacks the game's chronicle.
    /// </summary>
    public static List<string> Players(IEnumerable<(int Slot, string Name, bool Missing)> players, Func<string, string> text)
    {
        var lines = new List<string> { text("UI_NET_PLAYERSINGAME") };
        foreach (var (slot, name, missing) in players.OrderBy(p => p.Slot))
            lines.Add($"{text("UI_NET_PLAYER")} {slot + 1} : {(name.Length > 0 ? name : MpSetup.Choosing)}{(missing ? " " + MpSetup.NoChronicle : "")}");
        return lines;
    }

    /// <summary>
    /// In the room: the chosen game, or nothing when none is. `"%s %s"` — `UI_NET_GAMENAME`
    /// ("Game name:") and the game's name; `"%s: %s"` — `UI_NET_CHRONICLE` and the chronicle's name looked
    /// up in the strings (the announcement carries the key); and the options, when there
    /// are any (<see cref="Options"/>). 1.1's last line, `UI_NET_HOST` and the host's name found in the
    /// room's player list, is drawn only in WON mode (the lobby's `+0 == 2`) — gone with WON.
    /// </summary>
    public static List<string> Chosen(GameInfo? game, Func<string, string> text)
    {
        if (game is null) return [];
        var lines = new List<string>
        {
            $"{text("UI_NET_GAMENAME")} {game.Name}",
            $"{text("UI_NET_CHRONICLE")}: {text(game.Chronicle)}",
        };
        string options = Options(game.Flags, text);
        if (options.Length > 0) lines.Add($"{text("UI_MPCREATEGAME_OPTIONS")}: {options}");
        return lines;
    }

    /// <summary>
    /// The options line's list: each `"%s "` in turn — `UI_MPCREATEGAME_PW` for a password
    /// (0x4), `UI_NET_STORYTELLER` for Storyteller moderation (0x400), `UI_NET_CLOSED` for the Game Lock
    /// (0x8), and `UI_NET_COMBATIVE` when <b>0x100 is clear</b> (the create screen's Co-op box, which 1.1
    /// reads set as co-op; `NetJoin.TeamOf`) — the trailing space kept, as 1.1 draws it.
    /// </summary>
    public static string Options(GameFlags flags, Func<string, string> text)
    {
        var s = new System.Text.StringBuilder();
        if ((flags & GameFlags.Password) != 0) s.Append(text("UI_MPCREATEGAME_PW")).Append(' ');
        if ((flags & GameFlags.Moderated) != 0) s.Append(text("UI_NET_STORYTELLER")).Append(' ');
        if ((flags & GameFlags.Locked) != 0) s.Append(text("UI_NET_CLOSED")).Append(' ');
        if ((flags & GameFlags.CoOp) == 0) s.Append(text("UI_NET_COMBATIVE")).Append(' ');
        return s.ToString();
    }

    /// <summary>
    /// A games-list row chosen: a row from 0 to the count makes that game the chosen one, any
    /// other none. (1.1 lets the row equal to the count through — `param_2 &lt;= count` — and so takes
    /// the empty place after the last game, which is no game; the same here.)
    /// </summary>
    public static FoundGame? Choose(IReadOnlyList<FoundGame> games, int row) =>
        row >= 0 && row < games.Count ? games[row] : null;
}
