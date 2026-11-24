// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Windowing;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The window's rules as the platform has them** (<c>Vitomir.Platform/Windowing</c>, the rules
/// both programs' window code asks), pinned with the numbers of three real desktops — a 7680x1440 three-screen
/// desktop (three 2560x1440 screens the system sees as one display, the task bar 213 wide at the far right), one 2560x1440
/// screen alone, and a laptop's 1600x900 (the task bar 48 high along the bottom) — and plain ones. The cloud has no real
/// desktop (SDL's offscreen driver), so these are what it can prove of the window; the SDL side is proved on a Windows machine. The
/// engine's tests (<c>FullScreenTests</c>, <c>BorderlessTests</c>, <c>PictureTests</c>, <c>DesktopRoomTests</c>,
/// <c>WindowPlacementTests</c>, <c>WindowReportTests</c>) pin the same rules beside the engine's side of them (the
/// settings file, the keys page, the interface's layout) and stay where they were.
/// </summary>
public class WindowingTests
{
    static readonly ScreenMode Surround = new(7680, 1440), Single = new(2560, 1440), Laptop = new(1600, 900);
    static readonly DisplayRect SurroundBounds = new(0, 0, 7680, 1440), SingleBounds = new(0, 0, 2560, 1440), LaptopBounds = new(0, 0, 1600, 900);
    /// <summary>The desktops' usable bounds: the Surround desktop's task bar at the far right, the others' along the bottom.</summary>
    static readonly DisplayRect SurroundUsable = new(0, 0, 7680 - 213, 1440), SingleUsable = new(0, 0, 2560, 1440 - 48), LaptopUsable = new(0, 0, 1600, 900 - 48);

    /// <summary>The room a window with a title bar has: the usable bounds less Windows' usual frame (8 a side, 31 on top), as the host takes it when the system will not say.</summary>
    static ScreenMode RoomForWindow(DisplayRect usable) => new(usable.Width - 8 - 8, usable.Height - 31 - 8);

    // ---- the desktop's room ----

    [Fact]
    public void The_room_on_each_desktop_is_the_desktop_less_its_task_bar()
    {
        Assert.Equal(SurroundUsable, DesktopRoom.UsableOnDesktop(Surround, SurroundBounds, SurroundUsable));
        Assert.Equal(SingleUsable, DesktopRoom.UsableOnDesktop(Single, SingleBounds, SingleUsable));
        Assert.Equal(LaptopUsable, DesktopRoom.UsableOnDesktop(Laptop, LaptopBounds, LaptopUsable));
        // In an exclusive mode of 1600x900 on the Surround desktop: the mode's bounds, the bar's margin kept, the desktop's size.
        Assert.Equal(SurroundUsable, DesktopRoom.UsableOnDesktop(Surround, new DisplayRect(0, 0, 1600, 900), new DisplayRect(0, 0, 1600 - 213, 900)));
        // And a 2560x1440 mode on it (the Display page's Full Screen row at the middle screen's size): the same room.
        Assert.Equal(SurroundUsable, DesktopRoom.UsableOnDesktop(Surround, new DisplayRect(0, 0, 2560, 1440), new DisplayRect(0, 0, 2560 - 213, 1440)));
        // Just after leaving a mode the usable bounds still have its height in them: a margin over a quarter of the side is stale.
        Assert.Equal(SurroundBounds, DesktopRoom.UsableOnDesktop(Surround, SurroundBounds, new DisplayRect(0, 0, 7680, 540)));
        Assert.Equal(LaptopBounds, DesktopRoom.UsableOnDesktop(Laptop, LaptopBounds, new DisplayRect(0, 0, 1600, 200)));
    }

    [Fact]
    public void The_window_rows_offer_what_fits_each_desktop()
    {
        // A window with its title bar: what fits the room less the frame. 2560x1440 is the whole height of all three desktops but the laptop's, so never.
        Assert.Equal(new ScreenMode(1920, 1080), FullScreen.SizesThatFit(RoomForWindow(SurroundUsable), null)[^1]);
        Assert.Equal(new ScreenMode(1920, 1080), FullScreen.SizesThatFit(RoomForWindow(SingleUsable), null)[^1]);
        Assert.Equal(new ScreenMode(1366, 768), FullScreen.SizesThatFit(RoomForWindow(LaptopUsable), null)[^1]);
        Assert.DoesNotContain(new ScreenMode(1600, 900), FullScreen.SizesThatFit(RoomForWindow(LaptopUsable), null));
        // A window with none: the whole desktop. The Surround desktop has room for 2560x1440 and 3440x1440 too.
        var surround = FullScreen.SizesThatFit(Surround, null);
        Assert.Contains(new ScreenMode(2560, 1440), surround);
        Assert.Contains(new ScreenMode(3440, 1440), surround);
        Assert.DoesNotContain(new ScreenMode(3840, 2160), surround);
        Assert.Equal(new ScreenMode(2560, 1440), FullScreen.SizesThatFit(Single, null)[^1]);
        Assert.Equal(new ScreenMode(1600, 900), FullScreen.SizesThatFit(Laptop, null)[^1]);
        // The size in use is offered whatever it is (a window kept at a size of its own).
        Assert.Contains(new ScreenMode(1436, 808), FullScreen.SizesThatFit(RoomForWindow(LaptopUsable), new ScreenMode(1436, 808)));
    }

    // ---- borderless full screen at the world's own size, and the picture's bars ----

    [Fact]
    public void Borderless_full_screen_on_the_Surround_desktop_draws_the_middle_screen()
    {
        // 2560x1440 picked: the picture is the middle screen exactly, the world at its own size (nothing stretched).
        var picked = FullScreen.WorldSizeOn(Surround, new ScreenMode(2560, 1440));
        Assert.Equal(new ScreenMode(2560, 1440), picked);
        var picture = Picture.RectOn(Surround, picked);
        Assert.Equal(new PictureRect(2560, 0, 2560, 1440), picture);
        Assert.True(Picture.HasBars(Surround, picture));
        Assert.Null(Picture.WorldSizeIn(picture, picked));
        // 1280x720 picked: the same rectangle, the world drawn at 1280x720 and stretched over it.
        var small = Picture.RectOn(Surround, new ScreenMode(1280, 720));
        Assert.Equal(picture, small);
        Assert.Equal(new ScreenMode(1280, 720), Picture.WorldSizeIn(small, new ScreenMode(1280, 720)));
        // 1280x1024: 1800x1440 at 2940,0. The desktop's own, or nothing picked: the whole desktop, no bars.
        Assert.Equal(new PictureRect(2940, 0, 1800, 1440), Picture.RectOn(Surround, new ScreenMode(1280, 1024)));
        Assert.Null(FullScreen.WorldSizeOn(Surround, Surround));
        Assert.False(Picture.HasBars(Surround, Picture.RectOn(Surround, null)));
    }

    [Fact]
    public void Borderless_full_screen_on_one_screen_and_on_the_laptop()
    {
        // A size of the screen's own shape has no bars; the world is drawn at it when it is smaller.
        Assert.False(Picture.HasBars(Single, Picture.RectOn(Single, new ScreenMode(1920, 1080))));
        Assert.Equal(new ScreenMode(1920, 1080), Picture.WorldSizeIn(Picture.RectOn(Single, new ScreenMode(1920, 1080)), new ScreenMode(1920, 1080)));
        Assert.False(Picture.HasBars(Laptop, Picture.RectOn(Laptop, new ScreenMode(1280, 720))));
        // A 5:4 size on a 16:9 screen: bars at its sides, the odd pixel to the right.
        Assert.Equal(new PictureRect(380, 0, 1800, 1440), Picture.RectOn(Single, new ScreenMode(1280, 1024)));
        Assert.Equal(new PictureRect(237, 0, 1125, 900), Picture.RectOn(Laptop, new ScreenMode(1280, 1024)));
        // ... and the world then larger than the picture either way: drawn at the picture's own size.
        Assert.Null(Picture.WorldSizeIn(Picture.RectOn(Laptop, new ScreenMode(1280, 1024)), new ScreenMode(1280, 1024)));
        // A size larger than the desktop (a file from the big PC on the laptop) is none: the desktop's own, no bars.
        Assert.Null(FullScreen.WorldSizeOn(Laptop, new ScreenMode(2560, 1440)));
        Assert.Null(FullScreen.WorldSizeOn(Laptop, new ScreenMode(1920, 1080)));
        Assert.Equal(new ScreenMode(1280, 720), FullScreen.WorldSizeOn(Laptop, new ScreenMode(1280, 720)));
        // A 16:9 size on a 5:4 screen: bars above and below.
        Assert.Equal(new PictureRect(0, 152, 1280, 720), Picture.RectOn(new ScreenMode(1280, 1024), new ScreenMode(1920, 1080)));
    }

    [Fact]
    public void Inside_the_picture_the_pointer_is_the_pictures()
    {
        var picture = Picture.RectOn(Surround, new ScreenMode(2560, 1440));
        Assert.Equal((440f, 100f), Picture.FromWindow(picture, 3000, 100));
        Assert.Equal((3000f, 100f), Picture.ToWindow(picture, 440, 100));
        // A press on a bar is off the picture; a movement there is held to the picture's nearest edge.
        Assert.False(Picture.On(picture, 100, 100));
        Assert.False(Picture.On(picture, 5120, 100));
        Assert.True(Picture.On(picture, 5119, 1439));
        Assert.Equal((0f, 100f), Picture.FromWindow(picture, 100, 100));
        Assert.Equal((2559f, 100f), Picture.FromWindow(picture, 7000, 100));
        Assert.Contains("the picture is 2560x1440 at 2560,0 of the window's 7680x1440", Picture.Describe(Surround, picture, null));
    }

    [Theory]
    [InlineData(7680, 1440, 640, 120)]
    [InlineData(2560, 1440, 640, 360)]
    [InlineData(1600, 900, 640, 360)]
    [InlineData(640, 480, 640, 480)]
    public void A_replay_nobody_watches_is_shown_small_on_any_desktop(int w, int h, int ww, int wh) =>
        Assert.Equal((ww, wh), Picture.ShrunkWindow(w, h));

    // ---- the centred window with no title bar ----

    [Fact]
    public void The_borderless_window_is_centred_on_the_whole_desktop()
    {
        var size = new ScreenMode(2560, 1440);
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), Borderless.Centre(SurroundBounds, size));
        Assert.Equal(new DisplayRect(0, 0, 2560, 1440), Borderless.Centre(SingleBounds, size));
        // On the laptop it is cut to the display's size.
        Assert.Equal(new DisplayRect(0, 0, 1600, 900), Borderless.Centre(LaptopBounds, size));
        Assert.Equal(new DisplayRect(160, 90, 1280, 720), Borderless.Centre(LaptopBounds, new ScreenMode(1280, 720)));
        // The display it was last kept on, else the primary.
        DisplayBounds surround = new("NVIDIA Surround", SurroundBounds, true), side = new("Side", new DisplayRect(-1600, 0, 1600, 900), false);
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), Borderless.Place(size, [side, surround], null));
        Assert.Equal(new DisplayRect(-1600, 0, 1600, 900), Borderless.Place(size, [side, surround], "Side"));
        Assert.Null(Borderless.Place(size, [], null));
        // The Enhanced game's centred window (its own rules, the same arithmetic) lands in the same place.
        Assert.Equal(new Vitomir.Platform.Presentation.DisplayRect(2560, 0, 2560, 1440),
                     Vitomir.Platform.Presentation.WindowPlacement.Centre(new Vitomir.Platform.Presentation.DisplayRect(0, 0, 7680, 1440), 2560, 1440));
    }

    [Fact]
    public void On_top_only_with_the_focus_and_a_full_screen_only_at_a_mode_of_its_own()
    {
        Assert.True(Borderless.OnTop(focused: true));
        Assert.False(Borderless.OnTop(focused: false));
        Assert.True(DesktopRoom.StaysOnTop(full: true, ownMode: true, keyboard: true));
        Assert.False(DesktopRoom.StaysOnTop(full: true, ownMode: false, keyboard: true));
        Assert.False(DesktopRoom.StaysOnTop(full: true, ownMode: true, keyboard: false));
        Assert.False(DesktopRoom.StaysOnTop(full: false, ownMode: true, keyboard: true));
    }

    // ---- the window's place kept, and the safeguard ----

    [Fact]
    public void A_kept_window_comes_back_where_it_was_on_each_desktop()
    {
        DisplayArea surround = new("NVIDIA Surround", SurroundUsable, true), single = new("Screen", SingleUsable, true), laptop = new("Laptop", LaptopUsable, true);
        var onSurround = new WindowPlace(new DisplayRect(2560, 31, 1920, 1080), "NVIDIA Surround", false);
        Assert.Equal(PlaceVerdict.Restored, WindowPlacement.Resolve(onSurround, [surround], 1600, 900, 31).Verdict);
        // A borderless 2560x1440 window kept on the Surround desktop fits its whole height; with a title bar it would not.
        var bare = new WindowPlace(new DisplayRect(2560, 0, 2560, 1440), "NVIDIA Surround", false);
        Assert.Equal(PlaceVerdict.Restored, WindowPlacement.Resolve(bare, [surround], 1600, 900, 31, borderless: true).Verdict);
        Assert.Equal(PlaceVerdict.TooBig, WindowPlacement.Resolve(bare, [surround], 1600, 900, 31).Verdict);
        // The big PC's window on the laptop: its display is gone, so the fallback on the laptop's primary, fitted with its title bar.
        var gone = WindowPlacement.Resolve(onSurround, [laptop], 1600, 900, 31);
        Assert.Equal(PlaceVerdict.DisplayGone, gone.Verdict);
        Assert.True(gone.Width <= LaptopUsable.Width && gone.Height + 31 <= LaptopUsable.Height);
        Assert.InRange(gone.Width / (double)gone.Height, 16 / 9.0 - 0.01, 16 / 9.0 + 0.01);
        Assert.Equal(LaptopUsable.X + (LaptopUsable.Width - gone.Width) / 2, gone.Rect!.Value.X);
        // Nothing kept on one screen: 1600x900 as it is, centred below its title bar.
        var first = WindowPlacement.Resolve(null, [single], 1600, 900, 31);
        Assert.Equal((PlaceVerdict.NoneSaved, 1600, 900), (first.Verdict, first.Width, first.Height));
        Assert.Equal(new DisplayRect(480, 31 + (1392 - 31 - 900) / 2, 1600, 900), first.Rect);
        // A window mostly off its display (a display moved): the fallback.
        var off = new WindowPlace(new DisplayRect(2300, 1000, 1280, 720), "Screen", false);
        Assert.Equal(PlaceVerdict.OffDisplay, WindowPlacement.Resolve(off, [single], 1600, 900, 31).Verdict);
    }

    // ---- what a full screen must be ----

    [Fact]
    public void A_full_screen_is_checked_to_cover_its_whole_display()
    {
        WindowFacts Facts(bool full, DisplayRect window) =>
            new(full, full ? "fullscreen" : "", window, new ScreenMode(window.Width, window.Height), null, "NVIDIA Surround (display 1)",
                SurroundBounds, SurroundUsable, Surround, Surround);
        Assert.Null(WindowReport.Fault(true, Facts(true, SurroundBounds)));
        Assert.Contains("usable bounds", WindowReport.Fault(true, Facts(true, SurroundUsable)));
        Assert.Equal("it is not full screen", WindowReport.Fault(true, Facts(false, SurroundBounds)));
        Assert.Null(WindowReport.Fault(false, Facts(false, new DisplayRect(2560, 0, 2560, 1440))));
    }

    // ---- what the window keeps, and how it is read ----

    [Fact]
    public void The_window_keys_are_read_without_regard_to_case_and_the_last_wins()
    {
        var keys = new WindowKeys([new("FullScreen", "off"), new("fullscreenmode", "2560x1440"), new("worldsize", "1280x720"), new("fullscreen", "on"), new("Maximised", "Yes")]);
        Assert.Equal("on", keys["FULLSCREEN"]);
        Assert.Equal(new FullScreenSetting(true, new ScreenMode(2560, 1440)), FullScreen.Read(keys));
        Assert.Equal(new ScreenMode(1280, 720), FullScreen.ReadWorldSize(keys));
        Assert.True(WindowKeys.IsTrue(keys["maximised"]));
        Assert.Null(keys["borderless"]);
        Assert.Equal(FullScreen.Default, FullScreen.Read(WindowKeys.None));
        Assert.Null(Borderless.Read(WindowKeys.None));
        Assert.Null(WindowPlacement.Read(WindowKeys.None));
        Assert.Equal(new ScreenMode(2560, 1440), Borderless.Read(new WindowKeys([new("borderless", "2560x1440")])));
    }

    /// <summary>Settings in memory, as a test's: what the rules write lands in <c>[window]</c>'s keys.</summary>
    sealed class Kept : IWindowSettings
    {
        readonly List<KeyValuePair<string, string>> lines = [];
        public WindowKeys Read() => new(lines);
        public void Write(IEnumerable<KeyValuePair<string, string>> values) => lines.AddRange(values);
    }

    [Fact]
    public void What_the_rules_write_reads_back_as_written()
    {
        var kept = new Kept();
        FullScreen.WriteOn(kept, false);
        FullScreen.WriteMode(kept, new ScreenMode(2560, 1440));
        FullScreen.WriteWorldSize(kept, new ScreenMode(1280, 720));
        FullScreen.WriteBorderless(kept, true);
        WindowPlacement.Write(kept, new WindowPlace(new DisplayRect(2560, 31, 1920, 1080), "NVIDIA Surround", true));
        var keys = kept.Read();
        Assert.Equal(new FullScreenSetting(false, new ScreenMode(2560, 1440)), FullScreen.Read(keys));
        Assert.Equal(new ScreenMode(1280, 720), FullScreen.ReadWorldSize(keys));
        Assert.True(FullScreen.ReadBorderless(keys));
        Assert.Equal(new WindowPlace(new DisplayRect(2560, 31, 1920, 1080), "NVIDIA Surround", true), WindowPlacement.Read(keys));
        Assert.Equal("NVIDIA Surround", Borderless.KeptDisplay(keys));
        // Nothing is kept for a host given no settings.
        FullScreen.WriteOn(NoWindowSettings.Instance, true);
        Assert.Equal(0, NoWindowSettings.Instance.Read().Count);
    }

    // ---- how the window starts on each desktop ----

    [Fact]
    public void A_first_start_is_borderless_full_screen_at_each_desktops_own_size()
    {
        var start = FullScreen.Start(FullScreen.Read(WindowKeys.None), replaying: false, size: null, harness: false, windowFlag: false, fullscreenFlag: false);
        Assert.Equal((true, true, true, (ScreenMode?)null), (start.Restore, start.Keep, start.Full, start.Mode));
        foreach (var desk in new[] { Surround, Single, Laptop })
            Assert.False(Picture.HasBars(desk, Picture.RectOn(desk, FullScreen.WorldSizeOn(desk, FullScreen.ReadWorldSize(WindowKeys.None)))));
        // His big PC's [window] borderless = 2560x1440 is over everything but the command line's own switches.
        var keys = new WindowKeys([new("borderless", "2560x1440")]);
        var bare = Borderless.Start(start, Borderless.Read(keys), null, replaying: false, sizeFlag: false, windowFlag: false, fullscreenFlag: false);
        Assert.Equal(new ScreenMode(2560, 1440), bare.Borderless);
        Assert.Null(Borderless.Start(start, Borderless.Read(keys), null, replaying: false, sizeFlag: true, windowFlag: false, fullscreenFlag: false).Borderless);
        Assert.Null(Borderless.Start(start, Borderless.Read(keys), "off", replaying: false, sizeFlag: false, windowFlag: false, fullscreenFlag: false).Borderless);
        Assert.Null(Borderless.Start(start, Borderless.Read(keys), "2560x1440", replaying: true, sizeFlag: false, windowFlag: false, fullscreenFlag: false).Borderless);
    }
}
