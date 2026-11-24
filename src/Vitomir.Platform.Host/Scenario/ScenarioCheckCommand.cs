// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// <c>--scenario &lt;file|folder&gt; --check [--json]</c>: reads each scenario, checks
/// every name against the loaded game (characters, presets, sheets, traits, weapons, armour, powers and options, switches) and
/// every place against the ground, builds each point's first world (no tick) so the module refuses what it would refuse at a
/// run's start, and prints the scenario resolved — every default explicit — with its points, its actors' places and execution
/// modes and its setup conditions. Exit code 0 when every scenario is valid, 1 when one is not, 2 when the modules refuse.
/// Runs nothing.
/// </summary>
public static partial class ScenarioCheckCommand   // partial (ScenarioCheckCommand.Overrides.cs)
{
    /// <summary>The scenario files a path names: the file, or every <c>*.scenario.json</c> in a folder (name order).</summary>
    public static IReadOnlyList<string> Files(string path) =>
        Directory.Exists(path) ? [.. Directory.GetFiles(path, "*.scenario.json").Order(StringComparer.Ordinal)] : [path];

    internal static int Run(Cli cli, PlatformSettings settings, HostLog log, string path, TextWriter output)
    {
        bool json = cli.Flag("--json");
        bool world = !cli.Flag("--check-no-world");
        var files = Files(path);
        var baseline = new SessionOptions
        {
            Level = "",
            Game = Program.GameFolder(cli, settings),
            GameList = ModulesCommand.GameChoice(cli),
            ModulesFolder = cli.Arg("--modules"),
            Switches = ModulesCommand.Switches(cli),
            Say = log.Say,
            LegTurn = !cli.Flag("--no-leg-turn"),
            DirectionPace = !cli.Flag("--no-direction-pace"),
            CombatTempo = Program.CombatTempoOf(cli),
        };
        string? level = cli.Arg("--level");
        using var game = ScenarioGame.Load(baseline, null);
        var levels = new Dictionary<string, LevelSpace?>(StringComparer.Ordinal);
        var reports = new JsonArray();
        bool allOk = files.Count > 0;
        if (files.Count == 0) log.Say($"check: {path}: no *.scenario.json in it");
        SessionContent? content = null;
        try
        {
            foreach (var file in files)
            {
                // The ground's level (named grounds: --level or VITOMIR_COURSE_LEVEL), for places; loaded once each.
                var first = ScenarioSpec.Load(file, null, level);
                string? lv = first.Scenario?.Level ?? (first.Resolved?["ground"] is JsonObject g ? g["level"]?.GetValue<string>() : level ?? Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL"));
                LevelSpace? space = null;
                if (lv is not null && !levels.TryGetValue(lv, out space))
                {
                    try { space = File.Exists(Path.Combine(lv, "level.json")) ? LevelSpace.Open(lv) : null; }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { log.Say($"check: the level {lv} could not be read ({e.Message})"); }
                    levels[lv] = space;
                }
                var load = first.Errors.Any(e => e.Code is ScenarioErrorCode.NotJson or ScenarioErrorCode.FileMissing or ScenarioErrorCode.UnknownFormat)
                    ? first : ScenarioSpec.Load(file, game.At(space is null ? null : lv, space), level);
                var errors = load.Errors.ToList();
                if (load.Scenario is { } scenario && world && game.HasTemplate is not null)
                    errors.AddRange(Worlds(scenario, baseline, ref content, log));
                bool ok = load.Scenario is not null && errors.Count == 0;
                allOk &= ok;
                reports.Add(Report(file, load, errors, ok));
                if (TakeClips() is { } actorClips) ((JsonObject)reports[^1]!)["clips"] = actorClips;   // each actor's clips (overrides.clipRate)
                if (!json) Print(file, load, errors, ok, log);
            }
        }
        finally { content?.Dispose(); }
        if (json)
        {
            JsonNode doc = files.Count == 1 ? reports[0]!.DeepClone() : new JsonObject { ["ok"] = allOk, ["scenarios"] = reports.DeepClone() };
            output.WriteLine(doc.ToJsonString(ScenarioSpec.Writing));
        }
        return allOk ? 0 : 1;
    }

    /// <summary>What the game refuses when each point's first world is built (<see cref="Worlds"/>), the content loaded and let go here.</summary>
    public static IReadOnlyList<ScenarioError> WorldErrors(CompiledScenario scenario, SessionOptions baseline, HostLog log)
    {
        SessionContent? content = null;
        try { return Worlds(scenario, baseline, ref content, log); }
        finally { content?.Dispose(); }
    }

    /// <summary>
    /// Each point's first world built (no tick), as a run would build it, so what only the module can refuse — a sheet value out of
    /// its range, an option its rules do not read, a weapon without the shield asked for, a template it does not know — is found
    /// now, each error at its actor (<see cref="ScenarioErrorCode.Refused"/>).
    /// </summary>
    static List<ScenarioError> Worlds(CompiledScenario scenario, SessionOptions baseline, ref SessionContent? content, HostLog log)
    {
        var errors = new List<ScenarioError>();
        var actors = scenario.Points[0].Cast.Fighters.Select(f => f.Name).ToList();
        foreach (var point in scenario.Points)
        {
            var options = OptionsFor(scenario, point, 0, baseline) with { Say = _ => { } };
            if (content is not null && content.LoadedFor != SessionContent.KeyOf(options)) { content.Dispose(); content = null; }
            try
            {
                content ??= Session.LoadContent(options with { Say = log.Say });
                using var session = Session.Create(options with { Content = content });
                if (point.Index == 0) SayClips(session, actors, log);   // the clips a clip rate may name
            }
            catch (Exception e) when (point.DataOverrides is not null && e is not SessionException && e is not (OutOfMemoryException or StackOverflowException))
            {
                errors.AddRange(DataRefusals(scenario, point, baseline, e));   // the module refused the data as overridden
            }
            catch (SessionException e)
            {
                string message = e.Message.StartsWith("arena: ", StringComparison.Ordinal) ? e.Message[7..] : e.Message;
                int who = actors.FindIndex(a => message.StartsWith($"fighter {a}:", StringComparison.Ordinal));
                if (who >= 0) message = message[$"fighter {actors[who]}:".Length..].Trim();
                if (who >= 0 && ClipRateError(scenario, point, actors[who], message) is { } clipError) { errors.Add(clipError); continue; }
                errors.Add(new ScenarioError(ScenarioErrorCode.Refused, who >= 0 ? $"/actors/{who}" : "",
                    $"{(who >= 0 ? actors[who] + ": " : "")}the game refused it when the world was built{(point.Name.Length == 0 ? "" : $" at point {point.Index} ({point.Name})")}: {message}", []));
            }
        }
        return errors;
    }

    /// <summary>
    /// A run's session options: the command line's game and switches with the point's, the scenario's level, no townsfolk,
    /// the point's cast and seed <c>Seed + run</c> — the arena's (<see cref="ArenaRun.OptionsFor"/>), so a scenario's run is the arena's
    /// bout for the same cast. The results work's runner may call this.
    /// </summary>
    public static SessionOptions OptionsFor(CompiledScenario scenario, SweepPoint point, int run, SessionOptions baseline) => baseline with
    {
        Switches = point.Switches.Count == 0 ? baseline.Switches : new HashSet<string>(baseline.Switches.Concat(point.Switches), StringComparer.Ordinal),
        CombatTempo = baseline.CombatTempo ?? point.CombatTempo,
        Level = scenario.Level,
        Start = "0",
        People = 0,
        Witnesses = [],
        Night = point.Night,
        Place = null,
        TargetTest = null,
        NpcActions = null,
        Seed = scenario.Seed + (ulong)run,
        Arena = point.Cast.Write(),
        DataOverrides = point.DataOverrides,
        Light = point.Light,
    };

    static readonly JsonSerializerOptions ProgramJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A scenario's check as one JSON object (<c>--json</c>).</summary>
    static JsonObject Report(string file, ScenarioLoad load, List<ScenarioError> errors, bool ok)
    {
        var o = new JsonObject
        {
            ["file"] = Path.GetFullPath(file),
            ["ok"] = ok,
            ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode)new JsonObject
            {
                ["code"] = e.Code.ToString(),
                ["pointer"] = e.Pointer,
                ["message"] = e.Message,
                ["suggestions"] = new JsonArray([.. e.Suggestions.Select(s => (JsonNode?)s)]),
            })]),
            ["warnings"] = new JsonArray([.. load.Warnings.Select(w => (JsonNode?)w)]),
        };
        o["build"] = PlatformVersion.Text;
        o["git"] = ScenarioDescribe.Git();
        if (load.Scenario is not { } s) return o;
        o["sha256"] = s.Sha256;
        o["executionMode"] = s.ExecutionMode;
        o["controlled"] = s.Controlled;
        o["runs"] = s.Runs;
        o["points"] = new JsonArray([.. s.Points.Select(p => (JsonNode)new JsonObject
        {
            ["index"] = p.Index,
            ["name"] = p.Name,
            ["values"] = new JsonObject(p.Values.Select(v => KeyValuePair.Create(v.Path, JsonNode.Parse(v.Value)))),
            ["seeds"] = new JsonArray(s.Seed, s.Seed + (ulong)Math.Max(0, s.Runs - 1)),
            ["executionMode"] = p.ExecutionMode,
            ["actors"] = new JsonArray([.. p.Cast.Fighters.Select((f, i) => (JsonNode)new JsonObject
            {
                ["name"] = f.Name,
                ["executionMode"] = p.ActorModes[i],
                ["template"] = f.Template,
                ["weapon"] = f.Weapon,
                ["feet"] = new JsonArray(Math.Round(f.X, 4), Math.Round(f.Y, 4), Math.Round(f.Z, 4)),
                ["facingDegrees"] = Math.Round(f.Facing, 3),
                ["brain"] = f.Brain.ToString(),
                ["foe"] = p.Cast.Fighters[f.Foe].Name,
            })]),
            ["setup"] = p.Setup.DeepClone(),
            ["switches"] = new JsonArray([.. p.Switches.Select(x => (JsonNode?)x)]),
            ["director"] = p.Program is null ? null : JsonSerializer.SerializeToNode(p.Program, ProgramJson),
        })]);
        o["scenario"] = JsonNode.Parse(s.ResolvedJson);
        return o;
    }

    /// <summary>A scenario's check in words: its errors (one a line), or the resolved scenario and its points.</summary>
    static void Print(string file, ScenarioLoad load, List<ScenarioError> errors, bool ok, HostLog log)
    {
        if (!ok)
        {
            log.Say($"check: {Path.GetFullPath(file)}: refused, {errors.Count} error(s)");
            foreach (var e in errors) log.Say(e.Line());
            foreach (var w in load.Warnings) log.Say($"warning: {w}");
            return;
        }
        var s = load.Scenario!;
        log.Say($"check: {Path.GetFullPath(file)}: valid ({s.Points.Count} point(s) x {s.Runs} runs, seeds {s.Seed} to {s.Seed + (ulong)Math.Max(0, s.Runs - 1)} at every point; {s.ExecutionMode}{(s.Controlled ? ", CONTROLLED" : "")})");
        foreach (var w in load.Warnings) log.Say($"warning: {w}");
        log.Say("check: the scenario resolved (every default explicit):");
        foreach (var line in s.ResolvedJson.Split('\n')) log.Say(line.TrimEnd('\r'));
        foreach (var p in s.Points)
        {
            log.Say($"check: point {p.Index}{(p.Name.Length == 0 ? "" : $" ({p.Name})")}: {p.ExecutionMode}{(p.Program is null ? ", no director" : "")}");
            for (int i = 0; i < p.Cast.Fighters.Count; i++)
            {
                var f = p.Cast.Fighters[i];
                log.Say(string.Create(CultureInfo.InvariantCulture,
                    $"  {f.Name}: {p.ActorModes[i]}, {f.Template}{(f.Weapon is null ? "" : $" with {f.Weapon}")} at [{f.X:0.####}, {f.Y:0.####}, {f.Z:0.####}] facing {f.Facing:0.###} degrees, {f.Brain.ToString().ToLowerInvariant()} against {p.Cast.Fighters[f.Foe].Name}"));
            }
            foreach (var c in p.Setup) log.Say($"  setup: {c!["actor"]} {c["kind"]} {c["id"]} = {c["value"]?.ToJsonString()} ({c["source"]})");
        }
    }
}
