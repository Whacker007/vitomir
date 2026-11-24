// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Engine.Levels.Export;
using Vitomir.Engine.Scene;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A level exported as glTF 2.0** (ours). A small level and the
/// Old Town are exported into the temporary folder from the install (skipping without one) and the
/// result is read back as plain JSON: the buffer is the size the document says and every accessor
/// fits inside its view, the triangles are exactly the fans of the surfaces the game draws, every
/// material and texture index points at something, and there is a lightmap image per page of the
/// NIL. The exported files are deleted afterwards.
///
/// It exports whole levels, textures and lightmaps included, so the class runs on its own: beside
/// the rest of the suite it starved timed tests
/// (the convent's sound read-ahead, which waits at most 60 s, timed out on the laptop's Debug run).
/// </summary>
[Collection(nameof(GltfExportTests))]
[CollectionDefinition(nameof(GltfExportTests), DisableParallelization = true)]
public class GltfExportTests
{
    static readonly Dictionary<int, int> ComponentSize = new() { [5120] = 1, [5121] = 1, [5122] = 2, [5123] = 2, [5125] = 4, [5126] = 4 };
    static readonly Dictionary<string, int> TypeCount = new() { ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4 };

    static void ExportAndCheck(string levelName, bool perPage)
    {
        using var files = Install.Open();
        Assert.SkipUnless(files.TryRead($"levels/{levelName}.nil", out var data), $"no levels/{levelName}.nil in the install");
        var level = NilLevel.Read(data, levelName);
        string dir = Path.Combine(Path.GetTempPath(), $"vitomir-gltf-{levelName}-{Guid.NewGuid():N}");
        try
        {
            var r = GltfLevelExporter.Export(level, new MaterialCache(files), levelName, dir, new GltfExportOptions(MaterialPerLightmapPage: perPage));
            using var doc = JsonDocument.Parse(File.ReadAllText(r.GltfPath));
            var root = doc.RootElement;
            Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());

            long binLength = new FileInfo(r.BinPath).Length;
            Assert.Equal(binLength, root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt64());
            var views = root.GetProperty("bufferViews").EnumerateArray().ToArray();
            foreach (var v in views)
                Assert.True(v.GetProperty("byteOffset").GetInt64() + v.GetProperty("byteLength").GetInt64() <= binLength);
            var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
            foreach (var a in accessors)
            {
                var view = views[a.GetProperty("bufferView").GetInt32()];
                long need = (long)a.GetProperty("count").GetInt32() * ComponentSize[a.GetProperty("componentType").GetInt32()]
                            * TypeCount[a.GetProperty("type").GetString()!];
                Assert.Equal(need, view.GetProperty("byteLength").GetInt64());
                Assert.Equal(0, view.GetProperty("byteOffset").GetInt64() % 4);
            }

            int materials = root.GetProperty("materials").GetArrayLength();
            int textures = root.GetProperty("textures").GetArrayLength();
            long triangles = 0;
            foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
                foreach (var p in mesh.GetProperty("primitives").EnumerateArray())
                {
                    triangles += accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3;
                    int m = p.GetProperty("material").GetInt32();
                    Assert.InRange(m, 0, materials - 1);
                    foreach (var attr in p.GetProperty("attributes").EnumerateObject()) Assert.InRange(attr.Value.GetInt32(), 0, accessors.Length - 1);
                }
            Assert.Equal(GltfLevelExporter.DrawnTriangles(level), triangles);
            Assert.Equal(r.Triangles, triangles);
            foreach (var m in root.GetProperty("materials").EnumerateArray())
                if (m.GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out var t))
                    Assert.InRange(t.GetProperty("index").GetInt32(), 0, textures - 1);

            var images = root.GetProperty("images").EnumerateArray().Select(i => i.GetProperty("uri").GetString()!).ToArray();
            Assert.Equal(level.Lightmaps.Length, images.Count(u => u.StartsWith("lightmaps/")));
            Assert.All(images, u => Assert.True(File.Exists(Path.Combine(dir, u)), u));

            using var walk = JsonDocument.Parse(File.ReadAllText(r.WalkPath));
            Assert.Equal(level.PathNodes.Length, walk.RootElement.GetProperty("nodes").GetArrayLength());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_small_level_exports_to_a_gltf_whose_buffers_triangles_materials_and_lightmaps_all_add_up() =>
        ExportAndCheck("N1_SLUM", perPage: false);

    [Fact]
    public void The_Old_Town_exports_with_a_material_per_lightmap_page_and_everything_still_adds_up() =>
        ExportAndCheck("P1_OLDT", perPage: true);

    [Fact]
    public void The_daytime_Old_Town_exports_with_a_material_per_lightmap_page_and_everything_still_adds_up() =>
        ExportAndCheck("P1_OTDY", perPage: true);

    [Fact]
    public void A_NIL_point_becomes_a_Y_up_right_handed_point_in_metres()
    {
        // NIL +z (up) is glTF +y; NIL +y is glTF -z; the conversion is a rotation, so winding holds.
        var p = GltfLevelExporter.ToGltf(new System.Numerics.Vector3(72, 144, 216), 1f / 72f);
        Assert.Equal(new System.Numerics.Vector3(1, 3, -2), p);
    }
}
