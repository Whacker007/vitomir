// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/CellLooks.cs at b4185ef, reworked for the platform.
using Vitomir.Platform.Render.Level;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render;

/// <summary>
/// **Ours.** What a cell level is seen by, beyond its manifest's sun: a look or a time of day asked for
/// on the command line (<c>--level-l2 … --look morning|chronicle|golden|dusk|level</c> or
/// <c>--time-of-day h</c>, <c>--day n</c>), the graphics quality (<c>--quality</c>), the clouds' options, and the level's own torches for the
/// modern path when its scene brings none. With no look asked for a cell level is seen in
/// <see cref="DefaultLook"/>, the default light for new levels; <c>--look level</c> keeps the
/// level's own sun and the renderer's defaults instead.
/// </summary>
internal sealed class CellLooks
{
    /// <summary>The look cell levels are seen in when none is asked for: <c>morning</c>.</summary>
    public const string DefaultLook = "morning";

    /// <summary>The name that keeps the level's own sun (its manifest's) and no look.</summary>
    public const string LevelsOwn = "level";

    /// <summary>The look asked for (<see cref="ModernLooks.Names"/> or <see cref="LevelsOwn"/>), or null for <see cref="DefaultLook"/>.</summary>
    public string? Look { get; set; }

    /// <summary>The hour to put the sun at (over Prague, on the level's day; <see cref="ModernSettings.TimeOfDay"/>), or null.</summary>
    public float? TimeOfDay { get; set; }
    /// <summary>The day of the year to put the sun on whatever the level's date says (<c>--day n</c>, for testing; ours), or null.</summary>
    public int? Day { get; set; }

    /// <summary>The clouds' cover 0..1 (<c>--clouds</c>), the wind as a bearing it blows from and metres a second (<c>--wind</c>), the sky's march steps (<c>--cloud-steps</c>); null keeps the look's.</summary>
    public float? CloudCover { get; set; }
    public (float From, float Speed)? Wind { get; set; }
    public int? CloudSteps { get; set; }
    /// <summary>Where in their drift the clouds start, seconds (<c>--cloud-time</c>; <see cref="ModernCloudSettings.ClockOffset"/>), or null.</summary>
    public float? CloudTime { get; set; }

    /// <summary>The graphics quality asked for (<c>--quality low|medium|high|ultra|auto</c>; <see cref="ModernQualities"/>), or null.</summary>
    public string? Quality { get; set; }
    /// <summary>Whether a quality asked for is kept in the player's <c>vitomir.ini</c> (a windowed run; not a still or a replay).</summary>
    /// <summary>The preset in force, once <see cref="ResolveQuality"/> has run (or a test sets it); null keeps the renderer's defaults (medium's).</summary>
    public ModernQualityPreset? Preset { get; set; }
    /// <summary>The sky's clouds at half resolution (<c>--cloud-pass half|full</c>; <see cref="ModernCloudSettings.HalfResolution"/>), or null.</summary>
    public bool? CloudsHalf { get; set; }
    /// <summary>Temporal anti-aliasing on or off whatever the preset says (<c>--taa</c>, <c>--no-taa</c>), or null.</summary>
    public bool? Taa { get; set; }
    /// <summary>The level's light probes on or off whatever the preset says (<c>--probes on|off</c>; ours), or null.</summary>
    public bool? Probes { get; set; }
    /// <summary>A scale on the probes' bounced light (<c>--probe-bounce x</c>; ours), or null.</summary>
    public float? ProbeBounce { get; set; }
    /// <summary>The exposure whatever the look says (<c>--exposure x</c>; ours), or null.</summary>
    public float? Exposure { get; set; }
    /// <summary>
    /// The reach of the screen's ambient occlusion in metres whatever the settings say, 0 turning it off
    /// (<c>--ao-radius m</c>; ours: for telling what the occlusion takes from the probes' light), or null.
    /// </summary>
    public float? AoRadius { get; set; }
    /// <summary>Hex tiling on or off whatever the quality says (<c>--tiling hex|plain</c>; ours), or null.</summary>
    public bool? Tiling { get; set; }

    /// <summary>Works out the quality preset for this run (the option, the ini, the GPU), once.</summary>
    /// <summary>Where the renderer's lines go (the program's log; the console by default).</summary>
    public Action<string> Say { get; set; } = Console.WriteLine;

    public ModernQualityPreset ResolveQuality(Gpu gpu) =>
        Preset ??= ModernQualities.ForRun(Quality, gpu, Say);

    /// <summary>The streamer's settings: the quality's, then the environment's (<see cref="ModernStreamSettings.FromEnvironment(ModernQualityPreset?)"/>).</summary>
    public ModernStreamSettings StreamSettings() => ModernStreamSettings.FromEnvironment(Preset);

    /// <summary>The look to apply: the one asked for, else the default.</summary>
    public string LookName => Look ?? DefaultLook;

    /// <summary>The day asked for, the look, then the quality preset, then the time of day and the clouds' options, over settings already read from the level (its date among them).</summary>
    public void Apply(ModernSettings settings)
    {
        var name = LookName;
        if (Day is { } day) settings.Day = Math.Clamp(day, 1, 365);   // before the look, which follows the date
        if (!string.Equals(name, LevelsOwn, StringComparison.OrdinalIgnoreCase) && !ModernLooks.Apply(settings, name))
            Console.Error.WriteLine($"look: no look named {name}; the looks are {string.Join(", ", ModernLooks.Names)}, {LevelsOwn}");
        Preset?.Apply(settings);   // the quality's cost settings (ours); the options below override it
        if (CloudsHalf is { } half) settings.Clouds.HalfResolution = half;
        if (Taa is { } taa) settings.Taa = taa;
        if (TimeOfDay is { } hour) { settings.NorthYaw = settings.LevelNorthYaw; settings.TimeOfDay(hour, settings.Day); }   // the real sky's sun: by the level's compass
        if (CloudCover is { } cover) settings.Clouds.Cover = Math.Clamp(cover, 0f, 1f);
        if (Wind is { } wind) (settings.Clouds.WindFrom, settings.Clouds.WindSpeed) = wind;
        if (CloudSteps is { } steps) settings.Clouds.Steps = Math.Clamp(steps, 0, 64);
        if (CloudTime is { } time) settings.Clouds.ClockOffset = time;
        if (Probes is { } probes) settings.Probes = probes;
        if (ProbeBounce is { } bounce) settings.ProbeBounce = MathF.Max(bounce, 0f);
        if (Exposure is { } exposure) settings.Exposure = MathF.Max(exposure, 0f);
        if (Tiling is { } hex) settings.HexTiling = hex;
        if (AoRadius is { } reach) { if (reach > 0f) settings.AoRadiusMetres = reach; else settings.AmbientOcclusion = false; }
    }

    /// <summary>
    /// The level's <c>LITE</c> lights (the cook's torches, metres) as the modern path's point lights
    /// (engine units; the intensity is already the modern path's scale), when the scene's glTF brought
    /// no lights of its own — the concept street's export has none, the Old Town's has its 51.
    /// </summary>
    public static void AddLights(ModernScene scene, RenderLevel level)
    {
        if (scene.Lights.Count > 0) return;
        float scale = level.Manifest.Units.EngineUnitsPerMetre;
        foreach (var l in level.Lights)
            scene.Lights.Add(new ModernPointLight(l.Position * scale, l.Colour * l.Intensity, l.Range * scale));
    }
}
