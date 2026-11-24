// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws a place's decals — the explosions' wall marks and the projectiles' impact marks
/// (the engine's <c>Decals</c>, sent in the snapshot as <see cref="ClassicDecal"/>s) — as `rlDirect.dll` draws them.
///
/// **Where**: in the surface-group draw, after the group's own surfaces (in both of the
/// draw plan's branches), the group's decal list walked in link
/// order, each decal drawn only when its surface was drawn this frame (the surface's drawn
/// stamp against the frame's). Here every surface is drawn every frame — Vitomir has no portal
/// visibility — so every decal is, and they go in one run after the level's opaque surfaces and the
/// light blobs. **Ours**: the order among decals is the table's, not each group's list's (newest
/// first); only overlapping marks in blending modes could tell.
///
/// **How**: the polygon as a fan (Direct3D's triangle fan, FVF 0x144: transformed,
/// one colour, one set of coordinates) with the decal's texture coordinates, every point given the
/// decal's colour — its red, green, blue and alpha floats packed as a byte each (the 1.5 × 2²³ trick,
/// always in range here) — in the material's own mode chosen as a particle's is (the material's second
/// mode, <see cref="DrawPlan.ForParticle"/>; a material with <c>pass</c>
/// lines goes through its pass list), with the material's layer-0 slide. The shipped marks:
/// `BulletMark1` and `ExplosionMark1` are `subblend` — <c>subparticle</c>, the frame darkened by the
/// texture times the colour, so a fading mark (its colour going to black) lightens away —
/// and `DCLBloodSpatter3` is one <c>saturate_decal</c> pass.
/// </summary>
public sealed unsafe class DecalRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;                       // vertices
    SceneVertex[] vertices = [];
    int count;

    /// <summary>A run of triangles in one material.</summary>
    readonly record struct Run(int Material, int First, int Count);
    readonly List<Run> runs = [];

    public DecalRenderer(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
    }

    /// <summary>How many decals the last upload carried.</summary>
    public int Drawn { get; private set; }

    /// <summary>Builds this frame's decal triangles and uploads them. Outside a render pass; false when there is nothing to draw.</summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicFrame frame)
    {
        count = 0;
        Drawn = 0;
        runs.Clear();
        points = frame.DecalPoints;
        var live = frame.Decals.Select(d => (Decal: d, d.Material))
                         .Where(x => x.Material >= 0 && x.Decal.Count >= 3)
                         .OrderBy(x => x.Material).ToList();
        if (live.Count == 0) return false;
        int need = 0;
        foreach (var x in live) need += (x.Decal.Count - 2) * 3;
        Ensure(need);
        int runStart = 0, runMat = -1;
        foreach (var (d, mat) in live)
        {
            if (mat != runMat)
            {
                if (count > runStart) runs.Add(new Run(runMat, runStart, count - runStart));
                runStart = count; runMat = mat;
            }
            uint color = Pack(d.Color);
            for (int i = 1; i + 1 < d.Count; i++)
            {
                Vertex(d, 0, color);
                Vertex(d, i, color);
                Vertex(d, i + 1, color);
            }
            Drawn++;
        }
        if (count > runStart) runs.Add(new Run(runMat, runStart, count - runStart));

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

    /// <summary>
    /// The decal's colour as the original renderer packs it: alpha, red, green, blue from the decal's four colour
    /// floats (red, green, blue, alpha), each rounded to a byte.
    /// </summary>
    public static uint Pack(Vector4 c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v, MidpointRounding.ToEven), 0, 255);
        return SceneVertex.FromBgra((B(c.W) << 24) | (B(c.X) << 16) | (B(c.Y) << 8) | B(c.Z));
    }

    void Vertex(ClassicDecal d, int i, uint color) =>
        vertices[count++] = new SceneVertex { Position = points[d.First + i].Position, Uv = points[d.First + i].Uv, Color = color };

    /// <summary>This frame's decals' points (the snapshot's).</summary>
    List<SceneVertex> points = [];

    void Ensure(int need)
    {
        if (need <= capacity) return;
        int size = Math.Max(1024, capacity);
        while (size < need) size *= 2;
        var dev = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(dev, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(dev, transfer);
        vertices = new SceneVertex[size];
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices);
        var info = new SDL_GPUTransferBufferCreateInfo
        { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)(size * sizeof(SceneVertex)) };
        transfer = SDL_CreateGPUTransferBuffer(dev, &info);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        capacity = size;
    }

    /// <summary>Records the decals. Inside the world's render pass, after the level's opaque surfaces.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, float seconds)
    {
        if (count == 0 || vertexBuffer == null) return;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var r in runs)
        {
            if (!world.HasMaterial(r.Material)) continue;
            var mat = world.Material(r.Material);
            if (world.Scene.HasPasses(r.Material))
            {
                foreach (var p in mat.Passes)
                {
                    if (p.Image < 0 || RenderKinds.State(p.Kind) is not { } ps) continue;
                    Record(cmd, pass, bindings, r, ps, world.MaterialImage(r.Material, p.Image), p.Slide * seconds, p.NoMip);
                }
                continue;
            }
            if (RenderKinds.State(DrawPlan.ForParticle(mat.Traits)) is not { } state) continue;
            Record(cmd, pass, bindings, r, state, world.MaterialTexture(r.Material), mat.Slide0 * seconds, false);
        }
    }

    void Record(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, SDL_GPUTextureSamplerBinding* bindings,
                Run r, RenderKindState state, SDL_GPUTexture* texture, Vector2 slide, bool noMip)
    {
        // The mode's own culling: `subparticle` culls nothing, `saturate_decal` the back.
        SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, twoSided: false));
        bindings[0] = new SDL_GPUTextureSamplerBinding
        { texture = texture, sampler = world.SamplerFor(state.Address == Address.Clamp, state.Mipmaps && !noMip) };
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        var prm = world.Params(state, slide: slide);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
        SDL_DrawGPUPrimitives(pass, (uint)r.Count, 1, (uint)r.First, 0);
    }

    public void Dispose()
    {
        var dev = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(dev, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(dev, transfer);
    }
}
