// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What scripts show to one player, and the progression loop, with a host and
/// players on localhost against the install: a script's read pane opens on the reader's machine and its
/// close comes back for `textended`; the three prints reach whom 1.1's
/// host sends them to — the one player, everybody, those nearby, and PrintFormatNLS's fall-through to
/// the radius; quests follow the host's log (loaded, flagged,
/// and the join's list); the scripts hear `joined` once and `beginscene` at every arrival; `Advance`
/// opens the screen on the player's own machine and the host takes their choices;
/// experience is split between the players; a kill costs Humanity only with Track Humanity;
/// and a character brings its sheet, disciplines and pack to the next game.
/// </summary>
public class NetTextsTests
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
        public readonly List<string> Lines = [];
        public readonly List<string> Opened = [];
        public readonly QuestLog Quests = new();
        public WorldThing? Advancing;
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    static Game Host(Vfs.GameFiles files, GameFlags flags = GameFlags.AutoStoryteller, bool awake = false)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", flags);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        if (awake) place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var session = new Session { Here = place };
        place.Session = session;
        session.Quests.Read = QuestFiles(files);
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Place = place, Sim = new Simulation(place) };
    }

    static Func<string, byte[]?> QuestFiles(Vfs.GameFiles files) =>
        name => files.TryRead($"quests/{Path.GetFileNameWithoutExtension(name)}.nqd", out var nqd) ? nqd : null;

    static Player Join(Vfs.GameFiles files, Game game, NetCharacter me, DisciplineDefinitions? powers = null)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port), me) { Powers = powers },
        };
        p.Quests.Read = QuestFiles(files);
        p.Client.Posted += (text, _) => p.Lines.Add(text);
        p.Client.Texts.Open = name => { p.Opened.Add(name); return true; };
        p.Client.Texts.Quests = p.Quests;
        p.Client.Texts.AdvanceAsked += body => p.Advancing = body;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: me.Template, entrance: s.Entrance);
            p.Sim = new Simulation(p.Place);
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template) =>
        Join(files, game, new NetCharacter { Name = name, Template = template });

    static WorldThing Body(Game game, string name) => game.Server.Players.Single(p => p.Name == name).Avatar!;

    [Fact]
    public void A_scripts_text_opens_on_the_readers_machine_and_its_close_comes_back_for_textended()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var ended = new List<(int Script, int Reader)>();
        game.Server.Texts.TextEnded += (script, reader) => ended.Add((script, reader));

        // The host answers true and opens nothing of its own: the pane is hers.
        Assert.Equal(TextRoute.Elsewhere, game.Server.Texts.Text(77, Body(game, "Anna").Guid, 0, "IntroTextCTD"));
        Assert.True(Until(() => anna.Opened.Count > 0, All), "her machine never opened it");
        Assert.Equal("IntroTextCTD", anna.Opened.Single());
        Assert.Empty(bob.Opened);
        Assert.Equal("IntroTextCTD", anna.Client.Texts.Reading);

        // She closes it: the host hears the script and the reader.
        anna.Client.Texts.Closed();
        Assert.True(Until(() => ended.Count > 0, All), "the close never came back");
        Assert.Equal((77, Body(game, "Anna").Guid), ended.Single());

        // A reader who is nobody's gets true and nothing: no player's machine opens anything.
        Assert.Equal(TextRoute.Elsewhere, game.Server.Texts.Text(77, 123456, 0, "IntroTextCTD"));
        for (int i = 0; i < 20; i++) All();
        Assert.Single(anna.Opened);
        Assert.Empty(bob.Opened);
    }

    [Fact]
    public void The_prints_reach_the_player_everybody_or_those_nearby_as_the_hosts_code_sends_them()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna");
        var his = Body(game, "Bob");
        his.Position = hers.Position + new Vector3(300, 0, 0);          // inside 500, outside 100
        var texts = game.Server.Texts;

        // Flags 0: her machine alone, and not the host's box.
        Assert.False(texts.Print(hers.Guid, 0, "for Anna", 0xffffffffu));
        // Broadcast: both, and the host's box too.
        Assert.True(texts.Print(hers.Guid, HostTexts.Broadcast, "for all", 0xffffffffu));
        // The small radius (100) around her: only her; the medium (500): both.
        Assert.False(texts.Print(hers.Guid, HostTexts.RadiusSmallFlag, "small", 0xffffffffu));
        Assert.False(texts.Print(hers.Guid, 0x8, "medium", 0xffffffffu));
        // PrintNLS: the key to her, said in her machine's words (here the key itself).
        Assert.False(texts.PrintKey(hers.Guid, "GEN_DOORLOCKED", 0xffffffffu));
        // PrintFormatNLS with flags 0 goes to her and then on through the 500 sweep: her twice, Bob once.
        Assert.False(texts.PrintFormat(hers.Guid, 0, "RPG_X", "", 0xffffffffu));
        // A GUID that is nobody's: Print shows it nowhere, PrintNLS and PrintFormatNLS on the host.
        Assert.False(texts.Print(999999, 0, "nobody", 0xffffffffu));
        Assert.True(texts.PrintKey(999999, "GEN_DOORLOCKED", 0xffffffffu));
        Assert.True(texts.PrintFormat(999999, 0, "RPG_X", "", 0xffffffffu));

        Assert.True(Until(() => anna.Lines.Count(l => l == "RPG_X") == 2 && bob.Lines.Contains("RPG_X"), All),
                    $"Anna: {string.Join(" | ", anna.Lines)}; Bob: {string.Join(" | ", bob.Lines)}");
        for (int i = 0; i < 10; i++) All();
        Assert.Contains("for Anna", anna.Lines);
        Assert.DoesNotContain("for Anna", bob.Lines);
        Assert.Contains("for all", anna.Lines);
        Assert.Contains("for all", bob.Lines);
        Assert.Contains("small", anna.Lines);
        Assert.DoesNotContain("small", bob.Lines);
        Assert.Contains("medium", bob.Lines);
        Assert.Contains("GEN_DOORLOCKED", anna.Lines);
        Assert.DoesNotContain("GEN_DOORLOCKED", bob.Lines);
        Assert.Single(bob.Lines, l => l == "RPG_X");
        Assert.DoesNotContain("nobody", anna.Lines.Concat(bob.Lines));
    }

    [Fact]
    public void A_quest_the_host_loads_and_changes_is_in_every_players_log_and_a_newcomer_is_sent_the_list()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, Two), "never played");
        var log = game.Place.Session!.Quests;
        var quest = log.Load("LOT_ReturnToHaven");
        Assert.SkipWhen(quest is null, "quests/LOT_ReturnToHaven.nqd is not in the install");
        game.Server.Texts.QuestLoaded("LOT_ReturnToHaven");
        Assert.True(Until(() => anna.Quests.Find("LOT_ReturnToHaven") is not null, Two), "her log never had it");

        log.SetFlags(quest!, QuestLog.Complete);
        game.Server.Texts.QuestFlags(quest!.Name, quest.Flags);
        Assert.True(Until(() => anna.Quests.Find("LOT_ReturnToHaven")!.Complete, Two), "never complete on her machine");
        Assert.Equal(quest.Flags, anna.Quests.Find("LOT_ReturnToHaven")!.Flags);

        // Bob joins late: the list brings it.
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => bob.Quests.Find("LOT_ReturnToHaven") is { Complete: true }, All), "the newcomer never had it");
    }

    [Fact]
    public void The_scripts_hear_joined_once_and_beginscene_at_every_arrival()
    {
        using var files = Install.Open();
        using var game = Host(files);
        var heard = new List<string>();
        game.Server.Entered += (p, first) => heard.Add($"{p.Name} {(first ? "joined" : "arrived")} {p.Avatar!.Guid}");
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => heard.Count > 0 && anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()), "never arrived");
        Assert.Equal($"Anna joined {Body(game, "Anna").Guid}", heard.Single());
        // The "just joined" bit is spent (the first arrival clears it).
        Assert.Equal(0u, Body(game, "Anna").Actor!.Flags2 & GameServer.JoinedFlag2);

        var street = Location.Load(files, "p1_nqtr", scene: "NQTR_CTD_1_1", entrance: 0);
        game.Place = street;
        game.Sim = new Simulation(street);
        game.Server.Travel(street, null);
        Assert.True(Until(() => heard.Count > 1, () => game.Step(), () => anna.Step()), "never arrived in the street");
        Assert.Equal($"Anna arrived {Body(game, "Anna").Guid}", heard[1]);

        // Leaving, the scripts hear `disconnected` while her character is still there.
        int guid = Body(game, "Anna").Guid;
        game.Server.Departing += p => heard.Add($"{p.Name} leaving {(p.Avatar is { InWorld: true } b ? b.Guid : 0)}");
        anna.Client.Leave();
        Assert.True(Until(() => heard.Count > 2, () => game.Step(), () => anna.Step()), "never left");
        Assert.Equal($"Anna leaving {guid}", heard[2]);
    }

    [Fact]
    public void Advance_opens_the_screen_on_the_players_machine_and_the_host_takes_what_she_spent()
    {
        using var files = Install.Open();
        using var game = Host(files, awake: true);
        var powers = new DisciplineDefinitions(files);
        using var anna = Join(files, game, new NetCharacter { Name = "Anna", Template = "MP_Serena" }, powers);
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, Two), "never played");
        var hers = Body(game, "Anna");
        hers.Actor!.SpendableExperience = 5000;
        var answered = new List<WorldThing>();
        game.Server.Texts.Advanced += (_, body) => answered.Add(body);

        Assert.True(game.Server.Texts.Advance(hers.Guid));
        Assert.True(Until(() => anna.Advancing is not null, Two), "her screen never opened");
        var mine = anna.Advancing!;
        Assert.Same(anna.Client.Avatar, mine);
        Assert.Equal(5000, mine.Actor!.SpendableExperience);
        Assert.True(anna.Client.Texts.Advancing);

        // She raises Strength on her own machine; the host's sheets do not undo it meanwhile.
        float before = mine.Actor.BaseStat[0];
        var screen = new Advancement(mine, [mine], powers);
        Assert.True(screen.BuyStat(0));
        long left = mine.Actor.SpendableExperience;
        for (int i = 0; i < 30; i++) Two();
        Assert.Equal(before + 1, mine.Actor.BaseStat[0]);

        anna.Client.Texts.Advanced(mine);
        Assert.False(anna.Client.Texts.Advancing);
        Assert.True(Until(() => answered.Count > 0, Two), "the host never heard");
        Assert.Equal(before + 1, hers.Actor.BaseStat[0]);
        Assert.Equal(left, hers.Actor.SpendableExperience);
        // Anybody not a player is the host's own business (single player's advance).
        Assert.False(game.Server.Texts.Advance(0));
        // The host's walk: slot 0's character — a headless host has none — then each player's.
        Assert.Equal([hers], game.Server.Texts.WalkList());
    }

    [Fact]
    public void In_a_network_game_only_your_own_character_is_raised_unless_moderated_and_the_seated_host_raises_free()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var place = Location.Load(files, "p1_inn4", player: "MP_Serena");
        var mine = place.Player!.Thing;
        var theirs = place.Place("MP_Erik", mine.Position + new Vector3(64, 0, 0), mine.Orientation, mine.Sector)!;
        mine.Actor!.SpendableExperience = theirs.Actor!.SpendableExperience = 0;

        // Not moderated: the walk shows another player's character, but nothing of theirs can be bought.
        theirs.Actor.SpendableExperience = 100_000;
        var walk = new Advancement(mine, [mine, theirs], powers) { Network = new NetAdvanceRules(false, true, false, mine) };
        Assert.True(walk.Next());
        Assert.False(walk.BuyStat(0));

        // Moderated, the host in the seat: a raise costs nothing — the cost is awarded first.
        theirs.Actor.SpendableExperience = 0;
        long earned = theirs.Actor.Experience;
        var seat = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(true, true, true, mine) };
        float before = theirs.Actor.BaseStat[0];
        Assert.True(seat.BuyStat(0));
        Assert.Equal(before + 1, theirs.Actor.BaseStat[0]);
        Assert.Equal(0, theirs.Actor.SpendableExperience);
        Assert.True(theirs.Actor.Experience > earned);

        // Moderated, not seated: it costs, as ever.
        var plain = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(true, true, false, mine) };
        Assert.False(plain.BuyStat(0));
    }

    [Fact]
    public void Experience_is_split_between_the_players_and_a_kills_share_only_between_those_there_and_alive()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = Body(game, "Anna").Actor!;
        var his = Body(game, "Bob").Actor!;
        long a0 = hers.Experience, b0 = his.Experience;

        // 1,001 between two is 500 each, the odd one lost to the division.
        var got = game.Place.AwardPartyExperience(1001);
        Assert.Equal(2, got.Count);
        Assert.Equal(a0 + 500, hers.Experience);
        Assert.Equal(b0 + 500, his.Experience);

        // Tied to a thing (a kill's victim): the dead do not count.
        his.Flags |= Actor.Flag.Dead;
        game.Place.AwardPartyExperience(300, near: Body(game, "Anna"));
        Assert.Equal(a0 + 800, hers.Experience);
        Assert.Equal(b0 + 500, his.Experience);
        his.Flags &= ~Actor.Flag.Dead;

        // A share that comes to nothing gives nobody anything.
        Assert.Empty(game.Place.AwardPartyExperience(1));
        Assert.Equal(a0 + 800, hers.Experience);
    }

    [Fact]
    public void A_kill_costs_humanity_on_a_host_only_with_track_humanity()
    {
        using var files = Install.Open();
        using var plain = Host(files);
        Assert.False(NetAwards.TracksHumanity(plain.Place));
        using var tracked = Host(files, GameFlags.AutoStoryteller | GameFlags.TrackHumanity);
        Assert.True(NetAwards.TracksHumanity(tracked.Place));
        Assert.True(NetAwards.TracksHumanity(Location.Load(files, tracked.Server.Settings.Level)));   // single player
        var vampire = new Actor { Type = ActorType.Vampire };
        vampire.Flags |= Actor.Flag.HumanityTrack;
        Assert.Equal(0f, Damage.LoseHumanity(vampire, Damage.HumanityPerKill, NetAwards.TracksHumanity(plain.Place)));
        Assert.Equal(Damage.HumanityPerKill, Damage.LoseHumanity(vampire, Damage.HumanityPerKill, NetAwards.TracksHumanity(tracked.Place)));
    }

    [Fact]
    public void A_character_brings_its_sheet_disciplines_and_pack_to_the_next_game()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        NetCharacter kept;
        List<string> pack;
        using (var game = Host(files, awake: true))
        {
            using var anna = Join(files, game, "Anna", "MP_Serena");
            Assert.True(Until(() => anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()), "never played");
            var hers = Body(game, "Anna");
            hers.Actor!.BaseStat[0] = 4;
            hers.Actor.Stat[0] = 4;
            hers.Actor.SpendableExperience = 1234;
            Assert.SkipWhen(powers["BloodHealing"] is null, "misc/game.ndd not in the install");
            DisciplineBook.SetLevel(hers, powers, "BloodHealing", 2);
            // A sword in her hand and a vitae in the pack (the template's kit is empty).
            var sword = game.Place.Make("broadSword")!;
            sword.Owner = hers;
            hers.Actor.Inventory.Add(sword.Item!);
            Assert.Equal(EquipResult.Equipped, Equipment.Equip(hers, sword.Item!, Slot.RightWeapon, game.Place.Effects!, 0));
            var vitae = game.Place.Make("VitaeRat")!;
            vitae.Owner = hers;
            hers.Actor.Inventory.Add(vitae.Item!);
            Assert.True(Stow.Place(hers.Actor, vitae.Item!, Slot.Inventory));
            pack = hers.Actor.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Template).Order().ToList();
            Assert.Equal(2, pack.Count);
            kept = anna.Client.Me!.Keep(hers);
        }
        // Written and read back as the `.vplayer` is.
        var folder = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        try
        {
            new Crypt(folder).Save(kept);
            kept = new Crypt(folder).All().Single();
        }
        finally { Directory.Delete(folder, true); }
        Assert.NotNull(kept.Saved);

        using var next = Host(files, awake: true);
        using var again = Join(files, next, kept, powers);
        Assert.True(Until(() => again.Client.Avatar is not null, () => next.Step(), () => again.Step()), "never played again");
        var back = Body(next, "Anna").Actor!;
        Assert.Equal(4f, back.BaseStat[0]);
        Assert.Equal(1234, back.SpendableExperience);
        Assert.Equal(2, back.Disciplines["BloodHealing"]?.Level);
        Assert.Equal(pack, back.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Template).Order().ToList());
        // Written back dead, a character comes back at a tenth.
        var fallen = kept with { Saved = kept.Saved! with { Dead = true, Health = 0 } };
        using var third = Host(files, awake: true);
        using var risen = Join(files, third, fallen, powers);
        Assert.True(Until(() => risen.Client.Avatar is not null, () => third.Step(), () => risen.Step()), "never played a third time");
        var up = Body(third, "Anna").Actor!;
        Assert.False(up.Has(Actor.Flag.Dead));
        Assert.Equal(up.MaxHealth * 0.1f, up.Health, 0.01f);
        // The sword is worn again, in the same hand.
        Assert.Equal(Slot.RightWeapon, back.Inventory.Items.Single(i => i.Thing!.Template == "broadSword").Location);
    }

    /// <summary>
    /// **1.1's own character file, imported and brought to a game**: built here as 1.1 writes one (<c>NpfBytes</c>), made Vitomir's
    /// (<see cref="OriginalCharacter"/>), kept as a <c>.vplayer</c> and joined: its numbers, its disciplines, its worn sword and the rest of
    /// its pack, its purse and its name are the file's.
    /// </summary>
    [Fact]
    public void One_one_s_character_file_imported_joins_with_its_sheet_and_pack()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers["BloodHealing"] is null, "misc/game.ndd not in the install");
        var bytes = new Vitomir.TestData.NpfBytes { Name = "Bertha", Template = "mp_serena", Clan = "toreador", ClanName = "Toreador", Purse = 777 };
        bytes.Disciplines.Clear();
        bytes.Disciplines.Add(("BloodHealing", 2, 0, 0));
        bytes.Disciplines.Add(("bloodstrength", -1, -1, 0));                         // learnable, not learnt: 1.1 lists every one so
        bytes.Items.Clear();
        bytes.Items.Add(("broadSword", (uint)Slot.RightWeapon, 0, 0, 1));
        bytes.Items.Add(("VitaeRat", (uint)Slot.Inventory, 2, 1, 1));
        bytes.Effects.Clear();
        bytes.BaseStats[0] = bytes.Stats[0] = 4;
        var notes = new List<string>();
        var imported = OriginalCharacter.From(Vitomir.Formats.Npf.Read(bytes.Build()), notes, files);
        Assert.Equal("MP_Serena", imported.Template);                                   // spelt as the install's templates spell it
        Assert.DoesNotContain(notes, n => n.Contains("not one of", StringComparison.Ordinal) || n.Contains("cannot make", StringComparison.Ordinal));
        var folder = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        NetCharacter kept;
        try
        {
            new Crypt(folder).Save(imported);
            kept = new Crypt(folder).All().Single();
        }
        finally { Directory.Delete(folder, true); }

        using var game = Host(files, awake: true);
        using var bertha = Join(files, game, kept, powers);
        Assert.True(Until(() => bertha.Client.Avatar is not null, () => game.Step(), () => bertha.Step()), "never played");
        var her = Body(game, "Bertha").Actor!;
        Assert.Equal(4f, her.BaseStat[0]);
        Assert.Equal(250L, her.SpendableExperience);
        Assert.Equal(1250L, her.Experience);
        Assert.Equal(2, her.Disciplines["BloodHealing"]?.Level);
        Assert.Equal(-1, her.Disciplines["BloodStrength"]?.Level);                     // −1 crosses the wire as −1 (it came as 4)
        Assert.True(Until(() => bertha.Client.Avatar?.Actor?.Disciplines["BloodHealing"]?.Level == 2, () => game.Step(), () => bertha.Step()),
                    "her sheet never reached her machine");
        Assert.Equal(-1, bertha.Client.Avatar!.Actor!.Disciplines["BloodStrength"]?.Level);   // and on her machine, from the host's sheet
        Assert.Equal(777, her.Cash);
        Assert.Equal("toreador", her.Clan);
        Assert.Equal(Slot.RightWeapon, her.Inventory.Items.Single(i => i.Thing!.Template.Equals("broadSword", StringComparison.OrdinalIgnoreCase)).Location);
        var rat = her.Inventory.Items.Single(i => i.Thing!.Template.Equals("VitaeRat", StringComparison.OrdinalIgnoreCase));
        Assert.Equal((Slot.Inventory, (2, 1)), (rat.Location, rat.Cell));
    }
}
