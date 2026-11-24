// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The modern path's arithmetic (<see cref="ModernMath"/>, ours), none of it needing a GPU: the tone
/// curve and sRGB encoding, the sun's place and colour, the reversed-depth projection, and the cascade
/// splits and fitting.
/// </summary>
public class ModernMathTests
{
    [Fact]
    public void The_tone_curve_keeps_black_rises_steadily_and_rolls_highlights_off_to_white()
    {
        Assert.Equal(Vector3.Zero, ModernMath.AcesFitted(Vector3.Zero));
        float last = 0;
        foreach (float x in new[] { 0.01f, 0.05f, 0.18f, 0.5f, 1f, 2f, 5f, 10f })
        {
            float y = ModernMath.AcesFitted(new Vector3(x)).X;
            Assert.True(y > last, $"rises at {x}");
            last = y;
        }
        Assert.InRange(ModernMath.AcesFitted(new Vector3(0.18f)).X, 0.10f, 0.11f);
        Assert.InRange(ModernMath.AcesFitted(new Vector3(1f)).X, 0.61f, 0.63f);
        Assert.True(ModernMath.AcesFitted(new Vector3(10f)).X < 0.98f, "a bright highlight is not yet white");
        Assert.Equal(Vector3.One, ModernMath.AcesFitted(new Vector3(1000f)));
    }

    [Fact]
    public void The_tone_curve_desaturates_a_very_bright_colour_towards_white()
    {
        var dim = ModernMath.AcesFitted(new Vector3(0.2f, 0.05f, 0.02f));
        var bright = ModernMath.AcesFitted(new Vector3(20f, 5f, 2f));
        Assert.True(bright.Z / bright.X > dim.Z / dim.X);
    }

    [Fact]
    public void Srgb_encoding_is_the_piecewise_curve_and_round_trips()
    {
        Assert.Equal(0f, ModernMath.LinearToSrgb(0f));
        Assert.Equal(1f, ModernMath.LinearToSrgb(1f), 5);
        Assert.Equal(0.0031308f * 12.92f, ModernMath.LinearToSrgb(0.0031308f), 5);
        Assert.InRange(ModernMath.LinearToSrgb(0.18f), 0.46f, 0.47f);
        foreach (float x in new[] { 0.001f, 0.05f, 0.5f, 0.9f })
            Assert.Equal(x, ModernMath.SrgbToLinear(ModernMath.LinearToSrgb(x)), 4);
    }

    [Fact]
    public void Cascade_splits_run_from_near_to_far_between_the_uniform_and_the_logarithmic_scheme()
    {
        var s = ModernMath.CascadeSplits(10, 10000, 4, 0.75f);
        Assert.Equal(5, s.Length);
        Assert.Equal(10f, s[0]);
        Assert.Equal(10000f, s[4]);
        for (int i = 1; i < 5; i++) Assert.True(s[i] > s[i - 1]);
        var log = ModernMath.CascadeSplits(10, 10000, 4, 1f);
        var uni = ModernMath.CascadeSplits(10, 10000, 4, 0f);
        for (int i = 1; i < 4; i++)
        {
            Assert.Equal(log[i] / log[i - 1], log[i + 1] / log[i], 2);        // even ratios
            Assert.Equal(uni[i] - uni[i - 1], uni[i + 1] - uni[i], 1);        // even steps
            Assert.InRange(s[i], log[i], uni[i]);
        }
    }

    [Fact]
    public void Every_corner_of_a_cascades_slice_falls_inside_its_shadow_map()
    {
        var eye = new Vector3(100, 200, 150);
        var fwd = Vector3.Normalize(new Vector3(1, 0.3f, -0.1f));
        float fov = 1.0f, aspect = 16f / 9f;
        var toSun = ModernMath.SunDirection(145, 40);
        var m = ModernMath.FitCascade(eye, fwd, Vector3.UnitZ, fov, aspect, 300, 1200, toSun, 2048,
                                      new Vector3(-3000, -3000, -300), new Vector3(3000, 3000, 1600));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitZ));
        var up = Vector3.Cross(right, fwd);
        float ty = MathF.Tan(fov / 2), tx = ty * aspect;
        foreach (float d in new[] { 300f, 1200f })
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                {
                    var p = eye + fwd * d + right * (sx * tx * d) + up * (sy * ty * d);
                    var c = Vector4.Transform(new Vector4(p, 1), m);
                    Assert.InRange(c.X / c.W, -1f, 1f);
                    Assert.InRange(c.Y / c.W, -1f, 1f);
                    Assert.InRange(c.Z / c.W, 0f, 1f);
                }
        // Nearer the sun is nearer in the map (smaller depth).
        var a = Vector4.Transform(new Vector4(eye + fwd * 600, 1), m);
        var b = Vector4.Transform(new Vector4(eye + fwd * 600 + toSun * 200, 1), m);
        Assert.True(b.Z < a.Z);
    }

    [Fact]
    public void A_cascade_moves_in_whole_texels_so_shadow_edges_hold_still()
    {
        var toSun = ModernMath.SunDirection(145, 40);
        Matrix4x4 At(float x) => ModernMath.FitCascade(new Vector3(x, 0, 150), Vector3.UnitX, Vector3.UnitZ, 1f, 1.5f, 10, 800, toSun, 1024,
                                                       new Vector3(-5000), new Vector3(5000));
        // A fixed world point's position in the map changes by whole texels (2 / resolution in clip space) as the camera slides.
        var p = new Vector4(400, 50, 0, 1);
        var a = Vector4.Transform(p, At(0));
        var b = Vector4.Transform(p, At(3.7f));
        float texel = 2f / 1024;
        float dx = (b.X - a.X) / texel, dy = (b.Y - a.Y) / texel;
        Assert.Equal(MathF.Round(dx), dx, 2);
        Assert.Equal(MathF.Round(dy), dy, 2);
    }

    [Fact]
    public void Reversed_infinite_depth_is_one_at_the_near_plane_and_falls_towards_zero_with_no_far_plane()
    {
        var p = ModernMath.ReversedInfinitePerspective(1.2f, 16f / 9f, 10f);
        float Depth(float distance) { var c = Vector4.Transform(new Vector4(0, 0, -distance, 1), p); return c.Z / c.W; }
        Assert.Equal(1f, Depth(10f), 5);
        Assert.Equal(0.1f, Depth(100f), 5);
        Assert.True(Depth(1e7f) > 0f);
        Assert.True(Depth(1e7f) < 1e-5f);
    }

    [Fact]
    public void The_sun_follows_the_compass_with_north_along_the_old_towns_plus_x()
    {
        Assert.True(Vector3.Distance(Vector3.UnitX, ModernMath.SunDirection(0, 0)) < 1e-5f);        // north
        Assert.True(Vector3.Distance(-Vector3.UnitY, ModernMath.SunDirection(90, 0)) < 1e-5f);      // east
        Assert.True(Vector3.Distance(-Vector3.UnitX, ModernMath.SunDirection(180, 0)) < 1e-5f);     // south: the clock's face looks this way
        Assert.True(Vector3.Distance(Vector3.UnitZ, ModernMath.SunDirection(33, 90)) < 1e-5f);
        Assert.True(Vector3.Distance(Vector3.UnitY, ModernMath.SunDirection(0, 0, northYaw: 90)) < 1e-5f);
    }

    [Fact]
    public void Over_prague_the_equinox_sun_rises_in_the_east_and_stands_south_at_noon()
    {
        var (az, el) = ModernMath.SunAt(12f, dayOfYear: 81);
        Assert.Equal(180f, az, 1);
        Assert.Equal(90f - ModernMath.PragueLatitude, el, 1);
        (az, el) = ModernMath.SunAt(6f, dayOfYear: 81);
        Assert.Equal(90f, az, 1);
        Assert.Equal(0f, el, 1);
        (_, el) = ModernMath.SunAt(12f, dayOfYear: 172);
        Assert.Equal(90f - ModernMath.PragueLatitude + 23.44f, el, 0);
        (az, el) = ModernMath.SunAt(10.5f);
        Assert.InRange(az, 120f, 160f);
        Assert.InRange(el, 35f, 55f);
    }

    [Fact]
    public void Sunlight_reddens_towards_the_horizon_and_is_gone_below_it()
    {
        var noon = ModernMath.SunTransmittance(60);
        var low = ModernMath.SunTransmittance(5);
        Assert.True(noon.Z > 0.8f && noon.X > noon.Z);
        Assert.True(low.Z / low.X < noon.Z / noon.X);
        Assert.True(low.X < noon.X);
        Assert.Equal(Vector3.Zero, ModernMath.SunTransmittance(-5));
    }

    [Fact]
    public void The_sky_is_bluer_overhead_than_at_the_horizon_and_dark_at_night()
    {
        var day = ModernLight.From(new ModernSettings { SunElevation = 45 });
        Assert.True(day.Zenith.Z / day.Zenith.X > day.Horizon.Z / day.Horizon.X);
        var night = ModernLight.From(new ModernSettings { SunElevation = -12 });
        Assert.True(night.Zenith.Z < day.Zenith.Z * 0.05f);
        Assert.Equal(Vector3.Zero, night.SunColor);
    }

    [Fact]
    public void Culling_keeps_what_is_in_front_however_far_and_drops_what_is_behind_or_beside()
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);
        var planes = ModernMath.FrustumPlanes(view * ModernMath.ReversedInfinitePerspective(1.2f, 16f / 9f, 10f));
        Assert.True(ModernMath.BoxVisible(planes, new Vector3(100, -10, -10), new Vector3(120, 10, 10)));
        Assert.True(ModernMath.BoxVisible(planes, new Vector3(1e6f, -10, -10), new Vector3(1e6f + 20, 10, 10)));
        Assert.True(ModernMath.BoxVisible(planes, new Vector3(-50, -50, -50), new Vector3(50, 50, 50)));   // round the eye
        Assert.False(ModernMath.BoxVisible(planes, new Vector3(-120, -10, -10), new Vector3(-100, 10, 10)));
        Assert.False(ModernMath.BoxVisible(planes, new Vector3(100, 500, -10), new Vector3(120, 520, 10)));
    }
}
