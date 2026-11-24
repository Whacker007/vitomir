// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The models repository's clips — getting over and onto things, off edges, the air fitted to the fall — as the program gives them
/// (<c>overClipsOff</c>, <c>--over-clips-off</c>; the note <c>over</c>). A tape without the note is the earlier build's. On the proving ground (skip without it, the 1.1
/// install or the models repository's files): on a set without the clips the world has no store and digests as with <c>--over-clips-off</c>, said once
/// in the log; with them (a newer set) the player vaults on its clip, and a recording carries the note and replays digest for digest.
/// </summary>
public sealed class PeopleOverTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_and_flags_give_the_rules_and_the_over_clips_off_switch_turns_them_off()
    {
        var plain = new PlatformSettings();
        Assert.True(plain.PersonOverFor(new Cli([])).Clips);
        Assert.False(plain.PersonOverFor(new Cli(["--over-clips-off"])).Any);
        Assert.False(plain.PersonOverFor(new Cli(["--air-clips-off"])).Any);   // they ride on the air's clips
        Assert.False(new PlatformSettings { OverClipsOff = true }.PersonOverFor(new Cli([])).Any);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-over-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { OverClipsOff = true }.Save(path);
            Assert.Contains("\"overClipsOff\": true", File.ReadAllText(path));
            Assert.True(PlatformSettings.Load(path).OverClipsOff);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_the_over_clips_off()
    {
        var head = new TapeHeader();
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonOver.Any);
        head.Notes[PersonOverSettings.TapeNote] = new PersonOverSettings { CutSeconds = 0.05f }.Note();
        var n = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(n.PersonOver.Clips);
        Assert.Equal(0.05f, n.PersonOver.CutSeconds);
        head.Notes[PersonOverSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonOver.Any);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-over-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, List<string>? said = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = line => said?.Add(line),
    };

    static ActionFrame Frame(float ahead, Buttons buttons) => new(new Vector2(0f, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>
    /// At the vault station's 0.80 m wall (its start 8 m before it, facing it): running at it, jump pressed 2.5 m short (by where the player is), on
    /// over it, and stopping; the frames as they were given.
    /// </summary>
    static IEnumerable<ActionFrame> RunAndVault(Session session, float pressShort = 2.5f)
    {
        var from = session.World.Get<Body>(session.Player).Feet;
        bool pressed = false;
        for (int t = 0; t < 5 * World.TicksPerSecond; t++)
        {
            float gone = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(session.World.Get<Body>(session.Player).Feet.X, session.World.Get<Body>(session.Player).Feet.Y));
            bool press = !pressed && gone >= 8f - pressShort;
            pressed |= press;
            yield return Frame(t < 4 * World.TicksPerSecond ? 1f : 0f, press ? Buttons.Jump : Buttons.None);
        }
    }

    [Fact]
    public void Without_the_clips_the_world_is_as_with_them_off_and_with_them_the_player_vaults_on_its_clip()
    {
        var (course, game, models) = Needs();
        var said = new List<string>();
        var options = On(course, game, models, "vault-0.80", said);
        using var now = Session.Create(options);
        using var then = Session.Create(options with { PersonOver = PersonOverSettings.Off });
        var assets = now.World.Service<CharacterAssets>();
        var person = assets.Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        Assert.False(then.World.TryStore<PersonOver>(out _));
        if (person.Over is null)
        {
            // On an older set: nothing of the earlier build's is in the world, said once, and it digests as the off switch gives it.
            Assert.False(now.World.TryStore<PersonOver>(out _));
            Assert.Equal(person.Air is null ? 0 : 1, said.Count(l => l.Contains("no moves over and onto things")));
            int compared = 0;
            foreach (var frame in RunAndVault(now))
            {
                then.Step(frame);
                now.Step(frame);
                if (now.Tick % 60 == 0) { Assert.Equal(then.Digest(), now.Digest()); compared++; }
            }
            output.WriteLine($"on an older set: {compared} digests the same as the over clips off's");
            Assert.True(compared > 3);
            return;
        }
        Assert.True(now.World.Has<PersonOver>(now.Player));
        Assert.DoesNotContain(said, l => l.Contains("no moves over and onto things"));
        var clips = new List<string>();
        var started = new List<TraversalKind>();
        bool pressed = false;
        var from = now.World.Get<Body>(now.Player).Feet;
        for (int t = 0; t < 5 * World.TicksPerSecond; t++)
        {
            var feet = now.World.Get<Body>(now.Player).Feet;
            bool press = !pressed && feet.X - from.X >= 5.5f;
            pressed |= press;
            now.Step(Frame(t < 4 * World.TicksPerSecond ? 1f : 0f, press ? Buttons.Jump : Buttons.None));
            var name = assets.Clip(now.World.Get<CharacterAnimation>(now.Player).Clip).Name;
            if (clips.Count == 0 || clips[^1] != name) clips.Add(name);
            var kind = now.World.Get<Traversal>(now.Player).Kind;
            if (kind != TraversalKind.None && (started.Count == 0 || started[^1] != kind)) started.Add(kind);
        }
        output.WriteLine("clips: " + string.Join(", ", clips.Where(c => !c.Contains(':'))) + "; moves: " + string.Join(", ", started));
        Assert.Contains("vault_low", clips);
        Assert.Contains(TraversalKind.Vault, started);
        Assert.True(now.World.Get<Body>(now.Player).Feet.X > from.X + 8.3f, "over the wall");
    }

    [Fact]
    public void A_recording_notes_the_moves_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "vault-0.80") with { PersonOver = new PersonOverSettings { EnterSeconds = 0.1f } };
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            bool pressed = false;
            var from = session.World.Get<Body>(session.Player).Feet;
            for (int t = 0; t < 5 * World.TicksPerSecond; t++)
            {
                bool press = !pressed && session.World.Get<Body>(session.Player).Feet.X - from.X >= 5.5f;
                pressed |= press;
                var frame = Frame(t < 4 * World.TicksPerSecond ? 1f : 0f, press ? Buttons.Jump : Buttons.None);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonOver.Note(), tape.Header.Notes[PersonOverSettings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonOver.Note(), again.PersonOver.Note());
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        output.WriteLine($"{compared} digests replayed");
        Assert.True(compared >= 5);
    }

    /// <summary>
    /// With <c>VITOMIR_OVER_TAPES</c> (a folder): the tapes the stills are drawn from (<c>--replay-dump … --dump-stills … --dump-frame 3.2,90</c>),
    /// each with the clips (<c>…-now</c>) and with the earlier build's (<c>…-before</c>): a vault of 0.80 m and of 1.00 m out of a run; a climb onto 0.9 m and a mantle of
    /// 1.30 m and 1.70 m (walked to 0.55 m from the face and tapped); a run and a walk off <c>drops-1</c> and <c>drops-2</c>.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_OVER_TAPES") is { Length: > 0 }, "VITOMIR_OVER_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_OVER_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        // Walked to within 0.55 m of the face 8 m ahead, stopped, tapped.
        static IEnumerable<ActionFrame> WalkAndTap(Session s)
        {
            var from = s.World.Get<Body>(s.Player).Feet;
            int t = 0;
            while (s.World.Get<Body>(s.Player).Feet.X - from.X < 8f - 0.55f - 0.3f && t++ < 12 * World.TicksPerSecond) yield return Frame(1f, Buttons.Walk);
            for (int i = 0; i < 60; i++) yield return Frame(0f, Buttons.None);
            yield return Frame(0f, Buttons.Jump);
            for (int i = 0; i < 360; i++) yield return Frame(0f, Buttons.None);
        }
        static IEnumerable<ActionFrame> Off(Session s, Buttons gait)
        {
            for (int t = 0; t < 3 * World.TicksPerSecond; t++) yield return Frame(1f, gait);
            for (int t = 0; t < 2 * World.TicksPerSecond; t++) yield return Frame(0f, Buttons.None);
        }
        foreach (var (over, suffix) in new[] { (new PersonOverSettings(), "now"), (PersonOverSettings.Off, "before") })
            foreach (var (name, start, kind) in new[]
            {
                ("vault080", "vault-0.80", 0), ("vault100", "vault-1.00", 0), ("climb090", "vault-climb0.9", 1), ("mantle130", "mantle-1.30", 1),
                ("mantle170", "mantle-1.70", 1), ("runoff1", "drops-1", 2), ("runoff2", "drops-2", 2), ("walkoff1", "drops-1", 3), ("walkoff2", "drops-2", 3),
            })
            {
                using var session = Session.Create(On(course, game, models, start) with { PersonOver = over });
                var tape = new Tape { Header = session.TapeHeader(name) };
                var frames = kind switch { 0 => RunAndVault(session), 1 => WalkAndTap(session), 2 => Off(session, Buttons.None), _ => Off(session, Buttons.Walk) };
                foreach (var frame in frames)
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                output.WriteLine($"{path}: {tape.Frames.Count} ticks, the player ended at {session.World.Get<Body>(session.Player).Feet}");
            }
    }
}
