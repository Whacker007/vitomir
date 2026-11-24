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
/// As the program gives it (tier 1 on the engine, part 2: the models repository's clips): the settings (<c>hangMovesOff</c> / <c>--hang-moves-off</c>,
/// <c>leapTolerance</c>, <c>cornerSlack</c>, <c>hangAngleTolerance</c>) and the note <c>hang</c> (none: the earlier build's); on the proving ground (skip without it, the 1.1
/// install or the models repository's files and after): the corner station's braced and free corners each way and its corner too short, the leap
/// station's leaps (to a side and back, up, across at the top, down by crouch with use held, back to the facing ledge), the earlier build's behind the switch, and a
/// recording replays digest for digest.
/// </summary>
public sealed class PeopleHangTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_hang_s_corners_and_leaps_and_the_switch_turns_them_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonHangFor(new Cli([]));
        Assert.True(on.On && on.Corners && on.Leaps);
        Assert.Equal("on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15 breaks=1", on.Note());   // breaks=1
        Assert.False(plain.PersonHangFor(new Cli(["--hang-moves-off"])).On);
        Assert.Equal("off", plain.PersonHangFor(new Cli(["--hang-moves-off"])).Note());
        Assert.False(new PlatformSettings { HangMovesOff = true }.PersonHangFor(new Cli([])).On);
        Assert.False(plain.PersonHangFor(new Cli(["--moves-off"])).On);   // it rides on tier 1's moves
        Assert.Equal(0.4f, new PlatformSettings { LeapTolerance = 0.4f }.PersonHangFor(new Cli([])).LeapTolerance);
        Assert.Equal(0.25f, new PlatformSettings { LeapTolerance = 2f }.PersonHangFor(new Cli([])).LeapTolerance);   // out of 0.05 to 0.5: the default
        Assert.Equal(0.1f, new PlatformSettings { CornerSlack = 0.1f }.PersonHangFor(new Cli([])).CornerSlack);
        Assert.Equal(20f, new PlatformSettings { HangAngleTolerance = 20f }.PersonHangFor(new Cli([])).AngleToleranceDegrees);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-hang-hang-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { HangMovesOff = true, LeapTolerance = 0.3f, CornerSlack = 0.2f, HangAngleTolerance = 10f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"hangMovesOff\": true", text);
            Assert.Contains("\"leapTolerance\": 0.3", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.HangMovesOff);
            Assert.Equal((0.3f, 0.2f, 10f), (back.LeapTolerance, back.CornerSlack, back.HangAngleTolerance));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_hang_note_has_the_hang_moves_off()
    {
        var head = new TapeHeader();
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonHang.On);
        head.Notes[PersonHangSettings.TapeNote] = (new PersonHangSettings() with { Corners = false, LeapTolerance = 0.3f }).Note();
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, false, true, 0.3f), (o.PersonHang.On, o.PersonHang.Corners, o.PersonHang.Leaps, o.PersonHang.LeapTolerance));
        head.Notes[PersonHangSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonHang.On);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-hang-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string? place = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Place = place, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static SessionOptions WithHangMovesOff(SessionOptions o) => o with { PersonHang = PersonHangSettings.Off };

    /// <summary>Skips without the hang's clips on the player (an older set), or without the course's lane.</summary>
    static void NeedsHang(SessionOptions options)
    {
        Session s;
        try { s = Session.Create(options); }
        catch (SessionException e) when (e.Message.Contains("start", StringComparison.OrdinalIgnoreCase)) { Assert.Skip($"the course has no lane {options.Start} (before the walls')"); throw; }
        using (s)
        {
            var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
            Assert.SkipUnless(person?.Ledge?.Paths.HasHangMoves == true, "the models repository's files are too old (no hang leaps or corners)");
        }
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons) => new(wish, Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>
    /// At a lane: walk up to 0.45 m short of its face (8 m on; none when placed), stand, tap jump (the standing grab); once hanging 0.4 s, <paramref name="then"/>
    /// (the ticks since, the session) asks.
    /// </summary>
    internal static Func<int, Session, ActionFrame> GrabThen(Func<int, Session, ActionFrame> then, bool placed = false)
    {
        float from = float.NaN;
        int still = placed ? 0 : -1, hung = -1;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            if (float.IsNaN(from)) from = b.Feet.X;
            if (still < 0 && b.Feet.X - from >= 8f - 0.45f - 0.3f) still = t;
            if (still < 0) return Frame(new Vector2(0f, 1f), Buttons.Walk);
            if (hung < 0 && b.State == BodyState.Hanging) hung = t;
            if (hung >= 0 && t >= hung + 48) return then(t - hung - 48, s);
            return Frame(Vector2.Zero, t == still + 60 ? Buttons.Jump : Buttons.None);
        };
    }

    /// <summary>A run of <paramref name="ticks"/> frames from <paramref name="plan"/>, its log and the player's clips in order (each change).</summary>
    static (List<string> Said, List<string> Clips, Body End, Tape? Tape) Run(SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks, bool record = false)
    {
        var said = new List<string>();
        var clips = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var assets = s.World.Service<CharacterAssets>();
        var tape = record ? new Tape { Header = s.TapeHeader("test") } : null;
        for (int t = 0; t < ticks; t++)
        {
            var frame = plan(t, s);
            s.Step(frame);
            if (tape is not null)
            {
                tape.Frames.Add(frame);
                if (s.Tick % Tape.DigestEvery == 0) tape.Digests[s.Tick] = s.Digest();
            }
            string c = assets.Clip(s.World.Get<CharacterAnimation>(s.Player).Clip).Name;
            if (clips.Count == 0 || clips[^1] != c) clips.Add(c);
        }
        return (said, clips, s.World.Get<Body>(s.Player), tape);
    }

    /// <summary>The wish to the view's left (−1) or right (+1) held.</summary>
    static Func<int, Session, ActionFrame> Side(int way) => (t, s) => Frame(new Vector2(way, 0f), Buttons.None);

    /// <summary>The leap station's columns: jump with left held, back with right, jump (up), jump with right (across at the top), crouch with use held (down).</summary>
    internal static Func<int, Session, ActionFrame> LeapRoute() => (t, s) =>
    {
        const int gap = 270;   // 2.25 s between presses: each leap's 1.8 s and the hang
        int k = t / gap, at = t % gap;
        bool press = at < 12;   // the side held for the press only (held on, the hang after the leap shimmies on that way)
        return k switch
        {
            0 => Frame(press ? new Vector2(-1f, 0f) : Vector2.Zero, at == 0 ? Buttons.Jump : Buttons.None),
            1 => Frame(press ? new Vector2(1f, 0f) : Vector2.Zero, at == 0 ? Buttons.Jump : Buttons.None),
            2 => Frame(Vector2.Zero, at == 0 ? Buttons.Jump : Buttons.None),
            3 => Frame(press ? new Vector2(1f, 0f) : Vector2.Zero, at == 0 ? Buttons.Jump : Buttons.None),
            4 => Frame(Vector2.Zero, at < 20 ? Buttons.Crouch | Buttons.Use : Buttons.None),
            _ => Frame(Vector2.Zero, Buttons.None),
        };
    };

    /// <summary>Placed 0.45 m out of the leap station's back wall (between it and the facing one), grabbed, then back with jump.</summary>
    internal static string BehindPlace(SessionOptions o)
    {
        using var probe = Session.Create(o);
        var at = probe.World.Get<Body>(probe.Player).Feet;
        return string.Create(CultureInfo.InvariantCulture, $"{at.X + 8f - 0.45f:0.###},{at.Y:0.###},{at.Z:0.###},0");
    }

    internal static Func<int, Session, ActionFrame> Back() => (t, s) => Frame(t < 12 ? new Vector2(0f, -1f) : Vector2.Zero, t == 0 ? Buttons.Jump : Buttons.None);

    [Fact]
    public void On_the_proving_ground_the_corners_braced_and_free_each_way_and_one_too_short()
    {
        var (course, game, models) = Needs();
        foreach (var (lane, way, expect) in new[]
        {
            ("corners-braced", -1, "round the outside corner to the left (braced)"), ("corners-braced", 1, "round the inside corner to the right (braced)"),
            ("corners-free", -1, "round the outside corner to the left (free)"), ("corners-free", 1, "round the inside corner to the right (free)"),
            ("corners-short", -1, "not round the outside corner to the left: the lip round it is too short"),
        })
        {
            var o = On(course, game, models, lane);
            NeedsHang(o);
            var (said, clips, end, _) = Run(o, GrabThen(Side(way)), 14 * World.TicksPerSecond);
            output.WriteLine($"{lane}, the wish {(way < 0 ? "left" : "right")}: {string.Join(", ", clips.Where(c => c.StartsWith("hang_") || c.StartsWith("shimmy_") || c.StartsWith("grab_")))}");
            foreach (var l in said.Where(l => l.Contains("hang:"))) output.WriteLine("  " + l);
            Assert.Contains(said, l => l.Contains("hang: " + expect));
            Assert.Equal(BodyState.Hanging, end.State);
            if (lane != "corners-short") Assert.Contains(clips, c => c.StartsWith("hang_corner_", StringComparison.Ordinal));
            else Assert.Single(said, l => l.Contains("hang: not round"));   // said once
        }
        // The shimmy stops at the corner, nothing said.
        var (old, oldClips, _, _) = Run(WithHangMovesOff(On(course, game, models, "corners-braced")), GrabThen(Side(-1)), 12 * World.TicksPerSecond);
        Assert.DoesNotContain(oldClips, c => c.StartsWith("hang_corner_", StringComparison.Ordinal));
        Assert.DoesNotContain(old, l => l.Contains("hang:"));
    }

    [Fact]
    public void On_the_proving_ground_the_leaps()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "leaps-columns");
        NeedsHang(o);
        var (said, clips, end, _) = Run(o, GrabThen(LeapRoute()), 22 * World.TicksPerSecond);
        output.WriteLine($"leaps-columns: {string.Join(", ", clips.Where(c => c.StartsWith("hang_") || c.StartsWith("grab_")))}");
        foreach (var l in said.Where(l => l.Contains(" s: "))) output.WriteLine("  " + l);
        var leaps = said.Where(l => l.Contains("hang: leap")).Select(l => l[(l.IndexOf("hang: ", StringComparison.Ordinal) + 6)..].Split(' ')[1]).ToList();
        Assert.Equal(["left", "right", "up", "right", "down"], leaps);
        Assert.Equal(BodyState.Hanging, end.State);
        // Back with jump to the facing ledge 2.0 m behind.
        var behind = On(course, game, models, "leaps-behind");
        behind = behind with { Place = BehindPlace(behind) };
        var (bs, bc, be, _) = Run(behind, GrabThen(Back(), placed: true), 8 * World.TicksPerSecond);
        foreach (var l in bs.Where(l => l.Contains("hang:"))) output.WriteLine("  behind: " + l);
        Assert.Contains("hang_leap_back", bc);
        Assert.Equal(BodyState.Hanging, be.State);
        Assert.InRange(MathF.Abs(DetMath.WrapAngle(be.Yaw - MathF.PI)), 0f, 0.01f);   // facing the other way round
        // Jump with left held is no leap.
        var (old, oldClips, _, _) = Run(WithHangMovesOff(o), GrabThen(LeapRoute()), 6 * World.TicksPerSecond);
        Assert.DoesNotContain(oldClips, c => c.StartsWith("hang_leap_", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recording_notes_the_hang_and_replays_on_it_and_one_without_its_moves_without_them()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "corners-braced");
        NeedsHang(o);
        foreach (var options in new[] { o, WithHangMovesOff(o) })
        {
            var tape = Run(options, GrabThen(Side(-1)), 12 * World.TicksPerSecond, record: true).Tape!;
            Assert.Equal(options.PersonHang.Note(), tape.Header.Notes[PersonHangSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonHang, again.PersonHang);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonHangSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 10);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_HANG_TAPES</c> (a folder): the tapes the stills are drawn from (each corner kind each way, the leaps, the leap back, the leap down by
    /// crouch with use held, a refused corner), and beside each the ticks to draw (<c>.ticks</c>: where a leap's or a corner's clip begins and ends) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_HANG_TAPES") is { Length: > 0 }, "VITOMIR_HANG_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_HANG_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var behind = On(course, game, models, "leaps-behind");
        behind = behind with { Place = BehindPlace(behind) };
        var cases = new (string Name, SessionOptions Options, Func<Func<int, Session, ActionFrame>> Plan, int Seconds)[]
        {
            ("corner-out-braced", On(course, game, models, "corners-braced"), () => GrabThen(Side(-1)), 17),
            ("corner-in-braced", On(course, game, models, "corners-braced"), () => GrabThen(Side(1)), 17),
            ("corner-out-free", On(course, game, models, "corners-free"), () => GrabThen(Side(-1)), 17),
            ("corner-in-free", On(course, game, models, "corners-free"), () => GrabThen(Side(1)), 17),
            ("corner-short", On(course, game, models, "corners-short"), () => GrabThen(Side(-1)), 13),
            ("leaps", On(course, game, models, "leaps-columns"), () => GrabThen(LeapRoute()), 21),
            ("leap-back", behind, () => GrabThen(Back(), placed: true), 7),
        };
        foreach (var (name, o, plan, seconds) in cases)
        {
            var said = new List<string>();
            using var session = Session.Create(o with { Say = said.Add });
            var assets = session.World.Service<CharacterAssets>();
            var tape = new Tape { Header = session.TapeHeader(name) };
            var marks = new List<(int Tick, string What)>();
            string last = "";
            var frames = plan();
            for (int t = 0; t < seconds * World.TicksPerSecond; t++)
            {
                var frame = frames(t, session);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                string clip = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                string what = clip.StartsWith("hang_leap_", StringComparison.Ordinal) || clip.StartsWith("hang_corner_", StringComparison.Ordinal) ? clip
                              : clip.StartsWith("shimmy_", StringComparison.Ordinal) || clip.StartsWith("hang_", StringComparison.Ordinal) ? "hang" : "";
                if (what != last && what != "") marks.Add(((int)session.Tick, what));
                last = what;
            }
            string path = Path.Combine(dir, $"{name}.tape");
            tape.Write(path);
            File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
            File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
            output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
        }
    }
}
