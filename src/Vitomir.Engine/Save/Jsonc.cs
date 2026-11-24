// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// JSON with comments, both ways. Reading is the framework's own reader told to skip comments and
// allow trailing commas; writing is ours, because no serialiser writes a comment — so the save's
// explanations are generated here from the data, every time the file is written, and a comment a
// person adds by hand is gone the next time the game saves (the header's `notes`
// is the place for a person's own words).
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Engine.Save;

public static class Jsonc
{
    /// <summary>
    /// How every part is read and written: camelCase names, enums by name, nulls left out, comments
    /// and trailing commas accepted on the way in, and a three-number array for a vector.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Make();

    static JsonSerializerOptions Make()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new VectorConverter());
        o.Converters.Add(new ScriptValueConverter());
        return o;
    }

    /// <summary>
    /// `--save-strict-json`: every part written as plain JSON, without the
    /// comments, for anyone whose tools need it — the saves and the kept network characters alike.
    /// Reading is lenient either way. Off by default: the comments are the point of the format.
    /// </summary>
    public static bool Strict { get; set; }

    /// <summary>Reads a part leniently: comments skipped, trailing commas allowed.</summary>
    public static T Read<T>(string text) where T : new() =>
        JsonSerializer.Deserialize<T>(text, Options) ?? new T();

    /// <summary>
    /// Writes a part as indented JSON with line comments. <paramref name="banner"/> heads the file;
    /// <paramref name="comment"/> is asked about every property by its path (<c>things/UNOR_7_1#41/actor</c>)
    /// and may answer a line to go above it and a note to go beside it. <paramref name="strict"/> —
    /// <see cref="Strict"/> when not said — leaves both out, and what is left is strict JSON.
    /// </summary>
    public static string Write<T>(T value, string? banner = null, Func<string, JsonNode?, Note>? comment = null,
                                  bool? strict = null)
    {
        if (strict ?? Strict) { banner = null; comment = null; }
        var node = JsonSerializer.SerializeToNode(value, Options);
        var sb = new StringBuilder();
        if (banner is not null)
            foreach (var line in banner.Split('\n')) sb.Append("// ").Append(line.TrimEnd()).Append('\n');
        Emit(sb, node, 0, "", comment);
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>What a comment provider answers: a line above a property, and a note after its value.</summary>
    public readonly record struct Note(string? Above = null, string? Beside = null)
    {
        public static readonly Note None = default;
    }

    const int Indent = 2;
    /// <summary>An array of plain values this long or shorter goes on one line.</summary>
    const int InlineArray = 120;
    /// <summary>An object of this many plain values or fewer goes on one line, if it fits.</summary>
    const int InlineMembers = 4;

    static void Emit(StringBuilder sb, JsonNode? node, int depth, string path, Func<string, JsonNode?, Note>? comment)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                if (obj.Count == 0) { sb.Append("{}"); break; }
                // A small record of plain values — a stat's four numbers — goes on one line, unless
                // something in it wants a comment.
                if (obj.Count <= InlineMembers && obj.All(kv => kv.Value is JsonValue)
                    && obj.All(kv => comment?.Invoke(path + "/" + kv.Key, kv.Value) is not { } n || (n.Above is null && n.Beside is null)))
                {
                    string flatObj = "{ " + string.Join(", ", obj.Select(kv => JsonSerializer.Serialize(kv.Key, Options) + ": " + kv.Value!.ToJsonString(Options))) + " }";
                    if (flatObj.Length <= InlineArray) { sb.Append(flatObj); break; }
                }
                sb.Append("{\n");
                int i = 0;
                foreach (var (key, child) in obj)
                {
                    string childPath = path.Length == 0 ? key : path + "/" + key;
                    var note = comment?.Invoke(childPath, child) ?? Note.None;
                    if (note.Above is { Length: > 0 } above)
                        foreach (var line in above.Split('\n'))
                            sb.Append(' ', (depth + 1) * Indent).Append("// ").Append(line.TrimEnd()).Append('\n');
                    sb.Append(' ', (depth + 1) * Indent);
                    sb.Append(JsonSerializer.Serialize(key, Options)).Append(": ");
                    Emit(sb, child, depth + 1, childPath, comment);
                    if (++i < obj.Count) sb.Append(',');
                    if (note.Beside is { Length: > 0 } beside) sb.Append("  // ").Append(beside);
                    sb.Append('\n');
                }
                sb.Append(' ', depth * Indent).Append('}');
                break;
            case JsonArray arr:
                if (arr.Count == 0) { sb.Append("[]"); break; }
                if (arr.All(e => e is JsonValue))
                {
                    string flat = "[" + string.Join(", ", arr.Select(e => e!.ToJsonString(Options))) + "]";
                    if (flat.Length <= InlineArray) { sb.Append(flat); break; }
                }
                sb.Append("[\n");
                for (int j = 0; j < arr.Count; j++)
                {
                    string childPath = path + "/" + j;
                    var note = comment?.Invoke(childPath, arr[j]) ?? Note.None;
                    if (note.Above is { Length: > 0 } above)
                        foreach (var line in above.Split('\n'))
                            sb.Append(' ', (depth + 1) * Indent).Append("// ").Append(line.TrimEnd()).Append('\n');
                    sb.Append(' ', (depth + 1) * Indent);
                    Emit(sb, arr[j], depth + 1, childPath, comment);
                    if (j + 1 < arr.Count) sb.Append(',');
                    if (note.Beside is { Length: > 0 } beside) sb.Append("  // ").Append(beside);
                    sb.Append('\n');
                }
                sb.Append(' ', depth * Indent).Append(']');
                break;
            default:
                sb.Append(node.ToJsonString(Options));
                break;
        }
    }

    /// <summary>A vector as <c>[x, y, z]</c>, which is how a person would write one.</summary>
    sealed class VectorConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a vector is written [x, y, z]");
            var v = new float[3];
            int n = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (n < 3) v[n] = reader.GetSingle();
                n++;
            }
            if (n != 3) throw new JsonException($"a vector has three numbers, not {n}");
            return new Vector3(v[0], v[1], v[2]);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }

    /// <summary>A script value as <c>{"int": 3}</c> — its type is the name, so the file says what it is.</summary>
    sealed class ScriptValueConverter : JsonConverter<ScriptValue>
    {
        public override ScriptValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("a script value is written {\"int\": 3}");
            var v = new ScriptValue();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString() ?? "";
                reader.Read();
                switch (name)
                {
                    case "bool": v.Bool = reader.GetBoolean(); break;
                    case "int": v.Int = reader.GetInt32(); break;
                    case "float": v.Float = reader.GetSingle(); break;
                    case "string": v.String = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    default: reader.Skip(); break;
                }
            }
            return v;
        }

        public override void Write(Utf8JsonWriter writer, ScriptValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.Bool is { } b) writer.WriteBoolean("bool", b);
            else if (value.Int is { } i) writer.WriteNumber("int", i);
            else if (value.Float is { } f) writer.WriteNumber("float", f);
            else writer.WriteString("string", value.String ?? "");
            writer.WriteEndObject();
        }
    }
}
