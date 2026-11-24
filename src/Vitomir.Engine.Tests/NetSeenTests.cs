// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What each machine sees of the others, with a host and players on localhost
/// against the install: a sword put on is in the wearer's hand on every machine and a late joiner is
/// told it (0x13 … 0x15); an effect goes on and off every copy of the character,
/// an `OWNER ONLY` one only on its owner's, and a player's right click on a cancellable icon asks the
/// host; a blow is shown and a death cried and written on the
/// players' machines (0xd); foley and named sounds are relayed;
/// a thing hidden, turned hostile or no longer talked to is so everywhere, a newcomer is sent
/// it, and a machine keeps its own `REFLECTION ONLY` (message 4); and a hitscan
/// shot is fired again on the players' machines.
/// </summary>
public class NetSeenTests
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
        public void Step(float seconds = 1f / 30)
        {
            Sim.Step(seconds);
            Ms += (int)(seconds * 1000);
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
        public readonly List<(SeenBlow Blow, WorldThing? Victim)> Blows = [];
        public readonly List<(WorldThing Thing, int Slot)> Heard = [];
        public readonly List<(WorldThing Thing, string Name)> Sounds = [];
        public readonly List<WorldThing> Shots = [];
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        /// <summary>This machine's copy of the host's thing.</summary>
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
        p.Client.Seen.Struck += (b, victim, _) => p.Blows.Add((b, victim));
        p.Client.Seen.Heard += (t, slot) => p.Heard.Add((t, slot));
        p.Client.Seen.Sounded += (t, n) => p.Sounds.Add((t, n));
        p.Client.Seen.Shot += p.Shots.Add;
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

    static bool Playing(params Player[] players) => players.All(p => p.Client.Avatar is not null);

    [Fact]
    public void A_sword_put_on_is_in_the_wearers_hand_on_every_machine_and_a_late_joiner_sees_it()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");

        // The host puts a broadsword in Anna's hand.
        var hers = Body(game, "Anna");
        var sword = game.Place.Make("broadsword")!;
        sword.Owner = hers;
        hers.Actor!.Inventory.Add(sword.Item!);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(hers, sword.Item!, Slot.None, game.Effects, game.Ms));
        string held = hers.Actor.Weapon!.Name;

        // On Bob's machine and on her own, her character holds it.
        Assert.True(Until(() => bob.Copy(hers)?.Actor?.Weapon?.Name == held && anna.Client.Avatar?.Actor?.Weapon?.Name == held, All),
                    $"Bob sees {bob.Copy(hers)?.Actor?.Weapon?.Name}, Anna {anna.Client.Avatar?.Actor?.Weapon?.Name}");
        Assert.Contains(bob.Copy(hers)!.Actor!.Inventory.Items, i => i.Thing?.Guid == sword.Guid || i.Thing?.Template == "broadsword");

        // A late joiner is told every character's kit on arriving.
        using var cid = Join(files, game, "Cid", "MP_Serena");
        void Three() { All(); cid.Step(); }
        Assert.True(Until(() => cid.Client.Avatar is not null && cid.Copy(hers)?.Actor?.Weapon?.Name == held, Three),
                    $"Cid sees {cid.Copy(hers)?.Actor?.Weapon?.Name}");

        // Taken off, the built-in weapon is back in her hand everywhere.
        Assert.True(Equipment.Unequip(hers, sword.Item!, game.Effects));
        Assert.True(Until(() => bob.Copy(hers)?.Actor?.Weapon?.Name != held && cid.Copy(hers)?.Actor?.Weapon?.Name != held, Three),
                    "the sword stayed in her hand on the others' machines");
        Assert.Same(bob.Copy(hers)!.Actor!.DefaultWeapon, bob.Copy(hers)!.Actor!.Weapon);
        Assert.DoesNotContain(bob.Copy(hers)!.Actor!.Inventory.Items, i => (i.Location & Slot.Worn) != 0);
    }

    [Fact]
    public void An_effect_goes_on_and_off_every_copy_her_own_icon_shows_and_her_right_click_asks_the_host()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var hers = Body(game, "Anna");
        // Blood Strength (a stat, with an icon, cancellable — 0x200) and the plain strength effect the
        // disciplines' scripts use, flagged OWNER ONLY (0x40).
        var shown = game.Effects["ef_disc_bloodstrength"]!;
        var owners = game.Effects["ef_stat_strength"]!;
        Assert.True(shown.Has(EffectFlags.Cancellable) && !shown.Has(EffectFlags.OwnerOnly));
        Assert.True(owners.Has(EffectFlags.OwnerOnly));

        // On the host: on her copy on Bob's machine and on her own.
        var on = EffectRuntime.Apply(hers, shown, 0, game.Ms, durationMs: 60_000)!;
        Assert.True(Until(() => bob.Copy(hers) is { } seen && seen.Effects.All.Any(e => e.Name == shown.Name && !e.Spent)
                              && anna.Client.Avatar!.Effects.All.Any(e => e.Name == shown.Name && !e.Spent), All),
                    $"{shown.Name} never reached the players' machines");
        // Its icon is on her own screen (the effect icons over her own character's list).
        Assert.Contains(Ui.EffectIcons.Layout(anna.Client.Avatar, barShown: true), i => i.Effect.Name == shown.Name);

        // Its look: Cloak of Shadows' fade (MODELALPHA, put on by value as its script does).
        EffectRuntime.Apply(hers, game.Effects["ef_disc_cloakofshadows2"]!, 0, game.Ms, durationMs: 60_000, value: 0.35f);
        Assert.True(Until(() => bob.Copy(hers)!.ModelAlpha == 0.35f, All), $"her fade on Bob's machine is {bob.Copy(hers)!.ModelAlpha}");

        // An OWNER ONLY effect goes to her machine alone (the effect message's 0x40 test).
        EffectRuntime.Apply(hers, owners, 0, game.Ms, durationMs: 60_000);
        Assert.True(Until(() => anna.Client.Avatar!.Effects.All.Any(e => e.Name == owners.Name), All), $"{owners.Name} never reached her");
        for (int i = 0; i < 10; i++) All();
        Assert.DoesNotContain(bob.Copy(hers)!.Effects.All, e => e.Name == owners.Name);

        // Her right click on the cancellable icon asks the host, which ends it
        // there, and its end comes back to every machine.
        var mine = anna.Client.Avatar!.Effects.All.First(e => e.Name == shown.Name && !e.Spent);
        Assert.True(ClientSeen.Cancel(anna.Client.Avatar!, mine));
        Assert.False(mine.Spent);                                   // nothing here until the host says so
        Assert.True(Until(() => on.Spent, All), "the host never ended it");
        Assert.True(Until(() => !bob.Copy(hers)!.Effects.All.Any(e => e.Name == shown.Name && !e.Spent)
                              && !anna.Client.Avatar!.Effects.All.Any(e => e.Name == shown.Name && !e.Spent), All),
                    "its end never reached the players' machines");
    }

    [Fact]
    public void A_blow_is_shown_on_the_players_machines_and_a_death_is_cried_and_written()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var his = Body(game, "Bob");
        var hers = Body(game, "Anna");
        Assert.NotEqual("", his.Foley);

        // A blow that hurts: shown on her machine.
        var rng = new Ai.Rng(3);
        var harm = Damage.Apply(his, hers, HitKind.Melee, DamageType.Lethal, 6f, rng, game.Ms, skipSoak: true);
        Assert.False(harm.Killed);
        Assert.True(Until(() => anna.Blows.Count > 0, All), "the blow never reached her");
        var (blow, victim) = anna.Blows[0];
        Assert.Same(anna.Copy(his), victim);
        Assert.Equal(hers.Guid, blow.Attacker);
        Assert.Equal(6f, blow.Amount);
        Assert.False(blow.Killed);
        // His hurt noise is not relayed: her machine makes it itself in its own hit reaction (the foley with its fifth
        // argument 0, NetMadeHereTests).
        for (int i = 0; i < 10; i++) All();
        Assert.DoesNotContain(anna.Heard, h => h.Thing == anna.Copy(his) && h.Slot == FoleyEvent.Damage + (int)DamageType.Lethal);
        // Nobody is hurt twice: her machine deals nothing (Damage.Apply refuses there).
        float there = anna.Copy(his)!.Actor!.Health;
        Assert.Equal(0f, Damage.Apply(anna.Copy(his)!, anna.Copy(hers), HitKind.Melee, DamageType.Lethal, 6f, rng, 0, skipSoak: true).Taken);
        Assert.Equal(there, anna.Copy(his)!.Actor!.Health);

        // A killing blow: the death is told, and a party member's written in red on every machine.
        anna.Blows.Clear();
        Assert.True(Damage.Apply(his, hers, HitKind.Melee, DamageType.Lethal, 10_000f, rng, game.Ms, skipSoak: true).Killed);
        Assert.True(Until(() => anna.Blows.Any(b => b.Blow.Killed) && bob.Blows.Any(b => b.Blow.Killed), All), "the death never reached them");
        Assert.True(Until(() => anna.Copy(his)!.Actor!.Dead, All), "he is not dead on her machine");
        Assert.Equal(ThingType.Player, anna.Copy(his)!.Type);
        Assert.Contains(anna.Place!.Session!.Messages.Lines, l => l.Colour == Ui.MessageLog.Red);
        Assert.Contains(bob.Place!.Session!.Messages.Lines, l => l.Colour == Ui.MessageLog.Red);
    }

    [Fact]
    public void Foley_and_named_sounds_the_host_plays_are_played_on_the_players_machines_but_not_an_animations()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => Playing(anna), Two), "never played");
        var who = Body(game, "Anna");
        Assert.NotEqual("", who.Foley);

        // A foley and a named sound.
        Noise.Foley(who, FoleyEvent.VoxLostHumanity);
        HostSeen.Sound(who, "sound/test.wav", 200f, 5000f, 0.75f, ChannelFlag.ReleaseAtEnd);
        // An animation's keyframe noise is every machine's own, and is not relayed.
        using (HostSeen.Local()) Noise.Foley(who, FoleyEvent.AnimIdle);
        Assert.True(Until(() => anna.Heard.Count > 0 && anna.Sounds.Count > 0, Two), "nothing reached her");
        for (int i = 0; i < 10; i++) Two();
        Assert.Contains((anna.Copy(who)!, FoleyEvent.VoxLostHumanity), anna.Heard);
        Assert.DoesNotContain(anna.Heard, h => h.Slot == FoleyEvent.AnimIdle);
        Assert.Equal("sound/test.wav", anna.Sounds.Single().Name);
    }

    [Fact]
    public void A_character_hidden_turned_hostile_or_no_longer_talked_to_is_so_on_every_machine_and_a_newcomer_is_told()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var prop = game.Place.Things.First(t => t.Model.Length > 0 && t.Actor is null && WorldWatch.FromScene(game.Place, t.Guid));
        var his = Body(game, "Bob");

        // Her first-person look's own bit on her own character stays hers (ours).
        anna.Client.Avatar!.RenderFlags |= ThingFlags.LocalRender;

        // Talk-to on (what the Storyteller's Talk does): on his copy on her machine.
        his.Actor!.Flags |= Actor.Flag.TalkTo;
        Assert.True(Until(() => anna.Copy(his)?.Actor is { } a && (a.Flags & Actor.Flag.TalkTo) != 0, All), "talk-to never reached her");

        // What the MP scripts do: hide a thing (`SetRenderFlags`) — and the Storyteller's Enemy:
        // no longer talked to, and on an enemy team.
        prop.RenderFlags |= 0x2;
        his.Actor.Flags &= ~Actor.Flag.TalkTo;
        his.Actor.Team = Actor.Teams.EnemyFirst;
        Body(game, "Anna").Actor!.Team = 3;
        Assert.True(Until(() => anna.Copy(prop)!.ModelHidden && anna.Copy(his)!.Actor!.Team == Actor.Teams.EnemyFirst
                              && anna.Client.Avatar!.Actor!.Team == 3, All), "the flags never reached her");
        Assert.Equal(0u, anna.Copy(his)!.Actor!.Flags & Actor.Flag.TalkTo);
        Assert.NotEqual(0u, anna.Client.Avatar!.RenderFlags & ThingFlags.LocalRender);
        // His own machine too, waited on by itself (under the simulator his copy may come a round trip after hers).
        Assert.True(Until(() => bob.Client.Avatar!.Actor!.Team == Actor.Teams.EnemyFirst, All), "his own team never reached him");

        // A thing moved from where the scene put it — a door opened long ago, say — and settled: the
        // snapshots have stopped sending it (WorldWatch.SettleMs).
        var moved = prop.Position + new Vector3(40f, 0f, 0f);
        game.Place.Move(prop, moved);
        for (int i = 0; i < 90; i++) All();

        // A newcomer is told them on arriving, and where the moved thing stands.
        using var cid = Join(files, game, "Cid", "MP_Serena");
        void Three() { All(); cid.Step(); }
        Assert.True(Until(() => cid.Client.Avatar is not null && cid.Copy(prop)!.ModelHidden
                              && cid.Copy(his)!.Actor!.Team == Actor.Teams.EnemyFirst, Three), "the newcomer never learnt them");
        Assert.True(Until(() => Vector3.Distance(cid.Copy(prop)!.Position, moved) < 1f, Three), $"the moved thing is at {cid.Copy(prop)!.Position} on the newcomer's machine");

        // Shown again, everywhere.
        prop.RenderFlags &= ~0x2u;
        Assert.True(Until(() => !anna.Copy(prop)!.ModelHidden && !cid.Copy(prop)!.ModelHidden, Three), "never shown again");
    }

    /// <summary>
    /// **Attachments** (1.1's 0x25 — `AttachThing` on a host; the newcomer's
    /// catch-up; read and attached): Celerity's trail, spawned beside Bob and attached
    /// to him as `EffectDiscCelerity` does, rides his copy on Anna's machine — it follows him when he moves
    /// there — and a late joiner is told it on arriving.
    /// </summary>
    [Fact]
    public void A_thing_a_script_attaches_rides_its_bearer_on_every_machine_and_a_late_joiner_is_told()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var his = Body(game, "Bob");
        Assert.True(Until(() => anna.Copy(his) is not null, All), "Anna never saw Bob");

        var trail = game.Place.Place("celerityTrail", his.Position, his.Orientation, his.Sector)!;
        Assert.NotNull(trail);
        Assert.True(Attachments.Attach(his, trail, -1, new System.Numerics.Vector3(0, 0, 40), trail.Rotation, Attachments.AutoRemove));
        HostSeen.Attached(his, trail);                                            // what the AttachThing native does on a host
        Assert.True(Until(() => anna.Copy(trail) is { AttachedTo: { } rides } && ReferenceEquals(rides, anna.Copy(his)), All),
                    "the trail never rode Bob on Anna's machine");
        var hisThere = anna.Copy(his)!;
        var trailThere = anna.Copy(trail)!;
        Assert.True(System.Numerics.Vector3.Distance(trailThere.Position, hisThere.Position + new System.Numerics.Vector3(0, 0, 40)) < 1f);

        // A late joiner is told every attachment on arriving.
        using var cid = Join(files, game, "Cid", "MP_Serena");
        void Three() { All(); cid.Step(); }
        Assert.True(Until(() => cid.Copy(trail)?.AttachedTo is { } r && ReferenceEquals(r, cid.Copy(his)), Three), "Cid was never told");
    }

    /// <summary>
    /// **Names** (1.1's 0x53 — with every character announced, and in the
    /// catch-up): a character the Storyteller renames (`rename`: its own name and
    /// the second-flags bit 0x800) carries the name to a machine that meets it afterwards — a late joiner —
    /// while one that already had it keeps the old name, as in 1.1, which sends nothing at the rename.
    /// </summary>
    [Fact]
    public void A_renamed_character_s_name_reaches_a_machine_that_meets_it_afterwards()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var his = Body(game, "Bob");
        Assert.True(Until(() => anna.Copy(his) is not null, All), "Anna never saw Bob");
        string before = anna.Copy(his)!.DescriptionId;

        his.DescriptionId = "Bob the Bold";
        his.Actor!.Flags2 |= HostSeen.Renamed;
        for (int i = 0; i < 10; i++) All();
        Assert.Equal(before, anna.Copy(his)!.DescriptionId);                     // nothing is sent at the rename

        using var cid = Join(files, game, "Cid", "MP_Serena");
        void Three() { All(); cid.Step(); }
        Assert.True(Until(() => cid.Copy(his)?.DescriptionId == "Bob the Bold", Three), $"Cid sees {cid.Copy(his)?.DescriptionId}");
        // But a player's character is called by its player's name in a network game (the player's name, which the
        // rename does not touch — it writes the character's own name alone), so the new name shows only on a townsman.
        Assert.Equal("Bob", Ui.Rollover.NameOf(cid.Copy(his)!));
    }

    /// <summary>
    /// **A network game's save has nobody's character in it** (the save leaves out every
    /// `THING_TYPE_PLAYER` thing while a network session is up): the host's place saved
    /// with Anna and Bob in it keeps the inn's own people and not their characters, so a loaded network
    /// save has no bodies waiting — each player who joins it brings their own and arrives as a newcomer.
    /// The same place saved as a single player's keeps them.
    /// </summary>
    [Fact]
    public void A_network_save_leaves_every_players_character_out()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => Playing(anna, bob), All), "never played");
        var theirs = new[] { Body(game, "Anna"), Body(game, "Bob") };
        Assert.All(theirs, t => Assert.Equal(ThingType.Player, t.Type));
        var saved = Save.SaveCapture.OfPlace(game.Place);
        Assert.DoesNotContain(saved.Things.Values, t => theirs.Any(b => b.Guid == t.Guid));
        Assert.Contains(saved.Things.Values, t => t.Template.Length > 0);                 // the inn itself is kept
        NetOrders.Detach(game.Place);                                                     // no network game: a player-type thing is written
        var single = Save.SaveCapture.OfPlace(game.Place);
        Assert.Contains(single.Things.Values, t => theirs.Any(b => b.Guid == t.Guid));
    }

    /// <summary>
    /// **A save hosted as a network game gives slot 0 the character chosen, not its party**. 1.1's
    /// load reads the party back only outside a network session (only with
    /// no session up, after the teardown has emptied it), and the game it then makes puts the select
    /// screen's character in slot 0  and makes it the
    /// party at the start. So the save a host loads has no party left — the run
    /// then makes the host's character from its own template and `GameServer.Enter` lays its own sheet
    /// over it — and keeps its worlds and its chronicle.
    /// </summary>
    [Fact]
    public void A_save_hosted_as_a_network_game_keeps_its_worlds_but_not_its_party()
    {
        var save = new Save.SaveGame
        {
            Party = new Save.PartySave { Steered = "party:Christof", Members = [new Save.ThingSave { Template = "Christof" }, new Save.ThingSave { Template = "Wilhem" }] },
            Chronicle = new Save.ChronicleSave(),
        };
        save.World["Monastery"] = new Save.LocationSave { Level = "P1_MON1" };
        var hosted = NetSaves.ForHost(save);
        Assert.Empty(hosted.Party.Members);
        Assert.Null(hosted.Party.Steered);
        Assert.Equal("P1_MON1", hosted.World["Monastery"].Level);
        Assert.NotNull(hosted.Chronicle);
    }

    [Fact]
    public void A_hitscan_shot_on_the_host_is_fired_again_on_the_players_machines()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => Playing(anna), Two), "never played");
        var hers = Body(game, "Anna");

        // Resolved and gone in the host's step: the watch never sees it.
        var shot = Projectiles.Fire(game.Place, hers, "proj_bullet", hers.Position + new Vector3(0, 0, 50),
                                    Projectiles.Facing(Vector3.UnitX), hers.Guid);
        Assert.NotNull(shot);
        Assert.True(shot!.Removed);
        Assert.True(Until(() => anna.Shots.Count > 0, Two), "the shot never reached her");
        Assert.Equal("proj_bullet", anna.Shots[0].Template);
        Assert.Same(anna.Client.Avatar, anna.Place!.Thing(anna.Shots[0].SpawnedBy));
        Assert.True(anna.Shots[0].Removed);
    }
}
