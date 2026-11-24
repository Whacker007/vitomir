// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.RegularExpressions;

namespace Vitomir.Formats;

/// <summary>One entry of the SDK's tag dictionary (<c>NodSDK/noteditortags.txt</c>).</summary>
public sealed record NotTagDef(uint Id, string Name, string DataType, string Ref, string Description);

/// <summary>A named template: a tag list, inheriting from its PARENT (tag 0).</summary>
public sealed record NotTemplate(string Name, int Offset, NodTag[] Tags, string Comment)
{
    public const uint TagParent = 0;

    public NodTag? Tag(uint id)
    {
        foreach (var t in Tags) if (t.Id == id) return t;
        return null;
    }

    public string Parent => Tag(TagParent)?.AsString() ?? "";
}

/// <summary>
/// NOT template file (Global.not, darkAges.not, modernDay.not, Multiplayer.not). Undocumented by the
/// SDK. 'NOT\x10', u32 version 1, u32 count, a directory of
/// (char[32] name, u32 offset), then per template: u32 numTags, tags (36 B), u32 commentLen, comment.
/// Directory offsets are authoritative (Multiplayer.not carries 6.8 KB of stale editor data).
/// </summary>
public sealed class NotFile
{
    public const uint Version1 = 1;

    public uint Version { get; init; }
    public NotTemplate[] Templates { get; init; } = [];
    public IReadOnlyDictionary<string, NotTemplate> ByName { get; init; } = new Dictionary<string, NotTemplate>();
    /// <summary>Bytes not covered by any record (editor leftovers).</summary>
    public int SlackBytes { get; init; }

    public static NotFile Read(byte[] data, string name = "<not>")
    {
        var r = new BinReader(data, name);
        if (data.Length < 4 || data[0] != (byte)'N' || data[1] != (byte)'O' || data[2] != (byte)'T' || data[3] != 0x10)
            throw r.Error("bad magic");
        r.Skip(4);
        uint version = r.U32();
        if (version != Version1) throw r.Error($"unexpected NOT version {version}");
        int n = r.Count32(100_000);
        var dir = new (string Name, int Offset)[n];
        for (int i = 0; i < n; i++) dir[i] = (r.CStr(32), (int)r.U32());
        var templates = new NotTemplate[n];
        int slack = 0;
        for (int i = 0; i < n; i++)
        {
            var (tname, off) = dir[i];
            if (off < r.Pos) throw r.Error($"template '{tname}' offset 0x{off:x} overlaps the previous record");
            slack += off - r.Pos;
            r.Pos = off;
            int cnt = r.Count32(4096);
            var tags = new NodTag[cnt];
            for (int t = 0; t < cnt; t++) tags[t] = NodTag.Read(r);
            int clen = r.Count32(1 << 20);
            string comment = BinReader.CString(r.Bytes(clen));
            int next = i + 1 < n ? dir[i + 1].Offset : data.Length;
            if (r.Pos > next) throw r.Error($"template '{tname}' overruns the next record (0x{next:x})");
            templates[i] = new NotTemplate(tname, off, tags, comment);
        }
        slack += data.Length - r.Pos;
        var byName = new Dictionary<string, NotTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in templates) byName.TryAdd(t.Name, t);
        return new NotFile { Version = version, Templates = templates, ByName = byName, SlackBytes = slack };
    }

    /// <summary>
    /// Flattens inheritance for <paramref name="template"/>: child tags override the PARENT chain.
    /// Parents are looked up in this file, then in <paramref name="globals"/> (Global.not).
    /// Returns tag id → tag; empty if the template is unknown.
    /// </summary>
    public Dictionary<uint, NodTag> Resolve(string template, NotFile? globals = null)
    {
        var chain = new List<NotTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string cur = template;
        while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
        {
            if (!ByName.TryGetValue(cur, out var t) && (globals is null || !globals.ByName.TryGetValue(cur, out t))) break;
            chain.Add(t);
            cur = t.Parent;
        }
        var result = new Dictionary<uint, NodTag>();
        for (int i = chain.Count - 1; i >= 0; i--)
            foreach (var tag in chain[i].Tags) result[tag.Id] = tag;
        return result;
    }

    /// <summary>Parses the SDK's <c>noteditortags.txt</c> (id → name and datatype). The file is the player's.</summary>
    public static Dictionary<uint, NotTagDef> LoadTagDictionary(string noteditortagsPath)
    {
        var text = File.ReadAllText(noteditortagsPath, Encoding.Latin1).Replace("\r\n", "\n");
        var result = new Dictionary<uint, NotTagDef>();
        foreach (Match m in Regex.Matches(text, @"\[(\d+)\]\s*\n(.*?)(?=\n\[|\z)", RegexOptions.Singleline))
        {
            uint id = uint.Parse(m.Groups[1].Value);
            var kv = new Dictionary<string, string>();
            foreach (Match l in Regex.Matches(m.Groups[2].Value, @"^(\w+)=(.*)$", RegexOptions.Multiline))
                kv[l.Groups[1].Value] = l.Groups[2].Value.TrimEnd();
            result[id] = new NotTagDef(id, kv.GetValueOrDefault("NAME", $"TAG{id}"), kv.GetValueOrDefault("DATATYPE", "?"),
                                       kv.GetValueOrDefault("REF", ""), kv.GetValueOrDefault("DESC", ""));
        }
        return result;
    }
}
