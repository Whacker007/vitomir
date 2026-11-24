// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A game in a window's console lines: kept in its log, and shown in its log window when it has one.
using System.Text;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The lines a game in a window prints**: such a game attaches to no console, so what it writes to
/// <see cref="Console.Out"/> and <see cref="Console.Error"/> would go nowhere. Instead each whole line goes to the program's log
/// (<see cref="Log"/>: answers false when the log cannot take a line yet, and the line waits for the next), the last
/// <see cref="Kept"/> are kept for a log window opened later, and, while one is open (<see cref="Show"/>), each is written to it
/// as it comes. Safe from any thread. Nothing here is Windows' own: <see cref="TextConsole"/> puts it in place on Windows.
/// </summary>
public sealed class ConsoleLines
{
    /// <summary>How many lines are kept for a log window opened later, and how many wait for a log that cannot take them yet.</summary>
    public const int Kept = 5000;

    /// <summary>How many of the standard error's last lines are kept for a failure's message box.</summary>
    public const int ErrorsKept = 8;

    readonly object gate = new();
    readonly Queue<string> kept = new();
    readonly Queue<string> waiting = new();
    readonly Queue<string> errors = new();
    TextWriter? window;

    /// <summary>A line given to the log: answers whether the log took it (false: it waits and is given again first next time).</summary>
    public Func<string, bool>? Log { get; set; }

    /// <summary>What goes in place of <see cref="Console.Out"/>.</summary>
    public TextWriter Out { get; }

    /// <summary>What goes in place of <see cref="Console.Error"/>.</summary>
    public TextWriter Error { get; }

    public ConsoleLines(Func<string, bool>? log = null)
    {
        Log = log;
        Out = new Writer(this, error: false);
        Error = new Writer(this, error: true);
    }

    /// <summary>The lines kept, oldest first.</summary>
    public IReadOnlyList<string> Recent { get { lock (gate) return [.. kept]; } }

    /// <summary>The standard error's last lines, oldest first (a failure's message box says them).</summary>
    public IReadOnlyList<string> LastErrors { get { lock (gate) return [.. errors]; } }

    /// <summary>Whether a log window is showing the lines.</summary>
    public bool Showing { get { lock (gate) return window is not null; } }

    /// <summary>
    /// A log window to show the lines in (null: none any more): the lines kept written to it first, then each line as it comes.
    /// </summary>
    public void Show(TextWriter? to)
    {
        lock (gate)
        {
            window = to;
            if (to is null) return;
            foreach (var line in kept) Write(to, line);
        }
    }

    /// <summary>The lines no longer shown in <paramref name="which"/> (a log window that closed), if it is still the one shown.</summary>
    public void Hide(TextWriter which)
    {
        lock (gate)
            if (ReferenceEquals(window, which)) window = null;
    }

    /// <summary>
    /// Whether the line being written is in the log already (a log line the program also prints: the platform's own, the
    /// engine's console echo of its log), read on the writing thread before any lock is taken; such a line is kept and shown only.
    /// </summary>
    public Func<bool>? AlreadyLogged { get; set; }

    readonly object logGate = new();

    /// <summary>One whole line, as the writers hand it on.</summary>
    public void Line(string text, bool error = false)
    {
        bool toLog = Log is not null && !(AlreadyLogged?.Invoke() ?? false);
        lock (gate)
        {
            kept.Enqueue(text);
            while (kept.Count > Kept) kept.Dequeue();
            if (error)
            {
                errors.Enqueue(text);
                while (errors.Count > ErrorsKept) errors.Dequeue();
            }
            if (toLog)
            {
                waiting.Enqueue(text);
                while (waiting.Count > Kept) waiting.Dequeue();
            }
            if (window is { } to) Write(to, text);
        }
        // The log outside the lines' lock (it takes its own: a logger that also prints must not wait on this one while holding it).
        if (!toLog) return;
        lock (logGate)
        {
            while (Log is { } log)
            {
                string next;
                lock (gate)
                {
                    if (waiting.Count == 0) break;
                    next = waiting.Peek();
                }
                if (!log(next)) break;
                lock (gate) if (waiting.Count > 0) waiting.Dequeue();
            }
        }
    }

    // The log window is on the screen, and a stream may show it: its lines go through streamer mode; the log keeps them as they are.
    static void Write(TextWriter to, string line)
    {
        try { to.WriteLine(Vitomir.Platform.Presentation.StreamerMode.Shown(line)); } catch (IOException) { } catch (ObjectDisposedException) { }
    }

    /// <summary>A writer that hands each whole line on (a carriage return before the line's end dropped).</summary>
    sealed class Writer(ConsoleLines lines, bool error) : TextWriter
    {
        readonly StringBuilder partial = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (partial)
            {
                if (value == '\n')
                {
                    if (partial.Length > 0 && partial[^1] == '\r') partial.Length--;
                    string line = partial.ToString();
                    partial.Clear();
                    lines.Line(line, error);
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
                Write(value);
                Write('\n');
            }
        }

        public override void WriteLine() => Write('\n');
    }
}
