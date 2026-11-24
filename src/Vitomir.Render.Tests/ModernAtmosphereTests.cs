// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The physically based atmosphere (ours; <see cref="ModernAtmosphere"/>, after Hillaire 2020) on the
/// CPU: its transmittance against the closed form for a vertical path, its tables' mappings, and the sky and
/// air it gives — blue overhead and paler towards the horizon, warm round a low sun, the distance hazed blue —
/// and the morning look's light from it. No GPU.
/// </summary>
public class ModernAtmosphereTests
{
    static readonly ModernAtmosphere Clear = new(1f, 0.3f);

    [Fact]
    public void Transmittance_straight_up_matches_the_closed_form_of_the_three_media()
    {
        // Straight up from the ground the path crosses each medium's whole column: air over 8 km and
        // aerosols over 1.2 km (exponential, to the top 100 km up), ozone's tent 15 km either side of 25 km.
        float top = ModernAtmosphere.TopKm - ModernAtmosphere.GroundKm;
        var air = ModernAtmosphere.Rayleigh * ModernAtmosphere.RayleighHeightKm * (1f - MathF.Exp(-top / ModernAtmosphere.RayleighHeightKm));
        float aerosol = (ModernAtmosphere.MieScatter + ModernAtmosphere.MieAbsorb) * ModernAtmosphere.MieHeightKm * (1f - MathF.Exp(-top / ModernAtmosphere.MieHeightKm));
        var ozone = ModernAtmosphere.Ozone * 15f;
        var depth = air + new Vector3(aerosol) + ozone;
        var expected = new Vector3(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
        var exact = Clear.TransmittanceExact(ModernAtmosphere.GroundKm, 1f, 2000);
        var table = Clear.Transmittance(ModernAtmosphere.GroundKm, 1f);
        for (int c = 0; c < 3; c++)
        {
            Assert.InRange(exact[c] / expected[c], 0.998f, 1.002f);
            Assert.InRange(table[c] / expected[c], 0.99f, 1.01f);
        }
        // Blue is lost most: about 0.76 of it comes through, 0.94 of the red (Hillaire's air).
        Assert.InRange(table.Z, 0.74f, 0.78f);
        Assert.InRange(table.X, 0.93f, 0.96f);
    }

    [Fact]
    public void The_table_reads_slanted_paths_as_the_march_does_and_its_mapping_round_trips()
    {
        foreach (var (r, mu) in new[] { (ModernAtmosphere.GroundKm + 0.1f, 0.3f), (ModernAtmosphere.GroundKm + 2f, 0.05f), (ModernAtmosphere.GroundKm + 30f, -0.1f) })
        {
            var (u, v) = ModernAtmosphere.TransUv(r, mu);
            var (r2, mu2) = ModernAtmosphere.TransRMu(u, v);
            Assert.InRange(r2, r - 0.01f, r + 0.01f);
            Assert.InRange(mu2, mu - 1e-3f, mu + 1e-3f);
            var exact = Clear.TransmittanceExact(r, mu, 1000);
            var table = Clear.Transmittance(r, mu);
            for (int c = 0; c < 3; c++) Assert.InRange(table[c] - exact[c], -0.02f, 0.02f);
        }
        float horizon = ModernAtmosphere.HorizonElevation(0.5f);
        foreach (var (phi, e) in new[] { (0.3f, 0.9f), (2.5f, 0.01f), (1.0f, -0.4f), (3.0f, horizon + 0.002f) })
        {
            var (u, v) = ModernAtmosphere.SkyUv(phi, e, horizon);
            var (p2, e2) = ModernAtmosphere.SkyDirection(u, v, horizon);
            Assert.InRange(p2, phi - 1e-3f, phi + 1e-3f);
            Assert.InRange(e2, e - 1e-3f, e + 1e-3f);
        }
    }

    [Fact]
    public void The_sky_is_blue_overhead_paler_to_the_horizon_and_warm_round_a_low_sun()
    {
        var sun = ModernMath.SunDirection(130f, 50f, 0f);
        var zenith = Clear.Sky(Vector3.UnitZ, sun, 0.1f);
        var away = new Vector3(-sun.X, -sun.Y, 0f);
        var horizon = Clear.Sky(Vector3.Normalize(away + new Vector3(0, 0, 0.05f)), sun, 0.1f);
        Assert.True(zenith.Z > zenith.Y && zenith.Y > zenith.X, $"zenith {zenith}");
        // Paler: the horizon's blue is a smaller share of its light than the zenith's.
        Assert.True(horizon.Z / (horizon.X + horizon.Y + horizon.Z) < zenith.Z / (zenith.X + zenith.Y + zenith.Z), $"horizon {horizon} zenith {zenith}");
        // A low sun: the light through the air to it is redder, and the sky beside it warmer than opposite it.
        var low = ModernMath.SunDirection(270f, 3f, 0f);
        var tLow = Clear.Transmittance(ModernAtmosphere.GroundKm + 0.01f, low.Z);
        var tHigh = Clear.Transmittance(ModernAtmosphere.GroundKm + 0.01f, sun.Z);
        Assert.True(tLow.X / tLow.Z > tHigh.X / tHigh.Z * 2f, $"low {tLow} high {tHigh}");
        var near = Clear.Sky(Vector3.Normalize(low + new Vector3(0.15f, 0, 0.05f)), low, 0.1f);
        var opposite = Clear.Sky(Vector3.Normalize(new Vector3(-low.X, -low.Y, 0.08f)), low, 0.1f);
        Assert.True(near.X / near.Z > opposite.X / opposite.Z, $"near the sun {near}, opposite {opposite}");
    }

    [Fact]
    public void The_air_hazes_the_distance_blue_and_leaves_the_near_clear()
    {
        var sun = ModernMath.SunDirection(130f, 50f, 0f);
        var hazy = ModernAtmosphere.For(5f, 0.25f);
        var (ins, tra) = hazy.AerialPerspective(sun, 0.002f);
        int aw = ModernAtmosphere.ApAz * ModernAtmosphere.ApColumns;
        // Level, a quarter turn from the sun: slice 0 (62 m) and slice 15 (16 km).
        int x = ModernAtmosphere.ApAz / 2, y = ModernAtmosphere.ApEl / 2;
        Vector3 At(Vector3[] t, int k) => t[((k / ModernAtmosphere.ApColumns) * ModernAtmosphere.ApEl + y) * aw + (k % ModernAtmosphere.ApColumns) * ModernAtmosphere.ApAz + x];
        Assert.True(At(tra, 0).X > 0.99f && At(ins, 0).Length() < 0.01f, $"near: {At(tra, 0)} {At(ins, 0)}");
        var t16 = At(tra, 15); var l16 = At(ins, 15);
        Assert.InRange(ModernAtmosphere.ApSliceKm(15), 15.9f, 16.1f);
        Assert.True(t16.Z < t16.X && t16.Z < 0.8f, $"16 km: transmittance {t16}");
        Assert.True(l16.Z > l16.X, $"16 km: the air's light {l16}");
    }

    [Fact]
    public void The_morning_look_lights_by_the_atmosphere_and_others_keep_their_skies()
    {
        var s = new ModernSettings();
        ModernLooks.Apply(s, "morning");
        Assert.True(s.Atmosphere);
        var light = ModernLight.From(s);
        Assert.True(light.Physical);
        // A warm white sun at 50°, a blue zenith, a paler horizon; the ambient sky from the same table.
        Assert.True(light.SunColor.X > light.SunColor.Z && light.SunColor.Z > 0.6f, $"sun {light.SunColor}");
        Assert.True(light.Zenith.Z > light.Zenith.X, $"zenith {light.Zenith}");
        Assert.True(light.Horizon.X / light.Horizon.Z > light.Zenith.X / light.Zenith.Z, $"horizon {light.Horizon}");
        var up = ModernAmbient.Evaluate(ModernAmbient.Sky(light, s), Vector3.UnitZ);
        Assert.InRange(up.Y / (light.SunIntensity * light.SunColor.Y * light.ToSun.Z), 0.08f, 0.4f);
        foreach (var look in new[] { "chronicle", "golden", "dusk" })
        {
            var o = new ModernSettings();
            ModernLooks.Apply(o, look);
            Assert.False(ModernLight.From(o).Physical, look);
        }
    }
}
