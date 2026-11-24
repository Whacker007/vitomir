// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The status bar's message box as the player sees it: the log's lines laid out in `sbTextbox`, the
/// scroll that keeps the newest in view, and the scroll bar beside it. The geometry is
/// the shipped pane's — a 308 x 41 box, a 42-pixel bar with an 8-pixel slider — and the font is a
/// stand-in with the shipped one's 10-pixel line, so every number here is arithmetic.
/// </summary>
public class MessageBoxTests
{
    const string Nui = """
        name=statusBarSP
        pos=0,0
        element scrollslider name=sbscrollslider pos=474,93 size=8,8 bitmap=slider.tga
        element scrollbar name=sbscrollbar pos=474,93 size=8,42 flags=0x02 vertical=1 slider=sbscrollslider
        element textbox name=sbtextbox pos=164,94 size=308,41 flags=0x02 maxitems=100 textboxflags=0x04 vscrollbar=sbscrollbar
        """;

    /// <summary>A font with a fixed advance and the shipped `defaultFont10`'s ten-pixel line.</summary>
    static NfdFont Font(int advance = 6, int line = 10)
    {
        var glyphs = new NfdGlyph[256];
        for (int i = 0; i < 256; i++) glyphs[i] = new NfdGlyph(0, 0, advance, line, advance, 0);
        return new NfdFont { LineHeight = line, StripWidth = 1, StripHeight = 1, Glyphs = glyphs };
    }

    static (MessageLog Log, MessageBox Box) Open()
    {
        var pane = NuiPane.Parse(Nui, "statusBarSP.nui");
        UiControl Control(string name) => new(pane.Elements.First(e => e.Name == name));
        var log = new MessageLog();
        return (log, new MessageBox(log, Font(), Control("sbtextbox"), Control("sbscrollbar"), Control("sbscrollslider")));
    }

    static void Say(MessageLog log, MessageBox box, int count, string prefix = "line")
    {
        for (int i = 0; i < count; i++) log.Add($"{prefix} {i}");
        box.Sync();
    }

    /// <summary>
    /// The first line said sits against the top of the box and each later one below it
    /// (counting up from the box's bottom); while they fit nothing scrolls, and the slider is at the
    /// **bottom** of its track — where a box that fits always leaves it (the scroll bar turns every value
    /// outside the range into the range's minimum, and the range is upside down).
    /// </summary>
    [Fact]
    public void Lines_fill_the_box_from_the_top()
    {
        var (log, box) = Open();
        Say(log, box, 3);
        Assert.Equal([31, 21, 11], box.Items.Select(i => i.Y));
        Assert.Equal(0, box.Scroll);
        Assert.Equal([31, 21, 11], box.Drawn().Select(d => d.Y));
        Assert.Equal(93, box.Slider!.Y);
    }

    /// <summary>
    /// The fourth line ends a pixel above the bottom, which is less than the quarter line the scroll
    /// keeps — so the box lifts by one, and by the fifth the first line is lifted out of sight: the
    /// newest always ends two pixels above the bottom edge.
    /// </summary>
    [Fact]
    public void Once_full_the_newest_sits_a_quarter_line_above_the_bottom()
    {
        var (log, box) = Open();
        Say(log, box, 4);
        Assert.Equal(1, box.Scroll);
        Assert.Equal([32, 22, 12, 2], box.Drawn().Select(d => d.Y));
        log.Add("line 4");
        box.Sync();
        Assert.Equal(11, box.Scroll);
        Assert.Equal(["line 1", "line 2", "line 3", "line 4"], box.Drawn().Select(d => d.Text));
        Assert.Equal([32, 22, 12, 2], box.Drawn().Select(d => d.Y));
        Assert.False(box.Visible(0));
    }

    /// <summary>
    /// The wheel over the box moves it a page — the box's own 41 pixels — towards the
    /// oldest for a turn away, held at the top, and the slider follows; **a new line puts the view
    /// back on the newest** wherever it was, because the add scrolls unconditionally.
    /// </summary>
    [Fact]
    public void The_wheel_pages_and_a_new_line_brings_the_view_back()
    {
        var (log, box) = Open();
        Say(log, box, 10);                         // the last line's bottom at 41 - 100 = -59
        Assert.Equal(61, box.MaxScroll);
        Assert.Equal(61, box.Scroll);
        Assert.Equal(93, box.Slider!.Y);

        box.Wheel(+1);
        Assert.Equal(20, box.Scroll);
        Assert.Equal(115, box.Slider.Y);           // (41 / 61) * 34 + 93, truncated
        box.Wheel(+1);
        Assert.Equal(0, box.Scroll);
        Assert.Equal(127, box.Slider.Y);           // the top of the track
        Assert.Equal(["line 0", "line 1", "line 2", "line 3", "line 4"], box.Drawn().Select(d => d.Text));
        box.Wheel(-1);
        Assert.Equal(41, box.Scroll);

        log.Add("news");
        box.Sync();
        Assert.Equal(box.MaxScroll, box.Scroll);
        Assert.Equal("news", box.Drawn().Last().Text);
        Assert.Equal(93, box.Slider.Y);
    }

    /// <summary>
    /// A press on the bar pages towards the press: above the slider's bottom towards the
    /// oldest, level with it or below towards the newest.
    /// </summary>
    [Fact]
    public void A_press_on_the_bar_pages_towards_it()
    {
        var (log, box) = Open();
        Say(log, box, 10);
        Assert.Contains("up from the newest", box.Press(476, 120));
        Assert.Equal(20, box.Scroll);
        box.Press(476, 100);                       // below the slider, now at 115
        Assert.Equal(61, box.Scroll);
        Assert.False(box.Dragging);
    }

    /// <summary>
    /// The slider is the bar's child, so a press on it is
    /// the slider's: it is taken hold of when the newest line's bottom
    /// is below the box, and dragging it scrolls the box to match.
    /// </summary>
    [Fact]
    public void Dragging_the_slider_scrolls_the_box()
    {
        var (log, box) = Open();
        Say(log, box, 10);
        Assert.Equal("took hold of the slider", box.Press(476, 95));
        Assert.True(box.Dragging);
        box.DragTo(95 + 17);                       // halfway up the 34-pixel track
        Assert.Equal(110, box.Slider!.Y);
        Assert.Equal(30, box.Scroll);              // -(17 / 34 * 61 - 61) = 30.5, truncated
        box.DragTo(400);                           // far past the top: held to the track
        Assert.Equal(127, box.Slider.Y);
        Assert.Equal(0, box.Scroll);
        box.DragTo(390);                           // and it comes back at once, moving by the pointer's travel
        Assert.Equal(117, box.Slider.Y);
        box.Release();
        Assert.False(box.Dragging);
    }

    /// <summary>
    /// A line arriving mid-drag moves the slider (the range changed), and moving a held slider lets
    /// go of it — so the drag ends and the view goes to the newest.
    /// </summary>
    [Fact]
    public void A_line_arriving_mid_drag_ends_the_drag()
    {
        var (log, box) = Open();
        Say(log, box, 10);
        box.Press(476, 95);
        box.DragTo(112);
        Assert.True(box.Dragging);
        log.Add("news");
        box.Sync();
        Assert.False(box.Dragging);
        Assert.Equal(box.MaxScroll, box.Scroll);
        Assert.Equal(93, box.Slider!.Y);
    }

    /// <summary>
    /// The slider cannot be taken while the newest line's bottom is still inside the box — even the
    /// fourth line, which the box has lifted by a pixel — and the press does not page either.
    /// </summary>
    [Fact]
    public void The_slider_is_not_taken_while_everything_fits()
    {
        var (log, box) = Open();
        Say(log, box, 4);
        Assert.Equal("the slider (nothing to scroll)", box.Press(476, 95));
        Assert.False(box.Dragging);
        Assert.Equal(1, box.Scroll);
    }

    /// <summary>
    /// A line wider than the box is broken between words into one item two lines tall, and
    /// its first line is drawn above its second; a line posted without a colour takes the box's own
    /// 240 grey.
    /// </summary>
    [Fact]
    public void A_long_line_wraps_and_keeps_its_order()
    {
        var (log, box) = Open();
        string text = string.Join(' ', Enumerable.Repeat("word", 12));      // 59 characters, 354 px
        log.Add(text);
        log.Add("red", MessageLog.Red);
        box.Sync();
        var first = box.Items[0];
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(20, first.Height);
        Assert.Equal(21, first.Y);
        var drawn = box.Drawn().ToList();
        Assert.Equal((first.Lines[0], 31, MessageBox.OwnColour), drawn[0]);
        Assert.Equal((first.Lines[1], 21, MessageBox.OwnColour), drawn[1]);
        Assert.Equal(MessageLog.Red, drawn[2].Colour);
    }

    /// <summary>The box keeps a hundred items, like the log it shows, dropping the oldest.</summary>
    [Fact]
    public void The_box_keeps_the_last_hundred()
    {
        var (log, box) = Open();
        Say(log, box, MessageLog.MaxItems + 5);
        Assert.Equal(MessageLog.MaxItems, box.Items.Count);
        Assert.Equal("line 5", box.Items[0].Message.Text);
        Assert.Equal(box.MaxScroll, box.Scroll);
    }

    /// <summary>
    /// Emptying the box (the advancement screen's) drops the items and the scroll and leaves
    /// the bar alone: the slider stays where it was until the next line.
    /// </summary>
    [Fact]
    public void Emptying_the_box_leaves_the_slider()
    {
        var (log, box) = Open();
        Say(log, box, 10);
        box.Wheel(+1);
        int at = box.Slider!.Y;
        log.Clear();
        box.Sync();
        Assert.Empty(box.Items);
        Assert.Equal(0, box.Scroll);
        Assert.Equal(at, box.Slider.Y);
        Assert.Equal("the message box is empty", box.Wheel(+1));
        log.Add("again");
        box.Sync();
        Assert.Equal(93, box.Slider.Y);
    }
}
