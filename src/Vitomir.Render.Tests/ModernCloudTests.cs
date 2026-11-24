// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The morning look and its clouds on the CPU (ours; <see cref="ModernLooks"/>, <see cref="ModernClouds"/>,
/// <see cref="CellLooks"/>): the look's values and its sun at 10:00 on day 135 over Prague, the morning
/// as a cell level's default, the cover's threshold, and the clouds' shadow — the CPU twin of what
/// ModernClouds.hlsli draws — being 1 under clear sky, less under a cloud, and drifting with the wind
/// and nothing else. None of it needs a GPU.
/// </summary>
[Collection("cell looks")]
public class ModernCloudTests
{
    static ModernSettings Morning()
    {
        var s = new ModernSettings();
        Assert.True(ModernLooks.Apply(s, "morning"));
        return s;
    }

    [Fact]
    public void The_morning_sun_stands_about_fifty_degrees_up_in_the_south_east_at_ten_on_day_135()
    {
        var s = Morning();
        Assert.Equal(135, ModernLooks.SeasonDay);
        Assert.InRange(s.SunElevation, 50f, 51.2f);
        Assert.InRange(s.SunAzimuth, 125f, 140f);     // south-east, a little short of 135° at 10:00 solar time
        // North is the engine's +X, so the south-east is −X and −Y (east is yaw −90).
        var toSun = s.ToSun;
        Assert.True(toSun.X < -0.3f && toSun.Y < -0.3f && toSun.Z > 0.75f, $"{toSun}");
        Assert.Equal(0f, s.NorthYaw);
    }

    [Fact]
    public void The_morning_look_is_a_bright_partly_cloudy_late_morning_in_agx()
    {
        var s = Morning();
        Assert.Contains("morning", ModernLooks.Names);
        Assert.Null(s.SunColour);                      // the air's own reddening at 50°: a warm white
        var t = ModernMath.SunTransmittance(s.SunElevation);
        Assert.True(t.X > t.Y && t.Y > t.Z && t.Z > 0.8f, $"{t}");
        Assert.Null(s.SkyHorizon);                     // no gradient sky:
        Assert.True(s.Atmosphere);                     // the physically based sky and air, which follow the sun
        Assert.Equal(0f, s.CloudCover);                // no overcast pattern
        Assert.InRange(s.Clouds.Cover, 0.3f, 0.4f);    // cumulus over a third of the sky
        Assert.Equal(1500f, s.Clouds.BaseMetres);
        Assert.Equal(1f, s.Clouds.ShadowStrength);
        Assert.InRange(s.HazeVisibilityMetres, 50000f, 70000f);   // the town's own haze; the atmosphere carries the distance
        Assert.True(s.SunDisc > 0f && s.LensFlare);
        Assert.Equal(ModernToneCurve.AgX, s.ToneCurve);
        // A castle 2 km off (slice 5 of the aerial perspective, 2.25 km, level) through the town's haze and the
        // air keeps most of its contrast.
        float extinction = 3.912f / s.HazeVisibilityMetres;
        var (_, tra) = ModernAtmosphere.For(s.AtmosphereAerosols, s.GroundAlbedo).AerialPerspective(s.ToSun, 0.002f);
        int aw = ModernAtmosphere.ApAz * ModernAtmosphere.ApColumns;
        var air = tra[(ModernAtmosphere.ApEl / 2) * aw + 5 * ModernAtmosphere.ApAz + ModernAtmosphere.ApAz / 2];
        float kept = MathF.Exp(-extinction * 2250f) * (air.X + air.Y + air.Z) / 3f;
        Assert.InRange(kept, 0.7f, 0.85f);
    }

    [Fact]
    public void A_cell_level_is_seen_in_the_morning_unless_another_look_or_its_own_sun_is_asked_for()
    {
        try
        {
            CellLooks.Look = null;
            Assert.Equal("morning", CellLooks.LookName);
            var s = new ModernSettings { SunAzimuth = 200, SunElevation = 20 };
            CellLooks.Apply(s);
            Assert.InRange(s.SunElevation, 50f, 51.2f);
            Assert.True(s.Clouds.Cover > 0f);

            CellLooks.Look = CellLooks.LevelsOwn;
            var own = new ModernSettings { SunAzimuth = 200, SunElevation = 20 };
            CellLooks.Apply(own);
            Assert.Equal(20f, own.SunElevation);
            Assert.Equal(0f, own.Clouds.Cover);

            CellLooks.Look = "golden";
            var golden = new ModernSettings();
            CellLooks.Apply(golden);
            Assert.InRange(golden.SunElevation, 5.9f, 6.1f);

            CellLooks.Look = null;
            CellLooks.CloudCover = 0.6f; CellLooks.Wind = (90f, 3f); CellLooks.CloudSteps = 0; CellLooks.CloudTime = 5f;
            var options = new ModernSettings();
            CellLooks.Apply(options);
            Assert.Equal(0.6f, options.Clouds.Cover);
            Assert.Equal((90f, 3f), (options.Clouds.WindFrom, options.Clouds.WindSpeed));
            Assert.Equal(0, options.Clouds.Steps);
            Assert.Equal(5f, options.Clouds.ClockOffset);
        }
        finally
        {
            CellLooks.Look = null; CellLooks.CloudCover = null; CellLooks.Wind = null; CellLooks.CloudSteps = null; CellLooks.CloudTime = null;
        }
    }

    [Fact]
    public void The_cover_threshold_leaves_the_share_of_the_plan_asked_for_inside_a_clouds_base()
    {
        foreach (float cover in new[] { 0.2f, 0.35f, 0.6f })
        {
            var (threshold, headroom) = ModernClouds.ThresholdFor(cover);
            Assert.True(headroom > 0.05f);
            // A grid the threshold was not measured on.
            int inside = 0, n = 0;
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++, n++)
                    if (ModernClouds.Field(new Vector2(x * 0.5f + 0.13f, y * 0.5f + 0.29f), ModernClouds.ShapeOctaves) > threshold) inside++;
            Assert.InRange(inside / (float)n, cover - 0.04f, cover + 0.04f);
        }
        Assert.True(ModernClouds.ThresholdFor(0f).Threshold >= 1f);    // no cover, no clouds (the shaders' test)
    }

    [Fact]
    public void A_clouds_shadow_is_one_under_clear_sky_and_dark_under_a_cloud_with_a_soft_edge_between()
    {
        var s = Morning();
        var toSun = s.ToSun;
        float clear = float.NaN, under = float.NaN, edge = float.NaN;
        for (int i = 0; i < 4000 && (float.IsNaN(clear) || float.IsNaN(under) || float.IsNaN(edge)); i++)
        {
            var p = new Vector3(i * 37f * ModernMath.UnitsPerMetre, (i % 13) * 101f * ModernMath.UnitsPerMetre, 0);
            float v = ModernClouds.Shadow(s.Clouds, s.NorthYaw, p, toSun, 0f);
            if (v >= 1f) clear = v;
            else if (v < 0.05f) under = v;
            else if (v > 0.3f && v < 0.7f) edge = v;
        }
        Assert.Equal(1f, clear);
        Assert.InRange(under, 0f, 0.05f);
        Assert.InRange(edge, 0.3f, 0.7f);
        // No clouds, no shadows; and shadows at strength 0 are none.
        var none = new ModernCloudSettings();
        Assert.Equal(1f, ModernClouds.Shadow(none, 0f, Vector3.Zero, toSun, 0f));
    }

    [Fact]
    public void A_clouds_shadow_covers_a_share_of_the_ground_like_its_share_of_the_sky()
    {
        var s = Morning();
        int shaded = 0, n = 0;
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 60; x++, n++)
                if (ModernClouds.Shadow(s.Clouds, 0f, new Vector3(x * 400f, y * 400f, 0) * ModernMath.UnitsPerMetre, s.ToSun, 0f) < 0.5f) shaded++;
        Assert.InRange(shaded / (float)n, 0.2f, 0.45f);
    }

    [Fact]
    public void The_shadows_drift_downwind_and_the_same_time_gives_the_same_clouds()
    {
        var s = Morning();
        var c = s.Clouds;
        c.WindFrom = 270f; c.WindSpeed = 10f; c.ClockOffset = 0f;
        var toSun = s.ToSun;
        // A west wind blows towards the east: engine −Y (north is +X).
        var downwind = new Vector3(0, -1, 0) * ModernMath.UnitsPerMetre;
        for (int i = 0; i < 50; i++)
        {
            var p = new Vector3(i * 211f, i * 97f, 0) * ModernMath.UnitsPerMetre;
            c.Seconds = 30f;
            float before = ModernClouds.Shadow(c, 0f, p, toSun, 0f);
            Assert.Equal(before, ModernClouds.Shadow(c, 0f, p, toSun, 0f));
            c.Seconds = 50f;   // 20 s later the same shadow is 200 m downwind
            Assert.InRange(ModernClouds.Shadow(c, 0f, p + downwind * 200f, toSun, 0f), before - 0.02f, before + 0.02f);
        }
        // The drift wraps on the field's period, however long the clock runs.
        c.Seconds = 3.0e7f;
        var drift = ModernClouds.Drift(c, 0f);
        Assert.InRange(drift.X, 0f, ModernClouds.Period);
        Assert.InRange(drift.Y, 0f, ModernClouds.Period);
    }

    [Fact]
    public void The_field_is_the_shaders_integer_hash_and_noise_and_repeats_on_its_period()
    {
        Assert.Equal(ModernClouds.Hash(3, -7, 1141), ModernClouds.Hash(3, -7, 1141));
        Assert.NotEqual(ModernClouds.Hash(3, -7, 1141), ModernClouds.Hash(3, -7, 1142));
        for (int i = 0; i < 200; i++)
        {
            var q = new Vector2(i * 0.731f, i * 0.377f);
            float f = ModernClouds.Field(q);
            Assert.InRange(f, -0.5f, 1.5f);
            Assert.InRange(ModernClouds.Field(q + new Vector2(ModernClouds.Period, 0)), f - 1e-3f, f + 1e-3f);
            Assert.InRange(ModernClouds.Noise3(new Vector3(q, 0.3f), 64, 7), -1f, 1f);
        }
    }
}

/// <summary>Tests that set <see cref="CellLooks"/>' static options run one at a time.</summary>
[CollectionDefinition("cell looks", DisableParallelization = true)]
public class CellLooksCollection { }
