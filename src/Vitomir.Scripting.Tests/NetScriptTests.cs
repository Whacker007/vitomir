// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The MP chronicles' own scripts on a host with a player on localhost: a
/// player joining *To Curse the Darkness* is shown the intro text on their own machine by the bar
/// keeper's `joined` (`ExecuteText` → message 0x26), the host opens
/// nothing of its own, and the close comes back as the script's `textended`. The ending
/// chain's first link — `Advance(player)` from a script — opens the advancement on that player's
/// machine.
/// </summary>
[Collection("scripts")]
public class NetScriptTests(Scripts s)
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 8000)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    [Fact]
    public void A_player_joining_curse_the_darkness_reads_the_intro_on_their_own_machine()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var session = new Session { Here = place };
        place.Session = session;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        var opened = new List<string>();
        scenes.ReadText = name => { opened.Add(name); return true; };            // the host's own pane
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        NetNatives.Register(scenes, server);
        var sim = new Simulation(place) { Scripts = scenes };
        scenes.Enter(place);
        Assert.Contains(scenes.Running, r => r.Contains("CTD_BarKeep"));
        server.Start();
        server.Enter(place, null);
        int ms = 0;
        void Host() { sim.Step(1f / 30); ms += 33; server.Poll(ms); }

        var read = new List<string>();
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                          new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Texts.Open = name => { read.Add(name); return true; };
        var advanced = new List<WorldThing>();
        client.Texts.AdvanceAsked += advanced.Add;
        Location? mine = null;
        client.Started += () =>
        {
            var c = client.Settings!;
            mine = Location.Load(files, c.Level, scene: c.Scene.Length > 0 ? c.Scene : null, player: "MP_Serena", entrance: c.Entrance);
            client.Enter(mine, mine.Player?.Thing);
        };
        void Both() { Host(); client.Poll(1f / 30); }

        Assert.True(Until(() => read.Count > 0, Both), $"her machine never opened a text ({client.State})");
        Assert.Equal("IntroTextCTD", read.Single());
        Assert.DoesNotContain("IntroTextCTD", opened);                        // not on the host's screen

        // Closing it goes back to the bar keeper's script, which answers nothing but must hear it.
        client.Texts.Closed();
        for (int i = 0; i < 20; i++) Both();

        // A script's Advance(player) opens her advancement screen, on her machine.
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        hers.Actor!.SpendableExperience = 2000;
        s.Engine.get(new object[] { "CodexSequence.Advance", null!, new object[] { java.lang.Integer.valueOf(hers.Guid) } });
        Assert.True(Until(() => advanced.Count > 0, Both), "her screen never opened");
        Assert.Equal(2000, advanced[0].Actor!.SpendableExperience);
    }

    /// <summary>
    /// A run (`--host --mp-chronicle MP_CurseDarkness --name Hosta --player-template MP_Christof_DA`,
    /// then `--join 127.0.0.1 --name Anna --player-template MP_Serena`), in the windowed host's order: the
    /// scene's scripts begin with the host's own character as the player (`beginscene`, single player's
    /// natives), then the session is tied to the world (`NetPlay.Attach`: `GameServer.Enter`, then
    /// `NetNatives.Register`). The bar keeper's `beginscene` shows the intro once — to the host, and
    /// sets the chronicle's `CTD_TEXTINTRO` flag — and a player joining hears `joined`, which opens it
    /// on her machine alone; her `beginscene` finds the flag set and shows nothing, so neither screen
    /// opens it twice.
    /// </summary>
    [Fact]
    public void A_playing_hosts_intro_opens_once_at_the_start_and_a_joiners_on_her_machine_alone()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Hosta's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null,
                                  player: "MP_Christof_DA", entrance: settings.Entrance);
        var session = new Session { Here = place };
        place.Session = session;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        var opened = new List<string>();
        scenes.ReadText = name => { opened.Add(name); return true; };            // the host's own pane
        var sim = new Simulation(place) { Scripts = scenes };
        scenes.Enter(place);
        Assert.Equal("IntroTextCTD", opened.Single());                          // the host's own, at the start

        using var server = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = "Hosta", Template = "MP_Christof_DA" })
        {
            Qol = new Qol(), Fixes = new Fixes(),
        };
        server.Start();
        server.Enter(place, place.Player!.Thing);
        NetNatives.Register(scenes, server);
        int ms = 0;
        void Host() { sim.Step(1f / 30); ms += 33; server.Poll(ms); }

        var read = new List<string>();
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                          new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Texts.Open = name => { read.Add(name); return true; };
        client.Started += () =>
        {
            var c = client.Settings!;
            var mine = Location.Load(files, c.Level, scene: c.Scene.Length > 0 ? c.Scene : null, player: "MP_Serena", entrance: c.Entrance);
            client.Enter(mine, mine.Player?.Thing);
        };
        void Both() { Host(); client.Poll(1f / 30); }

        Assert.True(Until(() => read.Count > 0, Both), $"her machine never opened a text ({client.State})");
        for (int i = 0; i < 60; i++) Both();
        Assert.Equal("IntroTextCTD", read.Single());                            // hers, once, from `joined`
        Assert.Equal("IntroTextCTD", opened.Single());                          // and not again on the host
    }
}
