// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A character hurt by a blow flinches: the hit reaction, and the quiet after it.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The hit reaction** — the start of the damage reaction, the effects of a blow that did harm, and the
/// flinch it can end in. In the engine's order, for a blow of more than one point
/// (<see cref="Threshold"/>) to a character in the world, and only if it has not flinched in the
/// last 3.5 s (<see cref="Damage.HurtQuietMs"/>):
/// <list type="number">
/// <item>the hurt noises, <c>damage_&lt;type&gt;</c> and <c>vox_damage_&lt;type&gt;</c>
/// (<see cref="Damage.Hurt"/>);</item>
/// <item>the **roll**: a chance of <c>damage × 8 − Stamina</c> percent, held between 5 and 95
/// (<see cref="Chance"/>), against <c>rand01 × 100</c> — drawn only when the character is not being
/// thrown, throwing or staked (actions 17, 18 and 19);</item>
/// <item>on a hit, whatever it is doing is cancelled — the ordinary, unforced cancel, which
/// does nothing to a busy character — its walk is stopped, and it **flinches**
/// (<see cref="Play"/>).</item>
/// </list>
/// So the 3.5 s gate is not only on the noise: a character that has just flinched cannot flinch
/// again, or cry out, until it has passed, however hard it is hit. Stamina is the only defence —
/// a Cappadocian's 35 makes a blow of 5 or less a 5% flinch and one of 17 or more a 95% one.
///
/// **As in 1.1**, a blow that costs health within 3.5 s of the victim's last flinch never
/// makes it flinch, and outside that time the victim flinches as often as the formula above
/// says.
/// Every flinch sets the action to 10 and plays <c>hurt</c> plus the weapon's carry at 30 frames a
/// second on the action channel — `hurt_onehand` for a sword, `hurt_bow` for the skeleton archer,
/// plain `hurt` for a knight with a pole-arm whose set has no `hurt_polearm` — and it cuts short
/// swings (`swordslash`, `bow`, `punch`) and a cast (`handsspell`) as readily as it catches a
/// character standing.
/// </summary>
public static class Flinch
{
    /// <summary>A blow must be more than this for any of it (compared <c>1.0 &lt; damage</c>).</summary>
    public const float Threshold = 1f;

    /// <summary>The chance per point of damage, in percent.</summary>
    public const float PercentPerPoint = 8f;

    /// <summary>The chance's floor and ceiling, and the scale of the roll (a double).</summary>
    public const float Floor = 5f, Ceiling = 95f, Hundred = 100f;

    /// <summary>
    /// The body mode of a flinch: <c>hurt</c> (0xdd = 221), plus the weapon's <see cref="Weapon.Carry"/>
    /// — the same offset a stand or a walk takes — so `hurt_onehand`, `hurt_bow`, `hurt_polearm`.
    /// </summary>
    public const int HurtMode = 0xdd;

    /// <summary>
    /// <c>damage × 8 − Stamina</c>, held to 5..95 — the percent chance a blow makes a character
    /// flinch. A value that is neither below 5 nor above 95 (a NaN) is kept.
    /// </summary>
    public static float Chance(float damage, float stamina)
    {
        var r = Rules.GameRules.Current.Wounds;
        float chance = damage * r.FlinchChancePerPoint - stamina;
        if (chance < r.FlinchChanceClamp[0]) return r.FlinchChanceClamp[0];
        if (chance > r.FlinchChanceClamp[1]) return r.FlinchChanceClamp[1];
        return chance;
    }

    /// <summary>
    /// Whether a character can be made to flinch at all by the roll: not while it is
    /// being thrown, throwing somebody or staked.
    /// </summary>
    public static bool Interruptible(Actor actor) =>
        actor.Action is not (ActionMode.Thrown or ActionMode.Throwing or ActionMode.Staked);

    /// <summary>
    /// The roll after the noises. The caller has already made the gate's tests
    /// (<see cref="Damage.Hurt"/>). Answers whether the character flinched.
    /// </summary>
    public static bool Roll(WorldThing thing, Actor actor, float damage, Rng rng, int nowMs)
    {
        float chance = Chance(damage, actor.Stat[(int)ActorStat.Stamina]);
        if (!Interruptible(actor)) return false;                                       // before the rand()
        if (rng.Unit() * Rules.GameRules.Current.Wounds.FlinchChanceClamp[2] > chance) return false;
        Actions.Cancel(thing);
        Actions.StopWalking(thing);
        return Play(thing, actor, nowMs);
    }

    /// <summary>
    /// **The flinch itself**. Only for a character that is free — not busy, dead or held
    /// — **or one in the middle of a swing (5) or a cast (13)**, which it cuts short; and never a
    /// staked one. It plays <see cref="HurtMode"/> plus the carry on the action channel at thirty
    /// frames a second, falling back to plain `hurt` when the set has not got that one, and when
    /// something played the character's action becomes <see cref="ActionMode.Hurt"/> and it is busy
    /// for the animation's length. A player's portrait head gives its `head_impact` either way.
    /// And the time is written down — the start of the 3.5 s quiet.
    /// </summary>
    /// <returns>Whether the character flinched — the time was written.</returns>
    public static bool Play(WorldThing thing, Actor actor, int nowMs)
    {
        bool free = (uint)actor.BusyUntil <= (uint)nowMs && !actor.Has(Actor.Flag.Dead)
                    && !actor.Has2(Actor.Flag2.Restrained);
        if (!(free || actor.Action is ActionMode.Swing or ActionMode.Cast)) return false;
        if (actor.Has(Actor.Flag.Staked)) return false;
        int mode = HurtMode + (actor.Weapon?.Carry ?? 0);
        int ms = Actions.PlayAction(thing, mode);
        bool none = false;
        if (ms < 0)
        {
            ms = Actions.PlayAction(thing, HurtMode);
            none = ms < 0;
        }
        if (ms != 0 && !none)
        {
            actor.Action = ActionMode.Hurt;
            actor.BusyUntil = nowMs + ms;
        }
        if (thing.Type == ThingType.Player) actor.HeadAction = Ui.HeadMode.Impact;
        actor.FlinchedAt = nowMs;
        return true;
    }
}
