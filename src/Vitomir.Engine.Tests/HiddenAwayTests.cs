// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A character a script has put away is found by nobody** (Vukodlak in the
/// Cathedral of Flesh's first level). 1.1's searches and shouts gather through a collision query,
/// which answers only things with a body; `FLS1_Main` keeps Vukodlak unrendered, with no
/// collide shape and held until his scene. Vitomir offered him to the party's own search, and the
/// Fix `wakeonhit` then let their blows wake him: an enemy nobody could see.
/// </summary>
public class HiddenAwayTests
{
    static (Location Place, Simulation Sim, WorldThing Wilhem, WorldThing Hidden) Hall(Vfs.GameFiles files, bool away)
    {
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        place.Fixes = new Fixes { WakeOnHit = true };
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = place.Place("Wilhem", christof.Position + new Vector3(0f, 70f, 0f), default, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        wilhem.Actor.Flags2 |= Actor.Flag2.InFormation;
        wilhem.Actor.Stance = PartyStance.Attack;                       // he looks for a fight himself
        place.Minds!.Adopt(wilhem);
        var hidden = place.Place("cappadocian", christof.Position + new Vector3(200f, 0f, 0f), default, christof.Sector)!;
        hidden.Actor!.Flags |= Actor.Flag.AiPaused;
        if (away)
        {
            hidden.RenderFlags |= FallenListener.DontRender;            // FLS1_Main.beginscene's three lines
            hidden.Collide = CollideShape.None;
        }
        place.Minds.Adopt(hidden);
        return (place, new Simulation(place), wilhem, hidden);
    }

    [Fact]
    public void An_offensive_companion_is_not_offered_one_with_no_body()
    {
        using var files = Install.Open();
        var (_, sim, wilhem, hidden) = Hall(files, away: true);
        for (int i = 0; i < 30 * 5; i++)
        {
            sim.Step(1f / 30f);
            Assert.False(ReferenceEquals(wilhem.Mind!.Target, hidden), $"Wilhem took the hidden one {i / 30f:0.0} s in");
        }
        Assert.Equal(hidden.Actor!.MaxHealth, hidden.Actor.Health);
    }

    [Fact]
    public void The_same_character_with_a_body_is_taken()
    {
        using var files = Install.Open();
        var (_, sim, wilhem, hidden) = Hall(files, away: false);
        bool took = false;
        for (int i = 0; i < 30 * 5 && !took; i++) { sim.Step(1f / 30f); took = ReferenceEquals(wilhem.Mind!.Target, hidden); }
        Assert.True(took, "Wilhem never took the enemy standing in front of him");
    }

    [Fact]
    public void The_fix_does_not_wake_one_that_is_put_away()
    {
        using var files = Install.Open();
        var (place, _, wilhem, hidden) = Hall(files, away: true);
        Damage.Apply(hidden, wilhem, HitKind.Effect, DamageType.Lethal, 5f, new Rng(1), place.Minds!.Now);
        Assert.True(hidden.Actor!.Has(Actor.Flag.AiPaused));
    }
}
