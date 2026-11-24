// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// Reads the fields of one data file and writes down, by file and field, everything wrong with it instead of
/// stopping at the first: the module refuses with the whole list (fail closed, every reason named).
/// </summary>
internal sealed class JsonChecks(string file, List<string> problems)
{
    public string File { get; } = file;

    public void Fail(string message) => problems.Add($"{File}: {message}");

    public bool Has(JsonElement o, string key) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out _);

    public JsonElement? Field(JsonElement o, string key, JsonValueKind kind, bool required = true)
    {
        if (o.ValueKind != JsonValueKind.Object) { Fail($"expected an object holding '{key}'"); return null; }
        if (!o.TryGetProperty(key, out var v))
        {
            if (required) Fail($"'{key}' is missing");
            return null;
        }
        if (v.ValueKind != kind) { Fail($"'{key}' must be {Describe(kind)}, not {Describe(v.ValueKind)}"); return null; }
        return v;
    }

    public string? Str(JsonElement o, string key, bool required = true) =>
        Field(o, key, JsonValueKind.String, required)?.GetString();

    public bool? Bool(JsonElement o, string key, bool required = true)
    {
        if (o.ValueKind != JsonValueKind.Object) { Fail($"expected an object holding '{key}'"); return null; }
        if (!o.TryGetProperty(key, out var v)) { if (required) Fail($"'{key}' is missing"); return null; }
        if (v.ValueKind is JsonValueKind.True) return true;
        if (v.ValueKind is JsonValueKind.False) return false;
        Fail($"'{key}' must be true or false, not {Describe(v.ValueKind)}");
        return null;
    }

    public int? Int(JsonElement o, string key, bool required = true)
    {
        var v = Field(o, key, JsonValueKind.Number, required);
        if (v is null) return null;
        if (!v.Value.TryGetInt32(out int i)) { Fail($"'{key}' must be a whole number, not {v.Value.GetRawText()}"); return null; }
        return i;
    }

    public double? Num(JsonElement v, string what)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
        {
            Fail($"{what} must be a finite number");
            return null;
        }
        return d;
    }

    public JsonElement? Arr(JsonElement o, string key, bool required = true) => Field(o, key, JsonValueKind.Array, required);

    public JsonElement? Obj(JsonElement o, string key, bool required = true) => Field(o, key, JsonValueKind.Object, required);

    /// <summary>Checks the file's <c>schema</c> field names the schema this reader understands.</summary>
    public bool Schema(JsonElement root, string expected)
    {
        var s = Str(root, "schema");
        if (s is null) return false;
        if (s != expected) { Fail($"schema is '{s}', this module reads '{expected}'"); return false; }
        return true;
    }

    static string Describe(JsonValueKind k) => k switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        JsonValueKind.Null => "null",
        _ => k.ToString(),
    };
}
