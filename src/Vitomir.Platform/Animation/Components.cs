// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Animation;

/// <summary>What a character's motion is for: the <see cref="AnimationSystem"/> picks one from how the body moves.</summary>
public enum MotionRole
{
    /// <summary>Standing (1.1's <c>stand</c>).</summary>
    Idle,
    /// <summary>Walking (1.1's <c>walk</c>), its rate matched to the ground speed.</summary>
    Walk,
    /// <summary>Running (1.1's <c>run</c>), its rate matched to the ground speed.</summary>
    Run,
    /// <summary>Leaving the ground. 1.1 has none: a character without one holds its pose in the air.</summary>
    Jump,
    /// <summary>In the air. 1.1 has none: a character without one holds its pose.</summary>
    Fall,
    /// <summary>A hard landing (Christof's rough landing in 1.1, from its <c>Fall</c> mark).</summary>
    Land,

    // The moves' slots. 1.1 has a motion for none of them: each
    // is filled by a composition of 1.1's own poses (OneOneCompositions) with the hands and feet placed by IK
    // (PoseRig), until a real motion fills the slot. A traversal slot's motion is played by
    // the move's progress (its time is the progress times its length), not by the clock.

    /// <summary>Crouched and still: 1.1 has no crouch (its <c>crouch</c> mode is the wolf's shape-change).</summary>
    Crouch,
    /// <summary>Crouched and moving: the walk with the hips lowered and the knees bent by IK, stride-matched, until a real one comes.</summary>
    CrouchWalk,
    /// <summary>A hop up onto something a jump reaches (a bench, a stool).</summary>
    HopUp,
    /// <summary>Over something low at a pace: the sprint vault, and the run's step-over.</summary>
    Hurdle,
    /// <summary>Over something thin, a hand on its top.</summary>
    Vault,
    /// <summary>Up onto something deep, hands on its edge and a knee up.</summary>
    ClimbOnto,
    /// <summary>Up onto a top at the chest or above, from the ground.</summary>
    Mantle,
    /// <summary>The hands catching a ledge in a jump.</summary>
    Grab,
    /// <summary>Hanging by the hands from a ledge.</summary>
    Hang,
    /// <summary>From hanging, up over the ledge.</summary>
    PullUp,
    /// <summary>Letting go of a ledge.</summary>
    Drop,
    /// <summary>
    /// <b>A dodge</b> (Bodies/Dodge.cs): 1.1's own <c>dodge_onehand</c> or <c>dodge</c> (Christof's <c>ChrisDodgeSword</c>,
    /// <c>ChrisDodgeFists</c>, 39 frames: a duck of 0.25 m with a lean and a crossover step to his right, the hips 0.33 m out,
    /// and back to where he stood), played by the dodge's progress (<see cref="MotionInput.DodgeProgress"/>) — its lowest,
    /// furthest pose as the burst ends — while the body really moves, for a dodge any way (<see cref="MotionSlots.DodgeMotionAside"/>).
    /// A kind without it plays its locomotion.
    /// </summary>
    Dodge,
    /// <summary>
    /// <b>a turn on the spot</b> (the models repository's <c>turn_left_45</c>, <c>_90</c>, <c>turn_right_45</c>,
    /// <c>_90</c>): a person standing whose legs the view has left behind steps round to it (<see cref="People.PeopleTurn"/>).
    /// No kind has a motion in this role's slot: the turn the legs need chooses the clip, and the legs' turn plays it. 1.1's
    /// kinds never play it.
    /// </summary>
    Turn,
}

/// <summary>The motion slots of the moves (ours): which <see cref="MotionRole"/> a <see cref="Bodies.TraversalKind"/> plays.</summary>
public static class MotionSlots
{
    /// <summary>The slot a move plays, or <see cref="MotionRole.Idle"/> for none.</summary>
    public static MotionRole Of(Bodies.TraversalKind kind) => kind switch
    {
        Bodies.TraversalKind.HopUp => MotionRole.HopUp,
        Bodies.TraversalKind.Hurdle => MotionRole.Hurdle,
        // The run's step-over plays the hurdle's slot (1.1's run, a stride lifted), the sprint vault's too.
        Bodies.TraversalKind.StepOver => MotionRole.Hurdle,
        Bodies.TraversalKind.Vault => MotionRole.Vault,
        Bodies.TraversalKind.ClimbOnto => MotionRole.ClimbOnto,
        Bodies.TraversalKind.Mantle => MotionRole.Mantle,
        Bodies.TraversalKind.Grab => MotionRole.Grab,
        // Lowering to hang from an edge plays the grab's slot (no motion of its own yet).
        Bodies.TraversalKind.LowerToHang => MotionRole.Grab,
        Bodies.TraversalKind.Hang => MotionRole.Hang,
        Bodies.TraversalKind.PullUp => MotionRole.PullUp,
        Bodies.TraversalKind.Drop => MotionRole.Drop,
        // No motions of their own (1.1 has none): getting onto a ladder plays the grab's slot, being on it the hang's.
        Bodies.TraversalKind.LadderOn => MotionRole.Grab,
        Bodies.TraversalKind.Ladder => MotionRole.Hang,
        // Getting off a ladder on its clips (the people's ladder chooses the clip).
        Bodies.TraversalKind.LadderOff => MotionRole.Drop,
        // The walls on their clips (the people's ledge chooses the clip).
        Bodies.TraversalKind.WallOn => MotionRole.Grab,
        Bodies.TraversalKind.Wall => MotionRole.Hang,
        Bodies.TraversalKind.WallOff => MotionRole.Drop,
        // Squeezing and cover on their clips (the people's ledge chooses the clip).
        Bodies.TraversalKind.Squeeze => MotionRole.Hang,
        Bodies.TraversalKind.Cover => MotionRole.Hang,
        _ => MotionRole.Idle,
    };

    /// <summary>
    /// The dodge's slot is played for a dodge whose way is at least this much to the body's right
    /// (<see cref="MotionInput.DodgeAside"/>, the cosine): −1, every way. 1.1's dodge is a duck of 0.25 m with a crossover step
    /// and a lean to the right; played for every way it leans the wrong way on a dodge to the left, but it is an evasive pose
    /// and the hit volumes follow it. The first sweeps decided it: dodging straight back with the locomotion instead (a run
    /// played backwards, then the stand, its free hand forward) the guard's late arc met 43 % of the dodged blows at Dodge 0 and
    /// 71 % at Dodge 5 (the quicker dodge in the stand sooner); with the duck, 19 % and 20 %. A mirrored or a per-way motion
    /// (the models repository) replaces it; 0.5 plays it for a dodge to the right only.
    /// </summary>
    public const float DodgeMotionAside = -1f;

    /// <summary>Whether a role is a move's slot (played by its progress).</summary>
    public static bool IsTraversal(MotionRole role) => role is >= MotionRole.HopUp and <= MotionRole.Drop;

    /// <summary>Whether a role is crouched.</summary>
    public static bool IsCrouch(MotionRole role) => role is MotionRole.Crouch or MotionRole.CrouchWalk;
}

/// <summary>
/// How the body is moving, for the animation (ours): filled each tick by the program from the body (the
/// body's horizontal speed over the ground, whether it stands on ground, its vertical speed), read by the
/// <see cref="AnimationSystem"/> at the end of <see cref="Core.Phase.Move"/>. Plain data, digested.
/// </summary>
public struct MotionInput
{
    /// <summary>Speed over the ground, metres a second (the horizontal part of the body's velocity).</summary>
    public float GroundSpeed;

    /// <summary>Vertical speed, metres a second, up positive.</summary>
    public float VerticalSpeed;

    /// <summary>
    /// Speed along the way the body faces, metres a second: negative when it moves backwards (ours, a walk or
    /// run then plays backwards).
    /// </summary>
    public float ForwardSpeed;

    int grounded;

    /// <summary>Whether the body stands on ground this tick.</summary>
    public bool Grounded { readonly get => grounded != 0; set => grounded = value ? 1 : 0; }

    /// <summary>The move the body is making (<see cref="Bodies.Traversal"/>), or none.</summary>
    public Bodies.TraversalKind Traversal;

    /// <summary>How far through that move, 0 to 1.</summary>
    public float TraversalProgress;

    int crouched;

    /// <summary>Whether the body is crouched.</summary>
    public bool Crouched { readonly get => crouched != 0; set => crouched = value ? 1 : 0; }

    int dodging;

    /// <summary>Whether the body is dodging (<see cref="Bodies.Dodging"/>, its start, burst or recovery).</summary>
    public bool Dodging { readonly get => dodging != 0; set => dodging = value ? 1 : 0; }

    /// <summary>Where the dodge's motion is drawn, 0 to 1 (<see cref="Bodies.Dodging.Progress"/>).</summary>
    public float DodgeProgress;

    /// <summary>How much the dodge's way is to the body's right: the cosine between them, −1 (to its left) to 1.</summary>
    public float DodgeAside;

    /// <summary>
    /// Speed across the way the body faces, metres a second, to its left positive (with <see cref="ForwardSpeed"/>, the
    /// way the legs turn, <see cref="LegTurn"/>).
    /// </summary>
    public float SideSpeed;

    public static MotionInput Standing => new() { Grounded = true };
}

/// <summary>
/// What a character is playing (ours; the shape of 1.1's channel with its fade): its kind
/// (an index into <see cref="CharacterAssets"/>), the current motion and how far through it is, the motion
/// it is fading from and how far through that is, and the fade's weight. Every field is a number, so the
/// component is digested byte for byte and two runs agree; the pose is a pure function of it
/// (<see cref="PoseEvaluator"/>). Made with <see cref="For"/>.
/// </summary>
public struct CharacterAnimation
{
    /// <summary>The character's kind: its model and its motions (<see cref="CharacterAssets.Kind"/>).</summary>
    public int Kind;

    /// <summary>What the current motion is for.</summary>
    public MotionRole Role;

    /// <summary>The current motion (<see cref="CharacterAssets.Clip"/>), or −1 for the rest pose.</summary>
    public int Clip;

    /// <summary>Seconds into the current motion.</summary>
    public float Time;

    /// <summary>How fast the current motion plays: 1 is 1.1's 30 frames a second; a walk or run is stride-matched.</summary>
    public float Rate;

    /// <summary>The once-rolled factor of a <c>variable</c> mode (1.1's 0.75 to 1.25), else 1.</summary>
    public float RateScale;

    /// <summary>The motion being faded from, or −1 when there is no fade.</summary>
    public int PreviousClip;

    /// <summary>Seconds into the motion being faded from (it keeps playing while it fades).</summary>
    public float PreviousTime;

    /// <summary>How fast the motion being faded from plays.</summary>
    public float PreviousRate;

    /// <summary>The current motion's weight in the pose: 0 as a fade starts, 1 when it is alone.</summary>
    public float Blend;

    /// <summary>The body's vertical speed on the last tick (the speed it landed at, on the tick it lands).</summary>
    public float LastVerticalSpeed;

    int wasGrounded;

    /// <summary>Whether the body stood on ground on the last tick.</summary>
    public bool WasGrounded { readonly get => wasGrounded != 0; set => wasGrounded = value ? 1 : 0; }

    // ---- the action channel (ActionChannel): an attack, the block, a hurt, a death, over the locomotion ----

    /// <summary>What the action channel plays; <see cref="ActionRole.None"/> (the default) when it is empty and the other action fields mean nothing.</summary>
    public ActionRole Action;

    /// <summary>The action's clip (<see cref="CharacterAssets.Clip"/>).</summary>
    public int ActionClip;

    /// <summary>Clip seconds into the action, and where it was before the last advance (for its <c>Strike</c> mark).</summary>
    public float ActionTime, ActionLast;

    /// <summary>How fast the action plays (1.1's <c>STRIKETIME</c> stretching for an attack).</summary>
    public float ActionRate;

    /// <summary>The action's weight over the locomotion: 0 to 1 as it comes in, down to 0 as it ends.</summary>
    public float ActionWeight;

    /// <summary>How much the hips and legs take the action (1 while the body stands, 0 while it moves: the action then rides on the upper body).</summary>
    public float ActionLegs;

    /// <summary>The action being cross-faded from on the channel (<see cref="ActionRole.None"/> for none), its clip, time and rate, and the current one's share (0 to 1).</summary>
    public ActionRole ActionFrom;
    public int ActionFromClip;
    public float ActionFromTime, ActionFromRate, ActionBlend;

    /// <summary>Clip seconds a held action holds at (the block's raised frame), when <see cref="ActionHolds"/>.</summary>
    public float ActionHoldAt;

    int actionHolds, actionEnding;

    /// <summary>Whether the action holds at <see cref="ActionHoldAt"/> (a block held).</summary>
    public bool ActionHolds { readonly get => actionHolds != 0; set => actionHolds = value ? 1 : 0; }

    /// <summary>Whether the action has ended and is fading out.</summary>
    public bool ActionEnding { readonly get => actionEnding != 0; set => actionEnding = value ? 1 : 0; }

    /// <summary>
    /// At the end: the seconds this action's fade-out takes once it ends, 0 for the channel's own
    /// (<see cref="ActionChannelSettings.FadeOutSeconds"/>): a stopped blade blended back to guard over the rest of its swing
    /// (<see cref="ActionChannel.FadeFromHere"/>).
    /// </summary>
    public float ActionFadeOut;

    // ---- the guard's layer (Combat/Guard.cs) ----

    /// <summary>
    /// The shield arm's layer — the block's clip (<see cref="GuardClip"/>) at <see cref="GuardTime"/> laid over the shield
    /// arm's bones by <see cref="GuardWeight"/>, moving toward <see cref="GuardTarget"/> (0 or 1) at the channel's fade-in: the
    /// shield kept raised while the weapon arm swings. Nothing at weight 0 (the default).
    /// </summary>
    public int GuardClip;
    public float GuardTime, GuardWeight, GuardTarget;

    /// <summary>The guard's turn of the chest toward a threat's line (radians about the body's up, left positive; <see cref="Combat.GuardLayer.Twist"/>); 0 the default.</summary>
    public float GuardTwist;

    /// <summary>The guard's stance — the weapon hand toward the side a blow comes from (−1 right to 1 left) and its height (−1 low to 1 high); 0, 0 the block's own (<see cref="Combat.GuardLayer.Aim"/>).</summary>
    public float GuardSide, GuardHeight;

    // ---- the arm's recoil off a blocked blow (Animation/Recoil.cs) ----

    /// <summary>
    /// The weapon hand's recoil now (model space, m: pushed out of what the blade met), the upper spine's lean with it and the
    /// wrist's turn tipping the blade back (radians now) — laid on by <see cref="Recoil.Apply"/>; zero, nothing (the default).
    /// </summary>
    public System.Numerics.Vector3 RecoilBack;
    public float RecoilLean, RecoilWrist;

    /// <summary>The recoil's kick — its direction (model space, unit), its most (m), and the tick it began (<see cref="RecoilSystem"/> moves it on).</summary>
    public System.Numerics.Vector3 RecoilDir;
    public float RecoilPeak;
    public long RecoilSince;

    // ---- the upper body's push by a stagger (Combat/Stagger.cs) ----

    /// <summary>The stagger's push — the axis the chest turns about (model space, unit), its most and its turn now (radians), the tick it began (<see cref="Combat.StaggerPushSystem"/> moves it on); zero, nothing (the default).</summary>
    public System.Numerics.Vector3 StaggerAxis;
    public float StaggerPeak, StaggerTurn;
    public long StaggerSince;

    // ---- a held blow switched to another direction ----

    /// <summary>The cross-fade into the action now on the channel lasts this long (s) in place of the channel's own (<see cref="ActionChannelSettings.CrossSeconds"/>); 0, the channel's. Set by a held blow's switch; a new action starts at 0.</summary>
    public float ActionCrossSeconds;

    /// <summary>A character of <paramref name="kind"/> standing in its idle from its first frame, with no fade.</summary>
    public static CharacterAnimation For(CharacterAssets assets, int kind)
    {
        var idle = assets.Kind(kind).Motion(MotionRole.Idle);
        return new CharacterAnimation
        {
            Kind = kind, Role = MotionRole.Idle, Clip = idle.Clip, Time = idle.StartSeconds, Rate = 1f, RateScale = 1f,
            PreviousClip = -1, Blend = 1f, WasGrounded = true,
        };
    }
}
