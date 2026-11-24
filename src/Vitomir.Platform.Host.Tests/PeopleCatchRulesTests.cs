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
/// As the program gives it: the commit point's rules version 2 (the cone 60°, a jump's descent catching the lips under its
/// take-off, the catch at full reach at the wall past the arc's top, the catch decided whole at its commit), <c>commitRules1</c> / <c>--commit-rules-1</c> for the earlier build's,
/// the note <c>commit</c>'s <c>v</c> (none: the earlier build's). On the proving ground (skip without it, the 1.1 install or the models repository's files), a play log's cases:
/// a jump off the 2.8 m top at the 2.6 m block's lip, a running jump at the 2.8 m lane left about 1.6 m out, his 43.02 s catch at the 2.8 m block's
/// corner; and a recording replays digest for digest.
/// </summary>
public sealed class PeopleCatchRulesTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_commit_point_s_rules_2_and_the_rules_1_switch_gives_rules_1()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonCommitFor(new Cli([]));
        Assert.Equal((2, 60f), (on.Version, on.AutoConeDegrees));
        Assert.Equal((2, 60f), (on.Rules().Version, on.Rules().AutoCone));
        Assert.Contains(" v=2", on.Note());
        var cy = plain.PersonCommitFor(new Cli(["--commit-rules-1"]));
        Assert.Equal((true, 1, 30f), (cy.On, cy.Version, cy.AutoConeDegrees));
        Assert.DoesNotContain("v=", cy.Note());
        Assert.Equal(cy, new PlatformSettings { CommitRules1 = true }.PersonCommitFor(new Cli([])));
        Assert.False(plain.PersonCommitFor(new Cli(["--commit-rules-1", "--commit-off"])).Any);   // The earlier build's wins
        Assert.Equal(45f, new PlatformSettings { AutoCatchCone = 45f }.PersonCommitFor(new Cli([])).AutoConeDegrees);   // the setting stays
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-catch-rules-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { CommitRules1 = true }.Save(path);
            Assert.Contains("\"commitRules1\": true", File.ReadAllText(path));
            Assert.True(PlatformSettings.Load(path).CommitRules1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_s_commit_note_without_v_is_rules_1()
    {
        var head = new TapeHeader();
        head.Notes[PersonCommitSettings.TapeNote] = "on=1 lead=0.3 auto=1 cone=30 arms=0.12 brace=0.45";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 1, 30f), (o.PersonCommit.On, o.PersonCommit.Version, o.PersonCommit.AutoConeDegrees));
        head.Notes[PersonCommitSettings.TapeNote] = new PersonCommitSettings().Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((2, 60f), (o.PersonCommit.Version, o.PersonCommit.AutoConeDegrees));
        Assert.Equal(new PersonCommitSettings().Note(), o.PersonCommit.Note());
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-catch-rules-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string? place = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Place = place, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
        PersonGaits = Animation.People.PersonGaitSettings.Off,   // climbing is measured on the earlier build's paces until its rework (the paces' approaches)
    };

    static SessionOptions OnRules1(SessionOptions o) => o with { PersonCommit = o.PersonCommit.AsRules1 };

    /// <summary>Skips without the commit point (the models repository's files: no ledge clips, which it rides on).</summary>
    static void NeedsCommit(SessionOptions options)
    {
        using var s = Session.Create(options);
        Assert.SkipUnless(s.World.TryStore<PersonCommit>(out _), "the models repository's files are too old (no ledge clips: no commit point)");
    }

    static ActionFrame Frame(float ahead, Buttons buttons) => new(new Vector2(0f, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>
    /// A run ahead (forward held on the ground), jump pressed the first tick on the ground that <paramref name="press"/> says (the body's feet); in the air forward
    /// held or not (<paramref name="release"/>), <paramref name="held"/> held throughout; <paramref name="ticks"/> in all.
    /// </summary>
    static IEnumerable<ActionFrame> RunAndJump(Session s, Func<Vector3, bool> press, bool release, Buttons held = Buttons.None, int ticks = 5 * World.TicksPerSecond)
    {
        bool pressed = false, left = false;
        for (int t = 0; t < ticks; t++)
        {
            var b = s.World.Get<Body>(s.Player);
            bool now = !pressed && b.State == BodyState.Ground && press(b.Feet);
            pressed |= now;
            left |= pressed && b.State != BodyState.Ground;
            yield return Frame(left && release ? 0f : 1f, (now ? Buttons.Jump : Buttons.None) | held);
        }
    }

    /// <summary>What a run of <paramref name="frames"/> said of its jump, its commits, its moves and its landing (in turn), and where it left the ground.</summary>
    static (List<string> Said, Vector3 Off) Run(SessionOptions options, Func<Session, IEnumerable<ActionFrame>> frames)
    {
        var said = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var off = new Vector3(float.NaN);
        var last = BodyState.Ground;
        foreach (var frame in frames(s))
        {
            s.Step(frame);
            var b = s.World.Get<Body>(s.Player);
            if (last == BodyState.Ground && b.State == BodyState.Air && float.IsNaN(off.X) && s.Tick > 10) off = b.Feet;   // not the start's settling
            last = b.State;
        }
        return (said.Where(l => l.Contains(" jump: ", StringComparison.Ordinal) || l.Contains(" air: ", StringComparison.Ordinal)
                                || l.Contains(" move: ", StringComparison.Ordinal) || l.Contains(" land: ", StringComparison.Ordinal)).ToList(), off);
    }

    static bool Grabbed(List<string> said) => said.Any(l => l.Contains("move: grab begins", StringComparison.Ordinal));

    static bool Lapsed(List<string> said) =>
        said.Any(l => l.Contains("air: commit: catch", StringComparison.Ordinal)) && !Grabbed(said) && said.Any(l => l.Contains("air: commit: land", StringComparison.Ordinal));

    static string Short(List<string> said) => string.Join(" | ", said.Take(4));

    /// <summary>From the 2.8 m block's top (its south edge at y 41.5), a run south and a tap 0.6 m before the edge: at the 2.6 m block's north lip 3 m across.</summary>
    static SessionOptions OffTheTop(string course, string game, string models) => On(course, game, models, "grab-2.8", "-20,43.3,2.8,-90");

    static IEnumerable<ActionFrame> OffTheTopFrames(Session s, Buttons held = Buttons.None) => RunAndJump(s, f => f.Y <= 41.5f + 0.6f, release: true, held);

    [Fact]
    public void A_jump_off_the_2_8_m_top_catches_the_2_6_m_block_s_lip_on_the_way_down()
    {
        var (course, game, models) = Needs();
        var options = OffTheTop(course, game, models);
        NeedsCommit(options);
        foreach (var use in new[] { false, true })
        {
            var held = use ? Buttons.Use : Buttons.None;
            var (cz, _) = Run(options, s => OffTheTopFrames(s, held));
            var (cy, _) = Run(OnRules1(options), s => OffTheTopFrames(s, held));
            output.WriteLine($"use {(use ? "held" : "not held")}: rules 2 {Short(cz)}");
            output.WriteLine($"use {(use ? "held" : "not held")}: rules 1 {Short(cy)}");
            // Jumped at it (square): by itself, and with use held; the earlier build's took every lip at or under the take-off for the jump's own moves'.
            Assert.Contains(cz, l => l.Contains("air: commit: catch the lip at 2.60 m falling (jumped at it)", StringComparison.Ordinal));
            Assert.True(Grabbed(cz));
            Assert.False(Grabbed(cy));
            Assert.Contains(cy, l => l.Contains("air: commit: land", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_running_jump_left_about_1_6_m_out_at_the_2_8_m_lane_is_caught_FB2()
    {
        var (course, game, models) = Needs();
        // His 17.78 s: a tap at a run left the ground 1.59 m out of the 2.8 m face, met it past the arc's top with the hands under the lip, and landed.
        // A press from about 1.1 to 2.1 m out at a run is held for the run's footfall (the earlier build's running grab) and the jump leaves about 1.6 m out.
        var options = On(course, game, models, "grab-2.8");
        NeedsCommit(options);
        float face;
        using (var probe = Session.Create(options)) face = probe.World.Get<Body>(probe.Player).Feet.X + 8f;
        foreach (var release in new[] { false, true })
        {
            var (cz, off) = Run(options, s => RunAndJump(s, f => f.X >= face - 2f, release));
            var (cy, offCy) = Run(OnRules1(options), s => RunAndJump(s, f => f.X >= face - 2f, release));
            output.WriteLine($"forward {(release ? "released" : "held")} in the air, left {F(face - off.X)} m out: rules 2 {Short(cz)}");
            output.WriteLine($"forward {(release ? "released" : "held")} in the air, left {F(face - offCy.X)} m out: rules 1 {Short(cy)}");
            Assert.InRange(face - off.X, 1.5f, 1.7f);
            Assert.Contains(cz, l => l.Contains("air: commit: catch the lip at 2.80 m falling (jumped at it)", StringComparison.Ordinal));
            Assert.True(Grabbed(cz));
            Assert.False(Grabbed(cy));
        }
    }

    [Fact]
    public void A_catch_at_the_2_8_m_block_s_corner_is_made_not_turned_into_a_landing()
    {
        var (course, game, models) = Needs();
        // His 43.02 s: a run past the block's north-east corner and a tap; an earlier build committed the catch on the probe's corner normal, glanced off the corner,
        // looked again as the catch began, found nothing and landed. An earlier build squares the lip onto the face, decides the catch whole and keeps it.
        var options = On(course, game, models, "grab-2.8", "-18.3,45.9,0,-116.4");
        NeedsCommit(options);
        var (cz, _) = Run(options, s => RunAndJump(s, f => f.Y <= 44.6f, release: true, ticks: 3 * World.TicksPerSecond));
        var (cy, _) = Run(OnRules1(options), s => RunAndJump(s, f => f.Y <= 44.6f, release: true, ticks: 3 * World.TicksPerSecond));
        output.WriteLine($"rules 2 {Short(cz)}");
        output.WriteLine($"rules 1 {Short(cy)}");
        Assert.True(Lapsed(cy), "rules 1's lapse reproduced");
        Assert.True(Grabbed(cz));
        Assert.False(Lapsed(cz));
    }

    [Fact]
    public void A_recording_notes_v_2_and_replays_on_it_and_one_on_rules_1_replays_on_rules_1()
    {
        var (course, game, models) = Needs();
        foreach (var options in new[] { OffTheTop(course, game, models), OnRules1(OffTheTop(course, game, models)) })
        {
            Tape tape;
            using (var session = Session.Create(options))
            {
                tape = new Tape { Header = session.TapeHeader("test") };
                foreach (var frame in OffTheTopFrames(session, Buttons.Use))
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
    /// With <c>VITOMIR_CATCH_RULES_TAPES</c> (a folder): the tapes the stills are drawn from, each with the earlier build's (<c>…-now</c>) and the earlier build's (<c>…-before</c>), and beside
    /// each the ticks to draw (<c>.ticks</c>) and its log: the jump off the 2.8 m top at the 2.6 m block, use held (<c>offtop</c>); the running jump at the
    /// 2.8 m lane left about 1.6 m out (<c>fb2</c>); the catch at the 2.8 m block's corner (<c>corner</c>).
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_CATCH_RULES_TAPES") is { Length: > 0 }, "VITOMIR_CATCH_RULES_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_CATCH_RULES_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var lane = On(course, game, models, "grab-2.8");
        float face;
        using (var probe = Session.Create(lane)) face = probe.World.Get<Body>(probe.Player).Feet.X + 8f;
        var cases = new (string Name, SessionOptions Options, Func<Session, IEnumerable<ActionFrame>> Frames)[]
        {
            ("offtop", OffTheTop(course, game, models), s => OffTheTopFrames(s, Buttons.Use)),
            ("fb2", lane, s => RunAndJump(s, f => f.X >= face - 2f, release: true)),
            ("corner", On(course, game, models, "grab-2.8", "-18.3,45.9,0,-116.4"), s => RunAndJump(s, f => f.Y <= 44.6f, release: true, ticks: 3 * World.TicksPerSecond)),
        };
        foreach (var (name, options, frames) in cases)
            foreach (var (o, suffix) in new[] { (options, "now"), (OnRules1(options), "before") })
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
                                             || clip.StartsWith("jump", StringComparison.Ordinal)))
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
