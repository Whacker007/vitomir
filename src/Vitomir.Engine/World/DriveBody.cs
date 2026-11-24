// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **The `bodycollision` Fix's body, standing on the steps** — ours, as the Fix and driving are.
/// The Fix makes a driven character fit its whole body against
/// the level: the centre and a ring of eight points at its radius, each at knee, waist and head
/// height (<see cref="Collision.Body"/>). Measured from the feet, the ring's knee is **inside the
/// next tread** wherever a step rises more than knee height within the radius — and on the way
/// down, inside the tread just left — so the Fix refused every staircase and every rough step in the
/// game — 42.5% of the walkable ground, measured. A play-test had it on, and
/// that was most of its troubles with driving mode over some short steps
/// and other areas: Christof stopped at the lips of the Silver Mines' steps, a
/// university stair and an Old Town flight, and was clicked past.
///
/// So each point of the body — the centre and the ring — stands on **the ground under it**: where
/// 1.1's own ground ray (<see cref="WorldSweep.Ground"/>) cast from 50 units over that
/// point meets walk-flagged ground, when that is higher than the feet — the ray a click
/// walk's step lands by (<see cref="WalkMap.Stride"/>), so no higher than a click walk climbs. A
/// ring point over the next tread is tested above that tread, and the centre over a sloping riser
/// above the riser; one over nothing, or over ground more than a step up, is tested from the feet as
/// before. A wall has no ground in it, so the margin the Fix is for — the shoulder kept off a wall's
/// corner — is kept; a step has, so it is climbed.
///
/// And **only the half of the ring ahead of the step counts**, as only a thing ahead of the move
/// stops 1.1's walker (<c>Player.Ahead</c>). Tested all round, a body that did not fit
/// where it stood could not move at all, not even away from what it was touching — Christof on a
/// flight of steps in the Old Town, a corner 28 units off his shoulder at head height, could go
/// neither up nor down — and a passage narrower than the body was closed to it however it was
/// entered. A wall or a corner in front still stops the
/// step before the shoulder reaches it. <see cref="Collision.Fits"/> itself, the plain all-round
/// test the measurements use, is unchanged.
/// </summary>
public static class DriveBody
{
    /// <summary>
    /// Whether a driven character stepping from <paramref name="from"/> to <paramref name="feet"/>
    /// fits there: its centre, and the points of its ring **ahead of the step**, each on the
    /// ground under it (see the class summary). <paramref name="hint"/> is the sector to start the
    /// collision trees' search from.
    /// </summary>
    public static bool Fits(Collision solid, Vector3 from, Vector3 feet, float radius, int hint = -1)
    {
        var step = new Vector2(feet.X - from.X, feet.Y - from.Y);
        float centre = Floor(solid, feet, hint);
        Span<float> floors = stackalloc float[8];
        for (int i = 0; i < 8; i++) floors[i] = float.NaN;
        foreach (float h in Collision.Body)
        {
            if (solid.Solid(feet with { Z = centre + h }, hint)) return false;
            if (radius <= 0f) continue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.Tau / 8f;
                var round = new Vector2(MathF.Cos(a), MathF.Sin(a));
                if (Vector2.Dot(round, step) <= 1e-4f) continue;          // beside or behind the step
                var at = feet + new Vector3(round * radius, 0f);
                if (float.IsNaN(floors[i])) floors[i] = Floor(solid, at, hint);
                if (solid.Solid(at with { Z = floors[i] + h }, hint)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The height a point of the body stands on: where 1.1's ground ray
    /// (<see cref="WorldSweep.Ground"/>, from <see cref="WorldSweep.GroundAbove"/> over the point)
    /// meets walk-flagged ground, when that is above the feet; the feet's own height otherwise.
    /// </summary>
    static float Floor(Collision solid, Vector3 at, int hint) =>
        WorldSweep.Ground(solid.Level, hint, at) is { } met && met.Point.Z > at.Z ? met.Point.Z : at.Z;
}
