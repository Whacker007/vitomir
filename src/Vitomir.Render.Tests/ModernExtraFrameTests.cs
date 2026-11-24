// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Geometry drawn beside the scene** (<see cref="ModernExtra"/>, ours; how a cell level's characters
/// are drawn, <see cref="CellThingPass"/>): a 2 m block floating 4 m over a 20 m square of ground, handed
/// to the modern renderer as extra geometry rather than as part of its scene, casts the sun's shadow
/// where the sun puts it, and is shaded as the same block in the scene is — the same sun, sky, ambient
/// light and tone — to within a few levels. Built here; holds on any device.
/// </summary>
[Collection("headless")]
public unsafe class ModernExtraFrameTests(Headless h)
{
    const int W = 320, H = 180;
    static readonly Vector3 BlockMin = new(-72, -72, 4 * 72), BlockMax = new(72, 72, 6 * 72);

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

    /// <summary>The ground, and the block too when <paramref name="block"/> is set.</summary>
    static GltfScene Scene(bool block)
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        s.Materials.Add(new GltfMaterial { Name = "block", BaseColorFactor = new Vector4(0.6f, 0.3f, 0.2f, 1), MetallicFactor = 0, RoughnessFactor = 0.6f });
        float m = ModernMath.UnitsPerMetre;
        s.Primitives.Add(Box(new Vector3(-10 * m, -10 * m, -0.2f * m), new Vector3(10 * m, 10 * m, 0), 0));
        if (block) s.Primitives.Add(Box(BlockMin, BlockMax, 1));
        foreach (var p in s.Primitives) { s.Min = Vector3.Min(s.Min, p.Min); s.Max = Vector3.Max(s.Max, p.Max); }
        // Both scenes have the same extent, so the shadows' cascades are fitted alike.
        s.Min = Vector3.Min(s.Min, BlockMin); s.Max = Vector3.Max(s.Max, BlockMax);
        return s;
    }

    static byte Srgb(float linear) => (byte)Math.Round(255 * (linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f));

    /// <summary>The block as extra geometry: its own vertices, one draw, a 1×1 texture of the block's colour.</summary>
    ModernExtra Extra()
    {
        var extra = new ModernExtra(h.Gpu!);
        var box = Box(BlockMin, BlockMax, 0);
        int tex = extra.AddTexture(1, 1, [Srgb(0.6f), Srgb(0.3f), Srgb(0.2f), 255]);
        var cmd = SDL_AcquireGPUCommandBuffer(h.Gpu!.Device);
        extra.Upload(cmd, box.Vertices, box.Indices);
        var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
        SDL_WaitForGPUFences(h.Gpu.Device, true, &fence, 1);
        SDL_ReleaseGPUFence(h.Gpu.Device, fence);
        extra.Draws.Add(new ModernExtra.Draw(0, (uint)box.Indices.Length, BlockMin, BlockMax, tex, -1f, false, 0.6f, 0f, 1f));
        return extra;
    }

    static FreeCamera Camera() => new() { Position = new Vector3(0, -14 * 72, 9 * 72), Yaw = 90, Pitch = -25 };

    Func<Vector3, (int R, int G, int B)> Render(bool extra)
    {
        var settings = new ModernSettings { SunAzimuth = 180, SunElevation = 60, HazeVisibilityMetres = 0, AmbientOcclusion = false };
        using var scene = new ModernScene(h.Gpu!, Scene(block: !extra));
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        using var block = extra ? Extra() : null;
        renderer.Extra = block;
        var camera = Camera();
        var pixels = renderer.RenderToImage(scene, camera, W, H);
        var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
        return p =>
        {
            var c = Vector4.Transform(new Vector4(p, 1), vp);
            int x = (int)((c.X / c.W * 0.5f + 0.5f) * W), y = (int)((0.5f - c.Y / c.W * 0.5f) * H);
            int o = (Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)) * 4;
            return (pixels[o], pixels[o + 1], pixels[o + 2]);
        };
    }

    static int Sum((int R, int G, int B) c) => c.R + c.G + c.B;

    [Fact]
    public void An_extra_block_casts_the_sun_s_shadow_where_the_sun_puts_it()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // As ModernFrameTests' block in the scene: the shadow of a block 4–6 m up under a sun from the
        // south at 60° falls 2.3–3.5 m towards +X.
        var at = Render(extra: true);
        float m = ModernMath.UnitsPerMetre;
        var shade = at(new Vector3(2.9f * m, 0, 0));
        var sun = at(new Vector3(-6f * m, 0, 0));
        Assert.True(Sum(shade) < Sum(sun) * 0.6f, $"shadow {shade} against sunlit ground {sun}");
        Assert.True(Sum(shade) > 10, "the shadow is lit by the sky, not black");
    }

    [Fact]
    public void An_extra_block_is_shaded_as_the_same_block_in_the_scene()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var inScene = Render(extra: false);
        var asExtra = Render(extra: true);
        // Its sunlit top, and its south face (towards the sun) high and low.
        foreach (var p in new[] { new Vector3(0, 0, 6 * 72), new Vector3(0, -72, 5.5f * 72), new Vector3(0, -72, 4.3f * 72) })
        {
            var a = inScene(p); var b = asExtra(p);
            Assert.True(Sum(a) > 30, $"the scene's block is not drawn at {p}: {a}");
            Assert.InRange(b.R, a.R - 6, a.R + 6);
            Assert.InRange(b.G, a.G - 6, a.G + 6);
            Assert.InRange(b.B, a.B - 6, a.B + 6);
        }
    }
}
