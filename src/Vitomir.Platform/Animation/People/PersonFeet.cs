// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on what play showed (the new bodies turning and settling with their feet sliding), the models repository's
// catch-up list (foot locks, soft IK, contact planes), the note (the bend helpers after the engine's IK) and
// the squat kept on the balls of the feet ("slav squat" style).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// A person's planted feet, the heels-up squat and the bend helpers' rule — the numbers (<b>SANDBOX</b>, ours, looked at on
/// a play-test's tape and in stills). Off altogether (<see cref="Off"/>, <c>--feet-off</c> and <c>--bend-rule-off</c>) a person's feet are
/// The earlier build's to the bit and the world has no <see cref="PersonFeet"/> store. Only the models repository's people have any of it.
/// </summary>
public sealed record PersonFeetSettings
{
    /// <summary>Whether a foot the motion has planted is held where it was planted in the world (<see cref="PersonFeet"/>). <c>--feet-off</c> turns it off.</summary>
    public bool Locks { get; init; } = true;

    /// <summary>How long a foot takes to be taken by its lock or let go (s; 0.1). <c>footLockSeconds</c>, <c>--foot-lock-seconds</c>.</summary>
    public float LockSeconds { get; init; } = 0.1f;

    /// <summary>
    /// How far the motion may carry a held foot from its lock before the lock lets it go and takes it again where the motion has it
    /// (m; 0.2): a body pushed or stopped on the spring, or legs pivoting far, re-plant their feet in a tenth of a second rather than
    /// stretching a leg. <c>footReplant</c>, <c>--foot-replant</c>.
    /// </summary>
    public float Replant { get; init; } = 0.2f;

    /// <summary>
    /// The most a held foot keeps its own heading against the legs' (30°): past it the foot swivels on its ball with them (the legs
    /// pivoting on their own past <see cref="LegTurnSettings.PivotFrom"/>), the ball staying where it is.
    /// </summary>
    public float SwivelMost { get; init; } = 30f * DetMath.DegToRad;

    /// <summary>
    /// The heel lift of the crouch held and the crouched turns (radians; 0 off), the foot pitched up about its ball, the toes left on
    /// the ground — the stopgap for the squat on the balls of the feet — <b>only by what the clip lacks</b> (<see cref="FootPlacement.Leg"/>).
    /// The models repository's made <c>crouch</c> itself the squat on the balls of the feet (heels 40° up, knees 125°), so on it
    /// the lift adds nothing; it is kept for an older models repository (the flat crouch), which it still lifts.
    /// <c>heelLift</c> (degrees), <c>--heel-lift</c>.
    /// </summary>
    public float HeelLift { get; init; } = 28f * DetMath.DegToRad;

    /// <summary>The share of the ankle's rise from the heel lift that the hips rise too (0.5), so the knees bend only a little deeper.</summary>
    public float LiftHips { get; init; } = 0.5f;

    /// <summary>
    /// Where the legs' soft limit begins, as a share of the leg's reach (0.97): a foot asked to be further from the hip than that is
    /// reached for less and less, never snapped straight. 0 is the earlier build's hard limit.
    /// </summary>
    public float Soft { get; init; } = 0.97f;

    /// <summary>Whether the bend helpers are posed rule after the legs' IK (<see cref="BendRule"/>); off, as the clips keyed them. <c>--bend-rule-off</c> turns it off.</summary>
    public bool Bend { get; init; } = true;

    /// <summary>A ball counts as on the ground in a clip when it is within this of the stand's (m; 0.02) …</summary>
    public const float ContactHeight = 0.02f;

    /// <summary>… and moves over the ground no faster than this (m/s; 0.25).</summary>
    public const float ContactSpeed = 0.25f;

    /// <summary>
    /// A foot is newly taken by its lock only while it is drawn moving over the ground no faster than this (m/s; 0.6): a foot carried by a
    /// fade from a fall or a stride to the stand, or by the body still sliding to a stop, is taken once it has come to rest, not on its way.
    /// (A turn's planted foot drifts under the body at up to about 0.45 m/s: taken.)
    /// </summary>
    public const float TakeSpeed = 0.6f;

    /// <summary>Whether anything here is on (else the world needs no <see cref="PersonFeet"/> store).</summary>
    public bool Any => Locks || HeelLift > 0f || Bend;

    /// <summary>The earlier build's feet, exactly: no locks, no heel lift, no soft limit, the helpers as keyed.</summary>
    public static PersonFeetSettings Off => new() { Locks = false, HeelLift = 0f, Soft = 0f, Bend = false };

    /// <summary>
    /// The numbers as a tape's note (<c>feet</c>): <c>off</c> for the earlier build's feet, else <c>locks=1 lockSeconds=0.1 replant=0.2 swivel=30
    /// lift=28 liftHips=0.5 soft=0.97 bend=1</c> (degrees for the angles). A tape without the note was made on the earlier build's feet (<see cref="WithNote"/>).
    /// </summary>
    public string Note()
    {
        if (!Any && Soft <= 0f) return "off";
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        return string.Create(ic,
            $"locks={(Locks ? 1 : 0)} lockSeconds={LockSeconds:0.####} replant={Replant:0.####} swivel={D(SwivelMost)} lift={D(HeelLift)} liftHips={LiftHips:0.####} soft={Soft:0.####} bend={(Bend ? 1 : 0)}");

        static string D(float radians) => (radians * DetMath.RadToDeg).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// These settings with a tape's note <c>feet</c> (<see cref="Note"/>) laid on; no note (an older tape) or <c>off</c> is
    /// <see cref="Off"/>. A number the note lacks keeps this one's; the numbers go through the note's text, so a replay's are the
    /// recording's to the bit.
    /// </summary>
    public PersonFeetSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this;
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            float r = v * DetMath.DegToRad;
            s = part[..eq] switch
            {
                "locks" => s with { Locks = v != 0f }, "lockSeconds" => s with { LockSeconds = v }, "replant" => s with { Replant = v },
                "swivel" => s with { SwivelMost = r }, "lift" => s with { HeelLift = r }, "liftHips" => s with { LiftHips = v },
                "soft" => s with { Soft = v }, "bend" => s with { Bend = v != 0f },
                _ => s,
            };
        }
        return s;
    }
}

/// <summary>
/// <b>a person's planted feet</b> — each foot's lock (simulation state: where its ball was planted in the world, its
/// heading then, how much it is held) and what the pose reads of it this tick (in the model's space). Moved on in the tick by
/// <see cref="PersonFeetSystem"/>, laid on the pose by <see cref="PoseEvaluator.Place"/> (<see cref="FootPlacement"/>); it moves the
/// hit volumes (the legs), so it is the simulation's and digested. A world has the store only when it has people and any of the
/// feet's rules is on (<see cref="AnimationRegistration.Animation"/>), so 1.1's worlds (the arena) and the earlier build's feet digest as before.
/// </summary>
public struct PersonFeet
{
    /// <summary>Where each planted ball is held in the world (m; its height is the motion's and the ground's, not held).</summary>
    public Vector3 LeftAt, RightAt;

    /// <summary>Each held foot's heading in the world (radians: its heel-to-ball line about the vertical).</summary>
    public float LeftHeading, RightHeading;

    /// <summary>How much each foot is held at its lock now (0 the motion's own foot, 1 exactly there), eased over <see cref="PersonFeetSettings.LockSeconds"/>.</summary>
    public float LeftHold, RightHold;

    /// <summary>Where the motion had each ball in the world last tick (its speed over the ground for <see cref="PersonFeetSettings.TakeSpeed"/>).</summary>
    public Vector3 LeftWas, RightWas;

    /// <summary>For the pose, this tick: where each ball is held in the model's space (its X and Y), and each foot's turn about the vertical from where the motion has it (radians).</summary>
    public Vector3 LeftBall, RightBall;
    public float LeftTurn, RightTurn;

    /// <summary>
    /// For the pose: the heel lift now (radians, by how much the crouch held or a crouched turn is the pose), and the pitch of the kind's
    /// feet standing (radians: the line from the ball up to the ankle above level), so a heel the clip already holds up (a crouched turn's
    /// pivoting heel) is lifted only by what it lacks.
    /// </summary>
    public float Lift, StandPitch;

    int flags;

    const int OnBit = 1, BendBit = 2, LeftPlantedBit = 4, RightPlantedBit = 8, LeftReleasingBit = 16, RightReleasingBit = 32;

    bool Get(int bit) => (flags & bit) != 0;
    void Set(int bit, bool value) => flags = value ? flags | bit : flags & ~bit;

    /// <summary>Whether the feet's rules have posed this person (0, the default: the pose is the earlier build's).</summary>
    public bool On { readonly get => (flags & OnBit) != 0; set => Set(OnBit, value); }

    /// <summary>Whether the bend helpers are posed by the rule after the legs' IK.</summary>
    public bool Bend { readonly get => (flags & BendBit) != 0; set => Set(BendBit, value); }

    /// <summary>The soft limit's start as a share of a leg's reach (0: the earlier build's hard limit).</summary>
    public float Soft;

    /// <summary>The share of the heel lift's ankle rise the hips rise too.</summary>
    public float LiftHips;

    /// <summary>Whether the motion has the foot planted (its lock taken).</summary>
    public bool Planted(bool left) => Get(left ? LeftPlantedBit : RightPlantedBit);
    internal void Planted(bool left, bool value) => Set(left ? LeftPlantedBit : RightPlantedBit, value);

    /// <summary>Whether the lock is letting the foot go to take it again where the motion has it (<see cref="PersonFeetSettings.Replant"/>).</summary>
    public bool Releasing(bool left) => Get(left ? LeftReleasingBit : RightReleasingBit);
    internal void Releasing(bool left, bool value) => Set(left ? LeftReleasingBit : RightReleasingBit, value);
}

/// <summary>
/// Moves every person's <see cref="PersonFeet"/> on by one tick, after the legs' turn and the rig
/// (<see cref="AnimationRegistration.AnimationOrder"/> + <see cref="Order"/>), for each character with a body whose kind is a person:
/// <list type="number">
/// <item><b>Where the motion has each foot</b>: its ball and its heading in the model's space, from tables of the clips made once a
/// clip (30 a second, as the rig's ankles: <see cref="PersonMotion.Sources"/> for a gait's blend or a turn played for some degrees),
/// the two clips of a fade by their shares, turned by the legs' turn about the root bone; then in the world by the body's place.</item>
/// <item><b>Whether the motion has it planted</b>: in the same tables, made from the clip as the models repository made it (a turn with
/// its root's turn, a gait moving over the ground at its own speed), a ball within <see cref="PersonFeetSettings.ContactHeight"/> of
/// the stand's height and moving over the ground no faster than <see cref="PersonFeetSettings.ContactSpeed"/>; the fade's two by
/// their shares. Only the motions a body stands in are held — the stand, the turns on the spot, the crouch held, its ways in and out (and a standing landing)
/// and its turns — by their share of the pose: a gait keeps its own stride, so a start lets the feet go over the fade and a stop
/// takes them as the stand comes in. Never off the ground, in a move or a dodge.</item>
/// <item><b>The lock</b>: a foot newly planted is taken where it is drawn now; held, it stays there in the world, its weight eased up to
/// the share over <see cref="PersonFeetSettings.LockSeconds"/>; lifted, let go over the same; carried more than
/// <see cref="PersonFeetSettings.Replant"/> from it, let go and taken again where the motion has it. Its heading is kept within
/// <see cref="PersonFeetSettings.SwivelMost"/> of the motion's: past it the foot swivels on its ball.</item>
/// <item><b>The heel lift</b> by the share of the pose that is the crouch held or a crouched turn (<see cref="PersonCrouch"/>).</item>
/// </list>
/// Deterministic (<see cref="DetMath"/>, the tables made from the clips); allocates nothing once every clip a person plays has been seen.
/// </summary>
public sealed class PersonFeetSystem(CharacterAssets assets, PersonFeetSettings settings) : ISystem
{
    /// <summary>The name it is registered under, and its place after the rig's (<see cref="PoseRigSystem.Order"/>).</summary>
    public const string Name = "platform.person-feet";
    public const int Order = 15;

    readonly Dictionary<long, FootTrack> tracks = [];
    readonly Dictionary<int, (float Ball, float Pitch)> grounds = [];
    BoneTransform[] local = new BoneTransform[64];
    Matrix4x4[] model = new Matrix4x4[64];
    int bodiesKnown, feetKnown;

    public PersonFeetSettings Settings => settings;

    /// <summary>A clip's feet in its model space 30 times a second: each ball, each foot's heading, the root's place, and whether the motion has each ball planted.</summary>
    internal sealed class FootTrack(Vector3[] left, Vector3[] right, float[] leftHeading, float[] rightHeading, Vector3[] root, bool[] leftDown, bool[] rightDown,
                                    float duration, bool loops)
    {
        public int Frames => left.Length;

        float Frame(float seconds)
        {
            float f = seconds * 30f;
            if (loops && duration > 0f) { float frames = duration * 30f; f -= MathF.Floor(f / frames) * frames; }
            return Math.Clamp(f, 0f, left.Length - 1);
        }

        /// <summary>Each ball, each heading, the root, at <paramref name="seconds"/> (linearly between frames).</summary>
        public void At(float seconds, out Vector3 l, out Vector3 r, out float lh, out float rh, out Vector3 at)
        {
            float f = Frame(seconds);
            int i = (int)f, j = Math.Min(i + 1, left.Length - 1);
            float t = f - i;
            l = PoseMath.Lerp(left[i], left[j], t);
            r = PoseMath.Lerp(right[i], right[j], t);
            lh = leftHeading[i] + DetMath.WrapAngle(leftHeading[j] - leftHeading[i]) * t;
            rh = rightHeading[i] + DetMath.WrapAngle(rightHeading[j] - rightHeading[i]) * t;
            at = PoseMath.Lerp(root[i], root[j], t);
        }

        /// <summary>Whether the motion has each ball planted at <paramref name="seconds"/> (the nearest frame).</summary>
        public (bool Left, bool Right) Down(float seconds)
        {
            int i = (int)(Frame(seconds) + 0.5f);
            i = Math.Min(i, left.Length - 1);
            return (leftDown[i], rightDown[i]);
        }
    }

    public void Run(World world)
    {
        var bodies = PoseRigSystem.Optional<Body>(world, ref bodiesKnown);
        var feetStore = PoseRigSystem.Optional<PersonFeet>(world, ref feetKnown);
        if (bodies is null || feetStore is null) return;
        var animations = world.Store<CharacterAnimation>();
        var rigs = world.Store<PoseRig>();
        var inputs = world.Store<MotionInput>();
        world.TryStore<PersonFlight>(out var flights);   // on a flight, the stair clip's feet
        var data = rigs.Data;
        var ents = rigs.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var anim = ref animations.Find(e);
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) continue;
            var kind = assets.Kind(anim.Kind);
            if (kind.Person is null || kind.Limbs is null) continue;
            if (!feetStore.Has(e)) world.Set(e, new PersonFeet());
            ref var input = ref inputs.Find(e);
            var motion = Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input;
            var flight = flights is not null && flights.Has(e) ? flights.Get(e) : default;
            Step(ref feetStore.Get(e), in anim, in data[i], in motion, body.Feet, body.Yaw, World.TickSeconds, flight);
        }
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>One person's feet for one tick: from its animation, its rig (the legs' turn, the ground), how its body moves, where its feet are and which way it faces.</summary>
    public void Step(ref PersonFeet feet, in CharacterAnimation anim, in PoseRig rig, in MotionInput input, Vector3 at, float yaw, float seconds) =>
        Step(ref feet, anim, rig, input, at, yaw, seconds, default);

    /// <summary>As the overload before, on a flight of stairs (<paramref name="flight"/>): the stair clip's balls laid over the gait's by its share, as the pose lays them.</summary>
    public void Step(ref PersonFeet feet, in CharacterAnimation anim, in PoseRig rig, in MotionInput input, Vector3 at, float yaw, float seconds, in PersonFlight flight)
    {
        var s = settings;
        var kind = assets.Kind(anim.Kind);
        var person = kind.Person!;
        feet.On = true;
        feet.Bend = s.Bend;
        feet.Soft = s.Soft;
        feet.LiftHips = s.LiftHips;
        bool grounded = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging;

        // The two clips of the pose and their shares.
        float blend = anim.PreviousClip >= 0 ? Math.Clamp(anim.Blend, 0f, 1f) : 1f;
        float share = 0f, held = 0f, lift = 0f;
        Vector3 ballL = default, ballR = default, root = default;
        float headL = 0f, headR = 0f, downL = 0f, downR = 0f;
        bool first = true;
        for (int k = 0; k < 2; k++)
        {
            int clip = k == 0 ? anim.Clip : anim.PreviousClip;
            float w = k == 0 ? blend : 1f - blend;
            float time = k == 0 ? anim.Time : anim.PreviousTime;
            if (clip < 0 || w <= 0f) continue;
            if (!Feet(anim.Kind, kind, person, clip, time, out var l, out var r, out float lh, out float rh, out var rt, out var dl, out var dr)) continue;
            if (flight.Share > 0f && person.Stairs is { } stairs && stairs.For(person, clip, flight.Way, flight.Share) is var (stair, _, sw) && stair >= 0)
            {
                // The stair clip at the same share of its cycle, by its share.
                float d = assets.Clip(clip).Duration, p = d > 0f ? time / d : 0f;
                p -= MathF.Floor(p);
                var ts = Track(anim.Kind, kind, stair);
                float st = p * assets.Clip(stair).Duration;
                ts.At(st, out var l2, out var r2, out float lh2, out float rh2, out var rt2);
                var (dl2, dr2) = ts.Down(st);
                (l, r, rt) = (PoseMath.Lerp(l, l2, sw), PoseMath.Lerp(r, r2, sw), PoseMath.Lerp(rt, rt2, sw));
                lh += DetMath.WrapAngle(lh2 - lh) * sw;
                rh += DetMath.WrapAngle(rh2 - rh) * sw;
                if (sw >= 0.5f) (dl, dr) = (dl2, dr2);
            }
            if (first) { (ballL, ballR, headL, headR, root) = (l, r, lh, rh, rt); first = false; share = w; }
            else
            {
                float t = w / (share + w);
                (ballL, ballR, root) = (PoseMath.Lerp(ballL, l, t), PoseMath.Lerp(ballR, r, t), PoseMath.Lerp(root, rt, t));
                headL += DetMath.WrapAngle(lh - headL) * t;
                headR += DetMath.WrapAngle(rh - headR) * t;
                share += w;
            }
            downL += dl ? w : 0f;
            downR += dr ? w : 0f;
            if (Stands(kind, person, clip)) held += w;
            if (Lifts(person, clip)) lift += w;
        }
        feet.Lift = s.HeelLift > 0f && grounded && person.Crouch?.HeelsUp is not >= 0 ? s.HeelLift * lift * MathF.Min(1f, rig.GroundWeight) : 0f;
        feet.StandPitch = Ground(anim.Kind, kind).Pitch;
        if (first || !s.Locks)
        {
            feet.LeftHold = feet.RightHold = 0f;
            feet.Planted(true, false); feet.Planted(false, false);
            feet.Releasing(true, false); feet.Releasing(false, false);
            return;
        }

        // The legs' turn about the root bone, then the body's place: each ball and heading in the world.
        if (rig.LegYaw != 0f)
        {
            var (ls, lc) = DetMath.SinCos(rig.LegYaw);
            ballL = About(ballL, root, ls, lc);
            ballR = About(ballR, root, ls, lc);
            headL += rig.LegYaw;
            headR += rig.LegYaw;
        }
        float turn = yaw - kind.FacingYaw;
        var (sin, cos) = DetMath.SinCos(turn);
        float scale = kind.Scale > 0f ? kind.Scale : 1f;
        float step = seconds / MathF.Max(1e-3f, s.LockSeconds);
        Foot(ref feet, true, ToWorld(ballL, at, sin, cos, scale), DetMath.WrapAngle(headL + turn), grounded && held > 0f && downL >= 0.5f * share, held, step, seconds, sin, cos, scale, at);
        Foot(ref feet, false, ToWorld(ballR, at, sin, cos, scale), DetMath.WrapAngle(headR + turn), grounded && held > 0f && downR >= 0.5f * share, held, step, seconds, sin, cos, scale, at);
    }

    /// <summary>One foot's lock for one tick (<see cref="PersonFeetSystem"/>, 3).</summary>
    void Foot(ref PersonFeet feet, bool left, Vector3 drawn, float heading, bool down, float held, float step, float seconds, float sin, float cos, float scale, Vector3 at)
    {
        var s = settings;
        ref var lockAt = ref left ? ref feet.LeftAt : ref feet.RightAt;
        ref var lockHeading = ref left ? ref feet.LeftHeading : ref feet.RightHeading;
        ref var hold = ref left ? ref feet.LeftHold : ref feet.RightHold;
        ref var was = ref left ? ref feet.LeftWas : ref feet.RightWas;
        float Off(Vector3 p) { float dx = p.X - drawn.X, dy = p.Y - drawn.Y; return MathF.Sqrt(dx * dx + dy * dy); }
        bool still = Off(was) <= PersonFeetSettings.TakeSpeed * seconds;   // drawn coming to rest over the ground: it may be taken
        was = drawn;

        bool take = down && (!feet.Planted(left) || hold <= 0f);
        if (take && still)
        {
            // Newly planted: taken where it is drawn now (the motion's, or still partly the last lock's as it lets go).
            if (hold > 0f && Off(lockAt) <= s.Replant)
            {
                lockAt = PoseMath.Lerp(drawn, lockAt, hold);
                lockHeading = heading + DetMath.WrapAngle(lockHeading - heading) * hold;
            }
            else (lockAt, lockHeading, hold) = (drawn, heading, 0f);
            feet.Planted(left, true);
            feet.Releasing(left, false);
        }
        else if (!down || take) { feet.Planted(left, false); feet.Releasing(left, false); }   // lifted, or not yet come to rest
        if (Off(lockAt) > 1f) (lockAt, lockHeading, hold) = (drawn, heading, 0f);   // the body put somewhere else: no lock across it
        if (feet.Planted(left) && !feet.Releasing(left) && Off(lockAt) > s.Replant) feet.Releasing(left, true);
        float target = feet.Planted(left) && !feet.Releasing(left) ? held : 0f;
        hold = Towards(hold, target, step);
        if (feet.Releasing(left) && hold <= 0f)
        {
            (lockAt, lockHeading) = (drawn, heading);   // let go: taken again where the motion has it
            feet.Releasing(left, false);
        }
        if (!feet.Planted(left) && hold <= 0f) (lockAt, lockHeading) = (drawn, heading);
        // The heading kept within SwivelMost of the motion's: past it the foot swivels on its ball.
        float off = Math.Clamp(DetMath.WrapAngle(lockHeading - heading), -s.SwivelMost, s.SwivelMost);
        lockHeading = DetMath.WrapAngle(heading + off);

        // For the pose: the ball in the model's space, the turn from the motion's heading.
        var ball = ToModel(lockAt - at, sin, cos) / scale;
        if (left) { feet.LeftBall = ball; feet.LeftTurn = off; }
        else { feet.RightBall = ball; feet.RightTurn = off; }
    }

    /// <summary>
    /// Where the clip <paramref name="clip"/> has the balls, the feet's headings and the root at <paramref name="seconds"/> (model space),
    /// and whether it has each ball planted. A person's own clips (a gait's blend, a turn for some degrees) from the tables of the
    /// clips that stand for them (<see cref="PersonMotion.Sources"/>), so no table is made when a body first goes some new way.
    /// </summary>
    bool Feet(int kindIndex, CharacterKind kind, PersonMotion person, int clip, float seconds, out Vector3 l, out Vector3 r, out float lh, out float rh, out Vector3 root,
              out bool downL, out bool downR)
    {
        if (person.Sources(assets.Clip(clip), seconds, out int a, out float atA, out int b, out float atB, out float w))
        {
            var ta = Track(kindIndex, kind, a);
            ta.At(atA, out l, out r, out lh, out rh, out root);
            (downL, downR) = ta.Down(atA);
            if (w > 0f)
            {
                var tb = Track(kindIndex, kind, b);
                tb.At(atB, out var l2, out var r2, out float lh2, out float rh2, out var root2);
                (l, r, root) = (PoseMath.Lerp(l, l2, w), PoseMath.Lerp(r, r2, w), PoseMath.Lerp(root, root2, w));
                lh += DetMath.WrapAngle(lh2 - lh) * w;
                rh += DetMath.WrapAngle(rh2 - rh) * w;
                var (dl2, dr2) = tb.Down(atB);
                (downL, downR) = (w < 0.5f ? downL : dl2, w < 0.5f ? downR : dr2);
            }
            return true;
        }
        var t = Track(kindIndex, kind, clip);
        t.At(seconds, out l, out r, out lh, out rh, out root);
        (downL, downR) = t.Down(seconds);
        return true;
    }

    /// <summary>Whether a clip is one a body stands in, whose planted feet are held: the stand, a turn on the spot, the crouch's own (not its walk).</summary>
    internal static bool Stands(CharacterKind kind, PersonMotion person, int clip) =>
        clip == kind.Motion(MotionRole.Idle).Clip || person.TurnsOf(clip) is not null || (person.Crouch is { } c && c.Holds(clip) && c.Walk?.Holds(clip) != true)
        || person.Air?.Stands(clip) == true   // a standing landing, its feet on the ground from the contact
        || person.PlayerGaits?.GaitOfStop(clip) >= 0;   // the player's own stops, their feet held but in their slides

    /// <summary>Whether a clip has its heels lifted (<see cref="PersonFeetSettings.HeelLift"/>): the crouch held and the crouched turns.</summary>
    internal static bool Lifts(PersonMotion person, int clip) =>
        person.Crouch is { } c && (clip == c.Hold || c.Turns?.Holds(clip) == true);

    FootTrack Track(int kindIndex, CharacterKind kind, int clip)
    {
        long key = ((long)kindIndex << 32) | (uint)clip;
        if (tracks.TryGetValue(key, out var t)) return t;
        // First sight of this kind: a table for every clip it plays, so later changes of motion allocate nothing.
        foreach (var m in kind.Motions)
            if (m.Clip >= 0 && !(assets.Clip(m.Clip) is GaitBlendClip or TurnClip)) Build(kindIndex, kind, m.Clip);
        if (kind.Person is { } person)
        {
            foreach (int c in person.PoseClips) Build(kindIndex, kind, c);
            if (person.Crouch is { } crouch)
                foreach (int c in new[] { crouch.Hold, crouch.Down, crouch.Up, crouch.Rise, crouch.Kneel, crouch.Settle, crouch.KneelDown, crouch.KneelUp })
                    if (c >= 0) Build(kindIndex, kind, c);
        }
        Build(kindIndex, kind, clip);
        return tracks[key];
    }

    /// <summary>
    /// For a kind, from the stand's start (model space): the balls' height on the ground, the height a planted ball is near; and the feet's
    /// pitch, the line from the ball up to the ankle above level (<see cref="PersonFeet.StandPitch"/>).
    /// </summary>
    (float Ball, float Pitch) Ground(int kindIndex, CharacterKind kind)
    {
        if (grounds.TryGetValue(kindIndex, out var g)) return g;
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        var limbs = kind.Limbs!;
        int idle = kind.Motion(MotionRole.Idle).Clip;
        Pose(skeleton, idle >= 0 ? assets.Clip(idle) : null, 0f);
        var up = Heel(limbs.LeftLeg) - Tip(limbs.LeftLeg);
        g = (MathF.Min(Tip(limbs.LeftLeg).Z, Tip(limbs.RightLeg).Z), DetMath.Atan2(up.Z, MathF.Sqrt(up.X * up.X + up.Y * up.Y)));
        grounds[kindIndex] = g;
        return g;
    }

    Vector3 Tip(in Limb leg) => leg.Tip >= 0 ? model[leg.Tip].Translation : leg.End >= 0 ? model[leg.End].Translation : Vector3.Zero;

    Vector3 Heel(in Limb leg) => leg.End >= 0 ? model[leg.End].Translation : Vector3.Zero;

    void Pose(Skeleton skeleton, IAnimationClip? clip, float seconds)
    {
        int n = skeleton.Count;
        if (local.Length < n) { local = new BoneTransform[n]; model = new Matrix4x4[n]; }
        if (clip is null) PoseEvaluator.Rest(skeleton, local.AsSpan(0, n));
        else clip.Sample(seconds, skeleton, local.AsSpan(0, n));
        PoseEvaluator.ToModel(skeleton, local.AsSpan(0, n), model.AsSpan(0, n));
    }

    void Build(int kindIndex, CharacterKind kind, int clipIndex)
    {
        long key = ((long)kindIndex << 32) | (uint)clipIndex;
        if (tracks.ContainsKey(key)) return;
        float ground = Ground(kindIndex, kind).Ball;
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        var limbs = kind.Limbs!;
        int rootBone = kind.Person!.Root;
        var clip = assets.Clip(clipIndex);
        int frames = Math.Max(1, (int)MathF.Ceiling(clip.Duration * 30f) + 1);
        var left = new Vector3[frames];
        var right = new Vector3[frames];
        var lh = new float[frames];
        var rh = new float[frames];
        var root = new Vector3[frames];
        for (int f = 0; f < frames; f++)
        {
            Pose(skeleton, clip, MathF.Min(f / 30f, clip.Duration));
            left[f] = Tip(limbs.LeftLeg);
            right[f] = Tip(limbs.RightLeg);
            lh[f] = Heading(Heel(limbs.LeftLeg), left[f]);
            rh[f] = Heading(Heel(limbs.RightLeg), right[f]);
            root[f] = (uint)rootBone < (uint)skeleton.Count ? model[rootBone].Translation : Vector3.Zero;
        }
        // Planted: made on the clip as the models repository made it — a turn with its root's turn, a gait over the ground at its own speed.
        IAnimationClip made = clip is TurnClip turn ? turn.Source : clip;
        var over = Vector3.Zero;
        if (made is INotedClip noted && noted.Notes.GroundSpeed > 0f && !float.IsNaN(noted.Notes.HeadingDeg))
        {
            var (hs, hc) = DetMath.SinCos(noted.Notes.HeadingDeg * DetMath.DegToRad);
            over = new Vector3(-hs, hc, 0f) * noted.Notes.GroundSpeed;
        }
        // The player's own starts and stops made in a capsule moved by their speed a frame: planted against it.
        var steps = kind.Person!.PlayerGaits?.Steps(clipIndex) == true && made is INotedClip { Notes.StartStop: { Speeds.Length: > 1 } } stepped ? stepped.Notes.StartStop : null;
        var groundL = new Vector3[frames];
        var groundR = new Vector3[frames];
        for (int f = 0; f < frames; f++)
        {
            if (ReferenceEquals(made, clip)) { (groundL[f], groundR[f]) = (left[f], right[f]); continue; }
            Pose(skeleton, made, MathF.Min(f / 30f, made.Duration));
            (groundL[f], groundR[f]) = (Tip(limbs.LeftLeg), Tip(limbs.RightLeg));
        }
        // A stair clip's balls are on its own treads, not at the stand's height.
        int stairSlot = kind.Person!.Stairs is { } stairs ? Array.IndexOf(stairs.All, clipIndex) : -1;
        tracks[key] = new FootTrack(left, right, lh, rh, root, Down(groundL), Down(groundR), clip.Duration, clip.Loops);

        bool[] Down(Vector3[] ball)
        {
            var down = new bool[frames];
            for (int f = 0; f < frames; f++)
            {
                int a = f > 0 ? f - 1 : clip.Loops && frames > 2 ? frames - 2 : f, b = f < frames - 1 ? f + 1 : clip.Loops && frames > 2 ? 1 : f;
                float span = (a == f || b == f ? 1f : 2f) / 30f;
                var by = steps is null ? over : new Vector3(0f, steps.SpeedAt(f / 30f), 0f);   // the capsule's speed that frame (ahead, +Y)
                var v = (ball[b] - ball[a]) / (a == b ? 1f : span) + by;
                if (a == b) v = by;
                float speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
                float under = stairSlot >= 0 ? kind.Person!.Stairs!.ClipGround(stairSlot, MathF.Min(f / 30f, clip.Duration), ball[f].Y) : 0f;
                down[f] = ball[f].Z - under <= ground + PersonFeetSettings.ContactHeight && speed <= PersonFeetSettings.ContactSpeed;
                if (steps is not null && steps.Sliding(ReferenceEquals(ball, groundL), f / 30f)) down[f] = false;   // its slide: off the lock, taken again after
            }
            return down;
        }
    }

    /// <summary>The heading of the line from the heel (the ankle) to the ball about the vertical (radians).</summary>
    static float Heading(Vector3 heel, Vector3 ball) => DetMath.Atan2(ball.Y - heel.Y, ball.X - heel.X);

    /// <summary><paramref name="p"/> turned about the vertical through <paramref name="pivot"/>.</summary>
    static Vector3 About(Vector3 p, Vector3 pivot, float sin, float cos)
    {
        float x = p.X - pivot.X, y = p.Y - pivot.Y;
        return new Vector3(pivot.X + cos * x - sin * y, pivot.Y + sin * x + cos * y, p.Z);
    }

    /// <summary>A model-space point in the world (<see cref="CharacterKind.Placement"/>'s turn, scale and place).</summary>
    static Vector3 ToWorld(Vector3 p, Vector3 at, float sin, float cos, float scale) =>
        new(at.X + (cos * p.X - sin * p.Y) * scale, at.Y + (sin * p.X + cos * p.Y) * scale, at.Z + p.Z * scale);

    /// <summary>A world offset from the feet turned into the model's own axes.</summary>
    static Vector3 ToModel(Vector3 d, float sin, float cos) => new(cos * d.X + sin * d.Y, -sin * d.X + cos * d.Y, d.Z);

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}

/// <summary>
/// A person's foot laid on the pose by its <see cref="PersonFeet"/> (<see cref="PoseEvaluator.Place"/>), before the legs'
/// IK: from where the rig would put the ankle (the motion's, on its ground, the foot laid on the slope), the ball moved to its lock by
/// the lock's hold, the foot swivelled on its ball by its turn, and the heel lifted about the ball (the foot pitched up about the
/// line across it through the ball); the ankle's target and the foot's turn for <see cref="LimbIK.Solve"/>. With nothing held and no
/// lift, the rig's own target and turn, unchanged.
/// </summary>
public static class FootPlacement
{
    /// <summary>
    /// The ankle's target and the foot's turn (model space) for <paramref name="leg"/>, from the rig's <paramref name="ankle"/> and slope
    /// <paramref name="tilt"/>, the ball held at <paramref name="ball"/> (its X and Y) by <paramref name="hold"/>, the foot turned
    /// <paramref name="turn"/> about the vertical by the same hold and its heel lifted <paramref name="lift"/> radians above the feet's
    /// pitch standing (<paramref name="standPitch"/>; a heel the motion already has up is lifted only by what it lacks); and the lift's own
    /// turn (for the toes, <see cref="Toes"/>) and how far it raised the ankle.
    /// </summary>
    public static (Vector3 Ankle, Quaternion? Turn, Quaternion Lift, float Rise) Leg(Skeleton skeleton, ReadOnlySpan<BoneTransform> local, in Limb leg,
                                                                                   Vector3 ankle, Quaternion? tilt, Vector3 ball, float turn, float hold, float lift,
                                                                                   float standPitch = 0f)
    {
        hold = Math.Clamp(hold, 0f, 1f);
        if (!leg.Has || leg.Tip < 0 || (hold <= 0f && lift <= 0f)) return (ankle, tilt, Quaternion.Identity, 0f);
        var foot = LimbIK.ModelRotation(skeleton, local, leg.End);
        if (lift > 0f)
        {
            var up = -LimbIK.Rotate(foot, local[leg.Tip].Translation);     // the ball up to the ankle, as the motion has the foot
            float pitch = DetMath.Atan2(up.Z, MathF.Sqrt(up.X * up.X + up.Y * up.Y));
            lift = MathF.Max(0f, lift - MathF.Max(0f, pitch - standPitch));
        }
        var laid = tilt is { } t ? LimbIK.Normalize(PoseMath.Multiply(t, foot)) : foot;
        var b0 = ankle + LimbIK.Rotate(laid, local[leg.Tip].Translation);
        var back = ankle - b0;                                   // the ball to the ankle
        var swivel = Quaternion.Identity;
        if (hold > 0f && turn != 0f)
        {
            var (s, c) = DetMath.SinCos(turn * hold * 0.5f);
            swivel = new Quaternion(0f, 0f, s, c);
            back = LimbIK.Rotate(swivel, back);
        }
        var b1 = new Vector3(b0.X + (ball.X - b0.X) * hold, b0.Y + (ball.Y - b0.Y) * hold, b0.Z);
        var raise = Quaternion.Identity;
        float rise = 0f;
        if (lift > 0f)
        {
            // About the line across the foot through the ball: a positive turn about up × (heel to ball) lifts the heel.
            var axis = LimbIK.Unit(LimbIK.Cross(Vector3.UnitZ, new Vector3(-back.X, -back.Y, 0f)));
            if (axis != Vector3.Zero)
            {
                var (s, c) = DetMath.SinCos(lift * 0.5f);
                raise = new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
                var lifted = LimbIK.Rotate(raise, back);
                rise = lifted.Z - back.Z;
                back = lifted;
            }
        }
        var end = PoseMath.Multiply(raise, swivel);
        return (b1 + back, LimbIK.Normalize(tilt is { } tt ? PoseMath.Multiply(end, tt) : end), raise, rise);
    }

    /// <summary>
    /// The legs' soft limit (<see cref="LimbIK.SoftReach"/>) on an ankle's <paramref name="target"/>, by <paramref name="weight"/>: a target
    /// further from the hip than <paramref name="soft"/> × the leg's reach (in <paramref name="model"/>, the pose as the hips now are) is
    /// brought in along the line to it, so the leg comes to full stretch smoothly and never snaps straight; nearer, unchanged.
    /// </summary>
    public static Vector3 Soft(ReadOnlySpan<Matrix4x4> model, in Limb leg, Vector3 target, float soft, float weight)
    {
        if (!leg.Has || soft <= 0f || weight <= 0f) return target;
        var hip = model[leg.Upper].Translation;
        var toward = target - hip;
        float d = LimbIK.Length(toward), reach = LimbIK.Reach(model, leg);
        if (d <= reach * soft || d < 1e-6f) return target;
        float r = LimbIK.SoftReach(d, reach, soft);
        return hip + toward * ((d + (r - d) * MathF.Min(weight, 1f)) / d);
    }

    /// <summary>
    /// After the IK: the toes (<paramref name="leg"/>'s tip, the ball's bone) turned back by the heel lift <paramref name="raise"/>, so they
    /// stay on the ground as the foot pitches up about them.
    /// </summary>
    public static void Toes(Skeleton skeleton, Span<BoneTransform> local, in Limb leg, Quaternion raise)
    {
        if (leg.Tip < 0 || leg.End < 0 || raise == Quaternion.Identity) return;
        var foot = LimbIK.ModelRotation(skeleton, local, leg.End);
        var want = PoseMath.Multiply(LimbIK.Conjugate(raise), PoseMath.Multiply(foot, local[leg.Tip].Rotation));
        local[leg.Tip] = local[leg.Tip] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Conjugate(foot), want)) };
    }
}
