// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The melee resolve's reach test at the blow (the reach test),
/// found by a comparison with 1.1: a swing whose target has moved out of the
/// attack's <c>WEAPON_MAXDISTANCE</c> by the time the keyframe comes round whiffs — no roll, no
/// harm, nobody told — and so does one whose target has gone. Earlier, Vitomir rolled
/// every blow wherever the target stood, so a character could not step back out of a swing, and
/// the Space key's strike hurt a target across the room.
/// </summary>
public class StrikeReachTests
{
    static WorldThing Body(Vector3 at, int guid = 99) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f },
    };

    [Fact]
    public void A_swing_whiffs_when_its_target_is_out_of_reach_or_gone()
    {
        var me = Body(Vector3.Zero);
        var sword = new Weapon { Name = "sword" };
        me.Actor!.Weapon = sword;
        me.Actor.Action = ActionMode.Swing;
        float reach = sword.Current.MaxDistance;

        // Nobody to hit at all.
        Assert.True(StrikeReach.Whiffs(me, me.Actor, sword));

        // Within reach between the bodies: the blow is rolled.
        var near = Body(new Vector3(me.Radius + 32f + reach - 1f, 0f, 0f), guid: 100);
        me.Actor.Target = near;
        Assert.False(StrikeReach.Whiffs(me, me.Actor, sword));

        // A step further than the attack reaches: the swing meets the air.
        near.Position = new Vector3(me.Radius + 32f + reach + 1f, 0f, 0f);
        Assert.True(StrikeReach.Whiffs(me, me.Actor, sword));

        // Only a swing asks (action 5); a throw's blow (action 18) is not measured.
        me.Actor.Action = ActionMode.Throwing;
        Assert.False(StrikeReach.Whiffs(me, me.Actor, sword));

        // Taken out of the world since the swing began: gone, whatever the distance.
        me.Actor.Action = ActionMode.Swing;
        near.Position = Vector3.Zero with { X = 40f };
        near.Removed = true;
        Assert.True(StrikeReach.Whiffs(me, me.Actor, sword));
    }

    [Fact]
    public void A_target_that_steps_back_before_the_blow_lands_is_not_hit()
    {
        using var files = Install.Open();
        var (sim, christof, cappa) = Standoff(files);
        float health = cappa.Actor!.Health;

        Assert.Equal(OrderResult.Started, Actions.Attack(christof, cappa, christof.Actor!.Weapon, 0,
                                                         sim.Milliseconds, strikeNow: true, motions: sim.Location.Motions));
        Assert.Equal(ActionMode.Swing, christof.Actor.Action);
        // Out of reach before the keyframe: well past any fist's WEAPON_MAXDISTANCE.
        cappa.Position = christof.Position with { X = christof.Position.X + 400f };
        for (int frame = 0; frame < 60; frame++) sim.Step(1f / 30f);

        Assert.DoesNotContain(sim.Blows, b => ReferenceEquals(b.Target, cappa));
        Assert.Equal(health, cappa.Actor.Health);
    }

    [Fact]
    public void A_target_that_stands_its_ground_is_rolled_for()
    {
        // The same swing with nobody moving: the keyframe comes round inside the two seconds and
        // the blow is resolved, hit or miss — which is what makes the test above mean something.
        using var files = Install.Open();
        var (sim, christof, cappa) = Standoff(files);

        Assert.Equal(OrderResult.Started, Actions.Attack(christof, cappa, christof.Actor!.Weapon, 0,
                                                         sim.Milliseconds, strikeNow: true, motions: sim.Location.Motions));
        for (int frame = 0; frame < 60; frame++) sim.Step(1f / 30f);

        Assert.Contains(sim.Blows, b => ReferenceEquals(b.Target, cappa));
    }

    /// <summary>Christof and a Cappadocian a pace apart in the second monastery, with no minds.</summary>
    static (Simulation Sim, WorldThing Christof, WorldThing Cappa) Standoff(GameFiles files)
    {
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2",
                                  player: "Christof");
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;
        var cappa = place.Things.First(t => t.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)
                                            && t.Actor is { Dead: false });
        cappa.Position = christof.Position with { X = christof.Position.X + 40f };
        cappa.Sector = christof.Sector;
        return (sim, christof, cappa);
    }
}
