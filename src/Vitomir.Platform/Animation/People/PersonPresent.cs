// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: lean into turns ( atan(v·ω/g), capped about 18° sprinting, 12° running, little walking, eased over about 0.15 s), the head
// and chest lead the turn toward the way of travel while moving, and ("the head looks where the body is going ... absolutely natural movement": the way
// ahead, into a turn, the landing in the air; small and smoothed), from his Horizon Zero Dawn footage (video 6: banked 15-20° sprinting, 10-15° running).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the player's lean, look and lead, and the ground states' inertialized joins</b> — presentation (<b>SANDBOX</b>, ours): the drawn pose only
/// (<see cref="CharacterPoses.Draw"/>; the simulation's pose is made without them), its state in a store that is not digested, so a tape's digests are the same
/// with any of it on or off. They ride on the player's own gaits (<see cref="PersonGaitSettings"/>): a body not playing them is drawn as before. Each has its
/// setting (the host's <c>lean</c>, <c>headLook</c>, <c>leadTurns</c>, <c>inertialJoins</c>).
/// </summary>
public sealed record PersonPresentSettings
{
    /// <summary>The drawn body leaned into a turn about its feet on the ground (the feet's IK keeps them where they are), by atan(v·ω/g).</summary>
    public bool Lean { get; init; } = true;

    /// <summary>The lean's most at the sprint's pace and at the run's (degrees: 18 and 12; between, by the speed).</summary>
    public float LeanSprintDegrees { get; init; } = 18f;
    public float LeanRunDegrees { get; init; } = 12f;

    /// <summary>The share of the lean walking (0.25: the run's cap and the lean itself by it at the walk's pace, rising to all of it at the run's).</summary>
    public float LeanWalkShare { get; init; } = 0.25f;

    /// <summary>How long the lean takes to come in or go out (s; 0.15: nine-tenths of the way in that time, on a critically damped spring).</summary>
    public float LeanEase { get; init; } = 0.15f;

    /// <summary>The head looking where the body is going (the way ahead and into a turn; down toward the landing in the air), at most so far (degrees).</summary>
    public bool Look { get; init; } = true;
    public float LookYawDegrees { get; init; } = 20f;
    public float LookPitchDegrees { get; init; } = 15f;

    /// <summary>How far ahead in a turn the head looks (s of the turn's rate: the way of travel in that long).</summary>
    public float LookAhead { get; init; } = 0.35f;

    /// <summary>The chest turned after the head toward the way of travel (a share of the head's), the head first, the hips last (the legs' turn).</summary>
    public bool Lead { get; init; } = true;
    public float ChestShare { get; init; } = 0.5f;

    /// <summary>The head's and the chest's catching up (s, half-lives): the head first, the chest after.</summary>
    public float HeadHalfLife { get; init; } = 0.05f;
    public float ChestHalfLife { get; init; } = 0.1f;

    /// <summary>
    /// The ground states' changes of clip (the gaits into each other both ways, a start, a stop, the crouch in and out) joined by inertialization (the earlier build's
    /// <see cref="Inertial"/>) in place of the crossfade: the new clip played whole at once, the old pose's difference from it and its velocity's decayed to
    /// nought over <see cref="JoinSeconds"/> (0.2 s, the crossfade's own length).
    /// </summary>
    public bool Joins { get; init; } = true;
    public float JoinSeconds { get; init; } = 0.2f;

    /// <summary>Whether any is on.</summary>
    public bool Any => Lean || Look || Lead || Joins;

    /// <summary>None of it (the earlier build's drawing).</summary>
    public static PersonPresentSettings Off => new() { Lean = false, Look = false, Lead = false, Joins = false };
}

/// <summary>
/// A person's lean, look and lead (<see cref="PersonPresentSettings"/>): moved on in the tick by <see cref="PersonPresentSystem"/>, laid on the pose by
/// <see cref="PersonPresentRule"/>. Presentation: the store is not digested.
/// </summary>
public struct PersonPresent
{
    /// <summary>The lean (radians, toward the body's left positive: into a turn to the left) and its rate; the way it leans about (radians from the facing).</summary>
    public float Roll, RollRate, Axis;

    /// <summary>The head's and the chest's turn toward the way (radians from where the chain has them, to the left positive) and their rates; the head's pitch (up positive).</summary>
    public float Head, HeadRate, Chest, ChestRate, Pitch, PitchRate;

    /// <summary>The way the body went last tick (radians in the world), and whether it was going.</summary>
    public float Heading;
    public int Going;

    /// <summary>The join under way (<see cref="PersonJoin"/>), the one it interrupted (its offset then carried into it), and the fade last seen (its clip and share).</summary>
    public PersonJoin Join, Carry;
    public int LastPrevious;
    public float LastBlend;
}

/// <summary>
/// One inertialized join: the clip left and the clip taken (their places + 1; 0 none), each one's time and rate at the join, how long ago it began, how
/// long it takes; and, for a join that interrupted another, how far into that one it was (<see cref="CarriedAt"/>).
/// </summary>
public struct PersonJoin
{
    public int From, To;
    public float FromTime, ToTime, FromRate, ToRate, Age, Seconds, CarriedAt;
    public readonly bool On => From > 0 && To > 0 && Age < Seconds;
}

/// <summary>A person's lean, look and lead this tick, for the pose (made when the pose is made; not kept); and its join and the one that carried into it.</summary>
public readonly record struct PersonPresentPose(float Roll, float Axis, float Head, float Chest, float Pitch)
{
    public bool On => Roll != 0f || Head != 0f || Chest != 0f || Pitch != 0f;
    public PersonJoin Join { get; init; }
    public PersonJoin Carry { get; init; }
}

/// <summary>
/// Moves every player's <see cref="PersonPresent"/> on by one tick (after the feet, <see cref="AnimationRegistration.AnimationOrder"/> + <see cref="Order"/>),
/// for each body playing the player's own gaits (<see cref="PlayerGaits.Plays"/>):
/// <list type="bullet">
/// <item><b>The lean</b>: on the ground and moving (no move, no dodge), atan(v·ω/g) from the body's speed and the turning of its way (ω), by the walk's share
/// below the run's pace, capped by its pace (the run's cap times the walk's share at the walk's, the run's at the run's, the sprint's at the sprint's), on a
/// critically damped spring of <see cref="PersonPresentSettings.LeanEase"/>; about the way the body goes.</item>
/// <item><b>The joins</b>: a fade begun between two of the ground states' clips taken as an inertialized join (<see cref="PersonJoinRule"/>).</item>
/// <item><b>The look and the head's lead</b>: moving, the head toward the way of travel <see cref="PersonPresentSettings.LookAhead"/> ahead in the turn,
/// from the facing (the view), within <see cref="PersonPresentSettings.LookYawDegrees"/>, less as the way goes behind (none past 120°: backpedalling, the head
/// stays on the view); the chest a share of it after; in the air the head down toward the landing. Standing still, nothing: the chain follows the camera as
/// today.</item>
/// </list>
/// Deterministic (<see cref="DetMath"/>); allocates nothing.
/// </summary>
public sealed class PersonPresentSystem(CharacterAssets assets, PersonPresentSettings settings) : ISystem
{
    public const string Name = "platform.person-present";
    public const int Order = 18;

    public PersonPresentSettings Settings => settings;

    public void Run(World world)
    {
        if (!world.TryStore<PersonStep>(out var steps) || !world.TryStore<PersonPresent>(out var presents) || !world.TryStore<Body>(out var bodies)) return;
        if (!world.TryService<PersonGaitSettings>(out var gaits)) return;
        var animations = world.Store<CharacterAnimation>();
        var inputs = world.Store<MotionInput>();
        var ents = steps.Entities;
        var data = steps.Data;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var anim = ref animations.Find(e);
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) continue;
            var person = assets.Kind(anim.Kind).Person;
            if (!PlayerGaits.Plays(gaits, person, data[i])) continue;
            if (!presents.Has(e)) world.Set(e, new PersonPresent());
            ref var input = ref inputs.Find(e);
            ref var p = ref presents.Get(e);
            Step(ref p, person!.PlayerGaits!, body, Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input, World.TickSeconds);
            if (settings.Joins) Joins(ref p, assets.Kind(anim.Kind), person, anim, World.TickSeconds);
        }
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>One person's lean, look and lead for one tick.</summary>
    public void Step(ref PersonPresent p, PlayerGaits gaits, in Body body, in MotionInput input, float dt)
    {
        var s = settings;
        var v = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = v.Length();
        bool going = speed > 0.5f;
        float heading = going ? DetMath.Atan2(v.Y, v.X) : p.Heading;
        float turnRate = going && p.Going != 0 ? DetMath.WrapAngle(heading - p.Heading) / dt : 0f;
        p.Heading = heading;
        p.Going = going ? 1 : 0;
        bool ground = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging;
        float away = going ? DetMath.WrapAngle(heading - body.Yaw) : 0f;   // the way it goes from the facing

        // The lean: atan(v·ω/g), by the walk's share and capped by the pace.
        float roll = 0f;
        if (s.Lean && ground && going)
        {
            float walk = gaits.Speeds[0], run = gaits.Speeds[1], sprint = gaits.Speeds[2] > 0f ? gaits.Speeds[2] : run;
            float share = speed <= walk ? s.LeanWalkShare : speed >= run ? 1f : s.LeanWalkShare + (1f - s.LeanWalkShare) * (speed - walk) / MathF.Max(1e-3f, run - walk);
            float runCap = s.LeanRunDegrees * DetMath.DegToRad, sprintCap = s.LeanSprintDegrees * DetMath.DegToRad;
            float cap = speed <= walk ? runCap * s.LeanWalkShare
                      : speed <= run ? runCap * (s.LeanWalkShare + (1f - s.LeanWalkShare) * (speed - walk) / MathF.Max(1e-3f, run - walk))
                      : speed >= sprint ? sprintCap : runCap + (sprintCap - runCap) * (speed - run) / MathF.Max(1e-3f, sprint - run);
            float g = 9.81f;
            roll = Math.Clamp(DetMath.Atan(speed * turnRate / g) * share, -cap, cap);
            p.Axis = away;
        }
        Spring(ref p.Roll, ref p.RollRate, roll, s.LeanEase / 2.81f, dt);

        // The look and the head's lead: toward the way ahead in the turn, less as it goes behind; in the air down toward the landing.
        float head = 0f, pitch = 0f;
        if ((s.Look || s.Lead) && going && input.Traversal == TraversalKind.None && !input.Dodging)
        {
            float ahead = DetMath.WrapAngle(away + turnRate * s.LookAhead);
            float behind = MathF.Abs(away) * DetMath.RadToDeg;
            float weight = behind <= 60f ? 1f : behind >= 120f ? 0f : 1f - (behind - 60f) / 60f;
            float most = s.LookYawDegrees * DetMath.DegToRad;
            head = Math.Clamp(ahead, -most, most) * weight;
        }
        if (s.Look && !input.Grounded && input.Traversal == TraversalKind.None && body.Velocity.Z < 0f)
        {
            // In the air, falling: the head down toward where the body will land (along its way through the air), at most so far.
            float down = DetMath.Atan2(-body.Velocity.Z, MathF.Max(speed, 0.5f));
            pitch = -MathF.Min(down * 0.6f, s.LookPitchDegrees * DetMath.DegToRad);
        }
        float chest = s.Lead ? head * s.ChestShare : 0f;
        if (!s.Look && !s.Lead) head = 0f;
        Spring(ref p.Head, ref p.HeadRate, head, s.HeadHalfLife, dt);
        Spring(ref p.Chest, ref p.ChestRate, chest, s.ChestHalfLife, dt);
        Spring(ref p.Pitch, ref p.PitchRate, pitch, s.HeadHalfLife * 2f, dt);
        if (MathF.Abs(p.Roll) < 1e-5f && MathF.Abs(p.RollRate) < 1e-4f && roll == 0f) (p.Roll, p.RollRate) = (0f, 0f);
        if (MathF.Abs(p.Head) < 1e-5f && MathF.Abs(p.HeadRate) < 1e-4f && head == 0f) (p.Head, p.HeadRate) = (0f, 0f);
        if (MathF.Abs(p.Chest) < 1e-5f && MathF.Abs(p.ChestRate) < 1e-4f && chest == 0f) (p.Chest, p.ChestRate) = (0f, 0f);
        if (MathF.Abs(p.Pitch) < 1e-5f && MathF.Abs(p.PitchRate) < 1e-4f && pitch == 0f) (p.Pitch, p.PitchRate) = (0f, 0f);
    }

    /// <summary>
    /// A new fade between two of the ground states' clips (<see cref="Grounded"/>) begun this tick is taken as an inertialized join (the clip left and the clip
    /// taken where they are now); one under way ages, and is done after its <see cref="PersonPresentSettings.JoinSeconds"/>. A join that interrupts another carries
    /// that one's offset then into its own.
    /// </summary>
    void Joins(ref PersonPresent p, CharacterKind kind, PersonMotion person, in CharacterAnimation a, float dt)
    {
        bool fading = a.PreviousClip >= 0 && a.Blend < 1f;
        bool begun = fading && (a.PreviousClip + 1 != p.LastPrevious || a.Blend < p.LastBlend - 1e-6f);
        p.LastPrevious = fading ? a.PreviousClip + 1 : 0;
        p.LastBlend = fading ? a.Blend : 1f;
        if (p.Join.From > 0) p.Join.Age += dt;
        if (p.Carry.From > 0) p.Carry.Age += dt;
        if (begun && Grounded(kind, person, a.Clip) && Grounded(kind, person, a.PreviousClip))
        {
            p.Carry = p.Join.On ? p.Join with { CarriedAt = p.Join.Age } : default;
            p.Join = new PersonJoin
            {
                From = a.PreviousClip + 1, To = a.Clip + 1, FromTime = a.PreviousTime, ToTime = a.Time, FromRate = a.PreviousRate, ToRate = a.Rate, Seconds = settings.JoinSeconds,
            };
        }
        if (p.Join.From > 0 && !p.Join.On) p.Join = p.Carry = default;
    }

    /// <summary>
    /// Whether a clip is one of the ground states' (the stand, the gaits' loops and blends ahead and every way, the sprint, the sneak, the player's own gaits and
    /// every start and stop, the crouch held, its ways down and up and its walk): a join between two of them is inertialized; any other (a turn on the spot, the
    /// air, a move, a hang, a ladder, a wall) keeps its crossfade.
    /// </summary>
    internal static bool Grounded(CharacterKind kind, PersonMotion person, int clip) =>
        clip >= 0 && (clip == kind.Motion(MotionRole.Idle).Clip || person.Walk?.Holds(clip) == true || person.Run?.Holds(clip) == true || clip == person.Sprint
                      || clip == person.Sneak || person.PlayerGaits?.Holds(clip) == true || person.Starts?.Holds(clip) == true
                      || (person.Crouch is { } c && (c.Holds(clip) || c.Walk?.Holds(clip) == true) && c.Turns?.Holds(clip) != true));

    /// <summary>A number on Holden's critically damped spring toward <paramref name="goal"/> (the half-life <paramref name="halfLife"/>), its rate carried.</summary>
    internal static void Spring(ref float x, ref float rate, float goal, float halfLife, float dt)
    {
        if (halfLife <= 1e-4f) { x = goal; rate = 0f; return; }
        float y = 2f * 0.69314718f / halfLife;
        float j0 = x - goal, j1 = rate + j0 * y, e = DetMath.Exp(-y * dt);
        x = e * (j0 + j1 * dt) + goal;
        rate = e * (rate - j1 * y * dt);
    }
}

/// <summary>
/// Laying a person's lean, look and lead on its pose (<see cref="PersonPresentPose"/>). Only the drawn pose has them (<see cref="CharacterPoses.Draw"/>): the
/// simulation's (the hit volumes, the eye the cameras ride, the swing) is made without them, so nothing simulated reads them and no digest changes.
/// </summary>
public static class PersonPresentRule
{
    /// <summary>A person's lean, look and lead this tick (none without the store, or for a body without it).</summary>
    public static PersonPresentPose Of(World world, EntityId entity)
    {
        if (!world.TryStore<PersonPresent>(out var store) || !store.Has(entity)) return default;
        ref readonly var p = ref store.Get(entity);
        return new PersonPresentPose(p.Roll, p.Axis, p.Head, p.Chest, p.Pitch) { Join = p.Join, Carry = p.Join.On ? p.Carry : default };
    }

    /// <summary>
    /// The look and the head's lead on the pose (after the chain, before the limbs are placed): the chest's turn over the spine by the chain's shares, the head's over
    /// the neck and the head, and the head's pitch about its own side.
    /// </summary>
    public static void Turn(Skeleton skeleton, Assets.People.PersonRig rig, in PersonPresentPose present, Span<BoneTransform> local)
    {
        var spine = rig.Spine;
        if (present.Chest != 0f && spine.Length > 0)
        {
            var shares = TurnChain.SpineShares;
            for (int i = 0; i < spine.Length; i++)
                if ((uint)spine[i] < (uint)local.Length) LegTurn.Turn(skeleton, spine[i], present.Chest * (spine.Length == shares.Length ? shares[i] : 1f / spine.Length), local);
        }
        bool neck = (uint)rig.Neck < (uint)local.Length, head = (uint)rig.Head < (uint)local.Length;
        float over = present.Head - present.Chest;
        if (over != 0f)
        {
            float each = neck && head ? over * 0.5f : over;
            if (neck) LegTurn.Turn(skeleton, rig.Neck, each, local);
            if (head) LegTurn.Turn(skeleton, rig.Head, each, local);
        }
        if (present.Pitch != 0f && head)
        {
            // About the head's side as it now faces (its turn about the vertical in the model: the line between the eyes, square to the facing when it is 0).
            var (s, c) = DetMath.SinCos(present.Head);
            var side = new Vector3(c, s, 0f);
            float each = neck ? present.Pitch * 0.5f : present.Pitch;
            if (neck) About(skeleton, rig.Neck, side, each, local);
            About(skeleton, rig.Head, side, each, local);
        }
    }

    /// <summary>
    /// The lean on the pose (in the limbs' placing, before the legs' IK: <see cref="PoseEvaluator.Place"/>): the hips and all above them turned about the way the body
    /// goes through the point between the feet on the ground (the root bone's joint), so the feet's targets, taken before, keep them where they are.
    /// </summary>
    public static void Roll(Skeleton skeleton, int pelvis, in PersonPresentPose present, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (present.Roll == 0f || (uint)pelvis >= (uint)local.Length) return;
        int parent = skeleton.Bones[pelvis].Parent;
        var pivot = parent >= 0 ? model[parent].Translation : Vector3.Zero;
        var q = Lean(present);
        var parentRotation = parent >= 0 ? LimbIK.ModelRotation(skeleton, local, parent) : Quaternion.Identity;
        var rotation = LimbIK.Normalize(PoseMath.Multiply(q, LimbIK.ModelRotation(skeleton, local, pelvis)));
        var at = pivot + LimbIK.Rotate(q, model[pelvis].Translation - pivot);
        var back = LimbIK.Conjugate(parentRotation);
        local[pelvis] = new BoneTransform(LimbIK.Normalize(PoseMath.Multiply(back, rotation)), LimbIK.Rotate(back, at - pivot));
    }

    /// <summary>The lean's turn in the model: about the way the body goes (<see cref="PersonPresentPose.Axis"/> from the facing, +Y), toward its left for a positive lean.</summary>
    static Quaternion Lean(in PersonPresentPose present)
    {
        var (s, c) = DetMath.SinCos(present.Axis);
        var axis = new Vector3(-s, c, 0f);
        var (hs, hc) = DetMath.SinCos(-present.Roll * 0.5f);
        return new Quaternion(axis.X * hs, axis.Y * hs, axis.Z * hs, hc);
    }

    /// <summary>A bone turned by <paramref name="angle"/> about <paramref name="axis"/> in the model (as <see cref="LegTurn.Turn"/> turns one about the vertical).</summary>
    static void About(Skeleton skeleton, int b, Vector3 axis, float angle, Span<BoneTransform> local)
    {
        int parent = skeleton.Bones[b].Parent;
        var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), axis) : axis;
        seen = LimbIK.Unit(seen);
        var (s, c) = DetMath.SinCos(angle * 0.5f);
        var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
        local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
    }

}

/// <summary>
/// <b>an inertialized join on the pose</b> (the earlier build's <see cref="Inertial"/>, Holden's cubic, bone by bone): the clip taken played whole, and on each bone the
/// difference the clip left had from it at the join (its turn, as an axis times its angle, and its place) and the difference of their speeds there, decayed to nought
/// over the join (<c>x·H0(s) + v·T·H1(s)</c>): so the pose and its speed are whole at the join and the new clip's alone at its end. A join that interrupted another
/// carries that one's offset at that moment in its own. Allocates nothing once its scratch is made (each thread its own).
/// </summary>
public static class PersonJoinRule
{
    const float H = 1f / 60f;   // the step the speeds at the join are measured over (s)

    [ThreadStatic] static BoneTransform[]? a0, a1, b0, b1;
    [ThreadStatic] static Vector3[]? turn, turnSpeed, place, placeSpeed;

    /// <summary>Lays <paramref name="join"/>'s offset (and <paramref name="carry"/>'s carried into it) on <paramref name="local"/>, the clip taken's pose.</summary>
    public static void Apply(CharacterAssets assets, Skeleton skeleton, in PersonJoin join, in PersonJoin carry, Span<BoneTransform> local)
    {
        if (!join.On) return;
        int n = Math.Min(skeleton.Count, local.Length);
        if (a0 is null || a0.Length < n)
        {
            (a0, a1, b0, b1) = (new BoneTransform[n], new BoneTransform[n], new BoneTransform[n], new BoneTransform[n]);
            (turn, turnSpeed, place, placeSpeed) = (new Vector3[n], new Vector3[n], new Vector3[n], new Vector3[n]);
        }
        Differences(assets, skeleton, join, n, withSpeed: true, 1f);
        if (carry.From > 0) Differences(assets, skeleton, carry with { Age = carry.CarriedAt }, n, withSpeed: false, 1f, add: true);
        float t = join.Age, T = join.Seconds;
        for (int b = 0; b < n; b++)
        {
            var r = Inertial.Offset(turn![b], turnSpeed![b], t, T);
            var x = Inertial.Offset(place![b], placeSpeed![b], t, T);
            local[b] = new BoneTransform(LimbIK.Normalize(PoseMath.Multiply(Exp(r), local[b].Rotation)), local[b].Translation + x);
        }
    }

    /// <summary>
    /// Each bone's difference at the join (the clip left less the clip taken: turn and place) and, when asked, their speeds' difference; with
    /// <paramref name="add"/> the offset of an interrupted join at the moment it was interrupted laid on top (its speed not carried).
    /// </summary>
    static void Differences(CharacterAssets assets, Skeleton skeleton, in PersonJoin j, int n, bool withSpeed, float share, bool add = false)
    {
        var from = assets.Clip(j.From - 1);
        var to = assets.Clip(j.To - 1);
        from.Sample(j.FromTime, skeleton, a0.AsSpan(0, n));
        to.Sample(j.ToTime, skeleton, b0.AsSpan(0, n));
        if (withSpeed)
        {
            from.Sample(MathF.Max(0f, j.FromTime - H * j.FromRate), skeleton, a1.AsSpan(0, n));
            to.Sample(MathF.Max(0f, j.ToTime - H * j.ToRate), skeleton, b1.AsSpan(0, n));
        }
        for (int b = 0; b < n; b++)
        {
            var x = Log(PoseMath.Multiply(a0![b].Rotation, LimbIK.Conjugate(b0![b].Rotation)));
            var p = a0[b].Translation - b0[b].Translation;
            if (add)
            {
                // The interrupted join's offset where it had got to (its speed's part left out).
                turn![b] += Inertial.Offset(x, Vector3.Zero, j.Age, j.Seconds) * share;
                place![b] += Inertial.Offset(p, Vector3.Zero, j.Age, j.Seconds) * share;
                continue;
            }
            turn![b] = x;
            place![b] = p;
            if (withSpeed)
            {
                var wa = Log(PoseMath.Multiply(a0[b].Rotation, LimbIK.Conjugate(a1![b].Rotation))) / H;
                var wb = Log(PoseMath.Multiply(b0[b].Rotation, LimbIK.Conjugate(b1![b].Rotation))) / H;
                turnSpeed![b] = wa - wb;
                placeSpeed![b] = (a0[b].Translation - a1[b].Translation - (b0[b].Translation - b1[b].Translation)) / H;
            }
            else turnSpeed![b] = placeSpeed![b] = Vector3.Zero;
        }
    }

    /// <summary>A unit quaternion's turn as an axis times its angle (radians; the shorter way round).</summary>
    internal static Vector3 Log(Quaternion q)
    {
        if (q.W < 0f) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        float s = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
        if (s < 1e-7f) return new Vector3(q.X, q.Y, q.Z) * 2f;
        float angle = 2f * DetMath.Atan2(s, q.W);
        return new Vector3(q.X, q.Y, q.Z) * (angle / s);
    }

    /// <summary>The unit quaternion of an axis times its angle (<see cref="Log"/>'s inverse).</summary>
    internal static Quaternion Exp(Vector3 r)
    {
        float angle = r.Length();
        if (angle < 1e-7f) return LimbIK.Normalize(new Quaternion(r.X * 0.5f, r.Y * 0.5f, r.Z * 0.5f, 1f));
        var (s, c) = DetMath.SinCos(angle * 0.5f);
        var axis = r / angle;
        return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
    }
}
