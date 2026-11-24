// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What the window came to, said in full, and a full screen checked to be one (ours).** In play,
/// the Display screen's Full screen row at 2560x1440 on a three-screen desktop
/// changed the desktop resolution: only the middle monitor was on, the side two shut off; in the
/// middle one the task bar was visible on the right side, and the game window took up the remaining
/// 80% or so of the screen. Its log said what had been asked for (<c>window: full screen at 2560x1440</c>)
/// and nothing of what the window was, so the cause could not be read from it. These pin
/// <see cref="WindowReport"/>: the lines every change of window mode writes afterwards, the check that a
/// full screen is flagged so and stands on its display's whole bounds, and what is tried when it does
/// not. The SDL side (<c>Vitomir.Platform.Render/Windowing/WindowReportHost.cs</c>) and the Windows calls in it cannot run in the cloud.
/// </summary>
public class WindowReportTests
{
    static WindowFacts Facts(bool full, DisplayRect window, ScreenMode? mode, DisplayRect? bounds, DisplayRect? usable = null) =>
        new(full, full ? "fullscreen, input focus" : "resizable, input focus", window, new ScreenMode(window.Width, window.Height), mode,
            "Surround (display 1)", bounds, usable ?? bounds, mode ?? new ScreenMode(7680, 1440), new ScreenMode(7680, 1440));

    static readonly DisplayRect Middle = new(0, 0, 2560, 1440);

    [Fact]
    public void A_full_screen_on_its_displays_whole_bounds_is_as_asked()
    {
        Assert.Null(WindowReport.Fault(true, Facts(true, Middle, new ScreenMode(2560, 1440), Middle)));
        // Borderless at the desktop's size, on his three screens.
        var all = new DisplayRect(0, 0, 7680, 1440);
        Assert.Null(WindowReport.Fault(true, Facts(true, all, null, all, new DisplayRect(0, 0, 7618, 1440))));
        // A window that was asked to be a window is never at fault, wherever it is.
        Assert.Null(WindowReport.Fault(false, Facts(false, new DisplayRect(40, 60, 1600, 900), null, all)));
        // Nothing is made of bounds SDL would not give.
        Assert.Null(WindowReport.Fault(true, Facts(true, Middle, null, null)));
    }

    /// <summary>What play showed, as the two ways it can be read: not full screen at all, or full screen on less than the display.</summary>
    [Fact]
    public void A_full_screen_that_is_not_one_is_said()
    {
        // SDL dropped it back to a window (a change of mode it gave up on leaves full screen and still answers true).
        Assert.Equal("it is not full screen", WindowReport.Fault(true, Facts(false, new DisplayRect(0, 0, 2048, 1152), null, Middle)));
        // Flagged full screen, but on the display less the task bar at its right.
        var usable = new DisplayRect(0, 0, 2048, 1440);
        string? fault = WindowReport.Fault(true, Facts(true, usable, new ScreenMode(2560, 1440), Middle, usable));
        Assert.NotNull(fault);
        Assert.Contains("it does not cover its display", fault);
        Assert.Contains("2048x1440 at 0,0", fault);
        Assert.Contains("2560x1440 at 0,0", fault);
        Assert.Contains("the usable bounds", fault);
        // The right size at the wrong place is not on the display either.
        Assert.Contains("2560x1440 at 2560,0", WindowReport.Fault(true, Facts(true, new DisplayRect(2560, 0, 2560, 1440), new ScreenMode(2560, 1440), Middle)));
    }

    /// <summary>Asked afresh once; then, from a mode of its own, the desktop's size; then left and said.</summary>
    /// <summary>The lines name the moment and say the mode asked for, the mode SDL reports, the flags, the place, both sizes, and the display's bounds and usable bounds.</summary>
    [Fact]
    public void The_lines_say_what_was_asked_and_what_the_window_is()
    {
        var facts = new WindowFacts(true, "fullscreen, resizable, input focus", Middle, new ScreenMode(2560, 1440), new ScreenMode(2560, 1440),
                                    "Surround (display 1)", Middle, new DisplayRect(0, 0, 2498, 1440), new ScreenMode(2560, 1440), new ScreenMode(7680, 1440));
        var lines = WindowReport.Lines("after the Display screen's change", "full screen at 2560x1440", facts);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("window: [after the Display screen's change] ", l));
        Assert.Contains("asked for full screen at 2560x1440; it is full screen at a mode of its own, 2560x1440", lines[0]);
        Assert.Contains("at 0,0, 2560x1440, 2560x1440 in pixels; flags: fullscreen, resizable, input focus", lines[1]);
        Assert.Contains("on Surround (display 1): bounds 2560x1440 at 0,0, usable (less the task bar) 2498x1440 at 0,0, in the mode 2560x1440, the desktop's own 7680x1440", lines[2]);
        // The other two answers to "what is it".
        Assert.Contains("it is full screen with no mode of its own (the desktop's)", WindowReport.Lines("x", "y", facts with { Mode = null })[0]);
        Assert.Contains("it is not full screen", WindowReport.Lines("x", "y", facts with { Full = false })[0]);
        Assert.Contains("bounds not given", WindowReport.Lines("x", "y", facts with { Bounds = null })[2]);
    }
}
