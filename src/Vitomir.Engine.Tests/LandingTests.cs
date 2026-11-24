// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What falls in 1.1 and what does not.
/// A physics thing falls, and meets the level through the engine's own sweep
/// — so it lands on the floor's own plane, passes through every body on the
/// way, and is not stopped by leaving every sector. A walker does not fall at all: the ground is
/// looked for straight down from 50 over its feet for 140, so it steps down as much as
/// 90 in one step, and where there is nothing it goes on at the height of the floor it left.
/// </summary>
public class LandingTests
{
    static Location Monastery(Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    /// <summary>A walkable spot near Christof, with open air above it, and its floor.</summary>
    static (Vector3 Floor, long Sector) Spot(Location place)
    {
        var christof = place.Player!.Thing.Position;
        foreach (var node in place.Walk!.Nodes.OrderBy(n => Vector2.Distance((n.Min + n.Max) * 0.5f, new Vector2(christof.X, christof.Y))))
        {
            if (node.Outline.Length == 0 || node.Max.X - node.Min.X < 120f || node.Max.Y - node.Min.Y < 120f) continue;
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var floor = new Vector3(c.X, c.Y, node.Height(c.X, c.Y));
            // Nothing solid for 200 over it.
            if (Enumerable.Range(1, 20).Any(k => place.Solid!.Solid(floor + new Vector3(0f, 0f, k * 10f)))) continue;
            return (floor, place.Solid!.SectorAt(floor + new Vector3(0f, 0f, 16f), node.Sector));
        }
        throw new InvalidOperationException("no open floor near Christof");
    }

    /// <summary>
    /// A sword made a physics thing, falling — the dropped loot that falls, and a thing that is not a
    /// projectile, so it answers the base class's contacts. (The gibs' templates make some of them
    /// projectiles, which answer a thing they meet themselves.)
    /// </summary>
    static WorldThing Chunk(Location place, Vector3 at, long sector)
    {
        var chunk = place.Place("BroadSword", at, Vector3.Zero, sector);
        Assert.NotNull(chunk);
        Assert.NotEqual(ThingType.Projectile, chunk!.Type);
        chunk.MoveType = WorldThing.MovePhysics;
        chunk.PhysicsFlags = Physics.Gravity;               // falling
        chunk.Velocity = Vector3.Zero;
        chunk.RotVelocity = Vector3.Zero;
        return chunk;
    }

    /// <summary>
    /// **A chunk dropped on the monastery floor comes to rest on it, not in it.** The sweep meets
    /// the floor's own plane, one-sided, from the chunk's position along its move; a move that
    /// reaches it is refused and bounced, so the chunk never goes below the plane and
    /// settles within a step of it.
    /// </summary>
    [Fact]
    public void A_chunk_dropped_on_the_floor_comes_to_rest_on_it_and_not_in_it()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var (floor, sector) = Spot(place);
        var chunk = Chunk(place, floor + new Vector3(0f, 0f, 100f), sector);

        float lowest = float.MaxValue;
        for (int tick = 0; tick < 30 * 30 && (chunk.PhysicsFlags & Physics.Asleep) == 0; tick++)
        {
            Physics.Step(place, chunk, 1f / 30f);
            lowest = MathF.Min(lowest, chunk.Position.Z);
        }

        Assert.Equal(Physics.Asleep, chunk.PhysicsFlags & Physics.Asleep);
        Assert.True(lowest >= floor.Z - 0.01f, $"it went {floor.Z - lowest} below the floor");
        Assert.InRange(chunk.Position.Z, floor.Z - 0.01f, floor.Z + 8f);
    }

    /// <summary>
    /// **A chunk thrown at a barrel goes through it.** The sweep meets the barrel — a thing with a
    /// body in the chunk's way — and offers it to the chunk's `OnThingContact`, which for everything
    /// but a projectile is the base class's answer, "not finished"; so the chunk flies on through
    /// it and the floor is the first thing that stops it.
    /// </summary>
    [Fact]
    public void A_chunk_thrown_at_a_barrel_goes_through_it()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var (floor, sector) = Spot(place);
        var barrel = place.Place("barrel2", floor, Vector3.Zero, sector);
        Assert.NotNull(barrel);
        Assert.Equal(CollideShape.Cylinder, barrel!.Collide);
        var chunk = Chunk(place, floor + new Vector3(50f, 0f, 30f), sector);
        chunk.Velocity = new Vector3(-300f, 0f, 0f);

        // The sweep along its first moves does meet the barrel.
        var met = WorldSweep.Line(place.Level, place.Things, (int)chunk.Sector, chunk.Position, -Vector3.UnitX, 60f,
                             new WorldSweep.Asks(Pick.Tests.Both, Ignore: chunk));
        Assert.Contains(met, c => ReferenceEquals(c.Thing, barrel));

        for (int tick = 0; tick < 12; tick++) Physics.Step(place, chunk, 1f / 30f);
        Assert.True(chunk.Position.X < floor.X - barrel.Radius, $"it stopped at {chunk.Position}, the barrel is at {floor}");
    }

    /// <summary>
    /// **A gib chunk that flies into a standing character is spent on him** — unlike the sword above,
    /// a `gib_*` is a projectile (`TYPE 4`, `PROJECT_DAMAGE 1`), so a body it meets gets
    /// `OnThingContact` (→ `Hurt`) and the chunk is destroyed. As in 1.1,
    /// where a corpse's chunks vanish early, each at the
    /// moment it reaches Christof's or Wilhem's body, with no health lost to the soak. One flying past
    /// him is not stopped.
    /// </summary>
    [Fact]
    public void A_gib_chunk_that_flies_into_a_standing_character_is_spent_on_him()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var at = christof.Position + new Vector3(60f, 0f, 60f);
        var hit = place.Place("gib_2_2", at, Vector3.Zero, christof.Sector);
        Assert.NotNull(hit);
        Assert.Equal(ThingType.Projectile, hit!.Type);
        hit.Velocity = new Vector3(-300f, 0f, 0f);
        for (int tick = 0; tick < 12 && !hit.Removed; tick++) Physics.Step(place, hit, 1f / 30f);
        Assert.True(hit.Removed, $"the chunk flew on to {hit.Position}; Christof stands at {christof.Position}");

        var past = place.Place("gib_2_2", christof.Position + new Vector3(60f, 80f, 60f), Vector3.Zero, christof.Sector)!;
        past.Velocity = new Vector3(-300f, 0f, 0f);
        for (int tick = 0; tick < 8 && !past.Removed; tick++) Physics.Step(place, past, 1f / 30f);
        Assert.False(past.Removed);
    }

    /// <summary>
    /// **A line straight down never meets a cylinder.** The cylinder test measures across the ground at
    /// <c>|d| / (d̂ · dir)</c>, and a vertical line has no direction across the ground: the divide
    /// comes out infinite (or 0/0 on the axis itself), and both fail the test that follows. So in 1.1
    /// a chunk dropped straight on to a barrel's lid, or on to a character's head, is not even
    /// offered to it — the ground ray and the falling thing both look straight through cylinders.
    /// </summary>
    [Fact]
    public void A_line_straight_down_never_meets_a_cylinder()
    {
        var feet = new Vector3(0f, 0f, 0f);
        Assert.False(WorldSweep.MeetsCylinder(new Vector3(0f, 0f, 200f), -Vector3.UnitZ, 400f, feet, 22f, 60f, out _));
        Assert.False(WorldSweep.MeetsCylinder(new Vector3(10f, 0f, 200f), -Vector3.UnitZ, 400f, feet, 22f, 60f, out _));
        // Tilted the least bit towards the axis, it is met.
        var tilted = Vector3.Normalize(new Vector3(-0.2f, 0f, -1f));
        Assert.True(WorldSweep.MeetsCylinder(new Vector3(10f, 0f, 200f), tilted, 400f, feet, 22f, 60f, out _));
    }

    /// <summary>
    /// **A physics thing that has left every sector flies on**, still filed under the last one.
    /// The sector finder never answers "nowhere" (it hands back the hint), so the guard in
    /// the physics step never fires. Vitomir refused the move until then.
    /// </summary>
    [Fact]
    public void A_physics_thing_outside_every_sector_still_moves()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var (floor, sector) = Spot(place);
        var chunk = Chunk(place, floor + new Vector3(0f, 0f, 50f), sector);
        var away = floor + new Vector3(0f, 0f, 100_000f);
        chunk.Position = away;                              // somewhere no sector holds
        Assert.Equal(-1, place.Solid!.SectorAt(away));
        chunk.PhysicsFlags = 0;                             // no gravity: a straight flight
        chunk.Velocity = new Vector3(300f, 0f, 0f);

        Physics.Step(place, chunk, 0.1f);

        Assert.Equal(away.X + 30f, chunk.Position.X, 2);
        Assert.Equal(sector, chunk.Sector);
    }

    // ---- the walker: no falling ------------------------------------------------------------------

    /// <summary>
    /// A point just beyond the edge of a piece of ground, with what the engine's ground ray finds
    /// under it: the first walk surface straight down from 50 over the point for 140.
    /// </summary>
    static IEnumerable<(WalkNode From, Vector3 Inside, Vector3 Outside, Contact? Under)> Edges(Location place)
    {
        var map = place.Walk!;
        foreach (var node in map.Nodes)
        {
            if (node.Outline.Length < 3) continue;
            var c = Vector2.Zero;
            foreach (var v in node.Outline) c += v;
            c /= node.Outline.Length;
            for (int i = 0; i < node.Outline.Length; i++)
            {
                var edgeMiddle = (node.Outline[i] + node.Outline[(i + 1) % node.Outline.Length]) * 0.5f;
                var outward = edgeMiddle - c;
                if (outward.LengthSquared() < 1f) continue;
                outward = Vector2.Normalize(outward);
                var inside = edgeMiddle - outward * 4f;
                var outside = edgeMiddle + outward * 12f;
                if (!node.Contains(inside) || node.Contains(outside)) continue;
                var feet = new Vector3(inside, node.Height(inside.X, inside.Y));
                var there = new Vector3(outside, node.Height(outside.X, outside.Y));
                int hint = place.Solid!.SectorAt(feet with { Z = feet.Z + 16f }, node.Sector);
                yield return (node, feet, there, WorldSweep.Ground(place.Level, hint < 0 ? node.Sector : hint, there));
            }
        }
    }

    /// <summary>
    /// **A walker steps down as much as 90 in a single step** (the step's ray, 50 above the feet
    /// and 140 long): off the edge of a piece of ground on to one that lies 30 to 90 below, the
    /// step lands on the lower one at once — no fall, no time in the air.
    /// </summary>
    [Fact]
    public void A_walker_steps_down_as_much_as_ninety_units_at_once()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        var map = place.Walk!;
        int found = 0;
        foreach (var (from, inside, outside, under) in Edges(place))
        {
            if (under is not { } ground || outside.Z - ground.Point.Z is < 30f or > 90f) continue;
            var step = map.Stride(from.Index, inside, outside);
            Assert.Equal(ground.Point.Z, step.Position.Z, 1);
            if (++found >= 5) break;
        }
        Assert.True(found > 0, "no step down of 30 to 90 in Old Town");
    }

    /// <summary>
    /// **A walker whose ground runs out goes on at the height it had.** Where the ray finds nothing
    /// — more than 90 down, or no walk surface at all — the surface it had is kept and
    /// the height comes off that surface's plane, over nothing. There is no falling in 1.1: no
    /// character is a physics thing and the walker has no fall of its own.
    /// </summary>
    [Fact]
    public void A_walker_whose_ground_runs_out_goes_on_at_the_height_it_had()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        var map = place.Walk!;
        int found = 0;
        foreach (var (from, inside, outside, under) in Edges(place))
        {
            if (under is not null) continue;
            var step = map.Stride(from.Index, inside, outside);
            Assert.Equal(from.Index, step.Node);
            Assert.Equal(from.Height(outside.X, outside.Y), step.Position.Z, 2);
            Assert.False(step.Blocked);
            if (++found >= 5) break;
        }
        Assert.True(found > 0, "no edge in Old Town with nothing under it");
    }
}
