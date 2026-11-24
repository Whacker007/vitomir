// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using Vitomir.Engine.World;

namespace Vitomir.Render.Modern;

/// <summary>
/// What each of the modern path's features costs (ours; <c>vitomir --view-gltf … --bench n</c>): the
/// same view timed with everything the settings turn on, then with each feature turned off in turn, and
/// with all of them off. A feature's cost is the difference it makes; on a software GPU the relative
/// sizes are what count, not the milliseconds.
/// </summary>
public static class ModernBench
{
    public static void Run(ModernRenderer renderer, ModernScene scene, FreeCamera camera, int width, int height, int frames)
    {
        var s = renderer.Settings;
        int lightShadows = s.LightShadows;
        float cover = s.Clouds.Cover, cloudShadows = s.Clouds.ShadowStrength;
        int parallax = s.ParallaxSteps, ssr = s.SsrSteps;
        var features = new (string Name, Func<bool> Get, Action<bool> Set)[]
        {
            ("sun shadows", () => s.Shadows, v => s.Shadows = v),
            ("point-light shadows", () => s.LightShadows > 0, v => s.LightShadows = v ? Math.Max(lightShadows, 1) : 0),
            ("ambient occlusion", () => s.AmbientOcclusion, v => s.AmbientOcclusion = v),
            ("sun shafts", () => s.Volumetric, v => s.Volumetric = v),
            ("point and spot lights", () => s.Lights, v => s.Lights = v),
            ("bloom", () => s.Bloom, v => s.Bloom = v),
            ("lens flare", () => s.LensFlare, v => s.LensFlare = v),
            ("FXAA", () => s.Fxaa, v => s.Fxaa = v),
            ("temporal anti-aliasing", () => s.Taa, v => s.Taa = v),
            ("cloud shadows", () => s.Clouds.Cover > 0 && s.Clouds.ShadowStrength > 0, v => s.Clouds.ShadowStrength = v ? cloudShadows : 0),
            ("clouds, sky and shadows", () => s.Clouds.Cover > 0, v => s.Clouds.Cover = v ? cover : 0),
            // (ours).
            ("parallax occlusion", () => s.ParallaxSteps > 0, v => s.ParallaxSteps = v ? parallax : 0),
            ("screen-space reflections", () => s.SsrSteps > 0, v => s.SsrSteps = v ? ssr : 0),
            ("specular anti-aliasing", () => s.SpecularAntiAliasing, v => s.SpecularAntiAliasing = v),   // (ours)
            ("physical sky and air", () => s.Atmosphere, v => s.Atmosphere = v),
            ("large-scale variation", () => s.MacroVariation, v => s.MacroVariation = v),
            ("hex tiling", () => s.HexTiling, v => s.HexTiling = v),   // (ours)
        };
        var on = features.Select(f => f.Get()).ToArray();
        double all = renderer.TimeFrames(scene, camera, width, height, frames);
        Console.WriteLine($"bench {width}x{height}, {frames} frames each, {renderer.LastDrawn} draws, {renderer.LastLights} lights");
        Console.WriteLine($"  everything the settings turn on: {all:0.0} ms a frame");
        for (int i = 0; i < features.Length; i++)
        {
            if (!on[i]) { Console.WriteLine($"  {features[i].Name}: off in these settings"); continue; }
            features[i].Set(false);
            double t = renderer.TimeFrames(scene, camera, width, height, frames);
            features[i].Set(true);
            Console.WriteLine($"  {features[i].Name}: {all - t:0.0} ms ({t:0.0} without it)");
        }
        for (int i = 0; i < features.Length; i++) features[i].Set(false);
        double bare = renderer.TimeFrames(scene, camera, width, height, frames);
        for (int i = 0; i < features.Length; i++) features[i].Set(on[i]);
        Console.WriteLine($"  all of them off (surfaces, sky, tone mapping): {bare:0.0} ms");
        // The physically based sky's tables (ours) are worked out on the CPU when the sun or the eye's height moves.
        if (s.Atmosphere)
        {
            var atm = ModernAtmosphere.For(s.AtmosphereAerosols, s.GroundAlbedo);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            atm.SkyView(s.ToSun, 0.002f);
            double sky = clock.Elapsed.TotalMilliseconds;
            atm.AerialPerspective(s.ToSun, 0.002f);
            Console.WriteLine($"  the atmosphere's tables on the CPU: sky view {sky:0} ms, aerial perspective {clock.Elapsed.TotalMilliseconds - sky:0} ms");
        }
    }
}
