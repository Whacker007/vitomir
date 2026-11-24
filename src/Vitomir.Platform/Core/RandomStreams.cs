// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Core;

/// <summary>
/// One stream of random numbers: <b>xoshiro256**</b> (Blackman and Vigna, 2018), seeded through
/// SplitMix64. Exact integer arithmetic, so a stream gives the same numbers on every machine.
/// </summary>
public sealed class Rng
{
    ulong s0, s1, s2, s3;

    public Rng(ulong seed)
    {
        ulong x = seed;
        s0 = SplitMix(ref x); s1 = SplitMix(ref x); s2 = SplitMix(ref x); s3 = SplitMix(ref x);
    }

    static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>How many numbers this stream has given (for traces).</summary>
    public long Draws { get; private set; }

    public ulong NextULong()
    {
        Draws++;
        ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        ulong t = s1 << 17;
        s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3;
        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 45);
        return result;
    }

    /// <summary>A whole number from 0 up to but not including <paramref name="bound"/>, without bias (Lemire's method).</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        ulong b = (ulong)bound;
        while (true)
        {
            ulong x = NextULong();
            UInt128 m = (UInt128)x * b;
            ulong low = (ulong)m;
            if (low >= b || low >= unchecked(0UL - b) % b) return (int)(ulong)(m >> 64);
        }
    }

    /// <summary>A number in [0, 1) with 24 random bits: exact in single precision.</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

    /// <summary>A number in [<paramref name="lo"/>, <paramref name="hi"/>).</summary>
    public float NextFloat(float lo, float hi) => lo + (hi - lo) * NextFloat();

    /// <summary>A ten-sided die, 1 to 10 (the rules package's d10).</summary>
    public int NextD10() => NextInt(10) + 1;
}

/// <summary>
/// The world's random streams, <b>one per purpose</b>: a stream is keyed by the
/// world's seed and its purpose's name, so a draw added for one purpose never shifts another's. Streams
/// are made the first time a purpose asks and live as long as the world.
/// </summary>
public sealed class RandomStreams
{
    readonly Dictionary<string, Rng> streams = new(StringComparer.Ordinal);

    public RandomStreams(ulong worldSeed) { WorldSeed = worldSeed; }

    public ulong WorldSeed { get; }

    public Rng For(string purpose)
    {
        if (!streams.TryGetValue(purpose, out var rng))
            streams[purpose] = rng = new Rng(WorldSeed ^ Fnv1a64.Of(purpose));
        return rng;
    }
}
