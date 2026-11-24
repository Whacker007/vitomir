// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vitomir.Render.Modern;

/// <summary>
/// One vertex of the modern path (ours): position, normal and tangent in the engine's world space
/// (Z up, engine units), two texture-coordinate sets and a colour. 60 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ModernVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    /// <summary>xyz along the texture's u; w is +1 or −1, the side the bitangent is on (glTF's convention).</summary>
    public Vector4 Tangent;
    public Vector2 Uv0;
    public Vector2 Uv1;
    /// <summary>Linear RGBA8, R in the lowest byte; white when the file has none.</summary>
    public uint Color;
}

/// <summary>How a glTF material's alpha is used: ignored, a cut-out at <see cref="GltfMaterial.AlphaCutoff"/>, or blended.</summary>
public enum GltfAlphaMode { Opaque, Mask, Blend }

/// <summary>A texture slot of a material: which of the scene's <see cref="GltfScene.Textures"/>, and which coordinate set it reads.</summary>
public readonly record struct GltfTextureSlot(int Texture, int TexCoord)
{
    public static readonly GltfTextureSlot None = new(-1, 0);
    public bool Present => Texture >= 0;
}

/// <summary>
/// A glTF 2.0 metallic-roughness material, as the file gives it. The texture slots index
/// <see cref="GltfScene.Textures"/>; a missing slot is <see cref="GltfTextureSlot.None"/>, which the
/// shader reads as white (or the flat normal).
/// </summary>
public sealed class GltfMaterial
{
    public string Name = "";
    public Vector4 BaseColorFactor = Vector4.One;
    public GltfTextureSlot BaseColorTexture = GltfTextureSlot.None;
    public float MetallicFactor = 1f, RoughnessFactor = 1f;
    /// <summary>Roughness in the green channel, metalness in the blue (glTF's packing).</summary>
    public GltfTextureSlot MetallicRoughnessTexture = GltfTextureSlot.None;
    public GltfTextureSlot NormalTexture = GltfTextureSlot.None;
    public float NormalScale = 1f;
    /// <summary>Occlusion in the red channel.</summary>
    public GltfTextureSlot OcclusionTexture = GltfTextureSlot.None;
    public float OcclusionStrength = 1f;
    public Vector3 EmissiveFactor = Vector3.Zero;
    public GltfTextureSlot EmissiveTexture = GltfTextureSlot.None;
    /// <summary><c>KHR_materials_emissive_strength</c>'s multiplier, 1 without it.</summary>
    public float EmissiveStrength = 1f;
    /// <summary><c>KHR_materials_specular</c>'s specularFactor: scales a dielectric's reflection (its F0 and F90), 1 without it.</summary>
    public float SpecularFactor = 1f;
    public GltfAlphaMode AlphaMode = GltfAlphaMode.Opaque;
    public float AlphaCutoff = 0.5f;
    public bool DoubleSided;
    /// <summary>A height map (ours: <c>extras.height</c>'s index; 1 the highest), drawn with parallax occlusion.</summary>
    public GltfTextureSlot HeightTexture = GltfTextureSlot.None;
    /// <summary>The metres the height map's full range spans (<c>extras.height.scale</c>; 0: none).</summary>
    public float HeightScale;
    /// <summary>Leaves, grass and ivy (ours: <c>extras.foliage</c>): lit through, swayed by the wind.</summary>
    public bool Foliage;
    /// <summary>How much of the sun's light comes through the leaf, 0..1.</summary>
    public float Translucency;
    /// <summary>How much the wind sways it, 0..1, times each vertex's sway weight (its colour's alpha, which then does not cut).</summary>
    public float Wind;
    /// <summary>How the maps tile (ours: <c>extras.tiling</c>); null for a plain repeat.</summary>
    public GltfTiling? Tiling;
    /// <summary>The material a decal lies on (ours: <c>extras.decal.on</c>), whose tiling it takes for the maps it borrows from it; null for anything else.</summary>
    public string? DecalOn;

    /// <summary>The material glTF prescribes when a primitive names none: white, fully metallic factor 1 and rough.</summary>
    public static GltfMaterial Default() => new() { Name = "(default)" };
}

/// <summary>
/// **Ours: how a material's maps tile** (<c>extras.tiling</c>):
/// <c>hex</c> lays hexagonal cells over the texture coordinate set <paramref name="TexCoord"/>, each cell showing
/// the maps read through that set at a random offset and, with <paramref name="Rotate"/>, a random rotation;
/// <paramref name="Scale"/> is the distance between neighbouring cells' centres as a fraction of the tile.
/// <see cref="Weights"/> is, for a decal, the height map of the surface under it, whose stones the blend follows.
/// </summary>
public sealed record GltfTiling(string Mode, bool Rotate, float Scale, int TexCoord = 0)
{
    public GltfTextureSlot Weights { get; init; } = GltfTextureSlot.None;
    public bool Hex => Mode == "hex" && Scale > 0f;
}

/// <summary>A texture: an image and how it wraps. Filtering is the modern path's own (trilinear, anisotropic).</summary>
public sealed record GltfTexture(int Image, bool RepeatU, bool RepeatV, bool MirrorU = false, bool MirrorV = false);

/// <summary>A decoded image: 8-bit RGBA, top row first; <see cref="Rgba"/> is null when it could not be decoded (a JPEG, say).</summary>
public sealed class GltfImage
{
    public string Name = "";
    public string MimeType = "";
    public int Width, Height;
    public byte[]? Rgba;
    /// <summary>
    /// Ours: the image's PNG bytes, read from the file again at each call, when the reader was told to
    /// leave images undecoded (<see cref="GltfReadOptions.DeferImages"/>; the cook, which needs one image at a
    /// time, not a town's two gigabytes of pixels at once). <see cref="Rgba"/> is then null.
    /// </summary>
    public Func<byte[]>? Encoded;
    /// <summary>The mean of its colour in linear light, once something has worked it out (ours; the probe bake's albedo).</summary>
    public System.Numerics.Vector3? Mean;
    /// <summary>Whether it has pixels: decoded, or to be decoded when wanted.</summary>
    public bool Present => Rgba is not null || Encoded is not null;
    /// <summary>Its pixels: <see cref="Rgba"/>, or, deferred, decoded afresh and not kept (null if the PNG cannot be decoded).</summary>
    public byte[]? Decode() => Rgba ?? (Encoded is { } read ? PngDecoder.Decode(read(), out Width, out Height) : null);
}

/// <summary>
/// A triangle list in world space, with its material. The node transforms and the change from glTF's
/// axes and metres to the engine's are already applied to every vertex.
/// </summary>
public sealed class GltfPrimitive
{
    public string Name = "";
    public ModernVertex[] Vertices = [];
    public uint[] Indices = [];
    public int Material = -1;
    public Vector3 Min, Max;
}

/// <summary>The kinds of <c>KHR_lights_punctual</c> light.</summary>
public enum GltfLightType { Directional, Point, Spot }

/// <summary>
/// A <c>KHR_lights_punctual</c> light placed by its node: position in engine units, the direction it
/// shines in (engine axes, unit length), colour, intensity as the file gives it (candela for point and
/// spot lights, lux for directional ones), range in engine units (0: none given, unlimited), and a
/// spot's inner and outer cone angles in radians.
/// </summary>
public sealed record GltfLight(string Name, GltfLightType Type, Vector3 Position, Vector3 Direction, Vector3 Colour,
                               float Intensity, float Range, float InnerCone, float OuterCone);

/// <summary>
/// What <see cref="GltfReader"/> reads: a glTF scene flattened into world-space triangle lists in the
/// engine's coordinates, its materials, textures and decoded images, and anything worth a warning.
/// </summary>
public sealed class GltfScene
{
    public List<GltfPrimitive> Primitives { get; } = [];
    public List<GltfMaterial> Materials { get; } = [];
    public List<GltfTexture> Textures { get; } = [];
    public List<GltfImage> Images { get; } = [];
    public List<string> Warnings { get; } = [];
    /// <summary>The <c>KHR_lights_punctual</c> lights the scene's nodes place.</summary>
    public List<GltfLight> Lights { get; } = [];
    public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
    public int TriangleCount => Primitives.Sum(p => p.Indices.Length / 3);
    public bool Empty => Primitives.Count == 0;
}
