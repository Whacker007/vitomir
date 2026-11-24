// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>Where a value form is read: what it may take there and which code its errors carry.</summary>
internal enum FormUse
{
    /// <summary>A trigger's <c>when</c>: a selector, an observable or a count, with a comparator; checked in the world each tick.</summary>
    Trigger,
    /// <summary>A stop's <c>when</c>: an observable, a count, a rate or a time, with a comparator.</summary>
    Stop,
    /// <summary>A measure: any form but a check, no comparator.</summary>
    Measure,
    /// <summary>An assertion: any form, a comparator (but a trend).</summary>
    Assertion,
}

internal sealed partial class ScenarioReader
{
    // ---------------------------------------------------------------- selectors

    IReadOnlyList<EventKind>? events;
    IReadOnlyList<EventKind> EventKinds => events ??= ScenarioWords.Events();

    /// <summary>An event selector (section 4.5): its event kind, by and on actors, where filters (fields case aside), windows and sum.</summary>
    internal JsonObject? Selector(JsonElement s, string at)
    {
        if (s.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.BadSelector, at, $"an event selector is {{ \"event\", \"by\", \"on\", \"where\", ... }}, got {Kind(s)}"); return null; }
        Known(s, at, SelectorFields, "an event selector");
        var o = new JsonObject();
        EventKind? kind = null;
        if (!s.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String)
            Error(ScenarioErrorCode.BadSelector, at + "/event", $"is required: an event's kind (--describe events), as \"ContactMade\"");
        else
        {
            string name = ev.GetString()!;
            kind = EventKinds.FirstOrDefault(e => e.Name == name) ?? EventKinds.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (kind is null) Error(ScenarioErrorCode.UnknownName, at + "/event", $"there is no event \"{name}\" (--describe events lists them)", ScenarioPointer.Nearest(name, EventKinds.Select(e => e.Name)));
            else if (kind.Status == "bow" && !ScenarioGame.BowBuilt) NotBuilt(at + "/event", $"the event \"{kind.Name}\" (the bow's)", "a later build");
            o["event"] = kind?.Name ?? name;
        }
        if (s.TryGetProperty("by", out var by) && by.ValueKind != JsonValueKind.Null) o["by"] = Actor(by, at + "/by");
        if (s.TryGetProperty("on", out var on) && on.ValueKind != JsonValueKind.Null) o["on"] = Actor(on, at + "/on");
        if (s.TryGetProperty("where", out var where) && where.ValueKind != JsonValueKind.Null)
        {
            var w = new JsonObject();
            if (where.ValueKind != JsonValueKind.Object) Error(ScenarioErrorCode.BadSelector, at + "/where", $"must be an object of field: value (or [values]), got {Kind(where)}");
            else foreach (var f in where.EnumerateObject())
                {
                    string fp = ScenarioPointer.Of(at + "/where", f.Name);
                    string field = f.Name;
                    if (kind is { Status: "built" })
                    {
                        var fields = ScenarioWords.WhereFields(kind);
                        var hit = fields.FirstOrDefault(n => n.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
                        if (hit is null) { Error(ScenarioErrorCode.BadSelector, fp, $"{kind.Name} has no field \"{f.Name}\" (its fields: {string.Join(", ", fields)})", ScenarioPointer.Nearest(f.Name, fields)); continue; }
                        field = hit;
                    }
                    if (f.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null || f.Value.ValueKind == JsonValueKind.Array && f.Value.EnumerateArray().Any(x => x.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
                    { Error(ScenarioErrorCode.BadSelector, fp, $"must be a value or a list of values (text, number, true/false), got {Kind(f.Value)}"); continue; }
                    WhereWords(kind, field, f.Value, fp);
                    w[field] = Node(f.Value);
                }
            o["where"] = w;
        }
        if (Number(s, "fromSeconds", at, 0, 86_400, "s") is { } fs) o["fromSeconds"] = fs;
        if (Number(s, "toSeconds", at, 0, 86_400, "s") is { } ts) o["toSeconds"] = ts;
        if (Text(s, "fromMarker", at, what: "a mark step's text") is { } fm) o["fromMarker"] = fm;
        if (Text(s, "toMarker", at, what: "a mark step's text") is { } tm) o["toMarker"] = tm;
        if (Text(s, "sum", at, what: "a numeric field of the event") is { } sum)
        {
            string field = sum;
            if (kind is { Status: "built" })
            {
                var numeric = kind.Fields.Where(f => f.Type is "number" or "whole number").Select(f => f.Name).ToList();
                field = numeric.FirstOrDefault(n => n.Equals(sum, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (field.Length == 0) Error(ScenarioErrorCode.BadSelector, at + "/sum", $"{kind.Name} has no numeric field \"{sum}\" to add (its numeric fields: {string.Join(", ", numeric)})", ScenarioPointer.Nearest(sum, numeric));
            }
            o["sum"] = field.Length == 0 ? sum : field;
        }
        return o;
    }

    /// <summary>A where filter's words checked against the field's own (an enum's names, the derived fields' words), case aside.</summary>
    void WhereWords(EventKind? kind, string field, JsonElement value, string at)
    {
        IReadOnlyList<string>? words = field switch
        {
            "region" or "aimedRegion" => ScenarioWords.Regions,
            "side" => ScenarioWords.Sides,
            "verdict" => ["held", "parried", "overpowered", "clashed", "dominated"],
            _ => null,
        };
        if (words is null && kind?.Fields.FirstOrDefault(f => f.Name == field) is { } ef && ef.Type.Contains(" | ", StringComparison.Ordinal)) words = ef.Type.Split(" | ");
        if (words is null) return;
        foreach (var x in value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value])
            if (x.ValueKind == JsonValueKind.String && !words.Any(w => w.Equals(x.GetString(), StringComparison.OrdinalIgnoreCase)))
                Error(ScenarioErrorCode.BadSelector, at, $"\"{x.GetString()}\" is not a {field} of {kind?.Name ?? "the event"} ({string.Join(", ", words)})", ScenarioPointer.Nearest(x.GetString()!, words));
    }

    // ---------------------------------------------------------------- observables

    /// <summary>An observable path (section 4.5), in the actors' own spelling; <see cref="ScenarioErrorCode.BadObservable"/> when it is none.</summary>
    internal string? Observable(JsonElement v, string at)
    {
        if (v.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.BadObservable, at, $"must be an observable's path (\"Guard.health\"), got {Kind(v)}"); return null; }
        string path = v.GetString()!;
        if (path == "time") return path;
        var parts = path.Split('.');
        var shapes = ScenarioWords.Observables.Select(o => o.Path).ToList();
        string? actor = actorNames.FirstOrDefault(n => n.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (actor is null)
        {
            Error(ScenarioErrorCode.BadObservable, at, $"\"{path}\" does not start with an actor's name or \"time\" (the actors: {string.Join(", ", actorNames)})", ScenarioPointer.Nearest(parts[0], [.. actorNames, "time"]));
            return null;
        }
        string rest = string.Join('.', parts.Skip(1));
        var simple = shapes.Where(s => s.StartsWith("<actor>.", StringComparison.Ordinal) && !s[8..].Contains('<')).Select(s => s[8..]).ToList();
        string? hit = simple.FirstOrDefault(s => s.Equals(rest, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return $"{actor}.{hit}";
        if (parts.Length == 3 && parts[1].Equals("distanceTo", StringComparison.OrdinalIgnoreCase))
            return Actor(parts[2], at) is { } other ? $"{actor}.distanceTo.{other}" : null;
        if (parts.Length == 3 && parts[1].Equals("powerLive", StringComparison.OrdinalIgnoreCase))
        {
            string power = game is null ? parts[2] : Name(parts[2], at, game.Powers.Keys, "power") ?? parts[2];
            return $"{actor}.powerLive.{power}";
        }
        var all = simple.Concat(["distanceTo.<actor>", "powerLive.<power>"]).ToList();
        Error(ScenarioErrorCode.BadObservable, at, $"\"{path}\": {actor} has no observable \"{rest}\" (an actor's: {string.Join(", ", all)})", ScenarioPointer.Nearest(rest, all).Select(s => $"{actor}.{s}").ToList());
        return null;
    }

    // ---------------------------------------------------------------- value forms and comparators

    static ScenarioErrorCode CodeOf(FormUse use) => use switch
    {
        FormUse.Trigger => ScenarioErrorCode.BadTrigger,
        FormUse.Measure => ScenarioErrorCode.BadMeasure,
        FormUse.Assertion => ScenarioErrorCode.BadAssertion,
        _ => ScenarioErrorCode.WrongType,
    };

    /// <summary>
    /// One value form (section 4.5) in <paramref name="o"/>, written into <paramref name="into"/>: exactly one of value, count, rate,
    /// timeOf, delay, difference, ratio, measure, check (as <paramref name="use"/> allows), with its side fields.
    /// </summary>
    internal bool ValueForm(JsonElement o, string at, FormUse use, JsonObject into, ICollection<string>? measures)
    {
        string[] allowed = use switch
        {
            FormUse.Trigger => ["value", "count"],
            FormUse.Stop => ["value", "count", "rate", "timeOf"],
            FormUse.Measure => ["value", "count", "rate", "timeOf", "delay", "difference", "ratio"],
            _ => ScenarioWords.ValueForms,
        };
        var given = ScenarioWords.ValueForms.Where(f => Has(o, f)).ToList();
        if (given.Count != 1)
        {
            Error(CodeOf(use), at, given.Count == 0 ? $"names no value; it takes one of {ScenarioPointer.Words(allowed)}" : $"names {given.Count} values ({string.Join(", ", given)}); it takes one");
            return false;
        }
        string form = given[0];
        string fp = ScenarioPointer.Of(at, form);
        if (!allowed.Contains(form)) { Error(CodeOf(use), fp, $"\"{form}\" is not taken here; {(use == FormUse.Trigger ? "a trigger's when" : use == FormUse.Stop ? "a stop's when" : "a measure")} takes {ScenarioPointer.Words(allowed)}"); return false; }
        var v = o.GetProperty(form);
        switch (form)
        {
            case "value":
                into["value"] = Observable(v, fp);
                if (o.TryGetProperty("atEvent", out var ae) && ae.ValueKind != JsonValueKind.Null) into["atEvent"] = Selector(ae, at + "/atEvent");
                break;
            case "count":
                into["count"] = Selector(v, fp);
                break;
            case "timeOf":
                into["timeOf"] = Selector(v, fp);
                break;
            case "rate":
                if (!IsObject(v, fp, "{ \"of\": selector, \"per\": selector } or { \"of\": selector, \"perSecond\": true }")) return false;
                Known(v, fp, ["of", "per", "perSecond"], "a rate");
                var rate = new JsonObject();
                if (!v.TryGetProperty("of", out var of)) Error(CodeOf(use), fp + "/of", "is required: the selector counted");
                else rate["of"] = Selector(of, fp + "/of");
                bool perSecond = Flag(v, "perSecond", fp) ?? false;
                if (v.TryGetProperty("per", out var per) == perSecond) Error(CodeOf(use), fp, "a rate takes \"per\" (a selector) or \"perSecond\": true, one of them");
                if (perSecond) rate["perSecond"] = true;
                else if (per.ValueKind != JsonValueKind.Undefined) rate["per"] = Selector(per, fp + "/per");
                into["rate"] = rate;
                break;
            case "delay":
                if (!IsObject(v, fp, "{ \"from\": selector, \"to\": selector, \"pairBy\": field }")) return false;
                Known(v, fp, ["from", "to", "pairBy"], "a delay");
                var delay = new JsonObject();
                if (v.TryGetProperty("from", out var fr)) delay["from"] = Selector(fr, fp + "/from"); else Error(CodeOf(use), fp + "/from", "is required: the selector the delay runs from");
                if (v.TryGetProperty("to", out var to)) delay["to"] = Selector(to, fp + "/to"); else Error(CodeOf(use), fp + "/to", "is required: the selector the delay runs to");
                if (Text(v, "pairBy", fp, what: "a field both events have (\"swing\")") is { } pair)
                {
                    var fk = EventKinds.FirstOrDefault(e => e.Name == delay["from"]?["event"]?.GetValue<string>());
                    var tk = EventKinds.FirstOrDefault(e => e.Name == delay["to"]?["event"]?.GetValue<string>());
                    string? f1 = fk?.Fields.FirstOrDefault(f => f.Name.Equals(pair, StringComparison.OrdinalIgnoreCase))?.Name;
                    string? f2 = tk?.Fields.FirstOrDefault(f => f.Name.Equals(pair, StringComparison.OrdinalIgnoreCase))?.Name;
                    if (fk is { Status: "built" } && tk is { Status: "built" } && (f1 is null || f2 is null))
                        Error(ScenarioErrorCode.BadSelector, fp + "/pairBy", $"\"{pair}\" is not a field of both {fk.Name} and {tk.Name}", ScenarioPointer.Nearest(pair, fk.Fields.Select(f => f.Name).Intersect(tk.Fields.Select(f => f.Name))));
                    delay["pairBy"] = f1 ?? pair;
                }
                else delay["pairBy"] = "next";
                into["delay"] = delay;
                break;
            case "difference":
            case "ratio":
                if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2) { Error(CodeOf(use), fp, $"must be [first, second]: two measures' names (or inline measures), got {Kind(v)}"); return false; }
                var pairList = new JsonArray();
                int i = 0;
                foreach (var m in v.EnumerateArray())
                {
                    string mp = ScenarioPointer.Of(fp, i++);
                    if (m.ValueKind == JsonValueKind.String) pairList.Add(MeasureName(m.GetString()!, mp, measures));
                    else if (m.ValueKind == JsonValueKind.Object)
                    {
                        var inner = new JsonObject();
                        Known(m, mp, ScenarioWords.ValueForms.Concat(["atEvent", "nth"]).ToArray(), "an inline measure");
                        ValueForm(m, mp, FormUse.Measure, inner, measures);
                        pairList.Add(inner);
                    }
                    else Error(CodeOf(use), mp, $"must be a measure's name or an inline measure, got {Kind(m)}");
                }
                into[form] = pairList;
                break;
            case "measure":
                if (v.ValueKind != JsonValueKind.String) { Error(CodeOf(use), fp, $"must be a measure's name, got {Kind(v)}"); return false; }
                into["measure"] = MeasureName(v.GetString()!, fp, measures);
                break;
            case "check":
                if (v.ValueKind != JsonValueKind.String) { Error(CodeOf(use), fp, $"must be a built-in check's name, got {Kind(v)}"); return false; }
                var checks = BuiltInChecks.All.Select(c => c.Name).ToList();
                if (Name(v.GetString()!, fp, checks, "built-in check") is { } check)
                {
                    if (BuiltInChecks.All.First(c => c.Name == check).NeedsBow && !ScenarioGame.BowBuilt) NotBuilt(fp, $"the check \"{check}\" (the bow's)", "a later build");
                    into["check"] = check;
                }
                break;
        }
        if (Has(o, "nth"))
        {
            if (form is not ("timeOf" or "value") || form == "value" && !Has(o, "atEvent")) Error(CodeOf(use), at + "/nth", "goes with \"timeOf\" or with \"value\" and \"atEvent\"");
            into["nth"] = Whole(o, "nth", at, 1, 1_000_000);
        }
        else if (form == "timeOf" || form == "value" && Has(o, "atEvent")) into["nth"] = 1;
        if (Has(o, "atEvent") && form != "value") Error(CodeOf(use), at + "/atEvent", "goes with \"value\" (the value when the event happened)");
        return true;
    }

    string MeasureName(string name, string at, ICollection<string>? measures)
    {
        if (measures is null) { Error(ScenarioErrorCode.BadMeasure, at, "names a measure, and none may be named here"); return name; }
        if (measures.Contains(name)) return name;
        var builtIn = ScenarioGame.BuiltInMeasures(actorNames);
        if (builtIn.Contains(name)) return name;
        Error(ScenarioErrorCode.UnknownName, at, $"there is no measure \"{name}\" (the scenario's: {(measures.Count == 0 ? "none" : string.Join(", ", measures))}; the built-in ones are <actor>.<column>: --describe measures)", ScenarioPointer.Nearest(name, measures.Concat(builtIn)));
        return name;
    }

    /// <summary>
    /// The comparator in <paramref name="o"/> (exactly one of below, atMost, above, atLeast, equals, between, isTrue, isFalse),
    /// written into <paramref name="into"/>; false (with an error) when there is none or more than one and one is needed.
    /// </summary>
    internal bool Comparator(JsonElement o, string at, FormUse use, JsonObject into, bool required = true)
    {
        var given = ScenarioWords.Comparators.Where(c => Has(o, c)).ToList();
        if (given.Count == 0)
        {
            if (required) Error(CodeOf(use), at, $"names no comparator; it takes one of {ScenarioPointer.Words(ScenarioWords.Comparators)}");
            return false;
        }
        if (given.Count > 1) { Error(CodeOf(use), at, $"names {given.Count} comparators ({string.Join(", ", given)}); it takes one"); return false; }
        string c = given[0];
        string cp = ScenarioPointer.Of(at, c);
        var v = o.GetProperty(c);
        switch (c)
        {
            case "between":
                if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number) && v[0].GetDouble() <= v[1].GetDouble())
                    into[c] = new JsonArray(v[0].GetDouble(), v[1].GetDouble());
                else Error(CodeOf(use), cp, $"must be [low, high] (two numbers, rising), got {Kind(v)}");
                break;
            case "isTrue":
            case "isFalse":
                if (v.ValueKind != JsonValueKind.True) Error(CodeOf(use), cp, $"is written \"{c}\": true, got {Kind(v)}");
                into[c] = true;
                break;
            case "equals":
                if (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) into[c] = Node(v);
                else Error(CodeOf(use), cp, $"must be a number (or true/false), got {Kind(v)}");
                break;
            default:
                if (v.ValueKind == JsonValueKind.Number) into[c] = v.GetDouble();
                else Error(CodeOf(use), cp, $"must be a number, got {Kind(v)}");
                break;
        }
        return true;
    }

    /// <summary>A condition (a trigger's or a stop's <c>when</c>): an event selector, or a value form with a comparator.</summary>
    internal JsonObject? Condition(JsonElement w, string at, FormUse use)
    {
        if (!IsObject(w, at, "an event selector, or a value with a comparator ({ \"value\": \"Guard.health\", \"atMost\": 40 })")) return null;
        if (Has(w, "event"))
        {
            // An event selector: the trigger fires when a matching event happens.
            if (use != FormUse.Trigger) { Error(CodeOf(use), at, "a stop's condition is a value with a comparator; for \"the first such event\", use { \"count\": <selector>, \"atLeast\": 1 }"); return null; }
            return new JsonObject { ["event"] = Selector(w, at) };
        }
        var o = new JsonObject();
        Known(w, at, [.. ScenarioWords.ValueForms, .. ScenarioWords.Comparators, "atEvent", "nth"], use == FormUse.Trigger ? "a trigger's condition" : "a stop's condition");
        if (!ValueForm(w, at, use, o, null)) return o;
        Comparator(w, at, use, o);
        // The director reads these observables in the world (a trigger's value); the others are the run's tallies (a stop's).
        if (use == FormUse.Trigger && o["value"]?.GetValue<string>() is { } path && path != "time")
        {
            string prop = path.Split('.')[1];
            if (!TriggerObservables.Contains(prop))
                Error(ScenarioErrorCode.NotBuilt, at + "/value", $"a trigger reads time and an actor's {string.Join(", ", TriggerObservables)} in this build, not {prop}; for it use a count condition on its event ({{ \"count\": {{ \"event\": \"ContactMade\", ... }}, \"atLeast\": 1 }})");
        }
        return o;
    }

    /// <summary>The observables a trigger's value may read (the director work's director, in the world).</summary>
    internal static readonly string[] TriggerObservables = ["health", "healthMax", "blood", "bloodMax", "distanceTo", "speed", "stopped", "powerLive", "swings"];

    // ---------------------------------------------------------------- triggers

    JsonArray TriggersOf(JsonElement root)
    {
        var list = new JsonArray();
        if (!root.TryGetProperty("triggers", out var ts) || ts.ValueKind == JsonValueKind.Null) return list;
        if (!IsList(ts, "/triggers", "a list of triggers")) return list;
        int i = 0;
        foreach (var t in ts.EnumerateArray())
        {
            string tp = ScenarioPointer.Of("/triggers", i++);
            if (!IsObject(t, tp, "{ \"label\", \"when\", \"do\", \"repeat\", \"cooldownSeconds\" }")) continue;
            Known(t, tp, TriggerFields, "a trigger");
            var o = new JsonObject { ["label"] = Text(t, "label", tp) ?? $"trigger {i - 1}" };
            if (!t.TryGetProperty("when", out var when)) Error(ScenarioErrorCode.BadTrigger, tp + "/when", "is required: an event selector, or a value with a comparator");
            else o["when"] = Condition(when, tp + "/when", FormUse.Trigger);
            var does = new JsonArray();
            if (!t.TryGetProperty("do", out var d) || d.ValueKind != JsonValueKind.Array || d.GetArrayLength() == 0)
                Error(ScenarioErrorCode.BadTrigger, tp + "/do", "is required: a list of { \"actor\", \"step\" } or { \"end\": \"<reason>\" }");
            else
            {
                int k = 0;
                foreach (var x in d.EnumerateArray())
                {
                    string xp = ScenarioPointer.Of(tp + "/do", k++);
                    if (x.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.BadTrigger, xp, $"must be {{ \"actor\", \"step\" }} or {{ \"end\": \"<reason>\" }}, got {Kind(x)}"); continue; }
                    if (Has(x, "end"))
                    {
                        Known(x, xp, ["end"], "a trigger's end");
                        does.Add(new JsonObject { ["end"] = Text(x, "end", xp, true, "the reason") });
                        continue;
                    }
                    Known(x, xp, ["actor", "step"], "a trigger's action");
                    string? actor = x.TryGetProperty("actor", out var av) ? Actor(av, xp + "/actor") : null;
                    if (!Has(x, "actor")) Error(ScenarioErrorCode.BadTrigger, xp + "/actor", "is required: the actor whose step it starts");
                    if (!x.TryGetProperty("step", out var sv)) { Error(ScenarioErrorCode.BadTrigger, xp + "/step", "is required: the step to start"); continue; }
                    if (actor is not null && actor == inputActor) Error(ScenarioErrorCode.BadInput, xp + "/actor", $"{actor} is driven by the player's input: a trigger cannot start a step on it");
                    int ai = actor is null ? -1 : actorNames.IndexOf(actor);
                    var step = ai >= 0 ? Step(sv, xp + "/step", ai) : null;
                    does.Add(new JsonObject { ["actor"] = actor, ["step"] = step });
                }
            }
            o["do"] = does;
            o["repeat"] = Flag(t, "repeat", tp) ?? false;
            o["cooldownSeconds"] = Number(t, "cooldownSeconds", tp, 0, 3600, "s") ?? 0;
            if (Number(t, "afterSeconds", tp, 0, 86_400, "s") is { } after) o["afterSeconds"] = after;
            if (Number(t, "beforeSeconds", tp, 0, 86_400, "s") is { } before) o["beforeSeconds"] = before;
            list.Add(o);
        }
        return list;
    }
}
