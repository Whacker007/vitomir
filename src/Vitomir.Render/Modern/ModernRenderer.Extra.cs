// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The geometry drawn beside the scene (ours): a cell level's characters and things (ModernExtra, CellThingPass).
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Render.Modern;

public sealed unsafe partial class ModernRenderer
{
    /// <summary>
    /// Geometry drawn with the scene this frame (ours): into the sun's cascades, the prepass and the
    /// surfaces, by the same pipelines and shaders, so it casts and receives the sun's shadows, takes
    /// ambient occlusion, the clustered lights, the ambient light and the haze, and goes through the
    /// same tone mapping. Null for none (every <c>--view-gltf</c> still). Not drawn into the point
    /// lights' shadow cubes, which are kept from frame to frame for a static scene, nor into the sky
    /// occlusion's height map.
    /// </summary>
    public ModernExtra? Extra { get; set; }

    bool HasExtra => Extra is { Vertices: not null, Indices: not null, Draws.Count: > 0 };

    void BindExtra(SDL_GPURenderPass* pass)
    {
        var vb = new SDL_GPUBufferBinding { buffer = Extra!.Vertices };
        var ib = new SDL_GPUBufferBinding { buffer = Extra.Indices };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
    }

    /// <summary>The extra geometry into each of the <paramref name="count"/> cascades of the shadow atlas (inside the shadow pass).</summary>
    void ExtraShadows(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4[] cascades, int count)
    {
        if (!HasExtra) return;
        var extra = Extra!;
        BindExtra(pass);
        int res = Settings.ShadowResolution;
        for (int c = 0; c < count; c++)
        {
            var port = new SDL_GPUViewport { x = (c & 1) * res, y = (c >> 1) * res, w = res, h = res, min_depth = 0, max_depth = 1 };
            SDL_SetGPUViewport(pass, &port);
            var box = new SDL_Rect { x = (c & 1) * res, y = (c >> 1) * res, w = res, h = res };
            SDL_SetGPUScissor(pass, &box);
            var lvp = cascades[c];
            var planes = ModernMath.FrustumPlanes(lvp);
            bool? bound = null;
            foreach (var d in extra.Draws)
            {
                if (!ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
                if (bound != d.DoubleSided)
                {
                    SDL_BindGPUGraphicsPipeline(pass, d.DoubleSided ? shadowPipeline : shadowPipelineCulled);
                    PushVertex(cmd, lvp, Vector4.Zero);
                    bound = d.DoubleSided;
                }
                var b = extra.BaseColour(d.Texture);
                Gpu.BindFragmentSampler(pass, b);
                var p = new Vector4(d.AlphaCutoff, 1f, 0, 0);
                SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(Vector4));
                SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, 0, 0);
            }
        }
    }

    /// <summary>The extra geometry's depth and normals (inside the prepass).</summary>
    void ExtraPrepass(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, Vector4[] planes)
    {
        if (!HasExtra) return;
        var extra = Extra!;
        BindExtra(pass);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        bool? bound = null;
        foreach (var d in extra.Draws)
        {
            if (!ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
            if (bound != d.DoubleSided)
            {
                SDL_BindGPUGraphicsPipeline(pass, prepass[d.DoubleSided ? 1 : 0]);
                PushVertex(cmd, viewProj, Vector4.Zero);
                bound = d.DoubleSided;
            }
            bindings[0] = bindings[1] = extra.BaseColour(d.Texture);
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            var p = new CutUniforms { Cut = new Vector4(d.AlphaCutoff, 1f, 0, 0) };
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(CutUniforms));
            SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, 0, 0);
        }
    }

    /// <summary>The extra geometry's surfaces, shaded as the scene's opaque ones are (inside the main pass, before the sky).</summary>
    void ExtraSurfaces(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, FrameUniforms frame,
                       SDL_GPUBuffer** storage, SDL_GPUTextureSamplerBinding* shared, Vector4[] planes)
    {
        if (!HasExtra) return;
        var extra = Extra!;
        BindExtra(pass);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[SurfaceSamplers];
        bool? bound = null;
        foreach (var d in extra.Draws)
        {
            if (!ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
            if (bound != d.DoubleSided)
            {
                BindSurface(cmd, pass, surface[d.DoubleSided ? 1 : 0], viewProj, frame, storage);
                bound = d.DoubleSided;
            }
            bindings[0] = extra.BaseColour(d.Texture);
            bindings[1] = bindings[3] = bindings[4] = extra.StandIn(normal: false);
            bindings[2] = extra.StandIn(normal: true);
            bindings[5] = shared[0]; bindings[6] = shared[1]; bindings[7] = shared[2]; bindings[8] = bindings[9] = shared[3];
            bindings[10] = extra.StandIn(normal: false);
            bindings[11] = shared[4]; bindings[12] = shared[5]; bindings[13] = shared[6];
            bindings[14] = shared[7]; bindings[15] = SsrBinding(shared[1]);   // the light probes: characters are lit by them too; the screen's reflections
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, SurfaceSamplers);
            var u = new MaterialUniforms
            {
                BaseColorFactor = Vector4.One,
                MrFactors = new Vector4(d.Metallic, d.Roughness, 1f, 0f),
                Emissive = Vector4.Zero,
                TexSets = Vector4.Zero,
                Flags = new Vector4(0, 0, 0, d.Specular),
            };
            SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&u), (uint)sizeof(MaterialUniforms));
            SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, 0, 0);
        }
    }
}
