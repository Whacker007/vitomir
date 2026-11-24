// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json.Nodes;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// <c>--describe data</c>: every JSON asset of the modules' <c>Data/</c>, one object each — its name for
/// <c>overrides.data</c>'s <c>asset</c> (<c>combat/weapons.json</c>), its id, schema and status, whether the modules read it, whether a
/// scenario may override it, how many leaves it has — and every leaf as a row <c>[pointer, type, value, status]</c>: the RFC 6901
/// pointer for <c>overrides.data</c>'s <c>pointer</c>, the JSON type a new value must have, the current value (a text over 100
/// characters cut), and the status that holds for it (the nearest <c>status</c> of an object it is in, else the asset's).
/// </summary>
public static partial class ScenarioDescribe
{
    /// <summary>The longest text value written whole (longer ones are cut, with their length said).</summary>
    const int LongestText = 100;

    static JsonArray DataList(ScenarioGame game)
    {
        var list = new JsonArray();
        foreach (var a in game.DataAssets)
        {
            var root = a.Root;
            var rows = new JsonArray();
            foreach (var (pointer, value) in JsonPointer.Leaves(root))
            {
                JsonPointer.TryParse(pointer, out var tokens, out _);
                rows.Add(new JsonArray(pointer, JsonPointer.KindOf(value), Shown(value), DataLeaves.StatusAt(root, tokens)));
            }
            string overridable = a.Id.Path == FoePresets.Asset ? "no: its presets are expanded when a scenario is read, before overrides apply"
                : !a.Read ? "yes, but this build's modules do not read it (an override changes nothing)" : "yes (but /schema: the module finds its files by it)";
            list.Add(new JsonObject
            {
                ["asset"] = a.File, ["id"] = a.Id.ToString(), ["schema"] = a.Schema, ["status"] = a.Status, ["read"] = a.Read,
                ["overridable"] = overridable, ["leaves"] = rows.Count, ["columns"] = new JsonArray("pointer", "type", "value", "status"), ["values"] = rows,
            });
        }
        return list;
    }

    static JsonNode? Shown(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue(out string? s) && s.Length > LongestText
            ? JsonValue.Create(string.Create(CultureInfo.InvariantCulture, $"{s[..LongestText]}… ({s.Length} characters)"))
            : value?.DeepClone();

    /// <summary><c>--describe data</c> in words: an asset a heading, a value a line.</summary>
    static IEnumerable<string> DataWords(JsonArray assets)
    {
        int total = 0;
        foreach (var a in assets)
        {
            total += a!["leaves"]!.GetValue<int>();
            yield return $"  -- {a["asset"]} ({a["id"]}; {a["schema"]}; {a["status"]}; read: {a["read"]}; overridable: {a["overridable"]}; {a["leaves"]} values)";
            foreach (var row in a["values"]!.AsArray())
                yield return $"    {row![0]} = {row[2]?.ToJsonString() ?? "null"} ({row[1]}{(row[3]!.GetValue<string>().Length > 0 ? ", " + row[3] : "")})";
        }
        yield return string.Create(CultureInfo.InvariantCulture, $"  {assets.Count} assets, {total} values");
    }
}
