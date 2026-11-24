// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (CONVENTIONS, Notes, traverse.leap,
// traverse.corner, turn_deg, root_turns): the hang's leaps and corners.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>the hang's corners and leaps</b> on the models repository's clips (<see cref="HangRules"/>; the player's, riding on tier 1's moves):
/// <list type="bullet">
/// <item><b>Corners</b>: the shimmy's wish into a corner of the lip — outside (the face turns away) or inside (the other face comes toward the climber),
/// within <see cref="HangRules.AngleTolerance"/> of a quarter turn — goes round it on <c>hang_corner_*</c> where it can be taken: the corner within half a
/// shimmy's step beyond the clip's own <c>corner_along_m</c> (the cycle that reaches it) and no nearer than <see cref="HangRules.CornerSlack"/> short of it, the
/// other face's lip on past the end's hang (<c>ends_along_m</c>) by half the hands' spacing, a wall under the end's edge for a braced corner and none for a
/// free one, the hang fitting there and the body's room through the turn (its path swept by the radius the clip itself keeps from its own corner,
/// <see cref="LedgeClip.Room"/>). The clip is laid with its own corner on the real one: the start's difference along the lip is taken out as the hands go
/// to the corner, the end's (the real lip's place and turn against the clip's) as the body comes round. One that cannot be taken is told why and the shimmy
/// stops there as before. After it the hang on the other face, the shimmy going on while the wish that went in holds (<see cref="HangMove.Carry"/>).</item>
/// <item><b>Leaps</b> from the braced hang (or a braced shimmy's cycle), each where its target is: a holdable lip under both hands' places, in
/// the same face's plane (within 0.1 m) for the side, up and down leaps, square behind for the leap back, with a wall under it and room to hang, its middle
/// within <see cref="HangRules.LeapTolerance"/> of the clip's own offset (the nearest such), and the clip's path clear. The clip is laid from the hang and
/// warped onto the target between its push (<c>push_s</c>) and the leading hand's contact (<c>contact_s</c>), as the grabs are onto the real edge. None
/// from a free hang (the clips push off a wall with the feet).</item>
/// <item><b>The turn</b> (the corners, the leap back: <c>turn_deg</c>, <c>root_turns</c>): the path is in the clip's first frame's axes; the body's facing
/// carries the root's turn as it plays (<see cref="LedgeClip.TurnAt"/>) and the drawn pose has the root's own taken out, so after it the capsule faces
/// <c>turn_deg</c> further round (and the real lip's way, the difference over the end's window).</item>
/// <item><b>The capsule</b> follows the clip's path (the ledge clips' rule), swept as it goes: met by something, the body falls from there.</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How far a lip's face may stand off the hang's face's plane for a leap that keeps the facing (m: <see cref="LipGoesOn"/>'s own).</summary>
    public const float LeapPlane = 0.1f;

    /// <summary>Whether the wish that went round a corner still holds (within about 30° of it): the shimmy's way (<see cref="HangMove.Carry"/>), not forward or back; else forgotten.</summary>
    static bool Carried(ref HangMove h, in MoveIntent intent)
    {
        if (h.Carry == 0) return false;
        float wl = intent.Direction.Length();
        if (wl > 0.3f && Vector2.Dot(intent.Direction / wl, h.Wish) > 0.85f) return true;
        h.Carry = 0;
        h.Wish = Vector2.Zero;
        return false;
    }

    /// <summary>The share of a window (<paramref name="from"/> to <paramref name="to"/>) by <paramref name="t"/>, eased.</summary>
    static float Window(float t, float from, float to) => to > from + 1e-5f ? Smooth((t - from) / (to - from)) : t >= to ? 1f : 0f;

    /// <summary>Where the feet are in a leap or a corner at <paramref name="t"/> s of it (<paramref name="age"/> s after it began): its path laid, the start's difference and the end's taken out.</summary>
    static Vector3 HangPoint(in LedgeMove l, in HangMove h, LedgeClip c, float t, float age) => LedgePoint(l, c, t, age) + h.End * Window(t, h.EndFrom, h.EndTo);

    /// <summary>The body's facing at <paramref name="t"/> s of a leap or a corner: its start's, the clip's turn so far, the end's difference over its window.</summary>
    static float HangYaw(in HangMove h, LedgeClip c, float t) => DetMath.WrapAngle(h.Yaw0 + c.TurnAt(t) + h.YawEnd * Window(t, h.EndFrom, h.EndTo));

    /// <summary>
    /// Whether a body of radius <paramref name="radius"/> and <paramref name="height"/> moving from <paramref name="from"/> to <paramref name="to"/> meets anything
    /// (the level, another body), and how far it gets (a share).
    /// </summary>
    static bool HangSwept(in Mover m, in Locomotion loco, float radius, float height, Vector3 from, Vector3 to, out float share)
    {
        share = 1f;
        var d = to - from;
        float len = d.Length();
        if (len < 1e-7f) return false;
        var thin = loco;
        thin.Radius = MathF.Min(loco.Radius, radius);
        var mover = new Mover(m.Level, m.Others, m.Self, thin, height);
        if (!Sweep(mover, from, d / len, len + Skin, out var hit) || hit.Distance >= len - 1e-4f) return false;
        share = MathF.Max(0f, hit.Distance - Skin) / len;
        return true;
    }

    /// <summary>The body's radius through a leap or a corner: its own, or a corner's room (the clip's own clearance from its corner).</summary>
    static float HangRadius(in Mover m, LedgeClip c) => c.Role == LedgeRole.Corner && !float.IsNaN(c.Room) ? MathF.Min(m.R, c.Room) : m.R;

    /// <summary>The level's top under <paramref name="xy"/> between <paramref name="high"/> and <paramref name="low"/> (world Z): a face looking up met going down, past anything else; NaN for none.</summary>
    static float TopUnder(CollisionWorld level, Vector2 xy, float high, float low, out int triangle)
    {
        triangle = -1;
        float z = high;
        for (int i = 0; i < 4 && z > low; i++)
        {
            if (level.Ray(new Vector3(xy, z), -Vector3.UnitZ, z - low + 1e-3f) is not { } hit) return float.NaN;
            if (hit.Normal.Z >= 0.7f && hit.Point.Z >= low - 1e-4f) { triangle = hit.Triangle; return hit.Point.Z; }
            z = hit.Point.Z - 1e-3f;
        }
        return float.NaN;
    }

    /// <summary>
    /// A holdable lip near <paramref name="mid"/> (a lip's point at about the height <paramref name="top"/>) whose face looks out along <paramref name="n"/>: its
    /// middle within <paramref name="tolAlong"/> along the lip (<paramref name="along"/>, the nearest first, 2 cm apart) and its top within <paramref name="tolUp"/>
    /// of <paramref name="mid"/>'s, its face within <see cref="LeapPlane"/> of <paramref name="mid"/>'s plane (a level ray in at it just under the top looked for),
    /// the lip going on under both hands (<paramref name="half"/> either side; <see cref="LipGoesOn"/>: a level top, not to be climbed, an upright face under
    /// it). The lip's point on its face at its top, the face's outward level normal and the top.
    /// </summary>
    /// <remarks>From the <paramref name="k"/>th place (0: the clip's own; then 2 cm either side in turn), the place found left in it (for the next to be tried after it).</remarks>
    static bool LipNear(CollisionWorld level, Vector3 mid, Vector2 along, Vector2 n, float half, float tolAlong, float tolUp, ref int k, out Vector3 lip, out Vector2 normal, out float top)
    {
        lip = default; normal = default; top = float.NaN;
        var n3 = new Vector3(n, 0f);
        var a3 = new Vector3(along, 0f);
        int steps = (int)MathF.Floor(tolAlong / 0.02f + 1e-4f);
        for (; k <= 2 * steps; k++)
        {
            float d = (k + 1) / 2 * 0.02f * (k % 2 == 1 ? 1f : -1f);
            var p = mid + a3 * d;
            // The face: a level ray in at it just under the top looked for (and lower, for a top lower than that), within the plane's tolerance.
            for (int pass = 0; pass < 2; pass++)
            {
                float zr = pass == 0 ? mid.Z - 0.06f : mid.Z - tolUp + 0.02f;
                if (level.Ray(new Vector3(p.X + n.X * 0.3f, p.Y + n.Y * 0.3f, zr), -n3, 0.3f + LeapPlane + 0.02f) is not { } face || face.Distance < 0.3f - LeapPlane - 0.02f) continue;
                var fn = new Vector2(face.Normal.X, face.Normal.Y);
                float fl = fn.Length();
                if (fl < 1e-4f || Vector2.Dot(fn / fl, n) < 0.9f || MathF.Abs(face.Normal.Z) > 0.35f) continue;
                var inside = new Vector2(face.Point.X - n.X * 0.03f, face.Point.Y - n.Y * 0.03f);
                float z = TopUnder(level, inside, mid.Z + tolUp + 0.02f, mid.Z - tolUp - 0.02f, out int tri);
                if (float.IsNaN(z) || z < zr - 1e-3f || level.NoClimb(tri, new Vector3(inside, z))) continue;
                var at = new Vector3(face.Point.X, face.Point.Y, z);
                if (!LipGoesOn(level, at + a3 * half, z, n3) || !LipGoesOn(level, at - a3 * half, z, n3)) continue;
                lip = at;
                normal = fn / fl;
                top = z;
                return true;
            }
        }
        return false;
    }

    /// <summary>A refusal told (the log says it): which, why, about where.</summary>
    static void Refuse(ref MotorOutcome o, HangMoveKind kind, HangLeap leap, bool outside, int way, HangRefusal why, Vector3 at)
    {
        o.HangRefused = why;
        o.HangRefusedKind = kind;
        o.HangLeap = leap;
        o.HangOutside = outside;
        o.HangWay = way;
        o.HangAt = at;
    }

    // ---------------------------------------------------------------- leaps

    /// <summary>
    /// A leap asked for from the hang (or a shimmy's cycle) — <paramref name="which"/>: to a side, up, down or back — begun where its target is (see the
    /// class's notes); false (nothing changed, the refusal told) where it is not, for the control's own fallback.
    /// </summary>
    static bool TryLeap(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco, HangLeap which,
                        ref MotorOutcome o, ref HangMove h, HangRules hr)
    {
        var cur = paths.At(l.Clip - 1);
        // Where the hang's middle is now (a shimmy's cycle under way: along it).
        var place = l.Place;
        if (cur is { Role: LedgeRole.Shimmy }) place += new Vector3(LeftOf(l.In) * cur.At(l.Time).Left, 0f);
        var in1 = l.In;
        var left1 = LeftOf(in1);
        var n1 = -in1;
        var e1 = new Vector3(place.X + in1.X * paths.EdgeFwd, place.Y + in1.Y * paths.EdgeFwd, l.Top);
        if (l.Braced == 0) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.FreeHang, e1); return false; }
        var c = which switch
        {
            HangLeap.Left => paths.LeapOf(1, 0), HangLeap.Right => paths.LeapOf(-1, 0), HangLeap.Up => paths.LeapOf(0, 1), HangLeap.Down => paths.LeapOf(0, -1),
            _ => paths.LeapOf(0, 0, back: true),
        };
        if (c is null) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.NoClip, e1); return false; }
        float tol = hr.LeapTolerance;
        float half = c.HandsApart > 0f ? 0.5f * c.HandsApart : ShimmyHand;
        // Where its lip is looked for: the clip's own offset (the leap back's, square behind on the facing face found there).
        Vector3 expect, mid;
        Vector2 alongT, nT;
        if (which != HangLeap.Back)
        {
            expect = mid = e1 + new Vector3(left1 * c.LeapLeft, c.LeapUp);
            (alongT, nT) = (left1, n1);
        }
        else
        {
            expect = e1 - new Vector3(in1 * c.LeapBack, 0f);
            var from = new Vector3(e1.X - in1.X * 0.3f, e1.Y - in1.Y * 0.3f, l.Top - tol - 0.05f);
            if (m.Level.Ray(from, new Vector3(-in1, 0f), MathF.Max(0.1f, c.LeapBack + tol - 0.3f)) is not { } back
                || MathF.Abs(0.3f + back.Distance - c.LeapBack) > tol) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.NoLip, expect); return false; }
            var bn = new Vector2(back.Normal.X, back.Normal.Y);
            float bl = bn.Length();
            if (bl < 1e-4f || Vector2.Dot(bn / bl, in1) < DetMath.Cos(hr.AngleTolerance)) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.Angle, expect); return false; }
            nT = bn / bl;
            alongT = LeftOf(-nT);
            mid = new Vector3(back.Point.X, back.Point.Y, l.Top);
        }
        // The nearest place with both hands on a lip, a wall under it and room to hang.
        Vector3 lip = default, target = default;
        Vector2 n2 = default, in2 = default;
        float top = float.NaN;
        var why = HangRefusal.NoLip;
        var tried = expect;
        bool found = false;
        for (int k = 0; LipNear(m.Level, mid, alongT, nT, half, tol, tol, ref k, out lip, out n2, out top); k++)
        {
            target = HangPlace(paths, lip, new Vector3(n2, 0f), top);
            in2 = -n2;
            tried = lip;
            if (!WallUnder(m.Level, paths, target, in2)) { why = HangRefusal.NoWall; continue; }
            if (!HangFits(m.Level, loco, target, lip, new Vector3(n2, 0f))) { why = HangRefusal.NoRoom; continue; }
            found = true;
            break;
        }
        if (!found) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, why, tried); return false; }
        // A side leap only where the lip breaks between the hands and the target (a gap at least the tolerance wide, or a different lip).
        if (hr.Breaks && which is HangLeap.Left or HangLeap.Right && !LipBreaks(m.Level, e1, l.Top, new Vector3(n1, 0f), alongT * MathF.Sign(c.LeapLeft), half, lip, top, tol))
        {
            Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.Continuous, lip);
            return false;
        }
        var n23 = new Vector3(n2, 0f);
        var hang = paths.Of(LedgeRole.Hang, braced: true) ?? paths.Of(LedgeRole.Hang, braced: false)!;
        var next = l;
        next.Place = place;
        Switch(ref next, paths, c, 0f, feet, LedgeBlend, fade: true);
        next.Way = 0;
        next.Want = 0;
        next.Floor = float.NegativeInfinity;
        var ends = new LedgeMove { Place = target, In = in2 };
        var plan = new HangMove
        {
            Kind = HangMoveKind.Leap, Way = (int)which,
            End = LedgeRaw(ends, hang, 0f) - LedgeRaw(next, c, c.Duration),
            EndFrom = float.IsNaN(c.PushS) ? 0f : c.PushS, EndTo = float.IsNaN(c.ContactS) ? c.Duration : c.ContactS,
            Yaw0 = DetMath.Atan2(in1.Y, in1.X), Place = target, In = in2, Top = top, Lip = lip, LipNormal = n23, Braced = hang.Braced ? 1 : 0,
        };
        plan.YawEnd = DetMath.WrapAngle(DetMath.Atan2(in2.Y, in2.X) - (plan.Yaw0 + c.TurnAt(c.Duration)));
        if (!PathClear(m, loco, next, plan, c, feet, 0f)) { Refuse(ref o, HangMoveKind.Leap, which, false, 0, HangRefusal.Blocked, lip); return false; }
        StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
        l = next;
        h = plan;
        body.State = BodyState.Traversing;
        o.HangStarted = HangMoveKind.Leap;
        o.HangLeap = which;
        o.HangBraced = true;
        o.HangFrom = feet;
        o.HangTo = LedgeRaw(ends, hang, 0f);
        o.HangLip = lip;
        HangMoveTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, ref o, ref h, advance: true);
        return true;
    }

    /// <summary>
    /// Whether the lip <b>breaks</b> between the hang's leading hand (<paramref name="half"/> along <paramref name="way"/> from its middle
    /// <paramref name="e1"/>, its top <paramref name="top1"/>, its face's outward normal <paramref name="n1"/>) and the target's near hand: the target a different lip
    /// (its top or its face more than 5 cm off this one's), or a stretch at least <paramref name="gap"/> long between where the lip does not go on (the test, every
    /// 2 cm).
    /// </summary>
    static bool LipBreaks(CollisionWorld level, Vector3 e1, float top1, Vector3 n1, Vector2 way, float half, Vector3 lip2, float top2, float gap)
    {
        if (MathF.Abs(top2 - top1) > 0.05f || MathF.Abs(Vector3.Dot(lip2 - e1, n1)) > 0.05f) return true;
        var w3 = new Vector3(way, 0f);
        float to = Vector2.Dot(new Vector2(lip2.X - e1.X, lip2.Y - e1.Y), way) - half;
        float run = 0f;
        for (float s = half; s <= to + 1e-4f; s += 0.02f)
        {
            if (LipGoesOn(level, e1 + w3 * s, top1, n1)) run = 0f;
            else if ((run += 0.02f) >= gap - 1e-4f) return true;
        }
        return false;
    }

    /// <summary>Whether a leap's or a corner's path from <paramref name="t0"/> s of it is clear (<see cref="LedgeSamples"/> pieces, the body tucked, of its radius).</summary>
    static bool PathClear(in Mover m, in Locomotion loco, in LedgeMove l, in HangMove h, LedgeClip c, Vector3 feet, float t0)
    {
        float r = HangRadius(m, c), height = TuckedHeight(loco);
        var prev = feet;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float t = t0 + (c.Duration - t0) * i / LedgeSamples;
            var p = HangPoint(l, h, c, t, l.Age + (t - t0));
            if (HangSwept(m, loco, r, height, prev, p, out _)) return false;
            prev = p;
        }
        return true;
    }

    // ---------------------------------------------------------------- corners

    /// <summary>
    /// In the hang or at a shimmy's cycle's end (<paramref name="fromCycleEnd"/>, <paramref name="carry"/> s into the next), the wish holding
    /// <paramref name="way"/> (+1 left): a corner of the lip that way within its clip's reach (see the class's notes) begun, true; none within reach, or one
    /// that cannot be taken (told why), false — the shimmy (<paramref name="shimmy"/>, its cycle's step for the reach) as before.
    /// </summary>
    static bool TryCorner(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip shimmy, ref Vector3 feet, in Locomotion loco,
                          int way, bool fromCycleEnd, float carry, in MoveIntent intent, ref MotorOutcome o, ref HangMove h, HangRules hr)
    {
        _ = intent;
        bool braced = l.Braced != 0;
        var outsideClip = paths.CornerOf(true, braced, way);
        var insideClip = paths.CornerOf(false, braced, way);
        if (outsideClip is null && insideClip is null) return false;
        var in1 = l.In;
        var along = LeftOf(in1) * way;
        var a3 = new Vector3(along, 0f);
        var n1 = new Vector3(-in1, 0f);
        float top = l.Top;
        var e1 = new Vector3(l.Place.X + in1.X * paths.EdgeFwd, l.Place.Y + in1.Y * paths.EdgeFwd, top);
        float halfStep = 0.5f * MathF.Abs(shimmy.At(shimmy.Duration).Left);
        float reach = MathF.Max(outsideClip?.CornerAlong ?? 0f, insideClip?.CornerAlong ?? 0f) + halfStep + 0.02f;
        // Inside: the other face across the lip's line ahead, under the lip (a level ray just out from the face).
        float dIn = float.PositiveInfinity;
        Vector2 nIn = default;
        Vector3 hitIn = default;
        if (insideClip is not null
            && m.Level.Ray(new Vector3(e1.X - in1.X * 0.05f, e1.Y - in1.Y * 0.05f, top - 0.1f), a3, reach) is { } across && MathF.Abs(across.Normal.Z) < 0.35f)
        {
            var an = new Vector2(across.Normal.X, across.Normal.Y);
            if (an.Length() > 1e-4f) { dIn = across.Distance; nIn = Vector2.Normalize(an); hitIn = across.Point; }
        }
        // Outside: where the lip stops going on (2 cm, then halved four times).
        float dOut = float.PositiveInfinity;
        if (outsideClip is not null)
            for (float d = 0f; d <= MathF.Min(reach, dIn) + 1e-4f; d += 0.02f)
                if (!LipGoesOn(m.Level, e1 + a3 * d, top, n1))
                {
                    float lo = MathF.Max(0f, d - 0.02f), hi = d;
                    if (d > 0f)
                        for (int i = 0; i < 4; i++)
                        {
                            float mid = 0.5f * (lo + hi);
                            if (LipGoesOn(m.Level, e1 + a3 * mid, top, n1)) lo = mid; else hi = mid;
                        }
                    dOut = 0.5f * (lo + hi);
                    break;
                }
        bool outside = dOut < dIn - 0.05f;   // an inside corner's other face stops the lip's test at it too
        float dist = outside ? dOut : dIn;
        if (float.IsInfinity(dist)) return false;   // the lip goes on: the shimmy
        var c = outside ? outsideClip : insideClip;
        if (c is null) return false;
        if (dist - c.CornerAlong > halfStep + 1e-3f) return false;   // not yet: the cycle that reaches it
        var at = e1 + a3 * dist;
        if (dist < c.CornerAlong - hr.CornerSlack - 1e-3f) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.TooNear, at); return false; }
        // The other face: its normal (outside: a level ray in at it just round the corner; inside: the one met), within the angle of a quarter turn.
        Vector2 n2;
        Vector3 on2;
        if (outside)
        {
            var probe = at + new Vector3(in1 * 0.15f, 0f) + a3 * 0.3f + new Vector3(0f, 0f, -0.08f);
            if (m.Level.Ray(probe, -a3, 0.45f) is not { } face || face.Distance < 0.1f) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.ShortLip, at); return false; }
            var fn = new Vector2(face.Normal.X, face.Normal.Y);
            if (fn.Length() < 1e-4f) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.ShortLip, at); return false; }
            n2 = Vector2.Normalize(fn);
            on2 = face.Point;
            if (Vector2.Dot(n2, along) < DetMath.Cos(hr.AngleTolerance)) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.Angle, at); return false; }
        }
        else
        {
            n2 = nIn;
            on2 = hitIn;
            if (Vector2.Dot(n2, -along) < DetMath.Cos(hr.AngleTolerance)) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.Angle, at); return false; }
        }
        // The corner: where the two lips' lines meet.
        var d2 = new Vector2(-n2.Y, n2.X);
        if (outside ? Vector2.Dot(d2, in1) < 0f : Vector2.Dot(d2, in1) > 0f) d2 = -d2;   // along the other lip away from the corner
        float den = along.X * d2.Y - along.Y * d2.X;
        if (MathF.Abs(den) < 0.3f) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.Angle, at); return false; }
        var w = new Vector2(on2.X - e1.X, on2.Y - e1.Y);
        float t1 = (w.X * d2.Y - w.Y * d2.X) / den;
        var corner = new Vector3(e1.X + along.X * t1, e1.Y + along.Y * t1, top);
        dist = t1;
        if (dist < c.CornerAlong - hr.CornerSlack - 1e-3f) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.TooNear, corner); return false; }
        // The other lip on past the end's hang by half the hands' spacing.
        var n23 = new Vector3(n2, 0f);
        var d23 = new Vector3(d2, 0f);
        float half = c.HandsApart > 0f ? 0.5f * c.HandsApart : ShimmyHand;
        for (float s = 0.06f; ; s = MathF.Min(s + 0.1f, c.EndsAlong + half))
        {
            if (!LipGoesOn(m.Level, corner + d23 * s, top, n23)) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.ShortLip, corner); return false; }
            if (s >= c.EndsAlong + half - 1e-4f) break;
        }
        var end = corner + d23 * c.EndsAlong;
        var endInside = new Vector2(end.X - n2.X * 0.04f, end.Y - n2.Y * 0.04f);
        float top2 = TopUnder(m.Level, endInside, top + 0.15f, top - 0.15f, out _);
        if (float.IsNaN(top2)) top2 = top;
        var lip2 = new Vector3(end.X, end.Y, top2);
        var target = HangPlace(paths, lip2, n23, top2);
        var in2 = -n2;
        bool wall = WallUnder(m.Level, paths, target, in2);
        if (c.Braced && !wall) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.NoWall, corner); return false; }
        if (!c.Braced && wall) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.Wall, corner); return false; }
        if (!HangFits(m.Level, loco, target, lip2, n23)) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.NoRoom, corner); return false; }
        // Laid with its own corner on the real one: the start's difference along the lip out as the hands go to the corner (the turn's first 5 %, 0.3 s at
        // the least, half the clip at the most), the end's as the body comes round (from half the turn).
        float turnIn = 0.3f, turnHalf = 0.5f * c.Duration;
        for (float t = 0f; t <= c.Duration; t += World.TickSeconds)
            if (MathF.Abs(c.TurnAt(t)) >= 0.05f * MathF.Abs(c.Turn)) { turnIn = Math.Clamp(t, 0.3f, 0.5f * c.Duration); break; }
        for (float t = 0f; t <= c.Duration; t += World.TickSeconds)
            if (MathF.Abs(c.TurnAt(t)) >= 0.5f * MathF.Abs(c.Turn)) { turnHalf = t; break; }
        var next = l;
        next.Place = l.Place + a3 * (dist - c.CornerAlong);
        float t0 = fromCycleEnd ? carry : 0f;
        Switch(ref next, paths, c, t0, feet, turnIn, fade: !fromCycleEnd);   // its age from nought: the start's difference out over its first turnIn s
        next.Way = way;
        next.Want = 0;
        var hang = paths.Of(LedgeRole.Hang, c.Braced) ?? paths.Of(LedgeRole.Hang, !c.Braced)!;
        var ends = new LedgeMove { Place = target, In = in2 };
        var plan = new HangMove
        {
            Kind = HangMoveKind.Corner, Way = way,
            End = LedgeRaw(ends, hang, 0f) - LedgeRaw(next, c, c.Duration), EndFrom = MathF.Min(turnHalf, c.Duration - World.TickSeconds), EndTo = c.Duration,
            Yaw0 = DetMath.Atan2(in1.Y, in1.X), Place = target, In = in2, Top = top2, Lip = lip2, LipNormal = n23, Braced = hang.Braced ? 1 : 0,
        };
        plan.YawEnd = DetMath.WrapAngle(DetMath.Atan2(in2.Y, in2.X) - (plan.Yaw0 + c.TurnAt(c.Duration)));
        if (!PathClear(m, loco, next, plan, c, feet, t0)) { Refuse(ref o, HangMoveKind.Corner, HangLeap.None, outside, way, HangRefusal.Blocked, corner); return false; }
        l = next;
        h = plan;
        o.HangStarted = HangMoveKind.Corner;
        o.HangOutside = outside;
        o.HangWay = way;
        o.HangBraced = c.Braced;
        o.HangFrom = feet;
        o.HangTo = LedgeRaw(ends, hang, 0f);
        o.HangLip = corner;
        // Its first tick: from the hang, a tick on; from a cycle's end, where the carried time has it.
        HangMoveTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, ref o, ref h, advance: !fromCycleEnd);
        return true;
    }

    // ---------------------------------------------------------------- a tick

    /// <summary>
    /// <b>a tick of a leap or a corner</b>: along its path (the start's and the end's differences out), swept as it goes, the facing carrying its turn;
    /// at its end the hang at the new lip from its first frame (a brace when the wall there says otherwise), and after a corner the shimmy going on while the
    /// wish holds. Neither is let go once begun (committed, as a catch).
    /// </summary>
    static void HangMoveTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                             in MoveIntent intent, ref MotorOutcome o, ref HangMove h, HangRules hr)
    {
        _ = hr;
        if (h.Kind == HangMoveKind.None)
        {
            // Its state lost (nothing begun it): it falls from where it is.
            l = default;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        HangMoveTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, ref o, ref h, advance: true, intent.Direction);
    }

    static void HangMoveTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                             ref MotorOutcome o, ref HangMove h, bool advance, Vector2 wish = default)
    {
        const float dt = World.TickSeconds;
        if (advance) Tick(ref l, c);
        var p = HangPoint(l, h, c, l.Time, l.Age);
        if (HangSwept(m, loco, HangRadius(m, c), TuckedHeight(loco), feet, p, out float share))
        {
            feet += (p - feet) * share;
            l = default;
            h = default;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        move.From = feet; move.To = feet;
        body.Yaw = HangYaw(h, c, l.Time);
        if (l.Time < c.Duration - 1e-4f) return;
        // The hang at the new lip, from its first frame (the clip ends on it there).
        l.Place = h.Place;
        l.In = h.In;
        l.Top = h.Top;
        move.Lip = h.Lip;
        move.LipNormal = h.LipNormal;
        move.Height = h.Lip.Z - feet.Z;
        var hang = paths.Of(LedgeRole.Hang, h.Braced != 0) ?? paths.Of(LedgeRole.Hang, h.Braced == 0)!;
        Switch(ref l, paths, hang, 0f, feet, LedgeBlend, fade: false);
        l.Braced = hang.Braced ? 1 : 0;
        l.Way = 0;
        l.Want = 0;
        body.State = BodyState.Hanging;
        body.Velocity = Vector3.Zero;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(h.In.Y, h.In.X));
        bool corner = h.Kind == HangMoveKind.Corner;
        int way = h.Way;
        if (h.Kind == HangMoveKind.Leap) body.AirSeconds = 0f;
        h = default;
        if (Brace(m, ref l, paths, feet)) return;
        if (!corner) return;
        // After a corner: the shimmy goes on that way while the wish that went in holds (its cycle from the hang's first frame, no fade).
        float wl = wish.Length();
        if (wl <= 0.3f) return;
        h.Carry = way;
        h.Wish = wish / wl;
        if (paths.Of(LedgeRole.Shimmy, l.Braced != 0, way) is { } sc && CycleFits(m, loco, l, paths, sc, feet, move.LipNormal))
        {
            Switch(ref l, paths, sc, 0f, feet, LedgeBlend, fade: false);   // from the hang's first frame: the capsule's millimetres out (the shimmy's own row against the hang's)
            l.Way = way;
        }
    }
}
