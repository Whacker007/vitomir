// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class SkyWindowTests
{
    static (SkyWindow Window, Vector3 Eye, float Yaw) Level(string name)
    {
        using var files = Install.Open();
        var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
        var scene = NsdScene.Read(files.Read($"scenes/{name}.nsd"), name);
        var st = scene.Starts[0];
        float yaw = MathF.Atan2(st.Rotation[1], st.Rotation[0]) * 180f / MathF.PI + 90f;
        return (new SkyWindow(level), st.Position + new Vector3(0, 0, 48), yaw);
    }

    [Fact]
    public void Sky_shows_above_the_rooftops_of_the_golden_lane()
    {
        var (window, eye, yaw) = Level("P1_GOLD");
        Assert.True(window.SurfaceCount > 0);
        var cam = new FreeCamera { Position = eye, Yaw = yaw, Pitch = 25 };
        Assert.True(window.ScreenRect(cam.ViewProjection(16f / 9), 1280, 720, out var up));
        Assert.True(up.Y < 360 && up.W > 0 && up.H > 0);    // the window reaches the upper half of the screen
    }

    [Fact]
    public void An_interior_has_no_sky_window()
    {
        var (window, eye, yaw) = Level("P1_HAVN");
        var cam = new FreeCamera { Position = eye, Yaw = yaw, Pitch = 25 };
        Assert.Equal(0, window.SurfaceCount);
        Assert.False(window.ScreenRect(cam.ViewProjection(16f / 9), 1280, 720, out _));
    }
}
