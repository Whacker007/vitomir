// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The eye's easing over steps follows src/Vitomir.Engine/Levels/Cells/CellStepEase.cs at b4185ef, reworked for the platform (metres).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Cameras;

/// <summary>
/// The cameras' numbers, as data: the fields of view, the third-person
/// camera's shoulder offset (to the side and up from the head), its boom and the radius of the sphere that
/// pulls it in from walls, and the eye's easing over steps.
/// </summary>
public sealed record CameraSettings
{
    public float ThirdPersonFieldOfView { get; init; } = 75f;
    public float FirstPersonFieldOfView { get; init; } = 80f;
    /// <summary>How far to the side of the head the third-person pivot is (m, times the rig's shoulder).</summary>
    public float ShoulderSide { get; init; } = 0.5f;
    /// <summary>How far above the head the pivot is (m).</summary>
    public float ShoulderUp { get; init; } = 0.2f;
    /// <summary>How far behind the pivot the camera wants to be (m).</summary>
    public float Boom { get; init; } = 2.5f;
    /// <summary>The radius of the sphere swept to keep the camera out of walls (m).</summary>
    public float ProbeRadius { get; init; } = 0.2f;
    /// <summary>Of the eye's lift after a step, the part paid back per metre walked (a little steeper than a stair).</summary>
    public float EaseSlope { get; init; } = 0.55f;
    /// <summary>The lift paid back a second whatever the body does (m/s; the earlier build's 48 units: a riser in a quarter second).</summary>
    public float EaseSettle { get; init; } = 48f / 72f;
    /// <summary>A rise or drop larger than this in a tick is not a step and is not eased (m).</summary>
    public float EaseMaxStep { get; init; } = 0.45f;
    /// <summary>A move across larger than this in a tick is a placing, not a walk, and clears the lift (m).</summary>
    public float EasePut { get; init; } = 96f / 72f;
    /// <summary>How fast the eye goes down when the body crouches and up when it stands (m/s; ours, crouching in about 0.2 s).</summary>
    public float CrouchEase { get; init; } = 3.5f;

    /// <summary>
    /// The field a freed pointer's place is given in, in first person (degrees, vertical): the widest the
    /// Field of View slider allows, so whatever field the viewer draws with, the program can put the pointer's place
    /// on the picture into it (<see cref="Presentation.FirstPersonView.PickScale"/>) and the pick is the ray under the
    /// cursor. The field the picture is drawn with is the viewer's own and is not the simulation's (it is not on a tape).
    /// </summary>
    public float FirstPersonPickField { get; init; } = 110f;

    /// <summary>
    /// The steady eye's smoothing (ours): the head's eyes less the gait's bob (<see cref="EyeTracks"/>) are
    /// followed by two stages, each with this time constant (s), which take out what is left of a step (a fade between
    /// two motions, the rig's lean on a bob made upright) while a lean or a crouch comes through in a fifth of a second.
    /// </summary>
    public float SteadySeconds { get; init; } = 0.1f;

    /// <summary>
    /// The most the steady eye is let fall behind the head's eyes less the bob (m): beyond it the steady eye is
    /// drawn along, so a crouch, a jump's tuck or a mantle's climb is followed at once and the eye is never left in
    /// the neck.
    /// </summary>
    public float SteadyLeash { get; init; } = 0.06f;

    /// <summary>The radius of the sphere that keeps the eye out of walls and ceilings (m): it is swept from the body's axis to the eye.</summary>
    public float EyeClearance { get; init; } = 0.1f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: how far the head's own motion through an <b>action</b> — a
    /// swing, a block, a flinch (the action channel, <see cref="ActionRole"/>, but a death), the guard's stance, the
    /// dodge's sway — may carry the first-person eye from where the motion alone (the locomotion channel) puts it (m).
    /// The excess is taken out smoothly (a soft limit: a small move comes through whole, a large one tends to this). 0.04:
    /// 1.1's blows carry Christof's head 0.46 to 0.75 m (the aim work's survey) and the crosshair with it; at 4 cm the eye
    /// moves less than the gait's own bob at a walk (13 cm a stride), enough that a swing is felt and not enough that the
    /// crosshair leaves what it was on (4 cm is 1.5° at 1.5 m). Negative turns the limit off (the earlier build's eye: the head's
    /// whole motion).
    /// </summary>
    public float ActionAllowance { get; init; } = 0.04f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: how much of the dodge's duck (1.1's <c>dodge</c>: Christof's eyes 0.31 m down
    /// at its lowest) the first-person eye keeps, 0 to 1. The duck is the body's real evasive move, as a crouch is (the hit
    /// volumes follow it), so the view goes down with it; its sway across (the hips 0.33 m out and back, the head 0.6 m) is
    /// an action's, held to <see cref="ActionAllowance"/>. 0.5: the whole duck took the eye, held over the middle while the
    /// dodge leans the chest forward and aside, 2 mm into the chest's volume; half keeps it 3.7 cm clear (0: 5.6 cm).
    /// </summary>
    public float DodgeDuck { get; init; } = 0.5f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: how far the first-person eye leans out ahead (m, along the look) when the
    /// view looks straight down, by the sine of the look's pitch below level (half of it at 30° down): as a head leans out
    /// to look down, so the picture looks down on the chest's front and the feet rather than into the collar under the eye
    /// (standing, Christof's nearest skin is the collar round the hidden neck, 0.165 m from the eye). The reticle measures
    /// from the leaned eye too (it is the simulation's).
    /// </summary>
    public float LookDownAhead { get; init; } = 0.1f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: the time (s) of the third-person pivot's critically damped follow
    /// of the body's changes of speed (<see cref="CameraSystem"/>'s <c>Follow</c>): a start, stop, dodge or landing is
    /// followed over about this long, a steady pace with no lag. 0.08 s: a human sprint's start (5.5 m/s) lags at most
    /// 0.16 m, a Celerity 5 sprint's 0.74 m, a dodge's burst 0.1 m. 0 is the earlier build's rigid camera.
    /// </summary>
    public float ThirdPersonFollowSeconds { get; init; } = 0.08f;

    /// <summary>Ours, ENGINE_SANDBOX: the most the third-person follow may lag the body (m), so the body never leaves the picture.</summary>
    public float ThirdPersonLeash { get; init; } = 1.0f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: the time (s) over which a boom pulled in by a wall lets out again once the wall is gone
    /// (pulled in at once, always). 0.25 s: running past a pillar does not jump the camera 2 m back in a tick.
    /// </summary>
    public float BoomOutSeconds { get; init; } = 0.25f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: a move of the head's eyes in one tick (model space, m) larger than this is the
    /// pose snapping, not the body moving, and is eased out of the first-person eye (<see cref="CameraSystem"/>'s
    /// <c>Unpopped</c>). 0.1: a run's head moves at most 4.5 cm a tick (120 a second); the snap measured on stopping from a
    /// run is 0.275 m.
    /// </summary>
    public float PopStep { get; init; } = 0.1f;

    /// <summary>Ours, ENGINE_SANDBOX: the time (s) over which a snap of the pose is paid out of the first-person eye; 0: not eased.</summary>
    public float PopSeconds { get; init; } = 0.1f;

    /// <summary>
    /// A development control; ours, ENGINE_SANDBOX: the third-person <b>melee framing</b>, off by default (the
    /// host's <c>thirdPersonMeleeFraming</c>): while a melee weapon or the fists are out the pivot goes up and to the middle
    /// (<see cref="MeleeShoulderSide"/>, <see cref="MeleeShoulderUp"/>, <see cref="MeleeBoom"/>), eased, as Bannerlord's
    /// melee camera stands, so a foe 1.0 to 1.5 m ahead is seen over the player's head rather than behind his own back.
    /// Measured (a townsman facing Christof, his head, chest and belly each seen or not past Christof's own hit volumes):
    /// off, at 1.0 m over the right shoulder none of them, the left head and chest; at 1.5 m the right the head only, the
    /// left all three. On (0, 0.8, 2.5): at 1.0 m head and chest either way, at 1.5 m all three; the foe's chest then 0.38
    /// (1.0 m) and 0.33 (1.5 m) of the half-height below the picture's middle (level look). Tried: up 0.45 and 0.75 or 0.9 to
    /// the side (at 1.0 m over the right shoulder the head only: the sword arm is in the way), 1.1 to the side (all but the
    /// belly at 1.0 m, the foe 0.24 of the half-width aside). The look is the player's: no pitch is added.
    /// </summary>
    public bool MeleeFraming { get; init; }
    /// <summary>ENGINE_SANDBOX: the melee framing's shoulder offset to the side (m; 0, the middle; the camera's 0.5 otherwise).</summary>
    public float MeleeShoulderSide { get; init; } = 0f;
    /// <summary>ENGINE_SANDBOX: the melee framing's pivot over the eyes (m; 0.2 otherwise).</summary>
    public float MeleeShoulderUp { get; init; } = 0.8f;
    /// <summary>ENGINE_SANDBOX: the melee framing's boom (m; 2.5, as otherwise).</summary>
    public float MeleeBoom { get; init; } = 2.5f;
    /// <summary>ENGINE_SANDBOX: the time (s) the melee framing eases in and out over.</summary>
    public float MeleeFramingSeconds { get; init; } = 0.3f;

    /// <summary>
    /// <i>"there's some camera insanity if I hit a wall at an odd angle"</i>: the third-person camera at
    /// walls as before — the shoulder's pivot stopped against a wall (its sphere 1 cm off it, so a look a degree into the wall
    /// collapsed the boom to the head and a degree away let it out), the boom's sphere stopped by any edge it grazed on its way (a block's
    /// corner beside the line pulled the camera from 2.5 m to the head in a tick). Off (the default) the walls are calm
    /// (<see cref="WallMargin"/>, <see cref="ShoulderInSeconds"/>; the boom kept where the camera's own place and its line to the pivot are
    /// clear). Only the models repository's people have the calm walls; 1.1's bodies keep the earlier build's camera. Not in <see cref="ToNote"/>: the
    /// host's note <c>cameraWalls</c> carries it (a tape without it had the earlier build's).
    /// </summary>
    public bool HardWalls { get; init; }

    /// <summary>
    /// Ours, ENGINE_SANDBOX: how far the third-person pivot is kept off a wall beyond the probe's radius (m; 0.1), so the boom's
    /// sweep never starts against one: the boom's length then changes smoothly as the look turns toward or away from the wall.
    /// </summary>
    public float WallMargin { get; init; } = 0.1f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: the time (s) of the shoulder's pull-in toward the head when a wall comes within
    /// <see cref="WallMargin"/> of the pivot (0.05; never into the wall itself: the probe's own radius is a hard stop); it lets out
    /// over <see cref="BoomOutSeconds"/>.
    /// </summary>
    public float ShoulderInSeconds { get; init; } = 0.05f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: the radius that stops the third-person pivot at a wall outright (m; 0.05). The pivot is not the camera
    /// (that stands on the boom behind it, kept clear by the probe's radius): it needs only to stay this side of the wall, so the eased
    /// pull-in of <see cref="ShoulderInSeconds"/> is seldom overtaken (an earlier build stopped it at the probe's radius: 0.4 m in three ticks running
    /// into a wall at a slant).
    /// </summary>
    public float ShoulderStopRadius { get; init; } = 0.05f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: the time (s) of the boom's pull-in when the walls want it shorter (0.05) — eased only while the camera's
    /// place on the way stays clear of the level (its sphere touches nothing), else at once.
    /// </summary>
    public float BoomInSeconds { get; init; } = 0.05f;

    /// <summary>
    /// Ours, ENGINE_SANDBOX: how far an edge of the level may come into the third-person camera's sphere (of
    /// <see cref="ProbeRadius"/>) before the boom is shortened for it (m; 0.05): the camera's centre stays at least 0.15 m from anything,
    /// beyond the picture's near plane (0.14 m), and a corner just touching the sphere's rim does not move it.
    /// </summary>
    public float CornerGive { get; init; } = 0.05f;

    /// <summary>
    /// The earlier build's camera, to compare (<c>--camera-rigid</c>, the camera survey): the eye rides the head through
    /// an action, no lean looking down, the third person rigid. (A body in the air keeps no step lift either way.)
    /// </summary>
    public static CameraSettings Rigid => new() { ActionAllowance = -1f, LookDownAhead = 0f, ThirdPersonFollowSeconds = 0f, BoomOutSeconds = 0f, PopSeconds = 0f };

    /// <summary>
    /// The player's camera settings (the host's <c>firstPersonActionSway</c>, cm, <c>thirdPersonFollow</c>,
    /// s, and <c>thirdPersonMeleeFraming</c>) over these numbers, kept within their ends (0 to 1 m; 0 to 0.3 s); null when
    /// all are the defaults.
    /// </summary>
    public static CameraSettings? Of(float swayCentimetres, float followSeconds, bool meleeFraming = false)
    {
        var d = new CameraSettings();
        float sway = float.IsFinite(swayCentimetres) ? Math.Clamp(swayCentimetres / 100f, 0f, 1f) : d.ActionAllowance;
        float follow = float.IsFinite(followSeconds) ? Math.Clamp(followSeconds, 0f, 0.3f) : d.ThirdPersonFollowSeconds;
        return sway == d.ActionAllowance && follow == d.ThirdPersonFollowSeconds && !meleeFraming ? null
            : d with { ActionAllowance = sway, ThirdPersonFollowSeconds = follow, MeleeFraming = meleeFraming };
    }

    /// <summary>
    /// These numbers as a tape's note (the twelve that move the simulation's view, invariant, ';' between),
    /// and back (<see cref="FromNote"/>: null for an empty or malformed note, the defaults then).
    /// </summary>
    public string ToNote() => string.Join(";", new[] { ActionAllowance, LookDownAhead, ThirdPersonFollowSeconds, BoomOutSeconds, DodgeDuck, PopStep, PopSeconds,
            MeleeFraming ? 1f : 0f, MeleeShoulderSide, MeleeShoulderUp, MeleeBoom, MeleeFramingSeconds }
        .Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    /// <inheritdoc cref="ToNote"/>
    public static CameraSettings? FromNote(string? note)
    {
        var parts = (note ?? "").Split(';');
        if (parts.Length != 12) return null;
        var v = new float[12];
        for (int i = 0; i < 12; i++)
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return null;
        return new CameraSettings
        {
            ActionAllowance = v[0], LookDownAhead = v[1], ThirdPersonFollowSeconds = v[2], BoomOutSeconds = v[3], DodgeDuck = v[4], PopStep = v[5], PopSeconds = v[6],
            MeleeFraming = v[7] != 0f, MeleeShoulderSide = v[8], MeleeShoulderUp = v[9], MeleeBoom = v[10], MeleeFramingSeconds = v[11],
        };
    }
}

/// <summary>
/// The eye's lift over steps (presentation, kept in the simulation so it replays): how far below or above
/// where the body is its head is shown, so that stepping up a riser in one tick does not jerk the view, and
/// where the feet were last tick.
/// </summary>
public struct EyeEase
{
    public float Lift;
    /// <summary>How far below the standing eyes the eye is for a crouch (m), eased towards the crouch's drop.</summary>
    public float Duck;
    public Vector3 LastFeet;
    /// <summary>1 once the body has been seen; the first tick starts with no lift.</summary>
    public int Seen;

    /// <summary>
    /// The steady eye's filter: its two stages, following the head's eyes in the body's own model space (m),
    /// and the tick after the one it last ran in (0 never; a gap starts it again from the head).
    /// </summary>
    public Vector3 Steady1, Steady2;
    public long SteadyNext;

    /// <summary>
    /// The lift after a tick that took the feet from <paramref name="from"/> to <paramref name="to"/>: the
    /// tick's rise taken out, then the ease's share of the ground covered and of the time paid back
    /// (the earlier build's <c>CellStepEase.Next</c>). A rise beyond a step, or a move beyond a walk, clears it.
    /// </summary>
    public static float Next(float lift, Vector3 from, Vector3 to, float seconds, CameraSettings s)
    {
        float rise = to.Z - from.Z;
        float ground = new Vector2(to.X - from.X, to.Y - from.Y).Length();
        if (MathF.Abs(rise) > s.EaseMaxStep || ground > s.EasePut) return 0f;
        lift -= rise;
        float pay = ground * s.EaseSlope + MathF.Max(seconds, 0f) * s.EaseSettle;
        lift = lift > 0f ? MathF.Max(0f, lift - pay) : MathF.Min(0f, lift + pay);
        return Math.Clamp(lift, -s.EaseMaxStep, s.EaseMaxStep);
    }
}

/// <summary>Where the picture is seen from this tick (a service the program reads after each step).</summary>
public sealed class ViewState
{
    /// <summary>The view, when there is a controlled body to see from.</summary>
    public ViewPoint View { get; set; }
    public bool HasView { get; set; }
    /// <summary>The body it is seen from.</summary>
    public EntityId Of { get; set; } = EntityId.None;
    public ViewKind Kind { get; set; }
    /// <summary>
    /// The controlled body's eyes, steady: where the head's eyes are in the pose, low-passed (no bounce or sway),
    /// eased over steps. The reticle, use, talk, the pointer's pick and aim measure from here, whatever the
    /// viewer's head-motion setting; the first-person view stands here.
    /// </summary>
    public Vector3 Eyes { get; set; }

    /// <summary>
    /// The controlled body's eyes where the pose has the head this tick (eased over steps as <see cref="Eyes"/>):
    /// presentation only. The picture stands between <see cref="Eyes"/> and here by the viewer's head motion
    /// (<see cref="Presentation.FirstPersonView.Eye"/>). <see cref="Eyes"/> when the body has no posed head. the
    /// head's own motion through an action held to <see cref="CameraSettings.ActionAllowance"/> (see <see cref="CameraSystem"/>).
    /// </summary>
    public Vector3 Head { get; set; }
    /// <summary>How far the third-person camera stands behind its pivot after the walls pulled it in (m).</summary>
    public float Boom { get; set; }
}

/// <summary>
/// The Post phase's camera (ours): the <see cref="ViewPoint"/> of the controlled
/// body into <see cref="ViewState"/>. <b>Third person</b>: a pivot at the head (the eyes' height, eased over
/// steps) put over the shoulder — to the side by the rig's shoulder and up — and the camera on a boom behind
/// it along the rig's look, pulled in by a sphere swept from the head to the pivot and from the pivot out,
/// so a wall between the body and the camera brings it in. The pivot's eyes go down as the body crouches and up
/// as it stands, eased (<see cref="CameraSettings.CrouchEase"/>), and follow a move's path without the steps'
/// ease.
/// <para>
/// <b>First person</b>: the eye <b>rides the head</b>. Each tick, after the animation and the pose rig (Post/0
/// and 10), the eyes are found where the pose has put the head (<see cref="HeadCollapse.EyesIn"/>, in the body's
/// model space), so the run's lean forward and down, the crouch, a jump's tuck, a mantle's climb and a ladder's
/// are the eye's as they are the body's, and it never stands behind the hidden head over the neck. Two eyes are kept:
/// the head's (<see cref="ViewState.Head"/>, presentation only) and a <b>steady</b> one (<see cref="ViewState.Eyes"/>):
/// the head's less the gait's bob — its bounce and sway, the looping motion's own cycle (<see cref="EyeTracks"/>) —
/// smoothed by a light two-stage low-pass (<see cref="CameraSettings.SteadySeconds"/>) never let more than
/// <see cref="CameraSettings.SteadyLeash"/> behind. Both are eased over steps
/// (the body's feet go up a riser in a tick; its eyes do not) and kept out of walls and ceilings by a small sphere
/// swept from the body's axis (<see cref="CameraSettings.EyeClearance"/>). The simulation's view stands at the steady
/// eye, looking where the player looks (no roll, nothing of the head's turn); the picture is drawn between the two by
/// the viewer's head-motion setting. A body with no posed head (no animation, or a kind without a head) keeps the
/// upright eye: the eye height over the feet, eased over steps and crouches.
/// </para>
/// <para>
/// <b>an earlier build, steady</b>: the head's own motion through an action (a swing, a block, a flinch, the
/// guard, a dodge's sway) is held to <see cref="CameraSettings.ActionAllowance"/> about where the motion alone puts the
/// eyes (<c>MotionAlone</c>); looking down the eye leans out (<see cref="CameraSettings.LookDownAhead"/>); a body in the air
/// keeps no step lift. In third person the pivot's head follows the body's changes of speed with a critically damped lag
/// (<c>Follow</c>, <see cref="CameraSettings.ThirdPersonFollowSeconds"/>) and the boom lets out eased (<c>Boomed</c>,
/// <see cref="CameraSettings.BoomOutSeconds"/>). The movement doc's "A steady camera".
/// </para>
/// </summary>
public sealed class CameraSystem : ISystem
{
    Matrix4x4[] skin = new Matrix4x4[64];
    // The pose the motion alone makes (MotionAlone), for the controlled body's eyes; made once, reused.
    readonly PoseEvaluator alonePose = new();
    Matrix4x4[] aloneModel = new Matrix4x4[64], aloneSkin = new Matrix4x4[64];
    EyeTracks? tracks;
    CharacterAssets? tracksOf;
    // The third-person follow's lag and its rate, the target's last place and velocity, and the boom let out
    // (Follow, Boomed). Presentation of the simulation's own path, kept here (not digested): a world run again from its
    // start makes them again the same.
    Vector3 lag, lagRate, followLast, followVelocity;
    EntityId followOf = EntityId.None, boomOf = EntityId.None;
    long followNext, boomNext;
    bool followKnown;
    // The melee framing's share, eased (MeleeWeight).
    float meleeWeight;
    EntityId meleeOf = EntityId.None;
    long meleeNext;
    // The animation's jump taken out of the first-person eye and being paid out (Unpopped), and the head's last place.
    Vector3 pop, popLast;
    EntityId popOf = EntityId.None;
    long popNext;
    float boomNow;
    // The shoulder's shares of its rise and side as the walls left them, eased (Shouldered).
    float shoulderRise, shoulderSide;
    EntityId shoulderOf = EntityId.None;
    long shoulderNext;

    public void Run(World world)
    {
        var view = world.Service<ViewState>();
        var settings = world.Service<CameraSettings>();
        var level = world.Service<LevelSpace>().Collision;
        var tags = world.Store<Controlled>();
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var rigs = world.Store<CameraRig>();
        var eases = world.Store<EyeEase>();

        // Every body's eye is eased, so a body that becomes the controlled one has a settled lift.
        var data = bodies.Data;
        var ents = bodies.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            if (!eases.Has(ents[i])) continue;
            ref var ease = ref eases.Get(ents[i]);
            // A move's path is smooth already: the step's lift is not kept through it.
            // And the controlled body in the air — a fall is the body's own motion, not a step down to ease
            // (the eye was held up to 0.45 m over the head through a 2 m drop and sank back for 0.7 s after it). Only the
            // controlled body's: every other body's eased lift (digested; seen only if it becomes the controlled one) is as it was.
            bool moving = data[i].State is BodyState.Traversing or BodyState.Hanging or BodyState.Ladder   // a ladder's climb too
                          || (data[i].State == BodyState.Air && tags.Has(ents[i]));
            ease.Lift = ease.Seen != 0 && !moving ? EyeEase.Next(ease.Lift, ease.LastFeet, data[i].Feet, World.TickSeconds, settings) : 0f;
            float duck = data[i].Crouched != 0 && locos.Has(ents[i]) ? MathF.Max(0f, locos.Get(ents[i]).Height - locos.Get(ents[i]).CrouchHeight) : 0f;
            float by = settings.CrouchEase * World.TickSeconds;
            ease.Duck = ease.Seen == 0 ? duck : ease.Duck < duck ? MathF.Min(duck, ease.Duck + by) : MathF.Max(duck, ease.Duck - by);
            ease.LastFeet = data[i].Feet;
            ease.Seen = 1;
        }

        view.HasView = false;
        world.TryStore<Animation.People.PersonGround>(out var grounds);
        var controlled = tags.Entities;
        for (int i = 0; i < controlled.Length; i++)
        {
            var e = controlled[i];
            if (!bodies.Has(e) || !rigs.Has(e) || !locos.Has(e)) continue;
            ref readonly var body = ref bodies.Get(e);
            ref readonly var rig = ref rigs.Get(e);
            ref readonly var loco = ref locos.Get(e);
            // A person drawn on the line (People.PersonGround, the stairs' rule) is seen on it: the line is its step's lift (the upright
            // eye rides it; the head's eyes, posed on it, are followed without it and have it laid back on), in place of the eye's own ease.
            bool onLine = grounds is not null && grounds.Has(e) && grounds.Get(e).Stairs;
            float stepLift = !eases.Has(e) ? 0f : onLine ? grounds!.Get(e).Line : eases.Get(e).Lift;
            float lift = eases.Has(e) ? stepLift - eases.Get(e).Duck : 0f;
            var upright = body.Feet + new Vector3(0, 0, loco.EyeHeight + lift);
            var (steady, head) = (upright, upright);
            if (eases.Has(e) && HeadEyes(world, e, ref eases.Get(e), settings, out var steadyModel, out var headModel, out var place, onLine ? stepLift : 0f))
            {
                var up = new Vector3(0, 0, stepLift);
                // Looking down in first person the eye leans out over the chest, as a head does.
                if (rig.View == ViewKind.First && rig.Pitch < 0f && settings.LookDownAhead > 0f)
                {
                    var (ys, yc) = DetMath.SinCos(rig.Yaw);
                    up += new Vector3(yc, ys, 0f) * (settings.LookDownAhead * DetMath.Sin(MathF.Min(-rig.Pitch, DetMath.HalfPi)));
                }
                var axis = new Vector3(body.Feet.X, body.Feet.Y, 0f);
                float top = body.Feet.Z + body.CapsuleHeight(loco) - loco.Radius;
                steady = Clear(level, axis, PoseMath.Transform(steadyModel, place) + up, top, settings.EyeClearance);
                head = Clear(level, axis, PoseMath.Transform(headModel, place) + up, top, settings.EyeClearance);
            }
            view.Of = e;
            view.Kind = rig.View;
            view.Eyes = steady;
            view.Head = head;
            if (rig.View == ViewKind.First)
            {
                view.View = new ViewPoint(steady, rig.Yaw, rig.Pitch, settings.FirstPersonPickField);
                view.Boom = 0f;
            }
            else
            {
                // Free look swings the camera round the body (CameraRig.Orbit; 0 otherwise).
                float yaw = rig.Orbit == 0f ? rig.Yaw : DetMath.WrapAngle(rig.Yaw + rig.Orbit);
                var (s, c) = DetMath.SinCos(yaw);
                var right = new Vector3(s, -c, 0f);
                // The melee framing (off by default) moves the shoulder's offset and the boom toward their melee
                // numbers while a melee weapon or the fists are out, eased.
                float melee = MeleeWeight(world, e, settings);
                float side = settings.ShoulderSide + (settings.MeleeShoulderSide - settings.ShoulderSide) * melee;
                float raise = settings.ShoulderUp + (settings.MeleeShoulderUp - settings.ShoulderUp) * melee;
                var offset = right * (side * (rig.Shoulder >= 0 ? 1f : -1f)) + new Vector3(0, 0, raise);
                var back = -DetMath.Direction(yaw, rig.Pitch);
                var boom = back * (settings.Boom + (settings.MeleeBoom - settings.Boom) * melee);
                // A person's camera is calm at walls (CameraSettings.HardWalls: the earlier build's, and 1.1's bodies').
                bool calm = !settings.HardWalls && IsPerson(world, e);
                Vector3 pivot, arm;
                if (!calm)
                {
                    pivot = upright + Reach(level, upright, offset, settings.ProbeRadius);
                    arm = Reach(level, pivot, boom, settings.ProbeRadius);
                }
                else
                {
                    pivot = upright + Shouldered(world, e, offset, Shoulder(level, upright, offset, settings), settings);
                    arm = BoomedCalm(world, e, level, pivot, boom, BoomClear(level, pivot, boom, settings.ProbeRadius, settings.CornerGive), settings);
                }
                if (!calm) arm = Boomed(world, e, arm, settings);
                // The camera lags the body's changes of speed (Follow), swept from where it would stand on the body,
                // so the lag never takes it through a wall (a stop at a wall: the camera runs on toward the body, not into it).
                var camera = pivot + arm;
                camera += Reach(level, camera, -Follow(world, e, upright, settings), settings.ProbeRadius);
                view.View = new ViewPoint(camera, yaw, rig.Pitch, settings.ThirdPersonFieldOfView);
                view.Boom = arm.Length();
            }
            view.HasView = true;
            break;
        }
    }

    /// <summary>
    /// The head's eyes this tick and the steady eye (both in the body's model space, metres) and the matrix that
    /// stands the model in the world, for a body whose kind has a head (<see cref="HeadCollapse"/>); false otherwise.
    /// Runs the steady eye's filter one tick on (see the class).
    /// </summary>
    bool HeadEyes(World world, EntityId e, ref EyeEase ease, CameraSettings settings, out Vector3 steady, out Vector3 head, out Matrix4x4 place, float line = 0f)
    {
        steady = head = default;
        place = default;
        if (!world.TryService<CharacterPoses>(out var poses) || !world.TryService<CharacterAssets>(out var assets)) return false;
        var anims = world.Store<CharacterAnimation>();
        if (!anims.Has(e)) return false;
        var kind = assets.Kind(anims.Get(e).Kind);
        if (kind.Head is not { } hidden) return false;
        int n = assets.ModelOf(anims.Get(e).Kind).Skeleton.Count;
        if (skin.Length < n) skin = new Matrix4x4[n];
        // The unbent head — the aim offset bends the spine for a swing, and the eye does not tip with it.
        int count = poses.CopySkin(world, e, skin, unbent: true);
        ref var drawnRig = ref world.Store<PoseRig>().Find(e);
        float legs = Unsafe.IsNullRef(ref drawnRig) ? 0f : drawnRig.LegYaw;
        head = OnTheView(hidden.EyesIn(skin.AsSpan(0, count)), hidden, skin.AsSpan(0, count), CharacterPoses.Chain(world, e), assets, anims.Get(e), legs);   // a person's head as if on the view
        ref readonly var body = ref world.Get<Body>(e);
        place = kind.Placement(body.Feet, body.Yaw);
        if (tracks is null || !ReferenceEquals(tracksOf, assets)) (tracks, tracksOf) = (new EyeTracks(assets), assets);
        // The head's own motion through an action held to a small allowance about where the motion alone has the eyes.
        if (settings.ActionAllowance >= 0f && MotionAlone(world, e, assets, hidden, settings, out var alone))
            head = alone + Allow(head - alone, settings.ActionAllowance);
        head.Z -= line;   // a person posed on the line (People.PersonGround) is followed without it, the line laid on after as a step's lift
        head = Unpopped(world, e, head, settings);
        var calm = head - tracks.Bob(anims.Get(e));
        if (ease.SteadyNext != world.Tick) ease.Steady1 = ease.Steady2 = calm;
        else
        {
            float a = 1f - DetMath.Exp(-World.TickSeconds / MathF.Max(settings.SteadySeconds, World.TickSeconds));
            ease.Steady1 = Leash(ease.Steady1 + (calm - ease.Steady1) * a, calm, settings.SteadyLeash);
            ease.Steady2 = Leash(ease.Steady2 + (ease.Steady1 - ease.Steady2) * a, calm, settings.SteadyLeash);
        }
        ease.SteadyNext = world.Tick + 1;
        steady = ease.Steady2;
        return true;
    }

    /// <summary>
    /// The eyes (model space) of a person whose head the turn as a chain has turned from the view
    /// (<see cref="Animation.People.PersonTurn.Head"/>), turned back about the neck's joint as the pose has it: where they would be with
    /// the head on the view. The eye is the simulation's (the first-person camera, the reticle, use, the aim), and the view is the
    /// body's facing; so the chain's catching up is drawn but moves no eye. Unchanged for anyone else. with the earlier build's waist (the trunk
    /// left on the pelvis, the body turned about the root) every turn of the chain is undone about its own joint
    /// (<see cref="Animation.People.TurnChain.EyesOnTheView"/>); with the earlier build's (<see cref="CharacterAssets.PersonWaistEven"/>) as before.
    /// </summary>
    static Vector3 OnTheView(Vector3 eyes, HeadCollapse hidden, ReadOnlySpan<Matrix4x4> skin, in Animation.People.PersonTurn chain,
                             CharacterAssets assets, in CharacterAnimation state, float legs)
    {
        if (!chain.On || (uint)hidden.Root >= (uint)skin.Length) return eyes;
        // With the earlier build's waist the body turns about the root and the trunk on the pelvis: each turn undone about its joint.
        if (!assets.PersonWaistEven && assets.Kind(state.Kind).Person is { } person)
            return Animation.People.TurnChain.EyesOnTheView(assets.ModelOf(state.Kind).Skeleton, assets, state, person, legs, chain, skin, eyes);
        if (chain.Head == 0f) return eyes;
        var neck = PoseMath.Transform(hidden.Pivot, skin[hidden.Root]);
        var (sin, cos) = DetMath.SinCos(-chain.Head);
        float x = eyes.X - neck.X, y = eyes.Y - neck.Y;
        return new Vector3(neck.X + x * cos - y * sin, neck.Y + x * sin + y * cos, eyes.Z);
    }

    /// <summary>
    /// The eyes (model space, m) where the <b>motion alone</b> would pose the head — the locomotion channel
    /// (the gait, its lean, a crouch, a jump, a landing, a move, a ladder, a hang) and the legs' turn and the rig's placing,
    /// without the action channel (a swing, a block, a flinch; a death is kept: the body really falls), the guard's layer and
    /// stance, or the aim's bend; for a dodge, the motion it took over from (the stand when it took over from none) with the
    /// dodge's duck (<see cref="CameraSettings.DodgeDuck"/>) but not its sway. False when nothing is laid over the motion (the
    /// head's eyes are the motion's own), so a body that only walks costs nothing more.
    /// </summary>
    bool MotionAlone(World world, EntityId e, CharacterAssets assets, HeadCollapse hidden, CameraSettings settings, out Vector3 eyes)
    {
        eyes = default;
        var s = world.Get<CharacterAnimation>(e);
        bool action = s.Action != ActionRole.None && s.ActionWeight > 0f && !ActionRoles.IsDeath(s.Action);
        bool guard = s.GuardWeight > 0f || s.GuardSide != 0f || s.GuardHeight != 0f || s.GuardTwist != 0f;
        var kind = assets.Kind(s.Kind);
        int dodgeClip = kind.Motion(MotionRole.Dodge).Clip;
        bool dodge = dodgeClip >= 0 && (s.Clip == dodgeClip || (s.PreviousClip == dodgeClip && s.Blend < 1f));
        if (!action && !guard && !dodge) return false;
        if (action) { s.Action = ActionRole.None; s.ActionWeight = 0f; s.ActionFrom = ActionRole.None; }
        if (guard) { s.GuardWeight = 0f; s.GuardSide = 0f; s.GuardHeight = 0f; s.GuardTwist = 0f; }
        float duck = 0f;
        if (dodge)
        {
            // The dodge's duck: how far its pose has the eyes below the stand's (taken back in below, by DodgeDuck).
            int stand = kind.Motion(MotionRole.Idle).Clip;
            bool current = s.Clip == dodgeClip;
            float weight = current ? (s.PreviousClip >= 0 ? Math.Clamp(s.Blend, 0f, 1f) : 1f) : 1f - Math.Clamp(s.Blend, 0f, 1f);
            float at = current ? s.Time : s.PreviousTime;
            if (stand >= 0) duck = weight * MathF.Min(0f, tracks!.Eyes(s.Kind, dodgeClip, at).Z - tracks.Eyes(s.Kind, stand, 0f).Z);
            if (current) { s.Clip = stand; s.Time = 0f; }
            if (s.PreviousClip == dodgeClip || (current && s.PreviousClip < 0)) { s.PreviousClip = s.PreviousClip == dodgeClip ? stand : -1; s.PreviousTime = 0f; }
        }
        int n = assets.ModelOf(s.Kind).Skeleton.Count;
        if (aloneModel.Length < n) (aloneModel, aloneSkin) = (new Matrix4x4[n], new Matrix4x4[n]);
        ref var rig = ref world.Store<PoseRig>().Find(e);
        if (Unsafe.IsNullRef(ref rig)) alonePose.Evaluate(assets, s, aloneModel, aloneSkin);
        else
        {
            var flat = rig;
            flat.AimPitch = 0f;
            alonePose.Evaluate(assets, s, flat, aloneModel, aloneSkin, default, CharacterPoses.Chain(world, e));   // a person's chain as drawn
        }
        eyes = OnTheView(hidden.EyesIn(aloneSkin.AsSpan(0, n)), hidden, aloneSkin.AsSpan(0, n), CharacterPoses.Chain(world, e), assets, s, Unsafe.IsNullRef(ref rig) ? 0f : rig.LegYaw)
               + new Vector3(0f, 0f, duck * Math.Clamp(settings.DodgeDuck, 0f, 1f));
        return true;
    }

    /// <summary>
    /// The head's eyes (model space) with the animation's <b>jumps</b> eased out: a move of the eyes in one
    /// tick larger than <see cref="CameraSettings.PopStep"/> is not the body's motion (a run's head moves at most 4.5 cm a
    /// tick) but the pose snapping — 1.1's channel keeps one fade, so a fade begun while another is under way drops the
    /// older motion's share at once (stopping from a run, Christof's head jumps 0.275 m: run → walk → stand) — and the
    /// view would jump with it (the steady eye's leash draws it along). On the ground only, the jump is taken back and
    /// paid out over <see cref="CameraSettings.PopSeconds"/>. 0 turns it off.
    /// </summary>
    Vector3 Unpopped(World world, EntityId e, Vector3 head, CameraSettings settings)
    {
        bool fresh = popNext != world.Tick || popOf != e || settings.PopSeconds <= 0f;
        if (fresh) pop = Vector3.Zero;
        else
        {
            pop *= DetMath.Exp(-World.TickSeconds / settings.PopSeconds);
            // On the ground only: a move's, a ladder's or a hang's pose may truly move the head fast, and is the body's.
            var jump = head - popLast;
            if (jump.Length() > settings.PopStep && world.Get<Body>(e).State == BodyState.Ground) pop -= jump;
        }
        (popLast, popOf, popNext) = (head, e, world.Tick + 1);
        return head + pop;
    }

    /// <summary>
    /// <paramref name="d"/> held to <paramref name="most"/> (m) smoothly — its length becomes
    /// <c>most · tanh(|d| / most)</c>: a move much smaller than the allowance passes almost whole, a large one tends to it.
    /// </summary>
    static Vector3 Allow(Vector3 d, float most)
    {
        float len = d.Length();
        if (len < 1e-6f || most <= 0f) return most <= 0f ? Vector3.Zero : d;
        float x = 2f * len / most;
        float tanh = x > 20f ? 1f : 1f - 2f / (DetMath.Exp(x) + 1f);
        return d * (most * tanh / len);
    }

    /// <summary>
    /// How far the third-person camera lags behind where it would stand on the body (the lag,
    /// m), following <paramref name="target"/> (the body's eyes, eased over steps and crouches) with a <b>critically damped
    /// lag</b> that answers only to the body's changes of speed: the lag <c>e</c> takes each tick's change of the target's velocity into
    /// its rate, and decays as a critically damped spring of time <see cref="CameraSettings.ThirdPersonFollowSeconds"/>
    /// (<c>e(t) = (e₀ + (ė₀ + ωe₀)t)·e^(−ωt)</c>, ω its inverse). At a steady pace there is no lag (the body keeps its place
    /// in the picture); a start, a stop, a dodge's burst or a landing is followed smoothly, the lag at most Δv·T/e (a
    /// Celerity sprint's start of 25 m/s: 0.74 m at T 0.08 s) and never more than <see cref="CameraSettings.ThirdPersonLeash"/>.
    /// A placing (a move across of more than <see cref="CameraSettings.EasePut"/> in a tick), another body or a gap starts it
    /// again with no lag. T 0 is the earlier build's camera (no lag). The look is the player's at once: only the place is eased.
    /// </summary>
    Vector3 Follow(World world, EntityId e, Vector3 target, CameraSettings settings)
    {
        float t = settings.ThirdPersonFollowSeconds;
        float dt = World.TickSeconds;
        bool fresh = followNext != world.Tick || followOf != e || t <= 0f;
        var velocity = (target - followLast) / dt;
        // A fresh start (the view just turned to third person, say mid-run) knows the body's velocity a tick later: no lag
        // until then, and then none for the pace it already had.
        bool placing = !fresh && Vector3.Distance(target, followLast) > settings.EasePut;
        if (fresh || placing || !followKnown)
        {
            (lag, lagRate) = (Vector3.Zero, Vector3.Zero);
            (followVelocity, followKnown) = fresh || placing ? (Vector3.Zero, false) : (velocity, true);
        }
        else
        {
            lagRate += velocity - followVelocity;
            followVelocity = velocity;
            float w = 1f / t, x = w * dt, fade = DetMath.Exp(-x);
            var k = lagRate + w * lag;
            (lag, lagRate) = ((lag + k * dt) * fade, (lagRate - w * k * dt) * fade);
            float len = lag.Length();
            if (len > settings.ThirdPersonLeash && len > 0f)
            {
                var dir = lag / len;
                lag = dir * settings.ThirdPersonLeash;
                float outward = Vector3.Dot(lagRate, dir);
                if (outward > 0f) lagRate -= dir * outward;
            }
        }
        followLast = target;
        followOf = e;
        followNext = world.Tick + 1;
        return lag;
    }

    /// <summary>
    /// (From the swing arc's pictures: over the shoulder a foe 1.0 to 1.5 m ahead stands behind
    /// the player's own back): how much of the <b>melee framing</b> is on, 0 to 1 — eased toward 1 over
    /// <see cref="CameraSettings.MeleeFramingSeconds"/> while <see cref="CameraSettings.MeleeFraming"/> is set and the body
    /// has a melee weapon or its fists out (it fights: a <see cref="Control.Combatant"/> and its kind's attacks), toward 0
    /// otherwise. 0 when the setting is off (the camera as before).
    /// </summary>
    float MeleeWeight(World world, EntityId e, CameraSettings settings)
    {
        if (!settings.MeleeFraming) { meleeOf = EntityId.None; return meleeWeight = 0f; }
        bool armed = world.TryStore<Control.Combatant>(out var fighters) && fighters.Has(e)
                     && world.TryService<CharacterAssets>(out var assets) && world.Store<CharacterAnimation>().Has(e)
                     && assets.Kind(world.Get<CharacterAnimation>(e).Kind).Actions is not null;
        float want = armed ? 1f : 0f;
        bool fresh = meleeNext != world.Tick || meleeOf != e || settings.MeleeFramingSeconds <= 0f;
        meleeWeight = fresh ? want : meleeWeight + (want - meleeWeight) * (1f - DetMath.Exp(-World.TickSeconds / settings.MeleeFramingSeconds));
        (meleeOf, meleeNext) = (e, world.Tick + 1);
        return meleeWeight;
    }

    /// <summary>
    /// The boom as the walls left it, let out again eased: a wall that comes between pulls the camera in at
    /// once (it never passes through), and when it is gone the boom lengthens back over
    /// <see cref="CameraSettings.BoomOutSeconds"/> rather than jumping out in a tick (running past a pillar). 0: at once.
    /// </summary>
    Vector3 Boomed(World world, EntityId e, Vector3 arm, CameraSettings settings)
    {
        float want = arm.Length();
        bool fresh = boomNext != world.Tick || boomOf != e || settings.BoomOutSeconds <= 0f;
        float now = fresh || want <= boomNow ? want : boomNow + (want - boomNow) * (1f - DetMath.Exp(-World.TickSeconds / settings.BoomOutSeconds));
        (boomNow, boomOf, boomNext) = (now, e, world.Tick + 1);
        return want > 1e-6f ? arm * (now / want) : arm;
    }

    /// <summary>
    /// The boom's length (<paramref name="want"/>, <see cref="BoomClear"/>'s) eased both ways: out over
    /// <see cref="CameraSettings.BoomOutSeconds"/> as before, and in by a quick spring of <see cref="CameraSettings.BoomInSeconds"/> so long as
    /// the camera has room on the way in (<see cref="Room"/>); where it has not, at once — the camera never stands in a wall. A corner that comes between the camera and the body (the line cut, the camera itself in the open) is zoomed in on over a
    /// few ticks rather than jumped: an earlier build pulled in at once always, 0.8 m in a tick as the line swept across a block's corner.
    /// </summary>
    Vector3 BoomedCalm(World world, EntityId e, CollisionWorld level, Vector3 pivot, Vector3 boom, float want, CameraSettings settings)
    {
        float len = boom.Length();
        if (len < 1e-6f) return Vector3.Zero;
        var dir = boom / len;
        bool fresh = boomNext != world.Tick || boomOf != e;
        float now;
        if (fresh) now = want;
        else if (want >= boomNow)
            now = settings.BoomOutSeconds > 0f ? boomNow + (want - boomNow) * (1f - DetMath.Exp(-World.TickSeconds / settings.BoomOutSeconds)) : want;
        else
        {
            float eased = settings.BoomInSeconds > 0f ? boomNow + (want - boomNow) * (1f - DetMath.Exp(-World.TickSeconds / settings.BoomInSeconds)) : want;
            now = Room(level, pivot, dir, eased, settings.ProbeRadius, settings.CornerGive) ? eased : want;
        }
        (boomNow, boomOf, boomNext) = (now, e, world.Tick + 1);
        return dir * now;
    }

    /// <summary>Whether the body is one of the models repository's people (its kind has a person): the calm walls are theirs.</summary>
    static bool IsPerson(World world, EntityId e) =>
        world.TryService<CharacterAssets>(out var assets) && world.Store<CharacterAnimation>().Has(e) && assets.Kind(world.Get<CharacterAnimation>(e).Kind).Person is not null;

    /// <summary>
    /// How much of the third-person pivot's offset from the head (<paramref name="offset"/>: to the shoulder's side and up) the walls
    /// allow, as shares (0 to 1) of its rise and of its side, the rise swept first, then the side from there: <c>Soft</c> keeps the pivot
    /// <see cref="CameraSettings.WallMargin"/> off a wall beyond the probe's radius, <c>Hard</c> only <see cref="CameraSettings.ShoulderStopRadius"/>
    /// (the most it may be, ever). An earlier build stopped the whole offset at once with its sphere a centimetre off the wall.
    /// </summary>
    public static (float SoftRise, float SoftSide, float HardRise, float HardSide) Shoulder(CollisionWorld level, Vector3 head, Vector3 offset, CameraSettings settings)
    {
        var rise = new Vector3(0f, 0f, offset.Z);
        var across = offset - rise;
        float r = MathF.Min(settings.ProbeRadius, MathF.Max(0.01f, settings.ShoulderStopRadius)), wide = settings.ProbeRadius + MathF.Max(0f, settings.WallMargin);
        float Share(Vector3 from, Vector3 move, float radius)
        {
            float len = move.Length();
            return len < 1e-6f ? 1f : Reach(level, from, move, radius).Length() / len;
        }
        float softRise = Share(head, rise, wide), hardRise = Share(head, rise, r);
        return (softRise, Share(head + rise * softRise, across, wide), hardRise, Share(head + rise * hardRise, across, r));
    }

    /// <summary>
    /// The shoulder's offset as the walls allow it, eased (<see cref="Shoulder"/>'s shares of its rise and side): toward the soft share
    /// by a quick spring of <see cref="CameraSettings.ShoulderInSeconds"/> when a wall brings it in, out over
    /// <see cref="CameraSettings.BoomOutSeconds"/> when the wall lets it go, and never beyond the hard share (within
    /// <see cref="CameraSettings.ShoulderStopRadius"/> of a wall) — so running into a wall at a slant the camera slides in over a few ticks,
    /// not 0.4 m in three (a play-test's tape at 25.6 s). Only the shares are eased: the offset turns with the look at once.
    /// </summary>
    Vector3 Shouldered(World world, EntityId e, Vector3 offset, (float SoftRise, float SoftSide, float HardRise, float HardSide) walls, CameraSettings settings)
    {
        bool fresh = shoulderNext != world.Tick || shoulderOf != e;
        float dt = World.TickSeconds;
        float inA = settings.ShoulderInSeconds > 0f ? 1f - DetMath.Exp(-dt / settings.ShoulderInSeconds) : 1f;
        float outA = settings.BoomOutSeconds > 0f ? 1f - DetMath.Exp(-dt / settings.BoomOutSeconds) : 1f;
        float Ease(float was, float want, float most) => MathF.Min(most, fresh ? want : was + (want - was) * (want < was ? inA : outA));
        shoulderRise = Ease(shoulderRise, walls.SoftRise, walls.HardRise);
        shoulderSide = Ease(shoulderSide, walls.SoftSide, walls.HardSide);
        (shoulderOf, shoulderNext) = (e, world.Tick + 1);
        var rise = new Vector3(0f, 0f, offset.Z);
        return rise * shoulderRise + (offset - rise) * shoulderSide;
    }

    /// <summary>
    /// How far along <paramref name="boom"/> from <paramref name="pivot"/> the camera may stand (m): as far as a sphere of
    /// <paramref name="radius"/> swept along it goes, or further — up to where the line itself meets the level, less the radius —
    /// where the camera has room (<see cref="Room"/>): a corner the swept sphere only grazes on the way no longer pulls the camera in to the
    /// head (from 2.5 m to 3 cm in a tick passing a block's corner, a play-test's tape at 25.7 s). Searched from the furthest in steps
    /// of 5 cm and the last step halved four times, so the length moves smoothly as a corner comes beside the camera; deterministic,
    /// allocates nothing.
    /// </summary>
    public static float BoomClear(CollisionWorld level, Vector3 pivot, Vector3 boom, float radius, float give = 0f)
    {
        float len = boom.Length();
        if (len < 1e-6f) return 0f;
        var dir = boom / len;
        if (level.SphereSweep(pivot, dir, len, radius) is not { } hit) return len;
        float swept = MathF.Max(0f, hit.Distance - 0.01f);
        float top = level.Ray(pivot, dir, len) is { } line ? MathF.Min(len, line.Distance - radius - 0.01f) : len;
        if (top <= swept) return swept;
        if (Room(level, pivot, dir, top, radius, give)) return top;
        float bad = top;
        for (float at = top - 0.05f; at > swept; at -= 0.05f)
        {
            if (!Room(level, pivot, dir, at, radius, give)) { bad = at; continue; }
            float good = at;
            for (int i = 0; i < 4; i++)
            {
                float mid = 0.5f * (good + bad);
                if (Room(level, pivot, dir, mid, radius, give)) good = mid; else bad = mid;
            }
            return good;
        }
        return swept;
    }

    /// <summary>
    /// Whether a camera <paramref name="at"/> metres along <paramref name="dir"/> from <paramref name="pivot"/> has room: its sphere of
    /// <paramref name="radius"/> no more than <paramref name="give"/> into the level (<see cref="CameraSettings.CornerGive"/>: an edge beside
    /// it may touch the sphere's rim, never the picture's near plane).
    /// </summary>
    public static bool Room(CollisionWorld level, Vector3 pivot, Vector3 dir, float at, float radius, float give = 0f) =>
        level.Penetration(new Capsule(pivot + dir * at, 0f, radius)) <= give;

    /// <summary><paramref name="p"/> drawn to within <paramref name="most"/> of <paramref name="to"/>.</summary>
    static Vector3 Leash(Vector3 p, Vector3 to, float most)
    {
        var d = p - to;
        float len = d.Length();
        return len > most && len > 0f ? to + d * (most / len) : p;
    }

    /// <summary>
    /// The eye <paramref name="eye"/> kept out of the level: a sphere of <paramref name="radius"/> swept to it from the
    /// body's axis (<paramref name="axis"/>, x and y) at the eye's height, or the capsule's <paramref name="top"/> when the
    /// eye is above it, stops it short of a wall or a ceiling between.
    /// </summary>
    static Vector3 Clear(CollisionWorld level, Vector3 axis, Vector3 eye, float top, float radius)
    {
        var from = new Vector3(axis.X, axis.Y, MathF.Min(eye.Z, top));
        return from + Reach(level, from, eye - from, radius);
    }

    /// <summary>How much of <paramref name="move"/> from <paramref name="from"/> a sphere of <paramref name="radius"/> can travel before it touches the level.</summary>
    static Vector3 Reach(CollisionWorld level, Vector3 from, Vector3 move, float radius)
    {
        float len = move.Length();
        if (len < 1e-6f) return Vector3.Zero;
        var dir = move / len;
        return level.SphereSweep(from, dir, len, radius) is { } hit ? dir * MathF.Max(0f, hit.Distance - 0.01f) : move;
    }
}
