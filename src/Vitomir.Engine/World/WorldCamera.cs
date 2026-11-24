// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine's one camera, the game camera that follows the
// character, the cutscene camera the scripts drive shot by shot, camera paths, and the screen fades.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// What the camera is doing (`camera+4`). The engine keeps a function pointer beside it
/// and calls that every frame; the pairs are fixed.
/// </summary>
public enum CameraMode
{
    None = 0,
    /// <summary>Following the steered character. What the player sees nearly all the time.</summary>
    Game = 1,
    /// <summary>
    /// Looking out of the character's eyes while <b>Z</b> —
    /// or the fourth mouse button — is held.
    /// </summary>
    Look = 2,
    /// <summary>No character to follow: the angles and the eye are used as they stand.</summary>
    Free = 3,
    /// <summary>Playing a camera path out of `paths/*.ncp`.</summary>
    Path = 4,
    /// <summary>A script's cutscene: shots set with <c>SetShot</c>.</summary>
    Cutscene = 5,
}

/// <summary>
/// The game camera, one per game. Everything the player looks through,
/// ported field by field, so 1.1's camera and this one can be
/// compared field for field.
///
/// **What it owns and what it does not.** It decides where the eye is, where it looks, the field
/// of view and the four screen effects. It does not draw anything, and it does not read input: the
/// harness turns keys into <see cref="TurnPitch"/>, <see cref="TurnYaw"/> and <see cref="Zoom"/>,
/// which are the entry points the engine's own input code calls.
///
/// **The world is asked three questions and they are handed in**, so that a test can build a
/// camera with no level: how far a ray gets before something stops it (<see cref="Obstruction"/>,
/// the game camera's pull-in), where the first wall between a shot's subject and its eye
/// is (<see cref="SightLine"/>, the cutscene's), and where a character's head is
/// (<see cref="HeadOf"/>, the cutscene framing). <see cref="CameraSweep"/> answers the
/// first two the way the engine does.
///
/// </summary>
public sealed partial class WorldCamera
{
    // ---------------------------------------------------------------- what everything else reads

    /// <summary>The mode, <c>camera+4</c>.</summary>
    public CameraMode Mode { get; private set; } = CameraMode.Game;

    /// <summary>Where the eye is (and its copy).</summary>
    public Vector3 Eye;

    /// <summary>
    /// The camera's Euler angles in degrees (and a copy): <b>X is the
    /// pitch, Y the roll, Z the yaw</b>, each kept in 0..360 by the angle wrap. The engine builds its
    /// matrix from them in the thing matrix's own order (Y, then X, then Z), so the view
    /// runs along the matrix's second row — <c>(−cos X sin Z, cos X cos Z, sin X)</c>.
    /// </summary>
    public Vector3 Angles;

    /// <summary>
    /// The point it looks at. The game camera puts it 128 units above the
    /// character's origin; a cutscene shot puts it where the shot's framing says.
    /// </summary>
    public Vector3 Target;

    /// <summary>
    /// The field of view in degrees and where it is heading. The
    /// engine's fov is <b>horizontal</b>; the game camera's is 80.
    /// </summary>
    public float Fov = DefaultFov, FovTarget = DefaultFov;

    /// <summary>The game camera's field of view, and a path's (80.0).</summary>
    public const float DefaultFov = 80f;

    /// <summary>
    /// The camera matrix, rows as the engine keeps them: right, forward, up, and
    /// the eye as the translation. What the renderer is handed.
    /// </summary>
    public Matrix4x4 Frame = Matrix4x4.Identity;

    /// <summary>The way the eye is looking, the frame's second row.</summary>
    public Vector3 Forward => new(Frame.M21, Frame.M22, Frame.M23);

    /// <summary>Which way is up for the eye, the frame's third row. Z unless something has rolled it.</summary>
    public Vector3 Up => new(Frame.M31, Frame.M32, Frame.M33);

    /// <summary>
    /// Whether the mouse pointer is drawn (tested by the cursor
    /// draw). A cutscene hides it and a release shows it again; a conversation's
    /// reply list shows it and choosing a reply hides it. It is a flag on
    /// the side of the camera in the original too — a global the camera happens to be the main
    /// writer of.
    /// </summary>
    public bool CursorShown { get; set; } = true;

    /// <summary>
    /// Whether the status bar's panes are down for a cutscene. Entering a
    /// cutscene takes them down; releasing lays the screen out again.
    /// </summary>
    public bool StatusBarHidden => Mode is CameraMode.Cutscene or CameraMode.Path;

    // ------------------------------------------------------------------ what the world answers

    /// <summary>
    /// How far a ray from a point along a direction gets before it is stopped, or null for
    /// nowhere. The game camera asks it from the target back towards the eye, out to where the eye
    /// is (a radius-10 cast); <see cref="CameraSweep.GameCamera"/> is the level's answer.
    /// </summary>
    public Func<Vector3, Vector3, float, float?>? Obstruction { get; set; }

    /// <summary>
    /// Where a character's head is, for framing a shot: the <c>Helmet</c> tag's bone,
    /// posed and placed, when the thing has a skeleton that names one. Null means "no head", and
    /// the shot falls back to the origin raised by the body's height or radius.
    /// </summary>
    public Func<WorldThing, Vector3?>? HeadOf { get; set; } = HelmetOf;

    /// <summary>
    /// The motion-set tag the head is found by (the motion set is asked for <c>0x14</c> by the game
    /// camera and again by the cutscene framing).
    /// </summary>
    public const int HelmetTag = 0x14;

    /// <summary>
    /// The engine's own head lookup (the same in the cutscene framing): a thing with a
    /// skeleton and a motion set whose tag table names <see cref="HelmetTag"/> has its head at that
    /// bone's origin, posed at the current frame and carried out by the thing's own matrix.
    /// Anything else has none, and the caller falls back.
    /// </summary>
    public static Vector3? HelmetOf(WorldThing thing)
    {
        if (thing.Animation is not { BoneCount: > 0 } animation) return null;
        int bone = animation.TagBone(HelmetTag);
        if (bone < 0) return null;
        return (animation.BoneMatrix(bone) * thing.World).Translation;
    }

    /// <summary>The steered character, which the game camera follows and a release returns to.</summary>
    public WorldThing? Subject { get; private set; }

    // ---------------------------------------------------------------- the game camera's settings

    /// <summary>
    /// The pitch limits, stored wrapped: <b>280 and 340</b>, which is
    /// −80° to −20° — the camera always looks down, never flat and never straight down.
    /// </summary>
    public float PitchLow = Wrap(-80f), PitchHigh = Wrap(-20f);

    /// <summary>
    /// How far up the game camera turns, in degrees from level (ours): the base game's 20° below level unless the game
    /// sets it from <see cref="CameraChoices.Up"/>. A new camera is the base game's.
    /// </summary>
    public float UpLimit = CameraChoices.BaseUp;

    /// <summary>Whether the game camera is smoothed on steps (ours, <see cref="CameraChoices.Steps"/>). A new camera is not.</summary>
    public bool SmoothSteps;

    /// <summary>
    /// <see cref="UpLimit"/> stored wrapped, as the clamps use it: the base game's <see cref="PitchHigh"/> at 20° below level,
    /// up to just short of level — a hundredth of a degree under, so that the wrapped limit stays above
    /// <see cref="PitchLow"/> and the clamps compare as the base game's do.
    /// </summary>
    public float PitchTop => Wrap(MathF.Min(CameraChoices.Held(UpLimit), -0.01f));

    WorldThing? stepWho;
    Vector3 stepFrom;
    float stepLift;

    /// <summary>
    /// The camera's own lift on steps this frame (ours, <see cref="SmoothSteps"/>): the change in the height the
    /// subject is shown at taken out and paid back by <see cref="Levels.Cells.CellStepEase.Next"/>'s rule. Nothing when
    /// smoothing is off, for a new subject, after a fall or a move by a script (the rule clears it), or when the camera
    /// has not been following (<see cref="Reset"/>).
    /// </summary>
    float StepLift(WorldThing thing, float seconds)
    {
        var shown = thing.Position + new Vector3(0, 0, thing.ShownLift);
        stepLift = SmoothSteps && ReferenceEquals(stepWho, thing)
            ? Levels.Cells.CellStepEase.Next(stepLift, stepFrom, shown, seconds)
            : 0f;
        stepWho = thing;
        stepFrom = shown;
        return stepLift;
    }

    /// <summary>The camera's lift on steps as of the last frame (ours; for the diagnostics and the tests).</summary>
    public float StepLiftNow => stepLift;

    /// <summary>
    /// The first-person look's pitch limits: −80° and +80°, kept
    /// signed by the angle code.
    /// </summary>
    public float LookPitchLow = Signed(-80f), LookPitchHigh = Signed(80f);

    /// <summary>
    /// How far behind the target the camera wants to be (one at the shallowest pitch,
    /// one at the steepest): <b>300 and 512</b>, eased between by the pitch — see
    /// <see cref="GameUpdate"/>.
    /// </summary>
    public float NearDistance = 300f, FarDistance = 512f;

    /// <summary>How high above the character's origin the target sits: 128.</summary>
    public float TargetHeight = 128f;

    /// <summary>
    /// The distance it is at, the one it is heading for and how fast
    /// it gets there, in units a second. A new camera starts at 354.79, which is what
    /// the eased distance comes to at the starting pitch of −30° with the engine's table cosine
    /// (<see cref="TableTrig"/>: 354.7911) — Vitomir's library cosine made it 354.87 until then.
    /// </summary>
    public float Distance = StartDistance, DistanceTarget = StartDistance, DistanceRate = 1000f;

    /// <summary>The distance a fresh camera is built at (0x43b1653f as a float, 354.79).</summary>
    public const float StartDistance = 354.79f;

    /// <summary>The ceiling on a shrinking distance.</summary>
    public const float DistanceCeiling = 50_000f;

    /// <summary>
    /// How fast the pitch and the yaw are being turned, in degrees a
    /// second, each held to ±180. The input code adds to them while a
    /// key is down; <see cref="StopTurning"/> puts them back to zero.
    /// </summary>
    public float PitchRate, YawRate;

    /// <summary>The turn rate limits.</summary>
    public float PitchRateLimit = 180f, YawRateLimit = 180f;

    /// <summary>
    /// The zoom: the eased distance is multiplied by it. 1 is as far out as the
    /// camera goes; the zoom keys bring it in to 0.1 and no further.
    /// </summary>
    public float ZoomScale = 1f;

    /// <summary>The zoom's floor and ceiling (0.1 and 1.0).</summary>
    public const float ZoomMin = 0.1f, ZoomMax = 1f;

    /// <summary>
    /// How far out the zoom goes (ours, <see cref="CameraChoices.Out"/>): the ceiling on <see cref="ZoomScale"/>, the base
    /// game's <see cref="ZoomMax"/> unless the game sets it from the choice, held to 1..3. A new camera is the base game's;
    /// a new camera still starts at a scale of 1 (the base game's distance), so going further out is the player's own doing.
    /// </summary>
    public float ZoomOut = ZoomMax;

    /// <summary>
    /// How what is in the way is seen through (ours, <see cref="CameraChoices.SeeThrough"/>). With the soft window
    /// (<see cref="SeeThroughMode.On"/>) the camera still pulls in as the base game's does, and the frame fades what is still in
    /// the way (<see cref="World.SeeThrough"/>); with the developer's x-ray (<see cref="SeeThroughMode.XRay"/>) the game camera's
    /// pull-in (<see cref="Obstruction"/>) and the cutscene eye's (<see cref="SightLine"/>) are not asked at all.
    /// A new camera is the base game's: off.
    /// </summary>
    public SeeThroughMode SeeThrough;

    /// <summary>The pitch a camera starts at, and is put back to by a release: −30°.</summary>
    public const float StartPitch = -30f;

    /// <summary>
    /// A new target is eased into over this many **milliseconds** rather than jumped to, when the
    /// old and new targets are within <see cref="HandoverReach"/> of each other (with a
    /// countdown and the old point kept). Selecting another party member is
    /// what this is for. A flag turns it on and a new camera has it on.
    /// </summary>
    public const int HandoverMs = 100;

    /// <summary>How close the two targets must be for the handover to be eased.</summary>
    public const float HandoverReach = 1000f;

    /// <summary>The handover flag.</summary>
    public bool EaseHandover = true;

    int handoverLeft;
    Vector3 handoverFrom;

    /// <summary>
    /// The engine's "the camera still has something to settle" bit (<c>camera+0 &amp; 1</c>): set
    /// by a shot and by the game camera every frame, cleared once the fov and the distance have
    /// both arrived.
    /// </summary>
    public bool Settling { get; private set; }

    /// <summary>
    /// How far back the level let the eye sit this frame, or null when nothing was in the way — the
    /// pull-in's own answer, kept so a run can record when it flickers.
    /// </summary>
    public float? PulledTo { get; private set; }

    // ---------------------------------------------------------------------------- building one

    /// <summary>A new camera, in the state the constructor leaves one in.</summary>
    public WorldCamera() => Reset();

    /// <summary>
    /// Everything back to what a new camera holds. Called at the start of a level
    /// before the camera is pointed at the character.
    /// </summary>
    public void Reset()
    {
        SlewReset();
        stepWho = null; stepLift = 0f;
        Mode = CameraMode.Game;
        Eye = Vector3.Zero;
        Angles = Vector3.Zero;
        Target = Vector3.Zero;
        Fov = FovTarget = DefaultFov;
        Distance = DistanceTarget = StartDistance;
        DistanceRate = 1000f;
        PitchRate = YawRate = 0f;
        ZoomScale = 1f;
        EaseHandover = true;
        handoverLeft = 0;
        Subject = null;
        cutA = cutB = null;
        shotIndex = 0;
        path = null;
        Frame = Matrix4x4.Identity;
        FullDetail = false;
        // A new camera shows the pointer.
        CursorShown = true;
        Effects.ClearAll();
    }

    /// <summary>
    /// The camera taking up its place when a level begins: reset, follow the steered
    /// character, pitch down 30° and face the way the character faces. With nobody to follow it
    /// is a free camera (mode 3) that simply shows its angles.
    ///
    /// **The character is drawn again** (render flag <c>0x80</c> cleared): the first-person
    /// look's bit (<see cref="RenderFirstPerson"/>) is cleared, so a character who left a level
    /// with the look key down — walking on through the exit while looking round — is not
    /// invisible in the next. Earlier, Vitomir left the bit, and only a press and release of the
    /// look key brought the character back.
    /// </summary>
    public void Start(WorldThing? steered)
    {
        Reset();
        if (steered is null) { Mode = CameraMode.Free; return; }
        SetSubject(steered);
        Angles.X = Wrap(StartPitch);
        Angles.Z = Wrap(steered.Orientation.Z + 360f);
        steered.RenderFlags &= ~RenderFirstPerson;
        Mode = CameraMode.Game;
    }

    /// <summary>
    /// Following somebody new. The target snaps to them unless they are close to the
    /// last one, in which case it slides over for a tenth of a second — the camera does not jump
    /// when you select the character standing next to you. The auto slew starts over from the new
    /// subject's facing, its clock not (<see cref="SlewFollow"/>).
    /// </summary>
    public void SetSubject(WorldThing? thing)
    {
        if (thing is null) return;
        handoverLeft = 0;
        var previous = Subject;
        Subject = thing;
        SlewFollow(thing);
        if (EaseHandover && previous is not null && !ReferenceEquals(previous, thing)
            && Vector3.Distance(previous.Position, thing.Position) <= HandoverReach)
        {
            handoverFrom = previous.Position + new Vector3(0, 0, TargetHeight);
            handoverLeft = HandoverMs;
        }
    }

    // ------------------------------------------------------------------------------ the tick

    /// <summary>
    /// One frame of the camera, in the engine's order: the screen effects, the fov and
    /// distance easing if anything is settling, the mode's own update, and the frame handed on.
    /// </summary>
    /// <param name="seconds">The frame's length in seconds.</param>
    /// <param name="nowMs">The game clock.</param>
    public void Tick(float seconds, int nowMs)
    {
        int frameMs = (int)MathF.Round(seconds * 1000f);
        (lastSeconds, lastFrameMs, lastNowMs) = (seconds, frameMs, nowMs);
        Effects.Step(nowMs, frameMs);
        if (Settling) Ease(seconds);
        Update(seconds, frameMs, nowMs);
    }

    /// <summary>
    /// **The game camera while the game's clock stands**. The engine keeps two clocks:
    /// the interface's (its frame's length; its pause nothing
    /// sets) and the game's (zero while the game pause is set — the Pause
    /// key, a menu, the advancement screen). The game camera turns and eases by the interface's,
    /// so on a paused game it still turns at the keypad's rate and the screen
    /// edge's, settles its distance and finishes a hand-over slide — while the zoom keys, which step
    /// by the game's, do nothing. The frame's own <see cref="Tick"/> then runs with the
    /// game's zero and moves nothing more. Only the game camera: the Pause key is not read in a path or
    /// a cutscene, and a menu cannot be opened in one. The screen effects stay on the
    /// frame's tick (ours: they are dated by the game clock here, by the interface's total in 1.1).
    /// </summary>
    public void InterfaceTick(float seconds)
    {
        if (Mode != CameraMode.Game || seconds <= 0f) return;
        if (Settling) Ease(seconds);
        GameUpdate(seconds, (int)MathF.Round(seconds * 1000f));
    }

    /// <summary>
    /// The last frame's length and the clock it ran at (the frame's
    /// milliseconds): a shot set from a script runs a camera frame on the spot
    /// with these, because they are what the globals hold between frames.
    /// </summary>
    float lastSeconds;
    int lastFrameMs, lastNowMs;

    void Update(float seconds, int frameMs, int nowMs)
    {
        switch (Mode)
        {
            case CameraMode.Game: GameUpdate(seconds, frameMs); break;
            case CameraMode.Cutscene: CutsceneUpdate(frameMs); break;
            case CameraMode.Path: PathUpdate(seconds); break;
            case CameraMode.Free: Frame = FromAngles(Angles, Eye); break;
            case CameraMode.Look: LookUpdate(); break;
        }
    }

    /// <summary>
    /// The easing of the field of view and the distance.
    ///
    /// **The lens never eases.** While a shot's lerp time is running the step is
    /// <c>(target − fov) / msLeft × seconds</c> — milliseconds divided by seconds, a thousandth of
    /// what was meant — and then the result is written over <em>the target as well as the fov</em>
    /// (the shot stores to both fields). So the first step of a lerped shot,
    /// which the shot runs itself before anything is drawn, moves the lens by a hair and forgets
    /// where it was going: the camera keeps the lens it had. Only a shot with no lerp time — no
    /// time left, so the fov is simply set to the target — changes lens, and it does so at once.
    /// Kept, as 1.1 has it.
    /// </summary>
    void Ease(float seconds)
    {
        bool fovMoving = false;
        if (Fov != FovTarget)
        {
            fovMoving = lerpLeftMs != 0;
            Fov = fovMoving ? (FovTarget - Fov) / lerpLeftMs * seconds + Fov : FovTarget;
            Fov = FovTarget = Math.Clamp(Fov, 0f, 360f);
        }
        if (Distance == DistanceTarget)
        {
            if (!fovMoving) Settling = false;
            return;
        }
        if (DistanceTarget <= Distance)
        {
            float d = Distance - seconds * DistanceRate;
            Distance = d < DistanceTarget ? DistanceTarget : d > DistanceCeiling ? DistanceCeiling : d;
        }
        else
        {
            float d = seconds * DistanceRate + Distance;
            Distance = d < 0f ? 0f : d > DistanceTarget ? DistanceTarget : d;
        }
    }

    // ------------------------------------------------------------------------ the game camera

    /// <summary>
    /// The game camera, every frame the player is in control.
    ///
    /// <list type="number">
    /// <item>Turn: the pitch by <see cref="PitchRate"/>, held between <see cref="PitchLow"/> and
    /// <see cref="PitchHigh"/>; the yaw by <see cref="YawRate"/>, free all the way round.</item>
    /// <item>The target: the character's origin raised by <see cref="TargetHeight"/> — or, for a
    /// tenth of a second after a change of character, a slide from the old one.</item>
    /// <item>Back off along the view by <see cref="Distance"/>, first letting the level pull that
    /// in. A pull-in sets the distance <em>and</em> where it is heading, so the camera
    /// jumps in at once and then eases back out at <see cref="DistanceRate"/> once it can.</item>
    /// <item>Work out where it should be heading: <c>(512 − 300) × cos(t × 90°) + 300</c>, times
    /// the zoom, where <c>t</c> is how far the pitch is from its steepest (−80°, t = 0) to its
    /// shallowest (−20°, t = 1). So <b>the steeper the camera looks down, the further back it
    /// sits</b> — 512 straight down the barrel of the limit, 300 at the shallow end.</item>
    /// </list>
    /// </summary>
    void GameUpdate(float seconds, int frameMs)
    {
        if (Subject is not { } thing) return;
        if (PitchRate != 0f || YawRate != 0f)
        {
            float pitch = seconds * PitchRate + Angles.X;
            if (PitchLow > pitch) pitch = PitchLow;
            else if (PitchTop < pitch) pitch = PitchTop;
            Angles.X = Wrap(pitch);
            Angles.Z = Wrap(seconds * YawRate + Angles.Z);
            SlewTurnedByHand();
        }
        // Auto camera slewing (options bit 8), `WorldCameraSlew.cs`.
        if (AutoSlew) SlewStep(thing, lastNowMs / 1000f);
        Angles.Y = Wrap(0f);

        var head = thing.Position + new Vector3(0, 0, TargetHeight + thing.ShownLift + StepLift(thing, seconds));   // the lifts are ours: a cell level's walker (CellStepEase), the camera on steps (CameraChoices)
        if (handoverLeft != 0)
        {
            float t = (HandoverMs - handoverLeft) * 0.01f;
            Target = (head - handoverFrom) * t + handoverFrom;
            handoverLeft -= frameMs;
            if (handoverLeft < 0) handoverLeft = 0;
        }
        else Target = head;

        var back = -FromAngles(Angles, Target).Forward();
        PulledTo = null;
        if (SeeThrough != SeeThroughMode.XRay && Obstruction?.Invoke(Target, back, Distance) is { } reach && reach < Distance)
        {
            Distance = DistanceTarget = reach;
            PulledTo = reach;
        }
        Frame = FromAngles(Angles, Target);
        var eye = Target - Frame.Forward() * Distance;
        Frame.Translation = eye;
        Eye = eye;

        // Over the base game's range even when the camera may turn higher (UpLimit): above it, the near distance. Held at the
        // top only: below the range it is left as the base game's formula has it (a harness's --camera-pitch sets an unwrapped
        // angle, and what the eye then does is what the tests were made on).
        float t2 = MathF.Min((Angles.X - PitchLow) / (PitchHigh - PitchLow), 1f) * 90f;
        // The engine's table cosine (`TableTrig`), not the library's: 354.79 at the start's
        // pitch, where the library's gave 354.87.
        float cos = TableTrig.Cos(MathF.Abs(t2));
        Settling = true;
        DistanceTarget = ((FarDistance - NearDistance) * cos + NearDistance) * ZoomScale;
    }

    /// <summary>
    /// The pitch key: adds to the pitch rate, held to ±<see cref="PitchRateLimit"/>.
    /// The engine's input code calls it with ±180 for as long as the key is down.
    /// </summary>
    public void TurnPitch(float degreesPerSecond) =>
        PitchRate = Math.Clamp(PitchRate + degreesPerSecond, -PitchRateLimit, PitchRateLimit);

    /// <summary>The yaw key: as <see cref="TurnPitch"/>, for the yaw.</summary>
    public void TurnYaw(float degreesPerSecond) =>
        YawRate = Math.Clamp(YawRate + degreesPerSecond, -YawRateLimit, YawRateLimit);

    /// <summary>Both rates back to zero, which the input code does every frame first.</summary>
    public void StopTurning() => PitchRate = YawRate = 0f;

    /// <summary>
    /// The zoom keys: <c>±seconds × 0.5</c> a frame onto <see cref="ZoomScale"/>, held
    /// between 0.1 and 1 — and out to <see cref="ZoomOut"/> where the game has let it go further (ours).
    /// </summary>
    public void Zoom(float amount)
    {
        float z = amount + ZoomScale, most = MathF.Max(ZoomMax, MathF.Min(ZoomOut, CameraChoices.MostOut));
        ZoomScale = z < ZoomMin ? ZoomMin : z > most ? most : z;
    }

    // ------------------------------------------------------------------ the first-person look

    /// <summary>
    /// The first-person look, every frame the look key is held. The eye goes to the
    /// character's head — the <c>Helmet</c> tag's bone, posed (<see cref="HeadOf"/>), or the origin
    /// raised by <see cref="TargetHeight"/> when there is no such bone — and the mouse turns it:
    /// <b>half a degree per mickey</b>, pitch first and held to ±80°, then the yaw, free all the
    /// way round. There is no roll.
    /// </summary>
    void LookUpdate()
    {
        if (Subject is not { } thing) return;
        Eye = (HeadOf?.Invoke(thing) ?? thing.Position + new Vector3(0, 0, TargetHeight)) + new Vector3(0, 0, thing.ShownLift);   // the lift: ours (CellStepEase)
        float pitch = Signed(Angles.X) + -MouseDy * 0.5f;
        if (pitch < LookPitchLow) pitch = LookPitchLow;
        else if (pitch > LookPitchHigh) pitch = LookPitchHigh;
        Angles.X = Wrap(pitch);
        Angles.Z = Wrap(-MouseDx * 0.5f + Angles.Z);
        Angles.Y = Wrap(0f);
        Frame = FromAngles(Angles, Eye);
    }

    // ---------------------------------------------------------------------- matrices and angles

    /// <summary>
    /// The camera matrix from Euler angles and a position — the thing matrix's own
    /// convention: rotate about Y (the roll), then X (the pitch), then Z (the yaw), rows out.
    /// </summary>
    public static Matrix4x4 FromAngles(Vector3 degrees, Vector3 position)
    {
        var r = degrees * (MathF.PI / 180f);
        var m = Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationZ(r.Z);
        m.Translation = position;
        return m;
    }

    /// <summary>
    /// A camera matrix looking from one point at another with no roll: the second row
    /// is the view, the first is <c>up × view</c>, the third is <c>view × first</c>. Two points in
    /// the same place leave the matrix as it was, which is what the engine does.
    /// </summary>
    public static Matrix4x4 LookingAt(Vector3 from, Vector3 to, Matrix4x4 previous)
    {
        var f = to - from;
        if (f.LengthSquared() == 0f) return previous;
        f = Vector3.Normalize(f);
        // The world up, unless the view is nearly straight up or down (the 0.999 test),
        // where the engine takes −Y or +Y instead so the cross product has something to work with.
        Vector3 up = MathF.Abs(f.Z) <= 0.999f ? Vector3.UnitZ : f.Z > 0f ? -Vector3.UnitY : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, f));
        var upAxis = Vector3.Normalize(Vector3.Cross(f, right));
        return new Matrix4x4(right.X, right.Y, right.Z, 0,
                             f.X, f.Y, f.Z, 0,
                             upAxis.X, upAxis.Y, upAxis.Z, 0,
                             from.X, from.Y, from.Z, 1);
    }

    /// <summary>
    /// The pitch and yaw a camera matrix comes to, which is what the cutscene's frame
    /// writes into the camera's own angles once it has looked from the eye at the
    /// target — and so 1.1's camera angles in a cutscene. From the view
    /// row: the yaw is the arc-cosine of its level part's y (negative when it points along +x, so 0
    /// faces +Y as everywhere else), the pitch the arc-cosine of its level length (negative when it
    /// points down), both in degrees and **signed, not wrapped** — a conversation in 1.1
    /// has `pitch -26.9; yaw -131`. A view within a thousandth of straight up or down is pitch ±90,
    /// yaw 0. The engine's arc-sine (a series) is taken as exact. The roll it also works
    /// out is not: nothing Vitomir has reads the cutscene's roll (ours).
    /// </summary>
    public static (float Pitch, float Yaw) PitchYawOf(Matrix4x4 frame)
    {
        const float degrees = 180f / MathF.PI;
        float fx = frame.M21, fy = frame.M22, fz = frame.M23;
        float level = MathF.Sqrt(fx * fx + fy * fy);
        float pitch, yaw = 0f;
        if (level < UprightView) pitch = 90f;
        else
        {
            yaw = MathF.Acos(Math.Clamp(fy / level, -1f, 1f)) * degrees;
            if (fx > 0f) yaw = -yaw;
            float across = (fx * fx + fy * fy) / level;
            pitch = across >= 1f ? 0f : MathF.Acos(across) * degrees;
        }
        if (fz < 0f) pitch = -pitch;
        return (pitch, yaw);
    }

    /// <summary>How short the view's level part may be before it counts as straight up or down.</summary>
    const float UprightView = 0.001f;

    /// <summary>An angle in 0..360. Exactly 360 comes back as 0.</summary>
    public static float Wrap(float degrees)
    {
        float a;
        if (degrees < 0f)
        {
            float neg = -degrees;
            a = neg < 360f ? 360f - neg : 360f - (neg - MathF.Floor(neg / 360f) * 360f);
        }
        else a = degrees < 360f ? degrees : degrees - MathF.Floor(degrees / 360f) * 360f;
        return a == 360f ? 0f : a;
    }

    /// <summary>An angle in −180..180: wrap it, and take anything past 180 back round.</summary>
    public static float Signed(float degrees)
    {
        float a = Wrap(degrees);
        return a > 180f ? -(360f - a) : a;
    }

    /// <summary>The screen effects: the fades, the flashes and the tinted filter.</summary>
    public ScreenEffects Effects { get; } = new();
}

/// <summary>The matrix helpers the camera reads its rows with.</summary>
static class CameraMatrix
{
    public static Vector3 Forward(this Matrix4x4 m) => new(m.M21, m.M22, m.M23);
}
