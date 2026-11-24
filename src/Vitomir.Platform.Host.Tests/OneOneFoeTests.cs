// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// 1.1's creatures as 1.1 has them, in play and in the arena: <c>--foe szlachta</c> moves at its 1.1 template's walk and
/// run and comes on with 1.1's approach and its AI file's numbers, all read from the install; the switch <c>foes-uniform</c> gives the earlier build's
/// szlachta (the human's movement, the duelling brain's approach, no approach's store); an arena fighter's movement and approach; and,
/// when asked (<c>VITOMIR_CADENCE_OUT</c>), the szlachta's blows in reach against a standing Christof written as a table (the gap from a
/// blow's end to the next one's start, at the global tempo, its kind's pace). Skips without the proving ground and the 1.1 install.
/// </summary>
public sealed class OneOneFoeTests
{
    static (string Course, string Game) Here()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-oneone-foe-tests-userdata"));
        return (course!, game!);
    }

    static HashSet<string> Switches(bool off) => off ? new(StringComparer.Ordinal) { "sandbox", Session.FoesUniformSwitch } : new(StringComparer.Ordinal) { "sandbox" };

    static OneOneCreature Szlachta()
    {
        using var install = GameInstall.TryOpen() ?? throw new Xunit.Sdk.XunitException("no install");
        return OneOneCreature.Read(new OneOneTemplates(install), install, "Szlachta") ?? throw new Xunit.Sdk.XunitException("Szlachta has no speeds");
    }

    [Fact]
    public void The_szlachta_preset_moves_at_its_1_1_speeds_and_comes_on_as_1_1_s_and_the_switch_gives_the_uniform_foes()
    {
        var (course, game) = Here();
        var creature = Szlachta();
        foreach (bool off in new[] { false, true })
        {
            var options = new SessionOptions
            {
                Level = course, Game = game, Switches = Switches(off), Seed = 5, People = 0, Place = "40,3,0,0", Foes = ["szlachta@42.5,3,0,180"],
            };
            using var session = Session.Create(options);
            var w = session.World;
            var foe = session.Foes[0];
            var loco = w.Get<Locomotion>(foe);
            var settings = w.Service<DuelBrainBook>().All[w.Get<DuelBrain>(foe).Book];
            if (!off)
            {
                Assert.Equal(creature.WalkMetresPerSecond, loco.WalkSpeed, 4);
                Assert.Equal(creature.RunMetresPerSecond, loco.RunSpeed, 4);
                Assert.Equal(creature.RunMetresPerSecond, loco.SprintSpeed, 4);   // it never runs faster than its run
                Assert.Equal(DuelApproach.OneOne, settings.Approach);
                Assert.Equal(creature.Ai!.MoveSeconds, settings.OneOneMoveSeconds, 4);
                Assert.Equal(creature.Ai.ChaseMetres, settings.OneOneStepMostMetres, 4);
                Assert.True(DuelOneOnes.Registered(w));
            }
            else
            {
                Assert.Equal(3.0f, loco.RunSpeed, 3);          // the human's
                Assert.Null(settings.Approach);
                Assert.False(DuelOneOnes.Registered(w));        // the world earlier
            }
            for (int t = 0; t < 3 * World.TicksPerSecond; t++) session.Step(ActionFrame.Idle);
            Assert.Equal(BrainKind.Duel, w.Get<DuelBrain>(foe).Kind);   // woken (2.5 m)
            if (!off) Assert.True(w.Store<DuelOneOne>().Has(foe));
        }
    }

    [Fact]
    public void An_arena_fighter_s_movement_and_approach_are_its_spec_s()
    {
        var (course, game) = Here();
        var spec = ArenaSpec.Read("""
            { "fighters": [
                { "name": "Christof", "template": "Christof", "place": [38, 3], "brain": "dummy", "movement": "elder" },
                { "name": "Szlachta", "template": "Szlachta", "sheet": "sandbox.szlachta", "place": [42, 3], "movement": "template",
                  "brain": { "kind": "duel", "approach": "oneone" } } ] }
            """, ".", course);
        var cast = spec.Cast([]);
        Assert.Equal("elder", cast.Fighters[0].Movement);
        Assert.Equal(DuelApproach.OneOne, cast.Fighters[1].Numbers!.Approach);
        Assert.True(ArenaCast.OneOneIn(cast.Write()));
        using var session = Session.Create(new SessionOptions { Level = course, Game = game, Switches = Switches(false), Seed = 3, Arena = cast.Write() });
        var w = session.World;
        Assert.Equal(Szlachta().RunMetresPerSecond, w.Get<Locomotion>(session.Fighters[1]).RunSpeed, 4);
        var profiles = w.Service<Vitomir.Platform.Modules.MovementProfiles>();
        Assert.Equal(profiles.IndexOf("elder"), w.Get<MasqueradeState>(session.Fighters[0]).Profile);   // the elder's (held to the human's in the masquerade)
        Assert.Equal(profiles.IndexOf("oneone:movement/szlachta"), w.Get<MasqueradeState>(session.Fighters[1]).Profile);
        Assert.True(DuelOneOnes.Registered(w));
        // A name that is no movement stops the bout by name.
        var bad = cast with { Fighters = [cast.Fighters[0] with { Movement = "giant" }, cast.Fighters[1]] };
        var e = Assert.Throws<SessionException>(() => Session.Create(new SessionOptions { Level = course, Game = game, Switches = Switches(false), Seed = 3, Arena = bad.Write() }));
        Assert.Contains("no movement \"giant\"", e.Message);
    }

    /// <summary>
    /// A scenario that sets both (the repository's example 9): it checks, its cast gives the szlachta its template's movement and 1.1's
    /// approach (and the point swept with <c>foes-uniform</c> the same cast, the switch passing them over); one run a point: 1.1's szlachta
    /// strikes later than the earlier build's. Words that are no movement or approach are refused at their JSON path with the nearest.
    /// </summary>
    [Fact]
    public void A_scenario_sets_an_actor_s_movement_and_approach_example_5()
    {
        var (course, game) = Here();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        string path = Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples", "09-szlachta-closes-as-1-1.scenario.json");
        var baseline = new SessionOptions { Level = course, Game = game, Switches = Switches(false) };
        using var scenarioGame = ScenarioGame.Load(baseline, course);   // the game's presets, characters and switches (as --scenario reads them)
        var load = ScenarioSpec.Load(path, scenarioGame, course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!);
        Assert.Equal(2, plan.Points.Count);
        var szlachta = plan.Points[0].Cast.Fighters[1];
        Assert.Equal("Szlachta", szlachta.Template);   // the preset's
        Assert.Equal(Session.TemplateMovement, szlachta.Movement);
        Assert.Equal(DuelApproach.OneOne, szlachta.Numbers!.Approach);
        string out_ = Path.Combine(Path.GetTempPath(), $"vitomir-cj-foes-example5-{Environment.ProcessId}");
        if (Directory.Exists(out_)) Directory.Delete(out_, recursive: true);
        var (results, exit) = ScenarioRunner.Execute(plan, baseline, out_, _ => { }, new ScenarioRunOptions { Runs = 1 });
        Assert.True(exit == 0, $"exit {exit}: " + string.Join("; ", results.Assertions.Select(a => $"{a.Name} {a.Result}"))
            + " | " + string.Join("; ", results.Points.Select(p => $"{p.Index}: first blow {p.Measures["firstBlowSeconds"].Mean}")));
        double First(int point) => results.Points[point].Measures["firstBlowSeconds"].Mean ?? double.NaN;
        Assert.True(First(0) > First(1) + 2, $"1.1's szlachta struck at {First(0):0.00} s, the uniform foe's at {First(1):0.00} s");

        // Wrong words: at their JSON paths, with the nearest.
        string bad = Path.Combine(out_, "bad.scenario.json");
        File.WriteAllText(bad, File.ReadAllText(path).Replace("\"movement\": \"template\"", "\"movement\": \"templat\"").Replace("\"approach\": \"oneone\"", "\"approach\": \"one-one\""));
        var refused = ScenarioSpec.Load(bad, scenarioGame, course);
        Assert.False(refused.Ok);
        var lines = refused.Errors.Select(e => e.Line()).ToList();
        Assert.Contains(lines, l => l.Contains("/actors/1/movement") && l.Contains("template"));
        Assert.Contains(lines, l => l.Contains("/actors/1/control/ai/approach") && l.Contains("oneone"));
        Directory.Delete(out_, recursive: true);
    }

    /// <summary>The szlachta's blows in reach against a standing Christof (1000 Health), as a table, when <c>VITOMIR_CADENCE_OUT</c> is set.</summary>
    [Fact]
    public void The_szlachta_s_blows_in_reach_are_written_for_measuring_when_asked()
    {
        string? output = Environment.GetEnvironmentVariable("VITOMIR_CADENCE_OUT");
        Assert.SkipWhen(output is null, "set VITOMIR_CADENCE_OUT to a file to write the szlachta's blows in reach");
        var (course, game) = Here();
        bool off = Environment.GetEnvironmentVariable("VITOMIR_CADENCE_OFF") == "1";
        int bouts = int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_CADENCE_BOUTS"), out int nb) ? nb : 10;
        var cast = new ArenaCast
        {
            Fighters =
            [
                new CastFighter { Name = "Christof", Template = "Christof", Weapon = "BroadSword", X = 38.5f, Y = 3f, Z = 0f, Facing = 0f, Brain = CastBrain.Stand, Foe = 1,
                    Sheet = new CastSheet { Template = "sandbox.player", Values = [new CastValue("health_pool", 1000)] } },
                new CastFighter { Name = "Szlachta", Template = "Szlachta", X = 39.6f, Y = 3f, Z = 0f, Facing = 180f, Foe = 0, Movement = "template",
                    Numbers = new DuelBrainSettings { Approach = DuelApproach.OneOne }, Sheet = new CastSheet { Template = "sandbox.szlachta" } },
            ],
        };
        var text = new StringBuilder("bout,blow,begin_s,end_s,next_begin_s,gap_s,paused_after\n");
        for (int bout = 0; bout < bouts; bout++)
        {
            using var session = Session.Create(new SessionOptions { Level = course, Game = game, Switches = Switches(off), Seed = (ulong)(4000 + bout), Arena = cast.Write() });
            var w = session.World;
            var s = session.Fighters[1];
            var blows = new List<(float Begin, float End, bool Paused)>();
            bool wasBusy = false;
            float begin = -1f;
            for (int t = 0; t < 60 * World.TicksPerSecond; t++)
            {
                session.Step(ActionFrame.Idle);
                float now = session.Tick * World.TickSeconds;
                foreach (var a in w.Events<AttackBegun>().ThisTick) if (a.Entity == s) begin = now;
                var c = w.Get<Combatant>(s);
                bool busy = c.Busy > 0f || c.Pending != AttackStrength.None || c.Stun > 0f;
                if (wasBusy && !busy && begin >= 0f) { blows.Add((begin, now, w.Get<DuelBrain>(s).PauseLeft > 0f)); begin = -1f; }
                wasBusy = busy;
            }
            for (int i = 0; i + 1 < blows.Count; i++)
                text.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{bout},{i},{blows[i].Begin:0.###},{blows[i].End:0.###},{blows[i + 1].Begin:0.###},{blows[i + 1].Begin - blows[i].End:0.###},{(blows[i].Paused ? 1 : 0)}\n"));
        }
        File.WriteAllText(output!, text.ToString());
    }
}
