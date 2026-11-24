// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How much trouble a character is in: the balance of strength round it and the
// strength score it is built from.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// How far round the character the balance is taken (512 units — both
    /// callers pass the same radius, no flags and no single foe).
    /// </summary>
    public const float ThreatRange = 512f;

    /// <summary>
    /// The query's type mask for the balance (`0xffffff7e`): every
    /// thing type is left out but 0 and 7 — actors and players (the query tests `1 &lt;&lt; type`).
    /// </summary>
    public const uint ThreatTypeMask = 0xffffff7e;

    /// <summary>
    /// The bands the ratio of the two sides' strength falls into: more than 3×,
    /// 2×, 1×, 0.9, 0.8 and 0.5
    /// answer 1 to 6; half or less answers 7.
    /// </summary>
    public static readonly float[] DangerBands = [3f, 2f, 1f, 0.9f, 0.8f, 0.5f];

    /// <summary>
    /// **How much trouble the character is in** — the number the
    /// discipline ladder unlocks its rungs against. It is **not a head
    /// count**: it is the balance of strength within <see cref="ThreatRange"/>.
    ///
    /// <list type="number">
    /// <item>The sphere query (over the sector flood) round the character's position, from
    /// its sector, on a freshly reset query — so its flag word is **zero** and it does not pass
    /// over the dead, and it leaves nobody out (its exclusion is zero: **the character
    /// itself is found**); only the type mask is set (<see cref="ThreatTypeMask"/>), so it answers
    /// characters and nothing else — no prop, door or item ever comes back. `DISABLE COLLIDE` and a
    /// body the sphere does not touch are passed over as for every sphere query.</item>
    /// <item>Everything found that is not a pet (its mind's bit <c>0x4</c>) and that the
    /// character does not count a friend adds its <see cref="Strength"/> to the foes'
    /// side. The foes' side includes whoever is merely not on the character's
    /// team — a villager is a foe to a Cappadocian here — and **the dead**, at their score less the
    /// penalties for having no health.</item>
    /// <item>Foes worth nothing — none at all, or scores that sum to exactly zero — answer
    /// <b>0</b>.</item>
    /// <item>Everything found that is not a pet and that it counts a friend — **itself among them**,
    /// being on its own team — adds to the friends' side.</item>
    /// <item>The ratio friends ÷ foes (the friends' sum loaded as an unsigned 64-bit
    /// integer, the foes' divided in as a signed 32-bit one, in single precision) picks
    /// the band (<see cref="DangerBands"/>): <b>1</b> my side more than three times as strong, down
    /// to <b>7</b> my side half as strong or less.</item>
    /// </list>
    ///
    /// So the ladder's <c>danger &gt; 1</c> means "my side is not three times as strong", and its
    /// <c>danger &lt; 2</c> means "no foes, or my side is more than three times as strong". A
    /// companion facing one strong enemy throws everything it has; one mopping up a crowd of weak
    /// ones need not. Once this counted the enemies in sight within 512 (ours), which ran
    /// the ladder the other way round in both cases.
    /// </summary>
    public int Threat(Mind mind)
    {
        var me = mind.Thing;
        if (where is null || me.Actor is null) return 0;
        // The flood, filtered by the mask above; everything else is the query's own.
        var found = Explosions.ExplosionDamage.Gather(where, me.Position, Rules.GameRules.Current.Ai.ThreatRange, me.Sector);
        int foes = 0;
        uint friends = 0;
        foreach (var hit in found)
            if (Character(hit.Thing) && !Pet(hit.Thing) && !Rpg.Sight.Friendly(me, hit.Thing))
                foes += Strength(hit.Thing.Actor!);
        if (foes == 0) return 0;
        foreach (var hit in found)
            if (Character(hit.Thing) && !Pet(hit.Thing) && Rpg.Sight.Friendly(me, hit.Thing))
                friends += (uint)Strength(hit.Thing.Actor!);
        return Band(friends, foes);

        // `1 << type & 0xffffff7e` is zero only for types 0 and 7.
        static bool Character(WorldThing thing) =>
            (ThreatTypeMask & (1u << ((int)thing.Type & 31))) == 0 && thing.Actor is not null;
        static bool Pet(WorldThing thing) => thing.Mind is { } m && (m.Flags & Mind.FlagPet) != 0;
    }

    /// <summary>
    /// The band of a friends-to-foes ratio. The division is 1.1's, in single
    /// precision; a foes' sum below zero makes the ratio negative,
    /// and a friends' sum below zero reads as a huge unsigned one — both as 1.1 has them.
    /// </summary>
    public static int Band(uint friends, int foes)
    {
        if (foes == 0) return 0;
        float ratio = (float)((double)friends / foes);
        for (int band = 0; band < Rules.GameRules.Current.Ai.DangerBands.Length; band++)
            if (ratio > Rules.GameRules.Current.Ai.DangerBands[band]) return band + 1;
        return Rules.GameRules.Current.Ai.DangerBands.Length + 1;
    }

    /// <summary>The generation at or under which a vampire's blood counts (13).</summary>
    public const float StrengthGenerationCap = 13f;
    /// <summary>What the generation is taken from (14) and what each step is worth (50).</summary>
    public const float StrengthGenerationBase = 14f, StrengthPerGeneration = 50f;
    /// <summary>What each discipline known is worth, per level and one more (`(level + 1) × 10`).</summary>
    public const int StrengthPerDisciplineLevel = 10;

    /// <summary>
    /// The health fractions the penalties step down at (0.95, 0.9, 0.75,
    /// 0.5, 0.25).
    /// </summary>
    public static readonly float[] StrengthSteps = [0.95f, 0.9f, 0.75f, 0.5f, 0.25f];
    /// <summary>What health below each step costs: 25, 50, 100, 150, and 200 below the last.</summary>
    public static readonly int[] HealthPenalty = [25, 50, 100, 150, 200];
    /// <summary>And what blood or mana below each costs: 20, 50, 75, 100, and 150 below the last.</summary>
    public static readonly int[] ReservePenalty = [20, 50, 75, 100, 150];

    /// <summary>
    /// **One character's strength** (whole, as in 1.1; its only caller is
    /// <see cref="Threat"/>, which always asks for the full score). In the engine's order, each step
    /// in single precision and each truncated to a whole number where 1.1 does:
    /// <list type="number">
    /// <item>Lethal soak + normal soak + Stamina + Dexterity + Strength + maximum health, the current
    /// values, summed and truncated.
    /// </item>
    /// <item>With a weapon in hand, the attack it has selected (the byte at
    /// Its damage added as a whole number, then its accuracy
    /// (`WEAPON_ACCURACY`) as a fraction, truncated again.</item>
    /// <item>A vampire (actor type 0) of generation 13 or lower: (14 − generation) × 50 more,
    /// truncated. A 13th-generation vampire gains 50; Christof, at 12, 100.</item>
    /// <item>Every discipline on its list: (level + 1) × 10.</item>
    /// <item>Unless <paramref name="raw"/>, the penalties: health below 95% of
    /// its maximum costs <see cref="HealthPenalty"/> by <see cref="StrengthSteps"/>; then blood below
    /// 95% for a vampire — `(blood + 1) / (pool + 1)` — or mana for a human, costs
    /// <see cref="ReservePenalty"/> on the same steps. A ghoul or a monster has neither, and the
    /// second test is made against the health fraction instead,
    /// so it pays for its wounds twice.</item>
    /// </list>
    /// A comparison that cannot be made (a fraction that is not a number) lands in the lowest step,
    /// as it does in 1.1.
    /// </summary>
    public static int Strength(Actor who, bool raw = false)
    {
        float sum = who.Soak[(int)DamageType.Lethal] + who.Soak[(int)DamageType.Normal];
        sum += who.Stat[(int)ActorStat.Stamina];
        sum += who.Stat[(int)ActorStat.Dexterity];
        sum += who.Stat[(int)ActorStat.Strength];
        sum += who.MaxHealth;
        int score = Truncate(sum);

        if (who.Weapon is { } held)
        {
            var attack = held.Current;
            uint whole = unchecked((uint)(attack.Damage + score));      // loaded as a 64-bit whole, high half 0
            score = Truncate((float)whole + attack.Accuracy);
        }
        float generation = who.Stat[(int)ActorStat.Generation];
        if (who.Type == ActorType.Vampire && generation <= Rules.GameRules.Current.Ai.StrengthGeneration[0])
            score = Truncate((Rules.GameRules.Current.Ai.StrengthGeneration[1] - generation) * Rules.GameRules.Current.Ai.StrengthGeneration[2] + score);
        foreach (var power in who.Disciplines.All)
            score += (power.Level + 1) * Rules.GameRules.Current.Ai.StrengthPerDisciplineLevel;
        if (raw) return score;

        float health = who.Health / who.MaxHealth;
        score -= Penalty(health, Rules.GameRules.Current.Ai.StrengthHealthPenalty);
        float reserve = who.Type switch
        {
            ActorType.Vampire => (who.Stat[(int)ActorStat.Blood] + 1f) / (who.Stat[(int)ActorStat.BloodPool] + 1f),
            ActorType.Human => (who.Stat[(int)ActorStat.Mana] + 1f) / (who.Stat[(int)ActorStat.ManaPool] + 1f),
            _ => health,                                                // The health fraction again
        };
        return score - Penalty(reserve, Rules.GameRules.Current.Ai.StrengthReservePenalty);
    }

    /// <summary>
    /// One of the two penalty ladders: nothing at or above the first step (a step counts only
    /// when below it, or not a number), else the cost of the first step it is still above
    /// (above it and a number), else the last.
    /// </summary>
    static int Penalty(float fraction, int[] cost)
    {
        if (fraction >= Rules.GameRules.Current.Ai.StrengthHealthSteps[0]) return 0;
        for (int i = 1; i < Rules.GameRules.Current.Ai.StrengthHealthSteps.Length; i++)
            if (fraction > Rules.GameRules.Current.Ai.StrengthHealthSteps[i]) return cost[i - 1];
        return cost[^1];
    }

    /// <summary>Truncated toward zero: the low 32 bits of the 64-bit whole.</summary>
    static int Truncate(float value) => unchecked((int)(long)value);
}
