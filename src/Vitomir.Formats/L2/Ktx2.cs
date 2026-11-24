// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;

namespace Vitomir.Formats.L2;

/// <summary>The block-compressed formats a cooked level's textures use, by their Vulkan format numbers (KTX2's <c>vkFormat</c>).</summary>
public enum Ktx2Format : uint
{
    Undefined = 0,
    Rgba8Unorm = 37,
    Rgba8Srgb = 43,
    /// <summary>One channel, from 4 × 4 blocks of 8 bytes: height maps (ours).</summary>
    Bc4Unorm = 139,
    /// <summary>Two channels, 8 bits of precision each from 4 × 4 blocks of 16 bytes: normal maps' X and Y.</summary>
    Bc5Unorm = 141,
    /// <summary>Four channels from 4 × 4 blocks of 16 bytes: colour (and alpha), roughness and metalness.</summary>
    Bc7Unorm = 145,
    Bc7Srgb = 146,
}

/// <summary>
/// **Ours (Phase 3): KTX 2.0 textures** (Khronos, "KTX File Format Specification", version 2.0) as a
/// cooked level keeps its materials' maps, <c>textures/&lt;name&gt;.ktx2</c>: one 2D image, a full mip
/// chain, no supercompression. The file's parts, little-endian: the 12-byte identifier; the header
/// (vkFormat, typeSize, width, height, depth 0, layers 0, faces 1, levels, supercompression 0); the
/// index (the data format descriptor's and key/value data's offsets and lengths, no supercompression
/// data); per level its offset, length and uncompressed length; the data format descriptor (a basic
/// block describing BC7, BC5 or RGBA8, its transfer function linear or sRGB); then the levels' bytes,
/// <b>smallest first</b>, each on a 16-byte boundary — so a streamer that wants only the small mips reads
/// only the end of the file's front.
/// </summary>
public sealed class Ktx2
{
    public static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public Ktx2Format Format { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>Level 0 (the full size) first.</summary>
    public List<byte[]> Levels { get; init; } = [];

    public bool Compressed => Format is Ktx2Format.Bc4Unorm or Ktx2Format.Bc5Unorm or Ktx2Format.Bc7Unorm or Ktx2Format.Bc7Srgb;
    public bool Srgb => Format is Ktx2Format.Bc7Srgb or Ktx2Format.Rgba8Srgb;

    /// <summary>A level's width and height.</summary>
    public static (int W, int H) LevelSize(int width, int height, int level) => (Math.Max(1, width >> level), Math.Max(1, height >> level));

    /// <summary>The bytes one level takes: 16 per 4 × 4 block when compressed (8 for BC4), 4 per pixel otherwise.</summary>
    public static int LevelBytes(Ktx2Format format, int width, int height, int level)
    {
        var (w, h) = LevelSize(width, height, level);
        if (format == Ktx2Format.Bc4Unorm) return ((w + 3) / 4) * ((h + 3) / 4) * 8;
        bool compressed = format is Ktx2Format.Bc5Unorm or Ktx2Format.Bc7Unorm or Ktx2Format.Bc7Srgb;
        return compressed ? ((w + 3) / 4) * ((h + 3) / 4) * 16 : w * h * 4;
    }

    /// <summary>The number of levels of a full chain, down to 1 × 1.</summary>
    public static int FullChain(int width, int height) => (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height)))) + 1;

    const int HeaderBytes = 12 + 9 * 4 + 4 * 4 + 2 * 8;   // identifier, header, index

    public byte[] Write()
    {
        if (Levels.Count == 0) throw new InvalidOperationException("a KTX2 file needs at least one level");
        for (int l = 0; l < Levels.Count; l++)
            if (Levels[l].Length != LevelBytes(Format, Width, Height, l))
                throw new InvalidOperationException($"level {l} is {Levels[l].Length} bytes, not {LevelBytes(Format, Width, Height, l)}");
        var dfd = Descriptor(Format);
        int levelIndex = HeaderBytes, dfdOffset = levelIndex + Levels.Count * 24;
        long at = Align(dfdOffset + dfd.Length);
        var offsets = new long[Levels.Count];
        for (int l = Levels.Count - 1; l >= 0; l--) { offsets[l] = at; at = Align(at + Levels[l].Length); }
        var bytes = new byte[offsets[0] + Levels[0].Length];
        var s = bytes.AsSpan();
        Identifier.CopyTo(s);
        int p = 12;
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(p), v); p += 4; }
        void U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(p), v); p += 8; }
        U32((uint)Format); U32(1); U32((uint)Width); U32((uint)Height); U32(0); U32(0); U32(1); U32((uint)Levels.Count); U32(0);
        U32((uint)dfdOffset); U32((uint)dfd.Length); U32(0); U32(0); U64(0); U64(0);
        for (int l = 0; l < Levels.Count; l++) { U64((ulong)offsets[l]); U64((ulong)Levels[l].Length); U64((ulong)Levels[l].Length); }
        dfd.CopyTo(s[dfdOffset..]);
        for (int l = 0; l < Levels.Count; l++) Levels[l].CopyTo(s[(int)offsets[l]..]);
        return bytes;
    }

    static long Align(long n) => (n + 15) & ~15L;

    /// <summary>The header of a KTX2 file: its format, size and where each level lies.</summary>
    public sealed record Header(Ktx2Format Format, int Width, int Height, (long Offset, long Length)[] Levels);

    /// <summary>
    /// Reads a file's header (the first bytes of it are enough: <paramref name="head"/> at least 80
    /// bytes plus 24 per level). Refuses anything but one 2D image of a format this build knows,
    /// uncompressed by any supercompression, whose levels are the lengths their sizes say.
    /// </summary>
    public static Header ReadHeader(ReadOnlySpan<byte> head, long fileLength, string name = "<ktx2>")
    {
        if (head.Length < HeaderBytes || !head[..12].SequenceEqual(Identifier)) throw new NodFormatException($"{name}: not a KTX 2.0 file");
        var bytes = head.ToArray();
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));
        var format = (Ktx2Format)U32(12);
        if (!Enum.IsDefined(format) || format == Ktx2Format.Undefined) throw new NodFormatException($"{name}: vkFormat {(uint)format} is not one this build reads");
        int width = (int)U32(20), height = (int)U32(24);
        if (U32(28) != 0 || U32(32) > 1 || U32(36) != 1) throw new NodFormatException($"{name}: not a single 2D image");
        int levels = (int)Math.Max(1, U32(40));
        if (U32(44) != 0) throw new NodFormatException($"{name}: supercompressed");
        if (width <= 0 || height <= 0 || width > 65536 || height > 65536 || levels > FullChain(width, height)) throw new NodFormatException($"{name}: {width} × {height} with {levels} levels");
        if (bytes.Length < HeaderBytes + levels * 24) throw new NodFormatException($"{name}: level index cut short");
        var list = new (long, long)[levels];
        for (int l = 0; l < levels; l++)
        {
            long off = (long)U64(HeaderBytes + 24 * l), len = (long)U64(HeaderBytes + 24 * l + 8);
            if (len != LevelBytes(format, width, height, l) || off < 0 || off + len > fileLength) throw new NodFormatException($"{name}: level {l} out of place");
            list[l] = (off, len);
        }
        return new Header(format, width, height, list);
    }

    public static Ktx2 Read(byte[] data, string name = "<ktx2>")
    {
        var h = ReadHeader(data, data.Length, name);
        return new Ktx2
        {
            Format = h.Format, Width = h.Width, Height = h.Height,
            Levels = [.. h.Levels.Select(l => data.AsSpan((int)l.Offset, (int)l.Length).ToArray())],
        };
    }

    /// <summary>
    /// The data format descriptor: a total size, then one basic descriptor block (Khronos Data Format
    /// Specification 1.3, section 5) — colour model BC7 (134), RGTC for BC4 and BC5 (131) or RGBSDA (1),
    /// primaries BT.709, transfer sRGB (2) or linear (1), the 4 × 4 block (or 1 × 1 texel) and its bytes,
    /// and a sample per channel.
    /// </summary>
    static byte[] Descriptor(Ktx2Format format)
    {
        (byte Model, byte Block, byte Bytes, (ushort Offset, byte Length, byte Channel)[] Samples) d = format switch
        {
            Ktx2Format.Bc7Unorm or Ktx2Format.Bc7Srgb => (134, 3, 16, [(0, 127, 0)]),
            Ktx2Format.Bc5Unorm => (131, 3, 16, [(0, 63, 0), (64, 63, 1)]),
            Ktx2Format.Bc4Unorm => (131, 3, 8, [(0, 63, 0)]),
            _ => (1, 0, 4, [(0, 7, 0), (8, 7, 1), (16, 7, 2), (24, 7, 15)]),
        };
        bool srgb = format is Ktx2Format.Bc7Srgb or Ktx2Format.Rgba8Srgb;
        int blockSize = 24 + 16 * d.Samples.Length;
        var b = new byte[4 + blockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)b.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 0);                       // vendor 0 (Khronos), type 0 (basic)
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), 2);                       // version 1.3
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), (ushort)blockSize);
        b[12] = d.Model; b[13] = 1; b[14] = (byte)(srgb ? 2 : 1); b[15] = 0;
        b[16] = d.Block; b[17] = d.Block; b[18] = 0; b[19] = 0;
        b[20] = d.Bytes;
        for (int i = 0; i < d.Samples.Length; i++)
        {
            int at = 28 + 16 * i;
            var (offset, length, channel) = d.Samples[i];
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), offset);
            b[at + 2] = length;
            // RGBA8's alpha is linear whatever the colour's transfer (bit 6, KHR_DF_SAMPLE_DATATYPE_LINEAR).
            b[at + 3] = (byte)(channel | (channel == 15 && srgb ? 0x40 : 0));
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 8), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 12), d.Model == 1 ? 255u : uint.MaxValue);
        }
        return b;
    }
}
