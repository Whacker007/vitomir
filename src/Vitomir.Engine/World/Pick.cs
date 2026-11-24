// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What you clicked on.
//
// The original casts a ray from the camera through the mouse and asks the collision system what it
// hits (the ground click → `collidePos`). What comes back is a surface and a
// point, and then one line decides whether a click means anything at all:
//
//     if ((pSurface->pGroup->surfaceFlags & SURFACE_WALK) == 0) return;
//
// So **you can only click on walkable floor**. Clicking a wall, a building, the sky or a barrel
// moves nobody — the click is simply dropped. That single test is why the original's movement never
// walks you into scenery: it never accepts a destination that is not ground in the first place.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>Where a ray met the level.</summary>
/// <param name="Point">The point on the surface, in world space.</param>
/// <param name="Sector">Which sector the surface belongs to.</param>
/// <param name="Surface">Its index in that sector.</param>
/// <param name="PathNode">Its path node, or −1 — which is also what says it is walkable.</param>
/// <param name="Walkable">Whether the surface carries SURFACE_WALK, the flag a click needs.</param>
public readonly record struct Hit(Vector3 Point, int Sector, int Surface, int PathNode, bool Walkable, float Distance);

/// <summary>
/// What kind of result a contact is, using **the engine's own values** out of the first word of a
/// result record (its first field, read in the hitscan's own loop).
///
/// <b>These are not the numbers the query asks with</b>, and that is a trap worth naming loudly: a
/// query's flags word is `0x1` things and `0x2` surfaces, while a *result*'s
/// kind is `0x2` thing and `0x4` surface. Two encodings, one bit apart, in the same subsystem.
/// <see cref="Pick.Tests"/> is the other one.
/// </summary>
[Flags]
public enum ContactKind { None = 0, Thing = 2, Surface = 4 }

/// <summary>
/// One thing a ray met. A trace answers a list of these; the engine answers them one at a time
/// through `NextResult` and its callers pull until it gives nothing.
/// </summary>
/// <param name="Distance">How far along the ray, which is what puts them in order.</param>
/// <param name="Thing">Set for a <see cref="ContactKind.Thing"/>; null for a surface.</param>
/// <param name="Normal">The surface's plane normal; zero for a thing.</param>
public readonly record struct Contact(ContactKind Kind, float Distance, Vector3 Point,
                                      WorldThing? Thing, int Sector, int Surface,
                                      Vector3 Normal, bool Walkable);

/// <summary>Casting rays at a level: what the mouse is over, and what a click would mean.</summary>
public static class Pick
{
    /// <summary>
    /// Which kinds of result a query asks for — the engine's query flags word, `0x1` things and
    /// `0x2` surfaces. **Deliberately a different type from <see cref="ContactKind"/>**, whose
    /// numbering differs, so the two cannot be passed to each other by accident.
    ///
    /// The two shipped readings are the evidence: the **click** sets the word to `2` and gets
    /// surfaces, so it passes through barrels and people (and `Rpg.Cursor` handles the
    /// thing case separately); the **walker** asks for both and keeps only the thing results,
    /// which is why a walker is blocked by a crate and not by a wall. The hitscan sets
    /// `0x1 | 0x2 | 0x4` and wants everything.
    ///
    /// **Bit `0x4` is pass over the dead** (identified): the query's filter (and its inline
    /// copy) skips a character whose dead flag is set when it is on. `WorldSweep` honours it;
    /// the name is kept so nothing that already passes it changes.
    /// </summary>
    [Flags]
    public enum Tests { None = 0, Things = 1, Surfaces = 2, Unknown4 = 4, Both = Things | Surfaces }

    /// <summary>
    /// A ray through a pixel, from the camera. <paramref name="x"/> and <paramref name="y"/> are in
    /// window pixels with the origin top left, which is how both SDL and the original report the
    /// mouse.
    ///
    /// <paramref name="rows"/> is how many of the window's rows the world is actually drawn into,
    /// from the top — fewer than <paramref name="height"/> while the status bar is up, because the
    /// world stops above it (`Ui.StatusBar.WorldBottom`). **It has to match what was drawn or the
    /// ray misses what the player is pointing at**: the vertical half-angle shrinks with the
    /// viewport while the horizontal one does not, since the scale is pinned by the width. Zero or
    /// negative means the whole window, which is what a view with no interface over it gets.
    ///
    /// A pixel **below** the last world row, or off the world's columns, is not on the world at all and
    /// the caller should not be asking: the ray is still worked out, through a place the picture does
    /// not show. <see cref="WorldFrame.Shows(float, float, int, int)"/> is that test, and every pointer
    /// event asks it first.
    /// </summary>
    public static (Vector3 Origin, Vector3 Direction) Ray(FreeCamera camera, float x, float y,
                                                          int width, int height, int rows = 0) =>
        Ray(camera, x, y, width, height, rows, View);

    /// <summary>The same through the columns <paramref name="view"/> gives the world rather than the ones in force.</summary>
    public static (Vector3 Origin, Vector3 Direction) Ray(FreeCamera camera, float x, float y,
                                                          int width, int height, int rows, ViewColumns view)
    {
        if (rows <= 0 || rows > height) rows = height;
        // The columns the world is drawn into while a pane is up, and its magnification.
        var (x0, cols) = view.Pixels(width);
        // Pixel to normalised device coordinates, then back out through the camera.
        float ndcX = 2f * (x - x0 + 0.5f) / cols - 1f;
        float ndcY = 1f - 2f * (y + 0.5f) / rows;
        float aspect = (float)cols / rows;
        // The same narrowing `FrameRenderer.WorldViewport` applies, so that the horizontal
        // half-extent stays `tan(fov/2) x width/height` whatever the viewport and only the vertical
        // one follows it.
        float tan = MathF.Tan(camera.VerticalFov * 0.5f) * rows / height / view.Magnify;
        var forward = camera.Forward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        var direction = Vector3.Normalize(forward + right * (ndcX * tan * aspect) + up * (ndcY * tan));
        return (camera.Position, direction);
    }

    /// <summary>
    /// The plane normal of the surface a <see cref="Hit"/> landed on, or null when the hit names no
    /// surface. This is the engine's own <c>planeArray[polyIndex]</c> with its sixteen-byte stride
    /// — a <see cref="NilPlane"/> is a <see cref="Vector3"/> and a float, which is the
    /// same sixteen bytes.
    /// </summary>
    public static Vector3? NormalAt(NilLevel level, Hit hit)
    {
        if (hit.Sector < 0 || hit.Sector >= level.Sectors.Length) return null;
        var sector = level.Sectors[hit.Sector];
        if (hit.Surface < 0 || hit.Surface >= sector.Surfaces.Length) return null;
        int plane = sector.Surfaces[hit.Surface].Plane;
        if (plane < 0 || plane >= sector.SurfacePlanes.Length) return null;
        return sector.SurfacePlanes[plane].Normal;
    }

    /// <summary>
    /// Whether a window pixel is on the world at all. Below the world's last row there is nothing
    /// to point at — not something hidden behind the status bar, but nothing drawn — so a pick
    /// there has no answer and the pointer must not take one from the geometry that would have been
    /// there. This is what keeps the cursor from turning into a sword over the interface.
    /// **The rows alone**: whose a pointer event is, across the world's columns and under a bar that
    /// lies over the world as well, is <see cref="WorldFrame.Shows(float, float, int, int)"/>,
    /// which the hover and every click ask.
    /// </summary>
    public static bool OnWorld(float y, int height, int rows) =>
        y >= 0f && y < (rows <= 0 || rows > height ? height : rows);

    /// <summary>
    /// The columns the world is drawn into now (<see cref="WorldView"/>), which the frame
    /// and every pick share. The program sets it once a frame; a test that sets it puts it back.
    /// </summary>
    public static ViewColumns View { get; set; } = ViewColumns.Whole;

    /// <summary>
    /// The nearest surface a ray meets, or null. Every surface in the level is a candidate — the
    /// walls included — because the original's click test has to be able to tell "you clicked a
    /// wall" from "you clicked the floor behind it".
    /// </summary>
    public static Hit? Nearest(NilLevel level, Vector3 origin, Vector3 direction, float far = 100_000f)
    {
        Hit? best = null;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var sector = level.Sectors[si];
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                if (surface.NumVerts < 3) continue;
                var plane = sector.SurfacePlanes[surface.Plane];
                float denominator = Vector3.Dot(plane.Normal, direction);
                if (MathF.Abs(denominator) < 1e-6f) continue;
                float t = (plane.Dist - Vector3.Dot(plane.Normal, origin)) / denominator;
                if (t <= 0f || t >= far) continue;
                if (best is { } b && t >= b.Distance) continue;
                var point = origin + direction * t;
                if (!Inside(sector, surface, plane, point)) continue;
                uint flags = sector.Groups[surface.Group].SurfaceFlags;
                best = new Hit(point, si, fi, surface.PathNode, (flags & NilGroup.FlagWalk) != 0, t);
            }
        }
        return best;
    }

    /// <summary>
    /// **What the pointer is over while things are seen through** (ours; <see cref="SeeThrough"/>): the nearest level
    /// surface the ray meets, either side, **passing over a surface the frame drew faded where it is drawn more clear than not**
    /// (<see cref="SeeThrough.LooksThrough"/>: anywhere on it under the x-ray, inside the soft window otherwise) — and, as the pointer's own
    /// sweep does, a Detail group's (<see cref="WorldSweep.Pointer(NilLevel, Collision?, int, Vector3, Vector3)"/>) — and the
    /// Sky and Nodraw groups, which are not drawn. So a click on the floor behind a faded wall lands on the floor. Every
    /// surface of the level is tried, wherever the eye is: zoomed out, it may be in no sector or in one that does not see
    /// the floor through a portal. Asked only while something is faded; otherwise the pointer is the base game's.
    /// </summary>
    public static Hit? NearestShown(NilLevel level, Vector3 origin, Vector3 direction, SeeThrough seen, float far = 100_000f)
    {
        Hit? best = null;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var sector = level.Sectors[si];
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                if (surface.NumVerts < 3) continue;
                uint flags = sector.Groups[surface.Group].SurfaceFlags;
                if ((flags & (NilGroup.FlagDetail | NilGroup.FlagSky | NilGroup.FlagNodraw)) != 0) continue;
                var plane = sector.SurfacePlanes[surface.Plane];
                float denominator = Vector3.Dot(plane.Normal, direction);
                if (MathF.Abs(denominator) < 1e-6f) continue;
                float t = (plane.Dist - Vector3.Dot(plane.Normal, origin)) / denominator;
                if (t <= 0f || t >= far) continue;
                if (best is { } b && t >= b.Distance) continue;
                var point = origin + direction * t;
                if (!Inside(sector, surface, plane, point)) continue;
                if (seen.LooksThrough(si, fi, point)) continue;
                best = new Hit(point, si, fi, surface.PathNode, (flags & NilGroup.FlagWalk) != 0, t);
            }
        }
        return best;
    }

    /// <summary>
    /// Whether a point on a surface's plane is inside the surface. This is the engine's own test:
    /// drop the plane's dominant axis, then require the point to be on the
    /// same side of every edge in turn. It treats every surface as convex, which 3,881 of the
    /// game's 35,266 walkable floors are not — measured over the whole game, that costs 0.06% of
    /// sampled points, so the original's own answer is what we use here.
    /// </summary>
    public static bool Inside(NilSector sector, NilSurface surface, NilPlane plane, Vector3 point)
    {
        var n = plane.Normal;
        var magnitude = new Vector3(MathF.Abs(n.X), MathF.Abs(n.Y), MathF.Abs(n.Z));
        int axis = magnitude.X > magnitude.Y ? (magnitude.Z > magnitude.X ? 2 : 0)
                                            : (magnitude.Z > magnitude.Y ? 2 : 1);
        // The projection pair, ordered by the sign of the dominant component so the winding of
        // floors and ceilings comes out the same way round (the engine's two tables).
        (int u, int v) = axis switch
        {
            0 => Component(n, 0) > 0 ? (2, 1) : (1, 2),
            1 => Component(n, 1) > 0 ? (0, 2) : (2, 0),
            _ => Component(n, 2) > 0 ? (1, 0) : (0, 1),
        };
        int count = surface.NumVerts;
        for (int i = 0; i < count; i++)
        {
            var a = Vertex(sector, surface, i);
            var b = Vertex(sector, surface, (i + 1) % count);
            float au = Component(a, u), av = Component(a, v);
            if ((Component(b, v) - av) * (Component(point, u) - au) -
                (Component(point, v) - av) * (Component(b, u) - au) < 0f) return false;
        }
        return true;
    }

    static Vector3 Vertex(NilSector sector, NilSurface surface, int i) =>
        sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex];

    static float Component(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    // --- things ------------------------------------------------------------------------------

    /// <summary>
    /// How wide a thing with no collision body of its own is still clickable at. 88 of the actor
    /// templates present no body at all (they never bump into anything), and the player has to be
    /// able to click them just the same, so they are picked as a cylinder of their RADIUS — or of
    /// this, when they have not got one either.
    /// </summary>
    public const float ClickRadius = 40f, ClickHeight = 96f;

    /// <summary>
    /// How much wider than its body a thing is to click. A body is what a character bumps into; a
    /// click has to survive a moving target, a moving camera and a person aiming with a mouse, and
    /// the original's cursor is noticeably forgiving in play.
    /// </summary>
    public const float ClickSlack = 12f;

    /// <summary><c>THING_RENDERFLAG_DONTRENDER</c>: a character hidden by a script, which the pointer does not find (ours; <see cref="PointerMeets"/>).</summary>
    public const uint HiddenFlag = 0x2;

    /// <summary>What a ray met on its way through the world.</summary>
    /// <param name="Distance">How far along the ray, so a thing behind a wall can be discarded.</param>
    public readonly record struct ThingHit(WorldThing Thing, float Distance);

    /// <summary>
    /// The nearest thing a ray passes through, out of <paramref name="things"/>, no further than
    /// <paramref name="far"/> — which the caller sets to the distance of the surface behind it, so
    /// a character on the other side of a wall is not picked.
    ///
    /// **Ours in method.** The engine's pointer asks its own things-only sweep out to 2,000 and then a
    /// surfaces-only one up to the thing (the interface's hover, read — `collidePos`,
    /// answers surfaces only); what the *click* then does with the thing is in
    /// <see cref="Rpg.Cursor"/>. Each thing is met by the engine's own body test for its shape,
    /// widened by <see cref="ClickSlack"/> for a moving mouse (<see cref="PointerMeets"/>).
    /// </summary>
    /// <param name="offers">
    /// An extra per-thing test, applied inside the walk the way the engine applies `Offers` inside
    /// its own (`Ui.Highlight.Offers`) — so the answer is the nearest thing that **passes**, not
    /// the nearest thing that a later test then throws away. Null accepts everything, which is
    /// what a raw geometric query wants.
    /// </param>
    public static ThingHit? NearestThing(IEnumerable<WorldThing> things, Vector3 origin, Vector3 direction,
                                         float far = 100_000f, WorldThing? ignore = null,
                                         Func<WorldThing, bool>? offers = null) =>
        NearestThing(things, origin, direction, out _, far, ignore, offers);

    /// <summary>
    /// <paramref name="blocked"/> is `THING_FLAG_BLOCKSELECT` reporting for duty: **a thing the
    /// pointer cannot take, but which stops the pointer reaching anything behind it**. The engine's
    /// rollover pass breaks out of its own iteration the moment it meets one
    /// (the hover sets its flag and stops), and the click handler then does *nothing at all* —
    /// the dispatcher answers "handled" for the whole dispatch, so the click reaches neither the
    /// thing nor the ground behind it.
    ///
    /// That combination is how the monastery's raising doors are built: `door_raise1_nhl` carries
    /// `NO HIGHLIGHT` **and** `BLOCKSELECT`, so it is not pointable, it swallows the click, and the
    /// only way to open it is the switch its `DoorSwitchPivot` script is watching. Clicking the
    /// door itself is *meant* to do nothing — including not walking you into it, which is what
    /// Vitomir did, because the click fell through to the floor beyond.
    /// </summary>
    public static ThingHit? NearestThing(IEnumerable<WorldThing> things, Vector3 origin, Vector3 direction,
                                         out bool blocked, float far = 100_000f, WorldThing? ignore = null,
                                         Func<WorldThing, bool>? offers = null)
    {
        ThingHit? best = null;
        float swallow = float.MaxValue;
        foreach (var thing in things)
        {
            if (ReferenceEquals(thing, ignore)) continue;
            if (thing.Has(WorldThing.Flag.Remove)) continue;
            // The sweep's own filter passes over a thing whose collision is off before
            // its body is looked at, so the pointer never meets it — not even to be stopped by it.
            if (thing.Has(WorldThing.Flag.DisableCollide)) continue;
            // Ours: a thing the frame drew faded for being in the camera's way is looked through (SeeThrough).
            if (thing.SeeThroughLevel > 0f) continue;
            bool takeable = offers is null || offers(thing);
            // A blocker that cannot be pointed at is still in the way, so it has to be measured
            // before it is dismissed.
            if (!takeable && !thing.Has(WorldThing.Flag.BlockSelect)) continue;
            if (PointerMeets(thing, origin, direction, far) is not { } d) continue;
            if (d > far) continue;
            if (!takeable) { swallow = MathF.Min(swallow, d); continue; }
            if (best is null || d < best.Value.Distance) best = new ThingHit(thing, d);
        }
        // Nearer than anything pointable, so the pointer never reached past it.
        blocked = swallow < (best?.Distance ?? float.MaxValue);
        return blocked ? null : best;
    }

    /// <summary>
    /// How far along the pointer's ray it meets <paramref name="thing"/>'s body, or null. **The
    /// engine's own three bodies** — the pointer's things-only sweep (the hover's
    /// line of no thickness) meets each thing as its collide shape says: a **sphere**
    /// about its position, a **cylinder** standing on its feet and a **box**,
    /// the bounds placed by the thing's transform — through the same
    /// <see cref="WorldSweep.MeetsSphere"/>, <see cref="WorldSweep.MeetsCylinder"/> and
    /// <see cref="WorldSweep.MeetsBox"/> the rest of the engine's lines use.
    ///
    /// The box is what the level exits are. Golden Lane's way to Judith Bridge is a `tracker_thing`
    /// with no model, a box 328 tall **centred** 144 above the floor, drawn over the level's own
    /// door. Measured as a cylinder rising from its position — the old test here, for every shape —
    /// it began at the door's middle, so only the upper half of the door took the pointer:
    /// near its top it did, lower it would not. A sphere had the
    /// same fault, its lower half missing.
    ///
    /// **Ours:** each body is still widened by <see cref="ClickSlack"/> — a sphere's and a
    /// cylinder's radius, and a box on each of its own three axes — for a moving target and a hand on
    /// a mouse.
    ///
    /// **A thing with no body is never met** (`COLLIDETYPE NONE`), as the engine's sweep meets none.
    /// Until then Vitomir met a bodiless character as a cylinder of its radius — ours — and that
    /// is what stopped a player talking to Vienna's weaponsmith: Friedrich's whetting wheel is
    /// a character of its own (`ViennaSmithyWheel`: `TALK TO`, no body, no script) standing seven units
    /// in front of him, so the pointer lit the wheel, gave the talk cursor, and a click on it went to no
    /// script. In 1.1 the pointer passes through the wheel to Friedrich. Six templates are such
    /// characters (the wheel, a flying bat, a crow, Orvus's bat, Emelia's cat and a Cappadocian skeleton).
    /// </summary>
    public static float? PointerMeets(WorldThing thing, Vector3 origin, Vector3 direction, float far = 100_000f)
    {
        // The engine's sweep is 2,000 long; ours stops at the surface behind instead,
        // and a box's slab clip wants a finite end.
        float length = MathF.Min(far, 100_000f);
        float d;
        var slack = new Vector3(ClickSlack);
        bool met = thing.Collide switch
        {
            CollideShape.Sphere => WorldSweep.MeetsSphere(origin, direction, length, 0f, thing.Position,
                                                          thing.Radius + ClickSlack, out d),
            CollideShape.Cylinder => WorldSweep.MeetsCylinder(origin, direction, length, thing.Position,
                                                              thing.Radius + ClickSlack, thing.Height, out d),
            CollideShape.Box => WorldSweep.MeetsBox(origin, direction, length, thing.Rotation, thing.Position,
                                                    thing.BoxMin - slack, thing.BoxMax + slack, out d),
            _ => NoBody(out d),
        };
        return met ? d : null;

        static bool NoBody(out float at)
        {
            at = 0f;
            return false;
        }
    }

    /// <summary>
    /// **Everything** a ray meets, nearest first — the query the hitscan and the projectile's
    /// contact handlers need, and the one piece of `simCollision` three features were waiting on.
    ///
    /// The engine's shape is an iterator, not a list: reset the query, cast it,
    /// then pull results with `NextResult(kindMask)` until it answers null.
    /// A caller walks that sequence and decides for itself when to stop — which is exactly why a
    /// shot can pass through its own owner, through a corpse and through a dropped sword and still
    /// hit the person behind them. <see cref="Nearest"/> and <see cref="NearestThing"/> cannot
    /// express that; they answer one hit and it may be the wrong one.
    ///
    /// <b>Ordered by distance.</b> That is inferred from the consumer rather than known:
    /// The hitscan moves the projectile to the first *surface* it is given and stops
    /// there, so results arriving out of order would let a far wall swallow a shot that should have
    /// hit someone near. No other ordering makes the caller correct.
    ///
    /// <paramref name="ignore"/> is the query's own exclusion (which the hitscan
    /// sets to the projectile itself). The owner skip is **not** here: the engine does
    /// that in the loop, per result, because it compares GUIDs rather than pointers.
    ///
    /// Ours in method — a loop over every sector, where the engine floods portals along the ray —
    /// and the same in answer.
    /// </summary>
    public static List<Contact> Trace(NilLevel? level, IEnumerable<WorldThing>? things,
                                      Vector3 origin, Vector3 direction, float far = 100_000f,
                                      Tests tests = Tests.Both, WorldThing? ignore = null)
    {
        var found = new List<Contact>();

        if ((tests & Tests.Surfaces) != 0 && level is not null)
            for (int si = 0; si < level.Sectors.Length; si++)
            {
                var sector = level.Sectors[si];
                for (int fi = 0; fi < sector.Surfaces.Length; fi++)
                {
                    var surface = sector.Surfaces[fi];
                    if (surface.NumVerts < 3) continue;
                    var plane = sector.SurfacePlanes[surface.Plane];
                    float denominator = Vector3.Dot(plane.Normal, direction);
                    if (MathF.Abs(denominator) < 1e-6f) continue;
                    float t = (plane.Dist - Vector3.Dot(plane.Normal, origin)) / denominator;
                    if (t <= 0f || t >= far) continue;
                    var point = origin + direction * t;
                    if (!Inside(sector, surface, plane, point)) continue;
                    uint flags = sector.Groups[surface.Group].SurfaceFlags;
                    found.Add(new Contact(ContactKind.Surface, t, point, null, si, fi,
                                          plane.Normal, (flags & NilGroup.FlagWalk) != 0));
                }
            }

        if ((tests & Tests.Things) != 0 && things is not null)
            foreach (var thing in things)
            {
                if (ReferenceEquals(thing, ignore)) continue;
                if (thing.Removed || thing.Has(WorldThing.Flag.Remove)) continue;
                if (!Clickable(thing, out float radius, out float height)) continue;
                if (Meets(thing.Position, radius, height, origin, direction) is not { } d) continue;
                if (d > far) continue;
                found.Add(new Contact(ContactKind.Thing, d, origin + direction * d, thing,
                                      (int)thing.Sector, -1, Vector3.Zero, false));
            }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return found;
    }

    /// <summary>
    /// The body a click tests against. A thing's own <see cref="CollideShape"/> when it has one,
    /// falling back to its RADIUS and HEIGHT, and then to <see cref="ClickRadius"/> — because a
    /// thing with no body is still something the player can point at.
    /// </summary>
    public static bool Clickable(WorldThing thing, out float radius, out float height)
    {
        radius = thing.Radius > 0f ? thing.Radius : ClickRadius;
        height = thing.Height > 0f ? thing.Height : ClickHeight;
        // A sphere stands on its centre; everything else rises from the thing's feet. Treating the
        // sphere as a cylinder of the same radius is close enough for a cursor and keeps one test.
        return thing.IsActor || thing.Collide != CollideShape.None;
    }

    /// <summary>
    /// Where a ray first meets an upright cylinder standing on <paramref name="feet"/>, or null.
    /// Solved on the ground plane and then clipped to the cylinder's own span, which is how every
    /// non-sphere test in the engine measures.
    /// </summary>
    public static float? Meets(Vector3 feet, float radius, float height, Vector3 origin, Vector3 direction)
    {
        var d = new Vector2(direction.X, direction.Y);
        var m = new Vector2(origin.X - feet.X, origin.Y - feet.Y);
        float a = Vector2.Dot(d, d);
        float enter, exit;

        if (a < 1e-9f)
        {
            // Straight up or down the axis: it is inside the circle or it never meets it.
            if (m.LengthSquared() > radius * radius) return null;
            enter = float.NegativeInfinity; exit = float.PositiveInfinity;
        }
        else
        {
            float b = Vector2.Dot(m, d), c = Vector2.Dot(m, m) - radius * radius;
            float disc = b * b - a * c;
            if (disc < 0f) return null;
            float root = MathF.Sqrt(disc);
            enter = (-b - root) / a;
            exit = (-b + root) / a;
        }

        // Then the ends, which is the ordinary slab clip.
        float bottom = feet.Z, top = feet.Z + height;
        if (MathF.Abs(direction.Z) < 1e-9f)
        {
            if (origin.Z < bottom || origin.Z > top) return null;
        }
        else
        {
            float t0 = (bottom - origin.Z) / direction.Z, t1 = (top - origin.Z) / direction.Z;
            if (t0 > t1) (t0, t1) = (t1, t0);
            enter = MathF.Max(enter, t0);
            exit = MathF.Min(exit, t1);
        }

        if (exit < enter || exit < 0f) return null;
        return enter < 0f ? 0f : enter;     // starting inside it counts as a hit at zero
    }
}
