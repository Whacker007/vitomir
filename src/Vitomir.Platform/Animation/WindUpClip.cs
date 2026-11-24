// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// One direction's wind-up and ready pose (*"Let's build the wind ups for all 4 directions"*; ours,
/// SANDBOX): how a held blow is cocked and how long it takes to get there and to strike from there. Times are in 1.1's frames (30 a
/// second) of the blow, played at the blow's own rate (the weapon's: the clip as long as 1.1's <c>STRIKETIME</c> makes it), so a
/// later pace can change them in the data alone. Places are in the kind's model space (metres; +X its right, +Y its front, +Z up,
/// the feet at 0) before the aim offset bends the upper body: the kinds of 1.1's skeleton 0 share it bone for bone.
/// </summary>
/// <param name="Direction">The direction it serves: <c>left</c>, <c>right</c>, <c>overhead</c> or <c>thrust</c>.</param>
/// <param name="WindUpFrames">From the press to the ready pose (the ready point: where a held blow waits).</param>
/// <param name="StrikeFrom">The frame of the blow (its flattened or stitched clip) the strike plays from: its body there is the ready pose's.</param>
/// <param name="JoinAt">The frame of the blow by which the blade is the blow's own again (the IK's hold on it eased out from <paramref name="StrikeFrom"/>): its path from here on is the blow's.</param>
/// <param name="StrikeFrames">How long the strike plays, from the ready point to the clip's end (the blow's frames <paramref name="StrikeFrom"/> to its end); −1 or 0 for as the blow plays them.</param>
/// <param name="Guard">The ready pose's guard (where the blade meets the hilt).</param>
/// <param name="Blade">The ready pose's blade: the way it points from the guard (made a unit vector).</param>
/// <param name="Via">The guard's way from the stance to the ready pose: the control point of its curve (a quadratic Bézier from where the blow's first frame holds it).</param>
/// <param name="TurnDegrees">The upper body turned to its left (negative: to its right) at the ready pose, about the vertical at the spine's first bone: eased in over the wind-up and out with the IK.</param>
public sealed record WindUpShape(string Direction, float WindUpFrames, float StrikeFrom, float JoinAt, float StrikeFrames,
                                 Vector3 Guard, Vector3 Blade, Vector3 Via, float TurnDegrees);

/// <summary>
/// A blow played with its own wind-up into a ready pose (ours): press → the wind-up into the ready pose (held there
/// while attack is held: <see cref="ReadySeconds"/>) → the strike from it → the recovery. The strike and the recovery are the blow's
/// own clip (<see cref="Strike"/>: the earlier build's flattened side blow or the earlier build's stitched overhead or thrust), played from its frame
/// <see cref="WindUpShape.StrikeFrom"/> to its end; the blade is put in the ready pose by the weapon arm's IK
/// (<see cref="LimbIK.Solve"/>, the blade turned to point its way) and the upper body turned, over 1.1's own body motion (the blow's
/// first frames, played through the wind-up), and the hold eased out by <see cref="WindUpShape.JoinAt"/>, from which the blade's path
/// through its active window is the blow's. Deterministic (<see cref="DetMath"/>); sampling allocates nothing.
/// </summary>
public sealed class WindUpClip : IAnimationClip
{
    readonly IAnimationClip strike;
    readonly WindUpShape shape;
    readonly Limb arm;
    readonly int bladeBone, turnBone;
    readonly Vector3 bladeGuard, bladeTip, hint, startGuard, startBlade, blade;
    readonly float windUp, strikeFrames, end, turnRadians;

    /// <summary>The frames a second the shape's times are counted in (1.1's).</summary>
    public const float FramesPerSecond = 30f;

    /// <param name="name">The clip's name (<c>windup:right</c>).</param>
    /// <param name="strike">The blow struck from the ready pose (its whole clip; the wind-up replaces its first <see cref="WindUpShape.StrikeFrom"/> frames).</param>
    /// <param name="shape">The wind-up's times and ready pose.</param>
    /// <param name="skeleton">The kind's skeleton (where the blow's first frame holds the blade is measured on it).</param>
    /// <param name="arm">The weapon arm.</param>
    /// <param name="bladeBone">The bone the blade is held by, and its guard and tip in that bone's frame (the held model's).</param>
    /// <param name="turnBone">The spine's first bone (the upper body over the hips), or −1 for no turn.</param>
    public WindUpClip(string name, IAnimationClip strike, WindUpShape shape, Skeleton skeleton, Limb arm, int bladeBone, Vector3 bladeGuard, Vector3 bladeTip, int turnBone)
    {
        Name = name;
        this.strike = strike;
        this.shape = shape;
        this.arm = arm;
        this.bladeBone = bladeBone;
        this.bladeGuard = bladeGuard;
        this.bladeTip = bladeTip;
        this.turnBone = turnBone;
        hint = new Vector3(1f, 0f, -1f);
        end = strike.Duration * FramesPerSecond;
        windUp = MathF.Max(0f, shape.WindUpFrames);
        float from = Math.Clamp(shape.StrikeFrom, 0f, end);
        strikeFrames = shape.StrikeFrames > 0f ? shape.StrikeFrames : end - from;
        turnRadians = shape.TurnDegrees * MathF.PI / 180f;
        blade = LimbIK.Unit(shape.Blade);
        // Where the blow's first frame holds the blade: the wind-up's start (the stance the blow begins from).
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        PoseEvaluator.Rest(skeleton, local);
        strike.Sample(0f, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        if ((uint)bladeBone < (uint)n)
        {
            startGuard = Vector3.Transform(bladeGuard, model[bladeBone]);
            startBlade = LimbIK.Unit(Vector3.Transform(bladeTip, model[bladeBone]) - startGuard);
        }
    }

    public string Name { get; }

    public float Duration => (windUp + strikeFrames) / FramesPerSecond;

    public bool Loops => false;

    /// <summary>The blow struck from the ready pose.</summary>
    public IAnimationClip Strike => strike;

    public WindUpShape Shape => shape;

    /// <summary>The ready point (clip seconds): the wind-up done, the ready pose reached; a held blow waits here.</summary>
    public float ReadySeconds => windUp / FramesPerSecond;

    /// <summary>The blow's frame played at this clip's <paramref name="frame"/>: through the wind-up its first <see cref="WindUpShape.StrikeFrom"/> frames (its body's own motion), then the rest at the strike's pace.</summary>
    public float BaseFrame(float frame)
    {
        float from = Math.Clamp(shape.StrikeFrom, 0f, end);
        if (frame <= windUp) return windUp > 0f ? from * Math.Clamp(frame / windUp, 0f, 1f) : from;
        return MathF.Min(end, from + (frame - windUp) * (end - from) / MathF.Max(strikeFrames, 1e-3f));
    }

    /// <summary>This clip's frame at which the blow's <paramref name="baseFrame"/> plays in the strike (its <c>Strike</c> mark, its window), for a frame at or after <see cref="WindUpShape.StrikeFrom"/>.</summary>
    public float FrameOf(float baseFrame)
    {
        float from = Math.Clamp(shape.StrikeFrom, 0f, end);
        return windUp + (baseFrame - from) * MathF.Max(strikeFrames, 1e-3f) / MathF.Max(end - from, 1e-3f);
    }

    static float Smooth(float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        Shaped(seconds, skeleton, local);
        Hips?.Apply(skeleton, local);   // last, a pure re-expression of the pose
    }

    /// <summary>The hips held upright, the blow's turn and bow carried by the bones on them (<see cref="StitchHips"/>), or null.</summary>
    public StitchHips? Hips { get; init; }

    void Shaped(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        float frame = Math.Clamp(seconds * FramesPerSecond, 0f, windUp + strikeFrames);
        float b = BaseFrame(frame);
        strike.Sample(b / FramesPerSecond, skeleton, local);
        int n = Math.Min(local.Length, skeleton.Count);
        if (n != skeleton.Count || !arm.Has || (uint)bladeBone >= (uint)n) return;
        // How far into the ready pose: up over the wind-up (s), held at the ready point, eased out from StrikeFrom to JoinAt (w).
        bool winding = frame <= windUp;
        float s = winding ? Smooth(windUp > 0f ? frame / windUp : 1f) : 1f;
        float join = shape.JoinAt - shape.StrikeFrom;
        float w = winding ? s : 1f - Smooth(join > 1e-4f ? (b - shape.StrikeFrom) / join : 1f);
        if (w <= 0f) return;
        if (turnRadians != 0f && (uint)turnBone < (uint)n) Turn(skeleton, local, n, turnBone, turnRadians * w);
        Span<Matrix4x4> model = n <= 128 ? stackalloc Matrix4x4[n] : new Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, local, model);
        var guard = Vector3.Transform(bladeGuard, model[bladeBone]);
        var along = LimbIK.Unit(Vector3.Transform(bladeTip, model[bladeBone]) - guard);
        var wrist = model[arm.End].Translation;
        Vector3 goal, way;
        if (winding)
        {
            // From the stance along the curve to the ready pose; the blade turned from the stance's way to the ready's.
            float r = 1f - s;
            goal = startGuard * (r * r) + shape.Via * (2f * r * s) + shape.Guard * (s * s);
            way = LimbIK.Rotate(DetMath.Slerp(Quaternion.Identity, LimbIK.FromTo(startBlade, blade), s), startBlade);
        }
        else
        {
            // Out of the ready pose into the blow's own path.
            goal = PoseMath.Lerp(guard, shape.Guard, w);
            way = LimbIK.Rotate(DetMath.Slerp(Quaternion.Identity, LimbIK.FromTo(along, blade), w), along);
        }
        var turn = LimbIK.FromTo(along, LimbIK.Unit(way));
        var target = goal - LimbIK.Rotate(turn, guard - wrist);
        LimbIK.Solve(skeleton, local, model, arm, target, 1f, hint, turnEnd: turn);
    }

    /// <summary>The upper body turned about the model's vertical at <paramref name="bone"/> (as <see cref="StitchTurn"/>).</summary>
    static void Turn(Skeleton skeleton, Span<BoneTransform> local, int n, int bone, float radians)
    {
        int parent = skeleton.Bones[bone].Parent;
        var up = Vector3.UnitZ;
        if (parent >= 0 && parent < n)
        {
            var q = Quaternion.Identity;
            for (int p = parent; p >= 0 && p < n; p = skeleton.Bones[p].Parent) q = PoseMath.Multiply(local[p].Rotation, q);
            up = LimbIK.Rotate(LimbIK.Conjugate(q), up);
        }
        var r = local[bone];
        local[bone] = r with { Rotation = LimbIK.Normalize(PoseMath.Multiply(DetMath.AxisAngle(up, radians), r.Rotation)) };
    }
}
