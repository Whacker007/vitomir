// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;
using Vitomir.Platform.Config;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// One numerical profile of the rules package (schema <c>VTMR_H1_NUMERIC_PROFILE</c>): the shipping
/// template <c>enhanced-vampire-SHIPPING-UNAPPROVED</c> (its critical numbers null until a human approves
/// them) or the research sandbox <c>SANDBOX_EXPLICIT_OPT_IN</c> (research coefficients,
/// opt-in only, never production defaults). Read as written: each value keeps its JSON token, null stays
/// null.
/// </summary>
public sealed class NumericProfile
{
    public const string SchemaId = "VTMR_H1_NUMERIC_PROFILE";

    NumericProfile() { }

    public string File { get; private init; } = "";
    public string ProfileId { get; private init; } = "";

    /// <summary>The profile's own status (<c>OPEN_NUMERIC</c>, <c>SANDBOX_ONLY_COEFFICIENT</c> …).</summary>
    public string Status { get; private init; } = "";

    /// <summary>Whether the profile asks to be run only with the developer's sandbox switch (<c>use_requires_explicit_dev_sandbox_flag</c>).</summary>
    public bool IsSandbox { get; private init; }

    /// <summary>The shipping template's <c>enabled</c> (absent in the sandbox: null).</summary>
    public bool? Enabled { get; private init; }

    public bool ShippingAllowed { get; private init; }
    public bool RequiresNumericalSignoff { get; private init; }
    public bool ExternalContactRequired { get; private init; }
    public int AttributePurchasedCap { get; private init; }
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    /// <summary>The values by key, in key order: the canonical JSON text of each, or null for a JSON null.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Values { get; private init; } = [];

    /// <summary>The whole file, parsed (for the configuration hash).</summary>
    internal JsonElement Root { get; private init; }

    /// <summary>Whether a parsed file is a profile at all (by its schema), so the module can find its profiles in its folder.</summary>
    internal static bool IsProfile(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schema", out var s) &&
        s.ValueKind == JsonValueKind.String && s.GetString() == SchemaId;

    internal static string? IdOf(JsonElement root) =>
        root.TryGetProperty("profile_id", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    internal static NumericProfile? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        var id = c.Str(root, "profile_id");
        var status = c.Str(root, "status");
        var shipping = c.Bool(root, "shipping_allowed");
        var cap = c.Int(root, "attribute_purchased_cap");
        bool sandbox = c.Bool(root, "use_requires_explicit_dev_sandbox_flag", required: false) ?? false;
        bool? enabled = c.Bool(root, "enabled", required: false);
        bool signoff = c.Bool(root, "requires_explicit_numerical_signoff", required: false) ?? false;
        bool contact = c.Bool(root, "external_contact_required", required: false) ?? false;
        // The rules package's decision: actual generation and era are metadata. This
        // module has no ancestry or era modifier; a profile that does not say so is not one it can honour.
        var noAncestry = c.Bool(root, "no_ancestry_or_era_runtime_modifier");
        if (noAncestry is false) c.Fail("no_ancestry_or_era_runtime_modifier is false, but this module has no ancestry or era modifier");
        var warnings = new List<string>();
        if (c.Arr(root, "warnings", required: false) is { } w)
            foreach (var x in w.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) warnings.Add(x.GetString()!);
        var values = new List<KeyValuePair<string, string?>>();
        if (c.Obj(root, "values") is { } vs)
        {
            foreach (var p in vs.EnumerateObject())
                values.Add(new(p.Name, p.Value.ValueKind == JsonValueKind.Null ? null : Encoding.UTF8.GetString(CanonicalJson.Canonicalize(p.Value))));
            values.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            for (int i = 1; i < values.Count; i++)
                if (values[i].Key == values[i - 1].Key) c.Fail($"value '{values[i].Key}' appears twice");
        }
        if (problems.Count > before) return null;
        return new NumericProfile
        {
            File = file,
            ProfileId = id!,
            Status = status!,
            IsSandbox = sandbox,
            Enabled = enabled,
            ShippingAllowed = shipping!.Value,
            RequiresNumericalSignoff = signoff,
            ExternalContactRequired = contact,
            AttributePurchasedCap = cap!.Value,
            Warnings = warnings,
            Values = values,
            Root = root.Clone(),
        };
    }
}

/// <summary>Where a profile value stands.</summary>
public enum ValueStatus
{
    /// <summary>Null in the profile: no number is chosen. Reading it at run time throws <see cref="UnresolvedValueException"/>.</summary>
    Unresolved,

    /// <summary>A number is written, but its gate has not been adopted for the game: a research or provisional value, not a shipping one.</summary>
    ResearchOnly,

    /// <summary>A number is written and its gate says the game adopted it (none is, yet).</summary>
    ShippingApproved,
}

/// <summary>
/// One value of the settled configuration with its status and provenance (by design:
/// "a provenance and a status per value"): its JSON text (null when unresolved), the gate it answers to
/// with that gate's research status, the profile it came from, and so whether it may ship.
/// </summary>
public sealed record ConfigValue(string Key, string? Json, ValueStatus Status, string GateId, string GateResearchStatus,
                                 bool GateAdopted, string ProfileId, string ProfileStatus)
{
    /// <summary>Whether the profile gives a value at all.</summary>
    public bool IsResolved => Json is not null;

    /// <summary>One line for a trace or a report: which profile, which gate, what standing.</summary>
    public string Provenance =>
        $"profile {ProfileId} ({ProfileStatus}); gate {GateId} {GateResearchStatus}{(GateAdopted ? ", adopted" : ", not adopted")}";
}

/// <summary>
/// A rule asked for a value the configuration leaves null (fail closed per use: no rule can silently use a
/// missing number). The sandbox runs with nulls in it; the first rule that needs one stops here, named.
/// </summary>
public sealed class UnresolvedValueException(string key, string profileId, string gateId, string gateStatus)
    : Exception($"value '{key}' is unresolved in profile {profileId} (gate {gateId}, {gateStatus}): no rule may use it until the rules package settles it")
{
    public string Key { get; } = key;
    public string ProfileId { get; } = profileId;
    public string GateId { get; } = gateId;
    public string GateStatus { get; } = gateStatus;
}
