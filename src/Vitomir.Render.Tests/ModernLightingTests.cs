// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The arithmetic of the modern path's lighting and screen effects (ours), none of it needing a GPU: the
/// ambient light's spherical harmonics, ambient occlusion's arc integral, the local lights' attenuation
/// and cone and their sorting into clusters, the haze's phase function, the bloom's threshold, AgX, the
/// lens flare's ghosts, and the looks and scene dressing.
/// </summary>
public class ModernLightingTests
{
    // ----- ambient light -----

    [Fact]
    public void A_sky_of_even_radiance_gives_pi_times_it_on_a_surface_facing_any_way()
    {
        var sh = ModernAmbient.Irradiance(_ => new Vector3(1f, 0.5f, 0.25f));
        foreach (var n in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, Vector3.Normalize(new Vector3(1, -2, 0.5f)) })
        {
            var e = ModernAmbient.Evaluate(sh, n);
            Assert.InRange(e.X, MathF.PI * 0.99f, MathF.PI * 1.01f);
            Assert.InRange(e.Z, MathF.PI * 0.25f * 0.99f, MathF.PI * 0.25f * 1.01f);
        }
    }

    [Fact]
    public void Radiance_rising_with_height_lights_a_surface_by_two_thirds_pi_times_its_upward_tilt()
    {
        // L(ω) = 1 + ω.z: the first two bands carry it exactly, so E(n) = π + (2π/3) n.z.
        var sh = ModernAmbient.Irradiance(d => new Vector3(1f + d.Z));
        foreach (float z in new[] { -1f, -0.5f, 0f, 0.7f, 1f })
        {
            var n = Vector3.Normalize(new Vector3(MathF.Sqrt(1 - z * z), 0, z));
            Assert.InRange(ModernAmbient.Evaluate(sh, n).X, MathF.PI + 2f * MathF.PI / 3f * z - 0.03f, MathF.PI + 2f * MathF.PI / 3f * z + 0.03f);
        }
    }

    [Fact]
    public void Under_a_clear_sky_a_floor_takes_more_sky_light_than_a_wall_and_the_ground_bounces_warm()
    {
        var s = new ModernSettings { SunElevation = 45 };
        var light = ModernLight.From(s);
        var sky = ModernAmbient.Sky(light, s);
        var floor = ModernAmbient.Evaluate(sky, Vector3.UnitZ);
        var wall = ModernAmbient.Evaluate(sky, Vector3.UnitY);
        Assert.True(floor.Z > wall.Z, $"floor {floor} wall {wall}");
        var ceiling = ModernAmbient.Evaluate(sky, -Vector3.UnitZ);
        Assert.True(ceiling.X > ceiling.Z * 0.9f, $"the ground's light is warm: {ceiling}");
        // A clear sky's diffuse light on the ground is a modest share of the sun's there.
        float sunOnGround = s.SunIntensity * MathF.Sin(45f * MathF.PI / 180f);
        Assert.InRange(floor.Y / sunOnGround, 0.08f, 0.35f);
    }

    [Fact]
    public void The_surroundings_facing_the_sun_light_a_wall_turned_away_from_it_more_than_one_turned_towards_it()
    {
        var s = new ModernSettings { SunAzimuth = 180, SunElevation = 30 };
        var light = ModernLight.From(s);
        var sky = ModernAmbient.Sky(light, s);
        var bounce = ModernAmbient.Bounce(light, s, sky);
        var sunward = Vector3.Normalize(new Vector3(light.ToSun.X, light.ToSun.Y, 0));
        // A wall facing away from the sun looks at walls that face it: those are sunlit.
        Assert.True(ModernAmbient.Evaluate(bounce, -sunward).X > ModernAmbient.Evaluate(bounce, sunward).X * 1.5f);
    }

    // ----- ambient occlusion -----

    [Fact]
    public void An_open_hemisphere_sees_everything_whichever_way_the_surface_leans()
    {
        // As the shader walks it: slices round the view direction, the normal projected into each; the
        // projected length times the slice's arc, averaged over the slices, is 1 with nothing in the way.
        var v = new Vector3(0, 0, -1);
        foreach (float tilt in new[] { 0f, 0.4f, 0.9f, 1.3f })
        {
            var n = new Vector3(MathF.Sin(tilt), 0, -MathF.Cos(tilt));
            double sum = 0; int k = 360;
            for (int i = 0; i < k; i++)
            {
                float phi = (i + 0.5f) * MathF.PI / k;
                var dir = new Vector3(MathF.Cos(phi), MathF.Sin(phi), 0);
                var ortho = dir - Vector3.Dot(dir, v) * v;
                var axis = Vector3.Normalize(Vector3.Cross(ortho, v));
                var projected = n - axis * Vector3.Dot(n, axis);
                float length = projected.Length();
                float angle = MathF.Sign(Vector3.Dot(ortho, projected)) * MathF.Acos(Math.Clamp(Vector3.Dot(projected, v) / length, -1f, 1f));
                sum += length * ModernMath.GtaoSlice(angle, angle - MathF.PI / 2f, angle + MathF.PI / 2f);
            }
            Assert.InRange(sum / k, 0.995, 1.005);
        }
    }

    [Fact]
    public void Horizons_rising_on_either_side_close_the_slice_steadily_and_a_closed_slice_sees_nothing()
    {
        float last = 1.01f;
        foreach (float h in new[] { 0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f })
        {
            // A floor seen straight on (n = 0), walls rising to elevation h on both sides.
            float v = ModernMath.GtaoSlice(0f, -(MathF.PI / 2f - h), MathF.PI / 2f - h);
            Assert.True(v < last, $"falls at {h}");
            last = v;
        }
        Assert.InRange(ModernMath.GtaoSlice(0f, 0f, 0f), 0f, 1e-5f);
        // Walls at 45° on both sides of a floor: the cosine-weighted share of the arc above them, 1 − sin²(45°)... = 0.5.
        Assert.InRange(ModernMath.GtaoSlice(0f, -MathF.PI / 4f, MathF.PI / 4f), 0.49f, 0.51f);
    }

    // ----- local lights -----

    [Fact]
    public void A_light_falls_off_as_the_inverse_square_in_metres_and_fades_to_nothing_at_its_range()
    {
        float m = ModernMath.UnitsPerMetre, range = 40 * m;
        Assert.InRange(ModernLights.Attenuation(2 * m, range), 0.249f, 0.251f);
        Assert.InRange(ModernLights.Attenuation(4 * m, range) / ModernLights.Attenuation(2 * m, range), 0.249f, 0.251f);
        Assert.Equal(0f, ModernLights.Attenuation(range, range));
        Assert.Equal(0f, ModernLights.Attenuation(range * 1.5f, range));
        float last = float.MaxValue;
        for (float d = 0.5f * m; d < range; d += m)
        {
            float a = ModernLights.Attenuation(d, range);
            Assert.True(a < last, $"falls at {d / m} m");
            last = a;
        }
        // At the bulb it is floored, not infinite.
        Assert.Equal(100f, ModernLights.Attenuation(0f, range));
    }

    [Fact]
    public void A_lights_reach_is_where_it_falls_to_the_cutoff()
    {
        float reach = ModernLights.ReachFor(4f, 0.01f);
        Assert.InRange(reach / ModernMath.UnitsPerMetre, 19.99f, 20.01f);
    }

    [Fact]
    public void A_spot_is_full_inside_its_inner_cone_dark_outside_its_outer_and_smooth_between()
    {
        float inner = MathF.Cos(20f * MathF.PI / 180f), outer = MathF.Cos(30f * MathF.PI / 180f);
        Assert.Equal(1f, ModernLights.Cone(MathF.Cos(10f * MathF.PI / 180f), inner, outer));
        Assert.Equal(0f, ModernLights.Cone(MathF.Cos(35f * MathF.PI / 180f), inner, outer));
        float mid = ModernLights.Cone(MathF.Cos(25f * MathF.PI / 180f), inner, outer);
        Assert.InRange(mid, 0.1f, 0.5f);
    }

    [Fact]
    public void Glb_lights_come_in_as_blender_watts_and_directional_ones_are_left_to_the_sun()
    {
        var g = new GltfScene();
        g.Lights.Add(new GltfLight("torch", GltfLightType.Point, new Vector3(72, 0, 144), -Vector3.UnitZ, new Vector3(1, 0.5f, 0.2f), 683f * 2f, 0f, 0, 0));
        g.Lights.Add(new GltfLight("sun", GltfLightType.Directional, Vector3.Zero, -Vector3.UnitZ, Vector3.One, 1000f, 0f, 0, 0));
        g.Lights.Add(new GltfLight("lamp", GltfLightType.Spot, Vector3.Zero, -Vector3.UnitZ, Vector3.One, 683f, 5f * 72f, 0.2f, 0.5f));
        var lights = ModernLights.FromGltf(g, 0.02f);
        Assert.Equal(2, lights.Count);
        Assert.Equal(new Vector3(2f, 1f, 0.4f), lights[0].Colour);
        Assert.InRange(lights[0].Range / 72f, 9.99f, 10.01f);   // √(2 / 0.02) = 10 m
        Assert.True(lights[1].Spot);
        Assert.Equal(5f * 72f, lights[1].Range);
    }

    [Fact]
    public void Khr_lights_punctual_lights_are_placed_by_their_nodes_in_engine_units_and_axes()
    {
        // A spot 2 m up (glTF's Y) and 3 m along glTF's −Z (the engine's +Y), turned to shine straight down;
        // a point light with a range; both under a parent node moved 1 m along X.
        string json = """
            { "asset": { "version": "2.0" },
              "extensions": { "KHR_lights_punctual": { "lights": [
                  { "type": "spot", "color": [1, 0.5, 0.25], "intensity": 683, "spot": { "innerConeAngle": 0.2, "outerConeAngle": 0.6 } },
                  { "type": "point", "intensity": 10, "range": 4 } ] } },
              "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "translation": [1, 0, 0], "children": [1, 2] },
                         { "translation": [0, 2, -3], "rotation": [-0.7071068, 0, 0, 0.7071068], "extensions": { "KHR_lights_punctual": { "light": 0 } } },
                         { "extensions": { "KHR_lights_punctual": { "light": 1 } } } ] }
            """;
        var g = GltfReader.Read(System.Text.Encoding.UTF8.GetBytes(json), Path.GetTempPath());
        Assert.Equal(2, g.Lights.Count);
        var spot = g.Lights[0];
        Assert.Equal(GltfLightType.Spot, spot.Type);
        Assert.True(Vector3.Distance(new Vector3(72, 216, 144), spot.Position) < 0.01f, $"{spot.Position}");
        Assert.True(Vector3.Distance(-Vector3.UnitZ, spot.Direction) < 1e-3f, $"{spot.Direction}");
        Assert.InRange(spot.OuterCone, 0.599f, 0.601f);
        Assert.Equal(4f * 72f, g.Lights[1].Range);
        Assert.True(Vector3.Distance(new Vector3(72, 0, 0), g.Lights[1].Position) < 0.01f);
    }

    [Fact]
    public void A_point_lies_on_the_cube_face_its_direction_leans_to_most_and_projects_inside_it()
    {
        Assert.Equal(0, ModernLights.CubeFace(new Vector3(5, 1, -2)));
        Assert.Equal(1, ModernLights.CubeFace(new Vector3(-5, 1, -2)));
        Assert.Equal(2, ModernLights.CubeFace(new Vector3(1, 5, 2)));
        Assert.Equal(3, ModernLights.CubeFace(new Vector3(1, -5, 2)));
        Assert.Equal(4, ModernLights.CubeFace(new Vector3(1, 2, 5)));
        Assert.Equal(5, ModernLights.CubeFace(new Vector3(1, 2, -5)));
        var light = new Vector3(100, 200, 50);
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            var p = light + d * 5 * ModernMath.UnitsPerMetre;
            var c = Vector4.Transform(new Vector4(p, 1), ModernLights.CubeFaceViewProjection(light, ModernLights.CubeFace(d), 10 * ModernMath.UnitsPerMetre, 512));
            Assert.True(c.W > 0);
            Assert.InRange(c.X / c.W, -0.995f, 0.995f);
            Assert.InRange(c.Y / c.W, -0.995f, 0.995f);
            Assert.InRange(c.Z / c.W, 0f, 1f);
        }
    }

    [Fact]
    public void The_lights_that_cast_shadows_are_the_brightest_at_the_eye_within_the_budget_and_in_view()
    {
        float m = ModernMath.UnitsPerMetre;
        var camera = new FreeCamera { Position = Vector3.Zero, Yaw = 0, Pitch = 0 };
        var planes = ModernMath.FrustumPlanes(ModernRenderer.ViewProjection(camera, 16f / 9f));
        var lights = new List<ModernPointLight>
        {
            new(new Vector3(30 * m, 0, 0), Vector3.One * 4, 5 * m),      // far and dim at the eye
            new(new Vector3(5 * m, 1 * m, 0), Vector3.One * 4, 5 * m),   // near
            new(new Vector3(-40 * m, 0, 0), Vector3.One * 40, 5 * m),    // bright but behind, out of view
            new(new Vector3(8 * m, -2 * m, 0), Vector3.One * 8, 5 * m),  // a little farther, brighter
        };
        Assert.Equal([1, 3], ModernLights.PickShadowed(lights, camera.Position, 2, planes));
        Assert.Empty(ModernLights.PickShadowed(lights, camera.Position, 0, planes));
        var packed = ModernLights.Pack(lights, [1, 3]);
        Assert.Equal(-1f, packed[0 * 4 + 3].Z);
        Assert.Equal(0f, packed[1 * 4 + 3].Z);
        Assert.Equal(1f, packed[3 * 4 + 3].Z);
    }

    /// <summary>A camera at the origin looking along +X, the free camera's default field of view (about 97° across a 16:9 picture).</summary>
    static (Matrix4x4 View, float Xs, float Ys) View()
    {
        var camera = new FreeCamera { Position = Vector3.Zero, Yaw = 0, Pitch = 0 };
        float ys = 1f / MathF.Tan(camera.VerticalFov / 2f);
        return (camera.View, ys / (16f / 9f), ys);
    }

    static int[] LightsAt(uint[] clusters, int px, int py, float depth, int w, int h)
    {
        var (gx, gy, _) = ModernLights.Grid(w, h);
        int s = ModernLights.SliceOf(depth);
        int k = (s * gy + Math.Min(py / ModernLights.TileSize, gy - 1)) * gx + Math.Min(px / ModernLights.TileSize, gx - 1);
        return Enumerable.Range(0, (int)clusters[k * 2 + 1]).Select(i => (int)clusters[clusters[k * 2] + i]).ToArray();
    }

    [Fact]
    public void A_light_ahead_is_listed_where_it_reaches_and_not_behind_the_eye_or_far_off_to_the_side()
    {
        int w = 1600, h = 900;
        var (view, xs, ys) = View();
        float m = ModernMath.UnitsPerMetre;
        var lights = new List<ModernPointLight>
        {
            new(new Vector3(20 * m, 0, 0), Vector3.One, 3 * m),          // straight ahead, 20 m
            new(new Vector3(-20 * m, 0, 0), Vector3.One, 3 * m),         // behind the eye
            new(new Vector3(20 * m, 40 * m, 0), Vector3.One, 3 * m),     // well off to the left, outside the view
        };
        var clusters = ModernLights.Assign(lights, view, xs, ys, w, h);
        Assert.Contains(0, LightsAt(clusters, w / 2, h / 2, 20 * m, w, h));
        Assert.DoesNotContain(0, LightsAt(clusters, w / 2, h / 2, 60 * m, w, h));   // far behind it, out of reach
        Assert.DoesNotContain(0, LightsAt(clusters, 10, 10, 20 * m, w, h));          // the picture's corner
        for (int k = 0; k < clusters.Length / 2 && k < ModernLights.Grid(w, h).X * ModernLights.Grid(w, h).Y * ModernLights.Slices; k++)
            for (int i = 0; i < clusters[k * 2 + 1]; i++)
                Assert.Equal(0u, clusters[clusters[k * 2] + i]);
    }

    [Fact]
    public void Every_point_a_light_reaches_finds_that_light_in_its_cluster()
    {
        // Brute force: random lights, random points in view; wherever a point is within a light's reach,
        // the point's cluster must list the light.
        int w = 1280, h = 720;
        var (view, xs, ys) = View();
        float m = ModernMath.UnitsPerMetre;
        var rng = new Random(1141);
        var lights = Enumerable.Range(0, 60).Select(_ => new ModernPointLight(
            new Vector3((float)rng.NextDouble() * 80 * m, ((float)rng.NextDouble() - 0.5f) * 60 * m, ((float)rng.NextDouble() - 0.3f) * 20 * m),
            Vector3.One, (1 + (float)rng.NextDouble() * 12) * m)).ToList();
        var clusters = ModernLights.Assign(lights, view, xs, ys, w, h);
        Matrix4x4.Invert(view, out var toWorld);
        int checkedPairs = 0;
        for (int t = 0; t < 4000; t++)
        {
            int px = rng.Next(w), py = rng.Next(h);
            float depth = (0.5f + (float)rng.NextDouble() * 90) * m;
            float u = (px + 0.5f) / w * 2 - 1, v = 1 - (py + 0.5f) / h * 2;
            var world = Vector3.Transform(new Vector3(u / xs * depth, v / ys * depth, -depth), toWorld);
            var listed = LightsAt(clusters, px, py, depth, w, h);
            for (int i = 0; i < lights.Count; i++)
                if (Vector3.Distance(world, lights[i].Position) < lights[i].Range)
                {
                    Assert.Contains(i, listed);
                    checkedPairs++;
                }
        }
        Assert.True(checkedPairs > 50, $"only {checkedPairs} lit points were tried");
    }

    // ----- atmosphere and screen effects -----

    [Fact]
    public void The_hazes_phase_function_scatters_all_its_light_and_forward_when_g_is_positive()
    {
        foreach (float g in new[] { 0f, 0.45f, 0.82f })
        {
            double sum = 0; int n = 20000;
            for (int i = 0; i < n; i++) { float mu = -1f + 2f * (i + 0.5f) / n; sum += ModernMath.PhaseHg(mu, g) * 2 * Math.PI * (2.0 / n); }
            Assert.InRange(sum, 0.98, 1.02);
        }
        Assert.True(ModernMath.PhaseHg(1f, 0.82f) > 50 * ModernMath.PhaseHg(-1f, 0.82f));
        Assert.InRange(ModernMath.PhaseHg(0.3f, 0f), 1f / (4f * MathF.PI) - 1e-4f, 1f / (4f * MathF.PI) + 1e-4f);
    }

    [Fact]
    public void The_bloom_threshold_passes_nothing_below_its_knee_all_but_the_threshold_above_and_everything_at_zero()
    {
        var c = new Vector3(3f, 2f, 1f);
        Assert.Equal(c, ModernMath.BloomThreshold(c, 0f, 0.5f));
        Assert.Equal(Vector3.Zero, ModernMath.BloomThreshold(new Vector3(0.4f), 1f, 0.5f));
        var above = ModernMath.BloomThreshold(c, 1f, 0.5f);
        Assert.InRange(above.X, 1.999f, 2.001f);   // 3 − 1
        Assert.InRange(above.Y / above.X, 0.666f, 0.668f);   // the colour kept
        float last = 0;
        for (float l = 0.5f; l < 1.6f; l += 0.05f)
        {
            float x = ModernMath.BloomThreshold(new Vector3(l), 1f, 0.5f).X;
            Assert.True(x >= last, $"rises at {l}");
            last = x;
        }
    }

    [Fact]
    public void Agx_keeps_black_puts_mid_grey_near_the_middle_rises_steadily_and_contrast_pivots_on_mid_grey()
    {
        Assert.InRange(ModernMath.AgX(Vector3.Zero).X, 0f, 0.001f);
        float mid = ModernMath.LinearToSrgb(ModernMath.AgX(new Vector3(0.18f)).X);
        Assert.InRange(mid, 0.45f, 0.55f);
        float last = 0;
        foreach (float x in new[] { 0.01f, 0.05f, 0.18f, 0.5f, 1f, 4f, 16f })
        {
            float y = ModernMath.AgX(new Vector3(x)).X;
            Assert.True(y > last, $"rises at {x}");
            last = y;
        }
        Assert.InRange(ModernMath.AgX(new Vector3(0.18f), 1.2f).X, ModernMath.AgX(new Vector3(0.18f)).X - 0.01f, ModernMath.AgX(new Vector3(0.18f)).X + 0.01f);
        Assert.True(ModernMath.AgX(new Vector3(0.02f), 1.2f).X < ModernMath.AgX(new Vector3(0.02f)).X, "more contrast darkens the darks");
        Assert.True(ModernMath.AgX(new Vector3(2f), 1.2f).X > ModernMath.AgX(new Vector3(2f)).X, "and brightens the lights");
    }

    [Fact]
    public void The_flares_ghosts_lie_on_the_line_from_the_sun_through_the_centre()
    {
        var sun = new Vector2(0.2f, 0.3f);
        foreach (var p in ModernMath.FlarePositions(sun))
        {
            var a = p - sun; var b = new Vector2(0.5f) - sun;
            Assert.InRange(a.X * b.Y - a.Y * b.X, -1e-5f, 1e-5f);
        }
        // A sun at the centre folds every ghost onto it.
        Assert.All(ModernMath.FlarePositions(new Vector2(0.5f)), p => Assert.Equal(new Vector2(0.5f), p));
    }

    [Fact]
    public void The_temporal_jitter_spreads_evenly_over_the_pixel()
    {
        var points = Enumerable.Range(1, 8).Select(ModernMath.Halton).ToArray();
        Assert.Equal(new Vector2(0.5f, 1f / 3f), points[0]);
        Assert.All(points, p => { Assert.InRange(p.X, 0f, 1f); Assert.InRange(p.Y, 0f, 1f); });
        var mean = points.Aggregate(Vector2.Zero, (a, b) => a + b) / points.Length;
        Assert.InRange(mean.X, 0.4f, 0.6f);
        Assert.InRange(mean.Y, 0.4f, 0.6f);
        // Each quarter of the pixel gets two of the eight.
        Assert.All(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, q =>
            Assert.Equal(2, points.Count(p => (int)(p.X * 2) == q.Item1 && (int)(p.Y * 2) == q.Item2)));
    }

    // ----- looks and dressing -----

    [Fact]
    public void Each_look_sets_blenders_sun_and_agx_and_an_unknown_look_is_refused()
    {
        // The three concept looks: Blender's gradient skies (the morning look's sky follows its sun; ModernCloudTests).
        foreach (var name in new[] { "chronicle", "golden", "dusk" })
        {
            var s = new ModernSettings();
            Assert.True(ModernLooks.Apply(s, name), name);
            Assert.Equal(ModernToneCurve.AgX, s.ToneCurve);
            Assert.NotNull(s.SkyHorizon);
        }
        var golden = new ModernSettings();
        ModernLooks.Apply(golden, "golden");
        Assert.InRange(golden.SunElevation, 5.9f, 6.1f);
        var dusk = new ModernSettings();
        ModernLooks.Apply(dusk, "dusk");
        Assert.InRange(dusk.Exposure, 4.9f, 4.95f);   // +2.3 stops
        Assert.False(ModernLooks.Apply(new ModernSettings(), "noon"));
    }

    [Fact]
    public void A_dressing_file_adds_lights_in_metres_hides_one_material_and_lights_a_share_of_another()
    {
        var g = new GltfScene();
        g.Materials.Add(new GltfMaterial { Name = "flame" });
        g.Materials.Add(new GltfMaterial { Name = "interior" });
        for (int i = 0; i < 400; i++) g.Primitives.Add(new GltfPrimitive { Material = i < 10 ? 0 : 1 });
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-dress-{Environment.ProcessId}.json");
        File.WriteAllText(path, """
            { "lights": [ { "position": [1, 2, 3], "colour": [1, 0.5, 0.25], "intensity": 4, "range": 10 } ],
              "materials": { "flame": { "hidden": true }, "interior": { "emissive": [1, 0.3, 0.1], "strength": 9, "fraction": 0.5, "seed": 7 } } }
            """);
        try
        {
            var lights = ModernDressing.Apply(path, g, 0.02f);
            var l = Assert.Single(lights);
            Assert.Equal(new Vector3(72, 144, 216), l.Position);
            Assert.Equal(new Vector3(4, 2, 1), l.Colour);
            Assert.Equal(720f, l.Range);
            Assert.Equal(390, g.Primitives.Count);
            int lit = g.Primitives.Count(p => p.Material == 2);
            Assert.InRange(lit, 150, 240);
            Assert.Equal(9f, g.Materials[2].EmissiveStrength);
            Assert.Equal(0f, g.Materials[1].EmissiveStrength * g.Materials[1].EmissiveFactor.X);
        }
        finally { File.Delete(path); }
    }
}
