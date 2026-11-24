// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The take, the use and the put-down, where a comparison with 1.1 found them short**:
/// a quest item picked up is destroyed, a pick-up to a full hand does nothing,
/// a repeat click on the item being walked to is ignored, shift does not make a take run,
/// space gives a take or a use out of reach up, and a put-down
/// whose item has gone shows the refusal cursor at the point.
/// </summary>
public class TakeRulesTests
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

    /// <summary>
    /// **The goblet of Ecaterina's blood** is a quest item (`EcaterinaVitae`, type 7), and so are the
    /// Lamia skull and the bone key: picked up, the item's `pickup` script has its say and then the
    /// cascade destroys it — nobody carries it, and its quest's picture in the
    /// quest pane is what the player sees of it. Vitomir kept it in the inventory with no cell.
    /// </summary>
    [Fact]
    public void A_quest_item_picked_up_is_destroyed_after_its_script_has_its_say()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var goblet = place.Place("EcaterinaVitae", who.Position, default, who.Sector)!;
        var item = goblet.Item!;
        Assert.Equal(ItemType.Quest, item.Type);
        int guid = goblet.Guid, carried = who.Actor!.Inventory.Items.Count;
        bool asked = false;
        var scripts = new Ground.Scripts(Pickup: (thing, picker) => { asked = thing == goblet && goblet.InWorld; return true; });

        Assert.Equal(GroundResult.Destroyed,
                     Ground.Take(place, who, goblet, InputModifier.None, steering: true, nowMs: 0, scripts));
        Assert.True(asked);                                   // on the floor still, when it was asked
        Assert.True(item.Destroyed);
        Assert.False(goblet.InWorld);
        Assert.Null(place.Thing(guid));
        Assert.Equal(carried, who.Actor.Inventory.Items.Count);
        Assert.DoesNotContain(item, who.Actor.Inventory.Items);
        Assert.Null(who.Actor.Held);
    }

    /// <summary>
    /// **A pick-up to the cursor with the hand already full does nothing**: alt chooses the
    /// cursor's branch whatever is in the hand, and there a full hand ends the take — the noise, the
    /// order over, the item left where it lay. Vitomir put it in the pack instead.
    /// </summary>
    [Fact]
    public void Alt_with_the_hand_full_leaves_the_item_on_the_floor()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var (_, inHand) = Carried(place, who, "potionhealth");
        Ground.Hand(who, inHand);
        var sword = place.Place("broadsword", who.Position, default, who.Sector)!;
        sword.Flags |= WorldThing.Flag.Targetted;
        int carried = who.Actor!.Inventory.Items.Count;

        Assert.Equal(GroundResult.Left,
                     Ground.Take(place, who, sword, InputModifier.Alt, steering: true, nowMs: 0));
        Assert.True(sword.InWorld);
        Assert.Null(sword.Owner);
        Assert.Same(inHand, who.Actor.Held);
        Assert.Equal(carried, who.Actor.Inventory.Items.Count);
        Assert.False(sword.Has(WorldThing.Flag.Targetted));   // The untarget runs anyway

        // Without alt the same full hand is no obstacle: the cascade puts it in the pack.
        Assert.Equal(GroundResult.Stowed,
                     Ground.Take(place, who, sword, InputModifier.None, steering: true, nowMs: 0));
    }

    /// <summary>
    /// **A second click on the item already being walked to is ignored**: no cancel, no
    /// new route. And **shift does not make it run**: the take's speed is the distance's.
    /// </summary>
    [Fact]
    public void A_repeat_click_is_ignored_and_shift_does_not_run()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, "broadsword");
        var taker = Walker(place, who, who.Position);
        var me = taker.Actor!;
        // Dropped 150 units from the taker, on ground it can walk to: within the 300 that walks.
        var at = Enumerable.Range(0, 12)
            .Select(i => taker.Position + 150f * new Vector3(MathF.Cos(i * MathF.PI / 6f), MathF.Sin(i * MathF.PI / 6f), 0f))
            .First(p => place.Walk!.Find(p) >= 0);
        Ground.Drop(place, who, item, nowMs: 0, at);

        Assert.Equal(OrderResult.Started, Actions.Fetch(taker, thing, nowMs: 0, InputModifier.Shift));
        Assert.Equal(me.WalkSpeed * me.MoveSpeedScale, me.ActionSpeed);
        int retry = me.ActionRetryAt;

        Assert.Equal(OrderResult.Ignored, Actions.Fetch(taker, thing, nowMs: 50));
        Assert.Equal(ActionMode.Take, me.Action);
        Assert.Equal(retry, me.ActionRetryAt);                // not ordered again
    }

    /// <summary>
    /// **Space gives a take out of reach up**: the order's first tick has already ordered
    /// the walk, and the unforced cancel ends the order, not the walk — the character goes over and
    /// stands there. The order answers 0.
    /// </summary>
    [Fact]
    public void Space_gives_a_take_out_of_reach_up()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var (thing, item) = Carried(place, who, "broadsword");
        Ground.Drop(place, who, item, nowMs: 0);
        var taker = Walker(place, who, thing.Position);

        Assert.Equal(OrderResult.Ignored, Actions.Fetch(taker, thing, nowMs: 0, InputModifier.Space));
        Assert.Equal(ActionMode.None, taker.Actor!.Action);
        Assert.True(thing.InWorld);

        // In reach, space changes nothing: the first tick takes it.
        taker.Position = thing.Position;
        Assert.Equal(OrderResult.Started, Actions.Fetch(taker, thing, nowMs: 10, InputModifier.Space));
        Assert.False(thing.InWorld);
    }

    /// <summary>
    /// **The use has the same space rule**, and its first tick orders no walk — so a use
    /// or a talk with space held out of reach does nothing at all.
    /// </summary>
    [Fact]
    public void Space_gives_a_use_out_of_reach_up_without_a_step()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var user = Walker(place, who, who.Position);

        Assert.Equal(OrderResult.Ignored, Actions.Use(user, who, nowMs: 0, mods: InputModifier.Space));
        Assert.Equal(ActionMode.None, user.Actor!.Action);
        Assert.False(user.Mind!.Walker.Walking);
    }

    /// <summary>
    /// **Whoever else was walking to it is cancelled unforced** (the cancel
    /// with (1, 0)): a free one stops, a busy one keeps its order — and finds the item gone at
    /// its next tick. Vitomir cleared both outright (a comparison with 1.1's flag).
    /// </summary>
    [Fact]
    public void A_pick_up_cancels_the_others_walking_to_it_unforced()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var who = place.Player!.Thing;
        var sword = place.Place("broadsword", who.Position, default, who.Sector)!;
        var others = place.Things.Where(t => t.Mind is not null && t.Actor is not null && !ReferenceEquals(t, who)).Take(2).ToArray();
        foreach (var o in others) { o.Actor!.Action = ActionMode.Take; o.Actor.Target = sword; }
        sword.Flags |= WorldThing.Flag.Targetted;
        others[1].Actor!.BusyUntil = place.Milliseconds + 5_000;

        Ground.Take(place, who, sword, InputModifier.None, steering: true, nowMs: place.Milliseconds);
        Assert.Equal(ActionMode.None, others[0].Actor!.Action);
        Assert.Equal(ActionMode.Take, others[1].Actor!.Action);
        Assert.Same(sword, others[1].Actor!.Target);
    }

    /// <summary>
    /// **The put-down's arrival with an item no longer the carrier's** shows the refusal cursor and
    /// its noise and ends the order. It is asked at the point: until then the
    /// carrier walks on.
    /// </summary>
    [Fact]
    public void A_put_down_whose_item_has_gone_is_refused_at_the_point()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new Ai.AiClasses(files));
        var cursor = new Ui.CursorState();
        place.Session = new Session { Here = place, Cursor = cursor };
        var player = place.Player!.Thing;
        var carrier = Walker(place, player, player.Position);
        var (thing, item) = Carried(place, carrier, "potionhealth");
        carrier.Actor!.Held = item;
        var point = player.Position;

        Assert.Equal(OrderResult.Started, Actions.Put(carrier, item, point, nowMs: 0));
        Actions.Tick(carrier, nowMs: 0);
        thing.Owner = null;                                   // a script took it
        carrier.Actor.Held = null;
        Actions.Tick(carrier, nowMs: Actions.ApproachRetryMs);
        Assert.Equal(ActionMode.Drop, carrier.Actor.Action);  // still walking over
        Assert.Equal(0, cursor.Refusals);

        carrier.Position = point;
        Actions.Tick(carrier, nowMs: 2 * Actions.ApproachRetryMs);
        Assert.Equal(ActionMode.None, carrier.Actor.Action);
        Assert.Equal(1, cursor.Refusals);
        Assert.Equal(Ui.CursorId.Negative, cursor.Current);
        Assert.False(thing.InWorld);
    }
}
