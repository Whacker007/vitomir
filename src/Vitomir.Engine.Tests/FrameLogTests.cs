// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The frame log's rows (ours).** The first walk through the
/// new town showed a bit of choppiness that seemed to smooth out with play, and is
/// worth instrumenting. <c>--frame-log</c> writes a row a second; these pin the arithmetic of
/// <see cref="FrameLog"/> — when a row closes, its mean, 95th percentile and worst frame times, what was
/// done in it as differences of running totals, and the CSV line — so a number in a play-test's log means what
/// the doc says it means. The clock, the process's memory and the file are <c>Vitomir.Classic/Harness/FrameLogHost.cs</c>'s.
/// And the <c>focused</c> column — a row says whether the window kept the keyboard focus, and the
/// long frame that ends a wait without it is marked, so it is not read as a hitch.
/// </summary>
public class FrameLogTests
{
    static FrameLogSample Sample(FrameLogTotals totals = default, int cells = 0, double working = 0, double managed = 0) =>
        new(totals, cells, working, managed);

    static List<FrameLogRow> Run(FrameLog log, IEnumerable<double> frames, Func<FrameLogSample>? sample = null)
    {
        var rows = new List<FrameLogRow>();
        foreach (double ms in frames)
            if (log.Frame(ms, sample ?? (() => Sample())) is { } row) rows.Add(row);
        return rows;
    }

    [Fact]
    public void A_row_closes_when_its_frames_come_to_a_second()
    {
        var log = new FrameLog(default);
        var rows = Run(log, Enumerable.Repeat(10.0, 250));
        Assert.Equal(2, rows.Count);
        Assert.Equal(100, rows[0].Frames);
        Assert.Equal(1.0, rows[0].Seconds, 6);
        Assert.Equal(2.0, rows[1].Seconds, 6);
        // The half second left over is the last row, written when the log ends.
        var last = log.End(() => Sample());
        Assert.NotNull(last);
        Assert.Equal(50, last.Value.Frames);
        Assert.Equal(2.5, last.Value.Seconds, 6);
        Assert.Null(log.End(() => Sample()));
    }

    [Fact]
    public void A_rows_times_are_the_mean_the_95th_percentile_and_the_worst_of_its_frames()
    {
        // Ninety frames at 10 ms, nine at 20 and one hitch of 80: the mean hides the hitch, the worst shows it,
        // and the 95th percentile says the slow tail is 20 ms frames.
        var frames = Enumerable.Repeat(10.0, 90).Concat(Enumerable.Repeat(20.0, 9)).Append(80.0);
        var log = new FrameLog(default, rowSeconds: 10);
        Assert.Empty(Run(log, frames));
        var row = log.End(() => Sample())!.Value;
        Assert.Equal(100, row.Frames);
        Assert.Equal(11.6, row.MeanMs, 6);
        Assert.Equal(20.0, row.P95Ms);
        Assert.Equal(80.0, row.WorstMs);
        Assert.Equal(1.16, row.Seconds, 6);
    }

    [Fact]
    public void The_95th_percentile_is_by_nearest_rank()
    {
        double[] twenty = [.. Enumerable.Range(1, 20).Select(i => (double)i)];
        Assert.Equal(19, FrameLog.Percentile(twenty, 0.95));
        Assert.Equal(10, FrameLog.Percentile(twenty, 0.50));
        Assert.Equal(20, FrameLog.Percentile(twenty, 1.0));
        Assert.Equal(1, FrameLog.Percentile(twenty, 0.0));
        Assert.Equal(7, FrameLog.Percentile([7], 0.95));
        Assert.Equal(9, FrameLog.Percentile([3, 9], 0.95));
        Assert.Equal(0, FrameLog.Percentile([], 0.95));
        double[] hundred = [.. Enumerable.Range(1, 100).Select(i => (double)i)];
        Assert.Equal(95, FrameLog.Percentile(hundred, 0.95));
    }

    [Fact]
    public void A_frame_longer_than_a_second_is_a_row_of_its_own()
    {
        var log = new FrameLog(default);
        var rows = Run(log, [16.0, 16.0, 3200.0, 16.0]);
        // The two short frames wait in the row the hitch then closes; nothing is lost and no second is skipped silently.
        var row = Assert.Single(rows);
        Assert.Equal(3, row.Frames);
        Assert.Equal(3200.0, row.WorstMs);
        Assert.Equal(3.232, row.Seconds, 6);
        Assert.Equal(1, log.End(() => Sample())!.Value.Frames);
    }

    [Fact]
    public void A_row_carries_what_was_done_in_it_not_the_running_totals()
    {
        var totals = new FrameLogTotals(CellsLoaded: 40, CellsUnloaded: 2, TextureLoads: 10, TextureShrinks: 1, UploadedBytes: 64 << 20,
                                        PipelinesMade: 3, TexturesMade: 12, Gen0: 100, Gen2: 4);
        var log = new FrameLog(totals);
        var first = Assert.Single(Run(log, Enumerable.Repeat(100.0, 10), () => Sample(totals with { CellsLoaded = 46, UploadedBytes = (64 << 20) + (8 << 20), Gen0 = 103 }, cells: 44, working: 2100.4, managed: 310.6)));
        Assert.Equal(new FrameLogTotals(6, 0, 0, 0, 8 << 20, 0, 0, 3, 0), first.Made);
        Assert.Equal(44, first.CellsIn);
        Assert.Equal(2100.4, first.WorkingSetMB);
        // The next row is counted from where the first ended.
        var second = Assert.Single(Run(log, Enumerable.Repeat(100.0, 10), () => Sample(totals with { CellsLoaded = 47, CellsUnloaded = 5, UploadedBytes = (64 << 20) + (8 << 20), Gen0 = 103, PipelinesMade = 5 })));
        Assert.Equal(new FrameLogTotals(1, 3, 0, 0, 0, 2, 0, 0, 0), second.Made);
    }

    [Fact]
    public void A_counter_that_began_again_counts_whole()
    {
        // A journey to another place builds a new renderer, whose counts start from nothing.
        var before = new FrameLogTotals(0, 0, 0, 0, 0, PipelinesMade: 9, TexturesMade: 200, Gen0: 50, Gen2: 2);
        var after = new FrameLogTotals(0, 0, 0, 0, 0, PipelinesMade: 4, TexturesMade: 31, Gen0: 55, Gen2: 2);
        Assert.Equal(new FrameLogTotals(0, 0, 0, 0, 0, 4, 31, 5, 0), after.Since(before));
    }

    [Fact]
    public void The_csv_line_matches_its_header_column_for_column_with_points_for_decimals()
    {
        var row = new FrameLogRow(Seconds: 12.5, Frames: 58, MeanMs: 17.241, P95Ms: 24.06, WorstMs: 131.5, WorkingSetMB: 2100.4, ManagedMB: 310.6, CellsIn: 44,
                                  Made: new FrameLogTotals(6, 1, 12, 2, UploadedBytes: 25 << 20, PipelinesMade: 2, TexturesMade: 7, Gen0: 3, Gen2: 1));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");   // a comma for the decimal mark
            Assert.Equal("12.50,58,17.24,24.06,131.50,2100,311,3,1,44,6,1,12,2,25.0,2,7,1", FrameLog.Line(row));
            Assert.EndsWith(",2,7,0", FrameLog.Line(row with { Focused = false }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        Assert.Equal("seconds,frames,mean_ms,p95_ms,worst_ms,working_set_mb,managed_mb,gc_gen0,gc_gen2," +
                     "cells_in,cells_loaded,cells_unloaded,texture_loads,texture_shrinks,uploaded_mb,pipelines_made,textures_made,focused", FrameLog.Header);
        Assert.Equal(FrameLog.Header.Split(',').Length, FrameLog.Line(row).Split(',').Length);
    }

    [Fact]
    public void A_row_says_whether_the_window_kept_the_keyboard_focus_all_through_it()
    {
        var log = new FrameLog(default);
        // A second of frames with the focus, a second in which one frame ended without it, and a second with it again.
        var rows = new List<FrameLogRow>();
        for (int i = 0; i < 300; i++)
            if (log.Frame(10.0, () => Sample(), focused: i != 150) is { } row) rows.Add(row);
        Assert.Equal([true, false, true], rows.Select(r => r.Focused));
        // A caller that says nothing of the window (the cloud's offscreen runs) has it throughout.
        Assert.All(Run(new FrameLog(default), Enumerable.Repeat(10.0, 250)), r => Assert.True(r.Focused));
    }

    [Fact]
    public void The_long_frame_that_ends_a_wait_without_the_focus_is_marked_and_the_rows_after_it_are_not()
    {
        // The game's loop stands still while its window is not the active one; the frame that ends the wait
        // is as long as the wait. A second walk had such a frame of 10.6 s that nothing explained.
        var log = new FrameLog(default);
        var rows = Run(log, Enumerable.Repeat(10.0, 150));          // a second and a half of play
        log.Away(); log.Away(); log.Away();                         // the loop waiting, 10 ms at a time
        if (log.Frame(10600.0, () => Sample(), focused: true) is { } gap) rows.Add(gap);   // the focus is back when the frame ends
        rows.AddRange(Run(log, Enumerable.Repeat(10.0, 100)));
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].Focused);
        Assert.False(rows[1].Focused);
        Assert.Equal(10600.0, rows[1].WorstMs);
        Assert.Equal(51, rows[1].Frames);                           // the half second before the wait and the frame that ended it
        Assert.True(rows[2].Focused);
        // The same long frame with the focus kept all along is a hitch, and says so.
        var hitch = new FrameLog(default);
        Assert.True(Assert.Single(Run(hitch, [10.0, 10600.0])).Focused);
    }

    [Fact]
    public void A_shorter_row_length_can_be_asked_for()
    {
        var log = new FrameLog(default, rowSeconds: 0.25);
        Assert.Equal(4, Run(log, Enumerable.Repeat(10.0, 100)).Count);
    }
}
