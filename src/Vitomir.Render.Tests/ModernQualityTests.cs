// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The graphics quality presets on the CPU (ours; <see cref="ModernQualityPreset"/>, <see cref="ModernQualities"/>,
/// <see cref="CellLooks"/>): each setting that costs rises from low to ultra, medium is the renderer's own
/// defaults, a look comes before a preset and a single option after it, the environment's streaming options
/// override a preset's, the choice goes into <c>vitomir.ini</c> and comes back out leaving the file's other
/// lines alone, the order in which a run's preset is chosen, and which GPUs' names are integrated ones.
/// None of it needs a GPU.
/// </summary>
[Collection("cell looks")]
public class ModernQualityTests
{
    static readonly ModernQualityPreset[] All = [.. Enum.GetValues<ModernQuality>().Select(ModernQualityPreset.For)];

    [Fact]
    public void Every_costly_setting_rises_or_stays_from_low_to_ultra()
    {
        static float View(ModernQualityPreset p) => p.ViewDistanceMetres <= 0 ? float.PositiveInfinity : p.ViewDistanceMetres;
        var rising = new (string Name, Func<ModernQualityPreset, float> Of)[]
        {
            ("cloud steps", p => p.CloudSteps), ("shadow map", p => p.ShadowResolution), ("cascades", p => p.Cascades),
            ("shadow reach", p => p.ShadowReachMetres), ("skyline shadows", p => p.SkylineShadowMetres),
            ("shadowed lights", p => p.LightShadows), ("their maps", p => p.LightShadowResolution),
            ("ambient occlusion", p => (int)p.AmbientOcclusion), ("shafts", p => p.ShaftSteps), ("TAA", p => p.Taa ? 1 : 0),
            ("anisotropy", p => p.Anisotropy), ("textures", p => p.TextureBudgetMB), ("geometry", p => p.GeometryBudgetMB),
            ("view distance", View), ("near cells", p => p.NearMetres), ("second detail", p => p.Lod1Metres),
            ("skyline detail", p => p.SkylineDetail),
        };
        foreach (var (name, of) in rising)
            for (int i = 1; i < All.Length; i++)
                Assert.True(of(All[i]) >= of(All[i - 1]), $"{name}: {All[i].Quality} {of(All[i])} below {All[i - 1].Quality} {of(All[i - 1])}");
        // The ends: low draws the clouds flat, with no shafts, occlusion or light shadows; ultra draws the whole map.
        var low = ModernQualityPreset.For(ModernQuality.Low);
        Assert.Equal((0, 0, 0, ModernAoQuality.Off), (low.CloudSteps, low.ShaftSteps, low.LightShadows, low.AmbientOcclusion));
        var ultra = ModernQualityPreset.For(ModernQuality.Ultra);
        Assert.Equal((0f, ModernAoQuality.Full, true), (ultra.ViewDistanceMetres, ultra.AmbientOcclusion, ultra.Taa));
    }

    [Fact]
    public void Medium_is_the_renderers_and_the_streamers_own_defaults()
    {
        var plain = new ModernSettings();
        var medium = new ModernSettings();
        ModernQualityPreset.For(ModernQuality.Medium).Apply(medium);
        Assert.Equal((plain.Clouds.Steps, plain.ShadowResolution, plain.Cascades, plain.LightShadows, plain.LightShadowResolution),
                     (medium.Clouds.Steps, medium.ShadowResolution, medium.Cascades, medium.LightShadows, medium.LightShadowResolution));
        Assert.Equal((plain.AmbientOcclusion, plain.SkyOcclusion, plain.AoFullResolution, plain.Volumetric, plain.VolumetricSteps),
                     (medium.AmbientOcclusion, medium.SkyOcclusion, medium.AoFullResolution, medium.Volumetric, medium.VolumetricSteps));
        Assert.Equal((plain.Fxaa, plain.Taa, plain.Anisotropy), (medium.Fxaa, medium.Taa, medium.Anisotropy));
        // The renderer's 0 means "the scene, up to 250 m": medium's reach is that cap.
        Assert.Equal(250f * ModernMath.UnitsPerMetre, medium.ShadowDistance);
        var stream = new ModernStreamSettings();
        var mediumStream = new ModernStreamSettings();
        ModernQualityPreset.For(ModernQuality.Medium).Apply(mediumStream);
        Assert.Equal((stream.TextureBudgetMB, stream.GeometryBudgetMB, stream.ViewDistanceMetres, stream.WholeMap, stream.NearMetres, stream.Lod1Metres),
                     (mediumStream.TextureBudgetMB, mediumStream.GeometryBudgetMB, mediumStream.ViewDistanceMetres, mediumStream.WholeMap,
                      mediumStream.NearMetres, mediumStream.Lod1Metres));
        Assert.Equal((stream.SkylineShadowMetres, stream.SkylineDetail), (mediumStream.SkylineShadowMetres, mediumStream.SkylineDetail));
    }

    [Fact]
    public void A_cell_levels_look_comes_first_then_the_preset_then_the_single_options()
    {
        try
        {
            CellLooks.Look = null;                                        // the morning look, which sets its own clouds
            CellLooks.Preset = ModernQualityPreset.For(ModernQuality.Ultra);
            var s = new ModernSettings();
            CellLooks.Apply(s);
            Assert.Equal(0.35f, s.Clouds.Cover);                          // the look's
            Assert.Equal(24, s.Clouds.Steps);                             // the preset's, over the look's new clouds
            Assert.True(s.Taa && s.AoFullResolution && s.ShadowResolution == 4096);
            CellLooks.CloudSteps = 6;                                     // --cloud-steps 6 overrides the preset
            CellLooks.CloudsHalf = false;                                 // --cloud-pass full
            CellLooks.Taa = false;                                        // --no-taa
            var o = new ModernSettings();
            CellLooks.Apply(o);
            Assert.Equal((6, false, false), (o.Clouds.Steps, o.Clouds.HalfResolution, o.Taa));
            Assert.Equal(4096, o.ShadowResolution);
        }
        finally { CellLooks.Preset = null; CellLooks.CloudSteps = null; CellLooks.CloudsHalf = null; CellLooks.Taa = null; CellLooks.Look = null; }
    }

    [Fact]
    public void The_environments_streaming_options_override_the_presets()
    {
        var ultra = ModernQualityPreset.For(ModernQuality.Ultra);
        Assert.True(ModernStreamSettings.FromEnvironment(ultra) is { WholeMap: true, TextureBudgetMB: 4096 });
        try
        {
            Environment.SetEnvironmentVariable("VITOMIR_VIEW_DISTANCE", "900");
            Environment.SetEnvironmentVariable("VITOMIR_TEXTURE_MB", "64");
            var s = ModernStreamSettings.FromEnvironment(ultra);
            Assert.Equal((false, 900f, 64, 2048), (s.WholeMap, s.ViewDistanceMetres, s.TextureBudgetMB, s.GeometryBudgetMB));
            Environment.SetEnvironmentVariable("VITOMIR_VIEW_DISTANCE", "whole");
            Assert.True(ModernStreamSettings.FromEnvironment(ModernQualityPreset.For(ModernQuality.Low)).WholeMap);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VITOMIR_VIEW_DISTANCE", null);
            Environment.SetEnvironmentVariable("VITOMIR_TEXTURE_MB", null);
        }
    }

    [Fact]
    public void The_quality_goes_into_vitomir_ini_and_comes_back_leaving_the_other_lines_alone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-quality-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "; the player's own\n[options]\ngamma=1.2\n\n[window]\nfullscreen=off\n");
            Assert.Null(ModernQualities.Read(IniFile.Read(path)));
            ModernQualities.Write(path, ModernQuality.Ultra);
            Assert.Equal(ModernQuality.Ultra, ModernQualities.Read(IniFile.Read(path)));
            ModernQualities.Write(path, ModernQuality.Low);
            var text = File.ReadAllText(path);
            Assert.Equal(ModernQuality.Low, ModernQualities.Read(IniFile.Parse(text)));
            Assert.Contains("; the player's own", text);
            Assert.Contains("gamma=1.2", text);
            Assert.Contains("fullscreen=off", text);
            Assert.Single(text.Split('\n'), l => l.StartsWith("quality", StringComparison.OrdinalIgnoreCase));
            Assert.Null(ModernQualities.Read(IniFile.Parse("[modern]\nquality=auto\n")));
            Assert.Equal(ModernQuality.High, ModernQualities.Read(IniFile.Parse("[Modern]\nQuality = HIGH\n")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_runs_quality_is_the_one_asked_for_else_the_inis_else_the_gpus()
    {
        var ini = IniFile.Parse("[modern]\nquality=high\n");
        var none = IniFile.Parse("");
        const string laptop = "Intel(R) Iris(R) Xe Graphics", desktop = "NVIDIA GeForce RTX 4070";
        Assert.Equal(ModernQuality.Ultra, ModernQualities.Resolve("ultra", ini, laptop, out _));
        Assert.Equal(ModernQuality.High, ModernQualities.Resolve(null, ini, laptop, out _));
        Assert.Equal(ModernQuality.Low, ModernQualities.Resolve(null, none, laptop, out var why));
        Assert.Contains("integrated", why);
        Assert.Equal(ModernQuality.Medium, ModernQualities.Resolve(null, none, desktop, out _));
        Assert.Equal(ModernQuality.Medium, ModernQualities.Resolve(null, none, null, out _));
        Assert.Equal(ModernQuality.Low, ModernQualities.Resolve("auto", ini, laptop, out _));      // auto: the GPU's, whatever the ini says
        Assert.False(ModernQualities.Check("extreme"));
        Assert.True(ModernQualities.Check("Ultra") && ModernQualities.Check("auto") && ModernQualities.Check(null));
    }

    [Theory]
    [InlineData("Intel(R) UHD Graphics 620", true)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("AMD Radeon 780M Graphics", true)]
    [InlineData("AMD Radeon(TM) Vega 8 Graphics", true)]
    [InlineData("AMD Radeon RX 6800 XT", false)]
    [InlineData("Radeon RX Vega 64", false)]
    [InlineData("NVIDIA GeForce RTX 4070", false)]
    [InlineData("NVIDIA GeForce GTX 1650", false)]
    [InlineData("llvmpipe (LLVM 19.1.7, 256 bits)", false)]
    [InlineData("Microsoft Basic Render Driver", false)]
    [InlineData(null, false)]
    public void An_integrated_gpu_is_known_by_its_name(string? name, bool integrated)
    {
        Assert.Equal(integrated, ModernQualities.IsIntegrated(name));
        Assert.Equal(integrated ? ModernQuality.Low : ModernQuality.Medium, ModernQualities.ForAdapter(name));
    }
}
