// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>A data file the configuration read, with its SHA-256 and whether that is the rules package's own file unchanged.</summary>
public sealed record DataFileIdentity(string File, string Sha256, string? PackagePath, bool MatchesPackage);

/// <summary>
/// The configuration <see cref="EnhancedModule.Configure"/> settled on (the rules package's configuration): the trait
/// layout from the registry, the chosen profile's values each with status and provenance, the power
/// contracts, the configuration hash, and the stamp every trace carries. Registered as a world service, so a
/// rule reads its numbers here — and a null one throws <see cref="UnresolvedValueException"/> at the read.
/// </summary>
public sealed class EnhancedConfig
{
    readonly SortedDictionary<string, ConfigValue> values;

    internal EnhancedConfig(ModuleStamp stamp, TraitRegistry registry, NumericProfile profile, GateRegister gates,
                            PowerContracts powers, IReadOnlyList<ConfigValue> values, IReadOnlyList<DataFileIdentity> files)
    {
        Stamp = stamp;
        Registry = registry;
        Profile = profile;
        Gates = gates;
        Powers = powers;
        Files = files;
        this.values = new SortedDictionary<string, ConfigValue>(StringComparer.Ordinal);
        foreach (var v in values) this.values[v.Key] = v;
    }

    public ModuleStamp Stamp { get; }

    /// <summary>SHA-256 (hex) of the canonical JSON of the registry and the profile together (<c>{"profile":…,"registry":…}</c>).</summary>
    public string ConfigHash => Stamp.ConfigHash;

    public TraitRegistry Registry { get; }
    public TraitLayout Layout => Registry.Layout;
    public NumericProfile Profile { get; }
    public GateRegister Gates { get; }
    public PowerContracts Powers { get; }

    /// <summary>The data files read, with their hashes.</summary>
    public IReadOnlyList<DataFileIdentity> Files { get; }

    /// <summary>Whether this is the research sandbox (non-shipping): the program must say so on screen and every trace carries it in the stamp.</summary>
    public bool IsSandbox => Stamp.NonshippingResearch;

    /// <summary>The line the program shows while the sandbox runs (empty for a shipping profile).</summary>
    public string Banner => IsSandbox
        ? $"RULES SANDBOX: research numbers, not the game's rules (config {ConfigHash[..8]})"
        : "";

    /// <summary>Every value in key order.</summary>
    public IEnumerable<ConfigValue> Values => values.Values;

    /// <summary>A value with its status and provenance; throws for a key the profile does not have.</summary>
    public ConfigValue Value(string key) =>
        values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"profile {Profile.ProfileId} has no value '{key}'");

    /// <summary>A value's JSON, or <see cref="UnresolvedValueException"/> when it is null (fail closed per use).</summary>
    public JsonElement Json(string key)
    {
        var v = Resolved(key);
        using var doc = JsonDocument.Parse(v.Json!);
        return doc.RootElement.Clone();
    }

    /// <summary>A numeric value; <see cref="UnresolvedValueException"/> when null, and a named error when it is not a number.</summary>
    public double Number(string key)
    {
        var v = Resolved(key);
        if (!double.TryParse(v.Json, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            throw new InvalidOperationException($"value '{key}' of profile {Profile.ProfileId} is {v.Json}, not a number");
        return d;
    }

    /// <summary>A text value (formula labels such as <c>raw/20</c>); <see cref="UnresolvedValueException"/> when null.</summary>
    public string Text(string key)
    {
        var e = Json(key);
        return e.ValueKind == JsonValueKind.String ? e.GetString()!
            : throw new InvalidOperationException($"value '{key}' of profile {Profile.ProfileId} is {e.GetRawText()}, not a text");
    }

    /// <summary>A true/false value; <see cref="UnresolvedValueException"/> when null.</summary>
    public bool Flag(string key)
    {
        var e = Json(key);
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"value '{key}' of profile {Profile.ProfileId} is {e.GetRawText()}, not true or false"),
        };
    }

    ConfigValue Resolved(string key)
    {
        var v = Value(key);
        if (!v.IsResolved) throw new UnresolvedValueException(key, v.ProfileId, v.GateId, v.GateResearchStatus);
        return v;
    }
}
