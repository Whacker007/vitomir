// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.cs at b4185ef, reworked for the platform.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The modern path's world renderer (ours). It draws
/// a <see cref="ModernScene"/> from a <see cref="FreeCamera"/>:
/// <list type="number">
/// <item>the scene's lights sorted into clusters on the CPU and uploaded (<see cref="ModernLights"/>);</item>
/// <item>the sun's shadow maps: up to four cascades, each a quarter of one depth atlas;</item>
/// <item>a prepass of depth and normals for every opaque and cut-out surface;</item>
/// <item>at half resolution, ambient occlusion (ModernAo.hlsl) and the sun's shafts through the haze
/// (ModernVolume.hlsl), each smoothed by a depth-aware blur;</item>
/// <item>the surfaces into a linear HDR target with reversed, infinite depth (there is no far clip),
/// physically based (ModernPbr.hlsl), each pixel shaded once (the depth must equal the prepass's), then
/// the sky where nothing was drawn, then blended surfaces back to front — all through the same height
/// fog and the shafts' in-scatter;</item>
/// <item>bloom (a chain of halvings and back) and the sun's visibility for the lens flare;</item>
/// <item>tone mapping: bloom and flare, exposure, ACES or AgX, sRGB encoding, a grade;</item>
/// <item>FXAA into the target.</item>
/// </list>
/// Each draw is culled by its bounding box against the camera's frustum, and each shadow cascade's
/// casters against the cascade's. Nothing of 1.1's is involved: 1.1's levels keep drawing through <see cref="WorldRenderer"/>.
/// </summary>
internal sealed unsafe partial class ModernRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct FrameUniforms
    {
        public Vector4 CameraPos, CameraForward, CameraRight, CameraUp, SunDir, SunColor, SkyZenith, SkyHorizon, Ground, SkyGlow, SkyGlowDir,
                       Fog, FogColour, FogSun, Splits, ShadowParams, CascadeTexel, ShadowSoft, Screen, Options, Cluster, ClusterDepth;
        public fixed float ShSky[36];
        public fixed float ShBounce[36];
        public Matrix4x4 ViewProj, InvViewProj, Cascade0, Cascade1, Cascade2, Cascade3;
        public Vector4 CloudShape, CloudMotion, CloudLight;   // the cumulus (ModernClouds)
        public Vector4 Atmo;   // the physically based sky (ModernAtmosphere; ours)
        public Vector4 ProbeOrigin, ProbeGrid, ProbeDims;   // the light probes (ModernProbes; ours)
        public fixed float ProbeRooms[8 * ModernProbeRoom.Most];   // the rooms in them (ModernProbeRoom; ours): origin and cosine each, then size and sine each
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MaterialUniforms
    {
        public Vector4 BaseColorFactor, MrFactors, Emissive, TexSets, Flags;
        public Vector4 Relief, Leaf;   // ours: parallax, foliage, the large-scale variation (ModernPbr.hlsl)
        public Vector4 Tiling;         // ours: hex tiling (ModernHex.hlsli)
    }

    /// <summary>The surfaces' and shadows' vertex constants: the view-projection and, ours, the foliage's sway (ModernFoliage.hlsli).</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct VertexUniforms
    {
        public Matrix4x4 ViewProj;
        public Vector4 Sway;
    }

    /// <summary>The prepass's constants: the cut-out and, ours, the roughness that marks surfaces for screen-space reflections.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct CutUniforms
    {
        public Vector4 Cut, Rough;
        public Vector4 Tiling;   // ours: the roughness read through hex tiling's cells
    }

    /// <summary>The surface shader's sampler slots (ModernPbr.hlsl): the material's five maps, the shadows, occlusion, shafts, the lights' cubes (twice), the height map, the atmosphere's three tables, the light probes and a spare.</summary>
    const int SurfaceSamplers = 16;

    void PushVertex(SDL_GPUCommandBuffer* cmd, Matrix4x4 viewProj, Vector4 sway)
    {
        var u = new VertexUniforms { ViewProj = viewProj, Sway = sway };
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&u), (uint)sizeof(VertexUniforms));
    }

    readonly Gpu gpu;
    readonly SDL_GPUTextureFormat outputFormat, shadowFormat;
    readonly SDL_GPUGraphicsPipeline*[] surface = new SDL_GPUGraphicsPipeline*[4];   // [blend * 2 + doubleSided]
    readonly SDL_GPUGraphicsPipeline*[] prepass = new SDL_GPUGraphicsPipeline*[2];   // [doubleSided]
    readonly SDL_GPUGraphicsPipeline* shadowPipeline, shadowPipelineCulled, skyPipeline, tonemapPipeline, fxaaPipeline;
    readonly SDL_GPUSampler* shadowSampler, postSampler, pointSampler;
    readonly bool depthSampled;
    SDL_GPUTexture* hdr, depth, ldr, normals, shadowAtlas;
    int width, height, atlasSize;
    float groundZ;   // the haze's base, which the clouds stand over (the flare's look through them)

    public ModernSettings Settings { get; }

    /// <summary>How many of the scene's draws the last frame's view found inside its frustum (the rest were culled).</summary>
    public int LastDrawn { get; private set; }
    /// <summary>How many point and spot lights the last frame shaded.</summary>
    public int LastLights { get; private set; }
    /// <summary>
    /// The last frame's depth (reversed: nearer is larger; the size it was drawn at), kept after the frame, for what
    /// is drawn into the finished picture in the world (ours, the reach highlight). Null before the first frame.
    /// </summary>
    internal SDL_GPUTexture* SceneDepth => depth;
    const SDL_GPUTextureFormat HdrFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;
    const SDL_GPUTextureFormat LdrFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
    const SDL_GPUTextureFormat NormalFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
    /// <summary>Where the shadow cascades start (units): the renderer's own near plane, kept whatever a view's, so the shadows do not change with it.</summary>
    const float CascadeNear = FreeCamera.DefaultNear;

    /// <param name="outputFormat">The format of the targets <see cref="Render"/> draws into: the swapchain's, or RGBA8 headless.</param>
    public ModernRenderer(Gpu gpu, ModernSettings settings, SDL_GPUTextureFormat outputFormat)
    {
        this.gpu = gpu;
        this.outputFormat = outputFormat;
        Settings = settings;
        ModernCloudNoise.Prepare();   // the clouds' noise, built while the level loads (ours)
        shadowFormat = ModernDevice.ShadowDepthFormat(gpu);
        shadowSampler = ModernDevice.CreateShadowSampler(gpu);
        postSampler = gpu.CreateSampler(repeat: false, mipmaps: false);
        pointSampler = ModernDevice.CreatePointSampler(gpu);
        depthSampled = ModernDevice.CanSampleDepth(gpu, gpu.DepthFormat);

        var vs = ModernDevice.LoadShader(gpu, "ModernPbr", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 1);
        var fs = ModernDevice.LoadShader(gpu, "ModernPbr", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, SurfaceSamplers, 2, 3);
        for (int blend = 0; blend < 2; blend++)
            for (int two = 0; two < 2; two++)
                surface[blend * 2 + two] = SurfacePipeline(vs, fs, blend == 1, two == 1);
        // The prepass shares the shading pass's vertex shader, so their depths are bit for bit the same.
        var pfs = ModernDevice.LoadShader(gpu, "ModernPrepass", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 1);
        for (int two = 0; two < 2; two++) prepass[two] = PrepassPipeline(vs, pfs, two == 1);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs); SDL_ReleaseGPUShader(gpu.Device, pfs);

        vs = ModernDevice.LoadShader(gpu, "ModernShadow", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 1);
        fs = ModernDevice.LoadShader(gpu, "ModernShadow", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 1);
        shadowPipeline = ShadowPipeline(vs, fs, cull: false);
        shadowPipelineCulled = ShadowPipeline(vs, fs, cull: true);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);

        vs = ModernDevice.LoadShader(gpu, "ModernSky", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 1);
        fs = ModernDevice.LoadShader(gpu, "ModernSky", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 8, 1);
        skyPipeline = FullScreen(vs, fs, HdrFormat, depthTest: true);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);

        vs = ModernDevice.LoadShader(gpu, "ModernTonemap", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
        fs = ModernDevice.LoadShader(gpu, "ModernTonemap", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 4, 1);
        tonemapPipeline = FullScreen(vs, fs, LdrFormat, depthTest: false);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);

        vs = ModernDevice.LoadShader(gpu, "ModernFxaa", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
        fs = ModernDevice.LoadShader(gpu, "ModernFxaa", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 1);
        fxaaPipeline = FullScreen(vs, fs, outputFormat, depthTest: false);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);

        CreatePostPipelines();
    }

    // ----- pipelines -----

    static SDL_GPUVertexInputState VertexInput(SDL_GPUVertexBufferDescription* desc, SDL_GPUVertexAttribute* attrs)
    {
        *desc = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(ModernVertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 12 };
        attrs[2] = new SDL_GPUVertexAttribute { location = 2, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 24 };
        attrs[3] = new SDL_GPUVertexAttribute { location = 3, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 40 };
        attrs[4] = new SDL_GPUVertexAttribute { location = 4, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 48 };
        attrs[5] = new SDL_GPUVertexAttribute { location = 5, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offset = 56 };
        return new SDL_GPUVertexInputState { vertex_buffer_descriptions = desc, num_vertex_buffers = 1, vertex_attributes = attrs, num_vertex_attributes = 6 };
    }

    static SDL_GPURasterizerState Rasterizer(bool doubleSided) => new()
    {
        fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
        cull_mode = doubleSided ? SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE : SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
        front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
    };

    SDL_GPUGraphicsPipeline* SurfacePipeline(SDL_GPUShader* vs, SDL_GPUShader* fs, bool blend, bool doubleSided)
    {
        var desc = stackalloc SDL_GPUVertexBufferDescription[1];
        var attrs = stackalloc SDL_GPUVertexAttribute[6];
        var b = new SDL_GPUColorTargetBlendState();
        if (blend)
        {
            b.enable_blend = true;
            b.color_blend_op = b.alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD;
            b.src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA;
            b.dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
            b.src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE;
            b.dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
        }
        var color = new SDL_GPUColorTargetDescription { format = HdrFormat, blend_state = b };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = VertexInput(desc, attrs),
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = Rasterizer(doubleSided),
            // Opaque surfaces shade only where the prepass left their own depth; blended ones test against
            // it (reversed depth: nearer is larger) and write none.
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = blend ? SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL : SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL,
                enable_depth_test = true, enable_depth_write = false,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &color, num_color_targets = 1, depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = true,
            },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(ModernPbr)");
        return p;
    }

    SDL_GPUGraphicsPipeline* PrepassPipeline(SDL_GPUShader* vs, SDL_GPUShader* fs, bool doubleSided)
    {
        var desc = stackalloc SDL_GPUVertexBufferDescription[1];
        var attrs = stackalloc SDL_GPUVertexAttribute[6];
        var color = new SDL_GPUColorTargetDescription { format = NormalFormat };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = VertexInput(desc, attrs),
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = Rasterizer(doubleSided),
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL, enable_depth_test = true, enable_depth_write = true,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &color, num_color_targets = 1, depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = true,
            },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(ModernPrepass)");
        return p;
    }

    SDL_GPUGraphicsPipeline* ShadowPipeline(SDL_GPUShader* vs, SDL_GPUShader* fs, bool cull)
    {
        var desc = stackalloc SDL_GPUVertexBufferDescription[1];
        var attrs = stackalloc SDL_GPUVertexAttribute[6];
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = VertexInput(desc, attrs),
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = cull ? SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK : SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                // A slope-scaled bias for the surfaces the sun grazes; the receiver's normal offset does the rest.
                // No clamp: a clamp needs Vulkan's depthBiasClamp feature, which SDL does not turn on (the
                // validation layer's VUID-…-00754), and 0.01 of a cascade's depth was metres, never reached.
                enable_depth_bias = true, depth_bias_constant_factor = 1f, depth_bias_slope_factor = 1.5f, depth_bias_clamp = 0f,
                enable_depth_clip = false,
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL, enable_depth_test = true, enable_depth_write = true,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { num_color_targets = 0, depth_stencil_format = shadowFormat, has_depth_stencil_target = true },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(ModernShadow)");
        return p;
    }

    SDL_GPUGraphicsPipeline* FullScreen(SDL_GPUShader* vs, SDL_GPUShader* fs, SDL_GPUTextureFormat format, bool depthTest, bool additive = false)
    {
        var b = new SDL_GPUColorTargetBlendState();
        if (additive)
        {
            b.enable_blend = true;
            b.color_blend_op = b.alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD;
            b.src_color_blendfactor = b.dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE;
            b.src_alpha_blendfactor = b.dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE;
        }
        var color = new SDL_GPUColorTargetDescription { format = format, blend_state = b };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                // The sky is at depth 0, the far end of reversed depth: it shows only where the clear value still stands.
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL, enable_depth_test = depthTest, enable_depth_write = false,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &color, num_color_targets = 1,
                depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = depthTest,
            },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(modern full screen)");
        return p;
    }

    // ----- targets -----

    void Targets(int w, int h)
    {
        if (hdr == null || w != width || h != height || Settings.AoFullResolution != aoFull)
        {
            Release(ref hdr); Release(ref depth); Release(ref ldr); Release(ref normals);
            var colourUse = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            hdr = gpu.CreateTarget(w, h, HdrFormat, colourUse);
            ldr = gpu.CreateTarget(w, h, LdrFormat, colourUse);
            normals = gpu.CreateTarget(w, h, NormalFormat, colourUse);
            depth = gpu.CreateTarget(w, h, gpu.DepthFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                                                          | (depthSampled ? SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER : 0));
            width = w; height = h;
            PostTargets(w, h);
        }
        int atlas = Settings.ShadowResolution * 2;   // four cascades' quarters, whether or not all are used
        if (shadowAtlas == null || atlas != atlasSize)
        {
            Release(ref shadowAtlas);
            shadowAtlas = gpu.CreateTarget(atlas, atlas, shadowFormat,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
            atlasSize = atlas;
        }
    }

    void Release(ref SDL_GPUTexture* t)
    {
        if (t != null) SDL_ReleaseGPUTexture(gpu.Device, t);
        t = null;
    }

    // ----- the frame -----

    /// <summary>The camera's view-projection with reversed, infinite depth.</summary>
    public static Matrix4x4 ViewProjection(FreeCamera camera, float aspect) =>
        camera.View * ModernMath.ReversedInfinitePerspective(camera.VerticalFov, aspect, camera.Near);

    /// <summary>
    /// The view-projection shifted by (<paramref name="jx"/>, <paramref name="jy"/>) in normalised device
    /// units, for temporal anti-aliasing: every point moves by the same fraction of a pixel.
    /// </summary>
    public static Matrix4x4 Jittered(FreeCamera camera, float aspect, float jx, float jy)
    {
        var proj = ModernMath.ReversedInfinitePerspective(camera.VerticalFov, aspect, camera.Near);
        // clip.x += jx · clip.w, and clip.w is −z in view space (the projection's M34 is −1).
        proj.M31 = -jx; proj.M32 = -jy;
        return camera.View * proj;
    }

    /// <summary>The frame's draws in the passes' orders, in lists kept from frame to frame (ours).</summary>
    readonly ModernDrawOrder order = new();

    /// <summary>Draws <paramref name="scene"/> from <paramref name="camera"/> into <paramref name="target"/>
    /// (<paramref name="w"/> × <paramref name="h"/>, in the output format) on <paramref name="cmd"/>.</summary>
    public void Render(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int w, int h, ModernScene scene, FreeCamera camera)
    {
        Targets(w, h);
        float aspect = (float)w / h;
        var light = ModernLight.From(Settings);
        var unjittered = ViewProjection(camera, aspect);
        var viewProj = unjittered;
        if (Settings.Taa)
        {
            taaFrame++;
            var j = (ModernMath.Halton(taaFrame % 8 + 1) - new Vector2(0.5f)) * 0.8f;
            viewProj = Jittered(camera, aspect, j.X * 2f / w, j.Y * 2f / h);
        }
        var planes = ModernMath.FrustumPlanes(unjittered);
        // What the camera sees, sorted for the passes, in lists that are kept from frame to frame (ours; ModernDrawOrder).
        order.See(scene.Draws, scene.Source, planes, camera.Position);
        var seen = order.Seen;
        LastDrawn = seen.Count;

        // 1. The lights, sorted into clusters and uploaded.
        LastLights = Settings.Lights ? UploadLights(cmd, scene, camera, aspect, w, h) : 0;
        var frame = FrameFor(scene, camera, aspect, light, viewProj, w, h, out var cascades, out int count);

        // SDL's Direct3D 12 driver has one heap of 2,048 sampler descriptors for the frame and writes past its
        // end: a frame that would is not drawn, and the run stops with the reason (ours;
        // ModernRenderer.Budget.cs). Never so on Vulkan.
        bool sun = Settings.Shadows && count > 0 && light.ToSun.Z > 0.01f && light.SunIntensity > 0f;
        if (!SamplersFit(scene, camera, seen, planes, cascades, sun ? count : 0)) return;

        // 2. The sun's shadow maps.
        if (sun)
            ShadowPass(cmd, scene, cascades, count);
        else frame.ShadowParams.W = 0;

        // The shadowed lights' cubes, drawn again only when the lights that cast shadows change (the scene is static).
        if (shadowed.Count > 0 && !PointShadowsCurrent(scene)) PointShadowPass(cmd, scene);

        // The atmosphere's tables, when the sun or the eye's height has changed (ours; ModernRenderer.Atmosphere.cs).
        UpdateAtmosphere(cmd, light, camera, frame.Fog.Z);
        // The light probes' light, when the sky has changed (ours; ModernRenderer.Probes.cs).
        UpdateProbes(cmd, scene, light, ref frame);

        // 3. Depth and normals.
        Prepass(cmd, scene, seen, viewProj, planes);
        CloudPass(cmd, ref frame);   // the clouds at half resolution (ours, ModernRenderer.Clouds.cs)

        // 4. Ambient occlusion and the sun's shafts, at half resolution.
        bool ao = Settings.AmbientOcclusion && depthSampled;
        bool shafts = Settings.Volumetric && depthSampled && frame.Fog.X > 0f && light.ToSun.Z > 0f && light.SunIntensity > 0f;
        frame.Options.X = ao ? 1 : 0;
        frame.CameraPos.W = shafts ? 1 : 0;
        if (ao) AmbientOcclusionPass(cmd, frame, scene, camera);
        if (shafts) VolumePass(cmd, frame);

        // 5. The screen's reflections on smooth surfaces, traced into the previous frame's picture (ours;
        // ModernRenderer.Ssr.cs); then the surfaces, which read them, the sky, the blended surfaces.
        if (SsrOn) SsrPass(cmd, ref frame); else ssrDrawn = false;
        MainPass(cmd, scene, camera, aspect, seen, viewProj, frame, planes);

        // 6. Temporal anti-aliasing: the frame blended into its history.
        var picture = hdr;
        if (Settings.Taa) picture = TemporalPass(cmd, frame, unjittered);
        else taaHistoryValid = false;
        SsrKeep(picture, unjittered);

        // 7–9. Bloom and the sun's visibility, tone mapping, FXAA.
        PostPasses(cmd, target, w, h, camera, aspect, light, picture);
    }

    void BindScene(SDL_GPURenderPass* pass, ModernScene scene)
    {
        var vb = new SDL_GPUBufferBinding { buffer = scene.Vertices };
        var ib = new SDL_GPUBufferBinding { buffer = scene.Indices };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
    }

    void Prepass(SDL_GPUCommandBuffer* cmd, ModernScene scene, List<ModernScene.Draw> seen, Matrix4x4 viewProj, Vector4[] planes)
    {
        var colour = new SDL_GPUColorTargetInfo
        {
            texture = normals, clear_color = new SDL_FColor { r = 0.5f, g = 0.5f, b = 1, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, clear_depth = 0, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, &ds);
        if (scene.Vertices != null)
        {
            BindScene(pass, scene);
            var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
            for (int side = 0; side < 2; side++)
            {
                var group = order.Opaque[side];
                if (group.Count == 0) continue;
                SDL_BindGPUGraphicsPipeline(pass, prepass[side]);
                foreach (var d in group)
                {
                    var m = scene.Material(d.Material);
                    PushVertex(cmd, viewProj, SwayFor(m));
                    bindings[0] = scene.Binding(m.BaseColorTexture, srgb: true);
                    bindings[1] = scene.Binding(m.MetallicRoughnessTexture, srgb: false);
                    SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
                    var p = new CutUniforms
                    {
                        Cut = new Vector4(m.AlphaMode == GltfAlphaMode.Mask ? m.AlphaCutoff : -1f, m.BaseColorFactor.W, m.BaseColorTexture.TexCoord, m.Foliage ? 1 : 0),
                        Rough = ReflectionMark(m),
                        Tiling = TilingFor(m),
                    };
                    SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(CutUniforms));
                    SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
                }
            }
        }
        ExtraPrepass(cmd, pass, viewProj, planes);   // a cell level's characters (ours; ModernRenderer.Extra.cs)
        SDL_EndGPURenderPass(pass);
    }

    void MainPass(SDL_GPUCommandBuffer* cmd, ModernScene scene, FreeCamera camera, float aspect, List<ModernScene.Draw> seen,
                  Matrix4x4 viewProj, FrameUniforms frame, Vector4[] planes)
    {
        var cloudNoise = stackalloc SDL_GPUTextureSamplerBinding[2];
        CloudNoiseBindings(cmd, cloudNoise);   // before the pass begins: the first frame copies the noise up
        var colour = new SDL_GPUColorTargetInfo
        {
            texture = hdr, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, &ds);
        var shared = stackalloc SDL_GPUTextureSamplerBinding[8];
        shared[0] = new SDL_GPUTextureSamplerBinding { texture = shadowAtlas, sampler = shadowSampler };
        shared[1] = new SDL_GPUTextureSamplerBinding { texture = aoA, sampler = postSampler };
        shared[2] = new SDL_GPUTextureSamplerBinding { texture = volA, sampler = postSampler };
        shared[3] = new SDL_GPUTextureSamplerBinding { texture = pointShadowAtlas, sampler = shadowSampler };
        AtmosphereBindings(shared + 4);   // the sky's view, the aerial perspective's light and transmittance (ours)
        shared[7] = ProbeBinding(scene);  // the light probes (ours)
        var storage = stackalloc SDL_GPUBuffer*[3];
        storage[0] = lightBuffer; storage[1] = clusterBuffer; storage[2] = shadowMatrixBuffer;
        if (scene.Vertices != null)
        {
            BindScene(pass, scene);
            // Opaque and cut-out, grouped by pipeline and material.
            for (int side = 0; side < 2; side++)
            {
                var group = order.Opaque[side];
                if (group.Count == 0) continue;
                BindSurface(cmd, pass, surface[side], viewProj, frame, storage);
                foreach (var d in group) DrawSurface(cmd, pass, scene, d, shared, blended: false);
            }
        }
        ExtraSurfaces(cmd, pass, viewProj, frame, storage, shared, planes);   // a cell level's characters (ours)
        // The sky, where nothing was drawn.
        SDL_BindGPUGraphicsPipeline(pass, skyPipeline);
        var rotation = camera.RotationView * ModernMath.ReversedInfinitePerspective(camera.VerticalFov, aspect, camera.Near);
        Matrix4x4.Invert(rotation, out var inverse);
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&inverse), (uint)sizeof(Matrix4x4));
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&frame), (uint)sizeof(FrameUniforms));
        var skyBindings = stackalloc SDL_GPUTextureSamplerBinding[8];
        skyBindings[0] = shared[2];
        skyBindings[1] = SkyClouds(shared[2]);
        skyBindings[2] = shared[4]; skyBindings[3] = shared[5]; skyBindings[4] = skyBindings[5] = shared[6];
        skyBindings[6] = cloudNoise[0]; skyBindings[7] = cloudNoise[1];   // the clouds' noise (ours)
        SDL_BindGPUFragmentSamplers(pass, 0, skyBindings, 8);
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        // Blended surfaces, farthest first.
        if (scene.Vertices != null)
        {
            var blended = order.Blended;
            if (blended.Count > 0)
            {
                BindScene(pass, scene);
                bool? bound = null;
                foreach (var d in blended)
                {
                    bool two = scene.Material(d.Material).DoubleSided;
                    if (bound != two) { BindSurface(cmd, pass, surface[2 + (two ? 1 : 0)], viewProj, frame, storage); bound = two; }
                    DrawSurface(cmd, pass, scene, d, shared, blended: true);
                }
            }
        }
        SDL_EndGPURenderPass(pass);
    }

    void BindSurface(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, SDL_GPUGraphicsPipeline* pipeline, Matrix4x4 viewProj,
                     FrameUniforms frame, SDL_GPUBuffer** storage)
    {
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        surfaceViewProj = viewProj;
        PushVertex(cmd, viewProj, Vector4.Zero);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&frame), (uint)sizeof(FrameUniforms));
        SDL_BindGPUFragmentStorageBuffers(pass, 0, storage, 3);
    }

    void DrawSurface(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, ModernScene scene, ModernScene.Draw d,
                     SDL_GPUTextureSamplerBinding* shared, bool blended)
    {
        var m = scene.Material(d.Material);
        PushVertex(cmd, surfaceViewProj, SwayFor(m));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[SurfaceSamplers];
        bindings[0] = scene.Binding(m.BaseColorTexture, srgb: true);
        bindings[1] = scene.Binding(m.MetallicRoughnessTexture, srgb: false);
        bindings[2] = scene.Binding(m.NormalTexture, srgb: false, normal: true);
        bindings[3] = scene.Binding(m.OcclusionTexture, srgb: false);
        bindings[4] = scene.Binding(m.EmissiveTexture, srgb: true);
        bindings[5] = shared[0]; bindings[6] = shared[1]; bindings[7] = shared[2]; bindings[8] = bindings[9] = shared[3];
        bindings[10] = scene.Binding(ReliefOrWeights(m), srgb: false);
        bindings[11] = shared[4]; bindings[12] = shared[5]; bindings[13] = shared[6];
        bindings[14] = shared[7]; bindings[15] = SsrBinding(shared[1]);   // the screen's reflections (ours)
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, SurfaceSamplers);
        var u = new MaterialUniforms
        {
            BaseColorFactor = m.BaseColorFactor,
            MrFactors = new Vector4(m.MetallicFactor, m.RoughnessFactor, m.NormalScale, m.OcclusionTexture.Present ? m.OcclusionStrength : 0f),
            Emissive = new Vector4(m.EmissiveFactor * m.EmissiveStrength, ModernWater.Flag(m)),   // w: open water (ours)
            TexSets = new Vector4(m.BaseColorTexture.TexCoord, m.MetallicRoughnessTexture.TexCoord, m.NormalTexture.TexCoord, m.OcclusionTexture.TexCoord),
            Flags = new Vector4(m.EmissiveTexture.TexCoord, m.NormalTexture.Present ? 1 : 0, blended ? 1 : 0, m.SpecularFactor),
            Relief = ReliefFor(m),
            Leaf = new Vector4(m.Foliage ? 1 : 0, m.Translucency, MacroFor(m) ? 1 : 0, m.HeightTexture.TexCoord),
            Tiling = TilingFor(m),
        };
        SDL_PushGPUFragmentUniformData(cmd, 1, (nint)(&u), (uint)sizeof(MaterialUniforms));
        SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
    }

    void ShadowPass(SDL_GPUCommandBuffer* cmd, ModernScene scene, Matrix4x4[] cascades, int count)
    {
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = shadowAtlas, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, null, 0, &ds);
        if (scene.Vertices != null)
        {
            BindScene(pass, scene);
            int res = Settings.ShadowResolution;
            for (int c = 0; c < count; c++)
            {
                var port = new SDL_GPUViewport { x = (c & 1) * res, y = (c >> 1) * res, w = res, h = res, min_depth = 0, max_depth = 1 };
                SDL_SetGPUViewport(pass, &port);
                var box = new SDL_Rect { x = (c & 1) * res, y = (c >> 1) * res, w = res, h = res };
                SDL_SetGPUScissor(pass, &box);
                var lvp = cascades[c];
                var planes = ModernMath.FrustumPlanes(lvp);
                // The cascade's casters by sidedness, the sidedness of the first one met first (ModernDrawOrder.Within).
                int firstSide = order.Within(scene.Casters ?? scene.Draws, scene.Source, planes);
                for (int turn = 0; turn < 2 && firstSide >= 0; turn++)
                {
                    int side = turn == 0 ? firstSide : 1 - firstSide;
                    var group = order.Cascade[side];
                    if (group.Count == 0) continue;
                    // Single-sided surfaces cast from their front faces only, so a closed mesh's back does not shadow its own front.
                    SDL_BindGPUGraphicsPipeline(pass, side == 1 ? shadowPipeline : shadowPipelineCulled);
                    foreach (var d in group)
                    {
                        var m = scene.Material(d.Material);
                        PushVertex(cmd, lvp, SwayFor(m));
                        Gpu.BindFragmentSampler(pass, scene.Binding(m.BaseColorTexture, srgb: true));
                        var p = new Vector4(m.AlphaMode == GltfAlphaMode.Mask ? m.AlphaCutoff : -1f, m.BaseColorFactor.W, m.BaseColorTexture.TexCoord, m.Foliage ? 1 : 0);
                        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(Vector4));
                        SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
                    }
                }
            }
        }
        ExtraShadows(cmd, pass, cascades, count);   // a cell level's characters cast too (ours)
        SDL_EndGPURenderPass(pass);
    }

    FrameUniforms FrameFor(ModernScene scene, FreeCamera camera, float aspect, ModernLight light, Matrix4x4 viewProj, int w, int h,
                           out Matrix4x4[] cascades, out int count)
    {
        var s = Settings;
        var forward = camera.Forward;
        // Shadows reach the scene's farthest corner from the camera, capped.
        float far = 0;
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? scene.Min.X : scene.Max.X, (i & 2) == 0 ? scene.Min.Y : scene.Max.Y, (i & 4) == 0 ? scene.Min.Z : scene.Max.Z);
            far = MathF.Max(far, Vector3.Distance(p, camera.Position));
        }
        float cap = s.ShadowDistance > 0 ? s.ShadowDistance : 250f * ModernMath.UnitsPerMetre;
        far = Math.Clamp(far, CascadeNear * 20, cap);
        count = scene.Empty ? 0 : Math.Clamp(s.Cascades, 1, 4);
        var splits = ModernMath.CascadeSplits(CascadeNear, far, Math.Max(count, 1));
        cascades = new Matrix4x4[4];
        var texel = new float[4];
        for (int c = 0; c < count; c++)
        {
            cascades[c] = ModernMath.FitCascade(camera.Position, forward, camera.Up, camera.VerticalFov, aspect, splits[c], splits[c + 1],
                                                light.ToSun, s.ShadowResolution, scene.Min, scene.Max);
            // One texel's world size: the projection's x scale is 2 / width.
            texel[c] = 2f / new Vector3(cascades[c].M11, cascades[c].M21, cascades[c].M31).Length() / s.ShadowResolution;
        }
        float visibility = s.HazeVisibilityMetres;
        float extinction = visibility > 0 ? 3.912f / (visibility * ModernMath.UnitsPerMetre) : 0f;
        float falloff = 1f / (MathF.Max(s.HazeHeightMetres, 1f) * ModernMath.UnitsPerMetre);
        float sunCos = MathF.Cos(MathF.Max(s.SunAngle, 0.1f) * 0.5f * MathF.PI / 180f);
        float ys = 1f / MathF.Tan(camera.VerticalFov / 2f), xs = ys / aspect;
        var (gx, gy, gz) = ModernLights.Grid(w, h);
        Matrix4x4.Invert(viewProj, out var inverse);
        var glow = ModernAmbient.GlowDirection(s);
        var f = new FrameUniforms
        {
            CameraPos = new Vector4(camera.Position, 0),
            CameraForward = new Vector4(forward, far),
            CameraRight = new Vector4(camera.Right, xs),
            // The eye's own up, square to the view (FreeCamera.Up is the world's +Z unless rolled): the
            // shaders' view rays (ViewRay: the shafts, TAA's sky, the half-resolution clouds) and ambient
            // occlusion's view-space normals need it; with +Z they leaned whenever the camera pitched.
            CameraUp = new Vector4(Vector3.Normalize(Vector3.Cross(camera.Right, forward)), ys),
            SunDir = new Vector4(light.ToSun, light.SunIntensity),
            SunColor = new Vector4(light.SunColor, sunCos),
            SkyZenith = new Vector4(light.Zenith, Math.Clamp(s.CloudCover, 0f, 1f)),
            SkyHorizon = new Vector4(light.Horizon, light.Gradient ? 1 : 0),
            Ground = new Vector4(light.Ground, s.SunDisc),
            SkyGlow = new Vector4(s.SkyGlow, 0),
            SkyGlowDir = new Vector4(glow, 0),
            Fog = new Vector4(extinction, falloff, s.HazeBase ?? scene.Min.Z, 1f),
            FogColour = new Vector4(s.HazeColour ?? light.Horizon, Math.Clamp(s.HazeAnisotropy, -0.95f, 0.95f)),
            FogSun = new Vector4(s.HazeSunScale, camera.Near, s.Taa ? taaFrame % 64 : 0, s.SpecularAntiAliasing ? 1 : 0),
            Splits = new Vector4(splits.Length > 1 ? splits[1] : far, splits.Length > 2 ? splits[2] : far, splits.Length > 3 ? splits[3] : far, splits.Length > 4 ? splits[4] : far),
            ShadowParams = new Vector4(1f / (s.ShadowResolution * 2), 1.5f, count, count > 0 ? 1 : 0),
            CascadeTexel = new Vector4(texel[0], texel[1], texel[2], texel[3]),
            ShadowSoft = new Vector4(MathF.Tan(s.SunAngle * 0.5f * MathF.PI / 180f) * 3f * ModernMath.UnitsPerMetre, 0, 0, 0),
            Screen = new Vector4(w, h, 1f / w, 1f / h),
            Options = new Vector4(0, LastLights, 1, LastLights > 0 ? 1 : 0),
            Cluster = new Vector4(gx, gy, gz, ModernLights.SliceScale),
            ClusterDepth = new Vector4(ModernLights.ClusterNear, ModernLights.TileSize, 1f / (6 * pointShadowRes), pointShadowSlots),
            ViewProj = viewProj, InvViewProj = inverse,
            Cascade0 = cascades[0], Cascade1 = cascades[1], Cascade2 = cascades[2], Cascade3 = cascades[3],
        };
        (f.CloudShape, f.CloudMotion, f.CloudLight) = ModernClouds.Uniforms(s.Clouds, s.NorthYaw);
        f.Atmo = AtmosphereUniforms(light, camera, f.Fog.Z);   // the physically based sky (ours)
        groundZ = f.Fog.Z;
        var sky = ModernAmbient.Sky(light, s);
        var bounce = ModernAmbient.Bounce(light, s, sky);
        (frameSky, frameBounce) = (sky, bounce);
        for (int k = 0; k < 9; k++)
        {
            f.ShSky[k * 4] = sky[k].X; f.ShSky[k * 4 + 1] = sky[k].Y; f.ShSky[k * 4 + 2] = sky[k].Z;
            f.ShBounce[k * 4] = bounce[k].X; f.ShBounce[k * 4 + 1] = bounce[k].Y; f.ShBounce[k * 4 + 2] = bounce[k].Z;
        }
        return f;
    }

    /// <summary>Renders one frame to an RGBA8 image, top row first (headless stills and tests).</summary>
    public byte[] RenderToImage(ModernScene scene, FreeCamera camera, int w, int h)
    {
        var target = gpu.CreateTarget(w, h, outputFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        // With temporal anti-aliasing a still is several frames of its one view, the history converging.
        // Without it, two when the screen's reflections are on: they read the frame before.
        int frames = Settings.Taa ? Math.Clamp(Settings.TaaStillFrames, 1, 64) : SsrOn ? 2 : 1;
        for (int i = 0; i < frames; i++)
        {
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            Render(cmd, target, w, h, scene, camera);
            // One frame at a time: a big scene's frames queued together can exhaust a software GPU.
            var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
            SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
            SDL_ReleaseGPUFence(gpu.Device, fence);
        }
        var pixels = gpu.Download(target, w, h);
        SDL_ReleaseGPUTexture(gpu.Device, target);
        return pixels;
    }

    /// <summary>
    /// Renders <paramref name="frames"/> frames of one view, waiting for each: the median time a frame
    /// takes on this GPU, in milliseconds (the median, so a busy moment on a shared machine does not
    /// count; the cost of each option is the difference it makes). A first frame warms the pipelines up
    /// and is not counted.
    /// </summary>
    public double TimeFrames(ModernScene scene, FreeCamera camera, int w, int h, int frames)
    {
        var target = gpu.CreateTarget(w, h, outputFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        var times = new List<double>();
        for (int i = -1; i < Math.Max(frames, 1); i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            Render(cmd, target, w, h, scene, camera);
            var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
            SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
            SDL_ReleaseGPUFence(gpu.Device, fence);
            if (i >= 0) times.Add(clock.Elapsed.TotalMilliseconds);
        }
        SDL_ReleaseGPUTexture(gpu.Device, target);
        times.Sort();
        return times[times.Count / 2];
    }

    public void Dispose()
    {
        Release(ref hdr); Release(ref depth); Release(ref ldr); Release(ref normals); Release(ref shadowAtlas);
        DisposePost();
        foreach (var p in surface) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, p);
        foreach (var p in prepass) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, p);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, shadowPipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, shadowPipelineCulled);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, skyPipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, tonemapPipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, fxaaPipeline);
        SDL_ReleaseGPUSampler(gpu.Device, shadowSampler);
        SDL_ReleaseGPUSampler(gpu.Device, postSampler);
        SDL_ReleaseGPUSampler(gpu.Device, pointSampler);
        DisposeProbes();
    }
}
