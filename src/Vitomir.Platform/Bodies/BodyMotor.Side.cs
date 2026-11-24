// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository (CONVENTIONS, Notes, traverse.side_step): squeezing
// sideways through a gap narrower than the body, and in stealth the back to a wall in cover.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>squeezing and cover</b> on the models repository's clips (<see cref="SideRules"/>; the player's, riding on the walls). The capsule follows the clips' paths
/// laid from where each begins (the ledge's state, <see cref="LedgeMove"/>: its clip, time, place and the way the body faces, <see cref="LedgeMove.In"/>) and the
/// side's (<see cref="SideMove"/>).
/// <list type="bullet">
/// <item><b>Squeezing</b>: walking into a gap between two upright faces narrower than the body's capsule but at least the clips' <c>gap_min_m</c>, straight
/// (no bend), open at its far end and with room overhead for the body standing: a quarter turn on the spot (<c>turn_left_90</c> or <c>_right_90</c>, its root's
/// turn carried by the facing) to face the nearer wall, the body laid onto the gap's line as it turns (its capsule's middle <c>(body_front_m − body_back_m)/2</c>
/// behind the gap's middle, so the skin is centred in it); then the side-steps (<c>squeeze_left</c>, <c>_right</c>, whole cycles from the narrow stance, the
/// capsule slimmed to the gap's least: <c>gap_min_m</c>/2 less a centimetre, centred on the skin's middle) while the move keys point along the gap either way,
/// <c>squeeze_idle</c> held when they let go; out the far side (or back the way in) where the body's own capsule fits again, the quarter turn back to face the
/// way it went, standing. A gap too narrow, one that bends, one with low headroom, or walked into crouched, is refused and said once a place.</item>
/// <item><b>Cover</b>: in stealth, standing (not crouched) still for <see cref="SideRules.CoverDwell"/> or backing, with an upright wall behind within
/// <see cref="SideRules.CoverReach"/> of the back: <c>cover_in</c> laid onto the real wall (its end's capsule <c>wall_back_m</c> off it, the start's difference
/// and the facing's taken out over its first 0.6), <c>cover_idle</c>; the side's wish side-steps along it (<c>cover_left</c>, <c>_right</c>) while the wall
/// goes on behind (at the end, or a corner, a stop said once: no turning round them); forward, stealth off, crouch or cancel, or the wall gone: <c>cover_out</c>.</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>The fade between a side-step's loop and its idle, and from a squeeze's quarter turn into them (s: the 0.2 s for the head the models repository asks).</summary>
    public const float SideBlend = 0.2f;

    /// <summary>The ledge's fade code for <see cref="SideBlend"/> (<see cref="LedgeMove.Fade"/>).</summary>
    public const int SideFade = 6;

    /// <summary>How long a gap is looked along for its far end (m); one longer is refused.</summary>
    public const float SqueezeLongest = 8f;

    /// <summary>How far a gap's walls may wander from straight and parallel along it (m), and turn (the cosine) before it is a bend.</summary>
    const float SqueezeTrue = 0.05f, SqueezeParallel = 0.985f;

    /// <summary>How far within the gap's way the wish must point to walk into it (the cosine, 35°), and to step along it (the share).</summary>
    const float SqueezeCone = 0.82f, SideCone = 0.5f;

    /// <summary>How fast a side-step's rate is brought to the one asked (a second): from the way along to back the other way in a fifth of a second.</summary>
    public const float SideSlew = 10f;

    /// <summary>How long the body's speed is carried into an idle as a side-step stops (s): the inertialized join's (1 cm past the stance at a stop's 0.9 m/s).</summary>
    const float StopCarry = 0.1f;

    /// <summary>The rate <c>cover_in</c> is run back at to standing when the way into cover is let go of.</summary>
    const float LetGoRate = 1.5f;

    /// <summary>How far into <c>cover_out</c> it is begun (s): its first tenth of a second is still.</summary>
    const float CoverOutFrom = 0.1f;

    /// <summary>How much wider than the body's own capsule the room must be for the quarter turn out of a squeeze while the way out is held (m: its arms).</summary>
    const float SqueezeTurnRoom = 0.08f;

    /// <summary>The squeeze's and the cover's capsule a centimetre inside the clips' reach (m).</summary>
    const float SideClear = 0.01f;

    /// <summary>The share of <c>cover_in</c> over which its start's difference and its facing's are taken out.</summary>
    const float CoverLay = 0.6f;

    /// <summary>The heights a gap's walls are looked for at (m over the floor), the ways into it beside the body's middle line (m), and a wall behind's heights.</summary>
    static readonly float[] SideHeights = [0.5f, 1.0f, 1.5f], SideOffsets = [0f, 0.1f, -0.1f, 0.2f, -0.2f], BackHeights = [0.9f, 1.4f];

    // ---------------------------------------------------------------- the capsule

    /// <summary>
    /// The capsule of a squeeze's or a cover's clip: its radius and how far ahead of the clip's capsule middle it is centred (m) — a squeeze's from its skin's reach
    /// (<c>body_front_m</c>, <c>body_back_m</c>) and its gap (<c>gap_min_m</c>), a cover's from its reach ahead and its wall behind (<c>wall_back_m</c>); the body's own
    /// (<paramref name="r"/>, 0) for a turn.
    /// </summary>
    static (float R, float Fwd) SideCapsule(LedgePaths paths, LedgeClip c, float r)
    {
        if (c.Role == LedgeRole.SideTurn) return (r, 0f);
        var notes = paths.Side(c.SideKind, LedgeRole.SideIdle) ?? c;
        float front = notes.SideFront, back = notes.SideBack;
        if (float.IsNaN(front) || float.IsNaN(back)) return (r, 0f);
        float half = 0.5f * (front + back);
        if (c.SideKind == 1 && !float.IsNaN(notes.GapMin)) half = MathF.Min(half, 0.5f * notes.GapMin);
        return (MathF.Max(0.05f, MathF.Min(r, half - SideClear)), 0.5f * (front - back));
    }

    /// <summary>Whether the side's capsule moving from <paramref name="from"/> to <paramref name="to"/> (the feet) meets anything, and how far it gets.</summary>
    static bool SideSwept(in Mover m, in Locomotion loco, LedgePaths paths, LedgeClip c, Vector2 inward, Vector3 from, Vector3 to, out float share)
    {
        var (r, fwd) = SideCapsule(paths, c, m.R);
        var off = new Vector3(inward * fwd, Skin);
        return HangSwept(m, loco, r, loco.Height, from + off, to + off, out share);
    }

    /// <summary>Whether the side's capsule fits at <paramref name="feet"/>.</summary>
    static bool SideFits(CollisionWorld level, in Locomotion loco, LedgePaths paths, LedgeClip c, Vector2 inward, Vector3 feet)
    {
        var (r, fwd) = SideCapsule(paths, c, loco.Radius);
        return Fits(level, feet + new Vector3(inward * fwd, Skin), r, loco.Height);
    }

    /// <summary>Whether a side clip's path from its start is clear (<see cref="LedgeSamples"/> pieces).</summary>
    static bool SidePathClear(in Mover m, in Locomotion loco, LedgePaths paths, in LedgeMove l, in SideMove s, LedgeClip c, Vector3 feet)
    {
        var prev = feet;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float t = c.Duration * i / LedgeSamples;
            var p = LedgePoint(l, c, t, t);
            if (SideSwept(m, loco, paths, c, l.In, prev, p, out _)) return false;
            prev = p;
        }
        return true;
    }

    /// <summary>A refusal told, once a place (<see cref="SideMove.Told"/>): not again for the same reason within half a metre of where it was last told.</summary>
    static void SideRefuse(ref SideMove s, ref MotorOutcome o, SideAct act, SideRefusal why, Vector3 at, float width = float.NaN)
    {
        if (why == SideRefusal.None || (s.Told == why && Vector3.DistanceSquared(s.ToldAt, at) < 0.25f)) return;
        s.Told = why;
        s.ToldAt = at;
        o.SideRefused = why;
        o.SideRefusedAct = act;
        o.SideAt = at;
        o.SideWidth = width;
    }

    /// <summary>A side move left: its state cleared but for what is told (and a quarter turn out's clip, while its fade lasts).</summary>
    static void SideLeave(ref SideMove s, int turned = 0) => s = new SideMove { Told = s.Told, ToldAt = s.ToldAt, Turned = turned, Rearm = s.Kind == 2 ? (byte)1 : (byte)0 };

    // ---------------------------------------------------------------- the gap

    /// <summary>A gap found ahead: its way along, its middle at the mouth (the floor's height), its least width, the near wall's side and the refusal if any.</summary>
    struct GapPlan
    {
        public Vector2 Axis;
        public Vector3 Mouth;
        public float Width;
        public float Ahead;
    }

    /// <summary>The walls either side of <paramref name="p"/> across <paramref name="across"/> (a level unit) at three heights: the least width, the middle's offset along it, the left wall's level normal.</summary>
    static bool Walls(CollisionWorld level, Vector3 p, Vector2 across, float reach, out float width, out float middle, out Vector2 leftNormal)
    {
        width = float.PositiveInfinity;
        middle = 0f;
        leftNormal = default;
        float sumMid = 0f;
        int found = 0;
        foreach (float z in SideHeights)
        {
            var o = new Vector3(p.X, p.Y, p.Z + z);
            if (level.Ray(o, new Vector3(across, 0f), reach) is not { } l || level.Ray(o, new Vector3(-across, 0f), reach) is not { } r) return false;
            if (MathF.Abs(l.Normal.Z) > 0.35f || MathF.Abs(r.Normal.Z) > 0.35f) return false;
            var ln = Vector2.Normalize(new Vector2(l.Normal.X, l.Normal.Y));
            var rn = Vector2.Normalize(new Vector2(r.Normal.X, r.Normal.Y));
            if (Vector2.Dot(ln, -across) < SqueezeParallel || Vector2.Dot(rn, across) < SqueezeParallel) return false;
            width = MathF.Min(width, l.Distance + r.Distance);
            sumMid += 0.5f * (l.Distance - r.Distance);
            leftNormal += ln;
            found++;
        }
        middle = sumMid / found;
        leftNormal = Vector2.Normalize(leftNormal);
        return true;
    }

    /// <summary>
    /// A gap ahead along <paramref name="way"/> from <paramref name="feet"/> narrower than the body (<paramref name="loco"/>'s radius twice): where its mouth is,
    /// its way along and its least width; <paramref name="why"/> what refuses it (too narrow for <paramref name="gapMin"/>, a bend or a closed end, low headroom, no
    /// way out within <see cref="SqueezeLongest"/>). False with nothing there.
    /// </summary>
    static bool FindGap(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float gapMin, out GapPlan plan, out SideRefusal why)
    {
        plan = default;
        why = SideRefusal.None;
        float r = loco.Radius;
        var left0 = LeftOf(way);
        // A way into it: the body's middle line ahead, or one a little to either side, clear for a body's length at the hips and the shoulders.
        Vector3 inside = default;
        bool any = false;
        foreach (float side in SideOffsets)
        {
            var start = feet + new Vector3(left0 * side, 0f);
            bool clear = true;
            foreach (float z in SideHeights)
                if (level.Ray(start + new Vector3(0f, 0f, z), new Vector3(way, 0f), r + 0.55f) is not null) { clear = false; break; }
            if (!clear) continue;
            var p = start + new Vector3(way * (r + 0.25f), 0f);
            if (!Walls(level, p, left0, r + 0.1f, out float w, out _, out _) || w >= 2f * r - 0.02f) continue;
            inside = p;
            any = true;
            break;
        }
        if (!any) return false;
        Walls(level, inside, left0, r + 0.1f, out float w0, out float mid0, out var ln0);
        // The gap's way: along its walls, the way the body goes.
        var axis = LeftOf(ln0);
        if (Vector2.Dot(axis, way) < 0f) axis = -axis;
        if (Vector2.Dot(axis, way) < SqueezeCone) return false;
        var across = LeftOf(axis);
        var mid = inside + new Vector3(LeftOf(way) * mid0, 0f);
        mid = new Vector3(mid.X, mid.Y, feet.Z);
        // Back to the mouth (where a wall ends), every 2 cm.
        float back = 0f;
        for (; back < 1.5f; back += 0.02f)
            if (!Walls(level, mid - new Vector3(axis * (back + 0.02f), 0f), across, 0.5f * w0 + 0.15f, out _, out _, out _)) break;
        var mouth = mid - new Vector3(axis * back, 0f);
        float ahead = Vector2.Dot(new Vector2(mouth.X - feet.X, mouth.Y - feet.Y), axis);
        // Along it to its far end, every 0.1 m: straight, parallel, of one width, open overhead, not closed.
        float least = w0;
        float baseMid = 0f;
        bool ended = false;
        for (float s = 0.02f; s <= SqueezeLongest; s += 0.1f)
        {
            var p = mouth + new Vector3(axis * s, 0f);
            if (!Walls(level, p, across, 0.5f * w0 + 0.15f, out float w, out float m, out var ln))
            {
                // A way out only with nothing ahead within a body's width (at the hips and the shoulders, its middle and either side); else the gap
                // turns there (a bend: one wall goes on into another across the way).
                var c0 = p + new Vector3(across * baseMid, 0f);
                foreach (float z in BackHeights)
                    foreach (float side in SideOffsets)
                        if (MathF.Abs(side) <= 0.1f && level.Ray(c0 + new Vector3(across * side, z), new Vector3(axis, 0f), 2f * r + 0.1f) is not null) { why = SideRefusal.Bend; break; }
                if (why != SideRefusal.None) break;
                ended = true;
                plan.Width = least;
                break;
            }
            if (w >= 2f * r) { ended = true; break; }
            if (s < 0.05f) baseMid = m;
            if (MathF.Abs(w - w0) > SqueezeTrue || MathF.Abs(m - baseMid) > SqueezeTrue || MathF.Abs(Vector2.Dot(LeftOf(ln), axis)) < SqueezeParallel) { why = SideRefusal.Bend; break; }
            least = MathF.Min(least, w);
            var c = p + new Vector3(across * m, 0f);
            // Closed ahead (the gap turns or ends in a wall) at the hips or the shoulders.
            if (level.Ray(c + new Vector3(0f, 0f, 1.0f), new Vector3(axis, 0f), 0.1f) is not null || level.Ray(c + new Vector3(0f, 0f, 0.5f), new Vector3(axis, 0f), 0.1f) is not null)
            { why = SideRefusal.Bend; break; }
            // Room overhead for the body standing.
            if (level.Ray(c + new Vector3(0f, 0f, 0.3f), Vector3.UnitZ, loco.Height - 0.3f + 0.02f) is not null) { why = SideRefusal.Low; break; }
        }
        plan = new GapPlan { Axis = axis, Mouth = mouth + new Vector3(across * baseMid, 0f), Width = least, Ahead = ahead };
        if (why != SideRefusal.None) return true;
        if (!ended) { why = SideRefusal.Long; return true; }
        if (least < gapMin - 0.005f) why = SideRefusal.Narrow;
        return true;
    }

    /// <summary>
    /// <b>walking into a gap</b> narrower than the body (<see cref="FindGap"/>): the quarter turn on the spot into the squeeze begun at its mouth, the body laid
    /// onto the gap's line as it turns. False (a refusal said once a place) for none.
    /// </summary>
    static bool TrySqueeze(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, SideRules rules, ref Vector3 feet,
                           in Locomotion loco, Vector2 wish, float wl, ref MotorOutcome o)
    {
        if (wl < 0.5f || paths.Side(1, LedgeRole.SideIdle) is not { } idle) return false;
        var way = wish / wl;
        if (!FindGap(m.Level, feet, way, loco, idle.GapMin, out var gap, out var why)) return false;
        // Only at the mouth: the body's skin within a few centimetres of it (it walks up to it first); further out by as far as its speed carries it on into
        // the turn and its arms swing ahead (a run's hand reached 3 cm into the mouth's corner otherwise), 3 cm for each metre a second.
        float speed = new Vector2(body.Velocity.X, body.Velocity.Y).Length();
        float carried = speed >= 0.05f ? BrakeReach + 0.03f * MathF.Min(speed, 5.5f) : 0f;
        if (gap.Ahead > loco.Radius + 0.12f + carried) return false;
        if (why == SideRefusal.None && body.Crouched != 0) why = SideRefusal.Crouched;
        if (why != SideRefusal.None) { SideRefuse(ref s, ref o, SideAct.SqueezeIn, why, gap.Mouth, gap.Width); return false; }
        // The near wall: the side of the gap's middle the body is on (by its facing when it is on the middle).
        var across = LeftOf(gap.Axis);
        float offset = Vector2.Dot(new Vector2(feet.X - gap.Mouth.X, feet.Y - gap.Mouth.Y), across);
        float turned = DetMath.WrapAngle(body.Yaw - DetMath.Atan2(gap.Axis.Y, gap.Axis.X));
        int turnWay = offset > 0.01f ? 1 : offset < -0.01f ? -1 : turned >= 0f ? 1 : -1;
        var inward = across * turnWay;   // facing the near wall
        if (paths.Side(1, LedgeRole.SideTurn, turnWay) is not { } turn) return false;
        var (_, fwd) = SideCapsule(paths, idle, loco.Radius);
        var place = gap.Mouth - new Vector3(gap.Axis * (loco.Radius + 2f * SideClear + carried), 0f) - new Vector3(inward * fwd, 0f);
        place.Z = feet.Z;
        if (!Fits(m.Level, place + new Vector3(0f, 0f, Skin), loco.Radius, loco.Height)) { SideRefuse(ref s, ref o, SideAct.SqueezeIn, SideRefusal.NoRoom, gap.Mouth, gap.Width); return false; }
        var next = new LedgeMove { Clip = paths.IndexOf(turn) + 1, Time = 0f, Age = 0f, Place = place, In = inward, Top = float.NaN, Floor = feet.Z, Fade = 1 };
        next.Delta = feet - place;
        float turnFor = turn.Duration / Math.Clamp(rules.TurnRate, 1f, 2f);   // the turn at its rate (s of the body's)
        (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, turnFor, 0f, turnFor);
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = new Traversal { Kind = TraversalKind.Squeeze, Seconds = float.PositiveInfinity, From = feet, To = place, Lip = gap.Mouth, LipNormal = new Vector3(-gap.Axis, 0f), Height = 0f };
        l = next;
        float yaw0 = body.Yaw;
        s = new SideMove
        {
            Kind = 1, Phase = 1, Axis = gap.Axis, Mid = gap.Mouth, Width = gap.Width, Yaw0 = yaw0,
            YawEnd = DetMath.WrapAngle(DetMath.Atan2(inward.Y, inward.X) - (yaw0 + turn.Turn)), EndFrom = 0f, EndTo = turn.Duration, Told = s.Told, ToldAt = s.ToldAt,
        };
        Carry(ref s, body);
        o.MoveStarted = TraversalKind.Squeeze;
        o.SideStarted = SideAct.SqueezeIn;
        o.SideWay = turnWay;
        o.SideFrom = feet;
        o.SideWidth = gap.Width;
        BeginSide(ref body);
        SideAdvance(m, ref body, ref move, ref l, paths, turn, ref s, ref feet, loco, ref o, first: true);
        return true;
    }

    /// <summary>The body's state for a side move under way.</summary>
    static void BeginSide(ref Body body)
    {
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0; body.Powered = 0;
        body.GroundSeconds = 0f;
    }

    // ---------------------------------------------------------------- cover

    /// <summary>
    /// The wall behind a body at <paramref name="feet"/> facing <paramref name="facing"/> within <paramref name="reach"/> of its middle: upright at the hips and the
    /// shoulders, one face looking along the facing within 25°; its point (at the floor's height) and its outward level normal.
    /// </summary>
    static bool WallBehind(CollisionWorld level, Vector3 feet, Vector2 facing, float reach, out Vector3 point, out Vector2 outward)
    {
        point = default;
        outward = default;
        if (level.Ray(feet + new Vector3(0f, 0f, 0.9f), new Vector3(-facing, 0f), reach) is not { } hip || MathF.Abs(hip.Normal.Z) > 0.35f) return false;
        var n = Vector2.Normalize(new Vector2(hip.Normal.X, hip.Normal.Y));
        if (Vector2.Dot(n, facing) < 0.9f) return false;
        if (level.Ray(feet + new Vector3(0f, 0f, 1.4f), new Vector3(-facing, 0f), reach + 0.1f) is not { } chest || MathF.Abs(chest.Normal.Z) > 0.35f
            || MathF.Abs(Vector2.Dot(new Vector2(chest.Point.X - hip.Point.X, chest.Point.Y - hip.Point.Y), n)) > 0.03f) return false;
        point = new Vector3(hip.Point.X, hip.Point.Y, feet.Z);
        outward = n;
        return true;
    }

    /// <summary>Whether the cover's wall goes on behind a capsule at <paramref name="place"/> (its back at <paramref name="back"/> m off it), across <paramref name="half"/> m either side at the hips and the shoulders.</summary>
    static bool WallGoesOn(CollisionWorld level, Vector3 place, Vector2 outward, float back, float half)
    {
        var across = LeftOf(outward);
        for (int k = 0; k < 3; k++)
            foreach (float z in BackHeights)
            {
                float a = k == 0 ? 0f : k == 1 ? half : -half;
                var o = place + new Vector3(across * a, z);
                if (level.Ray(o, new Vector3(-outward, 0f), back + 0.08f) is not { } hit || MathF.Abs(hit.Normal.Z) > 0.35f
                    || MathF.Abs(hit.Distance - back) > 0.05f) return false;
            }
        return true;
    }

    /// <summary>
    /// <b>a body in stealth taking cover</b>: standing still for <see cref="SideRules.CoverDwell"/> or backing, a wall behind within <see cref="SideRules.CoverReach"/>
    /// of its back: <c>cover_in</c> laid onto it. False for none (the still time kept in <paramref name="s"/>).
    /// </summary>
    static bool TryCover(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, SideRules rules, ref Vector3 feet,
                         in Locomotion loco, Vector2 wish, float wl, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        if (paths.Side(2, LedgeRole.CoverIn) is not { } cin || paths.Side(2, LedgeRole.SideIdle) is not { } idle || float.IsNaN(cin.WallBack)) { s.Still = 0f; return false; }
        var facing = Facing(body.Yaw);
        bool backing = wl > 0.5f && Vector2.Dot(wish / wl, facing) < -0.7f;
        bool still = wl < 0.2f && new Vector2(body.Velocity.X, body.Velocity.Y).Length() < 0.25f;
        s.Still = still ? s.Still + dt : 0f;
        // Out of cover, standing still there does not take it again: the body moves first (or backs into it).
        if (!still) s.Rearm = 0;
        if (!backing && (s.Rearm != 0 || s.Still < rules.CoverDwell - 1e-4f)) return false;
        if (!WallBehind(m.Level, feet, facing, loco.Radius + Math.Clamp(rules.CoverReach, 0.05f, 0.6f), out var wall, out var outward)) return false;
        float back = cin.WallBack;
        // The end: the cover's capsule wall_back_m off the real wall, where the body is along it; the clip laid so its path ends there.
        float off = Vector2.Dot(new Vector2(feet.X - wall.X, feet.Y - wall.Y), outward);
        var end = feet - new Vector3(outward * (off - back), 0f);
        var (endF, _, _) = cin.At(cin.Duration);
        var (startF, _, _) = cin.At(0f);
        var place = end - new Vector3(outward * (endF - startF), 0f);
        if (!WallGoesOn(m.Level, end, outward, back, 0.18f)) { SideRefuse(ref s, ref o, SideAct.CoverIn, SideRefusal.WallEnds, wall); return false; }
        if (!SideFits(m.Level, loco, paths, idle, outward, end)) { SideRefuse(ref s, ref o, SideAct.CoverIn, SideRefusal.NoRoom, wall); return false; }
        var next = new LedgeMove { Clip = paths.IndexOf(cin) + 1, Time = 0f, Age = 0f, Place = place, In = outward, Top = float.NaN, Floor = feet.Z, Fade = 1 };
        next.Delta = feet - LedgeRaw(next, cin, 0f);
        (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, CoverLay * cin.Duration, 0f, CoverLay * cin.Duration);
        var plan = new SideMove
        {
            Kind = 2, Phase = 2, Wall = wall, Out = outward, Yaw0 = body.Yaw, YawEnd = DetMath.WrapAngle(DetMath.Atan2(outward.Y, outward.X) - body.Yaw),
            EndFrom = 0f, EndTo = CoverLay * cin.Duration, Told = s.Told, ToldAt = s.ToldAt,
        };
        Carry(ref plan, body);
        if (!SidePathClear(m, loco, paths, next, plan, cin, feet)) { SideRefuse(ref s, ref o, SideAct.CoverIn, SideRefusal.Blocked, wall); return false; }
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = new Traversal { Kind = TraversalKind.Cover, Seconds = float.PositiveInfinity, From = feet, To = end, Lip = wall, LipNormal = new Vector3(outward, 0f), Height = 0f };
        l = next;
        s = plan;
        o.MoveStarted = TraversalKind.Cover;
        o.SideStarted = SideAct.CoverIn;
        o.SideFrom = feet;
        BeginSide(ref body);
        SideAdvance(m, ref body, ref move, ref l, paths, cin, ref s, ref feet, loco, ref o, first: true);
        return true;
    }

    // ---------------------------------------------------------------- a tick

    /// <summary>How far the body's way into a side move may carry it past the clip's start before it is at rest (m).</summary>
    const float BrakeReach = 0.04f;

    /// <summary>The body's speed at a side move's start carried into it (<see cref="SideMove.Brake"/>), brought to rest by <see cref="BrakeReach"/>.</summary>
    static void Carry(ref SideMove s, in Body body)
    {
        var v = new Vector3(body.Velocity.X, body.Velocity.Y, 0f);
        float speed = v.Length();
        if (speed < 0.05f) { s.Brake = Vector3.Zero; s.BrakeFor = 0f; return; }
        s.Brake = v;
        s.BrakeFor = Math.Clamp(BrakeReach / (speed * 4f / 27f), 0.05f, 0.3f);   // the Hermite's velocity term reaches 4/27 of the speed times its time
    }

    /// <summary>
    /// The carried speed's share of the way at <paramref name="age"/> s: the velocity's part of the earlier build's inertialized join (<see cref="Inertial"/>, Holden's cubic;
    /// the place's difference is <see cref="LedgeMove.Delta"/>'s smoothstep, its other part), the speed at its start and nought, at rest, at its end. Its clock is a
    /// tick ahead of the clip's: the tick the move begins already carries the body on by its speed (else it stands still for that tick, a pop of the whole speed).
    /// </summary>
    static Vector3 Braked(in SideMove s, float age) => s.BrakeFor <= 0f ? Vector3.Zero : Inertial.Offset(Vector3.Zero, s.Brake, age + World.TickSeconds, s.BrakeFor);

    /// <summary>
    /// A tick of a side clip: its time at <paramref name="rate"/> (a turn's at the turns' rate; a side-step's backward, toward its start, when the way is
    /// turned round or let go of nearer its start; an idle round its loop), the feet on its path (swept: met by something, the body stands there), the facing.
    /// </summary>
    static bool SideAdvance(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref SideMove s, ref Vector3 feet,
                            in Locomotion loco, ref MotorOutcome o, bool first = false, float rate = 1f)
    {
        const float dt = World.TickSeconds;
        if (!first)
        {
            l.Age += dt;
            l.Time += dt * rate;
            if (c.Role == LedgeRole.SideIdle) { if (l.Time >= c.Duration) l.Time -= c.Duration; }
            else if (l.Time > c.Duration - 1e-4f) l.Time = c.Duration;
            else if (l.Time < 1e-4f) l.Time = 0f;
        }
        var p = LedgePoint(l, c, l.Time, l.Age) + (c.Role is LedgeRole.SideTurn or LedgeRole.CoverIn or LedgeRole.SideIdle ? Braked(s, l.Age) : Vector3.Zero);
        if (c.Role is LedgeRole.SideStep or LedgeRole.CoverIn or LedgeRole.CoverOut && SideSwept(m, loco, paths, c, l.In, feet, p, out float share))
        {
            feet += (p - feet) * share;
            l = default;
            SideLeave(ref s);
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return false;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        body.Yaw = c.Role is LedgeRole.SideTurn or LedgeRole.CoverIn
            ? DetMath.WrapAngle(s.Yaw0 + c.TurnAt(l.Time) + s.YawEnd * Window(l.Time, s.EndFrom, s.EndTo))
            : DetMath.WrapAngle(DetMath.Atan2(l.In.Y, l.In.X));
        move.From = feet; move.To = feet;
        return true;
    }

    /// <summary>
    /// <b>one tick of a body squeezing or in cover</b> (a side clip carries the capsule): the quarter turns (at <see cref="SideRules.TurnRate"/>), the
    /// steps' cycles (none locked in — the way kept runs a cycle on, the way turned round runs it back toward its start, let go it runs at
    /// <see cref="SideRules.StopRate"/> to its nearer end, both the narrow stance; the next at either end while the way holds), the idles and their controls,
    /// <c>cover_in</c> to the idle (let go of until its contact), out to standing. <paramref name="stealth"/>: whether the body is in stealth (out of it,
    /// cover ends). The keys' way is kept <see cref="SideRules.Hold"/> after they let go, so a quick change from one way to the other is not a stop.
    /// </summary>
    static void SideTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref SideMove s, SideRules rules, bool stealth,
                         ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        body.AirSeconds = 0f;
        int held = HeldWay(ref s, s.Kind == 1 ? SqueezeWay(intent, s.Axis, l.In) : CoverWay(intent, l.In), rules);
        switch (c.Role)
        {
            case LedgeRole.SideTurn:
                if (!SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o, rate: Math.Clamp(rules.TurnRate, 1f, 2f))) return;
                if (l.Time < c.Duration - 1e-4f) return;
                if (s.Phase == 3) { SideOff(m, ref body, ref move, ref l, ref s, ref feet, loco, intent, ref o, turned: c.Clip + 1); return; }
                s.Phase = 2;
                SqueezeNext(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, held, ref o);
                return;
            case LedgeRole.SideStep:
            {
                // In cover, a wish to leave it: the cycle to its nearer end, then out.
                int ask = s.Kind == 2 && CoverLeaving(intent, l.In, stealth) ? 0 : held;
                float stop = Math.Clamp(rules.StopRate, 1f, 3f);
                // Let go: to the end it reaches sooner, where the cycle is and the way it is going now (its rate turned at SideSlew), chosen once.
                if (ask != 0) s.Stopping = 0;
                else if (s.Stopping == 0)
                {
                    float on = (c.Duration - l.Time) / stop + MathF.Abs(stop - s.Rate) / (2f * SideSlew), back = l.Time / stop + MathF.Abs(stop + s.Rate) / (2f * SideSlew);
                    s.Stopping = on <= back ? (sbyte)1 : (sbyte)-1;
                }
                float want = ask == c.Way ? 1f : ask == -c.Way ? -1f : s.Stopping * stop;
                s.Rate = s.Rate < want ? MathF.Min(want, s.Rate + SideSlew * World.TickSeconds) : MathF.Max(want, s.Rate - SideSlew * World.TickSeconds);
                if (!SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o, rate: s.Rate)) return;
                bool atEnd = l.Time >= c.Duration - 1e-4f;
                if (!atEnd && l.Time > 1e-4f) return;
                l.Place = LedgeRaw(l, c, atEnd ? c.Duration : 0f);
                l.Delta = Vector3.Zero;
                if (s.Kind == 1) SqueezeNext(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, held, ref o);
                else CoverNext(m, ref body, ref move, ref l, paths, ref s, stealth, ref feet, loco, intent, held, ref o);
                return;
            }
            case LedgeRole.SideIdle:
                if (s.Kind == 1) SqueezeNext(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, held, ref o);
                else CoverNext(m, ref body, ref move, ref l, paths, ref s, stealth, ref feet, loco, intent, held, ref o);
                return;
            case LedgeRole.CoverIn:
                // The way into cover let go of until the back meets the wall (its contact): forward, stealth off, crouch or cancel; the body walks on from there.
                if (s.Phase != 4 && !float.IsNaN(c.ContactS) && l.Time < c.ContactS && CoverLeaving(intent, l.In, stealth))
                {
                    s.Phase = 4;
                    s.Rate = 1f;
                    o.Cancelled = intent.Cancel;
                    o.SideStarted = SideAct.CoverLetGo;
                    o.SideFrom = feet;
                }
                if (s.Phase == 4)
                {
                    // Run back to its start (the stand), its rate turned round at SideSlew, then standing there.
                    s.Rate = MathF.Max(-LetGoRate, s.Rate - SideSlew * World.TickSeconds);
                    if (!SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o, rate: s.Rate)) return;
                    if (l.Time > 1e-4f) return;
                    SideOff(m, ref body, ref move, ref l, ref s, ref feet, loco, intent, ref o, turned: 0);
                    return;
                }
                if (!SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                l.Place = LedgeRaw(l, c, c.Duration);
                l.Delta = Vector3.Zero;
                s.Yaw0 = body.Yaw;
                ToSideIdle(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, ref o, fade: false);
                return;
            case LedgeRole.CoverOut:
                if (!SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                SideOff(m, ref body, ref move, ref l, ref s, ref feet, loco, intent, ref o, turned: 0);
                return;
            default:
                l = default;
                SideLeave(ref s);
                Abort(m, ref body, ref move, ref feet, loco, ref o);
                return;
        }
    }

    /// <summary>The keys' way kept for <see cref="SideRules.Hold"/> after they let go (one way straight to the other is taken at once).</summary>
    static int HeldWay(ref SideMove s, int raw, SideRules rules)
    {
        if (raw != 0) { s.Held = raw; s.HeldFor = Math.Clamp(rules.Hold, 0f, 0.25f); return raw; }
        if (s.Held != 0)
        {
            s.HeldFor -= World.TickSeconds;
            if (s.HeldFor > 1e-5f) return s.Held;
            s.Held = 0;
            s.HeldFor = 0f;
        }
        return 0;
    }

    /// <summary>The way along a squeeze's gap the keys ask, as the body faces the near wall (+1 to its left, −1 its right, 0 none).</summary>
    static int SqueezeWay(in MoveIntent intent, Vector2 axis, Vector2 inward)
    {
        int along = AlongGap(intent, axis);
        return along == 0 ? 0 : Vector2.Dot(axis * along, LeftOf(inward)) > 0f ? 1 : -1;
    }

    /// <summary>The way along a cover's wall the keys ask, the body's back to it (+1 to its left, −1 its right, 0 none or more forward than aside).</summary>
    static int CoverWay(in MoveIntent intent, Vector2 outward)
    {
        var d = intent.Direction;
        if (d.LengthSquared() <= 0.09f) return 0;
        float dl = Vector2.Dot(d, LeftOf(outward)) / d.Length(), df = Vector2.Dot(d, outward) / d.Length();
        return MathF.Abs(dl) > SideCone && MathF.Abs(dl) >= df ? (dl > 0f ? 1 : -1) : 0;
    }

    /// <summary>Whether a body in cover (or on its way into it) asks to leave: forward (away from the wall), stealth off, crouch or cancel.</summary>
    static bool CoverLeaving(in MoveIntent intent, Vector2 outward, bool stealth)
    {
        if (!stealth || intent.Crouch || intent.Cancel) return true;
        var d = intent.Direction;
        if (d.LengthSquared() <= 0.09f) return false;
        float dl = Vector2.Dot(d, LeftOf(outward)) / d.Length(), df = Vector2.Dot(d, outward) / d.Length();
        return df > SideCone && df > MathF.Abs(dl);
    }

    /// <summary>The idle at the place (a turn, a cycle and <c>cover_in</c> end on its stance), from its first frame; faded in when <paramref name="fade"/>.</summary>
    static void ToSideIdle(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, ref Vector3 feet, in Locomotion loco,
                           ref MotorOutcome o, bool fade)
    {
        var idle = paths.Side(s.Kind, LedgeRole.SideIdle)!;
        var place = l.Place;
        var inward = l.In;
        l = new LedgeMove { Clip = paths.IndexOf(idle) + 1, Time = 0f, Age = 0f, Place = place, In = inward, Top = float.NaN, Floor = float.NegativeInfinity, Fade = fade ? SideFade : 0 };
        s.Way = 0;
        s.Rate = 0f;
        // The body's speed as it comes to the stance carried on and brought to rest (the inertialized join), not stopped in a tick.
        var v = new Vector3(body.Velocity.X, body.Velocity.Y, 0f);
        (s.Brake, s.BrakeFor) = v.LengthSquared() > 0.0025f ? (v, StopCarry) : (Vector3.Zero, 0f);
        SideAdvance(m, ref body, ref move, ref l, paths, idle, ref s, ref feet, loco, ref o, first: true);
    }

    /// <summary>A side clip begun at the place from its first frame (faded in over <see cref="SideBlend"/>; <paramref name="fade"/> false: none).</summary>
    static void SideBegin(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref SideMove s, ref Vector3 feet, in Locomotion loco,
                          ref MotorOutcome o, bool fade = true, float at = 0f)
    {
        var place = l.Place;
        var inward = l.In;
        l = new LedgeMove { Clip = paths.IndexOf(c) + 1, Time = at, Age = 0f, Place = place, In = inward, Top = float.NaN, Floor = float.NegativeInfinity, Fade = fade ? SideFade : 0 };
        l.Delta = feet - LedgeRaw(l, c, at);
        s.Rate = 1f;
        s.Stopping = 0;
        (l.AcrossFrom, l.AcrossTo, l.UpFrom, l.UpTo) = (0f, SideBlend, 0f, SideBlend);
        SideAdvance(m, ref body, ref move, ref l, paths, c, ref s, ref feet, loco, ref o, first: true);
    }

    /// <summary>The way along the gap the wish asks (+1 along <see cref="SideMove.Axis"/>, −1 back, 0 none).</summary>
    static int AlongGap(in MoveIntent intent, Vector2 axis)
    {
        var d = intent.Direction;
        if (d.LengthSquared() < 0.09f) return 0;
        float a = Vector2.Dot(d, axis) / d.Length();
        return a > SideCone ? 1 : a < -SideCone ? -1 : 0;
    }

    /// <summary>
    /// In a squeeze at a stance (the turn's end, a cycle's, the idle): out where the body's own capsule fits again (once in the gap; or back from its mouth), with
    /// the quarter turn to face the way it went; else the step the wish asks along the gap (its path swept), or the idle.
    /// </summary>
    static void SqueezeNext(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, ref Vector3 feet, in Locomotion loco,
                            int held, ref MotorOutcome o)
    {
        bool fits = Fits(m.Level, feet + new Vector3(0f, 0f, Skin), loco.Radius, loco.Height);
        if (!fits) s.Inside = 1;
        var left = LeftOf(l.In);
        int along = held == 0 ? 0 : Vector2.Dot(left * held, s.Axis) > 0f ? 1 : -1;   // the keys' way kept (SideRules.Hold)
        // The way along the axis the last cycle went (none yet: into the gap).
        int went = s.Way != 0 ? MathF.Sign(Vector2.Dot(left * s.Way, s.Axis)) : 1;
        if (fits && (s.Inside != 0 || along < 0))
        {
            var to = s.Inside != 0 ? s.Axis * went : -s.Axis;
            int turnWay = Vector2.Dot(to, left) > 0f ? 1 : -1;
            // With the way out still held, a step more where the turn's arms would reach a wall's end (the body's own capsule fits, one SqueezeTurnRoom wider not).
            bool onward = along != 0 && Vector2.Dot(s.Axis * along, to) > 0f && !Fits(m.Level, feet + new Vector3(0f, 0f, Skin), loco.Radius + SqueezeTurnRoom, loco.Height)
                          && paths.Side(1, LedgeRole.SideStep, turnWay) is { } onStep
                          && SidePathClear(m, loco, paths, new LedgeMove { Clip = paths.IndexOf(onStep) + 1, Place = feet, In = l.In, Floor = float.NegativeInfinity }, s, onStep, feet);
            if (!onward && paths.Side(1, LedgeRole.SideTurn, turnWay) is { } turn)
            {
                var place = feet;
                var inward = l.In;
                l = new LedgeMove { Clip = paths.IndexOf(turn) + 1, Time = 0f, Age = 0f, Place = place, In = inward, Top = float.NaN, Floor = float.NegativeInfinity, Fade = SideFade };
                s.Phase = 3;
                s.Brake = Vector3.Zero;
                s.BrakeFor = 0f;
                s.Yaw0 = body.Yaw;
                s.YawEnd = DetMath.WrapAngle(DetMath.Atan2(to.Y, to.X) - (body.Yaw + turn.Turn));
                (s.EndFrom, s.EndTo) = (0f, turn.Duration);
                s.Way = 0;
                o.SideStarted = SideAct.SqueezeOut;
                o.SideWay = turnWay;
                o.SideFrom = feet;
                o.SideWidth = s.Width;
                SideAdvance(m, ref body, ref move, ref l, paths, turn, ref s, ref feet, loco, ref o, first: true);
                return;
            }
        }
        if (along != 0)
        {
            int way = Vector2.Dot(s.Axis * along, left) > 0f ? 1 : -1;
            if (paths.Side(1, LedgeRole.SideStep, way) is { } step)
            {
                var trial = new LedgeMove { Clip = paths.IndexOf(step) + 1, Place = feet, In = l.In, Floor = float.NegativeInfinity };
                if (SidePathClear(m, loco, paths, trial, s, step, feet))
                {
                    bool chained = paths.At(l.Clip - 1) is { Role: LedgeRole.SideStep };   // either way, each end of a cycle the narrow stance
                    l.Place = feet;
                    s.Way = way;
                    o.SideStarted = SideAct.SqueezeStep;
                    o.SideWay = way;
                    o.SideFrom = feet;
                    o.SideWidth = s.Width;
                    SideBegin(m, ref body, ref move, ref l, paths, step, ref s, ref feet, loco, ref o, fade: !chained);
                    return;
                }
                SideRefuse(ref s, ref o, SideAct.SqueezeStep, SideRefusal.Blocked, feet, s.Width);
            }
        }
        if (paths.At(l.Clip - 1) is { Role: LedgeRole.SideIdle } idling) { SideAdvance(m, ref body, ref move, ref l, paths, idling, ref s, ref feet, loco, ref o); return; }
        l.Place = feet;
        ToSideIdle(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, ref o, fade: true);
    }

    /// <summary>
    /// In cover at a stance (a cycle's end, the idle): out (<c>cover_out</c>) with the wish forward, out of stealth, crouch or cancel, or the wall gone from behind;
    /// else the side's wish steps along it while it goes on (at its end a stop, said once), or the idle.
    /// </summary>
    static void CoverNext(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, bool stealth, ref Vector3 feet,
                          in Locomotion loco, in MoveIntent intent, int held, ref MotorOutcome o)
    {
        var idle = paths.Side(2, LedgeRole.SideIdle)!;
        var left = LeftOf(l.In);
        float back = idle.WallBack;
        bool gone = !WallGoesOn(m.Level, feet, l.In, back, 0f);
        if (gone || CoverLeaving(intent, l.In, stealth))
        {
            if (paths.Side(2, LedgeRole.CoverOut) is { } cout)
            {
                l.Place = feet;
                var trial = new LedgeMove { Clip = paths.IndexOf(cout) + 1, Place = feet, In = l.In, Floor = float.NegativeInfinity };
                var (endF, _, _) = cout.At(cout.Duration);
                if (SidePathClear(m, loco, paths, trial, s, cout, feet) && Fits(m.Level, feet + new Vector3(l.In * endF, Skin), loco.Radius, loco.Height))
                {
                    o.Cancelled = intent.Cancel;
                    o.SideStarted = SideAct.CoverOut;
                    o.SideFrom = feet;
                    s.Way = 0;
                    // From a little in, where it is under way (its first tenth of a second barely moves: out in 4 frames, not 10).
                    SideBegin(m, ref body, ref move, ref l, paths, cout, ref s, ref feet, loco, ref o, fade: paths.At(l.Clip - 1) is not { Role: LedgeRole.SideIdle } || l.Time > 1e-3f,
                              at: MathF.Min(CoverOutFrom, 0.25f * cout.Duration));
                    return;
                }
                SideRefuse(ref s, ref o, SideAct.CoverOut, SideRefusal.NoRoom, feet);
            }
        }
        else if (held != 0)
        {
            int way = held;   // the keys' way kept (SideRules.Hold)
            if (paths.Side(2, LedgeRole.SideStep, way) is { } step)
            {
                var end = feet + new Vector3(left * (way * step.Step), 0f);
                var trial = new LedgeMove { Clip = paths.IndexOf(step) + 1, Place = feet, In = l.In, Floor = float.NegativeInfinity };
                if (!WallGoesOn(m.Level, end, l.In, back, 0.18f)) SideRefuse(ref s, ref o, SideAct.CoverStep, SideRefusal.WallEnds, end);
                else if (!SidePathClear(m, loco, paths, trial, s, step, feet)) SideRefuse(ref s, ref o, SideAct.CoverStep, SideRefusal.Blocked, end);
                else
                {
                    bool chained = paths.At(l.Clip - 1) is { Role: LedgeRole.SideStep };   // either way, each end of a cycle the narrow stance
                    l.Place = feet;
                    s.Way = way;
                    o.SideStarted = SideAct.CoverStep;
                    o.SideWay = way;
                    o.SideFrom = feet;
                    SideBegin(m, ref body, ref move, ref l, paths, step, ref s, ref feet, loco, ref o, fade: !chained);
                    return;
                }
            }
        }
        if (paths.At(l.Clip - 1) is { Role: LedgeRole.SideIdle } idling) { SideAdvance(m, ref body, ref move, ref l, paths, idling, ref s, ref feet, loco, ref o); return; }
        l.Place = feet;
        ToSideIdle(m, ref body, ref move, ref l, paths, ref s, ref feet, loco, ref o, fade: true);
    }

    /// <summary>Out of a squeeze or out of cover, standing: the move ended, the body on the ground under it (its own capsule).</summary>
    static void SideOff(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, ref SideMove s, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                        ref MotorOutcome o, int turned)
    {
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        move = default;
        l = default;
        SideLeave(ref s, turned);
        var still = intent;
        still.Crouch = false;
        SetDown(m, ref body, ref feet, loco, still, Vector3.Zero);
        body.Velocity = Vector3.Zero;
    }

    /// <summary>
    /// On the ground, before the move: a gap walked into squeezed through, and in stealth a wall behind taken as cover (BodySystem's
    /// <c>stealth</c>). True when one began.
    /// </summary>
    static bool TrySide(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref SideMove s, SideRules rules, bool stealth,
                        ref Vector3 feet, in Locomotion loco, Vector2 wish, float wl, ref MotorOutcome o)
    {
        if (s.Turned != 0 && (s.TurnedSeconds += World.TickSeconds) > 2f * SideBlend) { s.Turned = 0; s.TurnedSeconds = 0f; }
        if (body.State != BodyState.Ground || l.Clip != 0 || l.Pending != 0) { s.Still = 0f; return false; }
        if (rules.Squeeze && paths.HasSqueeze && TrySqueeze(m, ref body, ref move, ref l, paths, ref s, rules, ref feet, loco, wish, wl, ref o)) return true;
        if (rules.Cover && stealth && body.Crouched == 0 && paths.HasCover) return TryCover(m, ref body, ref move, ref l, paths, ref s, rules, ref feet, loco, wish, wl, ref o);
        s.Still = 0f;
        return false;
    }
}
