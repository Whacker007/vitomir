// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Formats;

namespace Vitomir.Formats.Tests;

/// <summary>
/// `.nvo` — the words an `.nco` only names. Two identifier columns of 32 and then the text.
/// </summary>
public class NvoTests
{
    static NvoStrings Parse(string text) => NvoStrings.Read(Encoding.Latin1.GetBytes(text));

    [Fact]
    public void A_row_is_two_padded_identifiers_and_the_text()
    {
        var nvo = Parse("// a comment\r\n\r\n"
                      + "Libussa_10_2_455".PadRight(32) + "Libussa_10_2_455".PadRight(32) + "He hath taken me.\r\n"
                      + "Anezka_1_1_2".PadRight(32) + "Anezka_1_1_2".PadRight(32) + "Christof, wait.\r\n");
        Assert.Equal(2, nvo.Count);
        Assert.Equal("He hath taken me.", nvo["Libussa_10_2_455"]);
        Assert.Equal("Christof, wait.", nvo["anezka_1_1_2"]);      // keys are case-insensitive
        Assert.Null(nvo["nobody_0_0_0"]);
        Assert.Empty(nvo.Dropped);
        Assert.Empty(nvo.Mismatched);
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored_and_a_short_line_is_reported()
    {
        var nvo = Parse("//\r\n\r\nnot a row\r\n");
        Assert.Equal(0, nvo.Count);
        Assert.Equal(["not a row"], nvo.Dropped);
    }

    /// <summary>
    /// **All three shipped files, read whole.** The check that matters is that nothing is dropped —
    /// a row this reader could not take would be a line of dialogue the game has and Vitomir does
    /// not — and that the two identifier columns never disagree, which is what says the second one
    /// carries no information.
    ///
    /// Then the join the whole format exists for: **1,749 of the 1,760 identifiers the 310 shipped
    /// conversations use resolve to words**, and the eleven that do not are 1.1's own
    /// gap.
    /// </summary>
    [Fact]
    public void Every_shipped_line_of_dialogue_reads_and_the_conversations_find_theirs()
    {
        using var local = GameData.Open("Local_Eng.nob");
        var nvo = new NvoStrings();
        foreach (var name in NvoStrings.Files) nvo.Add(local.Read(name));

        Assert.Empty(nvo.Dropped);
        Assert.Empty(nvo.Mismatched);
        Assert.Equal(1_702, nvo.Count);

        using var resource = GameData.Open("Resource.nob");
        int asked = 0, found = 0;
        foreach (var file in resource.NamesWithExtension(".nco"))
            foreach (var talk in NcoFile.Read(resource.Read(file), file).Conversations)
                foreach (var e in talk.Events)
                    foreach (var step in e.Steps)
                        if (step.Kind is NcoStep.Line or NcoStep.AnswerGoto or NcoStep.AnswerReturn)
                        {
                            asked++;
                            if (nvo[step.Id] is not null) found++;
                        }
        Assert.Equal(1_760, asked);
        Assert.Equal(1_749, found);
    }
}
