// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A host's scripts in several places** (`CodexScenes.Places.cs`, `Net.HostPlaces`): with *To Curse
/// the Darkness* hosted and Anna sent alone from the inn to Temesvar, Temesvar's scripts are made
/// beside the inn's without the host leaving the inn (the chronicle's current location stays the
/// inn's), the scripts work in Temesvar while it is stepped and in the inn after, the bar keeper's
/// `joined` is not heard a second time, and a script's `TakeExit` with her GUID takes Temesvar's exit
/// for her alone — the host's own journey is never asked for.
/// </summary>
[Collection("scripts")]
public class NetPlacesScriptTests(Scripts s)
{
    const int Temesvar = 1, Catacombs1 = 4;

    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 30000)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    [Fact]
    public void A_player_alone_in_another_place_has_its_scripts_and_takes_its_exits_alone()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var story = set["MP_CurseDarkness"]!;
        var settings = GameSettings.For(story, "Anna's game", GameFlags.AutoStoryteller);
        var inn = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var session = new Session { Here = inn };
        inn.Session = session;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        NetNatives.Register(scenes, server);
        var sim = new Simulation(inn) { Scripts = scenes };
        scenes.Enter(inn);
        int innIndex = scenes.Chronicle!.Current;
        var loaded = new Dictionary<int, Location>();
        server.Places.Chronicle = scenes.Chronicle;
        server.Places.Load = (index, entrance) =>
        {
            if (loaded.TryGetValue(index, out var had)) return had;
            var at = scenes.Chronicle.At(index)!;
            var there = loaded[index] = Location.Load(files, at.LevelName, scene: at.SceneName.Length > 0 ? at.SceneName : null, entrance: entrance);
            there.Session = session;
            scenes.Open(there);
            return there;
        };
        server.Places.Simulate = there => new Simulation(there) { Scripts = scenes };
        server.Start();
        server.Enter(inn, null);
        int ms = 0;
        void Host() { sim.Step(1f / 30); server.Places.Step(1f / 30); ms += 33; server.Poll(ms); }

        var read = new List<string>();
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                          new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Texts.Open = name => { read.Add(name); return true; };
        Location? mine = null;
        client.Started += () =>
        {
            var c = client.Settings!;
            mine = Location.Load(files, c.Level, scene: c.Scene.Length > 0 ? c.Scene : null, player: "MP_Serena", entrance: c.Entrance);
            client.Enter(mine, mine.Player?.Thing);
        };
        void Both() { Host(); client.Poll(1f / 30); }
        Assert.True(Until(() => read.Count > 0, Both), $"she never arrived ({client.State})");

        // Alone to Temesvar: its scripts are made beside the inn's, and the host stays in the inn.
        var anna = server.Players.Single(p => p.Name == "Anna");
        Assert.True(server.Places.Move(anna.Avatar!, Temesvar, 0));
        var temesvar = loaded[Temesvar];
        Assert.True(scenes.Runs(temesvar));
        Assert.Same(inn, scenes.Current);
        Assert.Equal(innIndex, scenes.Chronicle.Current);
        string level = story.At(Temesvar)!.LevelName;
        Assert.True(Until(() => client.State == ClientState.Playing && mine?.Name.Equals(level, StringComparison.OrdinalIgnoreCase) == true
                                && anna.Avatar is { InWorld: true }, Both), "she never reached Temesvar");
        for (int i = 0; i < 20; i++) Both();
        Assert.Single(read);                                                   // `joined` is for the first arrival only
        Assert.Same(inn, scenes.Current);                                      // the host's own place has the scripts between steps
        Assert.Null(scenes.Chronicle.Leaving);

        // Temesvar's own exit 1, taken by a script with her GUID, sends her alone to the catacombs.
        scenes.Use(temesvar);
        s.Engine.get(new object[] { "CodexSequence.TakeExit", null!, new object[] { java.lang.Integer.valueOf(1), java.lang.Integer.valueOf(anna.Avatar!.Guid) } });
        scenes.Use(inn);
        Assert.Null(scenes.Chronicle.Leaving);                                 // not the host's journey
        Assert.Same(loaded[Catacombs1], anna.Place);
        Assert.True(Until(() => mine?.Name.Equals(story.At(Catacombs1)!.LevelName, StringComparison.OrdinalIgnoreCase) == true
                                && client.State == ClientState.Playing && anna.Avatar is { InWorld: true }, Both), "she never took the exit");
        Assert.Equal(innIndex, scenes.Chronicle.Current);
    }
}
