// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Fix `wakeonhit`** (ours). A character a script holds
/// (`THING_AF_AIPAUSED`, as `ActorAIOff_TriggerOn` holds the Society of Leopold's soldiers) and a party
/// member hurts from a distance: in 1.1 it is handed its attacker and never thinks,
/// so it stands there; with the Fix the blow lets it go and it comes for him.
/// </summary>
public class WakeOnHitTests
{
    static (Simulation Sim, WorldThing Christof, WorldThing Held) Shot(Vfs.GameFiles files, bool fix, bool byParty = true)
    {
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        place.Fixes = new Fixes { WakeOnHit = fix };
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        if (byParty) christof.Actor!.Flags |= Actor.Flag.Party; else christof.Actor!.Flags &= ~Actor.Flag.Party;
        var held = place.Place("cappadocian", christof.Position + new Vector3(300f, 0f, 0f), default, christof.Sector)!;
        held.Actor!.Flags |= Actor.Flag.AiPaused;
        place.Minds!.Adopt(held);
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Damage.Apply(held, christof, HitKind.Projectile, DamageType.Lethal, 5f, new Rng(1), place.Minds.Now);
        return (sim, christof, held);
    }

    static float Moved(Simulation sim, WorldThing held)
    {
        var from = held.Position;
        for (int i = 0; i < 30 * 4; i++) sim.Step(1f / 30f);
        return Vector3.Distance(from, held.Position);
    }

    [Fact]
    public void As_1_1_has_it_a_held_character_that_is_shot_stands_where_it_is()
    {
        using var files = Install.Open();
        var (sim, christof, held) = Shot(files, fix: false);
        Assert.True(held.Actor!.Has(Actor.Flag.AiPaused));
        Assert.Same(christof, held.Mind!.Target);                 // the event handler hands it over all the same
        Assert.True(Moved(sim, held) < 1f, "it moved, held");
    }

    [Fact]
    public void With_the_fix_the_blow_lets_it_go_and_it_comes_for_its_attacker()
    {
        using var files = Install.Open();
        var (sim, christof, held) = Shot(files, fix: true);
        Assert.False(held.Actor!.Has(Actor.Flag.AiPaused));
        Assert.Same(christof, held.Mind!.Target);
        Assert.True(Moved(sim, held) > 50f || held.Actor.Action != ActionMode.None, "it still stands there");
    }

    [Fact]
    public void Only_the_party_wakes_it()
    {
        using var files = Install.Open();
        var (_, _, held) = Shot(files, fix: true, byParty: false);
        Assert.True(held.Actor!.Has(Actor.Flag.AiPaused));
    }
}
