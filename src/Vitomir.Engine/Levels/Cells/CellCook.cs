// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Security.Cryptography;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>What the cook is given: a level's triangles and what to put in it besides.</summary>
public sealed class CellCookInput
{
    /// <summary>The level's name, as the game asks for it.</summary>
    public required string Name { get; init; }
    /// <summary>Every triangle's corners in <b>engine units, Z up</b> (the glTF reader's frame).</summary>
    public required Vector3[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Each triangle's material, an index into <see cref="Materials"/>.</summary>
    public required int[] TriangleMaterials { get; init; }
    public required string[] Materials { get; init; }
    /// <summary>
    /// Per material (an index into <see cref="Materials"/>), whether it is foliage — the glTF material's
    /// <c>extras.foliage</c> (ours, the contract): its cards are kept or dropped whole in the levels
    /// of detail and far versions (<see cref="CellFoliage"/>), never simplified. Null: none is.
    /// </summary>
    public bool[]? Foliage { get; init; }
    /// <summary>Which materials are ground: a triangle of one that faces up can be stood on.</summary>
    public required Func<string, bool> Ground { get; init; }
    /// <summary>Which materials stop nothing (leaves, flames): left out of the collision entirely.</summary>
    public required Func<string, bool> Passable { get; init; }
    /// <summary>
    /// Per triangle, whether it may be walked on at all (ours). Null: every triangle may, and ground is decided
    /// by its material alone. A level that names its walkable meshes (the layout's <c>ground.walkNodes</c>, or
    /// <c>--walk-nodes</c>) sets this, so ground-coloured scenery such as fields to the horizon is never walkable.
    /// </summary>
    public bool[]? TriangleWalkable { get; init; }
    /// <summary>
    /// Per triangle, whether it stops nothing whatever its material (ours). Null: none does but the passable
    /// materials'. A level names its decoration-only meshes (the layout's <c>decoration.passableMeshes</c>):
    /// a sack or a pot against a house front on a street, drawn but left out of collision and navigation.
    /// </summary>
    public bool[]? TrianglePassable { get; init; }
    /// <summary>
    /// Per triangle, whether it is not to be climbed (ours, <see cref="L2Surface.NoClimb"/>). Null: none is. A
    /// level names its no-climb meshes (the layout's <c>traversal.noClimbMeshes</c>, <see cref="CellCook.NoClimbMeshes"/>):
    /// a cart's top, a quest's edge, a roof that must not be reached, in place of an invisible blocker.
    /// </summary>
    public bool[]? TriangleNoClimb { get; init; }
    /// <summary>
    /// Per triangle, whether no body is to stand on it (ours, <see cref="L2Surface.NoStand"/>). Null: none is. A
    /// level names its no-stand meshes (the layout's <c>traversal.noStandMeshes</c>, <see cref="CellCook.NoStandMeshes"/>)
    /// and marks areas <c>"stand": "none"</c> (<see cref="CellCook.MarkNoStandAreas"/>). Such a triangle is never Walk,
    /// so it is left out of the navigation mesh by either bake.
    /// </summary>
    public bool[]? TriangleNoStand { get; init; }
    /// <summary>The glTF file the level is drawn from, copied into the package's <c>assets/</c>.</summary>
    public string? Asset { get; init; }
    /// <summary>The asset's draws, for the <c>MESH</c> chunk (metres).</summary>
    public IReadOnlyList<L2Draw> Draws { get; init; } = [];
    public IReadOnlyList<L2Light> Lights { get; init; } = [];
    public IReadOnlyList<L2Anchor> Anchors { get; init; } = [];
    public L2GlobalLight? Sun { get; init; }
    /// <summary>The level's date for the manifest (ours; <see cref="L2Date"/>), or null for none (mid-May).</summary>
    public L2Date? Date { get; init; }
    /// <summary>The level's compass for the manifest (ours; <see cref="L2North"/>), or null for none (north along +X).</summary>
    public L2North? North { get; init; }
    /// <summary>The level's named volumes for the manifest (ours; <see cref="L2Area"/>), or null for none.</summary>
    public List<L2Area>? Areas { get; init; }
    /// <summary>The level's exits for the manifest (ours; <see cref="L2LayoutExits"/>), or null for none.</summary>
    public List<L2Exit>? Exits { get; init; }
    /// <summary>The 1.1 scene the level stands for, for the manifest (ours; <see cref="L2Scene"/>), or null for none.</summary>
    public L2Scene? Scene { get; init; }
    /// <summary>Where the level's ground departs from the carried 1.1 scene's, for the manifest (ours; <see cref="L2Lift"/>), or null for nowhere.</summary>
    public List<L2Lift>? Lifts { get; init; }
    /// <summary>The level's ladders for the manifest (ours, <see cref="L2Traversal"/>), or null for none.</summary>
    public L2Traversal? Traversal { get; init; }
    /// <summary>Free-form settings kept in the manifest's <c>extras</c> (the sun's bearing and height for the modern renderer).</summary>
    public System.Text.Json.Nodes.JsonObject? Extras { get; init; }
    /// <summary>The navigation mesh's agent and grid (metres); <see cref="NavSettings.Default"/> unless given.</summary>
    public NavSettings Nav { get; init; } = NavSettings.Default;
    /// <summary>
    /// The render meshes (metres, Z up), each one material's triangles, for the cells' <c>MESH</c> buffers
    /// (version 2). Empty: the cells have no <c>MESH</c> chunk (a level cooked for its collision alone).
    /// </summary>
    public IReadOnlyList<CookMesh> Meshes { get; init; } = [];
    /// <summary>The grid's cell size in metres (ours; <c>--cell-size</c>, 32–64 m is the design's range); 0 cooks one cell.</summary>
    public float CellSize { get; init; }
    /// <summary>Cells per side of a block, which one far version stands for (<c>--block</c>).</summary>
    public int BlockCells { get; init; } = 2;
    /// <summary>A stress test's repeat of the whole scene on a grid (<c>--repeat</c>); <see cref="CookRepeat.None"/> otherwise.</summary>
    public CookRepeat Repeat { get; init; } = CookRepeat.None;
    /// <summary>The second level of detail's share of the first's triangles, and the most it may move the surface (metres).</summary>
    public float Lod1Ratio { get; init; } = 0.35f;
    public float Lod1MaxError { get; init; } = 0.25f;
    /// <summary>How many cells are cooked at once.</summary>
    public int Threads { get; init; } = 2;
    /// <summary>Progress lines, when wanted.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>One material's triangles for drawing: vertices in metres, Z up (the renderer's layout), and a material index into the cook's list.</summary>
public sealed record CookMesh(string Name, int Material, L2Vertex[] Vertices, uint[] Indices);

/// <summary>
/// The whole scene laid down <see cref="Columns"/> × <see cref="Rows"/> times, <see cref="StepX"/> and
/// <see cref="StepY"/> metres apart (ours: a stress level ten or more times a town's area, made of that
/// town, for the streaming's proof). The level-wide geometry (triangles wider than a cell) is laid down once.
/// </summary>
public readonly record struct CookRepeat(int Columns, int Rows, float StepX, float StepY)
{
    public static readonly CookRepeat None = new(1, 1, 0, 0);
    public int Count => Math.Max(1, Columns) * Math.Max(1, Rows);
    public Vector3 Offset(int r) => new(r % Math.Max(1, Columns) * StepX, r / Math.Max(1, Columns) * StepY, 0);
}

/// <summary>
/// The agent a navigation mesh is built for and the grid it is built on, in metres. The default is
/// Christof (1.78 m) with a body 0.25 m round, stepping 0.4 m, on slopes up to 45°
/// (the walk map's own 0.7 floor cut, <c>WalkMap.MinFloorNormalZ</c>), voxels 0.15 m by 0.05 m. All ours.
/// </summary>
public sealed record NavSettings(float Radius, float Height, float Climb, float MaxSlope, float CellSize, float CellHeight)
{
    public static readonly NavSettings Default = new(0.25f, 1.7f, 0.4f, 45f, 0.15f, 0.05f);

    /// <summary>
    /// The new platform's human (ours): a body 0.3 m round and 1.8 m tall that steps
    /// 0.35 m and walks up to 40°, on <see cref="Default"/>'s grid. Recast counts it as 2 voxels round, 36 tall and
    /// 7 of climb (0.35 / 0.05 is exactly 7 in single precision). The cook's <c>--nav-agent human</c>.
    /// </summary>
    public static readonly NavSettings Human = Default with { Radius = 0.3f, Height = 1.8f, Climb = 0.35f, MaxSlope = 40f };

    /// <summary>
    /// The cook's own ledge filter in place of Recast's (ours, for cell levels; <see cref="CellLedges"/>): a
    /// span is a ledge where a neighbour drops more than the climb, as Recast has it, but the spread between its
    /// highest and lowest accessible neighbours is judged against <b>twice</b> the climb, so the treads of a steep
    /// stair (each seeing the tread above and the one below) stay walkable. The cook's <c>--nav-agent</c> turns it on;
    /// without the switch it is off and a package comes out byte for byte as before. Recorded in the manifest
    /// (<c>navigation.ledges</c>, <see cref="L2Navigation"/>).
    /// </summary>
    public bool StairLedges { get; init; }

    /// <summary>
    /// The agent the cook's <c>--nav-agent</c> names (ours): <c>human</c> (<see cref="Human"/>), <c>default</c>
    /// (<see cref="Default"/>), or four numbers <c>radius,height,climb,slope</c> (metres and degrees, a point for the
    /// decimals) on <see cref="Default"/>'s grid. Null with <paramref name="error"/> saying why for anything else: a
    /// word it does not know, not four numbers, a radius, height or climb not above 0 (or a height not above the
    /// climb), a slope not between 0 and 90°.
    /// </summary>
    public static NavSettings? ParseAgent(string? text, out string error)
    {
        error = "";
        var t = text?.Trim() ?? "";
        if (t.Equals("human", StringComparison.OrdinalIgnoreCase)) return Human;
        if (t.Equals("default", StringComparison.OrdinalIgnoreCase)) return Default;
        var parts = t.Split(',');
        var v = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i]))
                v[i] = float.NaN;
        if (parts.Length != 4 || v.Any(float.IsNaN))
        {
            error = $"--nav-agent wants 'human' or four numbers radius,height,climb,slope (metres, degrees), for example 0.3,1.8,0.35,40; {(t.Length == 0 ? "it was given none" : $"not '{text}'")}";
            return null;
        }
        if (v[0] <= 0 || v[1] <= 0 || v[2] <= 0 || v[1] <= v[2]) { error = $"--nav-agent {text}: the radius, height and climb must be above 0 and the height above the climb"; return null; }
        if (v[3] <= 0 || v[3] >= 90) { error = $"--nav-agent {text}: the slope must be between 0 and 90 degrees"; return null; }
        return Default with { Radius = v[0], Height = v[1], Climb = v[2], MaxSlope = v[3] };
    }

    /// <summary>The agent as a line says it: <c>radius 0.3 m, height 1.8 m, climb 0.35 m, slope 40°</c>.</summary>
    public string Describe() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"radius {Radius:0.###} m, height {Height:0.###} m, climb {Climb:0.###} m, slope {MaxSlope:0.#}°{(StairLedges ? "; the stairs' ledge filter (a neighbours' spread to twice the climb)" : "")}");
}

/// <summary>What a cook made, for its report.</summary>
public sealed record CellCookResult(string Package, int Triangles, int Walkable, int NavPolygons, int NavWalkPolygons,
                                    int Lights, int Anchors, TimeSpan NavTime)
{
    /// <summary>Cells written (not counting the resident one) and far versions.</summary>
    public int Cells { get; init; }
    public int FarVersions { get; init; }
    /// <summary>Triangles drawn at each level of detail, over every cell, and in the far versions.</summary>
    public long Lod0Triangles { get; init; }
    public long Lod1Triangles { get; init; }
    public long FarTriangles { get; init; }
    /// <summary>Bytes of the cells (with the resident one) and of the far versions.</summary>
    public long CellBytes { get; init; }
    public long FarBytes { get; init; }
}

/// <summary>
/// **Ours: the cook** ("why not glTF as the level format itself"). It turns a
/// level's triangles into an L2 package of one cell with the new chunks — <c>COLL</c> (collision
/// triangles, a surface flag each, a bounding-volume tree), <c>NAVM</c> (a Detour navigation mesh
/// built with DotRecast), <c>LITE</c>, <c>ANCH</c> and <c>MESH</c> (a reference to the glTF asset, which
/// is copied into the package).
/// </summary>
public static partial class CellCook
{
    /// <summary>The walkable-floor cut, the walk map's own (<c>WalkMap.MinFloorNormalZ</c>).</summary>
    public const float MinFloorNormalZ = World.WalkMap.MinFloorNormalZ;

    /// <summary>The one cell of a level cooked without a grid (<see cref="CellCookInput.CellSize"/> 0).</summary>
    public const string CellId = "c0";

    /// <summary>The grid's cell size <c>--cell-size</c> gives unless told otherwise (metres; 32 to 64 m by design).</summary>
    public const float DefaultCellSize = 48f;

    /// <summary>The region kind a cooked cell's manifest entry names.</summary>
    public const string Region = "cell";

    /// <summary>A point in the manifest's frame (metres, Z up) in Recast's (metres, Y up).</summary>
    public static RcVec3f ToRecast(Vector3 m) => new(m.X, m.Z, -m.Y);

    /// <summary>A point in Recast's frame in engine units, Z up.</summary>
    public static Vector3 FromRecast(float x, float y, float z, float scale) => new Vector3(x, -z, y) * scale;

    /// <summary>
    /// The names of the meshes that may be walked on (ours): <c>--walk-nodes a,b</c>; else the layout's
    /// <c>ground.walkNodes</c> list; else the first word of its <c>ground.walkable</c> note (the Old Town
    /// generator writes "ground_walk (…)"); else null, and any ground material is walkable.
    /// </summary>
    public static string[]? WalkNodes(string? option, System.Text.Json.Nodes.JsonNode? layout)
    {
        if (!string.IsNullOrWhiteSpace(option)) return option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (layout?["ground"] is not System.Text.Json.Nodes.JsonObject ground) return null;
        if (ground["walkNodes"] is System.Text.Json.Nodes.JsonArray list)
            return [.. list.Select(x => x?.GetValue<string>()).OfType<string>()];
        if (ground["walkable"] is System.Text.Json.Nodes.JsonValue note && note.TryGetValue<string>(out var text) && text.Split(' ', 2)[0] is { Length: > 0 } first)
            return [first];
        return null;
    }

    /// <summary>
    /// The names of the meshes that are drawn but stop nothing (ours): the layout's
    /// <c>decoration.passableMeshes</c> list, else none.
    /// </summary>
    public static string[] PassableMeshes(System.Text.Json.Nodes.JsonNode? layout) =>
        layout?["decoration"]?["passableMeshes"] is System.Text.Json.Nodes.JsonArray list
            ? [.. list.Select(x => x?.GetValue<string>()).OfType<string>()] : [];

    /// <summary>How far past the walkable ground the navigation mesh looks, in metres (ours): room for the
    /// walls that bound a street to be voxelised.</summary>
    public const float NavMargin = 2f;

    /// <summary>The walk-flagged triangles' bounds (metres, Z up), grown by <see cref="NavMargin"/> across
    /// and by a body's height above; empty (lo &gt; hi) when nothing is walkable.</summary>
    public static (Vector3 Lo, Vector3 Hi) WalkBounds(Vector3[] metres, int[] indices, L2Surface[] surfaces)
    {
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        for (int t = 0; t < surfaces.Length; t++)
        {
            if ((surfaces[t] & L2Surface.Walk) == 0) continue;
            for (int k = 0; k < 3; k++) { var v = metres[indices[3 * t + k]]; lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        }
        if (lo.X > hi.X) return (lo, hi);
        return (lo - new Vector3(NavMargin, NavMargin, NavMargin), hi + new Vector3(NavMargin, NavMargin, NavMargin + 2f));
    }

    /// <summary>
    /// Where the navigation voxels' layers lie, as a part of a voxel's height (ours): **every layer's
    /// boundary is a quarter of a voxel above a whole number of voxels from height 0**, so with the
    /// 0.05 m voxel the boundaries are at 0.0125 m, 0.0625 m, 0.1125 m and so on, up and down. A floor
    /// built at a round height -- 0.000, 7.500, 11.0, any whole number of centimetres, any whole number
    /// of millimetres -- is then never on a boundary: a multiple of 2.5 cm is 1.25 cm from the nearest
    /// one, a whole centimetre at least 2.5 mm, a whole millimetre at least half a millimetre. Recast
    /// puts a floor's walkable top on the boundary above it, so a floor at a multiple of 5 cm gets its
    /// navigation polygons 1.25 cm over it.
    /// </summary>
    public const float NavDatum = 0.25f;

    /// <summary>
    /// The height the navigation voxels are counted up from (metres; Recast's <c>bmin.y</c>), ours: the
    /// highest layer boundary of the fixed datum (<see cref="NavDatum"/>) at or under
    /// <paramref name="lowest"/>, which is the level's lowest walkable ground less
    /// <see cref="NavMargin"/>. It is never more than a voxel under <paramref name="lowest"/>.
    ///
    /// Earlier, the count began at <paramref name="lowest"/> itself, so the layers followed the
    /// level's lowest walkable vertex to the last bit: a ditch dug 2 cm deeper moved every layer under
    /// every floor and stair of the level, and a lowest point a whole number of voxels under the built
    /// floors put all of them exactly on layer boundaries, where the last bit of a float decides which
    /// layer a floor is in.
    /// </summary>
    public static float NavFloor(float lowest, float cellHeight)
    {
        double ch = cellHeight;
        return (float)((Math.Floor(lowest / ch - NavDatum) + NavDatum) * ch);
    }

    /// <summary>The triangles whose bounding box meets the box <paramref name="lo"/>..<paramref name="hi"/>.</summary>
    public static int[] TrianglesOverlapping(Vector3[] metres, int[] indices, Vector3 lo, Vector3 hi)
    {
        var kept = new List<int>(indices.Length);
        foreach (int t in TriangleNumbersOverlapping(metres, indices, lo, hi))
        { kept.Add(indices[3 * t]); kept.Add(indices[3 * t + 1]); kept.Add(indices[3 * t + 2]); }
        return [.. kept];
    }

    /// <summary>The numbers of the triangles whose bounding box meets the box <paramref name="lo"/>..<paramref name="hi"/>.</summary>
    static List<int> TriangleNumbersOverlapping(Vector3[] metres, int[] indices, Vector3 lo, Vector3 hi)
    {
        var kept = new List<int>(indices.Length / 3);
        for (int t = 0; t < indices.Length / 3; t++)
        {
            var a = metres[indices[3 * t]]; var b = metres[indices[3 * t + 1]]; var c = metres[indices[3 * t + 2]];
            var tlo = Vector3.Min(a, Vector3.Min(b, c)); var thi = Vector3.Max(a, Vector3.Max(b, c));
            if (thi.X < lo.X || tlo.X > hi.X || thi.Y < lo.Y || tlo.Y > hi.Y || thi.Z < lo.Z || tlo.Z > hi.Z) continue;
            kept.Add(t);
        }
        return kept;
    }

    /// <summary>
    /// Builds the one tile. Only walk-flagged triangles can become ground: the rest are rasterised as solid
    /// with no area (<see cref="Rasterise"/>), so a street and a flat yard or forecourt beside it are never one
    /// region nor one polygon. Every polygon is then asked whether ground is under it
    /// (<see cref="CellCollision.Floor"/> on walk-flagged triangles within a Recast voxel's climb of the
    /// polygon's middle): one that misses keeps its place in the tile but gets flags 0, which Detour's queries
    /// and the walk map both pass over. A polygon whose middle misses ground is asked again at its corners
    /// (each pulled a quarter of the way in): one with ground under most of them is walked on (ours). Since
    /// non-walk ground has no area this is rare -- a polygon over a stair whose middle the flattened mesh
    /// puts more than the reach off the steps (two of the Old Town's 324) -- but it is kept for them.
    /// </summary>
    static (byte[] Tile, int Polys, int WalkPolys) BuildNavMesh(Vector3[] metres, int[] allIndices, L2Surface[] surfaces, CellCollision solid, NavSettings nav,
                                                                  NavTileSpec? tileSpec = null)
    {
        if (allIndices.Length == 0) return ([], 0, 0);
        // Only where people can walk: the walk-flagged triangles' extent and a margin. A level's scenery can
        // reach far past its streets (fields to the horizon), and one tile over all of it at a voxel of
        // 0.15 m would be billions of cells. Triangles wholly outside are left out; Recast clips the rest.
        var (lo, hi) = WalkBounds(metres, allIndices, surfaces);
        if (lo.X > hi.X) return ([], 0, 0);
        var indices = allIndices;
        var triSurfaces = surfaces;
        if (tileSpec is null)
        {
            var kept = TriangleNumbersOverlapping(metres, allIndices, lo, hi);
            indices = new int[kept.Count * 3];
            triSurfaces = new L2Surface[kept.Count];
            for (int k = 0; k < kept.Count; k++)
            {
                int t = kept[k];
                indices[3 * k] = allIndices[3 * t]; indices[3 * k + 1] = allIndices[3 * t + 1]; indices[3 * k + 2] = allIndices[3 * t + 2];
                triSurfaces[k] = surfaces[t];
            }
        }
        var verts = new float[metres.Length * 3];
        for (int i = 0; i < metres.Length; i++)
        {
            var r = ToRecast(metres[i]);
            verts[3 * i] = r.X; verts[3 * i + 1] = r.Y; verts[3 * i + 2] = r.Z;
        }
        // Recast wants its triangles wound so that a floor's normal is +Y; ours are Z-up counter-clockwise,
        // and x, z, −y is a rotation, so the winding carries over.
        var geom = new RcSampleInputGeomProvider(verts, indices);
        // The stairs' ledge filter (ours, NavSettings.StairLedges): Recast's first two filters are turned off
        // and run here before the build instead -- its own low-hanging one, then ours in place of its ledge filter --
        // so the order is Recast's and only the ledge test differs. Off, the config is as it always was.
        bool recastLedges = !nav.StairLedges;
        RcBuilderConfig bcfg;
        if (tileSpec is { } ts)
        {
            // One tile of the level's grid (ours, Phase 3): Recast builds it over the tile and a border of
            // neighbouring geometry, so that its edges on the tile's sides meet the neighbours' exactly and
            // Detour links them when both are loaded. Region sizes in square metres: the untiled 8² and 20² voxels.
            float voxelArea = nav.CellSize * nav.CellSize;
            var cfg = new RcConfig(true, ts.TileVoxels, ts.TileVoxels, ts.BorderVoxels, RcPartition.WATERSHED, nav.CellSize, nav.CellHeight,
                                   nav.MaxSlope, nav.Height, nav.Radius, nav.Climb, 64 * voxelArea, 400 * voxelArea, 12f, 1.3f, 6, 6f, 1f,
                                   recastLedges, recastLedges, true, new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
            bcfg = new RcBuilderConfig(cfg, ts.GridMin, ts.GridMax, ts.X, ts.Z);
        }
        else
        {
            var cfg = new RcConfig(RcPartition.WATERSHED, nav.CellSize, nav.CellHeight, nav.MaxSlope, nav.Height, nav.Radius, nav.Climb,
                                   8, 20, 12f, 1.3f, 6, 6f, 1f, recastLedges, recastLedges, true, new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
            var rlo = ToRecast(lo); var rhi = ToRecast(hi);   // x, z, −y: the y bounds swap
            bcfg = new RcBuilderConfig(cfg, new RcVec3f(rlo.X, NavFloor(rlo.Y, nav.CellHeight), rhi.Z), new RcVec3f(rhi.X, rhi.Y, rlo.Z));
        }
        var heightfield = Rasterise(verts, indices, triSurfaces, bcfg);
        if (!recastLedges)
        {
            RcFilters.FilterLowHangingWalkableObstacles(new RcContext(), bcfg.cfg.WalkableClimb, heightfield);
            CellLedges.Filter(heightfield, bcfg.cfg.WalkableHeight, bcfg.cfg.WalkableClimb, 2 * bcfg.cfg.WalkableClimb);
        }
        var result = new RcBuilder().Build(new RcContext(), bcfg.tileX, bcfg.tileZ, geom, bcfg.cfg, heightfield, false);
        var pmesh = result.Mesh;
        var dmesh = result.MeshDetail;
        if (pmesh is null || pmesh.npolys == 0) return ([], 0, 0);

        float scale = L2Manifest.EngineUnitsPerMetre;
        int walk = 0;
        for (int p = 0; p < pmesh.npolys; p++)
        {
            // The polygon's middle, from its vertices' voxel coordinates.
            float x = 0, y = 0, z = 0; int n = 0;
            for (int k = 0; k < pmesh.nvp; k++)
            {
                int v = pmesh.polys[p * pmesh.nvp * 2 + k];
                if (v == RcRecast.RC_MESH_NULL_IDX) break;
                x += pmesh.bmin.X + pmesh.verts[v * 3] * pmesh.cs;
                y += pmesh.bmin.Y + pmesh.verts[v * 3 + 1] * pmesh.ch;
                z += pmesh.bmin.Z + pmesh.verts[v * 3 + 2] * pmesh.cs;
                n++;
            }
            var mid = FromRecast(x / n, y / n, z / n, scale);
            float reach = (nav.Climb + nav.CellHeight * 4) * scale;
            bool onGround = solid.Floor(mid.X, mid.Y, mid.Z, reach) is not null;
            if (!onGround && n > 0)
            {
                int hits = 0;
                for (int k = 0; k < n; k++)
                {
                    int v = pmesh.polys[p * pmesh.nvp * 2 + k];
                    var corner = FromRecast(pmesh.bmin.X + pmesh.verts[v * 3] * pmesh.cs, pmesh.bmin.Y + pmesh.verts[v * 3 + 1] * pmesh.ch,
                                            pmesh.bmin.Z + pmesh.verts[v * 3 + 2] * pmesh.cs, scale);
                    var at = corner + (mid - corner) * 0.25f;
                    if (solid.Floor(at.X, at.Y, at.Z, reach) is not null) hits++;
                }
                onGround = 2 * hits > n;
            }
            pmesh.flags[p] = onGround ? 1 : 0;
            if (onGround) walk++;
        }

        var option = new DtNavMeshCreateParams
        {
            verts = pmesh.verts, vertCount = pmesh.nverts, polys = pmesh.polys, polyAreas = pmesh.areas, polyFlags = pmesh.flags,
            polyCount = pmesh.npolys, nvp = pmesh.nvp,
            walkableHeight = nav.Height, walkableRadius = nav.Radius, walkableClimb = nav.Climb,
            bmin = pmesh.bmin, bmax = pmesh.bmax, cs = nav.CellSize, ch = nav.CellHeight, buildBvTree = true,
            tileX = tileSpec?.X ?? 0, tileZ = tileSpec?.Z ?? 0,
        };
        if (dmesh is not null)
        {
            option.detailMeshes = dmesh.meshes; option.detailVerts = dmesh.verts; option.detailVertsCount = dmesh.nverts;
            option.detailTris = dmesh.tris; option.detailTriCount = dmesh.ntris;
        }
        var data = DtNavMeshBuilder.CreateNavMeshData(option);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
            new DtMeshDataWriter().Write(w, data, RcByteOrder.LITTLE_ENDIAN, true);
        return (ms.ToArray(), pmesh.npolys, walk);
    }

    /// <summary>
    /// Recast's solid heightfield, with each triangle's area ours to give (ours): Recast's own step marks a
    /// triangle walkable by its slope alone, so a flat yard, a forecourt or a counter beside a street would be
    /// as walkable as the street, and its regions and polygons would run across both. Here a triangle that is
    /// not walk-flagged (<see cref="L2Surface.Walk"/>: ground material, walk mesh, floor slope) is rasterised
    /// with no area -- it is still solid, standing in the way of what is under or beside it, but it never
    /// becomes ground -- and a walk-flagged one keeps Recast's slope test. A no-stand triangle (<see cref="L2Surface.NoStand"/>,
    /// the cook never makes one Walk) is rasterised with no area too, whichever bake runs.
    /// </summary>
    /// <param name="surfaces">Each triangle's surface, in the order of <paramref name="indices"/>.</param>
    static RcHeightfield Rasterise(float[] verts, int[] indices, L2Surface[] surfaces, RcBuilderConfig bcfg)
    {
        var cfg = bcfg.cfg;
        var ctx = new RcContext();
        var solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);
        int count = indices.Length / 3;
        var areas = RcRecast.MarkWalkableTriangles(ctx, cfg.WalkableSlopeAngle, verts, indices, count, cfg.WalkableAreaMod);
        for (int t = 0; t < count; t++)
            if ((surfaces[t] & L2Surface.Walk) == 0 || (surfaces[t] & L2Surface.NoStand) != 0) areas[t] = RcRecast.RC_NULL_AREA;
        RcRasterizations.RasterizeTriangles(ctx, verts, indices, areas, count, solid, cfg.WalkableClimb);
        return solid;
    }

    /// <summary>
    /// One tile of a gridded level's navigation mesh (ours): the grid's bounds in Recast's frame (metres, Y
    /// up), the tile's place on it, its size and border in voxels.
    /// </summary>
    public readonly record struct NavTileSpec(RcVec3f GridMin, RcVec3f GridMax, int X, int Z, int TileVoxels, int BorderVoxels);

    /// <summary>Reads a <c>NAVM</c> chunk's tile back into Detour's own model.</summary>
    public static DtMeshData ReadTile(byte[] tile, int maxVertsPerPoly = 6)
    {
        using var r = new BinaryReader(new MemoryStream(tile));
        return new DtMeshDataReader().Read(r, maxVertsPerPoly);
    }
}
