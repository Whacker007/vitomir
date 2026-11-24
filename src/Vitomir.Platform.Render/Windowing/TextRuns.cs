// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which runs are all text and which are a game in a window: what each program asks
// of its console at the very start, before anything is printed.
namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **What a run asks of the console**, from its command line alone, a pure function tested on every OS. Both
/// programs are windowed (<c>WinExe</c>), so a shell gives its prompt back at once; the only tie to the terminal left was
/// attaching to it, which wrote the game's lines after the prompt and tied the game to that console (closing it ended the game).
/// So **a game in a window attaches to nothing** (<see cref="TextConsole.Wanted.Nothing"/>; its lines are kept in its log), and
/// only these attach to the terminal they were started from:
///
/// - the dedicated host (<c>--headless-server</c>): a console window of its own, always;
/// - the log window's viewer (<c>--log-viewer</c> first): a console window of its own, no prompt;
/// - <c>vitomir-tools</c>' <c>--text-window</c>: the terminal, or a window of its own when there is none;
/// - **the runs whose whole job is text**, that print and stop or run with no window (<see cref="Exe"/>, <see cref="Platform"/>):
///   the terminal it was started from, when there is one.
/// </summary>
public static class TextRuns
{
    /// <summary>
    /// <c>vitomir-tools</c>' text runs (and the Classic module's under the platform, whose command line is 1.1's): the reports, the
    /// data and level tools, the sweeps, the replay checks, the bot, a still, <c>--diagnostics</c>, <c>--rules</c>, <c>--help</c>.
    /// </summary>
    public static readonly string[] Exe =
    [
        "--net-bot", "--headless", "--diagnostics", "--rules", "--help", "-h",
        "--list-saves", "--chronicles", "--saves", "--gait",
        "--things", "--scripts-all", "--modes", "--find", "--storm", "--sky-groups",
        "--sweep", "--story-sweep", "--replay-folder", "--replay-check",
        "--ui-art-list", "--ui-art-sheet", "--ui-art-cut", "--ui-art-one",
        "--install-data", "--verify-data", "--import-to", "--log-bundle",
        "--export-gltf", "--relayout-l2", "--probes-at", "--cook-l2", "--convert-l2", "--stream-fly",
    ];

    /// <summary>
    /// <c>vitomir-platform.exe</c>'s own text runs, beside the module's (<see cref="Exe"/>): the checks and dumps, the
    /// harness's surveys, drives, arenas and scenarios, a still or a screen, the key bindings' command, <c>--help</c>, and the setup.
    /// </summary>
    public static readonly string[] Platform =
    [
        "--help", "-h", "--check-modules", "--bind", "--bindings",
        "--headless", "--headless-screen", "--replay-check", "--replay-dump", "--snagcheck", "--record-drive",
        "--arena", "--aim-survey", "--arc-survey", "--camera-survey", "--scenario", "--describe", "--check", "--trace-event",
        "--setup",
    ];

    /// <summary>
    /// **The decision's input**: what the run asks of the console, from its command line. <paramref name="platform"/> is
    /// <c>vitomir-platform.exe</c> (its own text runs and the module's both count).
    /// </summary>
    public static TextConsole.Wanted WantedFor(IReadOnlyList<string> args, bool platform)
    {
        // The log window's viewer: each program runs it before asking (LogViewer.Run asks for it itself).
        if (LogViewer.Asked(args)) return TextConsole.Wanted.LogViewer;
        if (args.Contains("--headless-server")) return TextConsole.Wanted.OwnWindow;
        if (args.Contains("--text-window")) return TextConsole.Wanted.TerminalElseOwnWindow;
        foreach (var a in args)
            if (Array.IndexOf(Exe, a) >= 0 || platform && Array.IndexOf(Platform, a) >= 0) return TextConsole.Wanted.Terminal;
        return TextConsole.Wanted.Nothing;
    }
}
