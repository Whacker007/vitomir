// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A `.wav` as the engine reads one.
//
// The reader is not a general RIFF parser, and the differences are the engine's:
//
//  * it seeks straight to offset 8 and wants `WAVE` there — the `RIFF` in front is never looked at;
//  * it skips the four bytes at 12 without reading them, **assuming the first chunk is `fmt `**;
//  * it walks the chunks after that until it meets `data`, skipping each by its size with **no pad
//    byte** for an odd size, which RIFF asks for.
//
// None of that bites the shipped data: every one of the 2,786 recordings the game ships (1,324 in
// Sounds.nob, 1,462 in Local_Eng.nob) is plain 16-bit PCM with `fmt ` first. 2,750 are mono 22,050 Hz,
// 34 are mono 44,100 Hz and one is stereo.
using System.Buffers.Binary;

namespace Vitomir.Formats;

/// <summary>What the engine keeps of a recording's header.</summary>
/// <param name="Rate">Samples a second.</param>
/// <param name="Bits">Bits a sample: <c>blockAlign ÷ channels × 8</c>, which is how the engine works it out.</param>
/// <param name="Channels">One or two. The engine only keeps "is it stereo" (a flag on the sound).</param>
/// <param name="FormatTag">1 for PCM. Read and not checked by the engine.</param>
/// <param name="DataOffset">Where the samples start in the file.</param>
/// <param name="DataSize">The `data` chunk's size, clipped here to what the file holds.</param>
public sealed record WavInfo(int Rate, int Bits, int Channels, int FormatTag, int DataOffset, int DataSize)
{
    public bool Stereo => Channels == 2;

    /// <summary>
    /// How long it plays, as the engine records it:
    /// <c>(int)(size × 8 ÷ (bits × rate) × 1000)</c>, and then **halved for a stereo file** — the
    /// formula has no channel count in it, so the halving is what makes a stereo length right.
    /// </summary>
    public int LengthMs
    {
        get
        {
            if (Bits <= 0 || Rate <= 0) return 0;
            uint ms = (uint)((double)DataSize * 8.0 / ((double)Bits * Rate) * 1000.0);
            return (int)(Stereo ? ms >> 1 : ms);
        }
    }
}

public static class Wav
{
    /// <summary>The header, or null for anything the engine would refuse (no `WAVE` at 8, no `data`).</summary>
    public static WavInfo? Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 36) return null;
        if (!file.Slice(8, 4).SequenceEqual("WAVE"u8)) return null;
        int fmtSize = BinaryPrimitives.ReadInt32LittleEndian(file[16..]);
        int tag = BinaryPrimitives.ReadUInt16LittleEndian(file[20..]);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(file[22..]);
        int rate = BinaryPrimitives.ReadInt32LittleEndian(file[24..]);
        int blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(file[32..]);
        if (channels <= 0) return null;
        int bits = blockAlign / channels << 3;
        long at = 20L + Math.Max(16, fmtSize);
        while (at + 8 <= file.Length)
        {
            var id = file.Slice((int)at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(file[((int)at + 4)..]);
            at += 8;
            if (id.SequenceEqual("data"u8))
            {
                int available = (int)Math.Max(0, Math.Min(size, file.Length - at));
                return new WavInfo(rate, bits, channels, tag, (int)at, available);
            }
            if (size < 0) return null;
            at += size;                                   // no pad byte: the engine's own walk
        }
        return null;
    }

    /// <summary>
    /// The samples as signed 16-bit, interleaved when stereo. Eight-bit data (unsigned in a WAV) is
    /// widened; nothing ships any.
    /// </summary>
    public static short[] Pcm16(ReadOnlySpan<byte> file, WavInfo info)
    {
        var data = file.Slice(info.DataOffset, info.DataSize);
        if (info.Bits == 8)
        {
            var wide = new short[data.Length];
            for (int i = 0; i < data.Length; i++) wide[i] = (short)((data[i] - 128) << 8);
            return wide;
        }
        var pcm = new short[data.Length / 2];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]);
        return pcm;
    }
}
