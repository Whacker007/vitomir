// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using static Vitomir.Engine.Tests.ReadPaneTests;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The newest dialog takes every click**: the read dialog is the active pane
/// wherever the pointer is, so its press is settled before anything else — the status bar up or
/// hidden. Earlier, Vitomir reached the dialog's scroll bar and OK button only from inside the
/// status bar's branch, and with the bar hidden (a cutscene, a camera path, the Storyteller's seat)
/// a book could not be closed with the mouse. These pin what a press on the dialog reaches on its
/// own, with no status bar anywhere: the slider or the track, the OK, or nothing — swallowed.
/// </summary>
public class DialogClicksTests
{
    static ReadPane Long() => Pane(Doc([.. Enumerable.Range(0, 40).Select(i => $"line {i}")]));

    [Fact]
    public void The_OK_button_is_held_by_the_press_with_no_status_bar()
    {
        var press = DialogClicks.Left(Long(), 460, 170);     // readOKbutton, 26 x 15 at 456,163
        Assert.Equal(DialogClicks.Took.Ok, press.What);
        Assert.Null(press.Said);
    }

    [Fact]
    public void The_slider_is_taken_and_the_track_pages_before_anything_else_is_asked()
    {
        var pane = Long();
        var slider = DialogClicks.Left(pane, 478, 445);      // the slider, the bar's child, at the top of the track
        Assert.Equal(DialogClicks.Took.ScrollBar, slider.What);
        Assert.Same(pane.Bar!.Slider, slider.Control);
        Assert.Equal("took hold of the slider", slider.Said);
        Assert.True(pane.Dragging);
        pane.Release();

        var track = DialogClicks.Left(pane, 478, 200);       // the track under the slider: a page on
        Assert.Equal(DialogClicks.Took.ScrollBar, track.What);
        Assert.Same(pane.Bar.Bar, track.Control);
        Assert.Equal(266, pane.Scroll);
    }

    /// <summary>
    /// A press on the text, or where the status bar's buttons would be under the dialog, or on the
    /// world, reaches none of the dialog's controls — and is still the dialog's: nothing scrolls,
    /// nothing is held, and the caller hands it to nobody else.
    /// </summary>
    [Theory]
    [InlineData(300, 300)]     // the text
    [InlineData(140, 100)]     // the status bar's inventory button, under the dialog's reach
    [InlineData(600, 450)]     // the world beside the dialog
    public void A_press_anywhere_else_reaches_nothing(int x, int y)
    {
        var pane = Long();
        var press = DialogClicks.Left(pane, x, y);
        Assert.Equal(DialogClicks.Took.Nothing, press.What);
        Assert.Null(press.Control);
        Assert.Equal(0, pane.Scroll);
        Assert.False(pane.Dragging);
    }
}
