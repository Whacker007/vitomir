// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>How a jump was taken: tapped (a human-sized jump) or charged (the powered leap).</summary>
public enum JumpKind : byte
{
    Tap = 0,
    Charged = 1,
}

/// <summary>A step of a charge: begun (jump held past <see cref="BodyMotor.HoldSeconds"/>), let go (it jumps), cancelled, or lost (the ground left past the grace after an edge; nothing jumps).</summary>
public enum ChargeStep : byte
{
    Began = 0,
    LetGo = 1,
    Cancelled = 2,
    Lost = 3,
}

/// <summary>
/// A jump took off (beside <see cref="JumpTookOff"/>): tapped or charged, how high it will lift the feet (m,
/// v²/2g of its upward speed), and how long it was charged (s past the hold; 0 for a tap).
/// </summary>
public readonly record struct JumpLaunched(EntityId Entity, long Tick, Vector3 Feet, JumpKind Kind, float Height, float ChargedSeconds);

/// <summary>
/// A charge: its step, where the feet are, the height it would jump now (begun: the tap's; let go: the jump's;
/// cancelled or lost: what it had reached), and the most it reaches (the body's own jump, m).
/// </summary>
public readonly record struct JumpCharge(EntityId Entity, long Tick, Vector3 Feet, ChargeStep Step, float Height, float Most);

/// <summary>
/// A move the body would have made was refused at a face marked not to be climbed: which move (by the
/// marked top's height; one in the vault's band is told as a climb onto, since a marked top's depth is not measured),
/// the lip, the feet, and the top's height above the ground left. Told once per press on the ground, once per flight
/// in the air.
/// </summary>
public readonly record struct MoveRefused(EntityId Entity, long Tick, TraversalKind Kind, Vector3 Lip, Vector3 Feet, float Height);

/// <summary>The jump's events: registered with the movement's, published by <see cref="BodySystem"/> from each body's <see cref="MotorOutcome"/>.</summary>
public static class JumpEvents
{
    /// <summary>Registers <see cref="JumpLaunched"/>, <see cref="JumpCharge"/> and <see cref="MoveRefused"/>.</summary>
    public static WorldBuilder Register(WorldBuilder b) => b.Event<JumpLaunched>().Event<JumpCharge>().Event<MoveRefused>();

    /// <summary>The jump's events of one body's tick, from its outcome (nothing is looked up when there are none).</summary>
    public static void Publish(World world, EntityId e, in Body body, in MotorOutcome o)
    {
        if (o.Launched) world.Events<JumpLaunched>().Publish(new JumpLaunched(e, world.Tick, o.TakeOffFeet, o.LaunchKind, o.LaunchHeight, o.ChargedSeconds));
        if (o.Charge) world.Events<JumpCharge>().Publish(new JumpCharge(e, world.Tick, body.Feet, o.ChargeStep, o.ChargeHeight, o.ChargeMost));
        if (o.Refused != TraversalKind.None) world.Events<MoveRefused>().Publish(new MoveRefused(e, world.Tick, o.Refused, o.RefusedLip, body.Feet, o.RefusedHeight));
    }
}
