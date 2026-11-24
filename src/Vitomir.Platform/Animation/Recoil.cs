// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1's blows never meet anything (a reach test and a roll), so nothing recoils.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The arm's recoil off a blocked blow (<i>"a kind of 'swing and weapon bounces off' animation kind
/// of how it looks in Bannerlord when a swing is blocked ... A kind of light rebound of the weapon like that is what I had in
/// mind"</i>; Bannerlord's developers: part of a blow's energy goes to <i>"kicking-back the attacker's hand(s)"</i>). No motion for it
/// exists, so it is stitched: the swing's own clip played backwards
/// (<see cref="FollowThrough.Rebound"/>) and, laid over it, this <b>additive layer</b> — the weapon hand pushed out of what the blade
/// met (along the contact's normal) by an amount from the blow's impulse, the shoulder and elbow following by two-bone IK
/// (<see cref="LimbIK.Solve"/>), the wrist tipping the blade back and the upper spine leaning a little with it; a kick that peaks at
/// <see cref="BladeStopRule.RecoilPeakSeconds"/> and springs back critically damped,
/// <c>x(t) = A · (t/T) · e^(1 − t/T)</c>, let go at <see cref="BladeStopRule.RecoilSettleSeconds"/>.
/// <para><b>Pose, not only presentation</b>: it is in <see cref="CharacterAnimation"/> (digested), set by contact's follow-through
/// and moved on by <see cref="RecoilSystem"/> before the pose rig, so the pose contact poses hit volumes from is the pose drawn: the
/// attacker's own arm is where it is drawn when the defender's answer comes. The blade itself is held (springing back, then
/// brought back: <see cref="FollowThrough.Held"/>) and contact sweeps no held blade, so the recoil meets nothing new.</para>
/// Scalar arithmetic and <see cref="DetMath"/> only; nothing allocated.
/// </summary>
public static class Recoil
{
    /// <summary>
    /// The kick begun on <paramref name="a"/> by a blade that met <paramref name="made"/>: its most
    /// min(<see cref="BladeStopRule.RecoilMetresPerImpulse"/> × the impulse, <see cref="BladeStopRule.RecoilMostMetres"/>) ×
    /// <paramref name="scale"/> (a parry's), along the contact's normal (out of what was met) in the body's model space.
    /// </summary>
    public static void Start(World world, EntityId a, ref CharacterAnimation anim, BladeStopRule rule, in ContactMade made, float scale)
    {
        float peak = MathF.Min(MathF.Max(0f, made.NormalImpulse) * rule.RecoilMetresPerImpulse, rule.RecoilMostMetres) * MathF.Max(0f, scale);
        if (peak <= 0f || rule.RecoilPeakSeconds <= 0f) return;
        float yaw = 0f, facing = 0f;
        if (world.TryStore<Body>(out var bodies))
        {
            ref var body = ref bodies.Find(a);
            if (!Unsafe.IsNullRef(ref body)) yaw = body.Yaw;
        }
        if (world.TryService<CharacterAssets>(out var assets) && (uint)anim.Kind < (uint)assets.KindCount) facing = assets.Kind(anim.Kind).FacingYaw;
        var (sin, cos) = DetMath.SinCos(yaw - facing);
        var n = made.Normal;
        var dir = new Vector3(cos * n.X + sin * n.Y, -sin * n.X + cos * n.Y, n.Z);   // the world's normal in the model's frame (PoseRig's ToModel)
        dir = LimbIK.Unit(dir);
        if (dir == Vector3.Zero) dir = -Vector3.UnitY;   // no normal kept: straight back (the model faces +Y)
        anim.RecoilDir = dir;
        anim.RecoilPeak = peak;
        anim.RecoilSince = world.Tick;
        anim.RecoilBack = Vector3.Zero;
        anim.RecoilLean = anim.RecoilWrist = 0f;
    }

    /// <summary>The kick's share of its most <paramref name="seconds"/> after it began: (t/T) e^(1 − t/T), 1 at <paramref name="peak"/>, 0 at 0 and from <paramref name="settle"/>.</summary>
    public static float Share(float seconds, float peak, float settle)
    {
        if (seconds <= 0f || peak <= 0f || seconds >= settle) return 0f;
        float u = seconds / peak;
        return u * DetMath.Exp(1f - u);
    }

    /// <summary>Moves one recoil on to <paramref name="tick"/> by <paramref name="rule"/>'s spring; let go (all zero) once settled.</summary>
    public static void Step(ref CharacterAnimation anim, BladeStopRule rule, long tick)
    {
        if (anim.RecoilPeak <= 0f) return;
        float t = (tick - anim.RecoilSince) * World.TickSeconds;
        if (t >= rule.RecoilSettleSeconds)
        {
            anim.RecoilPeak = 0f;
            anim.RecoilBack = anim.RecoilDir = Vector3.Zero;
            anim.RecoilLean = anim.RecoilWrist = 0f;
            anim.RecoilSince = 0;
            return;
        }
        float x = anim.RecoilPeak * Share(t, rule.RecoilPeakSeconds, rule.RecoilSettleSeconds);
        anim.RecoilBack = anim.RecoilDir * x;
        anim.RecoilLean = x * rule.RecoilLeanPerMetre;
        anim.RecoilWrist = x * rule.RecoilWristPerMetre;
    }

    /// <summary>
    /// The recoil laid on a pose (the evaluator calls it after the aim's bend): the spine leaned by <see cref="CharacterAnimation.RecoilLean"/>
    /// toward the push (each spine bone its share), then the weapon arm put by IK so its hand is <see cref="CharacterAnimation.RecoilBack"/>
    /// from where the pose had it, the hand turned by <see cref="CharacterAnimation.RecoilWrist"/> tipping the blade's point the same
    /// way. Nothing when there is no recoil, no limb map or no weapon hand. <paramref name="model"/> is scratch.
    /// </summary>
    public static void Apply(Skeleton skeleton, CharacterKind kind, in CharacterAnimation state, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (state.RecoilBack == Vector3.Zero || kind.Actions is not { } set) return;
        float scale = kind.Scale > 0f ? kind.Scale : 1f;
        var back = state.RecoilBack / scale;
        var dir = LimbIK.Unit(back);
        // The upper body leans with the push: about the axis that tips the model's up toward it.
        var axis = LimbIK.Unit(LimbIK.Cross(Vector3.UnitZ, dir));
        if (state.RecoilLean != 0f && axis != Vector3.Zero && set.Spine.Length > 0)
        {
            var (s, c) = DetMath.SinCos(state.RecoilLean / set.Spine.Length * 0.5f);
            for (int i = 0; i < set.Spine.Length; i++)
            {
                int b = set.Spine[i];
                if ((uint)b >= (uint)local.Length) continue;
                int parent = skeleton.Bones[b].Parent;
                var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), axis) : axis;
                seen = LimbIK.Unit(seen);
                var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
                local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
            }
        }
        if (kind.Limbs is not { } limbs || set.WeaponHand < 0) return;
        bool right = limbs.RightArm.Has && Under(skeleton, set.WeaponHand, limbs.RightArm.Upper);
        var arm = right ? limbs.RightArm : limbs.LeftArm.Has && Under(skeleton, set.WeaponHand, limbs.LeftArm.Upper) ? limbs.LeftArm : Limb.None;
        if (!arm.Has) return;
        PoseEvaluator.ToModel(skeleton, local, model);
        var target = model[arm.End].Translation + back;
        // The wrist: the blade's line turned toward the push, so its point kicks back further than the hand.
        Quaternion? wrist = null;
        if (state.RecoilWrist != 0f)
        {
            var held = kind.Held;
            for (int mi = 0; mi < held.Count && wrist is null; mi++)
                for (int ti = 0; ti < held[mi].Things.Count; ti++)
                {
                    var t = held[mi].Things[ti];
                    if (t.Kind != HeldKind.Blade || (uint)t.Bone >= (uint)model.Length) continue;
                    var along = LimbIK.Unit(PoseMath.TransformNormal(LimbIK.Unit(t.Tip - t.Guard), model[t.Bone]));
                    var w = LimbIK.Unit(LimbIK.Cross(along, dir));
                    if (w != Vector3.Zero) wrist = DetMath.AxisAngle(w, state.RecoilWrist);
                    break;
                }
        }
        LimbIK.Solve(skeleton, local, model, arm, target, 1f, new Vector3(right ? 0.6f : -0.6f, -0.2f, -0.8f), turnEnd: wrist);
    }

    static bool Under(Skeleton skeleton, int b, int top)
    {
        var bones = skeleton.Bones;
        for (int i = b, n = 0; i >= 0 && n < bones.Length; i = bones[i].Parent, n++)
        {
            if (i == top) return true;
            if (bones[i].Parent >= i) return false;
        }
        return false;
    }
}

/// <summary>
/// The recoils moved on (<see cref="AnimationRegistration.PosePhase"/>, after the animation and the legs' turn, before the pose
/// rig: <c>Move/207 platform.recoil</c>), each by the stop rule's spring (<see cref="ContactConfig.Stop"/>). Nothing allocated.
/// </summary>
public sealed class RecoilSystem : ISystem
{
    /// <summary>Its name, and its place after the animation (<see cref="AnimationRegistration.AnimationOrder"/> + 7: after the legs' turn, 5; before the rig, 10).</summary>
    public const string Name = "platform.recoil";
    public const int Order = 7;

    static readonly ContactConfig Defaults = new();

    public void Run(World world)
    {
        var rule = (world.TryService<ContactConfig>(out var cc) ? cc : Defaults).Stop;
        var anims = world.Store<CharacterAnimation>();
        var data = anims.Data;
        long tick = world.Tick;
        for (int i = 0; i < data.Length; i++)
            if (data[i].RecoilPeak > 0f) Recoil.Step(ref data[i], rule, tick);
    }
}
