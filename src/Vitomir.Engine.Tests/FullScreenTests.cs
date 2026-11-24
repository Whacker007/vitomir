// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Full screen, as 1.1 starts** ("I can't force the game into fullscreen mode. The old
/// game always assumed fullscreen."). 1.1 is full screen at its video mode unless started with
/// <c>-window</c>; the rest is ours — <c>[window] fullscreen</c> and <c>fullscreenmode</c>, the
/// first start's default (the desktop's own size, with no change of video mode —; it was
/// 1600x900 until then, which a file from before still means), <c>--window</c> and <c>--fullscreen</c>, and Alt+Enter. These pin the rules of
/// <see cref="FullScreen"/> and the toggle's place among the key bindings; the SDL side
/// (<c>Vitomir.Platform.Render/Windowing/FullScreenHost.cs</c>) cannot go full screen in the cloud's offscreen driver and is not here.
/// </summary>
public class FullScreenTests
{
    static FullScreenSetting Kept(string ini) => FullScreen.Read(IniFile.Parse(ini).Window());

    static WindowStart Start(FullScreenSetting kept, bool replaying = false, ScreenMode? size = null, bool harness = false,
                             bool window = false, bool fullscreen = false) =>
        FullScreen.Start(kept, replaying, size, harness, window, fullscreen);

    // ---- what the ini keeps ----

    /// <summary>
    /// **A first start is full screen at the desktop's own resolution.** A settings file with
    /// neither full-screen key — an empty one, or one with other sections only — reads as full screen
    /// with no mode of its own: borderless at the desktop's size, no change of video mode; and with no
    /// <c>worldsize</c> the world is the desktop's size too, so the picture is the whole screen.
    /// </summary>
    [Fact]
    public void Nothing_kept_is_borderless_full_screen_at_the_desktops_own_size()
    {
        foreach (var text in new[] { "", "[sound]\nvolume = 3\n", "[window]\nx = 5\ny = 6\n" })
        {
            var ini = IniFile.Parse(text).Window();
            var kept = FullScreen.Read(ini);
            Assert.Equal(FullScreen.Default, kept);
            Assert.True(kept.On);
            Assert.Null(kept.Mode);
            Assert.Null(FullScreen.ReadWorldSize(ini));
            var start = Start(kept);
            Assert.Equal((true, true, true), (start.Restore, start.Keep, start.Full));
            Assert.Null(start.Mode);
        }
        // Whatever the desktop, nothing picked is the whole of it: no bars.
        foreach (var desk in new ScreenMode[] { new(7680, 1440), new(1920, 1080), new(1600, 900), new(1280, 1024) })
            Assert.False(Picture.HasBars(desk, Picture.RectOn(desk, FullScreen.ReadWorldSize(IniFile.Parse("").Window()))));
    }

    /// <summary>
    /// **An existing settings file is read exactly as before.** One that keeps <c>fullscreen</c> and names
    /// no mode — Alt+Enter wrote the state alone until then — still means 1600x900, the default it
    /// was written under; so does a mode that does not read; and a mode alone is full screen at it.
    /// </summary>
    [Fact]
    public void A_file_from_before_that_names_no_mode_still_means_1600x900()
    {
        Assert.Equal(new FullScreenSetting(true, new ScreenMode(1600, 900)), Kept("[window]\nfullscreen = on\n"));
        Assert.Equal(new FullScreenSetting(false, new ScreenMode(1600, 900)), Kept("[window]\nfullscreen = off\n"));
        Assert.Equal(new FullScreenSetting(true, new ScreenMode(1600, 900)), Kept("[window]\nfullscreenmode = soon\n"));
        Assert.Equal(new FullScreenSetting(true, new ScreenMode(1280, 720)), Kept("[window]\nfullscreenmode = 1280x720\n"));
        Assert.Equal(new FullScreenSetting(true, null), Kept("[window]\nfullscreenmode = desktop\n"));
        Assert.Equal(new FullScreenSetting(false, null), Kept("[window]\nfullscreen = off\nfullscreenmode = desktop\n"));
        Assert.Equal(new ScreenMode(1600, 900), FullScreen.DefaultMode);
    }

    /// <summary>His <c>vitomir.ini</c> as it was when he stopped: the older window section and nothing of full screen — it starts full screen, at the desktop's own size now.</summary>
    [Fact]
    public void An_older_ini_starts_full_screen_and_still_keeps_its_window_to_come_back_to()
    {
        var ini = IniFile.Parse("[window]\nx = 82\ny = 31\nwidth = 1436\nheight = 808\ndisplay = Generic PnP Monitor\nmaximised = 1\n").Window();
        var start = Start(FullScreen.Read(ini));
        Assert.True(start.Full);
        Assert.True(start.Restore);
        Assert.True(start.Keep);
        Assert.Null(start.Mode);
        var place = WindowPlacement.Read(ini);
        Assert.NotNull(place);
        Assert.Equal(new DisplayRect(82, 31, 1436, 808), place!.Rect);
        Assert.True(place.Maximised);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("No", false)]
    [InlineData("false", false)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("anything", true)]
    public void Fullscreen_reads_as_on_unless_it_says_off(string value, bool on) =>
        Assert.Equal(on, Kept($"[window]\nfullscreen = {value}\n").On);

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" 1280 x 720 ", 1280, 720)]
    [InlineData("800X600", 800, 600)]
    [InlineData("320x200", 1600, 900)]      // smaller than 1.1's smallest: the default
    [InlineData("big", 1600, 900)]
    [InlineData("1600x", 1600, 900)]
    public void The_mode_reads_as_width_x_height_or_else_the_default(string value, int w, int h) =>
        Assert.Equal(new ScreenMode(w, h), Kept($"[window]\nfullscreenmode = {value}\n").Mode);

    [Fact]
    public void Desktop_is_full_screen_at_the_desktops_own_size()
    {
        Assert.Null(Kept("[window]\nfullscreenmode = desktop\n").Mode);
        Assert.True(FullScreen.ParseMode("Desktop", out var m));
        Assert.Null(m);
        Assert.Equal("desktop", FullScreen.FormatMode(null));
        Assert.Equal("1600x900", FullScreen.FormatMode(new ScreenMode(1600, 900)));
    }

    [Fact]
    public void Writing_the_state_and_the_mode_leaves_the_windows_place_and_every_other_line_alone()
    {
        string folder = Path.Combine(Path.GetTempPath(), "vitomir-fullscreen-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "vitomir.ini");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "[options]\ngamma = 1.2\n\n[window]\n; mine\nx = 82\ny = 31\nwidth = 1436\nheight = 808\ndisplay = Generic PnP Monitor\nmaximised = 1\n");
            var settings = new IniWindowSettings(path);
            FullScreen.WriteOn(settings, false);
            FullScreen.WriteMode(settings, new ScreenMode(1920, 1080));
            var ini = IniFile.Read(path);
            Assert.Equal(new FullScreenSetting(false, new ScreenMode(1920, 1080)), FullScreen.Read(ini.Window()));
            Assert.Equal(new DisplayRect(82, 31, 1436, 808), WindowPlacement.Read(ini.Window())!.Rect);
            Assert.Equal("1.2", ini["options.gamma"]);
            Assert.Contains("; mine", File.ReadAllText(path));
            FullScreen.WriteOn(settings, true);
            FullScreen.WriteMode(settings, null);
            Assert.Equal(new FullScreenSetting(true, null), FullScreen.Read(IniFile.Read(path).Window()));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // ---- how the window starts ----

    [Fact]
    public void A_plain_start_follows_the_ini_and_keeps_what_changes()
    {
        var on = Start(FullScreen.Default);
        Assert.Equal((true, true, true), (on.Restore, on.Keep, on.Full));
        var off = Start(new FullScreenSetting(false, new ScreenMode(1280, 720)));
        Assert.Equal((true, true, false), (off.Restore, off.Keep, off.Full));
        var desktop = Start(new FullScreenSetting(true, null));
        Assert.True(desktop.Full);
        Assert.Null(desktop.Mode);
    }

    /// <summary>1.1's <c>-window</c> as ours: a window where it was left, and nothing written — it wins over <c>--fullscreen</c>.</summary>
    [Fact]
    public void Window_flag_is_a_window_where_it_was_left_and_writes_nothing()
    {
        var s = Start(FullScreen.Default, window: true);
        Assert.Equal((true, false, false), (s.Restore, s.Keep, s.Full));
        Assert.False(Start(FullScreen.Default, window: true, fullscreen: true).Full);
        Assert.Equal(0x2000u, FullScreen.WindowBit);
    }

    [Fact]
    public void Fullscreen_flag_forces_it_over_an_ini_that_says_off()
    {
        var s = Start(new FullScreenSetting(false, new ScreenMode(1280, 720)), fullscreen: true);
        Assert.True(s.Full);
        Assert.True(s.Keep);
        Assert.Equal(new ScreenMode(1280, 720), s.Mode);
    }

    /// <summary>A replay's window is sized by its tape (<c>interactive … size=</c> and the recorded resizes): never full screen, never kept, whatever its launch said.</summary>
    [Fact]
    public void A_replay_is_a_window_never_kept_even_of_a_full_screen_recording()
    {
        var s = Start(FullScreen.Default, replaying: true, fullscreen: true);
        Assert.Equal((false, false, false), (s.Restore, s.Keep, s.Full));
    }

    [Fact]
    public void Size_is_a_window_of_that_size_unless_fullscreen_asks_for_it_as_the_mode()
    {
        var s = Start(FullScreen.Default, size: new ScreenMode(640, 360));
        Assert.Equal((false, false, false), (s.Restore, s.Keep, s.Full));
        var f = Start(FullScreen.Default, size: new ScreenMode(1280, 720), fullscreen: true);
        Assert.Equal((false, false, true), (f.Restore, f.Keep, f.Full));
        Assert.Equal(new ScreenMode(1280, 720), f.Mode);
    }

    [Fact]
    public void A_harness_run_is_a_window_and_writes_nothing()
    {
        var s = Start(FullScreen.Default, harness: true);
        Assert.Equal((true, false, false), (s.Restore, s.Keep, s.Full));
        var f = Start(FullScreen.Default, harness: true, fullscreen: true);
        Assert.Equal((false, true), (f.Keep, f.Full));
    }

    // ---- the Display screen's list ----

    [Fact]
    public void The_modes_are_the_displays_once_each_by_size_with_the_one_in_use_smallest_first()
    {
        ScreenMode[] offered = [new(1920, 1080), new(1920, 1080), new(1600, 900), new(1280, 720), new(1600, 900), new(800, 600)];
        var list = FullScreen.ModeList(offered, new ScreenMode(1600, 900));
        Assert.Equal([new(800, 600), new(1280, 720), new(1600, 900), new(1920, 1080)], list);
        // The one in use is always there, even when the display's own list lacks it (the desktop's size, say).
        Assert.Contains(new ScreenMode(1366, 768), FullScreen.ModeList(offered, new ScreenMode(1366, 768)));
        Assert.Equal([new ScreenMode(1024, 768)], FullScreen.ModeList([], new ScreenMode(1024, 768)));
    }

    [Fact]
    public void What_became_of_a_request_is_said_plainly()
    {
        var m = new ScreenMode(1600, 900);
        Assert.Equal("window: full screen at 1600x900 on Laptop", FullScreen.Describe(FullScreenLanded.Exclusive, m, m, "Laptop"));
        Assert.Contains("the closest it offers to 1600x900", FullScreen.Describe(FullScreenLanded.Exclusive, m, new ScreenMode(1680, 1050), "Laptop"));
        Assert.Contains("offers no mode close to 1600x900", FullScreen.Describe(FullScreenLanded.Desktop, m, new ScreenMode(1366, 768), "Laptop"));
        Assert.Contains("desktop's size, 1920x1080", FullScreen.Describe(FullScreenLanded.Desktop, null, new ScreenMode(1920, 1080), "Laptop"));
        Assert.Contains("a window", FullScreen.Describe(FullScreenLanded.Window, m, null, "Laptop"));
    }

    // ---- Alt+Enter ----

    [Fact]
    public void The_toggle_is_alt_enter_remappable_and_on_the_key_page()
    {
        var info = KeyBindings.Of(KeyCommand.FullScreen);
        Assert.Equal("fullscreen", info.Id);
        Assert.Equal(CommandKind.Press, info.Kind);
        Assert.Equal([new KeyChord(KeyNames.Return, KeyMods.Alt)], info.Defaults);
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.FullScreen));
        Assert.True(KeyBindings.IsHostFirst(KeyCommand.FullScreen));

        var keys = new KeyBindings();
        keys.Apply(IniFile.Parse("[keys]\nfullscreen = F11\n"));
        Assert.True(keys.Is(KeyCommand.FullScreen, KeyNames.F(11), KeyMods.None));
        Assert.False(keys.Is(KeyCommand.FullScreen, KeyNames.Return, KeyMods.Alt));
        Assert.Contains(keys.Values(), v => v.Key == "fullscreen" && v.Value == "F11");
    }

    /// <summary>
    /// **Plain Enter stays 1.1's.** Enter opens a network game's chat line (which tests no Alt), sends
    /// the console's line and closes a text box; the toggle takes Enter only with Alt held, and Enter is not
    /// counted among 1.1's keys whose command has moved (which the binding layer swallows), nor is the toggle
    /// ever what a key press is handed on as.
    /// </summary>
    [Fact]
    public void Plain_enter_is_not_the_toggle_and_is_never_swallowed_for_it()
    {
        var keys = new KeyBindings();
        Assert.True(keys.Is(KeyCommand.FullScreen, KeyNames.Return, KeyMods.Alt));
        Assert.True(keys.Is(KeyCommand.FullScreen, KeyNames.Return, KeyMods.Alt | KeyMods.Shift));
        Assert.False(keys.Is(KeyCommand.FullScreen, KeyNames.Return, KeyMods.None));
        Assert.False(keys.Is(KeyCommand.FullScreen, KeyNames.KeypadEnter, KeyMods.Alt));
        Assert.False(KeyBindings.AnyDefault(KeyNames.Return));
        Assert.DoesNotContain(keys.Pressed(KeyNames.Return, KeyMods.Alt, driving: true, camera: true), p => p.Command == KeyCommand.FullScreen);
    }

    /// <summary>With <c>[qol] keys</c> off the keys are 1.1's own; the toggle changes the window, not the game, and keeps its key.</summary>
    [Fact]
    public void The_toggle_keeps_its_key_with_the_keys_switch_off()
    {
        Assert.True(KeyBindings.Original().Is(KeyCommand.FullScreen, KeyNames.Return, KeyMods.Alt));
    }
    /// <summary>
    /// **The size picked for borderless full screen, as it is kept.** Smaller than the desktop either
    /// way: that size; the desktop's own, a larger one, or none: null, written <c>desktop</c>. What the size
    /// does — the picture's shape, the world's size — is <see cref="Picture"/>'s (<c>PictureTests</c>).
    /// </summary>
    [Fact]
    public void The_world_size_is_a_size_smaller_than_the_desktop_or_none()
    {
        var desk = new ScreenMode(1600, 900);
        Assert.Equal(new ScreenMode(1280, 720), FullScreen.WorldSizeOn(desk, new ScreenMode(1280, 720)));
        Assert.Equal(new ScreenMode(1280, 900), FullScreen.WorldSizeOn(desk, new ScreenMode(1280, 900)));
        Assert.Null(FullScreen.WorldSizeOn(desk, desk));
        Assert.Null(FullScreen.WorldSizeOn(desk, new ScreenMode(1920, 1080)));
        Assert.Null(FullScreen.WorldSizeOn(desk, new ScreenMode(1280, 1024)));
        Assert.Null(FullScreen.WorldSizeOn(desk, null));
    }

    /// <summary><c>[window] worldsize</c> reads as a size, and as none when absent, <c>desktop</c> or unreadable.</summary>
    [Fact]
    public void The_world_size_is_read_from_the_window_section()
    {
        WindowKeys Ini(string body) => IniFile.Parse("[window]\n" + body).Window();
        Assert.Equal(new ScreenMode(1280, 720), FullScreen.ReadWorldSize(Ini("worldsize = 1280x720")));
        Assert.Null(FullScreen.ReadWorldSize(Ini("worldsize = desktop")));
        Assert.Null(FullScreen.ReadWorldSize(Ini("worldsize = soon")));
        Assert.Null(FullScreen.ReadWorldSize(Ini("fullscreen = on")));
    }

    /// <summary>
    /// **The grip of a window with no title bar.** A square in the top right corner, shown only with
    /// the pointer near the top edge; its boxes land in the interface's space where the square is.
    /// </summary>
    [Fact]
    public void The_grip_is_in_the_top_right_corner_and_shows_near_the_top_edge()
    {
        var r = WindowGrip.Rect(1280);
        Assert.Equal((1280 - WindowGrip.Margin - WindowGrip.Width, WindowGrip.Margin, WindowGrip.Width, WindowGrip.Height), r);
        Assert.True(WindowGrip.On(r.X, r.Y, 1280));
        Assert.True(WindowGrip.On(r.X + r.W - 1, r.Y + r.H - 1, 1280));
        Assert.False(WindowGrip.On(r.X - 1, r.Y, 1280));
        Assert.False(WindowGrip.On(r.X, r.Y + r.H, 1280));
        Assert.True(WindowGrip.Near(10, WindowGrip.NearRows - 1, 1280, 720));
        Assert.False(WindowGrip.Near(10, WindowGrip.NearRows, 1280, 720));
        Assert.False(WindowGrip.Near(-1, 5, 1280, 720));
        Assert.False(WindowGrip.Near(1280, 5, 1280, 720));
        var boxes = WindowGripMarks.Boxes(1280, 720).ToList();
        var back = boxes[0];
        Assert.False(back.Mark);
        // The window's own pixels, through whatever layout the sheet is in (UiLayout): the grip is the window's.
        var (gx, gy) = UiLayout.FromPicture(r.X, r.Y + r.H, 1280, 720);
        Assert.Equal(gx, back.X, 3);
        Assert.Equal(gy, back.Y, 3);
        var stretched = UiLayout.On(1280, 720, UiLayoutMode.Stretch);          // and stretched, where it always was
        Assert.Equal(r.X * 640f / 1280, (r.X - stretched.X) / stretched.ScaleX, 3);
        Assert.Equal(480f - (r.Y + r.H) * 480f / 720, (stretched.Bottom - (r.Y + r.H)) / stretched.ScaleY, 3);
        Assert.All(boxes.Skip(1), b => Assert.True(b.Mark && b.X >= back.X && b.X + b.W <= back.X + back.W + 0.001f
                                                   && b.Y >= back.Y && b.Y + b.H <= back.Y + back.H + 0.001f));
    }
}
