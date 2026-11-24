// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The edges of the world: a level's exits read from its manifest, the
/// player's body stopped at an exit's volume with one <see cref="ExitReached"/> for each time it walks up to
/// it (the townsfolk walking through), a body that falls out of the world put back where it last stood, the
/// Old Town's exit 9 at the smithy's door in a cooked package when one is given, and the centred window's
/// arithmetic.
/// </summary>
public sealed class WorldEdgesTests(ITestOutputHelper output)
{
    const string Manifest = """
        { "format": "vitomir-l2", "version": 2, "name": "t",
          "bounds": { "min": [-10, -10, -2], "max": [10, 10, 5] }, "cells": [],
          "exits": [
            { "chronicle": "", "location": "", "number": 9, "to": "SmithyDay", "toLevel": "P1_SMYD", "entrance": 0, "closed": false,
              "door": "smithy", "at": [-8, 17.28, 1.22],
              "volume": { "origin": [-10, 15.28, -1.28], "rotZ": 0.5, "size": [4, 4, 4.5] } },
            { "number": 7, "to": "VysMountain", "toLevel": "P2_VYSM", "closed": true } ] }
        """;

    [Fact]
    public void An_exit_is_read_from_a_manifest_with_its_sign_and_its_turned_volume()
    {
        var exits = LevelExits.From(L2Manifest.Read(Manifest).Exits);
        Assert.Equal(2, exits.Count);
        Assert.Equal(1, exits.WalkIn);
        var nine = exits.Number(9)!;
        Assert.Equal(("SmithyDay", "P1_SMYD", false, "smithy"), (nine.To, nine.ToLevel, nine.Closed, nine.Door));
        Assert.Equal(new Vector3(-8, 17.28f, 1.22f), nine.At);
        var box = nine.Volume!.Value;
        Assert.Equal(new Vector3(4, 4, 4.5f), box.Size);
        Assert.True(box.Contains(box.Centre));
        Assert.Equal(box.Origin, box.ToLevel(Vector3.Zero));
        // Turned half a radian: its own x side runs that way from the corner.
        var alongX = box.ToLevel(new Vector3(3, 0, 0));
        Assert.Equal(-10f + 3f * DetMath.Cos(0.5f), alongX.X, 4);
        Assert.Equal(15.28f + 3f * DetMath.Sin(0.5f), alongX.Y, 4);
        Assert.True(Vector3.Distance(new Vector3(1, 2, 3), box.ToLocal(box.ToLevel(new Vector3(1, 2, 3)))) < 1e-4f);
        Assert.Equal("exit 9: to SmithyDay (P1_SMYD)", nine.Name);
        var seven = exits.Number(7)!;
        Assert.Null(seven.Volume);
        Assert.Null(seven.At);
        Assert.Equal("exit 7 (shut): to VysMountain (P2_VYSM)", seven.Name);
        Assert.Null(exits.Number(3));
    }

    /// <summary>A floor 30 m by 20 m with exit 4's volume, 4 m square, standing on it from x = 5 to 9.</summary>
    internal static LevelSpace DoorLevel()
    {
        var floor = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var exit = new LevelExit(4, "ConventDay", "P1_CTDY", false, "door_4", new Vector3(7, 0, 1.5f),
                                 new TurnedBox(new Vector3(5, -2, -1), 0f, new Vector3(4, 4, 4.5f)));
        return new LevelSpace("door", floor.Collision) { Exits = new LevelExits([exit]) };
    }

    internal static (World World, EntityId Player) Play(LevelSpace level, Vector3 at, float yaw = 0f)
    {
        var w = MovementServices.Register(new WorldBuilder(3), level).Build();
        return (w, MovementServices.SpawnControlled(w, at, yaw, Locomotion.Sandbox));
    }

    [Fact]
    public void A_body_running_into_an_exit_stops_at_its_edge_and_the_exit_is_reached_once()
    {
        var (w, player) = Play(DoorLevel(), new Vector3(0, 0.5f, 0.01f));
        var reached = new List<ExitReached>();
        float lastX = 0f, mostBack = 0f;
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default));
            reached.AddRange(w.Events<ExitReached>().ThisTick);
            var x = w.Get<Body>(player).Feet.X;
            mostBack = MathF.Max(mostBack, lastX - x);
            lastX = x;
            Assert.True(x < 5f - 0.3f + 0.01f, $"tick {i}: the feet went into the volume, x {x}");
        }
        var body = w.Get<Body>(player);
        output.WriteLine($"held at {body.Feet}, {body.Velocity} m/s, {reached.Count} reached");
        Assert.InRange(body.Feet.X, 4.65f, 4.701f);
        Assert.Equal(BodyState.Ground, body.State);
        Assert.True(mostBack < 0.06f, $"a jolt back of {mostBack} m");
        var r = Assert.Single(reached);
        Assert.Equal((4, "ConventDay", "P1_CTDY", false, player), (r.Number, r.To, r.ToLevel, r.Closed, r.Entity));

        // Back off a metre and run in again: reached a second time.
        for (int i = 0; i < World.TicksPerSecond / 2; i++) MovementServices.Step(w, new ActionFrame(new Vector2(0, -1), Vector2.Zero, Buttons.None, default));
        Assert.True(w.Get<Body>(player).Feet.X < 4.2f);
        Assert.Empty(w.Events<ExitReached>().ThisTick);
        int again = 0;
        for (int i = 0; i < World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default));
            again += w.Events<ExitReached>().ThisTick.Count;
        }
        Assert.Equal(1, again);
    }

    [Fact]
    public void A_body_running_slantwise_into_an_exit_slides_along_its_face_and_round_its_corner()
    {
        // Facing 30° to the left of +X, so it meets the face at x = 5 and goes on along it towards +Y.
        var (w, player) = Play(DoorLevel(), new Vector3(0, -1.5f, 0.01f), 30f * DetMath.DegToRad);
        bool held = false;
        for (int i = 0; i < 5 * World.TicksPerSecond && w.Get<Body>(player).Feet.Y < 2.4f; i++)
        {
            MovementServices.Step(w, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default));
            var feet = w.Get<Body>(player).Feet;
            if (feet.Y < 2.29f) Assert.True(feet.X < 4.701f, $"tick {i}: into the volume at {feet}");
            held |= feet.X > 4.65f;
        }
        Assert.True(held, "it never came to the face");
        Assert.True(w.Get<Body>(player).Feet.Y >= 2.4f, $"it did not slide along the face: {w.Get<Body>(player).Feet}");
    }

    [Fact]
    public void The_townsfolk_walk_through_an_exit_and_a_player_starting_inside_one_walks_out()
    {
        var (w, player) = Play(DoorLevel(), new Vector3(7, 0, 0.01f));
        var townsman = MovementServices.SpawnBody(w, new Vector3(0, -1, 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            w.Get<MoveIntent>(townsman).Direction = new Vector2(1, 0);
            var walk = i < 2 * World.TicksPerSecond ? new Vector2(0, 1) : Vector2.Zero;
            MovementServices.Step(w, new ActionFrame(walk, Vector2.Zero, Buttons.None, default));
            Assert.Empty(w.Events<ExitReached>().ThisTick);
        }
        Assert.True(w.Get<Body>(player).Feet.X > 9.5f, $"the player did not walk out: {w.Get<Body>(player).Feet}");
        Assert.True(w.Get<Body>(townsman).Feet.X > 9.5f, $"the townsman was stopped: {w.Get<Body>(townsman).Feet}");
        // The player, walking back in from the far side, is stopped at the face it comes to.
        w.Get<MoveIntent>(townsman).Direction = Vector2.Zero;
        int reached = 0;
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, new ActionFrame(new Vector2(0, -1), Vector2.Zero, Buttons.None, default));
            reached += w.Events<ExitReached>().ThisTick.Count;
        }
        Assert.InRange(w.Get<Body>(player).Feet.X, 9.299f, 9.35f);
        Assert.Equal(1, reached);
    }

    [Fact]
    public void A_body_that_falls_out_of_the_world_is_put_back_where_it_stood_a_little_before_the_edge()
    {
        // A floor that ends at x = 2, with nothing beyond it.
        var level = new TestLevel().Floor(-10, -10, 2, 10).Build();
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var e = MovementServices.SpawnBody(w, new Vector3(-6, 0, 0.01f), 0f, Locomotion.Sandbox);
        var recovered = new List<BodyRecovered>();
        float lowest = 0f;
        for (int i = 0; i < 6 * World.TicksPerSecond; i++)
        {
            w.Get<MoveIntent>(e).Direction = recovered.Count == 0 ? new Vector2(1, 0) : Vector2.Zero;
            w.Step();
            recovered.AddRange(w.Events<BodyRecovered>().ThisTick);
            lowest = MathF.Min(lowest, w.Get<Body>(e).Feet.Z);
        }
        var r = Assert.Single(recovered);
        output.WriteLine($"fell to {r.From}, put back at {r.To} (tick {r.Tick})");
        Assert.True(r.From.Z < level.Bounds.Min.Z - WorldEdges.FallMargin);
        Assert.True(lowest > level.Bounds.Min.Z - WorldEdges.FallMargin - 0.5f, $"it fell to {lowest} before it was caught");
        // Stood on, and back from the edge by a quarter of a second's running or more.
        Assert.InRange(r.To.X, -6f, 2f - 3.0f * 0.25f);
        Assert.Equal(0f, r.To.Z, 2);
        var body = w.Get<Body>(e);
        Assert.Equal(BodyState.Ground, body.State);
        Assert.Equal(r.To.X, body.Feet.X, 3);
        Assert.Equal(0f, body.Velocity.Length(), 3);
    }

    [Fact]
    public void A_body_never_put_on_ground_goes_back_where_it_was_first_seen()
    {
        var level = new TestLevel().Floor(-10, -10, 2, 10).Build();
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var e = MovementServices.SpawnBody(w, new Vector3(5, 0, 1f), 0f, Locomotion.Sandbox);
        int count = 0;
        for (int i = 0; i < 4 * World.TicksPerSecond && count == 0; i++)
        {
            w.Step();
            foreach (var r in w.Events<BodyRecovered>().ThisTick) { count++; Assert.Equal(5f, r.To.X, 3); }
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void The_world_steps_the_same_with_its_edges()
    {
        ulong Run()
        {
            var (w, _) = Play(DoorLevel(), new Vector3(0, 0.5f, 0.01f));
            for (int i = 0; i < 3 * World.TicksPerSecond; i++)
                MovementServices.Step(w, new ActionFrame(new Vector2(0.3f, 1), new Vector2(i % 50 == 0 ? 0.2f : 0f, 0), Buttons.None, default));
            return w.Digest();
        }
        Assert.Equal(Run(), Run());
    }

    // ---- the Old Town's exit 9, in a cooked package ----

    static readonly Lazy<LevelSpace?> OldTown = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_EXITS_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    [Fact]
    public void In_the_cooked_old_town_the_player_runs_to_the_smithy_and_is_stopped_at_exit_9()
    {
        Assert.SkipUnless(OldTown.Value is not null, "no cooked package with exits (VITOMIR_EXITS_LEVEL, e.g. P1_OTDY cooked by tools/platform/cook-1-1-level.ps1)");
        var level = OldTown.Value!;
        var nine = level.Exits.Number(9);
        Assert.NotNull(nine);
        Assert.NotNull(nine!.Volume);
        Assert.NotNull(nine.At);
        output.WriteLine($"{nine.Name}: sign at {nine.At}, volume {nine.Volume}; {level.Exits.WalkIn} of {level.Exits.Count} walk-in");
        // From the Old Town's own start for the smithy's entrance (1.1's playerstart09s stand outside its door).
        var start = level.Starts.First(s => s.Group == 9);
        var (w, player) = Play(level, start.Position, start.Yaw);
        var sign = new Vector2(nine.At!.Value.X, nine.At.Value.Y);
        var reached = new List<ExitReached>();
        for (int i = 0; i < 8 * World.TicksPerSecond; i++)
        {
            var feet = w.Get<Body>(player).Feet;
            var to = sign - new Vector2(feet.X, feet.Y);
            float turn = DetMath.WrapAngle(DetMath.Atan2(to.Y, to.X) - w.Get<CameraRig>(player).Yaw);
            MovementServices.Step(w, new ActionFrame(new Vector2(0, 1), new Vector2(turn, 0), Buttons.None, default));
            reached.AddRange(w.Events<ExitReached>().ThisTick);
            Assert.False(ExitThreshold.Overlaps(nine.Volume!.Value, w.Get<Body>(player).Feet, 0.3f - 0.01f, 1.8f), $"tick {i}: in the volume at {w.Get<Body>(player).Feet}");
        }
        var held = w.Get<Body>(player).Feet;
        output.WriteLine($"start {start.Position} → held at {held}, {Vector2.Distance(sign, new Vector2(held.X, held.Y)):0.00} m from the sign; reached {string.Join(", ", reached.Select(r => r.Number))}");
        var r = Assert.Single(reached);
        Assert.Equal(9, r.Number);
        Assert.Equal("SmithyDay", r.To);
        Assert.True(ExitThreshold.Clearance(nine.Volume!.Value, held, 0.3f) < 0.05f, "not held at the volume's edge");
    }

    // ---- the centred window ----

    [Fact]
    public void The_window_is_centred_on_the_display_s_whole_bounds_and_cut_to_a_smaller_display()
    {
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), WindowPlacement.Centre(new DisplayRect(0, 0, 7680, 1440), 2560, 1440));
        Assert.Equal(new DisplayRect(0, 0, 1920, 1080), WindowPlacement.Centre(new DisplayRect(0, 0, 1920, 1080), 2560, 1440));
        Assert.Equal(new DisplayRect(-1600, 180, 1280, 720), WindowPlacement.Centre(new DisplayRect(-1920, 0, 1920, 1080), 1280, 720));
        Assert.Equal(new DisplayRect(0, 0, 2560, 1080), WindowPlacement.Centre(new DisplayRect(0, 0, 2560, 1080), 2560, 1440));
        Assert.True(WindowPlacement.OnTop(focused: true));
        Assert.False(WindowPlacement.OnTop(focused: false));
        Assert.Equal("window: centred, no title bar, 2560x1440 at 2560,0 on Surround (7680x1440 at 0,0)",
                     WindowPlacement.Describe(2560, 1440, new DisplayRect(2560, 0, 2560, 1440), new DisplayRect(0, 0, 7680, 1440), "Surround"));
        Assert.EndsWith("the display being smaller than the 2560x1440 asked",
                        WindowPlacement.Describe(2560, 1440, new DisplayRect(0, 0, 1920, 1080), new DisplayRect(0, 0, 1920, 1080), ""));
    }
}
