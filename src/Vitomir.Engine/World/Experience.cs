// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Experience handed out, and what a new rank gives (the player's award, the party's rank
// ladder and its rank-up).
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The one award every script's experience goes through and the rank-up it can set off.
/// A script hands experience out with <c>CodexPlayer.AwardPartyExperience</c> — the
/// monastery's first conversation gives 50 — or <c>AwardPlayerExperience</c> for one character.
/// </summary>
public static class Experience
{
    /// <summary>
    /// What each rank gives, by rank and <c>ACTOR_TYPE</c> (the rank table, 44 bytes a cell,
    /// the name then three numbers: the threshold, then these two). <b>Pool</b> is added to the
    /// blood pool — the mana pool for a human — and <b>Health</b> to the maximum health, which is
    /// then healed by the same amount. Rank 0 is never reached by a rank-up and gives nothing;
    /// the monster column is empty in every row.
    /// </summary>
    public static readonly (int Pool, int Health)[,] RankBonus =
    {
        //  kindred      ghoul        human        monster
        { (0, 0),     (0, 0),      (0, 0),      (0, 0) },
        { (20, 20),   (10, 25),    (20, 20),    (0, 0) },
        { (20, 30),   (10, 40),    (40, 40),    (0, 0) },
        { (30, 50),   (15, 75),    (60, 60),    (0, 0) },
        { (40, 100),  (20, 150),   (80, 80),    (0, 0) },
    };

    /// <summary>
    /// The rank table's pools or health of one actor type as the rules file lists them (<see cref="Rules.RankRules.RankBonusVampirePool"/>
    /// and the rest): ranks 1 to 4.
    /// </summary>
    public static int[] BonusDefaults(ActorType type, bool pool)
    {
        var list = new int[4];
        for (int rank = 1; rank < 5; rank++)
            list[rank - 1] = pool ? RankBonus[rank, (int)type].Pool : RankBonus[rank, (int)type].Health;
        return list;
    }

    /// <summary>
    /// Experience for one character: onto the spendable pool and the lifetime total
    /// together, each held to 0..100,000,000. If the lifetime total has crossed into a higher rank,
    /// the rank-up runs once, for the rank it has reached — two ranks at once give only the second
    /// rank's bonus. <paramref name="rankGained"/> hears about it, which is where the console line
    /// <c>RPG_NEWRANKGAINED</c> goes.
    /// </summary>
    public static void Award(WorldThing thing, int amount, Action<WorldThing, int>? rankGained = null)
    {
        if (thing.Actor is not { } actor) return;
        int before = Ui.CharacterPane.Rank(actor.Experience);
        long cap = Rules.GameRules.Current.Stats.ExperienceCap;
        actor.SpendableExperience = Math.Clamp(actor.SpendableExperience + amount, 0, cap);
        actor.Experience = Math.Clamp(actor.Experience + amount, 0, cap);
        int after = Ui.CharacterPane.Rank(actor.Experience);
        if (after <= before) return;
        // 1.1 pays only the rank reached; the rules file may pay each rank crossed, in turn (ours).
        for (int rank = Rules.GameRules.Current.Ranks.MultiRankAwardsOnlyLast ? after : before + 1; rank <= after; rank++)
        {
            RankUp(thing, rank);
            rankGained?.Invoke(thing, rank);
        }
    }

    /// <summary>
    /// A new rank. A human's mana pool grows, and the mana is filled to it; anybody
    /// else's blood pool grows and the blood is filled to it. The maximum health grows and the
    /// character is healed by that much. Each pool is written as the bonus plus its <em>current</em>
    /// value, onto the base, then refolded.
    /// </summary>
    public static void RankUp(WorldThing thing, int rank)
    {
        if (thing.Actor is not { } actor || rank is < 0 or >= 5) return;
        int type = (int)actor.Type is >= 0 and < 4 ? (int)actor.Type : 3;
        var ranks = Rules.GameRules.Current.Ranks;
        var (pool, health) = rank == 0 ? (0, 0) : (ranks.RankBonus(type).Pool[rank - 1], ranks.RankBonus(type).Health[rank - 1]);
        if (actor.Type == ActorType.Human)
        {
            actor.SetBaseStat((int)ActorStat.ManaPool, pool + actor.Stat[(int)ActorStat.ManaPool]);
            Rpg.EffectRuntime.Recompute(thing, EffectKind.Stat, (int)ActorStat.ManaPool);
            actor.SetBaseStat((int)ActorStat.Mana, actor.Stat[(int)ActorStat.ManaPool]);
            Rpg.EffectRuntime.Recompute(thing, EffectKind.Stat, (int)ActorStat.Mana);
        }
        else
        {
            actor.SetBaseStat((int)ActorStat.BloodPool, pool + actor.Stat[(int)ActorStat.BloodPool]);
            Rpg.EffectRuntime.Recompute(thing, EffectKind.Stat, (int)ActorStat.BloodPool);
            actor.SetBaseStat((int)ActorStat.Blood, actor.Stat[(int)ActorStat.BloodPool]);
            Rpg.EffectRuntime.Recompute(thing, EffectKind.Stat, (int)ActorStat.Blood);
        }
        actor.MaxHealth += health;
        Rpg.EffectRuntime.Heal(thing, health);
    }
}
