// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rule is the character models' own (their reference twist tool), carried over to the platform's types and
// arithmetic.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// The twist bones' rule: the skin of an upper arm, a forearm and a thigh is shared between
/// twist bones along it (children of the bone, at rest exactly as it is), and the pose turns them from the other bones, so the
/// skin turns evenly along the limb instead of pinching where two bones meet. With T a bone's twist — the turn about its own
/// length (the line to its child, in its own frame) of <c>q0⁻¹ q</c>, q its local rotation and q0 its rest one — an upper
/// arm's or a thigh's twist bone at place u along it is turned by <c>(u − 1) T</c> about that line; a forearm's by
/// <c>u (T + Th) − T</c>, Th the hand's twist about the forearm's line (in the hand's own frame): the pronation spread from the
/// elbow to the wrist. Each twist bone's local rotation is its rest rotation times that turn. The places u are taken from the
/// bones' rest positions along the limb.
/// <para>
/// The clips carry the rule's result already; the platform applies it again <b>after everything that turns the limbs</b> (the
/// IK, the legs' turn, a blend of two clips), every pose (<see cref="PoseEvaluator.Evaluate(CharacterAssets, in CharacterAnimation, in PoseRig, Span{Matrix4x4}, Span{Matrix4x4}, Span{Matrix4x4})"/>).
/// Stateless: T is taken in (−π, π] (a roll stays well inside a half turn of rest; the reference unwraps over a clip, which
/// differs only past a half turn). <see cref="DetMath"/> and <see cref="PoseMath"/> only; allocates nothing.
/// </para>
/// </summary>
public sealed class TwistRule
{
    /// <summary>One twist bone: its index, its place along its limb (0 at the limb bone's joint, 1 at its child's), its rest rotation, and its limb's line in its own frame.</summary>
    public readonly record struct Bone(int Index, float Place, Quaternion Rest, Vector3 Axis);

    /// <summary>
    /// One driven limb bone (an upper arm, a forearm, a thigh): its index, its rest rotation, its line (unit, in its own frame),
    /// for a forearm its hand, the hand's rest rotation and the forearm's line in the hand's frame (else −1), and its twist bones.
    /// </summary>
    public sealed record Limb(string Name, int Index, Quaternion Rest, Vector3 Axis, int Hand, Quaternion HandRest, Vector3 HandAxis, Bone[] Bones);

    /// <summary>The driven limbs: each side's upper arm, forearm and thigh.</summary>
    public required Limb[] Limbs { get; init; }

    /// <summary>
    /// The rule for <paramref name="skeleton"/> by the models repository's names (<c>upperarm_twist_01_l</c> …, children of
    /// <c>upperarm_l</c>, <c>lowerarm_l</c>, <c>thigh_l</c>), or null when it has no twist bones.
    /// </summary>
    public static TwistRule? For(Skeleton skeleton)
    {
        var limbs = new List<Limb>();
        foreach (var (bone, child) in new[] { ("upperarm", "lowerarm"), ("lowerarm", "hand"), ("thigh", "calf") })
            foreach (var side in new[] { "l", "r" })
            {
                int j = skeleton.Find($"{bone}_{side}"), k = skeleton.Find($"{child}_{side}");
                if (j < 0 || k < 0) continue;
                var line = skeleton.Bones[k].Rest.Translation;
                float length = LimbIK.Length(line);
                if (length < 1e-6f) continue;
                var axis = line / length;
                var rest = skeleton.Bones[j].Rest.Rotation;
                int hand = -1;
                Quaternion handRest = Quaternion.Identity;
                Vector3 handAxis = default;
                if (bone == "lowerarm")
                {
                    hand = k;
                    handRest = skeleton.Bones[k].Rest.Rotation;
                    handAxis = LimbIK.Rotate(LimbIK.Conjugate(handRest), axis);
                }
                var twists = new List<Bone>();
                for (int b = 0; b < skeleton.Count; b++)
                {
                    var info = skeleton.Bones[b];
                    if (info.Parent != j || !info.Name.StartsWith($"{bone}_twist_", StringComparison.OrdinalIgnoreCase)
                        || !info.Name.EndsWith($"_{side}", StringComparison.OrdinalIgnoreCase)) continue;
                    float u = Math.Clamp(LimbIK.Dot(info.Rest.Translation, axis) / length, 0f, 1f);
                    twists.Add(new Bone(b, u, info.Rest.Rotation, LimbIK.Rotate(LimbIK.Conjugate(info.Rest.Rotation), axis)));
                }
                if (twists.Count > 0) limbs.Add(new Limb($"{bone}_{side}", j, rest, axis, hand, handRest, handAxis, [.. twists]));
            }
        return limbs.Count == 0 ? null : new TwistRule { Limbs = [.. limbs] };
    }

    /// <summary>Sets every twist bone's local rotation in <paramref name="local"/> from its limb's (and a forearm's hand's) by the rule.</summary>
    public void Apply(Span<BoneTransform> local)
    {
        foreach (var limb in Limbs)
        {
            if ((uint)limb.Index >= (uint)local.Length) continue;
            float t = Twist(PoseMath.Multiply(LimbIK.Conjugate(limb.Rest), local[limb.Index].Rotation), limb.Axis);
            float th = limb.Hand >= 0 && limb.Hand < local.Length
                ? Twist(PoseMath.Multiply(LimbIK.Conjugate(limb.HandRest), local[limb.Hand].Rotation), limb.HandAxis) : 0f;
            foreach (var b in limb.Bones)
            {
                if ((uint)b.Index >= (uint)local.Length) continue;
                float angle = limb.Hand >= 0 ? b.Place * (t + th) - t : (b.Place - 1f) * t;
                var (s, c) = DetMath.SinCos(angle * 0.5f);
                var turn = new Quaternion(b.Axis.X * s, b.Axis.Y * s, b.Axis.Z * s, c);
                local[b.Index] = local[b.Index] with { Rotation = PoseMath.Multiply(b.Rest, turn) };
            }
        }
    }

    /// <summary>The twist of the rotation <paramref name="d"/> about the unit <paramref name="axis"/>, radians in (−π, π].</summary>
    public static float Twist(Quaternion d, Vector3 axis)
    {
        float p = d.X * axis.X + d.Y * axis.Y + d.Z * axis.Z;
        if (p == 0f && d.W == 0f) return 0f;
        return DetMath.WrapAngle(2f * DetMath.Atan2(p, d.W));
    }
}
