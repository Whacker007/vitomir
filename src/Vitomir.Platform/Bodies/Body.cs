// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// Whether a body stands on something, is in the air, is making a move along a path (<see cref="Traversal"/>:
/// a hop up, a hurdle, a vault, a climb, a mantle, a grab or a pull-up), hangs from a lip, or is on a ladder.
/// Crouching is a flag beside the state (<see cref="Body.Crouched"/>), not a state.
/// </summary>
public enum BodyState
{
    Ground = 0,
    Air = 1,
    /// <summary>Making a move: the feet follow the move's path (<see cref="Traversal"/>), not the wish.</summary>
    Traversing = 2,
    /// <summary>Hanging from a lip by the hands.</summary>
    Hanging = 3,
    /// <summary>
    /// On a ladder (on a ladder): held to its line, climbing up and down it
    /// (BodyMotor.Ladder.cs); <see cref="Body.Ladder"/> says which, the <see cref="Traversal"/> (kind
    /// <see cref="TraversalKind.Ladder"/>) where along it.
    /// </summary>
    Ladder = 4,
}

/// <summary>
/// How fast a body wants to go: the three paces, and the body's
/// <b>supernatural speed</b> — its own resting speed (<see cref="Locomotion.RestingSpeed"/>), a sprint double-tapped:
/// a sprint in all else (the cone, the sprint vault), never slower than the sprint.
/// </summary>
public enum Gait
{
    Walk = 0,
    Run = 1,
    Sprint = 2,
    Supernatural = 3,
}

/// <summary>
/// A moving body (ours): <b>a kinematic capsule</b> moved by
/// <see cref="BodyMotor"/>, never pushed by a solver. Where its feet are (the capsule's lowest point),
/// how fast it goes, which way it faces, whether it stands or flies, and the little it must remember from
/// tick to tick — the ground's slope, how long it has been on the ground or in the air, the grace timers
/// of the jump, and the last landing. Plain four-byte fields only, so the world digests it byte for byte.
/// </summary>
public struct Body
{
    /// <summary>The capsule's lowest point, metres.</summary>
    public Vector3 Feet;
    /// <summary>Metres a second.</summary>
    public Vector3 Velocity;
    /// <summary>Which way the body faces (radians, counter-clockwise from +X seen from above).</summary>
    public float Yaw;
    public BodyState State;
    /// <summary>The unit normal of the ground stood on (up while in the air).</summary>
    public Vector3 GroundNormal;
    /// <summary>The collision triangle stood on, or −1.</summary>
    public int GroundTriangle;
    /// <summary>Seconds on the ground since the last landing, and in the air since leaving it.</summary>
    public float GroundSeconds, AirSeconds;
    /// <summary>Seconds left in which a jump still takes off after walking off an edge (the movement design's grace).</summary>
    public float Coyote;
    /// <summary>Seconds left in which a jump pressed in the air takes off on landing.</summary>
    public float JumpBuffer;
    /// <summary>The last landing: how fast the body came down (m/s, positive) and from how high above where it landed (m).</summary>
    public float LastLandingSpeed, LastFallHeight;
    /// <summary>The highest the feet have been since leaving the ground, and where they left it.</summary>
    public float HighestZ, TakeOffZ;
    /// <summary>1 while a jump is rising (its apex not yet reported), else 0.</summary>
    public int Rising;
    /// <summary>
    /// 1 while the body crouches: the capsule lowered to <see cref="Locomotion.CrouchHeight"/> (crouch held, or
    /// kept down under something too low to stand under), else 0.
    /// </summary>
    public int Crouched;
    /// <summary>1 while the body is in the air from a jump it took (not a walk off an edge), else 0.</summary>
    public int Jumped;
    /// <summary>
    /// How long jump has been down in its present press, or was down in its last one (s): a press this long
    /// is a hold, not a tap (<see cref="BodyMotor.HoldSeconds"/>).
    /// </summary>
    public float JumpHeldSeconds;
    /// <summary>
    /// Seconds left in which jump still counts as held: <see cref="BodyMotor.HeldGrace"/> while it is down,
    /// running out after it is let go (the movement design's grace for "held", 3.10).
    /// </summary>
    public float JumpGrace;
    /// <summary>1 when crouch was held the tick before (a press is told from a hold while hanging).</summary>
    public int CrouchWas;
    /// <summary>1 once the wish has been away from forward since the body began to hang (forward then pulls up).</summary>
    public int ForwardArmed;
    /// <summary>
    /// Seconds left of the stop at an edge the guard last held the body at (<see cref="BodySystem"/>): a guard
    /// met while it runs is the same stop, so <see cref="EdgeStopped"/> is told once a stop, not every tick.
    /// </summary>
    public float EdgeHold;
    /// <summary>
    /// How far the head is kept down (m, to <see cref="Locomotion.StepHeight"/>): standing level with an edge
    /// whose ground under its middle is lower — a tread it has stepped onto while its middle is still over the one
    /// below, or the nosing it is walking off — the body's head is its height above the ground under its middle,
    /// not above its feet, as a person's with a foot on each tread. So a body of 1.8 m walks up and down stairs
    /// under a slab its height and a little more above the nosings, and its head never comes lower than its height
    /// above the ground under its middle. It rises again only where the taller capsule fits; on the ground a move
    /// that would raise it where it does not fit is not taken.
    /// </summary>
    public float Stoop;
    /// <summary>
    /// 1 while jump, pressed on the ground by a body that can charge (<see cref="Locomotion.CanCharge"/>), is still
    /// held: a tap until <see cref="BodyMotor.HoldSeconds"/>, a charge after it (the body crouches and
    /// goes at most at a walk). Let go, it jumps; cancelled, or the ground left, nothing jumps. Else 0.
    /// </summary>
    public int Charging;
    /// <summary>
    /// 1 in the air from a charged jump (the powered leap) and through the move it starts: the moves then reach,
    /// whatever the mode, towards the body's own numbers (<see cref="Locomotion.PoweredReach"/>) as far as the charge
    /// went (<see cref="BodyMotor.MovesReach"/>). Else 0.
    /// </summary>
    public int Powered;
    /// <summary>How high the jump the body is in the air from lifts the feet (m): a tap's, or a charge's.</summary>
    public float JumpRise;
    /// <summary>1 once a move refused at a face marked not to be climbed has been told since the body last stood, so it is told once.</summary>
    public int RefusalTold;
    /// <summary>
    /// Seconds left in which a body coming down onto a no-stand top is the same landing (<see cref="BodySystem"/>):
    /// held while it is on the top and <see cref="BodySystem.NoStandSeconds"/> after, so <see cref="NoStandLanded"/> is
    /// told once a landing, not on each tick the motor meets the top coming down.
    /// </summary>
    public float NoStandHold;
    /// <summary>
    /// Seconds left of the stop the player was last told stopped at (<see cref="BodySystem"/>): a stop met again
    /// while it runs is the same one, so <see cref="PlayerStopped"/> is told once a stop, as <see cref="EdgeStopped"/> is.
    /// </summary>
    public float StopHold;
    /// <summary>
    /// The ladder the body is getting onto or is on, as its index in the level's list plus one
    /// (<see cref="Space.LevelLadders"/>); 0 for none.
    /// </summary>
    public int Ladder;
    /// <summary>
    /// Hanging, which way the body shimmied along the lip last tick: +1 to its right as it faces the wall, −1 to
    /// its left, 0 still — so the shimmy's start and stop are told once each.
    /// </summary>
    public int Shimmy;

    /// <summary>The capsule's height now: tucked while making a move; else crouched or standing, less the <see cref="Stoop"/>.</summary>
    public readonly float CapsuleHeight(in Locomotion loco) =>
        State == BodyState.Traversing ? TuckedHeight(loco) : MathF.Max(FullHeight(loco) - Stoop, 2f * loco.Radius);

    /// <summary>The capsule's height before any <see cref="Stoop"/>: crouched while crouching, else standing.</summary>
    public readonly float FullHeight(in Locomotion loco) => Crouched != 0 ? TuckedHeight(loco) : loco.Height;

    static float TuckedHeight(in Locomotion loco) => MathF.Max(MathF.Min(loco.CrouchHeight, loco.Height), 2f * loco.Radius);

    /// <summary>A body standing at <paramref name="feet"/> facing <paramref name="yaw"/>, not yet settled: it starts in the air and finds the ground on its first tick.</summary>
    public static Body At(Vector3 feet, float yaw) => new()
    {
        Feet = feet, Yaw = yaw, State = BodyState.Air, GroundNormal = Vector3.UnitZ, GroundTriangle = -1,
        HighestZ = feet.Z, TakeOffZ = feet.Z,
    };
}

/// <summary>
/// A body's numbers, in metres, seconds and kilograms: its size, its paces,
/// how quickly it gets up to speed and stops, its jump, its control in the air, its grace times and its
/// gravity. Where a number is one of the rules package's movement capabilities (its movement and physics
/// boundary: <c>JumpImpulseBudget</c>, <c>AccelerationCapacity</c>, <c>SprintEndurance</c>, <c>ClimbGrip</c>,
/// <c>GripBreakStrength</c>, <c>FallTolerance</c>) it carries that name, in the unit the engine chose; a
/// module fills them per character. Until the rules package sends curves every value is the platform's
/// <b>SANDBOX</b> default (<see cref="Sandbox"/>, the one place they are set).
/// </summary>
public struct Locomotion
{
    /// <summary>The capsule's radius and full height, and the eyes' height above the feet (m).</summary>
    public float Radius, Height, EyeHeight;
    /// <summary>The tallest step taken in stride, up or down (m).</summary>
    public float StepHeight;
    /// <summary>
    /// The steepest ground walked up (degrees from level). Ground between it and <see cref="SlideDegrees"/> is
    /// stood on without sliding and walked down or across, but not up: its uphill part is taken away as a
    /// wall's is, and no step is taken onto it.
    /// </summary>
    public float SteepestGroundDegrees;
    /// <summary>The steepest ground stood on at all (degrees from level); steeper is slid down, in the air.</summary>
    public float SlideDegrees;
    /// <summary>The paces (m/s).</summary>
    public float WalkSpeed, RunSpeed, SprintSpeed;
    /// <summary>
    /// The rules' <c>AccelerationCapacity</c>: how fast the body gains speed on the ground (m/s²). The sandbox's
    /// is the run's speed in 0.15 s.
    /// </summary>
    public float AccelerationCapacity;
    /// <summary>How fast the body loses speed on the ground with nothing wished (m/s²): the run stopped in 0.1 s.</summary>
    public float BrakingDeceleration;
    /// <summary>
    /// The rules' <c>JumpImpulseBudget</c>: the upward speed a jump starts with (m/s, the impulse per kilogram).
    /// The sandbox's is a human's hop: the feet rise 0.5 m, √(2gh).
    /// </summary>
    public float JumpImpulseBudget;
    /// <summary>The share of the ground's acceleration the body has in the air (0 to 1).</summary>
    public float AirControl;
    /// <summary>A jump still takes off this long after leaving an edge, and one pressed this long before landing jumps on landing (s).</summary>
    public float CoyoteSeconds, JumpBufferSeconds;
    /// <summary>Metres a second squared, downwards.</summary>
    public float Gravity;
    /// <summary>
    /// The rules' <c>SprintEndurance</c>: how long a sprint can be held (s). Sprinting does not tire yet;
    /// the sandbox's is unlimited.
    /// </summary>
    public float SprintEndurance;
    /// <summary>Crouched: the capsule's height and the pace (m, m/s).</summary>
    public float CrouchHeight, CrouchSpeed;
    /// <summary>How high above the feet the hands reach standing (m): a lip met there in the air is grabbed.</summary>
    public float StandingReach;
    /// <summary>
    /// The deliberate moves' bands (m above the ground jumped from): a deep top from <see cref="VaultMin"/> to
    /// <see cref="VaultMax"/> is climbed onto, a thin one above the stride to <see cref="VaultMax"/> vaulted, a
    /// top that can be stood on above <see cref="VaultMax"/> to <see cref="MantleMax"/> mantled onto.
    /// </summary>
    public float VaultMin, VaultMax, MantleMax;
    /// <summary>The least depth of a top that can be stood on (m along the way): thinner is thin — hurdled at a sprint or vaulted over, never stood on or climbed onto (one threshold).</summary>
    public float MantleMinDepth;
    /// <summary>How long a lip can be held before the hands let go (s; infinite: as long as the player likes).</summary>
    public float GripSeconds;
    /// <summary>The moves' lengths (s): short and readable.</summary>
    public float HopUpSeconds, HurdleSeconds, VaultSeconds, ClimbOntoSeconds, MantleSeconds, GrabSeconds, PullUpSeconds;
    /// <summary>The rules' <c>ClimbGrip</c> and <c>GripBreakStrength</c> (ladders and walls, later; not used yet).</summary>
    public float ClimbGrip, GripBreakStrength;
    /// <summary>The rules' <c>FallTolerance</c>: the impact speed taken without harm (m/s; reported, judged by the rules).</summary>
    public float FallTolerance;
    /// <summary>
    /// The highest drop the body walks off by itself (m): <b>at a walk</b> it stops at an
    /// edge whose drop — from the ground under its middle to the ground beyond — is more than this; running,
    /// sprinting or jumping go over.: a multiplier per kind of body times the highest it
    /// gets up by itself (the larger of its jump's height and <see cref="MantleMax"/>) — the human 1.5 × 1.9 = 2.85,
    /// the neonate 2.5 × 2.4 = 6.0, the elder 4 × 4.0 = 16.0 — written in each profile as the one number, not worked
    /// out. The masquerade holds a body to the human's (the lesser).
    /// </summary>
    public float SafeFall;
    /// <summary>
    /// How fast the body turns to the way it is going when nothing sets its facing (degrees a second; 0 turns at
    /// once): the townsfolk on their routes, and the player in third person facing the move. Ours.
    /// </summary>
    public float TurnDegreesPerSecond;
    /// <summary>
    /// How long a held jump takes to charge from the tap's height to the body's own jump (s; "about a
    /// second"), counted from <see cref="BodyMotor.HoldSeconds"/>; linear. A profile's number; the masquerade keeps it.
    /// </summary>
    public float ChargeSeconds;
    /// <summary>
    /// The upward speed of a <b>tapped</b> jump (m/s; "a human-sized jump for every body, everywhere"):
    /// worked out, never given — a human's (the human profile's jump, set by <see cref="Modules.MovementProfiles"/>),
    /// never more than the body's own (<see cref="TapImpulse"/>). Numbers made without profiles have their own jump's.
    /// </summary>
    public float TapJumpImpulse;
    /// <summary>
    /// The body's own jump and reach, which a full charge and the moves from it use whatever the mode (the
    /// powered leap works anywhere): worked out from the body's own numbers, never given, and never lowered
    /// by the masquerade (<see cref="Control.Masquerade.HeldToHuman"/> keeps every number named <c>Powered…</c>).
    /// </summary>
    public float PoweredJumpImpulse, PoweredStandingReach, PoweredVaultMax, PoweredMantleMax;

    /// <summary>The numbers worked out from the others, never given in a profile's file.</summary>
    public static readonly string[] WorkedOut =
        [nameof(TapJumpImpulse), nameof(PoweredJumpImpulse), nameof(PoweredStandingReach), nameof(PoweredVaultMax), nameof(PoweredMantleMax)];

    /// <summary>
    /// These numbers with the worked-out ones filled from the body's own: the powered jump and reach at least
    /// the body's own jump and reach, the tap its own jump when it has none (or more). Configuration, not a tick.
    /// </summary>
    public readonly Locomotion WithOwnPowers()
    {
        var l = this;
        l.PoweredJumpImpulse = MathF.Max(l.PoweredJumpImpulse, l.JumpImpulseBudget);
        l.PoweredStandingReach = MathF.Max(l.PoweredStandingReach, l.StandingReach);
        l.PoweredVaultMax = MathF.Max(l.PoweredVaultMax, l.VaultMax);
        l.PoweredMantleMax = MathF.Max(l.PoweredMantleMax, l.MantleMax);
        if (l.TapJumpImpulse <= 0f || l.TapJumpImpulse > l.JumpImpulseBudget) l.TapJumpImpulse = l.JumpImpulseBudget;
        return l;
    }

    /// <summary>The upward speed of a tap (m/s): <see cref="TapJumpImpulse"/>, no more than the body's jump now (its own when unset).</summary>
    public readonly float TapImpulse => TapJumpImpulse > 0f ? MathF.Min(TapJumpImpulse, JumpImpulseBudget) : JumpImpulseBudget;

    /// <summary>The upward speed of a full charge (m/s): the body's own jump, whatever the mode.</summary>
    public readonly float PoweredImpulse => MathF.Max(PoweredJumpImpulse, JumpImpulseBudget);

    /// <summary>How high a tap lifts the feet (m): v²/2g.</summary>
    public readonly float TapHeight => HeightOf(TapImpulse);

    /// <summary>How high a full charge lifts the feet (m): the body's own jump.</summary>
    public readonly float PoweredHeight => HeightOf(PoweredImpulse);

    /// <summary>Whether a held jump charges: the body's own jump is higher than a tap's (the human has nothing to charge).</summary>
    public readonly bool CanCharge => PoweredImpulse > TapImpulse + 1e-3f && Gravity > 0f;

    /// <summary>
    /// The numbers the moves from a charged jump reach with: these with the reach, the vault and the mantle
    /// raised to the body's own (<see cref="PoweredStandingReach"/>, <see cref="PoweredVaultMax"/>,
    /// <see cref="PoweredMantleMax"/>).
    /// </summary>
    public readonly Locomotion PoweredReach()
    {
        var l = this;
        l.StandingReach = MathF.Max(l.StandingReach, l.PoweredStandingReach);
        l.VaultMax = MathF.Max(l.VaultMax, l.PoweredVaultMax);
        l.MantleMax = MathF.Max(l.MantleMax, l.PoweredMantleMax);
        return l;
    }

    readonly float HeightOf(float impulse) => Gravity > 0f ? impulse * impulse / (2f * Gravity) : 0f;
    /// <summary>
    /// The run's step-over: the highest thin thing (shallower than <see cref="MantleMinDepth"/>) a body
    /// moving at a run or faster steps over with a higher step, never onto (m above the feet). The value can be tuned
    /// later; the sandbox's 0.5 for every body.
    /// </summary>
    public float StepOverMax;
    /// <summary>
    /// The sprint vault (it replaces the first hurdle): a sprint vaults over a thing to this high (m above
    /// the feet) and <see cref="SprintVaultDepth"/> deep, thin or not — a low fence, a bench, a trough — with ground
    /// beyond at about the feet's height; never onto. The sandbox's 1.0 by 0.7 for every body.
    /// </summary>
    public float SprintVaultMax, SprintVaultDepth;
    /// <summary>
    /// The body's resting supernatural speed (m/s): the pace of a sprint double-tapped, <i>"not quite
    /// celerity, but it lets you move at whatever your innate resting supernatural speed is"</i>, anywhere — the
    /// masquerade does not lower it (in public it is a risk). 0: none (the human), a plain sprint.
    /// </summary>
    public float RestingSpeed;
    /// <summary>
    /// On a ladder (ours, 1.1 has none): how fast the body climbs up and down it (m/s along the ladder), and how
    /// fast it shimmies along a lip it hangs from (m/s). The sandbox's human: 1.0 up, 1.3 down, 0.6 along a lip. In the
    /// masquerade they are held to the human's, as every number.
    /// </summary>
    public float ClimbUpSpeed, ClimbDownSpeed, ShimmySpeed;

    /// <summary>The upward speed that lifts the feet <paramref name="height"/> under <paramref name="gravity"/>: √(2gh).</summary>
    public static float ImpulseFor(float height, float gravity) => MathF.Sqrt(2f * gravity * MathF.Max(0f, height));

    /// <summary>The least upward part of a ground's normal that can be walked up (the cosine of <see cref="SteepestGroundDegrees"/>).</summary>
    public readonly float MinGroundNormalZ => DetMath.Cos(SteepestGroundDegrees * DetMath.DegToRad);

    /// <summary>The least upward part of a ground's normal that can be stood on (the cosine of <see cref="SlideDegrees"/>, never less steep than the walked).</summary>
    public readonly float MinStandNormalZ => DetMath.Cos(MathF.Max(SlideDegrees, SteepestGroundDegrees) * DetMath.DegToRad);

    /// <summary>How high a jump lifts the feet: v²/2g.</summary>
    public readonly float JumpHeight => Gravity > 0f ? JumpImpulseBudget * JumpImpulseBudget / (2f * Gravity) : 0f;

    /// <summary>The speed of a gait (supernatural speed: the resting speed, never less than the sprint).</summary>
    public readonly float SpeedOf(Gait gait) => gait switch
    {
        Gait.Walk => WalkSpeed, Gait.Sprint => SprintSpeed, Gait.Supernatural => MathF.Max(SprintSpeed, RestingSpeed), _ => RunSpeed,
    };

    /// <summary>
    /// <b>SANDBOX</b>: the human's numbers, all tunable, until
    /// The rules package's curves replace them — radius 0.3, height 1.8, eyes 1.65, a stride of 0.35, ground
    /// walked to 40° and slid on above 45°, walk 1.3, run 3.0, sprint 5.5, full speed in 0.15 s, stopping in
    /// 0.1 s, a jump lifting the feet 0.5 m, 30% control in the air, 0.1 s of grace either side of the
    /// ground, gravity 9.81; crouched 1.15 m at 1.0 m/s; the hands reaching 2.3 m; vaults
    /// and climbs 0.6–1.2 m, mantles to 1.9 m onto a top at least 0.3 m deep; a grip without limit; a hop up
    /// in 0.35 s, a sprint vault 0.4, a vault 0.6, a climb onto 0.7, a mantle 0.9, a grab 0.2, a pull-up 0.8;
    /// a walk stopping at drops of more than 2.85 m; turning to the way it goes at 720° a second; a held jump
    /// charging over 1 s, with nothing to charge (the tap is its own jump; the powered numbers its own); a run's
    /// step-over to 0.5 m and a sprint's vault to 1.0 m by 0.7 m, and no supernatural speed; ladders climbed at 1.0 m/s
    /// up and 1.3 down, a lip shimmied along at 0.6.
    /// </summary>
    public static Locomotion Sandbox => new()
    {
        Radius = 0.3f, Height = 1.8f, EyeHeight = 1.65f,
        StepHeight = 0.35f, SteepestGroundDegrees = 40f, SlideDegrees = 45f,
        WalkSpeed = 1.3f, RunSpeed = 3.0f, SprintSpeed = 5.5f,
        AccelerationCapacity = 3.0f / 0.15f, BrakingDeceleration = 3.0f / 0.1f,
        JumpImpulseBudget = ImpulseFor(0.5f, 9.81f),
        AirControl = 0.3f,
        CoyoteSeconds = 0.1f, JumpBufferSeconds = 0.1f,
        Gravity = 9.81f,
        SprintEndurance = float.PositiveInfinity,
        CrouchHeight = 1.15f, CrouchSpeed = 1.0f,
        StandingReach = 2.3f, VaultMin = 0.6f, VaultMax = 1.2f, MantleMax = 1.9f,
        MantleMinDepth = 0.35f, GripSeconds = float.PositiveInfinity,
        HopUpSeconds = 0.35f, HurdleSeconds = 0.4f, VaultSeconds = 0.6f, ClimbOntoSeconds = 0.7f, MantleSeconds = 0.9f,
        GrabSeconds = 0.2f, PullUpSeconds = 0.8f,
        ClimbGrip = 0f, GripBreakStrength = 0f,
        FallTolerance = 8.0f,
        SafeFall = 2.85f,
        TurnDegreesPerSecond = 720f,
        ChargeSeconds = 1.0f,
        TapJumpImpulse = ImpulseFor(0.5f, 9.81f), PoweredJumpImpulse = ImpulseFor(0.5f, 9.81f),
        PoweredStandingReach = 2.3f, PoweredVaultMax = 1.2f, PoweredMantleMax = 1.9f,
        StepOverMax = 0.5f, SprintVaultMax = 1.0f, SprintVaultDepth = 0.7f, RestingSpeed = 0f,
        ClimbUpSpeed = 1.0f, ClimbDownSpeed = 1.3f, ShimmySpeed = 0.6f,
    };
}

/// <summary>
/// What a body wants this tick (made by the player's control or by a route): the way to go in the world's
/// plane (length up to 1: a share of the gait's speed), the gait, whether jump was pressed this tick and
/// whether it is down, whether crouch is held, and which way to face.
/// </summary>
public struct MoveIntent
{
    /// <summary>The wished direction in world X and Y, length 0 to 1.</summary>
    public Vector2 Direction;
    public Gait Gait;
    /// <summary>Which way the body should face (radians).</summary>
    public float Facing;
    /// <summary>Jump pressed this tick (not held).</summary>
    public bool Jump;
    /// <summary>Jump down this tick (pressed or held): held past <see cref="BodyMotor.HoldSeconds"/> by a body that can charge, it charges. A press counts as down.</summary>
    public bool JumpHeld;
    /// <summary>Crouch held: the body crouches (and lets go of a lip it hangs from).</summary>
    public bool Crouch;
    /// <summary>Whether <see cref="Facing"/> is set (the body faces it at once); when not, the body turns to the way it is going at <see cref="Locomotion.TurnDegreesPerSecond"/>.</summary>
    public bool Faces;
    /// <summary>
    /// Cancel pressed this tick and something to cancel (<see cref="BodyMotor.CanCancel"/>): the hands let go
    /// of a lip, or a move in its first <see cref="BodyMotor.CancelSeconds"/> is undone, the body put back where it
    /// started, standing.
    /// </summary>
    public bool Cancel;
    /// <summary>The edge guard asked for: which drops the body is kept from moving over on the ground.</summary>
    public EdgeGuard Guard;
    /// <summary>With <see cref="EdgeGuard.Footing"/>: the route's next corner (a step a little over the body's is taken only where the route goes down it, <see cref="BodyMotor.StepsDown"/>).</summary>
    public Vector3 GuardCorner;
    /// <summary>
    /// Use pressed this tick (Space by default): at a ladder's foot or top it puts the body on the ladder
    /// (BodyMotor.Ladder.cs). Route-followers never set it, so they never use ladders.
    /// </summary>
    public bool Use;
    /// <summary>
    /// Use held this tick — set only with the commit point in the air on (the player's control; a world without it leaves it false,
    /// so the intent's bytes are the earlier build's): at the commit a lip coming into range is caught (a jump taken at the wall catches it by itself).
    /// </summary>
    public bool UseHeld;
}

/// <summary>
/// The edge guard an intent asks the motor for (ours): on the ground the body is kept from moving its middle
/// over a drop — measured from the ground under its middle to the ground beyond the edge, or none within
/// <see cref="BodyMotor.DropLook"/> — higher than the guard's limit; it slides along the edge instead, as along a
/// wall, or stops where it cannot.
/// </summary>
public enum EdgeGuard : byte
{
    /// <summary>No guard: the body walks off any edge.</summary>
    None = 0,
    /// <summary>The player at a walk: drops higher than <see cref="Locomotion.SafeFall"/>.</summary>
    SafeFall = 1,
    /// <summary>
    /// A body following a route (the levels' report): drops higher than its step
    /// (<see cref="Locomotion.StepHeight"/>), or than <see cref="BodyMotor.StepDownSlack"/> more where its route goes
    /// down there (<see cref="MoveIntent.GuardCorner"/>, <see cref="BodyMotor.StepsDown"/>); a step it takes is stepped
    /// down onto, never flown over, and it never falls.
    /// </summary>
    Footing = 2,
}

/// <summary>Another body as a mover meets it: a vertical capsule standing at its feet.</summary>
public readonly record struct BodyCapsule(ulong Entity, Vector3 Feet, float Radius, float Height);

// ---- events, for the rules package's traces ("each attempted jump … each landing or fall") ----

/// <summary>Jump was pressed (taken off at once, buffered for a landing, or refused in the air).</summary>
public readonly record struct JumpAttempted(EntityId Entity, long Tick, Vector3 Feet, float Yaw, bool OnGround);

/// <summary>A jump took off: where, with what upward speed, and whether by the grace after an edge or a press buffered before landing.</summary>
public readonly record struct JumpTookOff(EntityId Entity, long Tick, Vector3 Feet, Vector3 Velocity, bool Coyote, bool Buffered);

/// <summary>A jump's highest point: where, and how high above the take-off the feet rose (the measured height).</summary>
public readonly record struct JumpApex(EntityId Entity, long Tick, Vector3 Feet, float Height);

/// <summary>
/// A landing: where, how fast the body came down (m/s, positive), how far it fell from the highest point
/// since leaving the ground (m), how long it was in the air, what it landed on (the triangle, its
/// material and flags), and how hard it was by the body's safe fall (<see cref="LandingClass"/>).
/// </summary>
public readonly record struct Landed(EntityId Entity, long Tick, Vector3 Feet, float ImpactSpeed, float FallHeight, float AirSeconds,
                                     int Triangle, ushort Material, Vitomir.Formats.L2.L2Surface Surface, LandingClass Class = LandingClass.Soft);

/// <summary>A move began: which, where the feet's path starts and ends, the lip (zero when none) and the obstacle's top above the start.</summary>
public readonly record struct TraversalStarted(EntityId Entity, long Tick, TraversalKind Kind, Vector3 From, Vector3 To, Vector3 Lip, float Height);

/// <summary>A move ended: which, where the feet are, and whether it ran to its end (false: something stopped it on the way and the body fell).</summary>
public readonly record struct TraversalEnded(EntityId Entity, long Tick, TraversalKind Kind, Vector3 Feet, bool Completed);

/// <summary>The hands took a lip in the air: where, and how high it is above the ground the body left.</summary>
public readonly record struct LedgeGrabbed(EntityId Entity, long Tick, Vector3 Lip, Vector3 Feet, float Height);

/// <summary>The hands let go of a lip (crouch, or the grip ran out): where the feet were.</summary>
public readonly record struct LedgeDropped(EntityId Entity, long Tick, Vector3 Lip, Vector3 Feet);
