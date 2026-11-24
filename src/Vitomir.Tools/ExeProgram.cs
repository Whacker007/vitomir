// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Moved from the old program's top-level statements at 31f36b7, word for word: the body of vitomir-tools' process
// (and of vitomir-watchlog's).
using Vitomir.Platform.Render.Windowing;

namespace Vitomir;

/// <summary>
/// **A process of 1.1's frame controller on this program's SDL host, with the tools** (the old program's top-level statements
/// once): its console, the folder of replays, then the process's runs (<c>ClassicProgram</c>,
/// the library's) with the SDL host (<see cref="SdlClassicHost"/>: the window and its modes, the GPU, the events, the clock, the
/// pointer, the sound device) and the tools (<see cref="ExeTools"/>: the commands, reports and sweeps that run instead of a game).
/// The pushed frame: the host steps the runs a frame at a time, as vitomir-platform's does (SdlHost.Frames, the platform's
/// frame loop: the host's waits, the input gathered and handed in), and is closed when they are over. <c>vitomir-tools</c> runs
/// the command lines that name a tool through it (<see cref="ToolsProgram"/>).
/// </summary>
public static class ExeProgram
{
    /// <summary>
    /// The process with the tools and no first start of an unzipped game, its console what the run is: for a program that
    /// hosts the runs with additions of its own (<see cref="RunAdditions"/>), made before it is called.
    /// </summary>
    public static int Run(string[] args) => Run(args, TextRuns.WantedFor(args, platform: false), new ExeTools());

    /// <summary>The process: <paramref name="wanted"/> is what it asks of the console (<see cref="TextRuns.WantedFor"/>).</summary>
    internal static int Run(string[] args, TextConsole.Wanted wanted, IRunTools tools)
    {
        // The console (both programs' one implementation, Windowing.TextConsole; Windowing.TextRuns): a dedicated
        // host a console window of its own, --text-window the terminal or a window of its own, a run whose whole job is text the terminal
        // it was started from, and a game in a window nothing at all, its lines kept in its log (Diag.Plain) and in its log window
        // (--log-window, the console's logwindow: a viewer of its own); a redirect left as it is. Said when a run that is all text asks for it (SdlHost.TextConsole).
        TextConsole.Open(wanted, log: Vitomir.Engine.Diagnostics.Diag.Plain);
        if (TextConsole.Lines is { } lines)
        {
            // The engine's console echo of a log line is in the log already; a crash's box comes once the log has it.
            lines.AlreadyLogged = () => Vitomir.Engine.Diagnostics.ConsoleLogSink.Writing;
            Vitomir.Engine.Diagnostics.Diag.Crashed = e => FailureBox.Show("Vitomir", 1,
                [$"Vitomir stopped on an unhandled error: {e?.GetType().Name}: {e?.Message}"], Vitomir.Engine.Diagnostics.Diag.FilePath);
        }
        try
        {
            if (Array.IndexOf(args, "--replay-folder") >= 0) return ReplayFolder.Run(args);   // every tape of a folder checked, each in a process of its own (ours, ReplayFolder.cs)
            var host = new SdlClassicHost();
            var program = new ClassicProgram(args, host, tools);
            int exit = host.Frames(frame => program.Frame(frame)).ExitCode;
            host.Close();                                             // the GPU, the window's place, the window and SDL (the host's)
            // A game in a window that ended with an error: no terminal to see it in, so a message box naming the log.
            FailureBox.Show("Vitomir", exit, null, Vitomir.Engine.Diagnostics.Diag.FilePath);
            return exit;
        }
        finally
        {
            // The dedicated host's own window held open on its last line until a key; nothing for any other run.
            TextConsole.Ending();
        }
    }
}
