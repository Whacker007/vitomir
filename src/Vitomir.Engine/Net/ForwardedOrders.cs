// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The client arm of three order functions, read:
// each makes its own tests and the cancel first, and only then sends the order and makes the player's
// machine busy. Earlier, Vitomir forwarded at the top of each, before any test, and set no busy time.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **A player's orders that wait on the player's machine** — the use, the pick-up and the put-down. In each
/// 1.1 order function the network test (net mode 1) comes **after** the order's own tests — busy, dead
/// or held (its busy time against the clock, the dead flag 0x10000000, the held flag 0x80), the repeat (the same
/// mode on the same target answers 0), ethereal (flag 0x20000), what the item may be — and after the
/// cancel and the busy test it asks again; then the order goes to the host and the
/// character on the player's machine is **busy** (its busy time set to now plus the time below), so a second click within that time is
/// refused there and never sent:
/// <list type="table">
/// <item><term>use</term><description>Kind 4: **250 ms**.</description></item>
/// <item><term>pick up</term><description>Kind 3: **200 ms**.</description></item>
/// <item><term>put down</term><description>Kind 0xe: **200 ms**; and for the
/// machine's own character the walk's ring at the point, which Vitomir's put-down does not show in single
/// player either.</description></item>
/// </list>
///
/// The order answers 4, "taken", as before. The host's copy is not made busy by this — it runs the same function
/// on the order, with its own tests. The attack's two orders are not these:
/// the shot at the ground already forwards after its tests (`Rpg.ClickOrders.AttackPoint`).
/// </summary>
public static class ForwardedOrders
{
    /// <summary>How long a player's machine holds its character after sending a use.</summary>
    public const int UseBusyMs = 250;
    /// <summary>After a pick-up.</summary>
    public const int TakeBusyMs = 200;
    /// <summary>After a put-down.</summary>
    public const int PutBusyMs = 200;

    /// <summary>
    /// On a player's machine: the order to the host (<see cref="NetOrders.Forward"/>) and the character busy for
    /// <paramref name="busyMs"/> from <paramref name="nowMs"/>; true, and the order function answers "started".
    /// Anywhere else false, and the order is carried out here.
    /// </summary>
    /// <summary>
    /// **The cancel's stop** (the cancel's tail): on a player's machine, when the cancel
    /// found the character doing something (case 0 returns first) and was not asked by a feed's release (its first
    /// argument 0), and the character is the one the machine steers, the host is sent order
    /// 0 for it, which the host carries out as a cancel and a stop of the walk (the order's
    /// case 0). So a use, a pick-up, a put-down or a shot at the ground clicked while the
    /// character walks sends a stop before its own order (their cancels), and
    /// Space sends a second one (its own handler sends one first). The character is walking there because the host's walk
    /// says so (message 9 sets mode 1): here the snapshot's <see cref="ThingState.Moving"/>.
    /// Nothing anywhere else.
    /// </summary>
    public static void Cancelled(WorldThing who)
    {
        if (NetOrders.Mode(who.Place) != NetMode.Client || !ReferenceEquals(who.Place!.Steered, who)) return;
        NetOrders.Forward(who, OrderKind.Stop);
    }

    public static bool Send(WorldThing who, OrderKind kind, int nowMs, int busyMs, WorldThing? target = null,
                            Vector3 point = default, int flags = 0)
    {
        if (!NetOrders.Forward(who, kind, target, point, flags)) return false;
        if (who.Actor is { } me) me.BusyUntil = nowMs + busyMs;
        return true;
    }
}
