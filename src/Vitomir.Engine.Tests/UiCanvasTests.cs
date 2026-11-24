// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's interface emitted into the platform's draw list.
using Vitomir.Engine.Ui;
using Vitomir.Platform.Interface;
using Lettering = Vitomir.Engine.Ui.Lettering;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The interface's canvas (<see cref="UiCanvas"/>): what the passes say becomes draw-list entries in the sheet's space, and the
/// pictures, fonts and faces they name go through the list's resource channel once, by id. The pixels are proved by the GPU
/// stills (old build against new, byte for byte); these say what the list holds.
/// </summary>
public class UiCanvasTests
{
    static List<object> Taken(DrawList list)
    {
        var taken = new List<object>();
        list.Resources.Take(taken);
        return taken;
    }

    [Fact]
    public void A_pane_quad_is_an_image_in_the_sheet_and_its_picture_is_sent_once()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var ui = new UiCanvas(assets);
        var bar = assets.Pane("statusBarSP");
        var q = bar.Quads[0];
        ui.Begin(1280, 720);
        ui.Draw(q.Material, q.Pos[0], q.Pos[1], q.Size[0], q.Size[1]);
        ui.Draw(q.Material, q.Pos[0], q.Pos[1], q.Size[0], q.Size[1]);
        var list = ui.List;
        Assert.Equal([DrawKind.Space, DrawKind.Image, DrawKind.Image], list.Commands.Select(c => c.Kind));
        var map = UiLayout.On(1280, 720);
        Assert.Equal(new DrawSpace(map.ScaleX, map.ScaleY, map.X, map.Bottom), list.Spaces[0]);
        var image = list.Images[0];
        Assert.Equal(image, list.Images[1]);
        Assert.Equal((q.Pos[0], q.Pos[1]), (image.X, image.Y));
        var sent = Taken(list).OfType<DrawPicture>().ToList();
        Assert.Single(sent);
        Assert.Equal(image.Picture, sent[0].Id);
        var tga = assets.Texture(q.Material)!;
        Assert.Equal((tga.Width, tga.Height), (sent[0].Width, sent[0].Height));

        // The next pass's list starts empty; what was sent is not sent again.
        ui.Clear();
        ui.Begin(1280, 720);
        ui.Draw(q.Material, q.Pos[0], q.Pos[1], q.Size[0], q.Size[1]);
        Assert.Empty(Taken(list));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Text_is_a_glyph_run_whose_advance_is_the_fonts_and_whose_font_is_sent_once()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var ui = new UiCanvas(assets);
        var font = assets.Font("defaultFont10");
        ui.Begin(640, 480);
        float end = ui.Text(font, 10f, 20f, "Prague by night", Tint.White);
        ui.TextClipped(font, 10f, 40f, "cut", Tint.Red, 0f, 0f, 30f, 480f);
        Assert.Equal(10f + font.Measure("Prague by night"), end);
        var list = ui.List;
        Assert.Equal(2, list.GlyphRuns.Count);
        var run = list.GlyphRuns[0];
        Assert.Equal("Prague by night", list.Chars(run.First, run.Length).ToString());
        Assert.Null(run.Cut);
        Assert.Equal(new DrawCut(0f, 0f, 30f, 480f), list.GlyphRuns[1].Cut);
        var sent = Taken(list).OfType<DrawBitmapFont>().ToList();
        Assert.Single(sent);
        Assert.Equal((font.StripWidth, font.StripHeight), (sent[0].StripWidth, sent[0].StripHeight));
        Assert.Equal(font['P'].Advance, sent[0]['P'].Advance);
    }

    [Fact]
    public void A_missing_picture_draws_nothing_and_a_box_is_the_flat_picture()
    {
        using var files = Install.Open();
        var ui = new UiCanvas(new UiAssets(files));
        ui.Begin(640, 480);
        ui.Sub("no such picture", 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f);
        Assert.False(ui.Icon("no such picture", 0f, 0f, 10f, 10f));
        ui.Box(1f, 2f, 3f, 4f, new Tint(0.5f, 0.25f, 0f, 0.75f));
        var list = ui.List;
        Assert.Single(list.Images);
        Assert.Equal(new DrawImage(0, 1f, 2f, 3f, 4f, 0f, 0f, 1f, 1f, new Rgba(0.5f, 0.25f, 0f, 0.75f)), list.Images[0]);
        Assert.Empty(Taken(list).OfType<DrawPicture>());
    }

    [Fact]
    public void Our_lettering_is_a_letter_run_with_its_face_sent_once()
    {
        using var files = Install.Open();
        var ui = new UiCanvas(new UiAssets(files));
        var face = Lettering.Load(Lettering.Book);
        ui.Begin(640, 480);
        var style = new LetterStyle(Outline: new Tint(0.1f, 0.1f, 0.1f), OutlineWidth: 0.03f);
        float end = ui.Letters(face, 5f, 6f, 20f, "HOLM", Tint.White, style, tracking: 0.1f);
        ui.Letters(face, 5f, 36f, 20f, "HOLM", Tint.White, LetterStyle.Plain);
        Assert.Equal(5f + face.Measure("HOLM", 20f, 0.1f), end, 3);
        var list = ui.List;
        Assert.Equal(2, list.LetterRuns.Count);
        Assert.Equal(style.Look, list.LetterRuns[0].Look);
        Assert.Equal(TextLook.Plain, list.LetterRuns[1].Look);
        var faces = Taken(list).OfType<DrawFace>().ToList();
        Assert.Single(faces);
        Assert.Equal(face.Em, faces[0].Em);
    }
}
