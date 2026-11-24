// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The interface art tool's arithmetic (ours, `PaneArt`): a pane's tiles laid out as the picture they
/// make, and a picture painted at four times that cut back into files the engine takes as finer art.
/// </summary>
public class PaneArtTests
{
    [Fact]
    public void A_picture_written_as_1_1s_art_is_stored_reads_back_the_same()
    {
        var rgba = new byte[5 * 3 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7 + 3);
        var back = TgaImage.Read(TgaImage.Write(5, 3, rgba));
        Assert.Equal((5, 3, true), (back.Width, back.Height, back.HasAlpha));
        Assert.Equal(rgba, back.Rgba);
    }

    [Fact]
    public void The_status_bar_is_one_picture_across_the_sheets_bottom()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var sheet = PaneArt.Of(assets, "statusBarSP")!;
        Assert.Equal((0, 0, UiAssets.Width), (sheet.SheetX, sheet.SheetY, sheet.Width));
        Assert.InRange(sheet.Height, 100, 140);                                  // the bar: about a quarter of the sheet
        Assert.Equal(assets.Pane("statusBarSP").Quads.Count, sheet.Tiles.Count);
        Assert.All(sheet.Tiles, t => Assert.True(t.Left >= 0 && t.Top >= 0 && t.Left + t.Width <= sheet.Width && t.Top + t.Height <= sheet.Height
                                                 && t.Width <= t.CanvasWidth && t.Height <= t.CanvasHeight, t.ToString()));
        Assert.NotEmpty(sheet.Slots);
        // 1.1's own picture of it: solid along the bottom row, and with the heads' window clear in the middle.
        var original = PaneArt.Original(assets, sheet);
        Assert.Equal(sheet.Width * sheet.Height * 4, original.Length);
        Assert.Equal(255, original[((sheet.Height - 1) * sheet.Width + 20) * 4 + 3]);
        Assert.Contains(Enumerable.Range(0, sheet.Width), x => original[((sheet.Height - 30) * sheet.Width + x) * 4 + 3] == 0);
        // The outline is the pane's shape and nothing of its art: three greys and two line colours at most.
        var outline = PaneArt.Template(assets, sheet, 4);
        Assert.Equal(sheet.Width * 4 * sheet.Height * 4 * 4, outline.Length);
        var colours = new HashSet<(byte, byte, byte, byte)>();
        for (int i = 0; i < outline.Length; i += 4) colours.Add((outline[i], outline[i + 1], outline[i + 2], outline[i + 3]));
        Assert.InRange(colours.Count, 3, 5);
        Assert.Equal(6, PaneArt.Describe(sheet, 4).Count - sheet.Tiles.Count - sheet.Slots.Count);
    }

    [Theory]
    [InlineData("statusBarSP")]
    [InlineData("inventoryPane")]
    public void A_picture_painted_at_four_times_cuts_into_files_the_engine_takes(string pane)
    {
        string? install = GameFiles.Locate();
        Assert.SkipWhen(install is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-paneart-" + Guid.NewGuid().ToString("N"));
        string materials = Path.Combine(dir, "ui", "materials");
        Directory.CreateDirectory(materials);
        try
        {
            PaneArt.Sheet sheet;
            byte[] original;
            using (var plain = new GameFiles(install!))
            {
                var assets = new UiAssets(plain);
                sheet = PaneArt.Of(assets, pane)!;
                original = PaneArt.Original(assets, sheet);
            }
            // "Painted": a picture of the right size whose every pixel says where it is, so a tile cut from
            // the wrong place, or laid in its file the wrong way up, cannot pass.
            const int scale = 4;
            int w = sheet.Width * scale, h = sheet.Height * scale;
            var painted = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int d = (y * w + x) * 4;
                    (painted[d], painted[d + 1], painted[d + 2], painted[d + 3]) = ((byte)x, (byte)y, (byte)((x >> 8) | (y >> 8) << 4), 255);
                }
            Assert.Equal(scale, PaneArt.ScaleOf(sheet, w, h));
            Assert.Equal(0, PaneArt.ScaleOf(sheet, w - 1, h));
            Assert.Equal(0, PaneArt.ScaleOf(sheet, sheet.Width * 7, sheet.Height * 7));
            var notes = new List<string>();
            var cuts = PaneArt.Cuts(sheet, painted, scale, notes);
            Assert.Equal(sheet.Tiles.Select(t => t.Material).Distinct(StringComparer.OrdinalIgnoreCase).Count(), cuts.Count);
            foreach (var cut in cuts) File.WriteAllBytes(Path.Combine(materials, cut.File), cut.Data);

            using var files = new GameFiles(install!, [dir]);
            var over = new UiAssets(files);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in sheet.Tiles)
            {
                if (!seen.Add(tile.Material)) continue;
                var finer = over.Finer(tile.Material);
                Assert.True(finer is not null, $"{tile.Material}: the engine did not take its finer file");
                var image = finer!.Value.Image;
                Assert.Equal((scale, tile.CanvasWidth * scale, tile.CanvasHeight * scale), (finer.Value.Scale, image.Width, image.Height));
                // The tile's corners in its file are the painted picture's pixels at the tile's place: the
                // art is the canvas's bottom left corner, as 1.1's is.
                int top = (tile.CanvasHeight - tile.Height) * scale;
                foreach (var (tx, ty) in new[] { (0, 0), (tile.Width * scale - 1, 0), (0, tile.Height * scale - 1), (tile.Width * scale - 1, tile.Height * scale - 1) })
                {
                    int s = ((top + ty) * image.Width + tx) * 4, px = tile.Left * scale + tx, py = tile.Top * scale + ty;
                    Assert.Equal(((byte)px, (byte)py, (byte)255), (image.Rgba[s], image.Rgba[s + 1], image.Rgba[s + 3]));
                }
                // And the padding 1.1 leaves clear is clear.
                if (top > 0) Assert.Equal(0, image.Rgba[3]);
            }
            Assert.Empty(over.FinerRefused);
        }
        finally { Directory.Delete(dir, true); }
    }
}
