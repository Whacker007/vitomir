// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The pace scale (Celerity's movement; BodyMotor.PaceScale.cs) on levels built in memory and on the proving ground:
/// absent, the motor is as before; unbound, each gait's pace is max(own, the human's × f) — a human body's sprint the human's
/// times f, an elder's never its own times f —, acceleration and braking likewise, the crouch and the charge keeping their caps;
/// in the masquerade's mode the factor is ignored; a paced sprint unbound in a public place is a risk, in a free place
/// none; and at 6× and 11× a human's sprint a body meets a wall and a person and never passes through.
/// </summary>
public sealed class PaceScaleTests
{
    static readonly Locomotion Human = Locomotion.Sandbox;

    /// <summary>An elder with Celerity and Potence baked in: a 9 m/s sprint, a 4.5 run, quick to speed up (the masquerade tests' elder).</summary>
    static readonly Locomotion Elder = Human with
    {
        RunSpeed = 4.5f, SprintSpeed = 9f, AccelerationCapacity = 60f, BrakingDeceleration = 60f,
        JumpImpulseBudget = Locomotion.ImpulseFor(3f, 9.81f), AirControl = 0.5f, FallTolerance = 17f,
    };

    sealed record Rig(World W, EntityId Player, MovementProfiles Profiles, int HumanIndex, int ElderIndex);

    static LevelSpace Floor(LevelPlaces? places = null, Action<TestLevel>? more = null)
    {
        var t = new TestLevel().Floor(-20, -20, 420, 20, cell: 4f);
        more?.Invoke(t);
        return new LevelSpace("floor", t.Build("floor").Collision) { Places = places ?? LevelPlaces.None };
    }

    static Rig Make(LevelSpace level, bool elder = false, bool contract = true, Vector3? at = null)
    {
        var b = new WorldBuilder(3);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        if (contract) PowerContract.Register(b);
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        int h = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int e = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/elder"), "an elder", Elder, true));
        var p = MovementServices.SpawnControlled(w, at ?? new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        profiles.Give(w, p, elder ? e : h);
        return new Rig(w, p, profiles, h, e);
    }

    /// <summary>The body unbound (as the key or the catch letting go makes it): its own numbers.</summary>
    static void Unbind(Rig r)
    {
        ref var st = ref r.W.Get<MasqueradeState>(r.Player);
        st.Mode = MasqueradeMode.Unbound;
        r.W.Get<Locomotion>(r.Player) = r.Profiles.Numbers(st.Profile, MasqueradeMode.Unbound);
    }

    static readonly ActionFrame Forward = new(new Vector2(0, 1), default, Buttons.None, default);
    static readonly ActionFrame Sprint = Forward with { Held = Buttons.Sprint };

    static float Speed(Rig r) { var v = r.W.Get<Body>(r.Player).Velocity; return new Vector2(v.X, v.Y).Length(); }

    static void Run(Rig r, int ticks, ActionFrame frame, List<MasqueradeRisk>? risks = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(r.W, frame);
            risks?.AddRange(r.W.Events<MasqueradeRisk>().ThisTick);
        }
    }

    static float TopSpeed(Rig r, ActionFrame frame, int ticks = 2 * World.TicksPerSecond)
    {
        float top = 0;
        for (int i = 0; i < ticks; i++) { Run(r, 1, frame); top = MathF.Max(top, Speed(r)); }
        return top;
    }

    [Fact]
    public void The_numbers_under_a_pace_scale_are_the_larger_of_the_body_s_own_and_the_human_s_times_the_factor()
    {
        var p = BodyMotor.Paced(Elder, Human, 3f);
        Assert.Equal((3.9f, 9f, 16.5f), (MathF.Round(p.WalkSpeed, 4), p.RunSpeed, p.SprintSpeed));
        Assert.Equal(16.5f, p.SpeedOf(Gait.Supernatural));                                // the human's sprint × 3
        Assert.Equal((60f, 90f), (p.AccelerationCapacity, p.BrakingDeceleration));        // max(60, 20 × 3), max(60, 30 × 3)
        Assert.Equal(Elder.CrouchSpeed, p.CrouchSpeed);                                    // the crouch keeps its cap
        Assert.Equal(Elder.JumpImpulseBudget, p.JumpImpulseBudget);                        // no jump from it
        var slow = BodyMotor.Paced(Elder, Human, 1.5f);
        Assert.Equal(9f, slow.SprintSpeed);                                                // max(9, 8.25): never 9 × 1.5
        Assert.Equal(Elder, BodyMotor.Paced(Elder, Human, 1f));
        Assert.Equal(Elder, BodyMotor.Paced(Elder, Human, 0f));
    }

    [Fact]
    public void Without_a_pace_scale_the_motor_is_as_before_step_for_step()
    {
        var with = Make(Floor());
        var without = Make(Floor(), contract: false);
        Unbind(with); Unbind(without);
        for (int i = 0; i < 600; i++)
        {
            var f = i % 200 < 120 ? Sprint : i % 200 < 150 ? Forward with { Held = Buttons.Jump } : Forward;
            Run(with, 1, f); Run(without, 1, f);
            Assert.Equal(without.W.Get<Body>(without.Player), with.W.Get<Body>(with.Player));
        }
        with.W.Set(with.Player, new PaceScale { Factor = 1f });                            // a factor of 1 is no scale
        Run(with, 60, Sprint); Run(without, 60, Sprint);
        Assert.Equal(without.W.Get<Body>(without.Player), with.W.Get<Body>(with.Player));
    }

    [Fact]
    public void A_human_body_unbound_under_a_pace_scale_runs_at_the_human_s_times_the_factor()
    {
        var r = Make(Floor(), at: new(-15, 0, 0.01f));
        Unbind(r);
        r.W.Set(r.Player, new PaceScale { Factor = 3f });
        Run(r, 10, ActionFrame.Idle);
        Assert.Equal(16.5f, TopSpeed(r, Sprint), 2);
        Run(r, 60, ActionFrame.Idle);
        Assert.Equal(9f, TopSpeed(r, Forward), 2);                                        // always run: the run × 3
    }

    [Fact]
    public void An_elder_under_a_pace_scale_runs_at_the_larger_of_its_own_and_the_human_s_times_the_factor_never_its_own_times_it()
    {
        var r = Make(Floor(), elder: true, at: new(-15, 0, 0.01f));
        Unbind(r);
        r.W.Set(r.Player, new PaceScale { Factor = 1.5f });
        Run(r, 10, ActionFrame.Idle);
        Assert.Equal(9f, TopSpeed(r, Sprint), 2);                                         // max(9, 5.5 × 1.5 = 8.25), not 13.5
        r.W.Get<PaceScale>(r.Player).Factor = 3f;
        Assert.Equal(16.5f, TopSpeed(r, Sprint), 2);                                      // max(9, 16.5), not 27
    }

    [Fact]
    public void In_the_masquerade_s_mode_the_factor_is_ignored_and_the_crouch_and_the_charge_keep_their_caps()
    {
        var r = Make(Floor(), elder: true, at: new(-15, 0, 0.01f));
        Assert.Equal(MasqueradeMode.Masquerade, r.W.Get<MasqueradeState>(r.Player).Mode);
        r.W.Set(r.Player, new PaceScale { Factor = 4f });
        Run(r, 10, ActionFrame.Idle);
        Assert.Equal(Human.SprintSpeed, TopSpeed(r, Sprint), 2);                          // a human's, whatever the scale

        var u = Make(Floor(), elder: true, at: new(-15, 0, 0.01f));
        Unbind(u);
        u.W.Set(u.Player, new PaceScale { Factor = 4f });
        Run(u, 10, ActionFrame.Idle);
        Run(u, 30, Forward with { Held = Buttons.Crouch });
        Assert.NotEqual(0, u.W.Get<Body>(u.Player).Crouched);
        Assert.InRange(TopSpeed(u, Forward with { Held = Buttons.Crouch }, 60), 0f, Elder.CrouchSpeed + 1e-3f);
        Run(u, 60, ActionFrame.Idle);
        var c = Make(Floor(), elder: true, at: new(-15, 0, 0.01f));
        Unbind(c);
        c.W.Set(c.Player, new PaceScale { Factor = 4f });
        Run(c, 10, ActionFrame.Idle);
        Assert.True(c.W.Get<Locomotion>(c.Player).CanCharge);
        Run(c, 60, Sprint);                                                               // at speed first (12 m/s: the run × 4)
        var held = new List<float>();
        for (int i = 0; i < 100; i++)
        {
            Run(c, 1, Forward with { Held = Buttons.Jump });
            if (c.W.Get<Body>(c.Player).Charging == 2) held.Add(Speed(c));                // a charge, past the tap's hold
        }
        Assert.True(held.Count > 40);
        Assert.All(held.Skip(15), v => Assert.InRange(v, 0.1f, Elder.WalkSpeed + 1e-3f)); // braked (at 120 m/s²: 11 ticks) to its own walk, not 5.2
    }

    [Fact]
    public void A_paced_sprint_unbound_is_a_risk_in_a_public_place_and_none_in_a_free_place_or_in_the_masquerade()
    {
        var pub = Make(Floor(), at: new(-15, 0, 0.01f));                                  // a human body: only the scale makes it fast
        Unbind(pub);
        pub.W.Set(pub.Player, new PaceScale { Factor = 3f });
        var risks = new List<MasqueradeRisk>();
        Run(pub, 10, ActionFrame.Idle);
        Run(pub, 3 * World.TicksPerSecond, Sprint, risks);
        var risk = Assert.Single(risks);
        Assert.Equal(MasqueradeAct.Speed, risk.Act);
        Assert.InRange(risk.Measured, 16.4f, 16.6f);
        Assert.Equal(Human.SprintSpeed, risk.HumanLimit);

        var free = Make(Floor(LevelPlaces.Of([], PlaceKind.Free)), at: new(-15, 0, 0.01f));
        Unbind(free);
        free.W.Set(free.Player, new PaceScale { Factor = 3f });
        risks.Clear();
        Run(free, 3 * World.TicksPerSecond, Sprint, risks);
        Assert.Empty(risks);
        Assert.Equal(16.5f, Speed(free), 2);

        var held = Make(Floor(), at: new(-15, 0, 0.01f));
        held.W.Set(held.Player, new PaceScale { Factor = 3f });
        risks.Clear();
        Run(held, 3 * World.TicksPerSecond, Sprint, risks);
        Assert.Empty(risks);
    }

    /// <summary>The least distance from <paramref name="c"/> to the segment <paramref name="a"/>–<paramref name="b"/> (horizontal).</summary>
    static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(c - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(a + ab * t, c);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(11f)]
    public void At_six_and_eleven_times_a_human_s_sprint_a_body_meets_a_wall_and_a_person_and_never_passes_through(float factor)
    {
        // A wall across the way at x 60, 2 m high; a person standing at x 30 on the line.
        var level = Floor(LevelPlaces.Of([], PlaceKind.Free), t => t.Box(new(60, -20, 0), new(61, 20, 2f)));
        var r = Make(level, at: new(-15, 0, 0.01f));
        Unbind(r);
        r.W.Set(r.Player, new PaceScale { Factor = factor });
        var other = MovementServices.SpawnBody(r.W, new(30, 0, 0.01f), 0f, Locomotion.Sandbox);
        r.W.Set(other, new MoveIntent());
        Run(r, 10, ActionFrame.Idle);
        float top = 0;
        var before = r.W.Get<Body>(r.Player).Feet;
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            Run(r, 1, Sprint);
            var now = r.W.Get<Body>(r.Player).Feet;
            top = MathF.Max(top, Speed(r));
            Assert.True(now.X < 60f - Human.Radius + 0.02f, $"tick {i}: through the wall at x {now.X}");
            var them = r.W.Get<Body>(other).Feet;
            Assert.True(SegmentDistance(new(before.X, before.Y), new(now.X, now.Y), new(them.X, them.Y)) >= 2 * Human.Radius - 0.02f,
                $"tick {i}: through the person ({before} to {now}, they at {them})");
            before = now;
        }
        Assert.InRange(top, Human.SprintSpeed * factor * 0.9f, Human.SprintSpeed * factor + 0.01f);   // it got up to speed on the way
    }

    static readonly Lazy<LevelSpace?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    [Theory]
    [InlineData(6f)]
    [InlineData(11f)]
    public void On_the_proving_ground_a_paced_sprint_from_the_free_yard_never_passes_through_the_level(float factor)
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var level = Course.Value!;
        float z = level.Collision.Floor(40, 3, 5f, 10f) ?? 0f;
        int checkedWays = 0;
        for (int way = 0; way < 8; way++)
        {
            var (s, c) = DetMath.SinCos(way * DetMath.Pi / 4f);
            var r = Make(level, at: new(20, -40, z + 0.01f));                            // the player out of the way
            var e = MovementServices.SpawnBody(r.W, new(40, 3, z + 0.01f), 0f, Locomotion.Sandbox);
            r.Profiles.Give(r.W, e, r.HumanIndex);
            ref var st = ref r.W.Get<MasqueradeState>(e);
            st.Mode = MasqueradeMode.Unbound;
            r.W.Set(e, new PaceScale { Factor = factor });
            Run(r, 10, ActionFrame.Idle);
            var before = r.W.Get<Body>(e).Feet;
            float top = 0;
            for (int i = 0; i < 3 * World.TicksPerSecond; i++)
            {
                r.W.Get<MoveIntent>(e) = new MoveIntent { Direction = new Vector2(c, s), Gait = Gait.Sprint };
                Run(r, 1, ActionFrame.Idle);
                var now = r.W.Get<Body>(e).Feet;
                var v = r.W.Get<Body>(e).Velocity;
                top = MathF.Max(top, new Vector2(v.X, v.Y).Length());
                var step = now - before;
                float len = new Vector2(step.X, step.Y).Length();
                if (len > 1e-4f)
                {
                    // The chest's path this tick crosses nothing solid.
                    var chest = before + new Vector3(0, 0, 1.0f);
                    var dir = Vector3.Normalize(new Vector3(step.X, step.Y, 0));
                    Assert.True(level.Collision.Ray(chest, dir, len) is null, $"way {way}, tick {i}: through the level between {before} and {now}");
                }
                before = now;
            }
            Assert.True(top > Human.SprintSpeed * 2f, $"way {way}: only {top} m/s");
            checkedWays++;
        }
        Assert.Equal(8, checkedWays);
    }
}
