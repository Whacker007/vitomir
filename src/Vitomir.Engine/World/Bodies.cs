// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What you bump into that isn't the building.
//
// `Collision` answers "is this point inside the level's own geometry". This answers the other half:
// the barrels, carts, crates, chests and people the level compiler knows nothing about, because
// they are scene things placed on top of it.
//
// This is 1.1's collision, and all of the following is as 1.1 has it
// rather than reasoned about:
//
//   * Every thing carries a **collide type** (template tag 12, THING_COLLIDE_*), a **radius**
//     (tag 13) and a **height** (tag 14). The thing constructor zeroes all three, and
//     the template applier takes `collide` only when it is 0..3 and the two sizes only when they
//     are not negative. So a prop whose template says nothing about collision has no
//     body and nothing ever touches it — 263 of the game's 2,619 templates say nothing about collision
//     and 220 of those are props,
//     which is why some scenery is meant to be walked through.
//   * A **box** thing's body is its model's own bounding box, copied out of the loaded model when
//     the thing is finalized (six floats — the first thing the model
//     loader reads after the header).
//   * A walker asks the body test `(pThing, newPos)`: it finds the sector the new position is in,
//     collects every thing in reach, and returns the first that blocks. A walker that is not itself
//     a sphere or a cylinder is never blocked by anything.
//   * Reach is a **portal flood**: start in the walker's sector, and step through a
//     portal into its neighbour when the walker is in front of that portal's plane by less than its
//     radius. Only things in the sectors that flood reaches are considered.
//   * The six shape pairings each have their own test, and they are not symmetric — sphere-vs-
//     sphere measures in three dimensions, everything else measures across the ground and handles
//     height separately. `Hit` below is those six functions.
//
// None of this keeps anyone on the ground or out of walls; that is `WalkMap` and `Collision`.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The things in a place that have a body, indexed by the sector they are in, and the tests that
/// say whether someone walking into one is stopped by it.
/// </summary>
public sealed class Bodies
{
    readonly NilLevel level;
    readonly List<WorldThing>[] bySector;
    readonly List<WorldThing> loose = [];       // things whose sector the scene did not record

    /// <summary>
    /// The thing types a walker ignores outright. The walker's query sets its ignore mask to
    /// 0x472 — items, projectiles, weapons, armour and type 10 — so
    /// characters walk through dropped swords but not through each other or the scenery.
    /// </summary>
    public const uint WalkerIgnoreMask = 0x472;

    public Bodies(NilLevel level, IEnumerable<WorldThing> things)
    {
        this.level = level;
        bySector = new List<WorldThing>[level.Sectors.Length];
        foreach (var t in things) Add(t);
    }

    void Add(WorldThing t)
    {
        if (t.Collide == CollideShape.None) return;
        if (t.Sector >= 0 && t.Sector < bySector.Length) Link(t.Sector, t);
        else loose.Add(t);
    }

    /// <summary>
    /// A thing goes in **at the head** of its sector's list, as the engine links it — placed in the
    /// world or moved across a portal: the thing's next = the old head,
    /// the sector's head = the thing. The queries walk the list from the
    /// head, so within a sector the newest arrival comes first. It decides whose
    /// shout is heard last — and so whom a rally leaves the selected member aiming at: in the convent
    /// fight 1.1's Christof ends on the torch-bearing nun, placed before the other one. Vitomir
    /// appended until then and walked them oldest first.
    /// </summary>
    void Link(long sector, WorldThing t) => (bySector[sector] ??= []).Insert(0, t);

    /// <summary>
    /// Puts a thing that has just come into the world into its sector's list. The engine links it
    /// into the sector's own doubly-linked chain of things (the sector's head, with next and previous
    /// on the thing) as the last step of placing it; this keeps the same
    /// membership without the pointers.
    /// </summary>
    public void Enter(WorldThing t) => Add(t);

    /// <summary>Takes a thing that has left the world out of its sector's list.</summary>
    public void Leave(WorldThing t)
    {
        if (t.Sector >= 0 && t.Sector < bySector.Length) bySector[t.Sector]?.Remove(t);
        loose.Remove(t);
    }

    /// <summary>
    /// Follows a thing from one sector to another, which is what the engine's SetPosition does when
    /// a walker crosses a portal.
    /// </summary>
    public void Moved(WorldThing thing, long from, long to)
    {
        if (from == to) return;
        if (from >= 0 && from < bySector.Length) bySector[from]?.Remove(thing);
        else loose.Remove(thing);
        if (to >= 0 && to < bySector.Length) Link(to, thing);
        else if (thing.Collide != CollideShape.None) loose.Add(thing);
    }

    /// <summary>How many things in this place have a body at all.</summary>
    public int Count => bySector.Sum(s => s?.Count ?? 0) + loose.Count;

    /// <summary>
    /// The sectors a body of that radius standing at <paramref name="at"/> can reach: its own, and
    /// then out through any portal it is within <paramref name="radius"/> of. This is the engine's
    /// own search — a portal is crossed when the point is in front of its plane (the
    /// normal points back into the sector that owns it) by less than the radius.
    /// </summary>
    public IReadOnlyList<int> SectorsNear(Vector3 at, float radius, int start)
    {
        var found = new List<int>();
        if (start < 0 || start >= level.Sectors.Length) return found;
        found.Add(start);
        var seen = new HashSet<int> { start };
        for (int i = 0; i < found.Count && found.Count < 256; i++)
            foreach (var portal in level.Sectors[found[i]].Portals)
            {
                int next = (int)portal.NeighbourSector;
                if (next < 0 || next >= level.Sectors.Length || seen.Contains(next)) continue;
                if (portal.Quad.Length == 0) continue;
                float d = Vector3.Dot(at - portal.Quad[0], portal.Normal);
                if (d < 0f || d >= radius) continue;
                seen.Add(next);
                found.Add(next);
            }
        return found;
    }

    /// <summary>The things with a body in any of those sectors, plus any the scene left unplaced.</summary>
    public IEnumerable<WorldThing> In(IReadOnlyList<int> sectors)
    {
        foreach (int s in sectors)
            if (bySector[s] is { } list)
                foreach (var t in list) yield return t;
        foreach (var t in loose) yield return t;
    }

    /// <summary>
    /// What stops <paramref name="mover"/> if it stands at <paramref name="at"/>, or null if nothing
    /// does — **the walker's body test**.
    ///
    /// The last rule is the one that matters in play: a thing only blocks when it is *ahead* —
    /// <c>dot(thing − me, direction) &gt; 0</c>. That is what lets a character who has
    /// somehow ended up inside a barrel walk back out of it instead of being trapped. **"Me" is where
    /// the walker stands now, not the point being tested, and the direction is its three-dimensional
    /// move direction** (it reads the walker's position and the unit
    /// direction of the leg `StartWalk` set) — which a slide does not change: the slide asks
    /// about the slid-to point with the leg's own direction still set. Earlier, this
    /// measured from the new point along a flat heading, and a slide passed its own direction (ours).
    /// </summary>
    /// <param name="mover">The walker.</param>
    /// <param name="at">The point it would stand on.</param>
    /// <param name="from">Where it stands now.</param>
    /// <param name="direction">Its move direction, a unit vector.</param>
    /// <param name="sector">The sector the flood starts from.</param>
    public WorldThing? Blocker(WorldThing mover, Vector3 at, Vector3 from, Vector3 direction, int sector)
    {
        // A walker that is not a sphere or a cylinder is never blocked.
        if (mover.Collide is not (CollideShape.Sphere or CollideShape.Cylinder)) return null;
        WorldThing? best = null;
        float nearest = float.MaxValue;
        foreach (var thing in In(SectorsNear(at, mover.Radius, sector)))
        {
            if (!Considers(mover, thing)) continue;
            if (!Hit(mover.Collide, at, mover.Radius, mover.Height, thing, out float distance)) continue;
            if (!Blocks(thing)) continue;
            if (Vector3.Dot(thing.Position - from, direction) <= 0f) continue;
            if (distance >= nearest) continue;
            nearest = distance;
            best = thing;
        }
        return best;
    }

    /// <summary>
    /// What is standing where someone wants to walk to. `rpgAction::MoveTo` asks this before it
    /// starts and **refuses the move outright** when the nearest thing there
    /// is not the walker itself (<see cref="FreeSpot"/>).
    ///
    /// The filters are not the walker's: the walker itself is not excluded, and the move order sets
    /// its own (<see cref="FreeSpot.Passed"/>) — a plain item (type 1) and a dead body
    /// are passed over, a dropped sword (a weapon thing, type 5) is not. Earlier, this counted items
    /// and bodies too, on a reading that had the query reset with no filters.
    /// </summary>
    public WorldThing? Occupied(WorldThing mover, Vector3 at, int sector)
    {
        if (mover.Collide is not (CollideShape.Sphere or CollideShape.Cylinder)) return null;
        WorldThing? best = null;
        float nearest = float.MaxValue;
        foreach (var thing in In(SectorsNear(at, mover.Radius, sector)))
        {
            if (thing.Collide == CollideShape.None || FreeSpot.Passed(thing)) continue;
            if (!Hit(mover.Collide, at, mover.Radius, mover.Height, thing, out float distance)) continue;
            if (distance >= nearest) continue;
            nearest = distance;
            best = thing;
        }
        return best;
    }

    /// <summary>
    /// **Does a thing fit at a point** (a collision routine nothing obvious reaches; read)
    /// — the follow's slot test. Its own
    /// shape and radius (and height) put at the point — a sphere or a cylinder;
    /// anything else touches nothing — the flood from **its own sector** (the thing's sector), and
    /// every thing in the flooded sectors tested by its shape: for a sphere inline,
    /// against a sphere, a cylinder or a model; for a cylinder a
    /// sector at a time through the cylinder's sector test, against a sphere,
    /// a cylinder or a model. The answer is whether anything was
    /// hit at all (the hit count non-zero).
    ///
    /// The query is set up with **no filters of its own** (flag word 0, type mask
    /// 0) and with the thing itself as the one left out, so through the query's filter only
    /// a <c>DISABLECOLLIDE</c> thing is passed over: a body, a dropped sword and a plain item all
    /// count, a <c>NOBLOCK</c> thing counts, and there is no "ahead" rule. The flags word its one
    /// caller pushes (`0x62`) is never read.
    /// </summary>
    public bool Touches(WorldThing mover, Vector3 at, int sector)
    {
        if (mover.Collide is not (CollideShape.Sphere or CollideShape.Cylinder)) return false;
        foreach (var thing in In(SectorsNear(at, mover.Radius, sector)))
        {
            if (ReferenceEquals(thing, mover) || thing.Collide == CollideShape.None) continue;
            if (thing.Has(WorldThing.Flag.DisableCollide)) continue;
            if (Hit(mover.Collide, at, mover.Radius, mover.Height, thing, out _)) return true;
        }
        return false;
    }

    /// <summary>Whether a walker's query looks at that thing at all.</summary>
    public static bool Considers(WorldThing mover, WorldThing thing)
    {
        if (ReferenceEquals(mover, thing)) return false;
        if (thing.Collide == CollideShape.None) return false;
        if ((WalkerIgnoreMask & (1u << ((int)thing.Type & 31))) != 0) return false;
        if (thing.Has(WorldThing.Flag.DisableCollide)) return false;
        return true;
    }

    /// <summary>
    /// Whether a thing that was hit actually stops a walker. Two things do not:
    /// anything flagged NOBLOCK — which the flag's own comment in Codex.java describes as
    /// "will not block walkers but collides normally" — and a dead character, so that people can
    /// walk over the bodies.
    /// </summary>
    public static bool Blocks(WorldThing thing)
    {
        if (thing.Has(WorldThing.Flag.NoBlock)) return false;
        if (thing.Type is ThingType.Actor or ThingType.Player && thing.Actor is { } a &&
            (a.Flags & Actor.Flag.Dead) != 0) return false;
        return true;
    }

    /// <summary>
    /// Whether a mover of that shape standing at <paramref name="at"/> touches
    /// <paramref name="thing"/>, and how far away the engine records the hit as being.
    ///
    /// Six pairings, six functions in the original, and they do not agree with each other about
    /// what "distance" means. Sphere against sphere measures in three dimensions;
    /// everything else measures across the ground and tests the vertical overlap separately.
    /// A box hit is recorded at distance 0, and a cylinder-against-cylinder hit is recorded with
    /// an uninitialised stack slot — which on a 32-bit stack address is a denormal, so
    /// in practice both sort first and the original returns whichever it met first. Recording 0 for
    /// both is what reproduces that; it is the one place here that copies an effect rather than the
    /// arithmetic that caused it.
    /// </summary>
    public static bool Hit(CollideShape moverShape, Vector3 at, float radius, float height,
                           WorldThing thing, out float distance)
    {
        distance = 0f;
        switch (moverShape, thing.Collide)
        {
            case (CollideShape.Sphere, CollideShape.Sphere):
                // Straight three-dimensional sphere overlap.
                distance = Vector3.Distance(at, thing.Position);
                return distance <= radius + thing.Radius;

            case (CollideShape.Sphere, CollideShape.Cylinder):
                return SphereVsCylinder(at, radius, thing.Position, thing.Radius, thing.Height, out distance);

            case (CollideShape.Cylinder, CollideShape.Sphere):
                // The same function with the roles the other way up: the thing is the sphere.
                return SphereVsCylinder(thing.Position, thing.Radius, at, radius, height, out distance);

            case (CollideShape.Cylinder, CollideShape.Cylinder):
                // Overlap in Z, then distance across the ground.
                if (thing.Position.Z > at.Z + height || at.Z > thing.Position.Z + thing.Height) return false;
                return Distance2D(at, thing.Position) <= radius + thing.Radius;

            case (CollideShape.Sphere, CollideShape.Box):
                return SphereVsBox(at, radius, thing);

            case (CollideShape.Cylinder, CollideShape.Box):
                return CapsuleVsBox(at, radius, height, thing);

            default:
                return false;
        }
    }

    /// <summary>
    /// A sphere centred at <paramref name="centre"/> against a cylinder standing on
    /// <paramref name="feet"/>: the sphere spans <c>z ± r</c>, the cylinder <c>z … z + h</c>, and
    /// what is measured across the ground is the distance between the two axes.
    /// </summary>
    static bool SphereVsCylinder(Vector3 centre, float sphereRadius, Vector3 feet, float cylinderRadius,
                                 float cylinderHeight, out float distance)
    {
        distance = 0f;
        if (sphereRadius + feet.Z + cylinderHeight < centre.Z) return false;   // sphere above the cylinder
        if (sphereRadius + centre.Z < feet.Z) return false;                    // sphere below it
        distance = Distance2D(centre, feet);
        return distance <= sphereRadius + cylinderRadius;
    }

    /// <summary>
    /// A sphere against a box. The box's three axes are the thing's own; the sphere is a point widened by its
    /// radius. Only the box's axes are separated on — the original does not test the other five
    /// directions a proper separating-axis test would, so a sphere tucked into a corner of a
    /// turned box counts as touching it.
    /// </summary>
    static bool SphereVsBox(Vector3 centre, float radius, WorldThing thing)
    {
        var box = thing.Rotation;
        for (int i = 0; i < 3; i++)
        {
            var axis = Axis(box, i);
            float p = Vector3.Dot(axis, centre);
            float c = Vector3.Dot(axis, thing.Position);
            if (c + Component(thing.BoxMax, i) < p - radius) return false;
            if (p + radius < c + Component(thing.BoxMin, i)) return false;
        }
        return true;
    }

    /// <summary>
    /// A capsule against a box. The same three axes, but the mover is a capsule: its axis runs from
    /// <c>z + r</c> to <c>z + h − r</c> and is widened by <c>r</c>. So a walker really is tested as
    /// a rounded body against a box, even though against another cylinder it is a plain cylinder.
    /// </summary>
    static bool CapsuleVsBox(Vector3 feet, float radius, float height, WorldThing thing)
    {
        var box = thing.Rotation;
        var low = feet with { Z = feet.Z + radius };
        var high = feet with { Z = feet.Z + height - radius };
        for (int i = 0; i < 3; i++)
        {
            var axis = Axis(box, i);
            float a = Vector3.Dot(axis, low), b = Vector3.Dot(axis, high);
            float c = Vector3.Dot(axis, thing.Position);
            if (c + Component(thing.BoxMax, i) < a - radius && c + Component(thing.BoxMax, i) < b - radius) return false;
            if (a + radius < c + Component(thing.BoxMin, i) && b + radius < c + Component(thing.BoxMin, i)) return false;
        }
        return true;
    }

    /// <summary>One row of a thing's transform: the direction its local X, Y or Z points.</summary>
    static Vector3 Axis(in Matrix4x4 m, int i) => i switch
    {
        0 => new Vector3(m.M11, m.M12, m.M13),
        1 => new Vector3(m.M21, m.M22, m.M23),
        _ => new Vector3(m.M31, m.M32, m.M33),
    };

    static float Component(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    /// <summary>Distance across the ground. Everything but sphere-against-sphere uses this.</summary>
    public static float Distance2D(Vector3 a, Vector3 b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// How much clear space there is between two bodies: the distance between their centres, in
    /// three dimensions, less each one's radius. **Not the reach test** — 1.1's reach is measured
    /// across the ground and asks the heights overlap (<see cref="InReach"/>); this is
    /// Vitomir's own measure (ours), kept for what is not the reach test: the diagnostics, the
    /// spawner's overlap, and distances whose own routine has not been read as that.
    /// </summary>
    public static float Gap(WorldThing a, WorldThing b) =>
        Vector3.Distance(a.Position, b.Position) - a.Radius - b.Radius;

    /// <summary>
    /// **The reach test**: whether <paramref name="a"/> can reach
    /// <paramref name="b"/> across <paramref name="reach"/> — every melee close-in and blow, the AI's
    /// combat and approach tests, the take, the use, the bite and the stake ask it. It has two halves,
    /// both of which must pass:
    /// <list type="number">
    /// <item>**the two bodies' height spans overlap** (<see cref="HeightSpan"/>) —
    /// a character on a ledge above the other's head, or down a stairwell below its feet, is out of
    /// reach however close it stands across the ground;</item>
    /// <item>**the distance across the ground** (x and y only) **less both radii**
    /// (whatever the shape) is no more than the reach — so a target down a slope is as
    /// near as it is across the floor, where a three-dimensional measure found it out of reach
    /// sooner.</item>
    /// </list>
    /// A reach of 0 asks that the bodies touch; a negative one that they overlap by that much.
    ///
    /// <para>**The seam for hit ranges.** This is the one place a reach is measured: every reach 1.1
    /// measures comes here and nowhere else, so a later hitbox or hit-range modification
    /// replaces this method's body and every one of them follows. The
    /// measures that are <em>not</em> the reach test in 1.1 — the approach's stopping distance,
    /// the party's follow distance, the diagnostics — keep <see cref="Gap"/>.</para>
    /// </summary>
    public static bool InReach(WorldThing a, WorldThing b, float reach)
    {
        var (aLow, aHigh) = HeightSpan(a);
        var (bLow, bHigh) = HeightSpan(b);
        if (bLow > aHigh || aLow > bHigh) return false;
        // Double-precision arithmetic on the float coordinates, as in 1.1, less the two radii, against the reach.
        double dx = (double)b.Position.X - a.Position.X, dy = (double)b.Position.Y - a.Position.Y;
        return Math.Sqrt(dx * dx + dy * dy) - a.Radius - b.Radius <= reach;
    }

    /// <summary>
    /// The height a body occupies for the reach test, bottom and top, by its collide shape:
    /// a **cylinder** from its feet up by its height; a **box** from its
    /// position plus its own box's bottom to its position plus its top, unturned;
    /// **anything else** — a sphere, or a thing that collides with nothing — its radius
    /// either side of its centre.
    /// </summary>
    public static (float Low, float High) HeightSpan(WorldThing thing)
    {
        float z = thing.Position.Z;
        return thing.Collide switch
        {
            CollideShape.Cylinder => (z, z + thing.Height),
            CollideShape.Box => (z + thing.BoxMin.Z, z + thing.BoxMax.Z),
            _ => (z - thing.Radius, z + thing.Radius),
        };
    }
}
