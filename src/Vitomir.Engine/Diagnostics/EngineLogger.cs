// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sinks. Microsoft's ILogger is the API — it is the one every C# tool already understands, and
// its source generator gives allocation-free logging when a level is off — but the implementation
// behind it is ours, so the engine carries no dependency-injection or configuration machinery and
// the log format is one we choose.
using System.Text;
using Microsoft.Extensions.Logging;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// Which threshold a sink follows. The console and the log file are deliberately different: the
/// console says as little as the player asked for, while the file keeps a basic narrative of the
/// run whatever the console is doing, so that a crash has something to look at afterwards.
/// </summary>
public enum SinkChannel { Console, File, Everything }

/// <summary>Somewhere a formatted line goes.</summary>
public interface ILogSink : IDisposable
{
    SinkChannel Channel { get; }
    void Write(LogLevel level, string category, string message, Exception? error);
}

/// <summary>
/// An <see cref="ILoggerFactory"/> over a fixed set of sinks and one <see cref="DiagnosticSettings"/>.
/// Changing the settings takes effect at once: the level test is a dictionary lookup on the
/// category, cached per logger.
/// </summary>
public sealed class EngineLoggerFactory(DiagnosticSettings settings, params ILogSink[] sinks) : ILoggerFactory
{
    readonly List<ILogSink> sinks = [.. sinks];
    readonly Dictionary<string, EngineLogger> loggers = new(StringComparer.Ordinal);
    bool disposed;

    public DiagnosticSettings Settings { get; } = settings;
    /// <summary>Every line written, when something is collecting them (the tests are).</summary>
    public IReadOnlyList<ILogSink> Sinks => sinks;

    public ILogger CreateLogger(string categoryName)
    {
        lock (loggers)
        {
            if (!loggers.TryGetValue(categoryName, out var logger))
                loggers[categoryName] = logger = new EngineLogger(this, categoryName);
            return logger;
        }
    }

    /// <summary>**Ours**: one more sink on the running factory, until <see cref="Remove"/> (<see cref="Diag.Listen"/>).</summary>
    internal void Add(ILogSink sink) { lock (loggers) if (!disposed) sinks.Add(sink); }

    /// <summary>The sink <see cref="Add"/> put on, taken off; it is not disposed.</summary>
    internal void Remove(ILogSink sink) { lock (loggers) sinks.Remove(sink); }

    /// <summary>Not used: the engine installs its sinks itself rather than through a provider list.</summary>
    public void AddProvider(ILoggerProvider provider) { }

    internal void Write(LogLevel level, string category, string message, Exception? error)
    {
        lock (loggers)
        {
            if (disposed) return;
            foreach (var sink in sinks)
                if (level >= Settings.LevelFor(category, sink.Channel))
                    sink.Write(level, category, message, error);
        }
    }

    /// <summary>
    /// Closes the sinks. **Under the same lock <see cref="Write"/> holds**, because
    /// <see cref="Diag"/> is a global and whoever replaces it may be doing so while something else
    /// is part-way through a line — a test class in another thread, or a worker still logging while
    /// the game shuts down. Without the lock that is a disposed stream being written to, which is
    /// how it announced itself: a passing test suite failing in the *logger* rather than in
    /// anything it was testing.
    /// </summary>
    /// <summary>
    /// A plain line to the file sinks alone (<see cref="Diag.Plain"/>), never the console's: answers false when there is no
    /// file to take it.
    /// </summary>
    internal bool WritePlain(string category, string line)
    {
        lock (loggers)
        {
            if (disposed) return false;
            bool any = false;
            foreach (var sink in sinks)
                if (sink.Channel == SinkChannel.File)
                {
                    sink.Write(LogLevel.Information, category, line, null);
                    any = true;
                }
            return any;
        }
    }

    public void Dispose()
    {
        List<ILogSink> closing;
        lock (loggers)
        {
            if (disposed) return;
            disposed = true;
            closing = [.. sinks];
            sinks.Clear();
        }
        foreach (var sink in closing) sink.Dispose();
    }
}

sealed class EngineLogger(EngineLoggerFactory factory, string category) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    // The most interested sink decides whether a line is worth building at all; each one then
    // filters again on its own threshold.
    public bool IsEnabled(LogLevel level) =>
        level != LogLevel.None && level >= factory.Settings.LowestLevelFor(category);

    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                            Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        factory.Write(level, category, formatter(state, error), error);
    }

    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// The console sink. Warnings and worse go to standard error so that piping the viewer's output
/// somewhere still shows problems; everything else goes to standard output.
/// </summary>
public sealed class ConsoleLogSink : ILogSink
{
    public SinkChannel Channel => SinkChannel.Console;

    public void Write(LogLevel level, string category, string message, Exception? error)
    {
        var writer = level >= LogLevel.Warning ? Console.Error : Console.Out;
        writing = true;
        try { writer.WriteLine(Format(level, category, message, error)); }
        finally { writing = false; }
    }

    [ThreadStatic] static bool writing;

    /// <summary>
    /// Whether this thread is printing a log line: a game in a window's console lines go into its log
    /// (<see cref="Diag.Plain"/>), and this echo of one is there already.
    /// </summary>
    public static bool Writing => writing;

    public void Dispose() { }

    /// <summary>
    /// <c>warn  Vitomir.World  the chronicle names a scene that is not installed</c> — the level
    /// first so a scan down the left margin finds the problems, then who said it.
    /// </summary>
    public static string Format(LogLevel level, string category, string message, Exception? error)
    {
        var sb = new StringBuilder();
        sb.Append(Short(level).PadRight(5)).Append("  ").Append(Trim(category)).Append("  ").Append(message);
        if (error is not null) sb.Append(" — ").Append(error.GetType().Name).Append(": ").Append(error.Message);
        return sb.ToString();
    }

    /// <summary>The five-character level tags the log lines up on.</summary>
    public static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "FATAL",
        _ => "none",
    };

    /// <summary>Category names all start with "Vitomir."; the prefix is noise in every line.</summary>
    public static string Trim(string category) =>
        category.StartsWith("Vitomir.", StringComparison.Ordinal) ? category["Vitomir.".Length..] : category;
}

/// <summary>
/// The file sink, which exists only when the player asks for one: the engine writes nothing to disk
/// by default. Lines carry a timestamp, which the console's do not.
/// </summary>
public sealed class FileLogSink : ILogSink
{
    StreamWriter? writer;

    public SinkChannel Channel => SinkChannel.File;

    public string Path { get; }
    /// <summary>Why the file could not be opened, when it could not.</summary>
    public string? Error { get; }

    /// <summary>The header line an archive reads back to date the run it came from.</summary>
    public const string LaunchedAt = "launched";

    public FileLogSink(string path, IEnumerable<string>? header = null)
    {
        Path = path;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Flushed on every line: the whole point of the file is to survive whatever happens next.
            writer = new StreamWriter(path, append: false) { AutoFlush = true };
            foreach (var line in header ?? []) writer.WriteLine("# " + line);
            writer.WriteLine($"# {LaunchedAt} {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Error = e.Message;
        }
    }

    /// <summary>
    /// **A log sink does not throw.** Whatever has gone wrong with the file — the disk filled, the
    /// handle was closed underneath us — the one thing that must not happen is a diagnostic turning
    /// into a crash in the middle of reporting something else. So a write that fails closes the
    /// file and the sink goes quiet for the rest of the run.
    /// </summary>
    public void Write(LogLevel level, string category, string message, Exception? error)
    {
        if (writer is not { } open) return;
        try
        {
            open.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {ConsoleLogSink.Format(level, category, message, error)}");
            if (error is not null) open.WriteLine(error.StackTrace);
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException or NotSupportedException)
        {
            writer = null;
        }
    }

    public void Dispose()
    {
        var closing = writer;
        writer = null;
        closing?.Dispose();
    }
}

/// <summary>Keeps every line in memory, which is how the tests read the log back.</summary>
public sealed class MemoryLogSink(SinkChannel channel = SinkChannel.Everything) : ILogSink
{
    public SinkChannel Channel { get; } = channel;
    public List<(LogLevel Level, string Category, string Message)> Lines { get; } = [];

    public void Write(LogLevel level, string category, string message, Exception? error) =>
        Lines.Add((level, category, error is null ? message : $"{message} — {error.GetType().Name}"));

    public void Dispose() { }

    public IEnumerable<string> At(LogLevel level) => Lines.Where(l => l.Level == level).Select(l => l.Message);
}
