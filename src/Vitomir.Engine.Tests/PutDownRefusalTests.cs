// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A refused put-down answers the click and does not walk** (*"Yes let's match
/// it."*). The put-down order walks as it is given and refuses a point it has no way to (<see cref="PutRoute"/>),
/// before its ACTION 0xE is written; the ground click
/// hands that answer to the mouse, and an answer of 1 is the red cross (<see cref="ClickAnswer"/>).
/// Vitomir's order had walked nowhere, its tick had ended it
/// in silence, and the harness had walked the character to the click instead.
/// </summary>
public class PutDownRefusalTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    static (WorldThing Thing, Item Item) Held(Location place, WorldThing who)
    {
        var thing = place.Make("potionhealth")!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        Stow.Place(who.Actor, item, Slot.Inventory);
        who.Actor.Held = item;
        return (thing, item);
    }

    [Fact]
    public void A_put_down_with_no_way_there_is_refused_and_nothing_is_ordered()
    {
        using var files = Install.Open();
        var place = Open(files);
        var walker = place.Player!;
        Assert.True(walker.Ground());                       // standing on the walk map (its surface set)
        var who = walker.Thing;
        var (thing, item) = Held(place, who);

        // A point high in the air over him has no ground under it: the route is refused.
        var nowhere = who.Position + new Vector3(0, 0, 2_000);
        Assert.Equal(OrderResult.Refused, Actions.Put(who, item, nowhere, nowMs: 0));
        Assert.Equal(ActionMode.None, who.Actor!.Action);   // ACTION 0xE never written
        Assert.False(walker.Walking);
        Assert.Same(item, who.Actor.Held);                  // still on the pointer
        Assert.False(thing.InWorld);
        Assert.True(ClickAnswer.RedCross(OrderResult.Refused));
    }

    [Fact]
    public void A_put_down_the_walk_can_reach_walks_as_it_is_ordered()
    {
        using var files = Install.Open();
        var place = Open(files);
        var walker = place.Player!;
        Assert.True(walker.Ground());
        var who = walker.Thing;
        var (_, item) = Held(place, who);

        // A spot on the ground he stands on, out of arm's reach: the order starts the walk itself.
        var there = who.Position + new Vector3(120, 0, 0);
        Assert.True(walker.Map.Find(there) >= 0, "the test's spot must be on the walk map");
        Assert.Equal(OrderResult.Started, Actions.Put(who, item, there, nowMs: 0));
        Assert.Equal(ActionMode.Drop, who.Actor!.Action);
        Assert.True(walker.Walking);
        Assert.False(ClickAnswer.RedCross(OrderResult.Started));
    }

    /// <summary>
    /// Odd, and 1.1's: on the ground he stands on the route is a straight walk, and one
    /// shorter than a tenth of a unit answers 0 — so a put-down at his own feet is refused.
    /// </summary>
    [Fact]
    public void A_put_down_at_the_carriers_own_feet_is_refused()
    {
        using var files = Install.Open();
        var place = Open(files);
        var walker = place.Player!;
        Assert.True(walker.Ground());
        var who = walker.Thing;
        var (_, item) = Held(place, who);

        Assert.Equal(OrderResult.Refused, Actions.Put(who, item, who.Position, nowMs: 0));
        Assert.Equal(ActionMode.None, who.Actor!.Action);
        Assert.Equal(OrderResult.Started, Actions.Put(who, item, who.Position + new Vector3(10, 0, 0), nowMs: 0));
    }

    /// <summary>
    /// The click compares the handler's answer with 1 and nothing else: busy (2) is silence, and so is
    /// an order taken (4) or one with nothing to do (0).
    /// </summary>
    [Fact]
    public void Only_a_refusal_is_the_red_cross()
    {
        Assert.True(ClickAnswer.RedCross(OrderResult.Refused));
        Assert.False(ClickAnswer.RedCross(OrderResult.Busy));
        Assert.False(ClickAnswer.RedCross(OrderResult.Started));
        Assert.False(ClickAnswer.RedCross(OrderResult.Ignored));
    }
}
