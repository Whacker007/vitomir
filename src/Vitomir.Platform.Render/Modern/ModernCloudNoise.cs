// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernCloudNoise.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: the clouds' shape noise**, after Schneider's Nubis
/// ("The Real-time Volumetric Cloudscapes of Horizon Zero Dawn", SIGGRAPH 2015; GPU Pro 7). Two tiling 3D
/// textures of four 8-bit channels, built once on the CPU from a fixed seed, so every run and every machine
/// has the same clouds:
/// <list type="bullet">
/// <item><b>the shape</b>, <see cref="ShapeSize"/>³: red is Perlin–Worley noise (fractal gradient noise, its
/// billows lifted by inverted cellular noise underneath, so it is round-lobed like a cumulus' cauliflower);
/// green, blue and alpha are inverted cellular (Worley) noise at three finer frequencies, the lobes' own
/// lobes;</item>
/// <item><b>the detail</b>, <see cref="DetailSize"/>³: inverted cellular noise at three frequencies, which
/// eats the shape's edges away — wisps at the base, billows at the top.</item>
/// </list>
/// Everything wraps at the textures' edges (the gradients' lattice and the cells' feature points are picked by
/// a hash of the wrapped coordinates), so they tile across the sky without a seam.
/// </summary>
internal static class ModernCloudNoise
{
    public const int ShapeSize = 64, DetailSize = 32;
    static byte[]? shape, detail;
    static readonly object gate = new();

    static Task? preparing;

    /// <summary>The shape texture, RGBA8, x fastest then y then z.</summary>
    public static byte[] Shape { get { Wait(); lock (gate) return shape ??= BuildShape(); } }
    /// <summary>The detail texture, RGBA8, as <see cref="Shape"/>.</summary>
    public static byte[] Detail { get { Wait(); lock (gate) return detail ??= BuildDetail(); } }

    /// <summary>
    /// Starts building both textures on a worker thread (a second or a few of CPU), so that a renderer made
    /// before a level loads finds them ready by its first frame; <see cref="Shape"/> and <see cref="Detail"/> wait for it.
    /// </summary>
    public static void Prepare()
    {
        lock (gate)
        {
            if (preparing is not null || (shape is not null && detail is not null)) return;
            preparing = Task.Run(() =>
            {
                var s = BuildShape(); var d = BuildDetail();
                lock (gate) { shape ??= s; detail ??= d; }
            });
        }
    }

    static void Wait()
    {
        Task? t;
        lock (gate) t = preparing;
        t?.Wait();
    }

    /// <summary>Builds the shape texture afresh (what <see cref="Shape"/> keeps).</summary>
    public static byte[] BuildShape() => Build(ShapeSize, ShapeTexel);
    /// <summary>Builds the detail texture afresh (what <see cref="Detail"/> keeps).</summary>
    public static byte[] BuildDetail() => Build(DetailSize, DetailTexel);

    static byte[] Build(int n, Func<Vector3, Vector4> texel)
    {
        var data = new byte[n * n * n * 4];
        Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 2 }, z =>
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var v = Vector4.Clamp(texel(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) / n), Vector4.Zero, Vector4.One);
                    int o = ((z * n + y) * n + x) * 4;
                    data[o] = (byte)MathF.Round(v.X * 255f); data[o + 1] = (byte)MathF.Round(v.Y * 255f);
                    data[o + 2] = (byte)MathF.Round(v.Z * 255f); data[o + 3] = (byte)MathF.Round(v.W * 255f);
                }
        });
        // Each channel stretched so that its 2nd to 98th percentiles span 0..1: the noises' raw ranges are
        // narrow (cellular noise rarely reaches 0, fractal noise rarely its ends), and the shapes are carved
        // by thresholds on them, which want the whole range (ours).
        for (int c = 0; c < 4; c++)
        {
            var hist = new int[256];
            for (int i = c; i < data.Length; i += 4) hist[data[i]]++;
            int total = data.Length / 4, lo = 0, hi = 255, acc = 0;
            for (int k = 0; k < 256; k++) { acc += hist[k]; if (acc >= total * 0.02) { lo = k; break; } }
            acc = 0;
            for (int k = 255; k >= 0; k--) { acc += hist[k]; if (acc >= total * 0.02) { hi = k; break; } }
            if (hi <= lo) continue;
            for (int i = c; i < data.Length; i += 4) data[i] = (byte)Math.Clamp((data[i] - lo) * 255 / (hi - lo), 0, 255);
        }
        return data;
    }

    static Vector4 ShapeTexel(Vector3 p)
    {
        // Perlin–Worley: three octaves of gradient noise from 4 cells, remapped over three of cellular from 4 cells.
        float perlin = 0f, amp = 1f, total = 0f;
        for (int o = 0; o < 3; o++) { perlin += Perlin(p, 4 << o, 11u + (uint)o) * amp; total += amp; amp *= 0.5f; }
        perlin = Math.Clamp(perlin / total * 0.9f + 0.5f, 0f, 1f);
        float worley = WorleyFbm(p, 4, 31u);
        float pw = Remap(perlin, 0f, 1f, worley, 1f);
        return new Vector4(pw, WorleyFbm(p, 8, 41u), WorleyFbm(p, 16, 51u), WorleyFbm(p, 32, 61u));
    }

    static Vector4 DetailTexel(Vector3 p) =>
        new(WorleyFbm(p, 4, 71u), WorleyFbm(p, 8, 81u), WorleyFbm(p, 16, 91u), 1f);

    static float Remap(float v, float lo, float hi, float a, float b) => a + (v - lo) / MathF.Max(hi - lo, 1e-6f) * (b - a);

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        return x;
    }

    static uint Hash3(int x, int y, int z, uint seed) => Hash((uint)x * 73856093u ^ Hash((uint)y * 19349663u ^ Hash((uint)z * 83492791u ^ seed)));

    static float Unit(uint h) => (h & 0xffffff) / 16777216f;

    /// <summary>Gradient noise of period <paramref name="cells"/> over the unit cube, about −1..1.</summary>
    static float Perlin(Vector3 p, int cells, uint seed)
    {
        var q = p * cells;
        int x0 = (int)MathF.Floor(q.X), y0 = (int)MathF.Floor(q.Y), z0 = (int)MathF.Floor(q.Z);
        var f = q - new Vector3(x0, y0, z0);
        var u = f * f * f * (f * (f * 6f - new Vector3(15f)) + new Vector3(10f));
        float Corner(int dx, int dy, int dz)
        {
            uint h = Hash3(Mod(x0 + dx, cells), Mod(y0 + dy, cells), Mod(z0 + dz, cells), seed);
            // One of Perlin's twelve cube edges.
            var g = (h % 12u) switch
            {
                0 => new Vector3(1, 1, 0), 1 => new Vector3(-1, 1, 0), 2 => new Vector3(1, -1, 0), 3 => new Vector3(-1, -1, 0),
                4 => new Vector3(1, 0, 1), 5 => new Vector3(-1, 0, 1), 6 => new Vector3(1, 0, -1), 7 => new Vector3(-1, 0, -1),
                8 => new Vector3(0, 1, 1), 9 => new Vector3(0, -1, 1), 10 => new Vector3(0, 1, -1), _ => new Vector3(0, -1, -1),
            };
            return Vector3.Dot(g, f - new Vector3(dx, dy, dz));
        }
        float x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), u.X), x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), u.X);
        float x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), u.X), x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), u.X);
        return Lerp(Lerp(x00, x10, u.Y), Lerp(x01, x11, u.Y), u.Z);
    }

    /// <summary>Inverted cellular noise of <paramref name="cells"/> cells a side: 1 at a cell's feature point, falling to 0 a cell's width away.</summary>
    static float Worley(Vector3 p, int cells, uint seed)
    {
        var q = p * cells;
        int x0 = (int)MathF.Floor(q.X), y0 = (int)MathF.Floor(q.Y), z0 = (int)MathF.Floor(q.Z);
        float best = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = x0 + dx, cy = y0 + dy, cz = z0 + dz;
                    uint h = Hash3(Mod(cx, cells), Mod(cy, cells), Mod(cz, cells), seed);
                    var feature = new Vector3(cx + Unit(h), cy + Unit(Hash(h + 1u)), cz + Unit(Hash(h + 2u)));
                    best = MathF.Min(best, Vector3.DistanceSquared(feature, q));
                }
        return 1f - Math.Clamp(MathF.Sqrt(best), 0f, 1f);
    }

    /// <summary>Three octaves of <see cref="Worley"/>, 0.625, 0.25 and 0.125 of it.</summary>
    static float WorleyFbm(Vector3 p, int cells, uint seed) =>
        Worley(p, cells, seed) * 0.625f + Worley(p, cells * 2, seed + 1u) * 0.25f + Worley(p, cells * 4, seed + 2u) * 0.125f;

    static int Mod(int a, int n) => ((a % n) + n) % n;
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>A texture's channels at a point (texture coordinates, wrapping), trilinearly, as the GPU reads them (the CPU's twin, for tests).</summary>
    public static Vector4 Sample(byte[] tex, int n, Vector3 uvw)
    {
        var q = uvw * n - new Vector3(0.5f);
        int x0 = (int)MathF.Floor(q.X), y0 = (int)MathF.Floor(q.Y), z0 = (int)MathF.Floor(q.Z);
        var f = q - new Vector3(x0, y0, z0);
        var sum = Vector4.Zero;
        for (int c = 0; c < 8; c++)
        {
            int dx = c & 1, dy = (c >> 1) & 1, dz = c >> 2;
            float w = (dx == 1 ? f.X : 1 - f.X) * (dy == 1 ? f.Y : 1 - f.Y) * (dz == 1 ? f.Z : 1 - f.Z);
            int o = ((Mod(z0 + dz, n) * n + Mod(y0 + dy, n)) * n + Mod(x0 + dx, n)) * 4;
            sum += new Vector4(tex[o], tex[o + 1], tex[o + 2], tex[o + 3]) / 255f * w;
        }
        return sum;
    }
}
