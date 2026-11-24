// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The program's <c>--scenario &lt;file|folder&gt; --out &lt;folder&gt;</c>: reads each scenario
/// with the format work's reader against the loaded game (a refusal prints every error by its JSON pointer: exit 1), runs it
/// (<see cref="ScenarioRunner"/>) into the folder — a folder of scenarios each into <c>&lt;out&gt;/&lt;name&gt;/</c> and a
/// <c>batch.json</c> — and exits with the worst code: 0 every assertion passed, 3 one failed, 4 none failed and one was inconclusive,
/// 1 refused, 2 the modules refused, 5 a run threw, 6 cancelled (Ctrl-C, or a file named CANCEL in the output folder). Options over the
/// file: <c>--runs n</c>, <c>--seed n</c>, <c>--only point</c>, <c>--trace-runs n</c>, <c>--jobs n</c>; <c>--sandbox</c> and the
/// game's switches as ever; <c>--level</c> for a named ground when <c>VITOMIR_COURSE_LEVEL</c> is not set. (<c>--check</c> and
/// <c>--describe</c> are the format work's.)
/// </summary>
internal static class ScenarioCommand
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    internal static int Run(Cli cli, PlatformSettings settings, HostLog log, string path)
    {
        if (cli.Arg("--out") is not { } outFolder)
        {
            log.Say("error: --scenario needs --out <folder> for results.json, the tables, the event streams and the tapes (or --check to validate only)");
            return 1;
        }
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
            StitchedAsQuick = Program.StitchedAsQuick(cli),
        };
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); log.Say("scenario: cancelling (the runs under way finish; results.json says CANCELLED)"); };
        Console.CancelKeyPress += onCancel;
        try
        {
            // ---- --only-run <run_id> [--compare <results.json>] [--trace-event <eventId>]: one run again, with its whole trace
            int? onlyPoint = null, onlyRun = null;
            ulong? onlySeed = null;   // the seed form, p003@4717: point 3 at that seed (its run: the seed less the file's first)
            (ulong Seed, string Digest, string ScenarioSha)? recorded = null;
            if (cli.Arg("--only-run") is { } runIdText)
            {
                if (ScenarioTraceEvent.TryPointSeed(runIdText, out int sp, out ulong ss)) (onlyPoint, onlySeed) = (sp, ss);
                else if (!ScenarioTraceEvent.TryRunId(runIdText, out int op, out int or))
                {
                    log.Say($"error: --only-run \"{runIdText}\" is not a run id: p<point>-r<run>, as runs.csv's run_id (p003-r017: point 3, run 17), or p<point>@<seed> (p003@4717: point 3 at seed 4717, as an assertion's first failing seed)");
                    return 1;
                }
                else (onlyPoint, onlyRun) = (op, or);
                if (onlySeed is null && cli.Arg("--compare") is { } compareWith)
                {
                    recorded = ScenarioTraceEvent.Recorded(compareWith, runIdText, out string why);
                    if (recorded is null) { log.Say($"error: --compare: {why}"); return 1; }
                }
            }
            else if (cli.Arg("--compare") is not null) { log.Say("error: --compare <results.json> goes with --only-run <run_id> (the run to compare)"); return 1; }
            // ---- end
            var options = new ScenarioRunOptions
            {
                Jobs = cli.Int("--jobs", 1),
                Runs = cli.Arg("--runs") is not null ? cli.Int("--runs", 0) : null,
                Seed = cli.Arg("--seed") is { } s && ulong.TryParse(s, NumberStyles.None, Ic, out ulong seed) ? seed : null,
                TraceRuns = cli.Arg("--trace-runs") is not null ? cli.Int("--trace-runs", 0) : null,
                Only = onlyPoint ?? (cli.Arg("--only") is not null ? cli.Int("--only", 0) : null),
                OnlyRun = onlyRun,
                OnlyRunSeed = recorded?.Seed,
                CommandLine = Vitomir.Platform.Modules.SecretSwitches.Line(Environment.GetCommandLineArgs().Skip(1)),
                Sandbox = cli.Flag("--sandbox"),
                Cancel = cancel.Token,
            };
            bool isFolder = Directory.Exists(path);
            var files = isFolder ? Directory.GetFiles(path, "*.scenario.json").Order(StringComparer.Ordinal).ToList() : [path];
            if (files.Count == 0) { log.Say($"error: {path}: no *.scenario.json in it"); return 1; }
            string level = cli.Arg("--level") ?? Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL") ?? "";
            using var game = ScenarioGame.Load(baseline, level.Length > 0 ? level : null);   // a modules' refusal throws: exit 2 (Program)
            var wall = Stopwatch.StartNew();
            string startedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", Ic);
            var entries = new List<BatchEntry>();
            foreach (var file in files)
            {
                if (cancel.IsCancellationRequested) break;
                string name = Path.GetFileName(file);
                string stem = name.EndsWith(".scenario.json", StringComparison.OrdinalIgnoreCase) ? name[..^".scenario.json".Length] : Path.GetFileNameWithoutExtension(name);
                string folder = isFolder ? Path.Combine(outFolder, stem) : outFolder;
                var load = ScenarioSpec.Load(file, game, cli.Arg("--level"));
                if (load.Ok && load.Scenario is { } checking && ScenarioCheckCommand.DataErrors(checking, baseline with { Level = checking.Level }) is { Count: > 0 } dataErrors)
                    load = load with { Scenario = null, Errors = [.. load.Errors, .. dataErrors] };   // the module refused its data as overridden
                if (!load.Ok || load.Scenario is null)
                {
                    foreach (var e in load.Errors) log.Say($"{name}: {e.Line()}");
                    log.Say($"scenario: {name} refused ({load.Errors.Count} error{(load.Errors.Count == 1 ? "" : "s")}); nothing ran: exit 1");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "results.json"), ScenarioJson.Write(new
                    {
                        schema = ScenarioResults.SchemaName, state = "FAILED", scenario = new { file = name },
                        errors = load.Errors.Select(e => new { code = e.Code.ToString(), pointer = e.Pointer, message = e.Message, suggestions = e.Suggestions }),
                        summary = new { exitCode = 1, verdict = "REFUSED" },
                    }));
                    entries.Add(new BatchEntry(name, Rel(outFolder, folder), 1, "REFUSED", 0, 0, 0, 0, $"{load.Errors.Count} errors"));
                    continue;
                }
                foreach (var w in load.Scenario.Warnings) log.Say($"{name}: warning: {w}");
                var plan = ScenarioPlan.From(load.Scenario);
                log.Say(string.Create(Ic, $"scenario: {Path.GetFullPath(file)}: {plan.Points[0].Cast.Fighters.Count} actors, {plan.Points.Count} point(s) × {options.Runs ?? plan.Runs} runs, at most {plan.Model.Stop.AfterSeconds:0.#} s each, seeds from {options.Seed ?? plan.Seed}, to {Path.GetFullPath(folder)}"));
                if (onlyPoint is { } opIndex && opIndex >= plan.Points.Count)
                {
                    log.Say($"error: --only-run p{opIndex:000}-r{onlyRun:000}: the scenario has {plan.Points.Count} point(s) (p000 to p{plan.Points.Count - 1:000})");
                    return 1;
                }
                var runOptions = options;
                if (onlySeed is { } wanted)   // the seed form: its run is the seed less the file's first (or --seed's)
                {
                    ulong first = options.Seed ?? plan.Seed;
                    if (wanted < first || wanted - first > int.MaxValue) { log.Say($"error: --only-run p{onlyPoint:000}@{wanted}: run r has seed {first} + r here, so the seed is {first} or more"); return 1; }
                    onlyRun = (int)(wanted - first);
                    runOptions = options with { OnlyRun = onlyRun };
                    if (cli.Arg("--compare") is { } compareSeed)
                    {
                        recorded = ScenarioTraceEvent.Recorded(compareSeed, $"p{onlyPoint:000}-r{onlyRun:000}", out string whySeed);
                        if (recorded is null) { log.Say($"error: --compare: {whySeed}"); return 1; }
                    }
                }
                if (recorded is { ScenarioSha.Length: > 0 } rec && rec.ScenarioSha != load.Scenario.Sha256)
                    log.Say($"scenario: warning: --compare's run was recorded from another text of the scenario (sha256 {rec.ScenarioSha[..12]}…, this file's {load.Scenario.Sha256[..12]}…): the digests may differ for that alone");
                var (results, code) = ScenarioRunner.Execute(plan, baseline with { Level = plan.Level }, Path.GetFullPath(folder), log.Say, runOptions);
                if (onlyRun is not null) code = OnlyRunVerdict(cli, log, Path.GetFullPath(folder), $"p{onlyPoint:000}-r{onlyRun:000}", recorded, code);
                entries.Add(new BatchEntry(name, Rel(outFolder, folder), code, results.Summary.Verdict, results.Summary.Passed, results.Summary.Failed,
                    results.Summary.Inconclusive, results.Summary.Threw));
            }
            int exit = ScenarioRunner.WorstExit(entries.Select(e => e.ExitCode).Append(cancel.IsCancellationRequested ? (int)ScenarioExit.Cancelled : 0));
            if (isFolder)
            {
                Directory.CreateDirectory(outFolder);
                File.WriteAllText(Path.Combine(outFolder, "batch.json"), ScenarioJson.Write(new BatchResults(BatchResults.SchemaName, startedAt,
                    Math.Round(wall.Elapsed.TotalSeconds, 3), entries, exit)));
                log.Say($"scenario: {entries.Count} scenario(s); wrote {Path.Combine(Path.GetFullPath(outFolder), "batch.json")}: exit {exit}");
            }
            return exit;
        }
        catch (SessionException e)
        {
            log.Say($"error: {e.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    static string Rel(string root, string folder) => Path.GetRelativePath(root, folder).Replace('\\', '/');

    /// <summary>
    /// After <c>--only-run</c>, the run's end digest against the recorded one (<c>--compare</c>), said and written
    /// to <c>only-run.json</c> beside its stream, harm trace, tape and tick trace; with <c>--trace-event &lt;id&gt;</c> the event's chain in
    /// the new stream. Exit 7 when the digest differs from the recorded one, else the run's own code (inconclusive, 4, is 0: one run makes no aggregate).
    /// </summary>
    static int OnlyRunVerdict(Cli cli, HostLog log, string folder, string runId, (ulong Seed, string Digest, string ScenarioSha)? recorded, int code)
    {
        string? digest = ScenarioTraceEvent.DigestIn(folder, runId);
        bool? agrees = recorded is { } r && digest is not null ? string.Equals(r.Digest, digest, StringComparison.OrdinalIgnoreCase) : null;
        string Rel1(string sub) => File.Exists(Path.Combine(folder, sub)) ? sub : "";
        var doc = new System.Text.Json.Nodes.JsonObject
        {
            ["runId"] = runId,
            ["digest"] = digest,
            ["recordedDigest"] = recorded?.Digest,
            ["recordedSeed"] = recorded is { } rs ? rs.Seed : null,
            ["agrees"] = agrees,
            ["events"] = Rel1($"events/{runId}.events.jsonl"),
            ["harm"] = Rel1($"events/{runId}.harm.jsonl"),
            ["tape"] = Rel1($"tapes/{runId}.tape"),
            ["ticks"] = Rel1($"ticks/{runId}.ticks.jsonl"),
        };
        File.WriteAllText(Path.Combine(folder, "only-run.json"), doc.ToJsonString(ScenarioSpec.Writing));
        log.Say(agrees switch
        {
            true => $"only-run: {runId} reproduced: its digest {digest} equals the recorded one (seed {recorded!.Value.Seed}); its whole trace is in {folder} (only-run.json)",
            false => $"only-run: {runId} did NOT reproduce: its digest {digest} differs from the recorded {recorded!.Value.Digest} (seed {recorded.Value.Seed}): another build, another switch or another text of the scenario; exit 7",
            null => $"only-run: {runId} ran again: digest {digest ?? "(none)"}{(recorded is null ? " (give --compare <results.json> to check it against the recorded run)" : "")}; its whole trace is in {folder} (only-run.json)",
        });
        if (cli.Arg("--trace-event") is { } ev) ScenarioTraceEvent.Run(ev, folder, Path.Combine(folder, "events", runId + ".events.jsonl"), false, log.Say, Console.Out);
        return agrees == false ? 7 : code == (int)ScenarioExit.Inconclusive ? 0 : code;   // one run: an aggregate or a trend is inconclusive by nature
    }
}
