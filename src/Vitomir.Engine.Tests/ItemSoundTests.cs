// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// An item's own noises from the pack, each from the item's foley set and sounded on its owner
/// (the item's own foley): `remove` as it is lifted off the pane (the removal
/// with its sound on), `wear` **twice** as it is put down (then the drop's own), and `use`
/// when it is used.
/// </summary>
public class ItemSoundTests
{
    const string Nui = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        """;

    [Fact]
    public void An_item_says_remove_when_lifted_wear_twice_when_put_down_and_use_when_used()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var audio = new GameAudio(files);
        place.Audio = audio;
        audio.Enter(place);
        var christof = place.Player!.Thing;
        var foley = audio.Foley.Get("itemBloodBag.nag");
        Assert.SkipWhen(foley is null || foley[FoleyEvent.Wear].Count == 0 || foley[FoleyEvent.Remove].Count == 0
                        || foley[FoleyEvent.Use].Count == 0, "itemBloodBag.nag has no wear, remove and use");

        var bag = place.Make("CuthbertLetter")!;
        bag.Foley = "itemBloodBag.nag";
        var item = new Item { Name = "blood bag", Type = ItemType.General, IconSize = 0, Icon = "x", Location = Slot.Inventory, Cell = (0, 0), Amount = 2, Thing = bag };
        christof.Actor!.Inventory.Add(item);
        var pane = new InventoryPane(NuiPane.Parse(Nui, "inventoryPane.nui")) { Subject = christof.Actor };
        var hands = new InventoryHands(pane, christof, new EffectDefinitions(files)) { Rules = new Qol() };

        int Playing(int slot) => audio.Mixer.Channels.Count(c => c.Active && c.Sound is { } s
                                   && foley![slot].Any(e => ReferenceEquals(e.Sound, s)));

        // Counted as channels started, since a set may give two events the same recording.
        int removes = Playing(FoleyEvent.Remove);
        Assert.Equal(HandAction.Lifted, hands.ClickIn(pane.Grids().First(), 0, 0).What);
        Assert.Equal(removes + 1, Playing(FoleyEvent.Remove));

        int wears = Playing(FoleyEvent.Wear);
        Assert.Equal(HandAction.Dropped, hands.ClickIn(pane.Grids().First(), 3, 1).What);
        Assert.Equal(wears + 2, Playing(FoleyEvent.Wear));

        int uses = Playing(FoleyEvent.Use);
        Items.Use(christof, item, place.Milliseconds, null);
        Assert.Equal(uses + 1, Playing(FoleyEvent.Use));
    }

    /// <summary>
    /// A script's `RemoveItemFromInventory` passes the removal its sound flag, so the item
    /// says `remove` as it leaves its keeper — unless it is one that goes at no charges and has none
    /// left (item flag 1 and an amount of 0).
    /// </summary>
    [Fact]
    public void A_script_s_RemoveItem_says_remove()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var where = ChronicleSet.Load(files).ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var audio = new GameAudio(files);
        place.Audio = audio;
        audio.Enter(place);
        var christof = place.Player!.Thing;
        var foley = audio.Foley.Get("itemBloodBag.nag");
        Assert.SkipWhen(foley is null || foley[FoleyEvent.Remove].Count == 0, "itemBloodBag.nag has no remove");
        int Playing() => audio.Mixer.Channels.Count(c => c.Active && c.Sound is { } s
                           && foley![FoleyEvent.Remove].Any(e => ReferenceEquals(e.Sound, s)));

        WorldThing Carried(ItemFlags flags, int amount)
        {
            var thing = place.Make("CuthbertLetter")!;
            thing.Foley = "itemBloodBag.nag";
            var item = thing.Item!;
            item.Flags = flags;
            item.Amount = amount;
            item.Location = Slot.Inventory;
            christof.Actor!.Inventory.Add(item);
            thing.Owner = christof;
            return thing;
        }

        int before = Playing();
        Assert.Same(christof, Ground.Remove(place, Carried(ItemFlags.None, 1)));
        Assert.Equal(before + 1, Playing());
        Assert.Same(christof, Ground.Remove(place, Carried(ItemFlags.DestroyedAtZeroCharges, 0)));
        Assert.Equal(before + 1, Playing());                      // spent: no noise
    }
}
