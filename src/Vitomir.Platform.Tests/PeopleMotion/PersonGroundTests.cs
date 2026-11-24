// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// A person on stairs and slopes (<see cref="PersonGround"/>, <see cref="PersonGroundSystem"/>, <see cref="PersonSlopes"/>). On a stair
/// built here (0.20 m risers on 0.30 m treads) with the body's capsule stepping each riser in a tick as the motor steps it: the drawn hips'
/// vertical acceleration is bounded (the earlier build's follows each riser) and the drawn body comes back onto the capsule when it stands; a world with
/// The earlier build's rules has no store. The line's spring follows a steady climb with no lag. On the models repository's files (skip without
/// <c>VITOMIR_MODELS_DIR</c>, and): the slope clips are found, their share follows the grade (0 flat, 1 at 15° and steeper,
/// none sideways), the stride blends, and a set without them is the flat gait's.
/// </summary>
public sealed class PersonGroundTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Riser = 0.20f, Tread = 0.30f, StairX = 2f, Radius = 0.3f;
    const int Risers = 6;

    /// <summary>A floor with a flight of <see cref="Risers"/> risers rising along +X from <see cref="StairX"/>, a landing 2 m deep at its top.</summary>
    static LevelSpace Stairs()
    {
        var level = new TestLevel().Floor(-20, -20, 20, 20);
        for (int k = 1; k <= Risers; k++)
            level.Box(new Vector3(StairX + (k - 1) * Tread, -1f, 0f), new Vector3(StairX + Risers * Tread + 2f, 1f, k * Riser));
        return level.Build("stairs");
    }

    /// <summary>The capsule's height at <paramref name="x"/> going up: a riser stepped as soon as the capsule's front meets it, in a tick.</summary>
    static float CapsuleUp(float x)
    {
        int k = (int)MathF.Floor((x + Radius - StairX) / Tread) + 1;
        return Math.Clamp(k, 0, Risers) * Riser;
    }

    /// <summary>
    /// The made-up person walked up the stair at 1.3 m/s for <paramref name="seconds"/>, then standing for <paramref name="rest"/>: the drawn
    /// pelvis's height every tick, the capsule's, and the world.
    /// </summary>
    static (List<float> Pelvis, List<float> Capsule, World World, EntityId Person) Climb(PersonGroundSettings rules, float seconds = 4f, float rest = 1.5f)
    {
        var (assets, kind, person) = PeopleDoubles.Kind();
        var (world, e) = Make(assets, kind, Stairs(), Vector3.Zero, 0f, groundRules: rules);
        var pelvis = new List<float>();
        var capsule = new List<float>();
        float x = 0f, speed = 1.3f;
        int walk = (int)(seconds / Tick), stand = (int)(rest / Tick);
        for (int t = 0; t < walk + stand; t++)
        {
            bool moving = t < walk;
            if (moving) x += speed * Tick;
            var feet = new Vector3(x, 0f, CapsuleUp(x));
            Step(world, e, feet, 0f, true, velocity: moving ? new Vector3(speed, 0f, 0f) : Vector3.Zero);
            pelvis.Add(Bone(world, e, assets, person.Rig.Pelvis).Z);
            capsule.Add(feet.Z);
        }
        return (pelvis, capsule, world, e);
    }

    /// <summary>The largest vertical acceleration of a height sampled every tick (m/s²), over [<paramref name="from"/>, <paramref name="to"/>).</summary>
    static float MostAcceleration(List<float> z, int from, int to)
    {
        float most = 0f;
        for (int i = Math.Max(1, from); i < Math.Min(z.Count - 1, to); i++)
            most = MathF.Max(most, MathF.Abs(z[i + 1] - 2f * z[i] + z[i - 1]) / (Tick * Tick));
        return most;
    }

    [Fact]
    public void Up_a_stair_the_drawn_hips_ride_a_smoothed_line_and_come_back_onto_the_capsule_standing()
    {
        var (pelvis, capsule, world, e) = Climb(new PersonGroundSettings());
        var (old, oldCapsule, oldWorld, _) = Climb(PersonGroundSettings.Off);
        Assert.Equal(capsule, oldCapsule);   // the same capsule (the test's own steps) under both
        int first = capsule.FindIndex(z => z > 0f), last = capsule.FindIndex(z => z >= Risers * Riser - 1e-4f);
        Assert.True(first > 0 && last > first, $"the flight is climbed between ticks {first} and {last}");
        // Over the climb and a second after the last riser: a riser in a tick is not drawn.
        float smooth = MostAcceleration(pelvis, first - 60, last + 120), steps = MostAcceleration(old, first - 60, last + 120);
        float jump = 0f, oldJump = 0f;
        for (int i = first - 60; i < last + 120; i++) { jump = MathF.Max(jump, MathF.Abs(pelvis[i + 1] - pelvis[i])); oldJump = MathF.Max(oldJump, MathF.Abs(old[i + 1] - old[i])); }
        output.WriteLine($"the drawn pelvis up 0.20 m risers at 1.3 m/s: most vertical acceleration {F(smooth)} m/s² (the rules off {F(steps)}), most move in a tick {F(jump * 1000f)} mm (the rules off {F(oldJump * 1000f)})");
        Assert.InRange(smooth, 0f, 150f);
        Assert.True(steps > 1000f, $"the rules off drawn hips step each riser ({F(steps)} m/s²)");
        Assert.InRange(jump, 0f, 0.02f);
        // Standing at the top, the drawn body is back on its capsule: the line at nothing, the pelvis as before standing there.
        var ground = world.Get<PersonGround>(e);
        output.WriteLine($"standing 1.5 s at the top: the line {F(ground.Line * 1000f, "0.00")} mm, the pelvis {F((pelvis[^1] - old[^1]) * 1000f, "0.00")} mm from the rules off");
        Assert.InRange(ground.Line, -0.002f, 0.002f);
        Assert.InRange(pelvis[^1] - old[^1], -0.01f, 0.01f);
        Assert.False(oldWorld.TryStore<PersonGround>(out _));   // The earlier build's rules: no store, the earlier build's digests
        Assert.True(world.TryStore<PersonGround>(out _));
    }

    [Fact]
    public void The_line_follows_a_steady_climb_with_no_lag_and_a_step_smoothly()
    {
        // A goal climbing at 0.87 m/s (a 0.20 m stair walked at 1.3 m/s): the line (in the world) catches it and stays on it.
        float x = 0f, v = 0f, goal = 0f, rate = 0.87f;
        for (int t = 0; t < 240; t++) { goal += rate * Tick; PersonGroundSystem.Follow(ref x, ref v, goal, rate, 0.1f, Tick); }
        Assert.InRange(x - goal, -1e-3f, 1e-3f);
        Assert.InRange(v - rate, -1e-3f, 1e-3f);
        // A step of the goal (0.2 m at once) is followed smoothly: bounded acceleration, there within a second, no overshoot to speak of.
        x = v = 0f;
        float last = 0f, lastV = 0f, most = 0f, over = 0f;
        for (int t = 0; t < 120; t++)
        {
            PersonGroundSystem.Follow(ref x, ref v, 0.2f, 0f, 0.1f, Tick);
            if (t > 0) most = MathF.Max(most, MathF.Abs(v - lastV) / Tick);
            over = MathF.Max(over, x - 0.2f);
            (last, lastV) = (x, v);
        }
        Assert.InRange(last, 0.199f, 0.2001f);
        Assert.InRange(most, 0f, 60f);
        Assert.InRange(over, -1f, 1e-4f);
        // With none, at once.
        x = v = 0f;
        PersonGroundSystem.Follow(ref x, ref v, 0.3f, 0.5f, 0f, Tick);
        Assert.Equal((0.3f, 0.5f), (x, v));
    }

    [Fact]
    public void The_rules_go_through_a_tape_s_note_and_none_is_the_rules_off()
    {
        var tuned = new PersonGroundSettings { LineHalfLife = 0.13f, Window = 0.8f, Slopes = false, FullGrade = 12f * DetMath.DegToRad };
        var back = new PersonGroundSettings().WithNote(tuned.Note());
        Assert.Equal(tuned.Note(), back.Note());
        Assert.False(back.Slopes);
        Assert.Equal(0.13f, back.LineHalfLife);
        Assert.Equal(PersonGroundSettings.Off, new PersonGroundSettings().WithNote(null));
        Assert.Equal(PersonGroundSettings.Off, new PersonGroundSettings().WithNote("off"));
        Assert.Equal("off", PersonGroundSettings.Off.Note());
        Assert.False(PersonGroundSettings.Off.Any);
    }

    [Fact]
    public void A_gait_takes_the_slope_clip_ahead_and_none_sideways_or_back()
    {
        Assert.Equal(1f, PersonSlopes.Forward(0));
        Assert.Equal(1f, PersonSlopes.Forward(30));
        Assert.Equal(1f, PersonSlopes.Forward(330));
        Assert.InRange(PersonSlopes.Forward(52), 0.3f, 0.7f);
        Assert.Equal(0f, PersonSlopes.Forward(75));
        Assert.Equal(0f, PersonSlopes.Forward(90));
        Assert.Equal(0f, PersonSlopes.Forward(180));
        Assert.Equal(0f, PersonSlopes.Forward(270));
        Assert.Equal(0f, PersonSlopes.Forward(Animation.People.Gait.Bins + 3));   // an alternate (by the backpedal's line)
    }

    [Fact]
    public void A_set_without_slope_clips_walks_the_flat_gait_on_a_slope()
    {
        var (assets, kind, _) = PeopleDoubles.Kind();
        var person = assets.Kind(kind).Person!;
        Assert.Null(person.Slopes);   // the made-up set has none (as an older models repository)
        Assert.Null(person.Stairs);
        int ahead = person.Walk!.First;
        var skeleton = assets.ModelOf(kind).Skeleton;
        var flat = new BoneTransform[skeleton.Count];
        var laid = new BoneTransform[skeleton.Count];
        assets.Clip(ahead).Sample(0.3f, skeleton, flat);
        flat.CopyTo(laid, 0);
        PersonSlopes.Lay(assets, person, ahead, 0.3f, 1f, skeleton, laid);
        Assert.Equal(flat, laid);
    }

    // ---- on the models repository's files ----

    static PersonSlopes Slopes(CharacterAssets assets, int kind)
    {
        var slopes = assets.Kind(kind).Person!.Slopes;
        Assert.SkipUnless(slopes is not null, "the models repository's files are too old (no slope clips)");
        return slopes!;
    }

    [Fact]
    public void Round_34_s_slope_clips_are_found_and_blend_the_stride()
    {
        var (assets, kind, _) = Player();
        var slopes = Slopes(assets, kind);
        Assert.All(new[] { slopes.WalkUp, slopes.WalkDown, slopes.RunUp, slopes.RunDown }, c => Assert.True(c >= 0));
        Assert.Equal(15f, slopes.Grade * DetMath.RadToDeg, 3);
        var person = assets.Kind(kind).Person!;
        int walkAhead = person.Walk!.First, runAhead = person.Run!.First;
        Assert.Equal((slopes.WalkUp, 1f), slopes.For(person, walkAhead, 1f));
        Assert.Equal((slopes.WalkDown, 0.5f), slopes.For(person, walkAhead, -0.5f));
        Assert.Equal((slopes.RunUp, 1f), slopes.For(person, runAhead, 2f));   // held to 1
        Assert.Equal((-1, 0f), slopes.For(person, walkAhead + 90, 1f));        // walking to the left: the flat gait
        Assert.Equal((-1, 0f), slopes.For(person, walkAhead, 0f));             // flat ground
        Assert.Equal((-1, 0f), slopes.For(person, person.Sprint, 1f));         // the sprint: its own clip on any ground
        // The stride blended: the walk ahead's own speed at none, the uphill walk's stride over the walk's cycle at all.
        var blend = (GaitBlendClip)assets.Clip(walkAhead);
        var up = (INotedClip)assets.Clip(slopes.WalkUp);
        Assert.Equal(blend.NaturalSpeed, PersonSlopes.NaturalSpeed(blend, up, 0f));
        float whole = up.Notes.GroundSpeed * up.Duration / blend.Duration;
        Assert.Equal(whole, PersonSlopes.NaturalSpeed(blend, up, 1f), 4);
        Assert.Equal((blend.NaturalSpeed + whole) * 0.5f, PersonSlopes.NaturalSpeed(blend, up, 0.5f), 4);
        output.WriteLine($"the walk ahead is drawn for {F(blend.NaturalSpeed, "0.000")} m/s; with walk_uphill whole, {F(whole, "0.000")} m/s on its cycle");
    }

    /// <summary>
    /// The player walked at 1.3 m/s along <paramref name="way"/> on a ramp rising along +X at <paramref name="degrees"/> (its ground's normal as
    /// the motor finds it), for a second: the slope clips' share and the grade at the end.
    /// </summary>
    static (float Slope, float Grade) OnRamp(float degrees, Vector2 way, float yaw)
    {
        var (assets, kind, _) = Player();
        var level = new TestLevel().Floor(-20, -20, 20, 20).Ramp(-10f, 30f, -10f, 10f, MathF.Max(degrees, 0.01f)).Build("ramp");
        var (world, e) = Make(assets, kind, level, Vector3.Zero, yaw);
        float t = DetMath.Tan(degrees * DetMath.DegToRad);
        var n = Vector3.Normalize(new Vector3(-t, 0f, 1f));
        var at = new Vector2(0f, 0f);
        for (int i = 0; i < (int)(1f / Tick); i++)
        {
            at += way * 1.3f * Tick;
            world.Get<Body>(e).GroundNormal = n;
            Step(world, e, new Vector3(at.X, at.Y, (at.X + 10f) * t), yaw, true, velocity: new Vector3(way.X * 1.3f, way.Y * 1.3f, way.X * 1.3f * t));
        }
        var g = world.Get<PersonGround>(e);
        return (g.Slope, g.Grade * DetMath.RadToDeg);
    }

    [Fact]
    public void The_slope_clips_share_follows_the_grade_along_the_way()
    {
        var (assets, kind, _) = Player();
        Slopes(assets, kind);
        var east = new Vector2(1f, 0f);
        var results = new List<string>();
        foreach (float degrees in new[] { 0f, 7.5f, 15f, 25f })
        {
            var (up, gradeUp) = OnRamp(degrees, east, 0f);
            var (down, gradeDown) = OnRamp(degrees, -east, DetMath.Pi);
            var (across, _) = OnRamp(degrees, new Vector2(0f, 1f), DetMath.HalfPi);
            results.Add($"{degrees}°: up {F(up, "0.00")} ({F(gradeUp)}°), down {F(down, "0.00")} ({F(gradeDown)}°), across {F(across, "0.00")}");
            float want = MathF.Min(1f, degrees / 15f);
            Assert.InRange(up, want - 0.02f, want + 0.02f);
            Assert.InRange(down, -want - 0.02f, -want + 0.02f);
            Assert.InRange(across, -0.02f, 0.02f);
            Assert.InRange(gradeUp, degrees - 0.5f, degrees + 0.5f);
        }
        output.WriteLine(string.Join("\n", results));
    }

    [Fact]
    public void Up_a_15_degree_ramp_the_walk_is_the_uphill_clip_and_the_feet_on_the_ramp()
    {
        var (assets, kind, person) = Player();
        Slopes(assets, kind);
        float t = DetMath.Tan(15f * DetMath.DegToRad);
        var level = new TestLevel().Floor(-20, -20, 20, 20).Ramp(-10f, 30f, -10f, 10f, 15f).Build("ramp");
        var (on, e) = Make(assets, kind, level, new Vector3(0f, 0f, 10f * t), 0f);
        var (off, e14) = Make(assets, kind, level, new Vector3(0f, 0f, 10f * t), 0f, groundRules: PersonGroundSettings.Off);
        var n = Vector3.Normalize(new Vector3(-t, 0f, 1f));
        int[] balls = [person.Model.Skeleton.Find("ball_l"), person.Model.Skeleton.Find("ball_r")];
        float worst = 0f, worst14 = 0f, hips = 0f, hips14 = 0f;
        float x = 0f;
        for (int i = 0; i < (int)(3f / Tick); i++)
        {
            x += 1.3f * Tick;
            var feet = new Vector3(x, 0f, (x + 10f) * t);
            foreach (var (w, who) in new[] { (on, e), (off, e14) })
            {
                w.Get<Body>(who).GroundNormal = n;
                Step(w, who, feet, 0f, true, velocity: new Vector3(1.3f, 0f, 1.3f * t));
            }
            if (i < (int)(1f / Tick)) continue;
            foreach (int b in balls)
            {
                // How far each ball is into the ramp (positive: below its face).
                var p = Bone(on, e, assets, b);
                worst = MathF.Max(worst, (p.X + 10f) * t - p.Z);
                var q = Bone(off, e14, assets, b);
                worst14 = MathF.Max(worst14, (q.X + 10f) * t - q.Z);
            }
            hips = MathF.Min(hips, on.Get<PoseRig>(e).HipDrop);
            hips14 = MathF.Min(hips14, off.Get<PoseRig>(e14).HipDrop);
        }
        var anim = on.Get<CharacterAnimation>(e);
        Assert.Equal(1f, on.Get<PersonGround>(e).Slope, 2);
        Assert.True(person.Model.Skeleton.Count > 0 && assets.Clip(anim.Clip) is GaitBlendClip, assets.Clip(anim.Clip).Name);
        output.WriteLine($"up 15° at 1.3 m/s: a ball at most {F(worst * 1000f)} mm into the ramp (the rules off {F(worst14 * 1000f)}), the hips dropped at most {F(-hips * 1000f)} mm (the rules off {F(-hips14 * 1000f)})");
        Assert.InRange(worst, -1f, 0.03f);
        Assert.InRange(-hips, 0f, 0.03f);   // the clip climbs: the hips need not drop for the lower foot
    }
}
