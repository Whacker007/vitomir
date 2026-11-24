// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The reader's part for <c>overrides.data</c> (a module data value by asset and JSON pointer),
/// <c>overrides.actorTempo</c> (an actor's own combat tempo), <c>overrides.clipRate</c> (one clip's play rate for one actor) and the
/// top-level <c>light</c>; their settable paths and swept values. Every check is against the game as loaded (the asset as the module
/// reads it), so a pointer that names nothing is found here with the nearest that exist; what only the module can refuse (a value out
/// of its range) is found when <c>--check</c> builds the point's world (<see cref="ScenarioCheckCommand"/>).
/// </summary>
internal sealed partial class ScenarioReader
{
    internal static readonly string[] DataOverrideFields = ["asset", "pointer", "value", "op"];
    internal static readonly string[] DataOps = ["replace", "remove", "add"];
    internal static readonly string[] ClipRateFields = ["actor", "clip", "rate"];
    internal static readonly string[] LightStepFields = ["atSeconds", "level"];

    /// <summary>The least and the most clip rate (a multiplier of the clip's play rate).</summary>
    public const double ClipRateLeast = 0.1, ClipRateMost = 10;

    /// <summary>The data overrides as resolved (for a swept value's kind).</summary>
    JsonArray dataResolved = [];

    void OverridesCj(JsonElement ov, JsonObject o)
    {
        // ---- data
        var data = (JsonArray)o["data"]!;
        dataResolved = data;
        if (ov.TryGetProperty("data", out var ds) && ds.ValueKind != JsonValueKind.Null
            && IsList(ds, "/overrides/data", "a list of { \"asset\", \"pointer\", \"value\" } (--describe data lists the assets and their pointers)"))
        {
            int i = 0;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in ds.EnumerateArray())
            {
                string dp = ScenarioPointer.Of("/overrides/data", i++);
                var entry = DataOverride(d, dp);
                if (entry is null) continue;
                string key = entry["asset"]!.GetValue<string>() + entry["pointer"]!.GetValue<string>();
                if (seen.TryGetValue(key, out int other))
                    Error(ScenarioErrorCode.BadPath, dp + "/pointer", $"{entry["pointer"]} of {entry["asset"]} is overridden twice (as /overrides/data/{other}); give one, or sweep its value");
                else seen[key] = i - 1;
                data.Add(entry);
            }
        }

        // ---- actorTempo
        var tempos = (JsonObject)o["actorTempo"]!;
        if (ov.TryGetProperty("actorTempo", out var at) && at.ValueKind != JsonValueKind.Null
            && IsObject(at, "/overrides/actorTempo", "{ \"<actor>\": tempo, ... }: an actor's own combat tempo, 0.25 to 4, in place of the global"))
            foreach (var p in at.EnumerateObject())
            {
                string pp = ScenarioPointer.Of("/overrides/actorTempo", p.Name);
                string? who = Actor(p.Name, pp);
                double? t = Number(p.Value, pp, CombatTempoOverride.Least, CombatTempoOverride.Most, "the actor's combat tempo; 1 is 1.1's own timing");
                if (who is not null && t is { } tempo) tempos[who] = tempo;
            }

        // ---- clipRate
        var rates = (JsonArray)o["clipRate"]!;
        if (ov.TryGetProperty("clipRate", out var cr) && cr.ValueKind != JsonValueKind.Null
            && IsList(cr, "/overrides/clipRate", "a list of { \"actor\", \"clip\", \"rate\" }"))
        {
            int i = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cr.EnumerateArray())
            {
                string cp = ScenarioPointer.Of("/overrides/clipRate", i++);
                if (!IsObject(c, cp, "{ \"actor\", \"clip\", \"rate\" }")) continue;
                Known(c, cp, ClipRateFields, "a clip rate");
                string? who = c.TryGetProperty("actor", out var a) ? Actor(a, cp + "/actor") : null;
                if (!Has(c, "actor")) Error(ScenarioErrorCode.WrongType, cp + "/actor", "is required: the actor whose clip it is");
                string? clip = Text(c, "clip", cp, true, $"a clip's name as the log's actions line names it (chrisAttackSword, windup:left, stitched:overhead), or an action kind: {string.Join(", ", Session.ClipRateKinds)}");
                if (clip is { Length: 0 }) { Error(ScenarioErrorCode.WrongType, cp + "/clip", "is empty: a clip's name or an action kind"); clip = null; }
                double? rate = Number(c, "rate", cp, ClipRateLeast, ClipRateMost, "a multiplier of the clip's play rate, on top of the tempo");
                if (!Has(c, "rate")) Error(ScenarioErrorCode.WrongType, cp + "/rate", Inv($"is required: a number {ClipRateLeast} to {ClipRateMost} (1: as it is; 1.2: 20 % faster)"));
                if (who is null || clip is null || rate is null) continue;
                if (!seen.Add(who + "\n" + clip)) { Error(ScenarioErrorCode.BadPath, cp + "/clip", $"{who}'s clip \"{clip}\" is given a rate twice"); continue; }
                rates.Add(new JsonObject { ["actor"] = who, ["clip"] = clip, ["rate"] = rate });
            }
        }
    }

    /// <summary>One <c>overrides.data</c> entry checked against the asset as the module reads it, resolved (null when refused).</summary>
    JsonObject? DataOverride(JsonElement d, string dp)
    {
        if (!IsObject(d, dp, "{ \"asset\": \"combat/weapons.json\", \"pointer\": \"/activeWindow/shareOfPeak\", \"value\": 0.4 }")) return null;
        Known(d, dp, DataOverrideFields, "a data override");
        string? assetName = Text(d, "asset", dp, true, "a data asset's path under the module's Data/ (--describe data)");
        string? pointer = Text(d, "pointer", dp, true, "an RFC 6901 JSON pointer into the asset (/activeWindow/shareOfPeak; --describe data lists every one)");
        string op = Word(d, "op", dp, DataOps, "a data override's operation") ?? "replace";
        bool hasValue = d.TryGetProperty("value", out var value);
        if (op == "remove" && hasValue) Error(ScenarioErrorCode.BadPath, dp + "/value", "\"op\": \"remove\" takes no value");
        if (op != "remove" && !hasValue) Error(ScenarioErrorCode.WrongType, dp + "/value", "is required: the new value, JSON of the same kind as the value it replaces");
        if (assetName is null || pointer is null) return null;
        if (!JsonPointer.TryParse(pointer, out var tokens, out string problem)) { Error(ScenarioErrorCode.BadPath, dp + "/pointer", problem); return null; }
        var entry = new JsonObject { ["asset"] = assetName, ["pointer"] = pointer, ["op"] = op };
        if (op != "remove" && hasValue) entry["value"] = Node(value);
        if (game is null) return entry;   // the form alone: the asset and the pointer are checked with the game
        var asset = DataAsset(assetName, dp + "/asset");
        if (asset is null) return null;
        entry["asset"] = asset.File;
        if (asset.Id.Path == FoePresets.Asset)
        {
            Error(ScenarioErrorCode.NotBuilt, dp + "/asset", $"{asset.File} cannot be overridden: its presets are expanded when the scenario is read, before overrides apply (give the actor's template, gear and sheet in place of the preset)");
            return null;
        }
        var root = asset.Root;
        if (tokens.Length == 0) { Error(ScenarioErrorCode.BadPath, dp + "/pointer", "\"\" is the whole asset: name a value inside it (--describe data)"); return null; }
        if (!asset.Read) Warnings.Add($"{dp}/asset: {asset.File} is not read by this build's modules (an override of it changes nothing)");
        bool exists = JsonPointer.TryFind(root, tokens, out var before);
        if (op == "add")
        {
            if (!JsonPointer.TryFind(root, tokens[..^1], out var parent) || parent is not (JsonObject or JsonArray))
            {
                MissingPointer(root, tokens[..^1], JsonPointer.Of(tokens[..^1]), asset.File, dp + "/pointer");
                return null;
            }
            if (parent is JsonObject && exists) { Error(ScenarioErrorCode.BadPath, dp + "/pointer", $"{pointer} already exists in {asset.File}: \"op\": \"add\" adds a member that is not there (to change it, leave \"op\" out)"); return null; }
        }
        else if (!exists) { MissingPointer(root, tokens, pointer, asset.File, dp + "/pointer"); return null; }
        if (op == "replace" && hasValue)
        {
            string want = JsonPointer.KindOf(before), got = JsonPointer.KindOf(entry["value"]);
            if (want != got && want != "null")
            {
                Error(ScenarioErrorCode.WrongType, dp + "/value", $"must be a{(want[0] is 'a' or 'o' ? "n" : "")} {want}, as the value it replaces at {pointer} in {asset.File} is ({Short(JsonPointer.Text(before))}), got {Kind(value)}");
                return null;
            }
        }
        if (exists) entry["before"] = before?.DeepClone();
        string status = DataLeaves.StatusAt(root, tokens);
        entry["status"] = status;
        entry["counterfactual"] = DataLeaves.IsCounterfactual(status);
        if (DataLeaves.IsCounterfactual(status))
            Warnings.Add($"{dp}: {pointer} in {asset.File} has the status {status} (not a research number): the override is recorded as a counterfactual");
        return entry;
    }

    static string Short(string text) => text.Length <= 60 ? text : text[..57] + "...";

    /// <summary>A pointer that names nothing: the error with the nearest pointers that exist (the deepest existing part's members).</summary>
    void MissingPointer(JsonNode root, IReadOnlyList<string> tokens, string pointer, string file, string at)
    {
        int n = JsonPointer.Existing(root, tokens);
        JsonPointer.TryFind(root, tokens.Take(n).ToList(), out var parent);
        string parentPointer = JsonPointer.Of(tokens.Take(n));
        var members = parent switch
        {
            JsonObject o => o.Select(kv => kv.Key).ToList(),
            JsonArray a => Enumerable.Range(0, a.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList(),
            _ => [],
        };
        string missing = n < tokens.Count ? tokens[n] : "";
        var near = ScenarioPointer.Nearest(missing, members).ToList();
        string rest = n + 1 < tokens.Count ? JsonPointer.Of(tokens.Skip(n + 1)) : "";
        var suggestions = (near.Count > 0 ? near : members.Take(5)).Select(m => parentPointer + "/" + JsonPointer.Escape(m) + rest).ToList();
        string where = parentPointer.Length == 0 ? "the asset's top" : parentPointer;
        string what = parent switch
        {
            JsonObject => $"{where} has no member \"{missing}\" (it has {string.Join(", ", members.Take(12))}{(members.Count > 12 ? $", … {members.Count} in all" : "")})",
            JsonArray a => $"{where} is a list of {a.Count}: \"{missing}\" is not an index of it (0 to {a.Count - 1})",
            _ => $"{where} is a {JsonPointer.KindOf(parent)}, which has no members",
        };
        Error(ScenarioErrorCode.BadPath, at, $"{pointer} names nothing in {file}: {what} (--describe data lists every pointer)", suggestions);
    }

    /// <summary>A data asset by the name a scenario gives (<c>combat/weapons.json</c>, <c>combat/weapons</c>, <c>enhanced:combat/weapons</c>, <c>Data/combat/weapons.json</c>).</summary>
    GameDataAsset? DataAsset(string given, string at)
    {
        string g = given.Trim().Replace('\\', '/');
        if (g.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) g = g[5..];
        string? module = null;
        int colon = g.IndexOf(':');
        if (colon > 0) { module = g[..colon]; g = g[(colon + 1)..]; }
        if (g.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) g = g[..^5];
        g = g.ToLowerInvariant();
        var hit = game!.DataAssets.FirstOrDefault(a => a.Id.Path == g && (module is null || a.Id.Module == module));
        if (hit is not null) return hit;
        var names = game.DataAssets.Select(a => a.File).ToList();
        Error(ScenarioErrorCode.UnknownName, at, $"there is no data asset \"{given}\" (a path under the module's Data/, as --describe data names it: {string.Join(", ", names.Take(8))}, …)",
            ScenarioPointer.Nearest(given.Replace('\\', '/'), names));
        return null;
    }

    // ---------------------------------------------------------------- light

    /// <summary>The top-level <c>light</c>: a level 0 to 1, or steps <c>[{ "atSeconds", "level" }]</c> from 0 s; "the time's" when not given.</summary>
    JsonNode Light(JsonElement root)
    {
        if (!root.TryGetProperty("light", out var l) || l.ValueKind == JsonValueKind.Null) return "the time's";
        if (l.ValueKind == JsonValueKind.Number) return Number(l, "/light", 0, 1, "a light level: 0 dark, 0.3 the night's, 1 the day's") is { } x ? x : "the time's";
        if (!IsList(l, "/light", "a level 0 to 1, or a list of steps [{ \"atSeconds\": 0, \"level\": 0.3 }, ...]")) return "the time's";
        var steps = new JsonArray();
        double last = -1;
        int i = 0;
        foreach (var s in l.EnumerateArray())
        {
            string sp = ScenarioPointer.Of("/light", i++);
            if (!IsObject(s, sp, "{ \"atSeconds\", \"level\" }")) continue;
            Known(s, sp, LightStepFields, "a light step");
            double? atS = Number(s, "atSeconds", sp, 0, 86_400, "s from the run's start");
            double? level = Number(s, "level", sp, 0, 1, "a light level: 0 dark, 0.3 the night's, 1 the day's");
            if (!Has(s, "atSeconds")) Error(ScenarioErrorCode.WrongType, sp + "/atSeconds", "is required: the second it begins at");
            if (!Has(s, "level")) Error(ScenarioErrorCode.WrongType, sp + "/level", "is required: the level, 0 to 1");
            if (atS is not { } t || level is not { } lv) continue;
            if (steps.Count == 0 && t != 0) Error(ScenarioErrorCode.OutOfRange, sp + "/atSeconds", Inv($"the first step begins at 0 s (the light from the start), got {t}"));
            else if (t <= last) Error(ScenarioErrorCode.OutOfRange, sp + "/atSeconds", Inv($"the steps' times must rise: {t} s is not after {last} s"));
            last = t;
            steps.Add(new JsonObject { ["atSeconds"] = t, ["level"] = lv });
        }
        if (steps.Count == 0) { Error(ScenarioErrorCode.OutOfRange, "/light", "has no steps (a level 0 to 1, or [{ \"atSeconds\": 0, \"level\": 0.3 }, ...])"); return "the time's"; }
        return steps;
    }

    // ---------------------------------------------------------------- sweeps

    /// <summary>A pointer written for a settable path (<c>/overrides/data/0/value</c>) as its dotted path (<c>overrides.data.0.value</c>).</summary>
    static string PointerPath(string pointer) =>
        JsonPointer.TryParse(pointer, out var tokens, out _) ? string.Join('.', tokens) : pointer.TrimStart('/').Replace('/', '.');

    /// <summary><c>overrides.data.&lt;i&gt;.value</c>, <c>overrides.actorTempo.&lt;actor&gt;</c>, <c>overrides.clipRate.&lt;i&gt;.rate</c> checked.</summary>
    Settable? OverrideSettable(string[] parts, string given, string at, JsonObject? doc)
    {
        var ov = doc?["overrides"];
        switch (parts[1])
        {
            case "data" when parts.Length == 4 && parts[3] == "value" && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int i):
            {
                int count = (ov?["data"] as JsonArray)?.Count ?? 0;
                if (i >= count) { Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": overrides.data has {count} entr{(count == 1 ? "y" : "ies")} (0 to {count - 1}); sweep the value of one given there"); return null; }
                if (ov!["data"]![i]!["op"]?.GetValue<string>() == "remove") { Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": /overrides/data/{i} removes its value, so it has none to sweep"); return null; }
                return new Settable($"overrides.data.{i}.value", "data", -1, i.ToString(CultureInfo.InvariantCulture));
            }
            case "actorTempo" when parts.Length == 3:
            {
                string? who = Actor(parts[2], at);
                return who is null ? null : new Settable($"overrides.actorTempo.{who}", "actorTempo", actorNames.IndexOf(who), who);
            }
            case "clipRate" when parts.Length == 4 && parts[3] == "rate" && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int i):
            {
                int count = (ov?["clipRate"] as JsonArray)?.Count ?? 0;
                if (i >= count) { Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": overrides.clipRate has {count} entr{(count == 1 ? "y" : "ies")} (0 to {count - 1}); sweep the rate of one given there"); return null; }
                return new Settable($"overrides.clipRate.{i}.rate", "clipRate", -1, i.ToString(CultureInfo.InvariantCulture));
            }
        }
        string[] shapes = ["overrides.data.<index>.value", "overrides.actorTempo.<actor>", "overrides.clipRate.<index>.rate"];
        Error(ScenarioErrorCode.BadPath, at, $"\"{given}\" is not a settable path of the overrides ({string.Join(", ", shapes)}; or as pointers: /overrides/data/0/value)", ScenarioPointer.Nearest(given, shapes));
        return null;
    }

    /// <summary>A swept value of the overrides' paths and <c>light</c>, checked for its kind.</summary>
    JsonNode? OverrideSweptValue(Settable path, JsonElement v, string at)
    {
        switch (path.Kind)
        {
            case "light": return Number(v, at, 0, 1, "a light level");
            case "actorTempo": return Number(v, at, CombatTempoOverride.Least, CombatTempoOverride.Most, "the actor's combat tempo");
            case "clipRate": return Number(v, at, ClipRateLeast, ClipRateMost, "a clip rate");
            case "data":
                int i = int.Parse(path.Key!, CultureInfo.InvariantCulture);
                var entry = i < dataResolved.Count ? dataResolved[i] : null;
                var node = Node(v);
                string want = entry?["before"] is { } b ? JsonPointer.KindOf(b) : entry?["value"] is { } nv ? JsonPointer.KindOf(nv) : JsonPointer.KindOf(node);
                if (JsonPointer.KindOf(node) != want && want != "null")
                {
                    Error(ScenarioErrorCode.BadSweep, at, $"a value of {path.Path} must be a{(want[0] is 'a' or 'o' ? "n" : "")} {want} (as {entry?["pointer"]} is in {entry?["asset"]}), got {Kind(v)}");
                    return null;
                }
                return node;
        }
        return null;
    }
}

/// <summary>A data asset's leaves and their statuses, for the reader and <c>--describe data</c>.</summary>
internal static class DataLeaves
{
    /// <summary>The status a leaf has: the nearest <c>status</c> (or <c>…_status</c>) member of an object it is in, else the asset's, else "".</summary>
    public static string StatusAt(JsonNode root, IReadOnlyList<string> tokens)
    {
        for (int k = tokens.Count; k >= 0; k--)
        {
            if (!JsonPointer.TryFind(root, tokens.Take(k).ToList(), out var n) || n is not JsonObject o) continue;
            if (o["status"] is JsonValue s && s.TryGetValue(out string? st)) return st;
            foreach (var (key, val) in o)
                if (key.EndsWith("_status", StringComparison.Ordinal) && val is JsonValue sv && sv.TryGetValue(out string? st2)) return st2;
        }
        return "";
    }

    /// <summary>
    /// Whether a value of this status is more than a research number — an approved or agreed design, a rule — so that overriding it is a
    /// counterfactual (recorded and flagged): any status that names none of SANDBOX, OPEN, PROVISIONAL, UNCONFIRMED, UNDECIDED, DEFERRED.
    /// </summary>
    public static bool IsCounterfactual(string status) =>
        status.Length > 0 && !new[] { "SANDBOX", "OPEN", "PROVISIONAL", "UNCONFIRMED", "UNDECIDED", "DEFERRED", "NOT_NATIVE" }.Any(w => status.Contains(w, StringComparison.Ordinal));
}
