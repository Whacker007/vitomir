// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The resolve's branch on the action, read for a comparison with 1.1's
// gaps 5 and 6.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **What the resolve does, by the action the striker is in**. Once the target
/// is known to be there and in the same place (a missing one is a whiff, noise and all), the
/// resolve reads the action and does one of three things:
/// <list type="bullet">
/// <item>**a swing** (5): the reach test, the container's branch, the to-hit roll, the lot —
/// <see cref="Combat.Swing"/>;</item>
/// <item>**a throw** (18, the special move's grab — the war ghoul's toss, the Vozhd's, the alligator's
/// maul): **straight to the damage** — no reach test, no container branch, no
/// actor-type test and no roll, so a throw's blow always lands (<see cref="Throw"/>);</item>
/// <item>**anything else**: nothing at all, not even the swing noise — a strike tag
/// a script's attack motion crosses outside a swing does no harm.</item>
/// </list>
/// Vitomir rolled a throw's blow as a swing (so it could miss) and resolved a strike tag in any action
/// with a target, until then.
/// </summary>
public static class ThrowBlow
{
    /// <summary>The three things the resolve can do.</summary>
    public enum Resolve { Swing, Throw, Nothing }

    /// <summary>What the resolve does for a striker in <paramref name="action"/>.</summary>
    public static Resolve For(ActionMode action) => action switch
    {
        ActionMode.Swing => Resolve.Swing,
        ActionMode.Throwing => Resolve.Throw,
        _ => Resolve.Nothing,
    };

    /// <summary>
    /// A throw's blow: the damage roll, doubled by silver on a target silver
    /// hurts, and then the critical test (`margin > 60`) — against a margin the
    /// throw's path never wrote. The original reads an uninitialised local here, which holds whatever the last
    /// call to use that depth of the stack left there: not the resolve's own caller chain (they
    /// write nothing so deep), but whatever ran
    /// before the mixer's tick that frame. That was not traced, and it is not a number one can count on,
    /// so **Vitomir takes it as never critical (ours)**: no critical doubling, and none of the critical's
    /// stake on a vampire. A throw is therefore always a hit, for its damage roll, silver
    /// doubled. <see cref="Blow.Margin"/> is 0 (there was none).
    /// </summary>
    public static Blow Throw(WorldThing attacker, WorldThing target, WeaponAttack attack, Rng rng)
    {
        float damage = Combat.RollDamage(attacker, attack, rng);
        float multiplier = 0f;
        if (attack.Is(WeaponFlag.Silver) && target.Actor is { } t && t.Has(Actor.Flag.HurtBySilver))
        {
            damage += damage;
            multiplier = Combat.MultiplierStep;
        }
        return new Blow(true, 0, damage, multiplier, false);
    }
}
