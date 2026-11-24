// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **Old Town's night fly-through ends.** `OLDT_7_1`'s `beginscene` plays two camera paths with
/// fades between them on its own timers — the clock tower, St Thomas — and `Release`s the camera
/// sixteen seconds in. A harness run of the scene had no player, and the camera stayed on
/// the church for good; with Christof it comes back, and with nobody it now goes free.
/// </summary>
[Collection("scripts")]
public class FlyThroughTests(Scripts s)
{
    [Fact]
    public void The_night_fly_through_hands_the_camera_back()
    {
        using var p = Place.Enter(s, "P1_OLDT", "OLDT_7_1");
        bool pathed = false;
        for (float t = 0; t < 25; t += 1f / 30f)
        {
            p.Sim.Step(1f / 30f);
            pathed |= p.Sim.Camera!.Mode == CameraMode.Path;
        }
        Assert.True(pathed);
        Assert.Equal(CameraMode.Game, p.Sim.Camera!.Mode);
    }

    [Fact]
    public void With_nobody_to_follow_a_released_camera_goes_free()
    {
        var camera = new WorldCamera();
        camera.EnterCutscene();
        camera.Release(null);
        Assert.Equal(CameraMode.Free, camera.Mode);
    }
}
