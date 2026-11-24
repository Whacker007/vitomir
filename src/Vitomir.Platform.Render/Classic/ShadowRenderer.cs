// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// Draws the characters' shadows (the engine's <c>Shadows</c> works them out, for the snapshot's <see cref="ClassicShadows"/>) as 1.1 hands them to the
/// renderer's draw entry: each piece a fan of its points, every point coloured
/// (shade, shade, shade, 255), in mode **30, <c>shadow</c>** — the texture times the colour, blended
/// <c>zero + invsrccolor</c>, so the frame under it is multiplied by one less the shade: white texels
/// at full shade darken it to black, black texels leave it be. Back faces culled, texture clamped,
/// z-bias 1, fogged.
///
/// **Where**: 1.1 draws a thing's shadows right after the thing's model, inside the
/// things' draw; here they go in one run after the opaque things, before the shells and the
/// translucent batches. The blend multiplies, so the order among shadows does not show.
///
/// **The pictures**: <c>shadowblob</c> and <c>silshadow</c> are materials, loaded here as the world
/// comes up (1.1 loads them then). The full shadow's
/// picture is one 128×128 bitmap in 1.1, drawn into and sent up again for each caster
/// before its pieces are drawn; a frame recorded all at once needs one picture a caster,
/// so here they share one texture, fifteen 128×128 cells a row with a clear texel round each (so
/// the filtering at a cell's edge reads nothing of its neighbour), sent up once a frame in the
/// frame's own copy pass (**ours**).
/// </summary>
public sealed unsafe class ShadowRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    int blob, silhouette;
    SDL_GPUTexture* atlas;
    SDL_GPUTransferBuffer* atlasTransfer;
    int atlasRows;                      // cell rows the atlas holds
    int usedRows;                       // cell rows this frame's pictures fill
    SDL_GPUBuffer* vertexBuffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;
    SceneVertex[] vertices = [];
    int count;

    /// <summary>A picture's cell, with its clear border.</summary>
    const int Cell = ClassicShadows.BitmapSize + 2;
    /// <summary>Cells a row of the atlas.</summary>
    const int PerRow = 15;
    const int AtlasWidth = Cell * PerRow;

    /// <summary>A run of triangles with one picture: a material, or the atlas (<see cref="Material"/> −1).</summary>
    readonly record struct Run(int Material, int First, int Count);
    readonly List<Run> runs = [];

    public ShadowRenderer(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
    }

    /// <summary>How many pieces the last frame drew.</summary>
    public int Drawn { get; private set; }

    /// <summary>
    /// Sends up this frame's shadows (<paramref name="shadows"/>, the snapshot's: the world's, or the reflection pass's own,
    /// <see cref="ReflectionPass"/>, which the producer worked out with the mirror's rule for which things cast and the
    /// reflected eye). Outside a render pass; false when there is nothing to draw.
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicShadows shadows)
    {
        count = 0;
        Drawn = 0;
        runs.Clear();
        if (!shadows.Cast) return false;
        blob = shadows.BlobMaterial;
        silhouette = shadows.SilhouetteMaterial;
        var polys = shadows.Pieces;
        if (polys.Count == 0) return false;

        if (shadows.BitmapCount > 0) SendPictures(cmd, shadows);
        int need = 0;
        foreach (var p in polys) need += (p.Count - 2) * 3;
        Ensure(need);
        var points = shadows.Points;
        // Grouped by picture; the polygons of one picture stay in the order they were made.
        foreach (var group in polys.GroupBy(Key))
        {
            int first = count;
            foreach (var p in group)
            {
                for (int k = 1; k + 1 < p.Count; k++)
                {
                    Vertex(points, p, 0); Vertex(points, p, k); Vertex(points, p, k + 1);
                }
                Drawn++;
            }
            if (count > first) runs.Add(new Run(group.Key, first, count - first));
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

    int Key(ClassicShadowPiece p) => p.Texture switch
    {
        ShadowTexture.Blob => blob >= 0 ? blob : -2,
        ShadowTexture.Silhouette => silhouette >= 0 ? silhouette : -2,
        _ => -1,
    };

    /// <summary>
    /// The shade as the draw entry turns a colour float into a byte (rounded, halves to even) and
    /// packs it: grey (shade, shade, shade), alpha 255.
    /// </summary>
    public static uint Pack(float shade)
    {
        uint c = (uint)Math.Clamp((int)MathF.Round(shade, MidpointRounding.ToEven), 0, 255);
        return 0xff000000u | (c << 16) | (c << 8) | c;
    }

    void Vertex(List<ClassicShadowPoint> points, ClassicShadowPiece p, int i)
    {
        var point = points[p.First + i];
        var uv = point.Uv;
        if (p.Texture == ShadowTexture.Bitmap)
        {
            // Into the picture's cell of the atlas.
            int cx = p.Bitmap % PerRow, cy = p.Bitmap / PerRow, size = ClassicShadows.BitmapSize;
            uv = new Vector2((cx * Cell + 1 + uv.X * size) / AtlasWidth, (cy * Cell + 1 + uv.Y * size) / (float)(atlasRows * Cell));
        }
        vertices[count++] = new SceneVertex { Position = point.Position, Uv = uv, Color = Pack(point.Shade) };
    }

    /// <summary>
    /// This frame's pictures into the atlas — white where a silhouette is, clear elsewhere — the
    /// atlas grown to hold them when it must, and the rows they fill sent up in the frame's copy pass.
    /// </summary>
    void SendPictures(SDL_GPUCommandBuffer* cmd, ClassicShadows shadows)
    {
        int n = shadows.BitmapCount, size = ClassicShadows.BitmapSize;
        usedRows = (n + PerRow - 1) / PerRow;
        var dev = gpu.Device;
        if (usedRows > atlasRows)
        {
            if (atlas != null) SDL_ReleaseGPUTexture(dev, atlas);
            if (atlasTransfer != null) SDL_ReleaseGPUTransferBuffer(dev, atlasTransfer);
            atlasRows = Math.Max(usedRows, Math.Max(2, atlasRows * 2));
            var info = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER, width = AtlasWidth, height = (uint)(atlasRows * Cell),
                layer_count_or_depth = 1, num_levels = 1, sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
            };
            atlas = SDL_CreateGPUTexture(dev, &info);
            if (atlas == null) throw new SdlException("SDL_CreateGPUTexture");
            var tinfo = new SDL_GPUTransferBufferCreateInfo
            { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)(AtlasWidth * atlasRows * Cell * 4) };
            atlasTransfer = SDL_CreateGPUTransferBuffer(dev, &tinfo);
            if (atlasTransfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        }
        int height = usedRows * Cell;
        var map = (uint*)SDL_MapGPUTransferBuffer(dev, atlasTransfer, true);
        new Span<uint>(map, AtlasWidth * height).Clear();
        for (int b = 0; b < n; b++)
        {
            var bits = shadows.Bitmaps[b];
            int x0 = (b % PerRow) * Cell + 1, y0 = (b / PerRow) * Cell + 1;
            for (int y = 0; y < size; y++)
            {
                uint* row = map + (y0 + y) * AtlasWidth + x0;
                for (int x = 0; x < size; x++) if (bits[y * size + x] != 0) row[x] = 0xffffffffu;
            }
        }
        SDL_UnmapGPUTransferBuffer(dev, atlasTransfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTextureTransferInfo { transfer_buffer = atlasTransfer, offset = 0, pixels_per_row = AtlasWidth, rows_per_layer = (uint)height };
        var dst = new SDL_GPUTextureRegion { texture = atlas, w = AtlasWidth, h = (uint)height, d = 1 };
        SDL_UploadToGPUTexture(copy, &src, &dst, true);
        SDL_EndGPUCopyPass(copy);
    }

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

    /// <summary>Records the shadows. Inside the world's render pass, after the opaque things.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj)
    {
        if (count == 0 || vertexBuffer == null) return;
        if (RenderKinds.State(RenderKind.Shadow) is not { } state) return;
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        var vb = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(state, twoSided: false));
        var prm = world.Params(state);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
        foreach (var r in runs)
        {
            SDL_GPUTexture* texture;
            bool mips;
            if (r.Material < -1) continue;                                 // a picture the game lacks
            if (r.Material == -1)
            {
                if (atlas == null) continue;
                texture = atlas;
                mips = false;
            }
            else
            {
                if (r.Material < 0 || !world.HasMaterial(r.Material)) continue;
                texture = world.MaterialTexture(r.Material);
                mips = state.Mipmaps;
            }
            bindings[0] = new SDL_GPUTextureSamplerBinding { texture = texture, sampler = world.SamplerFor(clamp: true, mips) };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            SDL_DrawGPUPrimitives(pass, (uint)r.Count, 1, (uint)r.First, 0);
        }
    }

    public void Dispose()
    {
        var dev = gpu.Device;
        if (vertexBuffer != null) SDL_ReleaseGPUBuffer(dev, vertexBuffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(dev, transfer);
        if (atlas != null) SDL_ReleaseGPUTexture(dev, atlas);
        if (atlasTransfer != null) SDL_ReleaseGPUTransferBuffer(dev, atlasTransfer);
        atlas = null;
        atlasTransfer = null;
    }
}
