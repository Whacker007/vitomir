// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>What stopped the player, as one read of the probe tells it (ours, <see cref="PlayerStopped"/>).</summary>
public enum StopCause : byte
{
    /// <summary>Nothing the probe reads: another body, a ceiling too low, a slope too steep under the stride.</summary>
    Unread = 0,
    /// <summary>
    /// A thing above the stride with a top that no move takes at the body's pace now (<see cref="PlayerStopped.Move"/>
    /// and <see cref="PlayerStopped.By"/> say which move would, if any).
    /// </summary>
    TooHigh = 1,
    /// <summary>A face going on above anything a move reaches (no top within the look): a wall.</summary>
    Wall = 2,
    /// <summary>A face or top the level marks not to be climbed (<see cref="Obstacle.NoClimb"/>).</summary>
    NoClimb = 3,
    /// <summary>
    /// A top the level marks no place to stand (<see cref="Obstacle.NoStand"/>): level with the floor or within the
    /// stride, walking stops at its edge (the motor takes the step back); above the stride, no move ends on it.
    /// </summary>
    NoStand = 4,
}

/// <summary>How the move that would take what stopped the player is made (ours, <see cref="PlayerStopped.By"/>).</summary>
public enum StopTaker : byte
{
    /// <summary>No move takes it.</summary>
    None = 0,
    /// <summary>By itself at a run: the run's step-over (<see cref="TraversalKind.StepOver"/>).</summary>
    Run = 1,
    /// <summary>By itself at a sprint: the sprint vault (<see cref="TraversalKind.Hurdle"/>).</summary>
    Sprint = 2,
    /// <summary>A tap of jump: a hop up, or in the air a vault, a climb onto, a mantle or a grab.</summary>
    Tap = 3,
    /// <summary>A charged jump, as far as the body's own reach (a body that can charge).</summary>
    Charge = 4,
}

/// <summary>
/// The player wished to move into something and did not move on (ours, for the log): told <b>once a stop</b> — a
/// stop met again within <see cref="BodySystem.StopSeconds"/> of the last is the same one (<see cref="Body.StopHold"/>), as
/// <see cref="EdgeStopped"/> is —, and only for the body the player controls, read once with the probe along the wish
/// (<see cref="BodyMotor.ReadStop"/>). Where the feet are, the level way wished, the gait, what it was, how high its top
/// is above the ground the feet stand on and how deep it is square to its face (m; 0 when not read), and the move that
/// would take it and how (<see cref="TraversalKind.None"/> and <see cref="StopTaker.None"/> for none): "a 0.9 m thing
/// 0.4 m deep: a tap climbs onto it", "a 0.6 m rail: a sprint vaults it". An edge the guard stops the body at is
/// <see cref="EdgeStopped"/>, not this.
/// </summary>
public readonly record struct PlayerStopped(EntityId Entity, long Tick, Vector3 Feet, Vector2 Way, Gait Gait, StopCause Cause,
                                            float Height, float Depth, TraversalKind Move, StopTaker By);
