// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Net.Sockets;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>Tests that put a tape in force (<see cref="NetTape.Tape"/>, one for the whole process): run alone.</summary>
[CollectionDefinition(nameof(NetTapeCollection), DisableParallelization = true)]
public sealed class NetTapeCollection;

/// <summary>
/// **A recording player who loads longer than the timeout under the simulator** (the cause of ClassicNetworkTests' dedicated
/// host failing now and then under <c>VITOMIR_NET_SIM</c>, the player disconnected right after the load). The keep-alive took the
/// host's datagrams off the socket into the simulator during the load only when no tape was in, so a recording player read none of
/// them at its first turn after the load (the simulator held each for its way's delay from then) and gave the host up as silent
/// since before the load. <see cref="NetInternetTests"/>'s keep-alive test is this one without the tape.
/// </summary>
[Collection(nameof(NetTapeCollection))]
public sealed class NetKeepAliveTapeTests
{
    /// <summary>A tape that records (it reads the socket as a game does) and keeps nothing.</summary>
    sealed class Recording : INetTape
    {
        public long Clock(long live) => live;
        public int Seed(int live) => live;
        public bool Receive(Socket socket, byte[] buffer, ReceiveRead read, Func<byte[], int, byte[]> taped, out int length, out EndPoint from) =>
            read(buffer, out length, out from);
        public bool WrongPassword(bool live) => live;
    }

    static bool Until(Func<bool> done, int ms, Action turn)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            turn();
            if (done()) return true;
            Thread.Sleep(5);
        }
        return done();
    }

    [Fact]
    public void A_recording_player_loading_longer_than_the_timeout_stays_joined_under_the_simulator()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        var spec = new NetSim.Spec(LatencyMs: 80, JitterMs: 20, Loss: 2, Reorder: 1, Dup: 0.5, Kbps: 1000);
        using var server = new GameServer(settings, new NetPeer(0, seed: 1) { Sim = new NetSim(spec, 1) }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var loaded = false;
        var was = NetTape.Tape;
        NetTape.Tape = new Recording();
        using var stop = new CancellationTokenSource();
        int ms = 0;
        var hostLoop = new Thread(() => { while (!stop.IsCancellationRequested) { lock (server) server.Poll(ms += 5); Thread.Sleep(5); } });
        var client = new GameClient(new NetPeer(0, seed: 2) { Sim = new NetSim(spec, 2) }, new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                    new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        try
        {
            // The player's load (GameClient.Started, the host's answer to its Start) sleeps two seconds past the transport's ten.
            client.Started += () => { Thread.Sleep(NetPeer.TimeoutMs + 2_000); loaded = true; };
            hostLoop.Start();
            Assert.True(Until(() => loaded || client.State == ClientState.Gone, 40_000, () => client.Poll(0.01f)), $"the load never ended: {client.State}");
            // Her first turns after the load: the host's datagrams of the load are read, not held back, and the connection stands.
            Until(() => client.State == ClientState.Gone, 1_000, () => client.Poll(0.01f));
            Assert.NotEqual(ClientState.Gone, client.State);
            var mine = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, player: "MP_Serena", entrance: settings.Entrance);
            mine.Session = new Session { Here = mine };
            client.Enter(mine, mine.Player?.Thing);
            Assert.True(Until(() => client.AvatarGuid != 0 || client.State == ClientState.Gone, 20_000, () => client.Poll(0.01f)), $"never placed: {client.State} {client.Reason}");
            Assert.Equal(ClientState.Playing, client.State);
            lock (server) Assert.Contains(server.Players, p => p.Name == "Anna" && p.Avatar is not null);
        }
        finally
        {
            stop.Cancel();
            if (hostLoop.IsAlive) hostLoop.Join();
            client.Dispose();
            NetTape.Tape = was;
        }
    }
}
