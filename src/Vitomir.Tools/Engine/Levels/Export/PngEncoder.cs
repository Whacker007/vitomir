// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.IO.Compression;

namespace Vitomir.Engine.Levels.Export;

/// <summary>
/// A minimal PNG writer for the exporter (ours): 8-bit RGBA, rows top first, no filtering. The
/// renderer has its own (`Vitomir.Render.Png`), which the engine cannot reach.
/// </summary>
public static class PngEncoder
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

    /// <param name="rgba">Width × height pixels, four bytes each, the top row first.</param>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)width); BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", []);
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8];
        BigEndian(head, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head);
        s.Write(data);
        uint c = 0xFFFFFFFFu;
        for (int i = 4; i < 8; i++) c = Crc[(c ^ head[i]) & 0xff] ^ (c >> 8);
        foreach (var b in data) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
        var crc = new byte[4];
        BigEndian(crc, 0, c ^ 0xFFFFFFFFu);
        s.Write(crc);
    }

    static void BigEndian(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}
