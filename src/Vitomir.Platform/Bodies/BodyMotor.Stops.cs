// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>What stopped the player</b> (ours, for the log; <see cref="PlayerStopped"/>): when the player wishes to move
/// on the ground and does not move on (<see cref="StoppedAgainst"/>), one read of the probe along the wish tells what it
/// was — a thing too high for any move at the body's pace, with its height and depth and the move that would take it
/// (<see cref="StopTaker"/>: the run's step-over, the sprint vault, a tap's, a charge's, or none); a face marked not to be
/// climbed; a top marked no place to stand; a wall; or nothing the probe reads. The moves' bands are the motor's own
/// (<c>BodyMotor.Pace.cs</c>, <c>TryHopUp</c>, <c>TryFromAir</c>), read from the one obstacle; nothing here moves a
/// body. Made only for the player, only when stopped and only once a stop (<see cref="BodySystem"/>), allocating nothing.
/// </summary>
public static partial class BodyMotor
{
    /// <summary>
    /// A body going slower than this across (m/s), the tick before and after its step, has not moved on: a body setting
    /// off from rest goes faster than this after its first tick (a walk's 1.3 m/s reached in 0.15 s: 0.07 m/s).
    /// </summary>
    public const float StopSpeed = 0.02f;

    /// <summary>How far beyond its radius a stopped body looks for what stopped it (m).</summary>
    public const float StopLook = 0.1f;

    /// <summary>
    /// Whether a body on the ground that wishes to move (<paramref name="intent"/>) did not move on: it went slower than
    /// <see cref="StopSpeed"/> across both before and after this tick's step (<paramref name="before"/>, the body's
    /// velocity before it), standing, in no move. Pure.
    /// </summary>
    public static bool StoppedAgainst(in Body body, Vector3 before, in MoveIntent intent)
    {
        if (body.State != BodyState.Ground || intent.Direction.LengthSquared() < 0.09f) return false;
        return new Vector2(before.X, before.Y).LengthSquared() < StopSpeed * StopSpeed
               && new Vector2(body.Velocity.X, body.Velocity.Y).LengthSquared() < StopSpeed * StopSpeed;
    }

    /// <summary>
    /// <see cref="StoppedAgainst(in Body, Vector3, in MoveIntent)"/> for a body on the spring: also the spring asked more than
    /// <see cref="StopSpeed"/> across this tick (<paramref name="asked"/>, <see cref="MotorOutcome.SpringAsked"/>). Setting off from
    /// rest the spring's first ticks ask less (from rest, at 0.2 s: 0.002, 0.008, 0.018 m/s, then 0.03), and that is no stop. Pure.
    /// </summary>
    public static bool StoppedAgainst(in Body body, Vector3 before, in MoveIntent intent, float asked) =>
        asked >= StopSpeed && StoppedAgainst(body, before, intent);

    /// <summary>
    /// What a body standing at <paramref name="feet"/> was stopped at along the level <paramref name="way"/>: one
    /// read of the probe (<see cref="TraversalProbe.Read"/>) from the radius to <see cref="StopLook"/> beyond it, looking
    /// as high as a charge's grab reaches; when it reads nothing, one ray down ahead for a no-stand top within the stride
    /// and one level ray ahead for a face with no top in the look (a wall).
    /// <paramref name="height"/> is the top above the ground under the feet and <paramref name="depth"/> its depth square
    /// to its face (0 when not read); <paramref name="move"/> and <paramref name="by"/> the move that would take it (the
    /// least: the run's, the sprint's, a tap's, a charge's) or none. Pure.
    /// </summary>
    public static StopCause ReadStop(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco,
                                     out float height, out float depth, out TraversalKind move, out StopTaker by)
    {
        height = 0f; depth = 0f; move = TraversalKind.None; by = StopTaker.None;
        float wl = way.Length();
        if (wl < 1e-4f) return StopCause.Unread;
        way /= wl;
        float ground = feet.Z - Skin;
        var own = loco.PoweredReach();
        float above = MathF.Max(MathF.Max(loco.MantleMax, own.MantleMax),
                                MathF.Max(loco.StandingReach + loco.TapHeight, own.StandingReach + loco.PoweredHeight)) + 0.25f;
        if (TraversalProbe.Read(level, feet, way, loco, loco.Radius + StopLook, above, out var ob))
        {
            if (!ob.HasTop && !ob.NoClimb) return StopCause.Wall;
            height = ob.Top - ground;
            depth = ob.Depth;
            if (ob.NoClimb) return StopCause.NoClimb;
            (move, by) = Taker(level, feet, way, loco, ob, height);
            return ob.NoStand ? StopCause.NoStand : StopCause.TooHigh;
        }
        // Nothing above the stride: a top marked no-stand level with the floor or within the stride, just ahead.
        var ahead = new Vector2(feet.X, feet.Y) + way * (loco.Radius + StopLook);
        float from = feet.Z + MathF.Max(0f, loco.StepHeight) + 0.05f;
        if (level.Ray(new Vector3(ahead, from), -Vector3.UnitZ, from - (ground - 0.1f)) is { } g && level.NoStand(g.Triangle))
        {
            height = g.Point.Z - ground;
            return StopCause.NoStand;
        }
        // A face the probe met but found no top to (a solid going on above the look: its ray down starts inside it).
        float chest = feet.Z + MathF.Max(0f, loco.StepHeight) + 0.1f;
        if (level.Ray(new Vector3(feet.X, feet.Y, chest), new Vector3(way, 0f), loco.Radius + StopLook + 0.05f) is { } face
            && Vector2.Dot(new Vector2(face.Normal.X, face.Normal.Y), way) < -0.2f)
            return StopCause.Wall;
        return StopCause.Unread;
    }

    /// <summary>The least move that takes the obstacle <paramref name="ob"/>, <paramref name="h"/> above the ground, and how.</summary>
    static (TraversalKind, StopTaker) Taker(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, in Obstacle ob, float h)
    {
        bool thin = ob.Depth < loco.MantleMinDepth - 1e-4f;
        if (h > loco.StepHeight && ob.HasTop && RisesFromFeet(level, feet, ob) && Beyond(level, feet, way, loco, ob, out _))
        {
            if (thin && loco.StepOverMax > loco.StepHeight && h <= loco.StepOverMax + Within) return (TraversalKind.StepOver, StopTaker.Run);
            if (loco.SprintVaultMax > loco.StepHeight && h <= loco.SprintVaultMax + Within && ob.Depth <= loco.SprintVaultDepth + 1e-4f)
                return (TraversalKind.Hurdle, StopTaker.Sprint);
        }
        var tap = Takes(loco, ob, h, thin, loco.TapHeight, HopReach(loco));
        if (tap != TraversalKind.None) return (tap, StopTaker.Tap);
        if (loco.CanCharge)
        {
            var charge = Takes(loco.PoweredReach(), ob, h, thin, loco.PoweredHeight, 0f);
            if (charge != TraversalKind.None) return (charge, StopTaker.Charge);
        }
        return (TraversalKind.None, StopTaker.None);
    }

    /// <summary>
    /// The move a jump of <paramref name="rise"/> with <paramref name="reach"/>'s numbers makes at the obstacle (as
    /// <c>TryHopUp</c> on the ground to <paramref name="hop"/>, then <c>TryFromAir</c>'s bands: a vault of a thin thing,
    /// a climb onto, a mantle, a grab of a lip under the hands at the jump's top), or none.
    /// </summary>
    static TraversalKind Takes(in Locomotion reach, in Obstacle ob, float h, bool thin, float rise, float hop)
    {
        if (ob.Standable && h <= hop + Within) return TraversalKind.HopUp;
        if (thin && ob.Beyond && h <= reach.VaultMax + Within) return TraversalKind.Vault;
        if (ob.Standable && h >= reach.VaultMin - Within && h <= reach.VaultMax + Within) return TraversalKind.ClimbOnto;
        if (ob.Standable && h > reach.VaultMax + Within && h <= reach.MantleMax + Within) return TraversalKind.Mantle;
        if (ob.Holdable && reach.StandingReach > 0f && h <= reach.StandingReach + rise + 0.05f) return TraversalKind.Grab;
        return TraversalKind.None;
    }
}
