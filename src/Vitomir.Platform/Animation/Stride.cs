// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Anim/Stride.cs at b4185ef, reworked for the platform (metres, seconds, any clip).
using System.Numerics;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation;

/// <summary>A foot planting in a locomotion clip: when (seconds into the clip) and which foot.</summary>
public readonly record struct Footfall(float Seconds, bool Left);

/// <summary>
/// How much ground one loop of a locomotion clip is drawn as covering, measured from the clip itself, and
/// the rate that makes its feet match a ground speed (ours, as the old engine's <c>Stride</c>, which built it
/// for its <c>matchedgait</c> fix: 1.1 plays every cycle at a flat 30 frames a second whatever the speed, so
/// its townsfolk skate).
/// <para>
/// A planted foot does not move over the ground, so in a clip authored in place it travels backwards
/// through the model's own space by exactly the distance the character advances. Summing that over every
/// stance — from each footfall to the next, the last one holding until the first of the next loop — gives
/// the ground one loop covers. A character moving at <c>speed</c> then plays the clip at
/// <c>speed ÷ natural speed</c> times its own rate, the natural speed being stride ÷ duration.
/// </para>
/// </summary>
public static class Stride
{
    /// <summary>The least stride counted as one (half a 1.1 unit, the old engine's 0.5).</summary>
    public const float Least = 0.5f / Assets.OneOne.OneOneModels.UnitsPerMetre;

    /// <summary>
    /// The ground one loop covers, in metres, or null when it cannot be measured (fewer than two
    /// footfalls, a foot bone the skeleton has not got, or nothing measured). <paramref name="plants"/> must
    /// be in time order.
    /// </summary>
    public static float? Measure(IAnimationClip clip, Skeleton skeleton, int leftFoot, int rightFoot, ReadOnlySpan<Footfall> plants)
    {
        if ((uint)leftFoot >= (uint)skeleton.Count || (uint)rightFoot >= (uint)skeleton.Count) return null;
        if (clip.Duration <= 0f || plants.Length < 2) return null;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        float total = 0f;
        for (int i = 0; i < plants.Length; i++)
        {
            float from = plants[i].Seconds;
            float to = i + 1 < plants.Length ? plants[i + 1].Seconds : plants[0].Seconds;
            int bone = plants[i].Left ? leftFoot : rightFoot;
            var a = Foot(clip, skeleton, from, bone, local, model);
            var b = Foot(clip, skeleton, to, bone, local, model);
            total += Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        }
        return total > Least ? total : null;
    }

    /// <summary>
    /// The ground one loop covers, in metres, estimated without footfall marks (ours; two of the
    /// catalogue's walks have none): each foot sweeps back over the ground while it stands, so a loop of
    /// two steps covers about the length of the left foot's sweep plus the right's — the greatest distance
    /// over the ground between two of its positions, sampled <paramref name="samples"/> times round the
    /// loop. Null when there is nothing to measure.
    /// </summary>
    public static float? Sweep(IAnimationClip clip, Skeleton skeleton, int leftFoot, int rightFoot, int samples = 48)
    {
        if ((uint)leftFoot >= (uint)skeleton.Count || (uint)rightFoot >= (uint)skeleton.Count || clip.Duration <= 0f || samples < 2) return null;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        var left = new Vector2[samples];
        var right = new Vector2[samples];
        for (int i = 0; i < samples; i++)
        {
            clip.Sample(clip.Duration * i / samples, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            left[i] = new Vector2(model[leftFoot].M41, model[leftFoot].M42);
            right[i] = new Vector2(model[rightFoot].M41, model[rightFoot].M42);
        }
        float total = Widest(left) + Widest(right);
        return total > Least ? total : null;

        static float Widest(Vector2[] p)
        {
            float w = 0f;
            for (int i = 0; i < p.Length; i++)
                for (int j = i + 1; j < p.Length; j++) w = MathF.Max(w, Vector2.Distance(p[i], p[j]));
            return w;
        }
    }

    /// <summary>The ground speed a clip was drawn for, metres a second: what plays at its own rate.</summary>
    public static float NaturalSpeed(float stride, float durationSeconds) => durationSeconds > 0f ? stride / durationSeconds : 0f;

    /// <summary>How fast to play a clip drawn for <paramref name="naturalSpeed"/> so its feet keep pace with <paramref name="speed"/> (1 when the natural speed is unknown).</summary>
    public static float RateFor(float speed, float naturalSpeed) => naturalSpeed > 0f ? speed / naturalSpeed : 1f;

    static Vector3 Foot(IAnimationClip clip, Skeleton skeleton, float seconds, int bone, BoneTransform[] local, Matrix4x4[] model)
    {
        clip.Sample(seconds, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        return model[bone].Translation;
    }
}
