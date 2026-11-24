// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The interface's own two rollovers: an item icon's — the item's
/// ordinary rollover, both boxes at once in a grid (mode 2) and the large one 1.5 s later on the
/// belt (mode 3) — with its name **red** when the character being steered cannot use it;
/// and a party row's, the small box alone with the name and `Health / Blood / Frenzy`, or
/// `Health / Mana / Faith` for a human, each number truncated.
/// </summary>
public class IconRolloverTests
{
    static WorldThing Person(ActorType type, bool player = true) => new()
    {
        Index = -1, Guid = 1, Scene = new NsdThing(), Template = "christof", DescriptionId = "", Type = ThingType.Player,
        Actor = new Actor { Type = type, Health = 99.9f, MaxHealth = 120f, IsPlayerThing = player },
    };

    static WorldThing Sword(params ItemRestriction[] restrictions)
    {
        var item = new Item { Name = "broadsword", Type = ItemType.Weapon, Flags = ItemFlags.Identified, Restrictions = restrictions };
        var thing = new WorldThing { Index = -1, Guid = 2, Scene = new NsdThing(), Template = "broadsword", Type = ThingType.Weapon, Item = item };
        item.Thing = thing;
        return thing;
    }

    [Fact]
    public void An_item_the_steered_character_cannot_use_is_named_in_red_and_one_it_can_in_white()
    {
        var christof = Person(ActorType.Vampire);
        christof.Actor!.Stat[(int)ActorStat.Strength] = 2f;
        var heavy = Sword(new ItemRestriction(RestrictionType.Strength, 5f));
        Assert.Equal(Rollover.Unusable, Rollover.Name(heavy, user: christof)!.Value.Colour);
        Assert.Equal(Rollover.White, Rollover.Name(heavy)!.Value.Colour);            // nobody asked
        Assert.Equal(Rollover.White, Rollover.Name(Sword(), user: christof)!.Value.Colour);
        // Only a player-type character is held to the restrictions (any other type may).
        var npc = Person(ActorType.Vampire, player: false);
        Assert.Equal(Rollover.White, Rollover.Name(heavy, user: npc)!.Value.Colour);
    }

    [Fact]
    public void An_icon_in_a_grid_brings_both_boxes_at_once_and_one_on_the_belt_makes_the_large_one_wait()
    {
        var sword = Sword();
        var grid = IconRollover.Item(sword.Item, onStatusBar: false, user: null, text: null, effects: null)!.Value;
        Assert.True(grid.LargeNow);
        Assert.Same(sword.Item, grid.Key);
        Assert.Single(grid.Small);
        var belt = IconRollover.Item(sword.Item, onStatusBar: true, user: null, text: null, effects: null)!.Value;
        Assert.False(belt.LargeNow);
        // An item with no thing behind it has nothing to say.
        Assert.Null(IconRollover.Item(new Item { Name = "loose" }, false, null, null, null));
    }

    [Fact]
    public void A_party_row_says_the_name_and_health_blood_frenzy_or_for_a_human_mana_and_faith()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        var christof = Person(ActorType.Vampire);
        christof.Actor!.Stat[(int)ActorStat.Blood] = 87.6f;
        christof.Actor.Stat[(int)ActorStat.Frenzy] = 3f;
        var row = IconRollover.PartyRow(christof, text)!.Value;
        Assert.Equal(2, row.Small.Count);
        Assert.Empty(row.Large);                                                   // mode 1: the small box alone
        Assert.Equal("Health: 99 Blood: 87 Frenzy: 3", row.Small[1].Text);

        var human = Person(ActorType.Human);
        human.Actor!.Stat[(int)ActorStat.Mana] = 40f;
        human.Actor.Stat[(int)ActorStat.Faith] = 7.9f;
        Assert.Equal("Health: 99 Mana: 40 Faith: 7", IconRollover.PartyRow(human, text)!.Value.Small[1].Text);
    }

    /// <summary>
    /// The hover writes a control's rollover when the control under the pointer is not last frame's,
    /// and not again while it stays: feeding with the pointer on the portrait, the line keeps the
    /// blood it had when the pointer arrived, however the mouse moves inside the row; off the row and
    /// back, it is read afresh. (A play-test saw it follow the feed in jumps, as the mouse
    /// twitched.)
    /// </summary>
    [Fact]
    public void A_party_row_s_line_is_written_when_the_pointer_arrives_and_not_again_while_it_stays()
    {
        var christof = Person(ActorType.Vampire);
        christof.Actor!.Stat[(int)ActorStat.Blood] = 40f;
        var shown = IconRollover.OnChange(null, IconRollover.PartyRow(christof, null));
        Assert.Equal("Health: 99 Blood: 40 Frenzy: 0", shown!.Value.Small[1].Text);

        christof.Actor.Stat[(int)ActorStat.Blood] = 43f;                           // three feed ticks later
        for (int moved = 0; moved < 3; moved++)                                   // the mouse twitches on the row
            shown = IconRollover.OnChange(shown, IconRollover.PartyRow(christof, null));
        Assert.Equal("Health: 99 Blood: 40 Frenzy: 0", shown!.Value.Small[1].Text);

        shown = IconRollover.OnChange(shown, null);                              // off the row
        Assert.Null(shown);
        shown = IconRollover.OnChange(shown, IconRollover.PartyRow(christof, null));   // and back
        Assert.Equal("Health: 99 Blood: 43 Frenzy: 0", shown!.Value.Small[1].Text);

        var wilhem = Person(ActorType.Vampire);                                   // straight onto another row
        shown = IconRollover.OnChange(shown, IconRollover.PartyRow(wilhem, null));
        Assert.Equal("Health: 99 Blood: 0 Frenzy: 0", shown!.Value.Small[1].Text);
    }
}
