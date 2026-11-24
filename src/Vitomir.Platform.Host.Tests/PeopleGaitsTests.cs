// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The player's own gaits, quicker stops, the lean, the look and the head's lead as the
/// program gives them: the settings (<c>playerGaitsOff</c> / <c>--player-gaits-off</c>, <c>playerRunFrom</c>, <c>playerSprintFrom</c>, <c>stopBlend</c>; <c>lean</c> /
/// <c>--no-lean</c>, <c>leanSprintDegrees</c>, <c>leanRunDegrees</c>, <c>leanWalkShare</c>, <c>leanEase</c>; <c>headLook</c> / <c>--no-head-look</c>,
/// <c>lookYawDegrees</c>, <c>lookPitchDegrees</c>; <c>leadTurns</c> / <c>--no-lead-turns</c>; <c>inertialJoins</c> / <c>--no-inertial-joins</c>,
/// <c>inertialJoinSeconds</c>); the note <c>gaits</c> (none: the earlier build's); on the proving ground (skip
/// without it, the 1.1 install or the models repository's files of that round and after): the player's paces and a recording replayed digest for digest on these and
/// The earlier build's, and with the lean, the look and the head's lead off; with <c>VITOMIR_GAITS_TAPES</c>, the tapes the stills are drawn from.
/// </summary>
public sealed class PeopleGaitsTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_player_s_gaits_and_presentation_and_the_switches_turn_them_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonGaitsFor(new Cli([]));
        Assert.Equal((true, 2.8f, 4.8f, 0.2f), (on.Player, on.RunFrom, on.SprintFrom, on.StopBlend));
        Assert.Equal(PersonGaitSettings.Off, plain.PersonGaitsFor(new Cli(["--player-gaits-off"])));
        Assert.Equal(PersonGaitSettings.Off, new PlatformSettings { PlayerGaitsOff = true }.PersonGaitsFor(new Cli([])));
        Assert.Equal(PersonGaitSettings.Off, plain.PersonGaitsFor(new Cli(["--start-clips-off"])));   // it rides on the starts and stops
        Assert.Equal("off", PersonGaitSettings.Off.Note());
        Assert.Equal(3.0f, new PlatformSettings { PlayerRunFrom = 3.0f }.PersonGaitsFor(new Cli([])).RunFrom);
        Assert.Equal(2.8f, new PlatformSettings { PlayerRunFrom = 9f }.PersonGaitsFor(new Cli([])).RunFrom);   // out of 2.2 to 3.6: the default
        Assert.Equal(0.3f, new PlatformSettings { StopBlend = 0.3f }.PersonGaitsFor(new Cli([])).StopBlend);
        var shown = plain.PersonPresentFor(new Cli([]));
        Assert.Equal((true, 18f, 12f, 0.25f, 0.15f, true, 20f, 15f, true),
                     (shown.Lean, shown.LeanSprintDegrees, shown.LeanRunDegrees, shown.LeanWalkShare, shown.LeanEase, shown.Look, shown.LookYawDegrees, shown.LookPitchDegrees, shown.Lead));
        Assert.Equal((true, 0.2f), (shown.Joins, shown.JoinSeconds));
        var none = plain.PersonPresentFor(new Cli(["--no-lean", "--no-head-look", "--no-lead-turns", "--no-inertial-joins"]));
        Assert.False(none.Any);
        Assert.Equal(0.3f, new PlatformSettings { InertialJoinSeconds = 0.3f }.PersonPresentFor(new Cli([])).JoinSeconds);
        Assert.False(new PlatformSettings { Lean = false }.PersonPresentFor(new Cli([])).Lean);
        Assert.Equal(18f, new PlatformSettings { LeanSprintDegrees = 40f }.PersonPresentFor(new Cli([])).LeanSprintDegrees);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-gaits-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { PlayerGaitsOff = true, PlayerRunFrom = 3f, StopBlend = 0.25f, Lean = false, LeanRunDegrees = 10f, HeadLook = false, LeadTurns = false, InertialJoins = false }.Save(path);
            var text = File.ReadAllText(path);
            foreach (var key in new[] { "\"playerGaitsOff\": true", "\"playerRunFrom\": 3", "\"stopBlend\": 0.25", "\"lean\": false", "\"leanRunDegrees\": 10", "\"headLook\": false", "\"leadTurns\": false", "\"inertialJoins\": false" })
                Assert.Contains(key, text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((true, 3f, 0.25f, false, 10f, false, false), (back.PlayerGaitsOff, back.PlayerRunFrom, back.StopBlend, back.Lean, back.LeanRunDegrees, back.HeadLook, back.LeadTurns));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_with_no_gaits_note_plays_on_the_townsfolk_s_gaits_and_one_with_it_on_the_player_s()
    {
        var head = new TapeHeader();
        Assert.Equal(PersonGaitSettings.Off, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonGaits);
        head.Notes[PersonGaitSettings.TapeNote] = "off";
        Assert.Equal(PersonGaitSettings.Off, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonGaits);
        var mine = new PersonGaitSettings { RunFrom = 3.1f, StopBlend = 0.15f };
        head.Notes[PersonGaitSettings.TapeNote] = mine.Note();
        Assert.Equal(mine, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonGaits);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-gaits-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start = "start") => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static void NeedsGaits(SessionOptions options)
    {
        using var s = Session.Create(options);
        var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
        Assert.SkipUnless(person?.PlayerGaits is { } g && g.Stops.All(c => c >= 0), "the models repository's files are too old (no walk_player, run_player, the player's 18 starts and stops)");
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons, float look = 0f) => new(wish, new Vector2(look, 0f), buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    static readonly Vector2 Fwd = new(0f, 1f);

    /// <summary>
    /// From the start: 2 s walking ahead, a stop, 3 s running, 3 s sprinting with the view turned left through a curve (a quarter turn over 2 s) and back, a stop.
    /// </summary>
    static Func<int, Session, ActionFrame> Paces() => (t, s) =>
    {
        float sec = t * World.TickSeconds;
        return sec switch
        {
            < 2f => Frame(Fwd, Buttons.Walk),
            < 3.5f => Frame(Vector2.Zero, Buttons.None),
            < 6.5f => Frame(Fwd, Buttons.None),
            < 7.5f => Frame(Fwd, Buttons.Sprint),
            < 9.5f => Frame(Fwd, Buttons.Sprint, DetMath.HalfPi / (2f * World.TicksPerSecond)),
            < 10.5f => Frame(Fwd, Buttons.Sprint),
            _ => Frame(Vector2.Zero, Buttons.None),
        };
    };

    /// <summary>A run: the log, each tick's drawn clip, the body's speed, the tape.</summary>
    static (List<string> Said, List<(string Drawn, float Speed)> Rows, Tape Tape) Run(SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks)
    {
        var said = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var assets = s.World.Service<CharacterAssets>();
        var tape = new Tape { Header = s.TapeHeader("test") };
        var rows = new List<(string, float)>();
        for (int t = 0; t < ticks; t++)
        {
            var frame = plan(t, s);
            s.Step(frame);
            tape.Frames.Add(frame);
            if (s.Tick % Tape.DigestEvery == 0) tape.Digests[s.Tick] = s.Digest();
            var b = s.World.Get<Body>(s.Player);
            rows.Add((assets.Clip(s.World.Get<CharacterAnimation>(s.Player).Clip).Name, new Vector2(b.Velocity.X, b.Velocity.Y).Length()));
        }
        return (said, rows, tape);
    }

    [Fact]
    public void On_the_proving_ground_the_player_walks_runs_and_sprints_at_the_clips_paces_and_a_recording_replays_with_its_gaits_and_without()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models);
        NeedsGaits(o);
        int ticks = 12 * World.TicksPerSecond;
        foreach (var options in new[] { o, o with { PersonGaits = PersonGaitSettings.Off } })
        {
            var (_, rows, tape) = Run(options, Paces(), ticks);
            float walk = rows[(int)(1.9f * World.TicksPerSecond)].Speed, run = rows[(int)(6.4f * World.TicksPerSecond)].Speed, sprint = rows[(int)(7.4f * World.TicksPerSecond)].Speed;
            output.WriteLine($"{options.PersonGaits.Note()}: walk {F(walk)}, run {F(run)}, sprint {F(sprint)} m/s; the clips {string.Join(", ", rows.Select(r => r.Drawn).Distinct().Take(14))}");
            if (options.PersonGaits.Player) Assert.Equal((2.0f, 4.0f, 5.8f), (MathF.Round(walk, 1), MathF.Round(run, 1), MathF.Round(sprint, 1)));
            else Assert.Equal((1.3f, 3.0f, 5.5f), (MathF.Round(walk, 1), MathF.Round(run, 1), MathF.Round(sprint, 1)));
            Assert.Equal(options.PersonGaits.Note(), tape.Header.Notes[PersonGaitSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonGaits, again.PersonGaits);
            // Replayed as recorded, and with the lean, the look and the head's lead off: the same digests (presentation).
            foreach (var replayed in options.PersonGaits.Player ? new[] { again, again with { PersonPresent = PersonPresentSettings.Off } } : [again])
            {
                using var replay = Session.Create(replayed);
                Assert.Equal(replayed.PersonPresent.Any && options.PersonGaits.Player, replay.World.TryStore<PersonPresent>(out _));
                int compared = 0;
                foreach (var frame in tape.Frames)
                {
                    replay.Step(frame);
                    if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
                }
                output.WriteLine($"  replayed {(replayed.PersonPresent.Any ? "as recorded" : "with the lean, the look and the head's lead off")}: {compared} digests the same");
                Assert.True(compared >= 8);
            }
        }
    }

    /// <summary>
    /// With <c>VITOMIR_GAITS_TAPES</c> (a folder): the tapes the stills are drawn from (each gait side-on, each stop, the lean in a sharp curve at a run and a
    /// sprint, the head into a turn; the earlier build's beside them), each with its ticks to draw (<c>.ticks</c>: where each clip begins) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_GAITS_TAPES") is { Length: > 0 }, "VITOMIR_GAITS_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_GAITS_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var o = On(course, game, models);
        Func<int, Session, ActionFrame> Straight(Buttons pace, float seconds) => (t, s) => t * World.TickSeconds < seconds ? Frame(Fwd, pace) : Frame(Vector2.Zero, Buttons.None);
        // A sharp curve: up to pace 2 s, then the view turned left 90°/s for 1.5 s (the body faces the view), on, then still.
        Func<int, Session, ActionFrame> Bend(Buttons pace) => (t, s) =>
        {
            float sec = t * World.TickSeconds;
            return sec < 2f ? Frame(Fwd, pace) : sec < 3.5f ? Frame(Fwd, pace, DetMath.HalfPi / World.TicksPerSecond) : sec < 4f ? Frame(Fwd, pace) : Frame(Vector2.Zero, Buttons.None);
        };
        var cases = new List<(string Name, SessionOptions Options, Func<int, Session, ActionFrame> Plan, int Seconds)>
        {
            ("walk", o, Straight(Buttons.Walk, 3f), 5),
            ("run", o, Straight(Buttons.None, 3f), 5),
            ("sprint", o, Straight(Buttons.Sprint, 3f), 6),
            ("bend-run", o, Bend(Buttons.None), 6),
            ("bend-sprint", o, Bend(Buttons.Sprint), 6),
            ("bend-run-off", o with { PersonGaits = PersonGaitSettings.Off }, Bend(Buttons.None), 6),
            ("walk-off", o with { PersonGaits = PersonGaitSettings.Off }, Straight(Buttons.Walk, 3f), 5),
            ("run-off", o with { PersonGaits = PersonGaitSettings.Off }, Straight(Buttons.None, 3f), 5),
            ("crouch", o, (t, s) => t * World.TickSeconds < 3f ? Frame(Fwd, Buttons.Walk | Buttons.Crouch) : Frame(Vector2.Zero, Buttons.None), 4),
        };
        foreach (var (name, options, plan, seconds) in cases)
        {
            var said = new List<string>();
            using var session = Session.Create(options with { Say = said.Add });
            var assets = session.World.Service<CharacterAssets>();
            var tape = new Tape { Header = session.TapeHeader(name) };
            var marks = new List<(int Tick, string What)>();
            string last = "";
            for (int t = 0; t < seconds * World.TicksPerSecond; t++)
            {
                var frame = plan(t, session);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                string clip = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                if (clip.Contains(':', StringComparison.Ordinal)) clip = clip[(clip.IndexOf(':') + 1)..];
                if (clip.Contains('@', StringComparison.Ordinal)) clip = clip[..clip.IndexOf('@')];
                if (clip != last) marks.Add(((int)session.Tick, clip));
                last = clip;
            }
            string path = Path.Combine(dir, $"{name}.tape");
            tape.Write(path);
            File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
            File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
            output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
        }
    }
}
