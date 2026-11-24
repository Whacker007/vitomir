// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The published schemas of the runner's outputs: <c>results.json</c> (<c>VITOMIR_SCENARIO_RESULTS_1</c>) and a
/// line of <c>events.jsonl</c> (<c>VITOMIR_EVENT_TRACE_2</c>), as JSON Schema (draft 2020-12). The results' schema is generated from
/// the records <see cref="ScenarioJson"/> writes (property names camelCase, a record's fields all listed, nothing else allowed, a
/// field that may be null optional), so it cannot drift from what is written; <c>--describe</c> publishes them.
/// </summary>
public static class ScenarioSchemas
{
    public const string Draft = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>The schema of <c>results.json</c>: a run set's results, or a refused scenario's (state FAILED, its errors).</summary>
    public static JsonObject Results()
    {
        var defs = new JsonObject();
        var run = Of(typeof(ScenarioResults), defs);
        var refused = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("schema", "state", "scenario", "errors", "summary"),
            ["properties"] = new JsonObject
            {
                ["schema"] = new JsonObject { ["const"] = ScenarioResults.SchemaName },
                ["state"] = new JsonObject { ["const"] = "FAILED" },
                ["scenario"] = new JsonObject { ["type"] = "object" },
                ["errors"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject
                {
                    ["type"] = "object", ["required"] = new JsonArray("code", "pointer", "message"),
                    ["properties"] = new JsonObject
                    {
                        ["code"] = new JsonObject { ["type"] = "string" }, ["pointer"] = new JsonObject { ["type"] = "string" },
                        ["message"] = new JsonObject { ["type"] = "string" }, ["suggestions"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    },
                } },
                ["summary"] = new JsonObject { ["type"] = "object" },
            },
        };
        return new JsonObject
        {
            ["$schema"] = Draft,
            ["$id"] = "vitomir:" + ScenarioResults.SchemaName,
            ["title"] = "Vitomir scenario results (results.json)",
            ["description"] = "One per scenario run set: provenance, run counts, per point the measures' aggregates with n and 95 % intervals, each assertion's verdict with the measured value, paired comparisons, the files written. Per-run rows are in runs.csv, actors.csv and actions.csv. Numbers are rounded to nine significant digits; a null is left out.",
            ["anyOf"] = new JsonArray(run, refused),
            ["$defs"] = defs,
        };
    }

    /// <summary>The schema of one line of <c>events.jsonl</c> after the head (each kind's <c>event</c> fields: <c>--describe events</c>).</summary>
    public static JsonObject EventLine() => new()
    {
        ["$schema"] = Draft,
        ["$id"] = "vitomir:" + EventTrace.Schema2 + ":line",
        ["title"] = "A line of a scenario's event stream (events/p000-r000.events.jsonl)",
        ["type"] = "object",
        ["required"] = new JsonArray("kind", "tick", "seconds", "event", "runId", "seed", "eventId", "parentEventId", "actors"),
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "the event's kind (the record's name: ContactMade, StepBegan, ...; CombatTempo)" },
            ["tick"] = new JsonObject { ["type"] = "integer", ["description"] = "the tick it happened in (120 a second)" },
            ["seconds"] = new JsonObject { ["type"] = "number", ["description"] = "seconds of the run (tick / 120)" },
            ["event"] = new JsonObject { ["type"] = "object", ["description"] = "the record as published: its fields by their C# names; people as \"e3\" (the head's actors say who)" },
            ["runId"] = new JsonObject { ["type"] = "string", ["description"] = "p000-r003: the point and run (the tables' join key)" },
            ["seed"] = new JsonObject { ["type"] = "integer", ["description"] = "the run's seed" },
            ["eventId"] = new JsonObject { ["type"] = "integer", ["description"] = "its place in the run's stream, 0 up (stable for the same scenario, point and seed)" },
            ["parentEventId"] = new JsonObject { ["type"] = new JsonArray("integer", "null"), ["description"] = "the event it follows from: a blow's AttackBegun for its contacts, strike, release; a contact for its harm, verdict, stagger; a power's use for its end; a step's beginning for its end; an arrow's loosing for its contact and end" },
            ["actorId"] = new JsonObject { ["type"] = "string", ["description"] = "who did it: a0, a1 ... (the cast's order); absent for an event nobody did" },
            ["actors"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "string" }, ["description"] = "each person field of the event by the actor's name ({\"Attacker\": \"Christof\", \"Target\": \"Guard\"})" },
            ["derived"] = new JsonObject
            {
                ["type"] = "object", ["description"] = "fields the selectors may use that the record has not",
                ["properties"] = new JsonObject
                {
                    ["region"] = new JsonObject { ["type"] = "string", ["description"] = "the body region met, as aimAt names regions (upperArm)" },
                    ["side"] = new JsonObject { ["type"] = "string", ["description"] = "left or right, for a region with sides" },
                    ["aimedRegion"] = new JsonObject { ["type"] = "string", ["description"] = "the region the step asked for (\"chest\", \"left upperArm\")" },
                    ["regionAsAimed"] = new JsonObject { ["type"] = "boolean", ["description"] = "whether the region met is the region asked (either side when none was asked)" },
                    ["weaponClass"] = new JsonObject { ["type"] = "string", ["description"] = "arrow for a projectile's contact, melee otherwise" },
                    ["verdict"] = new JsonObject { ["type"] = "string", ["description"] = "the guard's verdict (held, parried, overpowered, clash...)" },
                    ["power"] = new JsonObject { ["type"] = "string", ["description"] = "the power's id (celerity)" },
                    ["fixed"] = new JsonObject { ["type"] = "boolean", ["description"] = "on HarmResolved and GuardResolved: its contact was a fixed contact (FIXED_CONTACT; written only when true; ContactMade carries the field Fixed itself)" },
                },
            },
        },
    };

    /// <summary>The schema of the stream's head line (the trace's head plus the run's identity).</summary>
    public static JsonObject EventHead() => new()
    {
        ["$schema"] = Draft,
        ["$id"] = "vitomir:" + EventTrace.Schema2 + ":head",
        ["title"] = "The first line of a scenario's event stream",
        ["type"] = "object",
        ["required"] = new JsonArray("kind", "schema", "evidence", "build", "game", "modules", "seed", "level", "ticksPerSecond", "people", "runId", "actors"),
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["const"] = "head" },
            ["schema"] = new JsonObject { ["const"] = EventTrace.Schema2 },
            ["evidence"] = new JsonObject { ["type"] = "string" },
            ["build"] = new JsonObject { ["type"] = "string" },
            ["game"] = new JsonObject { ["type"] = "string" },
            ["modules"] = new JsonObject { ["type"] = "object", ["description"] = "each module's configuration hash" },
            ["seed"] = new JsonObject { ["type"] = "integer" },
            ["level"] = new JsonObject { ["type"] = "string" },
            ["ticksPerSecond"] = new JsonObject { ["type"] = "integer" },
            ["notes"] = new JsonObject { ["type"] = "object", ["description"] = "the options the tape carries (the cast, the program, the switches)" },
            ["activeWindows"] = new JsonObject { ["type"] = "array", ["description"] = "every attack clip's active window (s), by character" },
            ["people"] = new JsonObject { ["type"] = "array", ["description"] = "each person: id (e3), name, character, sheet (purchased and effective)" },
            ["runId"] = new JsonObject { ["type"] = "string" },
            ["scenario"] = new JsonObject { ["type"] = "string" },
            ["scenarioSha256"] = new JsonObject { ["type"] = "string" },
            ["point"] = new JsonObject { ["type"] = "integer" },
            ["pointName"] = new JsonObject { ["type"] = "string" },
            ["values"] = new JsonObject { ["type"] = "object" },
            ["runSeed"] = new JsonObject { ["type"] = "integer" },
            ["actors"] = new JsonObject { ["type"] = "array", ["description"] = "actorId (a0), id (e3), name" },
        },
    };

    // ---- the results' schema, from the records ----

    static readonly NullabilityInfoContext Nullability = new();

    static JsonObject Of(Type t, JsonObject defs)
    {
        if (t == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (t == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (t == typeof(int) || t == typeof(long) || t == typeof(ulong) || t == typeof(uint)) return new JsonObject { ["type"] = "integer" };
        if (t == typeof(double) || t == typeof(float)) return new JsonObject { ["type"] = "number" };
        if (t.IsEnum) return new JsonObject { ["type"] = "string" };
        if (Nullable.GetUnderlyingType(t) is { } inner) return Of(inner, defs);
        if (t == typeof(JsonNode)) return new JsonObject();
        if (t == typeof(JsonArray)) return new JsonObject { ["type"] = "array" };
        if (t == typeof(JsonObject)) return new JsonObject { ["type"] = "object" };
        if (t.IsArray) return new JsonObject { ["type"] = "array", ["items"] = Of(t.GetElementType()!, defs) };
        if (t.IsGenericType && Dictionary(t) is { } valueType)
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Of(valueType, defs) };
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
            return new JsonObject { ["type"] = "array", ["items"] = Of(t.GetGenericArguments()[0], defs) };
        string name = t.Name;
        if (!defs.ContainsKey(name))
        {
            defs[name] = new JsonObject();   // placeholder against recursion
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || p.Name == "EqualityContract") continue;
                string json = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
                var s = Of(p.PropertyType, defs);
                bool nullable = Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || (!p.PropertyType.IsValueType && Nullability.Create(p).WriteState == NullabilityState.Nullable)
                    || p.PropertyType == typeof(JsonNode);
                props[json] = s;
                if (!nullable) required.Add(json);
            }
            defs[name] = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required, ["additionalProperties"] = false };
        }
        return new JsonObject { ["$ref"] = $"#/$defs/{name}" };
    }

    static Type? Dictionary(Type t)
    {
        foreach (var i in t.GetInterfaces().Append(t))
            if (i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
                return i.GetGenericArguments()[1];
        return null;
    }
}
