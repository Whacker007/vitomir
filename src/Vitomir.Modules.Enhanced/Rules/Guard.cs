// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the guard's contest. 1.1 has no block contest, parry or clash (its blows are a reach test and a
// to-hit roll: the combat survey); the rules package fixes the shape
// and leaves every number open, so every number here is SANDBOX, for the rules package to replace.
using System.Text;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The guard's numbers (<c>Data/combat/guard.json</c>, schema <see cref="SchemaId"/>, <b>SANDBOX</b>): the platform's
/// part (the parry window, the guard's arc and speed, the cancel's return: <see cref="Apply"/> gives them to the platform's
/// <see cref="GuardSettings"/>) and the rules' contest (<see cref="GuardRules"/>).
/// </summary>
public sealed class GuardData
{
    public const string SchemaId = "VITOMIR_ENHANCED_GUARD";

    public string File { get; private init; } = "";

    // The traits read.
    public string Strength { get; private init; } = "strength";
    public string Dexterity { get; private init; } = "dexterity";
    public string Melee { get; private init; } = "melee";
    public DiceCurve AttributeDice { get; private init; } = null!;

    // The platform's part.
    public float ParryWindowSeconds { get; private init; }
    public float ParryRepressSeconds { get; private init; }
    public float CancelReturnSeconds { get; private init; }
    public float DodgeLetGoSeconds { get; private init; }
    public float GuardArcDegrees { get; private init; }
    public float GuardMostTwistDegrees { get; private init; }
    public float GuardMostUpDegrees { get; private init; }
    public float GuardMostDownDegrees { get; private init; }
    public float GuardDegreesPerSecond { get; private init; }
    public float ParryDegreesPerSecond { get; private init; }
    public float GuardThreatMetres { get; private init; }
    public float ParryReboundScale { get; private init; }
    public float GuardSideDegrees { get; private init; }
    public float GuardLevelDegrees { get; private init; }
    public float GuardHeightSpanDegrees { get; private init; }

    // The contest.
    public double ForceStrength { get; private init; }
    public double WeaponForceDefault { get; private init; }
    public IReadOnlyDictionary<string, double> WeaponForce { get; private init; } = new Dictionary<string, double>();
    public double HoldStrength { get; private init; }
    public double ShieldFactor { get; private init; }
    public double BladeFactor { get; private init; }
    public double TechniqueWeight { get; private init; }
    public double IncidentalShare { get; private init; }
    public double OverpowerMargin { get; private init; }
    public int OverpowerSharpness { get; private init; }
    public double BudgetLeast { get; private init; }
    public double ParryForceShare { get; private init; }
    public int ParrySharpness { get; private init; }
    public double ClashMargin { get; private init; }
    public int ClashSharpness { get; private init; }
    public double ClashDeflectBelowDegrees { get; private init; }
    public float RiposteSeconds { get; private init; }
    public float BrokenSeconds { get; private init; }
    public string Stream { get; private init; } = "enhanced.guard";

    // ---- the arm solver's and the give's numbers (optional objects "arm_solver" and "give"; absent, the
    // platform's own defaults, GuardSettings): each (key, value) read, given to the platform by Apply. ----
    public IReadOnlyList<(string Key, float Value)> SolverNumbers { get; private init; } = [];

    /// <summary>The keys of "arm_solver" and "give" and the platform's setting each fills, with its least and most.</summary>
    static readonly (string Block, string Key, double Least, double Most, Action<GuardSettings, float> Set)[] SolverKeys =
    [
        ("arm_solver", "hand_metres_per_second", 0, 50, (g, v) => g.SolverHandMetresPerSecond = v),
        ("arm_solver", "parry_hand_metres_per_second", 0, 50, (g, v) => g.SolverParryHandMetresPerSecond = v),
        ("arm_solver", "blade_degrees_per_second", 0, 10000, (g, v) => g.SolverBladeDegreesPerSecond = v),
        ("arm_solver", "parry_blade_degrees_per_second", 0, 10000, (g, v) => g.SolverParryBladeDegreesPerSecond = v),
        ("arm_solver", "twist_most_degrees", 0, 90, (g, v) => g.SolverTwistMostDegrees = v),
        ("arm_solver", "twist_degrees_per_second", 0, 10000, (g, v) => g.SolverTwistDegreesPerSecond = v),
        ("arm_solver", "parry_twist_degrees_per_second", 0, 10000, (g, v) => g.SolverParryTwistDegreesPerSecond = v),
        ("arm_solver", "reach_share", 0.1, 1, (g, v) => g.SolverReachShare = v),
        ("arm_solver", "own_blade_from", 0, 1, (g, v) => g.SolverOwnFrom = v),
        ("arm_solver", "own_blade_to", 0, 1, (g, v) => g.SolverOwnTo = v),
        ("arm_solver", "incoming_blade_from", 0, 1, (g, v) => g.SolverIncomingFrom = v),
        ("arm_solver", "least_cross_degrees", 0, 90, (g, v) => g.SolverLeastCrossDegrees = v),
        ("arm_solver", "clearance_metres", 0, 2, (g, v) => g.SolverClearanceMetres = v),
        ("arm_solver", "hand_forward_metres", -1, 1, (g, v) => g.SolverHandForward = v),
        ("arm_solver", "hand_across_metres", 0, 2, (g, v) => g.SolverHandAcross = v),
        ("arm_solver", "hand_lowest_metres", 0, 3, (g, v) => g.SolverHandLowest = v),
        ("arm_solver", "hand_highest_metres", 0, 3, (g, v) => g.SolverHandHighest = v),
        ("arm_solver", "read_seconds", 0, 2, (g, v) => g.SolverReadSeconds = v),
        ("arm_solver", "return_seconds", 0.001, 5, (g, v) => g.SolverReturnSeconds = v),
        ("arm_solver", "sample_seconds", 0.001, 0.1, (g, v) => g.SolverSampleSeconds = v),
        ("give", "metres_per_newton_second", 0, 1, (g, v) => g.GiveMetresPerNewtonSecond = v),
        ("give", "least_metres", 0, 1, (g, v) => g.GiveLeastMetres = v),
        ("give", "most_metres", 0, 1, (g, v) => g.GiveMostMetres = v),
        ("give", "overpowered_scale", 0, 10, (g, v) => g.GiveOverpoweredScale = v),
        ("give", "spring_hertz", 0.1, 50, (g, v) => g.GiveHertz = v),
    ];

    static List<(string, float)> ReadSolver(JsonElement root, JsonChecks c, List<string> problems)
    {
        var read = new List<(string, float)>();
        foreach (var (block, key, least, most, _) in SolverKeys)
        {
            if (!root.TryGetProperty(block, out var o) || o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(key, out var v)) continue;
            double x = c.Num(v, $"{block}.{key}") ?? 0;
            if (x < least || x > most) { c.Fail($"{block}.{key} {x} is outside {least} to {most}"); continue; }
            read.Add(($"{block}.{key}", (float)x));
        }
        return read;
    }
    // ---- end

    // ---- the guard by skill and the brain's parry timed by its hand (optional objects "skill" and
    // "brain_parry"; absent, the earlier build's guard and the platform's defaults) ----

    /// <summary>The guard's curve by Dexterity and Melee (<c>skill</c>; null: none, every fighter guards as before).</summary>
    public GuardSkillCurve? Skill { get; private init; }

    /// <summary>The stagger when hit (<c>stagger</c>; null: none given, the first module's flinch).</summary>
    public StaggerData? Stagger { get; private init; }

    /// <summary>The brain's parry timing (<c>brain_parry</c>: travel_metres, margin_seconds, arrive_seconds), when given.</summary>
    public (float Travel, float Margin, float Arrive)? BrainParry { get; private init; }

    /// <summary>The duelling brain's pace by the kind of person (<c>brain_pace</c>; null: none given, the earlier build's constant aggression).</summary>
    public BrainPaceData? Pace { get; private init; }

    static (float, float, float)? ReadBrainParry(JsonElement root, JsonChecks c)
    {
        if (!root.TryGetProperty("brain_parry", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        double travel = o.TryGetProperty("travel_metres", out var t) ? c.Num(t, "brain_parry.travel_metres") ?? 0.8 : 0.8;
        double margin = o.TryGetProperty("margin_seconds", out var m) ? c.Num(m, "brain_parry.margin_seconds") ?? 0.03 : 0.03;
        double arrive = o.TryGetProperty("arrive_seconds", out var a) ? c.Num(a, "brain_parry.arrive_seconds") ?? 0.05 : 0.05;
        if (travel < 0 || travel > 3) c.Fail($"brain_parry.travel_metres {travel} is outside 0 to 3");
        if (margin < 0 || margin > 0.5) c.Fail($"brain_parry.margin_seconds {margin} is outside 0 to 0.5");
        if (arrive < 0 || arrive > 0.5) c.Fail($"brain_parry.arrive_seconds {arrive} is outside 0 to 0.5");
        return ((float)travel, (float)margin, (float)arrive);
    }
    // ---- end

    /// <summary>A weapon class's force (its heft in the contest: <see cref="WeaponForceDefault"/> for a class not named).</summary>
    public double ForceOf(string? classId) => classId is not null && WeaponForce.TryGetValue(classId, out var f) ? f : WeaponForceDefault;

    /// <summary>Gives the platform's part to the platform's <see cref="GuardSettings"/>.</summary>
    public void Apply(GuardSettings g)
    {
        g.ParryWindowSeconds = ParryWindowSeconds;
        g.ParryRepressSeconds = ParryRepressSeconds;
        g.CancelReturnSeconds = CancelReturnSeconds;
        g.DodgeLetGoSeconds = DodgeLetGoSeconds;
        g.GuardArcDegrees = GuardArcDegrees;
        g.GuardMostTwistDegrees = GuardMostTwistDegrees;
        g.GuardMostUpDegrees = GuardMostUpDegrees;
        g.GuardMostDownDegrees = GuardMostDownDegrees;
        g.GuardDegreesPerSecond = GuardDegreesPerSecond;
        g.ParryDegreesPerSecond = ParryDegreesPerSecond;
        g.GuardThreatMetres = GuardThreatMetres;
        g.ParryReboundScale = ParryReboundScale;
        g.GuardSideDegrees = GuardSideDegrees;
        g.GuardLevelDegrees = GuardLevelDegrees;
        g.GuardHeightSpanDegrees = GuardHeightSpanDegrees;
        foreach (var (key, value) in SolverNumbers)
            foreach (var k in SolverKeys)
                if ($"{k.Block}.{k.Key}" == key) k.Set(g, value);
        if (BrainParry is { } bp) { g.BrainParryTravelMetres = bp.Travel; g.BrainParryMarginSeconds = bp.Margin; g.BrainParryArriveSeconds = bp.Arrive; }
        if (ShieldRead is { } sr) { g.ShieldReadOverMetres = sr.OverMetres; g.ShieldReadOverShare = sr.OverShare; g.ShieldReadUnderMetres = sr.UnderMetres; }
    }

    // ---- the shield read (guard.json's optional "shield_read"; absent, no read: the earlier build's brain) ----
    public ShieldReadData? ShieldRead { get; private init; }
    // ---- end

    internal static GuardData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "SANDBOX") c.Fail($"status is '{status}': the guard's numbers are SANDBOX until the rules package gives them");
        double N(JsonElement o, string key, double least = 0, double most = double.MaxValue)
        {
            if (!o.TryGetProperty(key, out var v)) { c.Fail($"{key} is missing"); return 0; }
            double x = c.Num(v, key) ?? 0;
            if (x < least || x > most) c.Fail($"{key} {x} is outside {least} to {most}");
            return x;
        }
        int S(JsonElement o, string key)
        {
            double x = N(o, key, 1, 4);
            if (x != Math.Floor(x)) c.Fail($"{key} must be a whole number 1 to 4 (the contest's sharpness: a power by repeated products)");
            return Math.Max(1, (int)x);
        }

        string str = "strength", dex = "dexterity", melee = "melee";
        if (c.Obj(root, "traits") is { } t)
        {
            str = c.Str(t, "strength") ?? str;
            dex = c.Str(t, "dexterity") ?? dex;
            melee = c.Str(t, "melee") ?? melee;
        }
        DiceCurve? dice = null;
        if (c.Str(root, "attribute_dice") is { } dt)
        {
            try { dice = DiceCurve.Parse("attribute_dice", dt); }
            catch (FormatException e) { c.Fail(e.Message); }
        }
        var pf = c.Obj(root, "platform");
        var ct = c.Obj(root, "contest");
        if (pf is not { } p || ct is not { } k || problems.Count > before || dice is null) return null;
        var force = c.Obj(k, "force");
        var hold = c.Obj(k, "hold");
        var over = c.Obj(k, "overpower");
        var parry = c.Obj(k, "parry");
        var clash = c.Obj(k, "clash");
        if (force is not { } fo || hold is not { } ho || over is not { } ov || parry is not { } pa || clash is not { } cl) return null;
        var weapons = new Dictionary<string, double>(StringComparer.Ordinal);
        if (c.Obj(fo, "weapon_force") is { } wf)
            foreach (var w in wf.EnumerateObject())
            {
                if (w.Name is "note" or "default") continue;
                weapons[w.Name] = c.Num(w.Value, $"force.weapon_force.{w.Name}") ?? 0;
            }
        var data = new GuardData
        {
            File = file, Strength = str, Dexterity = dex, Melee = melee, AttributeDice = dice,
            ParryWindowSeconds = (float)N(p, "parry_window_seconds", 0, 2), ParryRepressSeconds = (float)N(p, "parry_repress_seconds", 0, 5),
            CancelReturnSeconds = (float)N(p, "cancel_return_seconds", 0, 2), DodgeLetGoSeconds = (float)N(p, "dodge_let_go_seconds", 0, 1),
            GuardArcDegrees = (float)N(p, "guard_arc_degrees", 0, 180), GuardMostTwistDegrees = (float)N(p, "guard_most_twist_degrees", 0, 90),
            GuardMostUpDegrees = (float)N(p, "guard_most_up_degrees", 0, 90), GuardMostDownDegrees = (float)N(p, "guard_most_down_degrees", 0, 90),
            GuardDegreesPerSecond = (float)N(p, "guard_degrees_per_second", 0), ParryDegreesPerSecond = (float)N(p, "parry_degrees_per_second", 0),
            GuardThreatMetres = (float)N(p, "guard_threat_metres", 0, 20), ParryReboundScale = (float)N(p, "parry_rebound_scale", 0, 10),
            GuardSideDegrees = (float)N(p, "guard_side_degrees", 1, 90), GuardLevelDegrees = (float)N(p, "guard_level_degrees", -90, 90),
            GuardHeightSpanDegrees = (float)N(p, "guard_height_span_degrees", 1, 90),
            ForceStrength = N(fo, "strength_weight"), WeaponForceDefault = fo.TryGetProperty("weapon_force", out var wd) && wd.TryGetProperty("default", out var d0) ? c.Num(d0, "force.weapon_force.default") ?? 1 : 1,
            WeaponForce = weapons,
            HoldStrength = N(ho, "strength_weight"), ShieldFactor = N(ho, "shield_factor"), BladeFactor = N(ho, "blade_factor"),
            TechniqueWeight = N(ho, "technique_weight"), IncidentalShare = N(ho, "incidental_share", 0, 1),
            OverpowerMargin = N(ov, "margin", 1e-3), OverpowerSharpness = S(ov, "sharpness"), BudgetLeast = N(ov, "budget_least", 0, 1),
            ParryForceShare = N(pa, "force_share", 1e-3), ParrySharpness = S(pa, "sharpness"),
            ClashMargin = N(cl, "margin", 1e-3), ClashSharpness = S(cl, "sharpness"), ClashDeflectBelowDegrees = N(cl, "deflect_below_degrees", 0, 90),
            RiposteSeconds = (float)N(k, "riposte_seconds", 0, 5), BrokenSeconds = (float)N(k, "broken_seconds", 0, 5),
            Stream = c.Str(k, "stream") ?? "enhanced.guard",
            SolverNumbers = ReadSolver(root, c, problems),
            Pace = BrainPaceData.Read(root, c),
            Skill = GuardSkillCurve.Read(root, c),
            BrainParry = ReadBrainParry(root, c),
            Stagger = StaggerData.Read(root, c),
            ShieldRead = ShieldReadData.Read(root, c),
        };
        return problems.Count == before ? data : null;
    }
}

/// <summary>One side of the guard's contest: a person's Strength, Dexterity and Melee (their effective values) and the force their blow carries.</summary>
public readonly record struct GuardSide(int Strength, int Dexterity, int Melee, double WeaponForce, float Budget);

/// <summary>What the contest made of a guard met (for the event, the trace and the tests).</summary>
public readonly record struct GuardOutcome(GuardVerdict Verdict, double Force, double Hold, double Technique, double OverpowerChance, double ParryChance,
    double OverpowerDraw, double ParryDraw, float BudgetLeft);

/// <summary>
/// The guard's contest (the enhanced module's; <b>SANDBOX</b>, the rules package's to replace; the rules package:
/// <i>"For direct force contests, developed Strength should be the primary determinant of overpowering a held guard.
/// Dexterity/Melee proficiency, timing, leverage, geometry and weapon familiarity govern technique and eligible redirects.
/// Relative weapon quality supplies only a smaller, bounded factor"</i>). With φ the Attributes' dice (<c>raw/20</c>):
/// <list type="bullet">
/// <item>the blow's <b>force</b> F = budget × (w_s φ(Strength) + the weapon's force) — Strength first, the weapon's heft a small
/// bounded term, scaled by what is left of the swing's attack budget (never refilled);</item>
/// <item>the guard's <b>hold</b> H = w_h φ(Strength) × (the shield's factor, or the blade's) + w_t (φ(Dexterity) + Melee) —
/// Strength first, technique second; an incidental interception (no guard raised) holds by its Strength alone times
/// the incidental share, with no technique;</item>
/// <item><b>overpowered</b> with chance F^n / (F^n + (m H)^n) (m the margin a blow must have over the hold for even odds, n the
/// sharpness): the blade goes on with budget × max(least, 1 − m H / F);</item>
/// <item>else, met in the parry window (<see cref="GuardMeet.Parry"/>), <b>parried</b> with chance T^k / (T^k + (q F)^k), T the
/// defender's technique φ(Dexterity) + Melee and q the share of the force a timed parry must turn; else <b>held</b>;</item>
/// <item>a <b>clash</b> (one paired transaction): side a dominates with chance P_a = F_a^n / (F_a^n + (m F_b)^n), side
/// b with P_b likewise, from one draw u (u &lt; P_a: a; u ≥ 1 − P_b: b); otherwise both stop, or both deflect when the blades met
/// at under the deflection angle (the mean of the two contacts' angles, from their speeds along and across the normal).</item>
/// </list>
/// Deterministic: draws from the rules' own stream (<see cref="GuardData.Stream"/>) in the contacts' order, the overpower draw
/// always and the parry draw only for a contact in the window; a clash's one draw for the pair.
/// </summary>
public sealed class GuardRules(GuardData data, bool techniqueOn = true, bool meleeOn = true)
{
    public GuardData Data { get; } = data;

    // ---- the rules package's (by design to the Melee ablations: the guard's technique, a fourth lane of Melee) ----
    /// <summary>
    /// Whether the guard's technique term counts (the module's switch <c>guard-technique-off</c>: no technique —
    /// a guard holds by its Strength alone, w_h φ(Strength) × its factor, and a parry has no technique to turn a blade, so it holds),
    /// and whether Melee counts in it (<c>guard-melee-off</c>: technique φ(Dexterity) alone — Melee out of every guard verdict, the
    /// rules package's lanes isolated). Both on by default: the earlier build's contest.
    /// </summary>
    public bool TechniqueOn { get; } = techniqueOn;
    public bool MeleeOn { get; } = meleeOn;
    // ---- end

    double Phi(int raw) => Data.AttributeDice.Of(Math.Max(0, raw));

    /// <summary>The blow's force (Strength first, the weapon's heft second, by the budget left).</summary>
    public double Force(in GuardSide a) => a.Budget * (Data.ForceStrength * Phi(a.Strength) + a.WeaponForce);

    /// <summary>The defender's technique (φ(Dexterity) + Melee).</summary>
    public double Technique(in GuardSide d) => !TechniqueOn ? 0 : Phi(d.Dexterity) + (MeleeOn ? Math.Max(0, d.Melee) : 0);   // the switches

    /// <summary>The guard's hold: Strength by the shield's or the blade's factor, plus technique; incidental, Strength alone by the share.</summary>
    public double Hold(in GuardSide d, ContactMet met, GuardMeet meet)
    {
        double factor = met == ContactMet.Shield ? Data.ShieldFactor : Data.BladeFactor;
        double strength = Data.HoldStrength * Phi(d.Strength) * factor;
        return meet == GuardMeet.Incidental ? strength * Data.IncidentalShare : strength + Data.TechniqueWeight * Technique(in d);
    }

    /// <summary>x^n / (x^n + y^n), n by repeated products (no Math.Pow: the same bits everywhere).</summary>
    public static double Odds(double x, double y, int n)
    {
        double xn = 1, yn = 1;
        for (int i = 0; i < n; i++) { xn *= Math.Max(0, x); yn *= Math.Max(0, y); }
        return xn + yn <= 0 ? 0.5 : xn / (xn + yn);
    }

    /// <summary>A guard met (not a clash): overpowered, parried or held.</summary>
    public GuardOutcome Judge(in GuardSide attacker, in GuardSide defender, ContactMet met, GuardMeet meet, IDeterministicRandom random)
    {
        double f = Force(in attacker), h = Hold(in defender, met, meet), t = Technique(in defender);
        double pOver = Odds(f, Data.OverpowerMargin * h, Data.OverpowerSharpness);
        double u = random.NextUnitInterval(Data.Stream);
        bool parryWindow = meet == GuardMeet.Parry && met == ContactMet.Weapon;
        double pParry = parryWindow ? Odds(t, Data.ParryForceShare * f, Data.ParrySharpness) : 0;
        double v = parryWindow ? random.NextUnitInterval(Data.Stream) : double.NaN;
        if (u < pOver)
        {
            double left = f > 0 ? Math.Max(Data.BudgetLeast, 1 - Data.OverpowerMargin * h / f) : Data.BudgetLeast;
            return new GuardOutcome(GuardVerdict.Overpowered, f, h, t, pOver, pParry, u, v, (float)(attacker.Budget * Math.Min(1, left)));
        }
        return new GuardOutcome(parryWindow && v < pParry ? GuardVerdict.Parried : GuardVerdict.Held, f, h, t, pOver, pParry, u, v, 0f);
    }

    /// <summary>A clash's pair (a the pair's lower entity, its contact first): the outcome of each side, one draw.</summary>
    public (GuardOutcome A, GuardOutcome B) Clash(in GuardSide a, in GuardSide b, double sineA, double sineB, IDeterministicRandom random)
    {
        double fa = Force(in a), fb = Force(in b);
        double pa = Odds(fa, Data.ClashMargin * fb, Data.ClashSharpness), pb = Odds(fb, Data.ClashMargin * fa, Data.ClashSharpness);
        double u = random.NextUnitInterval(Data.Stream);
        double deflectSine = DetMath.Sin((float)(Data.ClashDeflectBelowDegrees * DetMath.DegToRad));
        float LeftOf(in GuardSide s, double f, double other) => (float)(s.Budget * Math.Min(1, f > 0 ? Math.Max(Data.BudgetLeast, 1 - Data.ClashMargin * other / f) : Data.BudgetLeast));
        GuardVerdict va, vb;
        float la = 0f, lb = 0f;
        if (u < pa) { va = GuardVerdict.ClashDominates; vb = GuardVerdict.Clashed; la = LeftOf(in a, fa, fb); }
        else if (u >= 1 - pb) { vb = GuardVerdict.ClashDominates; va = GuardVerdict.Clashed; lb = LeftOf(in b, fb, fa); }
        else va = vb = 0.5 * (sineA + sineB) < deflectSine ? GuardVerdict.ClashDeflected : GuardVerdict.Clashed;
        return (new GuardOutcome(va, fa, fb, 0, pa, 0, u, double.NaN, la), new GuardOutcome(vb, fb, fa, 0, pb, 0, u, double.NaN, lb));
    }
}

/// <summary>
/// The guard's verdict in the world (<see cref="Phase.Resolve"/>, order <see cref="GuardServices.GuardOrder"/>, before
/// the harm): every <see cref="ContactMade"/> of this tick with a guard (<see cref="ContactMade.Guard"/>), in their order,
/// judged by <see cref="GuardRules"/> from the two sheets and published as <see cref="GuardResolved"/> — the platform's
/// follow-through reads it. A clash's two contacts (published together by contact) are judged together, once. The budget a
/// blow had is its swing's (the whole for the chain's first contact); an overpowering or dominating blade's budget left is
/// written after the harm has ended the chain at the guard (<see cref="GuardBudgetSystem"/>). Each verdict gives one trace line
/// when the program asks. Allocates nothing on a tick without guard contacts.
/// </summary>
public sealed class GuardSystem(GuardRules rules) : ISystem
{
    IDeterministicRandom? random;
    HarmWeapons? weapons;

    public void Run(World world)
    {
        if (!world.TryService<EventQueue<ContactMade>>(out var contacts)) return;
        var list = contacts.ThisTick;
        if (list.Count == 0) return;
        var sheets = world.Service<ActorSheets>();
        var verdicts = world.Events<GuardResolved>();
        world.TryService<CombatTrace>(out var trace);
        random ??= world.TryService<IDeterministicRandom>(out var dice) ? dice : new WorldRandom(world.Random);
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        var harm = world.TryService<HarmRules>(out var hr) ? hr : null;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.Guard == GuardMeet.None || c.Target.IsNone) continue;
            if (!sheets.Has(c.Attacker) || !sheets.Has(c.Target)) continue;
            var a = Side(world, sheets, harm, c.Attacker, c.Swing, c.Chain);
            if (c.Guard == GuardMeet.Clash)
            {
                // The pair's other contact: the target's, against this attacker, this tick (contact publishes them together).
                int j = -1;
                for (int k = i + 1; k < list.Count && j < 0; k++)
                    if (list[k].Guard == GuardMeet.Clash && list[k].Attacker == c.Target && list[k].Target == c.Attacker) j = k;
                if (j < 0) continue;   // its pair was judged with an earlier contact
                var o = list[j];
                var b = Side(world, sheets, harm, o.Attacker, o.Swing, o.Chain);
                // The pair judged with its lower entity as side a, whichever contact came first (independent of order).
                if (o.Attacker.Index < c.Attacker.Index)
                {
                    var (rb2, ra2) = rules.Clash(in b, in a, Sine(in o), Sine(in c), random);
                    Publish(world, verdicts, trace, in c, in ra2, a.Budget);
                    Publish(world, verdicts, trace, in o, in rb2, b.Budget);
                }
                else
                {
                    var (ra, rb) = rules.Clash(in a, in b, Sine(in c), Sine(in o), random);
                    Publish(world, verdicts, trace, in c, in ra, a.Budget);
                    Publish(world, verdicts, trace, in o, in rb, b.Budget);
                }
                continue;
            }
            var d = Side(world, sheets, harm, c.Target, 0, 0);
            var r = rules.Judge(in a, in d, c.Met, c.Guard, random);
            Publish(world, verdicts, trace, in c, in r, a.Budget);
        }
    }

    /// <summary>The sine of the angle the blade met at (its speed along the normal over its whole speed), 1 when unknown.</summary>
    static double Sine(in ContactMade c) => c.PointSpeed > 1e-4f ? Math.Clamp(c.ImpactSpeed / c.PointSpeed, 0f, 1f) : 1.0;

    GuardSide Side(World world, ActorSheets sheets, HarmRules? harm, EntityId e, int swing, int chain)
    {
        var d = rules.Data;
        string? cls = harm?.Data.ClassOf(weapons!.Of(world, e))?.Id;
        float budget = 1f;
        if (chain > 0 && world.TryStore<SwingBudget>(out var budgets))
        {
            ref var b = ref budgets.Find(e);
            budget = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref b) && b.Swing == swing ? Math.Max(0f, b.Left) : 0f;
        }
        return new GuardSide(sheets.Effective(e, d.Strength), sheets.Effective(e, d.Dexterity), sheets.Effective(e, d.Melee), d.ForceOf(cls), budget);
    }

    void Publish(World world, EventQueue<GuardResolved> verdicts, CombatTrace? trace, in ContactMade c, in GuardOutcome r, float budget)
    {
        var d = rules.Data;
        float riposte = r.Verdict == GuardVerdict.Parried ? d.RiposteSeconds : 0f;
        float broken = r.Verdict == GuardVerdict.Overpowered ? d.BrokenSeconds : 0f;
        verdicts.Publish(new GuardResolved(c.ContactId, c.Attacker, c.Target, world.Tick, c.Met, c.Guard, r.Verdict, (float)r.Force, (float)r.Hold,
            (float)r.Technique, (float)r.OverpowerChance, (float)r.ParryChance, r.OverpowerDraw, r.ParryDraw, budget, r.BudgetLeft, riposte, broken));
        if (trace is { Enabled: true }) trace.Write(Line(d, in c, in r, budget, riposte, broken));
    }

    /// <summary>The trace's line for a guard's verdict (the formula, every input and output, the draws).</summary>
    public static string Line(GuardData d, in ContactMade c, in GuardOutcome r, float budget, float riposte, float broken)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("schema", "VITOMIR_GUARD_TRACE");
            w.WriteNumber("format_version", 1);
            w.WriteString("request_id", $"contact:{c.ContactId}/guard");
            w.WriteNumber("contact_id", c.ContactId);
            w.WriteNumber("tick", c.Tick);
            w.WriteString("formula", c.Guard == GuardMeet.Clash ? "guard.clash.v1" : "guard.contest.v1");
            w.WriteString("attacker", c.Attacker.ToString());
            w.WriteString("defender", c.Target.ToString());
            w.WriteString("met", c.Met.ToString());
            w.WriteString("guard", c.Guard.ToString());
            w.WriteNumber("chain", c.Chain);
            w.WriteNumber("budget_before", budget);
            w.WriteNumber("force", r.Force);
            w.WriteNumber(c.Guard == GuardMeet.Clash ? "other_force" : "hold", r.Hold);
            w.WriteNumber("technique", r.Technique);
            w.WriteNumber("overpower_chance", r.OverpowerChance);
            w.WriteNumber("parry_chance", r.ParryChance);
            w.WriteNumber("overpower_draw", r.OverpowerDraw);
            if (!double.IsNaN(r.ParryDraw)) w.WriteNumber("parry_draw", r.ParryDraw);
            w.WriteString("stream", d.Stream);
            w.WriteString("verdict", r.Verdict.ToString());
            w.WriteNumber("budget_left", r.BudgetLeft);
            w.WriteNumber("riposte_seconds", riposte);
            w.WriteNumber("broken_seconds", broken);
            w.WriteString("status", "SANDBOX");
            w.WriteString("source", d.File);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// An overpowering (or a clash's dominating) blade's budget left, written after the harm (which ends a chain at every
/// shield or blade it meets) so its next body contact is resolved from it (<see cref="SwingBudget"/>; never more than it had).
/// </summary>
public sealed class GuardBudgetSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<EventQueue<GuardResolved>>(out var q) || q.ThisTick.Count == 0 || !world.TryStore<SwingBudget>(out var budgets)) return;
        var list = q.ThisTick;
        var contacts = world.Events<ContactMade>().ThisTick;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v.Verdict is not (GuardVerdict.Overpowered or GuardVerdict.ClashDominates) || !world.Alive(v.Attacker)) continue;
            int swing = 0;
            for (int k = 0; k < contacts.Count; k++) if (contacts[k].ContactId == v.ContactId) { swing = contacts[k].Swing; break; }
            float left = Math.Min(v.BudgetLeft, v.BudgetBefore);
            ref var b = ref budgets.Find(v.Attacker);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref b)) budgets.Set(v.Attacker, new SwingBudget { Swing = swing, Left = left });
            else { b.Swing = swing; b.Left = left; }
        }
    }
}

/// <summary>The guard's rules registered.</summary>
public static class GuardServices
{
    /// <summary>The verdict in <see cref="Phase.Resolve"/> before the harm (100); the budget after it.</summary>
    public const int GuardOrder = 95, BudgetOrder = 110;

    /// <summary>
    /// Registers the <see cref="GuardRules"/> service, the event <see cref="GuardResolved"/> (unless the platform did), the
    /// systems <c>Resolve/95 enhanced.guard</c> and <c>Resolve/110 enhanced.guard-budget</c>, and gives the platform's part of
    /// the data to its <see cref="GuardSettings"/>, telling it that the rules decide.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, GuardRules rules)
    {
        b.Service(rules);
        if (!b.Peek.TryService<EventQueue<GuardResolved>>(out _)) b.Event<GuardResolved>();
        if (b.Peek.TryService<GuardSettings>(out var g))
        {
            rules.Data.Apply(g);
            g.Enabled = true;
            g.RulesDecide = true;
        }
        b.System(Phase.Resolve, GuardOrder, "enhanced.guard", new GuardSystem(rules));
        b.System(Phase.Resolve, BudgetOrder, "enhanced.guard-budget", new GuardBudgetSystem());
        return b;
    }
}

// ---- the guard by skill ("I still want to avoid a stamina pool kind of system. I was
// going to say it could be a function of an actor's stats and how capable they are at the various kinds of combat. So a high melee
// ability person would block more often") ----

/// <summary>
/// The guard's curve by skill (<c>guard.json</c>'s <c>skill</c>, <b>ENGINE_SANDBOX</b> until the rules package's
/// the rules package gives it): a fighter's <b>technique</b> T = φ(Dexterity) × <see cref="DexterityWeight"/> + Melee (Brawl bare-handed) — the
/// guard contest's own T (<see cref="GuardRules.Technique"/>) — read through piecewise-linear tables at <see cref="Points"/> (held at
/// their ends), each <b>divided by its value at the reference sheet</b> (<see cref="ReferenceTechnique"/>: the combat rules' trained guard, Dexterity
/// 26 and Melee 2, T 3.3), so that sheet keeps the earlier build's guard exactly. What it gives is the platform's <see cref="GuardSkill"/>.
/// </summary>
public sealed class GuardSkillCurve
{
    public double DexterityWeight { get; private init; } = 1;
    public int ReferenceDexterity { get; private init; } = 26;
    public int ReferenceMelee { get; private init; } = 2;
    public double[] Points { get; private init; } = [];
    public double[] Hand { get; private init; } = [];
    public double[] ReadSeconds { get; private init; } = [];
    public double[] Guard { get; private init; } = [];
    public double[] Parry { get; private init; } = [];
    public double[] ReactSeconds { get; private init; } = [];
    public double[] ErrorSeconds { get; private init; } = [];

    /// <summary>
    /// The least Melee (Brawl bare-handed) at which a fighter's brain raises a blade or its fists to
    /// block or parry (<c>skill.blade_guard_least</c>); below it the fighter is untrained (<see cref="GuardSkill.Untrained"/>) and only a
    /// shield guards. 0 (absent): no gate, the earlier build's.
    /// </summary>
    public int BladeGuardLeast { get; private init; }

    /// <summary>
    /// "Even in KCD the amount of block responses is pretty low": the brain's guard and parry factors
    /// times these (<c>skill.response_scale</c>; 1 absent): the curve's shape kept, the chances lowered across the board.
    /// </summary>
    public double GuardScale { get; private init; } = 1;
    public double ParryScale { get; private init; } = 1;

    /// <summary>Whether a fighter with <paramref name="melee"/> (its Melee, or Brawl bare-handed) is trained enough to guard with a blade.</summary>
    public bool Trained(int melee) => melee >= BladeGuardLeast;

    /// <summary>The reference sheet's technique (φ(26) × the weight + 2 with the shipped data: 3.3).</summary>
    public double ReferenceTechnique(DiceCurve phi) => phi.Of(ReferenceDexterity) * DexterityWeight + ReferenceMelee;

    /// <summary>A fighter's technique from its effective Dexterity (raw) and Melee (or Brawl).</summary>
    public double Technique(DiceCurve phi, int dexterity, int melee) => phi.Of(Math.Max(0, dexterity)) * DexterityWeight + Math.Max(0, melee);

    /// <summary>A table read at <paramref name="t"/> (piecewise linear, held at its ends).</summary>
    public double At(double[] values, double t)
    {
        var p = Points;
        if (t <= p[0]) return values[0];
        for (int i = 1; i < p.Length; i++)
            if (t <= p[i]) return values[i - 1] + (values[i] - values[i - 1]) * (t - p[i - 1]) / (p[i] - p[i - 1]);
        return values[^1];
    }

    /// <summary>
    /// The platform's guard skill at technique <paramref name="t"/>: each factor its table at <paramref name="t"/> over its table at
    /// the reference (so the reference is 1 exactly), the hand times <paramref name="celerity"/> (Celerity's attack factor while it is
    /// live: the hand speeds as the swing does); the error in seconds as it is.
    /// </summary>
    public GuardSkill At(double t, double reference, float celerity = 1f)
    {
        float F(double[] v)
        {
            double r = At(v, reference), x = At(v, t);
            return r > 0 && x > 0 ? (float)(x / r) : 1f;
        }
        return new GuardSkill
        {
            Hand = F(Hand) * (celerity > 0f ? celerity : 1f), Read = F(ReadSeconds), Guard = F(Guard) * (float)GuardScale, Parry = F(Parry) * (float)ParryScale,
            React = F(ReactSeconds), Error = (float)Math.Max(0, At(ErrorSeconds, t)),
        };
    }

    internal static GuardSkillCurve? Read(JsonElement root, JsonChecks c)
    {
        if (!root.TryGetProperty("skill", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        double[]? Table(string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var a) || a.ValueKind != JsonValueKind.Array) { c.Fail($"skill.{key} is missing (an array)"); return null; }
            var list = new List<double>();
            foreach (var v in a.EnumerateArray())
            {
                double x = c.Num(v, $"skill.{key}") ?? double.NaN;
                if (!(x >= least && x <= most)) c.Fail($"skill.{key} has {x}, outside {least} to {most}");
                list.Add(x);
            }
            return [.. list];
        }
        var points = Table("points", 0, 1000);
        var hand = Table("hand", 0.05, 10);
        var read = Table("read_seconds", 0.001, 2);
        var guard = Table("guard", 0, 10);
        var parry = Table("parry", 0, 10);
        var react = Table("react_seconds", 0.001, 2);
        var error = Table("error_seconds", 0, 1);
        if (points is null || hand is null || read is null || guard is null || parry is null || react is null || error is null) return null;
        if (points.Length < 2) { c.Fail("skill.points needs at least two points"); return null; }
        for (int i = 1; i < points.Length; i++)
            if (!(points[i] > points[i - 1])) { c.Fail("skill.points must rise"); return null; }
        foreach (var (name, t) in new[] { ("hand", hand), ("read_seconds", read), ("guard", guard), ("parry", parry), ("react_seconds", react), ("error_seconds", error) })
            if (t.Length != points.Length) { c.Fail($"skill.{name} has {t.Length} values for {points.Length} points"); return null; }
        int refDex = 26, refMelee = 2;
        if (o.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            refDex = (int)(r.TryGetProperty("dexterity", out var d) ? c.Num(d, "skill.reference.dexterity") ?? 26 : 26);
            refMelee = (int)(r.TryGetProperty("melee", out var m) ? c.Num(m, "skill.reference.melee") ?? 2 : 2);
        }
        double weight = o.TryGetProperty("dexterity_weight", out var w) ? c.Num(w, "skill.dexterity_weight") ?? 1 : 1;
        if (weight < 0 || weight > 10) c.Fail($"skill.dexterity_weight {weight} is outside 0 to 10");
        int bladeLeast = 0;
        if (o.TryGetProperty("blade_guard_least", out var bl))
        {
            double x = c.Num(bl, "skill.blade_guard_least") ?? 0;
            if (!(x >= 0 && x <= 10) || x != Math.Floor(x)) c.Fail($"skill.blade_guard_least {x} is not a whole number from 0 to 10");
            else bladeLeast = (int)x;
        }
        double guardScale = 1, parryScale = 1;
        if (o.TryGetProperty("response_scale", out var rs) && rs.ValueKind == JsonValueKind.Object)
        {
            guardScale = rs.TryGetProperty("guard", out var gs) ? c.Num(gs, "skill.response_scale.guard") ?? 1 : 1;
            parryScale = rs.TryGetProperty("parry", out var ps) ? c.Num(ps, "skill.response_scale.parry") ?? 1 : 1;
            if (!(guardScale > 0 && guardScale <= 2)) c.Fail($"skill.response_scale.guard {guardScale} is outside 0 to 2");
            if (!(parryScale > 0 && parryScale <= 2)) c.Fail($"skill.response_scale.parry {parryScale} is outside 0 to 2");
        }
        return new GuardSkillCurve
        {
            GuardScale = guardScale, ParryScale = parryScale,
            BladeGuardLeast = bladeLeast,
            DexterityWeight = weight, ReferenceDexterity = refDex, ReferenceMelee = refMelee, Points = points, Hand = hand, ReadSeconds = read,
            Guard = guard, Parry = parry, ReactSeconds = react, ErrorSeconds = error,
        };
    }
}

/// <summary>
/// The guard by skill (<see cref="Phase.Act"/>, after the tempo and the dodge's numbers): each tick every fighter
/// with a sheet is given the platform's <see cref="GuardSkill"/> from its effective Dexterity and its effective Melee with a weapon in
/// hand, Brawl bare-handed (as the tempo channel picks them: <see cref="SkillData"/>), by <see cref="GuardSkillCurve"/>, times
/// Celerity's attack factor while it is live (the hand). Written only when it changes; a fighter with no sheet keeps none (the earlier build's
/// guard). Allocates nothing per tick.
/// </summary>
public sealed class GuardSkillSystem(GuardSkillCurve curve, DiceCurve phi, SkillData skill, HarmData harm, CelerityEffect? celerity = null, bool bladeGate = true,
    ShieldReadData? shieldRead = null) : ISystem   // the shield read by Melee
{
    int armed = -1, bare = -1, dexterity = -1, fightersKnown;
    HarmWeapons? weapons;
    readonly double reference = curve.ReferenceTechnique(phi);

    public void Run(World world)
    {
        if (fightersKnown == 0)
        {
            fightersKnown = world.TryStore<Platform.Control.Combatant>(out _) ? 1 : 2;   // a world with no fighting: nothing to do
        }
        if (fightersKnown != 1 || !world.TryStore<GuardSkill>(out var skills)) return;
        var sheets = world.Service<ActorSheets>();
        if (armed < 0) { armed = sheets.Layout.Require(skill.ArmedAbility); bare = sheets.Layout.Require(skill.BareAbility); dexterity = sheets.Layout.Require(skill.DexterityTrait); }
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        var ents = world.Store<Platform.Control.Combatant>().Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!sheets.Has(e)) continue;
            bool bareHanded = harm.ClassOf(weapons.Of(world, e)) is not { } cls || skill.IsBareHanded(cls.Id);
            int melee = sheets.Effective(e, bareHanded ? bare : armed);
            double t = curve.Technique(phi, sheets.Effective(e, dexterity), melee);
            var want = curve.At(t, reference, celerity?.Attack(sheets, e, world.Tick) ?? 1f);
            want.Untrained = bladeGate && !curve.Trained(melee);   // unless blade-guard-everyone
            if (shieldRead is not null) want.ShieldRead = shieldRead.ChanceAt(melee);   // unless shield-read-off
            ref var have = ref skills.Find(e);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref have)) world.Set(e, want);
            else if (!have.Same(in want)) have = want;
        }
    }

    /// <summary>The system's place: <c>Act/92</c>, after the tempo (90) and the dodge's numbers (91), before the attacks (100).</summary>
    public const int Order = 92;

    /// <summary>Registers the platform's <see cref="GuardSkill"/> component and <c>Act/92 enhanced.guard-skill</c>.</summary>
    public static WorldBuilder Register(WorldBuilder b, GuardSkillCurve curve, DiceCurve phi, SkillData skill, HarmData harm, CelerityEffect? celerity, bool bladeGate = true,
        ShieldReadData? shieldRead = null)
    {
        GuardSkills.Register(b);
        b.System(Phase.Act, Order, "enhanced.guard-skill", new GuardSkillSystem(curve, phi, skill, harm, celerity, bladeGate, shieldRead));
        return b;
    }
}
// ---- end

// ---- the shield read (skill should tell against a shielded guard) ----

/// <summary>
/// The shield read (<c>guard.json</c>'s <c>shield_read</c>, <b>ENGINE_SANDBOX</b> until the rules package gives it): the
/// chance that a duelling brain, beginning a blow at a foe who carries a shield, reads the shield and aims the blow where it is not
/// (the platform's <see cref="GuardSkill.ShieldRead"/>, the brain's <c>ReadShield</c>) — by the fighter's Melee (Brawl bare-handed),
/// straight lines between the points, held at the ends — and the geometry's three numbers (<see cref="GuardSettings"/>).
/// </summary>
public sealed class ShieldReadData
{
    public double[] Melee { get; private init; } = [];
    public double[] Chance { get; private init; } = [];
    public float OverMetres { get; private init; } = 0.15f;
    public float OverShare { get; private init; } = 0.5f;
    public float UnderMetres { get; private init; } = 0.25f;

    /// <summary>The chance at <paramref name="melee"/> (Melee, or Brawl bare-handed): the table read straight between its points, held at its ends.</summary>
    public float ChanceAt(int melee)
    {
        var p = Melee;
        if (p.Length == 0) return 0f;
        double t = melee;
        if (t <= p[0]) return (float)Chance[0];
        for (int i = 1; i < p.Length; i++)
            if (t <= p[i]) return (float)(Chance[i - 1] + (Chance[i] - Chance[i - 1]) * (t - p[i - 1]) / (p[i] - p[i - 1]));
        return (float)Chance[^1];
    }

    internal static ShieldReadData? Read(JsonElement root, JsonChecks c)
    {
        if (!root.TryGetProperty("shield_read", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        double[]? Table(string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var a) || a.ValueKind != JsonValueKind.Array) { c.Fail($"shield_read.{key} is missing (an array)"); return null; }
            var list = new List<double>();
            foreach (var v in a.EnumerateArray())
            {
                double x = c.Num(v, $"shield_read.{key}") ?? double.NaN;
                if (!(x >= least && x <= most)) c.Fail($"shield_read.{key} has {x}, outside {least} to {most}");
                list.Add(x);
            }
            return [.. list];
        }
        float Num(string key, float fallback, double least, double most)
        {
            if (!o.TryGetProperty(key, out var v)) return fallback;
            double x = c.Num(v, $"shield_read.{key}") ?? fallback;
            if (!(x >= least && x <= most)) c.Fail($"shield_read.{key} {x} is outside {least} to {most}");
            return (float)x;
        }
        var melee = Table("melee", 0, 100);
        var chance = Table("chance", 0, 1);
        if (melee is null || chance is null) return null;
        if (melee.Length < 1 || melee.Length != chance.Length) { c.Fail($"shield_read has {melee.Length} melee points and {chance.Length} chances"); return null; }
        for (int i = 1; i < melee.Length; i++)
            if (!(melee[i] > melee[i - 1])) { c.Fail("shield_read.melee must rise"); return null; }
        return new ShieldReadData
        {
            Melee = melee, Chance = chance,
            OverMetres = Num("over_metres", 0.15f, 0, 2), OverShare = Num("over_share", 0.5f, 0, 1), UnderMetres = Num("under_metres", 0.25f, 0, 2),
        };
    }
}
// ---- end

// ---- the duelling brain's pace by the kind of person ----

/// <summary>
/// The brain's pace by the kind of person (<c>guard.json</c>'s <c>brain_pace</c>, <b>ENGINE_SANDBOX</b>): the kinds'
/// pauses (s, least and most) and the longest run of blows before one; which kind a person is — its sheet's template when
/// <see cref="Templates"/> names it, else a vampire's sheet <see cref="Vampire"/>, else trained (<see cref="GuardSkillCurve.Trained"/>:
/// Melee, Brawl bare-handed, at least <c>skill.blade_guard_least</c>) <see cref="Trained"/>, else <see cref="Untrained"/>.
/// </summary>
public sealed class BrainPaceData
{
    public int RunMost { get; private init; } = 2;
    public IReadOnlyDictionary<string, (float Least, float Most)> Kinds { get; private init; } = new Dictionary<string, (float, float)>();
    public IReadOnlyDictionary<string, string> Templates { get; private init; } = new Dictionary<string, string>();
    public string Vampire { get; private init; } = "";
    public string Trained { get; private init; } = "";
    public string Untrained { get; private init; } = "";

    /// <summary>The riposte chance's curve — points of Melee (Brawl bare-handed) and the chance at each (straight lines between, held at the ends); empty: every opening.</summary>
    public double[] RiposteMelee { get; private init; } = [];
    public double[] RiposteChance { get; private init; } = [];

    /// <summary>The riposte chance at <paramref name="melee"/>.</summary>
    public float RiposteAt(int melee)
    {
        var p = RiposteMelee;
        if (p.Length == 0) return 1f;
        if (melee <= p[0]) return (float)RiposteChance[0];
        for (int i = 1; i < p.Length; i++)
            if (melee <= p[i]) return (float)(RiposteChance[i - 1] + (RiposteChance[i] - RiposteChance[i - 1]) * (melee - p[i - 1]) / (p[i] - p[i - 1]));
        return (float)RiposteChance[^1];
    }

    /// <summary>The kind of person a sheet of <paramref name="template"/> and <paramref name="species"/> is, trained or not.</summary>
    public string KindOf(string template, string species, bool trained) =>
        Templates.TryGetValue(template, out var k) ? k : species == "vampire" ? Vampire : trained ? Trained : Untrained;

    /// <summary>The platform's pace for kind <paramref name="kind"/> (none for a kind not given).</summary>
    public DuelPace PaceOf(string kind, int melee = 0) =>
        Kinds.TryGetValue(kind, out var p) ? new DuelPace { PauseLeast = p.Least, PauseMost = p.Most, RunMost = RunMost, RiposteChance = RiposteAt(melee) } : default;

    internal static BrainPaceData? Read(JsonElement root, JsonChecks c)
    {
        if (!root.TryGetProperty("brain_pace", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        int run = (int)(o.TryGetProperty("run_most", out var r) ? c.Num(r, "brain_pace.run_most") ?? 2 : 2);
        if (run < 1 || run > 10) c.Fail($"brain_pace.run_most {run} is outside 1 to 10");
        var kinds = new Dictionary<string, (float, float)>();
        if (o.TryGetProperty("kinds", out var ks) && ks.ValueKind == JsonValueKind.Object)
            foreach (var k in ks.EnumerateObject())
            {
                if (k.Value.ValueKind != JsonValueKind.Object || !k.Value.TryGetProperty("pause_seconds", out var ps) || ps.ValueKind != JsonValueKind.Array
                    || ps.GetArrayLength() != 2) { c.Fail($"brain_pace.kinds.{k.Name}.pause_seconds is missing (least and most)"); continue; }
                double least = c.Num(ps[0], $"brain_pace.kinds.{k.Name}.pause_seconds") ?? -1, most = c.Num(ps[1], $"brain_pace.kinds.{k.Name}.pause_seconds") ?? -1;
                if (!(least >= 0 && most >= least && most <= 60)) { c.Fail($"brain_pace.kinds.{k.Name}.pause_seconds [{least}, {most}] must rise from 0 to at most 60"); continue; }
                kinds[k.Name] = ((float)least, (float)most);
            }
        else c.Fail("brain_pace.kinds is missing (an object of kinds)");
        var templates = new Dictionary<string, string>();
        if (o.TryGetProperty("templates", out var ts) && ts.ValueKind == JsonValueKind.Object)
            foreach (var t in ts.EnumerateObject())
            {
                if (t.Value.ValueKind != JsonValueKind.String || !kinds.ContainsKey(t.Value.GetString()!)) { c.Fail($"brain_pace.templates.{t.Name} names no kind"); continue; }
                templates[t.Name] = t.Value.GetString()!;
            }
        string Kind(string key)
        {
            var v = c.Str(o, key) ?? "";
            if (!kinds.ContainsKey(v)) c.Fail($"brain_pace.{key} '{v}' names no kind");
            return v;
        }
        // The riposte chance by Melee (optional; absent, every opening).
        double[] rm = [], rc = [];
        if (o.TryGetProperty("riposte", out var rp) && rp.ValueKind == JsonValueKind.Object)
        {
            double[] Arr(string key, double least, double most)
            {
                if (!rp.TryGetProperty(key, out var a) || a.ValueKind != JsonValueKind.Array) { c.Fail($"brain_pace.riposte.{key} is missing (an array)"); return []; }
                var list = new List<double>();
                foreach (var v in a.EnumerateArray())
                {
                    double x = c.Num(v, $"brain_pace.riposte.{key}") ?? double.NaN;
                    if (!(x >= least && x <= most)) c.Fail($"brain_pace.riposte.{key} has {x}, outside {least} to {most}");
                    list.Add(x);
                }
                return [.. list];
            }
            rm = Arr("melee", 0, 10);
            rc = Arr("chance", 0, 1);
            if (rm.Length == 0 || rm.Length != rc.Length) c.Fail("brain_pace.riposte needs as many chances as Melee points, at least one");
            for (int i = 1; i < rm.Length; i++) if (!(rm[i] > rm[i - 1])) c.Fail("brain_pace.riposte.melee must rise");
        }
        return new BrainPaceData { RunMost = run, Kinds = kinds, Templates = templates, Vampire = Kind("vampire"), Trained = Kind("trained"), Untrained = Kind("untrained"),
            RiposteMelee = rm, RiposteChance = rc };
    }
}

/// <summary>
/// The brain's pace by the kind of person (<see cref="Phase.Act"/>, after the guard by skill): each tick every fighter
/// with a sheet is given the platform's <see cref="DuelPace"/> of its kind (<see cref="BrainPaceData.KindOf"/>), written only when it
/// changes. Not registered with the module's switch <c>pace-off</c> (the earlier build's constant aggression). Allocates nothing per tick.
/// </summary>
public sealed class DuelPaceSystem(BrainPaceData pace, int bladeGuardLeast, SkillData skill, HarmData harm) : ISystem
{
    int armed = -1, bare = -1, fightersKnown;
    HarmWeapons? weapons;

    public void Run(World world)
    {
        if (fightersKnown == 0) fightersKnown = world.TryStore<Platform.Control.Combatant>(out _) ? 1 : 2;
        if (fightersKnown != 1 || !world.TryStore<DuelPace>(out var paces)) return;
        var sheets = world.Service<ActorSheets>();
        if (armed < 0) { armed = sheets.Layout.Require(skill.ArmedAbility); bare = sheets.Layout.Require(skill.BareAbility); }
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        var ents = world.Store<Platform.Control.Combatant>().Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!sheets.Has(e)) continue;
            bool bareHanded = harm.ClassOf(weapons.Of(world, e)) is not { } cls || skill.IsBareHanded(cls.Id);
            int melee = sheets.Effective(e, bareHanded ? bare : armed);
            bool trained = melee >= bladeGuardLeast;
            var want = pace.PaceOf(pace.KindOf(sheets.Template(e), sheets.Species(e), trained), melee);   // its riposte chance by its Melee
            ref var have = ref paces.Find(e);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref have)) world.Set(e, want);
            else if (have.PauseLeast != want.PauseLeast || have.PauseMost != want.PauseMost || have.RunMost != want.RunMost || have.RiposteChance != want.RiposteChance) have = want;
        }
    }

    /// <summary>The system's place: <c>Act/93</c>, after the guard by skill (92), before the attacks (100).</summary>
    public const int Order = 93;

    /// <summary>Registers the platform's <see cref="DuelPace"/> component and <c>Act/93 enhanced.brain-pace</c>.</summary>
    public static WorldBuilder Register(WorldBuilder b, BrainPaceData pace, int bladeGuardLeast, SkillData skill, HarmData harm)
    {
        DuelPaces.Register(b);
        b.System(Phase.Act, Order, "enhanced.brain-pace", new DuelPaceSystem(pace, bladeGuardLeast, skill, harm));
        return b;
    }
}
// ---- end

// ---- the stagger when hit ----

/// <summary>
/// The stagger when hit (<c>guard.json</c>'s <c>stagger</c>, <b>ENGINE_SANDBOX</b>; <i>"I think we
/// need to bring the stagger when hit back mechanic as well"</i>): given to the platform's <see cref="HurtRule"/> by <see cref="Apply"/>,
/// which turns it on (the switch <c>stagger-oneone</c> leaves the first module's flinch).
/// </summary>
public sealed class StaggerData
{
    public (string Key, float Value)[] Numbers { get; private init; } = [];
    public bool Overpowered { get; private init; } = true;
    /// <summary>Whether the stagger plays 1.1's hurt clip (<c>hurt</c> "whole" or "short"; "none": the push alone).</summary>
    public bool HurtClip { get; private init; } = true;

    /// <summary>Whether the hurt plays whole (<c>hurt</c> "whole", the default: the stagger lasts 1.1's clip) or cut at the stagger's time ("short").</summary>
    public bool HurtWhole { get; private init; } = true;

    static readonly (string Key, double Least, double Most, Action<HurtRule, float> Set)[] Keys =
    [
        ("least_seconds", 0, 3, (h, v) => h.StaggerLeastSeconds = v),
        ("seconds_per_newton_second", 0, 1, (h, v) => h.StaggerSecondsPerImpulse = v),
        ("most_seconds", 0, 3, (h, v) => h.StaggerMostSeconds = v),
        ("impulse_default", 0, 200, (h, v) => h.StaggerImpulseDefault = v),
        ("guard_seconds", 0, 10, (h, v) => h.StaggerGuardSeconds = v),
        ("fade_seconds", 0, 2, (h, v) => h.StaggerFadeSeconds = v),
        ("push_radians_per_newton_second", 0, 1, (h, v) => h.PushRadiansPerImpulse = v),
        ("push_most_radians", 0, 1.5, (h, v) => h.PushMostRadians = v),
        ("push_peak_seconds", 0.001, 2, (h, v) => h.PushPeakSeconds = v),
        ("push_settle_seconds", 0.01, 5, (h, v) => h.PushSettleSeconds = v),
        ("push_chest_metres", 0, 3, (h, v) => h.PushChestMetres = v),
    ];

    /// <summary>Turns the stagger on and gives its numbers to the platform.</summary>
    public void Apply(HurtRule rule)
    {
        rule.Stagger = true;
        rule.StaggerOverpowered = Overpowered;
        rule.StaggerHurtClip = HurtClip;
        rule.StaggerHurtWhole = HurtWhole;
        foreach (var (key, value) in Numbers)
            foreach (var k in Keys)
                if (k.Key == key) k.Set(rule, value);
    }

    internal static StaggerData? Read(JsonElement root, JsonChecks c)
    {
        if (!root.TryGetProperty("stagger", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        var read = new List<(string, float)>();
        foreach (var (key, least, most, _) in Keys)
        {
            if (!o.TryGetProperty(key, out var v)) continue;
            double x = c.Num(v, $"stagger.{key}") ?? 0;
            if (x < least || x > most) { c.Fail($"stagger.{key} {x} is outside {least} to {most}"); continue; }
            read.Add((key, (float)x));
        }
        bool over = !o.TryGetProperty("overpowered", out var ov) || ov.ValueKind != JsonValueKind.False;
        // "hurt": "whole" (1.1's clip played whole, the default), "short" (cut at the stagger's time) or "none" (the push alone).
        string hurt = o.TryGetProperty("hurt", out var hv) && hv.ValueKind == JsonValueKind.String ? hv.GetString()! : "whole";
        if (hurt is not ("whole" or "short" or "none")) c.Fail($"stagger.hurt is '{hurt}'; it is whole, short or none");
        if (o.TryGetProperty("hurt_clip", out _)) c.Fail("stagger.hurt_clip is retired; give stagger.hurt (whole, short or none)");
        return new StaggerData { Numbers = [.. read], Overpowered = over, HurtClip = hurt != "none", HurtWhole = hurt == "whole" };
    }
}
// ---- end
