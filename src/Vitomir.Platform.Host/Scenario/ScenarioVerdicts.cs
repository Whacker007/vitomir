// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// A point's aggregates and the assertions' verdicts: each measure over the point's
/// runs (a count's or value's order statistics, a rate's summed numerator and denominator with Wilson's interval, a delay's samples
/// pooled), and each assertion's pass, fail or inconclusive — an aggregate passes only when its whole 95 % interval is on the
/// passing side; a trend by Spearman's rank correlation over the sweep's values with a bootstrap over the paired runs.
/// </summary>
public static class ScenarioVerdicts
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>The verdicts' names in results.json.</summary>
    public static string Name(Verdict v) => v switch
    {
        Verdict.Pass => "pass", Verdict.Fail => "fail", Verdict.Inconclusive => "inconclusive", _ => "notApplicable",
    };

    /// <summary>The worst of several verdicts (fail, then inconclusive, then pass); not applicable when none applies.</summary>
    public static Verdict Worst(IEnumerable<Verdict> vs)
    {
        bool any = false, inconclusive = false;
        foreach (var v in vs)
        {
            if (v == Verdict.Fail) return Verdict.Fail;
            if (v == Verdict.Inconclusive) inconclusive = true;
            if (v != Verdict.NotApplicable) any = true;
        }
        return inconclusive ? Verdict.Inconclusive : any ? Verdict.Pass : Verdict.NotApplicable;
    }

    // ---- what kind a measure is ----

    /// <summary>A form's kind; a named measure's through <paramref name="named"/>, a built-in one a count (or a value for its Health and Blood).</summary>
    public static MeasureKind KindOf(ValueForm f, Func<string, ValueForm?> named, int depth = 0) => f switch
    {
        CountForm => MeasureKind.Count,
        RateForm { PerSecond: true } => MeasureKind.RatePerSecond,
        RateForm => MeasureKind.Rate,
        TimeOfForm => MeasureKind.Time,
        DelayForm => MeasureKind.Delay,
        CheckForm => MeasureKind.Check,
        MeasureRefForm m when depth < 32 && named(m.Measure) is { } inner => KindOf(inner, named, depth + 1),
        MeasureRefForm m => m.Measure.EndsWith(".healthStart", StringComparison.Ordinal) || m.Measure.EndsWith(".healthEnd", StringComparison.Ordinal)
                            || m.Measure.EndsWith(".seconds", StringComparison.Ordinal) || m.Measure.EndsWith(".liveSeconds", StringComparison.Ordinal)
                            ? MeasureKind.Value : MeasureKind.Count,
        _ => MeasureKind.Value,
    };

    /// <summary>A kind's name in results.json.</summary>
    public static string KindName(MeasureKind k) => k switch
    {
        MeasureKind.Count => "count", MeasureKind.Rate => "rate", MeasureKind.RatePerSecond => "ratePerSecond", MeasureKind.Value => "value",
        MeasureKind.Time => "time", MeasureKind.Delay => "delay", _ => "check",
    };

    /// <summary>A kind's unit when the scenario gives none.</summary>
    public static string UnitOf(MeasureKind k) => k switch
    {
        MeasureKind.Count => "count", MeasureKind.Rate => "share", MeasureKind.RatePerSecond => "per s", MeasureKind.Time => "s",
        MeasureKind.Delay => "s", MeasureKind.Check => "share of runs held", _ => "value",
    };

    /// <summary>
    /// Whether Wilson's interval applies to a rate: counts of events, the numerator never above the denominator, and at most one trial
    /// a run (the run is the unit of independence: many blows of one run are not independent trials, so a rate pooled over them takes a
    /// bootstrap over the runs instead).
    /// </summary>
    public static bool WilsonFits(ValueForm f, IReadOnlyList<RunValue> runs, Func<string, ValueForm?> named, int depth = 0)
    {
        if (f is MeasureRefForm m && depth < 32 && named(m.Measure) is { } inner) return WilsonFits(inner, runs, named, depth + 1);
        if (f is not RateForm { PerSecond: false, OfSumField: null, PerSumField: null, Per: not null }) return false;
        foreach (var r in runs) if (r.Numerator is { } n && r.Denominator is { } d && (n > d || d > 1)) return false;
        return true;
    }

    // ---- a statistic of a set of runs ----

    /// <summary>The default statistic of a kind: a rate's (and a check's) rate, otherwise the mean.</summary>
    public static Statistic DefaultStatistic(MeasureKind k) => k is MeasureKind.Rate or MeasureKind.Check ? Statistic.Rate : Statistic.Mean;

    /// <summary>The values a statistic reads: a delay's samples pooled, otherwise each run's value (runs without one left out).</summary>
    static List<double> Values(MeasureKind kind, IEnumerable<RunValue> runs)
    {
        var xs = new List<double>();
        foreach (var r in runs)
        {
            if (kind == MeasureKind.Delay) { if (r.Samples is { } s) xs.AddRange(s); }
            else if (r.Value is { } v && !double.IsNaN(v)) xs.Add(v);
        }
        return xs;
    }

    /// <summary>A statistic over a set of runs, or null when it has nothing to say.</summary>
    public static double? Stat(Statistic s, MeasureKind kind, IEnumerable<RunValue> runs)
    {
        if (s == Statistic.Rate)
        {
            if (kind == MeasureKind.Check)
            {
                int n = 0, held = 0;
                foreach (var r in runs) if (r.Value is { } v) { n++; if (v != 0) held++; }
                return n == 0 ? null : held / (double)n;
            }
            if (kind is MeasureKind.Rate or MeasureKind.RatePerSecond)
            {
                double num = 0, den = 0;
                foreach (var r in runs) if (r.Numerator is { } a && r.Denominator is { } b) { num += a; den += b; }
                return den > 0 ? num / den : null;
            }
            s = Statistic.Mean;
        }
        var xs = Values(kind, runs);
        if (xs.Count == 0) return null;
        if (s == Statistic.Mean) return ScenarioStats.Mean(xs);
        xs.Sort();
        return s switch { Statistic.Median => ScenarioStats.Median(xs), Statistic.P10 => ScenarioStats.Quantile(xs, 0.1), _ => ScenarioStats.Quantile(xs, 0.9) };
    }

    /// <summary>A statistic's 95 % interval over the runs: Wilson's for a rate of counts and a check's share, else a bootstrap over the runs (a fixed seed). The method in words.</summary>
    public static (double[]? Interval, string Method) Interval(Statistic s, MeasureKind kind, IReadOnlyList<RunValue> runs, bool wilson)
    {
        if (s == Statistic.Rate && kind == MeasureKind.Check)
        {
            int n = 0, held = 0;
            foreach (var r in runs) if (r.Value is { } v) { n++; if (v != 0) held++; }
            return (ScenarioStats.Wilson(held, n), "wilson");
        }
        if (s == Statistic.Rate && kind == MeasureKind.Rate && wilson)
        {
            double num = 0, den = 0;
            foreach (var r in runs) if (r.Numerator is { } a && r.Denominator is { } b) { num += a; den += b; }
            return (ScenarioStats.Wilson(num, den), "wilson");
        }
        return (ScenarioStats.Bootstrap(runs.Count, idx => Stat(s, kind, idx.Select(i => runs[i]))),
            $"bootstrap over runs ({ScenarioStats.BootstrapResamples} resamples, fixed seed)");
    }

    // ---- a measure's aggregate at a point ----

    /// <summary>A measure's aggregate over a point's runs (section 4.10).</summary>
    public static MeasureAggregate Aggregate(MeasureKind kind, string? unit, IReadOnlyList<RunValue> runs, bool wilson)
    {
        unit ??= UnitOf(kind);
        string name = KindName(kind);
        var xs = Values(kind, runs);
        xs.Sort();
        double? mean = xs.Count > 0 ? ScenarioStats.Mean(xs) : null;
        double? median = xs.Count > 0 ? ScenarioStats.Median(xs) : null;
        double? p10 = xs.Count > 0 ? ScenarioStats.Quantile(xs, 0.1) : null;
        double? p90 = xs.Count > 0 ? ScenarioStats.Quantile(xs, 0.9) : null;
        double? min = xs.Count > 0 ? xs[0] : null;
        double? max = xs.Count > 0 ? xs[^1] : null;
        int censored = kind == MeasureKind.Delay ? runs.Sum(r => r.Unanswered) : runs.Count(r => r.Value is null);
        switch (kind)
        {
            case MeasureKind.Rate:
            case MeasureKind.RatePerSecond:
            {
                double num = 0, den = 0;
                int n = 0;
                foreach (var r in runs) if (r.Numerator is { } a && r.Denominator is { } b) { num += a; den += b; if (b > 0) n++; }
                var (iv, method) = kind == MeasureKind.Rate ? Interval(Statistic.Rate, kind, runs, wilson) : Interval(Statistic.Mean, kind, runs, false);
                return new MeasureAggregate(name, unit, n, runs.Count - n, Mean: mean, Median: median, P10: p10, P90: p90, Min: min, Max: max,
                    Numerator: num, Denominator: den, Value: den > 0 ? num / den : null, Interval95: iv, IntervalMethod: method);
            }
            case MeasureKind.Check:
            {
                double violations = 0, cases = 0;
                foreach (var r in runs) { violations += r.Numerator ?? 0; cases += r.Denominator ?? 0; }
                var (iv, method) = Interval(Statistic.Rate, kind, runs, true);
                return new MeasureAggregate(name, unit, runs.Count(r => r.Value is not null), censored, Numerator: violations, Denominator: cases,
                    Value: Stat(Statistic.Rate, kind, runs), Interval95: iv, IntervalMethod: method);
            }
            default:
            {
                var (iv, method) = xs.Count > 0 ? Interval(Statistic.Mean, kind, runs, false) : (null, "");
                var agg = new MeasureAggregate(name, unit, xs.Count, censored, Mean: mean, Median: median, P10: p10, P90: p90, Min: min, Max: max,
                    Value: mean, Interval95: iv, IntervalMethod: xs.Count > 0 ? method : null);
                // A time with runs censored (the event never came before the run's end): the Kaplan–Meier median and restricted mean over every run.
                if (kind == MeasureKind.Time && runs.All(r => r.FollowedSeconds is not null) && runs.Count > 0)
                {
                    static List<(double, bool)> Obs(IEnumerable<RunValue> rs) => [.. rs.Select(r => r.Value is { } v ? (v, true) : (r.FollowedSeconds!.Value, false))];
                    var obs = Obs(runs);
                    double tau = runs.Min(r => r.Value is { } v ? Math.Max(v, r.FollowedSeconds!.Value) : r.FollowedSeconds!.Value);
                    var kmIv = ScenarioStats.Bootstrap(runs.Count, idx => ScenarioStats.KaplanMeierMedian(Obs(idx.Select(i => runs[i]))));
                    agg = agg with
                    {
                        KaplanMeierMedian = ScenarioStats.KaplanMeierMedian(obs), KaplanMeierInterval95 = kmIv,
                        RestrictedMeanSeconds = ScenarioStats.RestrictedMean(obs, tau), RestrictedToSeconds = tau,
                    };
                }
                return agg;
            }
        }
    }

    /// <summary>A built-in measure's aggregate, compact: n, the runs without a value, the mean, the least and the most.</summary>
    public static MeasureAggregate Compact(MeasureKind kind, string? unit, IReadOnlyList<RunValue> runs)
    {
        var xs = Values(kind, runs);
        return new MeasureAggregate(KindName(kind), unit ?? UnitOf(kind), xs.Count, runs.Count(r => r.Value is null),
            Mean: xs.Count > 0 ? ScenarioStats.Mean(xs) : null, Min: xs.Count > 0 ? xs.Min() : null, Max: xs.Count > 0 ? xs.Max() : null);
    }

    // ---- assertions at a point ----

    /// <summary>A comparison as written (<c>atMost 40</c>, <c>between [1, 3]</c>).</summary>
    public static string Threshold(Comparison? c) => c is null ? "" : c.Op switch
    {
        CompareOp.Between => string.Create(Ic, $"between [{c.Value}, {c.High}]"),
        CompareOp.IsTrue => "isTrue",
        CompareOp.IsFalse => "isFalse",
        _ => string.Create(Ic, $"{char.ToLowerInvariant(c.Op.ToString()[0])}{c.Op.ToString()[1..]} {c.Value}"),
    };

    /// <summary>An interval against a comparison: pass when every value in it passes, fail when every value fails, else inconclusive.</summary>
    public static Verdict Judge(Comparison c, double lo, double hi)
    {
        bool? a = ScenarioTally.Holds(c, lo), b = ScenarioTally.Holds(c, hi);
        switch (c.Op)
        {
            case CompareOp.Equals:
                if (a == true && b == true) return Verdict.Pass;
                return lo > c.Value || hi < c.Value ? Verdict.Fail : Verdict.Inconclusive;
            case CompareOp.Between:
                if (a == true && b == true) return Verdict.Pass;
                return hi < c.Value || lo > c.High ? Verdict.Fail : Verdict.Inconclusive;
            case CompareOp.IsTrue:
                if (lo > 0 || hi < 0) return Verdict.Pass;
                return lo == 0 && hi == 0 ? Verdict.Fail : Verdict.Inconclusive;
            case CompareOp.IsFalse:
                if (lo == 0 && hi == 0) return Verdict.Pass;
                return lo > 0 || hi < 0 ? Verdict.Fail : Verdict.Inconclusive;
            default:   // monotone: the ends say it
                if (a == true && b == true) return Verdict.Pass;
                if (a == false && b == false) return Verdict.Fail;
                return Verdict.Inconclusive;
        }
    }

    /// <summary>One assertion (not a trend) at one point: <paramref name="values"/> its value in each run, <paramref name="seeds"/> the runs' seeds.</summary>
    public static (Verdict Verdict, AssertionPointResult Result) AtPoint(AssertionSpec a, MeasureKind kind, IReadOnlyList<RunValue> values,
        IReadOnlyList<ulong> seeds, bool wilson)
    {
        string scope = ScopeName(a.Scope);
        string threshold = Threshold(a.Compare);
        if (a.Compare is not { } cmp)
            return (Verdict.Inconclusive, new AssertionPointResult(a.Name, scope, Name(Verdict.Inconclusive), Threshold: threshold, Note: "no comparison given"));
        // A built-in check that found no case to look at in any run (blockedBodyNoWound with no
        // blocked contact at all) is not applicable here, not a vacuous pass: its count (0) is said, and it fails no run.
        if (kind == MeasureKind.Check && values.Count > 0 && values.All(x => x.Denominator is 0))
            return (Verdict.NotApplicable, new AssertionPointResult(a.Name, scope, Name(Verdict.NotApplicable), Threshold: threshold, N: 0,
                Runs: values.Count, Note: $"no applicable cases: the check found 0 cases in each of the {values.Count} runs (not applicable, never a vacuous pass)"));
        switch (a.Scope)
        {
            case AssertionScope.EachRun:
            case AssertionScope.AnyRun:
            {
                int passed = 0;
                ulong? firstFail = null;
                for (int i = 0; i < values.Count; i++)
                {
                    if (ScenarioTally.Holds(cmp, values[i].Value) == true) passed++;
                    else firstFail ??= seeds[i];
                }
                int n = values.Count;
                double share = n == 0 ? 0 : passed / (double)n;
                Verdict v = n == 0 ? Verdict.Inconclusive
                    : a.Scope == AssertionScope.AnyRun ? (passed > 0 ? Verdict.Pass : Verdict.Fail)
                    : share + 1e-12 >= a.PassShare ? Verdict.Pass : Verdict.Fail;
                return (v, new AssertionPointResult(a.Name, scope, Name(v), Measured: share, Interval95: ScenarioStats.Wilson(passed, n),
                    Statistic: "share of runs passed", Threshold: threshold, N: n, RunsPassed: passed, Runs: n,
                    PassShare: a.Scope == AssertionScope.EachRun ? a.PassShare : null, FirstFailingSeed: firstFail,
                    Note: n == 0 ? "no runs" : values.Any(x => x.Value is null) ? "a run with no value counts as failing" : null));
            }
            case AssertionScope.Aggregate:
            {
                var s = a.Statistic ?? DefaultStatistic(kind);
                double? stat = Stat(s, kind, values);
                var (iv, _) = Interval(s, kind, values, wilson);
                int n = kind == MeasureKind.Delay ? values.Sum(x => x.Samples?.Count ?? 0) : values.Count(x => x.Value is not null);
                string sname = s.ToString().ToLowerInvariant();
                if (stat is null || iv is null || values.Count(x => x.Value is not null) < 2)
                    return (Verdict.Inconclusive, new AssertionPointResult(a.Name, scope, Name(Verdict.Inconclusive), stat, iv, sname, threshold, n,
                        Note: stat is null ? "no run had a value" : "fewer than 2 runs with a value: no interval"));
                var v = Judge(cmp, iv[0], iv[1]);
                return (v, new AssertionPointResult(a.Name, scope, Name(v), stat, iv, sname, threshold, n,
                    Note: v == Verdict.Inconclusive ? "the 95 % interval straddles the threshold" : null));
            }
        }
        return (Verdict.NotApplicable, new AssertionPointResult(a.Name, scope, Name(Verdict.NotApplicable)));
    }

    public static string ScopeName(AssertionScope s) => s switch
    {
        AssertionScope.EachRun => "eachRun", AssertionScope.AnyRun => "anyRun", AssertionScope.Aggregate => "aggregate", _ => "trend",
    };

    // ---- a trend across the sweep ----

    /// <summary>
    /// A trend (section 4.11): the points grouped by their value of <c>over</c> (numbers, in order; points sharing a value pooled),
    /// each group's statistic, Spearman's correlation of value with statistic, and its 95 % interval by a bootstrap over the run
    /// numbers (paired: a draw takes run r at every point). Rising passes when the whole interval is above 0 and fails when the whole
    /// is at or below 0 (falling the mirror); flat passes when the whole is within ±<see cref="AssertionSpec.FlatWithin"/> and fails
    /// when the whole is outside it.
    /// </summary>
    /// <param name="points">Each applicable point: its value of <c>over</c> as text, and its runs' values (run r at index r).</param>
    /// <summary>The runs with a value a sweep value needs to count in a trend.</summary>
    public const int MinTrendRuns = 2;

    public static (Verdict Verdict, TrendResult Result, string? Note) Trend(AssertionSpec a, MeasureKind kind,
        IReadOnlyList<(int Index, string Value, IReadOnlyList<RunValue> Runs)> points, bool wilson)
    {
        var s = a.Statistic ?? DefaultStatistic(kind);
        string sname = s.ToString().ToLowerInvariant();
        string dir = a.Direction?.ToString().ToLowerInvariant() ?? "rising";
        var groups = points
            .Select(p => (Text: p.Value, Num: double.TryParse(p.Value, NumberStyles.Float, Ic, out double d) ? d : double.NaN, p.Index, p.Runs))
            .GroupBy(p => p.Text, StringComparer.Ordinal)
            .Select(g => (Text: g.Key, Num: g.First().Num, Points: g.Select(x => x.Index).ToList(), Runs: g.Select(x => x.Runs).ToList()))
            .ToList();
        if (groups.Any(g => double.IsNaN(g.Num)))
        {
            // Values that are not numbers: their order in the sweep is their order.
            for (int i = 0; i < groups.Count; i++) groups[i] = (groups[i].Text, i, groups[i].Points, groups[i].Runs);
        }
        groups.Sort((x, y) => x.Num.CompareTo(y.Num));
        int runCount = points.Count == 0 ? 0 : points.Max(p => p.Runs.Count);

        // A sweep value counts when at least MinTrendRuns of its runs have a value (the run is the unit of independence).
        double? GroupStat(List<IReadOnlyList<RunValue>> runsAt, int[]? draw)
        {
            IEnumerable<RunValue> Pick(IReadOnlyList<RunValue> rs) => draw is null ? rs : draw.Where(i => i < rs.Count).Select(i => rs[i]);
            var picked = runsAt.SelectMany(Pick).ToList();
            if (picked.Count(r => r.Value is not null || (r.Samples?.Count ?? 0) > 0) < MinTrendRuns) return null;
            return Stat(s, kind, picked);
        }

        double? Rho(int[]? draw)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var g in groups)
                if (GroupStat(g.Runs, draw) is { } y) { xs.Add(g.Num); ys.Add(y); }
            return xs.Count < 2 ? null : ScenarioStats.Spearman(xs, ys);
        }

        var byValue = groups.Select(g => new TrendPoint(Node(g.Text), g.Points, GroupStat(g.Runs, null),
            kind == MeasureKind.Delay ? g.Runs.Sum(rs => rs.Sum(r => r.Samples?.Count ?? 0)) : g.Runs.Sum(rs => rs.Count(r => r.Value is not null)))).ToList();
        double? rho = Rho(null);
        var iv = runCount > 0 ? ScenarioStats.Bootstrap(runCount, idx => Rho(idx)) : null;
        var result = new TrendResult(a.Over ?? "", dir, sname, rho, iv, byValue, a.Direction == TrendDirection.Flat ? a.FlatWithin : null);
        int counted = byValue.Count(b => b.Statistic is not null);
        if (rho is null || iv is null || counted < 3)
            return (Verdict.Inconclusive, result, $"{counted} sweep value(s) had a number from at least {MinTrendRuns} runs; a trend needs 3");
        double lo = iv[0], hi = iv[1], w = a.FlatWithin;
        Verdict v = a.Direction switch
        {
            TrendDirection.Falling => hi < 0 ? Verdict.Pass : lo >= 0 ? Verdict.Fail : Verdict.Inconclusive,
            TrendDirection.Flat => lo >= -w && hi <= w ? Verdict.Pass : lo > w || hi < -w ? Verdict.Fail : Verdict.Inconclusive,
            _ => lo > 0 ? Verdict.Pass : hi <= 0 ? Verdict.Fail : Verdict.Inconclusive,
        };
        return (v, result, v == Verdict.Inconclusive ? "the correlation's 95 % interval straddles the line" : null);
    }

    /// <summary>A point's value as JSON: a number when it reads as one, true/false, else text.</summary>
    public static System.Text.Json.Nodes.JsonNode? Node(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, Ic, out double d)) return System.Text.Json.Nodes.JsonValue.Create(d);
        if (bool.TryParse(text, out bool b)) return System.Text.Json.Nodes.JsonValue.Create(b);
        return System.Text.Json.Nodes.JsonValue.Create(text);
    }

    /// <summary>Whether a point's value of a path is one of a filter's (numbers by value, text without regard to case).</summary>
    public static bool InFilter(string? value, PointFilter f)
    {
        if (value is null) return false;
        foreach (var w in f.Values)
        {
            if (double.TryParse(value, NumberStyles.Float, Ic, out double a) && double.TryParse(w, NumberStyles.Float, Ic, out double b))
            { if (Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(b))) return true; }
            else if (string.Equals(value, w, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
