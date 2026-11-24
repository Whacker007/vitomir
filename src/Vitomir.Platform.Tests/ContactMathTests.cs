// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The geometry contact will use: rays, moving points and swept blades against oriented capsules and boxes, and
/// a moving point against the level, each checked against brute-force sampling on seeded random cases and on the edge
/// cases — a graze just inside and just outside, starting inside, parallel to the thing met, a thin thing a fast blade
/// would jump over between ticks — and found the same bit for bit when asked twice.
/// </summary>
public sealed class ContactMathTests
{
    static Vector3 RandomPoint(Rng rng, float half) =>
        new((rng.NextFloat() * 2 - 1) * half, (rng.NextFloat() * 2 - 1) * half, (rng.NextFloat() * 2 - 1) * half);

    static Vector3 RandomUnit(Rng rng)
    {
        while (true)
        {
            var v = RandomPoint(rng, 1f);
            float l = v.Length();
            if (l > 0.2f && l <= 1f) return v / l;
        }
    }

    static OrientedBox RandomBox(Rng rng)
    {
        var x = RandomUnit(rng);
        var y = Geometry.Normalize(Geometry.Cross(x, RandomUnit(rng)), Geometry.Perpendicular(x));
        var z = Geometry.Cross(x, y);
        return new OrientedBox(RandomPoint(rng, 0.3f), x, y, z, new Vector3(0.05f + rng.NextFloat() * 0.4f, 0.05f + rng.NextFloat() * 0.4f, 0.02f + rng.NextFloat() * 0.2f));
    }

    static float CapsuleGap(Vector3 p, in OrientedCapsule c)
    {
        var on = Geometry.ClosestOnSegment(p, c.A, c.B, out _);
        return Vector3.Distance(p, on) - c.Radius;
    }

    /// <summary>The first sample along the ray inside the shape (gap ≤ 0), or null; and the least gap met (to skip a case too near a graze to call).</summary>
    static (float? First, float Least) MarchRay(Vector3 o, Vector3 d, float length, Func<Vector3, float> gap, int samples)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i <= samples; i++)
        {
            float t = length * i / samples;
            float g = gap(o + d * t);
            least = MathF.Min(least, g);
            if (g <= 0f) return (t, least);
        }
        return (null, least);
    }

    [Fact]
    public void A_ray_meets_a_capsule_where_brute_force_marching_first_finds_it()
    {
        var rng = new Rng(14);
        int hits = 0, misses = 0;
        for (int n = 0; n < 400; n++)
        {
            var c = new OrientedCapsule(RandomPoint(rng, 0.5f), RandomPoint(rng, 0.5f), 0.03f + rng.NextFloat() * 0.3f);
            var o = RandomUnit(rng) * 2f;
            var d = Geometry.Normalize(RandomPoint(rng, 0.4f) - o, Vector3.UnitX);
            const float Length = 4f;
            var (first, least) = MarchRay(o, d, Length, p => CapsuleGap(p, c), 40000);
            bool hit = ContactMath.Ray(o, d, Length, c, out var h);
            if (first is null && least < 2e-4f) continue;   // too near a graze for the march to call
            Assert.Equal(first is not null, hit);
            if (!hit) { misses++; continue; }
            hits++;
            Assert.InRange(h.Time, first!.Value - Length / 40000f - 1e-4f, first.Value + 1e-4f);
            Assert.InRange(CapsuleGap(h.Point, c), -1e-4f, 1e-4f);                         // on the surface
            var on = Geometry.ClosestOnSegment(h.Point, c.A, c.B, out _);
            Assert.True(Vector3.Distance(Geometry.Normalize(h.Point - on, Vector3.Zero), h.Normal) < 1e-3f);   // out of the axis
            Assert.True(Geometry.Dot(h.Normal, d) <= 1e-4f);                                // facing the ray
        }
        Assert.True(hits > 50 && misses > 50, $"{hits} hits, {misses} misses");
    }

    [Fact]
    public void A_ray_meets_a_box_where_brute_force_marching_first_finds_it()
    {
        var rng = new Rng(15);
        int hits = 0, misses = 0;
        for (int n = 0; n < 400; n++)
        {
            var box = RandomBox(rng);
            var o = RandomUnit(rng) * 2f;
            var d = Geometry.Normalize(RandomPoint(rng, 0.5f) - o, Vector3.UnitX);
            const float Length = 4f;
            var (first, least) = MarchRay(o, d, Length, p => MathF.Sqrt(Geometry.PointBox(p, box, out _)) - (Inside(p, box) ? 1f : 0f), 40000);
            bool hit = ContactMath.Ray(o, d, Length, box, out var h);
            if (first is null && least < 2e-4f) continue;
            Assert.Equal(first is not null, hit);
            if (!hit) { misses++; continue; }
            hits++;
            Assert.InRange(h.Time, first!.Value - Length / 40000f - 1e-4f, first.Value + 1e-4f);
            Assert.True(Geometry.PointBox(h.Point, box, out _) < 1e-8f);
            Assert.InRange(MathF.Abs(Geometry.Dot(h.Normal, box.AxisX)) + MathF.Abs(Geometry.Dot(h.Normal, box.AxisY)) + MathF.Abs(Geometry.Dot(h.Normal, box.AxisZ)), 0.999f, 1.001f);   // a face's normal
            Assert.True(Geometry.Dot(h.Normal, d) < 0f);
        }
        Assert.True(hits > 50 && misses > 50, $"{hits} hits, {misses} misses");
    }

    static bool Inside(Vector3 p, in OrientedBox box)
    {
        var l = box.ToLocal(p);
        return MathF.Abs(l.X) <= box.Half.X && MathF.Abs(l.Y) <= box.Half.Y && MathF.Abs(l.Z) <= box.Half.Z;
    }

    [Fact]
    public void The_closest_points_of_two_segments_and_of_a_segment_and_a_box_are_the_least_brute_force_finds()
    {
        var rng = new Rng(16);
        for (int n = 0; n < 200; n++)
        {
            Vector3 p1 = RandomPoint(rng, 1f), q1 = RandomPoint(rng, 1f), p2 = RandomPoint(rng, 1f), q2 = RandomPoint(rng, 1f);
            if (n % 5 == 0) q2 = p2 + (q1 - p1) * 0.7f + RandomPoint(rng, 1e-4f);   // nearly parallel
            float d2 = Geometry.SegmentSegment(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
            Assert.InRange(Vector3.DistanceSquared(c1, c2), d2 - 1e-6f, d2 + 1e-6f);
            float least = float.PositiveInfinity;
            for (int i = 0; i <= 400; i++)
            {
                var a = Vector3.Lerp(p1, q1, i / 400f);
                var on = Geometry.ClosestOnSegment(a, p2, q2, out _);
                least = MathF.Min(least, Vector3.Distance(a, on));
            }
            Assert.InRange(MathF.Sqrt(d2), least - 6e-3f, least + 1e-4f);   // the march's own step is 1/400 of up to 3.5 m

            var box = RandomBox(rng);
            float b2 = Geometry.SegmentBox(p1, q1, box, out float s, out var onSeg, out var onBox);
            float bLeast = float.PositiveInfinity;
            for (int i = 0; i <= 4000; i++) bLeast = MathF.Min(bLeast, Geometry.PointBox(Vector3.Lerp(p1, q1, i / 4000f), box, out _));
            Assert.InRange(MathF.Sqrt(b2), MathF.Sqrt(bLeast) - 1e-3f, MathF.Sqrt(bLeast) + 1e-5f);
            Assert.InRange(s, 0f, 1f);
            Assert.True(Geometry.PointBox(onBox, box, out _) < 1e-8f);
            Assert.InRange(Vector3.Distance(onSeg, onBox), MathF.Sqrt(b2) - 1e-4f, MathF.Sqrt(b2) + 1e-4f);
        }
    }

    /// <summary>The first sampled time a blade (its ends moving straight) is within the capsule's reach; and the least gap.</summary>
    static (float? First, float Least) MarchSweep(Vector3 p0, Vector3 q0, Vector3 p1, Vector3 q1, float blade, Func<float, Vector3, Vector3, float> gap, int samples)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float g = gap(t, Vector3.Lerp(p0, p1, t), Vector3.Lerp(q0, q1, t)) - blade;
            least = MathF.Min(least, g);
            if (g <= 0f) return (t, least);
        }
        return (null, least);
    }

    /// <summary>A blade of about a metre whose hilt is a metre or so from the middle, swung across it (its tip from one side of the middle to another), the hilt moving a little.</summary>
    static (Vector3 Hilt0, Vector3 Tip0, Vector3 Hilt1, Vector3 Tip1) Swing(Rng rng)
    {
        var hilt0 = RandomUnit(rng) * 1.0f;
        var hilt1 = hilt0 + RandomPoint(rng, 0.1f);
        var tip0 = hilt0 + Geometry.Normalize(RandomPoint(rng, 0.8f) - hilt0, Vector3.UnitX) * 1.1f;
        var tip1 = hilt1 + Geometry.Normalize(RandomPoint(rng, 0.8f) - hilt1, Vector3.UnitX) * 1.1f;
        return (hilt0, tip0, hilt1, tip1);
    }

    [Fact]
    public void A_swept_blade_meets_a_still_or_moving_capsule_first_where_brute_force_finds_it()
    {
        var rng = new Rng(17);
        int hits = 0, misses = 0, most = 0;
        for (int n = 0; n < 300; n++)
        {
            bool moving = n % 2 == 1;
            var c0 = new OrientedCapsule(RandomPoint(rng, 0.4f), RandomPoint(rng, 0.4f), 0.03f + rng.NextFloat() * 0.2f);
            var c1 = moving ? new OrientedCapsule(c0.A + RandomPoint(rng, 0.1f), c0.B + RandomPoint(rng, 0.1f), c0.Radius) : c0;
            var (hilt0, tip0, hilt1, tip1) = Swing(rng);
            const float Blade = 0.01f;
            var (first, least) = MarchSweep(hilt0, tip0, hilt1, tip1, Blade, (t, p, q) =>
            {
                var c = c0.Toward(c1, t);
                return MathF.Sqrt(Geometry.SegmentSegment(p, q, c.A, c.B, out _, out _, out _, out _)) - c.Radius;
            }, 20000);
            bool hit = ContactMath.Sweep(hilt0, tip0, hilt1, tip1, Blade, c0, c1, out var h, out int steps);
            most = Math.Max(most, steps);
            if (first is null && least < 5e-4f) continue;
            Assert.Equal(first is not null, hit);
            if (!hit) { misses++; continue; }
            hits++;
            if (h.Inside) { Assert.Equal(0f, first!.Value); continue; }
            Assert.True(h.Time <= first!.Value + 1e-6f);   // never after the first touch
            // At the time found the blade touches (within the tolerance) the capsule's surface at the point given.
            var at = c0.Toward(c1, h.Time);
            Assert.InRange(CapsuleGap(h.Point, at), -1e-4f, 1e-4f);
            float gap = MathF.Sqrt(Geometry.SegmentSegment(Vector3.Lerp(hilt0, hilt1, h.Time), Vector3.Lerp(tip0, tip1, h.Time), at.A, at.B, out _, out _, out _, out _)) - at.Radius - Blade;
            Assert.InRange(gap, 0f, ContactMath.Tolerance);
        }
        Assert.True(hits > 40 && misses > 40, $"{hits} hits, {misses} misses");
        Assert.True(most < 200, $"the most steps one sweep took: {most}");
    }

    [Fact]
    public void A_swept_blade_meets_a_box_first_where_brute_force_finds_it()
    {
        var rng = new Rng(18);
        int hits = 0, misses = 0;
        for (int n = 0; n < 300; n++)
        {
            var box = RandomBox(rng);
            var (hilt0, tip0, hilt1, tip1) = Swing(rng);
            const float Blade = 0.01f;
            var (first, least) = MarchSweep(hilt0, tip0, hilt1, tip1, Blade, (t, p, q) => MathF.Sqrt(Geometry.SegmentBox(p, q, box, out _, out _, out _)), 20000);
            bool hit = ContactMath.Sweep(hilt0, tip0, hilt1, tip1, Blade, box, out var h);
            if (first is null && least < 5e-4f) continue;
            Assert.Equal(first is not null, hit);
            if (!hit) { misses++; continue; }
            hits++;
            if (h.Inside) { Assert.Equal(0f, first!.Value); continue; }
            Assert.True(h.Time <= first!.Value + 1e-6f);   // never after the first touch
            float gap = MathF.Sqrt(Geometry.SegmentBox(Vector3.Lerp(hilt0, hilt1, h.Time), Vector3.Lerp(tip0, tip1, h.Time), box, out _, out _, out _)) - Blade;
            Assert.InRange(gap, 0f, ContactMath.Tolerance);   // and touching then, within the tolerance
            Assert.True(Geometry.PointBox(h.Point, box, out _) < 1e-8f);
            Assert.InRange(Geometry.Length(h.Normal), 0.999f, 1.001f);
        }
        Assert.True(hits > 40 && misses > 40, $"{hits} hits, {misses} misses");
    }

    [Fact]
    public void A_graze_a_tenth_of_a_millimetre_either_side_of_touching_is_a_hit_or_a_miss()
    {
        var c = new OrientedCapsule(new Vector3(0, 0, 0), new Vector3(0, 0, 1), 0.1f);
        // A ray past the side.
        Assert.True(ContactMath.Ray(new Vector3(-1, 0.0999f, 0.5f), Vector3.UnitX, 2f, c, out var hit));
        Assert.False(hit.Inside);
        Assert.False(ContactMath.Ray(new Vector3(-1, 0.1002f, 0.5f), Vector3.UnitX, 2f, c, out _));
        // Over the top cap.
        Assert.True(ContactMath.Ray(new Vector3(-1, 0, 1.0999f), Vector3.UnitX, 2f, c, out _));
        Assert.False(ContactMath.Ray(new Vector3(-1, 0, 1.1002f), Vector3.UnitX, 2f, c, out _));
        // A blade swept past the side, upright, from one side to the other: within a tenth of a millimetre it touches.
        var (p0, q0) = (new Vector3(-0.5f, 0.1005f, 0.2f), new Vector3(-0.5f, 0.1005f, 0.8f));
        var (p1, q1) = (new Vector3(0.5f, 0.1005f, 0.2f), new Vector3(0.5f, 0.1005f, 0.8f));
        Assert.False(ContactMath.Sweep(p0, q0, p1, q1, 0f, c, out _));
        Assert.True(ContactMath.Sweep(p0 with { Y = 0.10005f }, q0 with { Y = 0.10005f }, p1 with { Y = 0.10005f }, q1 with { Y = 0.10005f }, 0f, c, out var graze));
        Assert.InRange(graze.Time, 0.49f, 0.51f);
        Assert.True(Vector3.Distance(graze.Normal, Vector3.UnitY) < 0.05f);   // taken a tenth of a millimetre early: 3 mm short of square
    }

    [Fact]
    public void Starting_inside_answers_at_once_with_the_way_out()
    {
        var c = new OrientedCapsule(new Vector3(0, 0, 0), new Vector3(0, 0, 1), 0.2f);
        Assert.True(ContactMath.Ray(new Vector3(0.1f, 0, 0.5f), Vector3.UnitY, 5f, c, out var h));
        Assert.True(h.Inside);
        Assert.Equal(0f, h.Time);
        Assert.True(Vector3.Distance(h.Normal, Vector3.UnitX) < 1e-5f);
        // A blade already through the capsule.
        Assert.True(ContactMath.Sweep(new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), new Vector3(-1, 1, 0.5f), new Vector3(1, 1, 0.5f), 0.01f, c, out var s));
        Assert.True(s.Inside);
        Assert.Equal(0f, s.Time);
        // A point inside a box, and a blade already through it.
        var box = new OrientedBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, 0.2f, 0.02f));
        Assert.True(ContactMath.Ray(new Vector3(0.1f, 0, 0.01f), Vector3.UnitX, 1f, box, out var b));
        Assert.True(b.Inside);
        Assert.True(Vector3.Distance(b.Normal, Vector3.UnitZ) < 1e-5f);   // the thin way out
        Assert.True(ContactMath.Sweep(new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), 0f, box, out var bs));
        Assert.True(bs.Inside);
    }

    [Fact]
    public void Parallel_to_the_thing_met_the_contact_comes_when_the_gap_closes()
    {
        var c = new OrientedCapsule(new Vector3(0, 0, 0), new Vector3(0, 0, 1), 0.1f);
        // A ray along the axis meets the bottom cap.
        Assert.True(ContactMath.Ray(new Vector3(0, 0, -1), Vector3.UnitZ, 3f, c, out var up));
        Assert.InRange(up.Time, 0.9f - 1e-5f, 0.9f + 1e-5f);
        Assert.True(Vector3.Distance(up.Normal, -Vector3.UnitZ) < 1e-5f);
        // A ray beside the axis, parallel to it, misses however long.
        Assert.False(ContactMath.Ray(new Vector3(0.11f, 0, -1), Vector3.UnitZ, 30f, c, out _));
        // A blade parallel to the capsule moving straight at it: the gap closes at 0.4 of the way (from 0.5 m off to 0.1 + 0.01 m).
        Assert.True(ContactMath.Sweep(new Vector3(0.5f, 0, 0.2f), new Vector3(0.5f, 0, 0.8f), new Vector3(-0.5f, 0, 0.2f), new Vector3(-0.5f, 0, 0.8f), 0.01f, c, out var s));
        Assert.InRange(s.Time, 0.39f - 2e-4f, 0.39f + 1e-6f);
        Assert.True(Vector3.Distance(s.Normal, Vector3.UnitX) < 1e-4f);
        Assert.InRange(s.Point.X, 0.1f - 1e-4f, 0.1f + 1e-4f);
        // A blade lying in a box's face plane, sliding along it, never meets it; pushed into it, meets the face.
        var box = new OrientedBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, 0.3f, 0.02f));
        Assert.False(ContactMath.Sweep(new Vector3(-1, 0, 0.05f), new Vector3(-0.5f, 0, 0.05f), new Vector3(0.5f, 0, 0.05f), new Vector3(1, 0, 0.05f), 0.01f, box, out _));
        Assert.True(ContactMath.Sweep(new Vector3(-0.2f, 0, 0.5f), new Vector3(0.2f, 0, 0.5f), new Vector3(-0.2f, 0, -0.5f), new Vector3(0.2f, 0, -0.5f), 0f, box, out var face));
        Assert.InRange(face.Time, 0.48f - 2e-4f, 0.48f + 1e-6f);
        Assert.True(Vector3.Distance(face.Normal, Vector3.UnitZ) < 1e-4f);
    }

    [Fact]
    public void A_fast_blade_does_not_jump_a_thin_capsule_that_sampled_ticks_would_miss()
    {
        // A hand 4 cm thick; the blade's tip moves 0.6 m in a tick, its middle 0.3 m: the start and end poses are clear of the hand on either side.
        var hand = new OrientedCapsule(new Vector3(0.5f, 0, 0.9f), new Vector3(0.5f, 0, 1.0f), 0.02f);
        var hilt = new Vector3(0, 0, 1);
        Vector3 Tip(float yaw) => hilt + new Vector3(DetMath.Cos(yaw), DetMath.Sin(yaw), 0) * 1f;
        float from = -0.3f, to = 0.3f;
        Assert.False(ContactMath.Moving(Tip(from), Tip(from), 0f, hand, out _));
        var mid0 = Vector3.Lerp(hilt, Tip(from), 0.5f);
        Assert.True(Vector3.Distance(Geometry.ClosestOnSegment(mid0, hand.A, hand.B, out _), mid0) > 0.1f);
        Assert.True(ContactMath.Sweep(hilt, Tip(from), hilt, Tip(to), 0.005f, hand, out var h));
        Assert.InRange(h.Time, 0.4f, 0.5f);
    }

    [Fact]
    public void A_moving_point_meets_capsules_boxes_and_the_level_at_the_share_of_its_step()
    {
        var c = new OrientedCapsule(new Vector3(0, 0, 0), new Vector3(0, 0, 1), 0.1f);
        Assert.True(ContactMath.Moving(new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), 0f, c, out var h));
        Assert.InRange(h.Time, 0.45f - 1e-5f, 0.45f + 1e-5f);
        Assert.True(ContactMath.Moving(new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), 0.05f, c, out var ball));   // a ball of 5 cm
        Assert.InRange(ball.Time, 0.425f - 1e-5f, 0.425f + 1e-5f);
        Assert.InRange(ball.Point.X, -0.1f - 1e-5f, -0.1f + 1e-5f);
        var box = new OrientedBox(new Vector3(0, 0, 0.5f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.2f, 0.2f, 0.2f));
        Assert.True(ContactMath.Moving(new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), 0f, box, out var b));
        Assert.InRange(b.Time, 0.4f - 1e-5f, 0.4f + 1e-5f);
        Assert.True(ContactMath.Moving(new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), 0.05f, box, out var bb));
        Assert.InRange(bb.Time, 0.375f - 2e-4f, 0.375f + 1e-6f);
        // The level: a floor at z 0 and a wall at x 2.
        var level = new TestLevel().Floor(-10, -10, 10, 10).Box(new Vector3(2, -5, 0), new Vector3(2.5f, 5, 3)).Build().Collision;
        Assert.True(ContactMath.Moving(new Vector3(0, 0, 1), new Vector3(4, 0, 1), 0f, level, out var wall, out int tri));
        Assert.InRange(wall.Time, 0.5f - 1e-4f, 0.5f + 1e-4f);
        Assert.True(Vector3.Distance(wall.Normal, -Vector3.UnitX) < 1e-4f);
        Assert.True(tri >= 0);
        Assert.True(ContactMath.Moving(new Vector3(0, 0, 1), new Vector3(0, 0, -1), 0f, level, out var floor, out _));
        Assert.InRange(floor.Time, 0.5f - 1e-4f, 0.5f + 1e-4f);
        Assert.False(ContactMath.Moving(new Vector3(0, 0, 1), new Vector3(1, 0, 1), 0f, level, out _, out _));
    }

    [Fact]
    public void The_same_sweep_twice_gives_the_same_bits()
    {
        var rng = new Rng(19);
        for (int n = 0; n < 50; n++)
        {
            var c0 = new OrientedCapsule(RandomPoint(rng, 0.4f), RandomPoint(rng, 0.4f), 0.1f);
            var c1 = new OrientedCapsule(c0.A + RandomPoint(rng, 0.1f), c0.B + RandomPoint(rng, 0.1f), 0.1f);
            Vector3 p0 = RandomPoint(rng, 1f), q0 = RandomPoint(rng, 1f), p1 = RandomPoint(rng, 1f), q1 = RandomPoint(rng, 1f);
            bool a = ContactMath.Sweep(p0, q0, p1, q1, 0.01f, c0, c1, out var ha);
            bool b = ContactMath.Sweep(p0, q0, p1, q1, 0.01f, c0, c1, out var hb);
            Assert.Equal(a, b);
            Assert.Equal(ha, hb);
        }
    }
}
