// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Xunit;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The inventory's own "put it somewhere" — the search, the repack and
/// the cascade between them.
/// </summary>
public class StowTests
{
    [Fact]
    public void The_search_fills_row_by_row_from_the_top_left()
    {
        // The free-cell search steps x first and only then y, which is what puts the second 1 x 1 item beside
        // the first rather than under it.
        var (thing, actor) = Who();
        var a = Cell(); var b = Cell(); var c = Cell();
        foreach (var it in new[] { a, b, c }) { actor.Inventory.Add(it); Stow.Place(actor, it, Slot.Inventory); }
        Assert.Equal((0, 0), a.Cell);
        Assert.Equal((1, 0), b.Cell);
        Assert.Equal((2, 0), c.Cell);
        Assert.Equal(Slot.Inventory, a.Location);
        Assert.NotNull(thing);
    }

    [Fact]
    public void The_search_wraps_to_the_next_row_and_then_gives_up()
    {
        var (_, actor) = Who();
        // The pack is 10 x 4, so forty single cells fit and the forty-first does not.
        for (int i = 0; i < 40; i++)
        {
            var item = Cell();
            actor.Inventory.Add(item);
            Assert.True(Stow.Place(actor, item, Slot.Inventory), $"item {i} would not fit");
        }
        var over = Cell();
        actor.Inventory.Add(over);
        Assert.False(Stow.Place(actor, over, Slot.Inventory));
    }

    /// <summary>
    /// The repack empties the **pack only** and re-places largest first — height 3 down to 1 and,
    /// inside each, width 3 down to 1. The worn item and the belt are not touched.
    /// </summary>
    [Fact]
    public void The_repack_empties_the_pack_and_lays_the_big_things_down_first()
    {
        var (_, actor) = Who();
        var worn = Sword();
        worn.Location = Slot.RightWeapon;
        var belt = Cell(); belt.Location = Slot.QuickUse; belt.Cell = (3, 0);
        var small = Cell(); var big = Sword();      // 1 x 1 and 2 x 3
        actor.Inventory.Add(worn); actor.Inventory.Add(belt);
        actor.Inventory.Add(small); actor.Inventory.Add(big);

        // Deliberately the wrong way round: the small one takes the corner first.
        Stow.Place(actor, small, Slot.Inventory);
        Stow.Place(actor, big, Slot.Inventory);
        Assert.Equal((0, 0), small.Cell);
        Assert.Equal((1, 0), big.Cell);

        Stow.Repack(actor);

        // Largest first, so the sword takes the corner and the single cell goes beside it.
        Assert.Equal(Slot.Inventory, big.Location);
        Assert.Equal((0, 0), big.Cell);
        Assert.Equal(Slot.Inventory, small.Location);
        Assert.Equal((2, 0), small.Cell);
        // Untouched.
        Assert.Equal(Slot.RightWeapon, worn.Location);
        Assert.Equal(Slot.QuickUse, belt.Location);
        Assert.Equal((3, 0), belt.Cell);
    }

    [Fact]
    public void Money_and_quest_items_never_take_a_cell()
    {
        var (thing, _) = Who();
        var coins = Cell(); coins.Flags |= ItemFlags.InstantCash;
        Assert.Equal(StowResult.Cash, Stow.Put(thing, coins, steering: true));

        var quest = new Item { Name = "relic", Type = ItemType.Quest, IconSize = 0, Icon = "x" };
        Assert.Equal(StowResult.Quest, Stow.Put(thing, quest, steering: true));
    }

    /// <summary>
    /// The base game's behaviour, and the stow's non-steered branch, in the same order:
    /// **into the pack; rearranged if it has to be; then the hand; then refused.**
    ///
    /// Five single cells in row 1, at columns 1, 3, 5, 7 and 8, block every 2 x 3 window the pack
    /// has — a 2 x 3 needs three of the four rows, so anything in row 1 is inside all of them — and
    /// the repack sweeps all five into row 0, which opens the pack up again.
    /// </summary>
    [Fact]
    public void Giving_to_a_companion_rearranges_before_it_reaches_for_their_hand()
    {
        var (thing, actor) = Who();
        var blockers = Fragment(actor);
        var given = Sword();
        Assert.False(Stow.Place(actor, given, Slot.Inventory), "the pack was not actually blocked");
        given.Location = Slot.None;

        Assert.Equal(StowResult.Stowed, Stow.Put(thing, given, steering: false));
        Assert.Equal(Slot.Inventory, given.Location);
        Assert.Null(actor.Held);
        // The repack is what made room, and it swept all five blockers into the first row. Which
        // of them lands in which column follows the inventory list's order, and that list is a
        // **head insert** (each new item hangs off the front), so it is the reverse of the
        // order they went in — the set is the claim, not the sequence.
        Assert.Equal([0, 1, 2, 3, 4], blockers.Select(b => b.Cell.X).Order().ToArray());
        Assert.All(blockers, b => Assert.Equal(0, b.Cell.Y));
    }

    /// <summary>
    /// The one place the steered character differs, and it is visible on the same pack: the stow
    /// asks `pActor == GetCurrentControl()` **before** it repacks, so the character you are driving
    /// puts it on his cursor rather than tidying up.
    /// </summary>
    [Fact]
    public void The_character_you_steer_reaches_for_his_own_hand_first()
    {
        var (thing, actor) = Who();
        Fragment(actor);
        var given = Sword();
        Assert.Equal(StowResult.ToHand, Stow.Put(thing, given, steering: true));
        Assert.Same(given, actor.Held);
        Assert.Equal(Slot.None, given.Location);
    }

    [Fact]
    public void A_full_pack_and_a_full_hand_is_a_refusal()
    {
        var (thing, actor) = Who();
        for (int i = 0; i < 40; i++) { var it = Cell(); actor.Inventory.Add(it); Stow.Place(actor, it, Slot.Inventory); }
        for (int i = 0; i < 5; i++) { var it = Cell(); actor.Inventory.Add(it); Stow.Place(actor, it, Slot.QuickUse); }

        var first = Cell();
        Assert.Equal(StowResult.ToHand, Stow.Put(thing, first, steering: false));
        Assert.Same(first, actor.Held);

        var second = Cell();
        Assert.Equal(StowResult.Refused, Stow.Put(thing, second, steering: false));
        Assert.Same(first, actor.Held);
    }

    [Fact]
    public void The_reach_and_the_walk_threshold_are_the_engines_own_numbers()
    {
        Assert.Equal(50f, Stow.Reach);
        Assert.Equal(300f, Stow.WalkWithin);

        Assert.True(GroundOrders.Walks(InputModifier.None, 299f));
        Assert.False(GroundOrders.Walks(InputModifier.None, 301f));
        Assert.False(GroundOrders.Walks(InputModifier.Shift, 10f));
        Assert.True(GroundOrders.Immediate(InputModifier.Space));
        Assert.False(GroundOrders.Immediate(InputModifier.Alt));
    }

    /// <summary>
    /// **The space-drop's landing point, and the one thing Vitomir does not reproduce.**
    ///
    /// Within the reach the item goes exactly where you clicked. Beyond it, the drop order computes
    /// `delta * 50 + origin` using the *same* constant as the threshold and never normalises the
    /// vector — three multiplications by a constant and no division — so a click 100 units away
    /// throws the item 5,000 units, onto walkable ground often enough to lose it for good.
    ///
    /// That is a **repair** rather than a Fix (<see cref="Repairs.DropLanding"/>): always applied,
    /// no switch, because losing the player's property is not a behaviour worth recognising. This
    /// test is therefore the repaired arithmetic, and the original's is quoted in the comment above
    /// rather than being reachable in code.
    /// </summary>
    [Fact]
    public void A_space_drop_past_arms_reach_lands_at_the_reach()
    {
        var me = Vector3.Zero;
        var near = new Vector3(30, 0, 0);
        var far = new Vector3(100, 0, 0);

        Assert.Equal(near, GroundOrders.Landing(me, near));
        // The original would answer (5000, 0, 0) here.
        Assert.Equal(new Vector3(50, 0, 0), GroundOrders.Landing(me, far));
        Assert.Equal(Stow.Reach, Vector3.Distance(me, GroundOrders.Landing(me, far)), 3);

        // Direction is kept, which is the whole point of the missing division.
        var diagonal = new Vector3(300, 400, 0);                     // length 500
        var landed = GroundOrders.Landing(me, diagonal);
        Assert.Equal(30f, landed.X, 3);
        Assert.Equal(40f, landed.Y, 3);
        Assert.Equal(0f, landed.Z, 3);
    }

    /// <summary>
    /// **The belt takes one-cell icons only** (the free-cell search tests the icon's grid mask, and the strip
    /// puts the belt's 0x200 in it only for a 1 x 1 icon): a 2 x 1 item — `launch_grenades` is the
    /// one shipped — is refused by the 5 x 1 belt it would fit, and the cascade lays it in the pack.
    /// </summary>
    [Fact]
    public void The_belt_refuses_an_icon_bigger_than_one_cell()
    {
        var (thing, actor) = Who();
        var grenades = new Item { Name = "launch_grenades", Type = ItemType.Ammo, IconSize = 7, Icon = "x" };
        Assert.Equal((2, 1), grenades.IconCells);
        actor.Inventory.Add(grenades);
        Assert.False(Stow.Place(actor, grenades, Slot.QuickUse));
        actor.Inventory.Remove(grenades);

        Assert.Equal(StowResult.Stowed, Stow.Put(thing, grenades, steering: false));
        Assert.Equal(Slot.Inventory, grenades.Location);
    }

    /// <summary>
    /// **What is stowed is owned by whoever stowed it** — the list link writes
    /// the item's owner. An item handed over on a portrait left the giver's keeping with no owner and
    /// arrived in the new pack still with none, so a shop refused to buy it from its new keeper (in
    /// play, Wilhem's broadsword, buckler and rags given to Christof, and not one
    /// of them sold at Jiri's).
    /// </summary>
    [Fact]
    public void An_item_handed_over_is_owned_by_its_new_keeper()
    {
        var (giver, _) = Who();
        var (taker, takerActor) = Who();
        var item = Cell();
        var itemThing = new WorldThing { Index = 1, Guid = 7, Scene = new NsdThing(), Template = "broadsword", Type = ThingType.Weapon, Item = item };
        item.Thing = itemThing;
        Assert.True(Ground.Hand(giver, item));          // lifted by the giver: out of every keeping
        Ground.ClearHand(giver);                        // the portrait click empties the hand
        Assert.Null(itemThing.Owner);
        Assert.Equal(StowResult.Stowed, Stow.Put(taker, item, steering: false));
        Assert.Same(taker, itemThing.Owner);
        Assert.Contains(item, takerActor.Inventory.Items);
    }

    static (WorldThing, Actor) Who()
    {
        var actor = new Actor { Type = ActorType.Human, Name = "Christof" };
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
            Type = ThingType.Player, Actor = actor,
        };
        return (thing, actor);
    }

    /// <summary>
    /// A pack with no 2 x 3 hole left in it that a repack can open up again: five single cells in
    /// row 1, at the columns that between them sit inside every 2 x 3 window.
    /// </summary>
    static List<Item> Fragment(Actor actor)
    {
        var blockers = new List<Item>();
        foreach (int x in new[] { 1, 3, 5, 7, 8 })
        {
            var item = Cell();
            item.Location = Slot.Inventory;
            item.Cell = (x, 1);
            actor.Inventory.Add(item);
            blockers.Add(item);
        }
        return blockers;
    }

    static Item Cell() => new() { Name = "ring", Type = ItemType.Rings, IconSize = 0, Icon = "ringIcon" };

    static Item Sword() => new()
    {
        Name = "broadsword", Type = ItemType.Weapon, IconSize = 5, Icon = "swordBroadIcon",
    };
}
