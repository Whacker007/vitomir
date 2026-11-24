// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Two arms of the ground click that Vitomir had not written: Alt fires a ranged weapon at the floor
// and Space turns in place (the ground click, the attack on a point, the move
// order), read.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// The ground click's Alt and Space arms, and the attack on a point they share with nothing else.
/// </summary>
public static class ClickOrders
{
    /// <summary>
    /// Whether a click on the ground **fires at it**: Alt (bit 0x4) held, nothing on the
    /// cursor, and the weapon in hand's current attack <see cref="WeaponFlag.Ranged"/> (the attack
    /// record's flag byte of the attack's record, bit 1). This comes before the test
    /// that the ground is walkable, so a shot can go at a floor nobody could walk on.
    /// </summary>
    public static bool FiresAtGround(WorldThing who, InputModifier mods) =>
        (mods & InputModifier.Alt) != 0 && who.Actor is { Held: null, Weapon: { } weapon }
        && weapon.Current.Is(WeaponFlag.Ranged);

    /// <summary>The attack a ground shot uses: the second with Shift (bit 0x1) held, as on a target.</summary>
    public static int AttackIndex(InputModifier mods) => (mods & InputModifier.Shift) != 0 ? 1 : 0;

    /// <summary>
    /// The weapon on the pointer when it is one to throw: an item that is a
    /// weapon thing (type 5) whose **current** attack is <see cref="WeaponFlag.Thrown"/>
    /// (`0x20` in the flag byte of the attack's record) — a grenade, a satchel charge, a
    /// throwing knife. Null otherwise.
    /// </summary>
    public static Weapon? ThrownOnPointer(WorldThing who) =>
        who.Actor?.Held is { Weapon: { } weapon } && weapon.Current.Is(WeaponFlag.Thrown) ? weapon : null;

    /// <summary>
    /// Whether a click on the ground **throws the weapon on the pointer**: Alt
    /// (bit 0x4) held and <see cref="ThrownOnPointer"/>. It is the pointer's first question, before
    /// the put-down that anything else on it gets, and — like the shot — before the walkable test.
    /// </summary>
    public static bool ThrowsFromPointer(WorldThing who, InputModifier mods) =>
        (mods & InputModifier.Alt) != 0 && ThrownOnPointer(who) is not null;

    /// <summary>
    /// The pointer's weapon **taken in hand** for the throw (the weapon swap,
    /// <see cref="WeaponOverride.Override"/> — the off hand kept, the
    /// fists shown). It stays on the pointer too; the throw hands the hand back and spends one from
    /// it (<see cref="Projectiles.Thrown"/>). A click on a thing does this for **any** attack
    /// click, Alt or not (nothing is asked of the modifiers), then orders the attack on the thing
    /// as ever. Nothing when the pointer holds no weapon to throw, or a swap is already in
    /// force (the swap's own test).
    /// </summary>
    public static bool TakeThrownInHand(WorldThing who) =>
        ThrownOnPointer(who) is { } weapon && WeaponOverride.Override(who, weapon, clearOffhand: false, hideFists: false);

    /// <summary>
    /// The Alt-click's throw at the ground: <see cref="TakeThrownInHand"/>, then
    /// the attack on the point (<see cref="AttackPoint"/>) with the second attack when Shift
    /// is held — the weapon now in hand is the pointer's, so the strike throws it at the point.
    /// </summary>
    public static OrderResult ThrowFromPointer(WorldThing who, Vector3 at, InputModifier mods, int nowMs,
                                               MotionSets? motions = null)
    {
        TakeThrownInHand(who);
        return AttackPoint(who, at, AttackIndex(mods), nowMs, motions, mods);
    }

    /// <summary>
    /// The attack on a point. Busy, dead or held: not now. Ethereal (flag
    /// 0x20000) or with no weapon: refused. Otherwise whatever the character was doing is cancelled
    /// and asked again; then it stops walking, the attack is chosen when the
    /// weapon has that many, it turns to the point, the point becomes the
    /// order's with no target thing (nobody for the target), and the strike plays at once
    /// — there is no approach, and the shot the strike looses flies at the point.
    /// A busy steered character's refusal while the game is paused puts "RPG_CANTDOACTION" in the
    /// message box (<see cref="PausedOrders"/>).
    /// **On a network client** the order goes to the host after the cancel
    /// and the second busy test — order 2 with no thing and the point, the click's mask (<paramref name="mods"/>)
    /// and the attack — and answers 4 without stopping, turning or striking here; the host's arm of
    /// order 2 with no thing makes the same attack on its own copy (`Net.ServerOrders`).
    /// </summary>
    public static OrderResult AttackPoint(WorldThing attacker, Vector3 at, int attack, int nowMs, MotionSets? motions = null,
                                          InputModifier mods = InputModifier.None)
    {
        if (attacker.Actor is not { } me) return OrderResult.Refused;
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained))
        {
            PausedOrders.Busy(attacker);                                  // RPG_CANTDOACTION while paused
            return OrderResult.Busy;
        }
        if (me.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (me.Weapon is not { } weapon) return OrderResult.Refused;
        if (!Actions.CancelFirst(attacker, me, nowMs)) return OrderResult.Busy;
        // A client sends it to the host (kind 2, no thing, the point).
        if (Net.NetOrders.Forward(attacker, Net.OrderKind.Attack, null, at, (int)mods, (byte)attack)) return OrderResult.Started;
        Actions.StopWalking(attacker);
        weapon.Select(attack);
        Diagnostics.Journal.Current?.Write(nowMs, Diagnostics.JournalKind.Order,
            ("what", "attack the ground"), ("who", Diagnostics.Watch.Name(attacker)),
            ("weapon", weapon.Name), ("attack", weapon.CurrentAttack), ("at", at));
        Actions.StrikeAt(attacker, at, weapon, nowMs, motions);
        return OrderResult.Started;
    }

    /// <summary>
    /// The move order's Space arm: after its busy test (not now while busy, dead or
    /// held) and before anything walks, **bit 0x8 turns the character to face the point**
    /// (it faces the point) and the order answers 0 — nothing refused, nothing started. The walk,
    /// the ring and the run are not reached.
    /// </summary>
    public static OrderResult TurnInPlace(WorldThing who, Vector3 at, int nowMs)
    {
        if (who.Actor is { } me && (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained)))
            return OrderResult.Busy;
        who.Face(at);
        return OrderResult.Ignored;
    }

    /// <summary>Whether a ground click turns in place rather than walking: Space (bit 0x8).</summary>
    public static bool Turns(InputModifier mods) => (mods & InputModifier.Space) != 0;
}
