// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests;

public sealed class DetMathTests
{
    static IEnumerable<float> Angles()
    {
        for (int i = -4000; i <= 4000; i++) yield return i * 0.00731f;
        yield return 0f; yield return DetMath.HalfPi; yield return DetMath.Pi; yield return -DetMath.Pi; yield return 1e4f;
    }

    [Fact]
    public void Its_sines_and_cosines_are_as_accurate_as_single_precision_allows()
    {
        foreach (var a in Angles())
        {
            Assert.InRange(DetMath.Sin(a) - Math.Sin(a), -2e-7, 2e-7);
            Assert.InRange(DetMath.Cos(a) - Math.Cos(a), -2e-7, 2e-7);
        }
    }

    [Fact]
    public void Its_arc_tangents_exponentials_and_logarithms_are_as_accurate_as_single_precision_allows()
    {
        for (int i = -500; i <= 500; i++)
        {
            float y = i * 0.0137f, x = 3.1f - i * 0.009f;
            Assert.InRange(DetMath.Atan2(y, x) - Math.Atan2(y, x), -3e-7, 3e-7);
            float v = i * 0.03f;
            Assert.InRange(DetMath.Exp(v) / Math.Exp(v) - 1, -2e-7, 2e-7);
            if (i > 0) Assert.InRange(DetMath.Log(v) - Math.Log(v), -3e-7, 3e-7);
        }
        Assert.Equal(0f, DetMath.Atan2(0, 0));
        Assert.Equal(DetMath.HalfPi, DetMath.Atan2(1, 0), 6);
        Assert.Equal(DetMath.Pi, DetMath.Atan2(0, -1), 6);
        Assert.Equal(DetMath.Pi / 3, DetMath.Acos(0.5f), 6);
    }

    /// <summary>
    /// The same bits on every machine: a hash of thousands of results, fixed when the function was written
    /// (in the cloud, Linux). Windows must give the same, or a replay would drift between them.
    /// </summary>
    [Fact]
    public void It_gives_the_same_bits_everywhere()
    {
        var h = new Fnv1a64();
        foreach (var a in Angles())
        {
            h.Add(DetMath.Sin(a)); h.Add(DetMath.Cos(a)); h.Add(DetMath.Atan2(a, 1.7f)); h.Add(DetMath.Exp(a * 0.01f));
            h.Add(DetMath.Log(MathF.Abs(a) + 0.5f)); h.Add(DetMath.Acos(MathF.Max(-1, MathF.Min(1, a * 0.03f))));
        }
        Assert.Equal(GoldenBits, h.Value);
    }

    internal const ulong GoldenBits = 5303617715884866762UL;   // fixed in the cloud (Linux x64)

    [Fact]
    public void A_slerp_halfway_between_two_turns_is_the_turn_halfway()
    {
        var a = DetMath.Yaw(0); var b = DetMath.Yaw(1.2f);
        var m = DetMath.Slerp(a, b, 0.5f);
        var want = DetMath.Yaw(0.6f);
        Assert.InRange(MathF.Abs(m.Z - want.Z) + MathF.Abs(m.W - want.W), 0, 1e-6f);
        var dir = DetMath.Direction(DetMath.HalfPi, 0);
        Assert.True(Vector3.Distance(dir, Vector3.UnitY) < 1e-6f);
    }
}
