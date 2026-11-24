// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernProbes.cs at b4185ef, reworked for the platform.
using System.Numerics;
using SDL;
using Vitomir.Formats.L2;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: a level's light probes at run time** (baked by
/// <see cref="ModernProbeBake"/>, kept as <see cref="L2ProbeGrid"/>). Whenever the sky changes (a look, the
/// time of day, the clouds' cover) each probe's light is worked out again on the CPU from what it baked:
/// <list type="bullet">
/// <item><b>the sky it sees</b>: its sky visibility times the sky's radiance (both as nine spherical
/// harmonics; the product by the harmonics' triple-product coefficients, worked out once by quadrature), so
/// the shade under a blue sky is blue-grey and under a grey one grey;</item>
/// <item><b>the sun's bounce</b>: what it baked for a sun of 1, times the sun's colour and strength;</item>
/// <item><b>the sky's bounce</b>: what it baked per unit of the sky's average radiance, times the sky's;</item>
/// </list>
/// then convolved with the cosine (band 0 times π, band 1 times 2π/3) into irradiance and kept to bands 0 and
/// 1: four RGB coefficients, each with the visibility's own coefficient in its alpha (for the reflections'
/// occlusion). They go to the GPU as one 3D texture of half floats, the four coefficients as four slabs one
/// above the other (<c>Nx × Ny × 4·Nz</c>), which the surfaces' shader (ModernProbes.hlsli) reads with the
/// hardware's trilinear filter.
///
/// <b>Another sun.</b> The bounce was baked for one sun (the morning look's). Within 10° of it the bounce is
/// used as baked; from there to 25° away it fades out, and in its place the directions the probe sees
/// blocked take the old model's light of the surroundings (<see cref="ModernAmbient.Bounce"/>: a wall of the
/// default albedo lit by this sun and sky) — so another look or <c>--time-of-day</c> keeps a bounce, only a
/// rougher one, while the sky's part follows the new sky exactly.
/// </summary>
internal sealed unsafe class ModernProbes : IDisposable
{
    public L2ProbeGrid Grid { get; }
    SDL_GPUTexture* texture;
    Gpu? gpu;
    Vector4[]? key;
    /// <summary>How many times the probes' light was worked out (for tests and the log).</summary>
    public int Updates { get; private set; }
    /// <summary>The last light worked out: per probe, four float4s (the irradiance coefficients' RGB and the visibility's).</summary>
    public Vector4[]? Light { get; private set; }

    public ModernProbes(L2ProbeGrid grid) => Grid = grid;

    /// <summary>The level's rooms (ours; <see cref="ModernProbeRoom"/>): inside one, the probes are read within its own air.</summary>
    public IReadOnlyList<ModernProbeRoom> Rooms { get; set; } = [];

    /// <summary>The GPU texture (null until <see cref="Update"/> made it).</summary>
    public SDL_GPUTexture* Texture => texture;

    /// <summary>Within this angle of the baked sun the baked bounce is used as it is; it is gone this far away (degrees).</summary>
    public const float SunFullDegrees = 10f, SunGoneDegrees = 25f;

    /// <summary>How much of the baked sun's bounce a sun towards <paramref name="toSun"/> keeps: 1 within <see cref="SunFullDegrees"/>, 0 beyond <see cref="SunGoneDegrees"/>.</summary>
    public float SunShare(Vector3 toSun)
    {
        float a = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(toSun), Grid.ToSun), -1f, 1f)) * 180f / MathF.PI;
        return Math.Clamp((SunGoneDegrees - a) / (SunGoneDegrees - SunFullDegrees), 0f, 1f);
    }

    // ----- the spherical harmonics' triple product -----

    static float[]? gaunt;

    /// <summary>∫ Y_i Y_j Y_k over the sphere for the nine bands-0–2 functions (i, j over nine, k over the first four), by quadrature.</summary>
    static float[] Gaunt()
    {
        if (gaunt is { } g) return g;
        var t = new double[9 * 9 * 4];
        int rings = 96, segments = 192;
        Span<float> y = stackalloc float[9];
        for (int r = 0; r < rings; r++)
        {
            double theta = (r + 0.5) / rings * Math.PI;
            double dOmega = Math.Sin(theta) * (Math.PI / rings) * (2 * Math.PI / segments);
            for (int s = 0; s < segments; s++)
            {
                double phi = (s + 0.5) / segments * 2 * Math.PI;
                var d = new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)(Math.Sin(theta) * Math.Sin(phi)), (float)Math.Cos(theta));
                ModernAmbient.Basis(d, y);
                for (int i = 0; i < 9; i++)
                    for (int j = 0; j < 9; j++)
                        for (int k = 0; k < 4; k++) t[(i * 9 + j) * 4 + k] += y[i] * y[j] * y[k] * dOmega;
            }
        }
        return gaunt = [.. t.Select(v => Math.Abs(v) < 1e-6 ? 0f : (float)v)];
    }

    /// <summary>The 9 × 4 matrix that takes a visibility's nine coefficients to the first four of its product with <paramref name="radiance"/> (raw, not convolved).</summary>
    static Vector3[] ProductMatrix(ReadOnlySpan<Vector3> radiance)
    {
        var g = Gaunt();
        var m = new Vector3[9 * 4];
        for (int i = 0; i < 9; i++)
            for (int k = 0; k < 4; k++)
            {
                var sum = Vector3.Zero;
                for (int j = 0; j < 9; j++) sum += radiance[j] * g[(i * 9 + j) * 4 + k];
                m[i * 4 + k] = sum;
            }
        return m;
    }

    /// <summary>The raw (not convolved) coefficients of a convolved set: each band divided by its cosine factor.</summary>
    public static Vector3[] Raw(ReadOnlySpan<Vector3> convolved)
    {
        var r = new Vector3[9];
        for (int k = 0; k < 9; k++) r[k] = convolved[k] / (k == 0 ? MathF.PI : k < 4 ? 2f * MathF.PI / 3f : MathF.PI / 4f);
        return r;
    }

    /// <summary>
    /// Works every probe's light out for a sky of convolved coefficients <paramref name="sky"/> (the open sky and
    /// the ground below the horizon, <see cref="ModernAmbient.Sky"/>), the old model's surroundings
    /// <paramref name="bounce"/> (<see cref="ModernAmbient.Bounce"/>), a sun of <paramref name="sunLight"/>
    /// (colour times strength) towards <paramref name="toSun"/>, and a sky of average radiance
    /// <paramref name="skyAverage"/>; <paramref name="bounceScale"/> scales both bounces.
    /// </summary>
    public Vector4[] Compute(ReadOnlySpan<Vector3> sky, ReadOnlySpan<Vector3> bounce, Vector3 sunLight, Vector3 toSun, Vector3 skyAverage, float bounceScale)
    {
        var g = Grid;
        int n = g.Count;
        var envM = ProductMatrix(Raw(sky));
        var oldM = ProductMatrix(Raw(bounce));
        float share = toSun.Z > -0.02f ? SunShare(toSun) : 0f;
        var sunK = sunLight * share * bounceScale;
        var skyK = skyAverage * share * bounceScale;
        float oldK = (1f - share) * bounceScale;
        float one = 2f * MathF.Sqrt(MathF.PI);   // the constant 1's first coefficient
        var band = new[] { MathF.PI, 2f * MathF.PI / 3f, 2f * MathF.PI / 3f, 2f * MathF.PI / 3f };
        var outp = new Vector4[n * 4];
        Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 2 }, p =>
        {
            for (int k = 0; k < 4; k++)
            {
                var l = Vector3.Zero;
                for (int i = 0; i < 9; i++)
                {
                    float v = g.Visibility[p * 9 + i];
                    l += envM[i * 4 + k] * v;
                    if (oldK > 0f) l += oldM[i * 4 + k] * ((i == 0 ? one : 0f) - v) * oldK;
                }
                int o = p * 12 + k * 3;
                l += new Vector3(g.SunBounce[o], g.SunBounce[o + 1], g.SunBounce[o + 2]) * sunK;
                l += new Vector3(g.SkyBounce[o], g.SkyBounce[o + 1], g.SkyBounce[o + 2]) * skyK;
                outp[p * 4 + k] = new Vector4(l * band[k], g.Visibility[p * 9 + k]);
            }
        });
        return outp;
    }

    /// <summary>A probe's irradiance on a surface facing <paramref name="normal"/>, from four of <see cref="Compute"/>'s float4s.</summary>
    public static Vector3 Evaluate(ReadOnlySpan<Vector4> c, Vector3 normal)
    {
        const float y0 = 0.282095f, y1 = 0.488603f;
        var e = new Vector3(c[0].X, c[0].Y, c[0].Z) * y0
              + (new Vector3(c[1].X, c[1].Y, c[1].Z) * normal.Y + new Vector3(c[2].X, c[2].Y, c[2].Z) * normal.Z + new Vector3(c[3].X, c[3].Y, c[3].Z) * normal.X) * y1;
        return Vector3.Max(e, Vector3.Zero);
    }

    /// <summary>The irradiance at a point (metres) facing <paramref name="normal"/>, trilinearly between the last <see cref="Compute"/>'s probes, as the shader reads them (the CPU's twin, for tests).</summary>
    public Vector3 IrradianceAt(Vector4[] light, Vector3 metres, Vector3 normal)
    {
        var g = Grid;
        var c = (metres - g.Origin) / g.Spacing;
        c = Vector3.Clamp(c, Vector3.Zero, new Vector3(g.Nx - 1, g.Ny - 1, g.Nz - 1));
        int x0 = Math.Min((int)c.X, g.Nx - 2), y0 = Math.Min((int)c.Y, g.Ny - 2), z0 = Math.Min((int)c.Z, g.Nz - 2);
        var f = c - new Vector3(x0, y0, z0);
        Span<Vector4> sum = stackalloc Vector4[4];
        for (int corner = 0; corner < 8; corner++)
        {
            int dx = corner & 1, dy = (corner >> 1) & 1, dz = corner >> 2;
            float w = (dx == 1 ? f.X : 1 - f.X) * (dy == 1 ? f.Y : 1 - f.Y) * (dz == 1 ? f.Z : 1 - f.Z);
            int p = g.Index(x0 + dx, y0 + dy, z0 + dz);
            for (int k = 0; k < 4; k++) sum[k] += light[p * 4 + k] * w;
        }
        return Evaluate(sum, normal);
    }

    /// <summary>Works the light out again and queues its upload on <paramref name="cmd"/> when the inputs changed since the last time.</summary>
    public void Update(Gpu gpu, SDL_GPUCommandBuffer* cmd, Vector3[] sky, Vector3[] bounce, Vector3 sunLight, Vector3 toSun, Vector3 skyAverage, float bounceScale)
    {
        var k = new Vector4[20];
        for (int i = 0; i < 9; i++) { k[i] = new Vector4(sky[i], 0); k[9 + i] = new Vector4(bounce[i], 0); }
        k[18] = new Vector4(sunLight, bounceScale); k[19] = new Vector4(toSun, skyAverage.X + skyAverage.Y * 3.1f + skyAverage.Z * 7.3f);
        if (texture != null && key is { } old && Same(old, k)) return;
        key = k;
        Updates++;
        var light = Compute(sky, bounce, sunLight, toSun, skyAverage, bounceScale);
        Light = light;
        this.gpu = gpu;
        var g = Grid;
        if (texture == null)
        {
            var info = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_3D, format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER, width = (uint)g.Nx, height = (uint)g.Ny,
                layer_count_or_depth = (uint)(4 * g.Nz), num_levels = 1, sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
            };
            texture = SDL_CreateGPUTexture(gpu.Device, &info);
            if (texture == null) throw new SdlException("SDL_CreateGPUTexture (light probes)");
        }
        Upload3D(gpu, cmd, texture, g.Nx, g.Ny, 4 * g.Nz, (at, total) =>
        {
            var map = (Half*)at;
            // Slab k holds coefficient k of every probe, in the probes' own order.
            long o = 0;
            for (int s = 0; s < 4; s++)
                for (int p = 0; p < g.Count; p++)
                {
                    var v = light[p * 4 + s];
                    map[o++] = (Half)v.X; map[o++] = (Half)v.Y; map[o++] = (Half)v.Z; map[o++] = (Half)v.W;
                }
        });
    }

    static bool Same(Vector4[] a, Vector4[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            var d = Vector4.Abs(a[i] - b[i]);
            var m = Vector4.Max(Vector4.Abs(a[i]), Vector4.Abs(b[i])) * 0.002f + new Vector4(1e-5f);
            if (d.X > m.X || d.Y > m.Y || d.Z > m.Z || d.W > m.W) return false;
        }
        return true;
    }

    /// <summary>Copies half floats (RGBA) into a 3D texture of <paramref name="w"/> × <paramref name="h"/> × <paramref name="d"/>.</summary>
    internal static void Upload3D(Gpu gpu, SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* tex, int w, int h, int d, Action<nint, long> fill)
    {
        long bytes = (long)w * h * d * 8;
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)bytes };
        var transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (light probes)");
        var map = (Half*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, false);
        FillHalves(map, bytes / 2, fill);
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = 0, pixels_per_row = (uint)w, rows_per_layer = (uint)h };
        var dst = new SDL_GPUTextureRegion { texture = tex, w = (uint)w, h = (uint)h, d = (uint)d };
        SDL_UploadToGPUTexture(copy, &src, &dst, false);
        SDL_EndGPUCopyPass(copy);
        SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
    }

    static void FillHalves(Half* map, long count, Action<nint, long> fill) => fill((nint)map, count);

    public void Dispose()
    {
        if (texture != null && gpu is not null) SDL_ReleaseGPUTexture(gpu.Device, texture);
        texture = null;
    }
}
