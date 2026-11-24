// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The desktop behind a full screen (ours, <see cref="DesktopRoom"/>), on the figures of a real run:
/// a desktop of 7680x1440 with a task bar 213 wide at its right, the game started
/// in a video mode of 1600x900. The Window row then offered nothing over 1366x768, because the room was
/// measured in the mode and not on the desktop; and the task bar lay over the game.
/// </summary>
public class DesktopRoomTests
{
    static readonly ScreenMode Desk = new(7680, 1440);

    [Fact]
    public void In_a_video_mode_of_its_own_the_room_is_the_desktops_less_the_task_bar()
    {
        // As the log had it: bounds 1600x900 at 0,0, usable 1387x900 at 0,0.
        var room = DesktopRoom.UsableOnDesktop(Desk, new DisplayRect(0, 0, 1600, 900), new DisplayRect(0, 0, 1387, 900));
        Assert.Equal(new DisplayRect(0, 0, 7680 - 213, 1440), room);
        // So a window of 1920x1080 or 2560x1080 has room, where the mode's own bounds stopped at 1366x768.
        Assert.Contains(new ScreenMode(1920, 1080), FullScreen.SizesThatFit(new ScreenMode(room.Width - 16, room.Height - 39), null));
        Assert.DoesNotContain(new ScreenMode(1920, 1080), FullScreen.SizesThatFit(new ScreenMode(1387 - 16, 900 - 39), null));
    }

    [Fact]
    public void On_the_desktop_already_the_usable_bounds_are_the_room()
    {
        var usable = new DisplayRect(0, 0, 7467, 1440);
        Assert.Equal(usable, DesktopRoom.UsableOnDesktop(Desk, new DisplayRect(0, 0, 7680, 1440), usable));
        // A task bar along the bottom of a laptop's 1600x900, and one on the left.
        Assert.Equal(new DisplayRect(0, 0, 1600, 852), DesktopRoom.UsableOnDesktop(new ScreenMode(1600, 900), new DisplayRect(0, 0, 1600, 900), new DisplayRect(0, 0, 1600, 852)));
        Assert.Equal(new DisplayRect(62, 0, 1858, 1080), DesktopRoom.UsableOnDesktop(new ScreenMode(1920, 1080), new DisplayRect(0, 0, 1920, 1080), new DisplayRect(62, 0, 1858, 1080)));
    }

    [Fact]
    public void A_margin_too_thick_for_a_task_bar_is_stale_and_left_out()
    {
        // Just after the display left 1600x900: bounds 7680x1440, usable 7680x540 — the old mode's height still in it.
        Assert.Equal(new DisplayRect(0, 0, 7680, 1440),
                     DesktopRoom.UsableOnDesktop(Desk, new DisplayRect(0, 0, 7680, 1440), new DisplayRect(0, 0, 7680, 540)));
    }

    [Fact]
    public void A_display_that_does_not_start_at_the_corner_keeps_its_place()
    {
        var room = DesktopRoom.UsableOnDesktop(new ScreenMode(2560, 1440), new DisplayRect(1920, 0, 1280, 720), new DisplayRect(1920, 0, 1280, 696));
        Assert.Equal(new DisplayRect(1920, 0, 2560, 1440 - 24), room);
    }

    [Fact]
    public void Only_a_full_screen_at_a_mode_of_its_own_with_the_keyboard_stays_on_top()
    {
        Assert.True(DesktopRoom.StaysOnTop(full: true, ownMode: true, keyboard: true));
        Assert.False(DesktopRoom.StaysOnTop(full: true, ownMode: true, keyboard: false));     // Alt+Tab
        Assert.False(DesktopRoom.StaysOnTop(full: true, ownMode: false, keyboard: true));     // borderless full screen
        Assert.False(DesktopRoom.StaysOnTop(full: false, ownMode: false, keyboard: true));    // a window
    }
}
