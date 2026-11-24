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
/// The models repository's jumps, fall and landings as the program gives them (<c>airClipsOff</c>, <c>--air-clips-off</c>; <c>jumpAtOnce</c>,
/// <c>--jump-at-once</c>; the note <c>air</c>). A tape without the note is the earlier build's. On the proving ground (skip without it, the 1.1 install or the
/// models repository's files): on a set without the clips the world has no store and digests as with <c>--air-clips-off</c>, said once in the log;
/// with them (a newer set) the player's jump leaves after its clip's squash (at once with <c>--jump-at-once</c>), and a recording carries the
/// note and replays digest for digest.
/// </summary>
public sealed class PeopleAirTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_and_flags_give_the_rules_and_the_air_clips_off_switch_turns_them_off()
    {
        var plain = new PlatformSettings();
        var s = plain.PersonAirFor(new Cli([]));
        Assert.Equal((true, true), (s.Clips, s.Squash));
        Assert.False(plain.PersonAirFor(new Cli(["--air-clips-off"])).Any);
        Assert.Equal((true, false), (plain.PersonAirFor(new Cli(["--jump-at-once"])).Clips, plain.PersonAirFor(new Cli(["--jump-at-once"])).Squash));
        Assert.False(new PlatformSettings { AirClipsOff = true }.PersonAirFor(new Cli([])).Any);
        Assert.False(new PlatformSettings { JumpAtOnce = true }.PersonAirFor(new Cli([])).Squash);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-air-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { AirClipsOff = true, JumpAtOnce = true }.Save(path);
            string text = File.ReadAllText(path);
            foreach (var key in new[] { "airClipsOff", "jumpAtOnce" }) Assert.Contains($"\"{key}\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.AirClipsOff && back.JumpAtOnce);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_the_air_clips_off()
    {
        var head = new TapeHeader();
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.False(o.PersonAir.Any);
        head.Notes[PersonAirSettings.TapeNote] = new PersonAirSettings { Squash = false }.Note();
        var n = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, false), (n.PersonAir.Clips, n.PersonAir.Squash));
        head.Notes[PersonAirSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonAir.Any);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-air-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, List<string>? said = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = line => said?.Add(line),
    };

    /// <summary>
    /// Off the ledge of <c>drops-2</c> at a run (it stands 2.5 m from its edge, facing it), jump pressed on the way and twice on the ground below: a
    /// fall of 2 m, standing and running jumps, their landings.
    /// </summary>
    static IEnumerable<ActionFrame> Drive()
    {
        int all = 7 * World.TicksPerSecond;
        for (int t = 0; t < all; t++)
        {
            bool go = t < 4 * World.TicksPerSecond || t > 5 * World.TicksPerSecond;
            bool jump = t is 30 or 360 or 560;
            yield return new ActionFrame(go ? new Vector2(0f, 1f) : Vector2.Zero, Vector2.Zero, jump ? Buttons.Jump : Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
        }
    }

    [Fact]
    public void Without_the_clips_the_world_is_as_with_them_off_and_with_them_the_player_s_jump_leaves_after_its_squash()
    {
        var (course, game, models) = Needs();
        var said = new List<string>();
        var options = On(course, game, models, "drops-2", said);
        using var now = Session.Create(options);
        using var then = Session.Create(options with { PersonAir = PersonAirSettings.Off });
        var person = now.World.Service<CharacterAssets>().Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        Assert.False(then.World.TryStore<PersonAir>(out _));
        if (person.Air is null)
        {
            // On an older set: nothing of the earlier build's is in the world, said once, and it digests as the off switch gives it.
            Assert.False(now.World.TryStore<PersonAir>(out _));
            Assert.Single(said, l => l.Contains("no jumps, fall or landings"));
            int compared = 0;
            foreach (var frame in Drive())
            {
                now.Step(frame);
                then.Step(frame);
                if (now.Tick % 60 == 0) { Assert.Equal(then.Digest(), now.Digest()); compared++; }
            }
            output.WriteLine($"on an older set: {compared} digests the same as the air clips off's");
            Assert.True(compared > 5);
            return;
        }
        Assert.True(now.World.Has<PersonAir>(now.Player));
        Assert.DoesNotContain(said, l => l.Contains("no jumps, fall or landings"));
        // A newer set: the player's jump leaves its clip's take-off after the press; with --jump-at-once on the press, as before.
        using var atOnce = Session.Create(options with { PersonAir = new PersonAirSettings { Squash = false } });
        var takeoff = ((INotedClip)now.World.Service<CharacterAssets>().Clip(person.Air.Jump)).Notes.Airborne!.TakeoffS;
        long left = -1, leftAtOnce = -1, leftOff = -1, pressed = -1;
        var clips = new HashSet<string>();
        foreach (var frame in Drive())
        {
            if (pressed < 0 && (frame.Held & Buttons.Jump) != 0 && now.Tick > 500) pressed = now.Tick + 1;   // the standing jump (stopped since 4 s)
            now.Step(frame);
            atOnce.Step(frame);
            then.Step(frame);
            var anim = now.World.Get<CharacterAnimation>(now.Player);
            clips.Add(now.World.Service<CharacterAssets>().Clip(anim.Clip).Name);
            if (pressed > 0 && left < 0 && now.World.Get<Body>(now.Player).State != BodyState.Ground) left = now.Tick;
            if (pressed > 0 && leftAtOnce < 0 && atOnce.World.Get<Body>(atOnce.Player).State != BodyState.Ground) leftAtOnce = atOnce.Tick;
            if (pressed > 0 && leftOff < 0 && then.World.Get<Body>(then.Player).State != BodyState.Ground) leftOff = then.Tick;
        }
        output.WriteLine($"the jump pressed on tick {pressed}: off on {left} (its clip's take-off {takeoff:0.00} s), --jump-at-once on {leftAtOnce}, the air clips off's on {leftOff}; "
                         + "clips played: " + string.Join(", ", clips.Where(c => !c.Contains(':')).OrderBy(c => c, StringComparer.Ordinal)));
        Assert.Equal(pressed, leftOff);
        Assert.Equal(pressed, leftAtOnce);
        Assert.Equal(pressed + (long)MathF.Round(takeoff * World.TicksPerSecond), left);
        Assert.Contains("fall", clips);
        Assert.Contains(clips, c => c.StartsWith("land_", StringComparison.Ordinal));
    }

    /// <summary>
    /// With <c>VITOMIR_AIR_TAPES</c> (a folder): the tapes the stills are drawn from (<c>--replay-dump … --dump-stills … --dump-frame 3.2,90</c>),
    /// each with the clips (<c>…-now</c>) and with the earlier build's (<c>…-before</c>): a standing jump in the start square; a running jump in the free yard; a walk off
    /// <c>drops-1</c> (1 m); a walk off the back of <c>grab-2.6</c>'s top (2.6 m).
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_AIR_TAPES") is { Length: > 0 }, "VITOMIR_AIR_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_AIR_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static ActionFrame Frame(float ahead, Buttons buttons) => new(new Vector2(0f, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);
        static IEnumerable<ActionFrame> Stand()
        {
            for (int t = 0; t < 60; t++) yield return Frame(0f, Buttons.None);
            yield return Frame(0f, Buttons.Jump);
            for (int t = 0; t < 300; t++) yield return Frame(0f, Buttons.None);
        }
        static IEnumerable<ActionFrame> Run()
        {
            for (int t = 0; t < 240; t++) yield return Frame(1f, Buttons.None);
            yield return Frame(1f, Buttons.Jump);
            for (int t = 0; t < 180; t++) yield return Frame(1f, Buttons.None);
            for (int t = 0; t < 120; t++) yield return Frame(0f, Buttons.None);
        }
        static IEnumerable<ActionFrame> Walk(int ticks)
        {
            for (int t = 0; t < ticks; t++) yield return Frame(1f, Buttons.Walk);
            for (int t = 0; t < 240; t++) yield return Frame(0f, Buttons.None);
        }
        foreach (var (air, suffix) in new[] { (new PersonAirSettings(), "now"), (PersonAirSettings.Off, "before") })
            foreach (var (name, options, frames) in new[]
            {
                ("stand", On(course, game, models, "0"), Stand()),
                ("run", On(course, game, models, "free-yard"), Run()),
                ("drop1", On(course, game, models, "drops-1"), Walk(360)),
                ("drop26", On(course, game, models, "0") with { Place = "-20.6,37.5,2.62,0" }, Walk(240)),
            })
            {
                using var session = Session.Create(options with { PersonAir = air });
                var tape = new Tape { Header = session.TapeHeader(name) };
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

    [Fact]
    public void A_recording_notes_the_air_and_replays_on_it()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "drops-2") with { PersonAir = new PersonAirSettings { ContactSeconds = 0.05f } };
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in Drive())
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonAir.Note(), tape.Header.Notes[PersonAirSettings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonAir.Note(), again.PersonAir.Note());
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
}
