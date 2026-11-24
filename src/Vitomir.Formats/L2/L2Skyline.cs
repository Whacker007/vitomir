// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a cell level's skyline** (manifest key <c>skyline</c>). The
/// city round the level — land, river, bridge, castle, the rest of the town, to the horizon — as drawn
/// geometry only: no collision, no navigation, no anchors. It is cut into <see cref="Parts"/>, square
/// tiles of the ground whose size grows with distance from the level; each part is a cell file holding
/// one <c>MESH</c> (version 2) with a draw per material, its draws' boxes tight: one level of detail, or
/// two — the near version and a far one, simpler, drawn from <see cref="FarFrom"/> metres off. Its
/// materials are the level's own, appended to the manifest's <c>materials</c> from
/// <see cref="FirstMaterial"/> for <see cref="MaterialCount"/>. A build that does not know the key keeps it
/// and draws the level without it.
/// </summary>
public sealed class L2Skyline
{
    /// <summary>The skyline's source glTF, copied into <c>assets/</c> for re-cooking.</summary>
    [JsonPropertyName("asset")] public string Asset { get; set; } = "";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("bounds")] public L2Box Bounds { get; set; } = new();
    /// <summary>The first of the skyline's materials in the manifest's <c>materials</c>.</summary>
    [JsonPropertyName("firstMaterial")] public int FirstMaterial { get; set; }
    [JsonPropertyName("materialCount")] public int MaterialCount { get; set; }
    [JsonPropertyName("triangles")] public long Triangles { get; set; }
    /// <summary>The far versions' triangles, all parts (those with one version count it twice: theirs is both).</summary>
    [JsonPropertyName("farTriangles")] public long FarTriangles { get; set; }
    /// <summary>How far from the camera (metres) a part's box must be for its far version to be drawn (ours; 0: never).</summary>
    [JsonPropertyName("farFrom")] public float FarFrom { get; set; }
    /// <summary>The level's own meshes the skyline stands for, left out of the level by the cook (the
    /// Old Town generator's horizon plane, <c>fields</c>).</summary>
    [JsonPropertyName("replaces")] public List<string> Replaces { get; set; } = [];
    [JsonPropertyName("parts")] public List<L2SkylinePart> Parts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>One tile of a <see cref="L2Skyline"/>: its cell file (<c>skyline/s&lt;column&gt;_&lt;row&gt;_&lt;size&gt;.cell</c>).</summary>
public sealed class L2SkylinePart
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("bounds")] public L2Box Bounds { get; set; } = new();
    /// <summary>The tile's side, metres: the ground it gathers by its triangles' middles.</summary>
    [JsonPropertyName("tile")] public float Tile { get; set; }
    [JsonPropertyName("triangles")] public long Triangles { get; set; }
    /// <summary>The far version's triangles: the cell's second level of detail, or its only one.</summary>
    [JsonPropertyName("farTriangles")] public long FarTriangles { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}
