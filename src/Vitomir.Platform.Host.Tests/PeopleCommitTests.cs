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
/// As the program gives it (from play): the commit point in the air (<c>commitOff</c>, <c>--commit-off</c>;
/// <c>commitLead</c>, <c>autoCatch</c>, <c>autoCatchCone</c>, <c>catchArmReach</c>, <c>braceOverlap</c>; the note <c>commit</c>) and a short fall's prelanding
/// (<c>prelandingShort</c>, <c>prelandingCutoff</c>, <c>--prelanding-short</c>; the note <c>prelanding</c>'s <c>short</c>, <c>cutoff</c>, <c>real</c>). A tape without
/// them is the earlier build's. On the proving ground (skip without it, the 1.1 install or the models repository's files): a tapped jump at the 2.6 m lip is caught on the
/// grab's clip with no stand-in (the motor's own grab), only the player is given it, and a recording replays digest for digest.
/// </summary>
public sealed class PeopleCommitTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_and_flags_give_the_commit_point_and_the_commit_off_switch_turns_it_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonCommitFor(new Cli([]));
        Assert.True(on.On);
        Assert.Equal((0.3f, true, 60f, 0.12f, 0.45f), (on.LeadSeconds, on.AutoCatch, on.AutoConeDegrees, on.ArmReach, on.BraceOverlap));   // the cone 60° (the earlier build's 30°)
        Assert.False(plain.PersonCommitFor(new Cli(["--commit-off"])).Any);
        Assert.False(plain.PersonCommitFor(new Cli(["--ledge-clips-off"])).Any);   // it rides on the ledge's clips
        Assert.False(plain.PersonCommitFor(new Cli(["--over-clips-off"])).Any);
        Assert.False(new PlatformSettings { CommitOff = true }.PersonCommitFor(new Cli([])).Any);
        Assert.Equal(0.25f, plain.PersonCommitFor(new Cli(["--commit-lead", "0.25"])).LeadSeconds);
        Assert.Throws<SessionException>(() => plain.PersonCommitFor(new Cli(["--commit-lead", "2"])));
        Assert.False(new PlatformSettings { AutoCatch = false }.PersonCommitFor(new Cli([])).AutoCatch);
        // The prelanding's short falls: the cutoff by default, part way on asking, the earlier build's with the switch; the fade in game time.
        var pre = plain.PersonPrelandingFor(new Cli([]));
        Assert.Equal((ShortFall.Cutoff, 0.2f, true), (pre.Short, pre.CutoffSeconds, pre.RealFade));
        Assert.Equal(ShortFall.Partway, plain.PersonPrelandingFor(new Cli(["--prelanding-short", "partway"])).Short);
        Assert.Equal(ShortFall.Partway, new PlatformSettings { PrelandingShort = "partway" }.PersonPrelandingFor(new Cli([])).Short);
        Assert.Equal(0.15f, new PlatformSettings { PrelandingCutoff = 0.15f }.PersonPrelandingFor(new Cli([])).CutoffSeconds);
        Assert.Throws<SessionException>(() => plain.PersonPrelandingFor(new Cli(["--prelanding-short", "sometimes"])));
        var old = plain.PersonPrelandingFor(new Cli(["--commit-off"]));
        Assert.Equal((ShortFall.Faster, false), (old.Short, old.RealFade));
        Assert.Equal("clips=1 from=0.13", old.Note());
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-commit-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { CommitOff = true, CommitLead = 0.25f, PrelandingShort = "partway" }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"commitOff\": true", text);
            Assert.Contains("\"commitLead\": 0.25", text);
            Assert.Contains("\"prelandingShort\": \"partway\"", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.CommitOff);
            Assert.Equal(0.25f, back.CommitLead);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_notes_has_no_commit_point()
    {
        var head = new TapeHeader();
        head.Notes[PersonPrelandingSettings.TapeNote] = "clips=1 from=0.13";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.False(o.PersonCommit.Any);
        Assert.Equal((ShortFall.Faster, false), (o.PersonPrelanding.Short, o.PersonPrelanding.RealFade));
        head.Notes[PersonCommitSettings.TapeNote] = new PersonCommitSettings().Note();
        head.Notes[PersonPrelandingSettings.TapeNote] = new PersonPrelandingSettings().Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(o.PersonCommit.On);
        Assert.Equal((ShortFall.Cutoff, true), (o.PersonPrelanding.Short, o.PersonPrelanding.RealFade));
        head.Notes[PersonCommitSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonCommit.Any);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-commit-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, List<string>? said = null, int people = 0) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = people,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = line => said?.Add(line),
    };

    /// <summary>The commit point off and the prelanding as before.</summary>
    static SessionOptions WithCommitOff(SessionOptions o) => o with { PersonCommit = PersonCommitSettings.Off, PersonPrelanding = o.PersonPrelanding.WithoutCommit };

    static ActionFrame Frame(float ahead, Buttons buttons, float right = 0f) => new(new Vector2(right, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>A place (the session's <c>Place</c>) <paramref name="ahead"/> m ahead of a start's feet and <paramref name="up"/> m up, facing as it faces turned by <paramref name="turn"/>°.</summary>
    static string Ahead(SessionOptions options, float ahead, float up, float turn = 0f)
    {
        using var probe = Session.Create(options with { Say = _ => { } });
        var at = probe.World.Get<Body>(probe.Player).Feet;
        float yaw = probe.World.Get<Body>(probe.Player).Yaw;
        var (sn, cs) = DetMath.SinCos(yaw);
        var p = at + new Vector3(cs, sn, 0f) * ahead + new Vector3(0f, 0f, up);
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###},{p.Y:0.###},{p.Z:0.###},{yaw * 180f / MathF.PI + turn:0.#}");
    }

    /// <summary>At a grab lane (its start 8 m before the wall, facing it): walked to within about 0.55 m of the face, stopped, jump tapped; then still.</summary>
    static IEnumerable<ActionFrame> WalkAndJump(Session s, Buttons held = Buttons.None)
    {
        var from = s.World.Get<Body>(s.Player).Feet;
        int t = 0;
        while (s.World.Get<Body>(s.Player).Feet.X - from.X < 8f - 0.55f - 0.3f && t++ < 12 * World.TicksPerSecond) yield return Frame(1f, Buttons.Walk);
        for (int i = 0; i < 60; i++) yield return Frame(0f, Buttons.None);
        yield return Frame(0f, Buttons.Jump | held);
        for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, held);
    }

    /// <summary>At a grab lane: a run at the wall (forward held on the ground, through the jump's squash, as a player runs and jumps), jump pressed
    /// <paramref name="press"/> m short of its face; nothing held in the air and after.</summary>
    static IEnumerable<ActionFrame> RunAndJump(Session s, float press, Buttons held = Buttons.None)
    {
        var from = s.World.Get<Body>(s.Player).Feet;
        bool pressed = false, left = false;
        for (int t = 0; t < 4 * World.TicksPerSecond; t++)
        {
            var b = s.World.Get<Body>(s.Player);
            bool now = !pressed && b.State == BodyState.Ground && b.Feet.X - from.X >= 8f - press;
            pressed |= now;
            left |= pressed && b.State != BodyState.Ground;
            yield return Frame(!left ? 1f : 0f, (now ? Buttons.Jump : Buttons.None) | held);
        }
        for (int i = 0; i < World.TicksPerSecond; i++) yield return Frame(0f, held);
    }

    /// <summary>Still for <paramref name="ticks"/> ticks (a body placed in the air falls), <paramref name="held"/> held.</summary>
    static IEnumerable<ActionFrame> Still(int ticks, Buttons held = Buttons.None)
    {
        for (int i = 0; i < ticks; i++) yield return Frame(0f, held);
    }

    /// <summary>The clips the player played, in turn (each once in a row), and whether the motor's own stand-in grab carried it.</summary>
    static (List<string> Clips, bool StandIn) Played(Session s, IEnumerable<ActionFrame> frames)
    {
        var assets = s.World.Service<CharacterAssets>();
        var clips = new List<string>();
        bool standIn = false;
        foreach (var frame in frames)
        {
            s.Step(frame);
            var name = assets.Clip(s.World.Get<CharacterAnimation>(s.Player).Clip).Name;
            if (clips.Count == 0 || clips[^1] != name) clips.Add(name);
            bool ledgeClip = s.World.TryStore<PersonLedge>(out var ls) && ls.Has(s.Player) && ls.Get(s.Player).Move.Clip > 0;
            standIn |= s.World.Get<Traversal>(s.Player).Kind == TraversalKind.Grab && !ledgeClip;
        }
        return (clips, standIn);
    }

    [Fact]
    public void A_jump_at_the_2_6_m_lip_is_caught_on_its_clip_and_only_the_player_has_the_commit_point()
    {
        var (course, game, models) = Needs();
        var said = new List<string>();
        var options = On(course, game, models, "grab-2.6", said, people: 6);
        using var now = Session.Create(options);
        var person = now.World.Service<CharacterAssets>().Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        if (person.Ledge is null)
        {
            // On an older set: no ledge, no commit point (it rides on the ledge's clips).
            Assert.False(now.World.TryStore<PersonCommit>(out _));
            return;
        }
        Assert.True(now.World.TryStore<PersonCommit>(out var commits));
        Assert.Equal(1, commits!.Count);   // the player only (the townsfolk neither jump nor grab)
        Assert.True(now.World.Has<PersonCommit>(now.Player));
        var (clips, standIn) = Played(now, WalkAndJump(now));
        output.WriteLine("the commit point: " + string.Join(", ", clips.Where(c => !c.Contains(':'))));
        Assert.Contains(clips, c => c.StartsWith("grab_jump_up", StringComparison.Ordinal));   // its braced twin at a wall
        Assert.False(standIn, "no stand-in grab");
        Assert.Contains(said, l => l.Contains("air: commit: catch the lip at 2.60 m", StringComparison.Ordinal));
        using var then = Session.Create(WithCommitOff(options));
        Assert.False(then.World.TryStore<PersonCommit>(out _));
        var (old, oldStandIn) = Played(then, WalkAndJump(then));
        output.WriteLine("off: " + string.Join(", ", old.Where(c => !c.Contains(':'))));
        Assert.True(oldStandIn, "the commit point off's stand-in grab from the air");
    }

    [Fact]
    public void A_recording_notes_the_commit_point_and_replays_on_it()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "grab-2.8");
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in RunAndJump(session, 1.6f, Buttons.Use))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonCommit.Note(), tape.Header.Notes[PersonCommitSettings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonCommit.Note(), again.PersonCommit.Note());
        Assert.Equal(options.PersonPrelanding.Note(), again.PersonPrelanding.Note());
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
    /// With <c>VITOMIR_COMMIT_TAPES</c> (a folder): the tapes the stills are drawn from, each with the earlier build's (<c>…-now</c>) and the earlier build's (<c>…-before</c>), and beside each
    /// tape the ticks to draw (<c>.ticks</c>: the commit, the clip's start, the hands' contact, the hang, a moment after; or the landing): a running jump at the
    /// 2.2 m lip pressed 1.35 m short (rising, braced) and 2.0 m short (reaching the wall past its top: a slow falling catch); standing jumps at the 2.6 and
    /// 2.8 m lips; a jump across the 1.0 m gap; falling past the 2.8 m lip, use not held and held; a hop down of 0.15 m (both settings).
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_COMMIT_TAPES") is { Length: > 0 }, "VITOMIR_COMMIT_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_COMMIT_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static IEnumerable<ActionFrame> Gap(Session s)
        {
            var from = s.World.Get<Body>(s.Player).Feet;
            bool pressed = false;
            for (int t = 0; t < 5 * World.TicksPerSecond; t++)
            {
                var b = s.World.Get<Body>(s.Player);
                // The gap's edge 2.5 m ahead of the start: pressed 0.25 m short of it.
                bool now = !pressed && b.State == BodyState.Ground && (b.Feet - from).Length() >= 2.25f;
                pressed |= now;
                yield return Frame(t < 3 * World.TicksPerSecond ? 1f : 0f, now ? Buttons.Jump : Buttons.None);
            }
        }
        var cases = new List<(string Name, SessionOptions Options, Func<Session, IEnumerable<ActionFrame>> Frames, bool Both)>
        {
            ("run22", On(course, game, models, "grab-2.2"), s => RunAndJump(s, 1.35f), true),
            ("run22late", On(course, game, models, "grab-2.2"), s => RunAndJump(s, 2.0f), true),
            ("stand26", On(course, game, models, "grab-2.6"), s => WalkAndJump(s), true),
            ("stand28", On(course, game, models, "grab-2.8"), s => WalkAndJump(s), true),
            ("gap", On(course, game, models, "gaps-1.0"), Gap, true),
        };
        var fall = On(course, game, models, "grab-2.8");
        fall = fall with { Place = Ahead(fall, 8f - 0.5f, 1.6f) };
        cases.Add(("fall28", fall, s => Still(4 * World.TicksPerSecond), true));
        cases.Add(("fall28use", fall, s => Still(4 * World.TicksPerSecond, Buttons.Use), false));
        var hop = On(course, game, models, "0");
        hop = hop with { Place = Ahead(hop, 1f, 0.15f) };
        cases.Add(("hop015", hop, s => Still(2 * World.TicksPerSecond), true));
        cases.Add(("hop015partway", hop with { PersonPrelanding = hop.PersonPrelanding with { Short = ShortFall.Partway } }, s => Still(2 * World.TicksPerSecond), false));
        foreach (var (name, options, frames, both) in cases)
            foreach (var (o, suffix) in both ? new[] { (options, "now"), (WithCommitOff(options), "before") } : new[] { (options, "now") })
            {
                var said = new List<string>();
                using var session = Session.Create(o with { Say = said.Add });
                var assets = session.World.Service<CharacterAssets>();
                var tape = new Tape { Header = session.TapeHeader(name) };
                var marks = new List<(int Tick, string What)>();
                string lastClip = "";
                var lastState = BodyState.Ground;
                foreach (var frame in frames(session))
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                    int tick = (int)session.Tick;
                    var body = session.World.Get<Body>(session.Player);
                    string clip = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                    var events = session.World.Events<AirCommitted>().ThisTick;
                    for (int i = 0; i < events.Count; i++) if (events[i].Entity == session.Player) marks.Add((tick, "commit-" + events[i].Outcome));
                    if (session.World.Events<LedgeGrabbed>().ThisTick.Count > 0) marks.Add((tick, "contact"));
                    if (clip != lastClip && (clip.StartsWith("grab_", StringComparison.Ordinal) || clip.StartsWith("hang_", StringComparison.Ordinal)
                                             || clip.StartsWith("prelanding_", StringComparison.Ordinal) || clip.StartsWith("land", StringComparison.Ordinal)
                                             || clip.Contains("Grab", StringComparison.Ordinal)))
                        marks.Add((tick, clip));
                    if (body.State == BodyState.Ground && lastState == BodyState.Air) marks.Add((tick, "landed"));
                    lastClip = clip;
                    lastState = body.State;
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
                File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
                output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
            }
    }
}
