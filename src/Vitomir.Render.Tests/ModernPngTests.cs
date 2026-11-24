// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The modern path's PNG reader (<see cref="PngDecoder"/>, ours) on PNGs assembled here chunk by chunk,
/// so each colour type, bit depth and row filter is exercised on known pixels.
/// </summary>
public class ModernPngTests
{
    /// <summary>A PNG from already-filtered rows (each starting with its filter byte).</summary>
    static byte[] Make(int w, int h, int depth, int colour, byte[] filteredRows, byte[]? plte = null, byte[]? trns = null, int interlace = 0)
    {
        var o = new MemoryStream();
        o.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length); Array.Reverse(len);
            o.Write(len); o.Write(System.Text.Encoding.ASCII.GetBytes(type)); o.Write(data); o.Write([0, 0, 0, 0]);   // the reader does not check CRCs
        }
        var ihdr = new byte[13];
        ihdr[3] = (byte)w; ihdr[7] = (byte)h; ihdr[8] = (byte)depth; ihdr[9] = (byte)colour; ihdr[12] = (byte)interlace;
        Chunk("IHDR", ihdr);
        if (plte is not null) Chunk("PLTE", plte);
        if (trns is not null) Chunk("tRNS", trns);
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(filteredRows);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        return o.ToArray();
    }

    [Fact]
    public void Rgb_rows_with_every_filter_decode_to_the_same_pixels()
    {
        // Two pixels a row, five rows, each row the pixels (10,20,30) (40,50,60) plus 10·row, with filters 0..4.
        var want = new byte[5][];
        for (int r = 0; r < 5; r++) want[r] = [(byte)(10 + r * 10), (byte)(20 + r * 10), (byte)(30 + r * 10), (byte)(40 + r * 10), (byte)(50 + r * 10), (byte)(60 + r * 10)];
        var rows = new List<byte>();
        for (int r = 0; r < 5; r++)
        {
            rows.Add((byte)r);
            var prev = r > 0 ? want[r - 1] : new byte[6];
            for (int i = 0; i < 6; i++)
            {
                int a = i >= 3 ? want[r][i - 3] : 0, b = prev[i], c = i >= 3 ? prev[i - 3] : 0;
                int pred = r switch
                {
                    0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
                    _ => Math.Abs(b - c) <= Math.Abs(a - c) && Math.Abs(b - c) <= Math.Abs(a + b - 2 * c) ? a : Math.Abs(a - c) <= Math.Abs(a + b - 2 * c) ? b : c,
                };
                rows.Add((byte)(want[r][i] - pred));
            }
        }
        var rgba = PngDecoder.Decode(Make(2, 5, 8, 2, rows.ToArray()), out int w, out int h);
        Assert.Equal((2, 5), (w, h));
        for (int r = 0; r < 5; r++)
            Assert.Equal(new byte[] { want[r][0], want[r][1], want[r][2], 255, want[r][3], want[r][4], want[r][5], 255 }, rgba![(r * 8)..(r * 8 + 8)]);
    }

    [Fact]
    public void A_two_bit_palette_image_takes_its_alpha_from_the_transparency_chunk()
    {
        // Four pixels in one byte: indices 0, 1, 2, 3.
        var rgba = PngDecoder.Decode(Make(4, 1, 2, 3, [0, 0b00_01_10_11], plte: [255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 9, 9], trns: [0, 128]), out _, out _);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 128, 0, 0, 255, 255, 9, 9, 9, 255 }, rgba);
    }

    [Fact]
    public void Sixteen_bit_grey_keeps_its_high_byte()
    {
        var rgba = PngDecoder.Decode(Make(2, 1, 16, 0, [0, 0x12, 0x34, 0xAB, 0xCD]), out _, out _);
        Assert.Equal(new byte[] { 0x12, 0x12, 0x12, 255, 0xAB, 0xAB, 0xAB, 255 }, rgba);
    }

    [Fact]
    public void An_interlaced_image_lands_every_pass_in_its_place()
    {
        // 2x2 RGBA, Adam7: pass 1 has (0,0); passes 2-5 are empty at this size; pass 6 has (1,0); pass 7 row 1 has (0,1),(1,1).
        byte[] P(int x, int y) => [(byte)(x * 100 + 1), (byte)(y * 100 + 1), 7, 255];
        var rows = new List<byte> { 0 }; rows.AddRange(P(0, 0));
        rows.Add(0); rows.AddRange(P(1, 0));
        rows.Add(0); rows.AddRange(P(0, 1)); rows.AddRange(P(1, 1));
        var rgba = PngDecoder.Decode(Make(2, 2, 8, 6, rows.ToArray(), interlace: 1), out _, out _);
        Assert.Equal(P(0, 0).Concat(P(1, 0)).Concat(P(0, 1)).Concat(P(1, 1)).ToArray(), rgba);
    }

    [Fact]
    public void What_is_not_a_png_is_answered_with_null()
    {
        Assert.Null(PngDecoder.Decode([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0], out _, out _));
    }
}
