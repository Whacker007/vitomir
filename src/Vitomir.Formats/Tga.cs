// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats;

/// <summary>
/// TGA image decoded to RGBA8, rows top-down. The game ships uncompressed true-colour (24/32-bit)
/// and one 8-bit greyscale texture; run-length variants are read too for mods.
/// </summary>
public sealed class TgaImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>R, G, B, A bytes per pixel; row 0 is the top of the image.</summary>
    public byte[] Rgba { get; init; } = [];
    /// <summary>Whether the file carried an alpha channel (32-bit).</summary>
    public bool HasAlpha { get; init; }

    public static TgaImage Read(byte[] data, string name = "<tga>")
    {
        var r = new BinReader(data, name);
        int idLength = r.U8(), colorMapType = r.U8(), imageType = r.U8();
        r.Skip(5);                                     // colour-map spec
        r.Skip(4);                                     // x/y origin
        int w = r.U16(), h = r.U16(), bpp = r.U8(), desc = r.U8();
        if (colorMapType != 0) throw r.Error("colour-mapped TGA not supported");
        bool rle = imageType is 10 or 11;
        bool grey = imageType is 3 or 11;
        if (imageType is not (2 or 3 or 10 or 11)) throw r.Error($"TGA image type {imageType} not supported");
        if (grey ? bpp != 8 : bpp is not (24 or 32)) throw r.Error($"TGA {bpp} bpp not supported for type {imageType}");
        r.Skip(idLength);
        int bytesPer = bpp / 8, count = w * h;
        var src = new byte[count * bytesPer];
        if (!rle) r.Bytes(src.Length).CopyTo(src, 0);
        else
        {
            int i = 0;
            while (i < src.Length)
            {
                int header = r.U8(), n = (header & 0x7f) + 1;
                if (i + n * bytesPer > src.Length) throw r.Error("TGA run overruns the image");
                if ((header & 0x80) != 0)
                {
                    var px = r.Bytes(bytesPer);
                    for (int k = 0; k < n; k++, i += bytesPer) px.CopyTo(src, i);
                }
                else { r.Bytes(n * bytesPer).CopyTo(src, i); i += n * bytesPer; }
            }
        }
        bool topDown = (desc & 0x20) != 0, rightToLeft = (desc & 0x10) != 0;
        var rgba = new byte[count * 4];
        for (int y = 0; y < h; y++)
        {
            int sy = topDown ? y : h - 1 - y;
            for (int x = 0; x < w; x++)
            {
                int sx = rightToLeft ? w - 1 - x : x;
                int s = (sy * w + sx) * bytesPer, d = (y * w + x) * 4;
                if (grey) { rgba[d] = rgba[d + 1] = rgba[d + 2] = src[s]; rgba[d + 3] = 255; }
                else
                {
                    rgba[d] = src[s + 2]; rgba[d + 1] = src[s + 1]; rgba[d + 2] = src[s];
                    rgba[d + 3] = bytesPer == 4 ? src[s + 3] : (byte)255;
                }
            }
        }
        return new TgaImage { Width = w, Height = h, Rgba = rgba, HasAlpha = !grey && bpp == 32 };
    }

    /// <summary>
    /// A picture as the file 1.1's own interface art is: uncompressed, 32 bits a pixel with its alpha, bottom
    /// row first (ours: what the interface art tool writes).
    /// <paramref name="rgba"/> is R, G, B, A with row 0 the top, as <see cref="Rgba"/> is.
    /// </summary>
    public static byte[] Write(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width is <= 0 or > 65535 || height is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(width), $"a TGA of {width}x{height} cannot be written");
        if (rgba.Length < width * height * 4) throw new ArgumentException($"{rgba.Length} bytes is not a {width}x{height} picture", nameof(rgba));
        var data = new byte[18 + width * height * 4];
        data[2] = 2;
        data[12] = (byte)width; data[13] = (byte)(width >> 8);
        data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = 32; data[17] = 8;
        for (int y = 0; y < height; y++)
        {
            int s = (height - 1 - y) * width * 4, d = 18 + y * width * 4;
            for (int x = 0; x < width; x++, s += 4, d += 4)
                (data[d], data[d + 1], data[d + 2], data[d + 3]) = (rgba[s + 2], rgba[s + 1], rgba[s], rgba[s + 3]);
        }
        return data;
    }
}
