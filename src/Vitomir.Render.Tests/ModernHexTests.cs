// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// the tiling flag on the CPU (ours): the glTF reader
/// takes <c>extras.tiling</c> (the levels' shared contract), a stain decal over a hex-tiled surface takes the
/// surface's cells on the set it borrows the surface's maps through, the cook's definition carries both into the L2
/// material and back, a material from an older package (no <c>tiling</c>) still loads as a plain repeat, and the
/// quality presets turn hex tiling off at <c>low</c> only. No GPU.
/// </summary>
public class ModernHexTests
{
    /// <summary>
    /// A quad's glTF with three materials over four textures (colour, normal, packed occlusion-roughness-metal, height):
    /// hex-tiled cobbles, a stain decal on them borrowing their normal and packed maps through set 1, and plaster with
    /// no tiling.
    /// </summary>
    static byte[] Gltf()
    {
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        int View(byte[] d)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = d.Length });
            bin.Write(d);
            return views.Count - 1;
        }
        int Acc(float[] f, string type, int n)
        {
            accessors.Add(new JsonObject { ["bufferView"] = View(f.SelectMany(BitConverter.GetBytes).ToArray()), ["componentType"] = 5126, ["count"] = n, ["type"] = type });
            return accessors.Count - 1;
        }
        int pos = Acc([0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0], "VEC3", 4);
        int uv = Acc([0, 0, 1, 0, 1, 1, 0, 1], "VEC2", 4);
        accessors.Add(new JsonObject { ["bufferView"] = View(new ushort[] { 0, 1, 2, 0, 2, 3 }.SelectMany(BitConverter.GetBytes).ToArray()), ["componentType"] = 5123, ["count"] = 6, ["type"] = "SCALAR" });
        int idx = accessors.Count - 1;
        var images = new JsonArray();
        var textures = new JsonArray();
        for (int i = 0; i < 4; i++)
        {
            var rgba = Enumerable.Range(0, 8 * 8 * 4).Select(k => (byte)(k * (i + 3))).ToArray();
            var path = Path.Combine(Path.GetTempPath(), $"vitomir-hex-test-{Guid.NewGuid():N}.png");
            Vitomir.Engine.Levels.Export.PngEncoder.Write(path, 8, 8, rgba);
            images.Add(new JsonObject { ["bufferView"] = View(File.ReadAllBytes(path)), ["mimeType"] = "image/png" });
            File.Delete(path);
            textures.Add(new JsonObject { ["source"] = i });
        }
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["images"] = images, ["textures"] = textures,
            ["materials"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = "cobble",
                    ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 }, ["metallicRoughnessTexture"] = new JsonObject { ["index"] = 2 } },
                    ["normalTexture"] = new JsonObject { ["index"] = 1 }, ["occlusionTexture"] = new JsonObject { ["index"] = 2 },
                    ["extras"] = new JsonObject
                    {
                        ["height"] = new JsonObject { ["index"] = 3, ["scale"] = 0.035 },
                        ["tiling"] = new JsonObject { ["mode"] = "hex", ["rotate"] = true, ["scale"] = 0.4 },
                    },
                },
                new JsonObject
                {
                    ["name"] = "decal_on_cobble", ["alphaMode"] = "BLEND",
                    ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0, ["texCoord"] = 0 }, ["metallicRoughnessTexture"] = new JsonObject { ["index"] = 2, ["texCoord"] = 1 } },
                    ["normalTexture"] = new JsonObject { ["index"] = 1, ["texCoord"] = 1 }, ["occlusionTexture"] = new JsonObject { ["index"] = 2, ["texCoord"] = 1 },
                    ["extras"] = new JsonObject { ["decal"] = new JsonObject { ["on"] = "cobble", ["kind"] = "stain" } },
                },
                new JsonObject
                {
                    ["name"] = "plaster",
                    ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } },
                }),
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["primitives"] = new JsonArray(
                    new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["TEXCOORD_0"] = uv, ["TEXCOORD_1"] = uv }, ["indices"] = idx, ["material"] = 0 },
                    new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["TEXCOORD_0"] = uv, ["TEXCOORD_1"] = uv }, ["indices"] = idx, ["material"] = 1 },
                    new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["TEXCOORD_0"] = uv }, ["indices"] = idx, ["material"] = 2 }),
            }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0 }),
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["scene"] = 0,
            ["accessors"] = accessors, ["bufferViews"] = views,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bin.ToArray()) }),
        };
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    [Fact]
    public void The_reader_takes_the_tiling_flag_and_a_decal_takes_its_surfaces_cells()
    {
        var scene = GltfReader.Read(Gltf(), Path.GetTempPath());
        var cobble = scene.Materials[0];
        Assert.Equal(new GltfTiling("hex", true, 0.4f, 0), cobble.Tiling);
        Assert.True(cobble.Tiling!.Hex);
        // The decal borrows the cobbles' normal and packed maps through set 1: it takes their cells there, and the
        // cobbles' height map (through set 1 too) to weight the blend, so a stain shows the stones under it.
        var decal = scene.Materials[1];
        Assert.Equal("cobble", decal.DecalOn);
        Assert.Equal(("hex", true, 0.4f, 1), (decal.Tiling!.Mode, decal.Tiling.Rotate, decal.Tiling.Scale, decal.Tiling.TexCoord));
        Assert.Equal(new GltfTextureSlot(cobble.HeightTexture.Texture, 1), decal.Tiling.Weights);
        Assert.Null(scene.Materials[2].Tiling);
    }

    [Fact]
    public void A_missing_scale_or_rotate_takes_the_contracts_defaults_and_another_mode_is_a_plain_repeat()
    {
        static GltfTiling? Read(string json) => GltfTilings.Read(System.Text.Json.JsonDocument.Parse(json).RootElement);
        Assert.Equal(new GltfTiling("hex", false, 0.5f), Read("""{"tiling": {"mode": "hex"}}"""));
        Assert.Equal(new GltfTiling("hex", false, 0.5f), Read("""{"tiling": {}}"""));
        Assert.False(Read("""{"tiling": {"mode": "plain", "scale": 0.5}}""")!.Hex);
        Assert.Null(Read("""{"height": {"index": 0}}"""));
        Assert.Null(Read("""{"tiling": "hex"}"""));
    }

    [Fact]
    public void The_cook_carries_the_tiling_flag_into_the_L2_material_and_back()
    {
        var scene = GltfReader.Read(Gltf(), Path.GetTempPath());
        var cooked = new List<(int Texture, TextureKind Kind)>();
        L2MaterialMap? Map(GltfTextureSlot slot, TextureKind kind)
        {
            if (!slot.Present) return null;
            cooked.Add((slot.Texture, kind));
            return new L2MaterialMap { File = $"textures/t{slot.Texture}_{kind}.ktx2", TexCoord = slot.TexCoord };
        }
        var folder = Path.Combine(Path.GetTempPath(), $"vitomir-hex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "materials"));
        try
        {
            var files = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var def = CellMaterials.Define(scene.Materials[i], scene.Materials[i].Name, Map, ground: i == 0, passable: false);
                File.WriteAllText(Path.Combine(folder, $"materials/m{i}.json"), def.Write());
                files.Add($"materials/m{i}.json");
            }
            var cobble = L2Material.Read(File.ReadAllText(Path.Combine(folder, files[0])));
            Assert.Equal(("hex", true, 0.4f, 0), (cobble.Tiling!.Mode, cobble.Tiling.Rotate, cobble.Tiling.Scale, cobble.Tiling.TexCoord));
            Assert.Null(cobble.Tiling.Weights);
            // The decal's weights are the cobbles' height map, cooked as a height map (BC4): the same file as theirs.
            var decal = L2Material.Read(File.ReadAllText(Path.Combine(folder, files[1])));
            Assert.Equal(1, decal.Tiling!.TexCoord);
            Assert.Equal((cobble.HeightTexture!.File, 1), (decal.Tiling.Weights!.File, decal.Tiling.Weights.TexCoord));
            Assert.Contains(decal.Maps(), m => m.Map == decal.Tiling.Weights && !m.Srgb);
            Assert.Equal(2, cooked.Count(c => c == (scene.Materials[0].HeightTexture.Texture, TextureKind.Height)));
            Assert.Null(L2Material.Read(File.ReadAllText(Path.Combine(folder, files[2]))).Tiling);
            Assert.DoesNotContain("tiling", File.ReadAllText(Path.Combine(folder, files[2])));

            // And back into the renderer's table, as a streamed level loads it.
            var (loaded, _) = CellMaterials.Load(folder, files);
            Assert.Equal(new GltfTiling("hex", true, 0.4f, 0), loaded.Materials[0].Tiling);
            var back = loaded.Materials[1].Tiling!;
            Assert.Equal(("hex", true, 0.4f, 1), (back.Mode, back.Rotate, back.Scale, back.TexCoord));
            Assert.Equal(loaded.Materials[0].HeightTexture.Texture, back.Weights.Texture);
            Assert.Equal(1, back.Weights.TexCoord);
            Assert.Null(loaded.Materials[2].Tiling);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_material_from_an_older_package_loads_as_a_plain_repeat_and_unknown_tiling_words_are_kept()
    {
        // An older package's material: no "tiling".
        var old = L2Material.Read("""{"name": "cobble", "heightTexture": {"file": "textures/h.ktx2"}, "heightScale": 0.035}""");
        Assert.Null(old.Tiling);
        // A later tool's words inside the flag are kept on a rewrite, as the format's extras are.
        var later = L2Material.Read("""{"name": "c", "tiling": {"mode": "hex", "rotate": false, "scale": 0.5, "contrast": 0.7}}""");
        Assert.True(later.Tiling!.Hex);
        Assert.Contains("\"contrast\"", later.Write());
    }

    [Fact]
    public void Hex_tiling_is_off_at_low_and_on_from_medium_up_and_by_default()
    {
        var all = Enum.GetValues<ModernQuality>().Select(ModernQualityPreset.For).ToArray();
        Assert.Equal([false, true, true, true], all.Select(p => p.HexTiling).ToArray());
        var low = new ModernSettings();
        all[0].Apply(low);
        Assert.False(low.HexTiling);
        Assert.True(new ModernSettings().HexTiling);   // the renderer's defaults are medium's
    }
}
