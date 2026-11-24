// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// 1.1's characters on the platform, from the player's install (found by VTMR_GAME_DIR or C:\Games\VTMR;
/// every test skips without it): the catalogue loads, models are in metres and skin back to their own
/// vertices at rest, 1.1's motions sample deterministically and loop smoothly, the animation system plays
/// Christof's own idle, walk and run, and first person hides his head and nothing else.
/// </summary>
public sealed class OneOneCharacterTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<string, OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name)
    {
        var lib = Lib();
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(name, out var c)) Loaded[name] = c = lib.Character(name) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    [Fact]
    public void Christofs_model_loads_with_plausible_counts_and_a_height_of_about_1_8_m()
    {
        var c = Character("Christof");
        var m = c.Model;
        Assert.Equal(40, m.Skeleton.Count);
        Assert.Equal(1209, m.Vertices.Length);
        Assert.Equal(1672 * 3, m.Indices.Length);
        Assert.Equal(m.Indices.Length, m.Parts.Sum(p => p.IndexCount));
        Assert.All(m.Indices, i => Assert.True(i < m.Vertices.Length));
        Assert.InRange(m.Parts.Length, 3, 6);
        Assert.InRange(m.BoundsMax.Z, 1.70f, 1.90f);    // the top of his head: 1.78 m above the ground at the origin
        Assert.InRange(m.BoundsMin.Z, 0f, 0.15f);       // the rest pose's feet are off the ground; his stand puts them on it
        Assert.Contains(m.Parts, p => p.TwoSided);       // the cape
        Assert.NotEmpty(c.Textures);
        Assert.All(c.Textures, t => Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length));
        Assert.All(m.Parts, p => Assert.Contains(c.Textures, t => t.Name == p.Texture));
        Assert.Equal(18, c.HeadBone);
        foreach (var role in new[] { MotionRole.Idle, MotionRole.Walk, MotionRole.Run, MotionRole.Land })
            Assert.NotNull(c.Role(role).Clip);
        Assert.InRange(c.Role(MotionRole.Walk).NaturalSpeed, 0.5f, 2.5f);
        Assert.InRange(c.Role(MotionRole.Run).NaturalSpeed, 2f, 7f);
    }

    [Fact]
    public void The_install_is_read_case_insensitively_and_textures_are_found_along_1_1s_search_path()
    {
        var install = Lib().Install;
        Assert.Equal(install.Read("3d/models/christof.nod"), install.Read(@"3D\Models\CHRISTOF.NOD"));
        Assert.Contains(install.List("misc/templates/"), p => p.EndsWith("/global.not", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("materials/prague/black.tga", install.FindMaterialFile("black.tga"));   // the only black.tga 1.1 ships
        Assert.Contains(Character("Christof").Model.Parts, p => p.Texture == "materials/prague/black.tga");
    }

    [Fact]
    public void A_1_1_character_faces_plus_Y_so_its_walk_carries_a_planted_foot_back_along_minus_Y()
    {
        var c = Character("Christof");
        var walk = c.Role(MotionRole.Walk).Clip!;
        var s = c.Model.Skeleton;
        int foot = c.MotionSet.Tags.First(t => t.Name == "LFoot").Bone;
        var plants = walk.Tags.Where(t => t.Type is 0 or 2).ToArray();     // Lwalk
        var next = walk.Tags.Where(t => t.Type <= 3 && t.Frame > plants[0].Frame).Min(t => t.Frame);
        var back = Joints(walk, s, next / 30f)[foot] - Joints(walk, s, plants[0].Frame / 30f)[foot];
        Assert.True(back.Y < -0.3f && MathF.Abs(back.X) < 0.05f, $"{back}");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c);
        var place = assets.Kind(kind).Placement(new Vector3(10, 20, 0), 0f);        // facing +X in the world
        var ahead = PoseMath.TransformNormal(Vector3.UnitY, place);
        Assert.True(Vector3.Distance(ahead, Vector3.UnitX) < 1e-6f, $"{ahead}");
        Assert.Equal(new Vector3(10, 20, 0), place.Translation);
    }

    [Fact]
    public void Every_catalogue_character_loads_with_an_idle_a_walk_and_a_run()
    {
        foreach (var entry in OneOneCatalogue.Characters)
        {
            var c = Character(entry.Name);
            Assert.InRange(c.Model.BoundsMax.Z, 1.5f, 1.9f);
            Assert.NotNull(c.Role(MotionRole.Idle).Clip);
            Assert.NotNull(c.Role(MotionRole.Walk).Clip);
            Assert.NotNull(c.Role(MotionRole.Run).Clip);
            Assert.True(c.HeadBone >= 0, entry.Name);
        }
    }

    [Fact]
    public void Skinning_the_rest_pose_gives_the_raw_vertices_in_metres_within_a_tenth_of_a_millimetre()
    {
        foreach (var entry in OneOneCatalogue.Characters)
        {
            var m = Character(entry.Name).Model;
            int n = m.Skeleton.Count;
            var local = new BoneTransform[n];
            var model = new Matrix4x4[n];
            var skin = new Matrix4x4[n];
            PoseEvaluator.Rest(m.Skeleton, local);
            PoseEvaluator.ToModel(m.Skeleton, local, model);
            PoseEvaluator.ToSkin(m.Skeleton, model, skin);
            float worst = 0f;
            foreach (var v in m.Vertices) worst = MathF.Max(worst, Vector3.Distance(PoseEvaluator.SkinPosition(v, skin), v.Position));
            Assert.True(worst < 1e-4f, $"{entry.Name}: {worst * 1000:F4} mm");
        }
    }

    [Fact]
    public void A_walk_clip_sampled_at_the_same_time_twice_gives_the_same_bits()
    {
        var c = Character("Christof");
        var walk = c.Role(MotionRole.Walk).Clip!;
        var s = c.Model.Skeleton;
        var a = new BoneTransform[s.Count];
        var b = new BoneTransform[s.Count];
        walk.Sample(0.4321f, s, a);
        c.Role(MotionRole.Run).Clip!.Sample(0.2f, s, b);   // something else between
        walk.Sample(0.4321f, s, b);
        Assert.Equal(MemoryMarshal.AsBytes(a.AsSpan()).ToArray(), MemoryMarshal.AsBytes(b.AsSpan()).ToArray());
    }

    [Fact]
    public void Poses_are_continuous_across_a_loops_wrap()
    {
        var c = Character("Christof");
        var s = c.Model.Skeleton;
        foreach (var role in new[] { MotionRole.Idle, MotionRole.Walk, MotionRole.Run })
        {
            var clip = c.Role(role).Clip!;
            Assert.True(clip.Loops);
            var before = Joints(clip, s, clip.Duration - 1e-4f);
            var after = Joints(clip, s, clip.Duration + 1e-4f);     // wrapped to just after the start
            var start = Joints(clip, s, 0f);
            for (int b = 0; b < s.Count; b++)
            {
                Assert.True(Vector3.Distance(before[b], after[b]) < 0.005f, $"{clip.Name} bone {b}: {Vector3.Distance(before[b], after[b]) * 1000:F2} mm across the wrap");
                Assert.True(Vector3.Distance(after[b], start[b]) < 0.001f);
            }
        }
    }

    static Vector3[] Joints(IAnimationClip clip, Skeleton s, float seconds)
    {
        var local = new BoneTransform[s.Count];
        var model = new Matrix4x4[s.Count];
        clip.Sample(seconds, s, local);
        PoseEvaluator.ToModel(s, local, model);
        return [.. model.Select(m => m.Translation)];
    }

    [Fact]
    public void The_animation_system_plays_Christofs_idle_walk_and_run_at_0_1_3_and_3_0_m_s()
    {
        var c = Character("Christof");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c);
        var world = new WorldBuilder(11).Animation(assets).Build();
        var e = world.Create();
        world.Set(e, CharacterAnimation.For(assets, kind));
        var poses = world.Service<CharacterPoses>();
        foreach (var (speed, role) in new[] { (0f, MotionRole.Idle), (1.3f, MotionRole.Walk), (3.0f, MotionRole.Run), (0f, MotionRole.Idle) })
        {
            world.Set(e, new MotionInput { GroundSpeed = speed, Grounded = true });
            for (int i = 0; i < 60; i++) world.Step();
            ref var a = ref world.Get<CharacterAnimation>(e);
            Assert.Equal(role, a.Role);
            Assert.Equal(assets.Kind(kind).Motion(role).Clip, a.Clip);
            if (role != MotionRole.Idle) Assert.Equal(speed / assets.Kind(kind).Motion(role).NaturalSpeed, a.Rate, 4);
            Assert.Equal(40, poses.Skin(world, e).Length);
        }
    }

    [Fact]
    public void A_hard_landing_plays_Christofs_rough_landing_from_where_he_meets_the_ground()
    {
        var c = Character("Christof");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c);
        var system = new AnimationSystem(assets);
        var a = CharacterAnimation.For(assets, kind);
        system.Step(ref a, new MotionInput { Grounded = false, VerticalSpeed = -9f }, World.TickSeconds);
        system.Step(ref a, new MotionInput { Grounded = true }, World.TickSeconds);
        Assert.Equal(MotionRole.Land, a.Role);
        Assert.Equal(13f / 30f + World.TickSeconds, a.Time, 4);
        for (int i = 0; i < 400; i++) system.Step(ref a, new MotionInput { Grounded = true }, World.TickSeconds);
        Assert.Equal(MotionRole.Idle, a.Role);
    }

    [Fact]
    public void The_head_collapse_leaves_the_rest_of_the_body_unchanged()
    {
        var c = Character("Christof");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c);
        var head = assets.Kind(kind).Head!;
        Assert.Equal([17, 18, 19, 20], head.Bones);     // the neck (17) with the head
        var state = CharacterAnimation.For(assets, kind) with { Role = MotionRole.Walk, Clip = assets.Kind(kind).Motion(MotionRole.Walk).Clip, Time = 0.3f };
        var model = new Matrix4x4[40];
        var skin = new Matrix4x4[40];
        new PoseEvaluator().Evaluate(assets, state, model, skin);
        var collapsed = (Matrix4x4[])skin.Clone();
        head.Apply(collapsed);
        var inHead = new HashSet<int>(head.Bones);
        for (int b = 0; b < 40; b++)
            if (!inHead.Contains(b)) Assert.Equal(skin[b], collapsed[b]);
        int moved = 0, kept = 0;
        var joint = model[head.Root].Translation;      // shrunk to the neck's joint
        foreach (var v in c.Model.Vertices)
        {
            bool headOnly = inHead.Contains(v.Bone(0)) && (v.Weights.Y == 0f || inHead.Contains(v.Bone(1)));
            bool bodyOnly = !inHead.Contains(v.Bone(0)) && (v.Weights.Y == 0f || !inHead.Contains(v.Bone(1)));
            var p = PoseEvaluator.SkinPosition(v, collapsed);
            if (bodyOnly) { Assert.Equal(PoseEvaluator.SkinPosition(v, skin), p); kept++; }
            if (headOnly) { Assert.True(Vector3.Distance(p, joint) < 0.001f); moved++; }
        }
        Assert.True(moved > 50 && kept > 500, $"{moved} head vertices, {kept} body vertices");
    }
}
