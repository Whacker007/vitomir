// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>What became of a contact the rules read (the rules package's <c>Outcome</c>, the ones the staged harm can reach).</summary>
public enum HarmOutcome
{
    /// <summary>Wounds were dealt (at least one).</summary>
    Applied,
    /// <summary>The blow landed and dealt no wound (why, in <see cref="HarmResult.ResistedBy"/> — the damage made nothing, soak took it all, or halving).</summary>
    Resisted,
    /// <summary>A shield or a weapon was met (or the blow was blocked before the body): no harm through a shield.</summary>
    Blocked,
    /// <summary>The level was met: no person, no harm.</summary>
    Missed,
    /// <summary>A protected person: found, but harm does not follow.</summary>
    Denied,
    /// <summary>The rules cannot resolve it: a value is null, a sheet or the Health is missing, a weapon has no class (named in <see cref="HarmResult.Refusal"/>).</summary>
    Unresolved,
}

/// <summary>The attacker as the staged harm sees it: its species, effective Strength, Potence (rank, and whether its passive option is learned) and the weapon template it strikes with (null: unknown; empty: bare hands); for the contest: its effective Dexterity, Melee and Brawl.</summary>
public readonly record struct Striker(EntityId Who, string Species, int Strength, int PotenceRank, bool PotenceLearned, string? Weapon,
    int Dexterity = 0, int Melee = 0, int Brawl = 0,
    string? SwingClip = null, string? Character = null,   // the clip its swing plays and its body's kind (blow quality's reference)
    double? WorkDice = null);   // an arrow's potential, its work's dice (ranged.json's arrow_harm), in place of Strength's; null for a blow

/// <summary>
/// The one struck: its species, effective Stamina, Fortitude (rank, and whether its passive is learned) and its Health (whether
/// it has one, now and at most); for the contest: its effective Dexterity, Dodge, Melee and Brawl, whether it holds a
/// weapon (a class that is not bare-handed) and whether it was <b>guarding</b> (its block raised) when struck; whether it was
/// <b>dodging</b> (the platform's dodge in its start or its burst) when struck; what it <b>wears</b>
/// (<see cref="WornArmor"/>, null without armour's rules).
/// </summary>
public readonly record struct Struck(EntityId Who, string Species, int Stamina, int FortitudeRank, bool FortitudeLearned, bool HasHealth, long Health, long HealthMaximum,
    int Dexterity = 0, int Dodge = 0, int Melee = 0, bool Armed = false, bool Guarding = false, int Brawl = 0, bool Dodging = false,
    WornArmor? Armor = null);   // what it wears (null: no armour's rules)

/// <summary>
/// One contact's staged harm, stage by stage (ours): reused from contact to contact (<see cref="Reset"/>), so
/// resolving allocates nothing but the draws' list growing. Whatever stage it stopped at, the numbers before it are kept.
/// </summary>
public sealed class HarmResult
{
    public HarmOutcome Outcome;
    /// <summary>Why it is <see cref="HarmOutcome.Unresolved"/> or <see cref="HarmOutcome.Denied"/> (a sentence naming what), else null.</summary>
    public string? Refusal;

    // potential
    public string? Weapon;
    public WeaponClass? Class;
    public HarmKind Kind;
    public double Bonus;
    public int Strength;
    public double StrengthDice, Potential;
    // the contest (when the module's contest runs)
    public bool Contested;
    public string AttackAbilityId = "", DefenceAbilityId = "";
    public int AttackDexterity, AttackAbility, AttackPool, AttackSuccesses;
    public double AttackDexterityDice, AttackPotential;
    public int DefenceDexterity, DefenceDodge, DefenceMelee, DefenceAbility, DefencePool, DefenceSuccesses;
    public bool DefenceArmed, DefenceGuarding;
    public double DefenceDexterityDice, DefencePotential;
    public int ContestNet, PoolBeforeContest, ContestChange;
    public bool ContestLimited;
    // the target was dead: no harm on the dead
    public bool DeadTarget;
    // conversion
    public int DamagePool, DamageSuccesses;
    // auto successes
    public int PotenceRank;
    public bool PotenceLearned;
    public int AutoSuccesses;
    // soak
    public bool StaminaSoaks, FortitudeSoaks;
    public int Stamina, FortitudeRank;
    public double StaminaDice, FortitudeDice, SoakPotential;
    public int SoakPool, SoakSuccesses;
    // wounds
    public int WoundsBeforeHalving, Wounds;
    public bool Halved;
    /// <summary>Why a resolved contact dealt no wound (<see cref="Platform.Combat.ResistedBy.None"/> when it dealt one): the "resisted" split.</summary>
    public Platform.Combat.ResistedBy ResistedBy;
    // health
    public long HealthBefore, HealthAfter, HealthMaximum;
    public bool Died;
    /// <summary>The Health a wound converts to (the translator, <c>hp_per_wound</c>), the Health the wounds took (at most what there was), and the target's band before and after (labels only).</summary>
    public int HealthPerWound;
    public long HealthLost;
    public HealthBand BandBefore, BandAfter;
    /// <summary>The blow's place in its swing's flurry (0 the swing's own, 1, 2, … the extra blows) and the extra blows the swing carried (Celerity's flurry; 0 without).</summary>
    public int Flurry, FlurryBlows;
    // The chain and the swing's attack budget
    /// <summary>The contact's place in its swing's chain (<see cref="ContactMade.Chain"/>), the share of the swing's budget it was resolved from (1 for the first) and the share left after it (0 when it stopped the blade), whether the blade goes on, and the potential and automatic successes the whole budget would have given.</summary>
    public int Chain;
    public float BudgetIn = 1f, BudgetOut;
    public bool Continues;
    public double FullPotential;
    public int FullAutoSuccesses;
    // Severance
    /// <summary>The region struck, the blow's post-soak harm in Health (before Health caps it), the part's threshold (0 when not severable), whether the blow could cut (an edged class, lethal or aggravated, a severable part), why it did not cut (null when it did), the part it cut off and whether that stops the body.</summary>
    public BodyRegion Region;
    public long PostSoakHealth;
    public int SeverThreshold;
    public double SeverThresholdShare;
    public bool SeverEligible;
    public SeverVerdict Verdict;
    public BodyRegion Severed;
    public bool StopsBody;
    /// <summary>What the body was missing before and after, the structural deficit before and after (Health off the effective maximum: the union), the loss this blow added, and the purchased maximum they are shares of.</summary>
    public uint MissingBefore, MissingAfter;
    public long DeficitBefore, DeficitAfter, StructuralLoss, PurchasedMaximum;
    /// <summary>Whether severance was judged for this blow (the swing's own blow on a body, the rules on or off).</summary>
    public bool SeveranceJudged;
    // ---- the blow's angle band, its cut share, Melee's edge and the body's part table ----
    /// <summary>The band the blow met the body in and its sine (impact speed over the point's whole speed); whether
    /// the angle's share and Melee's edge counted; the band's share (before the edge), the edge's Ability rank (−1 none) and factor,
    /// the cut share used and the harm that counted toward cutting (post-soak harm × cut share); the body's part table (null: none).</summary>
    public AngleBand Angle;
    public float Sine;
    public bool AngleOn, EdgeOn;
    public float CutShareBase = 1f, CutShare = 1f;
    public int EdgeRank = -1;
    public string EdgeAbility = "";
    public double EdgeFactor = 1, CutHarm;
    public string? Body;
    /// <summary>The angle's and the edge's data the blow was judged by (for the trace; null when not judged).</summary>
    public SeveranceAngle? AngleRule;
    public SeveranceEdge? EdgeRule;
    // ---- end ----

    // ---- the armour over the struck part
    /// <summary>Whether armour was judged for this blow (armour's rules registered), the region struck, the worn set's text, the pieces over the part (<c>padded_coat+mail_shirt</c>, null for none), what the armour did, its dice (the layered potential), the pool they made and its successes (both included in <see cref="SoakPool"/> and <see cref="SoakSuccesses"/>), and the wounds it kept off.</summary>
    public bool ArmorJudged;
    public BodyRegion ArmorRegion;
    public string ArmorWorn = "";
    public string? Armor;
    public ArmorVerdict ArmorVerdict;
    public double ArmorPotential;
    public int ArmorPool, ArmorSuccesses, ArmorKept;
    // ---- end
    // ---- an unavailable part's counterfactual (trace only, nothing reads it for an outcome)
    /// <summary>For a blow on a part whose severance is unavailable (a leg), the part it is read against and that part's threshold share and threshold in Health (<see cref="SeveranceData.CounterfactualOf"/>); <see cref="BodyRegion.None"/> and 0 otherwise.</summary>
    public BodyRegion CounterfactualRegion;
    public double CounterfactualShare;
    public int CounterfactualThreshold;
    /// <summary>Whether the blow would have cut the part read against (an edged class, a kind that cuts, a cut share above 0, its cutting harm at or over the threshold): trace only.</summary>
    public bool CounterfactualWouldCut;
    // ---- end

    // ---- the logical strike and, for an extra blow, what its check found and the guard's verdict on it
    /// <summary>The logical strike this blow is (<see cref="FlurryBlows.Id"/>: the contact's own blow 0, its extra blows 1, 2, …),
    /// what the check at an extra blow's moment found (<see cref="FlurryCheck.Met"/> for the swing's own blow), and the guard's verdict
    /// on an extra blow met at a guard (null for none).</summary>
    public long Strike;
    public FlurryCheck FlurryCheck;
    public GuardVerdict? GuardVerdict;
    // ---- end

    // ---- blow quality (gate-08; BlowQuality) ----
    /// <summary>Whether the quality stage judged this blow and applied (<c>blow-quality-off</c>: judged, not applied),
    /// whether it was measured (a peak speed), the swing's clip and the reference found (null: none, Q = 1), q (the contact's speed along
    /// the normal over its blow's peak), q_ref, Q, the factor applied (1 off) and the factor's own value, the damage potential before it.</summary>
    public bool QualityJudged, QualityOn, QualityMeasured;
    public string? QualityClip, QualityCharacter;
    public QualityReference? QualityReference;
    public BlowQualityData? QualityRule;
    public float QualitySpeedRatio, QualityRef, Quality = 1f, QualityFactor = 1f, QualityFactorRaw = 1f;
    public double PotentialBeforeQuality;
    /// <summary>The pool, its successes and the wounds the same dice would have given at Q = 1 (against the same soak), and
    /// whether that would have wounded where the blow did not (then <see cref="Platform.Combat.ResistedBy.LowWork"/>); the dice the
    /// blow counted (the first of those drawn: the rest were drawn for the Q = 1 pool only).</summary>
    public bool QualityRolled, QualityWouldWound;
    public int QualityPoolAtOne, QualitySuccessesAtOne, QualityWoundsAtOne, QualityDiceCounted;
    /// <summary>The hilt — whether the rule applied (on), whether the contact met within it (a blade's or a haft's class, along the
    /// blade below the data's limit), and whether it made the blow bashing.</summary>
    public bool HiltOn, HiltMeasured, Hilt;

    // ---- an arrow's contact (ContactMade.Shot above 0): its work at contact and the dice of it, in place of Strength's ----
    public bool Arrow;
    public double WorkDice, ArrowWork;
    public float ArrowSpeed, ArrowMassKg;
    public int ArrowShot;
    /// <summary>The cut share's band from Q (<see cref="BlowQuality"/>'s re-basing) and whether severance used it in place of the angle's.</summary>
    public AngleBand QualityCutBand;
    public bool CutShareFromQuality;
    // ---- end

    /// <summary>Every number drawn, in order: the damage pool's fraction, the damage dice, the soak pool's fraction, the soak dice.</summary>
    public readonly List<Draw> Draws = [];

    public void Reset()
    {
        Outcome = HarmOutcome.Unresolved; Refusal = null;
        Weapon = null; Class = null; Kind = HarmKind.Bashing; Bonus = 0; Strength = 0; StrengthDice = Potential = 0;
        DamagePool = DamageSuccesses = 0; PotenceRank = 0; PotenceLearned = false; AutoSuccesses = 0;
        StaminaSoaks = FortitudeSoaks = false; Stamina = FortitudeRank = 0; StaminaDice = FortitudeDice = SoakPotential = 0;
        SoakPool = SoakSuccesses = 0; WoundsBeforeHalving = Wounds = 0; Halved = false; ResistedBy = Platform.Combat.ResistedBy.None;
        HealthBefore = HealthAfter = HealthMaximum = 0; Died = false;
        HealthPerWound = 0; HealthLost = 0; BandBefore = BandAfter = HealthBand.None; Flurry = FlurryBlows = 0;
        Chain = 0; BudgetIn = 1f; BudgetOut = 0f; Continues = false; FullPotential = 0; FullAutoSuccesses = 0;
        Region = Severed = BodyRegion.None; PostSoakHealth = 0; SeverThreshold = 0; SeverThresholdShare = 0; SeverEligible = false; Verdict = SeverVerdict.NotJudged; StopsBody = false;
        MissingBefore = MissingAfter = 0; DeficitBefore = DeficitAfter = StructuralLoss = PurchasedMaximum = 0; SeveranceJudged = false;
        Angle = AngleBand.None; Sine = 0f; AngleOn = EdgeOn = false; CutShareBase = CutShare = 1f; EdgeRank = -1; EdgeAbility = ""; EdgeFactor = 1; CutHarm = 0; Body = null; AngleRule = null; EdgeRule = null;
        Contested = false; AttackAbilityId = DefenceAbilityId = "";
        AttackDexterity = AttackAbility = AttackPool = AttackSuccesses = 0; AttackDexterityDice = AttackPotential = 0;
        DefenceDexterity = DefenceDodge = DefenceMelee = DefenceAbility = DefencePool = DefenceSuccesses = 0; DefenceArmed = DefenceGuarding = false;
        DefenceDexterityDice = DefencePotential = 0; ContestNet = PoolBeforeContest = ContestChange = 0; ContestLimited = false;
        DeadTarget = false;
        ArmorJudged = false; ArmorRegion = BodyRegion.None; ArmorWorn = ""; Armor = null; ArmorVerdict = ArmorVerdict.NotJudged;
        ArmorPotential = 0; ArmorPool = ArmorSuccesses = ArmorKept = 0;
        Strike = 0; FlurryCheck = FlurryCheck.Met; GuardVerdict = null;
        CounterfactualRegion = BodyRegion.None; CounterfactualShare = 0; CounterfactualThreshold = 0; CounterfactualWouldCut = false;
        QualityJudged = QualityOn = QualityMeasured = false; QualityClip = QualityCharacter = null; QualityReference = null; QualityRule = null;
        QualitySpeedRatio = QualityRef = 0f; Quality = QualityFactor = QualityFactorRaw = 1f; PotentialBeforeQuality = 0;
        QualityRolled = QualityWouldWound = false; QualityPoolAtOne = QualitySuccessesAtOne = QualityWoundsAtOne = QualityDiceCounted = 0;
        HiltOn = HiltMeasured = Hilt = false; QualityCutBand = AngleBand.None; CutShareFromQuality = false;
        Arrow = false; WorkDice = ArrowWork = 0; ArrowSpeed = ArrowMassKg = 0f; ArrowShot = 0;
        Draws.Clear();
    }
}

/// <summary>
/// The staged harm (the rules package's combat rules), <b>on the research sandbox only</b>, from the evidence
/// of a contact. Built once from the settled configuration and <see cref="HarmData"/>; every profile value it needs is
/// read then, and a null one (or one it cannot read) makes it <b>refuse by name</b>: <see cref="Refusals"/> lists them
/// and every contact is then <see cref="HarmOutcome.Unresolved"/> with them. The stages:
/// <list type="number">
/// <item><b>Potential</b> <c>D = φ_strength(E_strength) + weapon</c>: <c>raw_to_damage_dice</c> of the attacker's effective
/// Strength plus its weapon class's addend (<c>weapon_damage_bonus_one_hand_sword</c> for a one-handed sword; the
/// others SANDBOX in the data). The contact's impact speed is recorded, <b>not</b> used: gate-08 (native contact quality
/// → damage potential) is open.</item>
/// <item><b>Conversion</b>: the potential made a whole pool (<see cref="Dice.Pool"/>) and rolled as d10s at
/// <c>damage_d10_difficulty</c> (<see cref="Dice.Successes"/>), from the damage stream. between the two, when the
/// module's contest runs (<see cref="Contest"/>), <b>the contest on contact</b> (<see cref="SkillContest"/>) adds to or takes
/// from the whole pool — never a gate on the hit.</item>
/// <item><b>Automatic successes</b>: <c>potence_damage_auto_success_per_rank</c> × Potence's rank (floored), only where the
/// attacker has learned <c>potence.passive</c> (a rank alone is not the option).</item>
/// <item><b>Soak</b>, type-legal: by the target's species and the harm's kind, <c>raw_to_soak_dice</c> of its
/// effective Stamina where Stamina may soak (a vampire's bashing and lethal; a human's bashing only), plus Fortitude's rank in
/// dice (ENGINE_SANDBOX: one per rank) where it is learned, aggravated only with Fortitude's permission; made whole and
/// rolled at <c>soak_d10_difficulty</c>, from the soak stream. then the <b>armour over the struck part</b>
/// (<see cref="ArmorRules.Soak"/>; its own pool, rolled after, its successes added), where armour's rules are registered.</item>
/// <item><b>Wounds</b> <c>W = max(0, N_damage + N_auto − N_soak)</c>; a vampire's bashing halved after soak, floored
/// (<c>vampire_bashing_halves_postsoak</c>).</item>
/// <item><b>Health</b>: the target's <c>health</c> resource (the engine-sandbox Health while the profile's
/// <c>health_max_purchase_curve</c> is null), less <c>min(H, W × hp_per_wound)</c> (Health on the 1–1000 scale, the
/// wounds kept as the stages' unit and converted by the module's translator); at nothing the zero-Health stop.</item>
/// </list>
/// The kind comes from the weapon's class. Every refusal is checked before any die is rolled, so a refused contact draws
/// nothing. Allocates nothing per contact (the result is reused).
/// </summary>
public sealed class HarmRules
{
    /// <summary>The profile values the stages read.</summary>
    public const string DamageDiceValue = "raw_to_damage_dice", DamageDifficultyValue = "damage_d10_difficulty",
        SoakDiceValue = "raw_to_soak_dice", SoakDifficultyValue = "soak_d10_difficulty";

    readonly Dictionary<string, (double Bonus, string? Refusal)> bonuses = new(StringComparer.Ordinal);
    readonly List<string> refusals = [];

    HarmRules(EnhancedConfig config, HarmData data)
    {
        Config = config;
        Data = data;
    }

    public EnhancedConfig Config { get; }
    public HarmData Data { get; }

    /// <summary>Why the stages cannot run at all (each value named with its gate), empty when they can.</summary>
    public IReadOnlyList<string> Refusals => refusals;

    public bool CanResolve => refusals.Count == 0;

    public DiceCurve? DamageCurve { get; private set; }
    public DiceCurve? SoakCurve { get; private set; }
    public int DamageDifficulty { get; private set; }
    public int SoakDifficulty { get; private set; }
    public double PotencePerRank { get; private set; }
    public bool BashingHalves { get; private set; }

    /// <summary>The contest on contact, or null when the module's contest is off (the stages exactly).</summary>
    public SkillContest? Contest { get; private set; }

    /// <summary>Whether the engine-sandbox Health stands in (the profile's Health is null).</summary>
    public bool EngineSandboxHealth { get; private set; }

    /// <summary>Blow quality's stage (<see cref="BlowQuality"/>), or null without it (the earlier build's stages exactly).</summary>
    public BlowQuality? Quality { get; set; }

    /// <summary>The profile values the stages read, by name (for the trace's gates).</summary>
    public IReadOnlyList<string> ValuesRead { get; private set; } = [];

    /// <summary>
    /// Builds the stages from the configuration, refusing by name what it cannot use (never throws for a value); with
    /// <paramref name="contest"/> (the module's skill data, its contest on) the contest on contact runs between
    /// the conversion's pool and its roll.
    /// </summary>
    public static HarmRules Build(EnhancedConfig config, HarmData data, SkillData? contest = null)
    {
        var r = new HarmRules(config, data);
        var read = new List<string>();
        if (!config.IsSandbox)
            r.refusals.Add($"the staged harm runs on the research sandbox only (profile {config.Profile.ProfileId} is not one)");

        T? Value<T>(string key, Func<string, T> get) where T : struct
        {
            read.Add(key);
            try { return get(key); }
            catch (UnresolvedValueException e) { r.refusals.Add(e.Message); }
            catch (KeyNotFoundException e) { r.refusals.Add(e.Message); }
            catch (InvalidOperationException e) { r.refusals.Add(e.Message); }
            catch (FormulaTextException e) { r.refusals.Add(e.Message); }
            return null;
        }
        DiceCurve? Curve(string key)
        {
            read.Add(key);
            try { return DiceCurve.Parse(key, config.Text(key)); }
            catch (UnresolvedValueException e) { r.refusals.Add(e.Message); }
            catch (KeyNotFoundException e) { r.refusals.Add(e.Message); }
            catch (InvalidOperationException e) { r.refusals.Add(e.Message); }
            catch (FormulaTextException e) { r.refusals.Add(e.Message); }
            return null;
        }
        int Difficulty(string key)
        {
            var d = Value(key, config.Number);
            if (d is not { } v) return 0;
            if (v != Math.Floor(v) || v < 2 || v > 10) { r.refusals.Add($"value '{key}' is {v}: a d10's difficulty is a whole number from 2 to 10"); return 0; }
            return (int)v;
        }

        r.DamageCurve = Curve(DamageDiceValue);
        r.DamageDifficulty = Difficulty(DamageDifficultyValue);
        r.SoakCurve = Curve(SoakDiceValue);
        r.SoakDifficulty = Difficulty(SoakDifficultyValue);
        r.PotencePerRank = Value(data.PotencePerRankProfileValue, config.Number) ?? 0;
        r.BashingHalves = Value(data.BashingHalvedProfileValue, config.Flag) ?? false;

        // Health: the profile's curve when it has one (this module cannot read one yet), else the engine-sandbox Health.
        read.Add(data.HealthProfileValue);
        try
        {
            var v = config.Value(data.HealthProfileValue);
            if (v.IsResolved) r.refusals.Add($"value '{data.HealthProfileValue}' now has a value ({v.Json}; gate {v.GateId}), which this module cannot read yet: the engine-sandbox Health stands in only while it is null");
            else r.EngineSandboxHealth = data.HealthStatus == "ENGINE_SANDBOX" && data.HealthMaximum > 0;
        }
        catch (KeyNotFoundException e) { r.refusals.Add(e.Message); }
        if (!r.EngineSandboxHealth && r.refusals.Count == 0)
            r.refusals.Add($"no Health: value '{data.HealthProfileValue}' is null and {data.File} gives no engine-sandbox Health");

        // Each class's addend: its own SANDBOX number, or a profile value (a null one refuses that class only).
        foreach (var (id, c) in data.Classes)
        {
            if (c.Bonus is { } b) { r.bonuses[id] = (b, null); continue; }
            read.Add(c.BonusProfileValue!);
            try { r.bonuses[id] = (config.Number(c.BonusProfileValue!), null); }
            catch (Exception e) when (e is UnresolvedValueException or KeyNotFoundException or InvalidOperationException) { r.bonuses[id] = (0, e.Message); }
        }
        if (contest is not null)
        {
            read.Add(contest.DifficultyProfileValue);
            r.Contest = SkillContest.Build(config, contest, r.refusals);
        }
        r.ValuesRead = [.. read.Distinct(StringComparer.Ordinal)];
        return r;
    }

    /// <summary>A class's addend and the value it came from (the profile's name, or SANDBOX).</summary>
    public (double Bonus, string? Refusal) BonusOf(WeaponClass c) => bonuses.TryGetValue(c.Id, out var b) ? b : (0, $"class '{c.Id}' has no addend");

    /// <summary>
    /// Resolves one contact on a body (the caller has checked it is a body, not protected, not blocked) into
    /// <paramref name="r"/>: the stages above, or <see cref="HarmOutcome.Unresolved"/> with the reason. Health is worked
    /// out, not written: the caller writes it.
    /// <para><paramref name="share"/> is the share of the swing's attack budget the contact is resolved from (the
    /// package's: one non-replenishing budget a swing): 1 for the chain's first body contact — the stages exactly as
    /// before —, the residual share for a later one, which multiplies the potential (Strength's dice and the weapon's addend)
    /// and Potence's automatic successes (floored); the contest, when on, and soak are the contact's own.</para>
    /// </summary>
    public void Resolve(in ContactMade contact, in Striker a, in Struck t, IDeterministicRandom random, HarmResult r, float share = 1f)
    {
        r.Reset();
        r.Chain = contact.Chain;
        r.BudgetIn = share;
        r.Strength = a.Strength;
        r.Weapon = a.Weapon;
        r.PotenceRank = a.PotenceRank;
        r.PotenceLearned = a.PotenceLearned;
        r.Stamina = t.Stamina;
        r.FortitudeRank = t.FortitudeRank;
        r.HealthBefore = r.HealthAfter = t.Health;
        r.HealthMaximum = t.HealthMaximum;
        r.HealthPerWound = Data.HealthPerWound;
        r.BandBefore = r.BandAfter = Data.Band(t.Health, t.HealthMaximum);

        // Every refusal before any die.
        if (!CanResolve) { r.Refusal = string.Join("; ", refusals); return; }
        if (a.Weapon is null) { r.Refusal = $"the weapon {a.Who} strikes with is not known (it has no character)"; return; }
        var cls = Data.ClassOf(a.Weapon);
        if (cls is null) { r.Refusal = $"weapon '{a.Weapon}' has no class in {Data.File} (its 'weapons' table)"; return; }
        r.Class = cls;
        r.Kind = cls.Kind;
        if (a.WorkDice is null) Quality?.Judge(in contact, cls, a.SwingClip, a.Character, r, a.Weapon);   // Q, the factor, the hilt (no die); not an arrow's (no swing)
        var (bonus, bonusRefusal) = BonusOf(cls);
        if (bonusRefusal is not null) { r.Refusal = bonusRefusal; return; }
        r.Bonus = bonus;
        if (Data.SoakFor(t.Species, r.Kind) is not { } soak) { r.Refusal = $"species '{t.Species}' of {t.Who} is not in {Data.File}'s soak table (who soaks what)"; return; }
        if (!t.HasHealth) { r.Refusal = $"{t.Who} has no Health: its sheet has no '{Data.HealthResource}' resource and value '{Data.HealthProfileValue}' is null (gate-17)"; return; }

        // Potential: Strength's dice and the weapon's addend; the impact speed is recorded, not used (gate-08 open).
        r.StrengthDice = a.WorkDice is null ? DamageCurve!.Of(a.Strength) : 0;   // an arrow's is its work's, not the archer's Strength
        r.WorkDice = a.WorkDice ?? 0;
        r.FullPotential = r.StrengthDice + r.WorkDice + bonus;
        r.Potential = share >= 1f ? r.FullPotential : r.FullPotential * share;   // a later contact from the budget left
        // ---- blow quality, one stage — the potential times min(cap, Q^p); no roll, the contact is never missed.
        r.PotentialBeforeQuality = r.Potential;
        bool scaled = r.QualityJudged && r.QualityOn && r.QualityFactor != 1f;
        if (scaled) r.Potential *= r.QualityFactor;
        // ---- end

        // Conversion: the pool made whole; the contest on contact adds to it or takes from it; then rolled.
        if (!scaled)
        {
            r.DamagePool = Dice.Pool(r.Potential, random, Data.PoolStream, r.Draws);
            if (a.WorkDice is null) Contest?.Roll(in a, in t, cls, random, r);   // no contest on an arrow's contact (its aim was the archer's)
            r.DamageSuccesses = Dice.Successes(r.DamagePool, DamageDifficulty, random, Data.DamageStream, r.Draws);
        }
        else QualityRoll(in a, in t, cls, random, r);   // the blow's pool and the Q = 1 pool, one set of dice

        // Automatic successes: Potence's passive, learned.
        r.FullAutoSuccesses = a.PotenceLearned && a.PotenceRank > 0 ? (int)Math.Floor(a.PotenceRank * PotencePerRank) : 0;
        r.AutoSuccesses = share >= 1f ? r.FullAutoSuccesses : (int)Math.Floor(r.FullAutoSuccesses * (double)share);
        int autoAtOne = r.AutoSuccesses;
        if (scaled && Quality!.Data.PotenceScaled) r.AutoSuccesses = (int)Math.Floor(r.AutoSuccesses * (double)r.QualityFactor);   // the rules package decides

        // Soak, type-legal.
        r.StaminaSoaks = soak.Stamina;
        r.FortitudeSoaks = soak.Fortitude && t.FortitudeLearned && t.FortitudeRank > 0
                           && (r.Kind != HarmKind.Aggravated || Data.FortitudeAggravatedPermission);   // the hilt's kind
        r.StaminaDice = r.StaminaSoaks ? SoakCurve!.Of(t.Stamina) : 0;
        r.FortitudeDice = r.FortitudeSoaks ? t.FortitudeRank * Data.FortitudeSoakDicePerRank : 0;
        r.SoakPotential = r.StaminaDice + r.FortitudeDice;
        r.SoakPool = Dice.Pool(r.SoakPotential, random, Data.PoolStream, r.Draws);
        r.SoakSuccesses = Dice.Successes(r.SoakPool, SoakDifficulty, random, Data.SoakStream, r.Draws);
        // ---- the armour over the struck part ("actual locally covering armor"), its own pool (the combat rules'
        // ArmorPool) made whole and rolled after the body's soak; its successes join the soak. Nothing drawn when it gives nothing.
        int bodySoak = r.SoakSuccesses;
        if (t.Armor is { } worn)
        {
            worn.Rules.Soak(worn, ArmorRules.RegionOf(contact.Site), r.Kind, cls.Id, SoakDifficulty, random, Data.PoolStream, Data.SoakStream, r);   // r.Kind (the hilt's)
            r.SoakPool += r.ArmorPool;
            r.SoakSuccesses += r.ArmorSuccesses;
        }
        // ---- end

        // Wounds; a vampire's bashing halved after soak.
        r.WoundsBeforeHalving = Math.Max(0, r.DamageSuccesses + r.AutoSuccesses - r.SoakSuccesses);
        r.Halved = r.Kind == HarmKind.Bashing && BashingHalves && Contains(Data.BashingHalvedFor, t.Species);   // r.Kind (the hilt's)
        r.Wounds = r.Halved ? r.WoundsBeforeHalving / 2 : r.WoundsBeforeHalving;
        if (r.ArmorSuccesses > 0)   // the wounds the armour kept off (what the body's soak alone would have let through, less what came)
        {
            int without = Math.Max(0, r.DamageSuccesses + r.AutoSuccesses - bodySoak);
            r.ArmorKept = (r.Halved ? without / 2 : without) - r.Wounds;
        }

        // Health (the wounds, the stages' injury units, converted by the translator hp_per_wound).
        r.HealthLost = Math.Min(t.Health, (long)r.Wounds * Data.HealthPerWound);
        r.HealthAfter = t.Health - r.HealthLost;
        r.BandAfter = Data.Band(r.HealthAfter, t.HealthMaximum);
        r.Died = t.Health > 0 && r.HealthAfter == 0;
        r.Outcome = r.Wounds > 0 ? HarmOutcome.Applied : HarmOutcome.Resisted;
        // The "resisted" split — the outcome stays Resisted (its consumers and the receipts' vocabulary unchanged); why is a field.
        r.ResistedBy = r.Wounds > 0 ? Platform.Combat.ResistedBy.None
            : r.DamageSuccesses + r.AutoSuccesses == 0 ? Platform.Combat.ResistedBy.NoDamage
            : r.WoundsBeforeHalving == 0 ? Platform.Combat.ResistedBy.Soak
            : Platform.Combat.ResistedBy.Halving;
        // ---- what the same dice at Q = 1 would have dealt against the same soak — low work, when it alone kept the wound off.
        if (r.QualityRolled)
        {
            int before = Math.Max(0, r.QualitySuccessesAtOne + autoAtOne - r.SoakSuccesses);
            r.QualityWoundsAtOne = r.Halved ? before / 2 : before;
            r.QualityWouldWound = r.QualityFactor < 1f && r.Wounds == 0 && r.QualityWoundsAtOne > 0;   // low work: only a blow its quality lessened
            BlowQuality.LowWorkResisted(r);
        }
        // ---- end
    }

    // ---- ----
    /// <summary>
    /// The conversion of a blow its quality scaled — the scaled potential made whole and, with the same unit draw,
    /// the potential at Q = 1 (one draw from the pool stream when either has a fraction); the contest when on (its change worked for
    /// each pool); then <b>one set of d10s</b>, as many as the larger pool: the blow counts the first of them (its own pool), the
    /// Q = 1 pool all it needs — so the dice are the ones the blow would have rolled at its typical quality, and the label
    /// <see cref="Platform.Combat.ResistedBy.LowWork"/> is decided by them, never by a roll of its own. Every die is written down.
    /// </summary>
    void QualityRoll(in Striker a, in Struck t, WeaponClass cls, IDeterministicRandom random, HarmResult r)
    {
        r.QualityRolled = true;
        Pools(r.Potential, r.PotentialBeforeQuality, random, Data.PoolStream, r.Draws, out int pool, out int atOne);
        r.DamagePool = pool;
        if (Contest is { } k)
        {
            k.Roll(in a, in t, cls, random, r);
            atOne = Math.Max(0, atOne + SkillContest.Change(r.ContestNet, atOne, k.Data.AddMost, k.Data.TakeMostShare));
        }
        r.QualityPoolAtOne = atOne;
        r.QualityDiceCounted = r.DamagePool;
        int n = Math.Max(r.DamagePool, atOne), sum = 0, sumAtOne = 0;
        for (int i = 0; i < n; i++)
        {
            long at = random.Position(Data.DamageStream);
            int d = random.NextD10(Data.DamageStream);
            r.Draws.Add(new Draw(Data.DamageStream, at, d, 0, true));
            int v = d >= DamageDifficulty ? 1 : d == 1 ? -1 : 0;
            if (i < r.DamagePool) sum += v;
            if (i < atOne) sumAtOne += v;
        }
        r.DamageSuccesses = Math.Max(0, sum);
        r.QualitySuccessesAtOne = Math.Max(0, sumAtOne);
    }

    /// <summary>Two potentials made whole with one unit draw (the combat rules' neighbouring integer): drawn only when either has a fraction; each 0 for none or less.</summary>
    static void Pools(double dice, double atOne, IDeterministicRandom random, string purpose, List<Draw> draws, out int pool, out int poolAtOne)
    {
        double w = dice > 0 ? Math.Floor(dice) : 0, f = dice > 0 ? dice - w : 0;
        double w1 = atOne > 0 ? Math.Floor(atOne) : 0, f1 = atOne > 0 ? atOne - w1 : 0;
        pool = (int)Math.Min(w, int.MaxValue - 1);
        poolAtOne = (int)Math.Min(w1, int.MaxValue - 1);
        if (f <= 0 && f1 <= 0) return;
        long at = random.Position(purpose);
        double u = random.NextUnitInterval(purpose);
        draws.Add(new Draw(purpose, at, 0, u, false));
        if (u < f) pool++;
        if (u < f1) poolAtOne++;
    }
    // ---- end

    static bool Contains(IReadOnlyList<string> list, string s)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == s) return true;
        return false;
    }
}
