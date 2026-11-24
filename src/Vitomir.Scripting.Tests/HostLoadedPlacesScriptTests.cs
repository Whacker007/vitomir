// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A host's early-loaded place runs its scripts with nobody in it** (`Net/HostPlaces.Loaded.cs`,
/// `CodexScenes.Places.cs`; the place tick's and the timers' host branches). A place loaded early — made and told
/// `created`, then put aside to wait for the party, as a chronicle change's start does (
/// `CodexScenes.Preload`) — is, on a host, one of the worlds the tick walks: its scripts are woken
/// beside the host's own place and it is stepped, the host's own place keeping the scripts between
/// steps. Driven with *To Curse the Darkness*: the inn hosted, Temesvar loaded early.
/// </summary>
[Collection("scripts")]
public class HostLoadedPlacesScriptTests(Scripts s)
{
    const int Temesvar = 1;

    [Fact]
    public void An_early_place_nobody_is_in_has_its_scripts_run_and_is_stepped_on_a_host()
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
        var at = scenes.Chronicle!.At(Temesvar)!;
        var temesvar = Location.Load(files, at.LevelName, scene: at.SceneName.Length > 0 ? at.SceneName : null);
        temesvar.Session = session;
        scenes.Preload(temesvar, scenes.Chronicle);                                // made, `created`, put aside
        Assert.True(scenes.Waiting(temesvar));
        Assert.False(scenes.Runs(temesvar));

        var loaded = new Dictionary<int, Location> { [Temesvar] = temesvar };
        server.Places.Chronicle = scenes.Chronicle;
        server.Places.Loaded = () => loaded;
        server.Places.Simulate = there => new Simulation(there) { Scripts = scenes };
        server.Start();
        server.Enter(inn, null);
        for (int i = 0; i < 60; i++) { sim.Step(1f / 30); server.Places.Step(1f / 30); }

        Assert.True(scenes.Runs(temesvar));                                         // woken: its scripts run beside the inn's
        Assert.False(scenes.Waiting(temesvar));
        Assert.Same(inn, scenes.Current);                                           // the host's own place has them between steps
        var h = server.Places.Of(temesvar);
        Assert.NotNull(h?.Sim);
        Assert.True(h!.Sim!.Seconds > 1.5f, $"Temesvar was stepped to {h.Sim.Seconds:0.00} s");
        Assert.False(server.Places.Occupied(temesvar));
    }
}
