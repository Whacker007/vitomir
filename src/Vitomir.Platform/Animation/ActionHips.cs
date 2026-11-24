// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// Ours, ENGINE_SANDBOX; off unless <see cref="CharacterAssets.CarryActionHips"/>: an action played on a moving
/// body keeps its upper body turned as the action turns it. While a body moves the action channel plays on the upper body only, over
/// the walk's or the run's hips (<see cref="PoseEvaluator.Local"/>: the bones outside <see cref="ActionSet.UpperBody"/> by
/// <see cref="CharacterAnimation.ActionLegs"/>, 0 while moving), so the turn and lean of the action's own hips — 1.1's forehand turns
/// its hips well into the swing — were lost, and the blows, whose blades two earlier builds bring level by IK over those hips, tilted: the
/// side blows rising a foot or two, the thrust low and to the left, a walking blow from the right coming down to the floor. Here each
/// root of the upper body (the spine's first bone) is turned by the difference between the action's chain of parents and the pose's,
/// so its orientation in the model is the action's; its place stays on the moving hips. Nothing changes for a standing body (the legs
/// take the action) or with no action.
/// </summary>
public static class ActionHips
{
    /// <summary>The enhanced module's switch that turns it off for a run, as before (<c>--blows-on-moving-hips</c>; an arena spec's <c>switches</c>). On by default.</summary>
    public const string Switch = "blows-on-moving-hips";

    /// <summary>
    /// Turns each root of <paramref name="upper"/> in <paramref name="result"/> (the pose with the action laid over it at
    /// <paramref name="weight"/>) so that, at weight 1, its rotation in the model is the action's (<paramref name="action"/>'s chain);
    /// partly at a lower weight. Does nothing when <paramref name="legs"/> (the action's weight on the hips and legs) is 1 or more.
    /// Deterministic (<see cref="PoseMath"/>, <see cref="DetMath"/>); allocates nothing.
    /// </summary>
    public static void Carry(Skeleton skeleton, bool[] upper, Span<BoneTransform> result, ReadOnlySpan<BoneTransform> action, float weight, float legs)
    {
        if (weight <= 0f || legs >= 1f) return;
        int n = Math.Min(skeleton.Count, Math.Min(result.Length, action.Length));
        for (int b = 0; b < n && b < upper.Length; b++)
        {
            if (!upper[b]) continue;
            int parent = skeleton.Bones[b].Parent;
            if (parent >= 0 && parent < upper.Length && upper[parent]) continue;    // not a root of the upper body
            var mine = Quaternion.Identity;
            var its = Quaternion.Identity;
            for (int p = parent; p >= 0 && p < n; p = skeleton.Bones[p].Parent)
            {
                mine = PoseMath.Multiply(result[p].Rotation, mine);
                its = PoseMath.Multiply(action[p].Rotation, its);
            }
            var turn = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Conjugate(mine), its));
            if (weight < 1f) turn = DetMath.Slerp(Quaternion.Identity, turn, weight);
            result[b] = result[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, result[b].Rotation)) };
        }
    }
}
