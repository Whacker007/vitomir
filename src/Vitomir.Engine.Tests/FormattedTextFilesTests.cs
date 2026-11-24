// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The original's text data files laid out as tables** (ours, <see cref="TextTable"/>, <c>vitomir-tools --format</c>): every one the
/// install has reads exactly as it did, through the reader the game reads it with, and a second pass changes nothing; and the check
/// would see a changed value.
/// </summary>
public sealed class FormattedTextFilesTests
{
    static List<(string Path, byte[] Data)> Shipped()
    {
        using var files = Install.Open();
        return [.. files.List("misc/").Where(TextTable.Knows).Where(p => p.IndexOf('/', 5) < 0).Select(p => (p, files.Read(p)))];
    }

    [Fact]
    public void Every_shipped_text_data_file_reads_the_same_laid_out()
    {
        var shipped = Shipped();
        Assert.True(shipped.Count >= 24, $"{shipped.Count} files");
        foreach (var ext in TextTable.Extensions) Assert.Contains(shipped, f => f.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        foreach (var (path, data) in shipped)
        {
            var laid = FormatCommands.Formatted(path, data, out var why);
            Assert.True(laid is not null, $"{path}: {why}");
            Assert.False(laid.AsSpan().SequenceEqual(data), $"{path} was already laid out");
        }
    }

    [Fact]
    public void The_check_sees_a_changed_value()
    {
        foreach (var (path, data) in Shipped().GroupBy(f => Path.GetExtension(f.Path).ToLowerInvariant()).Select(g => g.First()))
        {
            string text = System.Text.Encoding.Latin1.GetString(data);
            // The first data line's values, each with one more character on it in turn: the reading sees it (the values the game reads; a
            // clan's icon, say, it does not).
            var lines = text.Split('\n');
            int i = Array.FindIndex(lines, l => l.Trim() is { Length: > 0 } t && !t.StartsWith('#') && !t.StartsWith("//") && !t.StartsWith(';')
                                                && TextTable.Values(t).Count > 1);
            var values = TextTable.Values(lines[i].TrimEnd());
            string reading = FormatCommands.Reading(path, data);
            int seen = 0, from = 0;
            foreach (var v in values)
            {
                int at = lines[i].IndexOf(v, from, StringComparison.Ordinal);
                from = at + v.Length;
                var copy = (string[])lines.Clone();
                copy[i] = lines[i].Insert(at + v.Length, "9");
                if (FormatCommands.Reading(path, System.Text.Encoding.Latin1.GetBytes(string.Join('\n', copy))) != reading) seen++;
            }
            Assert.True(seen * 2 >= values.Count, $"{path}: {seen} of {values.Count} changes seen on {lines[i].TrimEnd()}");
        }
    }
}
