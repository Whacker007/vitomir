// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// A scenario's program in a session: the arena's cast with <see cref="SessionOptions.Scenario"/> runs the
/// director on the cast's fighters by their names, through the module (the enhanced game); its tape carries the program in its
/// head and replays digest for digest from the head alone, as <c>--replay-check</c> does; a bout without a program has no
/// director and no note (the arena's worlds unchanged); a program naming what the world does not have stops the session by name.
/// Skips without the 1.1 install (VTMR_GAME_DIR) or the proving ground (VITOMIR_COURSE_LEVEL).
/// </summary>
public sealed class ScenarioSessionTests
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

    static ArenaRun Duel()
    {
        var (course, game) = Grounds();
        var spec = ArenaSpec.Read("""
        { "fighters": [ { "name": "Christof", "template": "Christof", "weapon": "BroadSword", "sheet": "sandbox.christof", "place": [38, 3], "facing": 0 },
                        { "name": "Guard", "template": "rudolfGuard", "sheet": "sandbox.guard", "place": [42, 3], "facing": 180 } ],
          "seconds": 8, "bouts": 1, "seed": 5 }
        """, Path.GetTempPath(), course);
        return new ArenaRun(spec, new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } });
    }

    /// <summary>Christof walks in, strikes three times at the guard's chest and hands over to his brain; the guard guards and parries by his skill but never steps; a trigger blocks for Christof each time the guard strikes.</summary>
    static ScenarioProgram Scripted() => new()
    {
        Actors =
        [
            new ActorControl { Actor = "Christof", Foe = "Guard", Ai = ScenarioAi.Duel, Then = ScenarioThen.Ai, Tracks =
            [[
                new MoveToStep { To = new() { Actor = "Guard", DistanceMetres = 1.4f }, FaceWhileMoving = "Guard", WithinMetres = 0.1f, ForSeconds = 4f, Label = "close in" },
                new MarkStep { Text = "blows begin" },
                new RepeatStep { Count = 3, Steps = [new AttackStep { Direction = AttackDirection.Right, HoldSeconds = 0.2f, AimAt = new() { Region = AimRegion.Chest } }, new WaitStep { Seconds = 0.3f }] },
                new MarkStep { Text = "blows end" },
            ]] },
            new ActorControl { Actor = "Guard", Foe = "Christof", Ai = ScenarioAi.Duel, Mute = ControlChannels.Move, Then = ScenarioThen.Ai },
        ],
        Triggers = [new ScenarioTrigger { Label = "guard strikes", Repeat = true, When = new() { Event = new EventSelector("AttackBegun", By: "Guard") },
            Do = [new TriggerAction { Actor = "Christof", Step = new BlockStep { ForSeconds = 0.4f } }] }],
    };

    static (Tape Tape, List<AttackBegun> Begun, List<Marker> Marks, List<AimedBlow> Aimed, List<ControlChanged> Control) Record(SessionOptions options, float seconds)
    {
        using var session = Session.Create(options);
        var tape = new Tape { Header = session.TapeHeader("test") };
        var begun = new List<AttackBegun>();
        var marks = new List<Marker>();
        var aimed = new List<AimedBlow>();
        var control = new List<ControlChanged>();
        long most = (long)MathF.Round(seconds * World.TicksPerSecond);
        while (session.Tick < most)
        {
            session.Step(ActionFrame.Idle);
            tape.Frames.Add(ActionFrame.Idle);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            begun.AddRange(session.World.Events<AttackBegun>().ThisTick);
            if (session.World.TryService<EventQueue<Marker>>(out var m)) marks.AddRange(m.ThisTick);
            if (session.World.TryService<EventQueue<AimedBlow>>(out var a)) aimed.AddRange(a.ThisTick);
            if (session.World.TryService<EventQueue<ControlChanged>>(out var c)) control.AddRange(c.ThisTick);
        }
        tape.Digests[session.Tick] = session.Digest();
        return (tape, begun, marks, aimed, control);
    }

    [Fact]
    public void A_scripted_bout_runs_through_the_module_and_replays_from_its_tape_s_head_alone()
    {
        var run = Duel();
        var options = run.OptionsFor(0, 0) with { Scenario = Scripted().Write() };
        var (tape, begun, marks, aimed, control) = Record(options, 6f);
        Assert.Equal(Scripted().Write(), tape.Header.Notes[SessionOptions.ScenarioNote]);
        Assert.Contains(marks, m => m.Text == "blows end");
        long from = marks.First(m => m.Text == "blows begin").Tick, to = marks.First(m => m.Text == "blows end").Tick;
        var christof = begun.Where(b => b.Tick >= from && b.Tick <= to && aimed.Any(x => x.Swing == b.Swing && x.Entity == b.Entity)).ToList();
        Assert.Equal(3, christof.Count);
        Assert.All(aimed, a => Assert.Equal(BodyRegion.Chest, a.Region));
        Assert.Contains(control, c => c.Ai == ControlChannels.All);   // Christof handed to his brain after the script
        Assert.Contains(control, c => c.Muted == ControlChannels.Move); // the guard's steps muted

        // As --replay-check does: the options from the tape's head, a fresh session, every digest compared.
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        Assert.Equal(options.Scenario, again.Scenario);
        using var session = Session.Create(again);
        int checkedDigests = 0;
        foreach (var frame in tape.Frames)
        {
            session.Step(frame);
            if (tape.Digests.TryGetValue(session.Tick, out ulong want)) { Assert.Equal(want, session.Digest()); checkedDigests++; }
        }
        Assert.True(checkedDigests >= 10, $"{checkedDigests} digests checked");
        run.Content?.Dispose();
    }

    [Fact]
    public void An_actor_driven_through_the_player_s_controls_moves_turns_strikes_and_dodges_and_its_tape_replays()
    {
        var (course, game) = Grounds();
        var spec = ArenaSpec.Read("""
        { "fighters": [ { "name": "Christof", "template": "Christof", "weapon": "BroadSword", "sheet": "sandbox.christof", "place": [36, 3], "facing": 0 },
                        { "name": "Guard", "template": "rudolfGuard", "sheet": "sandbox.guard", "place": [42, 3], "facing": 180, "brain": "dummy" } ],
          "seconds": 8, "bouts": 1, "seed": 5 }
        """, Path.GetTempPath(), course);
        var run = new ArenaRun(spec, new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } });
        var program = new ScenarioProgram
        {
            Input = new PlayerInput
            {
                Actor = "Christof",
                Segments =
                [
                    new InputSegment { AtSeconds = 0.5f, ForSeconds = 1f, MoveForward = 1f, Label = "walk in" },
                    new InputSegment { AtSeconds = 1.6f, ForSeconds = 0.5f, TurnLeftDegrees = 90f, Label = "turn left" },
                    new InputSegment { AtSeconds = 2.4f, ForSeconds = 0.5f, TurnLeftDegrees = -90f },
                    new InputSegment { AtSeconds = 3.2f, ForSeconds = 0.3f, Hold = Buttons.Attack, Label = "strike" },
                    new InputSegment { AtSeconds = 5.0f, Press = Buttons.Dodge, Label = "dodge" },
                ],
            },
        };
        Assert.Equal("PLAYER_INPUT_REPLAY", program.AuthorityOf("Christof"));
        Assert.Equal("NATIVE_AI", program.AuthorityOf("Guard"));   // no control block: the cast's brain
        var options = run.OptionsFor(0, 0) with { Scenario = program.Write() };
        using var session = Session.Create(options);
        Assert.Equal(session.Fighters[0], session.Player);
        Assert.True(session.World.Has<Controlled>(session.Player));
        Assert.False(session.World.Has<DuelBrain>(session.Player));
        var tape = new Tape { Header = session.TapeHeader("test") };
        var begun = new List<AttackBegun>();
        var dodges = new List<DodgeBegun>();
        var feet = new List<System.Numerics.Vector3>();
        var yaw = new List<float>();
        while (session.Tick < 6 * World.TicksPerSecond)
        {
            var frame = session.ScenarioFrame();
            session.Step(frame);
            tape.Frames.Add(frame);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            begun.AddRange(session.World.Events<AttackBegun>().ThisTick);
            dodges.AddRange(session.World.Events<DodgeBegun>().ThisTick);
            feet.Add(session.World.Get<Body>(session.Player).Feet);
            yaw.Add(session.World.Get<Body>(session.Player).Yaw);
        }
        tape.Digests[session.Tick] = session.Digest();
        Assert.True(feet[180].X - feet[0].X > 1f, $"walked {feet[180].X - feet[0].X:0.00} m");
        Assert.InRange(DetMath.WrapAngle(yaw[2 * 120 + 30] - yaw[180]) * DetMath.RadToDeg, 80f, 100f);
        var strike = Assert.Single(begun, b => b.Entity == session.Player);
        Assert.InRange(strike.Tick, (long)(3.2f * 120), (long)(3.2f * 120) + 40);
        Assert.Single(dodges, d => d.Entity == session.Player && d.Tick >= 600 && d.Tick <= 602);

        // The frames are on the tape: a fresh session from its head, fed them back, agrees digest for digest.
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        using var replay = Session.Create(again);
        int checkedDigests = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); checkedDigests++; }
        }
        Assert.True(checkedDigests >= 10);
        run.Content?.Dispose();
    }

    [Fact]
    public void A_bout_without_a_program_has_no_director_and_no_note_and_a_bad_program_stops_by_name()
    {
        var run = Duel();
        var options = run.OptionsFor(0, 0);
        using (var session = Session.Create(options))
        {
            Assert.False(session.World.TryService<ScenarioDirector>(out _));
            Assert.False(session.TapeHeader("test").Notes.ContainsKey(SessionOptions.ScenarioNote));
            Assert.DoesNotContain(WorldBuilder.RunOrder(session.World), s => s.EndsWith("platform.scenario-director", StringComparison.Ordinal));
        }
        var bad = new ScenarioProgram { Actors = [new ActorControl { Actor = "Christof", Tracks = [[new FaceStep { Toward = new() { Actor = "Nobody" } }]] }] };
        var e = Assert.Throws<SessionException>(() => Session.Create(options with { Scenario = bad.Write() }));
        Assert.Contains("no actor is named Nobody", e.Message);
        Assert.Contains("not readable", Assert.Throws<SessionException>(() => Session.Create(options with { Scenario = "{ nope" })).Message);
        run.Content?.Dispose();
    }
}
