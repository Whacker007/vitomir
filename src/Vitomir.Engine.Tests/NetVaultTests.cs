// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The vault's moves on a player's machine, and the host's pane there**: a gesture
/// on the vault grid of a player's machine moves nothing there and asks the host (1.1's message
/// 0x16), the host makes the move on its copy with 1.1's refusals and tells the players,
/// and her machine makes it on hers — what came out in her hand, what went in in the vault's
/// first free cell, not where it was dropped. And the one pane the host puts up there (message 0x48 →
/// The pause menu for the Storyteller's Pause, the load screen while the host loads a place,
/// one at a time, and off taking down whichever is up. A host and a player's machine in one process, over loopback.
/// </summary>
public class NetVaultTests
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 10000)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    sealed class Two : IDisposable
    {
        public required GameServer Server;
        public required Location Host;
        public required Simulation HostSim;
        public required GameClient Client;
        public Location? Mine;
        public Simulation? MySim;
        public int Ms;
        public readonly List<string> Said = [];
        public void Step()
        {
            HostSim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
            Client.Poll(1f / 30);
            MySim?.Step(1f / 30);
        }
        public WorldThing Hers => Server.Players.Single(p => !p.Host).Avatar!;
        public void Dispose() { Client.Dispose(); Server.Dispose(); }
    }

    static Two Start(Vfs.GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0));
        server.Start();
        server.Enter(place, null);
        var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                    new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        var two = new Two { Server = server, Host = place, HostSim = new Simulation(place), Client = client };
        server.Log += two.Said.Add;
        client.Started += () =>
        {
            var s = client.Settings!;
            two.Mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
            two.MySim = new Simulation(two.Mine);
            client.Enter(two.Mine, two.Mine.Player?.Thing);
        };
        Assert.True(Until(() => client.Avatar is not null, two.Step), "never played");
        return two;
    }

    /// <summary>An item made on the host and put in her character's keeping at a slot and cell.</summary>
    static WorldThing Give(Two game, string template, int slot, int x, int y)
    {
        var hers = game.Hers;
        var thing = game.Host.Make(template)!;
        thing.Item!.Location = slot;
        thing.Item.Cell = (x, y);
        thing.Owner = hers;
        hers.Actor!.Inventory.Add(thing.Item);
        return thing;
    }

    /// <summary>The host opens her vault on her machine, what it holds sent first (ours, <see cref="Msg.VaultItems"/>).</summary>
    static void Open(Two game) => game.Server.SendVault(game.Server.Players.Single(p => !p.Host));

    /// <summary>Her machine's copy of a host's item, once a sheet or the vault's contents have brought it.</summary>
    static Item Hers(Two game, WorldThing onHost) => game.Client.Mirror!.Find(onHost.Guid)!.Item!;

    /// <summary>Her pack and her vault open on her machine, with the hands the game gives them.</summary>
    static (InventoryHands Hands, InventoryPane Vault, InventoryGrid Pack) Panes(Vfs.GameFiles files, WorldThing mine)
    {
        var assets = new UiAssets(files);
        var pack = new InventoryPane(assets.Pane(InventoryPane.PaneName)) { Subject = mine.Actor };
        var vault = new InventoryPane(assets.Pane(InventoryPane.VaultPaneName), InventoryPane.VaultSlots) { Subject = mine.Actor };
        var hands = new InventoryHands(pack, mine, new EffectDefinitions(files));
        hands.Panes.Add(vault);
        return (hands, vault, pack.Grids().Single(g => g.Slot == Slot.Inventory));
    }

    /// <summary>The middle of a vault cell in the pane's 640 x 480 space (cells count down from the top, `InventoryPane.At`).</summary>
    static (int X, int Y) Cell(InventoryPane vault, int cx, int cy)
    {
        var grid = vault.Grids().Single();
        var e = vault.Control(grid.Element)!;
        int px = vault.Pane.Pos[0] + e.Pos[0], py = vault.Pane.Pos[1] + e.Pos[1];
        return (px + cx * InventoryPane.CellWidth + InventoryPane.CellWidth / 2,
                py + (grid.Down - 1 - cy) * InventoryPane.CellHeight + InventoryPane.CellHeight / 2);
    }

    [Fact]
    public void A_click_on_a_vault_item_on_her_machine_moves_nothing_there_and_the_host_puts_it_in_her_hand()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var letter = Give(game, "AnezkaLetter", Slot.Vault, 2, 1);
        var mine = game.Client.Avatar!;
        Open(game);
        Assert.True(Until(() => game.Client.Mirror!.Find(letter.Guid)?.Item is { Location: Slot.Vault }, game.Step), "never in her vault");
        var (hands, vault, pack) = Panes(files, mine);

        // The icon click on a player's machine: the move asked of the host, none made here.
        var (x, y) = Cell(vault, 2, 1);
        var asked = hands.Click(x, y);
        Assert.Equal(HandAction.Nothing, asked.What);
        Assert.Contains(VaultMoves.Asked, asked.Say);
        Assert.Null(mine.Actor!.Held);
        Assert.Equal(Slot.Vault, Hers(game, letter).Location);

        // The host takes it out of her vault and tells her machine: her hand has it.
        Assert.True(Until(() => ReferenceEquals(mine.Actor.Held, Hers(game, letter)), game.Step), string.Join("; ", game.Said));
        Assert.DoesNotContain(Hers(game, letter), mine.Actor.Inventory.Items);
        // On the host, ours: the host's copy holds it too, so her next click is replayed on what she holds.
        Assert.Same(letter.Item, game.Hers.Actor!.Held);
        Assert.Contains(game.Said, l => l.Contains("vault") && l.EndsWith("made"));

        // And the sheets after leave it in her hand; a click in her pack puts it down on both machines.
        for (int i = 0; i < 20; i++) game.Step();
        Assert.Same(Hers(game, letter), mine.Actor.Held);
        Assert.Equal(HandAction.Dropped, hands.ClickIn(pack, 3, 0).What);
        Assert.True(Until(() => letter.Item!.Location == Slot.Inventory && letter.Item.Cell == (3, 0) && game.Hers.Actor.Held is null, game.Step),
                    $"the host has it at {letter.Item!.Location:x} {letter.Item.Cell}");
    }

    [Fact]
    public void A_drop_on_her_vault_goes_to_its_first_free_cell_wherever_it_was_dropped()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var letter = Give(game, "AnezkaLetter", Slot.Inventory, 0, 0);
        var mine = game.Client.Avatar!;
        Assert.True(Until(() => game.Client.Mirror!.Find(letter.Guid)?.Item is { Location: Slot.Inventory }, game.Step), "never in her pack");
        var (hands, vault, pack) = Panes(files, mine);
        Assert.Equal(HandAction.Lifted, hands.ClickIn(pack, 0, 0).What);
        Assert.True(Until(() => ReferenceEquals(game.Hers.Actor!.Held, letter.Item), game.Step), "the host never lifted it");

        // Dropped on the vault at 6,4: asked, still in her hand until the host answers.
        var (x, y) = Cell(vault, 6, 4);
        Assert.Contains(VaultMoves.Asked, hands.Click(x, y).Say);
        Assert.Same(Hers(game, letter), mine.Actor!.Held);

        // The host's free-cell search starts from 0,0: it lands in the first free cell, on both machines.
        Assert.True(Until(() => mine.Actor.Held is null && Hers(game, letter) is { Location: Slot.Vault, Cell: (0, 0) }, game.Step),
                    string.Join("; ", game.Said));
        Assert.Contains(Hers(game, letter), mine.Actor.Inventory.Items);
        Assert.Equal((Slot.Vault, (0, 0)), (letter.Item!.Location, letter.Item.Cell));
        Assert.Null(game.Hers.Actor!.Held);
    }

    [Fact]
    public void A_drop_on_one_vault_item_swaps_it_into_her_hand()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var kept = Give(game, "AnezkaLetter", Slot.Vault, 0, 0);
        var brought = Give(game, "AnezkaLetter", Slot.Inventory, 1, 0);
        var mine = game.Client.Avatar!;
        Open(game);
        Assert.True(Until(() => game.Client.Mirror!.Find(kept.Guid) is not null && game.Client.Mirror.Find(brought.Guid) is not null, game.Step));
        var (hands, vault, pack) = Panes(files, mine);
        Assert.Equal(HandAction.Lifted, hands.ClickIn(pack, 1, 0).What);
        Assert.True(Until(() => ReferenceEquals(game.Hers.Actor!.Held, brought.Item), game.Step), "the host never lifted it");

        // One item under it: the swap, (her, brought, kept) — kept out first, so brought takes its cell.
        var (x, y) = Cell(vault, 0, 0);
        Assert.Contains("for", hands.Click(x, y).Say);
        Assert.True(Until(() => ReferenceEquals(mine.Actor!.Held, Hers(game, kept)), game.Step), string.Join("; ", game.Said));
        Assert.Equal((Slot.Vault, (0, 0)), (Hers(game, brought).Location, Hers(game, brought).Cell));
        Assert.Same(kept.Item, game.Hers.Actor!.Held);
        Assert.Equal((Slot.Vault, (0, 0)), (brought.Item!.Location, brought.Item.Cell));
    }

    [Fact]
    public void A_drop_over_two_vault_items_is_refused_on_her_machine_and_nothing_is_sent()
    {
        using var files = Install.Open();
        using var game = Start(files);
        Give(game, "AnezkaLetter", Slot.Vault, 0, 0);
        Give(game, "AnezkaLetter", Slot.Vault, 0, 1);
        var sword = Give(game, "broadsword", Slot.Inventory, 0, 0);
        Assert.True(sword.Item!.IconCells.Down >= 2, "the sword must span two rows");
        var mine = game.Client.Avatar!;
        Open(game);
        Assert.True(Until(() => mine.Actor!.Inventory.Items.Count(i => i.Location == Slot.Vault) == 2
                                && game.Client.Mirror!.Find(sword.Guid) is not null, game.Step));
        var (hands, vault, pack) = Panes(files, mine);
        Assert.Equal(HandAction.Lifted, hands.ClickIn(pack, 0, 0).What);
        var (x, y) = Cell(vault, 0, 0);
        Assert.Equal(HandAction.Refused, hands.Click(x, y).What);
        for (int i = 0; i < 20; i++) game.Step();
        Assert.DoesNotContain(game.Said, l => l.Contains("vault"));
        Assert.Same(Hers(game, sword), mine.Actor!.Held);
    }

    [Fact]
    public void Her_whole_vault_reaches_her_machine_as_the_host_opens_it_and_the_sheets_leave_it_alone()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var mine = game.Client.Avatar!;
        // Forty letters in her vault (the earlier sheet carried 24 at most, and never the vault's), and one in her pack.
        for (int i = 0; i < 40; i++) Give(game, "AnezkaLetter", Slot.Vault, i % 10, i / 10);
        var packed = Give(game, "AnezkaLetter", Slot.Inventory, 2, 0);
        Assert.True(Until(() => game.Client.Mirror!.Find(packed.Guid) is not null, game.Step), "the sheet never brought her pack");
        Assert.DoesNotContain(mine.Actor!.Inventory.Items, i => i.Location == Slot.Vault);
        int asked = 0;
        game.Client.VaultAsked += () => asked++;
        Open(game);
        Assert.True(Until(() => asked == 1, game.Step));
        // The contents came first (reliable and in order), in one message (the earlier wire's went in three parts).
        Assert.Equal(40, mine.Actor.Inventory.Items.Count(i => i.Location == Slot.Vault));
        Assert.Contains(mine.Actor.Inventory.Items, i => i.Location == Slot.Vault && i.Cell == (9, 3));
        for (int i = 0; i < 40; i++) game.Step();                                  // a few sheets
        Assert.Equal(40, mine.Actor.Inventory.Items.Count(i => i.Location == Slot.Vault));
        Assert.Equal((Slot.Inventory, (2, 0)), (Hers(game, packed).Location, Hers(game, packed).Cell));
    }

    [Fact]
    public void The_host_refuses_what_1_1_refuses_and_an_item_in_that_finds_no_room_leaves_the_item_out_nowhere()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var hers = game.Hers;
        var letter = Give(game, "AnezkaLetter", Slot.Vault, 0, 0);
        // The mover must be an actor or a player; the item out an item, weapon or armour.
        var prop = game.Host.Things.First(t => t.Type == ThingType.Prop);
        Assert.False(VaultMoves.Host(game.Host, new VaultMove(prop.Guid, 0, letter.Guid)));
        Assert.False(VaultMoves.Host(game.Host, new VaultMove(hers.Guid, 0, prop.Guid)));
        // Ours: nothing that is not hers goes in, or comes out.
        var loose = game.Host.Make("AnezkaLetter")!;
        Assert.False(VaultMoves.Host(game.Host, new VaultMove(hers.Guid, loose.Guid, 0)));
        Assert.Equal(Slot.Vault, letter.Item!.Location);

        // A vault full of letters, and a sword to go in for the one at 0,0: 1.1 takes the letter out first,
        // then finds no room for the sword and stops — the letter in nobody's keeping.
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 10; x++)
                if ((x, y) != (0, 0)) Give(game, "AnezkaLetter", Slot.Vault, x, y);
        var sword = Give(game, "broadsword", Slot.Inventory, 0, 0);
        Assert.False(VaultMoves.Host(game.Host, new VaultMove(hers.Guid, sword.Guid, letter.Guid)));
        Assert.DoesNotContain(letter.Item, hers.Actor!.Inventory.Items);
        Assert.Null(letter.Owner);
        Assert.Null(hers.Actor.Held);
        Assert.Equal(Slot.Inventory, sword.Item!.Location);
    }

    [Fact]
    public void Her_machine_keeps_one_pane_and_off_takes_down_whichever_is_up()
    {
        var pane = new NetPane();
        Assert.True(pane.Take(NetPane.Pause, true));
        Assert.Equal(NetPane.PauseFile, pane.File);
        // A load during the pause shows nothing (the load flag is set) and its end takes the pause menu down.
        Assert.False(pane.Take(NetPane.Load, true));
        Assert.Equal(NetPane.Pause, pane.Up);
        Assert.True(pane.Take(NetPane.Load, false));
        Assert.Null(pane.Up);
        Assert.False(pane.Take(NetPane.Pause, false));
        // Any number but 0 is the pause menu.
        Assert.True(pane.Take(7, true));
        Assert.Equal(NetPane.PauseFile, pane.File);
        pane.Take(0, false);
        Assert.True(pane.Take(NetPane.Load, true));
        Assert.Equal(NetPane.LoadFile, pane.File);
    }

    [Fact]
    public void The_Storytellers_pause_puts_the_pause_menu_up_on_her_machine_and_resume_takes_it_down()
    {
        using var files = Install.Open();
        using var game = Start(files);
        game.Server.SetPaused(true);
        Assert.True(Until(() => game.Client.Pane.Up == NetPane.Pause, game.Step));
        Assert.True(game.Client.Paused);
        // `ui/pausemenu.nui`: its two quads at 240,145.
        var assets = new UiAssets(files);
        var quads = game.Client.Pane.Quads(assets.Pane).ToList();
        Assert.Equal(2, quads.Count);
        Assert.Equal((240f, 145f, 128f, 64f), (quads[0].X, quads[0].Y, quads[0].W, quads[0].H));
        Assert.Equal((368f, 145f, 32f, 64f), (quads[1].X, quads[1].Y, quads[1].W, quads[1].H));
        game.Server.SetPaused(false);
        Assert.True(Until(() => game.Client.Pane.Up is null && !game.Client.Paused, game.Step));
        Assert.Empty(game.Client.Pane.Quads(assets.Pane));
    }

    [Fact]
    public void A_place_the_host_loads_puts_the_load_screen_up_on_her_machine_while_it_loads()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        story.Current = story.Data.ReviveLocation;
        int builds = 0;
        Location Build(string level, string? scene, int entrance)
        {
            builds++;
            // The "on" went before the load, at once (the transport flushed): her machine has it now,
            // with the host still loading, and her world is not paused by it.
            Assert.True(Until(() => game.Client.Pane.Up == NetPane.Load, () => game.Client.Poll(1f / 30)), "no load screen during the load");
            Assert.False(game.Client.Paused);
            return Location.Load(files, level, scene: scene, entrance: entrance);
        }
        var campaign = new Campaign { Chronicle = story, Build = Build, Here = game.Host, LoadScreen = game.Server.LoadScreen };
        int other = Enumerable.Range(0, story.Count).First(i => i != story.Current && story.At(i) is { LevelName.Length: > 0 });
        Assert.NotNull(campaign.Visit(other, 0));
        Assert.Equal(1, builds);
        // Off after it.
        Assert.True(Until(() => game.Client.Pane.Up is null, game.Step));
        // A place still loaded is not built again, and says nothing (the load's first test).
        int shown = 0;
        campaign.LoadScreen = on => { if (on) shown++; };
        Assert.NotNull(campaign.Visit(other, 0));
        Assert.Equal(0, shown);
    }
}
