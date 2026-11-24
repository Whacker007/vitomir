// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/GltfReader.cs at b4185ef, reworked for the platform.
using System.Numerics;
using System.Text.Json;

namespace Vitomir.Platform.Render.Modern;

/// <summary>How <see cref="GltfReader"/> places a file in the engine's world.</summary>
/// <param name="UnitsPerMetre">Engine units per glTF metre: 72 (Christof's 128 units are 1.78 m).</param>
/// <param name="VertexColours">Whether <c>COLOR_0</c> tints the base colour, as glTF says it does. Null: yes,
/// except for a file from Vitomir's own level exporter, whose vertex colours are 1.1's baked light, not paint.</param>
/// <param name="DeferImages">Ours: leave the images undecoded (<see cref="GltfImage.Encoded"/>), and read a
/// <c>.glb</c>'s binary chunk from the file a buffer view at a time rather than whole — the cook's way, so a town of
/// 2K–4K maps is never all in memory at once.</param>
internal sealed record GltfReadOptions(float UnitsPerMetre = 72f, bool? VertexColours = null, bool DeferImages = false);

/// <summary>
/// Reads a glTF 2.0 file, <c>.gltf</c> (with its <c>.bin</c> and images beside it, or data URIs) or
/// <c>.glb</c>, into a <see cref="GltfScene"/> (ours). What it takes: the default scene's node tree
/// with translation, rotation, scale or matrix; triangle, strip and fan primitives, indexed or not; every
/// accessor component type, normalised or not, strided or packed, and sparse; the metallic-roughness
/// material with all five textures, its alpha mode and double-sidedness, and
/// <c>KHR_materials_emissive_strength</c>; PNG images (JPEG is not decoded yet: such a texture reads as
/// white and a warning says so).
///
/// **Axes and scale.** glTF is Y up, right-handed, in metres; the engine is Z up, right-handed, in units
/// of 1/72 m. A point goes over as engine (x, y, z) = 72 · (x, −z, y), the inverse of the level
/// exporter's glTF (x, y, z) = s · NIL (x, z, −y) — a quarter turn about x,
/// which keeps triangles' winding. Every node transform, and this change, is baked into the vertices:
/// the modern path draws a static scene as world-space triangle lists.
///
/// Missing normals are made flat per triangle (glTF's rule); missing tangents, where a normal map needs
/// them, are made from the texture coordinates.
/// </summary>
internal static class GltfReader
{
    const uint GlbMagic = 0x46546C67, ChunkJson = 0x4E4F534A, ChunkBin = 0x004E4942;

    /// <summary>Reads the file at <paramref name="path"/>.</summary>
    public static GltfScene Load(string path, GltfReadOptions? options = null)
    {
        if (options?.DeferImages == true && OpenGlb(path) is { } glb)
        {
            using var jsonDoc = JsonDocument.Parse(glb.Json);
            return new Reader(jsonDoc.RootElement, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", null, options) { Disk = glb.Bin }.Build();
        }
        var bytes = File.ReadAllBytes(path);
        return Read(bytes, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", options);
    }

    /// <summary>Reads a file's bytes; <paramref name="folder"/> is where relative URIs are resolved.</summary>
    public static GltfScene Read(byte[] bytes, string folder, GltfReadOptions? options = null)
    {
        options ??= new GltfReadOptions();
        byte[]? glbBin = null;
        ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 12 && BitConverter.ToUInt32(bytes, 0) == GlbMagic)
        {
            int at = 12;
            json = ReadOnlyMemory<byte>.Empty;
            while (at + 8 <= bytes.Length)
            {
                int length = BitConverter.ToInt32(bytes, at);
                uint type = BitConverter.ToUInt32(bytes, at + 4);
                if (type == ChunkJson) json = bytes.AsMemory(at + 8, length);
                else if (type == ChunkBin) glbBin = bytes.AsSpan(at + 8, length).ToArray();
                at += 8 + length + (4 - length % 4) % 4;
            }
        }
        using var doc = JsonDocument.Parse(json);
        return new Reader(doc.RootElement, folder, glbBin, options).Build();
    }

    /// <summary>A <c>.glb</c>'s binary chunk left in its file (ours): the file and where the chunk starts and how long it is.</summary>
    sealed record DiskChunk(string Path, long Start, long Length)
    {
        /// <summary>Bytes of the chunk, read from the file (a handle of its own each time, so any thread may call it).</summary>
        public byte[] Read(long offset, int length)
        {
            if (offset < 0 || offset + length > Length) throw new InvalidDataException($"a buffer view past the end of the binary chunk ({offset} + {length} > {Length})");
            var bytes = new byte[length];
            using var handle = File.OpenHandle(Path);
            int got = 0;
            while (got < length)
            {
                int n = RandomAccess.Read(handle, bytes.AsSpan(got), Start + offset + got);
                if (n <= 0) throw new EndOfStreamException($"{Path}: the binary chunk ends early");
                got += n;
            }
            return bytes;
        }
    }

    /// <summary>A <c>.glb</c>'s JSON chunk, and where its binary chunk is in the file; null if the file is not a <c>.glb</c>.</summary>
    static (byte[] Json, DiskChunk? Bin)? OpenGlb(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> head = stackalloc byte[12];
        if (file.Read(head) < 12 || BitConverter.ToUInt32(head) != GlbMagic) return null;
        byte[]? json = null; DiskChunk? bin = null;
        long at = 12;
        while (at + 8 <= file.Length)
        {
            file.Position = at;
            file.ReadExactly(head[..8]);
            int length = BitConverter.ToInt32(head), type = (int)BitConverter.ToUInt32(head[4..]);
            if ((uint)type == ChunkJson) { json = new byte[length]; file.ReadExactly(json); }
            else if ((uint)type == ChunkBin) bin = new DiskChunk(path, at + 8, length);
            at += 8 + length + (4 - length % 4) % 4;
        }
        return json is null ? null : (json, bin);
    }

    /// <summary>The change from glTF's axes and metres to the engine's, as a row-vector matrix (p' = p · M).</summary>
    public static Matrix4x4 ToEngine(float unitsPerMetre) => new(
        unitsPerMetre, 0, 0, 0,
        0, 0, unitsPerMetre, 0,
        0, -unitsPerMetre, 0, 0,
        0, 0, 0, 1);

    sealed class Reader(JsonElement root, string folder, byte[]? glbBin, GltfReadOptions options)
    {
        readonly GltfScene scene = new();
        readonly Dictionary<int, byte[]> buffers = [];
        readonly Dictionary<int, byte[]> diskViews = [];
        bool vertexColours;
        /// <summary>A <c>.glb</c>'s binary chunk left in its file (<see cref="GltfReadOptions.DeferImages"/>): its views are read one at a time.</summary>
        public DiskChunk? Disk { get; init; }

        public GltfScene Build()
        {
            string generator = root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("generator", out var g) ? g.GetString() ?? "" : "";
            vertexColours = options.VertexColours ?? !generator.StartsWith("Vitomir level exporter", StringComparison.Ordinal);
            if (root.TryGetProperty("extensionsRequired", out var req))
                foreach (var e in req.EnumerateArray())
                    if (e.GetString() is { } name && name != "KHR_materials_emissive_strength")
                        scene.Warnings.Add($"required extension {name} is not supported; drawn without it");
            ReadImages();
            ReadTextures();
            ReadMaterials();
            foreach (int node in RootNodes()) Walk(node, Matrix4x4.Identity, 0);
            return scene;
        }

        IEnumerable<int> RootNodes()
        {
            if (root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
            {
                int which = root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0;
                if (scenes[which].TryGetProperty("nodes", out var nodes)) return nodes.EnumerateArray().Select(n => n.GetInt32()).ToArray();
                return [];
            }
            // No scenes: every node nobody names as a child is a root.
            if (!root.TryGetProperty("nodes", out var all)) return [];
            var children = new HashSet<int>();
            foreach (var n in all.EnumerateArray())
                if (n.TryGetProperty("children", out var c)) foreach (var i in c.EnumerateArray()) children.Add(i.GetInt32());
            return Enumerable.Range(0, all.GetArrayLength()).Where(i => !children.Contains(i)).ToArray();
        }

        void Walk(int index, Matrix4x4 parent, int depth)
        {
            if (depth > 256) { scene.Warnings.Add("node tree deeper than 256; cut off"); return; }
            var node = root.GetProperty("nodes")[index];
            var world = LocalMatrix(node) * parent;
            if (node.TryGetProperty("mesh", out var mesh)) AddMesh(mesh.GetInt32(), world, node.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "");
            if (node.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("KHR_lights_punctual", out var lp) && lp.TryGetProperty("light", out var li))
                AddLight(li.GetInt32(), world);
            if (node.TryGetProperty("children", out var children))
                foreach (var c in children.EnumerateArray()) Walk(c.GetInt32(), world, depth + 1);
        }

        /// <summary>
        /// A <c>KHR_lights_punctual</c> light at its node: the node's origin, and its −Z axis as the
        /// direction a spot or directional light shines in, both carried to the engine's axes and units.
        /// </summary>
        void AddLight(int index, Matrix4x4 world)
        {
            if (!root.TryGetProperty("extensions", out var ext) || !ext.TryGetProperty("KHR_lights_punctual", out var lp)
                || !lp.TryGetProperty("lights", out var lights) || index < 0 || index >= lights.GetArrayLength())
            {
                scene.Warnings.Add($"KHR_lights_punctual light {index} is missing; left out");
                return;
            }
            var l = lights[index];
            var type = (l.TryGetProperty("type", out var t) ? t.GetString() : "") switch
            {
                "directional" => GltfLightType.Directional, "spot" => GltfLightType.Spot, _ => GltfLightType.Point,
            };
            float units = ModernMath.UnitsPerMetre;
            var toEngine = world * ToEngine(units);
            var position = Vector3.Transform(Vector3.Zero, toEngine);
            var direction = Vector3.TransformNormal(-Vector3.UnitZ, world * ToEngine(1f));
            direction = direction.LengthSquared() > 0 ? Vector3.Normalize(direction) : -Vector3.UnitZ;
            float inner = 0f, outer = MathF.PI / 4f;
            if (l.TryGetProperty("spot", out var spot))
            {
                if (spot.TryGetProperty("innerConeAngle", out var ic)) inner = ic.GetSingle();
                if (spot.TryGetProperty("outerConeAngle", out var oc)) outer = oc.GetSingle();
            }
            scene.Lights.Add(new GltfLight(
                l.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", type, position, direction,
                l.TryGetProperty("color", out var c) ? Vec3(c) : Vector3.One,
                l.TryGetProperty("intensity", out var i) ? i.GetSingle() : 1f,
                l.TryGetProperty("range", out var r) ? r.GetSingle() * units : 0f, inner, outer));
        }

        /// <summary>A node's own transform: its matrix (column-major in the file), else T · R · S.</summary>
        public static Matrix4x4 LocalMatrix(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out var m))
            {
                var f = m.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                // glTF's column-major column-vector matrix is, element for element, System.Numerics' row-vector one.
                return new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
            }
            var t = node.TryGetProperty("translation", out var tr) ? Vec3(tr) : Vector3.Zero;
            var r = node.TryGetProperty("rotation", out var ro) ? Quat(ro) : Quaternion.Identity;
            var s = node.TryGetProperty("scale", out var sc) ? Vec3(sc) : Vector3.One;
            return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }

        void AddMesh(int meshIndex, Matrix4x4 world, string nodeName)
        {
            var mesh = root.GetProperty("meshes")[meshIndex];
            string name = mesh.TryGetProperty("name", out var n) ? n.GetString() ?? nodeName : nodeName;
            Matrix4x4.Invert(world, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);
            float det = world.GetDeterminant();
            int p = 0;
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var built = Primitive(prim, world, normalMatrix, det < 0, $"{name}#{p++}");
                if (built is null) continue;
                scene.Primitives.Add(built);
                scene.Min = Vector3.Min(scene.Min, built.Min);
                scene.Max = Vector3.Max(scene.Max, built.Max);
            }
        }

        GltfPrimitive? Primitive(JsonElement prim, Matrix4x4 world, Matrix4x4 normalMatrix, bool mirrored, string name)
        {
            int mode = prim.TryGetProperty("mode", out var md) ? md.GetInt32() : 4;
            if (mode is not (4 or 5 or 6)) { scene.Warnings.Add($"{name}: primitive mode {mode} (points or lines) skipped"); return null; }
            var attrs = prim.GetProperty("attributes");
            if (!attrs.TryGetProperty("POSITION", out var posAcc)) return null;
            var pos = Floats(posAcc.GetInt32(), out int count);
            float[]? nrm = attrs.TryGetProperty("NORMAL", out var na) ? Floats(na.GetInt32(), out _) : null;
            float[]? tan = attrs.TryGetProperty("TANGENT", out var ta) ? Floats(ta.GetInt32(), out _) : null;
            float[]? uv0 = attrs.TryGetProperty("TEXCOORD_0", out var u0) ? Floats(u0.GetInt32(), out _) : null;
            float[]? uv1 = attrs.TryGetProperty("TEXCOORD_1", out var u1) ? Floats(u1.GetInt32(), out _) : null;
            float[]? col = null; int colWidth = 4;
            if (vertexColours && attrs.TryGetProperty("COLOR_0", out var ca))
            {
                col = Floats(ca.GetInt32(), out _);
                colWidth = Width(root.GetProperty("accessors")[ca.GetInt32()].GetProperty("type").GetString()!);
            }

            var v = new ModernVertex[count];
            for (int i = 0; i < count; i++)
            {
                v[i].Position = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                if (nrm is not null) v[i].Normal = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                if (tan is not null) v[i].Tangent = new Vector4(tan[i * 4], tan[i * 4 + 1], tan[i * 4 + 2], tan[i * 4 + 3]);
                if (uv0 is not null) v[i].Uv0 = new Vector2(uv0[i * 2], uv0[i * 2 + 1]);
                if (uv1 is not null) v[i].Uv1 = new Vector2(uv1[i * 2], uv1[i * 2 + 1]);
                v[i].Color = col is null ? 0xffffffffu : Pack(col, i, colWidth);
            }

            uint[] indices;
            if (prim.TryGetProperty("indices", out var ia))
            {
                var f = Floats(ia.GetInt32(), out int n, raw: true);
                indices = new uint[n];
                for (int i = 0; i < n; i++) indices[i] = (uint)f[i];
            }
            else indices = Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
            indices = mode switch { 5 => Strip(indices), 6 => Fan(indices), _ => indices };
            indices = indices.Where((_, i) => i < indices.Length / 3 * 3).ToArray();
            if (indices.Any(i => i >= count)) { scene.Warnings.Add($"{name}: an index past the vertices; skipped"); return null; }

            // Flat normals when the file has none: each triangle gets its own three vertices.
            if (nrm is null)
            {
                var flat = new ModernVertex[indices.Length];
                for (int t = 0; t < indices.Length; t += 3)
                {
                    var a = v[indices[t]]; var b = v[indices[t + 1]]; var c = v[indices[t + 2]];
                    var fn = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                    fn = fn.LengthSquared() > 0 ? Vector3.Normalize(fn) : Vector3.UnitY;
                    a.Normal = b.Normal = c.Normal = fn;
                    flat[t] = a; flat[t + 1] = b; flat[t + 2] = c;
                }
                v = flat;
                indices = Enumerable.Range(0, flat.Length).Select(i => (uint)i).ToArray();
            }

            int material = prim.TryGetProperty("material", out var mt) ? mt.GetInt32() : DefaultMaterial();
            if (tan is null && scene.Materials[material].NormalTexture.Present)
                MakeTangents(v, indices, scene.Materials[material].NormalTexture.TexCoord == 1);

            // Into the engine's world.
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            for (int i = 0; i < v.Length; i++)
            {
                v[i].Position = Vector3.Transform(v[i].Position, world * ToEngineBaked);
                var nw = Vector3.TransformNormal(v[i].Normal, normalMatrix * ToEngineRotation);
                v[i].Normal = nw.LengthSquared() > 0 ? Vector3.Normalize(nw) : Vector3.UnitZ;
                var tw = Vector3.TransformNormal(new Vector3(v[i].Tangent.X, v[i].Tangent.Y, v[i].Tangent.Z), world * ToEngineRotation);
                tw = tw.LengthSquared() > 0 ? Vector3.Normalize(tw) : Vector3.UnitX;
                float w = v[i].Tangent.W == 0 ? 1 : MathF.Sign(v[i].Tangent.W);
                v[i].Tangent = new Vector4(tw, mirrored ? -w : w);
                min = Vector3.Min(min, v[i].Position); max = Vector3.Max(max, v[i].Position);
            }
            // A mirroring transform turns triangles over: swap two corners to keep them facing out.
            if (mirrored)
                for (int t = 0; t < indices.Length; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
            return new GltfPrimitive { Name = name, Vertices = v, Indices = indices, Material = material, Min = min, Max = max };
        }

        // The engine change is applied after the node's world matrix; `world` here is already in glTF's world,
        // so the conversion is multiplied on last (row vectors: p · world · toEngine).
        Matrix4x4 ToEngineBaked => ToEngine(options.UnitsPerMetre);
        static Matrix4x4 ToEngineRotation => ToEngine(1f);

        int defaultMaterial = -1;
        int DefaultMaterial()
        {
            if (defaultMaterial < 0) { defaultMaterial = scene.Materials.Count; scene.Materials.Add(GltfMaterial.Default()); }
            return defaultMaterial;
        }

        static uint[] Strip(uint[] s)
        {
            var o = new List<uint>();
            for (int i = 0; i + 2 < s.Length; i++)
                if (i % 2 == 0) o.AddRange([s[i], s[i + 1], s[i + 2]]); else o.AddRange([s[i + 1], s[i], s[i + 2]]);
            return o.ToArray();
        }

        static uint[] Fan(uint[] s)
        {
            var o = new List<uint>();
            for (int i = 1; i + 1 < s.Length; i++) o.AddRange([s[0], s[i], s[i + 1]]);
            return o.ToArray();
        }

        static uint Pack(float[] c, int i, int width)
        {
            float r = c[i * width], g = c[i * width + 1], b = c[i * width + 2], a = width == 4 ? c[i * width + 3] : 1f;
            static uint B(float x) => (uint)Math.Clamp(MathF.Round(x * 255f), 0, 255);
            return B(r) | B(g) << 8 | B(b) << 16 | B(a) << 24;
        }

        /// <summary>
        /// Per-vertex tangents from each triangle's texture-coordinate gradient, summed and made orthogonal to
        /// the normal, with the handedness glTF's normal maps expect (the bitangent up the image).
        /// </summary>
        public static void MakeTangents(ModernVertex[] v, uint[] idx, bool secondSet)
        {
            var tan = new Vector3[v.Length]; var bit = new Vector3[v.Length];
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                uint i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                var e1 = v[i1].Position - v[i0].Position; var e2 = v[i2].Position - v[i0].Position;
                var w0 = secondSet ? v[i0].Uv1 : v[i0].Uv0; var w1 = secondSet ? v[i1].Uv1 : v[i1].Uv0; var w2 = secondSet ? v[i2].Uv1 : v[i2].Uv0;
                var d1 = w1 - w0; var d2 = w2 - w0;
                float r = d1.X * d2.Y - d2.X * d1.Y;
                if (MathF.Abs(r) < 1e-12f) continue;
                r = 1f / r;
                var sdir = (e1 * d2.Y - e2 * d1.Y) * r;
                var tdir = (e2 * d1.X - e1 * d2.X) * r;
                foreach (var i in (ReadOnlySpan<uint>)[i0, i1, i2]) { tan[i] += sdir; bit[i] += tdir; }
            }
            for (int i = 0; i < v.Length; i++)
            {
                var n = v[i].Normal;
                var t = tan[i] - n * Vector3.Dot(n, tan[i]);
                if (t.LengthSquared() < 1e-20f)
                {
                    // No gradient: any direction across the normal.
                    t = Vector3.Cross(MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, n);
                    if (t.LengthSquared() < 1e-20f) t = Vector3.UnitX;
                }
                t = Vector3.Normalize(t);
                // glTF's normal maps have +Y up the image, and its v runs down the image, so the bitangent the
                // map means points along decreasing v: the sign that makes cross(n, t) · w point against the
                // gradient of v (as three.js flips a derivative basis for glTF; checked against Blender's own tangents).
                float w = Vector3.Dot(Vector3.Cross(n, t), bit[i]) < 0 ? 1f : -1f;
                v[i].Tangent = new Vector4(t, w);
            }
        }

        // ----- materials, textures, images -----

        void ReadMaterials()
        {
            if (!root.TryGetProperty("materials", out var mats)) return;
            foreach (var m in mats.EnumerateArray())
            {
                var mat = new GltfMaterial { Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "" };
                if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var bc)) mat.BaseColorFactor = Vec4(bc);
                    mat.BaseColorTexture = Slot(pbr, "baseColorTexture");
                    if (pbr.TryGetProperty("metallicFactor", out var mf)) mat.MetallicFactor = mf.GetSingle();
                    if (pbr.TryGetProperty("roughnessFactor", out var rf)) mat.RoughnessFactor = rf.GetSingle();
                    mat.MetallicRoughnessTexture = Slot(pbr, "metallicRoughnessTexture");
                }
                mat.NormalTexture = Slot(m, "normalTexture");
                if (m.TryGetProperty("normalTexture", out var nt) && nt.TryGetProperty("scale", out var ns)) mat.NormalScale = ns.GetSingle();
                mat.OcclusionTexture = Slot(m, "occlusionTexture");
                if (m.TryGetProperty("occlusionTexture", out var ot) && ot.TryGetProperty("strength", out var os)) mat.OcclusionStrength = os.GetSingle();
                if (m.TryGetProperty("emissiveFactor", out var ef)) mat.EmissiveFactor = Vec3(ef);
                mat.EmissiveTexture = Slot(m, "emissiveTexture");
                if (m.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("KHR_materials_emissive_strength", out var es)
                    && es.TryGetProperty("emissiveStrength", out var esv)) mat.EmissiveStrength = esv.GetSingle();
                if (m.TryGetProperty("extensions", out var ext2) && ext2.TryGetProperty("KHR_materials_specular", out var sp)
                    && sp.TryGetProperty("specularFactor", out var spf)) mat.SpecularFactor = spf.GetSingle();
                if (m.TryGetProperty("alphaMode", out var am))
                    mat.AlphaMode = am.GetString() switch { "MASK" => GltfAlphaMode.Mask, "BLEND" => GltfAlphaMode.Blend, _ => GltfAlphaMode.Opaque };
                if (m.TryGetProperty("alphaCutoff", out var ac)) mat.AlphaCutoff = ac.GetSingle();
                if (m.TryGetProperty("doubleSided", out var ds)) mat.DoubleSided = ds.GetBoolean();
                // Ours: a height map and foliage in the material's extras.
                if (m.TryGetProperty("extras", out var ex) && ex.ValueKind == JsonValueKind.Object)
                {
                    if (ex.TryGetProperty("height", out var hx) && hx.ValueKind == JsonValueKind.Object)
                    {
                        mat.HeightTexture = Slot(ex, "height");
                        if (hx.TryGetProperty("scale", out var hs)) mat.HeightScale = hs.GetSingle();
                    }
                    if (ex.TryGetProperty("foliage", out var fx) && fx.ValueKind == JsonValueKind.Object)
                    {
                        mat.Foliage = true;
                        if (fx.TryGetProperty("translucency", out var tr)) mat.Translucency = tr.GetSingle();
                        if (fx.TryGetProperty("wind", out var wd)) mat.Wind = wd.GetSingle();
                    }
                    mat.Tiling = GltfTilings.Read(ex);   // ours
                    if (ex.TryGetProperty("decal", out var dx) && dx.ValueKind == JsonValueKind.Object
                        && dx.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.String) mat.DecalOn = on.GetString();
                }
                scene.Materials.Add(mat);
            }
            GltfTilings.ResolveDecals(scene.Materials);
        }

        GltfTextureSlot Slot(JsonElement owner, string name)
        {
            if (!owner.TryGetProperty(name, out var t) || !t.TryGetProperty("index", out var i)) return GltfTextureSlot.None;
            int index = i.GetInt32();
            if (index < 0 || index >= scene.Textures.Count || scene.Images.ElementAtOrDefault(scene.Textures[index].Image)?.Present != true)
                return GltfTextureSlot.None;
            if (t.TryGetProperty("extensions", out var e) && e.TryGetProperty("KHR_texture_transform", out _))
                scene.Warnings.Add($"KHR_texture_transform on a {name} is not supported; drawn without it");
            return new GltfTextureSlot(index, t.TryGetProperty("texCoord", out var tc) ? tc.GetInt32() : 0);
        }

        void ReadTextures()
        {
            if (!root.TryGetProperty("textures", out var texs)) return;
            var samplers = root.TryGetProperty("samplers", out var s) ? s.EnumerateArray().ToArray() : [];
            foreach (var t in texs.EnumerateArray())
            {
                int image = t.TryGetProperty("source", out var src) ? src.GetInt32() : -1;
                int wrapS = 10497, wrapT = 10497;
                if (t.TryGetProperty("sampler", out var si) && si.GetInt32() < samplers.Length)
                {
                    var smp = samplers[si.GetInt32()];
                    if (smp.TryGetProperty("wrapS", out var ws)) wrapS = ws.GetInt32();
                    if (smp.TryGetProperty("wrapT", out var wt)) wrapT = wt.GetInt32();
                }
                scene.Textures.Add(new GltfTexture(image, wrapS != 33071, wrapT != 33071, wrapS == 33648, wrapT == 33648));
            }
        }

        void ReadImages()
        {
            if (!root.TryGetProperty("images", out var imgs)) return;
            foreach (var im in imgs.EnumerateArray())
            {
                var image = new GltfImage
                {
                    Name = im.TryGetProperty("name", out var n) ? n.GetString() ?? "" : im.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "",
                    MimeType = im.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "" : "",
                };
                byte[]? data = null;
                try
                {
                    if (options.DeferImages && Deferred(im) is { } read)
                    {
                        // Ours: only the PNG's header now (its size); the pixels when the cook wants them.
                        var head = read(24);
                        if (PngDecoder.IsPng(head) && head.Length >= 24)
                        {
                            image.Width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16));
                            image.Height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20));
                            image.Encoded = () => read(int.MaxValue);
                            scene.Images.Add(image);
                            continue;
                        }
                        data = read(int.MaxValue);
                    }
                    else if (im.TryGetProperty("bufferView", out var bv)) data = View(bv.GetInt32()).ToArray();
                    else if (im.TryGetProperty("uri", out var uri)) data = Uri(uri.GetString()!);
                }
                catch (Exception e) { scene.Warnings.Add($"image {image.Name}: {e.Message}"); }
                if (data is not null && PngDecoder.IsPng(data))
                {
                    image.Rgba = PngDecoder.Decode(data, out image.Width, out image.Height);
                    if (image.Rgba is null) scene.Warnings.Add($"image {image.Name}: a PNG this reader cannot decode; drawn as white");
                }
                else if (data is not null)
                    scene.Warnings.Add($"image {image.Name}: {(data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8 ? "JPEG" : "an image format")} is not decoded yet; drawn as white");
                scene.Images.Add(image);
            }
        }

        /// <summary>
        /// An image's first so many bytes, read again at each call (ours, <see cref="GltfReadOptions.DeferImages"/>): a
        /// view of a <c>.glb</c>'s binary chunk from the file, a file beside the glTF, or a data URI's bytes (kept);
        /// null when the image is in a buffer held in memory, which is read as before.
        /// </summary>
        Func<int, byte[]>? Deferred(JsonElement im)
        {
            if (im.TryGetProperty("bufferView", out var bv))
            {
                var view = root.GetProperty("bufferViews")[bv.GetInt32()];
                var buf = root.GetProperty("buffers")[view.GetProperty("buffer").GetInt32()];
                long offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt64() : 0;
                int length = view.GetProperty("byteLength").GetInt32();
                if (buf.TryGetProperty("uri", out var bu) && !bu.GetString()!.StartsWith("data:", StringComparison.Ordinal))
                {
                    var file = Path.Combine(folder, System.Uri.UnescapeDataString(bu.GetString()!));
                    var chunk = new DiskChunk(file, 0, new FileInfo(file).Length);
                    return n => chunk.Read(offset, Math.Min(n, length));
                }
                if (!buf.TryGetProperty("uri", out _) && Disk is { } disk) return n => disk.Read(offset, Math.Min(n, length));
                return null;
            }
            if (im.TryGetProperty("uri", out var u) && u.GetString() is { } uri)
            {
                if (uri.StartsWith("data:", StringComparison.Ordinal)) { var bytes = Uri(uri); return n => n >= bytes.Length ? bytes : bytes[..n]; }
                var file = Path.Combine(folder, System.Uri.UnescapeDataString(uri));
                var whole = new DiskChunk(file, 0, new FileInfo(file).Length);
                return n => whole.Read(0, (int)Math.Min(n, whole.Length));
            }
            return null;
        }

        // ----- buffers and accessors -----

        byte[] Uri(string uri)
        {
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int comma = uri.IndexOf(',');
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            return File.ReadAllBytes(Path.Combine(folder, System.Uri.UnescapeDataString(uri)));
        }

        byte[] Buffer(int index)
        {
            if (buffers.TryGetValue(index, out var b)) return b;
            var buf = root.GetProperty("buffers")[index];
            b = buf.TryGetProperty("uri", out var uri) ? Uri(uri.GetString()!)
                : glbBin ?? throw new InvalidDataException($"buffer {index} has no uri and the file has no binary chunk");
            buffers[index] = b;
            return b;
        }

        ReadOnlySpan<byte> View(int index)
        {
            var view = root.GetProperty("bufferViews")[index];
            int buffer = view.GetProperty("buffer").GetInt32();
            // A .glb's binary chunk left in its file (ours, DeferImages): this view alone, read once.
            if (Disk is { } disk && !root.GetProperty("buffers")[buffer].TryGetProperty("uri", out _))
            {
                if (!diskViews.TryGetValue(index, out var bytes))
                    diskViews[index] = bytes = disk.Read(view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt64() : 0, view.GetProperty("byteLength").GetInt32());
                return bytes;
            }
            var b = Buffer(buffer);
            int offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            return b.AsSpan(offset, view.GetProperty("byteLength").GetInt32());
        }

        static int Width(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16, _ => 1 };
        static int Size(int componentType) => componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };

        /// <summary>
        /// An accessor as floats, <paramref name="count"/> elements of its width. Normalised integers become
        /// 0..1 (or −1..1); <paramref name="raw"/> keeps integers as they are (indices).
        /// </summary>
        float[] Floats(int accessorIndex, out int count, bool raw = false)
        {
            var acc = root.GetProperty("accessors")[accessorIndex];
            count = acc.GetProperty("count").GetInt32();
            int width = Width(acc.GetProperty("type").GetString()!);
            int ct = acc.GetProperty("componentType").GetInt32();
            bool normalized = !raw && acc.TryGetProperty("normalized", out var nz) && nz.GetBoolean();
            var result = new float[count * width];
            if (acc.TryGetProperty("bufferView", out var bvi))
            {
                var view = root.GetProperty("bufferViews")[bvi.GetInt32()];
                var data = View(bvi.GetInt32());
                int offset = acc.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                int stride = view.TryGetProperty("byteStride", out var st) ? st.GetInt32() : width * Size(ct);
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < width; c++)
                        result[i * width + c] = Component(data, offset + i * stride + c * Size(ct), ct, normalized);
            }
            if (acc.TryGetProperty("sparse", out var sparse))
            {
                int n = sparse.GetProperty("count").GetInt32();
                var si = sparse.GetProperty("indices"); var sv = sparse.GetProperty("values");
                var idata = View(si.GetProperty("bufferView").GetInt32());
                int ioff = si.TryGetProperty("byteOffset", out var io) ? io.GetInt32() : 0;
                int ict = si.GetProperty("componentType").GetInt32();
                var vdata = View(sv.GetProperty("bufferView").GetInt32());
                int voff = sv.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
                for (int k = 0; k < n; k++)
                {
                    int target = (int)Component(idata, ioff + k * Size(ict), ict, false);
                    for (int c = 0; c < width; c++)
                        result[target * width + c] = Component(vdata, voff + (k * width + c) * Size(ct), ct, normalized);
                }
            }
            return result;
        }

        static float Component(ReadOnlySpan<byte> d, int at, int type, bool normalized) => type switch
        {
            5126 => BitConverter.ToSingle(d.Slice(at, 4)),
            5125 => BitConverter.ToUInt32(d.Slice(at, 4)),
            5123 => normalized ? BitConverter.ToUInt16(d.Slice(at, 2)) / 65535f : BitConverter.ToUInt16(d.Slice(at, 2)),
            5122 => normalized ? Math.Max(BitConverter.ToInt16(d.Slice(at, 2)) / 32767f, -1f) : BitConverter.ToInt16(d.Slice(at, 2)),
            5121 => normalized ? d[at] / 255f : d[at],
            5120 => normalized ? Math.Max((sbyte)d[at] / 127f, -1f) : (sbyte)d[at],
            _ => throw new InvalidDataException($"accessor component type {type}"),
        };

        static Vector3 Vec3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        static Vector4 Vec4(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());
        static Quaternion Quat(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());
    }
}
