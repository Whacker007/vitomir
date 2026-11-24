// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, after Ericson, Real-Time Collision Detection (5.3 for the rays, 5.5 and 9.5's conservative advancement
// for the sweeps).
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Combat.Geometry;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The tests contact is made of (ours): a ray, a moving point and a swept blade against
/// oriented capsules (the drawn body's hit volumes) and oriented boxes (a shield), and a moving point against the
/// level. Every one gives the <b>first</b> contact — when, where on the thing met, and the normal there — and says when
/// the two already overlapped at the start. All are scalar arithmetic in a fixed order (<see cref="Geometry"/>), with no
/// trigonometry and no clock, so the same inputs give the same answer on every machine.
/// <para>
/// <b>The blade's sweep</b> (a segment whose two ends each move in a straight line between two ticks, against a capsule
/// that may move the same way, or a box) is found by <b>conservative advancement</b> rather than a swept quad or
/// sub-steps: the distance between the two can shrink no faster than the fastest point of either moves, so from any
/// time it is safe to advance by the distance over that speed; repeated, that walks up to the first contact without
/// ever stepping over it, however thin the thing or fast the swing, and it is exact for a blade that turns (whose swept
/// surface is curved, so a quad of two triangles is not). It stops within <see cref="Tolerance"/> of touching. A
/// sub-stepped test misses what lies between its steps unless they are finer than the thinnest thing (a 0.05 m hand
/// against a tip moving 0.6 m a tick wants a dozen steps a tick, every tick); this takes a few distance tests when
/// something is near and one when nothing is.
/// </para>
/// </summary>
public static class ContactMath
{
    /// <summary>A sweep's contact is taken when the two are this close (m): a tenth of a millimetre.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>The most steps a sweep takes; a blade sliding along a surface this side of <see cref="Tolerance"/> for a whole step takes the most, and is then taken as touching it if within <see cref="GrazeAtMost"/>.</summary>
    public const int MaxSteps = 4096;

    /// <summary>A sweep that runs out of steps (a long graze) is taken as a contact when the two are this close then (m), a miss otherwise.</summary>
    public const float GrazeAtMost = 1e-3f;

    // ---------------------------------------------------------------- rays

    /// <summary>
    /// The ray from <paramref name="o"/> along the unit <paramref name="d"/> for <paramref name="length"/> against a
    /// capsule: the distance along it to the first touch, the point and the outward normal there. A ray that starts
    /// inside answers at once (time 0, <see cref="ContactHit.Inside"/>, the normal out from the axis). Exact: the
    /// capsule is its round side (a cylinder, its ends cut at the segment's ends) and its two end spheres, and the first
    /// touch is the first of the three.
    /// </summary>
    public static bool Ray(Vector3 o, Vector3 d, float length, in OrientedCapsule c, out ContactHit hit)
    {
        var onAxis = ClosestOnSegment(o, c.A, c.B, out _);
        var away = o - onAxis;
        float r2 = c.Radius * c.Radius;
        if (LengthSquared(away) <= r2)
        {
            hit = new ContactHit(0f, o, Normalize(away, -d), true);
            return true;
        }
        float best = float.PositiveInfinity;
        Vector3 normal = default;
        var ab = c.B - c.A;
        float len2 = LengthSquared(ab);
        if (len2 > 1e-12f)
        {
            float len = MathF.Sqrt(len2);
            var u = ab / len;
            var ao = o - c.A;
            float m = Dot(ao, u), n = Dot(d, u);
            var aoP = ao - u * m;
            var dP = d - u * n;
            float qa = Dot(dP, dP), qb = Dot(aoP, dP), qc = Dot(aoP, aoP) - r2;
            if (qa > 1e-12f)
            {
                float disc = qb * qb - qa * qc;
                if (disc >= 0f)
                {
                    float t = (-qb - MathF.Sqrt(disc)) / qa;
                    float along = m + t * n;
                    if (t >= 0f && along >= 0f && along <= len)
                    {
                        best = t;
                        normal = Normalize(aoP + dP * t, -d);
                    }
                }
            }
        }
        if (Sphere(o, d, c.A, c.Radius) is { } ta && ta < best) { best = ta; normal = Normalize(o + d * ta - c.A, -d); }
        if (Sphere(o, d, c.B, c.Radius) is { } tb && tb < best) { best = tb; normal = Normalize(o + d * tb - c.B, -d); }
        if (!(best <= length)) { hit = default; return false; }
        hit = new ContactHit(best, o + d * best, normal, false);
        return true;
    }

    /// <summary>The ray from <paramref name="o"/> along the unit <paramref name="d"/> against a sphere from outside it: the distance to the first touch, or null.</summary>
    static float? Sphere(Vector3 o, Vector3 d, Vector3 centre, float r)
    {
        var m = o - centre;
        float b = Dot(m, d), c = Dot(m, m) - r * r;
        if (c > 0f && b > 0f) return null;
        float disc = b * b - c;
        if (disc < 0f) return null;
        float t = -b - MathF.Sqrt(disc);
        return t < 0f ? 0f : t;
    }

    /// <summary>
    /// The ray from <paramref name="o"/> along the unit <paramref name="d"/> for <paramref name="length"/> against a
    /// box: the distance to the first touch, the point and the face's outward normal there (the slab test in the box's
    /// frame). A ray that starts inside answers at once (time 0, inside, the normal of the nearest face).
    /// </summary>
    public static bool Ray(Vector3 o, Vector3 d, float length, in OrientedBox box, out ContactHit hit)
    {
        var lo = box.ToLocal(o);
        var ld = box.DirectionToLocal(d);
        float tMin = 0f, tMax = length;
        int enter = -1; float enterSign = 0f;
        bool inside = true;
        for (int i = 0; i < 3; i++)
        {
            float oi = Axis(lo, i), di = Axis(ld, i), h = Axis(box.Half, i);
            if (oi < -h || oi > h) inside = false;
            if (MathF.Abs(di) < 1e-12f)
            {
                if (oi < -h || oi > h) { hit = default; return false; }
                continue;
            }
            float t1 = (-h - oi) / di, t2 = (h - oi) / di;
            float sign = -1f;   // entering through the −h face
            if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
            if (t1 > tMin) { tMin = t1; enter = i; enterSign = sign; }
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax) { hit = default; return false; }
        }
        if (inside)
        {
            hit = new ContactHit(0f, o, NearestFace(box, lo), true);
            return true;
        }
        if (enter < 0) { hit = default; return false; }
        var nl = enter == 0 ? new Vector3(enterSign, 0, 0) : enter == 1 ? new Vector3(0, enterSign, 0) : new Vector3(0, 0, enterSign);
        hit = new ContactHit(tMin, o + d * tMin, box.DirectionFromLocal(nl), false);
        return true;
    }

    /// <summary>The outward normal of the box's face nearest the local point <paramref name="l"/> (the way out of least depth).</summary>
    static Vector3 NearestFace(in OrientedBox box, Vector3 l)
    {
        int axis = 0; float least = float.PositiveInfinity, sign = 1f;
        for (int i = 0; i < 3; i++)
        {
            float x = Axis(l, i), h = Axis(box.Half, i);
            float depth = h - MathF.Abs(x);
            if (depth < least) { least = depth; axis = i; sign = x >= 0f ? 1f : -1f; }
        }
        var nl = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
        return box.DirectionFromLocal(nl);
    }

    // ---------------------------------------------------------------- moving points (a projectile)

    /// <summary>
    /// A point — or a ball of <paramref name="radius"/> — moving in a straight line from <paramref name="from"/> to
    /// <paramref name="to"/> in one step, against a capsule: the share of the step at the first touch (0 to 1), the
    /// point on the capsule and the normal. A ball is the ray against the capsule grown by its radius.
    /// </summary>
    public static bool Moving(Vector3 from, Vector3 to, float radius, in OrientedCapsule c, out ContactHit hit)
    {
        var way = to - from;
        float len = Length(way);
        var grown = c with { Radius = c.Radius + radius };
        var d = len > 1e-12f ? way / len : Vector3.UnitX;
        if (!Ray(from, d, len, grown, out var h)) { hit = default; return false; }
        hit = new ContactHit(len > 1e-12f ? h.Time / len : 0f, h.Point - h.Normal * radius, h.Normal, h.Inside);
        return true;
    }

    /// <summary>
    /// A point moving from <paramref name="from"/> to <paramref name="to"/> against a box: the share of the step at the
    /// first touch, the point and the face's normal. A ball (<paramref name="radius"/> above 0) is swept as a blade of
    /// no length (<see cref="Sweep(Vector3, Vector3, Vector3, Vector3, float, in OrientedBox, out ContactHit)"/>).
    /// </summary>
    public static bool Moving(Vector3 from, Vector3 to, float radius, in OrientedBox box, out ContactHit hit)
    {
        if (radius > 0f) return Sweep(from, from, to, to, radius, box, out hit);
        var way = to - from;
        float len = Length(way);
        var d = len > 1e-12f ? way / len : Vector3.UnitX;
        if (!Ray(from, d, len, box, out var h)) { hit = default; return false; }
        hit = h with { Time = len > 1e-12f ? h.Time / len : 0f };
        return true;
    }

    /// <summary>
    /// A point (or a ball of <paramref name="radius"/>) moving from <paramref name="from"/> to <paramref name="to"/>
    /// against the level's triangles carrying any of <paramref name="mask"/>'s flags (the existing collision:
    /// <see cref="CollisionWorld.Ray"/>, or <see cref="CollisionWorld.SphereSweep"/> for a ball): the share of the
    /// step at the first touch, the point and the normal, and the triangle met.
    /// </summary>
    public static bool Moving(Vector3 from, Vector3 to, float radius, CollisionWorld level, out ContactHit hit, out int triangle, L2Surface mask = L2Surface.Solid)
    {
        var way = to - from;
        float len = Length(way);
        triangle = -1;
        hit = default;
        if (len <= 1e-12f) return false;
        var d = way / len;
        var found = radius > 0f ? level.SphereSweep(from, d, len, radius, mask) : level.Ray(from, d, len, mask);
        if (found is not { } h) return false;
        triangle = h.Triangle;
        hit = new ContactHit(h.Distance / len, h.Point, h.Normal, h.Distance <= 0f);
        return true;
    }

    // ---------------------------------------------------------------- swept blades

    /// <summary>
    /// A blade — the segment <paramref name="p0"/>–<paramref name="q0"/> at the start of a step, <paramref name="p1"/>–<paramref name="q1"/>
    /// at its end, each end moving in a straight line, <paramref name="bladeRadius"/> thick — against a capsule that
    /// moves from <paramref name="c0"/> to <paramref name="c1"/> over the same step (pass the same one twice for a
    /// still one): the share of the step at the first touch (0 to 1), the point on the capsule's surface and its
    /// normal there. Conservative advancement (the class's summary): exact to <see cref="Tolerance"/>, never past the
    /// first touch.
    /// </summary>
    public static bool Sweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float bladeRadius, in OrientedCapsule c0, in OrientedCapsule c1, out ContactHit hit)
    {
        var target = new CapsuleTarget(c0, c1, bladeRadius);
        return Advance(ref target, p0, q0, p1, q1, out hit, out _);
    }

    /// <summary>The blade against a still capsule.</summary>
    public static bool Sweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float bladeRadius, in OrientedCapsule c, out ContactHit hit) =>
        Sweep(p0, q0, p1, q1, bladeRadius, c, c, out hit);

    /// <summary>
    /// The blade (as <see cref="Sweep(Vector3, Vector3, Vector3, Vector3, float, in OrientedCapsule, in OrientedCapsule, out ContactHit)"/>)
    /// against a box standing still over the step (a shield where this tick's pose holds it): the share of the step at
    /// the first touch, the point on the box and the normal there.
    /// </summary>
    public static bool Sweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float bladeRadius, in OrientedBox box, out ContactHit hit)
    {
        var target = new BoxTarget(box, bladeRadius);
        return Advance(ref target, p0, q0, p1, q1, out hit, out _);
    }

    /// <summary>
    /// The blade against a box that moves from <paramref name="box0"/> to <paramref name="box1"/> over the step (a shield
    /// on a moving arm): swept in the box's own frame, where it stands still — the blade's ends taken into
    /// <paramref name="box0"/>'s frame at the start and <paramref name="box1"/>'s at the end, each moving in a straight
    /// line between (the box's turn over one tick is small) — and the contact brought back to the world on the box as it
    /// stands at the contact's time (its two places mixed by the time). Exact for a box that only slides.
    /// </summary>
    public static bool Sweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float bladeRadius, in OrientedBox box0, in OrientedBox box1, out ContactHit hit)
    {
        var still = new OrientedBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, box1.Half);
        if (!Sweep(box0.ToLocal(p0), box0.ToLocal(q0), box1.ToLocal(p1), box1.ToLocal(q1), bladeRadius, still, out var h)) { hit = default; return false; }
        var point = Lerp(box0.FromLocal(h.Point), box1.FromLocal(h.Point), h.Time);
        var normal = Normalize(Lerp(box0.DirectionFromLocal(h.Normal), box1.DirectionFromLocal(h.Normal), h.Time), box1.DirectionFromLocal(h.Normal));
        hit = new ContactHit(h.Time, point, normal, h.Inside);
        return true;
    }

    /// <summary>The blade against a moving capsule, with how many distance tests it took (for the tests and the costs).</summary>
    internal static bool Sweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float bladeRadius, in OrientedCapsule c0, in OrientedCapsule c1, out ContactHit hit, out int steps)
    {
        var target = new CapsuleTarget(c0, c1, bladeRadius);
        return Advance(ref target, p0, q0, p1, q1, out hit, out steps);
    }

    interface ITarget
    {
        /// <summary>How far apart the blade (p–q, at time t) and the target are less their radii; the nearest points and the normal out of the target.</summary>
        float Gap(Vector3 p, Vector3 q, float t, out Vector3 point, out Vector3 normal);
        /// <summary>The fastest any point of the target moves over the step (m a step).</summary>
        float Speed { get; }
    }

    readonly struct CapsuleTarget(OrientedCapsule c0, OrientedCapsule c1, float bladeRadius) : ITarget
    {
        public float Speed { get; } = MathF.Max(Length(c1.A - c0.A), Length(c1.B - c0.B)) + MathF.Abs(c1.Radius - c0.Radius);

        public float Gap(Vector3 p, Vector3 q, float t, out Vector3 point, out Vector3 normal)
        {
            var a = Lerp(c0.A, c1.A, t);
            var b = Lerp(c0.B, c1.B, t);
            float r = c0.Radius + (c1.Radius - c0.Radius) * t;
            float d2 = SegmentSegment(p, q, a, b, out _, out _, out var onBlade, out var onAxis);
            var mid = new Vector3((p.X + q.X) * 0.5f, (p.Y + q.Y) * 0.5f, (p.Z + q.Z) * 0.5f);
            normal = Normalize(onBlade - onAxis, Normalize(mid - ClosestOnSegment(mid, a, b, out _), Perpendicular(Normalize(b - a, Vector3.UnitZ))));
            point = onAxis + normal * r;
            return MathF.Sqrt(d2) - r - bladeRadius;
        }
    }

    readonly struct BoxTarget(OrientedBox box, float bladeRadius) : ITarget
    {
        public float Speed => 0f;

        public float Gap(Vector3 p, Vector3 q, float t, out Vector3 point, out Vector3 normal)
        {
            float d2 = SegmentBox(p, q, box, out _, out var onSeg, out var onBox);
            point = onBox;
            normal = d2 > 1e-14f ? Normalize(onSeg - onBox, Vector3.UnitZ) : NearestFace(box, box.ToLocal(onSeg));
            return MathF.Sqrt(d2) - bladeRadius;
        }
    }

    /// <summary>Conservative advancement of a blade against a target (the class's summary).</summary>
    static bool Advance<T>(ref T target, Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, out ContactHit hit, out int steps) where T : struct, ITarget
    {
        float speed = MathF.Max(Length(p1 - p0), Length(q1 - q0)) + target.Speed;
        float t = 0f;
        float gap = target.Gap(p0, q0, 0f, out var point, out var normal);
        steps = 1;
        if (gap <= 0f) { hit = new ContactHit(0f, point, normal, true); return true; }
        while (true)
        {
            if (gap <= Tolerance) { hit = new ContactHit(t, point, normal, false); return true; }
            if (speed <= 1e-12f) break;
            float next = t + gap / speed;
            if (next > 1f) break;
            if (steps >= MaxSteps)
            {
                if (gap <= GrazeAtMost) { hit = new ContactHit(t, point, normal, false); return true; }
                break;
            }
            t = next;
            gap = target.Gap(Lerp(p0, p1, t), Lerp(q0, q1, t), t, out point, out normal);
            steps++;
        }
        hit = default;
        return false;
    }
}
