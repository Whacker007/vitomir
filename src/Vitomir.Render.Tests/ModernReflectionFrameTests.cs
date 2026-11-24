// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// the reflections and specular as the GPU draws them
/// (ours): a wet plane seen at a grazing angle reflects the wall before it cleanly, with no more fine-grained
/// speckle than the plane has without screen-space reflections, with and without temporal anti-aliasing, and keeps
/// it clean when the camera moves; where the reflected rays leave the screen it shows the sky's reflection, as
/// without them, not black; a finely corrugated glossy plane far off and a sunlit crown of leaf cards sparkle no more than
/// their own supersampled pictures (specular anti-aliasing widens the roughness where the normal changes within
/// a pixel); and the sun's reflection is shadowed where the material's occlusion says a crevice is.
/// </summary>
[Collection("headless")]
public class ModernReflectionFrameTests(Headless h)
{
    const int W = 480, H = 270;
    const float M = ModernMath.UnitsPerMetre;

    static void Quad(GltfScene s, Vector3 o, Vector3 u, Vector3 v, int material, float uvScale = 1f, Vector3? normal = null, uint colour = 0xffffffff)
    {
        var n = normal ?? Vector3.Normalize(Vector3.Cross(u, v));
        var t = new Vector4(Vector3.Normalize(u), 1);
        var vs = new[]
        {
            new ModernVertex { Position = o, Normal = n, Tangent = t, Uv0 = new Vector2(0, uvScale), Color = colour },
            new ModernVertex { Position = o + u, Normal = n, Tangent = t, Uv0 = new Vector2(uvScale, uvScale), Color = colour },
            new ModernVertex { Position = o + u + v, Normal = n, Tangent = t, Uv0 = new Vector2(uvScale, 0), Color = colour },
            new ModernVertex { Position = o + v, Normal = n, Tangent = t, Uv0 = new Vector2(0, 0), Color = colour },
        };
        var min = Vector3.Min(Vector3.Min(o, o + u), Vector3.Min(o + v, o + u + v));
        var max = Vector3.Max(Vector3.Max(o, o + u), Vector3.Max(o + v, o + u + v));
        s.Primitives.Add(new GltfPrimitive { Vertices = vs, Indices = [0, 1, 2, 0, 2, 3], Material = material, Min = min, Max = max });
        s.Min = Vector3.Min(s.Min, min); s.Max = Vector3.Max(s.Max, max);
    }

    static int Image(GltfScene s, int w, int hh, Func<int, int, (byte, byte, byte, byte)> px)
    {
        var rgba = new byte[w * hh * 4];
        for (int y = 0; y < hh; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = px(x, y);
                int o = (y * w + x) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = a;
            }
        s.Images.Add(new GltfImage { Name = $"image{s.Images.Count}", Width = w, Height = hh, Rgba = rgba });
        s.Textures.Add(new GltfTexture(s.Images.Count - 1, true, true));
        return s.Textures.Count - 1;
    }

    /// <summary>A tangent-space normal map of random tilts (up to <paramref name="tilt"/>), one per <paramref name="block"/> texels.</summary>
    static int BumpMap(GltfScene s, int n, int block, float tilt, int seed)
    {
        var rng = new Random(seed);
        var tilts = new Vector2[(n / block) * (n / block)];
        for (int i = 0; i < tilts.Length; i++)
            tilts[i] = new Vector2((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * tilt;
        return Image(s, n, n, (x, y) =>
        {
            var t = tilts[(y / block) * (n / block) + x / block];
            return ((byte)(127.5f + 127f * t.X), (byte)(127.5f + 127f * t.Y), 255, 255);
        });
    }

    static ModernSettings Day(float azimuth, float elevation) => new()
    {
        SunAzimuth = azimuth, SunElevation = elevation, NorthYaw = 0, HazeVisibilityMetres = 0, Bloom = false, LensFlare = false,
        AmbientOcclusion = false, SkyOcclusion = false, Volumetric = false, SsrSteps = 0, Fxaa = false, Taa = false,
        MacroVariation = false, Shadows = false,
    };

    byte[] Render(GltfScene gltf, ModernSettings settings, FreeCamera camera, int w = W, int hh = H, string? name = null)
    {
        using var scene = new ModernScene(h.Gpu!, gltf);
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var px = renderer.RenderToImage(scene, camera, w, hh);
        Dump(name, px, w, hh);
        return px;
    }

    /// <summary>With VITOMIR_TEST_PICTURES set to a folder, adds a line to its measures.txt.</summary>
    static void Note(string line)
    {
        var dir = Environment.GetEnvironmentVariable("VITOMIR_TEST_PICTURES");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "measures.txt"), line + Environment.NewLine);
    }

    /// <summary>With VITOMIR_TEST_PICTURES set to a folder, writes the picture there to look at.</summary>
    static void Dump(string? name, byte[] px, int w = W, int hh = H)
    {
        var dir = Environment.GetEnvironmentVariable("VITOMIR_TEST_PICTURES");
        if (name is null || string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        Png.Write(Path.Combine(dir, name + ".png"), w, hh, px);
    }

    static float[] Luma(byte[] px, int w = W, int hh = H)
    {
        var l = new float[w * hh];
        for (int i = 0; i < l.Length; i++) l[i] = 0.299f * px[i * 4] + 0.587f * px[i * 4 + 1] + 0.114f * px[i * 4 + 2];
        return l;
    }

    /// <summary>A picture <paramref name="k"/> times larger reduced by averaging k × k blocks (a supersampled reference).</summary>
    static float[] Reduce(float[] l, int w, int hh, int k)
    {
        var r = new float[(w / k) * (hh / k)];
        for (int y = 0; y < hh / k; y++)
            for (int x = 0; x < w / k; x++)
            {
                float sum = 0;
                for (int dy = 0; dy < k; dy++)
                    for (int dx = 0; dx < k; dx++) sum += l[(y * k + dy) * w + x * k + dx];
                r[y * (w / k) + x] = sum / (k * k);
            }
        return r;
    }

    static Vector2 Pixel(FreeCamera camera, Vector3 p)
    {
        var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
        var c = Vector4.Transform(new Vector4(p, 1), vp);
        return new Vector2((c.X / c.W * 0.5f + 0.5f) * W, (0.5f - c.Y / c.W * 0.5f) * H);
    }

    /// <summary>
    /// The fine-grained energy of a picture over rows <paramref name="y0"/>..<paramref name="y1"/>: the mean
    /// difference of each pixel from the mean of its 3 × 3 neighbourhood — what a dithered speckle or sparkle adds and a
    /// smooth reflection does not.
    /// </summary>
    static float Speckle(float[] l, int y0, int y1, int x0 = 8, int x1 = W - 8, int w = W)
    {
        double sum = 0; int count = 0;
        for (int y = Math.Max(y0, 1); y <= Math.Min(y1, l.Length / w - 2); y++)
            for (int x = Math.Max(x0, 1); x <= Math.Min(x1, w - 2); x++)
            {
                float m = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) m += l[(y + dy) * w + x + dx];
                sum += MathF.Abs(l[y * w + x] - m / 9f); count++;
            }
        return (float)(sum / Math.Max(count, 1));
    }

    static float Mean(float[] l, int y0, int y1, int x0 = 8, int x1 = W - 8)
    {
        double sum = 0; int count = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { sum += l[y * W + x]; count++; }
        return (float)(sum / Math.Max(count, 1));
    }

    /// <summary>The mean of red less blue over the rows: how much of the red wall a region shows.</summary>
    static float Redness(byte[] px, int y0, int y1)
    {
        double sum = 0; int count = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = 8; x < W - 8; x++) { int o = (y * W + x) * 4; sum += px[o] - px[o + 2]; count++; }
        return (float)(sum / Math.Max(count, 1));
    }

    /// <summary>
    /// A wet plane (a dark stone's colour, roughness 0.08, smooth enough for the screen's reflections) and a
    /// red wall 25 m ahead of the origin, 12 m high, lit by the sun from behind the camera.
    /// </summary>
    static GltfScene WetPlaneAndWall()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "wet_stone", BaseColorFactor = new Vector4(0.12f, 0.11f, 0.1f, 1), MetallicFactor = 0, RoughnessFactor = 0.08f });
        s.Materials.Add(new GltfMaterial { Name = "red_wall", BaseColorFactor = new Vector4(0.6f, 0.1f, 0.06f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        Quad(s, new Vector3(-60 * M, -100 * M, 0), new Vector3(160 * M, 0, 0), new Vector3(0, 200 * M, 0), 0);
        Quad(s, new Vector3(25 * M, 100 * M, 0), new Vector3(0, -200 * M, 0), new Vector3(0, 0, 12 * M), 1);
        return s;
    }

    static ModernSettings Wet(int ssrSteps, bool taa)
    {
        var s = Day(180, 35);
        s.SsrSteps = ssrSteps; s.Taa = taa;
        return s;
    }

    [Fact]
    public void A_wet_plane_at_a_grazing_angle_reflects_the_wall_cleanly_with_and_without_temporal_anti_aliasing()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = -6f };
        // The rows where the plane mirrors the wall: below the wall's foot, down to where the wall's top is mirrored
        // (the ray to the wall top's mirror image, 12 m under the ground, meets the ground 3.1 m out).
        int foot = (int)Pixel(eye, new Vector3(25 * M, 0, 0)).Y, top = (int)Pixel(eye, new Vector3(3.1f * M, 0, 0)).Y;
        Assert.InRange(top - foot, 60, 300);
        int y0 = foot + 4, y1 = Math.Min(top - 4, H - 4);

        foreach (bool taa in new[] { false, true })
        {
            var dry = Render(WetPlaneAndWall(), Wet(0, taa), eye, name: $"wet_dry_taa{taa}");
            var wet = Render(WetPlaneAndWall(), Wet(24, taa), eye, name: $"wet_ssr_taa{taa}");
            float off = Speckle(Luma(dry), y0, y1), on = Speckle(Luma(wet), y0, y1);
            float redOff = Redness(dry, y0, y1), redOn = Redness(wet, y0, y1);
            Note($"grazing taa={taa}: speckle on {on:0.00} off {off:0.00}; red on {redOn:0.0} off {redOff:0.0}");
            // It reflects the wall (redder than the sky the plane reflects without them) ...
            Assert.True(redOn > redOff + 50f, $"taa {taa}: red less blue {redOn:0.0} with the screen's reflections, {redOff:0.0} without");
            // ... cleanly: no more fine speckle than the plane has without them.
            Assert.True(on <= off * 1.3f + 0.05f, $"taa {taa}: speckle {on:0.00} with the screen's reflections, {off:0.00} without (rows {y0}..{y1})");
        }
    }

    [Fact]
    public void A_moving_camera_keeps_the_wet_planes_reflection_clean_and_leaves_no_ghost()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var settings = Wet(24, true);
        using var scene = new ModernScene(h.Gpu!, WetPlaneAndWall());
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        // Settle at one place, then walk sideways 0.5 m a frame for four frames.
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = -6f };
        renderer.RenderToImage(scene, eye, W, H);
        settings.TaaStillFrames = 1;
        byte[] moving = [];
        for (int k = 1; k <= 4; k++)
        {
            eye = new FreeCamera { Position = new Vector3(0, 0.5f * k * M, 1.7f * M), Yaw = 0, Pitch = -6f };
            moving = renderer.RenderToImage(scene, eye, W, H);
        }
        // The same place settled from the start.
        settings.TaaStillFrames = 8;
        using var fresh = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var still = fresh.RenderToImage(scene, eye, W, H);
        Dump("move_moving", moving); Dump("move_still", still);
        int foot = (int)Pixel(eye, new Vector3(25 * M, 0, 0)).Y, top = (int)Pixel(eye, new Vector3(3.1f * M, 0, 0)).Y;
        int y0 = foot + 4, y1 = Math.Min(top - 4, H - 4);
        float sm = Speckle(Luma(moving), y0, y1), ss = Speckle(Luma(still), y0, y1);
        float mm = Mean(Luma(moving), y0, y1), ms = Mean(Luma(still), y0, y1);
        Note($"moving: speckle {sm:0.00} still {ss:0.00}; mean {mm:0.0} still {ms:0.0}");
        Assert.True(sm <= ss * 1.5f + 0.6f, $"speckle moving {sm:0.00}, still {ss:0.00}");
        Assert.True(MathF.Abs(mm - ms) < 0.06f * ms + 2f, $"mean moving {mm:0.0}, still {ms:0.0}");
    }

    [Fact]
    public void Where_the_reflected_rays_leave_the_screen_the_wet_plane_shows_the_skys_reflection_not_black()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // Looking down at 35°: the wall's mirror image is off the bottom of the picture and the rays from most
        // of the plane leave the screen at the top before they meet anything.
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = -35f };
        foreach (bool taa in new[] { false, true })
        {
            var dry = Luma(Render(WetPlaneAndWall(), Wet(0, taa), eye, name: $"down_dry_taa{taa}"));
            var wet = Luma(Render(WetPlaneAndWall(), Wet(24, taa), eye, name: $"down_ssr_taa{taa}"));
            int horizon = (int)Pixel(eye, new Vector3(25 * M, 0, 0)).Y;
            float a = Mean(dry, horizon + 6, H - 6), b = Mean(wet, horizon + 6, H - 6);
            float sa0 = Speckle(dry, horizon + 6, H - 6), sb0 = Speckle(wet, horizon + 6, H - 6);
            Note($"down taa={taa}: mean on {b:0.0} off {a:0.0}; speckle on {sb0:0.00} off {sa0:0.00}");
            Assert.True(b > a * 0.9f - 1f, $"taa {taa}: mean {b:0.0} with the screen's reflections, {a:0.0} without");
            // No band or edge where the rays leave: no more speckle than without.
            float sa = Speckle(dry, horizon + 6, H - 6), sb = Speckle(wet, horizon + 6, H - 6);
            Assert.True(sb <= sa * 1.3f + 0.6f, $"taa {taa}: speckle {sb:0.00} with, {sa:0.00} without");
        }
    }

    /// <summary>
    /// A large glossy plane finely corrugated across the view: strips 2 cm wide whose normals swing from 6° one way
    /// to 6° the other across each (smoothly, as a ridged sheet's vertex normals do), from 2 m to 200 m out, with
    /// the sun's glint ahead. Far off many strips fall in a pixel, which then should show their average glint.
    /// </summary>
    static GltfScene CorrugatedPlane()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "glossy", BaseColorFactor = new Vector4(0.2f, 0.2f, 0.2f, 1), MetallicFactor = 0, RoughnessFactor = 0.1f });
        const int strips = 9900;
        var vs = new ModernVertex[(strips + 1) * 2];
        var tilt = 6f * MathF.PI / 180f;
        for (int k = 0; k <= strips; k++)
        {
            float x = (2f + k * 0.02f) * M;
            var n = new Vector3((k % 2 == 0 ? 1 : -1) * MathF.Sin(tilt), 0, MathF.Cos(tilt));
            var t = new Vector4(1, 0, 0, 1);
            vs[k * 2] = new ModernVertex { Position = new Vector3(x, -100 * M, 0), Normal = n, Tangent = t, Color = 0xffffffff };
            vs[k * 2 + 1] = new ModernVertex { Position = new Vector3(x, 100 * M, 0), Normal = n, Tangent = t, Color = 0xffffffff };
        }
        var idx = new List<uint>();
        for (uint k = 0; k < strips; k++)
        {
            uint a = k * 2, b = k * 2 + 1, c = k * 2 + 2, d = k * 2 + 3;
            idx.AddRange([a, c, d, a, d, b]);
        }
        var min = new Vector3(2 * M, -100 * M, 0);
        var max = new Vector3((2f + strips * 0.02f) * M, 100 * M, 0);
        s.Primitives.Add(new GltfPrimitive { Vertices = vs, Indices = [.. idx], Material = 0, Min = min, Max = max });
        s.Min = min; s.Max = max;
        return s;
    }

    /// <summary>The mean difference of two pictures over a region.</summary>
    static float Difference(float[] a, float[] b, int y0, int y1, int x0, int x1)
    {
        double sum = 0; int count = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { sum += MathF.Abs(a[y * W + x] - b[y * W + x]); count++; }
        return (float)(sum / Math.Max(count, 1));
    }

    [Fact]
    public void A_finely_corrugated_glossy_plane_glints_as_its_supersampled_picture_does()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = -8f };
        var settings = Day(0, 12);
        var one = Luma(Render(CorrugatedPlane(), settings, eye, name: "ridged_1x"));
        var four = Reduce(Luma(Render(CorrugatedPlane(), settings, eye, W * 4, H * 4, "ridged_4x"), W * 4, H * 4), W * 4, H * 4, 4);
        var plainSettings = Day(0, 12);
        plainSettings.SpecularAntiAliasing = false;
        var plain = Luma(Render(CorrugatedPlane(), plainSettings, eye, name: "ridged_noaa"));
        // The sun's glint from 15 m out to 60 m, where a pixel spans several strips: the middle third of the picture.
        int y0 = (int)Pixel(eye, new Vector3(60 * M, 0, 0)).Y, y1 = (int)Pixel(eye, new Vector3(15 * M, 0, 0)).Y;
        int x0 = W / 3, x1 = 2 * W / 3;
        float m1 = Mean(one, y0, y1, x0, x1), m4 = Mean(four, y0, y1, x0, x1), m0 = Mean(plain, y0, y1, x0, x1);
        float d1 = Difference(one, four, y0, y1, x0, x1), d0 = Difference(plain, four, y0, y1, x0, x1);
        Note($"ridged: mean {m1:0.0}, without specular anti-aliasing {m0:0.0}, supersampled {m4:0.0}; difference from it {d1:0.0}, without {d0:0.0}");
        // One sample a pixel, the glint is as bright and as wide as sixteen samples make it (a narrow lobe on the one
        // normal each pixel happens to see makes a thin streak, some of it sparkling, and misses the rest).
        Assert.True(MathF.Abs(m1 - m4) < 0.06f * m4 + 2f, $"mean {m1:0.0} at one sample a pixel, {m4:0.0} supersampled");
        Assert.True(d1 < 0.6f * d0, $"difference from the supersampled picture {d1:0.0}, without specular anti-aliasing {d0:0.0}");
    }

    /// <summary>
    /// A crown of 900 leaf cards (25 cm, cut-out leaves with a normal map tilting each leaf its own way) in a ball
    /// 3 m across, 14 m off, 6 m up, their normals bent outward from the crown's centre as the tree generator's are.
    /// </summary>
    static GltfScene Crown()
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.3f, 0.32f, 0.2f, 1), MetallicFactor = 0, RoughnessFactor = 0.95f });
        int colour = Image(s, 32, 32, (x, y) =>
        {
            float dx = (x % 8) - 3.5f, dy = (y % 8) - 3.5f;
            return (40, 90, 25, (byte)(dx * dx + dy * dy * 0.5f < 12f ? 255 : 0));
        });
        int bumps = BumpMap(s, 32, 8, 0.5f, 5);
        s.Materials.Add(new GltfMaterial
        {
            Name = "leaves", BaseColorTexture = new GltfTextureSlot(colour, 0), NormalTexture = new GltfTextureSlot(bumps, 0),
            MetallicFactor = 0, RoughnessFactor = 0.45f, AlphaMode = GltfAlphaMode.Mask, AlphaCutoff = 0.5f, DoubleSided = true,
            Foliage = true, Translucency = 0.5f, Wind = 0f,
        });
        Quad(s, new Vector3(-50 * M, -100 * M, 0), new Vector3(200 * M, 0, 0), new Vector3(0, 200 * M, 0), 0);
        var rng = new Random(11);
        var centre = new Vector3(14 * M, 0, 6 * M);
        for (int i = 0; i < 900; i++)
        {
            Vector3 d;
            do d = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1);
            while (d.LengthSquared() > 1f);
            var p = centre + d * 1.5f * M;
            var a = Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1));
            var b = Vector3.Normalize(Vector3.Cross(a, new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, 1f)));
            var bent = Vector3.Normalize(p - centre + new Vector3(0, 0, 0.3f * M));
            Quad(s, p - (a + b) * 0.125f * M, a * 0.25f * M, b * 0.25f * M, 1, 1f, bent, 0x00ffffff);
        }
        return s;
    }

    [Fact]
    public void A_sunlit_crown_of_leaf_cards_shows_no_white_specks()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // The sun high beyond the crown, the crown seen from below its middle: its sunlit top is seen against the light.
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = 17f };
        var settings = Day(0, 50);
        var one = Luma(Render(Crown(), settings, eye, name: "crown_1x"));
        var four = Reduce(Luma(Render(Crown(), settings, eye, W * 4, H * 4, "crown_4x"), W * 4, H * 4), W * 4, H * 4, 4);
        var c = Pixel(eye, new Vector3(14 * M, 0, 6 * M));
        int r = (int)MathF.Abs(Pixel(eye, new Vector3(14 * M, 1.6f * M, 6 * M)).X - c.X);
        int y0 = (int)c.Y - r, y1 = (int)c.Y + r, x0 = (int)c.X - r, x1 = (int)c.X + r;
        float s1 = Speckle(one, y0, y1, x0, x1), s4 = Speckle(four, y0, y1, x0, x1);
        // A white speck: a pixel near white standing well above its neighbours.
        int specks = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float m = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) m += one[(y + dy) * W + x + dx];
                if (one[y * W + x] > 170f && one[y * W + x] > m / 9f + 35f) specks++;
            }
        Note($"crown: specks {specks}; sparkle {s1:0.00} ss {s4:0.00}");
        Assert.True(specks <= 2, $"{specks} white specks in the crown (sparkle {s1:0.00}, supersampled {s4:0.00})");
    }

    [Fact]
    public void The_suns_reflection_is_shadowed_where_the_materials_occlusion_says_a_crevice_is()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // A nearly black glossy plane, so what shows is its reflection, with an occlusion map of stripes across the
        // view: 1 m open (1), 1 m in a crevice (0.1); the open ones' middles at x = 6.5, 8.5, 10.5 m, the crevices'
        // a metre further.
        var s = new GltfScene();
        int occ = Image(s, 16, 16, (x, y) => (x < 8 ? (byte)255 : (byte)26, 0, 0, 255));
        s.Materials.Add(new GltfMaterial
        {
            Name = "joints", BaseColorFactor = new Vector4(0.01f, 0.01f, 0.01f, 1), MetallicFactor = 0, RoughnessFactor = 0.3f,
            OcclusionTexture = new GltfTextureSlot(occ, 0), OcclusionStrength = 1f,
        });
        Quad(s, new Vector3(-20 * M, -100 * M, 0), new Vector3(200 * M, 0, 0), new Vector3(0, 200 * M, 0), 0, 100f);
        // The sun low ahead: its glint lies on the plane some 5–15 m out.
        var eye = new FreeCamera { Position = new Vector3(0, 0, 1.7f * M), Yaw = 0, Pitch = -12f };
        var l = Luma(Render(s, Day(0, 15), eye, name: "joints"));
        // Sample the stripes' middles along the glint (y = 0).
        float open = 0, crevice = 0; int n = 0;
        for (float x = 6.5f; x < 11f; x += 2f)
        {
            var po = Pixel(eye, new Vector3(x * M, 0, 0));
            var pc = Pixel(eye, new Vector3((x + 1f) * M, 0, 0));
            open += l[(int)po.Y * W + (int)po.X]; crevice += l[(int)pc.Y * W + (int)pc.X]; n++;
        }
        open /= n; crevice /= n;
        Note($"joints: open {open:0.0} crevice {crevice:0.0}");
        Assert.True(open > 40f, $"the open stripes' glint {open:0.0}");
        Assert.True(crevice < open * 0.4f, $"the crevices' glint {crevice:0.0}, the open stripes' {open:0.0}");
    }
}
