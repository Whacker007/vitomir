// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **When a companion joins a fight, by stance** — in play on the `SP3` save, at the
/// standard stance, Wilhem did not attack until he was attacked directly. That is
/// 1.1's. The party think never searches except on **Offensive** (stance 0, the hunt
/// the companion's search); on **Neutral** and **Defensive** a companion takes a target only when he is handed one:
/// somebody goes for a party member — every swing and every feed's resist roll, whose party
/// half is the rally — the player orders an attack (the attack order's tail), or a blow worth switching
/// for lands on him. An enemy walking up to him, standing over him or taking him
/// as its target tells him nothing (the set-target tells only the setter's own pets).
/// So the Cappadocian who took Wilhem and came for him was let come, and answered at her first swing.
/// </summary>
public class CompanionStanceTests
{
    /// <summary>
    /// The first monastery's hall as it is in that save: Christof hidden (stealth 3) where he
    /// stood, Wilhem beside him at the stance given, and one Cappadocian 200 units off who has taken
    /// Wilhem as her target — everybody else out of the world.
    /// </summary>
    static (Minds Minds, Simulation Sim, Mind Wilhem, WorldThing Cappadocian) Hall(PartyStance stance)
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var wilhemThing = place.Place("wilhem", new Vector3(-916f, -610f, -252f), Vector3.Zero, place.Player!.Thing.Sector)!;
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player.Thing;
        var cappadocian = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)).Thing;
        var wilhem = wilhemThing.Mind!;
        foreach (var m in minds.All)
            if (m.Thing != cappadocian && m.Thing != christof && m.Thing != wilhemThing) m.Thing.Flags |= WorldThing.Flag.Remove;
        foreach (var member in new[] { christof, wilhemThing })
        {
            member.Actor!.Flags |= Actor.Flag.Party;
            member.Actor.Flags2 |= Actor.Flag2.InFormation;
            member.Mind?.Enter(AiMode.Party);
        }
        wilhemThing.Actor!.Stance = stance;
        place.Move(christof, new Vector3(-826f, -590f, -252f));
        place.Player.Ground();
        wilhem.Walker.Ground();
        christof.Actor!.Stealth = 3;
        place.Move(cappadocian, new Vector3(-715f, -533f, -252f));
        cappadocian.Mind!.Walker.Ground();
        var sim = new Simulation(place);
        sim.Step(1f / 60f);                                   // the removals go
        var her = cappadocian.Mind!;
        her.Aim(wilhemThing, minds.Now);
        her.Enter(AiMode.Enemy, andBase: false);
        her.NextThink = minds.Now;
        wilhem.Aim(null, minds.Now);
        return (minds, sim, wilhem, cappadocian);
    }

    /// <summary>
    /// **Neutral** (and Defensive, the same here, since he is the one attacked): Wilhem lets her walk
    /// up and stand at him, and takes her only once she goes for him.
    /// </summary>
    [Theory]
    [InlineData(PartyStance.Neutral)]
    [InlineData(PartyStance.Defense)]
    public void A_neutral_or_defensive_companion_lets_an_enemy_come_and_answers_her_first_swing(PartyStance stance)
    {
        var (_, sim, wilhem, cappadocian) = Hall(stance);
        var her = cappadocian.Actor!;
        int frame = 0, wentFor = -1;
        for (; frame < 60 * 25 && wentFor < 0; frame++)
        {
            sim.Step(1f / 60f);
            if (her.Action is ActionMode.Swing or ActionMode.ApproachFeed or ActionMode.Feeding) wentFor = frame;
            else Assert.True(wilhem.Target is null, $"{stance}: Wilhem took {wilhem.Target?.Template} {frame / 60f:0.0} s in, before she went for him");
        }
        Assert.True(wentFor >= 0, "she never went for him");
        for (int i = 0; i < 60 * 3 && wilhem.Target is null; i++) sim.Step(1f / 60f);
        Assert.Same(cappadocian, wilhem.Target);
    }

    /// <summary>
    /// **Offensive**: his own hunt (every 300 ms, within his AI file's `searchDist`) finds
    /// her before she reaches him.
    /// </summary>
    [Fact]
    public void An_offensive_companion_goes_for_an_enemy_before_she_goes_for_him()
    {
        var (_, sim, wilhem, cappadocian) = Hall(PartyStance.Attack);
        var her = cappadocian.Actor!;
        for (int frame = 0; frame < 60 * 5 && wilhem.Target is null; frame++)
        {
            sim.Step(1f / 60f);
            Assert.False(her.Action is ActionMode.Swing or ActionMode.ApproachFeed or ActionMode.Feeding,
                         "she went for him first");
        }
        Assert.Same(cappadocian, wilhem.Target);
    }
}
