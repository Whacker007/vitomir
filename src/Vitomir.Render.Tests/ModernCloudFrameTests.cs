// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The cumulus as the GPU draws them (ours; ModernClouds.hlsli): a 20 m square of ground under the
/// morning look's sun, at a moment the CPU twin (<see cref="ModernClouds"/>) says is clear over it and at
/// one it says is under a cloud. Under clear sky the clouds change nothing on the ground; under a cloud
/// the sunlit ground goes dark; a sky full of cloud is whiter than a clear one. Their shaders compile and
/// run on any device, the cloud's software Vulkan included.
/// </summary>
[Collection("headless")]
public class ModernCloudFrameTests(Headless h)
{
    const int W = 320, H = 180;
    const float M = ModernMath.UnitsPerMetre;

    static GltfScene Ground()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        Vector3 min = new(-10 * M, -10 * M, 0), max = new(10 * M, 10 * M, 0), n = Vector3.UnitZ;
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

    static ModernSettings Morning(float cover, float clock)
    {
        var s = new ModernSettings();
        ModernLooks.Apply(s, "morning");
        s.Clouds.Cover = cover; s.Clouds.ClockOffset = clock;
        s.Bloom = false; s.LensFlare = false; s.Volumetric = false; s.AmbientOcclusion = false; s.SkyOcclusion = false;
        return s;
    }

    /// <summary>The picture's mean channels round world point <paramref name="p"/> (or the centre of the screen).</summary>
    Func<Vector3?, Vector3> Render(ModernSettings settings, FreeCamera camera)
    {
        using var scene = new ModernScene(h.Gpu!, Ground());
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var pixels = renderer.RenderToImage(scene, camera, W, H);
        var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
        return p =>
        {
            int x = W / 2, y = H / 2;
            if (p is { } world)
            {
                var c = Vector4.Transform(new Vector4(world, 1), vp);
                x = (int)((c.X / c.W * 0.5f + 0.5f) * W); y = (int)((0.5f - c.Y / c.W * 0.5f) * H);
            }
            var sum = Vector3.Zero;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int o = (Math.Clamp(y + dy, 0, H - 1) * W + Math.Clamp(x + dx, 0, W - 1)) * 4;
                    sum += new Vector3(pixels[o], pixels[o + 1], pixels[o + 2]);
                }
            return sum / 9f;
        };
    }

    /// <summary>A moment (the clouds' clock offset) when the CPU twin says the sun at the ground's middle is as <paramref name="wanted"/> says.</summary>
    static float MomentWhen(Func<float, bool> wanted)
    {
        var s = Morning(0.35f, 0f);
        for (float t = 0; t < 20000f; t += 10f)
        {
            s.Clouds.ClockOffset = t;
            if (wanted(ModernClouds.SunThrough(s.Clouds, s.NorthYaw, Vector3.Zero, s.ToSun, 0f))) return t;
        }
        throw new InvalidOperationException("no such moment");
    }

    [Fact]
    public void Under_clear_sky_the_clouds_change_nothing_on_the_ground_and_under_a_cloud_it_goes_dark()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var camera = new FreeCamera { Position = new Vector3(0, -9 * M, 6 * M), Yaw = 90, Pitch = -30 };
        float clearAt = MomentWhen(v => v >= 1f), darkAt = MomentWhen(v => v < 0.03f);
        // Around the middle, where the CPU looked; the shadows are hundreds of metres across, so the square is all in or out.
        var points = new[] { Vector3.Zero, new Vector3(-3 * M, 2 * M, 0), new Vector3(3 * M, -2 * M, 0) };
        var none = Render(Morning(0f, clearAt), camera);
        var clear = Render(Morning(0.35f, clearAt), camera);
        var dark = Render(Morning(0.35f, darkAt), camera);
        foreach (var p in points)
        {
            float lit = none(p).X + none(p).Y + none(p).Z;
            // The ambient light takes the clouds as a share of the sky, so allow a few levels — and,
            // a little more: the morning's physically based clear sky is darker than the procedural one was, so
            // the sunlit clouds over a third of it lift the shade's light by more (as cumulus do).
            Assert.InRange(clear(p).X + clear(p).Y + clear(p).Z, lit - 24f, lit * 1.12f + 24f);
            // Lit by the sky alone: darker and bluer (through AgX, whose curve compresses the difference).
            Assert.True(dark(p).X + dark(p).Y + dark(p).Z < lit * 0.85f, $"under a cloud {dark(p)} against sunlit {none(p)}");
            Assert.True(dark(p).Z - dark(p).X > none(p).Z - none(p).X + 10f, $"under a cloud {dark(p)} against sunlit {none(p)}");
        }
    }

    [Fact]
    public void The_same_moment_draws_the_same_clouds_and_a_later_one_moves_them()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // the shapes (Schneider's noise, built on the CPU): the half-resolution pass's dither depends on
        // the pixel only, so the same time is the same picture, bit for bit.
        var up = new FreeCamera { Position = new Vector3(0, 0, 2 * M), Yaw = 30, Pitch = 35 };
        byte[] Sky(float clock)
        {
            using var scene = new ModernScene(h.Gpu!, Ground());
            using var renderer = new ModernRenderer(h.Gpu!, Morning(0.45f, clock), h.Gpu!.ColorFormat);
            return renderer.RenderToImage(scene, up, W, H);
        }
        var a = Sky(500f);
        var b = Sky(500f);
        var later = Sky(560f);
        Assert.Equal(a, b);
        int changed = 0;
        for (int i = 0; i < a.Length; i += 4) if (Math.Abs(a[i] - later[i]) > 6) changed++;
        Assert.True(changed > W * H / 50, $"a minute later the clouds have drifted: {changed} pixels changed");
    }

    [Fact]
    public void A_sky_full_of_cloud_is_whiter_than_a_clear_one()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var up = new FreeCamera { Position = new Vector3(0, 0, 2 * M), Yaw = 0, Pitch = 60 };
        var clear = Render(Morning(0f, 0f), up)(null);
        var overcast = Render(Morning(1f, 0f), up)(null);
        Assert.True(clear.Z > clear.X + 30f, $"a clear sky is blue: {clear}");
        Assert.True(overcast.Z - overcast.X < (clear.Z - clear.X) * 0.75f, $"a clouded sky is greyer: {overcast} against {clear}");
    }
}
