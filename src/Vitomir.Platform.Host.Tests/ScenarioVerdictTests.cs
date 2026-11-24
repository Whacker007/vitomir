// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The scenario runner's statistics and verdicts (the runner's output contract), without a
/// world: Wilson's interval, the order statistics, the fixed-seed bootstrap (the same on every call), Spearman's correlation; an
/// aggregate passes only when its whole interval is on the passing side, fails when the whole is on the failing side; each run's and
/// any run's checks; a trend rising, falling, flat and inconclusive; the selectors' value matching and regions; the published schema
/// of results.json accepts what the runner writes and refuses a field it does not know.
/// </summary>
public sealed class ScenarioVerdictTests
{
    static RunValue V(double? v) => new(v);

    [Fact]
    public void Wilson_and_the_order_statistics_are_the_textbook_ones()
    {
        var w = ScenarioStats.Wilson(8, 10)!;
        Assert.Equal(0.4902, w[0], 3);      // Wilson score interval, 8 of 10, z = 1.96: [0.490, 0.943]
        Assert.Equal(0.9433, w[1], 3);
        Assert.Null(ScenarioStats.Wilson(0, 0));
        var ws = ScenarioStats.Wilson(0, 20)!;
        Assert.Equal(0, ws[0], 9);
        Assert.Equal(0.1611, ws[1], 3);
        var xs = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Equal(5.5, ScenarioStats.Median(xs), 9);
        Assert.Equal(1.9, ScenarioStats.Quantile(xs, 0.1), 9);   // R's type 7
        Assert.Equal(9.1, ScenarioStats.Quantile(xs, 0.9), 9);
        Assert.Equal(1.0, ScenarioStats.Spearman([1, 2, 3, 4], [10, 20, 25, 90])!.Value, 9);
        Assert.Equal(-1.0, ScenarioStats.Spearman([1, 2, 3, 4], [9, 7, 5, 1])!.Value, 9);
        Assert.Equal(0.0, ScenarioStats.Spearman([1, 2, 3], [4, 4, 4])!.Value, 9);   // a statistic that does not vary is flat
    }

    [Fact]
    public void Kaplan_Meier_counts_a_run_without_the_event_as_censored_at_its_end()
    {
        // Events at 1, 2, 3; two runs censored at 2.5 and 10: S = 4/5 after 1, 3/5 after 2, then 2 at risk at 3: 3/10.
        (double, bool)[] runs = [(1, true), (2, true), (2.5, false), (3, true), (10, false)];
        var curve = ScenarioStats.KaplanMeier(runs);
        Assert.Equal([(1.0, 0.8), (2.0, 0.6), (3.0, 0.3)], curve.Select(c => (c.Time, Math.Round(c.Survival, 9))).ToList());
        Assert.Equal(3.0, ScenarioStats.KaplanMeierMedian(runs));
        Assert.Null(ScenarioStats.KaplanMeierMedian([(1, true), (5, false), (5, false)]));   // fewer than half had it
        Assert.Equal(1 * 1 + 0.8 * 1 + 0.6 * 1 + 0.3 * 1, ScenarioStats.RestrictedMean(runs, 4), 9);
        // In an aggregate: a time's runs, one censored at its end.
        var agg = ScenarioVerdicts.Aggregate(MeasureKind.Time, "s", [new RunValue(1, FollowedSeconds: 8), new RunValue(2, FollowedSeconds: 8), new RunValue(null, FollowedSeconds: 8)], false);
        Assert.Equal(1, agg.Censored);
        Assert.Equal(1.5, agg.Mean!.Value, 9);           // of the runs that had one
        Assert.Equal(2.0, agg.KaplanMeierMedian);         // over every run
        Assert.Equal(8.0, agg.RestrictedToSeconds);
    }

    [Fact]
    public void The_bootstrap_is_the_same_on_every_call_and_degenerate_for_constant_runs()
    {
        var runs = Enumerable.Range(0, 30).Select(i => V(i % 7)).ToList();
        var a = ScenarioVerdicts.Interval(Statistic.Mean, MeasureKind.Count, runs, false).Interval!;
        var b = ScenarioVerdicts.Interval(Statistic.Mean, MeasureKind.Count, runs, false).Interval!;
        Assert.Equal(a, b);
        Assert.True(a[0] < 3 && a[1] > 2.5, $"[{a[0]}, {a[1]}]");
        var flat = ScenarioVerdicts.Interval(Statistic.Mean, MeasureKind.Count, Enumerable.Repeat(V(4), 10).ToList(), false).Interval!;
        Assert.Equal([4.0, 4.0], flat);
    }

    [Fact]
    public void An_aggregate_passes_fails_or_is_inconclusive_by_its_whole_interval()
    {
        var above = new Comparison(CompareOp.Above, 0.5);
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.Judge(above, 0.6, 0.9));
        Assert.Equal(Verdict.Fail, ScenarioVerdicts.Judge(above, 0.1, 0.5));
        Assert.Equal(Verdict.Inconclusive, ScenarioVerdicts.Judge(above, 0.4, 0.7));
        var between = new Comparison(CompareOp.Between, 1, 3);
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.Judge(between, 1, 3));
        Assert.Equal(Verdict.Fail, ScenarioVerdicts.Judge(between, 3.5, 4));
        Assert.Equal(Verdict.Inconclusive, ScenarioVerdicts.Judge(between, 2, 4));
        var equals = new Comparison(CompareOp.Equals, 30);
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.Judge(equals, 30, 30));
        Assert.Equal(Verdict.Fail, ScenarioVerdicts.Judge(equals, 0, 0));
        Assert.Equal(Verdict.Inconclusive, ScenarioVerdicts.Judge(equals, 29, 31));

        // A rate of one trial a run (Wilson): 18 of 20 runs held, above 0.5: the whole interval is above.
        var spec = new AssertionSpec("share", new RateForm(new EventSelector("A"), new EventSelector("B")), above, AssertionScope.Aggregate);
        var runs = Enumerable.Range(0, 20).Select(i => new RunValue(i < 18 ? 1 : 0, i < 18 ? 1 : 0, 1)).ToList();
        var (v, res) = ScenarioVerdicts.AtPoint(spec, MeasureKind.Rate, runs, [.. Enumerable.Range(0, 20).Select(i => (ulong)(100 + i))], wilson: true);
        Assert.Equal(Verdict.Pass, v);
        Assert.Equal(0.9, res.Measured!.Value, 9);
        Assert.Equal("rate", res.Statistic);
        // Too few runs for an interval: inconclusive, said so.
        var (one, oneRes) = ScenarioVerdicts.AtPoint(spec, MeasureKind.Rate, [runs[0]], [1UL], wilson: true);
        Assert.Equal(Verdict.Inconclusive, one);
        Assert.Contains("fewer than 2", oneRes.Note);
    }

    [Fact]
    public void Each_run_and_any_run_count_runs_and_name_the_first_failing_seed()
    {
        var eq0 = new Comparison(CompareOp.Equals, 0);
        var each = new AssertionSpec("never attacked", new CountForm(new EventSelector("AttackBegun", By: "Guard")), eq0, AssertionScope.EachRun);
        var values = new List<RunValue> { V(0), V(0), V(2), V(0), V(null) };
        ulong[] seeds = [10, 11, 12, 13, 14];
        var (v, r) = ScenarioVerdicts.AtPoint(each, MeasureKind.Count, values, seeds, false);
        Assert.Equal(Verdict.Fail, v);
        Assert.Equal(3, r.RunsPassed);
        Assert.Equal(5, r.Runs);
        Assert.Equal(12UL, r.FirstFailingSeed);
        Assert.Contains("counts as failing", r.Note);     // a run with no value
        var share = each with { PassShare = 0.6 };
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.AtPoint(share, MeasureKind.Count, values, seeds, false).Verdict);
        var any = each with { Scope = AssertionScope.AnyRun, Compare = new Comparison(CompareOp.AtLeast, 2) };
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.AtPoint(any, MeasureKind.Count, values, seeds, false).Verdict);
    }

    static (int, string, IReadOnlyList<RunValue>) Point(int index, string value, params double[] runs) => (index, value, runs.Select(x => V(x)).ToList());

    [Fact]
    public void A_trend_rises_falls_is_flat_or_is_inconclusive_by_its_correlations_interval()
    {
        var rising = new AssertionSpec("rises", new MeasureRefForm("m"), null, AssertionScope.Trend, Over: "A.sheet.values.melee", Direction: TrendDirection.Rising);
        var up = new[] { Point(0, "0", 1, 1.1, 0.9), Point(1, "1", 2, 2.1, 1.9), Point(2, "2", 3, 3.2, 2.8), Point(3, "3", 4, 4.1, 3.9) };
        var (v, t, _) = ScenarioVerdicts.Trend(rising, MeasureKind.Value, up, false);
        Assert.Equal(Verdict.Pass, v);
        Assert.Equal(1.0, t.Correlation!.Value, 9);
        Assert.Equal(4, t.ByValue.Count);
        var falling = rising with { Direction = TrendDirection.Falling };
        Assert.Equal(Verdict.Fail, ScenarioVerdicts.Trend(falling, MeasureKind.Value, up, false).Verdict);
        var flat = rising with { Direction = TrendDirection.Flat };
        var same = new[] { Point(0, "0", 5, 5, 5), Point(1, "1", 5, 5, 5), Point(2, "2", 5, 5, 5) };
        Assert.Equal(Verdict.Pass, ScenarioVerdicts.Trend(flat, MeasureKind.Value, same, false).Verdict);
        Assert.Equal(Verdict.Fail, ScenarioVerdicts.Trend(flat, MeasureKind.Value, up, false).Verdict);
        // Noise with no order: neither way.
        var noise = new[] { Point(0, "0", 1, 5, 3), Point(1, "1", 4, 2, 3), Point(2, "2", 2, 5, 1), Point(3, "3", 5, 1, 3) };
        Assert.Equal(Verdict.Inconclusive, ScenarioVerdicts.Trend(rising, MeasureKind.Value, noise, false).Verdict);
        // Too few sweep values with two runs: inconclusive, said so.
        var thin = new[] { Point(0, "0", 1), Point(1, "1", 2), Point(2, "2", 3) };
        var (tv, _, note) = ScenarioVerdicts.Trend(rising, MeasureKind.Value, thin, false);
        Assert.Equal(Verdict.Inconclusive, tv);
        Assert.Contains("a trend needs 3", note);
    }

    [Fact]
    public void Selectors_match_values_without_regard_to_case_and_regions_as_aimAt_names_them()
    {
        Assert.True(ScenarioEventLog.ValueEquals("Held", "held"));
        Assert.True(ScenarioEventLog.ValueEquals(3.0, "3"));
        Assert.True(ScenarioEventLog.ValueEquals(true, "True"));
        Assert.False(ScenarioEventLog.ValueEquals("Body", "shield"));
        Assert.Equal(("upperArm", "left"), ScenarioEventLog.RegionOf("left upper arm"));
        Assert.Equal(("chest", null), ScenarioEventLog.RegionOf("chest"));
        Assert.Null(ScenarioEventLog.RegionOf("kite shield"));
        Assert.True(ScenarioEventLog.RegionMatches("left upperArm", "upperArm", "left"));
        Assert.True(ScenarioEventLog.RegionMatches("upperArm", "upperArm", "right"));     // no side asked: either
        Assert.False(ScenarioEventLog.RegionMatches("upperArm left", "upperArm", "right"));
        Assert.Equal("upperArm left", ScenarioEventLog.EnumRegionWords("LeftUpperArm"));
        Assert.Equal("chest", ScenarioEventLog.EnumRegionWords("Chest"));
        Assert.Equal(5, ScenarioRunner.WorstExit([0, 3, 5, 4]));
        Assert.Equal(2, ScenarioRunner.WorstExit([1, 2, 5]));
        Assert.Equal(4, ScenarioRunner.WorstExit([0, 4]));
    }

    [Fact]
    public void The_published_results_schema_accepts_what_the_runner_writes_and_refuses_an_unknown_field()
    {
        var schema = ScenarioSchemas.Results();
        var results = new ScenarioResults(ScenarioResults.SchemaName,
            new ScenarioIdentity("x.scenario.json", "abc", "vitomir-scenario/1", "t"),
            new Provenance("Vitomir Platform 1.0", null, null, "M", "enhanced", new Dictionary<string, ModuleProvenance> { ["enhanced"] = new("h", "p", true) },
                ["sandbox"], new LevelProvenance("free-yard", "/l", null), "NATIVE", "--scenario x", "2026-10-24T00:00:00-04:00", 1.5, "d", "c", 1, 2, [], 1, true),
            [new PointResult(0, "", new Dictionary<string, JsonNode?> { ["A.sheet.values.melee"] = JsonValue.Create(3) }, 2, [1, 2],
                new Dictionary<string, int> { ["TIMED_OUT"] = 2 }, new Dictionary<string, string> { ["A"] = "AI (duel)" }, false,
                new Dictionary<string, MeasureAggregate> { ["m"] = new("count", "count", 2, Mean: 1.5, Interval95: [1, 2], IntervalMethod: "bootstrap") },
                [new AssertionPointResult("a", "eachRun", "pass", 1, [0.3, 1], "share of runs passed", "equals 0", 2, 2, 2, 1)],
                new Dictionary<string, int>(), null)],
            [new AssertionResult("a", "eachRun", "pass", ["pass"])],
            new ResultsSummary(1, 2, 1, 0, 0, 0, 0, "PASSED"),
            new ResultsFiles("runs.csv", null, null, null, null, null, null, "scenario.json", "resolved.json", null),
            [], ["runs.csv"], new Dictionary<string, string> { ["m"] = "count" },
            "COMPLETED", new RunCounts(2, 2, 2, 2, 0, 0, 0), [], [new MetricResult("m", 0, "count", "count", 1.5, "mean", 2, null, null, 0, new Interval95(1, 2, "bootstrap"), 2)], ["x"]);
        var json = JsonNode.Parse(ScenarioJson.Write(results))!;
        Assert.Empty(MiniSchema.Errors(json, schema));
        json["surprise"] = 1;
        Assert.NotEmpty(MiniSchema.Errors(json, schema));
        var refused = JsonNode.Parse("""{ "schema": "VITOMIR_SCENARIO_RESULTS_1", "state": "FAILED", "scenario": { "file": "x" }, "errors": [ { "code": "UnknownName", "pointer": "/actors/0", "message": "m", "suggestions": [] } ], "summary": { "exitCode": 1 } }""")!;
        Assert.Empty(MiniSchema.Errors(refused, schema));
    }
}

/// <summary>
/// A small JSON Schema checker for the tests (the subset the runner's schemas use: $ref into $defs, anyOf, type, const, enum,
/// properties, required, additionalProperties, items). Returns each problem with its path.
/// </summary>
static class MiniSchema
{
    public static List<string> Errors(JsonNode? value, JsonObject schema)
    {
        var errors = new List<string>();
        Check(value, schema, schema, "$", errors);
        return errors;
    }

    static void Check(JsonNode? v, JsonObject s, JsonObject root, string path, List<string> errors)
    {
        if (s["$ref"] is JsonValue r)
        {
            string name = r.GetValue<string>().Split('/')[^1];
            Check(v, root["$defs"]![name]!.AsObject(), root, path, errors);
            return;
        }
        if (s["anyOf"] is JsonArray any)
        {
            List<string>? best = null;
            foreach (var alt in any)
            {
                var e = new List<string>();
                Check(v, alt!.AsObject(), root, path, e);
                if (e.Count == 0) return;
                if (best is null || e.Count < best.Count) best = e;
            }
            errors.AddRange(best ?? [$"{path}: no alternative"]);
            return;
        }
        if (s["const"] is JsonNode c && !JsonNode.DeepEquals(c, v)) errors.Add($"{path}: not {c.ToJsonString()}");
        if (s["type"] is JsonNode t)
        {
            var types = t is JsonArray ta ? ta.Select(x => x!.GetValue<string>()).ToList() : [t.GetValue<string>()];
            string kind = v switch
            {
                null => "null",
                JsonObject => "object",
                JsonArray => "array",
                JsonValue jv when jv.GetValueKind() == System.Text.Json.JsonValueKind.String => "string",
                JsonValue jv when jv.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
                JsonValue jv when jv.GetValueKind() == System.Text.Json.JsonValueKind.Number => double.IsInteger(jv.GetValue<double>()) ? "integer" : "number",
                _ => "?",
            };
            bool ok = types.Contains(kind) || (kind == "integer" && types.Contains("number"));
            if (!ok) { errors.Add($"{path}: {kind}, wanted {string.Join("|", types)}"); return; }
        }
        if (v is JsonObject o)
        {
            var props = s["properties"] as JsonObject;
            if (s["required"] is JsonArray req)
                foreach (var name in req) if (!o.ContainsKey(name!.GetValue<string>())) errors.Add($"{path}: missing {name}");
            foreach (var (k, child) in o)
            {
                if (props is not null && props[k] is JsonObject ps) Check(child, ps, root, $"{path}.{k}", errors);
                else if (s["additionalProperties"] is JsonValue ap && !ap.GetValue<bool>()) errors.Add($"{path}: {k} is not a field");
                else if (s["additionalProperties"] is JsonObject aps) Check(child, aps, root, $"{path}.{k}", errors);
            }
        }
        if (v is JsonArray a && s["items"] is JsonObject items)
            for (int i = 0; i < a.Count; i++) Check(a[i], items, root, $"{path}[{i}]", errors);
    }
}
