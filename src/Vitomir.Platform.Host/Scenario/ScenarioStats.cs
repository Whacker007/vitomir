// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The scenario runner's statistics: the order statistics, Wilson's interval for a
/// proportion, a bootstrap with a fixed seed (so the same runs give the same interval on any machine), and Spearman's rank
/// correlation for trends. Deterministic: its own generator (SplitMix64), never <see cref="Random"/>.
/// </summary>
public static class ScenarioStats
{
    /// <summary>The normal quantile for a two-sided 95 % interval.</summary>
    public const double Z95 = 1.959963984540054;

    /// <summary>The bootstrap's resamples and its seed (fixed).</summary>
    public const int BootstrapResamples = 2000;
    public const ulong BootstrapSeed = 0x5CE7A410_14C1UL;

    public static double Mean(IReadOnlyList<double> xs) { double s = 0; foreach (var x in xs) s += x; return s / xs.Count; }

    /// <summary>The q-quantile (0 to 1) by linear interpolation between order statistics (R's type 7); <paramref name="sorted"/> ascending, not empty.</summary>
    public static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 1) return sorted[0];
        double h = (sorted.Count - 1) * q;
        int lo = (int)Math.Floor(h);
        int hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
    }

    public static double Median(IReadOnlyList<double> sorted) => Quantile(sorted, 0.5);

    /// <summary>Wilson's score interval (95 %) for <paramref name="k"/> of <paramref name="n"/>; null when n is 0.</summary>
    public static double[]? Wilson(double k, double n)
    {
        if (n <= 0) return null;
        double p = k / n, z2 = Z95 * Z95;
        double centre = (p + z2 / (2 * n)) / (1 + z2 / n);
        double half = Z95 * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / (1 + z2 / n);
        return [Math.Max(0, centre - half), Math.Min(1, centre + half)];
    }

    /// <summary>SplitMix64: a small, fixed generator (the bootstrap's draws).</summary>
    public struct SplitMix(ulong seed)
    {
        ulong state = seed;
        public ulong Next()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public int Below(int n) => (int)(Next() % (ulong)n);
    }

    /// <summary>
    /// A bootstrap 95 % interval (percentile) of a statistic over <paramref name="units"/> resampled with replacement (the runs:
    /// paired across points, so a trend resamples run numbers, not values); <paramref name="statistic"/> gets the indices drawn
    /// and returns null when it has nothing to say for that draw (left out). Null when no draw gave a number.
    /// </summary>
    public static double[]? Bootstrap(int units, Func<int[], double?> statistic, int resamples = BootstrapResamples, ulong seed = BootstrapSeed)
    {
        if (units <= 0) return null;
        var rng = new SplitMix(seed);
        var draws = new List<double>(resamples);
        var idx = new int[units];
        for (int b = 0; b < resamples; b++)
        {
            for (int i = 0; i < units; i++) idx[i] = rng.Below(units);
            if (statistic(idx) is { } v && !double.IsNaN(v)) draws.Add(v);
        }
        if (draws.Count == 0) return null;
        draws.Sort();
        return [Quantile(draws, 0.025), Quantile(draws, 0.975)];
    }

    /// <summary>
    /// The Kaplan–Meier survival curve of times to an event, some runs censored (no event before their end): each distinct event time
    /// and the share still without the event just after it. <paramref name="runs"/>: (time, true when the event happened at it; false:
    /// censored then).
    /// </summary>
    public static List<(double Time, double Survival)> KaplanMeier(IReadOnlyList<(double Time, bool Event)> runs)
    {
        var curve = new List<(double, double)>();
        var sorted = runs.OrderBy(r => r.Time).ThenBy(r => r.Event ? 0 : 1).ToList();   // an event before a censoring at the same time
        double s = 1;
        int atRisk = sorted.Count;
        for (int i = 0; i < sorted.Count;)
        {
            double t = sorted[i].Time;
            int events = 0, leaving = 0;
            while (i < sorted.Count && sorted[i].Time == t) { if (sorted[i].Event) events++; leaving++; i++; }
            if (events > 0 && atRisk > 0) { s *= 1 - events / (double)atRisk; curve.Add((t, s)); }
            atRisk -= leaving;
        }
        return curve;
    }

    /// <summary>The Kaplan–Meier median: the first event time the curve reaches 0.5 or below; null when it never does.</summary>
    public static double? KaplanMeierMedian(IReadOnlyList<(double Time, bool Event)> runs)
    {
        foreach (var (t, s) in KaplanMeier(runs)) if (s <= 0.5 + 1e-12) return t;
        return null;
    }

    /// <summary>The restricted mean time without the event up to <paramref name="tau"/>: the area under the Kaplan–Meier curve from 0 to tau.</summary>
    public static double RestrictedMean(IReadOnlyList<(double Time, bool Event)> runs, double tau)
    {
        double area = 0, last = 0, s = 1;
        foreach (var (t, sv) in KaplanMeier(runs))
        {
            if (t >= tau) break;
            area += s * (t - last);
            (last, s) = (t, sv);
        }
        return area + s * Math.Max(0, tau - last);
    }

    /// <summary>The ranks of <paramref name="xs"/> (1 up), ties given their mean rank.</summary>
    public static double[] Ranks(IReadOnlyList<double> xs)
    {
        int n = xs.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => xs[i]).ThenBy(i => i).ToArray();
        var ranks = new double[n];
        for (int i = 0; i < n;)
        {
            int j = i;
            while (j + 1 < n && xs[order[j + 1]] == xs[order[i]]) j++;
            double r = (i + j) / 2.0 + 1;
            for (int k = i; k <= j; k++) ranks[order[k]] = r;
            i = j + 1;
        }
        return ranks;
    }

    /// <summary>Spearman's rank correlation of two equal-length lists (Pearson's on the ranks); 0 when either does not vary (flat), null with fewer than two pairs.</summary>
    public static double? Spearman(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        int n = xs.Count;
        if (n < 2 || ys.Count != n) return null;
        var rx = Ranks(xs);
        var ry = Ranks(ys);
        double mx = rx.Average(), my = ry.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            sxy += (rx[i] - mx) * (ry[i] - my);
            sxx += (rx[i] - mx) * (rx[i] - mx);
            syy += (ry[i] - my) * (ry[i] - my);
        }
        if (sxx <= 0 || syy <= 0) return 0;
        return sxy / Math.Sqrt(sxx * syy);
    }
}
