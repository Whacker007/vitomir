// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Render.Modern;

/// <summary>
/// A <see cref="GltfScene"/> on the GPU (ours): one vertex buffer and one index buffer for all of it,
/// a draw per primitive, and a texture per image and use — colour images (base colour, emissive) as
/// sRGB, data images (normals, metal-roughness, occlusion) as linear — each with its full mip chain,
/// sampled through trilinear, anisotropic samplers. Textures a material lacks are 1×1 stand-ins: white,
/// or the flat normal.
/// </summary>
public sealed unsafe class ModernScene : IDisposable
{
    /// <summary>
    /// One primitive's indices in the shared buffer. <see cref="BaseVertex"/> is added to every index (a
    /// streamed cell's buffers sit anywhere in the shared ones, their indices counted from their own start).
    /// </summary>
    public readonly record struct Draw(uint FirstIndex, uint IndexCount, int Material, Vector3 Min, Vector3 Max, int BaseVertex = 0)
    {
        public Vector3 Centre => (Min + Max) * 0.5f;
    }

    readonly Gpu gpu;
    public SDL_GPUBuffer* Vertices { get; }
    public SDL_GPUBuffer* Indices { get; }
    /// <summary>What is drawn; a streamed level's changes frame by frame (<see cref="CellStreamScene"/>, ours).</summary>
    public IReadOnlyList<Draw> Draws { get; set; }
    /// <summary>The draws that cast the sun's shadow when not all of <see cref="Draws"/> do (ours: a streamed level leaves its far skyline out).</summary>
    public IReadOnlyList<Draw>? Casters { get; set; }
    /// <summary>The level's baked light probes, when it has them (ours; <see cref="ModernProbes"/>); the scene does not own them.</summary>
    public ModernProbes? Probes { get; set; }
    public GltfScene Source { get; }
    /// <summary>The scene's box (engine units): its file's, or what a streamer sets (ours).</summary>
    public Vector3 Min { get => bounds?.Min ?? Source.Min; }
    public Vector3 Max { get => bounds?.Max ?? Source.Max; }
    (Vector3 Min, Vector3 Max)? bounds;
    /// <summary>Sets the scene's box when its draws do not come from its file (ours).</summary>
    public void SetBounds(Vector3 min, Vector3 max) => bounds = (min, max);
    /// <summary>
    /// Where a material's images come from when not from the file (ours): a texture streamer's current
    /// texture for an image (0 for none yet, and the file's or the stand-in is used).
    /// </summary>
    public Func<int, bool, nint>? TextureSource { get; set; }
    /// <summary>Whether anything is drawn: the file had primitives, or a streamer set draws (ours).</summary>
    public bool Empty => Source.Empty && Draws.Count == 0;
    /// <summary>
    /// The scene's point and spot lights: the file's <c>KHR_lights_punctual</c> ones to begin with (lights
    /// without a range reach where they fall to <see cref="ModernSettings.LightCutoff"/>'s default); a
    /// caller may add or replace them (a look's torches, the game's lights) before drawing.
    /// </summary>
    public List<ModernPointLight> Lights { get; }

    readonly Dictionary<(int Image, bool Srgb), nint> textures = [];
    readonly Dictionary<(bool, bool, bool, bool), nint> samplers = [];
    readonly SDL_GPUTexture* white, whiteSrgb, flatNormal;
    readonly float anisotropy;

    /// <summary>
    /// A scene drawn from buffers someone else fills (ours, Phase 3): a streamed level's geometry arena
    /// (<paramref name="vertices"/>, <paramref name="indices"/>, not released here), with
    /// <paramref name="materials"/>' materials and images; the draws are set frame by frame.
    /// </summary>
    public ModernScene(Gpu gpu, GltfScene materials, SDL_GPUBuffer* vertices, SDL_GPUBuffer* indices, float anisotropy, bool uploadImages)
    {
        this.gpu = gpu;
        this.anisotropy = anisotropy;
        Source = materials;
        external = true;
        Lights = [];
        Draws = [];
        Vertices = vertices;
        Indices = indices;
        var up = new ModernDevice.Uploader(gpu);
        white = up.Texture(1, 1, [255, 255, 255, 255], srgb: false, mips: false);
        whiteSrgb = up.Texture(1, 1, [255, 255, 255, 255], srgb: true, mips: false);
        flatNormal = up.Texture(1, 1, [128, 128, 255, 255], srgb: false, mips: false);
        if (uploadImages)
            foreach (var m in materials.Materials)
            {
                Want(m.BaseColorTexture, true); Want(m.EmissiveTexture, true);
                Want(m.MetallicRoughnessTexture, false); Want(m.NormalTexture, false); Want(m.OcclusionTexture, false); Want(m.HeightTexture, false);
            }
        up.Submit();

        void Want(GltfTextureSlot slot, bool srgb)
        {
            if (!slot.Present) return;
            int image = materials.Textures[slot.Texture].Image;
            if (textures.ContainsKey((image, srgb))) return;
            var img = materials.Images[image];
            if (img.Rgba is null) return;
            textures[(image, srgb)] = (nint)up.Texture(img.Width, img.Height, img.Rgba, srgb, mips: true);
        }
    }

    readonly bool external;

    public ModernScene(Gpu gpu, GltfScene scene, float anisotropy = 16f)
    {
        this.gpu = gpu;
        this.anisotropy = anisotropy;
        Source = scene;
        Lights = ModernLights.FromGltf(scene, new ModernSettings().LightCutoff);
        var up = new ModernDevice.Uploader(gpu);
        var vertices = new List<ModernVertex>();
        var indices = new List<uint>();
        var draws = new List<Draw>();
        foreach (var p in scene.Primitives)
        {
            uint baseVertex = (uint)vertices.Count;
            draws.Add(new Draw((uint)indices.Count, (uint)p.Indices.Length, p.Material, p.Min, p.Max));
            vertices.AddRange(p.Vertices);
            foreach (var i in p.Indices) indices.Add(i + baseVertex);
        }
        Draws = draws;
        Vertices = up.Buffer<ModernVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, vertices.ToArray());
        Indices = up.Buffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, indices.ToArray());
        white = up.Texture(1, 1, [255, 255, 255, 255], srgb: false, mips: false);
        whiteSrgb = up.Texture(1, 1, [255, 255, 255, 255], srgb: true, mips: false);
        flatNormal = up.Texture(1, 1, [128, 128, 255, 255], srgb: false, mips: false);
        foreach (var m in scene.Materials)
        {
            Want(m.BaseColorTexture, true); Want(m.EmissiveTexture, true);
            Want(m.MetallicRoughnessTexture, false); Want(m.NormalTexture, false); Want(m.OcclusionTexture, false); Want(m.HeightTexture, false);
        }
        up.Submit();

        void Want(GltfTextureSlot slot, bool srgb)
        {
            if (!slot.Present) return;
            int image = scene.Textures[slot.Texture].Image;
            if (textures.ContainsKey((image, srgb))) return;
            var img = scene.Images[image];
            if (img.Rgba is null) return;
            textures[(image, srgb)] = (nint)up.Texture(img.Width, img.Height, img.Rgba, srgb, mips: true);
        }
    }

    /// <summary>The texture and sampler for a material's slot, or the stand-in when the slot is empty.</summary>
    public SDL_GPUTextureSamplerBinding Binding(GltfTextureSlot slot, bool srgb, bool normal = false)
    {
        if (slot.Present)
        {
            var t = Source.Textures[slot.Texture];
            if (TextureSource?.Invoke(t.Image, srgb) is { } streamed && streamed != 0)
                return new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)streamed, sampler = Sampler(t.RepeatU, t.RepeatV, t.MirrorU, t.MirrorV) };
            if (textures.TryGetValue((t.Image, srgb), out var tex))
                return new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)tex, sampler = Sampler(t.RepeatU, t.RepeatV, t.MirrorU, t.MirrorV) };
        }
        return new SDL_GPUTextureSamplerBinding
        {
            texture = normal ? flatNormal : srgb ? whiteSrgb : white,
            sampler = Sampler(true, true, false, false),
        };
    }

    SDL_GPUSampler* Sampler(bool ru, bool rv, bool mu, bool mv)
    {
        if (!samplers.TryGetValue((ru, rv, mu, mv), out var s))
            samplers[(ru, rv, mu, mv)] = s = (nint)ModernDevice.CreateSampler(gpu, ru, rv, mu, mv, anisotropy);
        return (SDL_GPUSampler*)s;
    }

    public GltfMaterial Material(int index) => index >= 0 && index < Source.Materials.Count ? Source.Materials[index] : fallback;
    /// <summary>The material of a draw that names none: made once and only read (one was made at every asking, several times a draw a frame).</summary>
    readonly GltfMaterial fallback = GltfMaterial.Default();

    public void Dispose()
    {
        if (Vertices != null && !external) SDL_ReleaseGPUBuffer(gpu.Device, Vertices);
        if (Indices != null && !external) SDL_ReleaseGPUBuffer(gpu.Device, Indices);
        foreach (var t in textures.Values) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t);
        foreach (var s in samplers.Values) SDL_ReleaseGPUSampler(gpu.Device, (SDL_GPUSampler*)s);
        SDL_ReleaseGPUTexture(gpu.Device, white);
        SDL_ReleaseGPUTexture(gpu.Device, whiteSrgb);
        SDL_ReleaseGPUTexture(gpu.Device, flatNormal);
    }
}
