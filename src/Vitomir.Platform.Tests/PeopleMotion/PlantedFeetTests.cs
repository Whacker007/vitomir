// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// A person's planted feet (<see cref="PersonFeet"/>), the heels-up squat and the bend rule (<see cref="BendRule"/>)
/// on the models repository's own files (skip without <c>VITOMIR_MODELS_DIR</c>): a planted ball's drift in the world through a standing
/// turn and a crouched one with and without the locks; the legs' soft limit; the feet on the ground's height; the heel lift; the bend
/// rule against the helpers the clips key; the earlier build's feet with no store.
/// </summary>
public sealed class PlantedFeetTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_bend_rule_gives_the_helpers_the_clips_carry()
    {
        var (assets, kind, person) = Player();
        var skeleton = person.Model.Skeleton;
        var rule = assets.Kind(kind).Person!.Bend;
        Assert.SkipUnless(rule is not null, "the models repository's files are too old (no bend helpers)");
        Assert.Equal(4, rule.Joints.Length);
        Assert.Equal(8, rule.Joints.Sum(j => j.Helpers.Length));
        var local = new BoneTransform[skeleton.Count];
        var driven = new BoneTransform[skeleton.Count];
        var lib = People.PeopleLibraryTests.Library.Value!;
        float worstTurn = 0f, worstMove = 0f;
        string atTurn = "", atMove = "";
        int frames = 0;
        foreach (var clip in lib.Motions("male"))
        {
            if (clip.Name is not ("stand" or "walk" or "run" or "crouch" or "crouch_walk" or "crouch_down" or "crouch_turn_left_90" or "turn_right_90"
                                  or "kneel" or "crouch_kneel" or "walk_back_left" or "guard_ready")) continue;
            for (float s = 0f; s <= clip.Duration; s += 1f / 15f)
            {
                clip.Sample(s, skeleton, local);
                local.CopyTo(driven, 0);
                rule.Apply(driven);
                frames++;
                foreach (var joint in rule.Joints)
                    foreach (var h in joint.Helpers)
                    {
                        var q = local[h.Index].Rotation; var r = driven[h.Index].Rotation;
                        float dot = MathF.Min(1f, MathF.Abs(q.X * r.X + q.Y * r.Y + q.Z * r.Z + q.W * r.W));
                        float degrees = 2f * MathF.Acos(dot) * 180f / MathF.PI;
                        float mm = (local[h.Index].Translation - driven[h.Index].Translation).Length() * 1000f;
                        string where = $"{clip.Name} at {F(s, "0.00")} s, {skeleton.Bones[h.Index].Name}";
                        if (degrees > worstTurn) (worstTurn, atTurn) = (degrees, where);
                        if (mm > worstMove) (worstMove, atMove) = (mm, where);
                    }
            }
        }
        output.WriteLine($"the rule against the clips' own bend helpers over {frames} frames: turned at most {F(worstTurn, "0.000")}° off ({atTurn}), moved at most {F(worstMove, "0.000")} mm off ({atMove})");
        Assert.InRange(worstTurn, 0f, 0.5f);
        Assert.InRange(worstMove, 0f, 0.5f);
    }

    /// <summary>
    /// The view turned <paramref name="degrees"/> to the left at <paramref name="rate"/>° a second (0: at once) after a second standing (or
    /// crouched), then held, for <paramref name="seconds"/>: the same body with the feet's rules and with the earlier build's, tick for tick (the feet do
    /// not change what plays). A ball is planted while the locked body holds it fully (this tick and the last); answers each one's mean and
    /// most speed over the ground of a planted ball (cm/s), and how many ticks were measured.
    /// </summary>
    static (float Mean, float Most, float MeanOff, float MostOff, int Ticks) Drift(CharacterAssets assets, int kind, float degrees, float rate, int crouched = 0,
                                                                                  float seconds = 5f, LegTurnSettings? legs = null)
    {
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        float yaw0 = DetMath.HalfPi;
        var (on, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw0, legs: legs);
        var (off, e14) = Make(assets, kind, Floor(), Vector3.Zero, yaw0, legs: legs, feetRules: PersonFeetSettings.Off);
        Assert.True(on.TryStore<PersonFeet>(out _));
        Assert.False(off.TryStore<PersonFeet>(out _));     // The earlier build's feet: no store, the digests as before
        int settle = World.TicksPerSecond;
        for (int t = 0; t < settle; t++) { Step(on, e, Vector3.Zero, yaw0, true, crouched: crouched); Step(off, e14, Vector3.Zero, yaw0, true, crouched: crouched); }
        var last = new Vector3[2]; var last14 = new Vector3[2];
        var held = new bool[2];
        float sum = 0f, most = 0f, sum14 = 0f, most14 = 0f;
        int n = 0;
        for (int t = 0; t < seconds * World.TicksPerSecond; t++)
        {
            float yaw = yaw0 + (rate <= 0f ? degrees : MathF.Min(t * Tick * rate, degrees)) * DetMath.DegToRad;
            Step(on, e, Vector3.Zero, yaw, true, crouched: crouched);
            Step(off, e14, Vector3.Zero, yaw, true, crouched: crouched);
            Assert.Equal(on.Get<CharacterAnimation>(e).Clip, off.Get<CharacterAnimation>(e14).Clip);
            Assert.True(Finite(on, e));
            var feet = on.Get<PersonFeet>(e);
            for (int f = 0; f < 2; f++)
            {
                var b = Bone(on, e, assets, balls[f]);
                var b14 = Bone(off, e14, assets, balls[f]);
                bool now = (f == 0 ? feet.LeftHold : feet.RightHold) >= 0.999f;
                if (t > 0 && now && held[f])
                {
                    float d = Flat(b - last[f]) / Tick * 100f, d14 = Flat(b14 - last14[f]) / Tick * 100f;
                    (sum, most, sum14, most14) = (sum + d, MathF.Max(most, d), sum14 + d14, MathF.Max(most14, d14));
                    n++;
                }
                (last[f], last14[f], held[f]) = (b, b14, now);
            }
        }
        return (n > 0 ? sum / n : 0f, most, n > 0 ? sum14 / n : 0f, most14, n);

        static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);
    }

    [Fact]
    public void A_planted_foot_stays_where_it_was_planted_through_turns_on_the_spot()
    {
        var (assets, kind, _) = Player();
        // The half turn at once with the leash off (the hips left half a turn behind, the locks' hardest case) and on (the
        // body carried round past 90°, the feet let go and taken again: held, they still hold).
        var leashOff = new LegTurnSettings { Leash = 0f };
        foreach (var (degrees, rate, crouched, legs) in new (float, float, int, LegTurnSettings?)[] { (120f, 120f, 0, null), (90f, 60f, 0, null), (180f, 0f, 0, leashOff), (180f, 0f, 0, new LegTurnSettings()),
                                                                (60f, 0f, 0, null), (120f, 120f, 1, null), (90f, 0f, 1, null) })
        {
            var (mean, most, mean14, most14, ticks) = Drift(assets, kind, degrees, rate, crouched, legs: legs);
            output.WriteLine($"{(crouched != 0 ? "crouched" : "standing")} {degrees}° {(rate > 0 ? $"at {rate}°/s" : "at once")}{(legs is { Leashed: true } ? " (the leash on)" : "")}: "
                             + $"a planted ball moves {F(mean, "0.00")} cm/s on average, {F(most, "0.0")} at most ({ticks} foot-ticks); the feet off: feet {F(mean14, "0.00")}, {F(most14, "0.0")}");
            Assert.True(ticks > 100, "the feet were planted");
            Assert.InRange(mean, 0f, 0.5f);           // held: only the legs' soft limit lets a ball go, near full stretch
            Assert.InRange(most, 0f, 20f);
            if (legs is not { Leashed: true }) Assert.True(mean14 > 2f * mean + 0.5f, "the feet off: feet slide where the locks hold them");
        }
        // The woman's the same.
        var (wa, wk, _) = Woman();
        var w = Drift(wa, wk, 120f, 120f);
        output.WriteLine($"the woman standing 120° at 120°/s: {F(w.Mean, "0.00")} cm/s, {F(w.Most, "0.0")} at most; the feet off: feet {F(w.MeanOff, "0.00")}, {F(w.MostOff, "0.0")}");
        Assert.InRange(w.Mean, 0f, 0.5f);
        Assert.InRange(w.Most, 0f, 20f);
    }

    [Fact]
    public void The_legs_soft_limit_reaches_smoothly_and_never_snaps_straight()
    {
        // The reach: all the way up to the soft limit's start, then closing on the full reach without passing it, its slope continuous.
        const float reach = 0.9f, soft = 0.97f;
        Assert.Equal(0.5f, LimbIK.SoftReach(0.5f, reach, soft));
        Assert.Equal(0.95f, LimbIK.SoftReach(0.95f, reach, 0f));      // off: as before
        float last = 0f, lastSlope = 1f;
        for (float d = 0.80f; d <= 1.4f; d += 0.001f)
        {
            float r = LimbIK.SoftReach(d, reach, soft);
            Assert.True(r <= reach, $"{d}: {r} not past the reach");
            Assert.True(r >= last, "never back");
            float slope = (LimbIK.SoftReach(d + 1e-3f, reach, soft) - r) / 1e-3f;
            Assert.True(MathF.Abs(slope - lastSlope) < 0.05f, $"{d}: the slope jumps from {lastSlope} to {slope}");
            (last, lastSlope) = (r, slope);
        }
        // On the man's leg: a foot asked a little past the soft limit is reached for a little short; asked far beyond, the leg comes
        // straight but for a sliver and the ankle stays on the line to the target; asked within it, exactly.
        var (assets, kind, person) = Player();
        var skeleton = person.Model.Skeleton;
        var leg = assets.Kind(kind).Limbs!.LeftLeg;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        assets.Clip(assets.Kind(kind).Motion(MotionRole.Idle).Clip).Sample(0f, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        var hip = model[leg.Upper].Translation;
        float full = LimbIK.Reach(model, leg);
        var down = Vector3.Normalize(model[leg.End].Translation - hip);
        foreach (float ask in new[] { 0.9f, 0.98f, 1.02f, 1.3f })
        {
            var work = (BoneTransform[])local.Clone();
            var target = hip + down * (full * ask);
            LimbIK.Solve(skeleton, work, model, leg, FootPlacement.Soft(model, leg, target, soft, 1f), 1f, new Vector3(0f, 1f, 0f));
            var m = new Matrix4x4[skeleton.Count];
            PoseEvaluator.ToModel(skeleton, work, m);
            float got = (m[leg.End].Translation - hip).Length() / full;
            output.WriteLine($"asked {F(ask, "0.00")} of the leg's reach: reached {F(got, "0.0000")}");
            if (ask <= soft) Assert.Equal(ask, got, 3);
            else Assert.InRange(got, soft - 1e-3f, MathF.Min(ask, 1f));
            Assert.True(float.IsFinite(got));
        }
    }

    /// <summary>A person stood (or crouched) a while on <paramref name="level"/> at <paramref name="feet"/> facing <paramref name="yaw"/>: its world and entity.</summary>
    static (World World, EntityId Person) Stood(CharacterAssets assets, int kind, LevelSpace level, Vector3 feet, float yaw, PersonFeetSettings? rules = null,
                                                int crouched = 0, float seconds = 2.5f)
    {
        var (world, e) = Make(assets, kind, level, feet, yaw, feetRules: rules);
        for (int t = 0; t < seconds * World.TicksPerSecond; t++) Step(world, e, feet, yaw, true, crouched: crouched);
        Assert.True(Finite(world, e));
        return (world, e);
    }

    /// <summary>The pitch of a foot (radians): the line from its ball up to its ankle above level, in the world.</summary>
    static float Pitch(Vector3 ankle, Vector3 ball)
    {
        var up = ankle - ball;
        return MathF.Atan2(up.Z, MathF.Sqrt(up.X * up.X + up.Y * up.Y));
    }

    [Fact]
    public void The_crouch_held_stands_on_the_balls_of_the_feet()
    {
        // The squat on the balls of the feet: the heel's pitch over the ball, the clip's and the lift's together, at least the lift asked
        // above the feet's standing pitch. the crouch is itself on the balls of the feet (heels 40° up), so the stopgap lift
        // adds only what the clip lacks — nothing there; on the flat crouch it lifts the heels about the balls, the toes left on
        // the ground, the ankles and knees brought along by the IK, the hips a little higher. With the lift at 0, the clip's own feet.
        var (assets, kind, person) = Player();
        Assert.SkipUnless(assets.Kind(kind).Person!.Crouch is not null, "the models repository's files are too old (no crouch)");
        var skeleton = person.Model.Skeleton;
        var rig = assets.Kind(kind).Person!.Rig;
        var rules = new PersonFeetSettings();
        float asked = rules.HeelLift * DetMath.RadToDeg;
        var (up, e) = Stood(assets, kind, Floor(), Vector3.Zero, DetMath.HalfPi, rules, crouched: 1);
        var (flat, f) = Stood(assets, kind, Floor(), Vector3.Zero, DetMath.HalfPi, rules with { HeelLift = 0f }, crouched: 1);
        Assert.Equal("crouch", assets.Clip(up.Get<CharacterAnimation>(e).Clip).Name);
        Assert.True(up.Get<PersonFeet>(e).Lift > 0f);
        Assert.Equal(0f, flat.Get<PersonFeet>(f).Lift);
        float standing = up.Get<PersonFeet>(e).StandPitch * DetMath.RadToDeg;
        foreach (var leg in new[] { rig.LeftLeg, rig.RightLeg })
        {
            Vector3 ball = Bone(up, e, assets, leg.Tip), ball0 = Bone(flat, f, assets, leg.Tip);
            Vector3 ankle = Bone(up, e, assets, leg.End), ankle0 = Bone(flat, f, assets, leg.End);
            float clip = Pitch(ankle0, ball0) * DetMath.RadToDeg - standing;           // the clip's own heel over the ball, above standing
            float total = Pitch(ankle, ball) * DetMath.RadToDeg - standing;            // with the lift
            float lifted = total - clip;
            float lacks = MathF.Max(0f, asked - clip);                                    // what the lift is to add
            // The toes as they lay: the ball's bone turned no more than a little from the flat foot's.
            var toes = up.Service<CharacterPoses>().Bones(up, e)[leg.Tip];
            var toes0 = flat.Service<CharacterPoses>().Bones(flat, f)[leg.Tip];
            float toesTurn = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(new Vector3(toes.M21, toes.M22, toes.M23)), Vector3.Normalize(new Vector3(toes0.M21, toes0.M22, toes0.M23))), -1f, 1f)) * DetMath.RadToDeg;
            output.WriteLine($"{skeleton.Bones[leg.End].Name}: the clip's heel {F(clip, "0.0")}° up, the lift added {F(lifted, "0.0")}° (asked {F(asked, "0")}°, lacking {F(lacks, "0.0")}°), "
                             + $"{F(total, "0.0")}° in all; the ball moved {F((ball - ball0).Length() * 1000f, "0.0")} mm, the ankle rose {F((ankle.Z - ankle0.Z) * 100f, "0.0")} cm, the toes turned {F(toesTurn, "0.0")}°");
            Assert.True(total >= asked - 4f, $"the heel {F(total, "0.0")}° up over the ball, at least the {F(asked, "0")}° asked");
            Assert.InRange(lifted, lacks - 4f, lacks + 4f);
            Assert.InRange((ball - ball0).Length(), 0f, 0.006f);
            if (lacks >= 10f) Assert.InRange(ankle.Z - ankle0.Z, 0.02f, 0.09f);        // a flat crouch's heels lifted
            else Assert.InRange(ankle.Z - ankle0.Z, -0.002f, 0.03f);                     // the clip's own heels up: little or nothing added
            Assert.InRange(toesTurn, 0f, 4f);
        }
        float hips = Bone(up, e, assets, rig.Pelvis).Z - Bone(flat, f, assets, rig.Pelvis).Z;
        output.WriteLine($"the hips rose {F(hips * 100f, "0.0")} cm");
        Assert.InRange(hips, -0.002f, 0.05f);
    }

    [Fact]
    public void Each_foot_stands_on_the_ground_under_it_and_the_hips_go_down_for_a_lower_one()
    {
        // The rig's ground with the feet's rules on: a step under the left foot raises it; standing at the edge of a step with the
        // right foot over the drop lowers it and the hips with it; on a 15° ramp both feet lie on the slope.
        var (assets, kind, person) = Player();
        var rig = assets.Kind(kind).Person!.Rig;
        float yaw = DetMath.HalfPi;                       // facing +Y: the left foot at −X
        var (flat, f) = Stood(assets, kind, Floor(), Vector3.Zero, yaw);
        float ballFlat = Bone(flat, f, assets, rig.LeftLeg.Tip).Z, hipsFlat = Bone(flat, f, assets, rig.Pelvis).Z;

        var stepUp = new TestLevel().Floor(-20, -20, 20, 20).Box(new Vector3(-0.6f, -0.6f, 0f), new Vector3(-0.03f, 0.6f, 0.15f)).Build("step");
        var (up, e) = Stood(assets, kind, stepUp, Vector3.Zero, yaw);
        float left = Bone(up, e, assets, rig.LeftLeg.Tip).Z, right = Bone(up, e, assets, rig.RightLeg.Tip).Z;
        output.WriteLine($"a 15 cm step under the left foot: the left ball {F(left - ballFlat)} m up, the right {F(right - ballFlat)}");
        Assert.InRange(left - ballFlat, 0.13f, 0.17f);
        Assert.InRange(right - ballFlat, -0.01f, 0.01f);

        var stepDown = new TestLevel().Floor(-20, -20, 20, 20).Box(new Vector3(-0.6f, -0.6f, 0f), new Vector3(0.03f, 0.6f, 0.15f)).Build("edge");
        var top = new Vector3(0f, 0f, 0.15f);
        var (down, d) = Stood(assets, kind, stepDown, top, yaw);
        float rightDown = Bone(down, d, assets, rig.RightLeg.Tip).Z - top.Z, hips = Bone(down, d, assets, rig.Pelvis).Z - top.Z;
        output.WriteLine($"at a 15 cm edge, the right foot over the drop: its ball {F(rightDown - ballFlat)} m, the hips {F(hips - hipsFlat)} m");
        Assert.InRange(rightDown - ballFlat, -0.17f, -0.12f);
        Assert.InRange(hips - hipsFlat, -0.17f, -0.10f);

        var ramp = new TestLevel().Floor(-20, -20, 20, 20).Ramp(-2f, 4f, -2f, 2f, 15f).Build("ramp");
        float rise = DetMath.Tan(15f * DetMath.DegToRad);
        var on = new Vector3(0f, 0f, 2f * rise);
        var (slope, s) = Stood(assets, kind, ramp, on, 0f);  // facing +X, up the ramp
        foreach (var leg in new[] { rig.LeftLeg, rig.RightLeg })
        {
            var ball = Bone(slope, s, assets, leg.Tip);
            var ankle = Bone(slope, s, assets, leg.End);
            float surfaceBall = (ball.X + 2f) * rise, surfaceAnkle = (ankle.X + 2f) * rise;
            float pitch = Pitch(ankle, ball) * DetMath.RadToDeg;
            output.WriteLine($"on a 15° ramp: the ball {F(ball.Z - surfaceBall)} m above it, the ankle {F(ankle.Z - surfaceAnkle)}, the foot's pitch {F(pitch, "0.0")}°");
            Assert.InRange(ball.Z - surfaceBall, ballFlat - 0.025f, ballFlat + 0.03f);
        }
    }

    [Fact]
    public void The_feet_s_numbers_ride_on_a_note_and_the_feet_off_have_none()
    {
        var plain = new PersonFeetSettings();
        Assert.Equal(plain, plain.WithNote(plain.Note()));
        var tuned = plain with { LockSeconds = 0.15f, Replant = 0.3f, HeelLift = 20f * DetMath.DegToRad, Bend = false };
        var back = new PersonFeetSettings().WithNote(tuned.Note());
        Assert.Equal(tuned.Note(), back.Note());
        Assert.Equal(0.15f, back.LockSeconds);
        Assert.False(back.Bend);
        Assert.Equal("off", PersonFeetSettings.Off.Note());
        Assert.Equal(PersonFeetSettings.Off, plain.WithNote(null));      // an older tape: the earlier build's feet
        Assert.Equal(PersonFeetSettings.Off, plain.WithNote("off"));
        Assert.False(PersonFeetSettings.Off.Any);
        // The earlier build's feet but the bend rule: a world with the store, no locks, no lift.
        var bendOnly = PersonFeetSettings.Off with { Bend = true };
        Assert.True(bendOnly.Any);
        Assert.Equal(bendOnly, plain.WithNote(bendOnly.Note()));
    }
}
