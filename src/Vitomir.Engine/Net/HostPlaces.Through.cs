// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A character nobody plays going through a teleporter's end on a host: the mover's last branch with entrance −2.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed partial class HostPlaces
{
    /// <summary>
    /// **A character nobody plays through a teleporter's end** (the teleporter's network branch → the mover with entrance
    /// −2, the slot and the way). 1.1's network branch sends whoever used the end — any character of type 0 or 7
    /// — through the mover, and the teleporter's use answers 1 whatever the mover then does: the
    /// chronicle's own journey is never taken in a network game. The mover's last branch moves it
    /// only when it is **the one being steered** (the control: in the Storyteller's seat, the possessed body), and
    /// then: the end's place is loaded; the end its way leads to (way 1 the far end,
    /// way 0 the near end) looked up and, if it is there and in a world, the
    /// character leaves its place — its place's machines told, out of the world — and is put
    /// **beside the end** (flags 3, as the party is), grounded, its new place's machines told,
    /// its scripts hearing `beginscene(guid, 0)` and its mind attached there.
    /// **Not** on this path, unlike the entrance's: the camera going with it (that is the entrance arm's
    /// alone) — the Storyteller's view stays where it is — and nothing is cancelled or shut: the pair
    /// stays open (its closing is the players' branch's). A character not being steered, or an end
    /// gone, goes nowhere. **Ours**: in another place it is made again there, as <see cref="Carry"/> does (a new
    /// GUID, its sheet, pack, effects and mind carried), and a possessed body stays in the Storyteller's hand
    /// there (<see cref="StoryHand.Take"/>; 1.1's control is one for the game and stays on it); in the same
    /// place it is simply moved, and the snapshot shows it there.
    /// </summary>
    public bool CarryThrough(WorldThing who, int index, int slot, int way)
    {
        if (who.Place is not { } from || !ReferenceEquals(who, from.Steered)) return false;
        if (Chronicle?.At(index) is not { LevelName.Length: > 0 } || Load is null) return false;
        if (Load(index, 0) is not { } to) return false;                                                 // loaded, or the one already loaded
        if (to.Session?.Teleports.EndOf(to, slot, way) is not { InWorld: true } end) return false;
        Adopt(to, index);
        WorldThing made;
        if (ReferenceEquals(to, from)) made = who;
        else
        {
            WorldThing? again;
            using (CharacterSheets.Moving()) again = to.Place(who.Template, end.Position, who.Orientation, end.Sector);   // moved, not made: no sheet
            if (again is null) return false;
            made = again;
            MoverMinds.Carried(who, made);                          // the mind as it was
            from.Remove(who, drop: false);
            Campaign.Carry(who, made, to);
            Carried = made;
        }
        to.PlaceNear(made, end, 0f, ThroughFlags);                  // beside the end, flags 3
        made.Unsurfaced = false;
        to.WalkerFor(made)?.Ground();
        if (ReferenceEquals(server.Possessed, who) && !ReferenceEquals(made, who)) StoryHand.Take(server, made);
        server.Note($"net: {who.Template} (thing {who.Guid}) went through teleporter slot {slot} to {Chronicle!.NameOf(index)} as thing {made.Guid}");
        if (!ReferenceEquals(made, who)) Within(to, () => Came?.Invoke(to, made));
        return true;
    }

    /// <summary>The placing's flags: into the anchor's world (1), no answer wanted (2).</summary>
    const int ThroughFlags = 3;
}
