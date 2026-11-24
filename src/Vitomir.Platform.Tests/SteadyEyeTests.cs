// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Tests;

/// <summary>
/// A steady camera. First person: the eye held near where the motion alone has
/// the eyes through a blow (an allowance of a few centimetres), the gait as it was, the eye outside the body's own volumes
/// looking down, crouched, mid-swing and in a dodge, and never floating over the head through a drop; every human kind's
/// head and neck hidden. Third person: no lag at a steady pace, a dodge's burst eased, the boom let out eased. Christof
/// (with 1.1's broadsword) and the townsfolk from the install; skipped without it.
/// </summary>
public sealed class SteadyEyeTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<string, OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            string key = name + "/" + weapon;
            if (!Loaded.TryGetValue(key, out var c)) Loaded[key] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    sealed record Rig(World World, EntityId Player, CharacterAssets Assets, int Kind);

    /// <summary>A world as the program makes it, Christof controlled facing +Y with his broadsword, the masquerade's catch off; first person unless asked.</summary>
    static Rig Make(CameraSettings? cameras = null, bool first = true, Func<TestLevel, TestLevel>? more = null, Vector3? at = null)
    {
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        var level = (more ?? (l => l))(new TestLevel().Floor(-60, -60, 60, 60)).Build();
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level, cameras);
        MasqueradeServices.Register(b);
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        b.HitVolumes();
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at ?? new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        w.Set(e, new MasqueradeState { Mode = MasqueradeMode.Unbound });
        CombatServices.Arm(w, e);
        if (first)
        {
            foreach (var held in new[] { Buttons.SwitchView, Buttons.None, Buttons.SwitchView }) Drive(w, Frame(held), 1);
            Assert.Equal(ViewKind.First, w.Get<CameraRig>(e).View);
        }
        Drive(w, ActionFrame.Idle, 30);
        return new Rig(w, e, assets, kind);
    }

    static ActionFrame Frame(Buttons held, float pitch = 0f) => new(Vector2.Zero, new Vector2(0f, pitch), held, default);

    static ActionFrame Ahead(Buttons held) => new(new Vector2(0, 1), Vector2.Zero, held, default);

    static void Drive(World w, ActionFrame frame, int ticks)
    {
        for (int i = 0; i < ticks; i++) { w.Service<TickInput>().Set(frame); w.Step(); }
    }

    static readonly (string Name, bool Strong, int Variant)[] Blows = [("quick1", false, 0), ("quick2", false, 1), ("strong", true, 0)];

    /// <summary>
    /// One blow from a stand: the most the simulation's eye, the picture's head eye and the head's own eyes (the pose's,
    /// the swing in it) move from where they were as it began, and the least clearance of the eye from the body's own
    /// volumes (<see cref="Clearance"/>) through it.
    /// </summary>
    static (float Eye, float Head, float Pose, float Clear) Blow(Rig r, bool strong, int variant)
    {
        var w = r.World;
        var view = w.Service<ViewState>();
        w.Get<Combatant>(r.Player).NextVariant = variant;
        var (eye0, head0, pose0) = (view.Eyes, view.Head, PoseEyes(r));
        float eye = 0f, head = 0f, pose = 0f, clear = float.MaxValue;
        int hold = strong ? (int)MathF.Round(0.4f * World.TicksPerSecond) : 1;
        for (int t = 0; t < 2 * World.TicksPerSecond; t++)
        {
            Drive(w, t < hold ? Frame(Buttons.Attack) : ActionFrame.Idle, 1);
            eye = MathF.Max(eye, Vector3.Distance(view.Eyes, eye0));
            head = MathF.Max(head, Vector3.Distance(view.Head, head0));
            pose = MathF.Max(pose, Vector3.Distance(PoseEyes(r), pose0));
            clear = MathF.Min(clear, Clearance(r, FirstPersonView.Eye(view.Eyes, view.Head, 1f)));
        }
        Drive(w, ActionFrame.Idle, 60);
        return (eye, head, pose, clear);
    }

    /// <summary>The head's own eyes where the pose has them (world, m): what the earlier build's eye rode.</summary>
    static Vector3 PoseEyes(Rig r)
    {
        var k = r.Assets.Kind(r.Kind);
        var skin = r.World.Service<CharacterPoses>().UnbentSkin(r.World, r.Player);
        ref readonly var body = ref r.World.Get<Body>(r.Player);
        return PoseMath.Transform(k.Head!.EyesIn(skin), k.Placement(body.Feet, body.Yaw));
    }

    /// <summary>
    /// The least distance from <paramref name="eye"/> to the surface of the body's own hit volumes (the WYSIWYG capsules
    /// fitted to the drawn skin, as posed this tick) but the head's and the neck's, which first person hides: negative inside one.
    /// </summary>
    static float Clearance(Rig r, Vector3 eye)
    {
        var volumes = r.World.Service<HitVolumes>().Posed(r.World, r.Player);
        float least = float.MaxValue;
        foreach (var v in volumes)
        {
            if (v.Region is BodyRegion.Head or BodyRegion.Neck) continue;
            var (a, b) = (v.Capsule.A, v.Capsule.B);
            var ab = b - a;
            float t = ab.LengthSquared() > 1e-9f ? Math.Clamp(Vector3.Dot(eye - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            least = MathF.Min(least, Vector3.Distance(eye, a + ab * t) - v.Capsule.Radius);
        }
        return least;
    }

    [Fact]
    public void Through_each_blow_the_eye_moves_no_more_than_the_allowance_though_the_head_swings_half_a_metre()
    {
        var r = Make();
        float allowance = r.World.Service<CameraSettings>().ActionAllowance;
        foreach (var (name, strong, variant) in Blows)
        {
            var (eye, head, pose, clear) = Blow(r, strong, variant);
            output.WriteLine($"{name}: the eye {eye:0.000} m, the picture's head eye {head:0.000} m, the pose's head {pose:0.000} m; the eye's least clearance of the body {clear:0.000} m");
            Assert.True(pose > 0.3f, $"{name}: the head itself moved only {pose:0.000} m");
            // The allowance, and what is left of the stand's breathing (its bob) and the steady eye's filter.
            Assert.True(eye <= allowance + 0.01f, $"{name}: the eye moved {eye:0.000} m");
            Assert.True(head <= allowance + 0.015f, $"{name}: the picture's eye moved {head:0.000} m");
            Assert.True(clear > 0f, $"{name}: the eye came {-clear:0.000} m inside the body's volumes");
        }
    }

    [Fact]
    public void With_the_allowance_off_the_eye_rides_the_head_through_a_blow_as_the_rigid_camera_does()
    {
        var r = Make(CameraSettings.Rigid);
        var (eye, _, pose, _) = Blow(r, false, 0);
        output.WriteLine($"the rigid camera's eye through quick1: {eye:0.000} m (the head's {pose:0.000} m)");
        Assert.True(eye > 0.4f, $"the eye moved {eye:0.000} m");
    }

    [Fact]
    public void At_a_walk_a_run_a_sprint_and_crouched_the_eye_is_as_it_was_looking_ahead()
    {
        foreach (var (name, frame) in new[] { ("walk", Ahead(Buttons.Walk)), ("run", Ahead(Buttons.None)), ("sprint", Ahead(Buttons.Sprint)), ("crouch", Frame(Buttons.Crouch)), ("crouch-walk", Ahead(Buttons.Crouch)) })
        {
            var now = Make();
            var then = Make(CameraSettings.Rigid);
            float low = float.MaxValue, high = float.MinValue, lead = 0f, apart = 0f, step = 0f;
            var last = PoseEyes(now);
            for (int t = 0; t < 240; t++)
            {
                Drive(now.World, frame, 1);
                Drive(then.World, frame, 1);
                var a = now.World.Service<ViewState>();
                var b = then.World.Service<ViewState>();
                var pose = PoseEyes(now) - now.World.Get<Body>(now.Player).Feet;
                // Once the gait is under way (a second on: a snap of the animation as it starts is eased out over 0.1 s, PopSeconds).
                if (t >= World.TicksPerSecond) (apart, step) = (MathF.Max(apart, MathF.Max(Vector3.Distance(b.Eyes, a.Eyes), Vector3.Distance(b.Head, a.Head))), MathF.Max(step, Vector3.Distance(pose, last)));
                last = pose;
                if (t < World.TicksPerSecond) continue;
                var feet = now.World.Get<Body>(now.Player).Feet;
                (low, high) = (MathF.Min(low, a.Eyes.Z - feet.Z), MathF.Max(high, a.Eyes.Z - feet.Z));
                lead = a.Eyes.Y - feet.Y;
            }
            output.WriteLine($"{name}: the eye {low:0.000} to {high:0.000} m over the feet, {lead:0.000} m ahead of them; from the rigid camera's at most {apart:0.0000} m (the head's most in a tick {step:0.000} m)");
            Assert.True(apart < 0.005f, $"{name}: the eye {apart:0.0000} m from the rigid camera's");
        }
    }

    [Fact]
    public void Looking_straight_down_standing_and_crouched_the_eye_leans_out_and_stays_outside_the_body()
    {
        foreach (bool crouched in new[] { false, true })
        {
            var r = Make();
            var then = Make(CameraSettings.Rigid);
            var held = crouched ? Buttons.Crouch : Buttons.None;
            Drive(r.World, Frame(held, -1.55f), 1);
            Drive(then.World, Frame(held, -1.55f), 1);
            Drive(r.World, Frame(held), 90);
            Drive(then.World, Frame(held), 90);
            var view = r.World.Service<ViewState>();
            var old = then.World.Service<ViewState>();
            float ahead = view.Eyes.Y - old.Eyes.Y;
            float clear = Clearance(r, FirstPersonView.Eye(view.Eyes, view.Head, 1f)), oldClear = Clearance(then, old.Eyes);
            output.WriteLine($"{(crouched ? "crouched" : "standing")}, looking down: the eye {ahead:0.000} m further ahead than the rigid camera's; its clearance of the body {clear:0.000} m (the rigid camera's {oldClear:0.000})");
            float lean = r.World.Service<CameraSettings>().LookDownAhead;
            Assert.InRange(ahead, lean * 0.9f, lean * 1.01f);
            Assert.True(clear > 0.05f, $"the eye is {clear:0.000} m from the body");
        }
    }

    [Fact]
    public void In_a_dodge_the_eye_keeps_the_duck_but_not_the_sway_and_stays_outside_the_body()
    {
        var r = Make();
        var view = r.World.Service<ViewState>();
        var feet0 = r.World.Get<Body>(r.Player).Feet;
        float standing = view.Eyes.Z - feet0.Z, lowest = float.MaxValue, sway = 0f, clear = float.MaxValue, pose = 0f;
        var start = PoseEyes(r) - feet0;
        for (int t = 0; t < World.TicksPerSecond; t++)
        {
            Drive(r.World, t == 0 ? new ActionFrame(new Vector2(-1, 0), Vector2.Zero, Buttons.Dodge, default) : ActionFrame.Idle, 1);
            var feet = r.World.Get<Body>(r.Player).Feet;
            lowest = MathF.Min(lowest, view.Eyes.Z - feet.Z);
            sway = MathF.Max(sway, MathF.Abs(view.Eyes.X - feet.X - (feet0.X - feet0.X) - 0f - (start.X)));
            pose = MathF.Max(pose, MathF.Abs(PoseEyes(r).X - feet.X - start.X));
            clear = MathF.Min(clear, Clearance(r, FirstPersonView.Eye(view.Eyes, view.Head, 1f)));
        }
        output.WriteLine($"a dodge to the left: the eye down {standing - lowest:0.000} m at most (the duck), across the body {sway:0.000} m (the pose's head {pose:0.000} m); its least clearance of the body {clear:0.000} m");
        Assert.True(standing - lowest > 0.1f, "the duck is kept (half of it)");
        Assert.True(sway <= r.World.Service<CameraSettings>().ActionAllowance + 0.065f, $"the eye swayed {sway:0.000} m across");
        Assert.True(clear > 0.02f, $"the eye came within {clear:0.000} m of the body");
    }

    [Fact]
    public void Through_a_two_metre_drop_the_first_person_eye_never_floats_over_the_head()
    {
        // A 2 m block from y 1 on; Christof on it, walking off its -Y edge.
        var r = Make(more: l => l.Box(new(-5, 1, 0), new(5, 8, 2)), at: new(0, 1.6f, 2.01f));
        var view = r.World.Service<ViewState>();
        float most = 0f, fell = 0f;
        for (int t = 0; t < 3 * World.TicksPerSecond; t++)
        {
            Drive(r.World, new ActionFrame(new Vector2(0, -1), Vector2.Zero, Buttons.Walk, default), 1);
            most = MathF.Max(most, view.Eyes.Z - PoseEyes(r).Z);
            fell = MathF.Max(fell, 2f - r.World.Get<Body>(r.Player).Feet.Z);
        }
        output.WriteLine($"fell {fell:0.00} m; the eye at most {most:0.000} m over the head's eyes");
        Assert.True(fell > 1.9f, $"fell only {fell:0.00} m");
        // The steady eye is the head's less the walk's bob (6.5 cm either way) and within the leash (6 cm) of that: the earlier build's eye,
        // eased as if the drop were a step, was held up to 0.45 m over the head.
        Assert.True(most < 0.15f, $"the eye floated {most:0.000} m over the head");
    }

    [Fact]
    public void Every_human_kind_the_platform_draws_has_its_head_and_neck_hidden_and_its_eyes_at_its_face()
    {
        string[] kinds = ["Christof", "Civ12male", "Civ03merchant1", "Civ10monk2", "Civ06Milkmaid", "Civ11female", "Civ07drunk", "Civ02beggar", "KnightOfStJohn_torch", "rudolfGuard"];
        foreach (var name in kinds)
        {
            var assets = new CharacterAssets();
            int kind = OneOneLibrary.Register(assets, Character(name));
            var head = assets.Kind(kind).Head;
            Assert.True(head is not null, $"{name} has no head to hide");
            var skeleton = assets.ModelOf(kind).Skeleton;
            var joint = Matrix4x4.Invert(skeleton.InverseBind[head!.Bone], out var bind) ? bind.Translation : default;
            output.WriteLine($"{name}: head bone {head.Bone}, neck {head.Neck}, {head.Bones.Length} bones hidden; the eyes {head.Eyes.Z:0.000} m up, {head.Eyes.Y - joint.Y:0.000} m ahead of the head's joint");
            int parent = skeleton.Bones[head.Bone].Parent;
            if (head.Neck < 0)
                output.WriteLine($"  {name}: the head's parent {parent} ({skeleton.Bones[parent].Name}) has children " +
                    string.Join(", ", Enumerable.Range(0, skeleton.Count).Where(b => skeleton.Bones[b].Parent == parent).Select(b => $"{b} {skeleton.Bones[b].Name}")) +
                    $"; its parent {skeleton.Bones[parent].Parent}");
            // A skeleton whose head hangs straight from the chest (the milkmaid's: the chest branches to the arms, the head and
            // another) has no neck bone: its neck's skin is the head's and goes with it.
            bool branching = Enumerable.Range(0, skeleton.Count).Count(b => skeleton.Bones[b].Parent == parent) > 1;
            Assert.True(head.Neck >= 0 || branching, $"{name}: a neck bone not hidden");
            Assert.InRange(head.Eyes.Z, 1.3f, 1.9f);
            Assert.InRange(head.Eyes.Y - joint.Y, 0.02f, 0.2f);
        }
    }

    [Fact]
    public void Third_person_a_steady_pace_has_no_lag_and_a_dodge_is_eased_within_the_leash()
    {
        foreach (var (name, cameras) in new[] { ("rigid", CameraSettings.Rigid), ("default", new CameraSettings()) })
        {
            var r = Make(cameras, first: false);
            var view = r.World.Service<ViewState>();
            var settings = r.World.Service<CameraSettings>();
            var rest = view.View.Eye - r.World.Get<Body>(r.Player).Feet;
            // A sprint: 2 s on, its last second at a steady pace.
            float drift = 0f;
            for (int t = 0; t < 2 * World.TicksPerSecond; t++)
            {
                Drive(r.World, Ahead(Buttons.Sprint), 1);
                if (t >= World.TicksPerSecond) drift = MathF.Max(drift, Vector3.Distance(view.View.Eye - r.World.Get<Body>(r.Player).Feet, rest));
            }
            Drive(r.World, ActionFrame.Idle, 2 * World.TicksPerSecond);
            // A dodge to the right from a stand: the camera's peak acceleration and how far it strays from its place by the body.
            var eyes = new List<Vector3>();
            float lag = 0f;
            for (int t = 0; t < World.TicksPerSecond; t++)
            {
                Drive(r.World, t == 0 ? new ActionFrame(new Vector2(1, 0), Vector2.Zero, Buttons.Dodge, default) : ActionFrame.Idle, 1);
                eyes.Add(view.View.Eye);
                lag = MathF.Max(lag, Vector3.Distance(view.View.Eye - r.World.Get<Body>(r.Player).Feet, rest));
            }
            float peak = 0f;
            for (int i = 2; i < eyes.Count; i++) peak = MathF.Max(peak, (eyes[i] - 2f * eyes[i - 1] + eyes[i - 2]).Length() / (World.TickSeconds * World.TickSeconds));
            output.WriteLine($"{name}: at a steady sprint the camera strays {drift:0.0000} m from its place by the body; in a dodge it strays {lag:0.000} m, its peak acceleration {peak:0} m/s²");
            // A few millimetres at most (0.05° seen from the camera 2.5 m back).
            Assert.True(drift < 0.005f, $"{name}: the camera lagged {drift:0.0000} m at a steady pace");
            if (name == "default")
            {
                Assert.True(peak < 150f, $"the dodge whipped the camera at {peak:0} m/s²");
                Assert.True(lag <= settings.ThirdPersonLeash + 1e-3f);
            }
            else Assert.True(peak > 300f, $"the rigid camera's camera took the dodge's burst at once ({peak:0} m/s²)");
        }
    }

    [Fact]
    public void Third_person_a_boom_pulled_in_by_a_wall_lets_out_eased()
    {
        var peaks = new Dictionary<string, float>();
        foreach (var (name, cameras) in new[] { ("rigid", CameraSettings.Rigid), ("default", new CameraSettings()) })
        {
            // A wall 1 m behind (−Y); Christof sprints away from it: the boom lengthens as it clears, and stops at its length.
            var r = Make(cameras, first: false, more: l => l.Box(new(-5, -3, 0), new(5, -1, 3)));
            var view = r.World.Service<ViewState>();
            float start = view.Boom;
            var eyes = new List<Vector3>();
            for (int t = 0; t < 3 * World.TicksPerSecond; t++)
            {
                Drive(r.World, Ahead(Buttons.Sprint), 1);
                eyes.Add(view.View.Eye);
            }
            float peak = 0f;
            for (int i = 2; i < eyes.Count; i++) peak = MathF.Max(peak, (eyes[i] - 2f * eyes[i - 1] + eyes[i - 2]).Length() / (World.TickSeconds * World.TickSeconds));
            peaks[name] = peak;
            output.WriteLine($"{name}: the boom from {start:0.00} m to {view.Boom:0.00} m; the camera's peak acceleration {peak:0} m/s²");
            Assert.True(start < 1.5f && view.Boom > 2.4f);
        }
        Assert.True(peaks["default"] < 100f && peaks["rigid"] > 2f * peaks["default"], $"rigid {peaks["rigid"]:0}, default {peaks["default"]:0} m/s²");
    }

    [Fact]
    public void The_camera_allocates_nothing_a_tick_in_either_view()
    {
        foreach (bool first in new[] { true, false })
        {
            var r = Make(first: first);
            var camera = new CameraSystem();
            Drive(r.World, Frame(Buttons.Attack), 1);
            for (int i = 0; i < 10; i++) camera.Run(r.World);
            // Measured as the allocation tests measure (Allocations.None: the window measured more than once).
            Allocations.None($"{(first ? "first" : "third")} person: the camera's 200 runs", () =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++) camera.Run(r.World);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            });
        }
    }

    [Fact]
    public void Stopping_from_a_run_the_eye_does_not_jump_with_the_animations_snap()
    {
        foreach (var (name, cameras) in new[] { ("rigid", CameraSettings.Rigid), ("default", new CameraSettings()) })
        {
            var r = Make(cameras);
            var view = r.World.Service<ViewState>();
            Drive(r.World, Ahead(Buttons.Sprint), 2 * World.TicksPerSecond);
            float eye = 0f, picture = 0f, pose = 0f;
            var (lastEye, lastPicture, lastPose) = (view.Eyes, FirstPersonView.Eye(view.Eyes, view.Head, 0.5f), PoseEyes(r));
            for (int t = 0; t < World.TicksPerSecond; t++)
            {
                Drive(r.World, ActionFrame.Idle, 1);
                var (e, p, h) = (view.Eyes, FirstPersonView.Eye(view.Eyes, view.Head, 0.5f), PoseEyes(r));
                (eye, picture, pose) = (MathF.Max(eye, Vector3.Distance(e, lastEye)), MathF.Max(picture, Vector3.Distance(p, lastPicture)), MathF.Max(pose, Vector3.Distance(h, lastPose)));
                (lastEye, lastPicture, lastPose) = (e, p, h);
            }
            output.WriteLine($"{name}: stopping from a sprint, the most in a tick: the head's eyes {pose:0.000} m, the steady eye {eye:0.000} m, the picture at 50 % {picture:0.000} m");
            Assert.True(pose > 0.2f, $"the pose did not snap ({pose:0.000} m): the animation has changed, and this test with it");
            if (name == "default") Assert.True(eye < 0.06f && picture < 0.06f, $"the eye jumped {eye:0.000} m, the picture {picture:0.000} m");
            else Assert.True(eye > 0.2f);
        }
    }

    [Fact]
    public void Turning_to_third_person_mid_sprint_the_camera_takes_up_the_pace_without_a_lag()
    {
        var r = Make();
        var view = r.World.Service<ViewState>();
        Drive(r.World, Ahead(Buttons.Sprint), World.TicksPerSecond);
        Drive(r.World, Ahead(Buttons.Sprint | Buttons.SwitchView), 1);
        Assert.Equal(ViewKind.Third, r.World.Get<CameraRig>(r.Player).View);
        Drive(r.World, Ahead(Buttons.Sprint), 2);
        var place = view.View.Eye - r.World.Get<Body>(r.Player).Feet;
        float most = 0f;
        for (int t = 0; t < World.TicksPerSecond / 2; t++)
        {
            Drive(r.World, Ahead(Buttons.Sprint), 1);
            most = MathF.Max(most, Vector3.Distance(view.View.Eye - r.World.Get<Body>(r.Player).Feet, place));
        }
        output.WriteLine($"turned to third person at a sprint: the camera strays {most:0.0000} m from its place by the body");
        Assert.True(most < 0.005f, $"the camera strayed {most:0.000} m");
    }

    /// <summary>The least distance between the segments <paramref name="p0"/>–<paramref name="p1"/> and <paramref name="q0"/>–<paramref name="q1"/>.</summary>
    static float SegmentDistance(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
    {
        float best = float.MaxValue;
        // Sampled finely enough for a check of sight (the segments are a few metres; 200 steps a centimetre or two).
        for (int i = 0; i <= 200; i++)
        {
            var p = Vector3.Lerp(p0, p1, i / 200f);
            var d = q1 - q0;
            float t = d.LengthSquared() > 1e-9f ? Math.Clamp(Vector3.Dot(p - q0, d) / d.LengthSquared(), 0f, 1f) : 0f;
            best = MathF.Min(best, Vector3.Distance(p, q0 + d * t));
        }
        return best;
    }

    /// <summary>
    /// A foe standing <paramref name="ahead"/> m before the player, facing him: which of his head, chest and belly the
    /// third-person camera sees past the player's own body (its hit volumes), and where his chest is on a 16:9 picture.
    /// </summary>
    (int Seen, string Parts, Vector2 Chest) Framing(CameraSettings cameras, float ahead, bool left)
    {
        var r = Make(cameras, first: false);
        int townsman = OneOneLibrary.Register(r.Assets, Character("Civ12male"));
        var foe = MovementServices.SpawnBody(r.World, new(0, ahead, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        r.World.Set(foe, CharacterAnimation.For(r.Assets, townsman));
        r.World.Set(foe, MotionInput.Standing);
        if (left) Drive(r.World, Frame(Buttons.SwitchView), 1);
        Drive(r.World, ActionFrame.Idle, World.TicksPerSecond);
        var view = r.World.Service<ViewState>().View;
        var mine = r.World.Service<HitVolumes>().Posed(r.World, r.Player).ToArray();
        var his = r.World.Service<HitVolumes>().Posed(r.World, foe).ToArray();
        int seen = 0;
        var parts = new List<string>();
        Vector2 chest = default;
        foreach (var region in new[] { BodyRegion.Head, BodyRegion.Chest, BodyRegion.Belly })
        {
            var v = his.First(h => h.Region == region).Capsule;
            var point = (v.A + v.B) * 0.5f;
            bool hidden = mine.Any(m => SegmentDistance(view.Eye, point, m.Capsule.A, m.Capsule.B) < m.Capsule.Radius);
            if (!hidden) { seen++; parts.Add(region.ToString().ToLowerInvariant()); }
            if (region == BodyRegion.Chest)
            {
                var forward = DetMath.Direction(view.Yaw, view.Pitch);
                var (sy, cy) = DetMath.SinCos(view.Yaw);
                var right = new Vector3(sy, -cy, 0f);
                var up = Vector3.Cross(right, forward);
                var d = point - view.Eye;
                float z = Vector3.Dot(d, forward), tan = DetMath.Tan(view.FieldOfViewDegrees * DetMath.DegToRad / 2f);
                chest = new Vector2(Vector3.Dot(d, right) / (z * tan * 16f / 9f), Vector3.Dot(d, up) / (z * tan));
            }
        }
        return (seen, string.Join("+", parts), chest);
    }

    [Fact]
    public void The_melee_framing_off_by_default_sees_a_foe_ahead_over_the_head_when_on()
    {
        Assert.False(new CameraSettings().MeleeFraming);
        var on = new CameraSettings { MeleeFraming = true };
        foreach (float ahead in new[] { 1.0f, 1.5f })
            foreach (bool left in new[] { false, true })
            {
                var (offSeen, offParts, offChest) = Framing(new CameraSettings(), ahead, left);
                var (onSeen, onParts, onChest) = Framing(on, ahead, left);
                output.WriteLine($"a foe {ahead} m ahead, over the {(left ? "left" : "right")} shoulder: off {offSeen} seen ({offParts}), chest at {offChest.X:0.00},{offChest.Y:0.00}; on {onSeen} seen ({onParts}), chest at {onChest.X:0.00},{onChest.Y:0.00}");
                Assert.True(onSeen >= (ahead < 1.2f ? 2 : 3), $"on, {ahead} m: only {onParts}");
                Assert.True(onSeen >= offSeen);
                Assert.True(MathF.Abs(onChest.X) < 0.1f && onChest.Y > -0.5f, "the foe stays near the picture's middle");
            }
    }
}
