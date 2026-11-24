// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Render.Modern;

/// <summary>
/// What the modern path draws with (ours): the sun, the haze, the exposure,
/// shadows and anti-aliasing. Light values are relative, not photometric: the sun's
/// <see cref="SunIntensity"/> is the irradiance of full sunlight above the air, and everything else is
/// scaled from it, so <see cref="Exposure"/> 1 shows a sunlit mid-grey wall as a mid tone.
/// </summary>
public sealed class ModernSettings
{
    /// <summary>Compass degrees, 0 north, 90 east; where north is in the level is <see cref="NorthYaw"/>.</summary>
    public float SunAzimuth = 140f;
    /// <summary>Degrees above the horizon.</summary>
    public float SunElevation = 42f;
    /// <summary>
    /// The day of the year a look that follows the date puts its sun on (ours): the level's own
    /// (<see cref="Vitomir.Formats.L2.L2Date"/>), or <c>--day</c>; mid-May when neither says.
    /// </summary>
    public int Day = Vitomir.Formats.L2.L2Date.DefaultDay;
    /// <summary>The hour (local solar time) a look that follows the date puts its sun at, when the level gives one; null: the look's own.</summary>
    public float? Hour;
    public float SunIntensity = 6f;
    /// <summary>North as an engine yaw (<see cref="ModernMath.SunDirection"/>): what the sun's bearing, the wind's and the clouds' are counted from; +X by default.</summary>
    public float NorthYaw = ModernMath.OldTownNorthYaw;
    /// <summary>
    /// The level's own compass (ours; <see cref="Vitomir.Formats.L2.L2North"/>): where north is in the
    /// level, as an engine yaw. A look whose sun stands in the real sky (the morning's, <c>--time-of-day</c>)
    /// sets <see cref="NorthYaw"/> to it; the concept's looks, whose suns are given in their scene's own
    /// frame, do not. +X when the level says nothing, as.
    /// </summary>
    public float LevelNorthYaw = Vitomir.Formats.L2.L2North.DefaultYaw;
    /// <summary>A multiplier on the scene's light before the tone curve.</summary>
    public float Exposure = 1.2f;
    /// <summary>
    /// How far one sees through the haze at its base, in metres (the meteorological visibility, where
    /// contrast falls to 2%); 0 turns the haze off. 3 km is a light summer haze over a town with its
    /// hearths lit.
    /// </summary>
    public float HazeVisibilityMetres = 3000f;
    /// <summary>The height over which the haze thins by e (its scale height), in metres.</summary>
    public float HazeHeightMetres = 100f;
    /// <summary>The haze's base height in engine units; null: the scene's lowest point.</summary>
    public float? HazeBase;
    public bool Shadows = true;
    /// <summary>1 to 4 sun shadow cascades.</summary>
    public int Cascades = 4;
    /// <summary>Texels along each cascade's side.</summary>
    public int ShadowResolution = 2048;
    /// <summary>How far from the camera shadows reach, in engine units; 0: the whole scene, up to 250 m.</summary>
    public float ShadowDistance;
    /// <summary>Anisotropic filtering, 1 (off) to 16.</summary>
    public float Anisotropy = 16f;
    public bool Fxaa = true;
    /// <summary>
    /// Temporal anti-aliasing: the projection shifts by a fraction of a pixel each frame and the frames are
    /// blended where their pixels line up; FXAA still runs after it unless turned off. A still (headless)
    /// renders <see cref="TaaStillFrames"/> frames of its one view and keeps the last.
    /// </summary>
    public bool Taa;
    public int TaaStillFrames = 8;

    // ----- the sun and sky of a look (ModernLooks); null or zero keeps the procedural model -----

    /// <summary>The sun's colour, replacing the air's reddening (a look's, or the moon's).</summary>
    public Vector3? SunColour;
    /// <summary>A gradient sky from this horizon radiance to <see cref="SkyZenith"/>, in place of the procedural one.</summary>
    public Vector3? SkyHorizon, SkyZenith;
    /// <summary>An overcast's soft cloud pattern over the sky, 0 (clear) to 1; it also dims the sun's aureole.</summary>
    public float CloudCover;
    /// <summary>A glow low on the horizon towards <see cref="GlowYaw"/> (engine degrees), a dusk's last light.</summary>
    public Vector3 SkyGlow;
    public float GlowYaw;
    /// <summary>How bright the sun's disc is drawn, times its light; 0 hides it (an overcast, or a moon).</summary>
    public float SunDisc = 40f;
    /// <summary>The sun's angular diameter in degrees: a large one (an overcast's diffuse sun) widens the shadows' penumbra.</summary>
    public float SunAngle = 0.53f;
    /// <summary>The ground's albedo, for the light it bounces up.</summary>
    public float GroundAlbedo = 0.3f;
    /// <summary>The surroundings' albedo and tint: the light walls and roofs bounce into what ambient occlusion finds blocked.</summary>
    public Vector3 BounceAlbedo = new(0.34f, 0.3f, 0.25f);
    /// <summary>The haze's ambient colour (null: the sky's horizon) and how strongly it scatters the sun forward (Henyey-Greenstein g).</summary>
    public Vector3? HazeColour;
    public float HazeAnisotropy = 0.6f;
    /// <summary>
    /// The share of what the haze dims that it scatters as sunlight (its albedo): 1 is a white haze, as the
    /// looks use; the default 0.2 keeps a light summer haze from glowing round the sun.
    /// </summary>
    public float HazeSunScale = 0.2f;
    /// <summary>Fair-weather cumulus, their shadows and their drift (<see cref="ModernClouds"/>); none by default.</summary>
    public ModernCloudSettings Clouds = new();

    // ----- ambient occlusion -----

    public bool AmbientOcclusion = true;
    /// <summary>How far round a point ambient occlusion looks for what blocks the sky, in metres.</summary>
    public float AoRadiusMetres = 2.5f;
    /// <summary>A power on the visibility: above 1 darkens the occlusion.</summary>
    public float AoStrength = 1f;
    /// <summary>Directions (slices) and steps each way per pixel of the half-resolution ambient occlusion.</summary>
    public int AoSlices = 2, AoSteps = 6;
    /// <summary>Ambient occlusion at the picture's full resolution instead of half (ours; the ultra quality's).</summary>
    public bool AoFullResolution;
    /// <summary>
    /// The sky's visibility from a height map as well (the far side of a street, which screen-space
    /// occlusion cannot see): the scene seen from straight above over a square this many metres from the
    /// camera each way, <see cref="HeightMapResolution"/> texels along a side.
    /// </summary>
    public bool SkyOcclusion = true;
    public float SkyOcclusionMetres = 80f;
    public int HeightMapResolution = 1024;

    // ----- light probes (ours) -----

    /// <summary>A level's baked light probes light its surfaces and characters where it has them (<see cref="ModernProbes"/>).</summary>
    public bool Probes = true;
    /// <summary>
    /// A scale on the probes' bounced light (sun's and sky's). The bake follows the light two bounces; the
    /// further ones add some tenth more in a town's streets (each bounce about a fifth of the last: albedos of
    /// 0.3–0.4 and half the view walls and ground), so 1.1 by default (ours).
    /// </summary>
    public float ProbeBounce = 1.1f;

    // ----- local lights -----

    public bool Lights = true;
    /// <summary>
    /// The irradiance (relative units) below which a light without a range stops, which sets its reach:
    /// sqrt(intensity / cutoff) metres.
    /// </summary>
    public float LightCutoff = 0.02f;
    /// <summary>
    /// How many point and spot lights cast shadows: the brightest near the camera, each with a cube of six
    /// shadow maps <see cref="LightShadowResolution"/> texels square; 0 turns them off.
    /// </summary>
    public int LightShadows = 4;
    public int LightShadowResolution = 512;

    // ----- atmosphere, bloom, flare -----

    /// <summary>Sun shafts: the sun's in-scatter ray-marched through the haze against the shadow maps.</summary>
    public bool Volumetric = true;
    public int VolumetricSteps = 24;
    public bool Bloom = true;
    /// <summary>The share of the picture scattered into the bloom (a lens's veiling glare), 0..1.</summary>
    public float BloomIntensity = 0.02f;
    /// <summary>Light below this (relative units, after exposure) does not bloom; 0 blooms everything, as a lens does.</summary>
    public float BloomThreshold = 0f;
    /// <summary>The soft knee's width, as a fraction of the threshold.</summary>
    public float BloomKnee = 0.5f;
    public bool LensFlare = true;
    public float FlareIntensity = 1f;

    // ----- surfaces, foliage, reflections and the air (ours) -----

    /// <summary>Parallax occlusion mapping's most steps through a height map (0: off; the quality preset's).</summary>
    public int ParallaxSteps = 12;
    /// <summary>Whether a height map's relief shadows itself from the sun (one more march towards it).</summary>
    public bool ParallaxShadow;
    /// <summary>The distance in metres over which parallax fades out (it is gone at this distance).</summary>
    public float ParallaxMetres = 20f;
    /// <summary>A large-scale variation of colour and roughness over every surface, against visible tiling.</summary>
    public bool MacroVariation = true;
    /// <summary>
    /// Whether a material that asks for it (<c>extras.tiling</c> <c>hex</c>; ours) is drawn hex-tiled — its
    /// maps shuffled in hexagonal cells so a large surface does not repeat — or as a plain repeat (the quality
    /// preset's: off at <c>low</c>).
    /// </summary>
    public bool HexTiling = true;
    /// <summary>A scale on foliage's sway (the materials' own wind times this; the clouds' wind speed sets it in a look).</summary>
    public float FoliageWind = 1f;
    /// <summary>Screen-space reflections' march (0: off; the quality preset's), and whether at full resolution instead of half.</summary>
    public int SsrSteps = 16;
    public bool SsrFullResolution;
    /// <summary>Surfaces rougher than this reflect only the sky and their surroundings, not the screen.</summary>
    public float SsrMaxRoughness = 0.5f;
    /// <summary>
    /// Ours: specular anti-aliasing — each surface's roughness widened by how much its normal turns
    /// within the pixel, so fine relief, waves and leaves far off show their average glint instead of sparkling
    /// (ModernPbr.hlsl). On in every preset; off only to compare.
    /// </summary>
    public bool SpecularAntiAliasing = true;
    /// <summary>
    /// The physically based sky and aerial perspective (<see cref="ModernAtmosphere"/>) instead of the
    /// procedural sky; the morning look's. A look's gradient sky (<see cref="SkyHorizon"/>, <see cref="SkyZenith"/>) wins.
    /// </summary>
    public bool Atmosphere;
    /// <summary>How many times clear air's aerosols the atmosphere holds (its haze; <see cref="ModernAtmosphere.MieScale"/>).</summary>
    public float AtmosphereAerosols = 1f;

    // ----- tone and grade -----

    public ModernToneCurve ToneCurve = ModernToneCurve.Aces;
    /// <summary>Contrast round mid-grey in the tone curve's log space (AgX's looks: 1 base, about 1.2 "medium high").</summary>
    public float Contrast = 1f;
    public float Saturation = 1f;
    /// <summary>A lift of the blacks, display values 0..1.</summary>
    public float Lift;
    /// <summary>How much the corners darken, 0..1.</summary>
    public float Vignette;

    /// <summary>Shows one of the half-resolution buffers instead of the picture, for tuning.</summary>
    public ModernDebugView Debug;

    public Vector3 ToSun => ModernMath.SunDirection(SunAzimuth, SunElevation, NorthYaw);

    /// <summary>Puts the sun where it stands at <paramref name="hour"/> (local solar time) over Prague on <paramref name="dayOfYear"/>.</summary>
    public void TimeOfDay(float hour, int dayOfYear = 120)
    {
        (SunAzimuth, SunElevation) = ModernMath.SunAt(hour, dayOfYear);
    }
}

/// <summary>
/// The light of one moment, worked out from <see cref="ModernSettings"/>: the sun's colour after the
/// air, and the sky's and ground's radiance for ambient light and the sky's picture (ours). A simple
/// model: the sky's brightness follows the sun's elevation, reddening towards the horizon at dawn and
/// dusk; the ground's is sunlit earth and stone of albedo 0.3.
/// </summary>
public readonly record struct ModernLight(Vector3 ToSun, Vector3 SunColor, float SunIntensity,
                                          Vector3 Zenith, Vector3 Horizon, Vector3 Ground, bool Gradient = false, bool Physical = false)
{
    public static ModernLight From(ModernSettings s)
    {
        var toSun = s.ToSun;
        bool physical = s.Atmosphere && !(s.SkyHorizon is not null && s.SkyZenith is not null);
        if (physical) return Physically(s, toSun);
        var t = s.SunColour ?? ModernMath.SunTransmittance(s.SunElevation);
        // Day: 0 with the sun 6° below the horizon (civil twilight's end), 1 from 10° above.
        float x = Math.Clamp((s.SunElevation + 6f) / 16f, 0f, 1f);
        float day = x * x * (3 - 2 * x);
        float i = s.SunIntensity;
        Vector3 zenith, horizon;
        bool gradient = s.SkyHorizon is not null && s.SkyZenith is not null;
        if (gradient) { horizon = s.SkyHorizon!.Value; zenith = s.SkyZenith!.Value; }
        else
        {
            // A clear sky's diffuse light is about a sixth of the sun's on the ground at mid elevations; this
            // sky gives about that once its whole dome is integrated (ModernAmbient).
            zenith = i * 0.13f * new Vector3(0.09f, 0.27f, 1.0f) * (0.03f + 0.97f * day);
            var warm = t / MathF.Max(MathF.Max(t.X, t.Y), MathF.Max(t.Z, 1e-3f));
            horizon = i * 0.15f * new Vector3(0.50f, 0.68f, 1.0f) * Vector3.Lerp(warm, Vector3.One, day) * (0.03f + 0.97f * day);
        }
        float sunUp = MathF.Max(toSun.Z, 0f);
        var skyIrradiance = MathF.PI * (zenith + horizon) * 0.5f;
        var ground = s.GroundAlbedo * (i * t * sunUp + skyIrradiance) / MathF.PI * new Vector3(1.0f, 0.92f, 0.80f);
        return new ModernLight(toSun, t, i, zenith, horizon, ground, gradient);
    }

    /// <summary>
    /// The light from the physically based atmosphere (ours; <see cref="ModernAtmosphere"/>): the sun's
    /// colour is the air's transmittance towards it from the ground, the sky's zenith and horizon (the clouds'
    /// and the haze's ambient light) its sky-view table's, all times the sun's illuminance above the air.
    /// </summary>
    static ModernLight Physically(ModernSettings s, Vector3 toSun)
    {
        var atm = ModernAtmosphere.For(s.AtmosphereAerosols, s.GroundAlbedo);
        float i = s.SunIntensity;
        var t = s.SunColour ?? atm.Transmittance(ModernAtmosphere.GroundKm + 0.002f, toSun.Z) * Math.Clamp((s.SunElevation + 2f) / 2f, 0f, 1f);
        var lut = atm.GroundSkyView(toSun);
        var zenith = i * ModernAtmosphere.ReadSkyView(lut, Vector3.UnitZ, toSun, 0f);
        var horizon = Vector3.Zero;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.PI / 8f, e = 3f * MathF.PI / 180f;
            horizon += ModernAtmosphere.ReadSkyView(lut, new Vector3(MathF.Cos(e) * MathF.Cos(a), MathF.Cos(e) * MathF.Sin(a), MathF.Sin(e)), toSun, 0f);
        }
        horizon *= i / 16f;
        float sunUp = MathF.Max(toSun.Z, 0f);
        var skyIrradiance = MathF.PI * (zenith + horizon) * 0.5f;
        var ground = s.GroundAlbedo * (i * t * sunUp + skyIrradiance) / MathF.PI * new Vector3(1.0f, 0.92f, 0.80f);
        return new ModernLight(toSun, t, i, zenith, horizon, ground, false, true);
    }
}

/// <summary>What <see cref="ModernSettings.Debug"/> shows: the picture, or ambient occlusion's visibility, or the shafts' lit in-scatter, as grey.</summary>
public enum ModernDebugView { None, AmbientOcclusion, Shafts }

/// <summary>The curve that maps the HDR picture to the screen: ACES's filmic fit, or AgX (Blender's default since 4.0).</summary>
public enum ModernToneCurve { Aces, AgX }
