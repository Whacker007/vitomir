// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Modules;

/// <summary>What a data override does at its pointer: replace the value there (the default), remove it, or add one.</summary>
public enum DataOverrideOp
{
    Replace,
    Remove,
    Add,
}

/// <summary>
/// One module data value changed for one load of the modules (a scenario's <c>overrides.data</c>): the asset
/// (<c>enhanced:combat/weapons</c>), an RFC 6901 pointer into it (<c>/activeWindow/strikeStarts/0/metres</c>), the new value as JSON
/// text (null for <see cref="DataOverrideOp.Remove"/>) and the operation. The loader applies every one to its asset's bytes
/// <b>before</b> any module reads them (<see cref="ModuleLoadOptions.DataOverrides"/>), so the module's own reader checks the result;
/// nothing is written to the files, and another load without them reads the files as they are.
/// </summary>
public sealed record DataOverride(AssetId Asset, string Pointer, string? Value, DataOverrideOp Op = DataOverrideOp.Replace);

/// <summary>
/// A data override as the loader applied it: the asset, the pointer, the operation, the value before and
/// after (JSON text; null where there was none or is none), and the asset's SHA-256 before and after (of its bytes as read).
/// </summary>
public sealed record AppliedDataOverride(AssetId Asset, string Pointer, string Op, string? Before, string? After, string Sha256Before, string Sha256After)
{
    public override string ToString() =>
        $"data override: {Asset} {Pointer}: {Op.ToLowerInvariant()} {Before ?? "(none)"} -> {After ?? "(none)"} (the asset's sha256 {Sha256Before[..12]} -> {Sha256After[..12]})";
}

/// <summary>A data override that cannot be applied: its asset is no JSON object or array, its pointer is malformed or names nothing.</summary>
public sealed class DataOverrideException(string message) : Exception(message);

/// <summary>
/// RFC 6901 JSON pointers over <see cref="JsonNode"/> trees (ours): parse, find, and the operations of
/// <see cref="DataOverrideOp"/>; every leaf of a tree with its pointer (for <c>--describe data</c>).
/// </summary>
public static class JsonPointer
{
    /// <summary>The pointer's reference tokens (<c>~1</c> is <c>/</c>, <c>~0</c> is <c>~</c>); false with the problem when it is not a pointer.</summary>
    public static bool TryParse(string pointer, out string[] tokens, out string problem)
    {
        tokens = [];
        problem = "";
        if (pointer.Length == 0) return true;   // the whole document
        if (pointer[0] != '/') { problem = $"\"{pointer}\" is not a JSON pointer: it starts with \"/\" (RFC 6901: /activeWindow/shareOfPeak)"; return false; }
        var parts = pointer[1..].Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            for (int k = 0; k < p.Length; k++)
                if (p[k] == '~' && (k + 1 >= p.Length || p[k + 1] is not ('0' or '1')))
                {
                    problem = $"\"{pointer}\": \"~\" must be followed by 0 (\"~\") or 1 (\"/\") (RFC 6901)";
                    return false;
                }
            parts[i] = p.Replace("~1", "/").Replace("~0", "~");
        }
        tokens = parts;
        return true;
    }

    /// <summary>A token escaped for a pointer.</summary>
    public static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");

    /// <summary>The pointer of <paramref name="tokens"/>.</summary>
    public static string Of(IEnumerable<string> tokens) => string.Concat(tokens.Select(t => "/" + Escape(t)));

    /// <summary>The node at <paramref name="tokens"/> (a JSON null is found and null); false when the path names nothing.</summary>
    public static bool TryFind(JsonNode? root, IReadOnlyList<string> tokens, out JsonNode? found)
    {
        found = root;
        foreach (var t in tokens)
        {
            switch (found)
            {
                case JsonObject o when o.TryGetPropertyValue(t, out var child):
                    found = child;
                    break;
                case JsonArray a when Index(t, a.Count, false) is int i:
                    found = a[i];
                    break;
                default:
                    found = null;
                    return false;
            }
        }
        return true;
    }

    /// <summary>The deepest existing ancestor of <paramref name="tokens"/>: how many tokens of it exist.</summary>
    public static int Existing(JsonNode? root, IReadOnlyList<string> tokens)
    {
        for (int n = tokens.Count; n > 0; n--)
            if (TryFind(root, tokens.Take(n).ToList(), out _)) return n;
        return 0;
    }

    static int? Index(string token, int count, bool append)
    {
        if (append && token == "-") return count;
        if (token.Length == 0 || token.Length > 1 && token[0] == '0') return null;
        foreach (char c in token) if (c is < '0' or > '9') return null;
        if (!int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i)) return null;
        return i < count || append && i == count ? i : null;
    }

    /// <summary>
    /// Applies one operation at <paramref name="tokens"/> of <paramref name="root"/> (the root itself is never replaced):
    /// replace needs the target, remove needs it, add needs its parent (an object's new member, or an array's index or <c>-</c>).
    /// Returns the value before (JSON text, null when none). Throws <see cref="DataOverrideException"/>.
    /// </summary>
    public static string? Apply(JsonNode root, IReadOnlyList<string> tokens, DataOverrideOp op, JsonNode? value)
    {
        if (tokens.Count == 0) throw new DataOverrideException("the pointer \"\" is the whole asset: name a value inside it");
        if (!TryFind(root, tokens.Take(tokens.Count - 1).ToList(), out var parent) || parent is not (JsonObject or JsonArray))
            throw new DataOverrideException($"{Of(tokens.Take(tokens.Count - 1))} is not an object or a list in the asset");
        string last = tokens[^1];
        switch (parent)
        {
            case JsonObject o:
            {
                bool has = o.TryGetPropertyValue(last, out var was);
                if (op != DataOverrideOp.Add && !has) throw new DataOverrideException($"{Of(tokens)} names nothing (no \"{last}\" in {Of(tokens.Take(tokens.Count - 1))})");
                string? before = has ? Text(was) : null;
                if (op == DataOverrideOp.Remove) o.Remove(last);
                else o[last] = value?.DeepClone();
                return before;
            }
            case JsonArray a:
            {
                int? i = Index(last, a.Count, op == DataOverrideOp.Add);
                if (i is not int at) throw new DataOverrideException($"{Of(tokens)}: \"{last}\" is not an index of the list (0 to {a.Count - 1}{(op == DataOverrideOp.Add ? ", or \"-\" for the end" : "")})");
                if (op == DataOverrideOp.Add) { a.Insert(at, value?.DeepClone()); return null; }
                string? before = Text(a[at]);
                if (op == DataOverrideOp.Remove) a.RemoveAt(at);
                else a[at] = value?.DeepClone();
                return before;
            }
        }
        return null;
    }

    /// <summary>A node's JSON text (compact), <c>null</c> for a JSON null.</summary>
    public static string Text(JsonNode? n) => n is null ? "null" : n.ToJsonString();

    /// <summary>The JSON kind of a node (<c>object</c>, <c>array</c>, <c>string</c>, <c>number</c>, <c>boolean</c>, <c>null</c>).</summary>
    public static string KindOf(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null",
        },
        _ => "null",
    };

    /// <summary>Every leaf of a tree (a value that is not an object or a list, or an empty one) with its pointer, in document order.</summary>
    public static IEnumerable<(string Pointer, JsonNode? Value)> Leaves(JsonNode? root, string prefix = "")
    {
        switch (root)
        {
            case JsonObject o when o.Count > 0:
                foreach (var (k, v) in o)
                    foreach (var leaf in Leaves(v, prefix + "/" + Escape(k))) yield return leaf;
                break;
            case JsonArray a when a.Count > 0:
                for (int i = 0; i < a.Count; i++)
                    foreach (var leaf in Leaves(a[i], prefix + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))) yield return leaf;
                break;
            default:
                yield return (prefix, root);
                break;
        }
    }
}

/// <summary>Data overrides applied to an asset's bytes, and their text for a recording's head.</summary>
public static class DataOverrides
{
    static readonly JsonDocumentOptions Reading = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonNodeOptions Nodes = new() { PropertyNameCaseInsensitive = false };
    static readonly JsonSerializerOptions Writing = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The SHA-256 of bytes, lower-case hex.</summary>
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>An asset's text parsed as the module reads it (comments and trailing commas allowed; a byte-order mark skipped).</summary>
    public static JsonNode Parse(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        return JsonNode.Parse(text, Nodes, Reading) ?? throw new DataOverrideException("the asset is empty JSON");
    }

    /// <summary>
    /// The asset's bytes with <paramref name="overrides"/> applied in order (each to the result of the ones before), and what each
    /// did. Throws <see cref="DataOverrideException"/> naming the override that cannot be applied.
    /// </summary>
    public static byte[] Apply(AssetId asset, byte[] bytes, IReadOnlyList<DataOverride> overrides, List<AppliedDataOverride> applied)
    {
        JsonNode root;
        try { root = Parse(bytes); }
        catch (JsonException e) { throw new DataOverrideException($"{asset} is not JSON ({e.Message}): only a JSON asset's values can be overridden"); }
        if (root is not (JsonObject or JsonArray)) throw new DataOverrideException($"{asset} is not a JSON object or list");
        byte[] now = bytes;
        foreach (var o in overrides)
        {
            if (!JsonPointer.TryParse(o.Pointer, out var tokens, out string problem)) throw new DataOverrideException($"{asset}: {problem}");
            JsonNode? value;
            try { value = o.Value is null ? null : JsonNode.Parse(o.Value, Nodes, Reading); }
            catch (JsonException e) { throw new DataOverrideException($"{asset}{o.Pointer}: the value is not JSON ({e.Message})"); }
            string? before;
            try { before = JsonPointer.Apply(root, tokens, o.Op, value); }
            catch (DataOverrideException e) { throw new DataOverrideException($"{asset}: {e.Message}"); }
            string shaBefore = Sha256(now);
            now = Encoding.UTF8.GetBytes(root.ToJsonString(Writing));
            applied.Add(new AppliedDataOverride(asset, o.Pointer, o.Op.ToString(), before, o.Op == DataOverrideOp.Remove ? null : JsonPointer.Text(value), shaBefore, Sha256(now)));
        }
        return now;
    }

    /// <summary>The overrides as one line of JSON (a recording's head note; empty for none).</summary>
    public static string ToNote(IReadOnlyList<DataOverride> overrides)
    {
        if (overrides.Count == 0) return "";
        var a = new JsonArray();
        foreach (var o in overrides)
        {
            var x = new JsonObject { ["asset"] = o.Asset.ToString(), ["pointer"] = o.Pointer };
            if (o.Op != DataOverrideOp.Replace) x["op"] = o.Op.ToString().ToLowerInvariant();
            if (o.Value is not null) x["value"] = JsonNode.Parse(o.Value);
            a.Add(x);
        }
        return a.ToJsonString();
    }

    /// <summary>The overrides from <see cref="ToNote"/>'s text (none for null or empty); throws <see cref="DataOverrideException"/> when unreadable.</summary>
    public static IReadOnlyList<DataOverride> FromNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return [];
        try
        {
            var list = new List<DataOverride>();
            foreach (var x in JsonNode.Parse(note)!.AsArray())
            {
                var op = x!["op"]?.GetValue<string>() switch { "remove" => DataOverrideOp.Remove, "add" => DataOverrideOp.Add, _ => DataOverrideOp.Replace };
                list.Add(new DataOverride(AssetId.Parse(x["asset"]!.GetValue<string>()), x["pointer"]!.GetValue<string>(),
                    x["value"] is { } v ? v.ToJsonString() : op == DataOverrideOp.Remove ? null : "null", op));
            }
            return list;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new DataOverrideException($"the data overrides are not readable ({e.Message})");
        }
    }
}
