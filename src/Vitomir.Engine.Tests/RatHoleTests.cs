// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The silver mines' rat holes (in play the ghoul rats got stuck in
/// their holes in mines1 and mines2 and could not path out). Each hole has a <c>ghost</c> prop
/// running <c>Generator("rat", …)</c> with no keyframes, so its rats are made by `SpawnThing`
/// exactly where the prop stands — inside the hole, off every piece of the walk map, with
/// no floor under them, and on no surface. In that play they saw Christof, were ordered
/// to him at 250 and played `ratrun`, without moving a unit from birth to death. 1.1's walker writes
/// a step whatever the ground, so they walk out, straight, until the ground ray
/// finds a floor; Vitomir threw every such step away (<see cref="SurfacelessStep"/>).
/// </summary>
public class RatHoleTests
{
    /// <summary>
    /// The level with its chapter scene, awake, and a rat made at the generator standing at
    /// <paramref name="hole"/> the way the script makes it (`SpawnThing` from the generator).
    /// </summary>
    static (Location Place, WorldThing Hole, WorldThing Rat) Made(Vfs.GameFiles files, string level, string scene, Vector3 hole)
    {
        var place = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var generator = place.Things.Where(t => t.Script == "Generator").OrderBy(t => Vector3.Distance(t.Position, hole)).First();
        Assert.True(Vector3.Distance(generator.Position, hole) < 1f, $"no generator at {hole}; the nearest is at {generator.Position}");
        var rat = place.Spawn("rat", generator)!;
        Assert.Equal(generator.Position, rat.Position);
        return (place, generator, rat);
    }

    /// <summary>
    /// Both of the first mine's holes stand where there is no walk map and no floor: the walk map
    /// finds no piece there, and neither does the ground ray. A rat made there has no ground to
    /// stand on — which is what the rest of this class depends on.
    /// </summary>
    [Theory]
    [InlineData(-640f, -120f, -272f)]
    [InlineData(1248f, -1104f, -288f)]
    public void A_rat_hole_in_the_first_mine_has_no_ground_under_it(float x, float y, float z)
    {
        using var files = Install.Open();
        var (place, hole, rat) = Made(files, "P1_SIM1", "SIM1_5_1", new Vector3(x, y, z));
        Assert.Equal(-1, place.Walk!.Find(hole.Position));
        Assert.Equal(-1, place.Walk.Ray(hole.Position));
        Assert.True(rat.Unsurfaced);
        Assert.NotNull(rat.Mind);
        Assert.Equal(-1, place.WalkerFor(rat)!.Node);
    }

    /// <summary>
    /// **A rat ordered out of its hole walks out.** A move order finds no ground under it (the
    /// move order), so the walker walks straight at the point; each step is written with
    /// no surface until one lands over the floor, where the ray finds it and the rat
    /// stands on it from then on. Before this the rat stood where it was made while its walk ran out.
    /// </summary>
    [Theory]
    [InlineData(-640f, -120f, -272f, -340f, -120f)]
    [InlineData(1248f, -1104f, -288f, 1460f, -892f)]
    public void A_rat_made_in_its_hole_walks_out_when_ordered(float x, float y, float z, float gx, float gy)
    {
        using var files = Install.Open();
        var (place, _, rat) = Made(files, "P1_SIM1", "SIM1_5_1", new Vector3(x, y, z));
        place.Qol = new Qol { SpawnWalls = false };                       // 1.1's straight leg, the whole way
        var walker = place.WalkerFor(rat)!;
        var goal = new Vector3(gx, gy, z);
        int node = place.Walk!.Find(goal);
        Assert.True(node >= 0, "the goal is not on the walk map — this test no longer tests it");
        Assert.True(walker.WalkTo(goal, node, speed: 250f, mind: true));
        Assert.Empty(walker.Route);                                        // no ground, no search

        var sim = new Simulation(place);
        for (int f = 0; f < 60 && walker.Walking; f++) sim.Step(1f / 30f);

        Assert.True(Vector2.Distance(new(rat.Position.X, rat.Position.Y), new(gx, gy)) < 1f, $"it stopped at {rat.Position}");
        Assert.True(walker.Node >= 0, "it never found the floor");
        Assert.False(rat.Unsurfaced);
    }

    /// <summary>
    /// The whole of it, AI and all: a rat made in the first mine's west hole with Christof standing
    /// in the tunnel outside sees him, goes for him and reaches him — out of the hole and onto the
    /// floor. In that play the same rat stood at (−640, −120, −272) from its birth to
    /// its death.
    /// </summary>
    [Fact]
    public void A_rat_made_in_its_hole_comes_out_for_Christof()
    {
        using var files = Install.Open();
        var (place, hole, rat) = Made(files, "P1_SIM1", "SIM1_5_1", new Vector3(-640f, -120f, -272f));
        var christof = place.Player!;
        christof.Thing.Position = new Vector3(-300f, -120f, -272f);
        Assert.True(christof.Ground());
        // Only the two of them: the mine's other rats would join in and crowd him.
        foreach (var m in place.Minds!.All.ToArray())
            if (!ReferenceEquals(m.Thing, rat) && !ReferenceEquals(m.Thing, christof.Thing)) place.Remove(m.Thing);

        var sim = new Simulation(place);
        for (int f = 0; f < 30 * 8 && Bodies.Gap(rat, christof.Thing) > 40f; f++) sim.Step(1f / 30f);

        Assert.True(Vector3.Distance(rat.Position, hole.Position) > 150f, $"the rat is still in its hole, at {rat.Position}");
        Assert.True(place.WalkerFor(rat)!.Node >= 0, "it never found the floor");
        Assert.True(Bodies.Gap(rat, christof.Thing) <= 40f, $"it stopped {Bodies.Gap(rat, christof.Thing):0} short of Christof");
    }

    /// <summary>
    /// The second mine's hole is the same case: its generator stands at (1816, 2424, 2560) with no
    /// ground under it, and in that play every rat it made stood at that point
    /// from birth to death. Sent towards the nearest floor, one walks out.
    /// </summary>
    [Fact]
    public void A_rat_made_in_the_second_mines_hole_walks_out_too()
    {
        using var files = Install.Open();
        var (place, hole, rat) = Made(files, "P1_SIM2", "SIM2_5_1", new Vector3(1816f, 2424f, 2560f));
        Assert.Equal(-1, place.Walk!.Ray(hole.Position));
        var walker = place.WalkerFor(rat)!;
        Assert.Equal(-1, walker.Node);
        // The nearest floor round the hole.
        Vector3? goal = null;
        for (float r = 100f; r <= 400f && goal is null; r += 50f)
            for (int a = 0; a < 16 && goal is null; a++)
            {
                float angle = a * MathF.PI / 8f;
                var at = hole.Position + new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, 0f);
                if (place.Walk.Find(at) >= 0) goal = at;
            }
        Assert.NotNull(goal);
        Assert.True(walker.WalkTo(goal.Value, place.Walk.Find(goal.Value), speed: 250f, mind: true));

        var sim = new Simulation(place);
        for (int f = 0; f < 60 && walker.Walking; f++) sim.Step(1f / 30f);

        Assert.True(Vector3.Distance(rat.Position, hole.Position) > 90f, $"the rat is still in its hole, at {rat.Position}");
        Assert.True(walker.Node >= 0, "it never found the floor");
    }

    /// <summary>
    /// The quality of life <c>spawnwalls</c> leaves the holes alone: with it on, a rat made in either of the first mine's holes still
    /// walks out to the tunnel when ordered, since the way out is an opening, not a wall.
    /// </summary>
    [Theory]
    [InlineData(-640f, -120f, -272f, -340f, -120f)]
    [InlineData(1248f, -1104f, -288f, 1460f, -892f)]
    public void With_spawn_walls_on_a_rat_made_in_its_hole_still_walks_out(float x, float y, float z, float gx, float gy)
    {
        using var files = Install.Open();
        var (place, _, rat) = Made(files, "P1_SIM1", "SIM1_5_1", new Vector3(x, y, z));
        place.Qol = new Qol { SpawnWalls = true };
        var walker = place.WalkerFor(rat)!;
        var goal = new Vector3(gx, gy, z);
        Assert.True(walker.WalkTo(goal, place.Walk!.Find(goal), speed: 250f, mind: true));
        var sim = new Simulation(place);
        for (int f = 0; f < 90 && walker.Walking; f++) sim.Step(1f / 30f);
        // Out and there: a routed walk ends within a walker's arrival of its point, not on it as the straight leg does.
        Assert.True(Vector2.Distance(new(rat.Position.X, rat.Position.Y), new(gx, gy)) < 16f, $"it stopped at {rat.Position}");
        Assert.False(rat.Unsurfaced);
        Assert.Equal(1, walker.Replanned);                  // it looked for a route once, when it found its feet
    }

    /// <summary>With the quality of life off a rat made in its hole walks its straight leg the whole way, as 1.1's does: no route looked for.</summary>
    [Fact]
    public void With_spawn_walls_off_a_rat_made_in_its_hole_walks_its_straight_leg_the_whole_way()
    {
        using var files = Install.Open();
        var (place, _, rat) = Made(files, "P1_SIM1", "SIM1_5_1", new Vector3(1248f, -1104f, -288f));
        place.Qol = new Qol { SpawnWalls = false };
        var walker = place.WalkerFor(rat)!;
        var goal = new Vector3(1460f, -892f, -288f);
        Assert.True(walker.WalkTo(goal, place.Walk!.Find(goal), speed: 250f, mind: true));
        var sim = new Simulation(place);
        for (int f = 0; f < 60 && walker.Walking; f++) sim.Step(1f / 30f);
        Assert.Equal(0, walker.Replanned);
    }
}
