// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The ground you can stand on, and how you get from one piece of it to the next.
//
// The level compiler already worked this out. Every surface a level designer flagged Walk gets a
// **path node**, and the nodes are linked across their shared edges — the
// graph the original's AI and its click-to-move both run on. 43,083 of them across the game, and
// 1,131 of the 1,132 shipped player starts have one directly underneath.
//
// Two things we measured in the shipped data shape this:
//
//  * **Linked nodes meet at the same height.** Over all 135,319 links the height difference at the
//    shared edge is 0 in 88% of cases and never more than 6 units. Stairs are not steps in this
//    graph — each tread is its own surface, linked to the next where they touch — so walking the
//    graph climbs them with no step-height rule at all.
//  * **A sixth of the walkable surfaces are walls.** Walk is a brush flag in the level editor, so a
//    floor brush's sides carry it too and the compiler gives them nodes; you cannot stand on those.
//    <see cref="MinFloorNormalZ"/> cuts at 0.7 (about 45 degrees), which keeps 35,266 pieces of
//    ground and throws away 7,817. The cut is the shipped data's own empty band rather than a
//    constant of 1.1's: 30,112 surfaces are dead flat and only 153 in the whole game
//    lie between 0.05 and 0.7.
//
// The stored per-node half-planes are NOT used. They are a merged, lossy edge list — on 17% of
// nodes they disagree with the surface's own outline — so the polygon is the authority.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>One piece of walkable ground: a level surface, its outline, and where it leads.</summary>
public sealed class WalkNode
{
    public required int Index { get; init; }
    public required int Sector { get; init; }
    public required int Surface { get; init; }
    /// <summary>The surface's outline, looking straight down.</summary>
    public required Vector2[] Outline { get; init; }
    /// <summary>The surface's plane, which gives the floor height anywhere inside the outline.</summary>
    public required NilPlane Plane { get; init; }
    /// <summary>The nodes this one touches, in the level's own link order.</summary>
    public required int[] Neighbours { get; set; }
    /// <summary>
    /// The edges of the outline with nothing walkable on the other side: walls, and the lips of
    /// drops. A walker is kept its own radius clear of these; the other edges are where it crosses
    /// onto the next piece of ground.
    /// </summary>
    public (Vector2 A, Vector2 B)[] Walls { get; internal set; } = [];
    public Vector2 Min { get; init; }
    public Vector2 Max { get; init; }

    /// <summary>The floor height above (or below) a point, from the surface's own plane.</summary>
    public float Height(float x, float y)
    {
        float z = (Plane.Dist - Plane.Normal.X * x - Plane.Normal.Y * y) / Plane.Normal.Z;
        return Ground is { } ground ? ground(x, y, z) : z;
    }

    /// <summary>
    /// A cell level's true ground (ours, <c>Levels.Cells.CellStream.HeightOn</c>): given the point and the
    /// plane's height there, the height of the navigation mesh's detail triangles at the point and then of
    /// the collision triangles nearest that -- the plane through a navigation polygon's corners is only a
    /// fit and can be metres off the ground inside it -- and, off the polygon's outline, of its nearest
    /// edge. Null on a 1.1 level, whose surfaces are their planes.
    /// </summary>
    public Func<float, float, float, float>? Ground { get; init; }

    /// <summary>Whether a point, seen from above, is on this piece of ground.</summary>
    public bool Contains(Vector2 p)
    {
        if (p.X < Min.X || p.X > Max.X || p.Y < Min.Y || p.Y > Max.Y) return false;
        // Crossing number: works on the 8% of surfaces that are not convex, and does not care
        // which way round the outline was wound.
        bool inside = false;
        var o = Outline;
        for (int i = 0, j = o.Length - 1; i < o.Length; j = i++)
            if (o[i].Y > p.Y != o[j].Y > p.Y &&
                p.X < (o[j].X - o[i].X) * (p.Y - o[i].Y) / (o[j].Y - o[i].Y) + o[i].X)
                inside = !inside;
        return inside;
    }
}

/// <summary>
/// Every piece of walkable ground in a level, with a grid over it so finding the one under a point
/// does not mean looking at all of them.
/// </summary>
public sealed partial class WalkMap
{
    /// <summary>
    /// How far a surface's plane must face upward for someone to stand on it: about 45 degrees.
    /// Of the game's 43,083 Walk-flagged surfaces, 35,266 are at or above this and 7,817 below;
    /// only 153 lie between 0.05 and 0.7, which is the empty band this number was put in.
    /// </summary>
    public const float MinFloorNormalZ = 0.7f;

    /// <summary>How far below a point the ground may be and still count as the ground under it.</summary>
    public const float Reach = 64f;

    /// <summary>
    /// The engine's own "what is under this point" (the ground ray), for the callers that ask it
    /// directly — the enemy's repositioning, for one: a ray cast straight down from
    /// <see cref="RayAbove"/> units over the point, <see cref="RayLength"/> long, stopping at the first
    /// walkable surface it meets. So the ground may be up to 50 above the point and 90 below it —
    /// a wider window than <see cref="Reach"/>, which is ours. This is the walk map's stand-in for
    /// that ray (the highest ground in the window, by the walk map's own inside test); a route's
    /// step asks the engine's ray itself (<see cref="Ray"/>).
    /// </summary>
    public int Beneath(Vector3 at) => Find(at, RayLength - RayAbove, RayAbove);

    /// <summary>
    /// The same ray cast for <paramref name="owner"/>, whose body decides how long it is (the ray's
    /// branch on the body's shape): a **sphere**'s is its radius more, a **box**'s is
    /// longer by as far as the box hangs below the thing's position (140 less the box's
    /// lowest z), and anything else's is the plain <see cref="RayLength"/>. A level exit is
    /// a box centred halfway up its doorway, so its ray reaches the doorstep under it.
    /// </summary>
    public int Beneath(Vector3 at, WorldThing owner) => Find(at, RayLengthOf(owner) - RayAbove, RayAbove);

    /// <summary>How long the ground ray is for a thing: 140, a sphere's radius more, a box's lowest point more.</summary>
    public static float RayLengthOf(WorldThing owner) => owner.Collide switch
    {
        CollideShape.Sphere => owner.Radius + RayLength,
        CollideShape.Box => RayLength - owner.BoxMin.Z,
        _ => RayLength,
    };

    /// <summary>The ground ray: it starts this far above the point and is this long (140.0).</summary>
    public const float RayAbove = 50f, RayLength = 140f;
    /// <summary>And how far above, so standing a shade inside the floor still finds it.</summary>
    public const float Headroom = 16f;

    /// <summary>The side of one cell of the lookup grid, in world units.</summary>
    const float CellSize = 256f;

    /// <summary>How far past an edge to look for ground when deciding whether it is a wall.</summary>
    const float WallProbe = 2f;
    /// <summary>
    /// And how often to ask along the edge. The levels are built on an 8-unit grid, so a doorway is
    /// never narrower than this, and asking once per step cannot miss one.
    /// </summary>
    const float WallStep = 8f;

    /// <summary>
    /// How far above or below a walker a wall has to be before it is somebody else's problem. The
    /// tunnels, the factories and the towers stack corridors over one another, and without this a
    /// wall three storeys up blocks the floor below it — which is what it did until Josef's Tunnels
    /// turned out to be 1% walkable.
    /// </summary>
    public const float SameFloor = 64f;
    /// <summary>
    /// And how far down. Linked surfaces meet within 6 units of each other, so anything more than
    /// this is a drop rather than a join — and until there is gravity, the lip of a drop is a wall.
    /// </summary>
    const float WallDrop = 16f;

    public WalkNode[] Nodes { get; }
    /// <summary>How many walkable surfaces were left out for facing sideways.</summary>
    public int Vertical { get; }

    readonly Dictionary<(int X, int Y), List<int>> grid = [];
    /// <summary>The level the map was built from, for the engine's own ground ray (<see cref="Stride"/>).</summary>
    readonly NilLevel level;
    /// <summary>The cell level this map's ground is the navigation mesh of, or null for a 1.1 level (ours).</summary>
    readonly Levels.Cells.CellLevel? cells;

    public WalkMap(NilLevel level)
    {
        this.level = level;
        cells = Levels.Cells.CellLevel.Of(level);             // a cooked cell level (ours); null for every 1.1 level
        var nodes = new WalkNode?[level.PathNodes.Length];
        int vertical = 0;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var sector = level.Sectors[si];
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                int index = surface.PathNode;
                if (index < 0 || index >= nodes.Length || nodes[index] is not null) continue;
                var plane = sector.SurfacePlanes[surface.Plane];
                if (plane.Normal.Z < MinFloorNormalZ) { vertical++; continue; }
                var outline = new Vector2[surface.NumVerts];
                var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                for (int i = 0; i < outline.Length; i++)
                {
                    var v = sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];
                    outline[i] = new Vector2(v.X, v.Y);
                    min = Vector2.Min(min, outline[i]); max = Vector2.Max(max, outline[i]);
                }
                nodes[index] = new WalkNode
                {
                    Index = index, Sector = si, Surface = fi, Outline = outline, Plane = plane,
                    Min = min, Max = max, Ground = cells is null ? null : cells.Floor,
                    Neighbours = [.. level.PathNodes[index].Links.Select(l => (int)l.Target)
                                       .Where(t => t >= 0 && t < nodes.Length)],
                };
            }
        }
        Vertical = vertical;
        Nodes = [.. nodes.Select(n => n ?? Empty)];
        Bridge(level);
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].Outline.Length == 0) continue;
            var (lo, hi) = (Cell(Nodes[i].Min), Cell(Nodes[i].Max));
            for (int x = lo.X; x <= hi.X; x++)
                for (int y = lo.Y; y <= hi.Y; y++)
                {
                    if (!grid.TryGetValue((x, y), out var list)) grid[(x, y)] = list = [];
                    list.Add(i);
                }
        }
        // Which edges are walls can only be answered once every node is in the grid, because the
        // question is "is there ground on the other side of this edge".
        foreach (var node in Nodes) if (node.Outline.Length > 0) node.Walls = FindWalls(node);
        cells?.Attach(this);                                  // a cell level puts its loaded cells' ground in (ours, Restream)
    }

    /// <summary>
    /// Joins two pieces of ground that only touch through a surface nobody can stand on.
    ///
    /// A staircase in this data is not tread-tread-tread: each tread is linked to the **riser**
    /// between it and the next, and the riser is a Walk-flagged surface standing on its edge. Those
    /// are the 7,817 sideways-facing nodes this map leaves out, so without this a walker reaches
    /// the foot of a step and can go no further. The engine has no such gap — its route builder
    /// drops exactly these nodes from the route (they are the ones the level compiler flags
    /// pass-through) and its walker steps straight over them — so bridging
    /// them here is what makes our map agree with the engine's graph rather than a rule of our own.
    ///
    /// A **run** of them is followed, not just one. A single riser is the common case, but the
    /// shipped levels also stack two sideways surfaces back to back where a step meets a threshold
    /// — the haven's cellar hangs off exactly one such join, and bridging only one hop left 43 of
    /// its 57 pieces of ground unreachable while the engine's own graph reached all of them.
    /// Following the run to any depth costs nothing, because reaching a bridged neighbour still has
    /// to pass <see cref="Land"/>: the walker must be standing over that piece of ground and within
    /// <see cref="Reach"/> of its height. The bridge says *these two are adjacent*, not *you may
    /// teleport there*.
    /// </summary>
    void Bridge(NilLevel level)
    {
        var queue = new Queue<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].Outline.Length == 0) continue;
            List<int>? extra = null;
            queue.Clear(); seen.Clear();
            foreach (int n in Nodes[i].Neighbours)
                if (!IsGround(n) && seen.Add(n)) queue.Enqueue(n);
            while (queue.Count > 0)
                foreach (var link in level.PathNodes[queue.Dequeue()].Links)
                {
                    int beyond = (int)link.Target;
                    if (beyond < 0 || beyond >= Nodes.Length || beyond == i) continue;
                    // Another surface nobody can stand on: keep walking the run.
                    if (!IsGround(beyond)) { if (seen.Add(beyond)) queue.Enqueue(beyond); continue; }
                    if (Nodes[i].Neighbours.Contains(beyond)) continue;
                    (extra ??= []).Add(beyond);
                }
            if (extra is null) continue;
            var joined = extra.Distinct().ToArray();
            Bridged += joined.Length;
            Nodes[i].Neighbours = [.. Nodes[i].Neighbours, .. joined];
        }
    }

    /// <summary>How many joins had to be made through a surface nobody can stand on.</summary>
    public int Bridged { get; private set; }

    /// <summary>
    /// The parts of a piece of ground's outline with nothing walkable beyond them. Each edge is
    /// asked in short steps rather than once in the middle, and the answer can change along it:
    /// a room's floor is often one big polygon whose long side is mostly wall with a doorway
    /// somewhere along it, and judging the whole edge by its midpoint either seals the doorway or
    /// opens the wall — which is what it did until this was measured.
    ///
    /// Stepping past a point and looking for ground is what decides it, which does not care how the
    /// outline was wound, whether the neighbour is in the link list, or whether it is even in the
    /// same sector.
    /// </summary>
    (Vector2 A, Vector2 B)[] FindWalls(WalkNode node)
    {
        var walls = new List<(Vector2, Vector2)>();
        var o = node.Outline;
        for (int i = 0; i < o.Length; i++)
        {
            var a = o[i]; var b = o[(i + 1) % o.Length];
            var along = b - a;
            float length = along.Length();
            if (length < 1e-2f) continue;                                 // a degenerate edge is nothing
            var across = new Vector2(-along.Y, along.X) / length * WallProbe;
            int steps = Math.Clamp((int)MathF.Ceiling(length / WallStep), 1, 512);
            int runFrom = -1;
            for (int k = 0; k < steps; k++)
            {
                // The middle of this piece of the edge, stepped out to whichever side is outside.
                var at = a + along * ((k + 0.5f) / steps);
                var outside = node.Contains(at + across) ? at - across : at + across;
                bool wall = Find(new Vector3(outside, node.Height(at.X, at.Y)), WallDrop) < 0;
                if (wall && runFrom < 0) runFrom = k;
                if (!wall && runFrom >= 0)
                {
                    walls.Add((a + along * ((float)runFrom / steps), a + along * ((float)k / steps)));
                    runFrom = -1;
                }
            }
            if (runFrom >= 0) walls.Add((a + along * ((float)runFrom / steps), b));
        }
        return [.. walls];
    }

    static readonly WalkNode Empty = new()
    {
        Index = -1, Sector = -1, Surface = -1, Outline = [], Neighbours = [],
        Plane = new NilPlane(Vector3.UnitZ, 0),
    };

    /// <summary>True when this index names real ground rather than a gap in the node list.</summary>
    public bool IsGround(int node) => node >= 0 && node < Nodes.Length && Nodes[node].Outline.Length > 0;

    static (int X, int Y) Cell(Vector2 p) => ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize));

    /// <summary>
    /// The ground under a script's point, tested the way the floor probe tests it: by the point's
    /// distance above each surface's own plane, where a point **within 1e-5 of the plane counts as on
    /// it** (<c>_DAT_004eb760</c>) — so a point a hair under the floor, as a keyframe laid exactly on
    /// it can come out, is not refused as below — and one no more than <paramref name="reach"/> above
    /// it, straight down. The highest such surface, or −1. <see cref="Find"/> has no such allowance,
    /// and refused the running woman of NQTR_11_1 her last keyframe.
    /// </summary>
    public int FindUnder(Vector3 at, float reach)
    {
        int best = -1; float bestZ = float.MinValue;
        var p = new Vector2(at.X, at.Y);
        if (!grid.TryGetValue(Cell(p), out var here)) return -1;
        foreach (int i in here)
        {
            var node = Nodes[i];
            if (!node.Contains(p)) continue;
            var n = node.Plane.Normal;
            if (n.Z <= 0f) continue;                        // the ray down has to meet it from above
            float d = Vector3.Dot(at, n) - node.Plane.Dist;
            if (MathF.Abs(d) <= OnPlane) d = 0f;
            else if (d < 0f) continue;                      // under it
            if (d > reach * n.Z) continue;                  // further down than the reach
            float z = node.Height(p.X, p.Y);
            if (z > bestZ) { bestZ = z; best = i; }
        }
        return best;
    }

    /// <summary>How near a plane a point counts as on it (the plane test's constant).</summary>
    public const float OnPlane = 1e-5f;

    /// <summary>
    /// The ground under a point: the highest walkable surface whose outline the point stands over
    /// and whose floor is within <see cref="Reach"/> below it (or <see cref="Headroom"/> above).
    /// −1 when there is nothing to stand on.
    /// </summary>
    public int Find(Vector3 at, float reach = Reach, float headroom = Headroom)
    {
        int best = -1; float bestZ = float.MinValue;
        var p = new Vector2(at.X, at.Y);
        if (!grid.TryGetValue(Cell(p), out var here)) return -1;
        foreach (int i in here)
        {
            var node = Nodes[i];
            if (!node.Contains(p)) continue;
            float z = node.Height(p.X, p.Y);
            if (z > at.Z + headroom || z < at.Z - reach) continue;
            if (z > bestZ) { bestZ = z; best = i; }
        }
        return best;
    }

    /// <summary>
    /// The piece of ground under a point at any height — whichever of those the point stands over has
    /// its floor nearest the point's own height — or −1 when there is none at all. For a harness
    /// that names only where on the map, not which storey (`--start x,y`).
    /// </summary>
    public int Nearest(Vector3 at)
    {
        int best = -1; float bestGap = float.MaxValue;
        var p = new Vector2(at.X, at.Y);
        if (!grid.TryGetValue(Cell(p), out var here)) return -1;
        foreach (int i in here)
        {
            if (!Nodes[i].Contains(p)) continue;
            float gap = MathF.Abs(Nodes[i].Height(p.X, p.Y) - at.Z);
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        return best;
    }

    /// <summary>Where a walker ends up, and which piece of ground it is on.</summary>
    /// <param name="Node">−1 when it could not move at all.</param>
    /// <param name="Blocked">True when the walker was stopped or had to slide.</param>
    public readonly record struct Step(int Node, Vector3 Position, bool Blocked);

    /// <summary>
    /// Walks from <paramref name="from"/> towards <paramref name="to"/>, staying on the ground.
    /// A move that leaves the current surface is accepted when it lands on a surface next to it, or
    /// when it is over the gap a riser leaves before one (<see cref="Across"/>, ours); one that
    /// leaves the ground altogether is retried along each axis on its own, which is what makes a
    /// walker slide along a wall instead of sticking to it. The keyboard's step alone (driving).
    /// </summary>
    /// <param name="fits">
    /// What else has to be true of where the walker lands — for a character, that its body is clear
    /// of everything solid (<see cref="Collision"/>). The ground is this class's business; walls
    /// are the collision trees'.
    /// </param>
    public Step Move(int node, Vector3 from, Vector3 to, Func<Vector3, bool>? fits = null)
    {
        if (!IsGround(node)) { int found = Find(to); return new Step(found, Ground(found, to), found < 0); }
        if (Land(node, to) is { } straight && (fits?.Invoke(straight.Position) ?? true))
            return new Step(straight.Node, straight.Position, false);
        if (Across(node, from, to, fits) is { } over) return over;       // a riser's gap (ours, WalkMap.Drive.cs)

        // Blocked. Try each axis on its own, nearer one first, so a walker pressed against a wall
        // keeps whatever part of its movement runs along it.
        var dx = new Vector3(to.X, from.Y, to.Z);
        var dy = new Vector3(from.X, to.Y, to.Z);
        bool xFirst = MathF.Abs(to.X - from.X) >= MathF.Abs(to.Y - from.Y);
        foreach (var slide in xFirst ? new[] { dx, dy } : [dy, dx])
        {
            if (Land(node, slide) is { } along && (fits?.Invoke(along.Position) ?? true))
                return new Step(along.Node, along.Position, true);
            if (Across(node, from, slide, fits) is { } past) return past with { Blocked = true };
        }
        return Along(node, from, to, fits) ?? new Step(node, from, true);   // a slanting edge (ours)
    }

    /// <summary>
    /// Pushes a point away from any wall closer than <paramref name="radius"/>. A few passes,
    /// because pushing out of one wall can push into another — which is what a corner is.
    /// </summary>
    public Vector3 Clear(Vector3 at, float radius) => ClearOf(at, radius);

    /// <summary>
    /// **The engine's own step for a walker on a route** (the walker's step, then
    /// `simThing::SetPosition`) — which, unlike <see cref="Move"/>, never asks about the
    /// ground at all. The walker goes to exactly where the leg puts it, provided no *thing* is in
    /// the way (<paramref name="fits"/>; the body test keeps only the thing results). Then the surface:
    /// the one it had is kept while the point is **over it by the engine's own inside test**
    /// (<see cref="Over"/>); otherwise the ground is looked for with **the engine's ray**
    /// — the sweep straight down from 50 above (<see cref="WorldSweep.Ground"/>), from
    /// the sector the new point is in (the sector search with <paramref name="sector"/>, the walker's own,
    /// as the hint) — and, **when nothing is found, the old one is kept**. The height is that
    /// surface's plane at the new point, however far outside its outline the point now is.
    ///
    /// The case is the first monastery's: Wilhem, sent
    /// up the stairs from the floor beside them, walks a leg that runs exactly along the treads'
    /// side edge. In 1.1 he goes up it at the floor's height — at z −252 over
    /// ground that is not there, then on the tread at −204 as the next leg begins. Here
    /// <see cref="Move"/> found no ground under the edge, called it a wall, and he stood at the
    /// foot of the stairs until Christof came back down.
    ///
    /// <b>Ours, on a cell level:</b> the rule is the same, but a piece of ground there is a navigation
    /// polygon and its height is the ground's, not its plane's (<see cref="WalkNode.Ground"/>): a walker
    /// kept on a polygon it is no longer over stays at the height of that polygon's nearest edge.
    ///
    /// <b>Ours:</b> a walk-flagged surface the ray finds that is no piece of this map (one steeper
    /// than <see cref="MinFloorNormalZ"/>, or with no path node) counts as nothing found, since a
    /// walker here stands on a piece of the map; 1.1 would stand on it.
    /// </summary>
    public Step Stride(int node, Vector3 from, Vector3 to, Func<Vector3, bool>? fits = null, int sector = -1)
    {
        if (!(fits?.Invoke(to) ?? true)) return new Step(node, from, true);
        int surface = node;
        if (!IsGround(surface) || !Over(surface, to))
        {
            int found = Ray(to, sector);
            if (found >= 0) surface = found;
        }
        // The height comes off the surface's plane whenever it faces up at all (normal Z
        // over 0.3) — every walkable surface does.
        var at = IsGround(surface) ? to with { Z = Nodes[surface].Height(to.X, to.Y) } : to;
        return new Step(surface, at, false);
    }

    /// <summary>
    /// The over-surface test on a piece of ground's own surface — `SetPosition`'s "is the point still over the
    /// surface it had": the point projected onto the plane's two largest axes and inside
    /// every edge, **an edge itself counting as inside** (only a negative cross product is out), and
    /// the polygon taken to be convex (<see cref="Pick.Inside"/>). <see cref="WalkNode.Contains"/>,
    /// the crossing count, is ours and answers the rest of this class.
    /// </summary>
    public bool Over(int node, Vector3 at)
    {
        if (!IsGround(node)) return false;
        var n = Nodes[node];
        if (cells is not null) return n.Contains(new Vector2(at.X, at.Y));   // a cell level's ground is its own polygons (ours)
        var sector = level.Sectors[n.Sector];
        var surface = sector.Surfaces[n.Surface];
        return Pick.Inside(sector, surface, sector.SurfacePlanes[surface.Plane], at);
    }

    /// <summary>
    /// The ground ray under <paramref name="feet"/> as a piece of this map: the sector the
    /// feet are in (the sector search from <paramref name="sector"/>, as `SetPosition` finds it before it
    /// asks), the ray cast from there, and its surface's path node — −1 when the ray finds nothing
    /// or finds a surface that is no piece of the map.
    /// </summary>
    public int Ray(Vector3 feet, int sector = -1)
    {
        // A cell level has no collision tree to cast in; its ray is the map's own look straight down (ours).
        if (cells is not null) return Beneath(feet);
        int hint = SectorSearch.Find(level, feet, sector);
        if (WorldSweep.Ground(level, hint, feet) is not { } met) return -1;
        int index = level.Sectors[met.Sector].Surfaces[met.Surface].PathNode;
        return IsGround(index) ? index : -1;
    }

    Vector3 ClearOf(Vector3 at, float radius)
    {
        if (radius <= 0f) return at;
        var p = new Vector2(at.X, at.Y);
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            foreach (int i in Near(p, radius))
                foreach (var (a, b) in Nodes[i].Walls)
                {
                    var q = Nearest(a, b, p);
                    if (!OnThisFloor(i, q, at.Z)) continue;
                    var away = p - q;
                    float distance = away.Length();
                    if (distance >= radius) continue;
                    // Standing exactly on the wall: come off it along its own normal, inwards.
                    var push = distance > 1e-3f ? away / distance
                             : Vector2.Normalize(new Vector2(-(b - a).Y, (b - a).X));
                    if (distance <= 1e-3f && Nodes[i].Contains(p - push)) push = -push;
                    p = q + push * radius;
                    moved = true;
                }
            if (!moved) break;
        }
        return new Vector3(p, at.Z);
    }

    /// <summary>
    /// Whether a walker that wide stands clear of every wall here. False in a gap too narrow for
    /// it — which is how a doorway a character cannot fit through announces itself.
    /// </summary>
    public bool Fits(Vector3 at, float radius)
    {
        if (radius <= 0f) return true;
        var p = new Vector2(at.X, at.Y);
        foreach (int i in Near(p, radius))
            foreach (var (a, b) in Nodes[i].Walls)
            {
                var q = Nearest(a, b, p);
                if (Vector2.DistanceSquared(q, p) >= radius * radius - 0.5f) continue;
                if (OnThisFloor(i, q, at.Z)) return false;
            }
        return true;
    }

    /// <summary>Whether a wall belonging to a piece of ground is on the same storey as a walker.</summary>
    bool OnThisFloor(int node, Vector2 wallPoint, float z) =>
        MathF.Abs(Nodes[node].Height(wallPoint.X, wallPoint.Y) - z) <= SameFloor;

    /// <summary>The nodes whose cells a circle touches.</summary>
    IEnumerable<int> Near(Vector2 p, float radius)
    {
        var (lo, hi) = (Cell(p - new Vector2(radius)), Cell(p + new Vector2(radius)));
        for (int x = lo.X; x <= hi.X; x++)
            for (int y = lo.Y; y <= hi.Y; y++)
                if (grid.TryGetValue((x, y), out var list))
                    foreach (int i in list) yield return i;
    }

    /// <summary>The point on a segment nearest another point.</summary>
    static Vector2 Nearest(Vector2 a, Vector2 b, Vector2 p)
    {
        var along = b - a;
        float length = along.LengthSquared();
        if (length < 1e-6f) return a;
        return a + along * Math.Clamp(Vector2.Dot(p - a, along) / length, 0f, 1f);
    }

    /// <summary>
    /// The ground a point lands on: the one it is over, or one next to it — and when it is over
    /// more than one, whichever sits closest to the height the step was aiming at.
    ///
    /// The height is what makes stairs work. A route's waypoints sit **on** the shared edge between
    /// two treads, so both treads contain the point and preferring the one already stood on would
    /// leave the walker at the bottom of the step for ever. The engine has the same problem and
    /// solves it the same way: its move is three-dimensional, and `simThing::SetPosition` then
    /// re-finds the walk surface by looking down from 50 units above the new position,
    /// so a step that rises lands on the higher tread.
    /// </summary>
    (int Node, Vector3 Position)? Land(int from, Vector3 to)
    {
        var p = new Vector2(to.X, to.Y);
        int best = -1;
        float bestZ = 0f, bestGap = float.MaxValue;
        void Consider(int n)
        {
            if (!IsGround(n) || !Nodes[n].Contains(p)) return;
            float z = Nodes[n].Height(to.X, to.Y);
            // Linked surfaces meet at the same height in the shipped data, so a big jump here means
            // the walker has stepped over a gap onto something underneath.
            float gap = MathF.Abs(z - to.Z);
            if (n != from && gap > Reach) return;
            if (gap >= bestGap) return;
            best = n; bestZ = z; bestGap = gap;
        }
        Consider(from);
        foreach (int n in Nodes[from].Neighbours) Consider(n);
        return best < 0 ? null : (best, new Vector3(to.X, to.Y, bestZ));
    }

    Vector3 Ground(int node, Vector3 at) =>
        IsGround(node) ? new Vector3(at.X, at.Y, Nodes[node].Height(at.X, at.Y)) : at;

    /// <summary>How many pieces of ground there are, ignoring the gaps.</summary>
    public int Count => Nodes.Count(n => n.Outline.Length > 0);
}
