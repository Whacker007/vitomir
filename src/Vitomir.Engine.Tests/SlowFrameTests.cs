// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A frame is taken as a second at most**: 1.1's clock clamps each
/// frame's milliseconds to 1..1000, where Vitomir had capped them at 100. These pin the bound, and
/// then run the slow-frame check that came with the change — the first monastery's fight, its skeleton
/// archer, a walk and the particles, stepped a whole second at a time — for what a long step could
/// break: a position that is not a number, somebody through a floor, an arrow through the one it was
/// shot at, a particle that is not a number. None of these is ours: the walkers, the arrows and the
/// particles are 1.1's systems, taking 1.1's longest step.
/// </summary>
public class SlowFrameTests
{
    [Fact]
    public void A_frame_is_taken_as_a_second_at_most_and_a_millisecond_at_least()
    {
        Assert.Equal(1000, FrameStep.LongestMs);
        Assert.Equal(1, FrameStep.ShortestMs);
        Assert.Equal(0.25f, FrameStep.Seconds(250_000_000UL), 5);
        Assert.Equal(1f, FrameStep.Seconds(1_000_000_000UL), 5);
        Assert.Equal(1f, FrameStep.Seconds(4_500_000_000UL), 5);        // a stall: one second of play
        Assert.Equal(0.5f, FrameStep.Seconds(500_000_000UL), 5);         // Vitomir's old cap made this 0.1
        Assert.Equal(0.001f, FrameStep.Seconds(0UL), 6);
        Assert.Equal(0.001f, FrameStep.Seconds(200_000UL), 6);
    }

    /// <summary>
    /// The console's <c>timescale</c> scales the milliseconds **before** the bound (it multiplies,
    /// then clamps): three times a half-second frame is still one second, and a rate of 0 steps the
    /// game a millisecond a frame rather than stopping it.
    /// </summary>
    [Fact]
    public void The_timescale_scales_a_frame_before_it_is_bounded()
    {
        Assert.Equal(1f, FrameStep.Scaled(0.5f, 3f), 5);
        Assert.Equal(0.1f, FrameStep.Scaled(0.05f, 2f), 5);
        Assert.Equal(0.001f, FrameStep.Scaled(0.05f, 0f), 6);
        Assert.Equal(0.025f, FrameStep.Scaled(0.05f, 0.5f), 6);
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// The first monastery's scene with everybody in it awake — the Cappadocians, the corpses, the rats,
    /// the half-corpses, the skeletons — and two of them set on Christof, stepped for thirty seconds a second
    /// at a time (and a quarter of a second, and a thirtieth for the comparison): every character's
    /// position a number, nobody through a floor, and every particle drawn a number once the first
    /// births are gone.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(0.25f)]
    [InlineData(1f / 30f)]
    public void The_monastery_fight_takes_long_steps_without_a_bad_number_or_a_fall(float stepSeconds)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = place.Player!.Thing;
        christof.Actor!.Health = christof.Actor.MaxHealth = 100_000f;
        foreach (var m in minds.All.Where(m => m.Thing.Template.StartsWith("skel", StringComparison.OrdinalIgnoreCase)).Take(2))
            minds.Aggravate(m, christof);
        var start = place.Things.Where(t => t.Actor is not null).ToDictionary(t => t, t => t.Position);
        var sim = new Simulation(place);

        for (int second = 0; second < (int)(30f / stepSeconds); second++)
        {
            sim.Step(stepSeconds);
            foreach (var (thing, from) in start)
            {
                if (thing.Place is null) continue;                                 // gone from the world
                Assert.True(Finite(thing.Position), $"{thing.Template} {thing.Guid} at {thing.Position} after {second + 1} s");
                Assert.True(thing.Position.Z > from.Z - 300f, $"{thing.Template} {thing.Guid} fell from {from} to {thing.Position}");
            }
            // An emitter's first births divide by a frame of no length and are NaN, never seen — 1.1's,
            // kept (Emitter.BackDated) — and gone within their lifespan; after that, none.
            if (sim.Milliseconds < 5_000) continue;
            foreach (var batch in place.Particles.Batches)
                if (batch.Run == ParticleRun.Sprites)
                    for (int i = batch.First; i < batch.First + batch.Count; i++)
                    {
                        var p = place.Particles.Sprites[i];
                        Assert.True(Finite(p.Position) && float.IsFinite(p.Size),
                                    $"a particle of {batch.Emitter.GetType().Name} ({batch.Emitter.Settings.Name}) at {p.Position}, size {p.Size}, after {second + 1} s");
                    }
        }
        Assert.True(sim.Milliseconds >= 29_000);
    }

    /// <summary>
    /// **The skeleton archer's arrows at a second a step.** An arrow flies a thousand units a second, so
    /// one step carries it the whole of its flight to Christof and beyond; it must still meet him — the
    /// flight is swept along the whole step, not tested at its end — and every
    /// arrow is gone again after.
    /// </summary>
    [Fact]
    public void The_archers_arrows_still_meet_christof_when_a_step_carries_them_past_him()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files));
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        var christof = place.Player!.Thing;
        Assert.True(PlaceInSight(place, archer, christof), "no spot in the archer's sight");
        christof.Actor!.Health = christof.Actor.MaxHealth = 100_000f;
        minds.Aggravate(minds.All.First(m => ReferenceEquals(m.Thing, archer)), christof);

        var shot = Projectiles.FireAt(place, archer, christof, "arrow", Vector3.Zero, new Rng(4));
        Assert.NotNull(shot);
        WorldThing? struck = null;
        var shots = new Physics.Shots(new Rng(4), place.Milliseconds, place.Motions, s => struck = s.Victim);
        for (int step = 0; step < 6 && !shot!.Removed; step++) Physics.StepAll(place, 1f, shots);
        Assert.True(shot!.Removed, "the arrow is still flying");
        Assert.Same(christof, struck);
    }

    /// <summary>
    /// **A walk at a second a step** ends where the same walk at thirty steps a second ends: the walker
    /// takes no more of a leg than is left of it, so a long step finishes a leg and the next
    /// step starts the next one, and nobody is carried past a corner.
    /// </summary>
    [Fact]
    public void A_walk_at_one_second_steps_ends_where_it_ends_at_thirty_steps_a_second()
    {
        using var files = Install.Open();
        Vector3 End(float step, out int legs)
        {
            var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
            var christof = place.Player!;
            var from = christof.Thing.Position;
            var goal = place.Walk!.Nodes.Where(n => n.Outline.Length > 0 && n.Contains((n.Min + n.Max) * 0.5f))
                .Select(n => { var c = (n.Min + n.Max) * 0.5f; return new Vector3(c.X, c.Y, n.Height(c.X, c.Y)); })
                .Where(p => Vector3.Distance(p, from) is > 500f and < 900f)
                .OrderBy(p => p.X).ThenBy(p => p.Y)
                .First(p => christof.WalkTo(p, place.Walk.Find(p), run: false) && christof.Route.Length >= 3);
            legs = christof.Route.Length;
            var sim = new Simulation(place);
            for (float t = 0; t < 30f && christof.Walking; t += step)
            {
                sim.Step(step);
                Assert.True(Finite(christof.Thing.Position), $"Christof at {christof.Thing.Position}");
            }
            Assert.False(christof.Walking, $"still walking to {goal}, at {christof.Thing.Position}");
            return christof.Thing.Position;
        }
        var often = End(1f / 30f, out int legsOften);
        var seldom = End(1f, out int legsSeldom);
        Assert.Equal(legsOften, legsSeldom);
        Assert.True(Vector3.Distance(often, seldom) < 5f, $"the walk ended at {seldom}, not {often}");
    }

    static bool PlaceInSight(Location place, WorldThing archer, WorldThing christof)
    {
        foreach (var node in place.Walk!.Nodes)
        {
            if (node.Outline.Length == 0) continue;
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var at = new Vector3(c.X, c.Y, node.Height(c.X, c.Y));
            float d = Vector3.Distance(at, archer.Position);
            if (d < 300f || d > 450f || MathF.Abs(at.Z - archer.Position.Z) > 40f) continue;
            christof.Position = at;
            christof.Sector = node.Sector;
            if (Sight.Sees(place.Level, archer, christof)) return true;
        }
        return false;
    }
}
