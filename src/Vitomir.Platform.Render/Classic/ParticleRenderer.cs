// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws a place's particles: every sprite the engine's <c>ParticleSystem</c> collected this frame (the snapshot's
/// <see cref="ClassicParticles"/>),
/// as `rlDirect.dll`'s particle entry draws it (called from each
/// kind's draw).
///
/// **A sprite is a square facing the camera.** Its four corners are its centre ± its size along
/// the camera's right and ± its size along the camera's up (the camera matrix's rows 0 and 2),
/// so a size is half the square's width; nothing turns it or stretches it. The
/// corners are, in order, (−right −up), (−right +up), (+right +up), (+right −up) with texture
/// coordinates (0,0), (0,1), (1,1), (1,0) and the sprite's colour on all four, drawn as two
/// triangles (0,1,2) and (0,2,3). The renderer transforms them itself and hands Direct3D
/// screen-space vertices (FVF 0x144); here the level shader does the transforming.
///
/// **In the material's second mode.** The renderer draws a material with no <c>pass</c> lines in the
/// mode of its layer, the second — <see cref="DrawPlan.ForParticle"/>: <c>addparticle</c> for an
/// <c>addblend</c> material like the fire sprites, <c>blendparticle</c> for a plain one like the
/// smoke — with its current texture and its layer-0 slide. Both modes test depth and do not write
/// it, and neither culls. A material with <c>pass</c> lines goes through the pass list (whose
/// details are not known): ours draws each of its passes in turn, as a surface's are drawn. No material a built
/// kind uses has any.
///
/// **Last in the world pass, unsorted.** The world draw does the particles after every surface and thing,
/// emitter by emitter in the order of the live list, each emitter's particles in array order — no
/// sort by distance, so a far flame drawn after a near one can show through it where the modes
/// blend. Here they go after the translucent batches.
///
/// **A trail** (slot 0x20) is a ribbon: each segment gives
/// two vertices, its start at u 0 and its end at u 1, both at v = its life, and each two consecutive
/// segments are joined by the triangles (2i, 2i+1, 2i+3) and (2i, 2i+3, 2i+2) — which is one quad
/// each, so the trails go through the same quad buffer as the sprites, after them.
///
/// **A shock ring or a pad** is a square in its
/// own frame's X/Y plane, laid out by the engine side as a quad; **a rain streak** is
/// laid out here, because its width runs along the camera's right. All
/// three entries pick the material's mode the same way.
///
/// It borrows the world renderer's pipelines, material textures and samplers, as
/// <see cref="WalkTargetRenderer"/> does, so it needs no shader of its own.
/// </summary>
public sealed unsafe class ParticleRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUBuffer* indexBuffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;                   // sprites
    SceneVertex[] vertices = [];
    readonly List<ClassicParticleBatch> batches = [];
    int count;
    int spriteCount;
    int quadCount;

    public ParticleRenderer(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
    }

    /// <summary>How many squares the last upload carried: the sprites, the quads and the rain's streaks.</summary>
    public int Count => count;

    /// <summary>
    /// Builds this frame's squares for <paramref name="camera"/> and uploads them. Outside a render
    /// pass, as a copy pass has to be. False when there is nothing to draw.
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicParticles? particles, Matrix4x4 view)
    {
        count = 0;
        batches.Clear();
        if (particles is null || particles.Sprites.Count + particles.Quads.Count + particles.Streaks.Count == 0) return false;
        var sprites = particles.Sprites;
        var quads = particles.Quads;
        var streaks = particles.Streaks;
        Ensure(sprites.Count + quads.Count + streaks.Count);
        // The camera's right and up in the world: the view matrix's first two columns.
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        for (int i = 0; i < sprites.Count; i++)
        {
            var s = sprites[i];
            var r = right * s.Size;
            var u = up * s.Size;
            uint color = SceneVertex.FromBgra(s.Color);
            int v = i * 4;
            vertices[v] = new SceneVertex { Position = s.Position - r - u, Uv = new Vector2(0, 0), Color = color };
            vertices[v + 1] = new SceneVertex { Position = s.Position - r + u, Uv = new Vector2(0, 1), Color = color };
            vertices[v + 2] = new SceneVertex { Position = s.Position + r + u, Uv = new Vector2(1, 1), Color = color };
            vertices[v + 3] = new SceneVertex { Position = s.Position + r - u, Uv = new Vector2(1, 0), Color = color };
        }
        // The quads (trails, rings, shocks, pads) come after the sprites, laid out already.
        for (int j = 0; j < quads.Count; j++)
        {
            var q = quads[j];
            int v = (sprites.Count + j) * 4;
            vertices[v] = new SceneVertex { Position = q.P0, Uv = q.T0, Color = SceneVertex.FromBgra(q.C0) };
            vertices[v + 1] = new SceneVertex { Position = q.P1, Uv = q.T1, Color = SceneVertex.FromBgra(q.C1) };
            vertices[v + 2] = new SceneVertex { Position = q.P2, Uv = q.T2, Color = SceneVertex.FromBgra(q.C2) };
            vertices[v + 3] = new SceneVertex { Position = q.P3, Uv = q.T3, Color = SceneVertex.FromBgra(q.C3) };
        }
        // The rain's streaks last: the drop (0,0), the drop + width × right (1,0), the tip
        // + width × right (1,1), the tip (0,1) — the camera's right is the same row the sprites use.
        for (int k = 0; k < streaks.Count; k++)
        {
            var st = streaks[k];
            uint c = SceneVertex.FromBgra(st.Color);
            var w = right * st.Width;
            int v = (sprites.Count + quads.Count + k) * 4;
            vertices[v] = new SceneVertex { Position = st.Position, Uv = new Vector2(0, 0), Color = c };
            vertices[v + 1] = new SceneVertex { Position = st.Position + w, Uv = new Vector2(1, 0), Color = c };
            vertices[v + 2] = new SceneVertex { Position = st.Tip + w, Uv = new Vector2(1, 1), Color = c };
            vertices[v + 3] = new SceneVertex { Position = st.Tip, Uv = new Vector2(0, 1), Color = c };
        }
        spriteCount = sprites.Count;
        quadCount = quads.Count;
        count = sprites.Count + quads.Count + streaks.Count;
        batches.AddRange(particles.Batches);

        uint bytes = (uint)(count * 4 * sizeof(SceneVertex));
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

    /// <summary>Room for at least <paramref name="sprites"/>: the buffers double until they fit.</summary>
    void Ensure(int sprites)
    {
        if (sprites <= capacity) return;
        int size = Math.Max(1024, capacity);
        while (size < sprites) size *= 2;
        var d = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(d, indexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(d, transfer);
        vertices = new SceneVertex[size * 4];
        var indices = new uint[size * 6];
        for (uint q = 0; q < size; q++)
        {
            uint b = q * 4, o = q * 6;
            (indices[o], indices[o + 1], indices[o + 2]) = (b, b + 1, b + 2);
            (indices[o + 3], indices[o + 4], indices[o + 5]) = (b, b + 2, b + 3);
        }
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices);
        indexBuffer = gpu.CreateBuffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, indices);
        var info = new SDL_GPUTransferBufferCreateInfo
        { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)(vertices.Length * sizeof(SceneVertex)) };
        transfer = SDL_CreateGPUTransferBuffer(d, &info);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        capacity = size;
    }

    /// <summary>Records the squares, one run per emitter. Inside the world's render pass, after everything else in it.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, float seconds)
    {
        if (count == 0 || vertexBuffer == null || indexBuffer == null) return;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var ib = new SDL_GPUBufferBinding { buffer = indexBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var b in batches)
        {
            if (b.Material < 0 || !world.HasMaterial(b.Material)) continue;
            var mat = world.Material(b.Material);
            if (world.Scene.HasPasses(b.Material))
            {
                foreach (var p in mat.Passes)
                {
                    if (p.Image < 0 || RenderKinds.State(p.Kind) is not { } ps) continue;
                    Record(cmd, pass, bindings, b, ps, world.MaterialImage(b.Material, p.Image), p.Slide * seconds, p.NoMip);
                }
                continue;
            }
            if (RenderKinds.State(DrawPlan.ForParticle(mat.Traits)) is not { } state) continue;
            Record(cmd, pass, bindings, b, state, world.MaterialTexture(b.Material), mat.Slide0 * seconds, false);
        }
    }

    void Record(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, SDL_GPUTextureSamplerBinding* bindings,
                ClassicParticleBatch b, RenderKindState state, SDL_GPUTexture* texture, Vector2 slide, bool noMip)
    {
        SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, twoSided: true));
        bindings[0] = new SDL_GPUTextureSamplerBinding
        { texture = texture, sampler = world.SamplerFor(state.Address == Address.Clamp, state.Mipmaps && !noMip) };
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        // The vertex colour is the particle's colour, so the draw is not "fully lit".
        var prm = world.Params(state, slide: slide);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
        int first = b.Run switch
        {
            ParticleRun.Quads => spriteCount + b.First,
            ParticleRun.Streaks => spriteCount + quadCount + b.First,
            _ => b.First,
        };
        SDL_DrawGPUIndexedPrimitives(pass, (uint)(b.Count * 6), 1, (uint)(first * 6), 0, 0);
    }

    public void Dispose()
    {
        var d = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(d, indexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(d, transfer);
    }
}
