// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The people in motion on a made-up person (<see cref="PeopleDoubles"/>, no files needed): the kind's roles,
/// limbs, head and twist; the eight directions blended by angle and in step, not blended across the backpedal; turns on the
/// spot played in place with the root's turn given to the legs; the twist rule; one hit volume a region from all its bones'
/// skin; the stand-ins; the same digests twice.
/// </summary>
public sealed class PeopleDoubleTests
{
    [Fact]
    public void A_person_registers_with_its_roles_limbs_head_eyes_and_twist()
    {
        var (assets, k, person) = PeopleDoubles.Kind();
        var kind = assets.Kind(k);
        var s = person.Model.Skeleton;
        Assert.Equal(78, s.Count);
        Assert.Equal("stand", assets.Clip(kind.Motion(MotionRole.Idle).Clip).Name);
        Assert.Equal("double:walk@0", assets.Clip(kind.Motion(MotionRole.Walk).Clip).Name);
        Assert.Equal("double:run@0", assets.Clip(kind.Motion(MotionRole.Run).Clip).Name);
        Assert.Equal(1.25f, kind.Motion(MotionRole.Walk).NaturalSpeed, 4);
        Assert.Equal(3.0f, kind.Motion(MotionRole.Run).NaturalSpeed, 4);
        Assert.True(kind.Motion(MotionRole.CrouchWalk).Composed);
        Assert.Equal(kind.Motion(MotionRole.Walk).Clip, kind.Motion(MotionRole.CrouchWalk).Clip);
        Assert.Null(kind.Actions);
        Assert.Equal(s.Find("pelvis"), kind.Limbs!.Root);
        Assert.True(kind.Limbs.RaiseOnly);
        Assert.Equal(0.36f, kind.Limbs.ShoulderWidth, 3);
        Assert.Equal(0.06f, kind.Limbs.AnkleHeight, 3);
        Assert.Equal(s.Find("neck_01"), kind.Head!.Neck);
        Assert.Equal(new Vector3(0f, 0.08f, 1.65f), kind.Head.Eyes, new Vector3Comparer(1e-5f));
        Assert.Equal(6, kind.Person!.Twist!.Limbs.Length);
        // Every clip of the set first, in its order, then the platform's own.
        var set = PeopleDoubles.Motions(s);
        for (int i = 0; i < set.Count; i++) Assert.Equal(set[i].Name, assets.Clip(i).Name);
        Assert.Throws<ArgumentException>(() => PeopleKinds.Register(new CharacterAssets(), new Assets.People.PersonModel(person.Recipe,
            TraversalPoseTests.Person(), Assets.People.PersonRig.From(TraversalPoseTests.Person().Skeleton), []), set, "not a person"));
    }

    sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }

    static MotionInput Moving(float degrees, float speed)
    {
        float r = degrees * DetMath.DegToRad;
        return new MotionInput { Grounded = true, GroundSpeed = speed, ForwardSpeed = speed * DetMath.Cos(r), SideSpeed = speed * DetMath.Sin(r) };
    }

    [Fact]
    public void Every_way_plays_the_two_clips_either_side_blended_by_angle_in_step_at_the_blends_rate()
    {
        var (assets, k, person) = PeopleDoubles.Kind();
        var gait = assets.Kind(k).Person!.Walk!;
        var system = new AnimationSystem(assets);
        var a = CharacterAnimation.For(assets, k);
        foreach (float degrees in new[] { 0f, 20f, 45f, 60f, 90f, 150f, 180f, -170f, -160f, -45f, -20f })
        {
            for (int t = 0; t < 40; t++) system.Step(ref a, Moving(degrees, 1.0f), Tick);
            int bin = ((int)MathF.Round(degrees) % 360 + 360) % 360;
            Assert.Equal(MotionRole.Walk, a.Role);
            Assert.Equal(gait.First + bin, a.Clip);
            var blend = (GaitBlendClip)assets.Clip(a.Clip);
            Assert.Equal(1.0f / blend.NaturalSpeed, a.Rate, 4);     // the blend's stride on the ground, never backwards
            // The two either side, by angle.
            float from = blend.From.Notes.HeadingDeg, to = blend.To.Notes.HeadingDeg;
            if (blend.Weight > 0f)
            {
                float span = DetMath.WrapAngle((to - from) * DetMath.DegToRad) * DetMath.RadToDeg;
                float off = DetMath.WrapAngle((degrees - from) * DetMath.DegToRad) * DetMath.RadToDeg;
                Assert.Equal(off / span, blend.Weight, 3);
            }
            else Assert.Equal(degrees, from, 3);
        }
        // In step: at a share of the cycle each half is at that share of its own (the jaw's turn is the share).
        var mix = (GaitBlendClip)assets.Clip(gait.First + 160);
        Assert.NotEqual(mix.From.Duration, mix.To.Duration);
        var skeleton = person.Model.Skeleton;
        var local = new BoneTransform[skeleton.Count];
        int jaw = skeleton.Find("jaw");
        foreach (float share in new[] { 0.1f, 0.37f, 0.8f })
        {
            mix.Sample(share * mix.Duration, skeleton, local);
            float angle = 2f * MathF.Atan2(local[jaw].Rotation.X, local[jaw].Rotation.W);
            Assert.Equal(share, angle, 3);
        }
        // From one degree to the next no fade; the cycle carried on at its share.
        a = CharacterAnimation.For(assets, k);
        for (int t = 0; t < 60; t++) system.Step(ref a, Moving(10f, 1f), Tick);
        float before = a.Time / assets.Clip(a.Clip).Duration;
        system.Step(ref a, Moving(30f, 1f), Tick);
        Assert.Equal(gait.First + 30, a.Clip);
        Assert.Equal(-1, a.PreviousClip);
        Assert.Equal(before + Tick * a.Rate / assets.Clip(a.Clip).Duration, a.Time / assets.Clip(a.Clip).Duration, 3);
    }

    [Fact]
    public void Between_stepping_ahead_and_backpedalling_the_nearer_plays_alone_and_the_legs_take_the_rest()
    {
        var (assets, k, _) = PeopleDoubles.Kind();
        var gait = assets.Kind(k).Person!.Walk!;
        var clip = (int degrees) => (GaitBlendClip)assets.Clip(gait.First + degrees);
        Assert.Equal(("walk_left", 0f, 10f), (clip(100).From.Name, clip(100).Weight, clip(100).Turn * DetMath.RadToDeg), new TupleComparer());
        Assert.Equal(("walk_back_left", 0f, -10f), (clip(125).From.Name, clip(125).Weight, clip(125).Turn * DetMath.RadToDeg), new TupleComparer());
        Assert.Equal(("walk_back_right", 0f, 10f), (clip(235).From.Name, clip(235).Weight, clip(235).Turn * DetMath.RadToDeg), new TupleComparer());
        Assert.True(clip(160).Weight > 0f);      // two backpedals blend

        // A body moving 100° from its facing: the left walk alone, the legs 10° further round; past the halfway line the left walk
        // is kept within its band (118°), and beyond it the back-left one fades in, the legs turned back.
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero);
        var feet = Vector3.Zero;
        void Go(float degrees, int ticks)
        {
            float way = DetMath.HalfPi + degrees * DetMath.DegToRad;
            var v = new Vector3(DetMath.Cos(way), DetMath.Sin(way), 0f) * 1.1f;
            for (int t = 0; t < ticks; t++) { feet += v * Tick; Step(world, e, feet, DetMath.HalfPi, true, velocity: v); }
        }
        Go(100f, 60);
        Assert.Equal("walk_left", ((GaitBlendClip)assets.Clip(world.Get<CharacterAnimation>(e).Clip)).From.Name);
        Assert.Equal(10f, world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg, 2);
        Go(117f, 30);
        var a = world.Get<CharacterAnimation>(e);
        Assert.Equal("double:walk@117'", assets.Clip(a.Clip).Name);
        Assert.Equal(27f, world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg, 2);
        Go(122f, 1);
        a = world.Get<CharacterAnimation>(e);
        Assert.Equal("double:walk@122", assets.Clip(a.Clip).Name);
        Assert.True(a.PreviousClip >= 0 && a.Blend < 1f, "the backpedal fades in");
        Go(122f, 60);
        Assert.Equal(-13f, world.Get<PoseRig>(e).LegYaw * DetMath.RadToDeg, 2);
        Go(110f, 30);
        Assert.Equal("double:walk@110'", assets.Clip(world.Get<CharacterAnimation>(e).Clip).Name);
    }

    sealed class TupleComparer : IEqualityComparer<(string, float, float)>
    {
        public bool Equals((string, float, float) a, (string, float, float) b) => a.Item1 == b.Item1 && MathF.Abs(a.Item2 - b.Item2) < 1e-4f && MathF.Abs(a.Item3 - b.Item3) < 1e-3f;
        public int GetHashCode((string, float, float) v) => 0;
    }

    [Fact]
    public void A_standing_turn_plays_in_place_and_gives_its_roots_turn_to_the_legs_once()
    {
        var (assets, k, person) = PeopleDoubles.Kind();
        var skeleton = person.Model.Skeleton;
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero);
        for (int t = 0; t < 30; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true);
        float yaw = DetMath.HalfPi + 70f * DetMath.DegToRad, lastLegs = DetMath.HalfPi;
        var names = new List<string>();
        float given = 0f;
        for (int t = 0; t < 300; t++)
        {
            Step(world, e, Vector3.Zero, yaw, true);
            var a = world.Get<CharacterAnimation>(e);
            if (a.Role == MotionRole.Turn && !names.Contains(assets.Clip(a.Clip).Name)) names.Add(assets.Clip(a.Clip).Name);
            // Drawn in place: the root bone is turned by the legs' turn alone, never by the clip's own turn as well.
            var root = world.Service<CharacterPoses>().Bones(world, e)[skeleton.Find("root")];
            Assert.Equal(world.Get<PoseRig>(e).LegYaw, MathF.Atan2(root.M12, root.M11), 4);
            float legs = yaw + world.Get<PoseRig>(e).LegYaw;
            float step = DetMath.WrapAngle(legs - lastLegs);
            Assert.InRange(step, -1e-5f, 3f * DetMath.DegToRad);
            given += step;
            lastLegs = legs;
        }
        Assert.Equal(["double:turn_left_90@70"], names);
        Assert.Equal(MotionRole.Idle, world.Get<CharacterAnimation>(e).Role);
        Assert.Equal(0f, world.Get<PoseRig>(e).LegYaw, 4);
        Assert.Equal(70f, given * DetMath.RadToDeg, 2);     // the whole turn given once
        // The turn as played: the 90's root turn scaled to 70°, nothing at its end.
        var turn = (TurnClip)assets.Clip(assets.Kind(k).Person!.Turns!.Clip(70f * DetMath.DegToRad));
        Assert.Equal(70f, turn.Yaw(turn.Duration) * DetMath.RadToDeg, 3);
        Assert.Equal(turn.Yaw(turn.Duration), turn.Yaw(turn.Duration + 1f));
        Assert.Equal(0f, turn.Yaw(0f));
    }

    [Fact]
    public void The_twist_rule_turns_each_twist_bone_by_its_place()
    {
        var (assets, k, person) = PeopleDoubles.Kind();
        var skeleton = person.Model.Skeleton;
        var rule = assets.Kind(k).Person!.Twist!;
        var local = new BoneTransform[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        int upper = skeleton.Find("upperarm_l"), lower = skeleton.Find("lowerarm_l"), hand = skeleton.Find("hand_l");
        // The upper arm swung forward 40° and rolled 50° about its own length (−Z at rest); the forearm rolled 30°, the hand 20° about it.
        var axis = -Vector3.UnitZ;
        float t = 50f * DetMath.DegToRad, tf = 30f * DetMath.DegToRad, th = 20f * DetMath.DegToRad;
        local[upper] = local[upper] with { Rotation = PoseMath.Multiply(DetMath.AxisAngle(Vector3.UnitX, 0.7f), DetMath.AxisAngle(axis, t)) };
        local[lower] = local[lower] with { Rotation = DetMath.AxisAngle(axis, tf) };
        local[hand] = local[hand] with { Rotation = PoseMath.Multiply(DetMath.AxisAngle(Vector3.UnitY, 0.3f), DetMath.AxisAngle(axis, th)) };
        rule.Apply(local);
        float Angle(string bone) => TwistRule.Twist(local[skeleton.Find(bone)].Rotation, axis);
        Assert.Equal(-t, Angle("upperarm_twist_01_l"), 4);              // at the shoulder: (0 − 1) T
        Assert.Equal(-0.5f * t, Angle("upperarm_twist_03_l"), 4);       // halfway: (0.5 − 1) T
        Assert.Equal(-tf, Angle("lowerarm_twist_01_l"), 4);             // at the elbow: −T
        Assert.Equal(0.5f * (tf + th) - tf, Angle("lowerarm_twist_03_l"), 3);
        Assert.Equal(th, Angle("lowerarm_twist_05_l"), 3);              // at the wrist: the hand's own roll
        // Only a turn about the limb's own length: a twist bone stays on its line.
        var q = local[skeleton.Find("upperarm_twist_02_l")].Rotation;
        Assert.Equal(0f, q.X * q.X + q.Y * q.Y, 6);
    }

    [Fact]
    public void Each_region_has_one_volume_fitted_from_the_skin_of_all_its_bones()
    {
        var (assets, k, person) = PeopleDoubles.Kind();
        var model = person.Model;
        var table = AnatomyTables.For(model.Skeleton)!;
        Assert.StartsWith(PeopleAnatomy.Name, table.Name);
        Assert.Equal(BodyRegion.LeftUpperArm, table.Of(model.Skeleton.Find("upperarm_twist_03_l")));
        Assert.Equal(BodyRegion.LeftHand, table.Of(model.Skeleton.Find("pinky_02_l")));
        Assert.Equal(BodyRegion.None, table.Of(model.Skeleton.Find("root")));
        var body = HitVolumeFit.Fit(model)!;
        foreach (var region in BodyRegions.All) Assert.Single(body.Volumes, v => v.Region == region);
        // The upper arm's skin has no bone with half a vertex: only merged is it fitted at all.
        var arm = body.Volumes.Single(v => v.Region == BodyRegion.LeftUpperArm);
        Assert.Equal(model.Skeleton.Find("upperarm_l"), arm.Bone);
        Assert.Equal(40, arm.Vertices);
        Assert.Equal(0.045f, arm.Radius, 3);
        Assert.Equal(model.Skeleton.Find("lowerarm_l"), body.Volumes.Single(v => v.Region == BodyRegion.LeftForearm).Bone);
        // 1.1's tables are one volume a bone, as ever.
        Assert.Null(AnatomyTables.OneOneMan.Volumes);
        Assert.Null(AnatomyTables.For(TraversalPoseTests.Person().Skeleton));
        _ = assets; _ = k;
    }

    /// <summary>
    /// The double with the eight bend helpers after its 78
    /// (the knee's children of the thigh, the hip's of the pelvis, at the lower bone's joint) and a ring of skin at each knee and each
    /// hip shared with them as the models repository shares it: at the knee 0.35 on its front helper, 0.2 on the thigh and 0.45 on the
    /// calf; at the hip 0.4 on its front helper, 0.2 on the pelvis and 0.4 on the thigh.
    /// </summary>
    static SkinnedModel WithBendHelpers(SkinnedModel model)
    {
        var s = model.Skeleton;
        var bones = s.Bones.ToList();
        var binds = s.InverseBind.ToList();
        var at = new List<Vector3>();
        for (int b = 0; b < s.Count; b++) at.Add(-s.InverseBind[b].Translation);   // the double's binds are translations
        foreach (var (name, parent, joint) in new[]
                 {
                     ("knee_bend_front_l", "thigh_l", "calf_l"), ("knee_bend_back_l", "thigh_l", "calf_l"),
                     ("knee_bend_front_r", "thigh_r", "calf_r"), ("knee_bend_back_r", "thigh_r", "calf_r"),
                     ("hip_bend_front_l", "pelvis", "thigh_l"), ("hip_bend_back_l", "pelvis", "thigh_l"),
                     ("hip_bend_front_r", "pelvis", "thigh_r"), ("hip_bend_back_r", "pelvis", "thigh_r"),
                 })
        {
            int p = s.Find(parent);
            var place = at[s.Find(joint)];
            bones.Add(new BoneInfo(name, p, new BoneTransform(Quaternion.Identity, place - at[p])));
            binds.Add(Matrix4x4.CreateTranslation(-place));
            at.Add(place);
        }
        var skeleton = new Skeleton { Bones = [.. bones], InverseBind = [.. binds] };
        int B(string n) => skeleton.Find(n);
        var vertices = model.Vertices.ToList();
        void Ring(Vector3 centre, float radius, (int Bone, float Weight)[] w)
        {
            for (int k = 0; k < 8; k++)
            {
                var p = centre + new Vector3(MathF.Cos(k * MathF.PI / 4f), MathF.Sin(k * MathF.PI / 4f), 0f) * radius;
                vertices.Add(new SkinVertex
                {
                    Position = p, Normal = Vector3.UnitZ,
                    Bones = SkinVertex.PackBones(w[0].Bone, w[1].Bone, w[2].Bone, 0), Weights = new Vector4(w[0].Weight, w[1].Weight, w[2].Weight, 0f),
                });
            }
        }
        foreach (var side in new[] { "l", "r" })
        {
            Ring(at[B($"calf_{side}")], 0.06f, [(B($"knee_bend_front_{side}"), 0.35f), (B($"thigh_{side}"), 0.2f), (B($"calf_{side}"), 0.45f)]);
            Ring(at[B($"thigh_{side}")], 0.08f, [(B($"hip_bend_front_{side}"), 0.4f), (B("pelvis"), 0.2f), (B($"thigh_{side}"), 0.4f)]);
        }
        return new SkinnedModel
        {
            Name = model.Name + " with bend helpers", Vertices = [.. vertices], Indices = [], Parts = [], Skeleton = skeleton,
            BoundsMin = model.BoundsMin, BoundsMax = model.BoundsMax,
        };
    }

    [Fact]
    public void A_bend_helpers_skin_counts_as_its_parents_the_knees_the_thighs_and_the_hips_the_hips()
    {
        var model = WithBendHelpers(PeopleDoubles.Person().Model);
        var skeleton = model.Skeleton;
        Assert.Equal(86, skeleton.Count);
        var table = AnatomyTables.For(skeleton)!;
        Assert.StartsWith(PeopleAnatomy.Name, table.Name);
        foreach (var side in new[] { "l", "r" })
        {
            var thigh = side == "l" ? BodyRegion.LeftThigh : BodyRegion.RightThigh;
            foreach (var which in new[] { "front", "back" })
            {
                Assert.Equal(thigh, table.Of(skeleton.Find($"knee_bend_{which}_{side}")));
                Assert.Equal(skeleton.Find($"thigh_{side}"), table.Volumes![skeleton.Find($"knee_bend_{which}_{side}")]);
                Assert.Equal(BodyRegion.Hips, table.Of(skeleton.Find($"hip_bend_{which}_{side}")));
                Assert.Equal(skeleton.Find("pelvis"), table.Volumes![skeleton.Find($"hip_bend_{which}_{side}")]);
            }
        }
        Assert.Equal((BodyRegion.Hips, "pelvis"), PeopleAnatomy.Of("HIP_BEND_BACK_R"));
        Assert.Equal((BodyRegion.None, ""), PeopleAnatomy.Of("knee_bend_side_l"));   // not a helper the models repository has
        var body = HitVolumeFit.Fit(model)!;
        foreach (var region in BodyRegions.All) Assert.Single(body.Volumes, v => v.Region == region);
        int Count(FittedBody f, BodyRegion r) => f.Volumes.Single(v => v.Region == r).Vertices;
        // Each knee's ring (8 vertices) is the thigh's: 0.35 + 0.2 on its bones against the calf's 0.45; each hip's (16) the hips':
        // 0.4 + 0.2 against the thigh's 0.4. With the helpers counted as nothing (the earlier build's table), the knee's ring has no volume with
        // half its weight and is lost, and the hip's goes nowhere either.
        var plain = HitVolumeFit.Fit(PeopleDoubles.Person().Model)!;
        Assert.Equal(Count(plain, BodyRegion.LeftThigh) + 8, Count(body, BodyRegion.LeftThigh));
        Assert.Equal(Count(plain, BodyRegion.RightThigh) + 8, Count(body, BodyRegion.RightThigh));
        Assert.Equal(Count(plain, BodyRegion.Hips) + 16, Count(body, BodyRegion.Hips));
        Assert.Equal(Count(plain, BodyRegion.LeftShin), Count(body, BodyRegion.LeftShin));
        var without = new AnatomyTable
        {
            Name = table.Name, Parents = table.Parents,
            Regions = [.. table.Regions.Select((r, b) => b >= 78 ? BodyRegion.None : r)],
            Volumes = [.. table.Volumes!.Select((v, b) => b >= 78 ? -1 : v)],
        };
        var lost = HitVolumeFit.Fit(model, without)!;
        Assert.Equal(Count(plain, BodyRegion.LeftThigh), Count(lost, BodyRegion.LeftThigh));
        Assert.Equal(Count(plain, BodyRegion.Hips), Count(lost, BodyRegion.Hips));
    }

    [Fact]
    public void With_round_30s_crouch_a_person_kneels_settles_rises_walks_crouched_turns_and_stands_up()
    {
        var (assets, k, _) = PeopleDoubles.Kind(crouch: true);
        var kind = assets.Kind(k);
        Assert.False(kind.Motion(MotionRole.Crouch).Composed);
        Assert.False(kind.Motion(MotionRole.CrouchWalk).Composed);     // its own clips: not lowered by the rig
        Assert.Equal("double:crouch_walk@0", assets.Clip(kind.Motion(MotionRole.CrouchWalk).Clip).Name);
        // The kneel is the earlier build's, behind --crouch-kneel (AnimationSettings.PersonKneel); the turn here the earlier build's (--turn-chain-off).
        var kneel = new AnimationSettings { PersonKneel = true };
        var system = new AnimationSystem(assets, kneel);
        var a = CharacterAnimation.For(assets, k);
        string Name() => assets.Clip(a.Clip).Name;
        void Run(MotionInput input, float seconds) { for (int t = 0; t < (int)MathF.Round(seconds * 120f); t++) system.Step(ref a, input, Tick); }
        MotionInput Still(bool crouched) => new() { Grounded = true, Crouched = crouched };
        MotionInput Going(float degrees, float speed, bool crouched) { var m = Moving(degrees, speed); m.Crouched = crouched; return m; }

        // Pressed while still: down to the kneel. Moving: up into the crouch, then the crouched walk's blends, matched to the ground.
        Run(Still(false), 0.5f);
        Run(Still(true), 0.1f);
        Assert.Equal(("crouch_kneel_down", MotionRole.Crouch), (Name(), a.Role));
        Run(Still(true), 0.7f);
        Assert.Equal("crouch_kneel", Name());
        Run(Going(0f, 0.8f, true), 0.1f);
        Assert.Equal(("crouch_rise", MotionRole.CrouchWalk, 1f), (Name(), a.Role, a.Rate));
        Run(Going(0f, 0.8f, true), 0.5f);
        Assert.Equal(("double:crouch_walk@0", 0.8f), (Name(), a.Rate));
        Run(Going(90f, 0.8f, true), 0.3f);
        Assert.Equal("double:crouch_walk@90", Name());
        // Stopped: the crouch held about half a second, then settling into the kneel; standing up from it.
        Run(Still(true), 0.1f);
        Assert.Equal("crouch", Name());
        Run(Still(true), 0.45f);
        Assert.Equal("crouch_settle", Name());
        Run(Still(true), 0.7f);
        Assert.Equal("crouch_kneel", Name());
        Run(Still(false), 0.1f);
        Assert.Equal(("crouch_kneel_up", MotionRole.Idle), (Name(), a.Role));
        Run(Still(false), 0.8f);
        Assert.Equal("stand", Name());
        // Pressed while walking: the walk fades into the crouched walk; stopped and standing up from the crouch held.
        Run(Going(0f, 1.0f, false), 0.5f);
        Assert.Equal("double:walk@0", Name());
        Run(Going(0f, 1.0f, true), 0.05f);
        Assert.True(Name() == "double:crouch_walk@0" && a.PreviousClip >= 0);
        Run(Still(true), 0.2f);
        Assert.Equal("crouch", Name());
        Run(Still(false), 0.05f);
        Assert.Equal("crouch_up", Name());
        Run(Still(false), 0.7f);
        Assert.Equal("stand", Name());

        // Crouched in the kneel with the view turned 70°: the crouched turn, back to the crouch held, the legs aligned, the kneel again.
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero, legs: LegTurnSettings.NoChain, animation: kneel);
        for (int t = 0; t < 150; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true, crouched: 1);
        Assert.Equal("crouch_kneel", assets.Clip(world.Get<CharacterAnimation>(e).Clip).Name);
        var turned = new List<string>();
        for (int t = 0; t < 400; t++)
        {
            Step(world, e, Vector3.Zero, DetMath.HalfPi + 70f * DetMath.DegToRad, true, crouched: 1);
            var c = world.Get<CharacterAnimation>(e);
            if (c.Role == MotionRole.Turn && !turned.Contains(assets.Clip(c.Clip).Name)) turned.Add(assets.Clip(c.Clip).Name);
            Assert.True(Finite(world, e));
        }
        Assert.Equal(["double:crouch_turn_left_90@70"], turned);
        Assert.Equal(0f, world.Get<PoseRig>(e).LegYaw, 3);
        Assert.Equal("crouch_kneel", assets.Clip(world.Get<CharacterAnimation>(e).Clip).Name);
    }

    [Fact]
    public void A_controlled_person_walks_looks_in_first_person_and_its_attack_and_block_do_nothing()
    {
        var (assets, k, _) = PeopleDoubles.Kind();
        var level = new TestLevel().Floor(-30, -30, 30, 30).Build();
        var b = new WorldBuilder(5);
        Bodies.MovementServices.Register(b, level);
        Control.MasqueradeServices.Register(b);
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        Control.CombatServices.Register(b);
        b.HitVolumes();
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        var e = Bodies.MovementServices.SpawnControlled(w, new(0, 0, 0.01f), DetMath.HalfPi, Bodies.Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, k));
        w.Set(e, MotionInput.Standing);
        w.Set(e, new Control.MasqueradeState { Mode = Control.MasqueradeMode.Unbound });
        Control.CombatServices.Arm(w, e);
        int begun = 0, raised = 0;
        void Go(Control.ActionFrame frame, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Bodies.MovementServices.Step(w, frame);
                begun += w.Events<Control.AttackBegun>().ThisTick.Count;
                raised += w.Events<Control.BlockRaised>().ThisTick.Count;
                Assert.True(Finite(w, e));
            }
        }
        // First person, walking ahead and to the side, a look round, an attack held and let go, the block held.
        foreach (var held in new[] { Control.Buttons.SwitchView, Control.Buttons.None, Control.Buttons.SwitchView, Control.Buttons.None }) Go(new(Vector2.Zero, Vector2.Zero, held, default), 1);
        Go(new(new Vector2(0f, 1f), Vector2.Zero, Control.Buttons.None, default), 120);
        Go(new(new Vector2(1f, 0.5f), new Vector2(3f, 0f), Control.Buttons.None, default), 120);
        Go(new(Vector2.Zero, Vector2.Zero, Control.Buttons.Attack, default), 60);
        Go(Control.ActionFrame.Idle, 60);
        Go(new(Vector2.Zero, Vector2.Zero, Control.Buttons.Block, default), 60);
        Go(Control.ActionFrame.Idle, 30);
        var a = w.Get<CharacterAnimation>(e);
        Assert.Equal(0, begun);
        Assert.Equal(0, raised);
        Assert.Equal(ActionRole.None, a.Action);
        Assert.Equal(17, w.Service<HitVolumes>().Posed(w, e).Length);
        var eye = w.Get<Control.CameraRig>(e);
        Assert.Equal(Control.ViewKind.First, eye.View);
    }

    [Fact]
    public void Every_slot_poses_its_stand_in_and_two_runs_give_the_same_digests()
    {
        var (assets, k, _) = PeopleDoubles.Kind();
        var level = new TestLevel().Floor(-4, -4, 4, 4).Box(new Vector3(-3, 0.6f, 0), new Vector3(3, 2.1f, 1.2f)).Build("wall");
        var (a, ea) = Make(assets, k, level, new Vector3(0, 0.2f, 0));
        var (b, eb) = Make(assets, k, level, new Vector3(0, 0.2f, 0));
        int tick = 0;
        foreach (var move in Enum.GetValues<TraversalKind>())
            for (int t = 0; t <= 40; t++, tick++)
            {
                var traversal = move == TraversalKind.None ? default : new Traversal
                {
                    Kind = move, Progress = t / 40f, Seconds = 1f, From = new Vector3(0, 0.2f, 0), To = new Vector3(0, 0.9f, 1.2f), Height = 1.2f,
                    Lip = new Vector3(0, 0.6f, 1.2f), LipNormal = new Vector3(0, -1, 0),
                };
                var feet = new Vector3(0, 0.2f, 0.6f * t / 40f);
                float yaw = DetMath.HalfPi + 0.02f * tick;
                var v = move == TraversalKind.None ? new Vector3(0.8f, 0.6f, 0f) : Vector3.Zero;
                Step(a, ea, feet, yaw, move == TraversalKind.None, traversal, v, crouched: t > 30 ? 1 : 0);
                Step(b, eb, feet, yaw, move == TraversalKind.None, traversal, v, crouched: t > 30 ? 1 : 0);
                Assert.True(Finite(a, ea), $"{move} at {t}");
                Assert.Equal(a.Digest(), b.Digest());
                if (move != TraversalKind.None) Assert.Equal(MotionSlots.Of(move), a.Get<CharacterAnimation>(ea).Role);
            }
        Assert.True(a.Service<CharacterPoses>().Skin(a, ea).SequenceEqual(b.Service<CharacterPoses>().Skin(b, eb)));
    }
}
