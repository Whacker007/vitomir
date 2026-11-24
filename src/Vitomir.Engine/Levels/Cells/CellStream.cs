// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>How the simulation's cells stream (ours; graphics and world options).</summary>
public sealed class CellStreamOptions
{
    /// <summary>Cells whose box comes within this many metres of the player are loaded for the simulation.</summary>
    public float SimRadius { get; set; } = 96f;
    /// <summary>A loaded cell is let go only this much further out (metres).</summary>
    public float Hysteresis { get; set; } = 32f;
    /// <summary>Load every cell at once and never unload (tests, tools, a small level).</summary>
    public bool Everything { get; set; }
    /// <summary>
    /// The top of a frame waits for the cells it has just asked for (the worker still reads them): a frame
    /// then never runs on ground that should be in and is not, whatever the machine's speed — for tests and
    /// for replays, which must come out the same every time.
    /// </summary>
    public bool Wait { get; set; }

    /// <summary>The options from the environment, for harnesses: <c>VITOMIR_SIM_RADIUS</c> (metres, or <c>all</c>).</summary>
    /// <summary>
    /// The entrance a level is opened at (ours): the cells round that entrance's first start are the
    /// ones read at once, so a party that begins far from the level's first start stands on loaded ground. −1,
    /// as before: the level's first start.
    /// </summary>
    public int Entrance { get; set; } = -1;

    /// <summary>The entrance the next level opened from the environment's options begins at (`--level-l2 … --entrance N`); −1 for none.</summary>
    public static int OpenAtEntrance { get; set; } = -1;

    public static CellStreamOptions FromEnvironment()
    {
        var o = new CellStreamOptions { Entrance = OpenAtEntrance };
        if (Environment.GetEnvironmentVariable("VITOMIR_SIM_RADIUS") is { Length: > 0 } r)
        {
            if (r == "all") o.Everything = true;
            else if (float.TryParse(r, System.Globalization.CultureInfo.InvariantCulture, out float m)) o.SimRadius = m;
        }
        return o;
    }
}

/// <summary>
/// **Ours (Phase 3): the simulation's cells, streamed.** A background thread reads
/// and decodes the cells the player is near — their collision (<c>COLL</c>), navigation-mesh tile
/// (<c>NAVM</c>), lights and anchors — and builds the loaded cells' collision as one
/// (<see cref="CellCollision.Merge"/>); the simulation takes the result only at the top of a frame
/// (<see cref="Update"/>, from <c>Simulation.Step</c>), all of it at once: the collision swapped, the
/// tiles added to and taken from the one Detour navigation mesh (which links a tile's border polygons to
/// its loaded neighbours'), and the walk map's pieces of ground put in or emptied
/// (<see cref="WalkMap.Restream"/>). The simulation stays single-threaded (Architecture 5.12).
///
/// <b>Slots.</b> Every polygon of every cell's tile has a fixed walk-node number: its cell's first
/// slot (the cells' polygon counts added up in the manifest's order) and its number in the tile. So a
/// node keeps its number while its cell is in, whatever else comes and goes, and a walker standing on
/// one is not disturbed by a cell loading a street away.
/// </summary>
public sealed class CellStream : IDisposable
{
    readonly L2Package package;
    readonly float scale;
    readonly int[] slotBase;
    readonly CellStreamPlan.Cell[] planCells;
    readonly List<WeakReference<WalkMap>> maps = [];
    readonly Dictionary<int, int> cellOfTile = [];
    Dictionary<int, Live> live = [];

    public CellStreamOptions Options { get; }
    public L2Manifest Manifest => package.Manifest;
    /// <summary>The manifest's cell entries (region <c>cell</c>), in its order: a cell's index is its place here.</summary>
    public IReadOnlyList<L2CellEntry> Cells { get; }
    /// <summary>How many walk-node slots the level has: every polygon of every tile.</summary>
    public int Slots { get; }
    /// <summary>The one navigation mesh of the loaded tiles (Recast's frame, metres).</summary>
    public DtNavMesh Nav { get; } = new();
    public DtNavMeshQuery Query { get; }
    /// <summary>The loaded cells' collision as one, in engine units: what <see cref="CellLevel.Collision"/> answers from.</summary>
    public CellCollision Collision { get; private set; } = CellCollision.Empty;
    /// <summary>The ground function the walk nodes are given (the level's <see cref="CellLevel.Floor"/>), set once the level exists.</summary>
    public Func<float, float, float, float>? Ground { get; set; }

    /// <summary>A loaded cell's decoded parts.</summary>
    sealed record Live(int Index, CellCollision Part, DtMeshData? Tile, List<L2Anchor> Anchors, List<L2Light> Lights, long Bytes, double Milliseconds)
    {
        public long TileRef { get; set; }
        public long PolyBase { get; set; }
    }

    /// <summary>What the worker hands the simulation: the whole loaded set and its collision.</summary>
    sealed record State(Dictionary<int, Live> Cells, CellCollision Collision, double MergeMilliseconds);

    // The worker's side.
    readonly object gate = new();
    Thread? worker;
    HashSet<int>? pending;
    State? ready;
    bool stopping;
    HashSet<int> requested = [];
    readonly Dictionary<int, Live> decoded = [];

    public CellStream(L2Package package, CellStreamOptions? options = null)
    {
        this.package = package;
        Options = options ?? new CellStreamOptions();
        scale = package.Manifest.Units.EngineUnitsPerMetre;
        Cells = [.. package.Manifest.Cells.Where(c => c.Region == CellCook.Region)];
        slotBase = new int[Cells.Count + 1];
        for (int i = 0; i < Cells.Count; i++) slotBase[i + 1] = slotBase[i] + Math.Max(0, Cells[i].NavPolygons ?? 0);
        planCells = [.. Cells.Select(c => new CellStreamPlan.Cell(new Vector2(c.Bounds.Min[0], c.Bounds.Min[1]), new Vector2(c.Bounds.Max[0], c.Bounds.Max[1]),
                                                                 c.Resident == true))];
        // An older package's one cell has no polygon count in the manifest: its tile is read now for it.
        if (Cells.Count == 1 && Cells[0].NavPolygons is null)
        {
            var only = Decode(0);
            decoded[0] = only;
            slotBase[1] = only.Tile?.header.polyCount ?? 0;
        }
        Slots = slotBase[^1];

        var grid = package.Manifest.Grid;
        var nav = new DtNavMeshParams
        {
            maxTiles = NextPowerOfTwo(Math.Max(1, Cells.Count)),
            maxPolys = NextPowerOfTwo(Math.Max(1, Cells.Max(c => (c.NavPolygons ?? Slots)) + 1)),
        };
        if (grid is not null)
        {
            nav.orig = new RcVec3f(grid.NavOrigin[0], grid.NavOrigin[1], grid.NavOrigin[2]);
            nav.tileWidth = nav.tileHeight = grid.CellSize;
        }
        else
        {
            // One cell: one tile at (0, 0), as big as the level.
            var b = package.Manifest.Bounds;
            nav.orig = CellCook.ToRecast(new Vector3(b.Min[0], b.Max[1], b.Min[2]));
            nav.tileWidth = nav.tileHeight = MathF.Max(b.Max[0] - b.Min[0], b.Max[1] - b.Min[1]) + 1f;
        }
        Nav.Init(in nav, 6);
        Query = new DtNavMeshQuery(Nav);
    }

    static int NextPowerOfTwo(int n) { int p = 1; while (p < n) p <<= 1; return p; }

    /// <summary>The indices of the loaded cells.</summary>
    public IReadOnlyCollection<int> Loaded => live.Keys;
    /// <summary>The bytes the loaded cells' files hold (their simulation chunks).</summary>
    public long LoadedBytes => live.Values.Sum(l => l.Bytes);
    /// <summary>The loaded cells' anchors: what the simulation may find by name.</summary>
    public IEnumerable<L2Anchor> LoadedAnchors => live.Values.SelectMany(l => l.Anchors);
    /// <summary>Cells taken in and let go since the level opened, and how long reading them took in all.</summary>
    public int LoadCount { get; private set; }
    public int UnloadCount { get; private set; }
    public double LoadMilliseconds { get; private set; }
    /// <summary>The last merge of the collision (background) and the last apply at the top of a frame (milliseconds).</summary>
    public double LastMergeMilliseconds { get; private set; }
    public double LastApplyMilliseconds { get; private set; }

    /// <summary>The walk-node slot a cell's polygons start at, and the cell a slot is in (−1 for none).</summary>
    public int FirstSlot(int cell) => slotBase[cell];
    public int CellOfSlot(int slot)
    {
        if (slot < 0 || slot >= Slots) return -1;
        int i = Array.BinarySearch(slotBase, slot);
        if (i < 0) i = ~i - 1;
        while (i + 1 < slotBase.Length && slotBase[i + 1] <= slot) i++;
        return i < Cells.Count ? i : -1;
    }

    /// <summary>Every light in the level (all cells' <c>LITE</c>), read once when the level opens: 1.1's model lighting and the renderer's.</summary>
    public List<L2Light> AllLights()
    {
        var list = new List<L2Light>();
        foreach (var c in Cells)
        {
            var cell = L2Cell.ReadFile(Path.Combine(package.Folder, c.File), t => t == L2LightChunk.Tag);
            if (cell.Find(L2LightChunk.Tag) is { } lite) list.AddRange(L2LightChunk.Decode(lite));
        }
        return list;
    }

    /// <summary>The cells to have in for a focus (engine units), by the plan (<see cref="CellStreamPlan.Wanted"/>).</summary>
    public List<int> Wanted(Vector3 focus)
    {
        if (Options.Everything) return [.. Enumerable.Range(0, Cells.Count)];
        var f = new Vector2(focus.X, focus.Y) / scale;
        return CellStreamPlan.Wanted(f, planCells, requested, Options.SimRadius, Options.Hysteresis);
    }

    /// <summary>
    /// Loads the cells wanted round <paramref name="focus"/> (engine units) on this thread and takes
    /// them in at once — when a level opens, before its first frame, and in tests.
    /// </summary>
    public void LoadNow(Vector3 focus)
    {
        var want = Wanted(focus).ToHashSet();
        requested = want;
        Apply(Build(want));
    }

    /// <summary>Every cell, at once (tests and tools).</summary>
    public void LoadAll()
    {
        var want = Enumerable.Range(0, Cells.Count).ToHashSet();
        requested = want;
        Apply(Build(want));
    }

    /// <summary>
    /// The top of a frame: takes in whatever the worker has finished (all of it at once), then asks
    /// it for the cells round <paramref name="focus"/> (engine units) if that set has changed.
    /// </summary>
    public void Update(Vector3 focus)
    {
        State? done;
        lock (gate) { done = ready; ready = null; }
        if (done is not null) Apply(done);
        var want = Wanted(focus).ToHashSet();
        if (want.SetEquals(requested)) return;
        requested = want;
        lock (gate)
        {
            pending = want;
            if (worker is null)
            {
                worker = new Thread(Work) { IsBackground = true, Name = "cell stream (simulation)", Priority = ThreadPriority.BelowNormal };
                worker.Start();
            }
            Monitor.PulseAll(gate);
            if (!Options.Wait) return;
            var watch = Stopwatch.StartNew();
            while ((pending is not null || busy || ready is null) && watch.ElapsedMilliseconds < 60_000) Monitor.Wait(gate, 50);
            done = ready; ready = null;
        }
        if (done is not null) Apply(done);
    }

    /// <summary>Waits (a harness's, a test's) until the worker has nothing left to do and its result is taken in.</summary>
    public void Settle(Vector3 focus, int timeoutMs = 60_000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            Update(focus);
            bool idle;
            lock (gate) idle = pending is null && ready is null && !busy;
            if (idle && live.Keys.ToHashSet().SetEquals(requested)) return;
            Thread.Sleep(5);
        }
    }

    bool busy;

    void Work()
    {
        while (true)
        {
            HashSet<int> want;
            lock (gate)
            {
                while (pending is null && !stopping) Monitor.Wait(gate);
                if (stopping) return;
                want = pending!; pending = null; busy = true;
            }
            var state = Build(want);
            lock (gate) { ready = state; busy = false; Monitor.PulseAll(gate); }
        }
    }

    /// <summary>Decodes what is not yet decoded of <paramref name="want"/>, lets go of the rest, merges the collision.</summary>
    State Build(HashSet<int> want)
    {
        Dictionary<int, Live> cells = [];
        lock (decoded)
        {
            foreach (int i in want) cells[i] = decoded.TryGetValue(i, out var d) ? d : Decode(i);
            decoded.Clear();
            foreach (var (i, c) in cells) decoded[i] = c;
        }
        var watch = Stopwatch.StartNew();
        var merged = CellCollision.Merge([.. cells.OrderBy(c => c.Key).Select(c => c.Value.Part)]);
        return new State(cells, merged, watch.Elapsed.TotalMilliseconds);
    }

    Live Decode(int index)
    {
        var watch = Stopwatch.StartNew();
        var entry = Cells[index];
        var path = Path.Combine(package.Folder, entry.File);
        var cell = L2Cell.ReadFile(path, t => t is L2Collision.Tag or L2NavMesh.Tag or L2LightChunk.Tag or L2AnchorChunk.Tag);
        var part = cell.Find(L2Collision.Tag) is { } coll ? new CellCollision(L2Collision.Decode(coll), scale) : CellCollision.Empty;
        var nav = cell.Find(L2NavMesh.Tag) is { } navChunk ? L2NavMesh.Decode(navChunk) : null;
        var tile = nav is { Tile.Length: > 0 } ? CellCook.ReadTile(nav.Tile) : null;
        var lights = cell.Find(L2LightChunk.Tag) is { } lite ? L2LightChunk.Decode(lite) : [];
        var anchors = cell.Find(L2AnchorChunk.Tag) is { } anch ? L2AnchorChunk.Decode(anch, entry.Id) : [];
        long bytes = cell.Chunks.Sum(c => (long)c.Payload.Length);
        return new Live(index, part, tile, anchors, lights, bytes, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Takes a finished set in: tiles out and in, the collision swapped, the walk maps' ground changed.</summary>
    void Apply(State state)
    {
        var watch = Stopwatch.StartNew();
        var removed = live.Keys.Where(i => !state.Cells.ContainsKey(i)).Order().ToList();
        var added = state.Cells.Keys.Where(i => !live.ContainsKey(i)).Order().ToList();
        foreach (int i in removed)
        {
            var gone = live[i];
            if (gone.TileRef != 0)
            {
                if (Nav.GetTileByRef(gone.TileRef) is { } t) cellOfTile.Remove(t.index);
                Nav.RemoveTile(gone.TileRef);
                gone.TileRef = 0;
            }
        }
        foreach (int i in added)
        {
            var came = state.Cells[i];
            LoadCount++; LoadMilliseconds += came.Milliseconds;
            if (came.Tile is null) continue;
            var status = Nav.AddTile(came.Tile, 0, 0, out long tileRef);
            if (status.Failed()) continue;
            came.TileRef = tileRef;
            var tile = Nav.GetTileByRef(tileRef);
            came.PolyBase = Nav.GetPolyRefBase(tile);
            cellOfTile[tile.index] = i;
        }
        UnloadCount += removed.Count;
        live = state.Cells;
        Collision = state.Collision;
        LastMergeMilliseconds = state.MergeMilliseconds;

        if (removed.Count > 0 || added.Count > 0)
        {
            var emptied = removed.SelectMany(i => Enumerable.Range(slotBase[i], slotBase[i + 1] - slotBase[i])).ToList();
            var border = BorderCells(removed.Concat(added)).Where(i => live.ContainsKey(i) && !added.Contains(i)).ToList();
            foreach (var map in AttachedMaps())
            {
                var placed = new List<WalkNode>();
                foreach (int i in added) placed.AddRange(Nodes(i, borderOnly: false));
                foreach (int i in border) placed.AddRange(Nodes(i, borderOnly: true));
                map.Restream(emptied, placed, []);
            }
        }
        LastApplyMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>The loaded cells next to (the eight round) any of <paramref name="changed"/>.</summary>
    IEnumerable<int> BorderCells(IEnumerable<int> changed)
    {
        var set = new HashSet<int>();
        var at = new Dictionary<(int, int), int>();
        for (int i = 0; i < Cells.Count; i++) if (Cells[i].At is { Length: 2 } a) at[(a[0], a[1])] = i;
        foreach (int c in changed)
        {
            if (Cells[c].At is not { Length: 2 } a) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && at.TryGetValue((a[0] + dx, a[1] + dy), out int n)) set.Add(n);
        }
        return set;
    }

    IEnumerable<WalkMap> AttachedMaps()
    {
        maps.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in maps) if (w.TryGetTarget(out var m)) yield return m;
    }

    /// <summary>A walk map built on this level's shell: its ground is filled in from the loaded cells now and kept up as they change.</summary>
    public void Attach(WalkMap map)
    {
        maps.Add(new WeakReference<WalkMap>(map));
        var placed = new List<WalkNode>();
        foreach (int i in live.Keys.Order()) placed.AddRange(Nodes(i, borderOnly: false));
        map.Restream([], placed, []);
    }

    /// <summary>Whether a polygon can be walked on: flagged so by the cook (ground under it) and a ground polygon.</summary>
    static bool Walkable(DtPoly p) => p.flags != 0 && p.GetPolyType() == DtPolyTypes.DT_POLYTYPE_GROUND;

    /// <summary>
    /// A loaded cell's walkable polygons as walk nodes (all of them, or only those with an edge on the
    /// tile's border): the outline counter-clockwise from above in engine units, its plane, and its
    /// neighbours — every walkable polygon Detour has linked it to, in this tile or a loaded neighbour's.
    /// </summary>
    IEnumerable<WalkNode> Nodes(int cell, bool borderOnly)
    {
        if (!live.TryGetValue(cell, out var lc) || lc.TileRef == 0) yield break;
        var tile = Nav.GetTileByRef(lc.TileRef);
        var data = tile.data;
        for (int p = 0; p < data.header.polyCount; p++)
        {
            var poly = data.polys[p];
            if (!Walkable(poly)) continue;
            if (borderOnly)
            {
                bool edge = false;
                for (int k = 0; k < poly.vertCount; k++) if ((poly.neis[k] & DtDetour.DT_EXT_LINK) != 0) edge = true;
                if (!edge) continue;
            }
            var outline = new Vector3[poly.vertCount];
            for (int k = 0; k < poly.vertCount; k++)
            {
                int v = poly.verts[k];
                outline[k] = CellCook.FromRecast(data.verts[3 * v], data.verts[3 * v + 1], data.verts[3 * v + 2], scale);
            }
            if (Area(outline) < 0) Array.Reverse(outline);
            var neighbours = new List<int>();
            for (int l = poly.firstLink; l != DtDetour.DT_NULL_LINK; l = tile.links[l].next)
            {
                long other = tile.links[l].refs;
                if (other == 0 || Nav.GetTileAndPolyByRef(other, out var ot, out var op).Failed() || !Walkable(op)) continue;
                if (!cellOfTile.TryGetValue(ot.index, out int oc)) continue;
                neighbours.Add(slotBase[oc] + op.index);
            }
            var flat = outline.Select(o => new Vector2(o.X, o.Y)).ToArray();
            var min = flat.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
            var max = flat.Aggregate(new Vector2(float.MinValue), Vector2.Max);
            int slot = slotBase[cell] + p;
            yield return new WalkNode
            {
                Index = slot, Sector = 0, Surface = slot, Outline = flat, Plane = CellShell.PlaneOf(outline),
                Min = min, Max = max, Ground = GroundOn(lc.PolyBase | (long)p), Neighbours = [.. neighbours.Distinct()],
            };
        }
    }

    /// <summary>A walk node's ground function (<see cref="WalkNode.Ground"/>): <see cref="HeightOn"/> on its polygon.</summary>
    Func<float, float, float, float> GroundOn(long polyRef) => (x, y, plane) => HeightOn(polyRef, x, y, plane);

    /// <summary>
    /// **The height of the ground at a point of a walk polygon** (ours; engine units). A navigation polygon
    /// is flat only in plan: Recast cuts the walkable ground into polygons by their outlines seen from
    /// above and keeps the heights of the corners alone, so the plane through a polygon's corners
    /// (<see cref="WalkNode.Plane"/>) can be far from the ground inside it -- a polygon across a ditch's
    /// curved bank, one over a stair and its landing, a sliver 48 m long and 0.6 m wide whose plane is
    /// tipped steeply across its width by a few centimetres' difference between its corners. What knows
    /// the ground inside a polygon is Detour's **detail mesh**, a few triangles per polygon that follow
    /// the voxels to within one of them. So the height is asked in two steps: the detail mesh's height
    /// at the point (<c>DtNavMeshQuery.ClosestPointOnPoly</c>), then the collision triangles' own floor
    /// nearest that (<see cref="Ground"/>, the level's <see cref="CellLevel.Floor"/>), which is what
    /// puts feet on the cobbles themselves.
    ///
    /// **Off the polygon's outline the height is that of the nearest point of its edge**, never the
    /// plane carried on: a walker a route has left on a polygon it is no longer over (1.1's own rule
    /// for a walker whose ground ray finds nothing; <see cref="WalkMap.Stride"/>) stays at the
    /// height it left it at instead of riding a tipped plane down through the ground.
    ///
    /// <paramref name="plane"/>, the plane's own height at the point, answers only when the polygon is
    /// not loaded any more. Earlier, it was the first guess everywhere, and where it was more than
    /// <see cref="CellLevel.FloorWindow"/> from the ground it was the answer.
    /// </summary>
    public float HeightOn(long polyRef, float x, float y, float plane)
    {
        float guess = plane;
        if (polyRef != 0 && Query.ClosestPointOnPoly(polyRef, CellCook.ToRecast(new Vector3(x, y, plane) / scale), out var closest, out _).Succeeded())
            guess = closest.Y * scale;
        return Ground is { } ground ? ground(x, y, guess) : guess;
    }

    static float Area(Vector3[] o)
    {
        float a = 0;
        for (int i = 0; i < o.Length; i++) { var p = o[i]; var q = o[(i + 1) % o.Length]; a += p.X * q.Y - q.X * p.Y; }
        return a * 0.5f;
    }

    /// <summary>The Detour polygon of a walk-node slot, or 0 when its cell is not loaded.</summary>
    public long RefOf(int slot)
    {
        int cell = CellOfSlot(slot);
        if (cell < 0 || !live.TryGetValue(cell, out var lc) || lc.TileRef == 0) return 0;
        int poly = slot - slotBase[cell];
        return poly < Nav.GetTileByRef(lc.TileRef).data.header.polyCount ? lc.PolyBase | (long)poly : 0;
    }

    /// <summary>The walk-node slot of a Detour polygon, or −1 (and whether it can be walked on).</summary>
    public int SlotOf(long polyRef, out bool walkable)
    {
        walkable = false;
        if (polyRef == 0 || Nav.GetTileAndPolyByRef(polyRef, out var tile, out var poly).Failed()) return -1;
        if (!cellOfTile.TryGetValue(tile.index, out int cell)) return -1;
        walkable = Walkable(poly);
        return slotBase[cell] + poly.index;
    }

    readonly IDtQueryFilter walkFilter = new DtQueryDefaultFilter();

    /// <summary>
    /// The walkable polygon under a point (engine units) whose floor is within <paramref name="window"/>
    /// (engine units) of the point's height, as a walk-node slot; −1 when none is loaded there.
    /// </summary>
    public int NodeUnder(Vector3 point, float window)
    {
        var centre = CellCook.ToRecast(point / scale);
        var half = new RcVec3f(0.05f, window / scale, 0.05f);
        var status = Query.FindNearestPoly(centre, half, walkFilter, out long found, out _, out bool over);
        if (status.Failed() || found == 0 || !over) return -1;
        int slot = SlotOf(found, out bool walk);
        return walk ? slot : -1;
    }

    /// <summary>The first loaded tile's model (a one-cell level's only tile), or null.</summary>
    public DtMeshData? FirstTile => live.Values.OrderBy(l => l.Index).Select(l => l.TileRef != 0 ? Nav.GetTileByRef(l.TileRef).data : null).FirstOrDefault(t => t is not null);

    public void Dispose()
    {
        lock (gate) { stopping = true; Monitor.PulseAll(gate); }
        worker?.Join(2000);
    }
}
