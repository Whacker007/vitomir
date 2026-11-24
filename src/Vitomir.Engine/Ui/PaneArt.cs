// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A pane's art as one picture, and a painted picture cut back into the pane's tiles (ours): the
// arithmetic of the interface art tool, for finer art; the program's side is `UiArtCommands`.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A pane's art as one picture** (ours). 1.1 keeps a pane's backdrop as several textures of 256
/// pixels or less laid edge to edge by the pane's quads — the status bar is six of them — which is no way to
/// paint one. This lays them out as the picture they make (<see cref="Of"/>, <see cref="Original"/>), draws
/// the same thing as an outline with nothing of 1.1's in it (<see cref="Template"/>), and cuts a picture
/// painted to that outline back into the tiles, each <b>exactly N times</b> its original's size, which is
/// the one thing the engine asks of finer art (<see cref="UiAssets.Finer"/>).
///
/// Nothing here reads or writes a file but through <see cref="UiAssets"/>: the program does that
/// (<c>vitomir --ui-art-sheet</c>, <c>--ui-art-cut</c>, <c>--ui-art-one</c>).
/// </summary>
public static class PaneArt
{
    /// <summary>
    /// One quad's texture in the pane's picture. <see cref="Left"/>, <see cref="Top"/>, <see cref="Width"/>,
    /// <see cref="Height"/>: the part of it the pane shows, in the picture's pixels from its top left corner
    /// (a pixel is a unit of the 640x480 sheet). <see cref="CanvasWidth"/>, <see cref="CanvasHeight"/>: the
    /// texture file's own size, which is larger where 1.1 padded the art out to a power of two — the art is
    /// then the canvas's bottom left corner (<c>UiCanvas.Draw</c>).
    /// </summary>
    public readonly record struct Tile(string Material, int Left, int Top, int Width, int Height, int CanvasWidth, int CanvasHeight);

    /// <summary>A control of the pane — a button, a slot, a bar — in the picture's pixels, for the outline.</summary>
    public readonly record struct Slot(string Name, string Type, int Left, int Top, int Width, int Height);

    /// <summary>A pane's picture: where it sits on the sheet (bottom left, y up), its size, its tiles and controls.</summary>
    public sealed record Sheet(string Pane, int SheetX, int SheetY, int Width, int Height, IReadOnlyList<Tile> Tiles, IReadOnlyList<Slot> Slots);

    /// <summary>A file a cut makes: the texture's name with its scale (<c>StatusBarSP_01@4x.tga</c>) and the file.</summary>
    public readonly record struct Cut(string File, int Width, int Height, byte[] Data);

    /// <summary>The pane's picture, or null for a pane with no quads or none whose texture is there.</summary>
    public static Sheet? Of(UiAssets assets, string pane)
    {
        var nui = assets.Pane(pane);
        int px = nui.Pos.Length > 0 ? nui.Pos[0] : 0, py = nui.Pos.Length > 1 ? nui.Pos[1] : 0;
        var placed = new List<(string Material, int X, int Y, int W, int H, int Cw, int Ch)>();
        foreach (var q in nui.Quads)
        {
            if (assets.Texture(q.Material) is not { Width: > 0, Height: > 0 } image) continue;
            int qw = q.Size.Length > 0 ? q.Size[0] : image.Width, qh = q.Size.Length > 1 ? q.Size[1] : image.Height;
            int w = Math.Min(qw, image.Width), h = Math.Min(qh, image.Height);
            if (w <= 0 || h <= 0) continue;
            placed.Add((q.Material, px + (q.Pos.Length > 0 ? q.Pos[0] : 0), py + (q.Pos.Length > 1 ? q.Pos[1] : 0), w, h, image.Width, image.Height));
        }
        if (placed.Count == 0) return null;
        int x0 = placed.Min(p => p.X), y0 = placed.Min(p => p.Y), x1 = placed.Max(p => p.X + p.W), y1 = placed.Max(p => p.Y + p.H);
        var tiles = placed.Select(p => new Tile(p.Material, p.X - x0, y1 - (p.Y + p.H), p.W, p.H, p.Cw, p.Ch)).ToList();
        var slots = new List<Slot>();
        foreach (var e in nui.Elements)
        {
            if (e.Size is not [> 0, > 0, ..] size || e.Pos is not [var ex, var ey, ..]) continue;
            int left = px + ex - x0, top = y1 - (py + ey + size[1]);
            if (left >= x1 - x0 || top >= y1 - y0 || left + size[0] <= 0 || top + size[1] <= 0) continue;      // off the backdrop
            slots.Add(new Slot(e.Name, e.Type, left, top, size[0], size[1]));
        }
        return new Sheet(Path.GetFileNameWithoutExtension(pane), x0, y0, x1 - x0, y1 - y0, tiles, slots);
    }

    /// <summary>The part of a tile's texture the pane shows: its canvas's bottom left corner.</summary>
    static (int X, int Y) Shown(Tile tile) => (0, tile.CanvasHeight - tile.Height);

    /// <summary>
    /// 1.1's own art laid out as the pane shows it, a pixel a sheet unit, R, G, B, A with the top row first;
    /// a later quad over an earlier one as the pane draws them. **This is 1.1's art**: for looking at, never
    /// for shipping.
    /// </summary>
    public static byte[] Original(UiAssets assets, Sheet sheet)
    {
        var picture = new byte[sheet.Width * sheet.Height * 4];
        foreach (var tile in sheet.Tiles)
        {
            if (assets.Texture(tile.Material) is not { } image) continue;
            var (sx, sy) = Shown(tile);
            for (int y = 0; y < tile.Height; y++)
                for (int x = 0; x < tile.Width; x++)
                {
                    int s = ((sy + y) * image.Width + sx + x) * 4, d = ((tile.Top + y) * sheet.Width + tile.Left + x) * 4;
                    int a = image.Rgba[s + 3];
                    if (a == 0) continue;
                    for (int c = 0; c < 3; c++) picture[d + c] = (byte)((image.Rgba[s + c] * a + picture[d + c] * (255 - a)) / 255);
                    picture[d + 3] = (byte)Math.Max(picture[d + 3], a);
                }
        }
        return picture;
    }

    /// <summary>
    /// **The outline to paint to**, <paramref name="scale"/> times the pane's size, with none of 1.1's art
    /// in it: grey where the pane is solid and nothing where it is clear (its shape, from the original's
    /// alpha), the tiles' edges in blue, and each control's rectangle in yellow. What is painted over this
    /// has to keep the shape and the rectangles where they are — the engine lays the pane out, and reads a
    /// click, from the original's numbers.
    /// </summary>
    public static byte[] Template(UiAssets assets, Sheet sheet, int scale)
    {
        int w = sheet.Width * scale, h = sheet.Height * scale;
        var picture = new byte[w * h * 4];
        void Put(int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int d = (y * w + x) * 4;
            (picture[d], picture[d + 1], picture[d + 2], picture[d + 3]) = (r, g, b, 255);
        }
        void Frame(int left, int top, int width, int height, byte r, byte g, byte b)
        {
            int line = Math.Max(1, scale / 2);
            for (int t = 0; t < line; t++)
            {
                for (int x = left; x < left + width; x++) { Put(x, top + t, r, g, b); Put(x, top + height - 1 - t, r, g, b); }
                for (int y = top; y < top + height; y++) { Put(left + t, y, r, g, b); Put(left + width - 1 - t, y, r, g, b); }
            }
        }
        var solid = Original(assets, sheet);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = solid[((y / scale) * sheet.Width + x / scale) * 4 + 3];
                if (a == 0) continue;
                byte grey = a >= 128 ? (byte)96 : (byte)56;                     // solid, and the half-clear edge
                Put(x, y, grey, grey, grey);
            }
        foreach (var tile in sheet.Tiles) Frame(tile.Left * scale, tile.Top * scale, tile.Width * scale, tile.Height * scale, 40, 110, 220);
        foreach (var slot in sheet.Slots) Frame(slot.Left * scale, slot.Top * scale, slot.Width * scale, slot.Height * scale, 235, 200, 40);
        return picture;
    }

    /// <summary>The scale a painted picture is at: its size over the pane's, when that is one of <see cref="UiAssets.FinerScales"/> both ways; else 0.</summary>
    public static int ScaleOf(Sheet sheet, int width, int height) => ScaleOf(sheet.Width, sheet.Height, width, height);

    public static int ScaleOf(int originalWidth, int originalHeight, int width, int height)
    {
        foreach (int scale in UiAssets.FinerScales)
            if (width == originalWidth * scale && height == originalHeight * scale) return scale;
        return 0;
    }

    /// <summary>
    /// **A painted picture of the pane cut into its tiles.** <paramref name="rgba"/> is the whole pane,
    /// <paramref name="scale"/> times its size, top row first. Each texture the pane's quads name comes back
    /// as a file <paramref name="scale"/> times the original's canvas, the painted part in the canvas's
    /// bottom left corner where 1.1 has its art and the padding clear. A texture two quads use is cut from
    /// the first place it is used, and <paramref name="notes"/> says so.
    /// </summary>
    public static List<Cut> Cuts(Sheet sheet, ReadOnlySpan<byte> rgba, int scale, List<string>? notes = null)
    {
        int w = sheet.Width * scale;
        if (rgba.Length < w * sheet.Height * scale * 4) throw new ArgumentException($"the picture is not {w}x{sheet.Height * scale}", nameof(rgba));
        var cuts = new List<Cut>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in sheet.Tiles)
        {
            if (!done.Add(tile.Material)) { notes?.Add($"{tile.Material} is drawn in more than one place; cut from the first"); continue; }
            int cw = tile.CanvasWidth * scale, ch = tile.CanvasHeight * scale;
            var canvas = new byte[cw * ch * 4];
            var (sx, sy) = Shown(tile);
            for (int y = 0; y < tile.Height * scale; y++)
                rgba.Slice(((tile.Top * scale + y) * w + tile.Left * scale) * 4, tile.Width * scale * 4)
                    .CopyTo(canvas.AsSpan(((sy * scale + y) * cw + sx * scale) * 4));
            cuts.Add(new Cut(FinerName(tile.Material, scale), cw, ch, TgaImage.Write(cw, ch, canvas)));
        }
        return cuts;
    }

    /// <summary>A texture's finer file's name: the name a pane gives it, its scale, <c>.tga</c>.</summary>
    public static string FinerName(string texture, int scale)
    {
        string stem = texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? texture[..^4] : texture;
        return $"{stem}@{scale}x.tga";
    }

    /// <summary>The layout in words: the picture, each tile and each control, in the painted picture's pixels.</summary>
    public static List<string> Describe(Sheet sheet, int scale)
    {
        var lines = new List<string>
        {
            $"pane {sheet.Pane}: {sheet.Width}x{sheet.Height} units of the 640x480 sheet, its bottom left at ({sheet.SheetX},{sheet.SheetY})",
            $"paint it at exactly {sheet.Width * scale}x{sheet.Height * scale} pixels ({scale} to a unit); numbers below are in those pixels, from the top left",
            "",
            "tiles (blue in the outline; the file each is cut into):",
        };
        foreach (var t in sheet.Tiles)
            lines.Add($"  {FinerName(t.Material, scale),-34} at {t.Left * scale,5},{t.Top * scale,-5} {t.Width * scale}x{t.Height * scale}"
                      + (t.CanvasWidth != t.Width || t.CanvasHeight != t.Height ? $"   (in a file of {t.CanvasWidth * scale}x{t.CanvasHeight * scale})" : ""));
        lines.Add("");
        lines.Add("controls (yellow in the outline; the engine draws and reads them here whatever is painted):");
        foreach (var s in sheet.Slots)
            lines.Add($"  {s.Type,-14} {s.Name,-28} at {s.Left * scale,5},{s.Top * scale,-5} {s.Width * scale}x{s.Height * scale}");
        return lines;
    }
}
