// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// How hard a landing was by the body's own safe fall (the movement design, ours):
/// <see cref="Soft"/> within <see cref="Locomotion.SafeFall"/>, <see cref="Hard"/> to twice it, <see cref="Hurt"/>
/// beyond. What a hard or a hurt landing costs is the rules package's; the platform only says which it was.
/// </summary>
public enum LandingClass : byte
{
    /// <summary>A fall within the safe fall (every step, hop and plain jump).</summary>
    Soft = 0,
    /// <summary>A fall beyond the safe fall, to twice it.</summary>
    Hard = 1,
    /// <summary>A fall beyond twice the safe fall.</summary>
    Hurt = 2,
}

/// <summary>Landings told by their height.</summary>
public static class Landings
{
    /// <summary>
    /// The class of a fall of <paramref name="fall"/> metres for a body with <paramref name="loco"/>'s numbers: by its
    /// <see cref="Locomotion.SafeFall"/>, never less than its step (as the edge guard takes it,
    /// <see cref="BodyMotor.GuardLimit"/>).
    /// </summary>
    public static LandingClass Of(float fall, in Locomotion loco)
    {
        float safe = MathF.Max(loco.SafeFall, loco.StepHeight);
        return fall <= safe + 1e-3f ? LandingClass.Soft : fall <= 2f * safe + 1e-3f ? LandingClass.Hard : LandingClass.Hurt;
    }
}

/// <summary>
/// The edge guard stopped a body at an edge (<see cref="BodyMotor"/>'s edge guard): told <b>once a stop</b> — a guard
/// met again within <see cref="BodySystem.EdgeStopSeconds"/> of the last is the same stop, so a body sliding along an
/// edge or standing at it says so once. Where the feet are, the point of the edge, the drop beyond it (+∞ for none
/// within <see cref="BodyMotor.DropLook"/>), which guard (<see cref="EdgeGuard.SafeFall"/>: the player at a walk;
/// <see cref="EdgeGuard.Footing"/>: a body on a route) and its limit (the safe fall, or the step).
/// </summary>
public readonly record struct EdgeStopped(EntityId Entity, long Tick, Vector3 Feet, Vector3 Edge, float Drop, EdgeGuard Guard, float Limit);

/// <summary>
/// A body climbed down a drop: crouch held over an edge whose drop is more than <see cref="BodyMotor.HangMinDrop"/>
/// and less than the hands' reach, lowered and stood at the foot (<see cref="BodyMotor.ClimbsDown"/>). Where it began on
/// the top, the lip it held, where its feet are at the foot, and the drop. Told when it stands there.
/// </summary>
public readonly record struct ClimbedDown(EntityId Entity, long Tick, Vector3 From, Vector3 Lip, Vector3 Feet, float Drop);

/// <summary>
/// A body came down onto a top marked no place to stand (<see cref="MotorOutcome.NoStandMet"/>): where
/// its feet are and the level way it is slid off (<see cref="MotorOutcome.NoStandWay"/>), for the log.
/// </summary>
public readonly record struct NoStandLanded(EntityId Entity, long Tick, Vector3 Feet, Vector2 Way);
