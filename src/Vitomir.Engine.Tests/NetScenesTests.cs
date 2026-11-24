// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The conversations and cutscenes of a network game,
/// with a host and players on localhost against the install: a conversation a script starts for a
/// player runs on that player's machine and the host's script hears its lines, its reply — with the
/// step where a conversation on the host would give the reply's index, as 1.1's host does —
/// and its end; a player standing near hears it as subtitles; nothing starts without Auto
/// Storyteller; and a script's camera shot and release for a player move that player's
/// camera, not the host's.
/// </summary>
public class NetScenesTests
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
        public readonly Conversations Talk = new();
        public readonly WorldCamera Camera = new() { HeadOf = null };
        public readonly List<string> Subtitles = [];
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    static Game Host(Vfs.GameFiles files, GameFlags flags)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", flags);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0));
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Place = place, Sim = new Simulation(place) { Talk = new Conversations() } };
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }),
        };
        p.Client.Scenes.Talk = p.Talk;
        p.Client.Scenes.Camera = p.Camera;
        p.Client.Scenes.Subtitled += p.Subtitles.Add;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
            p.Sim = new Simulation(p.Place) { Talk = p.Talk };
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    [Fact]
    public void A_scripts_conversation_for_a_player_runs_on_their_machine_and_the_host_hears_every_step()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        // Bob stands beside her, well inside the 800 units of earshot.
        game.Server.Players.Single(p => p.Name == "Bob").Avatar!.Position = hers.Position + new Vector3(60, 0, 0);

        var heard = new List<string>();
        bool started = game.Sim.Talk!.Execute(game.Place, "12_1_Bill", "12_1_Bill", ConversationFlag.Callbacks, hers.Guid, game.Ms,
            npc: 0, wire: c =>
            {
                c.Reached += (id, ev, step, _, _) => heard.Add($"reached {id} {ev} {step}");
                c.Chosen += (id, ev, n, _, _) => heard.Add($"chosen {id} {ev} {n}");
                c.Ended += (id, aborted, code) => heard.Add($"ended {id} {aborted} {code}");
            });
        Assert.True(started);
        // Not on the host's screen: in Anna's slot, and on her machine.
        Assert.Null(game.Sim.Talk.Current);
        Assert.NotNull(game.Server.Players.Single(p => p.Name == "Anna").Talking);
        Assert.True(Until(() => anna.Talk.Current is { Waiting: true }, All), "her machine never offered the replies");
        Assert.Equal("12_1_Bill", anna.Talk.Current!.Talk.Name);
        Assert.True(Until(() => heard.Contains($"reached {hers.Guid} 0 0"), All), string.Join("; ", heard));
        // Bob, beside her, overhears Bill's line as a subtitle.
        Assert.True(Until(() => bob.Subtitles.Count > 0, All), "Bob never overheard it");
        Assert.StartsWith("<", bob.Subtitles[0]);

        // She picks the second reply, `ANSWERRETURN: … 2`, the third step of the event.
        Assert.True(anna.Talk.Current.Pick(1, anna.Place!.Milliseconds));
        Assert.True(Until(() => heard.Any(h => h.StartsWith("ended")), All), string.Join("; ", heard));
        // 1.1's host passes the reply's step (2) where a conversation of its own passes its index (1).
        Assert.Contains($"chosen {hers.Guid} 0 2", heard);
        Assert.Contains($"ended {hers.Guid} False 2", heard);
        Assert.Null(game.Server.Players.Single(p => p.Name == "Anna").Talking);
        Assert.Null(anna.Talk.Current);
    }

    [Fact]
    public void Without_auto_storyteller_no_conversation_starts_and_a_players_machine_starts_none_of_its_own()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.Moderated);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()));
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.False(game.Sim.Talk!.Execute(game.Place, "12_1_Bill", "12_1_Bill", ConversationFlag.Callbacks, hers.Guid, game.Ms));
        // And her machine, with no host behind it, refuses to begin one itself (the conversation start's client test).
        Assert.False(anna.Talk.Execute(anna.Place!, "12_1_Bill", "12_1_Bill", ConversationFlag.Callbacks, anna.Client.Avatar!.Guid, 0));
        // A thing no player has cannot start one on the host at all.
        game.Server.Flags |= GameFlags.AutoStoryteller;
        var someone = game.Place.Things.First(t => !ReferenceEquals(t, hers));
        Assert.False(game.Sim.Talk.Execute(game.Place, "12_1_Bill", "12_1_Bill", ConversationFlag.Callbacks, someone.Guid, game.Ms));
    }

    [Fact]
    public void A_scripts_cutscene_shot_for_a_player_moves_their_camera_and_the_release_gives_it_back()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()));
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        var route = SceneRoutes.For(game.Place)!;
        // The script names her: kept on the host, then sent with the shot.
        Assert.True(route.SetupCutscene(hers.Guid, hers, null));
        Assert.True(route.SetShot(hers.Guid, 0, 2, 2, 0, 0));
        Assert.True(Until(() => anna.Camera.Mode == CameraMode.Cutscene, () => game.Step(), () => anna.Step()), "her camera never cut");
        Assert.True(route.Release(hers.Guid));
        Assert.True(Until(() => anna.Camera.Mode == CameraMode.Game, () => game.Step(), () => anna.Step()), "her camera never came back");
        // A GUID that is nobody's machine is the host's own business.
        Assert.False(route.SetShot(12345, 0, 2, 2, 0, 0));
    }
}
