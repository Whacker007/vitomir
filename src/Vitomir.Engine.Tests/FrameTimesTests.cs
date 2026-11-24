// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **`--frame-times`: a row for every frame** (ours). The log is an instrument, so
/// what is pinned is that it can be trusted as one: every frame gets a row, the first of a run included; a row
/// holds the numbers <see cref="SlowFrames"/> measured, in the header's columns, written the same on any
/// machine's locale; one file serves a process's game loops in turn, each numbered; and writing a row
/// allocates nothing, so the log does not show up in its own allocation column.
/// </summary>
public class FrameTimesTests
{
    static string Temp() => Path.Combine(Path.GetTempPath(), $"vitomir-frametimes-{Guid.NewGuid():n}.tsv");

    static FrameTimes.Detail Detail(long sim, bool focused = true) =>
        new(sim, 0.25, 1.5, 2.5, 0, 0.75, 0.125, 7, 3, 51, 15, 2, 1, 4, 0.5, focused);

    [Fact]
    public void Every_frame_gets_a_row_in_the_header_s_columns_and_a_second_loop_appends_as_run_two()
    {
        string path = Temp();
        var said = new List<string>();
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");                  // a comma for the decimal point, if the log let it
            var slow = new SlowFrames("input", "draw", "submit");
            int first;
            using (var log = FrameTimes.Open(path, slow.Phases, said.Add)!)
            {
                first = log.Run;
                for (int frame = 1; frame <= 3; frame++)
                {
                    slow.Begin();
                    slow.Mark(0);
                    Thread.SpinWait(20_000);
                    slow.Mark(1);                                                    // the third phase is not marked: 0 in its column
                    slow.End(frame, "p1_arc3");
                    log.Frame(frame, "p1_arc3", slow, Detail(frame * 33));
                }
            }
            Assert.Equal($"frame times: a row a frame to {path}", Assert.Single(said));

            var lines = File.ReadAllLines(path);
            Assert.Equal(4, lines.Length);                                          // the header and a row a frame, the first frames too
            string[] head = lines[0].Split('\t');
            Assert.Equal(FrameTimes.Header(slow.Phases), lines[0]);
            Assert.Equal(["run", "frame", "place", "sim_ms", "frame_ms", "input", "draw", "submit", "rest", "step", "animated"], head.Take(11));
            Assert.Equal("focused", head[^1]);
            foreach (var (line, n) in lines.Skip(1).Select((l, i) => (l, i + 1)))
            {
                string[] cells = line.Split('\t');
                Assert.Equal(head.Length, cells.Length);
                var row = head.Zip(cells).ToDictionary(p => p.First, p => p.Second);
                double Number(string column) => double.Parse(row[column], CultureInfo.InvariantCulture);
                Assert.Equal((first.ToString(), n.ToString(), "p1_arc3", (n * 33).ToString()), (row["run"], row["frame"], row["place"], row["sim_ms"]));
                Assert.DoesNotContain(',', line);
                // The frame's time is its phases' and the rest, to the thousandth each is written to.
                Assert.InRange(Math.Abs(Number("frame_ms") - (Number("input") + Number("draw") + Number("submit") + Number("rest"))), 0, 0.005);
                Assert.True(Number("draw") > 0);
                Assert.Equal(0, Number("submit"));
                Assert.Equal((0.25, 1.5, 2.5, 0.75, 0.125), (Number("step"), Number("animated"), Number("shadows"), Number("world"), Number("interface")));
                Assert.Equal((7, 3, 51, 15), (Number("posed"), Number("passed"), Number("things"), Number("minds")));
                Assert.Equal((2, 1, 4, 0.5, 1), (Number("textures_made"), Number("buffers_rebuilt"), Number("files_read"), Number("read_ms"), Number("focused")));
                Assert.True(Number("alloc_kb") >= 0);
            }
            // What the slow-frame log keeps of the last frame is what was written.
            Assert.InRange(Math.Abs(slow.LastMs - (slow.LastPhase(0) + slow.LastPhase(1) + slow.LastPhase(2) + slow.LastRestMs)), 0, 0.001);   // each is a whole number of the clock's 100 ns
            Assert.Equal(0, slow.LastPhase(2));
            Assert.Equal(0, slow.LastPhase(9));                                     // no such phase

            // The game started again in the same process (a load, a journey): the same file, no second header, the next run.
            using (var again = FrameTimes.Open(path, slow.Phases)!)
            {
                Assert.Equal(first + 1, again.Run);
                slow.Begin();
                slow.End(1, "p1_otdy");
                again.Frame(1, "p1_otdy", slow, Detail(5, focused: false));
            }
            lines = File.ReadAllLines(path);
            Assert.Equal(5, lines.Length);
            Assert.Single(lines, l => l.StartsWith("run\t"));
            Assert.StartsWith($"{first + 1}\t1\tp1_otdy\t5\t", lines[4]);
            Assert.EndsWith("\t0", lines[4]);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
            File.Delete(path);
        }
    }

    [Fact]
    public void A_row_allocates_nothing_and_a_file_that_cannot_be_written_is_said_and_gives_no_log()
    {
        string path = Temp();
        try
        {
            var slow = new SlowFrames("input", "draw");
            using (var log = FrameTimes.Open(path, slow.Phases)!)
            {
                slow.Begin(); slow.Mark(0); slow.Mark(1); slow.End(1, "x");
                var detail = Detail(1);
                for (int i = 0; i < 100; i++) log.Frame(i, "a place with a\ttab in its name", slow, detail);   // warmed
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 5_000; i++) log.Frame(i, "p1_arc3", slow, detail);
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(5_101, lines.Length);
            Assert.All(lines, l => Assert.Equal(lines[0].Split('\t').Length, l.Split('\t').Length));   // the tab in a name did not make a column
        }
        finally { File.Delete(path); }

        var said = new List<string>();
        Assert.Null(FrameTimes.Open(null, ["a"], said.Add));
        Assert.Null(FrameTimes.Open("  ", ["a"], said.Add));
        Assert.Empty(said);                                                         // not asked for: nothing said
        string folder = Path.Combine(Path.GetTempPath(), $"vitomir-frametimes-{Guid.NewGuid():n}");
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(FrameTimes.Open(folder, ["a"], said.Add));                  // a folder is no file
            Assert.Contains("cannot be written", Assert.Single(said));
        }
        finally { Directory.Delete(folder, true); }
    }
}
