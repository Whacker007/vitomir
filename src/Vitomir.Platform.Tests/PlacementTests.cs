// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The player placed before the first tick (ours, the program's <c>--place</c> in play as well as in stills):
/// the text read or refused, the body put there facing the way asked with the view turned, and a recording that
/// carries the placement in its head replaying from the file with the same digests — while the same frames
/// replayed from the start anchor part at once.
/// </summary>
public sealed class PlacementTests
{
    [Fact]
    public void A_placement_reads_three_to_five_numbers_and_refuses_anything_else()
    {
        Assert.True(Placement.TryParse("1,2,3", out var p, out _));
        Assert.Equal(new Placement(new(1, 2, 3), null, null), p);
        Assert.True(Placement.TryParse(" 12.5 , -3 , 0.2 , 90 ", out p, out _));
        Assert.Equal(new Placement(new(12.5f, -3, 0.2f), 90f, null), p);
        Assert.True(Placement.TryParse("1,2,3,90,180", out p, out _));
        Assert.Equal((90f, 180f), (p.YawDegrees, p.ViewDegrees));
        foreach (var bad in new[] { null, "", "1,2", "1,2,3,4,5,6", "a,b,c", "1;2;3", "1,2,NaN", "1,2,Infinity" })
        {
            Assert.False(Placement.TryParse(bad, out _, out string error));
            Assert.StartsWith("--place wants x,y,z", error);
        }
    }

    static World MakeWorld(ulong seed, out EntityId player)
    {
        var w = MovementServices.Register(new WorldBuilder(seed), BodyMotorTests.Playground()).Build();
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        MovementServices.SpawnBody(w, new(1.5f, 0.2f, 0.01f), 1f, Locomotion.Sandbox);
        return w;
    }

    /// <summary>Forward, turning a little, sprinting and jumping now and then: the same frames every time.</summary>
    static ActionFrame Frame(int t)
    {
        var held = (t / 90 % 3 == 1 ? Buttons.Sprint : Buttons.None) | (t % 150 == 0 ? Buttons.Jump : Buttons.None);
        return new ActionFrame(new Vector2(t / 200 % 2 == 0 ? 0.3f : -0.4f, 1f), new Vector2(t / 120 % 2 == 0 ? 0.01f : -0.015f, 0f), held, default);
    }

    [Fact]
    public void The_body_is_put_there_facing_the_way_asked_with_the_view_turned()
    {
        var w = MakeWorld(7, out var player);
        Assert.True(Placement.TryParse("-12,-12,0.5,90,180", out var p, out _));
        float aside = p.Apply(w, player);
        Assert.Equal(new Vector3(-12, -12, 0.5f), w.Get<Body>(player).Feet);
        Assert.Equal(DetMath.Pi / 2, w.Get<Body>(player).Yaw, 5);
        Assert.Equal(DetMath.Pi, w.Get<CameraRig>(player).Yaw, 5);
        Assert.Equal(-DetMath.Pi / 2, aside, 5);
        for (int t = 0; t < World.TicksPerSecond; t++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.Equal(BodyState.Ground, w.Get<Body>(player).State);
        Assert.True(Vector2.Distance(new(-12, -12), new(w.Get<Body>(player).Feet.X, w.Get<Body>(player).Feet.Y)) < 0.05f);
    }

    [Fact]
    public void A_placed_recording_replays_from_the_file_with_the_same_digests()
    {
        const string where = "-12,-12,0.5,90";
        var w = MakeWorld(42, out var player);
        Assert.True(Placement.TryParse(where, out var placement, out _));
        placement.Apply(w, player);
        var tape = new Tape { Header = new TapeHeader { Level = "playground", Seed = 42, Notes = new() { [Placement.TapeNote] = where } } };
        for (int t = 1; t <= 600; t++)
        {
            tape.Frames.Add(Frame(t));
            MovementServices.Step(w, Frame(t));
            if (t % Tape.DigestEvery == 0) tape.Digests[t] = w.Digest();
        }
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-placed-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            Assert.True(Placement.TryParse(back.Header.Notes[Placement.TapeNote], out var again, out _));
            Assert.Equal(placement, again);

            var replay = MakeWorld(back.Header.Seed, out var replayed);
            again.Apply(replay, replayed);
            var unplaced = MakeWorld(back.Header.Seed, out _);
            bool parted = false;
            for (int t = 1; t <= back.Frames.Count; t++)
            {
                MovementServices.Step(replay, back.Frames[t - 1]);
                MovementServices.Step(unplaced, back.Frames[t - 1]);
                if (!back.Digests.TryGetValue(t, out ulong d)) continue;
                Assert.True(d == replay.Digest(), $"the placed replay parted at tick {t}");
                parted |= d != unplaced.Digest();
            }
            Assert.Equal(20, back.Digests.Count);
            Assert.True(parted, "a replay from the start anchor should part from a placed recording");
        }
        finally { File.Delete(path); }
    }
}
