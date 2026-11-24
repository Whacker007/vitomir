// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's words taken off its own art (ours). 1.1 draws the words
// of its menus as pictures: in the tiles of a pane, or as a button's picture over them. To set them again in our own
// lettering they have to come off first, and they come off the player's own copy when the pane is built: nothing of
// 1.1's is shipped, and nothing here is kept on disk.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A pane's art as one picture to take words off** (ours): its tiles laid out as the pane draws them, a pixel a
/// unit of the 640x480 sheet, the top row first (the sheet's y runs up; this picture's rows run down). The tools are
/// few and plain:
/// <list type="bullet">
/// <item>**a button's letters** are where its picture and its rollover picture differ (only the letters change colour);</item>
/// <item>**words on a bar** are taken off by repeating an empty stretch of the same rows, since 1.1's bars and plates
/// are even along their length;</item>
/// <item>**anything else** is filled from what is round it (<see cref="Fill"/>: each pixel the mean of its neighbours,
/// coarse to fine), leaning on the pixels above and below where the art's lines are upright.</item>
/// </list>
/// <see cref="Commit"/> cuts the picture back into the pane's tiles, registers each changed one with the assets under
/// a name of its own and points the pane's quads at them.
/// </summary>
public sealed class LiftedArt
{
    readonly UiAssets assets;
    readonly NuiPane nui;
    readonly int originX, originY;

    /// <summary>Where the pane's own corner is on the sheet (a dialog's is not the sheet's): its controls' places are from here.</summary>
    public int OriginX => originX;
    public int OriginY => originY;

    /// <summary>The picture's size: the sheet's.</summary>
    public int Width { get; } = UiAssets.Width;
    public int Height { get; } = UiAssets.Height;

    /// <summary>Red, green and blue a pixel, top row first, 0 to 255.</summary>
    public float[] Rgb { get; }

    LiftedArt(UiAssets assets, NuiPane nui)
    {
        this.assets = assets;
        this.nui = nui;
        originX = nui.Pos.Length > 0 ? nui.Pos[0] : 0;
        originY = nui.Pos.Length > 1 ? nui.Pos[1] : 0;
        Rgb = new float[Width * Height * 3];
    }

    /// <summary>A pane's tiles laid out as one picture, as the pane draws them (later quads over earlier ones).</summary>
    public static LiftedArt Of(UiAssets assets, NuiPane nui)
    {
        var art = new LiftedArt(assets, nui);
        foreach (var q in nui.Quads)
            if (assets.Texture(q.Material) is { Width: > 0, Height: > 0 } image)
                art.Paste(image, art.originX + At(q.Pos, 0), art.originY + At(q.Pos, 1), At(q.Size, 0, image.Width), At(q.Size, 1, image.Height));
        return art;
    }

    static int At(int[] v, int i, int otherwise = 0) => v.Length > i ? v[i] : otherwise;

    /// <summary>The picture's row of a sheet height (y up), for the top of something <paramref name="h"/> high.</summary>
    public int Row(int y, int h) => Height - y - h;

    /// <summary>
    /// A picture laid over the art where the pane would draw it: the bottom left of <paramref name="image"/>, at most
    /// <paramref name="w"/> by <paramref name="h"/>, at sheet (<paramref name="x"/>, <paramref name="y"/>) — the
    /// renderer's own rule for a quad and a button (<c>UiCanvas.Draw</c>).
    /// </summary>
    public void Paste(TgaImage image, int x, int y, int w, int h)
    {
        int cw = Math.Min(w, image.Width), ch = Math.Min(h, image.Height), sy = image.Height - ch, top = Row(y, ch);
        for (int r = 0; r < ch; r++)
            for (int c = 0; c < cw; c++)
            {
                int px = x + c, py = top + r;
                if ((uint)px >= (uint)Width || (uint)py >= (uint)Height) continue;
                int s = ((sy + r) * image.Width + c) * 4, d = (py * Width + px) * 3;
                float a = image.Rgba[s + 3] / 255f;
                if (!image.HasAlpha) a = 1f;
                if (a <= 0f) continue;
                for (int k = 0; k < 3; k++) Rgb[d + k] = image.Rgba[s + k] * a + Rgb[d + k] * (1f - a);
            }
    }

    /// <summary>A mask the picture's size, all clear.</summary>
    public bool[] Mask() => new bool[Width * Height];

    /// <summary>
    /// Where two pictures of a button differ (its letters, when one is the picture and the other its rollover), laid
    /// where the button is: a channel more than <paramref name="threshold"/> apart.
    /// </summary>
    public void Differ(bool[] mask, TgaImage a, TgaImage b, int x, int y, int w, int h, int threshold = 20)
    {
        int cw = Math.Min(w, Math.Min(a.Width, b.Width)), ch = Math.Min(h, Math.Min(a.Height, b.Height)), top = Row(y, ch);
        for (int r = 0; r < ch; r++)
            for (int c = 0; c < cw; c++)
            {
                int sa = ((a.Height - ch + r) * a.Width + c) * 4, sb = ((b.Height - ch + r) * b.Width + c) * 4;
                int px = x + c, py = top + r;
                if ((uint)px >= (uint)Width || (uint)py >= (uint)Height) continue;
                if (Math.Abs(a.Rgba[sa] - b.Rgba[sb]) > threshold || Math.Abs(a.Rgba[sa + 1] - b.Rgba[sb + 1]) > threshold
                    || Math.Abs(a.Rgba[sa + 2] - b.Rgba[sb + 2]) > threshold)
                    mask[py * Width + px] = true;
            }
    }

    /// <summary>Where a picture laid at a button's place differs from the art as it is now (1.1's words baked into the tiles, against their rollover).</summary>
    public void DifferFromArt(bool[] mask, TgaImage image, int x, int y, int w, int h, int threshold = 20)
    {
        int cw = Math.Min(w, image.Width), ch = Math.Min(h, image.Height), top = Row(y, ch);
        for (int r = 0; r < ch; r++)
            for (int c = 0; c < cw; c++)
            {
                int s = ((image.Height - ch + r) * image.Width + c) * 4;
                int px = x + c, py = top + r;
                if ((uint)px >= (uint)Width || (uint)py >= (uint)Height) continue;
                int d = (py * Width + px) * 3;
                if (Math.Abs(image.Rgba[s] - Rgb[d]) > threshold || Math.Abs(image.Rgba[s + 1] - Rgb[d + 1]) > threshold
                    || Math.Abs(image.Rgba[s + 2] - Rgb[d + 2]) > threshold)
                    mask[py * Width + px] = true;
            }
    }

    /// <summary>A mask grown by <paramref name="steps"/> pixels, a step to each of a pixel's four neighbours at a time.</summary>
    public bool[] Grown(bool[] mask, int steps)
    {
        var a = (bool[])mask.Clone();
        var b = new bool[a.Length];
        for (int s = 0; s < steps; s++)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    b[i] = a[i] || (x > 0 && a[i - 1]) || (x < Width - 1 && a[i + 1]) || (y > 0 && a[i - Width]) || (y < Height - 1 && a[i + Width]);
                }
            (a, b) = (b, a);
        }
        return a;
    }

    /// <summary>
    /// **Words on a bar taken off** (1.1's gold labels and titles): in the picture's rows <paramref name="top"/> to
    /// <paramref name="bottom"/> (inclusive) and columns <paramref name="left"/> to <paramref name="right"/> (the bar),
    /// the words are found by their colour (gold or bone: red well over blue) and their box, a little wider for their
    /// dark rims, is filled from the longer empty stretch of the same rows beside them, repeated.
    /// </summary>
    public void LabelOff(int top, int bottom, int left, int right)
    {
        int x0 = int.MaxValue, x1 = -1;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x < right; x++)
            {
                int d = (y * Width + x) * 3;
                float r = Rgb[d], g = Rgb[d + 1], b = Rgb[d + 2];
                if (r > 100 && r - b > 40 && g > 70) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); }
            }
        if (x1 < 0) return;
        int boxL = Math.Max(left, x0 - 3), boxR = Math.Min(right - 1, x1 + 3);
        (int From, int To) before = (left + 3, boxL - 1), after = (boxR + 1, right - 3);
        var (from, to) = before.To - before.From >= after.To - after.From ? before : after;
        int length = Math.Max(4, to - from);
        for (int y = top; y <= bottom; y++)
            for (int x = boxL; x <= boxR; x++)
            {
                int sx = from + ((x - from) % length + length) % length;
                int s = (y * Width + sx) * 3, d = (y * Width + x) * 3;
                Rgb[d] = Rgb[s];
                Rgb[d + 1] = Rgb[s + 1];
                Rgb[d + 2] = Rgb[s + 2];
            }
    }

    /// <summary>
    /// Marks 1.1's gold lettering (and its dark rim, a little round it) inside rows <paramref name="top"/> to
    /// <paramref name="bottom"/> and columns <paramref name="left"/> to <paramref name="right"/> − 1 (ours): the
    /// words of a label on a panel, to be filled from round them (<see cref="Fill(bool[], float, int)"/>).
    /// </summary>
    public void MarkGold(bool[] mask, int top, int bottom, int left, int right)
    {
        for (int y = Math.Max(0, top); y <= Math.Min(Height - 1, bottom); y++)
            for (int x = Math.Max(0, left); x < Math.Min(Width, right); x++)
            {
                int d = (y * Width + x) * 3;
                float r = Rgb[d], g = Rgb[d + 1], b = Rgb[d + 2];
                if (r > 100 && r - b > 40 && g > 70) mask[y * Width + x] = true;
            }
    }

    /// <summary>Rows <paramref name="from"/> to <paramref name="from"/> + <paramref name="count"/> − 1, columns <paramref name="left"/> to <paramref name="right"/> − 1, copied to start at row <paramref name="to"/>.</summary>
    public void CopyRows(int from, int count, int to, int left, int right)
    {
        var copy = new float[count * (right - left) * 3];
        for (int r = 0; r < count; r++)
            Array.Copy(Rgb, ((from + r) * Width + left) * 3, copy, r * (right - left) * 3, (right - left) * 3);
        for (int r = 0; r < count; r++)
            if ((uint)(to + r) < (uint)Height)
                Array.Copy(copy, r * (right - left) * 3, Rgb, ((to + r) * Width + left) * 3, (right - left) * 3);
    }

    /// <summary>Where <paramref name="where"/> is set, the pixel taken from <paramref name="other"/> (the same art with its words elsewhere).</summary>
    public void Take(LiftedArt other, bool[] where)
    {
        for (int i = 0; i < where.Length; i++)
            if (where[i])
                for (int k = 0; k < 3; k++) Rgb[i * 3 + k] = other.Rgb[i * 3 + k];
    }

    /// <summary>
    /// **The masked pixels filled from round them**: each the weighted mean of its four neighbours, the ones above
    /// and below counting <paramref name="upright"/> times the ones beside (a window's bars and a door's planks are
    /// upright: they carry across a gap), worked out first on the picture at half size, and that at half again, so a
    /// wide gap fills as quickly as a narrow one; then <paramref name="steps"/> rounds at full size.
    /// </summary>
    public void Fill(bool[] mask, float upright = 1f, int steps = 600) => Fill(Rgb, mask, Width, Height, upright, steps);

    static void Fill(float[] rgb, bool[] mask, int w, int h, float upright, int steps)
    {
        var hole = new List<int>();
        for (int i = 0; i < mask.Length; i++) if (mask[i]) hole.Add(i);
        if (hole.Count == 0) return;
        if (Math.Min(w, h) > 16)
        {
            // Half size: each pixel the mean of the known pixels of its block of four; unknown where none is.
            int w2 = (w + 1) / 2, h2 = (h + 1) / 2;
            var small = new float[w2 * h2 * 3];
            var count = new int[w2 * h2];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (mask[i]) continue;
                    int j = (y / 2) * w2 + x / 2;
                    count[j]++;
                    for (int k = 0; k < 3; k++) small[j * 3 + k] += rgb[i * 3 + k];
                }
            var smallMask = new bool[w2 * h2];
            for (int j = 0; j < count.Length; j++)
                if (count[j] == 0) smallMask[j] = true;
                else for (int k = 0; k < 3; k++) small[j * 3 + k] /= count[j];
            Fill(small, smallMask, w2, h2, upright, Math.Max(100, steps / 2));
            foreach (int i in hole)
            {
                int j = (i / w / 2) * w2 + (i % w) / 2;
                for (int k = 0; k < 3; k++) rgb[i * 3 + k] = small[j * 3 + k];
            }
        }
        float wy = upright, wx = 1f;
        var next = new float[hole.Count * 3];
        for (int s = 0; s < steps; s++)
        {
            for (int n = 0; n < hole.Count; n++)
            {
                int i = hole[n], x = i % w, y = i / w;
                int up = y > 0 ? i - w : i, down = y < h - 1 ? i + w : i, left = x > 0 ? i - 1 : i, right = x < w - 1 ? i + 1 : i;
                float total = 2f * wy + 2f * wx;
                for (int k = 0; k < 3; k++)
                    next[n * 3 + k] = (wy * (rgb[up * 3 + k] + rgb[down * 3 + k]) + wx * (rgb[left * 3 + k] + rgb[right * 3 + k])) / total;
            }
            for (int n = 0; n < hole.Count; n++)
                for (int k = 0; k < 3; k++) rgb[hole[n] * 3 + k] = next[n * 3 + k];
        }
    }

    /// <summary>
    /// **The picture back into the pane's tiles**: each tile whose shown part changed is made again — the original's
    /// canvas, the changed part over it, its alpha as it was — registered with the assets as
    /// <c><paramref name="prefix"/>/&lt;material&gt;</c>, and the pane's quad pointed at it. A tile that did not change
    /// is left as 1.1's.
    /// </summary>
    public void Commit(string prefix)
    {
        for (int qi = 0; qi < nui.Quads.Count; qi++)
        {
            var q = nui.Quads[qi];
            if (assets.Texture(q.Material) is not { Width: > 0, Height: > 0 } image) continue;
            int x = originX + At(q.Pos, 0), y = originY + At(q.Pos, 1);
            int cw = Math.Min(At(q.Size, 0, image.Width), image.Width), ch = Math.Min(At(q.Size, 1, image.Height), image.Height);
            int sy = image.Height - ch, top = Row(y, ch);
            var rgba = (byte[])image.Rgba.Clone();
            bool changed = false;
            for (int r = 0; r < ch; r++)
                for (int c = 0; c < cw; c++)
                {
                    int px = x + c, py = top + r;
                    if ((uint)px >= (uint)Width || (uint)py >= (uint)Height) continue;
                    int s = ((sy + r) * image.Width + c) * 4, d = (py * Width + px) * 3;
                    if (image.HasAlpha && image.Rgba[s + 3] < 255) continue;            // the art's edge stays 1.1's
                    for (int k = 0; k < 3; k++)
                    {
                        byte v = (byte)Math.Clamp((int)MathF.Round(Rgb[d + k]), 0, 255);
                        if (v != rgba[s + k]) { rgba[s + k] = v; changed = true; }
                    }
                }
            if (!changed) continue;
            string name = $"{prefix}/{q.Material}";
            assets.Put(name, new TgaImage { Width = image.Width, Height = image.Height, Rgba = rgba, HasAlpha = image.HasAlpha });
            nui.Quads[qi] = q with { Material = name };
        }
    }
}
