// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The window kept over starts, and its safeguard** (ours, "it should remember the game
/// window size and position as well. Will need a safeguard in place if the resolution is different and/or the
/// window position would be off the display"). The arithmetic only — <see cref="WindowPlacement"/> is fed
/// made-up displays, so the cases a player meets once in a year (a monitor unplugged, a lower resolution, a
/// window dragged nearly off the edge) are each pinned: kept where it passes, the default 1600x900 shrunk to
/// fit and centred on the primary display where it does not.
/// </summary>
public class WindowPlacementTests
{
    const int Title = 30;
    static readonly DisplayArea Laptop = new("Laptop", new DisplayRect(0, 0, 1920, 1040), Primary: true);
    static readonly DisplayArea Side = new("Side", new DisplayRect(1920, 0, 2560, 1400), Primary: false);

    static Placement Resolve(WindowPlace? saved, params DisplayArea[] displays) =>
        WindowPlacement.Resolve(saved, displays, 1600, 900, Title);

    [Fact]
    public void A_window_left_on_a_connected_display_comes_back_where_it_was_and_maximised_if_it_was()
    {
        var saved = new WindowPlace(new DisplayRect(100, 80, 1280, 720), "Laptop", Maximised: false);
        var p = Resolve(saved, Laptop, Side);
        Assert.Equal(PlaceVerdict.Restored, p.Verdict);
        Assert.Equal(saved.Rect, p.Rect);
        Assert.False(p.Maximised);

        var onSide = new WindowPlace(new DisplayRect(2200, 200, 1600, 900), "Side", Maximised: true);
        p = Resolve(onSide, Laptop, Side);
        Assert.Equal(PlaceVerdict.Restored, p.Verdict);
        Assert.Equal(onSide.Rect, p.Rect);
        Assert.True(p.Maximised);
    }

    [Fact]
    public void Nothing_kept_is_the_default_size_centred_on_the_primary_display()
    {
        var p = Resolve(null, Side, Laptop);
        Assert.Equal(PlaceVerdict.NoneSaved, p.Verdict);
        // 1920 wide: (1920-1600)/2 = 160 across; 1040 high less the 30 bar: 30 + (1010-900)/2 = 85 down.
        Assert.Equal(new DisplayRect(160, 85, 1600, 900), p.Rect);
    }

    [Fact]
    public void A_display_that_is_gone_falls_back_to_the_primary_and_is_not_maximised()
    {
        var saved = new WindowPlace(new DisplayRect(2200, 200, 1600, 900), "Side", Maximised: true);
        var p = Resolve(saved, Laptop);
        Assert.Equal(PlaceVerdict.DisplayGone, p.Verdict);
        Assert.Equal(new DisplayRect(160, 85, 1600, 900), p.Rect);
        Assert.False(p.Maximised);
    }

    [Fact]
    public void A_display_at_a_lower_resolution_than_the_window_gets_the_default_shrunk_to_fit_keeping_its_shape()
    {
        // Left 1600x900 on a 1920x1080 display that is now 1366x768 (less a 40-pixel task bar).
        var saved = new WindowPlace(new DisplayRect(160, 85, 1600, 900), "Laptop", Maximised: false);
        var small = Laptop with { Usable = new DisplayRect(0, 0, 1366, 728) };
        var p = Resolve(saved, small);
        Assert.Equal(PlaceVerdict.TooBig, p.Verdict);
        var r = Assert.IsType<DisplayRect>(p.Rect);
        Assert.True(r.Width <= 1366 && r.Height + Title <= 728, $"{r} does not fit");
        Assert.Equal(1600.0 / 900.0, r.Width / (double)r.Height, 2);
        Assert.InRange((1366 - r.Right) - r.X, 0, 1);                         // centred across
        Assert.InRange((728 - r.Bottom) - (r.Y - Title), 0, 1);               // and down, title bar included
    }

    [Fact]
    public void A_window_exactly_the_display_size_does_not_fit_because_its_title_bar_would_be_off_the_top()
    {
        // A 1600x900 window on a 1600x900 display: the inside alone fills it.
        var exact = new DisplayArea("Exact", new DisplayRect(0, 0, 1600, 900), true);
        var saved = new WindowPlace(new DisplayRect(0, 0, 1600, 900), "Exact", false);
        Assert.Equal(PlaceVerdict.TooBig, Resolve(saved, exact).Verdict);
    }

    [Fact]
    public void A_borderless_window_the_displays_whole_height_is_kept_where_it_was()
    {
        // A borderless 2560x1440 window on a three-screen desktop, 7680x1440 less a
        // task bar standing at its right. With an assumed title bar it was "too big", put at 1600x900 and kept so.
        var surround = new DisplayArea("NV Surround", new DisplayRect(0, 0, 7467, 1440), true);
        var saved = new WindowPlace(new DisplayRect(2560, 0, 2560, 1440), "NV Surround", false);
        Assert.Equal(PlaceVerdict.TooBig, WindowPlacement.Resolve(saved, [surround], 1600, 900, Title).Verdict);
        var kept = WindowPlacement.Resolve(saved, [surround], 1600, 900, Title, borderless: true);
        Assert.Equal(PlaceVerdict.Restored, kept.Verdict);
        Assert.Equal(saved.Rect, kept.Rect);
        // And one off the display is still not kept, bar or none.
        var off = saved with { Rect = saved.Rect with { X = 7000 } };
        Assert.Equal(PlaceVerdict.OffDisplay, WindowPlacement.Resolve(off, [surround], 1600, 900, Title, borderless: true).Verdict);
    }

    [Fact]
    public void A_window_whose_title_bar_is_off_the_top_is_moved_down_onto_its_display()
    {
        // Earlier, this fell back to 1600x900 in the middle; now its size and its place across are kept and it is
        // moved down just far enough for its title bar to be on the display (a window with a title
        // bar put at a borderless window's place stood with its bar off the top of the screen).
        var saved = new WindowPlace(new DisplayRect(100, 10, 1280, 720), "Laptop", false);   // bar from -20 to 10
        var p = Resolve(saved, Laptop);
        Assert.Equal(PlaceVerdict.MovedDown, p.Verdict);
        Assert.Equal(new DisplayRect(100, Title, 1280, 720), p.Rect);
        Assert.Equal(PlaceVerdict.Restored, Resolve(saved with { Rect = saved.Rect with { Y = Title } }, Laptop).Verdict);
        // Moved down and still mostly off the display across: the fallback, as before.
        Assert.Equal(PlaceVerdict.OffDisplay, Resolve(saved with { Rect = saved.Rect with { X = 1500 } }, Laptop).Verdict);
        // One too tall to fit with its bar is not moved: too big, the fallback.
        Assert.Equal(PlaceVerdict.TooBig, Resolve(saved with { Rect = new DisplayRect(100, 0, 1280, 1040) }, Laptop).Verdict);
    }

    [Fact]
    public void Mostly_on_means_half_the_window_and_half_its_title_bar()
    {
        // 1280 wide: 640 across the right edge is exactly half on; 641 is less than half.
        var half = new DisplayRect(1920 - 640, 100, 1280, 720);
        Assert.True(WindowPlacement.MostlyOn(half, [Laptop], Title));
        Assert.False(WindowPlacement.MostlyOn(half with { X = half.X + 1 }, [Laptop], Title));
        // Below the bottom: the title bar is on the display but most of the window is not.
        var low = new DisplayRect(100, 1040 - 300, 1280, 720);
        Assert.False(WindowPlacement.MostlyOn(low, [Laptop], Title));
        // A window left far off every display, as after an unplugged monitor whose name came back on another.
        Assert.Equal(PlaceVerdict.OffDisplay, Resolve(new WindowPlace(new DisplayRect(-5000, 300, 1280, 720), "Laptop", false), Laptop).Verdict);
    }

    [Fact]
    public void A_window_across_two_displays_counts_what_is_on_each()
    {
        // Two-thirds on the laptop and a third on the side display: all of it shows, counted over both.
        var across = new DisplayRect(1920 - 800, 200, 1200, 700);
        Assert.True(WindowPlacement.MostlyOn(across, [Laptop, Side], Title));
        Assert.Equal(PlaceVerdict.Restored, Resolve(new WindowPlace(across, "Laptop", false), Laptop, Side).Verdict);
        // The side display unplugged since: two-thirds still shows, on the display it was kept on.
        Assert.Equal(PlaceVerdict.Restored, Resolve(new WindowPlace(across, "Laptop", false), Laptop).Verdict);
    }

    [Fact]
    public void A_primary_smaller_than_the_default_shrinks_it_and_no_display_at_all_leaves_it_to_the_system()
    {
        var tiny = new DisplayArea("Tiny", new DisplayRect(0, 0, 1024, 600), true);
        var p = Resolve(null, tiny);
        var r = Assert.IsType<DisplayRect>(p.Rect);
        Assert.True(r.Width <= 1024 && r.Height + Title <= 600);
        Assert.InRange((600 - r.Bottom) - (r.Y - Title), 0, 1);

        var none = Resolve(null);
        Assert.Null(none.Rect);
        Assert.Equal((1600, 900), (none.Width, none.Height));
    }

    [Fact]
    public void An_unknown_title_bar_is_taken_as_the_assumed_one()
    {
        var p = WindowPlacement.Resolve(null, [Laptop], 1600, 900, titleBar: 0);
        Assert.Equal(WindowPlacement.AssumedTitleBar + (1040 - WindowPlacement.AssumedTitleBar - 900) / 2, p.Rect!.Value.Y);
    }

    [Fact]
    public void The_section_reads_back_what_was_written_and_a_broken_one_reads_as_nothing_kept()
    {
        var place = new WindowPlace(new DisplayRect(-1700, 40, 1500, 844), "DELL U2719D", true);
        string text = SettingsFile.Apply("[options]\ngamma = 1.3\n", WindowPlacement.Section, WindowPlacement.Values(place));
        Assert.Contains("gamma = 1.3", text);
        Assert.Equal(place, WindowPlacement.Read(IniFile.Parse(text).Window()));

        Assert.Null(WindowPlacement.Read(IniFile.Parse("[window]\nx = 10\ny = 10\nwidth = wide\nheight = 900\n").Window()));
        Assert.Null(WindowPlacement.Read(IniFile.Parse("[window]\nx = 10\ny = 10\nwidth = 100\nheight = 90\n").Window()));
        Assert.Null(WindowPlacement.Read(IniFile.Parse("").Window()));
    }
}
