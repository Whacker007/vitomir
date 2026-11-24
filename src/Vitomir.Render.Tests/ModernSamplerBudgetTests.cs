// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Direct3D 12's one heap of sampler descriptors (ours).** SDL's Direct3D 12 driver gives a
/// command buffer 2,048 sampler descriptors and writes past the heap's end when a frame needs more — the
/// access violation the new town died of on its first frame (a fast NVIDIA card). These pin
/// <see cref="D3D12SamplerBudget"/>'s arithmetic, which counts as SDL writes (a pipeline's whole set at a
/// draw whose pipeline is new or whose bindings changed): a small frame passes, a frame of the town's size
/// is refused on Direct3D 12 with a line that says what to do, and Vulkan is never refused. The last test
/// counts a real frame of two materials through the renderer on whatever device the machine has.
/// </summary>
[Collection("headless")]
public class ModernSamplerBudgetTests(Headless h)
{
    const int Surface = 16, Two = 2;

    /// <summary>A frame as the renderer counts it: cascades of shadow casters, the prepass, the surfaces.</summary>
    static D3D12SamplerBudget Frame(int materials, int cascades, int casterRunsPerCascade)
    {
        var b = new D3D12SamplerBudget();
        for (int c = 0; c < cascades; c++)
        {
            b.Pipeline(Two);
            for (int run = 0; run < casterRunsPerCascade; run++) b.Draw(run % materials);
        }
        b.Pipeline(Two);
        for (int m = 0; m < materials; m++) b.Draw(m);
        b.Pipeline(Surface);
        for (int m = 0; m < materials; m++) b.Draw(m);
        return b;
    }

    [Fact]
    public void A_draw_writes_its_pipelines_whole_set_when_the_pipeline_is_new_or_the_material_changes()
    {
        var b = new D3D12SamplerBudget();
        b.Pipeline(Surface);
        b.Draw(7);
        Assert.Equal(16, b.Used);
        b.Draw(7);                       // the same bindings again: nothing written
        b.Draw(7);
        Assert.Equal(16, b.Used);
        b.Draw(8);                       // another material's maps
        Assert.Equal(32, b.Used);
        b.Pipeline(Surface);             // bound again: the next draw writes even with the same material
        b.Draw(8);
        Assert.Equal(48, b.Used);
        b.Pipeline(Two);
        b.Draw(8);
        Assert.Equal(50, b.Used);
        b.Add(8);                        // a full-screen pass counted whole
        Assert.Equal(58, b.Used);
    }

    [Fact]
    public void A_pipeline_bound_and_never_drawn_with_writes_nothing()
    {
        var b = new D3D12SamplerBudget();
        b.Pipeline(Surface);
        b.Pipeline(Two);
        Assert.Equal(0, b.Used);
    }

    [Fact]
    public void A_frame_of_a_few_materials_passes_on_Direct3D_12()
    {
        var b = Frame(materials: 6, cascades: 4, casterRunsPerCascade: 12);
        Assert.Equal(4 * 12 * 2 + 6 * 2 + 6 * 16, b.Used);
        Assert.True(D3D12SamplerBudget.Fits("direct3d12", b.Used));
    }

    [Fact]
    public void A_frame_of_the_towns_size_is_refused_on_Direct3D_12_and_never_on_Vulkan()
    {
        // About seventy materials, and each cascade's casters cell by cell (a few hundred changes of material).
        var b = Frame(materials: 70, cascades: 4, casterRunsPerCascade: 300);
        Assert.True(b.Used > D3D12SamplerBudget.HeapSize, $"{b.Used}");
        Assert.False(D3D12SamplerBudget.Fits("direct3d12", b.Used));
        Assert.True(D3D12SamplerBudget.Fits("vulkan", b.Used));
        Assert.True(D3D12SamplerBudget.Fits("metal", b.Used));
        Assert.True(D3D12SamplerBudget.Fits("vulkan", int.MaxValue));
    }

    [Fact]
    public void The_limit_is_SDLs_heap_less_the_margin_for_the_full_screen_passes()
    {
        Assert.Equal(2048, D3D12SamplerBudget.HeapSize);
        Assert.Equal(2048 - D3D12SamplerBudget.Margin, D3D12SamplerBudget.Limit);
        // The full-screen passes as built: clouds 4, occlusion 4 + 2 blurs, shafts 2 + 2 blurs, reflections 4 + 4,
        // the sky 8, temporal anti-aliasing 4, the flare 2, bloom 6 down + 5 up, tone mapping 4, FXAA 2.
        int fullScreen = 4 + (4 + 2 * 2) + (2 + 2 * 2) + (4 + 4) + 8 + 4 + 2 + (6 + 5) * 2 + 4 + 2;
        Assert.Equal(68, fullScreen);
        Assert.True(fullScreen <= D3D12SamplerBudget.Margin);
        Assert.True(D3D12SamplerBudget.Fits("direct3d12", D3D12SamplerBudget.Limit));
        Assert.False(D3D12SamplerBudget.Fits("direct3d12", D3D12SamplerBudget.Limit + 2));
    }

    [Fact]
    public void The_refusal_says_what_happened_and_what_to_do()
    {
        Assert.Equal("gpu: this level needs more texture samplers in one frame than SDL's Direct3D 12 driver allows (3,660 of 2,048); run with --gpu vulkan",
                     D3D12SamplerBudget.Refusal(3660));
    }

    [Fact]
    public void A_stopped_run_is_said_once_and_ends_with_its_own_exit_code()
    {
        D3D12SamplerBudget.Forget();
        try
        {
            Assert.Null(D3D12SamplerBudget.Stopped);
            Assert.Equal(0, D3D12SamplerBudget.Exit(0));
            var said = new List<string>();
            D3D12SamplerBudget.Stop("first", said.Add);
            D3D12SamplerBudget.Stop("second", said.Add);
            Assert.Equal(["first"], said);
            Assert.Equal("first", D3D12SamplerBudget.Stopped);
            Assert.Equal(D3D12SamplerBudget.ExitCode, D3D12SamplerBudget.Exit(0));
            Assert.NotEqual(0, D3D12SamplerBudget.ExitCode);
        }
        finally { D3D12SamplerBudget.Forget(); }
    }

    // ---- a real frame, counted by the renderer ----

    static GltfPrimitive Slab(Vector3 min, Vector3 max, int material)
    {
        var v = new ModernVertex[4];
        v[0].Position = new Vector3(min.X, min.Y, max.Z); v[1].Position = new Vector3(max.X, min.Y, max.Z);
        v[2].Position = new Vector3(max.X, max.Y, max.Z); v[3].Position = new Vector3(min.X, max.Y, max.Z);
        for (int i = 0; i < 4; i++) { v[i].Normal = Vector3.UnitZ; v[i].Tangent = new Vector4(1, 0, 0, 1); v[i].Color = 0xffffffff; }
        return new GltfPrimitive { Vertices = v, Indices = [0, 1, 2, 0, 2, 3], Material = material, Min = min, Max = max };
    }

    [Fact]
    public void A_frame_of_two_materials_is_counted_as_its_prepass_and_its_surfaces()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), RoughnessFactor = 0.9f });
        s.Materials.Add(new GltfMaterial { Name = "block", BaseColorFactor = new Vector4(0.6f, 0.3f, 0.2f, 1), RoughnessFactor = 0.6f });
        float m = ModernMath.UnitsPerMetre;
        s.Primitives.Add(Slab(new Vector3(-10 * m, -10 * m, -0.2f * m), new Vector3(10 * m, 10 * m, 0), 0));
        s.Primitives.Add(Slab(new Vector3(-m, -m, 0), new Vector3(m, m, 2 * m), 1));
        s.Primitives.Add(Slab(new Vector3(-3 * m, -m, 0), new Vector3(-2 * m, m, 1 * m), 1));
        foreach (var p in s.Primitives) { s.Min = Vector3.Min(s.Min, p.Min); s.Max = Vector3.Max(s.Max, p.Max); }
        using var scene = new ModernScene(h.Gpu!, s);
        var camera = new FreeCamera { Position = new Vector3(0, -14 * 72, 9 * 72), Yaw = 90, Pitch = -25 };

        // No shadows and no occlusion: one prepass pipeline and one surface pipeline, two materials each.
        var plain = new ModernSettings { SunAzimuth = 180, SunElevation = 60, Shadows = false, AmbientOcclusion = false };
        using (var renderer = new ModernRenderer(h.Gpu!, plain, h.Gpu!.ColorFormat) { CountSamplers = true })
        {
            renderer.RenderToImage(scene, camera, 160, 90);
            Assert.Equal(2 * 2 + 2 * 16, renderer.LastSamplerDescriptors);
        }
        // With the sun's shadows every cascade that sees a caster adds its own, two descriptors a material.
        var shadowed = new ModernSettings { SunAzimuth = 180, SunElevation = 60, AmbientOcclusion = false };
        using (var renderer = new ModernRenderer(h.Gpu!, shadowed, h.Gpu!.ColorFormat) { CountSamplers = true })
        {
            renderer.RenderToImage(scene, camera, 160, 90);
            Assert.InRange(renderer.LastSamplerDescriptors, 36 + 2, 36 + 4 * 2 * 2);
            Assert.True(D3D12SamplerBudget.Fits("direct3d12", renderer.LastSamplerDescriptors));
        }
        Assert.Null(D3D12SamplerBudget.Stopped);
    }
}
