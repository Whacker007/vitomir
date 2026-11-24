// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SDL;
using Vitomir.Engine.World;
using Vitomir.Render;
using Vitomir.Render.Modern;
using static SDL.SDL3;

namespace Vitomir;

/// <summary>
/// `vitomir --view-gltf &lt;file.gltf|glb&gt;` (ours): a glTF scene drawn by the
/// modern renderer, in a window with a free camera or, with `--headless &lt;out.png&gt;`, as one still. The
/// file is placed in the engine's world at 72 units a metre, Z up, so the camera takes the same
/// `--pos x,y,z --yaw a --pitch b` as the game's free camera and a view of a level's export matches
/// the same view of the level. Nothing of the game runs; it reads no game files.
/// </summary>
public static unsafe class ViewGltfCommand
{
    const string Usage = "usage: vitomir --view-gltf <file.gltf|glb> [--pos x,y,z --yaw a --pitch b] [--fov <horizontal degrees>] [--sun azimuth,elevation | --time-of-day h] [--day n] [--north yaw]\n" +
                         "       [--exposure x] [--haze <visibility km, 0 off>] [--haze-height <m>] [--no-shadows] [--cascades 1-4] [--shadow-res n]\n" +
                         "       [--no-fxaa] [--aniso n] [--vertex-colours on|off] [--size WxH] [--headless out.png] [--gpu <driver>]\n" +
                         "       [--look chronicle|golden|dusk|morning] [--clouds <cover 0..1>] [--wind <from°,m/s>] [--cloud-steps n] [--cloud-time <s>]\n" +
                         "       [--lights <dressing.json>] [--no-lights] [--light-shadows n] [--no-ao] [--ao-radius <m>]\n" +
                         "       [--no-shafts] [--shaft-steps n] [--no-bloom] [--bloom <share>] [--bloom-threshold x] [--no-flare]\n" +
                         "       [--taa|--no-taa] [--tonemap aces|agx] [--bench <frames>] [--debug ao|shafts] [--quality low|medium|high|ultra|auto] [--cloud-pass half|full]\n" +
                         "       [--parallax-steps n] [--ssr-steps n] [--no-atmosphere] [--aerosols x] [--no-macro] [--tiling hex|plain]";

    public static int Run(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        string? file = Arg("--view-gltf");
        if (file is null || file.StartsWith("--") || !File.Exists(file)) { Console.Error.WriteLine(Usage); return 2; }

        if (!ModernQualities.Check(Arg("--quality"))) return 2;
        if (Arg("--look") is { } look && !ModernLooks.Apply(new ModernSettings(), look))
        {
            Console.Error.WriteLine($"no look {look}; the looks are {string.Join(", ", ModernLooks.Names)}");
            return 2;
        }
        var settings = Options(args, null);
        bool? vertexColours = Arg("--vertex-colours") switch { "on" => true, "off" => false, _ => null };

        string? headless = Arg("--headless");
        int width = headless is null ? 1280 : 1600, height = headless is null ? 720 : 900;
        if (Arg("--size") is { } size) { var wh = size.Split('x'); width = int.Parse(wh[0], CultureInfo.InvariantCulture); height = int.Parse(wh[1], CultureInfo.InvariantCulture); }

        var clock = Stopwatch.StartNew();
        var gltf = GltfReader.Load(file, new GltfReadOptions(VertexColours: vertexColours));
        Console.WriteLine($"read {file}: {gltf.Primitives.Count} primitives, {gltf.TriangleCount} triangles, {gltf.Materials.Count} materials, " +
                          $"{gltf.Images.Count(i => i.Rgba is not null)}/{gltf.Images.Count} images in {clock.ElapsedMilliseconds} ms");
        var dressed = new List<ModernPointLight>();
        if (Arg("--lights") is { } dressing) dressed = ModernDressing.Apply(dressing, gltf, settings.LightCutoff);
        foreach (var w in gltf.Warnings.Distinct().Take(20)) Console.WriteLine($"  warning: {w}");
        Console.WriteLine($"  bounds {Fmt(gltf.Min)} .. {Fmt(gltf.Max)} (engine units); sun azimuth {settings.SunAzimuth:0.#} elevation {settings.SunElevation:0.#}");

        var camera = new FreeCamera();
        if (Arg("--pos") is { } pos) { var p = pos.Split(',').Select(F).ToArray(); camera.Position = new Vector3(p[0], p[1], p[2]); }
        else
        {
            // No view given: from a corner of the scene, above it, looking at its middle.
            var centre = (gltf.Min + gltf.Max) * 0.5f; var span = gltf.Max - gltf.Min;
            camera.Position = new Vector3(gltf.Min.X - span.X * 0.1f, gltf.Min.Y - span.Y * 0.1f, gltf.Max.Z + span.Length() * 0.15f);
            var v = centre - camera.Position;
            camera.Yaw = MathF.Atan2(v.Y, v.X) * 180f / MathF.PI;
            camera.Pitch = MathF.Atan2(v.Z, new Vector2(v.X, v.Y).Length()) * 180f / MathF.PI;
        }
        if (Arg("--yaw") is { } yaw) camera.Yaw = F(yaw);
        // The picture's horizontal field of view, as a Blender camera gives it (2·atan(sensor / 2 / lens)); kept as
        // the free camera's 4:3 figure, which fixes the vertical and widens with the window (Hor+).
        if (Arg("--fov") is { } fov)
        {
            float v = 2f * MathF.Atan(MathF.Tan(F(fov) * MathF.PI / 360f) * height / width);
            camera.HorizontalFov43 = 2f * MathF.Atan(MathF.Tan(v / 2f) * 4f / 3f) * 180f / MathF.PI;
        }
        if (Arg("--pitch") is { } pitch) camera.Pitch = F(pitch);

        if (headless is not null) SDL_SetHint(SDL_HINT_VIDEO_DRIVER, Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") ?? "");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SdlException("SDL_Init");
        SDL_Window* window = null;
        if (headless is null)
        {
            window = SDL_CreateWindow($"Vitomir - {Path.GetFileName(file)} (modern renderer)", width, height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
            if (window == null) throw new SdlException("SDL_CreateWindow");
        }
        int bench = Arg("--bench") is { } b ? int.Parse(b, CultureInfo.InvariantCulture) : 0;
        try { return Draw(window, Arg("--gpu"), gltf, dressed, args, camera, headless, width, height, bench); }
        finally
        {
            if (window != null) SDL_DestroyWindow(window);
            SDL_Quit();
        }
    }

    /// <summary>
    /// The renderer's settings from the command line: a look, then a quality preset (<paramref name="quality"/>;
    /// null: the renderer's defaults, medium's), then every single option over them.
    /// </summary>
    static ModernSettings Options(string[] args, ModernQualityPreset? quality)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool Flag(string name) => args.Contains(name);
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        var settings = new ModernSettings();
        if (Arg("--day") is { } dayArg) settings.Day = Math.Clamp(int.Parse(dayArg, CultureInfo.InvariantCulture), 1, 365);   // the date the morning follows
        // A look first, then the quality preset (ours), so the options after them can change any of their values.
        if (Arg("--look") is { } look) ModernLooks.Apply(settings, look);
        quality?.Apply(settings);
        if (Arg("--cloud-pass") is { } cp) settings.Clouds.HalfResolution = cp != "full";
        if (Arg("--time-of-day") is { } tod) settings.TimeOfDay(F(tod), Arg("--day") is not null ? settings.Day : 120);
        if (Arg("--clouds") is { } cl) settings.Clouds.Cover = Math.Clamp(F(cl), 0f, 1f);
        if (Arg("--wind") is { } wind) { var p = wind.Split(',').Select(F).ToArray(); settings.Clouds.WindFrom = p[0]; if (p.Length > 1) settings.Clouds.WindSpeed = p[1]; }
        if (Arg("--cloud-steps") is { } cst) settings.Clouds.Steps = Math.Clamp(int.Parse(cst, CultureInfo.InvariantCulture), 0, 64);
        if (Arg("--cloud-time") is { } clk) settings.Clouds.ClockOffset = F(clk);
        if (Arg("--sun") is { } sun) { var p = sun.Split(',').Select(F).ToArray(); settings.SunAzimuth = p[0]; settings.SunElevation = p[1]; }
        if (Arg("--north") is { } north) settings.NorthYaw = F(north);
        if (Arg("--exposure") is { } ex) settings.Exposure = F(ex);
        if (Arg("--haze") is { } hz) settings.HazeVisibilityMetres = F(hz) * 1000f;
        if (Arg("--haze-height") is { } hh) settings.HazeHeightMetres = F(hh);
        if (Flag("--no-shadows")) settings.Shadows = false;
        if (Arg("--cascades") is { } cs) settings.Cascades = Math.Clamp(int.Parse(cs, CultureInfo.InvariantCulture), 1, 4);
        if (Arg("--shadow-res") is { } sr) settings.ShadowResolution = Math.Clamp(int.Parse(sr, CultureInfo.InvariantCulture), 256, 8192);
        if (Flag("--no-fxaa")) settings.Fxaa = false;
        if (Flag("--taa")) settings.Taa = true;
        if (Flag("--no-taa")) settings.Taa = false;
        if (Arg("--aniso") is { } an) settings.Anisotropy = Math.Clamp(F(an), 1f, 16f);
        if (Flag("--no-lights")) settings.Lights = false;
        if (Arg("--light-shadows") is { } lsh) settings.LightShadows = Math.Clamp(int.Parse(lsh, CultureInfo.InvariantCulture), 0, 16);
        if (Flag("--no-ao")) settings.AmbientOcclusion = false;
        if (Arg("--ao-radius") is { } ar) settings.AoRadiusMetres = F(ar);
        if (Flag("--no-shafts")) settings.Volumetric = false;
        if (Arg("--shaft-steps") is { } ss) settings.VolumetricSteps = int.Parse(ss, CultureInfo.InvariantCulture);
        if (Flag("--no-bloom")) settings.Bloom = false;
        if (Arg("--bloom") is { } bl) settings.BloomIntensity = F(bl);
        if (Arg("--bloom-threshold") is { } bt) settings.BloomThreshold = F(bt);
        if (Flag("--no-flare")) settings.LensFlare = false;
        if (Arg("--debug") is { } dbg) settings.Debug = dbg == "ao" ? ModernDebugView.AmbientOcclusion : dbg == "shafts" ? ModernDebugView.Shafts : ModernDebugView.None;
        if (Arg("--tonemap") is { } tm) settings.ToneCurve = tm == "agx" ? ModernToneCurve.AgX : ModernToneCurve.Aces;
        // Ours: parallax, screen-space reflections, the physically based sky, the large-scale variation.
        if (Arg("--parallax-steps") is { } ps) settings.ParallaxSteps = Math.Clamp(int.Parse(ps, CultureInfo.InvariantCulture), 0, 64);
        if (Arg("--ssr-steps") is { } srs) settings.SsrSteps = Math.Clamp(int.Parse(srs, CultureInfo.InvariantCulture), 0, 96);
        if (Flag("--no-atmosphere")) settings.Atmosphere = false;
        if (Arg("--aerosols") is { } aer) settings.AtmosphereAerosols = Math.Clamp(F(aer), 0f, 100f);
        if (Flag("--no-macro")) settings.MacroVariation = false;
        if (Arg("--tiling") is { } tl) settings.HexTiling = tl != "plain";   // Ours: the materials' hex tiling

        return settings;
    }

    /// <summary>The GPU's part, so that everything on the device is let go before the window and SDL are.</summary>
    static int Draw(SDL_Window* window, string? driver, GltfScene gltf, List<ModernPointLight> dressed, string[] args, FreeCamera camera,
                    string? headless, int width, int height, int bench)
    {
        using var gpu = GpuDrivers.Open(window, driver, false, Console.WriteLine);   // Vulkan first on Windows (ours)
        // The quality preset needs the GPU (an integrated one gets low); a choice is kept only from a window (ours).
        int q = Array.IndexOf(args, "--quality");
        var preset = ModernQualities.ForRun(q >= 0 && q + 1 < args.Length ? args[q + 1] : null, gpu, remember: headless is null && bench == 0);
        var settings = Options(args, preset);
        var clock = Stopwatch.StartNew();
        using var scene = new ModernScene(gpu, gltf, settings.Anisotropy);
        scene.Lights.AddRange(dressed);
        using var renderer = new ModernRenderer(gpu, settings, gpu.ColorFormat);
        Console.WriteLine($"  on the GPU ({gpu.Driver}) in {clock.ElapsedMilliseconds} ms; {scene.Lights.Count} point and spot lights");
        if (bench > 0)
        {
            ModernBench.Run(renderer, scene, camera, width, height, bench);
            return 0;
        }

        if (headless is not null)
        {
            clock.Restart();
            var pixels = renderer.RenderToImage(scene, camera, width, height);
            Png.Write(headless, width, height, pixels);
            Console.WriteLine($"wrote {headless} ({width}x{height}, {renderer.LastDrawn}/{scene.Draws.Count} draws in view) in {clock.ElapsedMilliseconds} ms from {Fmt(camera.Position)} yaw {camera.Yaw:0.#} pitch {camera.Pitch:0.#}");
            return 0;
        }

        Console.WriteLine("  keys: W/S/A/D move, Q/E down/up, arrows or right-drag look, Shift fast, [ ] sun elevation, , . sun azimuth, - = exposure, F FXAA, T TAA, H shadows, P print the view, Esc quit");
        Interactive(gpu, window, renderer, scene, camera, settings);
        return 0;
    }

    static void Interactive(Gpu gpu, SDL_Window* window, ModernRenderer renderer, ModernScene scene, FreeCamera camera, ModernSettings settings)
    {
        var clock = Stopwatch.StartNew();
        double last = 0;
        bool running = true, dragging = false;
        var held = new HashSet<SDL_Scancode>();
        while (running)
        {
            SDL_Event e;
            float mx = 0, my = 0;
            while (SDL_PollEvent(&e))
            {
                switch ((SDL_EventType)e.type)
                {
                    case SDL_EventType.SDL_EVENT_QUIT: running = false; break;
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == SDL_BUTTON_RIGHT: dragging = true; SDL_SetWindowRelativeMouseMode(window, true); break;
                    case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == SDL_BUTTON_RIGHT: dragging = false; SDL_SetWindowRelativeMouseMode(window, false); break;
                    case SDL_EventType.SDL_EVENT_MOUSE_MOTION when dragging: mx += e.motion.xrel; my += e.motion.yrel; break;
                    case SDL_EventType.SDL_EVENT_KEY_UP: held.Remove(e.key.scancode); break;
                    case SDL_EventType.SDL_EVENT_KEY_DOWN:
                        held.Add(e.key.scancode);
                        switch (e.key.scancode)
                        {
                            case SDL_Scancode.SDL_SCANCODE_ESCAPE: running = false; break;
                            case SDL_Scancode.SDL_SCANCODE_F: settings.Fxaa = !settings.Fxaa; break;
                            case SDL_Scancode.SDL_SCANCODE_T: settings.Taa = !settings.Taa; break;
                            case SDL_Scancode.SDL_SCANCODE_H: settings.Shadows = !settings.Shadows; break;
                            case SDL_Scancode.SDL_SCANCODE_P:
                                Console.WriteLine($"--pos {camera.Position.X:0},{camera.Position.Y:0},{camera.Position.Z:0} --yaw {camera.Yaw:0.#} --pitch {camera.Pitch:0.#} " +
                                                  $"--sun {settings.SunAzimuth:0.#},{settings.SunElevation:0.#} --exposure {settings.Exposure:0.##}");
                                break;
                        }
                        break;
                }
            }
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - last, 0.1);
            last = now;
            bool Held(SDL_Scancode k) => held.Contains(k);
            float speed = (Held(SDL_Scancode.SDL_SCANCODE_LSHIFT) || Held(SDL_Scancode.SDL_SCANCODE_RSHIFT) ? 4f : 1f) * 4f * ModernMath.UnitsPerMetre * dt;
            float fwd = (Held(SDL_Scancode.SDL_SCANCODE_W) ? 1 : 0) - (Held(SDL_Scancode.SDL_SCANCODE_S) ? 1 : 0);
            float right = (Held(SDL_Scancode.SDL_SCANCODE_D) ? 1 : 0) - (Held(SDL_Scancode.SDL_SCANCODE_A) ? 1 : 0);
            float up = (Held(SDL_Scancode.SDL_SCANCODE_E) ? 1 : 0) - (Held(SDL_Scancode.SDL_SCANCODE_Q) ? 1 : 0);
            camera.Move(fwd, right, up, speed);
            float turn = 90f * dt;
            camera.Turn(((Held(SDL_Scancode.SDL_SCANCODE_LEFT) ? 1 : 0) - (Held(SDL_Scancode.SDL_SCANCODE_RIGHT) ? 1 : 0)) * turn - mx * 0.15f,
                        ((Held(SDL_Scancode.SDL_SCANCODE_UP) ? 1 : 0) - (Held(SDL_Scancode.SDL_SCANCODE_DOWN) ? 1 : 0)) * turn - my * 0.15f);
            float sunStep = 30f * dt;
            if (Held(SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET)) settings.SunElevation = Math.Min(settings.SunElevation + sunStep, 90f);
            if (Held(SDL_Scancode.SDL_SCANCODE_LEFTBRACKET)) settings.SunElevation = Math.Max(settings.SunElevation - sunStep, -10f);
            if (Held(SDL_Scancode.SDL_SCANCODE_PERIOD)) settings.SunAzimuth = (settings.SunAzimuth + sunStep * 2) % 360f;
            if (Held(SDL_Scancode.SDL_SCANCODE_COMMA)) settings.SunAzimuth = (settings.SunAzimuth - sunStep * 2 + 360f) % 360f;
            if (Held(SDL_Scancode.SDL_SCANCODE_EQUALS)) settings.Exposure *= MathF.Exp(dt);
            if (Held(SDL_Scancode.SDL_SCANCODE_MINUS)) settings.Exposure *= MathF.Exp(-dt);

            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            SDL_GPUTexture* swap; uint sw, sh;
            if (SDL_WaitAndAcquireGPUSwapchainTexture(cmd, window, &swap, &sw, &sh) && swap != null && sw > 0 && sh > 0)
                renderer.Render(cmd, swap, (int)sw, (int)sh, scene, camera);
            SDL_SubmitGPUCommandBuffer(cmd);
            if (D3D12SamplerBudget.Stopped is not null) return;   // Direct3D 12 could not draw it: said once, and the viewer closes (ours)
        }
    }

    static string Fmt(Vector3 v) => $"({v.X:0},{v.Y:0},{v.Z:0})";
}
