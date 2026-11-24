// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A game's log window as a process of its own: the game's side. The game starts a viewer (the same program with
// --log-viewer, LogViewer.cs) and writes its lines to it over a named pipe, from a bounded queue on a writer thread of its own, so
// a slow or stuck viewer never stalls the game and the viewer's X (or its Windows Terminal tab) ends only the viewer.
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **What goes down the log window's pipe**: UTF-8, one record a line, each ended by <c>\n</c>. A record is a line of the
/// log as it is (<see cref="Encode"/>: no control character but a tab, at most <see cref="Longest"/> characters), or one of the
/// game's own records, which start with <see cref="Mark"/> (a character no line of the log carries once encoded):
/// <see cref="Close"/> (the game closed the window: the viewer ends at once, nothing held) and <see cref="Ping"/> (sent when the
/// game has had nothing to say for a second, so a viewer closed meanwhile is found). Anything else after the mark is ignored.
/// The pipe ending without <see cref="Close"/> is the game ending (or crashing): the viewer says so and waits for a key.
/// </summary>
public static class LogWire
{
    /// <summary>What starts a record of the game's own.</summary>
    public const char Mark = '\u0001';

    /// <summary>The game closed the window (<c>logwindow off</c>): the viewer ends at once.</summary>
    public const string Close = "\u0001close";

    /// <summary>Nothing new; the viewer shows nothing.</summary>
    public const string Ping = "\u0001ping";

    /// <summary>The longest line sent (characters); a longer one is cut and ends in <c>...</c>.</summary>
    public const int Longest = 8192;

    /// <summary>What a record is.</summary>
    public enum Kind
    {
        /// <summary>A line of the log, to show.</summary>
        Line,
        /// <summary>The game closed the window.</summary>
        Close,
        /// <summary>One of the game's own records that shows nothing (<see cref="Ping"/>, or one this viewer does not know).</summary>
        Quiet,
    }

    /// <summary>A line of the log as it goes down the pipe: a control character but a tab made a space, a line too long cut.</summary>
    public static string Encode(string line)
    {
        bool clean = line.Length <= Longest;
        if (clean)
            foreach (char c in line)
                if (c < ' ' && c != '\t' || c == '\u007f') { clean = false; break; }
        if (clean) return line;
        var o = new StringBuilder(Math.Min(line.Length, Longest + 3));
        foreach (char c in line)
        {
            if (o.Length >= Longest) { o.Append("..."); break; }
            o.Append(c < ' ' && c != '\t' || c == '\u007f' ? ' ' : c);
        }
        return o.ToString();
    }

    /// <summary>What a record read off the pipe is.</summary>
    public static Kind Read(string record) =>
        record.Length == 0 || record[0] != Mark ? Kind.Line
        : record == Close ? Kind.Close
        : Kind.Quiet;

    /// <summary>The line a window that fell behind is shown where lines were dropped (the log has them all).</summary>
    public static string Dropped(long count) =>
        $"log window: {count} {(count == 1 ? "line" : "lines")} not shown here (the window fell behind; the log has them all)";
}

/// <summary>
/// **The log window's feed**: what the game writes to its log window, held in a **bounded queue**
/// (<see cref="Capacity"/> lines) and written to the pipe by a thread of its own, so a viewer that is slow, stuck (a console
/// window whose text is being selected stops its writes) or gone never stalls the game: a line that finds the queue full is
/// dropped and counted (<see cref="DroppedLines"/>), and the window is told how many where they were missed
/// (<see cref="LogWire.Dropped"/>). The thread first waits for the viewer to connect (<c>connect</c>, which answers the pipe's
/// stream or null), then writes; when nothing has come for <see cref="PingEvery"/> it sends <see cref="LogWire.Ping"/>, so a
/// viewer closed meanwhile is found within that time. How it ended is told once (<see cref="Ended"/>). Safe from any thread;
/// nothing here is Windows' own.
/// </summary>
public sealed class LogFeed : TextWriter
{
    /// <summary>The queue's size: room for the 5,000 lines kept (<see cref="ConsoleLines.Kept"/>) sent first, and as many again.</summary>
    public const int Capacity = 10_000;

    /// <summary>How long the writer waits with nothing to send before it sends <see cref="LogWire.Ping"/>.</summary>
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(1);

    /// <summary>How a feed ended.</summary>
    public enum End
    {
        /// <summary>The viewer never connected (it could not start, or it ended first, or it took too long).</summary>
        NeverConnected,
        /// <summary>The pipe broke: the viewer was closed (its X, Ctrl+C, its tab) or killed.</summary>
        ViewerClosed,
        /// <summary>The game closed the window (<see cref="Finish"/> with the close record).</summary>
        ClosedByGame,
        /// <summary>The game ended: everything queued written and the pipe closed (the viewer says so and waits for a key).</summary>
        GameEnded,
    }

    readonly object gate = new();
    readonly Queue<(string Line, long DroppedBefore)> queue = new();
    readonly int capacity;
    readonly Func<Func<bool>, Stream?> connect;
    readonly StringBuilder partial = new();
    readonly Thread writer;
    long dropped, pendingDrops, sent;
    bool finishing, closeRecord, over;
    End? how;
    Stream? stream;

    /// <param name="connect">Waits for the viewer (on the feed's thread) and answers the pipe's stream, or null when it never came;
    /// it is given a test of whether the feed is being finished meanwhile.</param>
    /// <param name="ended">Told once, on the feed's thread, how the feed ended and how many lines were dropped in all.</param>
    /// <param name="capacity">The queue's size (a test's smaller one).</param>
    public LogFeed(Func<Func<bool>, Stream?> connect, Action<End, long>? ended = null, int capacity = Capacity)
    {
        this.connect = connect;
        this.capacity = capacity;
        Ended = ended;
        writer = new Thread(Run) { IsBackground = true, Name = "log window feed" };
        writer.Start();
    }

    /// <summary>Told once how the feed ended (see the constructor).</summary>
    public Action<End, long>? Ended { get; }

    /// <summary>How the feed ended, or null while it runs.</summary>
    public End? How { get { lock (gate) return how; } }

    /// <summary>Whether the feed has ended (or is being finished): it takes no more lines.</summary>
    public bool Over { get { lock (gate) return over || finishing; } }

    /// <summary>How many lines were dropped because the queue was full.</summary>
    public long DroppedLines { get { lock (gate) return dropped; } }

    /// <summary>How many lines were written to the pipe.</summary>
    public long SentLines { get { lock (gate) return sent; } }

    /// <summary>How many lines wait in the queue.</summary>
    public int Queued { get { lock (gate) return queue.Count; } }

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>
    /// **One line for the window**, never waiting: queued, or dropped and counted when the queue is full; nothing once the feed is
    /// over. Answers whether it was queued.
    /// </summary>
    public bool Add(string line)
    {
        lock (gate)
        {
            if (over || finishing) return false;
            if (queue.Count >= capacity)
            {
                dropped++;
                pendingDrops++;
                return false;
            }
            queue.Enqueue((line, pendingDrops));
            pendingDrops = 0;
            Monitor.Pulse(gate);
            return true;
        }
    }

    public override void Write(char value)
    {
        lock (partial)
        {
            if (value == '\n')
            {
                if (partial.Length > 0 && partial[^1] == '\r') partial.Length--;
                Add(partial.ToString());
                partial.Clear();
            }
            else partial.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        lock (partial) foreach (char c in value) Write(c);
    }

    public override void WriteLine(string? value)
    {
        lock (partial)
        {
            if (partial.Length == 0)
            {
                Add(value ?? "");
                return;
            }
            Write(value);
            Write('\n');
        }
    }

    public override void WriteLine() => Write('\n');

    /// <summary>
    /// **The feed finished**: no more lines taken, what is queued written, then the close record when <paramref name="closeWindow"/>
    /// (the game closed the window) or nothing (the game is ending: the viewer says so), and the pipe closed. Waits at most
    /// <paramref name="wait"/> for the writer; answers whether it finished in that time (a stuck viewer may hold it: the caller
    /// then ends the viewer, or lets the process's end close the pipe).
    /// </summary>
    public bool Finish(bool closeWindow, TimeSpan wait)
    {
        lock (gate)
        {
            if (!over && !finishing)
            {
                finishing = true;
                closeRecord = closeWindow;
            }
            Monitor.PulseAll(gate);
        }
        return writer.Join(wait);
    }

    void Run()
    {
        Stream? s = null;
        try { s = connect(() => Over); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException) { }
        if (s is null)
        {
            Stop(End.NeverConnected);
            return;
        }
        lock (gate) stream = s;
        var w = new StreamWriter(s, new UTF8Encoding(false), 16 * 1024) { AutoFlush = false, NewLine = "\n" };
        var batch = new List<(string Line, long DroppedBefore)>(256);
        try
        {
            while (true)
            {
                bool last, record;
                long tailDrops;
                batch.Clear();
                lock (gate)
                {
                    if (queue.Count == 0 && !finishing) Monitor.Wait(gate, PingEvery);
                    while (queue.Count > 0 && batch.Count < 256) batch.Add(queue.Dequeue());
                    last = finishing && queue.Count == 0;
                    record = closeRecord;
                    tailDrops = last ? pendingDrops : 0;
                }
                if (batch.Count == 0 && !last) w.Write(LogWire.Ping + "\n");
                foreach (var (line, before) in batch)
                {
                    if (before > 0) w.Write(LogWire.Encode(LogWire.Dropped(before)) + "\n");
                    w.Write(LogWire.Encode(line) + "\n");
                }
                if (last)
                {
                    if (tailDrops > 0) w.Write(LogWire.Encode(LogWire.Dropped(tailDrops)) + "\n");
                    if (record) w.Write(LogWire.Close + "\n");
                }
                w.Flush();
                lock (gate) sent += batch.Count;
                if (last)
                {
                    Stop(record ? End.ClosedByGame : End.GameEnded);
                    return;
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { Stop(End.ViewerClosed); }
    }

    void Stop(End end)
    {
        Stream? s;
        long total;
        lock (gate)
        {
            if (how is not null) return;
            how = end;
            over = true;
            queue.Clear();
            s = stream;
            stream = null;
            total = dropped;
            Monitor.PulseAll(gate);
        }
        try { s?.Dispose(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        try { Ended?.Invoke(end, total); }
        catch (Exception e) when (e is not OutOfMemoryException) { }
    }
}

/// <summary>
/// **A game's log window** (<i>"Have the game with it's own log window. It could be like a developer
/// option or toggle. Other games have this"</i>), the game's side: a named pipe of the current user's alone
/// (<c>\\.\pipe\vitomir-log-&lt;pid&gt;-&lt;n&gt;</c>, <see cref="PipeName"/>), **a viewer started as a process of its own**
/// (the same program with <c>--log-viewer &lt;pipe&gt; --title "&lt;title&gt;"</c>: <see cref="LogViewer"/>), and the game's
/// lines (<see cref="ConsoleLines"/>: the last 5,000 kept first, then each as it comes) fed to it (<see cref="LogFeed"/>).
/// Closing the viewer (its X, Ctrl+C, its Windows Terminal tab) ends the viewer alone; the game finds the pipe broken, says
/// <c>log window: closed</c> and stops writing to it, and can open another. <see cref="TextConsole"/> keeps the one a game has.
/// </summary>
public sealed class LogWindow
{
    /// <summary>A viewer the window started (a process of its own, or a test's stand-in).</summary>
    public interface IViewer
    {
        /// <summary>Its process id.</summary>
        int Id { get; }
        /// <summary>Whether it has ended.</summary>
        bool HasExited { get; }
        /// <summary>Ends it (a viewer stuck when the game closes the window).</summary>
        void Kill();
    }

    /// <summary>How long the viewer has to connect before the window is given up.</summary>
    public static readonly TimeSpan ConnectWithin = TimeSpan.FromSeconds(20);

    /// <summary>How long the game waits for its lines to reach the viewer when it closes the window or ends.</summary>
    public static readonly TimeSpan DrainWithin = TimeSpan.FromSeconds(2);

    static int count;

    /// <summary>The pipe's name for a process's n-th window (<c>\\.\pipe\</c> before it on Windows).</summary>
    public static string PipeName(int pid, int n) => $"vitomir-log-{pid}-{n}";

    readonly ConsoleLines lines;
    readonly Action<string> say;
    readonly IViewer viewer;

    /// <summary>The pipe's name.</summary>
    public string Pipe { get; }

    /// <summary>The window's title.</summary>
    public string Title { get; }

    /// <summary>The feed.</summary>
    public LogFeed Feed { get; }

    /// <summary>The viewer.</summary>
    public IViewer Viewer => viewer;

    /// <summary>Whether the window is still open: its feed runs.</summary>
    public bool Open => !Feed.Over;

    readonly NamedPipeServerStream server;

    /// <summary>
    /// **A log window opened**: the pipe made, the viewer started by <paramref name="start"/> (given the pipe's name and the title;
    /// it answers the viewer, or throws), the lines kept written to the feed and the lines to come after them. Answers the window
    /// (null when it could not be made) and the line saying what was done. <paramref name="say"/> is given, on the feed's thread,
    /// the lines said when the window closes by itself (<c>log window: closed</c>, and how many lines it missed).
    /// </summary>
    public static (LogWindow? Window, string Line) Start(ConsoleLines lines, string title, Func<string, string, IViewer> start, Action<string> say)
    {
        NamedPipeServerStream server;
        string pipe = PipeName(Environment.ProcessId, Interlocked.Increment(ref count));
        try
        {
            server = new NamedPipeServerStream(pipe, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                                               PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return (null, $"log window: not opened (its pipe could not be made: {e.GetType().Name}: {e.Message}; the log has the lines)");
        }
        IViewer viewer;
        try { viewer = start(pipe, title); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            server.Dispose();
            return (null, $"log window: not opened (its viewer could not be started: {e.GetType().Name}: {e.Message}; the log has the lines)");
        }
        var window = new LogWindow(lines, title, say, pipe, server, viewer);
        lines.Show(window.Feed);
        // A viewer that ended at once (its feed over before it was shown): let go again.
        if (window.Feed.Over) lines.Hide(window.Feed);
        return (window, $"log window: opened ({title}), a viewer of its own (process {viewer.Id}): closing it ends the viewer, never the game");
    }

    LogWindow(ConsoleLines lines, string title, Action<string> say, string pipe, NamedPipeServerStream server, IViewer viewer)
    {
        this.lines = lines;
        this.say = say;
        this.server = server;
        this.viewer = viewer;
        Title = title;
        Pipe = pipe;
        Feed = new LogFeed(Connect, Ended);
    }

    /// <summary>The feed's thread waits for the viewer: answers the pipe once it is connected, or null (the viewer gone, too long, closed meanwhile).</summary>
    Stream? Connect(Func<bool> finishing)
    {
        var s = server;
        using var cancel = new CancellationTokenSource();
        var waiting = s.WaitForConnectionAsync(cancel.Token);
        var since = Stopwatch.StartNew();
        try
        {
            while (!waiting.Wait(100))
            {
                if (finishing() || viewer.HasExited || since.Elapsed > ConnectWithin)
                {
                    cancel.Cancel();
                    try { waiting.Wait(1000); } catch (AggregateException) { }
                    s.Dispose();
                    return null;
                }
            }
            return s;
        }
        catch (AggregateException)
        {
            s.Dispose();
            return null;
        }
    }

    void Ended(LogFeed.End how, long dropped)
    {
        lines.Hide(Feed);
        if (how == LogFeed.End.ViewerClosed) say("log window: closed");
        else if (how == LogFeed.End.NeverConnected) say("log window: closed (its viewer never connected; the log has the lines)");
        if (dropped > 0) say($"log window: {dropped} {(dropped == 1 ? "line was" : "lines were")} not shown in it (the window fell behind; the log has them all)");
    }

    /// <summary>
    /// **The window closed by the game** (<c>logwindow off</c>): the lines stop, what is queued is written, the viewer told to end
    /// (it ends at once, nothing held) and, if it is stuck, ended. Answers <c>log window: closed</c>.
    /// </summary>
    public string Close()
    {
        lines.Hide(Feed);
        if (!Feed.Finish(closeWindow: true, DrainWithin))
            try { viewer.Kill(); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return "log window: closed";
    }

    /// <summary>
    /// **The game is ending** (or crashing): the lines queued written (at most <see cref="DrainWithin"/>) and the pipe closed, so the
    /// viewer says the game has ended and waits for a key.
    /// </summary>
    public void GameEnding() => Feed.Finish(closeWindow: false, DrainWithin);

    /// <summary>The viewer as a process of its own (<see cref="LogViewer.Command"/>): the same program, with no window of its own until it makes one.</summary>
    public static IViewer StartProcess(string pipe, string title)
    {
        var (file, args) = LogViewer.Command(Environment.ProcessPath ?? throw new InvalidOperationException("the program's path is not known"),
                                             System.Reflection.Assembly.GetEntryAssembly()?.Location, pipe, title);
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start answered nothing");
        return new ProcessViewer(process);
    }

    sealed class ProcessViewer(Process process) : IViewer
    {
        public int Id => process.Id;
        public bool HasExited { get { try { return process.HasExited; } catch (InvalidOperationException) { return true; } } }
        public void Kill() => process.Kill();
    }
}
