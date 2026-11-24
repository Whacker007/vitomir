// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;

namespace Vitomir.Platform.Render;

/// <summary>Minimal PNG writer and reader (RGBA8, no filtering) for screenshots and sweeps.</summary>
public static class Png
{
    static readonly uint[] Crc = BuildCrc();

    static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>Any PNG's bytes decoded to RGBA8, top row first (the platform's decoder: every colour type, depth and interlace); null when they are not one.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> bytes, out int width, out int height) => Modern.PngDecoder.Decode(bytes, out width, out height);

    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width); WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;                       // 8-bit RGBA
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);                          // filter: none
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", []);
    }

    /// <summary>
    /// Reads a PNG this writer produced: 8-bit RGBA, no interlacing, every row unfiltered.
    /// Returns null for anything else, since nothing here needs a general decoder.
    /// </summary>
    public static byte[]? Read(string path, out int width, out int height)
    {
        width = height = 0;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || bytes[1] != 'P' || bytes[2] != 'N' || bytes[3] != 'G') return null;
        int at = 8;
        using var idat = new MemoryStream();
        while (at + 8 <= bytes.Length)
        {
            int length = (int)ReadBE(bytes, at);
            string type = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            int start = at + 8;
            if (type == "IHDR")
            {
                width = (int)ReadBE(bytes, start); height = (int)ReadBE(bytes, start + 4);
                if (bytes[start + 8] != 8 || bytes[start + 9] != 6 || bytes[start + 12] != 0) return null;
            }
            else if (type == "IDAT") idat.Write(bytes, start, length);
            else if (type == "IEND") break;
            at = start + length + 4;
        }
        if (width <= 0 || height <= 0) return null;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var rgba = new byte[width * height * 4];
        var row = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            if (z.ReadByte() != 0) return null;         // only the filter this writer uses
            z.ReadExactly(row);
            row.CopyTo(rgba, y * width * 4);
        }
        return rgba;
    }

    static uint ReadBE(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8];
        WriteBE(head, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head);
        s.Write(data);
        uint c = 0xFFFFFFFFu;
        for (int i = 4; i < 8; i++) c = Crc[(c ^ head[i]) & 0xff] ^ (c >> 8);
        foreach (var b in data) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
        var crc = new byte[4];
        WriteBE(crc, 0, c ^ 0xFFFFFFFFu);
        s.Write(crc);
    }

    static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}
