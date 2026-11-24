// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1 has no guard or parry motions (the combat survey), and the
// real guard motions are not made: the guarding arm is placed by IK across the incoming blow's path.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The guard's arm solver laid on a pose (<i>"the engine moves the guarding arm
/// itself"</i>). Called by <see cref="PoseEvaluator"/> (and <see cref="SwingArcCore"/>, in the same place) after the aim's bend
/// and the guard's twist: the weapon arm placed by two-bone IK (<see cref="LimbIK.Solve"/>, shoulder, elbow, wrist) so the
/// guarding blade's guard is at <see cref="PoseRig.GuardHand"/> and the blade lies along <see cref="PoseRig.GuardBlade"/> (the
/// hand turned), by <see cref="PoseRig.GuardHandWeight"/> times the block's own weight on the channel (and, as an attack begins
/// from the guard, the share the block still has); and the <b>give</b> (<see cref="PoseRig.GuardGive"/>) added to the guarding
/// hand — the shield arm's wrist when a blow met the shield. A pure function of the state; <see cref="PoseMath"/>,
/// <see cref="DetMath"/> and <see cref="LimbIK"/> only.
/// </summary>
public static class GuardSolver
{
    /// <summary>Whether anything is laid on the pose (the solver's weight or a give): else the pose is the earlier build's, bit for bit.</summary>
    public static bool Wants(in CharacterAnimation state, in PoseRig rig) => rig.GuardHandWeight > 0f || rig.GuardGive != Vector3.Zero;

    /// <summary>The solver's weight on the pose: its own times the block's on the action channel (the share it keeps as an attack fades in from it).</summary>
    public static float HandWeight(in CharacterAnimation state, in PoseRig rig)
    {
        if (rig.GuardHandWeight <= 0f) return 0f;
        float channel = state.Action == ActionRole.Block ? MathF.Min(1f, state.ActionWeight)
            : state.ActionFrom == ActionRole.Block && state.ActionBlend < 1f ? (1f - Math.Clamp(state.ActionBlend, 0f, 1f)) * MathF.Min(1f, state.ActionWeight) : 0f;
        return MathF.Min(1f, rig.GuardHandWeight) * channel;
    }

    /// <summary>
    /// Lays the solver and the give on <paramref name="local"/> (<paramref name="model"/> is scratch, made from it here): see the
    /// type. The weapon arm's wrist goes to the blended target exactly (IK at weight 1): the composed pose's wrist moved by the
    /// solver's weight toward where the guard wants it, plus the give.
    /// </summary>
    public static void Pose(Skeleton skeleton, CharacterKind kind, in CharacterAnimation state, in PoseRig rig, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (kind.Limbs is not { } limbs) return;
        float w = HandWeight(state, rig);
        bool give = rig.GuardGive != Vector3.Zero;
        bool giveShield = give && rig.GuardGiveShield;
        bool giveBlade = give && !rig.GuardGiveShield && !ActionRoles.IsAttack(state.Action);
        if (w <= 0f && !giveShield && !giveBlade) return;
        if (w > 0f && rig.GuardSolveTwist != 0f && kind.Actions is { Spine.Length: > 0 } turned)
            GuardLayer.Twist(skeleton, turned.Spine, rig.GuardSolveTwist * w, local);   // the sword's shoulder toward the intercept
        PoseEvaluator.ToModel(skeleton, local, model);
        if ((w > 0f || giveBlade) && WeaponArm(skeleton, kind, limbs, out var arm, out float side))
        {
            var wrist = model[arm.End].Translation;
            var target = wrist;
            Quaternion? turn = null;
            if (w > 0f && FirstBlade(kind, skeleton.Count, out var t))
            {
                var axis = LimbIK.Unit(PoseMath.TransformNormal(t.Tip - t.Guard, model[t.Bone]));
                var guard = PoseMath.Transform(t.Guard, model[t.Bone]);
                var want = LimbIK.Unit(rig.GuardBlade);
                if (axis != Vector3.Zero && want != Vector3.Zero)
                {
                    var full = LimbIK.FromTo(axis, want);
                    var q = w >= 1f ? full : DetMath.Slerp(Quaternion.Identity, full, w);
                    turn = q;
                    var offset = LimbIK.Rotate(q, guard - wrist);   // the blade's guard from the wrist, turned with the hand
                    target = wrist + (rig.GuardHand - offset - wrist) * w;
                }
            }
            if (giveBlade) target += rig.GuardGive;
            LimbIK.Solve(skeleton, local, model, arm, target, 1f, new Vector3(0.6f * side, -0.2f, -0.8f), turnEnd: turn);
        }
        if (giveShield && ShieldArm(skeleton, kind, limbs, out var shieldArm, out float shieldSide))
            LimbIK.Solve(skeleton, local, model, shieldArm, model[shieldArm.End].Translation + rig.GuardGive, 1f, new Vector3(0.6f * shieldSide, -0.2f, -0.8f));
    }

    /// <summary>The arm whose chain holds the weapon hand (<see cref="ActionSet.WeaponHand"/>), and its side (+1 the body's right, −1 its left).</summary>
    public static bool WeaponArm(Skeleton skeleton, CharacterKind kind, LimbMap limbs, out Limb arm, out float side)
    {
        arm = Limb.None;
        side = 1f;
        if (kind.Actions is not { WeaponHand: >= 0 } set) return false;
        if (limbs.RightArm.Has && Under(skeleton, set.WeaponHand, limbs.RightArm.Upper)) { arm = limbs.RightArm; side = 1f; return true; }
        if (limbs.LeftArm.Has && Under(skeleton, set.WeaponHand, limbs.LeftArm.Upper)) { arm = limbs.LeftArm; side = -1f; return true; }
        return false;
    }

    /// <summary>The arm whose chain holds the first shield, and its side; false for a kind with no shield.</summary>
    public static bool ShieldArm(Skeleton skeleton, CharacterKind kind, LimbMap limbs, out Limb arm, out float side)
    {
        arm = Limb.None;
        side = -1f;
        int bone = -1;
        var held = kind.Held;
        for (int m = 0; m < held.Count && bone < 0; m++)
            for (int t = 0; t < held[m].Things.Count && bone < 0; t++)
                if (held[m].Things[t].Kind == HeldKind.Shield) bone = held[m].Things[t].Bone;
        if (bone < 0) return false;
        if (limbs.LeftArm.Has && Under(skeleton, bone, limbs.LeftArm.Upper)) { arm = limbs.LeftArm; side = -1f; return true; }
        if (limbs.RightArm.Has && Under(skeleton, bone, limbs.RightArm.Upper)) { arm = limbs.RightArm; side = 1f; return true; }
        return false;
    }

    /// <summary>The kind's first held blade (as contact takes it: <see cref="HeldItems.Blade"/>).</summary>
    public static bool FirstBlade(CharacterKind kind, int bones, out HeldThing thing)
    {
        thing = null!;
        var held = kind.Held;
        for (int m = 0; m < held.Count; m++)
            for (int t = 0; t < held[m].Things.Count; t++)
            {
                var x = held[m].Things[t];
                if (x.Kind == HeldKind.Blade && (uint)x.Bone < (uint)bones) { thing = x; return true; }
            }
        return false;
    }

    /// <summary>Whether bone <paramref name="b"/> is <paramref name="top"/> or under it.</summary>
    static bool Under(Skeleton skeleton, int b, int top)
    {
        var bones = skeleton.Bones;
        for (int i = b, n = 0; i >= 0 && n < bones.Length; i = bones[i].Parent, n++)
        {
            if (i == top) return true;
            if (bones[i].Parent >= i) return false;
        }
        return false;
    }
}

/// <summary>The solver began to follow a blow: whose guard, whose blow (and its number), when, and how long before its blade arrives at the intercept chosen (s; negative: the hand cannot get there in time).</summary>
public readonly record struct GuardSolverFollows(EntityId Who, EntityId Threat, int Swing, long Tick, float Lead, float Margin);

/// <summary>The defender's guard gave under a blow: whose, by whose blow, the contact, the arm (shield or blade), how far at most (m) and the verdict.</summary>
public readonly record struct GuardGave(EntityId Who, EntityId By, long Tick, long ContactId, bool Shield, float Metres, GuardVerdict Verdict);

/// <summary>
/// The guard's arm solver (<see cref="AnimationRegistration.PosePhase"/>, after the rig): for every
/// fighter whose lone blade's guard is raised (the block on the channel, no shield) against a blow it <b>sees</b> — the nearest
/// threat in its guard's arc, as before guard picks it (<see cref="GuardAim.Sees"/>) — the blow's blade is predicted
/// through its active window (<see cref="SwingArcCore"/>, the attacker's clip at its aim, placed where he stands now), and the
/// <b>intercept</b> chosen: the place on the blow's path, in front of the defender and outside his body, where its blade will pass
/// soonest after now with the guarding blade able to be there first — its line <b>across</b> the path (along the normal of the
/// surface the incoming blade sweeps, so it is met whatever the timing), its guard within the arm's reach and the shoulder's range,
/// the hand moving at most <see cref="GuardSettings.SolverHandMetresPerSecond"/> and the blade turning at most
/// <see cref="GuardSettings.SolverBladeDegreesPerSecond"/> (a parry window's faster numbers while it is open). When none can be
/// reached in time, the one it is least late for. The hand then moves toward it at that speed. No threat, or an unseen one: the
/// solved guard fades back into the composed one. And each tick the give springs back (critically damped).
/// <para>
/// Deterministic: inputs are the tick's digested state; the blow's path is a memo of the attacker's kind, clip, window and aim (to
/// a degree) — a pure function of its key, made the first time it is asked for — and the rest is arithmetic on it. The solver's
/// state is <see cref="PoseRig"/>'s (digested). Nothing allocated per tick once the memo and each kind's guard are made.
/// </para>
/// </summary>
public sealed class GuardSolverSystem : ISystem
{
    public const string Name = "platform.guard-solver";

    /// <summary>Its place after the animation (<see cref="AnimationRegistration.AnimationOrder"/> plus this): after the rig (+10).</summary>
    public const int Order = 20;

    /// <summary>The solver's own chest turns tried for each crossing: evenly from its most one way to its most the other.</summary>
    const int Twists = 5;

    readonly SwingArcCore core = new();
    readonly BlowPaths paths = new();
    readonly Dictionary<int, KindGuard?> kinds = [];
    readonly PoseEvaluator poser = new();
    Matrix4x4[] model = new Matrix4x4[64], skin = new Matrix4x4[64];
    Vector3[] g = new Vector3[BlowPaths.Places], t = new Vector3[BlowPaths.Places];
    int rigsKnown;

    /// <summary>How long the last <see cref="Run"/> took on the solving, all bodies (s; a stopwatch: never read by the simulation).</summary>
    public double LastSeconds { get; private set; }

    /// <summary>How many guards it solved on the last tick (for the cost's measure).</summary>
    public int LastSolved { get; private set; }

    /// <summary>How many blows' paths its memo has made (each a few milliseconds' posing, once).</summary>
    public int PathsMade => paths.Made;

    /// <summary>A kind's guard, measured once in its block's raised frame (model space): the weapon arm's shoulder, the spine's foot, the arm's reach, the blade's length, its guard from the wrist and its way.</summary>
    sealed record KindGuard(Limb Arm, float Side, Vector3 Shoulder, Vector3 Hips, float Reach, float Length, Vector3 Offset, Vector3 Axis);

    public void Run(World world)
    {
        LastSolved = 0;
        if (!world.TryService<GuardSettings>(out var gs) || !gs.Enabled || !gs.Solver) return;
        if (!world.TryService<CharacterAssets>(out var assets)) return;
        if (!world.TryService<ActiveWindows>(out var windows) || !world.TryService<ContactConfig>(out var config)) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        var rigs = PoseRigSystem.Optional<PoseRig>(world, ref rigsKnown);
        if (rigs is null) return;
        var fighters = world.Store<Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var bodies = world.Store<Body>();
        world.TryService<AimOffsetSettings>(out var aimSettings);
        aimSettings ??= DefaultAim;
        float dt = World.TickSeconds;
        float omega = DetMath.TwoPi * MathF.Max(0.01f, gs.GiveHertz), decay = DetMath.Exp(-omega * dt);
        bool changed = false;
        var ents = fighters.Entities;
        var data = fighters.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var rig = ref rigs.Find(e);
            ref var anim = ref anims.Find(e);
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref rig) || Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) continue;
            // The give springs back: one exact step of a critically damped spring.
            if (rig.GuardGive != Vector3.Zero || rig.GuardGiveVelocity != Vector3.Zero)
            {
                var x = rig.GuardGive;
                var v = rig.GuardGiveVelocity;
                var b = v + x * omega;
                rig.GuardGive = (x + b * dt) * decay;
                rig.GuardGiveVelocity = (v - b * (omega * dt)) * decay;
                if (LimbIK.Length(rig.GuardGive) < 1e-4f && LimbIK.Length(rig.GuardGiveVelocity) < 1e-3f) { rig.GuardGive = Vector3.Zero; rig.GuardGiveVelocity = Vector3.Zero; }
                changed = true;
            }
            ref var c = ref data[i];
            float before = rig.GuardHandWeight;
            bool solved = c.Blocking && anim.Action == ActionRole.Block && !anim.ActionEnding
                && Solve(world, assets, windows, config, gs, aimSettings, e, ref c, in anim, in body, ref rig, rigs);
            if (!solved && rig.GuardHandWeight > 0f)
            {
                rig.GuardHandWeight = MathF.Max(0f, rig.GuardHandWeight - dt / MathF.Max(1e-3f, gs.SolverReturnSeconds));
                if (rig.GuardHandWeight <= 0f) { rig.GuardSwing = 0; rig.GuardThreat = -1; }
            }
            if (solved) LastSolved++;
            changed |= solved || before != rig.GuardHandWeight;
        }
        if (changed && world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
        LastSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
    }

    static readonly AimOffsetSettings DefaultAim = new();

    /// <summary>One guard's tick: false when there is nothing to solve (no threat seen, no blade, no path).</summary>
    bool Solve(World world, CharacterAssets assets, ActiveWindows windows, ContactConfig config, GuardSettings gs, AimOffsetSettings aimSettings,
        EntityId e, ref Combatant c, in CharacterAnimation anim, in Body body, ref PoseRig rig, Store<PoseRig> rigs)
    {
        var kind = assets.Kind(anim.Kind);
        if (GuardLayer.ArmOf(assets, anim.Kind) is not null) return false;   // a shield is the guard: its arm keeps the block
        if (Guard(assets, anim.Kind) is not { } kg) return false;
        if (!Nearest(world, e, in body, gs, out var f, out int fi)) return false;
        var fighters = world.Store<Combatant>();
        ref var fa = ref world.Store<CharacterAnimation>().Find(f);
        ref var fb = ref world.Store<Body>().Find(f);
        var fc = fighters.Data[fi];
        var fk = assets.Kind(fa.Kind);
        var window = windows.For(assets, fa.Kind, fa.ActionClip, fc.StrikeAt, config.Window);
        if (!window.Has || fa.ActionTime >= window.To) return false;
        // Read only once the blow has been under way a moment (its own time): before that the guard is as it was.
        var skill = GuardSkills.Of(world, e);   // the guard by skill; absent, the earlier build's (every factor 1)
        if (fa.ActionTime < gs.SolverReadSeconds * skill.Read * MathF.Max(1e-3f, fa.ActionRate) && !(rig.GuardThreat == f.Index && rig.GuardSwing == fc.Swing)) return false;
        float aim = 0f;
        ref var frig = ref rigs.Find(f);
        if (!Unsafe.IsNullRef(ref frig)) aim = AimOffset.Clamp(frig.AimTarget, aimSettings);
        int aimDegrees = (int)MathF.Round(aim * DetMath.RadToDeg);
        int path = paths.Get(assets, core, config.HandRadius, fa.Kind, fa.Action, fa.ActionClip, window, aimDegrees, gs.SolverSampleSeconds);
        int count = paths.Count(path);
        if (count < 2) return false;
        var placeA = fk.Placement(fb.Feet, fb.Yaw);

        // The defender's model frame: the inverse of its placement (a turn by its yaw less its facing, its scale).
        float k = kind.Scale > 0f ? kind.Scale : 1f, inv = 1f / k;
        var (sin, cos) = DetMath.SinCos(body.Yaw - kind.FacingYaw);
        var feet = body.Feet;
        Vector3 ToModel(Vector3 p)
        {
            var d = p - feet;
            return new Vector3(cos * d.X + sin * d.Y, -sin * d.X + cos * d.Y, d.Z) * inv;
        }
        for (int j = 0; j < count; j++)
        {
            paths.At(path, j, out var mg, out var mt);
            g[j] = ToModel(PoseMath.Transform(mg, placeA));
            t[j] = ToModel(PoseMath.Transform(mt, placeA));
        }
        var toward = ToModel(fb.Feet) - kg.Hips;
        toward.Z = 0f;

        // Where the solved guard starts: as it is, or — not (fully) solved — where the pose has the blade now (no jump; kept only
        // if a place to meet the blow is found below).
        bool seeding = rig.GuardHandWeight < 1f;
        var hand = rig.GuardHand;
        var blade = LimbIK.Unit(rig.GuardBlade);
        float twistNow = rig.GuardSolveTwist;
        if (seeding)
        {
            if (!Seed(world, assets, e, in anim, out hand, out blade)) return false;
            twistNow *= MathF.Max(0f, rig.GuardHandWeight);   // the turn the pose shows now
        }

        bool parry = c.ParryLeft > 0f;
        float speed = (parry ? gs.SolverParryHandMetresPerSecond : gs.SolverHandMetresPerSecond) * skill.Hand * inv;   // × the skill's hand
        float turnSpeed = (parry ? gs.SolverParryBladeDegreesPerSecond : gs.SolverBladeDegreesPerSecond) * skill.Hand * DetMath.DegToRad;
        // The shoulder, turned with the chest by the guard's twist and the solver's own about the spine's foot (a few turns tried).
        var rel = kg.Shoulder - kg.Hips;
        float twistMost = gs.SolverTwistMostDegrees * DetMath.DegToRad;
        float twistSpeed = (parry ? gs.SolverParryTwistDegreesPerSecond : gs.SolverTwistDegreesPerSecond) * skill.Hand * DetMath.DegToRad;
        Span<Vector3> shoulders = stackalloc Vector3[Twists];
        Span<float> twists = stackalloc float[Twists];
        for (int q = 0; q < Twists; q++)
        {
            twists[q] = twistMost * (q - Twists / 2) / (Twists / 2);
            var (ts, tc) = DetMath.SinCos(anim.GuardTwist + twists[q]);
            shoulders[q] = kg.Hips + new Vector3(tc * rel.X - ts * rel.Y, ts * rel.X + tc * rel.Y, rel.Z);
        }
        float reach = kg.Reach * gs.SolverReachShare;
        float lo = gs.SolverOwnFrom * kg.Length, hi = gs.SolverOwnTo * kg.Length;
        float farthest = reach + hi + LimbIK.Length(kg.Offset);
        float clearance = gs.SolverClearanceMetres * inv, forward = gs.SolverHandForward * inv, across = gs.SolverHandAcross * inv;
        float lowest = gs.SolverHandLowest * inv, highest = gs.SolverHandHighest * inv;
        float rate = MathF.Max(1e-3f, fa.ActionRate);
        var (coneSin, coneCos) = DetMath.SinCos((90f - gs.SolverLeastCrossDegrees) * DetMath.DegToRad);
        if (blade == Vector3.Zero) blade = kg.Axis;

        bool found = false, inTime = false;
        Vector3 bestHand = hand, bestBlade = blade;
        float bestTwist = twistNow, jTwist = twistNow;
        float bestLate = float.MaxValue, bestLead = 0f;
        bool reachedBody = false;
        for (int j = 0; j < count && !inTime && !reachedBody; j++)
        {
            float time = paths.Time(path, j);
            if (time <= fa.ActionTime) continue;
            float lead = (time - fa.ActionTime) / rate;
            // The blade at the body (its outer half within the clearance of the hips' upright axis, between the shins and the head):
            // a crossing after this place is no guard, the body is met first. This place is the last tried.
            for (int s = 0; s < 5 && !reachedBody; s++)
            {
                var b = g[j] + (t[j] - g[j]) * (gs.SolverIncomingFrom + (1f - gs.SolverIncomingFrom) * s / 4f);
                float bx = b.X - kg.Hips.X, by = b.Y - kg.Hips.Y;
                reachedBody = bx * bx + by * by < clearance * clearance && b.Z > 0.2f * inv && b.Z < 2.0f * inv;
            }
            int jb = Math.Max(0, j - 1), ja = Math.Min(count - 1, j + 1);
            float jBest = float.MaxValue;
            Vector3 jHand = default, jBlade = default;
            for (int s = 0; s < 4; s++)
            {
                float share = gs.SolverIncomingFrom + (1f - gs.SolverIncomingFrom) * s / 3f;
                var x = g[j] + (t[j] - g[j]) * share;
                var vel = (g[ja] + (t[ja] - g[ja]) * share) - (g[jb] + (t[jb] - g[jb]) * share);
                // In front of the defender (on the attacker's side of him) and outside his body, at a height a guard can meet.
                // (Measured from the hips, not the feet: 1.1's block steps the body back, the hips 0.22 m behind the feet's place.)
                float hx = x.X - kg.Hips.X, hy = x.Y - kg.Hips.Y;
                if (hx * toward.X + hy * toward.Y < 0f) continue;
                if (hx * hx + hy * hy < clearance * clearance) continue;
                if (x.Z < lowest - 0.3f * inv || x.Z > highest + 0.3f * inv) continue;
                var n = LimbIK.Unit(LimbIK.Cross(t[j] - g[j], vel));
                if (n == Vector3.Zero) continue;
                for (int q = 0; q < Twists; q++)
                {
                var shoulder = shoulders[q];
                if (LimbIK.Length(x - shoulder) > farthest) continue;   // beyond the arm and the blade's meeting part from this shoulder
                float twistNeed = MathF.Abs(twists[q] - twistNow) / MathF.Max(1e-4f, twistSpeed);
                // The blade straight out from the hand toward the crossing where the arm can take it there, but crossing the swept
                // surface at no less than the least crossing angle: within a cone about its normal (either way).
                var reachOut = LimbIK.Unit(x - shoulder);
                for (int sign = 0; sign < 2; sign++)
                {
                    var normal = sign == 0 ? n : -n;
                    float cosOff = LimbIK.Dot(reachOut, normal);
                    if (cosOff <= 0f) continue;   // that way the hand would stand beyond the path
                    // Outside the cone: its edge in the plane of the normal and the arm's line (no trigonometry: the cone's own sine and cosine).
                    var u = cosOff >= coneCos ? reachOut : normal * coneCos + LimbIK.Unit(reachOut - normal * cosOff) * coneSin;
                    var offset = LimbIK.Rotate(LimbIK.FromTo(kg.Axis, u), kg.Offset);
                    float along = Math.Clamp(LimbIK.Dot(x - offset - shoulder, u), lo, hi);
                    var guardAt = x - u * along;
                    var wrist = guardAt - offset;
                    if (LimbIK.Length(wrist - shoulder) > reach) continue;
                    if (wrist.Y - kg.Hips.Y < forward || kg.Side * (wrist.X - kg.Hips.X) < -across || wrist.Z < lowest || wrist.Z > highest) continue;
                    float angle = DetMath.Acos(Math.Clamp(LimbIK.Dot(u, blade), -1f, 1f));
                    float need = MathF.Max(MathF.Max(LimbIK.Length(guardAt - hand) / MathF.Max(1e-4f, speed), angle / MathF.Max(1e-4f, turnSpeed)), twistNeed);
                    // A blade upright or pointing to the off side reads as a guard, and the chest turned no more than it needs: small
                    // preferences, never a reason to be late.
                    float cost = need + (u.Z < -0.3f ? 0.02f : 0f) + 0.01f * MathF.Abs(twists[q]) / MathF.Max(1e-4f, twistMost);
                    if (cost < jBest) { jBest = cost; jHand = guardAt; jBlade = u; jTwist = twists[q]; }
                }
                }
            }
            if (jBest == float.MaxValue) continue;
            found = true;
            if (jBest <= lead) { inTime = true; (bestHand, bestBlade, bestTwist, bestLead, bestLate) = (jHand, jBlade, jTwist, lead, jBest - lead); }
            else if (jBest - lead < bestLate) { (bestHand, bestBlade, bestTwist, bestLead, bestLate) = (jHand, jBlade, jTwist, lead, jBest - lead); }
        }
        // No place on the path the guard could meet the blade before the body (a blow from beyond its reach, or at the far side):
        // nothing to solve — the composed guard (and, raised by a press, the block's own rise) does what it does.
        if (!found) return false;
        if (seeding) rig.GuardHandWeight = 1f;

        // A new blow followed: said once.
        if (rig.GuardThreat != f.Index || rig.GuardSwing != fc.Swing)
        {
            rig.GuardThreat = f.Index;
            rig.GuardSwing = fc.Swing;
            if (world.TryService<EventQueue<GuardSolverFollows>>(out var q))
                q.Publish(new GuardSolverFollows(e, f, fc.Swing, world.Tick, found ? bestLead : 0f, found ? -bestLate : float.NaN));
        }

        // The hand toward it at its speed, the blade turning at its own.
        var step = bestHand - hand;
        float d = LimbIK.Length(step), most = speed * World.TickSeconds;
        rig.GuardHand = d <= most ? bestHand : hand + step * (most / d);
        float a = DetMath.Acos(Math.Clamp(LimbIK.Dot(blade, bestBlade), -1f, 1f)), turnMost = turnSpeed * World.TickSeconds;
        rig.GuardBlade = a <= turnMost ? bestBlade : LimbIK.Unit(LimbIK.Rotate(DetMath.Slerp(Quaternion.Identity, LimbIK.FromTo(blade, bestBlade), turnMost / a), blade));
        rig.GuardSolveTwist = GuardAim.Towards(twistNow, bestTwist, twistSpeed * World.TickSeconds);
        return true;
    }

    /// <summary>Where the pose has the blade now (model space: its guard and its way), from the pose as it stands.</summary>
    bool Seed(World world, CharacterAssets assets, EntityId e, in CharacterAnimation anim, out Vector3 hand, out Vector3 blade)
    {
        hand = blade = default;
        if (!world.TryService<CharacterPoses>(out var poses)) return false;
        var kind = assets.Kind(anim.Kind);
        var bones = poses.Bones(world, e);
        if (!GuardSolver.FirstBlade(kind, bones.Length, out var thing)) return false;
        hand = PoseMath.Transform(thing.Guard, bones[thing.Bone]);
        blade = LimbIK.Unit(PoseMath.TransformNormal(thing.Tip - thing.Guard, bones[thing.Bone]));
        return blade != Vector3.Zero;
    }

    /// <summary>The nearest threat <paramref name="e"/>'s guard turns to (<see cref="GuardAim.Threat(World, CharacterAssets, EntityId, GuardSettings, AimOffsetSettings, out float, out float, out EntityId)"/>'s choice without its line): another fighter within the threat's reach whose blow plays and is not held, within the guard's arc, seen.</summary>
    public static bool Nearest(World world, EntityId e, in Body body, GuardSettings g, out EntityId threat, out int index)
    {
        threat = EntityId.None;
        index = -1;
        var fighters = world.Store<Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var bodies = world.Store<Body>();
        float most = g.GuardThreatMetres * g.GuardThreatMetres, arc = g.GuardArcDegrees * DetMath.DegToRad, best = float.MaxValue;
        var ents = fighters.Entities;
        var data = fighters.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var f = ents[i];
            if (f == e) continue;
            ref var fa = ref anims.Find(f);
            ref var fb = ref bodies.Find(f);
            if (Unsafe.IsNullRef(ref fa) || Unsafe.IsNullRef(ref fb)) continue;
            if (!ActionRoles.IsAttack(fa.Action) || fa.ActionEnding || FollowThrough.Held(world, f, data[i].Swing)) continue;
            float dx = fb.Feet.X - body.Feet.X, dy = fb.Feet.Y - body.Feet.Y, d2 = dx * dx + dy * dy;
            if (d2 > most || d2 >= best) continue;
            float bearing = DetMath.WrapAngle(DetMath.Atan2(dy, dx) - body.Yaw);
            if (MathF.Abs(bearing) > arc) continue;
            if (g.SightRequired && !GuardAim.Sees(world, e, in body, f, g)) continue;
            best = d2;
            threat = f;
            index = i;
        }
        return !threat.IsNone;
    }

    /// <summary>The kind's guard measured once (null: no limbs, no weapon arm, no blade, no block).</summary>
    KindGuard? Guard(CharacterAssets assets, int kindIndex)
    {
        if (kinds.TryGetValue(kindIndex, out var known)) return known;
        KindGuard? made = null;
        var kind = assets.Kind(kindIndex);
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        int n = skeleton.Count;
        if (kind.Limbs is { } limbs && kind.Actions is { Block.Has: true } set && GuardSolver.WeaponArm(skeleton, kind, limbs, out var arm, out float side)
            && GuardSolver.FirstBlade(kind, n, out var thing))
        {
            if (model.Length < n) { model = new Matrix4x4[n]; skin = new Matrix4x4[n]; }
            var state = CharacterAnimation.For(assets, kindIndex);
            state.Action = ActionRole.Block;
            state.ActionClip = set.Block.Clip;
            state.ActionTime = MathF.Max(0f, set.Block.HoldSeconds);
            state.ActionWeight = 1f;
            state.ActionLegs = 1f;
            state.ActionBlend = 1f;
            state.ActionFrom = ActionRole.None;
            state.ActionRate = 1f;
            poser.Evaluate(assets, state, model.AsSpan(0, n), skin.AsSpan(0, n));
            var guard = PoseMath.Transform(thing.Guard, model[thing.Bone]);
            var tip = PoseMath.Transform(thing.Tip, model[thing.Bone]);
            var wrist = model[arm.End].Translation;
            var hips = model[limbs.Root].Translation;
            made = new KindGuard(arm, side, model[arm.Upper].Translation, new Vector3(hips.X, hips.Y, 0f), LimbIK.Reach(model.AsSpan(0, n), arm),
                LimbIK.Length(tip - guard), guard - wrist, LimbIK.Unit(tip - guard));
        }
        kinds[kindIndex] = made;
        return made;
    }
}

/// <summary>
/// The blows' paths: a memo of a blow's blade through its active window — its guard and tip at evenly spaced clip times in
/// the attacker's model space (unplaced), posed by <see cref="SwingArcCore"/> from a canonical state: the attack's clip alone at
/// full weight over the whole body (as it plays standing), the aim's bend settled at the aim (to the degree), no guard. Kept by
/// (kind, role, clip, window, aim, spacing): a pure function of its key, so made once or made again it is the same bits (a load
/// mid-fight starts it empty). Fixed storage: 16 paths of at most 64 places.
/// </summary>
public sealed class BlowPaths
{
    public const int Entries = 16, Places = 64;

    readonly (int Kind, int Role, int Clip, int From, int To, int Aim, int Spacing, int Count)[] keys = new (int, int, int, int, int, int, int, int)[Entries];
    readonly bool[] used = new bool[Entries];
    readonly float[] times = new float[Entries * Places];
    readonly Vector3[] guards = new Vector3[Entries * Places], tips = new Vector3[Entries * Places];
    int next;

    /// <summary>How many paths were made (each a few milliseconds' posing; for the cost's measure).</summary>
    public int Made { get; private set; }

    public int Count(int entry) => keys[entry].Count;
    public float Time(int entry, int j) => times[entry * Places + j];

    public void At(int entry, int j, out Vector3 guard, out Vector3 tip)
    {
        guard = guards[entry * Places + j];
        tip = tips[entry * Places + j];
    }

    /// <summary>The path's entry for a blow, made now when it is not kept.</summary>
    public int Get(CharacterAssets assets, SwingArcCore core, float handRadius, int kind, ActionRole role, int clip, in ActiveWindow window, int aimDegrees, float spacing)
    {
        var key = (Kind: kind, Role: (int)role, Clip: clip, From: BitConverter.SingleToInt32Bits(window.From), To: BitConverter.SingleToInt32Bits(window.To), Aim: aimDegrees,
            Spacing: BitConverter.SingleToInt32Bits(spacing), Count: 0);
        for (int i = 0; i < Entries; i++)
            if (used[i] && keys[i] with { Count = 0 } == key) return i;
        int at = next;
        next = (next + 1) % Entries;
        int count = Math.Clamp((int)MathF.Ceiling((window.To - window.From) / MathF.Max(1e-4f, spacing)) + 1, 2, Places);
        var state = CharacterAnimation.For(assets, kind);
        state.Clip = -1;
        state.PreviousClip = -1;
        state.Action = role;
        state.ActionClip = clip;
        state.ActionWeight = 1f;
        state.ActionLegs = 1f;
        state.ActionBlend = 1f;
        state.ActionFrom = ActionRole.None;
        state.ActionRate = 1f;
        float pitch = aimDegrees * DetMath.DegToRad;
        var rig = new PoseRig { AimPitch = pitch, AimTarget = pitch };
        int made = 0;
        for (int j = 0; j < count; j++)
        {
            float time = window.From + (window.To - window.From) * j / (count - 1);
            if (!core.BladeAt(assets, handRadius, state, true, rig, Matrix4x4.Identity, time, out var blade)) break;
            times[at * Places + j] = time;
            guards[at * Places + j] = blade.Guard;
            tips[at * Places + j] = blade.Tip;
            made++;
        }
        keys[at] = key with { Count = made };
        used[at] = true;
        Made++;
        return at;
    }
}

/// <summary>
/// The give's kick (<see cref="Phase.Resolve"/>, after the follow-through): each blow met at a guard and held, parried or
/// overpowered — by the rules' verdict (<see cref="GuardResolved"/>) when a rules module decides, else every guard contact — pushes
/// the guarding hand (the shield's arm for a shield) along the blow (against the contact's normal) by its impulse
/// (<see cref="GuardSettings.GiveMetresPerNewtonSecond"/>, within its least and most; an overpowered guard
/// <see cref="GuardSettings.GiveOverpoweredScale"/> times as far): the spring's velocity set so its furthest is that far.
/// </summary>
public sealed class GuardGiveSystem : ISystem
{
    public const string Name = "platform.guard-give";
    public const int Order = 960;

    int rigsKnown;

    public void Run(World world)
    {
        if (!world.TryService<GuardSettings>(out var gs) || !gs.Enabled || !gs.Solver) return;
        if (!world.TryService<EventQueue<ContactMade>>(out var contacts) || contacts.ThisTick.Count == 0) return;
        if (!world.TryService<CharacterAssets>(out var assets)) return;
        var rigs = PoseRigSystem.Optional<PoseRig>(world, ref rigsKnown);
        if (rigs is null) return;
        world.TryService<EventQueue<GuardGave>>(out var gave);
        var made = contacts.ThisTick;
        if (gs.RulesDecide)
        {
            if (!world.TryService<EventQueue<GuardResolved>>(out var verdicts)) return;
            var list = verdicts.ThisTick;
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v.Verdict is not (GuardVerdict.Held or GuardVerdict.Parried or GuardVerdict.Overpowered) || v.Met is not (ContactMet.Shield or ContactMet.Weapon)) continue;
                for (int j = 0; j < made.Count; j++)
                    if (made[j].ContactId == v.ContactId) { Kick(world, assets, rigs, gs, made[j], v.Verdict, gave); break; }
            }
        }
        else
            for (int j = 0; j < made.Count; j++)
                if (made[j].Met is ContactMet.Shield or ContactMet.Weapon && made[j].Guard is not (GuardMeet.None or GuardMeet.Clash))
                    Kick(world, assets, rigs, gs, made[j], GuardVerdict.Held, gave);
    }

    static void Kick(World world, CharacterAssets assets, Store<PoseRig> rigs, GuardSettings gs, in ContactMade c, GuardVerdict verdict, EventQueue<GuardGave>? gave)
    {
        ref var rig = ref rigs.Find(c.Target);
        ref var anim = ref world.Store<CharacterAnimation>().Find(c.Target);
        ref var body = ref world.Store<Body>().Find(c.Target);
        if (Unsafe.IsNullRef(ref rig) || Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) return;
        var kind = assets.Kind(anim.Kind);
        float scale = verdict == GuardVerdict.Overpowered ? gs.GiveOverpoweredScale : 1f;
        float metres = Math.Clamp(gs.GiveMetresPerNewtonSecond * c.NormalImpulse, gs.GiveLeastMetres, gs.GiveMostMetres) * scale;
        float k = kind.Scale > 0f ? kind.Scale : 1f;
        var (sin, cos) = DetMath.SinCos(body.Yaw - kind.FacingYaw);
        var push = LimbIK.Unit(-c.Normal);
        var dir = new Vector3(cos * push.X + sin * push.Y, -sin * push.X + cos * push.Y, push.Z);
        float omega = DetMath.TwoPi * MathF.Max(0.01f, gs.GiveHertz);
        // From rest, a critically damped spring kicked with speed v0 goes furthest v0 / (omega e), at 1/omega.
        float v0 = metres / k * omega * MathF.E;
        bool shield = c.Met == ContactMet.Shield;
        if (rig.GuardGiveShield != shield) { rig.GuardGive = Vector3.Zero; rig.GuardGiveVelocity = Vector3.Zero; }
        rig.GuardGiveShield = shield;
        rig.GuardGiveVelocity += dir * v0;
        gave?.Publish(new GuardGave(c.Target, c.Attacker, c.Tick, c.ContactId, shield, metres, verdict));
    }
}

/// <summary>The arm solver registered: its events, the solver after the rig in the pose phase (and as a service, for its cost), the give's kick in Resolve.</summary>
public static class GuardSolverServices
{
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!b.Peek.TryService<EventQueue<GuardSolverFollows>>(out _)) b.Event<GuardSolverFollows>().Event<GuardGave>();
        var solver = new GuardSolverSystem();
        b.Service(solver);   // for the cost's measure (its LastSeconds); not digested, never read by the simulation
        b.System(AnimationRegistration.PosePhase, AnimationRegistration.AnimationOrder + GuardSolverSystem.Order, GuardSolverSystem.Name, solver);
        b.System(Phase.Resolve, GuardGiveSystem.Order, GuardGiveSystem.Name, new GuardGiveSystem());
        return b;
    }
}
