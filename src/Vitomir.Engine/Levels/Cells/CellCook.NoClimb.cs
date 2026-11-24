// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// Meshes that are not to be climbed (ours): the geometry decides what can be
/// climbed, and a level may forbid it on an object — a cart's top, a quest's edge, a roof that must not be reached —
/// instead of standing an invisible blocker there. Their collision triangles carry <see cref="Formats.L2.L2Surface.NoClimb"/>;
/// the same on an area is the area's <c>"climb": "none"</c>, which the cook keeps in the manifest as it keeps any key
/// of an area's.
/// </summary>
public static partial class CellCook
{
    /// <summary>The names of the meshes whose triangles are not to be climbed (ours): the layout's <c>traversal.noClimbMeshes</c> list, else none.</summary>
    public static string[] NoClimbMeshes(System.Text.Json.Nodes.JsonNode? layout) =>
        layout?["traversal"]?["noClimbMeshes"] is System.Text.Json.Nodes.JsonArray list
            ? [.. list.Select(x => x?.GetValue<string>()).OfType<string>()] : [];
}
