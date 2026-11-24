// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The first-person eye: it rides the head where the pose has it, a steady eye (the
/// head's less the gait's bob) for the simulation and the head's own for the picture's share of the head motion; the
/// head and neck hidden from the picture only; the field of view and the pointer's pick. Most tests use Christof from
/// the 1.1 install and skip without it; the rest need nothing.
/// </summary>
public sealed class FirstPersonEyeTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static (CharacterAssets Assets, int Kind) Christof()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        return (assets, OneOneLibrary.Register(assets, Library.Value!.Character("Christof")!));
    }

    /// <summary>A world as the program makes it (movement, animation, the body's motion) on a floor, with Christof controlled in first person facing +Y.</summary>
    static (World World, EntityId Player) World(CharacterAssets assets, int kind, Func<TestLevel, TestLevel>? more = null)
    {
        var level = (more ?? (l => l))(new TestLevel().Floor(-60, -60, 60, 60)).Build();
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level);
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        // Switch view cycles from the start's right shoulder: the left, then first person.
        foreach (var held in new[] { Buttons.SwitchView, Buttons.None, Buttons.SwitchView })
            Drive(w, new ActionFrame(Vector2.Zero, Vector2.Zero, held, default), 1);
        Assert.Equal(ViewKind.First, w.Get<CameraRig>(e).View);
        Drive(w, ActionFrame.Idle, 30);
        return (w, e);
    }

    static void Drive(World w, ActionFrame frame, int ticks)
    {
        for (int i = 0; i < ticks; i++) { w.Service<TickInput>().Set(frame); w.Step(); }
    }

    static ActionFrame Ahead(Buttons gait) => new(new Vector2(0, 1), Vector2.Zero, gait, default);

    [Fact]
    public void Christofs_eyes_are_at_his_face_and_his_neck_is_hidden_with_his_head()
    {
        var (assets, kind) = Christof();
        var head = assets.Kind(kind).Head!;
        Assert.Equal(18, head.Bone);
        Assert.Equal(17, head.Neck);
        Assert.Equal(new[] { 17, 18, 19, 20 }, head.Bones);
        // Halfway up his head, at his body's eye height; ahead of the head's joint, behind the front of his face.
        Assert.InRange(head.Eyes.Z, Locomotion.Sandbox.EyeHeight - 0.03f, Locomotion.Sandbox.EyeHeight + 0.03f);
        var joint = Matrix4x4.Invert(assets.ModelOf(kind).Skeleton.InverseBind[18], out var bind) ? bind.Translation : default;
        Assert.InRange(head.Eyes.Y - joint.Y, 0.05f, 0.12f);     // 1.1's characters face +Y
        Assert.InRange(head.Eyes.X - joint.X, -0.02f, 0.02f);
    }

    [Fact]
    public void At_a_walk_a_run_and_a_sprint_the_steady_eye_keeps_its_height_within_a_centimetre_while_the_head_bobs()
    {
        var (assets, kind) = Christof();
        foreach (var gait in new[] { Buttons.Walk, Buttons.None, Buttons.Sprint })
        {
            var (w, e) = World(assets, kind);
            var view = w.Service<ViewState>();
            Drive(w, Ahead(gait), 240);
            float sLow = float.MaxValue, sHigh = float.MinValue, hLow = float.MaxValue, hHigh = float.MinValue, most = 0f;
            for (int t = 0; t < 240; t++)
            {
                Drive(w, Ahead(gait), 1);
                var feet = w.Get<Body>(e).Feet;
                float s = view.Eyes.Z - feet.Z, h = view.Head.Z - feet.Z;
                (sLow, sHigh, hLow, hHigh) = (MathF.Min(sLow, s), MathF.Max(sHigh, s), MathF.Min(hLow, h), MathF.Max(hHigh, h));
                most = MathF.Max(most, Vector3.Distance(view.Head, view.Eyes));
                // The simulation's view stands at the steady eye, looking where the player looks.
                Assert.Equal(view.Eyes, view.View.Eye);
                Assert.Equal(view.Eyes, FirstPersonView.Eye(view.Eyes, view.Head, 0f));
                Assert.True(Vector3.Distance(view.Head, FirstPersonView.Eye(view.Eyes, view.Head, 1f)) < 1e-5f);
            }
            Assert.True(sHigh - sLow < 0.01f, $"{gait}: the steady eye went from {sLow:0.000} to {sHigh:0.000} m");
            Assert.True(hHigh - hLow > 0.1f, $"{gait}: the head only went from {hLow:0.000} to {hHigh:0.000} m");
            // The run leans the body forward and down: the eye is lower than standing, and the head's and the steady eye differ by the bob alone.
            Assert.InRange(sHigh, 1.45f, 1.6f);
            Assert.True(most < 0.2f, $"{gait}: the head's eyes went {most:0.000} m from the steady eye");
        }
    }

    [Fact]
    public void Crouching_the_eye_goes_down_with_the_head_within_the_crouched_capsule_ahead()
    {
        var (assets, kind) = Christof();
        var (w, e) = World(assets, kind);
        var view = w.Service<ViewState>();
        float standing = view.Eyes.Z;
        Drive(w, new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Crouch, default), 120);
        var feet = w.Get<Body>(e).Feet;
        Assert.InRange(view.Eyes.Z - feet.Z, 0.9f, Locomotion.Sandbox.CrouchHeight + 0.05f);
        Assert.True(standing - view.Eyes.Z > 0.4f);
        Assert.True(Vector3.Distance(view.Eyes, view.Head) < 0.07f, "crouched and still, the two eyes are one");
    }

    [Fact]
    public void A_wall_and_a_low_ceiling_keep_the_eye_out_of_them()
    {
        var (assets, kind) = Christof();
        // A wall across +Y 2 m ahead, and a 1.25 m ceiling over the floor behind it (the body runs into the wall).
        var (w, e) = World(assets, kind, l => l.Box(new(-5, 2, 0), new(5, 3, 3)));
        var view = w.Service<ViewState>();
        var settings = w.Service<CameraSettings>();
        for (int t = 0; t < 240; t++)
        {
            Drive(w, Ahead(Buttons.Sprint), 1);
            Assert.True(view.Eyes.Y <= 2f - settings.EyeClearance + 0.011f, $"the steady eye at {view.Eyes}");
            Assert.True(view.Head.Y <= 2f - settings.EyeClearance + 0.011f, $"the head's eye at {view.Head}");
        }
        // A ceiling 1.25 m up from 1 m ahead on, crept under crouched.
        var (low, f) = World(assets, kind, l => l.Box(new(-5, 1, 1.25f), new(5, 8, 1.5f)));
        var under = low.Service<ViewState>();
        Drive(low, new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Crouch, default), 60);
        Drive(low, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Crouch, default), 300);
        Assert.True(low.Get<Body>(f).Feet.Y > 2.0f, $"crept to {low.Get<Body>(f).Feet}");
        Assert.True(under.Eyes.Z <= 1.25f - settings.EyeClearance + 0.011f, $"under the ceiling the eye is at {under.Eyes}");
        Assert.True(under.Head.Z <= 1.25f - settings.EyeClearance + 0.011f, $"under the ceiling the head's eye is at {under.Head}");
    }

    /// <summary>
    /// The render test (by the hidden bones): a vertex of the chest, the neck or the head is in view when it is in front of the
    /// near plane and within the picture's field (80°, 16:9) from <paramref name="eye"/> looking along the yaw, level.
    /// </summary>
    static int ChestAndNeckInView(World w, EntityId e, CharacterAssets assets, int kind, Vector3 eye, float yaw)
    {
        var k = assets.Kind(kind);
        var head = k.Head!;
        var model = assets.ModelOf(kind);
        var hidden = new bool[model.Skeleton.Count];
        foreach (int b in head.Bones) hidden[b] = true;
        hidden[model.Skeleton.Bones[head.Root].Parent] = true;    // the chest the neck stands on
        var skin = new Matrix4x4[model.Skeleton.Count];
        w.Service<CharacterPoses>().CopySkin(w, e, skin, firstPerson: true);
        var place = k.Placement(w.Get<Body>(e).Feet, w.Get<Body>(e).Yaw);
        var forward = DetMath.Direction(yaw, 0f);
        var (s, c) = DetMath.SinCos(yaw);
        var right = new Vector3(s, -c, 0f);
        var up = Vector3.Cross(right, forward);
        float tan = DetMath.Tan(FirstPersonView.DefaultFieldOfView * DetMath.DegToRad / 2f);
        int seen = 0;
        foreach (var v in model.Vertices)
        {
            int best = v.Bone(0);
            float most = v.Weights.X;
            if (v.Weights.Y > most) { most = v.Weights.Y; best = v.Bone(1); }
            if (v.Weights.Z > most) { most = v.Weights.Z; best = v.Bone(2); }
            if (v.Weights.W > most) best = v.Bone(3);
            if (!hidden[best]) continue;
            var d = PoseMath.Transform(PoseEvaluator.SkinPosition(v, skin), place) - eye;
            float z = Vector3.Dot(d, forward);
            if (z > FirstPersonView.NearPlane && MathF.Abs(Vector3.Dot(d, up)) < z * tan && MathF.Abs(Vector3.Dot(d, right)) < z * tan * 16f / 9f) seen++;
        }
        return seen;
    }

    [Fact]
    public void At_a_sprint_looking_ahead_nothing_of_the_neck_or_chest_is_in_view_at_any_head_motion_though_the_old_upright_eye_saw_them()
    {
        var (assets, kind) = Christof();
        var (w, e) = World(assets, kind);
        var view = w.Service<ViewState>();
        Drive(w, Ahead(Buttons.Sprint), 120);
        int before = 0;
        for (int t = 0; t < 120; t++)
        {
            Drive(w, Ahead(Buttons.Sprint), 1);
            float yaw = w.Get<CameraRig>(e).Yaw;
            foreach (float motion in new[] { 0f, 0.5f, 1f })
            {
                int seen = ChestAndNeckInView(w, e, assets, kind, FirstPersonView.Eye(view.Eyes, view.Head, motion), yaw);
                Assert.True(seen == 0, $"tick {t}, head motion {motion}: {seen} vertices of the neck or chest in view");
            }
            // The first eye: upright over the feet at the eye height.
            before += ChestAndNeckInView(w, e, assets, kind, w.Get<Body>(e).Feet + new Vector3(0, 0, Locomotion.Sandbox.EyeHeight), yaw);
        }
        Assert.True(before > 0, "the old eye should have seen the neck at a sprint (as seen in play)");
    }

    [Fact]
    public void The_simulation_is_the_same_whatever_the_viewer_sees_and_its_eyes_measure_from_the_steady_eye()
    {
        var (assets, kind) = Christof();
        ulong[] Run(float motion)
        {
            var (w, _) = World(assets, kind);
            var view = w.Service<ViewState>();
            var digests = new ulong[360];
            for (int t = 0; t < digests.Length; t++)
            {
                var gait = t < 120 ? Buttons.Walk : t < 240 ? Buttons.None : Buttons.Sprint;
                Drive(w, new ActionFrame(new Vector2(0, 1), new Vector2(t % 50 == 0 ? 0.3f : 0f, 0f), gait | (t == 300 ? Buttons.Jump : 0), default), 1);
                // What the program does with the setting: draws from here; nothing goes back.
                _ = FirstPersonView.Eye(view.Eyes, view.Head, motion);
                digests[t] = w.Digest();
            }
            return digests;
        }
        Assert.Equal(Run(0f), Run(1f));
    }

    [Fact]
    public void Switch_view_cycles_first_person_then_the_right_shoulder_then_the_left_and_an_older_tapes_swap_shoulder_still_swaps()
    {
        // "v is first person, third person right shoulder, third person left shoulder, repeat".
        var w = MovementServices.Register(new WorldBuilder(1), new TestLevel().Floor(-10, -10, 10, 10).Build()).Build();
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        (ViewKind, int) Now() => (w.Get<CameraRig>(e).View, w.Get<CameraRig>(e).Shoulder);
        void Press(Buttons b) { Drive(w, new ActionFrame(Vector2.Zero, Vector2.Zero, b, default), 1); Drive(w, ActionFrame.Idle, 1); }
        Assert.Equal((ViewKind.Third, 1), Now());
        Press(Buttons.SwitchView); Assert.Equal((ViewKind.Third, -1), Now());
        Press(Buttons.SwitchView); Assert.Equal(ViewKind.First, Now().Item1);
        Press(Buttons.SwitchView); Assert.Equal((ViewKind.Third, 1), Now());
        // The left shoulder by swap shoulder, then first person: after first person it is the right shoulder again. swap
        // shoulder is retired (no key, no row, never sent by the program); only an older tape holds its bit, and its press
        // still swaps, so such a tape replays as it was made.
        Press(Buttons.SwapShoulder); Assert.Equal((ViewKind.Third, -1), Now());
        Press(Buttons.SwitchView); Assert.Equal(ViewKind.First, Now().Item1);
        Press(Buttons.SwapShoulder);
        Press(Buttons.SwitchView); Assert.Equal((ViewKind.Third, 1), Now());
        // Held, it switches once.
        Drive(w, new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.SwitchView, default), 30);
        Assert.Equal((ViewKind.Third, -1), Now());
    }

    [Fact]
    public void Without_a_posed_head_the_eye_stands_at_the_eye_height_over_the_feet()
    {
        var w = MovementServices.Register(new WorldBuilder(1), new TestLevel().Floor(-10, -10, 10, 10).Build()).Build();
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        Drive(w, ActionFrame.Idle, 30);
        var view = w.Service<ViewState>();
        var feet = w.Get<Body>(e).Feet;
        Assert.Equal(feet.Z + Locomotion.Sandbox.EyeHeight, view.Eyes.Z, 0.001f);
        Assert.Equal(view.Eyes, view.Head);
    }

    [Fact]
    public void The_head_and_neck_are_hidden_from_the_first_person_picture_only_and_its_shadow_keeps_them()
    {
        var (assets, kind) = Christof();
        var (w, e) = World(assets, kind);
        var poses = w.Service<CharacterPoses>();
        var place = assets.Kind(kind).Placement(w.Get<Body>(e).Feet, w.Get<Body>(e).Yaw);
        var first = poses.Draw(w, e, place, firstPerson: true);
        var whole = poses.Skin(w, e).ToArray();
        Assert.NotNull(first.ShadowSkin);
        Assert.Equal(whole, first.ShadowSkin);
        foreach (int b in new[] { 17, 18, 19, 20 }) Assert.True(first.Skin[b].M11 * first.Skin[b].M11 + first.Skin[b].M12 * first.Skin[b].M12 < 1e-5f, $"bone {b} is drawn");
        foreach (int b in new[] { 0, 15, 16, 21, 26 }) Assert.Equal(whole[b], first.Skin[b]);
        var third = poses.Draw(w, e, place, firstPerson: false, reuse: first);
        Assert.Null(third.ShadowSkin);
        Assert.Equal(whole, third.Skin);
    }

    [Fact]
    public void The_pointers_place_put_into_the_pick_field_is_the_same_ray_as_on_the_viewers_picture()
    {
        var settings = new CameraSettings();
        foreach (float field in new[] { 60f, 80f, 110f })
        {
            float scale = FirstPersonView.PickScale(field, settings.FirstPersonPickField);
            Assert.InRange(scale, 0f, 1.0001f);
            var drawn = new ViewPoint(new Vector3(1, 2, 1.6f), 0.7f, -0.2f, field);
            var picked = drawn with { FieldOfViewDegrees = settings.FirstPersonPickField };
            foreach (var place in new[] { new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1f, 0.3f), new Vector2(0.8f, 1f) })
            {
                var pick = FirstPersonView.ToPick(place, scale);
                Assert.True(PointerPick.OnPicture(pick));
                var a = PointerPick.Direction(drawn, place, 16f / 9f);
                var b = PointerPick.Direction(picked, pick, 16f / 9f);
                Assert.True(Vector3.Distance(a, b) < 1e-4f, $"field {field}, place {place}: {a} against {b}");
                Assert.True(Vector2.Distance(FirstPersonView.FromPick(pick, scale), place) < 1e-5f);
            }
            Assert.False(PointerPick.OnPicture(FirstPersonView.ToPick(new Vector2(1.2f, 0.5f), scale)), "a place off the picture stays off it");
        }
        Assert.Equal(80f, FirstPersonView.Field(float.NaN));
        Assert.Equal(110f, FirstPersonView.Field(140f));
        Assert.Equal(60f, FirstPersonView.Field(30f));
    }
}
