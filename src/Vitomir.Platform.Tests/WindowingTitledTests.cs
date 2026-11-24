// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Windowing;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **A window with its title bar never stands with its bar off its display, and Alt+Enter brings back the window the run
/// had**. On one three-screen display of 7680x1440 (three 2560x1440 screens),
/// the task bar 213 wide at the far right, Windows' title bar 31 high — with a <c>[window]</c> keeping a borderless
/// 2560x1440 at 2560,0 (borderless on the middle screen): <c>--window</c> came up looking borderless, and after
/// the Display screen's Window 1920x1080, Alt+Enter twice "went to borderless windowed instead" — the window back at
/// 2560x1440 at 2560,0 with its title bar at -31, off the top. Those numbers, and those two paths, as the rules see them;
/// the SDL side is proved on a Windows machine.
/// </summary>
public class WindowingTitledTests
{
    const int Bar = 31;
    const string Surround = "NV Surround";
    /// <summary>That desktop as SDL reports it: one display, its usable bounds the desktop less the task bar at the right.</summary>
    static readonly DisplayArea Desk = new(Surround, new DisplayRect(0, 0, 7680 - 213, 1440), Primary: true);
    static readonly DisplayRect DeskBounds = new(0, 0, 7680, 1440);
    /// <summary>His <c>[window]</c> as the runs read it (his <c>vitomir.ini</c>).</summary>
    static readonly WindowKeys His = new([
        new("x", "2560"), new("y", "0"), new("width", "2560"), new("height", "1440"), new("display", Surround),
        new("maximised", "0"), new("fullscreen", "off"), new("fullscreenmode", "desktop"), new("borderless", "on"),
        new("worldsize", "2560x1440")]);

    /// <summary>The run's start as the host decides it: <see cref="FullScreen.Start"/>, then the borderless window's (<see cref="Borderless.Start"/>).</summary>
    static WindowStart Start(bool windowFlag) =>
        Borderless.Start(FullScreen.Start(FullScreen.Read(His), replaying: false, size: null, harness: false, windowFlag, fullscreenFlag: false),
                         Borderless.Read(His), null, replaying: false, sizeFlag: false, windowFlag, fullscreenFlag: false);

    /// <summary>The frame's top (its title bar's) on the display, for a window with a title bar at <paramref name="inside"/>.</summary>
    static void BarOn(DisplayRect inside)
    {
        var frame = WindowPlacement.Frame(inside, Bar);
        Assert.True(frame.Y >= Desk.Usable.Y && frame.Bottom <= Desk.Usable.Bottom && frame.X >= 0 && frame.Right <= Desk.Usable.Right,
                    $"the window {inside} with its title bar ({frame}) is not on the display");
    }

    [Fact]
    public void Window_starts_a_window_with_its_title_bar_and_a_plain_start_his_borderless_window()
    {
        var titled = Start(windowFlag: true);
        Assert.True(titled.Titled);
        Assert.Equal((true, false, false), (titled.Restore, titled.Keep, titled.Full));
        Assert.Null(titled.Borderless);
        // His plain start: the kept borderless window, kept, as before.
        var plain = Start(windowFlag: false);
        Assert.False(plain.Titled);
        Assert.Equal((true, true, false), (plain.Restore, plain.Keep, plain.Full));
        Assert.True(FullScreen.ReadBorderless(His));
    }

    [Fact]
    public void His_kept_borderless_place_is_not_a_place_for_a_window_with_a_title_bar()
    {
        var saved = WindowPlacement.Read(His)!;
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), saved.Rect);
        // Borderless (his plain start): kept where it was, as.
        var bare = WindowPlacement.Resolve(saved, [Desk], 1600, 900, Bar, borderless: true);
        Assert.Equal((PlaceVerdict.Restored, (DisplayRect?)saved.Rect), (bare.Verdict, bare.Rect));
        // With its title bar (--window): 1440 + 31 does not fit 1440, so moving it down cannot help: the safeguard's
        // fallback, 1600x900 in the middle of the usable area below its bar — on his middle screen (2560 to 5120).
        var titled = WindowPlacement.Resolve(saved, [Desk], 1600, 900, Bar);
        Assert.Equal(PlaceVerdict.TooBig, titled.Verdict);
        Assert.Equal(new DisplayRect((7467 - 1600) / 2, Bar + (1440 - Bar - 900) / 2, 1600, 900), titled.Rect);
        Assert.Equal(new DisplayRect(2933, 285, 1600, 900), titled.Rect);
        BarOn(titled.Rect!.Value);
        Assert.Equal("window: 2560x1440 does not fit NV Surround now; 1600x900 on the primary; the place kept is a borderless window's, and this window has its title bar",
                     WindowPlacement.Describe(titled, saved, keptBorderless: true));
    }

    [Fact]
    public void A_kept_place_that_fits_with_a_title_bar_once_moved_down_keeps_its_size()
    {
        // A borderless 1920x1080 kept at the top of the middle screen: with a title bar it is moved down by the bar.
        var top = new WindowPlace(new DisplayRect(3200, 0, 1920, 1080), Surround, false);
        var p = WindowPlacement.Resolve(top, [Desk], 1600, 900, Bar);
        Assert.Equal((PlaceVerdict.MovedDown, (DisplayRect?)new DisplayRect(3200, Bar, 1920, 1080)), (p.Verdict, p.Rect));
        BarOn(p.Rect!.Value);
        Assert.Equal("window: 1920x1080 at 3200,0 would have its title bar above the top of NV Surround; moved down onto it, 1920x1080 at 3200,31",
                     WindowPlacement.Describe(p, top));
        // Borderless, it is kept where it was.
        Assert.Equal(PlaceVerdict.Restored, WindowPlacement.Resolve(top, [Desk], 1600, 900, Bar, borderless: true).Verdict);
        // Where the Display screen puts its window is a place a window with a title bar keeps as it is.
        var shown = new WindowPlace(new DisplayRect(2773, 180, 1920, 1080), Surround, false);
        Assert.Equal(PlaceVerdict.Restored, WindowPlacement.Resolve(shown, [Desk], 1600, 900, Bar).Verdict);
        // The laptop (1600x900, the task bar 48 high at the bottom): a 1280x720 window kept at its top edge comes down 31.
        var laptop = new DisplayArea("Laptop", new DisplayRect(0, 0, 1600, 852), true);
        var low = WindowPlacement.Resolve(new WindowPlace(new DisplayRect(160, 0, 1280, 720), "Laptop", false), [laptop], 1600, 900, Bar);
        Assert.Equal((PlaceVerdict.MovedDown, (DisplayRect?)new DisplayRect(160, Bar, 1280, 720)), (low.Verdict, low.Rect));
    }

    [Fact]
    public void The_Display_screen_puts_his_window_of_1920x1080_where_his_log_has_it()
    {
        // Its log: "a window at 1920x1080", at 2773,180 — the usable area's middle across, the screen's middle down.
        Assert.Equal(new DisplayRect(2773, 180, 1920, 1080), WindowPlacement.DisplayScreenPlace(Desk.Usable, new ScreenMode(1920, 1080), bare: false));
        BarOn(new DisplayRect(2773, 180, 1920, 1080));
        // Borderless Window at 2560x1440: the middle of the whole desktop, his middle screen.
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), WindowPlacement.DisplayScreenPlace(DeskBounds, new ScreenMode(2560, 1440), bare: true));
        // A window with a title bar as tall as the usable area is never put higher than the bar.
        Assert.Equal(Bar, WindowPlacement.DisplayScreenPlace(Desk.Usable, new ScreenMode(2560, 1440), bare: false).Y);
    }

    [Fact]
    public void His_second_path_Display_screen_window_then_Alt_Enter_twice_comes_back_to_that_window()
    {
        var run = new WindowInRun();
        // `--window` with his ini: a window with its title bar, the safeguard's fallback (above).
        var start = WindowPlacement.Resolve(WindowPlacement.Read(His), [Desk], 1600, 900, Bar);
        run.Start(start.Rect, start.Maximised, bordered: true);
        Assert.True(run.Note(new WindowNow(start.Rect, Full: false, Maximised: false, Minimised: false, Borderless: false, Surround)));
        // The Display screen: Window, 1920x1080. Its window is noted as the run's (an earlier build did not).
        var shown = WindowPlacement.DisplayScreenPlace(Desk.Usable, new ScreenMode(1920, 1080), bare: false);
        run.Note(new WindowNow(shown, false, false, false, false, Surround));
        for (int cycle = 0; cycle < 2; cycle++)
        {
            // Alt+Enter: noted just before full screen, then full screen (not noted: no rectangle, its state unread).
            run.Note(new WindowNow(shown, false, false, false, false, Surround));
            run.Note(new WindowNow(null, Full: true, false, false, Borderless: false, Surround));
            // Alt+Enter again: that window, with its title bar, where it was — not the kept borderless 2560x1440 at 2560,0.
            var back = run.Back([Desk], Bar, 1600, 900);
            Assert.Equal(new WindowBack(new DisplayRect(2773, 180, 1920, 1080), Bordered: true, Maximised: false, PlaceVerdict.Restored), back);
            Assert.Null(run.Describe(back));
            BarOn(back.Rect!.Value);
            run.Note(new WindowNow(back.Rect, false, false, false, false, Surround));
        }
        // And the Display screen leaving full screen for Window 1920x1080 is that window too; another size or kind is not.
        Assert.True(run.Is(bordered: true, new ScreenMode(1920, 1080)));
        Assert.False(run.Is(bordered: false, new ScreenMode(1920, 1080)));
        Assert.False(run.Is(bordered: true, new ScreenMode(2560, 1440)));
    }

    [Fact]
    public void Back_from_full_screen_a_window_with_a_title_bar_at_a_borderless_place_is_put_on_the_display()
    {
        // The earlier way back, made safe: a window given its title bar at his kept borderless place (the system's window
        // 2576x1479 at 2552,-31 in its log) is not put back so; it does not fit with its bar: the fallback.
        var run = new WindowInRun();
        run.Start(new DisplayRect(2560, 0, 2560, 1440), maximised: false, bordered: false);
        run.Note(new WindowNow(new DisplayRect(2560, 0, 2560, 1440), false, false, false, Borderless: false, Surround));
        var back = run.Back([Desk], Bar, 1600, 900);
        Assert.Equal((PlaceVerdict.TooBig, true), (back.Verdict, back.Bordered));
        Assert.Equal(new DisplayRect(2933, 285, 1600, 900), back.Rect);
        BarOn(back.Rect!.Value);
        Assert.Equal("window: back from full screen 2560x1440 does not fit its display with a title bar; 1600x900 at 2933,285", run.Describe(back));
        // A 1920x1080 one at the top: moved down by the bar, its size kept.
        run.Note(new WindowNow(new DisplayRect(3200, 0, 1920, 1080), false, false, false, false, Surround));
        Assert.Equal(new WindowBack(new DisplayRect(3200, Bar, 1920, 1080), true, false, PlaceVerdict.MovedDown), run.Back([Desk], Bar, 1600, 900));
        // A display SDL named nothing: the safeguard reads every display rather than falling back for the name.
        var unnamed = new WindowInRun();
        unnamed.Note(new WindowNow(new DisplayRect(2773, 180, 1920, 1080), false, false, false, false, ""));
        Assert.Equal(PlaceVerdict.Restored, unnamed.Back([Desk], Bar, 1600, 900).Verdict);
    }

    [Fact]
    public void Back_from_full_screen_his_borderless_window_and_a_maximised_one_are_as_they_were()
    {
        // His usual run: the borderless 2560x1440 at 2560,0 goes full screen and comes back so, no title bar given it.
        var run = new WindowInRun();
        run.Start(new DisplayRect(2560, 0, 2560, 1440), false, bordered: false);
        run.Note(new WindowNow(new DisplayRect(2560, 0, 2560, 1440), false, false, false, Borderless: true, Surround));
        run.Note(new WindowNow(null, Full: true, false, false, false, Surround));
        Assert.Equal(new WindowBack(new DisplayRect(2560, 0, 2560, 1440), Bordered: false, Maximised: false, PlaceVerdict.Restored), run.Back([Desk], Bar, 1600, 900));
        // A maximised window: its normal place is kept from before it was maximised, and it is maximised again.
        var max = new WindowInRun();
        max.Note(new WindowNow(new DisplayRect(2773, 180, 1920, 1080), false, false, false, false, Surround));
        max.Note(new WindowNow(null, false, Maximised: true, false, false, Surround));
        max.Note(new WindowNow(null, Full: true, false, false, false, Surround));
        Assert.Equal(new WindowBack(new DisplayRect(2773, 180, 1920, 1080), true, Maximised: true, PlaceVerdict.Restored), max.Back([Desk], Bar, 1600, 900));
        // Minimised (Alt+Tab out of a full screen at a mode): nothing noted.
        Assert.False(new WindowInRun().Note(new WindowNow(new DisplayRect(0, 0, 160, 28), false, false, Minimised: true, false, Surround)));
        // Nothing noted at all: left where SDL puts it.
        Assert.Null(new WindowInRun().Back([Desk], Bar, 1600, 900).Rect);
    }
}
