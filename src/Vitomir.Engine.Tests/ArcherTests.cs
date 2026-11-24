// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A bowman stands and shoots. In play the skeleton archer did an odd slide
/// while shooting as it came on, as if it wanted to get within melee distance.
/// Archers do not do that: they get into range where they can use their bows and attack
/// while standing still. The enemy mode repositioned whenever its attack order was refused —
/// and a bow's order is refused for the whole of every reload.
/// </summary>
public class ArcherTests
{
    static (Location Place, Minds Minds, WorldThing Archer, WorldThing Christof, Vitomir.Engine.Vfs.GameFiles Files) Range(float near, float far)
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files));
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        var christof = place.Player!.Thing;
        // Somewhere the archer can see, at a bowman's distance: the first piece of ground that
        // qualifies, so the test does not depend on where the level puts the player.
        foreach (var node in place.Walk!.Nodes)
        {
            if (node.Outline.Length == 0) continue;
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var at = new Vector3(c.X, c.Y, node.Height(c.X, c.Y));
            float d = Vector3.Distance(at, archer.Position);
            if (d < near || d > far || MathF.Abs(at.Z - archer.Position.Z) > 40f) continue;
            christof.Position = at;
            christof.Sector = node.Sector;
            if (Sight.Sees(place.Level, archer, christof)) return (place, minds, archer, christof, files);
        }
        throw new InvalidOperationException("no spot in the archer's sight at that range");
    }

    [Fact]
    public void An_archer_in_range_stands_still_and_keeps_shooting()
    {
        var (place, minds, archer, christof, files) = Range(300f, 450f);
        using var _f = files;
        var sim = new Simulation(place);
        var mind = minds.All.First(m => ReferenceEquals(m.Thing, archer));
        minds.Aggravate(mind, christof);
        var start = archer.Position;
        float drift = 0f;
        for (int frame = 0; frame < 30 * 10; frame++)
        {
            sim.Step(1f / 30f);
            drift = MathF.Max(drift, Bodies.Distance2D(start, archer.Position));
        }
        int shots = sim.Shots.Count(b => ReferenceEquals(b.Attacker, archer));
        Assert.True(shots >= 3, $"only {shots} shots in ten seconds");
        Assert.True(drift < 5f, $"the archer walked {drift:0} units while it was in range");
        Assert.True(Bodies.Gap(archer, christof) > 200f, "the archer closed in");
    }

    [Fact]
    public void An_archer_out_of_sight_walks_until_it_can_see_and_then_stops()
    {
        // The enemy mode's other half: a target that cannot be made out is walked at in
        // `AiMode.Approach`, which hands back to the enemy mode once it can.
        var (place, minds, archer, christof, files) = Range(300f, 450f);
        using var _f = files;
        var mind = minds.All.First(m => ReferenceEquals(m.Thing, archer));
        minds.Aggravate(mind, christof);
        christof.Actor!.Stealth = 10;     // cannot be made out, whatever the line of sight
        var sim = new Simulation(place);
        for (int frame = 0; frame < 30 * 3; frame++) sim.Step(1f / 30f);
        Assert.Equal(AiMode.Approach, mind.Mode);
        christof.Actor!.Stealth = 0;
        for (int frame = 0; frame < 30 * 2; frame++) sim.Step(1f / 30f);
        Assert.Equal(AiMode.Enemy, mind.Mode);
    }
}
