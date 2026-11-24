// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The reading of a package's cells is taken from src/Vitomir.Engine/Levels/Cells/CellStream.cs at b4185ef,
// reworked for the platform: metres, every cell at once.
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Vitomir.Formats.L2;
using Vitomir.Platform.Core;
using Vitomir.Platform.Navigation;

namespace Vitomir.Platform.Space;

/// <summary>
/// One of a level's anchors as the simulation uses it: its id, kind (<c>start</c>, <c>actor</c> …), 1.1
/// name, place in metres, yaw in radians (the anchor's turn about Z) and group (a start's entrance).
/// </summary>
public readonly record struct LevelAnchor(string Id, string Kind, string Name, Vector3 Position, float Yaw, int Group);

/// <summary>
/// A level's space for the simulation: its collision as one
/// <see cref="CollisionWorld"/>, its navigation mesh (<see cref="LevelNavigation"/>, when the package has
/// one), its anchors, its exits (<see cref="LevelExits"/>) and its bounds. A world holds one as a
/// service (<see cref="Bodies.MovementServices.Register"/>); it is read-only while the world steps.
/// </summary>
public sealed class LevelSpace
{
    public string Name { get; }
    public CollisionWorld Collision { get; }
    public LevelNavigation? Navigation { get; }
    public IReadOnlyList<LevelAnchor> Anchors { get; }
    /// <summary>Where the space came from (a package folder), or empty.</summary>
    public string Source { get; init; } = "";
    /// <summary>The level's exits (ours): the manifest's, else none.</summary>
    public LevelExits Exits { get; init; } = LevelExits.None;
    /// <summary>
    /// The box round the level, metres: the manifest's <c>bounds</c> for a package, else the collision's. A body
    /// that falls far below its floor has fallen out of the world (<see cref="Bodies.FallRecoverySystem"/>).
    /// </summary>
    public (Vector3 Min, Vector3 Max) Bounds { get; init; }
    /// <summary>
    /// What its places are to the Masquerade (ours, <see cref="LevelPlaces"/>): the areas marked
    /// <c>public</c> or <c>free</c>, the level's own word; public where nothing says otherwise.
    /// </summary>
    public LevelPlaces Places { get; init; } = LevelPlaces.None;
    /// <summary>
    /// The ledge filter the package's navigation was baked with where it is not Recast's own (ours, the manifest's
    /// <c>navigation.ledges</c>, <see cref="L2Navigation"/>): <c>stairs</c> for the cook's stairs' filter, null for Recast's.
    /// </summary>
    public string? NavLedges { get; init; }

    public LevelSpace(string name, CollisionWorld collision, LevelNavigation? navigation = null, IReadOnlyList<LevelAnchor>? anchors = null)
    {
        Name = name; Collision = collision; Navigation = navigation; Anchors = anchors ?? [];
        Bounds = collision.Bounds;
    }

    /// <summary>The start anchors, in the manifest's order.</summary>
    public IEnumerable<LevelAnchor> Starts => Anchors.Where(a => a.Kind == "start");

    /// <summary>
    /// The start a player asks for (ours, the program's <c>--start</c>): a whole number is an index into
    /// <see cref="Starts"/>, held to the list's ends as before; anything else is a start's name (the proving ground's
    /// <c>mantle</c> or <c>kerbs-0.35</c>), compared without regard to case, the first of that name. A name the level
    /// does not have is refused with the names it has, in their order, for the log.
    /// </summary>
    public bool TryFindStart(string? text, out int index, out string error)
    {
        var starts = Starts.ToList();
        text = text?.Trim() ?? "";
        error = "";
        if (text.Length == 0) { index = 0; return true; }
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            index = Math.Clamp(number, 0, Math.Max(0, starts.Count - 1));
            return true;
        }
        for (index = 0; index < starts.Count; index++)
            if (string.Equals(starts[index].Name, text, StringComparison.OrdinalIgnoreCase)) return true;
        index = 0;
        error = starts.Count == 0
            ? $"--start {text}: the level {Name} has no starts"
            : $"--start {text}: the level {Name} has no start of that name; its starts are: {string.Join(", ", starts.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase))}";
        return false;
    }

    /// <summary>The region kind a cooked cell's manifest entry names (the cook's <c>CellCook.Region</c>).</summary>
    public const string CellRegion = "cell";

    /// <summary>
    /// Opens an L2 package (manifest version 1 or 2) and reads <b>every cell at
    /// once</b>: each cell's <c>COLL</c> merged into one tree (<see cref="CollisionWorld.Merge"/>), each
    /// <c>NAVM</c> tile added to one Detour mesh laid out on the package's grid (or, for a one-cell package
    /// without a grid, one tile as big as the level), with the agent the tiles were baked for
    /// (<see cref="LevelNavigation.Agent"/>), and the anchors — the manifest's, else the cells'
    /// own <c>ANCH</c> chunks.
    /// </summary>
    public static LevelSpace Open(string packageFolder)
    {
        var package = L2Package.Open(packageFolder);
        var manifest = package.Manifest;
        var cells = manifest.Cells.Where(c => c.Region == CellRegion).ToList();
        if (cells.Count == 0) cells = [.. manifest.Cells];
        var parts = new List<CollisionWorld>(cells.Count);
        var tiles = new List<DtMeshData>(cells.Count);
        var anchors = new List<LevelAnchor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in manifest.Anchors)
            if (seen.Add(a.Id)) anchors.Add(Anchor(a));
        bool cellAnchors = anchors.Count == 0;
        NavAgent? agent = null;
        bool mixedAgents = false;
        foreach (var entry in cells)
        {
            var path = Path.Combine(package.Folder, entry.File);
            if (!File.Exists(path)) continue;
            var cell = L2Cell.ReadFile(path, t => t is L2Collision.Tag or L2NavMesh.Tag or L2AnchorChunk.Tag);
            if (cell.Find(L2Collision.Tag) is { } coll) parts.Add(new CollisionWorld(L2Collision.Decode(coll)));
            if (cell.Find(L2NavMesh.Tag) is { } navChunk && L2NavMesh.Decode(navChunk) is { Tile.Length: > 0 } nav)
            {
                tiles.Add(LevelNavigation.ReadTile(nav.Tile));
                var baked = NavAgent.Of(nav);
                if (agent is null) agent = baked; else if (agent != baked) mixedAgents = true;
            }
            if (cellAnchors && cell.Find(L2AnchorChunk.Tag) is { } anch)
                foreach (var a in L2AnchorChunk.Decode(anch, entry.Id))
                    if (seen.Add(a.Id)) anchors.Add(Anchor(a));
        }
        LevelNavigation? navigation = null;
        if (tiles.Count > 0)
        {
            var parameters = new DtNavMeshParams
            {
                maxTiles = NextPowerOfTwo(Math.Max(1, tiles.Count)),
                maxPolys = NextPowerOfTwo(tiles.Max(t => t.header.polyCount) + 1),
            };
            if (manifest.Grid is { } grid)
            {
                parameters.orig = new RcVec3f(grid.NavOrigin[0], grid.NavOrigin[1], grid.NavOrigin[2]);
                parameters.tileWidth = parameters.tileHeight = grid.CellSize;
            }
            else
            {
                var b = manifest.Bounds;
                parameters.orig = LevelNavigation.ToRecast(new Vector3(b.Min[0], b.Max[1], b.Min[2]));
                parameters.tileWidth = parameters.tileHeight = MathF.Max(b.Max[0] - b.Min[0], b.Max[1] - b.Min[1]) + 1f;
            }
            navigation = new LevelNavigation(parameters, tiles) { Agent = agent, MixedAgents = mixedAgents };
        }
        var collision = CollisionWorld.Merge(parts);
        // The areas not to be climbed in (ours, LevelClimb), carried by the space the probe reads lips from.
        if (LevelClimb.From(manifest) is { } climb && climb != LevelClimb.None) collision = collision.WithClimb(climb);
        // The level's ladders (ours, LevelLadders), carried by the space the motor is given.
        if (LevelLadders.From(manifest) is { Count: > 0 } ladders) collision = collision.WithLadders(ladders);
        var bounds = manifest.Bounds is { Min.Length: >= 3, Max.Length: >= 3 } mb && mb.Max[2] > mb.Min[2]
            ? (new Vector3(mb.Min[0], mb.Min[1], mb.Min[2]), new Vector3(mb.Max[0], mb.Max[1], mb.Max[2]))
            : collision.Bounds;
        return new LevelSpace(manifest.Name, collision, navigation, anchors)
        {
            Source = packageFolder, Exits = LevelExits.From(manifest.Exits), Bounds = bounds,
            Places = LevelPlaces.From(manifest), NavLedges = manifest.Navigation?.Ledges,
        };
    }

    static int NextPowerOfTwo(int n) { int p = 1; while (p < n) p <<= 1; return p; }

    static LevelAnchor Anchor(L2Anchor a)
    {
        var p = a.Position.Length >= 3 ? new Vector3(a.Position[0], a.Position[1], a.Position[2]) : Vector3.Zero;
        float yaw = a.Rotation.Length >= 3 ? DetMath.WrapAngle(a.Rotation[2] * DetMath.DegToRad) : 0f;
        return new LevelAnchor(a.Id, a.Kind, a.Name, p, yaw, (int)(a.Group ?? 0));
    }
}
