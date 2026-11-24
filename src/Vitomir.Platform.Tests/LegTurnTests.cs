// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The legs' turn (Animation/LegTurn.cs): a body facing one way and moving another has its hips and legs
/// turned toward the move within 60°, toward its reverse when it moves backwards, eased at 360° a second so a change of the
/// keys never pops; standing, its legs keep their place in the world until the twist passes 50°, then catch up at 270° a
/// second, never twisted past 90°; off the ground they ease back; switched off, nothing turns. Laid on a pose, the hips and
/// legs turn about the model's up and the chest stays where it was; in a world, the pose the hit volumes ride on turns with
/// it, the same ticks make the same turn, and nothing is allocated per tick.
/// </summary>
public sealed class LegTurnTests
{
    const float Dt = World.TickSeconds;
    const float Deg = DetMath.DegToRad;
    static readonly LegTurnSettings S = new();

    static MotionInput Moving(float forward, float left) => new()
    {
        Grounded = true, ForwardSpeed = forward, SideSpeed = left, GroundSpeed = MathF.Sqrt(forward * forward + left * left),
    };

    [Fact]
    public void The_legs_turn_toward_the_move_within_their_limit_and_toward_its_reverse_backwards()
    {
        Assert.Equal(0f, LegTurn.Toward(3f, 0f, 3f, S), 1e-4f);                     // ahead
        Assert.Equal(60f * Deg, LegTurn.Toward(0f, 3f, 3f, S), 1e-4f);             // a sidestep left: the most, 60°
        Assert.Equal(-60f * Deg, LegTurn.Toward(0f, -3f, 3f, S), 1e-4f);           // right
        float d = 3f / MathF.Sqrt(2f);
        Assert.Equal(45f * Deg, LegTurn.Toward(d, d, 3f, S), 1e-3f);               // ahead and left: all of it
        Assert.Equal(0f, LegTurn.Toward(-3f, 0f, 3f, S), 1e-4f);                   // a backpedal: the legs on the facing, the clip backwards
        Assert.Equal(-45f * Deg, LegTurn.Toward(-d, d, 3f, S), 1e-3f);             // back and left: toward the reverse, ahead and right
        Assert.Equal(45f * Deg, LegTurn.Toward(-d, -d, 3f, S), 1e-3f);
        // 100° from the facing is not yet backwards (the animation's rule, 105°): toward the move, held to 60°.
        var (s, c) = DetMath.SinCos(100f * Deg);
        Assert.Equal(60f * Deg, LegTurn.Toward(3f * c, 3f * s, 3f, S), 1e-4f);
        Assert.Equal(0f, LegTurn.Toward(0f, 0f, 0f, S));
    }

    [Fact]
    public void The_turn_eases_at_its_rate_so_a_change_of_the_keys_never_pops()
    {
        var rig = new PoseRig();
        float yaw = 0.3f;
        LegTurn.Step(ref rig, MotionRole.Run, Moving(0f, 3f), yaw, S, Dt);
        Assert.Equal(S.Rate * Dt, rig.LegYaw, 1e-5f);                               // 3° on the first tick
        for (int i = 0; i < 30; i++) LegTurn.Step(ref rig, MotionRole.Run, Moving(0f, 3f), yaw, S, Dt);
        Assert.Equal(60f * Deg, rig.LegYaw, 1e-4f);                                  // there within a sixth of a second
        // The keys swung to the right: no tick moves the legs more than the rate's step, and they come to −60°.
        float last = rig.LegYaw;
        for (int i = 0; i < 60; i++)
        {
            LegTurn.Step(ref rig, MotionRole.Run, Moving(0f, -3f), yaw, S, Dt);
            Assert.True(MathF.Abs(rig.LegYaw - last) <= S.Rate * Dt + 1e-5f);
            last = rig.LegYaw;
        }
        Assert.Equal(-60f * Deg, rig.LegYaw, 1e-4f);
        // Off the ground (a jump), they ease back to the facing.
        for (int i = 0; i < 60; i++) LegTurn.Step(ref rig, MotionRole.Fall, new MotionInput { Grounded = false }, yaw, S, Dt);
        Assert.Equal(0f, rig.LegYaw, 1e-5f);
    }

    [Fact]
    public void Standing_the_legs_stay_planted_until_the_twist_passes_its_limit_then_catch_up()
    {
        var rig = new PoseRig();
        var still = MotionInput.Standing;
        LegTurn.Step(ref rig, MotionRole.Idle, still, 0f, S, Dt);
        // The view (and the body's yaw) turned 30° left: the legs keep their place, the spine twisted 30°.
        for (int i = 0; i < 120; i++) LegTurn.Step(ref rig, MotionRole.Idle, still, 30f * Deg, S, Dt);
        Assert.Equal(-30f * Deg, rig.LegYaw, 1e-4f);
        Assert.Equal(LegMode.Follow, rig.LegMode);
        // To 60°: past the 50° limit, the legs catch the chest up — all the way round, at the catch-up rate.
        LegTurn.Step(ref rig, MotionRole.Idle, still, 60f * Deg, S, Dt);
        Assert.Equal(LegMode.CatchUp, rig.LegMode);
        Assert.Equal(-60f * Deg + S.CatchUpRate * Dt, rig.LegYaw, 1e-4f);
        int ticks = 1;
        while (rig.LegYaw != 0f && ticks < 1000) { LegTurn.Step(ref rig, MotionRole.Idle, still, 60f * Deg, S, Dt); ticks++; }
        Assert.InRange(ticks, 26, 28);                                               // 60° at 270°/s: 0.22 s
        Assert.Equal(LegMode.Follow, rig.LegMode);
        // 120° at once: never twisted past 90° (the legs dragged round), then caught up.
        LegTurn.Step(ref rig, MotionRole.Idle, still, 180f * Deg, S, Dt);
        Assert.Equal(-90f * Deg, rig.LegYaw, 1e-4f);
        LegTurn.Step(ref rig, MotionRole.Idle, still, 180f * Deg, S, Dt);
        Assert.Equal(-90f * Deg + S.CatchUpRate * Dt, rig.LegYaw, 1e-4f);
    }

    [Fact]
    public void Switched_off_nothing_turns()
    {
        var off = new LegTurnSettings { On = false };
        var rig = new PoseRig();
        for (int i = 0; i < 60; i++) LegTurn.Step(ref rig, MotionRole.Run, Moving(0f, 3f), i * 0.05f, off, Dt);
        Assert.Equal(new PoseRig(), rig);
    }

    /// <summary>The made-up person (TraversalPoseTests.Person) with a spine for the turn: bones 9 and 10 (spine, chest).</summary>
    static (CharacterAssets Assets, int Kind) PersonWithSpine()
    {
        var (assets, kind) = TraversalPoseTests.PersonKind();
        var k = assets.Kind(kind);
        var turned = new CharacterKind
        {
            Name = "person, turning", Model = k.Model, Motions = k.Motions, FacingYaw = k.FacingYaw, Limbs = k.Limbs,
            Actions = new ActionSet { Quick = [], UpperBody = new bool[20], Spine = [9, 10] },
        };
        return (assets, assets.AddKind(turned));
    }

    [Fact]
    public void Laid_on_a_pose_the_hips_and_legs_turn_about_the_up_and_the_chest_stays()
    {
        var (assets, kind) = PersonWithSpine();
        var skeleton = assets.ModelOf(kind).Skeleton;
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var rest = new Matrix4x4[n];
        var turned = new Matrix4x4[n];
        PoseEvaluator.Rest(skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, rest);
        float yaw = 40f * Deg;
        LegTurn.Apply(skeleton, 0, [9, 10], yaw, local, new Matrix4x4[n]);
        PoseEvaluator.ToModel(skeleton, local, turned);
        var hips = rest[0].Translation;
        // Each ankle (3, 7) and ball (4, 8) is where the rest had it, turned 40° counter-clockwise about the hips' upright.
        var (s, c) = DetMath.SinCos(yaw);
        foreach (int b in new[] { 3, 4, 7, 8 })
        {
            var d = rest[b].Translation - hips;
            var want = hips + new Vector3(d.X * c - d.Y * s, d.X * s + d.Y * c, d.Z);
            Assert.True(Vector3.Distance(want, turned[b].Translation) < 1e-4f, $"bone {b}: {turned[b].Translation} not {want}");
        }
        // The chest, the head and the arms are where they were: the spine turned back by the same.
        foreach (int b in new[] { 10, 11, 12, 15, 16, 19 })
            Assert.True(Vector3.Distance(rest[b].Translation, turned[b].Translation) < 1e-4f, $"bone {b} moved");
        var chestFacing = Vector3.TransformNormal(Vector3.UnitY, turned[10]);
        Assert.Equal(0f, chestFacing.X, 1e-4f);
    }

    [Fact]
    public void In_a_world_a_body_moving_aside_turns_the_pose_the_hit_volumes_ride_on_and_the_same_ticks_turn_it_the_same()
    {
        var (assets, kind) = PersonWithSpine();
        var level = new LevelSpace("floor", new TestLevel().Floor(-20, -20, 20, 20, cell: 4f).Build("floor").Collision) { Places = LevelPlaces.None };
        (World W, EntityId E) Make()
        {
            var b = new WorldBuilder(5).Component<Body>().Component<Traversal>().Component<MoveIntent>().Service(level).Animation(assets);
            BodyMotionSystem.Register(b);
            var w = b.Build();
            var e = w.Create();
            // Facing +Y (yaw 90°) and moving to its left (−X) at a run, as a strafe; the body is moved by hand.
            w.Set(e, Body.At(new Vector3(0, 0, 0), DetMath.HalfPi) with { State = BodyState.Ground, Velocity = new Vector3(-3f, 0f, 0f) });
            w.Set(e, new Traversal());
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            return (w, e);
        }
        var (a, ea) = Make();
        var (bw, eb) = Make();
        var before = a.Service<CharacterPoses>().Bones(a, ea)[3].Translation;
        for (int i = 0; i < 60; i++)
        {
            a.Step();
            bw.Step();
            Assert.Equal(a.Digest(), bw.Digest());
        }
        // Nothing allocated per tick (the pose asked for each tick, as the contact and the picture ask).
        a.Service<CharacterPoses>().Bones(a, ea);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++) { a.Step(); a.Service<CharacterPoses>().Bones(a, ea); }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(60f * Deg, a.Get<PoseRig>(ea).LegYaw, 1e-4f);
        Assert.Equal(a.Get<PoseRig>(ea), bw.Get<PoseRig>(eb));
        var bones = a.Service<CharacterPoses>().Bones(a, ea);
        var hips = bones[0].Translation;
        var ankle = bones[3].Translation;
        // The left ankle (out to the model's −X, a little back) turned 60° counter-clockwise about the hips.
        var (s, c) = DetMath.SinCos(60f * Deg);
        var d = before - hips;
        var want = hips + new Vector3(d.X * c - d.Y * s, d.X * s + d.Y * c, d.Z);
        Assert.True(MathF.Abs(want.X - ankle.X) < 0.02f && MathF.Abs(want.Y - ankle.Y) < 0.02f, $"{ankle} not {want}");
        Assert.True(allocated < 1024, $"{allocated} bytes allocated in 30 ticks");

        // The turn is wholly its components (there is no save of a world yet): a fresh world given them goes on as the first.
        var (fresh, ef) = Make();
        fresh.Set(ef, a.Get<Body>(ea));
        fresh.Set(ef, a.Get<CharacterAnimation>(ea));
        fresh.Set(ef, a.Get<MotionInput>(ea));
        fresh.Set(ef, a.Get<PoseRig>(ea));
        a.Get<Body>(ea).Velocity = new Vector3(0f, -3f, 0f);      // now backwards: the legs swing back to the facing
        fresh.Get<Body>(ef).Velocity = new Vector3(0f, -3f, 0f);
        for (int i = 0; i < 40; i++)
        {
            a.Step();
            fresh.Step();
            Assert.Equal(a.Get<PoseRig>(ea).LegYaw, fresh.Get<PoseRig>(ef).LegYaw);
        }
        Assert.Equal(0f, a.Get<PoseRig>(ea).LegYaw, 1e-4f);
    }

    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    /// <summary>Christof facing the view (the player, third person) after a second of <paramref name="keys"/> at a run, legs turning or not.</summary>
    static (World W, EntityId P) Christof(bool legs, Vector2 keys)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Library.Value!.Character("Christof", "BroadSword") ?? throw new Xunit.Sdk.XunitException("Christof did not load"));
        var b = new WorldBuilder(7);
        MovementServices.Register(b, new TestLevel().Floor(-30, -30, 30, 30).Build());
        b.Animation(assets, legs: new LegTurnSettings { On = legs });
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        w.Service<TickInput>().ThirdPersonFacing = ThirdPersonFacing.View;
        var p = MovementServices.SpawnControlled(w, new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Set(p, CharacterAnimation.For(assets, kind));
        w.Set(p, MotionInput.Standing);
        for (int i = 0; i < World.TicksPerSecond; i++) MovementServices.Step(w, new ActionFrame(keys, default, Buttons.None, default));
        return (w, p);
    }

    static Vector3 Middle(ReadOnlySpan<HitVolume> volumes, BodyRegion region)
    {
        foreach (var v in volumes) if (v.Region == region) return (v.Capsule.A + v.Capsule.B) * 0.5f;
        throw new Xunit.Sdk.XunitException($"no {region} volume");
    }

    [Fact]
    public void Christofs_hit_volumes_turn_with_his_legs_and_his_chest_stays_on_the_view()
    {
        // A sidestep left at a run, facing the view (+X): the same feet both ways (no speed by direction in these worlds).
        var (on, pon) = Christof(true, new Vector2(-1, 0));
        var (off, poff) = Christof(false, new Vector2(-1, 0));
        Assert.Equal(on.Get<Body>(pon).Feet, off.Get<Body>(poff).Feet);
        Assert.Equal(60f * Deg, on.Get<PoseRig>(pon).LegYaw, 1e-3f);
        var a = on.Service<HitVolumes>().Posed(on, pon).ToArray();
        var b = off.Service<HitVolumes>().Posed(off, poff).ToArray();
        // The head, the arms and the hands stay where they were, to a millimetre (the spine turned back and moved back at its
        // root); the chest's volume, on a spine bone partly turned, within a couple of centimetres.
        foreach (var r in new[] { BodyRegion.Head, BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm, BodyRegion.LeftHand, BodyRegion.RightHand })
            Assert.True(Vector3.Distance(Middle(a, r), Middle(b, r)) < 0.002f, $"{r}: {Middle(a, r)} not {Middle(b, r)}");
        Assert.True(Vector3.Distance(Middle(a, BodyRegion.Chest), Middle(b, BodyRegion.Chest)) < 0.02f, $"the chest: {Middle(a, BodyRegion.Chest)} not {Middle(b, BodyRegion.Chest)}");
        // The feet: from the hips, each foot's volume is turned counter-clockwise (toward the move, +Y) by about the legs' turn.
        var hipsA = Middle(a, BodyRegion.Hips);
        var hipsB = Middle(b, BodyRegion.Hips);
        foreach (var region in new[] { BodyRegion.LeftFoot, BodyRegion.RightFoot })
        {
            var da = Middle(a, region) - hipsA;
            var db = Middle(b, region) - hipsB;
            float turned = DetMath.WrapAngle(DetMath.Atan2(da.Y, da.X) - DetMath.Atan2(db.Y, db.X));
            Assert.InRange(turned, 30f * Deg, 90f * Deg);
        }
    }
}
