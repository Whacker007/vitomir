// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/GltfTilings.cs at b4185ef, reworked for the platform.
using System.Text.Json;
using Vitomir.Formats.L2;
using TextureKind = Vitomir.Platform.Render.Level.TextureKind;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: the tiling flag through the formats** (the levels'
/// shared contract): a glTF material's <c>extras.tiling</c> read into <see cref="GltfTiling"/>, a decal given
/// the tiling of the surface it lies on, and the flag into the cooked <see cref="L2Tiling"/> and back.
/// </summary>
internal static class GltfTilings
{
    /// <summary>
    /// <c>extras.tiling</c>: <c>{"mode": "hex", "rotate": true|false, "scale": ~0.5}</c>; null when absent or not an
    /// object (a plain repeat). A missing scale is ½, a missing rotate false, a missing mode <c>hex</c>.
    /// </summary>
    public static GltfTiling? Read(JsonElement extras)
    {
        if (!extras.TryGetProperty("tiling", out var t) || t.ValueKind != JsonValueKind.Object) return null;
        string mode = t.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "hex";
        bool rotate = t.TryGetProperty("rotate", out var r) && r.ValueKind == JsonValueKind.True;
        float scale = t.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetSingle() : 0.5f;
        int set = t.TryGetProperty("texCoord", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        return new GltfTiling(mode, rotate, Math.Clamp(scale, 0.01f, 4f), set);
    }

    /// <summary>
    /// A stain decal borrows the normal and occlusion-roughness-metal maps of the surface under it through a second
    /// set of texture coordinates (the levels generator's decals). When that surface is hex-tiled, the
    /// decal's borrowed maps must show the same cells, or a stain shows the plain tile's stones through the
    /// surface's shuffled ones: so a decal without a tiling of its own (<c>extras.decal.on</c> naming a material
    /// with one) takes the surface's, laid out on the set it borrows through, with the blend's weights from the
    /// surface's height map. A decal that shares no map with its surface keeps a plain repeat.
    /// </summary>
    public static void ResolveDecals(IReadOnlyList<GltfMaterial> materials)
    {
        foreach (var m in materials)
        {
            if (m.Tiling is not null || m.DecalOn is null) continue;
            var under = materials.FirstOrDefault(x => x.Name == m.DecalOn && x.Tiling is { Hex: true });
            if (under?.Tiling is not { } tiling) continue;
            int set = -1;
            if (m.NormalTexture.Present && m.NormalTexture.Texture == under.NormalTexture.Texture) set = m.NormalTexture.TexCoord;
            else if (m.MetallicRoughnessTexture.Present && m.MetallicRoughnessTexture.Texture == under.MetallicRoughnessTexture.Texture) set = m.MetallicRoughnessTexture.TexCoord;
            else if (m.OcclusionTexture.Present && m.OcclusionTexture.Texture == under.OcclusionTexture.Texture) set = m.OcclusionTexture.TexCoord;
            if (set < 0) continue;
            var weights = under.HeightTexture.Present ? new GltfTextureSlot(under.HeightTexture.Texture, set) : GltfTextureSlot.None;
            m.Tiling = tiling with { TexCoord = set, Weights = weights };
        }
    }

    /// <summary>The cook's <see cref="L2Tiling"/> for a material's tiling, its weights' height map cooked by <paramref name="map"/>; null for a plain repeat.</summary>
    public static L2Tiling? Define(GltfTiling? t, Func<GltfTextureSlot, TextureKind, L2MaterialMap?> map) =>
        t is null ? null : new L2Tiling
        {
            Mode = t.Mode, Rotate = t.Rotate, Scale = t.Scale, TexCoord = t.TexCoord,
            Weights = t.Weights.Present ? map(t.Weights, TextureKind.Height) : null,
        };

    /// <summary>A cooked level's tiling back into the renderer's (the inverse of <see cref="Define"/>), its weights' map through <paramref name="slot"/>.</summary>
    public static GltfTiling? Load(L2Tiling? t, Func<L2MaterialMap?, GltfTextureSlot> slot) =>
        t is null ? null : new GltfTiling(t.Mode, t.Rotate, t.Scale, t.TexCoord) { Weights = slot(t.Weights) };
}
