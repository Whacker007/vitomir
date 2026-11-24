// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: what became of a 1.1 scene's things in a cell level, written out**
/// (<c>vitomir --level-l2 &lt;package&gt; --cell-scene-report &lt;out.json&gt;</c>). One record a
/// placed thing, from <see cref="CellLevel.Placements"/>: its scene and number, template, type, script, where
/// 1.1 has it and where it stands now (metres), how it was placed, whether there is walk ground under it,
/// whether its body meets the new level's solid triangles, how much a ground lift added to its 1.1 height
/// (<c>lift</c>), whether the drop rule took it and why it may
/// never be dropped. The levels generator's scene report reads it beside the generator's layout and writes
/// the table of what stands where, what sits in a new wall and what the new town already has.
/// </summary>
public static class CellSceneReport
{
    /// <summary>Writes the report for <paramref name="place"/> to <paramref name="path"/>; nothing for a 1.1 level or a null path.</summary>
    public static void Write(Location place, string? path)
    {
        if (path is null || CellLevel.Of(place.Level) is not { } cells) return;
        float scale = cells.Manifest.Units.EngineUnitsPerMetre;
        JsonArray V(System.Numerics.Vector3 v) => [MathF.Round(v.X / scale, 4), MathF.Round(v.Y / scale, 4), MathF.Round(v.Z / scale, 4)];
        var things = new JsonArray();
        foreach (var p in cells.Placements)
            things.Add(new JsonObject
            {
                ["id"] = p.Id, ["scene"] = p.Scene, ["index"] = p.Index, ["template"] = p.Template, ["type"] = p.Type.ToString(),
                ["script"] = p.Script, ["from"] = V(p.From), ["at"] = V(p.At), ["how"] = p.How, ["overWalk"] = p.OverWalk,
                ["inside"] = p.Inside, ["radius"] = MathF.Round(p.Radius / scale, 3), ["height"] = MathF.Round(p.Height / scale, 3),
                ["protected"] = p.Protected, ["dropped"] = p.Dropped, ["anchored"] = p.Anchored,
                ["lift"] = MathF.Round(p.Lift / scale, 4),
            });
        var root = new JsonObject
        {
            ["level"] = cells.Name, ["standsFor"] = cells.Manifest.Scene?.Level, ["chapter"] = place.SceneFile,
            ["drop"] = new JsonArray([.. (cells.Manifest.Scene?.Drop ?? []).Select(d => (JsonNode)d)]),
            ["things"] = things,
        };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"cell scene: report of {cells.Placements.Count} things written to {path}");
    }
}
