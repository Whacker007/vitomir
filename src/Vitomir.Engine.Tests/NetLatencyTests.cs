// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Latency, measured first** (measure, then predict): under the network-conditions simulator on the
/// player's machine at a round trip of 50, 100 and 150 ms, how long from a walk click on the player's machine to her own
/// character answering there (its first step), against the host's (her character's first step on the host, and the host's own
/// world turning: one frame). A timing test (left out of the plain run), paced at the wall clock's 30 frames a second, as a
/// game is; the figures are the report's. Prediction itself is not built.
/// </summary>
public sealed class NetLatencyTests(ITestOutputHelper output)
{
    const float Step = 1f / 30;

    [Fact, Trait("Category", "Timing")]
    public void A_walk_click_on_a_players_machine_answers_there_and_on_the_host_measured_at_three_round_trips()
    {
        using var files = Install.Open();
        var lines = new List<string>();
        foreach (int rtt in new[] { 50, 100, 150 })
        {
            var (local, host) = Walk(files, rtt);
            lines.Add($"round trip {rtt} ms: her own character's first step on her machine {local:0} ms after the click, on the host {host:0} ms");
            Assert.True(local < 5_000 && host < 5_000, lines[^1]);
        }
        output.WriteLine("a walk click on a player's machine (30 frames a second, the simulator on her machine alone; the host's own click answers in its next frame, 33 ms):");
        foreach (var l in lines) output.WriteLine("  " + l);
    }

    static (double Local, double Host) Walk(Vfs.GameFiles files, int rtt)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt), rtt) };
        using var client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Own.On = false;                       // The measure before her own walk was predicted (NetPredictionTests)
        Location? mine = null;
        Simulation? herSim = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
            herSim = new Simulation(mine);
            client.Enter(mine, mine.Player?.Thing);
        };
        var wall = Stopwatch.StartNew();
        long frame = 0;
        void Turn()
        {
            sim.Step(Step);
            server.Poll((int)wall.ElapsedMilliseconds);
            client.Poll(Step);
            herSim?.Step(Step);
            frame++;
            long due = (long)(frame * Step * 1000);
            long wait = due - wall.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        bool Until(Func<bool> done, int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { Turn(); if (done()) return true; }
            return done();
        }
        Assert.True(Until(() => client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null), 30_000), "never played");
        Until(() => false, 1_000);
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        var copy = mine!.Player!;
        var startHere = copy.Thing.Position;
        var startThere = hers.Position;
        var to = Far(mine, startHere);
        var click = Stopwatch.StartNew();
        Assert.True(copy.WalkTo(to, mine.Walk!.Find(to, 256f)));
        double local = -1, host = -1;
        Until(() =>
        {
            if (local < 0 && Vector3.Distance(copy.Thing.Position, startHere) > 1f) local = click.Elapsed.TotalMilliseconds;
            if (host < 0 && Vector3.Distance(hers.Position, startThere) > 1f) host = click.Elapsed.TotalMilliseconds;
            return local >= 0 && host >= 0;
        }, 6_000);
        return (local < 0 ? double.PositiveInfinity : local, host < 0 ? double.PositiveInfinity : host);
    }

    static Vector3 Far(Location place, Vector3 from)
    {
        var map = place.Walk!;
        for (int a = 0; a < 16; a++)
        {
            float r = a * MathF.PI / 8;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * 300f;
            if (map.Find(p, 256f) >= 0) return p;
        }
        return from + new Vector3(100, 0, 0);
    }
}
