// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernAmbient.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The modern path's ambient light as spherical harmonics (ours).
/// Two sets of nine coefficients, already convolved with the cosine so a shader gets a surface's
/// irradiance from its normal alone (Ramamoorthi and Hanrahan, "An Efficient Representation for
/// Irradiance Environment Maps"):
/// <list type="bullet">
/// <item><b>the open sky</b>: the sky's own radiance (the same model ModernCommon.hlsli draws) above the
/// horizon, the sunlit ground below it;</item>
/// <item><b>the surroundings</b>: what a point sees in the directions ambient occlusion finds blocked —
/// walls and roofs, whose light is their albedo times the sun and sky falling on them. A face turned
/// towards the sun is lit; seen from across a street, it is what lights the shaded side.</item>
/// </list>
/// The shader blends the two by the occlusion's visibility, so an alley's shaded wall takes the warm
/// light of the sunlit wall opposite instead of going black.
/// </summary>
internal static class ModernAmbient
{
    /// <summary>The nine real spherical-harmonic basis functions at a unit direction (bands 0–2).</summary>
    public static void Basis(Vector3 d, Span<float> y)
    {
        y[0] = 0.282095f;
        y[1] = 0.488603f * d.Y; y[2] = 0.488603f * d.Z; y[3] = 0.488603f * d.X;
        y[4] = 1.092548f * d.X * d.Y; y[5] = 1.092548f * d.Y * d.Z;
        y[6] = 0.315392f * (3f * d.Z * d.Z - 1f);
        y[7] = 1.092548f * d.X * d.Z; y[8] = 0.546274f * (d.X * d.X - d.Y * d.Y);
    }

    /// <summary>The cosine lobe's convolution per band: π, 2π/3, π/4.</summary>
    static readonly float[] Band = [MathF.PI, 2f * MathF.PI / 3f, 2f * MathF.PI / 3f, 2f * MathF.PI / 3f,
                                    MathF.PI / 4f, MathF.PI / 4f, MathF.PI / 4f, MathF.PI / 4f, MathF.PI / 4f];

    /// <summary>
    /// Projects a radiance function over the sphere onto nine coefficients and convolves them with the
    /// cosine, by numerical integration over <paramref name="rings"/> × 2·<paramref name="rings"/> cells.
    /// </summary>
    public static Vector3[] Irradiance(Func<Vector3, Vector3> radiance, int rings = 24)
    {
        var sh = new Vector3[9];
        Span<float> y = stackalloc float[9];
        int segments = rings * 2;
        for (int r = 0; r < rings; r++)
        {
            float theta = (r + 0.5f) / rings * MathF.PI;
            float dOmega = MathF.Sin(theta) * (MathF.PI / rings) * (2f * MathF.PI / segments);
            for (int s = 0; s < segments; s++)
            {
                float phi = (s + 0.5f) / segments * 2f * MathF.PI;
                var d = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta));
                var l = radiance(d) * dOmega;
                Basis(d, y);
                for (int k = 0; k < 9; k++) sh[k] += l * y[k];
            }
        }
        for (int k = 0; k < 9; k++) sh[k] *= Band[k];
        return sh;
    }

    /// <summary>A surface's irradiance from convolved coefficients (what ModernCommon.hlsli's ShIrradiance does).</summary>
    public static Vector3 Evaluate(ReadOnlySpan<Vector3> sh, Vector3 n)
    {
        Span<float> y = stackalloc float[9];
        Basis(n, y);
        var e = Vector3.Zero;
        for (int k = 0; k < 9; k++) e += sh[k] * y[k];
        return Vector3.Max(e, Vector3.Zero);
    }

    /// <summary>
    /// The sky's radiance in direction <paramref name="d"/>, without the sun's disc or the clouds'
    /// pattern: the C# twin of ModernCommon.hlsli's SkyRadiance, kept in step by hand; with the
    /// cumulus as their share of the sky at their average light (the sky shader draws them themselves).
    /// </summary>
    public static Vector3 SkyRadiance(ModernLight light, ModernSettings s, Vector3 d)
    {
        Vector3 sky;
        if (light.Physical)
        {
            // The physically based sky (ours): its sky-view table at the ground, as the sky shader reads it.
            var lut = ModernAtmosphere.For(s.AtmosphereAerosols, s.GroundAlbedo).GroundSkyView(light.ToSun);
            sky = light.SunIntensity * ModernAtmosphere.ReadSkyView(lut, d, light.ToSun, 0f);
            if (s.Clouds.Cover > 0f)
                sky = Vector3.Lerp(sky, ModernClouds.AverageRadiance(light, s.Clouds), s.Clouds.Cover * Math.Clamp(d.Z * 6f, 0f, 1f));
            return sky;
        }
        if (light.Gradient)
        {
            float f = MathF.Pow(Math.Clamp((d.Z + 0.02f) / 0.57f, 0f, 1f), 0.6f);
            sky = Vector3.Lerp(light.Horizon, light.Zenith, f);
            var glowDir = GlowDirection(s);
            float g = MathF.Pow(Math.Clamp((Vector3.Dot(d, glowDir) - 0.3f) / 0.7f, 0f, 1f), 3f) * Math.Clamp(1f - d.Z / 0.3f, 0f, 1f);
            sky += s.SkyGlow * g;
        }
        else
        {
            float up = Math.Clamp(d.Z, 0f, 1f);
            sky = Vector3.Lerp(light.Horizon, light.Zenith, MathF.Pow(up, 0.45f));
            float down = Math.Clamp(-d.Z, 0f, 1f);
            sky = Vector3.Lerp(sky, light.Horizon * 0.6f + light.Ground * 0.4f, Math.Clamp(down * 4f, 0f, 1f));
        }
        float mu = Math.Clamp(Vector3.Dot(d, light.ToSun), 0f, 1f);
        float clear = 1f - s.CloudCover;
        var sun = light.SunColor * light.SunIntensity * clear;
        sky += sun * (0.012f * MathF.Pow(mu, 8f) + 0.06f * MathF.Pow(mu, 200f));
        float low = 1f - Math.Clamp(light.ToSun.Z * 4f, 0f, 1f);
        float band = MathF.Exp(-MathF.Abs(d.Z) * 6f);
        var across = Vector2.Normalize(new Vector2(d.X, d.Y) + new Vector2(1e-5f));
        var sunAcross = Vector2.Normalize(new Vector2(light.ToSun.X, light.ToSun.Y) + new Vector2(1e-5f));
        float side = Math.Clamp(Vector2.Dot(across, sunAcross) * 0.5f + 0.5f, 0f, 1f);
        sky += sun * 0.05f * low * band * MathF.Pow(side, 4f);
        // The cumulus (ModernClouds): their share of the sky, at their average light, above a few degrees
        // up — the sky shader draws them over this; the ambient light takes them as a share.
        if (s.Clouds.Cover > 0f)
            sky = Vector3.Lerp(sky, ModernClouds.AverageRadiance(light, s.Clouds), s.Clouds.Cover * Math.Clamp(d.Z * 6f, 0f, 1f));
        return sky;
    }

    /// <summary>The direction of a look's horizon glow: <see cref="ModernSettings.GlowYaw"/>, 4° up.</summary>
    public static Vector3 GlowDirection(ModernSettings s)
    {
        float a = s.GlowYaw * MathF.PI / 180f, e = 4f * MathF.PI / 180f;
        return new Vector3(MathF.Cos(e) * MathF.Cos(a), MathF.Cos(e) * MathF.Sin(a), MathF.Sin(e));
    }

    /// <summary>The open sky and ground: the sky above the horizon, the ground's radiance below it.</summary>
    public static Vector3[] Sky(ModernLight light, ModernSettings s) =>
        Irradiance(d => d.Z >= 0f ? SkyRadiance(light, s, d) : light.Ground);

    /// <summary>
    /// The surroundings seen in each direction: a surface facing back along it, of albedo
    /// <see cref="ModernSettings.BounceAlbedo"/>, lit by the sun where it faces the sun (half of it in
    /// shadow, on average) and by the open sky.
    /// </summary>
    public static Vector3[] Bounce(ModernLight light, ModernSettings s, ReadOnlySpan<Vector3> sky)
    {
        var skyCopy = sky.ToArray();
        var sun = light.SunColor * light.SunIntensity * (light.ToSun.Z > -0.02f ? 1f : 0f);
        return Irradiance(d =>
        {
            var facing = -d;
            float lit = MathF.Max(Vector3.Dot(facing, light.ToSun), 0f) * 0.5f;
            var e = sun * lit + Evaluate(skyCopy, facing) * 0.5f;
            return s.BounceAlbedo * e / MathF.PI;
        }, 12);
    }
}
