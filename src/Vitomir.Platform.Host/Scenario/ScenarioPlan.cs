// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>One point of a scenario's sweep as the runner runs it: its values, the cast, the program and the world's settings there.</summary>
/// <param name="Index">Its index (0 up).</param>
/// <param name="Name">Its name (<c>Christof.sheet.values.melee = 3</c>; empty when nothing is swept).</param>
/// <param name="Values">The swept paths and their values at this point, as invariant text.</param>
/// <param name="Cast">The actors as the arena's fighters (the session's <see cref="SessionOptions.Arena"/>).</param>
/// <param name="Program">The director's program (null: no director: an arena world).</param>
/// <param name="Switches">The scenario's switches at this point (joined to the command line's).</param>
/// <param name="CombatTempo">The global combat tempo, or null for the module's.</param>
/// <param name="Night">Night (light 0.3) or day.</param>
/// <param name="ExecutionMode">Who drives the point's runs (the reader's: NATIVE_AI, CONTROLLED_SCRIPT, HYBRID), or null for the runner to say.</param>
/// <param name="ActorModes">Each actor's mode, in the cast's order (the reader's), or empty.</param>
/// <param name="Setup">What the scenario set on each actor at this point (sheet template, traits, powers, Health, Blood, gear: the reader's), for the provenance.</param>
public sealed record ScenarioPlanPoint(int Index, string Name, IReadOnlyList<(string Path, string Value)> Values, ArenaCast Cast,
    ScenarioProgram? Program, IReadOnlyList<string> Switches, float? CombatTempo, bool Night,
    string? ExecutionMode = null, IReadOnlyList<string>? ActorModes = null, JsonArray? Setup = null,
    string? DataOverrides = null, string? Light = null);   // the module data overrides and the light, null for none

/// <summary>
/// A scenario ready to run: what the runner needs from the format work's compiled scenario (<c>ScenarioSpec.Load</c>), in
/// the runner's own record so that it can be made by the reader, by a test, or from an arena spec (<see cref="FromArena"/>: the
/// arena-equivalent scenario whose tables must equal <c>--arena</c>'s byte for byte).
/// </summary>
public sealed record ScenarioPlan
{
    public required string File { get; init; }
    public string Text { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Format { get; init; } = "vitomir-scenario/1";
    public string? Title { get; init; }
    public string? Notes { get; init; }
    /// <summary>The level's package (full path).</summary>
    public required string Level { get; init; }
    /// <summary>The named ground, or null for a level given by path.</summary>
    public string? Ground { get; init; }
    public int Runs { get; init; } = 10;
    public ulong Seed { get; init; } = 1;
    public required IReadOnlyList<ScenarioPlanPoint> Points { get; init; }
    public required ScenarioMeasuresModel Model { get; init; }
    /// <summary>The scenario resolved (every default explicit), written as <c>resolved.json</c>.</summary>
    public string ResolvedJson { get; init; } = "{}";

    /// <summary>The trace policy (the reader's <c>outputs.tracePolicy</c>), over the outputs' own.</summary>
    public TracePolicy? TracePolicy { get; init; }
    /// <summary>The most megabytes of event streams and tapes kept (the reader's <c>outputs.traceBudgetMegabytes</c>); 0: no limit.</summary>
    public double TraceBudgetMegabytes { get; init; }
    /// <summary>Who drives the runs over the whole scenario (the reader's).</summary>
    public string? ExecutionMode { get; init; }
    /// <summary>The reader's warnings, carried into results.json.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>The objectives, followed in every run (none: the scenario gives none).</summary>
    public IReadOnlyList<ObjectiveSpec> Objectives { get; init; } = [];

    /// <summary>
    /// The runner's plan from the format work's compiled scenario (<see cref="ScenarioSpec.Load"/>): its points (values as text: a JSON
    /// string's quotes taken off), casts, programs, measures, assertions, stop, outputs, trace policy and budget.
    /// </summary>
    public static ScenarioPlan From(CompiledScenario c) => new()
    {
        File = c.File, Text = c.Text, Sha256 = c.Sha256, Format = c.Format, Title = c.Title, Notes = c.Notes, Level = c.Level, Ground = c.Ground,
        Runs = c.Runs, Seed = c.Seed, ResolvedJson = c.ResolvedJson,
        Points = [.. c.Points.Select(p => new ScenarioPlanPoint(p.Index, p.Name, [.. p.Values.Select(v => (v.Path, Unquote(v.Value)))], p.Cast, p.Program,
            p.Switches, p.CombatTempo, p.Night, p.ExecutionMode, p.ActorModes, p.Setup, p.DataOverrides, p.Light))],
        Model = new ScenarioMeasuresModel(c.Measures, c.Assertions, c.Stop, c.Outputs),
        TracePolicy = c.TracePolicy.ToUpperInvariant() switch
        {
            "NONE" => Scenario.TracePolicy.None, "FAILURES" => Scenario.TracePolicy.Failures, "ALL" => Scenario.TracePolicy.All, _ => Scenario.TracePolicy.Sampled,
        },
        TraceBudgetMegabytes = c.TraceBudgetMegabytes, ExecutionMode = c.ExecutionMode, Warnings = c.Warnings,
        Objectives = c.Objectives,
    };

    /// <summary>A value as the tables write it: a JSON string without its quotes, anything else as it is.</summary>
    public static string Unquote(string v)
    {
        if (v.Length >= 2 && v[0] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(v) ?? v; }
            catch (JsonException) { return v; }
        }
        return v;
    }

    /// <summary>The SHA-256 of a text as lower-case hex.</summary>
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// The arena-equivalent scenario of an arena spec: the same fighters (the spec's casts point by point), runs = bouts, the same
    /// seed and seconds, the arena's stop (the first harness stop), no program, no measures but the built-in ones. Its
    /// <c>runs.csv</c>, <c>contacts.csv</c> and <c>powers.csv</c> equal the arena's <c>bouts.csv</c>, <c>contacts.csv</c> and
    /// <c>powers.csv</c> byte for byte.
    /// </summary>
    public static ScenarioPlan FromArena(ArenaSpec spec, string file = "arena-equivalent")
    {
        var points = spec.Points.Select((values, i) => new ScenarioPlanPoint(i,
            values.Length == 0 ? "" : string.Join("; ", spec.Sweeps.Select((s, k) => $"{SweepPath(spec, s)} = {values[k].ToString(CultureInfo.InvariantCulture)}")),
            [.. spec.Sweeps.Select((s, k) => (SweepPath(spec, s), values[k].ToString(CultureInfo.InvariantCulture)))],
            spec.Cast(values), null, spec.Switches, spec.CombatTempo, spec.Night)).ToList();
        return new ScenarioPlan
        {
            File = file, Text = spec.Text, Sha256 = Hash(spec.Text), Title = spec.Title, Level = spec.Level,
            Runs = spec.Bouts, Seed = spec.Seed, Points = points,
            Model = new ScenarioMeasuresModel([], [], new ScenarioStop(spec.Seconds, HarnessStops.First), new ScenarioOutputs(TraceRuns.First, spec.Tapes, Tapes: spec.Tapes)),
        };
    }

    /// <summary>An arena sweep's settable path (<c>Christof.sheet.values.melee</c>, <c>Christof.sheet.powers.celerity.rank</c>).</summary>
    public static string SweepPath(ArenaSpec spec, ArenaSweep s) =>
        s.Power ? $"{spec.Fighters[s.Fighter].Name}.sheet.powers.{s.Id}.rank" : $"{spec.Fighters[s.Fighter].Name}.sheet.values.{s.Id}";
}
