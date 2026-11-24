// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A network host ticks every place it has loaded** (`Net/HostPlaces.Loaded.cs`; the place tick's host branch,
/// and the timers'). A place nobody is in — here Sorvena's cottage, loaded beside the inn
/// the host plays in, as a chronicle change's early places or a place every player has left are — is
/// adopted by the host and stepped with the rest, on the host's clock. Earlier, Vitomir's host stepped
/// only the places a player or the camera was in.
/// </summary>
public class HostLoadedPlacesTests
{
    const int Cottage = 2;

    [Fact]
    public void A_loaded_place_nobody_is_in_is_stepped_by_the_host_on_the_host_s_clock()
    {
        var files = Install.Open();
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(story, "Anna's game", GameFlags.AutoStoryteller);
        var inn = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        var sim = new Simulation(inn);
        story.Current = story.Data.ReviveLocation;
        server.Places.Chronicle = story;
        var at = story.At(Cottage)!;
        var cottage = Location.Load(files, at.LevelName, scene: at.SceneName.Length > 0 ? at.SceneName : null);
        var loaded = new Dictionary<int, Location> { [Cottage] = cottage };
        server.Places.Loaded = () => loaded;
        server.Start();
        server.Enter(inn, null);

        sim.Step(5f);                                                               // the host's game five seconds on
        Assert.False(server.Places.Occupied(cottage));
        for (int i = 0; i < 30; i++) { sim.Step(1f / 30); server.Places.Step(1f / 30); }

        var h = server.Places.Of(cottage);
        Assert.NotNull(h);                                                          // adopted: one of the host's places
        Assert.Equal(Cottage, h!.Index);
        Assert.NotNull(h.Sim);
        Assert.True(h.Sim!.Seconds >= 5.9f, $"the cottage's clock is at {h.Sim.Seconds:0.00} s, not the host's");
        Assert.Equal(inn.Milliseconds, cottage.Milliseconds, tolerance: 40);        // one clock
    }
}
