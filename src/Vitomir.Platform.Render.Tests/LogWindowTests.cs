// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **The log window as a process of its own** (<c>Windowing/LogWindow.cs</c>, <c>Windowing/LogViewer.cs</c>): what can run
/// on any OS. The line protocol (<see cref="LogWire"/>); the viewer's command (<see cref="LogViewer.Command"/>) and its reading of
/// a stream it is handed (<see cref="LogViewer.Pump"/>); the bounded queue that never stalls the game and drops and counts what it
/// cannot hold (<see cref="LogFeed"/>); and the whole round over a real named pipe with the viewer's run in a thread for its
/// process: the kept lines first, the live ones after, the viewer closed and opened again, the window closed by the game, the
/// game ending. The viewer's launch as a process and its console window are Windows' (tried there).
/// </summary>
public sealed class LogWindowTests
{
    [Fact]
    public void A_line_goes_down_the_pipe_as_it_is_and_a_control_character_never()
    {
        Assert.Equal("net: hosting on UDP 4994 \u2013 Vitomirův hostitel", LogWire.Encode("net: hosting on UDP 4994 \u2013 Vitomirův hostitel"));
        Assert.Equal("a\tb", LogWire.Encode("a\tb"));
        Assert.Equal("a b c  [31mred", LogWire.Encode("a\u0001b\rc \u001b[31mred"));
        Assert.Equal("del ", LogWire.Encode("del\u007f"));
        string longLine = new('x', LogWire.Longest + 50);
        string cut = LogWire.Encode(longLine);
        Assert.Equal(LogWire.Longest + 3, cut.Length);
        Assert.EndsWith("x...", cut);
        // A record of the game's own is told from any line of the log, which never starts with the mark once encoded.
        Assert.Equal(LogWire.Kind.Close, LogWire.Read(LogWire.Close));
        Assert.Equal(LogWire.Kind.Quiet, LogWire.Read(LogWire.Ping));
        Assert.Equal(LogWire.Kind.Quiet, LogWire.Read(LogWire.Mark + "a later record"));
        Assert.Equal(LogWire.Kind.Line, LogWire.Read(LogWire.Encode(LogWire.Close)));
        Assert.Equal(LogWire.Kind.Line, LogWire.Read(""));
        Assert.Equal("log window: 1 line not shown here (the window fell behind; the log has them all)", LogWire.Dropped(1));
        Assert.Equal("log window: 37 lines not shown here (the window fell behind; the log has them all)", LogWire.Dropped(37));
    }

    [Fact]
    public void The_viewer_is_the_same_program_with_the_viewer_s_switches()
    {
        var (file, args) = LogViewer.Command(@"C:\Vitomir\vitomir-platform.exe", @"C:\Vitomir\vitomir-platform.dll", "vitomir-log-4242-1", "Vitomir log: classic");
        Assert.Equal(@"C:\Vitomir\vitomir-platform.exe", file);
        Assert.Equal(["--log-viewer", "vitomir-log-4242-1", "--title", "Vitomir log: classic"], args);
        // A developer's `dotnet vitomir.dll`: the host, the assembly first.
        (file, args) = LogViewer.Command("/usr/local/dotnet/dotnet", "/w/bin/vitomir.dll", "vitomir-log-7-2", "Vitomir log: classic");
        Assert.Equal("/usr/local/dotnet/dotnet", file);
        Assert.Equal(["/w/bin/vitomir.dll", "--log-viewer", "vitomir-log-7-2", "--title", "Vitomir log: classic"], args);
        (file, args) = LogViewer.Command(@"C:\dotnet\dotnet.exe", null, "p", "t");
        Assert.Equal(["--log-viewer", "p", "--title", "t"], args);

        Assert.True(LogViewer.Asked(["--log-viewer", "p"]));
        Assert.False(LogViewer.Asked(["--game", "classic", "--log-viewer", "p"]));
        Assert.False(LogViewer.Asked([]));
        Assert.Equal("vitomir-log-1-1", LogViewer.PipeOf(["--log-viewer", @"\\.\pipe\vitomir-log-1-1"]));
        Assert.Equal("vitomir-log-1-1", LogViewer.PipeOf(["--log-viewer", "vitomir-log-1-1", "--title", "x"]));
        Assert.Null(LogViewer.PipeOf(["--log-viewer"]));
        Assert.Equal("Vitomir log: classic", LogViewer.TitleOf(["--log-viewer", "p", "--title", "Vitomir log: classic"]));
        Assert.Equal("Vitomir log", LogViewer.TitleOf(["--log-viewer", "p"]));
    }

    [Fact]
    public void The_viewer_writes_what_comes_down_the_pipe_and_tells_the_game_s_end_from_its_close()
    {
        var ended = Wire("one", LogWire.Ping, "two \u2013 dva", LogWire.Mark + "later", "three");
        var to = new StringWriter();
        Assert.Equal(LogViewer.End.GameEnded, LogViewer.Pump(ended, to));
        Assert.Equal(["one", "two \u2013 dva", "three"], LinesOf(to));

        var closed = Wire("one", LogWire.Close, "never shown");
        to = new StringWriter();
        Assert.Equal(LogViewer.End.ClosedByGame, LogViewer.Pump(closed, to));
        Assert.Equal(["one"], LinesOf(to));

        // A viewer with no pipe named, and one whose game is not there, say so and end.
        to = new StringWriter();
        Assert.Equal(2, LogViewer.Run(["--log-viewer"], to));
        Assert.StartsWith("log window: no pipe named", to.ToString());
        to = new StringWriter();
        var since = Stopwatch.StartNew();
        Assert.Equal(1, LogViewer.Run(["--log-viewer", $"vitomir-log-none-{Guid.NewGuid():N}", "--title", "t"], to, connectMs: 300));
        Assert.True(since.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains("could not be reached", to.ToString());
    }

    [Fact]
    public void A_stuck_viewer_never_stalls_the_game_and_the_lines_it_missed_are_counted_and_said()
    {
        var pipe = new StuckStream();
        var ends = new List<(LogFeed.End, long)>();
        var feed = new LogFeed(_ => pipe, (how, dropped) => { lock (ends) ends.Add((how, dropped)); }, capacity: 100);
        var since = Stopwatch.StartNew();
        for (int i = 0; i < 5000; i++) feed.WriteLine($"line {i}");
        var took = since.Elapsed;
        Assert.True(took < TimeSpan.FromSeconds(2), $"5,000 lines into a stuck window took {took.TotalMilliseconds:0} ms");
        Assert.True(feed.DroppedLines > 4000, $"{feed.DroppedLines} dropped");
        Assert.True(feed.Queued <= 100);
        long dropped = feed.DroppedLines;
        // The viewer comes back: what was queued reaches it, each gap said where it was.
        pipe.Release();
        Assert.True(SpinWait.SpinUntil(() => feed.Queued == 0, 10_000));
        for (int i = 5000; i < 5010; i++) feed.WriteLine($"line {i}");
        Assert.True(feed.Finish(closeWindow: false, TimeSpan.FromSeconds(10)));
        Assert.Equal([(LogFeed.End.GameEnded, dropped)], ends);
        var lines = LinesOf(new StringReader(Encoding.UTF8.GetString(pipe.Written)));
        Assert.Equal("line 0", lines[0]);
        Assert.Equal("line 5009", lines[^1]);
        Assert.Contains(lines, l => l.StartsWith("log window: ") && l.Contains("not shown here"));
        long said = lines.Where(l => l.StartsWith("log window: ") && l.Contains("not shown here")).Sum(l => long.Parse(l.Split(' ')[2]));
        Assert.Equal(dropped, said);
        Assert.Equal(5010 - dropped, lines.Count(l => l.StartsWith("line ")));
        // In order, whatever was dropped.
        var numbers = lines.Where(l => l.StartsWith("line ")).Select(l => int.Parse(l[5..])).ToList();
        Assert.Equal(numbers.OrderBy(n => n), numbers);
        Assert.False(feed.Add("after its end"));
    }

    [Fact]
    public void A_viewer_closed_while_the_game_is_quiet_is_found_within_a_ping()
    {
        var pipe = new ClosingStream();
        var ended = new ManualResetEventSlim();
        LogFeed.End? how = null;
        var feed = new LogFeed(_ => pipe, (h, _) => { how = h; ended.Set(); });
        feed.WriteLine("one");
        Assert.True(SpinWait.SpinUntil(() => pipe.Lines >= 1, 5000));
        pipe.Closed = true;
        Assert.True(ended.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the closed viewer was not found");
        Assert.Equal(LogFeed.End.ViewerClosed, how);
        Assert.True(feed.Over);
    }

    [Fact]
    public void The_window_s_round_over_a_named_pipe_kept_lines_first_closed_by_its_viewer_and_opened_again()
    {
        var lines = new ConsoleLines();
        for (int i = 0; i < ConsoleLines.Kept + 1000; i++) lines.Line($"before {i}");
        var said = new List<string>();
        var viewers = new List<ThreadViewer>();
        LogWindow.IViewer Start(string pipe, string title)
        {
            ThreadViewer v;
            lock (viewers) v = new ThreadViewer(pipe, title, closable: viewers.Count == 0);
            lock (viewers) viewers.Add(v);
            return v;
        }
        var (window, line) = LogWindow.Start(lines, "Vitomir log: classic", Start, s => { lock (said) said.Add(s); });
        Assert.NotNull(window);
        Assert.StartsWith("log window: opened (Vitomir log: classic), a viewer of its own (process ", line);
        Assert.Matches(@"^vitomir-log-\d+-\d+$", window!.Pipe);
        Assert.True(lines.Showing);
        lines.Line("live 1");
        lines.Line("live 2 \u2013 Vitomirův");
        var first = viewers[0];
        Assert.True(first.WaitFor("live 2 \u2013 Vitomirův"), "the live lines did not reach the viewer");
        var shown = first.Shown;
        // The last 5,000 kept first, in order, then the live ones.
        Assert.Equal("before 1000", shown[0]);
        Assert.Equal($"before {ConsoleLines.Kept + 999}", shown[ConsoleLines.Kept - 1]);
        Assert.Equal(["live 1", "live 2 \u2013 Vitomirův"], shown.Skip(ConsoleLines.Kept).ToArray());
        Assert.Equal("Vitomir log: classic", first.Title);

        // Its X: the viewer ends, the game says so within a ping and stops showing the lines; the game goes on.
        first.CloseByItsX();
        Assert.True(SpinWait.SpinUntil(() => !window.Open, 5000), "the game did not find its viewer closed");
        Assert.True(SpinWait.SpinUntil(() => { lock (said) return said.Contains("log window: closed"); }, 5000));
        Assert.False(lines.Showing);
        lines.Line("live 3");

        // Opened again: a new pipe, the kept lines again (live 3 among them), then live ones.
        var (again, _) = LogWindow.Start(lines, "Vitomir log: classic", Start, s => { lock (said) said.Add(s); });
        Assert.NotNull(again);
        Assert.NotEqual(window.Pipe, again!.Pipe);
        lines.Line("live 4");
        var second = viewers[1];
        Assert.True(second.WaitFor("live 4"));
        Assert.Contains("live 3", second.Shown);

        // Closed by the game (logwindow off): the viewer ends at once, nothing held, nothing said of the game's end.
        Assert.Equal("log window: closed", again.Close());
        Assert.True(second.Done.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, second.Exit);
        Assert.DoesNotContain(LogViewer.GameEnded, second.Shown);
        Assert.False(lines.Showing);

        // The game ending: the viewer has every line, then says the game has ended (and would wait for a key).
        var (third, _) = LogWindow.Start(lines, "Vitomir log: classic", Start, s => { lock (said) said.Add(s); });
        lines.Line("the last line");
        third!.GameEnding();
        var last = viewers[2];
        Assert.True(last.Done.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(["the last line", "", LogViewer.GameEnded], last.Shown.TakeLast(3).ToArray());
        lock (said) Assert.Single(said, s => s == "log window: closed");
    }

    // ---- the stand-ins -----------------------------------------------------------------------------------------------------

    static MemoryStream Wire(params string[] records) => new(Encoding.UTF8.GetBytes(string.Concat(records.Select(r => r + "\n"))));

    static List<string> LinesOf(StringWriter w) => LinesOf(new StringReader(w.ToString()));

    static List<string> LinesOf(TextReader r)
    {
        var all = new List<string>();
        while (r.ReadLine() is { } l) all.Add(l);
        return all;
    }

    /// <summary>
    /// A viewer in a thread for its process, what it shows kept: the viewer's own run (<see cref="LogViewer.Run"/>), or, to be
    /// closed by its X (<see cref="CloseByItsX"/>: the process gone, its end of the pipe with it), the pipe read as the run reads it.
    /// </summary>
    sealed class ThreadViewer : LogWindow.IViewer
    {
        readonly Screen to = new();
        readonly Thread thread;
        volatile NamedPipeClientStream? client;

        public ThreadViewer(string pipe, string title, bool closable)
        {
            Title = title;
            thread = new Thread(() =>
            {
                if (!closable) Exit = LogViewer.Run(["--log-viewer", pipe, "--title", title], to);
                else
                {
                    var c = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.CurrentUserOnly);
                    c.Connect(10_000);
                    client = c;
                    LogViewer.Pump(c, to);
                    Exit = 0;
                }
                Done.Set();
            }) { IsBackground = true };
            thread.Start();
        }

        public string Title { get; }
        public int Exit { get; private set; } = -1;
        public ManualResetEventSlim Done { get; } = new();
        public int Id => 1000 + thread.ManagedThreadId;
        public bool HasExited => Done.IsSet;
        public void Kill() => client?.Dispose();
        public List<string> Shown => to.Lines;

        public bool WaitFor(string line) => SpinWait.SpinUntil(() => Shown.Contains(line), 10_000);

        public void CloseByItsX()
        {
            Assert.True(SpinWait.SpinUntil(() => client is not null, 10_000));
            client!.Dispose();
        }

        sealed class Screen : TextWriter
        {
            readonly List<string> lines = [];
            readonly StringBuilder partial = new();
            public override Encoding Encoding => Encoding.UTF8;
            public List<string> Lines { get { lock (lines) return [.. lines]; } }

            public override void Write(char value)
            {
                lock (lines)
                {
                    if (value == '\n') { lines.Add(partial.ToString()); partial.Clear(); }
                    else if (value != '\r') partial.Append(value);
                }
            }
        }
    }

    /// <summary>A pipe whose reader is stuck until it is let go (a console window whose text is being selected).</summary>
    sealed class StuckStream : Stream
    {
        readonly ManualResetEventSlim go = new();
        readonly MemoryStream kept = new();
        public void Release() => go.Set();
        public byte[] Written { get { lock (kept) return kept.ToArray(); } }
        public override void Write(byte[] buffer, int offset, int count)
        {
            go.Wait();
            lock (kept) kept.Write(buffer, offset, count);
        }
        public override void Flush() => go.Wait();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>A pipe whose reader goes away (its window closed): every write after that is a broken pipe.</summary>
    sealed class ClosingStream : Stream
    {
        int lines;
        public volatile bool Closed;
        public int Lines => Volatile.Read(ref lines);
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Closed) throw new IOException("Pipe is broken.");
            for (int i = offset; i < offset + count; i++)
                if (buffer[i] == (byte)'\n') Interlocked.Increment(ref lines);
        }
        public override void Flush()
        {
            if (Closed) throw new IOException("Pipe is broken.");
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
