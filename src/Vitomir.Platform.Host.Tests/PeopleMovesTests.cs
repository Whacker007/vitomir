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
/// As the program gives it (tier 1 on the engine, part 1: the models repository's clips): the settings (<c>movesOff</c> / <c>--moves-off</c>,
/// <c>ladderSprintRate</c>, <c>toeBendLimitDeg</c>) and the note <c>moves</c> (none: the earlier build's); on the proving ground (skip without it, the 1.1 install or the models
/// stream's files and after): the standing and the falling grab at a braced wall on their twins, the hurdle at the step-over's 0.5 m rail and the
/// speed vault at the bench and the trough at a run, the ladders on clips (the upright one up and over and back down, the leaning one, the side landing off and
/// on, the ledge beside the ladder and back), the earlier build's behind the switch, and a recording replays digest for digest.
/// </summary>
public sealed class PeopleMovesTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_tier_1_s_moves_and_the_switch_turns_them_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonMovesFor(new Cli([]));
        Assert.True(on.On && on.BracedGrabs && on.LowVaults && on.Ladders);
        Assert.Equal((2f, 40f), (on.LadderSprintRate, on.ToeBendLimitDegrees));
        Assert.Equal("on=1 braced=1 low=1 ladders=1 sprint=2 lean=2 climb=1.5 move=1.5", on.Note());   // the ladders' rates
        Assert.False(plain.PersonMovesFor(new Cli(["--moves-off"])).On);
        Assert.Equal("off", plain.PersonMovesFor(new Cli(["--moves-off"])).Note());
        Assert.False(new PlatformSettings { MovesOff = true }.PersonMovesFor(new Cli([])).On);
        Assert.False(plain.PersonMovesFor(new Cli(["--commit-off"])).On);   // it rides on the commit point
        Assert.Equal(1.5f, new PlatformSettings { LadderSprintRate = 1.5f }.PersonMovesFor(new Cli([])).LadderSprintRate);
        Assert.Equal(2f, new PlatformSettings { LadderSprintRate = 9f }.PersonMovesFor(new Cli([])).LadderSprintRate);   // out of 1 to 3: the default
        Assert.Equal(2f * DetMath.DegToRad, on.Rules().LeanTolerance, 5);
        Assert.Equal(4f, new PlatformSettings { LadderLeanTolerance = 4f }.PersonMovesFor(new Cli([])).LeanToleranceDegrees);
        Assert.Equal(2f, new PlatformSettings { LadderLeanTolerance = 30f }.PersonMovesFor(new Cli([])).LeanToleranceDegrees);   // out of 0.5 to 7: the default
        var stiff = new PlatformSettings { ToeBendLimitDeg = 20f }.PersonMovesFor(new Cli([]));
        Assert.EndsWith(" toe=20", stiff.Note());
        Assert.Equal(0.018f, PersonMovesSettings.BracedFootBack(20f), 4);
        Assert.Equal(0f, PersonMovesSettings.BracedFootBack(40f), 4);
        Assert.Equal(0.028f, PersonMovesSettings.BracedFootBack(0f), 4);
        Assert.Equal(0.0135f, PersonMovesSettings.BracedFootBack(25f), 4);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-moves-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { MovesOff = true, LadderSprintRate = 1.5f, ToeBendLimitDeg = 30f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"movesOff\": true", text);
            Assert.Contains("\"ladderSprintRate\": 1.5", text);
            Assert.Contains("\"toeBendLimitDeg\": 30", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.MovesOff);
            Assert.Equal((1.5f, 30f), (back.LadderSprintRate, back.ToeBendLimitDeg));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_moves_note_has_the_moves_off()
    {
        var head = new TapeHeader();
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.False(o.PersonMoves.On);
        head.Notes[PersonMovesSettings.TapeNote] = (new PersonMovesSettings() with { LadderSprintRate = 1.75f, LowVaults = false }).Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((true, 1.75f, false, true), (o.PersonMoves.On, o.PersonMoves.LadderSprintRate, o.PersonMoves.LowVaults, o.PersonMoves.Ladders));
        head.Notes[PersonMovesSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonMoves.On);
        // A note (no climb, move) replays the ladders at the clips' own rates; the earlier build's carries them.
        head.Notes[PersonMovesSettings.TapeNote] = "on=1 braced=1 low=1 ladders=1 sprint=2 lean=2";
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((1f, 1f), (o.PersonMoves.LadderClimbRate, o.PersonMoves.LadderMoveRate));
        head.Notes[PersonMovesSettings.TapeNote] = new PersonMovesSettings { LadderClimbRate = 2f, LadderMoveRate = 1.25f }.Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal((2f, 1.25f), (o.PersonMoves.LadderClimbRate, o.PersonMoves.LadderMoveRate));
    }

    [Fact]
    public void The_settings_give_the_ladders_rates()
    {
        var on = new PlatformSettings().PersonMovesFor(new Cli([]));
        Assert.Equal((1.5f, 1.5f), (on.LadderClimbRate, on.LadderMoveRate));
        Assert.Equal((2f, 3f), (new PlatformSettings { LadderClimbRate = 2f, LadderMoveRate = 3f }.PersonMovesFor(new Cli([])) is var r ? (r.LadderClimbRate, r.LadderMoveRate) : default));
        Assert.Equal((1.5f, 1.5f), (new PlatformSettings { LadderClimbRate = 0.5f, LadderMoveRate = 4f }.PersonMovesFor(new Cli([])) is var q ? (q.LadderClimbRate, q.LadderMoveRate) : default));   // out of 1 to 3: the defaults
        Assert.Equal(3f, on.Rules().LadderClimbRate * 2f);   // sprint's 2 on the climb's 1.5: the most, 3
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-moves-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { LadderClimbRate = 1.25f, LadderMoveRate = 2f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"ladderClimbRate\": 1.25", text);
            Assert.Contains("\"ladderMoveRate\": 2", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((1.25f, 2f), (back.LadderClimbRate, back.LadderMoveRate));
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
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-moves-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string? place = null) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Place = place, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static SessionOptions WithMovesOff(SessionOptions o) => o with { PersonMoves = PersonMovesSettings.Off };

    /// <summary>Skips without tier 1's clips on the player (an older set: the twins, the low vaults, the ladders), or without the course's lane.</summary>
    static void NeedsTier1(SessionOptions options, bool ladders = false)
    {
        Session s;
        try { s = Session.Create(options); }
        catch (SessionException e) when (e.Message.Contains("start", StringComparison.OrdinalIgnoreCase)) { Assert.Skip($"the course has no lane {options.Start} (before on)"); throw; }
        using (s)
        {
            var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
            Assert.SkipUnless(person?.Ledge?.Paths.Clips.Any(c => c.Twin) == true && person.Over?.Paths?.HasLow == true && (!ladders || person.Ladder is not null),
                              "the models repository's files are too old (no tier 1 clips)");
        }
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons) => new(wish, Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>A run of <paramref name="ticks"/> frames from <paramref name="plan"/> (the tick, the session), its log and the player's clips in order.</summary>
    static (List<string> Said, List<string> Clips, Body End) Run(SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks) => Run(options, plan, ticks, false, out _);

    static (List<string> Said, List<string> Clips, Body End) Run(SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks, bool record, out Tape? tape)
    {
        var said = new List<string>();
        var clips = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var assets = s.World.Service<CharacterAssets>();
        tape = record ? new Tape { Header = s.TapeHeader("test") } : null;
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
        return (said, clips, s.World.Get<Body>(s.Player));
    }

    /// <summary>At a ladder lane: walk to the ladder, use at its foot, forward up and over; standing a moment, use (facing the drop), back down and off.</summary>
    static Func<int, Session, ActionFrame> LadderTrip()
    {
        int stage = 0, at = 0;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            switch (stage)
            {
                case 0:   // walk up to it
                    if (s.World.Get<Traversal>(s.Player).Kind != TraversalKind.None) { stage = 1; return Frame(Vector2.Zero, Buttons.None); }
                    return Frame(new Vector2(0f, 1f), Buttons.Walk | (t % 20 == 0 ? Buttons.Use : Buttons.None));
                case 1:   // up and over
                    if (b.State == BodyState.Ground && b.Feet.Z > 1f && s.World.Get<Traversal>(s.Player).Kind == TraversalKind.None) { stage = 2; at = t; }
                    return Frame(new Vector2(0f, 1f), Buttons.None);
                case 2:   // a moment on the top, then use
                    if (t == at + 40) { stage = 3; at = t; return Frame(Vector2.Zero, Buttons.Use); }
                    return Frame(Vector2.Zero, Buttons.None);
                default:   // back down (the view still along +X: back is down)
                    return Frame(t < at + 400 ? Vector2.Zero : new Vector2(0f, -1f), Buttons.None);
            }
        };
    }


    /// <summary>At the side landing's lane: on at the foot, the wish to the left onto the landing, use there back on, back off at the foot.</summary>
    static Func<int, Session, ActionFrame> SidePlan()
    {
        int stage = 0, at = 0;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            var k = s.World.Get<Traversal>(s.Player).Kind;
            if (stage == 0 && k != TraversalKind.None) stage = 1;
            if (stage == 1 && b.State == BodyState.Ladder && k == TraversalKind.Ladder) { stage = 2; at = t; }
            if (stage == 2 && b.State == BodyState.Ground) { stage = 3; at = t; }
            if (stage == 3 && t == at + 40) { stage = 4; return Frame(Vector2.Zero, Buttons.Use); }
            if (stage == 4 && b.State == BodyState.Ladder && k == TraversalKind.Ladder) { stage = 5; at = t; }
            return stage switch
            {
                0 => Frame(new Vector2(0f, 1f), Buttons.Walk | (t % 20 == 0 ? Buttons.Use : Buttons.None)),
                2 => Frame(t < at + 20 ? Vector2.Zero : new Vector2(-1f, 0f), Buttons.None),   // the view's left: the climber's left
                5 => Frame(t < at + 20 ? Vector2.Zero : new Vector2(0f, -1f), Buttons.None),
                _ => Frame(Vector2.Zero, Buttons.None),
            };
        };
    }

    /// <summary>At the ledge's lane: on at the foot, a cycle up to rung 3's place, the wish to the right into the braced hang, a moment, the wish to the left back on.</summary>
    static Func<int, Session, ActionFrame> LedgePlan()
    {
        int stage = 0, at = 0;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            var k = s.World.Get<Traversal>(s.Player).Kind;
            var climb = s.World.TryStore<PersonLadder>(out var ls) && ls.Has(s.Player) ? ls.Get(s.Player).Move : default;
            if (stage == 0 && k != TraversalKind.None) stage = 1;
            if (stage == 1 && b.State == BodyState.Ladder && climb.Rung == 3 && climb.Way == 0) { stage = 2; at = t; }
            if (stage == 2 && b.State == BodyState.Hanging) { stage = 3; at = t; }
            if (stage == 3 && t == at + 60) stage = 4;
            return stage switch
            {
                0 => Frame(new Vector2(0f, 1f), Buttons.Walk | (t % 20 == 0 ? Buttons.Use : Buttons.None)),
                1 => Frame(climb.Rung < 3 && climb.Time < 1f ? new Vector2(0f, 1f) : Vector2.Zero, Buttons.None),
                2 => Frame(new Vector2(1f, 0f), Buttons.None),
                4 => Frame(new Vector2(-1f, 0f), Buttons.None),
                _ => Frame(Vector2.Zero, Buttons.None),
            };
        };
    }

    /// <summary>At a grab lane: walk up to 0.45 m short of the face, stand half a second, tap jump.</summary>
    static Func<int, Session, ActionFrame> Tap()
    {
        float from = float.NaN;
        int still = -1;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            if (float.IsNaN(from)) from = b.Feet.X;
            // Run up to 0.45 m short of the face (the lane's face 8 m on), stand, then tap jump.
            if (still < 0 && b.Feet.X - from >= 8f - 0.45f - 0.3f) still = t;
            if (still < 0) return Frame(new Vector2(0f, 1f), Buttons.Walk);
            return Frame(Vector2.Zero, t == still + 60 ? Buttons.Jump : Buttons.None);
        };
    }

    [Fact]
    public void On_the_proving_ground_the_ladders_are_climbed_on_their_clips()
    {
        var (course, game, models) = Needs();
        foreach (var (lane, prefix) in new[] { ("not-built-ladder", "ladder_v_"), ("ladders-lean", "ladder_") })
        {
            var o = On(course, game, models, lane);
            NeedsTier1(o, ladders: true);
            var (said, clips, end) = Run(o, LadderTrip(), 30 * World.TicksPerSecond);
            var ours = clips.Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            output.WriteLine($"{lane}: {string.Join(", ", ours)}");
            foreach (var l in said.Where(l => l.Contains("ladder:") || l.Contains("move:"))) output.WriteLine("  " + l);
            Assert.Equal([prefix + "on_bottom", prefix + "idle", prefix + "up", prefix + "off_top", prefix + "on_top", prefix + "idle", prefix + "down", prefix + "off_bottom"],
                         ours.Where((c, k) => k == 0 || c != ours[k - 1]).ToList());
            Assert.Contains(said, l => l.Contains("ladder: on ") && l.Contains("from its foot"));
            Assert.Contains(said, l => l.Contains("over the top"));
            Assert.Contains(said, l => l.Contains("ladder: on ") && l.Contains("from its top"));
            Assert.Contains(said, l => l.Contains("stepped off at the foot"));
            Assert.Equal(BodyState.Ground, end.State);
            Assert.True(end.Feet.Z < 0.02f);
            // The stand-in.
            var (_, old, _) = Run(WithMovesOff(o), LadderTrip(), 12 * World.TicksPerSecond);
            Assert.DoesNotContain(old, c => c.StartsWith("ladder_", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void On_the_proving_ground_a_side_landing_and_a_ledge_beside_the_ladder()
    {
        var (course, game, models) = Needs();
        // The side landing: up on the ladder, the wish to the left at rung 1's place: onto the landing; use there: back on; back: off at the foot.
        {
            var o = On(course, game, models, "ladders-side");
            NeedsTier1(o, ladders: true);
            var (said, clips, end) = Run(o, SidePlan(), 20 * World.TicksPerSecond);
            var ours = clips.Where(c => c.StartsWith("ladder_v_", StringComparison.Ordinal)).ToList();
            output.WriteLine($"ladders-side: {string.Join(", ", ours)}");
            foreach (var l in said.Where(l => l.Contains("ladder:"))) output.WriteLine("  " + l);
            Assert.Contains("ladder_v_off_side_l", ours);
            Assert.Contains("ladder_v_on_side_l", ours);
            Assert.Contains(said, l => l.Contains("onto a landing beside it"));
            Assert.Contains(said, l => l.Contains("from its side (a landing)"));
            Assert.True(end.State == BodyState.Ground && end.Feet.Z < 0.02f);
        }
        // The ledge: up a cycle to rung 3, the wish to the right: into the braced hang; the wish to the left: back onto the ladder.
        {
            var o = On(course, game, models, "ladders-ledge");
            NeedsTier1(o, ladders: true);
            var (said, clips, end) = Run(o, LedgePlan(), 20 * World.TicksPerSecond);
            var ours = clips.Where(c => c.StartsWith("ladder_v_", StringComparison.Ordinal) || c.StartsWith("hang_", StringComparison.Ordinal)).ToList();
            output.WriteLine($"ladders-ledge: {string.Join(", ", ours)}");
            foreach (var l in said.Where(l => l.Contains("ladder:") || l.Contains("move:"))) output.WriteLine("  " + l);
            Assert.Contains("ladder_v_to_hang_braced_r", ours);
            Assert.Contains("hang_braced", ours);
            Assert.Contains("ladder_v_from_hang_braced_r", ours);
            Assert.Contains(said, l => l.Contains("into a hang beside it"));
            Assert.Contains(said, l => l.Contains("from its side (a hang)"));
            Assert.Equal(BodyState.Ladder, end.State);
        }
    }

    [Fact]
    public void On_the_proving_ground_the_braced_twins_and_the_low_vaults()
    {
        var (course, game, models) = Needs();
        var grab = On(course, game, models, "grab-2.2");
        NeedsTier1(grab);
        // A standing tap close under the 2.2 m lip: the braced twin, no brace after it; the earlier build's the free one and the brace.
        var (_, db, _) = Run(grab, Tap(), 12 * World.TicksPerSecond);
        var (_, da, _) = Run(WithMovesOff(grab), Tap(), 12 * World.TicksPerSecond);
        output.WriteLine($"grab-2.2 tapped: on {string.Join(", ", db.Where(c => c.StartsWith("grab_") || c.StartsWith("hang_")))}; off {string.Join(", ", da.Where(c => c.StartsWith("grab_") || c.StartsWith("hang_")))}");
        Assert.Contains("grab_jump_up_braced", db);
        Assert.DoesNotContain("hang_brace_on", db);
        Assert.Contains("grab_jump_up", da);
        Assert.Contains("hang_brace_on", da);
        // The low vaults at a run (always run): the hurdle at the step-over's 0.5 m rail, the speed vault at the bench and the trough; the earlier build's stand-ins.
        foreach (var (lane, clip, old) in new[] { ("step-over-0.50", "hurdle_low", "step-over"), ("sprint-vault-bench", "vault_speed", "stop"), ("sprint-vault-trough", "vault_speed", "stop") })
        {
            var o = On(course, game, models, lane);
            var (said, clips, _) = Run(o, (t, s) => Frame(new Vector2(0f, 1f), Buttons.None), 5 * World.TicksPerSecond);
            var (oldSaid, oldClips, _) = Run(WithMovesOff(o), (t, s) => Frame(new Vector2(0f, 1f), Buttons.None), 5 * World.TicksPerSecond);
            output.WriteLine($"{lane} at a run: {string.Join(", ", said.Where(l => l.Contains("move:")))}; off {string.Join(", ", oldSaid.Where(l => l.Contains("move:") || l.Contains("stop:")).Take(2))}");
            Assert.Contains(clip, clips);
            Assert.Contains(said, l => l.Contains("move: vault begins"));
            if (old == "step-over") Assert.Contains(oldSaid, l => l.Contains("move: step-over begins"));
            else Assert.DoesNotContain(oldSaid, l => l.Contains("move: "));
        }
    }

    [Fact]
    public void A_recording_notes_the_moves_and_replays_on_them_and_one_without_them_without_them()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "not-built-ladder");
        NeedsTier1(o, ladders: true);
        foreach (var options in new[] { o, WithMovesOff(o) })
        {
            Run(options, LadderTrip(), 14 * World.TicksPerSecond, true, out var made);
            var tape = made!;
            Assert.Equal(options.PersonMoves.Note(), tape.Header.Notes[PersonMovesSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonMoves, again.PersonMoves);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonMovesSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 10);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_MOVES_TAPES</c> (a folder): the tapes the stills are drawn from, each with the earlier build's (<c>…-now</c>) and the earlier build's (<c>…-before</c>), and beside each
    /// the ticks to draw (<c>.ticks</c>: where a ladder's, a grab's, a hang's or a vault's clip begins) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_MOVES_TAPES") is { Length: > 0 }, "VITOMIR_MOVES_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_MOVES_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static Func<int, Session, ActionFrame> Ahead(Buttons b) => (t, s) => Frame(new Vector2(0f, 1f), b);
        var grab = On(course, game, models, "grab-2.6");
        using (var probe = Session.Create(grab))
        {
            var at = probe.World.Get<Body>(probe.Player).Feet;
            grab = grab with { Place = string.Create(CultureInfo.InvariantCulture, $"{at.X + 7.5f:0.###},{at.Y:0.###},{at.Z + 1.4f:0.###},0") };
        }
        var cases = new (string Name, SessionOptions Options, Func<Func<int, Session, ActionFrame>> Plan, int Seconds)[]
        {
            ("stand22", On(course, game, models, "grab-2.2"), Tap, 9),
            ("fall26", grab, () => (t, s) => Frame(Vector2.Zero, Buttons.Use), 4),
            ("hurdle050", On(course, game, models, "step-over-0.50"), () => Ahead(Buttons.None), 5),
            ("trough-run", On(course, game, models, "sprint-vault-trough"), () => Ahead(Buttons.None), 5),
            ("trough-sprint", On(course, game, models, "sprint-vault-trough"), () => Ahead(Buttons.Sprint), 4),
            ("bench-run", On(course, game, models, "sprint-vault-bench"), () => Ahead(Buttons.None), 5),
            ("bench-sprint", On(course, game, models, "sprint-vault-bench"), () => Ahead(Buttons.Sprint), 4),
            ("ladder-v", On(course, game, models, "not-built-ladder"), LadderTrip, 32),
            ("ladder-lean", On(course, game, models, "ladders-lean"), LadderTrip, 32),
            ("ladder-side", On(course, game, models, "ladders-side"), SidePlan, 16),
            ("ladder-ledge", On(course, game, models, "ladders-ledge"), LedgePlan, 16),
        };
        foreach (var (name, options, plan, seconds) in cases)
            foreach (var (o, suffix) in new[] { (options, "now"), (WithMovesOff(options), "before") })
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
                    var kind = session.World.Get<Traversal>(session.Player).Kind;
                    string what = kind == TraversalKind.None && !clip.StartsWith("hang_", StringComparison.Ordinal) ? "" : clip;
                    if (what != last && what != "") marks.Add(((int)session.Tick, what));
                    last = what;
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
                File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
                output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
            }
    }
}
