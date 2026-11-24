// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The two modifier arms of the ground click Vitomir had not written:
/// **Alt** fires a ranged weapon at the floor — only with nothing on the
/// cursor and a ranged attack current, at once, with no approach — and **Space** turns in place
/// (the move order's bit-8 branch) without cancelling what the character was doing. And Space on a target,
/// the hold attack: the strike plays where the attacker stands.
/// </summary>
public class ClickOrderTests
{
    static WorldThing Body(Vector3 at, int guid = 99) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Player,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f },
    };

    static Weapon Bow()
    {
        var w = new Weapon { Name = "shortBow" };
        var a = w.Attacks[0];
        a.Flags = WeaponFlag.Ranged; a.AmmoType = 1; a.MaxAmmo = 20; a.Ammo = 5; a.AmmoUse = 1;
        a.Projectile = "arrow"; a.ReloadTime = 1500;
        return w;
    }

    [Fact]
    public void Alt_fires_at_the_ground_only_with_a_ranged_attack_and_nothing_on_the_cursor()
    {
        var shooter = Body(Vector3.Zero);
        shooter.Actor!.Weapon = Bow();
        Assert.True(ClickOrders.FiresAtGround(shooter, InputModifier.Alt));
        Assert.True(ClickOrders.FiresAtGround(shooter, InputModifier.Alt | InputModifier.Shift));
        Assert.False(ClickOrders.FiresAtGround(shooter, InputModifier.None));
        Assert.False(ClickOrders.FiresAtGround(shooter, InputModifier.Shift));
        shooter.Actor.Weapon = new Weapon { Name = "sword" };
        Assert.False(ClickOrders.FiresAtGround(shooter, InputModifier.Alt));   // a sword walks, and Alt means nothing to it
        shooter.Actor.Weapon = null;
        Assert.False(ClickOrders.FiresAtGround(shooter, InputModifier.Alt));
        Assert.Equal(1, ClickOrders.AttackIndex(InputModifier.Alt | InputModifier.Shift));
        Assert.Equal(0, ClickOrders.AttackIndex(InputModifier.Alt));
    }

    [Fact]
    public void The_shot_at_the_ground_turns_to_the_point_and_swings_at_once_with_no_target()
    {
        var shooter = Body(Vector3.Zero);
        shooter.Actor!.Weapon = Bow();
        var at = new Vector3(0f, -400f, 0f);                         // straight behind him (he faces +Y)
        Assert.Equal(OrderResult.Started, ClickOrders.AttackPoint(shooter, at, 0, nowMs: 1000));
        Assert.Equal(ActionMode.Swing, shooter.Actor.Action);
        Assert.Null(shooter.Actor.Target);
        Assert.Equal(at, shooter.Actor.ActionPoint);
        Assert.Equal(180f, MathF.Abs(shooter.Orientation.Z), 3);
        Assert.True(shooter.Actor.BusyUntil > 1000);
        // Busy with that, a second shot is not now.
        Assert.Equal(OrderResult.Busy, ClickOrders.AttackPoint(shooter, at, 0, nowMs: 1001));
    }

    [Fact]
    public void An_ethereal_or_unarmed_shooter_is_refused_the_ground()
    {
        var ghost = Body(Vector3.Zero);
        ghost.Actor!.Weapon = Bow();
        ghost.Actor.Flags |= Actor.Flag.Ethereal;
        Assert.Equal(OrderResult.Refused, ClickOrders.AttackPoint(ghost, Vector3.UnitX * 100f, 0, 0));
        var bare = Body(Vector3.Zero);
        Assert.Equal(OrderResult.Refused, ClickOrders.AttackPoint(bare, Vector3.UnitX * 100f, 0, 0));
    }

    [Fact]
    public void Space_on_the_ground_turns_in_place_and_cancels_nothing()
    {
        var turner = Body(Vector3.Zero);
        turner.Actor!.Action = ActionMode.Use;
        Assert.Equal(OrderResult.Ignored, ClickOrders.TurnInPlace(turner, new Vector3(100f, 0f, 0f), nowMs: 0));
        Assert.Equal(-90f, turner.Orientation.Z, 3);                 // facing +X
        Assert.Equal(ActionMode.Use, turner.Actor.Action);
        Assert.True(ClickOrders.Turns(InputModifier.Space | InputModifier.Shift));
        Assert.False(ClickOrders.Turns(InputModifier.Shift));
        // Busy, it does not turn at all (the move order answers 2 before the branch).
        turner.Actor.BusyUntil = 500;
        Assert.Equal(OrderResult.Busy, ClickOrders.TurnInPlace(turner, new Vector3(-100f, 0f, 0f), nowMs: 0));
        Assert.Equal(-90f, turner.Orientation.Z, 3);
    }

    [Fact]
    public void Space_on_a_target_strikes_where_the_attacker_stands_however_far_away()
    {
        var me = Body(Vector3.Zero);
        me.Actor!.Weapon = new Weapon { Name = "sword" };
        var them = Body(new Vector3(0f, 900f, 0f), guid: 100);
        Assert.Equal(OrderResult.Started, Actions.Attack(me, them, me.Actor.Weapon, 0, nowMs: 0, strikeNow: true));
        Assert.Equal(ActionMode.Swing, me.Actor.Action);
        Assert.Same(them, me.Actor.Target);
        // Without it, the order closes in first — at once, inside the order —
        // and answers by what the close-in left: a body with no walker to close with is
        // stopped there, and the order answers 1 (it answered 4 and closed in on the next frame).
        var other = Body(Vector3.Zero);
        other.Actor!.Weapon = new Weapon { Name = "sword" };
        Assert.Equal(OrderResult.Refused, Actions.Attack(other, them, other.Actor.Weapon, 0, nowMs: 0));
        Assert.Equal(ActionMode.None, other.Actor.Action);
    }

    /// <summary>
    /// The end of a swing: only with the repeat-attack option, at whatever is under the
    /// pointer now; the left button asks for the **first** attack even with Shift held (1.1 tests
    /// Windows' shift key codes against its scan-code array, which are Mute and Calculator), the
    /// middle button for the second, and Space for the hold.
    /// </summary>
    [Fact]
    public void A_held_button_asks_for_the_first_attack_whatever_shift_says_and_the_middle_one_for_the_second()
    {
        var foe = Body(new Vector3(0f, 100f, 0f), guid: 5);
        Assert.Null(SwingRepeat.Ask(false, true, foe, leftHeld: true, middleHeld: false, InputModifier.None));
        Assert.Null(SwingRepeat.Ask(true, false, foe, leftHeld: true, middleHeld: false, InputModifier.None));
        Assert.Null(SwingRepeat.Ask(true, true, null, leftHeld: true, middleHeld: false, InputModifier.None));
        Assert.Null(SwingRepeat.Ask(true, true, foe, leftHeld: false, middleHeld: false, InputModifier.Shift));
        Assert.Equal(new Actions.Asking(foe, 0, false), SwingRepeat.Ask(true, true, foe, true, false, InputModifier.Shift));
        Assert.Equal(new Actions.Asking(foe, 0, true), SwingRepeat.Ask(true, true, foe, true, true, InputModifier.Space));
        Assert.Equal(new Actions.Asking(foe, 1, false), SwingRepeat.Ask(true, true, foe, false, true, InputModifier.None));
    }

    /// <summary>
    /// The quality of life <c>heldshift</c>: with it on, the left button held with Shift goes on asking for the **second** attack;
    /// without Shift, the first as ever; the middle button the second either way.
    /// </summary>
    [Fact]
    public void With_held_shift_on_a_held_shift_and_button_keep_asking_for_the_second_attack()
    {
        var foe = Body(new Vector3(0f, 100f, 0f), guid: 5);
        Assert.Equal(new Actions.Asking(foe, 1, false), SwingRepeat.Ask(true, true, foe, true, false, InputModifier.Shift, heldShift: true));
        Assert.Equal(new Actions.Asking(foe, 1, true),
                     SwingRepeat.Ask(true, true, foe, true, false, InputModifier.Shift | InputModifier.Space, heldShift: true));
        Assert.Equal(new Actions.Asking(foe, 0, false), SwingRepeat.Ask(true, true, foe, true, false, InputModifier.None, heldShift: true));
        Assert.Equal(new Actions.Asking(foe, 1, false), SwingRepeat.Ask(true, true, foe, false, true, InputModifier.None, heldShift: true));
        Assert.Null(SwingRepeat.Ask(false, true, foe, true, false, InputModifier.Shift, heldShift: true));   // the option still gates it
    }
}
