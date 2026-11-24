// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the reach highlight drawn.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The reach highlight's marks (<see cref="FrameView.Reach"/>) drawn in the world: each a short line along its lip,
/// a ribbon turned to the eye, its colour the proving ground's for the move that starts there (a lip where more
/// than one starts is dashed in each colour in turn), over a darker rim so that a yellow line reads on a yellow
/// bench. Drawn into the finished picture after tone mapping (the colours are the course's own, as seen), before
/// the overlay, tested against the scene's own depth (reversed: nearer is larger; written to nowhere), so a lip
/// behind a wall is hidden and one in front of the body shows in first and third person alike. Its width follows
/// the distance, so a line is a few pixels wide near and far.
/// </summary>
internal sealed unsafe class ReachPass : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Vertex(Vector3 pos, Vector4 colour)
    {
        public readonly Vector3 Pos = pos;
        public readonly Vector4 Colour = colour;
    }

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* pipeline;
    readonly SDL_GPUTexture* solid;
    readonly SDL_GPUSampler* sampler;
    readonly List<Vertex> vertices = [];
    SDL_GPUBuffer* buffer;
    SDL_GPUTransferBuffer* transfer;
    uint bufferBytes;

    /// <summary>The pass, its lines tested against the scene's depth, or drawn over everything when <paramref name="overAll"/> (the hit volumes, inside the bodies they outline).</summary>
    public ReachPass(Gpu gpu, bool overAll = false)
    {
        this.gpu = gpu;
        var up = new ModernDevice.Uploader(gpu);
        solid = up.Texture(1, 1, [255, 255, 255, 255], srgb: false, mips: false);
        up.Submit();
        sampler = ModernDevice.CreateSampler(gpu, false, false, false, false, 1f);

        var vs = gpu.LoadShader("ReachLines", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var fs = gpu.LoadShader("ReachLines", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 0);
        var desc = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(Vertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        var attrs = stackalloc SDL_GPUVertexAttribute[2];
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 12 };
        var blend = new SDL_GPUColorTargetBlendState
        {
            enable_blend = true,
            color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD, alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
            dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
            dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
        };
        var colour = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat, blend_state = blend };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = new SDL_GPUVertexInputState { vertex_buffer_descriptions = &desc, num_vertex_buffers = 1, vertex_attributes = attrs, num_vertex_attributes = 2 },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = overAll ? SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS : SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL,
                enable_depth_test = !overAll, enable_depth_write = false,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &colour, num_color_targets = 1, depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = true,
            },
        };
        pipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        if (pipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(reach)");
    }

    // ----- what is drawn -----

    /// <summary>The proving ground's colours (make_course.py's LEGEND, sRGB), as drawn: the move's, and a faint dark grey for out of reach.</summary>
    internal static readonly Vector4 Hop = new(0.98f, 0.82f, 0.22f, 0.95f), Hurdle = new(0.98f, 0.55f, 0.16f, 0.95f),
                                      Vault = new(0.30f, 0.52f, 0.92f, 0.95f), Mantle = new(0.62f, 0.38f, 0.85f, 0.95f),
                                      Grab = new(0.90f, 0.25f, 0.25f, 0.95f), Out = new(0.38f, 0.38f, 0.42f, 0.5f),
                                      Ladder = new(0.62f, 0.88f, 0.18f, 0.95f),   // a ladder's foot and top (the course's "ladder")
                                      RunVault = new(1.00f, 0.76f, 0.48f, 0.95f);  // a run or a sprint vaults it by itself (light orange: the course's "a run takes it")

    /// <summary>The rim under a line: near black, fainter for a faint line.</summary>
    static readonly Vector4 Rim = new(0.04f, 0.04f, 0.05f, 0.8f);

    /// <summary>A line's half width at a distance (m): about two pixels either side at 720 lines, never under a centimetre; and its rim's more.</summary>
    internal static float HalfWidth(float distance) => MathF.Max(0.01f, 0.0045f * distance);
    internal static float RimWidth(float distance) => MathF.Max(0.006f, 0.0022f * distance);

    /// <summary>The colours of a mark's moves, in a fixed order (a tap's, a sprint's, a held jump's; out of reach alone): one to each dash.</summary>
    internal static int Colours(ReachMoves moves, Span<Vector4> into)
    {
        int n = 0;
        if ((moves & ReachMoves.Hop) != 0) into[n++] = Hop;
        if ((moves & ReachMoves.Hurdle) != 0) into[n++] = Hurdle;
        if ((moves & ReachMoves.RunVault) != 0) into[n++] = RunVault;
        if ((moves & (ReachMoves.Vault | ReachMoves.ClimbOnto)) != 0) into[n++] = Vault;
        if ((moves & ReachMoves.Mantle) != 0) into[n++] = Mantle;
        if ((moves & ReachMoves.Grab) != 0) into[n++] = Grab;
        if ((moves & ReachMoves.Ladder) != 0) into[n++] = Ladder;
        if (n == 0 && (moves & ReachMoves.Out) != 0) into[n++] = Out;
        return n;
    }

    /// <summary>
    /// The triangles for <paramref name="marks"/> seen from <paramref name="eye"/> (metres), in the renderer's units
    /// (<see cref="ModernMath.UnitsPerMetre"/>): for each dash a rim and over it the line, both ribbons turned to the eye.
    /// </summary>
    internal static void Build(IReadOnlyList<ReachMark> marks, Vector3 eye, List<Vertex> into)
    {
        into.Clear();
        Span<Vector4> colours = stackalloc Vector4[8];
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            int n = Colours(m.Moves, colours);
            if (n == 0) continue;
            float d = Vector3.Distance(eye, m.Middle);
            float half = HalfWidth(d), rim = half + RimWidth(d);
            var rimColour = colours[0].W < 0.9f ? Rim with { W = 0.35f } : Rim;
            for (int k = 0; k < n; k++)
            {
                var a = Vector3.Lerp(m.A, m.B, (float)k / n);
                var b = Vector3.Lerp(m.A, m.B, (float)(k + 1) / n);
                Ribbon(a, b, eye, rim, rimColour, into);
                Ribbon(a, b, eye, half, colours[k], into);
            }
        }
    }

    /// <summary>A flat strip from <paramref name="a"/> to <paramref name="b"/>, <paramref name="half"/> either side, turned to the eye; nothing when seen end on.</summary>
    internal static void Ribbon(Vector3 a, Vector3 b, Vector3 eye, float half, Vector4 colour, List<Vertex> into)
    {
        var side = Vector3.Cross(b - a, (a + b) * 0.5f - eye);
        float l = side.Length();
        if (l < 1e-8f) return;
        side *= half / l;
        float u = ModernMath.UnitsPerMetre;
        Vertex V(Vector3 p) => new(p * u, colour);
        into.Add(V(a - side)); into.Add(V(a + side)); into.Add(V(b + side));
        into.Add(V(a - side)); into.Add(V(b + side)); into.Add(V(b - side));
    }

    /// <summary>
    /// Draws <paramref name="frame"/>'s marks into <paramref name="target"/> (<paramref name="w"/> × <paramref name="h"/>),
    /// tested against <paramref name="depth"/>, the scene's depth of the same size. Nothing when there are none.
    /// </summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.Reach.Count == 0 || depth == null || w <= 0 || h <= 0) return;
        Build(frame.Reach, frame.View.Eye, vertices);
        Draw(cmd, target, depth, w, h, frame.View, vertices);
    }

    /// <summary>
    /// Draws the triangles <paramref name="built"/> (made by <see cref="Build"/>, or by another overlay in the world:
    /// The earlier build's <see cref="AwarenessPass"/>) seen from <paramref name="view"/>, as the marks are drawn.
    /// </summary>
    internal void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, ViewPoint view, List<Vertex> built)
    {
        if (built.Count == 0 || depth == null || w <= 0 || h <= 0) return;
        Upload(cmd, built);
        var camera = FreeCamera.From(view);
        var viewProj = ModernRenderer.ViewProjection(camera, (float)w / h);
        var colour = new SDL_GPUColorTargetInfo { texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, &ds);
        var port = new SDL_GPUViewport { x = 0, y = 0, w = w, h = h, min_depth = 0, max_depth = 1 };
        SDL_SetGPUViewport(pass, &port);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var vb = new SDL_GPUBufferBinding { buffer = buffer };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        Gpu.BindFragmentSampler(pass, new SDL_GPUTextureSamplerBinding { texture = solid, sampler = sampler });
        SDL_DrawGPUPrimitives(pass, (uint)built.Count, 1, 0, 0);
        SDL_EndGPURenderPass(pass);
    }

    void Upload(SDL_GPUCommandBuffer* cmd, List<Vertex> vertices)
    {
        uint bytes = (uint)(vertices.Count * sizeof(Vertex));
        if (bytes > bufferBytes)
        {
            if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
            if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
            bufferBytes = Math.Max(bytes * 2, 4096u);
            var binfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = bufferBytes };
            buffer = SDL_CreateGPUBuffer(gpu.Device, &binfo);
            var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = bufferBytes };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
            if (buffer == null || transfer == null) throw new SdlException("SDL_CreateGPUBuffer (reach)");
        }
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices)).CopyTo(new Span<byte>(map, (int)bytes));
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = bytes };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
    }

    public void Dispose()
    {
        if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        SDL_ReleaseGPUTexture(gpu.Device, solid);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, pipeline);
    }
}
