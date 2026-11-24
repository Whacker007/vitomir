// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--console-run` with `--settle`: the lines run before the settle, not at the still.
using Vitomir.Engine.Ui;

namespace Vitomir;

/// <summary>
/// <c>--console-run "line;line"</c> in a headless run that settles (ours). A headless still ran
/// its lines just before its one frame, after the settle, so the recipe for a hidden Christof —
/// <c>--load SP3 --console-run "sethide 3" --settle 20 …</c> — settled and watched twenty
/// seconds of an unhidden Christof and hid him for the picture alone. With a settle the lines now run
/// once, during it, through the same <see cref="DevConsole.Run"/> a typed line takes.
///
/// **On the settle's tenth step, not before its first.** They once ran before the first step,
/// which is not what a windowed run does: there they run on the tenth frame (<see
/// cref="ConsoleRun.Frame"/>), after the minds' first thinks. The difference decided a
/// play-test. Windowed, the Cappadocian by the stair in `SP3` had already taken Christof
/// before <c>sethide 3</c> ran, lost him, and walked at him (the approach, mode 16); settled, she had
/// never seen him, took Wilhem instead, and the log showed a different fight. Now both run the lines
/// at the same point.
/// </summary>
sealed class SettleConsole(string[] lines)
{
    /// <summary>The lines of the argument, or null when there is none.</summary>
    public static SettleConsole? From(string? arg)
    {
        if (arg is null || arg.StartsWith("--", StringComparison.Ordinal)) return null;
        var lines = arg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : new SettleConsole(lines);
    }

    int steps;
    bool done;

    /// <summary>One step of the settle has run: the lines go once the tenth has.</summary>
    public void Stepped(DevConsole? console)
    {
        if (++steps >= ConsoleRun.Frame) Run(console);
    }

    /// <summary>Runs the lines if they have not run yet — a settle shorter than ten steps, at its end.</summary>
    public void Run(DevConsole? console)
    {
        if (done || console is null) return;
        done = true;
        foreach (var line in lines) console.Run(line);
    }
}
