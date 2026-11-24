// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A game in a window's failure, seen with no terminal.
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **A failure with no terminal is still seen**: a game in a window attaches to no console, so an error that ends
/// it — an unhandled error, or a refusal at the start (a bad switch, the install not found, the port another program holds) —
/// is shown in SDL's message box as well as written to the log, with the log file named. Only for a game in a window
/// (<see cref="TextConsole.BoxesFailure"/>): a run with a terminal, a redirect (the tests) or the dedicated host's window never.
/// </summary>
public static class FailureBox
{
    /// <summary>Whether a box has been shown in this process (one is enough).</summary>
    public static bool Shown { get; private set; }

    /// <summary>A box of the program's own has been shown (the platform's refusals): no second one.</summary>
    public static void Mark() => Shown = true;

    /// <summary>What a failure said: the standard error's last lines, else the last three lines printed.</summary>
    static IReadOnlyList<string> Said() =>
        TextConsole.Lines is not { } lines ? [] : lines.LastErrors.Count > 0 ? lines.LastErrors : [.. lines.Recent.TakeLast(3)];

    /// <summary>The text of the box: what went wrong (the standard error's last lines, or the error), and where the log is. Plain words.</summary>
    public static string Text(IReadOnlyList<string> what, IReadOnlyList<string?> logs)
    {
        var lines = what.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(ConsoleLines.ErrorsKept).ToList();
        string said = lines.Count > 0 ? string.Join("\n", lines) : "It ended with an error.";
        var named = logs.Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
        return said + "\n\n" + (named.Count > 0 ? "The log has the whole run:\n" + string.Join("\n", named) : "The log folder has the whole run.");
    }

    /// <summary>
    /// The box, when <see cref="TextConsole.BoxesFailure"/> says so for this process (a game in a window, ended with
    /// <paramref name="exitCode"/>): <paramref name="what"/> said (the standard error's last lines when null), the logs named.
    /// Waits for its OK. Answers whether it was shown.
    /// </summary>
    public static unsafe bool Show(string title, int exitCode, IReadOnlyList<string>? what, params string?[] logs)
    {
        if (!TextConsole.BoxesFailure(TextConsole.Chosen, exitCode, Shown)) return false;
        Shown = true;
        try { SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, title, Text(what ?? Said(), logs), null); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
        return true;
    }
}
