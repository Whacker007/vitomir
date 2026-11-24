// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party leaving a place and arriving in one (the repair `memberstart`).
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **What the party's travel does to each member**, on the two sides of the door.
///
/// <para><b>Leaving</b> (per member, after its utility item comes off): a member who is
/// not staked has **its order cancelled, forced**  and its action
/// mode set to nothing  — so nothing a member was doing crosses the door: a feed
/// is stopped through the feed's own stop and its victim, left behind, is let go; a swing, a walk
/// up to an item, a cast in hand are all over. A staked member keeps its state (it is staked).
/// Vitomir only dropped the target (<see cref="Campaign"/>'s carry), so the action rode along and a
/// feeder's victim in the place left stayed held.</para>
///
/// <para><b>Arriving</b>: each member asks the entrance for the start one past its slot;
/// **one with no start of its own is put at the map's origin** in 1.1. Vitomir stands it
/// where the leader arrived instead — a repair (<see cref="Repairs.MemberStart"/>).</para>
/// </summary>
public static class PartyTravel
{
    /// <summary>
    /// One member leaving (the party move's loop body, its order half): unless staked
    /// (flag <c>0x200</c>), the order cancelled forced and the action mode cleared.
    /// </summary>
    public static void Leave(WorldThing member)
    {
        if (member.Actor is not { } me || me.Has(Actor.Flag.Staked)) return;
        Rpg.Actions.Cancel(member, force: true);
        me.Action = Rpg.ActionMode.None;                                        // the action mode cleared
    }

    /// <summary>
    /// Where member <paramref name="slot"/> (the leader is 0) arrives: the entrance's start for it
    /// when there is one (by group with <c>slot + 1</c>), and otherwise **where the leader arrived**
    /// — Vitomir's, a repair (<see cref="Repairs.MemberStart"/>): 1.1 stands it at the map's origin.
    /// </summary>
    public static Vector3 Arrival(Location to, int slot, WorldThing leader) =>
        slot < to.Arrivals.Length ? to.Arrivals[slot].Position : leader.Position;     // Repairs.MemberStart
}
