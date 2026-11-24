// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The shops on the shopper's machine, with a host and players on localhost
/// against the install: a script's `ExecuteBuySell` naming a player sends them the shop and
/// their machine builds the same shelf from the host's seed; their
/// purchase and sale are made on the host and answered,
/// the pack and purse follow; and their close ends the store on the host with
/// `buysellended(false)`.
/// </summary>
public class NetShopsTests
{
    internal static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard, not a speed test (levels fork, 4 s was too few on a busy laptop)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    internal sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Location Place;
        public required Simulation Sim;
        public int Ms;
        public void Step(float seconds = 1f / 30)
        {
            Sim.Step(seconds);
            Ms += (int)(seconds * 1000);
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    internal sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
        public Session Session = new();
        public readonly Conversations Talk = new();
        public readonly List<string> Said = [];
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    internal static Game Host(Vfs.GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Place = place, Sim = new Simulation(place) { Talk = new Conversations() } };
    }

    internal static Player Join(Vfs.GameFiles files, Game game, string name, string template)
    {
        var me = new NetCharacter { Name = name, Template = template };
        var p = new Player { Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port), me) };
        p.Client.Shops.Said += p.Said.Add;
        p.Client.Arms.Said += p.Said.Add;
        p.Client.Scenes.Talk = p.Talk;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: me.Template, entrance: s.Entrance);
            p.Session.Here = p.Place;
            p.Place.Session = p.Session;
            p.Sim = new Simulation(p.Place) { Talk = p.Talk };
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    internal static WorldThing Body(Game game, string name) => game.Server.Players.Single(p => p.Name == name).Avatar!;

    [Fact]
    public void A_players_shop_opens_on_their_machine_and_their_buying_selling_and_close_are_made_on_the_host()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");

        var her = Body(game, "Anna");
        // The inn has nobody else in it: Bob keeps the shop (a keeper is any thing both machines know).
        var keeper = Body(game, "Bob");
        Assert.True(Until(() => anna.Client.Mirror?.Find(keeper.Guid) is not null, All), "she never saw Bob");
        var hostShops = game.Place.Session!.Shops;
        var ended = new List<bool>();
        hostShops.Ended = (_, aborted) => ended.Add(aborted);
        game.Place.Session.AddCash(her, 5000);
        Assert.True(hostShops.Add(game.Place, "SorvenaShop.nbs", keeper, "SMITHY", 0x10, 1.25f, 0.8f, 0x1ff));
        var store = hostShops.All.First();
        var script = new object();

        // Sent to her machine; the host opens no pane of its own and she is the trader.
        Assert.Equal(ShopRoute.Sent, game.Server.Shops.Execute(keeper, her.Guid, Shops.WantFeedback, script));
        Assert.Null(hostShops.Active);
        Assert.Same(her, store.Trader);
        Assert.True(Until(() => anna.Client.Shops.Open is not null, All), "her machine never opened it: " + string.Join("; ", anna.Said));
        Assert.Null(bob.Client.Shops.Open);
        var there = anna.Client.Shops.Open!;
        Assert.Same(there, anna.Session.Shops.Active);
        Assert.Same(anna.Client.Avatar, there.Trader);

        // The same shelf, rolled from the host's seed, and the purse as the host has it.
        Assert.Equal(store.Lines.Select(l => l.Treasure?.Template), there.Lines.Select(l => l.Treasure?.Template));
        Assert.True(Until(() => Shops.CashOf(anna.Client.Avatar!) == 5000 && anna.Session.Gold == 5000, All), "the purse never came");

        // A second shopper is refused while she trades.
        Assert.Equal(ShopRoute.Refused, game.Server.Shops.Execute(keeper, keeper.Guid, Shops.WantFeedback, script));

        // She buys the cheapest thing she can carry: asked from her machine, made on the host.
        var effects = game.Place.Effects?.File;
        var line = there.Lines.Where(l => l.Treasure is not null).OrderBy(l => Shops.BuyPrice(there, l.Treasure!, effects)).First();
        int id = line.Treasure!.Id, price = Shops.BuyPrice(store, store.Lines.First(l => l.Treasure?.Id == id).Treasure!, effects);
        string template = line.Treasure.Template;
        int remaining = line.Remaining;
        Assert.Equal(BuyResult.Asked, anna.Session.Shops.Buy(anna.Place!, there, id));
        Assert.True(Until(() => her.Actor!.Inventory.Items.Any(i => i.Thing?.Template == template), All), "the host never made it");
        Assert.Equal(5000 - price, Shops.CashOf(her));
        Assert.True(Until(() => anna.Client.Avatar!.Actor!.Inventory.Items.Any(i => i.Thing?.Template == template)
                                && Shops.CashOf(anna.Client.Avatar!) == 5000 - price, All), "her pack or purse never followed");
        if (line.Copies != StockLine.Infinite) Assert.Equal(remaining - 1, line.Remaining);

        // She sells it back: made on the host (the item destroyed, the price paid), her pack follows.
        var mine = anna.Client.Avatar!.Actor!.Inventory.Items.First(i => i.Thing?.Template == template).Thing!;
        int before = Shops.CashOf(her);
        Assert.True(anna.Session.Shops.Sell(anna.Place!, there, mine));
        Assert.True(Until(() => Shops.CashOf(her) > before, All), "the host never took the sale");
        Assert.DoesNotContain(her.Actor!.Inventory.Items, i => i.Thing?.Guid == mine.Guid);
        Assert.True(Until(() => !anna.Client.Avatar!.Actor!.Inventory.Items.Any(i => i.Thing?.Guid == mine.Guid)
                                && Shops.CashOf(anna.Client.Avatar!) == Shops.CashOf(her), All), "her pack never followed the sale");

        // Done: her pane goes, the close reaches the host, which ends the store with bAborted false.
        anna.Session.Shops.End();
        Assert.Null(anna.Session.Shops.Active);
        Assert.True(Until(() => store.Trader is null, All), "the host never closed it");
        Assert.Equal([false], ended);
        Assert.Null(game.Server.Shops.OpenFor(game.Server.Players.Single(p => p.Name == "Anna")));

        // The host's own character, or nobody, goes the single-player way.
        Assert.Equal(ShopRoute.Here, game.Server.Shops.Execute(keeper, 0, 0, null));
    }

    /// <summary>A point inside cell (<paramref name="cx"/>, <paramref name="cy"/>) of a grid of the pane, in the pane's space (cells count down, `.nui` up).</summary>
    static (int X, int Y) PointIn(Ui.InventoryPane pane, int slot, int cx = 0, int cy = 0)
    {
        var grid = pane.Grids().Single(g => g.Slot == slot);
        var e = pane.Control(grid.Element)!;
        int px = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[0] : 0, py = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[1] : 0;
        var (_, h) = grid.Pixels;
        return (px + e.Pos[0] + cx * Ui.InventoryPane.CellWidth + 5, py + e.Pos[1] + h - 1 - cy * Ui.InventoryPane.CellHeight - 5);
    }

    /// <summary>
    /// A player's drag while her shop is open (ours, under `quickequip`): her machine sends the lift
    /// and the drop, and the host makes them on her character by the pane's rules — and only while that
    /// shop is open: the same drag after her Done moves nothing there.
    /// </summary>
    [Fact]
    public void A_players_drag_while_shopping_is_made_on_the_host_and_only_while_the_shop_is_open()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var her = Body(game, "Anna");
        var keeper = Body(game, "Bob");
        Assert.True(Until(() => anna.Client.Mirror?.Find(keeper.Guid) is not null, All), "she never saw Bob");

        // A sword in her pack's first cell, on the host, and so on her machine.
        foreach (var worn in her.Actor!.Inventory.Items.Where(i => (i.Location & Slot.Worn) != 0).ToList())
            Equipment.Unequip(her, worn, game.Place.Effects);
        foreach (var cleared in her.Actor.Inventory.Items.Where(i => i.Location == Slot.Inventory).ToList())
            her.Actor.Inventory.Remove(cleared);
        var sword = game.Place.Make("broadsword")!;
        sword.Item!.Location = Slot.Inventory;
        sword.Item.Cell = (0, 0);
        sword.Owner = her;
        her.Actor.Inventory.Add(sword.Item);
        var mine = anna.Client.Avatar!;
        Assert.True(Until(() => mine.Actor!.Inventory.Items.Any(i => i.Thing?.Template == "broadsword" && i.Location == Slot.Inventory
                                                                   && i.Cell == (0, 0)), All), "never in her pack");

        Assert.True(game.Place.Session!.Shops.Add(game.Place, "SorvenaShop.nbs", keeper, "SMITHY", 0x10, 1.25f, 0.8f, 0x1ff));
        Assert.Equal(ShopRoute.Sent, game.Server.Shops.Execute(keeper, her.Guid, Shops.WantFeedback, new object()));
        Assert.True(Until(() => anna.Client.Shops.Open is not null, All), "her machine never opened it");

        // Her own pane and hands, shopping as the game's hook has them.
        var pane = new Ui.InventoryPane(new Ui.UiAssets(files).Pane(Ui.InventoryPane.PaneName)) { Subject = mine.Actor };
        var hands = new Ui.InventoryHands(pane, mine, new EffectDefinitions(files)) { Rules = new Qol(), Shop = _ => true };
        var pack = PointIn(pane, Slot.Inventory);
        var hand = PointIn(pane, Slot.RightWeapon);
        Assert.Equal(Ui.HandAction.Nothing, hands.Press(pack.X, pack.Y, 100, 100).What);
        Assert.Equal(Ui.HandAction.Lifted, hands.Drag(110, 100).What);
        Assert.Equal(Ui.HandAction.Equipped, hands.Release(hand.X, hand.Y).What);
        Assert.True(Until(() => sword.Item.Location == Slot.RightWeapon && her.Actor.Held is null, All), $"the host has it in {sword.Item.Location}");
        Assert.Same(sword.Item, Equipment.InSlot(her.Actor, Slot.RightWeapon));

        // Done; and the same drag back to the pack, made again on her machine, is not the host's to make.
        anna.Session.Shops.End();
        Assert.True(Until(() => game.Server.Shops.OpenFor(game.Server.Players.Single(p => p.Name == "Anna")) is null, All), "never closed");
        Assert.True(Until(() => mine.Actor!.Inventory.Items.Any(i => i.Thing?.Template == "broadsword" && i.Location == Slot.RightWeapon), All));
        Assert.Equal(Ui.HandAction.Lifted, hands.ShopLift(hand.X, hand.Y, belt: false).What);
        hands.ShopDrop(pack.X, pack.Y, 0);
        for (int i = 0; i < 30; i++) All();
        Assert.Equal(Slot.RightWeapon, sword.Item.Location);
        Assert.Null(her.Actor.Held);
    }
}
