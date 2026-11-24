// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--frame-log <file.csv>` (ours): the part of the frame log that reads
// the clock, the process and the renderers' counters and writes the file. `Engine.Diagnostics.FrameLog` has
// the rows' arithmetic.
using System.Diagnostics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Classic;

namespace Vitomir;

/// <summary>
/// One row a second of a played level, written to a CSV as it goes (each row flushed, so a run that is
/// killed keeps what it wrote): frame times, the process's memory, and what the cell level's streamer and
/// the renderers did in that second, and whether the window kept the keyboard focus. Made by
/// <see cref="Open"/> before the game's loop, told of each frame at its end (<see cref="Frame"/>) and of the
/// loop standing still while the window is not the active one (<see cref="Away"/>), closed with the
/// part-second left over.
/// </summary>
sealed class FrameLogHost : IDisposable
{
    readonly StreamWriter file;
    readonly Func<PlaceFrame> renderer;
    readonly FrameLog log;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly string path;
    long last;
    int rows;

    FrameLogHost(string path, StreamWriter file, Func<PlaceFrame> renderer)
    {
        (this.path, this.file, this.renderer) = (path, file, renderer);
        log = new FrameLog(Sample().Totals);
        file.WriteLine(FrameLog.Header);
        file.Flush();
    }

    /// <summary>
    /// The log for <c>--frame-log</c>'s <paramref name="path"/>, or null when it was not asked for or the
    /// file cannot be written (said, and the game runs on without it). <paramref name="renderer"/> answers
    /// the frame renderer in use, which a journey to another place replaces.
    /// </summary>
    public static FrameLogHost? Open(string? path, Func<PlaceFrame> renderer)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            var host = new FrameLogHost(path, new StreamWriter(path, append: false), renderer);
            Console.WriteLine($"frame log: a row a second to {path}");
            return host;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Console.WriteLine($"frame log: {path} cannot be written ({e.Message}); no log");
            return null;
        }
    }

    /// <summary>
    /// The end of a frame: its time is the wall clock's since the frame before ended (since <see cref="Open"/>,
    /// for the first). <paramref name="focused"/> is whether the window has the keyboard focus and is not
    /// minimised now (<c>WindowActive.Active</c>).
    /// </summary>
    public void Frame(bool focused = true)
    {
        long now = clock.ElapsedTicks;
        double ms = (now - last) * 1000.0 / Stopwatch.Frequency;
        last = now;
        if (log.Frame(ms, Sample, focused) is { } row) Write(row);
    }

    /// <summary>The loop is standing still because the window is not the active one: the frame that ends the wait is marked.</summary>
    public void Away() => log.Away();

    void Write(FrameLogRow row)
    {
        file.WriteLine(FrameLog.Line(row));
        file.Flush();
        rows++;
    }

    /// <summary>The totals now, the cells in, and the process's memory.</summary>
    FrameLogSample Sample()
    {
        var frame = renderer();
        // A cell level's streaming (which only vitomir-tools draws: ICellWorld).
        var stream = frame.Below?.Stream ?? default;
        var totals = new FrameLogTotals(stream.Loads, stream.Unloads, stream.TextureLoads, stream.TextureShrinks, stream.UploadedBytes,
                                        frame.World.PipelinesMade, frame.World.TexturesMade, GC.CollectionCount(0), GC.CollectionCount(2));
        using var process = Process.GetCurrentProcess();
        return new FrameLogSample(totals, stream.ResidentCells, process.WorkingSet64 / 1048576.0, GC.GetTotalMemory(false) / 1048576.0);
    }

    public void Dispose()
    {
        if (log.End(Sample) is { } row) Write(row);
        file.Dispose();
        Console.WriteLine($"frame log: {rows} rows in {path}");
    }
}
