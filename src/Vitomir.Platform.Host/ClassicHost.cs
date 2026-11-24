// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic game's host in the platform's program: the
// platform's SDL host (the window in every mode the old program had, its GPU, its events, the clock, the pointer), with the sound
// device, for 1.1's frame controller.
using SDL;
using Vitomir.Platform.Audio;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Render;
using Vitomir.Platform.Render.Classic;
using Vitomir.Platform.Render.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>
/// **The Classic game's host in <c>vitomir-platform</c>** (<see cref="IClassicHost"/>): what 1.1's
/// frame controller asks of the program it runs in. Since  it is the platform's SDL host
/// (<see cref="SdlHost"/>), the same window code <c>vitomir-tools</c>' host is: the window and every one of its modes (a
/// window with its title bar or none, borderless full screen at the world's own size with the picture's bars, exclusive
/// full screen with the Surround desktop's rules, the window's place remembered, the grip, on top, Alt+Enter, the window's
/// lines in the log, a replay's plain or shrunk window), the GPU, the events made neutral (<see cref="SdlEvents"/>), the
/// clock, the pointer and the focus, the clipboard. What is this program's own: the sound device (<see cref="ClassicSpeakers"/>,
/// into the program's log) and, the harness's scripted input as <c>vitomir-tools</c> runs it (the platform's SDL host's,
/// <c>--ui-click</c>, <c>--ui-type</c>, <c>--menu-steps</c>: switches the program takes only with <c>--developer</c>), and where the window keeps its keys: the platform's, per game
/// (<see cref="Vitomir.Platform.Windowing.GameWindowSettings"/>), not the module's <c>vitomir.ini</c>.
/// </summary>
internal sealed class ClassicHost : SdlHost
{
    readonly Action<string> say;
    readonly Vitomir.Platform.Windowing.IWindowSettings? window;

    /// <param name="say">The program's log.</param>
    /// <param name="window">Where the game's window keeps its keys (the platform's, per game, <c>Games\&lt; id&gt; \window.json</c>).</param>
    /// <param name="windowless">an earlier build: the frames with no window (<c>--headless --frames</c>; <see cref="SdlHost.Windowless"/>).</param>
    public ClassicHost(Action<string> say, Vitomir.Platform.Windowing.IWindowSettings? window = null, bool windowless = false)
    {
        this.say = say;
        this.window = window;
        Windowless = windowless;
    }

    /// <summary>The platform's window settings for the game, not the module's <c>vitomir.ini</c>.</summary>
    public override Vitomir.Platform.Windowing.IWindowSettings? WindowSettings => window;

    public override AudioOutput Speakers(AudioFormat format) => new(ClassicSpeakers.Open(format, say));

    /// <summary>What was done with the console: into the program's log (and its console, the log's own).</summary>
    protected override void SayConsole(string line) => say(line);
}

/// <summary>
/// The speakers for the Classic game (taken from <c>src/Vitomir.Render/SdlSpeakers.cs</c> at 44531d8, its warnings into
/// the program's log): SDL's default playback device behind the platform's audio output, at the source's format.
/// </summary>
internal sealed unsafe class ClassicSpeakers : IAudioDevice
{
    SDL_AudioStream* stream;

    ClassicSpeakers(SDL_AudioStream* stream, AudioFormat format) { this.stream = stream; Format = format; }

    /// <summary>The device opened at the source's format, or null when SDL's audio would not start or there is no device (said).</summary>
    public static ClassicSpeakers? Open(AudioFormat format, Action<string> say)
    {
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            say($"no sound: SDL audio would not start ({SDL_GetError()})");
            return null;
        }
        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_S16LE, channels = format.Channels, freq = format.Rate };
        var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
        if (stream == null)
        {
            say($"no sound: no output device ({SDL_GetError()})");
            return null;
        }
        SDL_ResumeAudioStreamDevice(stream);
        return new ClassicSpeakers(stream, format);
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
