// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player holding fire: the host's arm of order 0x1e, at a point or at a thing.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Held fire, on the host** (from order 0x1e — a player's machine sends it on each
/// loop of held automatic fire). With a `REPEATABLE` attack in hand and a model, on the host
/// (net mode 2), and the character either **already swinging** (action 5) or **free** — not busy
/// (busy time ≤ now), not dead (the dead flag, bit 0x10000000), not held (the second flags' bit 0x80) — it aims:
/// <list type="bullet">
/// <item>**with a thing** it faces the thing, makes it the target (by its GUID)
/// and **flags it `TARGETTED`** (thing flag 0x2000000, <see cref="WorldThing.Flag.Targetted"/>);</item>
/// <item>**with none** it faces the point, makes the point the order's with nobody for
/// its target;</item>
/// </list>
/// and then, **not in the swing**, strikes **at once** (not as a throw): the swing at the thing or the point
/// (<see cref="Actions.Strike(WorldThing, WorldThing, Weapon, int, MotionSets?)"/>,
/// <see cref="Actions.StrikeAt"/>) — no cancel, no approach, no second choice of attack. **In the swing** the
/// animation jumps back to its `RepeatTo` tag (<see cref="HeldFire.JumpBack"/>), with no busy time. An earlier build built the
/// point; with a thing Vitomir gave the attack order (which walks into reach first) and asked no gate,
/// and in the swing neither set the target nor flagged it — this takes the host's arm whole.
/// </summary>
public static class ServerAim
{
    /// <summary>
    /// The host's arm past its weapon test: <see cref="OrderResult.Ignored"/> when it is busy, dead or held and not
    /// swinging (the gate), or in the swing with no <c>RepeatTo</c> to jump back to; otherwise
    /// <see cref="OrderResult.Started"/>.
    /// </summary>
    public static OrderResult Aim(WorldThing me, Weapon weapon, WorldThing? thing, Vector3 point, int nowMs, MotionSets? motions)
    {
        if (me.Actor is not { } actor) return OrderResult.Refused;
        bool swinging = actor.Action == ActionMode.Swing;                        // action 5
        if (!swinging && (actor.BusyUntil > nowMs || actor.Has(Actor.Flag.Dead) || actor.Has2(Actor.Flag2.Restrained)))
            return OrderResult.Ignored;
        if (thing is null)
        {
            me.Face(point);                                                      // facing the point, not sent
            actor.ActionPoint = point;
            actor.Target = null;                                                 // nobody
        }
        else
        {
            me.Face(thing.Position);
            actor.Target = thing;                                                // by its GUID
            thing.Flags |= WorldThing.Flag.Targetted;                            // thing flag 0x2000000
        }
        if (swinging) return HeldFire.JumpBack(me) ? OrderResult.Started : OrderResult.Ignored;
        if (thing is null) Actions.StrikeAt(me, point, weapon, nowMs, motions);
        else Actions.Strike(me, thing, weapon, nowMs, motions);
        return OrderResult.Started;
    }

    /// <summary>The arm with no thing: <see cref="Aim"/> with none.</summary>
    public static OrderResult AtPoint(WorldThing me, Weapon weapon, Vector3 point, int nowMs, MotionSets? motions) =>
        Aim(me, weapon, null, point, nowMs, motions);
}
