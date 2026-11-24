// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The frame's order** (gap 1 of a comparison with 1.1). 1.1's
/// world tick ticks every thing of the place and only then the
/// camera, the ramps, the script timers, the conversation and the messages;
/// the draw's clocks (the light styles, the materials) come after, and the frame's requests last.
/// Earlier, Vitomir ticked the camera first — framing where the steered one stood a
/// frame ago — and fired the timers before the animations. A stand-in for the scripts records
/// where in the frame each of its three parts is called.
/// </summary>
public class FrameOrderTests
{
    /// <summary>A scene's scripts that note what they see at each of their three places in the frame.</summary>
    sealed class Recorder(WorldThing moved, WorldCamera camera, Location place) : ISceneScripts
    {
        public readonly List<string> Calls = [];
        public Vector3 CameraAtTimers;
        public double MaterialsAtTimers = -1;
        public Action? AtTimers;

        public void Enter(Location location) { }
        public void Leave(int exitNumber = 0) { }
        public void Step(float seconds) => throw new InvalidOperationException("the simulation calls the three parts");

        // The scripts' part of the things' tick, which begins with the world's walk (the parts
        // are asked for from each thing's own tick after it): a move of ten units along x.
        public void BeginThings(float seconds) { Calls.Add("things"); moved.Position += new Vector3(10f, 0f, 0f); }

        public void StepTimers(float seconds)
        {
            Calls.Add("timers");
            CameraAtTimers = camera.Target;
            MaterialsAtTimers = place.Materials.ClockMs;
            AtTimers?.Invoke();
            AtTimers = null;
        }

        public void EndFrame() => Calls.Add("end");
    }

    static (Location place, WorldThing knight, WorldCamera camera, Recorder scripts, Simulation sim) Open(Vitomir.Engine.Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var knight = place.Things.First(t => t.Template.StartsWith("convknight", StringComparison.Ordinal));
        var camera = new WorldCamera();
        camera.Start(knight);
        var scripts = new Recorder(knight, camera, place);
        var sim = new Simulation(place) { Scripts = scripts, Camera = camera };
        return (place, knight, camera, scripts, sim);
    }

    /// <summary>
    /// The camera runs after the things, so it frames where the one it follows stands
    /// this frame — the move the thing tick made is already in the frame's target.
    /// </summary>
    [Fact]
    public void The_camera_frames_where_the_one_it_follows_stands_this_frame()
    {
        using var files = Install.Open();
        var (_, knight, camera, _, sim) = Open(files);

        sim.Step(1f / 30f);

        Assert.Equal(knight.Position + new Vector3(0, 0, camera.TargetHeight), camera.Target);
    }

    /// <summary>
    /// The scripts' three places: the thing tick's part first, the timers after the camera (the
    /// camera's target already has this frame's move in it) and before the draw's clocks (the
    /// materials have not yet moved this frame), and the frame's requests last.
    /// </summary>
    [Fact]
    public void The_timers_fire_after_the_things_and_the_camera_and_before_the_draw()
    {
        using var files = Install.Open();
        var (place, knight, camera, scripts, sim) = Open(files);

        sim.Step(1f / 30f);

        Assert.Equal(["things", "timers", "end"], scripts.Calls);
        Assert.Equal(knight.Position + new Vector3(0, 0, camera.TargetHeight), scripts.CameraAtTimers);
        Assert.Equal(0.0, scripts.MaterialsAtTimers);
        Assert.True(place.Materials.ClockMs > 0.0, "the materials did not step after the timers");
    }

    /// <summary>
    /// An animation a timer plays starts moving on the next frame's thing tick: the animations of
    /// this frame have already run when the timers fire (the timers after the things' tick). Earlier, it
    /// moved in the frame it was played.
    /// </summary>
    [Fact]
    public void An_animation_a_timer_plays_starts_moving_on_the_next_frame()
    {
        using var files = Install.Open();
        var (place, knight, _, scripts, sim) = Open(files);
        var animation = knight.Animation!;
        scripts.AtTimers = () => Assert.True(animation.Play("walk", place.Motions!) >= 0);

        sim.Step(1f / 30f);
        float played = animation.Motion!.Player.Frame;
        sim.Step(1f / 30f);
        float next = animation.Motion!.Player.Frame;

        Assert.Equal(0f, played);
        Assert.True(next > played, $"the walk did not move on the next frame ({played} → {next})");
    }
}
