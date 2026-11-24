// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.AirCommitTests;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// ("If the player keeps holding the controls to keep pulling up and keep moving, there shouldn't be much or any of a delay
/// once they hit the hang"): a grab whose player holds on flows into the pull-up as soon as the hands are planted, entered past its still start at
/// <c>pullUpRate</c> (1.3), the hands kept on the real lip; with nothing held the hang as before, a press there pulling up at once at its own rate; the
/// standing grab the same; the earlier build's behind the switch. Skips without the models repository's files, or.
/// </summary>
public sealed class PullUpFlowTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    static readonly PersonCommitSettings Straight = new();
    static readonly PersonCommitSettings WaitOut = new PersonCommitSettings().WaitOutClimb;

    static LedgePaths Paths(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge is not null && person.Over is not null && person.Air is { Prelandings.Length: > 0 },
                          "the models repository's files are too old (no ledge clips or prelandings)");
        return person.Ledge!.Paths;
    }

    /// <summary>A run at the wall of <paramref name="h"/>, jump pressed 1.35 m short, forward held throughout (on the ground, in the air, on the wall).</summary>
    static List<Row> RunHeld(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, int ticks = 700)
    {
        var (w, e) = Make(assets, kind, commit, new Vector3(-7f, 0f, 0f), blocks: [Wall(h, -12f, 12f)]);
        bool pressed = false;
        return Drive(w, e, assets, kind, (i, b) =>
        {
            bool now = !pressed && b.State == BodyState.Ground && 1f - b.Feet.X <= 1.35f;
            pressed |= now;
            return new Ask(new Vector2(1f, 0f), Gait.Run, Jump: now);
        }, ticks);
    }

    /// <summary>
    /// A fall past the wall of <paramref name="h"/> (put in the air 0.45 m out, 1 m over the falling grab's contact place), use held; forward held
    /// throughout (<paramref name="forward"/>), or from tick <paramref name="pressAt"/> on (a fresh press in the hang).
    /// </summary>
    static List<Row> FallUse(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, bool forward, int pressAt = int.MaxValue, int ticks = 700)
    {
        var c = Paths(assets, kind).Of(LedgeRole.GrabFall)!;
        float contactZ = h - (c.EdgeUp - c.At(c.ContactS).Up);
        var (w, e) = Make(assets, kind, commit, new Vector3(0.55f, 0f, 0f), blocks: [Wall(h)]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(forward || i >= pressAt ? new Vector2(1f, 0f) : Vector2.Zero, Use: true), ticks,
                     launch: (new Vector3(0.55f, 0f, contactZ + 1f), Vector3.Zero, false));
    }

    /// <summary>A standing tap at the wall of <paramref name="h"/> from 0.45 m out (the standing grab, from the ground), forward held from the tap.</summary>
    static List<Row> StandHeld(CharacterAssets assets, int kind, PersonCommitSettings commit, float h, int ticks = 600)
    {
        var (w, e) = Make(assets, kind, commit, new Vector3(0.55f, 0f, 0f), blocks: [Wall(h)]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Jump: i == 0), ticks);
    }

    /// <summary>The hands' contact (the grab clip at its <c>contact_s</c>), the pull-up's start, standing on top after it (ticks; −1 for none).</summary>
    static (int Contact, int PullUp, int Stand) Times(List<Row> rows, LedgePaths paths)
    {
        int contact = rows.FindIndex(r => paths.Clips.FirstOrDefault(c => c.Name == r.Ledge) is { } g
                                          && g.Role is LedgeRole.GrabRun or LedgeRole.GrabFall or LedgeRole.GrabStand && r.LedgeTime >= g.ContactS - 1e-4f);
        int pull = rows.FindIndex(r => r.Move == TraversalKind.PullUp);
        int stand = pull < 0 ? -1 : rows.FindIndex(pull, r => r.Move == TraversalKind.None && r.State == BodyState.Ground);
        return (contact, pull, stand);
    }

    /// <summary>
    /// How far the knuckles (both hands' mean) wander (the largest distance from where they were) over the pull-up from its clip's time <paramref name="t0"/> to
    /// 0.39 s of it (the earlier build's first 0.3 s at 1.3×: the capsule's difference taken out), the clip's own hands moving as they do there.
    /// </summary>
    static float Wander(List<Row> rows, int pull, float t0)
    {
        static Vector3 K(Row r) => 0.5f * (r.KnuckleL + r.KnuckleR);
        int from = rows.FindIndex(pull, r => r.LedgeTime >= t0 - 1e-4f);
        var at = K(rows[from]);
        float most = 0f;
        for (int i = from; i < rows.Count && rows[i].Move == TraversalKind.PullUp && rows[i].LedgeTime <= t0 + 0.39f; i++) most = MathF.Max(most, (K(rows[i]) - at).Length());
        return most;
    }

    /// <summary>The case's numbers (said): the contact, the pull-up's start, standing; the pops from the contact to 0.25 s after standing; the knuckles' wander.</summary>
    (int Contact, int PullUp, int Stand, float Pelvis, float Hands, float Wander) Measure(string name, List<Row> rows, LedgePaths paths, string build)
    {
        var (contact, pull, stand) = Times(rows, paths);
        Assert.True(contact > 0 && pull > contact && stand > pull, $"{build} {name}: contact {contact}, pull-up {pull}, stand {stand}");
        float pelvis = Pop(rows, contact, stand + 30, PelvisOf), hands = Pop(rows, contact, stand + 30, HandsOf);
        float wander = Wander(rows, pull, BodyMotor.PullUpEntry(paths.Clips.First(c => c.Name == rows[pull].Ledge)));
        output.WriteLine($"{name}, {build}: the pull-up {F((pull - contact) * Tick, "0.000")} s after the hands' contact on {rows[pull].Ledge}, standing "
                         + $"{F((stand - contact) * Tick)} s after it (the pull-up {F((stand - pull) * Tick)} s); from the contact to 0.25 s after standing the pelvis's pop "
                         + $"{F(pelvis * 1000f, "0.0")} mm (at {ArgPop(rows, contact, stand + 30, PelvisOf) - contact}), the hands' {F(hands * 1000f, "0.0")} mm "
                         + $"(at {ArgPop(rows, contact, stand + 30, HandsOf) - contact}); the knuckles wander {F(wander * 1000f, "0")} mm over its clip's 0.39 s from where straight on enters it");
        return (contact, pull, stand, pelvis, hands, wander);
    }

    [Fact]
    public void A_running_catch_with_forward_held_goes_straight_into_the_pull_up()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var da = Measure("a running catch at 2.2 m, forward held", RunHeld(assets, kind, Straight, 2.2f), paths, "straight on");
        var cz = Measure("a running catch at 2.2 m, forward held", RunHeld(assets, kind, WaitOut, 2.2f), paths, "waited out");
        Assert.InRange((da.PullUp - da.Contact) * Tick, 0.1f, 0.2f);
        Assert.True((cz.PullUp - cz.Contact) * Tick > 0.6f);
        Assert.True(da.Stand - da.Contact < cz.Stand - cz.Contact - (int)(0.9f / Tick), "standing 0.9 s sooner at least");
        Assert.True(da.Pelvis < 0.02f && da.Hands < 0.04f, "no pop");
        Assert.True(da.Wander < cz.Wander + 0.02f, "the hands kept on the lip as the clip has them");
    }

    [Fact]
    public void A_falling_catch_with_use_and_forward_held_goes_straight_into_the_pull_up()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var da = Measure("a falling catch at 2.6 m, use and forward held", FallUse(assets, kind, Straight, 2.6f, forward: true), paths, "straight on");
        var cz = Measure("a falling catch at 2.6 m, use and forward held", FallUse(assets, kind, WaitOut, 2.6f, forward: true), paths, "waited out");
        Assert.InRange((da.PullUp - da.Contact) * Tick, 0.1f, 0.2f);
        Assert.True((cz.PullUp - cz.Contact) * Tick > 0.6f);
        Assert.True(da.Pelvis < 0.02f && da.Hands < 0.04f, "no pop");
        Assert.True(da.Wander < cz.Wander + 0.02f, "the hands kept on the lip as the clip has them");
    }

    [Fact]
    public void With_nothing_held_it_hangs_and_a_press_there_pulls_up_at_once_at_its_own_rate()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        var still = FallUse(assets, kind, Straight, 2.6f, forward: false, ticks: 500);
        Assert.DoesNotContain(still, r => r.Move == TraversalKind.PullUp);
        Assert.Equal(TraversalKind.Hang, still[^1].Move);
        output.WriteLine($"nothing held: hanging to the end ({F(still.Count * Tick)} s), on {still[^1].Ledge}");
        // A fresh forward in the hang loop (over 3 s on) pulls up on that tick, at its own rate.
        foreach (var (commit, build) in new[] { (Straight, "straight on"), (WaitOut, "waited out") })
        {
            const int press = 400;
            var rows = FallUse(assets, kind, commit, 2.6f, forward: false, pressAt: press, ticks: 800);
            var (_, pull, stand) = Times(rows, paths);
            var clip = paths.Clips.First(c => c.Name == rows[pull].Ledge);
            float seconds = (stand - pull) * Tick;
            float pelvis = Pop(rows, press - 2, stand + 30, PelvisOf), hands = Pop(rows, press - 2, stand + 30, HandsOf);
            output.WriteLine($"{build}: forward pressed in the hang, the pull-up {pull - press} ticks later on {clip.Name}, {F(seconds)} s to standing; pops pelvis "
                             + $"{F(pelvis * 1000f, "0.0")}, hands {F(hands * 1000f, "0.0")} mm");
            Assert.InRange(pull - press, 0, 1);
            float own = clip.Duration - (commit.Climb ? BodyMotor.PullUpEntry(clip) : 0f);   // past its still start
            Assert.InRange(seconds, own - 0.05f, own + 0.05f);
            if (commit.Climb) Assert.True(pelvis < 0.02f && hands < 0.04f, "no pop");
        }
    }

    [Fact]
    public void A_tap_with_nothing_held_hangs_and_jump_pressed_during_a_catch_goes_on()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        // The standing tap that begins the grab is not a press to go on: it hangs.
        float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        var (w, e) = Make(assets, kind, Straight, new Vector3(0.55f, 0f, 0f), blocks: [Wall(h)]);
        var tap = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Jump: i == 0), 400);
        Assert.DoesNotContain(tap, r => r.Move == TraversalKind.PullUp);
        Assert.Equal(TraversalKind.Hang, tap[^1].Move);
        // A running catch with nothing held after the jump, jump pressed again in the grab before the hands' contact: on at FlowAfter.
        (w, e) = Make(assets, kind, Straight, new Vector3(-7f, 0f, 0f), blocks: [Wall(2.2f, -12f, 12f)]);
        bool pressed = false;
        int grab = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            bool now = !pressed && b.State == BodyState.Ground && 1f - b.Feet.X <= 1.35f;
            pressed |= now;
            if (grab < 0 && w.Get<Traversal>(e).Kind == TraversalKind.Grab) grab = i;
            return new Ask(b.State == BodyState.Ground ? new Vector2(1f, 0f) : Vector2.Zero, Gait.Run, Jump: now || (grab >= 0 && i == grab + 5));
        }, 700);
        var (contact, pull, _) = Times(rows, paths);
        output.WriteLine($"a standing tap at {F(h)} m, nothing held: hangs; a running catch, jump pressed in the grab: the pull-up {F((pull - contact) * Tick, "0.000")} s after the contact");
        Assert.InRange((pull - contact) * Tick, 0.1f, 0.2f);
    }

    [Fact]
    public void A_standing_grab_with_forward_held_goes_straight_into_the_pull_up()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        var da = Measure($"a standing grab at {F(h)} m, forward held", StandHeld(assets, kind, Straight, h), paths, "straight on");
        var cz = Measure($"a standing grab at {F(h)} m, forward held", StandHeld(assets, kind, WaitOut, h), paths, "waited out");
        Assert.InRange((da.PullUp - da.Contact) * Tick, 0.1f, 0.2f);
        Assert.True(da.Stand < cz.Stand);
        Assert.True(da.Pelvis < 0.02f && da.Hands < 0.04f, "no pop");
    }

    [Fact]
    public void The_pull_up_rate_is_a_setting_and_the_switch_waits_the_grab_out()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float Pull(PersonCommitSettings commit, out LedgeClip clip)
        {
            var rows = RunHeld(assets, kind, commit, 2.2f);
            var (_, pull, stand) = Times(rows, paths);
            clip = paths.Clips.First(c => c.Name == rows[pull].Ledge);
            return (stand - pull) * Tick;
        }
        foreach (float rate in new[] { 1.3f, 1.6f, 1f })
        {
            float s = Pull(Straight with { PullUpRate = rate }, out var clip);
            float own = clip.Duration - BodyMotor.PullUpEntry(clip);
            output.WriteLine($"pullUpRate {F(rate, "0.0")}: the pull-up {F(s)} s ({clip.Name} past its first {F(BodyMotor.PullUpEntry(clip))} s)");
            Assert.InRange(s, own / rate - 0.05f, own / rate + 0.05f);
        }
        float cz = Pull(WaitOut, out var c14);
        output.WriteLine($"waited out: the pull-up {F(cz)} s ({c14.Name}, its own {F(c14.Duration)} s)");
        Assert.InRange(cz, c14.Duration - 0.05f, c14.Duration + 0.05f);
        Assert.Contains(" climb=1 pull=1.3", Straight.Note());
        Assert.DoesNotContain("climb", WaitOut.Note());
        Assert.False(Straight.WithNote(WaitOut.Note()).Climb);
        Assert.Equal(Straight with { PullUpRate = 1.5f }, Straight.WithNote((Straight with { PullUpRate = 1.5f }).Note()));
    }
}
