// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Host.Scenario;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The bow in a scenario: the example (a human Christof with a war bow against the standard guard, 10, 20 and 30 m
/// apart on the proving ground's free yard) read, compiled with the bow in the cast and run as a session with
/// the scenario director — the bow's steps nock, draw, hold and loose arrows that fly and meet what they meet, the trigger at 4 m lets
/// the string down, puts the bow away and hands Christof to the duelling brain; the same run twice is the same run digest for digest;
/// an arena world (no bow in its cast) registers none of the bow. Skips without the proving ground and the 1.1 install.
/// </summary>
public sealed class BowScenarioTests(ITestOutputHelper output)
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-bow-tests-userdata"));
        return (course!, game!);
    }

    /// <summary>The design's example 1 (the design work/examples/01-bow-human-christof-vs-guard.scenario.json), its sheet's Archery as the module's ranged Skill (firearms, "Range").</summary>
    const string Example1 = """
    {
      "format": "vitomir-scenario/1",
      "title": "A human Christof with a bow against the standard guard, 10, 20 and 30 m apart on even ground",
      "ground": "free-yard", "time": "night",
      "actors": [
        { "name": "Christof", "template": "Christof",
          "sheet": { "template": "sandbox.human_reference", "values": { "melee": 2, "firearms": 2 } },
          "gear": { "weapon": "BroadSword", "shield": false, "ranged": { "weapon": "war_bow", "arrows": 6 } },
          "place": [21, 9], "facing": { "toward": "Guard" }, "foe": "Guard",
          "control": { "ai": "duel",
            "script": [ { "equip": "ranged" },
                        { "repeat": "untilStop", "steps": [
                            { "aim": { "at": "Guard", "region": "chest" } },
                            { "draw": { "toShare": 1.0 } },
                            { "wait": 0.4, "label": "settle" },
                            { "loose": true, "label": "loose" } ] } ],
            "then": "ai" } },
        { "name": "Guard", "preset": "guard",
          "place": { "from": "Christof", "distanceMetres": 20, "bearingDegrees": 0 },
          "facing": { "toward": "Christof" }, "foe": "Christof", "control": { "ai": "duel" } }
      ],
      "triggers": [
        { "label": "sword out at 4 m", "when": { "value": "Guard.distanceTo.Christof", "atMost": 4.0 },
          "do": [ { "actor": "Christof", "step": { "cancelDraw": true } }, { "actor": "Christof", "step": { "equip": "melee" } },
                  { "actor": "Christof", "step": { "handOver": "ai" } } ] },
        { "label": "out of arrows", "when": { "count": { "event": "ArrowLoosed", "by": "Christof" }, "atLeast": 6 },
          "do": [ { "actor": "Christof", "step": { "equip": "melee" } }, { "actor": "Christof", "step": { "handOver": "ai" } } ] }
      ],
      "stop": { "afterSeconds": 40, "onHarnessStops": "first" },
      "runs": 2, "seed": 3000,
      "sweep": [ { "set": "Guard.place.distanceMetres", "values": [10, 20, 30] } ]
    }
    """;

    static (CompiledScenario Scenario, SessionOptions Baseline) Load()
    {
        var (course, game) = Grounds();
        var baseline = new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        var g = ScenarioGame.Load(baseline, course);
        var load = ScenarioSpec.Read(Example1, Path.GetTempPath(), g, course, "01-bow-human-christof-vs-guard.scenario.json");
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        return (load.Scenario!, baseline);
    }

    static SessionOptions OptionsFor(CompiledScenario s, SessionOptions baseline, int point, int run) => baseline with
    {
        Switches = new HashSet<string>(baseline.Switches.Concat(s.Points[point].Switches), StringComparer.Ordinal),
        Level = s.Level, Start = "0", People = 0, Witnesses = [], Night = s.Points[point].Night, Place = null, TargetTest = null, NpcActions = null,
        Seed = s.Seed + (ulong)run,
        Arena = s.Points[point].Cast.Write(),
        Scenario = s.Points[point].Program?.Write(),
    };

    sealed class Seen
    {
        public readonly List<ArrowLoosed> Loosed = [];
        public readonly List<ArrowSpent> Spent = [];
        public readonly List<ContactMade> Arrows = [];
        public readonly List<HarmResolved> Harm = [];
        public readonly List<DrawCancelled> Cancelled = [];
        public readonly List<TriggerFired> Triggers = [];
        public readonly List<StepRefused> Refused = [];
        public readonly List<ulong> Digests = [];
        public readonly StringBuilder Log = new();
    }

    static Seen Run(SessionOptions options, float seconds)
    {
        var seen = new Seen();
        using var session = Session.Create(options);
        var w = session.World;
        var christof = session.Fighters[0];
        var guard = session.Fighters[1];
        var bodies = w.Store<Body>();
        long most = (long)(seconds * World.TicksPerSecond);
        while (session.Tick < most)
        {
            session.Step(ActionFrame.Idle);
            float d = Vector(bodies.Get(christof).Feet - bodies.Get(guard).Feet);
            foreach (var l in w.Events<ArrowLoosed>().ThisTick) { seen.Loosed.Add(l); seen.Log.AppendLine($"{l.Tick / 120.0,6:0.000} s  loosed #{l.Shot} at {l.DrawShare:0.00} draw, {l.SpeedMetresPerSecond:0.0} m/s, pitch {l.PitchDegrees:0.00}°, spread {l.SpreadDegrees:0.0}°; the guard {d:0.0} m off; {l.ArrowsLeft} left"); }
            foreach (var c in w.Events<ContactMade>().ThisTick) if (c.Shot > 0) { seen.Arrows.Add(c); seen.Log.AppendLine($"{c.Tick / 120.0,6:0.000} s  arrow #{c.Shot} met {c.Met} {c.Site} ({c.Material}) at {c.PointSpeed:0.0} m/s, impulse {c.NormalImpulse:0.00} N·s"); }
            foreach (var s in w.Events<ArrowSpent>().ThisTick) seen.Spent.Add(s);
            foreach (var h in w.Events<HarmResolved>().ThisTick) { seen.Harm.Add(h); seen.Log.AppendLine($"{h.Tick / 120.0,6:0.000} s  harm on {(h.Target == guard ? "the guard" : "Christof")} by {(h.Attacker == christof ? "Christof" : "the guard")}: {h.DamageDice} dice, {h.Wounds} wounds, Health {h.HealthBefore} -> {h.HealthAfter}"); }
            foreach (var c in w.Events<DrawCancelled>().ThisTick) { seen.Cancelled.Add(c); seen.Log.AppendLine($"{c.Tick / 120.0,6:0.000} s  draw let down ({c.Cause}) at {c.DrawShare:0.00}"); }
            foreach (var t in w.Events<TriggerFired>().ThisTick) { seen.Triggers.Add(t); seen.Log.AppendLine($"{t.Tick / 120.0,6:0.000} s  trigger \"{t.Label}\"; the guard {d:0.0} m off"); }
            foreach (var r in w.Events<StepRefused>().ThisTick) seen.Refused.Add(r);
            if (session.Tick % 120 == 0) seen.Digests.Add(session.Digest());
            if (w.Has<Dead>(christof) || w.Has<Dead>(guard)) { seen.Log.AppendLine($"{session.Tick / 120.0,6:0.000} s  {(w.Has<Dead>(guard) ? "the guard" : "Christof")} stopped"); break; }
        }
        seen.Digests.Add(session.Digest());
        return seen;
    }

    static float Vector(System.Numerics.Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    [Fact]
    public void The_bow_example_reads_with_the_bow_in_the_cast_and_its_steps()
    {
        var (s, _) = Load();
        Assert.Equal(3, s.Points.Count);
        foreach (var p in s.Points)
        {
            var c = p.Cast.Fighters[0];
            Assert.Equal(("war_bow", 6), (c.Ranged, c.Arrows));
            Assert.Null(p.Cast.Fighters[1].Ranged);
            Assert.True(ArenaCast.BowsIn(p.Cast.Write()));
            Assert.NotNull(p.Program);
        }
    }

    [Fact]
    public void At_20_metres_christof_looses_arrows_that_fly_and_meet_and_at_4_metres_puts_the_bow_away()
    {
        var (s, baseline) = Load();
        var seen = Run(OptionsFor(s, baseline, 1, 0), 30f);
        output.WriteLine(seen.Log.ToString());
        Assert.Empty(seen.Refused);
        Assert.NotEmpty(seen.Loosed);
        Assert.All(seen.Loosed, l => Assert.InRange(l.SpeedMetresPerSecond, 58.1f, 58.3f));   // full draw (the items set's war arrow, 58.2 m/s; the earlier build's 53.34 with arrows-heavy)
        // Every arrow loosed is spent once (met something, fell or timed out), and each one that met has its contact.
        Assert.Equal(seen.Loosed.Select(l => l.Shot).Order(), seen.Spent.Select(x => x.Shot).Order());
        foreach (var x in seen.Spent.Where(x => x.End == ArrowEnd.Met)) Assert.Single(seen.Arrows, c => c.ContactId == x.ContactId);
        Assert.Contains(seen.Triggers, t => t.Label == "sword out at 4 m");
    }

    [Fact]
    public void The_same_scenario_run_is_the_same_run_twice_digest_for_digest()
    {
        var (s, baseline) = Load();
        var a = Run(OptionsFor(s, baseline, 0, 1), 8f);
        var b = Run(OptionsFor(s, baseline, 0, 1), 8f);
        Assert.Equal(a.Digests, b.Digests);
        Assert.Equal(a.Arrows, b.Arrows);
        Assert.NotEmpty(a.Loosed);
    }

    [Fact]
    public void A_bow_scenario_replays_from_its_tapes_head_alone()
    {
        var (s, baseline) = Load();
        var options = OptionsFor(s, baseline, 2, 0);
        TapeHeader head;
        var digests = new Dictionary<long, ulong>();
        int loosed = 0;
        using (var session = Session.Create(options))
        {
            head = session.TapeHeader("test");
            for (int i = 0; i < 6 * World.TicksPerSecond; i++)
            {
                session.Step(ActionFrame.Idle);
                loosed += session.World.Events<ArrowLoosed>().ThisTick.Count;
                if (session.Tick % 60 == 0) digests[session.Tick] = session.Digest();
            }
        }
        Assert.True(loosed >= 2, $"{loosed} arrows loosed");
        var again = new SessionOptions { Level = "", Game = baseline.Game }.From(head);
        Assert.Equal(options.Arena, again.Arena);
        Assert.Equal(options.Scenario, again.Scenario);
        using var replay = Session.Create(again);
        Assert.True(replay.World.TryStore<Bow>(out _));
        for (int i = 0; i < 6 * World.TicksPerSecond; i++)
        {
            replay.Step(ActionFrame.Idle);
            if (digests.TryGetValue(replay.Tick, out ulong want)) Assert.Equal(want, replay.Digest());
        }
    }

    /// <summary>
    /// Pictures of example 1 at 20 m (run 0), from high over the yard's wall, the arrows drawn as white lines (<see cref="Arrow.Fill"/>;
    /// there is no bow model or draw pose): eight times into the folder <c>VITOMIR_BOW_PICTURES</c> names, for people to look
    /// at; without it, two into a folder of its own that is removed after (that pictures of a world with arrows are drawn at all).
    /// </summary>
    [Fact]
    public void Pictures_of_the_bow_example_are_drawn()
    {
        var asked = Environment.GetEnvironmentVariable("VITOMIR_BOW_PICTURES");
        var folder = string.IsNullOrEmpty(asked) ? Path.Combine(Path.GetTempPath(), $"vitomir-bow-pictures-{Environment.ProcessId}") : asked;
        float[] times = string.IsNullOrEmpty(asked) ? [2.15f, 4.20f] : [1.0f, 1.95f, 2.05f, 2.15f, 2.30f, 4.10f, 4.20f, 6.0f];
        Directory.CreateDirectory(folder);
        var (s, baseline) = Load();
        var options = OptionsFor(s, baseline, 1, 0);
        using var session = Session.Create(options);
        using var renderer = Vitomir.Platform.Render.PlatformRenderer.CreateHeadless(1280, 720,
            new Vitomir.Platform.Render.RendererOptions("", "", false, Look: Session.LookFor(options.Night)));
        renderer.CharacterTextures = session.Texture;
        renderer.CursorArt = session.CursorArt;
        renderer.LoadLevel(options.Level);
        session.PictureAspect = 16f / 9f;
        var w = session.World;
        var bodies = w.Store<Body>();
        var drawn = new List<string>();
        foreach (float t in times)
        {
            long until = (long)MathF.Round(t * World.TicksPerSecond);
            while (session.Tick < until) session.Step(ActionFrame.Idle);
            var view = new FrameView();
            session.Fill(view, 1f);
            var a = bodies.Get(session.Fighters[0]).Feet;
            var b = bodies.Get(session.Fighters[1]).Feet;
            var mid = (a + b) * 0.5f;
            // High and to the south, over the yard's 4.5 m wall (the clear line at y 9 runs between it and the house).
            var at = mid + new System.Numerics.Vector3(0f, 0f, 1.0f);
            var eye = new System.Numerics.Vector3(mid.X - 2f, mid.Y - 9f, mid.Z + 9f);
            var look = at - eye;
            view.View = new ViewPoint(eye, DetMath.Atan2(look.Y, look.X), DetMath.Atan2(look.Z, MathF.Sqrt(look.X * look.X + look.Y * look.Y)), 75f);
            var bow = w.Get<Bow>(session.Fighters[0]);
            int flying = 0;
            foreach (var x in w.Store<Arrow>().Data) if (x.Flying) flying++;
            view.StatusLine = FormattableString.Invariant($"the scripted bow v0   {t:0.00} s   {Vector(a - b):0.0} m apart   bow {bow.State}, draw {bow.Share:0.00}, {bow.Arrows} arrows left, {flying} in flight, {w.Store<Arrow>().Count - flying} spent");
            for (int i = 0; i < Math.Max(1, renderer.StillFrames); i++) renderer.Draw(view);
            // An arrow in flight or stuck is drawn: the earlier build's white line, or 1.1's arrow model when the install has it.
            if (w.Store<Arrow>().Count > 0)
                Assert.True(view.SwingTrail.Exists(l => l.Kind == SwingTrailKind.Arrow) || view.Characters.Exists(c => c.Model.Name == "arrow"), "an arrow drawn");
            string png = Path.Combine(folder, FormattableString.Invariant($"bow-20m-{t:00.00}s.png"));
            renderer.SaveScreenshot(png);
            drawn.Add(png);
            output.WriteLine($"{png}: {view.StatusLine}");
        }
        Assert.All(drawn, png => Assert.True(new FileInfo(png).Length > 10_000, png));
        if (string.IsNullOrEmpty(asked)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void An_arena_cast_without_a_bow_registers_none_of_it()
    {
        Assert.False(ArenaCast.BowsIn(null));
        var (s, baseline) = Load();
        var cast = s.Points[0].Cast;
        var bowless = new ArenaCast { Fighters = [.. cast.Fighters.Select(f => f with { Ranged = null, Arrows = null })] };
        Assert.False(ArenaCast.BowsIn(bowless.Write()));
        Assert.DoesNotContain("Ranged", bowless.Write(), StringComparison.Ordinal);   // the arena's cast text is as before
        using var session = Session.Create(OptionsFor(s, baseline, 0, 0) with { Arena = bowless.Write(), Scenario = null });
        Assert.False(session.World.TryStore<Bow>(out _));
        Assert.DoesNotContain(WorldBuilder.RunOrder(session.World), n => n.Contains("platform.bow", StringComparison.Ordinal) || n.Contains("projectiles", StringComparison.Ordinal));
    }
}
