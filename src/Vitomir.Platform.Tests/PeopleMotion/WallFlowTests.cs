// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.WallClimbTests;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// (after play of the earlier climb: the overall movements should be smoother, and the player should be able to go through them faster;
/// climbing diagonally is needed as well): <b>wall climbing, faster and flowing, with diagonals</b> — the wall's moves at <c>wallClimbRate</c> and the rest at
/// <c>wallMoveRate</c>; a held move out of the getting-on with no rest at the idle, the next out of a move as its last limb lands; over the top straight on into
/// the pull-up; forward or back with a side climbing diagonally; the catch from a sprint and the getting-on from a run-up softened; the earlier build's behind its switch.
/// Skips without the models repository's files, or.
/// </summary>
public sealed class WallFlowTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    /// <summary>The earlier build's defaults.</summary>
    internal static readonly PersonWallSettings Flowing = new();

    static readonly Vector2 Up = new(1f, 0f), Down = new(-1f, 0f), Left = new(0f, 1f), Right = new(0f, -1f);
    static Vector2 Diag(Vector2 a, Vector2 b) => Vector2.Normalize(a + b);

    /// <summary>The ticks where a wall's move began (its act and way, as the rows' <c>Began</c>), in order.</summary>
    internal static List<(int I, string What)> Begun(List<Row> rows) => [.. rows.Where(r => r.Began != "").Select(r => (r.I, r.Began))];

    /// <summary>
    /// As <see cref="WallClimbTests.OnHolds"/>, with a diagonal's end holds a step up or down (its second move's): each limb at each hold it takes in the moves (a
    /// move begun with both a side and up or down is a diagonal until the next move begins).
    /// </summary>
    internal static List<(string Clip, int Limb, Vector3 Off)> OnHoldsDiag(List<Row> rows, LedgePaths paths, bool onlyDiagonals)
    {
        var all = new List<(string, int, Vector3)>();
        int du = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Began.StartsWith("Move ", StringComparison.Ordinal))
            {
                var parts = r.Began[5..].Split(' ')[0].Split(',');
                int ml = int.Parse(parts[0], CultureInfo.InvariantCulture), mu = int.Parse(parts[1], CultureInfo.InvariantCulture);
                du = ml != 0 && mu != 0 ? mu : 0;
            }
            else if (r.Began != "") du = 0;
            if (onlyDiagonals && du == 0) continue;
            if (paths.Clips.FirstOrDefault(x => x.Name == r.Clip) is not { Role: LedgeRole.WallMove } c) continue;
            var p = rows[i - 1];
            bool fresh = p.Clip != r.Clip || r.Began != "";
            foreach (var h in c.Holds)
            {
                if (h.On < 0f || h.On >= c.Duration) continue;
                if (!(r.Time >= h.On - 1e-4f && (fresh ? r.Time - h.On < 2f * Tick * 3f : p.Time < h.On - 1e-4f))) continue;
                var lv = new Vector2(-r.In.Y, r.In.X);
                var hold = new Vector3(r.Place.X + r.In.X * c.WallFace + lv.X * h.Left, r.Place.Y + r.In.Y * c.WallFace + lv.Y * h.Left, r.Place.Z + h.Up + 0.4f * du);
                var at = h.Limb switch { 0 => r.KnuckleL, 1 => r.KnuckleR, 2 => r.BallL, _ => r.BallR };
                var d = at - hold;
                all.Add((c.Name, h.Limb, new Vector3(-Vector2.Dot(new Vector2(d.X, d.Y), r.In), Vector2.Dot(new Vector2(d.X, d.Y), lv), d.Z)));
            }
        }
        return all;
    }

    /// <summary>The pops at every seam (a wall's move begun, or the clip changed): from 3 ticks before to the flow's blend and 6 ticks after; the largest, and each.</summary>
    internal static (float Pelvis, float Hands, List<string> Each) Seams(List<Row> rows, float blend = 0.15f)
    {
        float p = 0f, h = 0f;
        var each = new List<string>();
        int span = (int)MathF.Ceiling(blend / Tick) + 6;
        for (int i = 3; i < rows.Count - 1; i++)
        {
            if (rows[i].Began == "" && rows[i].Clip == rows[i - 1].Clip) continue;
            if (rows[i].Clip == "-" && rows[i - 1].Clip == "-") continue;
            var (a, b) = Pops(rows, i - 3, i + span);
            each.Add($"{rows[i - 1].Clip}>{rows[i].Clip}{(rows[i].Began != "" ? $" [{rows[i].Began}]" : "")} {F(a * 1000f, "0.0")}/{F(b * 1000f, "0.0")}");
            p = MathF.Max(p, a);
            h = MathF.Max(h, b);
        }
        return (p, h, each);
    }

    /// <summary>The wall, the body before it, a tap of jump (the wish forward the first ticks, the wall's way), then <paramref name="plan"/>'s wish from tick 20.</summary>
    static Scene Tapped(string name, Func<int, Body, Vector2> plan, int ticks, Gait gait = Gait.Run, Wall[]? walls = null) =>
        new(name, walls ?? [HoldsWall(Top)], Before(), (i, b) => new Ask(i < 20 ? Up : plan(i, b), gait, Jump: i == 0), ticks);

    /// <summary>A sprint at the wall from 6 m out and a jump at it from about 2.4 m, caught in the air; once on the wall the wish <paramref name="then"/> (a run, not a sprint).</summary>
    static Scene Caught(string name, Vector2 then, int ticks) =>
        new(name, [HoldsWall(Top)], new Vector3(-6f, 0f, 0f), (i, b) =>
        {
            bool on = b.State is BodyState.Hanging or BodyState.Traversing;
            return new Ask(on ? then : Up, on ? Gait.Run : Gait.Sprint, Jump: b.Feet.X > -1.8f && b.Feet.X < -1.3f && b.State == BodyState.Ground);
        }, ticks);

    /// <summary>A run at the wall and a tap close to it: on with a jump; then <paramref name="then"/>.</summary>
    static Scene RunUp(string name, Vector2 then, int ticks) =>
        new(name, [HoldsWall(Top)], new Vector3(-6f, 0f, 0f), (i, b) =>
            new Ask(b.State is BodyState.Hanging or BodyState.Traversing ? then : Up, Jump: b.Feet.X > 1f - 0.378f - 0.55f && b.State == BodyState.Ground && b.Feet.X < 0.5f), ticks);

    static int Begin(List<Row> rows, string act, int from = 0) => rows.FindIndex(from, r => r.Began.StartsWith(act + " ", StringComparison.Ordinal));

    /// <summary>The largest drift of a point's path off the straight line between its ends (m), from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static float OffLine(List<Row> rows, int from, int to, Func<Row, Vector3> of)
    {
        var a = of(rows[from]);
        var d = of(rows[to]) - a;
        float l = d.Length(), most = 0f;
        if (l < 1e-5f) return 0f;
        var u = d / l;
        for (int i = from; i <= to; i++)
        {
            var v = of(rows[i]) - a;
            most = MathF.Max(most, (v - u * Vector3.Dot(v, u)).Length());
        }
        return most;
    }

    [Fact]
    public void The_note_carries_the_rates_the_flow_and_the_diagonals_and_a_note_without_them_is_without_the_flow()
    {
        Assert.Equal("on=1 reach=0.25 rate=1.3 move=1.5 flow=0.15 diag=1 wrist=50", Flowing.Note());
        Assert.Equal("on=1 reach=0.25 rate=1 wrist=50", PersonWallSettings.NoFlow.Note());
        var dd = Flowing.WithNote("on=1 reach=0.25 rate=1 wrist=50");
        Assert.Equal((true, 1f, 1f, 0f, false, false), (dd.On, dd.Rate, dd.MoveRate, dd.FlowBlend, dd.Diagonals, dd.Flows));
        Assert.Equal(PersonWallSettings.NoFlow, dd);
        var back = Flowing.WithNote((Flowing with { Rate = 2f, MoveRate = 1.25f, FlowBlend = 0.2f, Diagonals = false }).Note());
        Assert.Equal((2f, 1.25f, 0.2f, false, true), (back.Rate, back.MoveRate, back.FlowBlend, back.Diagonals, back.Flows));
        Assert.False(Flowing.WithNote("off").On);
        var r = (Flowing with { MoveRate = 9f, FlowBlend = 0.9f }).Rules(null);
        Assert.Equal((3f, 0.3f, true, true), (r.MoveRate, r.FlowBlend, r.Diagonals, r.Flows));
        var rNoFlow = PersonWallSettings.NoFlow.Rules(null);
        Assert.Equal((1f, 1f, 0f, false, false), (rNoFlow.Rate, rNoFlow.MoveRate, rNoFlow.FlowBlend, rNoFlow.Diagonals, rNoFlow.Flows));
    }

    [Fact]
    public void Getting_on_with_a_move_held_flows_straight_into_climbing_standing_and_caught_in_the_air()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            var paths = WallClimbTests.Paths(assets, kind);
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            foreach (var (name, scene, act, clip) in new[]
            {
                ("standing", Tapped("standing", (i, b) => i < 420 ? Up : Vector2.Zero, 600), "OnGround", "wall_on_ground"),
                ("caught in the air", Caught("caught", Up, 400), "Catch", "wall_on_jump"),
                ("with a run-up", RunUp("run-up", Up, 500), "OnJump", "wall_on_jump"),
            })
            {
                var dd = Play(assets, kind, NoFlow, scene);
                var rows = Play(assets, kind, Flowing, scene);
                int on = Begin(rows, act), first = Begin(rows, "Move", on);
                int on0 = Begin(dd, act), first0 = Begin(dd, "Move", on0);
                Assert.True(on >= 0 && first > on, $"{who}, {name}: {Story(rows)}");
                var c = paths.Clips.First(x => x.Name == clip);
                // No rest at the idle: the getting-on straight into the move, before its end.
                Assert.DoesNotContain(rows.GetRange(on, first - on + 1), r => r.Clip == "wall_idle");
                Assert.True((first - on) * Tick < c.Duration / Flowing.MoveRate - 0.1f, $"{who}, {name}: {F((first - on) * Tick, "0.000")} s");
                var (sp, sh) = Pops(rows, first - 3, first + 30);
                var (ip, ih) = Pops(rows, on - 3, on + 12);
                var (ip0, ih0) = Pops(dd, on0 - 3, on0 + 12);
                var held = OnHoldsDiag(rows.GetRange(first, Math.Min(rows.Count - first, 200)), paths, onlyDiagonals: false);
                output.WriteLine($"{who}, {name}: {clip}, the first move begun {F((first - on) * Tick, "0.000")} s after it began (no flow {F((first0 - on0) * Tick, "0.000")}); the seam into the move: pelvis {F(sp * 1000f, "0.0")} mm, hands {F(sh * 1000f, "0.0")}; "
                                 + $"its start: pelvis {F(ip * 1000f, "0.0")} mm (no flow {F(ip0 * 1000f, "0.0")}), hands {F(ih * 1000f, "0.0")} ({F(ih0 * 1000f, "0.0")}); the knuckles at the moves' on_s: left {Holds(held, 0)}, right {Holds(held, 1)}; {Story(rows)}");
                Assert.InRange(sp, 0f, 0.01f);
                Assert.InRange(sh, 0f, 0.012f);
                foreach (var (cl, limb, off) in held.Where(x => x.Limb < 2))
                    Assert.True(MathF.Abs(off.X + idle.HandOff) <= 0.02f && MathF.Abs(off.Y) <= 0.02f && off.Z is >= -0.01f and <= 0.04f, $"{who}, {name}: {cl} hand {limb} {off}");
            }
        }
    }

    [Fact]
    public void A_held_climb_flows_move_to_move_at_wallClimbRate_and_released_settles_into_the_idle()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            var paths = WallClimbTests.Paths(assets, kind);
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            foreach (var (name, wish, gait, clip, release) in new[]
            {
                ("up quick", Up, Gait.Run, "wall_up_quick", 340), ("up careful", Up, Gait.Walk, "wall_up", 480), ("left quick", Left, Gait.Run, "wall_left_quick", 345),
            })
            {
                var c = paths.Clips.First(x => x.Name == clip);
                float lastOn = c.Holds.Where(h => h.On >= 0f && h.On < c.Duration).Max(h => h.On);
                var rows = Play(assets, kind, Flowing, Tapped(name, (i, b) => i < release ? wish : Vector2.Zero, release + 400, gait));
                var moves = Begun(rows).Where(b => b.What.StartsWith("Move ", StringComparison.Ordinal)).Select(b => b.I).ToList();
                Assert.True(moves.Count >= 3, $"{who}, {name}: {Story(rows)}");
                var gaps = moves.Zip(moves.Skip(1), (a, b) => (b - a) * Tick).ToList();
                var (sp, sh, each) = Seams(rows);
                var on = OnHoldsDiag(rows, paths, onlyDiagonals: false);
                output.WriteLine($"{who}, {name}: {moves.Count} × {clip} begun every {string.Join(", ", gaps.Select(g => F(g, "0.000")))} s (its own {F(c.Duration, "0.00")} s, at {F(Flowing.Rate, "0.0")} {F(c.Duration / Flowing.Rate, "0.000")}, its last limb's hold at {F(lastOn / Flowing.Rate, "0.000")}); "
                                 + $"the seams' pops: pelvis {F(sp * 1000f, "0.0")} mm, hands {F(sh * 1000f, "0.0")} ({string.Join("; ", each)}); the knuckles at each on_s: left {Holds(on, 0)}, right {Holds(on, 1)}; the balls: left {Holds(on, 2)}, right {Holds(on, 3)}; {Story(rows)}");
                foreach (var g in gaps) Assert.InRange(g, lastOn / Flowing.Rate - Tick, c.Duration / Flowing.Rate - 0.02f);   // overlapping: begun as the last limb lands, before the end
                // No faster than a human speed climber's 2.9 hand moves a second (two hands a move).
                foreach (var g in gaps) Assert.True(2f / g <= 2.9f, $"{who}, {name}: {F(2f / g, "0.00")} hand moves a second");
                Assert.DoesNotContain(rows.GetRange(moves[0], moves[^1] - moves[0]), r => r.Clip == "wall_idle");
                Assert.Equal(("wall_idle", BodyState.Hanging), (rows[^1].Clip, rows[^1].State));
                Assert.InRange(sp, 0f, 0.01f);
                foreach (var (cl, limb, off) in on.Where(x => x.Limb < 2))
                    Assert.True(MathF.Abs(off.X + idle.HandOff) <= (who == "woman" && gait == Gait.Walk ? 0.035f : 0.02f) && MathF.Abs(off.Y) <= (who == "woman" && gait == Gait.Walk ? 0.07f : 0.02f) && off.Z is >= -0.01f and <= 0.04f, $"{who}, {name}: {cl} hand {limb} {off}");
            }
        }
    }

    [Fact]
    public void Over_the_top_in_one_go_into_standing_and_released_at_the_top_the_braced_hang()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            WallClimbTests.Paths(assets, kind);
            var scene = Tapped("over", (i, b) => b.State == BodyState.Ground && b.Feet.Z > Top - 0.1f ? Vector2.Zero : Up, 1500);
            var rows = Play(assets, kind, Flowing, scene);
            var dd = Play(assets, kind, NoFlow, scene);
            int over = Begin(rows, "ToHang"), stood = rows.FindIndex(over, r => r.State == BodyState.Ground);
            int over0 = Begin(dd, "ToHang"), stood0 = dd.FindIndex(over0, r => r.State == BodyState.Ground);
            int pull = rows.FindIndex(over, r => r.Clip == "pull_up_braced"), pull0 = dd.FindIndex(over0, r => r.Clip == "pull_up_braced");
            var (pp, ph) = Pops(rows, pull - 3, pull + 40);
            var (pp0, ph0) = Pops(dd, pull0 - 3, pull0 + 40);
            output.WriteLine($"{who}, up held over the top: {Story(rows)}");
            output.WriteLine($"  over the top to standing {F((stood - over) * Tick, "0.000")} s (no flow {F((stood0 - over0) * Tick, "0.000")}): the clip over the top {F((pull - over) * Tick, "0.000")} s ({F((pull0 - over0) * Tick, "0.000")}), the pull-up {F((stood - pull) * Tick, "0.000")} ({F((stood0 - pull0) * Tick, "0.000")}); "
                             + $"into the pull-up: pelvis {F(pp * 1000f, "0.0")} mm ({F(pp0 * 1000f, "0.0")}), hands {F(ph * 1000f, "0.0")} ({F(ph0 * 1000f, "0.0")})");
            var clips = Clips(rows);
            int at = clips.IndexOf("wall_to_hang_braced");
            Assert.True(at > 0 && clips[at + 1] == "pull_up_braced", string.Join(", ", clips));   // no braced hang between
            Assert.DoesNotContain(rows.GetRange(over, stood - over), r => r.Clip == "hang_braced");
            Assert.InRange(rows[stood].Feet.Z, Top - 0.02f, Top + 0.02f);
            Assert.True(stood - over < 0.7f * (stood0 - over0), "faster than no flow");
            Assert.InRange(pp, 0f, 0.012f);
            // Released as it goes over: the braced hang, as before.
            bool gone = false;
            var let = Play(assets, kind, Flowing, Tapped("over, let go", (i, b) => (gone |= b.Feet.Z > 2.16f + (who == "woman" ? 0.08f : 0f)) ? Vector2.Zero : Up, 1100));
            output.WriteLine($"{who}, released at the top: {Story(let)}");
            Assert.Contains("wall_to_hang_braced", Clips(let));
            Assert.Equal(("hang_braced", BodyState.Hanging), (let[^1].Clip, let[^1].State));
        }
    }

    [Fact]
    public void Diagonals_quick_blended_and_careful_by_turns_keep_the_hands_and_feet_on_their_holds()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            var paths = WallClimbTests.Paths(assets, kind);
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            foreach (var (name, wish, gait, below) in new[]
            {
                ("up-left quick", Diag(Up, Left), Gait.Run, false), ("down-right quick", Diag(Down, Right), Gait.Run, true),
                ("up-right quick", Diag(Up, Right), Gait.Run, false), ("down-left quick", Diag(Down, Left), Gait.Run, true),
                ("up-left careful", Diag(Up, Left), Gait.Walk, false), ("down-right careful", Diag(Down, Right), Gait.Walk, true),
            })
            {
                bool careful = gait == Gait.Walk;
                // Down ones from three moves up (up held first); then the diagonal, two steps (four moves careful), released.
                int up = below ? (careful ? 600 : 330) : 0, len = careful ? 600 : 330;
                var rows = Play(assets, kind, Flowing, Tapped(name, (i, b) => i < up ? Up : i < up + len ? wish : Vector2.Zero, up + len + 500, gait));
                var diag = Begun(rows).Where(b => b.I > up && b.What.StartsWith("Move ", StringComparison.Ordinal)).ToList();
                Assert.True(diag.Count >= 2, $"{who}, {name}: {Story(rows)}");
                int sl = Math.Sign(Vector2.Dot(wish, Left)), su = Math.Sign(Vector2.Dot(wish, Up));
                var on = OnHoldsDiag(rows, paths, onlyDiagonals: !careful);
                if (careful) on = OnHoldsDiag(rows.GetRange(diag[0].I - 1, rows.Count - diag[0].I + 1), paths, onlyDiagonals: false);
                var (sp, sh, each) = Seams(rows.GetRange(diag[0].I - 4, rows.Count - diag[0].I + 4));
                int a = diag[0].I, e = diag.Count > 1 ? diag[1].I : rows.Count - 1;
                float drift = OffLine(rows, a, e, r => r.Feet), pdrift = OffLine(rows, a, e, r => r.Pelvis);
                var start = rows[a].Place;
                var end = rows[diag[^1].I].Place;
                output.WriteLine($"{who}, {name}: {string.Join(", ", diag.Select(d => d.What))}; from {F(start.Y, "0.000")},{F(start.Z, "0.000")} to {F(end.Y, "0.000")},{F(end.Z, "0.000")} (along, up); "
                                 + $"the first move's capsule {F(drift * 100f, "0.0")} cm off the straight line between its ends, the pelvis {F(pdrift * 100f, "0.0")} cm; the seams' pops: pelvis {F(sp * 1000f, "0.0")} mm, hands {F(sh * 1000f, "0.0")} ({string.Join("; ", each)}); "
                                 + $"the knuckles at each on_s: left {Holds(on, 0)}, right {Holds(on, 1)}; the balls: left {Holds(on, 2)}, right {Holds(on, 3)}");
                if (careful)
                {
                    // By turns: the side's careful move and the up's (or down's), each flowing out of the other.
                    Assert.All(diag, d => Assert.True(d.What.StartsWith("Move 0,", StringComparison.Ordinal) || d.What.Contains(",0 p1", StringComparison.Ordinal), d.What));
                    Assert.Contains(diag, d => d.What.StartsWith("Move 0,", StringComparison.Ordinal));
                    Assert.Contains(diag, d => d.What.Contains(",0 p1", StringComparison.Ordinal));
                }
                else
                {
                    Assert.All(diag, d => Assert.StartsWith($"Move {sl},{su} p0", d.What, StringComparison.Ordinal));
                    // At a straight move's speed along its longer path (√2): about 1.4 times a straight move's time (Horizon's rule for a warped move).
                    var side = paths.Clips.First(x => x.Name == rows[diag[0].I].Clip);
                    float straight = (side.Holds.Where(h => h.On >= 0f && h.On < side.Duration).Max(h => h.On) + Flowing.Rate * Tick) / Flowing.Rate;
                    for (int k = 1; k < diag.Count; k++)
                        Assert.InRange((diag[k].I - diag[k - 1].I) * Tick / straight, 1.3f, 1.55f);
                    output.WriteLine($"  each diagonal {F((diag[1].I - diag[0].I) * Tick, "0.000")} s against a straight move's {F(straight, "0.000")}");
                }
                // Each move a step: a diagonal one along and one up or down (careful: one or the other by turns), the wall position moved by the notes' steps.
                for (int k = 1; k < diag.Count; k++)
                {
                    var dp = rows[diag[k].I].Place - rows[diag[k - 1].I].Place;
                    float along = Vector2.Dot(new Vector2(dp.X, dp.Y), new Vector2(-rows[diag[k].I].In.Y, rows[diag[k].I].In.X));
                    var want = careful ? (diag[k - 1].What.StartsWith("Move 0,", StringComparison.Ordinal) ? new Vector2(0f, 0.4f * su) : new Vector2(0.4f * sl, 0f)) : new Vector2(0.4f * sl, 0.4f * su);
                    Assert.True(Vector2.Distance(new Vector2(along, dp.Z), want) < 0.003f, $"{who}, {name}: step {k} {F(along, "0.000")},{F(dp.Z, "0.000")} against {want}");
                }
                Assert.InRange(sp, 0f, 0.01f);
                // The knuckles where the notes have them within 2 cm (the brief's mark for the blend); the woman's careful straight moves her own late reach across
                // (an earlier build measured it in wall_right: 13.3 cm out and 4.5 along there, the clip's own).
                foreach (var (cl, limb, off) in on.Where(x => x.Limb < 2))
                    Assert.True(MathF.Abs(off.X + idle.HandOff) <= (who == "woman" && careful ? 0.035f : 0.02f) && MathF.Abs(off.Y) <= (who == "woman" && careful ? 0.07f : 0.02f) && off.Z is >= -0.01f and <= 0.04f, $"{who}, {name}: {cl} hand {limb} {off}");
                // The balls on their holds' tops as the clips have them (their own turn-out along, 9.5–11.8 cm); the woman's leading foot going down and left is
                // short of its hold (her leg's reach: 5 cm over its top, measured, for the models repository).
                foreach (var (cl, limb, off) in on.Where(x => x.Limb >= 2))
                    Assert.True(off.Z is >= -0.01f && off.Z <= (who == "woman" && su < 0 && sl > 0 && !careful ? 0.06f : 0.03f) && MathF.Abs(off.X) <= 0.11f, $"{who}, {name}: {cl} foot {limb} {off}");
            }
        }
    }

    [Fact]
    public void The_wish_is_held_a_moment_a_brief_let_go_keeps_the_chain_and_one_key_after_the_other_is_a_diagonal()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        WallClimbTests.Paths(assets, kind);
        // A held climb, and the same let go for 8 ticks (within the 12 held) about its second move's start: the same moves at the same ticks.
        var held = Play(assets, kind, Flowing, Tapped("held", (i, b) => i < 330 ? Up : Vector2.Zero, 700));
        var moves = Begun(held).Where(b => b.What.StartsWith("Move ", StringComparison.Ordinal)).Select(b => b.I).ToList();
        int cut = moves[1];
        var gap = Play(assets, kind, Flowing, Tapped("gap", (i, b) => i < 330 && (i < cut - 4 || i >= cut + 4) ? Up : Vector2.Zero, 700));
        var gapMoves = Begun(gap).Where(b => b.What.StartsWith("Move ", StringComparison.Ordinal)).Select(b => b.I).ToList();
        output.WriteLine($"held: moves at {string.Join(", ", moves)}; let go {cut - 4} to {cut + 3}: {string.Join(", ", gapMoves)}");
        Assert.Equal(moves, gapMoves);
        Assert.DoesNotContain(gap.GetRange(moves[0], moves[^1] - moves[0]), r => r.Clip == "wall_idle");
        // From the idle: up, then left 6 ticks after (within the hold): the up move taken over by the diagonal up and left.
        var rest = Play(assets, kind, Flowing, Tapped("rest", (i, b) => Vector2.Zero, 400));
        int idle = rest.FindIndex(r => r.Clip == "wall_idle") + 12;
        var two = Play(assets, kind, Flowing, Tapped("two", (i, b) => i < idle ? Vector2.Zero : i < idle + 6 ? Up : i < idle + 60 ? Diag(Up, Left) : Vector2.Zero, idle + 300));
        var began = Begun(two).Where(b => b.I >= idle).ToList();
        var (sp, sh, _) = Seams(two.GetRange(idle - 4, two.Count - idle + 4));
        output.WriteLine($"up then left 6 ticks after: {string.Join(", ", began.Select(b => $"{b.I - idle} {b.What}"))}; the seams' pops: pelvis {F(sp * 1000f, "0.0")} mm, hands {F(sh * 1000f, "0.0")}; ends at {F(two[^1].Place.Y, "0.000")},{F(two[^1].Place.Z, "0.000")}");
        Assert.StartsWith("Move 0,1", began[0].What, StringComparison.Ordinal);
        Assert.True(began.Count >= 2 && began[1].What.StartsWith("Move 1,1", StringComparison.Ordinal) && began[1].I - idle <= 12, string.Join(", ", began));
        Assert.InRange(two[^1].Place.Y, 0.399f, 0.401f);   // one step across and up: the up move did not go on as well
        Assert.InRange(sp, 0f, 0.01f);
        Assert.InRange(sh, 0f, 0.015f);   // the hands' IK reckons the up move faded out from where it began
    }

    [Fact]
    public void A_diagonal_with_no_hold_there_is_the_straight_move_the_wish_s_stronger_part_asks_for()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        WallClimbTests.Paths(assets, kind);
        // A wall whose left end is 0.5 m to the left of the start: no hold a step up and left; the wish up and a little less left.
        var narrow = new Wall(new Vector3(1f, -3f, 0f), new Vector3(3f, 0.5f, Top));
        var wish = Vector2.Normalize(new Vector2(1f, 0.9f));
        var rows = Play(assets, kind, Flowing, Tapped("narrow", (i, b) => i < 300 ? wish : Vector2.Zero, 600, walls: [narrow]));
        var moves = Begun(rows).Where(b => b.What.StartsWith("Move ", StringComparison.Ordinal)).ToList();
        output.WriteLine($"no hold up and left: {string.Join(", ", moves.Select(m => m.What))}; {Story(rows)}");
        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.StartsWith("Move 0,1 ", m.What, StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Refused != "");   // a diagonal not taken is not told: the straight move is
        // Room: with the wall going on, the same wish climbs up and left.
        var wide = Play(assets, kind, Flowing, Tapped("wide", (i, b) => i < 300 ? wish : Vector2.Zero, 600));
        Assert.Contains(wide, r => r.Began.StartsWith("Move 1,1 ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_catch_from_a_sprint_and_the_run_up_are_softened_and_every_seam_measured()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            WallClimbTests.Paths(assets, kind);
            foreach (var (name, scene, act, most) in new[]
            {
                ("caught at a sprint", Caught("caught", Vector2.Zero, 400), "Catch", 0.02f), ("on with a run-up", RunUp("run-up", Vector2.Zero, 500), "OnJump", 0.012f),
            })
            {
                var rows = Play(assets, kind, Flowing, scene);
                var dd = Play(assets, kind, NoFlow, scene);
                int at = Begin(rows, act), at0 = Begin(dd, act);
                var (p, h) = Pops(rows, at - 3, at + 12);
                var (p0, h0) = Pops(dd, at0 - 3, at0 + 12);
                output.WriteLine($"{who}, {name}: into it the pelvis's pop {F(p * 1000f, "0.0")} mm (no flow {F(p0 * 1000f, "0.0")}), the hands' {F(h * 1000f, "0.0")} ({F(h0 * 1000f, "0.0")}); the speed then {F((rows[at - 1].Feet - rows[at - 2].Feet).Length() / Tick, "0.00")} m/s");
                Assert.True(at > 0 && at0 > 0, Story(rows));
                Assert.InRange(p, 0f, most);
                Assert.True(p < 0.6f * p0, $"{who}, {name}: {p} against no flow {p0}");
            }
            // Every seam of a climb up, across, a corner, over the top and off at the foot: the earlier build's against the earlier build's.
            var corner = new Wall(new Vector3(1f, -3f, 0f), new Vector3(3f, 1.1f, Top));
            foreach (var (name, scene) in new[]
            {
                ("up and over", Tapped("over", (i, b) => b.State == BodyState.Ground && b.Feet.Z > Top - 0.1f ? Vector2.Zero : Up, 1500)),
                ("round the corner", Tapped("corner", (i, b) => i < 700 ? Left : Vector2.Zero, 1100, walls: [corner])),
                ("up and off at the foot", Tapped("off", (i, b) => i < 200 ? Up : b.State == BodyState.Hanging ? Down : Vector2.Zero, 900)),
            })
            {
                var (sp, sh, each) = Seams(Play(assets, kind, Flowing, scene));
                var (sp0, sh0, each0) = Seams(Play(assets, kind, NoFlow, scene), 0.15f);
                output.WriteLine($"{who}, {name}: the seams (pelvis/hands mm) flow: {string.Join("; ", each)}");
                output.WriteLine($"  no flow: {string.Join("; ", each0)}");
                Assert.InRange(sp, 0f, 0.012f);
            }
        }
    }

    [Fact]
    public void A_corner_and_off_at_the_foot_at_wallMoveRate_facing_the_other_face()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            var paths = WallClimbTests.Paths(assets, kind);
            foreach (var (name, walls, wish, clip) in new[]
            {
                ("corner out left", new[] { new Wall(new Vector3(1f, -3f, 0f), new Vector3(3f, 1.1f, Top)) }, Left, "wall_corner_out_left"),
                ("corner in right", new[] { HoldsWall(Top), new Wall(new Vector3(-1f, -4f, 0f), new Vector3(1f, -1.3f, Top)) }, Right, "wall_corner_in_right"),
            })
            {
                var rows = Play(assets, kind, Flowing, Tapped(name, (i, b) => wish, 900, walls: walls));
                int st = rows.FindIndex(r => r.Clip == clip);
                Assert.True(st > 0, $"{who}, {name}: {Story(rows)}");
                int end = rows.FindIndex(st, r => r.Clip != clip);
                float off = MathF.Abs(DetMath.WrapAngle(rows[end].Yaw + MathF.PI / 2f)) * 180f / MathF.PI;
                var c = paths.Clips.First(x => x.Name == clip);
                output.WriteLine($"{who}, {name}: {clip} {F((end - st) * Tick, "0.000")} s (its own {F(c.Duration, "0.00")}, at {F(Flowing.MoveRate, "0.0")} {F(c.Duration / Flowing.MoveRate, "0.000")}); the facing after it {F(rows[end].Yaw * 180f / MathF.PI, "0.0")}° ({F(off, "0.00")}° off the other face's)");
                Assert.InRange((end - st) * Tick, c.Duration / Flowing.MoveRate - 2f * Tick, c.Duration / Flowing.MoveRate + 2f * Tick);
                Assert.InRange(off, 0f, 0.5f);
            }
            var down = Play(assets, kind, Flowing, Tapped("off", (i, b) => i < 200 ? Up : b.State == BodyState.Hanging ? Down : Vector2.Zero, 900));
            var dd = Play(assets, kind, NoFlow, Tapped("off", (i, b) => i < 200 ? Up : i < 700 ? Down : Vector2.Zero, 1300));
            int o = Begin(down, "Off"), s1 = down.FindIndex(Math.Max(0, o), r => r.State == BodyState.Ground);
            int o0 = Begin(dd, "Off"), s0 = dd.FindIndex(Math.Max(0, o0), r => r.State == BodyState.Ground);
            output.WriteLine($"{who}, off at the foot: standing {F((s1 - o) * Tick, "0.000")} s after it began (no flow {F((s0 - o0) * Tick, "0.000")})");
            Assert.True(o > 0 && s1 > o && s0 > o0, Story(down));
            Assert.InRange((s1 - o) * Tick, 0f, 0.7f * (s0 - o0) * Tick);
            Assert.InRange(down[s1].Feet.Z, -0.01f, 0.02f);
        }
    }

    [Fact]
    public void Let_go_and_push_off_at_wallMoveRate_into_the_fall()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        WallClimbTests.Paths(assets, kind);
        foreach (var (name, plan, clip) in new (string, Func<int, Body, Ask>, string)[]
        {
            ("let go", (i, b) => i < 20 ? new Ask(Up, Jump: i == 0) : i < 250 ? new Ask(Up) : new Ask(Vector2.Zero, Cancel: i == 330), "wall_let_go"),
            ("push off", (i, b) => i < 20 ? new Ask(Up, Jump: i == 0) : i < 250 ? new Ask(Up) : new Ask(i == 330 ? Down : Vector2.Zero, Jump: i == 330), "wall_push_off"),
        })
        {
            var rows = Play(assets, kind, Flowing, new Scene(name, [HoldsWall(Top)], Before(), plan, 600));
            int at = rows.FindIndex(r => r.Clip == clip), air = rows.FindIndex(Math.Max(0, at), r => r.State == BodyState.Air);
            int land = rows.FindIndex(Math.Max(0, air), r => r.State == BodyState.Ground);
            output.WriteLine($"{name}: {clip}, in the air {F((air - at) * Tick, "0.000")} s after (its own {F(WallClimbTests.Paths(assets, kind).Clips.First(x => x.Name == clip).Duration, "0.000")}); landed {F(rows[land].Feet.X, "0.000")}; {Story(rows)}");
            Assert.True(at > 0 && air > at && land > air, Story(rows));
            if (name == "push off") Assert.True(rows[land].Feet.X < rows[at].Place.X - 0.6f);
        }
    }

    [Fact]
    public void Off_the_switch_the_climb_has_no_flow_store_the_idle_between_and_the_clips_own_rates()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        var paths = WallClimbTests.Paths(assets, kind);
        var (w, _) = Make(assets, kind, NoFlow, Before(), unbound: false, HoldsWall(Top));
        Assert.True(w.TryStore<PersonWall>(out _));
        Assert.False(w.TryStore<PersonWallFlow>(out _));
        var (w2, _) = Make(assets, kind, Flowing, Before(), unbound: false, HoldsWall(Top));
        Assert.True(w2.TryStore<PersonWallFlow>(out _));
        var rows = Play(assets, kind, NoFlow, Tapped("no flow", (i, b) => Up, 700));
        var moves = Begun(rows).Where(b => b.What.StartsWith("Move ", StringComparison.Ordinal)).Select(b => b.I).ToList();
        output.WriteLine($"no flow: {Story(rows)}");
        Assert.Contains(rows.GetRange(0, moves[0]), r => r.Clip == "wall_idle");   // the getting-on to the idle first
        Assert.InRange((moves[1] - moves[0]) * Tick, paths.Clips.First(c => c.Name == "wall_up_quick").Duration - Tick, 1.0f + 2f * Tick);
        var diag = Play(assets, kind, NoFlow, Tapped("no flow diagonal", (i, b) => Diag(Up, Left), 500));
        Assert.DoesNotContain(diag, r => r.Began.StartsWith("Move 1,1", StringComparison.Ordinal));
    }
}
