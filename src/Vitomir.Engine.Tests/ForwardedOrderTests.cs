// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A player's use, pick-up and put-down on their own machine** (the use, take and drop orders, their client branches at
/// The order's own tests — busy, the repeat, ethereal — and the cancel come first, so an
/// order they refuse is never sent; one sent makes the character busy there for 250 ms (the use) or 200 ms (the other
/// two), so a second click within that time is refused there too. Found by a review of the docs: Vitomir
/// forwarded at the top of each, before any test, and set no busy time.
/// </summary>
public class ForwardedOrderTests
{
    sealed class Sink : IOrderSink
    {
        public readonly List<NetOrder> Orders = [];
        public void Send(NetOrder order) => Orders.Add(order);
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    [Fact]
    public void A_players_orders_are_tested_on_their_machine_before_they_go_and_hold_it_after()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var me = who.Actor!;
        var sword = place.Make("broadsword")!;
        sword.Owner = who;
        me.Inventory.Add(sword.Item!);
        Stow.Place(me, sword.Item!, Slot.Inventory);
        Ground.Drop(place, who, sword.Item!, nowMs: 0);
        Assert.True(sword.InWorld);
        var prop = place.Things.First(t => t.Type == ThingType.Prop && t.InWorld && !t.Has(WorldThing.Flag.Remove));

        var sink = new Sink();
        NetOrders.Attach(place, NetMode.Client, sink);
        try
        {
            // Busy: refused on her machine and never sent (the busy test comes before the send).
            me.BusyUntil = 500;
            Assert.Equal(OrderResult.Busy, Actions.Fetch(who, sword, nowMs: 0));
            Assert.Empty(sink.Orders);

            // Sent: kind 3, and her character busy 200 ms; nothing carried out here.
            Assert.Equal(OrderResult.Started, Actions.Fetch(who, sword, nowMs: 1000));
            Assert.Equal((OrderKind.Take, sword.Guid), (sink.Orders.Single().Kind, sink.Orders.Single().Target));
            Assert.Equal(1000 + ForwardedOrders.TakeBusyMs, me.BusyUntil);
            Assert.Equal(ActionMode.None, me.Action);
            // A second click inside the 200 ms: refused there, not sent.
            Assert.Equal(OrderResult.Busy, Actions.Fetch(who, sword, nowMs: 1150));
            Assert.Single(sink.Orders);

            // Ethereal: the use refuses it before sending.
            me.Flags |= Actor.Flag.Ethereal;
            Assert.Equal(OrderResult.Refused, Actions.Use(who, prop, 2000));
            me.Flags &= ~Actor.Flag.Ethereal;
            Assert.Single(sink.Orders);
            // The use sent: kind 4, busy 250 ms.
            Assert.Equal(OrderResult.Started, Actions.Use(who, prop, 2000));
            Assert.Equal((OrderKind.Use, prop.Guid), (sink.Orders[^1].Kind, sink.Orders[^1].Target));
            Assert.Equal(2000 + ForwardedOrders.UseBusyMs, me.BusyUntil);
            // The repeat — already on this use — answers 0 there and sends nothing.
            me.Action = ActionMode.Use;
            me.Target = prop;
            Assert.Equal(OrderResult.Ignored, Actions.Use(who, prop, 3000));
            Assert.Equal(2, sink.Orders.Count);
            me.Action = ActionMode.None;
            me.Target = null;

            // The put-down of something she carries: kind 0xe with the point, busy 200 ms.
            var dagger = place.Make("broadsword")!;
            dagger.Owner = who;
            me.Inventory.Add(dagger.Item!);
            Stow.Place(me, dagger.Item!, Slot.Inventory);
            var at = who.Position + new Vector3(40f, 0f, 0f);
            Assert.Equal(OrderResult.Started, Actions.Put(who, dagger.Item!, at, 4000));
            Assert.Equal((OrderKind.Drop, dagger.Guid, at), (sink.Orders[^1].Kind, sink.Orders[^1].Target, sink.Orders[^1].Point));
            Assert.Equal(4000 + ForwardedOrders.PutBusyMs, me.BusyUntil);
            Assert.False(dagger.InWorld);                                              // still hers until the host says
        }
        finally { NetOrders.Detach(place); }
    }

    /// <summary>
    /// With a host and a player on localhost: her put-down of what she holds, made on her machine, passes its tests
    /// there against her copy, goes to the host and holds her machine 200 ms; the host carries it out on her character,
    /// and the letter lies in the host's world.
    /// </summary>
    [Fact]
    public void A_players_put_down_goes_from_her_machine_to_the_host_which_makes_it()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var host = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var hostSim = new Simulation(host);
        using var server = new GameServer(settings, new NetPeer(0));
        server.Start();
        server.Enter(host, null);
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                          new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        Location? mine = null;
        Simulation? mySim = null;
        client.Started += () =>
        {
            var c = client.Settings!;
            mine = Location.Load(files, c.Level, scene: c.Scene.Length > 0 ? c.Scene : null, player: "MP_Serena", entrance: c.Entrance);
            mySim = new Simulation(mine);
            client.Enter(mine, mine.Player?.Thing);
        };
        int ms = 0;
        void Step() { hostSim.Step(1f / 30); ms += 33; server.Poll(ms); client.Poll(1f / 30); mySim?.Step(1f / 30); }
        bool Until(Func<bool> done)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 10000) { Step(); if (done()) return true; Thread.Sleep(1); }
            return done();
        }
        Assert.True(Until(() => client.Avatar is not null), "never played");
        var hers = server.Players.Single(p => !p.Host).Avatar!;
        var letter = host.Make("AnezkaLetter")!;
        letter.Item!.Location = Slot.Inventory;
        letter.Item.Cell = (0, 0);
        letter.Owner = hers;
        hers.Actor!.Inventory.Add(letter.Item);
        var her = client.Avatar!;
        Assert.True(Until(() => her.Actor!.Inventory.Items.Any(i => i.Thing?.Template == "AnezkaLetter")), "never in her pack");
        // She lifts it onto her pointer, as the game's hands do; the host lifts it on her character there.
        var pane = new Ui.InventoryPane(new Ui.UiAssets(files).Pane(Ui.InventoryPane.PaneName)) { Subject = her.Actor };
        var hands = new Ui.InventoryHands(pane, her, new EffectDefinitions(files));
        Assert.Equal(Ui.HandAction.Lifted, hands.ClickIn(pane.Grids().Single(g => g.Slot == Slot.Inventory), 0, 0).What);
        // (Nobody listens to the host's log here: until then the host made a pack move only inside the log line's
        // text, so with no listener it made none.)
        Assert.True(Until(() => ReferenceEquals(hers.Actor.Held, letter.Item)), "the host never lifted it");
        var copy = her.Actor!.Held!;

        int now = mine!.Milliseconds;
        Assert.Equal(OrderResult.Started, Actions.Put(her, copy, her.Position, now, InputModifier.Space));
        Assert.Equal(now + ForwardedOrders.PutBusyMs, her.Actor.BusyUntil);
        Assert.Equal(OrderResult.Busy, Actions.Put(her, copy, her.Position, now + 100, InputModifier.Space));
        Assert.True(Until(() => letter.InWorld), "the host never put it down");
        Assert.Null(letter.Owner);
    }
}
