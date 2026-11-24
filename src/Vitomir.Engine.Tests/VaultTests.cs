// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The haven vault, which turned out not to be a haven at all.
//
// This was the oldest open question left about items: `0x4000` had been named "the
// vault" from `VaultPane.nui` and then found six of Christof's things sitting in it **while he was
// walking around Prague**, which is not what a stash in a building should look like. The note said
// the experiment was to open the vault, move one item, and diff. A better one was run: it moved
// things in Christof's vault *and put one thing in Wilhem's* — and 1.1 was checked alongside
// it. Both say the same thing, which is why this file exists:
//
//   * **The vault is per character.** The vault fills `vaultGrid` by walking one
//     actor's own inventory list (item to item) and adding an icon for every
//     item whose `Location` is `0x4000`. It is handed `GetCurrentControl()`, and
//     `SetCurrentControl` calls it again — so switching who you steer
//     switches whose vault is on screen. `vaultCurrentChar` is the label that says whose.
//   * **It travels, because it is not a place.** The items hang off the character like everything
//     else they carry, so they are in the actor's record in `!main.nsg` wherever the party is.
//     A *Monastery 1* save — a long way from the Prague haven — carries four of Christof's
//     and one of Wilhem's. What is tied to the haven is only the *pane*: the script native
//     `Vault(guid)` (native 13) opens it, and the haven's `HavenVault` script is what
//     calls it.
//   * **It is an ordinary icon grid.** The vault's show gives the element mask `0x4000` and
//     registers the grid's drop handler on binding 0 — the same drop handler the pack and the belt get.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class VaultTests
{
    /// <summary>
    /// The shipped file, because this is the claim that a mod could change and the reader must
    /// follow: one grid, ten by nine, and the two other elements the game looks up by name.
    /// </summary>
    [Fact]
    public void The_shipped_pane_is_one_ten_by_nine_grid()
    {
        using var files = Install.Open();
        var pane = new InventoryPane(new UiAssets(files).Pane(InventoryPane.VaultPaneName),
                                     InventoryPane.VaultSlots);

        var grids = pane.Grids().ToList();
        var only = Assert.Single(grids);
        Assert.Equal("vaultGrid", only.Element);
        Assert.Equal(Slot.Vault, only.Slot);
        Assert.Equal((10, 9), (only.Across, only.Down));
        Assert.True(only.IsContainer);

        // The other two elements the vault looks up by name, so that a rename here is a test failure
        // rather than a pane that quietly loses its button.
        Assert.NotNull(pane.Control("vaultOKButton"));
        Assert.NotNull(pane.Control("vaultCurrentChar"));
    }

    /// <summary>
    /// The OK button is found where the file puts it (26 x 15 at 269,154) and nowhere else — the
    /// host presses it there and closes the vault on the release over it.
    /// </summary>
    [Fact]
    public void The_ok_button_is_under_its_own_rectangle_and_not_the_grid()
    {
        using var files = Install.Open();
        var pane = new InventoryPane(new UiAssets(files).Pane(InventoryPane.VaultPaneName), InventoryPane.VaultSlots);
        Assert.Equal(InventoryPane.VaultOk, pane.ButtonAt(269 + 13, 154 + 7), ignoreCase: true);
        Assert.Null(pane.ButtonAt(269 + 13, 154 + 20));
        Assert.Null(pane.ButtonAt(100, 300));             // the grid
    }

    /// <summary>
    /// `vaultCurrentChar` says whose vault this is — which is the pane admitting, in its own file,
    /// that there is more than one.
    /// </summary>
    [Fact]
    public void The_pane_names_the_character_whose_vault_it_shows()
    {
        var pane = Vault(Christof("Christof"));
        Assert.Equal("Christof", pane.Texts()["vaultCurrentChar"]);
        Assert.False(pane.Texts().ContainsKey("invGold"));

        pane.Subject = Christof("Wilhem");
        Assert.Equal("Wilhem", pane.Texts()["vaultCurrentChar"]);
    }

    /// <summary>The inventory pane keeps its own one box and gains nothing from the change.</summary>
    [Fact]
    public void The_inventory_pane_still_says_only_the_gold()
    {
        var pane = new InventoryPane(NuiPane.Parse(Inv, "inventoryPane.nui")) { Gold = 985 };
        Assert.Equal("985", pane.Texts()["invGold"]);
        Assert.False(pane.Texts().ContainsKey("vaultCurrentChar"));
    }

    /// <summary>
    /// **Two characters, two vaults**, which is the shape of that save and of the vault's fill both. The
    /// pane walks its subject's list and nobody else's, so Wilhem's one potion is invisible on
    /// Christof's pane and the other way round.
    /// </summary>
    [Fact]
    public void One_characters_vault_is_not_anothers()
    {
        var christof = Christof("Christof");
        var wilhem = Christof("Wilhem");
        christof.Inventory.Add(Stored("cuthbertletter", 0, 0));
        christof.Inventory.Add(Stored("potiondisease", 1, 0));
        wilhem.Inventory.Add(Stored("potionpoison", 0, 0));

        var pane = Vault(christof);
        Assert.Equal(["cuthbertletter", "potiondisease"],
                     pane.Placed().Select(p => p.Item.Name).Order());

        pane.Subject = wilhem;
        Assert.Equal("potionpoison", Assert.Single(pane.Placed()).Item.Name);
    }

    /// <summary>
    /// A stored item is **not** in the pack, and a packed item is not in the vault — the two panes
    /// read the same inventory list and split it by the one word that says where the item is.
    /// </summary>
    [Fact]
    public void The_two_panes_split_one_list_by_the_location_word()
    {
        var actor = Christof("Christof");
        var stored = Stored("rapier", 0, 0);
        var carried = new Item { Name = "axerusty", Type = ItemType.Weapon, Icon = "axeIcon",
                                 Location = Slot.Inventory, Cell = (0, 0) };
        actor.Inventory.Add(stored);
        actor.Inventory.Add(carried);

        Assert.Equal("rapier", Assert.Single(Vault(actor).Placed()).Item.Name);
        var pack = new InventoryPane(NuiPane.Parse(Inv, "inventoryPane.nui")) { Subject = actor };
        Assert.Equal("axerusty", Assert.Single(pack.Placed()).Item.Name);
    }

    /// <summary>
    /// **Out of the vault and into the pack, in two clicks across two panes.** This is the whole
    /// reason the hand holds a list: in the engine an icon grid belongs to a character and not to a
    /// pane, so the drag is one item changing its `Location` word and not a transfer between two
    /// inventories.
    /// </summary>
    [Fact]
    public void An_item_drags_out_of_the_vault_and_into_the_pack()
    {
        var (pack, vault, hands, thing) = Both();
        var rapier = Stored("rapier", 0, 0);
        thing.Actor!.Inventory.Add(rapier);
        hands.Panes.Add(vault);

        // vaultGrid is at 26,172, 10 x 9 cells of 27 x 32: cell (0,0) is the top-left, so the
        // highest row of pixels in the grid.
        var up = hands.Click(26 + 1, 172 + 9 * 32 - 1);
        Assert.Equal(HandAction.Lifted, up.What);
        Assert.Same(rapier, hands.Carrying);

        // And down into the pack, which is the other pane entirely.
        var down = hands.Click(346 + 1, 145 + 4 * 32 - 1);
        Assert.Equal(HandAction.Dropped, down.What);
        Assert.Equal(Slot.Inventory, rapier.Location);
        Assert.Equal((0, 0), rapier.Cell);
        Assert.Null(hands.Carrying);
    }

    /// <summary>The same drag the other way, because the vault takes anything.</summary>
    [Fact]
    public void And_back_in_again()
    {
        var (_, vault, hands, thing) = Both();
        var sword = new Item { Name = "broadsword", Type = ItemType.Weapon, IconSize = 5,
                               Icon = "swordBroadIcon", Location = Slot.Inventory, Cell = (0, 0) };
        thing.Actor!.Inventory.Add(sword);
        hands.Panes.Add(vault);

        Assert.Equal(HandAction.Lifted, hands.Click(346 + 1, 145 + 4 * 32 - 1).What);
        var down = hands.Click(26 + 27, 172 + 9 * 32 - 1 - 48);     // held by its middle over the top-left cells (DropCell)
        Assert.Equal(HandAction.Dropped, down.What);
        Assert.Equal(Slot.Vault, sword.Location);
        Assert.Equal((0, 0), sword.Cell);
    }

    /// <summary>
    /// **A closed pane is not clickable.** The vault's close destroys its elements when it shuts, so
    /// the grid stops answering for its rectangle — here that is the pane simply not being in the
    /// hand's list, and a click at those coordinates falling through to the world.
    /// </summary>
    [Fact]
    public void A_pane_that_is_not_open_does_not_take_the_click()
    {
        var (_, _, hands, thing) = Both();
        thing.Actor!.Inventory.Add(Stored("rapier", 0, 0));

        Assert.False(hands.Over(26 + 1, 172 + 9 * 32 - 1));
        Assert.Equal(HandAction.Nothing, hands.Click(26 + 1, 172 + 9 * 32 - 1).What);
        Assert.Null(hands.Carrying);
    }

    // --- the fixtures -------------------------------------------------------------------------

    // The shipped file's own two lines, so these tests do not need an install to place an item.
    const string VaultNui = """
        name=vaultPane
        pos=0,0
        element icongrid name=vaultGrid pos=26,172 size=270,288 gridsize=10,9 flags=0x02
        element button name=vaultOKButton depressed=OKButtonDep pos=269,154 size=26,15 flags=0x02
        element textbox name=vaultCurrentChar textboxflags=0x03 pos=175,153 size=90,17 flags=0x02 maxItems=1
        """;

    const string Inv = """
        name=inventoryPane
        pos=0,0
        element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4
        element weargrid name=invRightWeapon pos=353,300 size=54,96 gridsize=2,3
        element textbox name=invGold textboxflags=0x01 pos=560,120 size=60,17
        """;

    static InventoryPane Vault(Actor subject) =>
        new(NuiPane.Parse(VaultNui, "VaultPane.nui"), InventoryPane.VaultSlots) { Subject = subject };

    static Actor Christof(string name)
    {
        var actor = new Actor { Type = ActorType.Human, Name = name };
        actor.Health = actor.MaxHealth = 100;
        return actor;
    }

    static Item Stored(string name, int x, int y) => new()
    {
        Name = name, Type = ItemType.General, Icon = name + "Icon",
        Location = Slot.Vault, Cell = (x, y),
    };

    static (InventoryPane Pack, InventoryPane Vault, InventoryHands Hands, WorldThing Thing) Both()
    {
        var actor = Christof("Christof");
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
            Type = ThingType.Player, Actor = actor,
        };
        var pack = new InventoryPane(NuiPane.Parse(Inv, "inventoryPane.nui")) { Subject = actor };
        var vault = Vault(actor);
        var hands = new InventoryHands(pack, thing, new EffectDefinitions(Install.Open()))
        {
            Rules = new Qol(),
        };
        return (pack, vault, hands, thing);
    }
}
