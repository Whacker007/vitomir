// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The ray through a pixel has to agree with the viewport the frame was drawn into, or the click
/// lands somewhere other than where the player is pointing — silently, and worse the further from
/// the centre they click. The status bar makes the world's viewport shorter than the window
/// (`StatusBar.WorldBottom`), so this is not a hypothetical.
/// </summary>
public class PickRayTests
{
    static FreeCamera Camera() => new() { Position = new Vector3(0, 0, 0), Yaw = 0f, Pitch = 0f };

    const int W = 1600, H = 900;
    static int Rows => (int)MathF.Round(H * StatusBar.WorldShare);

    /// <summary>
    /// **The round trip.** Project a world point the way the frame projects it, then cast a ray
    /// through the pixel it landed on: the ray must pass through the point. This is the assertion
    /// that catches the ray and the drawn picture disagreeing at all, rather than transcribing the
    /// formula twice and asserting they are the same — and it is what the status bar broke, because
    /// the picture moved into a shorter viewport and the ray did not follow.
    /// </summary>
    [Theory]
    [InlineData(0)]        // no bar: the whole window
    [InlineData(1)]        // bar up: the world stops above it
    public void A_ray_through_a_pixel_hits_what_the_frame_drew_there(int barUp)
    {
        var cam = Camera();
        int rows = barUp == 1 ? Rows : H;
        // Exactly what `Render.FrameRenderer.WorldViewport` builds.
        float fovY = 2f * MathF.Atan(MathF.Tan(cam.VerticalFov * 0.5f) * rows / H);
        var viewProj = cam.View * Matrix4x4.CreatePerspectiveFieldOfView(fovY, W / (float)rows, cam.Near, cam.Far);

        // A spread of points in front of the camera, well away from the centre so a wrong scale and
        // a wrong centre both show up. At yaw 0 the camera looks along **+X** with up **+Z**, so the
        // first component is depth.
        foreach (var point in new[]
                 {
                     new Vector3(500f, 0f, 0f), new Vector3(500f, -120f, 90f), new Vector3(800f, 200f, -140f),
                     new Vector3(1500f, -60f, 300f), new Vector3(260f, 30f, -40f),
                 })
        {
            var clip = Vector4.Transform(new Vector4(point, 1f), viewProj);
            Assert.True(clip.W > 0f, $"{point} is behind the camera");
            // Clip to the viewport's pixels: x across the window, y down the *viewport*.
            float px = (clip.X / clip.W * 0.5f + 0.5f) * W;
            float py = (0.5f - clip.Y / clip.W * 0.5f) * rows;
            Assert.InRange(px, 0f, W);
            Assert.InRange(py, 0f, rows);

            var (origin, direction) = Pick.Ray(cam, px - 0.5f, py - 0.5f, W, H, rows);
            var toPoint = Vector3.Normalize(point - origin);
            Assert.True(Vector3.Dot(direction, toPoint) > 0.99999f,
                        $"bar {(barUp == 1 ? "up" : "down")}: the ray through the pixel {px:0.0},{py:0.0} "
                        + $"that {point} draws at points {direction} instead of {toPoint}");
        }
    }

    /// <summary>A viewport the size of the window is the window, so nothing moves.</summary>
    [Fact]
    public void The_whole_window_is_what_no_viewport_means()
    {
        var cam = Camera();
        foreach (var (x, y) in new[] { (0.5f, 0.5f), (W / 2f, H / 2f), (W - 0.5f, H - 0.5f) })
        {
            var (_, a) = Pick.Ray(cam, x, y, W, H);
            foreach (int rows in new[] { 0, -1, H, H + 50 })
            {
                var (_, b) = Pick.Ray(cam, x, y, W, H, rows);
                Assert.True(Vector3.Dot(a, b) > 0.999999f, $"rows {rows} moved the ray at {x},{y}");
            }
        }
    }

    /// <summary>
    /// The centre of the viewport looks straight ahead — which is the assertion that would have
    /// caught the ray and the drawn picture disagreeing, because the drawn centre moved and a ray
    /// built from the window's centre would not have.
    /// </summary>
    [Fact]
    public void The_viewport_centre_looks_straight_ahead()
    {
        var cam = Camera();
        var (_, middle) = Pick.Ray(cam, W / 2f - 0.5f, Rows / 2f - 0.5f, W, H, Rows);
        Assert.True(Vector3.Dot(middle, cam.Forward) > 0.99999f, $"{middle} is not {cam.Forward}");

        // And the window's centre does not, once the bar is up.
        var (_, window) = Pick.Ray(cam, W / 2f - 0.5f, H / 2f - 0.5f, W, H, Rows);
        Assert.True(Vector3.Dot(window, cam.Forward) < 0.9999f,
                    "the window's centre should no longer be the view's centre with the bar up");
    }

    /// <summary>Below the world's last row there is nothing to point at.</summary>
    [Fact]
    public void Nothing_below_the_world_is_on_the_world()
    {
        Assert.True(Pick.OnWorld(0f, H, Rows));
        Assert.True(Pick.OnWorld(Rows - 1, H, Rows));
        Assert.False(Pick.OnWorld(Rows, H, Rows));
        Assert.False(Pick.OnWorld(H - 1, H, Rows));
        Assert.False(Pick.OnWorld(-1f, H, Rows));
        // With no bar the whole window is the world.
        Assert.True(Pick.OnWorld(H - 1, H, H));
        Assert.True(Pick.OnWorld(H - 1, H, 0));
    }
}
