// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

// The repair `packcells` (Save.LoadedPack) and the used-up potion's absence from a save: saves made
// before them hold potions at −1..−5, one under the broadsword, and two on one belt cell.
public partial class SaveTests
{
    static Item Carry(Game g, string template, int slot, (int, int) cell)
    {
        var christof = g.Place.Player!.Thing;
        var made = g.Place.Make(template)!;
        made.Item!.Thing = made;
        made.Owner = christof;
        made.Item.Location = slot;
        made.Item.Cell = cell;
        christof.Actor!.Inventory.Add(made.Item);
        return made.Item;
    }

    [Fact]
    public void A_potion_drunk_to_nothing_is_not_in_the_save()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var potion = Carry(g, "potionMinorHealth", Slot.Inventory, (0, 1));
        christof.Actor!.Type = ActorType.Human;                     // the potion is NOVAMPIRE
        Assert.Equal(UseResult.UsedUp, Items.Use(christof, potion, g.Place.Milliseconds, null));

        var save = SaveCapture.Take(g.Place, g.Session);
        var him = save.Party!.Members.First(m => m.Template.Equals("Christof", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(him.Actor!.Inventory.Values, t => t.Template.Equals("potionMinorHealth", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Such records, made on purpose: a potion at −5 under the broadsword, one at 0 on the belt's
    /// first cell beside a full one, two full potions on belt cell 4 with the belt otherwise full, and a
    /// letter (destroyed at zero, and 0 charges from its template). A load keeps the letter and the
    /// full potions, drops the two used ones, and gives every item a cell of its own.
    /// </summary>
    [Fact]
    public void A_save_with_used_up_potions_and_shared_cells_loads_into_a_sane_pack()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var me = christof.Actor!;
        foreach (var i in me.Inventory.Items.Where(i => (i.Location & Slot.Containers) != 0).ToList()) me.Inventory.Remove(i);
        Carry(g, "broadsword", Slot.Inventory, (0, 0));
        var under = Carry(g, "potionMinorHealth", Slot.Inventory, (0, 1));
        var drunk = Carry(g, "potionHealth", Slot.QuickUse, (0, 0));
        Carry(g, "potionHealth", Slot.QuickUse, (0, 0));
        for (int x = 1; x <= 3; x++) Carry(g, "potionHealth", Slot.QuickUse, (x, 0));
        Carry(g, "potionMajorHealth", Slot.QuickUse, (4, 0));
        Carry(g, "potionHealth", Slot.QuickUse, (4, 0));
        Carry(g, "CuthbertLetter", Slot.Vault, (9, 8));
        under.Amount = -5;
        drunk.Amount = 0;

        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(g.Place, g.Session))));
        var him = loaded.Place.Player!.Thing.Actor!;
        var carried = him.Inventory.Items.Where(i => (i.Location & Slot.Containers) != 0).ToList();

        Assert.DoesNotContain(carried, i => i.Amount <= 0 && (i.Flags & ItemFlags.DestroyedAtZeroCharges) != 0
                                            && !i.Name.Equals("CuthbertLetter", StringComparison.OrdinalIgnoreCase));
        Assert.Single(carried, i => i.Name.Equals("CuthbertLetter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(carried, i => i.Name.Equals("potionMinorHealth", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(6, carried.Count(i => i.Name.StartsWith("potion", StringComparison.OrdinalIgnoreCase)));   // eight made, two used up
        Assert.Equal(5, carried.Count(i => i.Location == Slot.QuickUse));
        // The one found second on the full belt goes to the pack's first free cell, right of the sword.
        Assert.Contains(carried, i => i.Location == Slot.Inventory && i.Cell == (2, 0)
                                      && i.Name.StartsWith("potion", StringComparison.OrdinalIgnoreCase));
        UsedUpTests.EachCellOnce(him);
    }

    [Fact]
    public void A_letter_and_a_spent_item_are_kept_by_the_load_as_1_1_keeps_them()
    {
        // Only a consumable 1.1 would have destroyed is dropped: a letter's template gives it no charge,
        // and an item not destroyed at zero stays, spent, in 1.1.
        var me = new Actor();
        var letter = new Item { Name = "CuthbertLetter", Type = ItemType.Text, Flags = ItemFlags.DestroyedAtZeroCharges, Location = Slot.Vault, Cell = (9, 8) };
        var spent = new Item { Name = "bag", Type = ItemType.General, Flags = ItemFlags.Spent, Location = Slot.Inventory, Cell = (0, 0) };
        var drunk = new Item { Name = "potionHealth", Type = ItemType.General, Flags = ItemFlags.DestroyedAtZeroCharges, Amount = -2, Location = Slot.Inventory, Cell = (0, 0) };
        foreach (var i in new[] { letter, spent, drunk }) me.Inventory.Add(i);
        var changes = LoadedPack.Mend(me, i => ReferenceEquals(i, letter) ? 0 : 1);
        Assert.Equal([letter, spent], me.Inventory.Items.OrderBy(i => i.Name == "bag").ToList());
        Assert.Single(changes);
        Assert.Equal((Slot.Inventory, (0, 0)), (spent.Location, spent.Cell));
    }
}
