// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What the read of the `Rpg` module's last 32 unread neighbours ("The
/// pass") found different, and fixed. Nearly all of them are start-ups, shut-downs, destructors and
/// dead copies; two were not. **The documents' reset** (from the metafile's read
/// 1.1 throws every document away and reads the new chronicle's list at each chronicle's
/// start, where Vitomir read the first chronicle's once a run, so Vienna's letters were missing after a
/// chronicle change. **A paragraph's line ends** (the branch the pass left unread): kept
/// as 1.1 keeps them, so the read pane breaks the line there instead of running it on.
/// </summary>
public class RpgNeighbourTests
{
    [Fact]
    public void A_paragraphs_own_line_ends_are_line_breaks_in_the_read_pane()
    {
        // A blank line inside the paragraph is no break: the engine skips a line that starts with
        // white space, and the text box's strtok drops an empty piece.
        var ntx = NtxFile.Parse("<TEXT Verse none 0>\r\n<TITLE Verse>\r\n<P 0xFFFFFFFF>\r\nIt is one line,\r\n\r\n"
                                + "And another.\r\n</TEXT>\r\n", "t.ntx");
        var doc = ntx.Find("Verse")!;
        Assert.Equal("It is one line,\nAnd another.", doc.Paragraphs[0].Text);

        // Both fit the box's 31 characters on one line; the kept line end breaks them all the same.
        var lines = ReadPaneTests.Pane(doc).Lines();
        Assert.Equal(["It is one line,", "And another."], lines.Select(l => l.Text));
    }

    /// <summary>The shipped files meet it once each in two tables: a two-line verse.</summary>
    [Fact]
    public void Prague_has_one_paragraph_of_two_lines()
    {
        using var files = Install.Open();
        if (!files.TryRead("strings/prague.ntx", out var raw)) { Assert.Skip("no strings/prague.ntx"); return; }
        var broken = NtxFile.Read(raw, "prague.ntx").Entries
                            .SelectMany(e => e.Paragraphs).Where(p => p.Text.Contains('\n')).ToList();
        var verse = Assert.Single(broken);
        Assert.Equal(2, verse.Text.Split('\n').Length);
    }

    [Fact]
    public void A_chronicle_change_reads_the_new_metafiles_documents_from_none()
    {
        using var files = Install.Open();
        if (!files.TryRead("Chronicles/prague.nmf", out var prague) || !files.TryRead("Chronicles/vienna.nmf", out var vienna)
            || !files.TryRead("strings/vienna.ntx", out var viennaNtx))
        {
            Assert.Skip("no prague and vienna in this install");
            return;
        }
        var documents = new Documents(files);
        documents.AddFrom(NmfFile.Read(prague, "prague.nmf"));                 // the run began in Prague
        var own = NtxFile.Read(viennaNtx, "vienna.ntx").Entries.Select(e => e.Id).First(id => documents.Find(id) is null);

        // ChangeChronicle("vienna"): the metafile's read frees them all, then reads vienna.nmf's list.
        Assert.Equal(2, documents.Restart(NmfFile.Read(vienna, "vienna.nmf")));
        Assert.NotNull(documents.Find(own));
        Assert.NotNull(documents.Find("CuthbertLetter"));                       // vienna.nmf names prague.ntx first
        Assert.Equal(2, documents.Loaded.Count);

        // A table the new metafile does not name is gone: every document is let go first.
        Assert.Equal(1, documents.Restart(NmfFile.Read("NTX vienna.ntx\n"u8, "only_vienna.nmf")));
        Assert.Null(documents.Find("CuthbertLetter"));
        Assert.NotNull(documents.Find(own));
    }
}
