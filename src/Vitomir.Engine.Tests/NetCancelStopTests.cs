// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The cancel's stop from a player's machine** (the cancel's tail; the
/// orders). On a player's machine a cancel that found its character doing something, not asked by a feed's
/// release, of the character the machine steers, sends the host order 0, which the host carries
/// out as a cancel and a stop of the walk. The character is doing something there because the host's
/// walk says so (mode 1), here the snapshot's `ThingState.Moving`. So a use clicked while walking
/// sends a stop and then the use; one clicked standing sends the use alone; and a host's character a player stops that
/// way stands where it was.
/// </summary>
public class NetCancelStopTests
{
    sealed class Sink : IOrderSink
    {
        public readonly List<NetOrder> Orders = [];
        public void Send(NetOrder order) => Orders.Add(order);
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    static bool Until(Func<bool> done, params Action[] turns)
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

    [Fact]
    public void A_use_clicked_while_walking_sends_a_stop_before_the_use_and_one_clicked_standing_the_use_alone()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var me = who.Actor!;
        var prop = place.Things.First(t => t.Type == ThingType.Prop && t.InWorld && !t.Has(WorldThing.Flag.Remove));
        var other = place.Things.First(t => t.Type == ThingType.Actor && t.InWorld && t.Actor is not null);

        var sink = new Sink();
        NetOrders.Attach(place, NetMode.Client, sink);
        try
        {
            // Standing (case 0): the use goes alone.
            Assert.Equal(OrderResult.Started, Actions.Use(who, prop, 0));
            Assert.Equal([OrderKind.Use], sink.Orders.Select(o => o.Kind));

            // Walking by the host's word: the use's cancel tells the host first.
            me.BusyUntil = 0;                                                     // the first use's 250 ms, gone by
            me.Action = ActionMode.Move;
            Assert.Equal(OrderResult.Started, Actions.Use(who, prop, 1000));
            Assert.Equal([OrderKind.Use, OrderKind.Stop, OrderKind.Use], sink.Orders.Select(o => o.Kind));
            Assert.Equal(who.Guid, sink.Orders[1].Actor);
            Assert.Equal(ActionMode.None, me.Action);

            // A feed's release (the cancel's first argument 1) sends nothing; nor does a cancel of anybody else here.
            me.Action = ActionMode.Move;
            Actions.Cancel(who, force: true, release: true);
            other.Actor!.Action = ActionMode.Move;
            Actions.Cancel(other, force: true);
            Assert.Equal(3, sink.Orders.Count);
        }
        finally { NetOrders.Detach(place); }

        // Not a player's machine: nothing to send.
        me.Action = ActionMode.Move;
        Actions.Cancel(who, force: true);
        Assert.Equal(3, sink.Orders.Count);
    }

    [Fact]
    public void A_player_stopping_her_walk_on_her_machine_stops_it_on_the_host()
    {
        using var files = Install.Open();
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var host = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        host.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(host, null);
        var sim = new Simulation(host);
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                          new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        Location? mine = null;
        Simulation? herSim = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
            herSim = new Simulation(mine);
            client.Enter(mine, mine.Player?.Thing);
        };
        int ms = 0;
        void All() { sim.Step(1f / 30); ms += 33; server.Poll(ms); client.Poll(1f / 30); herSim?.Step(1f / 30); }
        Assert.True(Until(() => client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null), All), "never played");
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        var copy = mine!.Player!;

        // A click on the floor far off: the host walks her, and its snapshot puts her copy in the move order there.
        var from = hers.Position;
        var there = FloorNear(host, from, 400f);
        Assert.NotEqual(from, there);
        Assert.True(copy.WalkTo(there, mine.Walk!.Find(there, 256f)));
        Assert.True(Until(() => hers.Actor!.Action == ActionMode.Move && copy.Thing.Actor!.Action == ActionMode.Move, All),
                    "her machine never had her walking");

        // Her machine cancels (an order's cancel): the host is told to stop, and she stands short of the spot.
        Actions.Cancel(copy.Thing);
        Assert.Equal(ActionMode.None, copy.Thing.Actor!.Action);
        Assert.True(Until(() => hers.Actor!.Action == ActionMode.None && hers.Locomotion?.Walking != true, All), "the host never stopped her");
        var stood = hers.Position;
        for (int i = 0; i < 15; i++) All();
        Assert.True(Vector3.Distance(stood, hers.Position) < 1f, "she walked on");
        Assert.True(Vector3.Distance(hers.Position, there) > 20f, "she got there before the stop");
        Assert.Equal(ActionMode.None, copy.Thing.Actor!.Action);                  // the host's snapshot agrees
    }

    static Vector3 FloorNear(Location place, Vector3 from, float distance)
    {
        var map = place.Walk!;
        for (int a = 0; a < 16; a++)
        {
            float r = a * MathF.PI / 8;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * distance;
            int node = map.Find(p, 256f);
            if (place.Things.Any(t => t.Collide != CollideShape.None && !t.Has(WorldThing.Flag.DisableCollide)
                                      && Vector2.Distance(new(t.Position.X, t.Position.Y), new(p.X, p.Y)) < t.Radius + 48f)) continue;
            if (map.IsGround(node) && map.Nodes[node].Contains(new Vector2(p.X, p.Y))) return p with { Z = map.Nodes[node].Height(p.X, p.Y) };
        }
        return from;
    }
}
