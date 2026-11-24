// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken in part from src/Vitomir.Engine/Levels/Cells/CellLevel.cs and CellStream.AllLights at b4185ef, reworked for the platform.
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Render.Level;

/// <summary>
/// What the renderer needs of a cooked level package: its manifest, its folder,
/// the glTF that carries its materials, and every light its cells hold. The old engine's
/// <c>CellLevel</c> held these beside the simulation's collision and routes; the platform keeps those in
/// its own space, so the renderer reads the package itself through <c>Vitomir.Formats</c>' L2 readers.
/// </summary>
internal sealed class RenderLevel
{
    /// <summary>The region a gridded cell carries (the cook's <c>CellCook.Region</c>).</summary>
    public const string CellRegion = "cell";

    public required string Name { get; init; }
    public required string Package { get; init; }
    public required L2Manifest Manifest { get; init; }
    /// <summary>Every light in the level (all cells' <c>LITE</c>), read once when the level opens.</summary>
    public IReadOnlyList<L2Light> Lights { get; init; } = [];
    /// <summary>A version-1 package's one mesh (drawn whole from its glTF); null for a streamed level.</summary>
    public L2Mesh? Mesh { get; init; }
    public string? MeshFile => Mesh is null ? null : Path.Combine(Package, Mesh.Asset);
    /// <summary>A package of manifest version 2 or later is streamed by cells round the eye (<see cref="Modern.CellStreamScene"/>).</summary>
    public bool Streamed => Manifest.Version >= L2Manifest.Version2;
    /// <summary>The glTF the cook copied into the package (its materials, and a version-1 level's whole mesh).</summary>
    public string? AssetFile => Manifest.Extras["asset"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var a)
        ? Path.Combine(Package, a) : MeshFile;

    /// <summary>Opens a level package: the manifest, the lights of every cell, and a version-1 level's mesh.</summary>
    public static RenderLevel Load(string packageFolder)
    {
        var package = L2Package.Open(packageFolder);
        var cells = package.Manifest.Cells.Where(c => c.Region == CellRegion).ToList();
        if (cells.Count == 0) throw new InvalidDataException($"{packageFolder}: no cooked cells");
        var lights = new List<L2Light>();
        foreach (var c in cells)
        {
            var cell = L2Cell.ReadFile(Path.Combine(packageFolder, c.File), t => t == L2LightChunk.Tag);
            if (cell.Find(L2LightChunk.Tag) is { } lite) lights.AddRange(L2LightChunk.Decode(lite));
        }
        L2Mesh? mesh = null;
        if (!(package.Manifest.Version >= L2Manifest.Version2))
        {
            var first = L2Cell.ReadFile(Path.Combine(packageFolder, cells[0].File), t => t == L2Mesh.Tag);
            if (first.Find(L2Mesh.Tag) is { Version: L2Mesh.Version1 } m) mesh = L2Mesh.Decode(m);
        }
        return new RenderLevel { Name = package.Manifest.Name, Package = packageFolder, Manifest = package.Manifest, Lights = lights, Mesh = mesh };
    }
}
