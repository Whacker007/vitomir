// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A template file's recooked text (ours), and a template file written back as the engine reads it.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// **A template as text** (ours): one file a template, its own values only, one <c>KEY = value</c> line a tag in the template's order,
/// <c>PARENT</c> first where it has one. What it does not set, it inherits, as the original file's template does. The original editor's
/// comment on the template is written above it as comment lines, and read back as nothing (the engine never reads it).
/// <para>The templates of one original file (<c>misc/Templates/darkAges.not</c>) are a folder of the same name
/// (<c>misc/Templates/darkAges/</c>), a file a template (<see cref="FileName"/>), and their order is kept beside it
/// (<c>misc/Templates/darkAges.order</c>, a name a line): a template no order names goes after them, by name.</para>
/// </summary>
public static class NotText
{
    /// <summary>A template's text file's extension.</summary>
    public const string Extension = ".txt";

    /// <summary>The order file's extension, beside the folder.</summary>
    public const string OrderExtension = ".order";

    /// <summary>A template's text.</summary>
    public static string Write(NotTemplate template, string? from = null)
    {
        var w = new RecookText.Writer();
        w.Comment(from is null ? template.Name : $"{template.Name}, recooked from {from}");
        if (template.Comment.Trim().Length > 0) w.Comment(template.Comment.Trim());
        foreach (var tag in template.Tags) w.Key(TagTable.NameOf(tag.Id), TagTable.Write(tag));
        return w.ToString();
    }

    /// <summary>A template read back from its text; <paramref name="name"/> is the template's name (<see cref="NameOf"/> of its file).</summary>
    public static NotTemplate Read(RecookText text, string name)
    {
        if (text.Sections.Count > 1) throw text.Error(text.Sections[1].Line, "a template has no sections");
        var tags = new List<NodTag>();
        var seen = new HashSet<uint>();
        foreach (var e in text.Top.Entries)
        {
            if (TagTable.IdOf(e.Key) is not uint id) throw text.Error(e.Line, $"{e.Key} is no tag the engine has");
            if (!seen.Add(id)) throw text.Error(e.Line, $"{e.Key} is set twice");
            if (TagTable.Read(id, e.Value, out var why) is not { } tag) throw text.Error(e.Line, $"{e.Key} = {e.Value}: {why}");
            tags.Add(tag);
        }
        return new NotTemplate(name, 0, [.. tags], "");
    }

    /// <summary>
    /// A template's file name: its name, with a character a file name cannot hold written as <c>%XX</c> (and <c>%</c> itself), so
    /// <c>_Bottles/Flasks</c> is <c>_Bottles%2FFlasks.txt</c>.
    /// </summary>
    public static string FileName(string template)
    {
        var sb = new StringBuilder();
        foreach (char c in template)
            if (c is '%' or '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || c < 32 || c > 126) sb.Append($"%{(int)c & 0xFF:X2}");
            else sb.Append(c);
        string name = sb.ToString();
        if (name.EndsWith('.') || name.EndsWith(' ')) name = name[..^1] + $"%{(int)name[^1]:X2}";
        return name + Extension;
    }

    /// <summary>The template a file name holds (<see cref="FileName"/> undone).</summary>
    public static string NameOf(string fileName)
    {
        string stem = fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^Extension.Length] : fileName;
        var sb = new StringBuilder();
        for (int i = 0; i < stem.Length; i++)
            if (stem[i] == '%' && i + 2 < stem.Length && IsHex(stem[i + 1]) && IsHex(stem[i + 2]))
            {
                sb.Append((char)Convert.ToByte(stem.Substring(i + 1, 2), 16));
                i += 2;
            }
            else sb.Append(stem[i]);
        return sb.ToString();
        static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    /// <summary>The order file's text: a template's name a line.</summary>
    public static string WriteOrder(IEnumerable<string> names) => string.Concat(names.Select(n => n + "\r\n"));

    public static List<string> ReadOrder(byte[] data) =>
        [.. Encoding.Latin1.GetString(data).Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0 && l[0] is not (';' or '#'))];
}

public static class NotFileWrite
{
    /// <summary>
    /// **A template file as the engine reads it** (<c>NOT\x10</c>, version 1): the directory, then each template's tags and an empty
    /// comment. What <see cref="NotFile.Read"/> reads back is the same templates, tag for tag.
    /// </summary>
    public static byte[] Write(IReadOnlyList<NotTemplate> templates)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("NOT\x10"u8);
        w.Write(NotFile.Version1);
        w.Write(templates.Count);
        int at = 12 + templates.Count * 36;
        foreach (var t in templates)
        {
            w.Write(Fixed(t.Name, 32));
            w.Write(at);
            at += 4 + t.Tags.Length * NodTag.Size + 4;
        }
        foreach (var t in templates)
        {
            w.Write(t.Tags.Length);
            foreach (var tag in t.Tags) { w.Write(tag.Id); w.Write(tag.Raw); }
            w.Write(0);
        }
        return ms.ToArray();
    }

    /// <summary>A name in a fixed field, NUL-terminated and clipped to fit.</summary>
    public static byte[] Fixed(string text, int size)
    {
        var b = new byte[size];
        var bytes = Encoding.Latin1.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, size - 1)).CopyTo(b);
        return b;
    }
}
