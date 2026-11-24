// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Getting from here to there.
//
// The original's primary control is click-to-move, and the search behind it is the path code's
// A* over the level compiler's path graph. Every rule and every constant
// below is 1.1's rather than chosen.
//
// The one idea worth stating up front, because it is what makes the routes look deliberate rather
// than like a grid search: **a step is not from one surface's middle to the next one's middle.**
// Each link between two surfaces is a shared edge, and the search works out where on that edge to
// cross — the point where the straight line from start to goal would cut it, pushed in by the
// walker's own radius at whichever end the link says is tight. So a walk down a corridor cuts the
// corners, and a walk through a doorway aims at the doorway.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>What a search found.</summary>
public enum PathResult
{
    /// <summary>Nowhere to go: no route, or one longer than the engine's own 255-step limit.</summary>
    None = 0,
    /// <summary>A route, as a list of points to cross in order.</summary>
    Path = 1,
    /// <summary>Start and goal are the same piece of ground — walk straight at it.</summary>
    Straight = 2,
}

/// <summary>One step of a route: where to cross onto the next piece of ground.</summary>
public readonly record struct Waypoint(int Node, Vector3 Point);

/// <summary>
/// The level compiler's path graph, searched the way the engine searches it. One of these per
/// level; a search is not thread-safe, because the engine's is not either — it keeps its working
/// state on the nodes themselves and stamps them with a search number.
/// </summary>
public sealed class PathFinder(NilLevel level)
{
    /// <summary>
    /// How much route the engine will pay for. `moveWalk::WalkTo` passes this literal to the search
    /// and a node costing more than it is never opened, so a goal further than this is
    /// simply unreachable however open the ground between is.
    /// </summary>
    public const float MaxCost = 5000f;

    /// <summary>
    /// Added to every node's estimate. It is not a distance — it is a toll on taking one
    /// more step, which is what makes the search prefer a longer leg over two short ones.
    /// </summary>
    public const float StepPenalty = 10f;

    /// <summary>
    /// How far from each end of an edge a crossing point is kept, and the point used when
    /// the cut falls behind an edge narrower than the walker. (An edge the straight line runs
    /// parallel to is crossed at its origin, not here.)
    /// </summary>
    public const float EdgeMargin = 0.5f;

    /// <summary>
    /// Below this, the straight line and the edge count as parallel and the edge's origin is used
    /// instead (a double, and the only one of these constants that is).
    /// </summary>
    public const float Parallel = 0.001f;

    /// <summary>The engine's own cap on how many steps a route may have.</summary>
    public const int MaxSteps = 255;

    /// <summary>Link flags. 1 and 2 pull a crossing point in by the walker's radius at that end of
    /// the edge; 4 bars ordinary characters (but not the player); 8 makes the node it belongs to a
    /// pass-through, which the route skips over rather than aiming at.</summary>
    public const ushort LinkNarrowStart = 0x1, LinkNarrowEnd = 0x2, LinkNoActors = 0x4, LinkPassThrough = 0x8;

    public NilLevel Level { get; } = level;

    /// <summary>A cooked cell level's Detour routes, which answer its searches (ours, <c>Levels.Cells.CellRoutes</c>); null for every 1.1 level.</summary>
    readonly Levels.Cells.CellRoutes? cellRoutes = Levels.Cells.CellLevel.Of(level)?.Routes;

    readonly int count = level.PathNodes.Length;
    // The engine keeps this state on the node records themselves; we keep it beside them, which is
    // the same thing without writing to the level.
    int[] stamp = new int[level.PathNodes.Length];
    float[] g = new float[level.PathNodes.Length], h = new float[level.PathNodes.Length], f = new float[level.PathNodes.Length];
    Vector3[] crossing = new Vector3[level.PathNodes.Length];
    int[] fromNode = new int[level.PathNodes.Length], fromLink = new int[level.PathNodes.Length];
    bool[] open = new bool[level.PathNodes.Length];
    int[] next = new int[level.PathNodes.Length], prev = new int[level.PathNodes.Length];
    int searchStamp;
    int head = -1, openCount;

    /// <summary>
    /// Which nodes carry a pass-through link. The loader sets a flag on the node when any of its
    /// links is flagged 8, and the route builder leaves those nodes out — you walk on
    /// through rather than aiming at them.
    /// </summary>
    readonly bool[] passThrough = BuildPassThrough(level);

    static bool[] BuildPassThrough(NilLevel level)
    {
        var flags = new bool[level.PathNodes.Length];
        for (int i = 0; i < flags.Length; i++)
            foreach (var link in level.PathNodes[i].Links)
                if ((link.Flags & LinkPassThrough) != 0) { flags[i] = true; break; }
        return flags;
    }

    /// <summary>How many nodes the last search opened — the measure of what a route cost to find.</summary>
    public int Opened { get; private set; }

    /// <summary>
    /// A route from <paramref name="from"/> on <paramref name="fromPathNode"/> to
    /// <paramref name="to"/> on <paramref name="toPathNode"/>.
    ///
    /// <paramref name="radius"/> is the walker's own, and is what links flagged narrow keep it clear
    /// of. <paramref name="isActor"/> is true for an ordinary character and false for the player,
    /// which is the difference link flag 4 makes: it bars the one and not the other.
    /// </summary>
    public PathResult Find(Vector3 from, int fromPathNode, Vector3 to, int toPathNode,
                           float radius, bool isActor, out Waypoint[] path)
    {
        path = [];
        Opened = 0;
        if (cellRoutes is not null) return cellRoutes.Find(from, fromPathNode, to, toPathNode, out path);   // a cell level: Detour (ours)
        if (fromPathNode == toPathNode) return PathResult.Straight;
        if (fromPathNode < 0 || fromPathNode >= count || toPathNode < 0 || toPathNode >= count) return PathResult.None;

        // The straight line from where we are to where we want to be. Every crossing point is
        // chosen against this one direction, not against the local step — which is why a route
        // hugs the direct line instead of wandering node to node.
        var straight = to - from;
        float span = straight.Length();
        var direction = span > 0 ? straight / span : Vector3.Zero;

        if (++searchStamp == 0) searchStamp = 1;
        head = -1; openCount = 0;

        stamp[fromPathNode] = searchStamp;
        g[fromPathNode] = 0f;
        h[fromPathNode] = f[fromPathNode] = span;
        crossing[fromPathNode] = from;
        fromLink[fromPathNode] = -1;
        fromNode[fromPathNode] = -1;
        open[fromPathNode] = true;
        next[fromPathNode] = prev[fromPathNode] = -1;
        head = fromPathNode;
        openCount = 1;

        while (openCount > 0)
        {
            int node = head;
            if (node == toPathNode) return Build(node, out path) ? PathResult.Path : PathResult.None;
            Unlink(node);
            open[node] = false;
            Opened++;

            var links = Level.PathNodes[node].Links;
            for (int li = 0; li < links.Length; li++)
            {
                var link = links[li];
                int target = (int)link.Target;
                if (target < 0 || target >= count) continue;
                if ((link.Flags & LinkNoActors) != 0 && isActor) continue;
                if (IsAncestor(node, target)) continue;

                var point = Cross(link, from, direction, radius);
                float cost = g[node] + Vector3.Distance(point, crossing[node]);
                if (cost > Rules.GameRules.Current.Movement.RouteMaxCost) continue;
                float estimate = Vector3.Distance(to, point);
                float total = estimate + cost + Rules.GameRules.Current.Movement.RouteStepPenalty;

                if (stamp[target] == searchStamp)
                {
                    if (f[target] <= total) continue;
                }
                else
                {
                    stamp[target] = searchStamp;
                    fromLink[target] = -1; fromNode[target] = -1;
                    next[target] = prev[target] = -1;
                    open[target] = false;
                }
                g[target] = cost;
                h[target] = estimate;
                f[target] = total;
                crossing[target] = point;
                fromNode[target] = node;
                fromLink[target] = li;
                if (open[target]) { Unlink(target); open[target] = false; }
                Insert(target);
            }
        }
        return PathResult.None;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is already on the way we came. The engine walks the
    /// parent chain for every neighbour of every node it opens, which is not cheap and is not an
    /// A* closed set — it only stops the search doubling back on its own route.
    /// </summary>
    bool IsAncestor(int node, int candidate)
    {
        for (int n = node; n >= 0; n = fromNode[n])
            if (n == candidate) return true;
        return false;
    }

    /// <summary>
    /// Where on a shared edge to cross it. The straight line from the start to the goal defines a
    /// plane through the edge; where that plane cuts the edge is the crossing point, pulled in by
    /// the walker's radius at whichever end the link flags say is tight, and always kept half a
    /// unit off each end. **An edge the line runs parallel to is crossed at its origin** — the
    /// engine loads 0.0 there, not the half-unit margin; a comparison with 1.1
    /// corrected this, which had used the margin point.
    /// </summary>
    public static Vector3 Cross(NilPathLink link, Vector3 from, Vector3 direction, float radius)
    {
        float t = EdgeMargin;
        var perpendicular = Vector3.Cross(direction, link.Direction);
        if (perpendicular.Length() < Parallel) t = 0f;                     // The edge's origin
        else
        {
            var normal = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(perpendicular), direction));
            float denominator = Vector3.Dot(normal, link.Direction);
            if (denominator != 0f)
            {
                float cut = -Vector3.Dot(link.Origin - from, normal) / denominator;
                float low = (link.Flags & LinkNarrowStart) != 0 ? radius : 0f;
                float high = link.Length - ((link.Flags & LinkNarrowEnd) != 0 ? radius : 0f);
                float clamped;
                if (high < low)
                {
                    // The edge is narrower than the body. The engine does not give up on it — it
                    // aims at the cut anyway, or at the margin when the cut is behind the edge.
                    if (cut < 0f) clamped = float.NaN;
                    else clamped = MathF.Min(cut, link.Length);
                }
                else clamped = cut < low ? low : MathF.Min(cut, high);

                if (!float.IsNaN(clamped))
                    t = clamped >= EdgeMargin ? MathF.Min(clamped, link.Length - EdgeMargin) : EdgeMargin;
            }
        }
        return link.Origin + t * link.Direction;
    }

    /// <summary>
    /// Pushes a point far enough inside a piece of ground for a body of that radius to stand on it.
    /// This is what a click gets before anyone walks at it, and it is the
    /// one place the engine uses the node's stored half-planes rather than the surface outline.
    ///
    /// Four passes, because pushing off one edge can push the point over another; if it is still
    /// outside after the fourth, the engine gives up and the move does not happen. The height is
    /// then taken from the surface's own plane, so the point ends up on the floor rather than
    /// wherever the click landed.
    /// </summary>
    public bool ClampInside(int node, ref Vector3 point, float radius, in NilPlane floor)
    {
        if (node < 0 || node >= count) return false;
        var planes = Level.PathNodes[node].Planes;
        var p = point;
        bool moved = false;
        for (int pass = 0; ; pass++)
        {
            int i = 0;
            for (; i < planes.Length; i++)
            {
                // The stored half-plane is (a, b, d) in 2-D: inside is a·x + b·y >= d.
                float outside = p.X * planes[i].X + p.Y * planes[i].Y - planes[i].Z - radius;
                if (outside >= PlaneSlack) continue;
                p.X -= outside * planes[i].X;
                p.Y -= outside * planes[i].Y;
                moved = true;
                break;
            }
            if (i == planes.Length) break;                  // clear of every edge
            if (pass >= 3) return false;                    // four tries and still cornered
        }
        if (moved && floor.Normal.Z != 0)
            p.Z = (floor.Dist - floor.Normal.X * p.X - floor.Normal.Y * p.Y) / floor.Normal.Z;
        point = p;
        return true;
    }

    /// <summary>How far outside a node's own edge a point may be before it is pushed in.</summary>
    public const float PlaneSlack = -0.1f;

    /// <summary>
    /// Turns the parent chain into the route, goal first and then backwards. Nodes that carry a
    /// pass-through link get no waypoint of their own — you walk on through them — and a chain
    /// longer than 255 steps is no route at all, which is the engine's own limit rather than ours.
    /// </summary>
    bool Build(int goal, out Waypoint[] path)
    {
        path = [];
        int steps = 0;
        for (int n = goal; fromLink[n] >= 0; n = fromNode[n])
        {
            if (passThrough[n]) continue;
            if (++steps > MaxSteps) return false;
        }
        var route = new Waypoint[steps];
        int at = steps;
        for (int n = goal; fromLink[n] >= 0; n = fromNode[n])
        {
            if (passThrough[n]) continue;
            route[--at] = new Waypoint(n, crossing[n]);
        }
        path = route;
        return true;
    }

    /// <summary>
    /// The open list is a linked list kept in order of estimate, and a new node goes in **after**
    /// everything that ties with it — so among equals the engine takes whichever it met first, and
    /// a heap would not give the same route.
    /// </summary>
    void Insert(int node)
    {
        int after = -1;
        int at = head;
        while (at >= 0 && f[at] <= f[node]) { after = at; at = next[at]; }
        next[node] = at;
        prev[node] = after;
        if (after < 0) head = node; else next[after] = node;
        if (at >= 0) prev[at] = node;
        open[node] = true;
        openCount++;
    }

    void Unlink(int node)
    {
        int before = prev[node], after = next[node];
        if (before >= 0) next[before] = after;
        if (after >= 0) prev[after] = before;
        if (head == node) head = after;
        next[node] = prev[node] = -1;
        openCount--;
    }
}
