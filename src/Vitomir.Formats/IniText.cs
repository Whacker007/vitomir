// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's vitomir.ini changed as text, one line in place: the engine's settings writer and the
// launcher (which does not load the engine) share it.
namespace Vitomir.Formats;

/// <summary>
/// Reads and changes keys in INI text the way the engine's reader takes them (ours): sections and keys
/// match case-insensitively, a comment starts with <c>;</c> or <c>#</c>, and a key repeated in its
/// section counts by its last occurrence.
/// </summary>
public static class IniText
{
    /// <summary>The value of <paramref name="key"/> under <paramref name="section"/> (its last occurrence), or null.</summary>
    public static string? Value(string text, string section, string key)
    {
        string? value = null, current = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                current = close > 1 ? line[1..close].Trim() : "";
                continue;
            }
            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase) || line.Length == 0 || line[0] is ';' or '#') continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) value = line[(eq + 1)..].Trim();
        }
        return value;
    }

    /// <summary>Whether a switch's value reads as on, as the engine reads its switches (1, on, true, yes).</summary>
    public static bool On(string? value) => value?.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";

    /// <summary>
    /// Sets <paramref name="values"/> under <paramref name="section"/>. A key already in its section keeps its
    /// line, position and comments and only its value changes (every occurrence, when repeated); a missing key
    /// is added at the end of its section; a missing section at the end of the text.
    /// </summary>
    public static string Apply(string text, string section, IEnumerable<KeyValuePair<string, string>> values)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var pending = new List<KeyValuePair<string, string>>();
        foreach (var kv in values)
        {
            int at = pending.FindIndex(p => p.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) pending[at] = kv; else pending.Add(kv);
        }

        // Where the section starts and ends (the last line that belongs to it, blank lines at its
        // foot excluded so an added key sits with its fellows).
        int head = -1, last = -1;
        string current = "";
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                current = close > 1 ? line[1..close].Trim() : "";
                if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && head < 0) { head = i; last = i; }
                continue;
            }
            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > 0) last = i;
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            int found = pending.FindIndex(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (found < 0) continue;
            string indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
            string spacing = line[..eq].EndsWith(' ') ? " = " : "=";
            lines[i] = $"{indent}{key}{spacing}{pending[found].Value}";
            done.Add(key);
        }

        var missing = pending.Where(p => !done.Contains(p.Key)).Select(p => $"{p.Key} = {p.Value}").ToList();
        if (missing.Count > 0)
        {
            if (head < 0)
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add($"[{section}]");
                lines.AddRange(missing);
            }
            else lines.InsertRange(last + 1, missing);
        }
        return lines.Count == 0 ? "" : string.Join(newline, lines) + newline;
    }
}
