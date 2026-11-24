// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The interface's draw list drawn over the picture: its solid rectangles, its words in the faces' atlases, and
/// its clips as scissor rectangles.
/// </summary>
public sealed class InterfaceDrawTests
{
    static FrameView Black() => new() { View = new ViewPoint(Vector3.Zero, 0, 0, 60), ShowReticle = false };

    [Fact]
    public void A_screens_rectangles_and_words_are_drawn_and_a_clip_cuts_what_is_outside_it()
    {
        var (w, h) = Stills.Size(320, 180);
        using var r = Stills.Headless(w, h);
        var list = new DrawList();
        list.Begin(w, h);
        list.Solid(new PixelRect(10, 10, 60, 40), new Rgba(0f, 0f, 1f));
        list.PushClip(new PixelRect(100, 10, 50, 50));
        list.Solid(new PixelRect(80, 0, 120, 120), new Rgba(1f, 0f, 0f));      // only its part inside the clip is seen
        list.PopClip();
        list.Text("mmmm", FaceId.Uncial, 40f, new Vector2(20, 150), new Rgba(1f, 1f, 1f), TextLook.Plain);
        r.Draw(Black(), list);
        Stills.Save(r, "interface-draw");
        var px = r.Pixels(out _, out _);

        Assert.True(Stills.Mean(px, w, 20, 20, 60, 40).Z > 240, "the blue rectangle");
        var clipped = Stills.Mean(px, w, 105, 15, 145, 55);
        Assert.True(clipped.X > 240 && clipped.Y < 10, $"the red one inside its clip ({clipped})");
        Assert.True(Stills.Mean(px, w, 160, 70, 195, 115).Length() < 1f, "nothing of it outside the clip");
        Assert.True(Stills.Mean(px, w, 85, 70, 98, 115).Length() < 1f, "nor to the clip's left");
        var letters = Stills.Mean(px, w, 20, 132, 100, 150);
        Assert.True(letters.X > 40, $"the uncial's letters on their baseline ({letters})");
    }

    [Fact]
    public void The_pause_menu_is_drawn_over_the_picture_its_plate_and_its_words()
    {
        var (w, h) = Stills.Size(640, 360);
        using var r = Stills.Headless(w, h);
        var screen = new ScreenLoader().LoadEmbedded("pause");
        screen.Layout(new UiMetrics(w, h));
        var list = new DrawList();
        screen.Draw(list);
        r.Draw(Black(), list);
        Stills.Save(r, "interface-pause");
        var px = r.Pixels(out _, out _);

        var plate = screen.Metrics.ToPixels(screen.Find("plate")!.Rect);
        var resume = screen.Metrics.ToPixels(screen.Find("resume")!.Rect);
        // The plate's field between its frame and its first button: the panel's dark warm brown, not black.
        var field = Stills.Mean(px, w, plate.X + 6, resume.Y - 6, plate.X + 12, resume.Y - 2);
        Assert.True(field.X > 8 && field.X > field.Z, $"the plate's field ({field})");
        // The chosen button's words, lit.
        var words = Stills.Mean(px, w, resume.X + resume.Width / 3, resume.Y + 3, resume.Right - resume.Width / 3, resume.Bottom - 3);
        Assert.True(words.X > field.X + 15, $"the chosen button's words ({words})");
        // Outside the plate, the veil (a dark warm tint) over black stays all but black.
        var beside = Stills.Mean(px, w, 0, 0, plate.X - 4, h);
        Assert.True(beside.Length() < 6f, $"all but black beside the plate ({beside})");
    }

    [Fact]
    public void A_screens_picture_is_drawn_from_its_sent_pixels_tinted_blended_by_its_alpha_and_clipped()
    {
        var (w, h) = Stills.Size(320, 180);
        using var r = Stills.Headless(w, h);
        var list = new DrawList();
        list.Begin(w, h);
        // A 4 x 2 picture: its left half red and opaque, its right half green at half alpha; sent once, mipmapped.
        var rgba = new byte[4 * 2 * 4];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = 4 * (y * 4 + x);
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = x < 2 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)255, (byte)0, (byte)128);
            }
        list.Resources.Picture(new DrawPicture(7, 4, 2, rgba, Mipmaps: true));
        list.Solid(new PixelRect(0, 0, w, h), new Rgba(0f, 0f, 1f));
        // Its left half stretched over 0..100, its right half over 100..200 (across all the rows), the lot cut by a clip at x 180.
        list.PushClip(new PixelRect(0, 0, 180, h));
        list.Picture(new PictureQuad(7, 0f, 20f, 100f, 100f, 0f, 0f, 0.5f, 1f, new Rgba(1f, 1f, 1f)));
        list.Picture(new PictureQuad(7, 100f, 20f, 100f, 100f, 0.5f, 0f, 1f, 1f, new Rgba(1f, 1f, 1f)));
        list.PopClip();
        r.Draw(Black(), list);
        Stills.Save(r, "interface-picture");
        var px = r.Pixels(out _, out _);

        var red = Stills.Mean(px, w, 20, 40, 80, 100);
        Assert.True(red.X > 240 && red.Y < 10 && red.Z < 10, $"the opaque half, as it was sent ({red})");
        var green = Stills.Mean(px, w, 120, 40, 170, 100);
        Assert.True(green.Y is > 110 and < 145 && green.Z is > 110 and < 145, $"the half-alpha half over the blue ({green})");
        Assert.True(Stills.Mean(px, w, 185, 40, 198, 100).Z > 240, "nothing of it past the clip");
        Assert.True(Stills.Mean(px, w, 20, 130, 80, 170).Z > 240, "nothing of it under its rectangle");
    }
}
