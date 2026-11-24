// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>One key of a <see cref="ComposedClip"/>: where along it (0 to 1), and every bone's local transform there.</summary>
public sealed record ComposedKey(float At, BoneTransform[] Pose);

/// <summary>
/// A motion made of a few key poses (ours): the stand-in that fills a move's slot until a real motion comes
/// for it. Its keys are poses taken from 1.1's own clips (a frame each, made for
/// one skeleton by <see cref="Key"/>); between two keys each bone is blended by <see cref="DetMath.Slerp"/> and a
/// linear blend of its translation, eased in and out (smoothstep), so it passes through each key and rests there.
/// A traversal's slot is played by the move's progress (the time is the progress times <see cref="Duration"/>); the
/// hands and feet are then put where the move needs them by IK (<see cref="PoseRig"/>). It is a clip like any other,
/// so the fades, the system and the evaluator need nothing special.
/// </summary>
public sealed class ComposedClip(string name, float duration, bool loops, IReadOnlyList<ComposedKey> keys) : IAnimationClip
{
    public string Name => name;
    public float Duration => duration;
    public bool Loops => loops;
    public IReadOnlyList<ComposedKey> Keys => keys;

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        int n = Math.Min(local.Length, skeleton.Count);
        if (keys.Count == 0) { PoseEvaluator.Rest(skeleton, local); return; }
        float p = duration > 0f ? seconds / duration : 0f;
        if (loops) p -= MathF.Floor(p);
        p = Math.Clamp(p, 0f, 1f);
        int k = 0;
        while (k + 1 < keys.Count && keys[k + 1].At <= p) k++;
        var from = keys[k];
        if (k + 1 >= keys.Count || p <= from.At) { Copy(from.Pose, local, n, skeleton); return; }
        var to = keys[k + 1];
        float t = (p - from.At) / MathF.Max(1e-6f, to.At - from.At);
        t = t * t * (3f - 2f * t);
        int m = Math.Min(n, Math.Min(from.Pose.Length, to.Pose.Length));
        PoseEvaluator.Crossfade(from.Pose.AsSpan(0, m), to.Pose.AsSpan(0, m), t, local[..m]);
        for (int b = m; b < n; b++) local[b] = skeleton.Bones[b].Rest;
    }

    static void Copy(BoneTransform[] pose, Span<BoneTransform> local, int n, Skeleton skeleton)
    {
        int m = Math.Min(n, pose.Length);
        pose.AsSpan(0, m).CopyTo(local);
        for (int b = m; b < n; b++) local[b] = skeleton.Bones[b].Rest;
    }

    /// <summary>
    /// A key at <paramref name="at"/>: <paramref name="source"/> sampled at <paramref name="seconds"/> for
    /// <paramref name="skeleton"/>, the root bone (<paramref name="root"/>) given <paramref name="rootHeight"/> as its
    /// height when one is given (a frame of a fall keeps its pose but not where the fall had got to) and turned by
    /// <paramref name="lean"/> radians forwards (<see cref="LimbIK.Pitch"/>).
    /// </summary>
    public static ComposedKey Key(float at, IAnimationClip source, float seconds, Skeleton skeleton, int root = 0, float? rootHeight = null, float lean = 0f)
    {
        var pose = new BoneTransform[skeleton.Count];
        source.Sample(seconds, skeleton, pose);
        if ((uint)root < (uint)pose.Length)
        {
            var r = pose[root];
            if (rootHeight is { } h) r = r with { Translation = new Vector3(r.Translation.X, r.Translation.Y, h) };
            if (lean != 0f) r = r with { Rotation = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Pitch(lean), r.Rotation)) };
            pose[root] = r;
        }
        return new ComposedKey(at, pose);
    }
}
