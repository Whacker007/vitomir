// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>Textures and material definitions from the player's install.</summary>
public class AssetTests
{
    [Theory]
    [InlineData("LMaterials.nob")]
    [InlineData("Resource.nob")]
    public void Every_tga_decodes(string archive)
    {
        using var nob = GameData.Open(archive);
        long pixels = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".tga", TgaImage.Read, (_, t) => pixels += (long)t.Width * t.Height);
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        Assert.True(parsed > 1000);
    }

    /// <summary>
    /// Every TGA in an extra data folder (a texture pack such as Neural Redemption, which ships
    /// top-down files the stock game never uses) must decode. Set VTMR_EXTRA_DATA to the folder;
    /// skipped otherwise.
    /// </summary>
    [Fact]
    public void Every_tga_in_an_extra_data_folder_decodes()
    {
        var dir = Environment.GetEnvironmentVariable("VTMR_EXTRA_DATA");
        Assert.SkipWhen(string.IsNullOrEmpty(dir) || !Directory.Exists(dir), "set VTMR_EXTRA_DATA to a folder of loose game files (e.g. a texture pack)");
        var failures = new List<string>();
        int parsed = 0, topDown = 0;
        foreach (var f in Directory.EnumerateFiles(dir!, "*.tga", SearchOption.AllDirectories))
        {
            var data = File.ReadAllBytes(f);
            try { TgaImage.Read(data, f); parsed++; if (data.Length > 17 && (data[17] & 0x20) != 0) topDown++; }
            catch (NodFormatException e) { failures.Add(e.Message); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        Assert.True(parsed > 0, "no .tga files found");
        TestContext.Current.SendDiagnosticMessage($"{parsed} textures decoded ({topDown} stored top-down)");
    }

    [Fact]
    public void Every_level_material_has_only_known_nam_keywords()
    {
        using var nob = GameData.Open("LMaterials.nob");
        var unknown = new List<string>();
        var (parsed, failures) = GameData.ParseAll(nob, ".nam", NamMaterial.Read, (n, m) => unknown.AddRange(m.Unknown.Select(u => $"{n}: {u}")));
        Assert.Empty(failures);
        Assert.True(unknown.Count == 0, string.Join("\n", unknown.Take(20)));
        Assert.True(parsed > 1900);
    }

    [Fact]
    public void Nam_parses_passes_slides_and_flags()
    {
        var m = NamMaterial.Parse("pass 0 waterTest.tga modplusdest\npass 3 nomip $lightmap modulatepost\nsound wet\n" +
                                  "slide 2 (-0.3, -0.05)\nMasked\n; a comment\nnumframes 3\n");
        Assert.Equal(2, m.Passes.Count);
        Assert.Equal(new NamPass(3, true, "$lightmap", "modulatepost"), m.Passes[1]);
        Assert.Equal("wet", m.Sound);
        Assert.Equal((-0.3f, -0.05f), m.Slides[2]);
        Assert.True(m.Masked);
        Assert.Equal(3, m.NumFrames);
        Assert.Empty(m.Unknown);
    }
}
