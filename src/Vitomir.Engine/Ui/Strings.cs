// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's string table, as the engine holds it: several `.nls` files stacked into one lookup.
//
// The string tables' loader reads **system, game, effects, chronicles** at start-up and keeps a
// register of which files are in, so a chronicle's own table can be added later without reloading
// the rest. The important property: **the first file to state a key
// wins**, because the hash map refuses a key it already holds rather than replacing it.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Every loaded `.nls`, stacked. Ask it for a key; it answers the key back if nothing has it.</summary>
public sealed class Strings
{
    readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);
    readonly List<string> loaded = [];
    readonly GameFiles files;

    /// <summary>Files that were asked for and are not in the install.</summary>
    public IReadOnlyList<string> Missing => missing;
    readonly List<string> missing = [];

    /// <summary>Names of the tables that are in, in load order.</summary>
    public IReadOnlyList<string> Loaded => loaded;

    public int Count => entries.Count;

    /// <summary>Loads the four the engine loads at start-up, in its order.</summary>
    public Strings(GameFiles files)
    {
        this.files = files;
        foreach (var name in NlsFile.Startup) Add(name);
    }

    /// <summary>
    /// Adds one more table — a chronicle's own, or a mod's. It cannot overwrite what is already
    /// here, which is the engine's behaviour and not a simplification: see the file header.
    /// </summary>
    public bool Add(string name)
    {
        if (loaded.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        string path = NlsFile.PathFor(name);
        if (!files.TryRead(path, out var data)) { missing.Add(path); return false; }

        var table = NlsFile.Read(data, path);
        foreach (var (key, value) in table.Entries) entries.TryAdd(key, value);
        loaded.Add(name);
        return true;
    }

    /// <summary>
    /// Everything a chronicle's **metafile** declares, in the order it declares it. That is where
    /// the list actually lives — `Chronicles/<name>.nmf`, one `NLS` line per table — which settles
    /// a question that had been on the deferred list since the format was decoded.
    ///
    /// **And it is not one table per chronicle.** `vienna.nmf` names one `NLS`, but the shape is a list and a mod
    /// will use it; the four the engine loads at start-up are stacked before any of these.
    /// </summary>
    public int AddFrom(NmfFile? meta)
    {
        int added = 0;
        foreach (var name in meta?.Nls ?? [])
            if (Add(name)) added++;
        return added;
    }

    /// <summary>The text for a key, or null when nothing states it.</summary>
    public string? Find(string key) =>
        entries.TryGetValue(key.ToUpperInvariant(), out var value) ? value : null;

    /// <summary>
    /// What the panes bind to. An unknown key comes back **as itself** rather than as an empty
    /// box — which is how `UI_STATUS_HUMAN0` was visible on the character sheet at all, and is
    /// worth keeping: a missing string should look missing.
    /// </summary>
    public string this[string key] => Find(key) ?? key;

    /// <summary>The bare identity, for a pane built without an install behind it.</summary>
    public static readonly Func<string, string> Untranslated = key => key;

    /// <summary>
    /// `FormatNLS`: the text of <paramref name="id"/> with its `%X` slots filled from
    /// <paramref name="format"/>, which is a run of `%` + key letter + value — the discipline
    /// messages pass `"%C" + casterName + "%d" + disciplineName` into `RPG_DISCMSG_CASTS`'s
    /// "%C uses Discipline %d."
    /// <list type="bullet">
    /// <item>a key that is an **upper-case** letter puts its value in as it is (`isupper`);
    /// any other key's value is itself looked up as a string id — so `%d` turns the discipline's
    /// name into its localised one — and a value that is no id goes in as itself;</item>
    /// <item>a slot whose key the format does not give comes out as nothing;</item>
    /// <item>an id that is in no table is its own text, as everywhere else.</item>
    /// </list>
    /// With no format at all (null) it is the plain lookup, blanks and all (the lookup's half); an
    /// empty one still drops every blank.
    /// </summary>
    public static string Format(Func<string, string?> find, string id, string? format)
    {
        string text = find(id) ?? id;
        if (format is null) return text;
        // The format's pairs: `%` and a key letter, then the value up to the next `%`.
        var pairs = new List<(char Key, string Value)>();
        for (int i = 0; i + 1 < format.Length;)
        {
            char key = format[i + 1];
            int start = i + 2, end = start;
            while (end < format.Length && format[end] != '%') end++;
            pairs.Add((key, start <= format.Length ? format[Math.Min(start, format.Length)..end] : ""));
            i = end;
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length;)
        {
            if (text[i] != '%') { sb.Append(text[i++]); continue; }
            if (i + 1 >= text.Length) break;
            char key = text[i + 1];
            i += 2;
            foreach (var (k, v) in pairs)
            {
                if (k != key) continue;
                sb.Append(char.IsAsciiLetterUpper(key) ? v : find(v) ?? v);
                break;
            }
        }
        return sb.ToString();
    }
}
