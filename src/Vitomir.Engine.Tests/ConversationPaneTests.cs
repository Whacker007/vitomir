// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The conversation on screen: the transcript and the replies. The conversation pane and the
/// subtitles are small, and the layout is in the player's own
/// `ui/conversationPane.nui` and `ui/subtitle.nui` — so most of this is read from the install
/// rather than hard-coded, and the tests that need it skip without one.
/// </summary>
public class ConversationPaneTests
{
    // The two shipped panes, verbatim, so the shape of what is being modelled is visible here.
    const string ReplyNui = """
        name=conversationPane
        pos=64,32
        dialog=1
        quad mat=conversation01.tga pos=0,0 size=256,64
        quad mat=conversation02.tga pos=256,0 size=256,64
        element textlistbox name=convResponse pos=2,2 size=508,60 color=255,0,0,0 itemspacing=0x04 textboxflags=0x04
        """;

    const string SubtitleNui = """
        name=subtitleOverlay
        pos=64,32
        quad mat=subTitle1.tga pos=0,0 size=256,128
        quad mat=subTitle2.tga pos=256,0 size=256,128
        element textbox name=subtitleTextBox flags=0x02 pos=1,1 size=510,126 itemspacing=3 textboxflags=0x0C
        """;

    static ConversationPane Pane() =>
        new(NuiPane.Parse(ReplyNui, "conversationPane.nui"), NuiPane.Parse(SubtitleNui, "subtitle.nui"));

    /// <summary>A font with a fixed advance, so wrapping is arithmetic rather than a measurement.</summary>
    static NfdFont Font(int advance = 10, int line = 12)
    {
        var glyphs = new NfdGlyph[256];
        for (int i = 0; i < 256; i++) glyphs[i] = new NfdGlyph(0, 0, advance, line, advance, 0);
        return new NfdFont { LineHeight = line, StripWidth = 1, StripHeight = 1, Glyphs = glyphs };
    }

    /// <summary>
    /// **It is two panes.** The replies are a list with an opaque black background; the transcript
    /// is a plain text box with none, which is why the lines sit straight on the scene and the
    /// reply box does not.
    /// </summary>
    [Fact]
    public void The_replies_have_a_background_and_the_transcript_does_not()
    {
        var pane = Pane();
        Assert.Equal("textlistbox", pane.List.Type);
        Assert.Equal("textbox", pane.Box.Type);
        Assert.Equal("255,0,0,0", pane.List.Element.Get("color"));   // opaque black
        Assert.Null(pane.Box.Element.Get("color"));

        // Both authored at the same spot, and `.nui` space counts up from the bottom.
        Assert.Equal([64, 32], pane.Replies.Pos);
        Assert.Equal([64, 32], pane.Transcript.Pos);
        Assert.Equal(ConversationPane.ConversationY, pane.Transcript.Pos[1]);
        Assert.True(pane.Replies.Dialog);
    }

    /// <summary>
    /// **Consecutive lines alternate white and grey**, and it is per line rather than per speaker —
    /// The transcript toggles a flag on every call. Starting again puts it back to white.
    /// </summary>
    [Fact]
    public void The_transcript_alternates_two_colours_line_by_line()
    {
        var pane = Pane();
        pane.Say("<Anezka> The mighty walls of Prague once held all demons at bay.");
        pane.Say("<Christof> I will never let them take thee!");
        pane.Say("<Anezka> Oh, sir...");

        Assert.Equal([ConversationPane.Bright, ConversationPane.Dim, ConversationPane.Bright],
                     pane.Said.Select(r => r.Colour));

        pane.Reset();
        Assert.Empty(pane.Said);
        pane.Say("<Anezka> Again.");
        Assert.Equal(ConversationPane.Bright, pane.Said[0].Colour);
    }

    /// <summary>
    /// A line too wide for the box is wrapped on words and each piece keeps the line's colour —
    /// which is why one long speech is three rows of the same shade rather than three alternating.
    /// </summary>
    [Fact]
    public void A_long_line_wraps_and_every_piece_keeps_its_colour()
    {
        var font = Font(advance: 10, line: 12);
        // 510 wide at 10 px a character is 51 characters a row.
        var wrapped = ConversationPane.Wrap(font, string.Join(' ', Enumerable.Repeat("aaaaaaaaa", 12)), 510);
        Assert.Equal(3, wrapped.Count);
        Assert.All(wrapped, w => Assert.True(font.Measure(w) <= 510));

        var pane = Pane();
        pane.Say(string.Join(' ', Enumerable.Repeat("aaaaaaaaa", 12)));
        pane.Say("short");
        var rows = pane.Rows(font);
        Assert.Equal(4, rows.Count);
        Assert.Equal([ConversationPane.Bright, ConversationPane.Bright, ConversationPane.Bright,
                      ConversationPane.Dim], rows.Select(r => r.Colour));
    }

    /// <summary>A word wider than the box is left over-long rather than broken mid-word.</summary>
    [Fact]
    public void A_word_wider_than_the_box_is_not_broken()
    {
        var font = Font();
        var wrapped = ConversationPane.Wrap(font, new string('x', 80), 510);
        Assert.Single(wrapped);
        Assert.True(font.Measure(wrapped[0]) > 510);
    }

    /// <summary>The box keeps the rows that fit, newest at the bottom — older ones scroll off.</summary>
    [Fact]
    public void Only_the_newest_rows_that_fit_are_kept()
    {
        var font = Font(line: 12);                    // 126 tall, spacing 3 -> 8 rows fit
        var pane = Pane();
        for (int i = 0; i < 20; i++) pane.Say($"line {i}");
        var rows = pane.Rows(font);
        Assert.Equal(126 / 15, rows.Count);
        Assert.Equal("line 19", rows[^1].Text);       // the newest is last, which is the bottom
        Assert.Equal("line 12", rows[0].Text);
    }

    /// <summary>
    /// **A reply is listed bare** — no speaker in front of it — and gets one only once it has been
    /// said, which is the one place a reply is formatted like a line.
    /// </summary>
    [Fact]
    public void A_reply_is_bare_in_the_list_and_named_once_it_is_spoken()
    {
        var pane = Pane();
        Assert.False(pane.Offering);
        pane.Offer(["Tomorrow I shall venture into the silver mine.", "A pity I am in poor health."]);
        Assert.True(pane.Offering);
        Assert.DoesNotContain(pane.Offered, r => r.StartsWith('<'));

        pane.Say(Conversation.Subtitle("Christof", "Tomorrow I shall venture into the silver mine."));
        Assert.StartsWith("<Christof> ", pane.Said[0].Text);
    }

    /// <summary>The list's rows are hit-tested from its own top, so row 0 is the highest one.</summary>
    [Fact]
    public void The_mouse_finds_the_reply_it_is_over()
    {
        var font = Font(line: 12);                    // rows 12 apart: `itemspacing=0x04` reads as 0
        var pane = Pane();
        Assert.Equal(-1, pane.RowAt(font, 300, 40));  // nothing offered, nothing to hit
        pane.Offer(["first", "second"]);

        int left = 64 + 2, bottom = 32 + 2;           // pane pos + control pos
        Assert.Equal(12, pane.RowHeight(font));
        Assert.Equal(0, pane.RowAt(font, left + 10, bottom + 60 - 1));   // the top of the box
        Assert.Equal(1, pane.RowAt(font, left + 10, bottom + 60 - 17));
        Assert.Equal(-1, pane.RowAt(font, left - 1, bottom + 60 - 1));   // outside to the left
        Assert.Equal(-1, pane.RowAt(font, left + 10, bottom - 1));       // below the box
        Assert.Equal(-1, pane.RowAt(font, left + 10, bottom + 10));      // inside, past the last row
    }

    /// <summary>
    /// **Five replies fit in 1.1** (in play at Unorna's shop the fifth hung under the box).
    /// The list's <c>itemspacing=0x04</c> is read with <c>sscanf("%d")</c>, which makes it 0,
    /// so with <c>defaultFont</c>'s 10-pixel line the rows sit at 50, 40, 30, 20 and 10 from the box's
    /// bottom — Vitomir had read the spacing as hex, 4, and put the fifth at −6.
    /// </summary>
    [Fact]
    public void Five_replies_fit_the_box_because_its_itemspacing_reads_as_nought()
    {
        var font = Font(line: 10);
        var pane = Pane();
        Assert.Equal(10, pane.RowHeight(font));
        Assert.Equal(12 + 3, pane.LineHeight(Font(line: 12)));          // the transcript's decimal 3 is 3
        pane.Offer(["one", "two", "three", "four", "five"]);
        var five = pane.Layout(font);
        Assert.Equal([50, 40, 30, 20, 10], five.Rows.Select(r => r.Y));
        Assert.Equal(0, five.Scroll);
    }

    /// <summary>
    /// **More than fit scroll to the last, as 1.1's add does**: the last reply brought up to a
    /// quarter line (2) above the box's bottom, the first rows pushed off the top, where the box cuts
    /// them. Six scroll by 2; seven by 12, which puts the first wholly out of sight.
    /// </summary>
    [Fact]
    public void More_replies_than_fit_scroll_to_the_last_and_none_hangs_below_the_box()
    {
        var font = Font(line: 10);
        var pane = Pane();
        pane.Offer(["1", "2", "3", "4", "5", "6"]);
        Assert.Equal(2, pane.Layout(font).Scroll);
        pane.Offer(["1", "2", "3", "4", "5", "6", "7"]);
        var seven = pane.Layout(font);
        Assert.Equal(12, seven.Scroll);
        Assert.Equal(2, seven.Rows[6].Y + seven.Scroll);
        Assert.False(seven.Visible(0));                                   // 62: above the box's 60
        Assert.True(seven.Visible(1));                                    // 52..62, cut at the top
        Assert.All(seven.Rows, r => Assert.True(r.Y + seven.Scroll >= 0, "a row below the box"));
    }

    /// <summary>
    /// The mouse finds what is where the scroll put it, and the wheel over the box pages it back
    /// (a box's height a turn, held to the range). Offering the same replies again, as the
    /// frame loop does every tick, keeps the scroll.
    /// </summary>
    [Fact]
    public void The_wheel_pages_the_replies_and_the_mouse_follows_the_scroll()
    {
        var font = Font(line: 10);
        var pane = Pane();
        string[] replies = ["1", "2", "3", "4", "5", "6", "7"];
        pane.Offer(replies);
        int left = 64 + 2, bottom = 32 + 2;
        Assert.Equal(6, pane.RowAt(font, left + 10, bottom + 5));      // the seventh, at 2..12
        Assert.Equal(1, pane.RowAt(font, left + 10, bottom + 59));     // the second's cut top; the first is off
        Assert.Equal(-1, pane.RowAt(font, left + 10, bottom + 63));    // above the box: nothing

        pane.Page(font, -1, (left + 10, bottom + 55));                 // the wheel turned up
        Assert.Equal(0, pane.Layout(font).Scroll);
        Assert.Equal(0, pane.Hovered);                                 // the first, whole again at 50..60
        Assert.Equal(5, pane.RowAt(font, left + 10, bottom + 5));

        pane.Offer(replies);                                            // the same again: kept
        Assert.Equal(0, pane.Layout(font).Scroll);
        pane.Page(font, +1, (left + 10, bottom + 5));
        Assert.Equal(12, pane.Layout(font).Scroll);                    // no further than the end
        Assert.False(pane.Over((left + 10, bottom - 1)));
    }

    /// <summary>A reply wider than the box is broken between words (`textboxflags` 0x04, read as <c>%i</c>) into one taller row.</summary>
    [Fact]
    public void A_long_reply_wraps_inside_the_box()
    {
        var font = Font(advance: 10, line: 10);
        var pane = Pane();
        pane.Offer([string.Join(' ', Enumerable.Repeat("aaaaaaaaa", 6)), "short"]);   // 59 characters at 10: past 508
        var list = pane.Layout(font);
        Assert.Equal(2, list.Rows[0].Lines.Count);
        Assert.All(list.Rows[0].Lines, l => Assert.True(font.Measure(l) <= 508));
        Assert.Equal(60 - 20 - 10, list.Rows[1].Y);
    }

    /// <summary>
    /// The runtime drives the pane: every line it puts up — a spoken one or a chosen reply — arrives
    /// as one append, which is what makes the box a transcript rather than a caption.
    /// </summary>
    [Fact]
    public void The_runtime_feeds_the_transcript_one_line_at_a_time()
    {
        var pane = Pane();
        var talk = Conversation.Resolve(
            NcoFile.Read(System.Text.Encoding.Latin1.GetBytes(
                "CONVERSATION: T 0\r\nEVENT: ev_0\r\n\tLINE: a Anezka\r\n\tLINE: b Anezka\r\n"),
                "T.nco").Conversations[0], []);
        talk.Voices = _ => new ConversationVoice("hello there", 0);
        talk.Spoke += pane.Say;
        talk.Start(1000);

        talk.Tick(1000);
        talk.Tick(talk.NextAt);
        Assert.Equal(2, pane.Said.Count);
        Assert.Equal("<Anezka> hello there", pane.Said[0].Text);
        Assert.Equal(ConversationPane.Dim, pane.Said[1].Colour);
    }

    /// <summary>The player's own files, as they ship — the layout above is theirs, not ours.</summary>
    [Fact]
    public void The_shipped_panes_are_the_ones_this_models()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var pane = new ConversationPane(assets.Pane(ConversationPane.ReplyPane),
                                        assets.Pane(ConversationPane.TranscriptPane));

        Assert.Equal([64, 32], pane.Replies.Pos);
        Assert.Equal([64, 32], pane.Transcript.Pos);
        Assert.Equal(2, pane.Replies.Quads.Count);          // 512 wide, in two 256 pieces
        Assert.Equal(2, pane.Transcript.Quads.Count);
        Assert.Equal(508, pane.List.Width);
        Assert.Equal(510, pane.Box.Width);
        Assert.Equal("255,0,0,0", pane.List.Element.Get("color"));
        Assert.Null(pane.Box.Element.Get("color"));
    }
}
