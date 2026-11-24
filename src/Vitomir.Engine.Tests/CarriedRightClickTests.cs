// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A right click over the pack with something on the cursor** (a comparison with 1.1's leftover).
/// 1.1 has no arm for it: an icon's right click (the icon's binding slot
/// 2) uses the icon's own item, and the grids register no right-button handler at
/// all (slot 0 only). So the carried item stays on the cursor:
/// the icon under the pointer is used, and an empty cell does nothing. Vitomir had put the carried
/// item back where it was lifted from.
/// </summary>
public class CarriedRightClickTests
{
    const string Nui = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        element weargrid name=invRightWeapon pos=353,300 size=54,96 gridsize=2,3
        """;

    static (InventoryHands Hands, WorldThing Thing) Set()
    {
        using var files = Install.Open();
        var actor = new Actor { Type = ActorType.Human, Name = "Christof" };
        actor.Health = actor.MaxHealth = 100;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
            Type = ThingType.Player, Actor = actor,
        };
        var pane = new InventoryPane(NuiPane.Parse(Nui, "inventoryPane.nui")) { Subject = actor };
        return (new InventoryHands(pane, thing, new EffectDefinitions(files)) { Rules = new Qol() }, thing);
    }

    static Item Potion(int x) => new()
    {
        Name = $"potion{x}", Type = ItemType.General, IconSize = 0, Icon = "vialIcon",
        Location = Slot.Inventory, Cell = (x, 0),
    };

    [Fact]
    public void A_right_click_on_an_icon_uses_it_and_leaves_the_carried_item_on_the_cursor()
    {
        var (hands, thing) = Set();
        var carried = Potion(0);
        var other = Potion(3);
        thing.Actor!.Inventory.Add(carried);
        thing.Actor.Inventory.Add(other);
        var used = new List<Item>();
        hands.Use = item => { used.Add(item); return $"used {item.Name}"; };

        Assert.Equal(HandAction.Lifted, hands.Click(346 + 5, 145 + 127).What);
        Assert.Same(carried, hands.Carrying);

        var got = hands.RightClick(346 + 3 * 27 + 5, 145 + 127);
        Assert.Equal(HandAction.Used, got.What);
        Assert.Same(other, Assert.Single(used));
        Assert.Same(carried, hands.Carrying);
    }

    [Fact]
    public void A_right_click_on_an_empty_cell_with_something_carried_does_nothing()
    {
        var (hands, thing) = Set();
        var carried = Potion(0);
        thing.Actor!.Inventory.Add(carried);

        hands.Click(346 + 5, 145 + 127);
        var got = hands.RightClick(346 + 8 * 27 + 5, 145 + 5);
        Assert.Equal(HandAction.Nothing, got.What);
        Assert.Same(carried, hands.Carrying);
        Assert.NotEqual(Slot.Inventory, carried.Location);
    }
}
