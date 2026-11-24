// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The modern path's glTF reader (<see cref="GltfReader"/>, ours) on small files written here: the node
/// tree's transforms and the change to the engine's axes and units, accessors of every shape it meets
/// (strided, normalised, sparse, missing), materials with every field, embedded PNGs and the binary
/// <c>.glb</c> container. No GPU and no game files.
/// </summary>
public class ModernGltfTests
{
    /// <summary>Builds a glTF's JSON and one binary buffer, the buffer embedded as a data URI or kept for a .glb.</summary>
    sealed class Builder
    {
        public readonly JsonObject Root = new() { ["asset"] = new JsonObject { ["version"] = "2.0" } };
        readonly MemoryStream bin = new();
        readonly JsonArray views = [], accessors = [];

        public int View(byte[] data, int stride = 0)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var v = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (stride > 0) v["byteStride"] = stride;
            bin.Write(data);
            views.Add(v);
            return views.Count - 1;
        }

        public int Accessor(int view, int componentType, int count, string type, bool normalized = false, int offset = 0)
        {
            var a = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (normalized) a["normalized"] = true;
            if (offset > 0) a["byteOffset"] = offset;
            accessors.Add(a);
            return accessors.Count - 1;
        }

        public JsonArray Accessors => accessors;

        public int Floats(string type, params float[] f) =>
            Accessor(View(f.SelectMany(BitConverter.GetBytes).ToArray()), 5126, f.Length / (type == "VEC3" ? 3 : type == "VEC2" ? 2 : type == "VEC4" ? 4 : 1), type);

        public int Shorts(params ushort[] s) => Accessor(View(s.SelectMany(BitConverter.GetBytes).ToArray()), 5123, s.Length, "SCALAR");

        void Finish(JsonObject buffer)
        {
            Root["buffers"] = new JsonArray(buffer);
            Root["bufferViews"] = views;
            Root["accessors"] = accessors;
        }

        public byte[] Gltf()
        {
            Finish(new JsonObject { ["byteLength"] = bin.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bin.ToArray()) });
            return Encoding.UTF8.GetBytes(Root.ToJsonString());
        }

        public byte[] Glb()
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            Finish(new JsonObject { ["byteLength"] = bin.Length });
            var json = Encoding.UTF8.GetBytes(Root.ToJsonString());
            int pad = (4 - json.Length % 4) % 4;
            json = json.Concat(Enumerable.Repeat((byte)' ', pad)).ToArray();
            var o = new MemoryStream();
            var w = new BinaryWriter(o);
            w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + json.Length + 8 + bin.Length));
            w.Write((uint)json.Length); w.Write(0x4E4F534Au); w.Write(json);
            w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin.ToArray());
            return o.ToArray();
        }
    }

    /// <summary>One triangle in the glTF's x–y plane (facing +z), with normals, a parent node that moves and a child that turns and scales.</summary>
    static Builder Triangle(Action<Builder, JsonObject>? more = null)
    {
        var b = new Builder();
        int pos = b.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        int nrm = b.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1);
        int idx = b.Shorts(0, 1, 2);
        var prim = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["NORMAL"] = nrm }, ["indices"] = idx };
        b.Root["meshes"] = new JsonArray(new JsonObject { ["name"] = "tri", ["primitives"] = new JsonArray(prim) });
        // Parent: 10 m along glTF +x. Child: a quarter turn about glTF +y, then twice the size.
        float s = MathF.Sqrt(0.5f);
        b.Root["nodes"] = new JsonArray(
            new JsonObject { ["translation"] = new JsonArray(10, 0, 0), ["children"] = new JsonArray(1) },
            new JsonObject { ["mesh"] = 0, ["rotation"] = new JsonArray(0, s, 0, s), ["scale"] = new JsonArray(2, 2, 2) });
        b.Root["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) });
        b.Root["scene"] = 0;
        more?.Invoke(b, prim);
        return b;
    }

    static GltfScene Read(byte[] bytes, GltfReadOptions? o = null) => GltfReader.Read(bytes, Path.GetTempPath(), o);

    static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void Node_transforms_and_the_change_to_engine_axes_place_every_vertex()
    {
        var scene = Read(Triangle().Gltf());
        var p = Assert.Single(scene.Primitives);
        // glTF: child turns (x → −z), scales by 2, parent adds x 10.
        //   (0,0,0) → (10,0,0); (1,0,0) → (10,0,−2); (0,1,0) → (10,2,0).
        // Engine = 72 · (x, −z, y).
        Near(new Vector3(720, 0, 0), p.Vertices[0].Position);
        Near(new Vector3(720, 144, 0), p.Vertices[1].Position);
        Near(new Vector3(720, 0, 144), p.Vertices[2].Position);
        // The normal (glTF +z) turns to glTF +x, which is engine +x, and stays unit length.
        Near(Vector3.UnitX, p.Vertices[0].Normal);
        Assert.Equal([0u, 1u, 2u], p.Indices);
        Near(new Vector3(720, 0, 0), scene.Min);
        Near(new Vector3(720, 144, 144), scene.Max);
    }

    [Fact]
    public void The_triangle_still_faces_the_way_its_normal_says_after_the_change_of_axes()
    {
        var p = Read(Triangle().Gltf()).Primitives[0];
        var v = p.Vertices;
        var face = Vector3.Cross(v[1].Position - v[0].Position, v[2].Position - v[0].Position);
        Assert.True(Vector3.Dot(face, v[0].Normal) > 0, "counter-clockwise front face kept");
    }

    [Fact]
    public void A_mirroring_scale_turns_the_winding_over_so_the_front_stays_the_front()
    {
        var b = Triangle();
        b.Root["nodes"]![1]!["scale"] = new JsonArray(-2, 2, 2);
        var p = Read(b.Gltf()).Primitives[0];
        var v = p.Vertices;
        var face = Vector3.Cross(v[(int)p.Indices[1]].Position - v[(int)p.Indices[0]].Position, v[(int)p.Indices[2]].Position - v[(int)p.Indices[0]].Position);
        Assert.True(Vector3.Dot(face, v[0].Normal) > 0);
    }

    [Fact]
    public void A_node_matrix_is_read_column_by_column()
    {
        var b = Triangle();
        var child = b.Root["nodes"]![1]!.AsObject();
        child.Remove("rotation"); child.Remove("scale");
        // Column-major: a translation of (0, 3, 0) m sits in elements 12..14.
        child["matrix"] = new JsonArray(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 3, 0, 1);
        var p = Read(b.Gltf()).Primitives[0];
        Near(new Vector3(720, 0, 216), p.Vertices[0].Position);
    }

    [Fact]
    public void Units_per_metre_scales_the_whole_scene()
    {
        var p = Read(Triangle().Gltf(), new GltfReadOptions(UnitsPerMetre: 1f)).Primitives[0];
        Near(new Vector3(10, 0, 0), p.Vertices[0].Position);
    }

    [Fact]
    public void Strided_normalised_texture_coordinates_and_colours_are_read_as_zero_to_one()
    {
        var b = Triangle((b, prim) =>
        {
            // Interleaved: two unsigned bytes of u, v then two of padding, 4 bytes a vertex.
            int view = b.View([0, 0, 9, 9, 255, 0, 9, 9, 0, 255, 9, 9], stride: 4);
            prim["attributes"]!["TEXCOORD_0"] = b.Accessor(view, 5121, 3, "VEC2", normalized: true);
            int col = b.View([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 128]);
            prim["attributes"]!["COLOR_0"] = b.Accessor(col, 5121, 3, "VEC4", normalized: true);
        });
        var v = Read(b.Gltf()).Primitives[0].Vertices;
        Near(new Vector3(1, 0, 0), new Vector3(v[1].Uv0, 0));
        Near(new Vector3(0, 1, 0), new Vector3(v[2].Uv0, 0));
        Assert.Equal(0xff0000ffu, v[0].Color);
        Assert.Equal(0x80ff0000u, v[2].Color);
    }

    [Fact]
    public void The_level_exporters_vertex_colours_are_left_out_unless_asked_for()
    {
        var b = Triangle((b, prim) =>
        {
            b.Root["asset"]!["generator"] = "Vitomir level exporter";
            prim["attributes"]!["COLOR_0"] = b.Accessor(b.View([10, 20, 30, 255, 10, 20, 30, 255, 10, 20, 30, 255]), 5121, 3, "VEC4", normalized: true);
        });
        var bytes = b.Gltf();
        Assert.Equal(0xffffffffu, Read(bytes).Primitives[0].Vertices[0].Color);
        Assert.Equal(0xff1e140au, Read(bytes, new GltfReadOptions(VertexColours: true)).Primitives[0].Vertices[0].Color);
    }

    [Fact]
    public void A_sparse_accessor_overrides_the_values_it_names()
    {
        var b = Triangle((b, prim) =>
        {
            int pos = (int)prim["attributes"]!["POSITION"]!;
            var acc = b.Accessors[pos]!.AsObject();
            int idx = b.View(BitConverter.GetBytes((ushort)2).Concat(new byte[2]).ToArray());
            int val = b.View(new float[] { 0, 5, 0 }.SelectMany(BitConverter.GetBytes).ToArray());
            acc["sparse"] = new JsonObject
            {
                ["count"] = 1,
                ["indices"] = new JsonObject { ["bufferView"] = idx, ["componentType"] = 5123 },
                ["values"] = new JsonObject { ["bufferView"] = val },
            };
        });
        var v = Read(b.Gltf()).Primitives[0].Vertices;
        // (0, 5, 0) m in the child: scaled 2 → (0,10,0), parent x 10 → engine 72 · (10, 0, 10).
        Near(new Vector3(720, 0, 720), v[2].Position);
    }

    [Fact]
    public void Without_normals_each_triangle_gets_its_own_flat_normal()
    {
        var b = Triangle((b, prim) => prim["attributes"]!.AsObject().Remove("NORMAL"));
        var p = Read(b.Gltf()).Primitives[0];
        Assert.Equal(3, p.Vertices.Length);
        foreach (var v in p.Vertices) Near(Vector3.UnitX, v.Normal);
    }

    [Fact]
    public void A_triangle_strip_and_a_fan_become_lists()
    {
        foreach (var (mode, expected) in new[] { (5, new uint[] { 0, 1, 2, 2, 1, 3 }), (6, new uint[] { 0, 1, 2, 0, 2, 3 }) })
        {
            var b = Triangle((b, prim) =>
            {
                prim["attributes"]!["POSITION"] = b.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0);
                prim["attributes"]!["NORMAL"] = b.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1);
                prim["indices"] = b.Shorts(0, 1, 2, 3);
                prim["mode"] = mode;
            });
            Assert.Equal(expected, Read(b.Gltf()).Primitives[0].Indices);
        }
    }

    static byte[] Png(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-gltf-test-{Guid.NewGuid():N}.png");
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, bl, a) = pixel(x, y);
                rgba[(y * w + x) * 4] = r; rgba[(y * w + x) * 4 + 1] = g; rgba[(y * w + x) * 4 + 2] = bl; rgba[(y * w + x) * 4 + 3] = a;
            }
        Vitomir.Engine.Levels.Export.PngEncoder.Write(path, w, h, rgba);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    static Builder Textured()
    {
        return Triangle((b, prim) =>
        {
            prim["attributes"]!["TEXCOORD_0"] = b.Floats("VEC2", 0, 0, 1, 0, 0, 1);
            var png = Png(4, 2, (x, y) => ((byte)(x * 60), (byte)(y * 200), 7, 255));
            b.Root["images"] = new JsonArray(new JsonObject { ["bufferView"] = b.View(png), ["mimeType"] = "image/png" },
                                             new JsonObject { ["uri"] = "data:image/jpeg;base64," + Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0]) });
            b.Root["samplers"] = new JsonArray(new JsonObject { ["wrapS"] = 33071, ["wrapT"] = 33648 });
            b.Root["textures"] = new JsonArray(new JsonObject { ["source"] = 0, ["sampler"] = 0 }, new JsonObject { ["source"] = 1 });
            b.Root["materials"] = new JsonArray(new JsonObject
            {
                ["name"] = "brick",
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorFactor"] = new JsonArray(0.5, 0.25, 1, 0.75),
                    ["baseColorTexture"] = new JsonObject { ["index"] = 0 },
                    ["metallicFactor"] = 0.2, ["roughnessFactor"] = 0.7,
                    ["metallicRoughnessTexture"] = new JsonObject { ["index"] = 0, ["texCoord"] = 1 },
                },
                ["normalTexture"] = new JsonObject { ["index"] = 0, ["scale"] = 0.5 },
                ["occlusionTexture"] = new JsonObject { ["index"] = 0, ["strength"] = 0.3 },
                ["emissiveFactor"] = new JsonArray(1, 0.5, 0),
                ["emissiveTexture"] = new JsonObject { ["index"] = 1 },
                ["extensions"] = new JsonObject { ["KHR_materials_emissive_strength"] = new JsonObject { ["emissiveStrength"] = 4 } },
                ["alphaMode"] = "MASK", ["alphaCutoff"] = 0.4, ["doubleSided"] = true,
            });
            prim["material"] = 0;
        });
    }

    [Fact]
    public void A_material_keeps_every_field_and_its_textures_images_are_decoded()
    {
        var scene = Read(Textured().Gltf());
        var m = Assert.Single(scene.Materials);
        Assert.Equal("brick", m.Name);
        Assert.Equal(new Vector4(0.5f, 0.25f, 1, 0.75f), m.BaseColorFactor);
        Assert.Equal(new GltfTextureSlot(0, 0), m.BaseColorTexture);
        Assert.Equal(new GltfTextureSlot(0, 1), m.MetallicRoughnessTexture);
        Assert.Equal((0.2f, 0.7f), (m.MetallicFactor, m.RoughnessFactor));
        Assert.Equal(0.5f, m.NormalScale);
        Assert.Equal(0.3f, m.OcclusionStrength);
        Assert.Equal(new Vector3(1, 0.5f, 0), m.EmissiveFactor);
        Assert.Equal(4f, m.EmissiveStrength);
        Assert.Equal(GltfAlphaMode.Mask, m.AlphaMode);
        Assert.Equal(0.4f, m.AlphaCutoff);
        Assert.True(m.DoubleSided);
        // The JPEG is not decoded: its slot reads as none (white), with a warning.
        Assert.False(m.EmissiveTexture.Present);
        Assert.Contains(scene.Warnings, w => w.Contains("JPEG"));

        var img = scene.Images[0];
        Assert.Equal((4, 2), (img.Width, img.Height));
        Assert.Equal(new byte[] { 180, 200, 7, 255 }, img.Rgba![28..32]);
        Assert.Equal(new GltfTexture(0, RepeatU: false, RepeatV: true, MirrorU: false, MirrorV: true), scene.Textures[0]);
    }

    [Fact]
    public void A_glb_read_with_its_images_deferred_has_the_same_triangles_and_decodes_the_same_pixels_when_asked()
    {
        // Ours (the cook's way): the binary chunk read a view at a time, the PNG left undecoded until wanted.
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-deferred-{Guid.NewGuid():N}.glb");
        File.WriteAllBytes(path, Textured().Glb());
        try
        {
            var eager = GltfReader.Load(path);
            var deferred = GltfReader.Load(path, new GltfReadOptions(DeferImages: true));
            Assert.Equal(eager.Primitives.Count, deferred.Primitives.Count);
            for (int i = 0; i < eager.Primitives.Count; i++)
            {
                Assert.Equal(eager.Primitives[i].Indices, deferred.Primitives[i].Indices);
                Assert.Equal(eager.Primitives[i].Vertices.Select(v => v.Position), deferred.Primitives[i].Vertices.Select(v => v.Position));
            }
            Assert.Equal(eager.Materials[0].BaseColorTexture, deferred.Materials[0].BaseColorTexture);
            var img = deferred.Images[0];
            Assert.Null(img.Rgba);
            Assert.True(img.Present);
            Assert.Equal((4, 2), (img.Width, img.Height));                 // from the PNG's header
            Assert.Equal(eager.Images[0].Rgba, img.Decode());
            Assert.False(deferred.Materials[0].EmissiveTexture.Present);   // the JPEG, as before
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_normal_map_without_tangents_gets_tangents_along_u_and_its_bitangent_up_the_image()
    {
        var p = Read(Textured().Gltf()).Primitives[0];
        // u runs along glTF +x in the child, which turns to glTF −z, which is engine +y.
        foreach (var v in p.Vertices)
        {
            Near(Vector3.UnitY, new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z));
            // v runs along the child's +y, which becomes engine +z; the map's up is decreasing v, so the
            // bitangent cross(n, t) · w must point along engine −z.
            var bitangent = Vector3.Cross(v.Normal, new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z)) * v.Tangent.W;
            Near(-Vector3.UnitZ, bitangent);
        }
    }

    [Fact]
    public void A_glb_reads_the_same_as_the_gltf()
    {
        var a = Read(Textured().Gltf());
        var b = Read(Textured().Glb());
        Assert.Equal(a.Primitives[0].Vertices, b.Primitives[0].Vertices);
        Assert.Equal(a.Images[0].Rgba, b.Images[0].Rgba);
        Assert.Equal(a.Materials[0].AlphaCutoff, b.Materials[0].AlphaCutoff);
    }

    [Fact]
    public void A_primitive_without_a_material_gets_the_default_one()
    {
        var scene = Read(Triangle().Gltf());
        var m = scene.Materials[scene.Primitives[0].Material];
        Assert.Equal(Vector4.One, m.BaseColorFactor);
        Assert.Equal((1f, 1f), (m.MetallicFactor, m.RoughnessFactor));
        Assert.False(m.DoubleSided);
    }
}
