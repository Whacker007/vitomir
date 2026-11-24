// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// One limb of a skeleton for IK (ours): the bone whose origin is the limb's root joint (a hip, a shoulder), the bone
/// whose origin is its middle joint (a knee, an elbow), the bone whose origin is its end (an ankle, a wrist), and the
/// bone beyond the end whose origin is the limb's tip (the ball of the foot, the knuckles). −1 for none.
/// </summary>
public readonly record struct Limb(int Upper, int Lower, int End, int Tip)
{
    public static Limb None => new(-1, -1, -1, -1);

    public bool Has => Upper >= 0 && Lower >= 0 && End >= 0;

    /// <summary>
    /// The limb ending in <paramref name="tip"/>: its parent is the end, that one's the middle joint and that one's
    /// the root joint, as 1.1's people are boned (Christof's left leg: tip 4, ankle 3, knee 2, hip 1). None when the
    /// chain is shorter.
    /// </summary>
    public static Limb FromTip(Skeleton skeleton, int tip)
    {
        if ((uint)tip >= (uint)skeleton.Count) return None;
        int end = Parent(skeleton, tip), lower = Parent(skeleton, end), upper = Parent(skeleton, lower);
        return upper >= 0 ? new Limb(upper, lower, end, tip) : None;
    }

    static int Parent(Skeleton s, int b) => b < 0 ? -1 : s.Bones[b].Parent is int p && p >= 0 && p < b ? p : -1;
}

/// <summary>
/// The limbs a kind of character is posed with by IK (ours): its root (the hips, which the moves move and
/// lower), its legs and its arms, how far apart its shoulders are (its hands hold a ledge that far apart) and how high
/// its ankles stand above the ground (a foot put on a top is put that high above it). Made from 1.1's motion set's
/// attach points (<c>LFoot</c>, <c>RFoot</c>, <c>LFingers</c>, <c>RFingers</c>) by <see cref="Limb.FromTip"/>.
/// </summary>
public sealed record LimbMap(int Root, Limb LeftLeg, Limb RightLeg, Limb LeftArm, Limb RightArm, float ShoulderWidth = 0.4f, float AnkleHeight = 0.12f)
{
    /// <summary>
    /// The rig only <b>raises</b> an ankle its motion has below its standing height (a lowered stand-in's), and
    /// leaves one above it as the motion has it — for the models repository's people, whose clips put the feet on the ground
    /// themselves (pulled down as it rose, a heel coming off the ground pushed the ball of the foot into it: 6 to 8 cm/s of
    /// slide). False for 1.1's kinds: a foot up to 0.1 m above its standing height is planted at it, as.
    /// </summary>
    public bool RaiseOnly { get; init; }

    /// <summary>
    /// The limbs from the four tips (−1 for a tip not known), measured in <paramref name="standing"/> at its start (the
    /// kind's idle, whose feet are on the ground) or else in the rest pose; null when there is no root or neither leg.
    /// </summary>
    public static LimbMap? FromTips(Skeleton skeleton, int leftFoot, int rightFoot, int leftHand, int rightHand, IAnimationClip? standing = null)
    {
        int root = -1;
        for (int b = 0; b < skeleton.Count && root < 0; b++)
            if (skeleton.Bones[b].Parent < 0 || skeleton.Bones[b].Parent >= b) root = b;
        var map = new LimbMap(root, Limb.FromTip(skeleton, leftFoot), Limb.FromTip(skeleton, rightFoot),
                              Limb.FromTip(skeleton, leftHand), Limb.FromTip(skeleton, rightHand));
        if (root < 0 || !(map.LeftLeg.Has || map.RightLeg.Has)) return null;
        return Measured(skeleton, map, standing);
    }

    /// <summary>
    /// <paramref name="map"/> with its shoulder width and ankle height measured as <see cref="FromTips"/> measures
    /// them (in <paramref name="standing"/> at its start, else the rest pose): for a skeleton whose limbs are known by name (the
    /// models repository's people, <see cref="People.PeopleKinds"/>), whose hips are not its first parentless bone.
    /// </summary>
    public static LimbMap Measured(Skeleton skeleton, LimbMap map, IAnimationClip? standing = null)
    {
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        if (standing is not null) standing.Sample(0f, skeleton, local); else PoseEvaluator.Rest(skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        float width = map.LeftArm.Has && map.RightArm.Has
            ? LimbIK.Length(model[map.LeftArm.Upper].Translation - model[map.RightArm.Upper].Translation) : 0.4f;
        var leg = map.LeftLeg.Has ? map.LeftLeg : map.RightLeg;
        float ankle = model[leg.End].Translation.Z;
        if (standing is null && leg.Tip >= 0) ankle -= MathF.Min(model[leg.Tip].Translation.Z, ankle);
        return map with { ShoulderWidth = width, AnkleHeight = MathF.Max(0.02f, ankle) };
    }
}

/// <summary>
/// Two-bone IK on a pose (ours), in model space, in <see cref="PoseMath"/>'s scalar arithmetic so the same pose
/// and targets give the same bits on every machine (only <see cref="MathF.Sqrt"/>; no trigonometry). The middle
/// joint bends in the plane of the root joint, the target and where the pose had it (nudged by a hint when the
/// limb is nearly straight), so a knee keeps bending forwards and an elbow back; a target out of reach is
/// reached for in a straight line.
/// </summary>
public static class LimbIK
{
    /// <summary>A bone's rotation in model space from the local pose: its parents' rotations and its own, parents first.</summary>
    public static Quaternion ModelRotation(Skeleton skeleton, ReadOnlySpan<BoneTransform> local, int bone)
    {
        if (bone < 0) return Quaternion.Identity;
        var q = local[bone].Rotation;
        for (int p = Parent(skeleton, bone); p >= 0; p = Parent(skeleton, p)) q = PoseMath.Multiply(local[p].Rotation, q);
        return q;
    }

    /// <summary><paramref name="v"/> turned by the unit quaternion <paramref name="q"/>.</summary>
    public static Vector3 Rotate(Quaternion q, Vector3 v)
    {
        // t = 2 (q × v); v' = v + w t + q × t
        float tx = 2f * (q.Y * v.Z - q.Z * v.Y), ty = 2f * (q.Z * v.X - q.X * v.Z), tz = 2f * (q.X * v.Y - q.Y * v.X);
        return new Vector3(v.X + q.W * tx + (q.Y * tz - q.Z * ty),
                           v.Y + q.W * ty + (q.Z * tx - q.X * tz),
                           v.Z + q.W * tz + (q.X * ty - q.Y * tx));
    }

    public static Quaternion Conjugate(Quaternion q) => new(-q.X, -q.Y, -q.Z, q.W);

    public static Quaternion Normalize(Quaternion q)
    {
        float l = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return l > 0f ? new Quaternion(q.X / l, q.Y / l, q.Z / l, q.W / l) : Quaternion.Identity;
    }

    /// <summary>The shortest turn taking the direction of <paramref name="from"/> to that of <paramref name="to"/>.</summary>
    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        var a = Unit(from); var b = Unit(to);
        float d = Dot(a, b);
        if (d < -0.99999f)
        {
            var axis = Cross(a, MathF.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            axis = Unit(axis);
            return new Quaternion(axis.X, axis.Y, axis.Z, 0f);
        }
        var c = Cross(a, b);
        return Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + d));
    }

    /// <summary>The turn about the model's X axis (its right, a 1.1 character facing +Y) by <paramref name="radians"/>: positive tips its front down.</summary>
    public static Quaternion Pitch(float radians)
    {
        var (s, c) = DetMath.SinCos(-radians * 0.5f);
        return new Quaternion(s, 0f, 0f, c);
    }

    /// <summary>
    /// Puts <paramref name="limb"/>'s end at <paramref name="target"/> (blended from where the pose has it by
    /// <paramref name="weight"/>) by turning its upper and lower bones in <paramref name="local"/>, from the model-space
    /// matrices of the same pose (<paramref name="model"/>, not changed: the caller works them out again). The end keeps
    /// its turn in model space (a foot stays as the motion held it) unless <paramref name="aim"/> is given, when the end
    /// is turned so the tip points at it (the knuckles onto a ledge), or turned further by <paramref name="turnEnd"/> (a
    /// model-space turn: a foot laid on a slope). <paramref name="hint"/> is the way the middle joint should bend (model
    /// space) when the limb is nearly straight.
    /// </summary>
    public static void Solve(Skeleton skeleton, Span<BoneTransform> local, ReadOnlySpan<Matrix4x4> model, in Limb limb,
                             Vector3 target, float weight, Vector3 hint, Vector3? aim = null, Quaternion? turnEnd = null)
    {
        if (!limb.Has || weight <= 0f) return;
        var A = model[limb.Upper].Translation; var B = model[limb.Lower].Translation; var C = model[limb.End].Translation;
        float a = Length(B - A), b = Length(C - B);
        if (a < 1e-5f || b < 1e-5f) return;
        var T = PoseMath.Lerp(C, target, MathF.Min(weight, 1f));
        var toT = T - A;
        float d = Length(toT);
        if (d < 1e-5f) return;
        var u = toT / d;
        float dc = Math.Clamp(d, MathF.Abs(a - b) + 1e-4f, (a + b) * 0.9999f);
        float cos = Math.Clamp((a * a + dc * dc - b * b) / (2f * a * dc), -1f, 1f);
        float sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
        var h = Unit(hint);
        var bend = Perp(B - A, u) + Perp(h, u) * (0.05f * a);
        if (Length(bend) < 1e-6f) bend = Perp(MathF.Abs(u.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY, u);
        var v = Unit(bend);
        var B2 = A + (u * cos + v * sin) * a;
        var C2 = A + u * dc;

        var qParent = ModelRotation(skeleton, local, Parent(skeleton, limb.Upper));
        var qUpper = PoseMath.Multiply(qParent, local[limb.Upper].Rotation);
        var qLower = PoseMath.Multiply(qUpper, local[limb.Lower].Rotation);
        var qEnd = PoseMath.Multiply(qLower, local[limb.End].Rotation);

        var r1 = FromTo(B - A, B2 - A);
        var qUpper2 = Normalize(PoseMath.Multiply(r1, qUpper));
        var C1 = A + Rotate(r1, C - A);
        var r2 = FromTo(C1 - B2, C2 - B2);
        var qLower2 = Normalize(PoseMath.Multiply(r2, PoseMath.Multiply(r1, qLower)));
        var qEnd2 = turnEnd is { } turn ? Normalize(PoseMath.Multiply(turn, qEnd)) : qEnd;
        if (aim is { } at && limb.Tip >= 0)
        {
            var tip = Rotate(qEnd, local[limb.Tip].Translation);
            if (Length(tip) > 1e-5f && Length(at - C2) > 1e-5f)
                qEnd2 = Normalize(PoseMath.Multiply(DetMath.Slerp(Quaternion.Identity, FromTo(tip, at - C2), MathF.Min(weight, 1f)), qEnd));
        }

        local[limb.Upper] = local[limb.Upper] with { Rotation = Normalize(PoseMath.Multiply(Conjugate(qParent), qUpper2)) };
        local[limb.Lower] = local[limb.Lower] with { Rotation = Normalize(PoseMath.Multiply(Conjugate(qUpper2), qLower2)) };
        local[limb.End] = local[limb.End] with { Rotation = Normalize(PoseMath.Multiply(Conjugate(qLower2), qEnd2)) };
    }

    /// <summary>
    /// How far a limb of full reach <paramref name="reach"/> reaches toward a target <paramref name="distance"/> away with its
    /// soft limit from <paramref name="soft"/> × the reach: all the way up to there, beyond it closing on the full reach as
    /// <c>1 − e^(−x)</c> does (its slope 1 where it begins, so nothing jumps), never quite straight.
    /// </summary>
    public static float SoftReach(float distance, float reach, float soft)
    {
        if (soft <= 0f || soft >= 1f || reach <= 0f) return distance;
        float from = reach * soft, span = reach - from;
        return distance <= from ? distance : from + span * (1f - DetMath.Exp(-(distance - from) / span));
    }

    /// <summary>The limb's full reach from its root joint to its end in this pose (the two bones' lengths).</summary>
    public static float Reach(ReadOnlySpan<Matrix4x4> model, in Limb limb) =>
        !limb.Has ? 0f : Length(model[limb.Lower].Translation - model[limb.Upper].Translation) + Length(model[limb.End].Translation - model[limb.Lower].Translation);

    static int Parent(Skeleton s, int b) => b < 0 ? -1 : s.Bones[b].Parent is int p && p >= 0 && p < b ? p : -1;

    internal static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static float Length(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    internal static Vector3 Unit(Vector3 v) { float l = Length(v); return l > 0f ? v / l : Vector3.Zero; }
    static Vector3 Perp(Vector3 v, Vector3 u) => v - u * Dot(v, u);
}
