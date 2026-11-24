// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rules file's sections for a character: its stats, the rank ladder, healing, and what advancement costs. Each default is the
// engine's own named constant, 1.1's number (GameRules).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rules;

[RuleSection("Stats", "A character's stats before its template and sheet are read, and the limits every stat write is held to. "
    + "A stat by number: 0 Strength, 1 Dexterity, 2 Stamina, 3 Perception, 4 Intelligence, 5 Wits, 6 Blood, 7 Humanity, 8 Frenzy, "
    + "9 Generation, 10 Faith, 11 Charisma, 12 Manipulation, 13 Appearance, 14 Frenzy Rating, 15 Blood Rating, 16 Blood Pool, 17 Mana, "
    + "18 Mana Pool.")]
public sealed class StatRules
{
    [Rule("The value every stat (base and current) holds before a template's tags are read. A stat the template does not name keeps it.")]
    public float StatUnsetDefault = Actor.UnsetStat;

    [Rule("Health and maximum health before a template sets them.", Count = 2, Values = "health, maximum health")]
    public float[] HealthDefault = [Actor.DefaultHealth, Actor.DefaultHealth];

    [Rule("The actor type a template that names none gets (0 vampire, 1 ghoul, 2 human, 3 monster). 83 of the 696 actor templates rely on it.", Min = 0, Max = 3)]
    public int ActorTypeDefault = (int)ActorType.Vampire;

    [Rule("The team a template that names none gets (8 is TEAM_NONE, nobody's side).", Min = 0, Max = 19)]
    public int TeamDefault = Actor.Teams.None;

    [Rule("The ceiling a stat with no authored maximum is held to, at every stat write.")]
    public float StatCeiling = Actor.StatCeiling;

    [Rule("The floor every stat write of a character that is not a player's is held to.")]
    public float StatFloor = Actor.StatFloor;

    [Rule("An authored stat limit of this value means no limit.", Fixed = true, FixedWhy = "a marker of the sheets' and clans' files, not a rule")]
    public float StatNoLimitMarker = Actor.NoStatLimit;

    [Rule("Every stat's maximum when a player's sheet is reset before it is read, and the two pools' (blood pool and mana pool).", Count = 2,
        Values = "every stat, the two pools")]
    public float[] SheetResetMax = [CharacterSheets.ResetMax, CharacterSheets.ResetPoolMax];

    [Rule("The values a sheet's reset writes before the sheet's own, by stat number. "
        + "They are written in 1.1's order, the pools before what they hold.", Count = Actor.Stats)]
    public float[] SheetResetValues = CharacterSheets.ResetDefaults();

    [Rule("The stats the character sheet offers a raise button for, by stat number: the six physical and mental, the three social.")]
    public int[] RaisableStats = [.. Rpg.Advancement.RaisableDefault];

    [Rule("The stats only a human's sheet may raise, by stat number: Faith (10).")]
    public int[] RaisableStatsHuman = [.. Rpg.Advancement.RaisableHumanDefault];

    [Rule("The clamp on both spendable and lifetime experience.", Min = 0)]
    public long ExperienceCap = Actor.MaxExperience;
}

[RuleSection("Ranks", "The rank ladder: the lifetime experience each rank needs, and what reaching it gives. Per rank and actor type, not per generation.")]
public sealed class RankRules
{
    [Rule("Lifetime experience needed for ranks 0 to 4. Rank is reckoned from lifetime experience, so spending never demotes.", Count = Ui.CharacterPane.MaxRanks)]
    public int[] RankThresholds = [.. Ui.CharacterPane.RankThresholds];

    [Rule("How many ranks there are (ACTOR_MAXRANKS).", Fixed = true, FixedWhy = "the rank titles' keys and the tables are made for five")]
    public int RankCount = Ui.CharacterPane.MaxRanks;

    // The rank bonuses, one pair of keys an actor type (they were two lists of sixteen, rank by type, which were hard to read).
    const string RankValues = "rank 1, rank 2, rank 3, rank 4";

    [Rule("A vampire's rank bonus to the blood pool on reaching ranks 1 to 4; the pool then refills.", Count = 4, Values = RankValues)]
    public int[] RankBonusVampirePool = Experience.BonusDefaults(ActorType.Vampire, pool: true);

    [Rule("A vampire's rank bonus to maximum health on reaching ranks 1 to 4; the character is healed by as much.", Count = 4, Values = RankValues)]
    public int[] RankBonusVampireHealth = Experience.BonusDefaults(ActorType.Vampire, pool: false);

    [Rule("A ghoul's rank bonus to the blood pool on reaching ranks 1 to 4; the pool then refills.", Count = 4, Values = RankValues)]
    public int[] RankBonusGhoulPool = Experience.BonusDefaults(ActorType.Ghoul, pool: true);

    [Rule("A ghoul's rank bonus to maximum health on reaching ranks 1 to 4; the character is healed by as much.", Count = 4, Values = RankValues)]
    public int[] RankBonusGhoulHealth = Experience.BonusDefaults(ActorType.Ghoul, pool: false);

    [Rule("A human's rank bonus to the mana pool on reaching ranks 1 to 4; the pool then refills.", Count = 4, Values = RankValues)]
    public int[] RankBonusHumanPool = Experience.BonusDefaults(ActorType.Human, pool: true);

    [Rule("A human's rank bonus to maximum health on reaching ranks 1 to 4; the character is healed by as much.", Count = 4, Values = RankValues)]
    public int[] RankBonusHumanHealth = Experience.BonusDefaults(ActorType.Human, pool: false);

    [Rule("A monster's rank bonus to the blood pool on reaching ranks 1 to 4 (nothing in 1.1); the pool then refills.", Count = 4, Values = RankValues)]
    public int[] RankBonusMonsterPool = Experience.BonusDefaults(ActorType.Monster, pool: true);

    [Rule("A monster's rank bonus to maximum health on reaching ranks 1 to 4 (nothing in 1.1); the character is healed by as much.", Count = 4, Values = RankValues)]
    public int[] RankBonusMonsterHealth = Experience.BonusDefaults(ActorType.Monster, pool: false);

    /// <summary>The pool and health bonuses of an actor type (0 vampire, 1 ghoul, 2 human, anything else a monster) for ranks 1 to 4.</summary>
    public (int[] Pool, int[] Health) RankBonus(int type) => type switch
    {
        (int)ActorType.Vampire => (RankBonusVampirePool, RankBonusVampireHealth),
        (int)ActorType.Ghoul => (RankBonusGhoulPool, RankBonusGhoulHealth),
        (int)ActorType.Human => (RankBonusHumanPool, RankBonusHumanHealth),
        _ => (RankBonusMonsterPool, RankBonusMonsterHealth),
    };

    [Rule("Whether crossing two ranks in one award pays only the last rank's bonus (1.1's quirk). false pays each rank crossed, in turn.")]
    public bool MultiRankAwardsOnlyLast = true;

    [Rule("Maximum health gained when a human is made a vampire (the scripts' SetActorType). 0: nothing, as Vitomir has played it.",
        Ours = true, Unsure = "1.1 was measured giving +20 at the Embrace; where it comes from is not found in the scripts, "
        + "the born effects or the type's setter, so the default is 0 until it is")]
    public float EmbraceMaxHealthGain = 0f;
}

[RuleSection("Healing", "Healing and what the engine itself drains. Most regeneration is data (the born effects' rates are misc/effects.ned's).")]
public sealed class HealingRules
{
    [Rule("The effects a vampire is born with.")]
    public string[] BornEffectsVampire = [.. Location.VampireBorn];

    [Rule("The effects a ghoul is given when revived (a ghoul is born with none).")]
    public string[] BornEffectsGhoul = [.. Location.VampireBorn];

    [Rule("The effects a human is born with.")]
    public string[] BornEffectsHuman = [.. Location.HumanBorn];


    /// <summary>The effects of a type as a revive (or a network character's making) puts them back: none for a monster.</summary>
    public string[] Born(ActorType type) => type switch
    {
        ActorType.Vampire => BornEffectsVampire,
        ActorType.Ghoul => BornEffectsGhoul,
        ActorType.Human => BornEffectsHuman,
        _ => [],
    };

    [Rule("Frenzy a heal takes off a vampire or a ghoul for each point of health asked (not the points that fitted).")]
    public float HealCalmsFrenzy = Rpg.EffectRuntime.HealCalmsFrenzy;

    [Rule("Nobody under this much health is healed.")]
    public float HealFloor = Rpg.Combat.DeathThreshold;

    [Rule("Whether a human (not NOBLEED) whose blood reaches exactly 0 dies on the spot.")]
    public bool HumanBleedsOutAtZeroBlood = true;
}

[RuleSection("Advancement", "What experience buys: an attribute point, a discipline level, and the network game's party award.")]
public sealed class AdvancementRules
{
    [Rule("The base-value bands of an attribute's cost rate: under the first, the first rate; under the second, the second; and so on; "
        + "at or over the last, the last rate.", Count = 5)]
    public float[] StatRaiseCostBands = [.. Rpg.Advancement.CostBands];

    [Rule("The rate of each band: an attribute point costs (whole) base value times the rate. Christof's Strength 30 costs 120.", Count = 6)]
    public float[] StatRaiseCostRates = [.. Rpg.Advancement.CostRates];

    [Rule("What one purchase adds, what a Storyteller's lowering takes, and the floor a lowering stops at.")]
    public float StatRaiseStep = Rpg.Advancement.LowerStep;

    [Rule("The multiplier on a discipline's experience column: learning it, then levels 0 to 1, 1 to 2, 2 to 3 and 3 to 4. "
        + "Worked in single precision, as 1.1 does: 500 × 1.4 is 700.", Count = 5)]
    public float[] DisciplineLevelMultipliers = [.. Rpg.Advancement.LevelMultipliers];

    [Rule("The cost multiplier outside the clan's three groups, and for everything for a Caitiff or a character with no clan (in clan: 1).")]
    public float DisciplineOutOfClanMultiplier = Rpg.Advancement.OutOfClan;

    [Rule("Whether learning a group's first discipline also pays the group's own price (game.ndg's).")]
    public bool DisciplineGroupFirstPrice = true;

    [Rule("A discipline's levels.", Fixed = true, FixedWhy = "the shape of the discipline file (game.ndd)")]
    public int DisciplineMaxLevel = Formats.NddDiscipline.Levels;

    [Rule("Whether, in a network game, a party award is divided evenly among the players present (a zero share gives nothing). "
        + "false gives each the whole award, as a single-player party award does.")]
    public bool PartyAwardSplitNetwork = true;
}
