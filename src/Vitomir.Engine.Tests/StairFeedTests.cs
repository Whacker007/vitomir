// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A feed on a Cappadocian on the monastery's stair, as in 1.1**.
/// On the monastery's
/// stair Christof, at (−296.4, −587.2, −367.2) facing −127.3, bites a Cappadocian with 47 blood left:
/// she is put 60 in front of him, facing his way, **on the step below** at −382.5 (the bite's
/// `SetPosition`); the first +3 comes a second after the bite (the bite sets
/// the first drain 1,000 ms away), then one a second, sixteen in all; the sixteenth finds
/// her at 2 and drains her to death — and still pays Christof his 3, 78 → 81 — 16.14 s after the bite.
/// </summary>
public class StairFeedTests
{
    [Fact]
    public void A_bite_on_the_stair_stands_the_victim_on_the_step_and_drains_her_as_1_1_did()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 120f;
        me.Stat[(int)ActorStat.Blood] = me.BaseStat[(int)ActorStat.Blood] = 33f;
        me.Stat[(int)ActorStat.BloodRating] = 1f;
        var victim = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)).Thing;
        foreach (var m in minds.All)
            if (!ReferenceEquals(m.Thing, victim) && !ReferenceEquals(m.Thing, christof)) m.Thing.Flags |= WorldThing.Flag.Remove;
        var them = victim.Actor!;
        them.Stat[(int)ActorStat.Blood] = them.BaseStat[(int)ActorStat.Blood] = 47f;

        place.Move(christof, new Vector3(-296.4f, -587.2f, -367.2f));
        place.Player.Ground();
        place.Move(victim, new Vector3(-220f, -645.4f, -391.7f));
        victim.Mind!.Walker.Ground();
        var sim = new Simulation(place);
        sim.Step(1f / 60f);                                   // the removals go
        int bitAt = sim.Milliseconds;
        Feeding.Bite(christof, victim, forced: true);
        Assert.Equal(ActionMode.Feeding, me.Action);

        // 60 in front of him, facing his way, on the step below.
        Assert.True(Bodies.Distance2D(victim.Position, new Vector3(-248.7f, -623.6f, 0f)) < 1.5f, $"she was put at {victim.Position}");
        Assert.InRange(victim.Position.Z, -384f, -381f);
        Assert.InRange(MathF.Abs(Player.Wrap(victim.Orientation.Z - christof.Orientation.Z)), 0f, 0.1f);

        var ticks = new List<int>();
        float was = them.Stat[(int)ActorStat.Blood];
        int drainedAt = -1;
        for (int frame = 0; frame < 60 * 20 && drainedAt < 0; frame++)
        {
            sim.Step(1f / 60f);
            float now = them.Stat[(int)ActorStat.Blood];
            if (now < was - 0.5f) ticks.Add(sim.Milliseconds - bitAt);
            was = now;
            if (them.Has(Actor.Flag.Dead)) drainedAt = sim.Milliseconds - bitAt;
        }
        Assert.InRange(ticks[0], 1000, 1050);
        for (int i = 1; i < ticks.Count; i++) Assert.InRange(ticks[i] - ticks[i - 1], 1000, 1034);
        Assert.Equal(15, ticks.Count);                         // 47 → 2, three at a time
        Assert.InRange(drainedAt, 15_950, 16_600);             // 1.1: 16,139 ms
        Assert.Equal(81f, me.Stat[(int)ActorStat.Blood], 0.01f);
        Assert.Equal(ActionMode.None, me.Action);
    }
}
