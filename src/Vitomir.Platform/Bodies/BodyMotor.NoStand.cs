// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>No-stand tops</b> (ours): no body stands on a triangle the level marks
/// <see cref="Formats.L2.L2Surface.NoStand"/> (a mesh the layout names, or a top inside an area marked
/// <c>"stand": "none"</c>; <see cref="Space.CollisionWorld.NoStand"/>).
///
/// <para>The ground test (<see cref="Probe"/>) takes no footing on one, so: <b>walking</b> meets it as a slope too steep
/// — a step up onto it fails, and a move on the ground that would end with the body's footing on one (a marked top
/// level with the floor, beyond a stair's head) is taken back and the body stops at its edge
/// (<see cref="StoppedAtNoStand"/>); a body <b>on it for any reason</b> — landed from a jump, put there, at the end of a
/// move — is in the air resting on it and <b>slides off</b>: each tick its speed across towards the nearest edge of the
/// marked surface is raised to <see cref="NoStandSlideSpeed"/> (<see cref="SlideOffNoStand"/>), so it leaves the top as
/// it would slide down a slope too steep, never stuck, and falls or lands beyond it. The traversal probe reads such a top
/// as not standable (<see cref="Obstacle.NoStand"/>), so no move ends on it.</para>
///
/// <para><b>The nearest edge</b> is found the same way on every machine: from the feet, <see cref="NoStandWays"/> level
/// ways at even turns (the first along +X, anticlockwise), each as far as the body's capsule, lifted a little, can go
/// along it; then rings every <see cref="NoStandLookStep"/> out to <see cref="NoStandLook"/>, a ray down at each way's
/// point in that ring. The first ring with a point that is off the marked surface (nothing under it, or a triangle not
/// marked) gives the way: the mean of its ways that are off (so a body with its middle just past an edge goes straight
/// out, not along it), or the first of them when they cancel. A point past what the capsule can reach along its way is
/// not looked at, so no body is slid into a wall or a chimney. With no edge in reach (a marked top wider than twice the
/// look) the body keeps the way it is moving, or its facing.</para>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>The least speed across at which a body is slid off a no-stand top towards its edge (m/s).</summary>
    public const float NoStandSlideSpeed = 2.0f;

    /// <summary>A body meeting a no-stand top faster than this downwards has come down onto it (<see cref="MotorOutcome.NoStandMet"/>, m/s).</summary>
    public const float NoStandMetSpeed = 0.5f;

    /// <summary>How far the edge of a no-stand top is looked for from the feet (m), and the rings' spacing (m).</summary>
    public const float NoStandLook = 6f, NoStandLookStep = 0.125f;

    /// <summary>How many level ways the edge is looked for along.</summary>
    public const int NoStandWays = 16;

    /// <summary>How far above the top the look's rays start (m), and how far the capsule is lifted to measure each way's room.</summary>
    const float NoStandRayAbove = 0.3f, NoStandLift = 0.05f;

    /// <summary>The level ways, at even turns from +X anticlockwise, worked out once with <see cref="DetMath"/>.</summary>
    static readonly Vector2[] NoStandWayTable = MakeWays();

    static Vector2[] MakeWays()
    {
        var ways = new Vector2[NoStandWays];
        for (int i = 0; i < ways.Length; i++)
        {
            var (s, c) = DetMath.SinCos(i * DetMath.TwoPi / NoStandWays);
            ways[i] = new Vector2(c, s);
        }
        return ways;
    }

    /// <summary>Whether a footing found by the ground test may be stood on: it is not a no-stand triangle (the hook in <see cref="Probe"/>).</summary>
    static bool Standable(in Mover m, in Support s) => !m.Level.NoStand(s.Triangle);

    /// <summary>
    /// Whether a move on the ground from <paramref name="start"/> to <paramref name="feet"/> found no footing only because
    /// it put the body on a no-stand top, from footing it may stand on (the hook in <see cref="MoveOnGround"/>): then the
    /// move is taken back, as walking into a slope too steep is stopped. A body whose footing at the start is itself
    /// marked (put there) is let go into the air, to slide off.
    /// </summary>
    static bool StoppedAtNoStand(in Mover m, Vector3 start, Vector3 feet)
    {
        float reach = m.Step + m.R + Skin;
        if (!Footing(m, feet, reach, keeping: true, out var at) || !m.Level.NoStand(at.Triangle)) return false;
        if (at.FeetZ < feet.Z - m.Step - 1e-4f || at.FeetZ > feet.Z + m.Step + 1e-4f) return false;
        return Footing(m, start, reach, keeping: true, out var from) && Standable(m, from);
    }

    /// <summary>
    /// A body in the air resting on a no-stand top (its footing within the landing's reach is a marked triangle, and it is
    /// not going up) is slid off it: its speed across along the way to the nearest edge raised to
    /// <see cref="NoStandSlideSpeed"/> (the hook in <see cref="MoveInAir"/>). <paramref name="downSpeed"/> is how fast it
    /// was coming down before it met the top this tick.
    /// </summary>
    static void SlideOffNoStand(in Mover m, Vector3 feet, float yaw, float downSpeed, ref Vector3 vel, ref MotorOutcome o)
    {
        if (vel.Z > 1e-4f) return;
        if (!Footing(m, feet, 4f * Skin, keeping: false, out var s) || !m.Level.NoStand(s.Triangle)) return;
        if (s.FeetZ < feet.Z - 4f * Skin || s.FeetZ > feet.Z + AirStride + 1e-3f) return;
        var vh = new Vector2(vel.X, vel.Y);
        if (!WayOffNoStand(m, feet, s.FeetZ, out var way))
        {
            float l = vh.Length();
            way = l > 0.1f ? vh / l : Facing(yaw);
        }
        float along = Vector2.Dot(vh, way);
        if (along < NoStandSlideSpeed) vh += way * (NoStandSlideSpeed - along);
        vel = new Vector3(vh, vel.Z);
        o.OnNoStand = true;
        o.NoStandWay = way;
        if (downSpeed > NoStandMetSpeed && !s.Edge) o.NoStandMet = true;   // onto its face; rolling off its edge is not a meeting
    }

    /// <summary>
    /// The level way from <paramref name="feet"/> to the nearest edge of the no-stand surface at height
    /// <paramref name="top"/>, within <see cref="NoStandLook"/> (see the class's remarks); false when none is in reach.
    /// </summary>
    static bool WayOffNoStand(in Mover m, Vector3 feet, float top, out Vector2 way)
    {
        way = Vector2.Zero;
        Span<float> room = stackalloc float[NoStandWays];
        var lifted = m.At(feet + new Vector3(0f, 0f, NoStandLift));
        for (int i = 0; i < NoStandWays; i++)
        {
            var w = NoStandWayTable[i];
            room[i] = m.Level.CapsuleSweep(lifted, new Vector3(w, 0f), NoStandLook) is { } hit ? hit.Distance : NoStandLook;
        }
        int rings = (int)(NoStandLook / NoStandLookStep + 0.5f);
        for (int k = 1; k <= rings; k++)
        {
            float d = k * NoStandLookStep;
            var sum = Vector2.Zero;
            int first = -1;
            for (int i = 0; i < NoStandWays; i++)
            {
                if (d > room[i] + 1e-4f) continue;
                var w = NoStandWayTable[i];
                var from = new Vector3(feet.X + w.X * d, feet.Y + w.Y * d, top + NoStandRayAbove);
                // Off: nothing under the point within a fall as deep as the look is far, or a triangle that is not marked.
                if (m.Level.Ray(from, -Vector3.UnitZ, NoStandRayAbove + 0.6f + d) is { } under && m.Level.NoStand(under.Triangle)) continue;
                sum += w;
                if (first < 0) first = i;
            }
            if (first < 0) continue;
            float sl = sum.Length();
            way = sl > 1e-3f ? sum / sl : NoStandWayTable[first];
            return true;
        }
        return false;
    }
}
