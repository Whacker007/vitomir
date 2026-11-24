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
/// As the program gives it (tier 1 on the engine, part 3: the models repository's clips): the settings (<c>wallsOff</c> / <c>--walls-off</c>,
/// <c>wallHoldReach</c>, <c>wallClimbRate</c>, <c>splayWristLimit</c>) and the note <c>walls</c> (none: the earlier build's), <c>breaks=1</c> in the note <c>hang</c>; on the
/// proving ground (skip without it, the 1.1 install or the models repository's files and after): the walls station's wall with holds up and over the top,
/// its corners, the sheer face only out of the masquerade, the wall marked not to be climbed refused and said, the leaps station's ledge that goes on, the earlier build's
/// behind the switch, and a recording replays digest for digest.
/// </summary>
public sealed class PeopleWallsTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_walls_and_the_switch_turns_them_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonWallFor(new Cli([]));
        Assert.True(on.On);
        Assert.Equal("on=1 reach=0.25 rate=1.3 move=1.5 flow=0.15 diag=1 wrist=50", on.Note());   // The earlier build's defaults (PeopleWallFlowTests)
        Assert.Equal("on=1 reach=0.25 rate=1 wrist=50", plain.PersonWallFor(new Cli(["--walls-no-flow"])).Note());
        Assert.True(plain.PersonHangFor(new Cli([])).Breaks);   // The breaks with the walls
        Assert.False(plain.PersonWallFor(new Cli(["--walls-off"])).On);
        Assert.Equal("off", plain.PersonWallFor(new Cli(["--walls-off"])).Note());
        Assert.False(plain.PersonHangFor(new Cli(["--walls-off"])).Breaks);   // The earlier build's leap along a lip that goes on
        Assert.Equal("on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15", plain.PersonHangFor(new Cli(["--walls-off"])).Note());
        Assert.False(new PlatformSettings { WallsOff = true }.PersonWallFor(new Cli([])).On);
        Assert.False(plain.PersonWallFor(new Cli(["--hang-moves-off"])).On);   // it rides on the hang's corners and leaps
        Assert.False(plain.PersonWallFor(new Cli(["--moves-off"])).On);
        Assert.Equal(0.3f, new PlatformSettings { WallHoldReach = 0.3f }.PersonWallFor(new Cli([])).HoldReach);
        Assert.Equal(0.25f, new PlatformSettings { WallHoldReach = 2f }.PersonWallFor(new Cli([])).HoldReach);   // out of 0.05 to 0.4: the default
        Assert.Equal(1.5f, new PlatformSettings { WallClimbRate = 1.5f }.PersonWallFor(new Cli([])).Rate);
        Assert.Equal(1.3f, new PlatformSettings { WallClimbRate = 9f }.PersonWallFor(new Cli([])).Rate);   // out of 1 to 3, the earlier build's default
        Assert.Equal(45f, new PlatformSettings { SplayWristLimit = 45f }.PersonWallFor(new Cli([])).WristLimitDegrees);
        Assert.Equal(50f, new PlatformSettings { SplayWristLimit = 90f }.PersonWallFor(new Cli([])).WristLimitDegrees);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-walls-wall-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { WallsOff = true, WallHoldReach = 0.2f, WallClimbRate = 1.25f, SplayWristLimit = 40f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"wallsOff\": true", text);
            Assert.Contains("\"wallHoldReach\": 0.2", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.WallsOff);
            Assert.Equal((0.2f, 1.25f, 40f), (back.WallHoldReach, back.WallClimbRate, back.SplayWristLimit));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_walls_note_has_the_walls_off_and_its_hang_note_leaps_along()
    {
        var head = new TapeHeader();
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonWall.On);
        head.Notes[PersonWallSettings.TapeNote] = (new PersonWallSettings() with { HoldReach = 0.3f }).Note();
        head.Notes[PersonHangSettings.TapeNote] = "on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 0.3f), (o.PersonWall.On, o.PersonWall.HoldReach));
        Assert.False(o.PersonHang.Breaks);
        head.Notes[PersonWallSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonWall.On);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-walls-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string? place = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Place = place, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
        PersonGaits = Animation.People.PersonGaitSettings.Off,   // climbing is measured on the earlier build's paces until its rework (the paces' approaches)
    };

    static SessionOptions WithWallsOff(SessionOptions o) => o with { PersonWall = PersonWallSettings.Off, PersonHang = o.PersonHang with { Breaks = false } };

    /// <summary>Skips without the walls' clips on the player (an older set), or without the course's lane.</summary>
    static void NeedsWalls(SessionOptions options)
    {
        Session s;
        try { s = Session.Create(options); }
        catch (SessionException e) when (e.Message.Contains("start", StringComparison.OrdinalIgnoreCase)) { Assert.Skip($"the course has no lane {options.Start} (before the walls')"); throw; }
        using (s)
        {
            var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
            Assert.SkipUnless(person?.Ledge?.Paths.HasWalls == true, "the models repository's files are too old (no wall clips)");
        }
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons) => new(wish, Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>
    /// At a lane: walk up to about half a metre short of its face (8 m on), stand, tap jump (the getting-on), Caps Lock first when <paramref name="unbind"/>; once on
    /// the wall (or after 3 s if not) 0.4 s, <paramref name="then"/> (the ticks since, the session) asks.
    /// </summary>
    internal static Func<int, Session, ActionFrame> OnThen(Func<int, Session, ActionFrame> then, bool unbind = false)
    {
        float from = float.NaN;
        int still = -1, on = -1;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            if (float.IsNaN(from)) from = b.Feet.X;
            if (t == 0 && unbind) return Frame(Vector2.Zero, Buttons.Masquerade);
            if (still < 0 && b.Feet.X - from >= 8f - 0.5f - 0.25f) still = t;
            if (still < 0) return Frame(new Vector2(0f, 1f), Buttons.Walk);
            if (on < 0 && (b.State == BodyState.Hanging || t >= still + 60 + 180)) on = t;
            if (on >= 0 && t >= on + 48) return then(t - on - 48, s);
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

    /// <summary>The wish held: forward (up the wall) or to the view's left (−1) or right (+1), with the pace's button.</summary>
    internal static Func<int, Session, ActionFrame> Hold(Vector2 wish, Buttons pace = Buttons.None, int ticks = int.MaxValue) =>
        (t, s) => Frame(t < ticks ? wish : Vector2.Zero, t < ticks ? pace : Buttons.None);

    [Fact]
    public void On_the_proving_ground_the_wall_with_holds_is_climbed_up_and_over_the_top()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "walls-holds");
        NeedsWalls(o);
        // Up held until it stands on the top (held on, it would walk on over the far edge).
        var (said, clips, end, _) = Run(o, OnThen((t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None)
                                                         : Frame(new Vector2(0f, 1f), Buttons.None)), 20 * World.TicksPerSecond);
        output.WriteLine($"walls-holds, up held: {string.Join(", ", clips.Where(c => c.StartsWith("wall_") || c.StartsWith("hang_") || c.StartsWith("pull_")))}");
        foreach (var l in said.Where(l => l.Contains("wall:") || l.Contains("move:"))) output.WriteLine("  " + l);
        Assert.Contains(said, l => l.Contains("wall: on from the ground"));
        Assert.Contains(said, l => l.Contains("wall: a quick move up"));
        Assert.Contains(said, l => l.Contains("wall: over the top into the braced hang"));
        Assert.Contains("pull_up_braced", clips);
        Assert.InRange(end.Feet.Z, 3.95f, 4.06f);   // standing on its top
        foreach (var (pace, button, word) in new[] { ("careful", Buttons.Walk, "a careful move up"), ("long", Buttons.Sprint, "a long move up") })
        {
            var (s2, c2, _, _) = Run(o, OnThen(Hold(new Vector2(0f, 1f), button, 4 * World.TicksPerSecond)), 10 * World.TicksPerSecond);
            output.WriteLine($"walls-holds, up held with {pace}: {string.Join(", ", c2.Where(c => c.StartsWith("wall_")))}");
            Assert.Contains(s2, l => l.Contains("wall: " + word));
        }
        // Across, and down to the foot and off.
        var (s3, c3, e3, _) = Run(o, OnThen((t, s) => t < 150 ? Frame(new Vector2(-1f, 0f), Buttons.None) : t < 330 ? Frame(new Vector2(1f, 0f), Buttons.None)
                                                    : t < 480 ? Frame(new Vector2(0f, 1f), Buttons.None) : Frame(new Vector2(0f, -1f), Buttons.None)), 16 * World.TicksPerSecond);
        output.WriteLine($"walls-holds, left, right, up, down: {string.Join(", ", c3.Where(c => c.StartsWith("wall_")))}");
        foreach (var l in s3.Where(l => l.Contains("wall:"))) output.WriteLine("  " + l);
        Assert.Contains(s3, l => l.Contains("wall: a quick move left"));
        Assert.Contains(s3, l => l.Contains("wall: a quick move right"));
        Assert.Contains(s3, l => l.Contains("wall: off at the foot"));
        Assert.Equal(BodyState.Ground, e3.State);
        // A tap at the wall is a jump.
        var (old, oldClips, _, _) = Run(WithWallsOff(o), OnThen(Hold(new Vector2(0f, 1f))), 6 * World.TicksPerSecond);
        Assert.DoesNotContain(oldClips, c => c.StartsWith("wall_", StringComparison.Ordinal));
        Assert.DoesNotContain(old, l => l.Contains("wall:"));
    }

    [Fact]
    public void On_the_proving_ground_the_corners_the_sheer_face_and_the_wall_not_to_be_climbed()
    {
        var (course, game, models) = Needs();
        var corners = On(course, game, models, "walls-corners");
        NeedsWalls(corners);
        foreach (var (way, word) in new[] { (-1f, "round the outside corner to the left"), (1f, "round the inside corner to the right") })
        {
            var (said, clips, end, _) = Run(corners, OnThen(Hold(new Vector2(way, 0f))), 17 * World.TicksPerSecond);
            output.WriteLine($"walls-corners, {(way < 0 ? "left" : "right")} held: {string.Join(", ", clips.Where(c => c.StartsWith("wall_")))}");
            foreach (var l in said.Where(l => l.Contains("wall:"))) output.WriteLine("  " + l);
            Assert.Contains(said, l => l.Contains("wall: " + word));
            Assert.Equal(BodyState.Hanging, end.State);
            Assert.InRange(MathF.Abs(MathF.Abs(DetMath.WrapAngle(end.Yaw)) - MathF.PI / 2f), 0f, 0.01f);   // facing the other face, a quarter round
        }
        // The sheer face: in the masquerade a tap is a jump; out of it (Caps Lock), climbed with the splayed grip.
        var sheer = On(course, game, models, "walls-sheer");
        var (inMask, maskClips, _, _) = Run(sheer, OnThen(Hold(new Vector2(0f, 1f))), 6 * World.TicksPerSecond);
        Assert.DoesNotContain(maskClips, c => c.StartsWith("wall_", StringComparison.Ordinal));
        Assert.DoesNotContain(inMask, l => l.Contains("wall:"));
        var (unbound, unboundClips, _, _) = Run(sheer, OnThen(Hold(new Vector2(0f, 1f), Buttons.None, 3 * World.TicksPerSecond), unbind: true), 12 * World.TicksPerSecond);
        output.WriteLine($"walls-sheer, Caps Lock: {string.Join(", ", unboundClips.Where(c => c.StartsWith("wall_")))}");
        foreach (var l in unbound.Where(l => l.Contains("wall:") || l.Contains("masquerade"))) output.WriteLine("  " + l);
        Assert.Contains(unbound, l => l.Contains("wall: on from the ground (a face with nothing to hold: the splayed grip)"));
        Assert.Contains(unbound, l => l.Contains("wall: a quick move up (a face with nothing to hold: the splayed grip)"));
        // The wall not to be climbed: refused, out of the masquerade too, said once.
        var (no, noClips, noEnd, _) = Run(On(course, game, models, "walls-noclimb"), OnThen(Hold(Vector2.Zero), unbind: true), 9 * World.TicksPerSecond);
        output.WriteLine($"noclimb: {string.Join(", ", noClips)}; end {noEnd.State} {F(noEnd.Feet.X)},{F(noEnd.Feet.Y)},{F(noEnd.Feet.Z)}");
        foreach (var l in no.Where(l => l.Contains(" s: "))) output.WriteLine("  noclimb: " + l);
        Assert.DoesNotContain(noClips, c => c.StartsWith("wall_", StringComparison.Ordinal));
        Assert.Single(no, l => l.Contains("wall: no on from the ground: the level says not to be climbed there"));
    }

    /// <summary>Placed 0.45 m out of the leap station's back wall (between it and the facing one), grabbed; then jump with left held.</summary>
    internal static string BehindPlace(SessionOptions o)
    {
        using var probe = Session.Create(o);
        var at = probe.World.Get<Body>(probe.Player).Feet;
        return string.Create(CultureInfo.InvariantCulture, $"{at.X + 8f - 0.45f:0.###},{at.Y:0.###},{at.Z:0.###},0");
    }

    internal static Func<int, Session, ActionFrame> SideJump() => (t, s) => Frame(t < 12 ? new Vector2(-1f, 0f) : Vector2.Zero, t == 0 ? Buttons.Jump : Buttons.None);

    [Fact]
    public void On_the_proving_ground_jump_with_a_side_held_on_a_ledge_that_goes_on_pulls_up_FI1()
    {
        var (course, game, models) = Needs();
        var behind = On(course, game, models, "leaps-behind");
        NeedsWalls(behind);
        behind = behind with { Place = BehindPlace(behind) };
        var (said, clips, end, _) = Run(behind, PeopleHangTests.GrabThen(SideJump(), placed: true), 7 * World.TicksPerSecond);
        output.WriteLine($"leaps-behind, jump with left held: {string.Join(", ", clips.Where(c => c.StartsWith("hang_") || c.StartsWith("pull_") || c.StartsWith("grab_")))}");
        foreach (var l in said.Where(l => l.Contains("hang:"))) output.WriteLine("  " + l);
        Assert.DoesNotContain("hang_leap_left", clips);
        Assert.Contains("pull_up_braced", clips);
        Assert.Contains(said, l => l.Contains("hang: no leap left: the lip goes on to it, no break between"));
        // The leap along it.
        var (_, oldClips, _, _) = Run(WithWallsOff(behind), PeopleHangTests.GrabThen(SideJump(), placed: true), 7 * World.TicksPerSecond);
        Assert.Contains("hang_leap_left", oldClips);
    }

    [Fact]
    public void A_recording_notes_the_walls_and_replays_on_them_and_one_without_them_without_them()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "walls-holds");
        NeedsWalls(o);
        foreach (var options in new[] { o, WithWallsOff(o) })
        {
            var tape = Run(options, OnThen(Hold(new Vector2(0f, 1f))), 10 * World.TicksPerSecond, record: true).Tape!;
            Assert.Equal(options.PersonWall.Note(), tape.Header.Notes[PersonWallSettings.TapeNote]);
            Assert.Equal(options.PersonHang.Note(), tape.Header.Notes[PersonHangSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonWall, again.PersonWall);
            Assert.Equal(options.PersonHang, again.PersonHang);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonWallSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 10);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_WALLS_TAPES</c> (a folder): the tapes the stills are drawn from, and beside each the ticks to draw (<c>.ticks</c>: where each wall clip
    /// begins) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_WALLS_TAPES") is { Length: > 0 }, "VITOMIR_WALLS_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_WALLS_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var holds = On(course, game, models, "walls-holds");
        var behind = On(course, game, models, "leaps-behind");
        behind = behind with { Place = BehindPlace(behind) };
        // A run at the wall and a tap where it comes within reach: the jump onto it (wall_on_jump from the ground); and one tapped further out (a jump
        // at the wall), the running jump caught in the air by itself (wall_on_jump from its hands' contact).
        Func<int, Session, ActionFrame> At(float from, float to) => (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            return Frame(new Vector2(0f, 1f), b.Feet.X > from && b.Feet.X < to && b.State == BodyState.Ground ? Buttons.Jump | Buttons.Sprint : Buttons.Sprint);
        };
        var cases = new (string Name, SessionOptions Options, Func<Func<int, Session, ActionFrame>> Plan, int Seconds)[]
        {
            ("on-standing", holds, () => OnThen(Hold(Vector2.Zero)), 10),
            ("on-run-up", holds, () => At(-68.8f, -68.4f), 5),
            ("on-jumped-at", holds, () => At(-69.6f, -69.2f), 5),
            ("up-quick", holds, () => OnThen(Hold(new Vector2(0f, 1f), Buttons.None, 260)), 12),
            ("up-careful", holds, () => OnThen(Hold(new Vector2(0f, 1f), Buttons.Walk, 260)), 14),
            ("up-long", holds, () => OnThen(Hold(new Vector2(0f, 1f), Buttons.Sprint, 260)), 13),
            ("across", holds, () => OnThen((t, s) => Frame(t >= 72 && t < 140 ? new Vector2(-1f, 0f) : t >= 260 && t < 320 ? new Vector2(1f, 0f) : Vector2.Zero, Buttons.None)), 13),
            ("corner-out", On(course, game, models, "walls-corners"), () => OnThen(Hold(new Vector2(-1f, 0f))), 12),
            ("corner-in", On(course, game, models, "walls-corners"), () => OnThen(Hold(new Vector2(1f, 0f))), 12),
            ("over-the-top", holds, () => OnThen((t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None)
                                                         : Frame(new Vector2(0f, 1f), Buttons.None)), 19),
            ("off-at-the-foot", holds, () => OnThen((t, s) => Frame(t < 100 ? new Vector2(0f, 1f) : new Vector2(0f, -1f), Buttons.None)), 13),
            ("push-off", holds, () => OnThen((t, s) => t < 150 ? Frame(new Vector2(0f, 1f), Buttons.None) : t == 260 ? Frame(new Vector2(0f, -1f), Buttons.Jump) : Frame(Vector2.Zero, Buttons.None)), 13),
            ("sheer", On(course, game, models, "walls-sheer"), () => OnThen(Hold(new Vector2(0f, 1f), Buttons.None, 260), unbind: true), 12),
            ("ledge-goes-on", behind, () => PeopleHangTests.GrabThen(SideJump(), placed: true), 6),
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
                string what = clip.StartsWith("wall_", StringComparison.Ordinal) || clip.StartsWith("pull_up", StringComparison.Ordinal) || clip.StartsWith("hang_", StringComparison.Ordinal) ? clip : "";
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
