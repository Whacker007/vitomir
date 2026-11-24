// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;

namespace Vitomir.Render.Modern;

/// <summary>
/// A general PNG reader for the modern path's textures (ours): every colour type (grey, RGB, palette,
/// grey with alpha, RGBA), every bit depth (1, 2, 4, 8 and 16, the last cut to 8), the palette's
/// transparency chunk, all five row filters and Adam7 interlacing. It answers 8-bit RGBA, top row
/// first. The older <see cref="Png"/> reads back only the frames Vitomir itself writes; a glTF from
/// Blender or a generator can hold any PNG at all.
/// </summary>
public static class PngDecoder
{
    /// <summary>Whether <paramref name="bytes"/> start with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';

    /// <summary>Decodes a PNG file's bytes to RGBA8, top row first; null when the data is not a PNG it can read.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (!IsPng(bytes)) return null;
        int depth = 0, colour = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        int at = 8;
        while (at + 8 <= bytes.Length)
        {
            int length = (int)BE(bytes, at);
            if (length < 0 || at + 12 + length > bytes.Length) break;
            var type = bytes.Slice(at + 4, 4);
            var data = bytes.Slice(at + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                width = (int)BE(data, 0); height = (int)BE(data, 4);
                depth = data[8]; colour = data[9]; interlace = data[12];
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) trns = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
            else if (type.SequenceEqual("IEND"u8)) break;
            at += 12 + length;
        }
        if (width <= 0 || height <= 0 || depth == 0) return null;
        int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || colour == 3 && palette is null) return null;
        byte[] raw;
        int w0 = width;
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var all = new MemoryStream())
        {
            z.CopyTo(all);
            raw = all.ToArray();
        }
        int bitsPerPixel = channels * depth;
        int bpp = Math.Max(1, bitsPerPixel / 8);
        var rgba = new byte[width * height * 4];
        int offset = 0;
        if (interlace == 0)
        {
            if (!Pass(raw, ref offset, width, height, bitsPerPixel, bpp, (x, y, px) => Put(rgba, w0, x, y, px, colour, depth, palette, trns))) return null;
        }
        else
        {
            // Adam7: seven sub-images, each a regular grid within the picture.
            int[] x0 = [0, 4, 0, 2, 0, 1, 0], y0 = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            for (int p = 0; p < 7; p++)
            {
                int w = (width - x0[p] + dx[p] - 1) / dx[p], h = (height - y0[p] + dy[p] - 1) / dy[p];
                if (w <= 0 || h <= 0) continue;
                int pp = p;
                if (!Pass(raw, ref offset, w, h, bitsPerPixel, bpp,
                          (x, y, px) => Put(rgba, w0, x0[pp] + x * dx[pp], y0[pp] + y * dy[pp], px, colour, depth, palette, trns))) return null;
            }
        }
        return rgba;
    }

    delegate void Pixel(int x, int y, ReadOnlySpan<byte> row);

    /// <summary>One (sub-)image: unfilters each row and hands every pixel's samples on.</summary>
    static bool Pass(byte[] raw, ref int offset, int w, int h, int bitsPerPixel, int bpp, Pixel pixel)
    {
        int stride = (w * bitsPerPixel + 7) / 8;
        var prev = new byte[stride];
        var row = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            if (offset + 1 + stride > raw.Length) return false;
            int filter = raw[offset];
            Array.Copy(raw, offset + 1, row, 0, stride);
            offset += 1 + stride;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                row[i] = filter switch
                {
                    0 => row[i],
                    1 => (byte)(row[i] + a),
                    2 => (byte)(row[i] + b),
                    3 => (byte)(row[i] + ((a + b) >> 1)),
                    4 => (byte)(row[i] + Paeth(a, b, c)),
                    _ => row[i],
                };
            }
            for (int x = 0; x < w; x++) pixel(x, y, SamplesAt(row, x, bitsPerPixel));
            (prev, row) = (row, prev);
        }
        return true;
    }

    [ThreadStatic] static byte[]? unpacked;

    /// <summary>A pixel's samples: bytes as stored for 8 and 16 bits, one unpacked byte for 1, 2 and 4.</summary>
    static ReadOnlySpan<byte> SamplesAt(byte[] row, int x, int bitsPerPixel)
    {
        if (bitsPerPixel >= 8) return row.AsSpan(x * bitsPerPixel / 8, bitsPerPixel / 8);
        unpacked ??= new byte[1];
        int bit = x * bitsPerPixel;
        unpacked[0] = (byte)((row[bit >> 3] >> (8 - bitsPerPixel - (bit & 7))) & ((1 << bitsPerPixel) - 1));
        return unpacked;
    }

    static void Put(byte[] rgba, int width, int x, int y, ReadOnlySpan<byte> s, int colour, int depth, byte[]? palette, byte[]? trns)
    {
        int o = (y * width + x) * 4;
        switch (colour)
        {
            case 0:
            {
                byte g = Sample(s, 0, depth);
                rgba[o] = rgba[o + 1] = rgba[o + 2] = g;
                rgba[o + 3] = trns is { Length: >= 2 } && Raw(s, 0, depth) == TrnsGrey(trns, depth) ? (byte)0 : (byte)255;
                break;
            }
            case 2:
                rgba[o] = Sample(s, 0, depth); rgba[o + 1] = Sample(s, 1, depth); rgba[o + 2] = Sample(s, 2, depth);
                rgba[o + 3] = trns is { Length: >= 6 } && Raw(s, 0, depth) == Trns(trns, 0, depth) && Raw(s, 1, depth) == Trns(trns, 1, depth) && Raw(s, 2, depth) == Trns(trns, 2, depth) ? (byte)0 : (byte)255;
                break;
            case 3:
            {
                int i = s[0];
                if (palette is not null && i * 3 + 2 < palette.Length)
                {
                    rgba[o] = palette[i * 3]; rgba[o + 1] = palette[i * 3 + 1]; rgba[o + 2] = palette[i * 3 + 2];
                }
                rgba[o + 3] = trns is not null && i < trns.Length ? trns[i] : (byte)255;
                break;
            }
            case 4:
                rgba[o] = rgba[o + 1] = rgba[o + 2] = Sample(s, 0, depth);
                rgba[o + 3] = Sample(s, 1, depth);
                break;
            case 6:
                rgba[o] = Sample(s, 0, depth); rgba[o + 1] = Sample(s, 1, depth); rgba[o + 2] = Sample(s, 2, depth); rgba[o + 3] = Sample(s, 3, depth);
                break;
        }
    }

    static byte Sample(ReadOnlySpan<byte> s, int i, int depth) => depth switch
    {
        16 => s[i * 2],
        8 => s[i],
        _ => (byte)(s[i] * 255 / ((1 << depth) - 1)),
    };

    static int Raw(ReadOnlySpan<byte> s, int i, int depth) => depth == 16 ? s[i * 2] << 8 | s[i * 2 + 1] : s[i];

    static int TrnsGrey(byte[] t, int depth) => (t[0] << 8 | t[1]) & (depth == 16 ? 0xffff : (1 << depth) - 1);
    static int Trns(byte[] t, int c, int depth) => (t[c * 2] << 8 | t[c * 2 + 1]) & (depth == 16 ? 0xffff : 0xff);

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static uint BE(ReadOnlySpan<byte> b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
}
