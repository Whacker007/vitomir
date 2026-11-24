// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a comparison with 1.1 found and fixed: who hears a
/// chat line (a said line carries 5000 units in the speaker's place, team talk goes to the
/// team), the join's teams (each player their own in a combative game, all 0 in co-op) and the
/// effects a joiner is born with by the type its player chose, a name compared case and all
/// and a duplicate refused before a full room, with the host's "duplicate name" line,
/// "has left" on the others' machines however a player went, and
/// `ping`'s reply on a player's machine, and a chat line's interface noise. The
/// chat, the teams, the noise and the ping are pinned without the install; the rest joins players to a
/// real game.
/// </summary>
public class NetAuditTests
{
    static ChatEar Ear(int slot, object? place, float x, int team = 0) => new(slot, true, place, new Vector3(x, 0, 0), team);
    static ChatLine Line(ChatKind kind, int from = 1, int to = -1) => new(kind, from, to, "hello");

    [Fact]
    public void A_said_line_carries_5000_units_and_only_in_the_speakers_place()
    {
        object inn = new(), crypt = new();
        var anna = Ear(1, inn, 0);
        foreach (var kind in new[] { ChatKind.Say, ChatKind.Emote, ChatKind.Roll })
        {
            Assert.True(ChatHearing.Hears(Line(kind), anna, Ear(2, inn, 5000), 0));        // At the edge
            Assert.False(ChatHearing.Hears(Line(kind), anna, Ear(2, inn, 5001), 0));
            Assert.False(ChatHearing.Hears(Line(kind), anna, Ear(2, crypt, 10), 0));       // another place
            Assert.True(ChatHearing.Hears(Line(kind), anna, anna, 0));                     // the speaker's own
            Assert.False(ChatHearing.Hears(Line(kind), anna, new ChatEar(2, false, null, default, 0), 0));   // travelling
        }
        // A shout, the server's own and the lobby reach everybody, anywhere.
        foreach (var kind in new[] { ChatKind.Shout, ChatKind.Narrate, ChatKind.Lobby })
            Assert.True(ChatHearing.Hears(Line(kind), anna, Ear(2, crypt, 99999), 0));
    }

    [Fact]
    public void The_moderated_hosts_seat_hears_every_said_line()
    {
        object inn = new(), crypt = new();
        var far = Ear(0, crypt, 99999);
        Assert.False(ChatHearing.Hears(Line(ChatKind.Say), Ear(1, inn, 0), far, 0));
        Assert.True(ChatHearing.Hears(Line(ChatKind.Say), Ear(1, inn, 0), far, GameFlags.Moderated));      // 0x400, slot 0
        Assert.False(ChatHearing.Hears(Line(ChatKind.Say), Ear(1, inn, 0), Ear(2, crypt, 0), GameFlags.Moderated));
    }

    [Fact]
    public void Team_talk_reaches_the_speakers_team_wherever_it_is()
    {
        object inn = new(), crypt = new();
        var anna = Ear(1, inn, 0, team: 1);
        Assert.True(ChatHearing.Hears(Line(ChatKind.Team), anna, Ear(3, crypt, 99999, team: 1), 0));   // teams equal
        Assert.False(ChatHearing.Hears(Line(ChatKind.Team), anna, Ear(2, inn, 1, team: 2), 0));
    }

    [Fact]
    public void A_joiners_team_is_their_slot_in_a_combative_game_and_0_in_co_op()
    {
        // The Co-op box checked sets 0x100; the lobby calls the game "Combative" when it is clear.
        Assert.Equal(3, NetJoin.TeamOf(3, GameFlags.AutoStoryteller));
        Assert.Equal(0, NetJoin.TeamOf(3, GameFlags.CoOp | GameFlags.AutoStoryteller));
        Assert.Equal(0, NetJoin.TeamOf(0, 0));
    }

    [Fact]
    public void Names_are_the_same_only_case_and_all()
    {
        Assert.True(NetJoin.SameName("Anna", "Anna"));
        Assert.False(NetJoin.SameName("Anna", "anna"));          // strncmp
        Assert.True(NetJoin.SameName(new string('a', 40), new string('a', 32) + "bbbbbbbb"));   // 32 characters
    }

    [Fact]
    public void A_chat_line_plays_MPMESSAGE1_a_roll_MPMESSAGE2_and_an_emote_nothing()
    {
        foreach (var kind in new[] { ChatKind.Say, ChatKind.Shout, ChatKind.Whisper, ChatKind.Team })
            Assert.Equal(0xf, ChatNoise.For(kind));
        Assert.Equal(0x10, ChatNoise.For(ChatKind.Roll));
        Assert.Null(ChatNoise.For(ChatKind.Emote));
        Assert.Null(ChatNoise.For(ChatKind.Narrate));
    }

    [Fact]
    public void Ping_on_a_players_machine_answers_with_the_round_trip_and_elsewhere_nothing()
    {
        Assert.Equal("Ping reply: 42 msec", Ui.ConsoleCommands.PingReply(42.7f));   // 1.1's words
        Assert.Null(Ui.ConsoleCommands.PingReply(null));                           // the host, or single player
    }

    // ---- against the install --------------------------------------------------------------------

    [Fact]
    public void Players_are_put_on_their_own_teams_and_a_human_on_a_vampires_model_is_born_a_human()
    {
        using var files = Install.Open();
        using var game = Host(files, Salon(files));                       // Co-op unchecked: combative
        using var anna = Join(files, game, "Anna", "MP_Christof_DA", ActorType.Human);
        using var bob = Join(files, game, "Bob", "MP_Christof_DA", ActorType.Ghoul);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never both played");

        var hers = game.Server.Players.Single(p => p.Name == "Anna");
        var his = game.Server.Players.Single(p => p.Name == "Bob");
        Assert.Equal(hers.Slot, hers.Avatar!.Actor!.Team);
        Assert.Equal(hers.Slot, hers.Avatar.Actor.OriginalTeam);
        Assert.Equal(his.Slot, his.Avatar!.Actor!.Team);
        // Each machine is told (the flags message carries the teams).
        Assert.True(Until(() => anna.Client.Mirror!.Find(his.Avatar.Guid)?.Actor?.Team == his.Slot, All), "Anna never saw Bob's team");

        // A human regains blood and mana and has no frenzy to lose; a ghoul's frenzy falls.
        var names = hers.Avatar.Effects.All.Where(e => !e.Spent).Select(e => e.Name).ToList();
        Assert.Contains("ef_increasemanaot", names);
        Assert.Contains("ef_increasebloodot", names);
        Assert.DoesNotContain("ef_decreasefrenzy", names);
        Assert.Contains(his.Avatar.Effects.All, e => !e.Spent && e.Name == "ef_decreasefrenzy");
    }

    /// <summary>
    /// The name is compared as 1.1's join compares it, case and all — now when the character is chosen in the room (protocol
    /// 23), so a duplicate is refused there, and a machine with no lobby (the command line's) is let go with 1.1's reason;
    /// the room's size is the knock's, so in a full game a newcomer is refused as full before any name is known.
    /// </summary>
    [Fact]
    public void A_name_differing_in_case_may_join_and_a_duplicate_is_refused_as_a_duplicate_in_the_room()
    {
        using var files = Install.Open();
        using var game = Host(files, Salon(files) with { MaxPlayers = 3 });
        var lines = new List<string>();
        game.Server.Posted += (text, _) => lines.Add(text);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()));

        // The same name is refused for its name, in the room.
        using var again = Join(files, game, "Anna", "MP_Kazi");
        Assert.True(Until(() => again.Client.State == ClientState.Gone, () => game.Step(), () => anna.Step(), () => again.Step()));
        Assert.Equal("UI_NET_JOINDUPLICATE", again.Client.Reason);
        Assert.Contains($"Anna {new Ui.Strings(files).Find("UI_NET_DUPLICATENAME")}", lines);   // the host's own line

        // "anna" is another name to 1.1, and is let in.
        using var lower = Join(files, game, "anna", "MP_Kazi");
        Assert.True(Until(() => lower.Client.Accepted, () => game.Step(), () => anna.Step(), () => lower.Step()));
        Assert.Contains(game.Server.Players, p => p.Name == "anna");

        // The room is full now (the host, Anna and anna): a fourth is refused at the knock.
        using var fourth = Join(files, game, "Anna", "MP_Kazi");
        Assert.True(Until(() => fourth.Client.State == ClientState.Gone, () => game.Step(), () => anna.Step(), () => lower.Step(), () => fourth.Step()));
        Assert.Equal("UI_NET_JOINFULL", fourth.Client.Reason);
    }

    [Fact]
    public void A_player_who_drops_has_left_on_the_others_machines_and_disconnected_on_the_hosts()
    {
        using var files = Install.Open();
        using var game = Host(files, Salon(files));
        var hosts = new List<string>();
        game.Server.Posted += (text, _) => hosts.Add(text);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        var bobsPeer = new NetPeer(0);
        using var bob = Join(files, game, "Bob", "MP_Erik", peer: bobsPeer);
        var annas = new List<string>();
        anna.Client.Posted += (text, _) => annas.Add(text);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));

        // Bob's machine goes without a goodbye: the host drops him.
        bobsPeer.Disconnect(bobsPeer.Connections.Single(), "gone");
        var strings = new Ui.Strings(files);
        Assert.True(Until(() => annas.Contains($"Bob {strings.Find("UI_NET_HASLEFT")}"), () => game.Step(), () => anna.Step()), "Anna was never told");
        Assert.Contains($"Bob {strings.Find("UI_NET_HASLEFT")}", annas);                    // 1.1's words
        Assert.Contains($"Bob {strings.Find("UI_NET_HASDISCONNECTED")}", hosts);
    }

    [Fact]
    public void A_player_who_leaves_by_the_menu_has_disconnected_on_the_hosts_machine_too()
    {
        // 1.1's leaving player sends nothing (it closes DirectPlay); the host hears only
        // DirectPlay's player-destroyed, whose one line is "has disconnected".
        using var files = Install.Open();
        using var game = Host(files, Salon(files));
        var hosts = new List<string>();
        game.Server.Posted += (text, _) => hosts.Add(text);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        var annas = new List<string>();
        anna.Client.Posted += (text, _) => annas.Add(text);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));

        bob.Client.Leave();
        var strings = new Ui.Strings(files);
        Assert.True(Until(() => annas.Contains($"Bob {strings.Find("UI_NET_HASLEFT")}"), () => game.Step(), () => anna.Step()), "Anna was never told");
        Assert.Contains($"Bob {strings.Find("UI_NET_HASDISCONNECTED")}", hosts);
    }

    // ---- a host and its players in one process (as NetTests) ------------------------------------

    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard, not a speed test (levels fork, 4 s was too few on a busy laptop)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(2);
        }
        return done();
    }

    sealed class Game : IDisposable
    {
        public required GameServer Server;
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
        public Simulation? Sim;
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    /// <summary>The Dark Ages Inn salon (`MP_DAInn`): one small level, no scene, no script.</summary>
    static GameSettings Salon(Vfs.GameFiles files) =>
        GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);

    static Game Host(Vfs.GameFiles files, GameSettings settings)
    {
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new Rpg.EffectDefinitions(files), new Rpg.DisciplineDefinitions(files));   // the born effects need the table
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        var strings = new Ui.Strings(files);
        server.Find = strings.Find;
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Sim = new Simulation(place) };
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template, ActorType type = ActorType.Vampire, NetPeer? peer = null)
    {
        var p = new Player
        {
            Client = new GameClient(peer ?? new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template, Type = type }),
        };
        var strings = new Ui.Strings(files);
        p.Client.Find = strings.Find;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            var place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
            p.Sim = new Simulation(place);
            p.Client.Enter(place, place.Player?.Thing);
        };
        return p;
    }
}
