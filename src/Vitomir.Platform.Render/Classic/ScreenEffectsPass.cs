// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The camera's fade and flash, drawn over the whole screen after the interface.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Puts the camera's screen effects on screen from the frame's snapshot alone (<see cref="ClassicScreen"/>, the
/// engine's <c>ScreenEffects</c> fill it after the frame's game side; <c>FrameRenderer.ScreenPass</c> (Vitomir.Render) hands it over
/// after the interface). Two full-screen quads at most, each in its
/// material's own draw mode:
///
/// <list type="bullet">
/// <item>the <b>fade</b>, `materials/General/fade.nam` — <c>subblend</c>, so
/// <c>subparticle</c>: zero + one-minus-source-colour, which <em>multiplies</em> what is there by
/// <c>1 − colour</c>. The colour is <c>255 − level</c> on every channel, so the screen
/// comes out at <c>level / 255</c> of itself — black at 0, untouched at 255;</item>
/// <item>the <b>flash</b>, `whiteFlash.nam` — <c>addblend</c>, so <c>addparticle</c>: one + one,
/// adding the flash's colour scaled by <c>intensity / 255</c>.</item>
/// </list>
///
/// Both go over <b>everything</b>, the status bar and the conversation included — the engine draws
/// them after the interface. The **filter** is not drawn: its material
/// is never set in 1.1, so
/// the original hands its draw call no material at all. What the renderer does with that is not
/// read.
/// </summary>
public sealed unsafe class ScreenEffectsPass : IClassicEffects
{
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex { public Vector2 Pos; public Vector2 Uv; public Vector4 Colour; }

    [StructLayout(LayoutKind.Sequential)]
    struct Params { public float Width, Height, Pad0, Pad1; }

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* multiply, add;
    readonly SDL_GPUSampler* sampler;
    readonly SDL_GPUTexture* white;
    readonly SDL_GPUBuffer* buffer;
    readonly SDL_GPUTransferBuffer* transfer;

    public ScreenEffectsPass(Gpu gpu)
    {
        this.gpu = gpu;
        sampler = gpu.CreateSampler(repeat: false, mipmaps: false);
        white = gpu.CreateTexture(1, 1, [255, 255, 255, 255], mipmaps: false);
        multiply = Pipeline(SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                            SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_COLOR);
        add = Pipeline(SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE, SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE);
        var bi = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = (uint)(12 * sizeof(Vertex)),
        };
        buffer = SDL_CreateGPUBuffer(gpu.Device, &bi);
        var ti = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)(12 * sizeof(Vertex)),
        };
        transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &ti);
        if (buffer == null || transfer == null) throw new SdlException("ScreenEffectsPass buffers");
    }

    SDL_GPUGraphicsPipeline* Pipeline(SDL_GPUBlendFactor src, SDL_GPUBlendFactor dst)
    {
        var vs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var fs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 0);
        var attributes = stackalloc SDL_GPUVertexAttribute[3];
        attributes[0] = new SDL_GPUVertexAttribute { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };
        attributes[1] = new SDL_GPUVertexAttribute { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 8 };
        attributes[2] = new SDL_GPUVertexAttribute { location = 2, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 16 };
        var binding = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(Vertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        var blend = new SDL_GPUColorTargetBlendState
        {
            enable_blend = true,
            src_color_blendfactor = src, dst_color_blendfactor = dst, color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
            dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
            alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
        };
        var target = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat, blend_state = blend };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            vertex_input_state = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &binding, num_vertex_buffers = 1,
                vertex_attributes = attributes, num_vertex_attributes = 3,
            },
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { color_target_descriptions = &target, num_color_targets = 1 },
        };
        var made = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        if (made == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(ScreenEffects)");
        return made;
    }

    /// <summary>The fade's colour: <c>255 − level</c> on every channel, as a byte.</summary>
    public static byte FadeByte(float level) => (byte)(int)(255f - level);

    /// <summary>The flash's colour: each channel of <paramref name="color"/> times <c>intensity / 255</c>.</summary>
    public static (byte R, byte G, byte B) FlashBytes(uint color, float intensity)
    {
        float f = intensity * (1f / 255f);
        return ((byte)(int)(((color >> 16) & 0xff) * f), (byte)(int)(((color >> 8) & 0xff) * f), (byte)(int)((color & 0xff) * f));
    }

    /// <summary>The frame's fade and flash (<paramref name="fx"/>, the snapshot's) over the whole of <paramref name="target"/>.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height, ClassicScreen fx)
    {
        if (width <= 0 || height <= 0) return;
        bool fade = fx.Fades, flash = fx.Flashes;
        if (!fade && !flash) return;

        var dst = (Vertex*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        byte v = FadeByte(fx.FadeLevel);
        Quad(dst, width, height, new Vector4(v, v, v, v) / 255f);
        var (r, g, b) = FlashBytes(fx.FlashColor, fx.FlashIntensity);
        Quad(dst + 6, width, height, new Vector4(r, g, b, Math.Clamp(fx.FlashIntensity, 0f, 255f)) / 255f);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var region = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = (uint)(12 * sizeof(Vertex)) };
        SDL_UploadToGPUBuffer(copy, &src, &region, true);
        SDL_EndGPUCopyPass(copy);

        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        var vb = new SDL_GPUBufferBinding { buffer = buffer, offset = 0 };
        var sb = new SDL_GPUTextureSamplerBinding { texture = white, sampler = sampler };
        var prm = new Params { Width = width, Height = height };
        // The engine's order: the filter, then the flash, then the fade.
        if (flash)
        {
            SDL_BindGPUGraphicsPipeline(pass, add);
            SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
            Gpu.BindFragmentSampler(pass, sb);
            SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(Params));
            SDL_DrawGPUPrimitives(pass, 6, 1, 6, 0);
        }
        if (fade)
        {
            SDL_BindGPUGraphicsPipeline(pass, multiply);
            SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
            Gpu.BindFragmentSampler(pass, sb);
            SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(Params));
            SDL_DrawGPUPrimitives(pass, 6, 1, 0, 0);
        }
        SDL_EndGPURenderPass(pass);
    }

    static void Quad(Vertex* v, int width, int height, Vector4 colour)
    {
        var a = new Vertex { Pos = new Vector2(0, 0), Uv = Vector2.Zero, Colour = colour };
        var b = new Vertex { Pos = new Vector2(width, 0), Uv = Vector2.Zero, Colour = colour };
        var c = new Vertex { Pos = new Vector2(0, height), Uv = Vector2.Zero, Colour = colour };
        var d = new Vertex { Pos = new Vector2(width, height), Uv = Vector2.Zero, Colour = colour };
        v[0] = a; v[1] = b; v[2] = c; v[3] = b; v[4] = d; v[5] = c;
    }

    public void Dispose()
    {
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, multiply);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, add);
        SDL_ReleaseGPUBuffer(gpu.Device, buffer);
        SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        SDL_ReleaseGPUTexture(gpu.Device, white);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
    }
}
