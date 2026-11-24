// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Multiplayer's small remainder**.
/// A play-test put the Storyteller's ghost on the floor and found a huge grey bald head: 1.1's
/// host draws the same head (the template hides nothing and the seat's fade is 1.0), but never tells the
/// players' machines of it (`NO NET SYNCH`). And the rest of the notes: the seat's pointer in the
/// host's own place offers anything but the target; 1.1's chat keys (Enter, Shift+; and
/// /); Tab in a network game (the seat walks the players, and outside it Tab steers nobody else);
/// the vault leaves the seat first; and a player holding fire at a point makes the
/// host swing there.
/// </summary>
public class StoryRemainderTests
{
    [Fact]
    public void The_Storyteller_s_ghost_is_drawn_on_the_host_and_never_sent_to_the_players()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var watch = new WorldWatch(inn);
        watch.Changes();
        var powers = new StoryPowers(host.Server, inn, null);
        var spot = StoryCarryTests.FloorNear(inn, mine.Position, 150f);
        host.Server.Storyteller = true;
        Assert.StartsWith("the ghost put at", powers.Ground(inn, new Hit(spot, (int)inn.At(spot, mine.Sector), 0, -1, true, 0f),
                                                            InputModifier.None, move: false, mine));
        var ghost = powers.Ghost!;

        // Drawn on the host as any character: no DON'T RENDER, not hidden, the fade 1.0.
        Assert.Equal(0u, ghost.RenderFlags & 0x2u);
        Assert.False(ghost.ModelHidden);
        Assert.Same(ghost, inn.Steered);
        Assert.Equal(1f, StealthFade.Fade(ghost, inn));

        // Never sent: the template's NO NET SYNCH keeps it off the players' machines.
        Assert.True((ghost.Flags & WorldThing.Flag.NoNetSync) != 0);
        Assert.True(NetSynch.Kept(ghost));
        var rat = inn.Place("Rat", spot, Vector3.Zero, ghost.Sector)!;              // a character that is sent
        var (made, _) = watch.Changes();
        Assert.DoesNotContain(made, m => m.Guid == ghost.Guid);
        Assert.Contains(made, m => m.Guid == rat.Guid);
        Assert.DoesNotContain(watch.CatchUp().Made, m => m.Guid == ghost.Guid);
        Assert.False(NetSynch.Kept(rat));
    }

    /// <summary>
    /// The seat's plain right button on the ground of a carried body's place: the spot's
    /// sector is that place's, so the ghost goes there and is the control there — the body let go, the ghost the
    /// target — and the next one in the host's own place brings it back (one ghost). An earlier build spent the click there.
    /// </summary>
    [Fact]
    public void The_ghost_goes_to_the_carried_body_s_place_and_back()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var drunk = inn.Things.First(t => t.Template.Equals("civ07drunk", StringComparison.OrdinalIgnoreCase));
        Assert.True(inn.SetSteered(drunk));
        host.Server.Possessed = drunk;
        Assert.True(host.Server.Places.JumpTo(drunk, 1));
        var body = host.Server.Places.Carried!;
        var there = body.Place!;
        Assert.True(StoryHand.Take(host.Server, body));                              // the view's hand (Program's run)
        Assert.True(StoryHand.Holds(body));
        var powers = new StoryPowers(host.Server, inn, null);
        host.Server.Storyteller = true;

        var spot = StoryCarryTests.FloorNear(there, body.Position, 150f);
        var hit = new Hit(spot, (int)there.At(spot, body.Sector), 0, -1, true, 0f);
        Assert.StartsWith("the ghost put at", powers.Ground(there, hit, InputModifier.None, move: false, body));
        var ghost = powers.Ghost!;
        Assert.Same(there, ghost.Place);
        Assert.True(ghost.InWorld);
        Assert.Equal(spot, ghost.Position);
        Assert.Same(ghost, powers.Target);
        Assert.Same(ghost, host.Server.Possessed);                                   // the control, held as a carried body is
        Assert.True(StoryHand.Holds(ghost));
        Assert.False(StoryHand.Holds(body));                                         // the body let go
        Assert.True(NetSynch.Kept(ghost));

        // Back in the host's own place: the ghost there, the one in the other place out of the world.
        var home = StoryCarryTests.FloorNear(inn, mine.Position, 150f);
        var hit2 = new Hit(home, (int)inn.At(home, mine.Sector), 0, -1, true, 0f);
        Assert.StartsWith("the ghost put at", powers.Ground(inn, hit2, InputModifier.None, move: false, mine));
        Assert.False(ghost.InWorld);
        Assert.Same(inn, powers.Ghost!.Place);
        Assert.Same(powers.Ghost, inn.Steered);
        Assert.Null(host.Server.Possessed);

        // And there again: the same ghost of that place comes back into the world.
        Assert.StartsWith("the ghost put at", powers.Ground(there, hit, InputModifier.None, move: false, powers.Ghost));
        Assert.Same(ghost, powers.Ghost);
        Assert.True(ghost.InWorld);
        Assert.Equal(1, there.Things.Count(t => t.Template.Equals(StoryPowers.GhostTemplate, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void In_the_seat_the_pointer_offers_anything_but_the_target()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var drunk = inn.Things.First(t => t.Template.Equals("civ07drunk", StringComparison.OrdinalIgnoreCase));
        var prop = inn.Things.First(t => t.InWorld && t.Type == ThingType.Prop && !Ui.Highlight.Offers(mine, t));

        // Out of the seat: the steered character's own rule, passing over itself.
        var (passed, offers) = StoryPointer.For(seat: false, target: drunk, steered: mine);
        Assert.Same(mine, passed);
        Assert.False(offers(prop));
        Assert.False(offers(mine));

        // In the seat: anything but the target — the prop, the host's own character — and the target passed over.
        (passed, offers) = StoryPointer.For(seat: true, target: drunk, steered: mine);
        Assert.Same(drunk, passed);
        Assert.True(offers(prop));
        Assert.True(offers(mine));
        Assert.False(offers(drunk));
    }

    [Fact]
    public void The_chat_line_opens_on_Enter_empty_on_Shift_and_semicolon_with_a_colon_and_on_slash_with_a_slash()
    {
        Assert.Equal("", ChatKeys.Opens(ChatKeys.Of(enter: true, semicolon: false, slash: false, shift: false)));
        Assert.Equal(":", ChatKeys.Opens(ChatKeys.Of(enter: false, semicolon: true, slash: false, shift: true)));
        Assert.Null(ChatKeys.Opens(ChatKeys.Of(enter: false, semicolon: true, slash: false, shift: false)));
        Assert.Equal("/", ChatKeys.Opens(ChatKeys.Of(enter: false, semicolon: false, slash: true, shift: false)));
        Assert.Equal("/", ChatKeys.Opens(ChatKeys.Of(enter: false, semicolon: false, slash: true, shift: true)));
        Assert.Null(ChatKeys.Opens(ChatKeys.Of(false, false, false, true)));
        Assert.Equal(0, ChatKeys.Sound);

        // Only while the game camera's key hook reads keys (the key handler's start).
        Assert.True(ChatKeys.Read(gameCamera: true, dialog: false, vault: false, advancement: false, shop: false, typing: false));
        Assert.False(ChatKeys.Read(gameCamera: false, dialog: false, vault: false, advancement: false, shop: false, typing: false));
        Assert.False(ChatKeys.Read(true, dialog: false, vault: true, advancement: false, shop: false, typing: false));
        Assert.False(ChatKeys.Read(true, dialog: false, vault: false, advancement: false, shop: true, typing: false));
        Assert.False(ChatKeys.Read(true, dialog: true, vault: false, advancement: false, shop: false, typing: false));
    }

    [Fact]
    public void Tab_in_the_seat_walks_the_players_and_outside_it_steers_nobody_else_in_a_network_game()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhem = minds.All.First(m => !ReferenceEquals(m, christof) && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christof, wilhem })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Enter(AiMode.Party);
        }

        // A network game: the click, but nobody else steered (Tab's party branch is single player's).
        Assert.Same(christof.Thing, place.SteerTab(() => 1f, network: true));
        Assert.Same(christof.Thing, place.SteerTab(() => 1f, network: true));
        Assert.Same(wilhem.Thing, place.SteerTab(() => 1f));                        // single player, as ever

        // The seat's arm: the player after the target's slot whose character is in a place, else slot 0's.
        var third = place.Things.First(t => t.InWorld && t.Type == ThingType.Actor);
        var gone = place.Place("Rat", christof.Thing.Position, Vector3.Zero, christof.Thing.Sector)!;
        place.Withdraw(gone);
        wilhem.Thing.Actor!.Flags |= Actor.Flag.Party;
        WorldThing?[] slots = [christof.Thing, null, wilhem.Thing, gone];
        Assert.Same(christof.Thing, StoryTab.Next(slots, null));
        Assert.Same(christof.Thing, StoryTab.Next(slots, third));                    // not a player: from slot 0
        Assert.Same(wilhem.Thing, StoryTab.Next(slots, christof.Thing));
        Assert.Same(christof.Thing, StoryTab.Next(slots, wilhem.Thing));             // slot 3 is out of the world: slot 0
        Assert.Null(StoryTab.Next([], null));
    }

    [Fact]
    public void The_vault_leaves_the_Storyteller_s_seat_before_it_opens()
    {
        using var files = Install.Open();
        using var host = StoryCarryTests.Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var said = new List<string>();

        Assert.True(StoryVault.Open(mine, vaultUp: false, seat: true, () => said.Add("leave"), () => said.Add("vault")));
        Assert.Equal(["leave", "vault"], said);
        said.Clear();
        Assert.True(StoryVault.Open(mine, vaultUp: false, seat: false, () => said.Add("leave"), () => said.Add("vault")));
        Assert.Equal(["vault"], said);
        said.Clear();

        // The ghost steered (type 0): nothing opens, and the seat stays.
        var drunk = inn.Things.First(t => t.Template.Equals("civ07drunk", StringComparison.OrdinalIgnoreCase));
        Assert.False(StoryVault.Open(drunk, vaultUp: false, seat: true, () => said.Add("leave"), () => said.Add("vault")));
        Assert.False(StoryVault.Open(mine, vaultUp: true, seat: true, () => said.Add("leave"), () => said.Add("vault")));
        Assert.Empty(said);
    }

    [Fact]
    public void A_player_holding_fire_at_a_point_makes_the_host_swing_there()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        foreach (var m in minds.All) if (m.Thing.Actor is { } a) a.Flags |= Actor.Flag.AiPaused;
        var christof = place.Steered!;
        var gun = Weapon.From("submachineGun", place.Templates!.Resolve("submachineGun"))!;
        foreach (var attack in gun.Attacks) attack.Ammo = attack.MaxAmmo = 1000;
        var me = christof.Actor!;
        me.Weapon = gun;
        int now = place.Milliseconds;
        var point = christof.Position + new Vector3(0f, 200f, 0f);
        var order = new NetOrder(christof.Guid, OrderKind.Aim, 0, point, 0);

        // Busy: the gate turns it away, and it does not even turn.
        me.BusyUntil = now + 1000;
        var facing = christof.Orientation;
        Assert.Equal(OrderResult.Ignored, ServerOrders.Apply(place, christof, order, now));
        Assert.Equal(facing, christof.Orientation);

        // Free: faced, the point the order's with nobody for the target, and the swing.
        me.BusyUntil = now;
        Assert.Equal(OrderResult.Started, ServerOrders.Apply(place, christof, order, now));
        Assert.Equal(ActionMode.Swing, me.Action);
        Assert.Null(me.Target);
        Assert.Equal(point, me.ActionPoint);
        var turned = christof.Orientation;
        christof.Face(point);                                                        // already facing it
        Assert.Equal(turned, christof.Orientation);
    }
}
