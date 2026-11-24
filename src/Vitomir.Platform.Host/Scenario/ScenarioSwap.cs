// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The paired swap. Each point of the scenario is followed by its <b>twin</b> with the swap
/// made — two actors' places and facings exchanged (<c>"swap": { "actors": [a, b] }</c>), or the two sides' ground turned round
/// (<c>"swap": { "sides": true }</c>: every actor's place turned 180° about the middle between the two sides' centres and its facing
/// turned with it, so each side stands where the other stood, its own formation kept). Both run on the same seeds (run r of each has seed
/// + r), so the runner can set each seed's result against its twin's (<see cref="SwapComparison"/>): a result that follows who stood where
/// shows. The swept path <c>swap</c> names each point's (<c>as written</c> or the swap's words) in runs.csv and the points' names.
/// </summary>
internal static class ScenarioSwap
{
    public const string Path = "swap";
    public const string AsWritten = "as written";

    /// <summary>The swap's words (<c>Christof&lt;-&gt;Guard</c>, <c>sides player&lt;-&gt;guards turned round</c>), or null with none.</summary>
    public static string? Words(JsonObject resolved) => resolved["swap"] switch
    {
        JsonObject { } s when s["actors"] is JsonArray a => $"{a[0]!.GetValue<string>()}<->{a[1]!.GetValue<string>()} places exchanged",
        JsonObject { } s when s["sides"] is JsonArray d => $"sides {d[0]!.GetValue<string>()}<->{d[1]!.GetValue<string>()} turned round",
        _ => null,
    };

    /// <summary>The points with each one's twin after it (re-numbered), or the points as they are when the scenario has no swap.</summary>
    public static List<SweepPoint> Apply(IReadOnlyList<SweepPoint> points, JsonObject resolved, ScenarioGame? game, ScenarioReader reader)
    {
        if (Words(resolved) is not { } words) return [.. points];
        bool range = resolved["ground"] is JsonValue gv && gv.TryGetValue(out string? g) && g == ScenarioGround.Range;
        bool freeYard = resolved["ground"] is JsonValue fv && fv.TryGetValue(out string? f) && f == ScenarioGround.FreeYard;
        var list = new List<SweepPoint>(points.Count * 2);
        foreach (var p in points)
        {
            var twinCast = Swapped(p.Cast, resolved["swap"]!.AsObject());
            for (int i = 0; i < twinCast.Fighters.Count; i++)
            {
                var c = twinCast.Fighters[i];
                var feet = new Vector3(c.X, c.Y, c.Z);
                if ((range ? ScenarioGame.RangeProblem(feet) : game?.PlaceProblem(feet, freeYard)) is { } problem)
                    reader.Error(ScenarioErrorCode.BadObjective, "/swap", $"swapped, {c.Name} would stand where it cannot: {problem}{(p.Name.Length == 0 ? "" : $", at point {p.Index} ({p.Name})")}");
            }
            list.Add(Named(p, list.Count, AsWritten, p.Cast, p.Resolved));
            list.Add(Named(p, list.Count, words, twinCast, Patched(p.Resolved, twinCast)));
        }
        return list;
    }

    static SweepPoint Named(SweepPoint p, int index, string value, ArenaCast cast, JsonObject? resolved) => p with
    {
        Index = index,
        Name = (p.Name.Length == 0 ? "" : p.Name + ", ") + $"{Path} = {value}",
        Values = [.. p.Values, (Path, System.Text.Json.JsonSerializer.Serialize(value))],
        Cast = cast,
        Resolved = resolved,
    };

    /// <summary>The cast with the swap made.</summary>
    public static ArenaCast Swapped(ArenaCast cast, JsonObject swap)
    {
        var fighters = cast.Fighters.ToList();
        if (swap["actors"] is JsonArray a)
        {
            int i = fighters.FindIndex(x => x.Name == a[0]!.GetValue<string>()), j = fighters.FindIndex(x => x.Name == a[1]!.GetValue<string>());
            if (i < 0 || j < 0) return cast;
            var (fi, fj) = (fighters[i], fighters[j]);
            fighters[i] = fi with { X = fj.X, Y = fj.Y, Z = fj.Z, Facing = fj.Facing };
            fighters[j] = fj with { X = fi.X, Y = fi.Y, Z = fi.Z, Facing = fi.Facing };
        }
        else if (swap["sides"] is JsonArray s)
        {
            string s0 = s[0]!.GetValue<string>(), s1 = s[1]!.GetValue<string>();
            Vector2 Centre(string side)
            {
                var m = fighters.Where(x => x.Side == side).ToList();
                return m.Count == 0 ? Vector2.Zero : new Vector2(m.Average(x => x.X), m.Average(x => x.Y));
            }
            var mid = (Centre(s0) + Centre(s1)) * 0.5f;
            for (int k = 0; k < fighters.Count; k++)
            {
                var x = fighters[k];
                float facing = x.Facing + 180f;
                if (facing > 180f) facing -= 360f;
                fighters[k] = x with { X = Round4(2 * mid.X - x.X), Y = Round4(2 * mid.Y - x.Y), Facing = Round3(facing) };
            }
        }
        return cast with { Fighters = fighters };
    }

    static JsonObject? Patched(JsonObject? resolved, ArenaCast cast)
    {
        if (resolved?.DeepClone() is not JsonObject r) return null;
        if (r["actors"] is JsonArray actors)
            for (int i = 0; i < actors.Count && i < cast.Fighters.Count; i++)
            {
                var c = cast.Fighters[i];
                actors[i]!["placeResolved"] = new JsonArray(Math.Round(c.X, 4), Math.Round(c.Y, 4), Math.Round(c.Z, 4));
                actors[i]!["facingResolvedDegrees"] = Math.Round(c.Facing, 3);
            }
        return r;
    }

    static float Round4(float v) => MathF.Round(v * 10000f) / 10000f;
    static float Round3(float v) => MathF.Round(v * 1000f) / 1000f;

    /// <summary>Each point against its twin (points 2k and 2k+1), seed for seed.</summary>
    internal static List<SwapComparison> Compare(IReadOnlyList<ScenarioPlanPoint> points, List<ScenarioRunner.RunOutput> done, ScenarioMeasuresModel model,
        IReadOnlyList<ObjectiveSpec> objectives)
    {
        var list = new List<SwapComparison>();
        foreach (var p in points)
        {
            if (p.Index % 2 != 0) continue;
            var twin = points.FirstOrDefault(x => x.Index == p.Index + 1);
            if (twin is null) continue;
            var a = done.Where(o => o.Point == p.Index && !o.Threw).ToDictionary(o => o.Seed);
            var pairs = done.Where(o => o.Point == twin.Index && !o.Threw && a.ContainsKey(o.Seed)).OrderBy(o => o.Run).Select(o => (A: a[o.Seed], B: o)).ToList();
            int changed = 0, differ = 0;
            foreach (var (x, y) in pairs)
            {
                bool objChanged = (x.Record.Objectives ?? []).Zip(y.Record.Objectives ?? []).Any(z => z.First.Result != z.Second.Result);
                if (x.Record.Stop != y.Record.Stop || x.Winner != y.Winner || objChanged) changed++;
                if (x.Record.Digest != y.Record.Digest) differ++;
            }
            var objs = objectives.Select(s =>
            {
                string R(ScenarioRunner.RunOutput o) => o.Record.Objectives?.FirstOrDefault(q => q.Name == s.Name)?.Result ?? "";
                return new SwapObjective(s.Name, pairs.Count(z => R(z.A) == "MET"), pairs.Count(z => R(z.B) == "MET"),
                    pairs.Count(z => R(z.A) != "MET" && R(z.B) == "MET"), pairs.Count(z => R(z.A) == "MET" && R(z.B) != "MET"),
                    pairs.Count(z => R(z.A) != R(z.B) && R(z.A) != "MET" && R(z.B) != "MET"));
            }).ToList();
            var differences = new List<PairedDifference>();
            foreach (var m in model.Measures)
            {
                var diffs = pairs.Where(z => z.A.Values.TryGetValue(m.Name, out var u) && u.Value is not null && z.B.Values.TryGetValue(m.Name, out var v) && v.Value is not null)
                    .Select(z => z.B.Values[m.Name].Value!.Value - z.A.Values[m.Name].Value!.Value).ToList();
                var iv = diffs.Count >= 2 ? ScenarioStats.Bootstrap(diffs.Count, idx => idx.Average(i => diffs[i])) : null;
                differences.Add(new PairedDifference(m.Name, diffs.Count, diffs.Count > 0 ? diffs.Average() : null,
                    iv is null ? null : new Interval95(iv[0], iv[1], $"paired bootstrap over seeds ({ScenarioStats.BootstrapResamples} resamples, fixed seed)"), diffs.Count(d => d != 0)));
            }
            string words = twin.Values.LastOrDefault(v => v.Path == Path).Value ?? "";
            list.Add(new SwapComparison(string.Create(CultureInfo.InvariantCulture, $"p{twin.Index:000}-swap-vs-p{p.Index:000}"), p.Index, twin.Index, words,
                pairs.Count, changed, differ, objs.Count > 0 ? objs : null, differences.Count > 0 ? differences : null));
        }
        return list;
    }
}
