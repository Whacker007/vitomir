// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;

namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>A save that cannot be read as a sheet, with every reason (a newer format, a wrong type, a duplicated id …).</summary>
public sealed class SheetFormatException(IReadOnlyList<string> problems)
    : Exception($"the sheet cannot be read: {string.Join("; ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// An actor sheet's save, in JSON (ours): <c>schema</c> <see cref="Schema"/>, <c>format_version</c>
/// <see cref="FormatVersion"/>, then the sheet. Written canonically — keys in ordinal order, lists in a
/// fixed order — so a sheet has one save and a save read back is the same sheet (exactly: whole numbers as
/// they are, familiarity percentages written round-trip). A save of another format version is refused, not
/// guessed at (the rules package: versioned, never silently rewritten); a migration, when one
/// is needed, will be its own named step. The domains (is 1,001 legal?) are checked by
/// <see cref="ActorSheets.Import"/> against the configured layout, not here.
/// </summary>
public static class SheetJson
{
    public const string Schema = "VITOMIR_ENHANCED_ACTOR_SHEET";
    public const int FormatVersion = 1;

    public static string Write(SheetData sheet, bool indented = true)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WritePropertyName("conditions");
            w.WriteStartObject();
            foreach (var (k, v) in Sorted(sheet.Conditions)) w.WriteString(k, v);
            w.WriteEndObject();
            w.WriteString("era", sheet.Era);
            w.WritePropertyName("familiarity");
            w.WriteStartObject();
            foreach (var (k, v) in Sorted(sheet.Familiarity)) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WriteNumber("format_version", FormatVersion);
            w.WriteString("generation", sheet.Generation);
            // The cast powers' records, only when there are any (a sheet without them saves as before).
            if (sheet.LivePowers.Count > 0)
            {
                w.WritePropertyName("live");
                w.WriteStartArray();
                foreach (var l in sheet.LivePowers.OrderBy(l => l.PowerId, StringComparer.Ordinal))
                {
                    w.WriteStartObject();
                    w.WriteNumber("action_id", l.ActionId);
                    w.WriteNumber("end_tick", l.EndTick);
                    w.WriteBoolean("ended", l.Ended);
                    w.WriteString("option", l.Option);
                    w.WriteString("power", l.PowerId);
                    w.WriteNumber("rank", l.Rank);
                    w.WriteNumber("recast_tick", l.RecastTick);
                    w.WriteNumber("start_tick", l.StartTick);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WritePropertyName("modifiers");
            w.WriteStartArray();
            foreach (var m in Order(sheet.Modifiers))
            {
                w.WriteStartObject();
                w.WriteNumber("delta", m.Delta);
                w.WriteNumber("expires_at_tick", m.ExpiresAtTick);
                w.WriteString("source", m.SourceId);
                w.WriteString("trait", m.TraitId);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("powers");
            w.WriteStartArray();
            foreach (var p in sheet.Powers.OrderBy(p => p.PowerId, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WritePropertyName("options");
                w.WriteStartArray();
                foreach (var o in p.Options.Order(StringComparer.Ordinal)) w.WriteStringValue(o);
                w.WriteEndArray();
                w.WriteString("power", p.PowerId);
                w.WriteNumber("rank", p.Rank);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("purchased");
            w.WriteStartObject();
            foreach (var (k, v) in Sorted(sheet.Purchased)) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WritePropertyName("resources");
            w.WriteStartArray();
            foreach (var r in sheet.Resources.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteNumber("current", r.Current);
                w.WriteString("id", r.Id);
                w.WriteNumber("maximum", r.Maximum);
                w.WriteNumber("per_action_spend_limit", r.PerActionSpendLimit);
                w.WriteNumber("per_cycle_spend_limit", r.PerCycleSpendLimit);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("schema", Schema);
            w.WriteString("species", sheet.Species);
            w.WriteString("template", sheet.Template);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a save; throws <see cref="SheetFormatException"/> with every problem.</summary>
    public static SheetData Read(string json)
    {
        var problems = new List<string>();
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException e) { throw new SheetFormatException([$"not valid JSON ({e.Message})"]); }
        if (root.ValueKind != JsonValueKind.Object) throw new SheetFormatException(["a sheet is a JSON object"]);

        string schema = Str(root, "schema", problems);
        if (schema != Schema) problems.Add($"schema is '{schema}', not '{Schema}'");
        if (!root.TryGetProperty("format_version", out var fv) || !fv.TryGetInt32(out int version))
            problems.Add("format_version is missing or not a whole number");
        else if (version != FormatVersion)
            problems.Add($"format_version is {version}; this build reads only {FormatVersion} and does not guess at another");
        if (problems.Count > 0) throw new SheetFormatException(problems);

        var purchased = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in Obj(root, "purchased", problems))
        {
            if (p.Value.TryGetInt32(out int v)) purchased[p.Name] = v;
            else problems.Add($"purchased.{p.Name} is {p.Value.GetRawText()}, not a whole number");
        }
        var familiarity = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in Obj(root, "familiarity", problems))
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double v)) familiarity[p.Name] = v;
            else problems.Add($"familiarity.{p.Name} is {p.Value.GetRawText()}, not a number");
        }
        var conditions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Obj(root, "conditions", problems))
        {
            if (p.Value.ValueKind == JsonValueKind.String) conditions[p.Name] = p.Value.GetString()!;
            else problems.Add($"conditions.{p.Name} is not a text");
        }
        var resources = new List<ResourceState>();
        foreach (var r in Arr(root, "resources", problems))
        {
            string id = Str(r, "id", problems);
            if (resources.Any(x => x.Id == id)) problems.Add($"resource '{id}' appears twice");
            resources.Add(new ResourceState(id, Long(r, "current", problems), Long(r, "maximum", problems),
                Long(r, "per_action_spend_limit", problems), Long(r, "per_cycle_spend_limit", problems)));
        }
        var powers = new List<LearnedPower>();
        foreach (var p in Arr(root, "powers", problems))
        {
            string id = Str(p, "power", problems);
            if (powers.Any(x => x.PowerId == id)) problems.Add($"power '{id}' appears twice");
            var options = new List<string>();
            foreach (var o in Arr(p, "options", problems))
            {
                if (o.ValueKind != JsonValueKind.String) { problems.Add($"an option of power '{id}' is not a text"); continue; }
                if (options.Contains(o.GetString()!)) problems.Add($"option '{o.GetString()}' of power '{id}' appears twice");
                options.Add(o.GetString()!);
            }
            powers.Add(new LearnedPower(id, Int(p, "rank", problems), options));
        }
        var modifiers = new List<TemporaryModifier>();
        foreach (var m in Arr(root, "modifiers", problems))
        {
            var mod = new TemporaryModifier(Str(m, "trait", problems), Int(m, "delta", problems), Str(m, "source", problems), Long(m, "expires_at_tick", problems));
            if (modifiers.Any(x => x.TraitId == mod.TraitId && x.SourceId == mod.SourceId))
                problems.Add($"two modifiers of '{mod.TraitId}' from source '{mod.SourceId}'");
            modifiers.Add(mod);
        }
        // The cast powers' records (optional: a save without them has none).
        List<LivePower>? live = null;
        if (root.TryGetProperty("live", out var lv))
        {
            live = [];
            foreach (var l in Arr(root, "live", problems))
            {
                var rec = new LivePower(Str(l, "power", problems), Str(l, "option", problems), Int(l, "rank", problems), Long(l, "start_tick", problems),
                    Long(l, "end_tick", problems), Long(l, "recast_tick", problems), Long(l, "action_id", problems), Bool(l, "ended", problems));
                if (live.Any(x => x.PowerId == rec.PowerId)) problems.Add($"power '{rec.PowerId}' is live twice");
                live.Add(rec);
            }
            if (live.Count == 0) live = null;
        }
        var sheet = new SheetData(Str(root, "species", problems), Str(root, "generation", problems), Str(root, "era", problems),
                                  Str(root, "template", problems), purchased, familiarity, resources, powers, conditions, modifiers, live);
        if (problems.Count > 0) throw new SheetFormatException(problems);
        return sheet;
    }

    /// <summary>The modifiers in their canonical order: by trait, then source.</summary>
    internal static IEnumerable<TemporaryModifier> Order(IEnumerable<TemporaryModifier> modifiers) =>
        modifiers.OrderBy(m => m.TraitId, StringComparer.Ordinal).ThenBy(m => m.SourceId, StringComparer.Ordinal);

    static IEnumerable<KeyValuePair<string, T>> Sorted<T>(IReadOnlyDictionary<string, T> d) => d.OrderBy(p => p.Key, StringComparer.Ordinal);

    static string Str(JsonElement o, string key, List<string> problems)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString()!;
        problems.Add($"'{key}' is missing or not a text");
        return "";
    }

    static long Long(JsonElement o, string key, List<string> problems)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.TryGetInt64(out long l)) return l;
        problems.Add($"'{key}' is missing or not a whole number");
        return 0;
    }

    static bool Bool(JsonElement o, string key, List<string> problems)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        problems.Add($"'{key}' is missing or not true or false");
        return false;
    }

    static int Int(JsonElement o, string key, List<string> problems)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.TryGetInt32(out int i)) return i;
        problems.Add($"'{key}' is missing or not a whole number in the 32-bit range");
        return 0;
    }

    static IEnumerable<JsonProperty> Obj(JsonElement o, string key, List<string> problems)
    {
        if (o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object) return v.EnumerateObject().ToList();
        problems.Add($"'{key}' is missing or not an object");
        return [];
    }

    static IEnumerable<JsonElement> Arr(JsonElement o, string key, List<string> problems)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().ToList();
        problems.Add($"'{key}' is missing or not an array");
        return [];
    }
}
