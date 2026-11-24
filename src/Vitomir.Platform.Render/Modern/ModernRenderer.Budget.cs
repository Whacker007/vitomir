// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Budget.cs at b4185ef, reworked for the platform.
// Ours: the frame about to be drawn, counted for SDL's Direct3D 12 sampler heap
// (D3D12SamplerBudget has the rule and its source). Each loop here is the loop of the pass it counts, in
// the pass's own order, with the draws left out: change a pass's order or its culling and change it here.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

internal sealed unsafe partial class ModernRenderer
{
    /// <summary>
    /// The sampler descriptors the last frame's passes were counted to write (<see cref="D3D12SamplerBudget"/>;
    /// an upper bound, the full-screen passes left to its margin). Counted on Direct3D 12, and on any
    /// driver when <see cref="CountSamplers"/>; zero when not counted.
    /// </summary>
    public int LastSamplerDescriptors { get; private set; }

    /// <summary>Count every frame's sampler descriptors whatever the driver (the tests', and a tool's that reports them).</summary>
    public bool CountSamplers { get; set; }

    /// <summary>
    /// Whether the frame fits Direct3D 12's one heap of sampler descriptors. A frame that does not is not
    /// drawn, and the run is stopped with the reason, once (<see cref="D3D12SamplerBudget.Stop"/>). Always
    /// true on any other driver.
    /// </summary>
    bool SamplersFit(ModernScene scene, FreeCamera camera, List<ModernScene.Draw> seen, Vector4[] planes, Matrix4x4[] cascades, int sunCascades)
    {
        bool limited = D3D12SamplerBudget.Limited(gpu.Driver) || HarnessLimit is not null;
        if (!limited && !CountSamplers) return true;
        LastSamplerDescriptors = SamplerBound(scene, camera, seen, planes, cascades, sunCascades).Used;
        if (HarnessLimit is { } held)
        {
            if (LastSamplerDescriptors <= held) return true;
            D3D12SamplerBudget.Stop(D3D12SamplerBudget.Refusal(LastSamplerDescriptors) + $" [VITOMIR_SAMPLER_LIMIT={held} on {gpu.Driver}: a harness run]");
            return false;
        }
        if (D3D12SamplerBudget.Fits(gpu.Driver, LastSamplerDescriptors)) return true;
        D3D12SamplerBudget.Stop(D3D12SamplerBudget.Refusal(LastSamplerDescriptors));
        return false;
    }

    /// <summary>
    /// A harness aid (ours): <c>VITOMIR_SAMPLER_LIMIT=&lt;n&gt;</c> in the environment holds every driver to
    /// <c>n</c> counted descriptors, so the stop — the world not drawn, the line, the exit code — can be
    /// tried on a machine without Direct3D 12 (the cloud's software Vulkan). Null when not set.
    /// </summary>
    static readonly int? HarnessLimit =
        int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_SAMPLER_LIMIT"), out int n) && n > 0 ? n : null;

    /// <summary>
    /// The frame's draws counted pass by pass, as <see cref="Render"/> is about to issue them: the sun's
    /// cascades, the point lights' cubes when they are due, the height map, the prepass and the surfaces,
    /// each with the cell level's characters (<see cref="Extra"/>).
    /// </summary>
    D3D12SamplerBudget SamplerBound(ModernScene scene, FreeCamera camera, List<ModernScene.Draw> seen, Vector4[] planes,
                                    Matrix4x4[] cascades, int sunCascades)
    {
        var budget = new D3D12SamplerBudget();
        bool TwoSided(ModernScene.Draw d) => scene.Material(d.Material).DoubleSided;
        // The characters' draws that <paramref name="within"/> sees, the pipeline bound again when the sidedness changes.
        void Extras(Vector4[] within, int samplers, bool shadow = false)
        {
            if (!HasExtra) return;
            bool? bound = null;
            foreach (var d in Extra!.Draws)
            {
                if (!(shadow ? d.InShadows : d.InView) || !ModernMath.BoxVisible(within, d.Min, d.Max)) continue;   // the passes a draw is in
                if (bound != d.DoubleSided) { budget.Pipeline(samplers); bound = d.DoubleSided; }
                budget.Draw(-1L - d.Texture);
            }
        }

        // ShadowPass: each cascade's casters, by sidedness, in the scene's order.
        if (sunCascades > 0)
        {
            if (scene.Vertices != null)
            {
                for (int c = 0; c < sunCascades; c++)
                {
                    int firstSide = order.Within(scene.Casters ?? scene.Draws, scene.Source, ModernMath.FrustumPlanes(cascades[c]));
                    for (int turn = 0; turn < 2 && firstSide >= 0; turn++)
                    {
                        var group = order.Cascade[turn == 0 ? firstSide : 1 - firstSide];
                        if (group.Count == 0) continue;
                        budget.Pipeline(Gpu.FragmentSamplers);
                        foreach (var d in group) budget.Draw(d.Material);
                    }
                }
            }
            for (int c = 0; c < sunCascades; c++) Extras(ModernMath.FrustumPlanes(cascades[c]), Gpu.FragmentSamplers, shadow: true);
        }

        // PointShadowPass, when the lights that cast have changed: one pipeline, every face of every cube.
        bool cubesCurrent = ReferenceEquals(scene, pointShadowScene) && pointShadowMatrices.AsSpan().SequenceEqual(pointShadowDrawn);
        if (shadowed.Count > 0 && !cubesCurrent && scene.Vertices != null)
        {
            budget.Pipeline(Gpu.FragmentSamplers);
            for (int face = 0; face < shadowed.Count * 6 && face < pointShadowMatrices.Length; face++)
            {
                var within = ModernMath.FrustumPlanes(pointShadowMatrices[face]);
                foreach (var d in scene.Draws)
                {
                    var mat = scene.Material(d.Material);
                    if (mat.AlphaMode == GltfAlphaMode.Blend || Glows(mat) || !ModernMath.BoxVisible(within, d.Min, d.Max)) continue;
                    budget.Draw(d.Material);
                }
            }
        }

        // HeightPass (ambient occlusion's sky occlusion): the casters under the square about the camera.
        if (Settings.AmbientOcclusion && depthSampled && Settings.SkyOcclusion && scene.Vertices != null)
        {
            var within = ModernMath.FrustumPlanes(HeightView(scene, camera, out _, out _, out _, out _, out _));
            budget.Pipeline(Gpu.FragmentSamplers);
            foreach (var d in order.Cast(scene.Casters ?? scene.Draws, scene.Source))
                if (ModernMath.BoxVisible(within, d.Min, d.Max)) budget.Draw(d.Material);
        }

        // Prepass and MainPass: what the camera sees, by sidedness and then by material.
        var opaque = order.Opaque;       // the frame's own, as the passes will issue them (ModernDrawOrder)
        if (scene.Vertices != null)
            foreach (var group in opaque)
            {
                if (group.Count == 0) continue;
                budget.Pipeline(2);   // ModernPrepass
                foreach (var d in group) budget.Draw(d.Material);
            }
        Extras(planes, 2);
        if (scene.Vertices != null)
            foreach (var group in opaque)
            {
                if (group.Count == 0) continue;
                budget.Pipeline(SurfaceSamplers);
                foreach (var d in group) budget.Draw(d.Material);
            }
        Extras(planes, SurfaceSamplers);
        // (The sky and the other full-screen passes are D3D12SamplerBudget.Margin's.)
        if (scene.Vertices != null)
        {
            bool? bound = null;
            foreach (var d in order.Blended)
            {
                bool two = TwoSided(d);
                if (bound != two) { budget.Pipeline(SurfaceSamplers); bound = two; }
                budget.Draw(d.Material);
            }
        }
        return budget;
    }
}
