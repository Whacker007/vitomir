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
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The models repository's starts and stops (<see cref="PersonStarts"/>, <see cref="PeopleStarts"/>, <see cref="PersonStep"/>) on the body's
/// own motor: the player's on the spring clips, its capsule on the 0.2 s spring as before (the same speeds tick for tick as without the clips) save
/// that a stop waits for its clip's phase at its speed; everyone else's on the natural clips, the capsule moved by the clip's speed.
/// The clips chosen by foot and phase, the loop taking over at a start's exit, a stop ending in the stand; a set without them and the earlier build's
/// switch have no store. Skips without the models repository's files, or.
/// </summary>
public sealed class StartStopClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonStarts Starts(CharacterAssets assets, int kind)
    {
        var starts = assets.Kind(kind).Person!.Starts;
        Assert.SkipUnless(starts is not null, "the models repository's files are too old (no starts and stops)");
        return starts!;
    }

    /// <summary>A world with the motor (the player's spring) and the animation, the starts as <paramref name="starts"/>, one body of the player's kind on the spring (<paramref name="player"/>) or not.</summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonStartSettings? starts, bool player)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: starts);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = player ? MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox) : MovementServices.SpawnBody(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        if (player) w.Remove<Controlled>(e);   // the test asks its intent itself
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = player ? 1 : 2 });
        return (w, e);
    }

    static void Ask(World w, EntityId e, float share, Gait gait)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = new Vector2(share, 0f);
        intent.Gait = gait;
        intent.Faces = true;
        intent.Facing = 0f;
    }

    sealed record Run(List<float> Speed, List<string> Clip, List<float> Time, List<StepMode> Mode, List<float> X);

    /// <summary>Settled, then asked to go for <paramref name="go"/> s at <paramref name="gait"/>, then nothing for <paramref name="rest"/> s.</summary>
    static Run Drive(World w, EntityId e, CharacterAssets assets, Gait gait, float go, float rest)
    {
        var r = new Run([], [], [], [], []);
        for (int i = 0; i < 30; i++) { Ask(w, e, 0f, gait); w.Step(); }
        int goTicks = (int)(go / Tick), all = goTicks + (int)(rest / Tick);
        for (int i = 0; i < all; i++)
        {
            Ask(w, e, i < goTicks ? 1f : 0f, gait);
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            r.Speed.Add(new Vector2(body.Velocity.X, body.Velocity.Y).Length());
            r.Clip.Add(assets.Clip(a.Clip).Name);
            r.Time.Add(a.Time);
            r.Mode.Add(w.TryStore<PersonStep>(out var s) && s.Has(e) ? s.Get(e).Mode : StepMode.None);
            r.X.Add(body.Feet.X);
        }
        return r;
    }

    [Fact]
    public void The_clips_are_found_by_gait_curve_and_foot()
    {
        var (assets, kind, _) = Player();
        var starts = Starts(assets, kind);
        string N(int c) => assets.Clip(c).Name;
        Assert.Equal("walk_start_l", N(starts.Start(false, false, true)));
        Assert.Equal("run_start_r", N(starts.Start(true, false, false)));
        Assert.Equal("walk_start_natural_r", N(starts.Start(false, true, false)));
        Assert.Equal("run_stop_natural_l", N(starts.Stop(true, true, true)));
        Assert.Equal("walk_stop_l", N(starts.Stop(false, true, true)));   // the walking stops serve everyone
        // Each foot's stop enters at a share of the stride its notes give (0.5 and 0; on an older set 0.27 and 0.77), the two apart.
        var stopL = ((INotedClip)assets.Clip(starts.Stop(false, false, true))).Notes.StartStop!;
        var stopR = ((INotedClip)assets.Clip(starts.Stop(false, false, false))).Notes.StartStop!;
        Assert.True(stopL.Stop && stopR.Stop);
        Assert.InRange(stopL.Phase, 0f, 1f);
        Assert.InRange(stopR.Phase, 0f, 1f);
        Assert.NotEqual(stopL.Phase, stopR.Phase);
        Assert.Equal(0.2f, stopL.HalfLife, 3);
        // The stand to hand to; the starts begin in its pose (every round); the stops end in it only where their files say so.
        Assert.True(starts.Stand >= 0);
        Assert.True(starts.BeginsInStand(starts.Start(false, false, true)));
        output.WriteLine($"enter phases: walk_stop_l {F(stopL.Phase)}, walk_stop_r {F(stopR.Phase)}; the stops ending in the stand's pose: "
                         + string.Join(", ", starts.ToStand.Select(c => assets.Clip(c).Name)));
        // The settings through a tape's note; none is the earlier build's.
        var tuned = new PersonStartSettings { Crowd = false, WaitForPhase = false };
        Assert.Equal(tuned.Note(), new PersonStartSettings().WithNote(tuned.Note()).Note());
        Assert.Equal(PersonStartSettings.Off, new PersonStartSettings().WithNote(null));
        Assert.False(PersonStartSettings.Off.Any);
    }

    /// <summary>A clip's start-or-stop note by its name.</summary>
    static ClipStartStop Notes(CharacterAssets assets, string name)
    {
        for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i) is INotedClip { Notes.StartStop: { } ss } c && c.Name == name) return ss;
        throw new InvalidOperationException(name);
    }

    static float Duration(CharacterAssets assets, string name)
    {
        for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i).Name == name) return assets.Clip(i).Duration;
        throw new InvalidOperationException(name);
    }

    /// <summary>
    /// The player stopping from <paramref name="gait"/>: the capsule held at its speed until the stop clip begins, within half a stride; the
    /// stop the one whose enter_phase the loop reached (by its notes); then the spring's stop, its distance and time the clip's (the clip's distance by the
    /// body's pace over the clip's), ending in the stand. Every expectation from the clips' notes, so any round with them passes.
    /// </summary>
    void PlayerStops(Gait gait, float pace)
    {
        var (assets, kind, _) = Player();
        Starts(assets, kind);
        var person = assets.Kind(kind).Person!;
        string g = gait == Gait.Run ? "run" : "walk";
        var (w, e) = Make(assets, kind, new PersonStartSettings(), player: true);
        var (w14, e14) = Make(assets, kind, null, player: true);
        Assert.False(w14.TryStore<PersonStep>(out _));   // no store
        float startD = Duration(assets, g + "_start_l");
        float go = startD + 2f;
        var now = Drive(w, e, assets, gait, go, 3f);
        var then = Drive(w14, e14, assets, gait, go, 3f);
        int goTicks = (int)(go / Tick);
        // Starting: the start clip at once, and the capsule on the spring as before, tick for tick.
        Assert.StartsWith(g + "_start_", now.Clip[0]);
        for (int i = 0; i < goTicks; i++) Assert.Equal(then.Speed[i], now.Speed[i]);
        var startNotes = Notes(assets, now.Clip[0]);
        int loop = now.Clip.FindIndex(c => !c.StartsWith(g + "_start_"));
        float loopD = assets.Clip((gait == Gait.Run ? person.Run : person.Walk)!.First).Duration;
        Assert.InRange(loop * Tick, startD - 0.06f, startD + 0.02f);   // the start played whole (entered where its speed was the capsule's)
        Assert.Contains($":{g}@", now.Clip[loop]);
        // The loop took over at the start's exit_phase.
        float exit = now.Time[loop] / loopD - startNotes.Phase;
        exit -= MathF.Round(exit);
        Assert.InRange(exit, -0.03f, 0.05f);
        int stop = now.Clip.FindIndex(goTicks, c => c.StartsWith(g + "_stop_"));
        Assert.True(stop >= goTicks, "a stop clip played");
        float waited = (stop - goTicks) * Tick;
        // At most the longer way round the stride between the two feet's enter phases (half of it when they are half a stride apart).
        float pl = Notes(assets, g + "_stop_l").Phase, pr = Notes(assets, g + "_stop_r").Phase, gap = pl - pr;
        gap -= MathF.Floor(gap);
        Assert.InRange(waited, 0f, MathF.Max(gap, 1f - gap) * loopD + 0.02f);
        for (int i = goTicks; i < stop; i++) Assert.InRange(now.Speed[i], now.Speed[goTicks - 1] - 0.03f * pace, now.Speed[goTicks - 1] + 0.01f);   // (the release's own tick on the spring)
        var stopNotes = Notes(assets, now.Clip[stop]);
        float atStop = now.Time[stop - 1] / loopD, off = atStop - stopNotes.Phase;
        off -= MathF.Round(off);
        Assert.True(MathF.Abs(off) < 0.05f, $"the stop {now.Clip[stop]} (enter_phase {F(stopNotes.Phase)}) at {F(atStop)} of the stride");
        int stood = now.Clip.FindIndex(stop, c => !c.StartsWith(g + "_stop_"));
        Assert.True(stood > stop, "it ends");
        Assert.Equal("stand", now.Clip[stood]);
        float distance = now.X[^1] - now.X[stop - 1], seconds = (stood - stop) * Tick;
        float want = stopNotes.Distance * pace / stopNotes.Speeds[0], stopD = Duration(assets, now.Clip[stop]);
        output.WriteLine($"{g} ({F(pace, "0.0")} m/s): started on {now.Clip[0]} ({F(startNotes.Distance)} m, {F(startD)} s), the loop from {F(loop * Tick)} s at {F(now.Time[loop] / loopD)} of its stride "
                         + $"(exit_phase {F(startNotes.Phase)}); the stop waited {F(waited)} s for {now.Clip[stop]} at {F(atStop)} of the stride, then went {F(distance)} m "
                         + $"(the clip's {F(stopNotes.Distance)} m at {F(stopNotes.Speeds[0])}: {F(want)} at {F(pace, "0.0")}) and stood in {F(seconds)} s (the clip's {F(stopD)} s); "
                         + $"from the release {F(now.X[^1] - now.X[goTicks - 1])} m (no clips {F(then.X[^1] - then.X[goTicks - 1])} m)");
        Assert.InRange(distance, 0.85f * want, 1.15f * want);
        Assert.InRange(seconds, stopD - 0.03f, stopD + 0.03f);
    }

    [Fact]
    public void The_player_starts_and_stops_walking_on_the_spring_clips_the_stop_waiting_for_its_phase() => PlayerStops(Gait.Walk, 1.3f);

    [Fact]
    public void The_player_runs_and_stops_on_the_spring_clips() => PlayerStops(Gait.Run, 3.0f);

    [Fact]
    public void Anyone_else_starts_and_stops_on_the_natural_clips_its_capsule_moved_by_the_clip()
    {
        var (assets, kind, _) = Player();
        Starts(assets, kind);
        var (w, e) = Make(assets, kind, new PersonStartSettings(), player: false);
        float startD = Duration(assets, "walk_start_natural_l");
        var walk = Drive(w, e, assets, Gait.Walk, startD + 3f, 4f);
        Assert.StartsWith("walk_start_natural_", walk.Clip[0]);
        var notes = Notes(assets, walk.Clip[0]);
        // Its speed is the clip's (by the body's walk over the clip's: 1.3 / its last speed), a tick behind; its prep too (the prep_s).
        float scale = 1.3f / notes.Speeds[^1], worst = 0f;
        for (int i = 2; i < (int)((startD - 0.1f) / Tick); i++) worst = MathF.Max(worst, MathF.Abs(walk.Speed[i] - notes.SpeedAt((i + 0.5f) * Tick) * scale));
        int loop = walk.Clip.FindIndex(c => !c.StartsWith("walk_start_natural_"));
        float started = walk.X[loop - 1] - walk.X[0];
        string prep = notes.Prep > 0f ? $", {F(notes.Prep)} s of it easing over the standing foot" : "";
        output.WriteLine($"walking: {walk.Clip[0]}{prep}, its speed within {F(worst * 1000f, "0.0")} mm/s of the clip's (x{F(scale)}), {F(started)} m in its {F(loop * Tick)} s "
                         + $"(the clip's {F(notes.Distance)} m x{F(scale)}, {F(startD)} s)");
        Assert.InRange(worst, 0f, 0.03f);
        Assert.InRange(started, 0.93f * notes.Distance * scale, 1.07f * notes.Distance * scale);
        Assert.InRange(loop * Tick, startD - 0.03f, startD + 0.02f);
        // Running, its stop: the natural one.
        var (wr, er) = Make(assets, kind, new PersonStartSettings(), player: false);
        var run = Drive(wr, er, assets, Gait.Run, 4f, 5f);
        int go = (int)(4f / Tick);
        int stop = run.Clip.FindIndex(go, c => c.StartsWith("run_stop_natural_"));
        Assert.True(stop >= go, "the natural running stop played");
        int stood = run.Clip.FindIndex(stop, c => !c.StartsWith("run_stop_natural_"));
        float distance = run.X[stood] - run.X[stop - 1];
        var stopNotes = Notes(assets, run.Clip[stop]);
        float stopD = Duration(assets, run.Clip[stop]), want = stopNotes.Distance * 3.0f / stopNotes.Speeds[0];
        output.WriteLine($"running: started on {run.Clip[0]}; the stop waited {F((stop - go) * Tick)} s for {run.Clip[stop]}, went {F(distance)} m in {F((stood - stop) * Tick)} s "
                         + $"(the clip's {F(stopNotes.Distance)} m, {F(stopD)} s)");
        Assert.InRange(distance, 0.93f * want, 1.07f * want);
        Assert.InRange((stood - stop) * Tick, stopD - 0.03f, stopD + 0.03f);
    }

    [Fact]
    public void Asked_again_mid_stop_it_goes_on_and_sideways_it_starts_as_before()
    {
        var (assets, kind, _) = Player();
        Starts(assets, kind);
        var (w, e) = Make(assets, kind, new PersonStartSettings(), player: true);
        for (int i = 0; i < 30; i++) { Ask(w, e, 0f, Gait.Walk); w.Step(); }
        // Sideways from the stand: the gait's blend as before (the starts are made ahead).
        for (int i = 0; i < 40; i++)
        {
            ref var intent = ref w.Get<MoveIntent>(e);
            (intent.Direction, intent.Gait, intent.Faces, intent.Facing) = (new Vector2(0f, 1f), Gait.Walk, true, 0f);
            w.Step();
        }
        Assert.DoesNotContain("start", assets.Clip(w.Get<CharacterAnimation>(e).Clip).Name);
        // Ahead past its start, stopped, asked again mid-stop: going on again (a walk, or a start from where it stood), then the loop.
        float startD = Duration(assets, "walk_start_l");
        var (w2, e2) = Make(assets, kind, new PersonStartSettings(), player: true);
        var r = Drive(w2, e2, assets, Gait.Walk, startD + 1f, 0.9f);
        int stop = r.Clip.FindIndex(c => c.StartsWith("walk_stop_"));
        Assert.True(stop > 0);
        Assert.StartsWith("walk_stop_", r.Clip[^1]);
        for (int i = 0; i < 20; i++) { Ask(w2, e2, 1f, Gait.Walk); w2.Step(); }
        Assert.Equal(MotionRole.Walk, w2.Get<CharacterAnimation>(e2).Role);
        for (int i = 0; i < (int)((startD + 0.5f) / Tick); i++) { Ask(w2, e2, 1f, Gait.Walk); w2.Step(); }
        Assert.Contains(":walk@", assets.Clip(w2.Get<CharacterAnimation>(e2).Clip).Name);
        Assert.True(Finite(w2, e2));
    }
}
