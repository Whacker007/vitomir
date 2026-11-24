// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A drop on a worn weapon slot, read to the end** (a comparison with 1.1's first gap;
/// <see cref="WearDrop"/>). The weargrids' count sees a two-handed weapon in both weapon
/// grids, and the swap takes the displaced item off before the carried one is equipped —
/// so a torch dropped beside a two-hander, and a two-hander dropped
/// beside a torch, are **swaps** in 1.1, where Vitomir had refused both. "I can't wear that"
/// (`vox_cannotwear`) is kept for an equip that still refuses once the drop's own tests
/// have passed; a character who may not use the item at all is refused first, with no word.
/// Real templates, because the two-hand rule reads the weapon in the hand.
/// </summary>
public class WearDropTests
{
    const string Nui = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        element weargrid name=invRightWeapon pos=353,300 size=54,96 gridsize=2,3
        element weargrid name=invLeftWeapon pos=555,300 size=54,96 gridsize=2,3
        """;

    // A point inside each grid (`.nui` space: y counts up, so the pack's top row is its highest y).
    const int PackX = 346 + 5, PackY = 145 + 127, RightX = 353 + 10, RightY = 300 + 10,
              LeftX = 555 + 10, LeftY = 300 + 10;

    sealed record Setup(Location Place, WorldThing Christof, Item Great, Item Torch, InventoryHands Hands);

    static Setup Open(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        var actor = christof.Actor!;
        // Strong enough for the greatsword (STR 55), so that only the hands can refuse it.
        actor.BaseStat[(int)ActorStat.Strength] = actor.Stat[(int)ActorStat.Strength] = 100f;
        var great = place.Make("GreatSword")!.Item!;
        var torch = place.Make("torchU")!.Item!;
        Assert.True(great.TwoHanded);
        actor.Inventory.Add(great);
        actor.Inventory.Add(torch);
        var effects = new EffectDefinitions(files);
        var pane = new InventoryPane(NuiPane.Parse(Nui, "inventoryPane.nui")) { Subject = actor };
        return new(place, christof, great, torch, new InventoryHands(pane, christof, effects) { Rules = new Qol() });
    }

    /// <summary>Puts <paramref name="item"/> in the pack's top-left cell and lifts it onto the cursor.</summary>
    static void Carry(Setup s, Item item)
    {
        item.Location = Slot.Inventory;
        item.Cell = (0, 0);
        Assert.Equal(HandAction.Lifted, s.Hands.Click(PackX, PackY).What);
        Assert.Same(item, s.Hands.Carrying);
    }

    [Fact]
    public void A_torch_dropped_on_the_off_hand_beside_a_two_hander_is_a_swap()
    {
        using var files = Install.Open();
        var s = Open(files);
        var effects = new EffectDefinitions(files);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(s.Christof, s.Great, Slot.RightWeapon, effects, 0));
        Carry(s, s.Torch);

        // The off-hand grid holds the two-hander too, so the count is 1: it comes off onto
        // the cursor and the torch goes on.
        var got = s.Hands.Click(LeftX, LeftY);
        Assert.True(got.What == HandAction.Swapped, got.Say);
        Assert.Equal(Slot.LeftWeapon, s.Torch.Location);
        Assert.Same(s.Torch, s.Christof.Actor!.Utility);
        Assert.Same(s.Great, s.Hands.Carrying);
        Assert.NotEqual(Slot.RightWeapon, s.Great.Location);
    }

    [Fact]
    public void A_two_hander_dropped_on_the_weapon_hand_beside_a_torch_is_a_swap()
    {
        using var files = Install.Open();
        var s = Open(files);
        var effects = new EffectDefinitions(files);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(s.Christof, s.Torch, Slot.LeftWeapon, effects, 0));
        Carry(s, s.Great);

        // The weapon grid is empty and its link holds the torch: count 1, and the swap takes the
        // link's item.
        var got = s.Hands.Click(RightX, RightY);
        Assert.True(got.What == HandAction.Swapped, got.Say);
        Assert.Equal(Slot.RightWeapon, s.Great.Location);
        Assert.Same(s.Torch, s.Hands.Carrying);
        Assert.Null(s.Christof.Actor!.Utility);
    }

    [Fact]
    public void An_equip_that_still_refuses_says_i_cant_wear_that()
    {
        using var files = Install.Open();
        var s = Open(files);
        var effects = new EffectDefinitions(files);
        // A torch in the hand that the pack does not show in the off hand: the count is 0, the drop's
        // tests pass, and the equip's own two-hand test refuses.
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(s.Christof, s.Torch, Slot.LeftWeapon, effects, 0));
        Carry(s, s.Great);
        s.Torch.Location = Slot.Inventory;
        s.Torch.Cell = (5, 0);

        var got = s.Hands.Click(RightX, RightY);
        Assert.Equal(HandAction.Refused, got.What);
        Assert.EndsWith(WearDrop.Said, got.Say);
        Assert.Same(s.Great, s.Hands.Carrying);
    }

    [Fact]
    public void A_character_who_may_not_use_it_is_refused_with_no_word()
    {
        using var files = Install.Open();
        var s = Open(files);
        var actor = s.Christof.Actor!;
        actor.BaseStat[(int)ActorStat.Strength] = actor.Stat[(int)ActorStat.Strength] = 10f;
        Carry(s, s.Great);

        // The drop asks the use test itself and refuses before the placing: no line.
        var got = s.Hands.Click(RightX, RightY);
        Assert.Equal(HandAction.Refused, got.What);
        Assert.DoesNotContain("can't wear", got.Say);
        Assert.Same(s.Great, s.Hands.Carrying);
    }

    [Fact]
    public void Only_the_equips_own_refusal_is_the_line()
    {
        Assert.True(WearDrop.EquipRefused(EquipResult.Restricted));
        Assert.False(WearDrop.EquipRefused(EquipResult.WrongSlot));
        Assert.False(WearDrop.EquipRefused(EquipResult.Equipped));
    }
}
