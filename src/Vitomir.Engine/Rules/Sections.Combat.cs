// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rules file's combat sections: the blow, the shot, the flinch and the wounds, the stake and the gore. Each default is the
// engine's own named constant, 1.1's number (GameRules).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rules;

[RuleSection("Combat", "The melee blow: whether it lands, how hard, the critical, and how much the target soaks.")]
public sealed class CombatRules
{
    [Rule("A blow lands when the to-hit margin (offence less defence, truncated toward zero) is greater than this.")]
    public int HitMargin = Rpg.Combat.HitMargin;

    [Rule("Over this margin a blow is a critical: doubled, and it may stake. Shots and the stake read it too.")]
    public int CriticalMargin = Rpg.Combat.CriticalMargin;

    [Rule("What a doubling (silver, a critical) adds to the blow's reported multiplier: the hit reaction and the messages read it. "
        + "The damage itself is doubled; this number does not scale it.")]
    public float CriticalMultiplierStep = Rpg.Combat.MultiplierStep;

    [Rule("Whether a silver weapon against a HURTBYSILVER target doubles the damage (before the critical).")]
    public bool SilverDoubles = true;

    [Rule("Defence lost by a target that is restrained or feeding.")]
    public float HelplessDefencePenalty = Rpg.Combat.HelplessDefencePenalty;

    [Rule("Defence a target loses from behind: the facing dot times this (a player's attack only).")]
    public float BackstabScale = Rpg.Combat.BackstabScale;

    [Rule("How much the two must face the same way (the dot of their facings) for the backstab.")]
    public float BackstabMinDot = Rpg.Combat.BackstabDot;

    [Rule("Whether the backstab is a player's attack's only.")]
    public bool BackstabPlayersOnly = true;

    [Rule("Each damage die is the floor plus a roll times this span: 9 makes a d10 of 1 to 10.")]
    public float DamageDieSpan = Rpg.Combat.DieSpan;

    [Rule("The floor of each damage die.")]
    public float DamageDieFloor = Rpg.Combat.DieFloor;

    [Rule("The damage pool's points per die, and the dice per point (its inverse, which 1.1 multiplies by); the remainder is scaled by one roll.",
        Count = 2, Values = "points per die, dice per point")]
    public float[] DamagePointsPerDie = [Rpg.Combat.PointsPerDie, Rpg.Combat.DicePerPoint];

    [Rule("Whether the damage pool adds the attacker's Strength (an attack flagged 0x10000 never does).")]
    public bool DamageIncludesStrength = true;

    [Rule("NORMAL (bashing) soak: the target's soak plus its Stamina times the first share; Fortitude times the second.",
        Count = 2, Values = "Stamina share, Fortitude share")]
    public float[] SoakNormal = [1f, 1f];

    [Rule("LETHAL soak: the soak plus Stamina times the first share (a human's times the second); Fortitude times the third.",
        Count = 3, Values = "Stamina share, a human's Stamina share, Fortitude share")]
    public float[] SoakLethal = [1f, 0f, 1f];

    [Rule("AGGRAVATED soak: the soak plus Stamina times the first share; Fortitude times the second.",
        Count = 2, Values = "Stamina share, Fortitude share")]
    public float[] SoakAggravated = [0f, 1f];

    [Rule("Electric, Fire, Sun, Faith and Cold are soaked at this share of the soak, and of Fortitude.")]
    public float ElementalSoakShare = Rpg.Combat.ElementalSoakShare;

    [Rule("Whether Poison and Disease are soaked by the soak alone, Fortitude not counting.")]
    public bool PoisonDiseaseIgnoreFortitude = true;

    [Rule("Health a character must keep to stay up; under it, damage is refused as past caring.")]
    public float DeathThreshold = Rpg.Combat.DeathThreshold;

    [Rule("The actor types (0 vampire, 1 ghoul, 2 human, 3 monster) that take no SUN or FAITH damage. The ghoul's immunity is 1.1's.")]
    public int[] SunAndFaithImmuneTypes = [(int)ActorType.Human, (int)ActorType.Monster, (int)ActorType.Ghoul];

    [Rule("The share of a shot a party member takes from an attacker outside the party.")]
    public float PartyShotShare = Rpg.Combat.PartyShotShare;

    [Rule("What a Cappadocian's FAITH damage is multiplied by.")]
    public float CappadocianFaithMultiplier = Rpg.Combat.CappadocianFaith;

    [Rule("What a melee blow on a container adds to the damage (it has no to-hit roll).")]
    public float ContainerMeleeBonus = Container.MeleeBonus;

    [Rule("The chance a strike comes with its vox_attack bark (a draw on the random stream).", Min = 0, Max = 1)]
    public float VoxAttackChance = Actions.VoxAttackChance;
}

[RuleSection("Ranged", "Shots: the scatter that makes a miss, the accuracy roll, the dice and the bullet.")]
public sealed class RangedRules
{
    [Rule("The scatter: spread = scale − (roll × scale + accuracy + Dexterity), times the percent; the aim turns by (roll × span − half) × spread degrees.",
        Count = 4, Values = "scale, percent, span, half")]
    public float[] ScatterFormula = [Projectiles.Hundred, Projectiles.Percent, Projectiles.ScatterSpan, Projectiles.ScatterHalf];

    [Rule("The accuracy roll: a roll times Dexterity times these shares, plus the effects, for the firer and the target; it decides only the critical and a bullet's type.",
        Count = 2, Values = "firer's Dexterity share, target's Dexterity share")]
    public float[] ProjectileAccuracyRoll = [1f, 1f];

    [Rule("What either side of the accuracy roll that is not a character rolls: a roll times this (traps, props).")]
    public float ProjectileUnownedRoll = Projectiles.Unnamed;

    [Rule("A shot's dice: its damage times the dice per point gives the count of dice, each the floor plus a roll times the span, the remainder over the points per die scaled by one roll.",
        Count = 4, Values = "dice per point, points per die, die span, die floor")]
    public float[] ProjectileDice = [Projectiles.DicePerPoint, Projectiles.PointsPerDie, Projectiles.DieSpread, Projectiles.DieFloor];

    [Rule("Against a vampire or a ghoul a bullet is NORMAL under this accuracy margin, LETHAL at or above it.")]
    public int BulletLethalMargin = Projectiles.BulletLethal;

    [Rule("Whether a party member's shot cannot hit another party member.")]
    public bool PartyFriendlyFireOff = true;

    [Rule("How many of a thrown weapon's ITEM_AMOUNT a throw spends.", Min = 0)]
    public int ThrownSpendsOne = 1;
}

[RuleSection("Wounds", "The flinch, the wound ladder and its penalties, the limp and the block.")]
public sealed class WoundRules
{
    [Rule("A blow must be more than this to make hurt noises or a flinch.")]
    public float FlinchMinDamage = Flinch.Threshold;

    [Rule("The flinch chance in percent: damage times this, less Stamina.")]
    public float FlinchChancePerPoint = Flinch.PercentPerPoint;

    [Rule("The flinch chance's floor and ceiling, and the scale of its roll.", Count = 3, Values = "floor, ceiling, roll's scale")]
    public float[] FlinchChanceClamp = [Flinch.Floor, Flinch.Ceiling, Flinch.Hundred];

    [Rule("After a flinch, no hurt noise or flinch roll for this long (the stagger lock).", Min = 0)]
    public int FlinchQuietMs = Damage.HurtQuietMs;

    [Rule("The wound ladder: health over maximum above each value is rungs 0 to 4, else rung 5. The fourth is 1.1's 0.9, plainly meant "
        + "for 0.09, so rung 3 is never reached; 0.09 would be a fix, not 1.1's.", Count = 5)]
    public float[] WoundLadderThresholds = [.. Rpg.Wounds.Thresholds];

    [Rule("The penalties each rung (1 to 4) lays, by effect: ef_dmg_dexterity's level (-1 none).", Count = 4)]
    public int[] WoundDexterityLevels = [1, 2, 3, 3];

    [Rule("ef_dmg_strength's level by rung 1 to 4 (-1 none).", Count = 4)]
    public int[] WoundStrengthLevels = [-1, 2, 3, 3];

    [Rule("ef_dmg_movespeed's level by rung 1 to 4 (-1 none): the limp.", Count = 4)]
    public int[] WoundMoveSpeedLevels = [-1, 2, 3, 4];

    [Rule("How long ef_dmg_movespeed lasts by rung 1 to 4, in milliseconds.", Count = 4)]
    public int[] WoundMoveSpeedMs = [0, 8000, 20000, 50000];

    [Rule("MOVESPEED at or under this sets LIMPING.")]
    public float LimpBelowMoveSpeed = Actor.Flag2.LimpBelow;

    [Rule("Whether a melee blow soaked away entirely, with something in the off hand, plays the block.")]
    public bool ShieldBlockOnFullSoak = true;
}

[RuleSection("Stake", "The stake, the beheading, the special throw, the gibs and the gore.")]
public sealed class StakeRules
{
    [Rule("A critical with a staking attack on a vampire rolls (whole) a roll times this against STAKEPROTECTION; above it stakes, with no damage.")]
    public float StakeRoll = Stake.RollScale;

    [Rule("A stake comes out by itself after this long, in milliseconds.", Min = 0)]
    public int StakeDurationMs = Stake.StakedForMs;

    [Rule("How close an unstaker closes (less its radius), and how far in front it stands.")]
    public float UnstakeReach = Stake.UnstakeReach;

    [Rule("The unstake approach asks again this often, in milliseconds.", Min = 1)]
    public int UnstakeRetryMs = Stake.UnstakeRetryMs;

    [Rule("On a surviving melee blow with CANBEHEAD: behead when a roll times the scale is under Strength times the share plus the damage.",
        Count = 2, Values = "scale, Strength share")]
    public float[] BeheadChance = [Beheading.Hundred, Beheading.StrengthShare];

    [Rule("How long ef_beheaded runs, in milliseconds.", Min = 0)]
    public int BeheadEffectMs = Beheading.EffectMs;

    [Rule("A beheading's even chance: a roll at or over this is the instant death, under it the bleed-out.", Min = 0, Max = 1)]
    public float BeheadBleedChoice = Beheading.Even;

    [Rule("The bleed per tick as a share of maximum health: the instant death's, the bleed-out's (five ticks).", Count = 2,
        Values = "instant, bleed-out")]
    public float[] BeheadBleedValues = [Beheading.Quick, Beheading.Slow];

    [Rule("The damage handed to the death effects when a thrower died mid-throw (under the quick death's, so the slow death).")]
    public float SpecialThrowDeadThrowerDamage = SpecialMove.DeadThrowerDamage;

    [Rule("A CANGIBKILL character comes apart on a killing blow over this (no corpse, nothing to raise).")]
    public float GibKillDamage = Damage.GibDamage;

    [Rule("How many sub-pieces a gib asks for (twice each).", Min = 0, Max = 64)]
    public int GibPieces = Damage.GibPieces;

    [Rule("The damage over which a hit throws one loose chunk, and two (a chunk is a projectile).", Count = 2, Values = "one chunk, two")]
    public float[] GibSprayThresholds = [Damage.GibSprayFirst, Damage.GibSpraySecond];

    [Rule("The speed a severed head or a chunk is launched at.")]
    public float GoreLaunchSpeed = Gore.LaunchSpeed;

    [Rule("How long a wound's blood or sparks show per damage point (ms), the bone chance, the death's bone chance, the blood's own time (ms) "
        + "and the death's (ms). The look only, but its rolls are on the random stream.", Count = 5,
        Values = "ms per point, bone chance, death's bone chance, blood ms, death ms")]
    public float[] WoundShowMsPerPoint = [WoundEffects.MsPerPoint, WoundEffects.BoneChance, WoundEffects.DeathBoneChance, WoundEffects.BloodMs, WoundEffects.DeathMs];
}
