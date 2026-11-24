// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The quality work on the GPU (ours): the clouds drawn at half resolution with dithered steps
/// (<see cref="ModernCloudSettings.HalfResolution"/>, ModernCloudPass.hlsl) give the sky the same light as
/// the per-pixel march and change less from one row of pixels to the next (the march's bands, the
/// "streaks", gone); one renderer drawing at one size and then another — a window resized — re-creates its
/// half-resolution and temporal targets and draws what a fresh renderer draws at the new size; and every
/// preset draws. They run on any device SDL can reach, the cloud's software Vulkan included.
/// </summary>
[Collection("headless")]
public class ModernQualityFrameTests(Headless h)
{
    const float M = ModernMath.UnitsPerMetre;

    static GltfScene Ground()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        Vector3 min = new(-20 * M, -20 * M, 0), max = new(20 * M, 20 * M, 0), n = Vector3.UnitZ;
        var v = new[]
        {
            new ModernVertex { Position = new Vector3(min.X, min.Y, 0), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(max.X, min.Y, 0), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(max.X, max.Y, 0), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(min.X, max.Y, 0), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
        };
        s.Primitives.Add(new GltfPrimitive { Vertices = v, Indices = [0, 1, 2, 0, 2, 3], Material = 0, Min = min, Max = max });
        s.Min = min; s.Max = max;
        return s;
    }

    static ModernSettings Morning(bool half)
    {
        var s = new ModernSettings();
        ModernLooks.Apply(s, "morning");
        s.Clouds.Cover = 0.6f;
        s.Clouds.HalfResolution = half;
        s.Bloom = false; s.LensFlare = false; s.Volumetric = false; s.AmbientOcclusion = false; s.SkyOcclusion = false; s.Fxaa = false;
        return s;
    }

    /// <summary>Looking along the ground towards the horizon, where the clouds are seen side on.</summary>
    static FreeCamera Horizon() => new() { Position = new Vector3(0, 0, 2 * M), Yaw = 30, Pitch = 12 };

    static byte[] Draw(Headless h, ModernSettings settings, FreeCamera camera, int w, int hgt)
    {
        using var scene = new ModernScene(h.Gpu!, Ground());
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        return renderer.RenderToImage(scene, camera, w, hgt);
    }

    static float Luma(byte[] p, int i) => p[i] * 0.2126f + p[i + 1] * 0.7152f + p[i + 2] * 0.0722f;

    /// <summary>The upper half of the picture (sky): its mean colour, and the mean change in brightness from a row to the next.</summary>
    static (Vector3 Mean, float RowToRow) Sky(byte[] p, int w, int hgt)
    {
        var sum = Vector3.Zero; float rows = 0; int n = 0, m = 0;
        for (int y = 2; y < hgt / 2; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                sum += new Vector3(p[i], p[i + 1], p[i + 2]); n++;
                rows += MathF.Abs(Luma(p, i) - Luma(p, i - w * 4)); m++;
            }
        return (sum / n, rows / m);
    }

    [Fact]
    public void The_half_resolution_clouds_light_the_sky_as_the_march_does_with_less_change_from_row_to_row()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        const int W = 480, H = 270;
        var full = Sky(Draw(h, Morning(half: false), Horizon(), W, H), W, H);
        var half = Sky(Draw(h, Morning(half: true), Horizon(), W, H), W, H);
        Assert.True(Vector3.Distance(full.Mean, half.Mean) < 6f, $"the sky's mean: half {half.Mean} against full {full.Mean}");
        Assert.True(full.RowToRow > 0.5f, $"there are clouds to see: {full.RowToRow}");
        Assert.True(half.RowToRow < full.RowToRow * 0.9f, $"row to row: half {half.RowToRow:0.00} against full {full.RowToRow:0.00}");
    }

    [Fact]
    public void A_renderer_drawing_at_a_new_size_draws_what_a_fresh_one_draws_there()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var settings = Morning(half: true);
        settings.Taa = true; settings.TaaStillFrames = 4;
        settings.AmbientOcclusion = true; settings.SkyOcclusion = true;   // its targets follow the size too
        using var scene = new ModernScene(h.Gpu!, Ground());
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        renderer.RenderToImage(scene, Horizon(), 320, 180);
        var resized = renderer.RenderToImage(scene, Horizon(), 400, 226);   // an odd half-size, for the half-resolution targets
        var fresh = Draw(h, settings, Horizon(), 400, 226);
        var a = Sky(resized, 400, 226); var b = Sky(fresh, 400, 226);
        Assert.True(Vector3.Distance(a.Mean, b.Mean) < 3f, $"resized {a.Mean} against fresh {b.Mean}");
        double ground = 0, groundFresh = 0;
        for (int i = 400 * 4 * 200; i < resized.Length; i += 4) { ground += Luma(resized, i); groundFresh += Luma(fresh, i); }
        Assert.True(Math.Abs(ground - groundFresh) / Math.Max(groundFresh, 1) < 0.03, $"the ground: resized {ground:0} against fresh {groundFresh:0}");
    }

    [Fact]
    public void Every_preset_draws_sky_and_ground()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        foreach (var q in Enum.GetValues<ModernQuality>())
        {
            var s = Morning(half: true);
            ModernQualityPreset.For(q).Apply(s);
            s.TaaStillFrames = 2;
            var p = Draw(h, s, Horizon(), 160, 90);
            var sky = Sky(p, 160, 90).Mean;
            int g = (80 * 160 + 80) * 4;
            Assert.True(sky.Z > sky.X && sky.Z > 60f, $"{q}: the sky {sky}");
            Assert.True(Luma(p, g) is > 20f and < 250f, $"{q}: the ground {Luma(p, g)}");
        }
    }
}
