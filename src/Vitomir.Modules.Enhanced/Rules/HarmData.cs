// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Combat;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>A weapon class (ours, <c>Data/rules/harm.json</c>): the kind of harm it does and its damage addend, its own SANDBOX number or a profile value by name.</summary>
public sealed record WeaponClass(string Id, HarmKind Kind, double? Bonus, string? BonusProfileValue, string Status, string Source);

/// <summary>What may soak one kind of harm for one species (the combat rules' soak eligibility): ordinary Stamina, learned Fortitude, both or neither.</summary>
public readonly record struct SoakEligibility(bool Stamina, bool Fortitude);

/// <summary>
/// The staged harm's own numbers (ours, <c>enhanced:rules/harm</c>, schema <see cref="SchemaId"/>): everything the staged harm needs
/// that the rules package's profile does not give — the dice policy's name and the purposes of its random streams, the
/// weapon classes (kind, addend) and which 1.1 weapon template is which, who soaks what with what (by species), Fortitude's
/// soak per rank and Potence's option, and the engine-sandbox Health. Every number is SANDBOX or ENGINE_SANDBOX. Read
/// strictly: a missing or wrong field is refused by name (fail closed), with every reason.
/// </summary>
public sealed class HarmData
{
    public const string SchemaId = "VITOMIR_ENHANCED_HARM";
    public const string Asset = "rules/harm";

    /// <summary>The dice policies and fraction rules this module knows (the combat rules' prototype; nothing else yet).</summary>
    public const string PrototypeDice = "H1_C4_PROTOTYPE", NeighbourFraction = "H1_C4_NEIGHBOUR";

    public string File { get; private init; } = "";
    public string Status { get; private init; } = "";
    public string DicePolicy { get; private init; } = "";
    public string FractionalPool { get; private init; } = "";
    public string PoolStream { get; private init; } = "";
    public string DamageStream { get; private init; } = "";
    public string SoakStream { get; private init; } = "";

    /// <summary>The classes by id (ordinal order).</summary>
    public IReadOnlyDictionary<string, WeaponClass> Classes { get; private init; } = new SortedDictionary<string, WeaponClass>(StringComparer.Ordinal);

    /// <summary>Which class each 1.1 weapon template is (its name, case aside).</summary>
    public IReadOnlyDictionary<string, string> Weapons { get; private init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The class of a body that carries no weapon.</summary>
    public string BareHands { get; private init; } = "";

    /// <summary>Who soaks what: species → kind → channels.</summary>
    public IReadOnlyDictionary<string, SoakEligibility[]> Soak { get; private init; } = new SortedDictionary<string, SoakEligibility[]>(StringComparer.Ordinal);

    /// <summary>The species whose bashing is halved after soak, and the profile's switch for it.</summary>
    public IReadOnlyList<string> BashingHalvedFor { get; private init; } = [];
    public string BashingHalvedProfileValue { get; private init; } = "";

    public string FortitudePower { get; private init; } = "";
    public string FortitudeOption { get; private init; } = "";
    public double FortitudeSoakDicePerRank { get; private init; }
    public bool FortitudeAggravatedPermission { get; private init; }
    public string FortitudeStatus { get; private init; } = "";

    public string PotencePower { get; private init; } = "";
    public string PotenceOption { get; private init; } = "";
    public string PotencePerRankProfileValue { get; private init; } = "";

    public string HealthStatus { get; private init; } = "";
    public string HealthProfileValue { get; private init; } = "";
    public string HealthResource { get; private init; } = "";
    /// <summary>The maximum a person gets when its sheet buys none (the purchase's default, 100, 1:1).</summary>
    public int HealthMaximum { get; private init; }
    public string HealthAtNothing { get; private init; } = "";

    /// <summary>
    /// Health is a purchased capacity on the 1–1000 scale, as Blood is — the module's own purchase
    /// <see cref="HealthPurchaseId"/> (<c>health_pool</c>) in <see cref="HealthPurchaseMin"/>..<see cref="HealthPurchaseMax"/>,
    /// <see cref="HealthMaximum"/> when a sheet lists none; the maximum is the purchase 1:1 (<see cref="MaximumOf"/>) while the
    /// profile's <c>health_max_purchase_curve</c> is null.
    /// </summary>
    public string HealthPurchaseId { get; private init; } = "health_pool";
    public int HealthPurchaseMin { get; private init; } = 1;
    public int HealthPurchaseMax { get; private init; } = 1000;
    public string HealthPurchaseSource { get; private init; } = "";

    /// <summary>The Health a wound converts to (ENGINE_SANDBOX, 15: seven wounds still stop the default 100; the translator).</summary>
    public int HealthPerWound { get; private init; } = 1;

    /// <summary>The bands' shares of the purchased maximum (labels only; the package's proposals: 25 % and 10 %).</summary>
    public double InjuredBelow { get; private init; }
    public double CrippledBelow { get; private init; }

    /// <summary>The Health maximum a purchase gives (1:1 while the profile's curve is null).</summary>
    public long MaximumOf(int pool) => pool;

    /// <summary>
    /// The band of <paramref name="health"/> out of <paramref name="maximum"/> (labels only): at nothing
    /// <see cref="HealthBand.Stopped"/>, below <see cref="CrippledBelow"/> of the maximum <see cref="HealthBand.Crippled"/>,
    /// below <see cref="InjuredBelow"/> <see cref="HealthBand.Injured"/>, else <see cref="HealthBand.None"/> (and None for a
    /// person with no maximum).
    /// </summary>
    public HealthBand Band(long health, long maximum) =>
        maximum <= 0 ? HealthBand.None
        : health <= 0 ? HealthBand.Stopped
        : health < maximum * CrippledBelow ? HealthBand.Crippled
        : health < maximum * InjuredBelow ? HealthBand.Injured
        : HealthBand.None;

    /// <summary>The class of a weapon template (null: not in the table); an empty template is bare hands.</summary>
    public WeaponClass? ClassOf(string? template)
    {
        string id = string.IsNullOrEmpty(template) ? BareHands : Weapons.TryGetValue(template, out var c) ? c : "";
        return id.Length > 0 && Classes.TryGetValue(id, out var wc) ? wc : null;
    }

    /// <summary>What may soak <paramref name="kind"/> for <paramref name="species"/>, or null when the species is not in the table.</summary>
    public SoakEligibility? SoakFor(string species, HarmKind kind) =>
        Soak.TryGetValue(species, out var s) ? s[(int)kind] : null;

    internal static HarmData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"status is '{status}': the staged harm's own numbers are SANDBOX until the rules package gives them");

        string policy = "", fraction = "", pool = "", damage = "", soakStream = "";
        if (c.Obj(root, "dice") is { } dice)
        {
            policy = c.Str(dice, "policy") ?? "";
            if (policy.Length > 0 && policy != PrototypeDice) c.Fail($"dice.policy '{policy}' is not one this module knows ({PrototypeDice})");
            fraction = c.Str(dice, "fractional_pool") ?? "";
            if (fraction.Length > 0 && fraction != NeighbourFraction) c.Fail($"dice.fractional_pool '{fraction}' is not one this module knows ({NeighbourFraction})");
            if (c.Obj(dice, "streams") is { } streams)
            {
                pool = c.Str(streams, "pool") ?? "";
                damage = c.Str(streams, "damage") ?? "";
                soakStream = c.Str(streams, "soak") ?? "";
                if (pool.Length > 0 && (pool == damage || pool == soakStream || damage == soakStream))
                    c.Fail("dice.streams: the pool, damage and soak streams must be three purposes (one stream per purpose)");
            }
        }

        var classes = new SortedDictionary<string, WeaponClass>(StringComparer.Ordinal);
        if (c.Obj(root, "classes") is { } cls)
            foreach (var p in cls.EnumerateObject())
            {
                var v = p.Value;
                var kindText = c.Str(v, "kind");
                HarmKind? kind = kindText switch { "bashing" => HarmKind.Bashing, "lethal" => HarmKind.Lethal, "aggravated" => HarmKind.Aggravated, null => null, _ => null };
                if (kindText is not null && kind is null) c.Fail($"classes.{p.Name}.kind '{kindText}' is not bashing, lethal or aggravated");
                double? bonus = null;
                string? fromProfile = c.Str(v, "bonus_profile_value", required: false);
                if (v.TryGetProperty("bonus", out var b)) bonus = c.Num(b, $"classes.{p.Name}.bonus");
                if (bonus is null == fromProfile is null)
                    c.Fail($"classes.{p.Name}: give exactly one of 'bonus' (a SANDBOX number) and 'bonus_profile_value' (a profile value's name)");
                string st = fromProfile is not null ? "PROFILE" : c.Str(v, "status") ?? "";
                if (fromProfile is null && st.Length > 0 && st != "SANDBOX") c.Fail($"classes.{p.Name}.status '{st}' must be SANDBOX");
                if (kind is { } k) classes[p.Name] = new WeaponClass(p.Name, k, bonus, fromProfile, st, c.Str(v, "source") ?? "");
            }

        var weapons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (c.Obj(root, "weapons") is { } wp)
            foreach (var p in wp.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) { c.Fail($"weapons.{p.Name} must name a class"); continue; }
                string id = p.Value.GetString()!;
                if (!classes.ContainsKey(id)) c.Fail($"weapons.{p.Name} names class '{id}', which 'classes' does not have");
                if (!weapons.TryAdd(p.Name, id)) c.Fail($"weapons.{p.Name} appears twice (case aside)");
            }
        string bare = c.Str(root, "bare_hands") ?? "";
        if (bare.Length > 0 && !classes.ContainsKey(bare)) c.Fail($"bare_hands names class '{bare}', which 'classes' does not have");

        var soak = new SortedDictionary<string, SoakEligibility[]>(StringComparer.Ordinal);
        var halved = new List<string>();
        string halvedValue = "";
        if (c.Obj(root, "soak") is { } sk)
        {
            if (c.Obj(sk, "species") is { } species)
                foreach (var p in species.EnumerateObject())
                {
                    var row = new SoakEligibility[3];
                    foreach (var (name, kind) in new[] { ("bashing", HarmKind.Bashing), ("lethal", HarmKind.Lethal), ("aggravated", HarmKind.Aggravated) })
                    {
                        if (c.Arr(p.Value, name) is not { } channels) continue;
                        bool stamina = false, fortitude = false;
                        foreach (var ch in channels.EnumerateArray())
                            switch (ch.ValueKind == JsonValueKind.String ? ch.GetString() : null)
                            {
                                case "stamina": stamina = true; break;
                                case "fortitude": fortitude = true; break;
                                default: c.Fail($"soak.species.{p.Name}.{name}: '{ch.GetRawText()}' is not a soak channel (stamina, fortitude)"); break;
                            }
                        row[(int)kind] = new SoakEligibility(stamina, fortitude);
                    }
                    soak[p.Name] = row;
                }
            if (c.Arr(sk, "bashing_halved_for") is { } hf)
                foreach (var s in hf.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.String) { c.Fail("soak.bashing_halved_for must hold species' names"); continue; }
                    if (!soak.ContainsKey(s.GetString()!)) c.Fail($"soak.bashing_halved_for names species '{s.GetString()}', which soak.species does not have");
                    halved.Add(s.GetString()!);
                }
            halvedValue = c.Str(sk, "bashing_halved_profile_value") ?? "";
        }

        string fPower = "", fOption = "", fStatus = "";
        double fPerRank = 0;
        bool fAgg = false;
        if (c.Obj(root, "fortitude") is { } f)
        {
            fStatus = c.Str(f, "status") ?? "";
            if (fStatus.Length > 0 && fStatus != "ENGINE_SANDBOX") c.Fail($"fortitude.status '{fStatus}' must be ENGINE_SANDBOX (the profile has no value for it)");
            fPower = c.Str(f, "power") ?? "";
            fOption = c.Str(f, "option") ?? "";
            if (f.TryGetProperty("soak_dice_per_rank", out var pr) && c.Num(pr, "fortitude.soak_dice_per_rank") is { } d)
            {
                if (d < 0) c.Fail("fortitude.soak_dice_per_rank must not be negative");
                fPerRank = d;
            }
            else if (!f.TryGetProperty("soak_dice_per_rank", out _)) c.Fail("fortitude.soak_dice_per_rank is missing");
            fAgg = c.Bool(f, "aggravated_permission") ?? false;
        }

        string pPower = "", pOption = "", pValue = "";
        if (c.Obj(root, "potence") is { } po)
        {
            pPower = c.Str(po, "power") ?? "";
            pOption = c.Str(po, "option") ?? "";
            pValue = c.Str(po, "per_rank_profile_value") ?? "";
            var rounding = c.Str(po, "rounding") ?? "";
            if (rounding.Length > 0 && rounding != "floor") c.Fail($"potence.rounding '{rounding}' is not one this module knows (floor)");
        }

        string hStatus = "", hValue = "", hResource = "", hAt = "", hId = "health_pool", hSource = "";
        int hMax = 0, hMin = 1, hTop = 1000, hPerWound = 1;
        double injured = 0, crippled = 0;
        if (c.Obj(root, "health") is { } h)
        {
            hStatus = c.Str(h, "status") ?? "";
            if (hStatus.Length > 0 && hStatus != "ENGINE_SANDBOX") c.Fail($"health.status '{hStatus}' must be ENGINE_SANDBOX (an engine stand-in, not a rule)");
            hValue = c.Str(h, "profile_value") ?? "";
            hResource = c.Str(h, "resource") ?? "";
            // The purchased capacity (health_pool, 1 to 1000, 100 by default: ) and its maximum 1:1.
            if (c.Obj(h, "purchase") is { } hp)
            {
                hId = c.Str(hp, "id") ?? "";
                if (!Sheets.TraitLayout.IsId(hId)) c.Fail($"health.purchase.id '{hId}' is not a lower-case identifier");
                hMin = c.Int(hp, "min") ?? 0;
                hTop = c.Int(hp, "max") ?? 0;
                hMax = c.Int(hp, "default") ?? 0;
                hSource = c.Str(hp, "source") ?? "";
                if (hMin < 1 || hTop < hMin) c.Fail($"health.purchase: min {hMin} and max {hTop} must make a range from 1 up (a person with no Health has no stop)");
                else if (hMax < hMin || hMax > hTop) c.Fail($"health.purchase.default {hMax} is outside {hMin}..{hTop}");
            }
            if (h.TryGetProperty("maximum", out var mx) && !(mx.ValueKind == JsonValueKind.String && mx.GetString() == "purchase"))
                c.Fail($"health.maximum must be \"purchase\" (the maximum is health_pool 1:1 while {hValue} is null), not {mx.GetRawText()}");
            _ = c.Str(h, "maximum_source");
            hPerWound = c.Int(h, "hp_per_wound") ?? 0;
            if (hPerWound < 1) c.Fail($"health.hp_per_wound must be at least 1, not {hPerWound} (a wound that costs no Health is no wound)");
            _ = c.Str(h, "hp_per_wound_source");
            if (c.Obj(h, "bands") is { } hb)
            {
                string bs = c.Str(hb, "status") ?? "";
                if (bs.Length > 0 && bs != "ENGINE_SANDBOX") c.Fail($"health.bands.status '{bs}' must be ENGINE_SANDBOX (the package's proposals, labels only)");
                injured = hb.TryGetProperty("injured_below", out var ib) ? c.Num(ib, "health.bands.injured_below") ?? 0 : 0;
                crippled = hb.TryGetProperty("crippled_below", out var cb) ? c.Num(cb, "health.bands.crippled_below") ?? 0 : 0;
                if (!(crippled > 0 && crippled < injured && injured < 1))
                    c.Fail($"health.bands: crippled_below {crippled} and injured_below {injured} must be shares with 0 < crippled < injured < 1");
                if (c.Str(hb, "effects") is { } fx && fx != "none: labels only") c.Fail($"health.bands.effects '{fx}': the bands are labels only here (their penalties are OPEN)");
                _ = c.Str(hb, "source");
            }
            hAt = c.Str(h, "at_nothing") ?? "";
            if (hAt.Length > 0 && hAt != "died") c.Fail($"health.at_nothing '{hAt}' is not one this module knows (died)");
        }

        return problems.Count > before ? null : new HarmData
        {
            File = file, Status = status, DicePolicy = policy, FractionalPool = fraction,
            PoolStream = pool, DamageStream = damage, SoakStream = soakStream,
            Classes = classes, Weapons = weapons, BareHands = bare, Soak = soak, BashingHalvedFor = halved, BashingHalvedProfileValue = halvedValue,
            FortitudePower = fPower, FortitudeOption = fOption, FortitudeSoakDicePerRank = fPerRank, FortitudeAggravatedPermission = fAgg, FortitudeStatus = fStatus,
            PotencePower = pPower, PotenceOption = pOption, PotencePerRankProfileValue = pValue,
            HealthStatus = hStatus, HealthProfileValue = hValue, HealthResource = hResource, HealthMaximum = hMax, HealthAtNothing = hAt,
            HealthPurchaseId = hId, HealthPurchaseMin = hMin, HealthPurchaseMax = hTop, HealthPurchaseSource = hSource,
            HealthPerWound = hPerWound, InjuredBelow = injured, CrippledBelow = crippled,
        };
    }
}
