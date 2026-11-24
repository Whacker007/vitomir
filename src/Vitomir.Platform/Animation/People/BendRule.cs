// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rule is the character models' own (their reference bend tool, its numbers bend.P), carried over to the platform's
// types and arithmetic.
using System.Numerics;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// The bend helpers' rule (the models repository): two helpers at each knee
/// (<c>knee_bend_front_l</c>, <c>knee_bend_back_l</c>, children of the thigh) and each hip (<c>hip_bend_front_l</c>,
/// <c>hip_bend_back_l</c>, children of the pelvis), each at the lower bone's joint with its parent's own axes, keep the skin's depth
/// where the joint bends far. Per helper, with no state from the frame before, in the upper bone's frame: a0 the lower bone's line
/// at rest and a1 its line now (to its own child: the foot from the calf, the calf from the thigh), r the helper's fixed direction
/// (forward for a front helper, backward for a back one, square to a0, from the body's heel-to-toe forward at rest):
/// <list type="bullet">
/// <item>its local rotation <b>half the shortest turn from a0 to a1</b> (the lower bone's own twist left out);</item>
/// <item>its local translation its rest place plus <b>radius × |a0 − a1| × (k_out × max(0, r·o) + k_in × max(0, −r·o))</b> along r
/// turned by that half turn, o = (a0 − a1)/|a0 − a1| the outside of the bend.</item>
/// </list>
/// The numbers are <c>bend.P</c>'s: the knee radius 0.055 m, k_out 0.15, k_in 0.6; the hip 0.08 m, 0.10, 1.0. Every clip keys the
/// helpers by this rule, so plain playback needs nothing; the platform applies it <b>after its own IK on the legs</b> (the planted
/// feet, the ground, the heel lift: <see cref="PersonFeet"/>), as it applies the twist rule (<see cref="TwistRule"/>) after
/// everything that turns the limbs. The knee's back helper carries no skin but is posed all the same.
/// <see cref="LimbIK"/>'s arithmetic only (no trigonometry); allocates nothing.
/// </summary>
public sealed class BendRule
{
    /// <summary>One helper: its bone, its rest translation (the lower bone's joint, in the upper bone's frame) and its direction r there.</summary>
    public readonly record struct Helper(int Index, Vector3 Rest, Vector3 Direction);

    /// <summary>
    /// One joint of one side: its lower bone, the line to the lower bone's child in the lower bone's own frame (<c>g</c>), that line at
    /// rest in the upper bone's frame (a0), the rule's numbers, and its two helpers (front, back).
    /// </summary>
    public sealed record Joint(string Name, int Lower, Vector3 Line, Vector3 Rest, float Radius, float Out, float In, Helper[] Helpers);

    /// <summary>The knee's numbers (<c>bend.P["knee"]</c>): radius (m), k_out, k_in.</summary>
    public const float KneeRadius = 0.055f, KneeOut = 0.15f, KneeIn = 0.6f;

    /// <summary>The hip's numbers (<c>bend.P["hip"]</c>).</summary>
    public const float HipRadius = 0.08f, HipOut = 0.10f, HipIn = 1.0f;

    /// <summary>The joints: each side's knee, then each side's hip.</summary>
    public required Joint[] Joints { get; init; }

    /// <summary>
    /// The rule for <paramref name="skeleton"/> by the models repository's names (the 86 bones), or null when it lacks any of the
    /// eight helpers or the bones they hang from (a skeleton; 1.1's).
    /// </summary>
    public static BendRule? For(Skeleton skeleton)
    {
        int F(string n) => skeleton.Find(n);
        int footL = F("foot_l"), footR = F("foot_r"), ballL = F("ball_l"), ballR = F("ball_r"), head = F("head");
        if (footL < 0 || footR < 0 || ballL < 0 || ballR < 0 || head < 0) return null;
        int n = skeleton.Count;
        var rest = new BoneTransform[n];
        var model = new Matrix4x4[n];
        PoseEvaluator.Rest(skeleton, rest);
        PoseEvaluator.ToModel(skeleton, rest, model);
        Vector3 At(int b) => model[b].Translation;
        // The body's forward at rest: heel to toe, both feet, level (square to the feet-to-head line), as bend.front_dir.
        var up = LimbIK.Unit(At(head) - (At(footL) + At(footR)) * 0.5f);
        var forward = At(ballL) - At(footL) + (At(ballR) - At(footR));
        forward = LimbIK.Unit(forward - up * LimbIK.Dot(forward, up));
        if (forward == Vector3.Zero || up == Vector3.Zero) return null;

        var joints = new List<Joint>(4);
        foreach (var (joint, upperName, lowerName, childName, radius, kOut, kIn) in new[]
                 {
                     ("knee", "thigh_{0}", "calf_{0}", "foot_{0}", KneeRadius, KneeOut, KneeIn),
                     ("hip", "pelvis", "thigh_{0}", "calf_{0}", HipRadius, HipOut, HipIn),
                 })
            foreach (var side in new[] { "l", "r" })
            {
                int upper = F(string.Format(System.Globalization.CultureInfo.InvariantCulture, upperName, side));
                int lower = F(string.Format(System.Globalization.CultureInfo.InvariantCulture, lowerName, side));
                int child = F(string.Format(System.Globalization.CultureInfo.InvariantCulture, childName, side));
                int front = F($"{joint}_bend_front_{side}"), back = F($"{joint}_bend_back_{side}");
                if (upper < 0 || lower < 0 || child < 0 || front < 0 || back < 0) return null;
                if (skeleton.Bones[front].Parent != upper || skeleton.Bones[back].Parent != upper) return null;
                var g = LimbIK.Unit(skeleton.Bones[child].Rest.Translation);     // the lower bone's line in its own frame
                if (g == Vector3.Zero) return null;
                var a0 = LimbIK.Unit(LimbIK.Rotate(skeleton.Bones[lower].Rest.Rotation, g));
                // Forward in the upper bone's frame at rest, square to a0.
                var f = LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, rest, upper)), forward);
                f = LimbIK.Unit(f - a0 * LimbIK.Dot(f, a0));
                if (f == Vector3.Zero) return null;
                joints.Add(new Joint($"{joint}_{side}", lower, g, a0, radius, kOut, kIn,
                                     [new Helper(front, skeleton.Bones[front].Rest.Translation, f), new Helper(back, skeleton.Bones[back].Rest.Translation, -f)]));
            }
        return new BendRule { Joints = [.. joints] };
    }

    /// <summary>Sets the eight helpers' local rotations and translations in <paramref name="local"/> from the knees' and hips' by the rule.</summary>
    public void Apply(Span<BoneTransform> local)
    {
        foreach (var joint in Joints) Apply(joint, local);
    }

    /// <summary>One joint's helpers by the rule (its lower bone as <paramref name="local"/> has it now).</summary>
    public static void Apply(Joint joint, Span<BoneTransform> local)
    {
        if ((uint)joint.Lower >= (uint)local.Length) return;
        var a0 = joint.Rest;
        var a1 = LimbIK.Unit(LimbIK.Rotate(local[joint.Lower].Rotation, joint.Line));
        if (a1 == Vector3.Zero) return;
        var q = HalfSwing(a0, a1);
        var d = a0 - a1;
        float b = LimbIK.Length(d);
        var o = d / MathF.Max(b, 1e-9f);
        foreach (var h in joint.Helpers)
        {
            if ((uint)h.Index >= (uint)local.Length) continue;
            float ro = LimbIK.Dot(o, h.Direction);
            float amount = joint.Radius * b * (joint.Out * MathF.Max(0f, ro) + joint.In * MathF.Max(0f, -ro));
            local[h.Index] = new BoneTransform(q, h.Rest + LimbIK.Rotate(q, h.Direction) * amount);
        }
    }

    /// <summary>
    /// The turn halfway from unit <paramref name="a0"/> to unit <paramref name="a1"/>, the shortest way (<c>bend.half_swing</c>): the
    /// turn from a0 to the halfway line h, made from the quarter-way line m as (a0 × m, a0 · m). Folded right back: none.
    /// </summary>
    public static Quaternion HalfSwing(Vector3 a0, Vector3 a1)
    {
        var h = a0 + a1;
        float n = LimbIK.Length(h);
        h = n > 1e-9f ? h / n : a0;
        var m = LimbIK.Unit(a0 + h);
        var c = LimbIK.Cross(a0, m);
        return new Quaternion(c.X, c.Y, c.Z, LimbIK.Dot(a0, m));
    }
}
