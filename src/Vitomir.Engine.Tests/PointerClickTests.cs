// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What a click does with something on the pointer** (on the ground,
/// and on a thing), a comparison with 1.1's second and third gaps, built. A weapon whose
/// current attack is <c>THROWN</c> — a grenade, a satchel — is taken in hand and thrown:
/// at the point with Alt on the ground, at the thing on any attack click.
/// It stays on the pointer while it flies, and the throw spends one from it there. And
/// anything else on the pointer is handed to the put-down order **before** the ground is asked
/// whether it is walkable, so with space held it can go on a table top or a
/// ledge; Vitomir said "not walkable ground" first. Without space the order routes to the struck
/// surface and refuses one off the walk graph (PutRoute). The click's own order of arms is the harness's
/// (`src/Vitomir.Classic/Controller/ClassicGame.Input.cs`); these pin the engine halves it calls.
/// </summary>
public class PointerClickTests
{
    static WorldThing Body(Vector3 at, int guid = 99) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Player,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f },
    };

    /// <summary>A grenade: every shipped thrown weapon is ranged too (`0x21`).</summary>
    static Item Grenade(int amount, bool destroyedAtZero = true)
    {
        var w = new Weapon { Name = "grenade" };
        var a = w.Attacks[0];
        a.Flags = WeaponFlag.Ranged | WeaponFlag.Thrown; a.Projectile = "grenade"; a.ReloadTime = 1000;
        return new Item
        {
            Name = "grenade", Type = ItemType.Weapon, Weapon = w, Amount = amount,
            Flags = destroyedAtZero ? ItemFlags.DestroyedAtZeroCharges : 0,
        };
    }

    [Fact]
    public void An_alt_click_on_the_ground_throws_the_grenade_on_the_pointer_at_the_point()
    {
        var me = Body(Vector3.Zero);
        var sword = new Weapon { Name = "sword" };
        me.Actor!.Weapon = sword;
        var grenade = Grenade(2);
        me.Actor.Held = grenade;

        // Alt with a grenade on the pointer: the throw, not the shot (whose pointer must be empty)
        // and not the put-down. Without Alt the ground click puts it down, as anything else.
        Assert.True(ClickOrders.ThrowsFromPointer(me, InputModifier.Alt));
        Assert.True(ClickOrders.ThrowsFromPointer(me, InputModifier.Alt | InputModifier.Shift));
        Assert.False(ClickOrders.ThrowsFromPointer(me, InputModifier.None));
        Assert.False(ClickOrders.FiresAtGround(me, InputModifier.Alt));

        var at = new Vector3(0f, 300f, 0f);
        Assert.Equal(OrderResult.Started, ClickOrders.ThrowFromPointer(me, at, InputModifier.Alt, nowMs: 1000));
        // Taken in hand (the weapon swap): the sword kept to come back, the grenade
        // still on the pointer, and the strike at the point with no target thing.
        Assert.Same(grenade.Weapon, me.Actor.Weapon);
        Assert.Same(sword, me.Actor.PrevWeapon);
        Assert.True(WeaponOverride.Active(me.Actor));
        Assert.Same(grenade, me.Actor.Held);
        Assert.Equal(ActionMode.Swing, me.Actor.Action);
        Assert.Null(me.Actor.Target);
        Assert.Equal(at, me.Actor.ActionPoint);

        // The throw hands the sword back and spends one from the grenade where it is — on the pointer
        // (the use on the weapon thing itself).
        Projectiles.Thrown(me, grenade.Weapon!);
        Assert.Same(sword, me.Actor.Weapon);
        Assert.False(WeaponOverride.Active(me.Actor));
        Assert.Equal(1, grenade.Amount);
        Assert.Same(grenade, me.Actor.Held);

        // The last one, destroyed at none left, leaves the pointer empty (ours: the destructor's part).
        ClickOrders.TakeThrownInHand(me);
        Projectiles.Thrown(me, grenade.Weapon!);
        Assert.True(grenade.Destroyed);
        Assert.Null(me.Actor.Held);
        Assert.Same(sword, me.Actor.Weapon);
    }

    [Fact]
    public void Any_attack_click_on_a_thing_takes_a_grenade_on_the_pointer_in_hand_first()
    {
        var me = Body(Vector3.Zero);
        var sword = new Weapon { Name = "sword" };
        me.Actor!.Weapon = sword;

        // Nothing on the pointer, or something that is not thrown: the hand is left alone.
        Assert.False(ClickOrders.TakeThrownInHand(me));
        me.Actor.Held = new Item { Name = "broadsword", Type = ItemType.Weapon, Weapon = new Weapon { Name = "broadsword" } };
        Assert.False(ClickOrders.TakeThrownInHand(me));
        me.Actor.Held = new Item { Name = "potionhealth", Type = ItemType.General };
        Assert.False(ClickOrders.TakeThrownInHand(me));
        Assert.Same(sword, me.Actor.Weapon);

        // A grenade goes in hand, with no modifier asked, and the attack is then ordered
        // with it as with any weapon in hand; a second swap while one is in force does nothing.
        var grenade = Grenade(3);
        me.Actor.Held = grenade;
        Assert.True(ClickOrders.TakeThrownInHand(me));
        Assert.Same(grenade.Weapon, me.Actor.Weapon);
        Assert.False(ClickOrders.TakeThrownInHand(me));
        Assert.Same(sword, me.Actor.PrevWeapon);

        // A grenade whose current attack is not the throw is not one to throw (the click tests the
        // current attack's flag).
        WeaponOverride.Cancel(me);
        grenade.Weapon!.Attacks[0].Flags = WeaponFlag.Ranged;
        Assert.Null(ClickOrders.ThrownOnPointer(me));
        Assert.False(ClickOrders.TakeThrownInHand(me));
    }

    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    [Fact]
    public void An_item_is_put_down_on_a_raised_surface_only_with_space_held()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var thing = place.Make("potionhealth")!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        Assert.True(Ground.Hand(who, item));

        // A table top beside him: 35 up and 30 across, within the put-down's 50, and no piece of the
        // walk graph. The ground click asks the put-down first; **the order routes to the
        // struck surface and refuses one with no path node**
        // (PutRoute) — so it stays on the pointer and nothing walks. With space held there is no route
        // to ask, and it lands on the top itself, not the floor below.
        Assert.True(place.Player!.Ground());
        var top = who.Position + new Vector3(0f, 30f, 35f);
        Assert.Equal(OrderResult.Refused, Actions.Put(who, item, top, nowMs: 0, node: -1));
        Assert.Same(item, who.Actor.Held);
        Assert.NotEqual(ActionMode.Drop, who.Actor.Action);
        Assert.Equal(OrderResult.Ignored, Actions.Put(who, item, top, nowMs: 0, InputModifier.Space, node: -1));
        Assert.True(thing.InWorld);
        Assert.Equal(top, thing.Position);
        Assert.Null(who.Actor.Held);

        // A point with no ground under it anywhere near: refused at the order, where
        // Vitomir's first tick had ended it — the item stays on the pointer and nothing walks.
        var other = place.Make("potionhealth")!;
        var otherItem = other.Item!;
        other.Owner = who;
        who.Actor.Inventory.Add(otherItem);
        Assert.True(Ground.Hand(who, otherItem));
        var sky = who.Position + new Vector3(0f, 400f, 2000f);
        Assert.Equal(OrderResult.Refused, Actions.Put(who, otherItem, sky, nowMs: 100));
        Assert.False(other.InWorld);
        Assert.Same(otherItem, who.Actor.Held);
        Assert.NotEqual(ActionMode.Drop, who.Actor.Action);
    }
}
