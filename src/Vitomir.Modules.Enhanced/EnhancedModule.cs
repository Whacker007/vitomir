// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Config;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced;

/// <summary>
/// The enhanced module: the FPS/TPS action RPG's rules as the
/// rules package specifies them. This is its first two increments, the configuration and the actor sheet.
/// <para><b>The configuration.</b> <see cref="Configure"/> reads the trait registry and the profile asked for
/// from the module's data, by id through the platform's registry (<c>enhanced:vampire_trait_registry</c>,
/// <c>enhanced:profiles/prototype_sandbox</c>, …), checks both, works out each value's status and provenance from the gate
/// register, hashes the registry and profile together, and <b>fails closed</b>: the shipping profile refuses
/// with every reason named (not enabled, shipping not allowed, each unresolved or unadopted value with its
/// gate); the research sandbox refuses unless the <see cref="SandboxSwitch"/> switch is on, and when it runs
/// its stamp says <see cref="ModuleStamp.NonshippingResearch"/>.</para>
/// <para><b>The actor sheet.</b> <see cref="Register"/> adds the <see cref="ActorIdentity"/> component, the
/// <see cref="EnhancedConfig"/> service and the <see cref="ActorSheets"/> service (purchased and effective
/// traits, powers, resources, familiarity, conditions), which feeds the world's digest, and dresses the people
/// the program makes (<see cref="PeopleHooks"/>: the player a vampire of the Dark Ages, the townsfolk human —
/// the placeholder townsperson's sheet in the sandbox). No systems yet.</para>
/// <para><b>The bodies' movement.</b> <see cref="Configure"/> also reads the movement profiles of
/// <c>Data/movement/</c> (<c>enhanced:movement/human</c>, <c>…/neonate</c>, <c>…/elder</c>: SANDBOX numbers;
/// <see cref="MovementProfileData"/>), refusing a missing or wrong number by name;
/// <see cref="Register"/> gives them to the platform's <see cref="MovementProfiles"/> (the human's the
/// masquerade's) and each person made its own: the player the profile the program names (<c>--profile</c>), else
/// the human's; the townsfolk the human's. They are not in the configuration hash (it is the rules profile's and
/// the registry's, as before: the sandbox is still <c>config 43051f7e</c>); a recording names the module's files'
/// hash, which they are in.</para>
/// <para><b>Who sees.</b> <see cref="Configure"/> also reads <c>Data/perception/</c> (the world's numbers,
/// <c>enhanced:perception/sight</c>, with its act table, and the kinds of observer, <c>…/townsman</c>, <c>…/guard</c>,
/// <c>…/drunk</c> and the rest:
/// SANDBOX numbers; <see cref="PerceptionFiles"/>), refusing a missing or wrong number by name;
/// <see cref="Register"/> gives them to the platform's <see cref="PerceptionConfig"/>. Not in the configuration hash
/// either.</para>
/// <para><b>The staged harm.</b> <see cref="Configure"/> also reads <c>Data/rules/harm.json</c> (the staged harm's
/// own SANDBOX numbers: the weapon classes, who soaks what, the engine-sandbox Health; <see cref="HarmData"/>) and the
/// fighters' sheet templates of <c>Data/sheets/</c> (<see cref="SheetFiles"/>: Christof, the guard, the watchman; SANDBOX),
/// refusing a wrong one by name; neither is in the configuration hash. <see cref="Register"/> builds the stages
/// (<see cref="HarmRules"/>, refusing by name what the profile leaves null), registers the harm system
/// (<see cref="HarmServices"/>: every <see cref="ContactMade"/> on a body staged into wounds, <see cref="HarmResolved"/>,
/// <see cref="ActorDied"/>, a trace line), gives the people their fighters' sheets by the character they are drawn as and
/// the engine-sandbox Health, and offers the program the <c>--sheet</c> route (<see cref="PersonSheetsPort"/>, the platform's
/// <see cref="IPersonSheets"/>).</para>
/// <para><b>Contact.</b> <see cref="Configure"/> also reads <c>Data/combat/</c>'s contact files
/// (<c>enhanced:combat/weapons</c>: the active window's rule, the weapons' and shields' matter, the body's and the level's
/// surfaces; <c>enhanced:combat/protected</c>: the people weapons never harm; SANDBOX; <see cref="CombatFiles"/>);
/// <see cref="Register"/> gives them to the platform's <see cref="ContactConfig"/>. Not in the configuration hash.</para>
/// <para><b>Abilities in the fight.</b> <see cref="Configure"/> also reads <c>Data/combat/skill.json</c>
/// (<c>enhanced:combat/skill</c>, <see cref="SkillData"/>:, two channels each with its own SANDBOX numbers —
/// the body's tempo from Melee or Brawl, and the contest on contact), refusing a wrong one by name; not in the configuration
/// hash. <see cref="Register"/> registers the tempo (<see cref="SkillTempoServices"/>: the platform's
/// <see cref="CombatTempo"/> set on every fighter from its sheet) unless the <see cref="TempoOffSwitch"/> switch is on, and
/// builds the staged harm with the contest (<see cref="SkillContest"/>) only when the <see cref="ContestSwitch"/> switch is
/// on (a research comparator). With the tempo off and the contest not asked for, a world is the first module's, digest for
/// digest.</para>
/// <para>It is a module folder, <c>Modules/Enhanced/</c> (its <c>module.json</c> names it <c>enhanced</c>, the
/// namespace of its data's ids); the program knows it only through <see cref="IModule"/> and the loader.
/// <see cref="ModuleId"/> stays the rules' own name for it, which its stamp carries.</para>
/// </summary>
public sealed class EnhancedModule : IModule
{
    /// <summary>The rules' name for the module (also the registry's <c>module</c>, and the stamp's): not its folder's id, <c>enhanced</c>.</summary>
    public const string ModuleId = "enhanced-vampire";

    /// <summary>The switch that lets the research sandbox run (the program's <c>--sandbox</c>).</summary>
    public const string SandboxSwitch = "sandbox";

    /// <summary>
    /// The switches of the Abilities' channels (the command line's <c>--skill-tempo-off</c>, <c>--skill-contest</c>; the
    /// arena's spec's <c>switches</c>): the tempo runs unless turned off; the contest on contact runs <b>only when asked</b> —
    /// The rules package allows it "only as an explicitly selectable research comparator, not a committed default".
    /// </summary>
    public const string TempoOffSwitch = "skill-tempo-off", ContestSwitch = "skill-contest";

    /// <summary>
    /// The switches of Celerity (the command line's <c>--celerity-curve-oneone</c>, <c>--celerity-attack-off</c>; the
    /// arena's spec's <c>switches</c>): 1.1's curve in place of (1 + rank) (in the data to compare), and Celerity's
    /// movement alone, its attack's part off (the attack's model is provisional).
    /// </summary>
    public const string CelerityCurveSwitch = "celerity-curve-oneone", CelerityAttackOffSwitch = "celerity-attack-off";

    /// <summary>
    /// Celerity's default curve is <c>split</c> (speed plus flurry); this switch (<c>--celerity-curve-tempo</c>)
    /// keeps the earlier build's <c>one-plus-rank</c>, all tempo and no flurry, to compare. Not with <see cref="CelerityCurveSwitch"/>.
    /// </summary>
    public const string CelerityTempoSwitch = "celerity-curve-tempo";

    /// <summary>
    /// Severance off (<c>--severance-off</c>; the arena's spec's <c>switches</c>): nothing is cut off and every body contact
    /// stops the blade — the harm, outcome for outcome. Severance (<see cref="SeveranceData"/>, <c>rules/severance</c>)
    /// runs unless it is given.
    /// </summary>
    public const string SeveranceOffSwitch = "severance-off";

    /// <summary>Severance's numbers (ours, <see cref="SeveranceData"/>).</summary>
    public const string SeveranceAsset = SeveranceData.Asset;

    // Severance's angle and Melee's edge, each with its switch off.
    /// <summary>
    /// The blow's angle off for severance (<c>--severance-angle-off</c>; the arena spec's <c>switches</c>): every
    /// blow's whole post-soak harm counts toward cutting, the test (the band is still measured and reported). Melee's edge goes
    /// with it.
    /// </summary>
    public const string SeveranceAngleOffSwitch = "severance-angle-off";

    /// <summary>Melee's edge off (<c>--severance-melee-off</c>): the bands' own cut shares, whatever the attacker's Melee.</summary>
    public const string SeveranceMeleeOffSwitch = "severance-melee-off";
    // ----
    /// <summary>
    /// Armour off (<c>--armor-off</c>; the arena spec's <c>switches</c>): armour soaks nothing — the harm of
    /// before armour, outcome for outcome (what people wear is still kept on their sheets). Armour's numbers
    /// (<see cref="ArmorData"/>, <c>combat/armor</c>) soak by the struck part unless it is given.
    /// </summary>
    public const string ArmorOffSwitch = "armor-off";

    /// <summary>Armour's numbers (ours, <see cref="ArmorData"/>).</summary>
    public const string ArmorAsset = ArmorData.Asset;
    // ---- end

    /// <summary>
    /// The switch <c>--contact-stop-off</c> (and the arena spec's <c>switches</c>): blades no longer stop where
    /// they stop — the first contact rule, the blade through bodies, one contact per swing and person, a stop cut short at once (the
    /// data's <c>stop.enabled</c> false, <c>Data/combat/weapons.json</c>), to compare.
    /// </summary>
    public const string ContactStopOffSwitch = "contact-stop-off";

    /// <summary>
    /// The blocked blade's rebound as before (<c>--rebound-fixed</c>; the arena spec's <c>switches</c>): 0.15 m
    /// for every blow, no recoil through the arm, no attacker's stun, the guard rising when the busy time ends — to compare; and the
    /// film's sizes, the scaled rebound and recoil halved (<c>--rebound-small</c>) or nearly doubled (<c>--rebound-large</c>, ×1.8).
    /// </summary>
    public const string ReboundFixedSwitch = "rebound-fixed", ReboundSmallSwitch = "rebound-small", ReboundLargeSwitch = "rebound-large";

    /// <summary>The attacker's stun counted from the end of the blocked swing's busy time (<c>--rebound-stun-after</c>; <see cref="BladeStopRule.StunFromSwingEnd"/>), to compare.</summary>
    public const string ReboundStunAfterSwitch = "rebound-stun-after";

    /// <summary>The dodge's Dexterity link off (<c>--dodge-dexterity-off</c>): the burst a human's for every body, to measure the Dodge Ability's channel alone.</summary>
    public const string DodgeDexterityOffSwitch = "dodge-dexterity-off";

    /// <summary>
    /// The guard off for the run (<c>--guard-off</c>; the arena spec's <c>switches</c>): the earlier build's guard — attack
    /// refused while blocking, no parry window, no clash, no automatic guard direction, a guard met holding at once — to compare
    /// (the guard otherwise runs: <c>Data/combat/guard.json</c>).
    /// </summary>
    public const string GuardOffSwitch = "guard-off";

    /// <summary>
    /// The guard's sight off for the run (<c>--guard-sight-off</c>; the arena spec's <c>switches</c>): the
    /// automatic guard turns toward any threat in its arc, the earlier build's, to compare — otherwise only toward a threat its owner can see
    /// (the rules package's; <see cref="GuardSettings.SightRequired"/>).
    /// </summary>
    public const string GuardSightOffSwitch = "guard-sight-off";

    /// <summary>A development control: the player's guard sees by the body's facing as everyone else does (<c>--guard-sight-body</c>), not by the player's view (<see cref="GuardSettings.PlayerSightByView"/>).</summary>
    public const string GuardSightBodySwitch = "guard-sight-body";

    /// <summary>
    /// The guard's arm solver off for the run (<c>--guard-solver-off</c>; the arena spec's <c>switches</c>): a lone
    /// blade's raised guard keeps the earlier build's composed pose and nothing gives (<see cref="GuardSettings.Solver"/>), to compare.
    /// </summary>
    public const string GuardSolverOffSwitch = "guard-solver-off";

    /// <summary>
    /// A development control: the guard's technique term off (<c>--guard-technique-off</c>: a guard holds by Strength
    /// alone, a parry turns nothing), and Melee alone out of it (<c>--guard-melee-off</c>: technique φ(Dexterity) only) — the fourth
    /// lane of Melee isolated for the rules package (<see cref="GuardRules.TechniqueOn"/>, <see cref="GuardRules.MeleeOn"/>).
    /// </summary>
    public const string GuardTechniqueOffSwitch = "guard-technique-off", GuardMeleeOffSwitch = "guard-melee-off";

    /// <summary>
    /// ("No free passive evasion roll after a real hit.") The
    /// contest's defence counts φ(Dexterity) only alongside a performed defence — a guard raised or a dodge in its burst
    /// (<c>--contest-dexterity-performed</c>; <see cref="SkillContest.DexterityOnlyPerformed"/>). Off by default: the rules package
    /// decides.
    /// </summary>
    public const string ContestDexterityPerformedSwitch = "contest-dexterity-performed";

    // ---- blow quality (gate-08; the engine's call) ----
    /// <summary>
    /// Blow quality off for the run (<c>--blow-quality-off</c>; the arena spec's <c>switches</c>): every contact deals
    /// its whole damage potential whatever the work it could do, the hilt cuts as the blade, the cut share is the angle's — the earlier build's harm,
    /// outcome for outcome (the quality is still measured and recorded). <c>--blow-quality-hilt-off</c>: the hilt alone off (a
    /// contact at the hilt keeps its class's kind). Blow quality otherwise runs (<see cref="BlowQualityData"/>, <c>rules/quality</c>).
    /// </summary>
    public const string BlowQualityOffSwitch = "blow-quality-off", BlowQualityHiltOffSwitch = "blow-quality-hilt-off";

    /// <summary>The duelling brain's range keeping off for the run (<c>--brain-range-off</c>): the brain before it (<see cref="DuelRangeSettings"/>).</summary>
    public const string BrainRangeOffSwitch = "brain-range-off";
    /// <summary>The duelling brain closes straight at its foe whatever is between them, as before (<c>--brain-straight</c>; <see cref="DuelRoutes"/>).</summary>
    public const string BrainStraightSwitch = "brain-straight";
    bool brainRangeOff;
    bool brainStraight;

    // ---- ----
    /// <summary>
    /// The player on the earlier build's fixed ramp for the run, as every other body (<c>--accel-ramp</c>).
    /// By default the player starts and stops on the spring of its movement profile's <c>StartStopHalfLife</c> (the module gives the
    /// numbers, <see cref="Platform.Bodies.StartStopSprings"/>; the program the player's component); every other body keeps the ramp. A
    /// tape without the note <c>accel</c> is given this switch, so it replays on the ramp.
    /// </summary>
    public const string AccelRampSwitch = "accel-ramp";

    /// <summary>
    /// The war arrow as before, the heavy war arrow (<c>--arrows-heavy</c>: <c>ranged.json</c>'s
    /// <c>arrows_heavy</c>). By default the items set's: the long bow's war arrow of 54.5 g at about 58.2 m/s. A tape without the note
    /// <c>arrows</c> is given this switch, so a tape that loosed the earlier build's arrows replays.
    /// </summary>
    public const string ArrowsHeavySwitch = "arrows-heavy";
    bool accelRamp, arrowsHeavy;

    /// <summary>
    /// The player's landings on the spring as before (<c>--landing-slide</c>): the landing's speed across kept
    /// whole for the spring to take up (a landing on a slope slides on). By default a landing keeps only the speed along the way asked, up to
    /// the pace asked (<see cref="Platform.Bodies.StartStopSprings.LandingSlide"/>). A tape on the spring without the note <c>landing</c>
    /// is given this switch.
    /// </summary>
    public const string LandingSlideSwitch = "landing-slide";
    bool landingSlide;
    // ---- end

    // ---- directional attacks ----
    /// <summary>
    /// 1.1's tap-and-hold back for the run (<c>--attack-spin</c>; the arena spec's <c>switches</c>): quick blows in
    /// turn, the whirl when attack is held, a blow begun on release (an earlier build exactly), in place of directional attacks — the blow's direction
    /// chosen at the press, held ready, struck on release (<see cref="Platform.Control.DirectionalAttacks"/>, <c>Data/combat/directions.json</c>).
    /// </summary>
    public const string AttackSpinSwitch = "attack-spin";

    /// <summary>Directional attacks' table and numbers (<see cref="DirectionsData"/>).</summary>
    public const string DirectionsAsset = DirectionsData.Asset;
    DirectionsData? directions;
    bool attackSpin;
    // ---- end

    /// <summary>Blow quality's numbers (<see cref="BlowQualityData"/>).</summary>
    public const string QualityAsset = BlowQualityData.Asset;

    BlowQualityData? quality;
    bool qualityOn = true, qualityHiltOn = true;

    /// <summary>Blow quality's numbers <see cref="Configure"/> read (null when the module's data has none), and whether it applies.</summary>
    public BlowQualityData? Quality => quality;
    public bool QualityOn => qualityOn;

    /// <summary>The staged harm's quality stage, when the data has it.</summary>
    HarmRules WithQuality(HarmRules rules)
    {
        if (quality is not null) rules.Quality = new BlowQuality(quality, qualityOn, qualityHiltOn);
        return rules;
    }
    // ---- end

    /// <summary>The contest's defence Dexterity only alongside a performed defence, when the switch asks.</summary>
    HarmRules Performed(HarmRules rules)
    {
        if (rules.Contest is { } k) k.DexterityOnlyPerformed = contestDexterityPerformed;
        return rules;
    }

    /// <summary>The guard's numbers (<see cref="GuardData"/>).</summary>
    public const string GuardAsset = "combat/guard";

    /// <summary>
    /// What a Celerity swing's extra blows are (the rules package asked for three side by side;
    /// <c>--flurry-compressed</c>, <c>--flurry-off</c>; the arena spec's <c>switches</c>). By default each extra
    /// blow is its own strike at its own moment, landing only where the blade's line still meets the target then
    /// (<see cref="FlurryModel.Geometric"/>, <c>Data/rules/flurry.json</c>); <c>flurry-compressed</c> plays the earlier build's translation (every
    /// extra blow resolved at once on the swing's one contact); <c>flurry-off</c> pure tempo (no extra blows). Not both.
    /// </summary>
    public const string FlurryCompressedSwitch = "flurry-compressed", FlurryOffSwitch = "flurry-off";

    /// <summary>The geometric flurry's blows spread through the rest of the swing (<see cref="FlurrySpacing.Swing"/>) in place of the data's spacing, to compare (<c>--flurry-swing-spacing</c>).</summary>
    public const string FlurrySwingSpacingSwitch = "flurry-swing-spacing";

    /// <summary>
    /// The re-strike motion on (<c>--flurry-motion</c>: the blade seen re-striking along the checked line
    /// for each extra blow; <c>Data/rules/flurry.json</c>'s motion, off there), and the fixed spacing's seconds in place of the data's
    /// 0.04 s, to compare (<c>--flurry-spacing-80ms</c>, <c>-120ms</c>, <c>-160ms</c>: the milliseconds are the switch's name). At most one spacing.
    /// </summary>
    public const string FlurryMotionSwitch = "flurry-motion";
    public static readonly string[] FlurrySpacingSwitches = ["flurry-spacing-80ms", "flurry-spacing-120ms", "flurry-spacing-160ms"];

    /// <summary>The flurry's numbers (<see cref="FlurryData"/>; a module without the file plays the defaults).</summary>
    public const string FlurryAsset = FlurryData.Asset;

    /// <summary>Blood's numbers and the powers (ours, <see cref="BloodData"/>, <see cref="PowerData"/>).</summary>
    public const string BloodAsset = BloodData.Asset, PowersAsset = PowerData.Asset;

    /// <summary>
    /// The warning a run with both channels carries ("any margin/quality bonus may not … count the same skill improvement
    /// both in animation and after impact"; both are chosen, so it is allowed and said plainly).
    /// </summary>
    public const string DoubleCountWarning = "Melee counted in the animation and after impact: a research comparator, not a rule";

    /// <summary>The Abilities' channels' numbers (ours, <see cref="SkillData"/>).</summary>
    public const string SkillAsset = "combat/skill";

    /// <summary>The shipping profile (the default): fail closed until its numbers are approved.</summary>
    public const string ShippingProfileId = "enhanced-vampire-SHIPPING-UNAPPROVED";

    /// <summary>The research sandbox profile.</summary>
    public const string SandboxProfileId = "SANDBOX_EXPLICIT_OPT_IN";

    /// <summary>The paths of its data's ids in its own namespace (<c>&lt;module&gt;:vampire_trait_registry</c>; the files are Data/vampire_trait_registry.json and so on).</summary>
    public const string RegistryAsset = "vampire_trait_registry";
    public const string GateRegisterAsset = "numeric_gate_register";
    public const string GateMappingAsset = "profile_value_gates";
    public const string PowerContractsAsset = "power_verb_contracts";

    /// <summary>The folder of its data whose files are profiles (found by their schema).</summary>
    public const string ProfilesFolder = "profiles";

    /// <summary>The folder of its movement profiles (ours, <see cref="MovementProfileData"/>), and the human's, which the masquerade holds bodies to.</summary>
    public const string MovementFolder = "movement";
    public const string HumanMovement = "movement/human";

    /// <summary>The folder of its perception (ours, <see cref="PerceptionFiles"/>): the world's numbers and the kinds of observer.</summary>
    public const string PerceptionFolder = "perception";

    /// <summary>The staged harm's own numbers (ours, <see cref="HarmData"/>), and the folder of the fighters' sheet templates (<see cref="SheetFiles"/>).</summary>
    public const string HarmAsset = HarmData.Asset, SheetsFolder = SheetFiles.Folder;
    /// <summary>The folder of its contact data (ours, <see cref="CombatFiles"/>): the weapons' and shields' matter, the active window's rule, the protected people.</summary>
    public const string CombatFolder = "combat";

    /// <summary>The rules package the data files come from, and each file's path and SHA-256 in it (Data/SOURCES.md says the same).</summary>
    public const string SourcePackage = "the rules package of 2026-10-18";

    /// <summary>Each of the package's files: its id's path, its file under Data/, its path in the package and its SHA-256.</summary>
    public static readonly IReadOnlyList<(string Asset, string File, string PackagePath, string Sha256)> PackageFiles =
    [
        (RegistryAsset, "vampire_trait_registry.json", "engine_h1/contracts/vampire_trait_registry.json", "c36415e47c23281f8e017665c160d5b014114cc9749e443b2d27311d2e3ab791"),
        ("profiles/enhanced_unapproved_template", "profiles/enhanced_unapproved_template.json", "engine_h1/configs/enhanced_unapproved_template.json", "9a6e61d0f4bd941a528cc16afb9aafd2ed0b5a358934d00ab07a10a02126a42f"),
        ("profiles/prototype_sandbox", "profiles/prototype_sandbox.json", "engine_h1/configs/prototype_sandbox.json", "02562ffa5a9b1dbb030e0c81b054249e72c50f0b1c24a0c899f843241a4de199"),
        (GateRegisterAsset, "NUMERIC_GATE_REGISTER.json", "engine_h1/governance/NUMERIC_GATE_REGISTER.json", "86370eb3d2bda4d91dd991f29c8977cbc496f77546e1a42a944bcf39507181a0"),
        (PowerContractsAsset, "power_verb_contracts.json", "engine_h1/contracts/power_verb_contracts.json", "7ec7a988cff2e10c257ccc9f38044f907842f969f327df5a9e6d22847886ae9b"),
    ];

    EnhancedConfig? config;
    List<MovementProfile> movement = [];
    Perceiving? perceiving;
    HarmData? harm;
    List<SheetFile> sheetFiles = [];
    SkillData? skill;
    bool tempoOn, contestOn;
    bool contactStopOff;
    bool reboundFixed;
    float reboundSize = 1f;
    bool reboundStunAfter;
    BloodData? blood;
    PowerData? powerData;
    bool celerityOneOne, celerityAttackOn, celerityTempo;
    SeveranceData? severance;
    bool severanceOn;
    bool severanceAngleOn, severanceEdgeOn;
    ArmorData? armorData;
    bool armorOn;

    /// <summary>Armour's numbers <see cref="Configure"/> read (null when the module's data has none), and whether armour soaks (<see cref="ArmorOffSwitch"/> not given).</summary>
    public ArmorData? Armor => armorData;
    public bool ArmorOn => armorOn;

    /// <summary>Severance's numbers <see cref="Configure"/> read, and whether it runs (<see cref="SeveranceOffSwitch"/> not given).</summary>
    public SeveranceData Severance => severance ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public bool SeveranceOn => severanceOn;

    bool dodgeDexterityOn = true;

    /// <summary>Blood's numbers and the powers <see cref="Configure"/> read, and Celerity's switches.</summary>
    public BloodData Blood => blood ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public PowerData Powers => powerData ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public bool CelerityOneOne => celerityOneOne;
    public bool CelerityAttackOn => celerityAttackOn;
    /// <summary>The curve Celerity runs on (<c>split</c> by default; <c>one-plus-rank</c> with <see cref="CelerityTempoSwitch"/>, <c>oneone</c> with <see cref="CelerityCurveSwitch"/>).</summary>
    public string CelerityCurveName(PowerDef celerity) => celerityOneOne ? PowerData.OneOne : celerityTempo ? PowerData.OnePlusRank : celerity.DefaultCurve;

    /// <summary>The Abilities' channels' numbers <see cref="Configure"/> read, and whether each channel runs (its off switch not given).</summary>
    public SkillData Skill => skill ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public bool TempoOn => tempoOn;
    /// <summary>The pace's numbers <see cref="Configure"/> read (<c>Data/combat/pace.json</c>), and its switches.</summary>
    public PaceData Pace => paceData ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public bool CombatTempoOff => combatTempoOff;
    public bool FightingPaceOn => !fightingPaceOff;

    /// <summary>Whether the dodge's Dexterity link is on (off with <see cref="DodgeDexterityOffSwitch"/>).</summary>
    public bool DodgeDexterityOn => dodgeDexterityOn;

    /// <summary>The guard's numbers <see cref="Configure"/> read, and whether the guard runs (<see cref="GuardOffSwitch"/> not given).</summary>
    public GuardData Guard => guard ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    public bool GuardOn => guardOn;
    public bool ContestOn => contestOn;

    /// <summary>The flurry's numbers and the model the switches chose.</summary>
    public FlurryData Flurry => flurry;
    public FlurryModel FlurryModel => flurryModel;

    /// <summary>The staged harm's own numbers <see cref="Configure"/> read.</summary>
    public HarmData Harm => harm ?? throw new InvalidOperationException($"module {ModuleId} is not configured");

    /// <summary>The fighters' sheet templates <see cref="Configure"/> read, in file order.</summary>
    public IReadOnlyList<SheetFile> Fighters => sheetFiles;
    (CombatFiles.Matter Matter, List<string> Protected)? contact;

    /// <summary>The module's perception as <see cref="Configure"/> read it: the world's numbers, the default kind and the kinds.</summary>
    sealed record Perceiving(PerceptionNumbers Numbers, ActResponse[] Acts, bool Sandbox, string? DefaultKind, IReadOnlyList<ObserverKind> Kinds);

    public ModuleInfo Info { get; } = new(ModuleId, "0.1.0", "VTMR-H1");

    /// <summary>The settled configuration; throws before a successful <see cref="Configure"/>.</summary>
    public EnhancedConfig Config => config ?? throw new InvalidOperationException($"module {ModuleId} is not configured");

    public ModuleStamp Stamp => Config.Stamp;

    /// <summary>The movement profiles <see cref="Configure"/> read (<c>Data/movement/</c>), the human's first.</summary>
    public IReadOnlyList<MovementProfile> Movement => movement;

    /// <summary>
    /// The sandbox's warning while it runs (<see cref="EnhancedConfig.Banner"/>); null for a shipping profile. with both
    /// of the Abilities' channels on, the double count's warning after it (<see cref="DoubleCountWarning"/>).
    /// </summary>
    public string? Banner => config is { Banner.Length: > 0 } c
        ? tempoOn && contestOn && c.IsSandbox ? $"{c.Banner}   {DoubleCountWarning}" : c.Banner
        : null;

    /// <summary>
    /// Reads and checks the module's data and settles the profile: <c>context.Profile</c> by its id or by
    /// <c>shipping</c> / <c>sandbox</c>; when none is named, the sandbox if the <see cref="SandboxSwitch"/>
    /// switch is on, else the shipping profile. Throws <see cref="ModuleRefusedException"/> with every reason.
    /// </summary>
    public void Configure(ModuleConfigContext context)
    {
        config = null;
        // The hint a refusal carries — the sandbox switch when it is not given (the module's other switches only
        // turn its research channels off or on, and mend no refusal).
        hint = context.Switches.Contains(SandboxSwitch) ? "" : SandboxHint;
        var problems = new List<string>();
        var assets = context.Assets;
        AssetId registryId = assets.Own(RegistryAsset), gatesId = assets.Own(GateRegisterAsset),
                mappingId = assets.Own(GateMappingAsset), powersId = assets.Own(PowerContractsAsset);

        var registryRoot = Load(assets, registryId, problems);
        var gatesRoot = Load(assets, gatesId, problems);
        var mappingRoot = Load(assets, mappingId, problems);
        var powersRoot = Load(assets, powersId, problems);

        // Profiles are found by their schema, so the folder may hold more than the two of the package.
        var profiles = new List<(string File, string Id, JsonElement Root)>();
        var ids = new Dictionary<string, AssetId>(StringComparer.Ordinal);
        foreach (var asset in assets.List(ProfilesFolder))
        {
            string name = asset.ToString();
            var root = Load(assets, asset, problems);
            if (root is not { } r || !NumericProfile.IsProfile(r)) continue;
            var id = NumericProfile.IdOf(r);
            if (id is null) { problems.Add($"{name}: 'profile_id' is missing"); continue; }
            if (profiles.Any(p => p.Id == id)) problems.Add($"{name}: profile '{id}' is also in {profiles.First(p => p.Id == id).File}");
            else { profiles.Add((name, id, r)); ids[name] = asset; }
        }

        string asked = context.Profile switch
        {
            null => context.Switches.Contains(SandboxSwitch) ? SandboxProfileId : ShippingProfileId,
            "shipping" => ShippingProfileId,
            "sandbox" => SandboxProfileId,
            var p => p,
        };
        var chosen = profiles.FirstOrDefault(p => p.Id == asked);
        if (chosen.Id is null)
            problems.Add($"no profile '{asked}' in {assets.Module}:{ProfilesFolder}/ (profiles there: {(profiles.Count == 0 ? "none" : string.Join(", ", profiles.Select(p => p.Id)))})");

        // The bodies' movement profiles: every file of Data/movement/, the human's among them.
        var moving = ReadMovement(assets, problems);
        // Who sees: the world's numbers and the kinds of observer of Data/perception/.
        var seeing = ReadPerception(assets, problems);
        // Contact: the weapons' and shields' matter, the active window's rule and the protected people of Data/combat/.
        var touching = ReadCombat(assets, problems);

        if (problems.Count > 0) Refuse(problems);

        var registry = TraitRegistry.Read(registryRoot!.Value, registryId.ToString(), ModuleId, problems);
        var gates = GateRegister.Read(gatesRoot!.Value, gatesId.ToString(), mappingRoot!.Value, mappingId.ToString(), problems);
        var powers = PowerContracts.Read(powersRoot!.Value, powersId.ToString(), problems);
        var profile = NumericProfile.Read(chosen.Root, chosen.File, problems);
        if (problems.Count > 0) Refuse(problems);

        // Blood's numbers and the powers; the purchased capacity (blood_pool) joins the layout before any sheet is read.
        var bloodId = assets.Own(BloodAsset);
        var readBlood = Load(assets, bloodId, problems) is { } br ? BloodData.Read(br, bloodId.ToString(), problems) : null;
        var powersAssetId = assets.Own(PowersAsset);
        var readPowers = Load(assets, powersAssetId, problems) is { } pr ? PowerData.Read(pr, powersAssetId.ToString(), problems) : null;
        if (readBlood is not null)
        {
            try { registry!.Layout.AddPurchase(new PurchaseDef(readBlood.PurchaseId, new TraitDomain(readBlood.PurchaseMin, readBlood.PurchaseMax, false), readBlood.PurchaseSource)); }
            catch (ArgumentException e) { problems.Add($"{bloodId}: {e.Message}"); }
        }
        if (problems.Count > 0) Refuse(problems);

        // The staged harm's own numbers and the fighters' sheets (the sheets need the registry's layout).
        var harmId = assets.Own(HarmAsset);
        var harmRoot = Load(assets, harmId, problems);
        var readHarm = harmRoot is { } hr ? HarmData.Read(hr, harmId.ToString(), problems) : null;
        // Severance's numbers (its edged classes must be harm's).
        var severanceId = assets.Own(SeveranceAsset);
        var readSeverance = Load(assets, severanceId, problems) is { } svr ? SeveranceData.Read(svr, severanceId.ToString(), problems, readHarm) : null;
        // Armour's numbers (its aggravated classes must be harm's); a module without the file has no armour.
        var armorId = assets.Own(ArmorAsset);
        var readArmor = assets.Exists(armorId) && Load(assets, armorId, problems) is { } arr ? ArmorData.Read(arr, armorId.ToString(), problems, readHarm) : null;
        // Blow quality's numbers (its hilt's and references' classes must be harm's); a module without the file has none.
        var qualityId = assets.Own(QualityAsset);
        var readQuality = assets.Exists(qualityId) && Load(assets, qualityId, problems) is { } qlr ? BlowQualityData.Read(qlr, qualityId.ToString(), problems, readHarm) : null;
        // Health's purchased capacity (health_pool, 1 to 1000) joins the layout before any sheet is read.
        if (readHarm is not null)
        {
            try { registry!.Layout.AddPurchase(new PurchaseDef(readHarm.HealthPurchaseId, new TraitDomain(readHarm.HealthPurchaseMin, readHarm.HealthPurchaseMax, false), readHarm.HealthPurchaseSource)); }
            catch (ArgumentException e) { problems.Add($"{harmId}: {e.Message}"); }
        }
        if (context.Switches.Contains(CelerityCurveSwitch) && context.Switches.Contains(CelerityTempoSwitch))
            problems.Add($"the switches {CelerityCurveSwitch} and {CelerityTempoSwitch} each choose Celerity's curve: give one");
        // Directional attacks' table and numbers (a module without the file plays the platform's defaults).
        var directionsId = assets.Own(DirectionsAsset);
        var readDirections = assets.Exists(directionsId) && Load(assets, directionsId, problems) is { } drs ? DirectionsData.Read(drs, directionsId.ToString(), problems) : null;
        // The flurry's numbers, and one model.
        var flurryId = assets.Own(FlurryAsset);
        var readFlurry = assets.Exists(flurryId) && Load(assets, flurryId, problems) is { } flr ? FlurryData.Read(flr, flurryId.ToString(), problems) : null;
        if (context.Switches.Contains(FlurryCompressedSwitch) && context.Switches.Contains(FlurryOffSwitch))
            problems.Add($"the switches {FlurryCompressedSwitch} and {FlurryOffSwitch} each choose what Celerity's extra blows are: give one");
        if (FlurrySpacingSwitches.Count(context.Switches.Contains) + (context.Switches.Contains(FlurrySwingSpacingSwitch) ? 1 : 0) > 1)
            problems.Add($"the switches {string.Join(", ", FlurrySpacingSwitches)} and {FlurrySwingSpacingSwitch} each choose the flurry's spacing: give one");
        var readSheets = new List<SheetFile>();
        foreach (var asset in assets.List(SheetsFolder))
            if (Load(assets, asset, problems) is { } sr && SheetFiles.Read(asset, sr, registry!.Layout, problems) is { } sf)
            {
                if (readSheets.FirstOrDefault(x => x.Template.Id == sf.Template.Id) is { } same)
                    problems.Add($"{asset}: template '{sf.Template.Id}' is also {same.Asset}");
                else readSheets.Add(sf);
            }
        // A role's default dresses whoever of that role no template names — at most one a role.
        foreach (var g in readSheets.Where(f => f.Role.Length > 0 && f.Characters.Count == 0).GroupBy(f => f.Role))
            if (g.Count() > 1) problems.Add($"{assets.Module}:{SheetsFolder}/: {string.Join(" and ", g.Select(f => f.Asset))} are both the {g.Key}'s default (a role and no characters)");
        // What harm reads of Potence and Fortitude, and the sheets' powers, must be powers and options the data knows.
        if (readPowers is not null && readHarm is not null)
            foreach (var (power, option) in new[] { (readHarm.PotencePower, readHarm.PotenceOption), (readHarm.FortitudePower, readHarm.FortitudeOption) })
                if (readPowers.Find(power) is not { } pd || pd.Kind != PowerKind.Passive || !pd.Options.Contains(option))
                    problems.Add($"{powersAssetId}: {readHarm.File} reads {power} by its option {option}, which is no passive power of it");
        if (readPowers is not null)
            foreach (var f in readSheets)
                foreach (var lp in f.Template.Sheet.Powers)
                    foreach (var o in lp.Options)
                        if (!readPowers.KnowsOption(o)) problems.Add($"{f.Asset}: power {lp.PowerId}'s option {o} is none of {powersAssetId}'s");
        // What a temporary Attribute raises must be a trait of the registry.
        if (readPowers is not null)
            foreach (var pd in readPowers.Powers)
                if (pd.Effect == PowerData.EffectTempAttribute && !registry!.Layout.Traits.Any(t => t.Id == pd.Trait))
                    problems.Add($"{powersAssetId}: power {pd.Id} raises '{pd.Trait}', which is no trait of the registry");
        if (problems.Count > 0) Refuse(problems);

        // The guard's numbers (the traits they read must be the registry's).
        var guardId = assets.Own(GuardAsset);
        var readGuard = Load(assets, guardId, problems) is { } gr ? GuardData.Read(gr, guardId.ToString(), problems) : null;
        if (readGuard is not null)
            foreach (var (what, id) in new[] { ("strength", readGuard.Strength), ("dexterity", readGuard.Dexterity), ("melee", readGuard.Melee) })
                if (!registry!.Layout.Traits.Any(t => t.Id == id)) problems.Add($"{guardId}: traits.{what} '{id}' is no trait of the registry");

        // The Abilities' channels (the traits they read must be the registry's, the bare-handed classes harm's).
        var skillId = assets.Own(SkillAsset);
        var readSkill = Load(assets, skillId, problems) is { } kr ? SkillData.Read(kr, skillId.ToString(), problems) : null;
        if (readSkill is not null)
        {
            foreach (var (what, id) in new[] { ("armed", readSkill.ArmedAbility), ("bare_handed", readSkill.BareAbility), ("dodge", readSkill.DodgeAbility), ("dexterity", readSkill.DexterityTrait) })
                if (!registry!.Layout.Traits.Any(t => t.Id == id)) problems.Add($"{skillId}: abilities.{what} '{id}' is no trait of the registry");
            foreach (var cls in readSkill.BareClasses)
                if (readHarm is not null && !readHarm.Classes.ContainsKey(cls)) problems.Add($"{skillId}: abilities.bare_handed_classes names class '{cls}', which {readHarm.File} does not have");
            if (readHarm is not null && readHarm.BareHands.Length > 0 && !readSkill.IsBareHanded(readHarm.BareHands))
                problems.Add($"{skillId}: abilities.bare_handed_classes must name {readHarm.File}'s bare_hands class '{readHarm.BareHands}'");
            if (readHarm is not null)
                foreach (var st in new[] { readSkill.PoolStream, readSkill.AttackStream, readSkill.DefenceStream })
                    if (st == readHarm.PoolStream || st == readHarm.DamageStream || st == readHarm.SoakStream)
                        problems.Add($"{skillId}: contest stream '{st}' is also the staged harm's ({readHarm.File}): the contest draws from streams of its own");
        }
        // The pace of a fight between humans (the global combat tempo, the fighting pace, the dodge's pace).
        var paceId = assets.Own(PaceAsset);
        var readPace = Load(assets, paceId, problems) is { } pcr ? PaceData.Read(pcr, paceId.ToString(), problems) : null;
        // ---- the bows, the aim's spread by Archery, an arrow's harm (ranged.json, SANDBOX) ----
        var rangedId = assets.Own(RangedAsset);
        var readRanged = Load(assets, rangedId, problems) is { } rgr ? RangedData.Read(rgr, rangedId.ToString(), problems, context.Switches.Contains(ArrowsHeavySwitch)) : null;   // arrows-heavy
        if (readRanged is not null && readHarm is not null && readHarm.ClassOf(readRanged.Bows[0].Id) is not { } bowClass)
            problems.Add($"{rangedId}: the bow '{readRanged.Bows[0].Id}' has no class in {readHarm.File}'s weapons (an arrow's harm needs '{readRanged.Harm.Class}')");
        // ---- end
        if (problems.Count > 0) Refuse(problems);

        // The profile and the registry must agree on the permanent cap (1000).
        int registryCap = registry!.Layout.Traits.Where(t => t.Kind == TraitKind.Attribute).Select(t => t.Domain.MaxPurchased).DefaultIfEmpty(0).Max();
        if (profile!.AttributePurchasedCap != registryCap)
            problems.Add($"{profile.File}: attribute_purchased_cap {profile.AttributePurchasedCap} disagrees with the registry's max_purchased {registryCap}");

        // Each value's status and provenance, from the gate register through the mapping.
        var values = new List<ConfigValue>();
        foreach (var (key, json) in profile.Values)
        {
            var gate = gates!.GateOf(key);
            if (gate is null) { problems.Add($"{profile.File}: value '{key}' answers to no gate ({mappingId} names none)"); continue; }
            var status = json is null ? ValueStatus.Unresolved : gate.GameNumericAdopted ? ValueStatus.ShippingApproved : ValueStatus.ResearchOnly;
            values.Add(new ConfigValue(key, json, status, gate.Id, gate.ResearchStatus, gate.GameNumericAdopted, profile.ProfileId, profile.Status));
        }
        if (problems.Count > 0) Refuse(problems);

        // Fail closed.
        if (profile.IsSandbox)
        {
            if (profile.ShippingAllowed)
                problems.Add($"profile {profile.ProfileId} is a research sandbox but says shipping_allowed=true; a sandbox never ships");
            if (!context.Switches.Contains(SandboxSwitch))
                problems.Add($"profile {profile.ProfileId} is a non-shipping research sandbox and runs only with the '{SandboxSwitch}' switch (--{SandboxSwitch})");
        }
        else
        {
            if (profile.Enabled != true) problems.Add($"profile {profile.ProfileId} is not enabled (enabled={Word(profile.Enabled)})");
            if (!profile.ShippingAllowed) problems.Add($"profile {profile.ProfileId} does not allow shipping (shipping_allowed=false)");
            foreach (var v in values)
            {
                if (v.Status == ValueStatus.Unresolved)
                    problems.Add($"value '{v.Key}' is unresolved (gate {v.GateId}, {v.GateResearchStatus})");
                else if (v.Status == ValueStatus.ResearchOnly)
                    problems.Add($"value '{v.Key}' = {v.Json} is not shipping-approved (gate {v.GateId}, {v.GateResearchStatus}, not adopted)");
            }
        }
        if (problems.Count > 0) Refuse(problems);

        string hash;
        try { hash = ConfigurationHash(registryRoot.Value, profile.Root); }
        catch (JsonException e) { Refuse([$"the registry or profile {profile.File} has no canonical form ({e.Message})"]); return; }
        // A load whose data a scenario overrode reports its own hash, the applied overrides folded in (none: unchanged).
        if (assets.DataOverrides is { Count: > 0 } changed)
            hash = ContentHash.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(hash + "\n" + string.Join("\n", changed.Select(c => $"{c.Asset}{c.Pointer} {c.Op} {c.After} {c.Sha256After}"))));
        var stamp = new ModuleStamp(ModuleId, profile.ProfileId, Info.FormulaVersion, profile.IsSandbox, hash);
        var files = new List<DataFileIdentity>();
        foreach (var id in new[] { registryId, ids[chosen.File], gatesId, mappingId, powersId })
        {
            string sha = ContentHash.Sha256Hex(assets.Read(id));
            var known = PackageFiles.FirstOrDefault(f => f.Asset == id.Path);
            files.Add(new DataFileIdentity(id.ToString(), sha, known.PackagePath, known.Sha256 == sha));
        }
        config = new EnhancedConfig(stamp, registry, profile, gates!, powers!, values, files);
        movement = moving;
        perceiving = seeing;
        harm = readHarm;
        sheetFiles = readSheets;
        contact = touching;
        skill = readSkill;
        tempoOn = !context.Switches.Contains(TempoOffSwitch);
        contactStopOff = context.Switches.Contains(ContactStopOffSwitch);
        reboundFixed = context.Switches.Contains(ReboundFixedSwitch);
        reboundSize = context.Switches.Contains(ReboundSmallSwitch) ? 0.5f : context.Switches.Contains(ReboundLargeSwitch) ? 1.8f : 1f;
        reboundStunAfter = context.Switches.Contains(ReboundStunAfterSwitch);
        contestOn = context.Switches.Contains(ContestSwitch);
        blood = readBlood;
        powerData = readPowers;
        celerityOneOne = context.Switches.Contains(CelerityCurveSwitch);
        celerityTempo = context.Switches.Contains(CelerityTempoSwitch);
        celerityAttackOn = !context.Switches.Contains(CelerityAttackOffSwitch);
        severance = readSeverance;
        severanceOn = !context.Switches.Contains(SeveranceOffSwitch);
        severanceAngleOn = !context.Switches.Contains(SeveranceAngleOffSwitch);
        severanceEdgeOn = !context.Switches.Contains(SeveranceMeleeOffSwitch);
        armorData = readArmor;
        armorOn = !context.Switches.Contains(ArmorOffSwitch);

        dodgeDexterityOn = !context.Switches.Contains(DodgeDexterityOffSwitch);
        guard = readGuard;
        guardOn = !context.Switches.Contains(GuardOffSwitch);
        guardSightOn = !context.Switches.Contains(GuardSightOffSwitch);   // the guard's sight
        guardSightBody = context.Switches.Contains(GuardSightBodySwitch);
        guardSolverOn = !context.Switches.Contains(GuardSolverOffSwitch);
        flurry = readFlurry ?? FlurryData.Defaults;
        if (context.Switches.Contains(FlurrySwingSpacingSwitch)) flurry = flurry.WithSpacing(FlurrySpacing.Swing);
        foreach (var sw in FlurrySpacingSwitches)   // the milliseconds are the switch's name
            if (context.Switches.Contains(sw))
                flurry = flurry.With(FlurrySpacing.Fixed, int.Parse(sw["flurry-spacing-".Length..^"ms".Length], System.Globalization.CultureInfo.InvariantCulture) / 1000f, flurry.Motion);
        if (context.Switches.Contains(FlurryMotionSwitch)) flurry = flurry.With(flurry.Spacing, flurry.SpacingSeconds, true);
        flurryModel = context.Switches.Contains(FlurryCompressedSwitch) ? FlurryModel.Compressed
            : context.Switches.Contains(FlurryOffSwitch) ? FlurryModel.Off : FlurryModel.Geometric;
        guardTechniqueOn = !context.Switches.Contains(GuardTechniqueOffSwitch);
        guardMeleeOn = !context.Switches.Contains(GuardMeleeOffSwitch);
        contestDexterityPerformed = context.Switches.Contains(ContestDexterityPerformedSwitch);
        quality = readQuality;
        qualityOn = !context.Switches.Contains(BlowQualityOffSwitch);
        qualityHiltOn = !context.Switches.Contains(BlowQualityHiltOffSwitch);
        brainRangeOff = context.Switches.Contains(BrainRangeOffSwitch);
        brainStraight = context.Switches.Contains(BrainStraightSwitch);
        directions = readDirections;
        attackSpin = context.Switches.Contains(AttackSpinSwitch);
        guardSkillOn = !context.Switches.Contains(GuardSkillOffSwitch);
        brainParryFixed = context.Switches.Contains(BrainParryFixedSwitch);
        staggerOneOne = context.Switches.Contains(StaggerOneOneSwitch);
        staggerPushOnly = context.Switches.Contains(StaggerPushOnlySwitch);
        staggerHurtShort = context.Switches.Contains(StaggerHurtShortSwitch) || context.Switches.Contains(StaggerHurtClipSwitch);   // The earlier build's name kept as an alias
        reboundTwoThirds = context.Switches.Contains(ReboundTwoThirdsSwitch);
        reboundStunAtMeeting = context.Switches.Contains(ReboundStunAtMeetingSwitch);
        stagger = readGuard?.Stagger;
        paceOff = context.Switches.Contains(PaceOffSwitch);
        bladeGuardEveryone = context.Switches.Contains(BladeGuardEveryoneSwitch);
        paceData = readPace;
        rangedData = readRanged;
        combatTempoOff = context.Switches.Contains(CombatTempoOffSwitch);
        fightingPaceOff = context.Switches.Contains(FightingPaceOffSwitch);
        dodgePaced = context.Switches.Contains(DodgePacedSwitch);
        rushUnpaced = context.Switches.Contains(RushUnpacedSwitch);
        shieldReadOff = context.Switches.Contains(ShieldReadOffSwitch);
        windowFromWindUp = context.Switches.Contains(WindowFromWindUpSwitch);
        accelRamp = context.Switches.Contains(AccelRampSwitch);
        arrowsHeavy = context.Switches.Contains(ArrowsHeavySwitch);
        landingSlide = context.Switches.Contains(LandingSlideSwitch);
    }

    // ---- the shield read (skill should tell against a shielded guard) ----
    /// <summary>
    /// No shield read (<c>--shield-read-off</c>): a duelling brain aims its blows at a shield-bearer as, whatever
    /// its Melee. Otherwise guard.json's <c>shield_read</c>: by a chance by Melee (Brawl bare-handed) a brain aims a blow over or under its
    /// foe's shield as it is held (<see cref="GuardSkill.ShieldRead"/>), with the guard by skill (not with <c>guard-skill-off</c>).
    /// </summary>
    public const string ShieldReadOffSwitch = "shield-read-off";

    bool shieldReadOff;
    // ---- end

    // ---- the strike's path without the move into it ("that's just the wind up") ----
    /// <summary>
    /// The earlier build's active windows exactly (<c>--window-from-windup</c>): a wind-up's blow is swept for contact, and drawn by
    /// the swing's arc, from where its blade leaves the ready pose; otherwise from where its strike proper begins (<c>weapons.json</c>'s
    /// <c>activeWindow.strikeStarts</c>, <see cref="Vitomir.Platform.Combat.StrikeStart"/>).
    /// </summary>
    public const string WindowFromWindUpSwitch = "window-from-windup";

    bool windowFromWindUp;
    // ---- end

    // ---- the pace of a fight between humans ("humans moving at human speeds"; pace.json) ----
    /// <summary>The pace's numbers (<c>Data/combat/pace.json</c>, <see cref="PaceData"/>).</summary>
    public const string PaceAsset = "combat/pace";

    /// <summary>
    /// The earlier build's pace exactly (<c>--combat-tempo-off</c>): no global combat tempo (1, every blow at 1.1's timing under Melee's
    /// and Celerity's rates), whatever pace.json or <c>--combat-tempo</c> says. Otherwise pace.json's <c>tempo.global</c> (0.70), or
    /// the program's <see cref="CombatTempoOverride"/>.
    /// </summary>
    public const string CombatTempoOffSwitch = "combat-tempo-off";

    /// <summary>No fighting pace (<c>--fighting-pace-off</c>): a body runs and sprints through its own blow, as earlier.</summary>
    public const string FightingPaceOffSwitch = "fighting-pace-off";

    /// <summary>The dodge at pace.json's pace (<c>--dodge-paced</c>; pace.json's <c>dodge.use</c> makes it the default): burst 0.35 s, recovery 0.45 s.</summary>
    public const string DodgePacedSwitch = "dodge-paced";

    PaceData? paceData;
    RangedData? rangedData;

    // ---- bows (Data/combat/ranged.json, RangedData) ----
    /// <summary>The bows' numbers, the aim's spread and an arrow's harm (<c>Data/combat/ranged.json</c>, <see cref="RangedData"/>).</summary>
    public const string RangedAsset = "combat/ranged";

    /// <summary>The bows <see cref="Configure"/> read.</summary>
    public RangedData Ranged => rangedData ?? throw new InvalidOperationException($"module {ModuleId} is not configured");
    // ---- end
    bool combatTempoOff, fightingPaceOff, dodgePaced;

    /// <summary>
    /// Rushing creatures keep their own pace through their blows (<c>--rush-unpaced</c>): the bodies whose sheet is one of
    /// pace.json's <c>rush_unpaced.templates</c> (the szlachta) are not held to the fighting pace (<see cref="RushUnpacedSystem"/>). Off by
    /// default (by design).
    /// </summary>
    public const string RushUnpacedSwitch = "rush-unpaced";
    bool rushUnpaced;

    /// <summary>The global combat tempo this world plays (<see cref="CombatTempoOffSwitch"/>: 1; else the program's override, else pace.json's).</summary>
    static float GlobalTempo(Vitomir.Platform.Core.World world, PaceData? pace, bool combatTempoOff) =>
        combatTempoOff || pace is null ? 1f : world.TryService<CombatTempoOverride>(out var o) ? o.Tempo : pace.Tempo;
    // ---- end

    // ---- the brain's pace by the kind of person and the blade guard by training ----
    /// <summary>
    /// The duelling brain's constant aggression (<c>--pace-off</c>; the arena spec's <c>switches</c>): no pause
    /// between its runs of blows, an arena spec's <c>brain.pace</c> included. On by default: guard.json's <c>brain_pace</c>.
    /// </summary>
    public const string PaceOffSwitch = "pace-off";

    /// <summary>
    /// Everyone's brain guards with a blade by skill as (<c>--blade-guard-everyone</c>): no untrained fighter
    /// (guard.json's <c>skill.blade_guard_least</c>, below it only a shield guards).
    /// </summary>
    public const string BladeGuardEveryoneSwitch = "blade-guard-everyone";

    bool paceOff, bladeGuardEveryone;
    // ---- end

    // ---- the guard by skill, the brain's parry, the stagger when hit, the bigger rebound and the stun from the swing's end ----
    /// <summary>
    /// The guard by skill off (<c>--guard-skill-off</c>; the arena spec's <c>switches</c>): every fighter guards with
    /// The earlier build's numbers (no <see cref="GuardSkill"/>: the solver's hand speeds and read time, the brain's chances), to compare. On by
    /// default: guard.json's <c>skill</c>.
    /// </summary>
    public const string GuardSkillOffSwitch = "guard-skill-off";

    /// <summary>The duelling brain's parry as before (<c>--brain-parry-fixed</c>): 0.25 s before the strike mark whatever its own swing (<see cref="GuardSettings.BrainParryByTravel"/>).</summary>
    public const string BrainParryFixedSwitch = "brain-parry-fixed";

    /// <summary>
    /// The flinch as before (<c>--stagger-oneone</c>): a wounding blow plays the hurt at most once in 3.5 s (1.1's wait),
    /// no push of the upper body, no stagger from an overpowered guard (<see cref="HurtRule.Stagger"/>). Otherwise the stagger when
    /// hit (<c>Data/combat/stagger.json</c>).
    /// </summary>
    public const string StaggerOneOneSwitch = "stagger-oneone";

    /// <summary>
    /// <i>"let's go with the full stagger v1.1 base game animation"</i>: by default the
    /// stagger plays 1.1's hurt clip whole (guard.json's <c>stagger.hurt</c> "whole"). <c>--stagger-push-only</c>: the push alone, as before
    /// shipped it (the swing cut short, the guard lowered and the body busy for 0.3–0.6 s, the chest pushed, upright and in reach).
    /// <c>--stagger-hurt-short</c>: 1.1's hurt clip cut at 0.3–0.6 s by the blow's impulse (the earlier build's <c>--stagger-hurt-clip</c>, kept as its
    /// alias so a tape that names it replays). (<see cref="HurtRule.StaggerHurtClip"/>, <see cref="HurtRule.StaggerHurtWhole"/>.)
    /// </summary>
    public const string StaggerPushOnlySwitch = "stagger-push-only", StaggerHurtShortSwitch = "stagger-hurt-short", StaggerHurtClipSwitch = "stagger-hurt-clip";

    /// <summary>The blocked blade's rebound with the earlier build's sizes (<c>--rebound-two-thirds</c>): 0.012 m per N·s within 0.15–0.5 m, the recoil 0.003 m per N·s to 0.12 m, a parry ×2 — more than before.</summary>
    public const string ReboundTwoThirdsSwitch = "rebound-two-thirds";

    /// <summary>The attacker's stun counted from the meeting, as before (<c>--rebound-stun-at-meeting</c>); by default from the blocked swing's end.</summary>
    public const string ReboundStunAtMeetingSwitch = "rebound-stun-at-meeting";

    bool guardSkillOn = true, brainParryFixed, staggerOneOne, staggerPushOnly, staggerHurtShort, reboundTwoThirds, reboundStunAtMeeting;
    StaggerData? stagger;
    // ---- end

    GuardData? guard;
    bool guardOn = true;
    bool guardSightOn = true, guardSightBody;   // the guard's sight; the player's view or the body's facing
    bool guardSolverOn = true;                   // the guard's arm solver
    FlurryData flurry = FlurryData.Defaults;
    FlurryModel flurryModel = FlurryModel.Geometric;
    bool guardTechniqueOn = true, guardMeleeOn = true;   // the guard's technique, Melee's fourth lane
    bool contestDexterityPerformed;                       // the contest's defence Dexterity only with a performed defence

    /// <summary>
    /// The contact data of <c>Data/combat/</c> (ours), read strictly (<see cref="CombatFiles"/>): one
    /// <c>VITOMIR_COMBAT_MATTER</c> file is required; <c>VITOMIR_PROTECTED</c> files add protected people; files of other
    /// schemas there are not contact's and are left alone.
    /// </summary>
    static (CombatFiles.Matter, List<string>)? ReadCombat(ModuleAssets assets, List<string> problems)
    {
        int before = problems.Count;
        CombatFiles.Matter? matter = null;
        AssetId? matterId = null;
        var people = new List<string>();
        foreach (var asset in assets.List(CombatFolder))
        {
            if (Load(assets, asset, problems) is not { } root) continue;
            switch (CombatFiles.SchemaOf(root))
            {
                case CombatFiles.MatterSchema:
                    if (matterId is { } first) { problems.Add($"{asset}: the contact matter is also in {first}"); continue; }
                    matterId = asset;
                    matter = CombatFiles.ReadMatter(asset, root, problems);
                    break;
                case CombatFiles.ProtectedSchema:
                    if (CombatFiles.ReadProtected(asset, root, problems) is { } list) people.AddRange(list);
                    break;
            }
        }
        if (matterId is null) problems.Add($"{assets.Module}:{CombatFolder}/: no {CombatFiles.MatterSchema} file (the weapons' and shields' matter, the active window's rule)");
        return problems.Count == before && matter is not null ? (matter, people) : null;
    }

    /// <summary>
    /// The perception of <c>Data/perception/</c> (ours), read strictly (<see cref="PerceptionFiles"/>): one file of
    /// the world's numbers and at least one kind of observer are required, and the default kind must be one of them.
    /// </summary>
    static Perceiving? ReadPerception(ModuleAssets assets, List<string> problems)
    {
        int before = problems.Count;
        (PerceptionNumbers Numbers, ActResponse[] Acts, bool Sandbox, string? DefaultKind)? world = null;
        AssetId? worldId = null;
        var kinds = new List<ObserverKind>();
        foreach (var asset in assets.List(PerceptionFolder))
        {
            if (Load(assets, asset, problems) is not { } root) continue;
            switch (PerceptionFiles.SchemaOf(root))
            {
                case PerceptionFiles.WorldSchema:
                    if (worldId is { } first) { problems.Add($"{asset}: the world's perception numbers are also in {first}"); continue; }
                    worldId = asset;
                    world = PerceptionFiles.ReadWorld(asset, root, problems);
                    break;
                case PerceptionFiles.KindSchema:
                    if (PerceptionFiles.ReadKind(asset, root, problems) is { } kind) kinds.Add(kind);
                    break;
                default:
                    problems.Add($"{asset}: schema is neither {PerceptionFiles.WorldSchema} nor {PerceptionFiles.KindSchema}");
                    break;
            }
        }
        string where = $"{assets.Module}:{PerceptionFolder}/";
        if (worldId is null) problems.Add($"{where}: no {PerceptionFiles.WorldSchema} file (the world's perception numbers)");
        if (kinds.Count == 0) problems.Add($"{where}: no {PerceptionFiles.KindSchema} file (a kind of observer)");
        if (world is { DefaultKind: { } d } && !kinds.Any(k => k.Name == d || k.Id.ToString() == d))
            problems.Add($"{worldId}: defaultKind '{d}' is no kind of {where} (the kinds: {string.Join(", ", kinds.Select(k => k.Name))})");
        return problems.Count == before && world is { } w ? new Perceiving(w.Numbers, w.Acts, w.Sandbox, w.DefaultKind, kinds) : null;
    }

    /// <summary>
    /// The movement profiles of <c>Data/movement/</c> (ours), read strictly (<see cref="MovementProfileData"/>),
    /// the human's (<see cref="HumanMovement"/>) first and required: the masquerade holds every body to it.
    /// </summary>
    static List<MovementProfile> ReadMovement(ModuleAssets assets, List<string> problems)
    {
        var list = new List<MovementProfile>();
        var humanId = assets.Own(HumanMovement);
        foreach (var asset in assets.List(MovementFolder))
        {
            if (Load(assets, asset, problems) is not { } root) continue;
            if (MovementProfileData.Read(asset, root, problems) is { } p)
            {
                if (asset == humanId) list.Insert(0, p);
                else list.Add(p);
            }
        }
        if (!assets.Exists(humanId))
            problems.Add($"{humanId}: missing (the human's movement profile: the masquerade holds every body to it; Data/{HumanMovement}.json)");
        return list;
    }

    /// <summary>Adds the module's component and services to the world, and its hook dressing the people the program makes. Configure first.</summary>
    public void Register(WorldBuilder world)
    {
        var c = Config;
        world.Component<ActorIdentity>();
        world.Service(c);
        var sheets = new ActorSheets(c.Layout, world.Peek);
        world.Service(sheets);
        var files = sheetFiles;
        var harmData = harm;
        var bloodData = blood;
        world.People.OnMade((e, role, character) => Dress(sheets, c, files, harmData, bloodData, e, role, character));
        // The program's route to a person's sheet (the arena's fighters, --sheet who=file), and the staged harm.
        // ---- armour's rules (a service the staged harm and the sheets' port read), and the people the
        // platform makes dressed by the 1.1 template they are drawn as (the data's dress; in the sandbox, after their sheet).
        var armorRules = armorData is not null && harmData is not null ? new ArmorRules(armorData, armorOn) : null;
        if (armorRules is not null)
        {
            ArmorServices.Register(world, armorRules);
            if (c.IsSandbox)
                world.People.OnMade((e, role, character) =>
                {
                    if (armorRules.Data.DressOf(character) is { } pieces && sheets.Has(e)) armorRules.TryWear(sheets, e, pieces, out _);
                });
        }
        // ---- end
        world.Service<IPersonSheets>(new PersonSheetsPort(sheets, c, harmData, files, bloodData, powerData) { Armor = armorRules });   // its armour
        if (harmData is not null)
            HarmServices.Register(world, WithQuality(Performed(HarmRules.Build(c, harmData, contestOn ? skill : null))),   // the defence's Dexterity; blow quality
                severance is not null ? new SeveranceRules(severance, severanceOn, severanceAngleOn, severanceEdgeOn) : null);   // severance, unless the data lacks it
        // Blood's ledger and the powers (the contract's stores and events, the catalogue, enhanced.powers), Celerity's
        // effect on the curve the switches choose.
        CelerityEffect? celerity = null;
        if (bloodData is not null && powerData is not null)
        {
            var ledger = new BloodLedger(sheets, bloodData, world.Peek);
            if (powerData.Powers.FirstOrDefault(p => p.Kind == PowerKind.Cast && p.Effect == "celerity") is { } cel)
                celerity = new CelerityEffect(cel, cel.Curves[CelerityCurveName(cel)], celerityAttackOn);
            PowerServices.Register(world, new PowerBook(powerData, ledger, celerity, harmData?.HealthResource));
        }
        // The tempo channel — every fighter's CombatTempo from its Melee or Brawl (off: the component is not even
        // registered); and Celerity's attack factor while it is live, unless celerity-attack-off.
        // With the global combat tempo (pace.json's, the program's --combat-tempo over it; combat-tempo-off: none).
        if (harmData is not null && c.IsSandbox)
            SkillTempoServices.Register(world, tempoOn ? skill : null, harmData, celerityAttackOn ? celerity : null, GlobalTempo(world.Peek, paceData, combatTempoOff));
        // The dodge's numbers from Dodge and Dexterity (skill.json's dodge), whatever the tempo's switch; at
        // pace.json's dodge pace with dodge-paced (or its dodge.use).
        if (harmData is not null && c.IsSandbox)
            SkillTempoServices.RegisterDodge(world, skill, dodgeDexterityOn, paceData is { } pd && (dodgePaced || pd.DodgeUse) ? pd : null);
        // The fighting pace (the platform's, in the bodies' movement), unless fighting-pace-off.
        if (c.IsSandbox && paceData is not null && !fightingPaceOff && !world.Peek.TryService<Platform.Bodies.FightingPace>(out _))
            world.Service(new Platform.Bodies.FightingPace { MaxSpeed = paceData.FightingPace });
        // Rushing creatures let through the fighting pace, with rush-unpaced (nothing registered without it).
        if (c.IsSandbox && paceData is not null && !fightingPaceOff && rushUnpaced) RushUnpacedSystem.Register(world, paceData.RushUnpaced);
        // A world with a bow (the platform's BowConfig registered: a cast fighter carries one) is given the bows, the aim's
        // spread by the archer's Archery and an arrow's harm; a world without one gets nothing.
        if (rangedData is not null && world.Peek.TryService<BowConfig>(out var bowConfig))
            RangedServices.Register(world, rangedData, bowConfig, sheets, c.Layout.Traits.Any(t => t.Id == rangedData.SpreadTrait));

        // The bodies' movement: the profiles to the platform's list, the human's the masquerade's; each person
        // made is given its own — the player the one the program names (--profile), else the human; the townsfolk human.
        if (world.Peek.TryService<MovementProfiles>(out var profiles))
        {
            int human = -1;
            foreach (var p in movement)
            {
                int i = profiles.Add(p, isHuman: p.Id.Path == HumanMovement);
                if (p.Id.Path == HumanMovement) human = i;
            }
            var w = world.Peek;
            // The movement work: the spring's numbers (the player's, by its profile's half-life), unless accel-ramp.
            if (!accelRamp)
                Platform.Bodies.StartStopSprings.Register(world, new Platform.Bodies.StartStopSprings
                {
                    Default = human >= 0 ? profiles.All[human].HalfLife : Platform.Bodies.StartStopSprings.Sandbox,
                    LandingSlide = landingSlide,
                });
            world.People.OnMade((e, role) =>
            {
                if (!w.Has<Platform.Bodies.Locomotion>(e)) return;
                int chosen = human;
                if (role == PersonRole.Player && profiles.PlayerProfile is { } name)
                    chosen = profiles.IndexOf(name) is var i and >= 0
                        ? i : throw new InvalidOperationException($"no movement profile '{name}' (the profiles are {profiles.Names})");
                if (chosen >= 0) profiles.Give(w, e, chosen);
            });
        }

        // Who sees: the world's numbers and the kinds to the platform's perception; the program makes each
        // townsperson an observer of its character's kind.
        if (perceiving is { } p2 && world.Peek.TryService<PerceptionConfig>(out var perception))
        {
            perception.Set(p2.Numbers, p2.Sandbox);
            p2.Acts.CopyTo(perception.Acts, 0);
            foreach (var k in p2.Kinds) perception.Add(k);
            perception.DefaultKind = p2.DefaultKind;
        }

        // Contact: the matter, the window's rule and the protected people to the platform's contact.
        if (contact is { } c2 && world.Peek.TryService<ContactConfig>(out var touch))
        {
            c2.Matter.Apply(touch);
            foreach (var name in c2.Protected) touch.Protected.Add(name);
            if (contactStopOff) touch.Stop = touch.Stop with { Enabled = false };   // The first contact rule, to compare
            if (reboundSize != 1f) touch.Stop = touch.Stop.Sized(reboundSize);   // the film's sizes
            if (reboundStunAfter) touch.Stop = touch.Stop with { StunFromSwingEnd = true };   // the stun on top of the swing's time
            // The earlier build's sizes (since made bigger) and its stun counted from the meeting (since counted from the swing's end)
            if (reboundTwoThirds) touch.Stop = touch.Stop with
            {
                ReboundMetresPerImpulse = 0.012f, ReboundLeastMetres = 0.15f, ReboundMostMetres = 0.5f, RecoilMetresPerImpulse = 0.003f, RecoilMostMetres = 0.12f,
            };
            if (reboundStunAtMeeting) touch.Stop = touch.Stop with { StunFromSwingEnd = false };
            if (reboundFixed) touch.Stop = touch.Stop.FixedRebound();   // The earlier build's rebound, no recoil, no stun, to compare
            if (windowFromWindUp) touch.Window = touch.Window with { StrikeStarts = [] };   // The earlier build's windows
        }

        // The guard — the contest and its verdict, and the platform's numbers; off, the earlier build's guard.
        if (guard is not null && guardOn && harmData is not null) GuardServices.Register(world, new GuardRules(guard, guardTechniqueOn, guardMeleeOn));   // the switches
        else if (!guardOn && world.Peek.TryService<GuardSettings>(out var gs)) { gs.Enabled = false; gs.RulesDecide = false; }
        if (!guardSightOn && world.Peek.TryService<GuardSettings>(out var sight)) sight.SightRequired = false;   // the guard's sight off, the earlier build's
        if (guardSightBody && world.Peek.TryService<GuardSettings>(out var byBody)) byBody.PlayerSightByView = false;   // the player by the body
        if (!guardSolverOn && world.Peek.TryService<GuardSettings>(out var solverOff)) solverOff.Solver = false;   // The earlier build's composed guard
        // ---- the guard by skill (Dexterity and Melee, Brawl bare-handed; guard.json's skill), unless guard-skill-off;
        // the brain's parry timed by its hand's travel, unless brain-parry-fixed; the stagger when hit, unless stagger-oneone.
        if (guard is not null && guardOn && guardSkillOn && guard.Skill is { } curve && skill is not null && harmData is not null && c.IsSandbox)
            GuardSkillSystem.Register(world, curve, guard.AttributeDice, skill, harmData, celerityAttackOn ? celerity : null, bladeGate: !bladeGuardEveryone,
                shieldRead: shieldReadOff ? null : guard.ShieldRead);   // the shield read, unless shield-read-off
        // The brain's pace by the kind of person (guard.json's brain_pace), unless pace-off.
        if (guard is not null && guardOn && !paceOff && guard.Pace is { } pace && skill is not null && harmData is not null && c.IsSandbox)
            DuelPaceSystem.Register(world, pace, guard.Skill?.BladeGuardLeast ?? 1, skill, harmData);
        if (brainParryFixed && world.Peek.TryService<GuardSettings>(out var parryTimed)) parryTimed.BrainParryByTravel = false;
        if (reboundTwoThirds && world.Peek.TryService<GuardSettings>(out var parryScale)) parryScale.ParryReboundScale = 2f;   // The earlier build's parry: twice the held
        if (world.Peek.TryService<HurtRule>(out var hurtRule))
        {
            if (staggerOneOne) hurtRule.Stagger = false;
            else if (stagger is { } st) st.Apply(hurtRule);
            // The whole hurt clip by default (the data); the push alone, or the hurt cut short, to compare.
            if (!staggerOneOne && staggerHurtShort) { hurtRule.StaggerHurtClip = true; hurtRule.StaggerHurtWhole = false; }
            if (!staggerOneOne && staggerPushOnly) hurtRule.StaggerHurtClip = false;
        }
        // ---- end

        // The quality work's follow-up: the duelling brain's range keeping off (the service registered here when the brains are not yet).
        if (brainRangeOff)
        {
            if (!world.Peek.TryService<DuelRangeSettings>(out var range)) world.Service(range = new DuelRangeSettings());
            range.Enabled = false;
        }

        // The duelling brain closes round a wall when its straight line meets the level; brain-straight: the earlier build's straight closing.
        if (!world.Peek.TryService<DuelRoutes>(out var duelRoutes)) world.Service(duelRoutes = new DuelRoutes());
        duelRoutes.Enabled = !brainStraight;

        // Directional attacks — on unless attack-spin (then 1.1's tap-and-hold); the table and numbers from the data.
        if (!world.Peek.TryService<Platform.Control.DirectionalAttacks>(out var dirs)) world.Service(dirs = new Platform.Control.DirectionalAttacks());
        directions?.Apply(dirs);
        dirs.Enabled = !attackSpin;

        // The flurry's model and numbers to the platform's side (registered with the harm, or by the program).
        if (world.Peek.TryService<FlurrySettings>(out var fs)) flurry.Apply(fs, flurryModel);
    }

    /// <summary>
    /// A person's sheet as it is made (ours, the program gave these itself until then). In the sandbox: the
    /// fighter's template whose characters name what the person is drawn as (<c>Data/sheets/</c>; Christof's for the
    /// player), else the player a vampire of the Dark Ages with a blank sheet and a townsperson the placeholder
    /// townsperson's sheet (<see cref="SheetTemplates.Townsperson"/>, every Attribute 25); and every one of them the
    /// engine-sandbox Health (<see cref="GiveHealth"/>). Outside it: the player's blank vampire, a townsperson's blank human.
    /// </summary>
    static void Dress(ActorSheets sheets, EnhancedConfig config, IReadOnlyList<SheetFile> files, HarmData? harm, BloodData? blood,
        Platform.Core.EntityId e, PersonRole role, string? character)
    {
        if (!config.IsSandbox)
        {
            if (role == PersonRole.Player) sheets.Give(e, "vampire", era: "dark-ages");
            else sheets.Give(e, "human");
            GiveBlood(sheets, blood, e);
            return;
        }
        var file = DressedBy(files, role, character);
        if (file is not null) sheets.Import(e, file.Template.Sheet);
        else if (role == PersonRole.Player) sheets.Give(e, "vampire", era: "dark-ages");
        else SheetTemplates.GiveTownsperson(sheets, config, e);
        GiveHealth(sheets, harm, e);
        GiveBlood(sheets, blood, e);
    }

    /// <summary>
    /// The template that dresses a person made in the sandbox: the first whose characters name what it is
    /// drawn as (its role the person's, or none), else its role's default — the template with that role and no characters
    /// (<c>sandbox.player</c> for the player, <c>sandbox.townsperson_reference</c> for the townsfolk); null when there is neither.
    /// </summary>
    public static SheetFile? DressedBy(IReadOnlyList<SheetFile> files, PersonRole role, string? character)
    {
        string roleName = role == PersonRole.Player ? "player" : "townsperson";
        if (character is not null)
            foreach (var f in files)
                if ((f.Role.Length == 0 || f.Role == roleName) && f.Characters.Contains(character, StringComparer.OrdinalIgnoreCase)) return f;
        foreach (var f in files)
            if (f.Role == roleName && f.Characters.Count == 0) return f;
        return null;
    }

    /// <summary>
    /// Gives a sheet its Blood (<c>Data/rules/blood.json</c>, ENGINE_SANDBOX) when its species has Blood: the purchased
    /// capacity <c>blood_pool</c> at the data's default (100, 1.1's Christof) when the sheet holds none, and the <c>blood</c>
    /// resource with the maximum the purchase gives (1:1) — full when new, else its current held to the maximum. A mortal is
    /// left without (it has no Blood to spend). Returns whether the sheet has Blood.
    /// </summary>
    public static bool GiveBlood(ActorSheets sheets, BloodData? blood, Platform.Core.EntityId e)
    {
        if (blood is null || !sheets.Has(e) || !blood.HasBlood(sheets.Species(e))) return false;
        int pool = sheets.Bought(e, blood.PurchaseId) ?? blood.PurchaseDefault;
        if (sheets.Bought(e, blood.PurchaseId) is null) sheets.SetBought(e, blood.PurchaseId, pool);
        long most = blood.MaximumOf(pool);
        var had = sheets.Resource(e, blood.Resource);
        sheets.SetResource(e, had is { } r
            ? r with { Maximum = most, Current = Math.Min(r.Current, most), PerActionSpendLimit = blood.PerActionLimit, PerCycleSpendLimit = blood.PerSecondLimit }
            : new ResourceState(blood.Resource, most, most, blood.PerActionLimit, blood.PerSecondLimit));
        return true;
    }

    /// <summary>
    /// Gives a sheet the engine-sandbox Health (<c>Data/rules/harm.json</c>, ENGINE_SANDBOX: the profile's
    /// <c>health_max_purchase_curve</c> is null). Health is <b>bought</b>, as Blood is — the purchased
    /// capacity <c>health_pool</c> (1 to 1000) at the data's default (100, every species) when the sheet holds none, and the
    /// <c>health</c> resource with the maximum it gives (1:1): <b>full when new</b>; a sheet's own Health is kept, held to the
    /// maximum when that is lower, and <b>a full one stays full</b> when the maximum is higher (a sheet's 7 of 7 is 100
    /// of 100). Returns whether the sheet had no Health before (it was given one).
    /// </summary>
    public static bool GiveHealth(ActorSheets sheets, HarmData? harm, Platform.Core.EntityId e)
    {
        if (harm is null || harm.HealthStatus != "ENGINE_SANDBOX" || harm.HealthMaximum < 1 || !sheets.Has(e)) return false;
        int pool = sheets.Bought(e, harm.HealthPurchaseId) ?? harm.HealthMaximum;
        if (sheets.Bought(e, harm.HealthPurchaseId) is null) sheets.SetBought(e, harm.HealthPurchaseId, pool);
        long most = harm.MaximumOf(pool);
        var had = sheets.Resource(e, harm.HealthResource);
        if (had is { } r)
        {
            long current = r.Current >= r.Maximum && most > r.Maximum ? most : Math.Min(r.Current, most);
            if (r.Maximum != most || r.Current != current) sheets.SetResource(e, r with { Maximum = most, Current = current });
            return false;
        }
        sheets.SetResource(e, new ResourceState(harm.HealthResource, most, most, 0, 0));
        return true;
    }

    /// <summary>
    /// The configuration hash: SHA-256 (hex) of the canonical JSON (<see cref="CanonicalJson"/>) of
    /// <c>{"profile": the profile file, "registry": the registry file}</c>. The same for any layout of the
    /// same two files; different when any value of either changes.
    /// </summary>
    public static string ConfigurationHash(JsonElement registry, JsonElement profile)
    {
        var buffer = new ArrayBufferWriter<byte>(8192);
        buffer.Write("{\"profile\":"u8);
        CanonicalJson.Write(profile, buffer);
        buffer.Write(",\"registry\":"u8);
        CanonicalJson.Write(registry, buffer);
        buffer.Write("}"u8);
        return ContentHash.Sha256Hex(buffer.WrittenSpan);
    }

    static JsonElement? Load(ModuleAssets assets, AssetId id, List<string> problems)
    {
        if (!assets.Exists(id)) { problems.Add($"{id}: missing (no such file in the module's Data/)"); return null; }
        try
        {
            using var doc = JsonDocument.Parse(assets.ReadText(id));
            return doc.RootElement.Clone();
        }
        catch (JsonException e) { problems.Add($"{id}: not valid JSON ({e.Message})"); return null; }
    }

    static string Word(bool? b) => b switch { true => "true", false => "false", null => "absent" };

    /// <summary>The hint for a refusal without the sandbox switch (the manifest's words for it).</summary>
    public const string SandboxHint = "Start with --sandbox to run the research sandbox profile (the shipping profile refuses: fail closed).";

    string hint = "";

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    void Refuse(IReadOnlyList<string> reasons) => throw new ModuleRefusedException(ModuleId, [.. reasons]) { Hint = hint };
}
