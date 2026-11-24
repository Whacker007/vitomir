// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sampling is taken from src/Vitomir.Engine/Anim/Skeleton.cs (Sample, Euler, Pose) and the playing
// rate and wrap from its AnimationPlayer at b4185ef, reworked for the platform.
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// A 1.1 motion (<c>.nad</c>) as a platform clip.
/// <para>
/// <b>Sampling is 1.1's</b> (the old engine's <c>Skeleton.Sample</c>): every track is
/// read at the frame from its last key at or before it, a key holding a value V and factors C, B, A, the
/// track giving <c>V + C·t + B·t² + A·t³</c> with <c>t = (frame − key.Frame) × key.FrameScale</c>, summed in
/// the same order of operations so the numbers are 1.1's to the bit; rotation tracks feed a bone's Euler
/// angles (degrees about X, Y, Z) and every other track its translation. A bone's local transform is then
/// the Euler rotation — about X, then Y, then Z, row vectors — made a quaternion with
/// <see cref="PoseMath.FromEulerDegrees"/>, and the sampled translation plus the bone's rest translation
/// (the conversion to metres, after the sum, is ours). A bone with no track stands unrotated at its rest
/// translation, which is how 1.1 poses it (and which is not always the bind pose: the rest orientation
/// lives only in the model's inverse bind matrices).
/// </para>
/// <para>
/// <b>Time.</b> 1.1 plays a motion at 30 frames a second (the rate every gameplay play site passes, the old
/// engine's <c>ThingAnimation.Rate</c>; a <c>.nad</c> stores no rate of its own), so a clip's
/// <see cref="Duration"/> is its length in frames over 30. A looping clip wraps by whole durations into
/// [0, duration), as 1.1's channel does; one that does not loop holds its last frame.
/// </para>
/// </summary>
public sealed class NadClip : IAnimationClip
{
    /// <summary>The frames a second 1.1 plays every motion at.</summary>
    public const float FramesPerSecond = 30f;

    readonly NadTrack[] tracks;

    public NadClip(string name, NadAnimation animation, bool loops)
    {
        Name = name;
        Animation = animation;
        Loops = loops;
        tracks = animation.Tracks;
    }

    public string Name { get; }

    /// <summary>The motion as read.</summary>
    public NadAnimation Animation { get; }

    /// <summary>Length in frames (1.1's timeline).</summary>
    public float Frames => Animation.Duration;

    public float Duration => Animation.Duration / FramesPerSecond;

    public bool Loops { get; }

    /// <summary>The motion's event marks (footfalls, the landing's <c>Fall</c>, …), in frames.</summary>
    public NadTag[] Tags => Animation.Tags;

    /// <summary>The frame at <paramref name="seconds"/>: 30 a second, wrapped by whole durations when the clip loops, held at the ends when it does not.</summary>
    public float FrameAt(float seconds)
    {
        float frame = seconds * FramesPerSecond;
        float d = Animation.Duration;
        if (d <= 0f) return 0f;
        if (!Loops) return frame < 0f ? 0f : frame > d ? d : frame;
        if (frame >= d || frame < 0f)
        {
            frame -= MathF.Floor(frame / d) * d;
            if (frame >= d) frame -= d;
            if (frame < 0f) frame = 0f;
        }
        return frame;
    }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local) =>
        SampleFrame(FrameAt(seconds), skeleton, local);

    /// <summary>Each bone's local transform at a frame of 1.1's timeline (no wrap: the frame is taken as given).</summary>
    public void SampleFrame(float frame, Skeleton skeleton, Span<BoneTransform> local)
    {
        int n = skeleton.Count;
        Span<Vector3> rotation = n <= 256 ? stackalloc Vector3[n] : new Vector3[n];
        Span<Vector3> translation = n <= 256 ? stackalloc Vector3[n] : new Vector3[n];
        SampleEuler(frame, rotation, translation);
        var bones = skeleton.Bones;
        for (int b = 0; b < n && b < local.Length; b++)
            local[b] = new BoneTransform(PoseMath.FromEulerDegrees(rotation[b]),
                                         translation[b] / OneOneModels.UnitsPerMetre + bones[b].Rest.Translation);
    }

    /// <summary>
    /// 1.1's own sums at a frame: each bone's Euler angles (degrees) and translation (1.1's units, without the
    /// rest translation) — the old engine's <c>Skeleton.Sample</c>, line for line.
    /// </summary>
    public void SampleEuler(float frame, Span<Vector3> rotation, Span<Vector3> translation)
    {
        rotation.Clear();
        translation.Clear();
        foreach (var track in tracks)
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
                float t = past * key.FrameScale, t2 = t * t, t3 = t2 * t;
                v = Add(key.Value, Scale(t, key.C));
                v = Add(Scale(t2, key.B), v);
                v = Add(Scale(t3, key.A), v);
            }
            if (track.Type == NadTrack.Rotation) rotation[(int)track.Bone] = Add(rotation[(int)track.Bone], v);
            else translation[(int)track.Bone] = Add(translation[(int)track.Bone], v);
        }
    }

    // Component by component, in the old engine's order (its Vector3 operators are per-lane IEEE operations too).
    static Vector3 Scale(float s, Vector3 v) => new(s * v.X, s * v.Y, s * v.Z);
    static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}
