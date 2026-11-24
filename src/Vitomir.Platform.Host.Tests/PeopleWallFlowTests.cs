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
/// As the program gives it (wall climbing faster and flowing, with diagonals): the settings (<c>wallsNoFlow</c> / <c>--walls-no-flow</c>,
/// <c>wallClimbRate</c> 1.5, <c>wallMoveRate</c>, <c>wallFlowBlend</c>, <c>wallDiagonals</c>; a file of the earlier build's climb rate brought to the earlier build's once), the note
/// <c>walls</c> (the earlier build's climbing); on the proving ground (skip without it, the 1.1 install or the models repository's files and after): a held
/// climb from the getting-on to standing on the top with no rest, a diagonal, the six timings of a play log, the earlier build's against the earlier build's, and a recording replays digest for digest
/// on the earlier build's and on the earlier build's.
/// </summary>
public sealed class PeopleWallFlowTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_flowing_climb_and_the_switch_the_climb_without_the_flow()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonWallFor(new Cli([]));
        Assert.Equal((1.3f, 1.5f, 0.15f, true, true), (on.Rate, on.MoveRate, on.FlowBlend, on.Diagonals, on.Flows));
        var dd = plain.PersonWallFor(new Cli(["--walls-no-flow"]));
        Assert.Equal(PersonWallSettings.NoFlow, dd);
        Assert.Equal("on=1 reach=0.25 rate=1 wrist=50", dd.Note());
        Assert.Equal(PersonWallSettings.NoFlow, new PlatformSettings { WallsNoFlow = true }.PersonWallFor(new Cli([])));
        Assert.False(plain.PersonWallFor(new Cli(["--walls-off"])).On);   // The earlier build's, as before
        Assert.Equal(0.3f, new PlatformSettings { WallHoldReach = 0.3f, WallsNoFlow = true }.PersonWallFor(new Cli([])).HoldReach);   // the reach kept on the earlier build's
        Assert.Equal(2f, new PlatformSettings { WallMoveRate = 2f }.PersonWallFor(new Cli([])).MoveRate);
        Assert.Equal(1.5f, new PlatformSettings { WallMoveRate = 0.5f }.PersonWallFor(new Cli([])).MoveRate);   // out of 1 to 3: the default
        Assert.Equal(0.25f, new PlatformSettings { WallFlowBlend = 0.25f }.PersonWallFor(new Cli([])).FlowBlend);
        Assert.Equal(0.15f, new PlatformSettings { WallFlowBlend = 0.05f }.PersonWallFor(new Cli([])).FlowBlend);   // out of 0.1 to 0.3
        Assert.False(new PlatformSettings { WallDiagonals = false }.PersonWallFor(new Cli([])).Diagonals);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-wall-flow-wall-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { WallsNoFlow = true, WallMoveRate = 2f, WallFlowBlend = 0.2f, WallDiagonals = false, WallClimbRate = 1f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"wallsNoFlow\": true", text);
            Assert.Contains("\"wallMoveRate\": 2", text);
            Assert.Contains("\"wallFlowBlend\": 0.2", text);
            Assert.Contains("\"wallDiagonals\": false", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((true, 2f, 0.2f, false, 1f), (back.WallsNoFlow, back.WallMoveRate, back.WallFlowBlend, back.WallDiagonals, back.WallClimbRate));   // a file keeps its 1
            // A file saved (no wallMoveRate) with the earlier build's default climb rate takes the earlier build's, once, said.
            File.WriteAllText(path, text.Replace("\"wallMoveRate\": 2,", "", StringComparison.Ordinal));
            var said = new List<string>();
            var old = PlatformSettings.Load(path, said.Add);
            Assert.Equal(1.3f, old.WallClimbRate);
            Assert.Contains(said, l => l.Contains("wallClimbRate is now 1.3", StringComparison.Ordinal));
            File.WriteAllText(path, text.Replace("\"wallMoveRate\": 2,", "", StringComparison.Ordinal).Replace("\"wallClimbRate\": 1,", "\"wallClimbRate\": 2,", StringComparison.Ordinal));
            Assert.Equal(2f, PlatformSettings.Load(path).WallClimbRate);   // one the player chose is kept
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_with_a_walls_note_without_the_flow_climbs_without_it_and_one_with_it_with_it()
    {
        var head = new TapeHeader();
        head.Notes[PersonWallSettings.TapeNote] = "on=1 reach=0.25 rate=1 wrist=50";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal(PersonWallSettings.NoFlow, o.PersonWall);
        head.Notes[PersonWallSettings.TapeNote] = new PersonWallSettings { FlowBlend = 0.2f }.Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 1.3f, 1.5f, 0.2f, true), (o.PersonWall.Flows, o.PersonWall.Rate, o.PersonWall.MoveRate, o.PersonWall.FlowBlend, o.PersonWall.Diagonals));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-wall-flow-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
        PersonGaits = Animation.People.PersonGaitSettings.Off,   // climbing is measured on the earlier build's paces until its rework (the paces' approaches)
    };

    static SessionOptions WithoutFlow(SessionOptions o) => o with { PersonWall = PersonWallSettings.NoFlow };

    static void NeedsWalls(SessionOptions options)
    {
        Session s;
        try { s = Session.Create(options); }
        catch (SessionException e) when (e.Message.Contains("start", StringComparison.OrdinalIgnoreCase)) { Assert.Skip($"the course has no lane {options.Start} (before no flow's)"); throw; }
        using (s)
        {
            var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
            Assert.SkipUnless(person?.Ledge?.Paths.HasWalls == true, "the models repository's files are too old (no wall clips)");
        }
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons) => new(wish, Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    static readonly Vector2 Fwd = new(0f, 1f), Back = new(0f, -1f), ViewLeft = new(-1f, 0f), ViewRight = new(1f, 0f);

    /// <summary>
    /// At a lane: walk up to about half a metre short of its face (8 m on), stand, tap jump with <paramref name="then"/>'s wish held at once (the getting-on with a
    /// move held: forward is the wall's way, so the tap gets on); from the tap <paramref name="then"/> (the ticks since, the session) asks.
    /// </summary>
    static Func<int, Session, ActionFrame> TapHolding(Func<int, Session, ActionFrame> then)
    {
        float from = float.NaN;
        int still = -1;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            if (float.IsNaN(from)) from = b.Feet.X;
            if (still < 0 && b.Feet.X - from >= 8f - 0.5f - 0.25f) still = t;
            if (still < 0) return Frame(Fwd, Buttons.Walk);
            if (t < still + 60) return Frame(Vector2.Zero, Buttons.None);
            if (t == still + 60) return Frame(Fwd, Buttons.Jump);
            return then(t - still - 60, s);
        };
    }

    /// <summary>A sprint at the lane's wall and a jump at it from about 2.4 m out: caught in the air; once on it (hanging), <paramref name="then"/>.</summary>
    static Func<int, Session, ActionFrame> CaughtThen(Func<int, Session, ActionFrame> then)
    {
        int on = -1;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            if (on < 0 && b.State is BodyState.Hanging or BodyState.Traversing) on = t;
            if (on >= 0) return then(t - on, s);
            return Frame(Fwd, b.Feet.X > -69.6f && b.Feet.X < -69.2f && b.State == BodyState.Ground ? Buttons.Jump | Buttons.Sprint : Buttons.Sprint);
        };
    }

    /// <summary>A run of <paramref name="ticks"/> frames: the log, each tick's ledge clip (the motor's) and the wall's moves begun (tick, act, way), the body at the end, the tape.</summary>
    internal static (List<string> Said, List<(int Tick, string Clip, BodyState State, float Z)> Rows, List<(int Tick, string What)> Begun, Tape? Tape) Run(
        SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks, bool record = false)
    {
        var said = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var assets = s.World.Service<CharacterAssets>();
        var paths = assets.Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person?.Ledge?.Paths;
        var tape = record ? new Tape { Header = s.TapeHeader("test") } : null;
        var rows = new List<(int, string, BodyState, float)>();
        var begun = new List<(int, string)>();
        for (int t = 0; t < ticks; t++)
        {
            var frame = plan(t, s);
            s.Step(frame);
            if (tape is not null)
            {
                tape.Frames.Add(frame);
                if (s.Tick % Tape.DigestEvery == 0) tape.Digests[s.Tick] = s.Digest();
            }
            var l = s.World.TryStore<PersonLedge>(out var ls) && ls.Has(s.Player) ? ls.Get(s.Player).Move : default;
            string clip = l.Clip > 0 && paths?.At(l.Clip - 1) is { } c ? c.Name : "-";
            var b = s.World.Get<Body>(s.Player);
            rows.Add(((int)s.Tick, clip, b.State, b.Feet.Z));
            foreach (var x in s.World.Events<WallMoveStarted>().ThisTick)
                if (x.Entity == s.Player) begun.Add(((int)s.Tick, $"{x.Act} {x.Left},{x.Up} p{x.Pace}"));
        }
        return (said, rows, begun, tape);
    }

    [Fact]
    public void On_the_proving_ground_a_held_climb_flows_from_the_getting_on_to_standing_on_the_top_and_climbs_diagonally()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "walls-holds");
        NeedsWalls(o);
        var (said, rows, begun, _) = Run(o, TapHolding((t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None) : Frame(Fwd, Buttons.None)),
                                        14 * World.TicksPerSecond);
        output.WriteLine($"walls-holds, up held from the tap: {string.Join(", ", begun.Select(b => $"{F(b.Tick * World.TickSeconds)} {b.What}"))}");
        foreach (var l in said.Where(l => l.Contains("wall:"))) output.WriteLine("  " + l);
        int on = rows.FindIndex(r => r.Clip == "wall_on_ground"), first = rows.FindIndex(r => r.Clip == "wall_up_quick");
        Assert.True(on >= 0 && first > on, string.Join(", ", begun.Select(b => b.What)));
        Assert.DoesNotContain(rows.GetRange(on, rows.FindIndex(r => r.State == BodyState.Ground && r.Z > 3.5f) - on), r => r.Clip is "wall_idle" or "hang_braced");   // no rest on the way
        Assert.Contains(said, l => l.Contains("wall: over the top into the braced hang"));
        Assert.InRange(rows[^1].Z, 3.95f, 4.06f);
        // Diagonally: up and left from the getting-on, three steps.
        var (s2, _, b2, _) = Run(o, TapHolding((t, s) => Frame(t < 480 ? Vector2.Normalize(Fwd + ViewLeft) : Vector2.Zero, Buttons.None)), 11 * World.TicksPerSecond);
        output.WriteLine($"walls-holds, up and left held: {string.Join(", ", b2.Select(b => b.What))}");
        Assert.Contains(s2, l => l.Contains("wall: a quick move up and left"));
        Assert.True(b2.Count(b => b.What.StartsWith("Move 1,1", StringComparison.Ordinal)) >= 3);
        // The idle between the getting-on and the move, no diagonal.
        var (_, r3, b3, _) = Run(WithoutFlow(o), TapHolding((t, s) => Frame(Vector2.Normalize(Fwd + ViewLeft), Buttons.None)), 11 * World.TicksPerSecond);
        Assert.Contains(r3, r => r.Clip == "wall_idle");
        Assert.DoesNotContain(b3, b => b.What.StartsWith("Move 1,1", StringComparison.Ordinal));
    }

    /// <summary>The six of a play log: the scenes, each on the earlier builds', and how each is timed from the run (seconds).</summary>
    internal static (string Name, string Start, Func<Func<int, Session, ActionFrame>> Plan, int Seconds, Func<List<(int Tick, string Clip, BodyState State, float Z)>, List<(int Tick, string What)>, float> Time)[] Six() =>
    [
        ("caught in the air, to the first move", "walls-holds", () => CaughtThen((t, s) => Frame(t < 400 ? Fwd : Vector2.Zero, Buttons.None)), 9,
            (rows, begun) => Gap(begun, "Catch", "Move")),
        ("each quick move", "walls-holds", () => TapHolding((t, s) => Frame(t < 600 ? Fwd : Vector2.Zero, Buttons.None)), 13,
            (rows, begun) => begun.Where(b => b.What.StartsWith("Move 0,1 p0", StringComparison.Ordinal)).Select(b => b.Tick).ToList() is { Count: >= 3 } m ? (m[2] - m[1]) * World.TickSeconds : float.NaN),
        ("over the top: the clip over it", "walls-holds", () => TapHolding((t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None) : Frame(Fwd, Buttons.None)), 20,
            (rows, begun) => begun.FirstOrDefault(b => b.What.StartsWith("ToHang", StringComparison.Ordinal)) is { What: not null } h && rows.FindIndex(r => r.Clip == "pull_up_braced") is >= 0 and var p
                ? (rows[p].Tick - h.Tick) * World.TickSeconds : float.NaN),
        ("over the top: the pull-up to standing", "walls-holds", () => TapHolding((t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None) : Frame(Fwd, Buttons.None)), 20,
            (rows, begun) => rows.FindIndex(r => r.Clip == "pull_up_braced") is >= 0 and var p && rows.FindIndex(p, r => r.State == BodyState.Ground) is >= 0 and var g
                ? (rows[g].Tick - rows[p].Tick) * World.TickSeconds : float.NaN),
        ("an inside corner", "walls-corners", () => TapHolding((t, s) => Frame(t < 1200 ? ViewRight : Vector2.Zero, Buttons.None)), 17,
            (rows, begun) => rows.FindIndex(r => r.Clip == "wall_corner_in_right") is >= 0 and var a && rows.FindIndex(a, r => r.Clip != "wall_corner_in_right") is >= 0 and var e
                ? (rows[e].Tick - rows[a].Tick) * World.TickSeconds : float.NaN),
        ("off at the foot, to standing", "walls-holds", () => TapHolding((t, s) => Frame(t < 420 ? Fwd : t < 1000 ? Back : Vector2.Zero, Buttons.None)), 16,
            (rows, begun) => begun.FirstOrDefault(b => b.What.StartsWith("Off", StringComparison.Ordinal)) is { What: not null } off && rows.FindIndex(r => r.Tick > off.Tick && r.State == BodyState.Ground) is >= 0 and var g
                ? (rows[g].Tick - off.Tick) * World.TickSeconds : float.NaN),
    ];

    static float Gap(List<(int Tick, string What)> begun, string a, string b)
    {
        int i = begun.FindIndex(x => x.What.StartsWith(a, StringComparison.Ordinal));
        int j = i < 0 ? -1 : begun.FindIndex(i + 1, x => x.What.StartsWith(b, StringComparison.Ordinal));
        return i >= 0 && j > i ? (begun[j].Tick - begun[i].Tick) * World.TickSeconds : float.NaN;
    }

    [Fact]
    public void The_timings_of_the_six_played_scenes_without_the_flow_against_with_it()
    {
        var (course, game, models) = Needs();
        NeedsWalls(On(course, game, models, "walls-holds"));
        var lines = new List<string> { "| the play log (no flow) | no flow | flow | flow / no flow |", "|---|---|---|---|" };   // printed
        foreach (var (name, start, plan, seconds, time) in Six())
        {
            var o = On(course, game, models, start);
            var (_, r0, b0, _) = Run(WithoutFlow(o), plan(), seconds * World.TicksPerSecond);
            var (_, r1, b1, _) = Run(o, plan(), seconds * World.TicksPerSecond);
            float t0 = time(r0, b0), t1 = time(r1, b1);
            lines.Add($"| {name} | {F(t0, "0.000")} s | {F(t1, "0.000")} s | {F(t1 / t0, "0.00")} |");
            Assert.False(float.IsNaN(t0) || float.IsNaN(t1), $"{name}: no flow {t0}, flow {t1}; {string.Join(", ", b1.Select(b => b.What))}");
            // At wallMoveRate (1.5) at least 1.5 times faster; the quick moves at wallClimbRate (1.3) with the flow, faster than the earlier build's and no more than a human
            // speed climber's 2.9 hand moves a second (two a quick move).
            if (name == "each quick move") Assert.True(t1 < 0.75f * t0 && 2f / t1 <= 2.9f, $"{name}: flow {F(t1, "0.000")} s ({F(2f / t1, "0.00")} hand moves a second) against no flow's {F(t0, "0.000")}");
            else Assert.True(t1 <= t0 / 1.5f + 0.02f, $"{name}: flow {F(t1, "0.000")} s against no flow's {F(t0, "0.000")}");
        }
        foreach (var l in lines) output.WriteLine(l);
    }

    [Fact]
    public void A_recording_notes_the_flowing_walls_and_replays_and_one_without_the_flow_without_it()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "walls-holds");
        NeedsWalls(o);
        foreach (var options in new[] { o, WithoutFlow(o) })
        {
            var tape = Run(options, TapHolding((t, s) => Frame(t < 500 ? Vector2.Normalize(Fwd + ViewLeft) : Vector2.Zero, Buttons.None)), 8 * World.TicksPerSecond, record: true).Tape!;
            Assert.Equal(options.PersonWall.Note(), tape.Header.Notes[PersonWallSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonWall, again.PersonWall);
            using var replay = Session.Create(again);
            Assert.Equal(options.PersonWall.Flows, replay.World.TryStore<PersonWallFlow>(out _));   // The earlier build's world has no flow's store: its digests are the earlier build's
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonWallSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 8);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_WALL_FLOW_TAPES</c> (a folder): the tapes the stills and its timings table are drawn from (the six on the earlier build's as <c>no-flow-six-*</c>), and beside
    /// each the ticks to draw (<c>.ticks</c>: where each wall clip, the pull-up or the hang begins, and each wall's move begun) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_WALL_FLOW_TAPES") is { Length: > 0 }, "VITOMIR_WALL_FLOW_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_WALL_FLOW_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var holds = On(course, game, models, "walls-holds");
        var corners = On(course, game, models, "walls-corners");
        Func<int, Session, ActionFrame> Up(int until) => (t, s) => Frame(t < until ? Fwd : Vector2.Zero, Buttons.None);
        Func<int, Session, ActionFrame> Over() => (t, s) => s.World.Get<Body>(s.Player) is { State: BodyState.Ground, Feet.Z: > 3.5f } ? Frame(Vector2.Zero, Buttons.None) : Frame(Fwd, Buttons.None);
        var cases = new List<(string Name, SessionOptions Options, Func<Func<int, Session, ActionFrame>> Plan, int Seconds)>
        {
            ("on-caught-held", holds, () => CaughtThen((t, s) => Frame(t < 150 ? Fwd : Vector2.Zero, Buttons.None)), 6),
            ("on-standing-held", holds, () => TapHolding(Up(190)), 9),
            ("up-three", holds, () => TapHolding(Up(340)), 11),
            ("over-the-top", holds, () => TapHolding(Over()), 15),
            ("released-at-the-top", holds, () => TapHolding((t, s) => s.World.Get<Body>(s.Player).Feet.Z > 2.2f || t > 520 ? Frame(Vector2.Zero, Buttons.None) : Frame(Fwd, Buttons.None)), 15),
            ("diag-up-left-quick", holds, () => TapHolding((t, s) => Frame(t < 380 ? Vector2.Normalize(Fwd + ViewLeft) : Vector2.Zero, Buttons.None)), 12),
            ("diag-down-right-quick", holds, () => TapHolding((t, s) => Frame(t < 330 ? Fwd : t < 600 ? Vector2.Normalize(Back + ViewRight) : Vector2.Zero, Buttons.None)), 14),
            ("diag-up-left-careful", holds, () => TapHolding((t, s) => Frame(t < 620 ? Vector2.Normalize(Fwd + ViewLeft) : Vector2.Zero, t < 620 ? Buttons.Walk : Buttons.None)), 14),
            ("diag-down-right-careful", holds, () => TapHolding((t, s) => Frame(t < 330 ? Fwd : t < 960 ? Vector2.Normalize(Back + ViewRight) : Vector2.Zero, t >= 330 && t < 960 ? Buttons.Walk : Buttons.None)), 18),
            ("corner-in", corners, () => TapHolding((t, s) => Frame(t < 600 ? ViewRight : Vector2.Zero, Buttons.None)), 14),
            ("off-at-the-foot", holds, () => TapHolding((t, s) => Frame(t < 140 ? Fwd : t < 600 ? Back : Vector2.Zero, Buttons.None)), 10),
        };
        foreach (var (name, start, plan, seconds, _) in Six())
        {
            var o = On(course, game, models, start);
            string key = new string([.. name.Where(char.IsLetter).Take(12)]);
            cases.Add(($"six-{key}", o, plan, seconds));
            cases.Add(($"no-flow-six-{key}", WithoutFlow(o), plan, seconds));
        }
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
                bool began = false;
                foreach (var x in session.World.Events<WallMoveStarted>().ThisTick) if (x.Entity == session.Player) began = true;
                if ((what != last || began) && what != "") marks.Add(((int)session.Tick, what));
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
