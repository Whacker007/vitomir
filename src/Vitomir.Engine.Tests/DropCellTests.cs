// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Which cells a carried item goes into** (<c>DropCell</c>). 1.1 puts it where
/// its picture is: the carried icon is drawn centred on the pointer (anchor 0) and the grid's cell
/// lookup takes the drawn top-left corner rounded to a cell — by truncating, so at the top and
/// left edges an item hanging off by more than half a cell is pulled in — and a footprint off the grid is
/// refused. The quality-of-life switch <c>dropcells</c> (ours, on) rounds to the nearest cell at
/// every edge. Pinned here on the pack's own 10 x 4 grid of 27 x 32 cells at 346,145 (its top row is at
/// y 272, `.nui` y counting up), with a 2 x 3 broadsword: the middle, each edge, both rules, and an item
/// whose picture lies partly over another.
/// </summary>
public class DropCellTests
{
    const string Nui = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        """;

    const int GridX = 346, TopRow = 145 + 127;

    static (InventoryHands Hands, WorldThing Thing) Set(bool dropCells = true)
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
        return (new InventoryHands(pane, thing, new EffectDefinitions(files)) { Rules = new Qol { DropCells = dropCells } }, thing);
    }

    static Item Sword() => new()
    {
        Name = "broadsword", Type = ItemType.Weapon, IconSize = 5, Icon = "swordBroadIcon",
        Flags = ItemFlags.Identified, Location = Slot.Inventory, Cell = (0, 0),
    };

    static Item Potion(int x, int y) => new()
    {
        Name = $"potion{x}{y}", Type = ItemType.General, IconSize = 0, Icon = "vialIcon",
        Location = Slot.Inventory, Cell = (x, y),
    };

    /// <summary>A sword on the pointer, lifted from the pack's top-left corner.</summary>
    static (InventoryHands Hands, Item Sword, WorldThing Thing) Holding(bool dropCells = true)
    {
        var (hands, thing) = Set(dropCells);
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        Assert.Equal(HandAction.Lifted, hands.Click(GridX + 1, TopRow).What);
        return (hands, sword, thing);
    }

    /// <summary>The pointer that draws the 2 x 3 sword with its top-left corner at (<paramref name="left"/>, <paramref name="top"/>) pixels of the grid.</summary>
    static (int X, int Y) Drawn(int left, int top) => (GridX + left + 27, TopRow - (top + 48));

    [Fact]
    public void A_sword_held_by_its_middle_goes_into_the_cells_it_is_drawn_over_not_the_ones_under_the_pointer()
    {
        var (hands, sword, _) = Holding();
        var (x, y) = Drawn(3 * 27, 1 * 32);            // drawn over columns 3-4, rows 1-3
        Assert.Equal((4, 2), ((x - GridX) / 27, (TopRow - y) / 32));   // the pointer's own cell, where it went until then
        Assert.Equal(HandAction.Dropped, hands.Click(x, y).What);
        Assert.Equal((3, 1), sword.Cell);
    }

    [Fact]
    public void At_the_right_and_bottom_edges_it_goes_in_while_it_hangs_off_by_less_than_half_a_cell_and_is_refused_after()
    {
        var (hands, sword, _) = Holding();
        Assert.Equal(HandAction.Refused, hands.Click(Drawn(232, 32).X, Drawn(232, 32).Y).What);   // 8.6 cells: columns 9-10
        Assert.Equal(sword, hands.Carrying);
        Assert.Equal(HandAction.Refused, hands.Click(Drawn(81, 51).X, Drawn(81, 51).Y).What);     // 1.6 rows: rows 2-4
        Assert.Equal(sword, hands.Carrying);
        var (x, y) = Drawn(227, 45);                     // 8.4 cells across and 1.4 rows down: the last cells
        Assert.Equal(HandAction.Dropped, hands.Click(x, y).What);
        Assert.Equal((8, 1), sword.Cell);
    }

    [Fact]
    public void At_the_top_and_left_edges_the_switch_rounds_to_the_nearest_cell_and_1_1_pulls_the_item_in()
    {
        // 0.7 of a cell off the left (and 0.7 of a row off the top): mostly off the grid.
        var left = Drawn(-19, 32);
        var top = Drawn(81, -22);

        var (on, sword, _) = Holding(dropCells: true);
        Assert.Equal(HandAction.Refused, on.Click(left.X, left.Y).What);
        Assert.Equal(HandAction.Refused, on.Click(top.X, top.Y).What);
        Assert.Equal(sword, on.Carrying);

        var (off, sword11, _) = Holding(dropCells: false);
        Assert.Equal(HandAction.Dropped, off.Click(left.X, left.Y).What);   // -0.2 truncates to 0
        Assert.Equal((0, 1), sword11.Cell);
        off.Click(GridX + 1, TopRow - 32 - 1);                               // lift it again
        Assert.Equal(sword11, off.Carrying);
        Assert.Equal(HandAction.Dropped, off.Click(top.X, top.Y).What);
        Assert.Equal((3, 0), sword11.Cell);

        // 0.4 of a cell off: the nearest cell is on the grid either way.
        var (again, sword2, _) = Holding(dropCells: true);
        var near = Drawn(-11, -13);
        Assert.Equal(HandAction.Dropped, again.Click(near.X, near.Y).What);
        Assert.Equal((0, 0), sword2.Cell);
    }

    [Fact]
    public void A_picture_partly_over_another_item_goes_into_the_free_cells_it_covers_most_and_swaps_or_refuses_as_1_1()
    {
        var (hands, sword, thing) = Holding();
        var potion = Potion(5, 1);
        thing.Actor!.Inventory.Add(potion);

        // 3.4 cells in: the picture reaches 0.4 of a cell over the potion's column, but columns 3-4 are free.
        var free = Drawn(92, 32);
        Assert.Equal(HandAction.Dropped, hands.Click(free.X, free.Y).What);
        Assert.Equal((3, 1), sword.Cell);
        Assert.Equal((5, 1), potion.Cell);

        // 3.6 cells in: columns 4-5, which hold the potion — one item under it is 1.1's swap.
        hands.Click(GridX + 3 * 27 + 1, TopRow - 32 - 1);
        var over = Drawn(97, 32);
        var swap = hands.Click(over.X, over.Y);
        Assert.Equal(HandAction.Swapped, swap.What);
        Assert.Equal((4, 1), sword.Cell);
        Assert.Equal(potion, hands.Carrying);

        // Two items under it is 1.1's refusal.
        var (two, sword2, owner) = Holding();
        owner.Actor!.Inventory.Add(Potion(5, 1));
        owner.Actor.Inventory.Add(Potion(5, 3));
        Assert.Equal(HandAction.Refused, two.Click(over.X, over.Y).What);
        Assert.Equal(sword2, two.Carrying);
    }

    [Fact]
    public void A_one_cell_item_goes_into_the_cell_under_the_pointer_by_either_rule()
    {
        foreach (bool rule in new[] { true, false })
        {
            var (hands, thing) = Set(rule);
            var potion = Potion(0, 0);
            thing.Actor!.Inventory.Add(potion);
            hands.Click(GridX + 1, TopRow);
            Assert.Equal(HandAction.Dropped, hands.Click(GridX + 5 * 27 + 26, TopRow - (2 * 32 + 31)).What);
            Assert.Equal((5, 2), potion.Cell);
        }
    }

    [Fact]
    public void One_axis_is_the_drawn_corner_plus_half_a_cell_over_a_cell_truncated_by_1_1_and_floored_by_the_switch()
    {
        // A 2-cell item, 27 a cell: the corner is the pointer less 27.
        Assert.Equal(3, DropCell.Axis(108, 54, 27, nearest: false));   // corner 81: exactly column 3
        Assert.Equal(3, DropCell.Axis(121, 54, 27, nearest: true));    // corner 94: 3.48, nearer 3
        Assert.Equal(4, DropCell.Axis(122, 54, 27, nearest: true));    // corner 95: 3.52, nearer 4
        Assert.Equal(0, DropCell.Axis(0, 54, 27, nearest: false));     // corner -27: -0.5 truncates to 0
        Assert.Equal(-1, DropCell.Axis(0, 54, 27, nearest: true));
    }
}
