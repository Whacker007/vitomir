// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The cast list: who a cast name — a `CASTID` — can find, and in what order.
namespace Vitomir.Engine.Rpg;

using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

/// <summary>
/// The engine's two cast lists, which every lookup by cast name goes through (a
/// conversation's speakers, and <c>GuidFromCastID</c>). The **party's** list is
/// asked first (rebuilt from the roster whenever it changes), then the **place's**
/// (built on entering a place from the world's thing list): a character — thing kind 0 or
/// 7 — with a cast id, **not carrying `THING_AF_PARTY` (`0x20000000`)**, since the party has its own
/// list, and **at most 64 of them** (`0x40`). A second character with a cast id already in a list is not added —
/// the hash insert refuses it — so the first in the world's order wins.
/// <para>
/// **The place's list is made once, on arrival, and never grows** (a comparison with 1.1
/// gap). The scene start makes it before it stands the party and
/// before any script hears <c>beginscene</c>; the add, which would add a later
/// arrival, is never used in 1.1. And each rebuild of the
/// party's list (the arrival's, every conversation start's, a join's, a
/// leave's) takes every member's **name** out of the place's list (the hash's
/// remove), never to be put back. So a character a script spawns after arrival — in its
/// <c>beginscene</c> or later — or one that has joined and then left the party answers to no cast
/// id: no speaker in a conversation (no talk, listen or lip sync; the subtitle is still shown, since
/// the display name is a separate lookup), 0 from <c>GuidFromCastID</c>. Entering the place again
/// or loading a save made there (both go through the level start) makes the list afresh, and then they
/// answer. In 1.1, for the leaving half: a companion that has left is not animated by a
/// conversation. A stranger who happens to share a party member's cast id is taken out with it.
/// </para>
/// <para>
/// The two lists are asked in that order and the place's is the one kept here, per place
/// (<see cref="Arrive"/>); the party's is read live from the roster, which is what 1.1's is after
/// every change. A place whose arrival Vitomir did not mark (the engine's tests, a run without
/// scripts, a player's machine in multiplayer) gets its list at its first lookup (ours). A character
/// that has been destroyed since arrival answers to nobody here, where 1.1's list keeps a pointer to
/// freed memory (ours; no shipped script names one after removing it). No shipped place has anywhere
/// near 64 named characters.
/// </para>
/// </summary>
public static class CastList
{
    /// <summary>How many characters the place's list holds (`< 0x40`).</summary>
    public const int PlaceCapacity = 64;

    /// <summary>Each place's list as the engine left it (ours: 1.1 has one, for the place it is in).</summary>
    static readonly ConditionalWeakTable<Location, PlaceCast> Places = new();

    /// <summary>
    /// A place has been entered: its list made from the things in it now,
    /// and the party's rebuild that follows takes the members' names out.
    /// Called before any script is told <c>beginscene</c>, as 1.1 does, and again every time the
    /// place is entered or a save made in it is loaded.
    /// </summary>
    public static void Arrive(Location place) => Make(place);

    static PlaceCast Make(Location place)
    {
        var made = new PlaceCast(place.Things);
        made.TakeOut(place.PartyMembers());
        Places.AddOrUpdate(place, made);
        return made;
    }

    /// <summary>
    /// The party's list rebuilt (after a join or a leave): every
    /// member's name taken out of the place's list.
    /// </summary>
    public static void PartyRebuilt(Location place) => Kept(place).TakeOut(place.PartyMembers());

    /// <summary>Everybody a cast name can find in <paramref name="place"/>, in the order the lookup looks.</summary>
    public static IEnumerable<WorldThing> Of(Location place)
    {
        // Every change to the roster rebuilds the party's list in 1.1, so a current member's name
        // is always out of the place's; taking them out here too covers the joins Vitomir's own
        // harness makes (`--companions`) without the rebuild (ours).
        var kept = Kept(place);
        var party = place.PartyMembers().ToList();
        kept.TakeOut(party);
        return Party(party).Concat(kept.Answering(place));
    }

    /// <summary>
    /// The two lists as they stand right after an arrival, from the party in roster order and the
    /// world's things in their own order — what the lists are built from.
    /// </summary>
    public static IEnumerable<WorldThing> Of(IEnumerable<WorldThing> partyMembers, IEnumerable<WorldThing> things)
    {
        var party = partyMembers.ToList();
        var here = new PlaceCast(things);
        here.TakeOut(party);
        return Party(party).Concat(here.Answering(null));
    }

    /// <summary>Whoever answers to <paramref name="castId"/>, or null.</summary>
    public static WorldThing? Find(Location place, string castId) =>
        castId.Length == 0 ? null
            : Of(place).FirstOrDefault(t => string.Equals(t.Actor!.CastId, castId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The place's list as 1.1 builds it from a world's things: characters with a cast id, not
    /// in the party, the first of each name, at most <see cref="PlaceCapacity"/>.
    /// </summary>
    public static IEnumerable<WorldThing> PlaceList(IEnumerable<WorldThing> things)
    {
        var here = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in things)
        {
            if (here.Count >= PlaceCapacity) yield break;
            if (t.Actor is not { CastId.Length: > 0 } a || a.Has(Actor.Flag.Party)) continue;
            if (here.Add(a.CastId)) yield return t;
        }
    }

    /// <summary>The party's list: the roster's members with a cast id, the first of each name.</summary>
    static IEnumerable<WorldThing> Party(IEnumerable<WorldThing> members)
    {
        var party = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
            if (member.Actor is { CastId.Length: > 0 } a && party.Add(a.CastId)) yield return member;
    }

    static PlaceCast Kept(Location place) => Places.TryGetValue(place, out var kept) ? kept : Make(place);

    /// <summary>
    /// One place's list (and its hash): made once, and names only ever taken
    /// out of it.
    /// </summary>
    sealed class PlaceCast(IEnumerable<WorldThing> things)
    {
        readonly List<WorldThing> list = [.. PlaceList(things)];
        readonly HashSet<string> takenOut = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Each member's name out of the place's hash, whoever in it had it.</summary>
        public void TakeOut(IEnumerable<WorldThing> members)
        {
            foreach (var m in members)
                if (m.Actor is { CastId.Length: > 0 } a) takenOut.Add(a.CastId);
        }

        /// <summary>
        /// Who in it still answers: a name not taken out, and a character still in this place (ours —
        /// see the class).
        /// </summary>
        public IEnumerable<WorldThing> Answering(Location? place)
        {
            foreach (var t in list)
            {
                if (takenOut.Contains(t.Actor!.CastId) || t.Removed) continue;
                if (place is not null && !ReferenceEquals(t.Place, place)) continue;
                yield return t;
            }
        }
    }
}
