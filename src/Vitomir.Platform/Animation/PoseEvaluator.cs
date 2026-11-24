// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The bone chain and the skin matrices are taken from src/Vitomir.Engine/Anim/Skeleton.cs (Pose, Skin,
// SkinPosition) at b4185ef, reworked for the platform's types; the crossfade is ours.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// Turns a character's animation state into its pose: each bone's local transform (sampled from the
/// current clip, crossfaded from the previous one), each bone's model-space matrix (parents first:
/// <c>local × parent</c>, row vectors, as 1.1 multiplies them), and the skin matrices a renderer draws with and
/// hit volumes ride on (<c>inverse bind × posed bone</c>, 1.1's <c>RestMatrixInverse × world</c>). All of
/// it is <see cref="PoseMath"/>'s scalar arithmetic and <see cref="DetMath"/>, so the same state gives the
/// same bits on every machine.
/// <para>
/// <b>The crossfade is ours</b>: the previous motion keeps playing while its weight falls, and each bone is
/// blended by <see cref="DetMath.Slerp"/> of its rotation and a linear blend of its translation. 1.1's own
/// fade (a fifth of a second) blends Euler angles towards a pose frozen at the change instead.
/// At weight 1 the pose is the current motion's alone and at 0 the previous one's, bit for bit.
/// </para>
/// An instance owns scratch buffers, so it is for one thread.
/// </summary>
public sealed class PoseEvaluator
{
    BoneTransform[] local = new BoneTransform[64], other = new BoneTransform[64], action = new BoneTransform[64];

    /// <summary>
    /// The caller poses a person's swing bones itself after (<see cref="People.SwingRule.Pose"/>, the simulator's): the skirt rule is not
    /// laid on (<see cref="CharacterPoses"/> sets it for a person whose swing is simulated now).
    /// </summary>
    public bool Swung { get; set; }

    /// <summary>
    /// A person's hands put on the real top in a move over or onto a thing (<see cref="People.PersonHandsRule"/>), laid on after the
    /// legs (<see cref="CharacterPoses"/> sets it for the pose it makes; none, the default, the clip's own hands).
    /// </summary>
    public People.PersonHands Hands { get; set; }

    /// <summary>
    /// how far a person's braced feet are moved back from the wall (the model's space, m; 0 none, the default): stiffer shoes than the clips'
    /// soft ones (<see cref="People.PersonMovesSettings.ToeBendLimitDegrees"/> under 40), by the models repository's table; laid on after the hands.
    /// </summary>
    public float FootBack { get; set; }

    /// <summary>
    /// A person's root bone drawn without its turn about the vertical (a corner's or the leap back's: the body's facing carries it, BodyMotor.Hang.cs), as
    /// a turn on the spot's <see cref="People.TurnClip"/> draws its source; false the default.
    /// </summary>
    public bool StillRoot { get; set; }

    /// <summary>
    /// A person's limbs on a wall beyond its clip (<see cref="People.PersonWallRule"/>: onto the level's own holds, onto a face with nothing to hold, the
    /// splayed grip's fingers), laid on after the braced feet; none, the default.
    /// </summary>
    public People.PersonWallPose Wall { get; set; }

    /// <summary>A person's stealth stance (<see cref="People.PersonStealthRule"/>: the pose blended toward the crouch's), laid on before its legs' IK; none, the default.</summary>
    public People.PersonStancePose Stance { get; set; }

    /// <summary>The player's lean into turns, the head's look and the chest's lead toward the way (<see cref="People.PersonPresentRule"/>), or none.</summary>
    public People.PersonPresentPose Present { get; set; }

    /// <summary>
    /// Each bone's local transform for a character's state: its current clip (the rest pose with none), crossfaded from the
    /// previous one while <see cref="CharacterAnimation.Blend"/> is below 1; then the action channel's clip laid over
    /// it by the action's weight — on the upper body (<see cref="ActionSet.UpperBody"/>) fully, on the hips and legs by
    /// <see cref="CharacterAnimation.ActionLegs"/> as well (the whole body while it stands, the upper body while it moves).
    /// </summary>
    public void Local(CharacterAssets assets, in CharacterAnimation state, Span<BoneTransform> result) => Local(assets, state, result, 0f);

    /// <summary>
    /// As <see cref="Local(CharacterAssets, in CharacterAnimation, Span{BoneTransform})"/>, with a person's slope clip laid over each of its
    /// gait's clips by <paramref name="slope"/> (<see cref="People.PersonGround.Slope"/>; <see cref="People.PersonSlopes.Lay"/>): 0, as before.
    /// </summary>
    public void Local(CharacterAssets assets, in CharacterAnimation state, Span<BoneTransform> result, float slope) => Local(assets, state, result, slope, default);

    /// <summary>As the overload before, with a person's stair clip laid over its gait on a flight (<paramref name="flight"/>; <see cref="People.PersonStairs.Lay"/>).</summary>
    public void Local(CharacterAssets assets, in CharacterAnimation state, Span<BoneTransform> result, float slope, in People.PersonFlight flight)
    {
        var skeleton = assets.ModelOf(state.Kind).Skeleton;
        int n = skeleton.Count;
        var person = slope != 0f ? assets.Kind(state.Kind).Person : null;
        var climber = flight.Share > 0f ? assets.Kind(state.Kind).Person : null;
        if (state.Clip >= 0) assets.Clip(state.Clip).Sample(state.Time, skeleton, result[..n]);
        else Rest(skeleton, result[..n]);
        if (person is not null) People.PersonSlopes.Lay(assets, person, state.Clip, state.Time, slope, skeleton, result[..n]);
        if (climber is not null) People.PersonStairs.Lay(assets, climber, state.Clip, state.Time, flight, skeleton, result[..n]);
        if (state.PreviousClip >= 0 && state.Blend < 1f)
        {
            if (other.Length < n) other = new BoneTransform[n];
            var from = other.AsSpan(0, n);
            assets.Clip(state.PreviousClip).Sample(state.PreviousTime, skeleton, from);
            if (person is not null) People.PersonSlopes.Lay(assets, person, state.PreviousClip, state.PreviousTime, slope, skeleton, from);
            if (climber is not null) People.PersonStairs.Lay(assets, climber, state.PreviousClip, state.PreviousTime, flight, skeleton, from);
            Crossfade(from, result[..n], state.Blend, result[..n]);
        }
        if (action.Length < n) action = new BoneTransform[n];
        if (state.Action == ActionRole.None || state.ActionWeight <= 0f)
        {
            if (state.GuardWeight > 0f) Combat.GuardLayer.Over(assets, state, skeleton, result[..n], action.AsSpan(0, n));
            return;
        }
        var act = action.AsSpan(0, n);
        assets.Clip(state.ActionClip).Sample(state.ActionTime, skeleton, act);
        if (state.ActionFrom != ActionRole.None && state.ActionBlend < 1f)
        {
            if (other.Length < n) other = new BoneTransform[n];
            var from = other.AsSpan(0, n);
            assets.Clip(state.ActionFromClip).Sample(state.ActionFromTime, skeleton, from);
            Crossfade(from, act, state.ActionBlend, act);
        }
        var upper = assets.Kind(state.Kind).Actions?.UpperBody;
        float w = MathF.Min(1f, state.ActionWeight), legs = w * Math.Clamp(state.ActionLegs, 0f, 1f);
        for (int b = 0; b < n; b++)
        {
            float wb = upper is null || (b < upper.Length && upper[b]) ? w : legs;
            if (wb >= 1f) result[b] = act[b];
            else if (wb > 0f) result[b] = new BoneTransform(DetMath.Slerp(result[b].Rotation, act[b].Rotation, wb), PoseMath.Lerp(result[b].Translation, act[b].Translation, wb));
        }
        if (assets.CarryActionHips && upper is not null) ActionHips.Carry(skeleton, upper, result[..n], act, w, legs);
        if (state.GuardWeight > 0f) Combat.GuardLayer.Over(assets, state, skeleton, result[..n], action.AsSpan(0, n));   // the shield arm's layer
    }

    /// <summary>
    /// A character's pose: the model-space matrix of every bone into <paramref name="model"/> and its skin
    /// matrix into <paramref name="skin"/> (both at least the skeleton's bone count long).
    /// </summary>
    public void Evaluate(CharacterAssets assets, in CharacterAnimation state, Span<Matrix4x4> model, Span<Matrix4x4> skin) =>
        Evaluate(assets, state, default, model, skin);

    /// <summary>
    /// A character's pose with its limbs placed by its <paramref name="rig"/>: the motion and its fade, then the
    /// hips, feet and hands put where the rig says by IK (<see cref="Place"/>) when the kind has limbs.
    /// </summary>
    public void Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin) =>
        Evaluate(assets, state, rig, model, skin, default);

    /// <summary>
    /// A character's pose with its limbs placed and its upper body bent by the aim offset (<see cref="PoseRig.AimPitch"/>,
    /// <see cref="AimOffset"/>) after the limbs; when it is bent and <paramref name="unbent"/> is long enough, the bones as they
    /// were before the bend go there (the first-person eye rides the unbent head), and the answer is true.
    /// </summary>
    public bool Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin, Span<Matrix4x4> unbent) =>
        Evaluate(assets, state, rig, model, skin, unbent, default);

    /// <summary>
    /// As <see cref="Evaluate(CharacterAssets, in CharacterAnimation, in PoseRig, Span{Matrix4x4}, Span{Matrix4x4}, Span{Matrix4x4})"/>, with a
    /// person's turn as a chain (<paramref name="chain"/>; <see cref="People.TurnChain"/>) when it is on: its hips, chest and head
    /// turned in place of the earlier build's legs' turn.
    /// </summary>
    public bool Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin, Span<Matrix4x4> unbent,
                         in People.PersonTurn chain) => Evaluate(assets, state, rig, model, skin, unbent, chain, default);

    /// <summary>
    /// As the overload before, with a person's planted feet, heel lift and bend helpers (<paramref name="feet"/>,
    /// <see cref="People.PersonFeet"/>) when they are on: the feet laid on in the legs' IK (<see cref="Place"/>), then the bend rule.
    /// </summary>
    public bool Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin, Span<Matrix4x4> unbent,
                         in People.PersonTurn chain, in People.PersonFeet feet) => Evaluate(assets, state, rig, model, skin, unbent, chain, feet, default);

    /// <summary>
    /// As the overload before, with a person on uneven ground (<paramref name="ground"/>, <see cref="People.PersonGround"/>): its slope clip
    /// laid over the gait (<see cref="Local(CharacterAssets, in CharacterAnimation, Span{BoneTransform}, float)"/>); the line and the hips are the
    /// rig's (<see cref="PoseRig.RootOffset"/>).
    /// </summary>
    public bool Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin, Span<Matrix4x4> unbent,
                         in People.PersonTurn chain, in People.PersonFeet feet, in People.PersonGround ground) => Evaluate(assets, state, rig, model, skin, unbent, chain, feet, ground, default);

    /// <summary>As the overload before, with a person on a flight of stairs (<paramref name="flight"/>, <see cref="People.PersonFlight"/>): its stair clip laid over the gait.</summary>
    public bool Evaluate(CharacterAssets assets, in CharacterAnimation state, in PoseRig rig, Span<Matrix4x4> model, Span<Matrix4x4> skin, Span<Matrix4x4> unbent,
                         in People.PersonTurn chain, in People.PersonFeet feet, in People.PersonGround ground, in People.PersonFlight flight)
    {
        var skeleton = assets.ModelOf(state.Kind).Skeleton;
        int n = skeleton.Count;
        if (local.Length < n) local = new BoneTransform[n];
        var l = local.AsSpan(0, n);
        // A ground state's join inertialized: the clip taken whole, the clip left's difference decayed on it (People.PersonJoinRule).
        bool joined = Present.Join.On && assets.Kind(state.Kind).Person is not null;
        if (joined)
        {
            var taken = state;
            taken.PreviousClip = -1;
            taken.Blend = 1f;
            Local(assets, taken, l, ground.Slope, flight);
            People.PersonJoinRule.Apply(assets, skeleton, Present.Join, Present.Carry, l);
        }
        else Local(assets, state, l, ground.Slope, flight);
        if (Stance.On) People.PersonStealthRule.Lay(assets, skeleton, Stance, l);   // stealth's stance, before the legs' IK keeps the feet down
        var kind = assets.Kind(state.Kind);
        if (StillRoot && kind.Person is { Root: >= 0 } still && still.Root < n) l[still.Root] = l[still.Root] with { Rotation = People.TurnClip.Swing(l[still.Root].Rotation) };
        // The legs' turn (LegTurn) laid on before the limbs are placed, so the feet's IK keeps to the turned legs.
        if (rig.LegYaw != 0f && kind.Limbs is { } turned && kind.Actions is { Spine.Length: > 0 } acts) LegTurn.Apply(skeleton, turned.Root, acts.Spine, rig.LegYaw, l, model[..n]);
        // A person's legs turned about its root bone (between the feet: a turn on the spot's pivot), its spine back.
        else if (kind.Person is { } person && chain.On) People.TurnChain.Apply(skeleton, assets, state, person, rig.LegYaw, chain, l, model[..n]);   // the chain
        else if (rig.LegYaw != 0f && kind.Person is { } personRig) LegTurn.Apply(skeleton, personRig.Root, personRig.Spine, rig.LegYaw, l, model[..n]);
        if (Present.On && kind.Person is { } presented) People.PersonPresentRule.Turn(skeleton, presented.Rig, Present, l);   // the look and the head's lead
        if (rig.Active && kind.Limbs is { } limbs) Place(skeleton, limbs, rig, l, model[..n], feet, Present);
        if (feet.On && feet.Bend) kind.Person?.Bend?.Apply(l);   // the bend helpers, after the legs' IK
        if (Hands.On && kind.Limbs is { } armed) People.PersonHandsRule.Apply(skeleton, armed, Hands, l, model[..n]);   // the hands on the real top
        if (FootBack > 0f && kind.Limbs is { } legged)   // the braced feet back from the wall for stiffer shoes
        {
            ToModel(skeleton, l, model[..n]);
            var back = new Vector3(0f, FootBack, 0f);
            var knee = LegTurn.Knee(0f);
            if (legged.LeftLeg.Has) LimbIK.Solve(skeleton, l, model[..n], legged.LeftLeg, model[legged.LeftLeg.End].Translation - back, 1f, knee);
            if (legged.RightLeg.Has) LimbIK.Solve(skeleton, l, model[..n], legged.RightLeg, model[legged.RightLeg.End].Translation - back, 1f, knee);
        }
        if (Wall.On && kind.Limbs is { } climbing) People.PersonWallRule.Apply(assets, skeleton, climbing, Wall, l, model[..n]);   // the limbs on the wall's holds
        if (state.GuardSide != 0f || state.GuardHeight != 0f) Combat.GuardLayer.Aim(skeleton, kind, state, l, model[..n]);   // the guard's stance
        bool bent = (rig.AimPitch != 0f || state.GuardTwist != 0f) && kind.Actions is { Spine.Length: > 0 };   // or turned by the guard
        if (bent)
        {
            if (unbent.Length >= n) ToModel(skeleton, l, unbent);
            AimOffset.Apply(skeleton, kind.Actions!.Spine, rig.AimPitch, l);
            Combat.GuardLayer.Twist(skeleton, kind.Actions!.Spine, state.GuardTwist, l);   // the guard's turn toward a threat
        }
        if (GuardSolver.Wants(state, rig)) GuardSolver.Pose(skeleton, kind, state, rig, l, model[..n]);   // the guard's arm solver and give
        if (state.RecoilBack != Vector3.Zero) Recoil.Apply(skeleton, kind, state, l, model[..n]);   // the arm kicked back off a blocked blow   // (the recoil after the solver: a kick on the arm where it is)
        if (state.StaggerTurn != 0f) Combat.StaggerPush.Apply(skeleton, kind, state, l);   // the upper body pushed by a stagger
        kind.Person?.Twist?.Apply(l);   // a person's twist bones from its limbs, after everything that turns them
        if (!assets.PersonSkirtSplit && !Swung) kind.Person?.Skirt?.Apply(l);   // a skirt hung from the thighs carried by both (where it is not simulated)
        ToModel(skeleton, l, model);
        ToSkin(skeleton, model, skin);
        return bent && unbent.Length >= n;
    }

    /// <summary>
    /// Puts a pose's limbs where <paramref name="rig"/> says (ours), changing <paramref name="local"/>
    /// (<paramref name="model"/> is scratch, left as the pose before the arms were placed): the ankles' targets are
    /// where the motion has them, raised by each foot's lift (or a point a foot is put at); then the hips are moved and
    /// leaned; when the hands hold on and the body is pulled, the hips are moved towards the hands until the arms reach
    /// them; then each leg and each arm is placed by <see cref="LimbIK.Solve"/> — the feet keep the motion's turn, the
    /// hands are turned so the knuckles meet their targets with the wrist out from the face and towards the shoulder.
    /// A person's <paramref name="feet"/>, when on, laid on the legs (<see cref="People.FootPlacement"/>): each ball held at its
    /// lock, the foot swivelled on it and its heel lifted, the hips raised by a share of the lift, the legs' soft limit; the toes kept on
    /// the ground after.
    /// </summary>
    public static void Place(Skeleton skeleton, LimbMap limbs, in PoseRig rig, Span<BoneTransform> local, Span<Matrix4x4> model) =>
        Place(skeleton, limbs, rig, local, model, default);

    /// <inheritdoc cref="Place(Skeleton, LimbMap, in PoseRig, Span{BoneTransform}, Span{Matrix4x4})"/>
    public static void Place(Skeleton skeleton, LimbMap limbs, in PoseRig rig, Span<BoneTransform> local, Span<Matrix4x4> model, in People.PersonFeet feet) =>
        Place(skeleton, limbs, rig, local, model, feet, default);

    /// <summary>As the overload before, with the lean into a turn (<paramref name="present"/>): the hips turned about the feet before the legs' IK keeps the feet.</summary>
    public static void Place(Skeleton skeleton, LimbMap limbs, in PoseRig rig, Span<BoneTransform> local, Span<Matrix4x4> model, in People.PersonFeet feet,
                             in People.PersonPresentPose present)
    {
        ToModel(skeleton, local, model);
        var up = Vector3.UnitZ;
        // On a slope the ankle stands its height off the ground along the slope's normal: a little higher above it.
        var keepLeft = limbs.LeftLeg.Has ? Planted(model[limbs.LeftLeg.End].Translation, limbs, rig.GroundWeight) + up * (rig.LeftLift + Raise(limbs, rig.LeftSlope, rig.GroundWeight)) : default;
        var keepRight = limbs.RightLeg.Has ? Planted(model[limbs.RightLeg.End].Translation, limbs, rig.GroundWeight) + up * (rig.RightLift + Raise(limbs, rig.RightSlope, rig.GroundWeight)) : default;
        Quaternion? endLeft = Tilt(rig.LeftSlope, rig.GroundWeight), endRight = Tilt(rig.RightSlope, rig.GroundWeight);
        Quaternion raiseLeft = Quaternion.Identity, raiseRight = Quaternion.Identity;
        float rise = 0f;
        if (feet.On)
        {
            // The planted feet, swivelled on their balls, the heels lifted.
            float riseLeft, riseRight;
            (keepLeft, endLeft, raiseLeft, riseLeft) = People.FootPlacement.Leg(skeleton, local, limbs.LeftLeg, keepLeft, endLeft, feet.LeftBall, feet.LeftTurn, feet.LeftHold, feet.Lift, feet.StandPitch);
            (keepRight, endRight, raiseRight, riseRight) = People.FootPlacement.Leg(skeleton, local, limbs.RightLeg, keepRight, endRight, feet.RightBall, feet.RightTurn, feet.RightHold, feet.Lift, feet.StandPitch);
            rise = (riseLeft + riseRight) * 0.5f * feet.LiftHips;
        }

        var root = local[limbs.Root];
        var rotation = rig.Lean != 0f ? LimbIK.Normalize(PoseMath.Multiply(LimbIK.Pitch(rig.Lean), root.Rotation)) : root.Rotation;
        local[limbs.Root] = new BoneTransform(rotation, rise != 0f ? root.Translation + rig.RootOffset + up * rise : root.Translation + rig.RootOffset);
        ToModel(skeleton, local, model);
        if (present.Roll != 0f)
        {
            People.PersonPresentRule.Roll(skeleton, limbs.Root, present, local, model);   // the lean into a turn, about the feet
            ToModel(skeleton, local, model);
        }

        bool arms = rig.HandWeight > 0f && limbs.LeftArm.Has && limbs.RightArm.Has;
        if (arms && rig.Pull > 0f)
            for (int pass = 0; pass < 2; pass++)
            {
                var (shift, more) = Shortfall(model, local, limbs.LeftArm, rig.LeftHand, rig);
                var (shiftR, moreR) = Shortfall(model, local, limbs.RightArm, rig.RightHand, rig);
                if (moreR > more) { shift = shiftR; more = moreR; }
                if (more <= 0f) break;
                var r = local[limbs.Root];
                local[limbs.Root] = r with { Translation = r.Translation + shift * rig.Pull };
                ToModel(skeleton, local, model);
            }

        var knee = LegTurn.Knee(rig.LegYaw);      // the knees forward along the turned legs
        float legLeft = MathF.Max(rig.LegWeight, rig.LeftFootWeight), legRight = MathF.Max(rig.LegWeight, rig.RightFootWeight);
        var targetLeft = PoseMath.Lerp(keepLeft, rig.LeftFoot, rig.LeftFootWeight);
        var targetRight = PoseMath.Lerp(keepRight, rig.RightFoot, rig.RightFootWeight);
        if (feet.On)
        {
            // A held or lifted foot reached for with the legs' soft limit, by how much it is held (a gait's own feet as before).
            targetLeft = People.FootPlacement.Soft(model, limbs.LeftLeg, targetLeft, feet.Soft, feet.Lift > 0f ? 1f : feet.LeftHold);
            targetRight = People.FootPlacement.Soft(model, limbs.RightLeg, targetRight, feet.Soft, feet.Lift > 0f ? 1f : feet.RightHold);
        }
        LimbIK.Solve(skeleton, local, model, limbs.LeftLeg, targetLeft, legLeft, knee, turnEnd: endLeft);
        LimbIK.Solve(skeleton, local, model, limbs.RightLeg, targetRight, legRight, knee, turnEnd: endRight);
        if (feet.On)
        {
            People.FootPlacement.Toes(skeleton, local, limbs.LeftLeg, raiseLeft);   // the toes left on the ground under a lifted heel
            People.FootPlacement.Toes(skeleton, local, limbs.RightLeg, raiseRight);
        }
        if (arms)
        {
            LimbIK.Solve(skeleton, local, model, limbs.LeftArm, Wrist(model, local, limbs.LeftArm, rig.LeftHand, rig.HandNormal), rig.HandWeight,
                         new Vector3(-0.5f, -0.5f, -0.7f), rig.LeftHand);
            LimbIK.Solve(skeleton, local, model, limbs.RightArm, Wrist(model, local, limbs.RightArm, rig.RightHand, rig.HandNormal), rig.HandWeight,
                         new Vector3(0.5f, -0.5f, -0.7f), rig.RightHand);
        }
    }

    static float Raise(LimbMap limbs, Vector3 normal, float ground) =>
        ground <= 0f || normal.Z <= 0f || normal.Z >= 0.9999f ? 0f : limbs.AnkleHeight * (1f / normal.Z - 1f) * MathF.Min(ground, 1f);

    /// <summary>The turn that lays a foot on ground of this normal (model space), by how much the feet are on the ground; null on level ground.</summary>
    static Quaternion? Tilt(Vector3 normal, float ground)
    {
        if (ground <= 0f || normal == Vector3.Zero || normal.Z >= 0.9999f) return null;
        var full = LimbIK.FromTo(Vector3.UnitZ, normal);
        return ground >= 1f ? full : DetMath.Slerp(Quaternion.Identity, full, ground);
    }

    /// <summary>
    /// An ankle the motion has on or near the ground put at the kind's standing height on it: fully when it is no
    /// higher than that, less as it rises, not at all from 0.1 m above it (a foot in its stride's swing keeps its
    /// height), all by how much the feet are on the ground. So a pose made for a sill or a wider stance plants its feet.
    /// </summary>
    static Vector3 Planted(Vector3 ankle, LimbMap limbs, float ground)
    {
        if (ground <= 0f) return ankle;
        float above = ankle.Z - limbs.AnkleHeight;
        float w = above <= 0f ? 1f : above >= 0.1f || limbs.RaiseOnly ? 0f : 1f - above / 0.1f;   // RaiseOnly, the people's
        return new Vector3(ankle.X, ankle.Y, ankle.Z + (limbs.AnkleHeight - ankle.Z) * w * MathF.Min(ground, 1f));
    }

    /// <summary>Where the wrist goes for the knuckles to be at <paramref name="knuckles"/>: a hand's length from them, out from the face and towards the shoulder.</summary>
    static Vector3 Wrist(ReadOnlySpan<Matrix4x4> model, ReadOnlySpan<BoneTransform> local, in Limb arm, Vector3 knuckles, Vector3 normal)
    {
        float hand = arm.Tip >= 0 ? LimbIK.Length(local[arm.Tip].Translation) : 0f;
        var toShoulder = LimbIK.Unit(model[arm.Upper].Translation - knuckles);
        return knuckles + LimbIK.Unit(toShoulder + normal * 0.5f) * hand;
    }

    /// <summary>How far the hips must move for an arm to reach its hand's target (a shortfall of 0 when it reaches), and which way.</summary>
    static (Vector3 Shift, float Short) Shortfall(ReadOnlySpan<Matrix4x4> model, ReadOnlySpan<BoneTransform> local, in Limb arm, Vector3 knuckles, in PoseRig rig)
    {
        var shoulder = model[arm.Upper].Translation;
        var wrist = Wrist(model, local, arm, knuckles, rig.HandNormal);
        var target = PoseMath.Lerp(model[arm.End].Translation, wrist, MathF.Min(1f, rig.HandWeight));
        var d = target - shoulder;
        float l = LimbIK.Length(d);
        float more = l - LimbIK.Reach(model, arm) * 0.97f;
        return more > 0f && l > 0f ? (d / l * more, more) : (Vector3.Zero, 0f);
    }

    /// <summary>The rest pose (the one the model's vertices are authored in): every bone's <see cref="BoneInfo.Rest"/>.</summary>
    public static void Rest(Skeleton skeleton, Span<BoneTransform> result)
    {
        var bones = skeleton.Bones;
        for (int b = 0; b < bones.Length && b < result.Length; b++) result[b] = bones[b].Rest;
    }

    /// <summary>
    /// <paramref name="from"/> blended towards <paramref name="to"/> by <paramref name="weight"/> (0 gives
    /// <paramref name="from"/>, 1 gives <paramref name="to"/>, exactly): rotations by <see cref="DetMath.Slerp"/>,
    /// translations linearly. <paramref name="result"/> may be either input.
    /// </summary>
    public static void Crossfade(ReadOnlySpan<BoneTransform> from, ReadOnlySpan<BoneTransform> to, float weight, Span<BoneTransform> result)
    {
        int n = Math.Min(result.Length, Math.Min(from.Length, to.Length));
        if (weight >= 1f) { to[..n].CopyTo(result); return; }
        if (weight <= 0f) { from[..n].CopyTo(result); return; }
        for (int b = 0; b < n; b++)
            result[b] = new BoneTransform(DetMath.Slerp(from[b].Rotation, to[b].Rotation, weight),
                                          PoseMath.Lerp(from[b].Translation, to[b].Translation, weight));
    }

    /// <summary>Each bone's model-space matrix from its local transform, parents first (a bone whose parent does not precede it is a root).</summary>
    public static void ToModel(Skeleton skeleton, ReadOnlySpan<BoneTransform> local, Span<Matrix4x4> model)
    {
        var bones = skeleton.Bones;
        for (int b = 0; b < bones.Length; b++)
        {
            var m = PoseMath.ToMatrix(local[b]);
            int parent = bones[b].Parent;
            model[b] = parent >= 0 && parent < b ? PoseMath.Multiply(m, model[parent]) : m;
        }
    }

    /// <summary>The skin matrices: <c>inverse bind × posed bone</c> (rest-pose model space to the posed model space).</summary>
    public static void ToSkin(Skeleton skeleton, ReadOnlySpan<Matrix4x4> model, Span<Matrix4x4> skin)
    {
        var inverse = skeleton.InverseBind;
        for (int b = 0; b < inverse.Length; b++) skin[b] = PoseMath.Multiply(inverse[b], model[b]);
    }

    /// <summary>A vertex's posed position: the weighted sum of its bones' skin matrices (a 1.1 vertex has two, its bone and that bone's parent).</summary>
    public static Vector3 SkinPosition(in SkinVertex v, ReadOnlySpan<Matrix4x4> skin)
    {
        var w = v.Weights;
        var p = PoseMath.Transform(v.Position, skin[v.Bone(0)]) * w.X;
        if (w.Y != 0f) p += PoseMath.Transform(v.Position, skin[v.Bone(1)]) * w.Y;
        if (w.Z != 0f) p += PoseMath.Transform(v.Position, skin[v.Bone(2)]) * w.Z;
        if (w.W != 0f) p += PoseMath.Transform(v.Position, skin[v.Bone(3)]) * w.W;
        return p;
    }

    /// <summary>A vertex's posed normal as 1.1 lights it: turned by its own (first) bone alone (the old engine's <c>PoseNormals</c>).</summary>
    public static Vector3 SkinNormalAsOneOne(in SkinVertex v, ReadOnlySpan<Matrix4x4> skin) =>
        PoseMath.TransformNormal(v.Normal, skin[v.Bone(0)]);
}
