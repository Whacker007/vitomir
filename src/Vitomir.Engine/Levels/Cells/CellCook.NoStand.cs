// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// Tops no body is to stand on (ours): a level may say of an object or an area
/// that it is not a place to stand — mostly 1.1's old roofs on the platform, which were never meant to be reached, and a
/// few plot things. Their collision triangles carry <see cref="L2Surface.NoStand"/> and never Walk, so the navigation
/// mesh leaves them out whichever bake runs. An object is named by its mesh (the layout's <c>traversal.noStandMeshes</c>);
/// an area by <c>"stand": "none"</c> on a layout area of any kind, which marks every triangle wholly inside its box (the
/// area holds the top). Unlike no-climb's area, which the platform reads from the manifest at run time
/// (<c>Space/LevelClimb.cs</c>), no-stand's is turned into the triangles' flag here, because the navigation mesh must
/// leave the tops out when it is baked. The area keeps its key in the manifest as any key of an area's.
/// </summary>
public static partial class CellCook
{
    /// <summary>The key an area says it with, and its one word.</summary>
    public const string StandKey = "stand", StandNoneWord = "none";

    /// <summary>How far outside an area's box a triangle's corner may lie and the triangle still be held by it (m): a top modelled flush with the box's side.</summary>
    public const float NoStandAreaTolerance = 0.01f;

    /// <summary>The names of the meshes whose triangles no body is to stand on (ours): the layout's <c>traversal.noStandMeshes</c> list, else none.</summary>
    public static string[] NoStandMeshes(System.Text.Json.Nodes.JsonNode? layout) =>
        layout?["traversal"]?["noStandMeshes"] is System.Text.Json.Nodes.JsonArray list
            ? [.. list.Select(x => x?.GetValue<string>()).OfType<string>()] : [];

    /// <summary>
    /// The areas marked <c>"stand": "none"</c>, in their order. An area whose <c>stand</c> says anything else is a
    /// problem (one line in <paramref name="problems"/>) and counts as unmarked.
    /// </summary>
    public static List<L2Area> NoStandAreas(IReadOnlyList<L2Area>? areas, List<string> problems)
    {
        var found = new List<L2Area>();
        foreach (var a in areas ?? [])
        {
            if (a.Unknown is null || !a.Unknown.TryGetValue(StandKey, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String && v.GetString() == StandNoneWord) found.Add(a);
            else problems.Add($"area {a.Name}: stand is {v.GetRawText()}, not \"{StandNoneWord}\"; the area is taken as unmarked (stood on as its geometry allows)");
        }
        return found;
    }

    /// <summary>
    /// Marks in <paramref name="noStand"/> each triangle (of <paramref name="indices"/> into <paramref name="vertices"/>,
    /// in engine units at <paramref name="unitsPerMetre"/>) whose three corners all lie inside one of
    /// <paramref name="areas"/>' boxes, to <see cref="NoStandAreaTolerance"/>; leaves the others as they are. Returns how
    /// many triangles each area marked, in its order (one marked already, by its mesh or an earlier area, is not counted).
    /// </summary>
    public static int[] MarkNoStandAreas(IReadOnlyList<L2Area> areas, IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices,
                                         float unitsPerMetre, bool[] noStand)
    {
        var counts = new int[areas.Count];
        if (areas.Count == 0) return counts;
        int triangles = Math.Min(noStand.Length, indices.Count / 3);
        for (int t = 0; t < triangles; t++)
        {
            var a = vertices[indices[3 * t]] / unitsPerMetre;
            var b = vertices[indices[3 * t + 1]] / unitsPerMetre;
            var c = vertices[indices[3 * t + 2]] / unitsPerMetre;
            for (int k = 0; k < areas.Count; k++)
            {
                var area = areas[k];
                if (area.Depth(a) < -NoStandAreaTolerance || area.Depth(b) < -NoStandAreaTolerance || area.Depth(c) < -NoStandAreaTolerance) continue;
                if (!noStand[t]) counts[k]++;
                noStand[t] = true;
                break;
            }
        }
        return counts;
    }
}
