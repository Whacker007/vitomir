// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Items on the floor. The deferred list called this "the conversion between an `Rpg.Item` record
// and a `WorldThing`"; there is no conversion, because in the original there is no pair —
// `rpgItem` **is** a `simThing`, and whether it is on the floor or in a pack is one pointer,
// its world. These tests are mostly about that: the object, and its GUID, survive the round
// trip untouched.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public class GroundTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    const string Sword = "broadsword", Potion = "potionhealth";

    /// <summary>
    /// **An item template builds an item.** `Location.Build` fills `WorldThing.Item` for the three
    /// item types exactly as it fills `Actor` for the two character types, and the two point at
    /// each other — which is the whole of the join.
    /// </summary>
    [Fact]
    public void An_item_thing_carries_its_item_and_the_item_carries_its_thing()
    {
        using var files = Install.Open();
        var place = Open(files);
        var made = place.Make(Sword);

        Assert.NotNull(made);
        Assert.Equal(ThingType.Weapon, made.Type);
        Assert.NotNull(made.Item);
        Assert.Same(made, made.Item!.Thing);
        Assert.Equal(Sword, made.Item.Name);
        // A weapon item knows what it does, which the kit never gave it before.
        Assert.NotNull(made.Item.Weapon);
        // Made and registered, but nowhere: never entered in a world.
        Assert.False(made.InWorld);
        Assert.Same(made, place.Thing(made.Guid));
    }

    /// <summary>
    /// The round trip, and the thing this test exists to state: **the GUID does not change**.
    /// A save experiment found exactly this from the other side — destroying a barrel
    /// allocated two GUIDs and picking up what it dropped allocated none.
    /// </summary>
    [Fact]
    public void An_item_keeps_its_guid_through_the_floor_and_back()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Sword);
        int guid = thing.Guid;

        Assert.Equal(GroundResult.Dropped, Ground.Drop(place, who, item, nowMs: 0));
        Assert.True(thing.InWorld);
        Assert.Equal(who.Position, thing.Position);
        Assert.Null(thing.Owner);
        Assert.DoesNotContain(item, who.Actor!.Inventory.Items);
        Assert.Equal(guid, thing.Guid);

        Assert.Equal(GroundResult.Stowed,
                     Ground.Take(place, who, thing, InputModifier.None, steering: true, nowMs: 0));
        Assert.False(thing.InWorld);
        Assert.Same(who, thing.Owner);
        Assert.Contains(item, who.Actor.Inventory.Items);
        Assert.Equal(guid, thing.Guid);
        Assert.Same(thing, place.Thing(guid));
    }

    /// <summary>
    /// **Alt puts it on the cursor instead**, which is the pick-up's straight-to-hand branch and needs
    /// all of: the steered character, an empty hand, and the modifier — *or* the inventory pane on
    /// screen, which is the other half of the same condition (see the modality test below).
    /// </summary>
    [Fact]
    public void Alt_takes_it_to_the_hand_and_nothing_else_does()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Potion);
        Ground.Drop(place, who, item, nowMs: 0);

        Assert.Equal(GroundResult.Stowed,
                     Ground.Take(place, who, thing, InputModifier.None, steering: true, nowMs: 0));
        Assert.Null(who.Actor!.Held);

        Ground.Drop(place, who, item, nowMs: 0);
        Assert.Equal(GroundResult.ToHand,
                     Ground.Take(place, who, thing, InputModifier.Alt, steering: true, nowMs: 0));
        Assert.Same(item, who.Actor.Held);
        Assert.Same(who, thing.Holder);
        Assert.Null(thing.Owner);
        Assert.False(thing.InWorld);
    }

    /// <summary>
    /// **A refused pick-up leaves the item where it was.** The cascade adds to the inventory as it
    /// searches, so a failure half way through could very easily lose the thing; it must not, and
    /// this is the test that says so.
    /// </summary>
    [Fact]
    public void Nowhere_to_put_it_leaves_it_on_the_floor()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var actor = who.Actor!;
        var (thing, item) = Carried(place, who, Potion);
        Ground.Drop(place, who, item, nowMs: 0);

        // Fill the pack, the belt and the hand, so every destination the cascade has is gone.
        for (int i = 0; i < 40; i++) Fill(place, who, Slot.Inventory);
        for (int i = 0; i < 5; i++) Fill(place, who, Slot.QuickUse);
        actor.Held = Fill(place, who, Slot.None);

        Assert.Equal(GroundResult.Refused,
                     Ground.Take(place, who, thing, InputModifier.None, steering: true, nowMs: 0));
        Assert.True(thing.InWorld);
        Assert.Null(thing.Owner);
        Assert.DoesNotContain(item, actor.Inventory.Items);
    }

    /// <summary>
    /// **`THING_FLAG_TARGETTED` and the untargeting**: two people walk towards the same sword, one of them
    /// gets there first, and the other stops rather than trudging on to an empty floor.
    /// </summary>
    [Fact]
    public void Taking_something_stops_everybody_who_was_walking_to_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        // Woken up, because the subject here is somebody *walking* to the sword and a character
        // without a mind has no walker to walk with.
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Sword);
        Ground.Drop(place, who, item, nowMs: 0);

        var other = place.Things.First(t => t.Mind is not null && !ReferenceEquals(t, who));
        // Out of arm's reach, or there is no walk to interrupt: **the take order ticks itself once
        // before it walks**, so an item already at your feet is simply picked up and the
        // order is over before this test's subject exists.
        other.Position = place.Walk!.Nodes
            .Select((n, i) => (Node: i, n.Outline))
            .Where(n => n.Outline.Length > 0)
            .Select(n => new Vector3(n.Outline[0], place.Walk.Nodes[n.Node].Height(n.Outline[0].X, n.Outline[0].Y)))
            .First(p => Vector3.Distance(p, thing.Position) is > 200f and < 2000f);
        other.Mind!.Walker.Ground();
        Assert.True(Bodies.Gap(other, thing) > Stow.Reach);

        Assert.Equal(OrderResult.Started, Actions.Fetch(other, thing, nowMs: 0));
        Assert.Equal(ActionMode.Take, other.Actor!.Action);
        Assert.True(thing.Has(WorldThing.Flag.Targetted));

        Ground.Take(place, who, thing, InputModifier.None, steering: true, nowMs: 0);

        Assert.Equal(ActionMode.None, other.Actor.Action);
        Assert.False(thing.Has(WorldThing.Flag.Targetted));
    }

    /// <summary>
    /// **A script can refuse either half.** `pickup` and `dropped` both return a boolean and a
    /// false answer ends the gesture.
    /// </summary>
    [Fact]
    public void A_script_can_veto_a_pick_up_or_a_put_down()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Potion);

        var no = new Ground.Scripts(Pickup: (_, _) => false, Dropped: (_, _) => false);
        Assert.Equal(GroundResult.Refused, Ground.Drop(place, who, item, 0, null, no));
        Assert.False(thing.InWorld);
        Assert.Contains(item, who.Actor!.Inventory.Items);

        Ground.Drop(place, who, item, nowMs: 0);
        Assert.True(thing.InWorld);
        Assert.Equal(GroundResult.Refused,
                     Ground.Take(place, who, thing, InputModifier.None, true, 0, no));
        Assert.True(thing.InWorld);
    }

    /// <summary>
    /// The **order**, not the transfer: the take walks first. And with **space** held the drop order
    /// skips the walk entirely — landing exactly where you clicked within arm's reach, and at the
    /// reach along that direction beyond it, which is `Repairs.DropLanding` and is the one place
    /// this engine deliberately does not do what 1.1 does.
    /// </summary>
    [Fact]
    public void Space_drops_it_at_once_and_never_further_than_the_reach()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Potion);
        who.Actor!.Held = item;

        // Within the reach it lands exactly where it was asked to, and nothing else is tested.
        var near = who.Position + new Vector3(0, 0, 20);
        Assert.Equal(OrderResult.Ignored,
                     Actions.Put(who, item, near, nowMs: 0, InputModifier.Space));
        Assert.True(thing.InWorld);
        Assert.Equal(near, thing.Position);
        Assert.Null(who.Actor.Held);

        // Past it, the landing point is the reach along the click's direction — the repair
        // (`Repairs.DropLanding`), where the original would have thrown it 20,000 units. The
        // engine's own walk-surface test still guards the landing, and one consequence of the
        // repair is that it now almost never fires: a point fifty units away is nearly always the
        // floor you are already standing on. It stays because it is the engine's and because a
        // mod's level can have a fifty-unit hole in it.
        var (second, other) = Carried(place, who, Potion);
        who.Actor.Held = other;
        var far = who.Position + new Vector3(0, 400, 0);
        Assert.Equal(OrderResult.Ignored,
                     Actions.Put(who, other, far, nowMs: 0, InputModifier.Space));
        Assert.True(second.InWorld);
        Assert.Equal(Stow.Reach, Vector3.Distance(who.Position, second.Position), 2);
        Assert.Null(who.Actor.Held);

        // Without space the click is an order and nothing has moved yet.
        var (third, another) = Carried(place, who, Potion);
        who.Actor.Held = another;
        Assert.Equal(OrderResult.Started, Actions.Put(who, another, far, nowMs: 0));
        Assert.Equal(ActionMode.Drop, who.Actor.Action);
        Assert.False(third.InWorld);
    }

    /// <summary>
    /// <see cref="Location.Withdraw"/> is not <see cref="Location.Remove"/>: the thing stops being
    /// in the world and goes on existing, GUID registration included — which is what lets it back
    /// in and what lets a save write a carried item.
    /// </summary>
    [Fact]
    public void Withdrawing_is_not_destroying_and_the_thing_can_come_back()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Potion);
        Ground.Drop(place, who, item, nowMs: 0);
        int count = place.Things.Count;

        Assert.True(place.Withdraw(thing));
        Assert.False(thing.InWorld);
        Assert.False(thing.Removed);
        Assert.Equal(count - 1, place.Things.Count);
        Assert.Same(thing, place.Thing(thing.Guid));       // the GUID table is untouched
        Assert.False(place.Withdraw(thing));               // and asking twice is the engine's 0

        place.Enter(thing);
        Assert.True(thing.InWorld);
        Assert.Equal(count, place.Things.Count);
    }

    /// <summary>
    /// **A drawn batch names its thing by position, so the renderer must not be pointed at the live
    /// list.** Taking something off the floor — or destroying anything — shortens
    /// <see cref="Location.Things"/> and shifts every index after it, which would draw each later
    /// thing at its neighbour's transform. `Location.Drawn` is the array the mesh was built from
    /// and it never shifts; the objects in it are the same objects, so a thing that moves still
    /// draws where it is.
    ///
    /// This was reachable from the moment things could be destroyed at run time and nobody had
    /// looked; picking items up would have made it an everyday bug.
    /// </summary>
    [Fact]
    public void What_the_renderer_draws_does_not_shift_when_something_leaves_the_world()
    {
        using var files = Install.Open();
        var place = Open(files);
        var drawn = place.Drawn.ToArray();
        Assert.NotEmpty(drawn);

        // Every batch's index has to keep naming the thing it was built for.
        var named = place.Mesh.Batches.Where(b => b.Thing >= 0).Select(b => drawn[b.Thing]).ToList();
        Assert.NotEmpty(named);

        var victim = place.Things.First(t => t.Type == ThingType.Prop);
        place.Remove(victim);

        // Destroying a thing leaves it in `Drawn`, holding its place. It stops being drawn because
        // it is out of the world, which is the renderer's test, not because it left the array.
        Assert.Equal(drawn, place.Drawn);
        Assert.False(victim.InWorld);
        Assert.Equal(named, place.Mesh.Batches.Where(b => b.Thing >= 0).Select(b => place.Drawn[b.Thing]));
        // And the live list really did shrink, which is what would have shifted them.
        Assert.DoesNotContain(victim, place.Things);
    }

    /// <summary>
    /// **The crash found in the first minute of play.** Every per-frame stage walks
    /// <see cref="Location.Things"/>, and the arrival of a ground order makes or unmakes a thing —
    /// so the drop added to the list the action stage was in the middle of enumerating and .NET
    /// threw. It was not specific to items: an effect tick that kills somebody spawns their
    /// skeleton and would have done the same, and the corpse step had already met it and worked
    /// around it locally.
    ///
    /// The engine gets this for free twice over — its things are a linked list, and
    /// `THING_FLAG_REMOVE`'s own comment in `Codex.java` reads *"Remove object at start of next
    /// frame (for safe removals)"* — so deferring to a safe point is its answer too.
    /// <see cref="Location.Snapshot"/> is ours.
    /// </summary>
    [Fact]
    public void An_order_arriving_inside_a_step_does_not_break_the_walk()
    {
        using var files = Install.Open();
        var place = Open(files);
        var sim = new Simulation(place);
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, Potion);
        who.Actor!.Held = item;

        // The point is already within reach, so the very first tick of the order arrives — which is
        // the frame that mutates the list mid-walk. Not his own feet: the order walks as it is given,
        // and a walk shorter than 0.1 is refused (PutRoute).
        Assert.Equal(OrderResult.Started, Actions.Put(who, item, who.Position + new Vector3(10, 0, 0), nowMs: 0));
        sim.Step(1f / 60f);
        Assert.True(thing.InWorld);
        Assert.Equal(ActionMode.None, who.Actor.Action);

        // And the other way: the take withdraws it, which shortens the same list.
        Assert.Equal(OrderResult.Started, Actions.Fetch(who, thing, sim.Milliseconds));
        sim.Step(1f / 60f);
        Assert.False(thing.InWorld);
        Assert.Equal(ActionMode.None, who.Actor.Action);
        Assert.Contains(item, who.Actor.Inventory.Items);
    }

    /// <summary>
    /// **The base game's behaviour, which is the pick-up's condition read from the other
    /// side**: without the inventory window open, an item clicked on the ground
    /// to pick up goes to the inventory by itself if there is space; if there is not, it
    /// goes to the cursor buffer.
    ///
    /// All three branches, in one test. The pane half was once written down as a
    /// nameless "option bit (the pane's bit 0)"; it is the inventory pane's shown
    /// flag plus the test that the menu is one of the panes actually up.
    /// </summary>
    [Fact]
    public void With_the_pack_open_a_pick_up_reaches_for_the_cursor_instead()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var actor = who.Actor!;
        var (thing, item) = Carried(place, who, Potion);

        // Pane closed, room in the pack: into the pack.
        Ground.Drop(place, who, item, nowMs: 0);
        Assert.Equal(GroundResult.Stowed,
                     Ground.Take(place, who, thing, InputModifier.None, true, 0, null, paneOpen: false));
        Assert.Null(actor.Held);

        // Pane open, same click: onto the cursor.
        Ground.Drop(place, who, item, nowMs: 0);
        Assert.Equal(GroundResult.ToHand,
                     Ground.Take(place, who, thing, InputModifier.None, true, 0, null, paneOpen: true));
        Assert.Same(item, actor.Held);
        Assert.DoesNotContain(item, actor.Inventory.Items);      // the hand is outside the list

        // Pane closed and nowhere to put it: the cascade falls through to the cursor anyway.
        Ground.Drop(place, who, item, nowMs: 0);
        for (int i = 0; i < 40; i++) Fill(place, who, Slot.Inventory);
        for (int i = 0; i < 5; i++) Fill(place, who, Slot.QuickUse);
        Assert.Equal(GroundResult.ToHand,
                     Ground.Take(place, who, thing, InputModifier.None, true, 0, null, paneOpen: false));
        Assert.Same(item, actor.Held);
    }

    /// <summary>
    /// **The vanishing letter, end to end.** Take it to the cursor off the floor, put it in the
    /// quick-use belt through the pane, and it has to be somewhere both the strip and the save can
    /// see it: in the character's inventory list, at that cell, owned by them and held by nobody.
    /// </summary>
    [Fact]
    public void An_item_taken_to_the_cursor_and_put_in_the_belt_is_still_carried()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var actor = who.Actor!;
        var (thing, item) = Carried(place, who, Potion);
        Ground.Drop(place, who, item, nowMs: 0);

        Assert.Equal(GroundResult.ToHand,
                     Ground.Take(place, who, thing, InputModifier.Alt, steering: true, nowMs: 0));
        Assert.Same(who, thing.Holder);
        Assert.Null(thing.Owner);

        Ground.Keep(who, item, Slot.QuickUse, (0, 0));

        Assert.Null(actor.Held);
        Assert.Null(thing.Holder);
        Assert.Same(who, thing.Owner);
        Assert.Contains(item, actor.Inventory.Items);
        Assert.Equal(Slot.QuickUse, item.Location);
        Assert.Equal((0, 0), item.Cell);
        Assert.False(thing.InWorld);
    }

    /// <summary>
    /// **The animation stage indexes by position too, and it is the worse of the two.** An
    /// `AnimatedThing` names its thing by the slot the mesh builder recorded, so reading the *live*
    /// list advances the wrong thing's animation once anything has left the world — and because
    /// `Simulation.Land` is driven off the `Strike` tag that walk crosses, a blow would land on
    /// whoever shifted into the slot. The renderer's version of this was found first and fixed;
    /// this one survived the same batch because nothing was looking at it.
    /// </summary>
    [Fact]
    public void The_animation_stage_indexes_the_array_the_mesh_was_built_from()
    {
        using var files = Install.Open();
        var place = Open(files);
        Assert.NotEmpty(place.Mesh.Animated);

        var named = place.Mesh.Animated.Select(a => place.Drawn[a.Thing]).ToList();
        var victim = place.Things.First(t => t.Type == ThingType.Prop);
        place.Remove(victim);

        // Same things, same order, after the live list has shortened under it.
        Assert.Equal(named, place.Mesh.Animated.Select(a => place.Drawn[a.Thing]));
        foreach (var a in place.Mesh.Animated) Assert.InRange(a.Thing, 0, place.Drawn.Count - 1);
    }

    // --- fixtures -----------------------------------------------------------------------------

    /// <summary>An item of <paramref name="template"/> in <paramref name="who"/>'s pack.</summary>
    static (WorldThing Thing, Item Item) Carried(Location place, WorldThing who, string template)
    {
        var thing = place.Make(template)!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        Stow.Place(who.Actor, item, Slot.Inventory);
        return (thing, item);
    }

    /// <summary>One more item, wherever it is asked for, to block the cascade's destinations.</summary>
    static Item Fill(Location place, WorldThing who, int slot)
    {
        var thing = place.Make(Potion)!;
        var item = thing.Item!;
        thing.Owner = who;
        who.Actor!.Inventory.Add(item);
        if (slot != Slot.None) Stow.Place(who.Actor, item, slot);
        return item;
    }
}
