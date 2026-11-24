// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The level's collision queries: the capsule sweep against the distance it
/// is built on, the tree against every triangle asked one by one, the push out of a corner, merged
/// cells answering as one, and a sliver of a triangle met only where it is.
/// </summary>
public sealed class CollisionWorldTests
{
    static Vector3 RandomPoint(Rng rng, float size) => new(rng.NextFloat(-size, size), rng.NextFloat(-size, size), rng.NextFloat(-size, size));

    static Vector3 RandomDirection(Rng rng)
    {
        while (true)
        {
            var v = RandomPoint(rng, 1);
            float l = v.Length();
            if (l > 0.1f && l <= 1f) return v / l;
        }
    }

    [Fact]
    public void A_capsule_sweep_stops_where_the_capsule_first_touches_a_triangle_and_never_passes_through_one()
    {
        var rng = new Rng(3);
        int hits = 0, misses = 0;
        for (int k = 0; k < 4000; k++)
        {
            var a = RandomPoint(rng, 2); var b = a + RandomPoint(rng, 1.5f); var c = a + RandomPoint(rng, 1.5f);
            var cap = new Capsule(RandomPoint(rng, 3), rng.NextFloat(0, 1.5f), rng.NextFloat(0.05f, 0.5f));
            // Aimed roughly at the triangle, so that about half the sweeps meet it.
            var dir = Vector3.Normalize((a + b + c) / 3f + RandomPoint(rng, 1.2f) - (cap.Lower + new Vector3(0, 0, cap.Segment * 0.5f)) + RandomDirection(rng) * 1e-3f);
            const float length = 6f;
            float Dist(float t)
            {
                var p = cap.Lower + dir * t;
                return MathF.Sqrt(SweepMath.SegmentTriangle(p, p + new Vector3(0, 0, cap.Segment), a, b, c, out _, out _));
            }
            if (Dist(0) <= cap.Radius + 1e-3f) continue;      // starting clear only
            var hit = SweepMath.CapsuleTriangle(cap.Lower, Vector3.UnitZ, cap.Segment, dir, length, cap.Radius, a, b, c);
            if (hit is { } t)
            {
                hits++;
                Assert.InRange(Dist(t), cap.Radius - 2e-3f, cap.Radius + 2e-3f);
                for (int i = 0; i < 200; i++)
                    Assert.True(Dist(t * i / 200f) >= cap.Radius - 2e-3f, $"case {k}: inside before the hit at {t}");
            }
            else
            {
                misses++;
                for (int i = 0; i <= 600; i++)
                    Assert.True(Dist(length * i / 600f) >= cap.Radius - 2e-3f, $"case {k}: passed through at {length * i / 600f}");
            }
        }
        Assert.True(hits > 300 && misses > 300, $"{hits} hits, {misses} misses");
    }

    [Fact]
    public void The_tree_finds_the_same_first_contact_as_asking_every_triangle()
    {
        var rng = new Rng(11);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (int t = 0; t < 600; t++)
        {
            var a = RandomPoint(rng, 20);
            vertices.Add(a); vertices.Add(a + RandomPoint(rng, 2)); vertices.Add(a + RandomPoint(rng, 2));
            indices.Add(3 * t); indices.Add(3 * t + 1); indices.Add(3 * t + 2);
        }
        var world = CollisionWorld.FromTriangles([.. vertices], [.. indices]);
        for (int k = 0; k < 300; k++)
        {
            var cap = new Capsule(RandomPoint(rng, 20), rng.NextFloat(0, 1.5f), rng.NextFloat(0.1f, 0.5f));
            var dir = RandomDirection(rng);
            float best = float.MaxValue;
            for (int t = 0; t < world.Triangles; t++)
            {
                var (a, b, c) = world.Corners(t);
                if (SweepMath.CapsuleTriangle(cap.Lower, Vector3.UnitZ, cap.Segment, dir, 15f, cap.Radius, a, b, c) is { } d) best = MathF.Min(best, d);
            }
            var hit = world.CapsuleSweep(cap, dir, 15f);
            if (best == float.MaxValue) Assert.Null(hit);
            else Assert.Equal(best, hit!.Value.Distance, 4);

            var ray = world.Ray(cap.Lower, dir, 15f);
            var sphere = world.SphereSweep(cap.Lower, dir, 15f, cap.Radius);
            if (ray is { } r && sphere is { } s) Assert.True(s.Distance <= r.Distance + 1e-4f);
        }
    }

    [Fact]
    public void Depenetrate_pushes_a_capsule_out_of_a_floor_and_a_corner_until_it_is_clear()
    {
        var level = new TestLevel().Floor(-5, -5, 5, 5).Box(new(1, -5, 0), new(2, 5, 3)).Box(new(-5, 1, 0), new(5, 2, 3)).Build();
        var cap = Capsule.OfBody(new(0.85f, 0.8f, -0.1f), 0.3f, 1.8f);
        Assert.True(level.Collision.Penetration(cap) > 0.1f);
        var moved = level.Collision.Depenetrate(ref cap, 0.001f);
        Assert.True(level.Collision.Penetration(cap) <= 1e-4f, $"still {level.Collision.Penetration(cap)} in, at {cap.Lower}");
        Assert.True(moved.X < 0 && moved.Y < 0 && moved.Z > 0, $"moved {moved}");
        Assert.InRange(cap.Lower.X, 0.6f, 0.71f);
        Assert.InRange(cap.Lower.Y, 0.6f, 0.71f);
    }

    [Fact]
    public void Merged_cells_answer_as_one_with_no_seam_between_them()
    {
        var left = new TestLevel().Floor(-10, -5, 0, 5).Build().Collision;
        var right = new TestLevel().Floor(0, -5, 10, 5, 0.1f).Build().Collision;
        var merged = CollisionWorld.Merge([left, right]);
        Assert.Equal(left.Triangles + right.Triangles, merged.Triangles);
        Assert.Equal(0f, merged.Floor(-0.01f, 0, 0.5f, 1f));
        Assert.Equal(0.1f, merged.Floor(0.01f, 0, 0.5f, 1f));
        var hit = merged.CapsuleSweep(Capsule.OfBody(new(0.2f, 0, 1f), 0.3f, 1.8f), -Vector3.UnitZ, 2f);
        Assert.NotNull(hit);
        Assert.InRange(hit!.Value.Distance, 0.89f, 0.91f);
    }

    [Fact]
    public void A_sliver_triangle_is_met_only_by_its_edges_and_never_by_a_capsule_off_its_end()
    {
        // A cook's sliver along a wall's top (P1_OTDY): three corners all but in a line, whose normal is
        // rounding. A body standing on the floor in line with it, well clear of its end, must not meet it.
        var level = new TestLevel().Floor(-5, -5, 5, 5)
            .Triangle(new(0, 0, 1.3f), new(0.2f, 0.2000001f, 1.3f), new(1, 1, 1.3f)).Build().Collision;
        for (float along = 1.25f; along <= 1.5f; along += 0.05f)
        {
            var cap = Capsule.OfBody(new(along, along, 0.002f), 0.3f, 1.8f);
            var hit = level.CapsuleSweep(cap, -Vector3.UnitZ, 0.7f);
            Assert.NotNull(hit);
            Assert.InRange(hit!.Value.Distance, 0.001f, 0.003f);   // the floor, not the sliver
            Assert.True(hit.Value.Point.Z < 0.01f, $"met at {hit.Value.Point}");
        }
        // Its edge is still met: a capsule swept into it from the side stops on it.
        var into = level.CapsuleSweep(Capsule.OfBody(new(0f, 1f, 0.002f), 0.3f, 1.8f), Vector3.Normalize(new Vector3(1, -1, 0)), 2f);
        Assert.NotNull(into);
        Assert.InRange(into!.Value.Distance, 0.40f, 0.41f);
    }
}
