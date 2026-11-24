// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The body's motor on small levels built in memory: steps of 0.34 and
/// 0.36 m against a stride of 0.35, slopes either side of 45°, the jump's height, sliding along a wall, never
/// ending inside anything, the grace after an edge, and the same digest from the same inputs.
/// </summary>
public sealed class BodyMotorTests
{
    static LevelSpace StepLevel(float height) =>
        new TestLevel().Floor(-10, -10, 20, 10).Box(new(2, -5, 0), new(15, 5, height)).Build();

    [Theory]
    [InlineData(0.34f, true)]
    [InlineData(0.36f, false)]
    public void A_body_walks_up_a_step_of_0_34_m_and_not_one_of_0_36(float height, bool climbs)
    {
        var level = StepLevel(height);
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        Assert.Equal(BodyState.Ground, body.State);
        foreach (var gait in new[] { Gait.Walk, Gait.Run })
        {
            var b = Drive.Run(level, body, 360, (_, _) => Drive.Toward(1, 0, gait));
            if (climbs)
            {
                Assert.True(b.Feet.X > 3f, $"{gait}: stopped at x {b.Feet.X}");
                Assert.InRange(b.Feet.Z, height - 0.001f, height + 0.005f);
            }
            else
            {
                Assert.InRange(b.Feet.X, 1.6f, 2f - 0.29f);
                Assert.InRange(b.Feet.Z, -0.001f, 0.005f);
            }
            Assert.Equal(BodyState.Ground, b.State);
        }
    }

    [Theory]
    [InlineData(44f, false)]
    [InlineData(46f, true)]
    public void A_body_stands_on_a_44_degree_slope_and_slides_off_a_46_degree_one(float degrees, bool slides)
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Ramp(2, 6, -5, 5, degrees).Build();
        float up = 3f * DetMath.Tan(degrees * DetMath.DegToRad);
        var body = Body.At(new(5, 0, up + 0.3f), 0f);
        var still = new MoveIntent();
        body = Drive.Run(level, body, 60, (_, _) => still);
        var then = body.Feet;
        body = Drive.Run(level, body, 300, (_, _) => still);
        if (slides)
        {
            Assert.True(body.Feet.X < then.X - 1f, $"stayed at x {body.Feet.X} (from {then.X})");
        }
        else
        {
            Assert.Equal(BodyState.Ground, body.State);
            Assert.True(Vector3.Distance(then, body.Feet) < 0.005f, $"crept from {then} to {body.Feet}");
        }
    }

    [Theory]
    [InlineData(Gait.Walk)]
    [InlineData(Gait.Run)]
    [InlineData(Gait.Sprint)]
    public void A_body_walks_down_stairs_and_a_slope_without_leaving_the_ground(Gait gait)
    {
        // A stair of five 0.17 m risers on 0.3 m treads down from a platform, and a 30° ramp down from another.
        var build = new TestLevel().Floor(-20, -20, 20, 20).Box(new(-5, -2, 0), new(0, 2, 1.02f));
        for (int i = 1; i <= 5; i++) build.Box(new(0.3f * (i - 1), -2, 0), new(0.3f * i, 2, 1.02f - 0.17f * i));
        build.Ramp(-4, 4, 6, 10, 30f).Box(new(0, 6, 0), new(4, 10, 4 * DetMath.Tan(30 * DetMath.DegToRad)));
        var level = build.Build();
        foreach (var (from, way) in new[] { (new Vector3(-3, 0, 1.03f), new Vector2(1, 0)), (new Vector3(2, 8, 2.32f), new Vector2(-1, 0)) })
        {
            var body = Drive.Settle(level, from);
            int air = 0;
            // Walk until well past the bottom, then stop.
            body = Drive.Run(level, body, 720, (_, b) => (way.X > 0 ? b.Feet.X > 3 : b.Feet.X < -6) ? new MoveIntent() : Drive.Toward(way.X, way.Y, gait),
                             (_, b, _) => { if (b.State == BodyState.Air) air++; });
            Assert.True(air == 0, $"{gait} from {from}: {air} ticks in the air, ended at {body.Feet}");
            Assert.InRange(body.Feet.Z, -0.001f, 0.005f);
        }
    }

    [Fact]
    public void The_jumps_apex_is_0_5_m_above_the_take_off_within_1_cm()
    {
        var level = new TestLevel().Floor(-10, -10, 10, 10).Build();
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        float start = body.Feet.Z, highest = start;
        float? apex = null;
        bool landed = false;
        Drive.Run(level, body, 240, (i, _) => new MoveIntent { Jump = i == 0 }, (_, b, o) =>
        {
            highest = MathF.Max(highest, b.Feet.Z);
            if (o.Apex) apex = o.ApexHeight;
            if (o.Landed) landed = true;
        });
        Assert.NotNull(apex);
        Assert.InRange(apex!.Value, 0.49f, 0.51f);
        Assert.InRange(highest - start, 0.49f, 0.51f);
        Assert.True(landed);
    }

    [Fact]
    public void A_body_slides_along_a_wall_met_at_an_angle_keeping_its_speed_along_the_wall()
    {
        var level = new TestLevel().Floor(-10, -30, 10, 30).Box(new(3, -30, 0), new(3.5f, 30, 3)).Build();
        var body = Drive.Settle(level, new(2, -15, 0.01f));
        // Thirty degrees from the wall's line: half the speed into it, 0.866 along it.
        var way = Drive.Toward(0.5f, 0.866f);
        body = Drive.Run(level, body, 240, (_, _) => way);
        var a = body.Feet;
        body = Drive.Run(level, body, 120, (_, _) => way);
        float along = (body.Feet.Y - a.Y) / 1f;
        Assert.InRange(along, 3f * 0.866f - 0.05f, 3f * 0.866f + 0.01f);
        Assert.InRange(body.Feet.X, 3f - 0.3f - 0.01f, 3f - 0.3f);
    }

    /// <summary>Everything at once: steps, slopes, a wall, a low ceiling and a pit.</summary>
    internal static LevelSpace Playground() => new TestLevel()
        .Floor(-20, -20, 20, 20)
        .Box(new(2, -6, 0), new(4, -2, 0.34f)).Box(new(2, 2, 0), new(4, 6, 0.36f))
        .Ramp(-8, 4, -6, -2, 44f).Ramp(-8, 4, 2, 6, 46f)
        .Box(new(8, -10, 0), new(8.5f, 10, 3f))
        .Box(new(-4, 8, 1.95f), new(4, 14, 2.2f))
        .Box(new(-2, -14, 0), new(2, -10, 1.0f))
        .Floor(-30, -30, 30, 30, -4f)
        .Build("playground");

    [Fact]
    public void A_body_never_ends_a_tick_more_than_1_mm_inside_a_triangle()
    {
        var level = Playground();
        var rng = new Rng(7);
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        var intent = new MoveIntent();
        float worst = 0f;
        int jumps = 0, landings = 0;
        Drive.Run(level, body, 120 * 60, (i, b) =>
        {
            if (i % 60 == 0)
            {
                float yaw = rng.NextFloat(-DetMath.Pi, DetMath.Pi);
                var (s, c) = DetMath.SinCos(yaw);
                intent = new MoveIntent { Direction = new Vector2(c, s), Gait = (Gait)rng.NextInt(3), Facing = yaw, Faces = true };
            }
            // Head back to the middle now and then, so the drive keeps meeting the playground's things.
            if (i % 600 == 300) intent.Direction = Vector2.Normalize(-new Vector2(b.Feet.X, b.Feet.Y) + new Vector2(0.01f, 0));
            var now = intent;
            now.Jump = rng.NextInt(90) == 0;
            return now;
        }, (i, b, o) =>
        {
            worst = MathF.Max(worst, Drive.Depth(level, b));
            if (o.TookOff) jumps++;
            if (o.Landed) landings++;
            Assert.True(b.Feet.Z > -4.01f, $"fell through at tick {i}: {b.Feet}");
        });
        Assert.True(worst <= 0.001f, $"ended a tick {worst * 1000:0.###} mm inside a triangle");
        Assert.True(jumps > 10 && landings > 10, $"{jumps} jumps, {landings} landings");
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(18, false)]
    public void A_coyote_jump_works_inside_0_1_s_after_an_edge_and_not_after(int ticksAfter, bool jumps)
    {
        var level = new TestLevel().Box(new(-10, -5, -1), new(2, 5, 0)).Floor(-10, -10, 20, 10, -3f).Build();
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        int left = -1;
        bool tookOff = false, coyote = false;
        var run = Drive.Toward(1, 0);
        Drive.Run(level, body, 240, (i, b) =>
        {
            if (left < 0 && b.State == BodyState.Air) left = i;
            var now = run;
            now.Jump = left >= 0 && i - left == ticksAfter;
            return now;
        }, (_, _, o) =>
        {
            if (o.TookOff) { tookOff = true; coyote = o.TookOffCoyote; }
        });
        Assert.True(left > 0, "never left the edge");
        Assert.Equal(jumps, tookOff);
        if (jumps) Assert.True(coyote);
    }

    [Theory]
    [InlineData(0.05f, true)]
    [InlineData(0.3f, false)]
    public void A_jump_pressed_shortly_before_landing_takes_off_on_landing_and_one_pressed_long_before_does_not(float before, bool jumps)
    {
        var level = new TestLevel().Floor(-10, -10, 10, 10).Build();
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        // A first jump (a hop of 0.5 m) lasts 2·√(2gh)/g ≈ 0.639 s, so its landing is about tick 77; the second press comes `before` earlier.
        int press = 77 - (int)(before * World.TicksPerSecond);
        int landed = -1;
        bool again = false, buffered = false;
        Drive.Run(level, body, 200, (i, _) => new MoveIntent { Jump = i == 0 || i == press }, (i, _, o) =>
        {
            if (o.Landed && landed < 0) landed = i;
            if (o.TookOff && i > 0) { again = true; buffered = o.TookOffBuffered; }
        });
        Assert.InRange(landed, 70, 84);
        Assert.Equal(jumps, again);
        if (jumps) Assert.True(buffered);
    }

    internal static World MakeWorld(LevelSpace level, ulong seed, out EntityId player)
    {
        var w = MovementServices.Register(new WorldBuilder(seed), level).Build();
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        var a = MovementServices.SpawnBody(w, new(1.5f, 0.2f, 0.01f), 1f, Locomotion.Sandbox);
        var b = MovementServices.SpawnBody(w, new(-3, -3, 0.5f), 2f, Locomotion.Sandbox);
        w.Get<MoveIntent>(a) = Drive.Toward(-1, 0, Gait.Walk);
        w.Get<MoveIntent>(b) = Drive.Toward(1, 1, Gait.Sprint);
        return w;
    }

    internal static ActionFrame RandomFrame(Rng rng, ActionFrame was, int tick)
    {
        if (tick % 45 != 0) return was with { Look = new Vector2(was.Look.X * 0.9f, 0) , Held = was.Held & ~Buttons.Jump };
        var held = Buttons.None;
        if (rng.NextInt(4) == 0) held |= Buttons.Jump;
        if (rng.NextInt(3) == 0) held |= Buttons.Sprint;
        if (rng.NextInt(5) == 0) held |= Buttons.Walk;
        if (rng.NextInt(9) == 0) held |= Buttons.SwitchView;
        return new ActionFrame(new Vector2(rng.NextFloat(-1, 1), rng.NextFloat(-0.2f, 1)), new Vector2(rng.NextFloat(-0.05f, 0.05f), rng.NextFloat(-0.02f, 0.02f)), held, default);
    }

    [Fact]
    public void Two_runs_with_the_same_inputs_have_the_same_digest_at_every_tick()
    {
        var level = Playground();
        List<ulong> Run()
        {
            var w = MakeWorld(level, 42, out _);
            var input = w.Service<TickInput>();
            var rng = new Rng(9);
            var frame = ActionFrame.Idle;
            var digests = new List<ulong>();
            for (int t = 0; t < 900; t++)
            {
                frame = RandomFrame(rng, frame, t);
                input.Set(frame);
                w.Step();
                digests.Add(w.Digest());
            }
            return digests;
        }
        var first = Run();
        var second = Run();
        Assert.Equal(first, second);
        Assert.True(first.Distinct().Count() > 800, "the world hardly changed");
    }

    [Fact]
    public void A_drive_recorded_on_a_tape_replays_from_the_file_with_the_same_digests()
    {
        var level = Playground();
        var w = MakeWorld(level, 42, out _);
        var tape = new Tape { Header = new TapeHeader { Level = level.Name, Seed = 42 } };
        var rng = new Rng(21);
        var frame = ActionFrame.Idle;
        for (int t = 1; t <= 900; t++)
        {
            frame = RandomFrame(rng, frame, t);
            tape.Frames.Add(frame);
            MovementServices.Step(w, frame);
            if (t % Tape.DigestEvery == 0) tape.Digests[t] = w.Digest();
        }
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-movement-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            var replay = MakeWorld(level, back.Header.Seed, out _);
            for (int t = 1; t <= back.Frames.Count; t++)
            {
                MovementServices.Step(replay, back.Frames[t - 1]);
                if (back.Digests.TryGetValue(t, out ulong d)) Assert.True(d == replay.Digest(), $"the replay parted at tick {t}");
            }
            Assert.Equal(30, back.Digests.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_controlled_body_goes_where_the_view_looks_and_the_camera_comes_in_from_a_wall()
    {
        var level = new TestLevel().Floor(-10, -10, 10, 10).Box(new(-3.5f, -5, 0), new(-3, 5, 4)).Build();
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        var input = w.Service<TickInput>();
        var view = w.Service<ViewState>();
        // Turn a quarter to the left (to face +Y) in one tick, then walk forward for a second.
        input.Set(new ActionFrame(Vector2.Zero, new Vector2(DetMath.HalfPi, 0), Buttons.None, default));
        w.Step();
        for (int i = 0; i < 120; i++) { input.Set(new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Walk, default)); w.Step(); }
        var feet = w.Get<Body>(player).Feet;
        Assert.True(feet.Y > 1.0f && MathF.Abs(feet.X) < 0.05f, $"went to {feet}");
        Assert.True(view.HasView);
        Assert.InRange(view.Boom, 2.3f, 2.5f);
        // Face +X: the camera behind is now towards the wall at x = −3, 3 m away from the head's shoulder.
        input.Set(new ActionFrame(Vector2.Zero, new Vector2(-DetMath.HalfPi, 0), Buttons.None, default));
        w.Step();
        Assert.InRange(w.Get<CameraRig>(player).Yaw, -0.01f, 0.01f);
        var eye = view.View.Eye;
        for (int i = 0; i < 300; i++) { input.Set(new ActionFrame(new Vector2(0, -1), Vector2.Zero, Buttons.None, default)); w.Step(); }
        Assert.True(view.Boom < 2.0f, $"boom {view.Boom}, eye {view.View.Eye}");
        Assert.True(view.View.Eye.X > -3f + 0.2f - 0.01f, $"the eye went into the wall: {view.View.Eye}");
        _ = eye;
        // The pointer freed: the look no longer turns the view.
        input.Set(new ActionFrame(Vector2.Zero, new Vector2(1, 0), Buttons.FreePointer, default));
        w.Step();
        Assert.True(w.Get<CameraRig>(player).PointerFree);
        Assert.InRange(w.Get<CameraRig>(player).Yaw, -0.01f, 0.01f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_reticle_answers_the_person_ahead_and_a_post_between_the_eyes_and_it_blocks(bool post)
    {
        var build = new TestLevel().Floor(-10, -10, 20, 10);
        // The camera is over the right shoulder (−y), the person ahead on its line; the post stands on the
        // line from the body's own eyes to the person, clear of the camera's.
        if (post) build.Box(new(0.9f, -0.2f, 0), new(1.1f, 0f, 3f));
        var level = build.Build();
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        var other = MovementServices.SpawnBody(w, new(6, -0.5f, 0.01f), 0f, Locomotion.Sandbox);
        var input = w.Service<TickInput>();
        input.Set(new ActionFrame(Vector2.Zero, new Vector2(0, -0.1f), Buttons.None, default));
        w.Step();
        for (int i = 0; i < 30; i++) { input.Set(ActionFrame.Idle); w.Step(); }
        var answer = w.Service<ReticleState>().Answer;
        Assert.Equal(ReticleTarget.Person, answer.Kind);
        Assert.Equal(other.Packed, answer.Entity);
        Assert.InRange(answer.Distance, 5f, 6.5f);
        Assert.Equal(post, answer.Blocked);
        _ = player;
    }

    [Fact]
    public void Two_bodies_walking_into_each_other_meet_as_capsules_and_slide_round()
    {
        var level = new TestLevel().Floor(-10, -10, 10, 10).Build();
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var a = MovementServices.SpawnBody(w, new(-3, 0.05f, 0.01f), 0f, Locomotion.Sandbox);
        var b = MovementServices.SpawnBody(w, new(3, -0.05f, 0.01f), DetMath.Pi, Locomotion.Sandbox);
        float closest = float.MaxValue;
        for (int t = 0; t < 600; t++)
        {
            w.Get<MoveIntent>(a) = Drive.Toward(1, 0, Gait.Walk);
            w.Get<MoveIntent>(b) = Drive.Toward(-1, 0, Gait.Walk);
            w.Service<TickInput>().Set(ActionFrame.Idle);
            w.Step();
            var pa = w.Get<Body>(a).Feet; var pb = w.Get<Body>(b).Feet;
            closest = MathF.Min(closest, new Vector2(pa.X - pb.X, pa.Y - pb.Y).Length());
        }
        Assert.True(closest >= 0.6f - 0.002f, $"came {closest} apart");
        Assert.True(w.Get<Body>(a).Feet.X > 1f && w.Get<Body>(b).Feet.X < -1f, "they did not get past each other");
    }

    [Fact]
    public void A_fall_from_a_3_m_ledge_lands_with_its_height_and_impact_speed()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Box(new(-10, -5, 0), new(2, 5, 3)).Build();
        var body = Drive.Settle(level, new(0, 0, 3.01f));
        MotorOutcome? landing = null;
        Drive.Run(level, body, 480, (_, _) => Drive.Toward(1, 0, Gait.Walk), (_, _, o) => { if (o.Landed) landing ??= o; });
        Assert.NotNull(landing);
        Assert.InRange(landing!.Value.FallHeight, 2.98f, 3.02f);
        Assert.InRange(landing.Value.ImpactSpeed, MathF.Sqrt(2 * 9.81f * 3f) - 0.1f, MathF.Sqrt(2 * 9.81f * 3f) + 0.1f);
    }

    [Fact]
    public void A_fall_onto_a_floor_lands_on_the_tick_it_meets_it_wherever_the_floor_s_seams_lie()
    {
        // (A walk in play: running into the proving ground's pit logged "from 4.00 m at 0.1 m/s"): meeting a
        // floor across one of its triangles' seams, the contact's normal tilts by a rounding error, and the slide along
        // it left a hair of rise (+2e-6 m/s) that put the landing off a tick, its speed then a tick's fall from rest.
        // Off a 3 m block, running west and east, at forty-one places across the floor's 2 m cells.
        var level = new TestLevel().Floor(-20, -10, 20, 10).Box(new(2, -5, 0), new(15, 5, 3)).Box(new(-15, -5, 0), new(-2, 5, 3)).Build();
        float want = MathF.Sqrt(2 * 9.81f * 3f);
        for (int way = -1; way <= 1; way += 2)
            for (int i = 0; i <= 40; i++)
            {
                float y = -1f + 0.05f * i;
                var body = Drive.Settle(level, new(way < 0 ? 3.5f : -3.5f, y, 3.01f), way < 0 ? DetMath.Pi : 0f);
                MotorOutcome? landing = null;
                Drive.Run(level, body, 360, (_, _) => Drive.Toward(way, 0, Gait.Run), (_, _, o) => { if (o.Landed) landing ??= o; });
                Assert.True(landing is { } l && MathF.Abs(l.ImpactSpeed - want) < 0.15f,
                    $"running {(way < 0 ? "west" : "east")} at y {y:0.00}: landed at {landing?.ImpactSpeed:0.00} m/s, not {want:0.00}");
            }
    }

    [Fact]
    public void A_body_left_to_face_its_way_turns_to_it_at_720_degrees_a_second()
    {
        var level = new TestLevel().Floor(-20, -20, 20, 20).Build();
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        Assert.InRange(MathF.Abs(DetMath.WrapAngle(body.Yaw)), 0f, 0.01f);
        // Wished to go the other way (+180°): after a tenth of a second it has turned 72°, after a quarter all the way.
        var tenth = Drive.Run(level, body, 12, (_, _) => new MoveIntent { Direction = new(-1, 0), Gait = Gait.Run });
        Assert.InRange(MathF.Abs(tenth.Yaw) * DetMath.RadToDeg, 71f, 73f);
        var quarter = Drive.Run(level, body, 30, (_, _) => new MoveIntent { Direction = new(-1, 0), Gait = Gait.Run });
        Assert.InRange(MathF.Abs(DetMath.WrapAngle(quarter.Yaw)) * DetMath.RadToDeg, 179.9f, 180.1f);
    }

    [Fact]
    public void In_third_person_the_player_turns_to_the_way_he_moves_unless_the_setting_says_the_view()
    {
        var level = new TestLevel().Floor(-20, -20, 20, 20).Build();
        foreach (var facing in new[] { ThirdPersonFacing.Move, ThirdPersonFacing.View })
        {
            var w = MovementServices.Register(new WorldBuilder(1), level).Build();
            var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
            var input = w.Service<TickInput>();
            input.ThirdPersonFacing = facing;
            // Hold "left" (A) for half a second with the view along +X: the body goes towards +Y.
            for (int i = 0; i < 60; i++) { input.Set(new ActionFrame(new Vector2(-1, 0), Vector2.Zero, Buttons.None, default)); w.Step(); }
            var body = w.Get<Body>(player);
            Assert.True(body.Velocity.Y > 2.9f, $"{facing}: velocity {body.Velocity}");
            float expect = facing == ThirdPersonFacing.Move ? DetMath.HalfPi : 0f;
            Assert.InRange(DetMath.WrapAngle(body.Yaw - expect), -0.01f, 0.01f);
            // First person faces the view whatever the setting says (switch view cycles right shoulder, left, first).
            foreach (var held in new[] { Buttons.SwitchView, Buttons.None, Buttons.SwitchView })
            {
                input.Set(new ActionFrame(new Vector2(-1, 0), Vector2.Zero, held, default));
                w.Step();
            }
            Assert.Equal(ViewKind.First, w.Get<CameraRig>(player).View);
            Assert.InRange(DetMath.WrapAngle(w.Get<Body>(player).Yaw), -0.01f, 0.01f);
        }
    }

    [Fact]
    public void Sprint_is_only_roughly_forwards_backwards_it_is_a_run()
    {
        var level = new TestLevel().Floor(-20, -20, 20, 20).Build();
        var body = Drive.Settle(level, new(0, 0, 0.01f));
        var forward = Drive.Run(level, body, 240, (_, _) => new MoveIntent { Direction = new(1, 0), Gait = Gait.Sprint, Facing = 0, Faces = true });
        var back = Drive.Run(level, body, 240, (_, _) => new MoveIntent { Direction = new(-1, 0), Gait = Gait.Sprint, Facing = 0, Faces = true });
        Assert.InRange(forward.Velocity.X, 5.49f, 5.51f);
        Assert.InRange(back.Velocity.X, -3.01f, -2.99f);
    }
}
