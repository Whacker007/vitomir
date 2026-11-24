// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// A cell level's date in the renderer (ours; <see cref="L2Date"/>, <see cref="ModernSettings.Day"/>,
/// <see cref="CellLooks.Day"/>, <see cref="ModernProbeBake.MorningSun"/>): the sun over Prague at 10:00 on the
/// last day of August — the pilot's date — checked against an independent figure; the morning look following
/// the level's date, lower than May's with longer shadows; a cooked level's date reaching the renderer's
/// settings and <c>--day</c> winning over it; and the light probes baked for the date's sun, which in late
/// August stands further from May's than the probes' bounce is kept for. None of it needs a GPU.
/// </summary>
[Collection("cell looks")]
public class ModernSeasonTests
{
    static ModernSettings Morning(int day, float? hour = null)
    {
        var s = new ModernSettings { Day = day, Hour = hour };
        Assert.True(ModernLooks.Apply(s, "morning"));
        return s;
    }

    [Fact]
    public void The_sun_at_ten_on_the_last_day_of_August_stands_about_41_degrees_up_over_Prague()
    {
        // NOAA's solar calculator (Spencer's declination, 8.96° that day) puts it at 41.9° at 10:00 local solar
        // time; the textbook declination the code uses (8.1°) at 41.2°: a degree apart, as SunAt promises.
        var (az, el) = ModernMath.SunAt(10f, 243);
        Assert.InRange(el, 40.6f, 42.4f);
        Assert.InRange(az, 135f, 142f);                                  // south-east, a little east of May's 132°
        Assert.InRange(ModernMath.SunAt(10f, 236).Elevation, 43f, 44.5f);   // 24 August, a week earlier: 43.5°
        Assert.InRange(ModernMath.SunAt(10f, 135).Elevation, 50f, 51.2f);   // mid-May, every level
    }

    [Fact]
    public void The_morning_follows_the_levels_date_lower_than_May_with_longer_shadows_and_still_warm_white()
    {
        var may = Morning(L2Date.DefaultDay);
        var august = Morning(243);
        Assert.Equal(ModernMath.SunAt(10f, 243), (august.SunAzimuth, august.SunElevation));
        static float Shadow(ModernSettings s) => 1f / MathF.Tan(s.SunElevation * MathF.PI / 180f);
        Assert.True(Shadow(august) > 1.3f * Shadow(may), $"{Shadow(august)} vs {Shadow(may)}");   // 1.14 a wall's height, May's 0.82
        var t = ModernMath.SunTransmittance(august.SunElevation);
        Assert.True(t.X > t.Y && t.Y > t.Z && t.Z > 0.75f, $"{t}");     // warmer than May's, not yet gold
        // A level that gives its hour has it; the looks matched to the concept stills keep their own sun.
        Assert.True(Morning(243, 9f).SunElevation < august.SunElevation - 5f);
        var golden = new ModernSettings { Day = 243 };
        ModernLooks.Apply(golden, "golden");
        Assert.InRange(golden.SunElevation, 5.9f, 6.1f);
    }

    [Fact]
    public void A_cooked_levels_date_reaches_the_renderer_and_day_on_the_command_line_wins_over_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-season-" + Guid.NewGuid().ToString("N"));
        try
        {
            CellLooks.Look = null;
            const float M = L2Manifest.EngineUnitsPerMetre;
            Vector3[] v = [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)];
            var package = L2Package.PathFor(root, "dated");
            CellCook.Cook(new CellCookInput
            {
                Name = "dated", Vertices = [.. v.Select(p => p * M)], Indices = [0, 1, 2, 0, 2, 3], TriangleMaterials = [0, 0],
                Materials = ["ground"], Ground = m => m == "ground", Passable = _ => false, Date = new L2Date { Day = 243, Hour = 10f },
            }, package);
            var level = CellLevel.Load(package);
            var s = CellWorldPass.SettingsFor(level);
            Assert.Equal((243, (float?)10f), (s.Day, s.Hour));
            Assert.Equal(ModernMath.SunAt(10f, 243), (s.SunAzimuth, s.SunElevation));

            CellLooks.Day = 135;                                        // --day 135
            var asked = CellWorldPass.SettingsFor(level);
            Assert.Equal(135, asked.Day);
            Assert.Equal(ModernMath.SunAt(10f, 135), (asked.SunAzimuth, asked.SunElevation));

            CellLooks.Day = null; CellLooks.TimeOfDay = 16f;            // --time-of-day 16 on the level's own day
            var afternoon = CellWorldPass.SettingsFor(level);
            Assert.Equal(ModernMath.SunAt(16f, 243), (afternoon.SunAzimuth, afternoon.SunElevation));
        }
        finally
        {
            CellLooks.Day = null; CellLooks.TimeOfDay = null; CellLooks.Look = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_clouds_shadows_fall_on_the_town_as_they_do_in_May_whatever_the_levels_date()
    {
        // The clock's offset was chosen so that the Old Town starts in sun under May's sun. August's lower sun
        // throws each cloud's shadow half a kilometre further, so the look shifts the field by the difference.
        var may = Morning(L2Date.DefaultDay);
        var august = Morning(243);
        Assert.Equal(Vector2.Zero, may.Clouds.Shift);
        float metres = august.Clouds.Shift.Length() * august.Clouds.SpacingMetres;
        Assert.InRange(metres, 400f, 700f);
        var unshifted = Morning(243);
        unshifted.Clouds.Shift = Vector2.Zero;
        float worst = 0f, apart = 0f;
        for (int x = -4; x <= 4; x++)
            for (int y = -4; y <= 4; y++)
            {
                var p = new Vector3(x, y, 0) * 100f * ModernMath.UnitsPerMetre;      // 800 m round the origin
                float m = ModernClouds.Shadow(may.Clouds, may.NorthYaw, p, may.ToSun, 0f);
                worst = MathF.Max(worst, MathF.Abs(m - ModernClouds.Shadow(august.Clouds, august.NorthYaw, p, august.ToSun, 0f)));
                apart = MathF.Max(apart, MathF.Abs(m - ModernClouds.Shadow(unshifted.Clouds, unshifted.NorthYaw, p, unshifted.ToSun, 0f)));
            }
        // The same clouds stand between each place and the sun; how dark their shadow is still follows the
        // sun's height (a lower sun crosses more of a cloud), so the shadows agree in place, not to the digit.
        Assert.True(worst < 0.2f, $"shifted: {worst}");
        Assert.True(apart > 0.5f, $"unshifted: {apart}");
        Assert.Equal(1f, ModernClouds.Shadow(august.Clouds, august.NorthYaw, Vector3.Zero, august.ToSun, 0f), 2);   // the town starts in sun
    }

    [Fact]
    public void The_cook_bakes_the_probes_for_the_sun_of_the_levels_date()
    {
        var may = ModernProbeBake.MorningSun();
        var august = ModernProbeBake.MorningSun(new L2Date { Day = 243 });
        Assert.True(Vector3.Distance(august, Morning(243).ToSun) < 1e-5f);
        Assert.True(Vector3.Distance(may, Morning(L2Date.DefaultDay).ToSun) < 1e-5f);
        // Late August's 10:00 sun is 10.7° from May's: past the 10° within which ModernProbes uses a bounce as
        // baked, so probes baked for May would already be fading towards the rougher model in August.
        float apart = MathF.Acos(Math.Clamp(Vector3.Dot(may, august), -1f, 1f)) * 180f / MathF.PI;
        Assert.InRange(apart, 10f, 11.5f);
    }
}
