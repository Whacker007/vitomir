// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Moving items with the mouse. The *rules* under this are the engine's and were read; the
/// *gesture* is ours, and these tests pin it so a change to it is deliberate.
/// </summary>
public class InventoryHandsTests
{
    const string Nui = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        element weargrid name=invRightWeapon pos=353,300 size=54,96 gridsize=2,3
        element weargrid name=invLeftWeapon pos=555,300 size=54,96 gridsize=2,3
        element weargrid name=invBody pos=454,308 size=54,96 gridsize=2,3
        element weargrid name=invHead pos=454,410 size=54,64 gridsize=2,2
        element weargrid name=invRightHand pos=421,283 size=27,32 gridsize=1,1
        element weargrid name=invRightArm pos=421,320 size=27,64 gridsize=1,2
        element weargrid name=invLeftArm pos=514,320 size=27,64 gridsize=1,2
        """;

    /// <summary>
    /// **The quick-use belt is an ordinary icon grid.** The status bar gives `sbQuickUse` grid
    /// id `0x200` and registers the grid's drop handler on binding 0 — the same handler every inventory grid
    /// gets — so a left click on it lifts and drops just like the pack. An earlier build had wired a left
    /// click to *use*, which invented a gesture the original does not have and lost the one it
    /// does; this is the same mistake the right-click was, two lines further down the same file.
    /// </summary>
    [Fact]
    public void A_left_click_on_the_belt_lifts_and_drops_like_any_other_grid()
    {
        var (_, hands, thing) = Set();
        var belt = new InventoryGrid("sbQuickuse", Slot.QuickUse, 5, 1);
        var vial = new Item
        {
            Name = "blood vial", Type = ItemType.General, IconSize = 0, Icon = "vialIcon",
            Location = Slot.QuickUse, Cell = (2, 0),
        };
        thing.Actor!.Inventory.Add(vial);

        var up = hands.ClickIn(belt, 2, 0);
        Assert.Equal(HandAction.Lifted, up.What);
        Assert.Same(vial, hands.Carrying);
        Assert.Equal(Slot.None, vial.Location);

        var down = hands.ClickIn(belt, 4, 0);
        Assert.Equal(HandAction.Dropped, down.What);
        Assert.Equal(Slot.QuickUse, vial.Location);
        Assert.Equal((4, 0), vial.Cell);
        Assert.Null(hands.Carrying);
    }

    /// <summary>
    /// **An item put down on a taken belt cell swaps with the one there**, as on every item grid
    /// (the drop counts the overlaps: one covered is a swap). In play:
    /// a Bottle of Vitae dropped on the Rat went on top of it — six items on a five-cell belt — because
    /// the overlap was asked of the grids the inventory pane draws, and the belt is drawn on the status
    /// bar. The pack's own swap is pinned beside it.
    /// </summary>
    [Fact]
    public void An_item_dropped_on_a_taken_belt_cell_swaps_with_the_one_there()
    {
        var (_, hands, thing) = Set();
        var actor = thing.Actor!;
        var belt = new InventoryGrid("sbQuickuse", Slot.QuickUse, 5, 1);
        var rat = new Item { Name = "Rat", Type = ItemType.General, IconSize = 0, Icon = "ratIcon", Location = Slot.QuickUse, Cell = (0, 0) };
        var vitae = new Item { Name = "Bottle of Vitae", Type = ItemType.General, IconSize = 0, Icon = "vitaeIcon", Location = Slot.QuickUse, Cell = (3, 0) };
        actor.Inventory.Add(rat);
        actor.Inventory.Add(vitae);

        Assert.Equal(HandAction.Lifted, hands.ClickIn(belt, 3, 0).What);
        var down = hands.ClickIn(belt, 0, 0);
        Assert.Equal(HandAction.Swapped, down.What);
        Assert.Same(rat, hands.Carrying);
        Assert.Equal((Slot.QuickUse, (0, 0)), (vitae.Location, vitae.Cell));
        Assert.DoesNotContain(rat, actor.Inventory.Items);
        Assert.Single(actor.Inventory.Items, i => i.Location == Slot.QuickUse && i.Cell == (0, 0));
    }

    /// <summary>The belt takes single cells only, so a 2 x 3 sword is refused rather than squeezed in.</summary>
    [Fact]
    public void The_belt_will_not_take_something_bigger_than_a_cell()
    {
        var (_, hands, thing) = Set();
        var belt = new InventoryGrid("sbQuickuse", Slot.QuickUse, 5, 1);
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);

        hands.Click(346 + 5, 145 + 127 - 5);
        Assert.Same(sword, hands.Carrying);
        Assert.Equal(HandAction.Refused, hands.ClickIn(belt, 0, 0).What);
        Assert.Same(sword, hands.Carrying);
    }

    /// <summary>
    /// **The hand is outside the inventory list, and what goes down has to go back into it.**
    /// Putting an item in the hand removes it from its owner before it writes the hand link, so an item on the
    /// cursor is in nobody's list — and every put-down therefore has to add it back.
    ///
    /// Play found the half that was missing, in the one gesture that exercised it: alt-click a
    /// letter off the floor, drop it in the quick-use belt, and **it vanished**. The take had
    /// taken it out of the list and the pane's put-down never put it back, so the strip drew an
    /// inventory the letter was no longer in. The last assertion here is that strip.
    /// </summary>
    [Fact]
    public void What_leaves_the_hand_goes_back_into_the_inventory()
    {
        var (pane, hands, thing) = Set();
        var actor = thing.Actor!;
        var belt = new InventoryGrid("sbQuickuse", Slot.QuickUse, 5, 1);
        var vial = new Item
        {
            Name = "blood vial", Type = ItemType.General, IconSize = 0, Icon = "vialIcon",
            Location = Slot.Inventory, Cell = (0, 0),
        };
        actor.Inventory.Add(vial);

        // Up: out of the list entirely, which is the half that was already right.
        Assert.Equal(HandAction.Lifted, hands.Click(346 + 1, 145 + 127).What);
        Assert.Same(vial, hands.Carrying);
        Assert.DoesNotContain(vial, actor.Inventory.Items);

        // Down: back into it, or nothing will ever draw it again.
        Assert.Equal(HandAction.Dropped, hands.ClickIn(belt, 0, 0).What);
        Assert.Null(hands.Carrying);
        Assert.Contains(vial, actor.Inventory.Items);
        Assert.Equal(Slot.QuickUse, vial.Location);
        Assert.Equal((0, 0), vial.Cell);
        Assert.Same(vial, pane.ItemIn(belt, 0, 0));
    }

    /// <summary>The same invariant on the other three ways out of the hand: back, worn, swapped.</summary>
    [Fact]
    public void Putting_back_wearing_and_swapping_all_return_it_to_the_list()
    {
        var (_, hands, thing) = Set();
        var actor = thing.Actor!;

        var sword = Sword();
        actor.Inventory.Add(sword);
        hands.Click(346 + 1, 145 + 127);                       // lift the sword out of the pack
        Assert.DoesNotContain(sword, actor.Inventory.Items);
        Assert.Equal(HandAction.Returned, hands.PutBack().What);
        Assert.Contains(sword, actor.Inventory.Items);

        hands.Click(346 + 1, 145 + 127);                       // lift it again, and wear it
        Assert.Equal(HandAction.Equipped, hands.Click(353 + 1, 300 + 1).What);
        Assert.Contains(sword, actor.Inventory.Items);
        Assert.Null(hands.Carrying);

        // And a swap: the one coming off the body must leave the list as the other joins it.
        var second = new Item
        {
            Name = "rapier", Type = ItemType.Weapon, IconSize = 5, Icon = "swordBroadIcon",
            Flags = ItemFlags.Identified, Location = Slot.Inventory, Cell = (0, 0),
        };
        actor.Inventory.Add(second);
        hands.Click(346 + 1, 145 + 127);
        Assert.Same(second, hands.Carrying);
        Assert.Equal(HandAction.Swapped, hands.Click(353 + 1, 300 + 1).What);
        Assert.Same(sword, hands.Carrying);
        Assert.DoesNotContain(sword, actor.Inventory.Items);
        Assert.Contains(second, actor.Inventory.Items);
    }

    static (InventoryPane Pane, InventoryHands Hands, WorldThing Thing) Set()
    {
        Install.Open().Dispose();                  // skips the whole class without an install
        var actor = new Actor { Type = ActorType.Human, Name = "Christof" };
        actor.Health = actor.MaxHealth = 100;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
            Type = ThingType.Player, Actor = actor,
        };
        var pane = new InventoryPane(NuiPane.Parse(Nui, "inventoryPane.nui")) { Subject = actor };
        // The switches are passed rather than taken from the global, so this class does not depend
        // on what another test class has installed while it runs.
        return (pane, new InventoryHands(pane, thing, Effects()) { Rules = new Qol() }, thing);
    }

    /// <summary>The shipped effect table; these tests need an install for the same reason
    /// `EquipmentTests` does — equipping applies effects.</summary>
    static EffectDefinitions Effects() => new(Install.Open());

    static Item Sword() => new()
    {
        Name = "broadsword", Type = ItemType.Weapon, IconSize = 5, Icon = "swordBroadIcon",
        Flags = ItemFlags.Identified, Location = Slot.Inventory, Cell = (0, 0),
    };

    /// <summary>
    /// **`.nui` space counts up and cells count down**, so the top-left cell of the pack is at the
    /// grid's highest y. The hit test has to make the same flip the drawing does, and this is where
    /// a mismatch between them would show.
    /// </summary>
    [Fact]
    public void A_point_lands_in_the_cell_the_drawing_would_have_put_there()
    {
        var (pane, _, _) = Set();
        // invInventory is at 346,145, 10 x 4 cells of 27 x 32. The bottom-left pixel is row 3.
        var bottomLeft = pane.At(346, 145);
        Assert.NotNull(bottomLeft);
        Assert.Equal((0, 3), (bottomLeft!.Value.X, bottomLeft.Value.Y));

        // The top-left cell is the highest y inside the grid: 145 + 4*32 - 1.
        var topLeft = pane.At(346, 145 + 127);
        Assert.Equal((0, 0), (topLeft!.Value.X, topLeft.Value.Y));

        // And one pixel past the right edge is nothing at all.
        Assert.Null(pane.At(346 + 270, 145 + 64));
    }

    [Fact]
    public void Clicking_the_body_of_an_item_picks_up_that_item_not_the_empty_cell_under_it()
    {
        var (pane, hands, thing) = Set();
        var sword = Sword();                       // 2 x 3 at cell (0,0) of the pack
        thing.Actor!.Inventory.Add(sword);

        // A point two rows down and one column across is still inside the sword's footprint.
        var spot = pane.At(346 + 27, 145 + 127 - 64);
        Assert.Equal(sword, spot!.Value.Item);

        var lifted = hands.Click(346 + 27, 145 + 127 - 64);
        Assert.Equal(HandAction.Lifted, lifted.What);
        Assert.Equal(sword, hands.Carrying);
        Assert.Equal(Slot.None, sword.Location);   // off the board while carried
    }

    /// <summary>
    /// **Where its picture is, and not off the grid** (<c>DropCellTests</c> has the
    /// edges). Earlier, Vitomir took the cell under the pointer as the top-left and pulled a footprint
    /// that hung off the grid back onto it; 1.1 refuses it and the item stays in hand.
    /// </summary>
    [Fact]
    public void An_item_dropped_in_a_container_lands_where_it_is_drawn_and_not_off_the_grid()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        hands.Click(346, 145 + 127);               // lift from the top-left

        // At the far right of the top row, the 2 x 3 is drawn a cell and a half off the grid: refused.
        Assert.Equal(HandAction.Refused, hands.Click(346 + 269, 145 + 127).What);
        Assert.Equal(sword, hands.Carrying);

        // Held by its middle over the last two columns and the top three rows, it goes there.
        var put = hands.Click(346 + 8 * 27 + 27, 145 + 127 - 48);
        Assert.Equal(HandAction.Dropped, put.What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal((8, 0), sword.Cell);          // 10 wide, 2 across: the last legal column is 8
        Assert.Null(hands.Carrying);
    }

    [Fact]
    public void A_weapon_goes_on_the_weapon_slot_and_nothing_else_does()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        hands.Click(346, 145 + 127);

        // The head slot refuses it — a weapon is not headgear — and the item stays in hand.
        var no = hands.Click(454 + 10, 410 + 10);
        Assert.Equal(HandAction.Refused, no.What);
        Assert.Equal(sword, hands.Carrying);

        var yes = hands.Click(353 + 10, 300 + 10);
        Assert.Equal(HandAction.Equipped, yes.What);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Null(hands.Carrying);
    }

    [Fact]
    public void A_refused_drop_can_always_be_put_back_where_it_came_from()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        sword.Cell = (4, 1);
        thing.Actor!.Inventory.Add(sword);

        hands.Click(346 + 4 * 27 + 5, 145 + 127 - 32);   // lift from (4,1)
        Assert.Equal(sword, hands.Carrying);
        var back = hands.PutBack();
        Assert.Equal(HandAction.Returned, back.What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal((4, 1), sword.Cell);
    }

    /// <summary>Right-click is the shortcut: on, then off again, with no carrying in between.</summary>
    /// <summary>
    /// **Right-click uses the item.** The letter is read by a right click, and
    /// a right click on a usable item uses it (scrolls, potions and the like): the original's own
    /// gesture, which an earlier build had unknowingly replaced with an equip shortcut.
    /// </summary>
    [Fact]
    public void Right_click_uses_what_is_under_it()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var used = new List<Item>();
        hands.Use = item => { used.Add(item); return $"used {item.Name}"; };

        var got = hands.RightClick(346 + 5, 145 + 127 - 5);
        Assert.Equal(HandAction.Used, got.What);
        Assert.Same(sword, Assert.Single(used));
        // It stays where it was: using something does not move it.
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Null(hands.Carrying);
    }

    /// <summary>
    /// **While a shop is up a right click on an item does nothing** (the right click tests the shop's flag
    /// first): it is not used, and with <c>quickequip</c> off the shortcut does not equip it either
    /// (with it on, see <see cref="Shift_right_click_equips_and_unequips_while_shopping_and_a_plain_one_still_sells"/>).
    /// </summary>
    [Fact]
    public void Right_click_does_nothing_while_shopping()
    {
        var (_, hands, thing) = Set();
        hands.Rules = new Qol { QuickEquip = false };
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var used = new List<Item>();
        hands.Use = item => { used.Add(item); return $"used {item.Name}"; };
        hands.Shop = _ => true;

        Assert.Equal(HandAction.Nothing, hands.RightClick(346 + 5, 145 + 127 - 5).What);
        Assert.Equal(HandAction.Nothing, hands.RightClick(346 + 5, 145 + 127 - 5, InputModifier.Shift).What);
        Assert.Empty(used);
        Assert.Equal(Slot.Inventory, sword.Location);

        hands.Shop = null;                                           // the shop gone, it uses again
        Assert.Equal(HandAction.Used, hands.RightClick(346 + 5, 145 + 127 - 5).What);
    }

    /// <summary>The shortcut kept its behaviour and took a modifier, because right-click is spoken for.</summary>
    [Fact]
    public void Shift_right_click_equips_from_the_pack_and_unequips_back_into_it()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);

        var on = hands.RightClick(346 + 5, 145 + 127 - 5, InputModifier.Shift);
        Assert.Equal(HandAction.Equipped, on.What);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Null(hands.Carrying);

        var off = hands.RightClick(353 + 10, 300 + 10, InputModifier.Shift);
        Assert.Equal(HandAction.Unequipped, off.What);
        Assert.Equal(Slot.Inventory, sword.Location);
    }

    /// <summary>With the switch off the shortcut does nothing, and right-click still uses.</summary>
    [Fact]
    public void The_quickequip_switch_turns_only_the_shortcut_off()
    {
        var (_, hands, thing) = Set();
        hands.Rules = new Qol { QuickEquip = false };
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        hands.Use = _ => "used";

        Assert.Equal(HandAction.Nothing, hands.RightClick(346 + 5, 145 + 127 - 5, InputModifier.Shift).What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal(HandAction.Used, hands.RightClick(346 + 5, 145 + 127 - 5).What);
    }

    /// <summary>
    /// **The weargrid link**, as the original's pane and its drop handler have it: a
    /// one-handed sword dropped on the off-hand lands in the sword hand. So it is in the
    /// base game; the two weapon slots and the two arm slots are the only linked pairs.
    /// </summary>
    [Fact]
    public void A_weapon_dropped_on_the_off_hand_moves_itself_to_the_sword_hand()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        Assert.Equal(Slot.RightWeapon, sword.Slots & Slot.Worn);

        hands.Click(346 + 5, 145 + 127 - 5);              // lift it
        Assert.Same(sword, hands.Carrying);

        // Drop it on invLeftWeapon at 555,300 — a slot a right-hand sword may not occupy.
        var got = hands.Click(555 + 10, 300 + 10);
        Assert.Equal(HandAction.Equipped, got.What);
        Assert.Equal(Slot.RightWeapon, got.Slot);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Contains("moved from", got.Say);
        Assert.Null(hands.Carrying);

        Assert.Equal(Slot.RightWeapon, InventoryPane.LinkOf(Slot.LeftWeapon));
        Assert.Equal(Slot.LeftWeapon, InventoryPane.LinkOf(Slot.RightWeapon));
        // Nothing else is linked: a ring on the wrong hand is a refusal, not a move.
        Assert.Equal(Slot.None, InventoryPane.LinkOf(Slot.RightHand));
        Assert.Equal(Slot.None, InventoryPane.LinkOf(Slot.Head));
        Assert.Equal(Slot.None, InventoryPane.LinkOf(Slot.Body));
    }

    /// <summary>
    /// **The other half of the link, and the one that was wrong.** A drop that lands in the slot it
    /// was aimed at says nothing about moving. The redirect test alone passed while the report said
    /// "moved from" on every equip, because `ReferenceEquals` on a record struct is always false.
    /// </summary>
    [Fact]
    public void A_weapon_dropped_on_its_own_slot_is_not_reported_as_moved()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);

        hands.Click(346 + 5, 145 + 127 - 5);
        var got = hands.Click(353 + 10, 300 + 10);          // invRightWeapon, where it belongs
        Assert.Equal(HandAction.Equipped, got.What);
        Assert.Equal(Slot.RightWeapon, got.Slot);
        Assert.DoesNotContain("moved from", got.Say);
    }

    [Fact]
    public void A_click_away_from_every_grid_does_nothing_at_all()
    {
        var (_, hands, _) = Set();
        Assert.Equal(HandAction.Nothing, hands.Click(10, 10).What);
        Assert.Null(hands.Carrying);
    }

    /// <summary>
    /// The hand is the actor's, not a field on the pane — which is why an item in hand survives a
    /// level change in the base game, and why a save has somewhere to put it.
    /// </summary>
    [Fact]
    public void What_is_carried_lives_on_the_actor()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);

        hands.Click(346 + 5, 145 + 127 - 5);
        Assert.Same(sword, hands.Carrying);
        Assert.Same(sword, thing.Actor.Held);

        hands.PutBack();
        Assert.Null(thing.Actor.Held);
    }

    /// <summary>
    /// The drop counts the overlaps and switches on the count: **one** is the swap,
    /// and the displaced item ends up on the cursor (right after the removal allows it).
    /// </summary>
    [Fact]
    public void Dropping_onto_one_item_swaps_it_onto_the_cursor()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        var knife = new Item
        {
            Name = "knife", Type = ItemType.Weapon, IconSize = 0, Icon = "daggerIcon",
            Location = Slot.Inventory, Cell = (4, 0),
        };
        thing.Actor!.Inventory.Add(sword);
        thing.Actor.Inventory.Add(knife);

        hands.Click(346 + 5, 145 + 127 - 5);                 // lift the sword from cell 0,0
        Assert.Same(sword, hands.Carrying);

        var swap = hands.Click(346 + 4 * 27 + 27, 145 + 127 - 48);  // drop it onto the knife, drawn over cells 4-5, rows 0-2 (DropCell)
        Assert.Equal(HandAction.Swapped, swap.What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal((4, 0), sword.Cell);
        Assert.Same(knife, hands.Carrying);
        Assert.Equal(Slot.None, knife.Location);
    }

    /// <summary>Two or more overlaps is the refusal, and the item stays in hand.</summary>
    [Fact]
    public void Dropping_across_two_items_is_refused_and_flashes()
    {
        var (_, hands, thing) = Set();
        var cursor = new CursorState();
        hands.Cursor = cursor;
        hands.NowMs = 1_000;

        var sword = Sword();                                  // 2 x 3
        thing.Actor!.Inventory.Add(sword);
        foreach (var (x, name) in new[] { (4, "ringA"), (5, "ringB") })
            thing.Actor.Inventory.Add(new Item
            {
                Name = name, Type = ItemType.Rings, IconSize = 0, Icon = "ringIcon",
                Location = Slot.Inventory, Cell = (x, 0),
            });

        hands.Click(346 + 5, 145 + 127 - 5);
        var no = hands.Click(346 + 4 * 27 + 5, 145 + 127 - 5);
        Assert.Equal(HandAction.Refused, no.What);
        Assert.Same(sword, hands.Carrying);
        Assert.Equal(CursorId.Negative, cursor.Current);
        Assert.Equal(1_000 + Cursors.RefuseMs, cursor.RefusedUntil);
    }

    /// <summary>
    /// The icon click's own first test: a character who is mid-action or restrained cannot move items,
    /// and the click is answered with the negative cursor rather than ignored. **The dead pass**:
    /// the gate tests the dead bit again on its own and skips the refusal for it — the
    /// A comparison with 1.1; this test had pinned Vitomir's refusal of the dead.
    /// </summary>
    [Fact]
    public void A_busy_or_restrained_character_cannot_move_anything_but_the_dead_can()
    {
        var (_, hands, thing) = Set();
        var cursor = new CursorState();
        hands.Cursor = cursor;
        thing.Actor!.Inventory.Add(Sword());

        thing.Actor.BusyUntil = 5_000;
        hands.NowMs = 1_000;
        Assert.Equal(HandAction.Refused, hands.Click(346 + 5, 145 + 127 - 5).What);
        Assert.Null(hands.Carrying);
        Assert.Equal(1, cursor.Refusals);

        thing.Actor.BusyUntil = 0;
        thing.Actor.Flags2 |= Actor.Flag2.Restrained;
        Assert.Equal(HandAction.Refused, hands.Click(346 + 5, 145 + 127 - 5).What);
        Assert.Null(hands.Carrying);
        thing.Actor.Flags2 &= ~Actor.Flag2.Restrained;

        thing.Actor.BusyUntil = 5_000;
        thing.Actor.Flags |= Actor.Flag.Dead;
        Assert.Equal(HandAction.Lifted, hands.Click(346 + 5, 145 + 127 - 5).What);
    }

    /// <summary>
    /// **Only an icon's click is gated.** A click on an empty cell is the grid's own, the drop
    /// The drop has no busy test: a character mid-swing can still put down what it carries.
    /// A click on an icon while carrying is the icon click, and that one is refused. (A comparison with 1.1.)
    /// </summary>
    [Fact]
    public void A_busy_character_can_put_what_it_carries_down_in_an_empty_cell()
    {
        var (_, hands, thing) = Set();
        var actor = thing.Actor!;
        var sword = Sword();
        var other = Sword();
        other.Cell = (6, 0);
        actor.Inventory.Add(sword);
        actor.Inventory.Add(other);
        Assert.Equal(HandAction.Lifted, hands.Click(346 + 5, 145 + 127 - 5).What);

        actor.BusyUntil = 5_000;
        hands.NowMs = 1_000;
        // On the other sword's icon: the icon click's gate.
        Assert.Equal(HandAction.Refused, hands.Click(346 + 6 * 27 + 5, 145 + 127 - 5).What);
        Assert.Same(sword, hands.Carrying);
        // In an empty cell: the grid's drop, ungated.
        Assert.Equal(HandAction.Dropped, hands.Click(346 + 3 * 27 + 27, 145 + 127 - 48).What);   // drawn over cells 3-4 (DropCell)
        Assert.Null(hands.Carrying);
        Assert.Equal((3, 0), sword.Cell);
    }

    /// <summary>
    /// **A right click uses the item whatever the character is doing**: the right click goes from the
    /// shop test and the two casts straight to the use with no busy test, so a character
    /// mid-swing drinks from its pack. Vitomir answered it with the red cross until a comparison with 1.1.
    /// The shift quick-equip, which is ours, keeps the gate.
    /// </summary>
    [Fact]
    public void A_busy_character_still_uses_an_item_from_its_pack()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        Item? used = null;
        hands.Use = item => { used = item; return "used"; };
        thing.Actor.BusyUntil = 5_000;
        hands.NowMs = 1_000;

        Assert.Equal(HandAction.Used, hands.RightClick(346 + 5, 145 + 127 - 5).What);
        Assert.Same(sword, used);

        Assert.Equal(HandAction.Refused, hands.RightClick(346 + 5, 145 + 127 - 5, InputModifier.Shift).What);
    }
    /// <summary>
    /// **Lifting the sword off the hand under Feral Claws ends the claws**. The icon click takes the
    /// item out of its keeper's keeping first, and the removal's first step ends a
    /// weapon swap when the item is the keeper's weapon or the one the
    /// swap put aside; the take-off then finds no swap to refuse it and
    /// gives the fists, as in 1.1 (the weapon override cleared,
    /// the broadsword in the hand, the weapon from <c>claw01</c> to <c>punch</c>; the sword back on with the effect
    /// still running, and nothing changed when it ends). Vitomir left the claws on and the
    /// sword on the pointer, and the claws' end then put the sword back in the hand as well.
    /// </summary>
    [Fact]
    public void Lifting_the_sword_under_the_claws_ends_them_and_the_claws_end_changes_nothing()
    {
        var (_, hands, thing) = Set();
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var actor = thing.Actor!;
        actor.Weapon = actor.DefaultWeapon = Weapon.From("Punch", templates.Resolve("Punch"));
        var sword = Item.From("broadSword", templates.Resolve("broadSword"))!;
        sword.Location = Slot.Inventory;
        actor.Inventory.Add(sword);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, sword, Slot.RightWeapon, effects, 0));
        Assert.Same(sword.Weapon, actor.Weapon);

        // The effect starts: CodexActor.OverrideActorWeapon → the weapon swap.
        var claw = Weapon.From("Claw01", templates.Resolve("Claw01"))!;
        Assert.True(WeaponOverride.Override(thing, claw, clearOffhand: true, hideFists: true));
        Assert.Same(sword.Weapon, actor.PrevWeapon);

        // The lift: the swap off, the fists in hand, the sword on the pointer and no longer worn.
        var up = hands.Click(353 + 10, 300 + 10);
        Assert.Equal(HandAction.Lifted, up.What);
        Assert.Same(sword, hands.Carrying);
        Assert.False(WeaponOverride.Active(actor));
        Assert.Same(actor.DefaultWeapon, actor.Weapon);
        Assert.Null(actor.PrevWeapon);
        Assert.Equal(0, (int)sword.Flags & Equipment.EquippedBit);
        Assert.Equal(Slot.None, sword.Location);
        Assert.DoesNotContain(sword, actor.Inventory.Items);

        // Worn again while the effect still runs: nothing refuses it now (the use test asks the bit).
        Assert.Equal(HandAction.Equipped, hands.Click(353 + 10, 300 + 10).What);
        Assert.Null(hands.Carrying);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.NotEqual(0, (int)sword.Flags & Equipment.EquippedBit);
        Assert.Same(sword.Weapon, actor.Weapon);

        // The effect ends: CancelOverrideActorWeapon → the swap's end, with no swap to end.
        Assert.False(WeaponOverride.Cancel(thing));
        Assert.Same(sword.Weapon, actor.Weapon);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Null(actor.Held);
        Assert.Null(hands.Carrying);
        Assert.Single(actor.Inventory.Items, i => ReferenceEquals(i, sword));
    }

    /// <summary>
    /// **A lift the removal refuses is no lift at all**: the icon click tests its answer
    /// and returns with no red cross and nothing on the pointer. A character in another
    /// shape is one such refusal. Vitomir lifted it anyway until then.
    /// </summary>
    [Fact]
    public void A_lift_the_keeper_refuses_leaves_the_item_where_it_was()
    {
        var (_, hands, thing) = Set();
        var actor = thing.Actor!;
        var sword = Sword();
        actor.Inventory.Add(sword);
        actor.ShapeChange = new ShapeChange { Template = "wolf" };

        var got = hands.Click(346 + 5, 145 + 127 - 5);
        Assert.Equal(HandAction.Nothing, got.What);
        Assert.Null(hands.Carrying);
        Assert.Contains(sword, actor.Inventory.Items);
        Assert.Equal(Slot.Inventory, sword.Location);
    }

    static Item Handwear(string name, bool pair) => new()
    {
        Name = name, Type = ItemType.Handwear, IconSize = 1, Icon = "leatherGlovesIcon",
        Flags = ItemFlags.Identified | (pair ? ItemFlags.TwoHanded : 0), Location = Slot.Inventory, Cell = (9, 0),
    };

    /// <summary>Gloves put on the right arm, as Wilhem's were: in both arm grids.</summary>
    static Item WearGloves(WorldThing thing)
    {
        var gloves = Handwear("leathergloves", pair: true);
        thing.Actor!.Inventory.Add(gloves);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, gloves, Slot.RightArm, Effects(), 0));
        return gloves;
    }

    /// <summary>
    /// **A click on the other half of a pair lifts the pair** (a weargrid with an item clicks
    /// that item's icon), and putting it back puts it where it was worn.
    /// </summary>
    [Fact]
    public void A_click_on_the_left_glove_lifts_the_gloves_worn_on_the_right()
    {
        var (pane, hands, thing) = Set();
        var gloves = WearGloves(thing);
        var lifted = hands.Click(514 + 5, 320 + 5);
        Assert.Equal(HandAction.Lifted, lifted.What);
        Assert.Same(gloves, hands.Carrying);
        Assert.Empty(pane.Placed());
        Assert.Equal(HandAction.Returned, hands.PutBack().What);
        Assert.Equal(Slot.RightArm, gloves.Location);
    }

    /// <summary>
    /// **With something in hand, a click on either half drops on the half the gloves were put in**
    /// (the icon click finds the weargrid holding the item whose mask is the icon's own
    /// grid), so a bracelet clicked onto the left glove swaps onto the **right** arm; the overlap count
    /// there is one (a bracelet is not two-handed), so it is a swap and not a refusal.
    /// </summary>
    [Fact]
    public void A_bracelet_clicked_on_the_left_glove_swaps_onto_the_arm_the_gloves_were_worn_on()
    {
        var (_, hands, thing) = Set();
        var gloves = WearGloves(thing);
        var bracelet = Handwear("silverbracelet", pair: false);
        thing.Actor!.Inventory.Add(bracelet);
        Assert.Equal(HandAction.Lifted, hands.Click(346 + 9 * 27 + 5, 145 + 127).What);
        Assert.Same(bracelet, hands.Carrying);

        var swap = hands.Click(514 + 5, 320 + 5);
        Assert.Equal(HandAction.Swapped, swap.What);
        Assert.Same(gloves, hands.Carrying);
        Assert.Equal(Slot.RightArm, bracelet.Location);
    }

    /// <summary>
    /// Gauntlets onto gloves are one two-handed item onto another: the two-handed test answers 1 (both halves hold
    /// the same thing), so a swap, landing on the half the gloves were put in.
    /// </summary>
    [Fact]
    public void Gauntlets_clicked_on_either_half_of_worn_gloves_swap_with_them()
    {
        var (pane, hands, thing) = Set();
        var gloves = WearGloves(thing);
        var gauntlets = Handwear("gauntlets", pair: true);
        thing.Actor!.Inventory.Add(gauntlets);
        Assert.Equal(HandAction.Lifted, hands.Click(346 + 9 * 27 + 5, 145 + 127).What);

        Assert.Equal(HandAction.Swapped, hands.Click(514 + 5, 320 + 5).What);
        Assert.Same(gloves, hands.Carrying);
        Assert.Equal(Slot.RightArm, gauntlets.Location);
        Assert.Equal(2, pane.Placed().Count(p => p.Item == gauntlets));
    }

    // ---- While a shop is up (ours, under `quickequip`): a dragged press lifts, a still one offers ----

    // The pack's top-left cell, the sword hand, the head, and a point that puts a 2 x 3 icon at the pack's (4, 0).
    const int PackX = 346 + 5, PackY = 145 + 127 - 5, HandX = 353 + 10, HandY = 300 + 10;
    const int HeadX = 454 + 10, HeadY = 410 + 10, PackCell4X = 346 + 125, PackCell4Y = 145 + 127 - 40;

    /// <summary>A shop up, as the game's hook has it: what was offered for sale and what was sold are kept.</summary>
    static (List<Item> Offered, List<Item> Sold) Shopping(InventoryHands hands)
    {
        var offered = new List<Item>();
        var sold = new List<Item>();
        hands.Shop = item => { offered.Add(item); return true; };
        hands.QuickSell = item => { sold.Add(item); return true; };
        return (offered, sold);
    }

    /// <summary>A press at a pane point, dragged ten window pixels: the lift.</summary>
    static HandResult DragFrom(InventoryHands hands, int x, int y)
    {
        Assert.Equal(HandAction.Nothing, hands.Press(x, y, 100, 100).What);
        return hands.Drag(110, 100);
    }

    [Fact]
    public void While_shopping_a_drag_from_the_pack_to_the_body_puts_it_on()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var (offered, _) = Shopping(hands);

        Assert.Equal(HandAction.Nothing, hands.Press(PackX, PackY, 100, 100).What);
        Assert.Equal(HandAction.Nothing, hands.Drag(103, 102).What);        // under five pixels: not yet
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal(HandAction.Lifted, hands.Drag(104, 103).What);        // five: the lift
        Assert.Same(sword, hands.Carrying);

        var on = hands.Release(HandX, HandY);
        Assert.Equal(HandAction.Equipped, on.What);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Same(sword, Equipment.InSlot(thing.Actor, Slot.RightWeapon));
        Assert.Null(hands.Carrying);
        Assert.Empty(offered);                                              // a drag offers nothing
        Assert.False(hands.ShopHeld);
    }

    [Fact]
    public void While_shopping_a_drag_from_the_body_to_the_pack_takes_it_off()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        Assert.Equal(HandAction.Equipped, hands.RightClick(PackX, PackY, InputModifier.Shift).What);
        Shopping(hands);

        Assert.Equal(HandAction.Lifted, DragFrom(hands, HandX, HandY).What);
        var off = hands.Release(PackCell4X, PackCell4Y);
        Assert.Equal(HandAction.Dropped, off.What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal((4, 0), sword.Cell);
        Assert.Null(Equipment.InSlot(thing.Actor!, Slot.RightWeapon));
    }

    [Fact]
    public void While_shopping_a_press_let_go_without_moving_still_offers_it_for_sale()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var (offered, _) = Shopping(hands);

        Assert.Equal(HandAction.Nothing, hands.Press(PackX, PackY, 100, 100).What);
        Assert.Empty(offered);                                              // the offer waits for the release
        Assert.Equal(HandAction.Nothing, hands.Drag(102, 103).What);
        Assert.Equal(HandAction.Offered, hands.Release(PackX, PackY).What);
        Assert.Same(sword, Assert.Single(offered));
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Null(hands.Carrying);
    }

    [Fact]
    public void While_shopping_a_refused_drop_goes_back_where_it_came_from()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        sword.Cell = (4, 0);
        thing.Actor!.Inventory.Add(sword);
        Shopping(hands);

        // From the pack onto the head: the slot mask refuses, and it is back in its cell.
        Assert.Equal(HandAction.Lifted, DragFrom(hands, PackCell4X, PackCell4Y).What);
        Assert.Equal(HandAction.Refused, hands.Release(HeadX, HeadY).What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Equal((4, 0), sword.Cell);
        Assert.Null(hands.Carrying);

        // From the body onto the head: back on the hand, worn again.
        hands.Shop = null;
        Assert.Equal(HandAction.Equipped, hands.RightClick(PackCell4X, PackCell4Y, InputModifier.Shift).What);
        Shopping(hands);
        Assert.Equal(HandAction.Lifted, DragFrom(hands, HandX, HandY).What);
        Assert.Null(Equipment.InSlot(thing.Actor, Slot.RightWeapon));      // the lift took it off
        Assert.Equal(HandAction.Refused, hands.Release(HeadX, HeadY).What);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Same(sword, Equipment.InSlot(thing.Actor, Slot.RightWeapon));
        Assert.Null(hands.Carrying);
    }

    [Fact]
    public void While_shopping_a_drop_on_the_shelf_or_off_the_grids_goes_back()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var (offered, sold) = Shopping(hands);

        Assert.Equal(HandAction.Lifted, DragFrom(hands, PackX, PackY).What);
        Assert.Equal(HandAction.Returned, hands.ReleaseOff().What);         // the shelf: no sale by drag
        Assert.Equal((Slot.Inventory, (0, 0)), (sword.Location, sword.Cell));
        Assert.Null(hands.Carrying);

        Assert.Equal(HandAction.Lifted, DragFrom(hands, PackX, PackY).What);
        Assert.Equal(HandAction.Returned, hands.Release(10, 10).What);      // the pane's art, between its grids
        Assert.Equal((Slot.Inventory, (0, 0)), (sword.Location, sword.Cell));
        Assert.Empty(offered);
        Assert.Empty(sold);
    }

    [Fact]
    public void While_shopping_a_sword_dragged_onto_the_one_worn_sends_that_one_to_where_it_came_from()
    {
        var (_, hands, thing) = Set();
        var old = Sword();
        thing.Actor!.Inventory.Add(old);
        Assert.Equal(HandAction.Equipped, hands.RightClick(PackX, PackY, InputModifier.Shift).What);
        var bought = Sword();
        bought.Cell = (4, 0);
        thing.Actor.Inventory.Add(bought);
        Shopping(hands);

        Assert.Equal(HandAction.Lifted, DragFrom(hands, PackCell4X, PackCell4Y).What);
        Assert.Equal(HandAction.Swapped, hands.Release(HandX, HandY).What);
        Assert.Same(bought, Equipment.InSlot(thing.Actor, Slot.RightWeapon));
        Assert.Equal((Slot.Inventory, (4, 0)), (old.Location, old.Cell));
        Assert.Null(hands.Carrying);
    }

    [Fact]
    public void Shift_right_click_equips_and_unequips_while_shopping_and_a_plain_one_still_sells()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var (_, sold) = Shopping(hands);

        Assert.Equal(HandAction.Equipped, hands.RightClick(PackX, PackY, InputModifier.Shift).What);
        Assert.Equal(Slot.RightWeapon, sword.Location);
        Assert.Equal(HandAction.Unequipped, hands.RightClick(HandX, HandY, InputModifier.Shift).What);
        Assert.Equal(Slot.Inventory, sword.Location);
        Assert.Empty(sold);

        Assert.Equal(HandAction.Sold, hands.RightClick(PackX, PackY).What);
        Assert.Same(sword, Assert.Single(sold));
    }

    [Fact]
    public void With_quickequip_off_a_shop_moves_nothing_as_before()
    {
        var (_, hands, thing) = Set();
        hands.Rules = new Qol { QuickEquip = false };
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        var (offered, sold) = Shopping(hands);

        Assert.Equal(HandAction.Offered, hands.Press(PackX, PackY, 100, 100).What);   // the press is the click, at once
        Assert.Equal(HandAction.Nothing, hands.Drag(140, 100).What);
        Assert.Equal(HandAction.Nothing, hands.Release(HandX, HandY).What);
        Assert.Equal((Slot.Inventory, (0, 0)), (sword.Location, sword.Cell));
        Assert.Null(hands.Carrying);
        Assert.Same(sword, Assert.Single(offered));

        // Shift and a right click is a plain right click again: it sells.
        Assert.Equal(HandAction.Sold, hands.RightClick(PackX, PackY, InputModifier.Shift).What);
        Assert.Same(sword, Assert.Single(sold));
    }

    [Fact]
    public void The_shop_going_mid_drag_puts_the_item_back()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);
        Shopping(hands);

        Assert.Equal(HandAction.Lifted, DragFrom(hands, PackX, PackY).What);
        hands.EndShopDrag();
        Assert.Equal((Slot.Inventory, (0, 0)), (sword.Location, sword.Cell));
        Assert.Null(hands.Carrying);
        Assert.False(hands.ShopHeld);
    }

    [Fact]
    public void Outside_a_shop_a_press_is_the_click_it_always_was()
    {
        var (_, hands, thing) = Set();
        var sword = Sword();
        thing.Actor!.Inventory.Add(sword);

        Assert.Equal(HandAction.Lifted, hands.Press(PackX, PackY, 100, 100).What);
        Assert.False(hands.ShopHeld);
        Assert.Equal(HandAction.Equipped, hands.Press(HandX, HandY, 100, 100).What);
    }
}
