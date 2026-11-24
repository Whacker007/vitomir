// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host carrying out an order a client sent: the order handler's switch, into the same order functions a
// click on the host's own machine calls. The client's word is not taken for anything but *what was
// asked*: the target is looked up here, and every rule — busy, dead, in reach, allowed — is the
// order function's own (the server's authority is the whole of the anti-cheat).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public static class ServerOrders
{
    /// <summary>Whether a thing is `THING_TYPE_ACTOR` or `THING_TYPE_PLAYER` — the handler's first test and its feed and unstake arms.</summary>
    static bool Person(WorldThing? t) => t?.Type is ThingType.Actor or ThingType.Player;

    /// <summary>
    /// The order carried out on <paramref name="me"/>. Answers what the order function
    /// said, or <see cref="OrderResult.Refused"/> for an order the handler itself turns away.
    /// </summary>
    public static OrderResult Apply(Location place, WorldThing me, NetOrder order, int nowMs)
    {
        if (!Person(me) || me.Actor is not { } actor) return OrderResult.Refused;
        var target = order.Target != 0 ? place.Thing(order.Target) : null;
        var mods = (InputModifier)(order.Flags & 0xff);
        switch (order.Kind)
        {
            case OrderKind.Stop:
                // Case 0: cancel, then stop walking.
                Actions.Cancel(me);
                Actions.StopWalking(me);
                return OrderResult.Started;

            case OrderKind.Move:
            {
                // Case 1: the move order — its own gate, then the walk or the run at the
                // character's own MOVESPEED, as the click on the host's machine does.
                // The client's click word, forwarded, never carries 0x100: somebody
                // standing there refuses it before the cancel (MoveOrder.Head).
                if (place.WalkerFor(me) is { } asked && MoveOrder.Head(me, asked, order.Point) == 1) return OrderResult.Refused;
                if (actor.Dead || !Actions.ReadyToMove(me)) return OrderResult.Busy;
                if (place.WalkerFor(me) is not { } walker || place.Walk is not { } map) return OrderResult.Refused;
                int node = map.Find(order.Point, 256f);
                if (node < 0) return OrderResult.Refused;
                actor.Repeating = null;
                bool run = (order.Flags & MoveRun) != 0;
                float speed = (run ? walker.RunSpeed : walker.WalkSpeed) * actor.MoveSpeedScale;
                if (!walker.WalkTo(order.Point, node, run: run, speed: speed)) return OrderResult.Refused;
                MoveOrder.Taken(me);                                     // ACTION 1
                return OrderResult.Started;
            }

            case OrderKind.Attack:
            {
                // Case 2: the weapon named is taken in hand if it is not the one already there,
                // then — with no thing named (the GUID tested) — the attack on the
                // point (a client's shot at the ground), else the attack on the thing.
                if (order.Target == 0)
                    return ClickOrders.AttackPoint(me, order.Point, order.AttackIndex, nowMs, place.Motions, mods);
                if (target is null) return OrderResult.Refused;
                if (target.Type is not (ThingType.Actor or ThingType.Player or ThingType.Container)) return OrderResult.Refused;
                // **One order, one attack**: the host does not repeat it. 1.1's end-of-swing question
                // is asked only of the character the machine steers, so on the host her swing ends
                // there; her own machine asks it of her copy at the end of the swing the host's strike put it in — her
                // *auto repeat attack* option (option bit 4, her own masquerade.ini), her button, what is under her
                // pointer, Space — and a yes is her next attack order, which comes here as this one.
                // Earlier, the host set her `Repeating` here and swung again by itself until she walked.
                var weapon = actor.Weapon;
                return Actions.Attack(me, target, weapon, order.AttackIndex, nowMs, motions: place.Motions, mods: mods);
            }

            case OrderKind.Unstake:
                // Case 0xf: the unstake order, only on a character.
                return target is null ? OrderResult.Refused : Stake.Order(me, target, nowMs);

            case OrderKind.Take:
                // Case 3: only an item, a weapon or armour, and one lying in the world.
                if (target is not { Type: ThingType.Item or ThingType.Weapon or ThingType.Armor, InWorld: true }) return OrderResult.Refused;
                return Actions.Fetch(me, target, nowMs, mods);

            case OrderKind.Use:
                if (target is null) return OrderResult.Refused;
                return Actions.Use(me, target, nowMs, place.Motions);

            case OrderKind.Drop:
                // Case 0xe: only what the orderer carries (the target's owner or holder must be them).
                if (actor.Held is not { } held) return OrderResult.Refused;
                return Actions.Put(me, held, order.Point, nowMs, mods);

            case OrderKind.StopFeeding:
                // Case 0x15: only while feeding (modes 8 and 11).
                if (actor.Action is not (ActionMode.Feeding or ActionMode.WristFeeding)) return OrderResult.Ignored;
                Actions.Cancel(me);
                return OrderResult.Started;

            case OrderKind.Feed:
                // Case 7: only on an actor or a player, then the feed order.
                if (!Person(target)) return OrderResult.Refused;
                return Feeding.Order(me, target!) switch
                {
                    4 => OrderResult.Started, 2 => OrderResult.Busy, 0 => OrderResult.Ignored, _ => OrderResult.Refused,
                };

            case OrderKind.Aim:
            {
                // Case 0x1e, the server's copy of held automatic fire (`Rpg.HeldFire`): with a
                // **`REPEATABLE`** attack in hand — the host's arm tests the attack's flag 0x4, not the ranged
                // flag 0x1 this read until then — face the thing or the point and, unless already in the
                // attack (action 5), start it. In action 5 the animation jumps back to its
                // `RepeatTo` tag instead (`HeldFire.JumpBack`), with no busy time. **A point with no
                // thing swings at it**: the point is the order's (with nobody for its target) and the
                // strike plays there (`ServerAim.AtPoint`) — an earlier build only turned.
                // **With a thing** it strikes at once and flags the thing `TARGETTED` — Vitomir gave the
                // attack order until then. Both arms are `ServerAim.Aim`.
                if (actor.Weapon is not { } w || !w.Current.Is(WeaponFlag.Repeatable)) return OrderResult.Refused;
                return ServerAim.Aim(me, w, target, order.Point, nowMs, place.Motions);
            }

            default:
                return OrderResult.Refused;
        }
    }

    /// <summary>
    /// A discipline cast from a player's machine (1.1's message 0x18): the caster must be an
    /// actor or a player. A scroll the caster holds is read as the cast (the item-type-0xb arm);
    /// otherwise the discipline is found by name among the caster's own, made the current one if it is
    /// not, and cast through the one cast order — every
    /// rule the single-player cast has.
    /// </summary>
    public static OrderResult Cast(Location place, WorldThing me, NetCast cast) => Cast(place, me, cast, out _);

    /// <summary>
    /// <see cref="Cast(Location, WorldThing, NetCast)"/>, saying why (the answer to a numbered cast, <see cref="OwnCast"/>): a
    /// thing named that is not in this world is <see cref="CastWhy.Gone"/> when the cast is refused for want of a target.
    /// </summary>
    public static OrderResult Cast(Location place, WorldThing me, NetCast cast, out CastWhy why)
    {
        why = CastWhy.Unknown;
        if (!Person(me) || me.Actor is not { } actor) return OrderResult.Refused;
        var target = cast.Target != 0 ? place.Thing(cast.Target) : null;
        var mods = (InputModifier)(cast.Mods & 0xff);
        OrderResult result;
        if (cast.Scroll != 0 && place.Thing(cast.Scroll) is { Item: { } scroll } held
            && ReferenceEquals(held.Owner ?? held.Holder, me))
            result = Rpg.Cast.Order(me, target, cast.Point, null, mods, scroll, out why);
        else
        {
            if (actor.Disciplines[cast.Discipline] is not { } inst) return OrderResult.Refused;
            if (!ReferenceEquals(actor.Disciplines.Current, inst)) DisciplineBook.Select(me, inst);
            result = Rpg.Cast.Order(me, target, cast.Point, null, mods, null, out why);
        }
        if (why == CastWhy.NoTarget && cast.Target != 0 && target is null) why = CastWhy.Gone;
        return result;
    }

    /// <summary>The move order's flag bit for "run" (ours: 1.1's order carries the walker's own flags word, unread).</summary>
    public const int MoveRun = 0x100;
}
