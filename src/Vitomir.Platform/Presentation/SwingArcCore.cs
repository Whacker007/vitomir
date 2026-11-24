// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the swing's arc's core — a body's blade at a clip time from its simulation state alone, quickly.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// The swing's arc's core: <b>a pure function of a body's simulation state</b> — its
/// <see cref="CharacterAnimation"/> (the clips, their times, the action's weight and legs, the guard's twist, side and height),
/// its <see cref="PoseRig"/> (the aim's bend, the legs' turn, the hips' offset and lean) and its placement (feet and facing)
/// — and, by <see cref="BladeAt(CharacterAssets, float, in CharacterAnimation, bool, in PoseRig, in Matrix4x4, float, out BladePlacement)"/>,
/// a clip time: the blade there, the same bits as <see cref="PoseEvaluator"/> and <see cref="HeldItems"/> give contact. No world,
/// no frame interpolation, no presentation state, no clock; nothing allocated once its buffers and cache are made. It is quick:
/// the clips' samples kept by (clip, time) — a deterministic memo: a sample kept is the sample made — the legs' IK left out (it
/// moves only the legs), only the bone's chain made into model space. <see cref="StepTick"/> moves a copy of the state on by a
/// tick as the systems do (the locomotion, the action channel, the aim's ease). An instance owns its buffers: one per caller
/// and thread. The presentation's <see cref="SwingArcs"/> wraps it (which blow, the body carried on by its velocity, the
/// level, who is met, the drawing); the simulation may call it too (a guard put across a blow's path).
/// </summary>
public sealed class SwingArcCore
{
    readonly PoseEvaluator poser = new();
    Matrix4x4[] model = [], skin = [];

    /// <summary>
    /// The body's striking segment for <paramref name="state"/> posed with <paramref name="rig"/> (when <paramref name="hasRig"/>)
    /// and stood at <paramref name="place"/> (<see cref="CharacterKind.Placement"/> of its feet and yaw): its first held blade from
    /// guard to tip, or bare-handed a ball of <paramref name="handRadius"/> about its weapon hand — <see cref="ContactSystem"/>'s
    /// <c>BladeAt</c> and <see cref="HeldItems.Blade"/>, bit for bit (<c>SwingArcTests</c>). False when it has neither.
    /// </summary>
    public bool BladeAt(CharacterAssets assets, float handRadius, in CharacterAnimation state, bool hasRig, in PoseRig rig, in Matrix4x4 place, out BladePlacement blade)
    {
        blade = default;
        var kind = assets.Kind(state.Kind);
        var held = kind.Held;
        for (int m = 0; m < held.Count; m++)
            for (int t = 0; t < held[m].Things.Count; t++)
            {
                var thing = held[m].Things[t];
                if (thing.Kind != HeldKind.Blade || (uint)thing.Bone >= (uint)assets.ModelOf(state.Kind).Skeleton.Count) continue;
                var mtx = PoseMath.Multiply(Bone(assets, kind, state, hasRig, rig, thing.Bone), place);
                float scale = LimbIK.Length(PoseMath.TransformNormal(Vector3.UnitX, mtx));
                blade = new BladePlacement(PoseMath.Transform(thing.Guard, mtx), PoseMath.Transform(thing.Tip, mtx), thing.Radius * scale);
                return true;
            }
        if (kind.Actions is not { WeaponHand: >= 0 } set || set.WeaponHand >= assets.ModelOf(state.Kind).Skeleton.Count) return false;
        var hand = PoseMath.Multiply(Bone(assets, kind, state, hasRig, rig, set.WeaponHand), place).Translation;
        blade = new BladePlacement(hand, hand, handRadius);
        return true;
    }

    /// <summary>
    /// The striking segment at clip time <paramref name="clipTime"/> of the action <paramref name="state"/> plays (its other numbers
    /// as they are: contact's sub-step rule), as <see cref="BladeAt(CharacterAssets, float, in CharacterAnimation, bool, in PoseRig, in Matrix4x4, out BladePlacement)"/>.
    /// </summary>
    public bool BladeAt(CharacterAssets assets, float handRadius, in CharacterAnimation state, bool hasRig, in PoseRig rig, in Matrix4x4 place, float clipTime, out BladePlacement blade)
    {
        var at = state;
        at.ActionTime = clipTime;
        return BladeAt(assets, handRadius, at, hasRig, rig, place, out blade);
    }

    // ---- the pose of one bone, quickly ----

    /// <summary>
    /// One bone's model-space matrix for a body in <paramref name="state"/> posed with <paramref name="rig"/>: the same numbers
    /// as <see cref="PoseEvaluator.Evaluate(CharacterAssets, in CharacterAnimation, in PoseRig, Span{Matrix4x4}, Span{Matrix4x4})"/>
    /// (its steps in its order, the same arithmetic), but the clips' samples kept by clip and time (a prediction made each tick
    /// asks for the same times again: the next tick's prediction steps through the same ticks), the legs' IK left out (it moves
    /// only the legs), and only the bone's chain from the root made into model space. A rig whose hands hold on (a move) is
    /// posed by the evaluator itself. Tested equal to the evaluator's (<c>SwingArcTests</c>).
    /// </summary>
    public Matrix4x4 Bone(CharacterAssets assets, CharacterKind kind, in CharacterAnimation state, bool hasRig, in PoseRig rig, int bone)
    {
        var skeleton = assets.ModelOf(state.Kind).Skeleton;
        int n = skeleton.Count;
        if (model.Length < n) { model = new Matrix4x4[n]; skin = new Matrix4x4[n]; }
        if (local.Length < n) { local = new BoneTransform[n]; act = new BoneTransform[n]; scratch = new BoneTransform[n]; }
        if (hasRig && (rig.HandWeight > 0f || rig.Pull > 0f))
        {
            poser.Evaluate(assets, state, rig, model.AsSpan(0, n), skin.AsSpan(0, n));
            return model[bone];
        }
        var l = local.AsSpan(0, n);
        Local(assets, kind, state, skeleton, l);
        var r = hasRig ? rig : default;
        var m = model.AsSpan(0, n);
        if (r.LegYaw != 0f && kind.Limbs is { } turned && kind.Actions is { Spine.Length: > 0 } acts) LegTurn.Apply(skeleton, turned.Root, acts.Spine, r.LegYaw, l, m);
        if (r.Active && kind.Limbs is { } limbs)
        {
            // PoseEvaluator.Place's hips: moved and leaned (its legs' IK moves only the legs; its hands' only in a move).
            var root = l[limbs.Root];
            var rotation = r.Lean != 0f ? LimbIK.Normalize(PoseMath.Multiply(LimbIK.Pitch(r.Lean), root.Rotation)) : root.Rotation;
            l[limbs.Root] = new BoneTransform(rotation, root.Translation + r.RootOffset);
        }
        if (state.GuardSide != 0f || state.GuardHeight != 0f) GuardLayer.Aim(skeleton, kind, state, l, m);
        if ((r.AimPitch != 0f || state.GuardTwist != 0f) && kind.Actions is { Spine.Length: > 0 } bend)
        {
            AimOffset.Apply(skeleton, bend.Spine, r.AimPitch, l);
            GuardLayer.Twist(skeleton, bend.Spine, state.GuardTwist, l);
        }
        if (GuardSolver.Wants(state, r)) GuardSolver.Pose(skeleton, kind, state, r, l, m);   // the evaluator's hook, in its place
        if (state.RecoilBack != System.Numerics.Vector3.Zero) Recoil.Apply(skeleton, kind, state, l, m);   // the evaluator's recoil, after the bend   // (the recoil after the solver: a kick on the arm where it is)
        if (bone < 0) { PoseEvaluator.ToModel(skeleton, l, m); return default; }   // every bone (PoseAll)
        // The chain from the root to the bone, parents first (PoseEvaluator.ToModel's arithmetic).
        var chain = Chain(skeleton, bone);
        var bones = skeleton.Bones;
        for (int i = 0; i < chain.Length; i++)
        {
            int b = chain[i];
            var x = PoseMath.ToMatrix(l[b]);
            int parent = bones[b].Parent;
            m[b] = parent >= 0 && parent < b ? PoseMath.Multiply(x, m[parent]) : x;
        }
        return m[bone];
    }

    /// <summary>
    /// Every bone of a body in <paramref name="state"/> posed with <paramref name="rig"/> in model space into
    /// <paramref name="into"/> (at least the skeleton's bone count), as <see cref="Bone"/> poses one: the legs as the motion has them
    /// (their IK left out) — for another body's hit volumes where only a little error in the legs is borne.
    /// </summary>
    public void PoseAll(CharacterAssets assets, in CharacterAnimation state, bool hasRig, in PoseRig rig, Span<Matrix4x4> into)
    {
        var kind = assets.Kind(state.Kind);
        int n = assets.ModelOf(state.Kind).Skeleton.Count;
        var r = hasRig ? rig with { HandWeight = 0f, Pull = 0f } : default;
        Bone(assets, kind, state, hasRig, r, -1);
        model.AsSpan(0, n).CopyTo(into);
    }

    BoneTransform[] local = [], act = [], scratch = [];
    readonly Dictionary<(Skeleton, int), int[]> chains = [];

    /// <summary>The bone's ancestors and itself, the root first (made once per skeleton and bone).</summary>
    int[] Chain(Skeleton skeleton, int bone)
    {
        if (chains.TryGetValue((skeleton, bone), out var c)) return c;
        var list = new List<int>();
        for (int b = bone; b >= 0; )
        {
            list.Add(b);
            int parent = skeleton.Bones[b].Parent;
            b = parent >= 0 && parent < b ? parent : -1;
        }
        list.Reverse();
        return chains[(skeleton, bone)] = [.. list];
    }

    /// <summary><see cref="PoseEvaluator.Local"/>'s blend, its clips' samples from the cache, the locomotion not sampled when the action covers the whole body.</summary>
    public void Local(CharacterAssets assets, CharacterKind kind, in CharacterAnimation state, Skeleton skeleton, Span<BoneTransform> result)
    {
        int n = skeleton.Count;
        bool acting = state.Action != ActionRole.None && state.ActionWeight > 0f;
        var upper = kind.Actions?.UpperBody;
        float w = MathF.Min(1f, state.ActionWeight), legs = w * Math.Clamp(state.ActionLegs, 0f, 1f);
        bool whole = acting && w >= 1f && (upper is null || legs >= 1f);
        if (!whole)
        {
            if (state.Clip >= 0) Moving.Get(assets, state.Clip, state.Time, skeleton).CopyTo(result);
            else PoseEvaluator.Rest(skeleton, result);
            if (state.PreviousClip >= 0 && state.Blend < 1f)
                PoseEvaluator.Crossfade(Moving.Get(assets, state.PreviousClip, state.PreviousTime, skeleton), result[..n], state.Blend, result[..n]);
        }
        if (!acting)
        {
            if (state.GuardWeight > 0f) GuardLayer.Over(assets, state, skeleton, result[..n], scratch.AsSpan(0, n));
            return;
        }
        var a = act.AsSpan(0, n);
        Samples.Get(assets, state.ActionClip, state.ActionTime, skeleton).CopyTo(a);
        if (state.ActionFrom != ActionRole.None && state.ActionBlend < 1f)
            PoseEvaluator.Crossfade(Samples.Get(assets, state.ActionFromClip, state.ActionFromTime, skeleton), a, state.ActionBlend, a);
        for (int b = 0; b < n; b++)
        {
            float wb = upper is null || (b < upper.Length && upper[b]) ? w : legs;
            if (wb >= 1f) result[b] = a[b];
            else if (wb > 0f) result[b] = new BoneTransform(DetMath.Slerp(result[b].Rotation, a[b].Rotation, wb), PoseMath.Lerp(result[b].Translation, a[b].Translation, wb));
        }
        if (assets.CarryActionHips && upper is not null) ActionHips.Carry(skeleton, upper, result[..n], a, w, legs);   // the evaluator's
        if (state.GuardWeight > 0f) GuardLayer.Over(assets, state, skeleton, result[..n], scratch.AsSpan(0, n));
    }

    /// <summary>
    /// The clips' samples kept by (clip, time): 384 of the actions', 128 of the locomotion's, the oldest let go (made once; nothing
    /// allocated after) — the actions' (a blow's window, asked for again tick after tick) apart from the locomotion's (a walk's or
    /// an idle's times move on each tick), so the one does not push the other out.
    /// </summary>
    readonly SampleCache Samples = new(384), Moving = new(128);

    sealed class SampleCache(int size)
    {
        public int Size { get; } = size;
        // All the slots in one array, made at the first sample (and again only for a skeleton with more bones): nothing is
        // allocated as the ring is filled and reused.
        BoneTransform[] kept = [];
        int stride;
        readonly (int Clip, int Bits, Skeleton? Skeleton)[] keys = new (int, int, Skeleton?)[size];
        readonly Dictionary<(int, int, Skeleton), int> index = new(size * 2);
        int next;

        public ReadOnlySpan<BoneTransform> Get(CharacterAssets assets, int clip, float time, Skeleton skeleton)
        {
            int bits = BitConverter.SingleToInt32Bits(time);
            int n = skeleton.Count;
            if (n > stride)
            {
                stride = n;
                kept = new BoneTransform[Size * stride];
                index.Clear();
                Array.Clear(keys);
                next = 0;
            }
            if (index.TryGetValue((clip, bits, skeleton), out int at)) return kept.AsSpan(at * stride, n);
            at = next;
            next = (next + 1) % Size;
            if (keys[at].Skeleton is { } old) index.Remove((keys[at].Clip, keys[at].Bits, old));
            assets.Clip(clip).Sample(time, skeleton, kept.AsSpan(at * stride, n));
            keys[at] = (clip, bits, skeleton);
            index[(clip, bits, skeleton)] = at;
            return kept.AsSpan(at * stride, n);
        }
    }

    /// <summary>
    /// <paramref name="s"/> and <paramref name="rig"/> moved on by one tick as the systems move them: the locomotion with its
    /// motion and rate kept (<see cref="AnimationSystem"/>), the action channel (<see cref="ActionChannel.Advance"/>, standing or
    /// moving, a fast blow's fade-in), and the aim's bend eased toward its target while an attack or the block plays
    /// (<see cref="PoseRigSystem"/>). Starting a blow (<see cref="ActionChannel.Start"/>) and the guard's turn are the caller's.
    /// </summary>
    public static void StepTick(CharacterAssets assets, ActionChannelSettings channel, AimOffsetSettings aim, bool hasSpine, bool standing, float fadeIn,
        ref CharacterAnimation s, ref PoseRig rig, float dt = World.TickSeconds)
    {
        Locomotion(assets, ref s, dt);
        ActionChannel.Advance(ref s, assets, channel, standing, dt, ActionRoles.IsAttack(s.Action) ? fadeIn : 1f);
        bool aiming = (ActionRoles.IsAttack(s.Action) || s.Action == ActionRole.Block) && !s.ActionEnding && hasSpine;
        rig.AimPitch = Towards(rig.AimPitch, aiming ? AimOffset.Clamp(rig.AimTarget, aim) : 0f, aim.Rate * dt);
    }

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    static readonly AnimationSettings Animation = new();

    /// <summary>The locomotion moved on by <paramref name="seconds"/> with its motion and rate kept (<see cref="AnimationSystem"/>'s advance).</summary>
    public static void Locomotion(CharacterAssets assets, ref CharacterAnimation a, float seconds)
    {
        a.Time = Wrap(assets, a.Clip, a.Time + seconds * a.Rate);
        if (a.PreviousClip >= 0)
        {
            a.PreviousTime = Wrap(assets, a.PreviousClip, a.PreviousTime + seconds * a.PreviousRate);
            a.Blend = Animation.FadeSeconds > 0f ? a.Blend + seconds / Animation.FadeSeconds : 1f;
            if (a.Blend >= 1f) { a.Blend = 1f; a.PreviousClip = -1; a.PreviousTime = 0f; a.PreviousRate = 0f; }
        }
    }

    /// <summary>A clip's time wrapped (a loop) or held at its ends, as the animation's own.</summary>
    public static float Wrap(CharacterAssets assets, int clip, float time)
    {
        if (clip < 0) return 0f;
        var c = assets.Clip(clip);
        float d = c.Duration;
        if (d <= 0f) return 0f;
        if (!c.Loops) return time > d ? d : time < 0f ? 0f : time;
        if (time >= d || time < 0f)
        {
            time -= MathF.Floor(time / d) * d;
            if (time >= d) time -= d;
            if (time < 0f) time = 0f;
        }
        return time;
    }

}
