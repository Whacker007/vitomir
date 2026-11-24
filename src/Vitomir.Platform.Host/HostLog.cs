// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir.Platform.Host;

/// <summary>
/// The program's log: every line to the terminal that started it (if any) and to a file, by default a new
/// one per run in the player's <c>Logs</c> folder, or the one <c>--log-file</c> names.
/// </summary>
public sealed class HostLog : IDisposable
{
    readonly StreamWriter? file;
    readonly DateTime start = DateTime.UtcNow;

    public HostLog(string? path)
    {
        try
        {
            path ??= Path.Combine(UserData.Ensure(UserData.Logs), $"platform-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            file = new StreamWriter(path, append: false) { AutoFlush = true };
            LogPath = path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"log: {path} could not be opened ({e.Message}); the terminal only");
        }
    }

    public string? LogPath { get; }

    public void Say(string line)
    {
        var stamped = $"{(DateTime.UtcNow - start).TotalSeconds,8:0.000} {line}";
        saying = true;
        try { Console.WriteLine(line); }
        finally { saying = false; }
        lock (gate) file?.WriteLine(stamped);
    }

    readonly object gate = new();
    [ThreadStatic] static bool saying;

    /// <summary>Whether this thread is printing one of the log's own lines: a game in a window's printed lines go into the log, this one is there already.</summary>
    public static bool Saying => saying;

    /// <summary>
    /// A line a game in a window printed (such a game attaches to no console; <c>TextConsole.Lines</c>), the module's
    /// among them, into the log file. Always taken.
    /// </summary>
    public bool Printed(string line)
    {
        lock (gate) file?.WriteLine($"{(DateTime.UtcNow - start).TotalSeconds,8:0.000} {line}");
        return true;
    }

    public void Dispose()
    {
        lock (gate) file?.Dispose();
    }
}
