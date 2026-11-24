// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The scenario file's reader: the three examples read and compile; every default made
/// explicit in the resolved form; places relative and absolute, facings toward; the sweep's grid and zip over any settable path; the
/// compiled cast, the director's program and the measures model; the execution modes and setup conditions; and every error code,
/// each at its JSON pointer, with the nearest names where a name was misspelt. Needs no game files (a game's names made by hand),
/// but the world-built check (Refused), which needs the 1.1 install and the proving ground.
/// </summary>
public sealed class ScenarioSpecTests
{
    /// <summary>A game's names, by hand (the enhanced module's, a few of each).</summary>
    static readonly ScenarioGame Game = new()
    {
        Characters = ["Christof", "rudolfGuard", "Szlachta"],
        Weapons = ["BroadSword", "Rapier", "weapRapier_shield", "war_bow"],   // the module's bow (combat/ranged's weapons), as the game lists it
        Presets = new Dictionary<string, FoePreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["guard"] = new FoePreset("guard", "Guard", "rudolfGuard", "weapRapier_shield", true, "sandbox.guard_trained"),
        },
        SheetTemplates = ["sandbox.townsperson", "sandbox.guard_trained", "sandbox.newly_embraced", "sandbox.human_reference"],
        Traits =
        [
            new GameTrait("strength", "attribute", "physical", 0, 1000, "attributePoints", "SANDBOX", "test"),
            new GameTrait("dexterity", "attribute", "physical", 0, 1000, "attributePoints", "SANDBOX", "test"),
            new GameTrait("melee", "ability", "Skills", 0, 10, "abilityDots", "SANDBOX", "test"),
            new GameTrait("dodge", "ability", "Skills", 0, 10, "abilityDots", "SANDBOX", "test"),
            new GameTrait("firearms", "ability", "Skills", 0, 10, "abilityDots", "SANDBOX", "test"),
            new GameTrait("blood_pool", "purchase", "resource", 0, 1_000_000, "bloodPoints", "SANDBOX", "test"),
            new GameTrait("health_pool", "purchase", "resource", 0, 1_000_000, "healthPoints", "SANDBOX", "test"),
        ],
        Powers = new Dictionary<string, GamePower>(StringComparer.Ordinal)
        {
            ["celerity"] = new GamePower("celerity", "Celerity", "cast", ["celerity.active"], [(1, 30, 15)], "SANDBOX"),
            ["potence"] = new GamePower("potence", "Potence", "passive", ["potence.passive"], [], "SANDBOX"),
        },
        Armor = ["mail_shirt", "light_helm"],
        Switches = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["sandbox"] = ("the sandbox", "enhanced"),
            ["skill-contest"] = ("the contest", "enhanced"),
            ["guard-skill-off"] = ("the guard by skill off", "enhanced"),
            ["shield-at-bow-off"] = ("no shield raised against a drawn bow", "enhanced"),   // example 1 sweeps it
        },
        // Two of the module's data assets, for overrides.data
        DataAssets =
        [
            new GameDataAsset(Vitomir.Platform.Modules.AssetId.Of("enhanced", "combat/foes"), "combat/foes.json", "VITOMIR_ENHANCED_FOES", "SANDBOX", true,
                System.Text.Encoding.UTF8.GetBytes("""{ "schema": "VITOMIR_ENHANCED_FOES", "status": "SANDBOX", "presets": { "guard": { "template": "rudolfGuard" } } }""")),
            new GameDataAsset(Vitomir.Platform.Modules.AssetId.Of("enhanced", "combat/weapons"), "combat/weapons.json", "VITOMIR_COMBAT_MATTER", "SANDBOX", true,
                System.Text.Encoding.UTF8.GetBytes("""{ "schema": "VITOMIR_COMBAT_MATTER", "status": "SANDBOX", "activeWindow": { "shareOfPeak": 0.4 } }""")),
        ],
    };

    static ScenarioLoad Read(string json, ScenarioGame? game = null, bool environment = false) =>
        ScenarioSpec.Read(json, Path.GetTempPath(), game ?? Game, ArenaSpecTests.FakeLevel.Value, levelFromEnvironment: environment);

    /// <summary>A valid scenario with <paramref name="actors"/> (default two) and the top-level fields <paramref name="extra"/>.</summary>
    static string Doc(string extra = "", string? actors = null) => $$"""
        { "format": "vitomir-scenario/1",
          "actors": {{actors ?? """[ { "name": "A", "place": [38, 3] }, { "name": "B", "template": "rudolfGuard", "place": [41, 3] } ]"""}}
          {{(extra.Length == 0 ? "" : "," + extra)}} }
        """;

    static ScenarioError Has(ScenarioLoad load, ScenarioErrorCode code, string pointer)
    {
        var hit = load.Errors.FirstOrDefault(e => e.Code == code && e.Pointer == pointer);
        Assert.True(hit is not null, $"expected {code} at {pointer}; got: {string.Join(" | ", load.Errors.Select(e => e.Line()))}");
        Assert.False(load.Ok);
        return hit!;
    }

    static CompiledScenario Valid(string json, ScenarioGame? game = null)
    {
        var load = Read(json, game);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        return load.Scenario!;
    }

    static string Examples()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "platform", "scenarios", "examples"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/scenarios/examples is not above the tests");
        return Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples");
    }

    // ---------------------------------------------------------------- the examples

    [Fact]
    public void Example_2_reads_and_compiles_six_points_of_a_scripted_attacker_and_a_muted_guard()
    {
        var load = ScenarioSpec.Load(Path.Combine(Examples(), "02-sword-scripted-blows-vs-guard-by-melee.scenario.json"), Game, ArenaSpecTests.FakeLevel.Value);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var s = load.Scenario!;
        Assert.Equal((6, 200, 3000UL), (s.Points.Count, s.Runs, s.Seed));
        Assert.Equal(ExecutionModes.Hybrid, s.ExecutionMode);
        Assert.True(s.Controlled);
        var p = s.Points[3];
        Assert.Equal("Christof.sheet.values.melee = 3", p.Name);
        Assert.Equal([ExecutionModes.ControlledScript, ExecutionModes.Hybrid], p.ActorModes);
        var (c, g) = (p.Cast.Fighters[0], p.Cast.Fighters[1]);
        Assert.Equal((38f, 3f, 0.02f, 0f), (c.X, c.Y, c.Z, c.Facing));
        Assert.Equal((41f, 3f, 180f), (g.X, g.Y, g.Facing));      // 3 m along +X from Christof, facing him
        Assert.Equal(CastBrain.Stand, c.Brain);                    // a script with no AI: the director holds what it needs
        Assert.Equal(CastBrain.Duel, g.Brain);
        Assert.Equal(new CastValue("melee", 3), c.Sheet.Values[^1]);
        Assert.Equal(0f, g.Numbers!.PaceMostSeconds);              // "pace": "none"
        var program = p.Program!;
        Assert.Equal(ControlChannels.Attack | ControlChannels.Move, program.Actors[1].Mute);
        Assert.Equal(ScenarioThen.Stand, program.Actors[0].Then);
        var track = Assert.Single(program.Actors[0].Tracks);
        var move = Assert.IsType<MoveToStep>(track[0]);
        Assert.Equal(("Guard", 1.3f, 0.1f, 6f, "close in"), (move.To.Actor, move.To.DistanceMetres, move.WithinMetres, move.ForSeconds, move.Label));
        var repeat = Assert.IsType<RepeatStep>(track[2]);
        Assert.Equal(6, repeat.Count);
        var attack = Assert.IsType<AttackStep>(repeat.Steps[4]);
        Assert.Equal((AttackDirection.Overhead, 0.4f, AimRegion.Head), (attack.Direction, attack.HoldSeconds, attack.AimAt!.Region));
        var left = Assert.IsType<AttackStep>(repeat.Steps[2]);   // the second blow of a round: from the left at the upper arm, no side named
        Assert.Equal((AttackDirection.Left, AimRegion.UpperArm, (StepSide?)null), (left.Direction, left.AimAt!.Region, left.AimAt.Side));
        Assert.Equal(7, s.Measures.Count);
        Assert.IsType<DelayForm>(s.Measures[6].Form);
        Assert.Equal(AssertionScope.Trend, s.Assertions[3].Scope);
        Assert.Equal("Christof.sheet.values.melee", s.Assertions[3].Over);
        Assert.Contains(p.Setup, n => (string)n!["kind"]! == "trait" && (string)n["id"]! == "melee" && (string)n["source"]! == "sweep");
    }

    [Fact]
    public void Example_3_reads_with_a_power_swept_by_rank_and_windows_by_marker()
    {
        var load = ScenarioSpec.Load(Path.Combine(Examples(), "03-celerity-at-a-set-time.scenario.json"), Game, ArenaSpecTests.FakeLevel.Value);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var s = load.Scenario!;
        Assert.Equal(6, s.Points.Count);
        var p0 = s.Points[0].Cast.Fighters[0];
        var v0 = p0.Sheet.Values[^1];
        Assert.Equal(("celerity", 0, true), (v0.Id, v0.Value, v0.Power));
        Assert.Equal(["celerity.active"], v0.Options!);
        Assert.Equal(100, p0.Blood);
        var use = s.Points[2].Program!.Actors[0].Tracks[0].OfType<UsePowerStep>().Single();
        Assert.Equal(("celerity", 8f, "celerity"), (use.Power, use.AtSeconds, use.Label));
        Assert.Equal(ScenarioThen.End, s.Points[0].Program!.Actors[0].Then);
        var rate = Assert.IsType<RateForm>(s.Measures[2].Form);
        Assert.True(rate.PerSecond);
        Assert.Equal(("before", "beforeEnd"), (rate.Of.FromMarker, rate.Of.ToMarker));   // the before window ends with its blows
        var wounds = Assert.IsType<CountForm>(s.Measures[5].Form);
        Assert.Equal("Wounds", wounds.SumField);   // "sum": "wounds", in the record's own spelling
        var diff = Assert.IsType<DifferenceForm>(s.Assertions[4].Value);
        Assert.Equal(new MeasureRefForm("blowsPerSecondAfter"), diff.Left);
        Assert.Equal(["1", "2", "3", "4", "5"], s.Assertions[4].Points![0].Values);
    }

    [Fact]
    public void Example_1_is_refused_only_for_the_bow_until_the_bow_is_built_and_reads_once_it_is()
    {
        if (ScenarioGame.BowBuilt)
        {
            var withBow = new ScenarioGame
            {
                Characters = Game.Characters, Weapons = [.. Game.Weapons, "war_bow"], Presets = Game.Presets, SheetTemplates = Game.SheetTemplates,
                Traits = Game.Traits, Powers = Game.Powers, Armor = Game.Armor, Switches = Game.Switches,
            };
            var ok = ScenarioSpec.Load(Path.Combine(Examples(), "01-bow-human-christof-vs-guard.scenario.json"), withBow, ArenaSpecTests.FakeLevel.Value);
            Assert.True(ok.Ok, string.Join("\n", ok.Errors.Select(e => e.Line())));
            Assert.Equal(6, ok.Scenario!.Points.Count);   // three distances by the guard's answer to the bow on and off
            Assert.Equal(["/outputs/film: this build's runner does not film a scenario yet; the arena's --film-at does"], ok.Warnings);
            // The bow is built — example 1 reads with no error, its archer carrying the war bow and six arrows.
            var built = ScenarioSpec.Load(Path.Combine(Examples(), "01-bow-human-christof-vs-guard.scenario.json"), Game, ArenaSpecTests.FakeLevel.Value);
            Assert.True(built.Ok, string.Join("\n", built.Errors.Select(e => e.Line())));
            Assert.All(built.Scenario!.Points, p => Assert.Equal(("war_bow", 6), (p.Cast.Fighters[0].Ranged, p.Cast.Fighters[0].Arrows)));
            return;
        }
        var load = ScenarioSpec.Load(Path.Combine(Examples(), "01-bow-human-christof-vs-guard.scenario.json"), Game, ArenaSpecTests.FakeLevel.Value);
        Assert.NotEmpty(load.Errors);
        Assert.All(load.Errors, e => Assert.Equal(ScenarioErrorCode.NotBuilt, e.Code));
        Assert.Contains(load.Errors, e => e.Pointer == "/actors/0/gear/ranged");
        Assert.Contains(load.Warnings, w => w.StartsWith("/outputs/film", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the resolved form and the compiler

    [Fact]
    public void Every_default_is_made_explicit_in_the_resolved_form()
    {
        var s = Valid(Doc());
        var r = JsonNode.Parse(s.ResolvedJson)!;
        Assert.Equal(("fight", "free-yard", "night", 10L, 1L, "grid"), ((string)r["kind"]!, (string)r["ground"]!, (string)r["time"]!, (long)r["runs"]!, (long)r["seed"]!, (string)r["sweepMode"]!));
        var a = r["actors"]![0]!;
        Assert.Equal(("Christof", "BroadSword", "full", "full", "B"), ((string)a["template"]!, (string)a["gear"]!["weapon"]!, (string)a["health"]!, (string)a["blood"]!, (string)a["foe"]!));
        Assert.Equal("stand", (string)a["control"]!["ai"]!["kind"]!);
        Assert.Equal("ai", (string)a["control"]!["then"]!);
        Assert.Equal(30.0, (double)r["stop"]!["afterSeconds"]!);
        Assert.Equal(("first", "SAMPLED", 2L), ((string)r["outputs"]!["events"]!, (string)r["outputs"]!["tracePolicy"]!, (long)r["outputs"]!["traceRuns"]!));
        Assert.Equal(ExecutionModes.NativeAi, (string)r["executionMode"]!);
        Assert.Null(s.Points[0].Program);   // nothing scripted, muted or triggered: no director in the world
    }

    [Fact]
    public void An_arena_spec_s_scenario_makes_the_same_cast_text_so_its_runs_are_the_arena_s_bouts()
    {
        var arena = ArenaSpec.Read("""
            { "fighters": [
                { "name": "Christof", "template": "Christof", "weapon": "Rapier", "shield": false,
                  "sheet": { "template": "sandbox.newly_embraced", "values": { "dodge": 3 } }, "place": [38, 3], "facing": 0, "brain": "duel" },
                { "name": "Guard", "template": "rudolfGuard", "weapon": "Rapier", "shield": false, "sheet": "sandbox.guard_trained",
                  "place": [42, 3], "facing": 180, "brain": { "kind": "duel", "blockChance": 0.8 } } ],
              "sweep": { "fighter": "Christof", "trait": "melee", "values": [0, 3] }, "bouts": 200, "seed": 3000, "seconds": 60 }
            """, Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var scenario = Valid(Doc("""
            "runs": 200, "seed": 3000, "stop": { "afterSeconds": 60 }, "sweep": [ { "set": "Christof.sheet.values.melee", "values": [0, 3] } ]
            """, """
            [ { "name": "Christof", "template": "Christof", "gear": { "weapon": "Rapier", "shield": false },
                "sheet": { "template": "sandbox.newly_embraced", "values": { "dodge": 3 } }, "place": [38, 3], "facing": 0, "control": { "ai": "duel" } },
              { "name": "Guard", "template": "rudolfGuard", "weapon": "Rapier", "shield": false, "sheet": "sandbox.guard_trained",
                "place": [42, 3], "facing": 180, "control": { "ai": { "kind": "duel", "blockChance": 0.8 } } } ]
            """));
        Assert.Equal(arena.Points.Count, scenario.Points.Count);
        for (int p = 0; p < arena.Points.Count; p++) Assert.Equal(arena.Cast(arena.Points[p]).Write(), scenario.Points[p].Cast.Write());
        Assert.All(scenario.Points, p => Assert.Null(p.Program));   // and no director in its worlds
        // With no places at all, both stand round the free yard's middle facing each other.
        var bare = ArenaSpec.Read("""{ "fighters": [ { "name": "A" }, { "name": "B", "template": "rudolfGuard" } ] }""", Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var bareScenario = Valid(Doc(actors: """[ { "name": "A", "control": { "ai": "duel" } }, { "name": "B", "template": "rudolfGuard", "control": { "ai": "duel" } } ]"""));
        Assert.Equal(bare.Cast([]).Write(), bareScenario.Points[0].Cast.Write());
    }

    [Fact]
    public void A_duelling_brain_lists_all_36_numbers_and_takes_the_arena_s_names()
    {
        var s = Valid(Doc(actors: """[ { "name": "A", "place": [38, 3], "control": { "ai": { "kind": "duel", "blockChance": 1, "dodgeHoldOff": false } } }, { "name": "B", "place": [41, 3] } ]"""));
        var ai = JsonNode.Parse(s.ResolvedJson)!["actors"]![0]!["control"]!["ai"]!.AsObject();
        Assert.Equal(38, ai.Count);   // kind and 36 numbers; and its approach
        Assert.Equal(BrainNumbers.Names, ai.Select(kv => kv.Key).Skip(1).Take(36));
        Assert.Equal("duel", ai["approach"]!.GetValue<string>());   // the duelling brain's own unless a preset or the file says oneone
        var n = s.Points[0].Cast.Fighters[0].Numbers!;
        Assert.Equal((1f, false), (n.BlockChance, n.DodgeHoldOff));
    }

    [Fact]
    public void The_shared_brain_table_holds_the_arena_s_36_numbers_in_its_order()
    {
        Assert.Equal(36, BrainNumbers.All.Count);
        Assert.Equal(["strongShare", "highShare", "levelShare", "lowShare", "highDegrees", "lowDegrees", "blockChance", "reactSeconds", "blockSeconds",
            "backOffChance", "backOffSeconds", "strikeShare", "closeShare", "tooCloseShare", "walkWithin", "powerShare", "renewSeconds", "powerWaitSeconds",
            "paceWithPowers", "healBelow", "dodgeChance", "dodgeBackShare", "dodgeSideShare", "dodgeWaitSeconds", "dodgeHoldOff", "dodgeAtStrike",
            "parryChance", "parryLeadSeconds", "leftShare", "rightShare", "overheadShare", "thrustShare", "awayFromGuardShare", "heldParryGiveUpSeconds",
            "pace", "paceRun"], BrainNumbers.Names);
        var d = new Combat.DuelBrainSettings();
        foreach (var n in BrainNumbers.All.Where(n => n.Kind == BrainNumberKind.Number)) Assert.Equal(n.Get(d), n.Get(n.Set(d, n.Get(d))));
    }

    [Fact]
    public void A_preset_expands_and_fields_beside_it_win()
    {
        var s = Valid(Doc(actors: """[ { "name": "A", "place": [38, 3] }, { "name": "G", "preset": "guard", "place": [41, 3], "sheet": { "values": { "melee": 4 } } } ]"""));
        var g = s.Points[0].Cast.Fighters[1];
        Assert.Equal(("rudolfGuard", "weapRapier_shield", true, "sandbox.guard_trained"), (g.Template, g.Weapon, g.Shield, g.Sheet.Template));
        Assert.Equal(new CastValue("melee", 4), Assert.Single(g.Sheet.Values));
        Assert.Contains(s.Points[0].Setup, n => (string)n!["kind"]! == "sheetTemplate" && (string)n["source"]! == "preset");
    }

    [Fact]
    public void Relative_places_chain_and_facings_turn_toward_an_actor_or_a_point()
    {
        var s = Valid(Doc(actors: """
            [ { "name": "A", "place": [30, 9], "facing": { "toward": [30, 10] } },
              { "name": "B", "place": { "from": "A", "distanceMetres": 10, "bearingDegrees": 0 } },
              { "name": "C", "place": { "from": "B", "distanceMetres": 2, "bearingDegrees": 90 }, "facing": 45, "foe": "A" } ]
            """));
        var f = s.Points[0].Cast.Fighters;
        Assert.Equal((30f, 9f, 90f), (f[0].X, f[0].Y, f[0].Facing));
        Assert.Equal((40f, 9f, 90f), (f[1].X, f[1].Y, f[1].Facing));    // toward its foe, the next actor: C, 2 m up +Y
        Assert.Equal((40f, 11f, 45f), (f[2].X, f[2].Y, f[2].Facing));
    }

    [Fact]
    public void A_grid_sweeps_every_combination_and_a_zip_walks_side_by_side_over_any_settable_paths()
    {
        const string actors = """[ { "name": "A", "place": [30, 9], "control": { "ai": "duel" } }, { "name": "B", "place": { "from": "A", "distanceMetres": 5 } } ]""";
        var grid = Valid(Doc("""
            "sweep": [ { "set": "B.place.distanceMetres", "values": [5, 10] }, { "set": "A.ai.blockChance", "from": 0, "to": 1, "step": 0.5 },
                       { "set": "overrides.switches.skill-contest", "values": [false, true] } ]
            """, actors));
        Assert.Equal(12, grid.Points.Count);
        var last = grid.Points[^1];
        Assert.Equal("B.place.distanceMetres = 10, A.ai.blockChance = 1, overrides.switches.skill-contest = true", last.Name);
        Assert.Equal(40f, last.Cast.Fighters[1].X);
        Assert.Equal(1f, last.Cast.Fighters[0].Numbers!.BlockChance);
        Assert.Equal(["skill-contest"], last.Switches);
        Assert.Empty(grid.Points[0].Switches);
        var zip = Valid(Doc("""
            "sweepMode": "zip", "sweep": [ { "set": "B.place.distanceMetres", "values": [5, 10, 15] }, { "set": "time", "values": ["night", "day", "night"] } ]
            """, actors));
        Assert.Equal(3, zip.Points.Count);
        Assert.Equal((45f, false), (zip.Points[2].Cast.Fighters[1].X, zip.Points[1].Night));
    }

    [Fact]
    public void Health_goes_into_the_cast_and_a_power_s_rank_sweeps_with_its_options()
    {
        var s = Valid(Doc("""
            "sweep": [ { "set": "A.sheet.powers.celerity.rank", "values": [0, 2] }, { "set": "B.health", "values": [50] } ]
            """, """[ { "name": "A", "place": [38, 3], "sheet": { "powers": { "celerity": { "rank": 1, "options": ["celerity.active"] } } } }, { "name": "B", "place": [41, 3] } ]"""));
        var rank = s.Points[1].Cast.Fighters[0].Sheet.Values[^1];
        Assert.Equal(("celerity", 2, true), (rank.Id, rank.Value, rank.Power));
        Assert.Equal(["celerity.active"], rank.Options!);
        Assert.Equal(50, s.Points[1].Cast.Fighters[1].Health);
        var cast = ArenaCast.Read(s.Points[1].Cast.Write());
        Assert.Equal(50, cast.Fighters[1].Health);
        Assert.Null(s.Points[1].Cast.Fighters[0].Health);
        Assert.DoesNotContain("\"Health\"", new ArenaCast { Fighters = [s.Points[1].Cast.Fighters[0], s.Points[1].Cast.Fighters[0]] }.Write());   // a cast without Health writes as before
    }

    [Fact]
    public void Triggers_compile_to_the_director_s_program_and_make_the_actor_hybrid()
    {
        var s = Valid(Doc("""
            "triggers": [ { "label": "dodge the overheads", "when": { "event": "AttackBegun", "by": "B" }, "do": [ { "actor": "A", "step": { "dodge": "back" } } ], "repeat": true, "cooldownSeconds": 1 },
                          { "label": "near", "when": { "value": "A.distanceTo.b", "atMost": 2 }, "do": [ { "end": "close" } ] } ]
            """, """[ { "name": "A", "place": [38, 3], "control": { "ai": "duel" } }, { "name": "B", "place": [41, 3], "control": { "ai": "duel" } } ]"""));
        var p = s.Points[0];
        Assert.Equal([ExecutionModes.Hybrid, ExecutionModes.NativeAi], p.ActorModes);
        var t = p.Program!.Triggers;
        Assert.Equal(("AttackBegun", "B"), (t[0].When.Event!.Event, t[0].When.Event!.By));
        Assert.Equal(DodgeWay.Back, Assert.IsType<DodgeStep>(t[0].Do[0].Step).Way);
        Assert.Equal(("A.distanceTo.B", CompareOp.AtMost, 2.0), (t[1].When.Value!.Observable, t[1].When.Value!.Compare.Op, t[1].When.Value!.Compare.Value));
        Assert.Equal("close", t[1].Do[0].End);
    }

    [Fact]
    public void A_player_input_compiles_to_the_director_s_input_and_its_actor_is_the_player()
    {
        var s = Valid(Doc(actors: """
            [ { "name": "A", "place": [38, 3], "control": { "playerInput": [
                  { "atSeconds": 0.5, "forSeconds": 1, "moveForward": 1 }, { "atSeconds": 2, "press": ["Dodge"], "label": "dodge" },
                  { "atSeconds": 0, "attackMode": "mouse" }, { "atSeconds": 3, "forSeconds": 0.3, "hold": ["attack"], "turnLeftDegrees": 30 } ] } },
              { "name": "B", "place": [41, 3], "control": { "ai": "duel" } } ]
            """));
        var p = s.Points[0];
        Assert.Equal([ExecutionModes.PlayerInputReplay, ExecutionModes.NativeAi], p.ActorModes);
        var input = p.Program!.Input!;
        Assert.Equal("A", input.Actor);
        Assert.Equal((0.5f, (float?)1f, (float?)1f), (input.Segments[0].AtSeconds, input.Segments[0].ForSeconds, input.Segments[0].MoveForward));
        Assert.Equal((Buttons.Dodge, (float?)null, "dodge"), (input.Segments[1].Press, input.Segments[1].ForSeconds, input.Segments[1].Label));
        Assert.Equal(AttackDirectionMode.Mouse, input.Segments[2].AttackMode);
        Assert.Equal((Buttons.Attack, 30f), (input.Segments[3].Hold, input.Segments[3].TurnLeftDegrees));
        Assert.DoesNotContain(p.Program.Actors, c => c.Actor == "A");   // the director refuses a control block on the player's body
        Assert.Equal(ExecutionModes.PlayerInputReplay, p.Program.AuthorityOf("A"));
    }

    [Fact]
    public void BadInput_for_a_second_player_or_an_input_actor_given_other_control()
    {
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "playerInput": [] } }, { "name": "B", "control": { "playerInput": [] } } ]""")), ScenarioErrorCode.BadInput, "/actors/1/control/playerInput");
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "ai": "duel", "playerInput": [] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadInput, "/actors/0/control/ai");
        Has(Read(Doc("""  "triggers": [ { "when": { "value": "time", "atLeast": 1 }, "do": [ { "actor": "A", "step": { "wait": 1 } } ] } ]  """,
            """[ { "name": "A", "control": { "playerInput": [ { "atSeconds": 1 } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadInput, "/triggers/0/do/0/actor");
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "playerInput": [ { "atSeconds": 1, "moveForward": 2 } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.OutOfRange, "/actors/0/control/playerInput/0/moveForward");
        Assert.Contains("dodge", Has(Read(Doc(actors: """[ { "name": "A", "control": { "playerInput": [ { "press": ["dodj"] } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/control/playerInput/0/press/0").Suggestions);
        // Swap shoulder is retired — named, it is refused with switchView suggested.
        var retired = Has(Read(Doc(actors: """[ { "name": "A", "control": { "playerInput": [ { "press": ["swapShoulder"] } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/control/playerInput/0/press/0");
        Assert.Equal(["switchView"], retired.Suggestions);
    }

    [Fact]
    public void Trace_policy_and_events_say_the_same_thing_and_the_stop_compiles()
    {
        var s = Valid(Doc("""
            "outputs": { "tracePolicy": "FAILURES", "traceBudgetMegabytes": 50 },
            "stop": { "afterSeconds": 12, "onHarnessStops": "never", "when": [ { "count": { "event": "Marker", "by": "A", "where": { "text": "done" } }, "atLeast": 1 } ] }
            """));
        Assert.Equal((TracePolicies.Failures, 50.0, TraceRuns.First), (s.TracePolicy, s.TraceBudgetMegabytes, s.Outputs.Events));
        Assert.Equal((12.0, HarnessStops.Never), (s.Stop.AfterSeconds, s.Stop.OnHarnessStops));
        var cond = Assert.Single(s.Stop.When!);
        Assert.Equal(("Marker", "Text"), (Assert.IsType<CountForm>(cond.Value).Of.Event, Assert.IsType<CountForm>(cond.Value).Of.Where![0].Field));
        Has(Read(Doc("""  "outputs": { "events": "none", "tracePolicy": "ALL" }  """)), ScenarioErrorCode.OutOfRange, "/outputs/tracePolicy");
    }

    [Fact]
    public void The_description_is_one_JSON_document_with_every_list()
    {
        var doc = ScenarioDescribe.Describe(Game, ScenarioDescribe.Lists);
        var text = doc.ToJsonString();
        using var parsed = JsonDocument.Parse(text);
        foreach (var list in ScenarioDescribe.Lists) Assert.True(parsed.RootElement.TryGetProperty(list, out _), list);
        Assert.Contains(doc["events"]!.AsArray(), e => (string)e!["event"]! == "ContactMade" && (string)e["by"]! == "Attacker" && (string)e["on"]! == "Target");
        Assert.Contains(doc["events"]!.AsArray(), e => (string)e!["event"]! == "StepBegan" && (string)e["by"]! == "Entity");
        Assert.Equal(36, doc["brain"]!.AsArray().Count);
        Assert.Contains(doc["properties"]!["setup"]!.AsArray(), p => (string)p!["id"]! == "actor.sheet.values.melee" && (string)p["unit"]! == "abilityDots");
        Assert.Contains(doc["coverage"]!.AsArray(), c => ((string)c!["status"]!).StartsWith("NOT_BUILT", StringComparison.Ordinal));
        Assert.Equal(Enum.GetValues<ScenarioErrorCode>().Length, doc["errors"]!.AsArray().Count);
    }

    [Fact]
    public void An_event_record_s_fields_are_described_from_the_record_itself()
    {
        foreach (var t in ScenarioWords.EventTypes)
        {
            var kind = ScenarioWords.Describe(t);
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            Assert.Equal(ctor.GetParameters().Select(p => p.Name), kind.Fields.Select(f => f.Name));
        }
    }

    [Fact]
    public void A_file_mangled_anywhere_is_refused_by_name_and_never_throws()
    {
        // Every example, each of its values replaced in turn by a value of another kind (or removed): the reader answers with
        // errors (or reads it), never with an exception — an author's tool can rely on --check's answer.
        var rng = new Random(14);
        JsonNode?[] odd = [null, 0, -1, 1e9, "x", true, new JsonArray(), new JsonObject(), new JsonArray(1, "y")];
        int tried = 0;
        foreach (var file in Directory.GetFiles(Examples(), "*.scenario.json"))
        {
            var original = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            var places = new List<(JsonNode Parent, object Key)>();
            void Walk(JsonNode? n)
            {
                if (n is JsonObject o) foreach (var (k, v) in o) { places.Add((o, k)); Walk(v); }
                else if (n is JsonArray a) for (int i = 0; i < a.Count; i++) { places.Add((a, i)); Walk(a[i]); }
            }
            Walk(original);
            for (int t = 0; t < 150; t++)
            {
                var doc = original.DeepClone();
                var mine = new List<(JsonNode Parent, object Key)>();
                void Walk2(JsonNode? n)
                {
                    if (n is JsonObject o) foreach (var (k, v) in o) { mine.Add((o, k)); Walk2(v); }
                    else if (n is JsonArray a) for (int i = 0; i < a.Count; i++) { mine.Add((a, i)); Walk2(a[i]); }
                }
                Walk2(doc);
                var (parent, key) = mine[rng.Next(mine.Count)];
                var with = odd[rng.Next(odd.Length + 1) % odd.Length]?.DeepClone();
                if (parent is JsonObject po && key is string name) { if (rng.Next(4) == 0) po.Remove(name); else po[name] = with; }
                else if (parent is JsonArray pa && key is int index) pa[index] = with;
                var load = ScenarioSpec.Read(doc.ToJsonString(), Path.GetDirectoryName(file)!, Game, ArenaSpecTests.FakeLevel.Value, levelFromEnvironment: false);
                Assert.True(load.Ok || load.Errors.Count > 0);
                Assert.All(load.Errors, e => Assert.False(string.IsNullOrEmpty(e.Message)));
                tried++;
            }
        }
        Assert.True(tried >= 450);
    }

    // ---------------------------------------------------------------- every error code

    [Fact]
    public void FileMissing_names_the_file() =>
        Assert.Equal(ScenarioErrorCode.FileMissing, Assert.Single(ScenarioSpec.Load("/nonexistent/x.scenario.json").Errors).Code);

    [Fact]
    public void NotJson_for_text_that_is_not_JSON() => Has(Read("{ \"format\": "), ScenarioErrorCode.NotJson, "");

    [Fact]
    public void UnknownFormat_without_the_format_or_with_another()
    {
        Has(Read("""{ "actors": [] }"""), ScenarioErrorCode.UnknownFormat, "/format");
        Has(Read("""{ "format": "vitomir-scenario/2" }"""), ScenarioErrorCode.UnknownFormat, "/format");
    }

    [Fact]
    public void UnknownField_suggests_the_nearest_name()
    {
        var e = Has(Read(Doc("\"seeds\": 4")), ScenarioErrorCode.UnknownField, "/seeds");
        Assert.Contains("seed", e.Suggestions);
        Assert.Contains("did you mean \"seed\"", e.Line());
    }

    [Fact]
    public void WrongType_at_the_field() => Has(Read(Doc("\"title\": 4")), ScenarioErrorCode.WrongType, "/title");

    [Fact]
    public void OutOfRange_says_the_range_and_what_was_given()
    {
        var e = Has(Read(Doc(actors: """[ { "name": "A", "place": [38, 3], "control": { "script": [ { "attack": "left", "holdSeconds": 12 } ] } }, { "name": "B", "place": [41, 3] } ]""")),
            ScenarioErrorCode.OutOfRange, "/actors/0/control/script/0/holdSeconds");
        Assert.Contains("0 to 10", e.Message);
        Assert.Contains("got 12", e.Message);
        Has(Read(Doc("\"time\": \"dusk\"")), ScenarioErrorCode.OutOfRange, "/time");
    }

    [Fact]
    public void NoLevel_without_a_level_for_the_free_yard() =>
        Has(ScenarioSpec.Read(Doc(), Path.GetTempPath(), Game, null, levelFromEnvironment: false), ScenarioErrorCode.NoLevel, "/ground");

    [Fact]
    public void LevelNotFound_for_a_folder_that_is_no_package() =>
        Has(Read(Doc("""  "ground": { "level": "/nonexistent/level.l2" }  """)), ScenarioErrorCode.LevelNotFound, "/ground/level");

    [Fact]
    public void TooFewActors_for_one() => Has(Read("""{ "format": "vitomir-scenario/1", "actors": [ { "name": "A" } ] }"""), ScenarioErrorCode.TooFewActors, "/actors");

    [Fact]
    public void BadName_for_a_name_with_a_dot_or_repeated()
    {
        Has(Read(Doc(actors: """[ { "name": "A.1" }, { "name": "B" } ]""")), ScenarioErrorCode.BadName, "/actors/0/name");
        Has(Read(Doc(actors: """[ { "name": "A" }, { "name": "a" } ]""")), ScenarioErrorCode.BadName, "/actors/1/name");
    }

    [Fact]
    public void BadPlace_for_a_place_that_is_not_one_or_a_circle_of_relative_places()
    {
        Has(Read(Doc(actors: """[ { "name": "A", "place": [38] }, { "name": "B" } ]""")), ScenarioErrorCode.BadPlace, "/actors/0/place");
        Has(Read(Doc(actors: """[ { "name": "A", "place": { "from": "B", "distanceMetres": 2 } }, { "name": "B", "place": { "from": "A", "distanceMetres": 2 } } ]""")),
            ScenarioErrorCode.BadPlace, "/actors/0/place/from");
    }

    [Fact]
    public void BadBrain_for_numbers_a_dummy_does_not_take_or_a_mute_without_an_AI()
    {
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "ai": { "kind": "stand", "blockChance": 1 } } }, { "name": "B" } ]""")), ScenarioErrorCode.BadBrain, "/actors/0/control/ai/blockChance");
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "script": [ { "wait": 1 } ], "mute": ["attack"] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadBrain, "/actors/0/control/mute");
    }

    [Fact]
    public void BadSheet_for_a_sheet_that_is_not_one() => Has(Read(Doc(actors: """[ { "name": "A", "sheet": 5 }, { "name": "B" } ]""")), ScenarioErrorCode.BadSheet, "/actors/0/sheet");

    [Fact]
    public void SheetFileMissing_for_a_file_not_there() =>
        Has(Read(Doc(actors: """[ { "name": "A", "sheet": { "file": "no-such-sheet.json" } }, { "name": "B" } ]""")), ScenarioErrorCode.SheetFileMissing, "/actors/0/sheet/file");

    [Fact]
    public void UnknownFoe_for_no_other_actor()
    {
        var e = Has(Read(Doc(actors: """[ { "name": "Christof", "foe": "Gaurd" }, { "name": "Guard" } ]""")), ScenarioErrorCode.UnknownFoe, "/actors/0/foe");
        Assert.Contains("Guard", e.Suggestions);
    }

    [Fact]
    public void BadSweep_for_a_zip_of_unequal_lengths_or_a_wrong_value()
    {
        Has(Read(Doc("""  "sweepMode": "zip", "sweep": [ { "set": "A.health", "values": [10, 20] }, { "set": "B.health", "values": [10] } ]  """)), ScenarioErrorCode.BadSweep, "/sweepMode");
        Has(Read(Doc("""  "sweep": [ { "set": "overrides.switches.skill-contest", "values": [1] } ]  """)), ScenarioErrorCode.BadSweep, "/sweep/0/values/0");
    }

    [Fact]
    public void BadPower_for_a_rank_with_no_options() =>
        Has(Read(Doc(actors: """[ { "name": "A", "sheet": { "powers": { "potence": { "rank": 2, "options": [] } } } }, { "name": "B" } ]""")), ScenarioErrorCode.BadPower, "/actors/0/sheet/powers/potence/options");

    [Fact]
    public void BadSlots_for_a_slot_that_is_not_one() =>
        Has(Read(Doc(actors: """[ { "name": "A", "slots": [9] }, { "name": "B" } ]""")), ScenarioErrorCode.BadSlots, "/actors/0/slots/0");

    [Fact]
    public void BadSide_for_sides_given_to_some_or_a_named_foe_on_its_own_side()
    {
        Has(Read(Doc(actors: """[ { "name": "A", "side": "x" }, { "name": "B" } ]""")), ScenarioErrorCode.BadSide, "/actors");
        Has(Read(Doc(actors: """[ { "name": "A", "side": "x", "foe": "C" }, { "name": "B", "side": "y" }, { "name": "C", "side": "x" } ]""")), ScenarioErrorCode.BadSide, "/actors/0/foe");
    }

    [Fact]
    public void UnknownName_for_a_weapon_trait_power_or_switch_the_game_has_not_with_the_nearest()
    {
        var e = Has(Read(Doc(actors: """[ { "name": "A", "gear": { "weapon": "BroadSwrod" } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/gear/weapon");
        Assert.Equal("BroadSword", e.Suggestions[0]);
        Assert.Contains("melee", Has(Read(Doc(actors: """[ { "name": "A", "sheet": { "values": { "mele": 2 } } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/sheet/values/mele").Suggestions);
        Has(Read(Doc("""  "overrides": { "switches": ["skill-contset"] }  """)), ScenarioErrorCode.UnknownName, "/overrides/switches/0");
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "script": [ { "usePower": "celerty" } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/control/script/0/usePower");
    }

    [Fact]
    public void BadStep_for_a_step_with_no_kind_or_two_or_a_wrong_value()
    {
        var e = Has(Read(Doc(actors: """[ { "name": "A", "control": { "script": [ { "atack": "left" } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadStep, "/actors/0/control/script/0");
        Assert.Contains("attack", e.Suggestions);
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "script": [ { "wait": 1, "mark": "x" } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadStep, "/actors/0/control/script/0");
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "script": [ { "strafe": "left" } ] } }, { "name": "B" } ]""")), ScenarioErrorCode.BadStep, "/actors/0/control/script/0/forSeconds");
    }

    [Fact]
    public void A_trigger_reads_only_what_the_director_reads_in_the_world()
    {
        var e = Has(Read(Doc("""  "triggers": [ { "when": { "value": "A.woundsTaken", "atLeast": 1 }, "do": [ { "end": "hurt" } ] } ]  """)), ScenarioErrorCode.NotBuilt, "/triggers/0/when/value");
        Assert.Contains("count condition", e.Message);
        Has(Read(Doc(actors: """[ { "name": "A", "sheet": { "values": { "archery": 2 } } }, { "name": "B" } ]""")), ScenarioErrorCode.UnknownName, "/actors/0/sheet/values/archery");
    }

    [Fact]
    public void BadSelector_for_a_field_the_event_has_not_or_a_value_it_cannot_take()
    {
        Has(Read(Doc("""  "measures": [ { "name": "m", "count": { "event": "ContactMade", "where": { "mett": "body" } } } ]  """)), ScenarioErrorCode.BadSelector, "/measures/0/count/where/mett");
        Has(Read(Doc("""  "measures": [ { "name": "m", "count": { "event": "ContactMade", "where": { "met": "bodyy" } } } ]  """)), ScenarioErrorCode.BadSelector, "/measures/0/count/where/met");
        Has(Read(Doc("""  "measures": [ { "name": "m", "count": { "by": "A" } } ]  """)), ScenarioErrorCode.BadSelector, "/measures/0/count/event");
    }

    [Fact]
    public void BadObservable_for_a_path_that_is_not_one()
    {
        var e = Has(Read(Doc("""  "stop": { "when": [ { "value": "B.helth", "atMost": 40 } ] }  """)), ScenarioErrorCode.BadObservable, "/stop/when/0/value");
        Assert.Contains("B.health", e.Suggestions);
    }

    [Fact]
    public void BadPath_for_a_sweep_s_set_or_a_trend_s_over_that_is_not_one()
    {
        Has(Read(Doc("""  "sweep": [ { "set": "A.sheet.valus.melee", "values": [1] } ]  """)), ScenarioErrorCode.BadPath, "/sweep/0/set");
        Has(Read(Doc("""  "sweep": [ { "set": "A.place.distanceMetres", "values": [1] } ]  """)), ScenarioErrorCode.BadPath, "/sweep/0/set");   // A's place is not relative
        Has(Read(Doc("""  "measures": [ { "name": "m", "value": "A.health" } ], "assertions": [ { "name": "t", "measure": "m", "scope": "trend", "over": "A.health", "direction": "rising" } ]  """)),
            ScenarioErrorCode.BadPath, "/assertions/0/over");
    }

    [Fact]
    public void ChannelClash_for_two_tracks_holding_one_channel_at_once() =>
        Has(Read(Doc(actors: """[ { "name": "A", "control": { "tracks": [ [ { "attack": "left" } ], [ { "attack": "right" } ] ] } }, { "name": "B" } ]""")),
            ScenarioErrorCode.ChannelClash, "/actors/0/control/tracks/1/0");

    [Fact]
    public void PlaceBlocked_for_a_place_outside_the_free_yard_or_on_another_actor()
    {
        var e = Has(Read(Doc(actors: """[ { "name": "A", "place": [10, 3] }, { "name": "B", "place": [41, 3] } ]""")), ScenarioErrorCode.PlaceBlocked, "/actors/0/place");
        Assert.Contains("outside the free yard", e.Message);
        Has(Read(Doc("""  "sweep": [ { "set": "B.place.distanceMetres", "values": [2, 0.2] } ]  """, """[ { "name": "A", "place": [30, 9] }, { "name": "B", "place": { "from": "A", "distanceMetres": 2 } } ]""")),
            ScenarioErrorCode.PlaceBlocked, "/actors/1/place");
    }

    /// <summary>An objective with no kind, and one that defeats itself, are BadObjective at their pointers (ScenarioGoalTests has the rest).</summary>
    [Fact]
    public void BadObjective_points_at_the_objective()
    {
        Has(Read(Doc("""  "objectives": [ { "actor": "A" } ]  """)), ScenarioErrorCode.BadObjective, "/objectives/0");
        Has(Read(Doc("""  "objectives": [ { "actor": "A", "defeat": "A" } ]  """)), ScenarioErrorCode.BadObjective, "/objectives/0/defeat");
        Assert.True(Read(Doc("""  "objectives": [ { "actor": "A", "defeat": "B", "bySeconds": 20 } ], "stop": { "onObjectives": "first" }  """)).Ok);
    }

    [Fact]
    public void NotBuilt_names_the_batch_that_brings_it()
    {
        // Light and overrides.data are built; the foes' presets cannot be overridden (they are read with the scenario).
        Assert.True(Read(Doc("""  "light": 0.3, "overrides": { "data": [ { "asset": "combat/weapons.json", "pointer": "/activeWindow/shareOfPeak", "value": 0.5 } ] }  """)).Ok);
        var e = Has(Read(Doc("""  "overrides": { "data": [ { "asset": "combat/foes.json", "pointer": "/presets/guard/template", "value": "Szlachta" } ] }  """)),
            ScenarioErrorCode.NotBuilt, "/overrides/data/0/asset");
        Assert.Contains("presets", e.Message);
        Has(Read(Doc("\"kind\": \"contact\"")), ScenarioErrorCode.BadContact, "/contacts");   // built; its contacts are required (FixedContactTests has the rest)
    }

    [Fact]
    public void BadTrigger_for_a_trigger_with_no_do() =>
        Has(Read(Doc("""  "triggers": [ { "when": { "event": "AttackBegun" } } ]  """)), ScenarioErrorCode.BadTrigger, "/triggers/0/do");

    [Fact]
    public void BadMeasure_for_a_repeated_name_or_no_value()
    {
        Has(Read(Doc("""  "measures": [ { "name": "m", "value": "A.health" }, { "name": "m", "value": "B.health" } ]  """)), ScenarioErrorCode.BadMeasure, "/measures/1/name");
        Has(Read(Doc("""  "measures": [ { "name": "m" } ]  """)), ScenarioErrorCode.BadMeasure, "/measures/0");
    }

    [Fact]
    public void BadAssertion_for_no_comparator_or_a_trend_without_its_direction()
    {
        Has(Read(Doc("""  "assertions": [ { "name": "a", "value": "A.health" } ]  """)), ScenarioErrorCode.BadAssertion, "/assertions/0");
        Has(Read(Doc("""  "sweep": [ { "set": "A.health", "values": [10, 20] } ], "assertions": [ { "name": "a", "value": "A.health", "scope": "trend", "over": "A.health" } ]  """)),
            ScenarioErrorCode.BadAssertion, "/assertions/0/direction");
    }

    [Fact]
    public void Refused_when_the_module_refuses_what_a_world_is_built_with()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var install = Vitomir.Platform.Assets.GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(install is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-scenario-tests-userdata"));
        // A melee of 99 passes the reader (a whole number) but not the module (an Ability is 0 to 10).
        var load = ScenarioSpec.Read(Doc(actors: """[ { "name": "A", "place": [38, 3], "sheet": { "template": "sandbox.townsperson", "values": { "melee": 99 } } }, { "name": "B", "template": "rudolfGuard", "place": [41, 3] } ]"""),
            Path.GetTempPath(), null, course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        using var log = new HostLog(Path.Combine(Path.GetTempPath(), $"vitomir-scenario-check-{Environment.ProcessId}.log"));
        var baseline = new SessionOptions { Level = course!, Game = install, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Say = _ => { } };
        var errors = ScenarioCheckCommand.WorldErrors(load.Scenario!, baseline, log);
        var e = Assert.Single(errors);
        Assert.Equal((ScenarioErrorCode.Refused, "/actors/0"), (e.Code, e.Pointer));
        Assert.Contains("melee", e.Message);
    }

    [Fact]
    public void Every_error_code_has_its_test_here()
    {
        var source = File.ReadAllText(Path.Combine(Examples(), "..", "..", "..", "..", "src", "Vitomir.Platform.Host.Tests", "ScenarioSpecTests.cs"));
        foreach (var code in Enum.GetValues<ScenarioErrorCode>())
            Assert.True(source.Contains($"ScenarioErrorCode.{code}", StringComparison.Ordinal), $"no test names ScenarioErrorCode.{code}");
    }
}
