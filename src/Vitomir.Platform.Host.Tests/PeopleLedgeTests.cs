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
/// The models repository's clips — the ledge on clips (hang, shimmy, braces, pull-up, drop, grabs) — as the program gives them (<c>ledgeClipsOff</c>,
/// <c>--ledge-clips-off</c>; the note <c>ledge</c>). A tape without the note is the earlier build's. On the proving ground (skip without it, the 1.1 install or the models
/// stream's files): on a set without the clips the world has no store and digests as with <c>--ledge-clips-off</c>, said once in the log; with them
/// (a newer set) only the player has the ledge (the models repository's no townsperson hangs), a tap below the 2.2 m wall grabs on its clip, and a
/// recording carries the note and replays digest for digest.
/// </summary>
public sealed class PeopleLedgeTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_and_flags_give_the_ledge_and_the_ledge_clips_off_switch_turns_it_off()
    {
        var plain = new PlatformSettings();
        Assert.True(plain.PersonLedgeFor(new Cli([])).Clips);
        Assert.False(plain.PersonLedgeFor(new Cli(["--ledge-clips-off"])).Any);
        Assert.False(plain.PersonLedgeFor(new Cli(["--over-clips-off"])).Any);   // it rides on the moves over and onto things
        Assert.False(plain.PersonLedgeFor(new Cli(["--air-clips-off"])).Any);    // and on the air's clips
        Assert.False(new PlatformSettings { LedgeClipsOff = true }.PersonLedgeFor(new Cli([])).Any);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-ledge-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { LedgeClipsOff = true }.Save(path);
            Assert.Contains("\"ledgeClipsOff\": true", File.ReadAllText(path));
            Assert.True(PlatformSettings.Load(path).LedgeClipsOff);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_the_ledge_clips_off()
    {
        var head = new TapeHeader();
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonLedge.Any);
        head.Notes[PersonLedgeSettings.TapeNote] = new PersonLedgeSettings().Note();
        Assert.True(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonLedge.Clips);
        head.Notes[PersonLedgeSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonLedge.Any);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-ledge-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, List<string>? said = null, int people = 0) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = people,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = line => said?.Add(line),
    };

    static ActionFrame Frame(float ahead, Buttons buttons, float right = 0f) => new(new Vector2(right, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>
    /// At a grab lane (its start 8 m before the wall, facing it): walked to within 0.55 m of the face, stopped, jump tapped; hanging, a shimmy to the
    /// right for 1.2 s, then still, then <paramref name="last"/> (jump: the pull-up; crouch: the drop), and still to the end.
    /// </summary>
    static IEnumerable<ActionFrame> WalkGrabShimmy(Session s, Buttons last = Buttons.Jump, float shimmy = 1.2f)
    {
        var from = s.World.Get<Body>(s.Player).Feet;
        int t = 0;
        while (s.World.Get<Body>(s.Player).Feet.X - from.X < 8f - 0.55f - 0.3f && t++ < 12 * World.TicksPerSecond) yield return Frame(1f, Buttons.Walk);
        for (int i = 0; i < 60; i++) yield return Frame(0f, Buttons.None);
        yield return Frame(0f, Buttons.Jump);
        for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
        for (int i = 0; i < (int)(shimmy * World.TicksPerSecond); i++) yield return Frame(0f, Buttons.None, right: 1f);
        for (int i = 0; i < 2 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
        if (last != Buttons.None) yield return Frame(0f, last);
        for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
    }

    [Fact]
    public void Without_the_clips_the_world_is_as_with_them_off_and_with_them_only_the_player_hangs_on_them()
    {
        var (course, game, models) = Needs();
        var said = new List<string>();
        var options = On(course, game, models, "grab-2.2", said, people: 6);
        using var now = Session.Create(options);
        using var then = Session.Create(options with { PersonLedge = PersonLedgeSettings.Off });
        var assets = now.World.Service<CharacterAssets>();
        var person = assets.Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        Assert.False(then.World.TryStore<PersonLedge>(out _));
        if (person.Ledge is null)
        {
            // On an older set: nothing of the earlier build's is in the world, said once, and it digests as the off switch gives it.
            Assert.False(now.World.TryStore<PersonLedge>(out _));
            Assert.Equal(person.Over is null ? 0 : 1, said.Count(l => l.Contains("no ledge clips")));
            int compared = 0;
            foreach (var frame in WalkGrabShimmy(now))
            {
                then.Step(frame);
                now.Step(frame);
                if (now.Tick % 60 == 0) { Assert.Equal(then.Digest(), now.Digest()); compared++; }
            }
            output.WriteLine($"on an older set: {compared} digests the same as the ledge clips off's");
            Assert.True(compared > 3);
            return;
        }
        Assert.True(now.World.TryStore<PersonLedge>(out var ledges));
        Assert.True(now.World.Has<PersonLedge>(now.Player));
        Assert.Equal(1, ledges!.Count);   // the townsfolk none
        output.WriteLine($"{now.World.Store<CharacterAnimation>().Count} animated, {ledges.Count} with the ledge (the player)");
        Assert.DoesNotContain(said, l => l.Contains("no ledge clips"));
        var clips = new List<string>();
        foreach (var frame in WalkGrabShimmy(now))
        {
            now.Step(frame);
            var name = assets.Clip(now.World.Get<CharacterAnimation>(now.Player).Clip).Name;
            if (clips.Count == 0 || clips[^1] != name) clips.Add(name);
        }
        output.WriteLine("clips: " + string.Join(", ", clips.Where(c => !c.Contains(':'))));
        // With the braced twins the standing grab ends braced itself (no brace after it).
        bool twins = person.Ledge?.Paths.Clips.Any(c => c.Twin) == true && now.World.TryService<PersonMovesSettings>(out var mv) && mv.BracedGrabs;
        Assert.Contains(twins ? "grab_jump_up_braced" : "grab_jump_up", clips);
        if (twins) Assert.DoesNotContain("hang_brace_on", clips); else Assert.Contains("hang_brace_on", clips);
        Assert.Contains("shimmy_right_braced", clips);
        Assert.Contains("pull_up_braced", clips);
        Assert.True(now.World.Get<Body>(now.Player).State == BodyState.Ground && now.World.Get<Body>(now.Player).Feet.Z > 2.1f, "on the top");
    }

    [Fact]
    public void A_recording_notes_the_ledge_and_replays_on_it()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "grab-2.2");
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in WalkGrabShimmy(session, Buttons.Crouch))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonLedge.Note(), tape.Header.Notes[PersonLedgeSettings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonLedge.Note(), again.PersonLedge.Note());
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        output.WriteLine($"{compared} digests replayed");
        Assert.True(compared >= 10);
    }

    /// <summary>
    /// With <c>VITOMIR_LEDGE_TAPES</c> (a folder): the tapes the stills are drawn from, each with the clips (<c>…-now</c>) and with the earlier build's (<c>…-before</c>):
    /// a standing grab at <c>grab-2.2</c>, its shimmy and pull-up; a running grab at <c>grab-2.4</c> (pressed 3.1 m short), a pull-up after it; a falling
    /// grab at <c>grab-2.8</c> (placed in the air over the run-up, jump held); the shimmy ledge's hang and a drop to the ground.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_LEDGE_TAPES") is { Length: > 0 }, "VITOMIR_LEDGE_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_LEDGE_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static IEnumerable<ActionFrame> RunGrab(Session s)
        {
            var from = s.World.Get<Body>(s.Player).Feet;
            bool pressed = false;
            for (int t = 0; t < 4 * World.TicksPerSecond; t++)
            {
                var b = s.World.Get<Body>(s.Player);
                bool press = !pressed && b.Feet.X - from.X >= 8f - 3.1f;
                pressed |= press;
                yield return Frame(b.State == BodyState.Ground && !pressed ? 1f : 0f, press ? Buttons.Jump : Buttons.None);
            }
            for (int i = 0; i < World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
            yield return Frame(0f, Buttons.Jump);
            for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
        }
        static IEnumerable<ActionFrame> FallGrab(Session s)
        {
            for (int t = 0; t < 4 * World.TicksPerSecond; t++) yield return Frame(0f, t < World.TicksPerSecond ? Buttons.Jump : Buttons.None);
            yield return Frame(0f, Buttons.Crouch);
            for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
        }
        // The grab lanes stand side by side along Y from (−30, 25): each lane's start 8 m before its wall, the wall's face at x −30 + run-up.
        foreach (var (ledge, suffix) in new[] { (new PersonLedgeSettings(), "now"), (PersonLedgeSettings.Off, "before") })
            foreach (var (name, start, kind) in new[]
            {
                ("grab22", "grab-2.2", 0), ("grab24run", "grab-2.4", 1), ("grab28fall", "grab-2.8", 2), ("shimmydrop", "not-built-shimmy", 3),
            })
            {
                var options = On(course, game, models, start) with { PersonLedge = ledge };
                if (kind == 2)
                {
                    // In the air, its middle 0.5 m out from the 2.8 m wall's face (the falling grab's own start), its feet 1.6 m up, facing it: falling past its lip.
                    using var probe = Session.Create(options);
                    var at = probe.World.Get<Body>(probe.Player).Feet;
                    float yaw = probe.World.Get<Body>(probe.Player).Yaw;
                    var (sn, cs) = DetMath.SinCos(yaw);
                    float face = 8f;
                    var p = at + new Vector3(cs, sn, 0f) * (face - 0.5f) + new Vector3(0f, 0f, 1.6f);
                    options = options with { Place = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{p.X:0.###},{p.Y:0.###},{p.Z:0.###},{yaw * 180f / MathF.PI:0.#}") };
                }
                using var session = Session.Create(options);
                var tape = new Tape { Header = session.TapeHeader(name) };
                var frames = kind switch { 0 => WalkGrabShimmy(session), 1 => RunGrab(session), 2 => FallGrab(session), _ => WalkGrabShimmy(session, Buttons.Crouch, 2.0f) };
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
