// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// the hex tiling as the GPU draws it (ours): a large plane under a
/// strongly repeating texture, seen from above, repeats at its tile with a plain repeat (the picture correlates
/// with itself shifted by one tile) and does not with hex tiling; parallax marches the same cells' blended height
/// the surface's colour is read through, so a plateau of the relief stays where its colour is (nothing slides);
/// and as the camera moves sideways the relief under hex tiling changes from frame to frame no more than under a
/// plain repeat (it does not swim). The quality preset turns it off at <c>low</c>.
/// </summary>
[Collection("headless")]
public class ModernHexFrameTests(Headless h)
{
    const int W = 480, H = 270;
    const float M = ModernMath.UnitsPerMetre;

    static void Quad(GltfScene s, Vector3 o, Vector3 u, Vector3 v, int material, float uvScale)
    {
        var n = Vector3.Normalize(Vector3.Cross(u, v));
        var t = new Vector4(Vector3.Normalize(u), 1);
        var vs = new[]
        {
            new ModernVertex { Position = o, Normal = n, Tangent = t, Uv0 = new Vector2(0, uvScale), Color = 0xffffffff },
            new ModernVertex { Position = o + u, Normal = n, Tangent = t, Uv0 = new Vector2(uvScale, uvScale), Color = 0xffffffff },
            new ModernVertex { Position = o + u + v, Normal = n, Tangent = t, Uv0 = new Vector2(uvScale, 0), Color = 0xffffffff },
            new ModernVertex { Position = o + v, Normal = n, Tangent = t, Uv0 = new Vector2(0, 0), Color = 0xffffffff },
        };
        var min = Vector3.Min(Vector3.Min(o, o + u), Vector3.Min(o + v, o + u + v));
        var max = Vector3.Max(Vector3.Max(o, o + u), Vector3.Max(o + v, o + u + v));
        s.Primitives.Add(new GltfPrimitive { Vertices = vs, Indices = [0, 1, 2, 0, 2, 3], Material = material, Min = min, Max = max });
        s.Min = Vector3.Min(s.Min, min); s.Max = Vector3.Max(s.Max, max);
    }

    static int Image(GltfScene s, int n, Func<int, int, byte> grey)
    {
        var rgba = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                byte g = grey(x, y);
                int o = (y * n + x) * 4;
                rgba[o] = rgba[o + 1] = rgba[o + 2] = g; rgba[o + 3] = 255;
            }
        s.Images.Add(new GltfImage { Name = $"image{s.Images.Count}", Width = n, Height = n, Rgba = rgba });
        s.Textures.Add(new GltfTexture(s.Images.Count - 1, true, true));
        return s.Textures.Count - 1;
    }

    /// <summary>Blobs of random size on an n × n tile that wraps: a smoothed random field, 0..1.</summary>
    static float[] Blobs(int n, int seed)
    {
        var rng = new Random(seed);
        var f = new float[n * n];
        for (int i = 0; i < f.Length; i++) f[i] = (float)rng.NextDouble();
        for (int pass = 0; pass < 3; pass++)
        {
            var g = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++) sum += f[(y + dy + n) % n * n + (x + dx + n) % n];
                    g[y * n + x] = sum / 9f;
                }
            f = g;
        }
        float lo = f.Min(), hi = f.Max();
        return [.. f.Select(v => (v - lo) / (hi - lo))];
    }

    /// <summary>A plane 120 m square of one material whose maps tile every <paramref name="tileMetres"/>.</summary>
    static GltfScene Plane(GltfMaterial material, GltfScene s, float tileMetres)
    {
        s.Materials.Add(material);
        Quad(s, new Vector3(-60 * M, -60 * M, 0), new Vector3(120 * M, 0, 0), new Vector3(0, 120 * M, 0), 0, 120f / tileMetres);
        return s;
    }

    static ModernSettings Plain() => new()
    {
        SunAzimuth = 0, SunElevation = 70, NorthYaw = 0, HazeVisibilityMetres = 0, Bloom = false, LensFlare = false,
        AmbientOcclusion = false, SkyOcclusion = false, Volumetric = false, SsrSteps = 0, Fxaa = false, Taa = false,
        MacroVariation = false, Shadows = false,
    };

    float[] Luma(GltfScene gltf, ModernSettings settings, FreeCamera camera)
    {
        using var scene = new ModernScene(h.Gpu!, gltf);
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var px = renderer.RenderToImage(scene, camera, W, H);
        var l = new float[W * H];
        for (int i = 0; i < l.Length; i++) l[i] = 0.299f * px[i * 4] + 0.587f * px[i * 4 + 1] + 0.114f * px[i * 4 + 2];
        return l;
    }

    static float At(float[] l, float x, float y)
    {
        x = Math.Clamp(x, 0, W - 1.001f); y = Math.Clamp(y, 0, H - 1.001f);
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        float a = l[y0 * W + x0] + (l[y0 * W + x0 + 1] - l[y0 * W + x0]) * fx;
        float b = l[(y0 + 1) * W + x0] + (l[(y0 + 1) * W + x0 + 1] - l[(y0 + 1) * W + x0]) * fx;
        return a + (b - a) * fy;
    }

    static Vector2 Pixel(Matrix4x4 vp, Vector3 p)
    {
        var c = Vector4.Transform(new Vector4(p, 1), vp);
        return new Vector2((c.X / c.W * 0.5f + 0.5f) * W, (0.5f - c.Y / c.W * 0.5f) * H);
    }

    /// <summary>The picture's normalised correlation with itself shifted by <paramref name="shift"/> pixels, over the part where both lie inside.</summary>
    static float Correlation(float[] l, Vector2 shift)
    {
        var a = new List<float>(); var b = new List<float>();
        for (int y = 8; y < H - 8; y++)
            for (int x = 8; x < W - 8; x++)
            {
                float sx = x + shift.X, sy = y + shift.Y;
                if (sx < 8 || sy < 8 || sx > W - 9 || sy > H - 9) continue;
                a.Add(l[y * W + x]); b.Add(At(l, sx, sy));
            }
        float ma = a.Average(), mb = b.Average();
        double cov = 0, va = 0, vb = 0;
        for (int i = 0; i < a.Count; i++) { cov += (a[i] - ma) * (b[i] - mb); va += (a[i] - ma) * (a[i] - ma); vb += (b[i] - mb) * (b[i] - mb); }
        return (float)(cov / Math.Sqrt(va * vb + 1e-9));
    }

    [Fact]
    public void A_large_plane_repeats_at_its_tile_with_a_plain_repeat_and_not_with_hex_tiling()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        const float tile = 4f;
        GltfScene Scene(bool hex, bool rotate)
        {
            var s = new GltfScene();
            var blobs = Blobs(32, 7);
            int colour = Image(s, 32, (x, y) => (byte)(30 + 200 * blobs[y * 32 + x]));
            var m = new GltfMaterial
            {
                Name = "setts", BaseColorTexture = new GltfTextureSlot(colour, 0), MetallicFactor = 0, RoughnessFactor = 0.9f,
                Tiling = hex ? new GltfTiling("hex", rotate, 0.5f) : null,
            };
            return Plane(m, s, tile);
        }
        // From 16 m straight down: some eight tiles across the picture.
        var above = new FreeCamera { Position = new Vector3(0, 0, 16 * M), Yaw = 0, Pitch = -89f };
        var vp = ModernRenderer.ViewProjection(above, (float)W / H);
        var origin = Pixel(vp, Vector3.Zero);
        var alongU = Pixel(vp, new Vector3(tile * M, 0, 0)) - origin;
        var alongV = Pixel(vp, new Vector3(0, tile * M, 0)) - origin;
        Assert.InRange(alongU.Length(), 40f, 120f);

        var plain = Luma(Scene(false, false), Plain(), above);
        float plainU = Correlation(plain, alongU), plainV = Correlation(plain, alongV);
        // Half a tile over, the plain picture does not match itself: the texture is not periodic inside its tile.
        float plainHalf = Correlation(plain, alongU * 0.5f);
        Assert.True(plainU > 0.85f && plainV > 0.85f, $"a plain repeat correlates {plainU:0.00} and {plainV:0.00} a tile over");
        Assert.True(plainHalf < 0.5f, $"half a tile over {plainHalf:0.00}");

        foreach (bool rotate in new[] { false, true })
        {
            var hex = Luma(Scene(true, rotate), Plain(), above);
            float hexU = Correlation(hex, alongU), hexV = Correlation(hex, alongV);
            Assert.True(MathF.Abs(hexU) < 0.25f && MathF.Abs(hexV) < 0.25f,
                        $"hex tiling (rotate {rotate}) correlates {hexU:0.00} and {hexV:0.00} a tile over (plain {plainU:0.00}, {plainV:0.00})");
            // The same texture still: its brightness and contrast survive the blend between cells.
            float pm = plain.Average(), hm = hex.Average();
            Assert.True(MathF.Abs(pm - hm) < 0.08f * pm, $"mean brightness plain {pm:0.0}, hex {hm:0.0}");
            float ps = Spread(plain), hs = Spread(hex);
            Assert.True(hs > 0.7f * ps, $"contrast plain {ps:0.0}, hex {hs:0.0}");
        }

        // At low the preset turns it off: the flagged material draws as the plain one.
        var low = Plain();
        ModernQualityPreset.For(ModernQuality.Low).Apply(low);
        low.Fxaa = false; low.Shadows = false; low.MacroVariation = false;
        var lowPlain = Luma(Scene(false, false), low, above);
        var lowHex = Luma(Scene(true, true), low, above);
        Assert.Equal(lowPlain, lowHex);
    }

    static float Spread(float[] l)
    {
        float m = l.Average();
        return MathF.Sqrt(l.Select(v => (v - m) * (v - m)).Average());
    }

    /// <summary>
    /// Plateaus and pits (a height map of 0 or 1, soft-edged, tiling every metre), hex-tiled with turned cells, under
    /// either a colour that is the height (white where high, to find the plateaus in a picture) or a fine random
    /// colour (each texel its own, so any shift of the coordinates shows).
    /// </summary>
    static GltfScene Plateaus(bool hex, bool colourIsHeight = true)
    {
        var s = new GltfScene();
        var blobs = Blobs(64, 11);
        var rng = new Random(5);
        var noise = Enumerable.Range(0, 64 * 64).Select(_ => (byte)rng.Next(20, 240)).ToArray();
        float Step(float v) => Math.Clamp((v - 0.45f) * 10f, 0f, 1f);
        int colour = colourIsHeight ? Image(s, 64, (x, y) => (byte)(20 + 220 * Step(blobs[y * 64 + x]))) : Image(s, 64, (x, y) => noise[y * 64 + x]);
        int height = Image(s, 64, (x, y) => (byte)(255 * Step(blobs[y * 64 + x])));
        var m = new GltfMaterial
        {
            Name = "plateaus", BaseColorTexture = new GltfTextureSlot(colour, 0), MetallicFactor = 0, RoughnessFactor = 1f,
            HeightTexture = new GltfTextureSlot(height, 0), HeightScale = 0.06f,
            Tiling = hex ? new GltfTiling("hex", true, 0.5f) : null,
        };
        return Plane(m, s, 1f);
    }

    static ModernSettings Relief(bool on)
    {
        var s = Plain();
        s.ParallaxSteps = on ? 32 : 0;
        s.ParallaxMetres = 30f;
        return s;
    }

    [Fact]
    public void Parallax_marches_the_cells_blended_height_so_a_plateau_stays_where_its_colour_is()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var oblique = new FreeCamera { Position = new Vector3(0, -3 * M, 2.2f * M), Yaw = 90, Pitch = -40 };
        // Where the ray meets a plateau's top it stops at once: there parallax moves nothing. So where the surface
        // shows a plateau's top (the colour-is-height picture, without parallax), the fine random colour must read
        // the same with parallax as without — if the march reads the same blended height the maps are read through.
        // Reading any other height (the plain tile's, or one cell's), half the plateaus' tops would march into pits
        // and show other texels.
        (float OnTops, float Elsewhere, int Tops) Shift(bool hex)
        {
            var tops = Luma(Plateaus(hex), Relief(false), oblique);
            var flat = Luma(Plateaus(hex, colourIsHeight: false), Relief(false), oblique);
            var deep = Luma(Plateaus(hex, colourIsHeight: false), Relief(true), oblique);
            float top = tops.Max();
            double onTops = 0, elsewhere = 0; int n = 0, m = 0;
            for (int i = W * H / 3; i < tops.Length; i++)   // the nearer two thirds, where parallax is strongest
            {
                float d = MathF.Abs(flat[i] - deep[i]);
                if (tops[i] >= 0.97f * top) { onTops += d; n++; }
                else if (tops[i] < 0.3f * top) { elsewhere += d; m++; }
            }
            return ((float)(onTops / n), (float)(elsewhere / m), n);
        }
        var plain = Shift(false);
        var hexed = Shift(true);
        Assert.True(plain.Tops > 2000 && hexed.Tops > 2000, $"plateau tops: plain {plain.Tops}, hex {hexed.Tops} pixels");
        // In the pits parallax moves the colour (by up to some 7 cm here, several texels); on the tops it does not.
        Assert.True(hexed.Elsewhere > 15f && plain.Elsewhere > 15f, $"parallax's change in the pits: hex {hexed.Elsewhere:0.0}, plain {plain.Elsewhere:0.0}");
        Assert.True(hexed.OnTops < 0.4f * hexed.Elsewhere && hexed.OnTops < plain.OnTops + 3f,
                    $"parallax's change on the plateaus' tops: hex {hexed.OnTops:0.0} (pits {hexed.Elsewhere:0.0}), plain {plain.OnTops:0.0} (pits {plain.Elsewhere:0.0})");
    }

    [Fact]
    public void Under_hex_tiling_the_relief_does_not_swim_as_the_camera_moves_sideways()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        // Five frames 4 cm apart, sideways; each frame's picture read back at the same points of the ground (a grid
        // over the near ground, through each frame's own projection). Under a plain repeat what changes from frame
        // to frame is the relief's true parallax; hex tiling must not add to it (cells' contents sliding against
        // each other, or a seam that moves, would).
        float Change(bool hex)
        {
            var points = new List<Vector3>();
            for (float y = -0.6f; y <= 1.6f; y += 0.04f)
                for (float x = -1.6f; x <= 1.6f; x += 0.04f) points.Add(new Vector3(x * M, y * M, 0));
            float[]? last = null;
            double sum = 0; int pairs = 0;
            for (int k = 0; k < 5; k++)
            {
                var camera = new FreeCamera { Position = new Vector3(0.04f * k * M, -3 * M, 2.2f * M), Yaw = 90, Pitch = -40 };
                var l = Luma(Plateaus(hex), Relief(true), camera);
                var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
                var seen = points.Select(p => { var q = Pixel(vp, p); return At(l, q.X, q.Y); }).ToArray();
                if (last is not null) { sum += seen.Zip(last, (a, b) => MathF.Abs(a - b)).Average(); pairs++; }
                last = seen;
            }
            return (float)(sum / pairs);
        }
        float plain = Change(false), hexed = Change(true);
        Assert.True(hexed < plain * 1.35f + 1f, $"the ground's change from frame to frame: hex {hexed:0.00}, plain {plain:0.00}");
    }
}
