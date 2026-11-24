// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// the surfaces as the GPU draws them (ours): a cut-out
/// foliage card — half of its texture opaque, half clear, its vertices' alpha (the sway weight) 0, which must
/// not cut — floating over the ground. The clear half shows the ground through it in the prepass and the
/// shading pass and lets the sun's shadow cascades and a torch's shadow cube through; the opaque half shows
/// itself and casts. A card lit from behind is brighter with translucency than without. And a ground with a
/// height map draws the same at <c>low</c> as without one (parallax off) but not at <c>medium</c>. And a
/// foliage card seen from behind keeps its own (bent) normal, and its map's translucency mask limits the light
/// through it.
/// </summary>
[Collection("headless")]
public class ModernFoliageFrameTests(Headless h)
{
    const int W = 320, H = 180;
    const float M = ModernMath.UnitsPerMetre;

    static void Quad(GltfScene s, Vector3 o, Vector3 u, Vector3 v, int material, uint colour, float uvScale = 1f, Vector3? normal = null)
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

    /// <summary>The ground, and a 4 m square foliage card 2 m over it, facing up: its texture's left half (u &lt; ½) opaque red, its right half clear.</summary>
    static GltfScene CardOverGround(float translucency = 0.5f, bool vertical = false)
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        int tex = Image(s, 16, 16, (x, y) => (200, 40, 30, (byte)(x < 8 ? 255 : 0)));
        s.Materials.Add(new GltfMaterial
        {
            Name = "leaf", BaseColorTexture = new GltfTextureSlot(tex, 0), MetallicFactor = 0, RoughnessFactor = 0.8f,
            AlphaMode = GltfAlphaMode.Mask, AlphaCutoff = 0.5f, DoubleSided = true, Foliage = true, Translucency = translucency, Wind = 0f,
        });
        Quad(s, new Vector3(-10 * M, -10 * M, 0), new Vector3(20 * M, 0, 0), new Vector3(0, 20 * M, 0), 0, 0xffffffff);
        if (vertical) Quad(s, new Vector3(-2 * M, 0, 0.5f * M), new Vector3(4 * M, 0, 0), new Vector3(0, 0, 3 * M), 1, 0x00ffffff);
        else Quad(s, new Vector3(-2 * M, -2 * M, 2 * M), new Vector3(4 * M, 0, 0), new Vector3(0, 4 * M, 0), 1, 0x00ffffff);
        return s;
    }

    Func<Vector3, Vector3> Render(GltfScene gltf, ModernSettings settings, FreeCamera camera, params ModernPointLight[] lights)
    {
        using var scene = new ModernScene(h.Gpu!, gltf);
        scene.Lights.AddRange(lights);
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var pixels = renderer.RenderToImage(scene, camera, W, H);
        var vp = ModernRenderer.ViewProjection(camera, (float)W / H);
        return p =>
        {
            var c = Vector4.Transform(new Vector4(p, 1), vp);
            int x = (int)((c.X / c.W * 0.5f + 0.5f) * W), y = (int)((0.5f - c.Y / c.W * 0.5f) * H);
            var sum = Vector3.Zero;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int o = (Math.Clamp(y + dy, 0, H - 1) * W + Math.Clamp(x + dx, 0, W - 1)) * 4;
                    sum += new Vector3(pixels[o], pixels[o + 1], pixels[o + 2]);
                }
            return sum / 9f;
        };
    }

    static ModernSettings Day() => new()
    {
        SunAzimuth = 0, SunElevation = 60, NorthYaw = 0, HazeVisibilityMetres = 0, Bloom = false, LensFlare = false,
        AmbientOcclusion = false, SkyOcclusion = false, Volumetric = false, SsrSteps = 0, Fxaa = false,
    };

    static float Grey(Vector3 c) => (c.X + c.Y + c.Z) / 3f;

    [Fact]
    public void A_cut_out_shows_and_casts_only_its_opaque_half_in_the_sun()
    {
        var settings = Day();
        // Seen from straight above (a little off, so the card does not hide its own shadow's place).
        var above = new FreeCamera { Position = new Vector3(0, -1 * M, 14 * M), Yaw = 90, Pitch = -85 };
        var look = Render(CardOverGround(), settings, above);
        // Through the card: the opaque half is red, the clear half shows the grey ground (the prepass and the shading pass cut alike).
        var opaque = look(new Vector3(-1 * M, 0, 2 * M)); var clear = look(new Vector3(1 * M, 0, 2 * M));
        Assert.True(opaque.X > opaque.Y * 1.8f, $"the opaque half {opaque}");
        Assert.True(MathF.Abs(clear.X - clear.Y) < 25f, $"the clear half shows the ground: {clear}");
        // The shadow: the sun 60° up in the north (+X), so a point 2 m up shades the ground 2 / tan 60° m towards −X.
        float off = 2f / MathF.Tan(60f * MathF.PI / 180f) * M;
        var side = new FreeCamera { Position = new Vector3(0, -12 * M, 6 * M), Yaw = 90, Pitch = -25 };
        var ground = Render(CardOverGround(), settings, side);
        var shaded = ground(new Vector3(-1 * M - off, -1.5f * M, 0)); var lit = ground(new Vector3(1 * M - off, -1.5f * M, 0));
        var open = ground(new Vector3(6 * M, -1.5f * M, 0));
        Assert.True(Grey(shaded) < Grey(open) * 0.6f, $"under the opaque half {shaded}, in the open {open}");
        Assert.True(Grey(lit) > Grey(open) * 0.85f, $"under the clear half {lit}, in the open {open}");
    }

    [Fact]
    public void A_cut_out_casts_only_its_opaque_half_in_a_torchs_shadow_cube()
    {
        var night = new ModernSettings
        {
            SunElevation = 40, SunIntensity = 0.02f, HazeVisibilityMetres = 0, Bloom = false, LensFlare = false, Exposure = 4f,
            AmbientOcclusion = false, SkyOcclusion = false, Volumetric = false, SsrSteps = 0, LightShadows = 4, Fxaa = false,
        };
        var torch = new ModernPointLight(new Vector3(0, 0, 4 * M), new Vector3(10f, 10f, 10f), 12 * M);
        var side = new FreeCamera { Position = new Vector3(0, -12 * M, 6 * M), Yaw = 90, Pitch = -25 };
        var ground = Render(CardOverGround(), night, side, torch);
        // From 4 m up through the card at 2 m, a card point (x, y) shades the ground at (2x, 2y).
        var shaded = ground(new Vector3(-2.4f * M, -2.4f * M, 0)); var lit = ground(new Vector3(2.4f * M, -2.4f * M, 0));
        Assert.True(Grey(shaded) < Grey(lit) * 0.5f, $"under the opaque half {shaded}, under the clear half {lit}");
    }

    [Fact]
    public void A_leaf_lit_from_behind_glows_by_its_translucency()
    {
        var settings = Day();
        // The card stands upright facing −Y, towards the camera; the sun is behind it, 25° up at +Y (with north
        // at yaw 0, a bearing of 270°).
        settings.SunAzimuth = 270f; settings.SunElevation = 25f;
        var camera = new FreeCamera { Position = new Vector3(-1 * M, -8 * M, 2 * M), Yaw = 90, Pitch = 0 };
        var at = new Vector3(-1 * M, 0, 2 * M);
        var through = Render(CardOverGround(0.8f, vertical: true), settings, camera)(at);
        var none = Render(CardOverGround(0f, vertical: true), settings, camera)(at);
        Assert.True(Grey(through) > Grey(none) * 1.3f, $"translucent {through}, opaque {none}");
    }

    /// <summary>The ground and an upright 4 m foliage card (wholly opaque, red) whose vertex normals face −Y, the
    /// camera's side; `turned` winds it the other way, so the camera sees its back. `mask` is its
    /// occlusion-roughness-metal map's alpha, the translucency mask.</summary>
    static GltfScene UprightCard(bool turned, float translucency, byte mask = 255)
    {
        var s = new GltfScene();
        s.Materials.Add(new GltfMaterial { Name = "ground", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1), MetallicFactor = 0, RoughnessFactor = 0.9f });
        int tex = Image(s, 16, 16, (x, y) => (200, 40, 30, 255));
        int orm = Image(s, 16, 16, (x, y) => (255, 204, 0, mask));
        s.Materials.Add(new GltfMaterial
        {
            Name = "leaf", BaseColorTexture = new GltfTextureSlot(tex, 0), MetallicRoughnessTexture = new GltfTextureSlot(orm, 0),
            MetallicFactor = 1, RoughnessFactor = 1, AlphaMode = GltfAlphaMode.Mask, AlphaCutoff = 0.5f, DoubleSided = true,
            Foliage = true, Translucency = translucency, Wind = 0f,
        });
        Quad(s, new Vector3(-10 * M, -10 * M, 0), new Vector3(20 * M, 0, 0), new Vector3(0, 20 * M, 0), 0, 0xffffffff);
        var o = new Vector3(-2 * M, 0, 0.5f * M);
        var u = new Vector3(4 * M, 0, 0);
        var v = new Vector3(0, 0, 3 * M);
        if (turned) Quad(s, o + u, -u, v, 1, 0x00ffffff, normal: -Vector3.UnitY);
        else Quad(s, o, u, v, 1, 0x00ffffff, normal: -Vector3.UnitY);
        return s;
    }

    [Fact]
    public void A_foliage_card_seen_from_behind_is_lit_by_its_own_normal_not_a_turned_one()
    {
        // A tree's leaf cards carry normals bent out of the crown, whichever way a card is wound; seen
        // from behind, a leaf's normal must not turn round, or half a crown faces inward. Sun on the camera's side.
        var settings = Day();
        settings.SunAzimuth = 90f; settings.SunElevation = 25f;
        var camera = new FreeCamera { Position = new Vector3(-1 * M, -8 * M, 2 * M), Yaw = 90, Pitch = 0 };
        var at = new Vector3(-1 * M, 0, 2 * M);
        var front = Render(UprightCard(false, 0f), settings, camera)(at);
        var back = Render(UprightCard(true, 0f), settings, camera)(at);
        Assert.True(Grey(front) > 40f, $"front {front}");
        Assert.True(MathF.Abs(Grey(back) - Grey(front)) < Grey(front) * 0.15f, $"front {front}, back {back}");
    }

    [Fact]
    public void A_leaf_lets_through_what_its_translucency_mask_allows()
    {
        // The atlas keeps a mask in its occlusion-roughness-metal map's alpha: 1 on the blade, 0 on wood.
        var settings = Day();
        settings.SunAzimuth = 270f; settings.SunElevation = 25f;
        var camera = new FreeCamera { Position = new Vector3(-1 * M, -8 * M, 2 * M), Yaw = 90, Pitch = 0 };
        var at = new Vector3(-1 * M, 0, 2 * M);
        var blade = Render(UprightCard(false, 0.8f, 255), settings, camera)(at);
        var wood = Render(UprightCard(false, 0.8f, 0), settings, camera)(at);
        Assert.True(Grey(blade) > Grey(wood) * 1.3f, $"blade {blade}, wood {wood}");
    }

    [Fact]
    public void Foliage_sways_by_the_games_clock_and_one_moment_draws_one_picture()
    {
        // An upright card whose top vertices have a sway weight of 1 and bottom 0, in a strong wind.
        var gltf = CardOverGround(0.5f, vertical: true);
        gltf.Materials[1].Wind = 1f;
        var card = gltf.Primitives[1];
        for (int i = 0; i < card.Vertices.Length; i++)
            card.Vertices[i].Color = card.Vertices[i].Position.Z > 2 * M ? 0xffffffffu : 0x00ffffffu;
        byte[] At(float seconds)
        {
            var s = Day();
            s.FoliageWind = 2f; s.Clouds.WindSpeed = 12f; s.Clouds.Seconds = seconds;
            using var scene = new ModernScene(h.Gpu!, gltf);
            using var renderer = new ModernRenderer(h.Gpu!, s, h.Gpu!.ColorFormat);
            return renderer.RenderToImage(scene, new FreeCamera { Position = new Vector3(0, -8 * M, 2 * M), Yaw = 90, Pitch = 0 }, W, H);
        }
        var a = At(10f);
        Assert.Equal(a, At(10f));
        var b = At(12.5f);
        int changed = 0;
        for (int i = 0; i < a.Length; i += 4) if (Math.Abs(a[i] - b[i]) > 20) changed++;
        Assert.True(changed > 20, $"{changed} pixels moved");
    }

    /// <summary>A ground whose material has a height map of ridges, seen at a glancing angle.</summary>
    static GltfScene RidgedGround(float heightMetres)
    {
        var s = new GltfScene();
        int colour = Image(s, 64, 64, (x, y) => ((byte)(x % 16 < 8 ? 200 : 60), 120, 90, 255));
        int height = Image(s, 64, 64, (x, y) => { byte v = (byte)(127 + 127 * MathF.Sin(x * MathF.PI / 8f)); return (v, v, v, 255); });
        s.Materials.Add(new GltfMaterial
        {
            Name = "stone", BaseColorTexture = new GltfTextureSlot(colour, 0), MetallicFactor = 0, RoughnessFactor = 0.9f,
            HeightTexture = heightMetres > 0 ? new GltfTextureSlot(height, 0) : GltfTextureSlot.None, HeightScale = heightMetres,
        });
        Quad(s, new Vector3(-10 * M, -10 * M, 0), new Vector3(20 * M, 0, 0), new Vector3(0, 20 * M, 0), 0, 0xffffffff, uvScale: 10f);
        return s;
    }

    byte[] Picture(GltfScene gltf, ModernSettings settings)
    {
        using var scene = new ModernScene(h.Gpu!, gltf);
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        return renderer.RenderToImage(scene, new FreeCamera { Position = new Vector3(0, -4 * M, 1 * M), Yaw = 90, Pitch = -20 }, W, H);
    }

    [Fact]
    public void Parallax_is_off_at_low_and_moves_the_texture_at_medium()
    {
        ModernSettings With(ModernQuality q)
        {
            var s = Day();
            ModernQualityPreset.For(q).Apply(s);
            s.Fxaa = false; s.Taa = false; s.SsrSteps = 0;
            return s;
        }
        var flat = Picture(RidgedGround(0f), With(ModernQuality.Low));
        var lowRelief = Picture(RidgedGround(0.08f), With(ModernQuality.Low));
        Assert.Equal(flat, lowRelief);
        var flatMedium = Picture(RidgedGround(0f), With(ModernQuality.Medium));
        var relief = Picture(RidgedGround(0.08f), With(ModernQuality.Medium));
        int changed = 0;
        for (int i = 0; i < relief.Length; i += 4)
            if (Math.Abs(relief[i] - flatMedium[i]) > 16) changed++;
        Assert.True(changed > W * H / 20, $"{changed} pixels changed by parallax");
    }
}
