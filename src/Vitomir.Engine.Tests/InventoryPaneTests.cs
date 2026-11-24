// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The inventory pane as state. The slot mask behind it was closed from 1.1's saves months
/// before this file existed; what is tested here is the join between it and the pane's own grids.
/// </summary>
public class InventoryPaneTests
{
    /// <summary>The shipped pane, trimmed to the elements these tests name. Values are verbatim.</summary>
    const string Nui = """
        name=inventoryPane
        pos=0,0
        visGroup=2
        quad mat=InventoryPane01.tga pos=384,224 size=256,256
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4 flags=0x02
        element weargrid name=invRightWeapon pos=353,300 size=54,96 gridsize=2,3 flags=0x02
        element weargrid name=invRightArm pos=421,320 size=27,64 gridsize=1,2 flags=0x02
        element weargrid name=invRightHand pos=421,283 size=27,32 gridsize=1,1 flags=0x02
        element weargrid name=invLeftWeapon pos=555,300 size=54,96 gridsize=2,3 flags=0x02
        element weargrid name=invLeftArm pos=514,320 size=27,64 gridsize=1,2 flags=0x02
        element weargrid name=invLeftHand pos=514,283 size=27,32 gridsize=1,1 flags=0x02
        element weargrid name=invBody pos=454,308 size=54,96 gridsize=2,3 flags=0x02
        element weargrid name=invHead pos=454,410 size=54,64 gridsize=2,2 flags=0x02
        element weargrid name=invNeck pos=411,439 size=27,32 gridsize=1,1 flags=0x02
        element textbox name=invGold pos=539,423 size=69,15 flags=0x02 textboxflags=0x01
        element button name=invGoldButton pos=560,442 size=26,32 flags=0x02
        """;

    static InventoryPane Pane(Actor? subject = null) =>
        new(NuiPane.Parse(Nui, "inventoryPane.nui")) { Subject = subject };

    /// <summary>`RPG_ICONSIZE_*`: 0 and anything unlisted are 1x1; **5 is 2x3**, 4 is 2x2.</summary>
    static Item Thing(string name, int slot, int iconSize = 0, int x = 0, int y = 0) =>
        new() { Name = name, IconSize = iconSize, Location = slot, Cell = (x, y) };

    [Fact]
    public void Every_grid_the_shipped_pane_declares_is_a_whole_number_of_the_game_s_one_cell()
    {
        var pane = Pane();
        var grids = pane.Grids().ToList();
        Assert.Equal(10, grids.Count);              // nine worn slots and the pack

        foreach (var grid in grids)
        {
            var e = pane.Control(grid.Element)!;
            // The element's declared pixel size is exactly its cells at 27 x 32, with no remainder.
            Assert.Equal(e.Size[0], grid.Across * InventoryPane.CellWidth);
            Assert.Equal(e.Size[1], grid.Down * InventoryPane.CellHeight);
        }
    }

    /// <summary>
    /// The `.nui`'s own `gridsize` and the engine's container table
    /// are two independent sources for the same three numbers, and they agree.
    /// </summary>
    [Fact]
    public void The_pack_is_ten_by_four_from_both_of_the_sources_that_state_it()
    {
        var pack = Pane().Grids().Single(g => g.Slot == Slot.Inventory);
        Assert.Equal((10, 4), (pack.Across, pack.Down));
        Assert.Equal(Slot.Size(Slot.Inventory), (pack.Across, pack.Down));
        Assert.True(pack.IsContainer);
        Assert.Equal((270, 128), pack.Pixels);
    }

    [Fact]
    public void A_worn_slot_holds_one_item_and_the_pack_holds_many()
    {
        var a = new Actor();
        a.Inventory.Add(Thing("broadsword", Slot.RightWeapon, iconSize: 5));     // 2 x 3
        a.Inventory.Add(Thing("rags", Slot.Body, iconSize: 5));
        a.Inventory.Add(Thing("vial", Slot.Inventory, x: 0, y: 0));
        a.Inventory.Add(Thing("vial", Slot.Inventory, x: 3, y: 2));

        var placed = Pane(a).Placed().ToList();
        Assert.Equal(4, placed.Count);

        var sword = placed.Single(p => p.Item.Name == "broadsword");
        Assert.Equal("invRightWeapon", sword.Grid.Element);
        Assert.Equal((0, 0), (sword.X, sword.Y));   // a worn item ignores its own cell pair
        Assert.Equal((2, 3), sword.Cells);

        // The pack's two take the cells their own records name.
        var inPack = placed.Where(p => p.Grid.Slot == Slot.Inventory).OrderBy(p => p.X).ToList();
        Assert.Equal((0, 0), (inPack[0].X, inPack[0].Y));
        Assert.Equal((3, 2), (inPack[1].X, inPack[1].Y));
    }

    /// <summary>
    /// **An item fits a slot when its icon fits the grid**, which is the rule the whole pane runs
    /// on: the broadsword is `RPG_ICONSIZE_2x3` and `invRightWeapon` is 2 x 3, a ring is 1 x 1 and
    /// `invRightHand` is 1 x 1 — and the same rule is why the belt takes only 1 x 1 items.
    /// </summary>
    [Fact]
    public void An_item_fits_a_slot_when_its_icon_fits_the_grid()
    {
        var pane = Pane();
        var weapon = pane.Grids().Single(g => g.Slot == Slot.RightWeapon);
        var hand = pane.Grids().Single(g => g.Slot == Slot.RightHand);

        var sword = Thing("broadsword", Slot.RightWeapon, iconSize: 5);          // 2 x 3
        var ring = Thing("ringman", Slot.RightHand);                             // 1 x 1

        Assert.True(InventoryPane.Fits(sword, weapon));
        Assert.True(InventoryPane.Fits(ring, hand));
        Assert.True(InventoryPane.Fits(ring, weapon));                           // small fits big
        Assert.False(InventoryPane.Fits(sword, hand));                           // big does not fit small
    }

    /// <summary>
    /// The quick-use strip takes only a 1 x 1 icon (the strip's bit is given only then): a
    /// 2 x 1 item, which would fit the 5 x 1 strip, is refused.
    /// </summary>
    [Fact]
    public void The_quick_strip_takes_only_one_by_one_icons()
    {
        var strip = new InventoryGrid("sbQuickUse", Slot.QuickUse, 5, 1);
        Assert.True(InventoryPane.Fits(Thing("vial", Slot.Inventory), strip));
        Assert.False(InventoryPane.Fits(Thing("launch_grenades", Slot.Inventory, iconSize: 7), strip));   // 2 x 1
        Assert.True(InventoryPane.Fits(Thing("launch_grenades", Slot.Inventory, iconSize: 7), Pane().Grids().Single(g => g.Slot == Slot.Inventory)));
    }

    [Fact]
    public void A_footprint_that_runs_off_its_grid_is_reported_rather_than_drawn()
    {
        var grid = new InventoryGrid("invInventory", Slot.Inventory, 10, 4);
        var sword = Thing("broadsword", Slot.Inventory, iconSize: 5);            // 2 x 3
        Assert.True(InventoryPane.Inside(new PlacedItem(sword, grid, 8, 1)));
        Assert.False(InventoryPane.Inside(new PlacedItem(sword, grid, 9, 1)));   // 2 wide from column 9
        Assert.False(InventoryPane.Inside(new PlacedItem(sword, grid, 0, 2)));   // 3 tall from row 2
        Assert.False(InventoryPane.Inside(new PlacedItem(sword, grid, -1, 0)));
    }

    [Fact]
    public void A_drop_into_the_pack_takes_the_first_free_cell_row_by_row()
    {
        var a = new Actor();
        a.Inventory.Add(Thing("vial", Slot.Inventory, x: 0, y: 0));
        var pane = Pane(a);
        var pack = pane.Grids().Single(g => g.Slot == Slot.Inventory);

        // The next 1 x 1 goes beside it, not under it: the scan is row by row from the top left.
        Assert.Equal((1, 0), pane.FreeCell(pack, Thing("vial", Slot.Inventory)));

        // A 2 x 3 cannot start on row 0 of a 4-row grid without covering the occupied cell, so it
        // takes the first column pair that is clear.
        Assert.Equal((1, 0), pane.FreeCell(pack, Thing("broadsword", Slot.Inventory, iconSize: 5)));

        // Nothing fits a grid smaller than the item, however empty it is.
        var hand = pane.Grids().Single(g => g.Slot == Slot.RightHand);
        Assert.Null(pane.FreeCell(hand, Thing("broadsword", Slot.RightHand, iconSize: 5)));
    }

    [Fact]
    public void The_gold_box_is_the_pane_s_only_text_and_it_is_centred()
    {
        var pane = Pane();
        pane.Gold = 214;
        Assert.Equal("214", pane.Texts()["invGold"]);
        Assert.Equal(NuiPane.TextBoxFlags.Centre, NuiPane.TextBox(pane.Control("invGold")!));
    }

    [Fact]
    public void Without_a_subject_the_pane_still_has_its_grids_and_nothing_in_them()
    {
        var pane = Pane();
        Assert.Equal(10, pane.Grids().Count());
        Assert.Empty(pane.Placed());
    }

    /// <summary>The shipped pane, from the player's own install.</summary>
    [Fact]
    public void The_shipped_pane_declares_all_nine_worn_slots_and_the_pack()
    {
        using var files = Install.Open();
        Assert.SkipUnless(files.TryRead("ui/inventoryPane.nui", out var data), "no ui/inventoryPane.nui");
        var pane = new InventoryPane(NuiPane.Parse(System.Text.Encoding.Latin1.GetString(data),
                                                   "inventoryPane.nui"));
        var grids = pane.Grids().ToList();
        Assert.Equal(10, grids.Count);

        // Every wearable bit of the mask is shown exactly once, and the pack is the tenth.
        int shown = 0;
        foreach (var g in grids) shown |= g.Slot;
        Assert.Equal(Slot.Worn | Slot.Inventory, shown);

        // And every one is a whole number of cells, on the real file rather than the excerpt.
        foreach (var g in grids)
        {
            var e = pane.Control(g.Element)!;
            Assert.Equal(e.Size[0], g.Across * InventoryPane.CellWidth);
            Assert.Equal(e.Size[1], g.Down * InventoryPane.CellHeight);
        }
    }

    /// <summary>
    /// A worn slot draws its item centred: half of what the 54 x 96 hand is bigger than
    /// the icon, each way, in whole pixels. A 2 x 3 item fills it exactly, as before.
    /// </summary>
    [Theory]
    [InlineData(1, 366, 316)]      // 1 x 2 dagger: (54-27)/2 = 13 in, (96-64)/2 = 16 up
    [InlineData(0, 366, 332)]      // 1 x 1 grenade: 13 in, 32 up
    [InlineData(4, 353, 316)]      // 2 x 2 buckler: 0 in, 16 up
    [InlineData(5, 353, 300)]      // 2 x 3 sword: the slot's own corner
    public void A_worn_item_smaller_than_its_slot_is_drawn_centred(int iconSize, int x, int y)
    {
        var a = new Actor();
        a.Inventory.Add(Thing("held", Slot.RightWeapon, iconSize));
        var pane = Pane(a);
        var placed = pane.Placed().Single();
        Assert.Equal((x, (float)y), InventoryPane.Corner(pane.Control("invRightWeapon")!, placed));
    }

    /// <summary>
    /// A worn slot is one cell: anywhere in the 2 x 3 hand answers with the dagger in
    /// it — the click and the hover both — where the footprint alone used to.
    /// </summary>
    [Fact]
    public void Anywhere_in_a_worn_hand_answers_with_its_item()
    {
        var a = new Actor();
        var dagger = Thing("dagger", Slot.RightWeapon, iconSize: 1);
        a.Inventory.Add(dagger);
        var pane = Pane(a);
        foreach (var (x, y) in new[] { (354, 301), (406, 301), (354, 395), (406, 395), (380, 348) })
        {
            var spot = pane.At(x, y);
            Assert.NotNull(spot);
            Assert.Equal((0, 0), (spot.Value.X, spot.Value.Y));
            Assert.Same(dagger, spot.Value.Item);
        }
        var hand = pane.Grids().Single(g => g.Slot == Slot.RightWeapon);
        Assert.Same(dagger, pane.ItemIn(hand, 1, 2));
        Assert.Same(dagger, pane.ItemAt(hand, 1, 2));
        // An empty slot is still a spot — the drop goes to its cell (0, 0).
        var empty = pane.At(560, 390);
        Assert.Equal((Slot.LeftWeapon, 0, 0, (Item?)null), (empty!.Value.Grid.Slot, empty.Value.X, empty.Value.Y, empty.Value.Item));
    }

    static Item Gloves(int slot = Slot.RightArm) => new()
    {
        Name = "leathergloves", Type = ItemType.Handwear, IconSize = 1, Icon = "leatherGlovesIcon",
        Flags = ItemFlags.TwoHanded, Location = slot,
    };

    /// <summary>
    /// **A two-handed item is in both grids of its pair**: gloves worn on one
    /// arm are drawn, described and clicked on the other arm too — play showed one glove on Wilhem
    /// before. A bracelet, which has no <c>0x8000</c>, stays on its own arm.
    /// </summary>
    [Fact]
    public void Gloves_on_one_arm_are_in_both_arm_grids()
    {
        var a = new Actor();
        var gloves = Gloves();
        a.Inventory.Add(gloves);
        var pane = Pane(a);
        var shown = pane.Placed().Where(p => p.Item == gloves).Select(p => p.Grid.Slot).Order().ToList();
        Assert.Equal([Slot.RightArm, Slot.LeftArm], shown);
        Assert.Same(gloves, pane.At(514 + 5, 320 + 5)!.Value.Item);            // invLeftArm
        Assert.Same(gloves, pane.ItemIn(pane.GridOf(Slot.LeftArm)!.Value, 0, 0));
        Assert.Equal(Slot.RightArm, gloves.Location);                           // one item, recorded once

        var b = new Actor();
        b.Inventory.Add(new Item { Name = "silverbracelet", Type = ItemType.Handwear, IconSize = 1, Location = Slot.LeftArm });
        Assert.Equal([Slot.LeftArm], Pane(b).Placed().Select(p => p.Grid.Slot).ToList());
    }

    /// <summary>
    /// The weapon hands are linked the same way, and 95 shipped weapons carry
    /// <c>0x8000</c> — the great swords, the bows, the long guns — so a two-handed weapon shows in the
    /// off hand as well; a one-handed one does not, and an off hand's own item keeps its grid (the
    /// placing fills the partner only when it is empty).
    /// </summary>
    [Fact]
    public void A_two_handed_weapon_is_in_both_weapon_grids_and_a_one_handed_one_is_not()
    {
        var a = new Actor();
        var great = new Item { Name = "GreatSword", Type = ItemType.Weapon, IconSize = 5, Flags = ItemFlags.TwoHanded, Location = Slot.RightWeapon };
        a.Inventory.Add(great);
        Assert.Equal(2, Pane(a).Placed().Count(p => p.Item == great));

        var b = new Actor();
        var sword = new Item { Name = "broadsword", Type = ItemType.Weapon, IconSize = 5, Location = Slot.RightWeapon };
        var shield = new Item { Name = "buckler", Type = ItemType.Shield, IconSize = 4, Location = Slot.LeftWeapon };
        b.Inventory.Add(sword);
        b.Inventory.Add(shield);
        var placed = Pane(b).Placed().ToList();
        Assert.Single(placed, p => p.Item == sword);
        Assert.Same(shield, placed.Single(p => p.Grid.Slot == Slot.LeftWeapon).Item);
    }

    /// <summary>
    /// **The hover picture** (for a weargrid and for a pack icon): what is under the
    /// pointer draws its red-outlined `_R`, and a pair's two halves outline together whichever of
    /// them the pointer is on. Nothing is outlined with the pointer elsewhere or off the pane.
    /// </summary>
    [Fact]
    public void Hovering_either_half_of_a_pair_outlines_both_and_nothing_else()
    {
        var a = new Actor();
        var gloves = Gloves();
        var ring = Thing("ringman", Slot.RightHand);
        var vial = Thing("vial", Slot.Inventory, x: 0, y: 0);
        var other = Thing("vial", Slot.Inventory, x: 3, y: 0);
        a.Inventory.Add(gloves);
        a.Inventory.Add(ring);
        a.Inventory.Add(vial);
        a.Inventory.Add(other);
        var pane = Pane(a);
        string[] Lit() => pane.Placed().Where(pane.Outlined).Select(p => $"{p.Grid.Element}:{p.Item.Name}@{p.X}").Order().ToArray();

        Assert.Empty(Lit());                                                   // no pointer
        pane.Pointer = (514 + 5, 320 + 40);                                    // the left glove
        Assert.Equal(["invLeftArm:leathergloves@0", "invRightArm:leathergloves@0"], Lit());
        pane.Pointer = (421 + 5, 320 + 5);                                     // the right glove
        Assert.Equal(["invLeftArm:leathergloves@0", "invRightArm:leathergloves@0"], Lit());
        pane.Pointer = (421 + 5, 283 + 5);                                     // the ring
        Assert.Equal(["invRightHand:ringman@0"], Lit());
        pane.Pointer = (346 + 5, 145 + 127);                                   // the pack's top-left cell
        Assert.Equal(["invInventory:vial@0"], Lit());
        pane.Pointer = (10, 10);
        Assert.Empty(Lit());
    }
}
