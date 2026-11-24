// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Animation;

/// <summary>
/// Where a character's hips, feet and hands go beyond what its motion says (ours):
/// the hips moved and leaned, each ankle lifted or lowered to the ground under it or put on a top, and the
/// hands put on a ledge — all in the character's model space (metres, Z up, the model facing +Y as 1.1's do), applied by
/// the <see cref="PoseEvaluator"/> after the motion and its fade, by two-bone IK (<see cref="LimbIK"/>). The
/// <see cref="PoseRigSystem"/> works it out every tick from the body, the move it is making (<see cref="Traversal"/>)
/// and the ground under the feet (rays down the level's collision), and keeps the few numbers it eases from tick to
/// tick here too. Plain numbers, digested; the pose stays a pure function of the components.
/// </summary>
public struct PoseRig
{
    /// <summary>How far the hips (the root bone) are moved (model space, m) and leaned forwards (radians).</summary>
    public Vector3 RootOffset;
    public float Lean;

    /// <summary>How much the legs are placed (0: as the motion has them, 1: at their targets).</summary>
    public float LegWeight;
    /// <summary>Each ankle's target is where the motion has it raised by this (m): the ground under it, or a tuck.</summary>
    public float LeftLift, RightLift;
    /// <summary>An ankle put at a point instead (model space, m: a foot stepping onto a top), and how much.</summary>
    public Vector3 LeftFoot, RightFoot;
    public float LeftFootWeight, RightFootWeight;

    /// <summary>The knuckles' targets (model space, m: on a ledge, shoulder width apart along it) and the held face's outward normal.</summary>
    public Vector3 LeftHand, RightHand, HandNormal;
    /// <summary>How much the hands are on their targets, and how much the hips are pulled to where the arms reach them (hanging).</summary>
    public float HandWeight, Pull;

    /// <summary>The ground's slope under each foot: its normal (model space; zero for level ground), the foot turned to lie on it.</summary>
    public Vector3 LeftSlope, RightSlope;

    /// <summary>The system's memory: the feet's ground weight and lifts, the hips' drop, the crouch's lowering, the landing's dip.</summary>
    public float GroundWeight, LeftGround, RightGround, HipDrop, Crouch, DipSeconds, DipDepth, LastVerticalSpeed;

    int wasGrounded;

    public bool WasGrounded { readonly get => wasGrounded != 0; set => wasGrounded = value ? 1 : 0; }

    /// <summary>
    /// The aim offset (<see cref="AimOffset"/>): the bend of the upper body now (radians, up positive), eased by the
    /// <see cref="PoseRigSystem"/> towards <see cref="AimTarget"/> while an attack or the block plays and back to 0 after;
    /// and the aim wanted (set each tick by whoever aims the body: the player's view, a script).
    /// </summary>
    public float AimPitch, AimTarget;

    /// <summary>
    /// The legs' turn (<see cref="LegTurn"/>): how far the hips and legs are drawn turned from the body's facing now
    /// (radians, counter-clockwise: toward its left), the body's yaw that turn was measured against (so the legs keep their
    /// place in the world as it turns), and where the turn is (<see cref="Animation.LegMode"/>). Moved on by
    /// <see cref="LegTurnSystem"/>; 0 while the turn is off.
    /// </summary>
    public float LegYaw, LegFrom;
    public LegMode LegMode;

    // ---- the guard placed by the arm solver (Animation/GuardSolver.cs) ----

    /// <summary>
    /// Where the arm solver (<see cref="GuardSolverSystem"/>) has the guarding blade's guard (its hilt end) now, and the way
    /// the blade lies (unit, guard to tip), both in the model's space; laid on the pose by <see cref="GuardSolver.Pose"/> (the
    /// weapon arm by IK, the hand turned) by <see cref="GuardHandWeight"/> (0, the default: the composed guard as before).
    /// </summary>
    public Vector3 GuardHand, GuardBlade;
    public float GuardHandWeight;

    /// <summary>The blow the solver follows (its owner's entity index and the blow's number), so a new one is told once.</summary>
    public int GuardThreat, GuardSwing;

    /// <summary>The solver's own turn of the chest (radians about the body's up, left positive) beyond the guard's twist toward the foe: the sword's shoulder brought toward the intercept, by the same weight.</summary>
    public float GuardSolveTwist;

    /// <summary>
    /// The defender's <b>give</b> — the guarding hand pushed along a blow met at its guard and springing back (critically
    /// damped): its offset now (model space) and its velocity; on the shield arm when <see cref="GuardGiveShield"/>, else the weapon
    /// arm. Zero, nothing.
    /// </summary>
    public Vector3 GuardGive, GuardGiveVelocity;
    int guardGiveShield;

    /// <summary>Whether the give is on the shield arm (a blow met the shield) rather than the weapon arm.</summary>
    public bool GuardGiveShield { readonly get => guardGiveShield != 0; set => guardGiveShield = value ? 1 : 0; }

    /// <summary>Whether this rig moves the limbs (else the pose is the motion's alone; the aim offset is applied apart).</summary>
    public readonly bool Active =>
        RootOffset != Vector3.Zero || Lean != 0f || LegWeight > 0f || HandWeight > 0f || LeftFootWeight > 0f || RightFootWeight > 0f;
}

/// <summary>
/// The numbers the <see cref="PoseRigSystem"/> places the limbs by (ours, all tunable, all looked at by eye
/// on Christof).
/// </summary>
public sealed record PoseRigSettings
{
    /// <summary>The most the hips are lowered so a foot reaches a lower tread or slope (m), so the legs never fold.</summary>
    public float MostHipDrop { get; init; } = 0.3f;
    /// <summary>The most a foot is raised to ground above the body's feet (m): about a step.</summary>
    public float MostLift { get; init; } = 0.35f;
    /// <summary>The most a foot is lowered to ground below the body's feet (m).</summary>
    public float MostDrop { get; init; } = 0.45f;
    /// <summary>How fast a foot's lift follows the ground under it (m/s).</summary>
    public float LiftSpeed { get; init; } = 2.5f;
    /// <summary>How fast the hips' drop follows (m/s).</summary>
    public float HipSpeed { get; init; } = 1.5f;
    /// <summary>How long the feet take to come onto the ground or leave it (s).</summary>
    public float GroundSeconds { get; init; } = 0.12f;
    /// <summary>A crouched walk's hips below the walk's (m) and its lean (radians), when no crouched walk fills the slot.</summary>
    public float CrouchDrop { get; init; } = 0.42f;
    public float CrouchLean { get; init; } = 0.3f;
    /// <summary>How long the crouch's lowering takes (s): the fade's fifth of a second, so it keeps step with it.</summary>
    public float CrouchSeconds { get; init; } = 0.2f;
    /// <summary>A landing from this downward speed (m/s) dips the hips, more the faster, up to <see cref="MostDip"/> m over <see cref="DipSeconds"/> s.</summary>
    public float DipFrom { get; init; } = 2f;
    public float MostDip { get; init; } = 0.16f;
    public float DipSeconds { get; init; } = 0.3f;
    /// <summary>
    /// The most a foot is turned to lie on sloping ground: its normal's upward part at least this (the cosine of 35°,
    /// a little under the steepest ground walked).
    /// </summary>
    public float LeastFootNormalZ { get; init; } = 0.82f;
    /// <summary>How fast a foot turns to the slope under it (a share a second).</summary>
    public float SlopeRate { get; init; } = 12f;
    /// <summary>Where a foot's ground is looked for: from this far above the body's feet (m).</summary>
    public float RayAbove { get; init; } = 0.4f;
    /// <summary>A hit counts as ground when its normal's upward part is at least this.</summary>
    public float GroundNormalZ { get; init; } = 0.6f;
    /// <summary>The aim offset's reach and ease (SANDBOX).</summary>
    public AimOffsetSettings Aim { get; init; } = new();
}

/// <summary>
/// Works out every animated character's <see cref="PoseRig"/> each tick (the end of <see cref="Phase.Move"/>, after the
/// animation chose and advanced the motion; ours):
/// <list type="bullet">
/// <item><b>Feet on uneven ground.</b> Standing and moving on the ground, each ankle's place in the motion (a table of
/// the clip's ankles, made once per clip) is looked down on in the level's collision; the foot is raised or lowered to
/// the ground under it (eased, within <see cref="PoseRigSettings.MostLift"/> and <see cref="PoseRigSettings.MostDrop"/>)
/// and the hips lowered by the lower foot's drop (within <see cref="PoseRigSettings.MostHipDrop"/>), so on stairs, a
/// slope or an edge each foot stands on its own ground. Off in the air and in a move.</item>
/// <item><b>Crouching.</b> A crouched walk whose slot holds no motion of its own is the walk with the hips lowered and
/// leaned, the legs bent by IK to keep the walk's feet (its stride matched as the walk's).</item>
/// <item><b>A landing</b> dips the hips by the speed it came down at.</item>
/// <item><b>The moves</b> (<see cref="Traversal"/>): per move and along its progress, the hands put on the lip
/// (shoulder width apart along the edge, the knuckles on it), the hips pulled to where the arms reach when hanging,
/// the lean, the feet tucked over an obstacle, a foot stepping onto the top and the other kept on the ground below.</item>
/// </list>
/// A character with an animation but no rig is given one the first time it is seen. No allocation per tick once every
/// clip a character plays has been seen.
/// </summary>
public sealed class PoseRigSystem(CharacterAssets assets, PoseRigSettings? settings = null) : ISystem
{
    /// <summary>The name it is registered under, and its place after the animation (<see cref="AnimationRegistration.AnimationOrder"/> + 10).</summary>
    public const string Name = "platform.pose-rig";
    public const int Order = 10;

    readonly PoseRigSettings settings = settings ?? new PoseRigSettings();
    readonly Dictionary<long, AnkleTrack> tracks = [];
    BoneTransform[] local = new BoneTransform[64];
    Matrix4x4[] model = new Matrix4x4[64];
    int traversalKnown, bodiesKnown;   // 0 not looked for, 1 registered, 2 not registered (a world without movement)

    public PoseRigSettings Settings => settings;

    /// <summary>A clip's ankles in its model space, 30 times a second (1.1's frame rate), for looking down under the feet.</summary>
    sealed class AnkleTrack(Vector3[] left, Vector3[] right, float duration, bool loops, Vector3[]? leftBall = null, Vector3[]? rightBall = null)
    {
        public float Duration => duration;

        public (Vector3 Left, Vector3 Right) At(float seconds) => At(left, right, seconds);

        /// <summary>The balls of the feet (model space) at <paramref name="seconds"/>, or the ankles for a track made without them.</summary>
        public (Vector3 Left, Vector3 Right) Balls(float seconds) => leftBall is not null && rightBall is not null ? At(leftBall, rightBall, seconds) : At(seconds);

        (Vector3, Vector3) At(Vector3[] l, Vector3[] r, float seconds)
        {
            int n = l.Length;
            if (n == 0) return default;
            float f = seconds * 30f;
            if (loops && duration > 0f) { float frames = duration * 30f; f -= MathF.Floor(f / frames) * frames; }
            f = Math.Clamp(f, 0f, n - 1);
            int i = (int)f;
            int j = Math.Min(i + 1, n - 1);
            float t = f - i;
            return (PoseMath.Lerp(l[i], l[j], t), PoseMath.Lerp(r[i], r[j], t));
        }
    }

    public void Run(World world)
    {
        var animations = world.Store<CharacterAnimation>();
        var rigs = world.Store<PoseRig>();
        {
            var ents = animations.Entities;
            for (int i = 0; i < ents.Length; i++)
                if (!rigs.Has(ents[i])) world.Set(ents[i], new PoseRig());
        }
        var inputs = world.Store<MotionInput>();
        var bodies = Optional<Body>(world, ref bodiesKnown);
        var moves = Optional<Traversal>(world, ref traversalKnown);
        world.TryService<LevelSpace>(out var level);
        world.TryStore<People.PersonGround>(out var grounds);
        world.TryService<People.PersonGroundSettings>(out var groundSettings);
        world.TryStore<People.PersonFlight>(out var flights);

        var data = rigs.Data;
        var entities = rigs.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = entities[i];
            ref var anim = ref animations.Find(e);
            if (Unsafe.IsNullRef(ref anim)) continue;
            ref var input = ref inputs.Find(e);
            var motion = Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input;
            ref var body = ref bodies is null ? ref Unsafe.NullRef<Body>() : ref bodies.Find(e);
            bool hasBody = !Unsafe.IsNullRef(ref body);
            Traversal move = default;
            if (moves is not null)
            {
                ref var m = ref moves.Find(e);
                if (!Unsafe.IsNullRef(ref m)) move = m;
            }
            // A person on uneven ground (People.PersonGround), when the world keeps it.
            ref var ground = ref grounds is null ? ref Unsafe.NullRef<People.PersonGround>() : ref grounds.Find(e);
            ref var flight = ref flights is null ? ref Unsafe.NullRef<People.PersonFlight>() : ref flights.Find(e);   // on a flight
            Step(ref data[i], in anim, in motion, hasBody ? body.Feet : default, hasBody ? body.Yaw : 0f, hasBody, in move, level, World.TickSeconds, ref ground, groundSettings,
                 Unsafe.IsNullRef(ref flight) ? default : flight);
        }
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>A component's store when the world registered it (looked for once), else null.</summary>
    internal static Store<T>? Optional<T>(World world, ref int known) where T : struct
    {
        if (known == 0)
        {
            try { world.Store<T>(); known = 1; }
            catch (InvalidOperationException) { known = 2; }
        }
        return known == 1 ? world.Store<T>() : null;
    }

    /// <summary>
    /// One character's rig for one tick: from its animation (the motion it plays), how its body moves, where its feet
    /// are and which way it faces (when it has a body), the move it is making, and the level to look for ground in.
    /// </summary>
    public void Step(ref PoseRig rig, in CharacterAnimation anim, in MotionInput input, Vector3 feet, float yaw, bool hasBody,
                     in Traversal move, LevelSpace? level, float seconds) =>
        Step(ref rig, anim, input, feet, yaw, hasBody, move, level, seconds, ref Unsafe.NullRef<People.PersonGround>(), null);

    /// <summary>
    /// As the overload before, for a person on uneven ground (<paramref name="uneven"/>, <see cref="People.PersonGround"/>, a null
    /// reference for anyone else): its slope clip's own ground taken out of each foot's (the feet's IK lays on only the rest of the slope), its
    /// toes looked for as well as its ankles (a foot over a riser is put on the tread above, not into the riser), and — with the stairs' rule —
    /// the hips drawn on the line (<see cref="People.PersonGround.Line"/>) and lowered only so a planted foot reaches its ground, by a spring.
    /// </summary>
    public void Step(ref PoseRig rig, in CharacterAnimation anim, in MotionInput input, Vector3 feet, float yaw, bool hasBody,
                     in Traversal move, LevelSpace? level, float seconds, ref People.PersonGround uneven, People.PersonGroundSettings? groundSettings) =>
        Step(ref rig, anim, input, feet, yaw, hasBody, move, level, seconds, ref uneven, groundSettings, default);

    /// <summary>
    /// As the overload before, for a person on a flight of stairs (<paramref name="flight"/>, <see cref="People.PersonFlight"/>): its stair clip's feet
    /// laid over the gait's by its share, and the clip's own treads under them taken out of the ground found (the IK lays on only what the flight
    /// differs from the clip's).
    /// </summary>
    public void Step(ref PoseRig rig, in CharacterAnimation anim, in MotionInput input, Vector3 feet, float yaw, bool hasBody,
                     in Traversal move, LevelSpace? level, float seconds, ref People.PersonGround uneven, People.PersonGroundSettings? groundSettings,
                     in People.PersonFlight flight)
    {
        var kind = assets.Kind(anim.Kind);
        var s = settings;
        // The aim offset, eased towards its aim (clamped to the body's reach) while an attack or the block plays, and
        // back to straight as it ends.
        bool aiming = (ActionRoles.IsAttack(anim.Action) || anim.Action == ActionRole.Block) && !anim.ActionEnding && kind.Actions is { Spine.Length: > 0 };
        float aim = Towards(rig.AimPitch, aiming ? AimOffset.Clamp(rig.AimTarget, s.Aim) : 0f, s.Aim.Rate * seconds);
        rig.AimPitch = aim;
        if (kind.Limbs is not { } limbs) { float target = rig.AimTarget; rig = default; rig.AimPitch = aim; rig.AimTarget = target; return; }
        var (sin, cos) = DetMath.SinCos(yaw - kind.FacingYaw);
        float scale = kind.Scale > 0f ? kind.Scale : 1f;

        // The ground under each foot, standing or moving on it.
        bool onGround = input.Grounded && input.Traversal == TraversalKind.None;
        rig.GroundWeight = Towards(rig.GroundWeight, onGround ? 1f : 0f, seconds / MathF.Max(1e-3f, s.GroundSeconds));
        float wantLeft = 0f, wantRight = 0f;
        float ownLeft = 0f, ownRight = 0f;   // the stair clip's own treads under the feet (taken out with the clip's steps on the flight's, eased together)
        Vector3 slopeLeft = Vector3.UnitZ, slopeRight = Vector3.UnitZ;
        // A person on uneven ground.
        bool person = !Unsafe.IsNullRef(ref uneven) && kind.Person is not null;
        bool line = person && uneven.Stairs;
        float standLeft = 1f, standRight = 1f;
        if (person)
        {
            var p = kind.Person!;
            var (slopeClip, slopeWeight) = p.Slopes is { } slopes ? slopes.For(p, anim.Clip, uneven.Slope) : (-1, 0f);
            float signed = uneven.Slope >= 0f ? slopeWeight : -slopeWeight;
            // On a flight, its stair clip.
            var (stairClip, stairSlot, stairWeight) = p.Stairs is { } st && flight.Share > 0f ? st.For(p, anim.Clip, flight.Way, flight.Share) : (-1, -1, 0f);
            if (onGround && hasBody && level is not null && anim.Clip >= 0
                && PersonFeetAt(kind, anim.Kind, anim.Clip, anim.Time, limbs, slopeClip, slopeWeight, out var l, out var r, out var bl, out var br, stairClip, stairWeight))
            {
                var (ls, lc) = rig.LegYaw != 0f ? DetMath.SinCos(yaw + rig.LegYaw - kind.FacingYaw) : (sin, cos);
                float tan = p.Slopes is { } sl ? sl.Tan * signed : 0f;
                // With the stairs' rule, the balls a little ahead along the motion too (a foot in its swing onto the tread it comes to).
                var (al, ar) = (bl, br);
                if (line && groundSettings is { FootAhead: > 0f } && anim.Rate != 0f)
                    PersonFeetAt(kind, anim.Kind, anim.Clip, anim.Time + groundSettings.FootAhead * anim.Rate, limbs, slopeClip, slopeWeight, out _, out _, out al, out ar, stairClip, stairWeight);
                // The stair clip's own tread under each foot (the higher of under its ankle and under its ball, as the flight's is found).
                float clipLeft = 0f, clipRight = 0f;
                if (stairWeight > 0f)
                {
                    float d = assets.Clip(anim.Clip).Duration, share = d > 0f ? anim.Time / d : 0f;
                    share -= MathF.Floor(share);
                    float at = share * assets.Clip(stairClip).Duration;
                    var climb = p.Stairs!;
                    clipLeft = MathF.Max(climb.ClipGround(stairSlot, at, l.Y), climb.ClipGround(stairSlot, at, bl.Y)) * scale;
                    clipRight = MathF.Max(climb.ClipGround(stairSlot, at, r.Y), climb.ClipGround(stairSlot, at, br.Y)) * scale;
                    (ownLeft, ownRight) = (stairWeight * clipLeft, stairWeight * clipRight);
                }
                (wantLeft, slopeLeft, standLeft) = PersonGroundUnder(level, l * scale, bl * scale, al * scale, feet, ls, lc, tan, limbs.AnkleHeight * scale, ownLeft);
                (wantRight, slopeRight, standRight) = PersonGroundUnder(level, r * scale, br * scale, ar * scale, feet, ls, lc, tan, limbs.AnkleHeight * scale, ownRight);
                if (stairWeight > 0f && p.Stairs!.Made[stairSlot] is { } made && made.Ground.RiserM > 0f)
                {
                    // The ground found under each foot (less its clip's own tread) held within StairFootBand of where the clip's own treads are
                    // drawn on the line through the flight's nosings, the riser's difference laid on (its treads by the flight's riser over its own): a
                    // tread's edge met a frame apart under the foot and in the clip does not jolt it by a riser.
                    float more = flight.Riser / made.Ground.RiserM - 1f;
                    float madeLeft = stairWeight * (uneven.Line + more * clipLeft) + (1f - stairWeight) * (wantLeft + ownLeft);
                    float madeRight = stairWeight * (uneven.Line + more * clipRight) + (1f - stairWeight) * (wantRight + ownRight);
                    float band = StairFootBand + (1f - stairWeight);
                    wantLeft = madeLeft + Math.Clamp(wantLeft - madeLeft, -band, band);
                    wantRight = madeRight + Math.Clamp(wantRight - madeRight, -band, band);
                }
            }
        }
        else if (onGround && hasBody && level is not null && anim.Clip >= 0 && Ankles(kind, anim.Kind, anim.Clip, anim.Time, limbs, out var l, out var r))
        {
            // The ground looked for under the feet as the legs' turn has them (LegTurn).
            var (ls, lc) = rig.LegYaw != 0f ? DetMath.SinCos(yaw + rig.LegYaw - kind.FacingYaw) : (sin, cos);
            (wantLeft, slopeLeft) = GroundUnder(level, l * scale, feet, ls, lc);
            (wantRight, slopeRight) = GroundUnder(level, r * scale, feet, ls, lc);
        }
        float turn = MathF.Min(1f, s.SlopeRate * seconds);
        rig.LeftSlope = Ease(rig.LeftSlope, slopeLeft, turn);
        rig.RightSlope = Ease(rig.RightSlope, slopeRight, turn);
        float liftSpeed = line && groundSettings is not null ? groundSettings.FootLiftSpeed : s.LiftSpeed;   // quicker on the line
        rig.LeftGround = Towards(rig.LeftGround, wantLeft, liftSpeed * seconds);
        rig.RightGround = Towards(rig.RightGround, wantRight, liftSpeed * seconds);
        if (line)
        {
            // The hips on the line, lowered below it only so a foot planted below it reaches its ground (a foot in its swing need
            // not), by a critically damped spring.
            float below = MathF.Min(0f, MathF.Min(standLeft * (wantLeft - uneven.Line), standRight * (wantRight - uneven.Line)));
            float hipLine = onGround ? Math.Clamp(below, -s.MostHipDrop, 0f) : 0f;
            People.PersonGroundSystem.Damped(ref rig.HipDrop, ref uneven.HipRate, hipLine, groundSettings?.HipHalfLife ?? 0.1f, seconds);
        }
        else
        {
            float hip = onGround ? Math.Clamp(MathF.Min(0f, MathF.Min(wantLeft, wantRight)), -s.MostHipDrop, 0f) : 0f;
            rig.HipDrop = Towards(rig.HipDrop, hip, s.HipSpeed * seconds);
        }

        // The crouched walk, lowered when its slot holds no motion of its own.
        bool lower = anim.Role == MotionRole.CrouchWalk && kind.Motion(MotionRole.CrouchWalk).Composed;
        rig.Crouch = Towards(rig.Crouch, lower ? 1f : 0f, seconds / MathF.Max(1e-3f, s.CrouchSeconds));

        // A landing's dip.
        // Not under a landing of the models repository's: its own fold is the dip.
        if (input.Grounded && !rig.WasGrounded && -rig.LastVerticalSpeed >= s.DipFrom && kind.Person?.Air?.Holds(anim.Clip) != true
            && kind.Person?.Over?.Holds(anim.Clip) != true    // nor under its landings moving on
            && kind.Person?.Ledge?.Holds(anim.Clip) != true   // nor under a ledge clip
            && kind.Person?.Ladder?.Holds(anim.Clip) != true) // nor under a ladder clip
        {
            rig.DipSeconds = 0f;
            rig.DipDepth = MathF.Min(s.MostDip, 0.04f + (-rig.LastVerticalSpeed - s.DipFrom) * 0.03f);
        }
        else if (rig.DipDepth > 0f)
        {
            rig.DipSeconds += seconds;
            if (rig.DipSeconds >= s.DipSeconds || !input.Grounded) { rig.DipDepth = 0f; rig.DipSeconds = 0f; }
        }
        float dip = rig.DipDepth > 0f ? rig.DipDepth * Bump(rig.DipSeconds / s.DipSeconds, 0f, 1f) : 0f;
        rig.WasGrounded = input.Grounded;
        rig.LastVerticalSpeed = input.VerticalSpeed;

        // The move.
        // Not under a move's clip of the models repository's: the clip is the whole body, its capsule carried along its path.
        // Nor under a ledge clip: the clip is the whole body, its capsule placed from the edge (the hands' IK onto the lip is still open).
        // Nor under a ladder clip: the clip is the whole body, its capsule placed from the ladder position.
        var shape = input.Traversal == TraversalKind.None || kind.Person?.Over?.Holds(anim.Clip) == true || kind.Person?.Ledge?.Holds(anim.Clip) == true
                    || kind.Person?.Ladder?.Holds(anim.Clip) == true || input.Traversal is TraversalKind.Squeeze or TraversalKind.Cover ? default : Shape(input.Traversal, Math.Clamp(input.TraversalProgress, 0f, 1f), in move, limbs, feet, sin, cos, scale);

        rig.RootOffset = shape.Offset + new Vector3(0f, 0f, rig.HipDrop * rig.GroundWeight - rig.Crouch * s.CrouchDrop - dip + (line ? uneven.Line : 0f));   // on the line
        rig.Lean = shape.Lean + rig.Crouch * s.CrouchLean;
        float ground = rig.GroundWeight;
        rig.LegWeight = MathF.Max(MathF.Max(ground, rig.Crouch), MathF.Max(shape.Legs, dip > 0f ? 1f : 0f));
        rig.LeftLift = rig.LeftGround * ground + shape.LiftLeft;
        rig.RightLift = rig.RightGround * ground + shape.LiftRight;
        rig.LeftFoot = default; rig.RightFoot = shape.FootRight;
        rig.LeftFootWeight = 0f; rig.RightFootWeight = shape.FootRightWeight;
        rig.LeftHand = shape.HandLeft; rig.RightHand = shape.HandRight; rig.HandNormal = shape.HandNormal;
        rig.HandWeight = shape.Hands; rig.Pull = shape.Pull;
    }

    /// <summary>How far apart a ladder's rungs are taken to be for the feet's steps (m; ours).</summary>
    public const float LadderRung = 0.3f;

    /// <summary>What one move does to the limbs at a point of its progress.</summary>
    struct MoveShape
    {
        public Vector3 Offset; public float Lean;
        public float Legs, LiftLeft, LiftRight;
        public Vector3 FootRight; public float FootRightWeight;   // the leading foot; the other is kept on its ground
        public Vector3 HandLeft, HandRight, HandNormal; public float Hands, Pull;
    }

    /// <summary>
    /// Each move's limbs along its progress <paramref name="p"/> (ours; the numbers are looked at by eye). The hands go
    /// on the lip shoulder width apart; a foot that steps onto the top goes a quarter metre in from the edge; a planted
    /// foot is kept on the ground the move started from until it steps up.
    /// </summary>
    MoveShape Shape(TraversalKind kind, float p, in Traversal move, LimbMap limbs, Vector3 feet, float sin, float cos, float scale)
    {
        var shape = new MoveShape();
        bool hasLip = move.Lip != Vector3.Zero;
        var lip = ToModel(move.Lip - feet, sin, cos) / scale;
        var normal = LimbIK.Unit(ToModel(new Vector3(move.LipNormal.X, move.LipNormal.Y, 0f), sin, cos));
        if (normal == Vector3.Zero) normal = -Vector3.UnitY;
        float fromZ = (move.From.Z - feet.Z) / scale;
        float topZ = hasLip ? lip.Z : (move.From.Z + move.Height - feet.Z) / scale;
        var along = new Vector3(-normal.Y, normal.X, 0f);
        if (along.X < 0f) along = -along;
        float half = limbs.ShoulderWidth * 0.5f;
        shape.HandRight = lip + along * half;
        shape.HandLeft = lip - along * half;
        shape.HandNormal = normal;
        var onTop = new Vector3(lip.X, lip.Y, topZ) - normal * 0.25f + along * 0.1f + new Vector3(0f, 0f, limbs.AnkleHeight);
        float Planted(float stepUpFrom, float stepUpTo) =>
            Math.Clamp(fromZ + (topZ - fromZ) * Ramp(p, stepUpFrom, stepUpTo), -settings.MostDrop - 0.2f, settings.MostLift + 0.6f);

        switch (kind)
        {
            case TraversalKind.HopUp:
                shape.Lean = 0.2f * Bump(p, 0f, 1f);
                shape.Offset.Z = -0.1f * Bump(p, 0f, 0.3f) - 0.12f * Bump(p, 0.7f, 1f);
                shape.Legs = 1f;
                shape.LiftLeft = shape.LiftRight = 0.25f * Bump(p, 0.2f, 0.85f);
                break;
            case TraversalKind.StepOver:
                // A stride lifted over a thin thing — the hurdle's shape, upright and lighter.
                shape.Lean = 0.15f * Bump(p, 0f, 1f);
                shape.Legs = 1f;
                shape.LiftLeft = 0.3f * Bump(p, 0.1f, 0.8f);
                shape.LiftRight = 0.15f * Bump(p, 0.3f, 0.95f);
                break;
            case TraversalKind.Hurdle:
                shape.Lean = 0.35f * Bump(p, 0f, 1f);
                shape.Offset.Z = -0.08f * Bump(p, 0.8f, 1f);
                shape.Legs = 1f;
                shape.LiftLeft = 0.35f * Bump(p, 0.1f, 0.9f);
                shape.LiftRight = 0.2f * Bump(p, 0.15f, 0.85f);
                break;
            case TraversalKind.Vault:
                shape.Hands = hasLip ? Window(p, 0f, 0.2f, 0.55f, 0.75f) : 0f;
                shape.Pull = shape.Hands;
                shape.Lean = 0.6f * Bump(p, 0.05f, 0.95f);
                shape.Offset.Z = -0.1f * Bump(p, 0.8f, 1f);
                shape.Legs = 1f;
                shape.LiftLeft = shape.LiftRight = 0.4f * Bump(p, 0.15f, 0.8f);
                break;
            case TraversalKind.ClimbOnto:
                shape.Hands = hasLip ? Window(p, 0f, 0.15f, 0.6f, 0.8f) : 0f;
                shape.Pull = shape.Hands;
                shape.Lean = 0.5f * Bump(p, 0f, 0.9f);
                shape.Legs = 1f;
                shape.LiftLeft = Planted(0.55f, 0.9f);
                shape.LiftRight = topZ * Ramp(p, 0.6f, 0.95f);
                shape.FootRight = onTop; shape.FootRightWeight = Window(p, 0.25f, 0.45f, 0.75f, 0.95f);
                break;
            case TraversalKind.Mantle:
                shape.Hands = hasLip ? Window(p, 0f, 0.1f, 0.7f, 0.9f) : 0f;
                shape.Pull = shape.Hands;
                shape.Lean = 0.35f * Window(p, 0f, 0.15f, 0.8f, 1f);
                shape.Legs = Ramp(p, 0.4f, 0.6f);
                shape.LiftLeft = Planted(0.6f, 0.95f);
                shape.LiftRight = topZ * Ramp(p, 0.6f, 0.95f);
                shape.FootRight = onTop; shape.FootRightWeight = Window(p, 0.45f, 0.62f, 0.8f, 0.95f);
                break;
            case TraversalKind.Grab:
            case TraversalKind.LowerToHang:   // as a grab, the hands taking the lip over the first third
                shape.Hands = hasLip ? Ramp(p, 0f, 0.35f) : 0f;
                shape.Pull = 1f;
                break;
            case TraversalKind.Hang:
                shape.Hands = hasLip ? 1f : 0f;
                shape.Pull = 1f;
                break;
            case TraversalKind.PullUp:
                shape.Hands = hasLip ? 1f - Ramp(p, 0.65f, 0.85f) : 0f;
                shape.Pull = 1f;
                shape.Lean = 0.5f * Bump(p, 0.3f, 1f);
                shape.Legs = Ramp(p, 0.35f, 0.55f);
                shape.LiftLeft = topZ * Ramp(p, 0.6f, 0.95f);
                shape.LiftRight = topZ * Ramp(p, 0.6f, 0.95f);
                shape.FootRight = onTop; shape.FootRightWeight = Window(p, 0.35f, 0.55f, 0.8f, 0.95f);
                break;
            case TraversalKind.Drop:
                shape.Hands = hasLip ? 1f - Ramp(p, 0f, 0.3f) : 0f;
                shape.Pull = shape.Hands;
                break;
            case TraversalKind.LadderOn:
                // Getting onto a ladder, as a grab: the hands taking its face over the first half.
                shape.Hands = hasLip ? Ramp(p, 0f, 0.5f) : 0f;
                shape.Pull = 0.5f;
                shape.Legs = Ramp(p, 0.5f, 1f);
                break;
            case TraversalKind.Ladder:
            {
                // On a ladder, the hang's shape with the arms half bent: the hands on its face (the motor keeps the lip
                // there, LadderHands over the feet), the right foot on its face by IK, and the feet lifted in turn, a rung
                // (LadderRung) a step, as the body climbs.
                shape.Hands = hasLip ? 1f : 0f;
                shape.Pull = 0.5f;
                shape.Legs = 1f;
                float phase = (feet.Z - move.From.Z) / LadderRung;
                float f = phase - MathF.Floor(phase);
                float turn = f < 0.5f ? 2f * f : 2f - 2f * f;
                shape.LiftLeft = LadderRung * turn / scale;
                shape.LiftRight = LadderRung * (1f - turn) / scale;
                if (hasLip)
                {
                    shape.FootRight = new Vector3(lip.X, lip.Y, shape.LiftRight + limbs.AnkleHeight) + normal * 0.08f;
                    shape.FootRightWeight = 1f;
                }
                break;
            }
        }
        return shape;
    }

    /// <summary>A world offset from the feet turned into the model's own axes (the inverse of <see cref="CharacterKind.Placement"/>'s turn).</summary>
    static Vector3 ToModel(Vector3 d, float sin, float cos) => new(cos * d.X + sin * d.Y, -sin * d.X + cos * d.Y, d.Z);

    /// <summary>
    /// The ground's height above the body's feet under an ankle at <paramref name="ankle"/> (model space), or 0 when none
    /// is found within reach; and its normal in model space, tipped at most <see cref="PoseRigSettings.MostFootTilt"/>
    /// (up when there is none).
    /// </summary>
    (float Lift, Vector3 Normal) GroundUnder(LevelSpace level, Vector3 ankle, Vector3 feet, float sin, float cos) => GroundUnder(level, ankle, feet, sin, cos, out _);

    /// <summary>As the overload before, and whether any ground was found within reach (a toe over nothing is not looked under).</summary>
    (float Lift, Vector3 Normal) GroundUnder(LevelSpace level, Vector3 ankle, Vector3 feet, float sin, float cos, out bool found)
    {
        var s = settings;
        found = false;
        float wx = ankle.X * cos - ankle.Y * sin + feet.X, wy = ankle.X * sin + ankle.Y * cos + feet.Y;
        float length = s.RayAbove + s.MostDrop;
        var hit = level.Collision.Ray(new Vector3(wx, wy, feet.Z + s.RayAbove), -Vector3.UnitZ, length);
        if (hit is not { } h || h.Normal.Z < s.GroundNormalZ) return (0f, Vector3.UnitZ);
        float lift = h.Point.Z - feet.Z;
        if (lift > s.MostLift) return (0f, Vector3.UnitZ);
        found = true;
        var n = ToModel(h.Normal, sin, cos);
        float least = s.LeastFootNormalZ;
        if (n.Z < least)
        {
            float flat = MathF.Sqrt(n.X * n.X + n.Y * n.Y), k = MathF.Sqrt(1f - least * least) / MathF.Max(flat, 1e-6f);
            n = new Vector3(n.X * k, n.Y * k, least);
        }
        return (MathF.Max(lift, -s.MostDrop), n);
    }

    /// <summary>How high above its standing height (m) a person's ankle may be in its clip and still count as on the ground for the hips: fully at it, none from here.</summary>
    public const float StanceHeight = 0.08f;

    /// <summary>
    /// A person's foot's ground (m above the body's feet) and its normal (model space), and how much the clip has the foot on the ground
    /// (0 to 1: its ankle at its standing height over the clip's own ground, none from <see cref="StanceHeight"/> above): the ground looked
    /// for under the ankle and under the ball, the higher taken (a foot over a riser is put on the tread above it, its toes not into the
    /// riser; with the stairs' rule, under the ball a little ahead along the motion too: <paramref name="ahead"/>); less the clip's own slope there (<paramref name="tan"/>: its rise a metre ahead, by its share of the pose), and the normal
    /// turned back by that slope, so the IK lays on the rest.
    /// </summary>
    (float Lift, Vector3 Normal, float Stand) PersonGroundUnder(LevelSpace level, Vector3 ankle, Vector3 ball, Vector3 ahead, Vector3 feet, float sin, float cos, float tan, float ankleHeight,
                                                              float stairs = 0f)
    {
        var (lift, n) = GroundUnder(level, ankle, feet, sin, cos);
        var (atBall, _) = GroundUnder(level, ball, feet, sin, cos, out bool underBall);
        if (underBall && atBall > lift) lift = atBall;
        if (ahead != ball && GroundUnder(level, ahead, feet, sin, cos, out bool underAhead).Lift is var atAhead && underAhead && atAhead > lift) lift = atAhead;
        float own = ankle.Y * tan + stairs;   // and the stair clip's own tread there
        float stand = Math.Clamp(1f - (ankle.Z - ankleHeight - own) / StanceHeight, 0f, 1f);
        if (tan != 0f)
        {
            // The normal turned back by the clip's own slope (about the legs' across): on the clip's slope it is up.
            float c = 1f / MathF.Sqrt(1f + tan * tan), sn = tan * c;
            n = LimbIK.Unit(new Vector3(n.X, n.Y * c + n.Z * sn, -n.Y * sn + n.Z * c));
            if (n == Vector3.Zero) n = Vector3.UnitZ;
        }
        return (lift - own, n, stand);
    }

    /// <summary>
    /// A person's ankles and balls of the feet (model space) at <paramref name="seconds"/> into <paramref name="clipIndex"/> (from the
    /// tables of the clips that stand for it, as <see cref="Ankles"/>), with its slope clip <paramref name="slopeClip"/> laid on by
    /// <paramref name="slopeWeight"/> at the same share of its cycle.
    /// </summary>
    bool PersonFeetAt(CharacterKind kind, int kindIndex, int clipIndex, float seconds, LimbMap limbs, int slopeClip, float slopeWeight,
                      out Vector3 left, out Vector3 right, out Vector3 ballLeft, out Vector3 ballRight, int stairClip = -1, float stairWeight = 0f)
    {
        var person = kind.Person!;
        if (person.Sources(assets.Clip(clipIndex), seconds, out int a, out float atA, out int b, out float atB, out float w))
        {
            var ta = Track(kindIndex, a, limbs)!;
            (left, right) = ta.At(atA);
            (ballLeft, ballRight) = ta.Balls(atA);
            if (w > 0f)
            {
                var tb = Track(kindIndex, b, limbs)!;
                var (lb, rb) = tb.At(atB);
                var (blb, brb) = tb.Balls(atB);
                (left, right, ballLeft, ballRight) = (PoseMath.Lerp(left, lb, w), PoseMath.Lerp(right, rb, w), PoseMath.Lerp(ballLeft, blb, w), PoseMath.Lerp(ballRight, brb, w));
            }
        }
        else if (Track(kindIndex, clipIndex, limbs) is { } t) { (left, right) = t.At(seconds); (ballLeft, ballRight) = t.Balls(seconds); }
        else { (left, right, ballLeft, ballRight) = (default, default, default, default); return false; }
        if (slopeClip >= 0 && slopeWeight > 0f)
        {
            float d = assets.Clip(clipIndex).Duration, p = d > 0f ? seconds / d : 0f;
            p -= MathF.Floor(p);
            var ts = Track(kindIndex, slopeClip, limbs)!;
            float at = p * ts.Duration;
            var (ls, rs) = ts.At(at);
            var (bls, brs) = ts.Balls(at);
            left = PoseMath.Lerp(left, ls, slopeWeight);
            right = PoseMath.Lerp(right, rs, slopeWeight);
            ballLeft = PoseMath.Lerp(ballLeft, bls, slopeWeight);
            ballRight = PoseMath.Lerp(ballRight, brs, slopeWeight);
        }
        if (stairClip >= 0 && stairWeight > 0f)
        {
            // The stair clip's feet at the same share of its cycle, by its share.
            float d = assets.Clip(clipIndex).Duration, p = d > 0f ? seconds / d : 0f;
            p -= MathF.Floor(p);
            var ts = Track(kindIndex, stairClip, limbs)!;
            float at = p * ts.Duration;
            var (ls, rs) = ts.At(at);
            var (bls, brs) = ts.Balls(at);
            left = PoseMath.Lerp(left, ls, stairWeight);
            right = PoseMath.Lerp(right, rs, stairWeight);
            ballLeft = PoseMath.Lerp(ballLeft, bls, stairWeight);
            ballRight = PoseMath.Lerp(ballRight, brs, stairWeight);
        }
        return true;
    }

    /// <summary>How far a foot's ground found on a flight may be from its stair clip's own tread there, drawn on the flight (m).</summary>
    public const float StairFootBand = 0.03f;

    /// <summary>A unit direction eased towards another by <paramref name="t"/> (zero reads as up).</summary>
    static Vector3 Ease(Vector3 from, Vector3 to, float t)
    {
        if (from == Vector3.Zero) from = Vector3.UnitZ;
        var v = LimbIK.Unit(PoseMath.Lerp(from, to, t));
        return v == Vector3.Zero ? Vector3.UnitZ : v;
    }

    /// <summary>
    /// The ankles the motion has at <paramref name="seconds"/> into <paramref name="clipIndex"/> (model space). for a
    /// person's own clips (a gait's blend, a turn played for some degrees) from the tables of the clips that stand for them
    /// (<see cref="People.PersonMotion.Sources"/>: a blend's two at the same share of their cycles, by its weight), so no table is
    /// made when a body first goes some new way.
    /// </summary>
    bool Ankles(CharacterKind kind, int kindIndex, int clipIndex, float seconds, LimbMap limbs, out Vector3 left, out Vector3 right)
    {
        if (kind.Person is { } person && person.Sources(assets.Clip(clipIndex), seconds, out int a, out float atA, out int b, out float atB, out float w))
        {
            var (la, ra) = Track(kindIndex, a, limbs)!.At(atA);
            if (w <= 0f) { (left, right) = (la, ra); return true; }
            var (lb, rb) = Track(kindIndex, b, limbs)!.At(atB);
            (left, right) = (PoseMath.Lerp(la, lb, w), PoseMath.Lerp(ra, rb, w));
            return true;
        }
        if (Track(kindIndex, clipIndex, limbs) is { } track) { (left, right) = track.At(seconds); return true; }
        (left, right) = (default, default);
        return false;
    }

    AnkleTrack? Track(int kindIndex, int clipIndex, LimbMap limbs)
    {
        long key = ((long)kindIndex << 32) | (uint)clipIndex;
        if (tracks.TryGetValue(key, out var t)) return t;
        // First sight of this kind: a table for every clip it plays, so later changes of motion allocate nothing.
        var kind = assets.Kind(kindIndex);
        foreach (var m in kind.Motions)
            if (m.Clip >= 0) Build(kindIndex, m.Clip, limbs);
        if (kind.Person is { } person) foreach (int c in person.PoseClips) Build(kindIndex, c, limbs);   // what stands for a person's own clips
        if (!tracks.ContainsKey(key)) Build(kindIndex, clipIndex, limbs);
        return tracks[key];
    }

    void Build(int kindIndex, int clipIndex, LimbMap limbs)
    {
        long key = ((long)kindIndex << 32) | (uint)clipIndex;
        if (tracks.ContainsKey(key)) return;
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        var clip = assets.Clip(clipIndex);
        int n = skeleton.Count;
        if (local.Length < n) { local = new BoneTransform[n]; model = new Matrix4x4[n]; }
        int frames = Math.Max(1, (int)MathF.Ceiling(clip.Duration * 30f) + 1);
        var left = new Vector3[frames];
        var right = new Vector3[frames];
        // A person's balls of the feet too (its toes looked for over a riser).
        bool balls = assets.Kind(kindIndex).Person is not null && limbs.LeftLeg.Tip >= 0 && limbs.RightLeg.Tip >= 0;
        var leftBall = balls ? new Vector3[frames] : null;
        var rightBall = balls ? new Vector3[frames] : null;
        for (int f = 0; f < frames; f++)
        {
            clip.Sample(MathF.Min(f / 30f, clip.Duration), skeleton, local.AsSpan(0, n));
            PoseEvaluator.ToModel(skeleton, local.AsSpan(0, n), model.AsSpan(0, n));
            left[f] = limbs.LeftLeg.Has ? model[limbs.LeftLeg.End].Translation : Vector3.Zero;
            right[f] = limbs.RightLeg.Has ? model[limbs.RightLeg.End].Translation : Vector3.Zero;
            if (balls) { leftBall![f] = model[limbs.LeftLeg.Tip].Translation; rightBall![f] = model[limbs.RightLeg.Tip].Translation; }
        }
        tracks[key] = new AnkleTrack(left, right, clip.Duration, clip.Loops, leftBall, rightBall);
    }

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    /// <summary>0 outside [a, b], rising to 1 at its middle and back (a parabola).</summary>
    internal static float Bump(float p, float a, float b)
    {
        if (p <= a || p >= b) return 0f;
        float t = (p - a) / (b - a);
        return 4f * t * (1f - t);
    }

    /// <summary>0 before a, 1 after b, eased between (smoothstep).</summary>
    internal static float Ramp(float p, float a, float b)
    {
        if (p <= a) return 0f;
        if (p >= b) return 1f;
        float t = (p - a) / (b - a);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Rising over [a, b], held, falling over [c, d].</summary>
    internal static float Window(float p, float a, float b, float c, float d) => Ramp(p, a, b) * (1f - Ramp(p, c, d));
}
