// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The walker's wait scuffs (found by a comparison with 1.1): a walker
/// held up by somebody in its way says `stopmove` and the floor's scuff as the wait begins, and
/// stays walking — so no `startmove` when it goes on. Vitomir's footfalls listen for the edges of
/// <see cref="Player.Walking"/>, which a wait never makes, so the wait is counted
/// (<see cref="Player.Holds"/>) and <see cref="HeldUp"/> hears the count change. The scene is
/// <see cref="HiddenTargetTests"/>' monastery stair: a Cappadocian sent on a leg straight through
/// Christof.
/// </summary>
public class HeldUpTests
{
    [Fact]
    public void A_walker_held_up_by_somebody_is_heard_once_and_is_still_walking()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        var cappa = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)).Thing;
        foreach (var m in minds.All)
            if (m.Thing != cappa && m.Thing != christof) m.Thing.Flags |= WorldThing.Flag.Remove;
        place.Move(christof, new Vector3(-826f, -590f, -252f));
        place.Player.Ground();
        place.Move(cappa, new Vector3(-715f, -533f, -252f));
        var walker = cappa.Mind!.Walker;
        walker.Ground();
        var sim = new Simulation(place);
        sim.Step(1f / 60f);
        cappa.Mind.NextThink = int.MaxValue;                  // only the walker

        Assert.True(walker.WalkTo(new Vector3(-838f, -622f, -252f), walker.Node));
        int before = walker.Holds;
        Assert.False(HeldUp.Heard(place, cappa));
        for (int i = 0; i < 180 && !(walker.Waiting && ReferenceEquals(walker.BlockedBy, christof)); i++)
            sim.Step(1f / 60f);

        Assert.True(walker.Waiting, "the Cappadocian never stood waiting for Christof");
        Assert.True(walker.Holds > before);
        Assert.True(walker.Walking, "a wait is inside the walk: the walk bit stays set");
        Assert.True(HeldUp.Heard(place, cappa));
        Assert.False(HeldUp.Heard(place, cappa));             // once a wait
    }
}
