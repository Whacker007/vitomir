// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// <b>The no-climb flag</b> (ours): a level marks an object (its collision
/// triangles carry <see cref="L2Surface.NoClimb"/>) or an area (<c>"climb": "none"</c>, <see cref="LevelClimb"/>) as not
/// to be climbed, and the probe reads a lip there as a face with no top, so no grab, mantle, climb onto or hop up
/// starts from it; walking, stepping and standing are as without it. Pinned on small levels in memory: a block of
/// 1.6 m is mantled unflagged and refused flagged either way; so with a wall of 2.6 m (the grab) and a bench (the hop);
/// a flagged kerb of 0.3 m is still stepped onto; a manifest's area says it with <c>"climb": "none"</c> and nothing else.
/// </summary>
public sealed class NoClimbTests
{
    /// <summary>A level of triangles with a no-climb mark of their own, and the areas.</summary>
    sealed class Marked
    {
        readonly List<Vector3> v = [];
        readonly List<int> idx = [];
        readonly List<bool> marked = [];

        public Marked Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool noClimb = false)
        {
            int at = v.Count;
            v.AddRange([a, b, c, d]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            marked.AddRange([noClimb, noClimb]);
            return this;
        }

        public Marked Floor()
        {
            for (int x = -10; x < 10; x += 2)
                for (int y = -10; y < 10; y += 2)
                    Quad(new(x, y, 0), new(x + 2, y, 0), new(x + 2, y + 2, 0), new(x, y + 2, 0));
            return this;
        }

        /// <summary>A solid box (top and four sides; it stands on the floor), all its faces marked or none.</summary>
        public Marked Box(Vector3 a, Vector3 b, bool noClimb = false)
        {
            Vector3 P(float x, float y, float z) => new(x, y, z);
            Quad(P(a.X, a.Y, b.Z), P(b.X, a.Y, b.Z), P(b.X, b.Y, b.Z), P(a.X, b.Y, b.Z), noClimb);
            Quad(P(a.X, a.Y, a.Z), P(b.X, a.Y, a.Z), P(b.X, a.Y, b.Z), P(a.X, a.Y, b.Z), noClimb);
            Quad(P(a.X, b.Y, a.Z), P(a.X, b.Y, b.Z), P(b.X, b.Y, b.Z), P(b.X, b.Y, a.Z), noClimb);
            Quad(P(a.X, a.Y, a.Z), P(a.X, a.Y, b.Z), P(a.X, b.Y, b.Z), P(a.X, b.Y, a.Z), noClimb);
            Quad(P(b.X, a.Y, a.Z), P(b.X, b.Y, a.Z), P(b.X, b.Y, b.Z), P(b.X, a.Y, b.Z), noClimb);
            return this;
        }

        public LevelSpace Build(LevelClimb? climb = null)
        {
            var surfaces = new L2Surface[marked.Count];
            for (int t = 0; t < surfaces.Length; t++)
            {
                var (a, b, c) = (v[idx[3 * t]], v[idx[3 * t + 1]], v[idx[3 * t + 2]]);
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                surfaces[t] = L2Surface.Solid | (MathF.Abs(n.Z) >= 0.7f ? L2Surface.Walk : 0) | (marked[t] ? L2Surface.NoClimb : 0);
            }
            var world = CollisionWorld.FromTriangles([.. v], [.. idx], surfaces);
            return new LevelSpace("no-climb", climb is null ? world : world.WithClimb(climb));
        }
    }

    /// <summary>A deep block (to x 5) of <paramref name="height"/> whose face is at x 1, marked on its triangles or not.</summary>
    static Marked Block(float height, bool noClimb, float depth = 4f) =>
        new Marked().Floor().Box(new(1, -3, 0), new(1 + depth, 3, height), noClimb);

    /// <summary>An area marked not to be climbed in, round the block's near top edge at <paramref name="height"/>.</summary>
    static LevelClimb AreaAt(float height) =>
        LevelClimb.Of([new NoClimbArea("edge", new TurnedBox(new(0.5f, -4f, height - 0.5f), 0f, new(1.5f, 8f, 1f)))]);

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public int Grabs;
        public Body End;
        public override string ToString() => $"started [{string.Join(", ", Started)}], ended at {End.Feet} ({End.State})";
    }

    static Drove Go(LevelSpace level, Vector3 from, int ticks, Func<int, Body, MoveIntent> intent)
    {
        var d = new Drove();
        var body = Drive.Settle(level, from);
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, ticks, intent, (_, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.Grabbed) d.Grabs++;
        });
        return d;
    }

    /// <summary>Going along +X at a walk (or standing still facing it), jump pressed at <paramref name="press"/> and held for <paramref name="hold"/> ticks.</summary>
    static Func<int, Body, MoveIntent> Along(int press = -1, int hold = 1, bool still = false) => (i, b) =>
    {
        var m = still || b.Feet.X > 6f ? new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Walk } : Drive.Toward(1, 0, Gait.Walk);
        m.Jump = i == press;
        m.JumpHeld = press >= 0 && i >= press && i < press + hold;
        return m;
    };

    [Fact]
    public void A_mantle_height_block_flagged_on_its_triangles_or_in_an_area_refuses_the_mantle_and_unflagged_is_mantled()
    {
        var start = new Vector3(0.5f, 0, 0.01f);
        var plain = Go(Block(1.6f, false).Build(), start, 240, Along(press: 6, hold: 60, still: true));
        Assert.Equal([TraversalKind.Mantle], plain.Started);
        Assert.True(plain.End.Feet.Z > 1.55f, $"unflagged: on the block ({plain})");

        var byTriangles = Go(Block(1.6f, true).Build(), start, 240, Along(press: 6, hold: 60, still: true));
        Assert.Empty(byTriangles.Started);
        Assert.True(byTriangles.End.Feet.Z < 0.05f, $"flagged triangles: still on the ground ({byTriangles})");

        var byArea = Go(Block(1.6f, false).Build(AreaAt(1.6f)), start, 240, Along(press: 6, hold: 60, still: true));
        Assert.Empty(byArea.Started);
        Assert.True(byArea.End.Feet.Z < 0.05f, $"in a no-climb area: still on the ground ({byArea})");
    }

    [Fact]
    public void The_probe_reads_a_flagged_lip_as_a_face_with_no_top()
    {
        var loco = Drive.Numbers;
        var feet = new Vector3(0.5f, 0, 0);
        Assert.True(TraversalProbe.Read(Block(1.6f, false).Build().Collision, feet, Vector2.UnitX, loco, 1.0f, 2.2f, out var plain));
        Assert.True(plain.HasTop && plain.Standable && !plain.NoClimb, "unflagged: a top that can be stood on");
        foreach (var level in new[] { Block(1.6f, true).Build(), Block(1.6f, false).Build(AreaAt(1.6f)) })
        {
            Assert.True(TraversalProbe.Read(level.Collision, feet, Vector2.UnitX, loco, 1.0f, 2.2f, out var o));
            Assert.True(o.Found && o.NoClimb && !o.HasTop, "flagged: a face with no top");
            Assert.Equal(1.6f, o.Top, 2);
        }
        // An area elsewhere changes nothing here.
        var away = LevelClimb.Of([new NoClimbArea("away", new TurnedBox(new(-9f, -9f, 0f), 0f, new(2f, 2f, 3f)))]);
        Assert.True(TraversalProbe.Read(Block(1.6f, false).Build(away).Collision, feet, Vector2.UnitX, loco, 1.0f, 2.2f, out var far));
        Assert.True(far.HasTop && !far.NoClimb);
    }

    [Fact]
    public void A_flagged_wall_of_2_6_m_is_not_grabbed_and_a_flagged_bench_is_not_hopped_onto()
    {
        var wall = Go(Block(2.6f, true).Build(), new(0.6f, 0, 0.01f), 300, Along(press: 6, hold: 120));
        Assert.Equal(0, wall.Grabs);
        Assert.Empty(wall.Started);
        var plainWall = Go(Block(2.6f, false).Build(), new(0.6f, 0, 0.01f), 300, Along(press: 6, hold: 120));
        Assert.Equal(1, plainWall.Grabs);

        var bench = Go(Block(0.45f, true, depth: 0.4f).Build(), new(0.5f, 0, 0.01f), 240, Along(press: 6));
        Assert.DoesNotContain(TraversalKind.HopUp, bench.Started);
        Assert.True(bench.End.Feet.Z < 0.05f, $"a flagged bench: not on it ({bench})");
        var plainBench = Go(Block(0.45f, false, depth: 0.4f).Build(), new(0.5f, 0, 0.01f), 240, Along(press: 6));
        Assert.Contains(TraversalKind.HopUp, plainBench.Started);
    }

    [Fact]
    public void A_flagged_kerb_is_still_stepped_onto_and_walked()
    {
        var d = Go(Block(0.3f, true).Build(AreaAt(0.3f)), new(-1f, 0, 0.01f), 360, Along());
        Assert.Empty(d.Started);
        Assert.True(d.End.Feet.X > 1.5f && MathF.Abs(d.End.Feet.Z - 0.3f) < 0.02f, $"on the kerb and along it ({d})");
    }

    [Fact]
    public void A_manifests_area_says_it_with_climb_none_and_anything_else_is_a_problem()
    {
        const string Json = """
            { "format": "vitomir-l2", "version": 1, "name": "t", "areas": [
              { "name": "roof", "kind": "zone", "climb": "none", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [2, 2, 2] } },
              { "name": "cart", "kind": "zone", "climb": "no", "box": { "origin": [5, 0, 0], "rotZ": 0, "size": [2, 2, 2] } },
              { "name": "room", "kind": "interior", "box": { "origin": [9, 0, 0], "rotZ": 0, "size": [2, 2, 2] } } ] }
            """;
        var climb = LevelClimb.From(L2Manifest.Read(Json));
        Assert.Equal(["roof"], climb.Areas.Select(a => a.Name));
        Assert.Single(climb.Problems);
        Assert.Contains("cart", climb.Problems[0]);
        Assert.True(climb.NoClimbAt(new(1, 1, 1)));
        Assert.False(climb.NoClimbAt(new(6, 1, 1)));
        Assert.Same(LevelClimb.None, LevelClimb.From(L2Manifest.Read("""{ "format": "vitomir-l2", "version": 1, "name": "t" }""")));
    }
}
