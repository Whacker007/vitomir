// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The put-down order's own walk (the drop order's tail), matching the drop's.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The put-down walks when it is ordered, and a point it cannot walk to is refused there and then**.
/// Having kept the point and the item, the order itself starts the
/// walk: a walker with no surface under it  walks straight at the point,
/// which never refuses; any other asks `simWalker::WalkTo` for a route to the
/// clicked surface, and **no route answers 1** — the refusal —
/// before ACTION 0xE is written, so the order is never given. The ground click hands that answer
/// back to the mouse, which shows the red cross for it
/// (<see cref="ClickAnswer"/>), and the click is spent: there is no
/// walk after it.
///
/// Vitomir's put-down walked nowhere when it was ordered; its tick ordered the walk (at once, since
/// the order clears the clock) and, finding no way there, ended the order in silence — and the
/// harness then walked the character to the click instead. The tick still re-orders the walk every
/// half second (<see cref="Actions.Tick"/>), as the engine's does.
///
/// The route goes to **the surface the pointer's ray struck** — its path node (the
/// click's <see cref="Hit.PathNode"/>) — and the search answers 0 at once for a surface with none
/// (both nodes must be there). So a click with something held on a
/// surface that is no piece of the walk graph — a table top or a wall, unless the level's path file
/// made it one — is **refused**, unless space drops it at once (<see cref="GroundOrders.Immediate"/>,
/// which asks no route). Vitomir's tick had walked to the ground under such a point, or stopped. A
/// caller with no surface in hand (a test, the host) gets the walk map's ground under the point
/// instead (<see cref="WalkMap.Find"/>, ours).
///
/// **Odd, and 1.1's: a put-down at the carrier's own feet is refused.** On the ground it stands on,
/// the route is a straight walk, and a walk shorter than
/// <see cref="Player.Arrived"/> (a tenth of a unit) answers 0 — so the order answers 1.
/// A click lands that close only by luck; a step off it is walked to and put down on the half second.
/// </summary>
public static class PutRoute
{
    /// <summary>
    /// Order the put-down's walk to <paramref name="at"/> on the struck surface's path node
    /// <paramref name="struck"/> at <paramref name="speed"/>: true when the
    /// walk started (or will start: a carrier with nothing to walk it, which is ours, or with no ground
    /// under it, whose straight walk the tick orders), false for the engine's refusal.
    /// </summary>
    public static bool Walks(WorldThing carrier, Vector3 at, float speed, int? struck = null)
    {
        if (Actions.Walker(carrier) is not { } walker) return true;     // ours: no walker, nothing to ask
        if (walker.Node < 0) return true;                               // The straight walk
        int node = struck ?? walker.Map.Find(at);
        if (node < 0) return false;                                     // no node: the search answers 0
        // Straight to `simWalker::WalkTo`, not through the move order — so no clamp and no
        // free-spot test (Player.WalkTo's own notes), and the run by the order's speed.
        var me = carrier.Actor;
        bool run = me is not null && speed > me.WalkSpeed * me.MoveSpeedScale;
        return walker.WalkTo(at, node, run: run, speed: speed, clamp: false);
    }
}
