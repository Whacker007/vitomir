// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The stake** — the stake and its end, and the unstake order
/// with its tick and the pull itself.
///
/// A **critical** (margin over 60) against a **vampire** (`ACTOR_TYPE` 0) with a staking attack —
/// a melee attack with <see cref="WeaponFlag.CanStake"/> or a
/// <see cref="Projectiles.Flag.Staking"/> shot (every arrow is one) — rolls
/// <c>(int)(rand01 x 100)</c> against the victim's effect kind 21 (`STAKEPROTECTION`); a roll
/// **above** it stakes (strictly), and the blow does **no damage at
/// all**. Not on a network client. A melee stake is left in the victim: the
/// weapon leaves the attacker's pack, the hand goes back to the character's own weapon
/// (the weapon set to nothing) and the stake itself is destroyed.
///
/// Staked: `THING_AF_STAKED`; the victim turns to face whoever staked it, when that was a
/// character; `MOTION_STAKED` (256) on the action channel at 30; the `staked` foley
/// (0x6f); `ef_restrain`; **action 0x13**, with the time; `ef_staked`. A player's portrait
/// goes to `head_staked`, which Vitomir's heads read off the flag
/// (<see cref="Ui.PartyHeads"/>).
///
/// **A stake comes out by itself after twenty seconds** (the action tick's case 0x13: <c>20000 &lt; now −
/// stakedAt</c>): a character still alive is unstaked; one that died meanwhile is only
/// put back to action 0 with its target, pending discipline and busy time cleared.
///
/// Unstaked: the flag off; `MOTION_UNSTAKED` (151) at 30, busy for it; the `unstaked`
/// foley (0x70); action 0; `ef_restrain` and `ef_staked` off.
///
/// Pulling it out for somebody: the unstaker must be free (2), not ethereal (1), not the
/// victim (0), not already on it (0); the victim must be a character (1) and staked (1). Then the
/// cancel and the gate again, and **action 0xF** with the victim as the target
/// (`THING_FLAG_TARGETTED` on it), ticked at once. The tick walks up to within **60 less
/// the unstaker's radius** and asks again every 250 ms; a victim no longer staked ends it
/// (stop and cancel), a victim gone ends it forced. In reach: the unstaker's own action
/// cancelled, each turned to face the other, the unstaker put **60 in front of the victim**, the victim
/// unstaked and busy for `MOTION_UNSTAKED`, the unstaker playing `MOTION_UNSTAKE` (150) at 30, and the
/// `unstaked` foley on the victim.
///
/// The stake leaving the pack makes its `remove` noise (the release is passed its sound flag;
/// built). Ours: a
/// stake that was the attacker's own template weapon (`ACTOR_WEAPON`) rather than an item is left in
/// its hand (the engine destroys it and points the hand at the destroyed thing).
/// </summary>
public static class Stake
{
    /// <summary>`MOTION_STAKED` (Codex.java), played by the stake as mode 0x100.</summary>
    public const int StakedMode = 256;
    /// <summary>`MOTION_UNSTAKE`, the one pulling it out (mode 0x96).</summary>
    public const int UnstakeMode = 150;
    /// <summary>`MOTION_UNSTAKED`, the one it comes out of (mode 0x97).</summary>
    public const int UnstakedMode = 151;
    /// <summary>How long a stake stays in before it comes out by itself (20000).</summary>
    public const int StakedForMs = 20_000;
    /// <summary>How close an unstaker stands: 60 — less its own radius for the reach, in front of the victim for the pull.</summary>
    public const float UnstakeReach = 60f;
    /// <summary>How long the unstake approach waits before asking again: 250 ms (0xfa).</summary>
    public const int UnstakeRetryMs = 250;
    /// <summary>The roll's scale: <c>rand01 x 100</c>.</summary>
    public const float RollScale = 100f;

    /// <summary>
    /// The roll a staking critical makes: the victim's
    /// `STAKEPROTECTION` fold is taken first, then one <c>rand()</c>, truncated to a whole
    /// number out of a hundred; the stake goes in when the roll is **above** the protection.
    /// </summary>
    public static bool Rolls(WorldThing victim, Rng rng)
    {
        float protection = victim.Effects.Value(EffectKind.StakeProtection);
        int roll = (int)(rng.Unit() * Rules.GameRules.Current.Stake.StakeRoll);
        return roll > protection;
    }

    /// <summary>
    /// The melee half: after a critical has doubled the blow, a
    /// <see cref="WeaponFlag.CanStake"/> attack on a vampire rolls to stake. Answers true when it
    /// went in — the blow then does nothing else, not even its noise.
    /// </summary>
    public static bool Melee(WorldThing attacker, WorldThing victim, Weapon weapon, Blow blow, Rng rng)
    {
        if (!blow.Critical || Net.NetOrders.Mode(victim.Place) == Net.NetMode.Client) return false;
        if (victim.Actor is not { Type: ActorType.Vampire } || !weapon.Current.Is(WeaponFlag.CanStake)) return false;
        if (!Rolls(victim, rng)) return false;
        LeaveIn(attacker, weapon);
        In(victim, attacker);
        return true;
    }

    /// <summary>
    /// The shot half: a critical from a <see cref="Projectiles.Flag.Staking"/>
    /// shot on a vampire rolls to stake, staked by the shot's owner when that is a character. Answers
    /// true when it went in — the shot is then spent with no damage.
    /// </summary>
    public static bool Shot(WorldThing shot, WorldThing? owner, WorldThing victim, bool critical, Rng rng)
    {
        if (!critical || Net.NetOrders.Mode(victim.Place) == Net.NetMode.Client) return false;
        if (victim.Actor is not { Type: ActorType.Vampire } || (shot.ProjectileFlags & Projectiles.Flag.Staking) == 0) return false;
        if (!Rolls(victim, rng)) return false;
        In(victim, owner is { Type: ThingType.Actor or ThingType.Player } ? owner : null);
        return true;
    }

    /// <summary>
    /// The stake left in the victim: out of the attacker's pack, the hand back
    /// to its own weapon, and gone.
    /// </summary>
    static void LeaveIn(WorldThing attacker, Weapon weapon)
    {
        if (attacker.Actor is not { } actor) return;
        if (WeaponOverride.Active(actor)) WeaponOverride.Cancel(attacker);
        var item = actor.Inventory.Items.FirstOrDefault(i => ReferenceEquals(i.Weapon, weapon));
        if (item is null) return;                                                           // ours: see the class
        // The release is passed its sound flag here (1), so the stake says `remove` as it leaves the
        // pack — from its own set, on the attacker — unless it goes at no charges and has none.
        if ((item.Flags & ItemFlags.DestroyedAtZeroCharges) == 0 || item.Amount > 0)
            Items.Heard(attacker, item, Audio.FoleyEvent.Remove);
        actor.Inventory.Remove(item);
        item.Destroyed = true;
        if (ReferenceEquals(actor.Weapon, weapon))
        {
            actor.Weapon = actor.DefaultWeapon;
            attacker.Locomotion?.Reanimate();
        }
    }

    /// <summary>
    /// **Staked**. Nothing happens to a character already staked. <paramref name="by"/> is
    /// whoever drove it in, when that was a character.
    /// </summary>
    public static void In(WorldThing victim, WorldThing? by)
    {
        if (victim.Actor is not { } me || me.Has(Actor.Flag.Staked)) return;
        me.Flags |= Actor.Flag.Staked;
        if (by is { Type: ThingType.Actor or ThingType.Player }) victim.Face(by);
        Actions.PlayAction(victim, StakedMode);
        Audio.Noise.Foley(victim, Audio.FoleyEvent.Staked);
        EffectRuntime.Apply(victim, "ef_restrain", 0);
        me.Action = ActionMode.Staked;
        me.ActionStartedAt = victim.Place?.Milliseconds ?? 0;
        EffectRuntime.Apply(victim, "ef_staked", 0);
        Diagnostics.Journal.Current?.Write(me.ActionStartedAt, Diagnostics.JournalKind.Order, ("what", "staked"),
            ("who", Diagnostics.Watch.Name(victim)), ("by", by is null ? null : Diagnostics.Watch.Name(by)));
    }

    /// <summary>
    /// **Unstaked**. Nothing happens to a character that is not staked.
    /// </summary>
    public static void Out(WorldThing victim)
    {
        if (victim.Actor is not { } me || !me.Has(Actor.Flag.Staked)) return;
        me.Flags &= ~Actor.Flag.Staked;
        int ms = Actions.PlayAction(victim, UnstakedMode);
        Audio.Noise.Foley(victim, Audio.FoleyEvent.Unstaked);
        me.Action = ActionMode.None;
        if (ms > 0) me.BusyUntil = (victim.Place?.Milliseconds ?? 0) + ms;
        EffectRuntime.Remove(victim, "ef_restrain");
        EffectRuntime.Remove(victim, "ef_staked");
    }

    /// <summary>
    /// ACTION 0x13's tick: after <see cref="StakedForMs"/> the stake comes out
    /// by itself, or — for a character that died staked — the action is simply cleared.
    /// </summary>
    internal static void Staked(WorldThing thing, Actor me, int nowMs)
    {
        if (nowMs - me.ActionStartedAt <= Rules.GameRules.Current.Stake.StakeDurationMs) return;
        if (!me.Has(Actor.Flag.Dead)) { Out(thing); return; }
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
        me.BusyUntil = 0;
    }

    /// <summary>
    /// The unstake order, with the engine's own answers: 2 not now, 1 refused, 0 nothing
    /// to do, 4 taken.
    /// </summary>
    public static OrderResult Order(WorldThing unstaker, WorldThing victim, int nowMs)
    {
        if (unstaker.Actor is not { } me) return OrderResult.Refused;
        if (me.BusyUntil > nowMs || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained)) return OrderResult.Busy;
        if (me.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (ReferenceEquals(unstaker, victim)) return OrderResult.Ignored;
        if (me.Action == ActionMode.Unstake && ReferenceEquals(me.Target, victim)) return OrderResult.Ignored;
        if (victim.Type is not (ThingType.Actor or ThingType.Player) || victim.Actor is not { } them) return OrderResult.Refused;
        if (!them.Has(Actor.Flag.Staked)) return OrderResult.Refused;
        if (!Actions.CancelFirst(unstaker, me, nowMs)) return OrderResult.Busy;
        if (Net.NetOrders.Forward(unstaker, Net.OrderKind.Unstake, victim))
        {
            me.BusyUntil = nowMs + 200;
            return OrderResult.Started;
        }
        me.Target = victim;
        victim.Flags |= WorldThing.Flag.Targetted;
        me.Action = ActionMode.Unstake;
        me.ActionRetryAt = 0;
        Tick(unstaker, me, nowMs);                                                          // the action tick at once
        return OrderResult.Started;
    }

    /// <summary>
    /// ACTION 0xF's tick: walk up to the victim, retrying every 250 ms, and pull the
    /// stake out once in reach.
    /// </summary>
    internal static void Tick(WorldThing thing, Actor me, int nowMs)
    {
        if (me.ActionRetryAt > nowMs) return;
        if (me.Target is not { Type: ThingType.Actor or ThingType.Player, Actor: { } them } victim || !victim.InWorld)
        {
            Actions.StopWalking(thing);
            Actions.Cancel(thing, force: true);
            return;
        }
        if (!them.Has(Actor.Flag.Staked))
        {
            Actions.StopWalking(thing);
            Actions.Cancel(thing);
            return;
        }
        float reach = Rules.GameRules.Current.Stake.UnstakeReach - (thing.Collide == CollideShape.None ? 0f : thing.Radius);
        if (!Bodies.InReach(thing, victim, reach))
        {
            // The approach at the speed the last order left — the unstake order sets none.
            Actions.Close(thing, me, victim, nowMs, Rules.GameRules.Current.Stake.UnstakeRetryMs);
            return;
        }
        Pull(thing, victim);
    }

    /// <summary>The pull itself.</summary>
    static void Pull(WorldThing unstaker, WorldThing victim)
    {
        if (victim.Actor is not { } them) return;
        Actions.Cancel(unstaker);
        unstaker.Face(victim);
        victim.Face(unstaker);
        var forward = Vector3.TransformNormal(Vector3.UnitY, victim.Rotation);
        var to = victim.Position + forward * Rules.GameRules.Current.Stake.UnstakeReach;
        long was = unstaker.Sector;
        unstaker.Position = to;
        if (unstaker.Place is { } place)
        {
            if (place.Solid is { } solid) unstaker.Sector = solid.SectorAt(to, (int)was);
            place.Props?.Moved(unstaker, was, unstaker.Sector);
            unstaker.Locomotion?.Ground(snap: false);
            place.Regions?.Moved(unstaker, was);
        }
        Out(victim);
        int ms = Actions.PlayAction(victim, UnstakedMode);
        if (ms > 0) them.BusyUntil = (victim.Place?.Milliseconds ?? 0) + ms;
        if (unstaker.Animation is { } ua && unstaker.Place?.Motions is { } um)              // the paired motion, the victim its partner:
            ua.PlayPaired(UnstakeMode, um);                                                 // the root kept out of the fade
        Audio.Noise.Foley(victim, Audio.FoleyEvent.Unstaked);
        Diagnostics.Journal.Current?.Write(victim.Place?.Milliseconds ?? 0, Diagnostics.JournalKind.Order,
            ("what", "unstake"), ("who", Diagnostics.Watch.Name(unstaker)), ("on", Diagnostics.Watch.Name(victim)));
    }
}
