// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The models repository's people in motion on its own files (skip without <c>VITOMIR_MODELS_DIR</c>): the player
/// registered as a kind; walking, running and every way between with the planted feet's slide measured; a standing turn of
/// 120°; every move's stand-in; the first-person eye; the hit volumes; the same digests from two runs; the twist rule against
/// the clips' own twist bones.
/// </summary>
public sealed class PeopleMotionTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_player_registers_as_a_kind_with_every_role_its_limbs_head_and_twist()
    {
        var (assets, k, person) = Player();
        var kind = assets.Kind(k);
        var skeleton = person.Model.Skeleton;
        output.WriteLine($"{kind.Name}: {skeleton.Count} bones, {assets.ClipCount} clips");
        foreach (var role in Enum.GetValues<MotionRole>())
        {
            var m = kind.Motion(role);
            output.WriteLine($"  {role}: {(m.Has ? assets.Clip(m.Clip).Name : "-")} natural {F(m.NaturalSpeed, "0.00")}{(m.Composed ? " (composed)" : "")}");
        }
        Assert.Equal(DetMath.HalfPi, kind.FacingYaw);
        Assert.Null(kind.Actions);
        Assert.Empty(kind.Held);
        Assert.Equal("stand", assets.Clip(kind.Motion(MotionRole.Idle).Clip).Name);
        Assert.Equal(1.25f, kind.Motion(MotionRole.Walk).NaturalSpeed, 3);
        Assert.Equal(3.0f, kind.Motion(MotionRole.Run).NaturalSpeed, 3);
        foreach (var role in new[] { MotionRole.Jump, MotionRole.Fall, MotionRole.Land, MotionRole.HopUp, MotionRole.Hurdle,
                                     MotionRole.Vault, MotionRole.ClimbOnto, MotionRole.Mantle, MotionRole.Grab, MotionRole.Hang, MotionRole.PullUp, MotionRole.Drop, MotionRole.Dodge })
            Assert.True(kind.Motion(role).Has && kind.Motion(role).Composed, $"{role} has its stand-in");
        // The crouch: the models repository's own when the motions file has it, else the stand-ins.
        if (kind.Person!.Crouch is { } crouch)
        {
            Assert.Equal("crouch", assets.Clip(kind.Motion(MotionRole.Crouch).Clip).Name);
            Assert.False(kind.Motion(MotionRole.Crouch).Composed);
            Assert.Contains("crouch_walk", assets.Clip(kind.Motion(MotionRole.CrouchWalk).Clip).Name);   // the gait's blend, "player:crouch_walk@0"
            Assert.False(kind.Motion(MotionRole.CrouchWalk).Composed);
            Assert.True(crouch.Kneel >= 0 && crouch.Settle >= 0 && crouch.Rise >= 0 && crouch.Down >= 0 && crouch.Up >= 0, "the crouch's ways in and out");
        }
        else
            foreach (var role in new[] { MotionRole.Crouch, MotionRole.CrouchWalk })
                Assert.True(kind.Motion(role).Has && kind.Motion(role).Composed, $"{role} has its stand-in");
        Assert.False(kind.Motion(MotionRole.Turn).Has);

        var limbs = kind.Limbs!;
        Assert.Equal(skeleton.Find("pelvis"), limbs.Root);
        Assert.Equal((skeleton.Find("thigh_l"), skeleton.Find("calf_l"), skeleton.Find("foot_l"), skeleton.Find("ball_l")), (limbs.LeftLeg.Upper, limbs.LeftLeg.Lower, limbs.LeftLeg.End, limbs.LeftLeg.Tip));
        Assert.Equal((skeleton.Find("upperarm_r"), skeleton.Find("lowerarm_r"), skeleton.Find("hand_r"), skeleton.Find("middle_01_r")), (limbs.RightArm.Upper, limbs.RightArm.Lower, limbs.RightArm.End, limbs.RightArm.Tip));
        output.WriteLine($"shoulders {F(limbs.ShoulderWidth)} m apart, ankles {F(limbs.AnkleHeight)} m up");
        Assert.InRange(limbs.ShoulderWidth, 0.25f, 0.45f);
        Assert.InRange(limbs.AnkleHeight, 0.04f, 0.12f);

        var head = kind.Head!;
        Assert.Equal(skeleton.Find("head"), head.Bone);
        Assert.Equal(skeleton.Find("neck_01"), head.Neck);
        Assert.Contains(skeleton.Find("jaw"), head.Bones);
        Assert.Contains(skeleton.Find("eye_l"), head.Bones);
        Assert.DoesNotContain(skeleton.Find("spine_03"), head.Bones);
        var personMotion = kind.Person!;
        Assert.NotNull(personMotion.Walk);
        Assert.NotNull(personMotion.Run);
        Assert.Equal(8, personMotion.Walk!.Headings.Length);
        Assert.True(personMotion.Turns is { Complete: true });
        Assert.Equal(6, personMotion.Twist!.Limbs.Length);
        Assert.Equal(22, personMotion.Twist.Limbs.Sum(l => l.Bones.Length));
    }

    /// <summary>
    /// The planted feet's slide (cm/s): the body moved at the blend's own speed the way <paramref name="degrees"/> from its
    /// facing for <paramref name="seconds"/> after a second to settle; a foot is planted while its ball is within a centimetre of
    /// its lowest; the mean and the most of the balls' speed over the ground while planted.
    /// </summary>
    (float Mean, float Most, float Speed, string Clip) Slide(CharacterAssets assets, int kind, MotionRole role, float degrees, float speedScale = 1f, float seconds = 3f,
                                                            AnimationSettings? animation = null)
    {
        var person = assets.Kind(kind).Person!;
        var gait = person.GaitOf(role)!;
        int bin = ((int)MathF.Round(degrees) % 360 + 360) % 360;
        float speed = gait.Speeds[bin] * speedScale;
        float yaw = DetMath.HalfPi;
        float way = yaw + degrees * DetMath.DegToRad;
        var v = new Vector3(DetMath.Cos(way), DetMath.Sin(way), 0f) * speed;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw, animation: animation);
        var feet = Vector3.Zero;
        int ticks = (int)(seconds * World.TicksPerSecond), settle = World.TicksPerSecond;
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        var path = new Vector3[2][];
        for (int f = 0; f < 2; f++) path[f] = new Vector3[ticks];
        for (int t = 0; t < settle + ticks; t++)
        {
            feet += v * Tick;
            Step(world, e, feet, yaw, true, velocity: v);
            if (t >= settle) for (int f = 0; f < 2; f++) path[f][t - settle] = Bone(world, e, assets, balls[f]);
        }
        string clip = assets.Clip(world.Get<CharacterAnimation>(e).Clip).Name;
        float sum = 0f, most = 0f;
        int n = 0;
        for (int f = 0; f < 2; f++)
        {
            float low = path[f].Min(p => p.Z);
            for (int t = 1; t < ticks; t++)
            {
                if (path[f][t].Z > low + 0.01f || path[f][t - 1].Z > low + 0.01f) continue;
                var d = path[f][t] - path[f][t - 1];
                float s = MathF.Sqrt(d.X * d.X + d.Y * d.Y) / Tick;
                sum += s; most = MathF.Max(most, s); n++;
            }
        }
        return (n > 0 ? sum / n * 100f : float.NaN, most * 100f, speed, clip);
    }

    /// <summary>The same measure on a clip alone in its own space (no rig, no legs' turn), moved at <paramref name="speed"/> the way <paramref name="degrees"/>.</summary>
    static (float Mean, float Most) RawSlide(CharacterAssets assets, int kind, IAnimationClip clip, float degrees, float speed, float natural, float seconds = 3f)
    {
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        float h = degrees * DetMath.DegToRad;
        var dir = new Vector3(-DetMath.Sin(h), DetMath.Cos(h), 0f);
        int ticks = (int)(seconds * World.TicksPerSecond);
        var path = new Vector3[2][];
        for (int f = 0; f < 2; f++) path[f] = new Vector3[ticks];
        for (int t = 0; t < ticks; t++)
        {
            clip.Sample(t * Tick * speed / natural, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            for (int f = 0; f < 2; f++) path[f][t] = model[balls[f]].Translation + dir * (speed * t * Tick);
        }
        float sum = 0f, most = 0f;
        int n = 0;
        for (int f = 0; f < 2; f++)
        {
            float low = path[f].Min(p => p.Z);
            for (int t = 1; t < ticks; t++)
            {
                if (path[f][t].Z > low + 0.01f || path[f][t - 1].Z > low + 0.01f) continue;
                var d = path[f][t] - path[f][t - 1];
                float s = MathF.Sqrt(d.X * d.X + d.Y * d.Y) / Tick;
                sum += s; most = MathF.Max(most, s); n++;
            }
        }
        return (n > 0 ? sum / n * 100f : float.NaN, most * 100f);
    }

    [Fact]
    public void What_registering_and_posing_the_player_costs()
    {
        var (_, _, person) = Player();
        var lib = People.PeopleLibraryTests.Library.Value!;
        var motions = lib.Motions("male");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var assets = new CharacterAssets();
        int kind = PeopleKinds.Register(assets, person, motions, "player");
        double register = watch.Elapsed.TotalMilliseconds;
        var poser = new PoseEvaluator();
        int n = person.Model.Skeleton.Count;
        var model = new Matrix4x4[n]; var skin = new Matrix4x4[n];
        var system = new AnimationSystem(assets);
        string Cost(string what, MotionInput input, PoseRig rig)
        {
            var a = CharacterAnimation.For(assets, kind);
            for (int t = 0; t < 120; t++) system.Step(ref a, input, Tick);
            for (int t = 0; t < 200; t++) poser.Evaluate(assets, a, rig, model, skin);
            // Process CPU time, not the clock: the build machine is shared, and a clock counts the time others had the core.
            var process = System.Diagnostics.Process.GetCurrentProcess();
            const int times = 2000;
            var cpu = process.TotalProcessorTime;
            for (int t = 0; t < times; t++) poser.Evaluate(assets, a, rig, model, skin);
            process.Refresh();
            double pose = (process.TotalProcessorTime - cpu).TotalMilliseconds * 1000.0 / times;
            cpu = process.TotalProcessorTime;
            for (int t = 0; t < times; t++) system.Step(ref a, input, Tick);
            process.Refresh();
            double step = (process.TotalProcessorTime - cpu).TotalMilliseconds * 1000.0 / times;
            return $"{what}: a pose {F((float)pose, "0.0")} µs, an animation step {F((float)step, "0.00")} µs ({assets.Clip(a.Clip).Name})";
        }
        var feet = new PoseRig { LegWeight = 1f, GroundWeight = 1f };
        output.WriteLine($"registering the player: {F((float)register, "0")} ms ({assets.ClipCount} clips, {motions.Count} of them the set's)");
        output.WriteLine(Cost("standing", MotionInput.Standing, default));
        output.WriteLine(Cost("walking ahead, feet placed", Moving(0f, 1.25f), feet));
        output.WriteLine(Cost("walking 20° (a blend), feet placed", Moving(20f, 1.2f), feet));
        output.WriteLine(Cost("walking 20°, legs turned 15°", Moving(20f, 1.2f), feet with { LegYaw = 0.26f }));
        output.WriteLine(Cost("running 150° (a blend of backpedals)", Moving(150f, 2.3f), feet));

        static MotionInput Moving(float degrees, float speed)
        {
            float r = degrees * DetMath.DegToRad;
            return new MotionInput { Grounded = true, GroundSpeed = speed, ForwardSpeed = speed * DetMath.Cos(r), SideSpeed = speed * DetMath.Sin(r) };
        }
    }

    [Fact]
    public void The_woman_registers_walks_and_sees_from_between_her_eyes()
    {
        var (assets, kind, person) = Woman();
        var k = assets.Kind(kind);
        output.WriteLine($"woman: {person.Model.Skeleton.Count} bones; walk drawn for {F(k.Motion(MotionRole.Walk).NaturalSpeed, "0.00")} m/s, run {F(k.Motion(MotionRole.Run).NaturalSpeed, "0.00")}; shoulders {F(k.Limbs!.ShoulderWidth)} m, ankles {F(k.Limbs.AnkleHeight)} m");
        Assert.NotNull(k.Person!.Walk);
        Assert.True(k.Person.Turns is { Complete: true });
        Assert.NotNull(HitVolumeFit.Fit(person.Model));
        foreach (var degrees in new[] { 0f, 67f, 112f, 180f })
        {
            var (mean, most, speed, clip) = Slide(assets, kind, MotionRole.Walk, degrees);
            output.WriteLine($"walk {degrees}°: {F(speed, "0.00")} m/s, planted slide mean {F(mean, "0.0")} cm/s, most {F(most, "0.0")} ({clip})");
            Assert.InRange(mean, 0f, 8f);
        }
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero);
        for (int t = 0; t < 60; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true);
        var eyes = k.Head!.EyesIn(world.Service<CharacterPoses>().Skin(world, e));
        output.WriteLine($"her eye standing {F(eyes.Z)} m up");
        Assert.InRange(eyes.Z, 1.4f, 1.6f);
        var turned = Turn(assets, kind, 120f, 90f);
        output.WriteLine($"120° at 90°/s: {string.Join(", ", turned.Played)}, the legs {F(turned.Left, "0.00")}° off");
        Assert.InRange(MathF.Abs(turned.Left), 0f, 1f);
    }

    [Fact]
    public void A_sprint_plays_the_sprint_clip_stride_matched_and_sprint_clip_off_the_run_capped()
    {
        // Ahead at a sprint, the sprint clip (drawn for 5.5 m/s) at the stride-matched rate,
        // its planted feet still; --sprint-clip-off (PersonSprintFrom 0) the earlier build's: the run's blend capped at 1.35x, the feet sliding.
        foreach (var (assets, kind, person) in new[] { Player(), Woman() })
        {
            var p = assets.Kind(kind).Person!;
            Assert.SkipUnless(p.Sprint >= 0, "the models repository's files are too old (no sprint)");
            Assert.Equal("sprint", assets.Clip(p.Sprint).Name);
            Assert.Equal(((Assets.INotedClip)assets.Clip(p.Sprint)).Notes.GroundSpeed, p.SprintSpeed, 3);   // 5.5 on older sets, 5.8 (not pinned)
            Assert.Contains(p.Sprint, p.PoseClips);                          // its tables made with the kind's
            float run = p.Run!.Speeds[0];
            foreach (float scale in new[] { 1f, 1.35f, p.SprintSpeed / run })
            {
                var (mean, most, speed, clip) = Slide(assets, kind, MotionRole.Run, 0f, scale);
                var (oldMean, _, _, oldClip) = Slide(assets, kind, MotionRole.Run, 0f, scale, animation: new AnimationSettings { PersonSprintFrom = 0f });
                output.WriteLine($"{person.Recipe.Name} ahead at {F(speed, "0.00")} m/s: planted slide mean {F(mean, "0.0")} cm/s, most {F(most, "0.0")} ({clip}); "
                                 + $"the capped run's {F(oldMean, "0.0")} cm/s ({oldClip})");
                if (speed < 4.25f)
                {
                    Assert.Contains(":run@", clip);                              // a run's blend (<kind>:run@0)
                    Assert.InRange(mean, 0f, 10f);
                }
                else
                {
                    Assert.Equal("sprint", clip);
                    Assert.InRange(mean, 0f, 15f);
                    Assert.InRange(oldMean, 100f, 200f);   // 5.5 m/s on a run drawn for 3.0 played at 1.35x (4.05 m/s): 1.45 m/s of slide
                }
            }
        }
    }

    [Fact]
    public void The_sprint_is_ahead_only_its_rate_by_the_speed_and_its_legs_turned_toward_the_way()
    {
        var (assets, kind, _) = Player();
        var p = assets.Kind(kind).Person!;
        Assert.SkipUnless(p.Sprint >= 0, "the models repository's files are too old (no sprint)");
        (string Clip, float Rate, float Legs) Going(float speed, float degrees, AnimationSettings? settings = null)
        {
            float yaw = DetMath.HalfPi, way = yaw + degrees * DetMath.DegToRad;
            var v = new Vector3(DetMath.Cos(way), DetMath.Sin(way), 0f) * speed;
            var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw, animation: settings);
            var feet = Vector3.Zero;
            for (int t = 0; t < 90; t++) { feet += v * Tick; Step(world, e, feet, yaw, true, velocity: v); }
            var a = world.Get<CharacterAnimation>(e);
            return (assets.Clip(a.Clip).Name, a.Rate, world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg);
        }
        float sprint = p.SprintSpeed;                                  // 5.5 on older sets, 5.8 (not pinned)
        var ahead = Going(sprint, 0f);
        Assert.Equal(("sprint", 1f), (ahead.Clip, MathF.Round(ahead.Rate, 3)));
        var faster = Going(sprint * 1.2f, 0f);                         // a Celerity sprint: the rate by the speed, to the cap
        Assert.Equal("sprint", faster.Clip);
        Assert.Equal(1.2f, faster.Rate, 3);
        Assert.Equal(new AnimationSettings().MostStrideRate, Going(12f, 0f).Rate, 3);
        var slower = Going(4.0f, 0f);                                  // under 4.25: the run
        Assert.NotEqual("sprint", slower.Clip);
        var aslant = Going(sprint, 30f);                               // within the sprint cone: the sprint, the legs turned toward the way
        output.WriteLine($"ahead {ahead}, at 6.6 m/s {faster}, at 4.0 {slower}, 30° aslant {aslant}");
        Assert.Equal("sprint", aslant.Clip);
        Assert.InRange(aslant.Legs, 20f, 31f);
        var aside = Going(sprint, 90f);                                // sideways at that speed (Celerity's run): the run's blend as before
        var back = Going(sprint, 180f);
        output.WriteLine($"sideways {aside}, back {back}");
        Assert.NotEqual("sprint", aside.Clip);
        Assert.NotEqual("sprint", back.Clip);
        Assert.NotEqual("sprint", Going(5.5f, 0f, new AnimationSettings { PersonSprintFrom = 0f }).Clip);   // --sprint-clip-off
    }

    [Fact]
    public void Walking_running_and_every_way_between_keep_the_planted_feet_nearly_still()
    {
        var (assets, kind, _) = Player();
        var person = assets.Kind(kind).Person!;
        foreach (var role in new[] { MotionRole.Walk, MotionRole.Run })
            foreach (var degrees in new[] { 0f, 45f, 90f, 135f, 180f, -135f, -90f, -45f, 22f, 67f, 100f, 112f, 113f, 125f, 157f, -22f, -112f })
            {
                var (mean, most, speed, clip) = Slide(assets, kind, role, degrees);
                int bin = ((int)MathF.Round(degrees) % 360 + 360) % 360;
                var blend = (GaitBlendClip)assets.Clip(person.GaitOf(role)!.First + bin);
                var raw = RawSlide(assets, kind, blend, degrees - blend.Turn * DetMath.RadToDeg, speed, blend.NaturalSpeed);
                output.WriteLine($"{role} {degrees,5}°: {F(speed, "0.00")} m/s, planted slide mean {F(mean, "0.0")} cm/s, most {F(most, "0.0")} ({clip}; the clip alone {F(raw.Mean, "0.0")}, most {F(raw.Most, "0.0")})");
                Assert.InRange(mean, 0f, 8f);                 // The models repository measures 1 to 7 cm/s in the files
                Assert.InRange(mean, 0f, raw.Mean + 0.5f);    // the platform adds nothing to the clip's own
                Assert.StartsWith(role == MotionRole.Walk ? "player:walk@" : "player:run@", clip);
            }
    }

    /// <summary>
    /// The view turned <paramref name="degrees"/> to the left at <paramref name="rate"/>° a second (0: at once), then held, for six
    /// seconds: the turn clips played, the legs' turn at the end (degrees), the legs' most turn in a tick and most turn back
    /// (degrees), and the most speed of a ball on the ground (cm/s; within 1.5 mm of the stand's height, its step's lift and set-down
    /// left out).
    /// </summary>
    static (List<string> Played, MotionRole End, float Left, float Most, float Back, float Slide) Turn(CharacterAssets assets, int kind, float degrees, float rate,
                                                                                                     LegTurnSettings? turn = null)
    {
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        float yaw0 = DetMath.HalfPi;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw0, legs: turn);
        for (int t = 0; t < 60; t++) Step(world, e, Vector3.Zero, yaw0, true);
        float ground = Bone(world, e, assets, balls[0]).Z;
        var played = new List<string>();
        float lastLegs = yaw0, mostStep = 0f, back = 0f, slide = 0f, lastTime = float.MaxValue;
        var lastBall = new Vector3[2];
        for (int t = 0; t < 6 * World.TicksPerSecond; t++)
        {
            float yaw = yaw0 + (rate <= 0f ? degrees : MathF.Min(t * Tick * rate, degrees)) * DetMath.DegToRad;
            Step(world, e, Vector3.Zero, yaw, true);
            var a = world.Get<CharacterAnimation>(e);
            var rig = world.Get<PoseRig>(e);
            // A turn begun (a playing turn re-aimed keeps its time and is the same turn).
            if (a.Role == MotionRole.Turn && (played.Count == 0 || a.Time < lastTime)) played.Add(assets.Clip(a.Clip).Name);
            lastTime = a.Role == MotionRole.Turn ? a.Time : float.MaxValue;
            // The legs' way in the world: the body's yaw and the legs' turn.
            float legs = yaw + rig.LegYaw, step = DetMath.WrapAngle(legs - lastLegs);
            if (t > 0) { mostStep = MathF.Max(mostStep, step); back = MathF.Min(back, step); }
            lastLegs = legs;
            Assert.True(Finite(world, e));
            for (int f = 0; f < 2; f++)
            {
                var b = Bone(world, e, assets, balls[f]);
                if (t > 0 && b.Z < ground + 0.0015f && lastBall[f].Z < ground + 0.0015f)
                    slide = MathF.Max(slide, MathF.Sqrt((b.X - lastBall[f].X) * (b.X - lastBall[f].X) + (b.Y - lastBall[f].Y) * (b.Y - lastBall[f].Y)) / Tick);
                lastBall[f] = b;
            }
        }
        var end = world.Get<CharacterAnimation>(e);
        return (played, end.Role, world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg, mostStep * DetMath.RadToDeg, back * DetMath.RadToDeg, slide * 100f);
    }

    [Fact]
    public void A_standing_turn_of_120_degrees_plays_the_turns_steps_round_and_ends_aligned()
    {
        // The earlier build's turn (--turn-chain-off): the legs planted to 45°, the turn clips at their own pace, the legs dragged at the 90° twist.
        var (assets, kind, _) = Player();
        foreach (var (degrees, rate) in new[] { (120f, 90f), (120f, 0f), (120f, 300f), (60f, 0f) })
        {
            var (played, end, left, most, back, slide) = Turn(assets, kind, degrees, rate, LegTurnSettings.NoChain);
            output.WriteLine($"{degrees}° {(rate > 0 ? $"at {rate}°/s" : "at once")}: turns {string.Join(", ", played)}; at the end {end}, the legs {F(left, "0.00")}° off the facing; "
                             + $"the legs' most turn in a tick {F(most, "0.00")}°, back {F(back, "0.000")}°; a planted ball's most speed {F(slide, "0.0")} cm/s");
            Assert.NotEmpty(played);
            Assert.All(played, p => Assert.Contains("turn_left", p));
            Assert.Equal(MotionRole.Idle, end);
            Assert.InRange(MathF.Abs(left), 0f, 1f);                  // the legs end on the facing
            Assert.InRange(back, -1e-3f, 0f);                         // never turned back: no turn given twice and taken back
            // Never a whole turn in a tick (the double-turn trap: the clip's turn given at its end besides): at most the view's
            // own step dragging the legs at the twist's limit, or a clip's step.
            Assert.InRange(most, 0f, MathF.Max(2.6f, rate <= 0f ? degrees - 90f + 0.01f : rate / World.TicksPerSecond + 0.01f));
            // The feet step round: a planted ball moves only by what a turn scaled to the degrees it is played for (a 90 played for 74°)
            // turns it under the body: 0.2 to 5 cm/s for a turn played as made, up to about 18 cm/s for one scaled by a sixth.
            if (rate is > 0f and <= 90f) Assert.InRange(slide, 0f, 20f);
        }
    }

    [Fact]
    public void A_standing_turn_of_120_degrees_as_a_chain_turns_once_or_twice_sped_up_and_ends_aligned()
    {
        // The default: from 30°, each turn re-aimed at the view and sped up the further the hips are behind, the legs pivoting
        // past 75°; nothing dragged by a cap. The steps then slide more than the earlier build's (a 45 stretched to 67° and played up to twice as fast).
        var (assets, kind, _) = Player();
        var s = new LegTurnSettings();
        foreach (var (degrees, rate) in new[] { (120f, 90f), (120f, 0f), (120f, 300f), (60f, 0f), (60f, 120f) })
        {
            var (played, end, left, most, back, slide) = Turn(assets, kind, degrees, rate);
            output.WriteLine($"{degrees}° {(rate > 0 ? $"at {rate}°/s" : "at once")}: turns {string.Join(", ", played)}; at the end {end}, the legs {F(left, "0.00")}° off the facing; "
                             + $"the legs' most turn in a tick {F(most, "0.00")}°, back {F(back, "0.000")}°; a planted ball's most speed {F(slide, "0.0")} cm/s");
            Assert.NotEmpty(played);
            Assert.All(played, p => Assert.Contains("turn_left", p));
            Assert.InRange(played.Count, 1, 3);
            Assert.Equal(MotionRole.Idle, end);
            Assert.InRange(MathF.Abs(left), 0f, 1f);                  // the legs end on the facing
            Assert.InRange(back, -1e-3f, 0f);                         // never turned back: no turn given twice and taken back
            // Never a whole turn in a tick: at most a clip's step sped up, and the pivot's.
            Assert.InRange(most, 0f, 2.6f * s.TurnMostSpeedup + s.PivotRate * DetMath.RadToDeg / World.TicksPerSecond + 0.01f);
            if (rate is > 0f and <= 120f) Assert.InRange(slide, 0f, 45f);
        }
    }

    [Fact]
    public void A_view_turned_to_the_right_and_back_turns_each_way_and_moving_cancels_a_turn()
    {
        var (assets, kind, _) = Player();
        float yaw0 = DetMath.HalfPi;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw0);
        for (int t = 0; t < 60; t++) Step(world, e, Vector3.Zero, yaw0, true);
        float right = yaw0 - 70f * DetMath.DegToRad;
        string? turned = null;
        for (int t = 0; t < 240; t++)
        {
            Step(world, e, Vector3.Zero, right, true);
            var a = world.Get<CharacterAnimation>(e);
            if (a.Role == MotionRole.Turn) turned ??= assets.Clip(a.Clip).Name;
        }
        output.WriteLine($"70° to the right: {turned}; the legs {F(world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg, "0.00")}° off");
        Assert.Contains("turn_right_90@-70", turned);
        Assert.InRange(MathF.Abs(world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg), 0f, 1f);

        // A turn begun, then a walk: the walk fades in from the turn and the legs come back to the facing.
        for (int t = 0; t < 20; t++) Step(world, e, Vector3.Zero, yaw0, true);
        Assert.Equal(MotionRole.Turn, world.Get<CharacterAnimation>(e).Role);
        var feet = Vector3.Zero;
        var v = new Vector3(0f, 1.25f, 0f);
        for (int t = 0; t < 60; t++) { feet += v * Tick; Step(world, e, feet, yaw0, true, velocity: v); }
        var w = world.Get<CharacterAnimation>(e);
        Assert.Equal(MotionRole.Walk, w.Role);
        Assert.Equal(0f, world.Get<PoseRig>(e).LegYaw, 3);
        Assert.True(Finite(world, e));
    }

    static Traversal Move(TraversalKind kind, float p, Vector3 from, Vector3 to, float top, bool lip = true) => new()
    {
        Kind = kind, Progress = p, Seconds = 1f, From = from, To = to, Height = top - from.Z,
        Lip = lip ? new Vector3(0, 0.6f, top) : Vector3.Zero, LipNormal = lip ? new Vector3(0, -1, 0) : Vector3.Zero,
    };

    [Fact]
    public void Every_move_jump_fall_landing_and_crouch_plays_its_stand_in_without_NaNs()
    {
        var (assets, kind, _) = Player();
        var k = assets.Kind(kind);
        var level = new TestLevel().Floor(-4, -4, 4, 4).Box(new Vector3(-3, 0.6f, 0), new Vector3(3, 2.1f, 1.2f)).Build("wall");
        var skeleton = assets.ModelOf(kind).Skeleton;
        int hand = skeleton.Find("middle_01_r");
        foreach (var move in Enum.GetValues<TraversalKind>())
        {
            if (move == TraversalKind.None) continue;
            var (world, e) = Make(assets, kind, level, new Vector3(0, 0.2f, 0));
            for (int t = 0; t < 10; t++) Step(world, e, new Vector3(0, 0.2f, 0), DetMath.HalfPi, true);
            float worst = 0f;
            for (int t = 0; t <= 120; t++)
            {
                float p = t / 120f;
                var feet = Vector3.Lerp(new Vector3(0, 0.2f, 0), new Vector3(0, 0.9f, 1.2f), p);
                Step(world, e, feet, DetMath.HalfPi, false, Move(move, p, new Vector3(0, 0.2f, 0), new Vector3(0, 0.9f, 1.2f), 1.2f));
                Assert.True(Finite(world, e), $"{move} at {p}");
                if (world.Get<PoseRig>(e).HandWeight >= 1f)
                    worst = MathF.Max(worst, Vector3.Distance(Bone(world, e, assets, hand), new Vector3(0.186f, 0.6f, 1.2f)));
            }
            var a = world.Get<CharacterAnimation>(e);
            output.WriteLine($"{move}: {a.Role} plays {assets.Clip(a.Clip).Name}{(worst > 0 ? $"; the right knuckles at most {F(worst * 100, "0.0")} cm from the lip" : "")}");
            Assert.Equal(MotionSlots.Of(move), a.Role);
            Assert.True(k.Motion(a.Role).Composed);
        }
        // Up, down, a hard landing, a crouch still and moving.
        {
            var (world, e) = Make(assets, kind, level, new Vector3(0, -2, 0));
            var feet = new Vector3(0, -2, 0);
            var roles = new HashSet<MotionRole>();
            for (int t = 0; t < 30; t++) { feet.Z += 4f * Tick; Step(world, e, feet, DetMath.HalfPi, false, velocity: new Vector3(0, 0, 4f)); roles.Add(world.Get<CharacterAnimation>(e).Role); Assert.True(Finite(world, e)); }
            for (int t = 0; t < 120; t++) { feet.Z -= 6f * Tick; Step(world, e, feet, DetMath.HalfPi, false, velocity: new Vector3(0, 0, -8f)); roles.Add(world.Get<CharacterAnimation>(e).Role); Assert.True(Finite(world, e)); }
            feet.Z = 0f;
            for (int t = 0; t < 150; t++) { Step(world, e, feet, DetMath.HalfPi, true); roles.Add(world.Get<CharacterAnimation>(e).Role); Assert.True(Finite(world, e)); }
            Assert.Equal(MotionRole.Idle, world.Get<CharacterAnimation>(e).Role);     // the landing (0.8 s) over
            for (int t = 0; t < 60; t++) { Step(world, e, feet, DetMath.HalfPi, true, crouched: 1); roles.Add(world.Get<CharacterAnimation>(e).Role); Assert.True(Finite(world, e)); }
            Assert.Equal(MotionRole.Crouch, world.Get<CharacterAnimation>(e).Role);
            var hips = Bone(world, e, assets, skeleton.Find("pelvis"));
            var ankle = Bone(world, e, assets, skeleton.Find("foot_l"));
            output.WriteLine($"crouched still: the hips {F(hips.Z, "0.00")} m up, the ankle {F(ankle.Z, "0.000")} m");
            Assert.InRange(hips.Z, 0.4f, 0.65f);
            // The ankle over the ball of a foot whose heel is up — the crouch on the balls of the feet (heels 40° up, 0.136 m),
            // or the flat crouch with the platform's heel lift (PersonFeet) — not a flat foot's 0.08 m.
            Assert.InRange(ankle.Z, 0.05f, 0.16f);
            var v = new Vector3(0, 0.8f, 0);
            for (int t = 0; t < 60; t++) { feet += v * Tick; Step(world, e, feet, DetMath.HalfPi, true, velocity: v, crouched: 1); roles.Add(world.Get<CharacterAnimation>(e).Role); Assert.True(Finite(world, e)); }
            output.WriteLine($"roles played: {string.Join(", ", roles)}");
            Assert.Superset(new HashSet<MotionRole> { MotionRole.Jump, MotionRole.Fall, MotionRole.Land, MotionRole.Crouch, MotionRole.CrouchWalk }, roles);
        }
        // The dodge, by its progress.
        {
            var anim = CharacterAnimation.For(assets, kind);
            var system = new AnimationSystem(assets);
            var poser = new PoseEvaluator();
            int n = skeleton.Count;
            var model = new Matrix4x4[n]; var skin = new Matrix4x4[n];
            for (int t = 0; t <= 60; t++)
            {
                system.Step(ref anim, new MotionInput { Grounded = true, Dodging = true, DodgeProgress = t / 60f, DodgeAside = 1f }, Tick);
                poser.Evaluate(assets, anim, model, skin);
                Assert.Equal(MotionRole.Dodge, anim.Role);
                Assert.All(skin, m => Assert.True(float.IsFinite(m.M41) && float.IsFinite(m.M11)));
            }
        }
    }

    [Fact]
    public void The_first_person_eye_is_between_the_eyes_and_the_head_collapses_alone()
    {
        var (assets, kind, person) = Player();
        var head = assets.Kind(kind).Head!;
        var skeleton = person.Model.Skeleton;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero);
        for (int t = 0; t < 120; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true);
        var skin = world.Service<CharacterPoses>().Skin(world, e);
        var eyes = head.EyesIn(skin);
        var bones = world.Service<CharacterPoses>().Bones(world, e);
        var eyeL = bones[skeleton.Find("eye_l")].Translation;
        var eyeR = bones[skeleton.Find("eye_r")].Translation;
        output.WriteLine($"standing: the eye at ({F(eyes.X)}, {F(eyes.Y)}, {F(eyes.Z)}) m, the eyes' joints at ({F(eyeL.X)}, {F(eyeL.Y)}, {F(eyeL.Z)}) and ({F(eyeR.X)}, {F(eyeR.Y)}, {F(eyeR.Z)}); at rest {F(head.Eyes.Z)} m");
        Assert.InRange(eyes.Z, 1.5f, 1.65f);
        Assert.True(Vector3.Distance(eyes, (eyeL + eyeR) * 0.5f) < 0.005f);
        Assert.True(eyes.Y > bones[skeleton.Find("head")].Translation.Y, "the eyes are ahead of the head's joint");

        // First person: the neck, the head and what hangs from it collapse; nothing else moves.
        var whole = skin.ToArray();
        var collapsed = whole.ToArray();
        head.Apply(collapsed);
        int changed = 0;
        for (int b = 0; b < whole.Length; b++)
        {
            bool hidden = head.Bones.Contains(b);
            if (!hidden) Assert.Equal(whole[b], collapsed[b]);
            else changed++;
        }
        output.WriteLine($"first person hides {changed} bones: {string.Join(", ", head.Bones.Select(b => skeleton.Bones[b].Name))}");
        foreach (var name in new[] { "neck_01", "head", "jaw", "eye_l", "eye_r" }) Assert.Contains(skeleton.Find(name), head.Bones);
        // Everything weighted to the hidden bones (the lashes, teeth, tongue, hair, beard) shrinks to the neck's joint.
        int vertices = 0;
        foreach (ref readonly var v in person.Model.Vertices.AsSpan())
        {
            bool all = true;
            for (int k = 0; k < 4; k++)
            {
                float w = k == 0 ? v.Weights.X : k == 1 ? v.Weights.Y : k == 2 ? v.Weights.Z : v.Weights.W;
                if (w > 0f && !head.Bones.Contains(v.Bone(k))) all = false;
            }
            if (!all) continue;
            vertices++;
            var p = PoseEvaluator.SkinPosition(v, collapsed);
            Assert.True(Vector3.Distance(p, PoseMath.Transform(head.Pivot, whole[head.Root])) < 0.002f);
        }
        output.WriteLine($"{vertices} vertices weighted to them alone collapse to the neck");
        Assert.True(vertices > 5000);
    }

    [Fact]
    public void Every_region_has_one_volume_fitted_from_its_bones_skin_with_sane_radii()
    {
        var (assets, kind, person) = Player();
        var body = HitVolumeFit.Fit(person.Model)!;
        var skeleton = person.Model.Skeleton;
        output.WriteLine($"{body.Table}: {body.Volumes.Length} volumes, reach {F(body.Reach, "0.00")} m");
        foreach (var v in body.Volumes)
            output.WriteLine($"  {BodyRegions.Name(v.Region),-16} on {skeleton.Bones[v.Bone].Name,-12} radius {F(v.Radius)} m, length {F(Vector3.Distance(v.A, v.B))} m, {v.Vertices} vertices");
        Assert.StartsWith(PeopleAnatomy.Name, body.Table);
        Assert.Equal(17, body.Volumes.Length);
        foreach (var region in BodyRegions.All) Assert.Single(body.Volumes, v => v.Region == region);
        float R(BodyRegion r) => body.Volumes.Single(v => v.Region == r).Radius;
        Assert.InRange(R(BodyRegion.Head), 0.06f, 0.13f);
        Assert.InRange(R(BodyRegion.Neck), 0.04f, 0.09f);
        Assert.InRange(R(BodyRegion.Chest), 0.08f, 0.2f);
        Assert.InRange(R(BodyRegion.Belly), 0.08f, 0.2f);
        Assert.InRange(R(BodyRegion.Hips), 0.08f, 0.2f);
        foreach (var r in new[] { BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm, BodyRegion.LeftForearm, BodyRegion.RightForearm }) Assert.InRange(R(r), 0.025f, 0.08f);
        foreach (var r in new[] { BodyRegion.LeftThigh, BodyRegion.RightThigh }) Assert.InRange(R(r), 0.05f, 0.12f);
        foreach (var r in new[] { BodyRegion.LeftShin, BodyRegion.RightShin }) Assert.InRange(R(r), 0.035f, 0.09f);
        foreach (var r in new[] { BodyRegion.LeftHand, BodyRegion.RightHand, BodyRegion.LeftFoot, BodyRegion.RightFoot }) Assert.InRange(R(r), 0.02f, 0.07f);
        // The left is the person's left (−X, the model facing +Y); the twist bones' skin belongs to their limb.
        var posed = new HitVolume[body.Volumes.Length];
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero);
        for (int t = 0; t < 10; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true);
        HitVolumes.Pose(body, world.Service<CharacterPoses>().Bones(world, e), Matrix4x4.Identity, 1f, posed);
        Assert.True(posed.Single(v => v.Region == BodyRegion.LeftHand).Capsule.A.X < -0.1f);
        Assert.True(posed.Single(v => v.Region == BodyRegion.RightHand).Capsule.A.X > 0.1f);
        Assert.True(posed.Single(v => v.Region == BodyRegion.Head).Capsule.A.Z > 1.4f);
        Assert.Equal(skeleton.Find("upperarm_l"), body.Volumes.Single(v => v.Region == BodyRegion.LeftUpperArm).Bone);
        Assert.Equal(skeleton.Find("lowerarm_l"), body.Volumes.Single(v => v.Region == BodyRegion.LeftForearm).Bone);
        Assert.NotNull(world.Service<CharacterPoses>().Collapse(assets.Kind(kind).Model));
        // The bend helpers' skin is their parents': the knee's the
        // thigh's, the hip's the hips'. Counted as nothing (the earlier build's table) the skin they share at a knee or a hip falls out of the fit.
        var table = AnatomyTables.For(skeleton)!;
        if (skeleton.Find("knee_bend_front_l") < 0) { output.WriteLine("no bend helpers (an older models repository)"); return; }
        foreach (var (side, thigh) in new[] { ("l", BodyRegion.LeftThigh), ("r", BodyRegion.RightThigh) })
            foreach (var which in new[] { "front", "back" })
            {
                Assert.Equal(thigh, table.Of(skeleton.Find($"knee_bend_{which}_{side}")));
                Assert.Equal(BodyRegion.Hips, table.Of(skeleton.Find($"hip_bend_{which}_{side}")));
            }
        bool Helper(int b) => skeleton.Bones[b].Name.StartsWith("knee_bend_", StringComparison.Ordinal) || skeleton.Bones[b].Name.StartsWith("hip_bend_", StringComparison.Ordinal);
        var without = new AnatomyTable
        {
            Name = table.Name, Parents = table.Parents,
            Regions = [.. table.Regions.Select((r, b) => Helper(b) ? BodyRegion.None : r)],
            Volumes = [.. table.Volumes!.Select((v, b) => Helper(b) ? -1 : v)],
        };
        var lost = HitVolumeFit.Fit(person.Model, without)!;
        int Count(FittedBody f, BodyRegion r) => f.Volumes.Single(v => v.Region == r).Vertices;
        foreach (var r in new[] { BodyRegion.LeftThigh, BodyRegion.RightThigh, BodyRegion.Hips, BodyRegion.LeftShin, BodyRegion.RightShin })
            output.WriteLine($"{BodyRegions.Name(r)}: {Count(body, r)} vertices with the helpers' skin its parents', {Count(lost, r)} without");
        Assert.True(Count(body, BodyRegion.LeftThigh) > Count(lost, BodyRegion.LeftThigh));
        Assert.True(Count(body, BodyRegion.RightThigh) > Count(lost, BodyRegion.RightThigh));
        Assert.True(Count(body, BodyRegion.Hips) > Count(lost, BodyRegion.Hips));
    }

    [Fact]
    public void Two_runs_of_the_same_moves_give_the_same_digests_and_poses()
    {
        var (assets, kind, _) = Player();
        var (a, ea) = Make(assets, kind, Floor(), Vector3.Zero);
        var (b, eb) = Make(assets, kind, Floor(), Vector3.Zero);
        var feet = Vector3.Zero;
        for (int t = 0; t < 900; t++)
        {
            // Stand, turn the view, walk round a circle (every way from the facing), run, stop.
            float yaw = DetMath.HalfPi + (t < 200 ? MathF.Min(t, 60) * 2f * DetMath.DegToRad : 120f * DetMath.DegToRad);
            var v = t < 300 ? Vector3.Zero : t < 700 ? new Vector3(DetMath.Cos(t * 0.02f), DetMath.Sin(t * 0.02f), 0f) * (t < 500 ? 1.2f : 2.9f) : Vector3.Zero;
            feet += v * Tick;
            Step(a, ea, feet, yaw, true, velocity: v);
            Step(b, eb, feet, yaw, true, velocity: v);
            Assert.Equal(a.Digest(), b.Digest());
            if (t % 50 == 0) Assert.True(a.Service<CharacterPoses>().Skin(a, ea).SequenceEqual(b.Service<CharacterPoses>().Skin(b, eb)));
        }
    }

    [Fact]
    public void The_twist_rule_gives_the_twist_bones_the_clips_carry()
    {
        var (assets, kind, person) = Player();
        var skeleton = person.Model.Skeleton;
        var rule = assets.Kind(kind).Person!.Twist!;
        var local = new BoneTransform[skeleton.Count];
        var driven = new BoneTransform[skeleton.Count];
        var lib = People.PeopleLibraryTests.Library.Value!;
        float worst = 0f;
        string at = "";
        foreach (var clip in lib.Motions("male"))
        {
            if (clip.Name is not ("walk" or "run" or "stand" or "kneel" or "turn_left_90" or "point" or "draw" or "cut_right_strike" or "pray" or "walk_back_left")) continue;
            for (float s = 0f; s <= clip.Duration; s += 1f / 15f)
            {
                clip.Sample(s, skeleton, local);
                local.CopyTo(driven, 0);
                rule.Apply(driven);
                foreach (var limb in rule.Limbs)
                    foreach (var bone in limb.Bones)
                    {
                        var q = local[bone.Index].Rotation; var r = driven[bone.Index].Rotation;
                        float dot = MathF.Min(1f, MathF.Abs(q.X * r.X + q.Y * r.Y + q.Z * r.Z + q.W * r.W));
                        float degrees = 2f * MathF.Acos(dot) * 180f / MathF.PI;
                        if (degrees > worst) { worst = degrees; at = $"{clip.Name} at {F(s, "0.00")} s, {skeleton.Bones[bone.Index].Name}"; }
                    }
            }
        }
        output.WriteLine($"the rule against the clips' own twist bones: at most {F(worst, "0.00")}° ({at})");
        Assert.InRange(worst, 0f, 1f);
    }
}
