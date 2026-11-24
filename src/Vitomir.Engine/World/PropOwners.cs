// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whose a prop is: the owner's GUID looked up as the rollover does (its prop branch).
namespace Vitomir.Engine.World;

/// <summary>
/// **A prop's owner, found** — what the rollover's prop branch asks before it writes the owner line: the GUID
/// (<see cref="WorldThing.PropOwner"/>) looked up in the game's GUID table (the one
/// hashtable), so an owner anywhere in the game is found, and a GUID with no thing
/// behind it is nobody. The only props with an owner are a teleporter pair's ends.
/// </summary>
public static class PropOwners
{
    /// <summary>
    /// The thing <paramref name="prop"/>'s owner GUID names, or null: first in the prop's own place, then —
    /// Vitomir's places each keeping their own GUIDs where 1.1 has one table — through the teleporters, which
    /// ask the host for the pair's creator wherever the creator now is (<see cref="Teleports.OwnerOf"/>).
    /// **Ours**: 1.1's player's body keeps its GUID from place to place, Vitomir's is made again at each
    /// arrival, so on a host a pair's creator is found by the slot's player rather than by the old GUID; on a
    /// player's machine the GUID the host sent is looked up in that machine's world alone.
    /// </summary>
    public static WorldThing? Find(WorldThing? prop)
    {
        if (prop is not { Type: ThingType.Prop, PropOwner: not 0 and var guid } || prop.Place is not { } place) return null;
        if (place.Thing(guid) is { Removed: false } here) return here;
        return place.Session?.Teleports.OwnerOf(prop);
    }
}
