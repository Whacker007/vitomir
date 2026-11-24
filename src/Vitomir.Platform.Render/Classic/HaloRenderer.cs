// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws a place's halos (the engine's <c>Halos</c>, stepped by the snapshot's producer: <see cref="ClassicHalos"/>): each a square facing the camera, centred on its
/// thing, in its material's pass (every shipped halo material's is `glow`: added, depth test off),
/// as `rlDirect.dll` lays them out on the screen at depth 0. Here the square is built in
/// the world so the level shader can draw it: an ordinary halo is `HALOSIZE` world units from its
/// centre to its edge, which is what dividing by the depth on the screen amounts to; a fluoro halo
/// is `HALOSIZE` × its depth, which keeps it the same size on the screen wherever it is. It borrows
/// the world renderer's pipelines and textures, as the particles do.
/// </summary>
public sealed unsafe class HaloRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;

    public HaloRenderer(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
    }

    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;
    SceneVertex[] vertices = [];
    readonly List<(int Material, int First)> quads = [];

    /// <summary>The most halos a frame has room for at first (the engine's <c>Halos.Most</c>).</summary>
    const int Most = 32;

    /// <summary>
    /// Builds this frame's squares from the snapshot's halos (<paramref name="halos"/>; their materials made drawable by the
    /// producer the frame one was first met, as the engine reads a material's bitmaps in the first draw that needs them)
    /// for the camera's <paramref name="view"/>, and uploads them. Outside a render pass; false when there is
    /// nothing to draw.
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicHalos halos, Matrix4x4 view)
    {
        quads.Clear();
        if (halos.Halos.Count == 0 && halos.Twinkles.Count == 0) return false;
        Ensure((halos.Halos.Count + halos.Twinkles.Count) * 6);
        Matrix4x4.Invert(view, out var toWorld);
        var right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, toWorld));
        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, toWorld));
        int count = 0;
        // The twinkles first: the world draw does their list before the halos'.
        if (halos.Twinkles.Count > 0 && halos.TwinkleMaterial >= 0)
        {
            uint argb = halos.TwinkleColour;
            float half = halos.TwinkleHalf;
            foreach (var at in halos.Twinkles)
            {
                quads.Add((halos.TwinkleMaterial, count));
                Square(ref count, at, right * half, up * half, argb);
            }
        }
        foreach (var halo in halos.Halos)
        {
            var at = halo.Position;
            float depth = -Vector3.Transform(at, view).Z;                    // the view looks down −z
            if (depth <= 0f) continue;                                       // behind the eye: clipped
            float half = halo.Fluoro ? halo.Size * depth : halo.Size;
            quads.Add((halo.Material, count));
            Square(ref count, at, right * half, up * half, halo.Colour);
        }
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

    /// <summary>One square: (−r −u) (−r +u) (+r +u), (−r −u) (+r +u) (+r −u), the texture's corners on them.</summary>
    void Square(ref int count, Vector3 at, Vector3 r, Vector3 u, uint argb)
    {
        uint colour = ((argb >> 16) & 0xff) | (argb & 0xff00) | ((argb & 0xff) << 16) | (argb & 0xff000000);
        V(ref count, at - r - u, new Vector2(0, 1), colour);
        V(ref count, at - r + u, new Vector2(0, 0), colour);
        V(ref count, at + r + u, new Vector2(1, 0), colour);
        V(ref count, at - r - u, new Vector2(0, 1), colour);
        V(ref count, at + r + u, new Vector2(1, 0), colour);
        V(ref count, at + r - u, new Vector2(1, 1), colour);
    }

    void V(ref int count, Vector3 p, Vector2 uv, uint colour) =>
        vertices[count++] = new SceneVertex { Position = p, Uv = uv, LightmapUv = uv, Color = colour };


    void Ensure(int need)
    {
        if (need <= capacity) return;
        int size = Math.Max(Most * 6, capacity);
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

    /// <summary>Records the halos: last in the world pass, after the particles (the world draw's last loop).</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (quads.Count == 0 || vertexBuffer == null) return;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (var (m, first) in quads)
        {
            if (!world.HasMaterial(m)) continue;
            var mat = world.Material(m);
            int current = world.Scene.CurrentImage(m);
            // The material's passes (each one set up and drawn in turn); a halo material with none
            // is drawn in its particle mode, as the particles' own entry does.
            ReadOnlySpan<(RenderKind Kind, int Image)> steps = world.Scene.HasPasses(m)
                ? mat.Passes.Select(p => (p.Kind, p.Image < 0 ? current : p.Image)).ToArray()
                : [(DrawPlan.ForParticle(mat.Traits), current)];
            foreach (var (kind, image) in steps)
            {
                if (RenderKinds.State(kind) is not { } state) continue;
                // Depth 0 on the screen: nothing in the world is nearer.
                var over = state with { DepthFunc = Compare.Always, DepthWrite = false };
                SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(over, twoSided: true));
                bindings[0] = new SDL_GPUTextureSamplerBinding
                { texture = world.MaterialImage(m, image), sampler = world.SamplerFor(state.Address == Address.Clamp, state.Mipmaps) };
                bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
                SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
                var prm = world.Params(state);
                SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
                SDL_DrawGPUPrimitives(pass, 6, 1, (uint)first, 0);
            }
        }
    }

    public void Dispose()
    {
        var d = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(d, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(d, transfer);
    }
}
