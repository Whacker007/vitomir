// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

/// <summary>
/// **The world carried on down under a status bar that lies over it** (ours, `UiLayout.Overlays`,
/// `FrameRenderer.WorldViewport`'s `drawn`). Fitted on a picture wider than the sheet, the bar no longer
/// reaches the picture's sides, and the world is drawn to the bottom edge so that what shows beside the bar
/// is world. The one thing that may not happen is the view moving: everything 1.1 draws above the bar has to
/// stay on the pixel it was on, with only more of the same view below it. Arithmetic, no device.
/// </summary>
public class WorldViewportDrawnTests
{
    static (float X, float Y, float Depth) Pixel(Vector3 point, Matrix4x4 viewProj, int cols, int rows)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProj);
        return ((clip.X / clip.W + 1f) / 2f * cols, (1f - clip.Y / clip.W) / 2f * rows, clip.Z / clip.W);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(2560, 1440)]
    [InlineData(2560, 1080)]
    public void What_1_1_draws_above_the_bar_stays_on_its_pixel_and_there_is_more_below(int w, int h)
    {
        var cam = new FreeCamera { Position = Vector3.Zero, Yaw = 0f, Pitch = 0f };
        int rows = (int)MathF.Round(h * StatusBar.WorldShare);
        var above = cam.View * FrameRenderer.WorldViewport(cam, w, h, rows, cam.Far);                    // 1.1's, into the rows above the bar
        var whole = cam.View * FrameRenderer.WorldViewport(cam, w, h, rows, cam.Far, drawn: h);          // ours, into every row
        foreach (var point in new[]
                 {
                     new Vector3(500f, 0f, 0f), new Vector3(500f, -120f, 90f), new Vector3(800f, 200f, -140f),
                     new Vector3(1500f, -60f, 300f), new Vector3(260f, 30f, -40f), new Vector3(300f, 0f, -150f),
                 })
        {
            var was = Pixel(point, above, w, rows);
            var now = Pixel(point, whole, w, h);
            Assert.Equal(was.X, now.X, 2);
            Assert.Equal(was.Y, now.Y, 2);
            Assert.Equal(was.Depth, now.Depth, 5);

            // And the pick's ray through that pixel still goes through the point: it never knew the rows' end.
            var (origin, dir) = Pick.Ray(cam, now.X - 0.5f, now.Y - 0.5f, w, h, rows, ViewColumns.Whole);
            var to = Vector3.Normalize(point - origin);
            Assert.True(Vector3.Dot(dir, to) > 0.99999f, $"the ray through {now.X:0.0},{now.Y:0.0} misses {point}");
        }

        // A point 1.1 has no row for — below the view, behind the bar's rows — is drawn in them now.
        var low = new Vector3(300f, 0f, -175f);
        var under = Pixel(low, whole, w, h);
        Assert.InRange(under.Y, rows, h);
        Assert.Equal(Pixel(low, above, w, rows).Y, under.Y, 2);          // on the row 1.1's own view would give it, carried on
    }

    [Fact]
    public void With_no_more_rows_than_1_1_draws_it_is_1_1s_matrix()
    {
        var cam = new FreeCamera { Position = Vector3.Zero, Yaw = 0.4f, Pitch = -0.2f };
        var plain = FrameRenderer.WorldViewport(cam, 1280, 720, 546, cam.Far);
        Assert.Equal(plain, FrameRenderer.WorldViewport(cam, 1280, 720, 546, cam.Far, drawn: 546));
        Assert.Equal(plain, FrameRenderer.WorldViewport(cam, 1280, 720, 546, cam.Far, drawn: 0));
    }
}
