// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>
/// A sheet template in the module's data (ours, <c>Data/sheets/*.json</c>, schema <see cref="SheetFiles.TemplateSchema"/>):
/// a fighter's SANDBOX sheet, and whom it dresses — the role (<c>player</c>, <c>townsperson</c>) and the 1.1 characters
/// (catalogue or template names) it is given to.
/// </summary>
public sealed record SheetFile(AssetId Asset, SheetTemplate Template, string Role, IReadOnlyList<string> Characters);

/// <summary>
/// The fighters' sheets (ours): read from <c>Data/sheets/</c>, strictly, every reason named. A template gives the
/// species, generation and era, the purchased values it lists (Attributes it does not list at its
/// <c>unlisted_attributes</c>, Abilities at their minimum), its learned powers with their options, any resources and
/// familiarity (at the minimum unless given). Each is <c>SANDBOX</c> and refused outside the research sandbox.
/// </summary>
public static class SheetFiles
{
    public const string TemplateSchema = "VITOMIR_ENHANCED_SHEET_TEMPLATE";
    public const string Folder = "sheets";

    /// <summary>Reads one template; null (and the reasons in <paramref name="problems"/>) when it is wrong.</summary>
    public static SheetFile? Read(AssetId asset, JsonElement root, TraitLayout layout, List<string> problems) =>
        ReadAs(asset.ToString(), root, layout, problems) is { } t ? t with { Asset = asset } : null;

    internal static SheetFile? ReadAs(string file, JsonElement root, TraitLayout layout, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, TemplateSchema);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"status is '{status}': a fighter's template is SANDBOX until the rules package's cohort templates come");
        string id = c.Str(root, "id") ?? "";
        string role = c.Str(root, "role", required: false) ?? "";
        if (role.Length > 0 && role is not ("player" or "townsperson")) c.Fail($"role '{role}' is neither player nor townsperson");
        var characters = new List<string>();
        if (c.Arr(root, "characters", required: false) is { } ch)
            foreach (var x in ch.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) characters.Add(x.GetString()!);
                else c.Fail("characters must hold names");
        string species = c.Str(root, "species") ?? "";
        string generation = c.Str(root, "generation", required: false) ?? "";
        string era = c.Str(root, "era", required: false) ?? "";
        int unlisted = c.Int(root, "unlisted_attributes", required: false) ?? 0;

        var purchased = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in layout.Traits)
            purchased[t.Id] = t.Kind == TraitKind.Attribute ? unlisted : t.Domain.Min;
        if (c.Obj(root, "purchased") is { } p)
            foreach (var x in p.EnumerateObject())
            {
                var bought = layout.IndexOf(x.Name) < 0 ? layout.Purchase(x.Name) : null;   // the module's own (blood_pool)
                if (layout.IndexOf(x.Name) < 0 && bought is null) { c.Fail($"purchased: the layout has no trait '{x.Name}'"); continue; }
                if (x.Value.ValueKind != JsonValueKind.Number || !x.Value.TryGetInt32(out int v)) { c.Fail($"purchased.{x.Name} must be a whole number"); continue; }
                if (bought is not null && !bought.Domain.Allows(v)) { c.Fail($"purchased {x.Name} = {v} is outside {bought.Domain.Min}..{bought.Domain.MaxPurchased} ({bought.Source})"); continue; }
                purchased[x.Name] = v;
            }
        foreach (var t in layout.Traits)
            if (!t.Domain.Allows(purchased[t.Id])) c.Fail($"purchased {t.Id} = {purchased[t.Id]} is outside {t.Domain.Min}..{t.Domain.MaxPurchased}");
        // A role's default — a template with a role and no characters dresses whoever of that role no other names.
        if (role.Length > 0 && characters.Count == 0 && c.Has(root, "characters")) c.Fail("characters is empty: leave it out for the role's default");

        var familiarity = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in layout.Families) familiarity[f] = layout.FamiliarityMin;
        if (c.Obj(root, "familiarity", required: false) is { } fam)
            foreach (var x in fam.EnumerateObject())
            {
                if (layout.FamilyIndexOf(x.Name) < 0) { c.Fail($"familiarity: the layout has no family '{x.Name}'"); continue; }
                if (c.Num(x.Value, $"familiarity.{x.Name}") is { } d) familiarity[x.Name] = d;
            }

        var powers = new List<LearnedPower>();
        if (c.Arr(root, "powers", required: false) is { } pw)
            foreach (var x in pw.EnumerateArray())
            {
                var power = c.Str(x, "power");
                var rank = c.Int(x, "rank");
                var options = new List<string>();
                if (c.Arr(x, "options", required: false) is { } os)
                    foreach (var o in os.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String) options.Add(o.GetString()!);
                        else c.Fail($"powers: the options of '{power}' must be names");
                if (power is not null && rank is { } r) powers.Add(new LearnedPower(power, r, [.. options.Order(StringComparer.Ordinal)]));
            }

        var resources = new List<ResourceState>();
        if (c.Arr(root, "resources", required: false) is { } rs)
            foreach (var x in rs.EnumerateArray())
            {
                var rid = c.Str(x, "id");
                var cur = c.Int(x, "current");
                var max = c.Int(x, "maximum");
                if (rid is not null && cur is { } cu && max is { } mx)
                    resources.Add(new ResourceState(rid, cu, mx, c.Int(x, "per_action_spend_limit", required: false) ?? 0, c.Int(x, "per_cycle_spend_limit", required: false) ?? 0));
            }

        if (problems.Count > before) return null;
        var sheet = new SheetData(species, generation, era, id, purchased, familiarity, resources, powers, new Dictionary<string, string>(), []);
        var template = new SheetTemplate(id, status, c.Str(root, "note", required: false) ?? "ours: a SANDBOX fighter", sheet);
        return new SheetFile(default, template, role, characters);
    }

    /// <summary>
    /// A sheet from a file's text (the <c>--sheet</c> route): one of these templates, or a sheet's save
    /// (<see cref="SheetJson"/>). Throws <see cref="PersonSheetException"/> with every reason.
    /// </summary>
    public static SheetData FromText(string text, string source, TraitLayout layout)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException e) { throw new PersonSheetException(source, [$"not valid JSON ({e.Message})"]); }
        string? schema = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        if (schema == TemplateSchema)
        {
            var problems = new List<string>();
            var t = ReadAs(source, root, layout, problems);
            if (t is null) throw new PersonSheetException(source, problems);
            return t.Template.Sheet;
        }
        if (schema == SheetJson.Schema)
        {
            try { return SheetJson.Read(text); }
            catch (SheetFormatException e) { throw new PersonSheetException(source, e.Problems); }
        }
        throw new PersonSheetException(source, [$"schema is '{schema ?? "(none)"}': a sheet file is a {TemplateSchema} or a {SheetJson.Schema}"]);
    }
}
