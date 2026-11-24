// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The special-move throw, as in 1.1: the strike's part,
// the grab itself, the thrown victim's tick, the
// thrower's tick, the `Throwdeath` keyframe tag and the thrower's arm of the
// death effects.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The special-move throw.** Eight weapon templates carry the attack flag `THROW`
/// (<see cref="WeaponFlag.CanSpecialMove"/>, 0x1000) and the three `WEAPON_THROW*` tags — the
/// Vozhd's and the lesser Vozhd's claws, the advanced war ghoul's, both werewolves', the Golem's
/// punch, Zulo's bite and the alligator ghoul's. When a strike with one of them starts against a
/// character, a roll can turn it into a grab: the two turn to face each other, the victim is put
/// the weapon's throw distance in front of the thrower, restrained, and both play their throw
/// motions — `throw1`…`throw4` for the thrower and `thrown1`…`thrown4` for the victim, picked by
/// the weapon's throw type — each busy for exactly the length of its own. The flight, the fall and
/// the landing are all in the victim's animation; the engine moves nobody while it plays.
///
/// What the grab replaces is the swing: no attack animation, no swing noise, no war cry, and the
/// thrower is busy for the throw rather than for the attack's reload. The **blow is still struck**
/// — every shipped `throw` animation carries a `Strike` tag (the Vozhd's at frame 93 of 199, the
/// werewolf's at 16 of 99), and the tag handler does not care what action it fires in,
/// so the thrown victim is hit, by the same attack, from wherever it now is.
/// </summary>
public static class SpecialMove
{
    /// <summary>The thrower's first motion, `throw1`.</summary>
    public const int ThrowMode = 0xed;
    /// <summary>The victim's first motion, `thrown1`.</summary>
    public const int ThrownMode = 0xf2;
    /// <summary>The keyframe tag the landing is marked with, `Throwdeath`.</summary>
    public const uint ThrowdeathTag = 18;
    /// <summary>The damage a thrower that died in the throw is handed to its death effects when the
    /// throw ends (10.0) — under <see cref="Damage.QuickDeathDamage"/>,
    /// so it is the slow death.</summary>
    public const float DeadThrowerDamage = 10f;

    /// <summary>
    /// The branch in the strike, taken after the attacker has stopped
    /// and turned to its target and before anything is played. All of these, in this order:
    /// <list type="number">
    /// <item>the **current** attack carries `THROW` — which for the Vozhd's claw, the advanced war
    /// ghoul's and the lesser werewolf's is the **second** attack only (the templates put the flag on
    /// `WEAPON_FLAGS2`), so those throw only when their AI picks it;</item>
    /// <item>the target is a character or a party member (thing type 0 or 7);</item>
    /// <item><c>rand() × (1/32767) &lt; WEAPON_THROWCHANCE</c> — one draw of the engine's one
    /// generator. The chance is clamped to 0..1 when the template is read, so
    /// the big werewolf's 5 is 1, which still misses on the one roll in 32,768 that reaches 1.0.
    /// (A host then tells the players in the place — message 0xc with its last byte 1 —
    /// and each player's machine replays the grab with the roll skipped: `Net.NetArms`.);</item>
    /// <item>the grab itself (<see cref="Grab"/>) is taken.</item>
    /// </list>
    /// Answers true when the strike became a throw, and the strike then does nothing more.
    /// **A repair** (<see cref="Repairs.HostThrows"/>): on a player's machine it answers false before
    /// the roll. That machine strikes only what the host sends (the strike returns on a client unless
    /// the strike is replayed), and 1.1 rolls again there for a replayed ordinary strike
    /// the roll is skipped only for a replayed throw), so a player could see a
    /// throw the host never made. The host's own throw still comes, replayed as a grab
    /// (`Net.NetArms`'s `Thrown`, which calls <see cref="Grab"/> itself).
    /// </summary>
    /// <param name="rng">The generator to roll on; by default the place's (the engine has one). A
    /// place with none rolls nothing, and nobody is thrown (ours).</param>
    public static bool Try(WorldThing attacker, WorldThing victim, Weapon weapon, int nowMs, Rng? rng = null)
    {
        if (!weapon.Current.Is(WeaponFlag.CanSpecialMove)) return false;
        if (victim.Type is not (ThingType.Actor or ThingType.Player)) return false;
        if (Net.NetOrders.Mode(attacker.Place) == Net.NetMode.Client) return false;     // Repairs.HostThrows
        rng ??= attacker.Place?.Minds?.Random;
        if (rng is null || !(rng.Unit() < weapon.ThrowChance)) return false;
        if (Grab(attacker, victim, weapon.ThrowType, weapon.ThrowDistance, nowMs) != OrderResult.Started) return false;
        // A host tells the players in the place, whose machines replay it (`Net.NetArms`).
        Net.NetArms.Threw(attacker, victim, weapon);
        return true;
    }

    /// <summary>
    /// **The grab** (<c>thrower.Throw(victim, type, distance)</c>). Refused (1) unless both
    /// have a motion set, neither is dead, the victim is not on its way out of the
    /// world (<c>THING_FLAG_REMOVE</c>), and the thrower's set has `throw` + type and the victim's has
    /// `thrown` + type (the lookup on each: no fallback). Then (4):
    /// <list type="number">
    /// <item>the thrower turns to the victim, and the victim to the thrower (each way);</item>
    /// <item>the victim's action is cancelled (not forced — a victim in mid-swing keeps
    /// it until the mode is overwritten below), its action animation stopped, its walk
    /// stopped, and it is restrained (`ef_restrain`);</item>
    /// <item>the victim is **put** — `SetPosition`, not walked — at the thrower's position
    /// plus the distance along the thrower's facing (the matrix's row 1), at the
    /// thrower's height. Nothing asks whether a wall is in the way, and nothing does in 1.1;</item>
    /// <item>the victim goes into <see cref="ActionMode.Thrown"/> (17) and the thrower into
    /// <see cref="ActionMode.Throwing"/> (18);</item>
    /// <item>each plays its motion on the action channel at thirty frames a second and is
    /// busy until it ends — the victim first.</item>
    /// </list>
    /// </summary>
    public static OrderResult Grab(WorldThing thrower, WorldThing victim, int type, float distance, int nowMs)
    {
        if (thrower.Actor is not { } me || victim.Actor is not { } them) return OrderResult.Refused;
        if (thrower.Animation is not { } throwing || victim.Animation is not { } thrown) return OrderResult.Refused;
        if (them.Has(Actor.Flag.Dead) || me.Has(Actor.Flag.Dead) || victim.Has(WorldThing.Flag.Remove))
            return OrderResult.Refused;
        if ((thrower.Place ?? victim.Place)?.Motions is not { } motions) return OrderResult.Refused;
        if (motions.Find(throwing.Set, ThrowMode + type) is not { } hurl
            || motions.Find(thrown.Set, ThrownMode + type) is not { } flight)
            return OrderResult.Refused;

        thrower.Face(victim);
        victim.Face(thrower);
        Actions.Cancel(victim);
        thrown.StopAction();
        Actions.StopWalking(victim);
        EffectRuntime.Apply(victim, "ef_restrain", 0);
        var forward = Vector3.TransformNormal(Vector3.UnitY, thrower.Rotation);
        Put(victim, thrower.Position + forward * distance);

        them.Action = ActionMode.Thrown;
        me.Action = ActionMode.Throwing;
        them.BusyUntil = nowMs + thrown.Play(flight.Mode, flight.Animation, motions, onMotion: false,
                                             rate: Anim.ThingAnimation.Rate);
        me.BusyUntil = nowMs + throwing.Play(hurl.Mode, hurl.Animation, motions, onMotion: false,
                                             rate: Anim.ThingAnimation.Rate);
        Diagnostics.Journal.Current?.Write(nowMs, Diagnostics.JournalKind.Order, ("what", "throw"),
            ("who", Diagnostics.Watch.Name(thrower)), ("on", Diagnostics.Watch.Name(victim)), ("type", type));
        return OrderResult.Started;
    }

    /// <summary>
    /// `simThing::SetPosition` for the victim: the point, the sector it falls in and the
    /// regions (<see cref="Location.Move"/>), then its feet put on whatever ground is there — the
    /// same move the unstake makes of the one pulling the stake.
    /// </summary>
    static void Put(WorldThing victim, Vector3 to)
    {
        if (victim.Place is { } place) place.Move(victim, to);
        else victim.Position = to;
        victim.Locomotion?.Ground(snap: false);
    }

    /// <summary>
    /// The thrown victim's tick (ACTION_THROWN): once its motion has run out (busy time
    /// passed) `ef_restrain` comes off and the action is cancelled (not forced),
    /// which puts it back to nothing. A victim whose landing already ended the throw
    /// (<see cref="Throwdeath"/>) is no longer in this mode and never gets here.
    /// </summary>
    public static void ThrownTick(WorldThing thing, Actor me, int nowMs)
    {
        if (me.BusyUntil > nowMs) return;
        EffectRuntime.Remove(thing, "ef_restrain");
        Actions.Cancel(thing);
    }

    /// <summary>
    /// The thrower's tick (ACTION_THROWING): once the throw has played the action is
    /// cancelled (not forced). A thrower that is dead by then is cleared outright
    /// (action, target, pending discipline and busy time) and its death effects run again, as a
    /// slow death (the death's looks with 10 damage). **Not reached in single player that this project has
    /// found**: the kill cancels a dead character's action, forced, before any tick can
    /// see it. Of the death effects Vitomir replays only the fall (ours).
    /// </summary>
    public static void ThrowingTick(WorldThing thing, Actor me, int nowMs)
    {
        if (me.BusyUntil > nowMs) return;
        Actions.Cancel(thing);
        if (!me.Has(Actor.Flag.Dead)) return;
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
        me.BusyUntil = 0;
        Damage.Fall(thing, ActionMode.None, Rules.GameRules.Current.Stake.SpecialThrowDeadThrowerDamage, thing.Place?.Motions);
    }

    /// <summary>
    /// The `Throwdeath` keyframe tag (the dispatcher sends it only for a character or a
    /// party member). It marks the frame the thrown body comes to rest on — frame 154 of the 199 of
    /// `ChrisVozhdDamage`, 54 of 99 in the war ghoul's toss, 71 of 119 in the alligator's maul.
    /// <list type="bullet">
    /// <item>**dead** — killed by the blow the throw struck, or by anything else on the way: the
    /// action channel is stopped where it is (the mixer's "held" bit, 0x1000), so the body lies as it
    /// landed. A victim killed in mid-air plays no death of its own, so this is the only
    /// fall it gets;</item>
    /// <item>**alive**: `ef_restrain` comes off and, if it is still
    /// <see cref="ActionMode.Thrown"/>, the action goes back to nothing. It stays busy for the rest
    /// of the motion — getting up — all the same.</item>
    /// </list>
    /// </summary>
    public static void Throwdeath(WorldThing thing)
    {
        if (thing.Type is not (ThingType.Actor or ThingType.Player) || thing.Actor is not { } me) return;
        if (me.Has(Actor.Flag.Dead) && thing.Animation is { Action: not null } animation)
        {
            animation.HoldAction();
            return;
        }
        EffectRuntime.Remove(thing, "ef_restrain");
        if (me.Action == ActionMode.Thrown) me.Action = ActionMode.None;
    }

    /// <summary>
    /// **A thrower killed in the throw drops its victim** — the arm of the death effects for a
    /// character in <see cref="ActionMode.Throwing"/>, before its death is
    /// chosen: its own action animation is stopped, and its target, if that is still a
    /// character or a party member, has its action animation stopped too, is cancelled **forced**
    /// (the cancel with (0, 1)) and loses `ef_restrain`. So the victim is back on its feet, where
    /// it was put, the moment the thrower dies.
    /// </summary>
    public static void Dropped(WorldThing thrower)
    {
        thrower.Animation?.StopAction();
        if (thrower.Actor?.Target is not { } victim || victim.Type is not (ThingType.Actor or ThingType.Player)) return;
        victim.Animation?.StopAction();
        Actions.Cancel(victim, force: true);
        EffectRuntime.Remove(victim, "ef_restrain");
    }
}
