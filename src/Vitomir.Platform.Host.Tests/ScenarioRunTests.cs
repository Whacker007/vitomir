// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json.Nodes;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Host.Scenario;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The scenario runner end to end on the proving ground's free yard, with the format work's reader and the director work's
/// director: an arena-equivalent scenario writes the arena's bouts.csv, contacts.csv and powers.csv byte for byte; the design's
/// examples 2 and 3 (small) run, their results.json passes the published schema and their streams' lines the stream's; the exit codes
/// (0 passed, 3 failed, 4 inconclusive, 1 refused, 5 a run threw, 6 cancelled); the same scenario gives the same results digest on one
/// job or two; the trace policy keeps what it says; a scripted run's tape replays. Skips without the 1.1 install or the proving ground.
/// </summary>
public sealed class ScenarioRunTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-scenario-tests-userdata"));
        return (course!, game!);
    }

    static SessionOptions Baseline()
    {
        var (course, game) = Grounds();
        return new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
    }

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-scenario-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A scenario file's text into a folder of its own; its path.</summary>
    static string File_(string folder, string name, string text)
    {
        string path = Path.Combine(folder, name + ".scenario.json");
        File.WriteAllText(path, text);
        return path;
    }

    static CompiledScenario Compile(string path)
    {
        var load = ScenarioSpec.Load(path);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        return load.Scenario!;
    }

    const string Stand = """
        { "format": "vitomir-scenario/1", "title": "two stand", "ground": "free-yard",
          "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "control": { "ai": "stand" } },
                      { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false },
                        "place": { "from": "A", "distanceMetres": 3 }, "control": { "ai": "stand" } } ],
          "stop": { "afterSeconds": 1 }, "runs": RUNS, "seed": 1,
          "assertions": [ ASSERT ] }
        """;

    static string StandWith(string assertion, int runs = 2) => Stand.Replace("ASSERT", assertion).Replace("RUNS", runs.ToString(CultureInfo.InvariantCulture));

    // ---- the arena's tables, byte for byte ----

    [Fact]
    public void An_arena_equivalent_scenario_writes_the_arenas_tables_byte_for_byte()
    {
        var (course, _) = Grounds();
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A", "template": "Christof", "weapon": "BroadSword", "sheet": { "template": "sandbox.newly_embraced" } },
                            { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained" } ],
              "seconds": 4, "bouts": 2, "tapes": 1, "seed": 7,
              "sweep": { "fighter": "A", "trait": "melee", "values": [0, 3] } }
            """, Path.GetTempPath(), course);
        string arena = Folder("arena-eq-a"), scenario = Folder("arena-eq-s");
        ArenaRunner.Execute(spec, Baseline(), arena, _ => { });
        var (results, exit) = ScenarioRunner.Execute(ScenarioPlan.FromArena(spec), Baseline(), scenario, _ => { });
        Assert.Equal(0, exit);
        foreach (var (a, s) in new[] { ("bouts.csv", "bouts.csv"), ("contacts.csv", "contacts.csv"), ("powers.csv", "powers.csv") })
            Assert.Equal(File.ReadAllText(Path.Combine(arena, a)), File.ReadAllText(Path.Combine(scenario, s)));
        Assert.True(File.ReadAllLines(Path.Combine(arena, "contacts.csv")).Length > 1, "the bouts made contacts (the comparison means something)");
        Assert.Equal(4, results.RunCounts!.Completed);
        Assert.Single(results.Comparisons!);
        Assert.Equal(2, results.Comparisons![0].MatchedSeedCount);
        // The normalized tables: a row a run, two a run for the actors.
        Assert.Equal(5, File.ReadAllLines(Path.Combine(scenario, "runs.csv")).Length);
        Assert.Equal(9, File.ReadAllLines(Path.Combine(scenario, "actors.csv")).Length);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(scenario, "sweeps.csv")).Length);
    }

    [Fact]
    public void A_scenario_file_of_two_duellists_writes_the_same_tables_as_the_arena_spec_of_them()
    {
        var (course, _) = Grounds();
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A", "template": "Christof", "weapon": "Rapier", "shield": false, "sheet": "sandbox.newly_embraced", "place": [38, 3], "facing": 0, "brain": { "kind": "duel" } },
                            { "name": "B", "template": "rudolfGuard", "weapon": "Rapier", "shield": false, "sheet": "sandbox.guard_trained", "place": [41, 3], "facing": 180, "brain": { "kind": "duel" } } ],
              "bouts": 2, "seed": 100, "seconds": 6, "tapes": 0 }
            """, Path.GetTempPath(), course);
        string folder = Folder("file-eq");
        var plan = ScenarioPlan.From(Compile(File_(folder, "duel", """
            { "format": "vitomir-scenario/1", "title": "two duellists", "ground": "free-yard",
              "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "facing": 0, "foe": "B", "control": { "ai": "duel" } },
                          { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false }, "place": [41, 3], "facing": 180, "foe": "A", "control": { "ai": "duel" } } ],
              "stop": { "afterSeconds": 6 }, "runs": 2, "seed": 100 }
            """)));
        string arena = Folder("file-eq-a"), scenario = Folder("file-eq-s");
        ArenaRunner.Execute(spec, Baseline(), arena, _ => { });
        ScenarioRunner.Execute(plan, Baseline(), scenario, _ => { });
        Assert.Equal(File.ReadAllText(Path.Combine(arena, "bouts.csv")), File.ReadAllText(Path.Combine(scenario, "bouts.csv")));
        Assert.Equal(File.ReadAllText(Path.Combine(arena, "contacts.csv")), File.ReadAllText(Path.Combine(scenario, "contacts.csv")));
        Assert.Equal(File.ReadAllText(Path.Combine(arena, "powers.csv")), File.ReadAllText(Path.Combine(scenario, "powers.csv")));
    }

    // ---- the design's examples 2 and 3 (small) ----

    const string Example2 = """
        { "format": "vitomir-scenario/1", "title": "Scripted blows against a guard who guards by his skill (small)", "ground": "free-yard", "time": "night",
          "actors": [
            { "name": "Christof", "template": "Christof", "sheet": { "template": "sandbox.newly_embraced", "values": { "melee": 0 } },
              "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "facing": { "toward": "Guard" }, "foe": "Guard",
              "control": { "script": [
                  { "moveTo": { "actor": "Guard", "distanceMetres": 1.3 }, "gait": "walk", "faceWhileMoving": "Guard", "withinMetres": 0.1, "forSeconds": 6, "label": "close in" },
                  { "mark": "blows begin" },
                  { "repeat": 2, "steps": [
                      { "attack": "right", "holdSeconds": 0.3, "aimAt": "chest" }, { "wait": 0.6 },
                      { "attack": "left", "holdSeconds": 0.3, "aimAt": "upperArm" }, { "wait": 0.6 },
                      { "attack": "overhead", "holdSeconds": 0.4, "aimAt": "head" }, { "wait": 0.6 },
                      { "attack": "thrust", "holdSeconds": 0.2, "aimAt": "belly" }, { "wait": 0.6 },
                      { "keepDistance": { "from": "Guard", "metres": 1.3, "toleranceMetres": 0.1 }, "gait": "walk", "forSeconds": 0.5 } ] },
                  { "wait": 1.0 },
                  { "mark": "blows end" } ], "then": "stand" } },
            { "name": "Guard", "template": "rudolfGuard", "sheet": { "template": "sandbox.guard_trained", "values": { "health_pool": 1000 } },
              "gear": { "weapon": "Rapier", "shield": false }, "place": { "from": "Christof", "distanceMetres": 3.0, "bearingDegrees": 0 },
              "facing": { "toward": "Christof" }, "foe": "Christof",
              "control": { "ai": { "kind": "duel", "pace": "none" }, "mute": ["attack", "move"] } } ],
          "stop": { "afterSeconds": 30, "onHarnessStops": "first",
                    "when": [ { "count": { "event": "Marker", "by": "Christof", "where": { "text": "blows end" } }, "atLeast": 1 } ] },
          "runs": 2, "seed": 3000,
          "sweep": [ { "set": "Christof.sheet.values.melee", "values": [0, 2, 5] } ],
          "measures": [
            { "name": "blowsBegun", "count": { "event": "AttackBegun", "by": "Christof", "fromMarker": "blows begin" } },
            { "name": "guardBlockShare", "rate": { "of": { "event": "GuardResolved", "by": "Christof", "on": "Guard", "where": { "verdict": ["held", "parried"] } },
                                                   "per": { "event": "AttackBegun", "by": "Christof", "fromMarker": "blows begin" } } },
            { "name": "regionAsAimed", "rate": { "of": { "event": "ContactMade", "by": "Christof", "on": "Guard", "where": { "met": "body", "regionAsAimed": true } },
                                                 "per": { "event": "ContactMade", "by": "Christof", "on": "Guard", "where": { "met": "body" } } } },
            { "name": "woundsPerBlow", "rate": { "of": { "event": "HarmResolved", "by": "Christof", "on": "Guard", "sum": "wounds" },
                                                 "per": { "event": "AttackBegun", "by": "Christof", "fromMarker": "blows begin" } } },
            { "name": "timeToContact", "delay": { "from": { "event": "AttackBegun", "by": "Christof" }, "to": { "event": "ContactMade", "by": "Christof" }, "pairBy": "swing" } } ],
          "assertions": [
            { "name": "the script was carried out: 8 blows begun", "measure": "blowsBegun", "equals": 8, "scope": "eachRun" },
            { "name": "the guard never attacked (muted)", "count": { "event": "AttackBegun", "by": "Guard" }, "equals": 0, "scope": "eachRun" },
            { "name": "no wound through a held guard", "check": "blockedBodyNoWound", "isTrue": true, "scope": "eachRun" },
            { "name": "the guard blocks less as Melee rises", "measure": "guardBlockShare", "scope": "trend", "over": "Christof.sheet.values.melee", "direction": "falling" } ],
          "outputs": { "events": "first", "traceRuns": 1, "csv": true, "summaryPage": true } }
        """;

    const string Example3 = """
        { "format": "vitomir-scenario/1", "title": "Celerity used at 4 s (small)", "ground": "free-yard",
          "actors": [
            { "name": "Christof", "template": "Christof",
              "sheet": { "template": "sandbox.newly_embraced", "values": { "blood_pool": 100 }, "powers": { "celerity": { "rank": 1, "options": ["celerity.active"] } } },
              "blood": 100, "gear": { "weapon": "Rapier", "shield": false }, "place": [39, 3], "facing": { "toward": "Guard" }, "foe": "Guard",
              "control": { "script": [
                  { "wait": 0.5 }, { "mark": "before" },
                  { "attack": "right", "holdSeconds": 0.0 }, { "attack": "left", "holdSeconds": 0.0 },
                  { "usePower": "celerity", "at": 4.0, "label": "celerity" },
                  { "mark": "after" },
                  { "attack": "right", "holdSeconds": 0.0 }, { "attack": "left", "holdSeconds": 0.0 },
                  { "wait": 1.0 }, { "mark": "done" } ], "then": "end" } },
            { "name": "Guard", "template": "rudolfGuard", "sheet": { "template": "sandbox.guard_trained", "values": { "health_pool": 1000 } },
              "gear": { "weapon": "Rapier", "shield": false }, "place": { "from": "Christof", "distanceMetres": 1.25, "bearingDegrees": 0 },
              "facing": { "toward": "Christof" }, "control": { "ai": "stand" } } ],
          "stop": { "afterSeconds": 15, "onHarnessStops": "first" },
          "runs": 2, "seed": 5000,
          "sweep": [ { "set": "Christof.sheet.powers.celerity.rank", "values": [0, 1, 3] } ],
          "measures": [
            { "name": "toContactAfter", "delay": { "from": { "event": "AttackBegun", "by": "Christof", "fromMarker": "after", "toMarker": "done" },
                                                   "to": { "event": "ContactMade", "by": "Christof" }, "pairBy": "swing" } },
            { "name": "blowsPerSecondAfter", "rate": { "of": { "event": "AttackBegun", "by": "Christof", "fromMarker": "after", "toMarker": "done" }, "perSecond": true } },
            { "name": "bloodPaid", "value": "Christof.bloodSpent" },
            { "name": "refusedNoSlot", "count": { "event": "StepRefused", "by": "Christof", "where": { "why": "NoSlot" } } } ],
          "assertions": [
            { "name": "rank 0: nothing is paid", "points": { "Christof.sheet.powers.celerity.rank": 0 }, "measure": "bloodPaid", "equals": 0, "scope": "eachRun" },
            { "name": "rank 0: the step is refused, no slot", "points": { "Christof.sheet.powers.celerity.rank": 0 }, "measure": "refusedNoSlot", "equals": 1, "scope": "eachRun" },
            { "name": "ranks 1 and 3: 30 Blood paid", "points": { "Christof.sheet.powers.celerity.rank": [1, 3] }, "measure": "bloodPaid", "equals": 30, "scope": "eachRun" } ],
          "outputs": { "events": "first", "traceRuns": 1, "ticks": { "everyTicks": 30, "runs": "first" }, "csv": true, "summaryPage": true } }
        """;

    static void Valid(string folder, ScenarioResults results)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "results.json")))!;
        var errors = MiniSchema.Errors(json, ScenarioSchemas.Results());
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
        Assert.Equal("COMPLETED", json["state"]!.GetValue<string>());
        var line = ScenarioSchemas.EventLine();
        foreach (var stream in Directory.GetFiles(Path.Combine(folder, "events"), "*.events.jsonl"))
        {
            var lines = File.ReadAllLines(stream);
            Assert.Equal("VITOMIR_EVENT_TRACE_2", JsonNode.Parse(lines[0])!["schema"]!.GetValue<string>());
            Assert.Empty(MiniSchema.Errors(JsonNode.Parse(lines[0]), ScenarioSchemas.EventHead()));
            for (int i = 1; i < lines.Length; i++)
            {
                var e = MiniSchema.Errors(JsonNode.Parse(lines[i]), line);
                Assert.True(e.Count == 0, $"{Path.GetFileName(stream)} line {i + 1}: {string.Join("; ", e)}");
                Assert.Equal(i - 1, JsonNode.Parse(lines[i])!["eventId"]!.GetValue<int>());
            }
        }
        Assert.Equal(results.Summary.Runs + 1, File.ReadAllLines(Path.Combine(folder, "runs.csv")).Length);
    }

    [Fact]
    public void Example_2_runs_end_to_end_and_its_results_pass_the_published_schema()
    {
        Grounds();
        string folder = Folder("ex2"), out_ = Folder("ex2-out");
        var plan = ScenarioPlan.From(Compile(File_(folder, "02-sword", Example2)));
        var (results, exit) = ScenarioRunner.Execute(plan, Baseline(), out_, _ => { });
        Valid(out_, results);
        var byName = results.Assertions.ToDictionary(a => a.Name);
        Assert.Equal("pass", byName["the guard never attacked (muted)"].Result);
        Assert.Equal("pass", byName["no wound through a held guard"].Result);
        Assert.Equal("pass", byName["the script was carried out: 8 blows begun"].Result);
        Assert.Equal("trend", byName["the guard blocks less as Melee rises"].Scope);
        Assert.Equal("STATISTICAL", byName["the guard blocks less as Melee rises"].Kind);
        Assert.All(results.Points, p => Assert.Equal("HYBRID", p.ExecutionMode));
        Assert.All(results.Points, p => Assert.Equal(2, p.Ends.Where(e => e.Key.StartsWith("CONDITION:", StringComparison.Ordinal)).Sum(e => e.Value)));
        Assert.Contains(exit, new[] { 0, 3, 4 });
        // The aimed blows reach the stream: a body contact of an aimed blow carries the region asked and whether it was met.
        string stream = File.ReadAllText(Path.Combine(out_, "events", "p000-r000.events.jsonl"));
        Assert.Contains("\"aimedRegion\"", stream);
        Assert.Contains("\"kind\":\"GuardResolved\"", stream);
        Assert.Contains("\"kind\":\"AimedBlow\"", stream);
        Assert.Equal(6, results.Metrics!.Count(m => m.MetricId == "guardBlockShare") * 2);
        var share = results.Metrics!.First(m => m.MetricId == "guardBlockShare");
        Assert.Equal(2, share.NIndependent);
        Assert.Equal("bootstrap over runs (2000 resamples, fixed seed)", share.Interval95!.Method);   // many blows a run: not Wilson
        // Paired: each point against point 0 on the same seeds, each named measure's per-seed difference.
        Assert.Equal(2, results.Comparisons!.Count);
        var d = results.Comparisons![0].Differences!.First(x => x.MetricId == "blowsBegun");
        Assert.Equal(2, d.Pairs);
        Assert.Equal(0, d.MeanDifference);      // the script's 8 blows at every Melee
        Assert.Equal(0, d.PairsChanged);
    }

    [Fact]
    public void Example_3_runs_end_to_end_with_a_power_at_a_set_time_and_its_results_pass_the_published_schema()
    {
        Grounds();
        string folder = Folder("ex3"), out_ = Folder("ex3-out");
        var plan = ScenarioPlan.From(Compile(File_(folder, "03-celerity", Example3)));
        var (results, exit) = ScenarioRunner.Execute(plan, Baseline(), out_, _ => { });
        Valid(out_, results);
        Assert.All(results.Assertions, a => Assert.Equal("pass", a.Result));
        Assert.Equal(0, exit);
        Assert.All(results.Points, p => Assert.All(p.Ends.Keys, k => Assert.Equal("SCENARIO_END:script", k)));
        Assert.True(File.Exists(Path.Combine(out_, "ticks", "p000-r000.ticks.jsonl")));
        Assert.Contains("\"kind\":\"PowerUsed\"", File.ReadAllText(Path.Combine(out_, "events", "p001-r000.events.jsonl")));
        Assert.Contains(",usePower,denied,celerity,NoSlot", File.ReadAllText(Path.Combine(out_, "actions.csv")));
    }

    /// <summary>The repository's examples (tools/platform/scenarios/examples), whatever they say now: one run a point, results.json valid.</summary>
    [Theory]
    [InlineData("02-sword-scripted-blows-vs-guard-by-melee.scenario.json")]
    [InlineData("03-celerity-at-a-set-time.scenario.json")]
    public void The_repositorys_examples_2_and_3_run_one_run_a_point_and_their_results_pass_the_schema(string name)
    {
        Grounds();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        string path = Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples", name);
        Assert.SkipUnless(File.Exists(path), $"no {name}");
        var plan = ScenarioPlan.From(Compile(path));
        string out_ = Folder("repo-" + name[..2]);
        var (results, exit) = ScenarioRunner.Execute(plan, Baseline(), out_, _ => { }, new ScenarioRunOptions { Runs = 1 });
        Valid(out_, results);
        Assert.Equal(plan.Points.Count, results.RunCounts!.Completed);
        Assert.Contains(exit, new[] { 0, 3, 4 });
    }

    /// <summary>
    /// The value forms a fight between two duelling brains gives (each against the arena's own counts and the world): the time of the
    /// first blow, Health at the first contact, a value at a set second, a difference and a ratio of measures, a rate a second in a
    /// window of seconds, a count with a sum, and a stop on a value (the first wound ends the run: CONDITION:0).
    /// </summary>
    [Fact]
    public void The_value_forms_and_a_stop_on_a_value_agree_with_the_runs_own_counts()
    {
        Grounds();
        string folder = Folder("forms");
        string text = """
            { "format": "vitomir-scenario/1", "title": "two duellists", "ground": "free-yard",
              "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3],
                            "foe": "B", "control": { "ai": "duel" } },
                          { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false },
                            "place": { "from": "A", "distanceMetres": 3 }, "foe": "A", "control": { "ai": "duel" } } ],
              "stop": { "afterSeconds": 20, "when": [ { "value": "B.woundsTaken", "atLeast": 1 } ] }, "runs": 3, "seed": 11,
              "measures": [
                { "name": "firstBlowA", "timeOf": { "event": "AttackBegun", "by": "A" } },
                { "name": "healthAtFirstContactOnB", "value": "B.health", "atEvent": { "event": "ContactMade", "on": "B", "where": { "met": "body" } } },
                { "name": "swingsA", "count": { "event": "AttackBegun", "by": "A" } },
                { "name": "swingsB", "count": { "event": "AttackBegun", "by": "B" } },
                { "name": "swingDifference", "difference": ["swingsA", "swingsB"] },
                { "name": "swingRatio", "ratio": ["swingsA", "A.swings"] },
                { "name": "blowsPerSecondFirst5", "rate": { "of": { "event": "AttackBegun", "by": "A", "toSeconds": 5 }, "perSecond": true } },
                { "name": "woundsOnB", "count": { "event": "HarmResolved", "on": "B", "sum": "wounds" } } ],
              "assertions": [
                { "name": "they start 3 m apart", "value": "A.distanceTo.B", "atSeconds": 0, "between": [2.99, 3.01], "scope": "eachRun" },
                { "name": "the count is the arena's", "measure": "swingRatio", "equals": 1, "scope": "anyRun" } ] }
            """;
        var plan = ScenarioPlan.From(Compile(File_(folder, "forms", text)));
        string out_ = Folder("forms-out");
        var (results, _) = ScenarioRunner.Execute(plan, Baseline(), out_, _ => { });
        Valid(out_, results);
        var p = results.Points[0];
        Assert.All(results.Assertions, a => Assert.Equal("pass", a.Result));
        var rows = File.ReadAllLines(Path.Combine(out_, "runs.csv")).Select(l => l.Split(',')).ToList();
        int Col(string n) => Array.IndexOf(rows[0], n);
        foreach (var r in rows.Skip(1))
        {
            double D(string n) => double.Parse(r[Col(n)], CultureInfo.InvariantCulture);
            Assert.Equal(D("swingsA") - D("swingsB"), D("swingDifference"), 9);
            if (r[Col("stop")] == "CONDITION:0") Assert.True(D("woundsOnB") >= 1);
            Assert.True(D("firstBlowA") <= D("seconds"));
            Assert.True(D("healthAtFirstContactOnB") <= 100);
        }
        Assert.Equal("time", p.Measures["firstBlowA"].Kind);
        Assert.Equal("ratePerSecond", p.Measures["blowsPerSecondFirst5"].Kind);
        Assert.Equal(5, p.Measures["blowsPerSecondFirst5"].Denominator!.Value / 3, 6);   // three runs' windows of 5 s each (a run longer than 5 s)
    }

    // ---- the exit codes ----

    static int Cli(params string[] args) => Entry.Run(args);

    [Fact]
    public void The_exit_codes_say_passed_failed_inconclusive_refused_and_a_missing_out()
    {
        Grounds();
        string folder = Folder("exits");
        string pass = File_(folder, "pass", StandWith("""{ "name": "nobody attacks", "count": { "event": "AttackBegun" }, "equals": 0, "scope": "eachRun" }"""));
        string fail = File_(folder, "fail", StandWith("""{ "name": "someone attacks", "count": { "event": "AttackBegun" }, "atLeast": 1, "scope": "eachRun" }"""));
        string inconclusive = File_(folder, "maybe", StandWith("""{ "name": "one run, no interval", "value": "A.health", "atLeast": 50, "scope": "aggregate" }""", runs: 1));
        string refused = File_(folder, "refused", StandWith("""{ "name": "x", "count": { "event": "AttackBegunn" }, "equals": 0, "scope": "eachRun" }"""));
        string Out(string n) => Path.Combine(folder, "out-" + n);
        Assert.Equal(0, Cli("--scenario", pass, "--out", Out("pass"), "--sandbox"));
        Assert.Equal(3, Cli("--scenario", fail, "--out", Out("fail"), "--sandbox"));
        Assert.Equal(4, Cli("--scenario", inconclusive, "--out", Out("maybe"), "--sandbox"));
        Assert.Equal(1, Cli("--scenario", refused, "--out", Out("refused"), "--sandbox"));
        var refusedJson = JsonNode.Parse(File.ReadAllText(Path.Combine(Out("refused"), "results.json")))!;
        Assert.Equal("FAILED", refusedJson["state"]!.GetValue<string>());
        Assert.Empty(MiniSchema.Errors(refusedJson, ScenarioSchemas.Results()));
        Assert.Equal(1, Cli("--scenario", pass, "--sandbox"));   // no --out
        // A folder: each scenario into its own folder, batch.json, the worst code (1: one refused).
        Assert.Equal(1, Cli("--scenario", folder, "--out", Out("batch"), "--sandbox"));
        var batch = JsonNode.Parse(File.ReadAllText(Path.Combine(Out("batch"), "batch.json")))!;
        Assert.Equal(4, batch["scenarios"]!.AsArray().Count);
        Assert.Equal(1, batch["exitCode"]!.GetValue<int>());
        Assert.True(File.Exists(Path.Combine(Out("batch"), "pass", "results.json")));
    }

    [Fact]
    public void A_run_that_throws_is_exit_5_and_a_cancelled_run_set_is_exit_6_with_its_partial_results()
    {
        var (course, _) = Grounds();
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A", "template": "Christof", "sheet": { "template": "sandbox.newly_embraced", "values": { "no_such_trait": 3 } } },
                            { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained" } ], "seconds": 1, "bouts": 1, "tapes": 0 }
            """, Path.GetTempPath(), course);
        string threwFolder = Folder("threw");
        var (threw, exit) = ScenarioRunner.Execute(ScenarioPlan.FromArena(spec), Baseline(), threwFolder, _ => { });
        Assert.Equal(5, exit);
        Assert.Equal(1, threw.RunCounts!.FailedExecution);
        Assert.Contains("no_such_trait", File.ReadAllText(Path.Combine(threwFolder, "runs.csv")));   // the run's row says why

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        string folder = Folder("cancelled");
        var good = spec with { Fighters = [spec.Fighters[0] with { Sheet = new CastSheet { Template = "sandbox.newly_embraced" } }, spec.Fighters[1]] };
        var (results, code) = ScenarioRunner.Execute(ScenarioPlan.FromArena(good), Baseline(), folder, _ => { }, new ScenarioRunOptions { Cancel = cancel.Token });
        Assert.Equal(6, code);
        Assert.Equal("CANCELLED", results.State);
        Assert.Equal(1, results.RunCounts!.Cancelled);
        Assert.True(File.Exists(Path.Combine(folder, "results.json")));
    }

    // ---- the same results on one job or two; the trace policy; a tape replays ----

    [Fact]
    public void The_same_scenario_gives_the_same_results_digest_on_one_job_or_two_and_the_trace_policy_keeps_what_it_says()
    {
        Grounds();
        string folder = Folder("jobs");
        var compiled = Compile(File_(folder, "02-sword", Example2.Replace("\"runs\": 2", "\"runs\": 2").Replace("[0, 2, 5]", "[0, 5]")));
        var plan = ScenarioPlan.From(compiled);
        string oneFolder = Folder("jobs-1"), twoFolder = Folder("jobs-2");
        var (one, _) = ScenarioRunner.Execute(plan, Baseline(), oneFolder, _ => { });
        var (two, _) = ScenarioRunner.Execute(plan, Baseline(), twoFolder, _ => { }, new ScenarioRunOptions { Jobs = 2 });
        Assert.Equal(one.Provenance.ResultsDigest, two.Provenance.ResultsDigest);
        Assert.Equal(File.ReadAllText(Path.Combine(oneFolder, "runs.csv")), File.ReadAllText(Path.Combine(twoFolder, "runs.csv")));
        Assert.Equal(File.ReadAllText(Path.Combine(oneFolder, "contacts.csv")), File.ReadAllText(Path.Combine(twoFolder, "contacts.csv")));
        // FAILURES keeps only the runs that failed (none here: the assertions hold in every run, so no stream); ALL keeps every run.
        string failures = Folder("trace-f");
        var (kept, _) = ScenarioRunner.Execute(plan with { TracePolicy = TracePolicy.Failures }, Baseline(), failures, _ => { });
        Assert.DoesNotContain(kept.Assertions, a => a.Scope == "eachRun" && a.Result == "fail");
        Assert.Empty(Directory.Exists(Path.Combine(failures, "events")) ? Directory.GetFiles(Path.Combine(failures, "events"), "*.events.jsonl") : []);
        string all = Folder("trace-a");
        ScenarioRunner.Execute(plan with { TracePolicy = TracePolicy.All }, Baseline(), all, _ => { });
        Assert.Equal(4, Directory.GetFiles(Path.Combine(all, "events"), "*.events.jsonl").Length);
        // A scripted run's tape replays from its head alone (the program rides in it).
        string tape = Directory.GetFiles(Path.Combine(all, "tapes"), "*.tape").Order(StringComparer.Ordinal).First();
        Assert.Equal(0, Cli("--replay-check", tape, "--sandbox"));
    }
}
