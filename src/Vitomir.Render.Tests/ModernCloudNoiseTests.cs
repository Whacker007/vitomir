// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The clouds' shape and detail noise (ours; <see cref="ModernCloudNoise"/>): built afresh it is the
/// same bytes every time (so the clouds are the same on every run and machine), it tiles without a seam, and
/// each channel spans the whole range the shapes are carved from.
/// </summary>
public class ModernCloudNoiseTests
{
    [Fact]
    public void The_noise_is_the_same_bytes_every_time_it_is_built()
    {
        Assert.Equal(ModernCloudNoise.BuildShape(), ModernCloudNoise.Shape);
        Assert.Equal(ModernCloudNoise.BuildDetail(), ModernCloudNoise.Detail);
        Assert.Equal(ModernCloudNoise.ShapeSize * ModernCloudNoise.ShapeSize * ModernCloudNoise.ShapeSize * 4, ModernCloudNoise.Shape.Length);
    }

    [Fact]
    public void The_noise_tiles_without_a_seam()
    {
        var tex = ModernCloudNoise.Shape;
        int n = ModernCloudNoise.ShapeSize;
        // Across the wrap the neighbouring texels differ no more than neighbours inside do.
        double across = 0, inside = 0;
        for (int z = 0; z < n; z += 3)
            for (int y = 0; y < n; y += 3)
            {
                int Row(int x) => ((z * n + y) * n + x) * 4;
                across += Math.Abs(tex[Row(n - 1)] - tex[Row(0)]);
                inside += Math.Abs(tex[Row(n / 2)] - tex[Row(n / 2 + 1)]);
            }
        Assert.InRange(across, 0, inside * 1.6 + 1);
        var a = ModernCloudNoise.Sample(tex, n, new Vector3(0.1f, 0.2f, 0.3f));
        var b = ModernCloudNoise.Sample(tex, n, new Vector3(1.1f, -0.8f, 2.3f));
        Assert.True(Vector4.Distance(a, b) < 1e-4f);
    }

    [Fact]
    public void Each_channel_spans_the_whole_range()
    {
        foreach (var (tex, channels) in new[] { (ModernCloudNoise.Shape, 4), (ModernCloudNoise.Detail, 3) })
            for (int c = 0; c < channels; c++)
            {
                int lo = 255, hi = 0; double sum = 0; int count = 0;
                for (int i = c; i < tex.Length; i += 4) { lo = Math.Min(lo, tex[i]); hi = Math.Max(hi, tex[i]); sum += tex[i]; count++; }
                Assert.Equal(0, lo);
                Assert.Equal(255, hi);
                Assert.InRange(sum / count, 70, 185);
            }
    }
}
