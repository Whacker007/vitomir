// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The probe's width and its depth square to the face: a top
/// narrower than a top stood on (<see cref="Locomotion.MantleMinDepth"/>, 0.35 m) cannot be stood on whichever way it
/// is met — a beam of 0.2 m met end-on starts nothing and stops the body, met across it is thin and vaulted — and a
/// wall of 0.3 m met at 30°, 45° or 60° is as thin as met square on (along the way it once read 0.42 m at 45° and was
/// stood on along its length), on small levels and on the proving ground (<c>VITOMIR_COURSE_LEVEL</c>; skipped without
/// it). A stool of 0.4 m by 0.4 m is still stood on.
/// </summary>
public sealed class ProbeWidthTests
{
    static readonly Locomotion Human = Drive.Numbers;

    /// <summary>A wall 0.3 m thick and <paramref name="height"/> high along Y at x 0 to 0.3, on a floor.</summary>
    static LevelSpace Wall(float height) =>
        new TestLevel().Floor(-8, -8, 8, 8).Box(new(0, -4, 0), new(0.3f, 4, height)).Build($"wall {height}");

    /// <summary>A beam 0.2 m wide, 6 m long along +X from x 0, its top at 1.0 m and 0.15 m deep, on two posts at its ends (the course's).</summary>
    static LevelSpace Beam() => new TestLevel().Floor(-8, -8, 12, 8)
        .Box(new(0, -0.1f, 0.85f), new(6, 0.1f, 1.0f))
        .Box(new(0, -0.1f, 0), new(0.2f, 0.1f, 0.85f)).Box(new(5.8f, -0.1f, 0), new(6, 0.1f, 0.85f)).Build("beam");

    static Vector2 Way(float degrees)
    {
        var (s, c) = DetMath.SinCos(degrees * DetMath.DegToRad);
        return new Vector2(c, s);
    }

    /// <summary>The probe as a held jump asks it (TryFromAir's reach and height), from <paramref name="back"/> m short of where the way meets the point <paramref name="at"/>.</summary>
    static Obstacle Probe(LevelSpace level, Vector2 at, Vector2 way, float back, float above = 1.45f)
    {
        var feet = new Vector3(at - way * back, 0.002f);
        Assert.True(TraversalProbe.Read(level.Collision, feet, way, Human, Human.Radius + BodyMotor.CloseReach, above, out var ob), "the probe found nothing");
        return ob;
    }

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public float Highest = float.MinValue;
        public Body End;
    }

    /// <summary>Drives a body from <paramref name="from"/> along <paramref name="way"/> at a walk, jump pressed and held 0.5 s once <paramref name="ahead"/> says the face is <paramref name="pressAt"/> m off.</summary>
    static Drove Go(LevelSpace level, Vector3 from, Vector2 way, Func<Vector3, float> ahead, float pressAt, int ticks = 600, Gait gait = Gait.Walk)
    {
        var d = new Drove();
        var body = Drive.Settle(level, from, DetMath.Atan2(way.Y, way.X));
        var move = default(Traversal);
        int pressed = -1;
        d.End = Drive.Moves(level, body, ref move, ticks, (i, b) =>
        {
            var m = Drive.Toward(way.X, way.Y, gait);
            if (pressed < 0 && b.State == BodyState.Ground && ahead(b.Feet) <= pressAt) pressed = i;
            m.Jump = i == pressed;
            m.JumpHeld = pressed >= 0 && i < pressed + 60;
            return m;
        }, (_, b, _, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
        });
        return d;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(45f)]
    [InlineData(60f)]
    public void A_wall_of_0_3_m_met_at_a_slant_is_as_thin_as_met_square_on(float degrees)
    {
        var level = Wall(1.0f);
        var way = Way(degrees);
        var ob = Probe(level, Vector2.Zero, way, 0.6f);
        Assert.True(ob.HasTop);
        Assert.InRange(ob.Depth, 0.25f, 0.3f);                  // square to the face, at 5 cm steps
        Assert.False(ob.Standable);
        Assert.True(ob.Beyond, "ground beyond the wall along the way");
        // The far side cleared by the radius square to it: along a way at a slant that much further on.
        Assert.True(ob.Over.X >= 0.3f + Human.Radius, $"over at {ob.Over}");
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(45f)]
    [InlineData(60f)]
    public void Jump_held_at_a_wall_of_0_3_m_met_at_a_slant_vaults_it_and_never_climbs_onto_it(float degrees)
    {
        var level = Wall(1.0f);
        var way = Way(degrees);
        // Pressed 0.55 m from the face square to it (at 60° the body's side meets the wall 0.6 m short along the way).
        var d = Go(level, new Vector3(-2f * way.X, -2f * way.Y, 0.05f), way, f => -f.X, 0.55f);
        Assert.DoesNotContain(TraversalKind.ClimbOnto, d.Started);
        Assert.Equal(TraversalKind.Vault, d.Started.FirstOrDefault());
        Assert.True(d.End.Feet.X > 0.3f + Human.Radius && d.End.Feet.Z < 0.05f, $"ended at {d.End.Feet}");
    }

    [Fact]
    public void A_beam_of_0_2_m_met_end_on_is_deep_but_too_narrow_to_stand_on_and_jump_held_at_it_starts_nothing()
    {
        var level = Beam();
        var ob = Probe(level, Vector2.Zero, Vector2.UnitX, 0.6f);
        Assert.True(ob.HasTop);
        Assert.Equal(1.0f, ob.Top, 2);
        Assert.True(ob.Depth >= TraversalProbe.DeepEnough - 1e-3f, $"depth {ob.Depth}");
        Assert.InRange(ob.Width, 0.1f, 0.2f + 1e-3f);
        Assert.False(ob.Standable);
        foreach (var gait in new[] { Gait.Walk, Gait.Run })
        {
            var d = Go(level, new(-3f, 0f, 0.05f), Vector2.UnitX, f => -f.X, gait == Gait.Walk ? 0.55f : 0.9f, gait: gait);
            Assert.Empty(d.Started);
            Assert.True(d.End.Feet.X < -0.25f, $"at a {gait} ended at {d.End.Feet}");
            Assert.True(d.Highest < 0.6f, $"at a {gait} rose to {d.Highest:0.00}");    // a plain jump, never onto it
        }
    }

    [Fact]
    public void A_beam_of_0_2_m_met_across_is_thin_and_jump_held_vaults_it()
    {
        var level = Beam();
        var ob = Probe(level, new Vector2(3f, -0.1f), Vector2.UnitY, 0.6f);
        Assert.True(ob.HasTop);
        Assert.InRange(ob.Depth, 0.15f, 0.2f);
        Assert.False(ob.Standable);
        Assert.True(ob.Beyond);
        var d = Go(level, new(3f, -2f, 0.05f), Vector2.UnitY, f => -0.1f - f.Y, 0.55f);
        Assert.Equal(TraversalKind.Vault, d.Started.FirstOrDefault());
        Assert.True(d.End.Feet.Y > 0.1f + Human.Radius && d.End.Feet.Z < 0.05f, $"ended at {d.End.Feet}");
    }

    [Fact]
    public void A_stool_of_0_4_m_by_0_4_m_is_still_wide_enough_to_stand_on_and_a_tap_hops_onto_it()
    {
        var level = new TestLevel().Floor(-8, -8, 8, 8).Box(new(0, -0.2f, 0), new(0.4f, 0.2f, 0.45f)).Build("stool");
        var ob = Probe(level, Vector2.Zero, Vector2.UnitX, 0.6f, 0.7f);
        Assert.True(ob.Standable, $"width {ob.Width:0.000}, depth {ob.Depth:0.000}");
        Assert.InRange(ob.Width, 0.38f, 0.4f + 1e-3f);
        var body = Drive.Settle(level, new(-2f, 0f, 0.05f));
        var move = default(Traversal);
        var started = new List<TraversalKind>();
        int pressed = -1;
        body = Drive.Moves(level, body, ref move, 360, (i, b) =>
        {
            var m = Drive.Toward(1, 0, Gait.Walk);
            if (pressed < 0 && b.Feet.X >= -0.6f) pressed = i;
            m.Jump = m.JumpHeld = i == pressed;
            return m;
        }, (_, _, _, o) => { if (o.MoveStarted != TraversalKind.None) started.Add(o.MoveStarted); });
        Assert.Equal([TraversalKind.HopUp], started);
    }

    // ---------------------------------------------------------------- on the proving ground

    static LevelSpace? Course()
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        Assert.SkipUnless(!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        return LevelSpace.Open(path!);
    }

    static Vector3 StartOf(LevelSpace level, string name)
    {
        Assert.True(level.TryFindStart(name, out int s, out string error), error);
        return level.Starts.ElementAt(s).Position;
    }

    [Fact]
    public void On_the_course_the_not_built_beam_met_end_on_is_not_climbed_onto_at_a_walk_or_a_run()
    {
        var level = Course()!;
        var at = StartOf(level, "not-built-beam");     // the beam's end face at x 3.0, its top 1.0 m
        var ob = Probe(level, new Vector2(3f, at.Y), Vector2.UnitX, 0.6f);
        Assert.False(ob.Standable, $"width {ob.Width:0.000}");
        foreach (var gait in new[] { Gait.Walk, Gait.Run })
        {
            var d = Go(level, at, Vector2.UnitX, f => 3f - f.X, gait == Gait.Walk ? 0.55f : 0.9f, ticks: 900, gait: gait);
            Assert.Empty(d.Started);
            Assert.True(d.End.Feet.X < 2.75f && d.Highest < 0.6f, $"at a {gait}: ended at {d.End.Feet}, rose to {d.Highest:0.00}");
        }
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(45f)]
    [InlineData(60f)]
    public void On_the_course_the_vault_station_s_wall_of_0_3_m_met_at_a_slant_is_thin_and_vaulted(float degrees)
    {
        var level = Course()!;
        var at = StartOf(level, "vault-1.00");          // its wall's face at x −45.0, 0.3 m thick, 1.0 m high, 2 m wide
        var way = Way(degrees);
        var lip = new Vector2(-45f, at.Y);
        var ob = Probe(level, lip, way, 0.6f);
        Assert.InRange(ob.Depth, 0.25f, 0.3f);
        Assert.False(ob.Standable);
        Assert.True(ob.Beyond);
        var from = new Vector3(lip - way * 2f, at.Z);
        var d = Go(level, from, way, f => -45f - f.X, 0.55f);
        Assert.Equal(TraversalKind.Vault, d.Started.FirstOrDefault());
        Assert.True(d.End.Feet.X > -44.7f + Human.Radius && d.End.Feet.Z < 0.05f, $"ended at {d.End.Feet}");
    }
}
