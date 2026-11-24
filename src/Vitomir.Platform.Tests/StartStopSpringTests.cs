// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Starts and stops on a critically damped spring (BodyMotor.Spring.cs). The spring follows Daniel
/// Holden's exact form tick by tick and its closed form on the ground; a half-life of 0 is the earlier build's ramp to the bit; the spring rests in
/// the air; a world without <see cref="StartStopSprings"/> (or without a player on it) has no store of it, and in a world with it only
/// the player carries one, its half-life its profile's (held to the human's in the masquerade, shortened by a pace
/// scale); setting off from rest is no stop, a
/// wall is; and the numbers to judge it by (the times to 90 % of each pace, the stops, a right-angle turn and a reversal at a run), the
/// spring's beside the ramp's.
/// </summary>
public sealed class StartStopSpringTests(ITestOutputHelper output)
{
    const float Dt = World.TickSeconds;
    static readonly Locomotion Human = Locomotion.Sandbox;

    static LevelSpace Flat() => new TestLevel().Floor(-50, -50, 150, 50, cell: 10f).Build("flat");

    /// <summary>The closed form of a critically damped spring from rest toward <paramref name="goal"/>: goal·(1 − e^(−yt)(1 + yt)).</summary>
    static float FromRest(float goal, float halfLife, float t)
    {
        float y = BodyMotor.SpringDamping(halfLife);
        return goal * (1f - MathF.Exp(-y * t) * (1f + y * t));
    }

    [Fact]
    public void The_spring_follows_its_formula_tick_by_tick()
    {
        // Holden's form, step by step, from rest and from a speed with an acceleration, against its closed form
        // v(t) = goal + e^(−yt)(j0 + j1 t), a(t) = e^(−yt)(a0 − j1 y t).
        foreach (float h in new[] { 0.1f, 0.15f, 0.2f, 0.25f })
            foreach (var (v0, a0, goal) in new[] { (Vector2.Zero, Vector2.Zero, new Vector2(3f, 0f)), (new Vector2(3f, 0f), Vector2.Zero, Vector2.Zero),
                                                   (new Vector2(3f, 0f), new Vector2(0f, 5f), new Vector2(0f, 5.5f)), (new Vector2(-1f, 2f), new Vector2(4f, -4f), new Vector2(1.3f, 0f)) })
            {
                double y = 2.0 * Math.Log(2.0) / h;
                var j0 = v0 - goal;
                var j1 = a0 + j0 * (float)y;
                var v = v0;
                var a = a0;
                for (int k = 1; k <= 2 * World.TicksPerSecond; k++)
                {
                    v = BodyMotor.Spring(v, ref a, goal, h, Dt);
                    double t = k * (double)Dt, e = Math.Exp(-y * t);
                    var want = new Vector2((float)(goal.X + e * (j0.X + j1.X * t)), (float)(goal.Y + e * (j0.Y + j1.Y * t)));
                    var wantA = new Vector2((float)(e * (a0.X - j1.X * y * t)), (float)(e * (a0.Y - j1.Y * y * t)));
                    Assert.True(Vector2.Distance(v, want) < 2e-4f, $"h {h}, tick {k}: v {v} against {want}");
                    Assert.True(Vector2.Distance(a, wantA) < 2e-3f, $"h {h}, tick {k}: a {a} against {wantA}");
                }
            }
    }

    [Fact]
    public void On_the_ground_a_body_on_the_spring_sets_off_and_stops_on_the_curve()
    {
        var level = Flat();
        var body = Drive.Settle(level, new Vector3(0, 0, 0.01f));
        var spring = new StartStopSpring { HalfLife = 0.2f };
        var run = Drive.Toward(1, 0, Gait.Run);
        float x0 = body.Feet.X;
        for (int k = 1; k <= World.TicksPerSecond; k++)
        {
            BodyMotor.Step(ref body, ref spring, Human, run, level.Collision, [], 0, k);
            Assert.Equal(BodyState.Ground, body.State);
            Assert.InRange(body.Velocity.X, FromRest(3f, 0.2f, k * Dt) - 1e-3f, FromRest(3f, 0.2f, k * Dt) + 1e-3f);
        }
        // Let go after a second and a half at the run: it stops by the spring, h/ln2 a metre a second (0.87 m from 3 m/s).
        for (int k = 0; k < World.TicksPerSecond / 2; k++) BodyMotor.Step(ref body, ref spring, Human, run, level.Collision, [], 0, k);
        Assert.InRange(body.Velocity.X, 2.998f, 3.001f);   // 1.5 s: 1 − e^(−10.4)(1 + 10.4) of the run
        float estimate = BodyMotor.SpringStoppingAlong(spring, body.Velocity.X, Vector2.UnitX);
        float from = body.Feet.X;
        for (int k = 0; k < 3 * World.TicksPerSecond; k++)
            BodyMotor.Step(ref body, ref spring, Human, new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Run }, level.Collision, [], 0, k);
        float slid = body.Feet.X - from;
        Assert.InRange(slid, 0.84f, 0.87f);
        Assert.InRange(estimate - slid, -0.01f, 0.04f);    // the brains' estimate: the spring's integral and a tick's latency
        Assert.True(MathF.Abs(body.Velocity.X) < 1e-3f);
        Assert.True(x0 < from);
    }

    [Fact]
    public void A_half_life_of_zero_is_the_ramp_to_the_bit()
    {
        // The same drive (run, sprint, turn, stop, jump, walk off a step) through the earlier build's overload and through the spring's with a
        // half-life of 0: every tick's body the same bytes.
        var level = new TestLevel().Floor(-20, -20, 40, 20).Box(new(10, -5, 0), new(14, 5, 0.3f)).Build("steps");
        var a = Drive.Settle(level, new Vector3(0, 0, 0.01f));
        var b = a;
        var moveA = default(Traversal);
        var moveB = default(Traversal);
        var ramp = default(StartStopSpring);
        for (int i = 0; i < 6 * World.TicksPerSecond; i++)
        {
            var intent = Intent(i);
            var oa = BodyMotor.Step(ref a, ref moveA, Human, intent, level.Collision, [], 0, i);
            var ob = BodyMotor.Step(ref b, ref moveB, ref ramp, Human, intent, level.Collision, [], 0, i);
            Assert.True(MemoryMarshal.AsBytes(new ReadOnlySpan<Body>(ref a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<Body>(ref b))), $"tick {i}");
            Assert.Equal(oa.Landed, ob.Landed);
            Assert.Equal(0f, ob.SpringAsked);
        }
        Assert.Equal(Vector2.Zero, ramp.Acceleration);

        static MoveIntent Intent(int i) => i switch
        {
            < 120 => Drive.Toward(1, 0, Gait.Run),
            < 240 => Drive.Toward(1, 0.2f, Gait.Sprint),
            < 300 => Drive.Toward(0, 1, Gait.Run),
            < 360 => new MoveIntent { Facing = 1f, Faces = true },
            < 380 => Drive.Toward(1, 0, Gait.Run, jump: i == 362),
            < 600 => Drive.Toward(1, -0.3f, Gait.Walk),
            _ => new MoveIntent { Facing = 0f, Faces = true },
        };
    }

    [Fact]
    public void The_spring_rests_in_the_air_and_takes_up_the_landing_s_speed()
    {
        var level = Flat();
        var body = Drive.Settle(level, new Vector3(0, 0, 0.01f));
        var spring = new StartStopSpring { HalfLife = 0.2f };
        bool flew = false, landed = false;
        for (int i = 0; i < 2 * World.TicksPerSecond; i++)
        {
            var o = BodyMotor.Step(ref body, ref spring, Human, Drive.Toward(1, 0, Gait.Run, jump: i == 30), level.Collision, [], 0, i);
            if (body.State == BodyState.Air) { flew = true; Assert.Equal(Vector2.Zero, spring.Acceleration); }
            landed |= o.Landed;
        }
        Assert.True(flew && landed);
        Assert.InRange(body.Velocity.X, 2.99f, 3.01f);
    }

    // ---- landings ("a, with the qualifier that it should keep directional motion if the player is still moving.
    // But no sliding, there I agree.") ----

    /// <summary>
    /// A body on the spring (0.2 s; 0 the ramp) dropped 0.3 m onto a ramp of <paramref name="degrees"/> asking nothing: how far it goes
    /// across from where it lands to where it is 3 s after the drop (m), the same from where it is half a second after the drop (as
    /// <c>BodyMotorTests</c>' slope test measures), and its state then.
    /// </summary>
    static (float Slid, float SlidAfterHalf, BodyState State) SlopeLanding(float degrees, float halfLife, bool slide)
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Ramp(2, 6, -5, 5, degrees).Build();
        float up = 3f * DetMath.Tan(degrees * DetMath.DegToRad);
        var body = Body.At(new(5, 0, up + 0.3f), 0f);
        var spring = new StartStopSpring { HalfLife = halfLife };
        var still = new MoveIntent();
        Vector3? landed = null;
        var half = Vector3.Zero;
        for (int k = 0; k < 3 * World.TicksPerSecond; k++)
        {
            var o = BodyMotor.Step(ref body, ref spring, Human, still, level.Collision, [], 0, k, landingSlide: slide);
            if (o.Landed && landed is null) landed = body.Feet;
            if (k == World.TicksPerSecond / 2 - 1) half = body.Feet;
        }
        Assert.NotNull(landed);
        static float Across(Vector3 a, Vector3 b) => new Vector2(b.X - a.X, b.Y - a.Y).Length();
        return (Across(landed!.Value, body.Feet), Across(half, body.Feet), body.State);
    }

    [Theory]
    [InlineData(39f)]
    [InlineData(40f)]
    [InlineData(42f)]
    public void A_landing_on_a_slope_does_not_slide_on_the_spring_and_with_landing_slide_it_slides(float degrees)
    {
        var stops = SlopeLanding(degrees, 0.2f, slide: false);
        var slides = SlopeLanding(degrees, 0.2f, slide: true);
        var ramp = SlopeLanding(degrees, 0f, slide: false);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {degrees}° | {stops.Slid * 1000:0.0} / {stops.SlidAfterHalf * 1000:0.0} mm | {slides.Slid * 1000:0.0} / {slides.SlidAfterHalf * 1000:0.0} mm | {ramp.Slid * 1000:0.0} / {ramp.SlidAfterHalf * 1000:0.0} mm |"));
        Assert.Equal(BodyState.Ground, stops.State);
        Assert.True(stops.Slid < 0.001f, $"slid {stops.Slid} m after landing");
        // It slides on, about 0.28 m from the landing (0.07 m of it after half a second; the earlier build's note said 0.19 m).
        Assert.True(slides.Slid > 0.2f && slides.SlidAfterHalf > 0.05f, $"the landing slide's landing slid only {slides.Slid} m ({slides.SlidAfterHalf} m from 0.5 s)");
        // The ramp brakes a landing over a tick or two (about 12 mm from the landing), and is still after (under 5 mm from 0.5 s).
        Assert.True(ramp.Slid < 0.02f && ramp.SlidAfterHalf < 0.005f);
    }

    /// <summary>
    /// A body on the spring (0.2 s) at the pace of <paramref name="before"/> on the flat for two seconds, jumping, and asked
    /// <paramref name="inAir"/> from the jump on: the body and the motor's outcome on the tick it lands, and the spring then.
    /// </summary>
    static (Body Body, MotorOutcome Outcome, StartStopSpring Spring, LevelSpace Level) JumpAndLand(MoveIntent before, MoveIntent inAir, bool slide) =>
        JumpAndLand(before, _ => inAir, slide);

    /// <summary>As above, asked <paramref name="inAir"/> of the tick in the air (0 the first after the jump's).</summary>
    static (Body Body, MotorOutcome Outcome, StartStopSpring Spring, LevelSpace Level) JumpAndLand(MoveIntent before, Func<int, MoveIntent> inAir, bool slide)
    {
        var level = Flat();
        var body = Drive.Settle(level, new Vector3(0, 0, 0.01f));
        var spring = new StartStopSpring { HalfLife = 0.2f };
        for (int k = 0; k < 2 * World.TicksPerSecond; k++) BodyMotor.Step(ref body, ref spring, Human, before, level.Collision, [], 0, k);
        BodyMotor.Step(ref body, ref spring, Human, before with { Jump = true }, level.Collision, [], 0, 0);
        Assert.Equal(BodyState.Air, body.State);
        for (int k = 0; k < 2 * World.TicksPerSecond; k++)
        {
            var o = BodyMotor.Step(ref body, ref spring, Human, inAir(k), level.Collision, [], 0, k, landingSlide: slide);
            if (o.Landed) { landedAt = k; return (body, o, spring, level); }
        }
        Assert.Fail("it never landed");
        return default;
    }

    /// <summary>The tick in the air the last <see cref="JumpAndLand(MoveIntent, Func{int, MoveIntent}, bool)"/> landed on.</summary>
    [ThreadStatic] static int landedAt;

    static Vector2 Across(in Body b) => new(b.Velocity.X, b.Velocity.Y);

    [Fact]
    public void A_landing_with_nothing_asked_stops_at_once_and_with_landing_slide_it_slides_to_a_stop()
    {
        var nothing = new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Run };
        var (body, o, spring, level) = JumpAndLand(Drive.Toward(1, 0), nothing, slide: false);
        Assert.Equal(Vector2.Zero, Across(body));
        Assert.InRange(o.LandingDropped, 2.9f, 3.01f);   // the run's speed, carried through the air
        Assert.Equal(Vector2.Zero, spring.Acceleration);
        float at = body.Feet.X;
        for (int k = 0; k < World.TicksPerSecond; k++) BodyMotor.Step(ref body, ref spring, Human, nothing, level.Collision, [], 0, k);
        Assert.Equal(at, body.Feet.X);
        // The speed kept, the spring stops it as it stops a run (h/ln2 a metre a second).
        var (slid, o14, spring14, level14) = JumpAndLand(Drive.Toward(1, 0), nothing, slide: true);
        Assert.InRange(slid.Velocity.X, 2.9f, 3.01f);
        Assert.Equal(0f, o14.LandingDropped);
        float from = slid.Feet.X;
        for (int k = 0; k < 2 * World.TicksPerSecond; k++) BodyMotor.Step(ref slid, ref spring14, Human, nothing, level14.Collision, [], 0, k, landingSlide: true);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"from a run, landing asking nothing: 0 m on; with landing-slide {slid.Feet.X - from:0.00} m on"));
        Assert.InRange(slid.Feet.X - from, 0.75f, 0.9f);
    }

    [Fact]
    public void A_landing_asked_to_go_on_keeps_its_speed_along_the_way_up_to_the_pace_and_drops_the_rest()
    {
        // Running on: the run kept whole.
        var (on, onO, _, _) = JumpAndLand(Drive.Toward(1, 0), Drive.Toward(1, 0), slide: false);
        Assert.InRange(on.Velocity.X, 2.99f, 3.01f);
        Assert.InRange(onO.LandingDropped, 0f, 0.01f);
        // Turned square in the air: the old way's speed dropped at once, the new way's (what air control gave) kept.
        var (side, sideO, _, _) = JumpAndLand(Drive.Toward(1, 0), Drive.Toward(0, 1), slide: false);
        Assert.Equal(0f, side.Velocity.X, 6);
        Assert.InRange(side.Velocity.Y, 0.01f, Human.RunSpeed);
        Assert.InRange(sideO.LandingDropped, 2.5f, 3.01f);
        // Turned about on the landing's own tick (still moving ahead): nothing kept, ahead or back; the spring sets off the new way
        // from a standstill. (Turned about earlier in the air, air control has it going back by the landing: that is kept, to the pace.)
        var nothing = new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Run };
        JumpAndLand(Drive.Toward(1, 0), nothing, slide: false);
        int last = landedAt;
        var (back, backO, backSpring, backLevel) = JumpAndLand(Drive.Toward(1, 0), k => k == last ? Drive.Toward(-1, 0) : nothing, slide: false);
        Assert.Equal(last, landedAt);
        Assert.Equal(Vector2.Zero, Across(back));
        Assert.InRange(backO.LandingDropped, 2.9f, 3.01f);
        BodyMotor.Step(ref back, ref backSpring, Human, Drive.Toward(-1, 0), backLevel.Collision, [], 0, 0);
        Assert.True(back.Velocity.X < 0f, "the spring sets off the asked way");
        // From a sprint, landing at a walk: kept up to the walk's pace.
        var (walk, _, _, _) = JumpAndLand(Drive.Toward(1, 0, Gait.Sprint), Drive.Toward(1, 0, Gait.Walk), slide: false);
        Assert.Equal(Human.WalkSpeed, walk.Velocity.X, 4);
        Assert.Equal(0f, walk.Velocity.Y, 6);
        // With landing-slide, the speed across is kept whole whatever is asked.
        var (side14, side14O, _, _) = JumpAndLand(Drive.Toward(1, 0), Drive.Toward(0, 1), slide: true);
        Assert.InRange(side14.Velocity.X, 2.9f, 3.01f);
        Assert.Equal(0f, side14O.LandingDropped);
        // The ramp's landing is as ever (the switch is the spring's).
        var level = Flat();
        var a = Drive.Settle(level, new Vector3(0, 0, 0.01f));
        var b = a;
        var ramp = default(StartStopSpring);
        for (int i = 0; i < 2 * World.TicksPerSecond; i++)
        {
            var intent = i < 60 ? Drive.Toward(1, 0, jump: i == 50) : Drive.Toward(0, 1);
            BodyMotor.Step(ref a, Human, intent, level.Collision, [], 0, i);
            BodyMotor.Step(ref b, ref ramp, Human, intent, level.Collision, [], 0, i);
            Assert.Equal(a.Velocity, b.Velocity);
        }
    }

    // ---- worlds: who carries the spring, and its half-life ----

    sealed record Rig(World W, EntityId Player, MovementProfiles Profiles);

    static Rig Make(bool springs, Action<TestLevel>? more = null, float factor = 0f)
    {
        var t = new TestLevel().Floor(-20, -20, 200, 20, cell: 4f);
        more?.Invoke(t);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(3);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        PowerContract.Register(b);
        if (springs) { StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f }); Assert.True(StartStopSprings.RegisterPlayer(b)); }
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        int h = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true) { HalfLife = 0.2f }, isHuman: true);
        profiles.Add(new MovementProfile(AssetId.Of("test", "movement/elder"), "an elder", Human with { SprintSpeed = 9f }, true) { HalfLife = 0.1f });
        var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        profiles.Give(w, p, h);
        if (factor > 1f) w.Set(p, new PaceScale { Factor = factor });
        return new Rig(w, p, profiles);
    }

    static readonly ActionFrame Forward = new(new Vector2(0, 1), default, Buttons.None, default);

    [Fact]
    public void A_world_without_the_springs_has_no_store_of_them_and_in_one_with_them_only_the_player_carries_one()
    {
        var ramp = Make(springs: false);
        Assert.False(StartStopSprings.In(ramp.W));
        Assert.False(ramp.W.TryStore<StartStopSpring>(out _));
        // The numbers alone (a world with no player on the spring: an arena's bout) register no store.
        var numbers = new WorldBuilder(3);
        MovementServices.Register(numbers, Flat());
        StartStopSprings.Register(numbers, new StartStopSprings());
        Assert.False(numbers.Build().TryStore<StartStopSpring>(out _));
        Assert.False(StartStopSprings.RegisterPlayer(new WorldBuilder(3)));   // no numbers: no spring
        var sprung = Make(springs: true);
        Assert.True(StartStopSprings.In(sprung.W));
        Assert.True(sprung.W.Has<StartStopSpring>(sprung.Player));
        var other = MovementServices.SpawnWanderer(sprung.W, new(5, 5, 0.01f), 0f, Locomotion.Sandbox);
        Assert.False(sprung.W.Has<StartStopSpring>(other));   // every other body keeps the ramp
        // The same start, stepped (the first ticks settle it onto the floor): the ramp's body reaches the run in 0.15 s, the spring's in about 0.6 s.
        for (int i = 0; i < 24; i++) { MovementServices.Step(ramp.W, Forward); MovementServices.Step(sprung.W, Forward); }
        Assert.InRange(ramp.W.Get<Body>(ramp.Player).Velocity.Length(), 2.99f, 3.01f);
        Assert.InRange(sprung.W.Get<Body>(sprung.Player).Velocity.Length(), 0.5f, 1.5f);
        Assert.Equal(0.2f, sprung.W.Get<StartStopSpring>(sprung.Player).HalfLife);
    }

    [Fact]
    public void Each_body_s_half_life_is_its_profile_s_held_to_the_human_s_in_the_masquerade_and_shortened_by_a_pace_scale()
    {
        var r = Make(springs: true);
        ref var st = ref r.W.Get<MasqueradeState>(r.Player);
        st.Profile = 1;   // the elder's
        st.Mode = MasqueradeMode.Unbound;
        MovementServices.Step(r.W, Forward);
        Assert.Equal(0.1f, r.W.Get<StartStopSpring>(r.Player).HalfLife);
        r.W.Get<MasqueradeState>(r.Player).Mode = MasqueradeMode.Masquerade;
        MovementServices.Step(r.W, Forward);
        Assert.Equal(0.2f, r.W.Get<StartStopSpring>(r.Player).HalfLife);         // held to the human's: the slower
        Assert.Equal(0.2f, Masquerade.HeldHalfLife(0.1f, 0.2f));
        Assert.Equal(0.25f, Masquerade.HeldHalfLife(0.25f, 0.2f));
        Assert.Equal(0f, Masquerade.HeldHalfLife(0f, 0.2f));                      // a body on the ramp stays on it
        var paced = Make(springs: true, factor: 4f);
        paced.W.Get<MasqueradeState>(paced.Player).Mode = MasqueradeMode.Unbound;
        MovementServices.Step(paced.W, Forward);
        Assert.Equal(0.05f, paced.W.Get<StartStopSpring>(paced.Player).HalfLife, 5);   // min(0.2, 0.2 / 4)
        // The run's half-life given (--start-stop-half-life) is the player's whatever its profile and mode.
        r.W.Service<StartStopSprings>().Override = 0.12f;
        MovementServices.Step(r.W, Forward);
        Assert.Equal(0.12f, r.W.Get<StartStopSpring>(r.Player).HalfLife);
        r.W.Service<StartStopSprings>().Override = null;
        // A body with no masquerade state (a second controlled body, as a test may make): the default.
        var lone = MovementServices.SpawnControlled(r.W, new(8, 8, 0.01f), 0f, Locomotion.Sandbox);
        MovementServices.Step(r.W, Forward);
        Assert.Equal(0.2f, r.W.Get<StartStopSpring>(lone).HalfLife);
    }

    [Fact]
    public void Setting_off_from_rest_on_the_spring_is_no_stop_and_a_wall_is()
    {
        var open = Make(springs: true);
        var stops = new List<PlayerStopped>();
        for (int i = 0; i < World.TicksPerSecond; i++) { MovementServices.Step(open.W, Forward); stops.AddRange(open.W.Events<PlayerStopped>().ThisTick); }
        Assert.Empty(stops);   // its first ticks go slower than StopSpeed, and the spring asked no more
        // Against a block 1.5 m ahead too high for any move (the player faces +X: forward is +X), stopped: told once.
        var walled = Make(springs: true, t => t.Box(new(1.5f, -5, 0), new(2f, 5, 3f)));
        for (int i = 0; i < 2 * World.TicksPerSecond; i++) { MovementServices.Step(walled.W, Forward); stops.AddRange(walled.W.Events<PlayerStopped>().ThisTick); }
        Assert.Single(stops);
        Assert.True(stops[0].Feet.X > 1f);
    }

    // ---- the numbers to judge it by ----

    /// <summary>One pace's start and stop: the seconds to 90 % of it from standing, and the metres to stop from it (to 1 % of it, and all).</summary>
    readonly record struct PaceNumbers(float To90, float StopTo1, float StopAll);

    static PaceNumbers Pace(float halfLife, Gait gait)
    {
        var level = Flat();
        var body = Drive.Settle(level, new Vector3(-40, 0, 0.01f));
        var spring = new StartStopSpring { HalfLife = halfLife };
        float goal = Human.SpeedOf(gait), to90 = float.NaN;
        var go = Drive.Toward(1, 0, gait);
        for (int k = 1; k <= 3 * World.TicksPerSecond; k++)
        {
            BodyMotor.Step(ref body, ref spring, Human, go, level.Collision, [], 0, k);
            if (float.IsNaN(to90) && body.Velocity.X >= 0.9f * goal) to90 = k * Dt;
        }
        float from = body.Feet.X, to1 = float.NaN;
        var stand = new MoveIntent { Facing = 0f, Faces = true, Gait = gait };
        for (int k = 1; k <= 4 * World.TicksPerSecond; k++)
        {
            BodyMotor.Step(ref body, ref spring, Human, stand, level.Collision, [], 0, k);
            if (float.IsNaN(to1) && body.Velocity.X <= 0.01f * goal) to1 = body.Feet.X - from;
        }
        return new PaceNumbers(to90, to1, body.Feet.X - from);
    }

    /// <summary>A change of way at a run: the seconds until the velocity is within 10 % of the new one, the least speed on the way, and how far it carried on along the old way.</summary>
    readonly record struct TurnNumbers(float Within10, float LeastSpeed, float CarriedOn);

    static TurnNumbers Turn(float halfLife, Vector2 to)
    {
        var level = Flat();
        var body = Drive.Settle(level, new Vector3(-40, 0, 0.01f));
        var spring = new StartStopSpring { HalfLife = halfLife };
        for (int k = 0; k < 2 * World.TicksPerSecond; k++) BodyMotor.Step(ref body, ref spring, Human, Drive.Toward(1, 0), level.Collision, [], 0, k);
        float x0 = body.Feet.X, within = float.NaN, least = float.MaxValue, furthest = x0;
        var goal = Vector2.Normalize(to) * Human.RunSpeed;
        for (int k = 1; k <= 3 * World.TicksPerSecond; k++)
        {
            BodyMotor.Step(ref body, ref spring, Human, Drive.Toward(to.X, to.Y), level.Collision, [], 0, k);
            var v = new Vector2(body.Velocity.X, body.Velocity.Y);
            least = MathF.Min(least, v.Length());
            furthest = MathF.Max(furthest, body.Feet.X);
            if (float.IsNaN(within) && Vector2.Distance(v, goal) <= 0.1f * Human.RunSpeed) within = k * Dt;
        }
        return new TurnNumbers(within, least, furthest - x0);
    }

    [Fact]
    public void The_numbers_of_the_spring_beside_the_ramp()
    {
        var lines = new List<string> { "| | ramp () | spring 0.2 s (landing slide) |", "|---|---|---|" };
        string S(float v, string unit) => v.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit;
        foreach (var gait in new[] { Gait.Walk, Gait.Run, Gait.Sprint })
        {
            var ramp = Pace(0f, gait);
            var spring = Pace(0.2f, gait);
            string name = gait.ToString().ToLowerInvariant();
            lines.Add($"| standing to 90 % of the {name} ({Human.SpeedOf(gait):0.0} m/s) | {S(ramp.To90, "s")} | {S(spring.To90, "s")} |");
            lines.Add($"| stopping from the {name}: to 1 % / all | {S(ramp.StopTo1, "m")} / {S(ramp.StopAll, "m")} | {S(spring.StopTo1, "m")} / {S(spring.StopAll, "m")} |");
            // The spring's 90 % is 2.81 half-lives whatever the pace (to the tick); its stop h/ln2 a metre a second.
            Assert.InRange(spring.To90, 0.561f - Dt, 0.561f + Dt);
            // (the motor moves by each tick's new speed: half a tick's travel less than the integral)
            Assert.InRange(spring.StopAll, Human.SpeedOf(gait) * (0.2f / 0.6931f - Dt / 2f) - 0.01f, Human.SpeedOf(gait) * (0.2f / 0.6931f - Dt / 2f) + 0.01f);
            Assert.InRange(ramp.To90, 0.9f * Human.SpeedOf(gait) / Human.AccelerationCapacity, 0.9f * Human.SpeedOf(gait) / Human.AccelerationCapacity + Dt);
        }
        foreach (var (what, to) in new[] { ("a right-angle turn at a run", new Vector2(0, 1)), ("a reversal at a run", new Vector2(-1, 0)) })
        {
            var ramp = Turn(0f, to);
            var spring = Turn(0.2f, to);
            lines.Add($"| {what}: within 10 % of the new way | {S(ramp.Within10, "s")} | {S(spring.Within10, "s")} |");
            lines.Add($"| {what}: the least speed on the way | {S(ramp.LeastSpeed, "m/s")} | {S(spring.LeastSpeed, "m/s")} |");
            lines.Add($"| {what}: carried on along the old way | {S(ramp.CarriedOn, "m")} | {S(spring.CarriedOn, "m")} |");
        }
        foreach (var l in lines) output.WriteLine(l);
    }
}
