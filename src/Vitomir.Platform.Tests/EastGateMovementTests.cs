// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Movement on the East Gate, the new levels' test level (its L2 package found by
/// <c>VITOMIR_TEST_LEVEL</c>; skipped when absent): it loads, bodies settle at its starts, a long random
/// drive stays in the level and out of its walls, routes join its starts, wanderers wander, and
/// <see cref="SnagCheck"/> runs over it (its report printed).
/// </summary>
public sealed class EastGateMovementTests(ITestOutputHelper output)
{
    static readonly Lazy<LevelSpace?> Level = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_TEST_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    static LevelSpace Gate()
    {
        Assert.SkipUnless(Level.Value is not null, "no level package (VITOMIR_TEST_LEVEL)");
        return Level.Value!;
    }

    [Fact]
    public void The_east_gate_loads_with_its_collision_its_navigation_and_its_starts()
    {
        var level = Gate();
        Assert.True(level.Collision.Triangles > 1000, $"{level.Collision.Triangles} triangles");
        Assert.NotNull(level.Navigation);
        Assert.True(level.Navigation!.Tiles > 1 && level.Navigation.Polygons > 50, $"{level.Navigation.Tiles} tiles, {level.Navigation.Polygons} polygons");
        Assert.True(level.Starts.Count() >= 2);
        output.WriteLine($"{level.Name}: {level.Collision.Triangles} triangles, {level.Navigation.Tiles} tiles, {level.Navigation.Polygons} polygons, {level.Starts.Count()} starts");
    }

    [Fact]
    public void A_body_at_every_start_anchor_settles_on_ground_within_a_second()
    {
        var level = Gate();
        foreach (var start in level.Starts)
        {
            var body = Body.At(start.Position + new Vector3(0, 0, 0.05f), start.Yaw);
            int i = 0;
            for (; i < 120 && body.State != BodyState.Ground; i++)
                BodyMotor.Step(ref body, Locomotion.Sandbox, new MoveIntent(), level.Collision, [], 0, i);
            output.WriteLine($"{start.Id}: {(body.State == BodyState.Ground ? $"on ground after {i} ticks" : "still in the air")} at {body.Feet} (anchor {start.Position})");
            Assert.Equal(BodyState.Ground, body.State);
            Assert.True(Drive.Depth(level, body) <= 0.001f);
        }
    }

    [Fact]
    public void A_60_s_seeded_random_drive_from_a_start_never_falls_below_the_level_nor_ends_embedded()
    {
        var level = Gate();
        float floor = level.Collision.Bounds.Min.Z;
        var edge = level.Collision.Bounds;
        var rng = new Rng(2026);
        foreach (var start in level.Starts.Take(3))
        {
            var body = Body.At(start.Position + new Vector3(0, 0, 0.05f), start.Yaw);
            var intent = new MoveIntent();
            float worst = 0f, lowest = float.MaxValue, travelled = 0f;
            var was = body.Feet;
            for (int i = 0; i < 120 * 60; i++)
            {
                if (i % 90 == 0)
                {
                    float yaw = rng.NextFloat(-DetMath.Pi, DetMath.Pi);
                    var (s, c) = DetMath.SinCos(yaw);
                    intent = new MoveIntent { Direction = new Vector2(c, s), Gait = (Gait)rng.NextInt(3), Facing = yaw, Faces = true };
                }
                var now = intent;
                now.Jump = rng.NextInt(120) == 0;
                var feet = body.Feet;
                if (feet.X < edge.Min.X + 3f || feet.X > edge.Max.X - 3f || feet.Y < edge.Min.Y + 3f || feet.Y > edge.Max.Y - 3f)
                {
                    // Near the level's edge: turned back towards the start. A level with no wall round it (the proving
                    // ground) can be walked off, which is not falling through it, so the drive stays inside.
                    var back = Vector2.Normalize(new Vector2(start.Position.X - feet.X, start.Position.Y - feet.Y));
                    now.Direction = back;
                    now.Facing = MathF.Atan2(back.Y, back.X);
                }
                BodyMotor.Step(ref body, Locomotion.Sandbox, now, level.Collision, [], 0, i);
                worst = MathF.Max(worst, Drive.Depth(level, body));
                lowest = MathF.Min(lowest, body.Feet.Z);
                travelled += Vector3.Distance(was, body.Feet);
                was = body.Feet;
                Assert.True(body.Feet.Z > floor - 0.5f, $"{start.Id}: fell below the level at tick {i}: {body.Feet}");
            }
            output.WriteLine($"{start.Id}: travelled {travelled:0.0} m, lowest {lowest:0.00}, deepest {worst * 1000:0.000} mm, ended {body.Feet}");
            Assert.True(worst <= 0.001f, $"{start.Id}: ended a tick {worst * 1000:0.###} mm inside a triangle");
        }
    }

    [Fact]
    public void A_route_joins_two_start_anchors()
    {
        var level = Gate();
        var starts = level.Starts.ToList();
        int found = 0;
        for (int i = 0; i < starts.Count; i++)
            for (int j = i + 1; j < starts.Count; j++)
            {
                var route = level.Navigation!.Route(starts[i].Position, starts[j].Position);
                output.WriteLine($"{starts[i].Id} -> {starts[j].Id}: {(route is null ? "none" : $"{route.Count} corners")}");
                if (route is not null) found++;
            }
        Assert.True(found > 0);
    }

    [Fact]
    public void Five_wanderers_for_60_s_all_move_more_than_3_m()
    {
        var level = Gate();
        var w = MovementServices.Register(new WorldBuilder(5), level).Build();
        var start = level.Starts.First();
        var ids = new List<EntityId>();
        for (int k = 0; k < 5; k++)
        {
            var (s, c) = DetMath.SinCos(k * 1.2566f);
            ids.Add(MovementServices.SpawnWanderer(w, start.Position + new Vector3(c * 1.5f, s * 1.5f, 0.3f), 0f, Locomotion.Sandbox));
        }
        var from = ids.Select(e => w.Get<Body>(e).Feet).ToList();
        var far = new float[5];
        for (int t = 0; t < 120 * 60; t++)
        {
            w.Step();
            for (int k = 0; k < 5; k++) far[k] = MathF.Max(far[k], Vector3.Distance(from[k], w.Get<Body>(ids[k]).Feet));
        }
        for (int k = 0; k < 5; k++)
        {
            var r = w.Get<RouteWalker>(ids[k]);
            output.WriteLine($"wanderer {k}: farthest {far[k]:0.0} m from its start, {r.Asked} routes asked ({r.Reasked} again for want of progress), now {w.Get<Body>(ids[k]).Feet}");
        }
        Assert.All(far, f => Assert.True(f > 3f, $"a wanderer went only {f:0.00} m"));
    }

    [Fact]
    public void A_snagcheck_of_20_pairs_completes_and_reports()
    {
        var level = Gate();
        var report = SnagCheck.Run(level, new SnagCheckOptions { Pairs = 20, Seed = 14 });
        foreach (var line in report.Lines) output.WriteLine(line);
        Assert.Equal(20, report.Pairs.Count);
        Assert.StartsWith("{\"summary\":true", report.Lines[^1]);
    }

    [Fact]
    public void Random_reachable_points_are_the_same_from_the_same_seed()
    {
        var nav = Gate().Navigation!;
        var start = Gate().Starts.First().Position;
        List<Vector3> Draw(ulong seed)
        {
            var rng = new Rng(seed);
            var points = new List<Vector3>();
            for (int i = 0; i < 20; i++)
            {
                Assert.True(nav.RandomReachable(start, 40f, rng, out var p));
                points.Add(p);
                Assert.True(nav.RandomPoint(rng, out p));
                points.Add(p);
            }
            return points;
        }
        Assert.Equal(Draw(8), Draw(8));
        Assert.NotEqual(Draw(8), Draw(9));
    }

    [Fact]
    public void Two_runs_of_wanderers_on_the_east_gate_have_the_same_digest_at_every_tick()
    {
        var level = Gate();
        List<ulong> Run()
        {
            var w = MovementServices.Register(new WorldBuilder(77), level).Build();
            var start = level.Starts.First();
            for (int k = 0; k < 5; k++)
                MovementServices.SpawnWanderer(w, start.Position + new Vector3(k * 0.8f - 1.6f, 0, 0.3f), 0f, Locomotion.Sandbox);
            var digests = new List<ulong>();
            for (int t = 0; t < 120 * 15; t++) { w.Step(); digests.Add(w.Digest()); }
            return digests;
        }
        Assert.Equal(Run(), Run());
    }
}
