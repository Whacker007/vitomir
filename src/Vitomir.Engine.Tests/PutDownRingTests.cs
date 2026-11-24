// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A put-down that walks rings the point it walks to** (`Rpg/PutDownOrder.cs`). 1.1's put-down order
/// ends, for the steered character, by setting the walk-target ring at the point (in single
/// player and on a player's machine alike), as the move order does; Vitomir
/// had set it for a move order only. A put-down done at once with space held, and a refused one,
/// return before it and leave no ring.
/// </summary>
public class PutDownRingTests
{
    static (Location Place, Player Walker, Item Item, Simulation Sim) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var walker = place.Player!;
        Assert.True(walker.Ground());
        var who = walker.Thing;
        var thing = place.Make("potionhealth")!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        Stow.Place(who.Actor, item, Slot.Inventory);
        who.Actor.Held = item;
        return (place, walker, item, new Simulation(place));
    }

    /// <summary>The floor under <paramref name="at"/>, as a click straight down on it picks it.</summary>
    static Hit Ground(Location place, Vector3 at)
    {
        var hit = Pick.Nearest(place.Level, at + new Vector3(0, 0, 64), -Vector3.UnitZ);
        Assert.NotNull(hit);
        return hit!.Value;
    }

    [Fact]
    public void A_put_down_that_walks_rings_the_point()
    {
        using var files = Install.Open();
        var (place, walker, item, sim) = Open(files);
        var there = walker.Thing.Position + new Vector3(120, 0, 0);

        Assert.Equal(OrderResult.Started, PutDownOrder.Click(sim, walker.Thing, item, Ground(place, there), nowMs: 0));
        Assert.Equal(ActionMode.Drop, walker.Thing.Actor!.Action);

        Assert.True(walker.Walking);
        Assert.True(sim.WalkTarget.Active);
        Assert.Equal(Ground(place, there).Point, sim.WalkTarget.Point);
    }

    [Fact]
    public void A_put_down_done_at_once_leaves_no_ring()
    {
        using var files = Install.Open();
        var (place, walker, item, sim) = Open(files);
        var near = walker.Thing.Position + new Vector3(30, 0, 0);

        Assert.Equal(OrderResult.Ignored,
                     PutDownOrder.Click(sim, walker.Thing, item, Ground(place, near), nowMs: 0, InputModifier.Space));

        Assert.False(sim.WalkTarget.Active);
    }

    [Fact]
    public void A_refused_put_down_leaves_no_ring()
    {
        using var files = Install.Open();
        var (_, walker, item, sim) = Open(files);
        var nowhere = walker.Thing.Position + new Vector3(0, 0, 2_000);

        Assert.Equal(OrderResult.Refused,
                     PutDownOrder.Click(sim, walker.Thing, item, new Hit(nowhere, -1, -1, -1, false, 100f), nowMs: 0));

        Assert.False(sim.WalkTarget.Active);
    }
    sealed class Sink : IOrderSink
    {
        public readonly List<NetOrder> Orders = [];
        public void Send(NetOrder order) => Orders.Add(order);
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    /// <summary>
    /// On a player's machine the put-down goes to the host (`Net.ForwardedOrders`) and the ring is
    /// set there and then for the machine's own character, before the host has walked it.
    /// </summary>
    [Fact]
    public void A_put_down_sent_from_a_player_s_machine_rings_the_point_there()
    {
        using var files = Install.Open();
        var (place, walker, item, sim) = Open(files);
        var there = walker.Thing.Position + new Vector3(120, 0, 0);
        var sink = new Sink();
        NetOrders.Attach(place, NetMode.Client, sink);
        try
        {
            Assert.Equal(OrderResult.Started, PutDownOrder.Click(sim, walker.Thing, item, Ground(place, there), nowMs: 1000));
            Assert.Equal(OrderKind.Drop, sink.Orders.Single().Kind);
            Assert.True(sim.WalkTarget.Active);
            Assert.Equal(Ground(place, there).Point, sim.WalkTarget.Point);
        }
        finally { NetOrders.Detach(place); }
    }
}
