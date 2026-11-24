// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The cutscene half of the camera: SetupCutscene, SetShot and the eighteen shot framings,
// camera paths, and Release.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>The ten framings a script can ask for (<c>CAM_SHOT_*</c> in `Codex.java`).</summary>
public enum CameraShot
{
    None = 0, AngularA = 1, AngularB = 2, AngularC = 3, OverShoulderA = 4, OverShoulderC = 5,
    PovA = 6, PovC = 7, ProfileA = 8, ProfileC = 9,
}

public sealed partial class WorldCamera
{
    /// <summary>
    /// One shot's framing, as the engine caches it (100 bytes a shot, ten of them).
    /// A shot frames the look-at point somewhere between two points and puts the eye out along a
    /// direction from it; which of the five distances is asked for decides both how far out the
    /// eye goes and, through the clamp between <see cref="Near"/> and <see cref="Far"/>, how far
    /// between the two points the look-at point sits.
    /// </summary>
    public sealed class ShotSetup
    {
        /// <summary>Computed, and still good for where the targets are.</summary>
        public bool Valid;
        /// <summary>The lens.</summary>
        public float Fov;
        /// <summary>The turn about Z applied to the direction, or 0 for none.</summary>
        public float Turn;
        /// <summary>The distances the look-at point is interpolated over.</summary>
        public float Far, Near;
        /// <summary>Where it looks at the far distance.</summary>
        public Vector3 LookFar;
        /// <summary>Where it looks at the near one.</summary>
        public Vector3 LookNear;
        /// <summary>Which way the eye is from the look-at point.</summary>
        public Vector3 Direction;
        /// <summary>The five distances, <c>CAM_DIST_EXTREMECLOSEUP</c> to <c>CAM_DIST_LONG</c>.</summary>
        public readonly float[] Distances = new float[5];
        /// <summary>Where the two targets were when it was worked out.</summary>
        public Vector3 WhereA, WhereB;
    }

    /// <summary>The ten cached framings. They outlive a shot, and so does whatever a framing leaves unwritten.</summary>
    public ShotSetup[] Shots { get; } = [.. Enumerable.Range(0, 10).Select(_ => new ShotSetup())];

    WorldThing? cutA, cutB;
    /// <summary>The shot the cutscene is on; 0 until the first <see cref="SetShot"/>.</summary>
    int shotIndex;
    /// <summary>The last shot's four parameters and its client.</summary>
    public (int Shot, int Angle, int Distance, int Side) LastShot { get; private set; }

    int lerpLeftMs, lerpTotalMs;
    Vector3 eyeFrom, eyeTo, lookFrom, lookTo;

    /// <summary>
    /// The camera has just gone into a cutscene or a path, which is where the engine
    /// closes the panes the cutscene's start names. The harness owns the panes, so it listens here.
    /// </summary>
    public event Action? CutsceneBegan;

    /// <summary>The cutscene's two subjects.</summary>
    public WorldThing? CutsceneA => cutA;
    /// <inheritdoc cref="CutsceneA"/>
    public WorldThing? CutsceneB => cutB;

    /// <summary>
    /// Going into cutscene mode. The camera does not move: the eye and the look-at point
    /// are captured where they are, and nothing happens until a shot is set (the update returns at
    /// once while the camera's flag is 0). The status bar goes down and the pointer goes away, and so do
    /// the character sheet, the inventory, the quest and the discipline panes — which
    /// stay shut afterwards; only the bar comes back with the release.
    ///
    /// <b>The steered character's <c>DON'T RENDER</c> bit is cleared on the way in</b>, with the
    /// first-person bit (the <c>0xffffff7d</c> mask again), so a script that hid the player and
    /// then started a cutscene gets the player back on screen. Copied.
    /// </summary>
    public void EnterCutscene()
    {
        if (Mode == CameraMode.Cutscene) return;
        if (Subject is { } steered) steered.RenderFlags &= ~(RenderFirstPerson | RenderDontRender);
        CutsceneBegan?.Invoke();
        CursorShown = false;
        cutA = cutB = null;
        lookTo = lookFrom = Target;
        shotIndex = 0;
        eyeTo = eyeFrom = Frame.Translation;
        // The render view's bit 0 — every model at
        // its full detail for as long as the cutscene lasts (see FullDetail).
        FullDetail = true;
        Mode = CameraMode.Cutscene;
    }

    /// <summary>
    /// The bit a cutscene sets on the camera's render view (bit 0):
    /// set going in, cleared by the release. The one place that reads it is
    /// the model draw: a model with levels of detail is normally drawn at a
    /// level worked out from how far it is from the eye and the <c>modellod</c> factor
    /// (<c>1 / ((depth − start) × scale + 1)</c>), and with the bit set it is
    /// drawn at level 1 — whole — however far away it is. So a cutscene never shows a model thinned
    /// out by distance. **Vitomir draws every model whole all the time** (it has no model levels of
    /// detail), so the bit changes nothing in its picture; it is kept for what it says.
    /// </summary>
    public bool FullDetail { get; private set; }

    /// <summary>
    /// <c>CodexCamera.SetupCutscene(client, target0, target1)</c>. Into
    /// cutscene mode, frame these one or two, forget every cached framing, and — before the script
    /// has said anything else — go straight to <b>a point-of-view shot on the first target, medium
    /// distance, medium-low angle, over a tenth of a second</b>. That default is why a cutscene
    /// whose script only calls <c>SetupCutscene</c> still has a sensible picture.
    /// </summary>
    public void SetupCutscene(WorldThing? a, WorldThing? b)
    {
        if (a is null) return;
        EnterCutscene();
        cutA = a;
        cutB = b;
        foreach (var s in Shots) s.Valid = false;
        SetShot((int)CameraShot.PovA, 1, 2, 0, 100);
    }

    /// <summary>
    /// <c>CodexCamera.SetShot(client, shot, angle, distance, side, lerpTime)</c>. The native refuses a shot,
    /// angle or distance out of range; this is the camera
    /// half. It only works in cutscene mode.
    ///
    /// <list type="number">
    /// <item>Frame the shot, unless it is cached and neither target has moved more than a unit.</item>
    /// <item>The distance asked for, held between the framing's near and far, decides how far
    /// from the near look-at point to the far one the camera looks.</item>
    /// <item>The eye goes out from there along the framing's direction — mirrored when
    /// <paramref name="side"/> is set, and tipped up or down by the angle (0.3 and 0.25 down for
    /// low and medium-low; 0.1, 0.5 and 0.7 up for medium, medium-high and high) — by the
    /// distance itself, <em>unclamped</em>.</item>
    /// <item>The move from where the camera is to there takes <paramref name="ms"/>, eased by a
    /// half cosine. The lens is the framing's — <b>but only a shot with no lerp time gets it</b>:
    /// with one, the easing loses the new lens on its first step and the camera keeps the one it
    /// had (see <see cref="Ease"/>).</item>
    /// </list>
    ///
    /// Then the engine runs the camera's whole frame on the spot, on the <em>last</em> frame's
    /// timing — effects, easing, update — so a shot with no lerp
    /// time is on screen the same frame, a lerped one has already used up one frame of its time,
    /// and the fades get an extra step.
    /// </summary>
    public void SetShot(int shot, int angle, int distance, int side, int ms)
    {
        if (Mode != CameraMode.Cutscene) return;
        if (shot is < 0 or > 9 || angle is < 0 or > 4 || distance is < 0 or > 4) return;
        var s = Shots[shot];
        if (!s.Valid
            || (cutA is not null && Vector3.Distance(s.WhereA, cutA.Position) > 1f)
            || (cutB is not null && Vector3.Distance(s.WhereB, cutB.Position) > 1f))
        {
            if (cutA is null) return;               // the engine asserts pTargetA (0x79c)
            if (cutB is null) FrameOne(shot);
            else FrameTwo(shot);
        }

        float d = s.Distances[distance];
        float held = d < s.Near ? s.Near : s.Far < d ? s.Far : d;
        float frac = (held - s.Near) / (s.Far - s.Near);
        lookFrom = Target;
        var dir = s.Direction;
        if (side != 0) dir = -dir;
        dir.Z += angle switch { 0 => -0.3f, 1 => -0.25f, 2 => 0.1f, 3 => 0.5f, _ => 0.7f };
        dir = Normalize(dir) * d;
        var look = (s.LookFar - s.LookNear) * frac + s.LookNear;
        eyeFrom = Eye;
        lookTo = look;
        eyeTo = look + dir;
        FovTarget = Math.Clamp(s.Fov, 0f, 360f);
        Settling = true;
        lerpLeftMs = lerpTotalMs = ms;
        shotIndex = shot;
        LastShot = (shot, angle, distance, side);

        Effects.Step(lastNowMs, lastFrameMs);
        if (Settling) Ease(lastSeconds);
        Update(lastSeconds, lastFrameMs, lastNowMs);
    }

    /// <summary>
    /// The cutscene's frame. Nothing until a shot is set; nothing if the first target
    /// has gone; the second target dropped if it has. Otherwise the eye and the look-at point ease
    /// from where they were to where the shot put them — <c>(cos(180° − 180° × done) + 1) / 2</c>,
    /// a smooth start and a smooth stop — and the frame looks from one at the other.
    ///
    /// The engine then casts a thin ray from the look-at point out to the eye
    /// (query flags 2, the level's surfaces and nothing else, no radius) and, when it meets a
    /// surface, **puts the eye on it** — on the wall itself, nothing held back
    /// (write the hit point over both copies of the eye). Every frame, so a lerp that swings the eye
    /// behind a wall slides along it. <see cref="SightLine"/> asks the world.
    /// </summary>
    void CutsceneUpdate(int frameMs)
    {
        if (shotIndex == 0) return;
        if (cutA is null || !cutA.InWorld) return;
        if (cutB is not null && !cutB.InWorld) cutB = null;
        if (lerpLeftMs == 0)
        {
            Target = lookTo;
            Eye = eyeTo;
        }
        else
        {
            float done = (lerpTotalMs - lerpLeftMs) / (float)lerpTotalMs;
            float s = (MathF.Cos((done * -180f + 180f) * MathF.PI / 180f) + 1f) * 0.5f;
            Target = (lookTo - lookFrom) * s + lookFrom;
            Eye = (eyeTo - eyeFrom) * s + eyeFrom;
            lerpLeftMs -= frameMs;
            if (lerpLeftMs < 0) lerpLeftMs = 0;
        }
        // A thin ray from the look-at point out to the eye, against the level's
        // surfaces, from the sector the look-at point is in — found from the first
        // subject's own. The eye goes onto the first surface it meets.
        // Not under the developer's x-ray (ours, SeeThrough): the frame fades the wall instead.
        if (SeeThrough != SeeThroughMode.XRay && SightLine?.Invoke(cutA, Target, Eye) is { } onWall) Eye = onWall;
        Frame = LookingAt(Eye, Target, Frame);
        // And the frame's angles into the camera's own. Vitomir left the game
        // camera's there once, so the camera's angles stood still through a conversation.
        (Angles.X, Angles.Z) = PitchYawOf(Frame);
    }

    /// <summary>
    /// The cutscene eye's pull-in: given the shot's first subject, the point the shot
    /// looks at and where the eye is, where the first surface between them is, or null.
    /// <see cref="CameraSweep.CutsceneEye"/> is the level's answer.
    /// </summary>
    public Func<WorldThing, Vector3, Vector3, Vector3?>? SightLine { get; set; }

    /// <summary>
    /// <c>CodexCamera.Release(client)</c>: back to the game camera. Only
    /// from a cutscene or a path. The camera faces the way the steered character faces, pitched
    /// down 30°, 300 back, with an 80° lens — a fixed place to come back to rather than where the
    /// player left it. The pointer and the status bar come back.
    /// </summary>
    public void Release(WorldThing? steered)
    {
        if (Mode is not (CameraMode.Cutscene or CameraMode.Path)) return;
        if (steered is not null) Angles.Z = Wrap(steered.Orientation.Z + 360f);
        CursorShown = true;
        path = null;
        FullDetail = false;
        if (steered is not null)
        {
            SetSubject(steered);
            Angles.X = Wrap(StartPitch);
            Fov = FovTarget = DefaultFov;
            Distance = DistanceTarget = NearDistance;
            Mode = CameraMode.Game;
        }
        // Nobody to come back to — a harness run started with no player (ours; 1.1 always has one):
        // the free camera, where Escape still opens the menu, rather than a path that has ended and
        // holds the screen for good (a run of `OLDT_7_1` without `--player`).
        else Mode = CameraMode.Free;
    }

    // --------------------------------------------------------------------------- the framings

    /// <summary>
    /// A character's head, or the fallback: the <c>Helmet</c> tag's bone when there is
    /// one, else the origin raised by the height of a cylinder or the radius of anything else.
    /// </summary>
    Vector3 Head(WorldThing t) =>
        HeadOf?.Invoke(t) ?? t.Position + new Vector3(0, 0, t.Collide == CollideShape.Cylinder ? t.Height : t.Radius);

    static Vector3 Normalize(Vector3 v) => v.LengthSquared() > 0f ? Vector3.Normalize(v) : v;

    static float Sin(float deg) => MathF.Sin(deg * MathF.PI / 180f);
    static float Tan(float deg) => MathF.Tan(deg * MathF.PI / 180f);

    /// <summary>A vector turned about Z (an Euler of (0, 0, degrees)).</summary>
    static Vector3 TurnZ(Vector3 v, float degrees) =>
        Vector3.TransformNormal(v, Matrix4x4.CreateRotationZ(degrees * MathF.PI / 180f));

    static void Spread(ShotSetup s, float first, float second)
    {
        float span = s.Far - s.Near;
        s.Distances[0] = s.Near;
        s.Distances[1] = span * first + s.Near;
        s.Distances[2] = span * second + s.Near;
        s.Distances[3] = s.Far;
        s.Distances[4] = s.Far + s.Far;
    }

    /// <summary>
    /// A two-character framing (the nine framings of its switch). The shared part: each
    /// head; each <b>height</b> as 1.2 × the head's rise above the origin; the mean of the two; the
    /// middle of the heads and the middle of the feet; and a <b>side</b> — the horizontal
    /// perpendicular to the line between them, <c>normalize((B − A) × Z)</c>.
    /// </summary>
    void FrameTwo(int shot)
    {
        var a = cutA!; var b = cutB!;
        var headA = Head(a); var headB = Head(b);
        float hA = (headA.Z - a.Position.Z) * 1.2f, hB = (headB.Z - b.Position.Z) * 1.2f;
        float hMean = (hB - hA) * 0.5f + hA;
        var headMid = (headB - headA) * 0.5f + headA;
        var delta = b.Position - a.Position;
        var feetMid = delta * 0.5f + a.Position;
        var side = Normalize(Vector3.Cross(delta, Vector3.UnitZ));
        var s = Shots[shot];

        switch ((CameraShot)shot)
        {
            case CameraShot.AngularA:
            {
                s.Fov = 45f; s.Turn = -60f;
                float d = Vector3.Distance(a.Position, b.Position);
                float l1 = d * Sin(97.5f) / Sin(45f);
                float l2 = l1 * Sin(22.5f) / Sin(120f);
                float t = Tan(16.917294f) * 2f;
                s.Near = hA * 0.2f / t;
                s.Far = l2 * Sin(37.5f) / Sin(22.5f);
                s.LookNear = headB;
                float f = (d - l2) / d;
                s.LookFar = new Vector3((b.Position.X - a.Position.X) * f + a.Position.X,
                                        (b.Position.Y - a.Position.Y) * f + a.Position.Y,
                                        hMean * 0.5f + (b.Position.Z - a.Position.Z) * f + a.Position.Z);
                s.Direction = TurnZ(side, s.Turn);
                // Not the usual spread: the near, halfway, the far, half as far again, and twice.
                s.Distances[0] = s.Near;
                s.Distances[1] = (s.Far - s.Near) * 0.5f + s.Near;
                s.Distances[2] = s.Far;
                s.Distances[3] = s.Far * 1.5f;
                s.Distances[4] = s.Far + s.Far;
                break;
            }
            case CameraShot.AngularB:
            {
                s.Fov = 55f; s.Turn = 0f;
                float t = Tan(27.5f) * 2f;
                float l = Vector3.Distance(a.Position, b.Position) * 1.2f;
                s.Far = l * 4f / t;
                s.Near = l / t;
                s.LookNear = s.LookFar = (feetMid - headMid) * 0.5f + headMid;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.AngularC:
            {
                s.Fov = 45f; s.Turn = 60f;
                float d = Vector3.Distance(b.Position, a.Position);
                float l1 = d * Sin(97.5f) / Sin(45f);
                float l2 = l1 * Sin(22.5f) / Sin(120f);
                float near = l2 * Sin(37.5f) / Sin(22.5f);
                s.Near = near;
                s.Far = near + near;
                float f = (d - l2) / d;
                s.LookNear = new Vector3((a.Position.X - b.Position.X) * f + b.Position.X,
                                         (a.Position.Y - b.Position.Y) * f + b.Position.Y,
                                         hMean * 0.5f + (a.Position.Z - b.Position.Z) * f + b.Position.Z);
                s.LookFar = s.LookNear;
                s.Direction = TurnZ(side, s.Turn);
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.OverShoulderA:
            {
                s.Fov = 45f; s.Turn = -60f;
                float t = Tan(16.917294f) * 2f;
                s.LookNear = headA;
                s.LookFar = headMid;
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hA / t;
                s.Near = hA * 0.2f / t;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.OverShoulderC:
            {
                s.Fov = 45f; s.Turn = 60f;
                float t = Tan(16.917294f) * 2f;
                s.LookNear = headB;
                s.LookFar = headMid;
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hB / t;
                s.Near = hB * 0.2f / t;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.PovA:
            {
                s.Fov = 60f; s.Turn = 90f;
                float t = Tan(22.556391f) * 2f;
                s.LookNear = headA;
                s.LookFar = a.Position with { Z = hA * 0.5f + a.Position.Z };
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hA / t;
                s.Near = hA * 0.2f / t;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.PovC:
            {
                s.Fov = 60f; s.Turn = -90f;
                float t = Tan(22.556391f) * 2f;
                s.LookNear = headB;
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hB / t;
                s.Near = hB * 0.2f / t;
                s.LookFar = (headB - b.Position) * 0.5f + b.Position;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.ProfileA:
            {
                s.Fov = 45f; s.Turn = 0f;
                float t = Tan(16.917294f) * 2f;
                s.Far = hA / t;
                s.Near = hA * 0.2f / t;
                s.LookNear = headA + Normalize(headB - headA) * (hA * 0.2f);
                s.LookFar = (headA - a.Position) * 0.5f + a.Position;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.ProfileC:
            {
                // The distances are the *first* character's, though it frames the second.
                s.Fov = 45f; s.Turn = 0f;
                float t = Tan(16.917294f) * 2f;
                s.LookNear = headB;
                s.Far = hA / t;
                s.Near = hA * 0.2f / t;
                s.LookFar = (headB - b.Position) * 0.5f + b.Position;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
            }
        }
        s.Valid = true;
        s.WhereA = a.Position;
        s.WhereB = b.Position;
    }

    /// <summary>
    /// A one-character framing (its nine framings). The head, the height as 1.25 × the head's
    /// rise, and the character's own axes: its <b>right</b> (the matrix's first row) as the side,
    /// and its <b>facing</b> (the second row) where a framing wants one.
    /// </summary>
    void FrameOne(int shot)
    {
        var a = cutA!;
        var headA = Head(a);
        float hA = (headA.Z - a.Position.Z) * 1.25f;
        var m = a.Rotation;
        var side = new Vector3(m.M11, m.M12, m.M13);
        var facing = new Vector3(m.M21, m.M22, m.M23);
        var middle = (headA - a.Position) * 0.5f + a.Position;
        var s = Shots[shot];
        float t17 = Tan(16.917294f) * 2f;

        switch ((CameraShot)shot)
        {
            case CameraShot.AngularA:
                s.Fov = 45f; s.Turn = -50f;
                s.LookNear = headA; s.LookFar = middle;
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                s.Direction = TurnZ(side, s.Turn);
                Spread(s, 0.25f, 0.5f);
                break;
            case CameraShot.AngularB:
            {
                s.Fov = 45f; s.Turn = 0f;
                float t = Tan(22.5f) * 2f;
                s.LookNear = headA; s.LookFar = middle;
                s.Far = hA * 4f / t; s.Near = hA / t;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.AngularC:
                s.Fov = 45f; s.Turn = 50f;
                s.LookNear = headA; s.LookFar = middle;
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                s.Direction = TurnZ(side, s.Turn);
                Spread(s, 0.25f, 0.5f);
                break;
            case CameraShot.OverShoulderA:
                s.Fov = 45f; s.Turn = -60f;
                s.LookNear = s.LookFar = headA;
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                Spread(s, 0.25f, 0.5f);
                break;
            case CameraShot.OverShoulderC:
                s.Fov = 45f; s.Turn = 60f;
                s.LookNear = s.LookFar = headA;
                s.Direction = TurnZ(side, s.Turn);
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                Spread(s, 0.25f, 0.5f);
                break;
            case CameraShot.PovA:
            {
                // The one framing with its own spread: 0.35 and 0.75 rather than 0.25 and 0.5, and
                // a near of a quarter rather than a fifth.
                s.Fov = 60f; s.Turn = 90f;
                float t = Tan(22.556391f) * 2f;
                s.LookNear = headA;
                s.Far = hA / t; s.Near = hA * 0.25f / t;
                s.LookFar = middle;
                s.Direction = TurnZ(side, s.Turn);
                Spread(s, 0.35f, 0.75f);
                break;
            }
            case CameraShot.PovC:
            {
                // Looks at the character from straight in front of it: the direction is its facing
                // turned round. The −90° is stored and, unlike every other framing, never applied.
                s.Fov = 60f; s.Turn = -90f;
                float t = Tan(22.556391f) * 2f;
                s.LookNear = headA;
                s.Far = hA / t; s.Near = hA * 0.2f / t;
                s.LookFar = middle;
                s.Direction = -facing;
                Spread(s, 0.25f, 0.5f);
                break;
            }
            case CameraShot.ProfileA:
                s.Fov = 45f; s.Turn = 0f;
                s.LookNear = headA + facing * (hA * 0.2f);
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                s.LookFar = middle;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
            case CameraShot.ProfileC:
                // **Leaves the far look-at point unwritten** — whatever the last framing of this
                // slot put there, or nothing at all on a fresh camera. At the full and long
                // distances that is where the camera looks.
                s.Fov = 45f; s.Turn = 0f;
                s.LookNear = headA - facing * (hA * 0.2f);
                s.Far = hA / t17; s.Near = hA * 0.2f / t17;
                s.Direction = side;
                Spread(s, 0.25f, 0.5f);
                break;
        }
        s.Valid = true;
        s.WhereA = a.Position;
        s.WhereB = Vector3.Zero;
    }

    // ------------------------------------------------------------------------------- paths

    NcpCameraPath? path;
    float pathAt, pathSpeed;
    int pathKey;
    Action? pathEnded;

    /// <summary>The path being played, if any.</summary>
    public NcpCameraPath? PlayingPath => path;

    /// <summary>
    /// <c>CodexCamera.PlayPath(client, scriptGuid, name, speed)</c>. Into
    /// cutscene mode first if not already there, then path mode: start at the first keyframe's
    /// distance along the path and move along it at <paramref name="speed"/> units a second, with
    /// an 80° lens. <paramref name="ended"/> is the script's <c>pathended</c>, called once when the
    /// end is passed.
    /// </summary>
    public bool PlayPath(NcpCameraPath ncp, float speed, Action? ended, int startKey = 0, float fov = DefaultFov)
    {
        EnterCutscene();
        if (ncp.Keyframes.Length == 0) return false;
        if (startKey >= ncp.Keyframes.Length) startKey = 0;
        path = ncp;
        pathKey = startKey;
        pathSpeed = speed;
        pathAt = ncp.Keyframes[startKey].Distance;
        pathEnded = ended;
        Fov = FovTarget = Math.Clamp(fov, 0f, 360f);
        Mode = CameraMode.Path;
        return true;
    }

    /// <summary>
    /// A path's frame. Move along by speed × seconds; past the end, sit on the last
    /// keyframe, tell the script, and from then on do nothing at all — the engine leaves the
    /// camera in path mode with its position past the end, so every later frame returns at the
    /// first test and the picture holds until something releases it. Otherwise find the segment
    /// and evaluate its stored cubic for the position and the angles.
    ///
    /// <b>The splines are the file's.</b> The loader reads the 104-byte keyframes
    /// wholesale and nothing recomputes them before playing; only the path editor's insert and
    /// delete rebuild them. A single-keyframe path's are uninitialised memory, and it
    /// never reads them — its length is 0, so the first frame is already past the end.
    /// </summary>
    void PathUpdate(float seconds)
    {
        if (path is not { } p) return;
        if (pathAt > p.TotalLength) return;
        pathAt = seconds * pathSpeed + pathAt;
        var keys = p.Keyframes;
        if (pathAt > p.TotalLength)
        {
            var last = keys[^1];
            Eye = last.Position;
            Angles = new Vector3(Wrap(last.Angles.X), Wrap(last.Angles.Y), Wrap(last.Angles.Z));
            Frame = FromAngles(Angles, Eye);
            var tell = pathEnded;
            pathEnded = null;
            tell?.Invoke();
            return;
        }
        while (pathKey + 1 < keys.Length && !(pathAt < keys[pathKey + 1].Distance)) pathKey++;
        var k = keys[pathKey];
        float t = (pathAt - k.Distance) * k.InvSegmentLength, t2 = t * t, t3 = t2 * t;
        var ps = k.PositionSpline; var sp = k.AngleSpline;
        Eye = new Vector3(k.Position.X + t * ps[0] + t2 * ps[3] + t3 * ps[6],
                          k.Position.Y + t * ps[1] + t2 * ps[4] + t3 * ps[7],
                          k.Position.Z + t * ps[2] + t2 * ps[5] + t3 * ps[8]);
        var ang = new Vector3(k.Angles.X + t * sp[0] + t2 * sp[3] + t3 * sp[6],
                              k.Angles.Y + t * sp[1] + t2 * sp[4] + t3 * sp[7],
                              k.Angles.Z + t * sp[2] + t2 * sp[5] + t3 * sp[8]);
        Angles = new Vector3(Wrap(ang.X), Wrap(ang.Y), Wrap(ang.Z));
        Frame = FromAngles(Angles, Eye);
    }
}
