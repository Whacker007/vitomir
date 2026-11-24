// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The action state machine: what a character is *doing*, as opposed to what it is thinking.
//
// The combat half owns the attack. An order puts the character into ACTION_ATTACK (2),
// which closes the distance; once in reach the strike plays an animation and the character sits in
// ACTION_SWING (5) until it is free again. The blow itself is not fired by either state — it is
// fired by the animation's own `Strike` (or `Fire`) keyframe tag, through the keyframe
// dispatcher. So a weapon's reach in time is whichever frame the artist marked.
//
// The steps: the order, the close-in, the strike, the tag,
// the resolve, the swing's end, and the action tick's dispatch.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// What a character is doing. Only the ones whose handlers have
/// been read are named; the rest keep their numbers, because guessing a name for a state is how a
/// wrong reading gets embedded.
/// </summary>
public enum ActionMode
{
    None = 0,
    /// <summary>
    /// Walking to a point: the move order (`rpgAction::MoveTo`) sets 1 for an actor or a
    /// player, and its tick puts it back to nothing the moment
    /// the walk stops — so it lasts exactly as long as the walk, as in 1.1 on every
    /// one of Christof's walks (`none → move → none`). A second move order does not
    /// cancel it; any other order's cancel stops the walk. A script's
    /// walk and a flight never set it. <see cref="MoveOrder"/> has the whole reading.
    /// </summary>
    Move = 1,
    /// <summary>
    /// Walking up to something to pick it up (the take order sets 3).
    /// The approach is given a reach of 50 units and the take happens on arrival.
    /// </summary>
    Take = 3,
    /// <summary>Walking up to something to use or talk to it (the use order sets 4).</summary>
    Use = 4,
    /// <summary>Closing on a target to attack it (`actionMode == ACTION_ATTACK`).</summary>
    Attack = 2,
    /// <summary>The blow is in flight (`actionMode == ACTION_SWING`).</summary>
    Swing = 5,
    /// <summary>
    /// Flinching from a blow (the flinch sets 10, busy for the `hurt` animation — <see cref="Flinch"/>).
    /// Nothing ticks it; the next order's cancel takes it back to nothing.
    /// </summary>
    Hurt = 10,
    /// <summary>Walking up to somebody to feed on them (the feed order sets 7; it has a tick).</summary>
    ApproachFeed = 7,
    /// <summary>
    /// Feeding at the neck — **the feeder**. The dispatcher sends 8 and 11
    /// to the same feeder tick and 9 and 12 to the same victim tick, which
    /// is what settles the four: 8 and 11 feed, 9 and 12 are fed on, and the pairing is neck
    /// (8 with 9) against wrist (11 with 12). This enum had 11 as "fed on" until then.
    /// </summary>
    Feeding = 8,
    /// <summary>Being fed on at the neck — the victim, restrained.</summary>
    FedOn = 9,
    /// <summary>Feeding from a friend's wrist — the feeder of a party feed.</summary>
    WristFeeding = 11,
    /// <summary>Giving blood from the wrist — the donor of a party feed.</summary>
    WristFedOn = 12,
    /// <summary>
    /// Casting a discipline (the cast order sets 13). **It has no tick** — the action dispatch
    /// has no case for it — and nothing resets it when the cast is over; it lingers until the next
    /// order cancels it, which frees the pending discipline.
    /// </summary>
    Cast = 13,
    /// <summary>
    /// Staked (the stake sets 0x13, with `THING_AF_STAKED`, `ef_staked` and the
    /// staked pose); its tick lets the stake out after twenty seconds (<see cref="Stake"/>).
    /// </summary>
    Staked = 19,
    /// <summary>
    /// Walking up to a staked friend to pull the stake out (the pull order sets 0xF; it
    /// has a tick — <see cref="Stake"/>).
    /// </summary>
    Unstake = 0xF,
    /// <summary>Held still by a script (`ActorStill` sets 0x14).</summary>
    Still = 20,
    /// <summary>Walking into a discipline's range before casting it (the cast's walk sets 0x16; it has a tick).</summary>
    ApproachCast = 22,
    /// <summary>
    /// Thrown by a special move (the special move sets 0x11, with a tick — <see cref="SpecialMove"/>): the
    /// victim, restrained, busy for its `thrown` motion. The `Throwdeath` tag ends it early.
    /// </summary>
    Thrown = 17,
    /// <summary>Throwing somebody (the special move sets 0x12, with a tick — <see cref="SpecialMove"/>).</summary>
    Throwing = 18,
    /// <summary>
    /// Walking to a point to **put down** what is in hand (the drop order sets 0xe). The order carries
    /// the point and the item's id, and the drop happens on arrival —
    /// which is why the base game walks you over to where you clicked before the item lands.
    /// </summary>
    Drop = 0xE,
}

/// <summary>What an attack order came to (the engine's own return codes).</summary>
public enum OrderResult
{
    /// <summary>Already doing it, or the target is on its way out. Nothing changed.</summary>
    Ignored = 0,
    /// <summary>Refused: no weapon, ethereal, or a target that may not be attacked.</summary>
    Refused = 1,
    /// <summary>Not now: still busy, dead, or restrained.</summary>
    Busy = 2,
    /// <summary>Taken.</summary>
    Started = 4,
}

/// <summary>Driving a character's action state.</summary>
public static class Actions
{
    /// <summary>
    /// The flags a target may not carry for an attack order to be taken: `THING_AF_DEAD`,
    /// `THING_AF_NEUTRAL` and `THING_AF_TALKTO`. Between them they are why a corpse, an
    /// animal, and anybody you are meant to talk to cannot be swung at.
    /// </summary>
    public const uint Unattackable = Actor.Flag.Dead | Actor.Flag.Neutral | Actor.Flag.TalkTo;

    /// <summary>How long a character waits before trying to close again.</summary>
    public const int ApproachRetryMs = 500;

    /// <summary>
    /// How close you have to be to **use** something — 75 units, body to body, the constant
    /// 0x42960000 the use tick compares against.
    /// </summary>
    public const float UseReach = 75f;

    /// <summary>
    /// And how long it waits before trying to close again, which is **250 ms** rather than the
    /// attack's 500 (250). Both numbers are the engine's.
    /// </summary>
    public const int UseRetryMs = 250;

    /// <summary>
    /// How long the **take** waits before asking again. **150 ms**, not the
    /// attack's 500 and not the use's 250 — three orders, three numbers, all of them the engine's.
    /// </summary>
    public const int TakeRetryMs = 150;

    /// <summary>
    /// `simCombat::AttackThing`. Puts <paramref name="attacker"/> on <paramref name="target"/> with
    /// the weapon's <paramref name="attack"/>, and either starts closing or strikes at once.
    /// <paramref name="mods"/> is the click's modifier mask, which the engine passes all the way in
    /// — only Ctrl is read here, by the party rally at the end.
    /// </summary>
    public static OrderResult Attack(WorldThing attacker, WorldThing target, Weapon? weapon,
                                     int attack, int nowMs, bool strikeNow = false,
                                     MotionSets? motions = null,
                                     InputModifier mods = InputModifier.None)
    {
        if (attacker.Actor is not { } me) return OrderResult.Refused;
        // On her machine her own swing starts at once when the host's strike is certain (Net.OwnStrike), asked before the
        // order goes, which ends her walk here.
        var own = Net.NetOrders.Strikes(attacker);
        bool sure = own?.Sure(attacker, target, weapon, attack) == true;
        // A client sends the order to the host instead and answers 4 (kind 2).
        if (Net.NetOrders.Forward(attacker, Net.OrderKind.Attack, target, flags: (int)mods, attack: (byte)attack))
        {
            if (sure) own!.Start(attacker, target, weapon!, attack, nowMs, motions ?? attacker.Place?.Motions);
            return OrderResult.Started;
        }

        // Busy, dead or held down: not now.
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained))
        {
            PausedOrders.Busy(attacker);                                  // RPG_CANTDOACTION while paused
            return OrderResult.Busy;
        }
        // Already swinging at this one.
        if (me.Action == ActionMode.Attack && me.Target == target) return OrderResult.Ignored;
        if (me.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (weapon is null) return OrderResult.Refused;

        if (target.IsActor && (target.Actor!.Flags & Unattackable) != 0) return OrderResult.Refused;
        if (target.Has(WorldThing.Flag.Remove)) return OrderResult.Ignored;

        // Whatever it was doing is cancelled first, and asked again — a feed lets go and
        // plays its release, which makes it busy, so the attack waits. Vitomir skipped this until
        // And a Cappadocian feeding on Wilhem turned on Christof with her teeth still in him:
        // the feed was never let go, Wilhem was never released, and his fed-on pose was cut off
        // every frame by the release his own tick kept asking for.
        if (!CancelFirst(attacker, me, nowMs)) return OrderResult.Busy;

        Rally(attacker, target, mods);
        // **The walk is stopped**, before the target is set — whatever the
        // character was walking, a move order's route included, which the cancel above has already
        // stopped only for ACTION 1.
        StopWalking(attacker);
        weapon.Select(attack);
        // Journalled after the select, so the line says what will actually be swung: a weapon with
        // one attack record ignores an ask for the second, and the unused record's default reach of
        // 50 used to be written here — a bowman at 371 appeared to be ordering a sword thrust.
        Diagnostics.Journal.Current?.Write(nowMs, Diagnostics.JournalKind.Order,
            ("what", "attack"), ("who", Diagnostics.Watch.Name(attacker)),
            ("at", Diagnostics.Watch.Name(target)), ("weapon", weapon.Name),
            ("attack", weapon.CurrentAttack), ("gap", World.Bodies.Gap(attacker, target)),
            ("reach", weapon.Current.MaxDistance));

        me.Target = target;
        me.Action = ActionMode.Attack;
        me.BusyUntil = nowMs;
        // **The half-second retry is not touched** (the order writes the busy time,
        // the action and its speed only): a new target within half a second of the last closing walk
        // is not walked at until the retry comes round, and the attacker stands meanwhile. Vitomir
        // cleared it until then and closed on the new target at once.
        // The speed it will close at: its run speed, scaled by whatever MOVESPEED is on it now.
        // The engine reads the multiplier here, when the order is given, and not again.
        me.ActionSpeed = me.RunSpeed * me.MoveSpeedScale;

        if (strikeNow) { Strike(attacker, target, weapon, nowMs, motions); return OrderResult.Started; }
        // **The close-in runs at once, inside the order**: in reach, the
        // strike is this call's, not the next tick's, so a held button's next swing comes on the tick
        // the reload runs out; and an order the close-in cancels answers refused (`action != 0
        // ? 4: 1`). Vitomir left both to the next tick until then, a frame late on every swing.
        Approach(attacker, me, nowMs, moving: false, motions ?? attacker.Place?.Motions);
        return me.Action == ActionMode.None ? OrderResult.Refused : OrderResult.Started;
    }

    /// <summary>
    /// **The player's own attack pulls the party in** (the attack order's tail,
    /// calling the party's rally with the steered character as the "victim"). Only for the
    /// character being steered, and only against a character with a mind of its own — and then:
    ///
    /// <list type="bullet">
    /// <item>against a **townsperson** (home mode `pedwander`) or a **guard** (`searchStyle 1`), only
    /// with **Ctrl** held;</item>
    /// <item>against anybody else, always.</item>
    /// </list>
    ///
    /// Ctrl is also handed to the rally itself, where it pulls in members that already have a target
    /// and members that are feeding — which is the manual's *"party attack"* on Ctrl + left click,
    /// read from the side that does it. The two mind tests are the same pair the engine uses for
    /// who is afraid of a feeder, so "the people who call the watch" is one idea in the
    /// engine, not two.
    ///
    /// Held to keep swinging, the order is given again at the end of every swing with
    /// only Space in the mask, so the rally is offered again each time — without Ctrl.
    /// </summary>
    static void Rally(WorldThing attacker, WorldThing target, InputModifier mods)
    {
        if (attacker.Place is not { } place || !ReferenceEquals(place.Steered, attacker)) return;
        if (attacker.Mind is not { } lead || place.Minds is not { } minds) return;
        if (target.Type is not (ThingType.Actor or ThingType.Player) || target.Mind is not { } theirs) return;

        bool ctrl = (mods & InputModifier.Ctrl) != 0;
        bool bystander = theirs.BaseMode == Ai.AiMode.PedWander
                         || theirs.Class.SearchStyle == Ai.Minds.GuardSearchStyle;
        if (bystander && !ctrl) return;
        minds.Rally(lead, target, forced: ctrl);
    }

    /// <summary>
    /// The take order — walk over and pick that up. An **approach order**: it writes
    /// action 3, walks, and does the take on arrival, which is why the base game
    /// marches you across the room before the item moves.
    ///
    /// It also raises `THING_FLAG_TARGETTED` on the item, which is what makes the other half work:
    /// when somebody else gets there first, <see cref="Location.Untarget"/> stops this walk rather
    /// than leaving a character trudging towards an empty floor.
    /// </summary>
    public static OrderResult Fetch(WorldThing taker, WorldThing itemThing, int nowMs,
                                    InputModifier mods = InputModifier.None)
    {
        if (taker.Actor is not { } me || itemThing.Item is not { } item) return OrderResult.Refused;
        var gate = GroundOrders.MayTake(taker, item,
                                        itemThing.Owner is not null || itemThing.Holder is not null, nowMs);
        if (gate != OrderResult.Started) return gate;
        if (!itemThing.InWorld) return OrderResult.Refused;
        // A second click on the item already being walked to is ignored — no cancel, no
        // new route (TakeRules; a comparison with 1.1's gap).
        if (TakeRules.Repeat(me, ActionMode.Take, itemThing)) return OrderResult.Ignored;
        // Cancel what it is doing, then ask again.
        if (!CancelFirst(taker, me, nowMs)) return OrderResult.Busy;
        // A player's machine sends it to the host now, after the tests, and is busy 200 ms (Net.ForwardedOrders).
        if (Net.ForwardedOrders.Send(taker, Net.OrderKind.Take, nowMs, Rules.GameRules.Current.Party.ForwardedOrderBusyMs[1], itemThing, flags: (int)mods))
            return OrderResult.Started;

        me.Target = itemThing;
        me.Action = ActionMode.Take;
        me.OrderMods = mods;
        me.ActionRetryAt = 0;
        me.ActionSpeed = TakeRules.Speed(me, taker, itemThing.Position);     // Shift is not asked
        itemThing.Flags |= WorldThing.Flag.Targetted;
        // The order ticks it once here, so an item already at your feet is picked up without a
        // frame of walking, and then orders the approach. **A refused approach does not
        // cancel the order** (there is no `Cancel` on that path, only `return 1`): the character
        // stays on ACTION_TAKE and the tick keeps asking.
        me.ActionRetryAt = 0;
        Fetching(taker, me, nowMs);
        if (me.Action != ActionMode.Take) return OrderResult.Started;
        // With space held a take out of reach is given up where one stands.
        if (TakeRules.StandStill(taker, mods)) return OrderResult.Ignored;
        me.ActionRetryAt = 0;
        return Close(taker, me, itemThing, nowMs, Rules.GameRules.Current.Movement.TakeRetryMs) ? OrderResult.Started : OrderResult.Refused;
    }

    /// <summary>
    /// The drop order — walk over there and put this down. Mode `0xE`, the point kept,
    /// and the drop on arrival.
    ///
    /// **Except with space held**, which is the engine's own shortcut: `mods &amp; 0x8` skips the
    /// walk entirely and drops at once (<see cref="GroundOrders.Immediate"/>). Where it lands is
    /// <see cref="GroundOrders.Landing"/> — past arm's reach the original throws the item **fifty
    /// times** the click distance away, which is a repair rather than a Fix
    /// (<see cref="World.Repairs.DropLanding"/>) — and the walk-surface test at the far end still
    /// refuses a landing with no ground under it, because that part is the engine's. That is what this
    /// returns <see cref="OrderResult.Ignored"/> for — there was no order to give, because it has
    /// already happened.
    ///
    /// <paramref name="node"/> is the path node of the surface the click struck, which
    /// the order's walk is routed to (<see cref="PutRoute"/>); null finds the ground under the point
    /// instead (ours, for a caller with no surface in hand).
    /// </summary>
    public static OrderResult Put(WorldThing dropper, Item item, Vector3 at, int nowMs,
                                  InputModifier mods = InputModifier.None, int? node = null)
    {
        if (dropper.Actor is not { } me || item.Thing is not { } itemThing) return OrderResult.Refused;
        bool mine = ReferenceEquals(itemThing.Owner, dropper) || ReferenceEquals(itemThing.Holder, dropper);
        var gate = GroundOrders.MayDrop(dropper, item, mine, nowMs);
        if (gate != OrderResult.Started) return gate;
        // Cancel what it is doing, then ask again.
        if (!CancelFirst(dropper, me, nowMs)) return OrderResult.Busy;
        // A player's machine sends it to the host now, after the tests, and is busy 200 ms (Net.ForwardedOrders).
        if (Net.ForwardedOrders.Send(dropper, Net.OrderKind.Drop, nowMs, Rules.GameRules.Current.Party.ForwardedOrderBusyMs[2], itemThing, at, (int)mods))
            return OrderResult.Started;

        if (GroundOrders.Immediate(mods) && dropper.Place is { } here)
        {
            var where = GroundOrders.Landing(dropper.Position, at);
            // Beyond the reach the engine checks the landing point has walkable ground under it and
            // refuses when it has not — which is the only thing standing
            // between the arithmetic above and an item thrown across the level.
            if (where != at && (here.Walk?.Find(where) ?? -1) < 0) return OrderResult.Refused;
            return Ground.Drop(here, dropper, item, nowMs, where, here.ItemScripts) == GroundResult.Dropped
                ? OrderResult.Ignored : OrderResult.Refused;
        }

        // The order walks as it is given, and no way there refuses it (PutRoute).
        float speed = Speed(me, dropper, at, mods);
        if (!PutRoute.Walks(dropper, at, speed, node)) return OrderResult.Refused;
        me.Target = itemThing;
        me.ActionPoint = at;
        me.Action = ActionMode.Drop;
        me.OrderMods = mods;
        me.ActionRetryAt = 0;
        me.ActionSpeed = speed;
        return OrderResult.Started;
    }

    /// <summary>
    /// How fast an approach order moves: `(mods &amp; 0x11) == 0` and the point within
    /// <see cref="Stow.WalkWithin"/> walks, and anything else runs — the rule
    /// <see cref="GroundOrders.Walks"/> states, with the MOVESPEED multiplier the engine folds in
    /// when the order is given and not again.
    /// </summary>
    static float Speed(Actor me, WorldThing from, Vector3 to, InputModifier mods)
    {
        bool walks = GroundOrders.Walks(mods, Vector3.Distance(from.Position, to));
        return (walks ? me.WalkSpeed : me.RunSpeed) * me.MoveSpeedScale;
    }

    /// <summary>One tick of whatever the character is doing.</summary>
    public static void Tick(WorldThing thing, int nowMs, bool moving = false, MotionSets? motions = null)
    {
        if (thing.Actor is not { } me) return;
        switch (me.Action)
        {
            case ActionMode.Move: MoveOrder.Tick(thing, me); break;
            case ActionMode.Unstake: Stake.Tick(thing, me, nowMs); break;
            case ActionMode.Staked: Stake.Staked(thing, me, nowMs); break;
            case ActionMode.Attack: Approach(thing, me, nowMs, moving, motions); break;
            case ActionMode.Swing: Swinging(thing, me, nowMs, motions); break;
            // A finished cast lingers as Cast (1.1 has no tick for it), a flinch as Hurt: free once no longer busy.
            case ActionMode.None or ActionMode.Hurt or ActionMode.Cast: HeldAgain(thing, me, nowMs, motions); break;
            case ActionMode.Use: Using(thing, me, nowMs, motions); break;
            case ActionMode.Take: Fetching(thing, me, nowMs); break;
            case ActionMode.Drop: Putting(thing, me, nowMs); break;
            case ActionMode.ApproachCast: Cast.Approaching(thing, me, nowMs); break;
            case ActionMode.ApproachFeed: Feeding.Approaching(thing, me, nowMs); break;
            case ActionMode.Feeding:
            case ActionMode.WristFeeding: Feeding.Tick(thing, me, nowMs); break;
            case ActionMode.FedOn:
            case ActionMode.WristFedOn: Feeding.VictimTick(thing, me, nowMs); break;
            case ActionMode.Thrown: SpecialMove.ThrownTick(thing, me, nowMs); break;
            case ActionMode.Throwing: SpecialMove.ThrowingTick(thing, me, nowMs); break;
        }
    }

    /// <summary>
    /// ACTION_TAKE's arrival test. The reach is **50 units**, asked of the reach test
    /// (<see cref="World.Bodies.InReach"/>) like every other reach in this file, and out
    /// of reach the approach is ordered **again**, every <see cref="TakeRetryMs"/>. An item that has left the world while we were walking — somebody
    /// else got it — ends the order.
    /// </summary>
    static void Fetching(WorldThing thing, Actor me, int nowMs)
    {
        if (me.Target is not { Item: { } item } target || !target.InWorld || me.Has(Actor.Flag.Dead))
        {
            Stop(me);
            return;
        }
        // **The reach is asked on the retry clock** (the retry time is tested first): every 150 ms,
        // not every frame, so a taker walks on a little and picks up on the tick. The order clears
        // the clock, so an item at the feet is still taken at once. (A comparison with 1.1; Vitomir asked
        // every frame and picked up the instant it came within 50.)
        if (me.ActionRetryAt > nowMs) return;
        if (World.Bodies.InReach(thing, target, Rules.GameRules.Current.Movement.TakePutReach))
        {
            var place = thing.Place;
            Stop(me);
            if (place is null) return;
            // The cascade asks whether the taker is steered and the hand branch whether it is the
            // party's selected member — the same character except
            // while a possession lasts, and until then both read the party's leader instead.
            Ground.Take(place, thing, target, me.OrderMods,
                        ReferenceEquals(place.Steered, thing), nowMs, place.ItemScripts,
                        place.InventoryShown?.Invoke() ?? false, selected: ReferenceEquals(place.Selected, thing));
            return;
        }
        Close(thing, me, target, nowMs, Rules.GameRules.Current.Movement.TakeRetryMs);        // the answer is thrown away, as in 1.1
    }

    /// <summary>
    /// ACTION_DROP's arrival test: the same reach, against the point rather than a thing.
    /// </summary>
    static void Putting(WorldThing thing, Actor me, int nowMs)
    {
        if (me.Has(Actor.Flag.Dead)) { Stop(me); return; }
        // On the retry clock too: the reach is asked every 500 ms, so the carrier walks
        // on to the point and puts it down on the half second (a comparison with 1.1).
        if (me.ActionRetryAt > nowMs) return;
        if (Vector3.Distance(thing.Position, me.ActionPoint) <= Rules.GameRules.Current.Movement.TakePutReach)
        {
            var (place, at, target) = (thing.Place, me.ActionPoint, me.Target);
            Stop(me);
            // **An item no longer the carrier's is refused at the point**: the
            // refusal cursor and its noise, and the order over (PutRefusal; Vitomir stopped
            // in silence, and before the walk rather than at its end).
            if (!PutRefusal.Mine(thing, target)) { PutRefusal.Show(thing, nowMs); return; }
            if (place is not null) Ground.Drop(place, thing, target!.Item!, nowMs, at, place.ItemScripts);
            return;
        }
        Walk(thing, me, me.ActionPoint, nowMs);
    }

    /// <summary>
    /// The re-order every approach shares, for an order that walks at a **point** rather
    /// than at a thing — which in the engine is only the put-down. Walking at a *thing* goes
    /// through <see cref="Toward"/>, because a thing may be standing somewhere nobody can.
    /// </summary>
    static void Walk(WorldThing thing, Actor me, Vector3 to, int nowMs, int? retry = null)
    {
        int retryMs = retry ?? Rules.GameRules.Current.Movement.AttackApproachRetryMs;
        if (me.ActionRetryAt > nowMs) return;
        if (thing.Mind is not { } mind) { Stop(me); return; }
        int node = mind.Walker.Map.Find(to);
        if (node < 0) { Stop(me); return; }
        mind.Walker.WalkTo(to, node, run: me.ActionSpeed > me.WalkSpeed * me.MoveSpeedScale,
                                     speed: me.ActionSpeed);
        me.ActionRetryAt = nowMs + retryMs;
    }

    /// <summary>
    /// **The gap the engine leaves between two bodies when it cannot stand on top of one**
    /// (45 units). It is added to both radii on the approach's last two attempts.
    /// </summary>
    public const float ApproachMargin = 45f;

    /// <summary>
    /// Where to stand to reach <paramref name="target"/>, or −1 in the node when there is nowhere.
    ///
    /// This is the approach every order shares, and it is a **ladder
    /// of five**, not the single question Vitomir was asking. The first rung is the obvious one:
    /// the ground the target is standing on. The other four exist because **a great many things in
    /// this game are not standing on any ground at all** — a purse on a table, a bracelet on a
    /// shelf, a lever on a wall — and walking at their own position finds nothing and gives up,
    /// which is exactly what play showed when Christof refused to fetch a gold bag off a table and
    /// had to be walked over by hand.
    ///
    /// <list type="number">
    /// <item>the ground under the target's own position — by the engine's ground ray cast for the
    /// target (the ground ray: from 50 over it, 140 long, a sphere's radius or a box's lowest
    /// point longer), as every rung below asks it too;</item>
    /// <item><b>a character stops here.</b> Types 0 and 7 get no fallback at all: a
    /// person you cannot stand next to is a person you cannot reach, and the engine will not
    /// invent a spot for one;</item>
    /// <item>backed off toward the walker by the <b>target's radius</b>, at the target's own
    /// height — a radius of zero when it has no collide shape, so this rung is the same point
    /// again for anything insubstantial;</item>
    /// <item>the same XY at the <b>walker's own height</b>. This is the rung that finds the floor
    /// beside the table;</item>
    /// <item>backed off by <b>both radii plus <see cref="ApproachMargin"/></b>, at the target's
    /// height, and then</item>
    /// <item>that same point at the walker's height.</item>
    /// </list>
    ///
    /// The engine then **caches the surface it found on the target**, and
    /// every later approach to it takes the first rung: that surface, at the target's own x and y
    /// (<see cref="ApproachSurface"/>; built — Vitomir searched afresh every time and aimed
    /// at the backed-off point again).
    /// </summary>
    public static (Vector3 To, int Node) Toward(WorldThing thing, WorldThing target)
    {
        if (Walker(thing)?.Map is not { } map) return (target.Position, -1);
        if (ApproachSurface.Cached(target, map) is { } kept) return kept;

        // **Every rung asks the engine's own ground ray, cast for the target** (on the
        // target for the first, then the ray cast for the target): from 50 over the
        // point, 140 long, and longer for a sphere or a box (`WalkMap.Beneath`). Earlier, Vitomir
        // asked its own narrower window here (64 down, 16 up), which is why a click on the gate to
        // the Judith Bridge from the lower end of the Golden Lane left Christof standing: the gate
        // is a box centred 140 over its doorstep, and the doorstep is 48 over the lane.
        // And **whichever rung answers, the point walked at is on that surface's plane** (after the ladder and common to every way out of it), not at the height the ray
        // was cast from.
        int node = map.Beneath(target.Position, target);
        if (node >= 0) { ApproachSurface.Keep(target, map, node); return (OnGround(map, target.Position, node), node); }
        // A character gets no fallback. Everything else backs off along the line between them.
        if (target.Type is ThingType.Actor or ThingType.Player) return (target.Position, -1);

        var away = new Vector2(target.Position.X - thing.Position.X,
                               target.Position.Y - thing.Position.Y);
        // Standing exactly on top of it: there is no line to back off along, and the four remaining
        // rungs would all ask the same question again.
        if (away.LengthSquared() <= 1e-6f) return (target.Position, -1);
        away = Vector2.Normalize(away);

        float theirs = Reach(target), mine = Reach(thing);
        foreach (float back in (ReadOnlySpan<float>)[theirs, theirs + mine + Rules.GameRules.Current.Movement.ApproachBodyGap])
        {
            var at = target.Position - new Vector3(away * back, 0f);   // the Z term is zero
            if (map.Beneath(at, target) is var found and >= 0) { ApproachSurface.Keep(target, map, found); return (OnGround(map, at, found), found); }
            at.Z = thing.Position.Z;                                    // Try at our own height
            if (map.Beneath(at, target) is var lower and >= 0) { ApproachSurface.Keep(target, map, lower); return (OnGround(map, at, lower), lower); }
        }
        return (target.Position, -1);
    }

    /// <summary>A point's x and y at the height of the surface found under it.</summary>
    static Vector3 OnGround(WalkMap map, Vector3 at, int node) => at with { Z = map.Nodes[node].Height(at.X, at.Y) };

    /// <summary>
    /// How far a body reaches out from its own position for the purposes of the approach:
    /// its radius, or **zero** when it has no collide shape at all — the shape is tested
    /// before the radius is read, and the constant it falls back on is 0.0.
    /// </summary>
    static float Reach(WorldThing thing) => thing.Collide == CollideShape.None ? 0f : thing.Radius;

    /// <summary>
    /// Start (or re-start) a walk at <paramref name="target"/> through <see cref="Toward"/>.
    /// False when there is nowhere to stand, which is the engine's own refusal (the use order returns 1
    /// — "cannot" — when the approach answers zero).
    /// </summary>
    internal static bool Close(WorldThing thing, Actor me, WorldThing target, int nowMs, int retryMs)
    {
        // **The retry is set whatever happens**, which is the half of this that matters most: the
        // engine ignores the approach's answer inside both ticks (both throw it
        // away) and simply asks again in a moment.
        me.ActionRetryAt = nowMs + retryMs;
        if (Walker(thing) is not { } walker) return false;
        var (to, node) = Toward(thing, target);
        // **The approach answers with the walk's own answer** (it returns what
        // `simWalker::WalkTo` returned), and the *order* uses that answer once, at order time, to
        // say whether it started at all.
        return node >= 0
               && walker.WalkTo(to, node, run: me.ActionSpeed > me.WalkSpeed * me.MoveSpeedScale,
                                     speed: me.ActionSpeed);
    }

    /// <summary>
    /// What drives this character about. Every character with an AI file has a walker on its mind;
    /// the player's own thing is steered by the world's walker, and in a level where the player's
    /// template names no AI file that is the only place to find one.
    /// </summary>
    internal static World.Player? Walker(WorldThing thing) =>
        thing.Mind?.Walker ?? (ReferenceEquals(thing.Place?.Player?.Thing, thing) ? thing.Place.Player : null);

    /// <summary>
    /// ACTION_ATTACK, the close-in. Stop if the fight is off, strike if
    /// the bodies are within the attack's reach, and otherwise walk at the target — its own
    /// position and its own walk node — and wait half a second before asking again.
    ///
    /// The walk is only ordered if the character can still **notice** the target (its
    /// stealth against the character's perception). A character that has lost track of what it was
    /// chasing stops rather than walking at a place it cannot see.
    /// </summary>
    static void Approach(WorldThing thing, Actor me, int nowMs, bool moving, MotionSets? motions)
    {
        var target = me.Target;
        if (target is null || me.Weapon is null || me.Has(Actor.Flag.Dead)
            || (target.IsActor && (target.Actor!.Flags & (Actor.Flag.Neutral | Actor.Flag.TalkTo)) != 0))
        {
            Stop(me);
            return;
        }

        float reach = me.Weapon.Current.Reach(attackerMoving: moving);
        if (World.Bodies.InReach(thing, target, reach))
        {
            Strike(thing, target, me.Weapon, nowMs, motions);
            return;
        }
        if (me.ActionRetryAt > nowMs) return;

        // The close-in's walk is the same approach every other order uses, so it gets the same ladder
        // — though for a character the ladder is one rung, and the walker's own node is a better
        // first answer than a search when the target has one.
        if (thing.Mind is { } mind && Sight.Notices(thing, target, Moving(target)))
        {
            // At the speed the order was given at: the run times
            // MOVESPEED, fixed when the attack was ordered.
            if (target.Mind?.Walker.Node is >= 0 and var walking)
            {
                mind.Walker.WalkTo(target.Position, walking, run: true, speed: me.ActionSpeed);
                me.ActionRetryAt = nowMs + Rules.GameRules.Current.Movement.AttackApproachRetryMs;
                return;
            }
            var (to, node) = Toward(thing, target);
            if (node >= 0)
            {
                mind.Walker.WalkTo(to, node, run: true, speed: me.ActionSpeed);
                me.ActionRetryAt = nowMs + Rules.GameRules.Current.Movement.AttackApproachRetryMs;
                return;
            }
        }
        Stop(me);
    }

    /// <summary>
    /// `rpgAction::UseThing`. Puts <paramref name="user"/> on <paramref name="target"/>
    /// with ACTION_USE and either walks at it or uses it on the spot.
    ///
    /// **Everything the cursor calls "use" comes here**, because the engine's classifier
    /// returns the use verb for a whole type rather than for a set of interesting
    /// things: `case 2: return 4` — **every** `THING_TYPE_PROP` is usable, with no flag test and no
    /// look at whether anything would happen. A door, a lever, a barrel and an invisible
    /// camera-blocking `tracker_thing` all offer the same verb, and the last of those is what a player
    /// found themselves clicking thirteen times in the monastery. That is the original's behaviour and
    /// not a gap.
    /// </summary>
    /// <param name="mods">
    /// Only <b>space</b> is asked: with it held, a use or a talk out of reach is given up
    /// where one stands (<see cref="TakeRules.StandStill"/>). Shift and the rest choose nothing —
    /// the walk or the run is the distance's (<see cref="GroundOrders.ApproachWalks"/>).
    /// </param>
    public static OrderResult Use(WorldThing user, WorldThing target, int nowMs,
                                  Anim.MotionSets? motions = null, InputModifier mods = InputModifier.None)
    {
        if (user.Actor is not { } me) return OrderResult.Refused;
        // Busy, dead or held down: not now. The same three tests the attack makes, in the same
        // order.
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained))
            return OrderResult.Busy;
        if (me.Action == ActionMode.Use && ReferenceEquals(me.Target, target)) return OrderResult.Ignored;
        if (me.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;

        // Cancel what it is doing, then ask again (this reset the fields and no more).
        if (!CancelFirst(user, me, nowMs)) return OrderResult.Busy;
        // A player's machine sends it to the host now, after the tests, and is busy 250 ms (Net.ForwardedOrders).
        if (Net.ForwardedOrders.Send(user, Net.OrderKind.Use, nowMs, Rules.GameRules.Current.Party.ForwardedOrderBusyMs[0], target)) return OrderResult.Started;
        Stop(me);
        me.Target = target;
        target.Flags |= WorldThing.Flag.Targetted;      // set on the thing being used
        me.Action = ActionMode.Use;
        me.ActionRetryAt = 0;
        // The order ticks it once immediately, which is what uses a thing you are already standing
        // next to without a frame of walking first.
        Using(user, me, nowMs, motions);
        if (me.Action != ActionMode.Use) return OrderResult.Started;      // the tick finished it
        if (TakeRules.StandStill(user, mods)) return OrderResult.Ignored;  // Space held

        // **The walk is ordered here and only here**. The tick above re-tests the reach
        // every quarter second but never re-orders — that is the use order's own shape, and it is
        // different from the take beside it, which re-walks on every one of its ticks.
        me.ActionSpeed = TakeRules.Speed(me, user, target.Position);
        me.ActionRetryAt = 0;
        // No approach, and the order says so — but it is not cancelled, so the character
        // stays on ACTION_USE until something else displaces it. Vitomir used to answer "started"
        // whatever happened, which is what a lever on a wall or a chest against one looked like,
        // since neither is standing on ground anybody can walk to.
        return Close(user, me, target, nowMs, Rules.GameRules.Current.Movement.UseRetryMs) ? OrderResult.Started : OrderResult.Refused;
    }

    /// <summary>
    /// ACTION_USE's tick, and it is **shorter than it looks**: out of
    /// reach it sets the retry and returns, and that is all. It does not re-order the walk. The
    /// walk was ordered once by <see cref="Use"/>, and the tick's whole job is to notice when the
    /// character has got there — every <see cref="UseRetryMs"/>. In reach it **stops walking
    /// first** and then uses.
    ///
    /// That is the difference between this and the take beside it, and it is worth keeping
    /// straight because the two orders look identical otherwise: the take calls the approach again
    /// on every tick and waits <see cref="TakeRetryMs"/>; this one never does. Vitomir had both of
    /// them re-walking, which threw the route away four times a second.
    ///
    /// Using is one call: the thing's script gets `clicked(thingGuid, clickerGuid, 0)`
    /// (through <see cref="World.Location.Clicked"/>). A thing with no
    /// script is used successfully and nothing happens, which is most of the props in the game.
    /// The action then ends either way — a use is not held the way a swing is.
    ///
    /// **Containers are the branch this does not take.** The use sends `THING_TYPE_CONTAINER`
    /// (type 8) to the container's open instead, which is not what
    /// this project assumed it did.
    /// </summary>
    static void Using(WorldThing thing, Actor me, int nowMs, Anim.MotionSets? motions = null)
    {
        if (me.Target is not { } target || !target.InWorld || me.Has(Actor.Flag.Dead))
        {
            Stop(me);
            return;
        }
        if (me.ActionRetryAt > nowMs) return;
        if (!World.Bodies.InReach(thing, target, Rules.GameRules.Current.Movement.UseReach))
        {
            me.ActionRetryAt = nowMs + Rules.GameRules.Current.Movement.UseRetryMs;      // and nothing else
            return;
        }
        var place = thing.Place;
        thing.Mind?.Walker.Stop();
        Stop(me);
        // **The branch on the thing's type**: a container is opened, and everything else
        // has its script clicked. They are not the same call and they do not reach the same
        // callback — see <see cref="Container.Open"/>.
        if (target.Type == World.ThingType.Container) Container.Open(target, thing, motions);
        else place?.Clicked?.Invoke(target, thing);
    }

    /// <summary>Stops a character walking, whichever walker drives it.</summary>
    public static void StopWalking(WorldThing thing) => Walker(thing)?.Stop();

    /// <summary>Whether a thing is on the move, which is what makes it easier to spot.</summary>
    static bool Moving(WorldThing thing) => thing.Mind?.Moving ?? false;

    /// <summary>ACTION_SWING: nothing to do but wait for the busy time, then stop.</summary>
    static void Swinging(WorldThing thing, Actor me, int nowMs, MotionSets? motions)
    {
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained))
            return;

        // The swing is over. The engine now asks one question and the answer is what makes combat
        // feel like combat: **is the player still holding the button on something worth hitting?**
        // If so it orders the attack again, with the modifier deciding which of the two attacks;
        // if not, the character stops. So the original is hold-to-attack, not click-once-and-fight-
        // for-ever, and a character that stops swinging has stopped being asked.
        // **On a player's machine** the yes is her next attack order to the host (the host asks nobody's
        // input but its own steered character's, so it never repeats hers). 1.1's attack there cancels first
        // The swing ended here, and the host told, order 0) and only then sends order 2,
        // so the question is asked once at the swing's end and her copy stands until the host's strike comes
        // back. Vitomir's attack sends at its top (`NetOrders.Forward`), so that cancel is made here, before it.
        if (me.Repeating is { Target: { } asked } && Net.NetOrders.Mode(thing.Place) == Net.NetMode.Client
            && Cursor.Verb(thing, asked) == CursorVerb.Attack)
            Cancel(thing);
        if (me.Repeating is { } again && again.Target is { } target
            && Cursor.Verb(thing, target) == CursorVerb.Attack
            && Attack(thing, target, me.Weapon, again.Attack, nowMs,
                      strikeNow: again.StrikeNow, motions: motions) is OrderResult.Started)
            return;

        Stop(me);
    }

    /// <summary>
    /// **The held attack, wherever the character is free** (the Fix <c>heldattack</c>, ours; off, nothing). 1.1 asks the held
    /// button only at a swing's end (<see cref="Swinging"/>); with the Fix, a character doing nothing, done casting (a cast's
    /// action lingers until the next order) or done flinching, not busy, whose player holds the attack on something to attack (<see cref="Actor.Repeating"/>, which the Controls screen's
    /// *auto repeat attack* gates), is ordered to attack it with the asked attack, as the swing's end would. The host's: on a
    /// player's machine her order goes at her swing's end, as ever.
    /// </summary>
    static void HeldAgain(WorldThing thing, Actor me, int nowMs, MotionSets? motions)
    {
        if (me.Repeating is not { Target: { } target } again) return;
        if (!(thing.Place?.Fixes ?? World.Fixes.Current).HeldAttack) return;
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained)) return;
        if (Net.NetOrders.Mode(thing.Place) == Net.NetMode.Client) return;
        if (Cursor.Verb(thing, target) != CursorVerb.Attack) return;
        Attack(thing, target, me.Weapon, again.Attack, nowMs, strikeNow: again.StrikeNow, motions: motions);
    }

    /// <summary>
    /// The strike. Plays the attack animation, leaves the character busy, and goes into
    /// ACTION_SWING — the blow itself waits for the animation's own tag.
    /// </summary>
    public static void Strike(WorldThing thing, WorldThing target, Weapon weapon, int nowMs,
                              MotionSets? motions = null) =>
        Strike(thing, target, target.Position, weapon, nowMs, motions);

    /// <summary>
    /// **Her own swing, started on her machine at her click** (<see cref="Net.OwnStrike"/>): what of the strike her input
    /// certainly causes and nothing the host decides. The walk stopped and the turn to the target, the swing's animation, the
    /// weapon's own swing noise, busy for the reload and ACTION_SWING; **not** the throw's roll (never on a player's machine), the
    /// war cry's roll, the scripts' <c>beginswing</c> or anything of the blow. Answers the animation's length (0: none played) and
    /// the noise's channel (0: none), so a guess the host does not bear out can be taken back. The host's own strike, replayed here
    /// when it comes (<see cref="Net.ClientArms"/>), then does the rest without starting it again (<see cref="StrikeStarted"/>).
    /// </summary>
    public static int StartSwing(WorldThing thing, WorldThing target, Weapon weapon, int nowMs, MotionSets? motions, out int sound)
    {
        sound = 0;
        if (thing.Actor is not { } me) return 0;
        me.Target = target;
        thing.Mind?.Walker.Stop();
        StopWalking(thing);
        thing.Face(target.Position);
        int swingMs = PlaySwing(thing, me, weapon, motions);
        me.BusyUntil = nowMs + Reload(me, weapon.Current);
        sound = Audio.Noise.Swing(weapon, thing);
        me.Action = ActionMode.Swing;
        return Math.Max(swingMs, 0);
    }

    /// <summary>
    /// The host's strike replayed on her machine for a swing <see cref="StartSwing"/> already started there
    /// (<see cref="Net.OwnStrike"/>): the rest of the strike as <see cref="Strike(WorldThing, WorldThing, Weapon, int, MotionSets?)"/>
    /// replays it — the target, <c>beginswing</c> with the swing's <paramref name="swingMs"/>, busy for the reload from now, the war
    /// cry's roll on this machine's own stream, ACTION_SWING — but not the stop, the turn, the throw's test, the animation or the
    /// swing noise, which have been: one swing, one noise.
    /// </summary>
    public static void StrikeStarted(WorldThing thing, WorldThing target, Weapon weapon, int nowMs, int swingMs) =>
        Strike(thing, target, target.Position, weapon, nowMs, null, startedMs: swingMs);

    /// <summary>
    /// The strike at a **point** with no target thing — the attack on the ground's (the order sets
    /// the point and nobody for the target, then the strike): the same swing, turned to
    /// the point, and a shot from it flies at the point (<see cref="Projectiles"/>'s aim with no
    /// target).
    /// </summary>
    public static void StrikeAt(WorldThing thing, Vector3 point, Weapon weapon, int nowMs, MotionSets? motions = null) =>
        Strike(thing, null, point, weapon, nowMs, motions);

    static void Strike(WorldThing thing, WorldThing? target, Vector3 point, Weapon weapon, int nowMs, MotionSets? motions,
                       int startedMs = -1)
    {
        if (thing.Actor is not { } me) return;
        var attack = weapon.Current;
        me.Target = target;
        if (target is null) me.ActionPoint = point;
        bool started = startedMs >= 0;           // her own swing, started on her machine at her click (StartSwing)

        // **Stop walking, then turn to face the target, before the swing plays.** The strike does
        // both, in that order, and it is easy to miss: the stop stops a character that is walking,
        // and then the facing snaps its matrix round to look at the target's position (or, with no
        // target thing, at the point the order was given against).
        //
        // Without it a character swings in whatever direction its last step happened to point — and
        // since the approach walks *at* the target and then stops wherever the reach test first
        // passes, that direction is frequently away from what it is hitting. Play showed exactly
        // that: they faced away from their targets when swinging. The stop matters
        // as much as the turn: a walk left running would turn the character back towards where it
        // was going on the very next frame, and the swing would face the wrong way again.
        if (!started)
        {
            thing.Mind?.Walker.Stop();
            thing.Face(point);
        }

        // **The weapon's special move**: against a character, a `THROW` attack can become
        // a grab and a throw on a roll of its chance (<see cref="SpecialMove"/>) — and then none of
        // the swing below happens: no attack animation, no reload, no swing noise, no war cry.
        if (!started && target is not null && SpecialMove.Try(thing, target, weapon, nowMs)) return;

        int swingMs = started ? startedMs : PlaySwing(thing, me, weapon, motions);
        // On a host every strike that is not a throw goes to the players in the place, after the swing
        // (the strike message with its last byte 0), and each of their machines strikes it again.
        Net.NetArms.Struck(thing, target, point, weapon);
        // A swing at a thing tells the swinger's scripts `beginswing`.
        if (target is not null) thing.Place?.ItemScripts?.BeginSwing?.Invoke(thing, target, swingMs);

        // **How long the character is busy is the attack's RELOADTIME, for a swing too** — divided
        // by the character's attack speed for a swing, as it is for a shot not (the
        // ranged flag picks `now + reload`; everything else `now + reload / attack speed`, truncated).
        // Vitomir used the swing animation's length here once, which is STRIKETIME:
        // the same number for most weapons, but not for 60 of the 200 melee weapons. A
        // Cappadocian's scythe strikes in 1 second and reloads in 1.5: in 1.1 a swing holds it for
        // 1.5 seconds and one comes every 1.6 to 1.8 (a Cappadocian in the
        // Monastery), where Vitomir's held it for 1.0 and came every 1.1 to 1.2.
        me.BusyUntil = nowMs + Reload(me, attack);

        // The strike's tail: the weapon's own swing, then an even chance of a war cry.
        // The chance is the engine's one rand(), drawn whether or not anything is heard.
        if (!started) Audio.Noise.Swing(weapon, thing);
        // The war cry is each machine's own: the strike plays it with the foley's relay flag 0, which a
        // host does not relay — every player's machine strikes again and rolls its own, on the
        // launch's stream there (Net.StrikeRolls). Her own predicted swing rolls it here too, when the host's strike comes.
        if (Net.StrikeRolls.For(thing) is { } rng && rng.Unit() < Rules.GameRules.Current.Combat.VoxAttackChance)
        {
            using var _ = Net.HostSeen.Local();
            Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxAttack);
        }
        me.Action = ActionMode.Swing;
    }

    /// <summary>How long a strike leaves its striker busy: the reload, divided by the attack speed for a swing.</summary>
    static int Reload(Actor me, WeaponAttack attack) => attack.Is(WeaponFlag.Ranged)
        ? attack.ReloadTime
        : (int)(attack.ReloadTime / Math.Max(me.AttackSpeedScale, MinAttackSpeed));

    /// <summary>The swing's animation played on the action channel: its length, 0 with no model to play it on.</summary>
    static int PlaySwing(WorldThing thing, Actor me, Weapon weapon, MotionSets? motions)
    {
        var attack = weapon.Current;
        int swingMs = 0;                  // the swing time: 0 with no model to play it on (beginswing's third)
        if (motions is not null && thing.Animation is { } anim)
        {
            // The mode is the attack record's melee type offset from the block of attack modes, and
            // the record's `0x80` flag asks for the set's `special` variant of it. A set with nothing
            // for that mode falls back to `swordslash`, keeping the special flag. The animation is
            // played at whatever rate makes it last STRIKETIME, scaled by the character's own attack
            // speed for a melee swing — a rate of nothing means the nominal thirty frames a second.
            // Playing it on the action channel is what restarts it every swing.
            // A torch in the off hand swings the thirteen `util_` variants instead.
            int mode = weapon.AttackMode(weapon.CurrentAttack, Utility.Stance(me));
            bool special = attack.Is(WeaponFlag.SpecialAnimation);
            int ms = attack.StrikeTime > 0f
                ? (int)(attack.StrikeTime * Weapon.MillisecondsPerSecond
                        / (attack.Is(WeaponFlag.Ranged) ? 1f : Math.Max(me.AttackSpeedScale, MinAttackSpeed)))
                : 0;
            if ((swingMs = Swing(anim, motions, mode, ms, special)) < 0)
                swingMs = Swing(anim, motions, Weapon.FallbackAttackMode, ms, special);
        }
        return swingMs;
    }

    /// <summary>How often a strike comes with <c>vox_attack</c> (a double 0.5).</summary>
    public const float VoxAttackChance = 0.5f;

    /// <summary>
    /// Plays one attack mode, or reports that the motion set has nothing for it. Returns the
    /// animation's length in milliseconds, or -1 when the mode is missing.
    /// </summary>
    static int Swing(Anim.ThingAnimation anim, MotionSets motions, int mode, int milliseconds, bool special)
    {
        if (motions.Find(anim.Set, mode, special) is not { } found) return -1;
        float rate = milliseconds > 0
            ? Anim.ThingAnimation.Milliseconds(found.Animation) / (float)milliseconds * Anim.ThingAnimation.Rate
            : Anim.ThingAnimation.Rate;
        return anim.Play(found.Mode, found.Animation, motions, onMotion: false, rate: rate);
    }

    /// <summary>A character whose attack speed has been scaled to nothing would divide by zero.</summary>
    public const float MinAttackSpeed = 0.01f;

    /// <summary>
    /// A keyframe tag came round. `Fire` and `Strike` are the same handler in the engine, and the
    /// attack's own ranged flag decides which half runs; everything else is somebody else's.
    /// </summary>
    public static bool IsStrikeTag(NadTag tag) => tag.Type is StrikeTag.Fire or StrikeTag.Strike;

    /// <summary>The keyframe tag numbers the SDK names, for the two this file cares about.</summary>
    public static class StrikeTag { public const uint Fire = 4, Strike = 5; }

    /// <summary>
    /// **Cancel whatever the character is doing** — which every new order
    /// calls first (the cast, the feed, the attack, the walk). Unless <paramref name="force"/>d it
    /// does nothing at all to a character that is busy, dead or restrained — so a new order cannot
    /// cut a playing swing or cast short, and a neck victim cannot be ordered out of a feed. Then by
    /// mode:
    /// <list type="bullet">
    /// <item><b>move</b> (1): the walk stopped, the ring taken away, and back to nothing
    /// with target, pending discipline and busy time cleared (<see cref="MoveOrder.Cancelled"/>);</item>
    /// <item><b>attack / swing</b> (2, 5): a weapon swap ended when the weapon's mode is THROWN;
    /// back to nothing, the busy time cleared — **the target is
    /// kept** (the action's target is not touched on this branch);</item>
    /// <item><b>feeding</b> (8, 11): the feed is stopped (<see cref="Feeding.Stop"/>);</item>
    /// <item><b>fed on</b> (9, 12): the feeder's feed is stopped, if the feeder is still a
    /// character; this one's action animation stops and its `ef_restrain` comes off;</item>
    /// <item><b>casting</b> (13): a temporary (scroll) discipline waiting to fire is let go;</item>
    /// <item><b>still</b> (20): `actorunstill` is sent;</item>
    /// <item><b>anything else</b>: back to nothing, target, pending discipline and busy time
    /// cleared — and <b>`ef_restrain` removed</b>, whatever the mode was.</item>
    /// </list>
    /// </summary>
    /// <param name="release">Passed to the feed's stop (its own argument), which a forced stop from a kill sets.</param>
    public static void Cancel(WorldThing thing, bool force = false, bool release = false)
    {
        if (thing.Actor is not { } me) return;
        int now = thing.Place?.Milliseconds ?? 0;
        if (!force && (now < me.BusyUntil || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained))) return;
        if (me.Action == ActionMode.None) return;                         // case 0: nothing — and nothing sent
        CancelArm(thing, me, release);
        // On a player's machine, a cancel not of a feed's release (its first argument 0) of the steered
        // character tells the host — order 0, stop.
        if (!release) Net.ForwardedOrders.Cancelled(thing);
    }

    /// <summary>The branches of the cancel's switch on the action mode (all but case 0).</summary>
    static void CancelArm(WorldThing thing, Actor me, bool release)
    {
        switch (me.Action)
        {
            case ActionMode.Move:
                MoveOrder.Cancelled(thing, me);
                return;
            case ActionMode.Attack:
            case ActionMode.Swing:
                // An attack in a THROWN mode given up before it flies — the grenade, the holy water,
                // the knives the quick-use strip swaps in — gives the hands back (the swap's end,
                // read in a comparison with 1.1), as the throw's own tag does once it has flown.
                if (me.Weapon?.Current.Is(WeaponFlag.Thrown) == true) WeaponOverride.Cancel(thing);
                me.Action = ActionMode.None;
                me.BusyUntil = 0;
                return;
            case ActionMode.Feeding:
            case ActionMode.WristFeeding:
                Feeding.Stop(thing, release);
                return;
            case ActionMode.FedOn:
            case ActionMode.WristFedOn:
                if (me.Target is { IsActor: true } feeder && feeder.Place == thing.Place) Feeding.Stop(feeder, release);
                thing.Animation?.StopAction();
                EffectRuntime.Remove(thing, "ef_restrain");
                return;
            case ActionMode.Cast:
                Cast.Discard(thing);
                me.Action = ActionMode.None;
                me.BusyUntil = 0;
                return;
            case ActionMode.Still:
                thing.Place?.ScriptObjects?.Unstill(thing);
                me.Action = ActionMode.None;
                me.BusyUntil = 0;
                return;
            default:
                me.Action = ActionMode.None;
                me.Target = null;
                me.PendingDiscipline = null;
                me.BusyUntil = 0;
                EffectRuntime.Remove(thing, "ef_restrain");
                return;
        }
    }

    /// <summary>
    /// `ActorStill`: whatever it was doing cancelled, walking stopped, ACTION 20, and
    /// `actorstill` to its scripts. It stays so until an order cancels it — which sends
    /// `actorunstill`. Always taken.
    /// </summary>
    public static OrderResult Still(WorldThing thing)
    {
        if (thing.Actor is not { } me) return OrderResult.Refused;
        Cancel(thing);
        StopWalking(thing);
        me.Action = ActionMode.Still;
        thing.Place?.ScriptObjects?.Stilled(thing);
        return OrderResult.Started;
    }

    /// <summary>
    /// Plays a motion-set mode on the **action** channel at thirty frames a second — the engine's
    /// `PlayMotion(mode, 0, 30.0)` (which always restarts). Answers
    /// the animation's length in milliseconds, or −1 when the character has no model or motion set
    /// or its set has no such mode (the lookup has no fallback).
    /// </summary>
    public static int PlayAction(WorldThing thing, int mode, float rate = Anim.ThingAnimation.Rate)
    {
        if (thing.Animation is not { } anim || thing.Place?.Motions is not { } motions) return -1;
        return anim.Play(mode, motions, onMotion: false, rate: rate);
    }

    /// <summary>Puts the character back to doing nothing, forgetting its target.</summary>
    /// <summary>
    /// The head of the move order (`rpgAction::MoveTo`) — what a click on the ground asks
    /// before anything walks. **Not now** (false) while the character is busy, dead or
    /// held; otherwise whatever it is doing is cancelled — a feed lets go and plays its
    /// release, which makes the character busy for it — and then the same question is asked again.
    /// So the click that ends a feed does not also walk, and nothing walks until the release has
    /// played: before this, play had Christof walking off mid wipe-of-the-mouth.
    /// </summary>
    public static bool ReadyToMove(WorldThing thing)
    {
        if (thing.Actor is not { } me) return true;
        int now = thing.Place?.Milliseconds ?? 0;
        if (now < me.BusyUntil || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained)) return false;
        return MoveOrder.Ready(thing);          // the cancel, unless already moving
    }

    public static void Stop(Actor me)
    {
        me.Action = ActionMode.None;
        me.Target = null;
        me.ActionRetryAt = 0;
    }

    /// <summary>
    /// The head every order shares after its own refusals
    /// (**cancel** what the character is doing (not forced) and ask the
    /// busy, dead and held questions again. Answers whether the order may go on — a feed let go
    /// has made the character busy for its release, and the order is refused for now.
    /// </summary>
    public static bool CancelFirst(WorldThing thing, Actor me, int nowMs)
    {
        Cancel(thing);
        return !(me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained));
    }

    /// <summary>
    /// What the player is still asking for. The engine reads this off its own input globals at the
    /// end of every swing (the mouse button still down, the thing still
    /// under the cursor, the modifier still held); Vitomir has the input layer put it here instead,
    /// so nothing in the engine has to know what a mouse is. Null means nobody is asking.
    /// </summary>
    /// <param name="Target">The thing under the cursor while the button is down.</param>
    /// <param name="Attack">Which of the weapon's two attacks the modifier is asking for.</param>
    /// <param name="StrikeNow">The engine's flag 8: swing where you stand rather than closing in.</param>
    public sealed record Asking(WorldThing Target, int Attack = 0, bool StrikeNow = false);
}
