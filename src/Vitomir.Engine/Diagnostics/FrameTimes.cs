// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--frame-times <file.tsv>` (ours): a row for every frame of the game's loop.
using System.Globalization;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **Every frame, written down** (ours, 1.1 has nothing like it).
/// The slow-frame lines say where a frame over 28 ms went and the frame log gives a row a second; neither can
/// say what an ordinary frame costs, which is the first thing a performance pass has to know. This writes one
/// tab-separated row for each frame of the window's loop — its whole time, each phase's
/// (<see cref="SlowFrames"/>'s own marks, so there is one set of timestamps), the step's and the draw's parts,
/// what it allocated and what the garbage collector did — to be summed afterwards by
/// <c>tools/perf/frames.py</c>.
///
/// **It measures and changes nothing**: no rule reads it, and a recording replays the same with it or without.
/// A row is formatted into a buffer kept for the purpose and written through a buffered file, so the log
/// allocates nothing a frame and does not show up in its own allocation column; the file is flushed when the
/// run ends (<see cref="Dispose"/>), so a run that is killed loses its last buffer's rows.
///
/// One file serves every run of a process — the front end's menus have no such loop and write no rows, a game
/// started from them appends — and <see cref="Run"/> counts the loops, so a journey that starts a run afresh
/// can be told from a second of the same place.
/// </summary>
public sealed class FrameTimes : IDisposable
{
    /// <summary>The columns after the phases: the frame's parts and what happened in it.</summary>
    static readonly string[] Tail =
    [
        "rest", "step", "animated", "shadows", "mirrors", "world", "interface", "posed", "passed", "things", "minds",
        "alloc_kb", "gc0", "gc1", "gc2", "gc_pause_ms", "textures_made", "buffers_rebuilt", "files_read", "read_ms", "focused",
    ];

    /// <summary>What a frame's row holds beside <see cref="SlowFrames"/>'s own numbers.</summary>
    /// <param name="SimMs">The game's clock at the frame's end.</param>
    /// <param name="StepMs">The world's step, inside the phase that steps and draws.</param>
    /// <param name="Posed">Animating things posed, lit and drawn; <paramref name="Passed"/> the ones passed over as out of view.</param>
    /// <param name="Things">Things in the place; <paramref name="Minds"/> the characters with a mind.</param>
    public readonly record struct Detail(long SimMs, double StepMs, double AnimatedMs, double ShadowsMs, double MirrorsMs, double WorldMs,
                                         double InterfaceMs, int Posed, int Passed, int Things, int Minds, int TexturesMade,
                                         int BuffersRebuilt, long FilesRead, double ReadMs, bool Focused);

    static int runs;
    static readonly HashSet<string> begun = new(StringComparer.OrdinalIgnoreCase);

    readonly StreamWriter file;
    readonly char[] row = new char[1024];
    int at;

    /// <summary>Which of the process's game loops this is, from 1: a new game, a load and a journey that starts afresh each begin one.</summary>
    public int Run { get; }

    /// <summary>The header for a loop whose phases are <paramref name="phases"/>.</summary>
    public static string Header(IEnumerable<string> phases) =>
        string.Join('\t', new[] { "run", "frame", "place", "sim_ms", "frame_ms" }.Concat(phases).Concat(Tail));

    FrameTimes(StreamWriter file, int run) => (this.file, Run) = (file, run);

    /// <summary>
    /// The log for <c>--frame-times</c>'s <paramref name="path"/>, or null when it was not asked for or the file
    /// cannot be written (said through <paramref name="say"/>; the game runs on without it). The process's first
    /// loop to open a path makes the file afresh, with its header; a later one appends.
    /// </summary>
    public static FrameTimes? Open(string? path, IEnumerable<string> phases, Action<string>? say = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string full = Path.GetFullPath(path);
            if (Path.GetDirectoryName(full) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            bool first;
            lock (begun) first = begun.Add(full);
            var writer = new StreamWriter(new FileStream(full, first ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Read, 1 << 16));
            if (first) writer.WriteLine(Header(phases));
            say?.Invoke($"frame times: a row a frame to {path}");
            return new FrameTimes(writer, Interlocked.Increment(ref runs));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            say?.Invoke($"frame times: {path} cannot be written ({e.Message}); no log");
            return null;
        }
    }

    /// <summary>The frame's row: <paramref name="frames"/>'s numbers for the frame it has just ended, and the rest.</summary>
    public void Frame(int frame, string place, SlowFrames frames, in Detail d)
    {
        at = 0;
        Put(Run); Put(frame); Put(place); Put(d.SimMs); Put(frames.LastMs);
        for (int i = 0; i < frames.Phases.Count; i++) Put(frames.LastPhase(i));
        Put(frames.LastRestMs);
        Put(d.StepMs); Put(d.AnimatedMs); Put(d.ShadowsMs); Put(d.MirrorsMs); Put(d.WorldMs); Put(d.InterfaceMs);
        Put(d.Posed); Put(d.Passed); Put(d.Things); Put(d.Minds);
        Put(frames.LastAllocated / 1024.0);
        Put(frames.LastCollections.Gen0); Put(frames.LastCollections.Gen1); Put(frames.LastCollections.Gen2);
        Put(frames.LastPauseMs);
        Put(d.TexturesMade); Put(d.BuffersRebuilt); Put(d.FilesRead); Put(d.ReadMs); Put(d.Focused ? 1 : 0);
        row[at - 1] = '\n';                                                 // the last tab is the row's end
        file.Write(row, 0, at);
    }

    void Put(long value)
    {
        value.TryFormat(row.AsSpan(at), out int n, default, CultureInfo.InvariantCulture);
        at += n;
        row[at++] = '\t';
    }

    /// <summary>A time or a size, to a thousandth: a microsecond of a millisecond, a byte of a kilobyte.</summary>
    void Put(double value)
    {
        value.TryFormat(row.AsSpan(at), out int n, "0.###", CultureInfo.InvariantCulture);
        at += n;
        row[at++] = '\t';
    }

    void Put(string value)
    {
        int n = Math.Min(value.Length, 64);
        for (int i = 0; i < n; i++) row[at++] = value[i] is '\t' or '\n' or '\r' ? ' ' : value[i];
        row[at++] = '\t';
    }

    public void Dispose() => file.Dispose();
}
