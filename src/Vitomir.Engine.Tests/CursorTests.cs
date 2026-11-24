// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Xunit;

namespace Vitomir.Engine.Tests;

public class CursorTests
{
    [Fact]
    public void The_engine_registers_eight_of_the_nine_shipped_cursors()
    {
        // The cursor's set-up loads eight, in this order, and keeps them in this order.
        Assert.Equal(8, Cursors.Count);
        Assert.Equal("cursorDefault", Cursors.Name(CursorId.Default));
        Assert.Equal("cursorAttack", Cursors.Name(CursorId.Attack));
        Assert.Equal("cursorPickup", Cursors.Name(CursorId.Pickup));
        Assert.Equal("cursorTalk", Cursors.Name(CursorId.Talk));
        Assert.Equal("cursorNegative", Cursors.Name(CursorId.Negative));
        Assert.Equal("cursorSelect", Cursors.Name(CursorId.Select));
        Assert.Equal("cursorScroll", Cursors.Name(CursorId.Scroll));
        Assert.Equal("cursorTarget", Cursors.Name(CursorId.Target));
        // The ninth file ships and is never asked for.
        Assert.Equal("cursorUse", Cursors.Unregistered);
    }

    [Fact]
    public void The_four_pointers_anchor_top_left_and_the_four_markers_centre()
    {
        // The last argument of each load: 1 for these four, 0 for the others.
        foreach (var id in new[] { CursorId.Default, CursorId.Attack, CursorId.Pickup, CursorId.Scroll })
            Assert.Equal(CursorAnchor.TopLeft, Cursors.Anchor(id));
        foreach (var id in new[] { CursorId.Talk, CursorId.Negative, CursorId.Select, CursorId.Target })
            Assert.Equal(CursorAnchor.Centre, Cursors.Anchor(id));
    }

    [Fact]
    public void A_hotspot_is_the_corner_or_the_middle_of_the_artwork()
    {
        Assert.Equal((0f, 0f), Cursors.Hotspot(CursorAnchor.TopLeft, 32, 32));
        Assert.Equal((16f, 16f), Cursors.Hotspot(CursorAnchor.Centre, 32, 32));
        Assert.Equal((32f, 0f), Cursors.Hotspot(CursorAnchor.TopRight, 32, 32));
        Assert.Equal((0f, 32f), Cursors.Hotspot(CursorAnchor.BottomLeft, 32, 32));
        Assert.Equal((32f, 32f), Cursors.Hotspot(CursorAnchor.BottomRight, 32, 32));
    }

    [Fact]
    public void A_top_left_anchored_glyph_hangs_below_and_right_of_the_point()
    {
        // `.nui` y counts up, so the quad's bottom-left is a full height below the pointer and the
        // artwork's top-left corner lands exactly on it.
        Assert.Equal((100f, 168f), Cursors.Corner(CursorAnchor.TopLeft, 100, 200, 32, 32));
        Assert.Equal((84f, 184f), Cursors.Corner(CursorAnchor.Centre, 100, 200, 32, 32));
    }

    [Fact]
    public void A_refusal_holds_the_negative_cursor_for_a_quarter_second()
    {
        var state = new CursorState();
        state.Refuse(1_000);
        Assert.Equal(CursorId.Negative, state.Current);
        Assert.Equal(1_250, state.RefusedUntil);

        // It wins over the context for as long as it runs...
        state.Update(1_100, null, CursorId.Attack);
        Assert.Equal(CursorId.Negative, state.Current);
        // ...and over a held item too.
        state.Update(1_249, Sword(), CursorId.Attack);
        Assert.Equal(CursorId.Negative, state.Current);
        Assert.Null(state.Showing);
        // Then the context gets it back.
        state.Update(1_250, null, CursorId.Attack);
        Assert.Equal(CursorId.Attack, state.Current);
    }

    [Fact]
    public void An_item_in_hand_is_the_cursor_and_the_context_stops_mattering()
    {
        var state = new CursorState();
        var sword = Sword();
        state.Update(0, sword, CursorId.Talk);
        Assert.Same(sword, state.Showing);
        // The cursor choice returns before its switch while an icon is up, so the talkable person under the
        // pointer changes nothing.
        Assert.Equal(CursorAnchor.Centre, state.Anchor);
        // And the glyph is the icon's own footprint rather than 32 x 32.
        Assert.Equal((2 * InventoryPane.CellWidth, 3 * InventoryPane.CellHeight), state.Extent);

        state.Update(0, null, CursorId.Talk);
        Assert.Null(state.Showing);
        Assert.Equal(CursorId.Talk, state.Current);
        Assert.Equal((32f, 32f), state.Extent);
    }

    [Fact]
    public void Nothing_under_the_pointer_is_the_plain_cursor()
    {
        Assert.Equal(CursorId.Default, Highlight.Cursor(null, null));
    }

    [Fact]
    public void An_item_on_the_ground_puts_up_the_pickup_hand()
    {
        foreach (var type in new[] { ThingType.Item, ThingType.Weapon, ThingType.Armor })
            Assert.Equal(CursorId.Pickup, Highlight.Cursor(Bare(type), null));
    }

    [Fact]
    public void A_prop_and_a_container_keep_the_plain_cursor_even_though_a_click_does_something()
    {
        // The two switches disagree on purpose: the verb choice gives these a verb, the cursor choice has no case.
        var prop = Bare(ThingType.Prop);
        var crate = Bare(ThingType.Container);
        Assert.Equal(CursorId.Default, Highlight.Cursor(prop, null));
        Assert.Equal(CursorId.Default, Highlight.Cursor(crate, null));
        Assert.Equal(CursorVerb.Use, Cursor.Verb(Bare(ThingType.Player), prop));
    }

    [Fact]
    public void A_talkable_person_beats_a_liftable_one_and_both_beat_the_blade()
    {
        // Team 0 is the player's own; team 12 is the first enemy band.
        var me = Person(0, team: Actor.Teams.PlayerFirst);
        int foe = Actor.Teams.EnemyFirst;
        Assert.Equal(CursorId.Talk, Highlight.Cursor(Person(Highlight.TalkTo | Highlight.Liftable, foe), me));
        Assert.Equal(CursorId.Pickup, Highlight.Cursor(Person(Highlight.Liftable, foe), me));
        Assert.Equal(CursorId.Attack, Highlight.Cursor(Person(0, foe), me));
        // Somebody on your own side is not a target: the switch sets nothing.
        Assert.Null(Highlight.Cursor(Person(0, Actor.Teams.PlayerFirst), me));
    }

    [Fact]
    public void A_staked_friend_leaves_the_cursor_as_it_was()
    {
        // In 1.1, with Wilhem staked with `stakeme`, Christof
        // steered and the pointer on Wilhem, the pointer stays the plain one the whole hover, and
        // the click unstakes him. The verb is unstake, so the pointer lands on him; the cursor choice's
        // switch then has nothing to set for a friend and returns, and the cursor keeps what it had.
        var me = Person(0, Actor.Teams.PlayerFirst);
        var wilhem = Person(0, Actor.Teams.PlayerFirst);
        wilhem.Actor!.Flags |= Actor.Flag.Staked;
        Assert.Equal(CursorVerb.Unstake, Cursor.Verb(me, wilhem));
        Assert.Null(Highlight.Cursor(wilhem, me));

        var state = new CursorState();
        state.Update(0, null, Highlight.Cursor(null, me));                  // from the ground
        state.Update(16, null, Highlight.Cursor(wilhem, me));
        Assert.Equal(CursorId.Default, state.Current);

        state.Update(32, null, Highlight.Cursor(Person(0, Actor.Teams.EnemyFirst), me));
        state.Update(48, null, Highlight.Cursor(wilhem, me));               // swept straight off an enemy
        Assert.Equal(CursorId.Attack, state.Current);

        state.Refuse(64);                                                   // a refusal that runs out
        state.Update(64 + Cursors.RefuseMs, null, Highlight.Cursor(wilhem, me));
        Assert.Equal(CursorId.Default, state.Current);                      // puts the plain one back
    }

    [Fact]
    public void The_highlight_colours_are_the_ones_the_hover_writes_into_the_model()
    {
        Assert.Equal(0xFF600000, Highlight.Colour(CursorVerb.Attack, ThingType.Actor));
        Assert.Equal(0xFF005050, Highlight.Colour(CursorVerb.Take, ThingType.Item));
        Assert.Equal(0xFF000050, Highlight.Colour(CursorVerb.Use, ThingType.Actor));
        Assert.Equal(0xFF505000, Highlight.Colour(CursorVerb.Use, ThingType.Prop));
        Assert.Equal(0xFF104040, Highlight.Colour(CursorVerb.None, ThingType.Region));
    }

    static Item Sword() => new() { Name = "Fine Rapier", Icon = "rapierIcon", IconSize = 5 };

    static WorldThing Person(uint flags, int team = Actor.Teams.None) =>
        Bare(ThingType.Actor, new Actor { Flags = flags, Team = team, OriginalTeam = team });

    static WorldThing Bare(ThingType type, Actor? actor = null) => new()
    {
        Index = 0, Guid = 1, Scene = new NsdThing(), Template = "t", Type = type, Actor = actor,
    };
}
