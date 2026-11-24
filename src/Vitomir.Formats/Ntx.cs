// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `.NTX` — the **documents**: every letter, journal, scroll and book the player can read.
//
// Found from the other end. A play-test clicked the `CuthbertLetter` in the quick-use strip
// and the engine answered *"a kind of item nothing uses yet"* — correctly, because it is
// `ITEM_TYPE 13`, **BOOK**, and reading one needs text nothing had gone looking for. The template
// says where: **`ITEM_TEXT` (tag 30)** names an entry, and **`DESCRIPTIONID` (tag 82)** names a
// `.nls` key for the title. Both resolve, and they agree — `CUTHBERTLETTER` in `game.nls` is
// *"Sir Cuthbert's Letter to Christof"*, which is also the entry's own `<TITLE>`.
//
// **Seven files ship**, in `Local_Eng.nob/strings/`, named exactly like the `.nls` tables they sit
// beside: `prague`, `prague2`, `vienna`, `london`, `new_york` and two multiplayer. **57 entries,
// 232 paragraphs**, and the grammar has no variation at all in the shipped data:
//
// ```
// <TEXT CuthbertLetter Cuthbert_2_2_14.mp3 0>
// <TITLE Sir Cuthbert's Letter to Christof>
// <P 0xFFFFFFFF>
// My Good Kinsman Christof--
// </TEXT>
// ```
//
// Every entry names a voice file; every flags field is `0`; every paragraph colour is
// `0xFFFFFFFF`. All three are read anyway rather than assumed away — a mod will use them, and the
// standing rule is that a measurement of the shipped data is never permission to skip the
// capability.
using System.Globalization;

namespace Vitomir.Formats;

/// <summary>One paragraph, with the colour its own `&lt;P&gt;` gives it.</summary>
/// <param name="Text">
/// The prose, its lines joined with `\n` — kept as hard line breaks, as 1.1 keeps them (the read
/// pane's text box breaks at each).
/// </param>
/// <param name="Colour">ARGB as written — `0xFFFFFFFF` in every shipped paragraph.</param>
public readonly record struct NtxParagraph(string Text, uint Colour);

/// <summary>One readable document.</summary>
public sealed class NtxEntry
{
    /// <summary>The id an item's `ITEM_TEXT` names.</summary>
    public string Id { get; init; } = "";
    /// <summary>What the pane's title box shows.</summary>
    public string Title { get; init; } = "";
    /// <summary>The voice track, or "0" for none. Every shipped entry names one.</summary>
    public string Voice { get; init; } = "";
    /// <summary>The `&lt;TEXT&gt;` tag's third field. `0` in every shipped entry.</summary>
    public int Flags { get; init; }
    public IReadOnlyList<NtxParagraph> Paragraphs { get; init; } = [];

    /// <summary>The whole document as one string, paragraphs separated by a blank line.</summary>
    public string Text => string.Join("\n\n", Paragraphs.Select(p => p.Text));
}

/// <summary>A `.ntx` file: the documents of one chronicle.</summary>
public sealed class NtxFile
{
    public string File { get; private set; } = "";

    public IReadOnlyList<NtxEntry> Entries => entries;
    readonly List<NtxEntry> entries = [];

    /// <summary>Anything the reader could not place. Empty on all seven shipped files.</summary>
    public IReadOnlyList<string> Warnings => warnings;
    readonly List<string> warnings = [];

    public const string Folder = "strings/";
    public const string Extension = ".ntx";

    /// <summary>The default paragraph colour, which is the only one the shipped files use.</summary>
    public const uint White = 0xFFFFFFFF;

    public static string PathFor(string name) =>
        Folder + (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                  ? name[..^Extension.Length] : name) + Extension;

    /// <summary>The entry with this id, case-insensitively, or null (the read pane's and the item templates' lookup).</summary>
    public NtxEntry? Find(string id) =>
        entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads a `.ntx` document file — letters, journals, books.</summary>
    public static NtxFile Read(byte[] data, string name) =>
        Parse(System.Text.Encoding.Latin1.GetString(data), name);

    public static NtxFile Parse(string text, string name = "")
    {
        var file = new NtxFile { File = name };
        string? id = null, title = null, voice = null;
        int flags = 0;
        List<NtxParagraph> paragraphs = [];
        uint colour = White;
        var prose = new System.Text.StringBuilder();

        void EndParagraph()
        {
            var said = prose.ToString().Trim();
            if (said.Length > 0) paragraphs.Add(new NtxParagraph(said, colour));
            prose.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith("<TEXT", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.TrimEnd('>')[5..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                id = parts.Length > 0 ? parts[0] : "";
                voice = parts.Length > 1 ? parts[1] : "";
                flags = parts.Length > 2 && int.TryParse(parts[2], out var f) ? f : 0;
                title = null; paragraphs = []; colour = White; prose.Clear();
            }
            else if (trimmed.StartsWith("<TITLE", StringComparison.OrdinalIgnoreCase))
                title = trimmed[6..].TrimEnd('>').Trim();
            else if (trimmed.StartsWith("<P", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith('>'))
            {
                EndParagraph();
                colour = Colour(trimmed[2..^1].Trim());
            }
            else if (trimmed.StartsWith("</TEXT", StringComparison.OrdinalIgnoreCase))
            {
                EndParagraph();
                if (id is null) { file.warnings.Add($"{name}: </TEXT> with no <TEXT>"); continue; }
                file.entries.Add(new NtxEntry
                {
                    Id = id, Title = title ?? "", Voice = voice ?? "", Flags = flags,
                    Paragraphs = paragraphs,
                });
                id = null;
            }
            else if (id is not null)
            {
                // Prose. The game appends each line to the paragraph as fgets read it from a file
                // opened `rb`, its `\r\n` and all, and the text box the read pane fills
                // splits an item at every `\n` before it wraps (strtok on "\n") —
                // so a paragraph's own line ends are hard breaks: the verse in prague.ntx and
                // mp_ctd.ntx is two lines, not one run on. A blank line is no break (the engine skips
                // a line that starts with white space, and strtok drops an empty piece). **Ours**:
                // the `\r` 1.1 leaves on the end of each piece is dropped.
                if (trimmed.Length > 0) prose.Append(prose.Length > 0 ? "\n" : "").Append(trimmed);
            }
            else if (trimmed.Length > 0 && !trimmed.StartsWith(';'))
                file.warnings.Add($"{name}: '{trimmed}' is outside any <TEXT> block");
        }
        if (id is not null) file.warnings.Add($"{name}: <TEXT {id}> was never closed");
        return file;
    }

    /// <summary>`0xAARRGGBB` as written, tolerating a bare decimal.</summary>
    static uint Colour(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return hex;
        return uint.TryParse(value, out var dec) ? dec : White;
    }
}
