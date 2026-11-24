// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The move order's action** — `rpgAction::MoveTo` and ACTION 1's own tick.
/// The order itself, read end to end:
/// <list type="number">
/// <item>**not now** (2) while the character is busy, dead (`THING_AF_DEAD`) or held
/// (`THING_AF2_RESTRAINED`);</item>
/// <item>no ground (1) when no surface was named;</item>
/// <item>flag **0x8** only turns the character to face the point and answers 0;</item>
/// <item>without flag **0x100**, a body test at the point that hits anything but the
/// character itself refuses the order (1);</item>
/// <item>the point is pulled inside its surface by the walker's radius;</item>
/// <item>**the cancel** (unforced) — **unless the character is already moving** (action
/// 1) — and the busy, dead and held questions again (2);</item>
/// <item>a network client hands the order to the host instead;</item>
/// <item>the speed: flag 0x1 or 0x10 **runs**, 0x20 **walks**, and neither runs from 300 units out
/// — times MOVESPEED;</item>
/// <item>the walk — or, for a character with no surface under it, the ground found and a
/// straight leg — and a walk that finds no route refuses (1);</item>
/// <item>**action 1**, the time, the next held-button look (now + 200) and the
/// speed;</item>
/// <item>the ring, for the steered character without flag 0x200; answers 4.</item>
/// </list>
///
/// **The action lasts exactly as long as the walk.** ACTION 1's tick asks the walker
/// whether it is still going (bit 0 of the walker's flags) and, the moment it is not, puts the
/// action, the target, the pending discipline and the busy time back to nothing. In 1.1 every
/// one of Christof's walks goes `none → move` the
/// moment he sets off and `move → none` the moment his motion goes back to `stand`, and every
/// character standing still is `none`: the action does not outlast
/// the walk.
///
/// What the action changes is the **next** order's cancel. A character walking under a move order
/// is ACTION 1, and the cancel's branch for it **stops the walk**, takes the ring away if it is
/// the party's selected member, and clears the target, the pending discipline and the
/// busy time — so a flinch, a new attack, a use or a cast stops a walk in its tracks. A second move
/// order skips the cancel altogether and simply re-routes.
///
/// A script's `SendActorToPos` and the flier call the walker straight, not
/// this order, so neither sets the action — 1.1's crows are never `move`.
///
/// **The held-button walk** is the rest of ACTION 1's tick: while the steered
/// character walks under a move order, is not being driven by its AI (a frenzy, a daze,
/// a fear or a mesmerism has the controls), and the left button is still down on the world, every
/// 200 ms (which the order itself sets to now + 200) the ground under the pointer is looked
/// at, and once it is walkable and **more than 50 units** from the character itself,
/// it is ordered again — flags `0x200` (no new ring) and `0x1` with shift held. So holding the button
/// down and moving the mouse steers a walk, which is how 1.1 plays with the mouse alone. The viewer
/// says what is under the pointer (<see cref="Pointer"/>); everything else is here.
/// </summary>
public static class MoveOrder
{
    /// <summary>Flag 0x1 (and 0x10): run, whatever the distance.</summary>
    public const int Run = 0x1;
    /// <summary>Flag 0x10: run too — the AI's fear and hurt runs pass it.</summary>
    public const int RunToo = 0x10;
    /// <summary>Flag 0x20: walk, whatever the distance.</summary>
    public const int Walk = 0x20;
    /// <summary>Flag 0x8: only face the point.</summary>
    public const int FaceOnly = 0x8;
    /// <summary>Flag 0x100: do not ask whether the point is free.</summary>
    public const int AnyoneThere = 0x100;
    /// <summary>Flag 0x200: no ring — the held-button walk passes it.</summary>
    public const int NoRing = 0x200;

    /// <summary>How long before the held-button walk looks at the pointer again: 200 ms.</summary>
    public const int HeldLookMs = 200;

    /// <summary>
    /// The cancel in the middle of the order: whatever the character is doing
    /// is cancelled — **unless it is already walking under a move order** — and the order goes on
    /// only if that left it free. The head's own gate (busy, dead, held) comes first.
    /// </summary>
    public static bool Ready(WorldThing thing)
    {
        if (thing.Actor is not { } me) return true;
        int now = thing.Place?.Milliseconds ?? 0;
        if (NotFree(me, now)) return false;
        if (me.Action != ActionMode.Move) Actions.Cancel(thing);
        return !NotFree(me, now);
    }

    /// <summary>
    /// The head of the order, before anything is touched: **not now** (2) while
    /// the character is busy, dead or held; then — unless the caller set flag <c>0x100</c>
    /// (<paramref name="anyoneThere"/>) — the free-spot test at the point
    /// (<see cref="FreeSpot"/>): **refused** (1) when somebody else stands there, the walker's
    /// <see cref="Player.Occupant"/> set to them. 0 goes on to the clamp and the cancel
    /// (<see cref="Ready"/>). A refused order leaves the character walking its last route and in its
    /// action.
    ///
    /// Of the sixteen callers (the flags each pushes) only the approach
    /// (`0x100`/`0x110`) skips the test. Every other one asks: the enemy's manoeuvre and its
    /// fallback, the companions' sidestep, the fear's and the daze's
    /// strays, the hurt run to the leader (`0x10`), the
    /// walks home (`0x20`), the follow (`0`), the party's
    /// step away (`0x20`), the wanderer (`0x20` or `0x10`), the held-button walk
    /// (`0x200`/`0x201`), the click (its word is built from
    /// four keys into bits 0..3, never `0x100`) and the host's order for a client's character
    /// (the client's click word forwarded). Earlier, Vitomir asked for the manoeuvre, its
    /// fallback and the sidestep only.
    /// </summary>
    public static int Head(WorldThing thing, Player walker, System.Numerics.Vector3 point, bool anyoneThere = false)
    {
        walker.Occupant = null;
        if (thing.Actor is { } me && NotFree(me, thing.Place?.Milliseconds ?? 0)) return 2;
        if (anyoneThere) return 0;
        walker.Occupant = FreeSpot.Taken(walker, point);
        return walker.Occupant is null ? 0 : 1;
    }

    static bool NotFree(Actor me, int now) =>
        now < me.BusyUntil || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained);

    /// <summary>
    /// The walk was started: **ACTION 1**, the time and the speed
    /// — the walk or the run times MOVESPEED, as the walker was given it. The speed
    /// outlives the walk: an order that sets none of its own, such as the unstake, approaches at
    /// it. The held-button walk's next look is 200 ms off.
    /// </summary>
    public static void Taken(WorldThing thing)
    {
        if (thing.Actor is not { } me) return;
        me.Action = ActionMode.Move;
        me.ActionStartedAt = thing.Place?.Milliseconds ?? 0;
        me.ActionRetryAt = me.ActionStartedAt + HeldLookMs;              // the retry time = now + 200
        if (thing.Locomotion is { } walker)
            me.ActionSpeed = (walker.Running ? walker.RunSpeed : walker.WalkSpeed) * me.MoveSpeedScale;
    }

    /// <summary>
    /// ACTION 1's tick: once the walker has stopped — arrived, given up, or stopped by
    /// something else — the action, the target, the pending discipline and the busy time go back to
    /// nothing. While it walks, the held-button walk (see the class).
    /// </summary>
    public static void Tick(WorldThing thing, Actor me)
    {
        if (thing.Locomotion is { Walking: true } walker)
        {
            // The steered character, not in its AI's hands, its look due.
            int now = thing.Place?.Milliseconds ?? 0;
            if (!ReferenceEquals(thing.Place?.Steered, thing) || CastInput.Refused(thing)) return;
            if (me.ActionRetryAt >= now) return;
            me.ActionRetryAt = now + HeldLookMs;
            if (Pointer?.Invoke(thing) is { } held) Again(thing, walker, me, held, now);
            return;
        }
        // A player's machine's copy is in the move order by the host's word and does not walk itself (ours: its
        // position is eased to the host's), so the host's snapshot ends it (`Net.WorldMirror`).
        if (Net.NetOrders.Mode(thing.Place) == Net.NetMode.Client) return;
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
        me.BusyUntil = 0;
    }

    /// <summary>
    /// What is under the pointer while the left button is held on the world, for the held-button
    /// walk: the ground and its walk node, and whether shift is down. Null when the button is up,
    /// the pointer is on the interface, the camera is not the game camera, or the ground there is
    /// not walkable (the pointer ray's surface lacks `0x1000`). Set by the viewer; null in a run with none.
    /// </summary>
    public static Func<WorldThing, Held?>? Pointer { get; set; }

    /// <summary>The ground under a held button (<see cref="Pointer"/>).</summary>
    public readonly record struct Held(System.Numerics.Vector3 Point, int Node, bool Shift);

    /// <summary>How far the pointer's ground must be from the character to be ordered again (50).</summary>
    public const float HeldMoveBeyond = 50f;

    /// <summary>
    /// The held-button walk's order: the ground under the pointer, more than
    /// <see cref="HeldMoveBeyond"/> from the character's own position (not from
    /// the walk's goal), ordered with `0x200` (no
    /// ring) and `0x1` when shift is held — the move order again, which does not cancel a
    /// walk already under way — and the next look 200 ms off.
    /// </summary>
    public static bool Again(WorldThing thing, Player walker, Actor me, Held held, int now)
    {
        if (System.Numerics.Vector3.Distance(held.Point, thing.Position) <= HeldMoveBeyond) return false;
        if (Head(thing, walker, held.Point) != 0) return false;                // `0x200` asks
        if (!Ready(thing)) return false;
        bool runs = held.Shift || System.Numerics.Vector3.Distance(thing.Position, held.Point) >= Rules.GameRules.Current.Movement.RunBeyond;
        float speed = (runs ? walker.RunSpeed : walker.WalkSpeed) * me.MoveSpeedScale;
        if (!walker.WalkTo(held.Point, held.Node, run: runs, speed: speed)) return false;
        Taken(thing);
        me.ActionRetryAt = now + HeldLookMs;
        return true;
    }

    /// <summary>
    /// The cancel's arm for ACTION 1: the ring goes if this is the party's
    /// selected member (the simulation takes it away with the walk, see
    /// <see cref="Simulation"/>), the walk is stopped, and the action, the target, the
    /// pending discipline and the busy time are cleared. Unlike the catch-all arm it leaves
    /// `ef_restrain` alone.
    /// </summary>
    internal static void Cancelled(WorldThing thing, Actor me)
    {
        Actions.StopWalking(thing);
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
        me.BusyUntil = 0;
    }
}
