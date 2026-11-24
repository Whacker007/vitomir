// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The platform's sound out.
namespace Vitomir.Platform.Audio;

/// <summary>The shape of the samples a source makes and a device takes: interleaved signed 16-bit, <see cref="Channels"/> to a frame.</summary>
public readonly record struct AudioFormat(int Rate, int Channels)
{
    /// <summary>The frames in a tenth of a second: the least an output keeps queued (<see cref="AudioOutput.Ahead"/>).</summary>
    public int Tenth => Rate / 10;
}

/// <summary>
/// **A module's sound** as the platform pulls it: PCM made when asked, on the main thread, at the source's own rate. The
/// Classic module's is 1.1's mixer (22,050 Hz, stereo); what it mixes and how is the module's.
/// </summary>
public interface IAudioSource
{
    /// <summary>The rate and channels it makes.</summary>
    AudioFormat Format { get; }

    /// <summary>Makes the next <paramref name="frames"/> frames into <paramref name="into"/> (frames × channels samples).</summary>
    void Render(Span<short> into, int frames);
}

/// <summary>
/// **Where the sound goes**: the host's device (SDL's, opened at the source's format), a queue the speakers drain on their
/// own. The platform never names SDL; a test gives a counting one.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>The format it was opened at.</summary>
    AudioFormat Format { get; }

    /// <summary>The frames queued that the speakers have not played yet.</summary>
    int Queued { get; }

    /// <summary>The host's clock, in nanoseconds: how long since the last pump.</summary>
    ulong NowNs { get; }

    /// <summary>Queues <paramref name="samples"/> (whole frames) behind what is there.</summary>
    void Queue(ReadOnlySpan<short> samples);
}

/// <summary>
/// **The platform's audio output**: once a frame, on the main thread, it tops the device's queue up from a module's
/// source — the least it keeps is <see cref="Ahead"/>, more after a long frame (<see cref="OutputQueue"/>) — and says how
/// much it mixed. **The amount hook** (<c>amount</c> on <see cref="Pump"/>) is told the frames the device wants (0 with no
/// device) and answers how many to mix: a recording writes the number down, a replay answers the recorded one whatever
/// this machine's device played, because a channel's end feeds the game (a document's voice ending, 1.1's
/// <c>ReadPane</c>) and the mixer ends its channels as it mixes them. With no device (a muted or headless run) only the
/// hook's frames are mixed, and dropped; with neither nothing is mixed, as a muted run of 1.1's mixer always did.
/// </summary>
public sealed class AudioOutput(IAudioDevice? device = null) : IDisposable
{
    short[] buffer = [];
    ulong lastPump;

    /// <summary>The device, or null when nothing goes out (headless, muted, or no device would open).</summary>
    public IAudioDevice? Device => device;

    /// <summary>Whether the sound goes anywhere.</summary>
    public bool Live => device is not null;

    /// <summary>The least kept queued ahead of the speakers, in frames: a tenth of a second unless set.</summary>
    public int Ahead { get => ahead ?? device?.Format.Tenth ?? 0; set => ahead = value; }
    int? ahead;

    /// <summary>Sees a copy of everything sent to the device (<c>--record-sound</c>'s file); not what a deviceless pump mixed and dropped.</summary>
    public Action<ReadOnlySpan<short>>? Tap { get; set; }

    /// <summary>The frames mixed by the last <see cref="Pump"/>.</summary>
    public int LastFrames { get; private set; }

    /// <summary>All the frames mixed since the output was made.</summary>
    public long Frames { get; private set; }

    /// <summary>
    /// One frame's top-up from <paramref name="source"/>: the frames the device wants (its queue up to <see cref="Ahead"/>,
    /// or to one and a half of the gap since the last pump), or what <paramref name="amount"/> answers to that, mixed and
    /// queued. Answers the frames mixed. A source of another format than the device's is refused.
    /// </summary>
    public int Pump(IAudioSource source, Func<int, int>? amount = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var format = source.Format;
        int wanted;
        if (device is not null)
        {
            if (device.Format != format) throw new InvalidOperationException($"a {format} source into a device opened at {device.Format}");
            int queued = device.Queued;
            ulong now = device.NowNs;
            int least = Ahead;
            int keep = lastPump == 0 ? least : OutputQueue.Frames(least, (now - lastPump) / 1e9, format.Rate);
            lastPump = now;
            wanted = keep - queued;
            if (amount is not null) wanted = amount(Math.Max(wanted, 0));
        }
        else wanted = amount?.Invoke(0) ?? 0;
        LastFrames = 0;
        if (wanted <= 0) return 0;
        int samples = wanted * format.Channels;
        if (buffer.Length < samples) buffer = new short[samples];
        var mixed = buffer.AsSpan(0, samples);
        source.Render(mixed, wanted);
        if (device is not null)
        {
            device.Queue(mixed);
            Tap?.Invoke(mixed);
        }
        LastFrames = wanted;
        Frames += wanted;
        return wanted;
    }

    public void Dispose() => device?.Dispose();
}
