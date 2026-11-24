// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The player's bow in the free yard (aim first, the ready pose, the arrow where the crosshair is), with 1.1's characters: Christof carries the war bow beside his sword; F swaps to
/// it and he is drawn with 1.1's longBow (his bow clips, the bow's model); a shot held and loosed at a witness 15 m away is recorded
/// and replays to the same digests from the tape's head (its note <c>playerBow</c>), and a tape without the note builds a world with no
/// bow (as earlier); the examples 5 (the player's bow by the player's input) and 6 (the shield raised against a drawn bow, swept on
/// its switch) check and run. Skips without the proving ground or the 1.1 install.
/// </summary>
public sealed class PlayerBowSessionTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-cj-bow-tests-userdata"));
        return (course!, game!);
    }

    static SessionOptions Options(List<string> log, string? bow = SessionOptions.DefaultPlayerBow)
    {
        var (course, game) = Grounds();
        return new SessionOptions
        {
            Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 7, People = 0,
            Place = "40,18,0,-90", Witnesses = ["guard@40,3,0,90"], Night = false, PlayerBow = bow, Say = log.Add,
        };
    }

    /// <summary>F at 0.2 s; aim held from 1.2 s (the aim mode); attack held from 1.6 s for 1.6 s (full draw, held) and let go; aim let go at 4.0 s; still to 4.5 s.</summary>
    static List<ActionFrame> Shot()
    {
        var frames = new List<ActionFrame>();
        int s = World.TicksPerSecond;
        for (int t = 0; t < (int)(4.5f * s); t++)
        {
            var held = t == s / 5 ? Buttons.SwapWeapon : t >= (int)(1.6f * s) && t < (int)(3.2f * s) ? Buttons.Attack : Buttons.None;
            if (t >= (int)(1.2f * s) && t < 4 * s) held |= Buttons.Aim;
            frames.Add(new ActionFrame(Vector2.Zero, Vector2.Zero, held, new Vector2(0.5f, 0.5f), 16f / 9f));
        }
        return frames;
    }

    [Fact]
    public void In_the_free_yard_F_takes_the_bow_a_held_draw_looses_at_the_witness_and_the_tape_replays()
    {
        var log = new List<string>();
        var tape = new Tape();
        var loosed = new List<ArrowLoosed>();
        var spent = new List<ArrowSpent>();
        var swaps = new List<WeaponSwap>();
        int bowKindSeen = -1, meleeKind;
        Vector3 crosshairAtLoose;
        using (var session = Session.Create(Options(log)))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            Assert.Equal("war_bow:24", tape.Header.Notes[SessionOptions.PlayerBowNote]);
            Assert.Equal("straight", tape.Header.Notes[PlayerBowSettings.TapeNote]);        // along the line, a real arrow's flight
            var w = session.World;
            meleeKind = w.Get<CharacterAnimation>(session.Player).Kind;
            Assert.True(w.Has<BowLook>(session.Player));
            Vector3 crosshair = default;
            foreach (var frame in Shot())
            {
                if (w.Get<Bow>(session.Player).Drawn && frame.Holds(Buttons.Attack)) PlayerBow.Crosshair(w, session.Player, out crosshair);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                loosed.AddRange(w.Events<ArrowLoosed>().ThisTick);
                spent.AddRange(w.Events<ArrowSpent>().ThisTick);
                swaps.AddRange(w.Events<WeaponSwap>().ThisTick);
                if (w.Get<Bow>(session.Player).InHand) bowKindSeen = w.Get<CharacterAnimation>(session.Player).Kind;
            }
            tape.Digests[session.Tick] = session.Digest();
            crosshairAtLoose = crosshair;
            Assert.Equal(BowState.Ready, w.Get<Bow>(session.Player).State);              // aim let go: the bow low again
            Assert.True(w.Get<Bow>(session.Player).OnString);                            // the next arrow on the string
            Assert.True(BowShots.TurnOf(w, session.Player) != 0f);
        }
        Assert.Contains(log, l => l.Contains("bow: Christof drawn with 1.1's longBow", StringComparison.Ordinal) && l.Contains("stand_bow ChrisStandBow", StringComparison.Ordinal)
                                 && l.Contains("composed with the ready pose", StringComparison.Ordinal));
        Assert.Equal([(true, false), (true, true)], swaps.Select(x => (x.ToRanged, x.Ended)));   // F: the swap begun, 0.6 s later the bow in hand
        Assert.NotEqual(meleeKind, bowKindSeen);                                    // drawn and posed as the bow kind while it is in hand
        var l = Assert.Single(loosed);
        Assert.Equal(1f, l.DrawShare, 3);
        Assert.InRange(l.YawDegrees, -95f, -85f);                                   // toward the witness, down the yard (−Y)
        Assert.Equal(0f, l.SpreadDegrees);                                          // no spread for the player
        var s = Assert.Single(spent);
        Assert.True(s.DistanceMetres > 12f, $"flew {s.DistanceMetres:0.0} m, met {s.Met}");
        if (s.End == ArrowEnd.Met) Assert.True(Vector3.Distance(crosshairAtLoose, s.Point) < 0.1f || s.Met != ContactMet.Level,   // where the crosshair was (or a body in front of it)
            $"the crosshair at {crosshairAtLoose}, the arrow met the {s.Met} at {s.Point}");

        // Written, read back, replayed from its head: the same digests.
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-cj-bow-{Environment.ProcessId}.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        File.Delete(path);
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(back.Header);
        Assert.Equal(("war_bow", 24), (again.PlayerBow, again.PlayerArrows));
        Assert.False(again.PlayerBowAim!.NoAim);
        Assert.False(again.PlayerBowAim.Solve);
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in back.Frames)
        {
            replay.Step(frame);
            if (back.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 15, $"{compared} digests compared");

        // A tape (the bow's note, no aim note): the earlier build's rules.
        back.Header.Notes.Remove(PlayerBowSettings.TapeNote);
        Assert.True(new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(back.Header).PlayerBowAim!.NoAim);
        // A older tape (no note): no bow in its world.
        back.Header.Notes.Remove(SessionOptions.PlayerBowNote);
        var old = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(back.Header);
        Assert.Null(old.PlayerBow);
        using var before = Session.Create(old);
        Assert.False(before.World.TryStore<Bow>(out _));
    }

    static string Example(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        return Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples", name);
    }

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-cj-bow-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A measure's value in each run of runs.csv, by the run's point.</summary>
    static List<(int Point, double Value)> Column(string folder, string measure)
    {
        var lines = File.ReadAllLines(Path.Combine(folder, "runs.csv"));
        var head = Cells(lines[0]);
        int p = head.IndexOf("point"), m = head.IndexOf(measure);
        Assert.True(m >= 0, $"no column {measure}");
        return [.. lines.Skip(1).Select(Cells).Select(c => (int.Parse(c[p], CultureInfo.InvariantCulture), double.Parse(c[m], CultureInfo.InvariantCulture)))];
    }

    /// <summary>A CSV line's cells (a quoted cell may hold commas and doubled quotes).</summary>
    static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var b = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { b.Append('"'); i++; } else if (c == '"') quoted = false; else b.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(b.ToString()); b.Clear(); }
            else b.Append(c);
        }
        cells.Add(b.ToString());
        return cells;
    }

    [Fact]
    public void Example_5_the_players_bow_by_the_players_input_checks_and_runs()
    {
        var (course, install) = Grounds();
        var options = new SessionOptions { Level = course, Game = install, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        using var game = ScenarioGame.Load(options, course);
        var load = ScenarioSpec.Load(Example("10-player-input-bow-shot.scenario.json"), game, course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!);
        string out_ = Folder("ex05");
        var (results, exit) = ScenarioRunner.Execute(plan, options, out_, _ => { }, new ScenarioRunOptions { Runs = 2 });
        Assert.Equal(2, results.RunCounts!.Completed);
        Assert.All(Column(out_, "swaps"), c => Assert.Equal(2, c.Value));          // F: the swap's start and end
        Assert.All(Column(out_, "arrowsLoosed"), c => Assert.Equal(2, c.Value));   // two held draws, two arrows
        Assert.All(Column(out_, "arrowsOnBody"), c => Assert.Equal(2, c.Value));   // both where the crosshair was, his chest
        Assert.Contains(exit, new[] { 0, 3, 4 });
    }

    [Fact]
    public void The_ready_pose_is_1_1_s_full_draw_with_the_draw_hand_on_the_string_and_the_draw_joins_it_without_a_jump()
    {
        Grounds();
        using var install = GameInstall.TryOpen() ?? throw new Xunit.Sdk.XunitException("no install");
        var library = new Vitomir.Platform.Assets.OneOne.OneOneLibrary(install);
        var c = BowLooks.Character(library, "Christof", ready: true);
        Assert.NotNull(c);
        var clip = Assert.IsType<BowReadyClip>(c!.Actions!.Quick[0].Clip);
        var skeleton = c.Model.Skeleton;
        var limbs = c.Limbs!;
        var local = new BoneTransform[skeleton.Count];
        var model = new System.Numerics.Matrix4x4[skeleton.Count];
        (Vector3 Bow, Vector3 Draw) Hands(IAnimationClip from, float at)
        {
            from.Sample(at, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            return (model[limbs.LeftArm.End].Translation, model[limbs.RightArm.End].Translation);
        }
        var full = Hands(clip.Source, clip.FullDrawAt);
        var ready = Hands(clip, clip.ReadyAt);
        float fullSeparation = Vector3.Distance(full.Bow, full.Draw);
        Assert.True(Vector3.Distance(ready.Bow, full.Bow) < 1e-3f * fullSeparation);                    // the bow held as at 1.1's full draw: up, near vertical
        Assert.InRange(Vector3.Distance(ready.Bow, ready.Draw), BowReadyClip.RestShare * fullSeparation - clip.RestMiss - 1e-3f,
                       BowReadyClip.RestShare * fullSeparation + clip.RestMiss + 1e-3f);                  // the draw hand on the string at rest
        // The draw ends on 1.1's own full draw, and the loose goes on from it, without a jump.
        var end = Hands(clip, clip.FullDraw - 1e-4f);
        Assert.True(Vector3.Distance(end.Draw, full.Draw) < 0.01f * fullSeparation);
        var after = Hands(clip, clip.FullDraw + 1e-4f);
        Assert.True(Vector3.Distance(after.Draw, end.Draw) < 0.01f * fullSeparation);
        // The draw hand moves back evenly along the arrow's line: halfway through the draw, about halfway from the rest to full.
        var half = Hands(clip, (clip.ReadyAt + clip.FullDraw) / 2f);
        float share = (Vector3.Distance(half.Bow, half.Draw) - Vector3.Distance(ready.Bow, ready.Draw)) / (fullSeparation - Vector3.Distance(ready.Bow, ready.Draw));
        Assert.InRange(share, 0.3f, 0.7f);
        // And the raise starts from 1.1's own first frame (the bow low).
        var low = Hands(clip.Source, 0f);
        var start = Hands(clip, 0f);
        Assert.True(Vector3.Distance(low.Bow, start.Bow) < 1e-4f * fullSeparation);
    }

    [Fact]
    public void Example_12_the_archer_brain_by_range_checks_and_runs()
    {
        var (course, install) = Grounds();
        var options = new SessionOptions { Level = course, Game = install, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        using var game = ScenarioGame.Load(options, course);
        var load = ScenarioSpec.Load(Example("12-archer-brain-by-range.scenario.json"), game, course);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!);
        string out_ = Folder("ex12");
        var (results, exit) = ScenarioRunner.Execute(plan, options, out_, _ => { }, new ScenarioRunOptions { Runs = 1 });
        Assert.Equal(9, results.RunCounts!.Completed);                               // 3 distances by 3 Ranges
        Assert.All(Column(out_, "arrowsLoosed"), c => Assert.Equal(10, c.Value));
        Assert.All(Column(out_, "aimsDrawn"), c => Assert.True(c.Value >= 10));    // an error drawn for each arrow, as it was nocked
        var body = Column(out_, "arrowsOnBody");
        Assert.True(body.Single(c => c.Point == 2).Value >= body.Single(c => c.Point == 6).Value);   // Range 5 at 10 m hits at least as often as Range 0 at 30 m
        Assert.Equal(0, exit);
    }

    [Fact]
    public void A_foe_archer_in_play_shoots_at_the_player_its_error_drawn_before_each_shot()
    {
        var log = new List<string>();
        var options = Options(log) with { Foes = ["archer@40,-2,0,90"], Witnesses = [] };
        using var session = Session.Create(options);
        var w = session.World;
        var loosed = new List<ArrowLoosed>();
        var aimed = new List<ArcherAimed>();
        var spent = new List<ArrowSpent>();
        for (int t = 0; t < 10 * World.TicksPerSecond; t++)
        {
            session.Step(ActionFrame.Idle);
            loosed.AddRange(w.Events<ArrowLoosed>().ThisTick);
            aimed.AddRange(w.Events<ArcherAimed>().ThisTick);
            spent.AddRange(w.Events<ArrowSpent>().ThisTick);
        }
        Assert.Contains(log, l => l.Contains("the archer brain against the player", StringComparison.Ordinal));
        var archer = session.Foes[0];
        Assert.True(loosed.Count(l => l.Archer == archer) >= 3, $"{loosed.Count} loosed");
        Assert.All(loosed, l => Assert.Equal(0f, l.SpreadDegrees));
        Assert.All(loosed.Where(l => l.Archer == archer), l => Assert.Contains(aimed, a => a.Shot == l.Shot && a.Tick < l.Tick));
        Assert.Contains(spent, s => s.Archer == archer && s.Target == session.Player);   // some reach him (Range 2 at 20 m: about half)
        Assert.True(w.Has<BowLook>(archer));                                              // drawn with 1.1's bow
    }

    [Fact]
    public void Example_6_the_shield_raised_against_a_drawn_bow_checks_and_runs_and_its_switch_turns_it_off()
    {
        var (course, install) = Grounds();
        var options = new SessionOptions { Level = course, Game = install, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        using var game = ScenarioGame.Load(options, course);
        var load = ScenarioSpec.Load(Example("11-shield-against-a-drawn-bow.scenario.json"), game, course);   // with the game: its presets ("guard")
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!) with { TracePolicy = TracePolicy.All };
        string out_ = Folder("ex06");

        var (results, _) = ScenarioRunner.Execute(plan, options, out_, _ => { }, new ScenarioRunOptions { Runs = 3 });
        Assert.Equal(6, results.RunCounts!.Completed);
        var raised = Column(out_, "shieldRaised");
        var loosed = Column(out_, "arrowsLoosed");
        var onShield = Column(out_, "arrowsOnShield");
        string seen = string.Join(" ", raised.Select((r, i) => $"{r.Point}:{r.Value} raised, {loosed[i].Value} loosed, {onShield[i].Value} on the shield"))
            + " | " + string.Join(", ", File.ReadAllLines(Path.Combine(out_, "events", "p000-r000.events.jsonl")).Skip(1)
                .Select(x => System.Text.Json.JsonDocument.Parse(x).RootElement.GetProperty("kind").GetString()).GroupBy(k => k).Select(g => $"{g.Key} {g.Count()}"));
        Assert.True(raised.Where(r => r.Point == 0).All(r => r.Value >= 1), $"the guard raised his shield against the drawn bow: {seen}");
        Assert.True(raised.Where(r => r.Point == 1).All(r => r.Value == 0), $"shield-at-bow-off: the reach-only guard: {seen}");
    }
}
