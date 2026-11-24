// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir;

/// <summary>
/// **Ours, a harness.** <c>--frame-dump &lt;folder&gt; [--frame-dump-fps n]</c>: every frame at <c>n</c> a second (30
/// unless given) of the game's clock is drawn and written as <c>frame00000.png</c> onwards, for a film to be made of them
/// (<c>ffmpeg -framerate 30 -i frame%05d.png walk.mp4</c>). The world steps at its own rate in between. A headless walk
/// (<c>--walk-to</c>, <c>--click</c>, <c>--walk</c>) dumps its frames; so does a windowed run, a replay of a tape above all
/// (<c>--replay a.tape --frame-dump frames</c>), with <c>--frame-dump-log</c> (<see cref="FrameDumpLog"/>) writing what was
/// pressed and where the camera was at each frame written, for the web site's clips (<c>tools/site/media.py</c>).
/// </summary>
sealed class FrameDump
{
    readonly string folder;
    readonly float every;
    float due;
    int written;

    FrameDump(string folder, float fps)
    {
        this.folder = folder;
        every = 1f / fps;
        Directory.CreateDirectory(folder);
    }

    /// <summary>
    /// A dump when the command line asks for one. A load ends the game's run and begins another in the same process (a
    /// quick load in a recorded tape, say), and the new run asks for the same folder: it gets the same dump, which carries
    /// on from the next picture instead of starting again at <c>frame00000.png</c> over the pictures already written.
    /// </summary>
    public static FrameDump? From(string? folder, string? fps)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        string full = Path.GetFullPath(folder);
        lock (open)
        {
            if (open.TryGetValue(full, out var dump)) return dump;
            dump = new FrameDump(folder, float.TryParse(fps, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 30f);
            open[full] = dump;
            return dump;
        }
    }

    // The process's dumps by folder, so a run after a load carries on its predecessor's.
    static readonly Dictionary<string, FrameDump> open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The world's clock has moved on by <paramref name="seconds"/>: when a frame is due, the camera
    /// is aimed and the frame drawn and written. True when one was.
    /// </summary>
    public bool Tick(float seconds, Action aim, Func<byte[]> draw, int width, int height)
    {
        if (!Due(seconds)) return false;
        aim();
        Png.Write(Path.Combine(folder, $"frame{written++:00000}.png"), width, height, draw());
        return true;
    }

    /// <summary>
    /// Whether a frame falls due as the clock moves on by <paramref name="seconds"/>: the first at once, then one every
    /// <c>1/n</c> of a second, so a run stepped at 144 frames a second dumps 60 of them a second at <c>n</c> = 60, and one
    /// stepped at 30 dumps each of its frames once (a film's frame is never written twice to make up the rate).
    /// </summary>
    internal bool Due(float seconds)
    {
        due -= seconds;
        if (due > 1e-4f) return false;
        due = MathF.Max(due + every, 0f);
        return true;
    }

    /// <summary>A picture already drawn written as the next frame (a window's dump, read back from the screen's own frame).</summary>
    public void Write(byte[] rgba, int width, int height) =>
        Png.Write(Path.Combine(folder, $"frame{written++:00000}.png"), width, height, rgba);

    /// <summary>The frames a second asked for.</summary>
    public float Fps => 1f / every;

    public int Written => written;
}
