// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One of the game's films, decoded by the player's own FFmpeg.
//
// The engine hands FFmpeg the `.bik` and reads two plain streams back out of it: the picture as raw
// RGBA, a frame at a time, and the sound as raw 16-bit stereo. Nothing here knows anything about
// Bink; FFmpeg does. Two processes rather than one, because one process with two pipes has to be
// drained in lock step or it deadlocks, and a film is short enough that decoding it twice costs
// nothing anybody can see.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Vitomir.Engine.Audio;

namespace Vitomir.Engine.Video;

/// <summary>What a film is, before a frame of it is decoded.</summary>
/// <param name="Width">In pixels.</param>
/// <param name="Height">In pixels.</param>
/// <param name="Fps">Frames a second, as the file declares it.</param>
/// <param name="Seconds">How long it runs, or 0 when the file will not say.</param>
/// <param name="HasSound">Whether there is an audio stream to play alongside.</param>
public readonly record struct FilmInfo(int Width, int Height, double Fps, double Seconds, bool HasSound)
{
    public bool Sensible => Width is > 0 and <= 8192 && Height is > 0 and <= 8192 && Fps is > 0 and <= 1000;
}

/// <summary>
/// A film being played: the picture pulled frame by frame, the sound handed to the mixer as an
/// ordinary streaming channel. Dispose ends it, whether it finished or was skipped.
/// </summary>
public sealed partial class VideoClip : IFilm
{
    readonly FFmpegTool tool;
    readonly string path;
    Process? video;
    Stream? frames;
    byte[] pixels = [];
    int decoded = -1;               // the index of the frame sitting in `pixels`, -1 before the first
    bool ended;

    VideoClip(FFmpegTool tool, string path, FilmInfo info)
    {
        this.tool = tool;
        this.path = path;
        Info = info;
    }

    /// <summary>What the file says about itself.</summary>
    public FilmInfo Info { get; }

    /// <summary>The frame now in <see cref="Pixels"/>, or −1 before the first one is read.</summary>
    public int FrameIndex => decoded;

    /// <summary>The current frame, RGBA, top row first — empty until <see cref="Advance"/> succeeds.</summary>
    public ReadOnlySpan<byte> Pixels => pixels;

    /// <summary>Whether the film has run out.</summary>
    public bool Ended => ended;

    /// <summary>
    /// Opens a film. Answers null when the file is not there, when FFmpeg will not describe it, or
    /// when what it says makes no sense — the caller's job in every one of those cases is to carry
    /// on without a film.
    /// </summary>
    public static VideoClip? Open(FFmpegTool tool, string path)
    {
        if (!File.Exists(path)) return null;
        var info = Describe(tool, path);
        if (info is not { Sensible: true } good) return null;
        var clip = new VideoClip(tool, path, good);
        return clip.StartPicture() ? clip : null;
    }

    // --- what is in the file ----------------------------------------------------------------

    static FilmInfo? Describe(FFmpegTool tool, string path)
    {
        if (tool.FFprobe is { } probe && FromProbe(probe, path) is { } byProbe) return byProbe;
        return FromBanner(tool.FFmpeg, path);
    }

    /// <summary>ffprobe, which answers in `key=value` lines and is the reliable way round.</summary>
    static FilmInfo? FromProbe(string probe, string path)
    {
        string text = Run(probe,
            ["-v", "error", "-show_entries", "stream=codec_type,width,height,r_frame_rate",
             "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1", path], out int code);
        if (code != 0) return null;
        int width = 0, height = 0; double fps = 0, seconds = 0; bool sound = false, inVideo = false;
        foreach (var line in text.Split('\n'))
        {
            var (key, value) = Split(line);
            switch (key)
            {
                case "codec_type": inVideo = value == "video"; sound |= value == "audio"; break;
                case "width" when inVideo: int.TryParse(value, out width); break;
                case "height" when inVideo: int.TryParse(value, out height); break;
                case "r_frame_rate" when inVideo && fps == 0: fps = Ratio(value); break;
                case "duration": double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds); break;
            }
        }
        return new FilmInfo(width, height, fps, seconds, sound);
    }

    /// <summary>
    /// What `ffmpeg -i` prints when asked about a file it will not be writing anywhere. A fallback
    /// for an install with no ffprobe beside its ffmpeg, which the zip builds sometimes are.
    /// </summary>
    static FilmInfo? FromBanner(string ffmpeg, string path)
    {
        Run(ffmpeg, ["-hide_banner", "-i", path], out _, out string errors);
        var stream = VideoLine().Match(errors);
        if (!stream.Success) return null;
        double fps = double.TryParse(Fps().Match(errors) is { Success: true } f ? f.Groups[1].Value : "",
                                     NumberStyles.Float, CultureInfo.InvariantCulture, out double got) ? got : 0;
        double seconds = 0;
        if (Duration().Match(errors) is { Success: true } d)
            seconds = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60
                    + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
        return new FilmInfo(int.Parse(stream.Groups[1].Value), int.Parse(stream.Groups[2].Value), fps, seconds,
                            errors.Contains(": Audio:", StringComparison.Ordinal));
    }

    [GeneratedRegex(@": Video:.*?, (\d+)x(\d+)")] private static partial Regex VideoLine();
    [GeneratedRegex(@"([\d.]+) fps")] private static partial Regex Fps();
    [GeneratedRegex(@"Duration: (\d+):(\d+):([\d.]+)")] private static partial Regex Duration();

    static (string Key, string Value) Split(string line)
    {
        int at = line.IndexOf('=');
        return at < 0 ? ("", "") : (line[..at].Trim(), line[(at + 1)..].Trim());
    }

    /// <summary>`30000/1001` and the like — FFmpeg states a frame rate as an exact ratio.</summary>
    static double Ratio(string text)
    {
        int at = text.IndexOf('/');
        if (at < 0) return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double flat) ? flat : 0;
        return double.TryParse(text[..at], out double top) && double.TryParse(text[(at + 1)..], out double bottom)
            && bottom != 0 ? top / bottom : 0;
    }

    // --- the picture ------------------------------------------------------------------------

    bool StartPicture()
    {
        pixels = new byte[Info.Width * Info.Height * 4];
        // An improved film (an MP4 the player made, ours) is decoded with the graphics card's help where it offers it: it is up to
        // four times 1.1's size. 1.1's own Bink films have no such help and are decoded as they always were.
        string[] help = Path.GetExtension(path).Equals(".bik", StringComparison.OrdinalIgnoreCase) ? [] : ["-hwaccel", "auto"];
        video = Spawn(tool.FFmpeg,
            ["-v", "error", "-nostdin", .. help, "-i", path, "-an", "-f", "rawvideo", "-pix_fmt", "rgba", "-"]);
        frames = video?.StandardOutput.BaseStream;
        return frames is not null;
    }

    /// <summary>
    /// Brings the picture up to <paramref name="ms"/> since the film started, throwing away any
    /// frame that is already late — a slow machine drops frames rather than playing the film in slow
    /// motion, which is what every other player does and what the sound forces anyway.
    ///
    /// Answers whether there is a frame to draw. False once the film has run out.
    /// </summary>
    public bool Advance(int ms)
    {
        if (ended || frames is null) return false;
        int want = (int)(ms * Info.Fps / 1000.0);
        while (decoded < want)
            if (!ReadFrame()) return decoded >= 0 && !ended;
        return decoded >= 0;
    }

    bool ReadFrame()
    {
        int at = 0;
        while (at < pixels.Length)
        {
            int got = frames!.Read(pixels, at, pixels.Length - at);
            if (got <= 0) { ended = true; return false; }
            at += got;
        }
        decoded++;
        return true;
    }

    // --- the sound --------------------------------------------------------------------------

    /// <summary>
    /// The film's sound as something the mixer can play: a streaming <see cref="Sound"/> whose
    /// source is a second FFmpeg turning the same file into 16-bit stereo. Null when the film has
    /// none. The mixer owns it from here and disposes it when the channel stops.
    /// </summary>
    public Sound? Sound()
    {
        if (!Info.HasSound) return null;
        string file = path, program = tool.FFmpeg;
        return new Sound
        {
            Name = Path.GetFileNameWithoutExtension(path), Path = path, Stream = true,
            Rate = Miles.OutputRate, SourceRate = Miles.OutputRate, Channels = 2, Bits = 16,
            LengthMs = (int)(Info.Seconds * 1000),
            Open = () => new Pcm(program, file),
        };
    }

    /// <summary>16-bit stereo out of FFmpeg's mouth, at the mixer's own rate so nothing resamples.</summary>
    sealed class Pcm : IPcmSource
    {
        readonly Process? ffmpeg;
        readonly Stream? bytes;
        byte[] spare = [];

        public Pcm(string ffmpeg, string path)
        {
            this.ffmpeg = Spawn(ffmpeg,
                ["-v", "error", "-nostdin", "-i", path, "-vn", "-f", "s16le",
                 "-ar", Miles.OutputRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-"]);
            bytes = this.ffmpeg?.StandardOutput.BaseStream;
        }

        public int Rate => Miles.OutputRate;
        public int Channels => 2;

        public int Read(Span<short> into)
        {
            if (bytes is null) return 0;
            int want = into.Length * 2;
            if (spare.Length < want) spare = new byte[want];
            int at = 0;
            while (at < want)
            {
                int got = bytes.Read(spare, at, want - at);
                if (got <= 0) break;
                at += got;
            }
            int samples = at / 2;
            for (int i = 0; i < samples; i++) into[i] = (short)(spare[i * 2] | (spare[i * 2 + 1] << 8));
            return samples;
        }

        /// <summary>A film's sound never loops and never rewinds; the mixer only asks when it does.</summary>
        public void Rewind() { }

        public void Dispose() => Stop(ffmpeg);
    }

    // --- running the program ----------------------------------------------------------------

    static Process? Spawn(string program, string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        try
        {
            var p = Process.Start(start);
            // Nothing reads the error pipe, and a full one would wedge the decoder, so drain it.
            if (p is not null) _ = p.StandardError.ReadToEndAsync();
            return p;
        }
        catch { return null; }
    }

    static string Run(string program, string[] arguments, out int code) => Run(program, arguments, out code, out _);

    static string Run(string program, string[] arguments, out int code, out string errors)
    {
        code = -1; errors = "";
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(start);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEndAsync();
            var problems = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15000)) { try { p.Kill(true); } catch { } return ""; }
            code = p.ExitCode;
            errors = problems.Result;
            return output.Result;
        }
        catch { return ""; }
    }

    static void Stop(Process? p)
    {
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(true); } catch { }
        try { p.Dispose(); } catch { }
    }

    public void Dispose()
    {
        ended = true;
        try { frames?.Dispose(); } catch { }
        Stop(video);
        video = null;
        frames = null;
    }
}
