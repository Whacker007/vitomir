// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Storyteller's hand on a body it carried to another place:
// 1.1's control stays on the body, which goes on taking orders there.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The Storyteller steers a carried body in its new place**. 1.1's possession is the current
/// control, one for the whole game: the mover's last branch moves the controlled body to
/// another place without touching it, and gives the camera the body — so the camera's sector
/// is the body's and the world is drawn from there, and a click on the ground is an order
/// to the control, which walks in the body's own world — **until the Storyteller
/// lets it go** (Possess asked again, or another possession or Set Ghost), when the control and
/// the camera go back to the host's own character (F6 does it too). (1.1's pick casts
/// from the camera's sector — read, <see cref="Point"/>.) Vitomir's steering is a place's own
/// (`Minds.Steered`), so the body is made the steered one of the place it went to
/// (<see cref="Take"/>), the host's place keeps its own, and the host's window hands its clicks and keys
/// to the body while it looks there (<see cref="Walk"/>, and the keys' push, <see cref="Drive"/>, which is
/// the QoL `driving`'s and ours; `Vitomir.StoryView`).
/// **Ours**, the limits of it: a click on the ground walks it (the walk within 300 units, the run past
/// it or with shift, as the host's own click). The pointer picks there too
/// (<see cref="Point"/>), a click on a thing is the seat's target or, out of the seat, the body's order on
/// it (<see cref="Order"/>), the right button casts (<see cref="Cast"/>), and the walk rings.
/// </summary>
public static class StoryHand
{
    /// <summary>
    /// The body the Storyteller possessed, carried to another place, stays possessed: the server's
    /// possession is its new thing, and it is the steered one of its place — its mind
    /// held as the player's is, its walker taking the orders. False when it is in no place with minds.
    /// </summary>
    public static bool Take(GameServer server, WorldThing body)
    {
        if (body.Place is not { } there || !there.SetSteered(body)) return false;
        server.Possessed = body;
        return true;
    }

    /// <summary>
    /// The body let go, wherever it stands: no longer the steered one of its place, so its own mind
    /// thinks again (the control gone from it). The server's possession is the caller's
    /// (<see cref="StoryPowers.Possess"/>).
    /// </summary>
    public static void LetGo(WorldThing body)
    {
        if (body.Place?.Minds is { } minds && ReferenceEquals(minds.Steered, body)) minds.Steered = null;
    }

    /// <summary>Whether <paramref name="body"/> is the one steered in its own place — what the host's window then drives.</summary>
    public static bool Holds(WorldThing? body) =>
        body is { InWorld: true, Place: { } there } && ReferenceEquals(there.Minds?.Steered, body);

    /// <summary>
    /// A left click in the body's place, as the host's own ground click: the ray
    /// from the camera meets the ground there; walkable ground is a move order — the walk within 300
    /// units unless shift is held, the run otherwise, at the body's own speed — refused while it is busy
    /// or held (the click's gate) or will not take orders. Answers what happened, for the console.
    /// </summary>
    public static string Walk(WorldThing body, Vector3 origin, Vector3 direction, InputModifier mods) =>
        Walk(body, origin, direction, mods, out _);

    /// <summary>
    /// <see cref="Walk(WorldThing, Vector3, Vector3, InputModifier)"/>, answering the spot walked to — where
    /// the click puts the ring, at the very end of a move order that was taken.
    /// </summary>
    public static string Walk(WorldThing body, Vector3 origin, Vector3 direction, InputModifier mods, out Hit? walked)
    {
        walked = null;
        if (!Holds(body) || body.Place is not { } there || there.SteeredWalker is not { } walker) return "the body is not in hand";
        if (body.Actor?.Dead == true) return "dead characters do not act";
        if (CastInput.Refused(body)) return $"{body.Template} is not answering to orders";
        if (WorldSweep.Pointer(there, origin, direction) is not { Walkable: true } spot) return "not walkable ground";
        if (!Actions.ReadyToMove(body)) return $"{body.Template} is busy, not now";
        bool walks = GroundOrders.Walks(mods, Vector3.Distance(body.Position, spot.Point));
        if (!walker.WalkTo(spot.Point, spot.PathNode, run: !walks,
                           speed: (walks ? walker.WalkSpeed : walker.RunSpeed) * (body.Actor?.MoveSpeedScale ?? 1f)))
            return $"no route to {spot.Point.X:0},{spot.Point.Y:0},{spot.Point.Z:0}";
        MoveOrder.Taken(body);                                                  // ACTION 1
        walked = spot;
        return $"{(walker.Running ? "running" : "walking")} to {spot.Point.X:0},{spot.Point.Y:0},{spot.Point.Z:0} " +
               $"in {there.Name} by {walker.Route.Length} waypoints";
    }

    // ---- the pointer, a click on a thing and the cast, in the body's place ------------------

    /// <summary>
    /// **What the pointer is over in the body's place**. 1.1's pick casts its ray from **the
    /// camera's sector** — a sweep 2,000 units long — the sector the draw starts from, so with the camera following the carried body it
    /// meets that place's ground and things, as they are drawn. Each thing met is offered by the pick's filter, which
    /// **in the Storyteller's seat** answers "anything but the current target"
    /// and otherwise the player's rule for the one steered (<see cref="Ui.Highlight.Offers"/>); a BLOCKSELECT
    /// thing stops the ray and swallows the click (<paramref name="blocked"/>). The ground met is
    /// <paramref name="ground"/>. Null when nothing is offered.
    /// </summary>
    public static WorldThing? Point(WorldThing body, Vector3 origin, Vector3 direction, bool seat, WorldThing? target,
                                    out bool blocked, out Hit? ground)
    {
        blocked = false;
        ground = null;
        if (body.Place is not { } there) return null;
        ground = WorldSweep.Pointer(there, origin, direction);
        var (passed, offers) = StoryPointer.For(seat, target, body);
        return Pick.NearestThing(there.Things, origin, direction, out blocked, ground?.Distance ?? float.MaxValue,
                                 passed, offers)?.Thing;
    }

    /// <summary>
    /// **A left click on a thing, out of the seat** (the left click's own arm, for the control): refused
    /// while its mind has the controls, then by the cursor's verb (<see cref="Cursor.Verb"/>)
    /// — attack (shift the second attack, space striking where it stands), pick up (alt
    /// into the hand), use or talk, pull a stake out — as the host's own click orders
    /// the character it steers. False, with nothing said, when the verb is none (the click then goes on to
    /// the ground, as the click's answer 1 lets it). A held button asks for the next swing (<see cref="Repeat"/>).
    /// </summary>
    public static bool Order(WorldThing body, WorldThing thing, InputModifier mods, out string said)
    {
        said = "";
        if (!Holds(body) || body.Place is not { } there) { said = "the body is not in hand"; return true; }
        if (body.Actor?.Dead == true) { said = "dead characters do not act"; return true; }
        if (CastInput.Refused(body)) { said = $"{body.Template} is not answering to orders"; return true; }
        int now = there.Milliseconds;
        string name = thing.Template;
        switch (Cursor.Verb(body, thing))
        {
            case CursorVerb.Attack:
                var weapon = body.Actor?.Weapon;
                int which = mods.HasFlag(InputModifier.Shift) && weapon is { AttackCount: > 1 } ? 1 : 0;
                said = $"attacking {name}: " + Actions.Attack(body, thing, weapon, which, now,
                    strikeNow: mods.HasFlag(InputModifier.Space), motions: there.Motions, mods: mods);
                return true;
            case CursorVerb.Take:
                said = $"picking up {name}: {Actions.Fetch(body, thing, now, mods)}";
                return true;
            case CursorVerb.Use:
                said = $"using {name}: {Actions.Use(body, thing, now, there.Motions)}";
                return true;
            case CursorVerb.Unstake:
                said = $"pulling the stake out of {name}: {Stake.Order(body, thing, now)}";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// **The held button's repeat, for the body in hand**. 1.1's end-of-swing question asks
    /// whether the swinger **is the control** — the carried body, not the host's
    /// character — and its mind does not have the controls (the mind's refusal alone: a frenzy, fear, a trance or a daze,
    /// a pet, a Dominate's possession), then what <see cref="SwingRepeat.Ask"/> reads: the *auto repeat attack*
    /// option, the button still held, the thing under the pointer now (the view's pick, <see cref="Point"/>) and
    /// Space. Holding the left button repeats the attack in 1.1; an earlier view gave one swing a click. Set
    /// once a frame, as the host's own steered character's (Program), and left on <see cref="Actor.Repeating"/>
    /// for the swing's end (<see cref="Actions"/>).
    /// </summary>
    public static void Repeat(WorldThing body, bool option, WorldThing? underPointer, bool leftHeld, bool middleHeld, InputModifier mods)
    {
        if (body.Actor is not { } me) return;
        me.Repeating = SwingRepeat.Ask(option, Holds(body) && !CastInput.MindHasControls(body), underPointer, leftHeld, middleHeld, mods,
                                       heldShift: (body.Place?.Qol ?? World.Qol.Current).HeldShift);
    }

    /// <summary>
    /// **The right button in the body's place** (on a thing the pointer offered, or on the
    /// ground, neither asking about the seat): the body casts its own selected discipline
    /// (<see cref="CastInput.OnThing"/>, <see cref="CastInput.OnGround"/>). Answers what it said.
    /// </summary>
    public static string Cast(WorldThing body, WorldThing? on, Vector3? ground, InputModifier mods)
    {
        if (!Holds(body)) return "the body is not in hand";
        int result = on is not null && !ReferenceEquals(on, body) ? CastInput.OnThing(body, on, mods) : CastInput.OnGround(body, ground, mods);
        string at = on is not null ? on.Template : ground is { } p ? $"{p.X:0},{p.Y:0},{p.Z:0}" : "the ground";
        return $"{body.Actor?.Disciplines.Current?.Name ?? "nothing selected"} at {at} -> {(OrderResult)result}";
    }

    /// <summary>
    /// The keys' push on the body (the QoL `driving`, ours as the host's own is): the walker moves the way
    /// pushed, shift running, and a push is refused while the body is busy — the click's gate.
    /// </summary>
    public static void Drive(WorldThing body, Vector2 push, bool run, float seconds)
    {
        if (!Holds(body) || body.Place?.SteeredWalker is not { } walker || body.Actor?.Dead == true) return;
        if (push.LengthSquared() > 1e-6f && !Actions.ReadyToMove(body)) walker.Move(Vector2.Zero, seconds);
        else if (push.LengthSquared() > 1e-6f) { walker.Running = run; walker.Move(push, seconds); }
        else if (!walker.Walking) walker.Move(Vector2.Zero, seconds);
    }
}
