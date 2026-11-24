// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The kinds 1.1's interface adds to the draw list.
using System.Numerics;
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Interface.Tests;

public class DrawListClassicTests
{
    [Fact]
    public void The_new_kinds_keep_their_order_and_their_fields_in_their_tables()
    {
        var list = new DrawList();
        list.Begin(800, 600);
        list.Space(new DrawSpace(1.25f, 1.25f, 0f, 600f));
        list.Image(new DrawImage(3, 1f, 2f, 3f, 4f, 0f, 0f, 1f, 1f, new Rgba(1f, 1f, 1f)));
        list.Glyphs(1, 10f, 20f, "abc", new Rgba(1f, 0f, 0f));
        list.Letters(2, 30f, 40f, 16f, "de", new Rgba(0f, 1f, 0f), TextLook.Plain, 0.1f, new DrawCut(0f, 0f, 100f, 100f));
        list.Model(4, new PixelRect(5, 6, 7, 8), [new ModelDraw(9, Matrix4x4.Identity), new ModelDraw(10, Matrix4x4.Identity)]);
        Assert.Equal([DrawKind.Space, DrawKind.Image, DrawKind.Glyphs, DrawKind.Letters, DrawKind.Model], list.Commands.Select(c => c.Kind));
        Assert.All(list.Commands, c => Assert.Equal(0, c.Item));
        var run = list.GlyphRuns[0];
        Assert.Equal("abc", list.Chars(run.First, run.Length).ToString());
        var letters = list.LetterRuns[0];
        Assert.Equal("de", list.Chars(letters.First, letters.Length).ToString());
        var insert = list.Models[0];
        Assert.Equal((4, 2), (insert.Scene, insert.Draws));
        Assert.Equal(10, list.ModelDraws[insert.FirstDraw + 1].Identity);
        Assert.Empty(list.Texts());
    }

    [Fact]
    public void Clearing_the_list_keeps_its_unread_resources_and_a_take_empties_them()
    {
        var list = new DrawList();
        list.Begin(64, 64);
        list.Resources.Picture(new DrawPicture(1, 1, 1, [255, 255, 255, 255]));
        list.Resources.Scene(1, new ClassicScene());
        list.Image(new DrawImage(1, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, new Rgba(1f, 1f, 1f)));
        list.Clear();
        Assert.Equal(0, list.Count);
        Assert.Empty(list.Images);
        Assert.Equal(2, list.Resources.Pending);
        var taken = new List<object>();
        list.Resources.Take(taken);
        Assert.Equal(2, taken.Count);
        Assert.Equal(0, list.Resources.Pending);
        Assert.True(new DrawPicture(1, 0, 0, null).Gone);
    }
}
