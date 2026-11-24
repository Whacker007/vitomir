// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the bow's ready pose and draw, composed from 1.1's own shot clip (chrisAttackBow, read from the install at run
// time) with the arm solver (LimbIK): 1.1's clip raises the bow and draws it in one movement, so it has no frame with the bow up and
// the string undrawn. Nothing of 1.1's is stored: the clip is composed when the install's is read.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The bow's shot with its ready pose (to aim, Christof brings the bow up to
/// almost full vertical, held as if ready to draw it back), composed from 1.1's shot clip (<see cref="Source"/>) and
/// played by the bow's state on the same timeline the bow's look reads (<see cref="Combat.BowShot"/>):
/// <list type="bullet">
/// <item><b>The ready pose</b> (at <see cref="ReadyAt"/>): the clip's own full-draw frame (<see cref="FullDrawAt"/>: the bow up, near
/// vertical, the bow arm out, the body side-on) with the draw arm put by the arm solver so the draw hand is on the string at rest —
/// <see cref="RestShare"/> of the full draw's hand separation from the bow hand, along the arrow's line — the arrow nocked, ready to
/// draw;</item>
/// <item><b>the raise</b> (0 to <see cref="ReadyAt"/>): from the clip's first frame (the bow low at the waist) to the ready pose, each
/// bone turned evenly (eased in and out);</item>
/// <item><b>the draw</b> (<see cref="ReadyAt"/> to <see cref="ReadyAt"/> + the clip's draw span): the full-draw frame with the draw hand
/// pulled back along the arrow's line from the rest to where the clip's own full draw has it (the arm solver at each of
/// <see cref="DrawKeys"/> keys, blended between), so the ready pose and the full draw join without a jump;</item>
/// <item><b>the loose</b> and after: the clip itself from its full draw on (its release mark, the string let go and the follow-through).</item>
/// </list>
/// Played backwards it lowers the bow (a draw let down to the ready pose, then aim let go to the waist). Composed once when the kind is
/// made; deterministic (<see cref="LimbIK"/>, <see cref="DetMath"/>); sampling allocates nothing.
/// </summary>
public sealed class BowReadyClip : IAnimationClip
{
    /// <summary>The draw hand at rest on the string, as a share of the full draw's separation of the hands (ENGINE_SANDBOX, ours: about 0.2 m of a 0.7 m draw).</summary>
    public const float RestShare = 0.3f;

    /// <summary>How long the raise takes on this clip's timeline (s): the bow raised by the raise's progress; played backwards at the clip's rate it lowers in this long.</summary>
    public const float RaiseSeconds = 0.3f;

    /// <summary>The keys the draw is solved at (from the rest to full draw).</summary>
    public const int DrawKeys = 24;

    public IAnimationClip Source { get; }
    public string Name { get; }
    public float Duration { get; }
    public bool Loops => false;

    /// <summary>The ready pose's time on this clip (s).</summary>
    public float ReadyAt => RaiseSeconds;

    /// <summary>The full draw's time on this clip (s) and on the source's (<see cref="FullDrawAt"/>).</summary>
    public float FullDraw { get; }
    public float FullDrawAt { get; }

    /// <summary>This clip's time of a time on the source after its full draw (the loose and after).</summary>
    public float FromSource(float seconds) => seconds + Shift;

    /// <summary>How much later than the source's a time after the full draw is on this clip.</summary>
    public float Shift { get; }

    /// <summary>How far the arm solver left the draw hand from the rest point at the ready pose (in the model's units; 0 when it reached).</summary>
    public float RestMiss { get; }

    readonly BoneTransform[] low, ready;
    readonly BoneTransform[][] keys;

    /// <summary>
    /// Composes the clip from <paramref name="source"/> on <paramref name="skeleton"/>: <paramref name="bowArm"/> holds the bow and
    /// <paramref name="drawArm"/> draws; the clip's draw begins at <paramref name="drawStartAt"/> and is full at <paramref name="fullDrawAt"/>
    /// (source seconds). The draw arm's elbow bends the way the full draw's does.
    /// </summary>
    public BowReadyClip(IAnimationClip source, Skeleton skeleton, Limb bowArm, Limb drawArm, float drawStartAt, float fullDrawAt)
    {
        Source = source;
        Name = source.Name + "+ready";
        FullDrawAt = fullDrawAt;
        float span = MathF.Max(1f / 30f, fullDrawAt - drawStartAt);
        FullDraw = RaiseSeconds + span;
        Shift = FullDraw - fullDrawAt;
        Duration = source.Duration + Shift;
        int n = skeleton.Count;
        low = new BoneTransform[n];
        source.Sample(0f, skeleton, low);
        var full = new BoneTransform[n];
        source.Sample(fullDrawAt, skeleton, full);
        var model = new Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, full, model);
        var bowHand = model[bowArm.End].Translation;
        var drawHand = model[drawArm.End].Translation;
        var rest = bowHand + (drawHand - bowHand) * RestShare;
        var hint = model[drawArm.Lower].Translation - (model[drawArm.Upper].Translation + drawHand) * 0.5f;
        keys = new BoneTransform[DrawKeys + 1][];
        for (int k = 0; k <= DrawKeys; k++)
        {
            var pose = (BoneTransform[])full.Clone();
            if (k < DrawKeys)
            {
                float u = k / (float)DrawKeys;
                LimbIK.Solve(skeleton, pose, model, drawArm, rest + (drawHand - rest) * u, 1f, hint);
            }
            keys[k] = pose;
        }
        ready = keys[0];
        PoseEvaluator.ToModel(skeleton, ready, model);
        RestMiss = LimbIK.Length(model[drawArm.End].Translation - rest);
    }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        int n = Math.Min(local.Length, skeleton.Count);
        if (seconds >= FullDraw) { Source.Sample(MathF.Min(seconds - Shift, Source.Duration), skeleton, local); return; }
        if (seconds <= 0f) { Copy(low, local, n, skeleton); return; }
        if (seconds < ReadyAt)
        {
            float t = seconds / ReadyAt;
            t = t * t * (3f - 2f * t);
            PoseEvaluator.Crossfade(low.AsSpan(0, n), ready.AsSpan(0, n), t, local[..n]);
            return;
        }
        float u = Math.Clamp((seconds - ReadyAt) / (FullDraw - ReadyAt), 0f, 1f) * DrawKeys;
        int k = Math.Min(DrawKeys - 1, (int)MathF.Floor(u));
        PoseEvaluator.Crossfade(keys[k].AsSpan(0, n), keys[k + 1].AsSpan(0, n), u - k, local[..n]);
    }

    static void Copy(BoneTransform[] pose, Span<BoneTransform> local, int n, Skeleton skeleton)
    {
        int m = Math.Min(n, pose.Length);
        pose.AsSpan(0, m).CopyTo(local);
        for (int b = m; b < n; b++) local[b] = skeleton.Bones[b].Rest;
    }
}
