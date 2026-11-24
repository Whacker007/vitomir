// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the inertialized join, shared — the walls use it now; a later batch brings it to the other movement states.
using System.Numerics;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>an inertialized join</b> (the project's research: Holden's cubic). Where one path takes over from another, the difference between them at the join
/// <c>x</c> and the difference of their velocities <c>v</c> are decayed to nought over <c>T</c>: the offset at <c>s = t / T</c> is
/// <c>a s³ + b s² + c s + d</c> with <c>d = x</c>, <c>c = v T</c>, <c>b = −3d − 2c</c>, <c>a = 2d + c</c> — that is <c>x·H0(s) + v·T·H1(s)</c> — so position
/// and velocity are whole at the join and the offset is gone, with no velocity left, at its end. A join that interrupts one still decaying starts from the offset
/// and velocity the body has then (the path it is on, less the new one). <see cref="H0"/> is the smoothstep's complement the ledge's and the wall's
/// <c>LedgeMove.Delta</c> has always been taken out by; the walls add the velocity's part (<see cref="H1"/>, <c>BodyMotor.CarryOffset</c>).
/// </summary>
public static class Inertial
{
    /// <summary>The offset's share left at <paramref name="s"/> (0 to 1 of the blend): 1 − smoothstep.</summary>
    public static float H0(float s)
    {
        if (s <= 0f) return 1f;
        if (s >= 1f) return 0f;
        return 1f - s * s * (3f - 2f * s);
    }

    /// <summary>The velocity's part at <paramref name="s"/> (times v·T): s (1 − s)², nought at both ends, its slope 1 at the start.</summary>
    public static float H1(float s) => s <= 0f || s >= 1f ? 0f : s * (1f - s) * (1f - s);

    /// <summary>The offset at <paramref name="t"/> s into a join of <paramref name="blend"/> s that began with offset <paramref name="x"/> and velocity <paramref name="v"/>.</summary>
    public static Vector3 Offset(Vector3 x, Vector3 v, float t, float blend) =>
        blend <= 1e-5f ? (t >= blend ? Vector3.Zero : x) : x * H0(t / blend) + v * (blend * H1(t / blend));
}
