// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the approach of OneOneCompositions, on the models repository's standard set.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// One key of a person's stand-in: where along it (0 to 1), a clip of the standard set and the frame of it (30 a second),
/// how far the hips are lowered from where that frame has them (m) and leaned forwards (radians), whether the legs are the
/// stand's, and both upper arms turned up before the body (<paramref name="ArmsUp"/>, degrees about the body's across axis:
/// 90 straight ahead, 180 straight up) or out to the sides (<paramref name="ArmsOut"/>, degrees), in the model's space.
/// </summary>
public sealed record PersonKey(float At, string Clip, float Frame, float Lower = 0f, float Lean = 0f, bool StandingLegs = false, float ArmsUp = 0f, float ArmsOut = 0f);

/// <summary>A stand-in for one slot: its keys, whether it loops, and its length (s; a move's slot is played by the move's progress whatever its length).</summary>
public sealed record PersonComposition(MotionRole Role, bool Loops, float Duration, IReadOnlyList<PersonKey> Keys);

/// <summary>
/// The stand-ins for the slots the models repository has no motion for yet ("the movements don't need to be
/// pretty but they need to roughly match what's going on"): as <see cref="Assets.OneOne.OneOneCompositions"/> does from 1.1's
/// poses, a few frames of the standard set's own motions — the stand, the run's longest stride and its flight, the kneel on
/// one knee on its way down — blended along the slot (<see cref="ComposedClip"/>), with the hips lowered and leaned and the
/// arms turned where no frame has the shape (no motion of the set raises both arms), and the <see cref="PoseRig"/> then putting
/// the hands on the ledge and the feet on the ground or the top by IK. A crouch is the stand lowered and leaned with the knees
/// bent by the IK, until the models repository's low crouch comes. A real clip in a slot replaces its stand-in with no other
/// change. Chosen by reasoning about the frames' joints, not yet by eye.
/// </summary>
public static class PeopleCompositions
{
    /// <summary>The stand-ins, by slot (every role 1.1's kinds compose, and the jump, the fall, the landing and the dodge).</summary>
    public static readonly IReadOnlyList<PersonComposition> Slots =
    [
        // Leaving the ground: from the run's push-off into its flight, the arms swinging up.
        new(MotionRole.Jump, false, 0.5f, [new(0f, "run", 6f, ArmsUp: 20f), new(0.5f, "run", 9f, ArmsUp: 40f), new(1f, "run", 9f, ArmsUp: 40f)]),
        // In the air: the run's flight held, the arms out for balance.
        new(MotionRole.Fall, true, 1f, [new(0f, "run", 9f, ArmsOut: 35f)]),
        // A hard landing: down onto bent knees, leaning, the arms out; back up to the stand.
        new(MotionRole.Land, false, 0.8f, [new(0f, "stand", 0f, 0.3f, 0.35f, ArmsOut: 20f), new(0.35f, "stand", 0f, 0.3f, 0.35f, ArmsOut: 20f), new(1f, "stand", 0f)]),
        // Crouched and still: the stand lowered 0.4 m and leaned, the knees bent by the IK over the planted feet.
        new(MotionRole.Crouch, true, 1f, [new(0f, "stand", 0f, 0.4f, 0.35f)]),
        // A hop up: the knees' dip, up with a knee drawn (the run's flight), down onto the top.
        new(MotionRole.HopUp, false, 1f, [new(0f, "stand", 0f), new(0.3f, "stand", 0f, 0.15f, 0.15f), new(0.6f, "run", 9f), new(1f, "stand", 0f)]),
        // A hurdle: the run's longest stride, held over the top (1.1's: run 4, 8, 15).
        new(MotionRole.Hurdle, false, 1f, [new(0f, "run", 4f), new(0.5f, "run", 8f), new(1f, "run", 15f)]),
        // A vault: crouched over the top, the arms forward and down to it (the hands put on it by IK).
        new(MotionRole.Vault, false, 1f, [new(0f, "stand", 0f), new(0.45f, "stand", 0f, 0.3f, 0.5f, ArmsUp: 60f), new(1f, "stand", 0f)]),
        // Climbing onto: the arms up to the edge, bent over it with a knee up (the kneel's way down), standing on the top.
        new(MotionRole.ClimbOnto, false, 1f, [new(0f, "stand", 0f, ArmsUp: 150f), new(0.45f, "kneel_one_down", 40f, 0f, 0.4f, ArmsUp: 90f),
                                              new(0.8f, "stand", 0f, 0.2f, 0.2f), new(1f, "stand", 0f)]),
        // A mantle: the arms up, pulled up and over (the knee up), standing on the top.
        new(MotionRole.Mantle, false, 1f, [new(0f, "stand", 0f, ArmsUp: 160f), new(0.4f, "stand", 0f, ArmsUp: 160f),
                                           new(0.65f, "kneel_one_down", 40f, 0f, 0.4f, ArmsUp: 80f), new(1f, "stand", 0f)]),
        // The grab: from the air, the arms up to the ledge.
        new(MotionRole.Grab, false, 1f, [new(0f, "stand", 0f, ArmsUp: 120f), new(0.4f, "stand", 0f, ArmsUp: 165f), new(1f, "stand", 0f, ArmsUp: 165f, StandingLegs: true)]),
        // Hanging: the body straight, the arms up, the legs as it stands.
        new(MotionRole.Hang, false, 1f, [new(0f, "stand", 0f, ArmsUp: 165f, StandingLegs: true)]),
        // The pull-up: hanging, up over the edge with a knee up, standing on the top.
        new(MotionRole.PullUp, false, 1f, [new(0f, "stand", 0f, ArmsUp: 165f, StandingLegs: true), new(0.45f, "kneel_one_down", 40f, 0f, 0.4f, ArmsUp: 90f),
                                           new(0.8f, "stand", 0f, 0.2f, 0.2f), new(1f, "stand", 0f)]),
        // Letting go: hanging, then the arms coming down as it falls.
        new(MotionRole.Drop, false, 1f, [new(0f, "stand", 0f, ArmsUp: 165f, StandingLegs: true), new(0.5f, "stand", 0f, ArmsUp: 90f), new(1f, "stand", 0f)]),
        // A dodge (played by its progress, its lowest pose as the burst ends): a duck with a lean, the arms out.
        new(MotionRole.Dodge, false, 1f, [new(0f, "stand", 0f), new(0.6f, "stand", 0f, 0.25f, 0.3f, ArmsOut: 15f), new(1f, "stand", 0f)]),
    ];

    /// <summary>
    /// The stand-ins for a person (one per slot of <see cref="Slots"/>), <paramref name="clip"/> finding a clip of its set by name,
    /// <paramref name="stand"/> standing in for one the set lacks. Named <c>&lt;kind&gt;:&lt;role&gt;</c>.
    /// </summary>
    public static IEnumerable<(MotionRole Role, ComposedClip Clip)> For(string kind, Skeleton skeleton, Assets.People.PersonRig rig, Func<string, IAnimationClip?> clip, IAnimationClip stand)
    {
        var still = new BoneTransform[skeleton.Count];
        stand.Sample(0f, skeleton, still);
        foreach (var slot in Slots)
        {
            var keys = new List<ComposedKey>(slot.Keys.Count);
            foreach (var k in slot.Keys) keys.Add(Key(k, skeleton, rig, clip(k.Clip) ?? stand, still));
            yield return (slot.Role, new ComposedClip($"{kind}:{slot.Role}", slot.Duration, slot.Loops, keys));
        }
    }

    /// <summary>One key's pose: the frame, the hips lowered and leaned, the legs the stand's if asked, the arms turned.</summary>
    public static ComposedKey Key(PersonKey k, Skeleton skeleton, Assets.People.PersonRig rig, IAnimationClip source, ReadOnlySpan<BoneTransform> still)
    {
        var pose = new BoneTransform[skeleton.Count];
        source.Sample(MathF.Min(k.Frame / 30f, source.Duration), skeleton, pose);
        if (k.StandingLegs)
            foreach (var leg in new[] { rig.LeftLeg, rig.RightLeg })
                foreach (var b in new[] { leg.Upper, leg.Lower, leg.End, leg.Tip })
                    if (b >= 0) pose[b] = still[b];
        int hips = rig.Pelvis;
        if (hips >= 0 && (k.Lower != 0f || k.Lean != 0f))
        {
            var p = pose[hips];
            var q = k.Lean != 0f ? LimbIK.Normalize(PoseMath.Multiply(LimbIK.Pitch(k.Lean), p.Rotation)) : p.Rotation;
            pose[hips] = new BoneTransform(q, p.Translation - new Vector3(0f, 0f, k.Lower));
        }
        // Arms: up before the body about +X (the body faces +Y: a hanging arm swings forward and up), out to the sides about ±Y.
        if (k.ArmsUp != 0f || k.ArmsOut != 0f)
            foreach (var (arm, side) in new[] { (rig.LeftArm, -1f), (rig.RightArm, 1f) })
            {
                if (!arm.Has) continue;
                var turn = Quaternion.Identity;
                if (k.ArmsOut != 0f) turn = DetMath.AxisAngle(Vector3.UnitY, -side * k.ArmsOut * DetMath.DegToRad);
                if (k.ArmsUp != 0f) turn = PoseMath.Multiply(DetMath.AxisAngle(Vector3.UnitX, k.ArmsUp * DetMath.DegToRad), turn);
                TurnInModel(skeleton, pose, arm.Upper, turn);
            }
        return new ComposedKey(k.At, pose);
    }

    /// <summary>Turns bone <paramref name="b"/> by <paramref name="turn"/>, a turn in the model's space about its joint.</summary>
    static void TurnInModel(Skeleton skeleton, Span<BoneTransform> local, int b, Quaternion turn)
    {
        int parent = skeleton.Bones[b].Parent;
        var qp = parent >= 0 && parent < b ? LimbIK.ModelRotation(skeleton, local, parent) : Quaternion.Identity;
        var seen = PoseMath.Multiply(LimbIK.Conjugate(qp), PoseMath.Multiply(turn, qp));
        local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(seen, local[b].Rotation)) };
    }
}
