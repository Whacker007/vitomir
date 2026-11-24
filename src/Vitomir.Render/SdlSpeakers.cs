// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The speakers: the host's device behind the platform's audio output (AudioOut's pump before).
using Microsoft.Extensions.Logging;
using SDL;
using Vitomir.Engine.Diagnostics;
using Vitomir.Platform.Audio;
using static SDL.SDL3;

namespace Vitomir.Render;

/// <summary>
/// **The SDL device behind <see cref="AudioOutput"/>**. The original opens a 22,050 Hz, 16-bit stereo wave-out device
/// through Miles and lets Miles mix on its own timer; here the platform's output asks the module's source for a
/// little more each frame and queues it on an SDL stream opened at the source's format, which SDL converts for the real
/// device. Everything is on the frame loop's thread: the mixer is never touched from SDL's. What is mixed, when and how
/// much is the output's; this is only the queue and the clock.
/// </summary>
public sealed unsafe class SdlSpeakers : IAudioDevice
{
    SDL_AudioStream* stream;

    SdlSpeakers(SDL_AudioStream* stream, AudioFormat format) { this.stream = stream; Format = format; }

    /// <summary>
    /// The default output at <paramref name="format"/>, as the platform's output (<see cref="AudioOutput"/>), or a silent
    /// one (no device: it mixes only what a tape asks) when none opens, saying why.
    /// </summary>
    public static AudioOutput Output(AudioFormat format) => new(Open(format));

    /// <summary>Opens the default output, or answers null (and says why) when there is none.</summary>
    public static SdlSpeakers? Open(AudioFormat format)
    {
        var log = Diag.For<SdlSpeakers>();
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            log.LogWarning("no sound: SDL audio would not start ({Error})", SDL_GetError());
            return null;
        }
        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_S16LE, channels = format.Channels, freq = format.Rate };
        var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
        if (stream == null)
        {
            log.LogWarning("no sound: no output device ({Error})", SDL_GetError());
            return null;
        }
        SDL_ResumeAudioStreamDevice(stream);
        return new SdlSpeakers(stream, format);
    }

    public AudioFormat Format { get; }

    public int Queued => stream == null ? 0 : SDL_GetAudioStreamQueued(stream) / (2 * Format.Channels);

    public ulong NowNs => SDL_GetTicksNS();

    public void Queue(ReadOnlySpan<short> samples)
    {
        if (stream == null || samples.IsEmpty) return;
        fixed (short* p = samples) SDL_PutAudioStreamData(stream, (IntPtr)p, samples.Length * 2);
    }

    public void Dispose()
    {
        if (stream == null) return;
        SDL_DestroyAudioStream(stream);
        stream = null;
    }
}
