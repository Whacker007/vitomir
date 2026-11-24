// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The combat log's window laid out without a GPU (<c>Overlay/LogPass.cs</c>): a line wraps at spaces to the
/// window's width (its later rows indented), a word too long for a row is cut within it; the window stands in its corner inside
/// the picture at 1080 and 1440 lines, below the banner, above the status line; a line's colour by its subject.
/// </summary>
public sealed class LogPassTests
{
    static float Half(int rune) => 0.5f;

    [Fact]
    public void A_line_wraps_at_spaces_and_its_later_rows_are_indented()
    {
        // Each letter half an em: 10 ems hold 20 letters, 8.5 the later rows (17).
        string text = "1.06 s: guard: the blade met the shield and was held there";
        Span<int> starts = stackalloc int[8], lengths = stackalloc int[8];
        int rows = LogLayout.Wrap(text, 10f, 1.5f, Half, starts, lengths);
        var parts = new List<string>();
        for (int r = 0; r < rows; r++) parts.Add(text.Substring(starts[r], lengths[r]));
        Assert.Equal(new[] { "1.06 s: guard: the", "blade met the", "shield and was", "held there" }, parts);
        Assert.True(parts[0].Length <= 20);
        Assert.All(parts.Skip(1), p => Assert.True(p.Length <= 17));
        Assert.Equal(text.Replace(" ", ""), string.Concat(parts).Replace(" ", ""));
    }

    [Fact]
    public void A_word_longer_than_a_row_is_cut_and_the_rows_stop_at_the_most()
    {
        string text = new('x', 50);
        Span<int> starts = stackalloc int[3], lengths = stackalloc int[3];
        int rows = LogLayout.Wrap(text, 10f, 0f, Half, starts, lengths);
        Assert.Equal(3, rows);
        Assert.Equal(20, lengths[0]);
        Assert.Equal(20, starts[1]);
        Assert.Equal(0, LogLayout.Wrap("", 10f, 0f, Half, starts, lengths));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    public void The_window_stands_in_its_corner_inside_the_picture(int w, int h)
    {
        float em = LogLayout.Em * h / 1440f;
        float band = 14f * (h / 360f) * 1.6f;
        foreach (var corner in Enum.GetValues<LogCorner>())
        {
            var (min, max) = LogLayout.Box(corner, 10, em, w, h, band);
            Assert.True(min.X >= 0f && min.Y >= 0f && max.X <= w && max.Y <= h, $"{corner} at {min}–{max} in {w}x{h}");
            Assert.True(max.X - min.X < w * 0.45f, $"{corner}: {max.X - min.X:0} px wide of {w}");
            bool right = corner is LogCorner.TopRight or LogCorner.BottomRight, top = corner is LogCorner.TopRight or LogCorner.TopLeft;
            Assert.Equal(right, min.X > w / 2f);
            Assert.Equal(top, min.Y < h / 2f);
            if (top) Assert.True(min.Y >= band, "below the banner");
            if (corner == LogCorner.BottomLeft) Assert.True(max.Y <= h - 15f * (h / 360f), "above the status line");
        }
        // The lettering: 16.5 pixels to the em at 1080 lines, 22 at 1440.
        Assert.InRange(em, 11f, 22.01f);
    }

    [Fact]
    public void A_lines_colour_and_time_follow_its_words()
    {
        var guard = LogLayout.Colour("1.06 s: guard: held");
        var harm = LogLayout.Colour("1.06 s: harm (quick): cut");
        var stun = LogLayout.Colour("1.06 s: fight: Christof (e0) stunned 0.25 s");
        var plain = LogLayout.Colour("1.06 s: contact: the blade rebounds");
        Assert.True(guard.Z > guard.X);       // the guard's steel blue
        Assert.True(harm.X > harm.Z);         // harm's pale red
        Assert.True(stun.X > stun.Z);         // amber
        Assert.NotEqual(guard, plain);
        Assert.Equal(7, LogLayout.TimeLength("1.06 s: guard: held"));
        Assert.Equal(0, LogLayout.TimeLength("guard: held"));
    }
}
