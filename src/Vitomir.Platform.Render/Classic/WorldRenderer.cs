// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws a location: its level geometry and its things. Base texture × static lightmap, × baked
/// vertex light, or fully lit. Opaque and masked batches first (level, then things, with the sky
/// in between, as the original orders them), then translucent ones (Trans 33/66, alpha textures,
/// additive materials): the things' back to front, then the level's in rlDirect's order
/// (WorldRenderer.Translucent.cs). Materials with <c>pass</c> lines are drawn once per pass in
/// the <see cref="RenderKind"/> each pass names, with its own texture and its sliding texture
/// coordinates. Also owns the material textures and pipelines the sky borrows.
/// A thing that arrives after the place loaded is picked up by <see cref="Sync"/>, and one that
/// has left the world is passed over by <see cref="Shown"/>.
/// <para>**It reads the Classic snapshot's scene, nothing of the engine**:
/// the mesh and the materials as resources sent once, by id, and each frame's things, styles,
/// fog and posed vertices (<see cref="ClassicScene"/>, which the engine's <c>SceneSnapshot</c> fills).</para>
/// </summary>
public sealed unsafe partial class WorldRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MaterialParams
    {
        public float UseLightmap;   // 1: stage 1 samples the lightmap
        public float AlphaScale;
        public float AlphaTest;     // 0 = off, else the threshold
        public float FullyLit;      // 1: ignore lighting
        public Vector4 Fog;         // type (0 off), start, end, density
        public Vector4 FogColor;
        public Vector4 Combiner;    // stage-0 colour op, colour operand 2, stage-0 alpha op, stage-1 colour op
        public Vector4 Slide;       // texture-coordinate offset; then 1 when stage 0 uses the second set; then its scale
        public Vector4 Factor;      // the texture-factor constant
        // Ours: the see-through windows (Level.hlsl's Clearness): the eye and the surface's fade level (0: none), each
        // window's unit line and its clear cosine, their solid-again cosines, and their reaches (0: no window).
        public Vector4 HoleEye;
        public Vector4 HoleAxis0, HoleAxis1, HoleAxis2, HoleAxis3;
        public Vector4 HoleOuter;
        public Vector4 HoleFar;
    }

    /// <summary>Everything about a draw that lives in the pipeline rather than in a uniform.</summary>
    readonly record struct PipelineKey(Blend Src, Blend Dest, bool BlendEnable, bool DepthWrite,
                                       Compare DepthFunc, Cull Cull, int DepthBias, bool Lines = false);

    /// <summary>The smallest alpha the original's "greater than 0" alpha test keeps, in 8-bit terms.</summary>
    public const float AlphaTestThreshold = 1f / 255f;

    readonly Gpu gpu;
    /// <summary>The frame's scene, as the last <see cref="Sync"/> was handed it.</summary>
    ClassicScene scene = new();
    /// <summary>The mesh's batches at the revision the buffers were built from, and each one's number.</summary>
    DrawBatch[] batches = [];
    readonly Dictionary<DrawBatch, int> batchIndex = new(ReferenceEqualityComparer.Instance);
    uint indexCount;
    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUBuffer* indexBuffer;
    /// <summary>Staging for the animating things' vertices, which are re-uploaded every frame.</summary>
    SDL_GPUTransferBuffer* animTransfer;
    uint animBytes;
    /// <summary>Staging for the standing-still things relit this frame.</summary>
    SDL_GPUTransferBuffer* relitTransfer;
    uint relitBytes;
    /// <summary>The mesh revision these buffers were built from; see <see cref="Sync"/>.</summary>
    int revision = -1;
    /// <summary>One texture per image of each material (frames first, then pass textures).</summary>
    SDL_GPUTexture*[][] materialTextures;
    readonly SDL_GPUTexture*[] lightmapTextures;
    readonly SDL_GPUTexture* white;
    readonly SDL_GPUSampler* repeatSampler;
    readonly SDL_GPUSampler* clampSampler;
    readonly Dictionary<(bool Clamp, bool Mipmaps), nint> samplers = [];
    readonly Dictionary<PipelineKey, nint> pipelines = [];
    readonly SDL_GPUShader* vertexShader;
    readonly SDL_GPUShader* fragmentShader;

    /// <summary>
    /// The frame's scene (the last <see cref="Sync"/>'s): the mesh, the materials, the fog, the light styles (the place's for
    /// the world, the spare its maker drew for the heads and the portraits), and the things the batches name — each one's
    /// placement from where the scene built its geometry to where it is now (a batch names its thing by position in
    /// <c>Location.Drawn</c>, never the live list, which shrinks), alpha, fade, flags, shell and glow.
    /// </summary>
    public ClassicScene Scene => scene;
    /// <summary>The mesh's batches at the revision the buffers hold.</summary>
    public IReadOnlyList<DrawBatch> Batches => batches;
    /// <summary>How many things the batches can name this frame.</summary>
    int ThingCount => scene.ThingCount;
    public int MissingTextures { get; private set; }

    /// <summary>Textures made for materials seen for the first time, since the renderer was built (slow frames).</summary>
    public int TexturesMade { get; private set; }

    /// <summary>Whole rebuilds of the vertex and index buffers, since the renderer was built (slow frames).</summary>
    public int Rebuilds { get; private set; }
    /// <summary>Graphics pipelines made so far, one for each render state on its first use (ours,: the frame log's).</summary>
    public int PipelinesMade => pipelines.Count;
    public int Triangles => (int)(indexCount / 3);

    /// <param name="lightmaps">The level's lightmaps (a resource, sent once; none for the heads and the portraits).</param>
    public WorldRenderer(Gpu gpu, IReadOnlyList<ClassicPicture> lightmaps)
    {
        this.gpu = gpu;
        byte[] whitePx = [255, 255, 255, 255];
        white = gpu.CreateTexture(1, 1, whitePx, false);
        materialTextures = [];
        lightmapTextures = new SDL_GPUTexture*[lightmaps.Count];
        for (int i = 0; i < lightmaps.Count; i++)
            lightmapTextures[i] = gpu.CreateTexture(lightmaps[i].Width, lightmaps[i].Height, lightmaps[i].Rgba, mipmaps: false);
        repeatSampler = Sampler(clamp: false, mipmaps: true);
        clampSampler = Sampler(clamp: true, mipmaps: false);

        vertexShader = gpu.LoadShader("Level", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        fragmentShader = gpu.LoadShader("Level", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 1);
    }

    SDL_GPUSampler* Sampler(bool clamp, bool mipmaps)
    {
        if (samplers.TryGetValue((clamp, mipmaps), out var have)) return (SDL_GPUSampler*)have;
        var s = gpu.CreateSampler(repeat: !clamp, mipmaps: mipmaps);
        samplers[(clamp, mipmaps)] = (nint)s;
        return s;
    }

    SDL_GPUTexture* Checker()
    {
        var px = new byte[8 * 8 * 4];
        for (int i = 0; i < 64; i++)
        {
            bool on = ((i & 7) + (i >> 3)) % 2 == 0;
            px[i * 4] = on ? (byte)255 : (byte)40; px[i * 4 + 1] = 0; px[i * 4 + 2] = on ? (byte)255 : (byte)40; px[i * 4 + 3] = 255;
        }
        return gpu.CreateTexture(8, 8, px, false);
    }

    static SDL_GPUBlendFactor Factor(Blend b) => b switch
    {
        Blend.Zero => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
        Blend.One => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
        Blend.SrcColor => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_COLOR,
        Blend.InvSrcColor => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_COLOR,
        Blend.SrcAlpha => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
        Blend.InvSrcAlpha => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
        Blend.DestAlpha => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_ALPHA,
        Blend.InvDestAlpha => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_ALPHA,
        Blend.DestColor => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_COLOR,
        Blend.InvDestColor => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_COLOR,
        _ => SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA_SATURATE,
    };

    static SDL_GPUCompareOp CompareOp(Compare c) => c switch
    {
        Compare.Never => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NEVER,
        Compare.Less => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS,
        Compare.Equal => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL,
        Compare.LessEqual => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
        Compare.Greater => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER,
        Compare.NotEqual => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NOT_EQUAL,
        Compare.GreaterEqual => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL,
        _ => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS,
    };

    SDL_GPUGraphicsPipeline* CreatePipeline(PipelineKey key)
    {
        var vbDesc = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(SceneVertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        var attrs = stackalloc SDL_GPUVertexAttribute[4];
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 12 };
        attrs[2] = new SDL_GPUVertexAttribute { location = 2, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 20 };
        attrs[3] = new SDL_GPUVertexAttribute { location = 3, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offset = 28 };
        var blend = new SDL_GPUColorTargetBlendState();
        if (key.BlendEnable)
        {
            blend.enable_blend = true;
            blend.color_blend_op = blend.alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD;
            blend.src_color_blendfactor = blend.src_alpha_blendfactor = Factor(key.Src);
            blend.dst_color_blendfactor = blend.dst_alpha_blendfactor = Factor(key.Dest);
        }
        var color = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat, blend_state = blend };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vertexShader, fragment_shader = fragmentShader,
            vertex_input_state = new SDL_GPUVertexInputState { vertex_buffer_descriptions = &vbDesc, num_vertex_buffers = 1, vertex_attributes = attrs, num_vertex_attributes = 4 },
            primitive_type = key.Lines ? SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST   // the see-through outlines (ours)
                                       : SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = key.Cull switch
                {
                    Cull.Back => SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                    Cull.Front => SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
                    _ => SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                },
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                // The original's ZBIAS: a mode with a bias draws that many depth steps nearer.
                enable_depth_bias = key.DepthBias != 0,
                depth_bias_constant_factor = -key.DepthBias,
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = CompareOp(key.DepthFunc), enable_depth_test = true, enable_depth_write = key.DepthWrite,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &color, num_color_targets = 1, depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = true,
            },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline");
        return p;
    }

    SDL_GPUGraphicsPipeline* Pipeline(PipelineKey key)
    {
        // The mirror's view is a reflection, which turns every triangle over (the renderer's flag 8).
        if (Mirrored) key = key with { Cull = key.Cull == Cull.Back ? Cull.Front : key.Cull == Cull.Front ? Cull.Back : key.Cull };
        if (pipelines.TryGetValue(key, out var have)) return (SDL_GPUGraphicsPipeline*)have;
        var p = CreatePipeline(key);
        pipelines[key] = (nint)p;
        return p;
    }

    /// <summary>
    /// Catches the GPU up with a mesh that has grown and a material cache that has taken on new
    /// materials — which is what happens the moment anything enters the world after the place
    /// loaded: a dropped sword, a gib, a skeleton, a summoned wolf.
    ///
    /// The original needs none of this, and the shape of what it does instead is worth keeping in
    /// view. Its things are never baked into anything: each is submitted on its own, once a frame,
    /// off its sector's live linked list, so a new one draws on the next frame
    /// for free. Its model is read on a cache miss, its material likewise,
    /// and its **bitmaps are read inside the first draw that needs them** (guarded by
    /// resource flag 0x10) — a model appearing for the first time pulls its textures off disk in
    /// the frame it first appears. Level textures are the opposite: loaded and uploaded eagerly in
    /// the NIL loader, resident before the first frame.
    ///
    /// So the on-demand half is faithful; it is the one buffer per place that has to be paid for,
    /// and it is paid here, once per frame at most, rather than once per thing. Sixteen gib chunks
    /// out of one death are one rebuild.
    ///
    /// Called before <see cref="UploadAnimated"/>, and it submits its own uploads, so it wants to
    /// be outside the frame's command buffer rather than inside it. **Every fill of the scene is synced**, in order: a
    /// fill carries only the resources new since the last (<see cref="ClassicScene"/>).
    /// </summary>
    public void Sync(ClassicScene scene)
    {
        this.scene = scene;
        TakeFaded();                                                       // WorldRenderer.SeeThrough.cs (ours)
        if (scene.NewPictures.Count > 0) GrowMaterials(scene);
        if (scene.Repainted.Count > 0) UploadFilms(scene);                 // WorldRenderer.Films.cs
        var mesh = scene.Mesh;
        if (mesh.Revision == revision) return;
        revision = mesh.Revision;
        Rebuilds++;
        batches = mesh.Batches;
        batchIndex.Clear();
        for (int i = 0; i < batches.Length; i++) batchIndex[batches[i]] = i;
        var d = gpu.Device;
        var verts = mesh.Vertices; var idx = mesh.Indices;
        indexCount = (uint)idx.Length;
        // Releasing is deferred until the GPU is finished with them, so releasing a buffer the
        // last frame is still drawing from is safe.
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(d, indexBuffer);
        // An empty mesh leaves both null: the status bar's head viewport builds its renderer before
        // it has loaded a single head, and a renderer with nothing in it must be legal. Every draw
        // below checks. See <see cref="Gpu.CreateBuffer"/> for why null rather than a zero-byte
        // buffer.
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, verts);
        indexBuffer = gpu.CreateBuffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, idx);
    }

    /// <summary>
    /// Textures for the materials the cache has taken on since we last looked. The engine reads a
    /// model's bitmaps in the frame it is first drawn; this is the same moment, one
    /// step earlier in the frame.
    /// </summary>
    void GrowMaterials(ClassicScene scene)
    {
        var grown = new SDL_GPUTexture*[scene.Drawable][];
        Array.Copy(materialTextures, grown, materialTextures.Length);
        foreach (var (i, pictures) in scene.NewPictures)
        {
            grown[i] = new SDL_GPUTexture*[pictures.Length];
            for (int j = 0; j < grown[i].Length; j++)
            {
                if (pictures[j] is not { } img) { MissingTextures++; grown[i][j] = Checker(); continue; }
                grown[i][j] = gpu.CreateTexture(img.Width, img.Height, img.Rgba, mipmaps: true);
                TexturesMade++;
            }
        }
        materialTextures = grown;
    }


    /// <summary>One run of vertices into the staging buffer.</summary>
    static void Stage(byte* map, ref uint offset, ReadOnlySpan<SceneVertex> slice)
    {
        uint bytes = (uint)(slice.Length * sizeof(SceneVertex));
        fixed (SceneVertex* src = slice) Buffer.MemoryCopy(src, map + offset, bytes, bytes);
        offset += bytes;
    }

    void Blit(SDL_GPUCopyPass* copy, SDL_GPUTransferBuffer* from, ref uint offset, uint firstVertex, int count)
    {
        uint bytes = (uint)(count * sizeof(SceneVertex));
        var source = new SDL_GPUTransferBufferLocation { transfer_buffer = from, offset = offset };
        // Never cycle: these are slices of the one buffer the whole location is drawn from.
        var to = new SDL_GPUBufferRegion
        { buffer = vertexBuffer, offset = firstVertex * (uint)sizeof(SceneVertex), size = bytes };
        SDL_UploadToGPUBuffer(copy, &source, &to, false);
        offset += bytes;
    }

    /// <summary>A staging buffer of at least <paramref name="bytes"/>, made again larger when it must be.</summary>
    SDL_GPUTransferBuffer* Staging(ref SDL_GPUTransferBuffer* transfer, ref uint have, uint bytes)
    {
        if (bytes <= have && transfer != null) return transfer;
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        var info = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = bytes };
        transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &info);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        have = bytes;
        return transfer;
    }

    /// <summary>How many animating things the last frame posed, and how many it passed over, for the diagnostics.</summary>
    public (int Posed, int Passed) LastAnimated => scene.Animated;

    /// <summary>
    /// Sends up the vertices the frame's game side posed and lit (<see cref="ClassicScene.Posed"/>, then
    /// <see cref="ClassicScene.Relit"/>), leaving the rest of the buffer — the level and everything standing still — where it
    /// was. Call outside a render pass, the way the sky's upload is called. Reads the snapshot, writes only the GPU's buffers.
    /// </summary>
    public void UploadAnimated(SDL_GPUCommandBuffer* cmd)
    {
        Upload(cmd, scene.Posed, ref animTransfer, ref animBytes);
        Upload(cmd, scene.Relit, ref relitTransfer, ref relitBytes);
    }

    /// <summary>
    /// Runs of the snapshot's vertices over their slices of the vertex buffer, in one copy pass. A character and anything it
    /// is holding are separate slices — the held weapon is its own model, appended after the body — so each goes up on its own.
    /// </summary>
    void Upload(SDL_GPUCommandBuffer* cmd, List<ClassicSpan> spans, ref SDL_GPUTransferBuffer* transfer, ref uint have)
    {
        if (spans.Count == 0 || vertexBuffer == null) return;
        uint wanted = 0;
        foreach (var s in spans) wanted += (uint)(s.Count * sizeof(SceneVertex));
        var staging = Staging(ref transfer, ref have, wanted);
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, staging, true);
        uint offset = 0;
        foreach (var s in spans) Stage(map, ref offset, scene.Vertices.AsSpan(s.Offset, s.Count));
        SDL_UnmapGPUTransferBuffer(gpu.Device, staging);
        var copy = SDL_BeginGPUCopyPass(cmd);
        offset = 0;
        foreach (var s in spans) Blit(copy, staging, ref offset, s.First, s.Count);
        SDL_EndGPUCopyPass(copy);
    }

    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, Vector3 eye)
    {
        DrawOpaque(cmd, pass, viewProj, BatchLayer.Level);
        DrawOpaque(cmd, pass, viewProj, BatchLayer.Thing);
        DrawShells(cmd, pass, viewProj);
        DrawGlow(cmd, pass, viewProj);
        DrawTranslucent(cmd, pass, viewProj, eye);
    }

    /// <summary>Whether a batch belongs to a thing the frame has (its <see cref="DrawBatch.Thing"/> names one).</summary>
    bool OfThing(DrawBatch b) => b.Layer == BatchLayer.Thing && b.Thing >= 0 && b.Thing < ThingCount;

    /// <summary>
    /// The hover glow's tint: the hovered thing's batches drawn a second time in mode 28,
    /// <see cref="RenderKind.FullAddConstant"/> — the constant alone, added on top, no texture colour
    /// in it — which is the pass `rlDirect.dll` adds while the model carries the hover
    /// bit. The constant is the glow colour times its intensity (<see cref="ClassicThing.Glow"/>). The other half, the
    /// light, is in the vertex colours already: the glow raises the model's base light.
    /// </summary>
    public void DrawGlow(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (!scene.GlowOn || RenderKinds.State(RenderKind.FullAddConstant) is not { } state) return;
        if (!scene.GlowAny || !Bind(cmd, pass, viewProj)) return;
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        for (int i = 0; i < batches.Length; i++)
        {
            var b = batches[i];
            if (b.Layer != BatchLayer.Thing || b.Thing < 0 || b.Thing >= ThingCount || !Shown(i)) continue;
            ref readonly var thing = ref scene.Things[b.Thing];
            if (!thing.Glowing) continue;
            var add = thing.Glow;
            var mat = scene.Materials[b.Material];
            var m = Transform(b, viewProj);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&m), (uint)sizeof(Matrix4x4));
            SDL_BindGPUGraphicsPipeline(pass, Pipeline(state, b.TwoSided || mat.TwoSided));
            bindings[0] = new SDL_GPUTextureSamplerBinding
            {
                texture = materialTextures[b.Material][scene.CurrentImage(b.Material)],
                sampler = Sampler(state.Address == Address.Clamp, state.Mipmaps),
            };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = white, sampler = clampSampler };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            var prm = Params(state, AlphaOf(b), fullyLit: true, factor: new Vector4(add, 1f));
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, b.IndexCount, 1, b.FirstIndex, 0, 0);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        }
    }

    /// <summary>
    /// **The shells** (`SetShell` — the white flash over a character taken over, a discipline's glow):
    /// each shelled thing's batches drawn once more, as <see cref="ShellDraw"/> says `rlDirect.dll`
    /// does — the shell material's texture in its second mode, every vertex's alpha the shell's, the
    /// model's own coordinates slid (the shell's slide, and the model's own when its pass ran first). The original draws a
    /// model's passes back to back; here every shell goes after the opaque things and before the hover glow, which is the
    /// order of the passes. What a thing carries is shelled with it (the shell goes to the model's attachments too).
    /// </summary>
    public void DrawShells(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (Only is not null || !scene.AnyShells || !Bind(cmd, pass, viewProj)) return;
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        for (int i = 0; i < batches.Length; i++)
        {
            var b = batches[i];
            if (b.Layer != BatchLayer.Thing || b.Thing < 0 || b.Thing >= ThingCount || !Shown(i)) continue;
            ref readonly var thing = ref scene.Things[b.Thing];
            if (thing.Shell is not (>= 0 and var shellIndex) || shellIndex >= materialTextures.Length) continue;
            var shell = scene.Materials[shellIndex];
            if (thing.ShellDraw is not { } draw || RenderKinds.State(draw.Kind) is not { } state) continue;
            var slide = thing.HidesModel ? draw.Slide : draw.Slide + scene.Materials[b.Material].Slide0;
            var m = Transform(b, viewProj);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&m), (uint)sizeof(Matrix4x4));
            SDL_BindGPUGraphicsPipeline(pass, Pipeline(state, b.TwoSided || shell.TwoSided));
            var images = materialTextures[shellIndex];
            bindings[0] = new SDL_GPUTextureSamplerBinding
            {
                texture = images[Math.Clamp(draw.Image, 0, images.Length - 1)],
                sampler = Sampler(state.Address == Address.Clamp, state.Mipmaps && !draw.NoMip),
            };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = white, sampler = clampSampler };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            // The vertices' alpha is 1, so scaling it is replacing it.
            var prm = Params(state, draw.Alpha, b.Light == Lighting.Full, slide * scene.Seconds);
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, b.IndexCount, 1, b.FirstIndex, 0, 0);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        }
    }

    /// <summary>A thing whose shell has bit 0x80 is drawn as its shell alone (<see cref="ClassicThing.HidesModel"/>).</summary>
    bool ModelLeftOut(DrawBatch b) => OfThing(b) && scene.Things[b.Thing].HidesModel;

    /// <summary>
    /// A batch's alpha: its own, times its thing's model alpha when it belongs to one
    /// (<see cref="ClassicThing.ModelAlpha"/> — `SetAlpha`, and the MODELALPHA effect of Cloak of
    /// Shadows, Mist Form and the rest) and the stealth fade. A thing whose model alpha is below 1 is
    /// drawn with the translucent ones; one only faded is drawn in its place (<see cref="PassAlphaOf"/>).
    /// </summary>
    float AlphaOf(DrawBatch b) =>
        OfThing(b) ? b.AlphaScale * scene.Things[b.Thing].ModelAlpha * scene.Things[b.Thing].Fade : b.AlphaScale;

    /// <summary>
    /// The alpha that decides a batch's pass: <see cref="AlphaOf"/> **without the stealth fade**.
    /// The thing draw keeps back for the sorted pass only a thing whose render flags
    /// carry 0x40 or whose model alpha is under 1; the fade is
    /// multiplied in only as the model is drawn. So a half-seen character —
    /// Christof hidden, as he sees himself — is drawn in its place among the things, blended (mode 4,
    /// its depth written), not sorted in with the translucent ones at the end.
    /// </summary>
    float PassAlphaOf(DrawBatch b) => OfThing(b) ? b.AlphaScale * scene.Things[b.Thing].ModelAlpha : b.AlphaScale;

    BlendKind KindOf(DrawBatch b, ClassicMaterial m)
    {
        var kind = m.Blend;
        if (PassAlphaOf(b) < 1f && kind is BlendKind.Opaque or BlendKind.Masked) kind = BlendKind.Alpha;
        return kind;
    }

    /// <summary>Whether this batch waits for the translucent stage (<see cref="PassAlphaOf"/>).</summary>
    bool IsTranslucent(DrawBatch b) =>
        scene.HasPasses(b.Material) ? scene.PassesAreTranslucent(b.Material) || PassAlphaOf(b) < 1f
                                    : KindOf(b, scene.Materials[b.Material]) is BlendKind.Alpha or BlendKind.Additive;

    /// <summary>
    /// The matrix a batch is drawn with: the camera's, or — for a thing that has moved — the
    /// camera's times the difference between where the scene put the thing and where it is now
    /// (<see cref="ClassicThing.Placement"/>, its own scale in it).
    /// </summary>
    Matrix4x4 Transform(DrawBatch b, Matrix4x4 viewProj)
    {
        if (b.Thing < 0 || b.Thing >= ThingCount) return viewProj;
        ref readonly var thing = ref scene.Things[b.Thing];
        return thing.Placed ? thing.Placement * viewProj : viewProj;
    }

    /// <summary>
    /// Whether a batch's thing is still there to be drawn. **The mesh keeps a thing's geometry
    /// after it leaves the world** — a picked-up sword, a gib whose timer ran out, anything
    /// `Remove` destroyed — because taking it out would shift every index after it. The engine
    /// draws by walking the sector's list and `RemoveFromWorld` unlinks the
    /// thing from it, so leaving the world is exactly what stops it being drawn; this is that test.
    /// A thing whose render flags say not to draw its model is left out too
    /// (<see cref="ClassicThing.ModelHidden"/>).
    ///
    /// Level batches (<see cref="DrawBatch.Thing"/> below zero) and a caller that never said what
    /// its things were are both always drawn.
    /// </summary>
    bool Shown(int index)
    {
        var b = batches[index];
        bool ofThing = b.Thing >= 0 && b.Thing < ThingCount;
        // Drawing one thing and nothing else, which is what the status bar's head viewport wants:
        // its mesh holds every head model the party has ever had and each is drawn on its own, at
        // its own offset (`HeadsPass`). Null means the ordinary rule below.
        if (Only is { } only)
            return ofThing && scene.Things[b.Thing].Identity == only && !scene.BatchHidden[index];
        if (Leave?.Invoke(index) == true) return false;
        if (ofThing && scene.Things[b.Thing].Passed) return false;   // not posed this frame: the view cannot show it
        if (scene.Fades && ofThing && scene.Things[b.Thing].Fade <= 0f) return false;   // faded out: unseen
        // In the mirror (the thing draw's mirror rule): REFLECTION ONLY is drawn and NO REFLECTION is not.
        if (Mirrored && ofThing)
            return scene.Things[b.Thing] is { InWorld: true, Reflected: true } && !scene.BatchHidden[index];
        // And the thing's own render flags: DON'T RENDER leaves it out entirely, and
        // REFLECTION ONLY — the first-person look's bit — leaves out the model and what it holds.
        // Then a named shape hidden on its instance — the arrow off the bow, the fists under a
        // claw (the engine's NamedShapes).
        return !ofThing || scene.Things[b.Thing] is { InWorld: true, ModelHidden: false } && !scene.BatchHidden[index];
    }

    /// <summary>
    /// Draw only this thing's batches (its <see cref="ClassicThing.Identity"/>: the first in the scene's list that is it),
    /// or null for all of them. See <see cref="Shown"/>.
    /// </summary>
    public int? Only { get; set; }

    /// <summary>Batches (by number) every draw leaves out while set — the reflective groups, around the reflection pass (<see cref="ReflectionPass"/>).</summary>
    public Func<int, bool>? Leave { get; set; }

    /// <summary>Drawing the mirror's view (<see cref="ReflectionPass"/>): culling turned over and the reflection's render-flag rule.</summary>
    public bool Mirrored { get; set; }

    /// <summary>
    /// Binds the location's one vertex and index buffer. False when the mesh is empty — there is
    /// nothing to bind and nothing to draw, so every caller returns rather than recording a draw
    /// against a buffer that was never made.
    /// </summary>
    bool Bind(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (vertexBuffer == null || indexBuffer == null) return false;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var ib = new SDL_GPUBufferBinding { buffer = indexBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
        return true;
    }

    /// <summary>The opaque and masked batches of one layer.</summary>
    public void DrawOpaque(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, BatchLayer layer)
    {
        if (!Bind(cmd, pass, viewProj)) return;
        for (int i = 0; i < batches.Length; i++)
        {
            var b = batches[i];
            if (b.Layer != layer || !Shown(i) || ModelLeftOut(b)) continue;
            if (IsTranslucent(b)) continue;
            var m = Transform(b, viewProj);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&m), (uint)sizeof(Matrix4x4));
            if (DrawnElsewhere?.Invoke(i) == true) DrawDepthOnly(cmd, pass, b);   // a cell level's, lit by the modern path (ours)
            else if (FadedRange(i, out var lo, out var hi)) DrawKept(cmd, pass, b, lo, hi);   // less what is seen through (ours)
            else DrawBatch(cmd, pass, b);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        }
    }

    // DrawTranslucent: WorldRenderer.Translucent.cs (the things first, then the level's, as 1.1 and rlDirect order them).

    internal ClassicMaterial Material(int index) => scene.Materials[index];
    internal SDL_GPUTexture* MaterialTexture(int index) => materialTextures[index][scene.CurrentImage(index)];
    internal SDL_GPUTexture* White => white;
    internal SDL_GPUSampler* RepeatSampler => repeatSampler;
    internal SDL_GPUSampler* ClampSampler => clampSampler;

    /// <summary>The pipeline one mode's state asks for; a two-sided material can only widen the culling.</summary>
    internal SDL_GPUGraphicsPipeline* Pipeline(RenderKindState state, bool twoSided) =>
        Pipeline(new PipelineKey(state.SrcBlend, state.DestBlend, state.BlendEnable, state.DepthWrite,
                                 state.DepthFunc, twoSided ? Cull.None : state.Cull, state.DepthBias));

    /// <summary>The uniforms one mode's state asks for, less the parts that come from the batch.</summary>
    internal MaterialParams Params(RenderKindState state, float alphaScale = 1f, bool fullyLit = false,
                                   Vector2 slide = default, Vector4 factor = default)
    {
        static float Op(TexOp op) => (float)(int)op;
        var fog = scene.Fog;
        return new MaterialParams
        {
            UseLightmap = state.Color1.Enabled ? 1 : 0,
            AlphaScale = alphaScale,
            AlphaTest = state.AlphaTest ? AlphaTestThreshold : 0f,
            FullyLit = fullyLit ? 1 : 0,
            Fog = state.Fog ? new Vector4(fog.Type, fog.Start, fog.End, fog.Density) : Vector4.Zero,
            // An additive mode fogs towards black, so distance fades it out instead of tinting it.
            FogColor = state.FogBlack ? Vector4.Zero : new Vector4(fog.Color, 1),
            Combiner = new Vector4(Op(state.Color.Op), state.Color.Arg2 == TexArg.TextureFactor ? 1 : 0,
                                   Op(state.Alpha.Op), Op(state.Color1.Op)),
            Slide = new Vector4(slide, state.TexCoord == 1 ? 1 : 0, 1),
            Factor = factor,
        };
    }

    readonly List<DrawStep> steps = [];
    LayerLight[] layerLight = new LayerLight[8];

    /// <summary>
    /// A material with no <c>pass</c> lines: the original's own choice of mode (see
    /// <see cref="DrawPlan"/>) for each step of the surface's plan — normally one draw, but a
    /// surface whose light styles animate takes one per lightmap layer, and <c>detail</c> and
    /// <c>glow</c> materials add a draw of their own at the end.
    /// </summary>
    void DrawBatch(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, DrawBatch b)
    {
        var mat = scene.Materials[b.Material];
        if (scene.HasPasses(b.Material)) { DrawPasses(cmd, pass, b, mat); return; }
        var traits = mat.Traits;
        var styles = scene.Styles;
        if (b.Layer == BatchLayer.Thing)
        {
            // A model's material carries one mode, which the game works out from its keywords once.
            if (b.Light == Lighting.Full) traits = traits with { FullyLit = true };
            steps.Clear();
            steps.Add(new DrawStep(AlphaOf(b) < 1f && !traits.Masked ? RenderKind.Rgba : DrawPlan.ForThing(traits)));
        }
        else
        {
            if (layerLight.Length < b.Layers.Length) layerLight = new LayerLight[b.Layers.Length];
            for (int i = 0; i < b.Layers.Length; i++)
            {
                int style = b.Layers[i].Style;
                layerLight[i] = new LayerLight(style, styles.Brightness(style));
            }
            DrawPlan.ForSurface(traits, layerLight.AsSpan(0, b.Layers.Length), b.AlphaScale < 1f, steps);
        }
        bool twoSided = b.TwoSided || mat.TwoSided;
        int current = scene.CurrentImage(b.Material);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var step in steps)
        {
            var state = RenderKinds.State(step.Kind);
            if (state is null) continue;
            var texture = step.Layer >= 0 ? lightmapTextures[b.Layers[step.Layer].Page]
                        : step.Kind == RenderKind.SolidGlow ? materialTextures[b.Material][mat.GlowImage]
                        : step.Kind == RenderKind.DetailModulate ? materialTextures[b.Material][mat.DetailImage]
                        : materialTextures[b.Material][current];
            SDL_BindGPUGraphicsPipeline(pass, Pipeline(state, twoSided));
            bindings[0] = new SDL_GPUTextureSamplerBinding
            {
                texture = texture,
                sampler = Sampler(step.Layer >= 0 || state.Address == Address.Clamp, state.Mipmaps && step.Layer < 0),
            };
            bindings[1] = new SDL_GPUTextureSamplerBinding
            {
                texture = state.Color1.Enabled ? lightmapTextures[b.LightmapPage] : white, sampler = clampSampler,
            };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            // The constant colour is the brightness of the light style driving this draw: the style
            // of the lightmap layer it scales, or the material's glow ramp for a glow map.
            float bright = step.Layer >= 0 ? styles.Value(b.Layers[step.Layer].Style)
                         : step.Kind == RenderKind.SolidGlow ? styles.Value(mat.GlowRamp)
                         : 1f;
            var prm = Params(state, AlphaOf(b), b.Light == Lighting.Full || mat.FullyLit,
                             factor: new Vector4(bright, bright, bright, 1f));
            if (step.Kind == RenderKind.DetailModulate) prm.Slide.W = mat.DetailScale;
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, b.IndexCount, 1, b.FirstIndex, 0, 0);
        }
    }

    /// <summary>
    /// A material with <c>pass</c> lines: the same geometry once per pass, in the mode that pass
    /// names, with that pass's texture and its sliding texture coordinates. A pass whose mode the
    /// renderer has no state for leaves the state of the pass before it, as the original does.
    /// </summary>
    void DrawPasses(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, DrawBatch b, ClassicMaterial mat)
    {
        bool twoSided = b.TwoSided || mat.TwoSided;
        bool fullyLit = b.Light == Lighting.Full || mat.FullyLit;
        RenderKindState? previous = null;
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var p in mat.Passes)
        {
            var state = RenderKinds.State(p.Kind) ?? previous;
            if (state is null) continue;
            previous = state;
            bool lightmap = p.Image == MaterialPass.Lightmap;
            if (p.Image == MaterialPass.LightAdd) continue;         // dynamic light decals: not drawn yet
            if (lightmap && b.Light != Lighting.Lightmap) continue; // "$lightmap" on a surface without one
            var texture = lightmap ? lightmapTextures[b.LightmapPage]
                                   : materialTextures[b.Material][Math.Min(p.Image, materialTextures[b.Material].Length - 1)];
            SDL_BindGPUGraphicsPipeline(pass, Pipeline(state, twoSided));
            bindings[0] = new SDL_GPUTextureSamplerBinding
            {
                texture = texture,
                sampler = Sampler(lightmap || state.Address == Address.Clamp, state.Mipmaps && !p.NoMip && !lightmap),
            };
            bindings[1] = new SDL_GPUTextureSamplerBinding
            {
                texture = b.Light == Lighting.Lightmap ? lightmapTextures[b.LightmapPage] : white, sampler = clampSampler,
            };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            // A "$lightmap" pass samples the lightmap through stage 0, so it needs its coordinates.
            var prm = Params(state, AlphaOf(b), fullyLit, p.Slide * scene.Seconds);
            if (lightmap) prm.Slide.Z = 1;
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, b.IndexCount, 1, b.FirstIndex, 0, 0);
        }
    }

    public void Dispose()
    {
        var d = gpu.Device;
        foreach (var p in pipelines.Values) SDL_ReleaseGPUGraphicsPipeline(d, (SDL_GPUGraphicsPipeline*)p);
        foreach (var row in materialTextures)
            foreach (var t in row) SDL_ReleaseGPUTexture(d, t);
        foreach (var t in lightmapTextures) SDL_ReleaseGPUTexture(d, t);
        SDL_ReleaseGPUTexture(d, white);
        foreach (var s in samplers.Values) SDL_ReleaseGPUSampler(d, (SDL_GPUSampler*)s);
        SDL_ReleaseGPUShader(d, vertexShader);
        SDL_ReleaseGPUShader(d, fragmentShader);
        if (animTransfer != null) SDL_ReleaseGPUTransferBuffer(d, animTransfer);
        if (relitTransfer != null) SDL_ReleaseGPUTransferBuffer(d, relitTransfer);
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(d, indexBuffer);
        if (edgeBuffer != null) SDL_ReleaseGPUBuffer(d, edgeBuffer);
    }
}
