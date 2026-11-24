// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>How a power is used: always on and free (Potence, Fortitude), or cast, paid in Blood for a time (Celerity).</summary>
public enum PowerKind
{
    Passive,
    Cast,
}

/// <summary>A cast power's numbers at one rank: its Blood cost (1.1's units), how long it lasts and its recast (s).</summary>
public readonly record struct PowerRank(int Rank, int Cost, double Seconds, double Recast);

/// <summary>
/// A cast power's factor by rank (Celerity's movement and attack), with its status and source. An earlier build (added at
/// the end): the exponents that split the factors (Celerity's actions) into a body tempo and a flurry
/// (<see cref="CelerityEffect"/>; 1 when the curve names none: all tempo, as before).
/// </summary>
public sealed record PowerCurve(string Name, string Status, string Source, IReadOnlyList<double> Move, IReadOnlyList<double> Attack,
    double AttackExponent = 1.0, double MoveExponent = 1.0)
{
    /// <summary>The move factor at <paramref name="rank"/> (1 for a rank the curve does not hold).</summary>
    public double MoveAt(int rank) => rank >= 1 && rank <= Move.Count ? Move[rank - 1] : 1.0;

    /// <summary>The attack factor at <paramref name="rank"/> (1 for a rank the curve does not hold).</summary>
    public double AttackAt(int rank) => rank >= 1 && rank <= Attack.Count ? Attack[rank - 1] : 1.0;
}

/// <summary>
/// A reading of a power's amount (a temporary Attribute's): 1.1's amount is in 1.1's attribute points (0 to 100), ours are
/// raw points of 0 to 1000, so the data says how to read it — <c>raw</c> (1.1's points as raw points, scale 1) or
/// <c>proportional</c> (the same share of the scale, 1000 / 100) — each with its status and source.
/// </summary>
public sealed record AmountReading(string Name, string Status, string Source, double Scale);

/// <summary>
/// One power of <c>rules/powers</c>: its id, label, kind, options, and for a cast power its ranks, effect and curves. its
/// effect's kind (<see cref="PowerData.EffectCelerity"/>, <see cref="PowerData.EffectTempAttribute"/>, <see cref="PowerData.EffectHeal"/>);
/// for a temporary Attribute the trait it raises; for it and a heal the amount by rank (1.1's units) and the readings of that
/// amount (<see cref="AmountReading"/>: a scale on it), the default first.
/// </summary>
public sealed record PowerDef(string Id, string Label, PowerKind Kind, IReadOnlyList<string> Options, string Effect,
    IReadOnlyList<PowerRank> Ranks, IReadOnlyDictionary<string, PowerCurve> Curves, string DefaultCurve, string Source,
    string Trait = "", IReadOnlyList<int>? Amounts = null, IReadOnlyDictionary<string, AmountReading>? Readings = null, string DefaultReading = "",
    string? SlotLabel = null)
{
    /// <summary>The short name a quick slot and the wheel show (the data's <c>slot_label</c>, at most <see cref="PowerData.SlotLabelMax"/> characters; its label when it gives none).</summary>
    public string Short => SlotLabel ?? Label;

    /// <summary>The amount in 1.1's units at a rank (0 for a rank it does not hold, or an effect with none).</summary>
    public int RawAmountAt(int rank) => Amounts is { } a && rank >= 1 && rank <= a.Count ? a[rank - 1] : 0;

    /// <summary>
    /// The amount at a rank under a reading (the default's when <paramref name="reading"/> is null): 1.1's amount times the
    /// reading's scale, rounded to the nearest whole number (away from zero). An effect with no readings (a heal) is 1.1's amount.
    /// </summary>
    public int AmountAt(int rank, string? reading = null)
    {
        int raw = RawAmountAt(rank);
        if (Readings is not { Count: > 0 } rs || !rs.TryGetValue(reading ?? DefaultReading, out var r)) return raw;
        return (int)Math.Round(raw * r.Scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>The option a use of it is for: its first (the package's contract name, <c>celerity.active</c>).</summary>
    public string Option => Options[0];

    /// <summary>The numbers at a rank, or null when the rank has no semantics here (6 to 10, refused by name: gate-29).</summary>
    public PowerRank? At(int rank)
    {
        for (int i = 0; i < Ranks.Count; i++) if (Ranks[i].Rank == rank) return Ranks[i];   // no enumerator: called each tick
        return null;
    }
}

/// <summary>
/// The powers the module knows (ours, <c>Data/rules/powers.json</c>, schema <see cref="SchemaId"/>): read strictly, every
/// number with its status and source, every reason named. By design: Potence and Fortitude passive, Celerity cast
/// as 1.1 casts it at (1 + rank)x by default with 1.1's curve to compare, instant. The order of the file is
/// the catalogue's (<see cref="PowerCatalogue"/>) and the order a person's cast powers fill its quick slots.
/// </summary>
public sealed class PowerData
{
    public const string SchemaId = "VITOMIR_ENHANCED_POWERS";
    public const string Asset = "rules/powers";

    /// <summary>The curve names a cast power's data must hold (Celerity's two: the default and 1.1's).</summary>
    public const string OnePlusRank = "one-plus-rank", OneOne = "oneone";

    /// <summary>
    /// The effects a cast power may have: Celerity's (its curves; <see cref="CelerityEffect"/>), a temporary rise of an
    /// Attribute for a time (Blood Strength, Dexterity, Stamina: a modifier on the effective value), and an instant heal of
    /// Health (Blood Healing).
    /// </summary>
    public const string EffectCelerity = "celerity", EffectTempAttribute = "temp_attribute", EffectHeal = "heal";

    /// <summary>The most characters a slot's label may have (the stand-in HUD's slot shows about ten).</summary>
    public const int SlotLabelMax = 10;

    /// <summary>The amount readings a temporary Attribute's data must hold: 1.1's points as raw points, and the same share of the scale.</summary>
    public const string ReadingRaw = "raw", ReadingProportional = "proportional";

    public string File { get; private init; } = "";
    public IReadOnlyList<PowerDef> Powers { get; private init; } = [];

    /// <summary>A power by id, or null.</summary>
    public PowerDef? Find(string id)
    {
        foreach (var p in Powers) if (p.Id == id) return p;
        return null;
    }

    /// <summary>Whether some power of the module lists this option.</summary>
    public bool KnowsOption(string option)
    {
        foreach (var p in Powers) if (p.Options.Contains(option)) return true;
        return false;
    }

    /// <summary>Reads the file; null (and the reasons in <paramref name="problems"/>) when anything is wrong.</summary>
    public static PowerData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"status is '{status}': the powers' numbers are SANDBOX until the rules package answers");
        var list = new List<PowerDef>();
        if (c.Arr(root, "powers") is { } arr)
            foreach (var x in arr.EnumerateArray())
            {
                string id = c.Str(x, "power") ?? "";
                string label = c.Str(x, "label") ?? "";
                string? slotLabel = c.Str(x, "slot_label", required: false);                // a short name for a slot
                if (slotLabel is not null && (slotLabel.Length == 0 || slotLabel.Length > SlotLabelMax)) c.Fail($"power '{id}': slot_label '{slotLabel}' is not 1 to {SlotLabelMax} characters");
                string kindText = c.Str(x, "kind") ?? "";
                var kind = kindText switch { "passive" => PowerKind.Passive, "cast" => PowerKind.Cast, _ => (PowerKind?)null };
                if (kind is null) c.Fail($"power '{id}': kind '{kindText}' is neither passive nor cast");
                if (!Sheets.TraitLayout.IsId(id)) c.Fail($"power '{id}' is not a lower-case identifier");
                if (list.Exists(p => p.Id == id)) c.Fail($"power '{id}' appears twice");
                var options = new List<string>();
                if (c.Arr(x, "options") is { } os)
                    foreach (var o in os.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String && Sheets.TraitLayout.IsId(o.GetString())) options.Add(o.GetString()!);
                        else c.Fail($"power '{id}': an option is not a lower-case identifier");
                if (options.Count == 0) c.Fail($"power '{id}' lists no option (a rank never brings one: the rules read an option)");
                var ranks = new List<PowerRank>();
                var curves = new Dictionary<string, PowerCurve>(StringComparer.Ordinal);
                string effect = "", defaultCurve = "";
                string trait = "", defaultReading = "";                                     // a temporary Attribute's and a heal's
                List<int>? amounts = null;
                Dictionary<string, AmountReading>? readings = null;
                string source = c.Str(x, "source", required: kind != PowerKind.Cast) ?? "";
                if (kind == PowerKind.Cast)
                {
                    effect = c.Str(x, "effect") ?? "";
                    if (effect is not (EffectCelerity or EffectTempAttribute or EffectHeal)) c.Fail($"power '{id}': effect '{effect}' is not one this module has (celerity, temp_attribute, heal)");
                    foreach (var key in new[] { "cost_source", "seconds_source", "recast_source", "beyond" })
                        _ = c.Str(x, key);    // every number's source, required
                    if (c.Str(x, "ranks_status") is { } rs && rs != "SANDBOX") c.Fail($"power '{id}': ranks_status '{rs}' is not SANDBOX");
                    if (c.Arr(x, "ranks") is { } ra)
                        foreach (var r in ra.EnumerateArray())
                        {
                            int? rank = c.Int(r, "rank"), cost = c.Int(r, "cost");
                            double? seconds = r.TryGetProperty("seconds", out var sv) ? c.Num(sv, $"power '{id}' seconds") : null;
                            double? recast = r.TryGetProperty("recast", out var rv) ? c.Num(rv, $"power '{id}' recast") : null;
                            if (seconds is null || recast is null) { c.Fail($"power '{id}': a rank needs seconds and recast"); continue; }
                            if (rank is not { } k || cost is not { } co) continue;
                            if (k != ranks.Count + 1) c.Fail($"power '{id}': rank {k} is out of order (ranks run 1, 2, ... with none missing)");
                            if (co < 0) c.Fail($"power '{id}' rank {k}: cost {co} is below nothing");
                            if ((effect == EffectHeal ? seconds != 0 : seconds <= 0) || recast < 0)
                                c.Fail(effect == EffectHeal ? $"power '{id}' rank {k}: a heal is instant (seconds 0, not {seconds}) and its recast {recast} not below nothing"
                                    : $"power '{id}' rank {k}: seconds {seconds} must be above nothing and recast {recast} not below");
                            ranks.Add(new PowerRank(k, co, seconds.Value, recast.Value));
                        }
                    if (ranks.Count == 0) c.Fail($"power '{id}' is cast and has no ranks");
                    if (effect != EffectCelerity) amounts = ReadAmounts(c, x, id, effect, ranks.Count, out trait, out readings, out defaultReading);
                    else if (c.Obj(x, "curves") is { } cv)
                    {
                        defaultCurve = c.Str(cv, "default") ?? "";
                        foreach (var p in cv.EnumerateObject())
                        {
                            if (p.Name == "default") continue;
                            string cs = c.Str(p.Value, "status") ?? "";
                            if (cs != "SANDBOX") c.Fail($"power '{id}' curve '{p.Name}': status '{cs}' is not SANDBOX");
                            string src = c.Str(p.Value, "source") ?? "";
                            var move = Factors(c, p.Value, "move", $"power '{id}' curve '{p.Name}'");
                            var attack = Factors(c, p.Value, "attack", $"power '{id}' curve '{p.Name}'");
                            if (move.Count != ranks.Count || attack.Count != ranks.Count)
                                c.Fail($"power '{id}' curve '{p.Name}': {move.Count} move and {attack.Count} attack factors for {ranks.Count} ranks");
                            curves[p.Name] = new PowerCurve(p.Name, cs, src, move, attack, Exponent(c, p.Value, "attack_exponent", $"power '{id}' curve '{p.Name}'"), Exponent(c, p.Value, "move_exponent", $"power '{id}' curve '{p.Name}'"));
                        }
                        foreach (var need in new[] { OnePlusRank, OneOne })
                            if (!curves.ContainsKey(need)) c.Fail($"power '{id}': curve '{need}' is missing (and 1.1's, to compare)");
                        if (!curves.ContainsKey(defaultCurve)) c.Fail($"power '{id}': the default curve '{defaultCurve}' is none of its curves");
                    }
                }
                if (kind is { } kd) list.Add(new PowerDef(id, label, kd, options, effect, ranks, curves, defaultCurve, source, trait, amounts, readings, defaultReading, slotLabel));
            }
        if (list.Count == 0) c.Fail("no powers");
        if (problems.Count > before) return null;
        return new PowerData { File = file, Powers = list };
    }

    /// <summary>
    /// A temporary Attribute's or a heal's amounts by rank (each rank's <c>amount</c>, a whole number above nothing, in 1.1's
    /// units, with <c>amount_source</c>), and for a temporary Attribute the trait it raises and the readings of its amount
    /// (<see cref="ReadingRaw"/> and <see cref="ReadingProportional"/>, each SANDBOX with a scale above nothing and a source; the
    /// default one of them). A heal names no trait and has no readings: its amount is Health.
    /// </summary>
    static List<int> ReadAmounts(JsonChecks c, JsonElement x, string id, string effect, int rankCount, out string trait,
        out Dictionary<string, AmountReading>? readings, out string defaultReading)
    {
        var amounts = new List<int>();
        if (c.Arr(x, "ranks", required: false) is { } ra)
            foreach (var r in ra.EnumerateArray())
                if (c.Int(r, "amount") is { } a)
                {
                    if (a <= 0) c.Fail($"power '{id}' rank {amounts.Count + 1}: amount {a} must be above nothing");
                    amounts.Add(a);
                }
        if (amounts.Count != rankCount) c.Fail($"power '{id}': {amounts.Count} amounts for {rankCount} ranks");
        _ = c.Str(x, "amount_source");
        trait = ""; readings = null; defaultReading = "";
        if (effect == EffectHeal)
        {
            if (c.Has(x, "trait") || c.Has(x, "readings")) c.Fail($"power '{id}': a heal raises no trait and has no readings (its amount is Health)");
            return amounts;
        }
        trait = c.Str(x, "trait") ?? "";
        if (!Sheets.TraitLayout.IsId(trait)) c.Fail($"power '{id}': trait '{trait}' is not a lower-case identifier");
        readings = new Dictionary<string, AmountReading>(StringComparer.Ordinal);
        if (c.Obj(x, "readings") is { } rd)
        {
            defaultReading = c.Str(rd, "default") ?? "";
            foreach (var p in rd.EnumerateObject())
            {
                if (p.Name == "default") continue;
                string st = c.Str(p.Value, "status") ?? "";
                if (st != "SANDBOX") c.Fail($"power '{id}' reading '{p.Name}': status '{st}' is not SANDBOX");
                string src = c.Str(p.Value, "source") ?? "";
                double scale = p.Value.TryGetProperty("scale", out var sv) ? c.Num(sv, $"power '{id}' reading '{p.Name}' scale") ?? 0 : 0;
                if (scale <= 0) c.Fail($"power '{id}' reading '{p.Name}': scale {scale} must be above nothing");
                readings[p.Name] = new AmountReading(p.Name, st, src, scale);
            }
            foreach (var need in new[] { ReadingRaw, ReadingProportional })
                if (!readings.ContainsKey(need)) c.Fail($"power '{id}': reading '{need}' is missing (1.1's points as raw points, and the same share of the scale)");
            if (!readings.ContainsKey(defaultReading)) c.Fail($"power '{id}': the default reading '{defaultReading}' is none of its readings");
        }
        return amounts;
    }

    static List<double> Factors(JsonChecks c, JsonElement o, string key, string what)
    {
        var list = new List<double>();
        if (c.Arr(o, key) is { } a)
            foreach (var v in a.EnumerateArray())
                if (c.Num(v, $"{what} {key}") is { } d)
                {
                    if (d < 1.0) c.Fail($"{what} {key}: factor {d} is below 1 (a power never slows its user)");
                    list.Add(d);
                }
        return list;
    }

    /// <summary>A curve's optional exponent (1 when absent: all tempo), in (0, 1], with its source required.</summary>
    static double Exponent(JsonChecks c, JsonElement o, string key, string what)
    {
        if (!o.TryGetProperty(key, out var v)) return 1.0;
        _ = c.Str(o, key + "_source");
        if (c.Num(v, $"{what} {key}") is not { } d) return 1.0;
        if (!(d > 0 && d <= 1.0)) c.Fail($"{what} {key}: {d} is not in (0, 1] (the tempo is the actions to that power: 1 all tempo, below 1 the rest a flurry)");
        return d;
    }
}

/// <summary>
/// Blood's numbers (ours, <c>Data/rules/blood.json</c>, schema <see cref="SchemaId"/>, ENGINE_SANDBOX): the resource's
/// id, who has it (the species), the purchased capacity (<c>blood_pool</c>, 0 to 1000 in 1.1's units, 100 when a sheet does
/// not list it: 1.1's Christof), the maximum's rule (the purchase 1:1 while gate-19 is null), a new person full, and the
/// throughput (the rules package: capacity apart from throughput): the most spent in one action and in any one second (0: no
/// limit, as 1.1 has none).
/// </summary>
public sealed class BloodData
{
    public const string SchemaId = "VITOMIR_ENHANCED_BLOOD";
    public const string Asset = "rules/blood";

    public string File { get; private init; } = "";
    public string Resource { get; private init; } = "blood";
    public IReadOnlyList<string> Species { get; private init; } = [];
    public string PurchaseId { get; private init; } = "blood_pool";
    public int PurchaseMin { get; private init; }
    public int PurchaseMax { get; private init; }
    public int PurchaseDefault { get; private init; }
    public string PurchaseSource { get; private init; } = "";
    public long PerActionLimit { get; private init; }

    /// <summary>The most spent in any one second (0: none); the D1 resource's per-cycle limit, its cycle <see cref="CycleTicks"/>.</summary>
    public long PerSecondLimit { get; private init; }
    public const int CycleTicks = Platform.Core.World.TicksPerSecond;

    /// <summary>Whether a person of this species has Blood to spend.</summary>
    public bool HasBlood(string species) => Species.Contains(species);

    /// <summary>The maximum a purchase gives (ENGINE_SANDBOX: 1:1).</summary>
    public long MaximumOf(int purchased) => purchased;

    public static BloodData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        if (c.Str(root, "status") is { } s && s != "ENGINE_SANDBOX") c.Fail($"status is '{s}': Blood's numbers are ENGINE_SANDBOX while the profile's blood curves are null");
        string resource = c.Str(root, "resource") ?? "";
        if (!Sheets.TraitLayout.IsId(resource)) c.Fail($"resource '{resource}' is not a lower-case identifier");
        var species = new List<string>();
        if (c.Arr(root, "species") is { } sp)
            foreach (var x in sp.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) species.Add(x.GetString()!); else c.Fail("species must hold names");
        if (species.Count == 0) c.Fail("species names nobody: who has Blood?");
        string id = "", src = "";
        int min = 0, max = 0, dflt = 0;
        if (c.Obj(root, "purchase") is { } p)
        {
            id = c.Str(p, "id") ?? "";
            min = c.Int(p, "minimum") ?? 0; max = c.Int(p, "maximum") ?? 0; dflt = c.Int(p, "default") ?? 0;
            src = c.Str(p, "source") ?? "";
            if (c.Str(p, "status") is { } ps && ps != "ENGINE_SANDBOX") c.Fail($"purchase: status '{ps}' is not ENGINE_SANDBOX");
            if (!Sheets.TraitLayout.IsId(id)) c.Fail($"purchase id '{id}' is not a lower-case identifier");
            if (min < 0 || max < min || dflt < min || dflt > max) c.Fail($"purchase: {min}..{max} with default {dflt} is no domain");
        }
        if (c.Obj(root, "maximum") is { } m && c.Str(m, "rule") is { } rule && rule != "purchased")
            c.Fail($"maximum: rule '{rule}' is not one this module has (purchased: 1:1 while gate-19 is null)");
        if (c.Obj(root, "start") is { } st && c.Str(st, "rule") is { } sr && sr != "full")
            c.Fail($"start: rule '{sr}' is not one this module has (full)");
        long perAction = 0, perSecond = 0;
        if (c.Obj(root, "spend_limits") is { } l)
        {
            perAction = c.Int(l, "per_action") ?? 0; perSecond = c.Int(l, "per_second") ?? 0;
            if (perAction < 0 || perSecond < 0) c.Fail("spend_limits: a limit below nothing");
        }
        if (problems.Count > before) return null;
        return new BloodData
        {
            File = file, Resource = resource, Species = species, PurchaseId = id, PurchaseMin = min, PurchaseMax = max,
            PurchaseDefault = dflt, PurchaseSource = src, PerActionLimit = perAction, PerSecondLimit = perSecond,
        };
    }
}
