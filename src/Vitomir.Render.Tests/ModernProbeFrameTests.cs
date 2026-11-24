// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The light probes as the GPU draws them (ours; ModernProbes.hlsli): the street of
/// <see cref="ModernProbeTests"/> — two walls 6 m apart, the sun on the west one's face — baked and handed to
/// the renderer. The east wall's shaded face is brighter with the probes' bounce than with it scaled to
/// nothing, and the probes' sky alone lights it about as the CPU's twin says. The shaders compile and run on
/// any device, the cloud's software Vulkan included.
/// </summary>
[Collection("headless")]
public class ModernProbeFrameTests(Headless h)
{
    const int W = 320, H = 180;
    const float M = ModernMath.UnitsPerMetre;

    static GltfScene SceneOf(ModernProbeTests.Builder b)
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "plaster", BaseColorFactor = new Vector4(0.6f, 0.6f, 0.6f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        var v = new List<ModernVertex>();
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int t = 0; t < b.Indices.Count / 3; t++)
        {
            Vector3 a = b.Positions[b.Indices[3 * t]], c1 = b.Positions[b.Indices[3 * t + 1]], c2 = b.Positions[b.Indices[3 * t + 2]];
            var n = Vector3.Normalize(Vector3.Cross(c1 - a, c2 - a));
            var tan = Vector3.Normalize(c1 - a);
            foreach (var p in new[] { a, c1, c2 })
            {
                v.Add(new ModernVertex { Position = p * M, Normal = n, Tangent = new Vector4(tan, 1), Color = 0xffffffff });
                min = Vector3.Min(min, p * M); max = Vector3.Max(max, p * M);
            }
        }
        s.Primitives.Add(new GltfPrimitive { Vertices = [.. v], Indices = [.. Enumerable.Range(0, v.Count).Select(i => (uint)i)], Material = 0, Min = min, Max = max });
        s.Min = min; s.Max = max;
        return s;
    }

    /// <summary>The mean of the picture round the middle of the east wall's shaded face, seen from the street.</summary>
    Vector3 EastWall(float bounce, bool probes, Vector3 toSun)
    {
        var b = ModernProbeTests.Street(0.7f);
        var grid = b.Bake(new Vector3(-7, -6, 0.5f), new Vector3(7, 6, 10), toSun, rays: 128);
        var settings = new ModernSettings();
        ModernLooks.Apply(settings, "morning");
        settings.Clouds.Cover = 0f;
        settings.SunAzimuth = 0f; settings.NorthYaw = 0f; settings.SunElevation = 45f;   // over +x, 45° up: the street's sun
        settings.Bloom = false; settings.LensFlare = false; settings.Volumetric = false; settings.AmbientOcclusion = false; settings.SkyOcclusion = false;
        settings.Probes = probes; settings.ProbeBounce = bounce;
        using var scene = new ModernScene(h.Gpu!, SceneOf(b));
        using var p = new ModernProbes(grid);
        scene.Probes = p;
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        // From the street's west side, 1.7 m up, looking east at the shaded face.
        var camera = new FreeCamera { Position = new Vector3(-2f * M, 0, 1.7f * M), Yaw = 0f, Pitch = 10f };
        var pixels = renderer.RenderToImage(scene, camera, W, H);
        var sum = Vector3.Zero;
        for (int y = H / 2 - 6; y < H / 2 + 6; y++)
            for (int x = W / 2 - 6; x < W / 2 + 6; x++)
            {
                int o = (y * W + x) * 4;
                sum += new Vector3(pixels[o], pixels[o + 1], pixels[o + 2]);
            }
        return sum / 144f;
    }

    [Fact]
    public void The_shaded_wall_across_from_a_sunlit_one_is_brighter_with_the_probes_bounce()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var toSun = ModernMath.SunDirection(0f, 45f, 0f);
        Assert.InRange(Vector3.Dot(toSun, ModernProbeTests.StreetSun), 0.99f, 1.01f);
        var with = EastWall(1f, true, toSun);
        var without = EastWall(0f, true, toSun);
        var flat = EastWall(1f, false, toSun);
        Assert.True(with.X > without.X + 12f, $"bounce: {with} against none {without}");
        Assert.True(without.X > 3f, $"the probes' sky alone still lights it: {without}");
        // The shaded face is warmer with the bounce (sunlight off plaster) than the sky alone leaves it.
        Assert.True(with.X / MathF.Max(with.Z, 1f) > without.X / MathF.Max(without.Z, 1f), $"{with} against {without}");
        Assert.True(flat.X > 3f, $"without probes the flat ambient light still lights it: {flat}");
    }
}
