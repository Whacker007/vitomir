// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// NVO — the words. An `.nco` conversation holds only *identifiers*; the
// dialogue itself is here, in three files under `strings/`: `game.nvo` for the campaign and
// `mp_ctd.nvo`/`mp_lot.nvo` for the two multiplayer chronicles.
//
// The format is a table the game's own "Voice DB Exporter" wrote: comment lines opening `//`, blank
// lines, then one row per line of dialogue — **the identifier padded to 32 characters, the same
// identifier again padded to 32, and the text to the end of the line**. Why the identifier appears
// twice is not read; nothing in the shipped files makes the two columns differ.
using System.Text;

namespace Vitomir.Formats;

/// <summary>The dialogue, keyed by the identifiers an `.nco` uses.</summary>
public sealed class NvoStrings
{
    readonly Dictionary<string, string> lines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How wide each identifier column is.</summary>
    public const int KeyWidth = 32;

    /// <summary>How many lines this holds.</summary>
    public int Count => lines.Count;

    /// <summary>Every identifier, in no particular order.</summary>
    public IEnumerable<string> Keys => lines.Keys;

    /// <summary>
    /// The words for an identifier, or null. **About one line in 150 resolves to nothing** — eleven
    /// of the 1,760 the shipped conversations use — so a caller has to cope rather than assume.
    /// </summary>
    public string? this[string id] => lines.TryGetValue(id, out var text) ? text : null;

    /// <summary>Rows whose second column differs from the first. Empty across all three shipped files.</summary>
    public List<string> Mismatched { get; } = [];

    /// <summary>Lines that were neither a comment, nor blank, nor a row this could read.</summary>
    public List<string> Dropped { get; } = [];

    /// <summary>Reads one file into this table; later files win, as an overlay would.</summary>
    public NvoStrings Add(ReadOnlySpan<byte> data)
    {
        foreach (var raw in Encoding.Latin1.GetString(data).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.Length <= KeyWidth * 2) { Dropped.Add(line); continue; }
            string id = line[..KeyWidth].TrimEnd();
            string again = line[KeyWidth..(KeyWidth * 2)].TrimEnd();
            string text = line[(KeyWidth * 2)..].Trim();
            if (id.Length == 0) { Dropped.Add(line); continue; }
            if (!string.Equals(id, again, StringComparison.OrdinalIgnoreCase)) Mismatched.Add(id);
            lines[id] = text;
        }
        return this;
    }

    /// <summary>The three shipped files, in the order the engine would layer them.</summary>
    public static readonly string[] Files = ["strings/game.nvo", "strings/mp_ctd.nvo", "strings/mp_lot.nvo"];

    public static NvoStrings Read(ReadOnlySpan<byte> data) => new NvoStrings().Add(data);
}
