// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>What the command line changes over the scenario file (named in results.json), and how the runner runs.</summary>
public sealed record ScenarioRunOptions
{
    /// <summary>Workers running runs at once, each on content of its own.</summary>
    public int Jobs { get; init; } = 1;
    public int? Runs { get; init; }
    public ulong? Seed { get; init; }
    public int? TraceRuns { get; init; }
    /// <summary>Run only this point (its index).</summary>
    public int? Only { get; init; }
    public string CommandLine { get; init; } = "";
    public bool Sandbox { get; init; }
    /// <summary>Cancels cleanly: the runs under way finish, no more start, results.json says CANCELLED.</summary>
    public CancellationToken Cancel { get; init; }

    // ---- one run again, by its id (--only-run p003-r017): that run alone (with Only its point), its whole trace
    /// <summary>Run only this run of the point <see cref="Only"/> (its index r: seed + r), with its event stream, harm trace, tape and a per-tick trace.</summary>
    public int? OnlyRun { get; init; }
    /// <summary>The seed the run had when it was recorded (from <c>--compare</c>'s runs.csv), in place of the file's seed + r.</summary>
    public ulong? OnlyRunSeed { get; init; }

    /// <summary>The command line's overrides in words, for results.json.</summary>
    public IReadOnlyList<string> Overrides()
    {
        var o = new List<string>();
        if (Runs is { } r) o.Add($"--runs {r.ToString(CultureInfo.InvariantCulture)}");
        if (Seed is { } s) o.Add($"--seed {s.ToString(CultureInfo.InvariantCulture)}");
        if (TraceRuns is { } t) o.Add($"--trace-runs {t.ToString(CultureInfo.InvariantCulture)}");
        if (Only is { } p) o.Add(OnlyRun is { } rr ? $"--only-run p{p.ToString("000", CultureInfo.InvariantCulture)}-r{rr.ToString("000", CultureInfo.InvariantCulture)}" : $"--only {p.ToString(CultureInfo.InvariantCulture)}");   // --only-run
        if (Jobs > 1) o.Add($"--jobs {Jobs.ToString(CultureInfo.InvariantCulture)}");
        return o;
    }
}

/// <summary>
/// The scenario runner (its output contract): every point's runs on the arena's
/// engine — a fresh world per run from its seed (run r of every point has seed + r), the cast and the director's program in the
/// session's options, stepped with no input until the stop (time, a harness stop, a condition, the program's end) — each run's events
/// collected by name (<see cref="ScenarioEventLog"/>), its measures (<see cref="ScenarioTally"/>) and the arena's counts
/// (<see cref="BoutTally"/>); then each point's aggregates, the assertions' verdicts, the paired comparisons, and the files:
/// <c>results.json</c>, the normalized tables (<c>runs.csv</c>, <c>actors.csv</c>, <c>actions.csv</c>, <c>sweeps.csv</c>), the arena's
/// <c>contacts.csv</c>, <c>powers.csv</c> and (optional) wide <c>bouts.csv</c> and <c>summary.html</c>, the event streams and tapes the
/// trace policy keeps, <c>ticks.jsonl</c> when asked.
/// </summary>
public static class ScenarioRunner
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>The worst of several exit codes, for a folder of scenarios: 2 (modules refused), then 1 (refused), 5 (a run threw), 6 (cancelled), 3 (failed), 4 (inconclusive), 0.</summary>
    public static int WorstExit(IEnumerable<int> codes)
    {
        static int Rank(int c) => c switch { 2 => 8, 1 => 7, 5 => 6, 7 => 5, 6 => 4, 3 => 3, 4 => 2, 0 => 0, _ => 1 };   // 7, a run that did not reproduce
        int worst = 0;
        foreach (var c in codes) if (Rank(c) > Rank(worst)) worst = c;
        return worst;
    }

    /// <summary>The file in the output folder that cancels a run cleanly when it appears.</summary>
    public const string CancelFile = "CANCEL";

    /// <summary>The stages the output contract names that this build does not report yet (declared in results.json).</summary>
    public static readonly IReadOnlyList<string> NotCovered =
    [
        "AnimationWindow receipts per blow (the clips' active windows are in each event stream's head; no per-blow sampled pose)",
        "NPCKnowledge receipts (the duelling brain's decisions are streamed as BrainDecided with the distance, the foe's blow and its phase and its own state; what it perceived by sight and sound is not)",
        "Anatomy beyond severance (HarmResolved.Severed, StructuralLoss) and Damage beyond HarmResolved's staged fields",
        "CelerityRights beyond the flurry (activations.csv and results.json's receipts give each landed swing's extra blows granted, used and unused with why; the tempo's quicker swings are counted as swings while live, not as rights)",
        "a dodge counterfactual (a blow that met nothing is a dodge when its target, within its reach as it began, was dodging through it; the blade's path is not swept against where the body would have been)",
        "censoring-aware statistics beyond a timeOf measure's Kaplan-Meier median and restricted mean (a delay's samples and an aggregate assertion's statistic are of the runs or samples that had one, the rest counted as censored)",
        "paired tests beyond the bootstrap (comparisons give each named measure's per-seed difference against point 0 with a paired bootstrap interval; no permutation test, no correction for many comparisons)",
        "durable resumable jobs (a run set is one process; a CANCEL file or Ctrl-C ends it cleanly with partial results)",
        "pictures and films from a scenario (the arena's --picture-at and --film-at)",
    ];

    sealed class Worker(SessionOptions baseline)
    {
        public SessionContent? Content;
        public string? Key;
        public readonly SessionOptions Baseline = baseline;
    }

    /// <summary>What one run gave.</summary>
    internal sealed class RunOutput
    {
        public required int Point, Run;
        public required ulong Seed;
        public required BoutResult Bout;
        public List<ContactRow> Contacts = [];
        public required RunRecord Record;
        public Dictionary<string, RunValue> Values = new(StringComparer.Ordinal);
        public RunValue?[] AssertionValues = [];
        public List<string[]> ActorRows = [], ActionRows = [];
        public string? StreamPath, TapePath, HarmPath, TicksPath;
        public bool Failure, Threw, Valid = true;
        public string Winner = "";
        public List<string> Evidence = [];
        public string Mode = "NATIVE_AI";
        public bool Reloaded, Sampled;
        public Dictionary<string, string>? ConfigHashes;   // the modules' hashes, when the point overrides their data
        public RunReceipts? Receipts;   // the run's receipts (Health's books, defences, blows, activations, effects, decisions)
    }

    /// <summary>Runs a plan into <paramref name="folder"/>; returns the results and the exit code (section 5.1).</summary>
    public static (ScenarioResults Results, int ExitCode) Execute(ScenarioPlan plan, SessionOptions baseline, string folder, Action<string> say, ScenarioRunOptions? options = null)
    {
        options ??= new ScenarioRunOptions();
        var wall = Stopwatch.StartNew();
        string startedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", Ic);
        int runs = Math.Max(0, options.Runs ?? plan.Runs);
        ulong seed = options.Seed ?? plan.Seed;
        if (options.OnlyRun is { } onlyRun)   // that run alone, at the seed it had
        {
            runs = Math.Max(runs, onlyRun + 1);
            if (options.OnlyRunSeed is { } recorded) seed = recorded - (ulong)onlyRun;
        }
        var model = plan.Model;
        var outputs = model.Outputs;
        int traceRuns = options.TraceRuns ?? outputs.TraceRunCount;
        var policy = plan.TracePolicy ?? outputs.Policy ?? outputs.Events switch { TraceRuns.All => TracePolicy.All, TraceRuns.None => TracePolicy.None, _ => TracePolicy.Sampled };
        var points = plan.Points.Where(p => options.Only is not { } only || p.Index == only).ToList();

        Directory.CreateDirectory(folder);
        string cancelPath = Path.Combine(folder, CancelFile);
        try { if (File.Exists(cancelPath)) File.Delete(cancelPath); } catch (IOException) { }
        File.WriteAllText(Path.Combine(folder, "scenario.json"), plan.Text);
        File.WriteAllText(Path.Combine(folder, "resolved.json"), plan.ResolvedJson);
        foreach (var sub in new[] { "events", "tapes", "ticks" })
        {
            string d = Path.Combine(folder, sub);
            if (Directory.Exists(d)) foreach (var f in Directory.GetFiles(d)) File.Delete(f);   // a folder reused: nothing stale beside the new
        }

        var items = new List<(ScenarioPlanPoint Point, int Run)>();
        foreach (var p in points) for (int r = 0; r < runs; r++) if (options.OnlyRun is not { } only1 || r == only1) items.Add((p, r));   // --only-run
        var slots = new RunOutput?[items.Count];
        int jobs = Math.Clamp(options.Jobs, 1, Math.Max(1, items.Count));
        var workers = Enumerable.Range(0, jobs).Select(_ => new Worker(baseline)).ToArray();
        bool cancelled = false;
        bool Stop()
        {
            if (options.Cancel.IsCancellationRequested || File.Exists(cancelPath)) { cancelled = true; return true; }
            return false;
        }
        void Do(int i, Worker w, Action<string>? speak)
        {
            if (Stop()) return;
            var (point, r) = items[i];
            // SAMPLED (the default): the first traceRuns runs of each point and every run that failed; FAILURES: those that failed
            // only; ALL; NONE. A failure: an eachRun/anyRun assertion failed in it, a built-in check broke, it threw.
            bool sampled = policy == TracePolicy.All || (policy == TracePolicy.Sampled && r < traceRuns);
            bool full = options.OnlyRun is not null;   // --only-run keeps its whole trace, whatever the policy
            slots[i] = RunOne(plan, model, point, r, seed, w, folder, speak, sampled || full, policy is TracePolicy.Sampled or TracePolicy.Failures, policy != TracePolicy.None || full, items.Count, full);
        }
        if (items.Count > 0) Do(0, workers[0], say);
        if (jobs == 1) for (int i = 1; i < items.Count; i++) Do(i, workers[0], null);
        else Parallel.For(0, jobs, new ParallelOptions { MaxDegreeOfParallelism = jobs }, j =>
        {
            for (int i = 1 + j; i < items.Count; i += jobs) Do(i, workers[j], null);
        });
        var done = slots.Where(s => s is not null).Select(s => s!).ToList();

        // The trace budget: the sampled runs first, then the failures in run order; streams beyond it are removed.
        ApplyBudget(done, outputs.TraceBudgetRuns, plan.TraceBudgetMegabytes, folder, out var budgetNote);

        var warnings = new List<string>(plan.Warnings);
        if (budgetNote is not null) warnings.Add(budgetNote);
        if (cancelled) warnings.Add($"cancelled: {done.Count} of {items.Count} runs ran");
        var named = model.Measures.ToDictionary(m => m.Name, m => (ValueForm?)m.Form, StringComparer.Ordinal);
        ValueForm? Named(string n) => named.TryGetValue(n, out var f) ? f : null;

        // ---- each point ----
        var pointResults = new List<PointResult>();
        var metrics = new List<MetricResult>();
        var byAssertion = model.Assertions.Select(_ => new List<(int Point, Verdict V)>()).ToArray();
        var evidence = model.Assertions.Select(_ => new List<string>()).ToArray();
        string[] actorNames = plan.Points.Count == 0 ? [] : [.. plan.Points[0].Cast.Fighters.Select(f => f.Name)];
        foreach (var p in points)
        {
            var mine = done.Where(o => o.Point == p.Index).OrderBy(o => o.Run).ToList();
            var completed = mine.Where(o => !o.Threw).ToList();
            var measures = new SortedDictionary<string, MeasureAggregate>(StringComparer.Ordinal);
            foreach (string key in completed.SelectMany(o => o.Values.Keys).Distinct().Order(StringComparer.Ordinal))
            {
                var values = completed.Select(o => o.Values.TryGetValue(key, out var v) ? v : new RunValue(null)).ToList();
                var form = (ValueForm?)Named(key) ?? new MeasureRefForm(key);
                var kind = ScenarioVerdicts.KindOf(form, Named);
                string? unit = model.Measures.FirstOrDefault(m => m.Name == key)?.Unit ?? BuiltInUnit(key) ?? ObservableUnit(form);
                bool wilson = ScenarioVerdicts.WilsonFits(form, values, Named);
                // The built-in measures compactly (n, mean, min, max: every run's values are in actors.csv); the named ones whole.
                var agg = named.ContainsKey(key) ? ScenarioVerdicts.Aggregate(kind, unit, values, wilson) : ScenarioVerdicts.Compact(kind, unit, values);
                measures[key] = agg;
                if (named.ContainsKey(key))
                {
                    var stat = ScenarioVerdicts.DefaultStatistic(kind);
                    double? estimate = kind == MeasureKind.Rate || kind == MeasureKind.Check ? agg.Value : agg.Mean;
                    metrics.Add(new MetricResult(key, p.Index, agg.Kind, agg.Unit ?? "", estimate, stat.ToString().ToLowerInvariant(),
                        completed.Count(o => o.Values.TryGetValue(key, out var v) && (v.Value is not null || (v.Samples?.Count ?? 0) > 0)),
                        agg.Numerator, kind is MeasureKind.Rate or MeasureKind.RatePerSecond ? agg.Denominator : null, agg.Censored,
                        agg.Interval95 is { } iv ? new Interval95(iv[0], iv[1], agg.IntervalMethod ?? "") : null, completed.Count));
                }
            }
            // assertions here (trends after)
            var assertionResults = new List<AssertionPointResult>();
            for (int a = 0; a < model.Assertions.Count; a++)
            {
                var spec = model.Assertions[a];
                if (spec.Scope == AssertionScope.Trend) continue;
                if (!Applies(spec, p))
                {
                    byAssertion[a].Add((p.Index, Verdict.NotApplicable));
                    assertionResults.Add(new AssertionPointResult(spec.Name, ScenarioVerdicts.ScopeName(spec.Scope), "notApplicable", Note: "its points exclude this one"));
                    continue;
                }
                var values = completed.Select(o => o.AssertionValues[a] ?? new RunValue(null)).ToList();
                var seeds = completed.Select(o => o.Seed).ToList();
                var kind = ScenarioVerdicts.KindOf(spec.Value, Named);
                var (v, res) = ScenarioVerdicts.AtPoint(spec, kind, values, seeds, ScenarioVerdicts.WilsonFits(spec.Value, values, Named));
                if (res.FirstFailingSeed is { } ffs && completed.FirstOrDefault(o => o.Seed == ffs) is { } failing)
                {
                    var ev = new List<string>();
                    if (failing.StreamPath is not null) ev.Add(Rel(folder, failing.StreamPath));
                    ev.AddRange(failing.Evidence.Take(5));
                    res = res with { Evidence = ev.Count > 0 ? ev : null, FirstFailingRun = failing.Record.RunId };
                    evidence[a].AddRange(ev);
                }
                byAssertion[a].Add((p.Index, v));
                assertionResults.Add(res);
            }
            var ends = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in mine) ends[o.Record.Stop] = (ends.TryGetValue(o.Record.Stop, out int n) ? n : 0) + 1;
            var shortfalls = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in completed)
                foreach (var s in o.Record.Shortfalls ?? [])
                {
                    string k = $"{s.Actor} {s.Step}{(s.Label is null ? "" : $" \"{s.Label}\"")} {s.Event}{(s.Reason is null ? "" : $" ({s.Reason})")}";
                    shortfalls[k] = (shortfalls.TryGetValue(k, out int n) ? n : 0) + 1;
                }
            var (control, controlled, mode) = ControlOf(p);
            mode = p.ExecutionMode ?? mode;
            var pointCounts = Counts(runs, mine);
            pointResults.Add(new PointResult(p.Index, p.Name, p.Values.ToDictionary(v => v.Path, v => ScenarioVerdicts.Node(v.Value), StringComparer.Ordinal),
                mine.Count, [seed, seed + (ulong)Math.Max(0, runs - 1)], ends, control, controlled, measures, assertionResults, shortfalls,
                null, mine.Count(o => o.Threw), mine.Any(o => o.Reloaded), mode, pointCounts, p.Setup, p.ActorModes,
                mine.FirstOrDefault(o => o.ConfigHashes is not null)?.ConfigHashes,
                plan.Objectives.Count > 0 ? ObjectiveTracker.Aggregate(plan.Objectives, [.. completed.Select(o => (o.Record.RunId, o.Record.Objectives ?? (IReadOnlyList<ObjectiveOutcome>)[]))]) : null));
        }

        // ---- assertions over the scenario (trends here) ----
        var assertions = new List<AssertionResult>();
        for (int a = 0; a < model.Assertions.Count; a++)
        {
            var spec = model.Assertions[a];
            string kindName = spec.Scope is AssertionScope.Aggregate or AssertionScope.Trend ? "STATISTICAL" : "DETERMINISTIC";
            if (spec.Scope == AssertionScope.Trend)
            {
                var applicable = points.Where(p => Applies(spec, p)).ToList();
                var series = applicable.Select(p =>
                {
                    string value = p.Values.FirstOrDefault(v => string.Equals(v.Path, spec.Over, StringComparison.Ordinal)).Value ?? "";
                    var rs = done.Where(o => o.Point == p.Index && !o.Threw).OrderBy(o => o.Run).Select(o => o.AssertionValues[a] ?? new RunValue(null)).ToList();
                    return (p.Index, value, (IReadOnlyList<RunValue>)rs);
                }).ToList();
                var kind = ScenarioVerdicts.KindOf(spec.Value, Named);
                var (v, trend, note) = ScenarioVerdicts.Trend(spec, kind, series, false);
                if (note is not null) warnings.Add($"assertion \"{spec.Name}\": {note}");
                assertions.Add(new AssertionResult(spec.Name, "trend", ScenarioVerdicts.Name(v),
                    [.. points.Select(p => applicable.Contains(p) ? ScenarioVerdicts.Name(v) : "notApplicable")], trend, spec.Pointer, kindName));
                continue;
            }
            var worst = ScenarioVerdicts.Worst(byAssertion[a].Select(x => x.V));
            // A check with no applicable case at every point it applies to is notApplicable overall (counted
            // neither passed nor failed nor inconclusive: the exit code is not changed by it); one that applies at no point stays inconclusive.
            bool vacuous = worst == Verdict.NotApplicable && pointResults.Any(pr => pr.Assertions.Any(x => x.Name == spec.Name && x.N == 0 && x.Runs > 0 && x.Result == "notApplicable"));
            if (worst == Verdict.NotApplicable && !vacuous) warnings.Add($"assertion \"{spec.Name}\" applies at no point that ran");
            if (vacuous) warnings.Add($"assertion \"{spec.Name}\": its check found no applicable case in any run (notApplicable, not a pass)");
            assertions.Add(new AssertionResult(spec.Name, ScenarioVerdicts.ScopeName(spec.Scope), ScenarioVerdicts.Name(worst == Verdict.NotApplicable && !vacuous ? Verdict.Inconclusive : worst),
                [.. points.Select(p => byAssertion[a].FirstOrDefault(x => x.Point == p.Index) is var hit && hit.Point == p.Index ? ScenarioVerdicts.Name(hit.V) : "notApplicable")],
                null, spec.Pointer, kindName, evidence[a].Count > 0 ? evidence[a].Distinct().Take(10).ToList() : null));
        }

        // ---- paired comparisons: each point against point 0 on the same seeds ----
        var comparisons = new List<PairedComparison>();
        if (points.Count > 1)
        {
            var control = points[0];
            var c0 = done.Where(o => o.Point == control.Index && !o.Threw).ToDictionary(o => o.Seed);
            foreach (var t in points.Skip(1))
            {
                int matched = 0, changed = 0, differ = 0;
                foreach (var o in done.Where(o => o.Point == t.Index && !o.Threw))
                {
                    if (!c0.TryGetValue(o.Seed, out var c)) continue;
                    matched++;
                    if (c.Record.Stop != o.Record.Stop || c.Winner != o.Winner) changed++;
                    if (c.Record.Digest != o.Record.Digest) differ++;
                }
                var differences = new List<PairedDifference>();
                foreach (var m in model.Measures)
                {
                    var diffs = new List<double>();
                    foreach (var o in done.Where(o => o.Point == t.Index && !o.Threw).OrderBy(o => o.Run))
                        if (c0.TryGetValue(o.Seed, out var c) && c.Values.TryGetValue(m.Name, out var cv) && o.Values.TryGetValue(m.Name, out var tv)
                            && cv.Value is { } x && tv.Value is { } y)
                            diffs.Add(y - x);
                    var iv = diffs.Count >= 2 ? ScenarioStats.Bootstrap(diffs.Count, idx => idx.Average(i => diffs[i])) : null;
                    differences.Add(new PairedDifference(m.Name, diffs.Count, diffs.Count > 0 ? diffs.Average() : null,
                        iv is null ? null : new Interval95(iv[0], iv[1], $"paired bootstrap over seeds ({ScenarioStats.BootstrapResamples} resamples, fixed seed)"),
                        diffs.Count(d => d != 0)));
                }
                comparisons.Add(new PairedComparison($"p{t.Index:000}-vs-p{control.Index:000}", control.Index, t.Index, true, matched, changed, differ,
                    control.Name, t.Name, differences.Count > 0 ? differences : null));
            }
        }

        // ---- the tables ----
        var shadow = ShadowSpec(plan, points, runs, seed, out bool representable);
        if (outputs.Csv) WriteTables(plan, model, points, done, folder, shadow, representable, outputs, actorNames, warnings);

        // ---- results.json ----
        int threw = done.Count(o => o.Threw);
        int failed = assertions.Count(x => x.Result == "fail"), inconclusive = assertions.Count(x => x.Result == "inconclusive"), passed = assertions.Count(x => x.Result == "pass");
        var exit = cancelled ? ScenarioExit.Cancelled : threw > 0 ? ScenarioExit.RunThrew : failed > 0 ? ScenarioExit.AssertionFailed
            : inconclusive > 0 ? ScenarioExit.Inconclusive : ScenarioExit.Passed;
        var counts = Counts(runs * points.Count, done) with { Cancelled = runs * points.Count - done.Count };
        var content = workers[0].Content;
        var code = content?.Modules.Code.ToList() ?? [];
        var modules = code.ToDictionary(c => c.Info.Id, c => new ModuleProvenance(c.Stamp.ConfigHash, c.Stamp.ProfileId, c.Stamp.NonshippingResearch), StringComparer.Ordinal);
        string profileHash = ScenarioPlan.Hash(string.Join("\n", modules.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => $"{m.Key}={m.Value.ConfigHash}")));
        string overrides = string.Join("\n", baseline.Switches.Order(StringComparer.Ordinal)) + "\n" + baseline.CombatTempo?.ToString(Ic) + "\n"
            + string.Join("\n", points.Select(p => $"{p.Index}:{string.Join(",", p.Switches.Order(StringComparer.Ordinal))}|{p.CombatTempo?.ToString(Ic)}|{p.Night}|{string.Join(";", p.Values.Select(v => $"{v.Path}={v.Value}"))}"))
            + "\n" + string.Join(" ", options.Overrides());
        // The switches the modules declare (the command line's other flags reach SessionOptions.Switches too, and change nothing).
        var declared = (content?.Modules.Modules ?? []).SelectMany(m => m.Switches).ToHashSet(StringComparer.Ordinal);
        bool KnownSwitch(string sw) => declared.Count == 0 || declared.Contains(sw);
        var git = GitInfo.Read();
        string resultsDigest = ScenarioPlan.Hash(string.Join("\n", done.OrderBy(o => o.Point).ThenBy(o => o.Run).Select(o => $"{o.Point}:{o.Run}:{o.Record.Digest}")));
        var provenance = new Provenance(PlatformVersion.Text, git.Commit, git.Dirty, Environment.MachineName,
            content?.Modules.Game.Id ?? baseline.Game ?? "", modules,
            [.. baseline.Switches.Concat(points.SelectMany(p => p.Switches)).Distinct(StringComparer.Ordinal).Where(KnownSwitch).Order(StringComparer.Ordinal)],
            new LevelProvenance(plan.Ground, plan.Level, FileHash(plan.Level)), "NATIVE: the platform's own simulation of every run; numbers marked SANDBOX or ENGINE_SANDBOX are not rules",
            options.CommandLine, startedAt, Math.Round(wall.Elapsed.TotalSeconds, 3), resultsDigest,
            ScenarioPlan.Hash(plan.ResolvedJson + "\n" + profileHash), seed, runs, options.Overrides(), jobs, options.Sandbox,
            git.Branch, git.DiffSha256, plan.Sha256, ScenarioPlan.Hash(plan.ResolvedJson), ScenarioPlan.Hash(overrides), profileHash,
            SourceFrom: git.From, CloneCommit: git.CloneCommit);
        var units = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pr in pointResults) foreach (var (k, m) in pr.Measures) units.TryAdd(k, m.Unit ?? "");
        var files = new ResultsFiles(outputs.Csv ? "runs.csv" : null, outputs.Csv ? "contacts.csv" : null, outputs.Csv ? "powers.csv" : null,
            done.Any(o => o.StreamPath is not null) ? "events/" : null, done.Any(o => o.TicksPath is not null) ? "ticks/" : null,
            done.Any(o => o.TapePath is not null) ? "tapes/" : null, outputs.SummaryPage && representable && outputs.Csv ? "summary.html" : null,
            "scenario.json", "resolved.json", null);
        var results = new ScenarioResults(ScenarioResults.SchemaName,
            new ScenarioIdentity(Path.GetFileName(plan.File), plan.Sha256, plan.Format, plan.Title, plan.Notes),
            provenance, pointResults, assertions,
            new ResultsSummary(points.Count, done.Count, passed, failed, inconclusive, threw, (int)exit, VerdictOf(exit)),
            files, warnings, outputs.Csv ? TableNames(outputs) : [], units,
            cancelled ? "CANCELLED" : "COMPLETED", counts, comparisons, metrics, NotCovered,
            // The receipts of the runs whose streams are kept (every run's are in actors.csv and activations.csv)
            done.Where(o => o.StreamPath is not null && o.Receipts is not null).OrderBy(o => o.Point).ThenBy(o => o.Run).Select(o => o.Receipts!).ToList() is { Count: > 0 } kept ? kept : null,
            points.Any(p => p.Values.Any(v => v.Path == ScenarioSwap.Path)) ? ScenarioSwap.Compare(points, done, model, plan.Objectives) : null);   // the swap's pairs
        File.WriteAllText(Path.Combine(folder, "results.json"), ScenarioJson.Write(results));

        say(string.Create(Ic, $"scenario: {Path.GetFileName(plan.File)}: {done.Count} runs at {points.Count} point(s) in {wall.Elapsed.TotalSeconds:0.0} s; assertions {passed} passed, {failed} failed, {inconclusive} inconclusive{(threw > 0 ? $"; {threw} runs threw" : "")}{(cancelled ? "; CANCELLED" : "")}: exit {(int)exit}"));
        foreach (var a in assertions) say($"scenario: {a.Result.ToUpperInvariant()}: {a.Name}{(a.Trend is { } tr && tr.Correlation is { } rho ? string.Create(Ic, $" (rank correlation {rho:0.###}, 95 % [{tr.Interval95?[0]:0.###}, {tr.Interval95?[1]:0.###}])") : "")}");
        foreach (var w in workers) w.Content?.Dispose();
        return (results, (int)exit);
    }

    static string VerdictOf(ScenarioExit e) => e switch
    {
        ScenarioExit.Passed => "PASSED", ScenarioExit.AssertionFailed => "FAILED", ScenarioExit.Inconclusive => "INCONCLUSIVE",
        ScenarioExit.RunThrew => "RUN_THREW", ScenarioExit.Cancelled => "CANCELLED", ScenarioExit.Refused => "REFUSED", _ => "MODULES_REFUSED",
    };

    static string Rel(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

    static string? BuiltInUnit(string key)
    {
        int dot = key.IndexOf('.');
        if (dot < 0) return null;
        string what = key[(dot + 1)..];
        return BuiltInMeasures.PerActor.Concat(BuiltInMeasures.PerRun).FirstOrDefault(b => b.Name == what)?.Unit;
    }

    static string? FileHash(string path)
    {
        try { return File.Exists(path) ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : null; }
        catch (IOException) { return null; }
    }

    static RunCounts Counts(int requested, List<RunOutput> mine)
    {
        int completed = mine.Count(o => !o.Threw);
        int invalid = mine.Count(o => !o.Threw && !o.Valid);
        int censored = mine.Count(o => !o.Threw && o.Record.Stop == StopNames.Time);
        return new RunCounts(requested, completed, completed - invalid, censored, invalid, mine.Count(o => o.Threw), Math.Max(0, requested - mine.Count));
    }

    /// <summary>Whether an assertion applies at a point (its <c>points</c> filters).</summary>
    static bool Applies(AssertionSpec a, ScenarioPlanPoint p)
    {
        if (a.Points is not { Count: > 0 } filters) return true;
        foreach (var f in filters)
        {
            string? value = p.Values.FirstOrDefault(v => string.Equals(v.Path, f.Path, StringComparison.Ordinal)).Value;
            if (!ScenarioVerdicts.InFilter(value, f)) return false;
        }
        return true;
    }

    /// <summary>
    /// A value measure of an observable takes the
    /// observable's own unit (<see cref="ScenarioWords.Observables"/>: <c>&lt;actor&gt;.distanceTo.&lt;actor&gt;</c> is metres), unless the
    /// scenario gives one; null when the form reads no observable.
    /// </summary>
    public static string? ObservableUnit(ValueForm? form)
    {
        string? path = form switch { ObservableForm o => o.Observable, ObservableAtEventForm a => a.Observable, _ => null };
        if (path is null) return null;
        var parts = path.Split('.');
        foreach (var o in ScenarioWords.Observables)
        {
            var pattern = o.Path.Split('.');
            if (pattern.Length != parts.Length) continue;
            bool same = true;
            for (int i = 0; i < pattern.Length && same; i++) same = pattern[i].StartsWith('<') || string.Equals(pattern[i], parts[i], StringComparison.Ordinal);
            if (same) return o.Unit;
        }
        return null;
    }

    /// <summary>Each actor's control in words, whether any AI is muted or scripted over, and the run's authority (the output contract's labels).</summary>
    static (Dictionary<string, string> Control, bool Controlled, string Mode) ControlOf(ScenarioPlanPoint p)
    {
        var control = new Dictionary<string, string>(StringComparer.Ordinal);
        bool anyAi = false, anyScript = false, controlled = false;
        foreach (var f in p.Cast.Fighters)
        {
            var c = p.Program?.Actors.FirstOrDefault(a => a.Actor == f.Name);
            // The actor the player's controls drive.
            if (p.Program?.Input?.Actor == f.Name)
            {
                control[f.Name] = "the player's input (replayed)";
                anyScript = true;
                controlled = true;
                continue;
            }
            if (c is null)
            {
                string brain = f.Brain switch { CastBrain.Duel => "AI (duel)", CastBrain.Stand => "stands", CastBrain.Block => "AI (block)", CastBrain.Archer => "AI (archer)", _ => "script (arena)" };   // archer
                control[f.Name] = brain;
                if (f.Brain == CastBrain.Script) anyScript = true; else anyAi = true;
                continue;
            }
            bool script = c.Tracks.Any(t => t.Count > 0);
            bool ai = c.Ai != ScenarioAi.None;
            var parts = new List<string>();
            if (script) parts.Add("script");
            if (ai) parts.Add((script ? "then " : "") + $"AI ({c.Ai.ToString().ToLowerInvariant()})");
            else if (!script) parts.Add("stands");
            if (c.Mute != ControlChannels.None) parts.Add($"muted: {c.Mute.ToString().ToLowerInvariant()}");
            if (script && !ai) parts.Add($"then {c.Then.ToString().ToLowerInvariant()}");
            control[f.Name] = string.Join(", ", parts);
            if (script) anyScript = true;
            if (ai) anyAi = true;
            if (ai && (script || c.Mute != ControlChannels.None)) controlled = true;
        }
        string mode = anyScript && anyAi ? "HYBRID" : anyScript ? "CONTROLLED_SCRIPT" : controlled ? "HYBRID" : "NATIVE_AI";
        return (control, controlled || (anyScript && anyAi), mode);
    }

    // ---- one run ----

    static RunOutput RunOne(ScenarioPlan plan, ScenarioMeasuresModel model, ScenarioPlanPoint point, int run, ulong seedBase, Worker worker,
        string folder, Action<string>? say, bool sampled, bool failuresKept, bool mayTrace, int total, bool fullTrace = false)
    {
        ulong seed = seedBase + (ulong)run;
        string runId = $"p{point.Index:000}-r{run:000}";
        var opts = OptionsFor(plan, point, worker, seed, say);
        string key = SessionContent.KeyOf(opts with { Content = null });
        bool reloaded = false;
        if (worker.Content is null || worker.Key != key)
        {
            reloaded = worker.Content is not null;
            worker.Content?.Dispose();
            worker.Content = null;
            worker.Content = Session.LoadContent(opts with { Content = null, Say = say ?? (_ => { }) });
            worker.Key = key;
        }
        opts = opts with { Content = worker.Content };
        string? harm = sampled ? Path.Combine(folder, "events", runId + ".harm.jsonl") : null;
        if (harm is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(harm)!);
            opts = opts with { CombatTrace = harm };
        }
        var record0 = new RunRecord(run, seed, StopNames.Threw, 0, 0, "", new Dictionary<string, double?>(), RunId: runId);
        var output = new RunOutput { Point = point.Index, Run = run, Seed = seed, Record = record0,
            Bout = new BoutResult { Point = point.Index, Values = [], Bout = run, Seed = seed, Fighters = [] }, Reloaded = reloaded,
            Mode = point.ExecutionMode ?? ControlOf(point).Mode,
            ConfigHashes = point.DataOverrides is null ? null : worker.Content!.Modules.Code.ToDictionary(c => c.Info.Id, c => c.Stamp.ConfigHash, StringComparer.Ordinal) };
        try
        {
            using var session = Session.Create(opts);
            var w = session.World;
            var fighters = session.Fighters;
            // The brains' decisions, in the runs whose streams are kept from the start (outputs.decisions "traced", the
            // default), in every run that may keep one ("all"), or none ("none"); never in an untraced run's outputs, never in a digest.
            if (model.Outputs.Decisions switch { DecisionsOutput.None => false, DecisionsOutput.All => mayTrace, _ => sampled || fullTrace })
                BrainDecisions.Enable(w);
            var names = new Dictionary<EntityId, string>();
            var nameList = new List<string>();
            for (int i = 0; i < fighters.Count; i++)
            {
                string n = i < point.Cast.Fighters.Count ? point.Cast.Fighters[i].Name : $"e{fighters[i].Index}";
                names[fighters[i]] = n;
                nameList.Add(n);
            }
            var result = new BoutResult
            {
                Point = point.Index, Values = IntValues(point), Bout = run, Seed = seed,
                Fighters = [.. Enumerable.Range(0, fighters.Count).Select(_ => new FighterTally())],
            };
            var contacts = new List<ContactRow>();
            var tally = new BoutTally(w, fighters, result, contacts);
            var log = new ScenarioEventLog(w, names);
            var st = new ScenarioTally(w, fighters, nameList, result, log, model);
            var receipts = new ScenarioReceipts(w, fighters, nameList, log);
            var objectives = plan.Objectives.Count > 0 ? new ObjectiveTracker(w, fighters, nameList, point.Cast, plan.Objectives, log, result) : null;
            string? head = mayTrace ? EventTrace.HeadLine(w, session.TapeHeader(PlatformVersion.Text), [.. fighters.Select(f => (f, names[f]))],
                "NATIVE: the platform's own simulation of this run; numbers marked SANDBOX or ENGINE_SANDBOX are not rules", EventTrace.Schema2,
                jw => ScenarioStream.HeadExtra(jw, plan, point, runId, seed, fighters, names)) : null;
            var tape = mayTrace ? new Tape { Header = session.TapeHeader(PlatformVersion.Text) } : null;
            var ticks = fullTrace ? new ScenarioTicks(w, fighters, nameList, result, log, point.Cast, 1)   // --only-run, every tick
                : model.Outputs.Ticks is { } to && to.Runs != TraceRuns.None && (to.Runs == TraceRuns.All || run < model.Outputs.TraceRunCount)
                ? new ScenarioTicks(w, fighters, nameList, result, log, point.Cast, Math.Max(1, to.EveryTicks)) : null;
            ticks?.Sample();
            long most = (long)Math.Round(model.Stop.AfterSeconds * World.TicksPerSecond);
            string? stop = null;
            while (session.Tick < most)
            {
                // The player's input when the program drives an actor through it (the director work's PLAYER_INPUT_REPLAY), else idle (the arena's).
                var frame = session.ScenarioFrame();
                ticks?.Frame(frame);   // the player-input actor's receipts read the frame fed in
                session.Step(frame);
                if (tape is not null)
                {
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
                tally.Collect();
                log.Collect();
                receipts.Collect();
                st.Collect();
                ticks?.Collect();
                objectives?.Update();   // the objectives read the tick just stepped (they change nothing)
                if (log.Ended is { } reason) { stop = StopNames.ScenarioEndPrefix + reason; break; }
                if (model.Stop.OnHarnessStops == HarnessStops.First && tally.Over) { stop = HarnessStop(result); break; }
                if (st.StopNow() is { } cond) { stop = cond; break; }
                if (objectives?.StopNow(model.Stop.OnObjectives) is { } byObjectives) { stop = byObjectives; break; }
            }
            tally.Finish(session.Tick);
            result.Ticks = session.Tick;
            result.Digest = session.Digest();
            stop ??= StopNames.Time;
            var (outcomes, ending) = objectives?.Finish(stop, session.Tick) ?? (null!, null!);   // MET, FAILED or UNRESOLVED, and the ending in words
            var values = st.Finish();
            output.Values = values;
            output.AssertionValues = [.. model.Assertions.Select(a => (RunValue?)st.Evaluate(a.Value))];
            output.Bout = result;
            output.Contacts = contacts;
            output.Winner = result.Winner >= 0 && result.Winner < nameList.Count ? nameList[result.Winner] : "";
            // With sides the result is the side's — "side <name>", the one side left standing — never the
            // actor who dealt the last harm (the arena's own tables keep theirs).
            if (point.Cast.HasSides)
            {
                var standing = point.Cast.Fighters.Where((f, i) => i < result.Fighters.Length && !result.Fighters[i].Stopped).Select(f => f.Side).Distinct().ToList();
                output.Winner = result.SideWon >= 0 && standing.Count == 1 && standing[0] is { } sideLeft ? $"side {sideLeft}" : "";
            }
            var shortfalls = st.Shortfalls();
            output.Valid = !log.Events.Any(e => e.Raw is StepRefused sr && sr.Why is StepRefusal.NotBuilt or StepRefusal.NoTarget);

            // Anomalies: an assertion that fails in this run (each run's and any run's), a built-in check broken.
            var anomalies = new List<string>();
            for (int a = 0; a < model.Assertions.Count; a++)
            {
                var spec = model.Assertions[a];
                if (spec.Scope is not (AssertionScope.EachRun or AssertionScope.AnyRun) || spec.Compare is not { } cmp || !Applies(spec, point)) continue;
                if (ScenarioTally.Holds(cmp, output.AssertionValues[a]?.Value) != true) anomalies.Add($"assertion \"{spec.Name}\"");
            }
            foreach (var check in BuiltInChecks.All)
                if (!check.NeedsBow || log.Events.Any(e => e.Kind == "ArrowLoosed"))
                {
                    var c = st.Check(check.Name);
                    if (c.Value == 0) anomalies.Add($"check {check.Name} ({c.Numerator} of {c.Denominator})");
                }
            output.Failure = anomalies.Count > 0;
            output.Evidence = Violations(log, runId);

            output.Sampled = sampled;
            bool keep = sampled || (output.Failure && failuresKept);
            if (keep && head is not null)
            {
                output.StreamPath = Path.Combine(folder, "events", runId + ".events.jsonl");
                ScenarioStream.Write(output.StreamPath, head, log, runId, seed);
                output.TapePath = Path.Combine(folder, "tapes", runId + ".tape");
                tape!.Digests[session.Tick] = result.Digest;
                Directory.CreateDirectory(Path.GetDirectoryName(output.TapePath)!);
                tape.Write(output.TapePath);
            }
            output.HarmPath = harm;
            if (ticks is not null)
            {
                output.TicksPath = Path.Combine(folder, "ticks", runId + ".ticks.jsonl");
                ticks.Write(output.TicksPath);
            }
            output.Receipts = receipts.Finish(runId, seed);
            output.ActorRows = ScenarioTables.ActorRows(runId, point, run, seed, nameList, point.Cast, result, output.Receipts);
            output.ActionRows = ScenarioTables.ActionRows(runId, point, run, seed, log, nameList);
            output.Record = new RunRecord(run, seed, stop, Math.Round(session.Tick / (double)World.TicksPerSecond, 4), session.Tick, result.Digest.ToString("x16", Ic),
                model.Measures.ToDictionary(m => m.Name, m => values.TryGetValue(m.Name, out var v) ? Round(v.Value) : null, StringComparer.Ordinal),
                shortfalls.Count > 0 ? shortfalls : null, null,
                output.StreamPath is null ? null : Rel(folder, output.StreamPath), output.TapePath is null ? null : Rel(folder, output.TapePath),
                output.Winner.Length > 0 ? output.Winner : null, runId, output.Valid, anomalies.Count > 0 ? anomalies : null,
                outcomes, ending);
            if (say is not null)
            {
                say(string.Create(Ic, $"scenario: run {runId} (seed {seed}) of {total}: {stop} at {session.Tick / (double)World.TicksPerSecond:0.##} s, {log.Events.Count} events"));
                if (outcomes is not null) say($"scenario: run {runId}: {string.Join("; ", outcomes.Select(o => $"objective \"{o.Name}\" {o.Result}{(o.Seconds is { } s ? string.Create(Ic, $" at {s:0.##} s") : "")}"))}");
            }
        }
        catch (Exception e) when (e is not (ModuleRefusedException or ModuleCheckException))
        {
            output.Threw = true;
            output.Failure = true;
            output.Record = record0 with { Error = $"{e.GetType().Name}: {e.Message}", Anomalies = ["threw"] };
            say?.Invoke($"scenario: run {runId} (seed {seed}) threw: {e.GetType().Name}: {e.Message}");
        }
        return output;
    }

    static double? Round(double? v) => v is { } x ? Math.Round(x, 6) : null;

    /// <summary>The event ids where a built-in check broke, as evidence (<c>p000-r003#41</c>), the first few.</summary>
    static List<string> Violations(ScenarioEventLog log, string runId)
    {
        var ids = new List<string>();
        var blocked = new HashSet<long>();
        foreach (var e in log.Events)
        {
            if (e.Raw is GuardResolved g && g.Verdict != GuardVerdict.Overpowered) blocked.Add(g.ContactId);
            if (e.Raw is HarmResolved h && h.Flurry == 0 && h.Wounds > 0 && blocked.Contains(h.ContactId)) ids.Add($"{runId}#{e.Id}");
            if (ids.Count >= 5) break;
        }
        return ids;
    }

    static string HarnessStop(BoutResult r) =>
        r.Stop is { } s ? ArenaTables.StopName(s) : r.Disarmed ? StopNames.Disarmed : StopNames.Time;

    /// <summary>A point's values as integers for the arena's tables, when every one is an integer (else none: the tables carry the text).</summary>
    static int[] IntValues(ScenarioPlanPoint p)
    {
        var vs = new int[p.Values.Count];
        for (int i = 0; i < vs.Length; i++)
            if (!int.TryParse(p.Values[i].Value, NumberStyles.Integer, Ic, out vs[i])) return [];
        return vs;
    }

    /// <summary>A run's session options: the baseline's game and switches with the point's, the level, no townsfolk, the cast, the program and the seed (the arena's own, ArenaRun.OptionsFor).</summary>
    static SessionOptions OptionsFor(ScenarioPlan plan, ScenarioPlanPoint point, Worker worker, ulong seed, Action<string>? say)
    {
        var b = worker.Baseline;
        var o = b with
        {
            Switches = point.Switches.Count == 0 ? b.Switches : new HashSet<string>(b.Switches.Concat(point.Switches), StringComparer.Ordinal),
            CombatTempo = b.CombatTempo ?? point.CombatTempo,
            Level = plan.Level,
            Start = "0",
            People = 0,
            Witnesses = [],
            Night = point.Night,
            Place = null,
            TargetTest = null,
            NpcActions = null,
            Seed = seed,
            Arena = point.Cast.Write(),
            Content = worker.Content,
            Say = say ?? (_ => { }),
            DataOverrides = point.DataOverrides,
            Light = point.Light,
        };
        return WithProgram(o, point.Program);
    }

    /// <summary>The director's program in the session's options (the director work's field; null: no director, an arena world).</summary>
    static SessionOptions WithProgram(SessionOptions o, ScenarioProgram? program) => program is null ? o : o with { Scenario = program.Write() };

    // ---- the trace budget ----

    /// <summary>
    /// The trace budget (runs, megabytes): the streams kept in order — the sampled runs, then the failures, each in run order — until
    /// either budget is spent; the rest are removed (their seeds, outcomes and measures stay). Deterministic whatever the jobs.
    /// </summary>
    static void ApplyBudget(List<RunOutput> done, int budget, double megabytes, string folder, out string? note)
    {
        note = null;
        if (budget <= 0 && megabytes <= 0) return;
        var kept = done.Where(o => o.StreamPath is not null).OrderBy(o => o.Failure && !o.Sampled ? 1 : 0).ThenBy(o => o.Point).ThenBy(o => o.Run).ToList();
        long bytesLeft = megabytes > 0 ? (long)(megabytes * 1024 * 1024) : long.MaxValue;
        int keep = 0;
        foreach (var o in kept)
        {
            long size = new[] { o.StreamPath, o.TapePath, o.HarmPath }.Where(p => p is not null && File.Exists(p)).Sum(p => new FileInfo(p!).Length);
            if ((budget > 0 && keep >= budget) || size > bytesLeft) break;
            bytesLeft -= size;
            keep++;
        }
        if (keep >= kept.Count) return;
        int dropped = 0;
        foreach (var o in kept.Skip(keep))
        {
            foreach (var p in new[] { o.StreamPath, o.TapePath })
                if (p is not null && File.Exists(p)) File.Delete(p);
            o.Record = o.Record with { Events = null, Tape = null };
            o.StreamPath = o.TapePath = null;
            dropped++;
        }
        note = string.Create(Ic, $"trace budget ({(budget > 0 ? $"{budget} runs" : "")}{(budget > 0 && megabytes > 0 ? ", " : "")}{(megabytes > 0 ? $"{megabytes:0.#} MB" : "")}): {dropped} more runs' streams and tapes were not kept (their seeds, outcomes and measures are)");
    }

    // ---- the tables ----

    /// <summary>
    /// The arena spec the arena's tables are written with: point 0's fighters, the runs as bouts, and the sweep as the arena's sweeps
    /// when it is one the arena could make (one or two sheet values or power ranks, integers, a grid in the arena's order) — then the
    /// tables equal <c>--arena</c>'s byte for byte; otherwise no sweeps, and the scenario's own columns are put in after "point".
    /// </summary>
    static ArenaSpec ShadowSpec(ScenarioPlan plan, IReadOnlyList<ScenarioPlanPoint> points, int runs, ulong seed, out bool representable)
    {
        var p0 = plan.Points.Count > 0 ? plan.Points[0] : null;
        var fighters = (p0?.Cast.Fighters ?? []).Select(f => new SpecFighter(f.Name, f.Template, f.Weapon, f.X, f.Y, f.Z, f.Facing, f.Brain, f.Numbers,
            f.Script, f.Foe, f.Sheet, f.Blood, f.Slots, f.Shield, f.Armor, f.Side)).ToList();
        var spec = new ArenaSpec
        {
            Level = plan.Level, Title = plan.Title ?? "", Night = p0?.Night ?? true, Fighters = fighters, Bouts = runs, Seed = seed,
            Seconds = (float)plan.Model.Stop.AfterSeconds, Tapes = plan.Model.Outputs.TraceRunCount, Switches = p0?.Switches ?? [],
            CombatTempo = p0?.CombatTempo, Text = plan.Text,
        };
        representable = true;
        if (p0 is null || p0.Values.Count == 0) return spec;
        var sweeps = new List<ArenaSweep>();
        if (p0.Values.Count > 2) { representable = false; return spec; }
        for (int k = 0; k < p0.Values.Count; k++)
        {
            string path = p0.Values[k].Path;
            var parts = path.Split('.');
            int f = fighters.FindIndex(x => x.Name == parts[0]);
            var ints = new List<int>();
            foreach (var p in plan.Points)
                if (p.Values.Count == p0.Values.Count && int.TryParse(p.Values[k].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) { if (!ints.Contains(v)) ints.Add(v); }
                else { representable = false; return spec; }
            if (f >= 0 && parts.Length == 4 && parts[1] == "sheet" && parts[2] == "values") sweeps.Add(new ArenaSweep(f, parts[3], false, ints));
            else if (f >= 0 && parts.Length == 5 && parts[1] == "sheet" && parts[2] == "powers" && parts[4] == "rank") sweeps.Add(new ArenaSweep(f, parts[3], true, ints));
            else { representable = false; return spec; }
        }
        var withSweeps = spec with { Sweeps = sweeps };
        var arenaPoints = withSweeps.Points;
        if (arenaPoints.Count != plan.Points.Count) { representable = false; return spec; }
        for (int i = 0; i < arenaPoints.Count; i++)
            for (int k = 0; k < arenaPoints[i].Length; k++)
                if (arenaPoints[i][k].ToString(CultureInfo.InvariantCulture) != plan.Points[i].Values[k].Value) { representable = false; return spec; }
        return withSweeps;
    }

    static IReadOnlyList<string> TableNames(ScenarioOutputs o) =>
        o.WideBouts ? ["runs.csv", "actors.csv", "actions.csv", "sweeps.csv", "contacts.csv", "powers.csv", "bouts.csv", "activations.csv", "strikes.csv"]
                    : ["runs.csv", "actors.csv", "actions.csv", "sweeps.csv", "contacts.csv", "powers.csv", "activations.csv", "strikes.csv"];   // activations.csv, strikes.csv

    static void WriteTables(ScenarioPlan plan, ScenarioMeasuresModel model, IReadOnlyList<ScenarioPlanPoint> points, List<RunOutput> done,
        string folder, ArenaSpec shadow, bool representable, ScenarioOutputs outputs, string[] actorNames, List<string> warnings)
    {
        var ordered = done.Where(o => !o.Threw).OrderBy(o => o.Point).ThenBy(o => o.Run).ToList();
        var bouts = ordered.Select(o => o.Bout).ToList();
        var contacts = ordered.SelectMany(o => o.Contacts).ToList();
        var paths = plan.Points.Count > 0 ? plan.Points[0].Values.Select(v => v.Path).ToList() : [];
        var byPoint = plan.Points.ToDictionary(p => p.Index);
        // The arena's tables: as --arena writes them; a sweep the arena could not make has its columns put in after "point".
        string boutsPath = Path.Combine(folder, "bouts.csv"), contactsPath = Path.Combine(folder, "contacts.csv"), powersPath = Path.Combine(folder, "powers.csv");
        if (representable)
        {
            if (outputs.WideBouts) ArenaTables.WriteBouts(boutsPath, shadow, bouts);
            ArenaTables.WriteContacts(contactsPath, shadow, contacts);
            ArenaPowers.Write(powersPath, shadow, bouts);
            if (outputs.SummaryPage && bouts.Count > 0 && points.Count == plan.Points.Count)
            {
                var summary = ArenaSummary.Compute(shadow, bouts);
                var head = new ArenaSummary.Head(PlatformVersion.Text, "", "", shadow.Seed, $"{bouts.Count} runs", "", null);
                File.WriteAllText(Path.Combine(folder, "summary.html"), ArenaSummary.Page(shadow, summary, head));
            }
        }
        else
        {
            var bare = bouts.Select(b => Bare(b)).ToList();
            var bareContacts = contacts.Select(c => c with { Values = [] }).ToList();
            if (outputs.WideBouts) { ArenaTables.WriteBouts(boutsPath, shadow, bare); Splice(boutsPath, paths, ordered.Select(o => byPoint[o.Point]).ToList()); }
            ArenaTables.WriteContacts(contactsPath, shadow, bareContacts);
            Splice(contactsPath, paths, contacts.Select(c => byPoint[c.Point]).ToList());
            ArenaPowers.Write(powersPath, shadow, bare);
            if (outputs.SummaryPage) warnings.Add("summary.html is the arena's page and charts sheet sweeps only; this scenario's sweep is not one, so it was not written");
        }
        ScenarioTables.WriteRuns(Path.Combine(folder, "runs.csv"), plan, model, paths, byPoint, done);
        ScenarioTables.WriteActors(Path.Combine(folder, "actors.csv"), done);
        ScenarioTables.WriteActions(Path.Combine(folder, "actions.csv"), done);
        ScenarioTables.WriteActivations(Path.Combine(folder, "activations.csv"), done);
        ScenarioTables.WriteStrikes(Path.Combine(folder, "strikes.csv"), done);           // every extra blow, a row each
        ScenarioTables.WriteSweeps(Path.Combine(folder, "sweeps.csv"), plan with { Seed = shadow.Seed }, paths, points, done, model.Stop, shadow.Bouts);
    }

    static BoutResult Bare(BoutResult b) => new()
    {
        Point = b.Point, Values = [], Bout = b.Bout, Seed = b.Seed, Fighters = b.Fighters, Winner = b.Winner, Ticks = b.Ticks,
        KillSeconds = b.KillSeconds, Stop = b.Stop, Disarmed = b.Disarmed, Digest = b.Digest, Tape = b.Tape, BuildSeconds = b.BuildSeconds,
        RunSeconds = b.RunSeconds, SideWon = b.SideWon, DownOrder = b.DownOrder,
    };

    /// <summary>Puts the scenario's sweep columns in after the first ("point") of a table the arena wrote with none.</summary>
    static void Splice(string path, IReadOnlyList<string> columns, IReadOnlyList<ScenarioPlanPoint> rowPoints)
    {
        if (columns.Count == 0) return;
        var lines = File.ReadAllLines(path);
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int comma = line.IndexOf(',');
            string first = comma < 0 ? line : line[..comma], rest = comma < 0 ? "" : line[comma..];
            string inserted = i == 0 ? string.Join(",", columns.Select(ScenarioTables.Csv))
                : i - 1 < rowPoints.Count ? string.Join(",", rowPoints[i - 1].Values.Select(v => ScenarioTables.Csv(v.Value))) : "";
            sb.Append(first).Append(',').Append(inserted).Append(rest).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }
}

/// <summary>The program's git state, when it runs from a clone (provenance): the commit, the branch, whether the tree is dirty and the hash of its diff.</summary>
public sealed record GitInfo(string? Commit, string? Branch, bool? Dirty, string? DiffSha256, string? From = null, string? CloneCommit = null)
{
    static GitInfo? cached;

    /// <summary>The file the packaging script writes beside a packaged program (<c>tools/package-platform.ps1</c>): its clone's state when packaged.</summary>
    public const string BuildInfoFile = "build-info.json";

    /// <summary>
    /// Reads it once, from the program's folder (its clone), with a short timeout. An earlier build (a first run of the package: a packaged program sits
    /// outside its clone, so its provenance was null): else from <see cref="BuildInfoFile"/> beside the program — the commit, branch and
    /// dirty diff's hash of the clone it was packaged from, at packaging. Else from the program itself
    /// (<see cref="Built"/>); and when the clone's commit is no longer the one the program was built from (another commit checked out
    /// after building), the build's, with the clone's beside it (<see cref="CloneCommit"/>): the code that ran is the build's.
    /// <see cref="From"/> says which: <c>clone</c>, <c>package</c> or <c>build</c>. Nulls when none is there.
    /// </summary>
    public static GitInfo Read()
    {
        if (cached is not null) return cached;
        string dir = AppContext.BaseDirectory;
        var built = Built();
        string? commit = Git(dir, "rev-parse HEAD")?.Trim();
        if (string.IsNullOrEmpty(commit))
            return cached = (Packaged(Path.Combine(dir, BuildInfoFile)) is { } packaged ? packaged with { From = "package" } : null)
                ?? (built is null ? null : built with { From = "build" }) ?? new GitInfo(null, null, null, null);
        if (built is not null && !string.Equals(built.Commit, commit, StringComparison.Ordinal))
            return cached = built with { From = "build", CloneCommit = commit };
        string? branch = Git(dir, "rev-parse --abbrev-ref HEAD")?.Trim();
        string? diff = Git(dir, "diff HEAD");
        bool? dirty = diff is null ? null : diff.Length > 0;
        return cached = new GitInfo(commit, branch, dirty, dirty == true ? ScenarioPlan.Hash(diff!) : null, "clone");
    }

    /// <summary>
    /// The
    /// clone's state the program was built from, written into it at build time (the host project's <c>VitomirSourceIdentity</c>:
    /// <c>VitomirSourceCommit</c>, <c>VitomirSourceBranch</c>, <c>VitomirSourceDirty</c>), or null when the build had no git. A dirty
    /// build's diff is not kept in the program (no hash): the packaging script's <see cref="BuildInfoFile"/> has it.
    /// </summary>
    public static GitInfo? Built()
    {
        var meta = typeof(GitInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        if (!meta.TryGetValue("VitomirSourceCommit", out var commit) || string.IsNullOrEmpty(commit)) return null;
        bool? dirty = meta.TryGetValue("VitomirSourceDirty", out var d) && bool.TryParse(d, out bool b) ? b : null;
        return new GitInfo(commit, meta.GetValueOrDefault("VitomirSourceBranch"), dirty, null);
    }

    /// <summary>The packaged clone's state (<see cref="BuildInfoFile"/>: <c>commit</c>, <c>branch</c>, <c>dirty</c>, <c>diffSha256</c>), or null.</summary>
    public static GitInfo? Packaged(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            bool? dirty = r.TryGetProperty("dirty", out var d) && d.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? d.GetBoolean() : null;
            string? commit = Str("commit");
            return string.IsNullOrEmpty(commit) ? null : new GitInfo(commit, Str("branch"), dirty, dirty == true ? Str("diffSha256") : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    static string? Git(string dir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{dir}\" {args}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var text = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch (InvalidOperationException) { } return null; }
            return p.ExitCode == 0 ? text.Result : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }
}
