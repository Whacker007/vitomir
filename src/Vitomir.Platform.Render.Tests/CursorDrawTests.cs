// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The freed pointer's cursor drawn over the picture: 1.1's art for the verb with its point at its anchor — the
/// default's blade tip at the top left, the talk cursor's middle — the thing's name beside it, and a plain arrow
/// without the install; the reticle's talk mark, the person's name under it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CursorDrawTests
{
    static FrameView Black() => new() { View = new ViewPoint(Vector3.Zero, 0, 0, 60), ShowReticle = false };

    [Fact]
    public void Without_one_one_s_art_the_cursor_is_a_plain_arrow_from_its_tip_and_the_name_is_beside_it()
    {
        var (w, h) = (640, 360);
        using var r = Stills.Headless(w, h);
        var frame = Black();
        frame.Cursor = new PointerCursor(new Vector2(0.25f, 0.25f), CursorId.Default, "Merchant", Dimmed: true);
        r.Draw(frame);
        Stills.Save(r, "cursor-arrow");
        var px = r.Pixels(out _, out _);
        int x = w / 4, y = h / 4;
        float size = OverlayPass.CursorPixels(w, h, 1f);
        Assert.Equal(16f, size);                                    // 64 units on 360 lines
        Assert.True(Stills.Mean(px, w, x, y + 2, x + 3, y + 8).X > 100, "the arrow below and right of its tip");
        Assert.True(Stills.Mean(px, w, x - 12, y - 12, x - 2, y - 2).Length() < 1f, "nothing above and left of the tip");
        Assert.True(Stills.Mean(px, w, x + 15, y + 3, x + 60, y + 14).X > 5, "the name beside it");
    }

    [Fact]
    public void One_one_s_talk_cursor_is_drawn_round_its_middle_and_the_default_from_its_blade_tip()
    {
        using var install = GameInstall.TryOpen(Environment.GetEnvironmentVariable("VTMR_GAME_DIR"));
        Assert.SkipUnless(install is not null, "no 1.1 install (VTMR_GAME_DIR)");
        var cursors = OneOneCursors.From(install);
        var (w, h) = (1280, 720);
        using var r = Stills.Headless(w, h);
        r.CursorArt = cursors.Art;
        float size = OverlayPass.CursorPixels(w, h, 1f);          // 32 pixels on 720 lines

        var frame = Black();
        frame.Cursor = new PointerCursor(new Vector2(0.5f, 0.5f), CursorId.Talk, "Merchant", Dimmed: false);
        r.Draw(frame);
        Stills.Save(r, "cursor-talk");
        var px = r.Pixels(out _, out _);
        int cx = w / 2, cy = h / 2, half = (int)(size / 2);
        var inside = Stills.Mean(px, w, cx - half, cy - half, cx + half, cy + half);
        Assert.True(inside.Length() > 20f, $"the art round the pointer ({inside})");
        Assert.True(Stills.Mean(px, w, cx - half - 30, cy - half - 30, cx - half - 2, cy - half - 2).Length() < 1f, "nothing up and left of it");
        Assert.True(Stills.Mean(px, w, cx + half, cy - 4, cx + half + 80, cy + 10).X > 10f, "the name to its right");

        frame.Cursor = new PointerCursor(new Vector2(0.25f, 0.25f), CursorId.Default, null, Dimmed: true);
        r.Draw(frame);
        Stills.Save(r, "cursor-default");
        px = r.Pixels(out _, out _);
        int x = w / 4, y = h / 4, s = (int)size;
        Assert.True(Stills.Mean(px, w, x, y, x + s, y + s).Length() > 5f, "the blade below and right of its tip");
        Assert.True(Stills.Mean(px, w, x - s, y - s, x - 1, y - 1).Length() < 1f, "nothing above and left of it");
    }

    [Fact]
    public void The_reticle_on_a_person_to_talk_to_names_them_under_it()
    {
        var (w, h) = (640, 360);
        using var r = Stills.Headless(w, h);
        var frame = Black();
        frame.ShowReticle = true;
        frame.Reticle = new ReticleAnswer(ReticleTarget.Person, 7, Vector3.One, 1.2f, "Old Man", null, false);
        r.Draw(frame);
        var none = r.Pixels(out _, out _);
        float under = Stills.Mean(none, w, w / 2 - 30, h / 2 + 8, w / 2 + 30, h / 2 + 18).X;
        frame.ReticleVerb = PointerVerb.Talk;
        r.Draw(frame);
        Stills.Save(r, "reticle-talk");
        var talk = r.Pixels(out _, out _);
        Assert.True(under < 1f, "no name without the talk verb");
        Assert.True(Stills.Mean(talk, w, w / 2 - 30, h / 2 + 8, w / 2 + 30, h / 2 + 18).X > 10f, "the name under the reticle");
    }
}
