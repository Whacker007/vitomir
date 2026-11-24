// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The proving ground's findings put right (ours): a body of 1.8 m walks up stairs
/// under a slab its height or more over the nosings (2.0 m asked) and stops under less; one threshold for thin and deep
/// (a sprint hurdles walls 0.3 m thick, a bench stops it); a plain jump lands only on what its feet clear and a tap
/// close to a top of 0.35–0.6 m is a hop up; a body flush on a floor walks across the floor's own triangle edges.
/// </summary>
public sealed class MotorReachTests
{
    /// <summary>What a drive did: the moves begun in order, the grabs, the highest the feet stood (on the ground, making no move), the body at the end.</summary>
    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public int Grabs;
        public float Stood = float.MinValue, Highest = float.MinValue;
        public Body End;
        public override string ToString() => $"moves [{string.Join(", ", Started)}], ended at {End.Feet} ({End.State}), stood at {Stood:0.000}, highest {Highest:0.000}";
    }

    /// <summary>
    /// Drives a body from (<paramref name="x"/>, 0) along +X at a gait for <paramref name="seconds"/> (standing once past
    /// <paramref name="stop"/>), jump pressed once the feet are at <paramref name="press"/> (NaN: never) and held for
    /// <paramref name="hold"/> ticks (1: a tap).
    /// </summary>
    static Drove Go(LevelSpace level, float x, Gait gait, float seconds, float press = float.NaN, int hold = 1, float stop = float.PositiveInfinity)
    {
        var d = new Drove();
        var body = Drive.Settle(level, new Vector3(x, 0f, 0.02f));
        var move = default(Traversal);
        int pressed = -1;
        d.End = Drive.Moves(level, body, ref move, (int)(seconds * 120f), (i, b) =>
        {
            var m = b.Feet.X > stop ? new MoveIntent { Facing = 0f, Faces = true, Gait = gait } : Drive.Toward(1, 0, gait);
            if (pressed < 0 && !float.IsNaN(press) && b.Feet.X >= press && b.State == BodyState.Ground) pressed = i;
            m.Jump = i == pressed;
            m.JumpHeld = pressed >= 0 && i < pressed + hold;
            return m;
        }, (_, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.Grabbed) d.Grabs++;
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
            if (b.State == BodyState.Ground && m.Kind == TraversalKind.None) d.Stood = MathF.Max(d.Stood, b.Feet.Z);
        });
        return d;
    }

    static TestLevel Ground() => new TestLevel().Floor(-10, -10, 12, 10);

    /// <summary>A block across the way from x 1, <paramref name="depth"/> deep and <paramref name="height"/> high.</summary>
    static LevelSpace Block(float height, float depth) => Ground().Box(new(1, -3, 0), new(1 + depth, 3, height)).Build();

    // ---------------------------------------------------------------- headroom on stairs

    /// <summary>
    /// A flight up along +X from x 1 of <paramref name="riser"/> risers on 0.30 m treads to a landing about 1.2 m high, 3 m
    /// deep, under a slab (from x −2) whose edge is over the nosing of the step two below the landing and whose underside
    /// is <paramref name="headroom"/> over that nosing — the proving ground's <c>stairs-head</c> lanes.
    /// </summary>
    static LevelSpace StairsUnder(float riser, float headroom, out float landing, out float top, out float edge)
    {
        const float tread = 0.3f;
        int n = Math.Max(2, (int)MathF.Round(1.2f / riser));
        var t = Ground();
        for (int k = 1; k < n; k++) t.Box(new(1 + (k - 1) * tread, -3, 0), new(1 + k * tread, 3, k * riser));
        landing = 1 + (n - 1) * tread;
        top = n * riser;
        t.Box(new(landing, -3, 0), new(landing + 3, 3, top));
        int ks = Math.Max(1, n - 2);
        edge = 1 + (ks - 1) * tread;
        float under = ks * riser + headroom;
        t.Box(new(-2, -4, under), new(edge, 4, under + 0.3f));
        return t.Build($"stairs-{riser}-under-{headroom}");
    }

    /// <summary>
    /// 2.0 m, 1.9 and 1.85 m over the nosings, on risers to 0.35 m, at every pace, up and back down. On risers of
    /// 0.35 m on 0.30 m treads (a tread no deeper than the body's radius) the capsule's rounded top, 0.6 m across, still
    /// reaches back under the slab's edge as its middle comes onto the next tread (and, going down, forward under it
    /// while its feet are still level with the tread above), so there it needs the riser and 1.5 m and a little: 1.85 m
    /// stops it, and 1.95 m is tried instead.
    /// </summary>
    [Theory]
    [InlineData(0.15f, 1.85f)]
    [InlineData(0.20f, 1.85f)]
    [InlineData(0.30f, 1.85f)]
    [InlineData(0.35f, 1.95f)]
    public void A_body_walks_up_and_down_stairs_under_a_slab_its_height_and_more_over_the_nosings_at_every_pace(float riser, float lowest)
    {
        var wrong = new List<string>();
        foreach (var headroom in new[] { 2.0f, 1.9f, lowest })
            foreach (var gait in new[] { Gait.Walk, Gait.Run, Gait.Sprint })
            {
                var level = StairsUnder(riser, headroom, out float landing, out float top, out _);
                var d = Go(level, -3f, gait, 8f, stop: landing + 1.2f);
                if (d.End.Feet.X < landing + 1f || MathF.Abs(d.End.Feet.Z - top) > 0.01f || d.Started.Count != 0)
                    wrong.Add($"{headroom} m at a {gait}: {d}");
                if (Drive.Depth(level, d.End) > 1e-3f) wrong.Add($"{headroom} m at a {gait}: inside the level by {Drive.Depth(level, d.End):0.000}");
                // And back down under it.
                var back = Drive.Settle(level, new Vector3(landing + 1f, 0f, top + 0.02f), MathF.PI);
                back = Drive.Run(level, back, 120 * 8, (_, b) => b.Feet.X < -1.5f ? new MoveIntent { Facing = MathF.PI, Faces = true } : Drive.Toward(-1, 0, gait));
                if (back.Feet.X > -1.5f || MathF.Abs(back.Feet.Z) > 0.01f) wrong.Add($"{headroom} m down at a {gait}: ended at {back.Feet}");
            }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Theory]
    [InlineData(0.15f)]
    [InlineData(0.20f)]
    [InlineData(0.30f)]
    [InlineData(0.35f)]
    public void Under_a_slab_lower_than_its_height_over_the_nosings_a_standing_body_stops_and_a_crouched_one_passes(float riser)
    {
        var wrong = new List<string>();
        foreach (var headroom in riser > 0.3f ? new[] { 1.82f, 1.78f, 1.7f, 1.5f } : new[] { 1.78f, 1.7f, 1.5f })
        {
            foreach (var gait in new[] { Gait.Walk, Gait.Run, Gait.Sprint })
            {
                var level = StairsUnder(riser, headroom, out float end, out _, out float edge);
                var d = Go(level, -3f, gait, 8f);
                if (d.End.Feet.X > end || d.End.Feet.X > edge + 0.3f + 1e-3f) wrong.Add($"{headroom} m at a {gait}: not stopped, {d}");
            }
            var under = StairsUnder(riser, headroom, out float landing, out float top, out _);
            var body = Drive.Settle(under, new Vector3(-3f, 0f, 0.02f));
            body = Drive.Run(under, body, 120 * 12, (_, b) => b.Feet.X > landing + 1.2f ? new MoveIntent { Crouch = true } : Drive.Toward(1, 0, Gait.Walk) with { Crouch = true });
            if (body.Feet.X < landing + 1f || MathF.Abs(body.Feet.Z - top) > 0.01f) wrong.Add($"{headroom} m crouched: ended at {body.Feet}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void On_level_ground_a_ceiling_lower_than_the_body_stops_it_and_one_just_higher_is_walked_under()
    {
        foreach (var (clear, passes) in new[] { (1.79f, false), (1.81f, true) })
        {
            var level = Ground().Box(new(1, -4, clear), new(4, 4, clear + 0.3f)).Build();
            var d = Go(level, -3f, Gait.Run, 4f, stop: 6f);
            Assert.True(passes == d.End.Feet.X > 4.5f, $"{clear} m: {d}");
        }
    }

    // ---------------------------------------------------------------- thin and deep: one threshold

    [Theory]
    [InlineData(0.6f, 0.3f, true)]
    [InlineData(0.8f, 0.3f, true)]
    [InlineData(1.0f, 0.3f, true)]
    [InlineData(0.6f, 0.1f, true)]
    [InlineData(0.45f, 0.4f, true)]    // the course's bench: a sprint vaults it
    [InlineData(0.6f, 0.7f, true)]     // the course's trough
    [InlineData(0.8f, 0.8f, false)]    // deeper than the sprint vault's 0.7 m
    [InlineData(1.1f, 0.3f, false)]
    public void A_sprint_vaults_a_thing_to_a_metre_high_and_0_7_m_deep_and_a_deeper_or_higher_one_stops_it(float height, float depth, bool hurdles)
    {
        var level = Block(height, depth);
        var d = Go(level, -8f, Gait.Sprint, 3f);
        if (hurdles)
            Assert.True(d.Started.SequenceEqual([TraversalKind.Hurdle]) && d.End.Feet.X > 1 + depth + 0.3f && d.End.Feet.Z < 0.03f, d.ToString());
        else
            Assert.True(d.Started.Count == 0 && d.End.Feet.X < 1f - 0.25f && d.Highest < 0.05f, d.ToString());
    }

    [Theory]
    [InlineData(0.8f, 0.3f, TraversalKind.Vault)]
    [InlineData(1.0f, 0.3f, TraversalKind.Vault)]
    [InlineData(0.65f, 0.4f, TraversalKind.ClimbOnto)]
    [InlineData(0.8f, 0.4f, TraversalKind.ClimbOnto)]
    public void A_tap_vaults_over_a_thin_top_and_climbs_onto_one_as_deep_as_a_top_stood_on(float height, float depth, TraversalKind want)
    {
        var level = Block(height, depth);
        foreach (var gait in new[] { Gait.Walk, Gait.Run })
        {
            var d = Go(level, -4f, gait, 6f, press: 1f - (gait == Gait.Walk ? 0.55f : 0.9f), hold: 1);
            Assert.True(d.Started.FirstOrDefault() == want, $"{gait}: {d}");
            if (want == TraversalKind.ClimbOnto) Assert.True(d.Stood >= height - 0.02f, $"{gait}: {d}");
            else Assert.True(d.End.Feet.X > 1 + depth + 0.2f && d.End.Feet.Z < 0.03f, $"{gait}: {d}");
        }
    }

    // ---------------------------------------------------------------- no gap between the mantle and the grab

    [Theory]
    [InlineData(2.0f, true)]
    [InlineData(2.2f, true)]
    [InlineData(2.5f, true)]
    [InlineData(2.8f, true)]
    [InlineData(3.0f, false)]
    public void A_tap_grabs_a_lip_from_the_mantle_band_s_top_to_the_jump_s_reach_and_forward_held_pulls_up(float height, bool reaches)
    {
        var level = Block(height, 2f);
        var d = Go(level, -4f, Gait.Walk, 8f, press: 1f - 0.55f, hold: 1);
        if (reaches) Assert.True(d.Grabs == 1 && d.Started.Contains(TraversalKind.PullUp) && d.Stood >= height - 0.02f, d.ToString());
        else Assert.True(d.Grabs == 0 && d.Started.Count == 0 && d.End.Feet.Z < 0.03f, d.ToString());
    }

    [Theory]
    [InlineData(2.0f)]
    [InlineData(2.2f)]
    public void Nothing_held_at_the_grab_s_end_of_a_lip_under_the_hands_hangs_from_it(float height)
    {
        var level = Block(height, 2f);
        // A tap, and the walk stopped as it is made: neither forward nor jump held when the grab ends.
        var d = Go(level, -4f, Gait.Walk, 6f, press: 1f - 0.55f, hold: 1, stop: 1f - 0.55f);
        Assert.True(d.Grabs == 1 && d.End.State == BodyState.Hanging && !d.Started.Contains(TraversalKind.PullUp), d.ToString());
    }

    // ---------------------------------------------------------------- a plain jump lands only on what its feet clear

    [Theory]
    [InlineData(0.5f, true)]
    [InlineData(0.55f, true)]
    [InlineData(0.65f, false)]
    [InlineData(0.75f, false)]
    [InlineData(0.85f, false)]
    public void A_tap_s_jump_lands_on_a_top_to_about_0_6_m_and_climbs_onto_one_higher(float height, bool lands)
    {
        var level = Block(height, 2f);
        foreach (var at in new[] { 0.95f, 1.1f, 1.3f })
        {
            // Jump pressed with the body's middle further from the face than a hop up reaches (0.75 m), at a run: the
            // plain jump lands on what its feet clear; higher, the obstacle decides in the air: a climb.
            var d = Go(level, -4f, Gait.Run, 3f, press: 1f - at);
            Assert.True(!d.Started.Contains(TraversalKind.HopUp), $"{at}: {d}");
            if (lands) Assert.True(d.Stood >= height - 0.01f && d.Started.Count == 0, $"taken off {at} m from it: {d}");
            else Assert.True(d.Started.SequenceEqual([TraversalKind.ClimbOnto]) && d.Stood >= height - 0.01f, $"taken off {at} m from it: {d}");
        }
    }

    [Theory]
    [InlineData(0.40f)]
    [InlineData(0.45f)]
    [InlineData(0.50f)]
    [InlineData(0.60f)]
    public void A_tap_close_to_a_top_of_0_35_to_0_6_m_moving_towards_it_is_a_hop_up(float height)
    {
        foreach (var depth in new[] { 0.4f, 2f })
            foreach (var at in new[] { 0.45f, 0.6f, 0.7f })
            {
                var d = Go(Block(height, depth), -4f, Gait.Walk, 6f, press: 1f - at);
                Assert.True(d.Started.SequenceEqual([TraversalKind.HopUp]) && d.Stood >= height - 0.02f, $"{depth} deep, tapped {at} m from it: {d}");
            }
    }

    // ---------------------------------------------------------------- a body flush on the floor

    /// <summary>A floor of one quad, two triangles meeting on the diagonal from (−10, −10) to (10, 10), as the course's.</summary>
    static LevelSpace TwoTriangles() => new TestLevel()
        .Quad(new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0)).Build("two-triangles");

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    [InlineData(Gait.Sprint)]
    public void A_body_put_flush_on_a_floor_walks_across_the_floor_s_diagonal_at_every_angle(Gait gait)
    {
        var level = TwoTriangles();
        var wrong = new List<string>();
        for (int deg = 0; deg < 360; deg += 15)
        {
            var (s, c) = DetMath.SinCos(deg * DetMath.DegToRad);
            var way = new Vector2(c, s);
            // Started 3 m back from the diagonal's middle, so the walk crosses it.
            var cross = new Vector2(0.3f, 0.3f);
            var from = cross - way * 3f;
            var body = Body.At(new Vector3(from, 0f), deg * DetMath.DegToRad);
            int ticks = (int)((6f / (gait == Gait.Walk ? 1.3f : gait == Gait.Run ? 3f : 5.5f) + 0.5f) * 120f);
            body = Drive.Run(level, body, ticks, (_, _) => Drive.Toward(way.X, way.Y, gait));
            float went = Vector2.Dot(new Vector2(body.Feet.X, body.Feet.Y) - from, way);
            if (went < 5.5f || MathF.Abs(body.Feet.Z) > 0.01f || body.State != BodyState.Ground)
                wrong.Add($"{deg}°: went {went:0.00} m, feet z {body.Feet.Z:0.000}, {body.State}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
