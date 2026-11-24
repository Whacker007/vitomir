// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The log window's viewer is the platform's program, started with <c>--log-viewer</c>, and touches nothing**
/// (<c>Windowing/LogViewer.cs</c>): the program, run as the game runs its viewer (<c>--log-viewer &lt;pipe&gt; --title "..."</c>)
/// against a pipe this test holds, writes the pipe's lines, ends at the game's close record at once, or at the pipe's end says
/// the game has ended; and its user folder (<c>VITOMIR_USERDATA</c>) and working folder stay empty: no log, no archive, no
/// settings. Here the output is redirected (the tests read it), so no console window is made: that and the key held at the end
/// are Windows' (tried there).
/// </summary>
public sealed class LogViewerProcessTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("vitomir-log-viewer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Here => AppContext.BaseDirectory;

    static string Platform => Path.Combine(Here, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    /// <summary>The program run as a viewer of a pipe this test makes and writes <paramref name="records"/> to; its exit code and output.</summary>
    (int Code, string Output) View(string program, params string[] records)
    {
        string pipe = $"vitomir-log-test-{Guid.NewGuid():N}";
        string user = Directory.CreateDirectory(Path.Combine(dir, "user")).FullName;
        using var server = new NamedPipeServerStream(pipe, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        foreach (var a in (string[])["--log-viewer", pipe, "--title", "Vitomir log: classic"]) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = user;
        using var p = new Process { StartInfo = start };
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEndAsync();
        try
        {
            if (!server.WaitForConnectionAsync(TestContext.Current.CancellationToken).Wait(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken))
            {
                // What the viewer said, so a failure names its cause (once on a Windows PC, under the whole suite's load).
                bool ended = p.WaitForExit(5000);
                if (!ended) p.Kill();
                p.WaitForExit();
                lock (err) Assert.Fail($"the viewer never connected ({(ended ? $"it ended, exit code {p.ExitCode}" : "still running after 90 s")}): {output.Result}{err}");
            }
            var bytes = new UTF8Encoding(false).GetBytes(string.Concat(records.Select(r => r + "\n")));
            server.Write(bytes);
            server.Flush();
        }
        finally { server.Dispose(); }
        if (!p.WaitForExit(60_000))
        {
            p.Kill();
            Assert.Fail("the viewer did not end");
        }
        p.WaitForExit();
        Assert.Empty(Directory.GetFileSystemEntries(user, "*", SearchOption.AllDirectories));
        Assert.Equal([user], Directory.GetFileSystemEntries(dir));
        lock (err) return (p.ExitCode, output.Result + err);
    }

    static string[] Lines(string output) => output.Replace("\r", "").TrimEnd('\n').Split('\n');

    [Fact]
    public void The_platform_s_viewer_shows_the_lines_says_the_game_s_end_and_writes_nothing()
    {
        var (code, output) = View(Platform, "net: hosting on UDP 4994", LogWire.Ping, "second line");
        Assert.Equal(0, code);
        Assert.Equal(["net: hosting on UDP 4994", "second line", "", LogViewer.GameEnded], Lines(output));
    }

    [Fact]
    public void The_platform_s_viewer_closed_by_the_game_ends_at_once_and_writes_nothing()
    {
        var (code, output) = View(Platform, "one line", LogWire.Close, "never shown");
        Assert.Equal(0, code);
        Assert.Equal(["one line"], Lines(output));
    }
}
