// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The swing's arc's settings — shown at 25 % by default, its toggle unbound, saved and loaded, through the
/// Controls page's values and back; the third-person blow's pitch from the view by default, the old way behind a setting, and a
/// recording's note (a tape without one was made from the reticle's point). Needs no game files.
/// </summary>
public sealed class SwingArcSettingsTests
{
    [Fact]
    public void The_arc_is_shown_at_a_quarter_its_toggle_unbound_and_both_are_saved_and_loaded()
    {
        var s = new PlatformSettings();
        Assert.True(s.ShowSwingArc);
        Assert.Equal(25f, s.SwingArcOpacity);
        Assert.False(s.ThirdPersonAimFromReticle);
        Assert.Empty(PlatformSettings.DefaultBindings()["swingArc"]);
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-cc-arc-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        s.ShowSwingArc = false;
        s.SwingArcOpacity = 60f;
        s.ThirdPersonAimFromReticle = true;
        s.Save(path);
        var back = PlatformSettings.Load(path);
        Assert.False(back.ShowSwingArc);
        Assert.Equal(60f, back.SwingArcOpacity);
        Assert.True(back.ThirdPersonAimFromReticle);
        // The Controls page's values and back.
        var values = Menus.Values(back);
        Assert.Equal(60f, values.SwingArcOpacity);
        var other = new PlatformSettings();
        Menus.Store(values with { SwingArcOpacity = 35f }, other);
        Assert.Equal(35f, other.SwingArcOpacity);
        // Out of its ends in a file: the page shows it within them.
        back.SwingArcOpacity = 250f;
        Assert.Equal(100f, Menus.Values(back).SwingArcOpacity);
    }

    [Fact]
    public void Third_person_is_pitched_from_the_view_and_a_tape_says_which()
    {
        var view = new ThirdPersonAimSettings();
        Assert.False(view.FromReticle);
        Assert.Equal(0f, view.LevelOffsetDegrees);
        Assert.Equal("view:0", view.Note);
        Assert.False(ThirdPersonAimSettings.FromNote("view:0").FromReticle);
        Assert.Equal(10f, ThirdPersonAimSettings.FromNote("view:10").LevelOffsetDegrees);
        Assert.True(ThirdPersonAimSettings.FromNote("reticle").FromReticle);
        Assert.True(ThirdPersonAimSettings.FromNote(null).FromReticle);          // an older tape
        Assert.False(new SessionOptions { Level = "" }.ThirdPersonAim.FromReticle);
    }
}
