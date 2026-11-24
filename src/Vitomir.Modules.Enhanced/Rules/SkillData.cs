// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>The shape of an Ability's response over its dots: <c>power</c> <c>R^(b/10)</c>, or <c>linear</c> <c>1 + (R−1)·b/10</c>.</summary>
public enum SkillCurve
{
    Power,
    Linear,
}

/// <summary>
/// Abilities in the fight (ours, <c>Data/combat/skill.json</c>, schema <c>VITOMIR_ENHANCED_SKILL</c>, SANDBOX):
/// <i>"Both body and dice"</i> — as two channels, each authored on its own with its own numbers
/// (the rules package's warning: never one Ability factor multiplied into aim, contact, speed and damage at once):
/// <list type="bullet">
/// <item><b>the tempo</b>: the Ability a blow uses (Melee armed, Brawl bare-handed) makes the body's swing, its recovery
/// and its block's rise faster (<see cref="Platform.Combat.CombatTempo"/>), each by its own rate at Ability 10 and the curve
/// over 0–10 (<see cref="TempoAt"/>);</item>
/// <item><b>the contest</b> (a research comparator): on a body contact, φ(Dexterity) plus that Ability against the
/// defender's φ(Dexterity) plus Melee only while it was guarding (no unperformed defence; Dodge never counts until a dodge
/// is performed), rolled at the damage difficulty from its own streams; the net adds to or takes from the damage pool
/// within <see cref="AddMost"/> and <see cref="TakeMostShare"/> (never a gate on the hit).</item>
/// </list>
/// The tempo runs unless the module's switch <see cref="EnhancedModule.TempoOffSwitch"/> is given; the contest only with
/// <see cref="EnhancedModule.ContestSwitch"/>. Read strictly: a wrong or missing number is refused by name.
/// </summary>
public sealed class SkillData
{
    public const string SchemaId = "VITOMIR_ENHANCED_SKILL";

    /// <summary>The Ability dots the curves span (the registry's Abilities are 0–10).</summary>
    public const int MostDots = 10;

    public string File { get; private init; } = "";

    // Which traits.
    public string ArmedAbility { get; private init; } = "";
    public string BareAbility { get; private init; } = "";
    public IReadOnlyList<string> BareClasses { get; private init; } = [];
    public string DodgeAbility { get; private init; } = "";
    public string DexterityTrait { get; private init; } = "";

    // The tempo.
    public SkillCurve Curve { get; private init; }
    public double SwingAt10 { get; private init; }
    public double RecoveryAt10 { get; private init; }
    public double RiseAt10 { get; private init; }

    // The contest.
    public DiceCurve DexterityDice { get; private init; } = null!;
    public string DifficultyProfileValue { get; private init; } = "";
    public int? AddMost { get; private init; }
    public double TakeMostShare { get; private init; }
    public string PoolStream { get; private init; } = "";
    public string AttackStream { get; private init; } = "";
    public string DefenceStream { get; private init; } = "";

    float[] swing = [], recovery = [], rise = [];

    /// <summary>The dodge's numbers (the file's <c>dodge</c>, optional: absent, the platform's human dodge for everyone).</summary>
    public DodgeNumbers? Dodge { get; private init; }

    /// <summary>Whether a weapon class's blows are bare-handed (Brawl's, and no weapon held for the defence).</summary>
    public bool IsBareHanded(string classId)
    {
        for (int i = 0; i < BareClasses.Count; i++) if (BareClasses[i] == classId) return true;
        return false;
    }

    /// <summary>A rate at <paramref name="dots"/> of its Ability (clamped to 0–10) whose value at 10 is <paramref name="at10"/>, by <see cref="Curve"/>.</summary>
    public static double Rate(SkillCurve curve, double at10, int dots)
    {
        int b = Math.Clamp(dots, 0, MostDots);
        if (b == 0) return 1.0;
        if (b == MostDots) return at10;
        return curve == SkillCurve.Linear ? 1.0 + (at10 - 1.0) * b / MostDots : DetMath.Pow((float)at10, b / (float)MostDots);
    }

    /// <summary>The three rates at <paramref name="dots"/> (clamped to 0–10), from the tables worked out when read: allocates nothing.</summary>
    public (float Swing, float Recovery, float Rise) TempoAt(int dots)
    {
        int b = Math.Clamp(dots, 0, MostDots);
        return (swing[b], recovery[b], rise[b]);
    }

    internal static SkillData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"status is '{status}': the Abilities' channels are SANDBOX until the rules package gives them");

        string armed = "", bare = "", dodge = "", dex = "";
        var bareClasses = new List<string>();
        if (c.Obj(root, "abilities") is { } ab)
        {
            armed = c.Str(ab, "armed") ?? "";
            bare = c.Str(ab, "bare_handed") ?? "";
            dodge = c.Str(ab, "dodge") ?? "";
            dex = c.Str(ab, "dexterity") ?? "";
            if (c.Arr(ab, "bare_handed_classes") is { } bc)
                foreach (var v in bc.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.String) { c.Fail("abilities.bare_handed_classes must hold weapon classes' ids"); continue; }
                    bareClasses.Add(v.GetString()!);
                }
        }

        var curve = SkillCurve.Power;
        double sw = 0, rc = 0, ri = 0;
        if (c.Obj(root, "tempo") is { } t)
        {
            switch (c.Str(t, "curve"))
            {
                case "power": curve = SkillCurve.Power; break;
                case "linear": curve = SkillCurve.Linear; break;
                case null: break;
                case var other: c.Fail($"tempo.curve '{other}' is not one this module knows (power, linear)"); break;
            }
            if (c.Obj(t, "at_ability_10") is { } at)
            {
                double Rate(string name)
                {
                    if (!at.TryGetProperty(name, out var v)) { c.Fail($"tempo.at_ability_10.{name} is missing"); return 0; }
                    double r = c.Num(v, $"tempo.at_ability_10.{name}") ?? 0;
                    if (!(r >= 0.25 && r <= 4)) c.Fail($"tempo.at_ability_10.{name} = {r}: a rate at Ability 10 is from 0.25 to 4 (1 is 1.1's own timing)");
                    return r;
                }
                sw = Rate("swing");
                rc = Rate("recovery");
                ri = Rate("rise");
            }
        }

        DiceCurve? dexDice = null;
        string difficulty = "", pool = "", attack = "", defence = "";
        int? addMost = null;
        double takeShare = 0;
        if (c.Obj(root, "contest") is { } k)
        {
            if (c.Str(k, "dexterity_dice") is { } text)
            {
                try { dexDice = DiceCurve.Parse("contest.dexterity_dice", text); }
                catch (FormulaTextException) { c.Fail($"contest.dexterity_dice '{text}' is not a formula this module reads (raw/N, or raw/N for raw<=T; A+C*ln(1+(raw-T)/S) beyond)"); }
            }
            difficulty = c.Str(k, "difficulty_profile_value") ?? "";
            var rule = c.Str(k, "defence") ?? "";
            if (rule.Length > 0 && rule != "dexterity_plus_guard_when_performed") c.Fail($"contest.defence '{rule}' is not one this module knows (dexterity_plus_guard_when_performed)");
            if (k.TryGetProperty("add_most", out var am) && am.ValueKind != JsonValueKind.Null)
            {
                if (am.ValueKind != JsonValueKind.Number || !am.TryGetInt32(out int most) || most < 0) c.Fail("contest.add_most must be a whole number of dice, 0 or more, or null (no cap)");
                else addMost = most;
            }
            else if (!k.TryGetProperty("add_most", out _)) c.Fail("contest.add_most is missing (null for no cap)");
            if (k.TryGetProperty("take_most_share_of_pool", out var ts) && c.Num(ts, "contest.take_most_share_of_pool") is { } share)
            {
                if (!(share >= 0 && share <= 1)) c.Fail($"contest.take_most_share_of_pool = {share}: a share of the pool, 0 to 1");
                takeShare = share;
            }
            else if (!k.TryGetProperty("take_most_share_of_pool", out _)) c.Fail("contest.take_most_share_of_pool is missing");
            if (c.Obj(k, "streams") is { } st)
            {
                pool = c.Str(st, "pool") ?? "";
                attack = c.Str(st, "attack") ?? "";
                defence = c.Str(st, "defence") ?? "";
                if (pool.Length > 0 && (pool == attack || pool == defence || attack == defence))
                    c.Fail("contest.streams: the pool, attack and defence streams must be three purposes (one stream per purpose)");
            }
        }
        DodgeNumbers? dodgeNumbers = c.Obj(root, "dodge", required: false) is { } dg ? DodgeNumbers.Read(dg, c) : null;
        if (problems.Count > before) return null;

        var d = new SkillData
        {
            File = file, ArmedAbility = armed, BareAbility = bare, BareClasses = bareClasses, DodgeAbility = dodge, DexterityTrait = dex,
            Curve = curve, SwingAt10 = sw, RecoveryAt10 = rc, RiseAt10 = ri,
            DexterityDice = dexDice!, DifficultyProfileValue = difficulty, AddMost = addMost, TakeMostShare = takeShare,
            PoolStream = pool, AttackStream = attack, DefenceStream = defence, Dodge = dodgeNumbers,
        };
        d.swing = Table(curve, sw);
        d.recovery = Table(curve, rc);
        d.rise = Table(curve, ri);
        return d;
    }

    static float[] Table(SkillCurve curve, double at10)
    {
        var t = new float[MostDots + 1];
        for (int b = 0; b <= MostDots; b++) t[b] = (float)Rate(curve, at10, b);
        return t;
    }
}

/// <summary>
/// <b>The dodge's numbers</b> (ours, <c>skill.json</c>'s <c>dodge</c>, SANDBOX): a human's dodge (Dexterity 23, no Dodge:
/// <see cref="Distance"/> in <see cref="MoveSeconds"/> after <see cref="ReadySeconds"/>, then <see cref="RecoverSeconds"/>), the
/// <b>Dodge Ability</b>'s rate dividing the start and the recovery (<see cref="AbilityRate"/>: <c>R^(b/10)</c> or linear, as
/// the tempo channel), and <b>Dexterity</b>'s scale dividing the burst's time (<see cref="DexterityScale"/>:
/// <c>(D / mean)^k</c> within its least and most; held to <see cref="MasqueradeMostDexterity"/> for the masquerade's
/// <see cref="Platform.Bodies.DodgeTempo.HumanMove"/>). Each factor in its own channel (the rules package's rule).
/// </summary>
public sealed class DodgeNumbers
{
    public float Distance { get; private init; }
    public float MoveSeconds { get; private init; }
    public float ReadySeconds { get; private init; }
    public float RecoverSeconds { get; private init; }
    public SkillCurve Curve { get; private init; }
    public double RateAt10 { get; private init; }
    public bool DexterityOn { get; private init; }
    public double HumanMean { get; private init; }
    public double Exponent { get; private init; }
    public double Least { get; private init; }
    public double Most { get; private init; }
    public int MasqueradeMostDexterity { get; private init; }

    float[] rates = [];

    /// <summary>The Ability's rate at <paramref name="dots"/> (0–10, clamped): the start and the recovery are divided by it.</summary>
    public float AbilityRate(int dots) => rates[Math.Clamp(dots, 0, SkillData.MostDots)];

    /// <summary>Dexterity's scale at <paramref name="dexterity"/> (effective, raw): the burst's time is divided by it; 1 with <paramref name="on"/> false.</summary>
    public float DexterityScale(int dexterity, bool on = true)
    {
        if (!on || !DexterityOn) return 1f;
        float d = MathF.Max(1f, dexterity);
        float s = DetMath.Pow(d / (float)HumanMean, (float)Exponent);
        return Math.Clamp(s, (float)Least, (float)Most);
    }

    /// <summary>
    /// A body's dodge (the platform's <see cref="Platform.Bodies.DodgeTempo"/>) at Dodge <paramref name="dots"/> and effective
    /// Dexterity <paramref name="dexterity"/>; <paramref name="dexterityOn"/> false: Dexterity's link off (the switch).
    /// </summary>
    public Platform.Bodies.DodgeTempo At(int dots, int dexterity, bool dexterityOn = true)
    {
        float rate = AbilityRate(dots);
        return new Platform.Bodies.DodgeTempo
        {
            Ready = ReadySeconds / rate,
            Recover = RecoverSeconds / rate,
            Distance = Distance,
            Move = MoveSeconds / DexterityScale(dexterity, dexterityOn),
            HumanMove = MoveSeconds / DexterityScale(Math.Min(dexterity, MasqueradeMostDexterity), dexterityOn),
        };
    }

    /// <summary>
    /// These numbers with a human's burst over the distance taking <paramref name="move"/>
    /// seconds and the recovery <paramref name="recover"/> (<c>pace.json</c>'s dodge), all else the same.
    /// </summary>
    public DodgeNumbers WithTimes(float move, float recover) => new()
    {
        Distance = Distance, MoveSeconds = move, ReadySeconds = ReadySeconds, RecoverSeconds = recover, Curve = Curve, RateAt10 = RateAt10,
        DexterityOn = DexterityOn, HumanMean = HumanMean, Exponent = Exponent, Least = Least, Most = Most, MasqueradeMostDexterity = MasqueradeMostDexterity,
        rates = rates,
    };

    internal static DodgeNumbers? Read(JsonElement o, JsonChecks c)
    {
        string status = c.Str(o, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"dodge.status is '{status}': the dodge's numbers are SANDBOX until the rules package gives them");
        double Positive(JsonElement on, string key, string path, double lo, double hi)
        {
            if (!on.TryGetProperty(key, out var v)) { c.Fail($"{path}.{key} is missing"); return lo; }
            double x = c.Num(v, $"{path}.{key}") ?? lo;
            if (!(x >= lo && x <= hi)) c.Fail($"{path}.{key} = {x}: from {lo} to {hi}");
            return x;
        }
        float distance = 0, move = 0, ready = 0, recover = 0;
        if (c.Obj(o, "human") is { } h)
        {
            distance = (float)Positive(h, "distance", "dodge.human", 0.1, 5);
            move = (float)Positive(h, "move_seconds", "dodge.human", 0.05, 2);
            ready = (float)Positive(h, "ready_seconds", "dodge.human", 0, 2);
            recover = (float)Positive(h, "recover_seconds", "dodge.human", 0, 3);
        }
        var curve = SkillCurve.Power;
        double at10 = 1;
        if (c.Obj(o, "ability") is { } a)
        {
            switch (c.Str(a, "curve"))
            {
                case "power": curve = SkillCurve.Power; break;
                case "linear": curve = SkillCurve.Linear; break;
                case null: break;
                case var other: c.Fail($"dodge.ability.curve '{other}' is not one this module knows (power, linear)"); break;
            }
            at10 = Positive(a, "rate_at_ability_10", "dodge.ability", 0.25, 4);
        }
        bool on = true;
        double mean = 23, k = 0.25, least = 1, most = 1;
        int masked = 100;
        if (c.Obj(o, "dexterity") is { } x)
        {
            on = c.Bool(x, "on") ?? true;
            mean = Positive(x, "human_mean", "dodge.dexterity", 1, 1000);
            k = Positive(x, "exponent", "dodge.dexterity", 0, 2);
            least = Positive(x, "least", "dodge.dexterity", 0.1, 1);
            most = Positive(x, "most", "dodge.dexterity", 1, 20);
            masked = (int)Positive(x, "masquerade_most_dexterity", "dodge.dexterity", 1, 1000);
        }
        var n = new DodgeNumbers
        {
            Distance = distance, MoveSeconds = move, ReadySeconds = ready, RecoverSeconds = recover, Curve = curve, RateAt10 = at10,
            DexterityOn = on, HumanMean = mean, Exponent = k, Least = least, Most = most, MasqueradeMostDexterity = masked,
        };
        n.rates = new float[SkillData.MostDots + 1];
        for (int b = 0; b <= SkillData.MostDots; b++) n.rates[b] = (float)SkillData.Rate(curve, at10, b);
        return n;
    }
}
