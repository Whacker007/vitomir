// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Stairs as 1.1 builds them, for the body's motor (steps of 0.35 m up and
/// down in stride, ground stood on to 45°). 1.1's flights rise 16 units (0.222 m) a step on a 32-unit pitch (0.444 m),
/// and each nosing is cut back by a 45° chamfer 4 units wide, so a riser is 12 units of wall under 4 of
/// chamfer; where a flight flares the chamfer is a little steeper than 45°, and some nosings are cut back 8
/// units. A body walks, runs and sprints down and up such flights, and others with steps up to 0.35 m, without
/// leaving the ground; it climbs a flight met at an angle instead of sliding along its first riser; a drop of
/// more than a step is still a fall; and the same drive gives the same body every time.
/// </summary>
public sealed class StairTests
{
    /// <summary>One of 1.1's units, in metres.</summary>
    const float U = 1f / 72f;

    /// <summary>The flights tried: a step's rise and pitch and the chamfer cut back from its nosing (across and down), in metres.</summary>
    internal static (float Rise, float Pitch, float CutAcross, float CutDown) Kind(string kind) => kind switch
    {
        "1.1" => (16 * U, 32 * U, 4 * U, 4 * U),
        "flared" => (16 * U, 32 * U, 3.6f * U, 4 * U),     // a chamfer of 48°, too steep to stand on
        "wide-cut" => (24 * U, 36 * U, 8 * U, 8 * U),
        "sharp-0.17" => (0.17f, 0.3f, 0f, 0f),
        "sharp-0.33" => (24 * U, 0.6f, 0f, 0f),        // P1_OTDY's doorsteps: taller than the body's radius
        "sharp-0.35" => (0.35f, 0.5f, 0f, 0f),
        "cut-0.35" => (0.35f, 0.5f, 4 * U, 4 * U),
        _ => throw new ArgumentException(kind),
    };

    const int Steps = 6;

    /// <summary>
    /// A flight of <see cref="Steps"/> steps going down along +x from a landing (x −6 to 0) to a floor at 0,
    /// between y −8 and 8, each step a solid block whose nosing is cut back by the chamfer.
    /// </summary>
    internal static LevelSpace Flight(string kind, out float top, out float foot, Action<TestLevel, float, float>? more = null)
    {
        var (rise, pitch, ca, cd) = Kind(kind);
        top = Steps * rise;
        foot = (Steps - 1) * pitch;
        var build = new TestLevel().Floor(-10, -10, 12, 10);
        for (int k = 0; k < Steps; k++)
        {
            float xa = k == 0 ? -6f : (k - 1) * pitch, xb = k * pitch, z = top - k * rise;
            Vector2[] profile = ca > 0f
                ? [new(xa, z), new(xb - ca, z), new(xb, z - cd), new(xb, 0), new(xa, 0)]
                : [new(xa, z), new(xb, z), new(xb, 0), new(xa, 0)];
            Prism(build, profile, -8, 8);
        }
        more?.Invoke(build, top, foot);
        return build.Build(kind);
    }

    /// <summary>A solid of a convex profile (x across, z up, going clockwise seen from −y) between y0 and y1, its faces outwards.</summary>
    static void Prism(TestLevel build, Vector2[] profile, float y0, float y1)
    {
        Vector3 At(Vector2 p, float y) => new(p.X, y, p.Y);
        for (int i = 0; i < profile.Length; i++)
        {
            var a = profile[i]; var b = profile[(i + 1) % profile.Length];
            build.Quad(At(a, y0), At(b, y0), At(b, y1), At(a, y1));
        }
        for (int i = 1; i + 1 < profile.Length; i++)
        {
            build.Triangle(At(profile[0], y0), At(profile[i + 1], y0), At(profile[i], y0));
            build.Triangle(At(profile[0], y1), At(profile[i], y1), At(profile[i + 1], y1));
        }
    }

    /// <summary>Drives a body along <paramref name="way"/> at <paramref name="gait"/> until <paramref name="done"/>, counting its ticks in the air; false when it stalls on the way (no progress for a second).</summary>
    static (Body Body, int Air, bool Stalled) Go(LevelSpace level, Body body, Vector2 way, Gait gait, Func<Body, bool> done, List<Vector3>? track = null)
    {
        int air = 0, still = 0;
        for (int i = 0; i < 120 * 30 && !done(body); i++)
        {
            var before = body.Feet;
            body = Drive.Run(level, body, 1, (_, _) => Drive.Toward(way.X, way.Y, gait));
            track?.Add(body.Feet);
            if (body.State == BodyState.Air) air++;
            still = Vector2.Distance(new(before.X, before.Y), new(body.Feet.X, body.Feet.Y)) < 1e-4f ? still + 1 : 0;
            if (still > 120) return (body, air, true);
        }
        return (body, air, !done(body));
    }

    [Theory]
    [InlineData("1.1", Gait.Walk)]
    [InlineData("1.1", Gait.Run)]
    [InlineData("1.1", Gait.Sprint)]
    [InlineData("flared", Gait.Walk)]
    [InlineData("flared", Gait.Run)]
    [InlineData("flared", Gait.Sprint)]
    [InlineData("wide-cut", Gait.Run)]
    [InlineData("wide-cut", Gait.Sprint)]
    [InlineData("sharp-0.17", Gait.Sprint)]
    [InlineData("sharp-0.35", Gait.Walk)]
    [InlineData("sharp-0.35", Gait.Sprint)]
    [InlineData("cut-0.35", Gait.Run)]
    [InlineData("cut-0.35", Gait.Sprint)]
    public void A_body_goes_down_a_flight_without_leaving_the_ground(string kind, Gait gait)
    {
        var level = Flight(kind, out float top, out float foot);
        var body = Drive.Settle(level, new Vector3(-3, 0, top + 0.01f));
        Assert.Equal(BodyState.Ground, body.State);
        var (end, air, stalled) = Go(level, body, new Vector2(1, 0), gait, b => b.Feet.X > foot + 2f);
        Assert.False(stalled, $"{kind} at a {gait}: stalled at {end.Feet}");
        Assert.True(air == 0, $"{kind} at a {gait}: {air} ticks in the air, ended at {end.Feet}");
        Assert.InRange(end.Feet.Z, -0.001f, 0.005f);
        Assert.True(Drive.Depth(level, end) <= 0.001f);
    }

    [Theory]
    [InlineData("1.1", Gait.Walk)]
    [InlineData("1.1", Gait.Run)]
    [InlineData("1.1", Gait.Sprint)]
    [InlineData("flared", Gait.Walk)]
    [InlineData("flared", Gait.Sprint)]
    [InlineData("wide-cut", Gait.Walk)]
    [InlineData("wide-cut", Gait.Sprint)]
    [InlineData("sharp-0.35", Gait.Run)]
    [InlineData("cut-0.35", Gait.Walk)]
    [InlineData("cut-0.35", Gait.Sprint)]
    public void A_body_goes_up_a_flight_without_leaving_the_ground(string kind, Gait gait)
    {
        var level = Flight(kind, out float top, out float foot);
        var body = Drive.Settle(level, new Vector3(foot + 3f, 0, 0.01f));
        var (end, air, stalled) = Go(level, body, new Vector2(-1, 0), gait, b => b.Feet.X < -2f);
        Assert.False(stalled, $"{kind} at a {gait}: stalled at {end.Feet}");
        Assert.True(air == 0, $"{kind} at a {gait}: {air} ticks in the air, ended at {end.Feet}");
        Assert.InRange(end.Feet.Z, top - 0.001f, top + 0.005f);
        Assert.True(Drive.Depth(level, end) <= 0.001f);
    }

    [Theory]
    [InlineData("1.1", Gait.Walk, 15f)]
    [InlineData("flared", Gait.Walk, 15f)]
    [InlineData("flared", Gait.Run, 15f)]
    [InlineData("wide-cut", Gait.Walk, 15f)]
    [InlineData("sharp-0.33", Gait.Walk, 35f)]
    [InlineData("sharp-0.33", Gait.Run, 35f)]
    public void A_body_meeting_a_flight_at_an_angle_climbs_it_instead_of_sliding_along_its_first_riser(string kind, Gait gait, float degrees)
    {
        // P1_OTDY's stuck at the foot of the west flight: a route across the flare meets the bottom riser at
        // 15° and the body, unable to stand on the steep chamfer, slid along the riser at a tenth of its pace.
        // A riser taller than the body's radius takes its speed into the step every tick, so the step is tried
        // with a stride into it too when the wished way heads into it at 30° or more (a doorstep of 24 units
        // held routes on P1_OTDY so).
        var level = Flight(kind, out float top, out float foot);
        var body = Drive.Settle(level, new Vector3(foot + 0.6f, -7f, 0.01f));
        var way = new Vector2(-DetMath.Sin(degrees * DetMath.DegToRad), DetMath.Cos(degrees * DetMath.DegToRad));
        var (end, air, _) = Go(level, body, way, gait, b => b.Feet.Y > 7f || b.Feet.X < -4f);
        Assert.True(end.Feet.Z > 2.5f * Kind(kind).Rise, $"{kind} at a {gait}: still at {end.Feet}");
        Assert.True(air == 0, $"{kind} at a {gait}: {air} ticks in the air, ended at {end.Feet}");
        Assert.True(Drive.Depth(level, end) <= 0.001f);
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Sprint)]
    public void A_drop_of_more_than_a_step_is_still_a_fall(Gait gait)
    {
        // Steps of 0.45 m, past the stride of 0.35: the body leaves the ground at each and lands with the drop.
        var build = new TestLevel().Floor(-10, -10, 12, 10).Box(new(-6, -3, 0), new(0, 3, 0.9f)).Box(new(0, -3, 0), new(3, 3, 0.45f));
        var level = build.Build();
        var body = Drive.Settle(level, new Vector3(-3, 0, 0.91f));
        float fall = 0f;
        int air = 0;
        Drive.Run(level, body, 600, (_, b) => b.Feet.X > 6f ? new MoveIntent() : Drive.Toward(1, 0, gait),
                  (_, b, o) => { if (b.State == BodyState.Air) air++; if (o.Landed) fall = MathF.Max(fall, o.FallHeight); });
        Assert.True(air > 0, "never left the ground");
        Assert.True(fall >= 0.44f, $"its biggest fall was {fall} m");
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Sprint)]
    public void A_body_brushing_the_corner_of_a_block_a_step_high_is_not_lifted_onto_it(Gait gait)
    {
        // The East Gate's beam (walk35): a body running past a block a step high (0.34 m), its capsule catching the
        // block's corner by a millimetre, only grazes its top at the rim of its disc, which is no footing.
        var level = new TestLevel().Floor(-10, -10, 10, 10).Box(new(0, 0, 0), new(5, 1, 0.34f)).Build();
        var body = Drive.Settle(level, new Vector3(-1.5f, -0.299f, 0.01f));
        float highest = 0f;
        Drive.Run(level, body, 360, (_, b) => b.Feet.X > 6f ? new MoveIntent() : Drive.Toward(1, 0, gait),
                  (_, b, _) => highest = MathF.Max(highest, b.Feet.Z));
        Assert.True(highest < 0.01f, $"lifted to {highest} m");
    }

    [Fact]
    public void A_body_stands_still_on_a_slope_of_exactly_45_degrees()
    {
        // 1.1's ramps and chamfers rise as far as they run; such a face is ground whichever way its normal rounds.
        var build = new TestLevel().Floor(-10, -10, 10, 10);
        Prism(build, [new(0, 0), new(72 * U, 72 * U), new(72 * U, 0)], -3, 3);
        Prism(build, [new(72 * U, 72 * U), new(4, 72 * U), new(4, 0), new(72 * U, 0)], -3, 3);
        var level = build.Build();
        var body = Drive.Settle(level, new Vector3(0.5f, 0, 0.6f));
        var at = body.Feet;
        int air = 0;
        body = Drive.Run(level, body, 240, (_, _) => new MoveIntent(), (_, b, _) => { if (b.State == BodyState.Air) air++; });
        Assert.Equal(0, air);
        Assert.True(Vector3.Distance(at, body.Feet) < 0.01f, $"slid from {at} to {body.Feet}");
    }

    [Fact]
    public void The_same_drive_down_and_up_a_flight_gives_the_same_body_at_every_tick()
    {
        var level = Flight("1.1", out float top, out float foot);
        List<Vector3> Once()
        {
            var track = new List<Vector3>();
            var body = Drive.Settle(level, new Vector3(-3, 0, top + 0.01f));
            body = Go(level, body, new Vector2(1, 0), Gait.Sprint, b => b.Feet.X > foot + 2f, track).Body;
            Go(level, body, new Vector2(-1, 0.2f), Gait.Run, b => b.Feet.X < -2f, track);
            return track;
        }
        var first = Once();
        Assert.Equal(first, Once());
        Assert.True(first.Count > 200);
    }
}
