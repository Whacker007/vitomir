// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Copied from src/Vitomir.Platform.Render/Modern/PngDecoder.cs at 388106d (which stays where it is), made
// public and made fast for the large maps (the models repository's bodies are three 4096² maps each).
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vitomir.Formats.Png;

/// <summary>
/// A general PNG reader: every colour type (grey, RGB, palette, grey with alpha, RGBA), every bit depth (1, 2, 4,
/// 8 and 16, the last cut to its high byte), the palette's transparency chunk, all five row filters and Adam7
/// interlacing. It answers 8-bit RGBA, top row first. The IDAT chunks are inflated as one stream and read a row at a
/// time; each row is unfiltered in place against the one above (Sub, Average and Paeth with the left pixel in locals, Up a
/// vector at a time) and put into the picture at once, a word a pixel for 8-bit images without a <c>tRNS</c> chunk and a
/// sample at a time otherwise. It is safe to call from several threads at once.
/// </summary>
public static class PngDecoder
{
    /// <summary>Whether <paramref name="bytes"/> start with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G'
        && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    /// <summary>The size, bit depth and colour type in the header, without decoding; false when it is not a PNG.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> bytes, out int width, out int height, out int bitDepth, out int colourType)
    {
        width = height = bitDepth = colourType = 0;
        if (!IsPng(bytes) || bytes.Length < 8 + 8 + 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        bitDepth = bytes[24];
        colourType = bytes[25];
        return width > 0 && height > 0;
    }

    /// <summary>
    /// A key for a PNG's content without reading its pixels: its length and each chunk's type, length and CRC (which covers
    /// the chunk's data), hashed. Two files with the same key hold the same image (short of a CRC-32 collision in every
    /// chunk at once); a few hundred bytes are hashed for a map of megabytes. Null when it is not a PNG.
    /// </summary>
    public static string? ContentKey(ReadOnlySpan<byte> png)
    {
        if (!IsPng(png)) return null;
        var summary = new List<byte>(256);
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, png.Length);
        summary.AddRange(word);
        int at = 8;
        while (at + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            if (length < 0 || at + 12L + length > png.Length) break;
            foreach (byte b in png.Slice(at, 8)) summary.Add(b);                // its length and type
            foreach (byte b in png.Slice(at + 8 + length, 4)) summary.Add(b);   // its CRC
            at += 12 + length;
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(summary.ToArray()));
    }

    /// <summary>Decodes a PNG file's bytes to RGBA8, top row first; null when the data is not a PNG it can read.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> bytes, out int width, out int height) =>
        Decode(new ReadOnlyMemory<byte>(bytes.ToArray()), out width, out height);   // the memory overload: a byte[] would bind to this one again

    /// <summary>Decodes a PNG file's bytes (not copied) to RGBA8, top row first; null when the data is not a PNG it can read.</summary>
    public static byte[]? Decode(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        width = height = 0;
        var bytes = png.Span;
        if (!IsPng(bytes)) return null;
        int depth = 0, colour = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        var idat = new List<(int Start, int Length)>();
        int at = 8;
        while (at + 12 <= bytes.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);
            if (length < 0 || at + 12L + length > bytes.Length) break;
            var type = bytes.Slice(at + 4, 4);
            var data = bytes.Slice(at + 8, length);
            if (type.SequenceEqual("IHDR"u8) && length >= 13)
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                depth = data[8]; colour = data[9]; interlace = data[12];
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) trns = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Add((at + 8, length));
            else if (type.SequenceEqual("IEND"u8)) break;
            at += 12 + length;
        }
        if (width <= 0 || height <= 0 || depth is not (1 or 2 or 4 or 8 or 16) || (long)width * height > int.MaxValue / 4) return null;
        int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || colour == 3 && palette is null) return null;
        int bitsPerPixel = channels * depth;
        int bpp = Math.Max(1, bitsPerPixel / 8);

        // Every byte of the picture is written below (the seven passes of an interlaced one cover it), so it need not be zeroed first.
        var rgba = GC.AllocateUninitializedArray<byte>(width * height * 4);
        try
        {
            // One inflate stream over every IDAT chunk, read a row at a time: each row unfiltered against the one
            // before and put into the picture while it is in the cache (no buffer of the whole filtered image).
            using var z = new ZLibStream(new ChunkStream(png, idat), CompressionMode.Decompress);
            if (interlace == 0)
            {
                if (!Pass(z, width, height, 0, 0, 1, 1, bitsPerPixel, bpp, rgba, width, colour, depth, palette, trns)) return null;
            }
            else
            {
                // Adam7: seven sub-images, each a regular grid within the picture.
                ReadOnlySpan<int> x0 = [0, 4, 0, 2, 0, 1, 0], y0 = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
                for (int p = 0; p < 7; p++)
                {
                    int w = (width - x0[p] + dx[p] - 1) / dx[p], h = (height - y0[p] + dy[p] - 1) / dy[p];
                    if (w <= 0 || h <= 0) continue;
                    if (!Pass(z, w, h, x0[p], y0[p], dx[p], dy[p], bitsPerPixel, bpp, rgba, width, colour, depth, palette, trns)) return null;
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { return null; }
        return rgba;
    }

    /// <summary>One (sub-)image of <paramref name="w"/> by <paramref name="h"/> pixels read from the inflated stream into the picture at (x0 + x·dx, y0 + y·dy).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static bool Pass(Stream z, int w, int h, int x0, int y0, int dx, int dy, int bitsPerPixel, int bpp, byte[] rgba, int width,
                     int colour, int depth, byte[]? palette, byte[]? trns)
    {
        int stride = (int)(((long)w * bitsPerPixel + 7) / 8);
        // A filter byte, the row, and four bytes more so a pixel's samples can be read as one word at the row's end.
        var current = new byte[1 + stride + 4];
        var previous = new byte[1 + stride + 4];
        for (int y = 0; y < h; y++)
        {
            z.ReadExactly(current.AsSpan(0, 1 + stride));
            var row = current.AsSpan(1, stride);
            if (!Unfilter(row, previous.AsSpan(1, stride), current[0], bpp)) return false;
            Expand(current, stride, w, rgba, width, x0, y0 + y * dy, dx, colour, depth, palette, trns);
            (previous, current) = (current, previous);
        }
        return true;
    }

    /// <summary>The IDAT chunks' data, one after another, as a stream.</summary>
    sealed class ChunkStream(ReadOnlyMemory<byte> png, List<(int Start, int Length)> chunks) : Stream
    {
        int chunk, within;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (chunk < chunks.Count && within >= chunks[chunk].Length) { chunk++; within = 0; }
            if (chunk >= chunks.Count || buffer.Length == 0) return 0;
            var (start, length) = chunks[chunk];
            int n = Math.Min(buffer.Length, length - within);
            png.Span.Slice(start + within, n).CopyTo(buffer);
            within += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Undoes one row's filter in place against the row above (zeros above the first). Sub, Average and Paeth carry the
    /// pixel to the left in locals for three- and four-byte pixels (the maps: RGB and RGBA), so a byte waits on no store.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static bool Unfilter(Span<byte> row, ReadOnlySpan<byte> prev, int filter, int bpp)
    {
        int n = row.Length;
        ref byte r = ref MemoryMarshal.GetReference(row);
        ref byte p = ref MemoryMarshal.GetReference(prev);
        switch (filter)
        {
            case 0: return true;
            case 2:
                AddUp(row, prev);
                return true;
            case 1 or 3 or 4 when bpp is 3 or 4:
                if (bpp == 3) Unfilter3(ref r, ref p, n, filter); else Unfilter4(ref r, ref p, n, filter);
                return true;
            case 1:
                for (int i = bpp; i < n; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - bpp);
                return true;
            case 3:
                for (int i = 0; i < bpp && i < n; i++) Unsafe.Add(ref r, i) += (byte)(Unsafe.Add(ref p, i) >> 1);
                for (int i = bpp; i < n; i++) Unsafe.Add(ref r, i) += (byte)((Unsafe.Add(ref r, i - bpp) + Unsafe.Add(ref p, i)) >> 1);
                return true;
            case 4:
                for (int i = 0; i < bpp && i < n; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref p, i);   // with nothing to the left, Paeth is the byte above
                for (int i = bpp; i < n; i++)
                    Unsafe.Add(ref r, i) += Paeth(Unsafe.Add(ref r, i - bpp), Unsafe.Add(ref p, i), Unsafe.Add(ref p, i - bpp));
                return true;
            default: return false;
        }
    }

    /// <summary>Sub, Average or Paeth on a row of three-byte pixels; the left pixel and the one above it held in locals.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static void Unfilter3(ref byte r, ref byte p, int n, int filter)
    {
        int a0 = 0, a1 = 0, a2 = 0, c0 = 0, c1 = 0, c2 = 0;
        for (int i = 0; i + 2 < n; i += 3)
        {
            ref byte x = ref Unsafe.Add(ref r, i);
            ref byte b = ref Unsafe.Add(ref p, i);
            int b0 = b, b1 = Unsafe.Add(ref b, 1), b2 = Unsafe.Add(ref b, 2);
            switch (filter)
            {
                case 1: a0 = (byte)(x + a0); a1 = (byte)(Unsafe.Add(ref x, 1) + a1); a2 = (byte)(Unsafe.Add(ref x, 2) + a2); break;
                case 3:
                    a0 = (byte)(x + ((a0 + b0) >> 1)); a1 = (byte)(Unsafe.Add(ref x, 1) + ((a1 + b1) >> 1)); a2 = (byte)(Unsafe.Add(ref x, 2) + ((a2 + b2) >> 1));
                    break;
                default:
                    a0 = (byte)(x + Paeth(a0, b0, c0)); a1 = (byte)(Unsafe.Add(ref x, 1) + Paeth(a1, b1, c1)); a2 = (byte)(Unsafe.Add(ref x, 2) + Paeth(a2, b2, c2));
                    c0 = b0; c1 = b1; c2 = b2;
                    break;
            }
            x = (byte)a0; Unsafe.Add(ref x, 1) = (byte)a1; Unsafe.Add(ref x, 2) = (byte)a2;
        }
    }

    /// <summary>Sub, Average or Paeth on a row of four-byte pixels; the left pixel and the one above it held in locals.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static void Unfilter4(ref byte r, ref byte p, int n, int filter)
    {
        int a0 = 0, a1 = 0, a2 = 0, a3 = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0;
        for (int i = 0; i + 3 < n; i += 4)
        {
            ref byte x = ref Unsafe.Add(ref r, i);
            ref byte b = ref Unsafe.Add(ref p, i);
            int b0 = b, b1 = Unsafe.Add(ref b, 1), b2 = Unsafe.Add(ref b, 2), b3 = Unsafe.Add(ref b, 3);
            switch (filter)
            {
                case 1:
                    a0 = (byte)(x + a0); a1 = (byte)(Unsafe.Add(ref x, 1) + a1); a2 = (byte)(Unsafe.Add(ref x, 2) + a2); a3 = (byte)(Unsafe.Add(ref x, 3) + a3);
                    break;
                case 3:
                    a0 = (byte)(x + ((a0 + b0) >> 1)); a1 = (byte)(Unsafe.Add(ref x, 1) + ((a1 + b1) >> 1));
                    a2 = (byte)(Unsafe.Add(ref x, 2) + ((a2 + b2) >> 1)); a3 = (byte)(Unsafe.Add(ref x, 3) + ((a3 + b3) >> 1));
                    break;
                default:
                    a0 = (byte)(x + Paeth(a0, b0, c0)); a1 = (byte)(Unsafe.Add(ref x, 1) + Paeth(a1, b1, c1));
                    a2 = (byte)(Unsafe.Add(ref x, 2) + Paeth(a2, b2, c2)); a3 = (byte)(Unsafe.Add(ref x, 3) + Paeth(a3, b3, c3));
                    c0 = b0; c1 = b1; c2 = b2; c3 = b3;
                    break;
            }
            x = (byte)a0; Unsafe.Add(ref x, 1) = (byte)a1; Unsafe.Add(ref x, 2) = (byte)a2; Unsafe.Add(ref x, 3) = (byte)a3;
        }
    }

    /// <summary>The Paeth predictor of left <paramref name="a"/>, above <paramref name="b"/> and above-left <paramref name="c"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte Paeth(int a, int b, int c)
    {
        int pa = b - c, pb = a - c, pc = pa + pb;
        pa = (pa ^ (pa >> 31)) - (pa >> 31);
        pb = (pb ^ (pb >> 31)) - (pb >> 31);
        pc = (pc ^ (pc >> 31)) - (pc >> 31);
        return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
    }

    /// <summary>The Up filter: each byte plus the one above, wrapping, a vector at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static void AddUp(Span<byte> row, ReadOnlySpan<byte> prev)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated)
            for (; i + Vector<byte>.Count <= row.Length; i += Vector<byte>.Count)
                (new Vector<byte>(row[i..]) + new Vector<byte>(prev[i..])).CopyTo(row[i..]);
        for (; i < row.Length; i++) row[i] += prev[i];
    }

    /// <summary>
    /// One unfiltered row (<paramref name="buffer"/> from its second byte, four spare bytes after it) into the picture's row
    /// <paramref name="py"/> at x0 + x·dx: 8-bit RGB, RGBA, grey and grey-alpha without <c>tRNS</c> a word a pixel, the rest
    /// a sample at a time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // a cold load decodes once: no slow first tier
    static void Expand(byte[] buffer, int stride, int w, byte[] rgba, int width, int x0, int py, int dx,
                       int colour, int depth, byte[]? palette, byte[]? trns)
    {
        ref byte s = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), 1);
        ref byte o = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(rgba), (py * width + x0) * 4);
        int step = dx * 4;
        if (depth == 8 && trns is null && BitConverter.IsLittleEndian && colour is 0 or 2 or 4 or 6)
        {
            switch (colour)
            {
                case 6:
                    if (dx == 1) { buffer.AsSpan(1, w * 4).CopyTo(rgba.AsSpan((py * width + x0) * 4)); break; }
                    for (int x = 0; x < w; x++) Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, x * step), Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref s, 4 * x)));
                    break;
                case 2:
                    // The word at a pixel's three samples takes the next pixel's first byte (or a spare one) as its fourth: replaced by opaque.
                    for (int x = 0; x < w; x++)
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, x * step), Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref s, 3 * x)) | 0xFF000000u);
                    break;
                case 0:
                    for (int x = 0; x < w; x++) Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, x * step), Unsafe.Add(ref s, x) * 0x010101u | 0xFF000000u);
                    break;
                case 4:
                    for (int x = 0; x < w; x++)
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, x * step), Unsafe.Add(ref s, 2 * x) * 0x010101u | (uint)Unsafe.Add(ref s, 2 * x + 1) << 24);
                    break;
            }
            return;
        }
        var row = buffer.AsSpan(1, stride);
        int bits = (colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 }) * depth;
        Span<byte> unpacked = stackalloc byte[1];
        for (int x = 0; x < w; x++)
        {
            scoped ReadOnlySpan<byte> sample;
            if (bits >= 8) sample = row.Slice(x * bits / 8, bits / 8);
            else
            {
                int bit = x * bits;
                unpacked[0] = (byte)((row[bit >> 3] >> (8 - bits - (bit & 7))) & ((1 << bits) - 1));
                sample = unpacked;
            }
            Put(rgba, width, x0 + x * dx, py, sample, colour, depth, palette, trns);
        }
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
                else rgba[o] = rgba[o + 1] = rgba[o + 2] = 0;
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
}
