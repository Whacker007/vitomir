// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The original game's text files laid out as tables (ours): the formatter.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// **The formatter for the original's text files** (ours): the discipline, effect, clan, treasure and particle files
/// (<c>misc/game.*</c>, <c>misc/effects.ned</c>) and the player characters' files (<c>misc/*.npc</c>) are whitespace-separated columns
/// that the original's authors lined up with a mix of tabs and spaces, so they line up only at one tab width and often not at all.
/// This lays every line's values out again as a table, with spaces:
/// <list type="bullet">
/// <item>**A value is never changed** — only the white space between values: the same words in the same order on the same lines, a
/// value in quotes kept whole.</item>
/// <item>**Comment lines** (<c>#</c>, <c>//</c>, <c>;</c>) are kept as they are, less trailing spaces; so are blank lines. A comment
/// that names the columns of the line under it (as many words after its mark as the line has values, like a player file's
/// <c>// STATS min max current</c>) is lined up with them.</item>
/// <item>**Columns**: the rows of a table — a run of data lines (no blank or comment line between them) whose first words differ, or a
/// line alone —
/// share their column widths with every row of the file with as many values, so a file's tables line up across its sections; a
/// longer run of lines of one word (a player file's <c>item</c> lines) shares them within the run; and a line whose first word ends in a colon
/// (<c>game.ntt</c>'s <c>CLASS:</c>, <c>CITEM:</c> …) shares them with every line of the same word in the file. Two spaces between columns; a column is at most <see cref="MaxWidth"/> characters wide, and a longer value only pushes its
/// own line's next values along.</item>
/// <item>Windows line ends, as the originals have; a file that ends without one keeps ending without one.</item>
/// </list>
/// The readers split on white space, a line at a time, so a laid-out file reads exactly as the original does. <see cref="Checked"/> holds
/// every laid-out file to that (the same values on the same lines, no line longer than a reader takes whole, a second pass changing
/// nothing) and the recooking (<see cref="Recooked"/>) and <c>vitomir-tools --format</c> lay out nothing that fails it; the tools and the
/// tests also read each file both ways through the game's own reader and compare everything it reads.
/// </summary>
public static class TextTable
{
    /// <summary>The widest a column is laid out; a longer value overflows its own line only.</summary>
    public const int MaxWidth = 36;

    /// <summary>The space between two columns.</summary>
    public const int Gap = 2;

    /// <summary>The files the formatter knows, by extension (the original's own text data).</summary>
    public static readonly string[] Extensions = [".ndd", ".ndg", ".ned", ".ncd", ".ntt", ".npd", ".npc"];

    public static bool Knows(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    static bool IsComment(string trimmed) => trimmed.StartsWith('#') || trimmed.StartsWith("//") || trimmed.StartsWith(';');

    static string Mark(string trimmed) => trimmed.StartsWith("//") ? "//" : trimmed[..1];

    /// <summary>A line's values: split on spaces and tabs, a run in double quotes kept whole with its quotes.</summary>
    public static List<string> Values(string line)
    {
        var values = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && line[i] is ' ' or '\t') i++;
            if (i >= line.Length) break;
            int from = i;
            if (line[i] == '"')
            {
                int close = line.IndexOf('"', i + 1);
                i = close < 0 ? line.Length : close + 1;
                while (i < line.Length && line[i] is not (' ' or '\t')) i++;
            }
            else
                while (i < line.Length && line[i] is not (' ' or '\t')) i++;
            values.Add(line[from..i]);
        }
        return values;
    }

    /// <summary>The file's text laid out as a table (<see cref="TextTable"/>).</summary>
    public static string Format(string text)
    {
        bool endsWithBreak = text.EndsWith('\n');
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (endsWithBreak) lines = lines[..^1];

        // What each line is: a comment, blank, or data (its values, and the group whose widths it shares: its keyword's, or its run's).
        var kinds = new (bool Data, List<string> Values, string Group)[lines.Length];
        int run = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || IsComment(t)) { kinds[i] = (false, [], ""); continue; }
            var v = Values(t);
            if (i == 0 || !kinds[i - 1].Data) run++;
            kinds[i] = (true, v, v[0].EndsWith(':') ? v[0].ToUpperInvariant() : "run " + run);
        }
        // A run of rows of a table (one line, or first words that differ: a name each) shares its widths with every row of the file as long.
        foreach (var r in Enumerable.Range(0, lines.Length).Where(i => kinds[i].Group.StartsWith("run ")).GroupBy(i => kinds[i].Group).ToList())
            if (r.Count() == 1 || r.Select(i => kinds[i].Values[0].ToLowerInvariant()).Distinct().Count() > 1)
                foreach (int i in r) kinds[i].Group = "values " + kinds[i].Values.Count;
        // A comment naming the columns of the data line under it joins that line's group, its mark on its first word.
        var headers = new Dictionary<int, List<string>>();
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (!IsComment(t) || !kinds[i + 1].Data) continue;
            // Its first word keeps its mark as it has it: apart (`// STATS`, joined as one column) or on it (`;maxparticles`).
            var words = Values(t);
            if (words.Count > 1 && words[0] == Mark(t)) { words[1] = words[0] + " " + words[1]; words.RemoveAt(0); }
            if (words.Count < 2 || words.Count != kinds[i + 1].Values.Count) continue;
            headers[i] = words;
        }
        var widths = new Dictionary<string, List<int>>();
        void Measure(string group, List<string> values, int from)
        {
            if (!widths.TryGetValue(group, out var w)) widths[group] = w = [];
            while (w.Count < from) w.Add(0);
            for (int c = from; c < values.Count - 1; c++)                 // the last value's width never matters
            {
                while (w.Count <= c) w.Add(0);
                w[c] = Math.Max(w[c], Math.Min(values[c].Length, MaxWidth));
            }
        }
        for (int i = 0; i < lines.Length; i++)
        {
            if (kinds[i].Data) Measure(kinds[i].Group, kinds[i].Values, 0);
            else if (headers.TryGetValue(i, out var h)) Measure(kinds[i + 1].Group, h, 1);   // its mark and first word overflow, not widen
        }

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            List<string>? values = kinds[i].Data ? kinds[i].Values : headers.GetValueOrDefault(i);
            string group = kinds[i].Data ? kinds[i].Group : headers.ContainsKey(i) ? kinds[i + 1].Group : "";
            if (values is null) sb.Append(lines[i].TrimEnd());
            else
            {
                var w = widths[group];
                int at = 0;
                for (int c = 0; c < values.Count; c++)
                {
                    if (c > 0)
                    {
                        int column = 0;
                        for (int k = 0; k < c; k++) column += w[k] + Gap;
                        int spaces = Math.Max(column - at, Gap);            // past a long value, the gap still
                        sb.Append(' ', spaces);
                        at += spaces;
                    }
                    sb.Append(values[c]);
                    at += values[c].Length;
                }
            }
            if (i < lines.Length - 1 || endsWithBreak) sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static byte[] Format(byte[] data) => Encoding.Latin1.GetBytes(Format(Encoding.Latin1.GetString(data)));

    /// <summary>The longest line the original's line reader takes whole (the particle file's <c>fgets</c> buffer, less its end).</summary>
    public const int LineLimit = 0x1ff;

    static readonly char[] Blanks = [' ', '\t', '\r', '\v', '\f'];

    /// <summary>
    /// Whether two texts are the same to every reader of these files: the same number of lines, each with the same values split on white
    /// space, and a line longer than <see cref="LineLimit"/> in the second only where the first's was as long.
    /// </summary>
    public static bool SameValues(string original, string laid, out string? why)
    {
        why = null;
        var a = original.Split('\n');
        var b = laid.Split('\n');
        if (a.Length != b.Length) { why = $"{a.Length} lines became {b.Length}"; return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Split(Blanks, StringSplitOptions.RemoveEmptyEntries).SequenceEqual(b[i].Split(Blanks, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal))
            { why = $"line {i + 1}'s values changed"; return false; }
            if (b[i].TrimEnd('\r').Length > LineLimit && b[i].Length > a[i].Length)
            { why = $"line {i + 1} grew past {LineLimit} characters"; return false; }
        }
        return true;
    }

    /// <summary>
    /// **The file laid out, or null with why not**: laid out (<see cref="Format(byte[])"/>) only when it is the same to every reader
    /// (<see cref="SameValues"/>) and laying it out again changes nothing. A file with a carriage return in a line's middle — a line
    /// end to one reader, a space to another — is left as it is.
    /// </summary>
    public static byte[]? Checked(byte[] data, out string? why)
    {
        string text = Encoding.Latin1.GetString(data);
        why = null;
        if (text.Replace("\r\n", "\n").Contains('\r')) { why = "a carriage return in a line's middle"; return null; }
        string laid = Format(text);
        if (Format(laid) != laid) why = "laying it out a second time changes it";
        else SameValues(text.Replace("\r\n", "\n"), laid.Replace("\r\n", "\n"), out why);
        return why is null ? Encoding.Latin1.GetBytes(laid) : null;
    }
}
