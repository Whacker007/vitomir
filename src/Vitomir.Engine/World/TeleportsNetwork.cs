// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The teleporters' multiplayer parts, on a
// host with players in different places (`Net.HostPlaces`).
using Vitomir.Engine.Save;

namespace Vitomir.Engine.World;

/// <summary>
/// What the teleporters ask of a network game's host (`Net.HostPlaces` answers). A player's machine
/// makes no pair and moves nobody (nothing on a client) — its scripts do not run — so these
/// are the host's alone.
/// </summary>
public interface ITeleportNetwork
{
    /// <summary>The chronicle location a place is (the thing's world's) — a pair's near end is in its creator's place, which on a host need not be the chronicle's current one.</summary>
    int IndexOf(Location place);

    /// <summary>
    /// The creator's number among the players: its slot + 1 when it is a player's
    /// character in a network game — the host's own is 1 — and 1 otherwise. It picks the far end's
    /// start among the portal entrance's numbered ones, so each player's portal stands on its own.
    /// </summary>
    int Order(WorldThing creator);

    /// <summary>
    /// What a slot calls its creator when it is a player's character — the player, not the body, since
    /// Vitomir makes a player's body again at each arrival where 1.1's thing travels with its GUID
    /// (the slot holds the GUID). Null for anybody else.
    /// </summary>
    string? Key(WorldThing who);

    /// <summary>
    /// A remote player going through an end (the teleport journey's network branch, through the mover with
    /// entrance −2, the slot and the way): that player alone to the end's place; a character nobody plays
    /// beside the end when it is the one being steered, and nowhere otherwise (the mover's last branch). False
    /// only for the host's own character, whom the chronicle's own journey takes.
    /// </summary>
    bool Relay(WorldThing who, int location, int slot, int way);

    /// <summary>
    /// The character a slot's creator key (<see cref="Key"/>) names now — the player's body wherever it is, which
    /// Vitomir makes again at each arrival (ours; 1.1's keeps the GUID an end holds). Null for nobody.
    /// </summary>
    WorldThing? Who(string key);
}

public sealed partial class Teleports
{
    /// <summary>A network game's host (`Net.HostPlaces`), or null in single player.</summary>
    public ITeleportNetwork? Network { get; set; }

    /// <summary>
    /// The end the pair's way leads to must still be there (the teleport journey's first half, <see cref="Depart"/>
    /// — if it is gone the pair is shut and nobody moves); then a remote player goes alone.
    /// </summary>
    bool Relayed(WorldThing who, int slot, int way)
    {
        if (Network is not { } net) return false;
        var s = slots[slot];
        int to = way != 0 ? s.InLocation : s.OutLocation;
        var asked = new SequenceRequest(SequenceKind.Teleport, to, Entrance, Guid: who.Guid, Slot: slot, Way: way);
        if (!Depart(asked)) return true;
        return net.Relay(who, to, slot, way);
    }

    /// <summary>
    /// **One player arriving through a pair** on a host (the network half of the arrival with entrance
    /// −2): the character is put beside the end its way leads to (flags 3, as the party is)
    /// and stood on the ground; and on its creator's way back through a used pair the near end goes and
    /// the slot is freed, as the host's own does (the mover's tail: the near end's close). False when the
    /// end is not in <paramref name="to"/>.
    /// </summary>
    public bool LandOne(WorldThing who, Location to, int slot, int way)
    {
        if ((uint)slot >= Max) return false;
        var s = slots[slot];
        if (Find(to, way != 0 ? s.InThing : s.OutThing) is not { } end) return false;
        to.PlaceNear(who, end, 0f, NearFlags);
        who.Unsurfaced = false;
        if (who.Mind?.Walker is { } walker) { walker.Stop(); walker.Ground(); }
        if (way == 0 && (s.Flags & Used) != 0 && s.Creator == KeyOf(who))
        {
            CloseOut(slot, false);
            Release(slot, false);
        }
        return true;
    }

    /// <summary>The end a slot's way leads to in <paramref name="place"/> — way 1 the far end, way 0 the near — or null.</summary>
    public WorldThing? EndOf(Location place, int slot, int way) =>
        (uint)slot >= Max ? null : Find(place, way != 0 ? slots[slot].InThing : slots[slot].OutThing);

    /// <summary>
    /// **The owner of a teleporter's end** for the network rollover (the rollover's prop branch, on the
    /// end's owner, <see cref="PropOwners"/>) when its GUID is not in the end's place: on a host, the creator of
    /// the pair the end belongs to, found by its key wherever it is (<see cref="ITeleportNetwork.Who"/>) — a
    /// player's body, made again at each arrival — or, for anybody else, by the GUID in the other places the host
    /// has. Null in single player (no rollover asks there) and on a player's machine (it makes no pair).
    /// </summary>
    public WorldThing? OwnerOf(WorldThing end)
    {
        if (Network is not { } net || end.Place is not { } place || ThingIds.Of(place, end) is not { } id) return null;
        int at = net.IndexOf(place);
        foreach (var s in slots)
            if (s.Creator is { } key && ((s.OutLocation == at && s.OutThing == id) || (s.InLocation == at && s.InThing == id)))
                if (net.Who(key) is { Removed: false } who) return who;
        if (Campaign is { } campaign)
            foreach (var other in campaign.Resident.Values)
                if (other.Thing(end.PropOwner) is { Removed: false } t) return t;
        return null;
    }

    /// <summary>
    /// A player leaving the game: every pair they
    /// made is shut — both ends taken away whatever the flags — and its slot freed.
    /// </summary>
    public int ShutFor(WorldThing who)
    {
        string key = KeyOf(who);
        int shut = 0;
        for (int i = 0; i < Max; i++)
        {
            if (slots[i].Free || slots[i].Creator != key) continue;
            CloseIn(i, true);
            CloseOut(i, true);
            slots[i].Creator = null;
            shut++;
        }
        return shut;
    }
}
