// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>Speed by direction</b> (<i>"Backpedal about 70 % of forward, sidestep about
/// 90 %, as most melee games"</i> — <b>SANDBOX</b>): a body that faces one way and moves another (the player facing the view;
/// a duelling brain circling its foe or backing off) moves at its pace times a factor of the angle θ between the way it moves
/// and the way it faces — <see cref="Forward"/> straight ahead, <see cref="Sideways"/> at a right angle,
/// <see cref="Backwards"/> straight back — blended by cos²θ and sin²θ (ahead: Forward·cos²θ + Sideways·sin²θ; behind:
/// Backwards·cos²θ + Sideways·sin²θ), so a diagonal is between its neighbours and nothing jumps as the keys turn (at 45°
/// 0.95, at 135° 0.80). A body that turns to the way it goes (the townsfolk, the player facing the move) always moves ahead.
/// Applied by <see cref="BodySystem"/> on the ground (not in a move, on a ladder, in the air or in a dodge), to every body
/// alike — in first person too, which faces the view. The sprint stays forward only (<see cref="BodyMotor.SprintCone"/>).
/// A world without it (a test's) moves at its pace every way; <see cref="On"/> false (<c>--no-direction-pace</c>) likewise.
/// </summary>
public sealed record DirectionPace
{
    /// <summary>Whether the factors apply (on by default; <c>--no-direction-pace</c> turns them off for the arena's "unchanged").</summary>
    public bool On { get; init; } = true;

    /// <summary>Straight ahead (1.0: the pace itself).</summary>
    public float Forward { get; init; } = 1.0f;

    /// <summary>At a right angle, a sidestep (0.90).</summary>
    public float Sideways { get; init; } = 0.90f;

    /// <summary>Straight back, a backpedal (0.70).</summary>
    public float Backwards { get; init; } = 0.70f;

    /// <summary>The factor for moving along <paramref name="wish"/> (world X and Y) while facing <paramref name="yaw"/>; 1 when off or still.</summary>
    public float Factor(Vector2 wish, float yaw)
    {
        if (!On) return 1f;
        float l = wish.Length();
        if (l < 1e-4f) return 1f;
        var (s, c) = DetMath.SinCos(yaw);
        return At((wish.X * c + wish.Y * s) / l);
    }

    /// <summary>The factor for moving at an angle whose cosine to the facing is <paramref name="cos"/> (1 ahead, −1 straight back); 1 when off.</summary>
    public float At(float cos)
    {
        if (!On) return 1f;
        cos = Math.Clamp(cos, -1f, 1f);
        float c2 = cos * cos;
        return (cos >= 0f ? Forward : Backwards) * c2 + Sideways * (1f - c2);
    }
}
