// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The quick slot's refill behind the Controls screen's *auto-load quick slot from
/// backpack*: only with the option on, only when the use answered 1 (destroyed, or a
/// scroll or a thrown weapon taken into the hand), and only from the **pack** — the first item of
/// the same template in the character's own item order, into the same belt cell.
/// </summary>
public class QuickRefillTests
{
    static Item Potion(int location, int cellX = 0, string name = "bloodPack") =>
        new() { Name = name, Type = ItemType.General, Location = location, Cell = (cellX, 0), Amount = 1 };

    [Fact]
    public void A_used_up_belt_item_is_replaced_from_the_pack_into_the_same_cell_with_the_option_on()
    {
        var actor = new Actor();
        var vaulted = Potion(Slot.Vault);
        var packed = Potion(Slot.Inventory, 4);
        var other = Potion(Slot.Inventory, 2, name: "rat");
        foreach (var i in new[] { other, packed, vaulted }) actor.Inventory.Add(i);
        var used = Potion(Slot.QuickUse, 3);
        var (cell, template) = QuickRefill.Before(used);
        Assert.Equal((3, "bloodPack"), (cell, template));
        used.Destroyed = true;
        Assert.True(QuickRefill.Emptied(used, UseResult.UsedUp));
        Assert.Null(QuickRefill.From(actor, cell, template, option: false));
        Assert.Same(packed, QuickRefill.From(actor, cell, template, option: true));
        Assert.Equal(Slot.QuickUse, packed.Location);
        Assert.Equal((3, 0), packed.Cell);
        Assert.Equal(Slot.Vault, vaulted.Location);                // the vault is not the pack
        Assert.Null(QuickRefill.From(actor, cell, template, option: true));   // none left
    }

    [Fact]
    public void Only_a_use_that_answered_one_refills()
    {
        var spent = Potion(Slot.QuickUse);
        Assert.False(QuickRefill.Emptied(spent, UseResult.UsedUp));          // spent, not destroyed: still in its cell
        Assert.False(QuickRefill.Emptied(spent, UseResult.Used));
        Assert.True(QuickRefill.Emptied(new Item { Name = "scroll", Type = ItemType.Scroll }, UseResult.Held));
        var sword = new Item { Name = "sword", Type = ItemType.Weapon, Weapon = new Weapon { Name = "sword" } };
        Assert.False(QuickRefill.Emptied(sword, UseResult.Held));
        var stake = new Weapon { Name = "stake" };
        stake.Attacks[0].Flags = WeaponFlag.Thrown | WeaponFlag.Ranged;
        Assert.True(QuickRefill.Emptied(new Item { Name = "stake", Type = ItemType.Weapon, Weapon = stake }, UseResult.Held));
        Assert.Equal((-1, ""), QuickRefill.Before(Potion(Slot.Inventory)));
    }
}
