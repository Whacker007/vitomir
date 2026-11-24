// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The platform's animation without the 1.1 install: 1.1's Euler
/// order as quaternions, the crossfade's ends, the choice of idle, walk and run, the head collapse, the
/// determinism of a world that animates, and the game folder a Vitomir install lays out.
/// </summary>
public sealed class AnimationTests
{
    /// <summary>A two-bone test skeleton: a root and a child 1 m above it, bound unrotated.</summary>
    static Skeleton TwoBones()
    {
        var bones = new[]
        {
            new BoneInfo("root", -1, new BoneTransform(Quaternion.Identity, Vector3.Zero)),
            new BoneInfo("head", 0, new BoneTransform(Quaternion.Identity, new Vector3(0, 0, 1))),
        };
        var inverse = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0, -1) };
        return new Skeleton { Bones = bones, InverseBind = inverse };
    }

    /// <summary>A looping clip that turns the root about Z at <paramref name="turnsPerSecond"/> and leaves the rest at rest.</summary>
    sealed class TurnClip(string name, float duration, float turnsPerSecond) : IAnimationClip
    {
        public string Name => name;
        public float Duration => duration;
        public bool Loops => true;
        public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
        {
            PoseEvaluator.Rest(skeleton, local);
            local[0] = local[0] with { Rotation = DetMath.Yaw(seconds * turnsPerSecond * DetMath.TwoPi) };
        }
    }

    static SkinnedModel TwoBoneModel() => new()
    {
        Name = "test", Skeleton = TwoBones(), Indices = [], Parts = [],
        Vertices =
        [
            new SkinVertex { Position = new Vector3(0.1f, 0, 0.5f), Bones = SkinVertex.PackBones(0), Weights = new Vector4(1, 0, 0, 0) },
            new SkinVertex { Position = new Vector3(0, 0.1f, 1.1f), Bones = SkinVertex.PackBones(1), Weights = new Vector4(1, 0, 0, 0) },
        ],
    };

    internal static (CharacterAssets Assets, int Kind) TestKind(float walkNatural = 1.2f, float runNatural = 3.2f)
    {
        var assets = new CharacterAssets();
        int model = assets.AddModel(TwoBoneModel());
        int idle = assets.AddClip(new TurnClip("idle", 2f, 0.1f));
        int walk = assets.AddClip(new TurnClip("walk", 1f, 0.5f));
        int run = assets.AddClip(new TurnClip("run", 0.7f, 1f));
        var motions = new RoleMotion[Enum.GetValues<MotionRole>().Length];
        Array.Fill(motions, RoleMotion.None);
        motions[(int)MotionRole.Idle] = new RoleMotion(idle, 0, 0, false);
        motions[(int)MotionRole.Walk] = new RoleMotion(walk, walkNatural, 0, false);
        motions[(int)MotionRole.Run] = new RoleMotion(run, runNatural, 0, false);
        int kind = assets.AddKind(new CharacterKind
        {
            Name = "test", Model = model, Motions = motions, Head = HeadCollapse.For(assets.Model(model).Skeleton, 1),
        });
        return (assets, kind);
    }

    [Fact]
    public void Euler_degrees_become_the_quaternion_of_X_then_Y_then_Z_with_row_vectors()
    {
        foreach (var d in new[] { new Vector3(30, 0, 0), new Vector3(0, -45, 0), new Vector3(0, 0, 120), new Vector3(12, -70, 33), new Vector3(-170, 85, 260) })
        {
            var q = PoseMath.FromEulerDegrees(d);
            var m = PoseMath.ToMatrix(new BoneTransform(q, Vector3.Zero));
            var (sx, cx) = DetMath.SinCos(d.X * DetMath.DegToRad);
            var (sy, cy) = DetMath.SinCos(d.Y * DetMath.DegToRad);
            var (sz, cz) = DetMath.SinCos(d.Z * DetMath.DegToRad);
            var rx = new Matrix4x4(1, 0, 0, 0, 0, cx, sx, 0, 0, -sx, cx, 0, 0, 0, 0, 1);
            var ry = new Matrix4x4(cy, 0, -sy, 0, 0, 1, 0, 0, sy, 0, cy, 0, 0, 0, 0, 1);
            var rz = new Matrix4x4(cz, sz, 0, 0, -sz, cz, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            var expected = PoseMath.Multiply(PoseMath.Multiply(rx, ry), rz);
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    Assert.True(MathF.Abs(m[i, j] - expected[i, j]) < 2e-6f, $"{d}: [{i},{j}] {m[i, j]} against {expected[i, j]}");
        }
    }

    [Fact]
    public void The_crossfade_at_its_start_and_at_its_end_equals_each_clip_alone()
    {
        var (assets, kind) = TestKind();
        var e = new PoseEvaluator();
        var state = CharacterAnimation.For(assets, kind) with { Clip = 2, Time = 0.3f, PreviousClip = 1, PreviousTime = 0.2f, Blend = 0f };
        var faded = new BoneTransform[2];
        var alone = new BoneTransform[2];

        e.Local(assets, state, faded);
        e.Local(assets, state with { Clip = 1, Time = 0.2f, PreviousClip = -1, Blend = 1f }, alone);
        Assert.Equal(Bytes(alone), Bytes(faded));

        e.Local(assets, state with { Blend = 1f }, faded);
        e.Local(assets, state with { PreviousClip = -1, Blend = 1f }, alone);
        Assert.Equal(Bytes(alone), Bytes(faded));

        e.Local(assets, state with { Blend = 0.5f }, faded);   // between, it is neither
        Assert.NotEqual(Bytes(alone), Bytes(faded));
    }

    [Fact]
    public void The_animation_system_picks_idle_walk_and_run_at_0_1_3_and_3_0_m_s()
    {
        var (assets, kind) = TestKind();
        var world = new WorldBuilder(7).Animation(assets).Build();
        var e = world.Create();
        world.Set(e, CharacterAnimation.For(assets, kind));
        foreach (var (speed, role, clip) in new[] { (0f, MotionRole.Idle, 0), (1.3f, MotionRole.Walk, 1), (3.0f, MotionRole.Run, 2), (0f, MotionRole.Idle, 0) })
        {
            world.Set(e, new MotionInput { GroundSpeed = speed, Grounded = true });
            for (int i = 0; i < 60; i++) world.Step();
            ref var a = ref world.Get<CharacterAnimation>(e);
            Assert.Equal(role, a.Role);
            Assert.Equal(clip, a.Clip);
            Assert.Equal(-1, a.PreviousClip);   // the fade is over within half a second
            float rate = role == MotionRole.Walk ? 1.3f / 1.2f : role == MotionRole.Run ? 3.0f / 3.2f : 1f;
            Assert.Equal(rate, a.Rate, 5);
        }
    }

    [Fact]
    public void A_sprint_plays_the_run_at_most_1_35_times_its_rate_and_a_backwards_run_plays_backwards()
    {
        var (assets, kind) = TestKind(runNatural: 2.9f);
        var world = new WorldBuilder(7).Animation(assets).Build();
        var e = world.Create();
        world.Set(e, CharacterAnimation.For(assets, kind));
        world.Set(e, new MotionInput { GroundSpeed = 5.5f, ForwardSpeed = 5.5f, Grounded = true });
        for (int i = 0; i < 60; i++) world.Step();
        Assert.Equal(MotionRole.Run, world.Get<CharacterAnimation>(e).Role);
        Assert.Equal(1.35f, world.Get<CharacterAnimation>(e).Rate, 5);

        // Backwards at the run's own speed: the same cadence, the other way, and its time wraps from the start to the end.
        world.Set(e, new MotionInput { GroundSpeed = 2.9f, ForwardSpeed = -2.9f, Grounded = true });
        float before = world.Get<CharacterAnimation>(e).Time;
        world.Step();
        ref var a = ref world.Get<CharacterAnimation>(e);
        Assert.Equal(-1f, a.Rate, 5);
        Assert.Equal(before - World.TickSeconds, a.Time, 4);
        for (int i = 0; i < 200; i++) world.Step();
        Assert.InRange(world.Get<CharacterAnimation>(e).Time, 0f, 0.7f);

        // Sideways (90 degrees from the facing) still plays forwards.
        world.Set(e, new MotionInput { GroundSpeed = 2.9f, ForwardSpeed = 0f, Grounded = true });
        world.Step();
        Assert.Equal(1f, world.Get<CharacterAnimation>(e).Rate, 5);
    }

    [Fact]
    public void A_speed_on_the_walk_and_run_threshold_does_not_flicker_between_them()
    {
        var (assets, kind) = TestKind();
        var system = new AnimationSystem(assets);
        var a = CharacterAnimation.For(assets, kind);
        float threshold = system.Settings.RunFrom;
        int changes = 0;
        var role = a.Role;
        for (int i = 0; i < 240; i++)
        {
            float speed = threshold + (i % 2 == 0 ? 0.03f : -0.03f);
            system.Step(ref a, new MotionInput { GroundSpeed = speed, Grounded = true }, World.TickSeconds);
            if (a.Role != role) { changes++; role = a.Role; }
        }
        Assert.Equal(1, changes);   // idle to walk, once
    }

    [Fact]
    public void Without_a_motion_for_the_air_a_character_holds_its_pose_until_it_lands()
    {
        var (assets, kind) = TestKind();
        var system = new AnimationSystem(assets);
        var a = CharacterAnimation.For(assets, kind);
        for (int i = 0; i < 60; i++) system.Step(ref a, new MotionInput { GroundSpeed = 1.3f, Grounded = true }, World.TickSeconds);
        float t = a.Time;
        for (int i = 0; i < 30; i++) system.Step(ref a, new MotionInput { GroundSpeed = 1.3f, VerticalSpeed = 2f, Grounded = false }, World.TickSeconds);
        Assert.Equal(MotionRole.Walk, a.Role);
        Assert.Equal(t, a.Time);
        system.Step(ref a, new MotionInput { GroundSpeed = 1.3f, Grounded = true }, World.TickSeconds);
        Assert.True(a.Time > t);
    }

    [Fact]
    public void The_head_collapse_shrinks_the_head_and_touches_nothing_else()
    {
        var (assets, kind) = TestKind();
        var state = CharacterAnimation.For(assets, kind) with { Clip = 1, Time = 0.37f };
        var model = new Matrix4x4[2];
        var skin = new Matrix4x4[2];
        new PoseEvaluator().Evaluate(assets, state, model, skin);
        var collapsed = (Matrix4x4[])skin.Clone();
        assets.Kind(kind).Head!.Apply(collapsed);
        Assert.Equal(skin[0], collapsed[0]);
        var head = assets.ModelOf(kind).Vertices[1];
        var joint = model[1].Translation;
        Assert.True(Vector3.Distance(PoseEvaluator.SkinPosition(head, collapsed), joint) < 1e-3f * 0.2f + 1e-6f);
        Assert.True(Vector3.Distance(PoseEvaluator.SkinPosition(head, skin), joint) > 0.1f);
    }

    [Fact]
    public void Two_worlds_that_animate_the_same_way_have_the_same_digest_every_tick()
    {
        ulong[] Run()
        {
            var (assets, kind) = TestKind();
            var world = new WorldBuilder(3).Animation(assets).Build();
            var e = world.Create();
            world.Set(e, CharacterAnimation.For(assets, kind));
            var digests = new ulong[300];
            for (int i = 0; i < digests.Length; i++)
            {
                float speed = i < 100 ? i * 0.03f : 4f - (i - 100) * 0.02f;
                world.Set(e, new MotionInput { GroundSpeed = speed, Grounded = i % 97 != 50 });
                world.Step();
                digests[i] = world.Digest();
            }
            return digests;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_Vitomir_game_folder_is_read_by_the_paths_1_1_knows_and_case_insensitively()
    {
        var dir = Directory.CreateTempSubdirectory("vitomir-game-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "models"));
            File.WriteAllBytes(Path.Combine(dir.FullName, "models", "Thing.nod"), Encoding.ASCII.GetBytes("nod"));
            File.WriteAllText(Path.Combine(dir.FullName, GameInstall.ManifestName),
                """{"format":1,"files":[{"one":"3d/models/Thing.nod","path":"models/Thing.nod","size":3,"arc":"Resource.nob","rank":0}]}""");
            Assert.Equal(dir.FullName, GameInstall.Locate(dir.FullName));
            using var install = GameInstall.Open(dir.FullName);
            Assert.True(install.IsLayout);
            Assert.Equal("nod", Encoding.ASCII.GetString(install.Read(@"3D\MODELS\thing.NOD")));
            Assert.Equal(["3d/models/Thing.nod"], install.List("3d/Models/"));
            Assert.Equal("3d/models/Thing.nod", install.FindByFileName("THING.nod"));
            Assert.False(install.TryRead("3d/models/other.nod", out _));
        }
        finally { dir.Delete(true); }
    }

    static byte[] Bytes(BoneTransform[] t) => MemoryMarshal.AsBytes(t.AsSpan()).ToArray();
}
