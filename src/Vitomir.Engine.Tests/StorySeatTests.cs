// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Storyteller's seat and multiplayer's remainder**.
/// The seat's ground move is a key command (*"Let it be remappable"*), Alt and the right button
/// by default as 1.1's bit 0x4; the plain right button on the ground with nothing in
/// the control's hand puts the ghost there and never casts; a player's machine sends
/// its shot at the ground to the host, which makes it on its own copy;
/// and the escape menu hears a click before the seat does, which a play-test of
/// A comparison with 1.1 found — quitting a hosted Storyteller game took several clicks.
/// </summary>
public class StorySeatTests
{
    static readonly int Right = KeyChord.MouseBase + 3;

    static IEnumerable<KeyCommand> Heard(KeyBindings keys, int input, KeyMods held) =>
        keys.Pressed(input, held, driving: false).Select(p => p.Command);

    /// <summary>
    /// **The ground move is `storymove`**, a click command on the key page's sixth page, bound by default to 1.1's
    /// own chord — Alt with the right button — and handed on as that chord (the right button with the Alt flag),
    /// so with 1.1's keys nothing changes. As the most specific chord it wins over the cast; bound elsewhere, the
    /// new chord is the move and Alt with the right button is a cast again.
    /// </summary>
    [Fact]
    public void The_ground_move_is_a_key_command_Alt_and_the_right_button_by_default_and_it_moves()
    {
        var info = KeyBindings.Of(KeyCommand.StoryMove);
        Assert.Equal("storymove", info.Id);
        Assert.Equal(CommandKind.Click, info.Kind);
        Assert.Equal([KeyChord.Mouse(3, KeyMods.Alt)], info.Defaults);
        Assert.Equal("Alt+Right Click", info.Defaults[0].ToString());
        Assert.Equal([KeyCommand.StoryMove], KeysPage.Groups[^1].Commands);
        Assert.Equal((3, InputModifier.Alt, false), KeyBindings.ClickAs(KeyCommand.StoryMove));
        Assert.Equal((3, InputModifier.None, false), KeyBindings.ClickAs(KeyCommand.Cast));
        Assert.Equal((1, InputModifier.Alt, true), KeyBindings.ClickAs(KeyCommand.AltClick));

        var keys = new KeyBindings();
        Assert.Equal([KeyCommand.StoryMove], Heard(keys, Right, KeyMods.Alt));
        Assert.Equal([KeyCommand.StoryMove], Heard(keys, Right, KeyMods.Alt | KeyMods.Shift));
        Assert.Equal([KeyCommand.Cast], Heard(keys, Right, KeyMods.None));
        Assert.Equal([KeyCommand.Cast], Heard(keys, Right, KeyMods.Ctrl));

        // Moved to J with Ctrl: that is the move now, and Alt with the right button the cast.
        var read = new KeyBindings();
        read.Apply(IniFile.Parse("[keys]\nstorymove = Ctrl+J\n"));
        Assert.Equal([new KeyChord(KeyNames.Letter('J'), KeyMods.Ctrl)], read[KeyCommand.StoryMove]);
        Assert.Equal([KeyCommand.StoryMove], Heard(read, KeyNames.Letter('J'), KeyMods.Ctrl));
        Assert.Empty(Heard(read, KeyNames.Letter('J'), KeyMods.None));
        Assert.Equal([KeyCommand.Cast], Heard(read, Right, KeyMods.Alt));
        Assert.Contains(read.Values(), kv => kv.Key == "storymove" && kv.Value == "Ctrl+J");
        Assert.True(new KeyBindings().AreDefaults);
    }

    /// <summary>
    /// **The move is the command's, not Alt's** (the ground click's first branch, `StoryPowers.Ground`): the command puts
    /// the target at the spot whatever flags come with it, and a right click with the Alt flag that is not the
    /// command — the move bound elsewhere — is the seat's plain right button, the ghost's.
    /// </summary>
    [Fact]
    public void In_the_seat_the_move_command_puts_the_target_and_Alt_alone_does_not()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var powers = new StoryPowers(host.Server, inn, null);
        var drunk = inn.Things.First(t => t.InWorld && t.Type == ThingType.Actor && !ReferenceEquals(t, mine));
        powers.Target = drunk;
        var spot = StoryCarryTests.FloorNear(inn, drunk.Position, 120f);
        var hit = new Hit(spot, (int)inn.At(spot, drunk.Sector), 0, -1, true, 0f);
        var was = drunk.Position;

        host.Server.Storyteller = false;
        Assert.Null(powers.Ground(inn, hit, InputModifier.Alt, move: true, mine));     // out of the seat: the cast's
        host.Server.Storyteller = true;
        try
        {
            Assert.StartsWith("the ghost", powers.Ground(inn, hit, InputModifier.Alt, move: false, mine));
            Assert.Equal(was, drunk.Position);
            powers.Target = drunk;
            Assert.StartsWith($"{drunk.Template} put at", powers.Ground(inn, hit, InputModifier.None, move: true, mine));
            Assert.Equal(spot, drunk.Position);
        }
        finally { host.Server.Storyteller = false; }
    }

    /// <summary>
    /// **The seat's plain right button on the ground**: with nothing in the
    /// control's hand it never casts — the ghost, found or made, is stood at the spot in the spot's sector with
    /// its turn kept, made the control and the target, and a possession is over (1.1's
    /// possessed is the control). A miss spends the click; a hand holding something is used as
    /// ever; out of the seat it is the cast. A seat click on a thing takes the ghost out of the world without
    /// destroying it, and the next right click brings the same one back.
    /// </summary>
    [Fact]
    public void In_the_seat_the_right_button_on_the_ground_puts_the_ghost_there_and_steers_it()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var powers = new StoryPowers(host.Server, inn, null);
        var spot = StoryCarryTests.FloorNear(inn, mine.Position, 150f);
        var hit = new Hit(spot, (int)inn.At(spot, mine.Sector), 0, -1, true, 0f);

        Assert.Null(powers.Ground(inn, hit, InputModifier.None, move: false, mine));   // out of the seat: the cast's
        host.Server.Storyteller = true;
        try
        {
            mine.Actor!.Held = new Item { Name = "sword", Type = ItemType.Weapon, Amount = 1 };
            Assert.Null(powers.Ground(inn, hit, InputModifier.None, move: false, mine));   // the hand's, as ever
            mine.Actor.Held = null;
            Assert.Equal("no ground under the pointer", powers.Ground(inn, null, InputModifier.None, move: false, mine));
            Assert.Null(powers.Ghost);

            var drunk = inn.Things.First(t => t.InWorld && t.Type == ThingType.Actor && !ReferenceEquals(t, mine));
            host.Server.Possessed = drunk;
            Assert.StartsWith("the ghost put at", powers.Ground(inn, hit, InputModifier.None, move: false, mine));
            var ghost = powers.Ghost!;
            Assert.Equal(StoryPowers.GhostTemplate, ghost.Template, ignoreCase: true);
            Assert.True(ghost.InWorld);
            Assert.Equal(spot, ghost.Position);
            Assert.Equal(inn.At(spot, hit.Sector), ghost.Sector);
            Assert.Same(ghost, inn.Steered);
            Assert.Same(ghost, powers.Target);
            Assert.Null(host.Server.Possessed);

            // Clicked away, it leaves the world and is kept; the right button brings the same one back.
            Assert.True(inn.SetSteered(mine));
            Assert.True(powers.Clicked(drunk, inn.Steered));
            Assert.False(ghost.InWorld);
            Assert.False(ghost.Removed);
            var there = StoryCarryTests.FloorNear(inn, mine.Position, 90f);
            var hit2 = new Hit(there, (int)inn.At(there, mine.Sector), 0, -1, true, 0f);
            Assert.StartsWith("the ghost put at", powers.Ground(inn, hit2, InputModifier.None, move: false, mine));
            Assert.Same(ghost, powers.Ghost);
            Assert.True(ghost.InWorld);
            Assert.Equal(there, ghost.Position);
            Assert.Single(inn.Things, t => t.Template.Equals(StoryPowers.GhostTemplate, StringComparison.OrdinalIgnoreCase));
            Assert.Same(ghost, inn.Steered);
        }
        finally { host.Server.Storyteller = false; }
    }

    sealed class Sink : IOrderSink
    {
        public readonly List<NetOrder> Orders = [];
        public void Send(NetOrder order) => Orders.Add(order);
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    /// <summary>
    /// **A player's shot at the ground** (a comparison with 1.1's gap 7): after its own tests and
    /// the cancel, a player's machine sends order 2 with no thing, the point, the click's mask and the attack
    /// and does nothing more — no turn, no swing; the host's arm of order 2 with no thing
    /// makes the attack on the point on its own copy. An earlier build forwarded nothing and the host refused it.
    /// </summary>
    [Fact]
    public void A_players_shot_at_the_ground_goes_to_the_host_which_makes_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var christof = place.Steered!;
        var me = christof.Actor!;
        var bow = new Weapon { Name = "shortBow" };
        var a = bow.Attacks[0];
        a.Flags = WeaponFlag.Ranged; a.AmmoType = 1; a.MaxAmmo = 20; a.Ammo = 5; a.AmmoUse = 1;
        a.Projectile = "arrow"; a.ReloadTime = 1500;
        me.Weapon = bow;
        var at = christof.Position + new Vector3(0f, -300f, 0f);
        var turn = christof.Orientation;
        int now = place.Milliseconds;

        var sink = new Sink();
        NetOrders.Attach(place, NetMode.Client, sink);
        try
        {
            var mods = InputModifier.Alt;
            Assert.Equal(OrderResult.Started, ClickOrders.AttackPoint(christof, at, ClickOrders.AttackIndex(mods), now, place.Motions, mods));
            var sent = sink.Orders.Single();
            Assert.Equal(new NetOrder(christof.Guid, OrderKind.Attack, 0, at, (int)InputModifier.Alt, 0), sent);
            Assert.NotEqual(ActionMode.Swing, me.Action);
            Assert.Equal(turn, christof.Orientation);
            Assert.Equal(5, a.Ammo);
        }
        finally { NetOrders.Detach(place); }

        // The host: the same order, with no thing, is the attack on the point.
        me.BusyUntil = 0;
        var order = new NetOrder(christof.Guid, OrderKind.Attack, 0, at, (int)InputModifier.Alt, 0);
        Assert.Equal(OrderResult.Started, ServerOrders.Apply(place, christof, order, place.Milliseconds));
        Assert.Equal(ActionMode.Swing, me.Action);
        Assert.Null(me.Target);
        Assert.Equal(at, me.ActionPoint);
        // A thing named that is not there is still refused.
        Assert.Equal(OrderResult.Refused, ServerOrders.Apply(place, christof, order with { Target = 0x7ffffff0 }, place.Milliseconds));
    }

    /// <summary>
    /// **Who hears a left press in the seat**: the game's menu that is up — the
    /// escape menu is `dialog=1` — before the Storyteller's panes, the thing under the pointer, or the world.
    /// Vitomir offered the press to the seat first, and the seat asked about the thing the pointer was over
    /// when the menu came up, so every click on Quit made that thing the target (as a play-test showed).
    /// </summary>
    [Fact]
    public void With_the_escape_menu_up_the_click_is_the_menu_s_even_over_a_thing_in_the_seat()
    {
        Assert.Equal(StoryClickTo.Menu, StoryClick.Route(menuUp: true, paneUnder: true, thingUnder: true));
        Assert.Equal(StoryClickTo.Menu, StoryClick.Route(menuUp: true, paneUnder: false, thingUnder: true));
        Assert.Equal(StoryClickTo.Pane, StoryClick.Route(menuUp: false, paneUnder: true, thingUnder: true));
        Assert.Equal(StoryClickTo.Thing, StoryClick.Route(menuUp: false, paneUnder: false, thingUnder: true));
        Assert.Equal(StoryClickTo.World, StoryClick.Route(menuUp: false, paneUnder: false, thingUnder: false));
    }
}
