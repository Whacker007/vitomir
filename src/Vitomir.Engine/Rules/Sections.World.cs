// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rules file's sections for the rest of the game: casting, feeding, frenzy and Humanity, death, movement, sight, the minds,
// items and prices, time, and the party and network limits. Each default is the engine's own named constant (GameRules).
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rules;

[RuleSection("Casting", "What the engine adds to a discipline's cast. Costs, ranges, recasts and effects are game.ndd's and misc/effects.ned's; each discipline's behaviour is its script.")]
public sealed class CastingRules
{
    [Rule("The blood (a human's mana) a cast costs is paid whether it succeeds, fizzles or is resisted.", Fixed = true,
        FixedWhy = "the engine pays before the discipline's script decides a fizzle or a resist; it cannot know")]
    public bool CastCostPaidAlways = true;

    [Rule("Whether a cast's cost is multiplied by the caster's BLOODCOST effect.")]
    public bool CastCostScale = true;

    [Rule("A cast out of range walks up and asks again this often, in milliseconds.", Min = 1)]
    public int CastApproachRetryMs = Cast.ApproachRetryMs;

    [Rule("How far a Terrifying cast's event carries to minds.")]
    public float TerrifyEventRange = Cast.TerrifyRange;

    [Rule("The cast count a scroll's temporary discipline is made with.", Unsure = "what the count changes in play is not clear")]
    public int ScrollCastCount = Cast.ScrollCastCount;

    [Rule("Quick slots for disciplines and items.", Fixed = true, FixedWhy = "the interface's layouts have six")]
    public int QuickSlotCount = DisciplineList.QuickSlots;
}

[RuleSection("Feeding", "The bite: the approach, the resist, the drain and its clock, the wrist, and the release.")]
public sealed class FeedingRules
{
    [Rule("Close enough to bite, body to body.")]
    public float FeedReach = Feeding.Reach;

    [Rule("The approach looks again this often, in milliseconds.", Min = 1)]
    public int FeedApproachRetryMs = Feeding.ApproachRetryMs;

    [Rule("Within this the feeder walks to its victim; beyond it, runs.")]
    public float FeedWalkWithin = Feeding.WalkWithin;

    [Rule("The chance of vox_feedchase on a run (a draw on the random stream).", Min = 0, Max = 1)]
    public float FeedChaseBarkChance = Feeding.ChaseBarkChance;

    [Rule("Where the victim is put in front of the feeder: for the neck, for the wrist.", Count = 2, Values = "neck, wrist")]
    public float[] FeedVictimGap = [Feeding.NeckGap, Feeding.WristGap];

    [Rule("The feeder's chance against a resisting victim: Feed level times this, plus the feeder's Wits, less the victim's Wits and FEEDPROTECTION "
        + "(only an armed, hostile, non-pedestrian, unmesmerised victim resists).")]
    public float FeedResistPerLevel = Feeding.PerFeedLevel;

    [Rule("The floor and cap of that chance.", Count = 2, Values = "floor, cap")]
    public float[] FeedResistClamp = [Feeding.ResistFloor, Feeding.ResistCap];

    [Rule("The team that never resists (TEAM_PEDESTRIANS).", Fixed = true, FixedWhy = "a team's identity, not a number")]
    public int FeedPedestrianTeam = Feeding.PedestrianTeam;

    [Rule("Blood the victim loses each drain.")]
    public float FeedDrainPerTick = Feeding.Drain;

    [Rule("Blood the feeder gains each drain: its BloodRating times this (paid before the empty check, so the killing drain still pays).")]
    public float FeedGainPerBloodRating = Feeding.GainPerBloodRating;

    [Rule("The time between drains, in milliseconds: a feeder's, a FASTDRINKER's.", Count = 2, Values = "feeder, fast drinker", Min = 1)]
    public int[] FeedTickMs = [Feeding.TickMs, Feeding.FastTickMs];

    [Rule("The first drain comes this long after the bite, in milliseconds; and the bite keeps the feeder busy as long.", Count = 2,
        Values = "first drain, the bite's busy time", Min = 0)]
    public int[] FeedFirstDrainMs = [Feeding.FirstDrainMs, Feeding.BiteBusyMs];

    [Rule("An AI feeder does not think for its feedDuration (a roll times the .nai's) plus this, in milliseconds: anybody's, a party member's.",
        Count = 2, Values = "anybody, a party member")]
    public int[] FeedAiThinkPad = [Feeding.ThinkPadMs, Feeding.CompanionThinkPadMs];

    [Rule("A victim held by its feeder looks again this often, in milliseconds.", Min = 1)]
    public int FeedVictimCheckMs = Feeding.VictimCheckMs;

    [Rule("A wrist donor at or under this blood gives nothing; a wrist feed stops before it.")]
    public float FeedDonorFloor = Feeding.DonorFloor;

    [Rule("Letting go at the neck early kills when the victim's blood is under this over the Feed level (this at levels 0 and 1), and under a roll of it.")]
    public float FeedReleaseKillBase = Feeding.ReleaseKillBase;

    [Rule("The chance of the suck noise and of the bloodpointdrain noise each drain (draws on the random stream).", Count = 2,
        Values = "suck, bloodpointdrain", Min = 0, Max = 1)]
    public float[] FeedSuckNoiseChance = [Feeding.SuckNoiseChance, Feeding.SuckNoiseChance];

    [Rule("A network character's base BloodRating by clan as 1.1 makes one: the Ventrue's (their weakness), the Giovanni's.", Count = 2,
        Values = "Ventrue, Giovanni")]
    public float[] NetClanBloodRating = [Ui.MpCreateSetUp.VentrueBloodRating, Ui.MpCreateSetUp.GiovanniBloodRating];

    [Rule("Whether a new Giovanni network character is a FASTDRINKER (the drain twice as often): the clan's deadlier feed.")]
    public bool GiovanniFastDrinker = true;
}

[RuleSection("Frenzy", "Frenzy and Humanity: when a vampire frenzies and calms, what a blow stirs, and what a kill costs.")]
public sealed class FrenzyRules
{
    [Rule("At or above this frenzy a vampire or a ghoul frenzies.")]
    public float FrenzyThreshold = Rpg.Frenzy.Threshold;

    [Rule("At or below this a frenzy ends.")]
    public float FrenzyCalm = Rpg.Frenzy.Calm;

    [Rule("Crossing this upward says vox_nearfrenzy.")]
    public float FrenzyNearBark = Rpg.Frenzy.Near;

    [Rule("The effects a frenzy lays beside ef_frenzy (level 0), and takes off at its end.")]
    public string[] FrenzyEffects = [.. Rpg.Frenzy.StatEffects];

    [Rule("The chance a frenzy costs a clan with the frenzy-appearance weakness (the Gangrel) a point of base Appearance.", Min = 0, Max = 1)]
    public float GangrelFrenzyAppearanceChance = Rpg.Frenzy.GangrelChance;

    [Rule("Frenzy a blow adds = damage × (Blood × scale × blood term + empty term) × (FrenzyRating × scale × rating term + zero term) "
        + "× (fire term for FIRE) × (Wits × scale × Wits term + 1 when Wits is over the last value).", Count = 8,
        Values = "scale, blood term, empty term, rating term, zero term, fire term, Wits term, Wits over")]
    public float[] FrenzyFromDamage = [Rpg.Combat.StatScale, Rpg.Combat.BloodCalms, Rpg.Combat.TwoAtEmpty, Rpg.Combat.RatingStirs,
                                       Rpg.Combat.HalfAtZero, Rpg.Combat.FireFrenzy, Rpg.Combat.WitsCalm, Rpg.Combat.WitsMatterAbove];

    [Rule("What the scripts' ActorFrenzy and ActorUnfrenzy apply to frenzy.", Count = 2, Values = "provoke, soothe")]
    public float[] ProvokeFrenzyValue = [Rpg.Frenzy.ProvokeValue, Rpg.Frenzy.SootheValue];

    [Rule("Humanity a HUMANITYTRACK vampire loses (base) for each HUMANITYLOSS kill. In a network game only with Track Humanity.")]
    public float HumanityPerKill = Damage.HumanityPerKill;

    [Rule("The actor types (0 vampire, 1 ghoul, 2 human, 3 monster) a kill's Humanity is taken from.")]
    public int[] HumanityLossActorTypes = [(int)ActorType.Vampire];

    [Rule("At or above this Humanity a loss says vox_lostHumanity; under it, vox_lowHumanity.")]
    public float LowHumanityBark = Damage.LowHumanity;

    [Rule("A revive raises Humanity to this when it is below it.")]
    public float HumanityFloorOnRevive = Damage.HumanityFloorOnRevive;

    [Rule("Whether a HUMANITYTRACK character at Humanity 0 ends the game (in a network game it is a permanent frenzy instead).")]
    public bool HumanityZeroEndsGame = true;

    [Rule("A vampire or a ghoul falling to this share of its pool says vox_lowblood.")]
    public float LowBloodBarkFraction = Stats.LowBloodFraction;

    [Rule("Whether humans and monsters have no frenzy (a write of it is made 0).")]
    public bool FrenzyCapActorTypes = true;
}

[RuleSection("Death", "Death, the corpse's clocks, the dust and the revive.")]
public sealed class DeathRules
{
    [Rule("A killing blow over this plays the quick death; at or under it, the slow death (or the sun's).")]
    public float QuickDeathDamage = Damage.QuickDeathDamage;

    [Rule("An ordinary body lies still this long, in milliseconds, then fades: raising (TARGET DEAD casts) and looting stop when the fade begins.", Min = 0)]
    public int CorpseFadeMs = Damage.Corpse.FadeMs;

    [Rule("A party member's body starts to fade after this, in milliseconds (a party member can always be raised).", Min = 0)]
    public int PartyCorpseFadeMs = Damage.Corpse.PartyFadeMs;

    [Rule("An ordinary body is taken away this long after death, in milliseconds.", Min = 0)]
    public int CorpseExpireMs = Damage.Corpse.ExpireMs;

    [Rule("How long the fade itself takes, in milliseconds: a body's, dust's.", Count = 2, Values = "body, dust")]
    public float[] CorpseFadeDurationMs = [Damage.Corpse.FadeDurationMs, Damage.Corpse.DustFadeMs];

    [Rule("How long the dust's skeleton and its smoke last (ms), and the smoke's chance at each bone (a draw on the random stream).", Count = 3,
        Values = "skeleton ms, smoke ms, smoke chance")]
    public float[] SkeletonLifetimeMs = [Damage.Corpse.SkeletonMs, Damage.Corpse.SmokeMs, Damage.DustSmokeChance];

    [Rule("What a revive sets frenzy to (health, blood and mana are the fractions its caller gives).")]
    public float ReviveDefaults = 0f;

    [Rule("A fallen member taken out of the party is revived at this share of health and of blood.")]
    public float PartyLeaverReviveFraction = PartyLeaving.ReviveFraction;

    [Rule("Whether a dead pedestrian revives where it was first placed.")]
    public bool PedestrianGoesHome = true;

    [Rule("Whether a vampire's death is announced as entering torpor (RPG_DIEDV); it is the same death either way.")]
    public bool TorporIsDeathForVampires = true;

    [Rule("The hit kind a drained human or a feed's kill dies of.", Fixed = true, FixedWhy = "an identity the death's look and the scripts read")]
    public int DrainKillHitKind = (int)HitKind.Drain;
}

[RuleSection("Movement", "Walking and running, the route, the approach and the reach. The speeds themselves are the templates'.")]
public sealed class MovementRules
{
    [Rule("A destination further than this is run to, nearer walked (the steered character's, the drop's, the use's and the take's).")]
    public float RunBeyond = Player.RunBeyond;

    [Rule("Over this commanded speed the run cycle plays.")]
    public float RunAnimationBeyond = Player.RunAnimationBeyond;

    [Rule("The walk and run speeds of a character with none of its own.", Ours = true, Count = 2, Values = "walk, run")]
    public float[] DefaultWalkRunSpeed = [Player.DefaultWalkSpeed, Player.DefaultRunSpeed];

    [Rule("A route costing more than this is refused (a far click does nothing).")]
    public float RouteMaxCost = PathFinder.MaxCost;

    [Rule("The toll added at each route node (which changes the path chosen).")]
    public float RouteStepPenalty = PathFinder.StepPenalty;

    [Rule("The most steps a route may have.", Fixed = true, FixedWhy = "the route's step count is a byte")]
    public int RouteMaxSteps = PathFinder.MaxSteps;

    [Rule("Sideways directions tried before a detour is given up.", Min = 0)]
    public int DetourTries = Player.DetourTries;

    [Rule("How long a blocked walker waits before it tries again, in seconds: for a person, for anything else.", Count = 2, Values = "a person, a thing")]
    public float[] WaitBeforeRetry = [Player.WaitForPerson, Player.WaitForThing];

    [Rule("Half-second waits an AI walk takes before giving up (1.1 never gives up; this repair is ours).", Ours = true, Min = 0)]
    public int AiWalkGiveUpWaits = Player.MindGiveUpAfter;

    [Rule("How close counts as arrived.")]
    public float ArrivedDistance = Player.Arrived;

    [Rule("How long an attacker waits before it tries to close again, in milliseconds.", Min = 1)]
    public int AttackApproachRetryMs = Actions.ApproachRetryMs;

    [Rule("How close to use something.")]
    public float UseReach = Actions.UseReach;

    [Rule("The use order's retry, in milliseconds.", Min = 1)]
    public int UseRetryMs = Actions.UseRetryMs;

    [Rule("The take order's retry, in milliseconds.", Min = 1)]
    public int TakeRetryMs = Actions.TakeRetryMs;

    [Rule("How close to take from, or put on, the floor.")]
    public float TakePutReach = Stow.Reach;

    [Rule("The gap added to both bodies' radii on the approach's last two attempts.")]
    public float ApproachBodyGap = Actions.ApproachMargin;

    [Rule("The share of a melee attack's min..max reach a moving attacker must close to.")]
    public float MeleeMidReach = WeaponAttack.MidReach;

    [Rule("A weapon record's defaults when its template sets none: ammo use, maximum and minimum distance, throw distance, throw chance.",
        Count = 5, Values = "ammo use, max distance, min distance, throw distance, throw chance")]
    public float[] WeaponDefaults = [WeaponAttack.DefaultAmmoUse, WeaponAttack.DefaultMaxDistance, WeaponAttack.DefaultMinDistance,
                                     Weapon.DefaultThrowDistance, Weapon.DefaultThrowChance];
}

[RuleSection("Sight", "Stealth and sight: what being busy costs a hider, and the sight line.")]
public sealed class SightRules
{
    [Rule("Stealth lost while feeding or while a swing plays.", Min = 0)]
    public int StealthPenaltyFeedOrSwing = Sight.FeedingOrSwinging;

    [Rule("Stealth lost while a cast plays.", Min = 0)]
    public int StealthPenaltyCasting = Sight.Casting;

    [Rule("Stealth lost while walking.", Min = 0)]
    public int StealthPenaltyWalking = Sight.Walking;

    [Rule("A sight line starts at this share of the looker's height and aims at this share of the target's.", Count = 2, Values = "the looker's eye, the target")]
    public float[] SightLineHeights = [Sight.EyeShare, Sight.TargetShare];

    [Rule("A sight line shorter than this always sees.")]
    public float SightAlwaysWithin = Sight.SightMinimum;

    [Rule("A character's perception and stealth before any effect (HIDE, SENSE) sets them: at 0 everybody sees everybody.", Count = 2,
        Values = "perception, stealth", Min = 0, Max = 255)]
    public int[] PerceptionStealthDefaults = [0, 0];
}

[RuleSection("Ai", "The minds' own timings, ranges and odds around the .nai classes' tunables (attack and cast rates, lowhealth, breakDist, feedDuration).")]
public sealed class AiRules
{
    [Rule("The AI's roll is its rand() divided by this.", Fixed = true, FixedWhy = "part of the random stream's definition")]
    public float AiRandomScale = 32767f;

    [Rule("The radius the balance of strength is taken over.")]
    public float ThreatRange = Minds.ThreatRange;

    [Rule("The strength ratios over which danger is 1 to 6 (else 7).", Count = 6)]
    public float[] DangerBands = [.. Minds.DangerBands];

    [Rule("A vampire of generation at or under the cap counts (base − generation) × the step.", Count = 3, Values = "cap, base, step")]
    public float[] StrengthGeneration = [Minds.StrengthGenerationCap, Minds.StrengthGenerationBase, Minds.StrengthPerGeneration];

    [Rule("Each known discipline counts (level + 1) × this.")]
    public int StrengthPerDisciplineLevel = Minds.StrengthPerDisciplineLevel;

    [Rule("The health-share steps under which the strength penalties start.", Count = 5)]
    public float[] StrengthHealthSteps = [.. Minds.StrengthSteps];

    [Rule("The penalty under each health step.", Count = 5)]
    public int[] StrengthHealthPenalty = [.. Minds.HealthPenalty];

    [Rule("The penalty for blood or mana under each step.", Count = 5)]
    public int[] StrengthReservePenalty = [.. Minds.ReservePenalty];

    [Rule("The discipline ladder's gates: buff over a danger within a time (ms), hide likewise, summon likewise, and feed under a danger.",
        Count = 7, Values = "buff danger, buff ms, hide danger, hide ms, summon danger, summon ms, feed under danger")]
    public int[] LadderDangerGates = [.. Minds.LadderGates];

    [Rule("A vampire's chance to feed mid-fight, one that is not a companion: with low blood, and otherwise.", Count = 2,
        Values = "low blood, otherwise", Min = 0, Max = 1)]
    public float[] AiOpportunisticFeedChance = [Minds.FeedChances[0], Minds.FeedChances[2]];

    [Rule("A companion vampire's chance to feed mid-fight: with low blood, and otherwise.", Count = 2,
        Values = "low blood, otherwise", Min = 0, Max = 1)]
    public float[] AiOpportunisticFeedChanceCompanion = [Minds.FeedChances[1], Minds.FeedChances[3]];

    [Rule("A party member's percentage for casting a buff, by stance.", Count = 3, Values = "attack, neutral, defence")]
    public float[] StanceCastScaleBuff = [.. Minds.StanceScales[..3]];

    [Rule("A party member's percentage for casting a damage discipline, by stance.", Count = 3, Values = "attack, neutral, defence")]
    public float[] StanceCastScaleDamage = [.. Minds.StanceScales[3..]];

    [Rule("Blood a player-type buffer or summoner keeps after paying (over the stance scale).")]
    public float CompanionKeepBlood = Minds.KeepBlood;

    [Rule("A quick-slot item or discipline enters the candidates this many times.", Min = 0)]
    public int QuickSlotWeight = Minds.QuickWeight;

    [Rule("How far a cast's event carries.")]
    public float CastShoutRange = Minds.CastShoutRange;

    [Rule("The verylowhealth default: the share of maximum health under which a character counts as badly hurt (the .nai key's when a file leaves it out).")]
    public float HurtShare = Minds.HurtShare;

    [Rule("A party member's scale on that share, by stance.", Count = 3, Values = "attack, neutral, defence")]
    public float[] HurtShareStance = [.. Minds.HurtStanceScales];

    [Rule("Where party members stand behind the leader: the angles, in degrees.", Count = 6)]
    public float[] FormationAngle = [.. Minds.FormationAngle];

    [Rule("And how far out each stands.", Count = 6)]
    public float[] FormationRadius = [.. Minds.FormationRadius];

    [Rule("How close a member is content to be to its leader.")]
    public float FollowReach = Minds.FollowReach;

    [Rule("The stance's scale on the leash and the formation's radius (attack, neutral, defence).", Count = 3, Values = "attack, neutral, defence")]
    public float[] StanceLeash = [.. Minds.LeashScales];

    [Rule("The stance's scale on breakDist (attack, neutral, defence).", Count = 3, Values = "attack, neutral, defence")]
    public float[] StanceFightLeash = [.. Minds.FightLeashScales];

    [Rule("How often a party member thinks, in milliseconds.", Min = 1)]
    public int PartyThinkMs = Minds.PartyThink;

    [Rule("How often an Attack-stance member looks for a fight, and when it is switched off, in milliseconds.", Count = 2, Values = "on, off", Min = 1)]
    public int[] HuntThinkMs = [Minds.HuntThink, Minds.HuntBlocked];

    [Rule("How often a character with a target thinks, in milliseconds.", Min = 1)]
    public int EnemyThinkMs = Minds.EnemyThink;

    [Rule("The share of attackRate the think delay is stretched by.")]
    public float AttackJitter = Minds.AttackJitter;

    [Rule("The chance a manoeuvre comes with vox_taunt (a draw on the random stream).", Min = 0, Max = 1)]
    public float TauntChance = Minds.TauntChance;

    [Rule("The manoeuvre's angle: its spread (degrees) and floor.", Count = 2, Values = "spread, floor")]
    public float[] ManoeuvreArc = [Minds.ManoeuvreArc, Minds.ManoeuvreFloor];

    [Rule("How long a character chases a target it never reached, in milliseconds.", Min = 0, Unsure = "whether this is 1.1's number or ours")]
    public int GiveUpOnTargetMs = Minds.GiveUpOnTarget;

    [Rule("Approach time before the target is let go, in milliseconds.", Min = 0)]
    public int ApproachForMs = Minds.ApproachFor;

    [Rule("The approach's rethink (ms), its walking rethink (ms), and the distance it stops at.", Count = 3, Values = "think ms, walking think ms, stop")]
    public float[] ApproachThinkMs = [Minds.ApproachThink, Minds.ApproachWalkThink, Minds.ApproachStop];

    [Rule("The wait after a refusal that is expected to pass, in milliseconds.", Min = 1)]
    public int RetryCombatMs = Minds.RetryCombat;

    [Rule("How often a character with nothing to shoot says so, in milliseconds.", Min = 0)]
    public int OutOfAmmoMessageMs = Minds.OutOfAmmoMessage;

    [Rule("How far an attack carries to other minds.")]
    public float AttackShoutRange = Minds.AttackShoutRange;

    [Rule("How far the afraid mode's event carries.")]
    public float FearShoutRange = Minds.FearShoutRange;

    [Rule("How far a feeding victim's cry carries: the Masquerade's real range.")]
    public float FedOnShoutRange = Minds.FedOnShoutRange;

    [Rule("How far the assault, hurt, killed and attacked-by events carry.")]
    public float AssaultShoutRange = Minds.AssaultShoutRange;

    [Rule("How far a frenzied character looks for a victim.")]
    public float FrenzyTargetReach = Minds.FrenzyReach;

    [Rule("The chance a blow breaks Mesmerize.", Min = 0, Max = 1)]
    public float MesmerizeShakenOffChance = Minds.ShakenOff;

    [Rule("Majesty's and possession's check intervals, in milliseconds.", Count = 2, Values = "Majesty, possession", Min = 1)]
    public int[] MajestyThinkMs = [Minds.MajestyThink, Minds.PossessingThink];

    [Rule("The most summoned pets (AI_MAXPETS).", Fixed = true, FixedWhy = "the pets' table has four places")]
    public int MaxPets = Minds.MaxPets;

    [Rule("A released pet's life, and a pet's think interval, in milliseconds.", Count = 2, Values = "linger, pet think", Min = 1)]
    public int[] SummonedLingerMs = [Minds.SummonedLinger, Minds.PetThink];

    [Rule("A rally (event 9): how far a member can be and still pile in, how often it takes someone else, how far it looks for them.", Count = 3,
        Values = "range, spread, nearby")]
    public float[] RallyRange = [Minds.RallyRange, Minds.RallySpread, Minds.RallyNearby];

    [Rule("Get out of my way: its range; the step aside's reach, floor and wait (ms).", Count = 4, Values = "range, reach, floor, wait ms")]
    public float[] MakeWayRange = [Minds.MakeWayRange, Minds.StepAsideReach, Minds.StepAsideFloor, Minds.StepAsideWait];

    [Rule("The combat sidestep: first think, think, no-ground wait (ms); turn floor and arc (degrees); step floor and spread; the coin.", Count = 8,
        Values = "first think ms, think ms, no ground ms, turn floor, turn arc, step floor, step spread, coin")]
    public float[] Sidestep = [Minds.SidestepFirstThink, Minds.SidestepThink, Minds.SidestepNoGround, Minds.SidestepFloorAngle,
                               Minds.SidestepArc, Minds.SidestepFloor, Minds.SidestepSpread, Minds.SidestepCoin];

    [Rule("The idle flourish: a roll every (ms), its chance, the rest after one (ms), the idle rethink (ms). Cosmetic, but on the random stream.",
        Count = 4, Values = "retry ms, chance, rest ms, think ms")]
    public float[] Idle = [Minds.IdleRetry, Minds.IdleChance, Minds.IdleRest, Minds.IdleThink];

    [Rule("A pedestrian's wander rethink: spread, hurried spread, floor (ms); the flyer's spread and floor (ms), eye height and reach share.", Count = 7,
        Values = "wander spread, hurried spread, wander floor, fly spread, fly floor, fly eye height, fly reach")]
    public float[] Wander = [Minds.WanderSpread, Minds.WanderHurriedSpread, Minds.WanderFloor, Minds.FlySpread, Minds.FlyFloor, Minds.FlyEyeHeight, Minds.FlyReach];

    [Rule("The search's rethink, and switched off (ms); the walk home beyond this; ef_wanted's time (ms) and value.", Count = 5,
        Values = "search ms, off ms, home reach, wanted ms, wanted value")]
    public float[] Search = [Minds.SearchThink, Minds.SearchOffThink, Minds.HomeReach, Minds.WantedFor, Minds.WantedValue];

    [Rule("Re-takes of a target within the window (ms) before it counts as a loop.", Count = 2, Values = "re-takes, window ms",
        Unsure = "probably ours (a guard), not 1.1's")]
    public int[] TargetFlapLimit = [Minds.FlapLimit, Minds.FlapWindow];
}

[RuleSection("Items", "Prices, containers and treasure. The store rates are the .nbs files', the effects' costs misc/effects.ned's.")]
public sealed class ItemRules
{
    [Rule("Price paid = value × store buy rate × (base + slope × trader Manipulation × percent), each step truncated, at least the floor. "
        + "Worked in single precision from these doubles, as 1.1 does.", Count = 3, Values = "base, slope, percent")]
    public double[] BuyPriceFactor = [Shops.BuyBase, Shops.BuySlope, Shops.Percent];

    [Rule("Price received = value × store sell rate × (base + slope × Manipulation × percent), at least the floor.", Count = 3, Values = "base, slope, percent")]
    public double[] SellPriceFactor = [Shops.SellBase, Shops.SellSlope, Shops.Percent];

    [Rule("An unidentified item sells for its price divided by this.", Min = 1)]
    public int UnidentifiedSellShare = 4;

    [Rule("Whether an unidentified owned item's effects add nothing to its value.")]
    public bool UnidentifiedEffectsWorthless = true;

    [Rule("Whether UNIQUE COST items skip the store's buy rate (the buying side only).")]
    public bool UniqueCostSkipsBuyRate = true;

    [Rule("No price is below this.", Min = 0)]
    public int PriceFloor = 1;

    [Rule("Whether an item's effects add to its price (an effect's permanent cost × value, or duration cost × value × time × amount).")]
    public bool EffectCostFormula = true;

    [Rule("An item a container drops disappears after this, in milliseconds.", Min = 0)]
    public int ContainerDropLifetimeMs = Location.ContainerDropLifetime;

    [Rule("The treasure classes' count (classes 1 to 255).", Fixed = true, FixedWhy = "the treasure file's table size")]
    public int TreasureClassCount = Treasure.Classes;

    [Rule("A treasure line's chance is its file's value times this.")]
    public double TreasureChancePercent = Treasure.ChancePercent;

    [Rule("The repack re-places icons only up to this many cells a side (bigger items fall out).", Min = 1, Max = 16)]
    public int RepackMaxCells = Stow.RepackCells;

    [Rule("A weapon's ammo capacity when its template sets none.", Min = 0)]
    public int AmmoDefaultCapacity = WeaponAttack.DefaultAmmo;

    [Rule("The cash a new network character starts with.", Min = 0)]
    public int NewNetCharacterCash = Net.NetCharacter.NewCash;
}

[RuleSection("Time", "The simulation's clock.")]
public sealed class TimeRules
{
    [Rule("Each frame is stepped by its wall time held to this range, in milliseconds.", Count = 2, Values = "shortest, longest",
        Fixed = true, FixedWhy = "it changes the whole simulation's step")]
    public int[] FrameStepClampMs = [FrameStep.ShortestMs, FrameStep.LongestMs];

    [Rule("A proximity trigger looks on the frames where its GUID plus the frame count is a multiple of this (a power of two: it is a mask).", PowerOfTwo = true, Max = 1024)]
    public int TriggerEveryFrames = Proximity.Every;

    [Rule("The frame rate most rule-bearing animations play at (feed, stake, frenzy, flinch).", Fixed = true,
        FixedWhy = "the busy times are the animations' lengths at this rate, in many places")]
    public float AnimationRate = 30f;

    [Rule("An effect's icon blinks for its last this many milliseconds, at this period.", Count = 2, Values = "blink ms, period ms", Min = 1)]
    public int[] EffectIconBlink = [EffectRuntime.BlinkMs, (int)EffectRuntime.BlinkPeriod];
}

[RuleSection("Party", "The party's and the network game's limits.")]
public sealed class PartyRules
{
    [Rule("The party size shown and handled.", Fixed = true, FixedWhy = "the interface has four heads")]
    public int MaxParty = Ui.PartyHeads.MaxParty;

    [Rule("Network players, the host among them.", Fixed = true, FixedWhy = "the slot table; --max-players chooses fewer")]
    public int MaxPlayers = Net.GameSettings.Slots;

    [Rule("Places kept loaded: single player, a host.", Count = 2, Values = "single player, host", Min = 1)]
    public int[] KeptPlaces = [Campaign.KeptPlaces, Campaign.KeptPlacesHosting];

    [Rule("Single-player save slots.", Fixed = true, FixedWhy = "the save screens' list")]
    public int SaveSlots = Save.OriginalSaves.Slots;

    [Rule("How far chat, emotes and rolls carry.")]
    public float SayRange = Net.ChatHearing.SayRange;

    [Rule("How near another player must be to overhear a conversation.")]
    public float ConversationEarshot = Net.HostScenes.EarshotRange;

    [Rule("The most dice one % roll throws.", Min = 1, Max = 1000)]
    public int ChatMaxDice = Net.NetChat.MaxDice;

    [Rule("The longest chat line.", Fixed = true, FixedWhy = "the wire's and the chat box's limit")]
    public int ChatLineMax = Net.NetChat.MaxLength;

    [Rule("The game flag that makes a network game's kills cost Humanity (Track Humanity, a session option).", Fixed = true,
        FixedWhy = "a session option's bit, chosen on the host's screen")]
    public int TrackHumanityFlag = (int)Net.GameFlags.TrackHumanity;

    [Rule("The most quests the join's list carries.", Fixed = true, FixedWhy = "a message's limit")]
    public int NetQuestListMax = Net.HostTexts.QuestListLimit;

    [Rule("The most stock lines a shop message sends.", Fixed = true, FixedWhy = "a message's limit")]
    public int NetShopLineMax = Net.HostShops.LineLimit;

    [Rule("How long a player's machine holds its character after sending use, take and put, in milliseconds.", Count = 3, Values = "use, take, put", Min = 0)]
    public int[] ForwardedOrderBusyMs = [Net.ForwardedOrders.UseBusyMs, Net.ForwardedOrders.TakeBusyMs, Net.ForwardedOrders.PutBusyMs];

    [Rule("A conversation line on a player's machine: the pad per line and the most, in milliseconds.", Count = 2, Values = "pad ms, most ms", Min = 0)]
    public int[] ClientSceneTiming = [Net.SubtitleClock.PadMs, Net.SubtitleClock.MaxMs];

    [Rule("1.1's UDP port.", Fixed = true, FixedWhy = "a setting already (--port)")]
    public int DefaultPort = Net.NetPeer.DefaultPort;

    [Rule("LAN presence every this many milliseconds, forgotten after this many.", Count = 2, Values = "presence ms, forget ms", Fixed = true,
        FixedWhy = "the finder's, not a game rule")]
    public int[] LanTimings = [Net.LanRoom.PresenceMs, Net.LanRoom.ForgetMs];

    [Rule("The host's snapshot and sheet intervals, in milliseconds.", Count = 2, Values = "snapshot ms, sheet ms", Ours = true, Fixed = true,
        FixedWhy = "ours, the network's own timing; listed so nobody takes them for 1.1's")]
    public int[] NetSnapshot = [Net.GameServer.SnapshotMs, Net.GameServer.SheetMs];
}
