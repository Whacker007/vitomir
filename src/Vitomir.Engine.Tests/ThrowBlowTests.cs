// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The resolve's branch on the action** (a comparison with 1.1's gaps 5 and 6). A
/// swing (5) is rolled; a throw (18, the special move) goes straight to its damage — no reach test, no
/// roll, so it always lands — and its critical test reads a margin nothing on that path wrote, which
/// Vitomir takes as never critical; any other action resolves nothing, noise included.
/// </summary>
public class ThrowBlowTests
{
    static WorldThing Body(Vector3 at, int guid) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f },
    };

    [Fact]
    public void A_swing_is_rolled_a_throw_is_not_and_nothing_else_resolves()
    {
        Assert.Equal(ThrowBlow.Resolve.Swing, ThrowBlow.For(ActionMode.Swing));
        Assert.Equal(ThrowBlow.Resolve.Throw, ThrowBlow.For(ActionMode.Throwing));
        foreach (var other in new[] { ActionMode.None, ActionMode.Attack, ActionMode.Move, ActionMode.Use, ActionMode.Feeding })
            Assert.Equal(ThrowBlow.Resolve.Nothing, ThrowBlow.For(other));
    }

    [Fact]
    public void A_throws_blow_always_lands_for_its_damage_roll_and_is_never_critical()
    {
        var thrower = Body(Vector3.Zero, 1);
        var victim = Body(new Vector3(0f, 900f, 0f), 2);           // far out of any reach: no reach test
        var attack = new Weapon { Name = "claws" }.Attacks[0];
        attack.Damage = 6;
        var rng = new Rng(3);
        for (int i = 0; i < 200; i++)
        {
            var blow = ThrowBlow.Throw(thrower, victim, attack, rng);
            Assert.True(blow.Hit);
            Assert.False(blow.Critical);
            Assert.True(blow.Damage >= 0f);
            Assert.Equal(0f, blow.Multiplier);
        }
        // Silver on somebody silver hurts: doubled, as a swing's is.
        attack.Flags |= WeaponFlag.Silver;
        victim.Actor!.Flags |= Actor.Flag.HurtBySilver;
        var a = ThrowBlow.Throw(thrower, victim, attack, new Rng(9));
        attack.Flags &= ~WeaponFlag.Silver;
        var b = ThrowBlow.Throw(thrower, victim, attack, new Rng(9));
        Assert.Equal(b.Damage * 2f, a.Damage, 3);
        Assert.Equal(Combat.MultiplierStep, a.Multiplier);
    }

    [Fact]
    public void A_strike_tag_crossed_outside_a_swing_does_no_harm()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var sim = new Simulation(place);
        var knights = place.Things.Where(t => t.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        var (attacker, target) = (knights[0], knights[1]);
        target.Actor!.Flags &= ~Actions.Unattackable;
        target.Position = attacker.Position + new Vector3(10f, 0f, 0f);
        Assert.Equal(OrderResult.Started, Actions.Attack(attacker, target, attacker.Actor!.Weapon, 0, 0));
        Assert.Equal(ActionMode.Swing, attacker.Actor.Action);
        // The swing's animation plays on, but the action is something else when its `Strike` comes.
        attacker.Actor.Action = ActionMode.Use;
        float health = target.Actor.Health;
        sim.Advance(3f, 1f / 30f);
        Assert.Empty(sim.Blows);
        Assert.Equal(health, target.Actor.Health);
    }
}
