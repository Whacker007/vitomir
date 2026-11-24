// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Post.cs at b4185ef, reworked for the platform.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The modern renderer's screen-space passes (ours): the lights' upload, ambient
/// occlusion and the sun's shafts at half resolution with their depth-aware blur, bloom, the sun's
/// visibility for the lens flare, tone mapping and FXAA.
/// </summary>
internal sealed unsafe partial class ModernRenderer
{
    const SDL_GPUTextureFormat HalfFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT;
    /// <summary>Ambient occlusion's targets: r the occlusion, g the depth, b the screen's own occlusion alone (for the light probes; ours).</summary>
    const SDL_GPUTextureFormat AoFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;
    const int BloomLevels = 6;

    SDL_GPUGraphicsPipeline* aoPipeline, blurPipeline, aoBlurPipeline, volumePipeline, bloomDownPipeline, bloomUpPipeline, taaPipeline;
    readonly SDL_GPUTexture*[] taaHistory = new SDL_GPUTexture*[2];
    int taaFrame, taaCurrent;
    bool taaHistoryValid;
    Matrix4x4 taaPrevViewProj;
    SDL_GPUTexture* aoA, aoB, volA, volB, sunVis, heightMap;
    int heightSize;
    readonly SDL_GPUTexture*[] bloom = new SDL_GPUTexture*[BloomLevels];
    readonly (int W, int H)[] bloomSize = new (int, int)[BloomLevels];
    int halfW, halfH;
    int aoW, aoH;
    bool aoFull;
    SDL_GPUBuffer* lightBuffer, clusterBuffer, shadowMatrixBuffer;
    SDL_GPUTexture* pointShadowAtlas;
    int pointShadowRes, pointShadowSlots;
    List<int> shadowed = [];
    const int MaxShadowedLights = 16;
    SDL_GPUTransferBuffer* lightTransfer;
    int clusterCapacity, transferCapacity;

    [StructLayout(LayoutKind.Sequential)]
    struct Four { public Vector4 A, B, C, D, E; }

    void CreatePostPipelines()
    {
        SDL_GPUGraphicsPipeline* Make(string name, int samplers, int uniforms, SDL_GPUTextureFormat format, bool additive = false)
        {
            var vs = ModernDevice.LoadShader(gpu, name, SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
            var fs = ModernDevice.LoadShader(gpu, name, SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers, uniforms);
            var p = FullScreen(vs, fs, format, depthTest: false, additive);
            SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);
            return p;
        }
        aoPipeline = Make("ModernAo", 4, 2, AoFormat);
        blurPipeline = Make("ModernBlur", 2, 1, HalfFormat);
        aoBlurPipeline = Make("ModernBlur", 2, 1, AoFormat);
        volumePipeline = Make("ModernVolume", 2, 2, HalfFormat);
        bloomDownPipeline = Make("ModernBloom", 2, 1, HdrFormat);
        bloomUpPipeline = Make("ModernBloom", 2, 1, HdrFormat, additive: true);
        taaPipeline = Make("ModernTaa", 4, 2, HdrFormat);
        CreateCloudPipeline();   // the half-resolution clouds (ours, ModernRenderer.Clouds.cs)
        CreateSsrPipelines();    // screen-space reflections (ours, ModernRenderer.Ssr.cs)
        lightBuffer = ModernDevice.CreateStorageBuffer(gpu, ModernLights.MaxLights * 64);
        shadowMatrixBuffer = ModernDevice.CreateStorageBuffer(gpu, MaxShadowedLights * 6 * 64);
        EnsurePointShadowAtlas();
        sunVis = gpu.CreateTarget(1, 1, HdrFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
    }

    void PostTargets(int w, int h)
    {
        Release(ref aoA); Release(ref aoB); Release(ref volA); Release(ref volB);
        for (int k = 0; k < BloomLevels; k++) Release(ref bloom[k]);
        Release(ref taaHistory[0]); Release(ref taaHistory[1]);
        taaHistoryValid = false;
        var use = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
        halfW = Math.Max(1, (w + 1) / 2); halfH = Math.Max(1, (h + 1) / 2);
        // Ambient occlusion at half resolution, or the picture's own (the ultra quality's, ours).
        aoFull = Settings.AoFullResolution;
        (aoW, aoH) = aoFull ? (w, h) : (halfW, halfH);
        aoA = gpu.CreateTarget(aoW, aoH, AoFormat, use); aoB = gpu.CreateTarget(aoW, aoH, AoFormat, use);
        CloudTargets();
        taaHistory[0] = gpu.CreateTarget(w, h, HdrFormat, use); taaHistory[1] = gpu.CreateTarget(w, h, HdrFormat, use);
        volA = gpu.CreateTarget(halfW, halfH, HalfFormat, use); volB = gpu.CreateTarget(halfW, halfH, HalfFormat, use);
        for (int k = 0; k < BloomLevels; k++)
        {
            bloomSize[k] = (Math.Max(1, w >> (k + 1)), Math.Max(1, h >> (k + 1)));
            bloom[k] = gpu.CreateTarget(bloomSize[k].W, bloomSize[k].H, HdrFormat, use);
        }
        var (gx, gy, gz) = ModernLights.Grid(w, h);
        int need = gx * gy * gz * 2 + ModernLights.MaxIndices;
        if (need > clusterCapacity)
        {
            if (clusterBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, clusterBuffer);
            clusterBuffer = ModernDevice.CreateStorageBuffer(gpu, need * 4);
            clusterCapacity = need;
        }
    }

    /// <summary>Sorts the scene's lights into this view's clusters and queues their upload; returns how many are shaded.</summary>
    int UploadLights(SDL_GPUCommandBuffer* cmd, ModernScene scene, FreeCamera camera, float aspect, int w, int h)
    {
        var lights = scene.Lights;
        shadowed = [];
        if (lights.Count == 0) return 0;
        float ys = 1f / MathF.Tan(camera.VerticalFov / 2f), xs = ys / aspect;
        int budget = Math.Clamp(Settings.LightShadows, 0, MaxShadowedLights);
        if (budget > 0)
        {
            EnsurePointShadowAtlas();
            shadowed = ModernLights.PickShadowed(lights, camera.Position, budget, ModernMath.FrustumPlanes(ViewProjection(camera, aspect)));
        }
        var packed = ModernLights.Pack(lights, shadowed);
        var clusters = clusterSorter.Assign(lights, camera.View, xs, ys, w, h);   // its arrays kept from frame to frame
        var matrices = new Matrix4x4[Math.Max(shadowed.Count, 1) * 6];
        for (int k = 0; k < shadowed.Count; k++)
            for (int f = 0; f < 6; f++)
                matrices[k * 6 + f] = ModernLights.CubeFaceViewProjection(lights[shadowed[k]].Position, f, lights[shadowed[k]].Range, pointShadowRes);
        pointShadowMatrices = matrices;
        int lightBytes = packed.Length * 16, clusterBytes = Math.Min(clusters.Length, clusterCapacity) * 4, matrixBytes = matrices.Length * 64;
        int total = lightBytes + clusterBytes + matrixBytes;
        if (total > transferCapacity)
        {
            if (lightTransfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, lightTransfer);
            var info = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)total };
            lightTransfer = SDL_CreateGPUTransferBuffer(gpu.Device, &info);
            if (lightTransfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (lights)");
            transferCapacity = total;
        }
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, lightTransfer, true);
        MemoryMarshal.AsBytes(packed.AsSpan()).CopyTo(new Span<byte>(map, lightBytes));
        MemoryMarshal.AsBytes(clusters[..(clusterBytes / 4)]).CopyTo(new Span<byte>(map + lightBytes, clusterBytes));
        MemoryMarshal.AsBytes(matrices.AsSpan()).CopyTo(new Span<byte>(map + lightBytes + clusterBytes, matrixBytes));
        SDL_UnmapGPUTransferBuffer(gpu.Device, lightTransfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = lightTransfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = lightBuffer, offset = 0, size = (uint)lightBytes };
        SDL_UploadToGPUBuffer(copy, &src, &dst, true);
        src.offset = (uint)lightBytes;
        dst = new SDL_GPUBufferRegion { buffer = clusterBuffer, offset = 0, size = (uint)clusterBytes };
        SDL_UploadToGPUBuffer(copy, &src, &dst, true);
        src.offset = (uint)(lightBytes + clusterBytes);
        dst = new SDL_GPUBufferRegion { buffer = shadowMatrixBuffer, offset = 0, size = (uint)matrixBytes };
        SDL_UploadToGPUBuffer(copy, &src, &dst, true);
        SDL_EndGPUCopyPass(copy);
        return Math.Min(lights.Count, ModernLights.MaxLights);
    }

    readonly ModernLights.ClusterSorter clusterSorter = new();
    Matrix4x4[] pointShadowMatrices = [];
    ModernScene? pointShadowScene;
    Matrix4x4[] pointShadowDrawn = [];

    /// <summary>Whether the atlas already holds this frame's cubes: the same scene and the same faces as when last drawn.</summary>
    bool PointShadowsCurrent(ModernScene scene)
    {
        if (ReferenceEquals(scene, pointShadowScene) && pointShadowMatrices.AsSpan().SequenceEqual(pointShadowDrawn)) return true;
        pointShadowScene = scene;
        pointShadowDrawn = pointShadowMatrices;
        return false;
    }

    void EnsurePointShadowAtlas()
    {
        int res = Math.Clamp(Settings.LightShadowResolution, 64, 2048);
        int slots = Math.Clamp(Settings.LightShadows, 1, MaxShadowedLights);
        if (pointShadowAtlas != null && res == pointShadowRes && slots == pointShadowSlots) return;
        Release(ref pointShadowAtlas);
        pointShadowDrawn = [];
        pointShadowAtlas = gpu.CreateTarget(6 * res, slots * res, shadowFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        pointShadowRes = res; pointShadowSlots = slots;
    }

    /// <summary>
    /// The shadowed lights' cubes: for each, six square perspective views from the light, one per face,
    /// into a row of the atlas; each face draws only what lies in its own frustum.
    /// </summary>
    void PointShadowPass(SDL_GPUCommandBuffer* cmd, ModernScene scene)
    {
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = pointShadowAtlas, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, null, 0, &ds);
        if (scene.Vertices != null && shadowed.Count > 0)
        {
            BindScene(pass, scene);
            SDL_BindGPUGraphicsPipeline(pass, shadowPipeline);
            int res = pointShadowRes;
            for (int k = 0; k < shadowed.Count; k++)
                for (int f = 0; f < 6; f++)
                {
                    var port = new SDL_GPUViewport { x = f * res, y = k * res, w = res, h = res, min_depth = 0, max_depth = 1 };
                    SDL_SetGPUViewport(pass, &port);
                    var box = new SDL_Rect { x = f * res, y = k * res, w = res, h = res };
                    SDL_SetGPUScissor(pass, &box);
                    var m = pointShadowMatrices[k * 6 + f];
                    var planes = ModernMath.FrustumPlanes(m);
                    // Foliage at rest: the cubes are drawn once for a static scene (ours).
                    PushVertex(cmd, m, Vector4.Zero);
                    foreach (var d in scene.Draws)
                    {
                        var mat = scene.Material(d.Material);
                        // What glows (a torch's flame round its own light, a lit window) is light, not an occluder.
                        if (mat.AlphaMode == GltfAlphaMode.Blend || Glows(mat) || !ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
                        Gpu.BindFragmentSampler(pass, scene.Binding(mat.BaseColorTexture, srgb: true));
                        var p = new Vector4(mat.AlphaMode == GltfAlphaMode.Mask ? mat.AlphaCutoff : -1f, mat.BaseColorFactor.W, mat.BaseColorTexture.TexCoord, mat.Foliage ? 1 : 0);
                        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(Vector4));
                        SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
                    }
                }
        }
        SDL_EndGPURenderPass(pass);
    }

    static bool Glows(GltfMaterial m) => MathF.Max(m.EmissiveFactor.X, MathF.Max(m.EmissiveFactor.Y, m.EmissiveFactor.Z)) * m.EmissiveStrength > 0.5f;

    /// <summary>One full-screen draw into <paramref name="target"/> with two sampler bindings and up to two uniform blocks.</summary>
    void Screen(SDL_GPUCommandBuffer* cmd, SDL_GPUGraphicsPipeline* pipeline, SDL_GPUTexture* target, bool load,
                SDL_GPUTextureSamplerBinding a, SDL_GPUTextureSamplerBinding b, void* u0, int u0Size, void* u1 = null, int u1Size = 0)
    {
        var colour = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = load ? SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD : SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        bindings[0] = a; bindings[1] = b;
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        if (u0 != null) SDL_PushGPUFragmentUniformData(cmd, 0, (nint)u0, (uint)u0Size);
        if (u1 != null) SDL_PushGPUFragmentUniformData(cmd, 1, (nint)u1, (uint)u1Size);
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    SDL_GPUTextureSamplerBinding Bind(SDL_GPUTexture* t, SDL_GPUSampler* s) => new() { texture = t, sampler = s };

    /// <summary>Blurs <paramref name="a"/> across and then down through <paramref name="b"/>, back into <paramref name="a"/>.</summary>
    void BilateralBlur(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* a, SDL_GPUTexture* b, float sharpness, int tw, int th, SDL_GPUGraphicsPipeline* pipeline = null)
    {
        if (pipeline == null) pipeline = blurPipeline;
        var across = new Vector4(1f / tw, 0, sharpness, 0);
        var down = new Vector4(0, 1f / th, sharpness, 0);
        Screen(cmd, pipeline, b, false, Bind(a, postSampler), Bind(a, postSampler), &across, sizeof(Vector4));
        Screen(cmd, pipeline, a, false, Bind(b, postSampler), Bind(b, postSampler), &down, sizeof(Vector4));
    }

    void AmbientOcclusionPass(SDL_GPUCommandBuffer* cmd, FrameUniforms frame, ModernScene scene, FreeCamera camera)
    {
        var s = Settings;
        var p = new Four
        {
            A = new Vector4(s.AoRadiusMetres * ModernMath.UnitsPerMetre, 0.6f, Math.Clamp(s.AoSlices, 1, 8), Math.Clamp(s.AoSteps, 1, 16)),
            // The height map's skylines start where the screen-space radius ends, so nothing is counted twice.
            B = new Vector4(s.AoStrength, 0.2f * height, 0, s.AoRadiusMetres * ModernMath.UnitsPerMetre),
        };
        if (s.SkyOcclusion && scene.Vertices != null)
        {
            var (centre, half, top, range) = HeightPass(cmd, scene, camera);
            p.B.Z = 1;
            p.C = new Vector4(centre, half, 1f / heightSize);
            p.D = new Vector4(top, range, 0, 0);
        }
        else EnsureHeightMap(1);
        var colour = new SDL_GPUColorTargetInfo { texture = aoA, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, aoPipeline);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[4];
        bindings[0] = Bind(depth, pointSampler);
        bindings[1] = Bind(normals, pointSampler);
        bindings[2] = bindings[3] = Bind(heightMap, pointSampler);
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 4);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&frame), (uint)sizeof(FrameUniforms));
        SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&p), (uint)sizeof(Four));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
        BilateralBlur(cmd, aoA, aoB, 8f, aoW, aoH, aoBlurPipeline);
    }

    /// <summary>
    /// The height map's view from above, square about the camera and snapped to its texels (so it does not
    /// swim), with what the ambient occlusion needs to read it. One function for the pass and for the count
    /// of what the pass will draw (ModernRenderer.Budget.cs).
    /// </summary>
    Matrix4x4 HeightView(ModernScene scene, FreeCamera camera, out int res, out Vector2 centre, out float half, out float top, out float range)
    {
        var s = Settings;
        res = Math.Clamp(s.HeightMapResolution, 128, 4096);
        half = s.SkyOcclusionMetres * ModernMath.UnitsPerMetre;
        float texel = 2f * half / res;
        centre = new Vector2(MathF.Round(camera.Position.X / texel) * texel, MathF.Round(camera.Position.Y / texel) * texel);
        top = scene.Max.Z + 1f; range = MathF.Max(scene.Max.Z - scene.Min.Z + 2f, 1f);
        return new Matrix4x4(
            1f / half, 0, 0, 0,
            0, 1f / half, 0, 0,
            0, 0, -1f / range, 0,
            -centre.X / half, -centre.Y / half, top / range, 1);
    }

    /// <summary>
    /// The height map: every opaque surface within <see cref="ModernSettings.SkyOcclusionMetres"/> of the
    /// camera seen from straight above, orthographically, its depth running from the scene's top down to
    /// its bottom. The square moves in whole texels with the camera, so skylines do not crawl.
    /// </summary>
    (Vector2 Centre, float Half, float Top, float Range) HeightPass(SDL_GPUCommandBuffer* cmd, ModernScene scene, FreeCamera camera)
    {
        var m = HeightView(scene, camera, out int res, out var centre, out float half, out float top, out float range);
        EnsureHeightMap(res);
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = heightMap, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, null, 0, &ds);
        BindScene(pass, scene);
        var planes = ModernMath.FrustumPlanes(m);
        SDL_BindGPUGraphicsPipeline(pass, shadowPipeline);
        PushVertex(cmd, m, Vector4.Zero);
        // Every draw near the camera, in view or not (ours): the sun's casters where the streamer
        // chose them apart from the view; before, only what was in view, so the sky's occlusion changed as the
        // camera turned.
        foreach (var d in order.Cast(scene.Casters ?? scene.Draws, scene.Source))       // the frame's opaque casters, found once (ModernDrawOrder)
        {
            var mat = scene.Material(d.Material);
            if (!ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
            Gpu.BindFragmentSampler(pass, scene.Binding(mat.BaseColorTexture, srgb: true));
            var p = new Vector4(mat.AlphaMode == GltfAlphaMode.Mask ? mat.AlphaCutoff : -1f, mat.BaseColorFactor.W, mat.BaseColorTexture.TexCoord, mat.Foliage ? 1 : 0);
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(Vector4));
            SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
        }
        SDL_EndGPURenderPass(pass);
        return (centre, half, top, range);
    }

    /// <summary>Makes the height map <paramref name="res"/> texels square (a 1-texel stand-in keeps the binding valid when it is off).</summary>
    void EnsureHeightMap(int res)
    {
        if (heightMap != null && (heightSize == res || res == 1)) return;
        Release(ref heightMap);
        heightMap = gpu.CreateTarget(res, res, shadowFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        heightSize = res;
    }

    void VolumePass(SDL_GPUCommandBuffer* cmd, FrameUniforms frame)
    {
        var p = new Four { A = new Vector4(Math.Clamp(Settings.VolumetricSteps, 4, 128), 0, 0, 0) };
        Screen(cmd, volumePipeline, volA, false, Bind(depth, pointSampler), Bind(shadowAtlas, shadowSampler), &frame, sizeof(FrameUniforms), &p, sizeof(Four));
        BilateralBlur(cmd, volA, volB, 4f, halfW, halfH);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TaaUniforms { public Matrix4x4 PrevViewProj, InvUnjittered; public Vector4 Params; }

    /// <summary>Blends this frame's picture into the history (ModernTaa.hlsl); returns the blended picture, the next frame's history.</summary>
    SDL_GPUTexture* TemporalPass(SDL_GPUCommandBuffer* cmd, FrameUniforms frame, Matrix4x4 unjittered)
    {
        int next = 1 - taaCurrent;
        Matrix4x4.Invert(unjittered, out var inverse);
        var u = new TaaUniforms { PrevViewProj = taaPrevViewProj, InvUnjittered = inverse, Params = new Vector4(0.1f, taaHistoryValid ? 1 : 0, 0, 0) };
        var colour = new SDL_GPUColorTargetInfo { texture = taaHistory[next], load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, taaPipeline);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[4];
        bindings[0] = Bind(hdr, pointSampler);
        bindings[1] = Bind(taaHistory[taaCurrent], postSampler);
        bindings[2] = Bind(depth, pointSampler);
        bindings[3] = Bind(hdr, pointSampler);
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 4);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&frame), (uint)sizeof(FrameUniforms));
        SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&u), (uint)sizeof(TaaUniforms));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
        taaCurrent = next;
        taaHistoryValid = true;
        taaPrevViewProj = unjittered;
        return taaHistory[next];
    }

    void PostPasses(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int w, int h, FreeCamera camera, float aspect, ModernLight light,
                    SDL_GPUTexture* picture)
    {
        var s = Settings;
        // The sun on the screen, for the flare: its direction through the camera's rotation and projection.
        var rotation = camera.RotationView * ModernMath.ReversedInfinitePerspective(camera.VerticalFov, aspect, camera.Near);
        var clip = Vector4.Transform(new Vector4(light.ToSun, 1f), rotation);
        bool front = clip.W > 1e-4f;
        var sunUv = front ? new Vector2(clip.X / clip.W * 0.5f + 0.5f, 0.5f - clip.Y / clip.W * 0.5f) : new Vector2(-10f);
        float ys = 1f / MathF.Tan(camera.VerticalFov / 2f), xs = ys / aspect;
        float discUv = MathF.Tan(MathF.Max(s.SunAngle, 1f) * 0.5f * MathF.PI / 180f) * xs * 0.5f;
        bool flare = s.LensFlare && s.FlareIntensity > 0f && front && depthSampled && light.ToSun.Z > -0.02f && light.SunIntensity > 0f
                     && s.SunDisc > 0f && sunUv.X > -0.5f && sunUv.X < 1.5f && sunUv.Y > -0.5f && sunUv.Y < 1.5f;
        if (flare)
        {
            var p = new Four { A = new Vector4(0, 0, 3, 0), D = default, C = new Vector4(sunUv, discUv, aspect) };
            Screen(cmd, bloomDownPipeline, sunVis, false, Bind(picture, postSampler), Bind(depth, pointSampler), &p, sizeof(Four));
        }

        int levels = 0;
        if (s.Bloom && s.BloomIntensity > 0f)
        {
            levels = BloomLevels;
            while (levels > 1 && (bloomSize[levels - 1].W < 2 || bloomSize[levels - 1].H < 2)) levels--;
            var src = picture; int sw = w, sh = h;
            for (int k = 0; k < levels; k++)
            {
                var p = new Four { A = new Vector4(1f / sw, 1f / sh, k == 0 ? 0 : 1, 0), B = new Vector4(s.BloomThreshold, s.BloomKnee, s.Exposure, 0) };
                Screen(cmd, bloomDownPipeline, bloom[k], false, Bind(src, postSampler), Bind(depth, pointSampler), &p, sizeof(Four));
                src = bloom[k]; (sw, sh) = bloomSize[k];
            }
            for (int k = levels - 1; k > 0; k--)
            {
                var p = new Four { A = new Vector4(1f / bloomSize[k].W, 1f / bloomSize[k].H, 2, 0) };
                Screen(cmd, bloomUpPipeline, bloom[k - 1], true, Bind(bloom[k], postSampler), Bind(depth, pointSampler), &p, sizeof(Four));
            }
        }

        // Tone mapping. The up-chain leaves the sum of every level in the first, each carrying the whole
        // picture's light, so the bloom is that sum over the number of levels.
        var tone = new Four
        {
            A = new Vector4(s.Exposure, s.ToneCurve == ModernToneCurve.AgX ? 1 : 0, s.Contrast, s.Saturation),
            B = new Vector4(s.Lift, s.Vignette, levels > 0 ? s.BloomIntensity : 0f,
                            flare ? s.FlareIntensity * ModernClouds.SunThrough(s.Clouds, s.NorthYaw, camera.Position, light.ToSun, groundZ) : 0f),
            C = new Vector4(sunUv, aspect, front ? 1 : 0),
            D = new Vector4(light.SunColor * light.SunIntensity, 0),
        };
        var ldrTarget = new SDL_GPUColorTargetInfo { texture = ldr, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var pass = SDL_BeginGPURenderPass(cmd, &ldrTarget, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, tonemapPipeline);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[4];
        bindings[0] = Bind(picture, postSampler);
        bindings[1] = Bind(bloom[0], postSampler);
        bindings[2] = Bind(sunVis, postSampler);
        bindings[3] = Bind(s.Debug == ModernDebugView.Shafts ? volA : s.Debug == ModernDebugView.AmbientOcclusion ? aoA : picture, postSampler);
        tone.E = new Vector4(s.Debug != ModernDebugView.None ? 1 : 0, 0, 0, 0);
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 4);
        if (levels > 0) tone.B.Z = s.BloomIntensity;
        var bloomScale = levels > 0 ? 1f / levels : 0f;
        tone.D.W = bloomScale;
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&tone), (uint)sizeof(Four));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);

        // FXAA into the target.
        var outTarget = new SDL_GPUColorTargetInfo { texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        pass = SDL_BeginGPURenderPass(cmd, &outTarget, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, fxaaPipeline);
        Gpu.BindFragmentSampler(pass, new SDL_GPUTextureSamplerBinding { texture = ldr, sampler = postSampler });
        var fx = new Vector4(1f / w, 1f / h, s.Fxaa ? 1 : 0, 0);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&fx), (uint)sizeof(Vector4));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    void DisposePost()
    {
        Release(ref aoA); Release(ref aoB); Release(ref volA); Release(ref volB); Release(ref sunVis); Release(ref heightMap);
        for (int k = 0; k < BloomLevels; k++) Release(ref bloom[k]);
        Release(ref taaHistory[0]); Release(ref taaHistory[1]);
        DisposeClouds();
        DisposeSsr(); DisposeAtmosphere();   // ours
        foreach (var p in new[] { (nint)aoPipeline, (nint)blurPipeline, (nint)aoBlurPipeline, (nint)volumePipeline, (nint)bloomDownPipeline, (nint)bloomUpPipeline, (nint)taaPipeline })
            if (p != 0) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, (SDL_GPUGraphicsPipeline*)p);
        if (lightBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, lightBuffer);
        if (shadowMatrixBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, shadowMatrixBuffer);
        Release(ref pointShadowAtlas);
        if (clusterBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, clusterBuffer);
        if (lightTransfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, lightTransfer);
    }
}
