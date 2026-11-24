// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws the click-to-move ring: four vertices, one texture, once a frame at most.
/// <c>WalkTarget</c> (the engine's) carries the rules and the arithmetic; this is the quad.
///
/// It borrows its pipeline, its material texture and its samplers from the
/// <see cref="WorldRenderer"/>, the way <see cref="SkyRenderer"/> does, so it needs no shader of
/// its own — the mode the engine asks for, <see cref="RenderKind.Glow"/>, is one the level shader
/// already knows how to be.
///
/// **It is the last thing drawn in the world pass**, which is where 1.1 puts it: after every
/// surface, every thing and every translucent batch. It can afford to be, because mode 13's depth
/// test is `always` — the quad sits exactly in the floor's plane with no lift, and a depth test
/// would be a coin toss on every pixel.
/// </summary>
public sealed unsafe class WalkTargetRenderer : IClassicRing
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    readonly SceneVertex[] vertices = new SceneVertex[4];
    readonly SDL_GPUBuffer* vertexBuffer;
    readonly SDL_GPUBuffer* indexBuffer;
    readonly SDL_GPUTransferBuffer* transfer;
    readonly uint vertexBytes;
    readonly int material;
    bool ready;

    /// <param name="material">The ring's material (the engine's <c>walktarget</c>), loaded when the ring is made: the world
    /// renderer takes a material's pictures in its next catch-up, so one that arrives later than that has none to be drawn
    /// with until the frame after. **Load it now, not on the first click**: the engine loads <c>pWalkTargetMat</c> while the
    /// world is coming up (with the world's other effect materials), so its maker asks the place's cache for it
    /// as it makes the ring (<c>place.Materials.Get(WalkTarget.Material)</c>; the glue constructor that did so for
    /// the old program's until then, <c>WalkTargetRenderer.Engine.cs</c>, is gone: the ring reads only the snapshot).</param>
    public WalkTargetRenderer(Gpu gpu, WorldRenderer world, int material)
    {
        this.gpu = gpu; this.world = world; this.material = material;
        vertexBytes = (uint)(vertices.Length * sizeof(SceneVertex));
        // Two triangles over four corners, wound the way every other face in this tree is.
        uint[] indices = [0, 1, 2, 0, 2, 3];
        vertexBuffer = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices);
        indexBuffer = gpu.CreateBuffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, indices);
        var info = new SDL_GPUTransferBufferCreateInfo
        { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = vertexBytes };
        transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &info);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
    }

    /// <summary>
    /// Poses the quad at the marker's current size and colour and uploads it. Call outside a render
    /// pass, as a copy pass has to be. Answers false when there is nothing to draw, which is most
    /// frames.
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicRing marker)
    {
        ready = false;
        if (!marker.Shown || vertexBuffer == null || indexBuffer == null) return false;
        uint packed = Pack(marker.Colour);
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new SceneVertex { Position = marker.Corners[i].Position, Uv = marker.Corners[i].Uv, Color = packed };

        var map = SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        fixed (SceneVertex* src = vertices) Buffer.MemoryCopy(src, (void*)map, vertexBytes, vertexBytes);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = vertexBuffer, offset = 0, size = vertexBytes };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
        ready = true;
        return true;
    }

    /// <summary>
    /// The engine's vertex colour is four floats in 0..255 (<c>WalkTarget.Colour</c>); this
    /// tree's vertices carry a packed ARGB byte, which is the same number in a smaller box.
    /// </summary>
    /// <summary>The ring's mode (the engine's <c>WalkTarget.Mode</c>): <c>glow</c>.</summary>
    public const RenderKind Mode = RenderKind.Glow;

    static uint Pack(Vector4 c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v), 0, 255);
        return (B(c.W) << 24) | (B(c.X) << 16) | (B(c.Y) << 8) | B(c.Z);
    }

    /// <summary>Records the quad. Inside the world's render pass, after everything else in it.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (!ready || material < 0) return;
        if (RenderKinds.State(Mode) is not { } state) return;

        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var ib = new SDL_GPUBufferBinding { buffer = indexBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

        // Two-sided: the quad is coplanar with the floor and a slope can face either way once the
        // basis is built from the surface's own normal, so culling it would make it vanish on half
        // the ground in the game.
        SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, twoSided: true));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        bindings[0] = new SDL_GPUTextureSamplerBinding
        { texture = world.MaterialTexture(material), sampler = world.ClampSampler };
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        var prm = world.Params(state, fullyLit: true);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
        SDL_DrawGPUIndexedPrimitives(pass, 6, 1, 0, 0, 0);
    }

    public void Dispose()
    {
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, vertexBuffer);
        if (indexBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, indexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
    }
}
