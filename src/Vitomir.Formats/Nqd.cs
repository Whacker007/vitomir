// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A quest definition (`quests/*.nqd`), read the way the game reads one.
//
// ```
// # a comment — '#', ';' or '//' as the first characters of the line
// QUEST 0 0 Q_PRAGUEBYNIGHT_S Q_PRAGUEBYNIGHT_L QuestCheck.tga 300
// QITEM 2 0 Q_STEP_S Q_STEP_L unused.tga 50
// ```
//
// A line is `sscanf(line, "%s %d %d %s %s %s %u")` and counts only when all seven convert. The
// first word is `quest` or `qitem` (the line is lower-cased first, so either case). For `quest` the
// numbers are **how many items** it has, its **flags**, the two string keys, its **icon** and the
// **experience** completing it is worth; for `qitem` the first number is **how many steps** instead.
// All 45 shipped quests are one `QUEST` line and nothing else.
namespace Vitomir.Formats;

/// <summary>One `qitem` line: a part of a quest.</summary>
/// <param name="Steps">How many steps it has; a line that says fewer than 1 gets 1.</param>
/// <param name="Flags">`QUESTITEM_FLAG_*` it starts with: 1 complete, 2 hidden.</param>
/// <param name="Short">The string key for its name.</param>
/// <param name="Long">The string key for its description.</param>
/// <param name="Icon">
/// The icon the line names — **which the engine never loads**: the item's texture is made from the
/// quest's own icon name, so this field is parsed and dropped.
/// </param>
/// <param name="Xp">What completing it is worth, to every living member of the party.</param>
public sealed record NqdItem(int Steps, int Flags, string Short, string Long, string Icon, uint Xp);

/// <summary>A quest file, as its lines said it.</summary>
/// <param name="Found">
/// Whether a `quest` line was read at all. The loader does **not** fail without one (it succeeds
/// once the file has opened): the quest goes in the log nameless and blank.
/// </param>
/// <param name="ItemCount">How many items the `quest` line asked for: the size of the item array.</param>
/// <param name="Items">
/// Exactly <paramref name="ItemCount"/> entries: the `qitem` lines after the `quest` line, in order,
/// and <see cref="Nqd.Blank"/> for any the file did not fill.
/// </param>
public sealed record NqdQuest(bool Found, int Flags, int ItemCount, string Short, string Long, string Icon, uint Xp,
                              IReadOnlyList<NqdItem> Items);

public static class Nqd
{
    /// <summary>The engine reads a line with <c>fgets</c> into 0x200 bytes (at most 0x1ff characters).</summary>
    public const int LineMax = 0x1ff;

    /// <summary>
    /// Every line the loader would act on, in order, as (keyword, the six values). The keyword is
    /// lower-cased; a line that fails any of the gates is not here.
    /// </summary>
    public static IEnumerable<(string Word, int A, int B, string Short, string Long, string Icon, uint Xp)> Lines(byte[] data)
    {
        foreach (var raw in Split(data))
        {
            var line = raw;
            if (line.Length == 0) continue;
            // The comment test is on the raw line, before anything else.
            if (line[0] == ';' || line[0] == '#' || (line[0] == '/' && line.Length > 1 && line[1] == '/')) continue;
            line = line.ToLowerInvariant();                               // the game lower-cases each line
            // `strspn(line, " \r\t\n") == 0`: a line that *starts* with white space is skipped
            // outright, which is also what drops a blank line.
            if (" \r\t\n".Contains(line[0])) continue;
            if (Scan(line) is { } fields) yield return fields;
        }
    }

    /// <summary>Reads a whole file the way the game does.</summary>
    /// <param name="iconLoads">
    /// Whether an icon texture loads. The loader counts a `qitem` as read only once the texture it
    /// makes for it has loaded — and that texture is the **quest's** icon — so with the
    /// quest's icon missing every `qitem` lands in the first slot, each overwriting the last.
    /// Null is "everything loads".
    /// </param>
    public static NqdQuest Read(byte[] data, Func<string, bool>? iconLoads = null)
    {
        bool found = false;
        int flags = 0, count = 0;
        string shortKey = "", longKey = "", icon = "";
        uint xp = 0;
        NqdItem[] items = [];
        int read = 0;                                                     // `local_4a8`: never reset
        foreach (var (word, a, b, s, l, i, x) in Lines(data))
        {
            if (word == "quest")
            {
                // The item array is made at the `quest` line, `ItemCount` entries, all zero until a
                // `qitem` fills one. A second `quest` line makes a fresh array but does not reset
                // how many items have been read — which only matters to a file the game does not ship.
                found = true;
                count = a;
                flags = b;
                shortKey = s; longKey = l; icon = i; xp = x;
                items = count > 0 ? Enumerable.Repeat(Blank, count).ToArray() : [];
            }
            else if (word == "qitem" && found && read != count && read < count)
            {
                items[read] = new NqdItem(a < 1 ? 1 : a, b, s, l, i, x);
                if (iconLoads?.Invoke(icon) ?? true) read++;
            }
        }
        return new NqdQuest(found, flags, count, shortKey, longKey, icon, xp, items);
    }

    /// <summary>An item the `quest` line made room for and no `qitem` line filled.</summary>
    public static readonly NqdItem Blank = new(0, 0, "", "", "", 0);

    /// <summary><c>fgets</c> into a 0x200-byte buffer: a longer line comes back in pieces.</summary>
    static IEnumerable<string> Split(byte[] data)
    {
        int start = 0;
        while (start < data.Length)
        {
            int end = start;
            while (end < data.Length && data[end] != (byte)'\n' && end - start < LineMax - 1) end++;
            if (end < data.Length && data[end] == (byte)'\n') end++;
            yield return System.Text.Encoding.Latin1.GetString(data, start, end - start);
            start = end;
        }
    }

    /// <summary>`sscanf("%s %d %d %s %s %s %u")` with the C library's rules, or null short of seven.</summary>
    static (string, int, int, string, string, string, uint)? Scan(string line)
    {
        int p = 0;
        if (Word(line, ref p) is not { } word) return null;
        if (Int(line, ref p) is not { } a) return null;
        if (Int(line, ref p) is not { } b) return null;
        if (Word(line, ref p) is not { } s) return null;
        if (Word(line, ref p) is not { } l) return null;
        if (Word(line, ref p) is not { } i) return null;
        if (Int(line, ref p) is not { } x) return null;
        return (word, a, b, s, l, i, unchecked((uint)x));
    }

    static void Skip(string s, ref int p) { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; }

    static string? Word(string s, ref int p)
    {
        Skip(s, ref p);
        int start = p;
        while (p < s.Length && !char.IsWhiteSpace(s[p])) p++;
        return p > start ? s[start..p] : null;
    }

    /// <summary>`%d` and `%u` alike: an optional sign and at least one digit, stopping at the first non-digit.</summary>
    static int? Int(string s, ref int p)
    {
        Skip(s, ref p);
        int q = p;
        bool negative = false;
        if (q < s.Length && (s[q] == '+' || s[q] == '-')) { negative = s[q] == '-'; q++; }
        int digits = q;
        uint value = 0;
        while (q < s.Length && s[q] is >= '0' and <= '9') { value = unchecked(value * 10 + (uint)(s[q] - '0')); q++; }
        if (q == digits) return null;
        p = q;
        return unchecked(negative ? -(int)value : (int)value);
    }
}
