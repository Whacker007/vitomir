// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernQuality.cs at b4185ef, reworked for the platform.
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>The modern path's graphics quality presets (ours).</summary>
internal enum ModernQuality { Low, Medium, High, Ultra }

/// <summary>How ambient occlusion is drawn: not at all, at half the picture's resolution, or at its own.</summary>
internal enum ModernAoQuality { Off, Half, Full }

/// <summary>
/// **Ours.** One graphics quality preset: every setting that costs GPU time or memory, set together so
/// that they make sense with each other — a laptop's integrated GPU (<see cref="ModernQuality.Low"/>) to a
/// big desktop card drawing the whole map (<see cref="ModernQuality.Ultra"/>). The choice makes the
/// modern path the only one for new levels, and the design's "view distance scales with the machine" (no
/// fog wall) is what the view distance here carries out. <see cref="ModernQuality.Medium"/> is the
/// renderer's own defaults, as measured and looked at so far. A preset is applied after a look (which
/// sets the light, not the cost) and before any single option, which overrides it.
/// </summary>
internal sealed record ModernQualityPreset(
    ModernQuality Quality,
    int CloudSteps,                // the sky's march through the cumulus; 0: the flat layer
    int ShadowResolution,          // texels along each sun cascade's side
    int Cascades,                  // sun shadow cascades, 1..4
    float ShadowReachMetres,       // how far from the camera the sun's shadows reach
    float SkylineShadowMetres,     // how near a skyline draw must come to cast the sun's shadow (0: never)
    int LightShadows,              // point and spot lights with shadow cubes
    int LightShadowResolution,     // texels along each cube face's side
    ModernAoQuality AmbientOcclusion,
    int ShaftSteps,                // the sun shafts' march; 0: no shafts
    bool Fxaa,
    bool Taa,
    float Anisotropy,              // 1..16
    int TextureBudgetMB,
    int GeometryBudgetMB,
    float ViewDistanceMetres,      // how far the level's far versions are drawn; 0: the whole map
    float NearMetres,              // blocks within this have their cells loaded
    float Lod1Metres,              // a cell's second level of detail from this far
    float SkylineDetail)           // the skyline's level of detail, a scale on its switch distances (1: as built)
{
    /// <summary>Parallax occlusion's most steps (0: off), whether the relief shadows itself, and how far off it is gone (ours).</summary>
    public int ParallaxSteps { get; init; }
    public bool ParallaxShadow { get; init; }
    public float ParallaxMetres { get; init; }
    /// <summary>Screen-space reflections' steps (0: off) and whether traced at full resolution (ours).</summary>
    public int SsrSteps { get; init; }
    public bool SsrFullResolution { get; init; }
    /// <summary>Whether a level's baked light probes light it (ours): in every preset, low included, since they cost four texture reads a pixel and stand in for the ambient occlusion low has not.</summary>
    public bool Probes { get; init; } = true;
    /// <summary>Whether materials that ask for hex tiling get it (ours); a plain repeat at <c>low</c>, where it would cost a third more texture reads.</summary>
    public bool HexTiling { get; init; } = true;

    /// <summary>The preset with its name.</summary>
    public static ModernQualityPreset For(ModernQuality quality) => Costs(quality) with
    {
        // Ours: the surfaces' relief and the screen's reflections, off on a laptop's integrated GPU.
        ParallaxSteps = quality switch { ModernQuality.Low => 0, ModernQuality.High => 20, ModernQuality.Ultra => 32, _ => 12 },
        ParallaxShadow = quality >= ModernQuality.High,
        ParallaxMetres = quality switch { ModernQuality.Low => 0f, ModernQuality.High => 30f, ModernQuality.Ultra => 45f, _ => 20f },
        SsrSteps = quality switch { ModernQuality.Low => 0, ModernQuality.High => 24, ModernQuality.Ultra => 32, _ => 16 },
        SsrFullResolution = quality == ModernQuality.Ultra,
        HexTiling = quality >= ModernQuality.Medium,   // Ours
    };

    static ModernQualityPreset Costs(ModernQuality quality) => quality switch
    {
        ModernQuality.Low => new(quality, CloudSteps: 0, ShadowResolution: 1024, Cascades: 2, ShadowReachMetres: 80f, SkylineShadowMetres: 0f,
                                 LightShadows: 0, LightShadowResolution: 256, ModernAoQuality.Off, ShaftSteps: 0, Fxaa: true, Taa: false,
                                 Anisotropy: 4f, TextureBudgetMB: 128, GeometryBudgetMB: 256, ViewDistanceMetres: 400f, NearMetres: 90f,
                                 Lod1Metres: 60f, SkylineDetail: 0.5f),
        ModernQuality.High => new(quality, CloudSteps: 16, ShadowResolution: 2048, Cascades: 4, ShadowReachMetres: 400f, SkylineShadowMetres: 300f,
                                  LightShadows: 8, LightShadowResolution: 512, ModernAoQuality.Half, ShaftSteps: 32, Fxaa: true, Taa: true,
                                  Anisotropy: 16f, TextureBudgetMB: 1024, GeometryBudgetMB: 1024, ViewDistanceMetres: 2000f, NearMetres: 180f,
                                  Lod1Metres: 135f, SkylineDetail: 1.5f),
        ModernQuality.Ultra => new(quality, CloudSteps: 24, ShadowResolution: 4096, Cascades: 4, ShadowReachMetres: 800f, SkylineShadowMetres: 600f,
                                   LightShadows: 16, LightShadowResolution: 1024, ModernAoQuality.Full, ShaftSteps: 48, Fxaa: true, Taa: true,
                                   Anisotropy: 16f, TextureBudgetMB: 4096, GeometryBudgetMB: 2048, ViewDistanceMetres: 0f, NearMetres: 270f,
                                   Lod1Metres: 200f, SkylineDetail: 2.5f),
        // Medium: the renderer's and the streamer's own defaults (ModernSettings, ModernStreamSettings).
        _ => new(ModernQuality.Medium, CloudSteps: 12, ShadowResolution: 2048, Cascades: 4, ShadowReachMetres: 250f, SkylineShadowMetres: 150f,
                 LightShadows: 4, LightShadowResolution: 512, ModernAoQuality.Half, ShaftSteps: 24, Fxaa: true, Taa: false,
                 Anisotropy: 16f, TextureBudgetMB: 256, GeometryBudgetMB: 512, ViewDistanceMetres: 800f, NearMetres: 120f,
                 Lod1Metres: 90f, SkylineDetail: 1f),
    };

    /// <summary>The renderer's share: shadows, lights' cubes, ambient occlusion, shafts, anti-aliasing, filtering, clouds.</summary>
    public void Apply(ModernSettings s)
    {
        s.Clouds.Steps = CloudSteps;
        s.ShadowResolution = ShadowResolution;
        s.Cascades = Cascades;
        s.ShadowDistance = ShadowReachMetres * ModernMath.UnitsPerMetre;
        s.LightShadows = LightShadows;
        s.LightShadowResolution = LightShadowResolution;
        s.AmbientOcclusion = AmbientOcclusion != ModernAoQuality.Off;
        s.SkyOcclusion = AmbientOcclusion != ModernAoQuality.Off;
        s.AoFullResolution = AmbientOcclusion == ModernAoQuality.Full;
        s.Volumetric = ShaftSteps > 0;
        s.VolumetricSteps = Math.Max(ShaftSteps, 4);
        s.Fxaa = Fxaa;
        s.Taa = Taa;
        s.Anisotropy = Anisotropy;
        s.ParallaxSteps = ParallaxSteps;
        s.ParallaxShadow = ParallaxShadow;
        if (ParallaxMetres > 0f) s.ParallaxMetres = ParallaxMetres;
        s.SsrSteps = SsrSteps;
        s.SsrFullResolution = SsrFullResolution;
        s.Probes = Probes;
        s.HexTiling = HexTiling;
    }

    /// <summary>The streamer's share: the budgets, the view distance, the cells' distances and the skyline's.</summary>
    public void Apply(ModernStreamSettings s)
    {
        s.TextureBudgetMB = TextureBudgetMB;
        s.GeometryBudgetMB = GeometryBudgetMB;
        s.WholeMap = ViewDistanceMetres <= 0f;
        if (ViewDistanceMetres > 0f) s.ViewDistanceMetres = ViewDistanceMetres;
        s.NearMetres = NearMetres;
        s.Lod1Metres = Lod1Metres;
        s.SkylineShadowMetres = SkylineShadowMetres;
        s.SkylineDetail = SkylineDetail;
    }
}

/// <summary>
/// **Ours.** Which quality preset a run uses, and where it is kept: <c>--quality low|medium|high|ultra</c>
/// on the command line, else the <c>[modern] quality</c> key of the player's own <c>vitomir.ini</c> (the
/// levels build keeps its own, <see cref="BuildFlavour"/>), else one picked for the GPU — low on an
/// integrated one, medium on any other (<see cref="ForAdapter"/>).
/// </summary>
internal static class ModernQualities
{
    /// <summary>The ini's section and key.</summary>
    public const string Section = "modern", Key = "quality";

    /// <summary>The preset when nothing says otherwise and the GPU is not known to be integrated.</summary>
    public const ModernQuality Default = ModernQuality.Medium;

    /// <summary>The names, as written: <c>low</c>, <c>medium</c>, <c>high</c>, <c>ultra</c>.</summary>
    public static readonly string[] Names = [.. Enum.GetValues<ModernQuality>().Select(Name)];

    /// <summary>A preset's name as written.</summary>
    public static string Name(ModernQuality q) => q.ToString().ToLowerInvariant();

    /// <summary>A preset by its name (any case), or null — including for <c>auto</c>, which means "pick for the GPU".</summary>
    public static ModernQuality? Parse(string? text) =>
        text?.Trim().ToLowerInvariant() switch
        {
            "low" => ModernQuality.Low,
            "medium" => ModernQuality.Medium,
            "high" => ModernQuality.High,
            "ultra" => ModernQuality.Ultra,
            _ => null,
        };

    /// <summary>
    /// The preset for a GPU by its name (SDL's <c>SDL_PROP_GPU_DEVICE_NAME_STRING</c>, the driver's own
    /// name for the adapter): low for an integrated GPU, else <see cref="Default"/>. SDL tells neither an
    /// adapter's memory nor whether it is integrated, so this goes by the name: Intel's graphics other than
    /// Arc, and AMD's APU graphics (named "Radeon Graphics", "Radeon Vega … Graphics" or a Radeon 610M–890M),
    /// are integrated. A software rasterizer (lavapipe, llvmpipe, SwiftShader, WARP) keeps the default, so
    /// that test and review renders do not change with the machine.
    /// </summary>
    public static ModernQuality ForAdapter(string? name) => IsIntegrated(name) ? ModernQuality.Low : Default;

    /// <summary>Whether an adapter's name is an integrated GPU's (see <see cref="ForAdapter"/>).</summary>
    public static bool IsIntegrated(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.ToLowerInvariant();
        if (n.Contains("intel") && !n.Contains("arc")) return true;
        if (n.Contains("amd") || n.Contains("radeon"))
        {
            if (n.Contains(" rx ") || n.Contains("radeon pro") || n.Contains("instinct")) return false;
            if (n.Contains("vega") && n.Contains("graphics") && !n.Contains("vega 56") && !n.Contains("vega 64")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"radeon\s*(\(tm\))?\s*(\d{3}m\s+)?graphics")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"radeon\s*(\(tm\))?\s*[6-8]\d0m\b")) return true;
        }
        return false;
    }

    /// <summary>The GPU's name as its driver gives it, or null when SDL does not say.</summary>
    public static unsafe string? AdapterName(Gpu gpu)
    {
        var props = SDL_GetGPUDeviceProperties(gpu.Device);
        return SDL_GetStringProperty(props, SDL_PROP_GPU_DEVICE_NAME_STRING, null);
    }

    /// <summary>
    /// The preset in force: the one asked for, else the one the program keeps in its settings
    /// (<paramref name="kept"/>), else the GPU's (<paramref name="adapter"/>'s name). <paramref name="why"/>
    /// says which, for the log. (The old program read and wrote <c>vitomir.ini</c> here; the platform's
    /// program keeps its own settings and passes the choice in.)
    /// </summary>
    public static ModernQuality Resolve(string? asked, string? kept, string? adapter, out string why)
    {
        if (Parse(asked) is { } q) { why = "asked for"; return q; }
        bool auto = string.Equals(asked?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
        if (!auto && Parse(kept) is { } k) { why = "kept in the settings"; return k; }
        var picked = ForAdapter(adapter);
        why = picked == Default ? $"the default ({adapter ?? "GPU not named"})" : $"an integrated GPU ({adapter})";
        return picked;
    }

    /// <summary>The preset in force for a renderer, from the program's choice and the GPU; said in a line.</summary>
    public static ModernQualityPreset ForRun(string? asked, Gpu gpu, Action<string> say)
    {
        var q = Resolve(asked, null, AdapterName(gpu), out var why);
        say($"quality: {Name(q)} ({why})");
        return ModernQualityPreset.For(q);
    }

    /// <summary>The value of a <c>--quality</c> option, checked: an error line and false for a name that is not a preset.</summary>
    public static bool Check(string? asked)
    {
        if (asked is null || Parse(asked) is not null || asked.Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;
        Console.Error.WriteLine($"quality: no preset {asked}; the presets are {string.Join(", ", Names)} (or auto)");
        return false;
    }
}
