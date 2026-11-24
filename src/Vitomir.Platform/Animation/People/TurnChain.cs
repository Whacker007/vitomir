// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on the design note ("the camera can move at those twitch speeds and the
// actor's body parts are always orienting toward where the camera is, but playing catch up"), and the models repository's research page
// on turning on the spot (the head first, the thorax 50–60 ms after, the pelvis 100–130 ms after; the head up to ~20° ahead of the
// thorax in a fast turn).
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// Where a person's head and chest are drawn turned, the turn as a chain that catches up (<see cref="PeopleTurn"/>; the hips are
/// <see cref="PoseRig.LegYaw"/>, as before): each an angle from the body's facing — the view — (radians, toward its left positive)
/// and how fast it is turning. Moved on in the tick by <see cref="LegTurnSystem"/>, laid on the pose by <see cref="TurnChain.Apply"/>;
/// it moves the hit volumes, so it is the simulation's and digested. A world has the store only when it has people and the chain
/// is on (<see cref="AnimationRegistration.Animation"/>), so 1.1's worlds (the arena) and the earlier build's turn digest as before.
/// </summary>
public struct PersonTurn
{
    /// <summary>The head's turn from the facing and its speed (radians, radians a second).</summary>
    public float Head, HeadSpeed;

    /// <summary>The chest's turn from the facing and its speed.</summary>
    public float Chest, ChestSpeed;

    int on;

    /// <summary>Whether the chain has posed this person (0, the default: the pose is the earlier build's; set on its first tick).</summary>
    public bool On { readonly get => on != 0; set => on = value ? 1 : 0; }
}

/// <summary>
/// The turn as a chain on a person's pose (<see cref="PersonTurn"/>, <see cref="PeopleTurn"/>). On the pose as sampled (the
/// motion and its fade, the action laid over), before the limbs are placed:
/// <list type="number">
/// <item><b>A turn clip's own upper-body lead taken out</b> (<c>turn_*</c>, <c>crouch_turn_*</c>: the models repository's head starts,
/// the chest 0.06 s after, the hips 0.13 s after, the head up to 18° ahead of the chest): the chest's and the head's turn about the
/// vertical through the clip from its first frame (the stand, or the crouch), measured across the shoulders and across the eyes when
/// the kind is registered (<see cref="TurnClip.Lead"/>), each turn clip's by its share of the pose (a turn fading in or out, or two
/// across a turn turned round). So a turn takes the root, the legs and the hips from its clip and the chain is all the chest and
/// the head do.</item>
/// <item><b>The legs' turn</b> about the root bone (between the feet), as before (<see cref="LegTurn.Apply"/>): the hips and everything
/// under them turned by <see cref="PoseRig.LegYaw"/>.</item>
/// <item><b>The spine turned</b> so the chest is at the chain's <see cref="PersonTurn.Chest"/> from the facing. spread over
/// <c>spine_01</c> to <c>_03</c> by <see cref="SpineShares"/> — a real trunk turns mostly in its thoracic spine, little in the lumbar —
/// and the spine left on the pelvis, which the upper chain never turns or moves (<see cref="CharacterAssets.PersonWaistEven"/>:
/// split evenly and the spine moved at its first bone so the top of it stayed where it was, as before — which slid the trunk off
/// the hips at the belt by up to 21 cm on a play-test's tape: the waist looked disconnected); <b>the neck and the head</b> turned so the head
/// is at <see cref="PersonTurn.Head"/>, split evenly over <c>neck_01</c> and <c>head</c> (the lower neck and the joint under the skull,
/// which carry about half of the neck's turn each).</item>
/// </list>
/// <see cref="DetMath"/> and <see cref="PoseMath"/> only; allocates nothing.
/// </summary>
public static class TurnChain
{
    /// <summary>
    /// The shares of the chest's turn over the hips that <c>spine_01</c> (the lumbar spine), <c>_02</c> (the lower thoracic) and
    /// <c>_03</c> (the upper thoracic, under the chest) each take: 15%, 40%, 45% — the trunk turns mostly in its thoracic spine (about
    /// 70–85% of it), little in the lumbar (5–15%); so the belt stays with the hips and the most two neighbouring bones are ever turned
    /// apart is 45% of <see cref="LegTurnSettings.SpineMost"/> (13.5° at 30°) over what the motion has. A spine of another length: evenly.
    /// </summary>
    public static ReadOnlySpan<float> SpineShares => spineShares;

    static readonly float[] spineShares = [0.15f, 0.40f, 0.45f];

    /// <summary>
    /// Lays the chain on <paramref name="local"/> (<paramref name="scratch"/> is worked in, at least the skeleton's bone count):
    /// the turn clips' lead out, the legs turned by <paramref name="legYaw"/>, the chest and the head to <paramref name="turn"/>'s.
    /// </summary>
    public static void Apply(Skeleton skeleton, CharacterAssets assets, in CharacterAnimation state, PersonMotion person, float legYaw, in PersonTurn turn,
                             Span<BoneTransform> local, Span<Matrix4x4> scratch)
    {
        var rig = person.Rig;
        var spine = rig.Spine;
        int n = skeleton.Count;
        if (spine.Length == 0 || (uint)rig.Root >= (uint)local.Length) return;
        for (int i = 0; i < spine.Length; i++) if ((uint)spine[i] >= (uint)local.Length) return;

        // 1. The turn clips' own lead out: the chest from where the clip has it to the chain's, the head from where the chest has it.
        var (spineTurn, neckTurn) = Turns(assets, state, turn);
        if (legYaw == 0f && spineTurn == 0f && neckTurn == 0f) return;

        if (!assets.PersonWaistEven)
        {
            // The legs (the hips and all under them, about the root between the feet), the spine on the pelvis by its shares, the neck and head.
            if (legYaw != 0f) LegTurn.Turn(skeleton, rig.Root, legYaw, local);
            float over = spineTurn - legYaw;
            if (over != 0f)
            {
                var shares = SpineShares;
                for (int i = 0; i < spine.Length; i++) LegTurn.Turn(skeleton, spine[i], over * (spine.Length == shares.Length ? shares[i] : 1f / spine.Length), local);
            }
            Neck(skeleton, rig, neckTurn, local);
            return;
        }

        // 2 and 3, the earlier build's: the legs, the spine (the top of it kept where it was), the neck and head.
        PoseEvaluator.ToModel(skeleton, local[..n], scratch[..n]);
        int first = spine[0], last = spine[^1];
        var was = scratch[last].Translation;
        if (legYaw != 0f) LegTurn.Turn(skeleton, rig.Root, legYaw, local);
        float share = (spineTurn - legYaw) / spine.Length;
        if (share != 0f) for (int i = 0; i < spine.Length; i++) LegTurn.Turn(skeleton, spine[i], share, local);
        PoseEvaluator.ToModel(skeleton, local[..n], scratch[..n]);
        var moved = was - scratch[last].Translation;
        int parent = skeleton.Bones[first].Parent;
        if (parent >= 0 && parent < first)
            moved = LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), moved);
        local[first] = local[first] with { Translation = local[first].Translation + moved };
        Neck(skeleton, rig, neckTurn, local);
    }

    /// <summary>
    /// The turns the chain lays on the pose (radians): the spine's (the chest from where the motion has it to the chain's, with the turn
    /// clips' own lead out, each by its share of the pose) and the neck's (the head from where the chest has it to the chain's).
    /// </summary>
    static (float Spine, float Neck) Turns(CharacterAssets assets, in CharacterAnimation state, in PersonTurn turn)
    {
        float blend = state.PreviousClip >= 0 ? Math.Clamp(state.Blend, 0f, 1f) : 1f;
        float chestLead = 0f, headLead = 0f;
        if (state.Clip >= 0 && assets.Clip(state.Clip) is TurnClip now)
        {
            var (c, h) = now.Lead(state.Time);
            (chestLead, headLead) = (chestLead + c * blend, headLead + h * blend);
        }
        if (state.PreviousClip >= 0 && blend < 1f && assets.Clip(state.PreviousClip) is TurnClip before)
        {
            var (c, h) = before.Lead(state.PreviousTime);
            (chestLead, headLead) = (chestLead + c * (1f - blend), headLead + h * (1f - blend));
        }
        return (turn.Chest - chestLead, turn.Head - turn.Chest - headLead);
    }

    /// <summary>
    /// A person's eyes (model space, <paramref name="eyes"/>) as the pose the chain turned has them (<paramref name="skin"/>, its skin
    /// matrices), turned back to where they would be with the body on the view: the eye is the simulation's (the first-person camera, the
    /// reticle, use, the aim), and the chain is drawn but moves no eye. Each of the chain's turns is undone, last first, about the vertical
    /// through its bone's joint as posed — the head's and the neck's shares, the spine's (by <see cref="SpineShares"/>), the legs' turn
    /// about the root — so it is exact while those joints are where the pose has them (the rig's lean, a few degrees, aside). With the earlier build's
    /// waist the camera keeps the earlier build's own way (the eyes turned back by the head's turn about the neck).
    /// </summary>
    public static Vector3 EyesOnTheView(Skeleton skeleton, CharacterAssets assets, in CharacterAnimation state, PersonMotion person, float legYaw, in PersonTurn turn,
                                        ReadOnlySpan<Matrix4x4> skin, Vector3 eyes)
    {
        var rig = person.Rig;
        var spine = rig.Spine;
        int n = Math.Min(skeleton.Count, skin.Length);
        if (!turn.On || spine.Length == 0 || (uint)rig.Root >= (uint)n) return eyes;
        for (int i = 0; i < spine.Length; i++) if ((uint)spine[i] >= (uint)n) return eyes;
        var (spineTurn, neckTurn) = Turns(assets, state, turn);
        if (neckTurn != 0f)
        {
            bool neck = (uint)rig.Neck < (uint)n, headBone = (uint)rig.Head < (uint)n;
            float each = neck && headBone ? neckTurn * 0.5f : neckTurn;
            if (headBone) eyes = About(eyes, Joint(skeleton, skin, rig.Head), -each);
            if (neck) eyes = About(eyes, Joint(skeleton, skin, rig.Neck), -each);
        }
        float over = spineTurn - legYaw;
        if (over != 0f)
        {
            var shares = SpineShares;
            for (int i = spine.Length - 1; i >= 0; i--) eyes = About(eyes, Joint(skeleton, skin, spine[i]), -over * (spine.Length == shares.Length ? shares[i] : 1f / spine.Length));
        }
        return legYaw != 0f ? About(eyes, Joint(skeleton, skin, rig.Root), -legYaw) : eyes;

        static Vector3 Joint(Skeleton skeleton, ReadOnlySpan<Matrix4x4> skin, int b) =>
            Matrix4x4.Invert(skeleton.InverseBind[b], out var bind) ? PoseMath.Transform(bind.Translation, skin[b]) : skin[b].Translation;
    }

    /// <summary><paramref name="p"/> turned by <paramref name="angle"/> about the vertical through <paramref name="pivot"/>.</summary>
    static Vector3 About(Vector3 p, Vector3 pivot, float angle)
    {
        var (sin, cos) = DetMath.SinCos(angle);
        float x = p.X - pivot.X, y = p.Y - pivot.Y;
        return new Vector3(pivot.X + x * cos - y * sin, pivot.Y + x * sin + y * cos, p.Z);
    }

    /// <summary>The head's turn over the chest split evenly over <c>neck_01</c> and <c>head</c> (all on the one there is).</summary>
    static void Neck(Skeleton skeleton, Assets.People.PersonRig rig, float neckTurn, Span<BoneTransform> local)
    {
        if (neckTurn == 0f) return;
        bool neck = (uint)rig.Neck < (uint)local.Length, headBone = (uint)rig.Head < (uint)local.Length;
        float each = neck && headBone ? neckTurn * 0.5f : neckTurn;
        if (neck) LegTurn.Turn(skeleton, rig.Neck, each, local);
        if (headBone) LegTurn.Turn(skeleton, rig.Head, each, local);
    }


    /// <summary>
    /// The chest's and the head's turn about the vertical in a pose's model space (radians; 0 square to the model's facing, +Y): the
    /// line from the left shoulder to the right and from the left eye to the right. False when the rig lacks them.
    /// </summary>
    public static bool Lines(ReadOnlySpan<Matrix4x4> model, Assets.People.PersonRig rig, out float chest, out float head)
    {
        chest = head = 0f;
        int l = rig.LeftArm.Upper, r = rig.RightArm.Upper;
        if ((uint)l >= (uint)model.Length || (uint)r >= (uint)model.Length || (uint)rig.EyeL >= (uint)model.Length || (uint)rig.EyeR >= (uint)model.Length) return false;
        chest = Across(model[l].Translation, model[r].Translation);
        head = Across(model[rig.EyeL].Translation, model[rig.EyeR].Translation);
        return true;

        static float Across(Vector3 left, Vector3 right) => DetMath.Atan2(right.Y - left.Y, right.X - left.X);
    }
    /// <summary>
    /// The upper body's own lead through a turn clip (<see cref="TurnClip.ChestLeads"/>), 30 times a second and at its end: the chest's turn
    /// from its first frame and the head's from the chest's (<see cref="Lines"/>), unwrapped. Made once per source when the kind is
    /// registered (<see cref="PeopleKinds"/>).
    /// </summary>
    internal static (float[] Chest, float[] Head) Leads(IAnimationClip clip, Skeleton skeleton, Assets.People.PersonRig rig)
    {
        int n = skeleton.Count, frames = Math.Max(1, (int)MathF.Ceiling(clip.Duration * 30f - 1e-3f));
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        var chest = new float[frames + 1];
        var head = new float[frames + 1];
        float chest0 = 0f, head0 = 0f;
        for (int f = 0; f <= frames; f++)
        {
            clip.Sample(MathF.Min(f / 30f, clip.Duration), skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            if (!Lines(model, rig, out float c, out float h)) return ([], []);
            if (f == 0) (chest0, head0) = (c, h);
            chest[f] = DetMath.WrapAngle(c - chest0);
            head[f] = DetMath.WrapAngle(h - head0 - (c - chest0));
        }
        return (chest, head);
    }
}

/// <summary>
/// How a drawn person's trunk is turned bone by bone — what an upper torso that looks disconnected from the lower body, at the waist,
/// is measured by (the dump's <c>waist</c>, <c>neck</c> and <c>waistShift</c> columns, and the tests). Each bone's turn about the
/// vertical is its model-space turn from its rest (the twist about Z of now × rest⁻¹).
/// </summary>
public readonly record struct TrunkTurns(float Waist, float Neck, float Shift)
{
    /// <summary>
    /// The largest turn about the vertical between neighbours from the pelvis through <c>spine_01</c> to <c>_03</c> (<see cref="Waist"/>)
    /// and from <c>spine_03</c> through <c>neck_01</c> to the head (<see cref="Neck"/>), radians; and how far <c>spine_01</c>'s joint is
    /// from where the pelvis carries it (<see cref="Shift"/>, metres: the motions key translation on the pelvis only). Zeros when the rig
    /// lacks the bones. <paramref name="model"/>: the pose's bones in model space.
    /// </summary>
    public static TrunkTurns Of(Skeleton skeleton, Assets.People.PersonRig rig, ReadOnlySpan<Matrix4x4> model)
    {
        var spine = rig.Spine;
        int n = Math.Min(skeleton.Count, model.Length);
        if (spine.Length == 0 || (uint)rig.Pelvis >= (uint)n || (uint)rig.Neck >= (uint)n || (uint)rig.Head >= (uint)n) return default;
        for (int i = 0; i < spine.Length; i++) if ((uint)spine[i] >= (uint)n) return default;
        float waist = 0f, neck = 0f, before = Yaw(skeleton, model, rig.Pelvis);
        for (int i = 0; i < spine.Length; i++)
        {
            float y = Yaw(skeleton, model, spine[i]);
            waist = MathF.Max(waist, MathF.Abs(DetMath.WrapAngle(y - before)));
            before = y;
        }
        foreach (int b in (ReadOnlySpan<int>)[rig.Neck, rig.Head])
        {
            float y = Yaw(skeleton, model, b);
            neck = MathF.Max(neck, MathF.Abs(DetMath.WrapAngle(y - before)));
            before = y;
        }
        int first = spine[0], parent = skeleton.Bones[first].Parent;
        float shift = parent >= 0 && parent < n ? Vector3.Distance(PoseMath.Transform(skeleton.Bones[first].Rest.Translation, model[parent]), model[first].Translation) : 0f;
        return new TrunkTurns(waist, neck, shift);
    }

    /// <summary>Bone <paramref name="b"/>'s turn about the model's vertical from its rest (radians, toward the left positive).</summary>
    public static float Yaw(Skeleton skeleton, ReadOnlySpan<Matrix4x4> model, int b)
    {
        var rest = Quaternion.Identity;
        for (int i = b; i >= 0; i = skeleton.Bones[i].Parent) rest = PoseMath.Multiply(skeleton.Bones[i].Rest.Rotation, rest);
        var now = LimbIK.Normalize(Quaternion.CreateFromRotationMatrix(model[b]));
        var d = PoseMath.Multiply(now, LimbIK.Conjugate(rest));
        return DetMath.WrapAngle(2f * DetMath.Atan2(d.Z, d.W));
    }
}
