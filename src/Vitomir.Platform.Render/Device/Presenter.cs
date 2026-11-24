// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render;

/// <summary>
/// The frame is drawn into an off-screen target and copied to the screen through the game's
/// brightness curve: output = input^(1/gamma). The original asks the display hardware for this as
/// a gamma ramp, with gamma 1.4 in its own settings (the brightness slider, clamped to 0.5–2.0,
/// stored in masquerade.ini); being a hardware ramp it never appeared in screenshots of the
/// original, so here it is part of the image instead.
/// Vitomir's default is <see cref="DefaultGamma"/> = 1 (the frame as drawn), judged the
/// closer match to the game as it is remembered; <see cref="OriginalGamma"/> gives the curve the
/// original asks for. 1.1 does default to 1.4, and 1 is kept anyway:
/// as a normal darkness it is fine and correct, and some people will prefer
/// it. Players turn it up the way they would in 1.1.
/// </summary>
public sealed unsafe class Presenter : IDisposable
{
    public const float DefaultGamma = 1.0f, OriginalGamma = 1.4f, MinGamma = 0.5f, MaxGamma = 2.0f;

    [StructLayout(LayoutKind.Sequential)]
    struct PresentParams
    {
        public float InverseGamma;
        public float Pad0, Pad1, Pad2;
    }

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* pipeline;
    readonly SDL_GPUSampler* sampler;
    SDL_GPUTexture* scene;
    int width, height;

    float gamma = DefaultGamma;

    /// <summary>The game's brightness setting; clamped to 0.5–2.0 as the original clamps it.</summary>
    public float Gamma
    {
        get => gamma;
        set => gamma = Math.Clamp(value, MinGamma, MaxGamma);
    }

    public Presenter(Gpu gpu)
    {
        this.gpu = gpu;
        sampler = gpu.CreateSampler(repeat: false, mipmaps: false);
        var vs = gpu.LoadShader("Present", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 0);
        var fs = gpu.LoadShader("Present", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 1);
        var color = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { color_target_descriptions = &color, num_color_targets = 1 },
        };
        pipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        if (pipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(Present)");
    }

    /// <summary>The off-screen target to draw the frame into, created or resized as needed.</summary>
    public SDL_GPUTexture* SceneTarget(int w, int h)
    {
        if (scene != null && width == w && height == h) return scene;
        if (scene != null) SDL_ReleaseGPUTexture(gpu.Device, scene);
        scene = gpu.CreateTarget(w, h, gpu.ColorFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        width = w; height = h;
        return scene;
    }

    /// <summary>
    /// **1.1's present** (the Classic look's): the off-screen frame copied onto the whole of <paramref name="target"/> through the brightness curve,
    /// stretched to it if their sizes differ, with no bars and no viewport of its own: the old renderer's draw as it was,
    /// so its pictures stay byte for byte. <see cref="Present(SDL_GPUCommandBuffer*, SDL_GPUTexture*, int, int)"/> is the
    /// platform's (fitted, with bars).
    /// </summary>
    public void Present(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target)
    {
        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var binding = new SDL_GPUTextureSamplerBinding { texture = scene, sampler = sampler };
        Gpu.BindFragmentSampler(pass, binding);
        var prm = new PresentParams { InverseGamma = 1f / gamma };
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(PresentParams));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    /// <summary>
    /// Where a picture of <paramref name="pictureWidth"/> × <paramref name="pictureHeight"/> lands on a window of
    /// <paramref name="windowWidth"/> × <paramref name="windowHeight"/> (ours): as large as fits, its shape
    /// kept, centred; the rest is black bars. A picture the window's own size fills it.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Fit(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || windowWidth <= 0 || windowHeight <= 0) return (0, 0, Math.Max(windowWidth, 0), Math.Max(windowHeight, 0));
        if (pictureWidth == windowWidth && pictureHeight == windowHeight) return (0, 0, windowWidth, windowHeight);
        double scale = Math.Min((double)windowWidth / pictureWidth, (double)windowHeight / pictureHeight);
        int w = Math.Min(windowWidth, (int)Math.Round(pictureWidth * scale)), h = Math.Min(windowHeight, (int)Math.Round(pictureHeight * scale));
        return ((windowWidth - w) / 2, (windowHeight - h) / 2, w, h);
    }

    /// <summary>
    /// Copies the off-screen frame onto <paramref name="target"/> (<paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/>), applying the brightness curve: scaled to fit and centred, black
    /// around it (<see cref="Fit"/>), filtered linearly when its size changes.
    /// </summary>
    public void Present(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int targetWidth, int targetHeight)
    {
        var place = Fit(width, height, targetWidth, targetHeight);
        bool whole = place == (0, 0, targetWidth, targetHeight);
        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = whole ? SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE : SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 }, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        var port = new SDL_GPUViewport { x = place.X, y = place.Y, w = place.Width, h = place.Height, min_depth = 0, max_depth = 1 };
        SDL_SetGPUViewport(pass, &port);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var binding = new SDL_GPUTextureSamplerBinding { texture = scene, sampler = sampler };
        Gpu.BindFragmentSampler(pass, binding);
        var prm = new PresentParams { InverseGamma = 1f / gamma };
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(PresentParams));
        SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    /// <summary>The off-screen frame's size (the picture's).</summary>
    public (int Width, int Height) SceneSize => (width, height);

    public void Dispose()
    {
        if (scene != null) SDL_ReleaseGPUTexture(gpu.Device, scene);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, pipeline);
    }
}
