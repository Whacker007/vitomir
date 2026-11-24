// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/CellWorldPass.cs at b4185ef, reworked for the platform.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using SDL;
using Vitomir.Formats.L2;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Level;

/// <summary>
/// A cooked level's world, drawn by the modern renderer (<see cref="ModernRenderer"/>, called as the old
/// program calls it): a streamed level's cells and far versions round the eye (<see cref="CellStreamScene"/>),
/// or a version-1 level's glTF whole; its torches; its sun from its manifest's date and compass in the
/// morning look; the exposure its interiors ask for; and the characters handed in as extra geometry, so
/// they are lit, shadowed and occluded with the world. The old program's <c>CellWorldPass</c> without the
/// part that drew the world's depth again for 1.1's own thing pass, which the platform does not have.
/// </summary>
internal sealed unsafe class LevelPass : IDisposable
{
    readonly ModernRenderer renderer;
    readonly ModernScene scene;
    readonly CellStreamScene? stream;
    readonly ModernSettings levelSettings;

    public RenderLevel Level { get; }
    public ModernSettings Settings => renderer.Settings;
    public CellStreamScene? Stream => stream;

    public LevelPass(Gpu gpu, RenderLevel level, CellLooks looks)
    {
        Level = level;
        looks.ResolveQuality(gpu);   // the graphics quality preset: the program's choice, else the GPU's
        levelSettings = SettingsFor(level, looks);
        var settings = SettingsFor(level, looks);
        if (level.Streamed)
        {
            // A streamed level (the old program's Phase 3): its cells' buffers come and go; the glTF gives only the materials.
            stream = new CellStreamScene(gpu, level, settings.Anisotropy, looks.StreamSettings());
            scene = stream.Scene;
        }
        else
        {
            var source = level.MeshFile is { } file && File.Exists(file) ? GltfReader.Load(file) : new GltfScene();
            scene = new ModernScene(gpu, source, settings.Anisotropy);
        }
        CellLooks.AddLights(scene, level);   // the cook's torches when the glTF brings no lights
        renderer = new ModernRenderer(gpu, settings, gpu.ColorFormat);
    }

    /// <summary>
    /// The modern path's settings for a level: its manifest's <c>extras.modern</c> (the sun's bearing
    /// and height, exposure, haze), its <c>date</c> and its compass (<c>north</c>; an older package's
    /// <c>extras.modern.northYaw</c>) over the renderer's defaults, then the look.
    /// </summary>
    public static ModernSettings SettingsFor(RenderLevel level, CellLooks looks)
    {
        var s = new ModernSettings();
        if (level.Manifest.Extras["modern"] is JsonObject m)
        {
            float? F(string key) => m[key] is JsonValue v && v.TryGetValue(out double d) ? (float)d : null;
            s.SunAzimuth = F("sunAzimuth") ?? s.SunAzimuth;
            s.SunElevation = F("sunElevation") ?? s.SunElevation;
            s.NorthYaw = F("northYaw") ?? s.NorthYaw;
            s.LevelNorthYaw = s.NorthYaw;
            s.Exposure = F("exposure") ?? s.Exposure;
            s.HazeVisibilityMetres = F("hazeVisibility") ?? s.HazeVisibilityMetres;
        }
        if (level.Manifest.North is { } north) s.NorthYaw = s.LevelNorthYaw = north.Yaw;   // the level's compass
        var date = L2Date.Of(level.Manifest);   // the level's date: what the morning's sun follows
        (s.Day, s.Hour) = (date.DayOfYear, date.Hour);
        looks.Apply(s);
        if (Environment.GetEnvironmentVariable("VITOMIR_CELL_SUN") is { Length: > 0 } sun)   // az,el for a look without a re-cook (a harness aid)
        {
            var p = sun.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            (s.SunAzimuth, s.SunElevation) = (p[0], p[1]);
        }
        return s;
    }

    float? hourShown;

    /// <summary>
    /// The sun at <paramref name="hour"/> on the level's day by the level's compass (the old program's
    /// <c>--time-of-day</c>), or back to the level's own sun when null. Changed only when the hour changes,
    /// since the light probes are lit again whenever the sky does.
    /// </summary>
    public void TimeOfDay(float? hour)
    {
        if (hour == hourShown) return;
        hourShown = hour;
        var s = renderer.Settings;
        if (hour is { } h)
        {
            s.NorthYaw = s.LevelNorthYaw;
            s.TimeOfDay(Math.Clamp(h, 0f, 24f), s.Day);
        }
        else (s.SunAzimuth, s.SunElevation, s.NorthYaw) = (levelSettings.SunAzimuth, levelSettings.SunElevation, levelSettings.NorthYaw);
    }

    /// <summary>The factor the level's areas last put on the exposure, so it can be taken off again.</summary>
    float areaExposure = 1f;
    float areaStops;
    double? areaClock;
    /// <summary>How fast the eye opens and closes while the clock runs, stops a second (the old program's: the smithy's 2.5 stops in half a second).</summary>
    public const float AdaptStopsPerSecond = 5f;

    /// <summary>
    /// The level's areas' part of the exposure (<see cref="L2Area.ExposureStops"/>): with the eye in an
    /// interior the picture is exposed that many stops brighter, rising through the doorway. While the
    /// clock runs the exposure follows at <see cref="AdaptStopsPerSecond"/>; a first frame or a clock that
    /// stands is exposed for where the eye is.
    /// </summary>
    void AreaExposure(Vector3 eyeMetres, double seconds)
    {
        float stops = L2Area.ExposureStops(Level.Manifest.Areas, eyeMetres);
        if (areaClock is { } last && seconds > last && seconds - last < 0.25)
        {
            float dt = (float)(seconds - last);
            stops = areaStops + Math.Clamp(stops - areaStops, -AdaptStopsPerSecond * dt, AdaptStopsPerSecond * dt);
        }
        (areaStops, areaClock) = (stops, seconds);
        float factor = MathF.Pow(2f, stops);
        if (factor == areaExposure) return;
        renderer.Settings.Exposure *= factor / areaExposure;
        areaExposure = factor;
    }

    /// <summary>
    /// How many frames of one view a still takes: two when the screen's reflections are on, since they
    /// read the frame before, else one; <c>VITOMIR_STILL_FRAMES</c>
    /// asks for more (temporal anti-aliasing settles over several).
    /// </summary>
    public int StillFrames =>
        int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_STILL_FRAMES"), out int n) && n > 0 ? Math.Min(n, 64)
        : renderer.Settings.SsrSteps > 0 ? 2 : 1;

    /// <summary>The world, and <paramref name="extra"/> (the characters) with it, into <paramref name="colour"/> from <paramref name="camera"/>.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* colour, int width, int height, FreeCamera camera, double seconds, ModernExtra? extra)
    {
        if (stream is not null)
        {
            // The cells round the camera: all of them in before the first picture, then as they come.
            var cull = ModernRenderer.ViewProjection(camera, (float)width / height);
            if (!stream.Started) stream.LoadNow(camera.Position, cull);
            stream.Update(cmd, camera.Position, cull, height / (2f * MathF.Tan(camera.VerticalFov / 2f)));
        }
        renderer.Extra = extra is { Draws.Count: > 0 } ? extra : null;
        renderer.Settings.Clouds.Seconds = (float)seconds;
        AreaExposure(camera.Position / ModernMath.UnitsPerMetre, seconds);
        renderer.Render(cmd, colour, width, height, scene, camera);
    }

    /// <summary>The depth the last <see cref="Draw"/> left, the picture's size (the reach highlight is tested against it).</summary>
    public SDL_GPUTexture* Depth => renderer.SceneDepth;

    public void Dispose()
    {
        if (stream is not null) stream.Dispose(); else scene.Dispose();
        renderer.Dispose();
    }
}
