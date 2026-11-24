// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The network trace of the last session** (ours; item 6, the tester's log bundle): what the transport saw, a line
// an event and every ten seconds each connection's round trip and counts, written to the game's Logs folder as
// net-trace.log (the session before kept as net-trace.prev.log), for a friend to send with the bundle (LogBundle).
using System.Globalization;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The transport's trace**. Opened by the first transport a process makes once the program has named its
/// folder (<see cref="Folder"/>); each line is the wall clock's time and the line; never throws (a trace that cannot be
/// written is not written). Holds no message's contents and no key: knocks, answers, closes and why, the limits and the
/// drops, and the counts.
/// </summary>
public static class NetTrace
{
    /// <summary>The trace's file name in the Logs folder.</summary>
    public const string FileName = "net-trace.log", Previous = "net-trace.prev.log";

    /// <summary>Where the trace goes (the game's Logs folder), set by the program at its start; null writes none (a test).</summary>
    public static string? Folder { get; set; }

    static StreamWriter? writer;
    static readonly object gate = new();

    /// <summary>The trace's path, once it is open.</summary>
    public static string? Path { get; private set; }

    /// <summary>A line on the trace, opening it (the previous session's set aside) the first time.</summary>
    public static void Line(string line)
    {
        if (Folder is null) return;
        lock (gate)
        {
            try
            {
                if (writer is null)
                {
                    Directory.CreateDirectory(Folder);
                    string path = System.IO.Path.Combine(Folder, FileName), prev = System.IO.Path.Combine(Folder, Previous);
                    if (File.Exists(path)) File.Move(path, prev, overwrite: true);
                    writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                    Path = path;
                    writer.WriteLine($"# Vitomir's network trace, {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}; {NetIdentity.ProgramName}, build {NetIdentity.ThisBuild}");
                    if (NetSim.Default is { } sim) writer.WriteLine($"# the wire simulated: {sim}");
                }
                writer.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {line}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The trace closed (a test's, or the bundle's before it reads the file).</summary>
    public static void Close()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }
}
