// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;

namespace Vitomir.Platform.Host.Tests;

/// <summary>The frame cap — its rounding to an even fraction of the display's refresh, its setting and flag, its wait.</summary>
public sealed class FrameCapTests
{
    [Theory]
    [InlineData(60, 144, 72)]      // the brief's: 60 asked on 144 Hz draws 72 (144 / 2)
    [InlineData(60, 60, 60)]
    [InlineData(60, 120, 60)]
    [InlineData(30, 144, 36)]      // 144 / 4
    [InlineData(100, 144, 144)]    // 144 / 1: never under the ask
    [InlineData(50, 100, 50)]
    [InlineData(240, 144, 144)]    // more than the display shows: its refresh
    [InlineData(59.94, 59.94, 59.94)]
    [InlineData(60, 165, 82.5)]    // 165 / 2
    [InlineData(45, 0, 45)]        // the refresh not known: the number as given
    [InlineData(0, 144, 0)]        // none asked: none
    public void The_cap_is_an_even_fraction_of_the_refresh_not_under_the_ask(double asked, double refresh, double expected) =>
        Assert.Equal(expected, FrameCap.Effective(asked, refresh), 6);

    [Fact]
    public void The_setting_and_the_flag_give_the_cap_none_by_default()
    {
        var plain = new PlatformSettings();
        Assert.Equal(0f, plain.FrameCap);
        Assert.Equal(0f, plain.FrameCapFor(new Cli([])));
        Assert.Equal(60f, new PlatformSettings { FrameCap = 60f }.FrameCapFor(new Cli([])));
        Assert.Equal(90f, new PlatformSettings { FrameCap = 60f }.FrameCapFor(new Cli(["--frame-cap", "90"])));
        Assert.Equal(0f, new PlatformSettings { FrameCap = 60f }.FrameCapFor(new Cli(["--frame-cap", "0"])));
        Assert.Equal(0f, new PlatformSettings { FrameCap = -5f }.FrameCapFor(new Cli([])));   // a setting out of its sense: none
        Assert.Throws<SessionException>(() => plain.FrameCapFor(new Cli(["--frame-cap", "fast"])));
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-frame-cap-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { FrameCap = 72f }.Save(path);
            Assert.Contains("\"frameCap\": 72", File.ReadAllText(path));
            Assert.Equal(72f, PlatformSettings.Load(path).FrameCap);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A timing test: left out of the plain run and run alone by the timing command (the README's Build, test and run); its bounds kept.</summary>
    [Fact, Trait("Category", "Timing")]
    public void The_wait_holds_the_pictures_to_the_cap()
    {
        var cap = new FrameCap(100);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 30; i++) cap.Wait();
        double ms = clock.Elapsed.TotalMilliseconds;
        Assert.InRange(ms, 280, 600);   // 30 pictures at 100 a second: 0.29–0.30 s (the first is due at once), with room for a busy machine
    }
}
