// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;
using static Vitomir.Engine.Tests.ReadPaneTests;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The read dialog's scroll bar (a comparison with 1.1's gap: `readScrollBar` and `readScrollSlider`
/// were not drawn, so a document could not be scrolled with the mouse). It is the ui library's bar:
/// the range the paragraphs set and the top the open puts it at; the slider
/// where the value says; a press on the track a page; the slider taken only
/// when there is somewhere to go, dragged and the body following it;
/// the wheel over the body a page. The pane is the shipped layout: the bar
/// 8 × 268 at (475, 181), the slider 8 × 8, the body 312 × 266 — a track of 260.
/// </summary>
public class ReadScrollBarTests
{
    /// <summary>Forty one-line paragraphs: 851 pixels to scroll (<see cref="ReadPaneTests"/>).</summary>
    static ReadPane Long() => Pane(Doc([.. Enumerable.Range(0, 40).Select(i => $"line {i}")]));

    [Fact]
    public void Opening_a_long_document_sets_the_range_and_puts_the_slider_at_the_top()
    {
        var pane = Long();
        var bar = pane.Bar!;
        Assert.Equal((-851, 0, 0), (bar.Min, bar.Max, bar.Value));   // the last paragraph's bottom less a quarter line, to 0
        Assert.Equal(181 + 260, bar.Slider.Y);                        // the top of the track
        Assert.False(pane.Fits);
    }

    /// <summary>
    /// A page that fits sets a range above 0, so the value 0 is outside it and becomes its minimum
    /// — the slider sits at the bottom of the track — and the slider cannot be taken.
    /// </summary>
    [Fact]
    public void A_page_that_fits_leaves_the_slider_at_the_bottom_and_it_cannot_be_taken()
    {
        var pane = Pane(Doc("A short note."));
        Assert.True(pane.Fits);
        Assert.Equal(181, pane.Bar!.Slider.Y);
        Assert.Equal("the slider (nothing to scroll)", pane.Press(478, 185));
        Assert.False(pane.Dragging);
        pane.DragTo(300);
        Assert.Equal(0, pane.Scroll);
    }

    [Fact]
    public void A_press_on_the_track_pages_the_body_by_its_height_and_the_slider_follows()
    {
        var pane = Long();
        Assert.Same(pane.Bar!.Bar, pane.TrackAt(478, 200));
        pane.Press(478, 200);                              // below the slider: a page on
        Assert.Equal(266, pane.Scroll);
        Assert.Equal(-266, pane.Bar.Value);
        Assert.Equal((int)(585 / 851f * 260 + 181), pane.Bar.Slider.Y);

        pane.Press(478, 440);                              // above it: a page back
        Assert.Equal(0, pane.Scroll);
        Assert.Equal(441, pane.Bar.Slider.Y);

        pane.Press(478, 440);                              // level with the slider's bottom or under it: on
        Assert.Equal(266, pane.Scroll);
    }

    [Fact]
    public void The_slider_dragged_scrolls_the_body_to_where_it_is()
    {
        var pane = Long();
        Assert.Same(pane.Bar!.Slider, pane.TrackAt(478, 445));
        Assert.Equal("took hold of the slider", pane.Press(478, 445));
        Assert.True(pane.Dragging);

        pane.DragTo(445 - 130);                            // half way down the track
        Assert.Equal(311, pane.Bar.Slider.Y);
        Assert.Equal(425, pane.Scroll);                    // −(130/260 × 851 − 851), truncated

        pane.DragTo(-1000);                                // held to the track's bottom: the end
        Assert.Equal(181, pane.Bar.Slider.Y);
        Assert.Equal(851, pane.Scroll);

        pane.Release();
        Assert.False(pane.Dragging);
        pane.DragTo(445);
        Assert.Equal(851, pane.Scroll);                    // let go: the pointer no longer moves it
    }

    /// <summary>The slider follows a scroll unless it is held, when the body alone moves.</summary>
    [Fact]
    public void A_scroll_moves_the_slider_unless_it_is_held()
    {
        var pane = Long();
        pane.ScrollBy(pane.LineHeight);                    // an arrow key (ours)
        Assert.Equal(-20, pane.Bar!.Value);

        pane.Press(478, pane.Bar.Slider.Y + 4);
        int y = pane.Bar.Slider.Y;
        pane.ScrollBy(pane.Page);                          // a Page key while it is held
        Assert.Equal(286, pane.Scroll);
        Assert.Equal(y, pane.Bar.Slider.Y);
        Assert.True(pane.Dragging);
    }

    [Fact]
    public void The_wheel_over_the_body_turns_it_a_page_and_only_there()
    {
        var pane = Long();
        Assert.True(pane.OverBody(300, 300));
        Assert.False(pane.OverBody(478, 300));             // the bar is not the body
        Assert.False(pane.OverBody(100, 100));

        pane.Wheel(-1);                                    // towards you: on
        Assert.Equal(266, pane.Scroll);
        pane.Wheel(-3);                                    // a page however far it turned
        Assert.Equal(532, pane.Scroll);
        pane.Wheel(1);
        Assert.Equal(266, pane.Scroll);
        Assert.Equal(-266, pane.Bar!.Value);
    }

    [Fact]
    public void Nothing_on_the_bar_answers_with_no_document_open()
    {
        var pane = Pane();
        Assert.Null(pane.TrackAt(478, 200));
        Assert.Null(pane.Press(478, 200));
        Assert.Null(pane.Press(300, 300));                 // the body is not the bar
    }

    /// <summary>
    /// The shipped pane and font: `readScrollBar` finds its slider. In `defaultFont10` most documents
    /// fit the body — London's longest, `SocietyDocument3`, does — but the Prague narrator's Chronicle
    /// of Caine, a paragraph a line, runs past it, and the bar is what reaches its end.
    /// </summary>
    [Fact]
    public void A_long_shipped_document_needs_the_bar()
    {
        using var files = Install.Open();
        var documents = new Documents(files);
        Assert.SkipUnless(documents.Add("london") && documents.Add("prague"), "no London or Prague strings in this install");
        var assets = new UiAssets(files);
        var font = assets.Font("defaultFont10");
        var pane = new ReadPane(assets.Pane(ReadPane.PaneName)) { Advance = c => font[c].Advance, LineHeight = font.LineHeight };
        Assert.Equal("statusbarslider.tga", pane.Bar!.Slider.Element.Get("bitmap"), ignoreCase: true);
        Assert.Equal(260, pane.Bar.Travel);

        pane.Open(documents.Find("SocietyDocument3"));
        Assert.True(pane.Fits);
        Assert.Equal(181, pane.Bar.Slider.Y);              // a page that fits: the slider at the bottom

        pane.Open(documents.Find("Narrator_15_1_564"));
        Assert.False(pane.Fits);
        Assert.Equal(441, pane.Bar.Slider.Y);
        Assert.True(pane.MaxScroll > 0);
        pane.Press(478, 300);                              // the track below the slider: a page on
        Assert.Equal(Math.Min(266, pane.MaxScroll), pane.Scroll);
        for (int i = 0; i < 10; i++) pane.Wheel(-1);       // and the wheel to the end
        Assert.Equal(pane.MaxScroll, pane.Scroll);
        Assert.Equal(181, pane.Bar.Slider.Y);
    }
}
