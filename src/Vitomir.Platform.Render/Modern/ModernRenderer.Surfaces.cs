// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Surfaces.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: what a material asks of the surface shaders beyond glTF's model** — its relief
/// (parallax occlusion from a height map), foliage's sway and light through the leaf, the large-scale
/// variation against tiling, the mark the prepass leaves for screen-space reflections, and hex
/// tiling.
/// </summary>
internal sealed unsafe partial class ModernRenderer
{
    Matrix4x4 surfaceViewProj;

    /// <summary>
    /// The vertex constants' sway for a material (ModernFoliage.hlsli): its extras' wind times
    /// <see cref="ModernSettings.FoliageWind"/> and the wind's speed (6 m/s, the clouds' default, is 1; capped
    /// at 2), the game's clock (the clouds', so a time gives one picture), and the direction the wind blows
    /// towards. Zero for anything that is not foliage.
    /// </summary>
    Vector4 SwayFor(GltfMaterial m)
    {
        if (!m.Foliage || m.Wind <= 0f || Settings.FoliageWind <= 0f) return Vector4.Zero;
        var c = Settings.Clouds;
        float strength = m.Wind * Settings.FoliageWind * Math.Clamp(c.WindSpeed / 6f, 0f, 2f);
        float yaw = (Settings.NorthYaw - (c.WindFrom + 180f)) * MathF.PI / 180f;
        float clock = (float)(((double)c.Seconds + c.ClockOffset) % 3600.0);
        return new Vector4(strength, clock, MathF.Cos(yaw), MathF.Sin(yaw));
    }

    /// <summary>The material constants' relief: the height map's span in engine units, the steps, the fade distance and self-shadowing.</summary>
    Vector4 ReliefFor(GltfMaterial m)
    {
        if (!m.HeightTexture.Present || m.HeightScale <= 0f || Settings.ParallaxSteps <= 0) return Vector4.Zero;
        return new Vector4(m.HeightScale * ModernMath.UnitsPerMetre, Settings.ParallaxSteps,
                           MathF.Max(Settings.ParallaxMetres, 1f) * ModernMath.UnitsPerMetre, Settings.ParallaxShadow ? 1 : 0);
    }

    /// <summary>Whether the large-scale variation goes over a material: the world's opaque surfaces, not water, glow or glass.</summary>
    bool MacroFor(GltfMaterial m) =>
        Settings.MacroVariation && !ModernWater.IsWater(m) && !Glows(m) && m.AlphaMode != GltfAlphaMode.Blend;

    /// <summary>
    /// Hex tiling's constants for a material (ModernHex.hlsli; ours): the cells per texture unit (0: a plain
    /// repeat), whether they turn (+2 when the occlusion map is the metal-roughness map, read once), the coordinate set
    /// they lie on, and what weights their blend — the height map on that set (the material's own, or for a decal its
    /// surface's), else the base colour's brightness when it is on that set, else the cells' places alone. Only an
    /// opaque or blended material that asks (<c>extras.tiling</c> <c>hex</c>) gets it, when <see cref="ModernSettings.HexTiling"/>
    /// is on: not a cut-out (its alpha's shape must stay whole and the same in every pass), not foliage, not water.
    /// </summary>
    Vector4 TilingFor(GltfMaterial m)
    {
        if (!Settings.HexTiling || m.Tiling is not { Hex: true } t || m.Foliage || m.AlphaMode == GltfAlphaMode.Mask || ModernWater.IsWater(m))
            return Vector4.Zero;
        int set = t.TexCoord;
        float weights = m.HeightTexture.Present ? (m.HeightTexture.TexCoord == set ? 1 : 0)
                      : t.Weights.Present ? 1
                      : 0;
        if (weights == 0 && m.BaseColorTexture.Present && m.BaseColorTexture.TexCoord == set) weights = 2;
        bool occlusionIsMr = m.OcclusionTexture.Present && m.OcclusionTexture == m.MetallicRoughnessTexture;
        return new Vector4(1f / t.Scale, (t.Rotate ? 1 : 0) + (occlusionIsMr ? 2 : 0), set, weights);
    }

    /// <summary>What the surface shader's height slot holds: the material's height map, else a decal's surface's, which weights its hex tiling's blend.</summary>
    static GltfTextureSlot ReliefOrWeights(GltfMaterial m) =>
        m.HeightTexture.Present ? m.HeightTexture : m.Tiling?.Weights ?? GltfTextureSlot.None;

    /// <summary>
    /// The prepass's mark for screen-space reflections (ModernPrepass.hlsl's Rough): the roughness factor, its
    /// map's coordinate set, whether open water, and the roughness above which nothing is traced (0: off).
    /// </summary>
    Vector4 ReflectionMark(GltfMaterial m)
    {
        if (!SsrOn || m.Foliage || Glows(m)) return Vector4.Zero;
        return new Vector4(m.RoughnessFactor, m.MetallicRoughnessTexture.TexCoord, ModernWater.IsWater(m) ? 1 : 0, Settings.SsrMaxRoughness);
    }
}
