// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The player's bow (aim mode as designed, the arrow where the player aims): swapWeapon swaps the sword and
/// the bow over the bow's swap time; aim held (or latched by its toggle) raises the bow, attack held while aiming draws, released looses;
/// attack without aim does nothing with the bow; the arrow's launch is solved to meet the crosshair's point at 10, 20 and 40 m, first and
/// third person, and with the solve off it lands below by the drop; block, cancel and a dodge let a draw down (cancel's press does nothing
/// else); aim let go lets it down and lowers the bow; the earlier build's rules under their switch; a recorded shot replays to the same digests. No game
/// needed: bodies without characters, a floor and a wall (the shield raised against a drawn bow needs a held shield: the Host's
/// <c>BowShieldScenarioTests</c>).
/// </summary>
public sealed class PlayerBowTests(ITestOutputHelper output)
{
    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>A yard: a floor and a wall across +X at <paramref name="wall"/> m (its face there, 4 m high), the player at the origin facing +X with a bow put away.</summary>
    static (World World, EntityId Player) Yard(ulong seed = 5, float spread = 0f, Action<WorldBuilder>? more = null, float wall = 20f)
    {
        var level = new TestLevel().Floor(-40, -40, 80, 40).Box(new(wall, -10f, 0f), new(wall + 0.4f, 10f, 4f)).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        b.Component<CombatIntent>();
        var config = new BowConfig();
        config.Bows.Add(BowTests.WarBow());
        if (spread > 0f) config.SpreadDegrees = (_, _) => spread;
        BowServices.Register(b, config);
        more?.Invoke(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Set(e, new CombatIntent());
        Assert.True(BowServices.Give(w, e, "war_bow", 24));
        for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, e);
    }

    static ActionFrame Hold(Buttons b) => new(Vector2.Zero, Vector2.Zero, b, new Vector2(0.5f, 0.5f));

    static void Run(World w, int ticks, ActionFrame frame, List<ArrowLoosed>? loosed = null, List<ArrowSpent>? spent = null, List<DrawCancelled>? cancelled = null, List<WeaponSwap>? swaps = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, frame);
            loosed?.AddRange(w.Events<ArrowLoosed>().ThisTick);
            spent?.AddRange(w.Events<ArrowSpent>().ThisTick);
            cancelled?.AddRange(w.Events<DrawCancelled>().ThisTick);
            swaps?.AddRange(w.Events<WeaponSwap>().ThisTick);
        }
    }

    [Fact]
    public void Swap_weapon_takes_the_bow_after_its_swap_time_and_puts_it_away_again()
    {
        var (w, e) = Yard();
        var swaps = new List<WeaponSwap>();
        Run(w, 1, Hold(Buttons.SwapWeapon), swaps: swaps);
        Run(w, Ticks(0.55f), ActionFrame.Idle, swaps: swaps);
        Assert.False(w.Get<Bow>(e).InHand);                                         // not yet: 0.6 s
        Run(w, Ticks(0.1f), ActionFrame.Idle, swaps: swaps);
        Assert.True(w.Get<Bow>(e).InHand);
        Assert.Equal([(true, false), (true, true)], swaps.Select(s => (s.ToRanged, s.Ended)));
        Assert.InRange((int)(swaps[1].Tick - swaps[0].Tick), Ticks(0.6f) - 1, Ticks(0.6f) + 1);
        // A press held through the swap does not swap back; a second press puts it away.
        Run(w, 1, Hold(Buttons.SwapWeapon), swaps: swaps);
        Run(w, Ticks(0.7f), ActionFrame.Idle, swaps: swaps);
        Assert.False(w.Get<Bow>(e).InHand);
        Assert.Equal(4, swaps.Count);
    }

    /// <summary>The bow in hand and an arrow nocked by itself while it is low.</summary>
    static (World World, EntityId Player) Ready(bool first = false, float spread = 0f, ulong seed = 5, Action<WorldBuilder>? more = null, float wall = 20f)
    {
        var (w, e) = Yard(seed, spread, more, wall);
        if (first)                                                   // the views' cycle (V): over the right shoulder, the left, first person
            for (int k = 0; k < 2; k++) { Run(w, 1, Hold(Buttons.SwitchView)); Run(w, 1, ActionFrame.Idle); }
        Run(w, 1, Hold(Buttons.SwapWeapon));
        Run(w, Ticks(1.4f), ActionFrame.Idle);                      // 0.6 s swap, 0.6 s nock while low
        Assert.True(w.Get<Bow>(e).InHand);
        Assert.True(w.Get<Bow>(e).OnString);
        return (w, e);
    }

    [Theory]
    [InlineData(false, 10f)]
    [InlineData(false, 20f)]
    [InlineData(false, 40f)]
    [InlineData(true, 10f)]
    [InlineData(true, 20f)]
    [InlineData(true, 40f)]
    public void With_the_solve_on_the_arrow_meets_the_crosshair_s_point(bool first, float wall)
    {
        var (w, e) = Ready(first, wall: wall, more: b => b.Service(new PlayerBowSettings { Solve = true }));   // the solve (off by default: )
        Assert.Equal(first ? ViewKind.First : ViewKind.Third, w.Get<CameraRig>(e).View);
        var loosed = new List<ArrowLoosed>();
        var spent = new List<ArrowSpent>();
        Run(w, Ticks(0.4f), Hold(Buttons.Aim), loosed, spent);                       // aim mode: the bow up, an arrow nocked
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        Run(w, Ticks(1.2f), Hold(Buttons.Aim | Buttons.Attack), loosed, spent);
        Assert.Empty(loosed);
        Assert.Equal(BowState.Held, w.Get<Bow>(e).State);
        Assert.Equal(1f, w.Get<Bow>(e).Share, 3);
        Assert.True(PlayerBow.Crosshair(w, e, out var point));                         // where the crosshair is now, on the wall
        Run(w, 1, Hold(Buttons.Aim), loosed, spent);                                   // attack let go: loosed
        var l = Assert.Single(loosed);
        Assert.Equal(55f, l.SpeedMetresPerSecond, 2);
        Assert.Equal(0f, l.SpreadDegrees);                                             // no random spread: the arrow goes where he aims
        Run(w, Ticks(1.2f), Hold(Buttons.Aim), loosed, spent);
        var s = Assert.Single(spent);
        float miss = Vector3.Distance(point, s.Point);
        output.WriteLine($"{(first ? "first" : "third")} person, the wall at {wall} m: the crosshair at {point}, the arrow met the {s.Met} at {s.Point}: {miss * 100f:0.00} cm off; loosed {l.PitchDegrees:0.000}° up from {l.From}");
        Assert.Equal(ContactMet.Level, s.Met);
        Assert.Equal(wall, point.X, 1);
        Assert.True(miss < 0.03f, $"the arrow met {miss * 100f:0.0} cm from the crosshair's point");
    }

    [Theory]
    [InlineData(15f)]
    [InlineData(30f)]
    public void By_default_the_arrow_leaves_along_the_line_with_no_spread_and_lands_below_by_its_drop(float wall)
    {
        var (w, e) = Ready(wall: wall, spread: 2f);                                   // the module's spread is not the player's
        var loosed = new List<ArrowLoosed>();
        var spent = new List<ArrowSpent>();
        Run(w, Ticks(1.6f), Hold(Buttons.Aim | Buttons.Attack), loosed, spent);
        Assert.True(PlayerBow.Crosshair(w, e, out var point));
        Run(w, 1, Hold(Buttons.Aim), loosed, spent);
        var l = Assert.Single(loosed);
        Assert.Equal(0f, l.SpreadDegrees);                                            // no spread for the player
        Assert.True(Vector3.Dot(Vector3.Normalize(point - l.From), Vector3.Normalize(l.Velocity)) > MathF.Cos(0.05f * MathF.PI / 180f));   // along the line
        Run(w, Ticks(1.2f), Hold(Buttons.Aim), loosed, spent);
        var s = Assert.Single(spent);
        // The drop the same flight has along that line, flown by the ballistics alone.
        var d = w.Service<BowConfig>().Bows[0];
        var p = l.From;
        var v = l.Velocity;
        while (p.X < wall) { var p0 = p; Ballistics.Step(ref p, ref v, d.ArrowDragPerMetre, World.TickSeconds); if (p.X >= wall) p = p0 + (p - p0) * ((wall - p0.X) / (p.X - p0.X)); }
        output.WriteLine($"the solve off, the wall at {wall} m: the crosshair at {point}, the arrow met at {s.Point}: {(point.Z - s.Point.Z) * 100f:0.0} cm below; the ballistic flight {(point.Z - p.Z) * 100f:0.0} cm");
        Assert.Equal(point.Y, s.Point.Y, 2);
        Assert.InRange(point.Z - s.Point.Z, 0.2f, 3f);
        Assert.Equal(p.Z, s.Point.Z, 2);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.5f)]
    public void The_aid_s_point_of_impact_is_where_the_arrow_lands_and_its_path_is_the_arrow_s(float draw)
    {
        var (w, e) = Ready(wall: 25f, more: b => b.Service(new PlayerBowSettings { Solve = false }));   // along the line: it drops into the wall's foot or face
        var loosed = new List<ArrowLoosed>();
        var spent = new List<ArrowSpent>();
        Run(w, Ticks(0.4f), Hold(Buttons.Aim));
        Run(w, (int)MathF.Round(draw * World.TicksPerSecond) + 1, Hold(Buttons.Aim | Buttons.Attack), loosed, spent);
        var forecast = new BowForecast();
        Assert.True(forecast.Fly(w, e));
        Assert.True(forecast.Meets);
        var impact = forecast.Impact;
        var path = forecast.Path.ToArray();
        Run(w, 1, Hold(Buttons.Aim), loosed, spent);                            // loosed now, at the draw reached
        Run(w, Ticks(1.5f), Hold(Buttons.Aim), loosed, spent);
        var s = Assert.Single(spent);
        output.WriteLine($"draw {draw}: the aid's impact {impact}, the arrow met the {s.Met} at {s.Point}: {Vector3.Distance(impact, s.Point) * 1000f:0.0} mm; the path {path.Length} points");
        Assert.True(Vector3.Distance(impact, s.Point) < 0.01f);
        Assert.True(path.Length > 10);
        Assert.True(Vector3.Distance(Assert.Single(loosed).From, path[0]) < 1e-4f);
    }

    [Fact]
    public void Attack_without_aim_does_nothing_with_the_bow()
    {
        var (w, e) = Ready();
        var loosed = new List<ArrowLoosed>();
        Run(w, Ticks(1.5f), Hold(Buttons.Attack), loosed);
        Assert.Equal(BowState.Ready, w.Get<Bow>(e).State);                          // low at the waist, undrawn
        Run(w, 2, ActionFrame.Idle, loosed);
        Assert.Empty(loosed);
    }

    [Fact]
    public void Aim_s_toggle_latches_aim_mode_and_a_second_press_lets_it_go()
    {
        var (w, e) = Ready();
        Run(w, 1, Hold(Buttons.AimToggle));
        Run(w, Ticks(0.5f), ActionFrame.Idle);
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);                         // latched: up without the key held
        var loosed = new List<ArrowLoosed>();
        Run(w, Ticks(1.2f), Hold(Buttons.Attack), loosed);
        Run(w, 1, ActionFrame.Idle, loosed);
        Assert.Single(loosed);
        Run(w, 1, Hold(Buttons.AimToggle));
        Run(w, Ticks(0.2f), ActionFrame.Idle);
        Assert.Equal(BowState.Ready, w.Get<Bow>(e).State);                          // let go: low again
    }

    [Fact]
    public void Block_cancel_or_a_dodge_lets_a_draw_down_and_cancel_s_press_does_nothing_else()
    {
        var (w, e) = Ready();
        var cancelled = new List<DrawCancelled>();
        var loosed = new List<ArrowLoosed>();
        const Buttons A = Buttons.Aim;
        Run(w, Ticks(0.8f), Hold(A | Buttons.Attack), loosed, cancelled: cancelled);
        Run(w, 1, Hold(A | Buttons.Attack | Buttons.Block), loosed, cancelled: cancelled);
        Assert.Equal(DrawCancelCause.LetDown, Assert.Single(cancelled).Cause);
        Run(w, Ticks(0.5f), Hold(A | Buttons.Attack), loosed, cancelled: cancelled);
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);                       // held up, undrawn, until attack is let go
        Run(w, 2, Hold(A), loosed, cancelled: cancelled);
        Assert.Empty(loosed);                                                       // letting go after a let-down looses nothing
        // Cancel (crouch's key) while drawn: let down, and no crouch.
        Run(w, Ticks(0.9f), Hold(A | Buttons.Attack), loosed, cancelled: cancelled);
        Run(w, 3, Hold(A | Buttons.Attack | Buttons.Cancel | Buttons.Crouch), loosed, cancelled: cancelled);
        Assert.Equal(2, cancelled.Count);
        Assert.False(w.Get<MoveIntent>(e).Crouch);
        Run(w, 2, Hold(A), loosed, cancelled: cancelled);
        Assert.Empty(loosed);
        // A dodge pressed while drawn: let down too (the dodge goes on as ever).
        Run(w, Ticks(0.9f), Hold(A | Buttons.Attack), loosed, cancelled: cancelled);
        Run(w, 1, Hold(A | Buttons.Attack | Buttons.Dodge), loosed, cancelled: cancelled);
        Assert.Equal(3, cancelled.Count);
        Run(w, 2, Hold(A), loosed, cancelled: cancelled);
        Assert.Empty(loosed);
    }

    [Fact]
    public void Aim_let_go_while_drawn_lets_the_draw_down_and_lowers_the_bow()
    {
        var (w, e) = Ready();
        var cancelled = new List<DrawCancelled>();
        var loosed = new List<ArrowLoosed>();
        Run(w, Ticks(0.3f), Hold(Buttons.Aim), loosed, cancelled: cancelled);
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);                       // aim mode: up, nocked, undrawn
        Run(w, Ticks(0.5f), Hold(Buttons.Aim | Buttons.Attack), loosed, cancelled: cancelled);
        Assert.Equal(BowState.Drawing, w.Get<Bow>(e).State);
        Run(w, Ticks(0.5f), Hold(Buttons.Attack), loosed, cancelled: cancelled);            // aim let go, attack still held
        Assert.Equal(DrawCancelCause.AimLetGo, Assert.Single(cancelled).Cause);
        Assert.Equal(BowState.Ready, w.Get<Bow>(e).State);                        // low, the arrow still nocked
        Assert.True(w.Get<Bow>(e).OnString);
        Run(w, Ticks(0.5f), Hold(Buttons.Aim | Buttons.Attack), loosed, cancelled: cancelled);   // aim again, attack still held from before: no draw
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        Run(w, 2, Hold(Buttons.Aim), loosed, cancelled: cancelled);
        Assert.Empty(loosed);
    }

    [Fact]
    public void The_rules_without_aim_under_their_switch_draw_on_attack_alone()
    {
        var (w, e) = Ready(more: b => b.Service(new PlayerBowSettings { NoAim = true }));
        var loosed = new List<ArrowLoosed>();
        Run(w, Ticks(1.4f), Hold(Buttons.Attack), loosed);
        Assert.Equal(BowState.Held, w.Get<Bow>(e).State);
        Run(w, 1, ActionFrame.Idle, loosed);
        Assert.Single(loosed);
    }

    [Fact]
    public void The_flat_reach_of_a_full_draw_and_the_solve_beyond_it()
    {
        var d = BowTests.WarBow();
        float reach = Ballistics.FlatReach(d.SpeedAt(1f), d.ArrowDragPerMetre, out float best);
        float half = Ballistics.FlatReach(d.SpeedAt(0.5f), d.ArrowDragPerMetre, out float bestHalf);
        output.WriteLine($"a full draw ({d.SpeedAt(1f)} m/s, drag {d.ArrowDragPerMetre}/m) reaches {reach:0.0} m on the flat at {best * 180f / MathF.PI:0.0}° up; a half draw {half:0.0} m at {bestHalf * 180f / MathF.PI:0.0}°");
        Assert.InRange(reach, 100f, 310f);
        Assert.True(best < MathF.PI / 4f);                                          // drag: the best angle is below 45°
        // Beyond the reach the solve gives the farthest angle and says it falls short.
        float pitch = Ballistics.SolveAim(Vector3.Zero, new Vector3(reach + 30f, 0f, 0f), d.SpeedAt(1f), d.ArrowDragPerMetre, out bool reaches);
        Assert.False(reaches);
        Assert.InRange(pitch, 30f * MathF.PI / 180f, MathF.PI / 4f);              // the highest it can be at that distance: it falls short
        float near = Ballistics.SolveAim(Vector3.Zero, new Vector3(40f, 0f, 0f), d.SpeedAt(1f), d.ArrowDragPerMetre, out bool reachesNear);
        Assert.True(reachesNear);
        Assert.InRange(near, 0f, 0.1f);
    }

    [Fact]
    public void A_recorded_shot_replays_to_the_same_digests()
    {
        ulong[] Play()
        {
            var (w, e) = Ready(spread: 1.5f, seed: 9);
            var digests = new List<ulong>();
            var script = new List<ActionFrame>();
            for (int i = 0; i < Ticks(1.5f); i++) script.Add(Hold(Buttons.Aim | (i >= 30 ? Buttons.Attack : Buttons.None)) with { Look = new Vector2(i < 30 ? 0.004f : 0f, 0f) });
            for (int i = 0; i < Ticks(0.8f); i++) script.Add(Hold(Buttons.Aim));
            foreach (var f in script) { MovementServices.Step(w, f); digests.Add(w.Digest()); }
            Assert.Equal(1, w.Get<Bow>(e).Shots);
            return [.. digests];
        }
        Assert.Equal(Play(), Play());
    }
}
