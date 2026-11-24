// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernExtra.cs at b4185ef, reworked for the platform.
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours.** Geometry the modern renderer draws beside its <see cref="ModernScene"/> that changes from
/// frame to frame: 1.1's characters and things in a cell level (<see cref="CellThingPass"/> fills it).
/// It is drawn into the sun's shadow cascades (so it casts), into the depth-and-normal prepass (so
/// ambient occlusion and the shading pass's depth test see it) and into the surfaces with the same
/// physically based shader, lights and haze as the world; the renderer's tone mapping and grade then
/// treat it as the world. Blended surfaces are not drawn here: whoever fills it keeps those.
/// <para>One vertex buffer and one index buffer in <see cref="ModernVertex"/>'s layout, in world
/// space, written again each frame with <see cref="Upload"/>; a draw per batch with its own base-colour
/// texture (sRGB, with mips) and its own constant roughness, metalness and reflectance. Textures a
/// material lacks are the white and flat-normal stand-ins.</para>
/// </summary>
internal sealed unsafe class ModernExtra : IDisposable
{
    /// <summary>
    /// One draw: its indices in <see cref="Indices"/>, its world box (for culling against the view and
    /// each cascade), its base-colour texture (an index from <see cref="AddTexture"/>), the alpha
    /// below which a cut-out drops a pixel (negative for none), whether both faces are drawn, and the
    /// surface's constant roughness, metalness and dielectric reflectance (KHR_materials_specular's factor),
    /// and which passes draw it (the first-person body's hidden head and neck cast a shadow and are not seen).
    /// </summary>
    public readonly record struct Draw(uint FirstIndex, uint IndexCount, Vector3 Min, Vector3 Max, int Texture,
                                       float AlphaCutoff, bool DoubleSided, float Roughness, float Metallic, float Specular,
                                       DrawPasses Passes = DrawPasses.Both)
    {
        public bool InShadows => (Passes & DrawPasses.Shadow) != 0;
        public bool InView => (Passes & DrawPasses.View) != 0;

        /// <summary>
        /// The tangent-space normal map (an index from <see cref="AddTexture"/>, linear; glTF's +Y green), or −1
        /// for none (the flat stand-in, and the shader's normal map off: 1.1's parts).
        /// </summary>
        public int Normal { get; init; } = -1;
        /// <summary>
        /// The occlusion-roughness-metal map (linear: occlusion red, roughness green, metal blue), bound as both
        /// the shader's metal-roughness and its occlusion slot, or −1 for none (white: <see cref="Roughness"/> and
        /// <see cref="Metallic"/> alone, no occlusion; 1.1's parts).
        /// </summary>
        public int Orm { get; init; } = -1;
        /// <summary>The base colour's factor (linear; its alpha scales the cut-out's), white for 1.1's parts.</summary>
        public Vector4 BaseColorFactor { get; init; } = Vector4.One;
        /// <summary>The normal map's scale (glTF's <c>normalTexture.scale</c>), 1 by default.</summary>
        public float NormalScale { get; init; } = 1f;
    }

    /// <summary>Which of the frame's passes draw a piece of the extra geometry.</summary>
    [Flags]
    public enum DrawPasses : byte
    {
        /// <summary>The sun's shadow cascades.</summary>
        Shadow = 1,
        /// <summary>What the camera sees: the prepass and the surfaces.</summary>
        View = 2,
        Both = Shadow | View,
    }

    readonly Gpu gpu;
    readonly List<nint> textures = [];
    readonly SDL_GPUTexture* white, flatNormal;
    readonly SDL_GPUSampler* sampler;
    SDL_GPUTransferBuffer* transfer;
    uint transferBytes, vertexBytes, indexBytes;

    /// <summary>This frame's vertices, or null before anything was uploaded.</summary>
    public SDL_GPUBuffer* Vertices { get; private set; }
    /// <summary>This frame's indices (32-bit), or null.</summary>
    public SDL_GPUBuffer* Indices { get; private set; }
    /// <summary>This frame's draws, into <see cref="Vertices"/> and <see cref="Indices"/>.</summary>
    public List<Draw> Draws { get; } = [];

    public ModernExtra(Gpu gpu, float anisotropy = 16f)
    {
        this.gpu = gpu;
        var up = new ModernDevice.Uploader(gpu);
        white = up.Texture(1, 1, [255, 255, 255, 255], srgb: false, mips: false);
        flatNormal = up.Texture(1, 1, [128, 128, 255, 255], srgb: false, mips: false);
        up.Submit();
        sampler = ModernDevice.CreateSampler(gpu, true, true, false, false, anisotropy);
    }

    /// <summary>
    /// A texture (RGBA8, rows as the texture coordinates expect them), with its mips; answers its index. A base colour is sRGB;
    /// A data map (a normal or an occlusion-roughness-metal map) is linear (<paramref name="srgb"/> false).
    /// </summary>
    public int AddTexture(int width, int height, byte[] rgba, bool srgb = true)
    {
        var up = new ModernDevice.Uploader(gpu);
        textures.Add((nint)up.Texture(width, height, rgba, srgb, mips: true));
        up.Submit();
        return textures.Count - 1;
    }

    /// <summary>A texture with its mip levels given (a cut-out's coverage-keeping mips); answers its index.</summary>
    public int AddTextureLevels(int width, int height, byte[][] levels, bool srgb)
    {
        var up = new ModernDevice.Uploader(gpu);
        textures.Add((nint)up.TextureLevels(width, height, levels, srgb));
        up.Submit();
        return textures.Count - 1;
    }

    /// <summary>A draw's base colour: its texture through the repeating, anisotropic sampler.</summary>
    public SDL_GPUTextureSamplerBinding BaseColour(int texture) => new()
    {
        texture = texture >= 0 && texture < textures.Count ? (SDL_GPUTexture*)textures[texture] : white, sampler = sampler,
    };

    /// <summary>A draw's data map (<see cref="Draw.Normal"/>, <see cref="Draw.Orm"/>), or its stand-in (the flat normal, or white) for −1.</summary>
    public SDL_GPUTextureSamplerBinding Map(int texture, bool normal) =>
        texture >= 0 && texture < textures.Count ? new() { texture = (SDL_GPUTexture*)textures[texture], sampler = sampler } : StandIn(normal);

    /// <summary>The stand-in for an absent data texture: white, or the flat normal.</summary>
    public SDL_GPUTextureSamplerBinding StandIn(bool normal) => new() { texture = normal ? flatNormal : white, sampler = sampler };

    /// <summary>
    /// Writes this frame's geometry on <paramref name="cmd"/>, outside any render pass: the buffers grow
    /// when they must and are cycled otherwise, so a frame still in flight keeps what it drew.
    /// </summary>
    public void Upload(SDL_GPUCommandBuffer* cmd, ReadOnlySpan<ModernVertex> vertices, ReadOnlySpan<uint> indices)
    {
        uint vb = (uint)(vertices.Length * sizeof(ModernVertex)), ib = (uint)(indices.Length * sizeof(uint));
        if (vb == 0 || ib == 0) { Draws.Clear(); return; }
        Vertices = Grow(Vertices, ref vertexBytes, vb, SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX);
        Indices = Grow(Indices, ref indexBytes, ib, SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX);
        if (vb + ib > transferBytes)
        {
            if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
            transferBytes = (vb + ib) * 3 / 2;
            var info = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = transferBytes };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &info);
            if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (modern extra)");
        }
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        fixed (ModernVertex* v = vertices) Buffer.MemoryCopy(v, map, vb, vb);
        fixed (uint* i = indices) Buffer.MemoryCopy(i, map + vb, ib, ib);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = Vertices, offset = 0, size = vb };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        from.offset = vb;
        to = new SDL_GPUBufferRegion { buffer = Indices, offset = 0, size = ib };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
    }

    /// <summary>A buffer at least <paramref name="need"/> bytes: <paramref name="old"/> when it is big enough, else a new one half as big again.</summary>
    SDL_GPUBuffer* Grow(SDL_GPUBuffer* old, ref uint have, uint need, SDL_GPUBufferUsageFlags usage)
    {
        if (old != null && need <= have) return old;
        if (old != null) SDL_ReleaseGPUBuffer(gpu.Device, old);
        have = need * 3 / 2;
        var info = new SDL_GPUBufferCreateInfo { usage = usage, size = have };
        var made = SDL_CreateGPUBuffer(gpu.Device, &info);
        if (made == null) throw new SdlException("SDL_CreateGPUBuffer (modern extra)");
        return made;
    }

    public void Dispose()
    {
        if (Vertices != null) SDL_ReleaseGPUBuffer(gpu.Device, Vertices);
        if (Indices != null) SDL_ReleaseGPUBuffer(gpu.Device, Indices);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        foreach (var t in textures) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t);
        SDL_ReleaseGPUTexture(gpu.Device, white);
        SDL_ReleaseGPUTexture(gpu.Device, flatNormal);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        Vertices = Indices = null;
    }
}
