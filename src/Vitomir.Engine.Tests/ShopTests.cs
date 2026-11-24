// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Shops and their pane. The price
/// checks are purchases made in 1.1, which the formula reproduced to the coin before any of
/// this was written.
/// </summary>
public class ShopTests
{
    sealed record Shop(Location Place, Session Session, WorldThing Trader, WorldThing Keeper);

    static Shop Open(GameFiles files, float manipulation = 25f, int gold = 10_000)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var session = new Session { Here = place };
        place.Session = session;
        var trader = place.Player!.Thing;
        trader.Actor!.Stat[(int)ActorStat.Manipulation] = manipulation;
        session.Gold = gold;
        trader.Actor.Cash = gold;
        var keeper = place.Things.First(t => t.IsActor && !ReferenceEquals(t, trader));
        session.Shops.Clock = () => new DateTime(2026, 9, 21, 12, 0, 0);
        return new Shop(place, session, trader, keeper);
    }

    static int Gold(Shop s) => Shops.CashOf(s.Trader);

    static Store Add(Shop s, string file = "PragueSmithy.nbs")
    {
        Assert.True(s.Session.Shops.Add(s.Place, file, s.Keeper, "SMITHY", 0, 1.25f, 0.8f, 0x53));
        return s.Session.Shops.All[0];
    }

    [Fact]
    public void The_smithys_file_parses_line_for_line()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        Assert.Equal(56, store.Lines.Count);
        Assert.True(store.Lines[0].Direct);
        Assert.Equal("torchu", store.Lines[0].Template);
        Assert.Equal(3, store.Lines[0].Copies);
        Assert.Contains(store.Lines, l => !l.Direct && l.Class == 215 && l.Copies == 1);
        // Every direct line is stocked and identified, and numbered with its index.
        for (int i = 0; i < store.Lines.Count; i++)
            if (store.Lines[i] is { Direct: true, Treasure: { } t })
            {
                Assert.Equal(i, t.Id);
                Assert.True((t.Item.Flags & ItemFlags.Identified) != 0);
            }
    }

    [Fact]
    public void Parsing_skips_indented_lines_and_takes_any_non_d_letter_as_random()
    {
        var text = "# comment\n; also\n// and this\n  D broadSword 1\nD broadSword 0\nX 215 2\nR 300 1\nD nosuchthing 1\n\nR 215\n"u8.ToArray();
        var lines = Shops.Parse(text, name => name == "broadsword").ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(StockLine.Infinite, lines[0].Copies);          // 0 is infinite
        Assert.False(lines[1].Direct);                              // X is as good as R
        Assert.Equal(215, lines[1].Class);
    }

    /// <summary>
    /// The dedupe looks at the first store with the owner's GUID and nothing else: same file,
    /// refreshed and kept; a different file, a new store in front, stocked from scratch.
    /// </summary>
    [Fact]
    public void A_second_visit_keeps_the_stock_and_a_different_list_makes_a_new_store()
    {
        using var files = Install.Open();
        var s = Open(files);
        var first = Add(s);
        var shelf = first.Lines.Select(l => l.Treasure).ToList();
        Assert.True(s.Session.Shops.Add(s.Place, "pragueSMITHY.nbs", s.Keeper, "SMITHY", 0, 1.5f, 0.5f, 0x53));
        Assert.Single(s.Session.Shops.All);
        Assert.Equal(1.5f, first.BuyRate);
        Assert.Equal(shelf, first.Lines.Select(l => l.Treasure));
        Assert.True(s.Session.Shops.Add(s.Place, "UnornaShop.nbs", s.Keeper, "UNORNASHOP", 0, 1.25f, 0.8f, 0x1af));
        Assert.Equal(2, s.Session.Shops.All.Count);
        Assert.Equal("UnornaShop.nbs", s.Session.Shops.All[0].File);
        // The older one is now second, and so no longer the one a dedupe looks at.
        Assert.True(s.Session.Shops.Add(s.Place, "PragueSmithy.nbs", s.Keeper, "SMITHY", 0, 1.25f, 0.8f, 0x53));
        Assert.Equal(3, s.Session.Shops.All.Count);
        // Rates at or below zero are refused outright.
        Assert.False(s.Session.Shops.Add(s.Place, "PragueSmithy.nbs", s.Keeper, "SMITHY", 0, 0f, 0.8f, 0x53));
    }

    /// <summary>
    /// A buckler: 30 gold of `ITEM_BASECOST`, Manipulation 25, the smithy's 1.25: 1.1 charges 42.
    /// And plate mail, 5,000: about 7,500 low and 4,000 to 5,000 at 100, which the formula puts at
    /// 7,187 and 4,375.
    /// </summary>
    [Theory]
    [InlineData("Buckler", 25f, 42)]
    [InlineData("plateMail", 25f, 7187)]
    [InlineData("plateMail", 100f, 4375)]
    public void The_price_is_the_one_paid_in_1_1(string template, float manipulation, int price)
    {
        using var files = Install.Open();
        var s = Open(files, manipulation);
        var store = Add(s);
        store.Trader = s.Trader;
        var t = s.Place.Treasure!.Build(template.ToLowerInvariant())!;
        Assert.Equal(price, Shops.BuyPrice(store, t, s.Place.Effects?.File));
    }

    [Fact]
    public void Selling_pays_a_quarter_for_what_is_not_identified()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        store.Trader = s.Trader;
        var item = s.Place.Treasure!.Build("buckler")!.Item;
        item.Flags |= ItemFlags.Identified;
        Assert.Equal(20, Shops.SellPrice(store, item, null));      // 30 × 0.8 = 24, × 0.85 = 20.4
        item.Flags &= ~ItemFlags.Identified;
        Assert.Equal(5, Shops.SellPrice(store, item, null));       // 24 / 4 = 6, × 0.85 = 5.1
    }

    [Fact]
    public void Buying_puts_it_in_the_pack_identified_takes_the_price_and_counts_the_line_down()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        Assert.True(s.Session.Shops.Execute(s.Place, s.Keeper, Shops.WantFeedback, null));
        Assert.Same(s.Trader, store.Trader);
        var line = store.Lines.First(l => l is { Direct: true, Copies: 3, Treasure: not null } && l.Template == "buckler");
        var t = line.Treasure!;
        int price = Shops.BuyPrice(store, t, s.Place.Effects?.File), before = Gold(s);

        Assert.Equal(BuyResult.Bought, s.Session.Shops.Buy(s.Place, store, t.Id));

        Assert.Equal(before - price, Gold(s));
        var bought = s.Trader.Actor!.Inventory.Items.First();
        Assert.Equal("buckler", bought.Name, ignoreCase: true);
        Assert.True((bought.Flags & ItemFlags.Identified) != 0);
        Assert.Equal(Slot.Inventory, bought.Location);
        Assert.Same(s.Trader, bought.Thing!.Owner);
        Assert.Equal(2, line.Remaining);
        // Three copies, and the third empties the line.
        s.Session.Shops.Buy(s.Place, store, t.Id);
        s.Session.Shops.Buy(s.Place, store, t.Id);
        Assert.Null(line.Treasure);
        Assert.Equal(BuyResult.NoLine, s.Session.Shops.Buy(s.Place, store, t.Id));
    }

    [Fact]
    public void Without_the_money_nothing_is_bought()
    {
        using var files = Install.Open();
        var s = Open(files, gold: 5);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var t = store.Lines.First(l => l.Treasure is not null).Treasure!;
        int items = s.Trader.Actor!.Inventory.Count;
        Assert.Equal(BuyResult.CannotAfford, s.Session.Shops.Buy(s.Place, store, t.Id));
        Assert.Equal(items, s.Trader.Actor.Inventory.Count);
        Assert.Equal(5, Gold(s));
    }

    [Fact]
    public void Selling_destroys_the_item_and_pays_and_the_mask_and_no_sell_refuse()
    {
        using var files = Install.Open();
        var s = Open(files, gold: 1000);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var t = store.Lines.First(l => l.Template == "buckler").Treasure!;
        s.Session.Shops.Buy(s.Place, store, t.Id);
        var mine = s.Trader.Actor!.Inventory.Items.First().Thing!;
        int before = Gold(s), price = Shops.SellPrice(store, mine.Item!, s.Place.Effects?.File);

        Assert.True(s.Session.Shops.Sell(s.Place, store, mine));
        Assert.Equal(before + price, Gold(s));
        Assert.DoesNotContain(mine.Item!, s.Trader.Actor.Inventory.Items);
        Assert.True(mine.Removed);
        // Nothing sold ever joins the shelf.
        Assert.Equal(56, store.Lines.Count);

        s.Session.Shops.Buy(s.Place, store, t.Id);
        var again = s.Trader.Actor.Inventory.Items.First().Thing!;
        store.SellMask = 0x100;                                    // a shop that does not take it
        Assert.False(s.Session.Shops.Sell(s.Place, store, again));
        store.SellMask = 0x53;
        again.Item!.Flags |= ItemFlags.NoSell;
        Assert.False(s.Session.Shops.Sell(s.Place, store, again));
    }

    /// <summary>
    /// The sale takes the item off the trader through the inventory's removal (read in a comparison with 1.1): a
    /// worn item comes off first, and when its `unworn` keeps it on, the sale ends there — nothing
    /// paid, the item kept, and the click answered as taken. Vitomir took it off
    /// regardless and sold it.
    /// </summary>
    [Fact]
    public void A_worn_item_its_script_keeps_on_is_not_sold()
    {
        using var files = Install.Open();
        var s = Open(files, gold: 1000);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var t = store.Lines.First(l => l.Template == "buckler").Treasure!;
        s.Session.Shops.Buy(s.Place, store, t.Id);
        var mine = s.Trader.Actor!.Inventory.Items.First().Thing!;
        mine.Item!.Location = Slot.LeftWeapon;                    // worn, on the shield arm
        s.Place.ItemScripts = new Ground.Scripts(Unworn: (_, _) => false);
        int before = Gold(s);

        Assert.True(s.Session.Shops.Sell(s.Place, store, mine));
        Assert.Equal(before, Gold(s));
        Assert.Contains(mine.Item!, s.Trader.Actor.Inventory.Items);
        Assert.False(mine.Removed);
        Assert.NotEqual(Slot.None, mine.Item!.Location);
    }

    [Fact]
    public void The_end_tells_the_script_it_was_aborted_and_shuts_the_store()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        var script = new object();
        bool? told = null;
        s.Session.Shops.Ended = (_, aborted) => told = aborted;
        Assert.True(s.Session.Shops.Execute(s.Place, s.Keeper, Shops.WantFeedback, script));
        Assert.False(s.Session.Shops.Execute(s.Place, s.Keeper, Shops.WantFeedback, script));   // already open
        s.Session.Shops.End();
        Assert.True(told);
        Assert.Null(store.Trader);
        Assert.Null(s.Session.Shops.Active);
    }

    // --- the pane -----------------------------------------------------------------------------------

    static BuySellPane Pane(GameFiles files, Store store)
    {
        var pane = new BuySellPane(Formats.NuiPane.Read(files.Read("ui/buySellPane.nui"))) { Store = store };
        pane.Refresh();
        return pane;
    }

    [Fact]
    public void The_shelf_lists_name_and_price_greys_what_it_cannot_afford_and_chooses_row_zero()
    {
        using var files = Install.Open();
        var s = Open(files, gold: 45);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var pane = Pane(files, store);
        Assert.Equal(store.Lines.Count(l => l.Treasure is not null), pane.Rows.Count);
        Assert.EndsWith($"({Shops.BuyPrice(store, pane.Rows[0].Treasure, null)})", pane.Rows[0].Text);
        Assert.All(pane.Rows, r => Assert.Equal(
            (uint)45 >= (uint)Shops.BuyPrice(store, r.Treasure, null) ? BuySellPane.Affordable : BuySellPane.TooDear, r.Colour));
        Assert.Equal(0, pane.Current);
        Assert.Same(pane.Rows[0].Treasure, pane.SelectedTreasure);
    }

    /// <summary>
    /// Choosing a row scrolls it to the top — except one below the first page, whose offset
    /// comes out negative and clamps to 0: the list jumps back to the top.
    /// </summary>
    [Fact]
    public void Choosing_a_row_below_the_first_page_jumps_the_list_to_the_top()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var pane = Pane(files, store);
        Assert.True(pane.Rows.Count > 30);
        pane.SelectRow(10);
        Assert.Equal(10 * 11, pane.Scroll);
        pane.SelectRow(30);
        Assert.Equal(0, pane.Scroll);
        Assert.Equal(30, pane.Current);
        // Twenty-three whole rows and a sliver of the twenty-fourth.
        Assert.True(pane.Visible(22) && pane.Visible(23) && !pane.Visible(24));
        pane.Wheel(-1);
        Assert.Equal(BuySellPane.Page, pane.Scroll);
    }

    [Fact]
    public void Clicking_your_own_item_offers_it_and_keeps_the_shelf_row_current()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var t = store.Lines.First(l => l.Template == "buckler").Treasure!;
        s.Session.Shops.Buy(s.Place, store, t.Id);
        var pane = Pane(files, store);
        pane.SelectRow(3);
        var mine = s.Trader.Actor!.Inventory.Items.First().Thing!;

        Assert.True(pane.ClickItem(mine));

        Assert.True(pane.SellEnabled);
        Assert.False(pane.BuyEnabled);
        Assert.Equal(Shops.SellPrice(store, mine.Item!, null).ToString(), pane.Value);
        Assert.Equal(3, pane.Current);                             // the red row stays
        mine.Item!.Flags |= ItemFlags.NoSell;
        Assert.False(pane.ClickItem(mine));
    }

    /// <summary>
    /// **A right click on a shelf row buys it at once, and the list stays where it was** (ours,
    /// always on): a grind at Othelios' showed it: 1.1's rebuild after a purchase puts the list back at the
    /// top. The purse, the stock and the pack as a purchase asks.
    /// </summary>
    [Fact]
    public void A_right_click_on_a_row_buys_it_at_once_and_the_list_keeps_its_scroll()
    {
        using var files = Install.Open();
        var s = Open(files);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var pane = Pane(files, store);
        s.Session.Shops.Changed = _ => pane.Refresh();
        pane.Wheel(-1);
        int scrolled = pane.Scroll;
        Assert.True(scrolled > 0);
        // A row below the first page whose line still has stock after one is bought.
        int row = Enumerable.Range(24, pane.Rows.Count - 24).First(i =>
            store.Lines.First(l => ReferenceEquals(l.Treasure, pane.Rows[i].Treasure)) is var l
            && (l.Copies == StockLine.Infinite || l.Remaining > 1));
        var t = pane.Rows[row].Treasure;
        int price = Shops.BuyPrice(store, t, null), before = Gold(s), items = s.Trader.Actor!.Inventory.Count;

        Assert.Equal(BuyResult.Bought, pane.QuickBuy(row, s.Place, s.Session.Shops));

        Assert.Equal(before - price, Gold(s));
        Assert.Equal(items + 1, s.Trader.Actor.Inventory.Count);
        Assert.Equal(scrolled, pane.Scroll);                       // not back at the top
        Assert.Equal(row, pane.Current);
        Assert.Same(t, pane.SelectedTreasure);

        // Too dear: nothing bought, the row chosen with its price in red.
        s.Trader.Actor.Cash = s.Session.Gold = 0;
        Assert.Null(pane.QuickBuy(row, s.Place, s.Session.Shops));
        Assert.Equal(items + 1, s.Trader.Actor.Inventory.Count);
        Assert.Equal(BuySellPane.ValueTooDear, pane.ValueColour);
    }

    /// <summary>A right click on the player's own item sells it at once, by the shop's rules (ours).</summary>
    [Fact]
    public void A_right_click_on_an_item_sells_it_at_once_by_the_shops_rules()
    {
        using var files = Install.Open();
        var s = Open(files, gold: 1000);
        var store = Add(s);
        s.Session.Shops.Execute(s.Place, s.Keeper, 0, null);
        var t = store.Lines.First(l => l.Template == "buckler").Treasure!;
        s.Session.Shops.Buy(s.Place, store, t.Id);
        s.Session.Shops.Buy(s.Place, store, t.Id);
        var pane = Pane(files, store);
        s.Session.Shops.Changed = _ => pane.Refresh();
        pane.Wheel(-1);
        int scrolled = pane.Scroll;
        var mine = s.Trader.Actor!.Inventory.Items.First().Thing!;
        int before = Gold(s), price = Shops.SellPrice(store, mine.Item!, null);

        Assert.True(pane.QuickSell(mine, s.Place, s.Session.Shops));

        Assert.Equal(before + price, Gold(s));
        Assert.True(mine.Removed);
        Assert.Equal(scrolled, pane.Scroll);

        var other = s.Trader.Actor.Inventory.Items.First().Thing!;
        store.SellMask = 0x100;                                    // a shop that does not take it
        Assert.False(pane.QuickSell(other, s.Place, s.Session.Shops));
        Assert.False(other.Removed);
    }
}
