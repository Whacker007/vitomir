// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The parts of a glTF 2.0 file as read, in the file's own axes (Y up, +Z front, metres).
using System.Numerics;

namespace Vitomir.Formats.Gltf;

/// <summary>An accessor's component type, by the numbers glTF gives them.</summary>
public enum GltfComponentType
{
    Byte = 5120,
    UnsignedByte = 5121,
    Short = 5122,
    UnsignedShort = 5123,
    UnsignedInt = 5125,
    Float = 5126,
}

/// <summary>A material's alpha mode.</summary>
public enum GltfAlphaMode { Opaque, Mask, Blend }

/// <summary>How an animation sampler interpolates between its keys.</summary>
public enum GltfInterpolation { Linear, Step, CubicSpline }

/// <summary>What an animation channel drives.</summary>
public enum GltfPath { Translation, Rotation, Scale, Weights, Other }

/// <summary>A buffer: its length and, for a <c>.gltf</c>, its URI (null for a <c>.glb</c>'s binary chunk).</summary>
public sealed record GltfBuffer(int ByteLength, string? Uri, string? Name, string? Extras);

/// <summary>A run of a buffer. <see cref="ByteStride"/> is 0 when the file gives none (the elements are packed).</summary>
public sealed record GltfBufferView(int Buffer, int ByteOffset, int ByteLength, int ByteStride, int Target, string? Name, string? Extras);

/// <summary>A sparse accessor's replacements: <see cref="Count"/> indices and as many values.</summary>
public sealed record GltfSparse(int Count, int IndicesBufferView, int IndicesByteOffset, GltfComponentType IndicesComponentType,
                                int ValuesBufferView, int ValuesByteOffset);

/// <summary>
/// An accessor: <see cref="Count"/> elements of <see cref="Type"/> (<c>SCALAR</c>, <c>VEC2</c> … <c>MAT4</c>) made of
/// <see cref="ComponentType"/>; <see cref="BufferView"/> −1 when it has none (all zeros, then the sparse values).
/// </summary>
public sealed record GltfAccessor(int BufferView, int ByteOffset, GltfComponentType ComponentType, bool Normalized, int Count,
                                  string Type, float[]? Min, float[]? Max, GltfSparse? Sparse, string? Name, string? Extras)
{
    /// <summary>Components an element: 1 for <c>SCALAR</c> … 16 for <c>MAT4</c>.</summary>
    public int Width => Type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16,
        _ => throw new InvalidDataException($"accessor type {Type}"),
    };

    /// <summary>Bytes a component.</summary>
    public int ComponentSize => GltfFile.SizeOf(ComponentType);
}

/// <summary>
/// A node. Its own transform is <see cref="Matrix"/> when the file gives a matrix, else translation, rotation and scale;
/// <see cref="LocalMatrix"/> is the one to use either way (System.Numerics' row vectors: <c>v × M</c>).
/// </summary>
public sealed record GltfNode(string Name, int[] Children, int Mesh, int Skin, int Camera,
                              Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4? Matrix,
                              float[]? Weights, string? Extras)
{
    /// <summary>The node's transform relative to its parent: its matrix, else scale, then rotation, then translation.</summary>
    public Matrix4x4 LocalMatrix => Matrix ?? Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
}

/// <summary>A scene: its root nodes.</summary>
public sealed record GltfScene(string Name, int[] Nodes, string? Extras);

/// <summary>A skin: its joints (node indices, in the order a primitive's <c>JOINTS_0</c> counts them) and inverse bind matrices' accessor (−1: identities).</summary>
public sealed record GltfSkin(string Name, int[] Joints, int InverseBindMatrices, int Skeleton, string? Extras);

/// <summary>A mesh: its primitives, default morph weights and the morph targets' names (<c>extras.targetNames</c>, the common convention).</summary>
public sealed record GltfMesh(string Name, GltfPrimitive[] Primitives, float[] Weights, string[] TargetNames, string? Extras);

/// <summary>
/// A primitive: its attributes by name (<c>POSITION</c>, <c>NORMAL</c>, <c>TANGENT</c>, <c>TEXCOORD_0</c>, <c>COLOR_0</c>,
/// <c>JOINTS_0</c>, <c>WEIGHTS_0</c> …) to accessors, its indices' accessor (−1: none), material (−1: the default),
/// mode (4 triangles) and morph targets (each attribute name to an accessor of deltas).
/// </summary>
public sealed record GltfPrimitive(IReadOnlyDictionary<string, int> Attributes, int Indices, int Material, int Mode,
                                   IReadOnlyList<IReadOnlyDictionary<string, int>> Targets, string? Extras)
{
    /// <summary>The accessor of attribute <paramref name="name"/>, or −1.</summary>
    public int Attribute(string name) => Attributes.TryGetValue(name, out int a) ? a : -1;
}

/// <summary>A material's use of a texture: the texture's index, which <c>TEXCOORD_n</c>, and the normal map's scale or the occlusion's strength.</summary>
public sealed record GltfTextureRef(int Index, int TexCoord, float Scale);

/// <summary>
/// A metallic-roughness material with every slot glTF defines, its alpha mode and sides, the names of the extensions it
/// uses (<see cref="ExtensionsJson"/> holds their objects as written) and its extras.
/// </summary>
public sealed record GltfMaterial
{
    public string Name { get; init; } = "";
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;
    public GltfTextureRef? BaseColorTexture { get; init; }
    public float MetallicFactor { get; init; } = 1f;
    public float RoughnessFactor { get; init; } = 1f;
    public GltfTextureRef? MetallicRoughnessTexture { get; init; }
    public GltfTextureRef? NormalTexture { get; init; }
    public GltfTextureRef? OcclusionTexture { get; init; }
    public Vector3 EmissiveFactor { get; init; }
    public GltfTextureRef? EmissiveTexture { get; init; }
    public GltfAlphaMode AlphaMode { get; init; } = GltfAlphaMode.Opaque;
    public float AlphaCutoff { get; init; } = 0.5f;
    public bool DoubleSided { get; init; }
    public string[] Extensions { get; init; } = [];
    /// <summary>The material's <c>extensions</c> object as written, or null.</summary>
    public string? ExtensionsJson { get; init; }
    public string? Extras { get; init; }

    /// <summary>Whether the material uses extension <paramref name="name"/>.</summary>
    public bool Uses(string name) => Array.IndexOf(Extensions, name) >= 0;
}

/// <summary>A texture: its image (−1: none) and sampler (−1: the default, repeat and linear).</summary>
public sealed record GltfTexture(int Source, int Sampler, string Name, string? Extras);

/// <summary>A sampler's filters and wraps (glTF's numbers: 10497 repeat, 33071 clamp, 33648 mirrored repeat; 0 unset).</summary>
public sealed record GltfSampler(int MagFilter, int MinFilter, int WrapS, int WrapT, string? Extras);

/// <summary>An image: in a buffer view (<see cref="BufferView"/> ≥ 0) or at a URI; its MIME type.</summary>
public sealed record GltfImage(string Name, string MimeType, int BufferView, string? Uri, string? Extras);

/// <summary>An animation: its channels and samplers, and its extras (the models repository's notes are in <c>extras.vitomir</c>).</summary>
public sealed record GltfAnimation(string Name, GltfChannel[] Channels, GltfAnimationSampler[] Samplers, string? Extras);

/// <summary>A channel: which sampler drives which node's what (<see cref="PathName"/> as written; <see cref="Node"/> −1 when it has none).</summary>
public sealed record GltfChannel(int Sampler, int Node, GltfPath Path, string PathName);

/// <summary>A sampler: its times' accessor, its values' accessor and its interpolation.</summary>
public sealed record GltfAnimationSampler(int Input, int Output, GltfInterpolation Interpolation);
