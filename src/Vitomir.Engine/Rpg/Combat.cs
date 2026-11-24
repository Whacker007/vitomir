// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The blow: whether it lands, how hard, and how much of it the target shrugs off.
//
// This file is deliberately the *only* place the four combat rolls live. `misc/effects.ned` states
// three of them in its own header, and the engine agrees with it:
//
//   hit if random(Adex + weaponAccuracy) + Axoffense > random(Ddex) + Dxdefense
//   dam  = random(Astr + weaponDamage) + Apotence + Axdamage
//            - random(Dsoak [+ Dstamina]) [- Dfortitude] - Dxsoak
//
// Every number the rolls use is a named constant here rather than a literal in a formula, because
// the decision is to let the player replace these equations with a file at load time. When that
// comes, it replaces this class and nothing else.
//
// The steps: the resolve (the to-hit and what a hit then does), the damage
// roll, applying it, and the soak.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>What one swing came to.</summary>
/// <param name="Hit">Did it land at all?</param>
/// <param name="Margin">
/// How much the offence beat the defence by, truncated toward zero, as
/// 1.1 does. Above <see cref="Combat.CriticalMargin"/> the blow is a critical.
/// </param>
/// <param name="Damage">What was rolled, after the doublings.</param>
/// <param name="Multiplier">
/// 0 when nothing doubled it, 2 for one doubling, 4 for both. The engine carries this alongside the
/// damage so the hit reaction can say how hard it was; it is not a factor applied to the damage.
/// </param>
/// <param name="Critical">Was the margin over <see cref="Combat.CriticalMargin"/>?</param>
public readonly record struct Blow(bool Hit, int Margin, float Damage, float Multiplier, bool Critical);

/// <summary>
/// The combat formulae. Static and side-effect-free: it rolls, it does not apply. Applying is
/// <see cref="Damage"/>'s job.
/// </summary>
public static class Combat
{
    // ---- the constants, all the original game's ----

    /// <summary>A hit needs the margin to come out greater than this (`jg`).</summary>
    public const int HitMargin = 0;
    /// <summary>Over this and the blow doubles again, and may stake (`cmp 0x3c`).</summary>
    public const int CriticalMargin = 60;
    /// <summary>What a defender loses for being restrained or feeding (30.0).</summary>
    public const float HelplessDefencePenalty = 30f;
    /// <summary>
    /// The player's backstab: when the two characters face the same way by more than
    /// <see cref="BackstabDot"/>, the defence loses that much times this (20.0).
    /// Only a THING_TYPE_PLAYER attacker gets it.
    /// </summary>
    public const float BackstabScale = 20f, BackstabDot = 0.2f;
    /// <summary>The damage roll's die: `1 + rand01 * 9`, one per full ten points of the pool.</summary>
    public const float DieSpan = 9f, DieFloor = 1f, PointsPerDie = 10f;
    /// <summary>The pool is divided by ten to get the number of dice.</summary>
    public const float DicePerPoint = 0.1f;
    /// <summary>What a doubling adds to <see cref="Blow.Multiplier"/> (2.0).</summary>
    public const float MultiplierStep = 2f;
    /// <summary>Elemental damage is soaked at half, and fortitude counts half against it.</summary>
    public const float ElementalSoakShare = 0.5f;
    /// <summary>A character must keep at least this much health to stay up (1.0).</summary>
    public const float DeathThreshold = 1f;

    /// <summary>The rules in force (the rules file's [Combat]; the constants above are its defaults).</summary>
    static Rules.CombatRules R => Rules.GameRules.Current.Combat;

    /// <summary>A character's current stat, or zero when the thing is not a character.</summary>
    static float Stat(WorldThing thing, ActorStat stat) => thing.Actor?.Stat[(int)stat] ?? 0f;

    // ---- the rolls ----

    /// <summary>
    /// The attacker's side: `rand01 * (weapon accuracy + Dexterity) + XOFFENSE`.
    /// </summary>
    public static float Offense(WorldThing attacker, WeaponAttack attack, Rng rng) =>
        rng.Unit() * (attack.Accuracy + Stat(attacker, ActorStat.Dexterity))
        + attacker.Effects.Value(EffectKind.Offense);

    /// <summary>
    /// The defender's side: `rand01 * Dexterity + XDEFENSE`, less what being helpless or being
    /// taken from behind costs. <paramref name="facing"/> is the dot of the two characters'
    /// forward vectors on the ground plane, and is only consulted for a player attacker.
    /// </summary>
    public static float Defense(WorldThing target, bool helpless, bool attackerIsPlayer, float facing, Rng rng)
    {
        float d = rng.Unit() * Stat(target, ActorStat.Dexterity)
                  + target.Effects.Value(EffectKind.Defense);
        var r = R;
        if (helpless) d -= r.HelplessDefencePenalty;
        if ((attackerIsPlayer || !r.BackstabPlayersOnly) && facing >= r.BackstabMinDot) d -= facing * r.BackstabScale;
        return d;
    }

    /// <summary>
    /// The damage roll. The pool is the weapon's damage plus the attacker's Strength (unless the
    /// attack says otherwise); it buys one ten-sided die per full ten points, and the remainder is
    /// scaled by a single extra roll. POTENCE and XDAMAGE are added whole.
    /// </summary>
    public static float RollDamage(WorldThing attacker, WeaponAttack attack, Rng rng)
    {
        var r = R;
        float pool = attack.Damage;
        if (r.DamageIncludesStrength && !attack.Is(WeaponFlag.NoStrength)) pool += Stat(attacker, ActorStat.Strength);

        int dice = (int)(pool * r.DamagePointsPerDie[1]);
        float total = 0f;
        for (int i = 0; i < dice; i++) total += rng.Unit() * r.DamageDieSpan + r.DamageDieFloor;
        total += (pool - dice * r.DamagePointsPerDie[0]) * rng.Unit();

        return total + attacker.Effects.Value(EffectKind.Damage)
                     + attacker.Effects.Value(EffectKind.Potence);
    }

    /// <summary>
    /// How much of a blow the target keeps off: `rand01 * (soak + XSOAK) + rand01 * fortitude`.
    /// Which soak, and whether Stamina and Fortitude count, depends on the damage type:
    /// <list type="bullet">
    /// <item>NORMAL — soak[0] + Stamina, full Fortitude.</item>
    /// <item>LETHAL — soak[1] + Stamina, but a **human** gets no Stamina against it; full Fortitude.</item>
    /// <item>AGGRAVATED — soak[2] alone, full Fortitude.</item>
    /// <item>ELECTRIC, FIRE, SUN, FAITH, COLD — half the soak and half the Fortitude.</item>
    /// <item>POISON, DISEASE — the soak alone, and Fortitude does not count.</item>
    /// </list>
    /// </summary>
    public static float Soak(WorldThing target, DamageType type, Rng rng)
    {
        var actor = target.Actor ?? throw new ArgumentException("not a character", nameof(target));
        int i = (int)type;
        float soak, fortitude = target.Effects.Value(EffectKind.Fortitude);
        float stamina = actor.Stat[(int)ActorStat.Stamina];
        var r = R;
        // Each share is applied only when it is not 1.1's whole (or nothing), so the defaults' arithmetic is the formula's own.
        static float Plus(float soak, float stamina, float share) => share == 0f ? soak : soak + (share == 1f ? stamina : stamina * share);
        static float Times(float fortitude, float share) => share == 1f ? fortitude : fortitude * share;
        switch (type)
        {
            case DamageType.Normal:
                soak = Plus(actor.Soak[i], stamina, r.SoakNormal[0]);
                fortitude = Times(fortitude, r.SoakNormal[1]);
                break;
            case DamageType.Lethal:
                soak = actor.Type == ActorType.Human && r.SoakLethal[1] == 0f
                    ? actor.Soak[i] + 0f
                    : Plus(actor.Soak[i], stamina, actor.Type == ActorType.Human ? r.SoakLethal[1] : r.SoakLethal[0]);
                fortitude = Times(fortitude, r.SoakLethal[2]);
                break;
            case DamageType.Aggravated:
                soak = Plus(actor.Soak[i], stamina, r.SoakAggravated[0]);
                fortitude = Times(fortitude, r.SoakAggravated[1]);
                break;
            case DamageType.Electric or DamageType.Fire or DamageType.Sun
                 or DamageType.Faith or DamageType.Cold:
                soak = actor.Soak[i] * r.ElementalSoakShare;
                fortitude *= r.ElementalSoakShare;
                break;
            default:
                soak = actor.Soak[i];
                if (r.PoisonDiseaseIgnoreFortitude) fortitude = 0f;
                break;
        }
        float extra = target.Effects.Value(EffectKind.SoakBonus);
        if (soak + extra == 0f && fortitude == 0f) return 0f;
        return rng.Unit() * (soak + extra) + rng.Unit() * fortitude;
    }

    /// <summary>
    /// How much a blow raises a vampire's or a ghoul's frenzy. Three stats pull on it
    /// and fire makes it worse:
    /// <code>
    ///   frenzy = damage
    ///          x (2.0 - Blood        x 0.015)     // full of blood, half as shaken
    ///          x (0.5 + FrenzyRating x 0.015)     // a short fuse, four times as shaken
    ///          x (damage type is FIRE ? 1.25 : 1)
    ///          x (Wits &gt; 20 ? 1.0 - Wits x 0.008 : 1)
    /// </code>
    /// which the engine then applies as the `ef_increasefrenzy` effect at level 1.
    /// </summary>
    public static float FrenzyFromDamage(Actor actor, DamageType type, float damage)
    {
        // scale, blood term, empty term, rating term, zero term, fire term, Wits term, Wits over (the rules file's [Frenzy]).
        var t = Rules.GameRules.Current.Frenzy.FrenzyFromDamage;
        float f = damage
                * (t[0] * actor.Stat[(int)ActorStat.Blood] * t[1] + t[2])
                * (t[0] * actor.Stat[(int)ActorStat.FrenzyRating] * t[3] + t[4]);
        if (type == DamageType.Fire) f *= t[5];
        float wits = actor.Stat[(int)ActorStat.Wits];
        if (wits > t[7]) f *= t[0] * wits * t[6] + 1f;
        return f;
    }

    /// <summary>A stat is read as a percentage before it goes into the frenzy terms.</summary>
    public const float StatScale = 0.01f;
    /// <summary>The frenzy terms.</summary>
    public const float BloodCalms = -1.5f, TwoAtEmpty = 2f, RatingStirs = 1.5f, HalfAtZero = 0.5f,
                       FireFrenzy = 1.25f, WitsCalm = -0.8f, WitsMatterAbove = 20f;

    /// <summary>
    /// What a party member takes off a shot from somebody outside the party, and what a
    /// **Cappadocian** takes extra from FAITH damage — the one place in the combat code
    /// where a clan is named.
    /// </summary>
    public const float PartyShotShare = 0.75f, CappadocianFaith = 1.5f;

    /// <summary>
    /// One swing, from the roll to the doublings. The order is the engine's: offence and defence
    /// first, then the damage, then silver, then the critical — so a critical doubles what silver
    /// already doubled.
    /// </summary>
    public static Blow Swing(WorldThing attacker, WorldThing target, WeaponAttack attack, Rng rng,
                             bool targetHelpless = false, bool attackerIsPlayer = false, float facing = 0f)
    {
        float offense = Offense(attacker, attack, rng);
        float defense = Defense(target, targetHelpless, attackerIsPlayer, facing, rng);
        int margin = (int)(offense - defense);
        var r = R;
        if (margin <= r.HitMargin) return new Blow(false, margin, 0f, 0f, false);

        float damage = RollDamage(attacker, attack, rng);
        float multiplier = 0f;
        if (r.SilverDoubles && attack.Is(WeaponFlag.Silver) && target.Actor is { } t && t.Has(Actor.Flag.HurtBySilver))
        {
            damage += damage;
            multiplier = r.CriticalMultiplierStep;
        }
        bool critical = margin > r.CriticalMargin;
        if (critical)
        {
            damage += damage;
            multiplier += r.CriticalMultiplierStep;
        }
        return new Blow(true, margin, damage, multiplier, critical);
    }
}
