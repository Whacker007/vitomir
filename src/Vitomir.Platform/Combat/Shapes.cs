// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours.
using System.Numerics;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A capsule at any angle (ours): the sphere of <see cref="Radius"/> swept along the segment from
/// <see cref="A"/> to <see cref="B"/> (metres, Z up). A hit volume of the drawn body (a bone's), a blade's swept
/// thickness, a pole. <see cref="Space.Capsule"/> is the upright kind the moving body is; <see cref="Of"/> turns one
/// into this.
/// </summary>
public readonly record struct OrientedCapsule(Vector3 A, Vector3 B, float Radius)
{
    /// <summary>An upright capsule (a body's) as an oriented one.</summary>
    public static OrientedCapsule Of(in Capsule upright) => new(upright.Lower, upright.Upper, upright.Radius);

    /// <summary>The middle of its segment.</summary>
    public Vector3 Centre => new((A.X + B.X) * 0.5f, (A.Y + B.Y) * 0.5f, (A.Z + B.Z) * 0.5f);

    /// <summary>The same capsule a share <paramref name="t"/> of the way to <paramref name="to"/> (ends and radius blended; for a volume moving between two ticks).</summary>
    public OrientedCapsule Toward(in OrientedCapsule to, float t) =>
        new(Geometry.Lerp(A, to.A, t), Geometry.Lerp(B, to.B, t), Radius + (to.Radius - Radius) * t);
}

/// <summary>
/// A box at any angle (ours): its centre, its three axes (unit, at right angles: its local X, Y and Z in the
/// world) and its half sizes along them. A shield, a door, a crate's side. <see cref="Place"/> makes one from a box in
/// a model's or a bone's own space and the matrix that stands it in the world.
/// </summary>
public readonly record struct OrientedBox(Vector3 Centre, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 Half)
{
    /// <summary>
    /// The box from <paramref name="min"/> to <paramref name="max"/> in a local space, stood in the world by
    /// <paramref name="place"/> (row vectors, rotation and uniform scale and offset, no shear): the axes are the
    /// matrix's rows made unit, the half sizes scaled by their lengths.
    /// </summary>
    public static OrientedBox Place(Vector3 min, Vector3 max, in Matrix4x4 place)
    {
        var mid = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
        var half = new Vector3((max.X - min.X) * 0.5f, (max.Y - min.Y) * 0.5f, (max.Z - min.Z) * 0.5f);
        var x = new Vector3(place.M11, place.M12, place.M13);
        var y = new Vector3(place.M21, place.M22, place.M23);
        var z = new Vector3(place.M31, place.M32, place.M33);
        float lx = Geometry.Length(x), ly = Geometry.Length(y), lz = Geometry.Length(z);
        return new OrientedBox(Animation.PoseMath.Transform(mid, place),
                               lx > 0 ? x / lx : Vector3.UnitX, ly > 0 ? y / ly : Vector3.UnitY, lz > 0 ? z / lz : Vector3.UnitZ,
                               new Vector3(half.X * lx, half.Y * ly, half.Z * lz));
    }

    /// <summary>A world point in the box's own frame (its centre the origin, its axes the frame's).</summary>
    public Vector3 ToLocal(Vector3 p)
    {
        var r = p - Centre;
        return new Vector3(Geometry.Dot(r, AxisX), Geometry.Dot(r, AxisY), Geometry.Dot(r, AxisZ));
    }

    /// <summary>A direction in the box's own frame.</summary>
    public Vector3 DirectionToLocal(Vector3 d) => new(Geometry.Dot(d, AxisX), Geometry.Dot(d, AxisY), Geometry.Dot(d, AxisZ));

    /// <summary>A point of the box's own frame in the world.</summary>
    public Vector3 FromLocal(Vector3 l) => Centre + AxisX * l.X + AxisY * l.Y + AxisZ * l.Z;

    /// <summary>A direction of the box's own frame in the world.</summary>
    public Vector3 DirectionFromLocal(Vector3 l) => AxisX * l.X + AxisY * l.Y + AxisZ * l.Z;
}

/// <summary>
/// The first contact a test found (ours): when (for a ray, the distance along it in metres; for a sweep or a
/// moving point, the share of the step, 0 to 1), where on the thing met, the normal there (unit, out of the thing met,
/// towards what met it), and whether the two already overlapped at the start (then the time is 0, the point is the
/// nearest the test could find and the normal the way out).
/// </summary>
public readonly record struct ContactHit(float Time, Vector3 Point, Vector3 Normal, bool Inside);

/// <summary>
/// The scalar vector arithmetic the contact tests are made of (ours): each product and sum written out in a
/// fixed order in single precision, like <see cref="Animation.PoseMath"/>, so the same inputs give the same bits on
/// every machine. System.Numerics' <c>Dot</c>, <c>Length</c>, <c>Cross</c>, <c>Lerp</c> and <c>Transform</c> may take
/// hardware paths (a fused multiply-add, a dot-product instruction) that round differently, so they are not used here;
/// its component-wise sums, differences and scalings are exact and are.
/// </summary>
public static class Geometry
{
    public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static float LengthSquared(Vector3 a) => a.X * a.X + a.Y * a.Y + a.Z * a.Z;

    public static float Length(Vector3 a) => MathF.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);

    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary><paramref name="a"/> made unit, or <paramref name="fallback"/> when it is (nearly) nothing.</summary>
    public static Vector3 Normalize(Vector3 a, Vector3 fallback)
    {
        float l = Length(a);
        return l > 1e-12f ? a / l : fallback;
    }

    /// <summary>A unit vector at right angles to the unit <paramref name="u"/> (the same one for the same <paramref name="u"/>).</summary>
    public static Vector3 Perpendicular(Vector3 u)
    {
        var other = MathF.Abs(u.X) < 0.6f ? Vector3.UnitX : MathF.Abs(u.Y) < 0.6f ? Vector3.UnitY : Vector3.UnitZ;
        return Normalize(Cross(u, other), Vector3.UnitZ);
    }

    /// <summary>The point of the segment <paramref name="a"/>–<paramref name="b"/> nearest <paramref name="p"/>, and its share <paramref name="t"/> of the way from a.</summary>
    public static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
    {
        var ab = b - a;
        float l2 = LengthSquared(ab);
        t = l2 > 1e-20f ? Math.Clamp(Dot(p - a, ab) / l2, 0f, 1f) : 0f;
        return a + ab * t;
    }

    /// <summary>
    /// The squared distance between the segments p1–q1 and p2–q2, the closest points and their shares of the way
    /// (Ericson, <i>Real-Time Collision Detection</i>, 5.1.9; <see cref="SweepMath.SegmentSegment"/> in scalar steps).
    /// </summary>
    public static float SegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
    {
        const float Eps = 1e-12f;
        var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
        float a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r);
        if (a <= Eps && e <= Eps) { s = t = 0; }
        else if (a <= Eps) { s = 0; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = Dot(d1, r);
            if (e <= Eps) { t = 0; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = Dot(d1, d2), denom = a * e - b * b;
                if (denom <= 1e-6f * a * e) return NearlyParallel(p1, q1, p2, q2, out s, out t, out c1, out c2);
                s = Math.Clamp((b * f - c * e) / denom, 0f, 1f);
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        return LengthSquared(c1 - c2);
    }

    /// <summary>
    /// Two segments within a thousandth of a radian of parallel, where the closed form's denominator is rounding: the
    /// nearest of each end against the other segment (exact for parallel ones; within micrometres for nearly parallel
    /// ones at contact distances).
    /// </summary>
    static float NearlyParallel(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
    {
        var on = ClosestOnSegment(p1, p2, q2, out float tp);
        float best = LengthSquared(p1 - on); s = 0f; t = tp; c1 = p1; c2 = on;
        on = ClosestOnSegment(q1, p2, q2, out float tq);
        float d = LengthSquared(q1 - on);
        if (d < best) { best = d; s = 1f; t = tq; c1 = q1; c2 = on; }
        on = ClosestOnSegment(p2, p1, q1, out float sp);
        d = LengthSquared(p2 - on);
        if (d < best) { best = d; s = sp; t = 0f; c1 = on; c2 = p2; }
        on = ClosestOnSegment(q2, p1, q1, out float sq);
        d = LengthSquared(q2 - on);
        if (d < best) { best = d; s = sq; t = 1f; c1 = on; c2 = q2; }
        return best;
    }

    /// <summary>The squared distance from <paramref name="p"/> to a box (0 inside it), and the box's point nearest it.</summary>
    public static float PointBox(Vector3 p, in OrientedBox box, out Vector3 nearest)
    {
        var l = box.ToLocal(p);
        var c = new Vector3(Math.Clamp(l.X, -box.Half.X, box.Half.X), Math.Clamp(l.Y, -box.Half.Y, box.Half.Y), Math.Clamp(l.Z, -box.Half.Z, box.Half.Z));
        nearest = box.FromLocal(c);
        return LengthSquared(l - c);
    }

    /// <summary>
    /// The squared distance between the segment <paramref name="p"/>–<paramref name="q"/> and a box (0 when they
    /// meet), the segment's share <paramref name="s"/> at the nearest place, and the nearest points. Exact: in the box's
    /// frame the squared distance along the segment is a convex sum of pieces of parabolas, broken where the segment
    /// crosses a face's plane (at most six places); each piece's least is found in closed form and the least of those
    /// taken.
    /// </summary>
    public static float SegmentBox(Vector3 p, Vector3 q, in OrientedBox box, out float s, out Vector3 onSegment, out Vector3 onBox)
    {
        var lp = box.ToLocal(p);
        var ld = box.DirectionToLocal(q - p);
        Span<float> cuts = stackalloc float[8];
        int n = 0;
        cuts[n++] = 0f;
        for (int i = 0; i < 3; i++)
        {
            float d = Axis(ld, i), o = Axis(lp, i), h = Axis(box.Half, i);
            if (MathF.Abs(d) <= 1e-20f) continue;
            float c1 = (-h - o) / d, c2 = (h - o) / d;
            if (c1 > 0f && c1 < 1f) cuts[n++] = c1;
            if (c2 > 0f && c2 < 1f) cuts[n++] = c2;
        }
        cuts[n++] = 1f;
        // Sorted in place (a handful of numbers; insertion sort, the same order every time).
        for (int i = 1; i < n; i++)
        {
            float v = cuts[i]; int j = i - 1;
            while (j >= 0 && cuts[j] > v) { cuts[j + 1] = cuts[j]; j--; }
            cuts[j + 1] = v;
        }
        float best = float.PositiveInfinity, bestS = 0f;
        for (int k = 0; k + 1 < n; k++)
        {
            float a = cuts[k], b = cuts[k + 1], mid = (a + b) * 0.5f;
            // On this piece each axis is outside one face (a parabola) or between the two (nothing).
            float qa = 0f, qb = 0f;
            for (int i = 0; i < 3; i++)
            {
                float d = Axis(ld, i), o = Axis(lp, i), h = Axis(box.Half, i);
                float x = o + d * mid;
                if (x > h) { qa += d * d; qb += 2f * d * (o - h); }
                else if (x < -h) { qa += d * d; qb += 2f * d * (o + h); }
            }
            float sv = qa > 1e-20f ? Math.Clamp(-qb / (2f * qa), a, b) : (qb > 0f ? a : b);
            float f = SquaredOutside(lp, ld, box.Half, sv);
            if (f < best) { best = f; bestS = sv; }
            float fa = SquaredOutside(lp, ld, box.Half, a);
            if (fa < best) { best = fa; bestS = a; }
        }
        float f1 = SquaredOutside(lp, ld, box.Half, 1f);
        if (f1 < best) { best = f1; bestS = 1f; }
        s = bestS;
        onSegment = p + (q - p) * s;
        var x0 = lp + ld * s;
        onBox = box.FromLocal(new Vector3(Math.Clamp(x0.X, -box.Half.X, box.Half.X), Math.Clamp(x0.Y, -box.Half.Y, box.Half.Y), Math.Clamp(x0.Z, -box.Half.Z, box.Half.Z)));
        return best;
    }

    static float SquaredOutside(Vector3 lp, Vector3 ld, Vector3 half, float s)
    {
        float sum = 0f;
        for (int i = 0; i < 3; i++)
        {
            float x = Axis(lp, i) + Axis(ld, i) * s, h = Axis(half, i);
            float e = x > h ? x - h : x < -h ? -h - x : 0f;
            sum += e * e;
        }
        return sum;
    }

    internal static float Axis(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
}
