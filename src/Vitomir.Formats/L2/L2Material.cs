// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>One map of a material: its cooked texture (<c>textures/&lt;name&gt;.ktx2</c>), texture coordinate set and wrapping.</summary>
public sealed class L2MaterialMap
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("texCoord")] public int TexCoord { get; set; }
    [JsonPropertyName("repeatU")] public bool RepeatU { get; set; } = true;
    [JsonPropertyName("repeatV")] public bool RepeatV { get; set; } = true;
    [JsonPropertyName("mirrorU")] public bool MirrorU { get; set; }
    [JsonPropertyName("mirrorV")] public bool MirrorV { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>
/// **Ours: a foliage material's extras** — leaves, grass, weeds and ivy, cut out (<c>MASK</c>) and
/// two-sided: how much of the sun's light comes through a leaf (0..1) and how much the wind sways it
/// (0..1, times each vertex's sway weight, its colour's alpha).
/// </summary>
public sealed class L2Foliage
{
    [JsonPropertyName("translucency")] public float Translucency { get; set; }
    [JsonPropertyName("wind")] public float Wind { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>
/// **Ours: how a material's maps tile** — the glTF material's <c>extras.tiling</c>. Absent, the maps
/// repeat plainly. <c>hex</c>: hex-tile stochastic sampling (Mikkelsen 2022, after Heitz and Neyret 2018) — the
/// surface is cut into hexagonal cells, each showing the maps at a random offset (and, with <see cref="Rotate"/>,
/// a random rotation), blended across the cells' edges, so a large surface shows no repeat.
/// </summary>
public sealed class L2Tiling
{
    /// <summary><c>hex</c>; any other word draws as a plain repeat.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "hex";
    /// <summary>Whether each cell also turns the maps by a random angle (and the normals back with it).</summary>
    [JsonPropertyName("rotate")] public bool Rotate { get; set; }
    /// <summary>The distance between neighbouring cells' centres, as a fraction of the maps' tile (about ½).</summary>
    [JsonPropertyName("scale")] public float Scale { get; set; } = 0.5f;
    /// <summary>The texture coordinate set the cells are laid out in; only maps read through it are tiled (a decal's borrowed maps are on 1).</summary>
    [JsonPropertyName("texCoord")] public int TexCoord { get; set; }
    /// <summary>
    /// For a material without a height map of its own on that set (a decal over cobbles): the height map whose
    /// stones the blend between cells follows — the surface's under it — so its cells match the surface's.
    /// </summary>
    [JsonPropertyName("weights")] public L2MaterialMap? Weights { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    [JsonIgnore] public bool Hex => Mode == "hex";
}

/// <summary>
/// **Ours (Phase 3): a material definition**, <c>materials/&lt;name&gt;.json</c> — the
/// glTF metallic-roughness model's factors and maps, with the maps cooked to KTX2 (<see cref="Ktx2"/>:
/// base colour and emission BC7 sRGB, metal-roughness and occlusion BC7 linear, normals BC5, height BC4), and what
/// the cook made of the surface (ground, passable). The manifest's <c>materialFiles</c> lists them in
/// the order of its <c>materials</c>, so a draw's material number finds its file.
/// </summary>
public sealed class L2Material
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("baseColorFactor")] public float[] BaseColorFactor { get; set; } = [1, 1, 1, 1];
    [JsonPropertyName("baseColorTexture")] public L2MaterialMap? BaseColorTexture { get; set; }
    [JsonPropertyName("metallicFactor")] public float MetallicFactor { get; set; } = 1f;
    [JsonPropertyName("roughnessFactor")] public float RoughnessFactor { get; set; } = 1f;
    [JsonPropertyName("metallicRoughnessTexture")] public L2MaterialMap? MetallicRoughnessTexture { get; set; }
    [JsonPropertyName("normalTexture")] public L2MaterialMap? NormalTexture { get; set; }
    [JsonPropertyName("normalScale")] public float NormalScale { get; set; } = 1f;
    [JsonPropertyName("occlusionTexture")] public L2MaterialMap? OcclusionTexture { get; set; }
    [JsonPropertyName("occlusionStrength")] public float OcclusionStrength { get; set; } = 1f;
    [JsonPropertyName("emissiveFactor")] public float[] EmissiveFactor { get; set; } = [0, 0, 0];
    [JsonPropertyName("emissiveTexture")] public L2MaterialMap? EmissiveTexture { get; set; }
    [JsonPropertyName("emissiveStrength")] public float EmissiveStrength { get; set; } = 1f;
    [JsonPropertyName("specularFactor")] public float SpecularFactor { get; set; } = 1f;
    /// <summary>A height map (ours; the glTF material's <c>extras.height</c>): 1 the highest, drawn with parallax occlusion.</summary>
    [JsonPropertyName("heightTexture")] public L2MaterialMap? HeightTexture { get; set; }
    /// <summary>The metres the height map's full range spans (0: no relief).</summary>
    [JsonPropertyName("heightScale")] public float HeightScale { get; set; }
    /// <summary>Leaves and grass (ours; the glTF material's <c>extras.foliage</c>); null for anything else.</summary>
    [JsonPropertyName("foliage")] public L2Foliage? Foliage { get; set; }
    /// <summary>How the maps tile (ours; the glTF material's <c>extras.tiling</c>); null for a plain repeat, as every older package's.</summary>
    [JsonPropertyName("tiling")] public L2Tiling? Tiling { get; set; }
    /// <summary><c>OPAQUE</c>, <c>MASK</c> or <c>BLEND</c>, as glTF's.</summary>
    [JsonPropertyName("alphaMode")] public string AlphaMode { get; set; } = "OPAQUE";
    [JsonPropertyName("alphaCutoff")] public float AlphaCutoff { get; set; } = 0.5f;
    [JsonPropertyName("doubleSided")] public bool DoubleSided { get; set; }
    /// <summary>Whether the cook took the surface for ground (walkable when it faces up) and for passable (no collision).</summary>
    [JsonPropertyName("ground")] public bool Ground { get; set; }
    [JsonPropertyName("passable")] public bool Passable { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    public string Write() => JsonSerializer.Serialize(this, L2Manifest.Json);
    public static L2Material Read(string text, string name = "material") =>
        JsonSerializer.Deserialize<L2Material>(text, L2Manifest.Json) ?? throw new NodFormatException($"{name}: empty material");

    /// <summary>The material's maps and whether each is colour (sRGB).</summary>
    public IEnumerable<(L2MaterialMap Map, bool Srgb)> Maps()
    {
        if (BaseColorTexture is { } b) yield return (b, true);
        if (MetallicRoughnessTexture is { } mr) yield return (mr, false);
        if (NormalTexture is { } n) yield return (n, false);
        if (OcclusionTexture is { } o) yield return (o, false);
        if (EmissiveTexture is { } e) yield return (e, true);
        if (HeightTexture is { } h) yield return (h, false);
        if (Tiling?.Weights is { } w) yield return (w, false);
    }
}
