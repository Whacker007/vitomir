// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json.Nodes;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The compiler's part for the overrides beyond the switches and the global tempo — each point's module data
/// overrides (the session's <see cref="SessionOptions.DataOverrides"/>, applied to the modules' reads before they are configured), the
/// actors' own tempos and clip rates (on the cast's fighters: <see cref="CastFighter.Tempo"/>, <see cref="CastFighter.ClipRates"/>) and
/// the light (<see cref="SessionOptions.Light"/>) — each recorded as a setup condition of the point (results.json's <c>setup</c>): a data
/// override with its asset, pointer, value before and after, the asset's SHA-256 before and after, its status and whether it is a
/// counterfactual.
/// </summary>
internal sealed partial class ScenarioCompiler
{
    /// <summary>A swept value of the overrides' paths or <c>light</c> set in a point's copy of the resolved scenario.</summary>
    static void SetOverride(JsonObject doc, ScenarioReader.Settable path, JsonNode? value)
    {
        int Index() => int.Parse(path.Key!, CultureInfo.InvariantCulture);
        switch (path.Kind)
        {
            case "light": doc["light"] = value; break;
            case "data": doc["overrides"]!["data"]![Index()]!["value"] = value; break;
            case "actorTempo": doc["overrides"]!["actorTempo"]![path.Key!] = value; break;
            case "clipRate": doc["overrides"]!["clipRate"]![Index()]!["rate"] = value; break;
        }
    }

    /// <summary>
    /// The point's data overrides (as the session's text, null for none) and light (null for the time's); the actors' tempos and clip
    /// rates put on <paramref name="fighters"/>; each a setup condition in <paramref name="setup"/>.
    /// </summary>
    (string? Data, string? Light) Overridden(JsonObject doc, List<CastFighter> fighters, JsonArray setup)
    {
        var ov = doc["overrides"]!.AsObject();
        var swept = reader.SweepPaths.Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        string Source(string path) => swept.Contains(path) ? "sweep" : "scenario";

        // ---- data
        var list = new List<DataOverride>();
        var entries = (ov["data"] as JsonArray) ?? [];
        var notes = new List<JsonObject>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i]!.AsObject();
            string file = S(e["asset"]), pointer = S(e["pointer"]), op = S(e["op"]);
            var asset = game?.DataAssets.FirstOrDefault(a => a.File == file);
            var id = asset?.Id ?? AssetId.FromFile("enhanced", file);
            var kind = op switch { "remove" => DataOverrideOp.Remove, "add" => DataOverrideOp.Add, _ => DataOverrideOp.Replace };
            list.Add(new DataOverride(id, pointer, kind == DataOverrideOp.Remove ? null : JsonPointer.Text(e["value"]), kind));
            var note = new JsonObject
            {
                ["actor"] = null, ["kind"] = "data", ["id"] = file + "#" + pointer, ["value"] = kind == DataOverrideOp.Remove ? null : e["value"]?.DeepClone(),
                ["source"] = Source($"overrides.data.{i}.value"), ["asset"] = file, ["assetId"] = id.ToString(), ["pointer"] = pointer, ["op"] = op,
                ["before"] = e["before"]?.DeepClone(), ["status"] = e["status"]?.DeepClone(), ["counterfactual"] = e["counterfactual"]?.DeepClone(),
            };
            notes.Add(note);
            setup.Add(note);
        }
        // The asset's hash before and after each, as the loader will apply them (the same bytes, the same order).
        if (game is not null)
            foreach (var group in list.Select((o, i) => (o, i)).GroupBy(x => x.o.Asset))
            {
                var asset = game.DataAssets.FirstOrDefault(a => a.Id == group.Key);
                if (asset is null) continue;
                var applied = new List<AppliedDataOverride>();
                try { DataOverrides.Apply(group.Key, asset.Bytes, [.. group.Select(x => x.o)], applied); }
                catch (DataOverrideException e) { reader.Error(ScenarioErrorCode.BadPath, "/overrides/data", e.Message); continue; }
                int k = 0;
                foreach (var (_, i) in group)
                {
                    if (k >= applied.Count) break;
                    notes[i]["assetSha256Before"] = applied[k].Sha256Before;
                    notes[i]["assetSha256After"] = applied[k].Sha256After;
                    k++;
                }
            }

        // ---- the actors' own tempos and clip rates
        var names = fighters.Select(f => f.Name).ToList();
        if (ov["actorTempo"] is JsonObject tempos)
            foreach (var (who, v) in tempos)
            {
                int i = names.IndexOf(who);
                if (i < 0 || v is null) continue;
                fighters[i] = fighters[i] with { Tempo = (float)v.GetValue<double>() };
                setup.Add(new JsonObject { ["actor"] = who, ["kind"] = "actorTempo", ["id"] = "tempo", ["value"] = v.DeepClone(), ["source"] = Source($"overrides.actorTempo.{who}") });
            }
        if (ov["clipRate"] is JsonArray rates)
            for (int r = 0; r < rates.Count; r++)
            {
                var c = rates[r]!.AsObject();
                int i = names.IndexOf(S(c["actor"]));
                if (i < 0) continue;
                var rate = new CastClipRate(S(c["clip"]), (float)D(c["rate"]));
                fighters[i] = fighters[i] with { ClipRates = [.. fighters[i].ClipRates ?? [], rate] };
                setup.Add(new JsonObject { ["actor"] = names[i], ["kind"] = "clipRate", ["id"] = rate.Clip, ["value"] = c["rate"]!.DeepClone(), ["source"] = Source($"overrides.clipRate.{r}.rate") });
            }

        // ---- the light
        string? light = doc["light"] switch
        {
            JsonValue v when v.TryGetValue(out double level) => level.ToString("R", CultureInfo.InvariantCulture),
            JsonArray steps => string.Join(";", steps.Select(s => $"{D(s!["atSeconds"]).ToString("R", CultureInfo.InvariantCulture)}:{D(s!["level"]).ToString("R", CultureInfo.InvariantCulture)}")),
            _ => null,
        };
        if (light is not null) setup.Add(new JsonObject { ["actor"] = null, ["kind"] = "light", ["id"] = "light", ["value"] = doc["light"]!.DeepClone(), ["source"] = Source("light") });

        return (list.Count == 0 ? null : DataOverrides.ToNote(list), light);
    }
}
