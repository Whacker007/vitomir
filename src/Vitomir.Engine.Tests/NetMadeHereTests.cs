// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a player's machine makes for itself, with a host and two players on localhost
/// against the install. 1.1's host sends nothing flagged `NO NET SYNCH`, because each
/// player's machine deals the host's blows and deaths again for their looks (messages 0xd
/// and 0xf), throws a gib kill's chunks under its **own** no-gore setting (0x1f/0x1b)
/// and strikes a head off itself (0x1f/0x19). These pin that the
/// blood, the sparks, the chunks and the head are made on each player's machine and never sent, that the hurt noise
/// is not relayed, that a death hides the weapon there too and a revive — told as 1.1 tells it,
/// message 0x20 — shows it again, and that what a script other than an
/// effect's makes flagged is kept back too, as 1.1 keeps it, unless the Fix `netprops` is on (the effects' scripts'
/// own, and the torch's flame, in `NetEffectScriptTests` in Vitomir.Scripting.Tests).
/// </summary>
public class NetMadeHereTests
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
        public readonly List<WorldThing> Made = [];
        public readonly List<(WorldThing Thing, int Slot)> Heard = [];
        public void Step()
        {
            Client.Poll(1f / 30);
            Sim?.Step(1f / 30);
        }
        public WorldThing? Copy(WorldThing host) => Client.Mirror?.Find(host.Guid);
        public List<WorldThing> MadeOf(string prefix) => Made.Where(t => t.Template.StartsWith(prefix, StringComparison.Ordinal)).ToList();
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

    static Player Join(Vfs.GameFiles files, Game game, string name, string template, bool? noBlood)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }),
        };
        p.Client.Seen.NoBlood = noBlood;
        p.Client.Seen.Effects = game.Effects;
        p.Client.Seen.Made += p.Made.Add;
        p.Client.Seen.Heard += (t, slot) => p.Heard.Add((t, slot));
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

    /// <summary>A character made on the host beside <paramref name="near"/>, once every player's machine has it.</summary>
    static WorldThing Beside(Game game, WorldThing near, string template, Action all, params Player[] players)
    {
        var made = game.Place.Place(template, near.Position + new Vector3(60f, 0f, 0f), Vector3.Zero, near.Sector)!;
        Assert.NotNull(made);
        Assert.True(Until(() => players.All(p => p.Copy(made) is { InWorld: true }), all), $"{template} never reached the players");
        return made;
    }

    [Fact]
    public void A_blow_s_blood_and_sparks_are_made_on_each_player_s_machine_and_never_sent()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        using var bob = Join(files, game, "Bob", "MP_Erik", noBlood: true);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var his = Body(game, "Bob");
        var hers = Body(game, "Anna");
        var rng = new Ai.Rng(5);

        // A blow that draws blood: the host's own blood is kept back.
        Assert.False(Damage.Apply(his, hers, HitKind.Melee, DamageType.Lethal, 3f, rng, game.Ms, skipSoak: true).Killed);
        var hostBlood = game.Place.Things.Where(t => t.Template == WoundEffects.Blood && t.InWorld).ToList();
        Assert.NotEmpty(hostBlood);
        Assert.All(hostBlood, t => Assert.True(NetSynch.Kept(t)));

        // Her machine makes its own, hung on its copy of him; his machine,
        // with its no-gore setting on, makes none.
        Assert.True(Until(() => anna.MadeOf(WoundEffects.Blood).Count > 0, All), "her machine made no blood");
        var hers1 = anna.MadeOf(WoundEffects.Blood)[0];
        Assert.Same(anna.Copy(his), hers1.AttachedTo);
        Assert.Equal(0, hers1.Guid & ThingGuids.HostBit);                           // her own number, not the host's
        for (int i = 0; i < 10; i++) All();
        Assert.Empty(bob.MadeOf(WoundEffects.Blood));
        foreach (var b in hostBlood)
        {
            Assert.Null(anna.Copy(b));                                                  // never sent
            Assert.Null(bob.Copy(b));
        }
        // The hurt noise is each machine's own too (its relay flag 0): not relayed.
        Assert.DoesNotContain(anna.Heard, h => h.Slot == FoleyEvent.Damage + (int)DamageType.Lethal);

        // A burn (the damage reaction's fire branch): the flame's light on the root bone, on her machine alone.
        anna.Made.Clear();
        Assert.False(Damage.Apply(his, hers, HitKind.Melee, DamageType.Fire, 2f, rng, game.Ms, skipSoak: true).Killed);
        var hostLight = game.Place.Things.Single(t => t.Template == WoundEffects.FireLight && t.InWorld);
        Assert.True(NetSynch.Kept(hostLight));
        Assert.True(Until(() => anna.MadeOf(WoundEffects.FireLight).Count > 0, All), "her machine made no flame");
        Assert.True(Until(() => bob.MadeOf(WoundEffects.FireLight).Count > 0, All), "his machine made no flame");  // not gore
        Assert.Null(anna.Copy(hostLight));

        // The killing blow: his body bleeds on her machine for its 8 s.
        anna.Made.Clear();
        Assert.True(Damage.Apply(his, hers, HitKind.Melee, DamageType.Lethal, 10_000f, rng, game.Ms, skipSoak: true).Killed);
        Assert.True(Until(() => anna.MadeOf(WoundEffects.Blood).Count > 0, All), "his death drew no blood on her machine");
        Assert.Same(anna.Copy(his), anna.MadeOf(WoundEffects.Blood)[0].AttachedTo);
        Assert.All(game.Place.Things.Where(t => t.Template == WoundEffects.Blood), t => Assert.Null(anna.Copy(t)));
    }

    [Fact]
    public void A_gib_kill_throws_each_machine_s_own_chunks_under_its_own_gore_setting_and_none_is_sent()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        using var bob = Join(files, game, "Bob", "MP_Erik", noBlood: true);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");
        var rat = Beside(game, hers, "Rat", All, anna, bob);                      // gib class 2: gib_2_0 … gib_2_3
        var hersRat = anna.Copy(rat)!;
        var hisRat = bob.Copy(rat)!;

        // The host gibs it (the message first, then its own sixteen asks, then the body).
        int thrown = Damage.Gib(rat, rat.Actor!, new Ai.Rng(9));
        Assert.Equal(8, thrown);                                                  // four pieces, each twice
        var hostChunks = game.Place.Things.Where(t => t.Template.StartsWith("gib_2_", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, hostChunks.Count);
        Assert.All(hostChunks, t => Assert.True(NetSynch.Kept(t)));

        // Her machine (gore on) throws its own eight and takes the body out; his (gore off) throws none, and the
        // body goes there too (whatever the setting).
        Assert.True(Until(() => !hersRat.InWorld && !hisRat.InWorld, All), "the body stayed on a player's machine");
        Assert.Equal(8, anna.MadeOf("gib_2_").Count);
        Assert.Empty(bob.MadeOf("gib_2_"));
        Assert.All(anna.MadeOf("gib_2_"), t => Assert.Equal(0, t.Guid & ThingGuids.HostBit));
        Assert.All(hostChunks, t => { Assert.Null(anna.Copy(t)); Assert.Null(bob.Copy(t)); });   // never sent, never flown

        // A host with its gore off still tells them, and a player with hers on throws her own.
        var rat2 = Beside(game, hers, "Rat", All, anna, bob);
        anna.Made.Clear();
        Gore.NoBlood = true;
        try { Assert.Equal(0, Damage.Gib(rat2, rat2.Actor!, new Ai.Rng(9))); }
        finally { Gore.NoBlood = false; }
        Assert.True(Until(() => anna.MadeOf("gib_2_").Count == 8, All), "her machine threw nothing for a no-gore host");
    }

    [Fact]
    public void A_head_struck_off_on_the_host_is_struck_off_on_each_player_s_machine_by_its_own_setting()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        using var bob = Join(files, game, "Bob", "MP_Erik", noBlood: true);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");
        var zombu = Beside(game, hers, "zombu", All, anna, bob);

        Assert.True(Beheading.Behead(zombu, hers, new Ai.Rng(4), game.Ms));
        var hostHead = game.Place.Things.Single(t => t.Template == "zombu_head");
        Assert.True(NetSynch.Kept(hostHead));

        // Her machine strikes its own copy's head off: a head of her own, the body headless.
        Assert.True(Until(() => anna.MadeOf("zombu_head").Count == 1, All), "no head on her machine");
        Assert.True(anna.Copy(zombu)!.Actor!.Has2(Actor.Flag2.IsBeheaded));
        Assert.Null(anna.Copy(hostHead));
        // No effect put on there (the effect's add refuses one not from the network), and the host's is `NOT SYNCED` (0x80).
        for (int i = 0; i < 10; i++) All();
        Assert.DoesNotContain(anna.Copy(zombu)!.Effects.All, e => e.Name == Beheading.Effect);
        // His machine, gore off: the head stays on there (the beheading's no-gore branch, whose kill does nothing on a player's machine).
        Assert.Empty(bob.MadeOf("zombu_head"));
        Assert.False(bob.Copy(zombu)!.Actor!.Dead);
    }

    /// <summary>
    /// A death not dealt by a blow — a script's `KillActor` — is told as every death is (message
    /// 0xf): cried and written in red on a player's machine, which makes its looks itself.
    /// </summary>
    [Fact]
    public void A_death_not_dealt_by_a_blow_is_told_to_the_players_machines_too()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");

        Assert.True(Damage.Kill(hers, null, HitKind.Effect, DamageType.Lethal, game.Ms));
        Assert.True(Until(() => anna.Place!.Session!.Messages.Lines.Any(l => l.Colour == Ui.MessageLog.Red), All),
                    "her death was never told on her machine");
        Assert.True(Until(() => anna.MadeOf(WoundEffects.Blood).Count > 0, All), "her machine made nothing of her death");
    }

    /// <summary>
    /// **The weapon at a death and a revive**. The host's death hides the weapon's picture
    /// and so does her machine's, dealing the death again (forced); the host's
    /// revive sends message 0x20 at its end, and her machine revives its own copy with
    /// it: the weapon shown again. The awakening noise is played on each machine and
    /// not relayed (the sound's relay flag 0).
    /// </summary>
    [Fact]
    public void A_death_hides_the_weapon_on_her_machine_and_a_revive_shows_it_again()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");
        Assert.True(Until(() => anna.Copy(hers) is { InWorld: true }, All));
        var copy = anna.Copy(hers)!;
        Assert.False(copy.Actor!.WeaponHidden);

        Assert.True(Damage.Kill(hers, null, HitKind.Effect, DamageType.Lethal, game.Ms));
        Assert.True(hers.Actor!.WeaponHidden);
        Assert.True(Until(() => copy.Actor.WeaponHidden, All), "her machine never hid the weapon");
        Assert.True(Until(() => copy.Actor.Dead, All));

        Damage.Revive(hers, 1f, 1f, sound: true);
        Assert.False(hers.Actor.WeaponHidden);
        Assert.True(Until(() => !copy.Actor.WeaponHidden, All), "her machine never showed the weapon again");
        Assert.False(copy.Actor.Dead);
        Assert.Equal(copy.Actor.MaxHealth, copy.Actor.Health);
        for (int i = 0; i < 10; i++) All();
        Assert.DoesNotContain(anna.Heard, h => h.Slot == FoleyEvent.DeathAwaken);   // not relayed: her machine plays its own
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void What_a_script_other_than_an_effect_s_makes_flagged_is_kept_back_unless_the_netprops_fix_is_on(bool netprops)
    {
        using var files = Install.Open();
        using var game = Host(files);
        game.Place.Fixes = new Fixes { NetProps = netprops };                 // the host's switch
        using var anna = Join(files, game, "Anna", "MP_Serena", noBlood: false);
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");

        // A pad made on the host by a script that no player's machine runs — as a place's or a thing's script makes
        // a flagged thing (`ConveyerHold`'s blood, `MysticSpawner`'s magic): 1.1's host sends nothing so flagged,
        // so her machine never has it; the Fix sends it.
        WorldThing pad;
        using (MadeHere.ByScript(game.Place)) pad = game.Place.Spawn("sawToothBlue", hers)!;
        Assert.True((pad.Flags & WorldThing.Flag.NoNetSync) != 0);
        Assert.True(MadeHere.Scripted(pad));
        Assert.False(MadeHere.Own(pad));
        Assert.Equal(!netprops, NetSynch.Kept(pad));
        if (netprops)
            Assert.True(Until(() => anna.Copy(pad) is { InWorld: true }, All), "the pad never reached her");
        else
        {
            for (int i = 0; i < 30; i++) All();
            Assert.Null(anna.Copy(pad));
        }

        // Made outside any script — the engine's own — it is not a script's, and is sent either way (ours: the engine's
        // flagged things that every machine does not make are the effects' and the blows', marked where they are made).
        var loose = game.Place.Spawn("sawToothBlue", hers)!;
        Assert.False(NetSynch.Kept(loose));
        Assert.True(Until(() => anna.Copy(loose) is { InWorld: true }, All), "the engine's pad never reached her");
    }
}
