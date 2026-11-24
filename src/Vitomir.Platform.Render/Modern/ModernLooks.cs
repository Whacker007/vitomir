// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernLooks.cs at b4185ef, reworked for the platform.
using System.Numerics;
using System.Text.Json;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The modern path's looks (ours): named sets of sun, sky, haze,
/// exposure and grade, matched to the three looks of the Prague 1141 concept stills
/// (the levels generator's concept render). Their values are Blender's own — the sun's irradiance in its watts
/// per square metre, the sky's radiance, the haze's density, exposure in stops, AgX and its contrast —
/// so the engine and the path tracer start from the same light:
/// <list type="bullet">
/// <item><b>chronicle</b> (look A): a soft overcast, a pale grey sky with a cloud pattern and a large,
/// dim sun whose shadows are wide and soft;</item>
/// <item><b>golden</b> (look B): a low, warm sun down the street through a haze that scatters it forward
/// into shafts;</item>
/// <item><b>dusk</b> (look C): the blue hour into night, the moon as a weak cool sun, a dark gradient sky
/// with a last glow, a thick cool fog; torches and lit windows come from the scene's lights and
/// emissive materials (<see cref="ModernDressing"/>).</item>
/// <item><b>morning</b>: the default light for new levels (about a 10 AM late morning,
/// the sun in the sky, light or partly cloudy) — the sun where it stands over Prague at 10:00 local
/// solar time on the level's day (<see cref="ModernSettings.Day"/>; <see cref="SeasonDay"/>, mid-May,
/// without one: about 50° up in the south-east; late August's about 43°); a clear blue sky, paler at
/// the horizon; fair-weather cumulus over about a third of it, whose shadows drift over the town
/// (<see cref="ModernClouds"/>); a light haze (30 km) that tints the distance blue-grey. Not one of the
/// concept stills': it is the default for cell levels (<see cref="CellLooks"/>, ours).</item>
/// </list>
/// Blender measures a sun's azimuth from its +X towards +Y, which is the engine's yaw (the engine is 72 ×
/// Blender's coordinates), so a look sets north at yaw 0 and the sun's compass bearing as minus that angle.
/// </summary>
internal static class ModernLooks
{
    public static readonly string[] Names = ["chronicle", "golden", "dusk", "morning"];

    /// <summary>
    /// The day of the year the morning look's sun stands on when nothing says otherwise: 135, mid-May (ours;
    /// <see cref="Vitomir.Formats.L2.L2Date.DefaultDay"/>). Now the date is the level's own
    /// (<see cref="ModernSettings.Day"/>, from its manifest's <c>date</c>; Prague at the start
    /// in late summer, Vienna in winter, Prague again in spring); this is the day of a level without one.
    /// </summary>
    public const int SeasonDay = Vitomir.Formats.L2.L2Date.DefaultDay;

    /// <summary>The morning look's hour, local solar time.</summary>
    public const float MorningHour = 10f;

    /// <summary>Applies look <paramref name="name"/> to <paramref name="s"/>; false when there is no such look.</summary>
    public static bool Apply(ModernSettings s, string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "chronicle":
                Sun(s, elevation: 50f, blenderAzimuth: -40f, 2.6f, new Vector3(1.0f, 0.96f, 0.9f), angle: 12f, disc: 0f);
                s.SkyHorizon = new Vector3(0.86f, 0.86f, 0.85f) * 1.1f;
                s.SkyZenith = new Vector3(0.64f, 0.67f, 0.72f) * 1.1f;
                s.CloudCover = 0.45f;
                s.HazeVisibilityMetres = 8000f; s.HazeHeightMetres = 60f;
                s.HazeColour = new Vector3(0.72f, 0.75f, 0.8f) * 1.15f;
                s.HazeAnisotropy = 0.3f; s.HazeSunScale = 0.3f;
                s.Exposure = MathF.Pow(2f, 0.15f);
                Grade(s, contrast: 1.0f, saturation: 0.95f, lift: 0f, vignette: 0.18f);
                s.BloomIntensity = 0.012f;
                s.LensFlare = false;
                return true;
            case "golden":
                // Blender's still has two suns: its lamp at azimuth 13°, and its sky texture's sun disc, which
                // render.py's sun_rotation puts at −13° (mirrored) and which casts the street's shadows. One sun
                // here, the disc's, so the shadows fall as they do in the still, carrying the light of both
                // (twice the lamp's 4.5).
                Sun(s, elevation: 6f, blenderAzimuth: -13f, 9f, new Vector3(1.0f, 0.58f, 0.27f), angle: 0.8f, disc: 20f);
                s.SkyHorizon = new Vector3(0.95f, 0.78f, 0.6f);
                s.SkyZenith = new Vector3(0.42f, 0.5f, 0.66f);
                s.CloudCover = 0f;
                // Blender's haze: 0.0016 per metre (2.4 km visibility) up to 45 m, cream, strongly forward-scattering.
                s.HazeVisibilityMetres = 3.912f / 0.0016f; s.HazeHeightMetres = 45f;
                s.HazeColour = new Vector3(1.0f, 0.92f, 0.82f) * 0.55f;
                s.HazeAnisotropy = 0.82f; s.HazeSunScale = 0.6f;
                s.Exposure = 1f;
                Grade(s, contrast: 1.2f, saturation: 1.15f, lift: 0.01f, vignette: 0.28f);
                s.BloomIntensity = 0.03f;
                return true;
            case "dusk":
                Sun(s, elevation: 28f, blenderAzimuth: 150f, 0.5f, new Vector3(0.55f, 0.68f, 1.0f), angle: 0.6f, disc: 0f);
                s.SkyHorizon = new Vector3(0.055f, 0.075f, 0.14f);
                s.SkyZenith = new Vector3(0.006f, 0.01f, 0.028f);
                s.SkyGlow = new Vector3(0.16f, 0.12f, 0.16f); s.GlowYaw = 200f;
                s.CloudCover = 0f;
                // Blender's fog: 0.009 per metre (435 m visibility), cool blue, mildly forward-scattering.
                s.HazeVisibilityMetres = 3.912f / 0.009f; s.HazeHeightMetres = 45f;
                s.HazeColour = new Vector3(0.004f, 0.007f, 0.02f);   // the fog colour times the sky light it scatters
                s.HazeAnisotropy = 0.45f; s.HazeSunScale = 0.5f;
                s.Exposure = MathF.Pow(2f, 2.3f);
                Grade(s, contrast: 1.2f, saturation: 0.95f, lift: 0.004f, vignette: 0.35f);
                s.BloomIntensity = 0.03f;
                s.LensFlare = false;
                return true;
            case "morning":
                Morning(s);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The morning look (ours): the sun's place from the sun-path code, its colour the air's reddening at
    /// that height (warm white at 50°), the procedural clear sky that follows it, the cumulus, a light
    /// blue-grey haze, AgX with a little contrast.
    /// </summary>
    static void Morning(ModernSettings s)
    {
        s.NorthYaw = s.LevelNorthYaw;                // the level's compass, +X without one
        s.TimeOfDay(s.Hour ?? MorningHour, s.Day);   // the level's date, mid-May at 10:00 without one
        s.SunIntensity = 6f;
        s.SunColour = null;
        s.SunAngle = 0.53f;
        s.SunDisc = 40f;
        s.SkyHorizon = null; s.SkyZenith = null;
        s.SkyGlow = Vector3.Zero;
        s.CloudCover = 0f;
        s.GroundAlbedo = 0.25f;
        s.AoStrength = 1.2f;
        // A light haze: 30 km at its base, thinning over 600 m; a castle 2 km off keeps three quarters of its contrast.
        s.HazeVisibilityMetres = 30000f; s.HazeHeightMetres = 600f;
        s.HazeColour = null;
        s.HazeAnisotropy = 0.6f; s.HazeSunScale = 0.25f;
        s.Clouds = new ModernCloudSettings { Cover = 0.35f, Seconds = s.Clouds.Seconds, ClockOffset = s.Clouds.ClockOffset };
        // The clouds' shadows fall where they do at 10:00 in mid-May whatever the level's date: the
        // town starts in sun, as the clock's offset was chosen for, though a lower sun throws shadows further.
        // And whatever the level's compass. The reference is May's sun with north along +X, the frame
        // the offset was chosen in, and the wind's start is put where it stands in that frame.
        var (refAzimuth, refElevation) = ModernMath.SunAt(MorningHour, SeasonDay);
        s.Clouds.Shift = ModernClouds.ShiftFor(s.Clouds, ModernMath.SunDirection(refAzimuth, refElevation, ModernMath.OldTownNorthYaw), s.ToSun)
                         + ModernClouds.ShiftForNorth(s.Clouds, s.NorthYaw, ModernMath.OldTownNorthYaw);
        s.Exposure = 0.6f;
        Grade(s, contrast: 1.15f, saturation: 1.15f, lift: 0f, vignette: 0.15f);
        s.BloomIntensity = 0.015f;
        s.LensFlare = true;
        // Ours: the physically based sky and air (ModernAtmosphere) instead of the procedural sky,
        // with a summer town's aerosols, so the distance hazes blue-grey and the sky pales to the horizon; the
        // height haze stays for the town's own (the shafts' air), thinner, since the atmosphere now carries the
        // distance. The grade towards Kingdom Come's natural daylight: AgX base, gentle contrast, saturation
        // as it is, a little lift in the darks.
        s.Atmosphere = true;
        s.AtmosphereAerosols = MorningAerosols;
        s.HazeVisibilityMetres = 60000f;
        s.Exposure = MorningExposure;
        Grade(s, contrast: 1.05f, saturation: 1.0f, lift: 0.004f, vignette: 0.12f);
    }

    /// <summary>The morning's aerosols, times clear air's (ours): a light summer haze over a town of hearths.</summary>
    public const float MorningAerosols = 3f;
    /// <summary>The morning's exposure under the physically based sky (ours; 0.85, with the light probes lighting the shade), set by eye on the Old Town's views.</summary>
    public const float MorningExposure = 0.85f;

    static void Sun(ModernSettings s, float elevation, float blenderAzimuth, float irradiance, Vector3 colour, float angle, float disc)
    {
        // The concept's street and square are dark clay and river cobbles; and the path tracer's
        // occlusion reaches further than the screen's, so the looks darken it a little more.
        s.GroundAlbedo = 0.2f;
        s.AoStrength = 1.5f;
        s.NorthYaw = 0f;
        s.SunAzimuth = -blenderAzimuth;
        s.SunElevation = elevation;
        s.SunIntensity = irradiance;
        s.SunColour = colour;
        s.SunAngle = angle;
        s.SunDisc = disc;
    }

    static void Grade(ModernSettings s, float contrast, float saturation, float lift, float vignette)
    {
        s.ToneCurve = ModernToneCurve.AgX;
        s.Contrast = contrast; s.Saturation = saturation; s.Lift = lift; s.Vignette = vignette;
    }
}

/// <summary>
/// A scene's dressing for a look (ours): extra point and
/// spot lights, and changes to named materials, read from a small JSON file given to
/// <c>--view-gltf --lights</c>. For scenes whose lights did not come through the export (Blender does not
/// export its lights by default) and whose emissive materials change with the look (a torch's flame hidden
/// by day, windows lit at night):
/// <code>
/// { "lights": [ { "position": [x, y, z], "colour": [r, g, b], "intensity": 31.8, "range": 30,
///                 "direction": [x, y, z], "inner": 20, "outer": 30 } ],
///   "materials": { "name": { "emissive": [r, g, b], "strength": 9, "fraction": 0.58, "seed": 1, "hidden": false } } }
/// </code>
/// Positions are metres, Z up (Blender's axes; the engine's are 72 times them); intensity is the relative
/// irradiance one metre away (Blender's watts over 4π for its point lights); range, in metres, is optional
/// (else the reach where the light falls to <see cref="ModernSettings.LightCutoff"/>); a direction and cone
/// angles in degrees make a spot. A material's <c>fraction</c> lights only that share of the primitives
/// that use it, picked by a hash of their index (half a street's windows, say); <c>hidden</c> drops them.
/// </summary>
internal static class ModernDressing
{
    /// <summary>Applies the file's material changes to <paramref name="scene"/> and returns its lights (engine units).</summary>
    public static List<ModernPointLight> Apply(string path, GltfScene scene, float cutoff)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var lights = new List<ModernPointLight>();
        float u = ModernMath.UnitsPerMetre;
        if (root.TryGetProperty("lights", out var ls))
            foreach (var l in ls.EnumerateArray())
            {
                var pos = V3(l.GetProperty("position")) * u;
                var colour = l.TryGetProperty("colour", out var c) ? V3(c) : Vector3.One;
                float intensity = l.TryGetProperty("intensity", out var i) ? i.GetSingle() : 1f;
                float range = l.TryGetProperty("range", out var r) ? r.GetSingle() * u
                              : ModernLights.ReachFor(intensity * MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z)), cutoff);
                if (l.TryGetProperty("direction", out var d))
                {
                    float inner = l.TryGetProperty("inner", out var ia) ? ia.GetSingle() : 0f;
                    float outer = l.TryGetProperty("outer", out var oa) ? oa.GetSingle() : 45f;
                    lights.Add(new ModernPointLight(pos, colour * intensity, range, Vector3.Normalize(V3(d)),
                                                    MathF.Cos(inner * MathF.PI / 180f), MathF.Cos(outer * MathF.PI / 180f)));
                }
                else lights.Add(new ModernPointLight(pos, colour * intensity, range));
            }
        if (root.TryGetProperty("materials", out var ms))
            foreach (var m in ms.EnumerateObject())
            {
                int index = scene.Materials.FindIndex(x => x.Name == m.Name);
                if (index < 0) { scene.Warnings.Add($"dressing: no material {m.Name}"); continue; }
                var e = m.Value;
                if (e.TryGetProperty("hidden", out var h) && h.GetBoolean())
                {
                    scene.Primitives.RemoveAll(p => p.Material == index);
                    continue;
                }
                var source = scene.Materials[index];
                var lit = Clone(source);
                if (e.TryGetProperty("emissive", out var em)) lit.EmissiveFactor = V3(em);
                if (e.TryGetProperty("strength", out var st)) lit.EmissiveStrength = st.GetSingle();
                float fraction = e.TryGetProperty("fraction", out var f) ? f.GetSingle() : 1f;
                int seed = e.TryGetProperty("seed", out var sd) ? sd.GetInt32() : 1;
                if (fraction >= 1f) { scene.Materials[index] = lit; continue; }
                int litIndex = scene.Materials.Count;
                scene.Materials.Add(lit);
                for (int p = 0; p < scene.Primitives.Count; p++)
                    if (scene.Primitives[p].Material == index && Hash(p, seed) < fraction) scene.Primitives[p].Material = litIndex;
            }
        return lights;
    }

    /// <summary>A number in [0, 1) from a primitive's index and a seed, the same on every run.</summary>
    public static float Hash(int index, int seed)
    {
        uint x = (uint)index * 0x9E3779B1u ^ (uint)seed * 0x85EBCA77u;
        x ^= x >> 15; x *= 0x2C1B3C6Du; x ^= x >> 12; x *= 0x297A2D39u; x ^= x >> 15;
        return (x >> 8) / 16777216f;
    }

    static GltfMaterial Clone(GltfMaterial m) => new()
    {
        Name = m.Name + " (lit)", BaseColorFactor = m.BaseColorFactor, BaseColorTexture = m.BaseColorTexture,
        MetallicFactor = m.MetallicFactor, RoughnessFactor = m.RoughnessFactor, MetallicRoughnessTexture = m.MetallicRoughnessTexture,
        NormalTexture = m.NormalTexture, NormalScale = m.NormalScale, OcclusionTexture = m.OcclusionTexture,
        OcclusionStrength = m.OcclusionStrength, EmissiveFactor = m.EmissiveFactor, EmissiveTexture = m.EmissiveTexture,
        EmissiveStrength = m.EmissiveStrength, SpecularFactor = m.SpecularFactor, AlphaMode = m.AlphaMode, AlphaCutoff = m.AlphaCutoff, DoubleSided = m.DoubleSided,
    };

    static Vector3 V3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
}
