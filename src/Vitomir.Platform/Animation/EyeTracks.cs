// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The bob: how far the eyes are from their own middle at a moment of a looping motion (ours).
/// 1.1's run lifts and drops Christof's head by a quarter of a metre a stride and his walk by thirteen
/// centimetres (both measured), far more than a filter could take out of the eye without leaving it behind the head
/// whenever the body really moves; but a gait's bounce and sway are the motion's own cycle, so they are taken out
/// exactly: for each looping motion of a kind with a head, the eyes are found (<see cref="HeadCollapse.EyesIn"/>) 30
/// times a second through the cycle, as the motion alone poses them, and their mean taken; the bob at a moment is
/// the eyes there less that mean. The steady eye (<see cref="Cameras.CameraSystem"/>) is the head's eyes less the bob,
/// so what is left is what is not the cycle: the lean the rig gives a crouch, the crouch itself, a landing's dip, a
/// move's climb, a turn. A motion that does not loop (a move, a jump) has no bob: it is all the steady path.
/// <para>
/// A cache of tables made once per motion (the first time a kind is seen, every motion it plays), so asking
/// allocates nothing after; a table is a pure function of the assets, so it is the same on every machine.
/// </para>
/// </summary>
public sealed class EyeTracks(CharacterAssets assets)
{
    readonly Dictionary<long, Vector3[]?> tracks = [];
    BoneTransform[] local = new BoneTransform[64];
    Matrix4x4[] model = new Matrix4x4[64];

    /// <summary>
    /// The bob in <paramref name="state"/> (model space, metres): the current motion's, and the one fading out's, by
    /// the pose's own weights. Zero for a kind without a head or a motion that does not loop.
    /// </summary>
    public Vector3 Bob(in CharacterAnimation state)
    {
        float w = state.PreviousClip >= 0 ? Math.Clamp(state.Blend, 0f, 1f) : 1f;
        var bob = At(state.Kind, state.Clip, state.Time) * w;
        if (w < 1f) bob += At(state.Kind, state.PreviousClip, state.PreviousTime) * (1f - w);
        return bob;
    }

    /// <summary>The bob of one motion at <paramref name="seconds"/> into it (zero for none).</summary>
    public Vector3 At(int kind, int clip, float seconds)
    {
        if (clip < 0 || (uint)kind >= (uint)assets.KindCount) return Vector3.Zero;
        // A person's own clips' bob from the clips that stand for them (a gait's blend: its two, at the same share of
        // their cycles, by its weight; a turn, played once, has none), so nothing is made when a body first goes some new way.
        if (assets.Kind(kind).Person is { } person && person.Sources(assets.Clip(clip), seconds, out int a, out float atA, out int b, out float atB, out float w))
        {
            if (assets.Clip(clip) is People.TurnClip) return Vector3.Zero;
            var bob = At(kind, a, atA);
            return w <= 0f ? bob : PoseMath.Lerp(bob, At(kind, b, atB), w);
        }
        if (Track(kind, clip) is not { Length: > 1 } track) return Vector3.Zero;
        float duration = assets.Clip(clip).Duration;
        float f = seconds * 30f, frames = duration * 30f;
        if (frames > 0f) f -= MathF.Floor(f / frames) * frames;
        f = Math.Clamp(f, 0f, track.Length - 1);
        int i = (int)f;
        int j = Math.Min(i + 1, track.Length - 1);
        return PoseMath.Lerp(track[i], track[j], f - i);
    }

    /// <summary>
    /// The eyes (model space, m) as <paramref name="clip"/> alone poses them at <paramref name="seconds"/>
    /// (no fade, no rig), for a kind with a head; zero otherwise. Allocates nothing once the buffers are the skeleton's size.
    /// </summary>
    public Vector3 Eyes(int kindIndex, int clip, float seconds)
    {
        if (clip < 0 || (uint)kindIndex >= (uint)assets.KindCount || assets.Kind(kindIndex).Head is not { } head) return Vector3.Zero;
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        int n = skeleton.Count;
        if (local.Length < n) { local = new BoneTransform[n]; model = new Matrix4x4[n]; }
        var c = assets.Clip(clip);
        c.Sample(Math.Clamp(seconds, 0f, MathF.Max(c.Duration, 0f)), skeleton, local.AsSpan(0, n));
        PoseEvaluator.ToModel(skeleton, local.AsSpan(0, n), model.AsSpan(0, n));
        return PoseMath.Transform(PoseMath.Transform(head.Eyes, skeleton.InverseBind[head.Bone]), model[head.Bone]);
    }

    Vector3[]? Track(int kind, int clip)
    {
        long key = ((long)kind << 32) | (uint)clip;
        if (tracks.TryGetValue(key, out var t)) return t;
        // First sight of this kind: a table for every motion it plays, so later changes of motion allocate nothing.
        foreach (var m in assets.Kind(kind).Motions)
            if (m.Clip >= 0) Build(kind, m.Clip);
        if (assets.Kind(kind).Person is { } person) foreach (int c in person.PoseClips) Build(kind, c);   // what stands for a person's own clips
        Build(kind, clip);
        return tracks[key];
    }

    void Build(int kindIndex, int clipIndex)
    {
        long key = ((long)kindIndex << 32) | (uint)clipIndex;
        if (tracks.ContainsKey(key)) return;
        var kind = assets.Kind(kindIndex);
        var clip = assets.Clip(clipIndex);
        if (kind.Head is not { } head || !clip.Loops || clip.Duration <= 0f) { tracks[key] = null; return; }
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        int n = skeleton.Count;
        if (local.Length < n) { local = new BoneTransform[n]; model = new Matrix4x4[n]; }
        var inHead = PoseMath.Transform(head.Eyes, skeleton.InverseBind[head.Bone]);
        int frames = Math.Max(2, (int)MathF.Ceiling(clip.Duration * 30f) + 1);
        var track = new Vector3[frames];
        var sum = Vector3.Zero;
        for (int f = 0; f < frames; f++)
        {
            clip.Sample(MathF.Min(f / 30f, clip.Duration), skeleton, local.AsSpan(0, n));
            PoseEvaluator.ToModel(skeleton, local.AsSpan(0, n), model.AsSpan(0, n));
            track[f] = PoseMath.Transform(inHead, model[head.Bone]);
            if (f < frames - 1) sum += track[f];     // the last frame is the first again
        }
        var mean = sum / (frames - 1);
        for (int f = 0; f < frames; f++) track[f] -= mean;
        tracks[key] = track;
    }
}
