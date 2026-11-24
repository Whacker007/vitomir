// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Render.Modern;

/// <summary>
/// The fair-weather cumulus of a look (ours): a layer of clouds on flat
/// bases <see cref="BaseMetres"/> above the ground, as much of the sky covered as <see cref="Cover"/>
/// asks, drifting with a wind by the game's clock. Zero cover (the default) draws none.
/// </summary>
public sealed class ModernCloudSettings
{
    /// <summary>The share of the sky the clouds' bases cover, 0 (none) to 1.</summary>
    public float Cover;
    /// <summary>The height of their flat bases over the ground, in metres (fair-weather cumulus: 1–2 km).</summary>
    public float BaseMetres = 1500f;
    /// <summary>How deep the layer is, in metres: the tallest clouds' height from base to top.</summary>
    public float DepthMetres = 900f;
    /// <summary>The wavelength of the field's broadest octave, in metres: about how far apart the clouds stand.</summary>
    public float SpacingMetres = 1600f;
    /// <summary>The compass bearing the wind blows from (meteorology's convention); 270 is a west wind.</summary>
    public float WindFrom = 270f;
    /// <summary>The wind's speed at the clouds' height, metres a second.</summary>
    public float WindSpeed = 6f;
    /// <summary>
    /// Steps of the sky's ray-march through the layer (the quality setting): more give rounder, better lit
    /// clouds; 0 draws them as one flat layer, the cheapest, for a laptop's integrated GPU.
    /// </summary>
    public int Steps = 12;
    /// <summary>
    /// The sky's clouds marched at half resolution with their steps dithered per pixel and read through a
    /// small filter (ours; <c>ModernCloudPass.hlsl</c>), which takes out the streaks fixed steps leave on
    /// clouds seen near the horizon and costs a quarter of the marching; false marches every pixel of the
    /// sky at fixed steps, as before.
    /// </summary>
    public bool HalfResolution = true;
    /// <summary>How strongly the clouds shade the sun's light on the world, 0 (no shadows) to 1.</summary>
    public float ShadowStrength = 1f;
    /// <summary>Their optical thickness: extinction per kilometre of cloud.</summary>
    public float Extinction = 24f;
    /// <summary>The sun's light in them: a multiplier standing for the many scatterings of a thick cloud.</summary>
    public float SunScale = 0.28f;
    /// <summary>The distance in kilometres over which far clouds sink into the haze at the horizon (by e).</summary>
    public float FadeKm = 45f;
    /// <summary>The clock, seconds: the same time gives the same clouds (the game's simulation time on a cell level).</summary>
    public float Seconds;
    /// <summary>
    /// Seconds added to the clock: where in their drift the clouds start. Ours: 280, so that the Old Town
    /// (round the origin) starts in sun and has its first cloud shadow some fifty seconds later, for two
    /// minutes, under the default wind (at 0 a shadow would just be leaving it).
    /// </summary>
    public float ClockOffset = 280f;
    /// <summary>
    /// A shift of the field, in lattice cells, added to the wind's drift (ours;
    /// <see cref="ModernClouds.ShiftFor"/>). The morning look sets it so that the clouds' shadows fall on the
    /// ground as they do under its reference sun (10:00 in mid-May, what <see cref="ClockOffset"/> was chosen
    /// for) whatever the level's date: a lower sun throws the same cloud's shadow further — half a kilometre
    /// further on 31 August — and the Old Town, in sun at the start, would start under a cloud.
    /// </summary>
    public Vector2 Shift;
}

/// <summary>
/// The cumulus field on the CPU (ours): the exact twin of
/// ModernClouds.hlsli's fractal gradient noise (the same integer hash, gradients and periods), the
/// threshold that covers a share of the sky, the shaders' constants, and the clouds' shadow at a point —
/// which the lens flare uses for the sun's light reaching the eye, and the tests for what the shaders do.
/// The field is a function of a lattice point: the ground plan in kilometres over
/// <see cref="ModernCloudSettings.SpacingMetres"/>, plus the wind's drift; it repeats every
/// <see cref="Period"/> lattice cells (102 km at the default spacing), so the drift wraps exactly.
/// </summary>
public static class ModernClouds
{
    /// <summary>The broadest octave's period in lattice cells; each finer octave's is twice its own.</summary>
    public const int Period = 64;
    /// <summary>
    /// The field's octaves in the sky; the clouds' shapes — where their bases stand, what the cover counts
    /// and what casts the shadows — are the first <see cref="ShapeOctaves"/>, the rest are their billows.
    /// </summary>
    public const int SkyOctaves = 5, ShapeOctaves = 2;
    const uint Seed = 1141;

    /// <summary>ModernClouds.hlsli's CloudHash: a variant of Chris Wellons's lowbias32 over a lattice cell.</summary>
    public static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8da6b343u + (uint)y * 0xd8163841u + seed * 0xcb1ab31fu;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return h;
        }
    }

    /// <summary>ModernClouds.hlsli's CloudGrad: one of the four diagonals by the hash's low bits, dotted with <paramref name="f"/>.</summary>
    static float Grad(uint h, Vector2 f) => ((h & 1u) != 0 ? -f.X : f.X) + ((h & 2u) != 0 ? -f.Y : f.Y);

    /// <summary>Gradient noise at lattice point <paramref name="p"/>, periodic every <paramref name="period"/> cells; about −0.7..0.7.</summary>
    public static float Noise(Vector2 p, int period, uint seed)
    {
        float ix = MathF.Floor(p.X), iy = MathF.Floor(p.Y);
        var f = new Vector2(p.X - ix, p.Y - iy);
        int m = period - 1;
        int x0 = (int)ix & m, y0 = (int)iy & m, x1 = (x0 + 1) & m, y1 = (y0 + 1) & m;
        var u = f * f * f * (f * (f * 6f - new Vector2(15f)) + new Vector2(10f));
        float a = Grad(Hash(x0, y0, seed), f);
        float b = Grad(Hash(x1, y0, seed), f - new Vector2(1, 0));
        float c = Grad(Hash(x0, y1, seed), f - new Vector2(0, 1));
        float d = Grad(Hash(x1, y1, seed), f - new Vector2(1, 1));
        float ab = a + (b - a) * u.X, cd = c + (d - c) * u.X;
        return (ab + (cd - ab) * u.Y) * 0.70710678f;
    }

    /// <summary>ModernClouds.hlsli's CloudCorner: Perlin's twelve cube edges from four bits of the hash, dotted with <paramref name="f"/>.</summary>
    static float Corner(int x, int y, int z, uint seed, Vector3 f)
    {
        uint h = Hash(x, y, unchecked(seed + (uint)z * 0x9e3779b9u)) & 15u;
        float u = h < 8u ? f.X : f.Y;
        float v = h < 4u ? f.Y : (h == 12u || h == 14u ? f.X : f.Z);
        return ((h & 1u) != 0 ? -u : u) + ((h & 2u) != 0 ? -v : v);
    }

    /// <summary>ModernClouds.hlsli's CloudNoise3: gradient noise in three dimensions, periodic across the plan.</summary>
    public static float Noise3(Vector3 p, int period, uint seed)
    {
        float ix = MathF.Floor(p.X), iy = MathF.Floor(p.Y), iz = MathF.Floor(p.Z);
        var f = new Vector3(p.X - ix, p.Y - iy, p.Z - iz);
        int m = period - 1;
        int x0 = (int)ix & m, y0 = (int)iy & m, x1 = (x0 + 1) & m, y1 = (y0 + 1) & m, z0 = (int)iz, z1 = z0 + 1;
        var u = f * f * f * (f * (f * 6f - new Vector3(15f)) + new Vector3(10f));
        static float L(float a, float b, float t) => a + (b - a) * t;
        float a = L(Corner(x0, y0, z0, seed, f), Corner(x1, y0, z0, seed, f - new Vector3(1, 0, 0)), u.X);
        float b = L(Corner(x0, y1, z0, seed, f - new Vector3(0, 1, 0)), Corner(x1, y1, z0, seed, f - new Vector3(1, 1, 0)), u.X);
        float c = L(Corner(x0, y0, z1, seed, f - new Vector3(0, 0, 1)), Corner(x1, y0, z1, seed, f - new Vector3(1, 0, 1)), u.X);
        float d = L(Corner(x0, y1, z1, seed, f - new Vector3(0, 1, 1)), Corner(x1, y1, z1, seed, f - new Vector3(1, 1, 1)), u.X);
        return L(L(a, b, u.Y), L(c, d, u.Y), u.Z) * 0.7f;
    }

    /// <summary>
    /// The field at a lattice point and height <paramref name="z"/> (lattice cells): 0.5 plus
    /// <paramref name="octaves"/> octaves of noise, about 0..1 — the two broadest over the plan, the finer
    /// ones billows in three dimensions (ModernClouds.hlsli's CloudField).
    /// </summary>
    public static float Field(Vector2 q, int octaves = SkyOctaves, float z = 0f)
    {
        float sum = 0f, amp = 0.9f;
        int period = Period;
        for (int k = 0; k < octaves; k++)
        {
            float n = k < 2 ? Noise(q, period, Seed + (uint)k) : Noise3(new Vector3(q, z), period, Seed + (uint)k);
            sum += (k < 2 ? n : MathF.Abs(n) * 1.6f - 0.25f) * amp;
            q *= 2f; z *= 2f; period *= 2; amp *= 0.5f;
        }
        return 0.5f + sum;
    }

    static readonly float[] Sorted = SortedSample();

    /// <summary>The clouds' shapes over one whole period, 192 × 192 points, sorted: their distribution.</summary>
    static float[] SortedSample()
    {
        const int n = 192;
        var v = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                v[y * n + x] = Field(new Vector2((x + 0.37f) * Period / n, (y + 0.61f) * Period / n), ShapeOctaves);
        Array.Sort(v);
        return v;
    }

    /// <summary>
    /// The shapes' threshold that leaves <paramref name="cover"/> of the plan inside a cloud's base, and the
    /// headroom above it to their top (the 99.5th percentile), where the tallest clouds reach the layer's
    /// top. Cover 0 gives a threshold of 2: no clouds.
    /// </summary>
    public static (float Threshold, float Headroom) ThresholdFor(float cover)
    {
        if (cover <= 0f) return (2f, 1f);
        cover = MathF.Min(cover, 1f);
        int at = Math.Clamp((int)((1f - cover) * Sorted.Length), 0, Sorted.Length - 1);
        float threshold = cover >= 1f ? Sorted[0] - 0.05f : Sorted[at];
        float top = Sorted[(int)(Sorted.Length * 0.995f)];
        return (threshold, MathF.Max(top - threshold, 0.05f));
    }

    /// <summary>The wind's drift at the settings' clock, in lattice cells, wrapped to the field's period.</summary>
    public static Vector2 Drift(ModernCloudSettings c, float northYaw)
    {
        // The wind blows towards its bearing + 180°; a bearing a is the engine yaw northYaw − a.
        float yaw = (northYaw - (c.WindFrom + 180f)) * MathF.PI / 180f;
        double cells = (double)c.WindSpeed * ((double)c.Seconds + c.ClockOffset) / MathF.Max(c.SpacingMetres, 1f);
        // The field moves with the wind, so a ground point sees the field upwind of it: subtract.
        double x = -Math.Cos(yaw) * cells + c.Shift.X, y = -Math.Sin(yaw) * cells + c.Shift.Y;
        x -= Math.Floor(x / Period) * Period; y -= Math.Floor(y / Period) * Period;
        return new Vector2((float)x, (float)y);
    }

    /// <summary>
    /// The <see cref="ModernCloudSettings.Shift"/> (lattice cells) that makes the clouds' shadows under a sun
    /// towards <paramref name="toSun"/> fall on level ground where they fall under one towards
    /// <paramref name="reference"/> (ours): a shadow is the field sampled where the ray to the sun
    /// meets the clouds' middle (<see cref="SunThrough"/>), base + depth / 2 over the ground divided by the
    /// sun's sine along its horizontal bearing, so the two suns' sample points differ by one offset everywhere.
    /// </summary>
    public static Vector2 ShiftFor(ModernCloudSettings c, Vector3 reference, Vector3 toSun)
    {
        float midKm = (c.BaseMetres + MathF.Max(c.DepthMetres, 10f) * 0.5f) / 1000f;
        static Vector2 Reach(Vector3 sun, float midKm) => new Vector2(sun.X, sun.Y) * (midKm / MathF.Max(sun.Z, 0.05f));
        return (Reach(Vector3.Normalize(reference), midKm) - Reach(Vector3.Normalize(toSun), midKm)) * (1000f / MathF.Max(c.SpacingMetres, 1f));
    }

    /// <summary>
    /// The part of the <see cref="ModernCloudSettings.Shift"/> (lattice cells) that a level's compass asks for
    /// (ours): the wind's bearing is counted from north, so with north at <paramref name="northYaw"/>
    /// the clock's offset has carried the field along another line than with north at
    /// <paramref name="referenceYaw"/>; this is the difference, which puts the field at the clock's start where
    /// it stands under the reference. Zero when the two norths are one.
    /// </summary>
    public static Vector2 ShiftForNorth(ModernCloudSettings c, float northYaw, float referenceYaw)
    {
        if (northYaw == referenceYaw) return Vector2.Zero;
        float cells = c.WindSpeed * c.ClockOffset / MathF.Max(c.SpacingMetres, 1f);
        static Vector2 Along(float north, float from) { float yaw = (north - (from + 180f)) * MathF.PI / 180f; return new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)); }
        return (Along(northYaw, c.WindFrom) - Along(referenceYaw, c.WindFrom)) * cells;
    }

    /// <summary>The three constants the shaders read (ModernCommon.hlsli's CloudShape, CloudMotion, CloudLight).</summary>
    public static (Vector4 Shape, Vector4 Motion, Vector4 Light) Uniforms(ModernCloudSettings c, float northYaw)
    {
        var (threshold, headroom) = ThresholdFor(c.Cover);
        var drift = Drift(c, northYaw);
        return (new Vector4(threshold, 1f / headroom, c.BaseMetres / 1000f, MathF.Max(c.DepthMetres, 10f) / 1000f),
                new Vector4(drift, 1000f / MathF.Max(c.SpacingMetres, 1f), Math.Clamp(c.Steps, 0, 64)),
                new Vector4(c.Extinction, c.SunScale, Math.Clamp(c.ShadowStrength, 0f, 1f), MathF.Max(c.FadeKm, 1f)));
    }

    /// <summary>
    /// The sun's light through the clouds at <paramref name="world"/> (engine units) with the sun towards
    /// <paramref name="toSun"/> over ground at height <paramref name="groundZ"/>: ModernClouds.hlsli's
    /// CloudShadow without its strength — 1 under clear sky, near 0 under a thick cloud's middle.
    /// </summary>
    public static float SunThrough(ModernCloudSettings c, float northYaw, Vector3 world, Vector3 toSun, float groundZ)
    {
        var (shape, motion, light) = Uniforms(c, northYaw);
        if (shape.X >= 1f) return 1f;
        const float kmPerUnit = 1f / ModernMath.UnitsPerMetre / 1000f;
        float sz = MathF.Max(toSun.Z, 0.05f);
        float groundKm = (world.Z - groundZ) * kmPerUnit;
        float t = (shape.Z + shape.W * 0.5f - groundKm) / sz;
        var km = new Vector2(world.X, world.Y) * kmPerUnit + new Vector2(toSun.X, toSun.Y) * t;
        var q = km * motion.Z + new Vector2(motion.X, motion.Y);
        float column = Math.Clamp((Field(q, ShapeOctaves) - shape.X) * shape.Y, 0f, 1f);
        return MathF.Exp(-light.X * column * shape.W / sz * 0.5f);
    }

    /// <summary>What the shaders' cloud shadow is at <paramref name="world"/>: <see cref="SunThrough"/> at the settings' strength.</summary>
    public static float Shadow(ModernCloudSettings c, float northYaw, Vector3 world, Vector3 toSun, float groundZ) =>
        1f + (SunThrough(c, northYaw, world, toSun, groundZ) - 1f) * Math.Clamp(c.ShadowStrength, 0f, 1f);

    /// <summary>
    /// The clouds' average radiance seen from below, for the ambient light (ModernAmbient): white where
    /// the sun lights them, grey-blue at their bases, about half and half over a sky's worth.
    /// </summary>
    public static Vector3 AverageRadiance(ModernLight light, ModernCloudSettings c)
    {
        var sunlit = light.SunColor * light.SunIntensity * c.SunScale * 0.45f;
        var sky = (light.Zenith + light.Horizon) * 0.5f;
        var under = light.Ground * 0.8f + light.Horizon * 0.25f;
        return sunlit + (sky + under) * 0.5f;
    }
}
