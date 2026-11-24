// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws the dynamic lights on the walls: every light blob the engine's <c>DynamicLights</c> gathered this
/// frame (the snapshot's <see cref="ClassicBlob"/>s, laid on their surfaces by the producer), as `rlDirect.dll` draws one (from the surface-group draw).
///
/// **A blob is the surface drawn again, with the light's texture laid over it.** Each vertex of the
/// surface keeps its position; its texture coordinates are its place in the plane measured from the
/// foot of the light and scaled so that the blob texture spans the circle where a sphere of the
/// light's intensity cuts the plane (<c>DynamicLights.BlobUv</c>); every vertex carries one
/// colour, the light's colour times what is left of its intensity at the plane over 392, packed
/// without a clamp (<c>DynamicLights.BlobColor</c>). It is drawn in mode 29, `lightblob`:
/// one + one, depth tested and not written, back faces culled, no fog, clamped, z-bias 2 — with the
/// light's own material, or the engine's `lightblob` when it has none.
///
/// **Where the light goes into the surface: ours in method.** The original draws the blob in the
/// middle of the surface's own plan (the surface's plan has a slot for it): the lightmap
/// layers, then the blobs, then the texture multiplied over both (`fullmodulate`) — so the wall's
/// texture colours the torchlight, and the frame buffer clamps light + blob before the texture
/// multiplies. Here the surface is drawn as it always is and the blob goes over it afterwards with
/// the wall's texture multiplied in through the second stage — texture × blob added to texture ×
/// light. The two agree wherever light + blob stays under full white, and ours is brighter where it
/// does not. That plan has no slot for a masked, alpha-textured, fully lit or Trans 33/66 surface
/// (the plan's first test: material bits 0x20, 0x40, 0x1000 and group bits 0x6000) nor for a
/// material with `pass` lines and no `$lightadd` pass — no shipped material has one — so those get
/// no blob, here or there.
/// </summary>
public sealed unsafe class LightBlobRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;                       // vertices
    SceneVertex[] vertices = [];
    int count;

    /// <summary>
    /// A run of triangles drawn with one blob texture over one surface material; <c>Seen</c> when
    /// its surfaces are in sectors the portal walk reached (the engine's <c>DynamicLights.SurfaceSeen</c>).
    /// </summary>
    readonly record struct Run(int Blob, int Surface, int First, int Count, bool Seen);
    readonly List<Run> runs = [];

    public LightBlobRenderer(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
    }

    /// <summary>How many blobs the last upload carried.</summary>
    public int Blobs { get; private set; }

    /// <summary>
    /// Builds this frame's blob triangles and uploads them. Outside a render pass. False when there is nothing to draw. The
    /// blobs are this frame's, gathered from the walk by the frame's game side before the draw (WorldFrame.Frame), as
    /// 1.1 does in the world draw, and laid on their surfaces by the snapshot's producer.
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicFrame frame)
    {
        count = 0;
        Blobs = 0;
        runs.Clear();
        var blobs = frame.Blobs;
        if (blobs.Count == 0) return false;
        var points = frame.BlobPoints;
        int need = 0;
        foreach (var b in blobs) need += (b.Count - 2) * 3;
        Ensure(need);
        // Group by blob texture and wall material, so each run is one draw.
        var ordered = blobs.Where(x => x.Blob >= 0 && x.Surface >= 0)
            .OrderBy(x => x.Blob).ThenBy(x => x.Surface).ThenBy(x => !x.Seen);
        int runStart = 0, runBlob = -1, runSurf = -1;
        bool runSeen = true;
        foreach (var blob in ordered)
        {
            if (blob.Blob != runBlob || blob.Surface != runSurf || blob.Seen != runSeen)
            {
                if (count > runStart) runs.Add(new Run(runBlob, runSurf, runStart, count - runStart, runSeen));
                runStart = count; runBlob = blob.Blob; runSurf = blob.Surface; runSeen = blob.Seen;
            }
            // The surface's own fan, in the order the level mesh has it (SceneMesh.AddLevel).
            for (int i = 1; i + 1 < blob.Count; i++)
            {
                Vertex(points[blob.First], blob.Color);
                Vertex(points[blob.First + i], blob.Color);
                Vertex(points[blob.First + i + 1], blob.Color);
            }
            Blobs++;
        }
        if (count > runStart) runs.Add(new Run(runBlob, runSurf, runStart, count - runStart, runSeen));
        if (count == 0) return false;

        uint bytes = (uint)(count * sizeof(SceneVertex));
        var map = SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        fixed (SceneVertex* src = vertices) Buffer.MemoryCopy(src, (void*)map, bytes, bytes);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = vertexBuffer, offset = 0, size = bytes };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
        return true;
    }

    /// <summary>A point of the surface with the blob's coordinates (the light's texture) and the wall's own, in the blob's colour.</summary>
    void Vertex(SceneVertex point, uint color) => vertices[count++] = point with { Color = color };

    void Ensure(int need)
    {
        if (need <= capacity) return;
        int size = Math.Max(4096, capacity);
        while (size < need) size *= 2;
        var d = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(d, transfer);
        vertices = new SceneVertex[size];
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices);
        var info = new SDL_GPUTransferBufferCreateInfo
        { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)(size * sizeof(SceneVertex)) };
        transfer = SDL_CreateGPUTransferBuffer(d, &info);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        capacity = size;
    }

    /// <summary>
    /// Records the blobs. Inside the world's render pass, after the level's opaque surfaces. Only
    /// those on surfaces of sectors the walk reached, unless <paramref name="all"/> (the mirror's
    /// draw, which is not cut to a walk here).
    /// </summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, bool all = false)
    {
        if (count == 0 || vertexBuffer == null || RenderKinds.State(RenderKind.LightBlob) is not { } state) return;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, twoSided: false));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var r in runs)
        {
            if (!world.HasMaterial(r.Blob) || !world.HasMaterial(r.Surface) || !(r.Seen || all)) continue;
            bindings[0] = new SDL_GPUTextureSamplerBinding { texture = world.MaterialTexture(r.Blob), sampler = world.SamplerFor(true, false) };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.MaterialTexture(r.Surface), sampler = world.SamplerFor(false, true) };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            var prm = world.Params(state);
            prm.UseLightmap = 1;
            prm.Combiner.W = (float)(int)TexOp.Modulate;           // × the wall's texture (ours: see the summary)
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
            SDL_DrawGPUPrimitives(pass, (uint)r.Count, 1, (uint)r.First, 0);
        }
    }

    public void Dispose()
    {
        var d = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(d, transfer);
    }
}
