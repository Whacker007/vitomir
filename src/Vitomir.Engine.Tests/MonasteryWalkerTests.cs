// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Walkers in the first monastery, as in 1.1: Christof, a vampire now, leading
/// Wilhem up and down the stairs by the first cloister, and a barrel that gives up a rat. Walking
/// Christof's track through Vitomir with Wilhem following put Wilhem within fifty units of where
/// 1.1 has him the whole way — except on one staircase, where he stood at the foot for twelve
/// seconds; and the rat's two dashes came out a different shape. Both were the same thing: a walker
/// on a route in 1.1 **never asks about the ground**, and one made at run time
/// **stands on no ground at all** until its first step finds some.
/// </summary>
public class MonasteryWalkerTests
{
    static (Location Place, Minds Minds, Simulation Sim, Mind Wilhem) WithWilhem(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        var joined = place.Place("Wilhem", lead.Position + new Vector3(96f, 0f, 0f), lead.Orientation, lead.Sector)!;
        joined.Actor!.Flags |= Actor.Flag.Party;
        joined.Actor.Flags2 |= Actor.Flag2.InFormation;
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var wilhem = minds.All.Single(m => ReferenceEquals(m.Thing, joined));
        wilhem.Enter(AiMode.Party);
        lead.Actor!.Flags |= Actor.Flag.Party;
        // Only the two of them: the scene's Cappadocians and skeletons would make it a fight.
        foreach (var m in minds.All.ToArray())
            if (!ReferenceEquals(m.Thing, lead) && !ReferenceEquals(m, wilhem)) place.Remove(m.Thing);
        return (place, minds, new Simulation(place), wilhem);
    }

    /// <summary>
    /// The staircase by the first cloister, climbed from the floor beside it. The route runs along
    /// the treads' side edge — x = −1272, where the floor's surface ends and the treads' begin. In
    /// 1.1 Wilhem walks it **at the floor's height**: at
    /// (−1272.0, −506 … −433, −252), over ground that is not there, and then on the tread at −204
    /// as the next leg begins (other walks of the same line are on the treads — see the next test). Vitomir called the edge a wall and he stood at the foot
    /// of the stairs until Christof came back down.
    /// </summary>
    [Fact]
    public void Wilhem_climbs_the_stairs_from_the_floor_beside_them_along_the_treads_edge()
    {
        using var files = Install.Open();
        var (place, _, sim, wilhem) = WithWilhem(files);
        var christof = place.Player!;
        christof.Thing.Position = new Vector3(-1350f, -110f, -124f);     // the landing at the top
        Assert.True(christof.Ground());
        wilhem.Thing.Position = new Vector3(-1164f, -520f, -252f);       // where 1.1 had him
        Assert.True(wilhem.Walker.Ground());

        bool alongTheEdge = false;
        for (int i = 0; i < 30 * 10; i++)
        {
            sim.Step(1f / 30f);
            var p = wilhem.Thing.Position;
            if (MathF.Abs(p.X + 1272f) < 1f && p.Y > -480f && p.Y < -430f && MathF.Abs(p.Z + 252f) < 1f)
                alongTheEdge = true;
        }

        Assert.True(alongTheEdge, "the route did not take the treads' edge — this test no longer tests it");
        Assert.True(wilhem.Thing.Position.Z > -190f, $"he is still at the foot of the stairs, at {wilhem.Thing.Position}");
        Assert.True(Bodies.Gap(wilhem.Thing, christof.Thing) <= Minds.FollowReach,
                    $"he stopped {Bodies.Gap(wilhem.Thing, christof.Thing):0} short of Christof");
    }

    /// <summary>
    /// The step underneath that: the position's set keeps the surface it had while the point is over it **by
    /// the engine's own inside test** (an edge counting as inside), casts **the engine's
    /// ray** straight down from fifty above when it is not, and **keeps the old surface
    /// when the ray finds nothing**, taking the height off that surface's plane. The keyboard, which
    /// is ours, still stops where the ground runs out.
    ///
    /// Recalibrated on 1.1's walkers when the ray replaced the walk map's own search.
    /// The floor beside the stairs ends at y −488 and the treads' side edge is the line x = −1272.
    /// Past the floor's end and off that line there is no ground: 1.1 has Wilhem at the floor's
    /// height there (−1261.9, −470, −252). **On** the line, the
    /// engine's test puts the point over a tread, and 1.1 has walkers at x −1272.0 at the
    /// treads' heights — Wilhem at (−1272.0, −467.5, −220), Wilhem and Christof
    /// at −236 and −220 — and once at the floor's: a tenth of a unit
    /// cannot tell a leg a hair beside the line from one on it. An earlier build calibrated this test
    /// on that last walk and on the walk map's own inside test, which calls the line outside every
    /// surface; the engine's calls it inside the tread.
    /// </summary>
    [Fact]
    public void A_route_step_keeps_the_ground_it_had_where_there_is_none_underneath()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: false, sky: false);
        var map = place.Walk!;
        var from = new Vector3(-1272f, -491f, -252f);
        int floor = map.Find(from);
        Assert.True(floor >= 0);

        // Beside the treads, past the floor's end: the ray finds nothing, so the floor is kept, and
        // so is its height — where 1.1 had Wilhem.
        var beside = new Vector3(-1261.9f, -470f, -240f);
        Assert.Equal(-1, map.Ray(beside));
        var over = map.Stride(floor, from, beside);
        Assert.False(over.Blocked);
        Assert.Equal(floor, over.Node);
        Assert.Equal(-252f, over.Position.Z, 1);
        // And the keyboard's step, which keeps to the ground, will not go there at all.
        Assert.True(map.Move(floor, from, beside with { Z = -252f }).Blocked);

        // On the treads' side edge: over the tread by the engine's test, so the ray finds it.
        var edge = map.Stride(floor, from, new Vector3(-1272f, -470f, -240f));
        Assert.False(edge.Blocked);
        Assert.NotEqual(floor, edge.Node);
        Assert.Equal(-220f, edge.Position.Z, 1);
        Assert.True(map.Over(edge.Node, new Vector3(-1272f, -470f, -240f)));
        Assert.Equal(-1, map.Find(new Vector3(-1272f, -470f, -252f)));    // the walk map's own test: outside

        // Off the edge and over a tread: that tread is found and stood on.
        var tread = map.Stride(floor, from, new Vector3(-1290f, -415f, -230f));
        Assert.NotEqual(floor, tread.Node);
        Assert.Equal(-204f, tread.Position.Z, 1);

        // A thing in the way is the one thing that stops it.
        var stopped = map.Stride(floor, from, beside, _ => false);
        Assert.True(stopped.Blocked);
        Assert.Equal(from, stopped.Position);
    }

    static (Location Place, WorldThing Rat) BarrelRat(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var at = new Vector3(1488f, -310f, -270f);
        var barrel = place.Things.Where(t => t.Template.Equals("container_barrel2", StringComparison.OrdinalIgnoreCase))
                          .OrderBy(t => Vector3.Distance(t.Position, at)).First();
        Assert.True(Vector3.Distance(barrel.Position, at) < 1f, "the barrel Wilhem broke is not where it was");
        place.Player!.Thing.Position = new Vector3(1477f, -410f, -270f);
        Assert.True(Container.Open(barrel, place.Player!.Thing));
        var rat = place.Things.Single(t => t.Template.Equals(barrel.DropItem, StringComparison.OrdinalIgnoreCase));
        return (place, rat);
    }

    /// <summary>
    /// **The barrel rat's first dash is a straight line** — from the barrel to where its script
    /// sent it, across a gap in the floor. In 1.1 it ran from (1488, −310) to
    /// (1612, −144) on one heading in a second, at the barrel's height of −270 over no ground at
    /// all, and dropped to −286 as it reached the far floor. A thing made at run time stands on no
    /// surface (the world entry never looks), `SendActorToPos` calls the walker without the move order's
    /// look, and a walker with no surface walks straight at the point. Its first step
    /// then finds the barrel's floor, and keeps it across the gap.
    /// </summary>
    /// <summary>
    /// With the quality of life <c>spawnwalls</c> on (its default), the same barrel rat looks for a route by the walk map the
    /// moment its first step lands it on the floor, rather than dashing on along its straight leg.
    /// </summary>
    [Fact]
    public void With_spawn_walls_on_the_barrel_rat_looks_for_a_route_once_it_finds_the_floor()
    {
        using var files = Install.Open();
        var (place, rat) = BarrelRat(files);
        place.Qol = new Qol { SpawnWalls = true };
        var walker = place.WalkerFor(rat)!;
        var there = new Vector3(1612.1f, -144.2f, -286f);
        Assert.True(walker.WalkTo(there, place.Walk!.Find(there), speed: 210f, clamp: false, persist: true));
        var sim = new Simulation(place);
        for (int f = 0; f < 90 && (walker.Walking || walker.Replanned == 0); f++) sim.Step(1f / 30f);
        Assert.False(rat.Unsurfaced);
        Assert.Equal(1, walker.Replanned);
    }

    [Fact]
    public void A_barrel_rat_dashes_straight_across_the_gap_the_first_time_its_script_sends_it()
    {
        using var files = Install.Open();
        var (place, rat) = BarrelRat(files);
        place.Qol = new Qol { SpawnWalls = false };                     // 1.1's dash (the quality of life spawnwalls is ours)
        Assert.True(rat.Unsurfaced);
        var walker = place.WalkerFor(rat)!;
        Assert.Equal(-1, walker.Node);

        var there = new Vector3(1612.1f, -144.2f, -286f);
        Assert.True(walker.WalkTo(there, place.Walk!.Find(there), speed: 210f, clamp: false, persist: true));
        Assert.Empty(walker.Route);                                     // no search at all

        var sim = new Simulation(place);
        bool overTheGap = false;
        float heading = walker.Heading;
        int frames = 0;
        for (; frames < 60 && walker.Walking; frames++)
        {
            sim.Step(1f / 30f);
            Assert.Equal(heading, walker.Heading, 1);                   // one heading, the whole way
            var p = rat.Position;
            if (place.Walk!.Find(p) < 0 && MathF.Abs(p.Z + 270f) < 0.5f) overTheGap = true;
        }
        Assert.True(overTheGap, "it never crossed the gap at the barrel's height");
        Assert.True(Vector3.Distance(rat.Position, there) < 1f, $"it stopped at {rat.Position}");
        Assert.InRange(frames, 27, 33);                                 // 1.1: a second, at 210
        Assert.False(rat.Unsurfaced);
    }

    /// <summary>
    /// A move order does look first, so the same rat sent by the AI or a
    /// click rather than by its script finds its floor and routes round — which is why only the
    /// script's walk dashes.
    /// </summary>
    [Fact]
    public void A_move_order_finds_the_ground_first_and_routes()
    {
        using var files = Install.Open();
        var (place, rat) = BarrelRat(files);
        var walker = place.WalkerFor(rat)!;
        Assert.Equal(-1, walker.Node);

        var there = new Vector3(1612.1f, -144.2f, -286f);
        Assert.True(walker.WalkTo(there, place.Walk!.Find(there)));
        Assert.True(walker.Node >= 0);
        Assert.NotEmpty(walker.Route);
    }
}
