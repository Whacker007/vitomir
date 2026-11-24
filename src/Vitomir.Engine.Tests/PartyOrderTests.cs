// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The party put in a new order by dragging a companion's portrait along the bar (ours, <c>partyorder</c>): over the middle
/// of another companion's portrait the two swap; between two portraits it goes in between; anywhere else nothing. The
/// leader is always the first, and the order is the world's own, so the rows, the keys, the formation and the save follow.
/// </summary>
public class PartyOrderTests
{
    /// <summary>Four rows of hotspots 60 wide, 80 apart, as the bar lays a party of four out (the middles at 70, 150, 230, 310).</summary>
    static readonly PartySlot[] Four =
        [new(40, 4, 60, 80), new(120, 4, 60, 80), new(200, 4, 60, 80), new(280, 4, 60, 80)];

    [Fact]
    public void Over_the_middle_of_another_companion_swaps_and_between_two_goes_in_between()
    {
        // The middle half of the narrower of the hotspot and the step: 15 either side of a middle.
        Assert.Equal(new PartyDrop(PartyDropKind.Swap, 3), PartyDrag.At(Four, 1, 310, 40));
        Assert.Equal(new PartyDrop(PartyDropKind.Swap, 3), PartyDrag.At(Four, 1, 296, 40));
        Assert.Equal(new PartyDrop(PartyDropKind.Swap, 2), PartyDrag.At(Four, 1, 244, 40));
        // Between rows 2 and 3, and past the last one's middle: in front of row 3, and at the end.
        Assert.Equal(new PartyDrop(PartyDropKind.Insert, 3), PartyDrag.At(Four, 1, 270, 40));
        Assert.Equal(new PartyDrop(PartyDropKind.Insert, 4), PartyDrag.At(Four, 1, 330, 40));
        // Between the leader and the first companion: first after the leader.
        Assert.Equal(new PartyDrop(PartyDropKind.Insert, 1), PartyDrag.At(Four, 3, 110, 40));
    }

    [Fact]
    public void Over_the_leader_over_itself_beside_itself_and_off_the_portraits_is_nothing()
    {
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 70, 40));     // the leader's middle
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 45, 40));     // left of the leader's middle
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 230, 40));    // its own middle
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 190, 40));    // just left of its own row: where it is
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 270, 40));    // just right of it: where it is
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 150, 90));    // below the portraits
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 150, 2));     // above them
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 30, 40));     // left of the row
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 2, 350, 40));    // right of it
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four[..2], 1, 70, 40));   // a party of two has no order to choose
        Assert.Equal(PartyDrop.None, PartyDrag.At(Four, 0, 230, 40));    // the leader is never carried
    }

    [Fact]
    public void A_swap_trades_two_places_and_an_insert_moves_everybody_between_along_one()
    {
        string[] now = ["C", "A", "B", "D"];
        string[] Order(int carried, PartyDrop drop) =>
            [.. PartyDrag.Order(now.Select(Thing).ToList(), carried, drop).Select(t => t.Template)];
        Assert.Equal(["C", "D", "B", "A"], Order(1, new(PartyDropKind.Swap, 3)));
        Assert.Equal(["C", "B", "A", "D"], Order(1, new(PartyDropKind.Insert, 3)));
        Assert.Equal(["C", "B", "D", "A"], Order(1, new(PartyDropKind.Insert, 4)));
        Assert.Equal(["C", "D", "A", "B"], Order(3, new(PartyDropKind.Insert, 1)));
        Assert.Equal(now, Order(2, PartyDrop.None));
        Assert.Equal(now, Order(2, new(PartyDropKind.Swap, 0)));        // never with the leader
    }

    static readonly Dictionary<string, WorldThing> things = [];
    static WorldThing Thing(string tag) =>
        things.TryGetValue(tag, out var t) ? t : things[tag] = new WorldThing
        {
            Index = 0, Guid = things.Count + 1, Scene = Vitomir.Formats.NsdThing.Placed(tag, Vector3.Zero, Vector3.Zero, 0), Template = tag,
        };

    [Fact]
    public void A_press_lifts_only_a_companion_of_a_party_of_three_or_more_with_an_empty_hand_and_ctrl_up()
    {
        var party = new[] { Thing("C"), Thing("A"), Thing("B") };
        Assert.True(PartyDrag.Lifts(1, party, party, cursorBusy: false, ctrl: false));
        Assert.False(PartyDrag.Lifts(0, party, party, false, false));                 // the leader
        Assert.False(PartyDrag.Lifts(1, party, party, cursorBusy: true, ctrl: false)); // the hand-over click
        Assert.False(PartyDrag.Lifts(1, party, party, false, ctrl: true));            // the group toggle
        Assert.False(PartyDrag.Lifts(1, party[..2], party[..2], false, false));       // two
        Assert.False(PartyDrag.Lifts(1, [Thing("P"), .. party], party, false, false)); // a possession's row in front

        var drag = new PartyDrag();
        drag.Press(party[1], 1, 100, 100, 150, 40);
        Assert.False(drag.Move(102.9f, 97.1f));
        Assert.False(drag.Moving);
        Assert.True(drag.Move(100, 97));
        Assert.True(drag.Moving);
        drag.Clear();
        Assert.False(drag.Held);
    }

    /// <summary>Christof, Wilhem, Serena and Erik at the monastery, each put in the party the way a script does.</summary>
    internal static (Location Place, Minds Minds, WorldThing[] Party) Four_at_the_monastery(GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        lead.Actor!.Flags |= Actor.Flag.Party;
        string[] companions = ["Wilhem", "Serena", "Erik"];
        for (int i = 0; i < companions.Length; i++)
        {
            var c = place.Place(companions[i], lead.Position + new Vector3(96f * (i + 1), 0f, 0f), lead.Orientation, lead.Sector);
            Assert.True(c is not null, $"no {companions[i]}");
            c!.Actor!.Flags |= Actor.Flag.Party;
            c.Actor.Flags2 |= Actor.Flag2.InFormation;
        }
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var party = place.PartyMembers().ToArray();
        Assert.Equal(4, party.Length);
        Assert.Same(lead, party[0]);
        return (place, minds, party);
    }

    [Fact]
    public void A_new_order_is_the_worlds_own_and_the_keys_the_formation_and_tab_follow_it()
    {
        using var files = Install.Open();
        var (place, _, p) = Four_at_the_monastery(files);
        var others = place.Things.Where(t => !p.Contains(t)).ToList();
        var order = new[] { p[0], p[3], p[1], p[2] };

        Assert.True(place.ReorderParty(order));
        Assert.Equal(order, place.PartyMembers());
        Assert.Equal(others, place.Things.Where(t => !p.Contains(t)));    // nothing else in the list moved
        Assert.Equal(p.Length + others.Count, place.Things.Count);

        // F7 is the second row: now the one that was last.
        Assert.True(place.SteerSlot(1));
        Assert.Same(p[3], place.Steered);
        place.Steer(p[0]);
        // The formation's slots go by the order (the selected one, the leader, not counted; the first follower is 0).
        Assert.Equal(0, Minds.PartySlotOf(place, p[3]));
        Assert.Equal(2, Minds.PartySlotOf(place, p[2]));
        // And Tab goes round it.
        Assert.True(place.SteerNext(skipDead: false));
        Assert.Same(p[3], place.Steered);
    }

    [Fact]
    public void An_order_that_is_not_the_party_with_its_leader_first_changes_nothing()
    {
        using var files = Install.Open();
        var (place, _, p) = Four_at_the_monastery(files);
        var before = place.Things.ToList();
        Assert.False(place.ReorderParty([p[1], p[0], p[2], p[3]]));        // the leader moved
        Assert.False(place.ReorderParty([p[0], p[1], p[2]]));              // somebody missing
        Assert.False(place.ReorderParty([p[0], p[1], p[1], p[2]]));        // somebody twice
        var stranger = place.Things.First(t => t.IsActor && !p.Contains(t));
        Assert.False(place.ReorderParty([p[0], p[1], p[2], stranger]));    // somebody else
        Assert.False(place.ReorderParty(p));                               // the same order
        Assert.Equal(before, place.Things);
    }

    /// <summary>Under the switch a portrait's click reaches the top of the head drawn over it, and a little either side; without it, 1.1's hotspot.</summary>
    [Fact]
    public void A_portrait_s_click_reaches_its_whole_head_under_the_switch()
    {
        using var files = Install.Open();
        var (place, _, _) = Four_at_the_monastery(files);
        var pane = new UiAssets(files).Pane(StatusBar.PaneName);
        foreach (bool on in new[] { true, false })
        {
            place.Qol = new Qol { PartyOrder = on };
            var bar = new StatusBar(pane, place);
            bar.Update();
            var h = bar["statusHotspot1"]!;
            int top = StatusBar.HotspotY + h.Height + StatusBar.HeadReachUp;           // y = 92, under the text box at 94
            Assert.Equal(92, top);
            Assert.Equal(on ? 1 : -1, bar.HeadAt(h.X + h.Width / 2, top));
            Assert.Equal(on ? 1 : -1, bar.HeadAt(h.X - StatusBar.HeadReach, StatusBar.HotspotY + 1));
            Assert.Equal(on, ReferenceEquals(bar.At(h.X + h.Width / 2, top), h));
            Assert.Equal(-1, bar.HeadAt(h.X + h.Width / 2, top + 1));
            Assert.Equal(h.Height + (on ? StatusBar.HeadReachUp : 0), bar.HeadSlots[1].Height);
        }
    }

    [Fact]
    public void The_bar_lays_its_rows_out_in_the_preview_while_the_portraits_stay_put()
    {
        using var files = Install.Open();
        var (place, _, p) = Four_at_the_monastery(files);
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        bar.Update();
        Assert.Equal(p, bar.PartyThings);
        var slots = bar.HeadSlots;
        Assert.Equal(4, slots.Count);
        Assert.True(slots[0].Middle < slots[1].Middle && slots[2].Middle < slots[3].Middle);

        // Each portrait's hotspot answers for its row.
        for (int row = 0; row < 4; row++)
            Assert.Equal(row, bar.HeadAt((int)slots[row].Middle, slots[row].Y + 1));

        var preview = new[] { p[0], p[2], p[3], p[1] };
        bar.Preview = preview;
        bar.Update();
        Assert.Equal(preview, bar.PartyThings);
        Assert.Equal(slots, bar.HeadSlots);                                // the places do not move
        Assert.Equal(p, place.PartyMembers());                             // and the party is as it was

        // A preview that is not the party's members any more is dropped.
        bar.Preview = [p[0], p[2], p[1]];
        bar.Update();
        Assert.Null(bar.Preview);
        Assert.Equal(p, bar.PartyThings);
    }

    [Fact]
    public void Dropping_a_portrait_over_another_on_the_bar_swaps_them_and_the_panes_stay_on_their_subject()
    {
        using var files = Install.Open();
        var (place, _, p) = Four_at_the_monastery(files);
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        place.Steer(p[1]);                                                 // panes open on the second
        bar.Update();
        var slots = bar.HeadSlots;
        var drop = PartyDrag.At(slots, 1, (int)slots[3].Middle, slots[3].Y + 10);
        Assert.Equal(new PartyDrop(PartyDropKind.Swap, 3), drop);
        Assert.True(place.ReorderParty(PartyDrag.Order(place.PartyMembers().ToList(), 1, drop)));
        bar.Update();
        Assert.Equal(new[] { p[0], p[3], p[2], p[1] }, bar.PartyThings);
        // Steering did not change, so whatever is open on them stays on them, and their row is still the lit one.
        Assert.Same(p[1], place.Steered);
        Assert.Same(p[1].Actor, bar.Steered);
        Assert.Equal(1, bar["statusIndicators3"]!.Image);
    }
}
