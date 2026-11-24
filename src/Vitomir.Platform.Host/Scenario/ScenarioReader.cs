// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The scenario's reader: reads a <c>vitomir-scenario/1</c> file into its
/// <b>resolved</b> form — a JSON tree with every default made explicit and every name in the game's own spelling — collecting
/// every error with its JSON pointer, a stable code and the nearest names. It reads the arena's way (comments, trailing commas,
/// strict: nothing unknown is passed over). The game's names are checked when a <see cref="ScenarioGame"/> is given; without one
/// only the form is.
/// </summary>
internal sealed partial class ScenarioReader(string folder, ScenarioGame? game)
{
    public readonly List<ScenarioError> Errors = [];
    readonly List<string> actorNames = [];

    // The fields of each object, in the order the resolved form writes them.
    internal static readonly string[] TopFields = ["format", "title", "notes", "kind", "ground", "time", "light", "actors", "contacts", "triggers", "objectives", "overrides", "stop", "runs", "seed", "swap", "sweep", "sweepMode", "measures", "assertions", "outputs"];
    internal static readonly string[] ActorFields = ["name", "preset", "template", "sheet", "health", "blood", "movement", "gear", "weapon", "shield", "armor", "place", "facing", "foe", "side", "slots", "control"];
    internal static readonly string[] GearFields = ["weapon", "shield", "armor", "ranged"];
    internal static readonly string[] SheetFields = ["template", "file", "values", "powers"];
    internal static readonly string[] ControlFields = ["ai", "mute", "script", "tracks", "then", "playerInput"];
    internal static readonly string[] RelativePlaceFields = ["from", "distanceMetres", "bearingDegrees"];
    internal static readonly string[] SelectorFields = ["event", "by", "on", "where", "fromSeconds", "toSeconds", "fromMarker", "toMarker", "sum"];
    internal static readonly string[] TriggerFields = ["label", "when", "do", "repeat", "cooldownSeconds", "afterSeconds", "beforeSeconds"];
    internal static readonly string[] OverrideFields = ["switches", "combatTempo", "data", "actorTempo", "clipRate"];
    internal static readonly string[] StopFields = ["afterSeconds", "onHarnessStops", "when", "onObjectives"];   // onObjectives
    internal static readonly string[] SweepFields = ["set", "values", "from", "to", "step"];
    internal static readonly string[] OutputFields = ["events", "tracePolicy", "traceRuns", "traceBudgetMegabytes", "traceBudgetRuns", "ticks", "csv", "summaryPage", "wideBouts", "tapes", "pictures", "film",
        "decisions"];
    internal static readonly string[] AssertionExtras = ["name", "scope", "passShare", "statistic", "points", "over", "direction", "atSeconds", "atEvent", "nth"];

    /// <summary>The actors' names, in order (once read).</summary>
    public IReadOnlyList<string> Actors => actorNames;

    // ---------------------------------------------------------------- errors and fields

    public void Error(ScenarioErrorCode code, string pointer, string message, IReadOnlyList<string>? suggestions = null) =>
        Errors.Add(new ScenarioError(code, pointer, message, suggestions ?? []));

    static string Kind(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => $"the text {e.GetRawText()}",
        JsonValueKind.Number => $"the number {e.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => e.GetRawText(),
        JsonValueKind.Null => "null",
        _ => "nothing",
    };

    /// <summary>Every member of <paramref name="o"/> must be one of <paramref name="fields"/>; each other is an <see cref="ScenarioErrorCode.UnknownField"/> with the nearest.</summary>
    public void Known(JsonElement o, string ptr, IReadOnlyList<string> fields, string what)
    {
        foreach (var p in o.EnumerateObject())
            if (!fields.Contains(p.Name))
            {
                var near = ScenarioPointer.Nearest(p.Name, fields);
                Error(ScenarioErrorCode.UnknownField, ScenarioPointer.Of(ptr, p.Name), $"{what} has no field \"{p.Name}\"; it takes {ScenarioPointer.Words(fields)}", near);
            }
    }

    public static bool Has(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

    public string? Text(JsonElement o, string name, string ptr, bool required = false, string? what = null)
    {
        string at = ScenarioPointer.Of(ptr, name);
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required) Error(ScenarioErrorCode.WrongType, at, $"is required: {what ?? "text"}");
            return null;
        }
        if (v.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.WrongType, at, $"must be text{(what is null ? "" : $" ({what})")}, got {Kind(v)}"); return null; }
        return v.GetString();
    }

    public double? Number(JsonElement v, string at, double lo, double hi, string unit)
    {
        if (v.ValueKind != JsonValueKind.Number) { Error(ScenarioErrorCode.WrongType, at, $"must be a number{Unit(unit)}, got {Kind(v)}"); return null; }
        double x = v.GetDouble();
        if (!(x >= lo && x <= hi)) { Error(ScenarioErrorCode.OutOfRange, at, Inv($"must be {Range(lo, hi)}{Unit(unit)}, got {x}")); return null; }
        return x;
    }

    public double? Number(JsonElement o, string name, string ptr, double lo, double hi, string unit) =>
        o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? Number(v, ScenarioPointer.Of(ptr, name), lo, hi, unit) : null;

    public long? Whole(JsonElement v, string at, long lo, long hi, string unit = "")
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out long x)) { Error(ScenarioErrorCode.WrongType, at, $"must be a whole number{Unit(unit)}, got {Kind(v)}"); return null; }
        if (x < lo || x > hi) { Error(ScenarioErrorCode.OutOfRange, at, Inv($"must be {Range(lo, hi)}{Unit(unit)}, got {x}")); return null; }
        return x;
    }

    public long? Whole(JsonElement o, string name, string ptr, long lo, long hi, string unit = "") =>
        o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? Whole(v, ScenarioPointer.Of(ptr, name), lo, hi, unit) : null;

    public bool? Flag(JsonElement o, string name, string ptr)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        Error(ScenarioErrorCode.WrongType, ScenarioPointer.Of(ptr, name), $"must be true or false, got {Kind(v)}");
        return null;
    }

    /// <summary>A word from a closed list (case aside), in the list's spelling; else <see cref="ScenarioErrorCode.OutOfRange"/> with the nearest.</summary>
    public string? Word(JsonElement v, string at, IReadOnlyList<string> words, string what)
    {
        if (v.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.WrongType, at, $"must be {what}: {ScenarioPointer.Words(words)}, got {Kind(v)}"); return null; }
        string s = v.GetString()!;
        var hit = words.FirstOrDefault(w => w.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (hit is null) { Error(ScenarioErrorCode.OutOfRange, at, $"\"{s}\" is not {what}; it is one of {ScenarioPointer.Words(words)}", ScenarioPointer.Nearest(s, words)); return null; }
        return hit;
    }

    public string? Word(JsonElement o, string name, string ptr, IReadOnlyList<string> words, string what) =>
        o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? Word(v, ScenarioPointer.Of(ptr, name), words, what) : null;

    /// <summary>A name from a list the game gives (case aside), in the game's spelling; else <see cref="ScenarioErrorCode.UnknownName"/> with the nearest.</summary>
    public string? Name(string given, string at, IEnumerable<string> knownNames, string what)
    {
        var known = knownNames as IReadOnlyCollection<string> ?? [.. knownNames];
        var hit = known.FirstOrDefault(k => k.Equals(given, StringComparison.Ordinal)) ?? known.FirstOrDefault(k => k.Equals(given, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        Error(ScenarioErrorCode.UnknownName, at, $"there is no {what} \"{given}\"{(known.Count is > 0 and <= 12 ? $" (the {what}s: {string.Join(", ", known)})" : "")}", ScenarioPointer.Nearest(given, known));
        return null;
    }

    /// <summary>An actor named in a step, a selector or a path; <see cref="ScenarioErrorCode.UnknownName"/> with the nearest when it is none.</summary>
    public string? Actor(JsonElement v, string at)
    {
        if (v.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.WrongType, at, $"must be an actor's name, got {Kind(v)}"); return null; }
        return Actor(v.GetString()!, at);
    }

    public string? Actor(string name, string at) => Name(name, at, actorNames, "actor");

    public bool IsObject(JsonElement v, string at, string what)
    {
        if (v.ValueKind == JsonValueKind.Object) return true;
        Error(ScenarioErrorCode.WrongType, at, $"must be an object ({what}), got {Kind(v)}");
        return false;
    }

    public bool IsList(JsonElement v, string at, string what)
    {
        if (v.ValueKind == JsonValueKind.Array) return true;
        Error(ScenarioErrorCode.WrongType, at, $"must be a list ({what}), got {Kind(v)}");
        return false;
    }

    public void NotBuilt(string at, string what, string batch) =>
        Error(ScenarioErrorCode.NotBuilt, at, $"{what} is not built in this build yet; it comes with {batch}");

    static string Unit(string unit) => unit.Length == 0 ? "" : $" ({unit})";
    static string Range(double lo, double hi) =>
        hi >= double.MaxValue / 2 || hi >= long.MaxValue / 2 ? Inv($"{lo} or more") : lo <= double.MinValue / 2 || lo <= long.MinValue / 2 ? Inv($"{hi} or less") : Inv($"{lo} to {hi}");
    internal static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>A JSON element as a node of the resolved tree (copied).</summary>
    public static JsonNode? Node(JsonElement e) => JsonNode.Parse(e.GetRawText());

    // ---------------------------------------------------------------- the document

    /// <summary>Reads the whole file into its resolved form (null when it is not a JSON object or not this format).</summary>
    public JsonObject? Document(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.NotJson, "", $"the file must be a JSON object, got {Kind(root)}"); return null; }
        // format first: nothing else is read without it.
        if (!root.TryGetProperty("format", out var fv)) { Error(ScenarioErrorCode.UnknownFormat, "/format", $"is required, first: \"format\": \"{ScenarioWords.Format}\""); return null; }
        if (fv.ValueKind != JsonValueKind.String || fv.GetString() != ScenarioWords.Format)
        {
            Error(ScenarioErrorCode.UnknownFormat, "/format", $"this build reads \"{ScenarioWords.Format}\", not {fv.GetRawText()}");
            return null;
        }
        Known(root, "", TopFields, "the scenario");
        var doc = new JsonObject { ["format"] = ScenarioWords.Format };
        doc["title"] = Text(root, "title", "") ?? "";
        doc["notes"] = Text(root, "notes", "") ?? "";
        string kind = Word(root, "kind", "", ScenarioWords.Kinds, "a scenario's kind") ?? "fight";
        doc["kind"] = kind;
        doc["ground"] = Ground(root);
        doc["time"] = Word(root, "time", "", ScenarioWords.Times, "a time") ?? "night";
        doc["light"] = Light(root);   // the light over the time's (ScenarioReader.Overrides.cs)

        doc["actors"] = ActorsOf(root);
        if (ContactsOf(root, kind, (JsonArray)doc["actors"]!) is { } contacts) doc["contacts"] = contacts;   // the fixed contacts (a contact scenario only)
        doc["triggers"] = TriggersOf(root);
        if (ObjectivesOf(root, (JsonArray)doc["actors"]!) is { } objectives) doc["objectives"] = objectives;   // only when given
        if (kind == "contact") ContactKindRules(root, doc);   // no native blows beside them
        doc["overrides"] = Overrides(root);
        doc["stop"] = Stop(root);
        // With objectives, what they do to the run is written (default none); "first" or "all" asks for objectives.
        if (doc["objectives"] is JsonArray { Count: > 0 }) { if (!((JsonObject)doc["stop"]!).ContainsKey("onObjectives")) doc["stop"]!["onObjectives"] = "none"; }
        else if (doc["stop"]!["onObjectives"] is JsonValue oo && oo.GetValue<string>() != "none" && !root.TryGetProperty("objectives", out _))
            Error(ScenarioErrorCode.BadObjective, "/stop/onObjectives", "ends the run on objectives, and the scenario gives none: add \"objectives\", or leave onObjectives out");
        doc["runs"] = Whole(root, "runs", "", 1, 1_000_000) ?? 10;
        doc["seed"] = Whole(root, "seed", "", 0, long.MaxValue) ?? 1;
        if (SwapOf(root, (JsonArray)doc["actors"]!) is { } swap) doc["swap"] = swap;   // the paired swap, only when given
        var (sweep, mode) = Sweep(root, doc);
        doc["sweep"] = sweep;
        doc["sweepMode"] = mode;
        doc["measures"] = Measures(root);
        doc["assertions"] = Assertions(root, (JsonArray)doc["sweep"]!, (JsonArray)doc["measures"]!);
        doc["outputs"] = Outputs(root);
        return doc;
    }

    // ---------------------------------------------------------------- ground and time

    JsonNode Ground(JsonElement root)
    {
        if (!root.TryGetProperty("ground", out var g) || g.ValueKind == JsonValueKind.Null) return ScenarioGround.FreeYard;
        if (g.ValueKind == JsonValueKind.String)
        {
            string? name = Word(g, "/ground", [ScenarioGround.FreeYard, ScenarioGround.Range], "a named ground");
            return name == ScenarioGround.Range ? ScenarioGround.Range : ScenarioGround.FreeYard;   // the range, built in code
        }
        if (!IsObject(g, "/ground", "\"free-yard\", or { \"level\": \"<package folder>\", \"start\": \"<start name>\" }")) return ScenarioGround.FreeYard;
        Known(g, "/ground", ["level", "start"], "the ground");
        string? level = Text(g, "level", "/ground", true, "a level package's folder");
        string? start = Text(g, "start", "/ground");
        var o = new JsonObject();
        if (level is not null)
        {
            string full = Path.GetFullPath(Path.Combine(folder, level));
            if (!File.Exists(Path.Combine(full, "level.json"))) Error(ScenarioErrorCode.LevelNotFound, "/ground/level", $"{full} is not a level package (it has no level.json)");
            o["level"] = full;
        }
        if (start is not null) o["start"] = start;
        return o;
    }
}

/// <summary>The named grounds (section 4.3).</summary>
public static class ScenarioGround
{
    public const string FreeYard = "free-yard";
    public const string Range = "range";

    /// <summary>The free yard's bounds on the proving ground (x 20 to 56, y −15 to 21; the level's frame).</summary>
    public static readonly (float X0, float X1, float Y0, float Y1) Yard = (20f, 56f, -15f, 21f);

    /// <summary>The grounds as <c>--describe grounds</c> gives them.</summary>
    public static readonly (string Name, string Status, string Meaning)[] All =
    [
        (FreeYard, "built", "the proving ground's free yard (VITOMIR_COURSE_LEVEL or --level): x 20 to 56, y -15 to 21, the level's frame (+Z up); its structures stand from x 32 at y -12 to -6 (a 3 m wall, x 32 to 34), 0 to 6 (a 4.5 m wall, x 32 to 34) and 12 to 18 (a 6 m house, x 32 to 38); y = 9 is a clear straight line from x 20 to 56; actors with no place stand round (40, 3), 2 m out"),
        (Range, "built", "a flat, level, open lane built in code (no level file, no walls): x 0 to 170 m, y -6 to 24 m, the floor at z 0, its middle line y = 9 (the free yard's clear line carried on); a free place; actors with no place stand round (40, 3) as on the free yard; navigation one walkable polygon over it; not drawn (the renderer draws cooked levels only: no picture or film is made on it; a plan view from a run's tick trace shows a 170 by 30 m plain). For shots to 100 m and more: e.g. an archer at [21, 9] and a target 10 to 140 m along +X"),
        ("{ \"level\": \"<package folder>\", \"start\": \"<start name>\" }", "built", "any cooked level package; places are in its frame; actors with no place stand round the start (2 m out), or round the level's first start"),
    ];
}
