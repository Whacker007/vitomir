// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The moves drawn: a body's <see cref="Traversal"/> chooses its motion slot and plays it by its progress; the
/// <see cref="PoseRig"/> puts the hands on the lip and the feet on the top by IK, each foot on the ground under it
/// on uneven ground, and a crouched walk lowered over the walk's feet. Most tests use a small made-up person (no
/// install needed); the last ones use Christof from the 1.1 install and skip without it. Each world holds a body,
/// its move (set by the test, standing in for the body's own moves) and the animation, as the program has them.
/// </summary>
public sealed partial class TraversalPoseTests
{
    const float Tick = 1f / 120f;

    /// <summary>
    /// A made-up person in metres, facing +Y, standing at the origin: hips at 1.0, legs of 0.45 and 0.42 to the ankles
    /// (0.08 above the ground) and the balls of the feet ahead of them; a spine to the head at 1.7; arms hanging from
    /// shoulders 0.4 apart at 1.5, 0.28 and 0.27 to the wrists, the knuckles 0.09 below. Bound unrotated.
    /// </summary>
    internal static SkinnedModel Person()
    {
        var bones = new List<BoneInfo>();
        var at = new List<Vector3>();
        int Add(string name, int parent, Vector3 offset)
        {
            bones.Add(new BoneInfo(name, parent, new BoneTransform(Quaternion.Identity, offset)));
            at.Add((parent >= 0 ? at[parent] : Vector3.Zero) + offset);
            return bones.Count - 1;
        }
        int root = Add("hips", -1, new Vector3(0, 0, 1.0f));
        foreach (float side in new[] { -1f, 1f })
        {
            int hip = Add("hip", root, new Vector3(0.12f * side, 0, -0.05f));
            int knee = Add("knee", hip, new Vector3(0, 0.02f, -0.45f));
            int ankle = Add("ankle", knee, new Vector3(0, -0.02f, -0.42f));
            Add("ball", ankle, new Vector3(0, 0.12f, -0.06f));
        }
        int spine = Add("spine", root, new Vector3(0, 0, 0.25f));
        int chest = Add("chest", spine, new Vector3(0, 0, 0.25f));
        Add("head", chest, new Vector3(0, 0, 0.2f));
        foreach (float side in new[] { -1f, 1f })
        {
            int shoulder = Add("shoulder", chest, new Vector3(0.2f * side, 0, 0));
            int elbow = Add("elbow", shoulder, new Vector3(0, -0.02f, -0.28f));
            int wrist = Add("wrist", elbow, new Vector3(0, 0.02f, -0.27f));
            Add("knuckles", wrist, new Vector3(0, 0, -0.09f));
        }
        return new SkinnedModel
        {
            Name = "person", Indices = [], Parts = [], Vertices = [],
            Skeleton = new Skeleton { Bones = [.. bones], InverseBind = [.. at.Select(p => Matrix4x4.CreateTranslation(-p))] },
        };
    }

    // The person's bones: left leg 1-4, right leg 5-8, spine 9-11, left arm 12-15, right arm 16-19.
    const int LeftAnkle = 3, RightAnkle = 7, LeftKnuckles = 15, RightKnuckles = 19, Hips = 0;
    const float AnkleHeight = 0.08f;

    /// <summary>A clip that holds the rest pose, its hips swayed by <paramref name="sway"/> m a second along Y so it is not still.</summary>
    sealed class Hold(string name, float duration, float sway = 0f) : IAnimationClip
    {
        public string Name => name;
        public float Duration => duration;
        public bool Loops => true;
        public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
        {
            PoseEvaluator.Rest(skeleton, local);
            if (sway != 0f) local[0] = local[0] with { Translation = local[0].Translation + new Vector3(0, sway * seconds, 0) };
        }
    }

    /// <summary>The person as a kind: a stand, a walk, a composed crouch (the stand), the crouched walk as the walk, the moves' slots empty.</summary>
    internal static (CharacterAssets Assets, int Kind) PersonKind()
    {
        var assets = new CharacterAssets();
        var model = Person();
        int m = assets.AddModel(model);
        int idle = assets.AddClip(new Hold("stand", 2f));
        int walk = assets.AddClip(new Hold("walk", 1f, 0.02f));
        int crouch = assets.AddClip(new ComposedClip("crouch", 1f, true, [ComposedClip.Key(0f, new Hold("stand", 1f), 0f, model.Skeleton, 0, 0.62f)]));
        var motions = new RoleMotion[Enum.GetValues<MotionRole>().Length];
        Array.Fill(motions, RoleMotion.None);
        motions[(int)MotionRole.Idle] = new RoleMotion(idle, 0, 0, false);
        motions[(int)MotionRole.Walk] = new RoleMotion(walk, 1.3f, 0, false);
        motions[(int)MotionRole.Crouch] = new RoleMotion(crouch, 0, 0, false, Composed: true);
        motions[(int)MotionRole.CrouchWalk] = new RoleMotion(walk, 1.3f, 0, false, Composed: true);
        int kind = assets.AddKind(new CharacterKind
        {
            Name = "person", Model = m, Motions = motions, FacingYaw = DetMath.HalfPi,
            Limbs = LimbMap.FromTips(model.Skeleton, 4, 8, 15, 19, assets.Clip(idle)),
        });
        return (assets, kind);
    }

    /// <summary>A world with a body, its move, the animation and its rig on <paramref name="level"/>, and one person in it at <paramref name="feet"/> facing +Y.</summary>
    static (World World, EntityId Person) Make(CharacterAssets assets, int kind, LevelSpace level, Vector3 feet, ulong seed = 7, float yaw = DetMath.HalfPi)
    {
        var b = new WorldBuilder(seed).Component<Body>().Component<Traversal>().Component<MoveIntent>().Service(level).Animation(assets);
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var e = world.Create();
        world.Set(e, Body.At(feet, yaw) with { State = BodyState.Ground });
        world.Set(e, new Traversal());
        world.Set(e, CharacterAnimation.For(assets, kind));
        world.Set(e, MotionInput.Standing);
        return (world, e);
    }

    /// <summary>One tick with the body at <paramref name="feet"/> (on ground or not) making <paramref name="move"/>.</summary>
    static void Step(World world, EntityId e, Vector3 feet, bool grounded, in Traversal move, Vector3 velocity = default)
    {
        ref var body = ref world.Get<Body>(e);
        body.Feet = feet;
        body.Velocity = velocity;
        body.State = grounded ? BodyState.Ground : BodyState.Air;
        world.Get<Traversal>(e) = move;
        world.Step();
    }

    /// <summary>A bone's place in the world, as drawn.</summary>
    static Vector3 Bone(World world, EntityId e, CharacterAssets assets, int bone)
    {
        var body = world.Get<Body>(e);
        var place = assets.Kind(world.Get<CharacterAnimation>(e).Kind).Placement(body.Feet, body.Yaw);
        return Vector3.Transform(world.Service<CharacterPoses>().Bones(world, e)[bone].Translation, place);
    }

    /// <summary>A wall 3 m high whose face is at y = 0.6, running along X; its lip at (0, 0.6, 3).</summary>
    static LevelSpace Wall(float top = 3f, float depth = 1.5f) =>
        new TestLevel().Floor(-4, -4, 4, 4).Box(new Vector3(-3, 0.6f, 0), new Vector3(3, 0.6f + depth, top)).Build("wall");

    static Traversal Move(TraversalKind kind, float p, Vector3 from, Vector3 to, float top, bool lip = true) => new()
    {
        Kind = kind, Progress = p, Seconds = 1f, From = from, To = to, Height = top - from.Z,
        Lip = lip ? new Vector3(0, 0.6f, top) : Vector3.Zero, LipNormal = lip ? new Vector3(0, -1, 0) : Vector3.Zero,
    };

    [Fact]
    public void A_move_plays_its_slot_by_its_progress_and_an_empty_slot_keeps_its_role_over_the_stand()
    {
        var (assets, kind) = PersonKind();
        var (world, e) = Make(assets, kind, Wall(), new Vector3(0, 0.3f, 0));
        for (int i = 0; i < 10; i++) Step(world, e, new Vector3(0, 0.3f, 0), true, default);
        Step(world, e, new Vector3(0, 0.3f, 0.5f), false, Move(TraversalKind.Grab, 0.4f, new Vector3(0, 0.3f, 0.4f), new Vector3(0, 0.3f, 0.5f), 3f));
        var a = world.Get<CharacterAnimation>(e);
        Assert.Equal(MotionRole.Grab, a.Role);
        Assert.Equal(assets.Kind(kind).Motion(MotionRole.Idle).Clip, a.Clip);
        Assert.Equal(TraversalKind.Grab, world.Get<MotionInput>(e).Traversal);
        Assert.Equal(0.4f, world.Get<MotionInput>(e).TraversalProgress);
        Assert.Equal(1f, world.Get<PoseRig>(e).HandWeight);
    }

    [Fact]
    public void After_letting_go_the_body_falls_holding_the_drops_end_and_lands_in_its_stand()
    {
        var (assets, kind) = PersonKind();
        var k = assets.Kind(kind);
        int composed = assets.AddClip(new ComposedClip("drop", 1f, false,
            [ComposedKey(0f, 0.2f), ComposedKey(1f, 0.9f)]));
        var motions = (RoleMotion[])k.Motions.Clone();
        motions[(int)MotionRole.Drop] = new RoleMotion(composed, 0, 0, false, Composed: true);
        int dropping = assets.AddKind(new CharacterKind { Name = "dropper", Model = k.Model, Motions = motions, FacingYaw = k.FacingYaw, Limbs = k.Limbs });
        var feet = new Vector3(0, 0.3f, 1f);
        var (world, e) = Make(assets, dropping, Wall(), feet);
        for (int t = 0; t <= 60; t++) Step(world, e, feet, false, Move(TraversalKind.Drop, t / 60f, feet, feet, 3f));
        var a = world.Get<CharacterAnimation>(e);
        Assert.Equal((MotionRole.Drop, 1f), (a.Role, a.Time));
        for (int t = 0; t < 30; t++) Step(world, e, feet - new Vector3(0, 0, t * 0.02f), false, default, new Vector3(0, 0, -3f));
        a = world.Get<CharacterAnimation>(e);
        Assert.Equal((MotionRole.Drop, 1f), (a.Role, a.Time));      // falling, the end of the drop held
        for (int t = 0; t < 60; t++) Step(world, e, new Vector3(0, 0.3f, 0), true, default);
        Assert.Equal(MotionRole.Idle, world.Get<CharacterAnimation>(e).Role);

        ComposedKey ComposedKey(float at, float hips)
        {
            var pose = new BoneTransform[assets.Model(k.Model).Skeleton.Count];
            PoseEvaluator.Rest(assets.Model(k.Model).Skeleton, pose);
            pose[0] = pose[0] with { Translation = new Vector3(0, 0, hips) };
            return new ComposedKey(at, pose);
        }
    }

    [Fact]
    public void The_hands_reach_the_lip_within_2_cm_at_the_grab_and_while_hanging()
    {
        var (assets, kind) = PersonKind();
        var feet = new Vector3(0, 0.3f, 0.45f);     // the body's feet hang well below where the arms reach: the hips are pulled up
        var (world, e) = Make(assets, kind, Wall(), feet);
        var lipL = new Vector3(-0.2f, 0.6f, 3f); var lipR = new Vector3(0.2f, 0.6f, 3f);
        for (int t = 0; t <= 40; t++)
        {
            float p = t / 40f;
            Step(world, e, feet, false, Move(TraversalKind.Grab, p, feet - new Vector3(0, 0, 0.1f), feet, 3f));
            if (p >= 0.35f)
            {
                Assert.InRange(Vector3.Distance(Bone(world, e, assets, LeftKnuckles), lipL), 0f, 0.02f);
                Assert.InRange(Vector3.Distance(Bone(world, e, assets, RightKnuckles), lipR), 0f, 0.02f);
            }
        }
        for (int t = 0; t < 120; t++)
        {
            Step(world, e, feet, false, Move(TraversalKind.Hang, 0f, feet, feet, 3f));
            Assert.InRange(Vector3.Distance(Bone(world, e, assets, LeftKnuckles), lipL), 0f, 0.02f);
            Assert.InRange(Vector3.Distance(Bone(world, e, assets, RightKnuckles), lipR), 0f, 0.02f);
        }
        // Hanging, the body is below the lip and out from the face.
        var hips = Bone(world, e, assets, Hips);
        Assert.True(hips.Z < 3f - 0.8f && hips.Y < 0.6f, $"the hips at {hips}");
    }

    [Fact]
    public void The_feet_meet_the_top_at_the_end_of_a_hop_up_and_a_mantle()
    {
        var (assets, kind) = PersonKind();
        foreach (var (move, top, ticks) in new[] { (TraversalKind.HopUp, 0.5f, 60), (TraversalKind.Mantle, 1.6f, 130) })
        {
            var level = new TestLevel().Floor(-4, -4, 4, 4).Box(new Vector3(-2, 0.6f, 0), new Vector3(2, 2.2f, top)).Build("top");
            var from = new Vector3(0, 0.3f, 0); var to = new Vector3(0, 1.0f, top);
            var (world, e) = Make(assets, kind, level, from);
            for (int i = 0; i < 20; i++) Step(world, e, from, true, default);
            for (int t = 0; t <= ticks; t++)
            {
                float p = (float)t / ticks;
                // A path up and over: rising first, then forwards onto the top (the body's own path stands in).
                var feet = new Vector3(0, from.Y + (to.Y - from.Y) * Smooth(Math.Clamp((p - 0.4f) / 0.6f, 0f, 1f)), top * Smooth(Math.Clamp(p / 0.75f, 0f, 1f)));
                Step(world, e, feet, false, Move(move, p, from, to, top, lip: move == TraversalKind.Mantle));
            }
            foreach (var ankle in new[] { LeftAnkle, RightAnkle })
                Assert.InRange(Bone(world, e, assets, ankle).Z - top, AnkleHeight - 0.02f, AnkleHeight + 0.02f);
            for (int i = 0; i < 30; i++) Step(world, e, to, true, default);
            foreach (var ankle in new[] { LeftAnkle, RightAnkle })
                Assert.InRange(Bone(world, e, assets, ankle).Z - top, AnkleHeight - 0.02f, AnkleHeight + 0.02f);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }

    [Fact]
    public void Foot_IK_on_a_0_22_m_step_puts_each_foot_on_its_tread()
    {
        var (assets, kind) = PersonKind();
        // The step's edge runs along Y at x = 0; the body stands on its top edge with its left foot over the lower floor.
        var level = new TestLevel().Floor(-4, -4, 0, 4).Box(new Vector3(0, -4, 0), new Vector3(4, 4, 0.22f)).Build("step");
        var feet = new Vector3(0.05f, 0, 0.22f);
        var (world, e) = Make(assets, kind, level, feet);
        for (int i = 0; i < 60; i++) Step(world, e, feet, true, default);
        var left = Bone(world, e, assets, LeftAnkle); var right = Bone(world, e, assets, RightAnkle);
        Assert.True(left.X < 0f && right.X > 0f);
        Assert.InRange(left.Z - 0f, AnkleHeight - 0.02f, AnkleHeight + 0.02f);
        Assert.InRange(right.Z - 0.22f, AnkleHeight - 0.02f, AnkleHeight + 0.02f);
        Assert.InRange(world.Get<PoseRig>(e).RootOffset.Z, -0.23f, -0.21f);     // the hips lowered so the low foot reaches
        // In the air the feet are the motion's own again.
        for (int i = 0; i < 60; i++) Step(world, e, feet + new Vector3(0, 0, 1), false, default);
        Assert.InRange(Bone(world, e, assets, LeftAnkle).Z - 1.22f, AnkleHeight - 0.001f, AnkleHeight + 0.001f);
    }

    [Fact]
    public void On_a_30_degree_slope_each_foot_is_turned_to_lie_on_it()
    {
        var (assets, kind) = PersonKind();
        // The ramp rises along +X; the person faces up it.
        var level = new TestLevel().Floor(-4, -4, 0, 4).Ramp(0, 4, -2, 2, 30f).Build("ramp");
        float tan = MathF.Tan(30f * MathF.PI / 180f);
        var feet = new Vector3(1.5f, 0, 1.5f * tan);
        var (world, e) = Make(assets, kind, level, feet, yaw: 0f);
        for (int i = 0; i < 60; i++) Step(world, e, feet, true, default);
        foreach (var ball in new[] { 4, 8 })
        {
            var b = Bone(world, e, assets, ball);
            Assert.InRange(b.Z - b.X * tan, 0.0f, 0.045f);       // the ball of the foot on the slope, not in it
        }
        foreach (var ankle in new[] { LeftAnkle, RightAnkle })
        {
            var a = Bone(world, e, assets, ankle);
            Assert.InRange(a.Z - a.X * tan, AnkleHeight, AnkleHeight / MathF.Cos(30f * MathF.PI / 180f) + 0.02f);
        }
    }

    [Fact]
    public void A_crouched_walk_lowers_the_hips_and_keeps_the_walks_feet_and_a_still_crouch_plays_the_crouch()
    {
        var (assets, kind) = PersonKind();
        var level = new TestLevel().Floor(-4, -4, 4, 40).Build("floor");
        var feet = Vector3.Zero;
        var (world, e) = Make(assets, kind, level, feet);
        world.Get<Body>(e).Crouched = 1;     // the body's own flag (the motor sets it; the pose reads it)
        for (int t = 0; t < 60; t++)
        {
            feet += new Vector3(0, 1.0f * Tick, 0);
            Step(world, e, feet, true, default, new Vector3(0, 1.0f, 0));
        }
        Assert.Equal(MotionRole.CrouchWalk, world.Get<CharacterAnimation>(e).Role);
        Assert.InRange(world.Get<PoseRig>(e).RootOffset.Z, -0.43f, -0.41f);
        Assert.InRange(Bone(world, e, assets, Hips).Z - feet.Z, 0.5f, 0.62f);
        foreach (var ankle in new[] { LeftAnkle, RightAnkle })
            Assert.InRange(Bone(world, e, assets, ankle).Z, AnkleHeight - 0.01f, AnkleHeight + 0.01f);
        for (int t = 0; t < 60; t++) Step(world, e, feet, true, default);
        Assert.Equal(MotionRole.Crouch, world.Get<CharacterAnimation>(e).Role);
        Assert.InRange(world.Get<PoseRig>(e).RootOffset.Z, -0.01f, 0.01f);
        Assert.InRange(Bone(world, e, assets, Hips).Z, 0.6f, 0.64f);
    }

    [Fact]
    public void Two_worlds_making_the_same_moves_have_the_same_digest_and_the_same_pose_every_tick()
    {
        (ulong[] Digests, byte[] Pose) Run()
        {
            var (assets, kind) = PersonKind();
            var level = new TestLevel().Floor(-4, -4, 0, 4).Box(new Vector3(0, -4, 0), new Vector3(4, 4, 0.22f)).Box(new Vector3(-3, 1.6f, 0), new Vector3(3, 3f, 2.6f)).Build("mixed");
            var (world, e) = Make(assets, kind, level, new Vector3(0.05f, 0, 0.22f), seed: 11);
            var digests = new List<ulong>();
            var pose = new List<byte>();
            for (int t = 0; t < 400; t++)
            {
                float p = (t % 100) / 100f;
                var feet = new Vector3(0.05f - t * 0.0005f, t * 0.004f, 0.22f + 0.3f * p);
                var kindOf = (t / 100) switch { 0 => TraversalKind.None, 1 => TraversalKind.Vault, 2 => TraversalKind.Hang, _ => TraversalKind.PullUp };
                var move = kindOf == TraversalKind.None ? default : new Traversal
                {
                    Kind = kindOf, Progress = p, Seconds = 1f, From = new(0, 1.2f, 0.22f), To = new(0, 2f, 2.6f),
                    Lip = new(0, 1.6f, 2.6f), LipNormal = new(0, -1, 0), Height = 2.38f,
                };
                world.Set(e, new MoveIntent { Crouch = t % 50 < 20 });
                Step(world, e, feet, kindOf == TraversalKind.None, move, new Vector3(0, 0.5f, 0));
                digests.Add(world.Digest());
                pose.AddRange(MemoryMarshal.AsBytes(world.Service<CharacterPoses>().Skin(world, e)).ToArray());
            }
            return ([.. digests], [.. pose]);
        }
        var (d1, p1) = Run();
        var (d2, p2) = Run();
        Assert.Equal(d1, d2);
        Assert.Equal(p1, p2);
    }

    // ---- Christof, from the 1.1 install ----

    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static (CharacterAssets Assets, int Kind, OneOneCharacter Christof) Christof()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var c = Library.Value!.Character("Christof")!;
        var assets = new CharacterAssets();
        return (assets, OneOneLibrary.Register(assets, c), c);
    }

    [Fact]
    public void Christof_has_his_limbs_and_a_composition_in_every_slot_1_1_has_no_motion_for()
    {
        var (assets, kind, c) = Christof();
        var k = assets.Kind(kind);
        Assert.NotNull(k.Limbs);
        Assert.Equal(new Limb(1, 2, 3, 4), k.Limbs!.LeftLeg);       // the LFoot attach point is the ball of the foot, bone 4
        Assert.Equal(new Limb(5, 6, 7, 8), k.Limbs.RightLeg);
        Assert.Equal(new Limb(26, 27, 28, 29), k.Limbs.LeftArm);     // LFingers, bone 29
        Assert.Equal(new Limb(21, 22, 23, 24), k.Limbs.RightArm);
        Assert.InRange(k.Limbs.ShoulderWidth, 0.3f, 0.5f);
        foreach (var role in new[] { MotionRole.Crouch, MotionRole.HopUp, MotionRole.Hurdle, MotionRole.Vault, MotionRole.ClimbOnto, MotionRole.Mantle,
                                     MotionRole.Grab, MotionRole.Hang, MotionRole.PullUp, MotionRole.Drop })
        {
            var m = k.Motion(role);
            Assert.True(m.Has && m.Composed && assets.Clip(m.Clip) is ComposedClip, $"{role}");
        }
        Assert.True(k.Motion(MotionRole.CrouchWalk) is { Has: true, Composed: true } walk && walk.Clip == k.Motion(MotionRole.Walk).Clip);
        Assert.Equal(c.Compositions.Count, OneOneCompositions.Slots.Count);
    }

    [Fact]
    public void Christofs_hands_hold_a_ledge_within_2_cm_and_his_feet_stand_on_a_step_each()
    {
        var (assets, kind, _) = Christof();
        var limbs = assets.Kind(kind).Limbs!;
        var feet = new Vector3(0, 0.3f, 0.6f);
        var (world, e) = Make(assets, kind, Wall(), feet);
        for (int t = 0; t < 60; t++) Step(world, e, feet, false, Move(TraversalKind.Hang, 0f, feet, feet, 3f));
        float half = limbs.ShoulderWidth / 2;
        Assert.InRange(Vector3.Distance(Bone(world, e, assets, limbs.LeftArm.Tip), new Vector3(-half, 0.6f, 3f)), 0f, 0.02f);
        Assert.InRange(Vector3.Distance(Bone(world, e, assets, limbs.RightArm.Tip), new Vector3(half, 0.6f, 3f)), 0f, 0.02f);

        var level = new TestLevel().Floor(-4, -4, 0, 4).Box(new Vector3(0, -4, 0), new Vector3(4, 4, 0.22f)).Build("step");
        var on = new Vector3(0.05f, 0, 0.22f);
        (world, e) = Make(assets, kind, level, on);
        for (int i = 0; i < 60; i++) Step(world, e, on, true, default);
        var left = Bone(world, e, assets, limbs.LeftLeg.End); var right = Bone(world, e, assets, limbs.RightLeg.End);
        Assert.True(left.X < 0f && right.X > 0f, $"{left} {right}");
        Assert.InRange(left.Z - 0f, limbs.AnkleHeight - 0.02f, limbs.AnkleHeight + 0.02f);
        Assert.InRange(right.Z - 0.22f, limbs.AnkleHeight - 0.02f, limbs.AnkleHeight + 0.02f);
    }
}
