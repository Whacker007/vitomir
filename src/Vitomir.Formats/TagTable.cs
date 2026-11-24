// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A tag's value as text and back (ours): how the recooked files (RecookText.cs) write the 32 bytes of a tag.
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>How a tag's 32 bytes are read: the engine's datatypes, folded to what the bytes hold.</summary>
public enum TagKind
{
    /// <summary>A name or a file name: NUL-terminated, at most 31 characters.</summary>
    Text,
    Float,
    Int,
    UInt,
    /// <summary>Three floats (a position, an angle triple, a colour).</summary>
    Vector,
    /// <summary>A whole number, one of a list's named values.</summary>
    Enum,
    /// <summary>A whole number of bits, each named.</summary>
    Flags,
}

/// <summary>One tag: its number, its name, how its value reads, and the list that names an enum's or flags' values.</summary>
public sealed record TagDef(uint Id, string Name, TagKind Kind, string? List);

/// <summary>
/// **The engine's tags by name** (ours, for the recooked files): each tag's number, name and type, and the names of the values of the
/// enums and flags among them. A tag's value is written by its type — a number, three numbers, a name, a value's name, flags' names
/// separated by commas — and read back into the same bytes the engine reads. The bytes a tag's type does not read (what the original
/// editor left after a name's end or a number's four bytes) are not kept: nothing reads them.
/// </summary>
public static partial class TagTable
{
    // Made on first use: the table itself (TagTable.Data.cs) is another part of the class, whose fields may be set after these.
    static Dictionary<uint, TagDef>? idIndex;
    static Dictionary<string, TagDef>? nameIndex;
    static Dictionary<uint, TagDef> byId => idIndex ??= defs.ToDictionary(d => d.Id);
    static Dictionary<string, TagDef> byName => nameIndex ??= defs.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every tag, by number.</summary>
    public static IReadOnlyCollection<TagDef> All => defs;

    public static TagDef? Of(uint id) => byId.GetValueOrDefault(id);
    public static TagDef? Of(string name) => byName.GetValueOrDefault(name.Trim());

    /// <summary>A tag's name, or <c>TAG&lt;n&gt;</c> for one the table does not know (its value then written as its 32 bytes in hex).</summary>
    public static string NameOf(uint id) => byId.TryGetValue(id, out var d) ? d.Name : $"TAG{id}";

    /// <summary>A key's tag number: a known name, or <c>TAG&lt;n&gt;</c>. Null for neither.</summary>
    public static uint? IdOf(string key)
    {
        if (byName.TryGetValue(key.Trim(), out var d)) return d.Id;
        key = key.Trim();
        if (key.StartsWith("TAG", StringComparison.OrdinalIgnoreCase) && uint.TryParse(key.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out uint n)) return n;
        return null;
    }

    /// <summary>A value of a list that has a name of its own: not blank, not a placeholder in brackets, and no other value of the list named alike.</summary>
    static string? NameIn(string? list, int value)
    {
        if (list is null || !lists.TryGetValue(list, out var items)) return null;
        string? found = null;
        foreach (var (v, n) in items) if (v == value) found = n;
        if (found is null || found.StartsWith('(')) return null;
        int same = 0;
        foreach (var (_, n) in items) if (string.Equals(n, found, StringComparison.OrdinalIgnoreCase)) same++;
        return same == 1 ? found : null;
    }

    static int? ValueIn(string? list, string name)
    {
        if (list is null || !lists.TryGetValue(list, out var items)) return null;
        int? found = null;
        foreach (var (v, n) in items)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null) return null;
                found = v;
            }
        return found is int f && NameIn(list, f) is not null ? f : null;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A float as the shortest text that reads back to the same bits.</summary>
    public static string Number(float f) => f.ToString("R", Inv);

    public static string Numbers(Vector3 v) => $"{Number(v.X)}, {Number(v.Y)}, {Number(v.Z)}";

    /// <summary>A tag's value as text, by its type.</summary>
    public static string Write(NodTag tag)
    {
        if (!byId.TryGetValue(tag.Id, out var d)) return "hex " + Convert.ToHexString(tag.Raw);
        switch (d.Kind)
        {
            case TagKind.Text:
                string s = tag.AsString();
                return s.Length == 0 || s != s.Trim() || s.StartsWith('"') ? $"\"{s}\"" : s;
            case TagKind.Float: return Number(tag.AsFloat());
            case TagKind.Int: return tag.AsInt().ToString(Inv);
            case TagKind.UInt: return tag.AsUInt().ToString(Inv);
            case TagKind.Vector: return Numbers(tag.AsVector3());
            case TagKind.Enum: return NameIn(d.List, tag.AsInt()) ?? tag.AsInt().ToString(Inv);
            case TagKind.Flags:
            {
                uint bits = tag.AsUInt();
                if (bits == 0) return "none";
                var names = new List<string>();
                for (int b = 0; b < 32; b++)
                    if ((bits & (1u << b)) != 0) names.Add(NameIn(d.List, b) is { } n && !n.Contains(',') ? n : $"bit {b}");
                return string.Join(", ", names);
            }
        }
        return "hex " + Convert.ToHexString(tag.Raw);
    }

    /// <summary>A tag's value read back from text into the 32 bytes the engine reads; null, with <paramref name="why"/>, when it does not read.</summary>
    public static NodTag? Read(uint id, string text, out string? why)
    {
        why = null;
        text = text.Trim();
        var raw = new byte[32];
        if (text.StartsWith("hex ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = Convert.FromHexString(text[4..].Trim());
                if (bytes.Length > 32) { why = "more than 32 bytes"; return null; }
                bytes.CopyTo(raw, 0);
                return new NodTag(id, raw);
            }
            catch (FormatException) { why = "not hexadecimal"; return null; }
        }
        if (!byId.TryGetValue(id, out var d)) { why = "an unknown tag's value must be written as hex"; return null; }
        switch (d.Kind)
        {
            case TagKind.Text:
            {
                string s = text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
                var bytes = System.Text.Encoding.Latin1.GetBytes(s);
                if (bytes.Length > 31) { why = $"longer than 31 characters ({bytes.Length})"; return null; }
                bytes.CopyTo(raw, 0);
                return new NodTag(id, raw);
            }
            case TagKind.Float:
                if (!float.TryParse(text, NumberStyles.Float, Inv, out float f)) { why = "not a number"; return null; }
                BinaryPrimitives.WriteSingleLittleEndian(raw, f);
                return new NodTag(id, raw);
            case TagKind.Int:
                if (!int.TryParse(text, NumberStyles.Integer, Inv, out int i)) { why = "not a whole number"; return null; }
                BinaryPrimitives.WriteInt32LittleEndian(raw, i);
                return new NodTag(id, raw);
            case TagKind.UInt:
                if (!uint.TryParse(text, NumberStyles.None, Inv, out uint u)) { why = "not a whole number of 0 or more"; return null; }
                BinaryPrimitives.WriteUInt32LittleEndian(raw, u);
                return new NodTag(id, raw);
            case TagKind.Vector:
            {
                if (ReadVector(text) is not { } v) { why = "not three numbers"; return null; }
                BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(0), v.X);
                BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(4), v.Y);
                BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(8), v.Z);
                return new NodTag(id, raw);
            }
            case TagKind.Enum:
            {
                int value;
                if (int.TryParse(text, NumberStyles.Integer, Inv, out int n)) value = n;
                else if (ValueIn(d.List, text) is int named) value = named;
                else { why = $"not one of {d.List}'s values"; return null; }
                BinaryPrimitives.WriteInt32LittleEndian(raw, value);
                return new NodTag(id, raw);
            }
            case TagKind.Flags:
            {
                uint bits = 0;
                if (!text.Equals("none", StringComparison.OrdinalIgnoreCase))
                    foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.StartsWith("bit ", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(part.AsSpan(4), NumberStyles.None, Inv, out int b) && b < 32) bits |= 1u << b;
                        else if (ValueIn(d.List, part) is int bit && bit is >= 0 and < 32) bits |= 1u << bit;
                        else { why = $"\"{part}\" is not one of {d.List}'s flags"; return null; }
                    }
                BinaryPrimitives.WriteUInt32LittleEndian(raw, bits);
                return new NodTag(id, raw);
            }
        }
        why = "no type";
        return null;
    }

    /// <summary>Three numbers separated by commas, or null.</summary>
    public static Vector3? ReadVector(string text)
    {
        var p = text.Split(',', StringSplitOptions.TrimEntries);
        if (p.Length != 3) return null;
        if (!float.TryParse(p[0], NumberStyles.Float, Inv, out float x) || !float.TryParse(p[1], NumberStyles.Float, Inv, out float y)
            || !float.TryParse(p[2], NumberStyles.Float, Inv, out float z)) return null;
        return new Vector3(x, y, z);
    }

    /// <summary>A list of numbers separated by commas (none for an empty text), or null.</summary>
    public static float[]? ReadFloats(string text)
    {
        if (text.Trim().Length == 0) return [];
        var p = text.Split(',', StringSplitOptions.TrimEntries);
        var r = new float[p.Length];
        for (int i = 0; i < p.Length; i++) if (!float.TryParse(p[i], NumberStyles.Float, Inv, out r[i])) return null;
        return r;
    }

    /// <summary>A list of whole numbers of 0 or more separated by commas (none for an empty text), or null.</summary>
    public static uint[]? ReadUInts(string text)
    {
        if (text.Trim().Length == 0) return [];
        var p = text.Split(',', StringSplitOptions.TrimEntries);
        var r = new uint[p.Length];
        for (int i = 0; i < p.Length; i++) if (!uint.TryParse(p[i], NumberStyles.None, Inv, out r[i])) return null;
        return r;
    }

    /// <summary>The bytes of a tag the engine reads, by its type: the rest zero (what <see cref="Read"/> makes of its text).</summary>
    public static byte[] Meaningful(NodTag tag)
    {
        var raw = new byte[32];
        int keep = byId.TryGetValue(tag.Id, out var d)
            ? d.Kind switch
            {
                TagKind.Vector => 12,
                TagKind.Text => Array.IndexOf(tag.Raw, (byte)0) is var nul and >= 0 ? nul : 31,
                _ => 4,
            }
            : 32;
        tag.Raw.AsSpan(0, Math.Min(keep, 32)).CopyTo(raw);
        return raw;
    }
}
