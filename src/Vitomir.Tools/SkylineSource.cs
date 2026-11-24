// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir;

/// <summary>
/// **Ours.** <c>vitomir --cook-l2 … --skyline city.glb</c>: the city round a cell level, drawn only.
/// Read before the level is cooked — its materials, textures and
/// images are appended to the level's scene so they are cooked with the level's and named in its
/// manifest, and the level's meshes its note (<c>city.json</c>) says it replaces are taken out of the
/// level — and cooked after (<see cref="CellSkyline.Cook"/>), its entry written into the manifest.
/// </summary>
public sealed class SkylineSource
{
    readonly string path;
    readonly GltfScene scene;
    readonly int firstMaterial;
    readonly string[] replaces;

    SkylineSource(string path, GltfScene scene, int firstMaterial, string[] replaces)
    {
        this.path = path; this.scene = scene; this.firstMaterial = firstMaterial; this.replaces = replaces;
    }

    /// <summary>Reads the skyline and joins its materials to <paramref name="level"/>'s; takes out the meshes it replaces.</summary>
    public static SkylineSource Read(string path, GltfScene level)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var sky = GltfReader.Load(path, new GltfReadOptions(DeferImages: true));
        var replaces = CellSkyline.Replaces(path);
        int dropped = level.Primitives.RemoveAll(p => replaces.Contains(p.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase));
        int first = CellSkylineDraws.AppendMaterials(level, sky);
        Console.WriteLine($"cook: skyline {path}: {sky.Primitives.Count} primitives, {sky.TriangleCount:N0} triangles, {sky.Materials.Count} materials " +
                          $"(the level's from {first}); {dropped} level primitives it replaces ({string.Join(", ", replaces)}) left out " +
                          $"({watch.Elapsed.TotalSeconds:0.0}s)");
        return new SkylineSource(path, sky, first, replaces);
    }

    /// <summary>Cooks the skyline's parts into the package and writes its entry into the manifest.</summary>
    public void Cook(string output)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const float Scale = L2Manifest.EngineUnitsPerMetre;
        var meshes = new List<CookMesh>(scene.Primitives.Count);
        foreach (var p in scene.Primitives)
        {
            var vs = System.Runtime.InteropServices.MemoryMarshal.Cast<ModernVertex, L2Vertex>(p.Vertices).ToArray();
            for (int i = 0; i < vs.Length; i++) vs[i].Position /= Scale;
            meshes.Add(new CookMesh(p.Name, firstMaterial + Math.Max(0, p.Material), vs, p.Indices));
        }
        var package = L2Package.Open(output);
        var b = package.Manifest.Bounds;
        var centre = new Vector2((b.Min[0] + b.Max[0]) * 0.5f, (b.Min[1] + b.Max[1]) * 0.5f);
        var entry = CellSkyline.Cook(meshes, centre, path, firstMaterial, scene.Materials.Count, replaces, output);
        package.Manifest.Skyline = entry;
        File.WriteAllText(Path.Combine(output, L2Package.ManifestFile), package.Manifest.Write(), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"cook: skyline: {entry.Parts.Count} parts ({entry.Parts.Count(p => p.Tile == CellSkyline.NearTile)} of {CellSkyline.NearTile:0} m, " +
                          $"{entry.Parts.Count(p => p.Tile == CellSkyline.FarTile)} of {CellSkyline.FarTile:0} m, {entry.Parts.Count(p => p.Tile == CellSkyline.HorizonTile)} of {CellSkyline.HorizonTile:0} m), " +
                          $"{entry.Triangles:N0} triangles near, {entry.FarTriangles:N0} far (from {entry.FarFrom:0} m), " +
                          $"{entry.Parts.Sum(p => p.Bytes) / 1048576.0:0.0} MB, out to {MathF.Max(MathF.Max(-entry.Bounds.Min[0], entry.Bounds.Max[0]), MathF.Max(-entry.Bounds.Min[1], entry.Bounds.Max[1])) / 1000:0.0} km " +
                          $"({watch.Elapsed.TotalSeconds:0.0}s)");
    }
}
