// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// Who drives a run or an actor (the execution modes): the engine's own AI, a script, both, a fixed
/// contact or the player's input replayed (later). The compiler derives it from the control blocks; <c>--check</c> prints it
/// and <c>results.json</c> carries it.
/// </summary>
public static class ExecutionModes
{
    /// <summary>The engine's own AI drives it (the duelling brain, or the arena's standing or blocking dummy): play's behaviour.</summary>
    public const string NativeAi = "NATIVE_AI";
    /// <summary>A script drives it and no AI: every channel the steps hold, nothing else.</summary>
    public const string ControlledScript = "CONTROLLED_SCRIPT";
    /// <summary>A script and an AI share it (channels held, muted, handed over): behaviour that does not happen in play.</summary>
    public const string Hybrid = "HYBRID";
    /// <summary>A fixed contact, no animation (the rules package's harness as a scenario kind): an earlier build.</summary>
    public const string FixedContact = "FIXED_CONTACT";
    /// <summary>An actor driven through the player's own controls (synthesised action frames): not built.</summary>
    public const string PlayerInputReplay = "PLAYER_INPUT_REPLAY";

    /// <summary>Each mode, whether this build has it, and one sentence (for <c>--describe</c>).</summary>
    public static readonly (string Mode, string Status, string Meaning)[] All =
    [
        (NativeAi, "built", "the engine's own AI drives the actor: the duelling brain (\"ai\": \"duel\") or the arena's dummies (\"stand\", \"block\"), no script, nothing muted"),
        (ControlledScript, "built", "a script drives the actor and it has no AI (\"ai\": \"none\" or no ai): the director holds the channels its steps need"),
        (Hybrid, "built", "a script (or a trigger's step) and an AI share the actor, or part of its AI is muted: behaviour that does not happen in play; measures the controlled thing"),
        (FixedContact, "built", "a scenario of \"kind\": \"contact\": contacts placed at a time on a region with a given closing speed, squareness and place on the blade, resolved by the same harm as a swept contact; no swing, no animation; never mixed with native blows (no attack or bow step, no player's input; a duelling brain muted on attack)"),
        (PlayerInputReplay, "built", "one actor driven through the player's own controls by synthesised input frames (control.playerInput: move keys, the look turned, buttons held and pressed): the camera, the crosshair and the automatic directions as in play"),
    ];
}

/// <summary>The trace policies (<c>tracePolicy</c>): which runs keep their event stream and tape.</summary>
public static class TracePolicies
{
    public const string None = "NONE", Sampled = "SAMPLED", Failures = "FAILURES", All = "ALL";
    public static readonly string[] Names = [None, Sampled, Failures, All];

    public static readonly (string Name, string Meaning)[] Help =
    [
        (None, "no event stream and no tape for any run (results.json and the tables only)"),
        (Sampled, "the first traceRuns runs of each point and every run that failed (an eachRun or anyRun assertion failed in it, a built-in check broke, it threw): the default, as \"events\": \"first\""),
        (Failures, "every run that failed, and only those"),
        (All, "every run (as \"events\": \"all\")"),
    ];
}

/// <summary>A swept point: its values, the actors made as the arena's cast, the director's program, and its own switches, tempo and time.</summary>
public sealed record SweepPoint
{
    public required int Index { get; init; }
    /// <summary><c>Guard.place.distanceMetres = 20</c>, the values joined by <c>, </c>; empty when nothing is swept.</summary>
    public required string Name { get; init; }
    /// <summary>The swept paths and their values at this point (invariant JSON text: <c>20</c>, <c>"BroadSword"</c>, <c>true</c>).</summary>
    public required IReadOnlyList<(string Path, string Value)> Values { get; init; }
    /// <summary>The actors as the arena makes fighters, in the scenario's order (an actor's index is its index here).</summary>
    public required ArenaCast Cast { get; init; }
    /// <summary>The director's program (the director work's model), or null when nothing is scripted, muted or triggered (no director in the world).</summary>
    public ScenarioProgram? Program { get; init; }
    /// <summary>The switches every run of the point plays with (the scenario's, with the swept ones on or off).</summary>
    public IReadOnlyList<string> Switches { get; init; } = [];
    public float? CombatTempo { get; init; }
    public bool Night { get; init; } = true;
    /// <summary>Each actor's execution mode (<see cref="ExecutionModes"/>), in the actors' order.</summary>
    public IReadOnlyList<string> ActorModes { get; init; } = [];
    /// <summary>The run's execution mode: every actor's when they agree, else <see cref="ExecutionModes.Hybrid"/>.</summary>
    public string ExecutionMode { get; init; } = ExecutionModes.NativeAi;
    /// <summary>
    /// The setup conditions the scenario asks of each actor at this point (by design: assigning a trait or a power is an
    /// explicit condition recorded in provenance): <c>{ "actor", "kind", "id", "value", "source": "scenario" | "sweep" | "preset" }</c>.
    /// </summary>
    public JsonArray Setup { get; init; } = [];
    /// <summary>The point's whole scenario resolved (the sweep's values set in it), as --check prints it.</summary>
    public JsonObject? Resolved { get; init; }
    /// <summary>The point's module data overrides (<see cref="SessionOptions.DataOverrides"/>'s text), or null for none.</summary>
    public string? DataOverrides { get; init; }
    /// <summary>The light over the time's (<see cref="SessionOptions.Light"/>'s text), or null for the time's.</summary>
    public string? Light { get; init; }
}

/// <summary>A scenario read, resolved and compiled: what the runner runs.</summary>
public sealed record CompiledScenario
{
    public required string File { get; init; }
    public required string Text { get; init; }
    public required string Sha256 { get; init; }
    public string Format { get; init; } = ScenarioWords.Format;
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Kind { get; init; } = "fight";
    /// <summary>The ground's name (<c>free-yard</c>) or <c>level</c>.</summary>
    public string Ground { get; init; } = ScenarioGround.FreeYard;
    /// <summary>The level package's folder (full path).</summary>
    public required string Level { get; init; }
    public int Runs { get; init; } = 10;
    /// <summary>Run r of every point has seed <c>Seed + r</c>.</summary>
    public ulong Seed { get; init; } = 1;
    public required ScenarioStop Stop { get; init; }
    public required IReadOnlyList<SweepPoint> Points { get; init; }
    public required IReadOnlyList<MeasureSpec> Measures { get; init; }
    public required IReadOnlyList<AssertionSpec> Assertions { get; init; }
    public required ScenarioOutputs Outputs { get; init; }
    /// <summary>The trace policy (<see cref="TracePolicies"/>) and its budget, for the runner (the outputs' events and traceRuns say the same).</summary>
    public string TracePolicy { get; init; } = TracePolicies.Sampled;
    public double TraceBudgetMegabytes { get; init; } = 256;
    /// <summary>The run's execution mode over every point (<see cref="ExecutionModes"/>).</summary>
    public string ExecutionMode { get; init; } = ExecutionModes.NativeAi;
    /// <summary>Whether any run is <c>CONTROLLED</c> (an actor not on its native AI alone): the design's label for results.</summary>
    public bool Controlled => ExecutionMode != ExecutionModes.NativeAi;
    /// <summary>The scenario resolved, every default explicit (<c>resolved.json</c>).</summary>
    public required string ResolvedJson { get; init; }
    /// <summary>The objectives (none: the scenario gives none).</summary>
    public IReadOnlyList<ObjectiveSpec> Objectives { get; init; } = [];
    /// <summary>What the reader warned of without refusing.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The measures part as one record (the results work's <see cref="ScenarioMeasuresModel"/>).</summary>
    public ScenarioMeasuresModel MeasuresModel => new(Measures, Assertions, Stop, Outputs);
}

/// <summary>A scenario read: the compiled scenario when it is valid, and every error found (none when valid).</summary>
public sealed record ScenarioLoad(CompiledScenario? Scenario, IReadOnlyList<ScenarioError> Errors)
{
    public bool Ok => Scenario is not null && Errors.Count == 0;
    /// <summary>What the reader warned of without refusing.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>The scenario resolved as far as it was read (also when there are errors), for <c>--check</c>.</summary>
    public JsonObject? Resolved { get; init; }
}

/// <summary>
/// The scenario file's reader: <c>"format": "vitomir-scenario/1"</c>, JSON with
/// comments and trailing commas, strict, every error by its JSON pointer and code with the nearest names; resolved (every default
/// explicit) and compiled into the arena's cast, the director work's <see cref="ScenarioProgram"/> and the results work's measures model, a
/// point a sweep's combination.
/// </summary>
public static class ScenarioSpec
{
    /// <summary>
    /// Reads, resolves, checks and compiles a scenario file; relative paths in it are from its folder. <paramref name="game"/>: the
    /// loaded game's names (<see cref="ScenarioGame.Load"/>) — null checks the form alone. <paramref name="level"/>: <c>--level</c>.
    /// </summary>
    public static ScenarioLoad Load(string path, ScenarioGame? game = null, string? level = null)
    {
        string text;
        try { text = System.IO.File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ScenarioLoad(null, [new ScenarioError(ScenarioErrorCode.FileMissing, "", $"the scenario {path} could not be read ({e.Message})", [])]);
        }
        return Read(text, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", game, level, Path.GetFullPath(path));
    }

    static readonly JsonDocumentOptions Reading = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The resolved form written for a reader: indented, the actors' names as they are (no escaping of letters).</summary>
    public static readonly JsonSerializerOptions Writing = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Reads a scenario from its text; <paramref name="folder"/> is where relative paths start.</summary>
    public static ScenarioLoad Read(string text, string folder, ScenarioGame? game = null, string? level = null, string file = "", bool levelFromEnvironment = true)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text, Reading); }
        catch (JsonException e)
        {
            return new ScenarioLoad(null, [new ScenarioError(ScenarioErrorCode.NotJson, "", $"the file is not JSON (with comments and trailing commas): {e.Message}", [])]);
        }
        var reader = new ScenarioReader(folder, game);
        JsonObject? resolved;
        using (doc) resolved = reader.Document(doc.RootElement);
        if (resolved is null) return new ScenarioLoad(null, reader.Errors);
        // Every value read back as JSON (so a number reads as any number type, as a file's would).
        resolved = JsonNode.Parse(resolved.ToJsonString())!.AsObject();

        // The ground's level: the scenario's own, else --level, else VITOMIR_COURSE_LEVEL.
        string? lv = resolved["ground"] is JsonObject g ? g["level"]?.GetValue<string>() : level ?? (levelFromEnvironment ? Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL") : null);
        bool range = resolved["ground"] is JsonValue gv && gv.TryGetValue(out string? gname) && gname == ScenarioGround.Range;
        if (range) lv = Space.BuiltInGrounds.Range;   // built in code: no level file, no VITOMIR_COURSE_LEVEL needed
        else if (string.IsNullOrWhiteSpace(lv)) reader.Error(ScenarioErrorCode.NoLevel, "/ground", "no level for the free yard: set VITOMIR_COURSE_LEVEL to the proving ground's package (proving-ground.l2), or give --level");
        else
        {
            lv = Path.GetFullPath(lv.TrimEnd('/', '\\'));
            if (resolved["ground"] is not JsonObject && !System.IO.File.Exists(Path.Combine(lv, "level.json")))
                reader.Error(ScenarioErrorCode.LevelNotFound, "/ground", $"{lv} (VITOMIR_COURSE_LEVEL or --level) is not a level package (it has no level.json)");
        }
        if (reader.Errors.Count > 0) return new ScenarioLoad(null, reader.Errors) { Warnings = reader.Warnings, Resolved = resolved };

        var compiler = new ScenarioCompiler(reader, resolved, game);
        var points = ScenarioSwap.Apply(compiler.Points(), resolved, game, reader);   // each point and its swapped twin, when the scenario swaps
        var measures = compiler.MeasureSpecs();
        var assertions = compiler.AssertionSpecs();
        var stop = compiler.Stop();
        var (outputs, policy, budget) = compiler.Outputs();
        if (reader.Errors.Count > 0) return new ScenarioLoad(null, reader.Errors) { Warnings = reader.Warnings, Resolved = resolved };

        var modes = points.Select(p => p.ExecutionMode).Distinct().ToList();
        string mode = resolved["kind"]!.GetValue<string>() == "contact" ? ExecutionModes.FixedContact : modes.Count == 1 ? modes[0] : ExecutionModes.Hybrid;
        resolved["executionMode"] = mode;
        var scenario = new CompiledScenario
        {
            File = file,
            Text = text,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            Title = resolved["title"]!.GetValue<string>(),
            Notes = resolved["notes"]!.GetValue<string>(),
            Kind = resolved["kind"]!.GetValue<string>(),
            Ground = resolved["ground"] is JsonObject ? "level" : resolved["ground"]!.GetValue<string>(),
            Level = lv!,
            Runs = (int)resolved["runs"]!.GetValue<long>(),
            Seed = (ulong)resolved["seed"]!.GetValue<long>(),
            Stop = stop,
            Points = points,
            Measures = measures,
            Assertions = assertions,
            Outputs = outputs,
            TracePolicy = policy,
            TraceBudgetMegabytes = budget,
            ExecutionMode = mode,
            ResolvedJson = resolved.ToJsonString(Writing),
            Warnings = reader.Warnings,
            Objectives = ObjectiveSpec.From(resolved),
        };
        return new ScenarioLoad(scenario, []) { Warnings = reader.Warnings, Resolved = resolved };
    }

    /// <summary>A JSON value's invariant text (numbers as the shortest round trip).</summary>
    internal static string ValueText(JsonNode? v) => v?.ToJsonString() ?? "null";
}
