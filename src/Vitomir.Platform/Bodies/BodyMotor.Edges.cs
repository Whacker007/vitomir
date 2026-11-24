// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>Edges and cancel</b> (the movement design, ours, climbing down and the footing of
/// bodies on routes).
///
/// <para><b>The edge guard.</b> An intent may ask the motor to keep its body from moving over a drop
/// (<see cref="MoveIntent.Guard"/>): on the ground, a move that would take the body's <b>middle</b> over a drop —
/// from the ground under its middle where it stands to the ground under its middle where it would be, or no ground
/// within <see cref="DropLook"/> — higher than the guard's limit is not taken as it is: its part into the edge is
/// taken away and the rest tried along the edge, as along a wall; where that too would go over (a corner of the
/// edge) the body stops. A gap narrower than the body's radius (a gutter, a crack) is bridged by its footing and
/// never guarded. The limits: the player <b>at a walk</b> stops at drops of more than <see cref="Locomotion.SafeFall"/>
/// (running, sprinting and jumping go over); a body following a route stops at drops of more than its step, or of
/// <see cref="StepDownSlack"/> more where its route goes down there (<see cref="StepsDown"/>), whatever its route —
/// it never falls. The drop is measured from
/// the ground under the middle where the body stands — a stair's lower tread when its feet are level with the nosing
/// above — but never from more than a step below its feet, so a body standing level with an edge over lower ground
/// (a kerb or a wall's top beside a stair going down) is not let down a drop a little at a time; it may move back
/// over ground, never further out. And a move that would leave the ground altogether is measured from the feet
/// themselves (a kerb's top over a stair going down, which the middle's measure let by a little at a time).</para>
///
/// <para><b>The footing of a body on a route</b> (<i>"Ideally NPCs just walking shouldn't
/// ever hop vault mantle or fall off anything"</i>). Its disc's footing, which holds a body level with an edge until its
/// middle is a radius past it, is the player's; a body on a route also <b>steps down</b> onto the ground under its middle
/// when that is a step it takes below its feet (<see cref="StepDown"/>): held level with an edge beside its way whose
/// middle is over a stair going down (moved out from the edge by no more than <see cref="StepDownAside"/>), or walked
/// off a riser a little higher than its step, which it would otherwise fly over (put on the ground under it). It does
/// not <b>step up</b> onto an edge beside its way that its route does not climb (<see cref="SteppedUpAside"/>): that
/// edge is slid along as a wall.</para>
///
/// <para><b>Lowering to hang.</b> Crouch held as the body's middle goes over an edge — at any pace, moving towards
/// it — whose drop is more than <see cref="HangMinDrop"/> and whose lip the hands can hold (read by
/// <see cref="TraversalProbe"/> from where the body would hang: a near-level top meeting a near-upright face), and with
/// room to hang there: the body steps out over the edge and down to hang from it (<see cref="TraversalKind.LowerToHang"/>,
/// <see cref="LowerSeconds"/>), turning to face it, and then hangs as from a grab — jump or a fresh forward pulls up,
/// crouch pressed again drops. Where the hands cannot hold the edge, the guard (at a walk) stops the body, or it
/// walks off. <b>Climbing down</b>: where the drop is less than the hands' reach
/// (<see cref="Locomotion.StandingReach"/>) the lowering carries on to the ground, and the body stands at the foot a
/// radius out from the face (<see cref="ClimbsDown"/>; crouched while crouch is still held), rather than hanging with
/// its feet on the ground. The lowering's <see cref="Traversal.Height"/> is the drop.</para>
///
/// <para><b>Cancel</b> (<see cref="MoveIntent.Cancel"/>): the hands let go of a lip they hold (a hang or a grab), as
/// crouch does; a hop up, a vault, a climb onto, a mantle or a lowering to hang in its first
/// <see cref="CancelSeconds"/> is undone — the body put back where the move started, on the ground under it when
/// that is within a hop, standing.</para>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How far down the ground beyond an edge is looked for (m): none within it is a drop without end (+∞).</summary>
    public const float DropLook = 10f;

    /// <summary>The least drop crouch lowers the body to hang from (m): a lower edge is stepped or walked off.</summary>
    public const float HangMinDrop = 1.0f;

    /// <summary>How long lowering to hang takes (s): out over the edge in the first <see cref="LowerOut"/> of it, then down.</summary>
    public const float LowerSeconds = 0.6f;

    /// <summary>The share of lowering to hang spent stepping out over the edge, and how far above the top the feet go out (m).</summary>
    public const float LowerOut = 0.35f, LowerLift = 0.03f;

    /// <summary>A move can be cancelled this long after it starts (s).</summary>
    public const float CancelSeconds = 0.15f;

    /// <summary>
    /// How much more than its step a body following a route steps down where its route goes down (m): walk35's
    /// stair by the gate has risers of 0.353 m, 3 mm more than the human's step, and a body running down it flew over
    /// two treads and fell 1.07 m. Deeper than its step and this, a body on a route never goes, wherever its route goes.
    /// </summary>
    public const float StepDownSlack = 0.05f;

    /// <summary>How far a body following a route that has walked off a step may be moved across to be put down on the ground under it, clear of the edge (m).</summary>
    public const float StepDownNudge = 0.05f;

    /// <summary>
    /// How far a body following a route, held level with an edge beside its way, may be moved out from it to step down
    /// onto the ground under its middle (m): on walk35 a body running along the gate tower's landing beside its
    /// stair going down had its middle over the stair's top tread 0.17 m from the landing's edge.
    /// </summary>
    public const float StepDownAside = 0.2f;

    /// <summary>How many times a body stepping down is pushed out from the edge across before it must stand clear of it.</summary>
    const int StepDownRounds = 6;

    /// <summary>The least the ground under a body's middle is below its feet for it to step down onto it (m): less is the ground it stands on.</summary>
    const float StepDownMin = 0.03f;

    /// <summary>How far a body is drawn into the face it climbs down, as it reaches the foot (m): none — it stands a radius out from the face.</summary>
    const float ClimbDownTolerance = 0.01f;

    /// <summary>
    /// Whether a body following a route steps down a drop of <paramref name="drop"/> at <paramref name="edge"/>:
    /// one within its step always; one within <see cref="StepDownSlack"/> more where its route goes down
    /// (<see cref="RouteGoesDown"/>); a deeper one never — the body is kept from it, as the rest of its route asks
    /// (<i>"Ideally NPCs just walking shouldn't ever hop vault mantle or fall off anything"</i>).
    /// </summary>
    public static bool StepsDown(in MoveIntent intent, Vector3 edge, Vector2 edgeOut, float drop, float step) =>
        drop <= step + 1e-3f || (drop <= step + StepDownSlack + 1e-3f && RouteGoesDown(intent.GuardCorner, edge, edgeOut, drop));

    /// <summary>
    /// Whether lowering from an edge ends standing at its foot rather than hanging: the hands' reach
    /// (<see cref="Locomotion.StandingReach"/>) goes below the ground there, so the feet were put on it.
    /// </summary>
    public static bool ClimbsDown(in Traversal move, in Locomotion loco) =>
        move.Kind == TraversalKind.LowerToHang && move.Lip.Z - move.To.Z < loco.StandingReach - ClimbDownTolerance;

    /// <summary>
    /// Whether a route whose next corner is <paramref name="corner"/> goes down the step of <paramref name="drop"/> at
    /// <paramref name="edge"/> (its outward way across <paramref name="edgeOut"/>): the corner nearer the step's foot in
    /// height than its top, or beyond the edge by <see cref="RouteBeyond"/> or more — across a sunken way (1.1's Old Town
    /// has a ramp 0.37 m deep across a street, whose far side is the next corner). The corner is meant to be at the
    /// foot's height and within 2 m of the edge, to keep bodies off walk35's stair's open side (the next corner 8.7 m
    /// below); a body on a route is never let down more than <see cref="StepDownSlack"/> over its step whatever
    /// its route (<see cref="StepsDown"/>), and the last riser of 0.354 m of that stair, its next corner 7.5 m on, was a stop.
    /// </summary>
    public static bool RouteGoesDown(Vector3 corner, Vector3 edge, Vector2 edgeOut, float drop) =>
        !float.IsPositiveInfinity(drop)
        && (corner.Z < edge.Z - 0.5f * drop || Vector2.Dot(new Vector2(corner.X - edge.X, corner.Y - edge.Y), edgeOut) >= RouteBeyond);

    /// <summary>How far beyond an edge a route's next corner is for the route to cross it (m).</summary>
    public const float RouteBeyond = 0.3f;

    /// <summary>How far a lip read from below may be from the edge's height and be that edge (m).</summary>
    const float LipMatch = 0.1f;

    /// <summary>The radius of the ball swept down to find the ground under a point (m): small, so a crack between triangles is never a drop.</summary>
    const float GroundBall = 0.02f;

    /// <summary>How fast a body guarded at an edge is drawn back from it as it slides along (m/s): a millimetre a tick.</summary>
    const float EdgeBack = 0.1f;

    /// <summary>How near the side of a top's triangle the ball must touch for it to be the edge (m).</summary>
    const float EdgeNearest = 0.03f;

    /// <summary>A contact whose normal's upward part is less than this is a wall the ball grazed, not ground under it.</summary>
    const float GrazedWall = 0.3f;

    /// <summary>How many times the way to an edge is halved to find where the middle meets it.</summary>
    const int EdgeHalvings = 10;

    /// <summary>The highest drop <paramref name="intent"/>'s guard lets the body move over (m), or +∞ when it asks for none.</summary>
    public static float GuardLimit(in MoveIntent intent, in Locomotion loco) => intent.Guard switch
    {
        EdgeGuard.SafeFall => MathF.Max(loco.SafeFall, loco.StepHeight),
        EdgeGuard.Footing => loco.StepHeight,
        _ => float.PositiveInfinity,
    };

    /// <summary>
    /// Whether <paramref name="body"/> making <paramref name="move"/> has something cancel undoes: a lip held
    /// (a hang, a grab), or a hop up, a vault, a climb onto, a mantle or a lowering to hang in its first
    /// <see cref="CancelSeconds"/>; and a jump charging (<see cref="IsCharging"/>), which it ends with no jump.
    /// </summary>
    public static bool CanCancel(in Body body, in Traversal move) => IsCharging(body) || move.Kind switch
    {
        TraversalKind.Hang or TraversalKind.Grab => body.State is BodyState.Hanging or BodyState.Traversing,
        TraversalKind.Ladder => body.State == BodyState.Ladder,   // cancel lets go of a ladder (BodyMotor.Ladder.cs)
        TraversalKind.HopUp or TraversalKind.Vault or TraversalKind.ClimbOnto or TraversalKind.Mantle or TraversalKind.LowerToHang =>
            body.State == BodyState.Traversing && move.Progress * move.Seconds <= CancelSeconds + 1e-4f,
        _ => false,
    };

    /// <summary>
    /// One tick's move on the ground under the intent's guard (and crouch held over an edge lowering the body to hang):
    /// answers true when a lowering to hang started (the move has been run for its first tick).
    /// </summary>
    static bool GuardedMove(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, Vector2 vh, Vector2 way, in Locomotion loco,
                            in MoveIntent intent, bool moves, ref MotorOutcome o)
    {
        float guard = GuardLimit(intent, loco);
        bool footing = intent.Guard == EdgeGuard.Footing;
        bool lower = moves && intent.Crouch && loco.StandingReach > 0f && way != Vector2.Zero;
        // Crouched towards an edge, a body given the ledge's clips keeps its footing to the edge for the lowering (Mover.LowersAtEdge).
        if (lower && m.LeavesAtEdge && m.LowersAtEdge)
            return GuardedMove(new Mover(m.Level, m.Others, m.Self, loco, m.H), ref body, ref move, ref feet, vh, way, loco, intent, moves, ref o);
        float check = lower ? MathF.Min(guard, MathF.Max(HangMinDrop, m.Step)) : guard;
        if (float.IsPositiveInfinity(check))
        {
            MoveOnGround(m, ref body, ref feet, vh, way, loco);
            return false;
        }
        var saved = body;
        var start = feet;
        MoveOnGround(m, ref body, ref feet, vh, way, loco);
        if (footing && feet.Z > start.Z + StepDownMin && body.State == BodyState.Ground && SteppedUpAside(m, start, feet, intent))
        {
            // Not up onto an edge beside its way its route does not climb — slid along it as along a wall.
            body = saved; feet = start;
            var flatLoco = loco;
            flatLoco.StepHeight = 0f;
            var flat = new Mover(m.Level, m.Others, m.Self, flatLoco, m.H) { LeavesAtEdge = m.LeavesAtEdge };
            MoveOnGround(flat, ref body, ref feet, vh, way, flatLoco);
            // Where that does not keep it on the ground as it was (the ground-keeping without a step finds only ground at
            // its own height), it stays where it is this tick.
            if (body.State != BodyState.Ground || MathF.Abs(feet.Z - start.Z) > 1e-3f)
            {
                body = saved; feet = start;
                body.Velocity = Vector3.Zero;
            }
        }
        if (!OverDrop(m, start, feet, check, out float drop, out var edgeOut, out var edge))
        {
            // Walked off (left the ground this tick) over what the middle's measure let by — a kerb or a wall's top over
            // a stair going down, its middle already over the stair: the drop from the feet themselves decides.
            if (body.State != BodyState.Air || float.IsPositiveInfinity(guard))
            {
                if (footing) StepDown(m, ref body, ref feet, loco, intent, saved, Vector2.Zero);
                return false;
            }
            drop = GroundUnder(m.Level, feet.X, feet.Y, feet.Z + 0.03f, out float below, out _, out _) ? start.Z - below : float.PositiveInfinity;
            var away = new Vector2(feet.X - start.X, feet.Y - start.Y);
            float al = away.Length();
            edgeOut = al > 1e-6f ? away / al : Vector2.Zero;
            edge = new Vector3(feet.X, feet.Y, start.Z);
            // A body on a route that walks off a step it takes is put down on the ground under it, not flown
            // over the stair (walk35's risers of 0.353 m, a run: over two treads and down 1.07 m).
            if (footing && StepsDown(intent, edge, edgeOut, drop, m.Step))
            {
                StepDown(m, ref body, ref feet, loco, intent, saved, edgeOut);
                return false;
            }
            if (drop <= guard + 1e-3f || edgeOut == Vector2.Zero) return false;
            lower = false;
        }
        if (lower && drop > HangMinDrop)
        {
            var moved = body; var movedFeet = feet;
            body = saved; feet = start;
            if (TryLowerToHang(m, ref body, ref move, ref feet, loco, intent, edge, edgeOut, drop, ref o)) return true;
            body = moved; feet = movedFeet;
        }
        if (drop <= guard + 1e-3f) return false;
        if (footing && StepsDown(intent, edge, edgeOut, drop, m.Step))
        {
            // The middle over a step the route takes: the move stands, and the body steps down onto it when it can.
            StepDown(m, ref body, ref feet, loco, intent, saved, edgeOut);
            return false;
        }

        // Guarded: the part of the move into the edge taken away, the rest tried along it; else the body stops.
        o.Guarded = true;
        o.GuardedDrop = drop;
        o.GuardedEdge = edge;
        body = saved; feet = start;
        // A little back from the edge as well (EdgeBack), so the body sliding along it is never left exactly at the
        // point where its middle would be over the drop, to be stopped there on alternate ticks.
        float into = Vector2.Dot(vh, edgeOut);
        var along = into > 0f ? vh - edgeOut * (into + EdgeBack) : vh;
        float wayInto = Vector2.Dot(way, edgeOut);
        var wayAlong = wayInto > 0f ? way - edgeOut * wayInto : way;
        if (along.LengthSquared() > 1e-6f)
        {
            float wl = wayAlong.Length();
            MoveOnGround(m, ref body, ref feet, along, wl > 1e-3f ? wayAlong / wl : Vector2.Zero, loco);
            if (!OverDrop(m, start, feet, guard, out _, out _, out _))
            {
                if (footing) StepDown(m, ref body, ref feet, loco, intent, saved, Vector2.Zero);
                return false;
            }
            body = saved; feet = start;
        }
        body.Velocity = Vector3.Zero;
        // A body on a route held level with an edge whose middle is over a step down (walk35's landing beside its
        // stair, at a sprint) steps down onto it rather than standing there stopped.
        if (footing) StepDown(m, ref body, ref feet, loco, intent, saved, Vector2.Zero);
        return false;
    }

    /// <summary>
    /// A body following a route steps down onto the ground under its middle when that is lower than its feet by
    /// a step it takes (<see cref="StepsDown"/>): held level with an edge — a kerb or a landing — whose middle is over a
    /// stair going down (its disc's footing would hold it there until the guard stopped it short of stepping off), or
    /// walked off a riser a little higher than its step (which it would fly over). It is moved across no more than
    /// <see cref="StepDownNudge"/> to clear the edge, and only where it then stands on that ground; answers whether it
    /// stepped down. The player never does this: its footing is the disc's.
    /// </summary>
    /// <summary>
    /// Whether a body on a route that rose from <paramref name="start"/> to <paramref name="feet"/> this tick stepped up
    /// onto an edge <b>beside</b> its way that its route does not climb: its next corner no higher than it was, the
    /// ground under its middle still about where it came from (it stands level with the edge, not on a tread under it),
    /// and the edge to the side rather than ahead — the capsule where it would be had it not risen is pushed out of the
    /// edge across its way, not back from a riser in front of it (a stair, a threshold). On the proving ground's gaps station a body sprinting
    /// along the foot of a stair's side climbed its treads sideways, its middle over the floor, and stalled at 0.6 m.
    /// </summary>
    static bool SteppedUpAside(in Mover m, Vector3 start, Vector3 feet, in MoveIntent intent)
    {
        if (intent.GuardCorner.Z > start.Z + StepDownMin) return false;
        // Risen a riser's height onto an edge it stands level with, the ground under its middle where it came from (a
        // body on a slope's foot, its ball on the slope off its middle, rises a little as it goes and is no step).
        if (feet.Z - start.Z < StepUpAsideMin) return false;
        if (!GroundUnder(m.Level, feet.X, feet.Y, feet.Z + 0.03f, out float under, out _, out _) || under > start.Z + StepDownMin) return false;
        float wl = intent.Direction.Length();
        if (wl < 1e-4f) return false;
        // Where the edge is: the capsule where it would be had it not risen, pushed out of it — back from a riser ahead,
        // across from an edge beside.
        var cap = m.At(new Vector3(feet.X, feet.Y, start.Z));
        var push = m.Level.Depenetrate(ref cap, Skin);
        var across = new Vector2(push.X, push.Y);
        float pl = across.Length();
        if (pl < 2e-3f) return false;
        return Vector2.Dot(intent.Direction / wl, across / pl) > -StepUpAhead;
    }

    /// <summary>A step up onto an edge whose push back is this far against the way (the cosine) is up a riser in front of the body, not beside it.</summary>
    const float StepUpAhead = 0.5f;

    /// <summary>How far a body must rise in a tick for it to be a step up onto an edge (m): less is uneven ground or a slope.</summary>
    const float StepUpAsideMin = 0.1f;


    static bool StepDown(in Mover m, ref Body body, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, in Body saved, Vector2 edgeOut)
    {
        if (!GroundUnder(m.Level, feet.X, feet.Y, feet.Z + 0.03f, out float under, out int tri, out _) || tri < 0) return false;
        float d = feet.Z - Skin - under;
        if (d < StepDownMin || m.Level.Normal(tri).Z < loco.MinStandNormalZ - 2e-5f) return false;
        // Held level with an edge on the ground, it steps down only going down: its route's next corner lower than its
        // feet, and not heading into the top it is held on — a body that has just stepped up onto a tread stands level
        // with its edge, its middle over the tread below, and must not be put back down there.
        bool held = body.State == BodyState.Ground;
        if (held && intent.GuardCorner.Z > feet.Z - StepDownMin) return false;
        // Out from the edge across, on the ground below: the capsule put on that ground and pushed out of what it is in,
        // the push's part across taken and the ground found again, a few times (a tread's corner below the ball's middle
        // pushes it up as well as out; a sloping foot is stood on with the ball's middle the radius from its plane).
        var at = new Vector2(feet.X, feet.Y);
        float g = under; int gt = tri;
        for (int k = 0; k < StepDownRounds; k++)
        {
            float nz = MathF.Max(m.Level.Normal(gt).Z, 0.5f);
            var cap = m.At(new Vector3(at, g + Skin + m.R / nz - m.R));
            var push = m.Level.Depenetrate(ref cap, Skin);
            var h = new Vector2(push.X, push.Y);
            if (h.LengthSquared() < 1e-8f) break;
            at += h;
            if (!GroundUnder(m.Level, at.X, at.Y, feet.Z + 0.03f, out g, out gt, out _) || gt < 0) return false;
        }
        var across = at - new Vector2(feet.X, feet.Y);
        float pl = across.Length();
        if (edgeOut == Vector2.Zero && pl > 2e-3f) edgeOut = across / pl;
        if (!StepsDown(intent, feet, edgeOut, d, m.Step)) return false;
        if (!held) { if (pl > StepDownNudge) return false; }
        else if (pl > 2e-3f)
        {
            // Out from an edge beside its way (walk35's landing beside its stair), never into it nor ahead of it: an edge
            // ahead is a stair's nosing, walked down by the footing itself once the middle is past it.
            float wl = intent.Direction.Length();
            float ahead = wl > 1e-4f ? Vector2.Dot(intent.Direction / wl, across / pl) : 0f;
            if (pl > StepDownAside || ahead < -0.1f || ahead > 0.5f) return false;
        }
        // Still a step down from where it stands, and stood on there by the footing itself.
        var low = new Vector3(at, g + Skin);
        if (low.Z > feet.Z - StepDownMin) return false;
        if (!Probe(m, low + new Vector3(0, 0, 0.02f), 0.06f, keeping: false, out var s) || MathF.Abs(s.FeetZ - low.Z) > 0.02f) return false;
        low.Z = s.FeetZ;
        if (!Fits(m.Level, low, m.R, body.CapsuleHeight(loco))) return false;
        feet = low;
        body.State = BodyState.Ground;
        body.GroundNormal = s.Normal;
        body.GroundTriangle = s.Triangle;
        body.Velocity.Z = 0f;
        body.Coyote = 0f;
        body.AirSeconds = 0f;
        body.GroundSeconds = saved.GroundSeconds;
        return true;
    }

    /// <summary>
    /// Whether moving the body's middle from <paramref name="start"/> to <paramref name="end"/> takes it over a drop higher
    /// than <paramref name="limit"/>: <paramref name="drop"/> (+∞ for no ground within <see cref="DropLook"/>), the edge's
    /// outward way across (<paramref name="edgeOut"/>, the move's own way when the edge's triangle is not found) and the
    /// point of the edge the middle meets (<paramref name="edge"/>, at the top's height).
    /// </summary>
    static bool OverDrop(in Mover m, Vector3 start, Vector3 end, float limit, out float drop, out Vector2 edgeOut, out Vector3 edge)
    {
        drop = 0f; edgeOut = Vector2.Zero; edge = start;
        var across = new Vector2(end.X - start.X, end.Y - start.Y);
        float len = across.Length();
        if (len < 1e-6f) return false;
        var u = across / len;
        const float rise = 0.03f;
        // The ground under the middle where the body stands (a stair's lower tread when its feet are level with the
        // nosing above), never more than a step below its feet: a body standing level with an edge over lower ground
        // (a kerb or a wall's top beside a stair going down, walk35's falls) measures from a step below its feet, so it
        // is not let down a drop a little at a time.
        float refZ = GroundUnder(m.Level, start.X, start.Y, start.Z + rise, out float under, out _, out _) ? under : float.NegativeInfinity;
        refZ = MathF.Max(refZ, start.Z - m.Step);
        float top = MathF.Max(start.Z, end.Z) + rise;
        drop = DropAt(m.Level, end.X, end.Y, top, refZ);
        if (drop <= limit + 1e-3f) return false;
        // A gap narrower than the body's radius is bridged by its footing: ground a radius further on.
        if (DropAt(m.Level, end.X + u.X * m.R, end.Y + u.Y * m.R, top, refZ) <= limit + 1e-3f) { drop = 0f; return false; }
        float lo = 0f, hi = len;
        for (int i = 0; i < EdgeHalvings; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (DropAt(m.Level, start.X + u.X * mid, start.Y + u.Y * mid, top, refZ) <= limit + 1e-3f) lo = mid; else hi = mid;
        }
        var at = new Vector2(start.X + u.X * lo, start.Y + u.Y * lo);
        edgeOut = u;
        edge = new Vector3(at, refZ);
        // The edge itself, from what the ball rests on at the last place it found the top: the side of the top's
        // triangle it touches, or the face below the edge when it touches that.
        if (GroundUnder(m.Level, at.X, at.Y, top, out float gz, out int tri, out var touch) && EdgeNear(m.Level, tri, touch, u, out var n, out var p))
        {
            edgeOut = n;
            edge = new Vector3(p, gz);
        }
        return true;
    }

    /// <summary>The drop from <paramref name="refZ"/> to the ground under (x, y) looked for from <paramref name="top"/> down (+∞ for none within <see cref="DropLook"/>).</summary>
    static float DropAt(CollisionWorld level, float x, float y, float top, float refZ) =>
        GroundUnder(level, x, y, top, out float z, out _, out _) ? refZ - z : float.PositiveInfinity;

    /// <summary>The ground under (x, y) below <paramref name="top"/> within <see cref="DropLook"/>: a small ball swept down, so a crack is not a hole; where it touches, and what.</summary>
    static bool GroundUnder(CollisionWorld level, float x, float y, float top, out float z, out int triangle, out Vector3 touch)
    {
        z = 0f; triangle = -1; touch = default;
        if (level.SphereSweep(new Vector3(x, y, top + GroundBall), -Vector3.UnitZ, DropLook, GroundBall) is not { } hit) return false;
        // A ball grazing a wall (its middle within its radius of the face just past an edge) is not on the ground:
        // the line straight down says what is under the point.
        if (hit.Normal.Z < GrazedWall)
        {
            if (level.Ray(new Vector3(x, y, top), -Vector3.UnitZ, DropLook) is not { } line) return false;
            hit = line;
        }
        z = hit.Point.Z; triangle = hit.Triangle; touch = hit.Point;
        return true;
    }

    /// <summary>
    /// The edge a ball resting at <paramref name="touch"/> on triangle <paramref name="t"/> is at, facing the way
    /// <paramref name="u"/>: a steep face's own outward normal across (the face under the edge), or the side of a top's
    /// triangle nearest the touch (within <see cref="EdgeNearest"/>); its outward normal across (<paramref name="n"/>)
    /// and the point of it nearest the touch (<paramref name="p"/>).
    /// </summary>
    static bool EdgeNear(CollisionWorld level, int t, Vector3 touch, Vector2 u, out Vector2 n, out Vector2 p)
    {
        n = u; p = new Vector2(touch.X, touch.Y);
        if (t < 0) return false;
        var fn = level.Normal(t);
        var across = new Vector2(fn.X, fn.Y);
        if (MathF.Abs(fn.Z) < NosingNormalZ && across.LengthSquared() > 1e-6f)
        {
            across = Vector2.Normalize(across);
            n = Vector2.Dot(across, u) >= 0f ? across : -across;
            return true;
        }
        var (a3, b3, c3) = level.Corners(t);
        Span<Vector2> v = [new(a3.X, a3.Y), new(b3.X, b3.Y), new(c3.X, c3.Y)];
        var at = new Vector2(touch.X, touch.Y);
        float best = EdgeNearest;
        bool found = false;
        for (int i = 0; i < 3; i++)
        {
            var p0 = v[i]; var p1 = v[(i + 1) % 3]; var q = v[(i + 2) % 3];
            var e = p1 - p0;
            float el2 = e.LengthSquared();
            if (el2 < 1e-10f) continue;
            var nn = Vector2.Normalize(new Vector2(e.Y, -e.X));
            if (Vector2.Dot(nn, q - p0) > 0f) nn = -nn;
            if (Vector2.Dot(nn, u) <= 1e-3f) continue;
            var c = p0 + e * Math.Clamp(Vector2.Dot(at - p0, e) / el2, 0f, 1f);
            float d = Vector2.Distance(at, c);
            if (d > best) continue;
            best = d; n = nn; p = c; found = true;
        }
        return found;
    }

    /// <summary>
    /// Lowering to hang from the edge at <paramref name="edge"/> (its outward way <paramref name="edgeOut"/>, its drop
    /// <paramref name="drop"/>): when the hands can hold it, read from where the body would hang, and a hanging body fits there.
    /// </summary>
    static bool TryLowerToHang(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                               Vector3 edge, Vector2 edgeOut, float drop, ref MotorOutcome o)
    {
        float below = float.IsPositiveInfinity(drop) ? float.NegativeInfinity : edge.Z - drop;
        float off = m.R + 0.01f;
        var from = new Vector3(edge.X + edgeOut.X * off, edge.Y + edgeOut.Y * off, MathF.Max(edge.Z - loco.StandingReach, below + Skin));
        if (!TraversalProbe.Read(m.Level, from, -edgeOut, loco, m.R + GrabReach, edge.Z + 0.3f - from.Z, out var lip)
            || !lip.HasTop || !lip.Holdable || MathF.Abs(lip.Top - edge.Z) > LipMatch) return false;
        // Hanging at arm's length below the lip; or, where that is below the ground (a drop less than the hands' reach),
        // at the foot standing (climbing down, ClimbsDown), a radius out from the face.
        var hang = new Vector3(lip.Lip.X + lip.LipNormal.X * off, lip.Lip.Y + lip.LipNormal.Y * off, MathF.Max(lip.Top - loco.StandingReach, below + Skin));
        if (!Fits(m.Level, hang, loco.Radius, loco.Height)) return false;
        var at = feet;
        if (!Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.LowerToHang, lip, hang, LowerSeconds, ref o)) return false;
        // The move's height is the drop below the edge (the obstacle's top above the start, as for the other
        // moves, is the edge itself: the log once said "an obstacle of -0.00 m"); +∞ for no ground within DropLook.
        move.Height = drop;
        o.Grabbed = true;
        o.GripLip = lip.Lip;
        o.GripFeet = at;
        o.GripHeight = lip.Top - at.Z;
        return true;
    }

    /// <summary>
    /// The end of a lowering that climbs down (<see cref="ClimbsDown"/>): the body stands at the foot (crouched
    /// while crouch is still held, as anywhere), the hands let go of the lip, and the outcome says it climbed down.
    /// </summary>
    static void EndClimbDown(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        o.ClimbedDown = true;
        o.ClimbFrom = move.From;
        o.ClimbLip = move.Lip;
        o.ClimbDrop = move.Height;
        move = default;
        SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
    }

    /// <summary>Cancel: the hands let go of a lip; a move in its first moments undone, the body put back where it started, standing.</summary>
    static void CancelMove(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        o.Cancelled = true;
        if (move.Kind is TraversalKind.Hang or TraversalKind.Grab)
        {
            StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
            LetGo(m, ref body, ref move, feet, ref o);
            return;
        }
        o.MoveEnded = move.Kind; o.MoveCompleted = false;
        feet = move.From;
        move = default;
        // A move taken from a jump started in the air: back onto the ground under it, within a hop.
        var stand = new Mover(m.Level, m.Others, m.Self, loco, loco.Height);
        float reach = loco.JumpHeight + HopTuck + 0.05f;
        if (Probe(stand, feet, reach + m.R + Skin, keeping: false, out var s) && s.FeetZ >= feet.Z - reach && s.FeetZ <= feet.Z + 1e-3f) feet.Z = s.FeetZ;
        SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
    }
}
