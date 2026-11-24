// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Start into a game under way** and **players named by their names**, with
/// a host and players on localhost against the install. The session's stage (the lobby's) is 1
/// until the host's place is loaded in a started game and then 2, sent to every player (message 0x33)
/// and in the join's answer; a player's lobby lights Start only at 2 and its Start asks
/// to be let in (message 0x40), which the host answers only for a player still in its
/// room, with the chronicle's revive location to load. Nobody is taken in by the host's start — the room's
/// players and a late joiner alike — and a whole game's journey leaves a player still in the room there. In a
/// network game a player's character goes by its player's name on every machine.
/// </summary>
public class NetStartTests
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
        public int Ms;
        public void Step() { Ms += 33; Server.Poll(Ms); }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public void Step() => Client.Poll(1f / 30);
        public void Dispose() => Client.Dispose();
    }

    /// <summary>The inn's host, not yet started: `start` presses Start and `enter` loads its place.</summary>
    static Game Host(Vfs.GameFiles files, bool start = true, bool enter = true)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, sky: false, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        if (start) server.Start();
        if (enter) server.Enter(place, null);
        return new Game { Server = server, Place = place };
    }

    /// <summary>A player's machine; <paramref name="lobby"/> leaves Start to be pressed (the lobby's), else it asks in by itself (ours).</summary>
    static Player Join(Vfs.GameFiles files, Game game, string name, bool lobby)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = "MP_Serena" }) { StartsItself = !lobby },
        };
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, sky: false, player: "MP_Serena", entrance: s.Entrance);
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    [Fact]
    public void A_player_in_the_room_when_the_host_starts_comes_in_only_by_its_own_Start()
    {
        using var files = Install.Open();
        using var game = Host(files, start: false, enter: false);
        using var anna = Join(files, game, "Anna", lobby: true);
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.State == ClientState.Room, All));
        Assert.Equal(SessionStage.Forming, anna.Client.Stage);                     // hosted, not started
        Assert.False(anna.Client.CanStart);
        Assert.False(LobbyStart.Lit(hosting: false, anna.Client.Stage, ready: true));          // greyed
        Assert.True(LobbyStart.Lit(hosting: true, anna.Client.Stage, ready: true));            // the host's is lit from the first

        // The host presses Start: its place is not up yet, so nothing changes for her.
        game.Server.Start();
        for (int i = 0; i < 20; i++) All();
        Assert.Equal(SessionStage.Forming, game.Server.Stage);
        Assert.Equal(ClientState.Room, anna.Client.State);
        Assert.Equal(SessionStage.Forming, anna.Client.Stage);
        // A Start sent now is not answered (ours: 1.1's button is greyed).
        anna.Client.AskIn();
        for (int i = 0; i < 20; i++) All();
        Assert.Equal(ClientState.Room, anna.Client.State);

        // The host's place loaded: the stage goes to her and she is still in the room.
        game.Server.Enter(game.Place, null);
        Assert.True(Until(() => anna.Client.Stage == SessionStage.UnderWay, All));
        for (int i = 0; i < 20; i++) All();
        Assert.Equal(ClientState.Room, anna.Client.State);
        Assert.True(anna.Client.CanStart);
        Assert.False(game.Server.Players.Single(p => p.Name == "Anna").LetIn);
        // Heard as the room's while she waits (slot state 2).
        Assert.Equal(ChatSlot.Room, ChatEar.Of(game.Server.Players.Single(p => p.Name == "Anna"), null, game.Server.Started).State);

        // Her Start: let in, the place to load comes, and her character arrives.
        anna.Client.AskIn();
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "Anna never came in");
        Assert.True(game.Server.Players.Single(p => p.Name == "Anna").LetIn);
        Assert.Equal("P1_INN4", anna.Client.Settings!.Level);
    }

    [Fact]
    public void A_player_joining_a_game_under_way_waits_in_the_room_for_its_Start()
    {
        using var files = Install.Open();
        using var game = Host(files);
        Assert.Equal(SessionStage.UnderWay, game.Server.Stage);
        using var bob = Join(files, game, "Bob", lobby: true);
        void All() { game.Step(); bob.Step(); }
        Assert.True(Until(() => bob.Client.State == ClientState.Room, All));
        Assert.Equal(SessionStage.UnderWay, bob.Client.Stage);                     // the join's answer carries it
        for (int i = 0; i < 20; i++) All();
        Assert.Equal(ClientState.Room, bob.Client.State);                          // not taken in at once (earlier, he was)
        Assert.True(LobbyStart.Lit(hosting: false, bob.Client.Stage, ready: bob.Client.Accepted));

        bob.Client.AskIn();
        Assert.True(Until(() => bob.Client.Avatar is not null, All), "Bob never came in");
        // A second Start is not answered again (the host answers slot state 2 alone).
        var body = bob.Client.Avatar;
        bob.Client.AskIn();
        for (int i = 0; i < 20; i++) All();
        Assert.Same(body, bob.Client.Avatar);
        Assert.Equal(ClientState.Playing, bob.Client.State);
    }

    [Fact]
    public void The_whole_games_journey_leaves_a_player_still_in_the_room_there()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", lobby: false);                   // in by herself (ours: no lobby)
        using var bob = Join(files, game, "Bob", lobby: true);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.State == ClientState.Room, All));

        // The game moves (a script's Jump): Anna is sent on, Bob — slot state 2 — is not.
        var next = Location.Load(files, "P1_INN4", sky: false);
        game.Server.Travel(next, null);
        Assert.True(Until(() => game.Server.Players.Single(p => p.Name == "Anna").Avatar?.Place == next, All), "Anna never arrived in the next place");
        Assert.Equal(ClientState.Room, bob.Client.State);
        Assert.Null(game.Server.Players.Single(p => p.Name == "Bob").Place);

        // His Start takes him into the game where it now is.
        bob.Client.AskIn();
        Assert.True(Until(() => bob.Client.Avatar is not null, All), "Bob never came in");
        Assert.Same(next, game.Server.Players.Single(p => p.Name == "Bob").Avatar!.Place);
    }

    [Fact]
    public void Another_players_character_goes_by_its_players_name_on_every_machine()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", lobby: false);
        using var bob = Join(files, game, "Bob", lobby: false);
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.True(Until(() => bob.Client.Mirror?.Find(hers.Guid) is not null, All), "Bob never saw Anna");

        // On the host and on Bob's machine the world's rollover names her character "Anna".
        Assert.Equal("Anna", Rollover.Name(hers, text)!.Value.Text);
        var copy = bob.Client.Mirror!.Find(hers.Guid)!;
        Assert.Equal(ThingType.Player, copy.Type);
        Assert.Equal("Anna", Rollover.Name(copy, text)!.Value.Text);                // named on his machine
        Assert.Equal("Bob", Rollover.NameOf(bob.Client.Avatar!, text));            // and his own

        // Out of a network game the same character goes by its description (the name's other branch).
        var single = Location.Load(files, "P1_INN4", sky: false, player: "MP_Serena");
        var alone = single.Player!.Thing;
        alone.Actor!.Name = "Anna";
        Assert.Equal(alone.DescriptionId.Length > 0 ? text[alone.DescriptionId] : alone.Template, Rollover.NameOf(alone, text));
        Assert.NotEqual("Anna", Rollover.NameOf(alone, text));
    }

    [Fact]
    public void The_lobby_says_you_are_hosting_in_11s_words_and_format()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        // `"%s '%s'."` of UI_NET_YOUAREHOSTING and the game's name — ours to show.
        Assert.Equal($"{text["UI_NET_YOUAREHOSTING"]} 'Midnight at the Inn'.", LobbyLines.Hosting("Midnight at the Inn", k => text[k]));
        Assert.EndsWith("'Midnight at the Inn'.", LobbyLines.Hosting("Midnight at the Inn", k => text[k]));
    }
}
