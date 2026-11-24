// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// The enhanced module's perception (ours): the
/// files of <c>Data/perception/</c>, told apart by their schema — one <c>VITOMIR_PERCEPTION</c> file of the world's
/// numbers and its act table (<c>enhanced:perception/sight</c>: the light by day and night, motion, cover, the ladder,
/// the shout, the heat, the reaction rule and distances; how each act is taken) and one <c>VITOMIR_OBSERVER_KIND</c>
/// file per kind of person (<c>…/townsman</c>, <c>…/guard</c>, <c>…/drunk</c> and the rest: the 1.1 characters of that
/// kind and its numbers and traits) — read <b>strictly</b>, as the movement profiles are: a field
/// missing, unknown, of the wrong kind or out of its range is named, by file and field, and the module refuses.
/// <code>
/// { "schema": "VITOMIR_PERCEPTION", "status": "SANDBOX", "title": "...", "note": "...", "defaultKind": "townsman",
///   "numbers": { "DayLight": 1.0, "NightLight": 0.3, ... },
///   "acts": { "Jump": { "strangeness": 0.9, "threat": 0.5, "highest": "alarmed" }, "Speed": { ... }, ... } }
/// { "schema": "VITOMIR_OBSERVER_KIND", "status": "SANDBOX", "title": "...", "note": "...",
///   "characters": [ "rudolfGuard" ], "numbers": { "SightRange": 30, "ConeDegrees": 70, "Eyesight": 1.0, ... } }
/// </code>
/// </summary>
internal static class PerceptionFiles
{
    public const string WorldSchema = "VITOMIR_PERCEPTION", KindSchema = "VITOMIR_OBSERVER_KIND";
    const string Sandbox = "SANDBOX";

    static readonly string[] WorldKeys = ["schema", "status", "title", "note", "defaultKind", "numbers", "acts"];
    static readonly string[] KindKeys = ["schema", "status", "title", "note", "characters", "numbers", "duel"];

    /// <summary>A kind's <c>duel</c> (how one of it fights; optional): the duelling brain's personality, each optional, by the arena's names.</summary>
    static readonly (string Key, float Lo, float Hi, Func<DuelBrainSettings, float, DuelBrainSettings> With)[] DuelKeys =
    [
        ("strongShare", 0f, 1f, (d, v) => d with { StrongShare = v }),
        ("highShare", 0f, 1f, (d, v) => d with { HighShare = v }),
        ("levelShare", 0f, 1f, (d, v) => d with { LevelShare = v }),
        ("lowShare", 0f, 1f, (d, v) => d with { LowShare = v }),
        ("blockChance", 0f, 1f, (d, v) => d with { BlockChance = v }),
        ("reactSeconds", 0f, 10f, (d, v) => d with { ReactSeconds = v }),
        ("blockSeconds", 0f, 60f, (d, v) => d with { BlockSeconds = v }),
        ("backOffChance", 0f, 1f, (d, v) => d with { BackOffChance = v }),
        ("backOffSeconds", 0f, 10f, (d, v) => d with { BackOffSeconds = v }),
    ];
    static readonly string[] ActKeys = ["strangeness", "threat", "highest"];

    /// <summary>The world's numbers that are shares, 0 to 1.</summary>
    static readonly string[] Shares =
    [
        nameof(PerceptionNumbers.DayLight), nameof(PerceptionNumbers.NightLight), nameof(PerceptionNumbers.NightEyesWeight),
        nameof(PerceptionNumbers.CoverFloor), nameof(PerceptionNumbers.HeatPerAlarm), nameof(PerceptionNumbers.HeatWariness),
        nameof(PerceptionNumbers.DutyAt), nameof(PerceptionNumbers.LookAt), nameof(PerceptionNumbers.FleeAt),
        nameof(PerceptionNumbers.JoinDutyAt), nameof(PerceptionNumbers.CrippledFleeBravery),
    ];
    /// <summary>A kind's numbers that are shares (its traits), 0 to 1.</summary>
    static readonly string[] KindShares =
    [
        nameof(ObserverNumbers.Eyesight), nameof(ObserverNumbers.NightEyes), nameof(ObserverNumbers.Attention), nameof(ObserverNumbers.Bravery),
        nameof(ObserverNumbers.Curiosity), nameof(ObserverNumbers.Duty), nameof(ObserverNumbers.Excitability), nameof(ObserverNumbers.Voice),
    ];
    /// <summary>The numbers that must be above 0.</summary>
    static readonly string[] AboveZero =
    [
        nameof(PerceptionNumbers.AwareAt), nameof(PerceptionNumbers.Ceiling), nameof(PerceptionNumbers.GainPerSecond), nameof(PerceptionNumbers.BiggestAct),
        nameof(PerceptionNumbers.HeatCoolSeconds), nameof(ObserverNumbers.SightRange), nameof(ObserverNumbers.ConeDegrees), nameof(ObserverNumbers.CoolDownSeconds),
        nameof(PerceptionNumbers.TurnDegreesPerSecond), nameof(PerceptionNumbers.LookSeconds), nameof(PerceptionNumbers.MotionStill),
        nameof(PerceptionNumbers.MotionCrouched), nameof(PerceptionNumbers.MotionWalk), nameof(PerceptionNumbers.MotionRun), nameof(PerceptionNumbers.MotionSprint),
        nameof(PerceptionNumbers.JoinRung), nameof(PerceptionNumbers.GiveUpMetres), nameof(PerceptionNumbers.GiveUpSeconds),
        nameof(PerceptionNumbers.GiveUpUnseenSeconds),
    ];

    /// <summary>The file's schema, or null when it names none.</summary>
    public static string? SchemaOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

    /// <summary>The world's numbers and act table of <paramref name="id"/>, or null with every problem added.</summary>
    public static (PerceptionNumbers Numbers, ActResponse[] Acts, bool Sandbox, string? DefaultKind)? ReadWorld(AssetId id, JsonElement root, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(id.ToString(), problems);
        if (!Top(c, root, WorldKeys, WorldSchema, out bool sandbox)) return null;
        string? defaultKind = c.Str(root, "defaultKind", required: false);
        if (c.Obj(root, "numbers") is not { } numbers) return null;
        var n = Numbers<PerceptionNumbers>(c, numbers, Shares);
        var acts = c.Obj(root, "acts") is { } table ? Acts(c, table) : null;
        if (problems.Count == before)
        {
            if (!(n.AwareAt < n.AlertedAt && n.AlertedAt < n.AlarmedAt && n.AlarmedAt < n.Ceiling))
                c.Fail($"numbers: the ladder must rise, AwareAt {n.AwareAt} < AlertedAt {n.AlertedAt} < AlarmedAt {n.AlarmedAt} < Ceiling {n.Ceiling}");
            if (n.BiggestAct < 1) c.Fail($"numbers: BiggestAct {n.BiggestAct} is below 1 (an act counts at least as big as a human's)");
            if (n.HeatAwayFactor < 1) c.Fail($"numbers: HeatAwayFactor {n.HeatAwayFactor} is below 1 (an area cools no slower with the player gone)");
            if (!(n.ChallengeMetres <= n.ChallengeWalkMetres && n.ChallengeWalkMetres < n.ChallengeRunMetres))
                c.Fail($"numbers: the challenge's distances must rise, ChallengeMetres {n.ChallengeMetres} <= ChallengeWalkMetres {n.ChallengeWalkMetres} < ChallengeRunMetres {n.ChallengeRunMetres}");
            if (n.JoinRung != MathF.Floor(n.JoinRung)) c.Fail($"numbers: JoinRung {n.JoinRung} is a rung, a whole number (2 alerted, 3 alarmed, 4 or more never)");
        }
        return problems.Count == before && acts is not null ? (n, acts, sandbox, defaultKind) : null;
    }

    /// <summary>The act table: every <see cref="Platform.Control.MasqueradeAct"/> by its name, each with its strangeness and threat (shares) and the highest rung it raises by itself.</summary>
    static ActResponse[] Acts(JsonChecks c, JsonElement table)
    {
        var names = Enum.GetNames<Platform.Control.MasqueradeAct>();
        var acts = new ActResponse[names.Length];
        foreach (var p in table.EnumerateObject())
            if (Array.IndexOf(names, p.Name) < 0) c.Fail($"acts: unknown act '{p.Name}' (the acts are {string.Join(", ", names)})");
        for (int i = 0; i < names.Length; i++)
        {
            string what = $"acts: '{names[i]}'";
            if (!table.TryGetProperty(names[i], out var a) || a.ValueKind != JsonValueKind.Object) { c.Fail($"{what} is missing (an object of {string.Join(", ", ActKeys)})"); continue; }
            foreach (var p in a.EnumerateObject())
                if (Array.IndexOf(ActKeys, p.Name) < 0) c.Fail($"{what}: unknown field '{p.Name}' (the fields are {string.Join(", ", ActKeys)})");
            float Share(string key)
            {
                if (!a.TryGetProperty(key, out var v)) { c.Fail($"{what}: '{key}' is missing"); return 0f; }
                if (c.Num(v, $"{what}: '{key}'") is not { } d) return 0f;
                if (d is < 0 or > 1) { c.Fail($"{what}: '{key}' is {d}; it is a share, 0 to 1"); return 0f; }
                return (float)d;
            }
            float strangeness = Share("strangeness"), threat = Share("threat");
            var highest = c.Str(a, "highest") switch
            {
                "aware" => Awareness.Aware,
                "alerted" => Awareness.Alerted,
                "alarmed" => Awareness.Alarmed,
                null => Awareness.Unaware,
                var h => Bad(c, $"{what}: highest is '{h}'; it is aware, alerted or alarmed"),
            };
            acts[i] = new ActResponse(strangeness, threat, highest);
        }
        return acts;
    }

    static Awareness Bad(JsonChecks c, string message) { c.Fail(message); return Awareness.Unaware; }

    /// <summary>The kind of observer of <paramref name="id"/>, or null with every problem added.</summary>
    public static ObserverKind? ReadKind(AssetId id, JsonElement root, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(id.ToString(), problems);
        if (!Top(c, root, KindKeys, KindSchema, out bool sandbox)) return null;
        string title = c.Str(root, "title") ?? "";
        var characters = new List<string>();
        if (c.Arr(root, "characters") is { } list)
            foreach (var ch in list.EnumerateArray())
            {
                if (ch.ValueKind == JsonValueKind.String && ch.GetString() is { Length: > 0 } name) characters.Add(name);
                else c.Fail("characters: each is a 1.1 character's name (a string)");
            }
        if (c.Obj(root, "numbers") is not { } numbers) return null;
        var n = Numbers<ObserverNumbers>(c, numbers, KindShares);
        if (problems.Count == before && n.ConeDegrees > 180) c.Fail($"numbers: ConeDegrees {n.ConeDegrees} is a half-angle, at most 180");
        DuelBrainSettings? duel = root.TryGetProperty("duel", out var d) ? Duel(c, d) : null;
        return problems.Count == before ? new ObserverKind(id, title, characters, n, sandbox, duel) : null;
    }

    /// <summary>A kind's <c>duel</c>: the duelling brain's defaults with each value given (<see cref="DuelKeys"/>), checked by name.</summary>
    static DuelBrainSettings? Duel(JsonChecks c, JsonElement d)
    {
        if (d.ValueKind != JsonValueKind.Object) { c.Fail("duel: must be an object of the duelling brain's numbers"); return null; }
        var settings = new DuelBrainSettings();
        foreach (var p in d.EnumerateObject())
        {
            int k = Array.FindIndex(DuelKeys, x => x.Key == p.Name);
            if (k < 0) { c.Fail($"duel: unknown number '{p.Name}' (the numbers are {string.Join(", ", DuelKeys.Select(x => x.Key))})"); continue; }
            if (c.Num(p.Value, $"duel: '{p.Name}'") is not { } v) continue;
            var (key, lo, hi, with) = DuelKeys[k];
            if (v < lo || v > hi) { c.Fail($"duel: '{key}' is {v}; it is {lo} to {hi}"); continue; }
            settings = with(settings, (float)v);
        }
        return settings;
    }

    static bool Top(JsonChecks c, JsonElement root, string[] keys, string schema, out bool sandbox)
    {
        sandbox = false;
        if (root.ValueKind != JsonValueKind.Object) { c.Fail("must be an object"); return false; }
        foreach (var p in root.EnumerateObject())
            if (Array.IndexOf(keys, p.Name) < 0) c.Fail($"unknown field '{p.Name}' (the fields are {string.Join(", ", keys)})");
        c.Schema(root, schema);
        var status = c.Str(root, "status");
        if (status is not null && status != Sandbox) c.Fail($"status is '{status}'; the only status known is '{Sandbox}' (the engine's research numbers) until the rules package's");
        sandbox = status == Sandbox;
        c.Str(root, "title");
        c.Str(root, "note", required: false);
        return true;
    }

    /// <summary>Every number of <typeparamref name="T"/> by its name, each given once, none unknown, none below 0, <paramref name="shares"/> at most 1.</summary>
    static T Numbers<T>(JsonChecks c, JsonElement numbers, string[] shares) where T : struct
    {
        var given = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in numbers.EnumerateObject())
        {
            if (!given.TryAdd(p.Name, p.Value)) { c.Fail($"numbers: '{p.Name}' is given twice"); continue; }
            if (!NumberFields<T>.Has(p.Name)) c.Fail($"numbers: unknown number '{p.Name}' (the numbers are {string.Join(", ", NumberFields<T>.Names)})");
        }
        var t = default(T);
        foreach (var name in NumberFields<T>.Names)
        {
            if (!given.TryGetValue(name, out var v)) { c.Fail($"numbers: '{name}' is missing"); continue; }
            if (c.Num(v, $"numbers: '{name}'") is not { } d) continue;
            if (d < 0) { c.Fail($"numbers: '{name}' is {d}; it must not be below 0"); continue; }
            if (d == 0 && Array.IndexOf(AboveZero, name) >= 0) { c.Fail($"numbers: '{name}' must be above 0"); continue; }
            if (d > 1 && Array.IndexOf(shares, name) >= 0) { c.Fail($"numbers: '{name}' is {d}; it is a share, 0 to 1"); continue; }
            t = NumberFields<T>.With(t, name, (float)d);
        }
        return t;
    }
}
