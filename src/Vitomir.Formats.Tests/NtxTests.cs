// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The readable documents. Seven files, 57 entries, 232 paragraphs — and the one a play-test clicked.
/// </summary>
public class NtxTests
{
    const string Sample = """
        <TEXT CuthbertLetter Cuthbert_2_2_14.mp3 0>
        <TITLE Sir Cuthbert's Letter to Christof>
        <P 0xFFFFFFFF>
        My Good Kinsman Christof--
        <P 0xFF804020>
        Thy bravery carried the day
        in the battle of the Moravian hills.
        </TEXT>

        <TEXT Empty 0 3>
        <TITLE Nothing At All>
        </TEXT>
        """;

    [Fact]
    public void A_document_is_a_title_a_voice_and_its_paragraphs()
    {
        var ntx = NtxFile.Parse(Sample, "prague.ntx");
        Assert.Empty(ntx.Warnings);
        Assert.Equal(2, ntx.Entries.Count);

        var letter = ntx.Find("cuthbertletter")!;               // ids are case-insensitive
        Assert.Equal("Sir Cuthbert's Letter to Christof", letter.Title);
        Assert.Equal("Cuthbert_2_2_14.mp3", letter.Voice);
        Assert.Equal(0, letter.Flags);
        Assert.Equal(2, letter.Paragraphs.Count);

        // A paragraph's own line ends are kept, as 1.1 keeps them; the `<P>` tags are the
        // paragraphs.
        Assert.Equal("Thy bravery carried the day\nin the battle of the Moravian hills.",
                     letter.Paragraphs[1].Text);
        Assert.Equal(NtxFile.White, letter.Paragraphs[0].Colour);
        Assert.Equal(0xFF804020u, letter.Paragraphs[1].Colour);
    }

    [Fact]
    public void A_document_with_no_paragraphs_is_still_a_document()
    {
        var ntx = NtxFile.Parse(Sample, "prague.ntx");
        var empty = ntx.Find("Empty")!;
        Assert.Equal("Nothing At All", empty.Title);
        Assert.Empty(empty.Paragraphs);
        Assert.Equal(3, empty.Flags);                            // the flags field is read, not assumed
        Assert.Equal("", empty.Text);
    }

    [Fact]
    public void An_unclosed_block_and_stray_prose_are_both_reported()
    {
        var loose = NtxFile.Parse("this is not in a block\n<TEXT A 0 0>\n<TITLE A>\n", "t.ntx");
        Assert.Equal(2, loose.Warnings.Count);
        Assert.Empty(loose.Entries);
    }

    [Theory]
    [InlineData("prague", "strings/prague.ntx")]
    [InlineData("prague.ntx", "strings/prague.ntx")]
    [InlineData("new_york", "strings/new_york.ntx")]
    public void The_path_is_built_the_way_the_string_tables_beside_it_are(string name, string path) =>
        Assert.Equal(path, NtxFile.PathFor(name));

    /// <summary>Every shipped document, from the player's own install.</summary>
    [Fact]
    public void Every_shipped_document_parses_with_nothing_left_over()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        var files = nob.NamesWithExtension(NtxFile.Extension).ToList();
        Assert.SkipWhen(files.Count == 0, "no .ntx entries in Local_Eng.nob");
        Assert.Equal(7, files.Count);

        int entries = 0, paragraphs = 0;
        var warnings = new List<string>();
        foreach (var path in files)
        {
            var ntx = NtxFile.Read(nob.Read(path), path);
            warnings.AddRange(ntx.Warnings);
            entries += ntx.Entries.Count;
            foreach (var e in ntx.Entries)
            {
                paragraphs += e.Paragraphs.Count;
                Assert.False(string.IsNullOrWhiteSpace(e.Id), $"{path} has an entry with no id");
                Assert.False(string.IsNullOrWhiteSpace(e.Title), $"{path}: {e.Id} has no title");
                // Every shipped entry names a voice track, which is why the field is not optional.
                Assert.NotEqual("", e.Voice);
            }
        }
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));
        Assert.Equal(57, entries);
        Assert.Equal(232, paragraphs);
    }

    /// <summary>The letter Christof starts with, and the title the string table agrees on.</summary>
    [Fact]
    public void The_letter_in_christofs_belt_reads()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        Assert.SkipUnless(nob.TryRead(NtxFile.PathFor("prague"), out var data), "no strings/prague.ntx");
        var letter = NtxFile.Read(data, "prague.ntx").Find("CuthbertLetter");
        Assert.NotNull(letter);
        Assert.Equal("Sir Cuthbert's Letter to Christof", letter!.Title);
        Assert.Contains("My Good Kinsman Christof", letter.Paragraphs[0].Text);

        // And `DESCRIPTIONID` points at a string table entry that says the same thing.
        Assert.SkipUnless(nob.TryRead(NlsFile.PathFor("game"), out var nls), "no strings/game.nls");
        Assert.Equal(letter.Title, NlsFile.Read(nls, "game.nls").Find("CUTHBERTLETTER"));
    }
}
