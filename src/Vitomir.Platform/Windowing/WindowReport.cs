// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the window came to after a change of mode, said in full (ours). In play, the Display
// screen's Full screen row at 2560x1440 on a three-screen desktop changed the desktop resolution:
// only the middle monitor was on, the side two shut off; in the middle one the task bar was visible
// on the right side, and the game window took up the remaining 80% or so of the screen. Its log said
// what had been asked for and nothing of what the window was. These are the rules of what is said and
// of what counts as a full screen that is not one; the part that asks SDL is `Vitomir.Platform.Render/Windowing/WindowReportHost.cs`.
namespace Vitomir.Platform.Windowing;

/// <summary>
/// The window as SDL reports it at one moment: whether it is full screen, the flags' names, where it
/// is and its size, its size in pixels, the full-screen mode it has of its own (null: none, the
/// desktop's), and its display — the name, the whole bounds, the bounds less the task bar, the mode the
/// display is in now and the desktop's own mode. A bounds or mode SDL would not give is null.
/// </summary>
public sealed record WindowFacts(bool Full, string Flags, DisplayRect Window, ScreenMode Pixels, ScreenMode? Mode,
                                 string Display, DisplayRect? Bounds, DisplayRect? Usable, ScreenMode? Current, ScreenMode? Desktop);

/// <summary>
/// **The window's state in the log, and the check that a full screen is one (ours).**
/// <see cref="Lines"/> is what every change of window mode says afterwards — the mode asked for, the mode
/// SDL reports, the flags, the place and both sizes, and the display's bounds and usable bounds — so a
/// log from a machine nobody else can see settles what happened on it. <see cref="Fault"/> is the check:
/// a window meant to be full screen must be flagged so and stand exactly on its display's bounds; one
/// that does not is said in the log, and nothing more is done about it (an earlier build asked afresh and then fell
/// back to the desktop's size; a run showed the window was right and the task bar was over it, a
/// different matter — <see cref="DesktopRoom.StaysOnTop"/> — so the remedy for a fault never seen went).
/// </summary>
public static class WindowReport
{
    static string Rect(DisplayRect? r) => r is { } v ? $"{v.Width}x{v.Height} at {v.X},{v.Y}" : "not given";
    static string Mode(ScreenMode? m) => m is { } v ? v.ToString() : "not given";

    /// <summary>
    /// The lines for the log: <paramref name="when"/> names the moment (<c>after the Display screen's
    /// change</c>), <paramref name="asked"/> what was asked for (<c>full screen at 2560x1440</c>).
    /// </summary>
    public static string[] Lines(string when, string asked, WindowFacts f) =>
    [
        $"window: [{when}] asked for {asked}; it is "
            + (!f.Full ? "not full screen" : f.Mode is { } m ? $"full screen at a mode of its own, {m}" : "full screen with no mode of its own (the desktop's)"),
        $"window: [{when}] at {f.Window.X},{f.Window.Y}, {f.Window.Width}x{f.Window.Height}, {f.Pixels} in pixels; flags: {(f.Flags.Length == 0 ? "none" : f.Flags)}",
        $"window: [{when}] on {(f.Display.Length == 0 ? "a display with no name" : f.Display)}: bounds {Rect(f.Bounds)}, usable (less the task bar) {Rect(f.Usable)}, "
            + $"in the mode {Mode(f.Current)}, the desktop's own {Mode(f.Desktop)}",
    ];

    /// <summary>
    /// What is wrong with a window that was asked to be full screen (either kind), or null when it is
    /// one: it must be flagged full screen, and its place and size must be its display's whole bounds —
    /// not the usable bounds, which leave the task bar showing. Nothing is checked when the display's
    /// bounds are not given, and nothing for a window that was asked to be a window.
    /// </summary>
    public static string? Fault(bool wantFull, WindowFacts f)
    {
        if (!wantFull) return null;
        if (!f.Full) return "it is not full screen";
        if (f.Bounds is not { } b || f.Window == b) return null;
        return $"it does not cover its display: it is {Rect(f.Window)} and the display is {Rect(b)}"
               + (f.Usable is { } u && f.Window == u ? " (it is on the usable bounds, the display less the task bar)" : "");
    }
}
