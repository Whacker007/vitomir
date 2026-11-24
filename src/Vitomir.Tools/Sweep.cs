// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Globalization;
using System.Numerics;
using SDL;
using Vitomir.Classic;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir;

/// <summary>
/// The regression harness: renders every level in the install with no window, writes a PNG and a
/// line of numbers for each, and compares the run against an earlier one. Run a sweep before a
/// change and compare after it; every level that moved shows up, with how far.
/// </summary>
public static unsafe class Sweep
{
    /// <summary>One level's frame, as a row of the sweep's CSV.</summary>
    readonly record struct Row(string Level, int Batches, int Triangles, int Missing, float Mean, uint Hash)
    {
        public string ToCsv() => string.Create(CultureInfo.InvariantCulture,
            $"{Level},{Batches},{Triangles},{Missing},{Mean:F4},{Hash}");

        public static Row FromCsv(string line)
        {
            var f = line.Split(',');
            return new Row(f[0], int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]),
                           float.Parse(f[4], CultureInfo.InvariantCulture), uint.Parse(f[5]));
        }
    }

    public static int Run(GameFiles files, string directory, string? compareWith, string size, string? driver)
    {
        var parts = size.Split('x');
        int width = int.Parse(parts[0]), height = int.Parse(parts[1]);
        Directory.CreateDirectory(directory);
        var levels = files.List("levels/")
            .Where(p => p.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") ?? "");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SdlException("SDL_Init");
        using var gpu = new Gpu(null, driver);
        var renderer = new ClassicRenderer(gpu);                         // the host's renderer for 1.1's frames
        Console.WriteLine($"sweeping {levels.Length} levels at {width}x{height} on {gpu.Driver} into {directory}");

        var rows = new List<Row>();
        var failed = new List<string>();
        var started = DateTime.UtcNow;
        foreach (var name in levels)
        {
            try
            {
                var location = Location.Load(files, name);
                _ = LightStyles.DrawTheRenderersSpare();                 // the renderer's old three draws, where it made them
                using var frame = new PlaceFrame(renderer, location);
                var camera = location.Camera();
                frame.Game.Frame(camera, width, height, 0f);             // the still's game side (the renderer only reads)
                var pixels = frame.RenderToImage(width, height, camera);
                Png.Write(Path.Combine(directory, name + ".png"), width, height, pixels);
                rows.Add(new Row(name, location.Mesh.Batches.Count, location.Mesh.Indices.Length / 3,
                                 frame.World.MissingTextures, Mean(pixels), Hash(pixels)));
            }
            catch (Exception e)
            {
                failed.Add($"{name}: {e.Message}");
            }
        }
        // Every text file Vitomir writes for itself carries comments, so that a year later the file
        // says what it is without anyone having to find the code that wrote it. `#` starts one, and
        // the reader below skips them.
        File.WriteAllLines(Path.Combine(directory, "sweep.csv"),
            new[]
            {
                "# Vitomir level sweep — one rendered frame per level, for comparing against a later run.",
                $"# written {DateTime.Now:yyyy-MM-dd HH:mm:ss} at {width}x{height}, {rows.Count} levels" +
                (failed.Count > 0 ? $", {failed.Count} failed" : ""),
                "# mean is the frame's average brightness 0-1; hash identifies the pixels exactly.",
                "# compare with:  vitomir --sweep <new folder> --compare " + directory,
                "level,batches,triangles,missing,mean,hash",
            }.Concat(rows.Select(r => r.ToCsv())));
        SDL_Quit();

        Console.WriteLine($"{rows.Count} levels in {(DateTime.UtcNow - started).TotalSeconds:F0} s" +
                          (failed.Count > 0 ? $", {failed.Count} failed" : ", none failed"));
        foreach (var f in failed) Console.WriteLine("  " + f);
        int missing = rows.Sum(r => r.Missing);
        if (missing > 0) Console.WriteLine($"  {missing} missing textures across {rows.Count(r => r.Missing > 0)} levels");
        if (compareWith is not null) Compare(rows, compareWith, directory, width, height);
        return failed.Count > 0 ? 1 : 0;
    }

    /// <summary>Reports every level whose frame moved since an earlier sweep, and by how much.</summary>
    static void Compare(List<Row> now, string earlier, string directory, int width, int height)
    {
        string csv = Path.Combine(earlier, "sweep.csv");
        if (!File.Exists(csv)) { Console.WriteLine($"no sweep to compare with at {csv}"); return; }
        var before = File.ReadAllLines(csv)
                         .Where(l => l.Length > 0 && l[0] != '#' && !l.StartsWith("level,", StringComparison.Ordinal))
                         .Select(Row.FromCsv)
                         .ToDictionary(r => r.Level, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"\ncompared with {earlier}:");
        int same = 0;
        var changed = new List<(string Level, float Diff, string Note)>();
        foreach (var row in now)
        {
            if (!before.TryGetValue(row.Level, out var was)) { changed.Add((row.Level, float.MaxValue, "new")); continue; }
            if (was.Hash == row.Hash) { same++; continue; }
            float diff = Difference(Path.Combine(earlier, row.Level + ".png"), Path.Combine(directory, row.Level + ".png"), width, height);
            var note = was.Batches != row.Batches ? $"batches {was.Batches} -> {row.Batches}"
                     : was.Missing != row.Missing ? $"missing textures {was.Missing} -> {row.Missing}"
                     : $"mean {was.Mean:F3} -> {row.Mean:F3}";
            changed.Add((row.Level, diff, note));
        }
        foreach (var (levelName, diff, note) in changed.OrderByDescending(c => c.Diff))
            Console.WriteLine($"  {levelName,-10} {(diff == float.MaxValue ? "" : $"{diff,6:F2} per pixel  ")}{note}");
        foreach (var gone in before.Keys.Where(k => now.All(r => !string.Equals(r.Level, k, StringComparison.OrdinalIgnoreCase))))
            Console.WriteLine($"  {gone,-10} gone");
        Console.WriteLine($"  {same} of {now.Count} levels identical");
    }

    /// <summary>Mean difference per pixel between two of this harness's PNGs, 0 to 255.</summary>
    static float Difference(string a, string b, int width, int height)
    {
        if (!File.Exists(a) || !File.Exists(b)) return float.MaxValue;
        var pa = Png.Read(a, out int wa, out int ha);
        var pb = Png.Read(b, out int wb, out int hb);
        if (pa is null || pb is null || wa != wb || ha != hb) return float.MaxValue;
        long sum = 0;
        for (int i = 0; i < pa.Length; i += 4)
            sum += Math.Abs(pa[i] - pb[i]) + Math.Abs(pa[i + 1] - pb[i + 1]) + Math.Abs(pa[i + 2] - pb[i + 2]);
        return (float)sum / (pa.Length / 4 * 3);
    }

    static float Mean(byte[] rgba)
    {
        long sum = 0;
        for (int i = 0; i < rgba.Length; i += 4) sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
        return (float)sum / (rgba.Length / 4 * 3) / 255f;
    }

    /// <summary>FNV-1a over the pixels: cheap, and enough to say "this frame did not change".</summary>
    static uint Hash(byte[] rgba)
    {
        uint hash = 2166136261;
        foreach (byte b in rgba) { hash ^= b; hash *= 16777619; }
        return hash;
    }
}
