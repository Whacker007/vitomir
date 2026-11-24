// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The rules package's random source (its <c>IDeterministicRandom</c>: <c>NextD10(drawPurpose)</c>,
/// <c>NextUnitInterval(drawPurpose)</c>), with each purpose's position so a trace can say which draw each die was.
/// </summary>
public interface IDeterministicRandom
{
    /// <summary>A ten-sided die, 1 to 10, from the stream of <paramref name="purpose"/>.</summary>
    int NextD10(string purpose);

    /// <summary>A number in [0, 1) from the stream of <paramref name="purpose"/>.</summary>
    double NextUnitInterval(string purpose);

    /// <summary>How many numbers the stream of <paramref name="purpose"/> has given so far (the next draw's position).</summary>
    long Position(string purpose);
}

/// <summary>
/// The platform's random streams as the rules' source: <b>one stream per purpose</b>, each keyed by the world's
/// seed and the purpose's name (<see cref="RandomStreams"/>), so a draw added for one purpose never shifts another's.
/// A d10 is <see cref="Rng.NextD10"/>; a unit draw takes the top 53 bits of one number.
/// </summary>
public sealed class WorldRandom(RandomStreams streams) : IDeterministicRandom
{
    public int NextD10(string purpose) => streams.For(purpose).NextD10();

    public double NextUnitInterval(string purpose) => (streams.For(purpose).NextULong() >> 11) * (1.0 / (1UL << 53));

    public long Position(string purpose) => streams.For(purpose).Draws;
}

/// <summary>One number drawn for the staged harm: its purpose (the stream), its position in the stream before it was drawn, and what it was (a d10, or a unit draw).</summary>
public readonly record struct Draw(string Purpose, long Position, int D10, double Unit, bool IsD10);

/// <summary>
/// The dice (ours, after the combat rules' prototype; SANDBOX: gate-11 is open): a fractional pool is made whole by
/// one unit draw, and a pool is rolled as d10s against a difficulty. Every number drawn is written down
/// (<see cref="Draw"/>) for the trace.
/// </summary>
public static class Dice
{
    /// <summary>
    /// A whole pool from a fractional one (the combat rules: "stochastic neighboring-integer pool selection (weighted by the
    /// fractional part)"): the whole number below <paramref name="dice"/>, or the one above with the chance of the
    /// fraction — one unit draw from <paramref name="purpose"/>, drawn only when there is a fraction. 0 for none or less.
    /// </summary>
    public static int Pool(double dice, IDeterministicRandom random, string purpose, List<Draw> draws)
    {
        if (!(dice > 0)) return 0;
        double whole = Math.Floor(dice), fraction = dice - whole;
        int n = (int)Math.Min(whole, int.MaxValue - 1);
        if (fraction <= 0) return n;
        long at = random.Position(purpose);
        double u = random.NextUnitInterval(purpose);
        draws.Add(new Draw(purpose, at, 0, u, false));
        return u < fraction ? n + 1 : n;
    }

    /// <summary>
    /// A pool of d10s rolled against <paramref name="difficulty"/> (the combat rules' prototype): each die +1 at or above the
    /// difficulty, −1 on a 1, else 0; the net is the sum but never below 0. Any size of pool (no cap). Each die is
    /// written down with its stream's position.
    /// </summary>
    public static int Successes(int pool, int difficulty, IDeterministicRandom random, string purpose, List<Draw> draws)
    {
        int sum = 0;
        for (int i = 0; i < pool; i++)
        {
            long at = random.Position(purpose);
            int d = random.NextD10(purpose);
            draws.Add(new Draw(purpose, at, d, 0, true));
            if (d >= difficulty) sum++;
            else if (d == 1) sum--;
        }
        return Math.Max(0, sum);
    }
}
