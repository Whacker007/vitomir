// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

internal sealed partial class ScenarioReader
{
    /// <summary>What the reader warns of without refusing (a field this build's runner does not write yet, …).</summary>
    public readonly List<string> Warnings = [];

    /// <summary>The sweep's dimensions parsed, in order (the compiler sets their values at each point).</summary>
    internal readonly List<Settable> SweepPaths = [];

    // ---------------------------------------------------------------- overrides

    JsonObject Overrides(JsonElement root)
    {
        var o = new JsonObject { ["switches"] = new JsonArray(), ["combatTempo"] = "the module's", ["data"] = new JsonArray(), ["actorTempo"] = new JsonObject(), ["clipRate"] = new JsonArray() };   // the last three
        if (!root.TryGetProperty("overrides", out var ov) || ov.ValueKind == JsonValueKind.Null) return o;
        if (!IsObject(ov, "/overrides", "{ \"switches\", \"combatTempo\" }")) return o;
        Known(ov, "/overrides", OverrideFields, "the overrides");
        if (ov.TryGetProperty("switches", out var sw) && sw.ValueKind != JsonValueKind.Null && IsList(sw, "/overrides/switches", "the game's switches without dashes (\"skill-contest\")"))
        {
            var list = (JsonArray)o["switches"]!;
            int i = 0;
            foreach (var x in sw.EnumerateArray())
            {
                string xp = ScenarioPointer.Of("/overrides/switches", i++);
                if (x.ValueKind != JsonValueKind.String || x.GetString()!.TrimStart('-').Length == 0) { Error(ScenarioErrorCode.WrongType, xp, $"must be a switch's name, got {Kind(x)}"); continue; }
                string name = x.GetString()!.TrimStart('-');
                if (game is not null) name = Name(name, xp, game.Switches.Keys, "switch") ?? name;
                if (name == "sandbox") { Error(ScenarioErrorCode.OutOfRange, xp, "\"sandbox\" is the command line's (--sandbox), not the scenario's: the same file runs under either profile"); continue; }
                if (!list.Any(s => s!.GetValue<string>() == name)) list.Add(name);
            }
        }
        if (Number(ov, "combatTempo", "/overrides", CombatTempoOverride.Least, CombatTempoOverride.Most, "the global combat tempo") is { } tempo) o["combatTempo"] = tempo;
        OverridesCj(ov, o);   // data, actorTempo, clipRate (ScenarioReader.Overrides.cs)
        return o;
    }

    // ---------------------------------------------------------------- stop

    JsonObject Stop(JsonElement root)
    {
        var d = new ScenarioStop();
        var o = new JsonObject { ["afterSeconds"] = d.AfterSeconds, ["onHarnessStops"] = "first", ["when"] = new JsonArray() };
        if (!root.TryGetProperty("stop", out var st) || st.ValueKind == JsonValueKind.Null) return o;
        if (!IsObject(st, "/stop", "{ \"afterSeconds\", \"onHarnessStops\", \"when\" }")) return o;
        Known(st, "/stop", StopFields, "the stop");
        if (Number(st, "afterSeconds", "/stop", 0.1, 3600, "s") is { } after) o["afterSeconds"] = after;
        if (Word(st, "onHarnessStops", "/stop", ScenarioWords.HarnessStops, "what a harness stop does") is { } hs) o["onHarnessStops"] = hs;
        if (Word(st, "onObjectives", "/stop", OnObjectivesWords, "what the objectives do to the run") is { } oo) o["onObjectives"] = oo;
        if (st.TryGetProperty("when", out var when) && when.ValueKind != JsonValueKind.Null && IsList(when, "/stop/when", "a list of conditions, each a value with a comparator"))
        {
            int i = 0;
            foreach (var c in when.EnumerateArray())
                if (Condition(c, ScenarioPointer.Of("/stop/when", i++), FormUse.Stop) is { } cond) ((JsonArray)o["when"]!).Add(cond);
        }
        return o;
    }

    // ---------------------------------------------------------------- sweeps

    (JsonArray, string) Sweep(JsonElement root, JsonObject doc)
    {
        var dims = new JsonArray();
        string mode = Word(root, "sweepMode", "", ScenarioWords.SweepModes, "a sweep mode") ?? "grid";
        if (!root.TryGetProperty("sweep", out var sw) || sw.ValueKind == JsonValueKind.Null) return (dims, mode);
        if (!IsList(sw, "/sweep", "a list of dimensions, each { \"set\": \"<path>\", \"values\": [...] }")) return (dims, mode);
        int i = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in sw.EnumerateArray())
        {
            string dp = ScenarioPointer.Of("/sweep", i++);
            if (!IsObject(d, dp, "{ \"set\", \"values\" } or { \"set\", \"from\", \"to\", \"step\" }")) continue;
            Known(d, dp, SweepFields, "a sweep's dimension");
            if (!d.TryGetProperty("set", out var set) || set.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.BadSweep, dp + "/set", "is required: the settable path it sweeps (--describe settable)"); continue; }
            var path = SettablePath(set.GetString()!, dp + "/set", doc);
            if (path is null) continue;
            if (!paths.Add(path.Path)) { Error(ScenarioErrorCode.BadSweep, dp + "/set", $"\"{path.Path}\" is swept twice"); continue; }
            var values = new JsonArray();
            if (d.TryGetProperty("values", out var vs) && vs.ValueKind != JsonValueKind.Null)
            {
                if (Has(d, "from") || Has(d, "to") || Has(d, "step")) Error(ScenarioErrorCode.BadSweep, dp, "a dimension gives \"values\" or \"from\"/\"to\"/\"step\", not both");
                if (!IsList(vs, dp + "/values", "the values")) continue;
                int k = 0;
                foreach (var v in vs.EnumerateArray()) if (SweptValue(path, v, ScenarioPointer.Of(dp + "/values", k++)) is { } ok) values.Add(ok);
            }
            else if (Has(d, "from"))
            {
                double? from = Number(d, "from", dp, double.MinValue, double.MaxValue, ""), to = Number(d, "to", dp, double.MinValue, double.MaxValue, "");
                double step = Number(d, "step", dp, 1e-9, double.MaxValue, "") ?? 1;
                if (!Has(d, "to")) Error(ScenarioErrorCode.BadSweep, dp + "/to", "is required with \"from\"");
                if (from is { } f && to is { } t)
                {
                    int n = (int)Math.Floor(Math.Abs(t - f) / step + 1e-9) + 1;
                    if (n > 10_000) { Error(ScenarioErrorCode.BadSweep, dp, "has more than 10,000 values"); continue; }
                    for (int k = 0; k < n; k++)
                    {
                        double x = Math.Round(f + Math.Sign(t - f) * k * step, 9);
                        if (SweptValue(path, JsonDocument.Parse(x.ToString("R", CultureInfo.InvariantCulture)).RootElement, ScenarioPointer.Of(dp, "from")) is { } ok) values.Add(ok);
                    }
                }
            }
            if (values.Count == 0) { Error(ScenarioErrorCode.BadSweep, dp, "has no values (\"values\": [...], or \"from\", \"to\", \"step\")"); continue; }
            dims.Add(new JsonObject { ["set"] = path.Path, ["values"] = values });
            SweepPaths.Add(path);
        }
        if (mode == "zip" && dims.Count > 1 && dims.Select(x => x!["values"]!.AsArray().Count).Distinct().Count() > 1)
            Error(ScenarioErrorCode.BadSweep, "/sweepMode", $"\"zip\" walks the dimensions side by side, so they need as many values each ({string.Join(", ", dims.Select(x => x!["values"]!.AsArray().Count))})");
        return (dims, mode);
    }

    /// <summary>A settable path parsed (section 4.9): its kind, its actor (index), its key, and the path in the scenario's own spelling.</summary>
    internal sealed record Settable(string Path, string Kind, int Actor, string? Key);

    /// <summary>A settable path checked against the actors and the game (<see cref="ScenarioErrorCode.BadPath"/>).</summary>
    internal Settable? SettablePath(string given, string at, JsonObject? doc)
    {
        if (given.StartsWith('/')) given = PointerPath(given);   // a sweep may name a field by its pointer (/overrides/data/0/value)
        var parts = given.Split('.');
        string shapes = string.Join(", ", ScenarioWords.Settable.Where(s => s.Status == "built").Select(s => s.Path));
        if (given == "time") return new Settable("time", "time", -1, null);
        if (given == "light") return new Settable("light", "light", -1, null);
        if (parts[0] == "contacts") return ContactPath(given, at, doc);   // a fixed contact's field
        if (parts[0] == "overrides")
        {
            if (parts.Length == 2 && parts[1] == "combatTempo") return new Settable(given, "combatTempo", -1, null);
            if (parts.Length == 3 && parts[1] == "switches")
            {
                string name = game is null ? parts[2] : Name(parts[2], at, game.Switches.Keys, "switch") ?? parts[2];
                return new Settable($"overrides.switches.{name}", "switch", -1, name);
            }
            if (parts.Length >= 2 && parts[1] is "data" or "actorTempo" or "clipRate") return OverrideSettable(parts, given, at, doc);
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\" is not a settable path ({shapes})", ScenarioPointer.Nearest(given, ScenarioWords.Settable.Select(s => s.Path)));
            return null;
        }
        int actor = actorNames.FindIndex(n => n.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (actor < 0)
        {
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\" starts with no actor's name, \"overrides\" or \"time\" (the actors: {string.Join(", ", actorNames)})", ScenarioPointer.Nearest(parts[0], [.. actorNames, "overrides", "time"]));
            return null;
        }
        string a = actorNames[actor];
        string rest = string.Join('.', parts.Skip(1));
        var actorNode = doc?["actors"]?[actor]?.AsObject();
        switch (parts.Length)
        {
            case 2 when parts[1] is "health" or "blood":
                return new Settable($"{a}.{parts[1]}", parts[1], actor, null);
            case 3 when parts[1] == "gear" && parts[2] is "weapon" or "armor":
                return new Settable($"{a}.gear.{parts[2]}", "gear." + parts[2], actor, null);
            case 3 when parts[1] == "place" && parts[2] is "distanceMetres" or "bearingDegrees":
                if (actorNode?["place"] is not JsonObject)
                {
                    Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": {a}'s place is not relative ({{ \"from\", \"distanceMetres\", \"bearingDegrees\" }}), so it has no {parts[2]} to set");
                    return null;
                }
                return new Settable($"{a}.place.{parts[2]}", "place." + parts[2], actor, null);
            case 3 when parts[1] == "ai":
                if (BrainNumbers.Find(parts[2]) is null)
                {
                    Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": the duelling brain has no number \"{parts[2]}\" (--describe brain)", ScenarioPointer.Nearest(parts[2], BrainNumbers.Names).Select(n => $"{a}.ai.{n}").ToList());
                    return null;
                }
                if (actorNode is not null && actorNode["control"]?["ai"]?["kind"]?.GetValue<string>() != "duel")
                {
                    Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": {a}'s AI is not the duelling brain (\"ai\": \"duel\"), so it has no numbers to set");
                    return null;
                }
                return new Settable($"{a}.ai.{parts[2]}", "ai", actor, parts[2]);
            case 4 when parts[1] == "sheet" && parts[2] == "values":
            {
                string id = game is null ? parts[3] : Name(parts[3], at, game.SheetValueIds, "trait") ?? parts[3];
                return new Settable($"{a}.sheet.values.{id}", "sheet.values", actor, id);
            }
            case 5 when parts[1] == "sheet" && parts[2] == "powers" && parts[4] == "rank":
            {
                string id = game is null ? parts[3] : Name(parts[3], at, game.Powers.Keys, "power") ?? parts[3];
                return new Settable($"{a}.sheet.powers.{id}.rank", "sheet.powers", actor, id);
            }
        }
        var actorShapes = ScenarioWords.Settable.Where(s => s.Path.StartsWith("<actor>.", StringComparison.Ordinal)).Select(s => s.Path[8..]).ToList();
        Error(ScenarioErrorCode.BadPath, at, $"\"{given}\" is not a settable path of {a} ({string.Join(", ", actorShapes)})", ScenarioPointer.Nearest(rest, actorShapes).Select(s => $"{a}.{s}").ToList());
        return null;
    }

    /// <summary>A swept value checked for its path's kind.</summary>
    JsonNode? SweptValue(Settable path, JsonElement v, string at)
    {
        switch (path.Kind)
        {
            case "time": return Word(v, at, ScenarioWords.Times, "a time");
            case "light" or "data" or "actorTempo" or "clipRate": return OverrideSweptValue(path, v, at);
            case "combatTempo": return Number(v, at, CombatTempoOverride.Least, CombatTempoOverride.Most, "the global combat tempo");
            case "switch":
                if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
                Error(ScenarioErrorCode.BadSweep, at, $"a switch's values are true or false (on, off), got {Kind(v)}");
                return null;
            case "health": return Whole(v, at, 1, 1000, "Health");
            case "blood": return Whole(v, at, 0, 1_000_000, "Blood");
            case "sheet.values": return Whole(v, at, int.MinValue, int.MaxValue);
            case "sheet.powers": return Whole(v, at, 0, 10, "rank");
            case "place.distanceMetres": return Number(v, at, 0, 1000, "m");
            case "place.bearingDegrees": return Number(v, at, -3600, 3600, "degrees");
            case "contact": return ContactSweptValue(path.Path, path.Key![(path.Key!.IndexOf(':') + 1)..], v, at);
            case "gear.weapon":
                if (v.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.BadSweep, at, $"a weapon's values are 1.1 weapons' names, got {Kind(v)}"); return null; }
                return game is null ? v.GetString() : Name(v.GetString()!, at, game.Weapons, "1.1 weapon");
            case "gear.armor":
                if (v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) { Error(ScenarioErrorCode.BadSweep, at, $"an armour's values are lists of pieces ([\"mail_shirt\"], []), got {Kind(v)}"); return null; }
                return new JsonArray([.. v.EnumerateArray().Select(x => (JsonNode?)(game is null ? x.GetString() : Name(x.GetString()!, at, game.Armor, "armour piece") ?? x.GetString()))]);
            case "ai":
                var n = BrainNumbers.Find(path.Key!)!;
                return n.Kind switch
                {
                    BrainNumberKind.Number => Number(v, at, n.Least, n.Most, n.Unit),
                    BrainNumberKind.Count => Whole(v, at, (long)n.Least, (long)n.Most, n.Unit),
                    BrainNumberKind.Flag when v.ValueKind is JsonValueKind.True or JsonValueKind.False => v.GetBoolean(),
                    BrainNumberKind.Pace when v.ValueKind == JsonValueKind.String && v.GetString() == "none" || v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 => Node(v),
                    _ => SweepWrong(at, n.Name, v),
                };
        }
        return null;
    }

    JsonNode? SweepWrong(string at, string name, JsonElement v)
    {
        Error(ScenarioErrorCode.BadSweep, at, $"is not a value of the brain number {name}, got {Kind(v)}");
        return null;
    }

    // ---------------------------------------------------------------- measures

    JsonArray Measures(JsonElement root)
    {
        var list = new JsonArray();
        if (!root.TryGetProperty("measures", out var ms) || ms.ValueKind == JsonValueKind.Null) return list;
        if (!IsList(ms, "/measures", "a list of named measures")) return list;
        var names = new List<string>();
        int i = 0;
        foreach (var m in ms.EnumerateArray())
        {
            string mp = ScenarioPointer.Of("/measures", i++);
            if (!IsObject(m, mp, "{ \"name\", <one value form> }")) continue;
            Known(m, mp, ["name", .. ScenarioWords.ValueForms, "atEvent", "nth"], "a measure");
            string? name = Text(m, "name", mp, true, "the measure's name");
            if (name is not null && (name.Length == 0 || names.Contains(name) || name.Contains('.')))
                Error(ScenarioErrorCode.BadMeasure, mp + "/name", $"\"{name}\" is empty, repeated, or holds a dot (the built-in measures are <actor>.<name>)");
            var o = new JsonObject { ["name"] = name };
            ValueForm(m, mp, FormUse.Measure, o, names);
            if (name is not null) names.Add(name);
            list.Add(o);
        }
        return list;
    }

    // ---------------------------------------------------------------- assertions

    JsonArray Assertions(JsonElement root, JsonArray sweep, JsonArray measures)
    {
        var list = new JsonArray();
        if (!root.TryGetProperty("assertions", out var asx) || asx.ValueKind == JsonValueKind.Null) return list;
        if (!IsList(asx, "/assertions", "a list of assertions")) return list;
        var measureNames = measures.Select(m => m?["name"]?.GetValue<string>()).Where(n => n is not null).Select(n => n!).ToList();
        var swept = sweep.Select(d => d!["set"]!.GetValue<string>()).ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (var a in asx.EnumerateArray())
        {
            string ap = ScenarioPointer.Of("/assertions", i++);
            if (!IsObject(a, ap, "{ \"name\", <one value form>, <one comparator>, \"scope\" }")) continue;
            Known(a, ap, [.. AssertionExtras, .. ScenarioWords.ValueForms, .. ScenarioWords.Comparators], "an assertion");
            string name = Text(a, "name", ap, true, "the assertion's name") ?? $"assertion {i - 1}";
            if (!names.Add(name)) Error(ScenarioErrorCode.BadAssertion, ap + "/name", $"\"{name}\" is the name of another assertion");
            var o = new JsonObject { ["name"] = name };
            ValueForm(a, ap, FormUse.Assertion, o, measureNames);
            string scope = Word(a, "scope", ap, ScenarioWords.Scopes, "a scope") ?? "eachRun";
            bool hasComparator = Comparator(a, ap, FormUse.Assertion, o, required: scope != "trend");
            if (scope == "trend" && hasComparator) Error(ScenarioErrorCode.BadAssertion, ap, "a trend takes no comparator (its \"direction\" says what passes)");
            o["scope"] = scope;
            if (scope == "eachRun") o["passShare"] = Number(a, "passShare", ap, 0, 1, "share of runs") ?? 1.0;
            else if (Has(a, "passShare")) Error(ScenarioErrorCode.BadAssertion, ap + "/passShare", "goes with \"scope\": \"eachRun\"");
            if (Word(a, "statistic", ap, ScenarioWords.Statistics, "a statistic") is { } stat)
            {
                if (scope is "eachRun" or "anyRun") Error(ScenarioErrorCode.BadAssertion, ap + "/statistic", "goes with \"scope\": \"aggregate\" or \"trend\" (each run's value is compared as it is)");
                o["statistic"] = stat;
            }
            else if (scope is "aggregate" or "trend") o["statistic"] = "the measure's own";
            if (scope == "trend")
            {
                if (Text(a, "over", ap, true, "a swept path") is { } over)
                {
                    if (over.StartsWith('/')) over = PointerPath(over);   // a swept path by its pointer
                    var hit = swept.FirstOrDefault(s => s.Equals(over, StringComparison.OrdinalIgnoreCase));
                    if (hit is null) Error(ScenarioErrorCode.BadPath, ap + "/over", $"\"{over}\" is not a swept path (the sweep's: {(swept.Count == 0 ? "none" : string.Join(", ", swept))})", ScenarioPointer.Nearest(over, swept));
                    o["over"] = hit ?? over;
                }
                if (!Has(a, "direction")) Error(ScenarioErrorCode.BadAssertion, ap + "/direction", $"is required for a trend: {ScenarioPointer.Words(ScenarioWords.TrendDirections)}");
                else o["direction"] = Word(a, "direction", ap, ScenarioWords.TrendDirections, "a trend's direction");
            }
            else
            {
                if (Has(a, "over")) Error(ScenarioErrorCode.BadAssertion, ap + "/over", "goes with \"scope\": \"trend\"");
                if (Has(a, "direction")) Error(ScenarioErrorCode.BadAssertion, ap + "/direction", "goes with \"scope\": \"trend\"");
            }
            if (Number(a, "atSeconds", ap, 0, 86_400, "s") is { } atS)
            {
                if (!Has(a, "value") || Has(a, "atEvent")) Error(ScenarioErrorCode.BadAssertion, ap + "/atSeconds", "goes with \"value\" (the observable's value at that second)");
                o["atSeconds"] = atS;
            }
            if (a.TryGetProperty("points", out var pts) && pts.ValueKind != JsonValueKind.Null)
            {
                var po = new JsonObject();
                if (IsObject(pts, ap + "/points", "{ \"<swept path>\": value or [values] }"))
                    foreach (var p in pts.EnumerateObject())
                    {
                        string pp = ScenarioPointer.Of(ap + "/points", p.Name);
                        string pathName = p.Name.StartsWith('/') ? PointerPath(p.Name) : p.Name;
                        var hit = swept.FirstOrDefault(s => s.Equals(pathName, StringComparison.OrdinalIgnoreCase));
                        if (hit is null) { Error(ScenarioErrorCode.BadPath, pp, $"\"{p.Name}\" is not a swept path (the sweep's: {(swept.Count == 0 ? "none" : string.Join(", ", swept))})", ScenarioPointer.Nearest(p.Name, swept)); continue; }
                        var dimValues = sweep.First(d => d!["set"]!.GetValue<string>() == hit)!["values"]!.AsArray().Select(v => v!.ToJsonString()).ToList();
                        var chosen = new JsonArray();
                        foreach (var x in p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().ToArray() : [p.Value])
                        {
                            string text = Node(x)?.ToJsonString() ?? "null";
                            if (!dimValues.Any(dv => SameValue(dv, text))) Error(ScenarioErrorCode.BadAssertion, pp, $"{text} is not one of the sweep's values for {hit} ({string.Join(", ", dimValues)})");
                            else chosen.Add(Node(x));
                        }
                        po[hit] = chosen;
                    }
                o["points"] = po;
            }
            list.Add(o);
        }
        return list;
    }

    /// <summary>Two values' JSON text the same (numbers by value: <c>1</c> is <c>1.0</c>).</summary>
    internal static bool SameValue(string a, string b) =>
        a == b || double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && x == y;

    // ---------------------------------------------------------------- outputs

    /// <summary><c>outputs.decisions</c>' words.</summary>
    internal static readonly string[] DecisionWords = ["traced", "all", "none"];

    JsonObject Outputs(JsonElement root)
    {
        var d = new ScenarioOutputs();
        var o = new JsonObject { ["events"] = "first", ["tracePolicy"] = TracePolicies.Sampled, ["traceRuns"] = d.TraceRunCount, ["traceBudgetMegabytes"] = 256, ["traceBudgetRuns"] = 0, ["ticks"] = false, ["csv"] = d.Csv, ["summaryPage"] = d.SummaryPage, ["wideBouts"] = d.WideBouts, ["tapes"] = "as traceRuns" };
        if (!root.TryGetProperty("outputs", out var ou) || ou.ValueKind == JsonValueKind.Null) return o;
        if (!IsObject(ou, "/outputs", "{ \"events\", \"traceRuns\", \"ticks\", \"csv\", \"summaryPage\", \"tapes\" }")) return o;
        Known(ou, "/outputs", OutputFields, "the outputs");
        string? events = Word(ou, "events", "/outputs", ScenarioWords.EventsKept, "which runs keep their event stream");
        string? policy = Word(ou, "tracePolicy", "/outputs", TracePolicies.Names, "a trace policy");
        // The output contract's tracePolicy and the scenario's events say the same thing two ways: one decides the other.
        string? fromEvents = events switch { "first" => TracePolicies.Sampled, "all" => TracePolicies.All, "none" => TracePolicies.None, _ => null };
        if (events is not null && policy is not null && fromEvents != policy)
            Error(ScenarioErrorCode.OutOfRange, "/outputs/tracePolicy", $"\"{policy}\" says otherwise than \"events\": \"{events}\" (which is \"{fromEvents}\"); give one of them");
        policy ??= fromEvents ?? TracePolicies.Sampled;
        o["events"] = events ?? policy switch { TracePolicies.All => "all", TracePolicies.None => "none", _ => "first" };
        o["tracePolicy"] = policy;
        o["traceBudgetMegabytes"] = Number(ou, "traceBudgetMegabytes", "/outputs", 1, 1_000_000, "MB of event streams and tapes a scenario may write") ?? 256;
        o["traceBudgetRuns"] = Whole(ou, "traceBudgetRuns", "/outputs", 0, 1_000_000, "runs that keep a stream; 0: no limit") ?? 0;
        if (Flag(ou, "wideBouts", "/outputs") is { } wide) o["wideBouts"] = wide;
        // Which runs carry the brains' decisions (BrainDecided): "traced" (the default: the runs kept from the start),
        // "all" (every run that may keep a stream), "none"; true is "all", false "none". Written into resolved.json only when given.
        if (ou.TryGetProperty("decisions", out var dz) && dz.ValueKind != JsonValueKind.Null)
        {
            if (dz.ValueKind is JsonValueKind.True or JsonValueKind.False) o["decisions"] = dz.ValueKind == JsonValueKind.True ? "all" : "none";
            else if (Word(dz, "/outputs/decisions", DecisionWords, "which runs carry the duelling brains' decisions (or true / false)") is { } dw) o["decisions"] = dw;
        }
        if (Whole(ou, "traceRuns", "/outputs", 0, 1_000_000, "runs a point") is { } tr) o["traceRuns"] = tr;
        if (ou.TryGetProperty("ticks", out var tk) && tk.ValueKind != JsonValueKind.Null)
        {
            if (tk.ValueKind == JsonValueKind.False) o["ticks"] = false;
            else if (IsObject(tk, "/outputs/ticks", "{ \"everyTicks\", \"runs\" }, or false"))
            {
                Known(tk, "/outputs/ticks", ["everyTicks", "runs"], "the tick trace");
                o["ticks"] = new JsonObject
                {
                    ["everyTicks"] = Whole(tk, "everyTicks", "/outputs/ticks", 1, 1_000_000, "ticks; 120 a second") ?? 12,
                    ["runs"] = Word(tk, "runs", "/outputs/ticks", ScenarioWords.TickRuns, "which runs") ?? "first",
                };
            }
        }
        if (Flag(ou, "csv", "/outputs") is { } csv) o["csv"] = csv;
        if (Flag(ou, "summaryPage", "/outputs") is { } page) o["summaryPage"] = page;
        if (Whole(ou, "tapes", "/outputs", 0, 1_000_000, "tapes a point") is { } tapes) o["tapes"] = tapes;
        if (ou.TryGetProperty("pictures", out var pics) && pics.ValueKind != JsonValueKind.Null && IsObject(pics, "/outputs/pictures", "{ \"atSeconds\": [s, ...], \"run\": 0 }"))
        {
            Known(pics, "/outputs/pictures", ["atSeconds", "run"], "the pictures");
            var times = new JsonArray();
            if (pics.TryGetProperty("atSeconds", out var ats) && IsList(ats, "/outputs/pictures/atSeconds", "seconds"))
            {
                int k = 0;
                foreach (var t in ats.EnumerateArray()) if (Number(t, ScenarioPointer.Of("/outputs/pictures/atSeconds", k++), 0, 86_400, "s") is { } s) times.Add(s);
            }
            o["pictures"] = new JsonObject { ["atSeconds"] = times, ["run"] = Whole(pics, "run", "/outputs/pictures", 0, 1_000_000) ?? 0 };
            Warnings.Add("/outputs/pictures: this build's runner does not draw a scenario's pictures yet; the arena's --picture-at does");
        }
        if (ou.TryGetProperty("film", out var film) && film.ValueKind != JsonValueKind.Null && IsObject(film, "/outputs/film", "{ \"fromSeconds\", \"toSeconds\", \"view\", \"run\" }"))
        {
            Known(film, "/outputs/film", ["fromSeconds", "toSeconds", "view", "run"], "the film");
            double from = Number(film, "fromSeconds", "/outputs/film", 0, 86_400, "s") ?? 0, to = Number(film, "toSeconds", "/outputs/film", 0, 86_400, "s") ?? from + 4;
            if (to <= from) Error(ScenarioErrorCode.OutOfRange, "/outputs/film/toSeconds", ReaderInv($"must be after fromSeconds ({from} s)"));
            o["film"] = new JsonObject { ["fromSeconds"] = from, ["toSeconds"] = to, ["view"] = Word(film, "view", "/outputs/film", ScenarioWords.FilmViews, "a view") ?? "side", ["run"] = Whole(film, "run", "/outputs/film", 0, 1_000_000) ?? 0 };
            Warnings.Add("/outputs/film: this build's runner does not film a scenario yet; the arena's --film-at does");
        }
        return o;
    }
}
