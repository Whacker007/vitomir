// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// the camera went wild when the player hit a wall at an odd angle: the third-person camera at
/// walls. On a play-test's tape the camera went from 2.5 m behind him to his head in a tick as he ran past a block's corner (the boom's sphere grazed
/// the corner beside its line, from a pivot the shoulder had pressed against the block), then crept back out over a second. The boom's
/// clearance (<see cref="CameraSystem.BoomClear"/>) on its own, and a person sprinting at a slant into a block and round its corner as he did:
/// the camera moves calmly; a wall truly behind still brings it in; <see cref="CameraSettings.HardWalls"/> is the earlier build's. The person needs the
/// models repository's files (skipped without them).
/// </summary>
public sealed class CameraWallsTests(ITestOutputHelper output)
{
    [Fact]
    public void A_corner_the_boom_only_grazes_does_not_pull_the_camera_in_and_a_wall_across_it_does()
    {
        // A block whose corner is at the origin (x below 0, y above 0); the pivot just south-east of it, the boom going back east and a
        // little north, its line passing 0.15 m from the corner's edge.
        var level = new TestLevel().Floor(-20, -20, 20, 20).Box(new(-6, 0, 0), new(0, 6, 3)).Build().Collision;
        var pivot = new Vector3(-0.3f, -0.22f, 1.7f);
        var boom = Vector3.Normalize(new Vector3(1f, 0.1f, 0.3f)) * 2.5f;
        var swept = level.SphereSweep(pivot, Vector3.Normalize(boom), 2.5f, 0.2f);
        Assert.NotNull(swept);   // The earlier build's sphere stops at once
        output.WriteLine($"the swept sphere stops at {swept!.Value.Distance:0.000} m; the clear boom is {CameraSystem.BoomClear(level, pivot, boom, 0.2f):0.000} m");
        Assert.True(swept.Value.Distance < 0.3f);
        Assert.Equal(2.5f, CameraSystem.BoomClear(level, pivot, boom, 0.2f), 3);

        // A wall across the boom 1.2 m back: the camera stands the probe's radius before it, as before.
        var wall = new TestLevel().Floor(-20, -20, 20, 20).Box(new(1.2f, -5, 0), new(2, 5, 3)).Build().Collision;
        var back = new Vector3(2.5f, 0f, 0f);
        float clear = CameraSystem.BoomClear(wall, new Vector3(0, 0, 1.7f), back, 0.2f);
        Assert.InRange(clear, 0.95f, 1.0f);
        // Nothing in the way: the whole boom.
        Assert.Equal(2.5f, CameraSystem.BoomClear(new TestLevel().Floor(-20, -20, 20, 20).Build().Collision, new Vector3(0, 0, 1.7f), back, 0.2f), 4);
    }

    [Fact]
    public void Sprinting_at_a_slant_into_a_block_and_round_its_corner_the_camera_stays_calm()
    {
        var (assets, kind, _) = PeopleWorlds.Player();
        var results = new Dictionary<string, (float Jump, float Shortest, int Over)>();
        foreach (var (name, cameras) in new[] { ("hard", new CameraSettings { HardWalls = true }), ("calm", new CameraSettings()) })
        {
            // The corner of a play-test's tape at 25.6 s, turned to the origin: a block west of x = 0 and north of y = 0; the player sprints at −163°
            // (west, a little south) into its east face, slides along it to the corner and runs on along its south face.
            var level = new TestLevel().Floor(-30, -30, 30, 30).Box(new(-8, 0, 0), new(0, 8, 3)).Build();
            var (w, e) = Make(assets, kind, level, cameras, new Vector3(2.2f, 1.0f, 0.01f), -163f * DetMath.DegToRad);
            var view = w.Service<ViewState>();
            var feet = w.Get<Body>(e).Feet;
            var camera = view.View.Eye;
            float jump = 0f, shortest = float.MaxValue;
            int over = 0;
            for (int t = 0; t < 2 * World.TicksPerSecond; t++)
            {
                Drive(w, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Sprint, default), 1);
                var nowFeet = w.Get<Body>(e).Feet;
                // The camera's move this tick less the body's: what the picture shows as the camera lurching.
                float moved = Vector3.Distance(view.View.Eye - camera, nowFeet - feet);
                jump = MathF.Max(jump, moved);
                if (moved > 0.1f) over++;
                shortest = MathF.Min(shortest, view.Boom);
                (feet, camera) = (nowFeet, view.View.Eye);
            }
            Assert.True(w.Get<Body>(e).Feet.X < -1f && w.Get<Body>(e).Feet.Y < 0f, $"{name}: the run went round the corner ({w.Get<Body>(e).Feet})");
            results[name] = (jump, shortest, over);
            output.WriteLine($"{name}: the camera's largest move a tick beyond the body's {jump:0.000} m, {over} ticks over 0.1 m; the shortest boom {shortest:0.00} m");
        }
        // The boom collapsed to the head (that tape: 2.43 m in a tick) and crept back out. a few centimetres a tick into the
        // wall and along it; once, as the line from the camera to him swept across the block's corner (the camera's way in along the boom is
        // against the block's face: no room to ease through), the camera comes in about half a metre in a tick, never to his head.
        Assert.True(results["hard"].Jump > 1f && results["hard"].Shortest < 0.5f, "the hard walls' camera is reproduced");
        Assert.InRange(results["calm"].Over, 0, 1);
        Assert.InRange(results["calm"].Jump, 0f, 0.6f);
        Assert.True(results["calm"].Shortest > 1.5f);
    }

    [Fact]
    public void Sprinting_along_a_wall_at_the_shoulder_with_the_look_a_degree_or_two_either_way_the_boom_does_not_flicker()
    {
        var (assets, kind, _) = PeopleWorlds.Player();
        var results = new Dictionary<string, (int Flips, float Shortest)>();
        foreach (var (name, cameras) in new[] { ("hard", new CameraSettings { HardWalls = true }), ("calm", new CameraSettings()) })
        {
            // A wall just right of him (east, his shoulder's side) as he sprints north; the look turned 1.5° left and back every 1/12 s:
            // into the wall and parallel to it, as a hand on a mouse does.
            var level = new TestLevel().Floor(-30, -30, 30, 30).Box(new(0.32f, -20, 0), new(2, 20, 3)).Build();
            var (w, e) = Make(assets, kind, level, cameras, new Vector3(0, -8, 0.01f), DetMath.HalfPi);
            var view = w.Service<ViewState>();
            float last = view.Boom, shortest = float.MaxValue;
            int flips = 0;
            for (int t = 0; t < 2 * World.TicksPerSecond; t++)
            {
                float look = t % 20 == 0 ? 1.5f * DetMath.DegToRad : t % 20 == 10 ? -1.5f * DetMath.DegToRad : 0f;
                Drive(w, new ActionFrame(new Vector2(0, 1), new Vector2(look, 0f), Buttons.Sprint, default), 1);
                if (MathF.Abs(view.Boom - last) > 0.5f) flips++;
                shortest = MathF.Min(shortest, view.Boom);
                last = view.Boom;
            }
            results[name] = (flips, shortest);
            output.WriteLine($"{name}: the boom jumped more than 0.5 m in a tick {flips} times; the shortest boom {shortest:0.00} m");
        }
        Assert.True(results["hard"].Flips >= 2 && results["hard"].Shortest < 0.5f, "the hard walls' flicker is reproduced");
        Assert.Equal(0, results["calm"].Flips);
        Assert.True(results["calm"].Shortest > 1.5f);
    }

    [Fact]
    public void A_wall_behind_a_person_still_brings_the_camera_in_and_it_lets_out_eased()
    {
        var (assets, kind, _) = PeopleWorlds.Player();
        // A wall 1 m behind (−Y); the person sprints away from it, as SteadyEyeTests does with Christof.
        var level = new TestLevel().Floor(-30, -30, 30, 30).Box(new(-5, -3, 0), new(5, -1, 3)).Build();
        var (w, e) = Make(assets, kind, level, new CameraSettings(), new Vector3(0, 0, 0.01f), DetMath.HalfPi);
        var view = w.Service<ViewState>();
        float start = view.Boom, most = 0f, last = start;
        for (int t = 0; t < 3 * World.TicksPerSecond; t++)
        {
            Drive(w, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Sprint, default), 1);
            most = MathF.Max(most, view.Boom - last);
            last = view.Boom;
        }
        output.WriteLine($"the boom from {start:0.00} m to {view.Boom:0.00} m, let out at most {most * 1000f:0} mm a tick");
        Assert.True(start < 1.5f && view.Boom > 2.4f);
        Assert.True(most < 0.1f);
    }

    [Fact]
    public void The_calm_camera_at_a_wall_allocates_nothing_a_tick()
    {
        var (assets, kind, _) = PeopleWorlds.Player();
        // Against the wall of the flicker test, looking a little into it: the shoulder, the boom's search and its ease all run.
        var level = new TestLevel().Floor(-30, -30, 30, 30).Box(new(0.32f, -20, 0), new(2, 20, 3)).Build();
        var (w, _) = Make(assets, kind, level, new CameraSettings(), new Vector3(0, -8, 0.01f), DetMath.HalfPi - 0.05f);
        var camera = new CameraSystem();
        for (int i = 0; i < 10; i++) camera.Run(w);
        Allocations.None("the calm camera's 200 runs at a wall", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) camera.Run(w);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    static (World World, EntityId Person) Make(CharacterAssets assets, int kind, LevelSpace level, CameraSettings cameras, Vector3 at, float yaw)
    {
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level, cameras);
        b.Animation(assets);
        b.Service(new DirectionPace());
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at, yaw, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        Drive(w, ActionFrame.Idle, 30);
        Assert.Equal(ViewKind.Third, w.Get<CameraRig>(e).View);
        return (w, e);
    }

    static void Drive(World w, ActionFrame frame, int ticks)
    {
        for (int i = 0; i < ticks; i++) { w.Service<TickInput>().Set(frame); w.Step(); }
    }
}
