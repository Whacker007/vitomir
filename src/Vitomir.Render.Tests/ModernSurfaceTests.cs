// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// the surfaces on the CPU (ours): the glTF
/// reader takes a height map and foliage from a material's extras (the levels' shared contract), the cook's
/// definition carries them with the alpha mode, cut-off and sidedness into the L2 material and back, a height
/// map cooks to BC4 with every mip, and the quality presets turn parallax and the screen's reflections off at
/// <c>low</c>. No GPU.
/// </summary>
public class ModernSurfaceTests
{
    /// <summary>A quad's glTF with two materials: stone with a height map in its extras, and leaves (cut out, two-sided, foliage, COLOR_0).</summary>
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
        int col = Acc([1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1], "VEC4", 4);
        accessors.Add(new JsonObject { ["bufferView"] = View(new ushort[] { 0, 1, 2, 0, 2, 3 }.SelectMany(BitConverter.GetBytes).ToArray()), ["componentType"] = 5123, ["count"] = 6, ["type"] = "SCALAR" });
        int idx = accessors.Count - 1;
        var png = Png(8, 8, (x, y) => ((byte)(x * 30), (byte)(x * 30), (byte)(x * 30), (byte)(y < 4 ? 255 : 0)));
        int img = View(png);
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["images"] = new JsonArray(new JsonObject { ["bufferView"] = img, ["mimeType"] = "image/png" }),
            ["textures"] = new JsonArray(new JsonObject { ["source"] = 0 }),
            ["materials"] = new JsonArray(
                new JsonObject { ["name"] = "stone", ["extras"] = new JsonObject { ["height"] = new JsonObject { ["index"] = 0, ["scale"] = 0.04 } } },
                new JsonObject
                {
                    ["name"] = "leaves", ["alphaMode"] = "MASK", ["alphaCutoff"] = 0.45, ["doubleSided"] = true,
                    ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } },
                    ["extras"] = new JsonObject { ["foliage"] = new JsonObject { ["translucency"] = 0.6, ["wind"] = 0.8 } },
                }),
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["primitives"] = new JsonArray(
                    new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["TEXCOORD_0"] = uv }, ["indices"] = idx, ["material"] = 0 },
                    new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = pos, ["TEXCOORD_0"] = uv, ["COLOR_0"] = col }, ["indices"] = idx, ["material"] = 1 }),
            }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0 }),
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["scene"] = 0,
            ["accessors"] = accessors, ["bufferViews"] = views,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bin.ToArray()) }),
        };
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    static byte[] Png(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-surface-test-{Guid.NewGuid():N}.png");
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                int o = (y * w + x) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = a;
            }
        Vitomir.Engine.Levels.Export.PngEncoder.Write(path, w, h, rgba);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    [Fact]
    public void The_reader_takes_a_height_map_and_foliage_from_a_materials_extras()
    {
        var scene = GltfReader.Read(Gltf(), Path.GetTempPath());
        var stone = scene.Materials[0];
        Assert.True(stone.HeightTexture.Present);
        Assert.Equal(0.04f, stone.HeightScale, 4);
        Assert.False(stone.Foliage);
        var leaves = scene.Materials[1];
        Assert.False(leaves.HeightTexture.Present);
        Assert.Equal((true, 0.6f, 0.8f), (leaves.Foliage, leaves.Translucency, leaves.Wind));
        Assert.Equal((GltfAlphaMode.Mask, 0.45f, true), (leaves.AlphaMode, leaves.AlphaCutoff, leaves.DoubleSided));
        // COLOR_0's alpha comes through on the vertices: the sway weight, 0 at the root, 1 at the tips (the
        // file has no normals, so each triangle has its own three vertices: 0, 1, 2 and 0, 2, 3).
        var plant = scene.Primitives.Single(p => p.Material == 1);
        Assert.Equal(new uint[] { 0, 0, 255, 0, 255, 255 }, plant.Vertices.Select(v => v.Color >> 24).ToArray());
    }

    [Fact]
    public void The_cook_carries_height_and_foliage_into_the_L2_material_and_back()
    {
        var scene = GltfReader.Read(Gltf(), Path.GetTempPath());
        var kinds = new List<TextureKind>();
        L2MaterialMap? Map(GltfTextureSlot slot, TextureKind kind)
        {
            if (!slot.Present) return null;
            kinds.Add(kind);
            return new L2MaterialMap { File = $"textures/t{slot.Texture}_{kind}.ktx2", TexCoord = slot.TexCoord };
        }
        var folder = Path.Combine(Path.GetTempPath(), $"vitomir-surface-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "materials"));
        try
        {
            var files = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                var def = CellMaterials.Define(scene.Materials[i], scene.Materials[i].Name, Map, ground: i == 0, passable: i == 1);
                var text = def.Write();
                File.WriteAllText(Path.Combine(folder, $"materials/m{i}.json"), text);
                files.Add($"materials/m{i}.json");
            }
            Assert.Contains(TextureKind.Height, kinds);
            var stone = L2Material.Read(File.ReadAllText(Path.Combine(folder, files[0])));
            Assert.NotNull(stone.HeightTexture);
            Assert.Equal(0.04f, stone.HeightScale, 4);
            Assert.Null(stone.Foliage);
            Assert.Contains(stone.Maps(), m => m.Map == stone.HeightTexture && !m.Srgb);
            var leaves = L2Material.Read(File.ReadAllText(Path.Combine(folder, files[1])));
            Assert.Equal(("MASK", 0.45f, true), (leaves.AlphaMode, leaves.AlphaCutoff, leaves.DoubleSided));
            Assert.Equal((0.6f, 0.8f), (leaves.Foliage!.Translucency, leaves.Foliage.Wind));
            // And back into the renderer's table, as a streamed level loads it.
            var (loaded, _) = CellMaterials.Load(folder, files);
            Assert.True(loaded.Materials[0].HeightTexture.Present);
            Assert.Equal(0.04f, loaded.Materials[0].HeightScale, 4);
            Assert.Equal((true, 0.6f, 0.8f, GltfAlphaMode.Mask, true),
                         (loaded.Materials[1].Foliage, loaded.Materials[1].Translucency, loaded.Materials[1].Wind, loaded.Materials[1].AlphaMode, loaded.Materials[1].DoubleSided));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_height_map_cooks_to_BC4_with_every_mip_and_reads_back()
    {
        const int n = 64;
        var rgba = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                byte h = (byte)(127 + 120 * MathF.Sin(x * 0.3f) * MathF.Cos(y * 0.2f));
                rgba[(y * n + x) * 4] = rgba[(y * n + x) * 4 + 1] = rgba[(y * n + x) * 4 + 2] = h; rgba[(y * n + x) * 4 + 3] = 255;
            }
        var ktx = TextureCook.Cook(rgba, n, n, TextureKind.Height);
        Assert.Equal(Ktx2Format.Bc4Unorm, ktx.Format);
        Assert.Equal(Ktx2.FullChain(n, n), ktx.Levels.Count);
        Assert.Equal(n * n / 2, ktx.Levels[0].Length);   // 8 bytes a 4 × 4 block
        var back = Ktx2.Read(ktx.Write(), "height.ktx2");
        Assert.Equal((Ktx2Format.Bc4Unorm, n, n, ktx.Levels.Count), (back.Format, back.Width, back.Height, back.Levels.Count));
        Assert.True(back.Compressed);
    }

    [Fact]
    public void Parallax_and_the_screens_reflections_are_off_at_low_and_rise_with_the_preset()
    {
        var all = Enum.GetValues<ModernQuality>().Select(ModernQualityPreset.For).ToArray();
        Assert.Equal((0, false, 0), (all[0].ParallaxSteps, all[0].ParallaxShadow, all[0].SsrSteps));
        for (int i = 1; i < all.Length; i++)
        {
            Assert.True(all[i].ParallaxSteps > all[i - 1].ParallaxSteps, $"{all[i].Quality}");
            Assert.True(all[i].SsrSteps > all[i - 1].SsrSteps, $"{all[i].Quality}");
        }
        Assert.True(all[^1].ParallaxShadow && all[^1].SsrFullResolution);
        var low = new ModernSettings();
        all[0].Apply(low);
        Assert.Equal((0, 0), (low.ParallaxSteps, low.SsrSteps));
        var medium = new ModernSettings();
        all[1].Apply(medium);
        var plain = new ModernSettings();
        Assert.Equal((plain.ParallaxSteps, plain.ParallaxShadow, plain.ParallaxMetres, plain.SsrSteps, plain.SsrFullResolution),
                     (medium.ParallaxSteps, medium.ParallaxShadow, medium.ParallaxMetres, medium.SsrSteps, medium.SsrFullResolution));
    }
}
