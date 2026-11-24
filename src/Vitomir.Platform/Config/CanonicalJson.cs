// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vitomir.Platform.Config;

/// <summary>
/// A canonical form of a JSON document, for hashing a configuration (every
/// trace carries the configuration's hash). Two files that say the same thing hash the same, whatever
/// their layout: object keys are sorted ordinally (by UTF-16 code unit, as RFC 8785 sorts them), there is
/// no whitespace, strings are re-escaped one fixed way, and the output is UTF-8. Numbers are written as
/// their <b>original token text</b>, not re-formatted: <c>1.0</c> and <c>1</c> stay different, because a
/// rules value written <c>2.25</c> must hash as the rules package wrote it, and re-formatting a double could
/// change it on another runtime. A document with a duplicated key is refused (it has no one meaning).
/// Ours; close to RFC 8785 but for the numbers.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The canonical UTF-8 bytes of a JSON text. Throws <see cref="JsonException"/> on malformed JSON or a duplicated key.</summary>
    public static byte[] Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json, Options);
        return Canonicalize(doc.RootElement);
    }

    /// <summary>The canonical UTF-8 bytes of a JSON file's text.</summary>
    public static byte[] CanonicalizeFile(string path) => Canonicalize(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>The canonical UTF-8 bytes of a parsed value.</summary>
    public static byte[] Canonicalize(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        Write(value, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes a value's canonical form to a buffer (so several documents can be canonicalised into one).</summary>
    public static void Write(JsonElement value, IBufferWriter<byte> output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var props = new List<JsonProperty>();
                    foreach (var p in value.EnumerateObject()) props.Add(p);
                    props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    for (int i = 1; i < props.Count; i++)
                        if (props[i].Name == props[i - 1].Name)
                            throw new JsonException($"the key \"{props[i].Name}\" appears twice in one object");
                    Raw(output, "{");
                    for (int i = 0; i < props.Count; i++)
                    {
                        if (i > 0) Raw(output, ",");
                        WriteString(props[i].Name, output);
                        Raw(output, ":");
                        Write(props[i].Value, output);
                    }
                    Raw(output, "}");
                    break;
                }
            case JsonValueKind.Array:
                {
                    Raw(output, "[");
                    bool first = true;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (!first) Raw(output, ",");
                        first = false;
                        Write(item, output);
                    }
                    Raw(output, "]");
                    break;
                }
            case JsonValueKind.String: WriteString(value.GetString()!, output); break;
            case JsonValueKind.Number: Raw(output, value.GetRawText()); break;
            case JsonValueKind.True: Raw(output, "true"); break;
            case JsonValueKind.False: Raw(output, "false"); break;
            case JsonValueKind.Null: Raw(output, "null"); break;
            default: throw new JsonException($"no canonical form for a {value.ValueKind}");
        }
    }

    /// <summary>
    /// A string the one canonical way: the quote and the backslash escaped, the control characters below
    /// U+0020 as <c>\b \t \n \f \r</c> or <c>\u00xx</c> (lower-case hex), everything else as itself in UTF-8.
    /// </summary>
    static void WriteString(string s, IBufferWriter<byte> output)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\f': sb.Append("\\f"); break;
                case '\r': sb.Append("\\r"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        Raw(output, sb.ToString());
    }

    static void Raw(IBufferWriter<byte> output, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        output.Write(bytes);
    }

    static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
}
