// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Render.Tests;

/// <summary>
/// The diagnostic overlay's own parts — the font and the projection. Neither needs a GPU, which is
/// the point: the two things most likely to go quietly wrong (a glyph that is blank, a label that
/// lands somewhere plausible when it is behind the camera) are caught without one.
/// </summary>
public class OverlayTests
{
    [Fact]
    public void Every_printable_character_has_a_glyph_and_only_space_is_blank()
    {
        for (char c = (char)OverlayFont.First; c <= OverlayFont.Last; c++)
        {
            int lit = 0;
            for (int y = 0; y < OverlayFont.Height; y++)
                for (int x = 0; x < OverlayFont.Width; x++)
                    if (OverlayFont.Lit(c, x, y)) lit++;
            if (c == ' ') Assert.Equal(0, lit);
            else Assert.True(lit > 0, $"'{c}' ({(int)c}) has no glyph");
        }
        // Anything outside the range falls back rather than reading off the end.
        Assert.Equal(OverlayFont.Glyph('?'), OverlayFont.Glyph('é'));
        Assert.Equal(OverlayFont.Glyph('?'), OverlayFont.Glyph('\t'));
    }

    [Fact]
    public void No_two_letters_or_digits_share_a_shape()
    {
        // A font whose 'o' and '0' are the same is a font that makes a diagnostic lie.
        var seen = new Dictionary<ulong, char>();
        for (char c = (char)OverlayFont.First; c <= OverlayFont.Last; c++)
        {
            if (!char.IsLetterOrDigit(c)) continue;
            ulong g = OverlayFont.Glyph(c);
            Assert.False(seen.TryGetValue(g, out char other), $"'{c}' and '{other}' draw the same");
            seen[g] = c;
        }
    }

    [Fact]
    public void Letters_sit_on_one_baseline()
    {
        // Row 5 is the baseline and row 6 is for descenders. A glyph that ignores that is what made
        // "hopper" read as "hoPPer" the first time the overlay was drawn.
        const int Baseline = 5;
        foreach (char c in "abcdefhiklmnorstuvwxz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            Assert.True(Row(c, Baseline) != 0, $"'{c}' does not reach the baseline");
        foreach (char c in "abcdefhiklmnorstuvwxzABCDEFGHIKLMNOPRSTUVWXYZ0123456789")
            Assert.True(Row(c, Baseline + 1) == 0, $"'{c}' hangs below the baseline");
        foreach (char c in "gjpqy")
            Assert.True(Row(c, Baseline + 1) != 0, $"'{c}' should descend");

        static int Row(char c, int y)
        {
            int bits = 0;
            for (int x = 0; x < OverlayFont.Width; x++) if (OverlayFont.Lit(c, x, y)) bits |= 1 << x;
            return bits;
        }
    }

    [Fact]
    public void The_atlas_is_one_row_of_glyphs_with_the_mask_in_alpha()
    {
        var rgba = DeveloperOverlay.Atlas();
        int count = OverlayFont.Last - OverlayFont.First + 1;
        Assert.Equal(count * OverlayFont.Width * OverlayFont.Height * 4, rgba.Length);

        // 'A' is the 34th printable character; its top-left pixel is blank and its middle is not.
        int a = 'A' - OverlayFont.First;
        int At(int i, int x, int y) => ((y * count * OverlayFont.Width) + i * OverlayFont.Width + x) * 4 + 3;
        Assert.Equal(0, rgba[At(a, 0, 0)]);
        Assert.Equal(255, rgba[At(a, 2, 0)]);
        // And every colour channel is white, so a glyph tints to whatever the caller asks for.
        Assert.Equal(255, rgba[At(a, 0, 0) - 1]);
    }

    [Fact]
    public void A_point_behind_the_camera_does_not_project_onto_the_screen()
    {
        // The whole reason Project returns a bool. Without the w test a point behind the eye lands
        // at a perfectly plausible pixel, and labels appear over empty walls.
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, new Vector3(0, 1, 0), Vector3.UnitZ);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 1f, 5000f);
        var vp = view * proj;

        Assert.True(WatchOverlay.Project(new Vector3(0, 500, 0), vp, 1280, 720, out var ahead));
        Assert.InRange(ahead.X, 630f, 650f);
        Assert.InRange(ahead.Y, 350f, 370f);

        Assert.False(WatchOverlay.Project(new Vector3(0, -500, 0), vp, 1280, 720, out _));
        Assert.False(WatchOverlay.Project(new Vector3(9000, 500, 0), vp, 1280, 720, out _));
    }

    [Fact]
    public void A_tint_is_chosen_for_every_state()
    {
        foreach (WatchTint t in Enum.GetValues<WatchTint>())
            Assert.True(WatchOverlay.Colour(t).A > 0f);
        Assert.Equal(Tint.Red, WatchOverlay.Colour(WatchTint.Foe));
        Assert.Equal(Tint.Green, WatchOverlay.Colour(WatchTint.Friend));
    }
}
