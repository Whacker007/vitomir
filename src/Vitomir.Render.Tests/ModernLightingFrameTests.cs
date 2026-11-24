// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The modern renderer's lighting and effects on a scene built here (ours): a 20 m square of ground with
/// a 3 m cube standing on it. A torch at night lights the ground round it and not beyond its reach, nor
/// (with shadows) behind the cube; temporal anti-aliasing converges on the one-frame picture;
/// ambient occlusion darkens the ground at the cube's foot and nowhere in the open; every look draws a
/// picture that is neither black nor white. Asserts what must hold on any device, the cloud's software
/// Vulkan included.
/// </summary>
[Collection("headless")]
public class ModernLightingFrameTests(Headless h)
{
    const int W = 320, H = 180;
    const float M = ModernMath.UnitsPerMetre;

    static GltfPrimitive Quad(Vector3 min, Vector3 max, Vector3 n, int material)
    {
        // A flat quad in z = min.Z (the ground) or a box's face, as two triangles.
        var v = new[]
        {
            new ModernVertex { Position = new Vector3(min.X, min.Y, min.Z), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(max.X, min.Y, min.Z), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(max.X, max.Y, min.Z), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
            new ModernVertex { Position = new Vector3(min.X, max.Y, min.Z), Normal = n, Tangent = new Vector4(1, 0, 0, 1), Color = 0xffffffff },
        };
        return new GltfPrimitive { Vertices = v, Indices = [0, 1, 2, 0, 2, 3], Material = material, Min = min, Max = new Vector3(max.X, max.Y, min.Z) };
    }

    static GltfPrimitive Cube(Vector3 min, Vector3 max, int material)
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
        s.Primitives.Add(Quad(new Vector3(-10 * M, -10 * M, 0), new Vector3(10 * M, 10 * M, 0), Vector3.UnitZ, 0));
        s.Primitives.Add(Cube(new Vector3(-1.5f * M, 2 * M, 0), new Vector3(1.5f * M, 5 * M, 3 * M), 0));
        foreach (var p in s.Primitives) { s.Min = Vector3.Min(s.Min, p.Min); s.Max = Vector3.Max(s.Max, p.Max); }
        return s;
    }

    static FreeCamera Camera() => new() { Position = new Vector3(0, -9 * M, 6 * M), Yaw = 90, Pitch = -30 };

    Func<Vector3, float> Render(ModernSettings settings, params ModernPointLight[] lights) => Render(settings, Camera(), lights);

    Func<Vector3, float> Render(ModernSettings settings, FreeCamera camera, params ModernPointLight[] lights) => Render(settings, camera, W, H, lights);

    /// <summary>The picture at a size of the caller's choosing, as the brightness (0 to 255) round a point of the world.</summary>
    Func<Vector3, float> Render(ModernSettings settings, FreeCamera camera, int w, int h2, params ModernPointLight[] lights)
    {
        using var scene = new ModernScene(h.Gpu!, Scene());
        scene.Lights.AddRange(lights);
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var pixels = renderer.RenderToImage(scene, camera, w, h2);
        var vp = ModernRenderer.ViewProjection(camera, (float)w / h2);
        return p =>
        {
            var c = Vector4.Transform(new Vector4(p, 1), vp);
            int x = (int)((c.X / c.W * 0.5f + 0.5f) * w), y = (int)((0.5f - c.Y / c.W * 0.5f) * h2);
            float sum = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int o = (Math.Clamp(y + dy, 0, h2 - 1) * w + Math.Clamp(x + dx, 0, w - 1)) * 4;
                    sum += pixels[o] + pixels[o + 1] + pixels[o + 2];
                }
            return sum / 27f;
        };
    }

    static ModernSettings Night() => new()
    {
        SunElevation = 40, SunIntensity = 0.02f, HazeVisibilityMetres = 0, Bloom = false, LensFlare = false, Exposure = 4f,
    };

    [Fact]
    public void A_torch_at_night_lights_the_ground_round_it_and_not_beyond_its_reach()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var torch = new ModernPointLight(new Vector3(-5 * M, -2 * M, 1.5f * M), new Vector3(3f, 1.6f, 0.6f), 6 * M);
        var lit = Render(Night(), torch);
        var dark = Render(Night());
        var under = new Vector3(-5 * M, -2 * M, 0);
        var beyond = new Vector3(6 * M, -3 * M, 0);   // 11 m away, past its 6 m reach
        Assert.True(lit(under) > dark(under) + 40, $"under the torch {lit(under)} against {dark(under)} without it");
        Assert.InRange(lit(beyond) - dark(beyond), -3f, 3f);
    }

    [Fact]
    public void A_shadowed_torch_does_not_light_the_ground_behind_the_cube_and_an_unshadowed_one_does()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // The torch just in front of the cube's near face, low down; the ground just behind the cube.
        var torch = new ModernPointLight(new Vector3(0, 1.4f * M, 1f * M), new Vector3(12f, 6.4f, 2.4f), 8 * M);
        var shadowed = Night(); shadowed.LightShadows = 4;
        var open = Night(); open.LightShadows = 0;
        var behind = new Vector3(0, 5.8f * M, 0);
        var front = new Vector3(0, 0.5f * M, 0);
        // Seen from the side, so the ground behind the cube is in view.
        var side = new FreeCamera { Position = new Vector3(-9 * M, 3.5f * M, 9 * M), Yaw = 0, Pitch = -40 };
        var withShadows = Render(shadowed, side, torch);
        var without = Render(open, side, torch);
        var dark = Render(Night(), side);
        Assert.True(without(behind) > dark(behind) + 10, $"unshadowed, behind the cube {without(behind)} against {dark(behind)} with no torch");
        Assert.InRange(withShadows(behind) - dark(behind), -3f, 3f);
        Assert.InRange(withShadows(front), without(front) - 6, without(front) + 6);
    }

    [Fact]
    public void Ambient_occlusion_darkens_the_ground_at_the_cubes_foot_and_leaves_the_open_ground_alone()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // No sun: the ambient light alone, from an even overcast.
        ModernSettings Overcast(bool ao) => new()
        {
            SunElevation = 40, SunIntensity = 0f, SkyHorizon = new Vector3(0.8f), SkyZenith = new Vector3(0.8f), SunDisc = 0,
            HazeVisibilityMetres = 0, Bloom = false, LensFlare = false, AmbientOcclusion = ao,
        };
        var with = Render(Overcast(true));
        var without = Render(Overcast(false));
        var foot = new Vector3(0, 1.8f * M, 0);          // 20 cm in front of the cube's face
        var open = new Vector3(-7 * M, -6 * M, 0);
        Assert.True(with(foot) < without(foot) - 8, $"at the foot {with(foot)} with occlusion, {without(foot)} without");
        Assert.InRange(with(open), without(open) - 8, without(open) + 2);
    }

    /// <summary>
    /// A flat floor does not shade itself, at any picture size (ours). Ambient occlusion works at half the
    /// picture's size and reads the full-size depth buffer; a depth read from the nearest texel and placed
    /// on a ray that does not pass through that texel's centre is off its own surface by up to half a
    /// texel of the surface's slope, and at a low angle that lifts a floor above its own horizon. At an
    /// even size every half-size pixel sits on the corner four texels share, and which one answers is the
    /// GPU's rounding: Mesa's software Vulkan took the harmless one, an NVIDIA card the other, and its open
    /// ground came out 13 darker. At an odd size the mismatch is the same on every device, which is how
    /// this pins it: the far ground, seen low, is shaded alike at 320 by 180 and at 321 by 181.
    /// </summary>
    [Fact]
    public void Ambient_occlusion_shades_far_open_ground_alike_at_an_even_and_an_odd_picture_size()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        ModernSettings Overcast(bool ao) => new()
        {
            SunElevation = 40, SunIntensity = 0f, SkyHorizon = new Vector3(0.8f), SkyZenith = new Vector3(0.8f), SunDisc = 0,
            HazeVisibilityMetres = 0, Bloom = false, LensFlare = false, AmbientOcclusion = ao,
        };
        // Off to the cube's side and looking past it down the ground, so the far ground is seen at a low angle.
        var along = new FreeCamera { Position = new Vector3(-6 * M, -6 * M, 4 * M), Yaw = 90, Pitch = -50 };
        var evenWith = Render(Overcast(true), along, 320, 180);
        var evenWithout = Render(Overcast(false), along, 320, 180);
        var oddWith = Render(Overcast(true), along, 321, 181);
        var oddWithout = Render(Overcast(false), along, 321, 181);
        foreach (float y in new[] { 1f, 3f, 5f })   // 7, 9 and 11 m from the camera
        {
            var far = new Vector3(-6 * M, y * M, 0);
            float even = evenWith(far) - evenWithout(far), odd = oddWith(far) - oddWithout(far);
            Assert.True(MathF.Abs(odd - even) <= 1.5f, $"{y + 6} m off: occlusion takes {-even:F1} at 320 by 180 and {-odd:F1} at 321 by 181");
            Assert.True(odd >= -6f, $"{y + 6} m off: open ground {-odd:F1} darker with occlusion at 321 by 181");
        }
    }

    [Fact]
    public void Temporal_anti_aliasing_converges_on_the_same_picture_as_one_frame()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        ModernSettings Day(bool taa) => new() { SunAzimuth = 180, SunElevation = 50, Bloom = false, LensFlare = false, Taa = taa };
        var single = Render(Day(false));
        var temporal = Render(Day(true));
        foreach (var p in new[] { new Vector3(-6 * M, -4 * M, 0), new Vector3(5 * M, -2 * M, 0), new Vector3(0, 3 * M, 3 * M) })
            Assert.InRange(temporal(p), single(p) - 8, single(p) + 8);
    }

    [Fact]
    public void Every_look_draws_ground_and_sky_that_are_neither_black_nor_white()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        foreach (var name in ModernLooks.Names)
        {
            var s = new ModernSettings();
            ModernLooks.Apply(s, name);
            var at = Render(s);
            float ground = at(new Vector3(-6 * M, -4 * M, 0));
            Assert.InRange(ground, 8f, 245f);
            float sky = at(new Vector3(0, 60 * M, 40 * M));
            Assert.InRange(sky, 8f, 254f);
        }
    }
}
