// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The thin chase ray and the chase camera that stands on it: the tests' own, when a comparison with 1.1 found nothing
// else used them (they were `Pick.ChaseDistance`, `Pick.CameraClearance` and a `FreeCamera.Orbit` overload).
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>The tests' thin chase ray (ours: the game camera's pull-in is the engine's sweep, `CameraSweep.GameCamera`).</summary>
static class ChaseRay
{
    /// <summary>
    /// How far behind a character the camera can actually sit. The engine casts a **fat ray** of
    /// radius 10 from the character out towards where the camera wants to be and stops at the first
    /// thing that counts (the original's fat ray): a surface, a thing flagged THING_FLAG_CAMERABLOCK,
    /// or a region. Everything else — ordinary props, people — the camera passes straight through.
    ///
    /// This matters more than it looks. With the camera free to sit inside a wall, half of all
    /// clicks land on the inside of that wall instead of where the player pointed, because the ray
    /// a click casts starts where the camera is.
    ///
    /// **The game camera no longer uses this**: it casts the engine's own sweep,
    /// <see cref="CameraSweep.GameCamera"/>, the original's collision rules. What is left here is a
    /// **thin ray pulled in by the same 10**, against the level alone, which the harness's own chase
    /// camera (<see cref="Orbit"/>, `PathTests`) still uses.
    ///
    /// **A real swept sphere was written and taken out again.** Play had shown the
    /// camera jitter in play, and a thin ray does slip through the gap beside a pillar and let the
    /// camera out for a frame — `probe k28` counts those reversals, and a sphere removed them in
    /// the monastery. But the same sphere stops dead on any wall it *grazes*, which in Prague's
    /// streets pulled the camera onto the character's shoulder three times as often as the ray
    /// does: a swept-sphere camera has to **slide** along what it touches, and what the engine's
    /// own sweep does about that is in the collision query (one builds the query, another takes the
    /// nearest hit) and had not been read. It has now: the engine
    /// does not slide either — it stops on the first face its sphere reaches, and it counts a face
    /// whose edge is within the radius measured flat, so it snaps in more often than this ray does.
    /// </summary>
    public const float Clearance = 10f;

    /// <summary>
    /// How far back the camera may sit, <paramref name="want"/> being how far back it would like to.
    ///
    /// The ray is cast <b><see cref="Clearance"/> further than the camera wants to go</b>, and
    /// that is not a fudge — it is the fat ray's own reach. The engine sweeps a sphere of radius 10
    /// whose <em>centre</em> travels the requested distance (the length and the radius go
    /// into the query as two separate fields), so the
    /// sphere's <em>surface</em> touches anything within length + 10. A thin ray stopped at the
    /// length is blind to exactly that last 10 units — and since a pull-in leaves the camera sitting
    /// 10 short of what it hit, the wall that pulled it in lands squarely in the blind band.
    ///
    /// That blindness was the jitter seen in play. Pulled in, the camera cannot see the
    /// wall that pulled it; the distance eases back out; once it has crawled past the 10 units the
    /// wall reappears and snatches it back; repeat. At 60 fps one frame of easing (1000 u/s) clears
    /// the whole band in a single step and nothing shows, which is why it hid for so long — but his
    /// machine runs the loop far faster than that, and `probe k29` measures the swing growing to the
    /// full 10 units at 300 fps with the pull-in letting go and grabbing again 200-odd times in
    /// three seconds. Casting the extra 10 makes the ray see what the sphere would, and the swing
    /// goes to zero at every frame rate.
    /// </summary>
    public static float Distance(NilLevel level, Vector3 target, Vector3 towardsCamera, float want)
    {
        var hit = Pick.Nearest(level, target, towardsCamera, want + Clearance);
        return hit is { } spot ? Math.Clamp(spot.Distance - Clearance, 0f, want) : want;
    }

    /// <summary>
    /// <see cref="FreeCamera.Orbit(Vector3, float)"/>, but not through a wall: the level is asked how far back the camera
    /// can actually sit and it stops there, by the thin ray <see cref="Distance"/>. Not the game camera's pull-in, which
    /// is the engine's fat ray (`CameraSweep.GameCamera`).
    /// </summary>
    public static void Orbit(this FreeCamera camera, Vector3 target, float distance, NilLevel level) =>
        camera.Position = target - camera.Forward * Distance(level, target, -camera.Forward, distance);
}
