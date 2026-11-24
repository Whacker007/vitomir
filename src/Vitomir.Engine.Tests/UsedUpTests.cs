// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The last of a consumable is gone** (the item's use → its destructor,
/// <see cref="UsedUp"/>), and a pick-up never lands on a cell something already holds.
/// In play, potions used from the pack or the belt stayed in Christof's list, hidden, at
/// their cells; the next pick-up could take such a cell, and a save and a load brought the potion back
/// on top of or under it — used again, to −1, −2, −5. These pin the use, the belt's refill, the
/// cascade against a crowded pack, and a worn item an equip takes off (ours: 1.1's equip displaces
/// nothing), which kept the cell it had before it was worn.
/// </summary>
public class UsedUpTests
{
    static (Location Place, WorldThing Christof) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        // That play's Christof, still human: the health potions are NOVAMPIRE (the use test's restrictions).
        place.Steered!.Actor!.Type = ActorType.Human;
        return (place, place.Steered!);
    }

    /// <summary>An item made straight into a grid on the character, as a script's grant makes one.</summary>
    static Item Give(Location place, WorldThing to, string template, int slot, (int, int) cell)
    {
        var made = place.Make(template)!;
        var item = made.Item!;
        item.Thing = made;
        made.Owner = to;
        item.Location = slot;
        item.Cell = cell;
        to.Actor!.Inventory.Add(item);
        return item;
    }

    /// <summary>Every item in each grid on its own cells, inside the grid — what the pane and the strip can draw one of.</summary>
    internal static void EachCellOnce(Actor actor)
    {
        foreach (int grid in new[] { Slot.QuickUse, Slot.Inventory, Slot.Vault })
        {
            var (w, h) = Slot.Size(grid);
            var owner = new Item?[w, h];
            foreach (var item in actor.Inventory.Items)
            {
                if (item.Location != grid) continue;
                var (across, down) = item.IconCells;
                var (x, y) = item.Cell;
                Assert.True(x >= 0 && y >= 0 && x + across <= w && y + down <= h,
                            $"{item.Name} at {x},{y} ({across}x{down}) leaves {Slot.Name(grid)}");
                for (int dx = 0; dx < across; dx++)
                    for (int dy = 0; dy < down; dy++)
                    {
                        Assert.True(owner[x + dx, y + dy] is null,
                                    $"{item.Name} and {owner[x + dx, y + dy]?.Name} share {Slot.Name(grid)} {x + dx},{y + dy}");
                        owner[x + dx, y + dy] = item;
                    }
            }
        }
        Assert.Equal(actor.Inventory.Items.Count, actor.Inventory.Items.Distinct().Count());
    }

    [Fact]
    public void A_potion_drunk_to_its_last_charge_is_out_of_the_list_and_the_place()
    {
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var me = christof.Actor!;
        var potion = Give(place, christof, "potionMinorHealth", Slot.Inventory, (0, 1));
        Assert.Equal(1, potion.Amount);
        Assert.True((potion.Flags & ItemFlags.DestroyedAtZeroCharges) != 0);
        int guid = potion.Thing!.Guid;

        Assert.Equal(UseResult.UsedUp, Items.Use(christof, potion, place.Milliseconds, null));
        Assert.True(potion.Destroyed);
        Assert.DoesNotContain(potion, me.Inventory.Items);          // the destructor's removal
        Assert.True(potion.Thing!.Removed);
        Assert.Null(place.Thing(guid));
        Assert.Equal(0, potion.Amount);                             // never below: nothing is left to use again
    }

    [Fact]
    public void An_item_that_is_not_destroyed_at_zero_stays_spent_in_its_cell()
    {
        // The amount clamped to 0 and the spent bit set; the item stays, dead weight, as in 1.1.
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var bag = new Item { Name = "bag", Type = ItemType.General, Amount = 1, Location = Slot.Inventory, Cell = (3, 0) };
        christof.Actor!.Inventory.Add(bag);
        Assert.Equal(UseResult.UsedUp, Items.Use(christof, bag, place.Milliseconds, null));
        Assert.Contains(bag, christof.Actor.Inventory.Items);
        Assert.Equal((0, ItemFlags.Spent), (bag.Amount, bag.Flags & ItemFlags.Spent));
        Assert.Equal(UseResult.Refused, Items.Use(christof, bag, place.Milliseconds, null));
        Assert.Equal(0, bag.Amount);
    }

    [Fact]
    public void A_belt_potion_used_up_is_refilled_into_its_cell_and_the_cell_holds_one_item()
    {
        // The used one gone, the pack's next of the same template into the same cell.
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var me = christof.Actor!;
        var onBelt = Give(place, christof, "potionHealth", Slot.QuickUse, (4, 0));
        var inPack = Give(place, christof, "potionHealth", Slot.Inventory, (6, 3));
        string said = QuickSlots.Use(christof, onBelt, autoload: true,
                                     i => Items.Use(christof, i, place.Milliseconds, null).ToString());
        Assert.Contains("quick slot 5", said);
        Assert.DoesNotContain(onBelt, me.Inventory.Items);
        Assert.Equal((Slot.QuickUse, (4, 0)), (inPack.Location, inPack.Cell));
        Assert.Single(me.Inventory.Items, i => i.Location == Slot.QuickUse && i.Cell == (4, 0));
        EachCellOnce(me);
    }

    [Fact]
    public void A_pick_up_after_a_potion_is_drunk_takes_its_cell_and_nothing_else_is_there()
    {
        // That play's overlap: the drunk potion's cell was free to the search while the
        // potion was still listed there, and both came back from a load.
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var me = christof.Actor!;
        foreach (var i in me.Inventory.Items.Where(i => (i.Location & Slot.Containers) != 0).ToList()) me.Inventory.Remove(i);
        var potion = Give(place, christof, "potionMinorHealth", Slot.Inventory, (0, 0));
        Items.Use(christof, potion, place.Milliseconds, null);
        var found = place.Make("dagger")!;
        found.Item!.Thing = found;
        Assert.Equal(StowResult.Stowed, Stow.Put(christof, found.Item, steering: true));
        Assert.Equal((Slot.Inventory, (0, 0)), (found.Item.Location, found.Item.Cell));
        Assert.Single(me.Inventory.Items, i => i.Location == Slot.Inventory);
    }

    [Fact]
    public void Pick_ups_into_a_crowded_pack_never_overlap_and_each_is_listed_once()
    {
        // The cascade until nothing will take another: pack first for what can be worn, the
        // belt first for what cannot, the repack when neither has room.
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var me = christof.Actor!;
        foreach (var i in me.Inventory.Items.Where(i => (i.Location & Slot.Containers) != 0).ToList()) me.Inventory.Remove(i);
        string[] finds = ["broadsword", "potionHealth", "dagger", "shield", "potionMinorHealth", "club", "holyWater", "ringOnyx"];
        string last = "";
        for (int n = 0; n < 60; n++)
        {
            var found = place.Make(finds[n % finds.Length])!;
            found.Item!.Thing = found;
            var got = Stow.Put(christof, found.Item, steering: false);
            if (got != StowResult.Stowed) { last = $"{found.Item.Name} {got} after {n}"; break; }
            EachCellOnce(me);
        }
        Assert.NotEqual("", last);                                  // it ran until the pack was full
        Assert.True(me.Inventory.Items.Count(i => i.Location == Slot.Inventory) >= 8, last);
        Assert.DoesNotContain(me.Inventory.Items, i => i.Location == Slot.None);
        EachCellOnce(me);
    }

    [Fact]
    public void A_worn_sword_an_equip_takes_off_goes_to_a_free_cell_not_the_one_it_had_before()
    {
        using var files = Install.Open();
        var (place, christof) = Open(files);
        var me = christof.Actor!;
        var effects = new EffectDefinitions(files);
        foreach (var i in me.Inventory.Items.ToList()) me.Inventory.Remove(i);
        var sword = Give(place, christof, "broadsword", Slot.Inventory, (0, 0));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, sword, Slot.RightWeapon, effects, 0));
        Assert.Equal((0, 0), sword.Cell);                           // worn: one cell, as 1.1 writes it
        var potion = Give(place, christof, "potionHealth", Slot.Inventory, (0, 0));     // where the sword was
        var rapier = Give(place, christof, "rapier", Slot.Inventory, (4, 0));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, rapier, Slot.None, effects, 0));
        Assert.Equal(Slot.RightWeapon, rapier.Location);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.NotEqual((0, 0), sword.Cell);
        Assert.Equal((0, 0), potion.Cell);
        EachCellOnce(me);
    }
}
