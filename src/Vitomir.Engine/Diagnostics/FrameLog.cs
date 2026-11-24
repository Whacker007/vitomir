// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// Running totals a frame log reads once a row (ours): what the streamer and the renderers have done since
/// the process began. A row carries the difference from the row before.
/// </summary>
/// <param name="CellsLoaded">Cells of a cell level brought in.</param>
/// <param name="CellsUnloaded">Cells let go.</param>
/// <param name="TextureLoads">Streamed maps made again with finer mips.</param>
/// <param name="TextureShrinks">Streamed maps made again with coarser mips.</param>
/// <param name="UploadedBytes">Bytes sent to the GPU by the streamer: cells' geometry and maps' mips.</param>
/// <param name="PipelinesMade">Graphics pipelines made on first use (the game's own things: one for each blend state met).</param>
/// <param name="TexturesMade">The game's own textures made on first use.</param>
/// <param name="Gen0">The garbage collector's generation 0 collections.</param>
/// <param name="Gen2">Its generation 2 collections.</param>
public readonly record struct FrameLogTotals(long CellsLoaded, long CellsUnloaded, long TextureLoads, long TextureShrinks, long UploadedBytes,
                                             long PipelinesMade, long TexturesMade, long Gen0, long Gen2)
{
    /// <summary>
    /// What was done since <paramref name="before"/>. A total that went down is a counter that began again
    /// (a new renderer after a journey to another place): its whole value is what was done.
    /// </summary>
    public FrameLogTotals Since(FrameLogTotals before)
    {
        static long D(long now, long was) => now >= was ? now - was : now;
        return new FrameLogTotals(D(CellsLoaded, before.CellsLoaded), D(CellsUnloaded, before.CellsUnloaded), D(TextureLoads, before.TextureLoads),
                                  D(TextureShrinks, before.TextureShrinks), D(UploadedBytes, before.UploadedBytes), D(PipelinesMade, before.PipelinesMade),
                                  D(TexturesMade, before.TexturesMade), D(Gen0, before.Gen0), D(Gen2, before.Gen2));
    }
}

/// <summary>What a frame log reads when a row closes (ours): the totals, the cells in now, and the process's memory in MB.</summary>
public readonly record struct FrameLogSample(FrameLogTotals Totals, int CellsIn, double WorkingSetMB, double ManagedMB);

/// <summary>
/// One row of a frame log (ours): the time since the log began at the row's end, the frames in it and their
/// times in milliseconds (mean, 95th percentile, worst), the memory at its end, what was done in it, and
/// whether the window kept the keyboard focus and was not minimised all through it.
/// </summary>
public readonly record struct FrameLogRow(double Seconds, int Frames, double MeanMs, double P95Ms, double WorstMs,
                                          double WorkingSetMB, double ManagedMB, int CellsIn, FrameLogTotals Made, bool Focused = true);

/// <summary>
/// **A frame log: one row a second of how the game ran** (ours).
/// The first walk through the new town showed a bit of choppiness that seemed
/// to smooth out with play, and is worth instrumenting and testing. Each frame's time
/// is added (<see cref="Frame"/>); once the frames since the last row come to <see cref="RowSeconds"/> the
/// row is closed: how many frames, their mean, 95th percentile and worst times, the process's memory, and
/// what the streamer and the renderers did meanwhile. A frame longer than a second is a row of its own — a
/// hitch shows as one frame with a large worst time, not as missing rows. Arithmetic only:
/// <c>Vitomir.Classic/Harness/FrameLogHost.cs</c> reads the clock and the counters and writes the file.
///
/// A row also says whether the window had the keyboard focus (and was not minimised) all through
/// it. A second walk had one frame of 10.6 s nobody could explain; the game's loop stands still while
/// its window is not the active one (as 1.1's does), so the frame that ends such a wait is as
/// long as the wait, and the log could not tell it from a hitch. <see cref="Away"/> marks the wait and
/// <see cref="Frame"/> is told the window's state.
/// </summary>
public sealed class FrameLog(FrameLogTotals start, double rowSeconds = 1.0)
{
    /// <summary>The CSV's first line.</summary>
    public const string Header = "seconds,frames,mean_ms,p95_ms,worst_ms,working_set_mb,managed_mb,gc_gen0,gc_gen2," +
                                 "cells_in,cells_loaded,cells_unloaded,texture_loads,texture_shrinks,uploaded_mb,pipelines_made,textures_made,focused";

    /// <summary>How much frame time closes a row, in seconds.</summary>
    public double RowSeconds { get; } = rowSeconds;

    readonly List<double> times = [];
    FrameLogTotals before = start;
    double rowMs, totalMs;
    bool rowFocused = true, away;

    /// <summary>
    /// The loop stood still because the window is not the active one (no keyboard focus, or minimised): the
    /// frame that ends the wait, and so its row, is marked as not focused.
    /// </summary>
    public void Away() => away = true;

    /// <summary>
    /// A frame that took <paramref name="frameMs"/>, at whose end the window had the keyboard focus and was
    /// not minimised (<paramref name="focused"/>) or not. Answers the row it closed, if it closed one;
    /// <paramref name="sample"/> is asked only then.
    /// </summary>
    public FrameLogRow? Frame(double frameMs, Func<FrameLogSample> sample, bool focused = true)
    {
        if (!focused || away) rowFocused = false;
        away = false;
        times.Add(frameMs);
        rowMs += frameMs;
        totalMs += frameMs;
        return rowMs >= RowSeconds * 1000.0 ? Close(sample()) : null;
    }

    /// <summary>The log's end: the part of a second left over as a last row, or null when no frame is waiting.</summary>
    public FrameLogRow? End(Func<FrameLogSample> sample) => times.Count > 0 ? Close(sample()) : null;

    FrameLogRow Close(FrameLogSample s)
    {
        times.Sort();
        var row = new FrameLogRow(totalMs / 1000.0, times.Count, rowMs / times.Count, Percentile(times, 0.95), times[^1],
                                  s.WorkingSetMB, s.ManagedMB, s.CellsIn, s.Totals.Since(before), rowFocused);
        before = s.Totals;
        times.Clear();
        rowMs = 0;
        rowFocused = true;
        return row;
    }

    /// <summary>
    /// The value at <paramref name="share"/> of the way through <paramref name="sorted"/> (ascending), by
    /// nearest rank: the smallest value with at least that share of the values at or below it. Of twenty
    /// frames the 95th percentile is the nineteenth; of one frame, that frame.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sorted, double share)
    {
        if (sorted.Count == 0) return 0;
        int rank = (int)Math.Ceiling(share * sorted.Count - 1e-9);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    /// <summary>A row as its CSV line, in <see cref="Header"/>'s order, with a point for the decimal mark whatever the machine's language.</summary>
    public static string Line(FrameLogRow r) => string.Create(CultureInfo.InvariantCulture,
        $"{r.Seconds:0.00},{r.Frames},{r.MeanMs:0.00},{r.P95Ms:0.00},{r.WorstMs:0.00},{r.WorkingSetMB:0},{r.ManagedMB:0},{r.Made.Gen0},{r.Made.Gen2}," +
        $"{r.CellsIn},{r.Made.CellsLoaded},{r.Made.CellsUnloaded},{r.Made.TextureLoads},{r.Made.TextureShrinks},{r.Made.UploadedBytes / 1048576.0:0.0}," +
        $"{r.Made.PipelinesMade},{r.Made.TexturesMade},{(r.Focused ? 1 : 0)}");
}
