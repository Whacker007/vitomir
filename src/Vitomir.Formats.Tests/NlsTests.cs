// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The string tables. Thirteen ship in `Local_Eng.nob/strings/`, 3,250 lines between them, and
/// they are the difference between a character sheet that says `UI_STATUS_HUMAN0` and one that
/// says **Acolyte**.
/// </summary>
public class NlsTests
{
    const string Sample = """
        // ****************************************************************
        // SYSTEM.NLS
        // ****************************************************************

        STRID_COMPANY                   Nihilistic Software, Inc.
        UI_GENERAL_VAMPIRE              Kindred
        UI_GENERAL_HUMAN                Human
        UI_GENERAL_VAMPIRE              Something else entirely
        PROCLAMATION                    Council Proclamation #1
           # an indented hash is still a comment
        	// and so is an indented pair of slashes
        """;

    [Fact]
    public void A_line_is_a_key_then_the_rest_of_it_verbatim()
    {
        var nls = NlsFile.Parse(Sample, "system.nls");
        // The value keeps its spaces, commas and full stops: it is prose, not a token.
        Assert.Equal("Nihilistic Software, Inc.", nls.Find("STRID_COMPANY"));
        Assert.Equal("Human", nls.Find("UI_GENERAL_HUMAN"));
        Assert.Equal(4, nls.Entries.Count);                        // the indented comments are comments
        Assert.Empty(nls.Warnings);
    }

    /// <summary>
    /// **A comment is a whole line, never a tail.** The reader tests for `#` and `//` only at the first character
    /// past the line's leading blanks and copies the value to the carriage return,
    /// so a `#` or a `//` inside a value is text and blanks at its end are kept. 32 shipped strings
    /// carry a `#` ("Council Proclamation #1"); Vitomir cut them there until then.
    /// </summary>
    [Fact]
    public void A_hash_or_slashes_inside_a_value_are_text_and_its_end_is_verbatim()
    {
        var nls = NlsFile.Parse(Sample, "system.nls");
        Assert.Equal("Council Proclamation #1", nls.Find("PROCLAMATION"));
        var tail = NlsFile.Parse("RANK   Acolyte      // the first human rank\r\nPAD   Healing Ointment  \r\n", "t.nls");
        Assert.Equal("Acolyte      // the first human rank", tail.Find("RANK"));
        Assert.Equal("Healing Ointment  ", tail.Find("PAD"));      // up to the carriage return, blanks and all
    }

    [Fact]
    public void Keys_are_case_insensitive_because_the_engine_folds_both_ends()
    {
        // The case fold is applied to the key on the store and on the lookup.
        var nls = NlsFile.Parse(Sample, "system.nls");
        Assert.Equal("Human", nls.Find("ui_general_human"));
        Assert.Equal("Human", nls.Find("Ui_General_Human"));
        Assert.Null(nls.Find("UI_GENERAL_NOBODY"));
    }

    /// <summary>
    /// **The first file to state a key wins, and so does the first line.** The loader inserts
    /// without looking first; the map itself walks the bucket and, on a key it already holds,
    /// returns 0 having stored nothing. This is the property a mod loader has to know about,
    /// because it is the opposite of load-order override.
    /// </summary>
    [Fact]
    public void A_key_stated_twice_keeps_the_first_value()
    {
        var nls = NlsFile.Parse(Sample, "system.nls");
        Assert.Equal("Kindred", nls.Find("UI_GENERAL_VAMPIRE"));
        Assert.Equal(["UI_GENERAL_VAMPIRE"], nls.Duplicates);
    }

    [Fact]
    public void A_key_with_nothing_after_it_is_reported_rather_than_stored_empty()
    {
        var nls = NlsFile.Parse("GOOD    yes\nLONELY\nALSO_LONELY   \r\nNOT_LONELY   // a value, not a comment\n", "t.nls");
        Assert.Equal("yes", nls.Find("GOOD"));
        Assert.Null(nls.Find("LONELY"));
        Assert.Null(nls.Find("ALSO_LONELY"));                      // nothing but blanks to the carriage return
        Assert.Equal("// a value, not a comment", nls.Find("NOT_LONELY"));
        Assert.Equal(2, nls.Warnings.Count);
    }

    [Theory]
    [InlineData("system", "strings/system.nls")]
    [InlineData("system.nls", "strings/system.nls")]
    [InlineData("new_york", "strings/new_york.nls")]
    public void The_loader_builds_the_path_the_way_the_original_does(string name, string path) =>
        Assert.Equal(path, NlsFile.PathFor(name));

    /// <summary>Every shipped table, from the player's own install.</summary>
    [Fact]
    public void Every_shipped_string_table_parses_and_the_four_the_engine_loads_are_there()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        var files = nob.NamesWithExtension(NlsFile.Extension).ToList();
        Assert.SkipWhen(files.Count == 0, "no .nls entries in Local_Eng.nob");
        Assert.Equal(13, files.Count);

        int entries = 0;
        var warnings = new List<string>();
        foreach (var path in files)
        {
            var nls = NlsFile.Read(nob.Read(path), path);
            entries += nls.Entries.Count;
            warnings.AddRange(nls.Warnings);
        }
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));
        Assert.True(entries > 3000, $"only {entries} strings; the shipped tables hold about 3,100");

        // The four the game loads at start-up are all present, by the names it uses.
        foreach (var name in NlsFile.Startup)
            Assert.Contains(NlsFile.PathFor(name), files, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The two keys that were showing raw on the character sheet, resolved.</summary>
    [Fact]
    public void The_character_sheet_s_own_keys_say_what_the_original_says()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        Assert.SkipUnless(nob.TryRead(NlsFile.PathFor("system"), out var data), "no strings/system.nls");
        var nls = NlsFile.Read(data, "system.nls");
        Assert.Equal("Human", nls.Find("UI_GENERAL_HUMAN"));
        Assert.Equal("Ghoul", nls.Find("UI_GENERAL_GHOUL"));
        Assert.Equal("Kindred", nls.Find("UI_GENERAL_VAMPIRE"));   // not "Vampire" — the game's own word
        Assert.Equal("Acolyte", nls.Find("UI_STATUS_HUMAN0"));
    }

    /// <summary>
    /// The shipped strings with a `#` in them keep it — 32 of them across the tables (a count, not their text).
    /// </summary>
    [Fact]
    public void The_shipped_strings_with_a_hash_keep_it()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        var files = nob.NamesWithExtension(NlsFile.Extension).ToList();
        Assert.SkipWhen(files.Count == 0, "no .nls entries in Local_Eng.nob");
        int hashes = files.Sum(path => NlsFile.Read(nob.Read(path), path).Entries.Values.Count(v => v.Contains('#')));
        Assert.Equal(32, hashes);
    }
}
