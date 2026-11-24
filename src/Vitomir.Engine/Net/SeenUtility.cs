// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The utility item in the off hand — a torch, a flashlight — on a player's machine, for somebody else's
// character as for her own.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The kit's utility item** (on a player's machine). 1.1's kit message reaches the
/// equip there, whose switch on the item's type hands a utility item (10) to the utility setter as on the host: the item's
/// thing is put in the world at the holder, **attached** to the bone its `ITEM_TAG` names, `NO HIGHLIGHT`, set as
/// the holder's utility item and its owner, and the holder's locomotion chosen again — its `util` stance. So on
/// every machine another character's torch rides that character's hand, as the host's does, rather than following
/// the snapshots a tenth of a second behind; the host's snapshots leave a thing riding another alone
/// (<see cref="WorldMirror"/>). Taken off (the setter given nothing), it is let go, taken out of the
/// world and the stance with it. <see cref="Utility.Set"/> is the setter on both.
///
/// **What is not there** is the flame: the equip on a player's machine tells the item's script nothing,
/// so `ItemUtilityTorch` never hears `worn` there, and its `torchFlame_nosave` — made on the host, flagged `NO NET
/// SYNCH` — the host keeps back (<see cref="MadeHere.ByScript"/>; the Fix `netprops` sends it). Another character's
/// torch is in his hand on a player's screen, and dark — as on 1.1's (*To check against 1.1*).
/// </summary>
public static class SeenUtility
{
    /// <summary>The utility item among what <paramref name="a"/> wears now into the hand, or the one held out of it.</summary>
    public static void Hold(WorldThing body, Actor a, IReadOnlyList<Item> worn)
    {
        var utility = worn.FirstOrDefault(i => i.Type == ItemType.Utility && i.Thing is not null);
        if (utility is not null)
        {
            bool riding = utility.Thing!.AttachedTo is { } on && ReferenceEquals(on, body) && utility.Thing.InWorld;
            if (!ReferenceEquals(a.Utility, utility) || !riding) Utility.Set(body, utility);
        }
        else if (a.Utility is not null) Utility.Set(body, null);
    }
}
