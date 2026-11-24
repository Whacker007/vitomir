// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The read dialog's body: a document laid out as the ui library lays out a text box (bit 3
/// clear — a paragraph an item, broken by the library's wrap, `itemspacing` between items)
/// and scrolled by the pixel between the top and the last line a quarter line above the bottom.
/// The scroll bar that moves it is <see cref="ReadScrollBarTests"/>.
/// </summary>
public class ReadPaneTests
{
    internal const string Nui = """
        name=ReadTextBox
        pos=0,0
        dialog=1
        quad mat=ReadTextBox01 pos=149,218 size=256,256
        element button name=readOKbutton depressed=OKbuttonDep pos=456,163 size=26,15 flags=0x02
        element scrollslider name=readScrollSlider pos=475,181 size=8,8 bitmap=StatusBarSlider.tga
        element scrollbar name=readScrollBar pos=475,181 size=8,268 flags=0x02 vertical=1 slider=readScrollSlider
        element textbox name=readTextBox pos=161,182 size=312,266 flags=0x02 textboxflags=0x04 font=defaultFont10 vscrollbar=readScrollBar itemspacing=8
        element textbox name=mpReadTextTitle pos=196,453 size=252,13 textboxflags=0x01 font=defaultFont10
        """;

    /// <summary>Ten pixels a character, so a 312-wide box takes 31 characters — easy to count — and a 20-pixel font.</summary>
    internal static ReadPane Pane(NtxEntry? doc = null)
    {
        var pane = new ReadPane(NuiPane.Parse(Nui, "ReadTextBox.nui")) { Advance = _ => 10, LineHeight = 20 };
        pane.Open(doc);
        return pane;
    }

    internal static NtxEntry Doc(params string[] paragraphs) => new()
    {
        Id = "CuthbertLetter", Title = "Sir Cuthbert's Letter to Christof",
        Voice = "Cuthbert_2_2_14.mp3",
        Paragraphs = [.. paragraphs.Select(p => new NtxParagraph(p, NtxFile.White))],
    };

    [Fact]
    public void The_pane_takes_its_spacing_page_and_scroll_bar_from_the_file()
    {
        var pane = Pane();
        Assert.Equal(8, pane.Spacing);                    // itemspacing=8
        Assert.Equal(266, pane.Page);                     // the body is 266 tall
        Assert.NotNull(pane.Bar);                         // vscrollbar=readScrollBar, slider=readScrollSlider
        Assert.Equal(268 - 8, pane.Bar!.Travel);
        Assert.Equal("", pane.Title);                     // nothing open
        Assert.Empty(pane.Lines());
    }

    [Fact]
    public void A_paragraph_wraps_after_the_last_word_that_fits()
    {
        // 31 characters fit. "aaaa bbbb cccc dddd eeee ffff gggg" is 34, so it breaks before gggg.
        var pane = Pane(Doc("aaaa bbbb cccc dddd eeee ffff gggg"));
        var lines = pane.Lines();
        Assert.Equal(2, lines.Count);
        Assert.Equal("aaaa bbbb cccc dddd eeee ffff", lines[0].Text);
        Assert.Equal("gggg", lines[1].Text);
        Assert.All(lines, l => Assert.Equal(0, l.Paragraph));
    }

    /// <summary>The wrap with no space to break at: the line ends just before the character that overflowed.</summary>
    [Fact]
    public void A_word_longer_than_the_box_is_cut_where_it_overflows()
    {
        var pane = Pane(Doc("Constantinopolitanischerdudelsackpfeifenmacher"));
        var lines = pane.Lines();
        Assert.Equal(["Constantinopolitanischerdudelsa", "ckpfeifenmacher"], lines.Select(l => l.Text));
    }

    [Fact]
    public void Each_paragraph_is_an_item_of_its_own_colour_itemspacing_below_the_last()
    {
        var doc = new NtxEntry
        {
            Id = "x", Title = "x",
            Paragraphs = [new NtxParagraph("one", 0xFF112233), new NtxParagraph("two", NtxFile.White)],
        };
        var pane = Pane(doc);
        var items = pane.Items();
        Assert.Equal(2, items.Count);
        Assert.Equal(0xFF112233u, items[0].Colour);
        Assert.Equal(266 - 20, items[0].Y);               // the first against the box's top
        Assert.Equal(266 - 20 - 8 - 20, items[1].Y);      // the next 8 below it
        Assert.Equal(NtxFile.White, pane.Lines()[1].Colour);
        Assert.Equal(1, pane.Lines()[1].Paragraph);
    }

    /// <summary>
    /// Forty paragraphs of a line each: the last one's bottom is 40 × 20 + 39 × 8 = 1112 below the
    /// top, 846 below the box, so the body lifts from 0 to 846 plus a quarter line (5) and no further.
    /// </summary>
    [Fact]
    public void The_scroll_is_in_pixels_and_stops_at_the_top_and_a_quarter_line_past_the_end()
    {
        var pane = Pane(Doc([.. Enumerable.Range(0, 40).Select(i => $"line {i}")]));
        Assert.Equal(266 - 1112, pane.Items()[^1].Y);
        Assert.Equal(846 + 5, pane.MaxScroll);

        pane.ScrollBy(-5);
        Assert.Equal(0, pane.Scroll);                     // not above the top
        pane.ScrollBy(100);
        Assert.Equal(100, pane.Scroll);
        pane.ScrollBy(10000);
        Assert.Equal(851, pane.Scroll);                   // nor past the end
        Assert.Equal(("line 39", 5), (pane.Drawn().Last().Text, pane.Drawn().Last().Y));
    }

    /// <summary>
    /// What is drawn is every paragraph with any of itself in the box, whole — the box's surface cuts
    /// the rest — each line at its own height, the first line of an item highest.
    /// </summary>
    [Fact]
    public void A_paragraph_half_in_the_box_is_drawn_whole_and_the_rest_are_not()
    {
        var pane = Pane(Doc([.. Enumerable.Range(0, 40).Select(i => $"line {i}")]));
        var drawn = pane.Drawn().ToList();
        // 266 tall: items at 246, 218, … 22, −6 (half out below, still drawn), then −34 (not drawn).
        Assert.Equal(10, drawn.Count);
        Assert.Equal(("line 0", 246), (drawn[0].Text, drawn[0].Y));
        Assert.Equal(("line 9", -6), (drawn[9].Text, drawn[9].Y));

        pane.ScrollBy(30);
        Assert.DoesNotContain(pane.Drawn(), d => d.Text == "line 0");   // 276: past the top
        Assert.Contains(pane.Drawn(), d => d.Text == "line 1");         // 248
    }

    [Fact]
    public void Opening_a_document_puts_it_back_at_the_top_and_closing_empties_the_pane()
    {
        var pane = Pane(Doc(string.Join(" ", Enumerable.Repeat("word", 200))));
        pane.ScrollBy(50);
        Assert.Equal(50, pane.Scroll);

        pane.Open(Doc("short"));
        Assert.Equal(0, pane.Scroll);
        Assert.Equal("Sir Cuthbert's Letter to Christof", pane.Title);

        pane.Open(null);
        Assert.Null(pane.Document);
        Assert.Empty(pane.Lines());
    }

    /// <summary>The whole chain, from the shipped files: template id, document, title, prose.</summary>
    [Fact]
    public void The_letter_christof_carries_opens_and_reads()
    {
        using var files = Install.Open();
        var documents = new Documents(files);
        Assert.SkipUnless(documents.Add("prague"), "no strings/prague.ntx in this install");

        var doc = documents.Find("CuthbertLetter");
        Assert.NotNull(doc);

        var pane = Pane(doc);
        Assert.Equal("Sir Cuthbert's Letter to Christof", pane.Title);
        Assert.Equal(doc.Paragraphs.Count, pane.Items().Count);
        Assert.NotEmpty(pane.Lines());
        Assert.Contains("Kinsman", string.Join(" ", pane.Lines().Select(l => l.Text)));

        // A chronicle that is not there is reported rather than thrown, and nothing is lost.
        Assert.False(documents.Add("no_such_chronicle"));
        Assert.Contains("strings/no_such_chronicle.ntx", documents.Missing);
        Assert.NotNull(documents.Find("CuthbertLetter"));
    }

    /// <summary>
    /// **The metafile is where the list lives**, and a chronicle may name more than one table.
    /// `vienna.nmf` declares `prague.ntx` and `vienna.ntx`, so a letter picked up in Prague is still
    /// readable in Vienna — which guessing the file from the chronicle's own name would lose.
    /// </summary>
    [Fact]
    public void Vienna_keeps_pragues_documents_as_well_as_its_own()
    {
        using var files = Install.Open();
        if (!files.TryRead("Chronicles/vienna.nmf", out var raw)) { Assert.Skip("no vienna.nmf"); return; }
        var meta = NmfFile.Read(raw, "vienna.nmf");
        Assert.Equal(["prague.ntx", "vienna.ntx"], meta.Ntx);

        var documents = new Documents(files);
        Assert.Equal(2, documents.AddFrom(meta));
        // The Prague letter is reachable from Vienna.
        Assert.NotNull(documents.Find("CuthbertLetter"));
    }

    /// <summary>Prague names one of each, and the player and the cash templates besides.</summary>
    [Fact]
    public void A_metafile_names_the_tables_the_player_and_the_cash()
    {
        using var files = Install.Open();
        if (!files.TryRead("Chronicles/prague.nmf", out var raw)) { Assert.Skip("no prague.nmf"); return; }
        var meta = NmfFile.Read(raw, "prague.nmf");
        Assert.Equal("Christof", meta.Player);
        Assert.Equal("Christof", meta.Speaker);
        Assert.Equal(["prague.nls"], meta.Nls);
        Assert.Equal(["prague.ntx"], meta.Ntx);
        Assert.Equal(["game.nvo"], meta.Nvo);
        // What `Stow.Put`'s money branch is about, named per chronicle.
        Assert.Equal(["GoldBag", "GoldCoins"], meta.Cash);
        Assert.Empty(meta.Ignored);
    }
}
