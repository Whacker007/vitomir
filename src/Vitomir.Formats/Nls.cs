// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `.NLS` — the **string table**, and the last thing between Vitomir's panes and readable English.
//
// Found by chasing the `UI_GENERAL_HUMAN` and `UI_STATUS_HUMAN0` that the character
// sheet was showing as raw keys. The game builds two tables at start-up — a file
// register of 0x18 and a string map of 0x1000 — and loads **`system`, `game`, `effects`,
// `chronicles`**, in that order, through the loader, which appends the extension `nls`. Thirteen
// `.nls` ship, all in `Local_Eng.nob/strings/`: those four, `credits`, the five chronicle tables
// (`prague`, `prague2`, `vienna`, `london`, `new_york`) and three multiplayer ones. 3,250 lines
// between them.
//
// The format is one entry a line: a key, whitespace, then **the rest of the line verbatim** —
// spaces, commas, apostrophes, a `#` and a `//` included, since the value is prose — up to the carriage
// return or newline. A line is a comment only when its first character past any leading
// spaces and tabs is `#` or `//`;
// a `#` further along is text, which is how 32 shipped strings end in one ("Council Proclamation #1").
// Keys are folded to upper case on both the store and the lookup, so they are
// case-insensitive.
//
// **A duplicate key keeps the first one.** The loader inserts without looking first, and the hash
// map itself refuses a key it already holds — it walks the bucket, and on a match returns 0
// having stored nothing. So load order decides, and *earlier wins*: 182 of the 3,250 lines are
// duplicates, `CAITIFF` is in both `system.nls` and `game.nls`, and system loads first. That is the
// opposite of a mod model where the last file wins, which is worth knowing before a loader
// is written for it.

namespace Vitomir.Formats;

/// <summary>One string table. Keys are upper case; <see cref="Find"/> folds what it is given.</summary>
public sealed class NlsFile
{
    public string File { get; private set; } = "";

    /// <summary>Every entry, in file order, keyed upper case.</summary>
    public IReadOnlyDictionary<string, string> Entries => entries;
    readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys this file states more than once. The engine keeps the **first**, and so does this, but
    /// a table that shadows itself is worth being able to see — a mod will do it on purpose.
    /// </summary>
    public IReadOnlyList<string> Duplicates => duplicates;
    readonly List<string> duplicates = [];

    /// <summary>Lines that carried a key and no value at all.</summary>
    public IReadOnlyList<string> Warnings => warnings;
    readonly List<string> warnings = [];

    /// <summary>The folder the shipped tables live in, inside `Local_Eng.nob`.</summary>
    public const string Folder = "strings/";
    public const string Extension = ".nls";

    /// <summary>The four the engine loads at start-up, in the order it loads them.</summary>
    public static readonly string[] Startup = ["system", "game", "effects", "chronicles"];

    /// <summary>`strings/<paramref name="name"/>.nls`, with an extension already on it left alone.</summary>
    public static string PathFor(string name) =>
        Folder + (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                  ? name[..^Extension.Length] : name) + Extension;

    public string? Find(string key) =>
        entries.TryGetValue(key.ToUpperInvariant(), out var value) ? value : null;

    public static NlsFile Read(byte[] data, string name) =>
        Parse(System.Text.Encoding.Latin1.GetString(data), name);

    /// <summary>
    /// The longest line the loader reads: it fills a 0x400-byte buffer, and the rest of a longer line is read
    /// and thrown away (until the newline). No shipped line comes near it.
    /// </summary>
    public const int LineLimit = 0x3ff;

    /// <summary>
    /// 1.1's reading, line by line: the line ends at its carriage return or newline; spaces and tabs
    /// before the key are skipped; a line whose first character past them is `#`, `//`, or the line's
    /// end is a comment or blank — **only there**, so a `#` or a `//` later in the line is
    /// part of the value; the key runs to the next space or tab; the blanks after it are skipped
    /// and **the value is the rest of the line verbatim**, blanks at its end included (copied up to the
    /// carriage return). **Ours**: 1.1 scans a key 32 characters at most and a longer one runs into its
    /// value; here the key is whole (the longest shipped is 31).
    /// </summary>
    public static NlsFile Parse(string text, string name = "")
    {
        var nls = new NlsFile { File = name };
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Length > LineLimit ? raw[..LineLimit] : raw;
            int cr = line.IndexOf('\r');
            if (cr >= 0) line = line[..cr];

            int at = SkipBlanks(line, 0);
            if (at == line.Length || line[at] == '#' || string.CompareOrdinal(line, at, "//", 0, 2) == 0) continue;

            int gap = at;
            while (gap < line.Length && !Blank(line[gap])) gap++;
            string key = line[at..gap].ToUpperInvariant();
            int from = SkipBlanks(line, gap);
            // 1.1 stores nothing for a key with no value (nothing counted, nothing kept); we say so.
            if (from == line.Length) { nls.warnings.Add($"{name}: '{key}' has a key and no value"); continue; }

            // First wins, the way 1.1's map does it: the bucket walk finds the key and stores nothing.
            if (!nls.entries.TryAdd(key, line[from..])) nls.duplicates.Add(key);
        }
        return nls;
    }

    /// <summary>The loader's blanks: a space and a tab, nothing else.</summary>
    static bool Blank(char c) => c is ' ' or '\t';

    static int SkipBlanks(string line, int at)
    {
        while (at < line.Length && Blank(line[at])) at++;
        return at;
    }
}
