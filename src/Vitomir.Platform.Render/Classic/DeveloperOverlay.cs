// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The diagnostic overlay: text and flat quads drawn straight onto the finished frame.
//
// This is a developer tool and nothing else. It is drawn **after** the presenter, so the game's
// brightness curve does not touch it and what is on screen is what was asked for; it has its own
// plain font (see `OverlayFont.cs`) so it can never be mistaken for the HUD being rebuilt beside it; and
// the engine knows nothing about it - `Vitomir.Engine.Diagnostics.Watch` produces the words and
// the world positions, and this file is the only thing that knows they end up as triangles.
// The diagnostic overlay's drawing half (the module never draws). The laying out
// (the boxes, the text, the panels and the bars) is the module's (Vitomir.Classic's Overlay), which hands this its quads.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Classic;

/// <summary>Draws the overlay's quads (<see cref="ClassicOverlayQuads"/>). Built once with the GPU, then handed a frame's quads to draw.</summary>
public sealed unsafe class DeveloperOverlay : IClassicOverlay
{
    [StructLayout(LayoutKind.Sequential)]
    struct Params
    {
        public float Width, Height, Pad0, Pad1;
    }

    /// <summary>The atlas is the 95 printable characters in one row.</summary>
    const int Count = OverlayFont.Last - OverlayFont.First + 1;

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* pipeline;
    readonly SDL_GPUTexture* atlas;
    readonly SDL_GPUSampler* sampler;
    SDL_GPUBuffer* buffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;

    public DeveloperOverlay(Gpu gpu)
    {
        this.gpu = gpu;
        atlas = gpu.CreateTexture(Count * OverlayFont.Width, OverlayFont.Height, Atlas(), mipmaps: false);
        sampler = gpu.CreateSampler(repeat: false, mipmaps: false);

        var vs = gpu.LoadShader("Overlay", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var fs = gpu.LoadShader("Overlay", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 0);

        var attributes = stackalloc SDL_GPUVertexAttribute[3];
        attributes[0] = new SDL_GPUVertexAttribute { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };
        attributes[1] = new SDL_GPUVertexAttribute { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 8 };
        attributes[2] = new SDL_GPUVertexAttribute { location = 2, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 16 };
        var binding = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(OverlayVertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };

        // Straight alpha over whatever the presenter left on the target.
        var blend = new SDL_GPUColorTargetBlendState
        {
            enable_blend = true,
            src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
            dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
            dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
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
        pipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        if (pipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(Overlay)");
    }

    /// <summary>
    /// The font as one row of glyphs. Only the alpha channel carries anything - the shader takes
    /// the colour from the vertex - so a glyph tints to whatever the caller asked for.
    /// </summary>
    public static byte[] Atlas()
    {
        var rgba = new byte[Count * OverlayFont.Width * OverlayFont.Height * 4];
        for (int i = 0; i < Count; i++)
        {
            char c = (char)(OverlayFont.First + i);
            for (int y = 0; y < OverlayFont.Height; y++)
                for (int x = 0; x < OverlayFont.Width; x++)
                {
                    int at = ((y * Count * OverlayFont.Width) + i * OverlayFont.Width + x) * 4;
                    byte lit = OverlayFont.Lit(c, x, y) ? (byte)255 : (byte)0;
                    rgba[at] = rgba[at + 1] = rgba[at + 2] = 255;
                    rgba[at + 3] = lit;
                }
        }
        return rgba;
    }

    /// <summary>A frame's quads (the module laid them out), onto <paramref name="target"/>.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, ClassicOverlayQuads quads)
    {
        var vertices = quads.Vertices;
        if (vertices.Count == 0) return;
        Upload(cmd, vertices);

        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var vb = new SDL_GPUBufferBinding { buffer = buffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var sb = new SDL_GPUTextureSamplerBinding { texture = atlas, sampler = sampler };
        Gpu.BindFragmentSampler(pass, sb);
        var prm = new Params { Width = quads.Width, Height = quads.Height };
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(Params));
        SDL_DrawGPUPrimitives(pass, (uint)vertices.Count, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    void Upload(SDL_GPUCommandBuffer* cmd, List<OverlayVertex> vertices)
    {
        if (vertices.Count > capacity)
        {
            if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
            if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
            capacity = Math.Max(4096, vertices.Count * 2);
            uint size = (uint)(capacity * sizeof(OverlayVertex));
            var bi = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = size };
            buffer = SDL_CreateGPUBuffer(gpu.Device, &bi);
            if (buffer == null) throw new SdlException("SDL_CreateGPUBuffer(Overlay)");
            var ti = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = size };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &ti);
            if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer(Overlay)");
        }

        uint used = (uint)(vertices.Count * sizeof(OverlayVertex));
        var dst = (OverlayVertex*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        for (int i = 0; i < vertices.Count; i++) dst[i] = vertices[i];
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);

        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var region = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = used };
        SDL_UploadToGPUBuffer(copy, &src, &region, true);
        SDL_EndGPUCopyPass(copy);
    }

    public void Dispose()
    {
        if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        SDL_ReleaseGPUTexture(gpu.Device, atlas);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, pipeline);
    }
}
