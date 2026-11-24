// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>What happened to a body in one tick that the rules' traces want to know (turned into events by <see cref="BodySystem"/>).</summary>
public struct MotorOutcome
{
    /// <summary>A move that began this tick (<see cref="TraversalKind.None"/> when none), and one that ended and whether it ran to its end.</summary>
    public TraversalKind MoveStarted, MoveEnded;
    public bool MoveCompleted;
    /// <summary>The hands took a lip this tick, or let go of one.</summary>
    public bool Grabbed, Dropped;
    /// <summary>The lip and the feet of a grab or a drop, and the lip's height above the ground left.</summary>
    public Vector3 GripLip, GripFeet;
    public float GripHeight;
    public bool JumpAttempted, AttemptOnGround;
    public bool TookOff, TookOffCoyote, TookOffBuffered;
    public Vector3 TakeOffFeet, TakeOffVelocity;
    public bool Apex;
    public float ApexHeight;
    public Vector3 ApexFeet;
    public bool Landed;

    /// <summary>
    /// The commit point in the air committed this tick (<see cref="AirOutcome.None"/> when not): to what, the seconds to its contact foreseen, the lip
    /// (a catch's or one fallen past, a top cleared), the way the body went at it (+1 rising, −1 falling) and whether a catch was the jump's own.
    /// </summary>
    public AirOutcome Committed;
    public float CommitSeconds;
    public Vector3 CommitLip;
    public int CommitWay;
    public bool CommitAuto;
    /// <summary>Why a lip within reach was not caught (<see cref="AirOutcome.Past"/>), none otherwise.</summary>
    public CatchRefused CommitWhy;

    /// <summary>On a landing on the spring, the speed across dropped at once (m/s; <c>BodyMotor.Landed</c>); 0 otherwise.</summary>
    public float LandingDropped;
    public float ImpactSpeed, FallHeight, AirSeconds;
    public int LandTriangle;
    /// <summary>The edge guard kept the body from moving over a drop this tick (it slid along the edge or stopped).</summary>
    public bool Guarded;
    /// <summary>The drop the guard met (m; +∞ for none within <see cref="BodyMotor.DropLook"/>).</summary>
    public float GuardedDrop;
    /// <summary>Cancel took effect this tick: the hands let go, or a move was undone.</summary>
    public bool Cancelled;
    /// <summary>A jump took off this tick (with <see cref="TookOff"/>): tapped or charged, how high it lifts the feet (m), and how long it was charged (s).</summary>
    public bool Launched;
    public JumpKind LaunchKind;
    public float LaunchHeight, ChargedSeconds;
    /// <summary>A charge's step this tick: begun, let go or ended without a jump; its height then and the most it reaches (m).</summary>
    public bool Charge;
    public ChargeStep ChargeStep;
    public float ChargeHeight, ChargeMost;
    /// <summary>A move refused at a face marked not to be climbed (<see cref="TraversalKind.None"/> when none): the lip and its height above the ground left.</summary>
    public TraversalKind Refused;
    public Vector3 RefusedLip;
    public float RefusedHeight;
    /// <summary>
    /// BodyMotor.NoStand.cs: the body rested on a no-stand top this tick (in the air: it is never ground) and was
    /// slid towards its edge along <see cref="NoStandWay"/>, every tick until it is off; <see cref="NoStandMet"/>: it came
    /// down onto one this tick, faster than <see cref="BodyMotor.NoStandMetSpeed"/> (the landing a top that may be stood
    /// on would have had, refused; <see cref="ImpactSpeed"/> is not set for it).
    /// </summary>
    public bool OnNoStand, NoStandMet;
    /// <summary>The level way (a unit vector) the body is slid off a no-stand top, while <see cref="OnNoStand"/>.</summary>
    public Vector2 NoStandWay;
    /// <summary>The point of the edge the guard met (its top's height; for <see cref="EdgeStopped"/>).</summary>
    public Vector3 GuardedEdge;
    /// <summary>A lowering ended standing at the foot of the drop this tick (<see cref="BodyMotor.ClimbsDown"/>): where it began, the lip, the drop.</summary>
    public bool ClimbedDown;
    public Vector3 ClimbFrom, ClimbLip;
    public float ClimbDrop;
    /// <summary>
    /// BodyMotor.Ladder.cs: use put the body onto a ladder this tick (<see cref="LadderEnd.Foot"/> or
    /// <see cref="LadderEnd.Top"/>: from where), or it left one (<see cref="LadderOff"/>: how); which ladder (its index in
    /// the level's list) and where the feet were.
    /// </summary>
    public LadderEnd LadderOn;
    public LadderLeave LadderOff;
    public int LadderIndex;
    public Vector3 LadderFeet;
    /// <summary>
    /// A ladder used this tick that the clips do not fit (1 + its index, 0 none): its lean off both sets' own by more than their tolerance, or its top too
    /// far off the climb's rungs — climbed on the earlier build's stand-in (the session says so once) — with its lean (degrees) and its top's floor off the rungs (m).
    /// </summary>
    public int LadderStandIn;
    public float LadderLean;
    public float LadderTopOff;
    /// <summary>
    /// A leap or a corner of the hang began this tick (its kind, a leap's which, a corner's outside or not, its way, whether braced, the feet, the hang it
    /// ends at, the lip there), or one asked for was not taken (why, which, about where).
    /// </summary>
    public HangMoveKind HangStarted;
    public HangLeap HangLeap;
    public bool HangOutside, HangBraced;
    public int HangWay;
    public Vector3 HangFrom, HangTo, HangLip;
    public HangRefusal HangRefused;
    public HangMoveKind HangRefusedKind;
    public Vector3 HangAt;
    /// <summary>
    /// A wall's move began this tick (what, a move's way and pace, a corner's outside, whether the face is sheer, the feet and where the capsule ends), or
    /// one asked for was not taken (why, which, about where; once a place).
    /// </summary>
    public WallAct WallStarted;
    public int WallLeft, WallUp;
    public byte WallPace;
    public bool WallOutside, WallSheer;
    public Vector3 WallFrom, WallTo;
    public WallRefusal WallRefused;
    public WallAct WallRefusedAct;
    public Vector3 WallAt;
    /// <summary>
    /// A squeeze's or a cover's move began this tick (what, a step's or a turn's way, where from, a squeeze's gap), or one was not taken (why, which, about
    /// where; once a place).
    /// </summary>
    public SideAct SideStarted;
    public int SideWay;
    public Vector3 SideFrom;
    public float SideWidth;
    public SideRefusal SideRefused;
    public SideAct SideRefusedAct;
    public Vector3 SideAt;
    /// <summary>
    /// Hanging, the body began to shimmy along the lip this tick (+1 to its right, −1 to its left, as it faces the
    /// wall), or stopped (<see cref="ShimmyStop"/>: why); the lip and the feet where it did; and a push-off from a lip.
    /// </summary>
    public int ShimmyStarted;
    public ShimmyStop ShimmyStopped;
    public Vector3 ShimmyLip, ShimmyFeet;
    public bool PushedOffLip;
    /// <summary>
    /// The speed across the spring asked of a body on it this tick, on the ground, before the move met anything (m/s; 0 on the
    /// ramp): a body on the spring setting off from rest goes slower than <see cref="BodyMotor.StopSpeed"/> for its first ticks, so its
    /// stop is told only once the spring asks more than that (<see cref="BodyMotor.StoppedAgainst(in Body, Vector3, in MoveIntent, float)"/>).
    /// </summary>
    public float SpringAsked;
}

/// <summary>
/// <b>The body's motor</b> (ours): one tick of one body, a pure function of the
/// body, its numbers, its intent, the level's collision, the other bodies' capsules and the tick. Nothing
/// else is read and nothing is allocated.
///
/// <para>The body is a <b>kinematic capsule</b> kept <see cref="Skin"/> clear of everything it touches.
/// <b>On the ground</b> its speed goes towards the wished one at <see cref="Locomotion.AccelerationCapacity"/>
/// (or down at <see cref="Locomotion.BrakingDeceleration"/>) — or, for a body on the spring (the player's, <see cref="StartStopSpring"/>),
/// as a critically damped spring of its half-life (<see cref="Spring"/>) —, and the move, laid along the ground's plane, is
/// swept and <b>slid</b> along what it meets, up to <see cref="MaxSlides"/> times: ground it can stand on
/// (a slope up to the steepest) carries it on up, anything else is a wall whose upright part takes away the
/// motion into it, and two walls meeting stop it in their corner. A move a wall blocked is tried again
/// <b>a step higher</b> (up, along, and down onto what is there), and when that fails and the wish heads into
/// the wall, again with a <see cref="StepReach"/> stride into it; it is taken when it gets further and
/// the tread is no more than <see cref="Locomotion.StepHeight"/> above the feet. After the move the ground
/// is looked for under the body as far as a step down, so it walks down stairs and slopes instead of
/// floating off them; finding none it is in the air, with <see cref="Locomotion.CoyoteSeconds"/> of grace in
/// which a jump still takes off.</para>
///
/// <para><b>Footing.</b> The ground is found by sweeping the capsule down. Where its rounded bottom rests on a
/// face, that face is the ground if it is no steeper than <see cref="Locomotion.SteepestGroundDegrees"/>
/// (so a slope of 46° is slid down, in the air, and one of 44° is stood on). Where it rests on an edge or a
/// corner — a kerb, a tread's nosing, a roof's ridge — the face just beyond the edge decides, and the body
/// stands <b>level with the edge</b>: its footing is the disc of its radius, as a person's feet are, not the
/// point at the bottom of a ball, so it stands on a step's edge until its middle is past it, steps up onto a
/// tread as soon as its capsule meets the riser, and never rolls off a kerb. An edge is footing only well
/// inside the disc, except the one the body stands level with as it walks off it (to the rim, so a 0.4 m riser
/// is walked down without leaving the ground). A <b>nosing cut back</b> by a chamfer — a face or edge steeper
/// than 40° with a tread just beyond it (<see cref="NosingReach"/>) — is stood level with that tread, as a
/// sharp nosing is, whether or not the chamfer itself could be stood on (1.1's stairs).</para>
///
/// <para><b>In the air</b> gravity acts (integrated exactly for a constant pull, so a jump's apex is the
/// same √(2gh)²/2g at every tick rate's sampling), steering has <see cref="Locomotion.AirControl"/> of the
/// ground's acceleration and never brakes, and the move slides along everything. Landing is meeting ground
/// while not going up; it is reported with the speed and the height of the fall. Jump takes off from the
/// ground — a tap with <see cref="Locomotion.TapImpulse"/> upwards (a human's), a hold of a body that can charge with
/// what it charged (the partial <c>BodyMotor.Jump.cs</c>) —, also within the grace after an edge, and a
/// press up to <see cref="Locomotion.JumpBufferSeconds"/> before landing takes off on landing.</para>
///
/// <para><b>Other bodies</b> are capsules too: met as walls (their sum with this one swept as a ray), never
/// stood on, and pushed out of when overlapped. Each tick starts and ends by pushing the body out of
/// anything it is inside (<see cref="CollisionWorld.Depenetrate"/>).</para>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How far the capsule is kept from what it touches (m).</summary>
    public const float Skin = 0.002f;

    /// <summary>How many times a tick's move is slid along what it meets.</summary>
    public const int MaxSlides = 4;

    /// <summary>A sprint is run instead when the wished way is further than this from the facing (the cosine of 50°).</summary>
    public const float SprintCone = 0.6428f;

    /// <summary>How far beyond an edge the face that decides it is looked for, and how near the edge's height it must be (m).</summary>
    const float EdgeLook = 0.02f, EdgeTolerance = 0.025f;

    /// <summary>
    /// How far beyond a steep contact (a stair's nosing) its tread is looked for (m), in steps of
    /// <see cref="EdgeLook"/>: a nosing is often cut back — 1.1's by a 45° chamfer 4 or 8 units (5.6 or 11 cm)
    /// wide, some a little steeper — and the tread it belongs to lies just past it, no higher than it is far.
    /// </summary>
    public const float NosingReach = 0.16f;

    /// <summary>
    /// A face or an edge steeper than this (the cosine of 40°) is taken for a nosing when a tread lies just beyond
    /// it, whether or not it could be stood on; a tread is a face no steeper than this.
    /// </summary>
    public const float NosingNormalZ = 0.766f;

    /// <summary>
    /// How far into what blocked it a step up is tried on as well (m), when trying it along the body's own move
    /// fails and the wished way heads into it: a stride, so a body walking slowly, starting from a stand, or
    /// sliding along a riser taller than its radius (whose wall takes its speed into the step every tick) still
    /// gets its footing well onto the tread rather than only grazing its edge.
    /// </summary>
    public const float StepReach = 0.02f;

    /// <summary>
    /// How far the wished way must head into what blocked the body for the stride to be tried: at least 30° off
    /// along it (the cosine of 60°). A body brushing by at a shallower angle slides along, as earlier, and is
    /// not lifted onto a block a step high it only grazes (the East Gate's beam).
    /// </summary>
    public const float StrideCone = 0.5f;

    /// <summary>
    /// How far above its feet a body in the air may land (m): no stride is taken in the air beyond this, so
    /// a plain jump lands only on what its feet clear (the human's jump of 0.5 m on tops to about 0.6 m). Before
    /// Once a body landed level with any edge its capsule's rounded bottom met, up to its radius above the feet,
    /// and a plain jump stood the human on tops to about 0.85 m.
    /// </summary>
    public const float AirStride = 0.1f;

    /// <summary>How much a face's normal may round below the steepest ground's and the face still be ground.</summary>
    const float GroundRounding = 2e-5f;

    const float Eps = 1e-5f;

    /// <summary>A yaw turned toward <paramref name="target"/> the short way round by at most <paramref name="step"/> radians (any step at once when it is 0 or less).</summary>
    public static float TurnToward(float yaw, float target, float step)
    {
        float delta = DetMath.WrapAngle(target - yaw);
        if (step <= 0f || MathF.Abs(delta) <= step) return target;
        return yaw + (delta > 0f ? step : -step);
    }

    /// <summary>
    /// The level, the others and this body's numbers, for one tick, with the capsule's height now
    /// (<see cref="Body.CapsuleHeight"/>). <see cref="MinN"/> is the least upward part of a normal stood on
    /// (<see cref="Locomotion.SlideDegrees"/>), <see cref="WalkN"/> the least walked up
    /// (<see cref="Locomotion.SteepestGroundDegrees"/>).
    /// </summary>
    readonly ref struct Mover
    {
        public readonly CollisionWorld Level;
        public readonly ReadOnlySpan<BodyCapsule> Others;
        public readonly ulong Self;
        public readonly float R, H, Seg, MinN, WalkN, Step;

        public Mover(CollisionWorld level, ReadOnlySpan<BodyCapsule> others, ulong self, in Locomotion loco, float height)
        {
            Level = level; Others = others; Self = self;
            R = loco.Radius; H = MathF.Max(height, 2f * loco.Radius); Seg = H - 2f * R;
            // The steepest ground counts as ground: a face of exactly 45° (1.1's chamfers and ramps) is one
            // whichever way its normal rounds.
            MinN = loco.MinStandNormalZ - GroundRounding; Step = MathF.Max(0f, loco.StepHeight);
            WalkN = MathF.Max(MinN, loco.MinGroundNormalZ - GroundRounding);
        }

        public Capsule At(Vector3 feet) => new(feet + new Vector3(0, 0, R), Seg, R);

        /// <summary>
        /// A body given the clip-carried moves and the edges' clips lets go of an edge it stands level with (a ledge, not a step: no ground
        /// within a step under its middle) when its middle passes over it — the walk-off's and the run-off's take-off at the edge — rather than when
        /// its whole disc has (BodyMotor.Over.cs).
        /// </summary>
        public bool LeavesAtEdge { get; init; }

        /// <summary>
        /// With the ledge's clips, crouch held towards an edge lowers to hang from it as earlier (BodyMotor.Edges.cs): the body keeps the edge's
        /// footing for that move, so the lowering is measured before <see cref="LeavesAtEdge"/> lets it go (the earlier build's walk-off took the crouched walk over the
        /// edge instead, and the player never lowered).
        /// </summary>
        public bool LowersAtEdge { get; init; }
    }

    /// <summary>What a sweep met: the level (a triangle) or another body.</summary>
    struct Hit
    {
        public float Distance;
        public Vector3 Normal;
        public int Triangle;
        public bool IsBody;
    }

    /// <summary>The ground found under a body: where its feet go, the ground's normal, and the triangle; whether it stands level with an edge (a ledge, a tread's edge, a nosing) rather than on a face.</summary>
    struct Support
    {
        public float FeetZ;
        public Vector3 Normal;
        public int Triangle;
        public bool Edge;
    }

    /// <summary>
    /// One tick of <paramref name="body"/> as the plain body — stride, slide, jump, fall and crouch, but no
    /// traversal moves (the snag check's walking, and callers with no <see cref="Traversal"/> to keep).
    /// </summary>
    public static MotorOutcome Step(ref Body body, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, float chargeWalk = 0f)
    {
        var none = default(Traversal);
        var ramp = default(StartStopSpring);
        return Step(ref body, ref none, ref ramp, loco, intent, level, others, self, tick, moves: false, chargeWalk);
    }

    /// <summary>
    /// The plain body (as above) starting and stopping on <paramref name="spring"/> on the ground (its half-life 0: the ramp):
    /// the snag check's walking on the spring. <paramref name="landingSlide"/>: a landing on the spring as before (see below).
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, float chargeWalk = 0f, bool landingSlide = false)
    {
        var none = default(Traversal);
        return Step(ref body, ref none, ref spring, loco, intent, level, others, self, tick, moves: false, chargeWalk, landingSlide);
    }

    /// <summary>
    /// One tick of <paramref name="body"/> and the move it makes (<paramref name="move"/>, written every tick).
    /// <paramref name="others"/> are every body's capsule (this one's among them is known by
    /// <paramref name="self"/> and skipped). With <paramref name="moves"/> false no move starts. <paramref name="chargeWalk"/>:
    /// the walk a charging body is held to when above nothing — a paced body's own walk
    /// (<see cref="Paced"/>: the charge keeps its cap) —, else <paramref name="loco"/>'s.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, bool moves = true, float chargeWalk = 0f)
    {
        var ramp = default(StartStopSpring);
        return Step(ref body, ref move, ref ramp, loco, intent, level, others, self, tick, moves, chargeWalk);
    }

    /// <summary>
    /// One tick of <paramref name="body"/> and its move (as above), its speed on the ground following <paramref name="spring"/> (:
    /// a critically damped spring of the spring's half-life toward the wished velocity, <see cref="Spring"/>) when its
    /// half-life is above 0, else the earlier build's fixed ramp; the spring's acceleration is its state, kept on the ground and zero whenever the
    /// body is not on the ground (the air keeps <see cref="Locomotion.AirControl"/> × the ramp), making a move, hanging or on a ladder.
    /// A body on the spring that lands from the air keeps only its speed along the way it is asked to go, up
    /// to the pace asked (none when nothing is asked), the rest dropped at once, so it does not slide on (<see cref="Landed"/>); the
    /// spring works on from there. <paramref name="landingSlide"/> (the switch <c>landing-slide</c>): the landing's speed kept
    /// whole, as before, for the spring to take up. The ramp's landing is as ever.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, bool moves = true, float chargeWalk = 0f,
                                    bool landingSlide = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref System.Runtime.CompilerServices.Unsafe.NullRef<OverMove>(), null,
             moves, chargeWalk, landingSlide);

    /// <summary>
    /// As the overload before, for a body given the clip-carried moves (<paramref name="over"/>, its state, a null reference for any other body;
    /// <paramref name="paths"/>, its clips, null for none: BodyMotor.Over.cs): a press let go to start one starts it, one under way runs along its
    /// clip's path, and a vault, a climb onto or a mantle the traversal's own detection starts in the air is carried by its clip from the hands' contact.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, bool moves = true,
                                    float chargeWalk = 0f, bool landingSlide = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref System.Runtime.CompilerServices.Unsafe.NullRef<LedgeMove>(),
             null, moves, chargeWalk, landingSlide);

    /// <summary>
    /// As the overload before, for a body given the ledge's clips too (<paramref name="ledge"/>, its state, a null reference for any other body;
    /// <paramref name="ledges"/>, its clips, null for none: BodyMotor.Ledge.cs): the hang, its shimmy, braces, pull-up and drop on the clips' paths from the
    /// hang's place, the grabs from standing, running and falling on theirs, and the motor's own grabs and lowerings ending in the clip's hang.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, bool moves = true, float chargeWalk = 0f, bool landingSlide = false, bool autoVault = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref ledge, ledges,
             ref System.Runtime.CompilerServices.Unsafe.NullRef<AirCommit>(), null, moves, chargeWalk, landingSlide, autoVault);

    /// <summary>
    /// As the overload before, for a body given the commit point in the air too (<paramref name="commit"/>, its state, a null reference for any other body;
    /// <paramref name="commitRules"/>, its numbers, null for none: BodyMotor.Commit.cs), with the ledge's clips: in the air the outcome foreseen within the
    /// commit's lead committed to and a catch carried by its clip from then (no motor's stand-in grab from the air, nothing caught from a fall without use
    /// held), no move from the air at a top the arc clears, and a grab ending free braced by a wall under the edge before its end.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, ref AirCommit commit, AirCommitRules? commitRules, bool moves = true, float chargeWalk = 0f,
                                    bool landingSlide = false, bool autoVault = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref ledge, ledges, ref commit, commitRules,
             ref System.Runtime.CompilerServices.Unsafe.NullRef<LadderClipMove>(), null, null, moves, chargeWalk, landingSlide, autoVault);

    /// <summary>
    /// As the overload before, for a body given tier 1's moves too (<paramref name="tier"/>, their numbers, null for none: the earlier build's): the braced twins of
    /// the grabs (with the commit point's <see cref="AirCommitRules.BracedTwins"/>), the run's own low vaults, and the ladders on their clips
    /// (<paramref name="climb"/>, the ladder's state, a null reference for any other body; <paramref name="ladders"/>, the clips, null for none:
    /// BodyMotor.LadderClips.cs).
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, ref AirCommit commit, AirCommitRules? commitRules, ref LadderClipMove climb, LadderPaths? ladders,
                                    MovesRules? tier, bool moves = true, float chargeWalk = 0f, bool landingSlide = false, bool autoVault = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref ledge, ledges, ref commit, commitRules, ref climb, ladders,
             tier, ref System.Runtime.CompilerServices.Unsafe.NullRef<HangMove>(), moves, chargeWalk, landingSlide, autoVault);

    /// <summary>
    /// As the overload before, for a body given the hang's corners and leaps too (<paramref name="hang"/>, their state, a null reference for any other body;
    /// their numbers <paramref name="tier"/>'s <see cref="MovesRules.Hang"/>, null for none: the earlier build's): BodyMotor.Hang.cs.
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, ref AirCommit commit, AirCommitRules? commitRules, ref LadderClipMove climb, LadderPaths? ladders,
                                    MovesRules? tier, ref HangMove hang, bool moves = true, float chargeWalk = 0f, bool landingSlide = false, bool autoVault = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref ledge, ledges, ref commit, commitRules, ref climb, ladders,
             tier, ref hang, ref System.Runtime.CompilerServices.Unsafe.NullRef<WallMove>(), false, moves, chargeWalk, landingSlide, autoVault);

    /// <summary>
    /// As the overload before, for a body given the walls too (<paramref name="wall"/>, its state, a null reference for any other body; the numbers
    /// <paramref name="tier"/>'s <see cref="MovesRules.Wall"/>, null for none: the earlier build's): BodyMotor.Wall.cs. <paramref name="sheer"/>: whether a face with nothing to
    /// hold may be climbed now (the body out of the masquerade, or in a free place).
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, ref AirCommit commit, AirCommitRules? commitRules, ref LadderClipMove climb, LadderPaths? ladders,
                                    MovesRules? tier, ref HangMove hang, ref WallMove wall, bool sheer, bool moves = true, float chargeWalk = 0f, bool landingSlide = false,
                                    bool autoVault = false) =>
        Step(ref body, ref move, ref spring, loco, intent, level, others, self, tick, ref over, paths, ref ledge, ledges, ref commit, commitRules, ref climb, ladders,
             tier, ref hang, ref wall, sheer, ref System.Runtime.CompilerServices.Unsafe.NullRef<WallFlow>(), ref System.Runtime.CompilerServices.Unsafe.NullRef<SideMove>(), null, false,
             moves, chargeWalk, landingSlide, autoVault);

    /// <summary>
    /// As the overload before, with the wall's flow too (<paramref name="flow"/>, its state, a null reference for the earlier build's: then its numbers in
    /// <see cref="MovesRules.Wall"/> are the earlier build's, and nothing of the earlier build's is done), and for a body given squeezing and cover (<paramref name="side"/>, its
    /// state, a null reference for any other body; its numbers <paramref name="sides"/>, null for none: the earlier build's): BodyMotor.Side.cs. <paramref name="stealth"/>:
    /// whether the body is in stealth (cover only in it).
    /// </summary>
    public static MotorOutcome Step(ref Body body, ref Traversal move, ref StartStopSpring spring, in Locomotion loco, in MoveIntent intent, CollisionWorld level,
                                    ReadOnlySpan<BodyCapsule> others, ulong self, long tick, ref OverMove over, OverPaths? paths, ref LedgeMove ledge,
                                    LedgePaths? ledges, ref AirCommit commit, AirCommitRules? commitRules, ref LadderClipMove climb, LadderPaths? ladders,
                                    MovesRules? tier, ref HangMove hang, ref WallMove wall, bool sheer, ref WallFlow flow, ref SideMove side, SideRules? sides,
                                    bool stealth, bool moves = true, float chargeWalk = 0f, bool landingSlide = false, bool autoVault = false)
    {
        _ = tick;   // the outcome is stamped by the caller; the motor itself is the same on every tick
        bool carried = paths is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref over);
        bool ledgeOn = ledges is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref ledge);
        bool commitOn = ledgeOn && commitRules is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref commit);
        bool climbOn = ladders is not null && tier is { Ladders: true } && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref climb);
        bool hangOn = ledgeOn && commitOn && tier?.Hang is not null && ledges!.HasHangMoves && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref hang);
        bool wallOn = hangOn && tier!.Wall is not null && ledges!.HasWalls && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref wall);
        // The flow's state (none: the earlier build's numbers, so nothing of the earlier build's: a scratch one written and forgotten).
        var noFlow = default(WallFlow);
        bool flowOn = wallOn && tier!.Wall!.Flows && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref flow);
        ref var wf = ref flowOn ? ref flow : ref noFlow;
        var wr = wallOn ? (flowOn || !tier!.Wall!.Flows ? tier!.Wall! : WallRules.NoFlow(tier!.Wall!)) : null;
        bool sideOn = wallOn && sides is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref side);
        const float dt = World.TickSeconds;
        var o = default(MotorOutcome);
        var feet = body.Feet;
        if (ledgeOn && ledge.Again > 0f) ledge.Again = MathF.Max(0f, ledge.Again - dt);   // after a drop
        if (wallOn && wall.Again > 0f) wall.Again = MathF.Max(0f, wall.Again - dt);       // after leaving a wall
        // On the ground nothing is committed to (a lip fallen past is forgotten); a catch's flight only while its grab carries the capsule.
        if (commitOn && body.State == BodyState.Ground) { commit.Outcome = 0; commit.PastWay = 0; commit.Ticks = 0; commit.Catch = 0; }

        // ---- jump held: how long this press has lasted, and the grace after it is let go
        bool down = intent.JumpHeld || intent.Jump;
        if (intent.Jump) body.JumpHeldSeconds = 0f;
        if (down) { body.JumpHeldSeconds += dt; body.JumpGrace = HeldGrace; }
        else body.JumpGrace = MathF.Max(0f, body.JumpGrace - dt);

        if (body.State == BodyState.Traversing || body.State == BodyState.Hanging || body.State == BodyState.Ladder)
        {
            spring.Acceleration = default;   // the spring rests while a move, a hang or a ladder carries the body
            var mm = new Mover(level, others, self, loco, body.CapsuleHeight(loco));
            if (!moves || move.Kind == TraversalKind.None)
            {
                if (ledgeOn) { ledge.Clip = 0; ledge.Into = 0; }
                if (climbOn) climb = default;
                Abort(mm, ref body, ref move, ref feet, loco, ref o);
            }
            else if (climbOn && climb.Clip > 0)   // a ladder on its clips
                LadderClipTick(mm, ref body, ref move, ref climb, ladders!, ref feet, loco, intent, ref o, tier!, ref ledge, ledgeOn ? ledges : null);
            else if (sideOn && ledge.Clip > 0 && ledges!.At(ledge.Clip - 1) is { OnSide: true } onSide)   // squeezing or in cover on their clips
                SideTick(mm, ref body, ref move, ref ledge, ledges, onSide, ref side, sides!, stealth, ref feet, loco, intent, ref o);
            else if (wallOn && ledge.Clip > 0 && ledges!.At(ledge.Clip - 1) is { OnWall: true } onWall)   // a wall on its clips
                WallTick(mm, ref body, ref move, ref ledge, ledges, onWall, ref wall, ref wf, wr!, sheer, ref feet, loco, intent, ref o, commitOn ? commitRules : null);
            else if (climbOn && ledgeOn && ledge.Clip > 0 && body.State == BodyState.Hanging && !(hangOn && hang.Kind != HangMoveKind.None)
                     && ledges!.At(ledge.Clip - 1) is not { OnWall: true }
                     && TryFromHang(mm, ref body, ref move, ref ledge, ledges!, ref climb, ladders!, ref feet, loco, intent, tier!, ref o)) { }   // back onto a ladder
            else if (ledgeOn && ledge.Clip > 0 && body.State != BodyState.Ladder)
                LedgeTick(mm, ref body, ref move, ref ledge, ledges!, ref feet, loco, intent, ref o, ref commit, commitOn ? commitRules : null,
                          ref hang, hangOn ? tier!.Hang : null);   // a catch's flight; the hang's corners and leaps
            else if (body.State == BodyState.Ladder) LadderTick(mm, ref body, ref move, ref feet, loco, intent, ref o);
            else if (intent.Cancel && CanCancel(body, move)) CancelMove(mm, ref body, ref move, ref feet, loco, intent, ref o);
            else if (body.State == BodyState.Hanging) HangTick(mm, ref body, ref move, ref feet, loco, intent, ref o);
            else if (carried && over.Clip > 0 && paths!.At(over.Clip - 1) is { } clip) OverAdvance(mm, ref body, ref move, ref over, clip, ref feet, loco, intent, ref o);
            else
            {
                var ended = move.Kind;
                Advance(mm, ref body, ref move, ref feet, loco, intent, ref o, clipHang: ledgeOn && ledge.Into != 0);
                // The motor's own grab or lowering ended in the clip's hang place: its hang on the clip.
                if (ledgeOn && ledge.Into != 0 && body.State == BodyState.Hanging && move.Kind == TraversalKind.Hang)
                    LedgeHangAfter(mm, ref body, ref move, ref ledge, ledges!, ref feet, loco, intent, ended, ref o);
            }
            if (carried && move.Kind == TraversalKind.None) over.Clip = 0;   // ended, cancelled or stopped
            if (ledgeOn && ledge.Clip == 0 && move.Kind is not (TraversalKind.Grab or TraversalKind.LowerToHang)) ledge.Into = 0;
            if (commitOn && (ledge.Clip == 0 || move.Kind != TraversalKind.Grab)) commit.Catch = 0;   // a catch's flight ends with its grab
            if (climbOn && climb.Clip > 0 && move.Kind is not (TraversalKind.LadderOn or TraversalKind.Ladder or TraversalKind.LadderOff or TraversalKind.PullUp))
                climb = default;   // a ladder's clip ends with its move
            if (hangOn && (ledge.Clip == 0 || body.State is not (BodyState.Hanging or BodyState.Traversing))) hang = default;   // a leap's, a corner's, their carry, with the hang
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }

        // ---- crouch: lowered at once; stood up again only where a standing body fits (and held down by a charge)
        bool charging = ChargeHeld(body, intent);
        if (intent.Crouch || charging) body.Crouched = 1;
        else if (body.Crouched != 0 && Fits(level, feet, loco.Radius, loco.Height)) body.Crouched = 0;

        var m = new Mover(level, others, self, loco, body.CapsuleHeight(loco)) { LeavesAtEdge = carried, LowersAtEdge = ledgeOn };   // off a ledge at its edge; crouched, lowered from it
        Depenetrate(m, ref feet);
        // Use at a ladder's foot or top puts the body on it (BodyMotor.Ladder.cs).
        // On its clips (the foot, the top, a landing beside it), unless the ladder fits neither set (told).
        if (moves && intent.Use && body.State == BodyState.Ground && climbOn
            && TryUseLadderClips(m, ref body, ref move, ref climb, ladders!, ref feet, loco, tier!, ref o))
        {
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }
        if (moves && intent.Use && body.State == BodyState.Ground && TryUseLadder(m, ref body, ref move, ref feet, loco, intent, ref o))
        {
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }

        // ---- the wish
        var wish = intent.Direction;
        float wl = wish.Length();
        if (wl > 1f) { wish /= wl; wl = 1f; }
        float yaw = intent.Faces ? intent.Facing
                  : wl > 0.01f ? TurnToward(body.Yaw, DetMath.Atan2(wish.Y, wish.X), loco.TurnDegreesPerSecond * DetMath.DegToRad * dt)
                  : body.Yaw;
        body.Yaw = DetMath.WrapAngle(yaw);
        var gait = intent.Gait;
        if (gait >= Gait.Sprint && wl > 0f)
        {
            var (s, c) = DetMath.SinCos(body.Yaw);
            if (Vector2.Dot(wish / wl, new Vector2(c, s)) < SprintCone) gait = Gait.Run;
        }
        if (body.Crouched != 0 && gait >= Gait.Sprint) gait = Gait.Run;
        float pace = body.Crouched != 0 ? MathF.Min(loco.CrouchSpeed, loco.SpeedOf(gait)) : loco.SpeedOf(gait);
        if (charging) pace = MathF.Min(pace, chargeWalk > 0f ? chargeWalk : loco.WalkSpeed);
        var target = wish * pace;

        // ---- grace timers and the jump
        if (body.State == BodyState.Air) body.Coyote = MathF.Max(0f, body.Coyote - dt);
        body.JumpBuffer = MathF.Max(0f, body.JumpBuffer - dt);
        if (intent.Jump)
        {
            body.JumpBuffer = loco.JumpBufferSeconds;
            o.JumpAttempted = true;
            o.AttemptOnGround = body.State == BodyState.Ground;
        }
        var vh = new Vector2(body.Velocity.X, body.Velocity.Y);
        var vhBefore = vh;
        bool jumped = false;
        // A tap on the press for the human, on the release for a body that can charge; held, a charge.
        bool wasHeld = body.Charging != 0;
        var go = JumpPress(ref body, loco, intent, down, ref o);
        // A press let go to start a clip-carried move (BodyMotor.Over.cs): instead of the jump.
        // The run's own vault (Go 2) has no press: it starts as the hold ends; given up, nothing happens.
        if (carried && moves && over.Go != 0 && (go == JumpGo.Tap || over.Go == 2) && body.State == BodyState.Ground
            && TryOverFromGround(m, ref body, ref move, ref over, paths!, ref feet, loco, intent, wl > 0.1f ? wish / wl : Facing(body.Yaw), ref o,
                                 low: autoVault && tier is { LowVaults: true }))   // the run's own low vault
        {
            over.Go = 0;
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }
        if (carried) over.Go = 0;
        // A press let go to start a grab from the ground (BodyMotor.Ledge.cs): instead of the jump.
        if (ledgeOn && moves && ledge.Go != 0 && go == JumpGo.Tap && body.State == BodyState.Ground
            && TryLedgeFromGround(m, ref body, ref move, ref ledge, ledges!, ref feet, loco, intent, wl > 0.1f ? wish / wl : Facing(body.Yaw), ref o,
                                  twins: commitOn && commitRules!.BracedTwins))   // the braced twin
        {
            ledge.Go = 0;
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }
        if (ledgeOn) ledge.Go = 0;
        // A tap at a wall whose face goes on up past the grabs' bands gets onto it (BodyMotor.Wall.cs), instead of the jump.
        if (wallOn && moves && go == JumpGo.Tap && body.State == BodyState.Ground
            && TryWallFromGround(m, ref body, ref move, ref ledge, ledges!, ref wall, ref wf, wr!, sheer, ref feet, loco, wl > 0.1f ? wish / wl : Facing(body.Yaw), ref o))
        {
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }
        if (moves && go == JumpGo.Tap && body.State == BodyState.Ground
            && TryHopUp(m, ref body, ref move, ref feet, loco, intent, wl > 0.1f ? wish / wl : Vector2.Zero, ref o))
        {
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }
        float impulse = go == JumpGo.Charged ? ChargedImpulse(body, loco) : loco.TapImpulse;
        if (go != JumpGo.None && (body.State == BodyState.Ground || body.Coyote > Eps) && impulse > 0f)
        {
            jumped = true;
            body.Jumped = 1;
            o.TookOff = true;
            o.TookOffCoyote = body.State == BodyState.Air;
            o.TookOffBuffered = !intent.Jump && go == JumpGo.Tap && !wasHeld;
            o.TakeOffFeet = feet;
            body.State = BodyState.Air;
            body.Velocity.Z = impulse;
            body.Coyote = 0f; body.JumpBuffer = 0f; body.AirSeconds = 0f;
            body.TakeOffZ = feet.Z; body.HighestZ = feet.Z; body.Rising = 1;
            body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
            Launch(ref body, loco, go, impulse, ref o);
        }

        // ---- speed
        if (body.State == BodyState.Ground)
        {
            if (spring.HalfLife > 0f)
            {
                vh = Spring(vh, ref spring.Acceleration, target, spring.HalfLife, dt);   // the spring
                o.SpringAsked = vh.Length();
            }
            else
            {
                float rate = wl > 0f && target.LengthSquared() >= vh.LengthSquared() ? loco.AccelerationCapacity : loco.BrakingDeceleration;
                vh = MoveToward(vh, target, rate * dt);
            }
        }
        else
        {
            spring.Acceleration = default;   // the spring rests in the air
            if (wl > 0f)
            {
                // Air control adds speed towards the wish up to the gait's, and never takes any away.
                var way = wish / wl;
                float add = pace * wl - Vector2.Dot(vh, way);
                if (add > 0f) vh += way * MathF.Min(add, loco.AccelerationCapacity * loco.AirControl * dt);
            }
        }
        if (jumped) o.TakeOffVelocity = new Vector3(vh, body.Velocity.Z);

        // ---- the moves that start from running or flying: the pace's step-over and sprint vault; a vault, a climb, a mantle or a grab from a jump
        if (moves && !jumped)
        {
            var aim = wl > 0.1f ? wish / wl : Facing(body.Yaw);
            // Falling past a lip, the falling grab's clip first; none for a moment after a drop.
            bool again = ledgeOn && ledge.Again > 0f;
            // The commit point first (a catch begun), then the moves from the air with no grab of their own and none at a top the arc clears.
            bool started = body.State == BodyState.Ground
                ? body.Crouched == 0 && wl > 0.5f && !(carried && over.Pending == 2)   // not while the run's own vault is held
                  && TryPaceMove(m, ref body, ref move, ref feet, loco, intent, gait, vh, ref o, carried && autoVault ? paths : null, low: tier is { LowVaults: true })
                : commitOn
                    ? !again && ((wallOn && TryWallFromAir(m, ref body, ref move, ref ledge, ledges!, ref wall, ref wf, wr!, sheer, ref feet, loco, intent, ref o))   // a wall caught
                                 || CommitTick(m, ref body, ref move, ref ledge, ledges!, ref commit, commitRules!, ref feet, loco, intent, aim, vh, ref o)
                                 || TryFromAir(m, ref body, ref move, ref feet, loco, intent, aim, ref o, carried ? paths : null, null, arc: new Vector3(vh, body.Velocity.Z)))
                    : !again && ((ledgeOn && TryGrabFall(m, ref body, ref move, ref ledge, ledges!, ref feet, loco, intent, aim, ref o))
                                 || TryFromAir(m, ref body, ref move, ref feet, loco, intent, aim, ref o, carried ? paths : null, ledgeOn ? ledges : null));
            if (started)
            {
                if (carried && body.State == BodyState.Traversing) OverFromAir(m, ref body, ref move, ref over, paths!, ref feet, loco, intent, ref o);
                // The motor's own grab under way to the clip's hang place (TryGrab put it there).
                if (ledgeOn && ledge.Clip == 0 && move.Kind == TraversalKind.Grab)
                    ledge.Into = move.To == OwnMoveHang(ledges!, loco, move.Lip, move.LipNormal, move.Lip.Z) ? 1 : 0;
                body.Feet = feet;
                body.CrouchWas = intent.Crouch ? 1 : 0;
                return o;
            }
        }

        // A gap walked into squeezed through, and in stealth a wall behind taken as cover (BodyMotor.Side.cs).
        if (sideOn && moves && !jumped && TrySide(m, ref body, ref move, ref ledge, ledges!, ref side, sides!, stealth, ref feet, loco, wish, wl, ref o))
        {
            body.Feet = feet;
            body.CrouchWas = intent.Crouch ? 1 : 0;
            return o;
        }

        // ---- the move
        bool wasAir = body.State == BodyState.Air;
        if (body.State == BodyState.Ground)
        {
            // The edge guard the intent asks for, and crouch held over an edge lowering the body to hang from it.
            if (GuardedMove(m, ref body, ref move, ref feet, vh, wl > 0.01f ? wish / wl : Vector2.Zero, loco, intent, moves, ref o))
            {
                // A lowering that will hang goes down to the clip's hang place (the lowering itself as before).
                if (ledgeOn && move.Kind == TraversalKind.LowerToHang && !ClimbsDown(move, loco)
                    && OwnMoveHang(ledges!, loco, move.Lip, move.LipNormal, move.Lip.Z) is var place && Fits(level, place, loco.Radius, loco.Height))
                {
                    move.To = place;
                    ledge.Into = 1;
                }
                body.Feet = feet;
                body.CrouchWas = intent.Crouch ? 1 : 0;
                return o;
            }
        }
        else MoveInAir(m, ref body, ref feet, vhBefore, vh, loco, ref o);
        if (o.Landed) { body.Jumped = 0; body.Powered = 0; }
        if (o.Landed && spring.HalfLife > 0f && !landingSlide) Landed(ref body, ref spring, wl > 0f ? wish / wl : Vector2.Zero, pace * wl, ref o);
        if (body.State == BodyState.Ground) body.RefusalTold = 0;

        Depenetrate(m, ref feet);
        // Off the ground the head comes up again as soon as it fits (as a crouch's does).
        if (body.State != BodyState.Ground && body.Stoop > 0f && Fits(level, feet, loco.Radius, body.FullHeight(loco))) body.Stoop = 0f;
        body.Feet = feet;
        if (body.State == BodyState.Ground) body.GroundSeconds += dt;
        body.CrouchWas = intent.Crouch ? 1 : 0;

        // ---- a drop from a lip lasts a moment, or until the body lands
        if (move.Kind == TraversalKind.Drop)
        {
            float t = move.Seconds > 0f ? move.Progress + dt / move.Seconds : 1f;
            if (t >= 1f || (wasAir && body.State == BodyState.Ground))
            {
                o.MoveEnded = TraversalKind.Drop; o.MoveCompleted = true;
                move = default;
            }
            else move.Progress = t;
        }
        else if (move.Kind != TraversalKind.None) move = default;
        return o;
    }

    /// <summary>
    /// (<i>"a, with the qualifier that it should keep directional motion if the player is still
    /// moving. But no sliding, there I agree."</i>): a body on the spring landing. With nothing asked (<paramref name="way"/> zero) its
    /// speed across stops at once, as before ramp braked a landing; asked to go a way, it keeps the part of its velocity along that way,
    /// from none (never backward) up to <paramref name="pace"/> (the speed asked), and the rest — sideways, backward, beyond the pace —
    /// is dropped at once. The spring's acceleration is at rest (it rested in the air), so the spring takes it on from there. The
    /// speed dropped is <see cref="MotorOutcome.LandingDropped"/>.
    /// </summary>
    static void Landed(ref Body body, ref StartStopSpring spring, Vector2 way, float pace, ref MotorOutcome o)
    {
        var across = new Vector2(body.Velocity.X, body.Velocity.Y);
        var kept = way == Vector2.Zero ? Vector2.Zero : way * Math.Clamp(Vector2.Dot(across, way), 0f, pace);
        o.LandingDropped = (across - kept).Length();
        body.Velocity = new Vector3(kept, body.Velocity.Z);
        spring.Acceleration = default;
    }

    /// <summary>The level unit way a yaw faces.</summary>
    static Vector2 Facing(float yaw)
    {
        var (s, c) = DetMath.SinCos(yaw);
        return new Vector2(c, s);
    }

    /// <summary>Whether a body of <paramref name="height"/> fits standing at <paramref name="feet"/> (its capsule a skin up, inside nothing).</summary>
    static bool Fits(CollisionWorld level, Vector3 feet, float radius, float height) =>
        !level.Overlaps(Capsule.OfBody(feet + new Vector3(0, 0, Skin * 0.5f), radius, height), 1e-4f);

    static Vector2 MoveToward(Vector2 from, Vector2 to, float by)
    {
        var d = to - from;
        float l = d.Length();
        return l <= by || l < 1e-9f ? to : from + d * (by / l);
    }

    static void MoveOnGround(in Mover m, ref Body body, ref Vector3 feet, Vector2 vh, Vector2 way, in Locomotion loco)
    {
        const float dt = World.TickSeconds;
        var n = body.GroundNormal.Z > 0.05f ? body.GroundNormal : Vector3.UnitZ;
        if (n.Z < m.WalkN)
        {
            // Ground too steep to walk up (between the walked and the slid): its uphill part is taken away.
            var downhill = new Vector2(n.X, n.Y);
            float dl = downhill.Length();
            if (dl > 1e-4f)
            {
                downhill /= dl;
                float up = -Vector2.Dot(vh, downhill);
                if (up > 0f) vh += downhill * up;
            }
        }
        var d = OnPlane(new Vector3(vh.X * dt, vh.Y * dt, 0f), n);
        var vel = new Vector3(vh, 0f);
        var start = feet;
        var plain = start;
        var saved = body;
        float full = body.FullHeight(loco);
        bool blocked = Slide(m, ref plain, d, n, ref vel, ground: true, out var wall);
        var end = plain;
        bool stepped = false;
        if (blocked && m.Step > 0f)
        {
            // The step tried along the body's own move, then — when the wished way heads into what blocked it —
            // with a stride into it as well; swept with the head as low as a stoop may keep it (a step), and the
            // tread then held to the head as it will be there (a body once rose a whole riser, head and all,
            // with its middle still over the tread below, and stopped under a slab 2 m over the nosings).
            var low = new Mover(m.Level, m.Others, m.Self, loco, full - m.Step);
            var across = new Vector3(d.X, d.Y, 0f);
            stepped = TryStep(low, start, across, out var onto, out bool edge) && Across(start, onto) > Across(start, plain) + 1e-4f
                      && HeadFits(m, onto, full, edge);
            var into = new Vector2(-wall.X, -wall.Y);
            if (!stepped && Vector2.Dot(way, into) > StrideCone)
                stepped = TryStep(low, start, across + new Vector3(into * StepReach, 0f), out onto, out edge)
                          && Across(start, onto) > Across(start, plain) + 1e-4f && HeadFits(m, onto, full, edge);
            if (stepped)
            {
                end = onto;
                vel = new Vector3(vh, 0f);
            }
        }
        feet = end;

        if (Probe(m, feet, m.Step + m.R + Skin, keeping: true, out var s) && s.FeetZ >= feet.Z - m.Step - 1e-4f && s.FeetZ <= feet.Z + m.Step + 1e-4f
            && !(m.LeavesAtEdge && s.Edge && OverTheEdge(m, feet)))   // a person's middle over a ledge's edge lets go
        {
            feet.Z = s.FeetZ > feet.Z ? RaiseTo(m, feet, s.FeetZ) : s.FeetZ;
            // The head kept down standing level with an edge over lower ground; raised only where it fits, and the
            // move not taken where it does not (the body stops with its middle short of where it would stand up).
            float stoop = s.Edge ? StoopAt(m, feet) : 0f;
            if ((stoop < body.Stoop - 1e-4f || stepped) && !Fits(m.Level, feet, m.R, MathF.Max(full - stoop, 2f * m.R)))
            {
                body = saved;
                feet = start;
                body.Velocity = Vector3.Zero;
                return;
            }
            body.Stoop = stoop;
            body.GroundNormal = s.Normal;
            body.GroundTriangle = s.Triangle;
            body.Velocity = new Vector3(vel.X, vel.Y, 0f);
        }
        else if (StoppedAtNoStand(m, start, feet))
        {
            // Walked onto a no-stand top, met as a slope too steep: the move is taken back (BodyMotor.NoStand.cs).
            body = saved;
            feet = start;
            body.Velocity = Vector3.Zero;
        }
        else
        {
            // Walked off: in the air, with the grace of an edge.
            body.State = BodyState.Air;
            body.Coyote = loco.CoyoteSeconds;
            body.AirSeconds = 0f; body.GroundSeconds = 0f;
            body.HighestZ = feet.Z; body.TakeOffZ = feet.Z; body.Rising = 0;
            body.Velocity = new Vector3(vel.X, vel.Y, 0f);
            body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
        }
    }

    /// <summary>The most a body may still be rising (m/s) and land: a rounding error's worth.</summary>
    const float LandingRise = 1e-3f;

    static void MoveInAir(in Mover m, ref Body body, ref Vector3 feet, Vector2 vhBefore, Vector2 vh, in Locomotion loco, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        float g = loco.Gravity, vz0 = body.Velocity.Z, vz1 = vz0 - g * dt;
        // Exact for a constant pull: the mean of the tick's speeds across, and the parabola's own rise.
        var avg = (vhBefore + vh) * 0.5f;
        var d = new Vector3(avg.X * dt, avg.Y * dt, vz0 * dt - 0.5f * g * dt * dt);
        var vel = new Vector3(vh, vz1);
        float downSpeed = -vz1;
        Slide(m, ref feet, d, Vector3.UnitZ, ref vel, ground: false, out _);
        body.AirSeconds += dt;
        if (feet.Z > body.HighestZ) body.HighestZ = feet.Z;

        if (body.Rising == 1 && vel.Z <= 0f)
        {
            o.Apex = true;
            o.ApexHeight = body.HighestZ - body.TakeOffZ;
            o.ApexFeet = feet;
            body.Rising = 0;
        }

        // "not rising" allows a hair of rise. Meeting a floor across one of its triangles' seams, the contact's
        // normal tilts by a rounding error and the slide along it can leave +2e-6 m/s: that once put the landing off a
        // tick, and a run into the proving ground's pit was told as a 4 m fall at 0.1 m/s.
        if (vel.Z <= LandingRise && Probe(m, feet, 4f * Skin, keeping: false, out var s) && s.FeetZ >= feet.Z - 4f * Skin && s.FeetZ <= feet.Z + AirStride + 1e-3f
            && !(m.LeavesAtEdge && s.Edge && OverTheEdge(m, feet)))   // nor met again from the air while its middle is over the drop
        {
            feet.Z = s.FeetZ > feet.Z ? RaiseTo(m, feet, s.FeetZ) : s.FeetZ;
            o.Landed = true;
            o.ImpactSpeed = MathF.Max(0f, downSpeed);
            o.FallHeight = body.HighestZ - feet.Z;
            o.AirSeconds = body.AirSeconds;
            o.LandTriangle = s.Triangle;
            body.LastLandingSpeed = o.ImpactSpeed;
            body.LastFallHeight = o.FallHeight;
            body.State = BodyState.Ground;
            body.GroundSeconds = 0f;
            body.Coyote = 0f;
            body.Rising = 0;
            body.GroundNormal = s.Normal;
            body.GroundTriangle = s.Triangle;
            vel.Z = 0f;
        }
        if (!o.Landed) SlideOffNoStand(m, feet, body.Yaw, downSpeed, ref vel, ref o);   // a no-stand top is slid off (BodyMotor.NoStand.cs)
        body.Velocity = vel;
    }

    /// <summary>The horizontal distance between two points.</summary>
    static float Across(Vector3 a, Vector3 b) => new Vector2(b.X - a.X, b.Y - a.Y).Length();

    /// <summary>A move across laid on a plane (its height made to follow the plane), keeping its course across.</summary>
    static Vector3 OnPlane(Vector3 d, Vector3 n) => new(d.X, d.Y, n.Z > 0.05f ? -(n.X * d.X + n.Y * d.Y) / n.Z : 0f);

    /// <summary>
    /// Sweeps <paramref name="feet"/> along <paramref name="d"/> and slides along what it meets. On the
    /// ground (<paramref name="ground"/>), standable contacts carry the move on along their plane and the
    /// rest are walls (their upright part only, so a body never climbs or digs by sliding); in the air every
    /// contact's plane takes away the motion into it. Returns whether a wall was met; on the ground
    /// <paramref name="wall"/> is the first one's normal, laid level (zero when none).
    /// </summary>
    static bool Slide(in Mover m, ref Vector3 feet, Vector3 d, Vector3 groundN, ref Vector3 vel, bool ground, out Vector3 wall)
    {
        bool blocked = false;
        wall = Vector3.Zero;
        Vector3 first = default;
        bool hasFirst = false;
        for (int i = 0; i < MaxSlides; i++)
        {
            float len = d.Length();
            if (len < 1e-7f) break;
            var dir = d / len;
            if (!Sweep(m, feet, dir, len + Skin, out var hit)) { feet += d; break; }
            float into = -Vector3.Dot(dir, hit.Normal);
            float travel = Math.Clamp(into > 1e-3f ? hit.Distance - Skin / into : hit.Distance - Skin, 0f, len);
            feet += dir * travel;
            if (travel >= len - 1e-7f) break;
            var rem = d - dir * travel;
            var n = hit.Normal;
            if (ground)
            {
                if (!hit.IsBody && n.Z >= m.WalkN)
                {
                    groundN = n;
                    d = OnPlane(rem, n);
                    continue;
                }
                blocked = true;
                var w = new Vector3(n.X, n.Y, 0f);
                float wl = w.Length();
                if (wl < 1e-4f) break;                    // a flat ceiling met walking: stop
                w /= wl;
                rem -= Vector3.Dot(rem, w) * w;
                if (Vector3.Dot(vel, w) < 0f) vel -= Vector3.Dot(vel, w) * w;
                if (hasFirst && Vector3.Dot(rem, first) < -1e-7f) break;   // into a corner of two walls
                if (!hasFirst) wall = w;
                first = w; hasFirst = true;
                d = OnPlane(rem, groundN);
            }
            else
            {
                if (!hit.IsBody && n.Z < m.MinN) blocked = true;
                if (!hit.IsBody && n.Z > 1e-4f && n.Z < m.MinN)
                {
                    // Met low on the body by what cannot be stood on (an edge, a steep face): the motion across is
                    // stopped as a wall's (its upright part), never turned upwards, and only a fall into it is slid
                    // off along it — so a body in the air takes no stride up an edge (it once rode up the
                    // edge of a top 0.85 m high).
                    var w = new Vector3(n.X, n.Y, 0f);
                    float wl = w.Length();
                    w /= wl;
                    rem = Level(rem, n, w);
                    vel = Level(vel, n, w);
                }
                else
                {
                    rem -= Vector3.Dot(rem, n) * n;
                    if (Vector3.Dot(vel, n) < 0f) vel -= Vector3.Dot(vel, n) * n;
                }
                if (hasFirst && Vector3.Dot(rem, first) < -1e-7f)
                {
                    var crease = Vector3.Cross(first, n);
                    float cl = crease.Length();
                    if (cl < 1e-4f) break;
                    crease /= cl;
                    rem = crease * Vector3.Dot(rem, crease);
                    vel = crease * Vector3.Dot(vel, crease);
                }
                first = n; hasFirst = true;
                d = rem;
            }
        }
        return blocked;
    }

    /// <summary>
    /// A motion <paramref name="d"/> in the air against a contact low on the body that cannot be stood on (normal
    /// <paramref name="n"/>, laid level <paramref name="w"/>): its part across loses what goes into the contact,
    /// as against a wall; its part downwards, when it goes into the contact, slides off along it.
    /// </summary>
    static Vector3 Level(Vector3 d, Vector3 n, Vector3 w)
    {
        var across = new Vector3(d.X, d.Y, 0f);
        float into = Vector3.Dot(across, w);
        if (into < 0f) across -= into * w;
        var down = new Vector3(0f, 0f, d.Z);
        float fall = Vector3.Dot(down, n);
        if (fall < 0f) down -= fall * n;
        return across + down;
    }

    /// <summary>The move <paramref name="across"/> tried a step higher: up as far as the step (or a ceiling) lets, along, and down onto a tread no higher than a step above <paramref name="start"/>.</summary>
    static bool TryStep(in Mover m, Vector3 start, Vector3 across, out Vector3 result, out bool edge)
    {
        result = start; edge = false;
        float up = m.Step;
        if (Sweep(m, start, Vector3.UnitZ, up + Skin, out var roof)) up = MathF.Max(0f, roof.Distance - Skin);
        if (up < 0.01f) return false;
        var p = start + new Vector3(0, 0, up);
        var vel = Vector3.Zero;
        Slide(m, ref p, across, Vector3.UnitZ, ref vel, ground: true, out _);
        if (!Probe(m, p, up + m.R + Skin, keeping: false, out var s)) return false;
        if (s.FeetZ > start.Z + m.Step + 1e-4f || s.FeetZ > p.Z + 1e-4f || s.FeetZ < start.Z - m.Step - 1e-4f) return false;
        if (s.FeetZ > start.Z + 1e-3f && s.Normal.Z < m.WalkN) return false;   // never stepped up onto ground too steep to walk up
        result = new Vector3(p.X, p.Y, s.FeetZ);
        edge = s.Edge;
        return true;
    }

    /// <summary>
    /// How far a body standing at <paramref name="feet"/> level with an edge keeps its head down (<see cref="Body.Stoop"/>):
    /// how far its feet are above the ground under its middle, to a step (a step when there is none within it).
    /// </summary>
    static float StoopAt(in Mover m, Vector3 feet)
    {
        if (m.Step <= 0f) return 0f;
        const float above = 0.02f;
        if (m.Level.Ray(new Vector3(feet.X, feet.Y, feet.Z + above), -Vector3.UnitZ, above + m.Step + Skin) is not { } under) return m.Step;
        if (m.Level.Normal(under.Triangle).Z <= 0f) return 0f;     // inside something, or under it: no stoop
        return Math.Clamp(feet.Z - Skin - under.Point.Z, 0f, m.Step);
    }

    /// <summary>Whether a body of <paramref name="full"/> height fits standing at <paramref name="feet"/> with its head kept down as it would be there.</summary>
    static bool HeadFits(in Mover m, Vector3 feet, float full, bool edge) =>
        Fits(m.Level, feet, m.R, MathF.Max(full - (edge ? StoopAt(m, feet) : 0f), 2f * m.R));

    /// <summary>Lifts the feet to <paramref name="z"/>, or as far as a ceiling lets.</summary>
    static float RaiseTo(in Mover m, Vector3 feet, float z)
    {
        float lift = z - feet.Z;
        if (lift <= 0f) return z;
        return Sweep(m, feet, Vector3.UnitZ, lift + Skin, out var roof) ? feet.Z + MathF.Max(0f, MathF.Min(lift, roof.Distance - Skin)) : z;
    }

    /// <summary>
    /// The ground under a body standing at <paramref name="feet"/>, within <paramref name="distance"/>
    /// below: the capsule swept down; a face it rests on that can be stood on, or an edge or corner it rests
    /// on whose face beyond (away from the body's middle) can be — the body then stands level with the edge.
    /// <paramref name="keeping"/> is the ground-keeping after a move on the ground, where the edge the body
    /// stands level with (the one it is walking off) is footing anywhere under its disc.
    /// </summary>
    static bool Probe(in Mover m, Vector3 feet, float distance, bool keeping, out Support support) =>
        Footing(m, feet, distance, keeping, out support) && Standable(m, support);   // a no-stand top is no footing (BodyMotor.NoStand.cs)

    /// <summary>The footing <see cref="Probe"/> finds, whether or not it may be stood on (<see cref="Standable"/> decides that).</summary>
    static bool Footing(in Mover m, Vector3 feet, float distance, bool keeping, out Support support)
    {
        support = default;
        var cap = m.At(feet);
        if (m.Level.CapsuleSweep(cap, -Vector3.UnitZ, distance) is not { } hit) return false;
        var nc = hit.Normal; var nf = hit.FaceNormal;
        // The way from the body's middle out to where it rests, across.
        var off = new Vector2(hit.Point.X - feet.X, hit.Point.Y - feet.Y);
        float dx = off.Length();
        var away = dx > 1e-4f ? off / dx : Vector2.Zero;
        // Met off the middle and well inside the body's disc, or level with its feet (the edge it is walking
        // off, which is footing anywhere under it): where a contact can be footing at all. A ledge or a beam
        // only grazed at the rim of the disc never is.
        bool level = keeping && hit.Point.Z <= feet.Z + 1e-3f && hit.Point.Z >= feet.Z - EdgeTolerance;
        bool inside = dx > 1e-4f && (dx <= m.R * 0.98f || level);
        if (Vector3.Dot(nc, nf) > 0.9998f)
        {
            // Resting on a steep face off the middle with a tread just beyond: a stair's cut-back nosing.
            // The body stands level with that tread, as on a sharp nosing, whether or not the chamfer itself
            // could be stood on (1.1's 45° ones round either way), so it never rides down or up the chamfer.
            if (nf.Z < NosingNormalZ && inside && TreadBeyond(m, hit.Point, away, out float tz, out var tn, out int tt))
            {
                support = new Support { FeetZ = MathF.Max(tz, hit.Point.Z) + Skin, Normal = tn, Triangle = tt, Edge = true };
                return true;
            }
            // Resting on the face, the skin clear of it — lifted to the skin when nearer: a body put
            // exactly on a floor, its bottom in the plane, once stayed there and caught on the floor's own
            // triangle edges as it walked.
            if (nf.Z < m.MinN) return false;
            float gap = Skin / MathF.Max(nf.Z, 0.05f);
            support = new Support { FeetZ = feet.Z - (hit.Distance - gap), Normal = nf, Triangle = hit.Triangle };
            return true;
        }
        // On an edge or a corner: the faces either side of it decide. Ground on both sides is a crease in
        // the ground, rested on; ground on one side only is a ledge or a tread's edge, stood level with.
        // The edge it stands level with is footing anywhere under the disc, so even a step of 0.4 m is walked
        // down without leaving the ground.
        if (!inside && dx > 1e-4f) return false;
        var look = away * EdgeLook;
        bool beyond = FaceAt(m, hit.Point, look, out var n, out int under);
        float levelZ = hit.Point.Z;
        bool nosing = false;
        if ((!beyond || n.Z < NosingNormalZ) && inside && TreadBeyond(m, hit.Point, away, out float bz, out var bn, out int bt))
        {
            // The edge at the foot of a cut-back nosing: its tread, a little higher and further, decides.
            beyond = nosing = true; n = bn; under = bt; levelZ = MathF.Max(bz, hit.Point.Z);
        }
        var nearN = Vector3.UnitZ; int nearT = -1;
        bool near = dx > 1e-4f && FaceAt(m, hit.Point, -look, out nearN, out nearT);
        if (!beyond && !near) return false;
        // The edge between a chamfer on the middle's side and its tread beyond is the nosing's, not a crease.
        if (beyond && near && nearN.Z < NosingNormalZ && n.Z >= NosingNormalZ) nosing = true;
        if (beyond && near && nc.Z >= m.MinN && !nosing)
        {
            // A crease within the ground (ground on both sides of the edge): resting on it, as on a face.
            float gap = Skin / MathF.Max(nc.Z, 0.05f);
            support = new Support { FeetZ = feet.Z - (hit.Distance - gap), Normal = nc, Triangle = under };
            return true;
        }
        if (!beyond) { n = nearN; under = nearT; levelZ = hit.Point.Z; }
        // A ledge or a tread's edge: standing level with it.
        support = new Support { FeetZ = levelZ + Skin, Normal = n, Triangle = under, Edge = true };
        return true;
    }

    /// <summary>
    /// Whether a tread lies just beyond <paramref name="contact"/> (a point on a steep face or edge)
    /// along <paramref name="away"/>, within <see cref="NosingReach"/>: looking down every
    /// <see cref="EdgeLook"/>, the first face no steeper than <see cref="NosingNormalZ"/>, no lower than the
    /// contact and no higher above it than it is far from it (so a chamfer of about 45° climbs to it and a
    /// long slope or a wall never does). Steeper faces on the way (the nosing itself) are looked past; the
    /// level falling away, or a face seen from inside a solid, ends the look.
    /// </summary>
    static bool TreadBeyond(in Mover m, Vector3 contact, Vector2 away, out float z, out Vector3 normal, out int triangle)
    {
        z = contact.Z; normal = Vector3.UnitZ; triangle = -1;
        for (int i = 1; i * EdgeLook <= NosingReach + 1e-4f; i++)
        {
            float look = i * EdgeLook;
            float top = look + EdgeTolerance;
            var from = new Vector3(contact.X + away.X * look, contact.Y + away.Y * look, contact.Z + top);
            if (m.Level.Ray(from, -Vector3.UnitZ, top + EdgeTolerance) is not { } under) return false;
            if (m.Level.Normal(under.Triangle).Z <= 0f) return false;
            if (under.Normal.Z < NosingNormalZ) continue;
            z = under.Point.Z; normal = under.Normal; triangle = under.Triangle;
            return true;
        }
        return false;
    }

    /// <summary>Whether a face that can be stood on lies at the height of <paramref name="edge"/> just <paramref name="look"/> across from it.</summary>
    static bool FaceAt(in Mover m, Vector3 edge, Vector2 look, out Vector3 normal, out int triangle)
    {
        normal = Vector3.UnitZ; triangle = -1;
        var from = new Vector3(edge.X + look.X, edge.Y + look.Y, edge.Z + 0.05f);
        if (m.Level.Ray(from, -Vector3.UnitZ, 0.05f + EdgeTolerance) is not { } under) return false;
        var n = under.Normal.Z < 0f ? -under.Normal : under.Normal;
        if (n.Z < m.MinN || MathF.Abs(under.Point.Z - edge.Z) > EdgeTolerance) return false;
        normal = n; triangle = under.Triangle;
        return true;
    }

    /// <summary>The first thing the body's capsule meets moving from <paramref name="feet"/> along unit <paramref name="dir"/> within <paramref name="length"/>: the level or another body.</summary>
    static bool Sweep(in Mover m, Vector3 feet, Vector3 dir, float length, out Hit hit)
    {
        hit = default;
        bool found = false;
        var cap = m.At(feet);
        if (m.Level.CapsuleSweep(cap, dir, length) is { } h)
        {
            hit = new Hit { Distance = h.Distance, Normal = h.Normal, Triangle = h.Triangle };
            found = true;
            length = h.Distance;
        }
        var p = cap.Lower;
        foreach (ref readonly var other in m.Others)
        {
            if (other.Entity == m.Self) continue;
            float rr = m.R + other.Radius;
            float oseg = MathF.Max(0f, other.Height - 2f * other.Radius);
            // Their sum: the other's segment lengthened down by this one's, grown by this one's radius.
            var lower = other.Feet + new Vector3(0, 0, other.Radius - m.Seg);
            float seg = oseg + m.Seg;
            float reach = length + rr;
            if (MathF.Abs(lower.X - p.X) > reach || MathF.Abs(lower.Y - p.Y) > reach) continue;
            if (SweepMath.RayCapsule(p, dir, length, lower, seg, rr) is not { } t || (found && t >= hit.Distance)) continue;
            var at = p + dir * t;
            var axis = new Vector3(lower.X, lower.Y, Math.Clamp(at.Z, lower.Z, lower.Z + seg));
            var n = at - axis;
            float nl = n.Length();
            hit = new Hit { Distance = t, Normal = nl > 1e-6f ? n / nl : -dir, Triangle = -1, IsBody = true };
            found = true;
            length = t;
        }
        return found;
    }

    /// <summary>Pushes the body out of the level and out of the other bodies.</summary>
    static void Depenetrate(in Mover m, ref Vector3 feet)
    {
        var cap = m.At(feet);
        m.Level.Depenetrate(ref cap, Skin * 0.5f);
        var p = cap.Lower;
        foreach (ref readonly var other in m.Others)
        {
            if (other.Entity == m.Self) continue;
            float rr = m.R + other.Radius;
            float oseg = MathF.Max(0f, other.Height - 2f * other.Radius);
            var olo = other.Feet.Z + other.Radius;
            var across = new Vector2(p.X - other.Feet.X, p.Y - other.Feet.Y);
            float gapZ = p.Z > olo + oseg ? p.Z - (olo + oseg) : p.Z + m.Seg < olo ? olo - (p.Z + m.Seg) : 0f;
            float dist = MathF.Sqrt(across.LengthSquared() + gapZ * gapZ);
            if (dist >= rr - 1e-5f) continue;
            if (gapZ > 0f)
            {
                var n = new Vector3(across, p.Z > olo ? gapZ : -gapZ) / MathF.Max(dist, 1e-6f);
                p += n * (rr - dist + Skin * 0.5f);
            }
            else
            {
                float al = across.Length();
                var n = al > 1e-6f ? across / al : Vector2.UnitX;
                p += new Vector3(n * (rr - al + Skin * 0.5f), 0f);
            }
        }
        feet = p - new Vector3(0, 0, m.R);
    }

    /// <summary>The capsule of a body with these numbers standing at <paramref name="feet"/>.</summary>
    public static Capsule CapsuleOf(Vector3 feet, in Locomotion loco) => Capsule.OfBody(feet, loco.Radius, loco.Height);

    /// <summary>The capsule of <paramref name="body"/> as it is now (crouched, tucked in a move, or standing).</summary>
    public static Capsule CapsuleOf(in Body body, in Locomotion loco) => Capsule.OfBody(body.Feet, loco.Radius, body.CapsuleHeight(loco));
}
