// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: the skyline's cook**. A city made for distance — the land,
/// the river, the bridge, the castle, the rest of the town to the horizon (the levels generator's city) — is cut into
/// square tiles by each triangle's middle, whole triangles, never clipped: <see cref="NearTile"/> metres
/// within <see cref="NearReach"/> of the level's middle (along either axis), <see cref="FarTile"/> within
/// <see cref="FarReach"/>, <see cref="HorizonTile"/> beyond, so the parts near the level are small enough for
/// the camera's frustum and the sun's shadow cascades to take or leave them, and the far ones few. Each tile becomes one cell file holding one <c>MESH</c>
/// (version 2, a draw per material with its own box), under <c>skyline/</c>. Nothing of it collides, is
/// walked or is anchored: it is never in <c>COLL</c> or <c>NAVM</c>.
/// <para>
/// **Two versions.** The source names what is for near and what for far: a mesh whose name ends
/// <see cref="NearSuffix"/> is only in a tile's near version (its first level of detail), one ending
/// <see cref="FarSuffix"/> only in its far version (the second), every other mesh in both — the city's
/// generator writes each house row, wood and wall twice, the far one simpler (house rows as one mass, woods
/// as one canopy, walls without merlons, all of one untextured material). A tile with nothing named so has
/// one version. The renderer draws the far version of a part whose box is <see cref="L2Skyline.FarFrom"/>
/// metres or more from the camera (<see cref="FarFromMetres"/> unless the cook is told otherwise).
/// </para>
/// </summary>
public static class CellSkyline
{
    /// <summary>The package's folder of skyline parts.</summary>
    public const string Folder = "skyline";
    /// <summary>A near tile's side, metres (ours).</summary>
    public const float NearTile = 500f;
    /// <summary>A far tile's side, metres (ours).</summary>
    public const float FarTile = 2500f;
    /// <summary>How far from the level's middle, along X or Y, the near tiles reach (metres; ours).</summary>
    public const float NearReach = 2000f;
    /// <summary>How far the far tiles reach (metres; ours); the horizon's beyond.</summary>
    public const float FarReach = 7500f;
    /// <summary>A horizon tile's side, metres (ours).</summary>
    public const float HorizonTile = 8000f;
    /// <summary>The end of a mesh's name that keeps it to the near version of its tiles.</summary>
    public const string NearSuffix = " (near)";
    /// <summary>The end of a mesh's name that keeps it to the far version of its tiles.</summary>
    public const string FarSuffix = " (far)";
    /// <summary>
    /// How far from the camera (metres) a part's box must be for its far version to be drawn (ours): a
    /// house of 8 m there is some 7 pixels wide on a 1600-pixel screen at 90°, its row's mass no different.
    /// </summary>
    public const float FarFromMetres = 900f;

    /// <summary>
    /// Which of a tile's versions a mesh named <paramref name="name"/> belongs to: near (0), far (1) or both
    /// (-1). A primitive's number after <c>#</c> (the glTF reader's) is not part of the name.
    /// </summary>
    public static int Version(string? name)
    {
        if (name is null) return -1;
        int hash = name.IndexOf('#');
        var mesh = hash >= 0 ? name.AsSpan(0, hash) : name.AsSpan();
        return mesh.EndsWith(NearSuffix, StringComparison.Ordinal) ? 0 : mesh.EndsWith(FarSuffix, StringComparison.Ordinal) ? 1 : -1;
    }

    /// <summary>
    /// The tile a triangle whose middle is at <paramref name="p"/> (metres) belongs to, about the level's
    /// middle <paramref name="centre"/>: its size, column and row.
    /// </summary>
    public static (float Size, int Column, int Row) Tile(Vector2 p, Vector2 centre)
    {
        var d = p - centre;
        float reach = MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y));
        float size = reach < NearReach ? NearTile : reach < FarReach ? FarTile : HorizonTile;
        return (size, (int)MathF.Floor(d.X / size), (int)MathF.Floor(d.Y / size));
    }

    /// <summary>
    /// The level meshes a skyline stands for, which the cook leaves out of the level: the <c>replaces</c>
    /// list of the generator's note beside the glb (<c>city.json</c> beside <c>city.glb</c>), none without one.
    /// </summary>
    public static string[] Replaces(string skylineGlb)
    {
        var note = Path.ChangeExtension(skylineGlb, ".json");
        if (!File.Exists(note)) return [];
        return JsonNode.Parse(File.ReadAllText(note))?["replaces"] is JsonArray list
            ? [.. list.Select(n => (string?)n).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)]
            : [];
    }

    /// <summary>
    /// Cooks the skyline's meshes (metres, Z up, materials already counted in the level's list) into
    /// <c>skyline/*.cell</c> under <paramref name="packageFolder"/> and copies the source glb to <c>assets/</c>;
    /// returns the manifest's entry.
    /// </summary>
    public static L2Skyline Cook(IReadOnlyList<CookMesh> meshes, Vector2 centre, string source, int firstMaterial, int materialCount,
                                 IEnumerable<string> replaces, string packageFolder, float farFrom = FarFromMetres)
    {
        var version = meshes.Select(m => Version(m.Name)).ToArray();
        // Every triangle to its tile, by its middle.
        var tiles = new Dictionary<(float, int, int), List<(int Mesh, int Triangle)>>();
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            for (int t = 0; t < mesh.Indices.Length / 3; t++)
            {
                var a = mesh.Vertices[mesh.Indices[3 * t]].Position;
                var b = mesh.Vertices[mesh.Indices[3 * t + 1]].Position;
                var c = mesh.Vertices[mesh.Indices[3 * t + 2]].Position;
                var mid = (a + b + c) / 3f;
                var key = Tile(new Vector2(mid.X, mid.Y), centre);
                if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = [];
                list.Add((m, t));
            }
        }
        Directory.CreateDirectory(Path.Combine(packageFolder, Folder));
        var parts = new List<L2SkylinePart>();
        var all = (Min: new Vector3(float.MaxValue), Max: new Vector3(float.MinValue));
        long triangles = 0, farTriangles = 0;
        foreach (var ((size, column, row), list) in tiles.OrderBy(t => t.Key.Item1).ThenBy(t => t.Key.Item3).ThenBy(t => t.Key.Item2))
        {
            // The near version leaves out what is only for far, the far one what is only for near.
            bool two = list.Any(e => version[e.Mesh] >= 0);
            var lods = two
                ? new[] { Mesh(meshes, [.. list.Where(e => version[e.Mesh] != 1)]), Mesh(meshes, [.. list.Where(e => version[e.Mesh] != 0)]) }
                : [Mesh(meshes, list)];
            var boxes = lods.SelectMany(l => l.Draws).ToList();
            var min = boxes.Select(d => d.Min).Aggregate(Vector3.Min);
            var max = boxes.Select(d => d.Max).Aggregate(Vector3.Max);
            all = (Vector3.Min(all.Min, min), Vector3.Max(all.Max, max));
            var id = $"s{column}_{row}_{size:0}";
            var file = $"{Folder}/{id}.cell";
            var bytes = new L2Cell { Chunks = [new L2MeshBuffers { Lods = [.. lods] }.Encode()] }.Write();
            File.WriteAllBytes(Path.Combine(packageFolder, file), bytes);
            parts.Add(new L2SkylinePart
            {
                Id = id, File = file, Tile = size, Triangles = lods[0].Triangles, FarTriangles = lods[^1].Triangles, Bytes = bytes.Length,
                Bounds = new L2Box { Min = [min.X, min.Y, min.Z], Max = [max.X, max.Y, max.Z] },
            });
            triangles += lods[0].Triangles;
            farTriangles += lods[^1].Triangles;
        }
        var glb = File.ReadAllBytes(source);
        Directory.CreateDirectory(Path.Combine(packageFolder, "assets"));
        File.WriteAllBytes(Path.Combine(packageFolder, "assets", Path.GetFileName(source)), glb);
        return new L2Skyline
        {
            Asset = $"assets/{Path.GetFileName(source)}", Sha256 = Convert.ToHexStringLower(SHA256.HashData(glb)),
            Bounds = parts.Count == 0 ? new L2Box() : new L2Box { Min = [all.Min.X, all.Min.Y, all.Min.Z], Max = [all.Max.X, all.Max.Y, all.Max.Z] },
            FirstMaterial = firstMaterial, MaterialCount = materialCount, Triangles = triangles, FarTriangles = farTriangles, FarFrom = farFrom,
            Replaces = [.. replaces], Parts = parts,
        };
    }

    /// <summary>One tile's triangles as one level of detail: a draw per material, vertices shared within each (none for none).</summary>
    static L2MeshLod Mesh(IReadOnlyList<CookMesh> meshes, List<(int Mesh, int Triangle)> list)
    {
        var verts = new List<L2Vertex>();
        var idx = new List<uint>();
        var draws = new List<L2MeshDraw>();
        foreach (var group in list.GroupBy(e => meshes[e.Mesh].Material).OrderBy(g => g.Key))
        {
            var at = new Dictionary<(int, uint), uint>();
            int first = idx.Count;
            var dmin = new Vector3(float.MaxValue); var dmax = new Vector3(float.MinValue);
            foreach (var (m, t) in group)
            {
                var mesh = meshes[m];
                for (int corner = 0; corner < 3; corner++)
                {
                    uint v = mesh.Indices[3 * t + corner];
                    if (!at.TryGetValue((m, v), out uint n))
                    {
                        n = (uint)verts.Count; at[(m, v)] = n;
                        verts.Add(mesh.Vertices[v]);
                        dmin = Vector3.Min(dmin, mesh.Vertices[v].Position); dmax = Vector3.Max(dmax, mesh.Vertices[v].Position);
                    }
                    idx.Add(n);
                }
            }
            draws.Add(new L2MeshDraw(group.Key, first, idx.Count - first, dmin, dmax, 0f));
        }
        var vArr = verts.ToArray(); var iArr = idx.ToArray();
        for (int d = 0; d < draws.Count; d++)
            draws[d] = draws[d] with { UvDensity = MeshSimplify.UvDensity(vArr, iArr, draws[d].FirstIndex, draws[d].IndexCount) };
        return new L2MeshLod { Vertices = vArr, Indices = iArr, Draws = draws };
    }
}
