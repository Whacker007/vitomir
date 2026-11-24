// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation;

/// <summary>
/// What first person hides of the viewer's own body, and where its eyes are (ours; the movement design's "the real
/// body", 3.4): the head bone, every bone below it and the neck above it, shrunk to almost nothing about the neck's
/// joint, so the eye is not inside the face and never looks down on the neck's stump or into the collar, while the
/// arms, chest and legs below are drawn exactly as posed. Only the first-person picture hides them: the shadow keeps
/// the whole body (<see cref="Presentation.DrawnCharacter.ShadowSkin"/>).
/// <para>
/// A 1.1 character's head bone is the one its motion set's <c>Head</c> attach point names (tag 19 of the
/// engine's 43, <c>tag Head &lt;bone&gt;</c> in the <c>.nms</c>) — the bone 1.1 itself takes the head from:
/// a beheading's head and blood come off it (the old engine's <c>Beheading.HeadTag</c>, <c>Gore.HeadTag</c>,
/// 0x13), and the helmet (20) and jaw (21) tags ride on its children. Christof's is bone 18, with the
/// helmet (19) and the jaw (20) below it, and his neck (17) above it.
/// </para>
/// <para>
/// <b>The neck</b> is the head's parent when the head is its only child and it is not the root: a link of the
/// chain from the chest, which itself branches to the arms. A skeleton without one hides the head alone.
/// </para>
/// <para>
/// <b>The eyes</b> (ours) are worked out once from the head's own vertices in the rest pose (those whose
/// heaviest bone is the head or below it): across, the middle of the head; along the way the model faces, 2.5 cm
/// behind the front of the face (the nose's tip stands about that far ahead of the eyes); up, halfway between the
/// chin and the crown, where a human's eyes are. Christof's come to about 1.65 m, his body's eye height. Without
/// vertices they are a guess from the head's joint (<see cref="GuessUp"/> up, <see cref="GuessAhead"/> ahead). The
/// first-person camera rides them (<see cref="Cameras.CameraSystem"/>), through <see cref="EyesIn"/>.
/// </para>
/// <para>
/// The bones are scaled by <see cref="Scale"/> (a thousandth) rather than to zero, so a renderer that
/// normalises a skinned normal never divides by zero; a 25 cm head becomes a quarter of a millimetre at the
/// neck. A vertex weighted between the neck and the chest is drawn part way, as it is skinned.
/// </para>
/// </summary>
public sealed class HeadCollapse
{
    /// <summary>How much of its size a collapsed bone keeps.</summary>
    public const float Scale = 1e-3f;

    /// <summary>How far behind the front of the face the eyes are (m).</summary>
    public const float EyesBehindFace = 0.025f;

    /// <summary>Without the head's vertices: the eyes this far above the head's joint (m) and this far ahead of it.</summary>
    public const float GuessUp = 0.07f, GuessAhead = 0.09f;

    /// <summary>The head bone.</summary>
    public required int Bone { get; init; }

    /// <summary>The neck bone above the head that is hidden with it, or −1 for none.</summary>
    public int Neck { get; init; } = -1;

    /// <summary>The hidden bones — the neck, the head and every bone below it — in ascending order.</summary>
    public required int[] Bones { get; init; }

    /// <summary>The hidden bones' top joint in the rest pose, model space (metres): the neck's, else the head's; where they shrink to.</summary>
    public required Vector3 Pivot { get; init; }

    /// <summary>The eyes in the rest pose, model space (metres): what the first-person camera rides.</summary>
    public Vector3 Eyes { get; init; }

    /// <summary>The bone the hidden bones shrink about: the neck, else the head.</summary>
    public int Root => Neck >= 0 ? Neck : Bone;

    /// <summary>
    /// The collapse for the head at <paramref name="headBone"/> of <paramref name="skeleton"/>, or null when there is
    /// no such bone; its eyes from <paramref name="vertices"/> when given (the model faces <paramref name="facingYaw"/>,
    /// 1.1's +Y), else guessed from the head's joint.
    /// </summary>
    public static HeadCollapse? For(Skeleton skeleton, int headBone, ReadOnlySpan<SkinVertex> vertices = default, float facingYaw = Core.DetMath.HalfPi)
    {
        if ((uint)headBone >= (uint)skeleton.Count) return null;
        int parent = skeleton.Bones[headBone].Parent;
        int neck = -1;
        if (parent >= 0 && parent < headBone && skeleton.Bones[parent].Parent >= 0)
        {
            int children = 0;
            for (int b = 0; b < skeleton.Count; b++) if (skeleton.Bones[b].Parent == parent) children++;
            if (children == 1) neck = parent;
        }
        var under = new bool[skeleton.Count];
        under[headBone] = true;
        var head = new bool[skeleton.Count];
        var list = new List<int>();
        for (int b = 0; b < skeleton.Count; b++)
        {
            int p = skeleton.Bones[b].Parent;
            if (b != headBone && p >= 0 && p < b && under[p]) under[b] = true;
            head[b] = under[b];
            if (under[b] || b == neck) list.Add(b);
        }
        Vector3 Joint(int bone) => Matrix4x4.Invert(skeleton.InverseBind[bone], out var bind) ? bind.Translation : Vector3.Zero;
        var pivot = Joint(neck >= 0 ? neck : headBone);
        return new HeadCollapse { Bone = headBone, Neck = neck, Bones = [.. list], Pivot = pivot, Eyes = EyesOf(Joint(headBone), head, vertices, facingYaw) };
    }

    /// <summary>The eyes from the head's vertices (see the class), or guessed from <paramref name="joint"/> when it has none.</summary>
    static Vector3 EyesOf(Vector3 joint, bool[] head, ReadOnlySpan<SkinVertex> vertices, float facingYaw)
    {
        var (s, c) = Core.DetMath.SinCos(facingYaw);
        var ahead = new Vector3(c, s, 0f);
        var across = new Vector3(s, -c, 0f);
        float front = float.MinValue, left = float.MaxValue, right = float.MinValue, low = float.MaxValue, high = float.MinValue;
        int n = 0;
        foreach (ref readonly var v in vertices)
        {
            int best = v.Bone(0);
            float w = v.Weights.X;
            if (v.Weights.Y > w) { w = v.Weights.Y; best = v.Bone(1); }
            if (v.Weights.Z > w) { w = v.Weights.Z; best = v.Bone(2); }
            if (v.Weights.W > w) best = v.Bone(3);
            if ((uint)best >= (uint)head.Length || !head[best]) continue;
            front = MathF.Max(front, Vector3.Dot(v.Position, ahead));
            float x = Vector3.Dot(v.Position, across);
            left = MathF.Min(left, x); right = MathF.Max(right, x);
            low = MathF.Min(low, v.Position.Z); high = MathF.Max(high, v.Position.Z);
            n++;
        }
        if (n < 4) return joint + new Vector3(0, 0, GuessUp) + ahead * GuessAhead;
        return across * ((left + right) / 2f) + ahead * (front - EyesBehindFace) + new Vector3(0, 0, (low + high) / 2f);
    }

    /// <summary>
    /// The eyes where the pose has put the head (model space, metres), from the pose's skin matrices
    /// (<see cref="PoseEvaluator.ToSkin"/>'s, before <see cref="Apply"/>): the rest-pose eyes carried by the head bone.
    /// </summary>
    public Vector3 EyesIn(ReadOnlySpan<Matrix4x4> skin) => (uint)Bone < (uint)skin.Length ? PoseMath.Transform(Eyes, skin[Bone]) : Eyes;

    /// <summary>
    /// Collapses the head and neck in a set of skin matrices (<see cref="PoseEvaluator.ToSkin"/>'s): every one of
    /// <see cref="Bones"/> is followed by a shrink about the top joint (<see cref="Root"/>'s) where the pose has put it.
    /// The other bones' matrices are not touched.
    /// </summary>
    public void Apply(Span<Matrix4x4> skin)
    {
        if ((uint)Root >= (uint)skin.Length) return;
        var p = PoseMath.Transform(Pivot, skin[Root]);
        foreach (int b in Bones)
        {
            if ((uint)b >= (uint)skin.Length) continue;
            ref var m = ref skin[b];
            m.M11 *= Scale; m.M12 *= Scale; m.M13 *= Scale;
            m.M21 *= Scale; m.M22 *= Scale; m.M23 *= Scale;
            m.M31 *= Scale; m.M32 *= Scale; m.M33 *= Scale;
            m.M41 = (m.M41 - p.X) * Scale + p.X;
            m.M42 = (m.M42 - p.Y) * Scale + p.Y;
            m.M43 = (m.M43 - p.Z) * Scale + p.Z;
        }
    }
}
