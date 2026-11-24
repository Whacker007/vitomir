// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// `misc/effects.ned` read the way 1.1's reader reads it. The rules
/// that matter are the ones a modified file would meet and the shipped one does not — **the first
/// line of a name wins**, and a stat, soak or damage effect whose subtype is out of range is dropped
/// without taking its name — so most of this is small synthetic files, and one test holds the
/// shipped file to reading exactly as it did before.
/// </summary>
public class NedTests
{
    /// <summary>One row in the file's own column order, with values 1 to 6.</summary>
    static string Row(string name, int type, int subtype, string script = "none", int row = 0, string tail = "") =>
        $"{name} none {row} {script} {type} {subtype} 6 0x0 0 0 0 1 2 3 4 5 6{tail}";

    [Fact]
    public void The_first_line_of_a_name_wins_and_a_later_one_is_dropped()
    {
        var ned = NedFile.Read(string.Join('\n',
            Row("ef_twice", 3, 0),
            Row("EF_Twice", 3, 1),
            Row("ef_twice", 3, 2)));

        Assert.Single(ned.All);
        Assert.Equal(0, ned["ef_twice"]!.Subtype);
        Assert.Equal(2, ned.Dropped.Count);
        Assert.All(ned.Dropped, d => Assert.Contains("the first one stands", d));
    }

    /// <summary>
    /// Types 1–3 name a stat, 0 to 18; types 4–8 a damage type, 0 to 9; the comparisons are unsigned,
    /// so a negative subtype is out of range; and no other type is checked.
    /// </summary>
    [Fact]
    public void A_stat_or_damage_effect_with_its_subtype_out_of_range_is_dropped()
    {
        var ned = NedFile.Read(string.Join('\n',
            Row("ef_stat18", 1, 18), Row("ef_stat19", 2, 19), Row("ef_statneg", 3, -1),
            Row("ef_soak9", 6, 9), Row("ef_soak10", 4, 10), Row("ef_dot10", 8, 10),
            Row("ef_wanted99", 22, 99)));

        Assert.Equal(new[] { "ef_stat18", "ef_soak9", "ef_wanted99" }, ned.All.Select(e => e.Name));
        Assert.Equal(4, ned.Dropped.Count);
        Assert.All(ned.Dropped, d => Assert.Contains("out of range", d));
    }

    /// <summary>
    /// The duplicate test comes first but a line only takes its name when it is kept, so
    /// an out-of-range line does not stop a later line of the same name.
    /// </summary>
    [Fact]
    public void A_line_dropped_for_its_subtype_does_not_take_its_name()
    {
        var ned = NedFile.Read(string.Join('\n', Row("ef_late", 3, 40), Row("ef_late", 3, 5)));

        Assert.Equal(5, ned["ef_late"]!.Subtype);
        Assert.Single(ned.Dropped);
    }

    /// <summary>
    /// What a line has to look like to be read at all: `;`, `#` and `//` open a comment; a line that
    /// starts with white space is skipped whole (`strspn` against " \r\t\n"); seventeen
    /// columns must be read and an eighteenth is never looked at; the name is lowered; a
    /// row past 1 is made 0; and the script keeps 31 characters.
    /// </summary>
    [Fact]
    public void Comments_indented_lines_extra_columns_names_rows_and_scripts_go_as_the_reader_has_them()
    {
        var longScript = new string('S', 40);
        var ned = NedFile.Read(string.Join("\r\n",
            "# a comment",
            "; another",
            "// " + Row("ef_slashed", 3, 0),
            "",
            "   " + Row("ef_indented", 3, 0),
            "\t# an indented comment",
            Row("EF_Loud", 3, 0, row: 2, script: longScript),
            Row("ef_long", 3, 0, tail: " 7 8 extra"),
            "ef_short none 0 none 3 0 6 0x0 0 0 0 1 2 3 4"));

        Assert.Equal(new[] { "ef_loud", "ef_long" }, ned.All.Select(e => e.Name));
        var loud = ned["ef_loud"]!;
        Assert.Equal(0, loud.Row);
        Assert.Equal(new string('S', 31), loud.Script);
        Assert.Equal(6f, ned["ef_long"]!.At(5));
        Assert.Null(ned["ef_slashed"]);
        Assert.Null(ned["ef_indented"]);
        Assert.Equal(2, ned.Dropped.Count);                          // the indented row and the short one
        Assert.Contains(ned.Dropped, d => d.Contains("white space"));
        Assert.Contains(ned.Dropped, d => d.Contains("15 columns"));
    }

    /// <summary>
    /// And the shipped file reads exactly as it did: all 187 rows, none dropped, every name already
    /// lower case as written, every row 0 or 1, no script past 31 characters, no stat or damage
    /// subtype out of range and no name twice — so none of the rules above changes a shipped line.
    /// </summary>
    [Fact]
    public void Every_shipped_line_reads_as_before()
    {
        using var nob = GameData.Open("Resource.nob");
        Assert.SkipUnless(nob.TryRead("misc/effects.ned", out var data), "misc/effects.ned not in Resource.nob");
        var ned = NedFile.Read(data);
        Assert.Empty(ned.Dropped);
        Assert.Equal(187, ned.All.Count);

        var text = System.Text.Encoding.Latin1.GetString(data);
        var written = text.Split('\n').Select(l => l.Trim())
                          .Where(l => l.Length > 0 && l[0] is not ('#' or ';'))
                          .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]).ToList();
        Assert.Equal(written, ned.All.Select(e => e.Name));
        Assert.All(ned.All, e => Assert.InRange(e.Row, 0, 1));
        Assert.All(ned.All, e => Assert.True(e.Script.Length <= NedFile.ScriptLength, e.Name));
    }
}
