// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The take's and the use's three small rules, compared with
// 1.1 and built.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The take and the use, where they differ from the other approach orders.** Three rules, each a
/// small one in 1.1, which Vitomir's <see cref="Actions.Fetch"/> and <see cref="Actions.Use"/>
/// did not have:
///
/// <list type="number">
/// <item><b>A repeat click is ignored</b> (the take and the use alike):
/// a character already taking (mode 3) or using (mode 4) the very thing clicked answers 0 before
/// anything is cancelled — the walk goes on along the route it has. Vitomir's take cancelled and
/// ordered it again, a new route from wherever the character had got to. (The use already had it.)</item>
/// <item><b>Shift does not make it run</b> (the take and the use alike): the take's
/// and the use's speed is the distance's alone — within 300 units a walk, beyond them a run
/// (<see cref="GroundOrders.ApproachWalks"/>). Only the put-down asks shift
/// (<see cref="GroundOrders.Walks"/>).</item>
/// <item><b>Space gives it up where one stands</b> (the take and the use alike):
/// after the order's own first tick — which takes or uses a thing already in reach — a take or use
/// still waiting is cancelled, unforced, and the order answers 0. The two
/// differ in what that leaves: the take's first tick has **already ordered the walk** out of reach
/// and the cancel stops the order, not the walker, so the character walks to
/// the item and stands there without picking it up; the use's first tick orders no walk (the use's tick
/// only sets its retry), so a use or a talk with space held happens from where one stands or not
/// at all. The script native <c>ActorActionActivate</c> hands its modifier to the same order;
/// Vitomir's note that the modifier chose walk or run there was wrong.</item>
/// </list>
/// </summary>
public static class TakeRules
{
    /// <summary>
    /// Whether an order for <paramref name="target"/> is the one already under way — the same action
    /// on the same thing (action 3 and the action's target resolving to it).
    /// </summary>
    public static bool Repeat(Actor me, ActionMode action, WorldThing target) =>
        me.Action == action && ReferenceEquals(me.Target, target);

    /// <summary>
    /// The approach's speed: walk within <see cref="Stow.WalkWithin"/> of the target, run beyond it,
    /// times the MOVESPEED multiplier — the distance measured in three dimensions.
    /// No modifier is asked.
    /// </summary>
    public static float Speed(Actor me, WorldThing from, Vector3 to)
    {
        bool walks = GroundOrders.ApproachWalks(Vector3.Distance(from.Position, to));
        return (walks ? me.WalkSpeed : me.RunSpeed) * me.MoveSpeedScale;
    }

    /// <summary>
    /// <b>Space held</b>: the order still waiting after its first tick is cancelled, unforced
    /// (the cancel with (0, 0)). Answers whether it was, which is the order
    /// answering 0 (<see cref="OrderResult.Ignored"/>).
    /// </summary>
    public static bool StandStill(WorldThing thing, InputModifier mods)
    {
        if ((mods & InputModifier.Space) == 0) return false;
        Actions.Cancel(thing);
        return true;
    }
}
