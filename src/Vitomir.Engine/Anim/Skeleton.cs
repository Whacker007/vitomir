// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// Plays one NAD on a model the way the original's animation channel does:
/// the frame advances by rate × seconds, and a looping animation wraps by whole durations.
/// </summary>
public sealed class AnimationPlayer(NadAnimation animation, float rate = 30f)
{
    public NadAnimation Animation { get; } = animation;
    /// <summary>Frames per second. Scenes start their sky at 30.</summary>
    public float Rate { get; set; } = rate;
    /// <summary>Current position on the timeline, in frames; always in [0, duration) while looping.</summary>
    public float Frame { get; set; }
    /// <summary>
    /// False for a motion set's <c>noloop</c> mode: the frame stops at the end instead of wrapping,
    /// which is what leaves a death or a one-shot gesture holding its last pose.
    /// </summary>
    public bool Loop { get; set; } = true;

    public void Advance(float seconds)
    {
        if (seconds == 0f || Animation.Duration <= 0f) return;
        Frame = Rate * seconds + Frame;
        if (!Loop) { if (Frame > Animation.Duration) Frame = Animation.Duration; return; }
        if (Frame >= Animation.Duration)
        {
            // The original divides once and then subtracts whole durations (wrap, not reset).
            float laps = Frame / Animation.Duration;
            while (laps >= 1f) { Frame -= Animation.Duration; laps -= 1f; }
        }
    }
}

/// <summary>
/// Skeletal pose maths for NOD models. Matrices follow System.Numerics' row-vector convention,
/// which is also the original's: a vertex is transformed as v × M, and a child's world matrix is
/// local × parentWorld.
/// </summary>
public static class Skeleton
{
    /// <summary>
    /// Sums every track of <paramref name="animation"/> at <paramref name="frame"/> into per-bone
    /// Euler rotations (degrees; pitch, roll, yaw about X, Y, Z) and translations. Each
    /// key holds value V and factors C, B, A; with t = (frame − key.Frame) × key.FrameScale the
    /// track gives V + C·t + B·t² + A·t³ from the last key at or before the frame. Rotation tracks
    /// feed the rotation sums; every other track type is added to the translation (none ship).
    /// </summary>
    public static void Sample(NadAnimation animation, float frame, Span<Vector3> rotation, Span<Vector3> translation)
    {
        rotation.Clear();
        translation.Clear();
        foreach (var track in animation.Tracks)
        {
            if (track.Bone >= (uint)rotation.Length || track.Keys.Length == 0) continue;
            var keys = track.Keys;
            int k = 0;
            while (k < keys.Length - 1 && keys[k + 1].Frame <= frame) k++;
            var key = keys[k];
            float past = frame - key.Frame;
            Vector3 v = key.Value;
            if (past != 0f)
            {
                // Same order of operations as the original, so poses are bit-for-bit stable.
                float t = past * key.FrameScale, t2 = t * t, t3 = t2 * t;
                v = key.Value + t * key.C;
                v = t2 * key.B + v;
                v = t3 * key.A + v;
            }
            if (track.Type == NadTrack.Rotation) rotation[(int)track.Bone] += v;
            else translation[(int)track.Bone] += v;
        }
    }

    /// <summary>Rotation from Euler degrees: about X, then Y, then Z.</summary>
    public static Matrix4x4 Euler(Vector3 degrees)
    {
        const float d2r = MathF.PI / 180f;
        return Matrix4x4.CreateRotationX(degrees.X * d2r) * Matrix4x4.CreateRotationY(degrees.Y * d2r)
             * Matrix4x4.CreateRotationZ(degrees.Z * d2r);
    }

    /// <summary>
    /// Each bone's animated world matrix (in model space). A bone's local matrix is the
    /// sampled rotation, translated by the sampled translation plus the bone's rest translation;
    /// bones without tracks therefore sit unrotated at their rest offset. Parents precede children
    /// in every shipped model.
    /// </summary>
    public static void Pose(NodModel model, ReadOnlySpan<Vector3> rotation, ReadOnlySpan<Vector3> translation, Span<Matrix4x4> world)
    {
        var bones = model.Bones;
        for (int b = 0; b < bones.Length; b++)
        {
            var local = Euler(rotation[b]);
            local.Translation = translation[b] + bones[b].RestTranslate;
            int parent = bones[b].Parent;
            world[b] = parent >= 0 && parent < b ? local * world[parent] : local;
        }
    }

    /// <summary>The bone's inverse rest matrix as stored in the NOD (3×3 rotation rows, then translation).</summary>
    public static Matrix4x4 RestInverse(NodBone bone)
    {
        var m = bone.RestMatrixInverse;
        return new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
    }

    /// <summary>Skinning matrices: rest-pose model space → animated model space (inverse rest × world).</summary>
    public static void Skin(NodModel model, ReadOnlySpan<Matrix4x4> world, Span<Matrix4x4> skin)
    {
        for (int b = 0; b < model.Bones.Length; b++) skin[b] = RestInverse(model.Bones[b]) * world[b];
    }

    /// <summary>
    /// A vertex's animated position. Weight 1 (or more) binds it to its bone alone; below 1 it
    /// blends with the bone's parent, the weight going to the vertex's own bone.
    /// </summary>
    public static Vector3 SkinPosition(NodModel model, in NodVertex v, ReadOnlySpan<Matrix4x4> skin)
    {
        var own = Vector3.Transform(v.Position, skin[v.Bone]);
        if (v.Weight >= 1f) return own;
        int parent = model.Bones[v.Bone].Parent;
        if (parent < 0) return own;     // the original would read outside the bone array here; no shipped vertex does this
        var par = Vector3.Transform(v.Position, skin[parent]);
        return (own - par) * v.Weight + par;
    }
}
