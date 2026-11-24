// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **The formatter** (ours, <see cref="TextTable"/>): values never changed, only the white space between them; comments and blank lines
/// kept; a table's rows lined up across a file's sections, a keyword's lines with each other, a comment naming the columns with them;
/// a long value pushing only its own line along; the line ends kept; and laying a laid-out file out again changes nothing.
/// </summary>
public sealed class TextTableTests
{
    static string[] Lines(string text) => text.Split("\r\n");

    static List<List<string>> Words(string text) =>
        [.. text.Replace("\r\n", "\n").Split('\n').Select(l => l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList())];

    [Fact]
    public void Only_the_white_space_between_values_changes()
    {
        const string text = "# a table\r\nalpha\t1  0x20\r\nb 22\t\t\"two words\"   x\r\n\r\n  // a note   \r\nccc 3 4\r\n";
        string laid = TextTable.Format(text);
        Assert.Equal(Words(text).Select(w => string.Join(' ', w)), Words(laid).Select(w => string.Join(' ', w)));
        Assert.Contains("\"two words\"", laid);
        Assert.Equal("  // a note", Lines(laid)[4]);
        Assert.EndsWith("\r\n", laid);
        Assert.DoesNotContain('\t', laid.Replace("\"two words\"", ""));
    }

    [Fact]
    public void A_table_s_rows_line_up_across_its_sections()
    {
        string laid = TextTable.Format("# one\r\nfeed common 6\r\nbloodHealing common 1\r\n# two\r\nawe presence 3\r\n");
        var lines = Lines(laid);
        Assert.Equal("feed          common    6", lines[1]);
        Assert.Equal("bloodHealing  common    1", lines[2]);
        Assert.Equal("awe           presence  3", lines[4]);
    }

    [Fact]
    public void A_keyword_s_lines_line_up_with_each_other_and_a_header_with_its_rows()
    {
        string laid = TextTable.Format("CLASS: 1\r\nCITEM: baseloot 30 cash0\r\n// x\r\nCLASS: 2\r\nCITEM: a 5 -\r\n\r\n// STATS min max\r\nstrength 0 100\r\nwits 0 5\r\n");
        var lines = Lines(laid);
        Assert.Equal("CITEM:  baseloot  30  cash0", lines[1]);
        Assert.Equal("CITEM:  a         5   -", lines[4]);
        Assert.Equal("// STATS  min  max", lines[6]);
        Assert.Equal("strength  0    100", lines[7]);
    }

    [Fact]
    public void A_long_value_pushes_only_its_own_line_along()
    {
        string longName = new('n', TextTable.MaxWidth + 10);
        var lines = Lines(TextTable.Format($"a 1 2\r\n{longName} 1 2\r\n"));
        Assert.Equal($"a{new string(' ', TextTable.MaxWidth + TextTable.Gap - 1)}1  2", lines[0]);
        Assert.Equal($"{longName}  1  2", lines[1]);
    }

    [Fact]
    public void The_line_ends_are_kept_and_a_second_pass_changes_nothing()
    {
        string laid = TextTable.Format("a  1\nbb 2");
        Assert.Equal("a   1\r\nbb  2", laid);
        Assert.Equal(laid, TextTable.Format(laid));
    }

    [Fact]
    public void The_check_holds_a_layout_to_the_same_values_on_the_same_lines()
    {
        Assert.True(TextTable.SameValues("a  1\nb\t2", "a 1\nb 2", out _));
        Assert.False(TextTable.SameValues("a 1\nb 2", "a 1 b 2", out var lines));
        Assert.Contains("lines", lines);
        Assert.False(TextTable.SameValues("a 1", "a 2", out var values));
        Assert.Contains("values", values);
        string ys = new('y', TextTable.LineLimit - 2);
        Assert.False(TextTable.SameValues("x " + ys, "x   " + ys, out var grew));                   // past the limit, and longer
        Assert.Contains("grew", grew);
        Assert.True(TextTable.SameValues("x     " + ys, "x   " + ys, out _));                         // past it, but no longer
        Assert.True(TextTable.Checked("a  1\r\nbb 2\r\n"u8.ToArray(), out _) is { } laid && System.Text.Encoding.Latin1.GetString(laid) == "a   1\r\nbb  2\r\n");
        Assert.Null(TextTable.Checked("a 1\rb 2\r\n"u8.ToArray(), out var cr));
        Assert.Contains("carriage return", cr);
    }
}
