// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws a sky (the snapshot's <see cref="ClassicSky"/>, posed each frame by the producer) the way the 1.1 renderer does: after the level's opaque geometry and
/// before the things, restricted to the sky window, with its own far plane of 100,000 (the world
/// uses the scene's), depth test on, depth writes and fog off, fully lit, groups in file order.
/// Borrows textures, samplers and pipelines from the <see cref="WorldRenderer"/>.
/// </summary>
public sealed unsafe class SkyRenderer : IDisposable
{
    /// <summary>The far clip plane the original sets while drawing the sky.</summary>
    public const float FarPlane = ClassicSky.FarPlane;

    readonly Gpu gpu;
    readonly WorldRenderer world;
    readonly ClassicSky sky;
    readonly SceneVertex[] vertices;
    readonly (int Material, uint First, uint Count)[] groups;
    readonly SDL_GPUBuffer* vertexBuffer;
    readonly SDL_GPUBuffer* indexBuffer;
    readonly SDL_GPUTransferBuffer* transfer;
    readonly uint vertexBytes;

    public ClassicSky Sky => sky;
    /// <summary>A viewer aid: draw only the sky groups whose material name contains this.</summary>
    public string? Only { get; set; }
    /// <summary>A viewer aid: leave out the sky groups whose material name contains this.</summary>
    public string? Hide { get; set; }

    /// <summary>
    /// The console's `sky off` (bit 8 of the raster options): the world frame
    /// then skips the sky's draw and clears the screen instead, so where the sky
    /// would be there is the clear colour. A global in 1.1, so it outlasts a change of place here too.
    /// </summary>
    public static bool Off { get; set; }

    /// <summary>The sky this renderer draws — which the frame compares with its snapshot's (<see cref="SkySwap"/>).</summary>
    public ClassicSky Source => sky;

    public SkyRenderer(Gpu gpu, WorldRenderer world, ClassicSky sky)
    {
        this.gpu = gpu;
        this.world = world;
        this.sky = sky;
        vertices = (SceneVertex[])sky.Vertices.Clone();
        groups = sky.Groups;
        vertexBytes = (uint)(vertices.Length * sizeof(SceneVertex));
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices);
        indexBuffer = gpu.CreateBuffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, sky.Indices);
        // A sky model with no drawable group leaves the buffers null and nothing to stage; the two
        // methods below return. See <see cref="Gpu.CreateBuffer"/> for why a zero-byte buffer is
        // not made.
        if (vertexBytes > 0)
        {
            var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = vertexBytes };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
            if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        }
    }

    /// <summary>Uploads the sky at the frame's pose (<paramref name="positions"/>, the producer's). Call outside a render pass.</summary>
    public void Upload(SDL_GPUCommandBuffer* cmd, Vector3[] positions)
    {
        if (vertexBuffer == null || transfer == null) return;
        for (int i = 0; i < vertices.Length; i++) vertices[i].Position = positions[i];
        var map = SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        fixed (SceneVertex* src = vertices) Buffer.MemoryCopy(src, (void*)map, vertexBytes, vertexBytes);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = vertexBuffer, offset = 0, size = vertexBytes };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
    }

    /// <summary>
    /// Records the sky into <paramref name="pass"/>, scissored to <paramref name="window"/>.
    /// <paramref name="viewProj"/> is the camera's rotation only (the sky rides on the eye) times
    /// a projection with <see cref="FarPlane"/>. The scissor is reset to the whole target afterwards.
    /// </summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj,
                     (int X, int Y, int W, int H) window, int targetWidth, int targetHeight)
    {
        if (vertexBuffer == null || indexBuffer == null || Off) return;
        var scissor = new SDL_Rect { x = window.X, y = window.Y, w = window.W, h = window.H };
        SDL_SetGPUScissor(pass, &scissor);
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var ib = new SDL_GPUBufferBinding { buffer = indexBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var (mat, first, count) in groups)
        {
            var m = world.Material(mat);
            if (Only is { Length: > 0 } && !m.Name.Contains(Only, StringComparison.OrdinalIgnoreCase)) continue;
            if (Hide is { Length: > 0 } && m.Name.Contains(Hide, StringComparison.OrdinalIgnoreCase)) continue;
            // The sky model is an ordinary instance: each group's material picks its mode the way
            // any model's does, with the sky's own rules on top — no fog, no depth writes, fully lit.
            var state = (RenderKinds.State(DrawPlan.ForThing(m.Traits)) ?? RenderKinds.State(RenderKind.Rgb)!)
                        with { DepthWrite = false, Fog = false };
            SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, m.TwoSided));
            // The sky model's groups wrap their textures: the renderer's "sky" mode clamps, but that
            // mode is for a level's own sky surfaces — the model is drawn as an ordinary instance.
            bindings[0] = new SDL_GPUTextureSamplerBinding { texture = world.MaterialTexture(mat), sampler = world.RepeatSampler };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            // Instance flag 2 makes the sky fully lit; render flags 2 and 4 turn fog and depth writes off.
            var prm = world.Params(state, fullyLit: true);
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, count, 1, first, 0, 0);
        }
        var full = new SDL_Rect { x = 0, y = 0, w = targetWidth, h = targetHeight };
        SDL_SetGPUScissor(pass, &full);
    }

    public void Dispose()
    {
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, indexBuffer);
    }
}
