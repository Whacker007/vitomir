// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The game camera's two choices of ours (<see cref="CameraChoices"/>): how far up it turns (the base game stops at 20°
/// below level; ours goes to level by default) without changing how far back it sits over the base game's range, and the
/// smoothing on steps, which takes a riser out of where the camera looks and pays it back, while the walker stays where
/// the base game puts it. A new camera is the base game's; a recording's <c>camera</c> line puts its own back.
/// </summary>
public class CameraChoicesTests
{
    static WorldThing Someone(Vector3 at) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0), Template = "test",
        Type = ThingType.Player, Actor = new Actor { Health = 100, MaxHealth = 100 }, Position = at,
    };

    static WorldCamera Following(WorldThing who, float up = CameraChoices.BaseUp, bool steps = false)
    {
        var camera = new WorldCamera { HeadOf = null, UpLimit = up, SmoothSteps = steps };
        camera.Start(who);
        return camera;
    }

    [Fact]
    public void A_new_camera_is_the_base_games_and_stops_at_twenty_below_level()
    {
        var camera = Following(Someone(Vector3.Zero));
        Assert.Equal(CameraChoices.BaseUp, camera.UpLimit);
        Assert.False(camera.SmoothSteps);
        camera.TurnPitch(180f);
        for (int t = 50; t <= 2000; t += 50) camera.Tick(0.05f, t);
        Assert.Equal(-20f, WorldCamera.Signed(camera.Angles.X), 3);
        camera.Swivel(0f, -400f);
        Assert.Equal(-20f, WorldCamera.Signed(camera.Angles.X), 3);
    }

    [Fact]
    public void With_the_limit_raised_the_keys_and_the_swivel_turn_up_to_level_and_no_further()
    {
        var camera = Following(Someone(Vector3.Zero), up: 0f);
        camera.TurnPitch(180f);
        for (int t = 50; t <= 2000; t += 50) camera.Tick(0.05f, t);
        Assert.InRange(WorldCamera.Signed(camera.Angles.X), -0.02f, 0f);
        camera.Swivel(0f, -400f);                                   // further up still
        Assert.InRange(WorldCamera.Signed(camera.Angles.X), -0.02f, 0f);
        camera.Swivel(0f, 400f);                                    // and down: the base game's lower limit stands
        Assert.Equal(-80f, WorldCamera.Signed(camera.Angles.X), 3);

        var halfway = Following(Someone(Vector3.Zero), up: -10f);
        halfway.Swivel(0f, -400f);
        Assert.Equal(-10f, WorldCamera.Signed(halfway.Angles.X), 3);
    }

    [Fact]
    public void Above_the_base_games_limit_the_eye_sits_at_the_near_distance_and_below_it_as_the_base_game_has_it()
    {
        var who = Someone(Vector3.Zero);
        var ours = Following(who, up: 0f);
        var base_ = Following(who);
        foreach (var pitch in new[] { -80f, -50f, -30f, -20f })
        {
            ours.Angles.X = base_.Angles.X = WorldCamera.Wrap(pitch);
            ours.Tick(0.05f, 50); base_.Tick(0.05f, 50);
            Assert.Equal(base_.DistanceTarget, ours.DistanceTarget, 3);
        }
        foreach (var pitch in new[] { -15f, -5f, -0.5f })
        {
            ours.Angles.X = WorldCamera.Wrap(pitch);
            ours.Tick(0.05f, 50);
            Assert.Equal(ours.NearDistance * ours.ZoomScale, ours.DistanceTarget, 3);
        }
    }

    [Fact]
    public void On_a_riser_the_smoothed_camera_takes_the_step_out_and_pays_it_back_and_the_walker_is_not_moved()
    {
        var who = Someone(new Vector3(0, 0, 0));
        var smooth = Following(who, steps: true);
        var plain = Following(who);
        smooth.Tick(1 / 30f, 33); plain.Tick(1 / 30f, 33);
        float before = smooth.Target.Z;

        who.Position = new Vector3(8, 0, 12);                       // up one riser of the base game's stairs, in one tick
        smooth.Tick(1 / 30f, 66); plain.Tick(1 / 30f, 66);
        Assert.Equal(12f, plain.Target.Z - before, 3);             // the base game's camera jumps with the walker
        Assert.InRange(smooth.Target.Z - before, 0f, 7f);          // ours takes most of it out
        Assert.Equal(12f, who.Position.Z);                          // and the walker is where it stepped

        for (int t = 99; t <= 600; t += 33) { smooth.Tick(1 / 30f, t); plain.Tick(1 / 30f, t); }
        Assert.Equal(plain.Target.Z, smooth.Target.Z, 3);           // standing on the step, the two agree again
        Assert.Equal(0f, smooth.StepLiftNow, 3);
    }

    [Fact]
    public void A_fall_or_a_move_by_a_script_is_not_smoothed()
    {
        var who = Someone(Vector3.Zero);
        var smooth = Following(who, steps: true);
        smooth.Tick(1 / 30f, 33);
        float before = smooth.Target.Z;
        who.Position = new Vector3(0, 0, -200);                     // a fall's landing
        smooth.Tick(1 / 30f, 66);
        Assert.Equal(-200f, smooth.Target.Z - before, 3);
        who.Position = new Vector3(5000, 0, -190);                  // put somewhere else
        smooth.Tick(1 / 30f, 99);
        Assert.Equal(0f, smooth.StepLiftNow, 3);
    }

    [Fact]
    public void A_recording_carries_the_choices_and_one_from_before_the_line_replays_with_the_base_games_camera()
    {
        float up = CameraChoices.Up, far = CameraChoices.Out; bool steps = CameraChoices.Steps; var see = CameraChoices.SeeThrough;
        try
        {
            CameraChoices.Up = -5f; CameraChoices.Steps = true; CameraChoices.Out = 3f; CameraChoices.SeeThrough = SeeThroughMode.Off;
            var fields = CameraChoices.TapeFields();
            Assert.Equal(["up=-5", "steps=on", "out=3", "seethrough=off"], fields);
            CameraChoices.Up = 0f; CameraChoices.Steps = false;
            CameraChoices.FromTape(fields);
            Assert.Equal(-5f, CameraChoices.Up);
            Assert.True(CameraChoices.Steps);
            CameraChoices.FromTape(null);
            Assert.Equal(CameraChoices.BaseUp, CameraChoices.Up);
            Assert.False(CameraChoices.Steps);
        }
        finally { CameraChoices.Up = up; CameraChoices.Steps = steps; CameraChoices.Out = far; CameraChoices.SeeThrough = see; }
    }

    [Fact]
    public void A_new_camera_zooms_out_no_further_than_the_base_game_and_one_let_out_goes_to_three_times_its_distance()
    {
        var who = Someone(Vector3.Zero);
        var base_ = Following(who);
        Assert.Equal(WorldCamera.ZoomMax, base_.ZoomOut);
        Assert.Equal(SeeThroughMode.Off, base_.SeeThrough);
        base_.Zoom(10f);
        Assert.Equal(1f, base_.ZoomScale);

        var ours = new WorldCamera { HeadOf = null, ZoomOut = CameraChoices.DefaultOut };
        ours.Start(who);
        Assert.Equal(1f, ours.ZoomScale);                               // it starts at the base game's distance
        ours.Tick(0.05f, 50);
        Assert.Equal(WorldCamera.StartDistance, ours.DistanceTarget, 2);
        ours.Zoom(10f);                                                 // the keys or the wheel out as far as they go
        Assert.Equal(3f, ours.ZoomScale);
        ours.Tick(0.05f, 100);
        Assert.Equal(3f * WorldCamera.StartDistance, ours.DistanceTarget, 1);
        ours.Zoom(-10f);                                                // and in, as near as the base game's
        Assert.Equal(WorldCamera.ZoomMin, ours.ZoomScale);

        var two = new WorldCamera { HeadOf = null, ZoomOut = 2f };
        two.Start(who);
        two.Zoom(10f);
        Assert.Equal(2f, two.ZoomScale);
        var tooFar = new WorldCamera { HeadOf = null, ZoomOut = 9f };
        tooFar.Start(who);
        tooFar.Zoom(10f);
        Assert.Equal(CameraChoices.MostOut, tooFar.ZoomScale);
    }

    [Fact]
    public void A_recording_carries_the_zoom_limit_and_see_through_and_one_without_them_replays_with_the_base_games()
    {
        float far = CameraChoices.Out; var see = CameraChoices.SeeThrough; bool steps = CameraChoices.Steps; float up = CameraChoices.Up;
        try
        {
            CameraChoices.Out = 2.5f; CameraChoices.SeeThrough = SeeThroughMode.On;
            var fields = CameraChoices.TapeFields();
            Assert.Contains("out=2.5", fields);
            Assert.Contains("seethrough=window", fields);
            CameraChoices.Out = 1f; CameraChoices.SeeThrough = SeeThroughMode.Off;
            CameraChoices.FromTape(fields);
            Assert.Equal(2.5f, CameraChoices.Out);
            Assert.Equal(SeeThroughMode.On, CameraChoices.SeeThrough);
            CameraChoices.SeeThrough = SeeThroughMode.XRay;
            Assert.Contains("seethrough=xray", CameraChoices.TapeFields());
            CameraChoices.FromTape(["seethrough=on"]);                       // made when on was what the x-ray does now
            Assert.Equal(SeeThroughMode.XRay, CameraChoices.SeeThrough);
            CameraChoices.FromTape(["up=-5", "steps=on"]);                 // a recording from before the two words
            Assert.Equal(CameraChoices.BaseOut, CameraChoices.Out);
            Assert.Equal(SeeThroughMode.Off, CameraChoices.SeeThrough);
            Assert.True(CameraChoices.Steps);
        }
        finally { CameraChoices.Out = far; CameraChoices.SeeThrough = see; CameraChoices.Steps = steps; CameraChoices.Up = up; }
    }

    [Fact]
    public void The_zoom_limit_is_read_from_the_settings_file_and_the_flags_and_see_through_is_always_the_window_or_the_developers_xray()
    {
        float far = CameraChoices.Out; var see = CameraChoices.SeeThrough; bool steps = CameraChoices.Steps; float up = CameraChoices.Up;
        string ini = Path.Combine(Path.GetTempPath(), $"vitomir-camera-{Guid.NewGuid():N}.ini");
        try
        {
            var said = new List<string>();
            CameraChoices.Configure(null, null, said.Add, ini);                        // no file: the defaults
            Assert.Equal(CameraChoices.DefaultOut, CameraChoices.Out);
            Assert.Equal(SeeThroughMode.On, CameraChoices.SeeThrough);
            File.WriteAllText(ini, "[camera]\nout = 2\n");
            CameraChoices.Configure(null, null, said.Add, ini);
            Assert.Equal(2f, CameraChoices.Out);
            CameraChoices.Configure(null, null, said.Add, ini, outFlag: "7");          // the flag over the file, held to 3
            Assert.Equal(3f, CameraChoices.Out);
            CameraChoices.Configure(null, null, said.Add, ini, outFlag: "0.5");
            Assert.Equal(1f, CameraChoices.Out);                                         // and to the base game's at least
            Assert.Empty(said);
            CameraChoices.Configure(null, null, said.Add, ini, xray: true);             // the developer's x-ray
            Assert.Equal(SeeThroughMode.XRay, CameraChoices.SeeThrough);
            CameraChoices.Configure(null, null, said.Add, ini, plain: true);            // the developer's none, to compare with 1.1
            Assert.Equal(SeeThroughMode.Off, CameraChoices.SeeThrough);
            File.WriteAllText(ini, "[camera]\nseethrough = off\n");                    // no longer a setting: said, and passed over
            CameraChoices.Configure(null, null, said.Add, ini);
            Assert.Equal(SeeThroughMode.On, CameraChoices.SeeThrough);
            Assert.Single(said);
            said.Clear();
            File.WriteAllText(ini, "[camera]\nout = far\n");
            CameraChoices.Configure(null, null, said.Add, ini);
            Assert.Single(said);
            Assert.Equal(CameraChoices.DefaultOut, CameraChoices.Out);
        }
        finally { File.Delete(ini); CameraChoices.Out = far; CameraChoices.SeeThrough = see; CameraChoices.Steps = steps; CameraChoices.Up = up; }
    }

    [Fact]
    public void The_settings_file_and_the_flags_are_read_held_to_the_range_and_a_bad_word_is_said_and_passed_over()
    {
        float up = CameraChoices.Up; bool steps = CameraChoices.Steps;
        string ini = Path.Combine(Path.GetTempPath(), $"vitomir-camera-{Guid.NewGuid():N}.ini");
        try
        {
            var said = new List<string>();
            CameraChoices.Configure(null, null, said.Add, ini);                        // no file: the defaults
            Assert.Equal(CameraChoices.DefaultUp, CameraChoices.Up);
            Assert.True(CameraChoices.Steps);
            File.WriteAllText(ini, "[camera]\nup = -12\nsteps = off\n");
            CameraChoices.Configure(null, null, said.Add, ini);
            Assert.Equal(-12f, CameraChoices.Up);
            Assert.False(CameraChoices.Steps);
            CameraChoices.Configure("45", "on", said.Add, ini);                        // a flag over the file, held to level
            Assert.Equal(0f, CameraChoices.Up);
            Assert.True(CameraChoices.Steps);
            Assert.Empty(said);
            File.WriteAllText(ini, "[camera]\nup = high\nsteps = maybe\n");
            CameraChoices.Configure(null, null, said.Add, ini);
            Assert.Equal(2, said.Count);
            Assert.Equal(CameraChoices.DefaultUp, CameraChoices.Up);
        }
        finally { File.Delete(ini); CameraChoices.Up = up; CameraChoices.Steps = steps; }
    }
}
