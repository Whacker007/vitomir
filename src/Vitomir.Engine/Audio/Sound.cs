// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One loaded recording — the original's 0xe0-byte record.
namespace Vitomir.Engine.Audio;

/// <summary>The recording's own flags.</summary>
[Flags]
public enum SoundFlags : uint
{
    None = 0,
    /// <summary>Its samples are in memory (bit 1). Streams never set it.</summary>
    Loaded = 0x1,
    /// <summary>Two channels (bit 2). A stereo recording can only ever play on a 2D channel.</summary>
    Stereo = 0x2,
    /// <summary>A `.nlp` mouth track was found beside it (bit 4).</summary>
    LipSync = 0x4,
    /// <summary>An `.mp3` loaded whole rather than streamed (bit 8).</summary>
    Compressed = 0x8,
}

/// <summary>
/// A recording the engine has loaded. The fields are the record's, and the two counts are what the
/// mixer's rules run on: <see cref="PlayCount"/> is how many channels are playing it — the mixer
/// stops the oldest before starting a fourth — and <see cref="LoadCount"/> is how many
/// owners it has.
/// </summary>
public sealed class Sound
{
    /// <summary>The key it is cached under: lower case, no folder, no extension, at most 31 characters.</summary>
    public required string Name { get; init; }
    /// <summary>The file it came from, which a stream opens again every time it plays.</summary>
    public required string Path { get; init; }
    /// <summary>Samples a second. Zero for a stream, which plays at whatever its file says.</summary>
    public int Rate { get; init; }
    /// <summary>Bits a sample.</summary>
    public int Bits { get; init; }
    public SoundFlags Flags { get; set; }
    /// <summary>
    /// How long it plays, in milliseconds: from the header for a `.wav`, from the stream's
    /// own clock for a stream, and <b>zero</b> for an `.mp3` loaded whole — the engine never sets it
    /// on that path.
    /// </summary>
    public int LengthMs { get; init; }
    /// <summary>A stream: opened from its file when it plays, and only on a stream channel.</summary>
    public bool Stream { get; init; }
    /// <summary>How many owners it has. The last release frees it.</summary>
    public int LoadCount { get; set; } = 1;
    /// <summary>How many channels are playing it.</summary>
    public int PlayCount { get; set; }

    /// <summary>The samples, 16-bit, interleaved when <see cref="Channels"/> is 2. Null for a stream.</summary>
    public short[]? Pcm { get; init; }
    /// <summary>1 or 2.</summary>
    public int Channels { get; init; } = 1;
    /// <summary>The rate the samples in <see cref="Pcm"/> were recorded at — what a stream plays at.</summary>
    public int SourceRate { get; init; }

    /// <summary>How a stream is opened when it plays. Null for anything in memory.</summary>
    public Func<IPcmSource>? Open { get; init; }

    /// <summary>
    /// Its lip-sync track: the mouth shape for each thirtieth of a
    /// second, 0 to 8, read from the `.nlp` beside it when it loaded (<c>Formats.Nlp</c>).
    /// Empty when there is none.
    /// </summary>
    public byte[] Lips { get; set; } = [];

    public override string ToString() => $"{Name} ({(Stream ? "stream" : $"{Rate} Hz")}, {LengthMs} ms)";
}

/// <summary>Decoded samples on demand — what a playing stream reads from.</summary>
public interface IPcmSource : IDisposable
{
    int Rate { get; }
    int Channels { get; }
    /// <summary>Fills <paramref name="into"/> with interleaved samples; answers how many were written (0 at the end).</summary>
    int Read(Span<short> into);
    /// <summary>Back to the start, for a loop.</summary>
    void Rewind();
}

/// <summary>Samples already in memory, read as a stream (a `.wav` found on the stream path).</summary>
public sealed class MemoryPcm(short[] pcm, int rate, int channels) : IPcmSource
{
    int at;
    public int Rate => rate;
    public int Channels => channels;
    public int Read(Span<short> into)
    {
        int n = Math.Min(into.Length, pcm.Length - at);
        pcm.AsSpan(at, n).CopyTo(into);
        at += n;
        return n;
    }
    public void Rewind() => at = 0;
    public void Dispose() { }
}
