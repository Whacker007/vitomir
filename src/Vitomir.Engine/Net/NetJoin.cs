// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the host's half of a join does to the character it makes, and
// the line the others read when a player goes — the parts the Net module's
// A comparison with 1.1 found Vitomir did not do: the teams, the effects a
// character is born with by the type its player chose, a name compared as 1.1 compares it, and the
// players' own words for somebody leaving.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public static class NetJoin
{
    /// <summary>
    /// **The team a player's character is put on** (the join; the host's own session start puts
    /// its character on 0): in a **combative** game — the create screen's Co-op box *unchecked*, the
    /// game's flag 0x100 clear, which the lobby's game pane calls `UI_NET_COMBATIVE` — each
    /// player is their own team, their slot; in a **co-op** game (the box checked, 0x100 set) everybody
    /// is team 0. Both the team and the original team are set.
    /// (<see cref="GameFlags.CoOp"/>.)
    /// </summary>
    public static int TeamOf(int slot, GameFlags flags) => (flags & GameFlags.CoOp) != 0 ? 0 : slot;

    /// <summary>Puts <paramref name="thing"/> on its slot's team, both the team and the original.</summary>
    public static void Teams(WorldThing thing, int slot, GameFlags flags)
    {
        if (thing.Actor is not { } actor) return;
        actor.Team = actor.OriginalTeam = TeamOf(slot, flags);
    }

    /// <summary>
    /// **The effects a joining character is born with, by the type its player chose** (the join's
    /// Every effect off but the worn gear's, then a vampire's or a
    /// **ghoul's** (types 0 and 1) `ef_decreasefrenzy`, a human's (2) `ef_increasemanaot` and
    /// `ef_increasebloodot` (level 0), and the stats recomputed. The character was
    /// made from its template, which gave it the effects of the *template's* type (<see
    /// cref="Location.Born"/>); a player may play a human on a vampire's model, and 1.1 puts the right
    /// ones back here. A monster (3) gets none.
    /// </summary>
    public static void Reborn(WorldThing thing)
    {
        if (thing.Actor is not { } actor) return;
        EffectRuntime.RemoveAll(thing);
        foreach (var born in Rules.GameRules.Current.Healing.Born(actor.Type)) EffectRuntime.Apply(thing, born, 0);
    }

    /// <summary>
    /// Whether two players' names are the same to the join: `strncmp` over 32 characters —
    /// **case and all**, so "anna" may join a game "Anna" is in. 1.1 tests it before the room is full, so a duplicate in a
    /// full game is refused as a duplicate (`UI_NET_JOINDUPLICATE`, 7); Vitomir's knock carries no character (protocol 23), so
    /// the room's size is tested at the knock and the name when the character is chosen in the room.
    /// </summary>
    public static bool SameName(string a, string b) =>
        string.CompareOrdinal(a.Length > NameLength ? a[..NameLength] : a, b.Length > NameLength ? b[..NameLength] : b) == 0;

    /// <summary>How many characters of a name the join compares (`strncmp(…, 0x20)`).</summary>
    public const int NameLength = 32;

    /// <summary>
    /// The line the *other* players read when somebody goes: always `UI_NET_HASLEFT` (the client's kind 10), whichever way they went — the host's own message box says `UI_NET_HASDISCONNECTED`
    /// (its one line for a going: a player who leaves by the menu sends nothing,
    /// and the host hears DirectPlay's player-destroyed), but what it sends is only the slot, and each
    /// machine says "has left" in its own words.
    /// </summary>
    public const string LeftForPlayers = "UI_NET_HASLEFT";

    /// <summary>
    /// The line in the host's own message box and chat pane when a join is refused as a duplicate:
    /// "&lt;name&gt; UI_NET_DUPLICATENAME", the refused name first.
    /// </summary>
    public const string DuplicateName = "UI_NET_DUPLICATENAME";

    /// <summary>
    /// **A join of any size** (ours): the character chosen in the room (<see cref="Msg.Choose"/>; the Hello until protocol 23) carries the kept character whole — its pack, belt,
    /// worn items and vault — however many items that is, the transport sending it in pieces (<see cref="NetPeer"/>'s
    /// <c>Transport.Pieces.cs</c>). 1.1 sends each item by itself after the join's answer (the host making
    /// each one). Only a message past the transport's bound (<see cref="NetPeer.MaxReliableMessage"/>, some twenty
    /// thousand items) is refused here, on the joining machine, with a line on its console: the key the join ends
    /// with (1.1's `UI_NET_DISCONNECTED`), or null when it goes.
    /// </summary>
    public static string? Refused(byte[] hello, NetCharacter me, Action<string>? log)
    {
        if (NetPeer.Fits(hello.Length)) return null;
        log?.Invoke($"net: {me.Name} is too much to join with: {hello.Length:N0} bytes, the most one message may be is {NetPeer.MaxReliableMessage:N0}");
        return "UI_NET_DISCONNECTED";
    }
}
