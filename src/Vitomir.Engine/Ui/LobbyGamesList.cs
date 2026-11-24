// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The lobby's games list — `mpChatGame`, the upper right of `mpChat` — as 1.1 keeps
// its chosen row and whether it can be chosen at all.
// The list is left unselected until a row is clicked, as 1.1 leaves it.
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The games list's chosen row and its modes**.
/// <para>
/// **Nothing is chosen until a row is clicked.** Every redraw of the list — a game heard for the first time or
/// heard again, a game gone stale or gone by name, the WON room's refill
/// — asks the list for its chosen row's index first (−1 with none), clears it, adds a
/// `"%s (%d)"` row for each game, and chooses that index again **only when there was one** (the list's choose behind
/// `-1 < index`). So a list that nobody has clicked stays unchosen however many games come, and the game pane
/// under it stays empty. Earlier, Vitomir chose row 0 at every redraw (ours), so the first game
/// found was described at once.
/// </para>
/// <para>
/// **A chosen game stays chosen.** 1.1's list is in the order the games were heard, a new game added at its end,
/// so the index it keeps is the same game's. A game gone is taken out and the rest close
/// up; the index is chosen again — the game that slid into it, the choice's hook making it
/// the described one — or nothing when it is past the end (no row is found); then the game still described
/// (cleared only when it is the one gone) is chosen again by its own index. So a chosen game that goes
/// hands the choice to the game after it, or to nobody when it was the last. <b>Ours</b>: Vitomir's finder lists
/// the games by name (`LanFinder.Games`), not in the order heard, and hands over the whole list at once; the
/// choice follows the chosen game by its host's address, and "the game after it" is the next in the list shown.
/// </para>
/// <para>
/// **Shown only, or choosable, by mode** (on a change of mode only). In the LAN room (mode 1) and the
/// WON room (3) the list is cleared and made choosable; in a game (2, a
/// player's; 4, the host's) it is the game's chat, cleared and made only shown (which
/// also forgets the hovered and the chosen row, <see cref="MenuList.Inert"/>). The WON room then redraws it
/// from the games it knows; the LAN room waits for the next answer.
/// </para>
/// </summary>
public static class LobbyGamesList
{
    /// <summary>Whether the list is only shown in <paramref name="mode"/> (a game's chat).</summary>
    public static bool InertIn(int mode) => mode is 2 or 4;

    /// <summary>
    /// The row to choose after a redraw from <paramref name="was"/> to <paramref name="now"/>, the list's chosen
    /// row having been <paramref name="current"/> (−1 for none): none stays none; the chosen game, still there, at
    /// its new index; gone, the next game after it in <paramref name="was"/> that is still there, or none.
    /// </summary>
    public static int Keep(IReadOnlyList<FoundGame> was, IReadOnlyList<FoundGame> now, int current)
    {
        if (current < 0 || current >= was.Count) return -1;                  // −1: nothing chosen again
        for (int i = current; i < was.Count; i++)
        {
            int at = IndexOf(now, was[i]);
            if (at >= 0) return at;
        }
        return -1;
    }

    /// <summary>
    /// The list redrawn: cleared, a <see cref="GameInfo.Row"/> for each game with
    /// its index as its data, and the choice kept as <see cref="Keep"/> says — which, in the list's own words,
    /// is the list's choose run only for an index there was.
    /// </summary>
    public static void Redraw(MenuList list, IReadOnlyList<FoundGame> was, IReadOnlyList<FoundGame> now)
    {
        int keep = Keep(was, now, list.CurrentData);
        list.Clear();
        for (int i = 0; i < now.Count; i++) list.Add(now[i].Info.Row, i);
        if (keep >= 0) list.Select(keep);
    }

    /// <summary>A game's place in a list, by its host's address.</summary>
    static int IndexOf(IReadOnlyList<FoundGame> games, FoundGame game)
    {
        for (int i = 0; i < games.Count; i++)
            if (games[i].Host.Equals(game.Host)) return i;
        return -1;
    }
}
