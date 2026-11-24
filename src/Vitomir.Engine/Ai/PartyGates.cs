// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two tests that keep the party think to the party: the dispatcher's and the party
// think's own. And a note on the dispatcher's place-flag gate, which is unbuilt.
namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **Mode 1 runs the party think only for a party member**: the dispatcher's case 1
    /// tests the actor's `THING_AF_PARTY` (`0x20000000` in the actor's flags) and, without it, breaks out
    /// of the switch with nothing done and nothing rescheduled — so the mind asks again every frame
    /// and gets nothing. A script that puts somebody who is not in the party into mode 1 leaves him
    /// standing. Earlier, Vitomir ran the party think for anybody in mode 1 (a comparison with 1.1,
    ///
    /// </summary>
    static bool PartyFlagged(Mind mind) => mind.Thing.Actor?.Has(World.Actor.Flag.Party) == true;

    /// <summary>
    /// **The rest of the party think is a player-type's**: after the combat decision the
    /// chain — the Attack-stance hunt, the follow, the nurse — runs only when the thing's type is 7
    /// (`thing+4`). A party-flagged character of type 0 fights what it is handed and nothing more.
    /// Every companion the game makes is a player-type thing (its template's type), so this matters
    /// only to a script that flags an ordinary character into the party. Built.
    /// </summary>
    static bool PartyChain(Mind mind) => mind.Thing.Actor?.IsPlayerThing == true;

    // **The place-flag gate, not built.** The dispatcher runs a handler only when the first
    // byte of the actor's place (the loaded world the location record
    // keeps) has no `0x2`. What sets that byte was looked for and not found: the place
    // object is not the location record `SetLocationFlags`/`ClearLocationFlags` write (the
    // `LOCATION_FLAG_*` word); a search of the whole original program for an `or`/`and` of `0x2` on an object's first word, in place or through a
    // register, finds nothing that is this object (the one hit, is the camera's); and the
    // only readers of the byte are this gate and the walker's stop (`& 5`). No script
    // native names it. A whole-word store or a copy could still set it unseen, so this is unread,
    // not disproved; until something is found Vitomir has no gate.
}
