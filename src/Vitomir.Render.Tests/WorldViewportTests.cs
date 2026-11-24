// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

/// <summary>
/// The world is drawn into the rows above the status bar and nowhere else, and the picture that
/// leaves is the **same size** as the full-window one, not a smaller copy of it.
///
/// This is the measurement from two 1.1 screenshots of one spot — one with the bar, one without —
/// turned into an assertion. In the original the whole picture sits **108.9 pixels higher** with the
/// bar up at 1600x900, at a scale fit of exactly 1.00 and a horizontal fit of exactly 0. A pure
/// translation with no scaling is what a shorter viewport gives when the pixels-per-unit is pinned
/// by the width, which is the original's projection convention; it is also, separately, what
/// makes the head rectangle black, since the world simply stops above it. `StatusBar.WorldBottom`
/// carries the numbers.
/// </summary>
[Collection("headless")]
public class WorldViewportTests(Headless h)
{
    const int W = 640, H = 360;

    /// <summary>Where the second picture has to move to sit on the first, and how well it then fits.</summary>
    static (int Shift, double Fit, double Unshifted) Match(byte[] a, byte[] b)
    {
        static double Diff(byte[] x, byte[] y, int shift)
        {
            double sum = 0; int n = 0;
            for (int row = 20; row < H - 60; row++)
            {
                int other = row + shift;
                if (other < 0 || other >= H) continue;
                for (int col = 40; col < W - 40; col += 2)
                {
                    int i = (row * W + col) * 4, j = (other * W + col) * 4;
                    sum += Math.Abs(x[i] - y[j]) + Math.Abs(x[i + 1] - y[j + 1]) + Math.Abs(x[i + 2] - y[j + 2]);
                    n += 3;
                }
            }
            return n == 0 ? double.MaxValue : sum / n;
        }

        int best = 0; double bestDiff = double.MaxValue;
        for (int shift = -10; shift <= 90; shift++)
        {
            double d = Diff(a, b, shift);
            if (d < bestDiff) { bestDiff = d; best = shift; }
        }
        return (best, bestDiff, Diff(a, b, 0));
    }

    /// <summary>
    /// With the bar up the world moves up by half of what the bar takes, and everything else about
    /// the picture is unchanged — which is the whole claim, because a viewport that rescaled would
    /// not match at any shift.
    /// </summary>
    [Fact]
    public void The_status_bar_moves_the_world_up_without_resizing_it()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var full = h.Frame("P1_HAVN", W, H);
        var short_ = h.Frame("P1_HAVN", W, H, settings: f => f.ViewHeight = StatusBar.WorldShare);

        int rows = (int)MathF.Round(H * StatusBar.WorldShare);
        int expected = H / 2 - rows / 2;

        var (shift, fit, unshifted) = Match(short_, full);
        Assert.True(Math.Abs(shift - expected) <= 1,
                    $"the world moved {shift} rows, expected {expected} (the bar takes {H - rows} of {H})");
        Assert.True(fit < unshifted / 3,
                    $"at {shift} rows the two pictures differ by {fit:F2} against {unshifted:F2} unshifted; "
                    + "a shorter viewport should move the picture, not resize it");
    }

    /// <summary>Below the world's last row there is nothing but the frame's own black.</summary>
    [Fact]
    public void Nothing_is_drawn_below_the_world_rows()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var pixels = h.Frame("P1_HAVN", W, H, settings: f => f.ViewHeight = StatusBar.WorldShare);
        int rows = (int)MathF.Round(H * StatusBar.WorldShare);
        int lit = 0;
        for (int row = rows + 2; row < H; row++)
            for (int col = 0; col < W; col++)
            {
                int i = (row * W + col) * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 12) lit++;
            }
        Assert.Equal(0, lit);

        // And the row just above it is not black, so the cut is where it says it is.
        int above = 0;
        for (int col = 0; col < W; col++)
        {
            int i = ((rows - 3) * W + col) * 4;
            if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 12) above++;
        }
        Assert.True(above > W / 10, $"only {above} lit pixels on the world's last row");
    }

    /// <summary>
    /// **Everything that turns a world point into a pixel has to agree.** Three things do it: the
    /// frame's own projection, the overlay's labels, and the pick ray — and they have to agree with
    /// each other or a label sits off its character and a click lands somewhere other than where
    /// the cursor is. They have disagreed twice, both times because the status bar shortened the
    /// world's viewport and one of the three did not follow.
    ///
    /// This closes the loop rather than checking each in turn: project a point the way the frame
    /// projects it, hand that pixel to the overlay and to the ray, and require all three to name
    /// the same place.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_frame_the_labels_and_the_ray_agree_on_where_a_point_is(bool barUp)
    {
        const int w = 1600, hh = 900;
        var cam = new FreeCamera { Position = Vector3.Zero, Yaw = 0f, Pitch = 0f };
        int rows = barUp ? (int)MathF.Round(hh * StatusBar.WorldShare) : hh;
        var viewProj = cam.View * FrameRenderer.WorldViewport(cam, w, hh, rows, cam.Far);

        foreach (var point in new[]
                 {
                     new Vector3(500f, 0f, 0f), new Vector3(500f, -120f, 90f),
                     new Vector3(800f, 200f, -140f), new Vector3(1500f, -60f, 300f),
                 })
        {
            // The overlay's own map from a world point to a pixel.
            Assert.True(WatchOverlay.Project(point, viewProj, w, rows, out var label),
                        $"the overlay put {point} off screen");

            // The frame's, worked out from the same matrix.
            var clip = Vector4.Transform(new Vector4(point, 1f), viewProj);
            var drawn = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * w, (0.5f - clip.Y / clip.W * 0.5f) * rows);
            Assert.Equal(drawn.X, label.X, 2);
            Assert.Equal(drawn.Y, label.Y, 2);

            // And the ray back out through that pixel has to come back to the point.
            var (origin, direction) = Pick.Ray(cam, label.X - 0.5f, label.Y - 0.5f, w, hh, rows);
            Assert.True(Vector3.Dot(direction, Vector3.Normalize(point - origin)) > 0.99999f,
                        $"bar {(barUp ? "up" : "down")}: the label for {point} sits at {label}, "
                        + "and the ray through that pixel points somewhere else");
        }
    }
}
