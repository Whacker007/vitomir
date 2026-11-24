// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// Meshes that are collision and navigation only (ours). A level's stairs are drawn as
/// steps but walked as a smooth flight: the flight's mesh is kept for the collision triangles and the
/// navigation mesh like any other, and left out of what the cells draw.
/// </summary>
public static partial class CellCook
{
    /// <summary>
    /// The names of the meshes that stop and carry a walker but are not drawn (ours): the layout's
    /// <c>ground.collisionOnlyMeshes</c> list, else none.
    /// </summary>
    public static string[] CollisionOnlyMeshes(System.Text.Json.Nodes.JsonNode? layout) =>
        layout?["ground"]?["collisionOnlyMeshes"] is System.Text.Json.Nodes.JsonArray list
            ? [.. list.Select(x => x?.GetValue<string>()).OfType<string>()] : [];

    /// <summary>
    /// Whether a primitive goes into the cells' <c>MESH</c> buffers (ours): every one does but those of a
    /// collision-only mesh. A primitive is named "&lt;mesh&gt;#&lt;index&gt;" (the glTF reader's naming).
    /// </summary>
    public static bool Drawn(string primitive, string[] collisionOnly) =>
        !collisionOnly.Contains(primitive.Split('#')[0], StringComparer.OrdinalIgnoreCase);
}
