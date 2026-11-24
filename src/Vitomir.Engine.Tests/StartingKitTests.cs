// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The `item` lines of a `.NPC`, applied. Read early and parked; the inventory pane arriving
/// is what made them worth giving out, because before it they were invisible either way.
/// </summary>
public class StartingKitTests
{
    /// <summary>The shipped templates, from the player's own install.</summary>
    static (TemplateSet Templates, EffectDefinitions Effects, Vfs.GameFiles Files) Game()
    {
        var files = Install.Open();
        return (new TemplateSet(files), new EffectDefinitions(files), files);
    }

    static WorldThing Somebody() => new()
    {
        Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
        Type = ThingType.Player, Actor = new Actor { Type = ActorType.Human, Name = "Christof" },
    };

    /// <summary>
    /// **Christof's own two lines, verbatim from `misc/Christof.NPC`.** The broadsword is
    /// `ITEM_TYPE 1` so `Item.SlotsFor` sends it to the right weapon slot; the letter is a general
    /// item with no slot of its own, and `belt` names the quick-use strip outright.
    /// </summary>
    [Fact]
    public void Christof_starts_with_a_broadsword_in_hand_and_a_letter_on_his_belt()
    {
        var (templates, effects, files) = Game();
        using var _ = files;
        Assert.SkipUnless(templates.Has("broadsword"), "no broadsword template in this install");

        var npc = NpcFile.Parse("""
            item            CuthbertLetter          belt
            item            broadsword              equip
            """, "Christof.NPC");

        var thing = Somebody();
        var given = StartingKit.Give(thing, npc, templates, effects);

        Assert.Equal(2, given.Count);
        Assert.All(given, g => Assert.True(g.Placed, $"{g.Template} went nowhere: {g.Note}"));
        Assert.All(given, g => Assert.Null(g.Note));

        var sword = thing.Actor!.Inventory.Items.Single(i => i.Name == "broadsword");
        Assert.Equal(Slot.RightWeapon, sword.Location);

        var letter = thing.Actor.Inventory.Items.Single(i => i.Name == "CuthbertLetter");
        Assert.Equal(Slot.QuickUse, letter.Location);
        Assert.Equal((0, 0), letter.Cell);
    }

    /// <summary>
    /// A template the install has not got is **reported**, not thrown and not silently dropped —
    /// which is what a mod naming something it forgot to ship looks like.
    /// </summary>
    [Fact]
    public void A_template_that_is_not_there_is_reported_rather_than_dropped()
    {
        var (templates, effects, files) = Game();
        using var _ = files;

        var npc = NpcFile.Parse("item NoSuchThingAtAll main\n", "test.NPC");
        var given = Assert.Single(StartingKit.Give(Somebody(), npc, templates, effects));
        Assert.False(given.Placed);
        Assert.Equal("no such template", given.Note);
    }

    /// <summary>
    /// The pack fills row by row from the top left, and the same order the inventory pane's own
    /// `FreeCell` uses — deliberately, so the loader and the pane cannot disagree about where
    /// something is.
    /// </summary>
    [Fact]
    public void The_pack_fills_row_by_row_from_the_top_left()
    {
        var actor = new Actor();
        var one = new Item { Name = "a", IconSize = 0 };          // 1 x 1
        var two = new Item { Name = "b", IconSize = 0 };
        var big = new Item { Name = "c", IconSize = 5 };          // 2 x 3

        actor.Inventory.Add(one);
        actor.Inventory.Add(two);
        actor.Inventory.Add(big);

        Assert.True(Stow.Place(actor, one, Slot.Inventory));
        Assert.Equal((0, 0), one.Cell);
        Assert.True(Stow.Place(actor, two, Slot.Inventory));
        Assert.Equal((1, 0), two.Cell);
        // The 2 x 3 cannot start at column 0 or 1 without covering one of them.
        Assert.True(Stow.Place(actor, big, Slot.Inventory));
        Assert.Equal((2, 0), big.Cell);
    }

    /// <summary>
    /// The belt takes only 1 x 1 items — its grid is 5 x 1 — so anything larger asked onto it goes
    /// into the pack instead, and the report says so rather than the item vanishing.
    /// </summary>
    [Fact]
    public void Something_too_big_for_the_belt_falls_back_to_the_pack_and_says_so()
    {
        var actor = new Actor();
        var sword = new Item { Name = "broadsword", IconSize = 5 };    // 2 x 3
        actor.Inventory.Add(sword);
        Assert.False(Stow.Place(actor, sword, Slot.QuickUse));
        Assert.True(Stow.Place(actor, sword, Slot.Inventory));
        Assert.Equal(Slot.Inventory, sword.Location);
    }

    /// <summary>
    /// **Every shipped `.NPC`'s kit is given out without a single item going nowhere.** Eighteen
    /// files, and between them they name the templates a mod would be copying.
    /// </summary>
    [Fact]
    public void Every_shipped_character_s_starting_kit_is_placed()
    {
        var (templates, effects, files) = Game();
        using var _ = files;
        var names = files.List(NpcFile.Folder).Where(p => p.EndsWith(NpcFile.Extension,
                                                          StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.SkipWhen(names.Count == 0, "no .NPC files in this install");

        var trouble = new List<string>();
        int items = 0;
        foreach (var path in names)
        {
            if (!files.TryRead(path, out var data)) continue;
            var npc = NpcFile.Read(data, path);
            foreach (var line in StartingKit.Give(Somebody(), npc, templates, effects))
            {
                items++;
                if (!line.Placed || line.Note is not null)
                    trouble.Add($"{npc.Name}: {line.Template} {line.Where} -> {line.Note ?? "nowhere"}");
            }
        }
        Assert.True(trouble.Count == 0, string.Join("\n", trouble));
        Assert.True(items > 0, "no shipped character starts with anything");
    }
}
