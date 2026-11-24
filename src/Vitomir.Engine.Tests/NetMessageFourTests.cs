// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Message 4's whole body and the torch on other machines**.
/// 1.1's flags message carries, beside a thing's flags, a character's aura, blood,
/// mana, frenzy and money, an item's count and flags, a weapon's two loads and a container's state; it goes when the
/// setters mark the thing (the send tests `THING_FLAG_MODIFIED`), which a change of flags, teams, aura, money or item
/// flags does and a change of the stats, the count or the loads does not — those ride on the next one. And the kit's
/// utility item goes through its setter on a player's machine: another character's torch rides
/// his hand there, without its flame (<see cref="MadeHere.ByScript"/>). These run a host and two players
/// on localhost, and read the wire's record on its own.
/// </summary>
public class NetMessageFourTests
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard, not a speed test (levels fork, 4 s was too few on a busy laptop)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Location Place;
        public required Simulation Sim;
        public required EffectDefinitions Effects;
        public int Ms;
        public void Step()
        {
            Sim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
        public void Step()
        {
            Client.Poll(1f / 30);
            Sim?.Step(1f / 30);
        }
        public WorldThing? Copy(WorldThing host) => Client.Mirror?.Find(host.Guid);
        public void Dispose() => Client.Dispose();
    }

    static Game Host(Vfs.GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Place = place, Sim = new Simulation(place), Effects = new EffectDefinitions(files) };
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }),
        };
        p.Client.Seen.Effects = game.Effects;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
            p.Place.Session = new Session { Here = p.Place };
            p.Sim = new Simulation(p.Place);
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    static WorldThing Body(Game game, string name) => game.Server.Players.Single(p => p.Name == name).Avatar!;

    [Fact]
    public void Every_kind_reads_back_as_written_and_only_the_marked_part_sends_it()
    {
        ThingFlags[] all =
        [
            new() { Guid = 7, Kind = ThingFlags.None, Render = 0x2, Thing = 0x40, Collide = 2 },
            new() { Guid = 8, Kind = ThingFlags.Character, Render = 1, Thing = 2, Collide = 2, Actor = 0x80, Actor2 = 0x2,
                    Aura = 5, Team = 12, OriginalTeam = 3, Blood = 7.5f, Mana = 2f, Frenzy = 1.25f, Cash = 200 },
            new() { Guid = 9, Kind = ThingFlags.ItemKind, Amount = 11, ItemFlags = 0x200002 },
            new() { Guid = 10, Kind = ThingFlags.WeaponKind, Load1 = 6, Load2 = 1, ItemFlags = 0x2 },
            new() { Guid = 11, Kind = ThingFlags.ContainerKind, State = 1 },
        ];
        var w = new NetWriter(Msg.Flags);
        foreach (var f in all) f.Write(w);
        var r = new NetReader(w.ToArray());
        foreach (var f in all) Assert.Equal(f, ThingFlags.Read(r));

        // The stats, the count, the loads and the state ride; money, aura, teams and item flags send.
        var a = all[1];
        Assert.Equal(a.Trigger(), (a with { Blood = 1f, Mana = 9f, Frenzy = 3f }).Trigger());
        Assert.Equal(all[2].Trigger(), (all[2] with { Amount = 1 }).Trigger());
        Assert.Equal(all[3].Trigger(), (all[3] with { Load1 = 0, Load2 = 0 }).Trigger());
        Assert.Equal(all[4].Trigger(), (all[4] with { State = 0 }).Trigger());
        Assert.NotEqual(a.Trigger(), (a with { Cash = 201 }).Trigger());
        Assert.NotEqual(a.Trigger(), (a with { Aura = 6 }).Trigger());
        Assert.NotEqual(all[2].Trigger(), (all[2] with { ItemFlags = 0x2 }).Trigger());
    }

    [Fact]
    public void A_characters_blood_mana_and_frenzy_ride_on_his_next_flags_change_and_his_money_sends_one()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var his = Body(game, "Bob").Actor!;
        for (int i = 0; i < 20; i++) All();
        var copy = anna.Copy(Body(game, "Bob"))!.Actor!;
        float was = copy.BaseStat[(int)ActorStat.Blood];

        // Blood spent on the host (the stat write marks only the owner's stats message, flags2 0x10000000): not sent to Anna.
        float blood = his.SetBaseStat((int)ActorStat.Blood, was - 7f);
        Assert.NotEqual(was, blood);
        for (int i = 0; i < 20; i++) All();
        Assert.Equal(was, copy.BaseStat[(int)ActorStat.Blood]);

        // A flags change sends message 4, and the stats ride on it, each held to the bounds and
        // pool of the copy on her machine, which the setter clamps against there.
        his.Flags |= Actor.Flag.TalkTo;
        Assert.True(Until(() => (copy.Flags & Actor.Flag.TalkTo) != 0, All), "talk-to never reached her");
        Assert.Equal(blood, copy.BaseStat[(int)ActorStat.Blood]);
        foreach (var stat in new[] { ActorStat.Mana, ActorStat.Frenzy })
            Assert.Equal(copy.ClampStat((int)stat, his.BaseStat[(int)stat]), copy.BaseStat[(int)stat]);

        // Money is marked: it sends one on its own.
        his.Cash = 777;
        Assert.True(Until(() => copy.Cash == 777, All), $"his money on her machine is {copy.Cash}");
        // The aura is marked too (SetActorPredefinedAura).
        his.Aura = 9;
        Assert.True(Until(() => copy.Aura == 9, All), $"his aura on her machine is {copy.Aura}");
    }

    [Fact]
    public void A_worn_weapons_flags_reach_every_machine_and_its_loads_ride_on_them()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var him = Body(game, "Bob");
        var sword = game.Place.Make("broadsword")!;
        sword.Owner = him;
        him.Actor!.Inventory.Add(sword.Item!);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(him, sword.Item!, Slot.None, game.Effects, game.Ms));
        Assert.True(Until(() => anna.Copy(sword)?.Item?.Weapon is not null, All), "the sword never reached her");
        var there = anna.Copy(sword)!.Item!;

        // A load changed on the host rides; `SetItemFlags` sends.
        sword.Item!.Weapon!.Attacks[0].Ammo = 3;
        for (int i = 0; i < 10; i++) All();
        Assert.NotEqual(3, there.Weapon!.Attacks[0].Ammo);
        sword.Item.Flags |= ItemFlags.NoSell;
        Assert.True(Until(() => (there.Flags & ItemFlags.NoSell) != 0, All), "the item flags never reached her");
        Assert.Equal(3, there.Weapon.Attacks[0].Ammo);
    }

    [Fact]
    public void A_torch_in_another_characters_hand_rides_it_on_every_machine_dark_and_a_late_joiner_sees_it()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");

        // The host puts a torch in Bob's off hand: in the world, riding his hand.
        var him = Body(game, "Bob");
        var torch = game.Place.Make("torchU")!;
        torch.Owner = him;
        him.Actor!.Inventory.Add(torch.Item!);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(him, torch.Item!, Slot.None, game.Effects, game.Ms));
        Assert.Same(him, torch.AttachedTo);

        // On Anna's machine his copy holds it in the same way: riding his hand, not
        // highlighted, his utility item, the stance on.
        Assert.True(Until(() => anna.Copy(torch) is { } t && ReferenceEquals(t.AttachedTo, anna.Copy(him)), All),
                    $"the torch rides {anna.Copy(torch)?.AttachedTo?.Template ?? "nothing"} on her machine");
        var copy = anna.Copy(him)!.Actor!;
        Assert.Same(anna.Copy(torch)!.Item, copy.Utility);
        Assert.True(Utility.Stance(copy));
        Assert.True(anna.Copy(torch)!.Has(WorldThing.Flag.NoHighlight));
        // Dark: nothing rides the torch there — its flame is its script's, on the host alone.
        Assert.DoesNotContain(anna.Place!.Things, t => ReferenceEquals(t.AttachedTo, anna.Copy(torch)));
        // It follows his hand, not the snapshots: where it is is where his hand is, however he stands.
        for (int i = 0; i < 10; i++) All();
        Assert.True(anna.Copy(torch)!.InWorld);

        // A late joiner is told his kit on arriving, and holds it the same.
        using var cid = Join(files, game, "Cid", "MP_Serena");
        void Three() { All(); cid.Step(); }
        Assert.True(Until(() => cid.Client.Avatar is not null && cid.Copy(torch) is { } t && ReferenceEquals(t.AttachedTo, cid.Copy(him)), Three),
                    "the late joiner never saw it in his hand");

        // Put away (the take-off's utility case): out of his hand everywhere, the stance with it.
        Assert.True(Equipment.Unequip(him, torch.Item!, game.Effects));
        Assert.True(Until(() => anna.Copy(him)!.Actor!.Utility is null && cid.Copy(him)!.Actor!.Utility is null, Three),
                    "the torch stayed in his hand on the others' machines");
        Assert.False(Utility.Stance(anna.Copy(him)!.Actor));
        Assert.False(anna.Copy(torch) is { InWorld: true });
    }
}
