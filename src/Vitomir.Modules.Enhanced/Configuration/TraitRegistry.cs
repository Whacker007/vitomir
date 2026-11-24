// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Sheets;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>An Ability's working alias in the enhanced module (the registry's <c>alias</c>: Brawl as "Grappling", Firearms as "Range" …), with its decision status.</summary>
public sealed record TraitAlias(string? WorkingName, string Status, string? Candidate);

/// <summary>
/// The rules package's trait registry (<c>vampire_trait_registry.json</c>, schema
/// <c>VTMR_VAMPIRE_TRAIT_REGISTRY_H1</c>), read and checked: the nine Attributes (purchased 0..1,000,
/// temporary effective overcap allowed), the thirty Abilities (0..10), the domain of a learned power's
/// rank (0..10, options learned separately), the familiarity families (0..100 %), and the Abilities'
/// working aliases. It becomes a <see cref="TraitLayout"/>: from there on nothing counts traits in code.
/// </summary>
public sealed class TraitRegistry
{
    public const string SchemaId = "VTMR_VAMPIRE_TRAIT_REGISTRY_H1";

    TraitRegistry(TraitLayout layout, IReadOnlyDictionary<string, TraitAlias> aliases, bool arbitraryTraits, bool familiarityOptional, string familiarityCurves)
    {
        Layout = layout;
        Aliases = aliases;
        EngineMustSupportArbitraryTraits = arbitraryTraits;
        FamiliarityOptional = familiarityOptional;
        FamiliarityCurves = familiarityCurves;
    }

    public TraitLayout Layout { get; }

    /// <summary>Working aliases by Ability id (only those the registry gives one).</summary>
    public IReadOnlyDictionary<string, TraitAlias> Aliases { get; }

    public bool EngineMustSupportArbitraryTraits { get; }
    public bool FamiliarityOptional { get; }

    /// <summary>The registry's word on the familiarity response curves (<c>OPEN</c>).</summary>
    public string FamiliarityCurves { get; }

    /// <summary>Reads a registry, adding every problem to <paramref name="problems"/>; null when it cannot be used.</summary>
    internal static TraitRegistry? Read(JsonElement root, string file, string moduleId, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        var module = c.Str(root, "module");
        if (module is not null && module != moduleId) c.Fail($"module is '{module}', not '{moduleId}'");

        var attrDomain = Domain(c, root, "attribute_domain", "max_purchased", overcapKey: "effective_temp_overcap");
        var ablDomain = Domain(c, root, "ability_domain", "max", overcapKey: null);
        var discDomain = Domain(c, root, "discipline_domain", "max", overcapKey: null);
        if (c.Obj(root, "discipline_domain") is { } dd && c.Bool(dd, "option_learned_separately") is false)
            c.Fail("discipline_domain.option_learned_separately is false; this module learns every option by name");

        var traits = new List<TraitDef>();
        var aliases = new SortedDictionary<string, TraitAlias>(StringComparer.Ordinal);
        if (c.Arr(root, "attributes") is { } attrs)
        {
            if (attrs.GetArrayLength() == 0) c.Fail("'attributes' is empty");
            int i = 0;
            foreach (var a in attrs.EnumerateArray())
            {
                var id = c.Str(a, "id");
                var display = c.Str(a, "display");
                var category = c.Str(a, "category");
                if (id is not null && !TraitLayout.IsId(id)) c.Fail($"attributes[{i}].id '{id}' is not a lower-case identifier");
                if (id is not null && display is not null && category is not null && attrDomain is { } d)
                    traits.Add(new TraitDef(id, TraitKind.Attribute, display, category, d));
                i++;
            }
        }
        if (c.Arr(root, "abilities") is { } abls)
        {
            if (abls.GetArrayLength() == 0) c.Fail("'abilities' is empty");
            int i = 0;
            foreach (var a in abls.EnumerateArray())
            {
                var id = c.Str(a, "id");
                var name = c.Str(a, "source_name");
                var category = c.Str(a, "category");
                if (id is not null && !TraitLayout.IsId(id)) c.Fail($"abilities[{i}].id '{id}' is not a lower-case identifier");
                if (id is not null && name is not null && category is not null && ablDomain is { } d)
                    traits.Add(new TraitDef(id, TraitKind.Ability, name, category, d));
                if (id is not null && c.Obj(a, "alias", required: false) is { } al)
                {
                    var status = c.Str(al, "status");
                    string? working = al.TryGetProperty("enhanced_working_name", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                    string? candidate = al.TryGetProperty("candidate", out var cd) && cd.ValueKind == JsonValueKind.String ? cd.GetString() : null;
                    if (status is not null) aliases[id] = new TraitAlias(working, status, candidate);
                }
                i++;
            }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in traits)
            if (!seen.Add(t.Id)) c.Fail($"trait '{t.Id}' appears twice");

        var families = new List<string>();
        double famMin = 0, famMax = 100;
        bool famOptional = true;
        string famCurves = "";
        if (c.Obj(root, "familiarity") is { } fam)
        {
            if (c.Arr(fam, "domain_percent") is { } dp)
            {
                var ends = dp.EnumerateArray().ToArray();
                if (ends.Length != 2) c.Fail("familiarity.domain_percent must be [min, max]");
                else if (c.Num(ends[0], "familiarity.domain_percent[0]") is double lo && c.Num(ends[1], "familiarity.domain_percent[1]") is double hi)
                {
                    if (lo > hi) c.Fail("familiarity.domain_percent is empty");
                    famMin = lo; famMax = hi;
                }
            }
            if (c.Arr(fam, "families") is { } fs)
                foreach (var f in fs.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.String || !TraitLayout.IsId(f.GetString())) { c.Fail($"familiarity family {f.GetRawText()} is not a lower-case identifier"); continue; }
                    if (families.Contains(f.GetString()!)) c.Fail($"familiarity family '{f.GetString()}' appears twice");
                    else families.Add(f.GetString()!);
                }
            famOptional = c.Bool(fam, "optional") ?? true;
            famCurves = c.Str(fam, "numeric_curves") ?? "";
        }
        bool arbitrary = c.Bool(root, "engine_must_support_arbitrary_traits") ?? false;

        if (problems.Count > before || discDomain is null) return null;
        var layout = new TraitLayout(traits, families, famMin, famMax, discDomain.Value.Min, discDomain.Value.MaxPurchased);
        return new TraitRegistry(layout, aliases, arbitrary, famOptional, famCurves);
    }

    static TraitDomain? Domain(JsonChecks c, JsonElement root, string key, string maxKey, string? overcapKey)
    {
        if (c.Obj(root, key) is not { } d) return null;
        var integral = c.Bool(d, "integral");
        var min = c.Int(d, "min");
        var max = c.Int(d, maxKey);
        bool overcap = overcapKey is not null && (c.Bool(d, overcapKey) ?? false);
        if (integral is false) { c.Fail($"{key}.integral is false; this module keeps whole-number traits only"); return null; }
        if (min is null || max is null || integral is null) return null;
        if (max < min) { c.Fail($"{key} is empty ({min}..{max})"); return null; }
        return new TraitDomain(min.Value, max.Value, overcap);
    }
}
