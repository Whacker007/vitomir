// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// A cell level's compass in the renderer (ours; <see cref="L2North"/>,
/// <see cref="ModernSettings.LevelNorthYaw"/>): a level that names no
/// north is lit exactly as before; a level's north turns the morning's sun, the time of day's and the probes'
/// bake by the same angle and leaves the sun's height alone; a cooked level's compass reaches the renderer
/// through its manifest; the concept's looks keep their own suns; and a turned level still starts in sun
/// under the same clouds. They exist because the new Old Town is turned 68° so that its gates face their
/// names, and a sun left behind would light the wrong side of every street.
/// </summary>
public class ModernCompassTests
{
    static ModernSettings Morning(float north, int day = 243)
    {
        var s = new ModernSettings { Day = day, Hour = 10f, LevelNorthYaw = north };
        Assert.True(ModernLooks.Apply(s, "morning"));
        return s;
    }

    static string CookFlat(string root, string name, L2North? north)
    {
        const float M = L2Manifest.EngineUnitsPerMetre;
        Vector3[] v = [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)];
        var package = L2Package.PathFor(root, name);
        CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v.Select(p => p * M)], Indices = [0, 1, 2, 0, 2, 3], TriangleMaterials = [0, 0],
            Materials = ["ground"], Ground = m => m == "ground", Passable = _ => false, Date = new L2Date { Day = 243, Hour = 10f }, North = north,
        }, package);
        return package;
    }

    [Fact]
    public void A_level_that_names_no_north_is_lit_as_before()
    {
        var s = new ModernSettings { Day = 243, Hour = 10f };
        Assert.True(ModernLooks.Apply(s, "morning"));
        Assert.Equal(0f, s.LevelNorthYaw);
        Assert.Equal(0f, s.NorthYaw);
        // the cook logged its probes' sun for the Old Town: towards (−0.567, −0.495, 0.658).
        Assert.True(Vector3.Distance(new Vector3(-0.567f, -0.495f, 0.658f), s.ToSun) < 2e-3f, $"{s.ToSun}");
        Assert.True(Vector3.Distance(s.ToSun, ModernProbeBake.MorningSun(new L2Date { Day = 243, Hour = 10f })) < 1e-6f);
        // And the clouds stand where they were put before: the date's shift alone, nothing for the compass.
        var (refAz, refEl) = ModernMath.SunAt(ModernLooks.MorningHour, ModernLooks.SeasonDay);
        Assert.Equal(ModernClouds.ShiftFor(s.Clouds, ModernMath.SunDirection(refAz, refEl, 0f), s.ToSun), s.Clouds.Shift);
        Assert.Equal(Vector2.Zero, ModernClouds.ShiftForNorth(s.Clouds, 0f, 0f));
    }

    [Fact]
    public void A_levels_north_turns_the_suns_bearing_and_leaves_its_height()
    {
        var plain = Morning(0f);
        var turned = Morning(68f);
        Assert.Equal(68f, turned.NorthYaw);
        Assert.Equal((plain.SunAzimuth, plain.SunElevation), (turned.SunAzimuth, turned.SunElevation));   // the sky's sun is the same
        Assert.Equal(plain.ToSun.Z, turned.ToSun.Z, 5);
        // Seen from above the direction has turned 68° counter-clockwise.
        float Yaw(Vector3 v) => MathF.Atan2(v.Y, v.X) * 180f / MathF.PI;
        float by = ((Yaw(turned.ToSun) - Yaw(plain.ToSun)) % 360f + 360f) % 360f;
        Assert.Equal(68f, by, 2);
        // At 10:00 the sun is in the south-east: 139° from north by either compass.
        Assert.InRange(L2North.Bearing(turned.ToSun.X, turned.ToSun.Y, 68f), 138f, 140f);
        Assert.InRange(L2North.Bearing(plain.ToSun.X, plain.ToSun.Y, 0f), 138f, 140f);
        // The probes are baked for that same sun.
        Assert.True(Vector3.Distance(turned.ToSun, ModernProbeBake.MorningSun(new L2Date { Day = 243, Hour = 10f }, 68f)) < 1e-6f);
        // The University's front faces −X (the court): sunlit with north along +X, in its own shade once turned.
        Assert.True(Vector3.Dot(plain.ToSun, -Vector3.UnitX) > 0.5f);
        Assert.True(Vector3.Dot(turned.ToSun, -Vector3.UnitX) < -0.2f);
    }

    [Fact]
    public void A_cooked_levels_compass_reaches_the_renderer_and_a_level_cooked_without_one_keeps_north_along_x()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-compass-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellLooks.Look = null; CellLooks.Day = null; CellLooks.TimeOfDay = null;
            var turned = CellLevel.Load(CookFlat(root, "turned", L2North.FromLayout(JsonNode.Parse("""{ "frame": { "north": { "yaw": 68 } } }"""))));
            Assert.Equal(68f, L2North.Of(turned.Manifest));
            var s = CellWorldPass.SettingsFor(turned);
            Assert.Equal((68f, 68f), (s.NorthYaw, s.LevelNorthYaw));
            Assert.True(Vector3.Distance(Morning(68f).ToSun, s.ToSun) < 1e-6f);

            var plain = CellLevel.Load(CookFlat(root, "plain", null));
            Assert.Null(plain.Manifest.North);
            var p = CellWorldPass.SettingsFor(plain);
            Assert.Equal((0f, 0f), (p.NorthYaw, p.LevelNorthYaw));
            Assert.True(Vector3.Distance(Morning(0f).ToSun, p.ToSun) < 1e-6f);

            // --time-of-day puts the real sky's sun by the level's compass too, whatever the look;
            CellLooks.TimeOfDay = 16f;
            var afternoon = CellWorldPass.SettingsFor(turned);
            var (az, el) = ModernMath.SunAt(16f, 243);
            Assert.True(Vector3.Distance(ModernMath.SunDirection(az, el, 68f), afternoon.ToSun) < 1e-6f);
            CellLooks.Look = "golden";
            Assert.True(Vector3.Distance(ModernMath.SunDirection(az, el, 68f), CellWorldPass.SettingsFor(turned).ToSun) < 1e-6f);
            // while a concept look's own sun stays in its scene's frame, as it was matched to the still.
            CellLooks.TimeOfDay = null;
            var golden = CellWorldPass.SettingsFor(turned);
            Assert.Equal(0f, golden.NorthYaw);
            Assert.Equal(68f, golden.LevelNorthYaw);
            var unturned = new ModernSettings();
            ModernLooks.Apply(unturned, "golden");
            Assert.True(Vector3.Distance(unturned.ToSun, golden.ToSun) < 1e-6f);
        }
        finally
        {
            CellLooks.Day = null; CellLooks.TimeOfDay = null; CellLooks.Look = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_older_packages_north_in_its_extras_is_still_read_and_the_manifests_own_wins()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-compass-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellLooks.Look = null; CellLooks.Day = null; CellLooks.TimeOfDay = null;
            var package = CookFlat(root, "extras", null);
            var file = Path.Combine(package, L2Package.ManifestFile);
            var manifest = L2Package.Open(package).Manifest;
            manifest.Extras["modern"] = new JsonObject { ["northYaw"] = 30f };
            File.WriteAllText(file, manifest.Write());
            Assert.Equal(30f, CellWorldPass.SettingsFor(CellLevel.Load(package)).NorthYaw);
            manifest.North = new L2North { Yaw = 68f };
            File.WriteAllText(file, manifest.Write());
            var s = CellWorldPass.SettingsFor(CellLevel.Load(package));
            Assert.Equal((68f, 68f), (s.NorthYaw, s.LevelNorthYaw));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_turned_level_starts_in_sun_under_the_same_clouds_as_an_unturned_one()
    {
        // The clock's offset was chosen, with north along +X under May's sun, so that the Old Town starts in sun.
        // The wind's bearing and the sun's are counted from north, so a turned level would start under another
        // part of the cloud field; the look's shift takes both back out at the clock's start.
        var reference = Morning(0f, L2Date.DefaultDay);
        var turned = Morning(68f);
        var unshifted = Morning(68f);
        unshifted.Clouds.Shift = Vector2.Zero;
        float worst = 0f, apart = 0f;
        for (int x = -4; x <= 4; x++)
            for (int y = -4; y <= 4; y++)
            {
                var p = new Vector3(x, y, 0) * 100f * ModernMath.UnitsPerMetre;      // 800 m round the origin
                float m = ModernClouds.Shadow(reference.Clouds, reference.NorthYaw, p, reference.ToSun, 0f);
                worst = MathF.Max(worst, MathF.Abs(m - ModernClouds.Shadow(turned.Clouds, turned.NorthYaw, p, turned.ToSun, 0f)));
                apart = MathF.Max(apart, MathF.Abs(m - ModernClouds.Shadow(unshifted.Clouds, unshifted.NorthYaw, p, unshifted.ToSun, 0f)));
            }
        Assert.True(worst < 0.2f, $"shifted: {worst}");
        Assert.True(apart > 0.5f, $"unshifted: {apart}");
        Assert.Equal(1f, ModernClouds.Shadow(turned.Clouds, turned.NorthYaw, Vector3.Zero, turned.ToSun, 0f), 2);   // the town starts in sun
        // The wind still blows from the west by the level's compass: the field drifts along another line in the level.
        turned.Clouds.Seconds = 100f; reference.Clouds.Seconds = 100f;
        var moved = ModernClouds.Drift(turned.Clouds, turned.NorthYaw) - turned.Clouds.Shift;
        var before = ModernClouds.Drift(reference.Clouds, reference.NorthYaw) - reference.Clouds.Shift;
        Assert.True(Vector2.Distance(moved, before) > 0.1f);
    }
}
