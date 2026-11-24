// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The reader's objectives (<c>"objectives"</c>: what an actor or a side is out to do, each resolved during the run to
/// MET, FAILED or UNRESOLVED) and the paired swap (<c>"swap"</c>: each seed run again with two
/// actors' places exchanged, or the two sides' ground turned round). Both are written into the resolved form only when the file gives
/// them, so a scenario without them resolves as before, byte for byte.
/// </summary>
internal sealed partial class ScenarioReader
{
    internal static readonly string[] ObjectiveKinds = ["defeat", "survive", "reach", "escape", "protect", "hold"];
    internal static readonly string[] ObjectiveFields = ["name", "actor", "side", "defeat", "survive", "reach", "escape", "protect", "hold", "bySeconds", "untilSeconds"];
    internal static readonly string[] AreaFields = ["point", "place", "radiusMetres"];
    internal static readonly string[] EscapeFields = ["fromFoesMetres", "outOf"];
    internal static readonly string[] SwapFields = ["actors", "sides"];
    internal static readonly string[] OnObjectivesWords = ["none", "first", "all"];

    /// <summary>The default radius of a <c>reach</c> or <c>hold</c> area (m; ENGINE_SANDBOX, ours: a body's stride either side).</summary>
    public const double DefaultAreaRadiusMetres = 1.0;

    /// <summary>The scenario's objectives, resolved; null when the file gives none.</summary>
    JsonArray? ObjectivesOf(JsonElement root, JsonArray actors)
    {
        if (!root.TryGetProperty("objectives", out var list) || list.ValueKind == JsonValueKind.Null) return null;
        if (!IsList(list, "/objectives", "a list of objectives, each { \"name\", \"actor\" or \"side\", and one of defeat, survive, reach, escape, protect, hold }")) return null;
        var names = actors.Select(a => a!["name"]!.GetValue<string>()).ToList();
        var sideOf = actors.Select(a => a!["side"] is JsonValue sv && sv.TryGetValue(out string? s) ? s : null).ToList();
        var sides = sideOf.Where(s => s is not null).Select(s => s!).Distinct(StringComparer.Ordinal).ToList();
        var o = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var e in list.EnumerateArray())
        {
            string ptr = ScenarioPointer.Of("/objectives", i);
            int index = i++;
            if (!IsObject(e, ptr, "{ \"name\", \"actor\" or \"side\", and one of defeat, survive, reach, escape, protect, hold }")) continue;
            Known(e, ptr, ObjectiveFields, "an objective");
            var r = new JsonObject();
            string kinds = string.Join(", ", ObjectiveKinds);
            var given = ObjectiveKinds.Where(k => Has(e, k)).ToList();
            if (given.Count != 1)
            {
                Error(ScenarioErrorCode.BadObjective, ptr, given.Count == 0 ? $"names no kind: give one of {kinds}" : $"gives {string.Join(" and ", given)}: one kind an objective ({kinds})");
                continue;
            }
            string kind = given[0];
            string name = Text(e, "name", ptr) ?? $"{kind}-{index}";
            if (name.Length == 0 || name.IndexOfAny(['.', ',', ':', ';', '@', '^', '+', '/', '~', '"']) >= 0)
                Error(ScenarioErrorCode.BadObjective, ptr + "/name", $"\"{name}\": letters, digits, spaces, _ and - only (it names a column of runs.csv)");
            else if (!seen.Add(name)) Error(ScenarioErrorCode.BadObjective, ptr + "/name", $"\"{name}\" is repeated: each objective its own name");
            r["name"] = name;

            // The owner: an actor, or a side.
            string? actor = Text(e, "actor", ptr), side = Text(e, "side", ptr);
            int owner = -1;
            if ((actor is null) == (side is null))
            {
                Error(ScenarioErrorCode.BadObjective, ptr, actor is null ? "has no owner: give \"actor\" (whose objective it is) or \"side\"" : "gives \"actor\" and \"side\": one owner");
                continue;
            }
            if (actor is not null)
            {
                owner = names.FindIndex(n => n.Equals(actor, StringComparison.OrdinalIgnoreCase));
                if (owner < 0) { Error(ScenarioErrorCode.UnknownName, ptr + "/actor", $"no actor \"{actor}\"", ScenarioPointer.Nearest(actor, names)); continue; }
                r["actor"] = names[owner];
            }
            else
            {
                var hit = sides.FirstOrDefault(s => s.Equals(side, StringComparison.OrdinalIgnoreCase));
                if (hit is null) { Error(ScenarioErrorCode.UnknownName, ptr + "/side", sides.Count == 0 ? $"no actor has a side (\"{side}\"): give every actor a side, or the objective an actor" : $"no side \"{side}\"; the sides are {ScenarioPointer.Words(sides)}", ScenarioPointer.Nearest(side!, sides)); continue; }
                r["side"] = hit;
                side = hit;
                if (kind is "reach" or "escape" or "hold") { Error(ScenarioErrorCode.BadObjective, ptr + "/" + kind, $"a side cannot {kind} a place: give the objective to an actor (one objective an actor)"); continue; }
            }
            string? ownSide = owner >= 0 ? sideOf[owner] : side;
            r["kind"] = kind;
            var k = e.GetProperty(kind);
            string kp = ptr + "/" + kind;
            switch (kind)
            {
                case "defeat":
                case "protect":
                {
                    string? target = null, targetSide = null;
                    if (k.ValueKind == JsonValueKind.String) target = k.GetString();
                    else if (k.ValueKind == JsonValueKind.Object && kind == "defeat")
                    {
                        Known(k, kp, ["actor", "side"], "a defeat's target");
                        target = Text(k, "actor", kp);
                        targetSide = Text(k, "side", kp);
                        if ((target is null) == (targetSide is null)) { Error(ScenarioErrorCode.BadObjective, kp, "names one target: \"<actor>\", { \"actor\" } or { \"side\" }"); continue; }
                    }
                    else { Error(ScenarioErrorCode.WrongType, kp, kind == "defeat" ? "must be an actor's name, { \"actor\" } or { \"side\" }" : "must be the name of the actor protected"); continue; }
                    if (target is not null)
                    {
                        int t = names.FindIndex(n => n.Equals(target, StringComparison.OrdinalIgnoreCase));
                        if (t < 0) { Error(ScenarioErrorCode.UnknownName, k.ValueKind == JsonValueKind.String ? kp : kp + "/actor", $"no actor \"{target}\"", ScenarioPointer.Nearest(target, names)); continue; }
                        if (t == owner) { Error(ScenarioErrorCode.BadObjective, kp, $"{names[t]} cannot {kind} itself"); continue; }
                        if (kind == "defeat" && ownSide is not null && sideOf[t] == ownSide) { Error(ScenarioErrorCode.BadObjective, kp, $"{names[t]} is on {(owner >= 0 ? names[owner] + "'s" : "its")} own side ({ownSide}): a defeat's target is a foe"); continue; }
                        r["target"] = names[t];
                    }
                    else
                    {
                        var hit = sides.FirstOrDefault(s => s.Equals(targetSide, StringComparison.OrdinalIgnoreCase));
                        if (hit is null) { Error(ScenarioErrorCode.UnknownName, kp + "/side", sides.Count == 0 ? "no actor has a side" : $"no side \"{targetSide}\"; the sides are {ScenarioPointer.Words(sides)}", ScenarioPointer.Nearest(targetSide!, sides)); continue; }
                        if (hit == ownSide) { Error(ScenarioErrorCode.BadObjective, kp + "/side", $"\"{hit}\" is the owner's own side: a defeat's target is a foe"); continue; }
                        r["targetSide"] = hit;
                    }
                    break;
                }
                case "survive":
                    if (k.ValueKind != JsonValueKind.True) { Error(ScenarioErrorCode.WrongType, kp, "must be true (until the run ends, or untilSeconds)"); continue; }
                    r["survive"] = true;
                    break;
                case "reach":
                case "hold":
                    if (Area(k, kp, kind, required: false) is not { } area) continue;
                    r["area"] = area;
                    break;
                case "escape":
                    if (!IsObject(k, kp, "{ \"fromFoesMetres\": d } or { \"outOf\": { \"point\" or \"place\", \"radiusMetres\" } }")) continue;
                    Known(k, kp, EscapeFields, "an escape");
                    if (Has(k, "fromFoesMetres") == Has(k, "outOf")) { Error(ScenarioErrorCode.BadObjective, kp, "gives one of \"fromFoesMetres\" (farther than that from every foe still standing) and \"outOf\" (out of an area)"); continue; }
                    if (Has(k, "fromFoesMetres"))
                    {
                        if (Number(k, "fromFoesMetres", kp, 0, 10_000, "m") is not { } d) continue;
                        r["fromFoesMetres"] = d;
                    }
                    else
                    {
                        if (Area(k.GetProperty("outOf"), kp + "/outOf", "escape", required: true) is not { } outOf) continue;
                        r["outOf"] = outOf;
                    }
                    break;
            }

            // Times: a deadline for what is to be done, an end for what is to be kept.
            bool keeps = kind is "survive" or "protect" or "hold";
            if (Has(e, "bySeconds"))
            {
                if (keeps) Error(ScenarioErrorCode.BadObjective, ptr + "/bySeconds", $"a {kind} is kept, not done: give \"untilSeconds\" (it is met at that second, or when the run ends)");
                else if (Number(e, "bySeconds", ptr, 0, 3600, "s") is { } by) r["bySeconds"] = by;
            }
            if (Has(e, "untilSeconds"))
            {
                if (!keeps) Error(ScenarioErrorCode.BadObjective, ptr + "/untilSeconds", $"a {kind} is done by a time: give \"bySeconds\" (it fails when that second passes undone)");
                else if (Number(e, "untilSeconds", ptr, 0, 3600, "s") is { } until) r["untilSeconds"] = until;
            }
            if (!keeps && !r.ContainsKey("bySeconds")) r["bySeconds"] = "none: UNRESOLVED if the run ends first";
            if (keeps && !r.ContainsKey("untilSeconds")) r["untilSeconds"] = "the run's end";
            o.Add(r);
        }
        return o;
    }

    /// <summary>An area: <c>{ "point": [x, y(, z)] | "place": "&lt;the level's named place&gt;", "radiusMetres": r }</c>, resolved (the place's point written in).</summary>
    JsonObject? Area(JsonElement a, string at, string kind, bool required)
    {
        if (!IsObject(a, at, "{ \"point\": [x, y] or \"place\": \"<a named place of the level>\", \"radiusMetres\": r }")) return null;
        Known(a, at, AreaFields, $"a {kind}'s area");
        var r = new JsonObject();
        if (Has(a, "point") == Has(a, "place")) { Error(ScenarioErrorCode.BadObjective, at, "gives one of \"point\" ([x, y] or [x, y, z], metres) and \"place\" (a named place of the level: --describe places)"); return null; }
        if (Has(a, "point"))
        {
            var p = a.GetProperty("point");
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() is < 2 or > 3 || p.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
            { Error(ScenarioErrorCode.BadPlace, at + "/point", "must be [x, y] or [x, y, z] (metres, the level's frame)"); return null; }
            r["point"] = new JsonArray([.. p.EnumerateArray().Select(x => (JsonNode?)JsonValue.Create(x.GetDouble()))]);
        }
        else
        {
            string place = Text(a, "place", at)!;
            r["place"] = place;
            if (game?.Level is { } level)
            {
                var starts = level.Starts.ToList();
                var hit = starts.FirstOrDefault(s => s.Name.Equals(place, StringComparison.OrdinalIgnoreCase) || s.Id.Equals(place, StringComparison.OrdinalIgnoreCase));
                if (hit.Name is null) { Error(ScenarioErrorCode.UnknownName, at + "/place", $"the level has no place \"{place}\" (its named places are its starts: --describe places)", ScenarioPointer.Nearest(place, [.. starts.Select(s => s.Name)])); return null; }
                r["place"] = hit.Name;
                r["point"] = new JsonArray(Math.Round(hit.Position.X, 4), Math.Round(hit.Position.Y, 4), Math.Round(hit.Position.Z, 4));
            }
        }
        if (Has(a, "radiusMetres")) { if (Number(a, "radiusMetres", at, 0, 10_000, "m") is { } rr) r["radiusMetres"] = rr; else return null; }
        else if (required) { Error(ScenarioErrorCode.BadObjective, at + "/radiusMetres", "is required: the area's radius (m)"); return null; }
        else r["radiusMetres"] = DefaultAreaRadiusMetres;
        return r;
    }

    /// <summary>The paired swap, resolved; null when the file gives none.</summary>
    JsonObject? SwapOf(JsonElement root, JsonArray actors)
    {
        if (!root.TryGetProperty("swap", out var sw) || sw.ValueKind == JsonValueKind.Null) return null;
        if (!IsObject(sw, "/swap", "{ \"actors\": [\"<a>\", \"<b>\"] } or { \"sides\": true }")) return null;
        Known(sw, "/swap", SwapFields, "the swap");
        var names = actors.Select(a => a!["name"]!.GetValue<string>()).ToList();
        if (Has(sw, "actors") == Has(sw, "sides")) { Error(ScenarioErrorCode.BadObjective, "/swap", "gives one of \"actors\" (two actors' places and facings exchanged) and \"sides\" (the two sides' ground turned round)"); return null; }
        if (Has(sw, "actors"))
        {
            var a = sw.GetProperty("actors");
            if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 2 || a.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            { Error(ScenarioErrorCode.BadObjective, "/swap/actors", "must name two actors: [\"<a>\", \"<b>\"]"); return null; }
            var pair = new JsonArray();
            int k = 0;
            foreach (var x in a.EnumerateArray())
            {
                string n = x.GetString()!;
                int idx = names.FindIndex(m => m.Equals(n, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) { Error(ScenarioErrorCode.UnknownName, $"/swap/actors/{k}", $"no actor \"{n}\"", ScenarioPointer.Nearest(n, names)); return null; }
                pair.Add(names[idx]);
                k++;
            }
            if (pair[0]!.GetValue<string>() == pair[1]!.GetValue<string>()) { Error(ScenarioErrorCode.BadObjective, "/swap/actors", "names one actor twice: two actors exchange places"); return null; }
            return new JsonObject { ["actors"] = pair };
        }
        if (sw.GetProperty("sides").ValueKind != JsonValueKind.True) { Error(ScenarioErrorCode.WrongType, "/swap/sides", "must be true"); return null; }
        var sides = actors.Select(x => x!["side"] is JsonValue sv && sv.TryGetValue(out string? s) ? s : null).Where(s => s is not null).Distinct().ToList();
        if (sides.Count != 2) { Error(ScenarioErrorCode.BadObjective, "/swap/sides", $"turns two sides' ground round; this scenario has {(sides.Count == 0 ? "no sides" : $"{sides.Count} sides")}: give every actor one of two sides, or swap two actors"); return null; }
        return new JsonObject { ["sides"] = new JsonArray(sides[0], sides[1]) };
    }
}
