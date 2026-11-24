// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The modern renderer (<see cref="ModernRenderer"/>, ours) drawing a scene built here: a 20 m square of
/// ground and a 2 m block floating 4 m over it, under a sun from the south at 60°. Asserts what a
/// working sun, shadow map, sky and tone curve must show, not the exact picture, so it holds on any
/// device, the cloud's software Vulkan included.
/// </summary>
[Collection("headless")]
public class ModernFrameTests(Headless h)
{
    const int W = 320, H = 180;

    static GltfPrimitive Box(Vector3 min, Vector3 max, int material)
    {
        var v = new List<ModernVertex>(); var idx = new List<uint>();
        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            uint o = (uint)v.Count;
            foreach (var p in new[] { a, b, c, d })
                v.Add(new ModernVertex { Position = p, Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff });
            idx.AddRange([o, o + 1, o + 2, o, o + 2, o + 3]);
        }
        Vector3 P(float x, float y, float z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        Face(Vector3.UnitZ, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));
        Face(-Vector3.UnitZ, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0));
        Face(Vector3.UnitX, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1));
        Face(-Vector3.UnitX, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));
        Face(Vector3.UnitY, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0));
        Face(-Vector3.UnitY, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));
        return new GltfPrimitive { Vertices = v.ToArray(), Indices = idx.ToArray(), Material = material, Min = min, Max = max };
    }

    static GltfScene Scene()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        s.Materials.Add(new GltfMaterial { Name = "block", BaseColorFactor = new Vector4(0.6f, 0.3f, 0.2f, 1), MetallicFactor = 0, RoughnessFactor = 0.6f });
        float m = ModernMath.UnitsPerMetre;
        s.Primitives.Add(Box(new Vector3(-10 * m, -10 * m, -0.2f * m), new Vector3(10 * m, 10 * m, 0), 0));
        s.Primitives.Add(Box(new Vector3(-m, -m, 4 * m), new Vector3(m, m, 6 * m), 1));
        foreach (var p in s.Primitives) { s.Min = Vector3.Min(s.Min, p.Min); s.Max = Vector3.Max(s.Max, p.Max); }
        return s;
    }

    static FreeCamera Camera() => new() { Position = new Vector3(0, -14 * 72, 9 * 72), Yaw = 90, Pitch = -25 };

    (byte[] Pixels, Func<Vector3, (int R, int G, int B)> At) Render(ModernSettings settings)
    {
        using var scene = new ModernScene(h.Gpu!, Scene());
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var camera = Camera();
        var pixels = renderer.RenderToImage(scene, camera, W, H);
        var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
        return (pixels, p =>
        {
            var c = Vector4.Transform(new Vector4(p, 1), vp);
            int x = (int)((c.X / c.W * 0.5f + 0.5f) * W), y = (int)((0.5f - c.Y / c.W * 0.5f) * H);
            int o = (Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)) * 4;
            return (pixels[o], pixels[o + 1], pixels[o + 2]);
        });
    }

    [Fact]
    public void The_floating_block_casts_its_shadow_where_the_sun_puts_it()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // Sun from the south (−X in the Old Town's compass) at 60°: the shadow of the block (4–6 m up)
        // falls 2.3–3.5 m towards +X.
        var (_, at) = Render(new ModernSettings { SunAzimuth = 180, SunElevation = 60, HazeVisibilityMetres = 0 });
        float m = ModernMath.UnitsPerMetre;
        var shade = at(new Vector3(2.9f * m, 0, 0));
        var sun = at(new Vector3(-6f * m, 0, 0));
        int Sum((int R, int G, int B) c) => c.R + c.G + c.B;
        Assert.True(Sum(shade) < Sum(sun) * 0.6f, $"shadow {shade} against sunlit ground {sun}");
        Assert.True(Sum(shade) > 10, "the shadow is lit by the sky, not black");
    }

    [Fact]
    public void Without_shadows_that_ground_is_sunlit_like_the_rest()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var (_, at) = Render(new ModernSettings { SunAzimuth = 180, SunElevation = 60, HazeVisibilityMetres = 0, Shadows = false });
        float m = ModernMath.UnitsPerMetre;
        var a = at(new Vector3(2.9f * m, 0, 0));
        var b = at(new Vector3(-6f * m, 0, 0));
        Assert.InRange(a.R + a.G + a.B, (b.R + b.G + b.B) * 0.9f, (b.R + b.G + b.B) * 1.1f);
    }

    [Fact]
    public void The_sky_is_blue_the_sunlit_ground_is_neither_black_nor_blown_out()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var (pixels, at) = Render(new ModernSettings { SunAzimuth = 180, SunElevation = 60 });
        int o = (2 * W + W / 2) * 4;   // the top row's middle: sky above the ground
        Assert.True(pixels[o + 2] > pixels[o] + 20, $"sky ({pixels[o]}, {pixels[o + 1]}, {pixels[o + 2]})");
        var g = at(new Vector3(-6f * 72, 0, 0));
        Assert.InRange(g.R, 60, 245);
        Assert.InRange(g.G, 60, 245);
    }
}
