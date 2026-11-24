// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The picture's rectangle in borderless full screen (ours).** His
/// desktop is 7680x1440 — three screens the system sees as one display — and borderless full screen drew
/// across all three. Decided: the picture, world and interface together, takes the shape of the size
/// picked on the Display screen, as large as fits the desktop, centred, black everywhere else; the size
/// picked stays the size the world is drawn at; and the pointer is kept inside the picture. These pin
/// the rules of <see cref="Picture"/> on a three-screen desktop and on ordinary ones, the pointer's mapping at the
/// picture's corners and on a bar, and — by reading the source — that the game's loops ask the picture's
/// host for the window's size and the pointer rather than SDL, since one missed read is a pointer off by
/// the bar's width, which on such a desktop is 2,560 pixels. The SDL side (<c>Vitomir.Platform.Render/Windowing/PictureHost.cs</c>) is
/// seen in a harness run, not here.
/// </summary>
public class PictureTests
{
    static readonly ScreenMode WideDesktop = new(7680, 1440);

    // ---- the rectangle ----

    [Theory]
    [InlineData(2560, 1440, 2560, 0, 2560, 1440)]   // exactly the middle screen
    [InlineData(1280, 720, 2560, 0, 2560, 1440)]    // the same rectangle: the shape is what counts
    [InlineData(1920, 1080, 2560, 0, 2560, 1440)]
    [InlineData(1280, 1024, 2940, 0, 1800, 1440)]   // 5:4: 1800 wide
    [InlineData(1024, 768, 2880, 0, 1920, 1440)]    // 4:3
    [InlineData(3440, 1440, 2120, 0, 3440, 1440)]   // wider than one screen, narrower than three
    [InlineData(7680, 1440, 0, 0, 7680, 1440)]      // the desktop's own: all of it
    public void On_his_three_screens_the_picture_is_the_picked_shape_in_the_middle(int w, int h, int x, int y, int pw, int ph) =>
        Assert.Equal(new PictureRect(x, y, pw, ph), Picture.RectOn(WideDesktop, new ScreenMode(w, h)));

    [Fact]
    public void Nothing_picked_is_the_whole_desktop()
    {
        Assert.Equal(new PictureRect(0, 0, 7680, 1440), Picture.RectOn(WideDesktop, null));
        Assert.False(Picture.HasBars(WideDesktop, Picture.RectOn(WideDesktop, null)));
        Assert.False(Picture.HasBars(WideDesktop, Picture.RectOn(WideDesktop, WideDesktop)));
        Assert.True(Picture.HasBars(WideDesktop, Picture.RectOn(WideDesktop, new ScreenMode(2560, 1440))));
    }

    /// <summary>One rule on any screen: a 5:4 size on a 16:9 screen has bars at its sides (an earlier build drew a wide view at that size instead).</summary>
    [Fact]
    public void A_narrower_shape_on_a_wide_screen_has_bars_at_the_sides()
    {
        Assert.Equal(new PictureRect(285, 0, 1350, 1080), Picture.RectOn(new ScreenMode(1920, 1080), new ScreenMode(1280, 1024)));
        Assert.Equal(new PictureRect(200, 0, 1200, 900), Picture.RectOn(new ScreenMode(1600, 900), new ScreenMode(1024, 768)));
        // The same shape as the screen: no bars, whatever the size.
        Assert.Equal(new PictureRect(0, 0, 1920, 1080), Picture.RectOn(new ScreenMode(1920, 1080), new ScreenMode(1280, 720)));
        Assert.Equal(new PictureRect(0, 0, 1600, 900), Picture.RectOn(new ScreenMode(1600, 900), new ScreenMode(1600, 900)));
    }

    [Fact]
    public void A_wider_shape_on_a_taller_screen_has_bars_above_and_below()
    {
        Assert.Equal(new PictureRect(0, 152, 1280, 720), Picture.RectOn(new ScreenMode(1280, 1024), new ScreenMode(1280, 720)));
        Assert.Equal(new PictureRect(0, 60, 1920, 1080), Picture.RectOn(new ScreenMode(1920, 1200), new ScreenMode(1600, 900)));
        // The cloud's stand-in display, 1024x768, with a 16:9 size: 1024x576, 96 lines black above and below.
        Assert.Equal(new PictureRect(0, 96, 1024, 576), Picture.RectOn(new ScreenMode(1024, 768), new ScreenMode(1280, 720)));
    }

    /// <summary>A side that does not come out whole is rounded to the nearest pixel, and an odd pixel left over goes right or down.</summary>
    [Fact]
    public void A_side_that_is_not_whole_is_rounded_and_the_picture_stays_inside()
    {
        Assert.Equal(new PictureRect(2559, 0, 2561, 1440), Picture.RectOn(WideDesktop, new ScreenMode(1366, 768)));   // 2561.25
        Assert.Equal(new PictureRect(0, 99, 1024, 569), Picture.RectOn(new ScreenMode(1024, 768), new ScreenMode(900, 500)));   // 568.9
        foreach (var desk in new ScreenMode[] { WideDesktop, new(1920, 1080), new(1600, 900), new(1366, 768), new(1280, 1024), new(1024, 768) })
            foreach (var picked in FullScreen.WindowSizes)
            {
                var r = Picture.RectOn(desk, picked);
                Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.Width <= desk.Width && r.Y + r.Height <= desk.Height, $"{picked} on {desk}: {r}");
                Assert.True(r.Width == desk.Width || r.Height == desk.Height, $"{picked} on {desk} fills neither way: {r}");
                // The picked shape, to the pixel's rounding.
                Assert.InRange(r.Width * (double)picked.Height / picked.Width - r.Height, -1.0, 1.0);
                // Centred: the two bars differ by at most the odd pixel.
                Assert.InRange(desk.Width - r.Width - 2 * r.X, 0, 1);
                Assert.InRange(desk.Height - r.Height - 2 * r.Y, 0, 1);
            }
    }

    // ---- the world's size inside it ----

    /// <summary>The size picked stays the size the world is drawn at: stretched over the picture when smaller, the picture's own pixels otherwise.</summary>
    [Fact]
    public void The_world_is_drawn_at_the_size_picked_when_that_is_smaller_than_the_picture()
    {
        var middle = Picture.RectOn(WideDesktop, new ScreenMode(1280, 720));
        Assert.Equal(new ScreenMode(1280, 720), Picture.WorldSizeIn(middle, new ScreenMode(1280, 720)));
        Assert.Null(Picture.WorldSizeIn(middle, new ScreenMode(2560, 1440)));                       // the picture's own
        var fiveFour = Picture.RectOn(WideDesktop, new ScreenMode(1280, 1024));
        Assert.Equal(new ScreenMode(1280, 1024), Picture.WorldSizeIn(fiveFour, new ScreenMode(1280, 1024)));   // over 1800x1440
        Assert.Null(Picture.WorldSizeIn(new PictureRect(0, 0, 7680, 1440), null));
        // A size the picture is smaller than (a file written on a larger desktop): its shape, the picture's pixels.
        var small = Picture.RectOn(new ScreenMode(1024, 768), new ScreenMode(1280, 1024));
        Assert.Equal(new PictureRect(32, 0, 960, 768), small);
        Assert.Null(Picture.WorldSizeIn(small, new ScreenMode(1280, 1024)));
        // The same shape as the screen and smaller: the whole screen, the world stretched.
        Assert.Equal(new ScreenMode(1280, 720), Picture.WorldSizeIn(Picture.RectOn(new ScreenMode(1920, 1080), new ScreenMode(1280, 720)), new ScreenMode(1280, 720)));
    }

    // ---- the pointer ----

    [Fact]
    public void A_window_pixel_on_the_picture_is_the_pictures_less_the_bars_offset()
    {
        var r = Picture.RectOn(WideDesktop, new ScreenMode(2560, 1440));
        // The four corners of the picture.
        Assert.Equal((0f, 0f), Picture.FromWindow(r, 2560, 0));
        Assert.Equal((2559f, 0f), Picture.FromWindow(r, 5119, 0));
        Assert.Equal((0f, 1439f), Picture.FromWindow(r, 2560, 1439));
        Assert.Equal((2559f, 1439f), Picture.FromWindow(r, 5119, 1439));
        Assert.True(Picture.On(r, 2560, 0));
        Assert.True(Picture.On(r, 5119, 1439));
        // And back: the middle of the picture (the swivel lock's place) is the middle of the middle screen.
        Assert.Equal((3840f, 720f), Picture.ToWindow(r, 1280, 720));
        Assert.Equal((1280f, 720f), Picture.FromWindow(r, 3840, 720));
        // Bars above and below: the offset is in y.
        var tall = Picture.RectOn(new ScreenMode(1280, 1024), new ScreenMode(1280, 720));
        Assert.Equal((10f, 0f), Picture.FromWindow(tall, 10, 152));
        Assert.Equal((10f, 719f), Picture.FromWindow(tall, 10, 871));
        Assert.False(Picture.On(tall, 10, 151));
        Assert.False(Picture.On(tall, 10, 872));
    }

    /// <summary>A pixel on a bar is not on the picture — a click or a wheel turn there does nothing — and as a place it is the picture's nearest edge.</summary>
    [Fact]
    public void A_pixel_on_a_bar_is_off_the_picture_and_held_to_its_edge()
    {
        var r = Picture.RectOn(WideDesktop, new ScreenMode(2560, 1440));
        Assert.False(Picture.On(r, 2559, 700));        // the last pixel of the left bar
        Assert.False(Picture.On(r, 5120, 700));        // the first of the right bar
        Assert.False(Picture.On(r, 0, 0));
        Assert.False(Picture.On(r, 7679, 1439));       // where his task bar is
        Assert.Equal((0f, 700f), Picture.FromWindow(r, 100, 700));
        Assert.Equal((2559f, 700f), Picture.FromWindow(r, 7600, 700));
        // With no bars every pixel is on the picture and is itself.
        var whole = Picture.RectOn(WideDesktop, null);
        Assert.True(Picture.On(whole, 0, 0));
        Assert.True(Picture.On(whole, 7679, 1439));
        Assert.Equal((7679f, 1439f), Picture.FromWindow(whole, 7679, 1439));
        Assert.Equal((123f, 45f), Picture.ToWindow(whole, 123, 45));
    }

    [Fact]
    public void The_log_says_where_the_picture_is_and_what_the_world_is_drawn_at()
    {
        var r = Picture.RectOn(WideDesktop, new ScreenMode(1280, 720));
        string line = Picture.Describe(WideDesktop, r, Picture.WorldSizeIn(r, new ScreenMode(1280, 720)));
        Assert.Contains("the picture is 2560x1440 at 2560,0 of the window's 7680x1440", line);
        Assert.Contains("the world is drawn at 1280x720", line);
        Assert.Contains("the whole window", Picture.Describe(WideDesktop, Picture.RectOn(WideDesktop, null), null));
    }

    // ---- every read of the window's size and the pointer goes through the picture ----

    static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// The files that are the window's own hosts and may ask SDL itself (they want the window, not the
    /// picture), the tape's own replay and resize code, and the model viewer, which has a window of its own.
    /// </summary>
    static readonly string[] Hosts =
    [
        "PictureHost.cs", "FullScreenHost.cs", "WindowFrameHost.cs", "WindowPlaceHost.cs", "WindowReportHost.cs", "BorderlessHost.cs",
        "OptionsHost.cs", "SdlInput.cs", "SdlWindowModes.cs", "ViewGltfCommand.cs",
    ];

    /// <summary>
    /// **No loop reads the window's size, the pointer or the swapchain past the picture.** Every file of
    /// the program but the window's own hosts must use <c>PictureHost.Size</c>, <c>PictureHost.Warp</c>,
    /// <c>PictureHost.Acquire</c> and the input tape's poll and mouse state (which go through
    /// <c>PictureHost</c>): a direct call is a layout, a pick or a frame that ignores the bars.
    /// </summary>
    [Fact]
    public void The_programs_loops_ask_the_picture_and_not_SDL()
    {
        Assert.SkipUnless(Root() is not null, "the source tree is not beside the tests");
        var calls = new Regex(@"(?<![\w>])(SDL_GetWindowSize|SDL_GetWindowSizeInPixels|SDL_GetMouseState|SDL_WarpMouseInWindow|SDL_WaitAndAcquireGPUSwapchainTexture|SDL_AcquireGPUSwapchainTexture)\s*\(");
        var found = new List<string>();
        // The tools (vitomir-tools, with its host) and the frame controller's library (the
        // Classic module's), whose loops these are; and the platform program's Classic host, whose window is the same window code's (Vitomir.Platform.Render/Windowing).
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root()!, "src", "Vitomir.Tools"), "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(Root()!, "src", "Vitomir.Classic"), "*.cs", SearchOption.AllDirectories))
                     .Append(Path.Combine(Root()!, "src", "Vitomir.Platform.Host", "ClassicHost.cs")))
        {
            if (Hosts.Contains(Path.GetFileName(file))) continue;
            int n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                string code = line.TrimStart();
                if (code.StartsWith("//")) continue;
                if (calls.IsMatch(code)) found.Add($"{Path.GetFileName(file)}:{n}: {code}");
            }
        }
        Assert.True(found.Count == 0, "read past the picture:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// The input tape polls and samples through the picture, so a recording made with bars keeps picture pixels:
    /// the tape reads the live input only through its host (<c>InputTape.Live</c>), and the host's SDL side polls and reads
    /// the mouse through the picture.
    /// </summary>
    [Fact]
    public void The_input_tape_polls_and_samples_through_the_picture()
    {
        Assert.SkipUnless(Root() is not null, "the source tree is not beside the tests");
        string tape = File.ReadAllText(Path.Combine(Root()!, "src", "Vitomir.Classic", "Input", "InputTape.cs"));
        Assert.Contains("if (mode == Mode.Off) return Live.Poll(out e);", tape);
        Assert.Contains("bool got = Live.Poll(out e);", tape);
        Assert.Contains("if (mode == Mode.Off) return Live.Buttons(out x, out y);", tape);
        Assert.Contains("uint b = Live.Buttons(out float x, out float y);", tape);
        Assert.DoesNotMatch(new Regex(@"\bSDL_\w+\s*\("), tape);
        string live = File.ReadAllText(Path.Combine(Root()!, "src", "Vitomir.Platform.Render", "Windowing", "SdlInput.cs"));   // the platform's
        Assert.Contains("while (PictureHost.Poll(&raw))", live);
        Assert.Contains("(uint)PictureHost.MouseState(&px, &py)", live);
        Assert.DoesNotMatch(new Regex(@"SDL_GetMouseState\s*\("), live);
    }

    [Theory]
    [InlineData(1600, 900, 640, 360)]      // a recording at the old default: a quarter of the screen's corner
    [InlineData(2560, 1440, 640, 360)]
    [InlineData(1280, 1024, 600, 480)]     // taller than 4:3: held to the height
    [InlineData(320, 240, 320, 240)]       // the scripted recordings: never made larger
    public void A_replay_nobody_watches_is_shown_small_keeping_its_shape(int w, int h, int ww, int wh)
    {
        Assert.Equal((ww, wh), Picture.ShrunkWindow(w, h));
    }
}
