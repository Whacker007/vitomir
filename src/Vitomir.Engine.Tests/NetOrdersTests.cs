// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The orders a player's machine sends that the first stage left out, with a
/// host and a player on localhost against the install: a discipline cast on the player's machine goes
/// to the host by its name (1.1's message 0x18) and is cast there through the one
/// cast order; the host refuses an order while paused (`UI_STSBPAUSE`); and a move the
/// player makes in their own pack reaches the host (1.1's 0x13 … 0x17), so the next sheet does not put
/// it back; and a projectile the host fires flies on the player's machine as a picture of it (1.1's 0x50).
/// </summary>
public class NetOrdersTests
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

    [Fact]
    public void A_discipline_cast_on_a_players_machine_is_cast_by_the_host()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var powers = new DisciplineDefinitions(files);
        var hers = game.Hers;
        var healing = DisciplineBook.Make(hers, powers["BloodHealing"]!)!;
        DisciplineBook.SetLevel(hers, powers, "BloodHealing", 1);
        hers.Actor!.Stat[(int)ActorStat.Blood] = hers.Actor.Stat[(int)ActorStat.BloodPool];
        // Her machine knows it too, and casts it: the order goes to the host by name.
        var mine = game.Client.Avatar!;
        var local = DisciplineBook.Make(mine, powers["BloodHealing"]!)!;
        Assert.Equal(OrderResult.Started, Cast.Order(mine, null, null, local, InputModifier.None));
        Assert.NotEqual(ActionMode.Cast, mine.Actor!.Action);                           // not on her machine
        Assert.True(Until(() => healing.CastCount > 0 || hers.Actor.Action == ActionMode.Cast || healing.RecastUntil > 0, game.Step),
                    "the host never cast it");
        Assert.Same(healing, hers.Actor.Disciplines.Current);
    }

    [Fact]
    public void A_fireball_the_host_fires_flies_on_the_players_machine_and_bursts_there_as_the_hosts_does()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var hers = game.Hers;
        var from = Projectiles.Muzzle(hers, new System.Numerics.Vector3(0f, 30f, 0f));
        var shot = Projectiles.Fire(game.Host, hers, "proj_fireball", from, Projectiles.Facing(System.Numerics.Vector3.UnitX), hers.Guid);
        Assert.NotNull(shot);
        // Her machine flies a picture of it, known by the host's GUID, hers.
        WorldThing? copy = null;
        Assert.True(Until(() => (copy = game.Client.Mirror!.Find(shot!.Guid)) is not null, game.Step), "never fired there");
        Assert.Equal(ThingType.Projectile, copy!.Type);
        Assert.Same(game.Client.Avatar, game.Mine!.Thing(copy.SpawnedBy));
        Assert.True(copy.Velocity.Length() > 0f);
        // The host's burst comes as a thing made, and the picture goes when the host's shot goes.
        Assert.True(Until(() => shot!.Removed && !copy.InWorld, game.Step), "the picture outlived the shot");
        Assert.True(Until(() => game.Mine.Things.Any(t => t.Type == ThingType.Explosion) || game.Host.Things.All(t => t.Type != ThingType.Explosion), game.Step));
        Assert.True(game.Mine.Things.Count(t => t.Type == ThingType.Explosion) <= 1);
    }

    [Fact]
    public void A_move_in_a_players_own_pack_is_made_by_the_host_and_the_next_sheet_keeps_it()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var hers = game.Hers;
        var letter = game.Host.Make("AnezkaLetter")!;
        letter.Item!.Location = Slot.Inventory;
        letter.Item.Cell = (0, 0);
        letter.Owner = hers;
        hers.Actor!.Inventory.Add(letter.Item);
        var mine = game.Client.Avatar!;
        Assert.True(Until(() => mine.Actor!.Inventory.Items.Any(i => i.Thing?.Template == "AnezkaLetter"), game.Step), "never in her pack");

        // Her own pane and hands, as the game has them: lift the letter and put it down four cells along.
        var pane = new Ui.InventoryPane(new Ui.UiAssets(files).Pane(Ui.InventoryPane.PaneName)) { Subject = mine.Actor };
        var hands = new Ui.InventoryHands(pane, mine, new EffectDefinitions(files));
        var pack = pane.Grids().Single(g => g.Slot == Slot.Inventory);
        var said = new List<string>();
        game.Server.Log += said.Add;
        Assert.Equal(Ui.HandAction.Lifted, hands.ClickIn(pack, 0, 0).What);
        Assert.Equal(Ui.HandAction.Dropped, hands.ClickIn(pack, 4, 1).What);
        // The host made the same two clicks on her character there.
        Assert.True(Until(() => letter.Item.Cell == (4, 1) && hers.Actor.Held is null, game.Step), $"the host has it at {letter.Item.Cell}: {string.Join("; ", said)}");
        // And a second and a third sheet leave it where she put it.
        for (int i = 0; i < 40; i++) game.Step();
        Assert.Equal((4, 1), mine.Actor!.Inventory.Items.Single(i => i.Thing?.Template == "AnezkaLetter").Cell);
    }

    [Fact]
    public void While_the_game_is_paused_the_host_refuses_a_players_order_in_1_1s_words()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var posted = new List<string>();
        game.Client.Posted += (text, _) => posted.Add(text);
        game.Client.Find = key => key;
        game.Server.SetPaused(true);
        Assert.True(Until(() => game.Client.Paused, game.Step));
        var me = game.Mine!.Player!;
        var from = game.Hers.Position;
        Assert.True(me.WalkTo(from + new System.Numerics.Vector3(100, 0, 0), game.Mine.Walk!.Find(from, 256f)));
        Assert.True(Until(() => posted.Contains("UI_STSBPAUSE"), game.Step), string.Join("; ", posted));
    }
}
