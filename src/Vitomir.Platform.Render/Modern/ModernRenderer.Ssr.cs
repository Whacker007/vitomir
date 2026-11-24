// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Ssr.cs at b4185ef, reworked for the platform.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours (rebuilt): screen-space reflections** (ModernSsr.hlsl, ModernSsrResolve.hlsl).
/// After the prepass and before the surfaces are shaded, the smooth
/// surfaces the prepass marked trace their mirror reflection across the depth buffer (at half the picture's
/// resolution, or its own in <c>ultra</c>) and read what they meet from the previous frame's finished picture;
/// a resolve filters that by the surface's roughness and accumulates it over frames; the shading pass
/// (ModernPbr.hlsl) then uses it in place of the sky and light probes' reflection where it is confident, through
/// its own Fresnel term and specular occlusion, so nothing is added or taken away twice. The first frame after
/// a start or a resize has no previous picture and shows the sky and probes' reflection alone. Off at
/// <see cref="ModernSettings.SsrSteps"/> 0 (the <c>low</c> preset).
/// </summary>
internal sealed unsafe partial class ModernRenderer
{
    SDL_GPUGraphicsPipeline* ssrTracePipeline, ssrResolvePipeline;
    SDL_GPUTexture* ssrTrace, ssrHit;
    readonly SDL_GPUTexture*[] ssrHistory = new SDL_GPUTexture*[2];
    int ssrW, ssrH, ssrCurrent, ssrFrame, ssrSourceW, ssrSourceH;
    bool ssrHistoryValid, ssrDrawn;
    /// <summary>The previous frame's finished picture (the temporal history, or the shaded picture itself
    /// without temporal anti-aliasing: nothing writes it again before the next frame's trace).</summary>
    SDL_GPUTexture* ssrSource;
    Matrix4x4 ssrPrevViewProj;

    [StructLayout(LayoutKind.Sequential)]
    struct SsrUniforms { public Matrix4x4 PrevViewProj; public Vector4 Params, Params2, Params3; }

    /// <summary>Whether screen-space reflections are drawn this frame.</summary>
    bool SsrOn => Settings.SsrSteps > 0 && depthSampled;

    /// <summary>The longest reflected ray traced, in metres.</summary>
    public const float SsrRayMetres = 60f;

    /// <summary>The weight of a new frame in the reflections' history (ours): some ten frames of memory, the
    /// history clipped to the new frame's neighbourhood so it does not ghost.</summary>
    public const float SsrHistoryWeight = 0.2f;

    /// <summary>Whether the last frame's surfaces read reflections the screen found (tests).</summary>
    public bool LastSsr => ssrDrawn;

    void CreateSsrPipelines()
    {
        var vs = ModernDevice.LoadShader(gpu, "ModernSsr", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
        var fs = ModernDevice.LoadShader(gpu, "ModernSsr", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 4, 2);
        ssrTracePipeline = FullScreenTwo(vs, fs, HdrFormat, HalfFormat);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);
        vs = ModernDevice.LoadShader(gpu, "ModernSsrResolve", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
        fs = ModernDevice.LoadShader(gpu, "ModernSsrResolve", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 4, 2);
        ssrResolvePipeline = FullScreen(vs, fs, HdrFormat, depthTest: false);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);
    }

    /// <summary>A full-screen pipeline writing two colour targets (the trace's light and its hit).</summary>
    SDL_GPUGraphicsPipeline* FullScreenTwo(SDL_GPUShader* vs, SDL_GPUShader* fs, SDL_GPUTextureFormat a, SDL_GPUTextureFormat b)
    {
        var colours = stackalloc SDL_GPUColorTargetDescription[2];
        colours[0] = new SDL_GPUColorTargetDescription { format = a };
        colours[1] = new SDL_GPUColorTargetDescription { format = b };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { color_target_descriptions = colours, num_color_targets = 2 },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(modern reflections' trace)");
        return p;
    }

    /// <summary>
    /// Traces and resolves the reflections before the surfaces are shaded; on success the surfaces read them
    /// (<see cref="SsrBinding"/>) and <c>frame.SkyGlow.W</c> carries the roughness above which they do not.
    /// </summary>
    void SsrPass(SDL_GPUCommandBuffer* cmd, ref FrameUniforms frame)
    {
        ssrDrawn = false;
        frame.SkyGlow.W = 0;
        int w = Settings.SsrFullResolution ? width : halfW, h = Settings.SsrFullResolution ? height : halfH;
        if (ssrTrace == null || w != ssrW || h != ssrH)
        {
            Release(ref ssrTrace); Release(ref ssrHit); Release(ref ssrHistory[0]); Release(ref ssrHistory[1]);
            var use = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            ssrTrace = gpu.CreateTarget(w, h, HdrFormat, use);
            ssrHit = gpu.CreateTarget(w, h, HalfFormat, use);
            ssrHistory[0] = gpu.CreateTarget(w, h, HdrFormat, use);
            ssrHistory[1] = gpu.CreateTarget(w, h, HdrFormat, use);
            ssrW = w; ssrH = h;
            ssrHistoryValid = false;
        }
        // Without the previous frame's picture (the first frame, or after a resize) the surfaces keep the sky's reflection.
        // (The picture kept must still be one of the renderer's: a change of targets releases them.)
        bool kept = ssrSource != null && (ssrSource == hdr || ssrSource == taaHistory[0] || ssrSource == taaHistory[1]);
        if (!kept || ssrSourceW != width || ssrSourceH != height) { ssrHistoryValid = false; return; }

        var f = frame;
        var u = new SsrUniforms
        {
            PrevViewProj = ssrPrevViewProj,
            Params = new Vector4(Settings.SsrSteps, Settings.SsrMaxRoughness, ModernWater.Seconds, SsrRayMetres * ModernMath.UnitsPerMetre),
            Params2 = new Vector4(1, Settings.SsrFullResolution ? 0 : 1, ssrFrame % 64, ssrHistoryValid ? 1 : 0),
            Params3 = new Vector4(1f / w, 1f / h, SsrHistoryWeight, 0),
        };

        // The trace: the light found and its confidence, the hit's distance and the surface's roughness.
        var targets = stackalloc SDL_GPUColorTargetInfo[2];
        targets[0] = new SDL_GPUColorTargetInfo { texture = ssrTrace, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        targets[1] = new SDL_GPUColorTargetInfo { texture = ssrHit, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var pass = SDL_BeginGPURenderPass(cmd, targets, 2, null);
        SDL_BindGPUGraphicsPipeline(pass, ssrTracePipeline);
        var b = stackalloc SDL_GPUTextureSamplerBinding[4];
        b[0] = Bind(depth, pointSampler);
        b[1] = Bind(normals, pointSampler);
        b[2] = Bind(ssrSource, postSampler);
        b[3] = b[1];
        SDL_BindGPUFragmentSamplers(pass, 0, b, 4);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&f), (uint)sizeof(FrameUniforms));
        SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&u), (uint)sizeof(SsrUniforms));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);

        // The resolve: filtered by roughness, blended into the history.
        int next = 1 - ssrCurrent;
        var colour = new SDL_GPUColorTargetInfo { texture = ssrHistory[next], load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, ssrResolvePipeline);
        b[0] = Bind(ssrTrace, pointSampler);
        b[1] = Bind(ssrHit, pointSampler);
        b[2] = Bind(depth, pointSampler);
        b[3] = Bind(ssrHistory[ssrCurrent], postSampler);
        SDL_BindGPUFragmentSamplers(pass, 0, b, 4);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&f), (uint)sizeof(FrameUniforms));
        SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&u), (uint)sizeof(SsrUniforms));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
        ssrCurrent = next;
        ssrHistoryValid = true;
        ssrDrawn = true;
        frame.SkyGlow.W = Settings.SsrMaxRoughness;
    }

    /// <summary>What the surfaces read at their sixteenth sampler: this frame's reflections, else <paramref name="otherwise"/>.</summary>
    SDL_GPUTextureSamplerBinding SsrBinding(SDL_GPUTextureSamplerBinding otherwise) =>
        ssrDrawn ? Bind(ssrHistory[ssrCurrent], postSampler) : otherwise;

    /// <summary>Keeps the frame's finished picture (before bloom and tone mapping) and its unjittered
    /// view-projection for the next frame's reflections.</summary>
    void SsrKeep(SDL_GPUTexture* picture, Matrix4x4 unjittered)
    {
        if (!SsrOn) { ssrSource = null; ssrHistoryValid = false; return; }
        ssrSource = picture;
        ssrSourceW = width; ssrSourceH = height;
        ssrPrevViewProj = unjittered;
        ssrFrame++;
    }

    void DisposeSsr()
    {
        Release(ref ssrTrace); Release(ref ssrHit); Release(ref ssrHistory[0]); Release(ref ssrHistory[1]);
        if (ssrTracePipeline != null) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, ssrTracePipeline);
        if (ssrResolvePipeline != null) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, ssrResolvePipeline);
        ssrTracePipeline = ssrResolvePipeline = null;
        ssrSource = null;
    }
}
