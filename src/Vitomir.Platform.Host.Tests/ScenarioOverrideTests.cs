// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Scenario;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// A scenario's <c>overrides.data</c> (a module data value by asset and JSON pointer, applied before the module reads
/// it, its own checks run on the result, recorded with the value and the asset's hash before and after, the module's configuration hash
/// changed as it reports it), <c>overrides.actorTempo</c> and <c>overrides.clipRate</c> (the measured wind-up scales with them) and
/// <c>light</c> (perception sees by it); a refused value and a missing pointer at <c>--check</c>; two scenarios in one process share
/// nothing; a taped run with overrides replays; example 5 checks and runs. Skips without the 1.1 install or the proving ground.
/// </summary>
public sealed class ScenarioOverrideTests
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
        return new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Say = _ => { } };
    }

    static ScenarioGame? game;
    static ScenarioGame Game() => game ??= ScenarioGame.Load(Baseline(), Grounds().Course);

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-override-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Christof's blows from the right against a standing guard; OVERRIDES, SWEEP and LIGHT filled in.</summary>
    const string Blows = """
        { "format": "vitomir-scenario/1", "title": "overrides", "ground": "free-yard", LIGHT
          "actors": [
            { "name": "Christof", "sheet": { "template": "sandbox.newly_embraced" }, "gear": { "weapon": "BroadSword", "shield": false },
              "place": [38, 9], "facing": { "toward": "Guard" }, "foe": "Guard",
              "control": { "script": [ { "repeat": 3, "steps": [ { "attack": "right" }, { "wait": 0.5 } ] } ], "then": "stand" } },
            { "name": "Guard", "template": "rudolfGuard", "sheet": { "template": "sandbox.guard_trained", "values": { "health_pool": 1000 } },
              "gear": { "weapon": "Rapier", "shield": false }, "place": { "from": "Christof", "distanceMetres": 1.3 }, "control": { "ai": "stand" } } ],
          "overrides": OVERRIDES,
          "stop": { "afterSeconds": 6 }, "runs": 1, "seed": 11,
          "sweep": SWEEP,
          "measures": [ { "name": "windUpSeconds", "delay": { "from": { "event": "AttackBegun", "by": "Christof" }, "to": { "event": "ContactMade", "by": "Christof" }, "pairBy": "swing" } } ] }
        """;

    static string Text(string overrides, string sweep = "[]", string light = "") =>
        Blows.Replace("OVERRIDES", overrides).Replace("SWEEP", sweep).Replace("LIGHT", light.Length == 0 ? "" : $"\"light\": {light},");

    static ScenarioLoad Read(string text) => ScenarioSpec.Read(text, Path.GetTempPath(), Game(), Grounds().Course, "overrides.scenario.json");

    static CompiledScenario Compile(string text)
    {
        var load = Read(text);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        return load.Scenario!;
    }

    static string WeaponsFile() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Modules"), "weapons.json", SearchOption.AllDirectories).Single();

    static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    // ---- overrides.data ----

    [Fact]
    public void A_data_override_is_applied_before_the_module_reads_it_recorded_and_changes_the_config_hash()
    {
        var baseline = Baseline();
        string onDisk = Sha(WeaponsFile());
        var s = Compile(Text("""{ "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/strikeStarts/0/metres", "value": 0.3 } ] }"""));
        var point = Assert.Single(s.Points);
        // The setup condition: the asset, the pointer, the value before and after, the asset's hash before and after.
        var note = point.Setup.Single(n => n!["kind"]!.GetValue<string>() == "data")!;
        Assert.Equal("combat/weapons.json", note["asset"]!.GetValue<string>());
        Assert.Equal("/activeWindow/strikeStarts/0/metres", note["pointer"]!.GetValue<string>());
        Assert.Equal(0.1, note["before"]!.GetValue<double>(), 6);
        Assert.Equal(0.3, note["value"]!.GetValue<double>(), 6);
        Assert.Equal(onDisk, note["assetSha256Before"]!.GetValue<string>());
        Assert.NotEqual(onDisk, note["assetSha256After"]!.GetValue<string>());
        Assert.False(note["counterfactual"]!.GetValue<bool>());

        // The world the module configured from the changed data: the platform's window rule says 0.3, and the module's hash is its own.
        var options = ScenarioCheckCommand.OptionsFor(s, point, 0, baseline);
        using (var session = Session.Create(options))
        {
            Assert.Equal(0.3f, session.World.Service<ContactConfig>().Window.StrikeStarts[0].Metres, 4);
            using var plain = Session.Create(options with { DataOverrides = null });
            Assert.Equal(0.1f, plain.World.Service<ContactConfig>().Window.StrikeStarts[0].Metres, 4);
            Assert.NotEqual(plain.Modules.Code.Single().Stamp.ConfigHash, session.Modules.Code.Single().Stamp.ConfigHash);
            // The window of the blow from the right opens earlier with 0.3 m than with 0.1 m (its strike start found sooner).
            var assets = session.World.Service<Vitomir.Platform.Animation.CharacterAssets>();
            int kind = assets.FindKind("Christof");
            var right = assets.Kind(kind).Actions!.Stitched.Single(x => x.Direction == "right").Motion;
            var wide = ActiveWindows.Measure(assets, kind, right.Clip, right.StrikeSeconds, session.World.Service<ContactConfig>().Window);
            var narrow = ActiveWindows.Measure(assets, kind, right.Clip, right.StrikeSeconds, plain.World.Service<ContactConfig>().Window);
            Assert.True(wide.From < narrow.From, $"0.3 m opens at {wide.From}, 0.1 m at {narrow.From}");
        }
        Assert.Equal(onDisk, Sha(WeaponsFile()));   // never to the files

        // The run records it: results.json's setup and the point's config hash, the tape's head carries it, and the tape replays.
        string folder = Folder("data-run");
        var (results, exit) = ScenarioRunner.Execute(ScenarioPlan.From(s), baseline, folder, _ => { }, new ScenarioRunOptions());
        Assert.Equal(0, exit);
        var pr = Assert.Single(results.Points);
        Assert.Contains(pr.Setup!, n => n!["kind"]!.GetValue<string>() == "data" && n["assetSha256After"] is not null);
        Assert.NotNull(pr.ConfigHashes);
        Assert.Equal(results.Provenance.Modules.Single().Value.ConfigHash, pr.ConfigHashes!.Single().Value);
        string tape = Directory.GetFiles(Path.Combine(folder, "tapes"), "*.tape").Single();
        Assert.Equal(0, Entry.Run(["--replay-check", tape, "--sandbox"]));
        Assert.Equal(onDisk, Sha(WeaponsFile()));
    }

    [Fact]
    public void A_value_the_module_refuses_is_a_check_error_at_the_overrides_value_with_the_modules_message()
    {
        var baseline = Baseline();
        var s = Compile(Text("""{ "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/strikeStarts/0/metres", "value": 0.2 }, { "asset": "combat/weapons.json", "pointer": "/activeWindow/shareOfPeak", "value": -5 } ] }"""));
        using var log = new HostLog(Path.Combine(Path.GetTempPath(), $"vitomir-override-check-{Environment.ProcessId}.log"));
        var e = Assert.Single(ScenarioCheckCommand.WorldErrors(s, baseline, log));
        Assert.Equal((ScenarioErrorCode.Refused, "/overrides/data/1/value"), (e.Code, e.Pointer));
        Assert.Contains("shareOfPeak", e.Message);
        Assert.Contains("-5", e.Message);
        // The runner refuses it the same way before anything runs: exit 1, the error at its pointer in results.json.
        Assert.Single(ScenarioCheckCommand.DataErrors(s, baseline with { Level = s.Level }), x => x.Pointer == "/overrides/data/1/value");
        string folder = Folder("refused-run");
        string file = Path.Combine(folder, "refused.scenario.json");
        File.WriteAllText(file, Text("""{ "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/shareOfPeak", "value": -5 } ] }"""));
        Assert.Equal(1, Entry.Run(["--scenario", file, "--out", Path.Combine(folder, "out"), "--sandbox"]));
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "out", "results.json")))!;
        Assert.Equal("/overrides/data/0/value", doc["errors"]![0]!["pointer"]!.GetValue<string>());
    }

    [Fact]
    public void A_pointer_that_names_nothing_a_wrong_kind_and_an_unknown_asset_are_errors_with_suggestions()
    {
        var load = Read(Text("""{ "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/strikeStart/0/metres", "value": 0.2 }, { "asset": "combat/pace.json", "pointer": "/tempo/global", "value": "fast" }, { "asset": "combat/wepons.json", "pointer": "/activeWindow/shareOfPeak", "value": 0.5 }, { "asset": "combat/weapons.json", "pointer": "activeWindow", "value": 0.5 } ] }"""));
        var missing = Assert.Single(load.Errors, x => x.Pointer == "/overrides/data/0/pointer");
        Assert.Equal(ScenarioErrorCode.BadPath, missing.Code);
        Assert.Contains("/activeWindow/strikeStarts/0/metres", missing.Suggestions);
        Assert.Equal(ScenarioErrorCode.WrongType, Assert.Single(load.Errors, x => x.Pointer == "/overrides/data/1/value").Code);
        var asset = Assert.Single(load.Errors, x => x.Pointer == "/overrides/data/2/asset");
        Assert.Equal(ScenarioErrorCode.UnknownName, asset.Code);
        Assert.Contains("combat/weapons.json", asset.Suggestions);
        Assert.Equal(ScenarioErrorCode.BadPath, Assert.Single(load.Errors, x => x.Pointer == "/overrides/data/3/pointer").Code);
        // A clip rate naming no clip of its actor: at --check, at its own pointer, with the actor's clips.
        var s = Compile(Text("""{ "clipRate": [ { "actor": "Christof", "clip": "windup:rihgt", "rate": 1.2 } ] }"""));
        using var log = new HostLog(Path.Combine(Path.GetTempPath(), $"vitomir-override-check2-{Environment.ProcessId}.log"));
        var clip = Assert.Single(ScenarioCheckCommand.WorldErrors(s, Baseline(), log));
        Assert.Equal((ScenarioErrorCode.UnknownName, "/overrides/clipRate/0/clip"), (clip.Code, clip.Pointer));
        Assert.Contains("windup:right", clip.Suggestions);
    }

    [Fact]
    public void Two_scenarios_in_one_process_do_not_leak_overrides()
    {
        var baseline = Baseline();
        var with = Compile(Text("""{ "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/shareOfPeak", "value": 0.8 } ] }"""));
        var without = Compile(Text("{}"));
        var (a, _) = ScenarioRunner.Execute(ScenarioPlan.From(with), baseline, Folder("leak-a"), _ => { });
        var (b, _) = ScenarioRunner.Execute(ScenarioPlan.From(without), baseline, Folder("leak-b"), _ => { });
        Assert.NotEqual(a.Provenance.Modules.Single().Value.ConfigHash, b.Provenance.Modules.Single().Value.ConfigHash);
        Assert.Null(Assert.Single(b.Points).ConfigHashes);
        Assert.DoesNotContain(Assert.Single(b.Points).Setup!, n => n!["kind"]!.GetValue<string>() == "data");
        // The same game loaded again (as a later scenario's check would) reads the file as it is.
        using var session = Session.Create(ScenarioCheckCommand.OptionsFor(without, without.Points[0], 0, baseline));
        Assert.Equal(0.4f, session.World.Service<ContactConfig>().Window.ShareOfPeak, 4);
        Assert.Equal(Game().Stamps.Single().ConfigHash, b.Provenance.Modules.Single().Value.ConfigHash);
    }

    // ---- overrides.actorTempo and overrides.clipRate ----

    [Fact]
    public void ActorTempo_and_clipRate_scale_the_measured_wind_up()
    {
        var s = Compile(Text("""{ "clipRate": [ { "actor": "Christof", "clip": "windup:right", "rate": 1 } ] }""",
            """[ { "set": "overrides.clipRate.0.rate", "values": [1, 2] }, { "set": "/overrides/actorTempo/Christof", "values": [0.7, 1.4] } ]"""));
        Assert.Equal(4, s.Points.Count);
        string folder = Folder("tempo");
        var (results, exit) = ScenarioRunner.Execute(ScenarioPlan.From(s), Baseline(), folder, _ => { });
        Assert.Equal(0, exit);
        double Median(int p) => results.Points[p].Measures["windUpSeconds"].Median!.Value;
        // (rate 1, tempo 0.7) is the reference; doubling either halves the press-to-contact time, both quarter it (to a tick).
        double tick = 1.0 / 120;
        Assert.InRange(Median(1), Median(0) / 2 - 2 * tick, Median(0) / 2 + 2 * tick);   // tempo 1.4
        Assert.InRange(Median(2), Median(0) / 2 - 2 * tick, Median(0) / 2 + 2 * tick);   // rate 2
        Assert.InRange(Median(3), Median(0) / 4 - 2 * tick, Median(0) / 4 + 2 * tick);   // both
        // Both in actors.csv, per actor and run.
        var lines = File.ReadAllLines(Path.Combine(folder, "actors.csv"));
        var head = lines[0].Split(',');
        int tempo = Array.IndexOf(head, "tempo"), rates = Array.IndexOf(head, "clip_rates");
        Assert.True(tempo > 0 && rates > 0);
        var christof = lines.Skip(1).Select(l => l.Split(',')).Where(c => c[5] == "Christof").ToList();
        Assert.Equal(["0.7", "1.4", "0.7", "1.4"], christof.Select(c => c[tempo]));
        Assert.Equal(["windup:right=1", "windup:right=1", "windup:right=2", "windup:right=2"], christof.Select(c => c[rates]));
        var guard = lines.Skip(1).Select(l => l.Split(',')).First(c => c[5] == "Guard");
        Assert.Equal(("", ""), (guard[tempo], guard[rates]));
    }

    // ---- light ----

    [Fact]
    public void Light_reaches_perception_over_the_time_and_in_steps()
    {
        var baseline = Baseline();
        var flat = Compile(Text("{}", light: "0.45"));
        Assert.Equal("0.45", flat.Points[0].Light);
        using (var session = Session.Create(ScenarioCheckCommand.OptionsFor(flat, flat.Points[0], 0, baseline)))
            Assert.Equal(0.45f, session.World.Service<PerceptionConfig>().Light, 4);
        var steps = Compile(Text("{}", light: """[ { "atSeconds": 0, "level": 0.2 }, { "atSeconds": 1, "level": 0.9 } ]"""));
        using (var session = Session.Create(ScenarioCheckCommand.OptionsFor(steps, steps.Points[0], 0, baseline)))
        {
            var config = session.World.Service<PerceptionConfig>();
            Assert.Equal(0.2f, config.Light, 4);
            while (session.Tick < 60) session.Step(session.ScenarioFrame());
            Assert.Equal(0.2f, config.Light, 4);
            while (session.Tick < 125) session.Step(session.ScenarioFrame());
            Assert.Equal(0.9f, config.Light, 4);
        }
        // None given: the time's (night 0.3), as before.
        var none = Compile(Text("{}"));
        Assert.Null(none.Points[0].Light);
        using (var session = Session.Create(ScenarioCheckCommand.OptionsFor(none, none.Points[0], 0, baseline)))
        {
            Assert.Null(session.World.Service<PerceptionConfig>().LightLevel);
            Assert.Equal(0.3f, session.World.Service<PerceptionConfig>().Light, 4);
        }
        // A light that is no level is refused by the reader.
        Assert.Contains(Read(Text("{}", light: "1.5")).Errors, e => e.Pointer == "/light" && e.Code == ScenarioErrorCode.OutOfRange);
        Assert.Contains(Read(Text("{}", light: """[ { "atSeconds": 2, "level": 0.5 } ]""")).Errors, e => e.Pointer == "/light/0/atSeconds");
    }

    // ---- example 5 ----

    [Fact]
    public void Example_5_checks_and_runs_with_its_overrides_in_results_and_actors_csv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        string path = Directory.GetFiles(Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples"), "*override*.scenario.json").Single();
        var load = ScenarioSpec.Load(path, Game(), Grounds().Course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        using var log = new HostLog(Path.Combine(Path.GetTempPath(), $"vitomir-override-ex5-{Environment.ProcessId}.log"));
        Assert.Empty(ScenarioCheckCommand.WorldErrors(load.Scenario!, Baseline(), log));
        string folder = Folder("ex5");
        var (results, exit) = ScenarioRunner.Execute(ScenarioPlan.From(load.Scenario!), Baseline(), folder, _ => { }, new ScenarioRunOptions { Runs = 1 });
        Assert.Contains(exit, new[] { 0, 4 });
        Assert.All(results.Points, p => Assert.Contains(p.Setup!, n => n!["kind"]!.GetValue<string>() == "data"));
        Assert.All(results.Points, p => Assert.Contains(p.Setup!, n => n!["kind"]!.GetValue<string>() == "clipRate"));
        var head = File.ReadLines(Path.Combine(folder, "actors.csv")).First().Split(',');
        Assert.Contains("clip_rates", head);
        Assert.Contains(File.ReadLines(Path.Combine(folder, "actors.csv")).Skip(1), l => l.Contains("windup:right=1.25", StringComparison.Ordinal));
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "results.json")))!;
        Assert.NotNull(doc["points"]![0]!["configHashes"]);
    }
}
