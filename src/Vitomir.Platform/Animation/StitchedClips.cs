// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// One layer of a <see cref="StitchedClip"/> (ours): the bones of <see cref="Mask"/> taken from
/// <see cref="Source"/> by a weight that changes over the stitched clip's time. The weight is given at keys in 1.1's frames
/// of the base clip (30 a second, <see cref="Frames"/> and <see cref="Weights"/>, in order), linear between them and held
/// before the first and after the last. The source is sampled at the base clip's time (wrapped by its own looping: a stand
/// keeps its own small motion), so a stand's arm lives rather than freezes.
/// </summary>
public sealed record StitchLayer(string What, bool[] Mask, IAnimationClip Source, float[] Frames, float[] Weights)
{
    /// <summary>The layer's weight at <paramref name="frame"/> of the base clip.</summary>
    public float WeightAt(float frame) => Keyed(Frames, Weights, frame);

    /// <summary>A weight keyed at <paramref name="frames"/> (in order): linear between keys, held before the first and after the last; 0 with no keys.</summary>
    public static float Keyed(float[] frames, float[] weights, float frame)
    {
        if (frames.Length == 0) return 0f;
        if (frame <= frames[0]) return weights[0];
        for (int k = 1; k < frames.Length; k++)
            if (frame <= frames[k])
            {
                float span = frames[k] - frames[k - 1];
                float t = span > 1e-6f ? (frame - frames[k - 1]) / span : 1f;
                return weights[k - 1] + (weights[k] - weights[k - 1]) * t;
            }
        return weights[^1];
    }
}

/// <summary>
/// A turn of a <see cref="StitchedClip"/> (ours): bone <see cref="Bone"/> (the spine's first: the upper body over the hips)
/// turned about an axis of the body's model space (<see cref="Axis"/>: up, +Z, turns it counter-clockwise seen from above —
/// toward the body's left; right, +X, raises what is in front) by <see cref="Radians"/> times a weight keyed as a
/// <see cref="StitchLayer"/>'s: so a blow made for a two-handed weapon held at the right side is brought to the body's own
/// centre line, where the aim is, and a thrust to the height it is meant for.
/// </summary>
public sealed record StitchTurn(string What, int Bone, System.Numerics.Vector3 Axis, float Radians, float[] Frames, float[] Weights)
{
    public float WeightAt(float frame) => StitchLayer.Keyed(Frames, Weights, frame);
}

/// <summary>
/// A stitched clip's time warp (the thrust's second pass): which frame of the base clip plays at each frame of the
/// stitched clip (both 1.1's frames, 30 a second; <see cref="Frames"/> and <see cref="BaseFrames"/> in order, linear between,
/// the first and last pairs the clips' ends), so a part of the base clip can be played faster (the halberd's lunge) and the
/// rest slower, the whole lasting what it lasted.
/// </summary>
public sealed record StitchWarp(float[] Frames, float[] BaseFrames)
{
    /// <summary>The base clip's frame at the stitched clip's <paramref name="frame"/>.</summary>
    public float BaseFrame(float frame) => Map(Frames, BaseFrames, frame);

    /// <summary>The stitched clip's frame at which the base clip's <paramref name="baseFrame"/> plays.</summary>
    public float FrameOf(float baseFrame) => Map(BaseFrames, Frames, baseFrame);

    static float Map(float[] from, float[] to, float x)
    {
        if (from.Length == 0) return x;
        if (x <= from[0]) return to[0] + (x - from[0]);
        for (int k = 1; k < from.Length; k++)
            if (x <= from[k])
            {
                float span = from[k] - from[k - 1];
                return to[k - 1] + (span > 1e-6f ? (x - from[k - 1]) / span : 1f) * (to[k] - to[k - 1]);
            }
        return to[^1] + (x - from[^1]);
    }
}

/// <summary>
/// A stitched blow's weapon arm set by IK along a line (the thrust's second pass; ours): from <see cref="DriveFrom"/> to
/// <see cref="DriveTo"/> (stitched frames) the held blade's guard is driven from <see cref="From"/> along <see cref="Direction"/>
/// by <see cref="Length"/> (eased in and out: smoothstep), the blade turned to point along it, the arm (<see cref="Arm"/>) solved
/// to put it there (<see cref="LimbIK.Solve"/>, the elbow kept in its plane, <see cref="Hint"/> a little); the solve's weight
/// keyed over the clip's frames as a layer's. The blade's guard and tip are in its bone's frame (<see cref="BladeBone"/>), as
/// the held model's (<c>HeldThing</c>). Everything in the kind's model space before the aim offset bends it.
/// </summary>
public sealed record StitchReach(string What, Limb Arm, int BladeBone, System.Numerics.Vector3 BladeGuard, System.Numerics.Vector3 BladeTip,
                                 System.Numerics.Vector3 From, System.Numerics.Vector3 Direction, float Length, float DriveFrom, float DriveTo,
                                 System.Numerics.Vector3 Hint, float[] WeightFrames, float[] Weights);

/// <summary>
/// A side blow's sweep flattened (ours; *"can we somewhat flatten the horizontal swings so they're more
/// even side to side? Doesn't have to be perfect."*): by a weight keyed over the clip's frames (<see cref="WeightFrames"/>,
/// <see cref="Weights"/>, as a layer's), the held blade's guard is raised or lowered to <see cref="Height"/> where 1.1's motion
/// has it (its place across the body kept: the sweep's progress and timing are 1.1's), and the blade turned toward level (its
/// heading kept), the weapon arm solved to put it there (<see cref="LimbIK.Solve"/>, the elbow kept in its plane). At full weight
/// the blade sweeps level at <see cref="Height"/>, its tip with it. In the kind's model space, before the aim offset bends it.
/// </summary>
public sealed record StitchFlatten(string What, Limb Arm, int BladeBone, System.Numerics.Vector3 BladeGuard, System.Numerics.Vector3 BladeTip,
                                   float Height, System.Numerics.Vector3 Hint, float[] WeightFrames, float[] Weights);

/// <summary>
/// A blow stitched from 1.1's own motions (*"if you could stitch something together for a. For
/// the above attack it would be an overhand swing down, then for the bottom it would be basically a stab"*):
/// a 1.1 clip (<see cref="Base"/>, a motion of the same skeleton) played whole, with some of its bones taken from
/// other clips by <see cref="StitchLayer"/>s — a two-handed blow's off hand given back to the stand, its bow kept shallower.
/// Each masked bone is blended by <see cref="DetMath.Slerp"/> and a linear blend of its translation (as
/// <see cref="PoseEvaluator.Crossfade"/>), so it is deterministic; sampling allocates nothing (the source's pose on the stack).
/// It does not loop; its length is the base clip's.
/// </summary>
public sealed class StitchedClip(string name, IAnimationClip baseClip, IReadOnlyList<StitchLayer> layers, IReadOnlyList<StitchTurn>? turns = null) : IAnimationClip
{
    readonly IReadOnlyList<StitchTurn> turns = turns ?? [];

    /// <summary>The frames a second the layers' keys are counted in (1.1's).</summary>
    public const float FramesPerSecond = 30f;

    public string Name => name;
    public float Duration => baseClip.Duration;
    public bool Loops => false;

    /// <summary>The 1.1 clip the blow is played from.</summary>
    public IAnimationClip Base => baseClip;

    public IReadOnlyList<StitchLayer> Layers => layers;

    public IReadOnlyList<StitchTurn> Turns => turns;

    /// <summary>The time warp (null: the base clip plays at the stitched clip's time).</summary>
    public StitchWarp? Warp { get; init; }

    /// <summary>The weapon arm set along a line by IK (null: none).</summary>
    public StitchReach? Reach { get; init; }

    /// <summary>The side blow's sweep flattened by IK (null: none).</summary>
    public StitchFlatten? Flatten { get; init; }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        float t = Math.Clamp(seconds, 0f, baseClip.Duration);
        float frame = t * FramesPerSecond;
        float baseT = Warp is { } warp ? Math.Clamp(warp.BaseFrame(frame) / FramesPerSecond, 0f, baseClip.Duration) : t;
        baseClip.Sample(baseT, skeleton, local);
        int n = Math.Min(local.Length, skeleton.Count);
        Span<BoneTransform> other = n <= 256 ? stackalloc BoneTransform[n] : new BoneTransform[n];
        for (int l = 0; l < layers.Count; l++)
        {
            var layer = layers[l];
            float w = layer.WeightAt(frame);
            if (w <= 0f) continue;
            var src = layer.Source;
            float at = t;
            if (src.Duration > 0f) at = src.Loops ? t - MathF.Floor(t / src.Duration) * src.Duration : MathF.Min(t, src.Duration);
            src.Sample(at, skeleton, other);
            int m = Math.Min(n, layer.Mask.Length);
            for (int b = 0; b < m; b++)
            {
                if (!layer.Mask[b]) continue;
                local[b] = w >= 1f ? other[b]
                    : new BoneTransform(DetMath.Slerp(local[b].Rotation, other[b].Rotation, w), PoseMath.Lerp(local[b].Translation, other[b].Translation, w));
            }
        }
        // The turns: about the model's vertical, taken into the turned bone's parent's frame (a root's parent is the model).
        for (int i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            float w = turn.WeightAt(frame);
            if (w == 0f || (uint)turn.Bone >= (uint)n) continue;
            int parent = skeleton.Bones[turn.Bone].Parent;
            var up = turn.Axis;
            if (parent >= 0 && parent < n)
            {
                // The parent's model rotation (its chain of local rotations from the root): the vertical in its frame.
                var q = System.Numerics.Quaternion.Identity;
                for (int b = parent; b >= 0 && b < n; b = skeleton.Bones[b].Parent) q = PoseMath.Multiply(local[b].Rotation, q);
                up = LimbIK.Rotate(LimbIK.Conjugate(q), up);
            }
            var r = local[turn.Bone];
            local[turn.Bone] = r with { Rotation = LimbIK.Normalize(PoseMath.Multiply(DetMath.AxisAngle(up, turn.Radians * w), r.Rotation)) };
        }
        if (Reach is { } reach && n == skeleton.Count) Drive(reach, frame, skeleton, local);
        if (Flatten is { } flat && n == skeleton.Count) Level(flat, frame, skeleton, local);
        Hips?.Apply(skeleton, local);   // last, a pure re-expression of the pose
    }

    /// <summary>The hips held upright, the blow's turn and bow carried by the bones on them (<see cref="StitchHips"/>), or null.</summary>
    public StitchHips? Hips { get; init; }

    /// <summary>The <see cref="Flatten"/>: the guard brought to its height where 1.1 has it across the body, the blade toward level.</summary>
    static void Level(StitchFlatten f, float frame, Skeleton skeleton, Span<BoneTransform> local)
    {
        float w = StitchLayer.Keyed(f.WeightFrames, f.Weights, frame);
        int n = skeleton.Count;
        if (w <= 0f || !f.Arm.Has || (uint)f.BladeBone >= (uint)n) return;
        w = MathF.Min(w, 1f);
        Span<System.Numerics.Matrix4x4> model = n <= 128 ? stackalloc System.Numerics.Matrix4x4[n] : new System.Numerics.Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, local, model);
        var guard = System.Numerics.Vector3.Transform(f.BladeGuard, model[f.BladeBone]);
        var tip = System.Numerics.Vector3.Transform(f.BladeTip, model[f.BladeBone]);
        var wrist = model[f.Arm.End].Translation;
        var along = LimbIK.Unit(tip - guard);
        var level = LimbIK.Unit(new System.Numerics.Vector3(along.X, along.Y, 0f));
        if (LimbIK.Length(level) < 0.5f) return;                         // the blade upright: no heading to keep
        var turn = LimbIK.FromTo(along, LimbIK.Unit(PoseMath.Lerp(along, level, w)));
        var goal = new System.Numerics.Vector3(guard.X, guard.Y, guard.Z + (f.Height - guard.Z) * w);
        var target = goal - LimbIK.Rotate(turn, guard - wrist);
        LimbIK.Solve(skeleton, local, model, f.Arm, target, 1f, f.Hint, turnEnd: turn);
    }

    /// <summary>The <see cref="Reach"/>: the blade's guard put on its line at this frame, the blade along it, by the arm's IK.</summary>
    static void Drive(StitchReach r, float frame, Skeleton skeleton, Span<BoneTransform> local)
    {
        float w = StitchLayer.Keyed(r.WeightFrames, r.Weights, frame);
        int n = skeleton.Count;
        if (w <= 0f || !r.Arm.Has || (uint)r.BladeBone >= (uint)n) return;
        Span<System.Numerics.Matrix4x4> model = n <= 128 ? stackalloc System.Numerics.Matrix4x4[n] : new System.Numerics.Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, local, model);
        var guard = System.Numerics.Vector3.Transform(r.BladeGuard, model[r.BladeBone]);
        var tip = System.Numerics.Vector3.Transform(r.BladeTip, model[r.BladeBone]);
        var wrist = model[r.Arm.End].Translation;
        float u = r.DriveTo > r.DriveFrom ? Math.Clamp((frame - r.DriveFrom) / (r.DriveTo - r.DriveFrom), 0f, 1f) : 1f;
        float s = u * u * (3f - 2f * u);
        var goal = r.From + r.Direction * (r.Length * s);
        var turn = DetMath.Slerp(System.Numerics.Quaternion.Identity, LimbIK.FromTo(LimbIK.Unit(tip - guard), r.Direction), MathF.Min(w, 1f));
        var target = goal - LimbIK.Rotate(turn, guard - wrist);
        LimbIK.Solve(skeleton, local, model, r.Arm, target, w, r.Hint, turnEnd: turn);
    }
}

/// <summary>
/// One stitched blow as a kind's action set holds it: the direction it serves (<see cref="StitchedAttacks.Overhead"/>,
/// <see cref="StitchedAttacks.Thrust"/>), the motion the action channel plays (an attack like 1.1's: its rate, its <c>Strike</c>
/// mark, its busy time), the clip seconds of its <b>ready point</b> (where a blow pressed and held waits: the wind-up done, just
/// before its active window), and the 1.1 clip it is stitched from.
/// </summary>
public readonly record struct StitchedMotion(string Direction, ActionMotion Motion, float ReadySeconds, string From);

/// <summary>
/// The hips held upright (ours): a pure re-expression of a pose, applied last when a composed blow is sampled. The
/// action channel plays a blow on the whole body while the body stands and on the upper body alone while it moves
/// (<see cref="PoseEvaluator.Local"/>: the hips and legs then the locomotion's). 1.1's blows turn and bow the whole body from the hips —
/// the forehand twists them 45–66° through its window, the overhead bows them 15–33°, the backhand 18–26°, the thrust 7–21° — so a blow
/// struck while walking lost that turn and bow: its sweep came round 65° late and high (the forehand over a crouched szlachta's head at
/// 1.8–1.9 m, and into the wall behind), and the blade's IK (the flattened sweep's height, the thrust's line, the wind-up's ready pose),
/// worked out on the blow's own hips, missed by as much. Held upright, the hips' bone (<see cref="Bone"/>) is given
/// <see cref="Upright"/> (the kind's stand's) and every bone on it — the spine's first bone and the thighs — the hips' difference from it,
/// rotation and offset: every other bone is where it was in the model (the blow standing is the same blow, its blade where it was), and
/// the upper body carries the blow's turn and bow whatever hips it is laid on. The hips' own drop and travel (0.1–0.3 m) are not carried
/// (that would stretch the waist): a blow struck walking is as high as the walk's hips. Allocates nothing.
/// </summary>
public sealed record StitchHips(int Bone, System.Numerics.Quaternion Upright)
{
    /// <summary>
    /// The module's switch that holds the composed blows' hips upright (<c>blow-hips-upright</c>; off by default: the control's ActionHips,
    /// on by default (<c>blows-on-moving-hips</c> off), carries the hips' turn for every blow in the pose evaluator, and with it this adds nothing).
    /// </summary>
    public const string Switch = "blow-hips-upright";

    /// <summary>The kind's hips (its skeleton's root bone) as its stand holds them (the stand at its start), or null with no root.</summary>
    public static StitchHips? Of(Skeleton skeleton, IAnimationClip stand)
    {
        int root = -1;
        for (int b = 0; b < skeleton.Count; b++) if (skeleton.Bones[b].Parent < 0) { root = b; break; }
        if (root < 0) return null;
        var local = new BoneTransform[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        stand.Sample(0f, skeleton, local);
        return new StitchHips(root, LimbIK.Normalize(local[root].Rotation));
    }

    /// <summary>Holds the hips of <paramref name="local"/> upright, the bones on them given the difference.</summary>
    public void Apply(Skeleton skeleton, Span<BoneTransform> local)
    {
        int n = Math.Min(local.Length, skeleton.Count);
        if ((uint)Bone >= (uint)n) return;
        var delta = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Conjugate(Upright), local[Bone].Rotation));
        for (int b = 0; b < n; b++)
        {
            if (skeleton.Bones[b].Parent != Bone) continue;
            var t = local[b];
            local[b] = new BoneTransform(LimbIK.Normalize(PoseMath.Multiply(delta, t.Rotation)), LimbIK.Rotate(delta, t.Translation));
        }
        local[Bone] = local[Bone] with { Rotation = Upright };
    }
}

/// <summary>Finding a kind's stitched blows by direction.</summary>
public static class StitchedMotions
{
    /// <summary>The stitched blow for <paramref name="direction"/> (<c>overhead</c>, <c>thrust</c>), or <see cref="ActionMotion.None"/>.</summary>
    public static ActionMotion Stitched(this ActionSet set, string direction)
    {
        foreach (var s in set.Stitched)
            if (string.Equals(s.Direction, direction, StringComparison.Ordinal)) return s.Motion;
        return ActionMotion.None;
    }

    /// <summary>The ready point (clip seconds) of the stitched blow for <paramref name="direction"/>, or −1.</summary>
    public static float StitchedReady(this ActionSet set, string direction)
    {
        foreach (var s in set.Stitched)
            if (string.Equals(s.Direction, direction, StringComparison.Ordinal)) return s.ReadySeconds;
        return -1f;
    }
}

/// <summary>The directions the stitched blows serve (the direction work's table names them).</summary>
public static class StitchedAttacks
{
    /// <summary>An overhand swing down (*"for the above attack it would be an overhand swing down"*).</summary>
    public const string Overhead = "overhead";

    /// <summary>A stab forward and a little low (*"then for the bottom it would be basically a stab"*).</summary>
    public const string Thrust = "thrust";

    /// <summary>The clip name a stitched blow is registered under (<c>stitched:overhead</c>): what <c>directions.json</c>, the blow-quality references and the log name.</summary>
    public static string ClipName(string direction) => "stitched:" + direction;
}
