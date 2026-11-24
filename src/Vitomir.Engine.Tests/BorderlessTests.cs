// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The borderless window (ours).** A wide desktop can be one 7680x1440 desktop (three screens
/// Windows sees as a single display) with the task bar at the far right; the aim is the game
/// run windowed and centred at 2560x1440 with no title bar, whatever the task bar's position, on top of the task
/// bar. These pin <see cref="Borderless"/>: what <c>[window] borderless</c> and <c>--borderless</c> read as,
/// every row of what decides between borderless and the window as it always was
/// (<see cref="FullScreen.Start"/>, pinned by <see cref="FullScreenTests"/>), and the centring on the
/// display's whole bounds with his numbers. The SDL side (<c>Vitomir.Platform.Render/Windowing/BorderlessHost.cs</c>) — the window
/// without decorations, its place, on top while it has the focus — needs a real desktop and is not here.
/// </summary>
public class BorderlessTests
{
    static readonly ScreenMode Wide = new(2560, 1440);

    /// <summary>The window as it always was, for a run of each kind.</summary>
    static WindowStart AsToday(bool replaying = false, ScreenMode? size = null, bool harness = false, bool window = false, bool fullscreen = false,
                               string ini = "") =>
        FullScreen.Start(FullScreen.Read(IniFile.Parse(ini).Window()), replaying, size, harness, window, fullscreen);

    static WindowStart Start(string ini = "", string? flag = null, bool replaying = false, ScreenMode? size = null, bool harness = false,
                             bool window = false, bool fullscreen = false) =>
        Borderless.Start(AsToday(replaying, size, harness, window, fullscreen, ini), Borderless.Read(IniFile.Parse(ini).Window()), flag, replaying,
                         size is not null, window, fullscreen);

    const string Kept = "[window]\nborderless = 2560x1440\n";

    // ---- what is written ----

    [Fact]
    public void The_ini_key_reads_a_size_and_off_and_nothing()
    {
        Assert.Equal(Wide, Borderless.Read(IniFile.Parse(Kept).Window()));
        Assert.Equal(new ScreenMode(1920, 1080), Borderless.Read(IniFile.Parse("[window]\nborderless = 1920 x 1080\n").Window()));
        Assert.Null(Borderless.Read(IniFile.Parse("[window]\nborderless = off\n").Window()));
        Assert.Null(Borderless.Read(IniFile.Parse("[window]\nfullscreen = on\n").Window()));
        Assert.Null(Borderless.Read(IniFile.Parse("").Window()));
    }

    [Theory]
    [InlineData("wide")]
    [InlineData("2560")]
    [InlineData("320x200")]      // smaller than the smallest mode read
    [InlineData("desktop")]      // a size, not the desktop's: that is `fullscreenmode = desktop`
    [InlineData("on")]
    public void A_value_that_is_not_a_size_reads_as_none(string value)
    {
        Assert.False(Borderless.Parse(value, out var size));
        Assert.Null(size);
        Assert.Null(Borderless.Read(IniFile.Parse($"[window]\nborderless = {value}\n").Window()));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("false")]
    public void Off_reads_and_is_no_size(string value)
    {
        Assert.True(Borderless.Parse(value, out var size));
        Assert.Null(size);
    }

    // ---- what decides, row by row ----

    [Fact]
    public void With_nothing_said_the_window_is_as_it_always_was()
    {
        Assert.Equal(AsToday(), Start());
        Assert.Null(Start().Borderless);
        Assert.True(Start().Full);
    }

    [Fact]
    public void The_ini_key_makes_an_ordinary_run_borderless_never_restored_kept_or_full_screen()
    {
        var start = Start(Kept);
        Assert.Equal(Wide, start.Borderless);
        Assert.False(start.Restore);
        Assert.False(start.Keep);
        Assert.False(start.Full);
        Assert.Equal("[window] borderless", start.Why);
    }

    [Fact]
    public void Borderless_wins_over_full_screen_when_the_ini_says_both()
    {
        var start = Start("[window]\nfullscreen = on\nfullscreenmode = 1600x900\nborderless = 2560x1440\n");
        Assert.Equal(Wide, start.Borderless);
        Assert.False(start.Full);
    }

    [Fact]
    public void The_ini_key_makes_a_harness_run_borderless_too()
    {
        Assert.Equal(Wide, Start(Kept, harness: true).Borderless);
    }

    [Fact]
    public void A_size_on_the_command_line_is_a_window_of_that_size_whatever_the_ini_says()
    {
        var start = Start(Kept, size: new ScreenMode(960, 540));
        Assert.Null(start.Borderless);
        Assert.Equal(AsToday(size: new ScreenMode(960, 540), ini: Kept), start);
        Assert.Equal("--size", start.Why);
    }

    [Fact]
    public void Window_on_the_command_line_is_the_window_where_it_was_left_whatever_the_ini_says()
    {
        var start = Start(Kept, window: true);
        Assert.Null(start.Borderless);
        Assert.True(start.Restore);
        Assert.Equal("--window", start.Why);
    }

    [Fact]
    public void Fullscreen_on_the_command_line_is_full_screen_whatever_the_ini_says()
    {
        var start = Start(Kept, fullscreen: true);
        Assert.Null(start.Borderless);
        Assert.True(start.Full);
        Assert.Equal("--fullscreen", start.Why);
    }

    [Fact]
    public void The_flag_makes_one_run_borderless_with_nothing_in_the_ini()
    {
        var start = Start(flag: "2560x1440");
        Assert.Equal(Wide, start.Borderless);
        Assert.False(start.Keep);
        Assert.Equal("--borderless", start.Why);
    }

    [Fact]
    public void The_flags_size_is_taken_over_the_inis()
    {
        Assert.Equal(new ScreenMode(1920, 1080), Start(Kept, flag: "1920x1080").Borderless);
    }

    [Fact]
    public void The_flag_wins_over_fullscreen_window_and_size_on_the_same_command_line()
    {
        Assert.Equal(Wide, Start(flag: "2560x1440", fullscreen: true).Borderless);
        Assert.False(Start(flag: "2560x1440", fullscreen: true).Full);
        Assert.Equal(Wide, Start(flag: "2560x1440", window: true).Borderless);
        Assert.Equal(Wide, Start(flag: "2560x1440", size: new ScreenMode(960, 540)).Borderless);
        Assert.Equal(Wide, Start(flag: "2560x1440", harness: true).Borderless);
    }

    [Fact]
    public void The_flag_off_is_the_window_as_it_always_was_whatever_the_ini_says()
    {
        Assert.Equal(AsToday(ini: Kept), Start(Kept, flag: "off"));
        Assert.Null(Start(Kept, flag: "off").Borderless);
    }

    [Fact]
    public void A_flag_that_does_not_read_is_passed_over_and_the_ini_decides()
    {
        Assert.Equal(Wide, Start(Kept, flag: "wide").Borderless);
        Assert.Null(Start(flag: "wide").Borderless);
        Assert.Equal(AsToday(), Start(flag: "wide"));
    }

    [Fact]
    public void A_replay_stays_a_window_at_its_tapes_size_whatever_is_said()
    {
        var replay = AsToday(replaying: true);
        Assert.Equal(replay, Start(Kept, replaying: true));
        Assert.Equal(replay, Start(Kept, flag: "2560x1440", replaying: true));
        Assert.Null(Start(flag: "2560x1440", replaying: true).Borderless);
        Assert.False(replay.Full);
    }

    // ---- where it goes ----

    [Fact]
    public void On_a_7680x1440_desktop_a_2560x1440_window_is_at_2560_0()
    {
        var surround = new DisplayRect(0, 0, 7680, 1440);
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), Borderless.Centre(surround, Wide));
    }

    [Fact]
    public void The_task_bars_place_does_not_move_it()
    {
        // The whole bounds are what is centred on; the usable bounds (less a task bar 72 wide at the far right) are never asked.
        var displays = new[] { new DisplayBounds("Surround", new DisplayRect(0, 0, 7680, 1440), Primary: true) };
        Assert.Equal(new DisplayRect(2560, 0, 2560, 1440), Borderless.Place(Wide, displays, keptDisplay: null));
    }

    [Fact]
    public void A_smaller_window_is_centred_both_ways()
    {
        Assert.Equal(new DisplayRect(320, 180, 1280, 720), Borderless.Centre(new DisplayRect(0, 0, 1920, 1080), new ScreenMode(1280, 720)));
        Assert.Equal(new DisplayRect(1921, 0, 1919, 1080), Borderless.Centre(new DisplayRect(1920, 0, 1921, 1080), new ScreenMode(1919, 1080)));
    }

    [Fact]
    public void A_display_left_of_or_above_the_primary_is_centred_on_in_its_own_coordinates()
    {
        Assert.Equal(new DisplayRect(-1600, -180, 1280, 720), Borderless.Centre(new DisplayRect(-1920, -360, 1920, 1080), new ScreenMode(1280, 720)));
    }

    [Fact]
    public void A_size_larger_than_the_display_is_cut_to_the_display_and_centred()
    {
        var laptop = new DisplayRect(0, 0, 1920, 1080);
        Assert.Equal(new DisplayRect(0, 0, 1920, 1080), Borderless.Centre(laptop, Wide));
        Assert.Equal(new DisplayRect(0, 180, 1920, 720), Borderless.Centre(laptop, new ScreenMode(2560, 720)));
    }

    [Fact]
    public void It_opens_on_the_display_the_window_was_kept_on_else_the_primary_else_the_first()
    {
        var left = new DisplayBounds("Left", new DisplayRect(-1920, 0, 1920, 1080), Primary: false);
        var main = new DisplayBounds("Main", new DisplayRect(0, 0, 2560, 1440), Primary: true);
        Assert.Same(left, Borderless.Display([left, main], "Left"));
        Assert.Same(main, Borderless.Display([left, main], null));
        Assert.Same(main, Borderless.Display([left, main], "Unplugged"));
        Assert.Same(left, Borderless.Display([left], null));
        Assert.Null(Borderless.Display([], "Left"));
        Assert.Null(Borderless.Place(Wide, [], null));
        Assert.Equal(new DisplayRect(-1600, 180, 1280, 720), Borderless.Place(new ScreenMode(1280, 720), [left, main], "Left"));
    }

    [Fact]
    public void The_kept_display_is_the_window_sections()
    {
        Assert.Equal("Generic PnP Monitor", Borderless.KeptDisplay(IniFile.Parse("[window]\nx = 82\ndisplay = Generic PnP Monitor\n").Window()));
        Assert.Null(Borderless.KeptDisplay(IniFile.Parse("[window]\nborderless = 2560x1440\n").Window()));
    }

    [Fact]
    public void It_is_on_top_only_while_it_has_the_focus()
    {
        Assert.True(Borderless.OnTop(focused: true));
        Assert.False(Borderless.OnTop(focused: false));
    }

    [Fact]
    public void The_console_says_where_it_went_and_why()
    {
        Assert.Equal("window: borderless 2560x1440 at 2560,0 on Surround ([window] borderless)",
                     Borderless.Describe(Wide, new DisplayRect(2560, 0, 2560, 1440), "Surround", "[window] borderless"));
        Assert.Equal("window: borderless 1920x1080 at 0,0 on Laptop, the display being smaller than the 2560x1440 asked (--borderless)",
                     Borderless.Describe(Wide, new DisplayRect(0, 0, 1920, 1080), "Laptop", "--borderless"));
        Assert.Equal("window: borderless 2560x1440, placed by the system (no display reported) (--borderless)",
                     Borderless.Describe(Wide, null, "", "--borderless"));
    }
}
