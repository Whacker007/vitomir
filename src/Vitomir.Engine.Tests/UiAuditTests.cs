// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a comparison with 1.1 found and fixed on the status
/// bar: a party portrait's right click has two gates Vitomir did not —
/// it does nothing while a shop is up, and a scroll in the hand that names a discipline which cannot
/// be cast at a head is the red cross at once, before the current discipline is looked at; and the
/// haven vault's opener opens it only for the player's own character.
/// (The pack's fixes are in `InventoryHandsTests`.)
/// </summary>
public class UiAuditTests
{
    static (Location Place, StatusBar Bar, WorldThing Member) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        mind.Thing.Actor!.Flags |= Actor.Flag.Party;
        mind.Enter(AiMode.Party);
        bar.Update();
        return (place, bar, mind.Thing);
    }

    /// <summary>The right click on the member's portrait, high up the hotspot, clear of the row's bars.</summary>
    static UiClick RightClickOn(StatusBar bar, WorldThing member)
    {
        for (int row = 0; row < 4; row++)
            if (bar[$"statusHotspot{row}"] is { } hotspot && ReferenceEquals(hotspot.Actor, member.Actor))
                return bar.Click(hotspot.X + 1, hotspot.Y + hotspot.Height - 1, InputModifier.None, right: true);
        throw new InvalidOperationException("the member has no row");
    }

    /// <summary>
    /// The haven vault opens only for a player-type character (the steered thing's type is
    /// 7) and not over itself, and shows only a player-type subject's own stored page.
    /// **The companions are type 7 too**: a steered Wilhem opens it on his own page in 1.1
    /// (`vaultPane.vaultcurrentchar` is Wilhem). What gets nothing is
    /// a body taken over with Possession — an actor, type 0.
    /// </summary>
    [Fact]
    public void The_vault_opens_for_a_companion_and_not_for_a_possessed_body()
    {
        var christof = new WorldThing { Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player, Actor = new Actor { Name = "Christof" } };
        var wilhem = new WorldThing { Index = 1, Guid = 2, Scene = new NsdThing(), Template = "wilhem", Type = ThingType.Player, Actor = new Actor { Name = "Wilhem" } };
        var possessed = new WorldThing { Index = 2, Guid = 3, Scene = new NsdThing(), Template = "prague_guard", Type = ThingType.Actor, Actor = new Actor { Name = "Guard" } };

        Assert.True(InventoryPane.VaultMayOpen(christof, alreadyUp: false));
        Assert.False(InventoryPane.VaultMayOpen(christof, alreadyUp: true));
        Assert.True(InventoryPane.VaultMayOpen(wilhem, alreadyUp: false));
        Assert.False(InventoryPane.VaultMayOpen(possessed, alreadyUp: false));
        Assert.False(InventoryPane.VaultMayOpen(null, alreadyUp: false));

        // Each player-type character sees his own page; a possessed body an empty grid and no name.
        Assert.Same(christof.Actor, InventoryPane.VaultSubject(christof));
        Assert.Same(wilhem.Actor, InventoryPane.VaultSubject(wilhem));
        Assert.Null(InventoryPane.VaultSubject(possessed));
    }

    /// <summary>
    /// The portrait's right click opens with the shop's flag (set by the shop pane's show): while
    /// a shop is up a portrait's right click does nothing — no cast, not even the red cross.
    /// </summary>
    [Fact]
    public void A_portrait_right_click_does_nothing_while_a_shop_is_up()
    {
        using var files = Install.Open();
        var (_, bar, member) = Open(files);
        int refused = 0;
        bar.Refuse = () => refused++;

        bar.Shopping = true;
        var click = RightClickOn(bar, member);
        Assert.Contains("shop", click.Did);
        Assert.Equal(0, refused);

        bar.Shopping = false;
        RightClickOn(bar, member);
        Assert.Equal(1, refused);   // nothing to cast at a head: the red cross
    }

    /// <summary>
    /// A scroll in the hand that names a discipline is the portrait's whole answer: cast at the head when
    /// the discipline may be (`TARGET HEAD`, 0x80), and the red cross when it may not — the current
    /// discipline is looked at only when the hand holds no such scroll. Vitomir fell
    /// through to the current discipline.
    /// </summary>
    [Fact]
    public void A_scroll_that_cannot_touch_a_head_is_the_red_cross_even_with_a_discipline_that_can()
    {
        using var files = Install.Open();
        var (place, bar, member) = Open(files);
        Assert.NotNull(place.Powers?.File);
        var all = place.Powers!.File!.All;
        var head = all.First(d => d.Has(DisciplineFlags.TargetHead));
        var notHead = all.First(d => !d.Has(DisciplineFlags.TargetHead));
        var steered = place.Steered!;
        var me = steered.Actor!;

        var current = me.Disciplines.Add(head, 1, owner: steered);
        me.Disciplines.Current = current;
        var scroll = new Item
        {
            Name = "scroll", Type = ItemType.Scroll, Amount = 1, Location = Slot.Inventory,
            ScrollDiscipline = notHead.Name,
        };
        me.Inventory.Add(scroll);
        Ground.Hand(steered, scroll);
        Assert.Same(scroll, me.Held);
        int refused = 0;
        bar.Refuse = () => refused++;

        var click = RightClickOn(bar, member);

        Assert.Equal(1, refused);
        Assert.Contains("cannot be read", click.Did);
        Assert.Null(me.PendingDiscipline);
        Assert.Equal(ActionMode.None, me.Action);
    }
}
