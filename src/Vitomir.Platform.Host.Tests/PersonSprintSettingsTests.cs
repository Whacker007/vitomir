// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The models repository's as the program gives them — the people's sprint clip (the settings <c>sprintFrom</c>,
/// <c>sprintClipOff</c>, the flags <c>--sprint-from</c>, <c>--sprint-clip-off</c>; the tape's note <c>sprint</c>) and the new bodies' crouched
/// capsule of 1.15 m (<c>personCrouchHeight</c>, <c>--person-crouch-height</c>; the note <c>crouchHeight</c>); a tape without the notes
/// replays on the run and 1.25 m. The sessions' tests skip without the proving ground, the 1.1 install or the models
/// repository's files (and the sprint's without an older set).
/// </summary>
public sealed class PersonSprintSettingsTests
{
    [Fact]
    public void The_settings_and_flags_give_the_sprint_and_the_crouched_capsule()
    {
        var plain = new PlatformSettings();
        Assert.Equal(4.25f, plain.SprintFromFor(new Cli([])));
        Assert.Equal(new AnimationSettings().PersonSprintFrom, plain.SprintFromFor(new Cli([])));
        Assert.Equal(0f, plain.SprintFromFor(new Cli(["--sprint-clip-off"])));
        Assert.Equal(0f, new PlatformSettings { SprintClipOff = true }.SprintFromFor(new Cli(["--sprint-from", "5"])));
        Assert.Equal(5f, new PlatformSettings { SprintFrom = 3.5f }.SprintFromFor(new Cli(["--sprint-from", "5"])));
        Assert.Equal(3.5f, new PlatformSettings { SprintFrom = 3.5f }.SprintFromFor(new Cli([])));
        Assert.Equal(4.25f, new PlatformSettings { SprintFrom = -1f }.SprintFromFor(new Cli([])));            // a setting out of its sense: the default
        Assert.Throws<SessionException>(() => plain.SprintFromFor(new Cli(["--sprint-from", "fast"])));
        Assert.Throws<SessionException>(() => plain.SprintFromFor(new Cli(["--sprint-from", "40"])));

        Assert.Equal(1.15f, plain.PersonCrouchHeightFor(new Cli([])));
        Assert.Equal(PeopleBodies.CrouchHeight, plain.PersonCrouchHeightFor(new Cli([])));
        Assert.Equal(1.25f, plain.PersonCrouchHeightFor(new Cli(["--person-crouch-height", "1.25"])));
        Assert.Equal(1.2f, new PlatformSettings { PersonCrouchHeight = 1.2f }.PersonCrouchHeightFor(new Cli([])));
        Assert.Equal(1.15f, new PlatformSettings { PersonCrouchHeight = 3f }.PersonCrouchHeightFor(new Cli([])));
        Assert.Throws<SessionException>(() => plain.PersonCrouchHeightFor(new Cli(["--person-crouch-height", "0.3"])));

        string dir = Path.Combine(Path.GetTempPath(), "vitomir-sprint-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { SprintFrom = 4.5f, SprintClipOff = true, PersonCrouchHeight = 1.2f }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"sprintFrom\": 4.5", text);
            Assert.Contains("\"sprintClipOff\": true", text);
            Assert.Contains("\"personCrouchHeight\": 1.2", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((4.5f, true, 1.2f), (back.SprintFrom, back.SprintClipOff, back.PersonCrouchHeight));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_notes_runs_and_crouches_to_1_25_and_the_notes_carry_the_numbers()
    {
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(new TapeHeader());
        Assert.Equal((0f, PeopleBodies.CrouchHeightTall), (o.PersonSprintFrom, o.PersonCrouchHeight));
        var head = new TapeHeader();
        head.Notes[SessionOptions.SprintNote] = SessionOptions.NoteOf(4.7f);
        head.Notes[PeopleBodies.CrouchNote] = SessionOptions.NoteOf(1.15f);
        var n = new SessionOptions { Level = "", PersonSprintFrom = 0f, PersonCrouchHeight = 1.3f, Say = _ => { } }.From(head);   // the tape's numbers win
        Assert.Equal((4.7f, 1.15f), (n.PersonSprintFrom, n.PersonCrouchHeight));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-person-sprint-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions Person(string course, string game, string models) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static ActionFrame Frame(Vector2 move, Buttons held) => new(move, Vector2.Zero, held, new Vector2(0.5f, 0.5f), 16f / 9f);

    [Fact]
    public void The_player_sprints_on_the_sprint_clip_the_tape_notes_it_and_replays_and_without_the_note_runs()
    {
        var (course, game, models) = Needs();
        using var probe = Session.Create(Person(course, game, models));
        Assert.NotNull(probe.PlayerBody);
        var assets = probe.World.Service<CharacterAssets>();
        var person = assets.Kind(probe.World.Get<CharacterAnimation>(probe.Player).Kind).Person!;
        Assert.SkipUnless(person.Sprint >= 0, "the models repository's files are too old (no sprint)");

        (Tape Tape, List<string> Clips, float Fastest) Record(SessionOptions options)
        {
            using var session = Session.Create(options);
            var tape = new Tape { Header = session.TapeHeader("test") };
            var clips = new List<string>();
            float fastest = 0f;
            for (int t = 0; t < 150; t++)
            {
                var frame = Frame(new Vector2(0f, 1f), Buttons.Sprint);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                clips.Add(assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name);
                var v = session.World.Get<Body>(session.Player).Velocity;
                fastest = MathF.Max(fastest, new Vector2(v.X, v.Y).Length());
            }
            return (tape, clips, fastest);
        }
        var now = Record(Person(course, game, models));
        Assert.True(now.Fastest > 5f, $"the body sprinted (at most {now.Fastest:0.00} m/s)");
        Assert.Equal("sprint", now.Clips[^1]);
        Assert.Equal(SessionOptions.NoteOf(4.25f), now.Tape.Header.Notes[SessionOptions.SprintNote]);
        Assert.Equal(SessionOptions.NoteOf(1.15f), now.Tape.Header.Notes[PeopleBodies.CrouchNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(now.Tape.Header);
        Assert.Equal((4.25f, 1.15f), (again.PersonSprintFrom, again.PersonCrouchHeight));
        int compared = 0;
        using (var replay = Session.Create(again))
            foreach (var frame in now.Tape.Frames)
            {
                replay.Step(frame);
                if (now.Tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.True(want == replay.Digest(), $"tick {replay.Tick}"); compared++; }
            }
        Assert.True(compared >= 5);

        // --sprint-clip-off: the run's blend, as before; a tape without the note replays so.
        var old = Record(Person(course, game, models) with { PersonSprintFrom = 0f });
        Assert.DoesNotContain("sprint", old.Clips);
        Assert.NotEqual(now.Tape.Digests.Last().Value, old.Tape.Digests.Last().Value);   // the drawn pose is the simulation's
        old.Tape.Header.Notes.Remove(SessionOptions.SprintNote);
        Assert.Equal(0f, new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(old.Tape.Header).PersonSprintFrom);

        // Christof's tapes carry neither note.
        using var christof = Session.Create(Person(course, game, models) with { Body = PeopleBodies.OneOne });
        var h = christof.TapeHeader("test");
        Assert.False(h.Notes.ContainsKey(SessionOptions.SprintNote) || h.Notes.ContainsKey(PeopleBodies.CrouchNote));
    }

    [Fact]
    public void Crouched_the_new_body_passes_the_1_16_m_tunnel_at_1_15_and_not_at_the_tall_1_25()
    {
        var (course, game, models) = Needs();
        (float Gone, float Capsule) Crawl(float height)
        {
            using var session = Session.Create(Person(course, game, models) with { Start = "crouch-1.16", PersonCrouchHeight = height });
            Assert.NotNull(session.PlayerBody);
            float x0 = session.World.Get<Body>(session.Player).Feet.X;
            for (int t = 0; t < 14 * World.TicksPerSecond; t++) session.Step(Frame(new Vector2(0f, 1f), Buttons.Crouch));
            return (session.World.Get<Body>(session.Player).Feet.X - x0, session.World.Get<Locomotion>(session.Player).CrouchHeight);
        }
        var low = Crawl(PeopleBodies.CrouchHeight);
        var high = Crawl(PeopleBodies.CrouchHeightTall);
        Assert.Equal((1.15f, 1.25f), (low.Capsule, high.Capsule));
        Assert.True(low.Gone > high.Gone + 4f, $"in 14 s crouched at 1.15 m the body went {low.Gone:0.00} m, at 1.25 m {high.Gone:0.00} m (stopped at the tunnel's mouth)");
    }
}
