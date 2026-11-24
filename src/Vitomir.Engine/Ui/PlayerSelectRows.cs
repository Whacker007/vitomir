// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The character select's list: what each row says and which row is
// chosen as the screen opens.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The rows of `mpPlayerSelect`'s list**. 1.1 lists every `Crypt\*.npf` whose header
/// reads, numbering them from 1 in the order the folder gives them, each row
/// <c>"%d. %s (%s)"</c>: the number, the character's name (from the header) and its
/// clan's name (which the file's writer copies from the clan record — the
/// clan's name through the string table, so `Brujah`, and `Human` for a human,
/// whom Create gives the `human` clan). A header with no clan leaves the
/// brackets empty. The row chosen is the one whose name is the character in use's, compared without
/// regard to case (`__strcmpi`); with none it is left to the caller (a comparison
/// with 1.1; until then Vitomir listed the bare names and chose the first row when the name missed).
/// </summary>
public static class PlayerSelectRows
{
    /// <summary>The row's text: the number, the name, the clan's name in brackets.</summary>
    /// <param name="number">From 1, counting only the files that read.</param>
    /// <param name="clanName">The clan's name as the string table gives it; empty for none.</param>
    public static string Row(int number, string name, string clanName) => $"{number}. {name} ({clanName})";

    /// <summary>
    /// The clan whose name a character's row shows: a human's is `human`, anybody
    /// else's the one chosen for it, or failing that <paramref name="fallback"/> (the template's, as
    /// the character is made).
    /// </summary>
    public static string ClanKey(NetCharacter c, string? fallback = null) =>
        c.Type == ActorType.Human ? "human" : c.Clan.Length > 0 ? c.Clan : fallback ?? "";

    /// <summary>
    /// The row chosen as the list is made: the first whose name is <paramref name="inUse"/>'s, compared
    /// without regard to case (`__strcmpi`); −1 when nobody is in use or no name matches.
    /// </summary>
    public static int Chosen(IReadOnlyList<NetCharacter> listed, NetCharacter? inUse)
    {
        if (inUse is null) return -1;
        for (int i = 0; i < listed.Count; i++)
            if (string.Equals(listed[i].Name, inUse.Name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
