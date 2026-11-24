// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The take and the put-down ask "am I there?" on their retry clocks** — a comparison with 1.1 of
/// The take and the put-down's tick. Both test the retry time before anything
/// else, so the reach is asked every 150 ms for a take and every 500 ms for a put-down, not every
/// frame: a character walks on a little and picks up (or puts down) on the tick. Vitomir asked
/// every frame and did it the instant it came within 50 units. The use already had the
/// right order; these pin the other two. And the transfers at the end ask
/// nothing of the character: one made busy on the way still finishes.
/// </summary>
public class ArrivalClockTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    static (WorldThing Thing, Item Item) Carried(Location place, WorldThing who, string template)
    {
        var thing = place.Make(template)!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        Stow.Place(who.Actor, item, Slot.Inventory);
        return (thing, item);
    }

    /// <summary>A character with a walker, standing on walkable ground 200 to 2000 units from <paramref name="from"/>.</summary>
    static WorldThing Walker(Location place, WorldThing except, Vector3 from)
    {
        var other = place.Things.First(t => t.Mind is not null && t.Actor is not null && !ReferenceEquals(t, except));
        other.Position = place.Walk!.Nodes
            .Select((n, i) => (Node: i, n.Outline))
            .Where(n => n.Outline.Length > 0)
            .Select(n => new Vector3(n.Outline[0], place.Walk.Nodes[n.Node].Height(n.Outline[0].X, n.Outline[0].Y)))
            .First(p => Vector3.Distance(p, from) is > 200f and < 2000f);
        other.Mind!.Walker.Ground();
        return other;
    }

    [Fact]
    public void A_take_picks_up_on_its_150_ms_tick_not_the_frame_it_comes_in_reach()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, "broadsword");
        Ground.Drop(place, who, item, nowMs: 0);
        var taker = Walker(place, who, thing.Position);

        Assert.Equal(OrderResult.Started, Actions.Fetch(taker, thing, nowMs: 0));
        Assert.Equal(Actions.TakeRetryMs, taker.Actor!.ActionRetryAt);

        // Arrived early: within reach at 100 ms, and nothing happens until the clock comes round.
        taker.Position = thing.Position;
        Actions.Tick(taker, nowMs: 100);
        Assert.Equal(ActionMode.Take, taker.Actor.Action);
        Assert.True(thing.InWorld);

        Actions.Tick(taker, nowMs: Actions.TakeRetryMs);
        Assert.Equal(ActionMode.None, taker.Actor.Action);
        Assert.False(thing.InWorld);
    }

    /// <summary>
    /// The pick-up itself asks nothing of the taker — the order asked whether it was
    /// free, and the tick asks only that the item is still loose. So a taker flinched on the way (busy
    /// when it arrives) still picks the thing up. Vitomir asked the order's gate again and gave up.
    /// </summary>
    [Fact]
    public void A_taker_made_busy_on_the_way_still_picks_up_on_arrival()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, "broadsword");
        Ground.Drop(place, who, item, nowMs: 0);
        var taker = Walker(place, who, thing.Position);

        Assert.Equal(OrderResult.Started, Actions.Fetch(taker, thing, nowMs: 0));
        taker.Actor!.BusyUntil = 5_000;                  // a flinch on the way
        taker.Position = thing.Position;
        Actions.Tick(taker, nowMs: Actions.TakeRetryMs);

        Assert.False(thing.InWorld);
        Assert.Same(taker, thing.Owner ?? thing.Holder);
    }

    [Fact]
    public void A_put_down_happens_on_its_half_second_tick()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var player = place.Player!.Thing;
        var carrier = Walker(place, player, player.Position);
        var (thing, item) = Carried(place, carrier, "potionhealth");
        carrier.Actor!.Held = item;
        var point = player.Position;

        Assert.Equal(OrderResult.Started, Actions.Put(carrier, item, point, nowMs: 0));
        // The first tick is at once (the order clears the clock): out of reach, it walks and waits.
        Actions.Tick(carrier, nowMs: 0);
        Assert.Equal(ActionMode.Drop, carrier.Actor.Action);
        Assert.Equal(Actions.ApproachRetryMs, carrier.Actor.ActionRetryAt);

        carrier.Position = point;
        Actions.Tick(carrier, nowMs: 400);
        Assert.Equal(ActionMode.Drop, carrier.Actor.Action);
        Assert.False(thing.InWorld);

        // And made busy meanwhile, facing somewhere: the transfer asks nothing of the carrier, and the
        // item lies at the identity, not the carrier's facing.
        carrier.Actor.BusyUntil = 5_000;
        carrier.Orientation = new Vector3(0f, 0f, 135f);
        Actions.Tick(carrier, nowMs: Actions.ApproachRetryMs);
        Assert.Equal(ActionMode.None, carrier.Actor.Action);
        Assert.True(thing.InWorld);
        Assert.Equal(Vector3.Zero, thing.Orientation);
        Assert.Equal(point, thing.Position);
    }
}

/// <summary>
/// The cascade's order for a weapon you throw (read in a comparison with 1.1): wearable things try
/// the pack first and the belt second — **but a weapon whose current attack is THROWN counts as not
/// wearable** (the SDK's QUICKUSE bit), so holy water, greek fire, the knives and the
/// grenades go on the belt first. Vitomir put them in the pack.
/// </summary>
public class CascadeTests
{
    [Fact]
    public void A_thrown_weapon_picked_up_goes_on_the_belt_first()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var who = place.Player!.Thing;
        var flask = place.Make("holyWater")!.Item!;
        Assert.SkipUnless((flask.Slots & Slot.Worn) != 0 && flask.Weapon?.Current.Is(WeaponFlag.Thrown) == true,
                          "holyWater is not a wearable thrown weapon in this install");
        var sword = place.Make("broadsword")!.Item!;

        Assert.Equal(StowResult.Stowed, Stow.Put(who, flask, steering: true));
        Assert.Equal(Slot.QuickUse, flask.Location);
        // And an ordinary weapon still tries the pack first.
        Assert.Equal(StowResult.Stowed, Stow.Put(who, sword, steering: true));
        Assert.Equal(Slot.Inventory, sword.Location);
    }
}
