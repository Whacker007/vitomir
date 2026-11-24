// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Storyteller's and the chat's network leftovers** (a comparison with 1.1, *The Storyteller's*): a
/// player's cast is refused, in silence, only while the Storyteller has the body, and never for the pause;
/// a possessed player's line is `MP_TRIEDTOSAY` in the host's own chat box alone;
/// the moderated host's copies of a whisper (`MP_WHISPERALT`) and of another
/// team's talk (`MP_TEAMSAYALT`); the Storyteller's Comm Lock (0x80) keeping the room and the game
/// apart; a whisper reaching only the one whispered to, with the sender's copy the QoL
/// `whisperecho`; the speaker's lips moving only where a line is shown, the talk never in a snapshot; the
/// shell's priority test, which the model's never-kept bits make a no-op; and the game flag 0x100
/// named for what it means, co-op. The rules are pinned without the install; the rest plays a
/// real game in `MP_DAInn`.
/// </summary>
public class StoryLeftoverTests
{
    static ChatEar Ear(int slot, object? place, float x, int team = 0, ChatSlot state = ChatSlot.Playing) =>
        new(slot, place is not null, place, new Vector3(x, 0, 0), team, state);
    static ChatLine Line(ChatKind kind, int from = 1, int to = -1) => new(kind, from, to, "hello");

    [Fact]
    public void A_player_in_the_room_hears_a_said_line_anywhere_unless_the_Storyteller_has_locked_the_comms()
    {
        object inn = new(), crypt = new();
        var anna = Ear(1, inn, 0);
        var room = Ear(3, null, 0, state: ChatSlot.Room);
        foreach (var kind in new[] { ChatKind.Say, ChatKind.Emote, ChatKind.Roll })
        {
            Assert.True(ChatHearing.Hears(Line(kind), anna, room, 0));
            Assert.False(ChatHearing.Hears(Line(kind), anna, room, GameFlags.CommLocked));
            Assert.False(ChatHearing.Hears(Line(kind), anna, Ear(2, crypt, 0), 0));                // a player elsewhere still not
        }
        // A shout and the server's own: everybody, but the room while the comms are locked.
        foreach (var kind in new[] { ChatKind.Shout, ChatKind.Narrate })
        {
            Assert.True(ChatHearing.Hears(Line(kind), anna, room, 0));
            Assert.False(ChatHearing.Hears(Line(kind), anna, room, GameFlags.CommLocked));
            Assert.True(ChatHearing.Hears(Line(kind), anna, Ear(2, crypt, 99999), GameFlags.CommLocked));
        }
        // The room's (and a loading player's) line: everybody; locked, only those not playing and the moderated host.
        var lobby = Line(ChatKind.Lobby, from: 3);
        var loading = Ear(4, null, 0, state: ChatSlot.Loading);
        Assert.True(ChatHearing.Hears(lobby, room, anna, 0));
        Assert.False(ChatHearing.Hears(lobby, room, anna, GameFlags.CommLocked));
        Assert.True(ChatHearing.Hears(lobby, room, loading, GameFlags.CommLocked));
        Assert.True(ChatHearing.Hears(lobby, room, room, GameFlags.CommLocked));
        Assert.False(ChatHearing.Hears(lobby, room, Ear(0, inn, 0), GameFlags.CommLocked));
        Assert.True(ChatHearing.Hears(lobby, room, Ear(0, inn, 0), GameFlags.CommLocked | GameFlags.Moderated));
        // Vitomir's room is before the start: a player's ear then is the room's.
        Assert.Equal(ChatSlot.Room, ChatEar.Of(new NetPlayer { Slot = 1, Character = new NetCharacter { Name = "Anna" } }, null, started: false).State);
        Assert.Equal(ChatSlot.Loading, ChatEar.Of(new NetPlayer { Slot = 1, Character = new NetCharacter { Name = "Anna" } }, null).State);
    }

    [Fact]
    public void A_whisper_reaches_only_the_one_whispered_to_and_its_sender_is_echoed_unless_he_is_the_moderated_host()
    {
        object inn = new();
        var whisper = Line(ChatKind.Whisper, from: 1, to: 2);
        Assert.True(ChatHearing.Hears(whisper, Ear(1, inn, 0), Ear(2, inn, 0), 0));
        Assert.False(ChatHearing.Hears(whisper, Ear(1, inn, 0), Ear(1, inn, 0), 0));               // not its sender: 1.1's
        Assert.False(ChatHearing.Hears(whisper, Ear(1, inn, 0), Ear(0, inn, 0), GameFlags.Moderated));
        Assert.True(ChatHearing.Echoes(whisper, 1, 0));                                            // the QoL's copy (ours)
        Assert.False(ChatHearing.Echoes(Line(ChatKind.Whisper, from: 1, to: 1), 1, 0));            // to himself: once
        Assert.True(ChatHearing.Echoes(Line(ChatKind.Whisper, from: 0, to: 2), 0, 0));
        Assert.False(ChatHearing.Echoes(Line(ChatKind.Whisper, from: 0, to: 2), 0, GameFlags.Moderated));   // 1.1's copy is his
        Assert.False(ChatHearing.Echoes(Line(ChatKind.Say), 1, 0));
        Assert.True(new Qol().WhisperEcho);                                                        // on by default
        Assert.Equal(HostSwitches.Governor.Machine, HostSwitches.Of("qol", "whisperecho"));
    }

    [Fact]
    public void The_moderated_host_is_given_a_copy_of_a_whisper_to_another_and_of_another_teams_talk()
    {
        var whisper = new ChatLine(ChatKind.Whisper, 1, 2, "psst");
        Assert.Equal(new HostLine("MP_WHISPERALT", "%SAnna%RBob%Tpsst", 0xff40d040u),
                     StoryChat.HostCopy(whisper, "Anna", 1, "Bob", 0, GameFlags.Moderated));
        Assert.Null(StoryChat.HostCopy(whisper, "Anna", 1, "Bob", 0, 0));                         // moderation only
        Assert.Null(StoryChat.HostCopy(whisper with { To = 0 }, "Anna", 1, "Hank", 0, GameFlags.Moderated));   // to the host: his own
        var team = new ChatLine(ChatKind.Team, 1, -1, "flank");
        Assert.Equal(new HostLine("MP_TEAMSAYALT", "%SAnna%D1%Tflank", 0xffdd80ffu),
                     StoryChat.HostCopy(team, "Anna", 1, null, 0, GameFlags.Moderated));
        Assert.Null(StoryChat.HostCopy(team, "Anna", 1, null, 1, GameFlags.Moderated));           // his own team hears it anyway
        Assert.Null(StoryChat.HostCopy(new ChatLine(ChatKind.Say, 1, -1, "hi"), "Anna", 1, null, 0, GameFlags.Moderated));
        Assert.Equal(new HostLine("MP_TRIEDTOSAY", "%SAnna%Thello", 0xffdd4040u), StoryChat.TriedToSay("Anna", "hello"));
    }

    [Fact]
    public void A_shells_priority_is_tested_but_the_model_never_keeps_it_so_nothing_is_refused()
    {
        var fx = new ModelFx();
        fx.SetShell(Location.SteerShell, 0x5000, 0f, 1f, Location.SteerShellRamp);               // the steered one's, priority 1
        Assert.Equal(0u, fx.ShellFlags & ModelFx.ShellPriority);                                  // the shell keeps no 0x3000
        fx.SetShell("shellSprite_red", 0, 1f, 1f, 0f);                                            // priority 0 over it
        Assert.Equal("shellSprite_red", fx.Shell);
        fx.SetShell("shellSprite_blue", 0x3000, 1f, 1f, 0f);
        fx.SetShell("shellSprite_white", 0, 1f, 1f, 0f);
        Assert.Equal("shellSprite_white", fx.Shell);
    }

    [Fact]
    public void The_game_flag_0x100_is_co_op()
    {
        Assert.Equal(0x100u, (uint)GameFlags.CoOp);
        Assert.Equal(0, NetJoin.TeamOf(2, GameFlags.CoOp));                                       // Set, all on 0
        Assert.Equal(2, NetJoin.TeamOf(2, 0));                                                    // clear: combative
    }

    // ---- a real game ------------------------------------------------------------------------------

    [Fact]
    public void A_cast_is_taken_in_the_pause_and_refused_in_silence_while_the_Storyteller_has_the_body()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller, hostBody: false);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "Anna never played");
        var log = new List<string>();
        game.Server.Log += line => { lock (log) log.Add(line); };
        int Casts() { lock (log) return log.Count(l => l.Contains("Anna casts")); }
        var posted = new List<string>();
        anna.Client.Posted += (text, _) => posted.Add(text);
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        var cast = new NetCast(anna.Client.Avatar!.Guid, 0, 0, 0, null, "Celerity");

        game.Server.SetPaused(true);
        Assert.True(Until(() => anna.Client.Paused, All));
        int pauses = posted.Count;
        anna.Client.Send(cast);
        Assert.True(Until(() => Casts() == 1, All), "a cast in the pause was refused");            // no pause test in the host's cast
        Assert.Equal(pauses, posted.Count);                                                       // and no `UI_STSBPAUSE`

        game.Server.SetPaused(false);
        game.Server.Possessed = hers;                                                             // the host's steered body
        for (int i = 0; i < 10; i++) All();
        int before = posted.Count;
        anna.Client.Send(cast);
        for (int i = 0; i < 60; i++) All();
        Assert.Equal(1, Casts());                                                                 // refused …
        Assert.Equal(before, posted.Count);                                                       // … and nothing said
    }

    [Fact]
    public void A_possessed_players_line_is_shown_only_in_the_hosts_chat_box()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller, hostBody: false);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "Anna never played");
        var posted = new List<string>();
        anna.Client.Posted += (text, _) => posted.Add(text);
        game.Server.Posted += (text, _) => posted.Add("room: " + text);
        game.Server.Possessed = game.Server.Players.Single(p => p.Name == "Anna").Avatar;
        anna.Client.Say("let me go");
        Assert.True(Until(() => game.HostChat.Count > 0 && posted.Contains("You are posessed and unable to speak!"), All));   // MP_SAYFAILED
        Assert.Equal(("Anna tried to say 'let me go' (posessed)", StoryChat.TriedToSayColour), game.HostChat.Single());
        for (int i = 0; i < 10; i++) All();
        Assert.DoesNotContain(posted, p => p.Contains("tried to say"));                          // not to the room (the game's chat only)
        Assert.Empty(anna.Heard);
    }

    [Fact]
    public void The_moderated_host_sees_a_whisper_between_players_and_another_teams_talk_in_its_box()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller | GameFlags.Moderated, hostBody: false);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));
        anna.Client.Say("/bob psst");
        Assert.True(Until(() => bob.Heard.Count > 0 && game.HostChat.Count > 0, All));
        Assert.Equal("Anna whispers 'psst'", bob.Heard.Single());
        Assert.Equal(("Anna whispers to Bob, 'psst'", 0xff40d040u), game.HostChat.Single());   // MP_WHISPERALT
        Assert.Empty(game.Heard);                                                                 // not as a line said to it
        // Combative: Anna is team 1, alone on it; the host's box is told, Bob hears nothing.
        anna.Client.Say(",flank them");
        Assert.True(Until(() => game.HostChat.Count > 1, All));
        // Her team is her slot's (1 when she joined first; under the simulator Bob's knock may land first).
        int annaTeam = game.Server.Players.Single(p => p.Name == "Anna").Avatar!.Actor!.Team;
        Assert.Equal(($"Anna tells team {annaTeam}, 'flank them'", 0xffdd80ffu), game.HostChat[1]);       // MP_TEAMSAYALT
        for (int i = 0; i < 20; i++) All();
        Assert.Single(bob.Heard);
    }

    [Fact]
    public void A_whisper_moves_the_speakers_lips_only_where_it_is_shown_and_its_echo_is_the_senders_QoL()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller, hostBody: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik", new Qol { WhisperEcho = false });
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));
        var annaOnHost = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.True(Until(() => bob.Client.Mirror!.Find(annaOnHost.Guid) is { InWorld: true }, All), "Bob never saw Anna");
        var annaOnBobs = bob.Client.Mirror!.Find(annaOnHost.Guid)!;
        var annaOwn = anna.Client.Avatar!;
        static bool Speaking(WorldThing t) => t.Actor!.Has2(Actor.Flag2.Speaking);

        // Anna whispers to Bob: her lips move on Bob's machine and, by her echo, on hers — not on the host's.
        anna.Client.Say("/bob psst");
        Assert.True(Until(() => Speaking(annaOnBobs) && Speaking(annaOwn), All), "the two ends never moved her lips");
        Assert.Contains(anna.Heard, h => h == "Anna whispers to Bob, 'psst'");                   // her copy (whisperecho)
        Assert.False(Speaking(annaOnHost));
        // The talk on Bob's machine is his own, which the host's snapshots (saying she does nothing) leave alone.
        if (Walkless(annaOnBobs)) Assert.True(ChatLips.Local(annaOnBobs.Animation!.Action));
        Assert.True(Until(() => !Speaking(annaOnBobs) && !Speaking(annaOwn), All), "her lips never stopped");

        // To the host (slot 0): the host's copy talks, and Bob, who is not told, never sees her mouth move.
        anna.Client.Say(".a word, Hank");
        Assert.True(Until(() => Speaking(annaOnHost), All), "the host never moved her lips");
        if (Walkless(annaOnHost)) Assert.True(ChatLips.Local(annaOnHost.Animation!.Action));
        for (int i = 0; i < 30; i++)
        {
            All();
            Assert.False(Speaking(annaOnBobs));
            Assert.NotEqual(ChatLips.TalkMode, annaOnBobs.Animation?.Action?.Mode.Index ?? -1);
        }

        // Bob has the echo off: he is shown nothing of his own whisper and his mouth stays still at home.
        int heard = bob.Heard.Count;
        bob.Client.Say("/anna later");
        Assert.True(Until(() => anna.Heard.Contains("Bob whispers 'later'"), All));
        for (int i = 0; i < 10; i++) All();
        Assert.Equal(heard, bob.Heard.Count);
        Assert.False(Speaking(bob.Client.Avatar!));
    }

    /// <summary>A thing whose talk could be played (not walking, with an animation) — else the lips leave the motion out.</summary>
    static bool Walkless(WorldThing t) => t.Animation is not null && !(t.Locomotion?.Walking ?? t.Walking);

    // ---- the rig ----------------------------------------------------------------------------------

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

    sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Simulation Sim;
        public readonly List<string> Heard = [];
        public readonly List<(string, uint)> HostChat = [];
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
        public Simulation? Sim;
        public readonly List<string> Heard = [];
        public void Step()
        {
            Client.Poll(1f / 30);
            Sim?.Step(1f / 30);
        }
        public void Dispose() => Client.Dispose();
    }

    /// <summary>The Dark Ages Inn salon (`MP_DAInn`), hosted by Hank (`MP_Kazi`) in person or by a host with no body.</summary>
    static Game Host(Vfs.GameFiles files, GameFlags flags, bool hostBody)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Hank's game", flags);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null,
                                  player: hostBody ? "MP_Kazi" : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0), hostBody ? new NetCharacter { Name = "Hank", Template = "MP_Kazi" } : null)
            { Qol = new Qol(), Fixes = new Fixes(), Find = new Ui.Strings(files).Find };
        server.Start();
        server.Enter(place, hostBody ? place.Player?.Thing : null);
        var game = new Game { Server = server, Sim = new Simulation(place) };
        server.Heard += (_, text) => game.Heard.Add(text);
        server.HostChat += (text, colour) => game.HostChat.Add((text, colour));
        return game;
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template, Qol? qol = null)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }) { Qol = qol ?? new Qol() },
        };
        p.Client.Find = new Ui.Strings(files).Find;
        p.Client.Chatted += (line, _, _) => p.Heard.Add(line);
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
