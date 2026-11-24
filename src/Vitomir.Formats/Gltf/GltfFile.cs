// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Conventions from src/Vitomir.Platform.Render/Modern/GltfReader.cs (the levels' static-scene reader),
// which is left as it is: this one keeps the file as written (nodes, skins, animations) for whoever builds from it.
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vitomir.Formats.Gltf;

/// <summary>
/// A glTF 2.0 file as written: a <c>.glb</c> (its JSON and binary chunks) or a <c>.gltf</c> (its buffers beside it
/// or in data URIs). Everything stays in the file's own axes and units (Y up, +Z front, metres); whoever builds from it
/// converts. What it reads: scenes and the node tree (translation, rotation, scale or a matrix), meshes and their
/// primitives with every attribute and morph target (<c>mesh.extras.targetNames</c> as <see cref="GltfMesh.TargetNames"/>),
/// skins, metallic-roughness materials with every slot, alpha mode and sides and the names of the extensions they use,
/// textures, samplers and images, animations (LINEAR, STEP and CUBICSPLINE samplers on translation, rotation, scale and
/// weights), and every accessor: every component type, normalised or not, strided, and sparse. Each object's
/// <c>extras</c> is kept as raw JSON. Reading an accessor answers floats (normalised integers to 0..1 or −1..1, as glTF
/// defines) or raw integers (indices, joints).
/// </summary>
public sealed class GltfFile
{
    const uint GlbMagic = 0x46546C67, ChunkJson = 0x4E4F534A, ChunkBin = 0x004E4942;

    readonly byte[]?[] bufferData;
    readonly ArraySegment<byte> glbBin;
    readonly string? folder;
    int[]? parents;

    GltfFile(string? folder, ArraySegment<byte> glbBin, int buffers)
    {
        this.folder = folder;
        this.glbBin = glbBin;
        bufferData = new byte[]?[buffers];
    }

    public string Generator { get; private init; } = "";
    public string Version { get; private init; } = "";
    /// <summary>The default scene (−1 when the file names none).</summary>
    public int Scene { get; private init; } = -1;
    public IReadOnlyList<GltfScene> Scenes { get; private init; } = [];
    public IReadOnlyList<GltfNode> Nodes { get; private init; } = [];
    public IReadOnlyList<GltfMesh> Meshes { get; private init; } = [];
    public IReadOnlyList<GltfSkin> Skins { get; private init; } = [];
    public IReadOnlyList<GltfMaterial> Materials { get; private init; } = [];
    public IReadOnlyList<GltfTexture> Textures { get; private init; } = [];
    public IReadOnlyList<GltfSampler> Samplers { get; private init; } = [];
    public IReadOnlyList<GltfImage> Images { get; private init; } = [];
    public IReadOnlyList<GltfAnimation> Animations { get; private init; } = [];
    public IReadOnlyList<GltfAccessor> Accessors { get; private init; } = [];
    public IReadOnlyList<GltfBufferView> BufferViews { get; private init; } = [];
    public IReadOnlyList<GltfBuffer> Buffers { get; private init; } = [];
    public IReadOnlyList<string> ExtensionsUsed { get; private init; } = [];
    public IReadOnlyList<string> ExtensionsRequired { get; private init; } = [];
    /// <summary>The root object's <c>extras</c>, raw JSON, or null.</summary>
    public string? Extras { get; private init; }
    /// <summary>The asset object's <c>extras</c>, raw JSON, or null.</summary>
    public string? AssetExtras { get; private init; }

    /// <summary>Whether <paramref name="bytes"/> start as a <c>.glb</c>.</summary>
    public static bool IsGlb(ReadOnlySpan<byte> bytes) => bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic;

    /// <summary>Reads the file at <paramref name="path"/> (relative URIs from its folder).</summary>
    public static GltfFile Load(string path) =>
        Read(File.ReadAllBytes(path), Path.GetDirectoryName(Path.GetFullPath(path)));

    /// <summary>Reads a file's bytes (kept: the accessors and images are read from them later); <paramref name="folder"/> is
    /// where relative URIs are found (null: none may be used).</summary>
    public static GltfFile Read(byte[] bytes, string? folder = null)
    {
        ReadOnlyMemory<byte> json = bytes;
        var bin = ArraySegment<byte>.Empty;
        if (IsGlb(bytes))
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
            if (length > bytes.Length) throw new InvalidDataException($"a .glb of {length} bytes in {bytes.Length}");
            int at = 12;
            json = ReadOnlyMemory<byte>.Empty;
            bool first = true;
            while (at + 8 <= (int)length)
            {
                int chunk = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
                if (chunk < 0 || at + 8 + chunk > length) throw new InvalidDataException($"a .glb chunk of {chunk} bytes at {at} runs past the end");
                if (type == ChunkJson && first) json = bytes.AsMemory(at + 8, chunk);
                else if (type == ChunkBin && bin.Count == 0) bin = new ArraySegment<byte>(bytes, at + 8, chunk);
                if (first && type != ChunkJson) throw new InvalidDataException("a .glb whose first chunk is not JSON");
                first = false;
                at += 8 + chunk;   // chunks are padded to four bytes within their length
            }
            if (json.IsEmpty) throw new InvalidDataException("a .glb without a JSON chunk");
        }
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, MaxDepth = 256 });
        return Parse(doc.RootElement, folder, bin);
    }

    // ----- parsing the JSON -----

    static GltfFile Parse(JsonElement root, string? folder, ArraySegment<byte> bin)
    {
        var buffers = List(root, "buffers", b => new GltfBuffer(Int(b, "byteLength", 0), Str(b, "uri"), Str(b, "name"), ExtrasOf(b)));
        var asset = root.TryGetProperty("asset", out var a) ? a : default;
        return new GltfFile(folder, bin, buffers.Length)
        {
            Generator = asset.ValueKind == JsonValueKind.Object ? Str(asset, "generator") ?? "" : "",
            Version = asset.ValueKind == JsonValueKind.Object ? Str(asset, "version") ?? "" : "",
            AssetExtras = asset.ValueKind == JsonValueKind.Object ? ExtrasOf(asset) : null,
            Extras = ExtrasOf(root),
            Scene = Int(root, "scene", -1),
            ExtensionsUsed = Strings(root, "extensionsUsed"),
            ExtensionsRequired = Strings(root, "extensionsRequired"),
            Buffers = buffers,
            BufferViews = List(root, "bufferViews", v => new GltfBufferView(Int(v, "buffer", 0), Int(v, "byteOffset", 0), Int(v, "byteLength", 0),
                                                                          Int(v, "byteStride", 0), Int(v, "target", 0), Str(v, "name"), ExtrasOf(v))),
            Accessors = List(root, "accessors", ReadAccessor),
            Scenes = List(root, "scenes", s => new GltfScene(Str(s, "name") ?? "", Ints(s, "nodes"), ExtrasOf(s))),
            Nodes = List(root, "nodes", ReadNode),
            Meshes = List(root, "meshes", ReadMesh),
            Skins = List(root, "skins", s => new GltfSkin(Str(s, "name") ?? "", Ints(s, "joints"), Int(s, "inverseBindMatrices", -1), Int(s, "skeleton", -1), ExtrasOf(s))),
            Materials = List(root, "materials", ReadMaterial),
            Textures = List(root, "textures", t => new GltfTexture(Int(t, "source", -1), Int(t, "sampler", -1), Str(t, "name") ?? "", ExtrasOf(t))),
            Samplers = List(root, "samplers", s => new GltfSampler(Int(s, "magFilter", 0), Int(s, "minFilter", 0), Int(s, "wrapS", 10497), Int(s, "wrapT", 10497), ExtrasOf(s))),
            Images = List(root, "images", i => new GltfImage(Str(i, "name") ?? "", Str(i, "mimeType") ?? "", Int(i, "bufferView", -1), Str(i, "uri"), ExtrasOf(i))),
            Animations = List(root, "animations", ReadAnimation),
        };
    }

    static GltfAccessor ReadAccessor(JsonElement a)
    {
        GltfSparse? sparse = null;
        if (a.TryGetProperty("sparse", out var s))
        {
            var si = s.GetProperty("indices");
            var sv = s.GetProperty("values");
            sparse = new GltfSparse(Int(s, "count", 0), Int(si, "bufferView", -1), Int(si, "byteOffset", 0),
                                    (GltfComponentType)Int(si, "componentType", 5125), Int(sv, "bufferView", -1), Int(sv, "byteOffset", 0));
        }
        return new GltfAccessor(Int(a, "bufferView", -1), Int(a, "byteOffset", 0), (GltfComponentType)Int(a, "componentType", 5126),
                                a.TryGetProperty("normalized", out var n) && n.GetBoolean(), Int(a, "count", 0), Str(a, "type") ?? "SCALAR",
                                Floats(a, "min"), Floats(a, "max"), sparse, Str(a, "name"), ExtrasOf(a));
    }

    static GltfNode ReadNode(JsonElement n)
    {
        Matrix4x4? matrix = null;
        if (n.TryGetProperty("matrix", out var m))
        {
            var f = m.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            if (f.Length != 16) throw new InvalidDataException("a node's matrix of other than 16 numbers");
            // glTF's column-major column-vector matrix is, element for element, System.Numerics' row-vector one.
            matrix = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
        }
        var t = Floats(n, "translation");
        var r = Floats(n, "rotation");
        var sc = Floats(n, "scale");
        return new GltfNode(Str(n, "name") ?? "", Ints(n, "children"), Int(n, "mesh", -1), Int(n, "skin", -1), Int(n, "camera", -1),
                            t is { Length: 3 } ? new Vector3(t[0], t[1], t[2]) : Vector3.Zero,
                            r is { Length: 4 } ? new Quaternion(r[0], r[1], r[2], r[3]) : Quaternion.Identity,
                            sc is { Length: 3 } ? new Vector3(sc[0], sc[1], sc[2]) : Vector3.One,
                            matrix, Floats(n, "weights"), ExtrasOf(n));
    }

    static GltfMesh ReadMesh(JsonElement m)
    {
        var prims = List(m, "primitives", p =>
        {
            var attrs = new Dictionary<string, int>(StringComparer.Ordinal);
            if (p.TryGetProperty("attributes", out var at))
                foreach (var kv in at.EnumerateObject()) attrs[kv.Name] = kv.Value.GetInt32();
            var targets = new List<IReadOnlyDictionary<string, int>>();
            if (p.TryGetProperty("targets", out var ts))
                foreach (var t in ts.EnumerateArray())
                {
                    var d = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var kv in t.EnumerateObject()) d[kv.Name] = kv.Value.GetInt32();
                    targets.Add(d);
                }
            return new GltfPrimitive(attrs, Int(p, "indices", -1), Int(p, "material", -1), Int(p, "mode", 4), targets, ExtrasOf(p));
        });
        string[] names = [];
        if (m.TryGetProperty("extras", out var ex) && ex.ValueKind == JsonValueKind.Object && ex.TryGetProperty("targetNames", out var tn)
            && tn.ValueKind == JsonValueKind.Array)
            names = tn.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        return new GltfMesh(Str(m, "name") ?? "", prims, Floats(m, "weights") ?? [], names, ExtrasOf(m));
    }

    static GltfMaterial ReadMaterial(JsonElement m)
    {
        var mat = new GltfMaterial
        {
            Name = Str(m, "name") ?? "",
            NormalTexture = Slot(m, "normalTexture", "scale"),
            OcclusionTexture = Slot(m, "occlusionTexture", "strength"),
            EmissiveTexture = Slot(m, "emissiveTexture", null),
            EmissiveFactor = Floats(m, "emissiveFactor") is { Length: 3 } e ? new Vector3(e[0], e[1], e[2]) : Vector3.Zero,
            AlphaMode = Str(m, "alphaMode") switch { "MASK" => GltfAlphaMode.Mask, "BLEND" => GltfAlphaMode.Blend, _ => GltfAlphaMode.Opaque },
            AlphaCutoff = m.TryGetProperty("alphaCutoff", out var ac) ? ac.GetSingle() : 0.5f,
            DoubleSided = m.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean(),
            Extensions = m.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Object
                ? ext.EnumerateObject().Select(p => p.Name).ToArray() : [],
            ExtensionsJson = m.TryGetProperty("extensions", out var ej) ? ej.GetRawText() : null,
            Extras = ExtrasOf(m),
        };
        if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
            mat = mat with
            {
                BaseColorFactor = Floats(pbr, "baseColorFactor") is { Length: 4 } b ? new Vector4(b[0], b[1], b[2], b[3]) : Vector4.One,
                BaseColorTexture = Slot(pbr, "baseColorTexture", null),
                MetallicFactor = pbr.TryGetProperty("metallicFactor", out var mf) ? mf.GetSingle() : 1f,
                RoughnessFactor = pbr.TryGetProperty("roughnessFactor", out var rf) ? rf.GetSingle() : 1f,
                MetallicRoughnessTexture = Slot(pbr, "metallicRoughnessTexture", null),
            };
        return mat;
    }

    static GltfTextureRef? Slot(JsonElement owner, string name, string? scaleName) =>
        owner.TryGetProperty(name, out var t) && t.TryGetProperty("index", out var i)
            ? new GltfTextureRef(i.GetInt32(), Int(t, "texCoord", 0), scaleName is not null && t.TryGetProperty(scaleName, out var s) ? s.GetSingle() : 1f)
            : null;

    static GltfAnimation ReadAnimation(JsonElement a)
    {
        var channels = List(a, "channels", c =>
        {
            var target = c.GetProperty("target");
            string path = Str(target, "path") ?? "";
            return new GltfChannel(Int(c, "sampler", 0), Int(target, "node", -1), path switch
            {
                "translation" => GltfPath.Translation, "rotation" => GltfPath.Rotation, "scale" => GltfPath.Scale,
                "weights" => GltfPath.Weights, _ => GltfPath.Other,
            }, path);
        });
        var samplers = List(a, "samplers", s => new GltfAnimationSampler(Int(s, "input", -1), Int(s, "output", -1), Str(s, "interpolation") switch
        {
            "STEP" => GltfInterpolation.Step, "CUBICSPLINE" => GltfInterpolation.CubicSpline, _ => GltfInterpolation.Linear,
        }));
        return new GltfAnimation(Str(a, "name") ?? "", channels, samplers, ExtrasOf(a));
    }

    static T[] List<T>(JsonElement owner, string name, Func<JsonElement, T> read) =>
        owner.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray().Select(read).ToArray() : [];

    static string? ExtrasOf(JsonElement e) => e.TryGetProperty("extras", out var x) ? x.GetRawText() : null;
    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int Int(JsonElement e, string name, int otherwise) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : otherwise;
    static int[] Ints(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
    static float[]? Floats(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null;
    static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];

    // ----- the tree -----

    /// <summary>Each node's parent (−1 for a root), from the children lists.</summary>
    public IReadOnlyList<int> Parents
    {
        get
        {
            if (parents is not null) return parents;
            var p = new int[Nodes.Count];
            Array.Fill(p, -1);
            for (int i = 0; i < Nodes.Count; i++)
                foreach (int c in Nodes[i].Children)
                    if ((uint)c < (uint)p.Length) p[c] = i;
            return parents = p;
        }
    }

    /// <summary>A node's transform in the file's world: its own, then each parent's (row vectors).</summary>
    public Matrix4x4 WorldMatrix(int node)
    {
        var m = Matrix4x4.Identity;
        for (int n = node, guard = 0; n >= 0 && guard <= Nodes.Count; n = Parents[n], guard++) m *= Nodes[n].LocalMatrix;
        return m;
    }

    /// <summary>The default scene's nodes, depth first in the file's order (every root node when the file has no scene).</summary>
    public IEnumerable<int> SceneOrder()
    {
        int[] roots = Scene >= 0 && Scene < Scenes.Count ? Scenes[Scene].Nodes
            : Scenes.Count > 0 ? Scenes[0].Nodes
            : Enumerable.Range(0, Nodes.Count).Where(i => Parents[i] < 0).ToArray();
        var stack = new Stack<int>(Enumerable.Reverse(roots));
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (!seen.Add(n) || (uint)n >= (uint)Nodes.Count) continue;
            yield return n;
            var c = Nodes[n].Children;
            for (int i = c.Length - 1; i >= 0; i--) stack.Push(c[i]);
        }
    }

    /// <summary>The first node named <paramref name="name"/>, or −1.</summary>
    public int FindNode(string name)
    {
        for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Name == name) return i;
        return -1;
    }

    // ----- buffers -----

    /// <summary>A buffer's bytes: the <c>.glb</c>'s binary chunk, a data URI's or a file's beside the <c>.gltf</c>.</summary>
    public ArraySegment<byte> Buffer(int index)
    {
        if ((uint)index >= (uint)Buffers.Count) throw new InvalidDataException($"buffer {index} of {Buffers.Count}");
        var b = Buffers[index];
        if (b.Uri is null)
        {
            if (index != 0 || glbBin.Array is null) throw new InvalidDataException($"buffer {index} has no uri and the file has no binary chunk");
            return glbBin;
        }
        lock (bufferData) return bufferData[index] ??= FromUri(b.Uri);
    }

    byte[] FromUri(string uri)
    {
        if (uri.StartsWith("data:", StringComparison.Ordinal))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.Ordinal)) throw new InvalidDataException("a data URI that is not base64");
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        if (folder is null) throw new InvalidDataException($"a buffer at {uri}, and no folder to find it in");
        return File.ReadAllBytes(Path.Combine(folder, Uri.UnescapeDataString(uri)));
    }

    /// <summary>A buffer view's bytes.</summary>
    public ArraySegment<byte> View(int index)
    {
        if ((uint)index >= (uint)BufferViews.Count) throw new InvalidDataException($"buffer view {index} of {BufferViews.Count}");
        var v = BufferViews[index];
        var b = Buffer(v.Buffer);
        if (v.ByteOffset < 0 || v.ByteLength < 0 || (long)v.ByteOffset + v.ByteLength > b.Count)
            throw new InvalidDataException($"buffer view {index} ({v.ByteOffset} + {v.ByteLength}) past its buffer's {b.Count} bytes");
        return b.Slice(v.ByteOffset, v.ByteLength);
    }

    /// <summary>An image's encoded bytes (PNG, JPEG …): from its buffer view (no copy) or its URI.</summary>
    /// <summary>
    /// The file an image refers to by a relative <c>uri</c> (no buffer view, not a data URI: the models repository's crowd keeps
    /// the maps its people share once, beside them, as <c>../shared/&lt;file&gt;</c>), as a full path; null for an image held in the file.
    /// </summary>
    public string? ImageFile(int index)
    {
        var im = Images[index];
        if (im.BufferView >= 0 || im.Uri is null || im.Uri.StartsWith("data:", StringComparison.Ordinal) || folder is null) return null;
        return Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(im.Uri)));
    }

    public ArraySegment<byte> ImageBytes(int index)
    {
        var im = Images[index];
        if (im.BufferView >= 0) return View(im.BufferView);
        if (im.Uri is null) throw new InvalidDataException($"image {index} has neither a buffer view nor a uri");
        return FromUri(im.Uri);
    }

    // ----- accessors -----

    internal static int SizeOf(GltfComponentType t) => t switch
    {
        GltfComponentType.Byte or GltfComponentType.UnsignedByte => 1,
        GltfComponentType.Short or GltfComponentType.UnsignedShort => 2,
        GltfComponentType.UnsignedInt or GltfComponentType.Float => 4,
        _ => throw new InvalidDataException($"accessor component type {(int)t}"),
    };

    /// <summary>
    /// The byte offset of each component within an element: packed, except a <c>MAT2</c> or <c>MAT3</c> of one- or
    /// two-byte components, whose columns each start on four bytes (glTF 2.0, 3.6.2.4).
    /// </summary>
    static int ComponentOffset(GltfAccessor a, int c)
    {
        int size = a.ComponentSize;
        if (size < 4 && a.Type is "MAT2" or "MAT3")
        {
            int n = a.Type == "MAT2" ? 2 : 3;
            int column = (n * size + 3) & ~3;
            return c / n * column + c % n * size;
        }
        return c * size;
    }

    static int ElementSize(GltfAccessor a)
    {
        if (a.ComponentSize < 4 && a.Type is "MAT2" or "MAT3")
        {
            int n = a.Type == "MAT2" ? 2 : 3;
            return n * ((n * a.ComponentSize + 3) & ~3);
        }
        return a.Width * a.ComponentSize;
    }

    /// <summary>
    /// Accessor <paramref name="index"/> as floats, <see cref="GltfAccessor.Count"/> × its width; a normalised integer
    /// accessor as 0..1 (unsigned) or −1..1 (signed), any other integer as its value. Sparse values replace the base's.
    /// </summary>
    public float[] ReadFloats(int index) => Read(index, raw: false);

    /// <summary>Accessor <paramref name="index"/> as its integers, unnormalised (indices, joints); a float accessor is truncated.</summary>
    public uint[] ReadUInts(int index)
    {
        var a = Accessor(index);
        int width = a.Width;
        var result = new uint[a.Count * width];
        if (a.BufferView >= 0)
        {
            var data = View(a.BufferView).AsSpan();
            int stride = Stride(a);
            CheckRange(a, data.Length, stride);
            if (a.ComponentType == GltfComponentType.UnsignedShort && width == 1 && stride == 2 && a.ByteOffset % 2 == 0)
            {
                var src = MemoryMarshal.Cast<byte, ushort>(data.Slice(a.ByteOffset, a.Count * 2));
                for (int i = 0; i < src.Length; i++) result[i] = BitConverter.IsLittleEndian ? src[i] : BinaryPrimitives.ReverseEndianness(src[i]);
            }
            else
                for (int i = 0; i < a.Count; i++)
                    for (int c = 0; c < width; c++)
                        result[i * width + c] = IntComponent(data, a.ByteOffset + i * stride + ComponentOffset(a, c), a.ComponentType);
        }
        if (a.Sparse is { } s)
        {
            var (idx, vals) = SparseViews(a, s);
            for (int k = 0; k < s.Count; k++)
            {
                int target = (int)IntComponent(idx, s.IndicesByteOffset + k * SizeOf(s.IndicesComponentType), s.IndicesComponentType);
                if ((uint)target >= (uint)a.Count) throw new InvalidDataException($"accessor {index}: a sparse index {target} past its {a.Count}");
                for (int c = 0; c < width; c++)
                    result[target * width + c] = IntComponent(vals, s.ValuesByteOffset + k * ElementSize(a) + ComponentOffset(a, c), a.ComponentType);
            }
        }
        return result;
    }

    /// <summary>A <c>VEC2</c> accessor.</summary>
    public Vector2[] ReadVector2(int index) { var f = Read(index, false, 2); var r = new Vector2[f.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = new(f[2 * i], f[2 * i + 1]); return r; }

    /// <summary>A <c>VEC3</c> accessor.</summary>
    public Vector3[] ReadVector3(int index) { var f = Read(index, false, 3); var r = new Vector3[f.Length / 3]; for (int i = 0; i < r.Length; i++) r[i] = new(f[3 * i], f[3 * i + 1], f[3 * i + 2]); return r; }

    /// <summary>A <c>VEC4</c> accessor (a <c>VEC3</c> one with 1 as its fourth: a colour without alpha).</summary>
    public Vector4[] ReadVector4(int index)
    {
        var a = Accessor(index);
        if (a.Type == "VEC3")
        {
            var v3 = ReadVector3(index);
            var r3 = new Vector4[v3.Length];
            for (int i = 0; i < r3.Length; i++) r3[i] = new Vector4(v3[i], 1f);
            return r3;
        }
        var f = Read(index, false, 4);
        var r = new Vector4[f.Length / 4];
        for (int i = 0; i < r.Length; i++) r[i] = new(f[4 * i], f[4 * i + 1], f[4 * i + 2], f[4 * i + 3]);
        return r;
    }

    /// <summary>A <c>VEC4</c> accessor as quaternions (x, y, z, w).</summary>
    public Quaternion[] ReadQuaternions(int index) { var f = Read(index, false, 4); var r = new Quaternion[f.Length / 4]; for (int i = 0; i < r.Length; i++) r[i] = new(f[4 * i], f[4 * i + 1], f[4 * i + 2], f[4 * i + 3]); return r; }

    /// <summary>A <c>MAT4</c> accessor as System.Numerics matrices (glTF's column-major column-vector matrix is, element for element, the row-vector one).</summary>
    public Matrix4x4[] ReadMatrices(int index)
    {
        var f = Read(index, false, 16);
        var r = new Matrix4x4[f.Length / 16];
        for (int i = 0; i < r.Length; i++)
        {
            int o = 16 * i;
            r[i] = new Matrix4x4(f[o], f[o + 1], f[o + 2], f[o + 3], f[o + 4], f[o + 5], f[o + 6], f[o + 7],
                                 f[o + 8], f[o + 9], f[o + 10], f[o + 11], f[o + 12], f[o + 13], f[o + 14], f[o + 15]);
        }
        return r;
    }

    GltfAccessor Accessor(int index) =>
        (uint)index < (uint)Accessors.Count ? Accessors[index] : throw new InvalidDataException($"accessor {index} of {Accessors.Count}");

    int Stride(GltfAccessor a)
    {
        int stride = BufferViews[a.BufferView].ByteStride;
        return stride > 0 ? stride : ElementSize(a);
    }

    static void CheckRange(GltfAccessor a, int viewLength, int stride)
    {
        if (a.Count == 0) return;
        long end = a.ByteOffset + (long)(a.Count - 1) * stride + ElementSize(a);
        if (a.ByteOffset < 0 || end > viewLength) throw new InvalidDataException($"an accessor of {a.Count} {a.Type} runs past its buffer view ({end} > {viewLength})");
    }

    float[] Read(int index, bool raw, int width = 0)
    {
        var a = Accessor(index);
        if (width > 0 && a.Width != width) throw new InvalidDataException($"accessor {index} is {a.Type}, not of width {width}");
        int w = a.Width;
        var result = new float[a.Count * w];
        bool normalized = !raw && a.Normalized;
        if (a.BufferView >= 0)
        {
            var data = View(a.BufferView).AsSpan();
            int stride = Stride(a);
            CheckRange(a, data.Length, stride);
            if (a.ComponentType == GltfComponentType.Float && stride == w * 4 && BitConverter.IsLittleEndian)
                MemoryMarshal.Cast<byte, float>(data.Slice(a.ByteOffset, a.Count * w * 4)).CopyTo(result);
            else
                for (int i = 0; i < a.Count; i++)
                    for (int c = 0; c < w; c++)
                        result[i * w + c] = Component(data, a.ByteOffset + i * stride + ComponentOffset(a, c), a.ComponentType, normalized);
        }
        if (a.Sparse is { } s)
        {
            var (idx, vals) = SparseViews(a, s);
            for (int k = 0; k < s.Count; k++)
            {
                int target = (int)IntComponent(idx, s.IndicesByteOffset + k * SizeOf(s.IndicesComponentType), s.IndicesComponentType);
                if ((uint)target >= (uint)a.Count) throw new InvalidDataException($"accessor {index}: a sparse index {target} past its {a.Count}");
                for (int c = 0; c < w; c++)
                    result[target * w + c] = Component(vals, s.ValuesByteOffset + k * ElementSize(a) + ComponentOffset(a, c), a.ComponentType, normalized);
            }
        }
        return result;
    }

    (byte[] Indices, byte[] Values) SparseViews(GltfAccessor a, GltfSparse s)
    {
        var idx = View(s.IndicesBufferView).ToArray();
        var vals = View(s.ValuesBufferView).ToArray();
        if (s.Count > 0 && (s.IndicesByteOffset + (long)s.Count * SizeOf(s.IndicesComponentType) > idx.Length
                            || s.ValuesByteOffset + (long)s.Count * ElementSize(a) > vals.Length))
            throw new InvalidDataException($"a sparse accessor of {s.Count} runs past its buffer views");
        return (idx, vals);
    }

    static float Component(ReadOnlySpan<byte> d, int at, GltfComponentType type, bool normalized) => type switch
    {
        GltfComponentType.Float => BinaryPrimitives.ReadSingleLittleEndian(d[at..]),
        GltfComponentType.UnsignedInt => normalized ? (float)(BinaryPrimitives.ReadUInt32LittleEndian(d[at..]) / 4294967295.0) : BinaryPrimitives.ReadUInt32LittleEndian(d[at..]),
        GltfComponentType.UnsignedShort => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(d[at..]) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(d[at..]),
        GltfComponentType.Short => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(d[at..]) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(d[at..]),
        GltfComponentType.UnsignedByte => normalized ? d[at] / 255f : d[at],
        GltfComponentType.Byte => normalized ? Math.Max((sbyte)d[at] / 127f, -1f) : (sbyte)d[at],
        _ => throw new InvalidDataException($"accessor component type {(int)type}"),
    };

    static uint IntComponent(ReadOnlySpan<byte> d, int at, GltfComponentType type) => type switch
    {
        GltfComponentType.UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(d[at..]),
        GltfComponentType.UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(d[at..]),
        GltfComponentType.Short => (uint)BinaryPrimitives.ReadInt16LittleEndian(d[at..]),
        GltfComponentType.UnsignedByte => d[at],
        GltfComponentType.Byte => (uint)(sbyte)d[at],
        GltfComponentType.Float => (uint)BinaryPrimitives.ReadSingleLittleEndian(d[at..]),
        _ => throw new InvalidDataException($"accessor component type {(int)type}"),
    };
}
