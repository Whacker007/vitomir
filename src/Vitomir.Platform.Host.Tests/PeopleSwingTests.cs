// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The swing bones' simulator as the program gives it (<see cref="PersonSwingSettings"/>: <c>swingOff</c>, <c>swingNear</c>,
/// <c>swingFar</c> and their flags; presentation only, not on a tape). On the proving ground with its townsfolk (skip without it, the 1.1 install
/// or the models repository's files): a tape made on the earlier build's (no simulator) replays digest for digest with it, and one made with it replays on the earlier build's;
/// the player's and the townsfolk's swing stepped 60 times a second near the eye; what it costs a tick.
/// </summary>
public sealed class PeopleSwingTests(ITestOutputHelper output)
{
    static string F(double v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_and_flags_give_the_swing_and_swing_off_turns_it_off()
    {
        var plain = new PlatformSettings();
        var s = plain.PersonSwingFor(new Cli([]));
        Assert.True(s.On);
        Assert.Equal((15f, 30f, 60, 30), (s.NearMetres, s.FarMetres, s.StepsPerSecond, s.FarStepsPerSecond));
        Assert.False(plain.PersonSwingFor(new Cli(["--swing-off"])).On);
        Assert.False(new PlatformSettings { SwingOff = true }.PersonSwingFor(new Cli([])).On);
        var near = plain.PersonSwingFor(new Cli(["--swing-near", "8", "--swing-far", "20"]));
        Assert.Equal((8f, 20f), (near.NearMetres, near.FarMetres));
        Assert.Equal(40f, new PlatformSettings { SwingNear = 40f, SwingFar = 20f }.PersonSwingFor(new Cli([])).FarMetres);   // the far no nearer than the near
        Assert.Equal(15f, new PlatformSettings { SwingNear = -3f }.PersonSwingFor(new Cli([])).NearMetres);                  // a setting out of its sense: the default
        Assert.Throws<SessionException>(() => plain.PersonSwingFor(new Cli(["--swing-near", "x"])));
        Assert.Equal(60, s.RateAt(3f));
        Assert.Equal(30, s.RateAt(20f));
        Assert.Equal(0, s.RateAt(31f));
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-swing-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { SwingOff = true, SwingNear = 9f, SwingFar = 25f }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"swingOff\": true", text);
            Assert.Contains("\"swingNear\": 9", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.SwingOff);
            Assert.Equal((9f, 25f), (back.SwingNear, back.SwingFar));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-swing-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, int people) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = people,
        Start = "start", Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { }, TownsfolkBody = TownsfolkBodies.Models,   // The models repository's crowd
    };

    /// <summary>Run ahead for <paramref name="seconds"/>, stand, turn, and go again a little.</summary>
    static IEnumerable<ActionFrame> Go(float seconds)
    {
        int go = (int)(seconds * World.TicksPerSecond);
        for (int t = 0; t < go + 3 * World.TicksPerSecond; t++)
        {
            bool on = t < go || (t > go + World.TicksPerSecond * 2 && t < go + World.TicksPerSecond * 2 + 60);
            var look = t > go && t < go + World.TicksPerSecond ? new Vector2(4f, 0f) : Vector2.Zero;
            yield return new ActionFrame(on ? new Vector2(0f, 1f) : Vector2.Zero, look, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
        }
    }

    [Fact]
    public void A_tape_without_the_swing_replays_digest_for_digest_with_it_and_the_other_way_round()
    {
        var (course, game, models) = Needs();
        foreach (var (made, played) in new[] { (PersonSwingSettings.Off, PersonSwingSettings.Default), (PersonSwingSettings.Default, PersonSwingSettings.Off) })
        {
            var options = On(course, game, models, 6) with { PersonSwing = made };
            Tape tape;
            using (var session = Session.Create(options))
            {
                Assert.Equal(made.On, session.World.TryService<PersonSwings>(out _));
                tape = new Tape { Header = session.TapeHeader("test") };
                foreach (var frame in Go(4f))
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
            }
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header) with { PersonSwing = played };
            Assert.Equal(TownsfolkBodies.Models, again.TownsfolkBody);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            string swung = replay.World.TryService<PersonSwings>(out var sw) ? $"; replayed with the swing: {sw.Steps} steps (the player's and the townsfolk's)" : "";
            output.WriteLine($"made {(made.On ? "with the swing" : "on the swing off's")}, replayed {(played.On ? "with it" : "on the swing off's")}: {compared} digests the same{swung}");
            Assert.True(compared >= 5);
            if (played.On) { Assert.NotNull(sw!.Live(replay.Player)); Assert.True(sw.Steps > tape.Frames.Count, "townsfolk swung as well as the player"); }
        }
    }

    /// <summary>
    /// The swing on and off side by side, every tick's digest the same, on the baked crowd and on the crowd built at run time on the
    /// bases (the path every baked person falls back to when its files cannot be read; its outfits' swing chains fitted at run time,
    /// a hood over a tunic). A run on the files parted only because its tape's replay gave the places other people (the
    /// crowd's note, <c>TownsfolkBodiesTests</c>), not because of the swing.
    /// </summary>
    [Fact]
    public void The_swing_on_and_off_side_by_side_give_the_same_digest_every_tick_on_the_baked_and_the_run_time_crowd()
    {
        var (course, game, models) = Needs();
        foreach (bool runTime in new[] { false, true })
        {
            var options = On(course, game, models, 6) with { CrowdBuilt = runTime };
            using var on = Session.Create(options with { PersonSwing = PersonSwingSettings.Default });
            using var off = Session.Create(options with { PersonSwing = PersonSwingSettings.Off });
            var swings = on.World.Service<PersonSwings>();
            var anims = on.World.Store<CharacterAnimation>();
            var assets = on.World.Service<CharacterAssets>();
            int swung = anims.Entities.ToArray().Count(e => assets.Kind(anims.Get(e).Kind).Person?.Swing is not null);
            int ticks = 0;
            Assert.Equal(off.Digest(), on.Digest());
            foreach (var frame in Go(4f))
            {
                on.Step(frame);
                off.Step(frame);
                ticks++;
                Assert.True(off.Digest() == on.Digest(), $"the swing changed the digest at tick {on.Tick} ({(runTime ? "the run-time crowd" : "the baked crowd")})");
            }
            output.WriteLine($"{(runTime ? "the run-time crowd" : "the baked crowd")}: {ticks} ticks, every digest the same; {swung} characters with swing chains, {swings.Steps} steps");
            Assert.True(swung >= 2 && swings.Steps > ticks);
        }
    }

    [Fact]
    public void What_the_swing_costs_a_tick_with_the_townsfolk()
    {
        var (course, game, models) = Needs();
        // The player standing among the proving ground's townsfolk (the platform scatters ten round the start), the camera behind him.
        var options = On(course, game, models, 10);
        using var with = Session.Create(options);
        using var without = Session.Create(options with { PersonSwing = PersonSwingSettings.Off });
        using var drawnUnkept = Session.Create(options with { PersonSwing = PersonSwingSettings.Default with { KeepOutBetweenSteps = false } });   // The earlier build's drawing between steps
        var swings = with.World.Service<PersonSwings>();
        var idle = new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
        for (int t = 0; t < 120; t++) { with.Step(idle); without.Step(idle); drawnUnkept.Step(idle); }   // past the first poses and each one's state
        var times = new List<double>[] { [], [], [] };
        int stepped = 0, held = 0, ticks = 600;
        long steps0 = swings.Steps;
        for (int t = 0; t < ticks; t++)
        {
            // Every fourth second the player turns and walks a little; the townsfolk go about.
            var frame = t % 480 < 120 ? new ActionFrame(new Vector2(0f, 1f), new Vector2(1f, 0f), Buttons.Walk, new Vector2(0.5f, 0.5f), 16f / 9f) : idle;
            // A tick and the picture's poses (every character's skin, as the picture asks for them).
            long a = Stopwatch.GetTimestamp();
            with.Step(frame);
            Drawn(with);
            long b = Stopwatch.GetTimestamp();
            without.Step(frame);
            Drawn(without);
            long c = Stopwatch.GetTimestamp();
            drawnUnkept.Step(frame);
            Drawn(drawnUnkept);
            long d = Stopwatch.GetTimestamp();
            times[0].Add(Stopwatch.GetElapsedTime(a, b).TotalMilliseconds);
            times[1].Add(Stopwatch.GetElapsedTime(b, c).TotalMilliseconds);
            times[2].Add(Stopwatch.GetElapsedTime(c, d).TotalMilliseconds);
            stepped += swings.LastTick.Stepped;
            held += swings.LastTick.Held;
        }
        static void Drawn(Session s)
        {
            var poses = s.World.Service<CharacterPoses>();
            foreach (var e in s.World.Store<CharacterAnimation>().Entities) _ = poses.Skin(s.World, e);
        }
        double Median(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
        double on = Median(times[0]), off = Median(times[1]), asUnkept = Median(times[2]);
        long steps = swings.Steps - steps0;
        // The step alone, timed on each swinging person's own state (a copy's worth of steps on its last body).
        int people = 0, bones = 0;
        double stepMs = 0;
        var poses = with.World.Service<CharacterPoses>();
        var anims = with.World.Store<CharacterAnimation>();
        foreach (var e in anims.Entities.ToArray())
        {
            if (swings.Live(e) is not { } live) continue;
            var body = poses.Bones(with.World, e).ToArray();
            var probe = live.Rule.NewState();
            live.Rule.Reset(probe, body);
            for (int k = 0; k < 20; k++) live.Rule.Step(probe, body, 1f / 60f, 0f);
            long a = Stopwatch.GetTimestamp();
            for (int k = 0; k < 200; k++) live.Rule.Step(probe, body, 1f / 60f, 0f);
            stepMs += Stopwatch.GetElapsedTime(a).TotalMilliseconds / 200;
            people++;
            bones += live.Rule.BoneCount;
        }
        output.WriteLine($"{people} people swung ({bones} bones), {F(stepped / (double)ticks, "0.0")} stepped a tick on average, {F(held / (double)ticks, "0.0")} held off by distance; "
                         + $"{steps} steps in {ticks} ticks ({F(steps / (ticks / 120.0), "0")} a second); a step {F(stepMs / Math.Max(1, people) * 1000, "0")} µs a person on average; "
                         + $"a tick and its poses: median {F(on)} ms with the swing, {F(off)} ms without ({F(on - off)} ms); drawn between steps drawn without the keep-out {F(asUnkept)} ms");
        foreach (var e in anims.Entities.ToArray())
        {
            var kind = with.World.Service<CharacterAssets>().Kind(anims.Get(e).Kind);
            var feet = with.World.Has<Body>(e) ? with.World.Get<Body>(e).Feet : default;
            output.WriteLine($"  {kind.Name}: swing {(kind.Person?.Swing is { } r ? $"{r.ChainCount} chains, {r.BoneCount} bones" : "none")}, {F(Vector3.Distance(feet, with.World.Get<Body>(with.Player).Feet), "0.0")} m from the player, live {swings.Live(e) is not null}");
        }
        Assert.True(people >= 2, "the player and some of the townsfolk wear swing chains");
        Assert.True(steps > 0);
    }
    /// <summary>
    /// With <c>VITOMIR_SWING_TAPES</c> (a folder): the tapes the stills are drawn from (<c>--replay-dump … --dump-stills … --dump-frame …</c>,
    /// with and without <c>--swing-off</c>; <c>--dump-of</c> for a townsperson): the woman (the townswoman's dress) walking, turning and stopping
    /// among the townsfolk; the player up the 0.25 m stair; the player sprinting, stopping and crouching on the flat.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_SWING_TAPES") is { Length: > 0 }, "VITOMIR_SWING_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_SWING_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static ActionFrame Frame(float ahead, float turn, Buttons buttons) => new(new Vector2(0f, ahead), new Vector2(turn, 0f), buttons, new Vector2(0.5f, 0.5f), 16f / 9f);
        IEnumerable<ActionFrame> Woman()
        {
            for (int t = 0; t < 240; t++) yield return Frame(1f, 0f, Buttons.Walk);       // walk 2 s
            for (int t = 0; t < 120; t++) yield return Frame(1f, 3f, Buttons.Walk);       // walk turning 1 s
            for (int t = 0; t < 180; t++) yield return Frame(0f, 0f, Buttons.None);       // stop 1.5 s
            for (int t = 0; t < 120; t++) yield return Frame(0f, 6f, Buttons.None);       // turn on the spot 1 s
            for (int t = 0; t < 120; t++) yield return Frame(1f, 0f, Buttons.None);       // run 1 s
            for (int t = 0; t < 120; t++) yield return Frame(0f, 0f, Buttons.None);       // stop
        }
        IEnumerable<ActionFrame> Stairs()
        {
            for (int t = 0; t < 1200; t++) yield return Frame(1f, 0f, Buttons.Walk);      // walk to the flight and up it, 10 s
            for (int t = 0; t < 120; t++) yield return Frame(0f, 0f, Buttons.None);
        }
        IEnumerable<ActionFrame> Flat()
        {
            for (int t = 0; t < 300; t++) yield return Frame(1f, 0f, Buttons.Sprint);     // sprint 2.5 s
            for (int t = 0; t < 120; t++) yield return Frame(0f, 0f, Buttons.None);       // stop
            for (int t = 0; t < 240; t++) yield return Frame(0f, 0f, Buttons.Crouch);     // crouch 2 s
            for (int t = 0; t < 120; t++) yield return Frame(1f, 0f, Buttons.Crouch);     // crouched walk 1 s
            for (int t = 0; t < 120; t++) yield return Frame(0f, 0f, Buttons.None);       // up
        }
        foreach (var (name, options, frames) in new[]
        {
            ("woman", On(course, game, models, 6) with { Body = PeopleBodies.Woman }, Woman()),
            ("stairs", On(course, game, models, 0) with { Start = "stairs-0.25" }, Stairs()),
            ("flat", On(course, game, models, 0), Flat()),
        })
        {
            using var session = Session.Create(options);
            var tape = new Tape { Header = session.TapeHeader(name) };
            foreach (var frame in frames)
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
            string path = Path.Combine(dir, name + ".tape");
            tape.Write(path);
            output.WriteLine($"{path}: {tape.Frames.Count} ticks");
        }
    }
}
