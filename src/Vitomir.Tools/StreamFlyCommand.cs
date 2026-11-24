// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SDL;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.World;
using Vitomir.Render;
using Vitomir.Render.Modern;
using static SDL.SDL3;

namespace Vitomir;

/// <summary>
/// **Ours (Phase 3): a streamed level flown through headless, for the streaming's proof.**
/// <c>vitomir --stream-fly &lt;package&gt; --out &lt;folder&gt; --path x,y,z,yaw,pitch;… [--speed m/s]
/// [--fps n] [--still-every n] [--size WxH] [--lod-stills x,y,z] [--quality low|medium|high|ultra] [--cloud-pass half|full]</c>: the camera goes along the path
/// (metres) at the speed, a frame each 1/fps of flight; each frame the renderer's streamer and the
/// simulation's (as if the player were under the camera) are updated, the frame drawn and timed, and a
/// row written to <c>stream.tsv</c> — the cells in, the memory, the loads and their times. Every n-th
/// frame is kept as a still. <c>--lod-stills</c> instead looks at a point from further and further off
/// and keeps a still either side of each change of what its cell is drawn as. The view distance and
/// budgets are the quality preset's, then the environment's (<see cref="ModernStreamSettings.FromEnvironment(ModernQualityPreset?)"/>).
/// </summary>
public static unsafe class StreamFlyCommand
{
    public static int Run(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        var package = Arg("--stream-fly");
        var outDir = Arg("--out");
        if (package is null || outDir is null)
        {
            Console.Error.WriteLine("usage: vitomir --stream-fly <package.l2> --out <folder> --path x,y,z,yaw,pitch;... [--speed 8] [--fps 4] " +
                                    "[--still-every 8] [--size 960x540] [--lod-stills x,y,z] [--quality low|medium|high|ultra]");
            return 2;
        }
        Directory.CreateDirectory(outDir);
        int width = 960, height = 540;
        if (Arg("--size") is { } size) { var wh = size.Split('x'); width = int.Parse(wh[0], CultureInfo.InvariantCulture); height = int.Parse(wh[1], CultureInfo.InvariantCulture); }

        var clock = Stopwatch.StartNew();
        var level = CellLevel.Load(Path.GetFullPath(package));
        Console.WriteLine($"stream-fly: {level.Name}: {level.Stream.Cells.Count} cells, {level.Manifest.Far?.Count ?? 0} far versions; " +
                          $"opened in {clock.ElapsedMilliseconds} ms with {level.Stream.Loaded.Count} cells in for the simulation");
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") ?? "");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SdlException("SDL_Init");
        try
        {
            using var gpu = GpuDrivers.Open(null, Arg("--gpu"), false, Console.WriteLine);   // Vulkan first on Windows (ours)
            // The graphics quality (ours): --quality, else the ini's or the GPU's; never kept from a harness.
            CellLooks.Quality = Arg("--quality");
            if (Arg("--cloud-pass") is { } cp) CellLooks.CloudsHalf = cp != "full";
            if (Arg("--probes") is { } pr) CellLooks.Probes = pr != "off";   // the light probes (ours)
            if (Arg("--tiling") is { } tl) CellLooks.Tiling = tl != "plain";   // the materials' hex tiling (ours)
            CellLooks.ResolveQuality(gpu);
            var settings = CellWorldPass.SettingsFor(level);
            clock.Restart();
            using var stream = new CellStreamScene(gpu, level, settings.Anisotropy, CellLooks.StreamSettings());
            using var renderer = new ModernRenderer(gpu, settings, gpu.ColorFormat);
            Console.WriteLine($"stream-fly: materials and renderer in {clock.ElapsedMilliseconds} ms; view distance " +
                              $"{(stream.Settings.WholeMap ? "whole map" : $"{stream.Settings.ViewDistanceMetres:0} m")}, near {stream.Settings.NearMetres:0} m, " +
                              $"LOD1 from {stream.Settings.Lod1Metres:0} m, geometry budget {stream.Settings.GeometryBudgetMB} MB");
            if (Arg("--lod-stills") is { } target)
            {
                var t = target.Split(',').Select(F).ToArray();
                return LodStills(gpu, level, stream, renderer, new Vector3(t[0], t[1], t[2]), outDir, width, height);
            }
            var path = (Arg("--path") ?? "0,0,20,0,-10").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(',').Select(F).ToArray()).ToArray();
            float speed = Arg("--speed") is { } sp ? F(sp) : 8f, fps = Arg("--fps") is { } fp ? F(fp) : 4f;
            int stillEvery = Arg("--still-every") is { } se ? int.Parse(se, CultureInfo.InvariantCulture) : 8;
            return Fly(gpu, level, stream, renderer, path, speed, fps, stillEvery, outDir, width, height);
        }
        finally { SDL_Quit(); }
    }

    const float M = 72f;

    static FreeCamera Camera(float[] p) => new()
    {
        Position = new Vector3(p[0], p[1], p[2]) * M, Yaw = p.Length > 3 ? p[3] : 0f, Pitch = p.Length > 4 ? p[4] : -10f,
    };

    static int Fly(Gpu gpu, CellLevel level, CellStreamScene stream, ModernRenderer renderer, float[][] path, float speed, float fps,
                   int stillEvery, string outDir, int width, int height)
    {
        var target = gpu.CreateTarget(width, height, gpu.ColorFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        using var tsv = new StreamWriter(Path.Combine(outDir, "stream.tsv"));
        tsv.WriteLine("frame\tseconds\tx\ty\tz\tsim_cells\tsim_mb\tsim_loads\tsim_load_ms\tsim_merge_ms\tsim_apply_ms\t" +
                      "draw_cells\tdraw_far\tlod0_cells\tlod1_cells\tfar_drawn\tdraws\ttriangles\tgeometry_mb\tdraw_loads\tdraw_unloads\t" +
                      "draw_load_ms\tupload_ms\tpending\ttexture_mb\ttextures_full\ttexture_mean_top\tprocess_mb\tmanaged_mb\tframe_ms");
        // Seconds along the path at each waypoint.
        var at = new List<float> { 0f };
        for (int i = 1; i < path.Length; i++)
            at.Add(at[^1] + Vector3.Distance(new Vector3(path[i - 1][0], path[i - 1][1], path[i - 1][2]), new Vector3(path[i][0], path[i][1], path[i][2])) / speed);
        float total = at[^1];
        int frames = (int)MathF.Ceiling(total * fps) + 1;
        var times = new List<double>();
        var proc = Process.GetCurrentProcess();
        // What each part of a frame allocates, when VITOMIR_ALLOC_LOG asks (ours).
        var allocations = Vitomir.Engine.Diagnostics.FrameAllocations.FromEnvironment(["simulation cells", "streaming", "drawing", "submit"]);
        for (int f = 0; f < frames; f++)
        {
            allocations?.Begin(f + 1);
            float s = Math.Min(f / fps, total);
            int seg = Math.Max(0, at.FindLastIndex(x => x <= s));
            if (seg >= path.Length - 1) seg = path.Length - 2;
            float u = seg < 0 || at[seg + 1] <= at[seg] ? 1f : (s - at[seg]) / (at[seg + 1] - at[seg]);
            var a = path[Math.Max(seg, 0)]; var b = path[Math.Min(seg + 1, path.Length - 1)];
            var p = a.Zip(b, (x, y) => x + (y - x) * u).ToArray();
            var camera = Camera(p);
            level.Stream.Update(camera.Position with { Z = 0 });        // the simulation's cells, as if the player stood under the camera
            allocations?.Mark(0);
            var watch = Stopwatch.StartNew();
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            var cull = ModernRenderer.ViewProjection(camera, (float)width / height);
            if (!stream.Started) stream.LoadNow(camera.Position, cull);
            stream.Update(cmd, camera.Position, cull, height / (2f * MathF.Tan(camera.VerticalFov / 2f)));
            allocations?.Mark(1);
            renderer.Render(cmd, target, width, height, stream.Scene, camera);
            allocations?.Mark(2);
            var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
            SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
            SDL_ReleaseGPUFence(gpu.Device, fence);
            allocations?.Mark(3);
            allocations?.End();
            double ms = watch.Elapsed.TotalMilliseconds;
            times.Add(ms);
            proc.Refresh();
            var sim = level.Stream;
            tsv.WriteLine(string.Join('\t', new object[]
            {
                f, s.ToString("0.00", CultureInfo.InvariantCulture), p[0].ToString("0.0", CultureInfo.InvariantCulture),
                p[1].ToString("0.0", CultureInfo.InvariantCulture), p[2].ToString("0.0", CultureInfo.InvariantCulture),
                sim.Loaded.Count, (sim.LoadedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture), sim.LoadCount,
                sim.LoadMilliseconds.ToString("0", CultureInfo.InvariantCulture), sim.LastMergeMilliseconds.ToString("0.0", CultureInfo.InvariantCulture),
                sim.LastApplyMilliseconds.ToString("0.0", CultureInfo.InvariantCulture),
                stream.ResidentCells, stream.ResidentFar, stream.DrawnCellsLod0, stream.DrawnCellsLod1, stream.DrawnFar, stream.Scene.Draws.Count,
                stream.DrawnTriangles, (stream.ResidentBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture), stream.Loads, stream.Unloads,
                stream.LoadMilliseconds.ToString("0", CultureInfo.InvariantCulture), stream.LastUploadMilliseconds.ToString("0.0", CultureInfo.InvariantCulture),
                stream.Pending, ((stream.Textures?.ResidentBytes ?? 0) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture),
                stream.Textures?.AtFullSize ?? 0, (stream.Textures?.MeanTop ?? 0).ToString("0.00", CultureInfo.InvariantCulture), proc.WorkingSet64 / 1048576, GC.GetTotalMemory(false) / 1048576, ms.ToString("0", CultureInfo.InvariantCulture),
            }));
            if (stillEvery > 0 && (f % stillEvery == 0 || f == frames - 1))
            {
                var file = Path.Combine(outDir, $"fly{f:0000}.png");
                Png.Write(file, width, height, gpu.Download(target, width, height));
            }
            if (f % 10 == 0) Console.WriteLine($"  frame {f}/{frames}: at {p[0]:0},{p[1]:0}; sim {sim.Loaded.Count} cells; drawn {stream.DrawnCellsLod0}+{stream.DrawnCellsLod1} cells, " +
                                               $"{stream.DrawnFar} far, {stream.DrawnTriangles:N0} triangles; {ms:0} ms");
        }
        SDL_ReleaseGPUTexture(gpu.Device, target);
        allocations?.Summary(level.Name);
        times.Sort();
        Console.WriteLine($"stream-fly: {frames} frames, median {times[times.Count / 2]:0} ms, 95th {times[(int)(times.Count * 0.95)]:0} ms; " +
                          $"drawing streamer {stream.Loads} loads ({stream.LoadMilliseconds:0} ms reading), {stream.Unloads} unloads; " +
                          $"simulation {level.Stream.LoadCount} loads ({level.Stream.LoadMilliseconds:0} ms), {level.Stream.UnloadCount} unloads");
        return 0;
    }

    /// <summary>Stills of one point from further and further off, one either side of every change in what its cell is drawn as.</summary>
    static int LodStills(Gpu gpu, CellLevel level, CellStreamScene stream, ModernRenderer renderer, Vector3 target, string outDir, int width, int height)
    {
        string? last = null;
        byte[]? lastPixels = null; float lastDistance = 0;
        for (float d = 20f; d <= 420f; d += 4f)
        {
            // Looking back at the point from the south-west, a little above.
            var dir = Vector3.Normalize(new Vector3(-1f, -1f, 0.35f));
            var eye = target + dir * d;
            var toward = target - eye;
            var camera = new FreeCamera
            {
                Position = eye * M, Yaw = MathF.Atan2(toward.Y, toward.X) * 180f / MathF.PI,
                Pitch = MathF.Atan2(toward.Z, new Vector2(toward.X, toward.Y).Length()) * 180f / MathF.PI,
            };
            var cull = ModernRenderer.ViewProjection(camera, (float)width / height);
            stream.LoadNow(camera.Position, cull);
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            stream.Update(cmd, camera.Position, cull, height / (2f * MathF.Tan(camera.VerticalFov / 2f)));
            var state = stream.Describe(target * M);
            var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
            SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
            SDL_ReleaseGPUFence(gpu.Device, fence);
            var pixels = renderer.RenderToImage(stream.Scene, camera, width, height);
            if (last is not null && state != last)
            {
                Png.Write(Path.Combine(outDir, $"lod_{lastDistance:000}m_{last}.png"), width, height, lastPixels!);
                Png.Write(Path.Combine(outDir, $"lod_{d:000}m_{state}.png"), width, height, pixels);
                Console.WriteLine($"  {last} -> {state} between {lastDistance:0} and {d:0} m");
            }
            (last, lastPixels, lastDistance) = (state, pixels, d);
        }
        return 0;
    }
}
