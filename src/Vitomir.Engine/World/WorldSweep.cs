// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine's sweep: a line through the world, and everything it meets.
//
// `simCollisionQuery::Sweep` is the one question the engine asks of the
// level and of the things in it at once. The physics step asks it for every move, the
// ground test asks it straight down, the click asks it through the camera (`collidePos`),
// and the hitscan, the explosions, the clear shot and the pointer ask it too. It is a
// line of **zero thickness**, not a capsule.
//
// It floods out from a start sector through every portal whose plane the line reaches from the
// front, and in each sector it visits it asks three things:
//
//   * **things** in the sector's own list, through the query's filter, by the line
//     against the thing's body — sphere, cylinder, box (ours: every
//     candidate the caller gives is tried once, wherever it is filed; see Line);
//   * **surfaces**: the line is clipped to the sector's convex volume, the clipped piece
//     walked down the sector's collision tree marking the planes it straddles where solid lies
//     beyond, and every surface of a non-detail group on a marked plane met one-sided
//     and tested for being inside the polygon;
//   * **camera regions** (flag 0x20): the regions listed in the sector that collide with
//     cameras, the line clipped against each one's hull — only the camera asks (`Asks.Regions`).
//
// The answers come back nearest first, ties in the order they were found, and at most
// 256 of them (the replacement rule of the results' add). The camera's fat ray is the same query
// with a radius, and is here too (`Fat`).
//
// **One copy.** This was built for the physics and the click, and the camera's two casts
// separately (`CameraSweep`, its own query, surfaces and body tests). They were the same arithmetic
// line for line; they are now this one query, the camera asking it through `CameraSweep`. Where the
// two had differed, the engine decided: the things of a sector are walked with that sector, before
// its surfaces (the camera's way; `Asks.Filed`), a removed region is not met
// (this copy's way), and no surface is passed over for having fewer than three corners (neither's
// way: the thin ray's and the fat ray's surface tests have no such guard; none of the 751,952 non-detail surfaces of the
// 112 shipped levels has, so the answers are the same).
//
// Everything here is the engine's arithmetic in the engine's order, including its odd corners: a
// sphere a line stops short of by less than its radius is met at distance 0, a cylinder is met where
// the line crosses the line through its axis square to the start's bearing on it rather than where
// it comes closest, and a surface is only ever met from its front. What is ours is the storage — a
// list of results rather than the query's 6 K block on the stack — and the visited set, which is a
// set rather than 1.1's stamps (whose byte-wide plane stamp wraps at 256; the effect
// of that is one extra exact test, never a different answer).
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>The collision sweep: a line through the world.</summary>
public static class WorldSweep
{
    /// <summary>How many results a query holds (256 records of 24 bytes).</summary>
    public const int MaxResults = 256;

    /// <summary>
    /// How far either side of a plane a segment's end may be and still count as on that side, for
    /// the walk down the collision tree (0.1 in front, −0.1 behind).
    /// </summary>
    public const float TreeSlack = 0.1f;

    /// <summary>How close to a plane counts as on it.</summary>
    public const float OnPlane = 1e-5f;

    /// <summary>
    /// The length the click's sweep is given — no limit at all (the float
    /// <c>0x7f7fc99e</c>, 3.4 × 10³⁸).
    /// </summary>
    public const float Unlimited = 3.4e38f;

    /// <summary>
    /// What a sweep asks: the query's flags word, its type mask and the one
    /// thing it leaves out. <see cref="Pick.Tests.Unknown4"/> is the engine's bit `0x4`,
    /// **pass over the dead** — a character whose dead flag is set.
    /// </summary>
    /// <param name="Regions">
    /// The query's bit `0x20`: the place's regions, asked through each flooded sector's own list of
    /// them (filled at scene load) — only those flagged to collide with
    /// cameras (region bit 2) and with a hull, the line clipped against the hull in the region's
    /// own frame (in the start sector and after it). Neither the type mask nor the
    /// filter applies to them. Null leaves the bit clear, as every caller but the camera does.
    /// </param>
    /// <param name="Filed">
    /// The things **as the engine files them**, by sector: when given, each sector
    /// the line floods has its own things tried as it is entered, before its surfaces (the cast for
    /// the first, the flood after it), and the things whose sector the scene never
    /// recorded once, with the first sector's (ours). The thing list passed beside it is then not
    /// used. Null tries that list instead, every candidate once, first (ours; see
    /// <see cref="Line"/>): the physics, the hitscan, the sight line and the clear shot ask that
    /// way, so a thing whose position was written without its sector (as Vitomir's tests do) is
    /// still found where it stands.
    /// </param>
    public readonly record struct Asks(Pick.Tests Tests, WorldThing? Ignore = null, uint TypeMask = 0,
                                       Regions? Regions = null, Bodies? Filed = null)
    {
        public bool Things => (Tests & Pick.Tests.Things) != 0;
        public bool Surfaces => (Tests & Pick.Tests.Surfaces) != 0;
        public bool PassDead => (Tests & Pick.Tests.Unknown4) != 0;
    }

    /// <summary>
    /// Everything a line from <paramref name="start"/> meets along the **unit**
    /// <paramref name="direction"/> within <paramref name="length"/>, nearest first — the sweep of
    /// The cast from <paramref name="sector"/>, and the order the results are handed out in.
    /// </summary>
    /// <param name="things">
    /// The things that may be met; null asks about the level alone. <b>Ours:</b> every one of them is
    /// tried, wherever it is filed, where the engine walks the lists of the sectors its line floods.
    /// The answers are the same but for a thing whose body reaches into the line
    /// from a sector the flood does not enter — the flood is wide, crossing any portal whose plane
    /// the line reaches — and this way a thing whose position was written without its sector (which
    /// 1.1 never does, and Vitomir's tests do) is still found where it stands. A caller that
    /// wants only some kinds may pass only those.
    /// </param>
    public static List<Contact> Line(NilLevel level, IEnumerable<WorldThing>? things, int sector, Vector3 start,
                                     Vector3 direction, float length, Asks asks)
    {
        var query = new Query(level, asks.Things ? things : null, start, direction, length, asks);
        query.Run(sector);
        var found = query.InOrder();
        if (asks.Surfaces && Levels.Cells.CellLevel.Of(level) is { } cells)   // a cooked level's surfaces are its triangles (ours)
            Levels.Cells.CellLines.AddSurfaces(cells, found, start, direction, length);
        return found;
    }

    /// <summary>
    /// **The fat ray** — the camera's
    /// question: a sphere of <paramref name="radius"/>
    /// whose centre travels <paramref name="length"/> from <paramref name="start"/>. Three things
    /// differ from <see cref="Line"/>: a portal is crossed when its plane is reached within
    /// **length + radius**; **every** surface of a non-detail group is asked, the tree not walked —
    /// the sphere's first touch of the plane along the clipped line, or the start if it already
    /// touches (inline in the fat ray's surface test), and then whether the circle it cuts reaches the
    /// polygon; and the distance recorded is that contact's projection on the line,
    /// clamped to [0, length]. **Things are met by the thin line**, unwidened (the sphere test is passed a
    /// line radius of 0). Nothing slides: what the camera does with the answer is the camera's
    /// (<see cref="CameraSweep.GameCamera"/>, its one caller, as in 1.1).
    /// </summary>
    public static List<Contact> Fat(NilLevel level, IEnumerable<WorldThing>? things, int sector, Vector3 start,
                                    Vector3 direction, float length, float radius, Asks asks)
    {
        var query = new Query(level, asks.Things ? things : null, start, direction, length, asks, radius);
        query.Run(sector);
        return query.InOrder();
    }

    /// <summary>
    /// Where a sphere of <paramref name="radius"/> moving from <paramref name="a"/> to
    /// <paramref name="b"/> first touches a plane: nowhere if it is moving away (the end further in
    /// front than the start) or starts more than the radius behind; the start itself if it already
    /// touches; otherwise the point along the segment where the distance falls to the radius, if it
    /// gets there.
    /// </summary>
    public static bool Touches(NilPlane plane, Vector3 a, Vector3 b, float radius, out Vector3 contact)
    {
        contact = a;
        float da = Vector3.Dot(a, plane.Normal) - plane.Dist;
        float db = Vector3.Dot(b, plane.Normal) - plane.Dist;
        if (!(db <= da && -radius <= da)) return false;
        if (da < radius) return true;
        da -= radius;
        db -= radius;
        if (!(0f < da && db < 0f)) return false;
        float t = -(da / (db - da));
        contact = (b - a) * t + a;
        return true;
    }

    /// <summary>
    /// Whether a circle of <paramref name="radius"/> about a point on a surface's plane
    /// reaches the polygon: <see cref="Pick.Inside"/>'s edge test on the same two axes, but an edge
    /// the point lies outside of is forgiven when the point is within the radius of the edge's line
    /// — measured on those two axes, not in the plane, which is the engine's own arithmetic.
    /// </summary>
    public static bool Reaches(NilSector sector, NilSurface surface, NilPlane plane, Vector3 point, float radius)
    {
        var n = plane.Normal;
        var m = new Vector3(MathF.Abs(n.X), MathF.Abs(n.Y), MathF.Abs(n.Z));
        int axis = m.X <= m.Y ? (m.Z < m.Y ? 1 : 2) : (m.Z < m.X ? 0 : 2);
        (int u, int v) = axis switch
        {
            0 => Component(n, 0) > 0 ? (2, 1) : (1, 2),
            1 => Component(n, 1) > 0 ? (0, 2) : (2, 0),
            _ => Component(n, 2) > 0 ? (1, 0) : (0, 1),
        };
        int count = surface.NumVerts;
        for (int i = 0; i < count; i++)
        {
            var a = sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];
            var b = sector.Vertices[sector.SectorVertices[surface.FirstVertex + (i + 1) % count].Vertex];
            float eu = Component(b, u) - Component(a, u), ev = Component(b, v) - Component(a, v);
            float pu = Component(point, u) - Component(a, u), pv = Component(point, v) - Component(a, v);
            float cross = ev * pu - pv * eu;
            if (cross >= 0f) continue;
            // The edge normalised across the two axes, and the test again against −radius.
            float len = MathF.Sqrt(ev * ev + eu * eu);
            if (len != 0f) { eu /= len; ev /= len; }
            if (ev * pu - pv * eu < -radius) return false;
        }
        return true;
    }

    // ---- the ground, and the click -----------------------------------------------------------------

    /// <summary>How far above the feet the ground ray starts.</summary>
    public const float GroundAbove = 50f;

    /// <summary>How long the ground ray is for anything but a sphere or a box.</summary>
    public const float GroundLength = 140f;

    /// <summary>
    /// **The ground ray** — the ground under a thing: the sweep, surfaces only, cast straight
    /// down from <see cref="GroundAbove"/> over <paramref name="feet"/>, <see cref="GroundLength"/>
    /// long for a cylinder or no body, the radius plus that for a sphere and that less the box's
    /// lowest point for a box, from the sector the ray's start is in (the sector search,
    /// <see cref="SectorSearch.Find"/>, with <paramref name="hint"/>, the thing's own, as the hint;
    /// none, and there is no ground). The first result on a **walk-flagged** surface is the ground;
    /// any other surface it meets on the way down is passed over (a ceiling is not met at all from
    /// above, the sweep being one-sided). Answers null when there is none. The walker's route step
    /// asks it (<see cref="WalkMap.Stride"/>).
    /// </summary>
    public static Contact? Ground(NilLevel level, int hint, Vector3 feet,
                                  CollideShape shape = CollideShape.Cylinder, float radius = 0f, float boxBottom = 0f)
    {
        var from = feet with { Z = feet.Z + GroundAbove };
        float length = shape switch
        {
            CollideShape.Sphere => radius + GroundLength,
            CollideShape.Box => GroundLength - boxBottom,
            _ => GroundLength,
        };
        int sector = SectorSearch.Find(level, from, hint);
        if (sector < 0) return null;
        foreach (var c in Line(level, null, sector, from, -Vector3.UnitZ, length, new Asks(Pick.Tests.Surfaces)))
            if (c.Kind == ContactKind.Surface && c.Walkable) return c;
        return null;
    }

    /// <summary>
    /// **`collidePos`** and its twin — the first surface a ray meets: the sweep
    /// with surfaces only, from <paramref name="sector"/>, by default with no length
    /// limit (<see cref="Unlimited"/>). The engine returns the surface and writes the point; so does
    /// this. <b>It answers no thing</b>: the thing under the pointer is the pointer's own pair of
    /// sweeps, not this.
    /// </summary>
    public static Contact? FirstSurface(NilLevel level, int sector, Vector3 origin, Vector3 direction,
                                        float length = Unlimited)
    {
        var all = Line(level, null, sector, origin, direction, length, new Asks(Pick.Tests.Surfaces));
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// **What the pointer is over**, as the click and the pointer ask it: <see cref="FirstSurface"/>
    /// — `collidePos` for a click, and the pointer's own surfaces-only sweep (the interface's
    /// hover) for the rollover, which is the same question — from the sector the camera is in,
    /// answered in <see cref="Pick.Nearest"/>'s shape so the click chain reads it unchanged. It
    /// agrees with <see cref="Pick.Nearest"/> but on a detail brush, which this goes through to what
    /// lies beyond, as 1.1 does, and on the back of a surface, which this does not see.
    ///
    /// The camera's sector is found from <paramref name="hint"/> (the game keeps its own at
    /// the camera's own; Vitomir's camera keeps none, so the steered character's is the hint). **Ours:**
    /// a camera in no sector at all — inside a wall's thickness, which the pull-in makes rare —
    /// falls back to <see cref="Pick.Nearest"/>, rather than casting from a sector it is not in.
    /// </summary>
    public static Hit? Pointer(NilLevel level, Collision? solid, int hint, Vector3 origin, Vector3 direction)
    {
        if (Levels.Cells.CellLevel.Of(level) is { } cells) return Levels.Cells.CellSweep.Pointer(cells, origin, direction);   // a cooked level's triangles (ours)
        int sector = solid?.SectorAt(origin, hint) ?? -1;
        if (sector < 0) return Pick.Nearest(level, origin, direction);
        if (FirstSurface(level, sector, origin, direction) is not { } met) return null;
        var surface = level.Sectors[met.Sector].Surfaces[met.Surface];
        return new Hit(met.Point, met.Sector, met.Surface, surface.PathNode, met.Walkable, met.Distance);
    }

    /// <summary>
    /// <see cref="Pointer(NilLevel, Collision?, int, Vector3, Vector3)"/> in a place, hinted by its player's sector — or, while
    /// the frame drew a level surface faded for being in the camera's way, <see cref="Pick.NearestShown"/>, which looks through
    /// exactly what was faded (ours, <see cref="SeeThrough"/>).
    /// </summary>
    public static Hit? Pointer(Location place, Vector3 origin, Vector3 direction) =>
        place.SeeThrough.AnySurfaces && Levels.Cells.CellLevel.Of(place.Level) is null
            ? Pick.NearestShown(place.Level, origin, direction, place.SeeThrough)
            : Pointer(place.Level, place.Solid, place.Player is { } p ? (int)p.Thing.Sector : -1, origin, direction);

    /// <summary>
    /// The sector a point is in, as the sector search finds it: the hint's, when the point is inside
    /// it; otherwise the first sector that holds it; **and the hint again when none does** — the
    /// engine never answers "nowhere". Collision's own <see cref="Collision.SectorAt"/> does the
    /// looking (with its quarter-unit slack, which is ours) and this adds the fallback.
    /// </summary>
    public static int SectorOf(NilLevel level, Collision? solid, Vector3 point, int hint)
    {
        int found = solid?.SectorAt(point, hint) ?? -1;
        if (found >= 0) return found;
        return hint >= 0 && hint < level.Sectors.Length ? hint : -1;
    }

    // ---- the three bodies ---------------------------------------------------------------------------

    /// <summary>
    /// A line against a sphere of <paramref name="radius"/> about <paramref name="centre"/>,
    /// the line itself <paramref name="lineRadius"/> thick (the sweep passes 0). The closest approach
    /// must lie between 0 and length + radius along the line and pass within the radius. The
    /// distance recorded is where the line enters the sphere **when that is inside the line, and 0
    /// otherwise** — so a line that starts inside, and a line that stops short of the sphere by less
    /// than its radius, both meet it at 0. A zero-length line is a point test.
    /// </summary>
    public static bool MeetsSphere(Vector3 start, Vector3 direction, float length, float lineRadius,
                                   Vector3 centre, float radius, out float distance)
    {
        distance = 0f;
        float reach = lineRadius + radius;
        var towards = centre - start;
        if (length == 0f) return towards.Length() < reach;
        float along = Vector3.Dot(towards, direction);
        if (along < 0f || along > reach + length) return false;
        float off = (centre - (start + along * direction)).Length();
        if (!(off < reach)) return false;
        float enter = along - MathF.Sqrt(reach * reach - off * off);
        distance = enter <= length && enter >= 0f ? enter : 0f;
        return true;
    }

    /// <summary>
    /// A line against an upright cylinder standing on <paramref name="feet"/>. First the
    /// span of the line between the cylinder's floor and roof, clamped into [0, length] (the whole
    /// line when it is level, if it starts within the height). Then, across the ground, **not the
    /// closest approach**: the distance at which the line crosses the line through the axis square
    /// to the start's own bearing on it, <c>|d| / (d̂ · dir)</c>, which must lie in
    /// [0, length + radius] and is then clamped into the height span. A hit when that point is
    /// inside the radius, recorded at that distance less the depth, clamped to [0, length].
    /// </summary>
    public static bool MeetsCylinder(Vector3 start, Vector3 direction, float length,
                                     Vector3 feet, float radius, float height, out float distance)
    {
        distance = 0f;
        float low, high;
        if (direction.Z == 0f)
        {
            if (start.Z < feet.Z) return false;
            if (height + feet.Z < start.Z) return false;
            low = 0f; high = length;
        }
        else
        {
            float t1 = (feet.Z - start.Z) / direction.Z;
            float t2 = (height + feet.Z - start.Z) / direction.Z;
            if (t1 < 0f && t2 < 0f) return false;
            if (length < t1 && length < t2) return false;
            float a = t1 < 0f ? 0f : (length < t1 ? length : t1);
            float b = 0f;
            if (t2 >= 0f) b = length < t2 ? length : t2;
            low = a < b ? a : b;
            high = b < a ? a : b;
        }

        // The flat normalise normalises the start-to-axis vector across the ground and answers its length.
        float dx = feet.X - start.X, dy = feet.Y - start.Y;
        float across = MathF.Sqrt(dy * dy + dx * dx);
        if (across != 0f) { dx /= across; dy /= across; }
        float t = across / (dy * direction.Y + dx * direction.X);
        if (!(t >= 0f && t <= length + radius)) return false;
        if (t < low) t = low;
        else if (high < t) t = high;

        float px = feet.X - (t * direction.X + start.X), py = feet.Y - (t * direction.Y + start.Y);
        float off = MathF.Sqrt(py * py + px * px);
        if (!(off < radius)) return false;
        float d = t - (radius - off);
        distance = d >= 0f ? (length < d ? length : d) : 0f;
        return true;
    }

    /// <summary>
    /// A line against a thing's box: the model's bounds (<paramref name="min"/>,
    /// <paramref name="max"/>) placed by the thing's transform. An exact
    /// slab clip, axis by axis, the far face then the near; the distance recorded is how far along
    /// the line it enters, 0 when it starts inside.
    /// </summary>
    public static bool MeetsBox(Vector3 start, Vector3 direction, float length,
                                in Matrix4x4 rotation, Vector3 position, Vector3 min, Vector3 max, out float distance)
    {
        distance = 0f;
        var s = start;
        var e = start + length * direction;
        for (int i = 0; i < 3; i++)
        {
            var axis = Axis(rotation, i);
            float centre = Vector3.Dot(position, axis);

            // The far face: dot(axis, p) <= centre + max.
            float plane = centre + Component(max, i);
            float ds = Vector3.Dot(s, axis), de = Vector3.Dot(e, axis);
            if (ds <= plane)
            {
                if (plane < de) e = (plane - ds) / Vector3.Dot(axis, direction) * direction + s;
            }
            else
            {
                if (plane < de) return false;
                s = (ds - plane) / -Vector3.Dot(axis, direction) * direction + s;
            }

            // The near face, on the reversed axis: dot(−axis, p) <= −(centre + min).
            var back = -axis;
            plane = -centre - Component(min, i);
            ds = Vector3.Dot(s, back); de = Vector3.Dot(e, back);
            if (ds <= plane)
            {
                if (plane < de) e = (plane - ds) / Vector3.Dot(back, direction) * direction + s;
            }
            else
            {
                if (plane < de) return false;
                s = (ds - plane) / -Vector3.Dot(back, direction) * direction + s;
            }
        }
        distance = Vector3.Dot(s, direction) - Vector3.Dot(start, direction);
        return true;
    }

    /// <summary>
    /// Does the line reach a portal's **plane** from the front within its length. Only the
    /// plane: the portal's corners are never looked at, so the flood goes wider than the line does.
    /// </summary>
    public static bool ReachesPlane(Vector3 start, Vector3 direction, float length, Vector3 normal, Vector3 onPlane)
    {
        float d = Vector3.Dot(start - onPlane, normal);
        if (MathF.Abs(d) <= OnPlane) d = 0f;
        else if (d < 0f) return false;
        if (length < d) return false;
        float closing = -Vector3.Dot(direction, normal);
        if (closing <= 0f) return false;
        if (d <= 0f) return true;
        return !(closing * length < d);
    }

    /// <summary>
    /// A line meeting a surface's plane, **one-sided**: the start must be in front of the
    /// plane (or within <see cref="OnPlane"/> of it, which counts as on it), the line closing on it,
    /// and the meeting point within the length. The distance is never negative.
    /// </summary>
    public static bool MeetsPlane(Vector3 start, Vector3 direction, float length, NilPlane plane, out float distance)
    {
        distance = 0f;
        float d = Vector3.Dot(start, plane.Normal) - plane.Dist;
        if (MathF.Abs(d) <= OnPlane) d = 0f;
        else if (d < 0f) return false;
        if (!(d <= length)) return false;
        float closing = -Vector3.Dot(direction, plane.Normal);
        if (!(closing > 0f)) return false;
        if (d <= 0f) return true;
        if (!(d <= closing * length)) return false;
        distance = MathF.Max(d / closing, 0f);
        return true;
    }

    /// <summary>
    /// The part of a line inside a sector's convex volume: each boundary plane in turn
    /// pulls the end in where the line leaves or pushes the start on where it comes in, and a line
    /// that never gets inside answers false.
    /// </summary>
    public static bool Clip(NilSector sector, ref Vector3 start, Vector3 direction, ref float length)
    {
        foreach (var plane in sector.SectorPlanes)
        {
            float d = Vector3.Dot(start, plane.Normal) - plane.Dist;
            float along = Vector3.Dot(plane.Normal, direction);
            if (d >= 0f)
            {
                if (along < 0f && d < -(along * length)) length = -(d / along);
            }
            else
            {
                if (along <= 0f) return false;
                if (along * length < -d) return false;
                float t = -(d / along);
                start = t * direction + start;
                length -= t;
            }
        }
        return true;
    }

    /// <summary>
    /// The segment <paramref name="a"/>–<paramref name="b"/> down a sector's collision
    /// tree. Both ends <see cref="TreeSlack"/> or more in front of a node's plane: its front child;
    /// both that far behind: its back child; otherwise **both** children, the start's side first,
    /// and the node's plane — with its partner, index <c>^ 1</c> — is marked when either reaches a
    /// solid leaf. Answers whether a solid leaf was reached at all.
    /// </summary>
    public static bool Mark(NilSector sector, Vector3 a, Vector3 b, HashSet<int> marked, int node = 0)
    {
        int count = sector.BspCount;
        for (int guard = 0; guard < 4096; guard++)
        {
            if (node < 0 || node >= count) return false;
            var n = sector.Bsp(node);
            if ((n.Kind & NilBspNode.LeafBit) != 0) return (n.Kind & NilBspNode.SolidBit) != 0;
            if (n.Plane >= sector.SurfacePlanes.Length) return false;
            var plane = sector.SurfacePlanes[n.Plane];
            float da = Vector3.Dot(plane.Normal, a) - plane.Dist;
            float db = Vector3.Dot(plane.Normal, b) - plane.Dist;
            if (da < TreeSlack || db < TreeSlack)
            {
                if (-TreeSlack <= da || -TreeSlack <= db)
                {
                    // Straddling: both sides, the start's first.
                    int first = da >= 0f ? n.Front : n.Back, second = da >= 0f ? n.Back : n.Front;
                    bool reached = false;
                    if (first > node && Mark(sector, a, b, marked, first)) { Plant(marked, n.Plane); reached = true; }
                    if (!(second > node && Mark(sector, a, b, marked, second))) return reached;
                    Plant(marked, n.Plane);
                    return true;
                }
                // Both behind.
                if (n.Back <= node) return false;
                node = n.Back;
            }
            else
            {
                if (n.Front <= node) return false;
                node = n.Front;
            }
        }
        return false;
    }

    static void Plant(HashSet<int> marked, int plane)
    {
        marked.Add(plane);
        marked.Add(plane ^ 1);
    }

    static Vector3 Axis(in Matrix4x4 m, int i) => i switch
    {
        0 => new Vector3(m.M11, m.M12, m.M13),
        1 => new Vector3(m.M21, m.M22, m.M23),
        _ => new Vector3(m.M31, m.M32, m.M33),
    };

    static float Component(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    // ---- the query --------------------------------------------------------------------------------

    /// <summary>One sweep in progress: the query record, less its storage.</summary>
    sealed class Query(NilLevel level, IEnumerable<WorldThing>? things, Vector3 start, Vector3 direction,
                       float length, Asks asks, float fat = 0f)
    {
        readonly List<Contact> results = [];
        readonly HashSet<int> visited = [];
        bool looseTried;

        /// <summary>The cast's own sector, then the flood (every sector after it).</summary>
        public void Run(int sector)
        {
            // A candidate list: the things first, as the start sector's are, each once
            // (ours: see Line). Filed things are met sector by sector, in Visit.
            if (things is not null && asks.Filed is null) Things(things);
            if (sector < 0 || sector >= level.Sectors.Length) return;
            Visit(sector);
        }

        void Visit(int index)
        {
            visited.Add(index);
            var sector = level.Sectors[index];
            // The sector's own things first (the cast's first loop; each flooded sector opens with it).
            if (asks.Things && asks.Filed is { } filed) Things(Filed(filed, index));
            if (asks.Surfaces)
            {
                if (fat > 0f) FatSurfaces(index, sector);
                else Surfaces(index, sector);
            }
            if (asks.Regions is { } regions) Hulls(index, regions);
            // The portals, depth first in their own order (and in the flood).
            foreach (var portal in sector.Portals)
            {
                int next = (int)portal.NeighbourSector;
                if (next < 0 || next >= level.Sectors.Length || visited.Contains(next)) continue;
                if (portal.Quad.Length == 0) continue;
                // The fat ray crosses within length + radius.
                if (!ReachesPlane(start, direction, length + fat, portal.Normal, portal.Quad[0])) continue;
                Visit(next);
            }
        }

        void Things(IEnumerable<WorldThing> list)
        {
            foreach (var thing in list)
            {
                if (!Considers(thing)) continue;
                float d;
                bool met = thing.Collide switch
                {
                    CollideShape.Sphere => MeetsSphere(start, direction, length, 0f, thing.Position, thing.Radius, out d),
                    CollideShape.Cylinder => MeetsCylinder(start, direction, length, thing.Position, thing.Radius, thing.Height, out d),
                    CollideShape.Box => MeetsBox(start, direction, length, thing.Rotation, thing.Position, thing.BoxMin, thing.BoxMax, out d),
                    _ => Nothing(out d),
                };
                if (!met) continue;
                // The engine records a thing's point as zero; the point on the line is ours, for
                // the callers' convenience.
                Add(new Contact(ContactKind.Thing, d, start + direction * d, thing, (int)thing.Sector, -1, Vector3.Zero, false));
            }
        }

        static bool Nothing(out float d) { d = 0f; return false; }

        /// <summary>
        /// The things filed in one sector (each thing's next) — and, the first
        /// time only, those the scene left without a sector, which the engine never has (ours).
        /// </summary>
        IEnumerable<WorldThing> Filed(Bodies filed, int index)
        {
            var list = filed.In([index]);
            if (looseTried) return list.Where(t => t.Sector == index);
            looseTried = true;
            return list;
        }

        /// <summary>The query's filter (and its inline copy).</summary>
        bool Considers(WorldThing thing)
        {
            if (!thing.InWorld || thing.Collide == CollideShape.None) return false;   // in a pack, or no body
            if (asks.PassDead && thing.Type is ThingType.Actor or ThingType.Player
                && thing.Actor is { } actor && actor.Dead) return false;
            if ((asks.TypeMask & (1u << ((int)thing.Type & 31))) != 0) return false;
            if (ReferenceEquals(thing, asks.Ignore)) return false;
            return !thing.Has(WorldThing.Flag.DisableCollide);
        }

        /// <summary>The surfaces of one sector (for the first sector and for the rest alike).</summary>
        void Surfaces(int index, NilSector sector)
        {
            if (sector.BspCount == 0) return;                         // no tree, no surfaces
            var from = start;
            float reach = length;
            if (!Clip(sector, ref from, direction, ref reach)) return;
            var marked = new HashSet<int>();
            if (!Mark(sector, from, reach * direction + from, marked)) return;

            for (int g = 0; g < sector.Groups.Length; g++)
            {
                var group = sector.Groups[g];
                if ((group.SurfaceFlags & NilGroup.FlagDetail) != 0) continue;
                for (int f = group.FirstSurface; f < group.FirstSurface + group.NumSurfaces && f < sector.Surfaces.Length; f++)
                {
                    var surface = sector.Surfaces[f];
                    if (!marked.Contains(surface.Plane)) continue;
                    if (surface.Plane >= sector.SurfacePlanes.Length) continue;   // ours: a bad index; no corner count is asked
                    var plane = sector.SurfacePlanes[surface.Plane];
                    // Measured from the *unclipped* start, over the whole length.
                    if (!MeetsPlane(start, direction, length, plane, out float t)) continue;
                    var point = t * direction + start;
                    if (!Pick.Inside(sector, surface, plane, point)) continue;
                    Add(new Contact(ContactKind.Surface, t, point, null, index, f, plane.Normal,
                                    (group.SurfaceFlags & NilGroup.FlagWalk) != 0));
                }
            }
        }

        /// <summary>
        /// The query's `0x20` list in one sector: each region in the sector's
        /// slots that collides with cameras and has a hull, the line taken into the region's own
        /// frame and clipped plane by plane — the end pulled in where it leaves, the start pushed on
        /// where it comes in, nothing if both ends are outside any one plane — and recorded as a
        /// thing at the distance the clipped start lies along the line. A region listed in two
        /// flooded sectors is recorded twice, as the engine records it.
        /// </summary>
        void Hulls(int index, Regions regions)
        {
            foreach (var region in regions.In(index))
            {
                if (region is null || region.Removed || (region.RegionFlags & World.Regions.BlocksCameras) == 0) continue;
                if (region.Scene.Model is not { Planes.Length: > 0 } model) continue;
                var r = region.Rotation;
                var s = World.Regions.Local(region, start);
                var d = new Vector3(Vector3.Dot(direction, new Vector3(r.M11, r.M12, r.M13)),
                                    Vector3.Dot(direction, new Vector3(r.M21, r.M22, r.M23)),
                                    Vector3.Dot(direction, new Vector3(r.M31, r.M32, r.M33)));
                var a = s;
                var b = length * d + s;
                bool outside = false;
                foreach (var plane in model.Planes)
                {
                    var n = new Vector3(plane.X, plane.Y, plane.Z);
                    float da = Vector3.Dot(a, n) - plane.W, db = Vector3.Dot(b, n) - plane.W;
                    if (da < 0f)
                    {
                        if (db < 0f) continue;
                        b = (b - a) * (da / (da - db)) + a;               // leaves here: the end pulled in
                    }
                    else
                    {
                        if (db >= 0f) { outside = true; break; }         // both ends outside this plane
                        a = (b - a) * (da / (da - db)) + a;               // comes in here: the start pushed on
                    }
                }
                if (outside) continue;
                float t = Vector3.Dot(d, a) - Vector3.Dot(d, s);
                Add(new Contact(ContactKind.Thing, t, start + direction * t, region, index, -1, Vector3.Zero, false));
            }
        }

        /// <summary>
        /// The fat ray's surfaces in one sector (for the first, and the same for the
        /// rest): the line clipped to the sector, no tree walked, every surface of a non-detail group
        /// asked where the sphere first touches its plane and whether that circle reaches it.
        /// </summary>
        void FatSurfaces(int index, NilSector sector)
        {
            if (sector.BspCount == 0) return;                         // no tree, no surfaces
            var from = start;
            float reach = length;
            if (!Clip(sector, ref from, direction, ref reach)) return;
            var to = reach * direction + from;
            for (int g = 0; g < sector.Groups.Length; g++)
            {
                var group = sector.Groups[g];
                if ((group.SurfaceFlags & NilGroup.FlagDetail) != 0) continue;
                for (int f = group.FirstSurface; f < group.FirstSurface + group.NumSurfaces && f < sector.Surfaces.Length; f++)
                {
                    var surface = sector.Surfaces[f];
                    if (surface.Plane >= sector.SurfacePlanes.Length) continue;   // ours: a bad index; no corner count is asked
                    var plane = sector.SurfacePlanes[surface.Plane];
                    if (!Touches(plane, from, to, fat, out var contact)) continue;
                    if (!Reaches(sector, surface, plane, contact, fat)) continue;
                    // The contact's projection on the line, clamped to [0, length].
                    float t = Vector3.Dot(contact, direction) - Vector3.Dot(start, direction);
                    if (t < 0f) t = 0f;
                    else if (length < t) t = length;
                    Add(new Contact(ContactKind.Surface, t, contact, null, index, f, plane.Normal,
                                    (group.SurfaceFlags & NilGroup.FlagWalk) != 0));
                }
            }
        }

        /// <summary>
        /// A new result, or — the query being full — in place of the furthest
        /// result that is further than it, or nowhere.
        /// </summary>
        void Add(Contact contact)
        {
            if (results.Count < MaxResults) { results.Add(contact); return; }
            int worst = -1;
            float far = contact.Distance;
            for (int i = 0; i < results.Count; i++)
                if (far < results[i].Distance) { far = results[i].Distance; worst = i; }
            if (worst >= 0) results[worst] = contact;
        }

        /// <summary>The results, taken until it answers nothing: nearest first, the first found among equals.</summary>
        public List<Contact> InOrder() => [.. results.Select((c, i) => (c, i)).OrderBy(p => p.c.Distance).ThenBy(p => p.i).Select(p => p.c)];
    }
}
