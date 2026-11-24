// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json.Nodes;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// Objectives and endings, moveTo round the level (a route, no route, a blocked step), the duelling brain round a wall,
/// the paired swap, the player's receipts in the tick trace, and the examples 16 to 20. On the proving ground's free yard, whose
/// 4.5 m wall stands at x 32 to 34, y 0 to 6, and whose house at x 32 to 38, y 12 to 18.
/// </summary>
public sealed class ScenarioGoalTests
{
    static (string Course, string Game) Here()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-goal-tests-userdata"));
        return (course!, game!);
    }

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-ck-goals-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Reads, checks against the game and runs a scenario's text; its results, exit code and output folder.</summary>
    static (ScenarioResults Results, int Exit, string Out, ScenarioPlan Plan) Run(string name, string text, int? runs = null, IEnumerable<string>? switches = null)
    {
        var (course, game) = Here();
        string dir = Folder(name);
        string path = Path.Combine(dir, name + ".scenario.json");
        File.WriteAllText(path, text);
        return RunFile(path, Path.Combine(dir, "out"), runs, switches);
    }

    static (ScenarioResults Results, int Exit, string Out, ScenarioPlan Plan) RunFile(string path, string out_, int? runs = null, IEnumerable<string>? switches = null)
    {
        var (course, game) = Here();
        var set = new HashSet<string>(StringComparer.Ordinal) { "sandbox" };
        foreach (var s in switches ?? []) set.Add(s);
        var baseline = new SessionOptions { Level = course, Game = game, Switches = set };
        using var scenarioGame = ScenarioGame.Load(baseline, course);
        var load = ScenarioSpec.Load(path, scenarioGame, course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!);
        var (results, exit) = ScenarioRunner.Execute(plan, baseline, out_, _ => { }, new ScenarioRunOptions { Runs = runs });
        return (results, exit, out_, plan);
    }

    static List<JsonObject> Events(string out_, string runId) =>
        [.. File.ReadAllLines(Path.Combine(out_, "events", runId + ".events.jsonl")).Select(l => JsonNode.Parse(l)!.AsObject()).Where(o => o["event"] is not null)];

    static string Result(ScenarioResults r, string objective, int point = 0)
    {
        var a = r.Points[point].Objectives!.Single(o => o.Name == objective);
        return a.Met == a.Runs ? "MET" : a.Failed == a.Runs ? "FAILED" : a.Unresolved == a.Runs ? "UNRESOLVED" : $"mixed {a.Met}/{a.Failed}/{a.Unresolved}";
    }

    const string Standing = """
        { "format": "vitomir-scenario/1", "title": "every kind met, failed and left unresolved, standing still",
          "actors": [ { "name": "Christof", "place": [28, 9], "facing": 0, "control": { "ai": "stand" } },
                      { "name": "Guard", "preset": "guard", "place": [40, 9], "facing": 180, "control": { "ai": "stand" } } ],
          "objectives": [
            { "name": "sv-end", "actor": "Christof", "survive": true },
            { "name": "sv-until", "actor": "Christof", "survive": true, "untilSeconds": 1 },
            { "name": "sv-late", "actor": "Guard", "survive": true, "untilSeconds": 5 },
            { "name": "pr-end", "actor": "Christof", "protect": "Guard" },
            { "name": "pr-late", "actor": "Guard", "protect": "Christof", "untilSeconds": 5 },
            { "name": "re-here", "actor": "Christof", "reach": { "point": [28.5, 9], "radiusMetres": 1 } },
            { "name": "re-late", "actor": "Guard", "reach": { "point": [50, 9] }, "bySeconds": 1 },
            { "name": "re-open", "actor": "Guard", "reach": { "point": [50, 9] } },
            { "name": "es-met", "actor": "Christof", "escape": { "fromFoesMetres": 5 } },
            { "name": "es-late", "actor": "Christof", "escape": { "fromFoesMetres": 50 }, "bySeconds": 1 },
            { "name": "es-open", "actor": "Guard", "escape": { "outOf": { "point": [40, 9], "radiusMetres": 3 } } },
            { "name": "es-out", "actor": "Guard", "escape": { "outOf": { "point": [28, 9], "radiusMetres": 3 } } },
            { "name": "ho-met", "actor": "Christof", "hold": { "point": [28, 9], "radiusMetres": 1 }, "untilSeconds": 1 },
            { "name": "ho-out", "actor": "Guard", "hold": { "point": [28, 9], "radiusMetres": 1 }, "untilSeconds": 1 },
            { "name": "ho-open", "actor": "Christof", "hold": { "place": "free-yard-4.5", "radiusMetres": 12 }, "untilSeconds": 5 },
            { "name": "de-late", "actor": "Christof", "defeat": "Guard", "bySeconds": 1 },
            { "name": "de-open", "actor": "Christof", "defeat": "Guard" } ],
          "stop": { "afterSeconds": 2 }, "runs": 1 }
        """;

    const string Fight = """
        { "format": "vitomir-scenario/1", "title": "a stop decides them",
          "actors": [ { "name": "Christof", "place": [38, 9], "facing": 0, "health": 5, "foe": "Guard", "control": { "ai": "stand" } },
                      { "name": "Guard", "preset": "guard", "place": [40, 9], "facing": 180, "foe": "Christof", "control": { "ai": { "kind": "duel" } } },
                      { "name": "Friend", "place": [24, 15], "facing": 0, "foe": "Guard", "control": { "ai": "stand" } } ],
          "objectives": [
            { "name": "de-met", "actor": "Guard", "defeat": "Christof" },
            { "name": "sv-fail", "actor": "Christof", "survive": true },
            { "name": "pr-fail", "actor": "Friend", "protect": "Christof" },
            { "name": "ho-fail", "actor": "Christof", "hold": { "point": [38, 9], "radiusMetres": 2 }, "untilSeconds": 30 },
            { "name": "re-fail", "actor": "Christof", "reach": { "point": [50, 9] }, "bySeconds": 30 },
            { "name": "es-fail", "actor": "Christof", "escape": { "fromFoesMetres": 30 } },
            { "name": "de-fail", "actor": "Christof", "defeat": "Guard" } ],
          "stop": { "afterSeconds": 20, "onHarnessStops": "never", "onObjectives": "all" }, "runs": 1,
          "outputs": { "tracePolicy": "ALL" } }
        """;

    /// <summary>
    /// Every kind (defeat, survive, reach, escape, protect, hold) MET, FAILED and UNRESOLVED, each with its reason: standing still for
    /// 2 s, and a guard bringing a 5-Health Christof to a harness stop — never called a death — which decides the rest that tick and,
    /// with onObjectives "all", ends the run; the defeat's ObjectiveMet follows from the ActorDied that decided it.
    /// </summary>
    [Fact]
    public void Each_objective_kind_is_met_failed_or_left_unresolved_with_its_reason()
    {
        var (r, exit, _, _) = Run("kinds", Standing);
        Assert.Equal(0, exit);
        var want = new Dictionary<string, string>
        {
            ["sv-end"] = "MET", ["sv-until"] = "MET", ["sv-late"] = "UNRESOLVED", ["pr-end"] = "MET", ["pr-late"] = "UNRESOLVED",
            ["re-here"] = "MET", ["re-late"] = "FAILED", ["re-open"] = "UNRESOLVED", ["es-met"] = "MET", ["es-late"] = "FAILED",
            ["es-open"] = "UNRESOLVED", ["es-out"] = "MET", ["ho-met"] = "MET", ["ho-out"] = "FAILED", ["ho-open"] = "UNRESOLVED",
            ["de-late"] = "FAILED", ["de-open"] = "UNRESOLVED",
        };
        foreach (var (name, result) in want) Assert.True(Result(r, name) == result, $"{name}: {Result(r, name)}, not {result}");
        var lateReach = r.Points[0].Objectives!.Single(o => o.Name == "re-late");
        Assert.Contains(lateReach.Reasons!.Keys, k => k.StartsWith("FAILED: not done by", StringComparison.Ordinal));

        var (f, fexit, fout, _) = Run("fight", Fight);
        Assert.Equal(0, fexit);
        foreach (var n in new[] { "sv-fail", "pr-fail", "ho-fail", "re-fail", "es-fail", "de-fail" }) Assert.Equal("FAILED", Result(f, n));
        Assert.Equal("MET", Result(f, "de-met"));
        Assert.Equal(1, f.Points[0].Ends["OBJECTIVES:all"]);
        var rows = File.ReadAllLines(Path.Combine(fout, "runs.csv"));
        Assert.Contains("ending", rows[0]);
        Assert.Contains("objective_de-met", rows[0]);
        Assert.Contains("ZERO_HEALTH_STOP", rows[1]);
        Assert.Contains("a harness stop, not a death", rows[1]);
        Assert.DoesNotContain("died", rows[1]);
        var events = Events(fout, "p000-r000");
        var met = events.Single(e => (string?)e["kind"] == "ObjectiveMet" && (string?)e["event"]!["Objective"] == "de-met");
        int parent = (int)met["parentEventId"]!;
        Assert.Equal("ActorDied", (string?)events.Single(e => (int)e["eventId"]! == parent)["kind"]);
    }

    /// <summary>The check refuses what an objective or a swap cannot be, at its JSON pointer, with the nearest names.</summary>
    [Fact]
    public void The_check_refuses_objectives_an_actor_cannot_have_at_their_pointers()
    {
        var (course, game) = Here();
        string dir = Folder("refused");
        string path = Path.Combine(dir, "bad.scenario.json");
        File.WriteAllText(path, """
            { "format": "vitomir-scenario/1",
              "actors": [ { "name": "Christof", "place": [28, 9], "side": "a" }, { "name": "Guard", "place": [40, 9], "side": "b" },
                          { "name": "Friend", "place": [28, 12], "side": "a" } ],
              "objectives": [
                { "actor": "Gaurd", "survive": true },
                { "actor": "Christof", "reach": { "place": "free-yrad" } },
                { "actor": "Christof", "reach": { "point": [30, 9], "radiusMetres": -1 } },
                { "actor": "Christof", "defeat": "Christof" },
                { "side": "a", "reach": { "point": [30, 9] } },
                { "actor": "Christof", "reach": { "point": [30, 9] }, "untilSeconds": 3 },
                { "actor": "Christof", "defeat": "Friend" },
                { "actor": "Christof" } ],
              "swap": { "actors": ["Christof", "Gard"] } }
            """);
        var baseline = new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        using var scenarioGame = ScenarioGame.Load(baseline, course);
        var load = ScenarioSpec.Load(path, scenarioGame, course);
        Assert.False(load.Ok);
        var lines = load.Errors.Select(e => e.Line()).ToList();
        string all = string.Join("\n", lines);
        Assert.Contains(lines, l => l.Contains("/objectives/0/actor") && l.Contains("Guard") && l.Contains("[UnknownName]"));
        Assert.Contains(lines, l => l.Contains("/objectives/1/reach/place") && l.Contains("free-yard") && l.Contains("[UnknownName]"));
        Assert.Contains(lines, l => l.Contains("/objectives/2/reach/radiusMetres") && l.Contains("[OutOfRange]"));
        Assert.Contains(lines, l => l.Contains("/objectives/3/defeat") && l.Contains("itself") && l.Contains("[BadObjective]"));
        Assert.Contains(lines, l => l.Contains("/objectives/4/reach") && l.Contains("a side cannot") && l.Contains("[BadObjective]"));
        Assert.Contains(lines, l => l.Contains("/objectives/5/untilSeconds") && l.Contains("[BadObjective]"));
        Assert.Contains(lines, l => l.Contains("/objectives/6/defeat") && l.Contains("own side") && l.Contains("[BadObjective]"));
        Assert.Contains(lines, l => l.Contains("/objectives/7") && l.Contains("names no kind") && l.Contains("[BadObjective]"));
        Assert.True(lines.Any(l => l.Contains("/swap/actors/1") && l.Contains("Guard")), all);

        File.WriteAllText(path, """{ "format": "vitomir-scenario/1", "actors": [ { "name": "A", "place": [28, 9] }, { "name": "B", "place": [40, 9] } ], "stop": { "onObjectives": "first" } }""");
        var none = ScenarioSpec.Load(path, scenarioGame, course);
        Assert.Contains(none.Errors, e => e.Line().Contains("/stop/onObjectives") && e.Line().Contains("[BadObjective]"));
        // A scenario without objectives resolves as before: no "objectives", no "swap", no "onObjectives" written.
        File.WriteAllText(path, """{ "format": "vitomir-scenario/1", "actors": [ { "name": "A", "place": [28, 9] }, { "name": "B", "place": [40, 9] } ] }""");
        var plain = ScenarioSpec.Load(path, scenarioGame, course);
        Assert.True(plain.Ok);
        Assert.DoesNotContain("objectives", plain.Scenario!.ResolvedJson);
        Assert.DoesNotContain("onObjectives", plain.Scenario.ResolvedJson);
        Assert.DoesNotContain("\"swap\"", plain.Scenario.ResolvedJson);
    }

    const string Moves = """
        { "format": "vitomir-scenario/1", "title": "moveTo: round the wall, into the house, into a body",
          "actors": [
            { "name": "Christof", "place": [27, 3], "facing": 0, "control": { "script": [
                { "moveTo": [40, 3], "gait": "run", "forSeconds": 15, "label": "round the wall" },
                { "moveTo": [35, 15], "forSeconds": 5, "label": "into the house" },
                { "path": [[27, -3]], "gait": "run", "forSeconds": 8, "label": "back round" } ] } },
            { "name": "Walker", "place": [27, 9], "facing": 0, "control": { "script": [
                { "moveTo": { "actor": "Post", "distanceMetres": 0 }, "forSeconds": 20, "label": "into him" } ] } },
            { "name": "Post", "place": [31, 9], "control": { "ai": "stand" } } ],
          "stop": { "afterSeconds": 14, "onHarnessStops": "never" }, "runs": 1, "outputs": { "tracePolicy": "ALL" } }
        """;

    /// <summary>
    /// moveTo round the level: its straight line to [40, 3] meets the wall, so it runs the mesh's route round the wall's end and arrives
    /// (MoveToRouted, then StepEnded done); a point inside the house has no route (StepRefused NoRoute and MoveToFailed NoRoute, where it
    /// stood); a walk into another body's place stops moving and is reported Blocked with the path it walked — never fictional progress.
    /// </summary>
    [Fact]
    public void MoveTo_routes_round_a_wall_and_reports_no_route_and_a_blocked_step()
    {
        var (_, exit, out_, _) = Run("moves", Moves);
        Assert.Equal(0, exit);
        var ev = Events(out_, "p000-r000");
        JsonObject One(string kind, string label) => ev.First(e => (string?)e["kind"] == kind && (string?)e["event"]!["Label"] == label);
        var routed = One("MoveToRouted", "round the wall")["event"]!;
        Assert.True((int)routed["Corners"]! >= 2);
        Assert.True((double)routed["RouteMetres"]! > (double)routed["StraightMetres"]!);
        Assert.Equal("Done", (string?)One("StepEnded", "round the wall")["event"]!["Outcome"]);
        Assert.Equal("NoRoute", (string?)One("StepRefused", "into the house")["event"]!["Why"]);
        Assert.Equal("NoRoute", (string?)One("MoveToFailed", "into the house")["event"]!["Why"]);
        Assert.True((int)One("MoveToRouted", "back round")["event"]!["Corners"]! >= 2);   // a path's leg routes as a moveTo
        Assert.Equal("Done", (string?)One("StepEnded", "back round")["event"]!["Outcome"]);
        var blocked = One("MoveToFailed", "into him")["event"]!;
        Assert.Equal("Blocked", (string?)blocked["Why"]);
        Assert.True(((string)blocked["Walked"]!).Split(';').Length >= 5);
        Assert.True((double)blocked["LeftMetres"]! > 0.3);
        Assert.Equal("TimedOut", (string?)One("StepEnded", "into him")["event"]!["Outcome"]);
    }

    const string WallDuel = """
        { "format": "vitomir-scenario/1", "title": "a guard closing with the wall between",
          "actors": [ { "name": "Christof", "place": [27, 3], "control": { "ai": "stand" } },
                      { "name": "Guard", "preset": "guard", "place": [40, 3], "control": { "ai": { "kind": "duel" } } } ],
          "sweep": [ { "set": "overrides.switches.brain-straight", "values": [false, true] } ],
          "stop": { "afterSeconds": 12 }, "runs": 1,
          "measures": [ { "name": "firstBlow", "timeOf": { "event": "AttackBegun", "by": "Guard" } }, { "name": "gap", "value": "Guard.distanceTo.Christof" } ] }
        """;

    const string OpenDuel = """
        { "format": "vitomir-scenario/1", "title": "open ground: the routing never asked",
          "actors": [ { "name": "Christof", "place": [24, 9], "control": { "ai": { "kind": "duel" } } },
                      { "name": "Guard", "preset": "guard", "place": [30, 9], "control": { "ai": { "kind": "duel" } } } ],
          "sweep": [ { "set": "overrides.switches.brain-straight", "values": [false, true] } ],
          "stop": { "afterSeconds": 10 }, "runs": 2 }
        """;

    /// <summary>
    /// The duelling brain round a wall: measured with the earlier build's brain (brain-straight) the guard runs at Christof and stands pressed
    /// against the wall (over 6 m off at the end, no blow); with the earlier build's it runs the mesh's route round it and strikes. On open ground the
    /// two are the same run, digest for digest (the arena's runs say so over 200 bouts).
    /// </summary>
    [Fact]
    public void The_duelling_brain_closes_round_a_wall_only_when_its_line_meets_it()
    {
        var (r, _, _, _) = Run("wallduel", WallDuel);
        Assert.True(r.Points[0].Measures["firstBlow"].Mean is < 10, $"routing: first blow {r.Points[0].Measures["firstBlow"].Mean}");
        Assert.Null(r.Points[1].Measures["firstBlow"].Mean);
        Assert.True(r.Points[1].Measures["gap"].Mean > 6, $"straight: gap {r.Points[1].Measures["gap"].Mean}");
        var (o, _, _, _) = Run("openduel", OpenDuel);
        var c = Assert.Single(o.Comparisons!);
        Assert.Equal(2, c.MatchedSeedCount);
        Assert.Equal(0, c.DigestsDiffer);
    }

    /// <summary>
    /// The paired swap: every point followed by its twin with two actors' places and facings exchanged (or the sides turned round), on the
    /// same seeds; results.json's swaps set each seed against its twin, the objectives' flips counted.
    /// </summary>
    [Fact]
    public void A_paired_swap_runs_each_seed_again_with_the_places_exchanged()
    {
        const string text = """
            { "format": "vitomir-scenario/1", "title": "swapped",
              "actors": [ { "name": "Christof", "place": [27, 3], "facing": 0, "control": { "ai": "stand" } },
                          { "name": "Guard", "preset": "guard", "place": [40, 3], "facing": 90, "control": { "ai": { "kind": "duel" } } } ],
              "objectives": [ { "name": "close", "actor": "Guard", "reach": { "point": [30, 3], "radiusMetres": 2.5 }, "bySeconds": 6 } ],
              "swap": { "actors": ["Christof", "Guard"] },
              "stop": { "afterSeconds": 6 }, "runs": 2 }
            """;
        var (r, exit, out_, plan) = Run("swap", text);
        Assert.Equal(0, exit);
        Assert.Equal(2, plan.Points.Count);
        var a = plan.Points[0].Cast.Fighters; var b = plan.Points[1].Cast.Fighters;
        Assert.Equal((a[0].X, a[0].Y, a[0].Facing), (b[1].X, b[1].Y, b[1].Facing));
        Assert.Equal((a[1].X, a[1].Y, a[1].Facing), (b[0].X, b[0].Y, b[0].Facing));
        Assert.Contains("swap = Christof<->Guard places exchanged", plan.Points[1].Name);
        var s = Assert.Single(r.Swaps!);
        Assert.Equal(2, s.MatchedSeedCount);
        Assert.Equal(0, s.Point); Assert.Equal(1, s.SwappedPoint);
        Assert.Equal("MET", Result(r, "close", 0));       // the guard runs round the wall's end to [30, 3]
        Assert.Equal("MET", Result(r, "close", 1));       // swapped he starts beside it
        Assert.Contains("swap", File.ReadAllLines(Path.Combine(out_, "runs.csv"))[0].Split(','));
    }

    /// <summary>
    /// The player's receipts (B1/CP-04) on example 20: into the wall at x = 32 the body stops at x = 31.698 with the level
    /// named in the way; the frame, the camera's view and the crosshair's ray are on every traced line of the input-driven actor.
    /// </summary>
    [Fact]
    public void The_tick_trace_carries_the_player_s_receipts_into_a_wall_and_back()
    {
        string path = Example("20-player-input-into-the-wall-receipts.scenario.json");
        var (r, exit, out_, _) = RunFile(path, Folder("receipts"));
        Assert.True(exit == 0, string.Join("; ", r.Assertions.Select(a => $"{a.Name} {a.Result}")));
        var lines = File.ReadAllLines(Path.Combine(out_, "ticks", "p000-r000.ticks.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        JsonNode Me(JsonNode line) => line["actors"]!.AsArray().First(a => (string?)a!["name"] == "Christof")!;
        var at2 = lines.First(l => (double)l["seconds"]! >= 2.4);
        var p = Me(at2)["player"]!;
        Assert.Equal(31.698, (double)Me(at2)["feet"]![0]!, 2);
        Assert.Equal(1.0, (double)p["input"]!["moveForward"]!);
        Assert.Equal("rightShoulder", (string?)p["camera"]!["view"]);
        Assert.NotNull(p["aim"]!["direction"]);
        Assert.Equal("level", (string?)p["blockedBy"]!["what"]);
        Assert.Equal(32.0, (double)p["blockedBy"]!["point"]![0]!, 2);
        var back = Me(lines.First(l => (double)l["seconds"]! >= 5.0))["player"]!;
        Assert.Null(back["blockedBy"]);
        Assert.True((double)back["actual"]!["alongWayMetresPerSecond"]! > 1.5);
        Assert.All(lines.Skip(1), l => Assert.NotNull(Me(l)["player"]));
        Assert.All(lines, l => Assert.Null(l["actors"]!.AsArray().First(a => (string?)a!["name"] == "Onlooker")!["player"]));
    }

    static string Example(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        return Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples", name);
    }

    /// <summary>The examples 16 to 19 check and run (one run a point), their objectives resolved and explained.</summary>
    [Fact]
    public void The_bow_and_route_examples_check_and_run_with_their_objectives_resolved()
    {
        foreach (var name in new[] { "16-one-against-three-szlachta-survive-and-defeat", "17-reach-past-a-wall-guard-closes-by-route",
                     "18-escape-a-szlachta", "19-protect-a-townsman-from-a-szlachta" })
        {
            var (r, _, out_, plan) = RunFile(Example(name + ".scenario.json"), Folder(name[..2]), runs: 1);
            Assert.Equal("COMPLETED", r.State);
            Assert.True(plan.Objectives.Count >= 2, name);
            foreach (var p in r.Points)
                foreach (var o in p.Objectives!) Assert.Equal(1, o.Met + o.Failed + o.Unresolved);
            var rows = File.ReadAllLines(Path.Combine(out_, "runs.csv"));
            Assert.All(rows.Skip(1), row => Assert.Contains("ended by", row));
        }
    }
}
