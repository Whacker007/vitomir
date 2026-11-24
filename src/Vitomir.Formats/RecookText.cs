// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The recooked files' text (ours): `KEY = value` lines, `[section]` headers one deep, and whole-line comments.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// **The text of a recooked file** (ours): what Vitomir writes a template, a scene or a camera path as, for a person to read and change.
/// <list type="bullet">
/// <item>A line <c>KEY = value</c>. The key is matched without regard to case; the value is everything after the first <c>=</c>, trimmed.</item>
/// <item>A line <c>[name]</c> or <c>[name label]</c> starts a section; the lines before the first section are the file's own. One level only.</item>
/// <item>A line whose first character (after spaces) is <c>;</c> or <c>#</c> is a comment. A <c>;</c> anywhere else is part of the value.</item>
/// <item>Blank lines are nothing. A key may come more than once (a scene's parameters, a thing's keyframes): the order is kept.</item>
/// </list>
/// </summary>
public sealed class RecookText
{
    /// <summary>One <c>KEY = value</c> line, with its line number for a complaint.</summary>
    public readonly record struct Entry(string Key, string Value, int Line);

    /// <summary>A section: its name (empty for the lines before any header), its label (what follows the name in the header) and its lines.</summary>
    public sealed class Section(string name, string label, int line)
    {
        public string Name { get; } = name;
        public string Label { get; } = label;
        public int Line { get; } = line;
        public List<Entry> Entries { get; } = [];

        /// <summary>The last value of a key, or null.</summary>
        public string? this[string key]
        {
            get
            {
                string? found = null;
                foreach (var e in Entries) if (e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) found = e.Value;
                return found;
            }
        }

        /// <summary>Every value of a key, in order.</summary>
        public IEnumerable<Entry> All(string key) => Entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The file's name, for complaints.</summary>
    public string Name { get; }

    /// <summary>The sections in order, the file's own lines first (always there, maybe empty).</summary>
    public List<Section> Sections { get; } = [];

    /// <summary>The comment lines before the first key or section, without their <c>;</c>.</summary>
    public List<string> Heading { get; } = [];

    RecookText(string name) => Name = name;

    public Section Top => Sections[0];

    /// <summary>The sections of one name, in order.</summary>
    public IEnumerable<Section> Of(string name) => Sections.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a recooked file. A line that is neither a key, a header, a comment nor blank is refused with its line number.</summary>
    public static RecookText Parse(string text, string name)
    {
        var file = new RecookText(name);
        var current = new Section("", "", 0);
        file.Sections.Add(current);
        bool heading = true;
        int number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line[0] is ';' or '#')
            {
                if (heading) file.Heading.Add(line[1..].Trim());
                continue;
            }
            heading = false;
            if (line[0] == '[')
            {
                if (line[^1] != ']') throw file.Error(number, "a section header must end with ]");
                string inside = line[1..^1].Trim();
                int space = inside.IndexOf(' ');
                current = space < 0 ? new Section(inside, "", number) : new Section(inside[..space], inside[(space + 1)..].Trim(), number);
                if (current.Name.Length == 0) throw file.Error(number, "a section header needs a name");
                file.Sections.Add(current);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) throw file.Error(number, "expected KEY = value");
            current.Entries.Add(new Entry(line[..eq].Trim(), line[(eq + 1)..].Trim(), number));
        }
        return file;
    }

    public static RecookText Parse(byte[] data, string name) => Parse(Encoding.Latin1.GetString(data), name);

    public InvalidDataException Error(int line, string what) => new($"{Name}({line}): {what}");

    /// <summary>The text as the file's bytes: Latin-1, as the original formats hold their names.</summary>
    public static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>A file being written: header comments, keys and sections, with Windows line ends (the player opens them in Notepad).</summary>
    public sealed class Writer
    {
        readonly StringBuilder sb = new();
        bool any;

        public Writer Comment(string text)
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n')) sb.Append("; ").Append(line.TrimEnd()).Append("\r\n");
            return this;
        }

        public Writer Key(string key, string value)
        {
            sb.Append(key).Append(" = ").Append(value).Append("\r\n");
            any = true;
            return this;
        }

        public Writer Section(string name, string? label = null)
        {
            if (any || sb.Length > 0) sb.Append("\r\n");
            sb.Append('[').Append(name);
            if (label is { Length: > 0 }) sb.Append(' ').Append(label);
            sb.Append("]\r\n");
            any = true;
            return this;
        }

        public override string ToString() => sb.ToString();
    }
}
