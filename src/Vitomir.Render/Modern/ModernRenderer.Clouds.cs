// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using static SDL.SDL3;

namespace Vitomir.Render.Modern;

/// <summary>
/// The cumulus at half resolution (ours): after the prepass, a
/// half-size target gets each pixel's clouds, marched with steps placed by a per-pixel dither
/// (<c>ModernCloudPass.hlsl</c>), and the sky reads it through a small tent filter instead of marching
/// every pixel itself. It takes out the fine streaks that a march with the same steps for every pixel
/// leaves on clouds seen near the horizon, and costs a quarter of the marching. The target is made again
/// with the others when the picture changes size.
/// </summary>
public sealed unsafe partial class ModernRenderer
{
    SDL_GPUGraphicsPipeline* cloudPipeline;
    SDL_GPUTexture* cloudTex;

    /// <summary>Whether the last frame's sky read the half-resolution clouds (for tests and the bench).</summary>
    public bool LastCloudPass { get; private set; }

    void CreateCloudPipeline()
    {
        var vs = ModernDevice.LoadShader(gpu, "ModernCloudPass", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
        var fs = ModernDevice.LoadShader(gpu, "ModernCloudPass", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 4, 1);
        cloudPipeline = FullScreen(vs, fs, HdrFormat, depthTest: false);
        SDL_ReleaseGPUShader(gpu.Device, vs); SDL_ReleaseGPUShader(gpu.Device, fs);
    }

    void CloudTargets()
    {
        Release(ref cloudTex);
        cloudTex = gpu.CreateTarget(halfW, halfH, HdrFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
    }

    /// <summary>
    /// The half-resolution clouds, when the frame has clouds and the setting asks for them
    /// (<see cref="ModernCloudSettings.HalfResolution"/>); sets the frame's flag the sky reads.
    /// </summary>
    void CloudPass(SDL_GPUCommandBuffer* cmd, ref FrameUniforms frame)
    {
        LastCloudPass = Settings.Clouds.HalfResolution && depthSampled && frame.CloudShape.X < 1f;
        // 1: the sky's 4 × 4 box filter; 2, under temporal anti-aliasing, whose frames average the turning
        // dither: a 2 × 2 one, sharper.
        frame.SkyGlowDir.W = LastCloudPass ? (Settings.Taa ? 2 : 1) : 0;
        if (!LastCloudPass) return;
        var f = frame;
        var colour = new SDL_GPUColorTargetInfo { texture = cloudTex, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[4];
        bindings[0] = bindings[1] = Bind(depth, pointSampler);
        CloudNoiseBindings(cmd, bindings + 2);   // before the pass begins: the first frame copies the noise up
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, cloudPipeline);
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 4);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&f), (uint)sizeof(FrameUniforms));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    SDL_GPUTexture* cloudShapeTex, cloudDetailTex;
    SDL_GPUSampler* wrapSampler;

    /// <summary>
    /// The clouds' shape and detail noise (ours; <see cref="ModernCloudNoise"/>): two tiling 3D textures,
    /// built on the CPU once per process and copied up once per renderer, read through a wrapping sampler.
    /// </summary>
    void CloudNoiseBindings(SDL_GPUCommandBuffer* cmd, SDL_GPUTextureSamplerBinding* at)
    {
        if (cloudShapeTex == null)
        {
            wrapSampler = gpu.CreateSampler(repeat: true, mipmaps: false);
            cloudShapeTex = Upload3DRgba8(cmd, ModernCloudNoise.Shape, ModernCloudNoise.ShapeSize);
            cloudDetailTex = Upload3DRgba8(cmd, ModernCloudNoise.Detail, ModernCloudNoise.DetailSize);
        }
        at[0] = new SDL_GPUTextureSamplerBinding { texture = cloudShapeTex, sampler = wrapSampler };
        at[1] = new SDL_GPUTextureSamplerBinding { texture = cloudDetailTex, sampler = wrapSampler };
    }

    SDL_GPUTexture* Upload3DRgba8(SDL_GPUCommandBuffer* cmd, byte[] data, int n)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_3D, format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
            usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER, width = (uint)n, height = (uint)n, layer_count_or_depth = (uint)n,
            num_levels = 1, sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
        };
        var tex = SDL_CreateGPUTexture(gpu.Device, &info);
        if (tex == null) throw new SdlException("SDL_CreateGPUTexture (cloud noise)");
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)data.Length };
        var transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (cloud noise)");
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, false);
        data.AsSpan().CopyTo(new Span<byte>(map, data.Length));
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = 0, pixels_per_row = (uint)n, rows_per_layer = (uint)n };
        var dst = new SDL_GPUTextureRegion { texture = tex, w = (uint)n, h = (uint)n, d = (uint)n };
        SDL_UploadToGPUTexture(copy, &src, &dst, false);
        SDL_EndGPUCopyPass(copy);
        SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        return tex;
    }

    /// <summary>The sky's second texture: the half-resolution clouds when this frame has them.</summary>
    SDL_GPUTextureSamplerBinding SkyClouds(SDL_GPUTextureSamplerBinding otherwise) =>
        LastCloudPass ? Bind(cloudTex, postSampler) : otherwise;

    void DisposeClouds()
    {
        Release(ref cloudTex);
        Release(ref cloudShapeTex); Release(ref cloudDetailTex);
        if (wrapSampler != null) SDL_ReleaseGPUSampler(gpu.Device, wrapSampler);
        wrapSampler = null;
        if (cloudPipeline != null) SDL_ReleaseGPUGraphicsPipeline(gpu.Device, cloudPipeline);
        cloudPipeline = null;
    }
}
