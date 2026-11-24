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
/// As the program gives it: from the catch straight into the pull-up when the player holds on, the pull-up at <c>pullUpRate</c>
/// going on (<c>climbWaitOut</c> / <c>--climb-wait-out</c> for the earlier build's; the note <c>commit</c>'s <c>climb</c> and <c>pull</c>, none: the earlier build's). On the proving ground
/// (skip without it, the 1.1 install or the models repository's files): a running catch at the 2.2 m lane with forward held, a falling catch at the 2.6 m lane
/// with use and forward held, and with nothing held then a press; each timed from the log (the hands' contact, the pull-up's start, standing), both builds;
/// and a recording replays digest for digest.
/// </summary>
public sealed class PeopleClimbTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_straight_climb_and_the_wait_out_switch_gives_the_wait()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonCommitFor(new Cli([]));
        Assert.Equal((true, 1.3f), (on.Climb, on.PullUpRate));
        Assert.Equal((true, 1.3f), (on.Rules().Climb, on.Rules().PullUpRate));
        Assert.EndsWith(" v=2 climb=1 pull=1.3", on.Note());
        var cz = plain.PersonCommitFor(new Cli(["--climb-wait-out"]));
        Assert.False(cz.Climb);
        Assert.EndsWith(" v=2", cz.Note());
        Assert.Equal(cz, new PlatformSettings { ClimbWaitOut = true }.PersonCommitFor(new Cli([])));
        Assert.False(plain.PersonCommitFor(new Cli(["--commit-rules-1"])).Climb);   // The earlier build's had none
        Assert.Equal(1.6f, new PlatformSettings { PullUpRate = 1.6f }.PersonCommitFor(new Cli([])).PullUpRate);
        Assert.Equal(1.3f, new PlatformSettings { PullUpRate = 5f }.PersonCommitFor(new Cli([])).PullUpRate);   // out of 1 to 2: the default
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-climb-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { ClimbWaitOut = true, PullUpRate = 1.5f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"climbWaitOut\": true", text);
            Assert.Contains("\"pullUpRate\": 1.5", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.ClimbWaitOut);
            Assert.Equal(1.5f, back.PullUpRate);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_s_commit_note_without_climb_waits_the_grab_out()
    {
        var head = new TapeHeader();
        head.Notes[PersonCommitSettings.TapeNote] = "on=1 lead=0.3 auto=1 cone=60 arms=0.12 brace=0.45 v=2";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 2, false), (o.PersonCommit.On, o.PersonCommit.Version, o.PersonCommit.Climb));
        head.Notes[PersonCommitSettings.TapeNote] = (new PersonCommitSettings() with { PullUpRate = 1.45f }).Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 1.45f), (o.PersonCommit.Climb, o.PersonCommit.PullUpRate));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-climb-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string? place = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Place = place, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static SessionOptions WaitingOut(SessionOptions o) => o with { PersonCommit = o.PersonCommit.WaitOutClimb };

    /// <summary>Skips without the commit point (the models repository's files: no ledge clips, which it rides on).</summary>
    static void NeedsCommit(SessionOptions options)
    {
        using var s = Session.Create(options);
        Assert.SkipUnless(s.World.TryStore<PersonCommit>(out _), "the models repository's files are too old (no ledge clips: no commit point)");
    }

    static ActionFrame Frame(float ahead, Buttons buttons) => new(new Vector2(0f, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>A place <paramref name="ahead"/> m ahead of a start's feet and <paramref name="up"/> m up, facing as it faces.</summary>
    static string Ahead(SessionOptions options, float ahead, float up)
    {
        using var probe = Session.Create(options with { Say = _ => { } });
        var at = probe.World.Get<Body>(probe.Player).Feet;
        float yaw = probe.World.Get<Body>(probe.Player).Yaw;
        var (sn, cs) = DetMath.SinCos(yaw);
        var p = at + new Vector3(cs, sn, 0f) * ahead + new Vector3(0f, 0f, up);
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###},{p.Y:0.###},{p.Z:0.###},{yaw * 180f / MathF.PI:0.#}");
    }

    /// <summary>At a grab lane: a run at the wall, jump pressed 1.35 m short of its face, forward held throughout (on the ground, in the air, on the wall).</summary>
    static IEnumerable<ActionFrame> RunHeld(Session s)
    {
        var from = s.World.Get<Body>(s.Player).Feet;
        bool pressed = false;
        for (int t = 0; t < 6 * World.TicksPerSecond; t++)
        {
            var b = s.World.Get<Body>(s.Player);
            bool now = !pressed && b.State == BodyState.Ground && b.Feet.X - from.X >= 8f - 1.35f;
            pressed |= now;
            yield return Frame(1f, now ? Buttons.Jump : Buttons.None);
        }
    }

    /// <summary>Put in the air before a lane's wall: use held; forward held throughout (<paramref name="forward"/>), or from tick <paramref name="pressAt"/> on.</summary>
    static IEnumerable<ActionFrame> FallUse(bool forward, int pressAt = int.MaxValue, int ticks = 6 * World.TicksPerSecond)
    {
        for (int t = 0; t < ticks; t++) yield return Frame(forward || t >= pressAt ? 1f : 0f, Buttons.Use);
    }

    /// <summary>A fall at the 2.6 m lane's wall: put 0.5 m from its face, 1.4 m up.</summary>
    static SessionOptions Fall26(string course, string game, string models)
    {
        var o = On(course, game, models, "grab-2.6");
        return o with { Place = Ahead(o, 8f - 0.5f, 1.4f) };
    }

    /// <summary>The log's seconds of the hands' contact (<c>a lip held</c>), the pull-up's start and its end (NaN for none).</summary>
    static (float Contact, float PullUp, float Stand, List<string> Said) Timed(SessionOptions options, Func<Session, IEnumerable<ActionFrame>> frames)
    {
        var said = new List<string>();
        using (var s = Session.Create(options with { Say = said.Add }))
            foreach (var frame in frames(s)) s.Step(frame);
        static float At(List<string> said, string what)
        {
            var line = said.FirstOrDefault(l => l.Contains(what, StringComparison.Ordinal));
            return line is null ? float.NaN : float.Parse(line[..line.IndexOf(" s:", StringComparison.Ordinal)], CultureInfo.InvariantCulture);
        }
        return (At(said, "move: a lip held"), At(said, "move: pull up begins"), At(said, "move: pull up ends"), said);
    }

    void Say(string name, string build, (float Contact, float PullUp, float Stand, List<string> Said) t) =>
        output.WriteLine($"{name}, {build}: the pull-up {F(t.PullUp - t.Contact)} s after the hands' contact, standing {F(t.Stand - t.Contact)} s after it");

    [Fact]
    public void On_the_proving_ground_a_catch_held_on_goes_straight_into_the_pull_up()
    {
        var (course, game, models) = Needs();
        var run = On(course, game, models, "grab-2.2");
        NeedsCommit(run);
        var fall = Fall26(course, game, models);
        foreach (var (name, options, frames) in new (string, SessionOptions, Func<Session, IEnumerable<ActionFrame>>)[]
                 {
                     ("a running catch at 2.2 m, forward held", run, RunHeld),
                     ("a falling catch at 2.6 m, use and forward held", fall, _ => FallUse(forward: true)),
                 })
        {
            var da = Timed(options, frames);
            var cz = Timed(WaitingOut(options), frames);
            Say(name, "straight on", da);
            Say(name, "waited out", cz);
            Assert.InRange(da.PullUp - da.Contact, 0.08f, 0.2f);
            Assert.True(da.Stand - da.Contact < 1.45f);
            Assert.True(cz.PullUp - cz.Contact > 0.6f);
        }
        // Nothing held: the hang; forward pressed 2.5 s in pulls up on that tick.
        var still = Timed(fall, _ => FallUse(forward: false, pressAt: 300));
        Say("a falling catch at 2.6 m, use held, forward pressed 2.5 s in", "straight on", still);
        Assert.Contains(still.Said, l => l.Contains("move: hang begins", StringComparison.Ordinal));
        Assert.InRange(still.PullUp, 2.49f, 2.52f);
    }

    [Fact]
    public void A_recording_notes_the_climb_and_replays_on_it_and_one_waited_out_on_the_wait()
    {
        var (course, game, models) = Needs();
        var run = On(course, game, models, "grab-2.2");
        foreach (var options in new[] { run, WaitingOut(run) })
        {
            Tape tape;
            using (var session = Session.Create(options))
            {
                tape = new Tape { Header = session.TapeHeader("test") };
                foreach (var frame in RunHeld(session))
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
            }
            Assert.Equal(options.PersonCommit.Note(), tape.Header.Notes[PersonCommitSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonCommit, again.PersonCommit);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonCommitSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 10);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_CLIMB_TAPES</c> (a folder): the tapes the stills are drawn from, each with the earlier build's (<c>…-now</c>) and the earlier build's (<c>…-before</c>), and
    /// beside each the ticks to draw (<c>.ticks</c>) and its log: a running catch at the 2.2 m lane with forward held (<c>run22</c>); a falling catch at the
    /// 2.6 m lane with use and forward held (<c>fall26</c>); the same with use alone, forward pressed 2.5 s in (<c>fall26press</c>).
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_CLIMB_TAPES") is { Length: > 0 }, "VITOMIR_CLIMB_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_CLIMB_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var fall = Fall26(course, game, models);
        var cases = new (string Name, SessionOptions Options, Func<Session, IEnumerable<ActionFrame>> Frames)[]
        {
            ("run22", On(course, game, models, "grab-2.2"), RunHeld),
            ("fall26", fall, _ => FallUse(forward: true)),
            ("fall26press", fall, _ => FallUse(forward: false, pressAt: 300)),
        };
        foreach (var (name, options, frames) in cases)
            foreach (var (o, suffix) in new[] { (options, "now"), (WaitingOut(options), "before") })
            {
                var said = new List<string>();
                using var session = Session.Create(o with { Say = said.Add });
                var assets = session.World.Service<CharacterAssets>();
                var tape = new Tape { Header = session.TapeHeader(name) };
                var marks = new List<(int Tick, string What)>();
                string lastClip = "";
                foreach (var frame in frames(session))
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                    int tick = (int)session.Tick;
                    string clip = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                    if (session.World.Events<LedgeGrabbed>().ThisTick.Count > 0) marks.Add((tick, "contact"));
                    if (clip != lastClip && (clip.StartsWith("grab_", StringComparison.Ordinal) || clip.StartsWith("hang_", StringComparison.Ordinal)
                                             || clip.StartsWith("pull_up", StringComparison.Ordinal) || clip == "stand"))
                        marks.Add((tick, clip));
                    lastClip = clip;
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
                File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
                output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
            }
    }
}
