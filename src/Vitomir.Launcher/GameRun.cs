// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game started by Play as a process of its own and watched to its end.
using System.Diagnostics;
using System.Text;

namespace Vitomir.Launcher;

/// <summary>A game the launcher started: its process, its end, what it said last, the log the launcher keeps of it.</summary>
public interface IRunningGame
{
    /// <summary>Its process id (0 for a test's).</summary>
    int Id { get; }
    /// <summary>Its exit code once it has ended.</summary>
    Task<int> Ended { get; }
    /// <summary>The last lines it printed (its standard error's when it printed any there, else its standard output's).</summary>
    IReadOnlyList<string> LastLines { get; }
    /// <summary>The file every line it printed went to.</summary>
    string LogPath { get; }
}

/// <summary>
/// What Play starts: the program, its command line, where its lines are kept; <paramref name="Detached"/> when the launcher closes as the
/// game starts (its output then not read: the game keeps its own log, and shows its own box when it ends badly).
/// </summary>
public sealed record GameStart(string Program, IReadOnlyList<string> Args, string LogPath, bool Detached = false);

/// <summary>
/// **The game as a process of its own**: <c>vitomir-platform</c> beside the launcher, started with Play's command line
/// (<c>--game classic</c>), its standard output and error read by the launcher (so the game shows no box of its own when it ends badly:
/// a redirected game leaves its failures to whoever started it, <c>TextConsole.BoxesFailure</c>) and every line written to
/// <see cref="GameStart.LogPath"/> as it comes (<c>&lt;user data&gt;\Logs\game-&lt;when&gt;.log</c>: the platform's lines and the
/// module's, everything it printed). The last lines are kept for the launcher's failure notice. The environment is the launcher's
/// (<c>VITOMIR_USERDATA</c> with it). One game at a time.
/// </summary>
public sealed class ProcessGame : IRunningGame
{
    /// <summary>How many of its last lines are kept.</summary>
    public const int Kept = 12;

    readonly Process process;
    readonly Queue<string> last = new(), lastErrors = new();
    readonly StreamWriter? log;
    readonly object gate = new();
    readonly TaskCompletionSource<int> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    ProcessGame(Process process, StreamWriter? log, string logPath)
    {
        this.process = process;
        this.log = log;
        LogPath = logPath;
    }

    public int Id => process.Id;
    public Task<int> Ended => ended.Task;
    public string LogPath { get; }

    public IReadOnlyList<string> LastLines
    {
        get { lock (gate) return lastErrors.Count > 0 ? [.. lastErrors] : [.. last]; }
    }

    /// <summary>Starts it; throws when it cannot be started (the program missing, not allowed).</summary>
    public static ProcessGame Start(GameStart start)
    {
        var info = new ProcessStartInfo(start.Program)
        {
            UseShellExecute = false, RedirectStandardOutput = !start.Detached, RedirectStandardError = !start.Detached, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(start.Program)) ?? AppContext.BaseDirectory,
        };
        if (!start.Detached) { info.StandardOutputEncoding = new UTF8Encoding(false); info.StandardErrorEncoding = new UTF8Encoding(false); }
        foreach (var a in start.Args) info.ArgumentList.Add(a);
        if (start.Detached)
        {
            // The launcher closes as the game starts: nothing reads its output (a pipe whose reader is gone would fail the game's writes), so
            // it is not redirected; the game keeps its own log and says a failure in a box of its own.
            var alone = new Process { StartInfo = info };
            if (!alone.Start()) throw new InvalidOperationException("the process did not start");
            var detached = new ProcessGame(alone, null, start.LogPath);
            detached.ended.TrySetResult(0);
            return detached;
        }
        StreamWriter? log = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(start.LogPath))!);
            log = new StreamWriter(start.LogPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var game = new ProcessGame(process, log, start.LogPath);
        process.OutputDataReceived += (_, e) => game.Line(e.Data, error: false);
        process.ErrorDataReceived += (_, e) => game.Line(e.Data, error: true);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("the process did not start");
        }
        catch
        {
            log?.Dispose();
            process.Dispose();
            throw;
        }
        log?.WriteLine($"launcher: started {start.Program} {string.Join(' ', start.Args)} (process {process.Id})");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Its end once it has exited and both its streams are read to their ends.
        _ = process.WaitForExitAsync().ContinueWith(_ => game.Finish(), TaskScheduler.Default);
        return game;
    }

    void Line(string? line, bool error)
    {
        lock (gate)
        {
            if (line is null) return;
            try { log?.WriteLine(line); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            var into = error ? lastErrors : last;
            into.Enqueue(line);
            while (into.Count > Kept) into.Dequeue();
        }
    }

    void Finish()
    {
        int code = process.ExitCode;
        lock (gate)
        {
            try { log?.WriteLine($"launcher: the game ended, exit code {code}"); log?.Dispose(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        }
        process.Dispose();
        ended.TrySetResult(code);
    }
}

/// <summary>What the launcher asks of Windows' shell: a folder or a file opened, a command run in a console window the player sees.</summary>
public interface ILauncherShell
{
    /// <summary>Opens a folder in Explorer; answers null, or why it could not.</summary>
    string? OpenFolder(string folder);
    /// <summary>Opens a file with what opens it (a log: Notepad when nothing else does); answers null, or why it could not.</summary>
    string? OpenFile(string file);
    /// <summary>Opens a web address in the browser (About's source link); answers null, or why it could not.</summary>
    string? OpenLink(string url);
    /// <summary>Runs <paramref name="line"/> in a console window of its own that stays until a key (winget); answers its end, or throws.</summary>
    Task<int> RunVisible(string line);
}

/// <summary>Windows' shell (elsewhere, <c>xdg-open</c> for a folder or file and <c>sh</c> for a line, a developer's).</summary>
public sealed class WindowsShell : ILauncherShell
{
    public static readonly WindowsShell Instance = new();

    public string? OpenFolder(string folder)
    {
        if (!Directory.Exists(folder)) return $"{folder} is not there yet";
        return Open(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open", folder);
    }

    public string? OpenFile(string file)
    {
        if (!File.Exists(file)) return $"{file} is not there";
        if (!OperatingSystem.IsWindows()) return Open("xdg-open", file);
        // A .log has no program of its own on many Windows machines: Notepad, which every one has.
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return Open("notepad.exe", file); }
    }

    public string? OpenLink(string url)
    {
        if (!OperatingSystem.IsWindows()) return Open("xdg-open", url);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return $"the browser could not be started ({e.Message})"; }
    }

    static string? Open(string program, string what)
    {
        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            info.ArgumentList.Add(what);
            using var p = Process.Start(info);
            return p is null ? $"{program} did not start" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return $"{program} could not be started ({e.Message})"; }
    }

    public Task<int> RunVisible(string line)
    {
        // cmd /c "<line> & pause": the player sees winget's own words and the window stays until a key.
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { UseShellExecute = true, Arguments = $"/c \"{line} & echo. & pause\"" }
            : new ProcessStartInfo("sh") { UseShellExecute = false, ArgumentList = { "-c", line } };
        var p = Process.Start(info) ?? throw new InvalidOperationException("the console did not start");
        return p.WaitForExitAsync().ContinueWith(_ => { int code = p.ExitCode; p.Dispose(); return code; }, TaskScheduler.Default);
    }
}
