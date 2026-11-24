// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Detour;
using Vitomir.Formats;
using Vitomir.Formats.L2;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: a cell level loaded** — the region kind that is not 1.1's. It
/// carries the cooked chunks in engine units and the <b>shell</b> the rest of the game is handed in
/// place of a <c>.nil</c>: a <see cref="NilLevel"/> of one sector as big as the level, with no
/// collision tree and nothing to draw, whose walkable surfaces and path nodes are the navigation
/// mesh's polygons. So the walk map, the route search, the sectors of sounds and bodies and the
/// model lighting all work on a cell level as they are, and what is new answers from here:
/// the ground's height (<see cref="Floor"/>, from <c>COLL</c>), what a ray meets
/// (<see cref="Collision"/>), the world's drawing (<see cref="Mesh"/>, by the modern renderer).
///
/// A level that is one is found from its shell with <see cref="Of"/>; for a 1.1 level that is null
/// and nothing changes.
/// </summary>
public sealed partial class CellLevel
{
    static readonly ConditionalWeakTable<NilLevel, CellLevel> byShell = new();

    /// <summary>The cell level a shell stands for, or null for any other level (every 1.1 level).</summary>
    public static CellLevel? Of(NilLevel? level) => level is not null && byShell.TryGetValue(level, out var c) ? c : null;

    public required string Name { get; init; }
    public required string Package { get; init; }
    public required L2Manifest Manifest { get; init; }
    public required NilLevel Shell { get; init; }
    /// <summary>
    /// The simulation's cells (ours, Phase 3): loaded round the player on a background thread and taken
    /// in at the top of a frame (<see cref="CellStream.Update"/>, from <c>Simulation.Step</c>).
    /// </summary>
    public required CellStream Stream { get; init; }
    /// <summary>The loaded cells' collision as one (<see cref="CellCollision.Merge"/>), in engine units; it changes as cells come and go.</summary>
    public CellCollision Collision => Stream.Collision;
    /// <summary>The first loaded navigation-mesh tile, in Detour's model (Recast's frame, metres) — a one-cell level's only tile — or null.</summary>
    public DtMeshData? NavTile => Stream.FirstTile;
    /// <summary>Routes over the navigation mesh by Detour (<see cref="CellRoutes"/>), which the route search hands a cell level's searches to.</summary>
    public CellRoutes? Routes { get; init; }
    /// <summary>The scene the level stands in for a <c>.nsd</c>: its player starts, from the anchors.</summary>
    public required NsdScene Scene { get; init; }
    /// <summary>What became of each thing of the 1.1 scene the level carries, as placed at the last load (ours; <see cref="CellScene"/>).</summary>
    public List<CellScenePlacement> Placements { get; } = [];
    /// <summary>Every light of the level (all its cells' <c>LITE</c>).</summary>
    public IReadOnlyList<L2Light> Lights { get; init; } = [];
    /// <summary>
    /// A package cooked before Phase 3: its one cell's <c>MESH</c> version 1, a reference to the glTF asset
    /// (<see cref="MeshFile"/>, its full path), which the renderer reads whole. Null for a streamed level,
    /// whose cells carry GPU-ready buffers (<see cref="Streamed"/>).
    /// </summary>
    public L2Mesh? Mesh { get; init; }
    public string? MeshFile => Mesh is null ? null : Path.Combine(Package, Mesh.Asset);
    /// <summary>Whether the level's cells carry <c>MESH</c> version 2 buffers, drawn by the renderer's streamer (manifest version 2).</summary>
    public bool Streamed => Manifest.Version >= L2Manifest.Version2;
    /// <summary>The glTF the level was cooked from, kept in the package (materials and their images, for the renderer until textures are cooked), or null.</summary>
    public string? AssetFile => Manifest.Extras["asset"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var a)
        ? Path.Combine(Package, a) : MeshFile;

    /// <summary>How far above or below a walk polygon's detail mesh the true ground is looked for (engine units; <see cref="CellStream.HeightOn"/>).</summary>
    public const float FloorWindow = 40f;

    /// <summary>
    /// The height of the ground under (<paramref name="x"/>, <paramref name="y"/>) nearest
    /// <paramref name="z"/>, from the collision triangles; <paramref name="z"/> itself when there is
    /// none within <see cref="FloorWindow"/>. The walk map's pieces of ground are navigation-mesh
    /// polygons, whose detail mesh is up to a Recast voxel off the cobbles; this is what puts
    /// feet on the cobbles themselves.
    /// </summary>
    public float Floor(float x, float y, float z) => Collision.Floor(x, y, z, FloorWindow) ?? z;

    /// <summary>A walk map built on the shell (<c>WalkMap</c>'s constructor calls this): the stream fills its ground in and keeps it up.</summary>
    public void Attach(WalkMap map) => Stream.Attach(map);

    /// <summary>
    /// Opens a cooked package: the cells round the player's start are loaded at once, the rest stream
    /// in by <paramref name="options"/> (<see cref="CellStreamOptions.FromEnvironment"/> unless given).
    /// </summary>
    public static CellLevel Load(string packageFolder, CellStreamOptions? options = null)
    {
        var package = L2Package.Open(packageFolder);
        if (!package.Manifest.Cells.Any(c => c.Region == CellCook.Region))
            throw new NodFormatException($"{packageFolder}: no cooked cells");
        float scale = package.Manifest.Units.EngineUnitsPerMetre;
        options ??= CellStreamOptions.FromEnvironment();
        // A level that carries a 1.1 scene has its people standing all over it: the whole simulation is in (ours).
        if (package.Manifest.Scene is not null) options.Everything = true;
        var stream = new CellStream(package, options);
        var lights = stream.AllLights();
        // The start the level opens round: the asked entrance's first (ours), else the level's first.
        var startAnchor = (options.Entrance >= 0 ? package.Manifest.Anchors.FirstOrDefault(a => a.Kind == "start" && a.Group == (uint)options.Entrance) : null)
                          ?? package.Manifest.Anchors.FirstOrDefault(a => a.Kind == "start");
        var b = package.Manifest.Bounds;
        var focus = startAnchor is not null
            ? new Vector3(startAnchor.Position[0], startAnchor.Position[1], startAnchor.Position[2]) * scale
            : new Vector3((b.Min[0] + b.Max[0]) / 2, (b.Min[1] + b.Max[1]) / 2, 0) * scale;
        stream.LoadNow(focus);
        L2Mesh? mesh = null;
        if (!(package.Manifest.Version >= L2Manifest.Version2) && stream.Cells.Count > 0)
        {
            var first = L2Cell.ReadFile(Path.Combine(packageFolder, stream.Cells[0].File), t => t == L2Mesh.Tag);
            if (first.Find(L2Mesh.Tag) is { Version: L2Mesh.Version1 } m) mesh = L2Mesh.Decode(m);
        }
        var shell = CellShell.Build(package.Manifest, lights, stream.Slots, scale);
        var level = new CellLevel
        {
            Name = package.Manifest.Name, Package = packageFolder, Manifest = package.Manifest, Shell = shell, Stream = stream,
            Lights = lights, Mesh = mesh, Routes = new CellRoutes(stream, scale),
            Scene = CellShell.Scene(package.Manifest.Name, package.Manifest.Anchors, scale, stream.Collision),
        };
        stream.Ground = level.Floor;
        byShell.AddOrUpdate(shell, level);
        return level;
    }

    /// <summary>Whether a package folder holds a cooked cell level rather than a converted 1.1 one.</summary>
    public static bool IsCellPackage(string packageFolder)
    {
        try { return L2Package.Open(packageFolder).Manifest.Cells.Any(c => c.Region == CellCook.Region); }
        catch (IOException) { return false; }
    }
}
