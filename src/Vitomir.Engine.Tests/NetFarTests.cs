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
/// **Players far away** (friends across the US and in Australia, round trips of 150 to 250 ms;
/// the round trip is in the logs): a player
/// 200 and 250 ms from the host (the simulator on her machine, with the usual jitter and loss) joins through the whole handshake, is
/// let in, plays and leaves with nothing timed out; the host's log and hers each say the round trip measured, its least and its
/// most, and what the line says is what the wire did. Paced at the wall clock's 30 frames a second, as a game is (the simulator
/// keeps the wall clock's time).
/// <para>**Timing tests** (<c>[Trait("Category", "Timing")]</c>), every one: each holds a round trip measured at the wall clock to a
/// bound, which a machine busy with the rest of the plain run's assemblies stretches (by up to 150 ms on a full run), so they run
/// alone after it, as the other timing tests do.</para>
/// </summary>
[Trait("Category", "Timing")]
public sealed class NetFarTests
{
    const float Step = 1f / 30;

    /// <summary>The simulated wire's jitter: the spread of a round trip (each way ±a quarter of it, <see cref="NetSim"/>).</summary>
    const int JitterMs = 20;

    [Theory]
    [InlineData(200)]
    [InlineData(250)]
    public void A_far_player_joins_plays_and_leaves_and_both_logs_say_the_round_trip(int rtt)
    {
        using var files = Install.Open();
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var hostSaid = new List<string>();
        var herSaid = new List<string>();
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Log += l => { lock (hostSaid) hostSaid.Add(l); };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt, JitterMs: JitterMs, Loss: 2, Seed: 17 + rtt), 17 + rtt) };
        var client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Log += l => { lock (herSaid) herSaid.Add(l); };
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
        var push = Vector2.Zero;
        void Turn()
        {
            sim.Step(Step);
            server.Poll((int)wall.ElapsedMilliseconds);
            client.Poll(Step);
            if (herSim is not null && mine?.Player is { } me) client.Drive(push, true, me, Step);   // her keys, as her frame reads them
            herSim?.Step(Step);
            frame++;
            long wait = (long)(frame * Step * 1000) - wall.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        bool Until(Func<bool> done, int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { Turn(); if (done()) return true; }
            return done();
        }
        try
        {
            // The join's whole handshake (the knock and its answer, the hello and its proof, the welcome, Start, the place) at this distance.
            Assert.True(Until(() => client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null), 30_000),
                $"never played at {rtt} ms: {client.State}\n{string.Join('\n', herSaid.TakeLast(8))}");
            // A few seconds of play: a run by the keys, predicted on her machine, let go; she stands where the host has her.
            var her = mine!.Player!.Thing;
            var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
            Until(() => false, 1_000);
            var from = her.Position;
            push = Vector2.Normalize(new Vector2(-1f, 1f));
            Until(() => Vector3.Distance(her.Position, from) > 1f, 100);
            Assert.True(client.Own.Driving && Vector3.Distance(her.Position, from) > 1f, "her keys did not move her at once");
            Until(() => false, 1_200);
            push = Vector2.Zero;
            Assert.True(Until(() => !client.Own.Active && Vector3.Distance(her.Position, hers.Position) < 2f, 1_700),
                $"she stands {Vector3.Distance(her.Position, hers.Position):0.0} units from where the host has her; {client.Own.Line()}");
            Assert.Equal(0, client.Own.Done.DriveRefused + client.Own.Done.DriveStopped + client.Own.Done.DriveShoved);
            Assert.Equal(0, client.Own.Done.Snapped);
            Assert.Equal(ClientState.Playing, client.State);
            var link = server.Players.Single(p => p.Name == "Anna").Link!;
            // What the transport measured is the distance, counted in this loop's turns. The simulator adds its round trip to
            // loopback's, the jitter spreading it by half its figure either way, and three waits for a turn come on top: her end
            // reads a message on her next turn (the simulator hands over what is due when she polls), the acknowledgement she
            // sends then leaves the simulator on her next turn after its way out is due (it lets go of what it holds only when she
            // polls; a real wire would not wait for her), and the host reads it on its next turn — a whole one, since each turn
            // polls the host just before her. So every round trip measured is a whole number of turns: at 200 ms the least is 7
            // (233 ms) when the jitter falls well both ways, 8 (267) when it does not on any of the ~20 messages measured, and at
            // 250 it is 9 (300). A player 200 ms away feels the wire and her own and the host's frames, nothing more.
            Assert.InRange(link.RoundTripsMeasured, 3, 100_000);                          // the host's reliable messages, sent once and answered
            float turnMs = Step * 1000;
            Assert.InRange(link.RoundTripLeastMs, rtt - JitterMs / 2, rtt + JitterMs / 2 + 3 * turnMs);
            Assert.InRange(link.RoundTripMostMs, link.RoundTripLeastMs, 5_000);           // the most takes in a stall: her load, here on the host's thread
            Assert.InRange(link.RoundTripMs, rtt - 20, rtt + 120);
            client.Leave();
            Assert.True(Until(() => !server.Players.Any(p => p.Name == "Anna"), 10_000), "the host never saw her leave");
        }
        finally { client.Dispose(); }
        lock (hostSaid)
        {
            Assert.DoesNotContain(hostSaid, l => l.Contains("timeout", StringComparison.Ordinal));
            var line = Assert.Single(hostSaid, l => l.StartsWith(RoundTrips.Mark + "to Anna (", StringComparison.Ordinal));
            Assert.EndsWith("; Anna disconnected", line);                                // 1.1's host has one line for a going
            Assert.Contains(" ms smoothed, least ", line);
            Assert.Contains(" measured over ", line);
        }
        lock (herSaid)
        {
            var line = Assert.Single(herSaid, l => l.StartsWith(RoundTrips.Mark + "to the host (", StringComparison.Ordinal));
            Assert.Contains("; out of the game (UI_NET_YOULEFTGAME)", line);
            Assert.Contains(" ms smoothed, least ", line);
        }
        Assert.Contains(RoundTrips.Last(), l => l.StartsWith(RoundTrips.Mark + "to Anna (", StringComparison.Ordinal));
    }

    /// <summary>
    /// **A round trip longer than twice the first guess is learned** (a bug found at 200 ms): the transport's first guess is
    /// 100 ms and a reliable message unanswered in twice the guess is sent again; Karn's rule measures only a message sent once, so on
    /// a wire of 200 ms and more every message was sent twice and nothing was ever measured (the host measured none of a far player's
    /// in a whole session). A resend backs the timer off (Karn's algorithm) until a round trip is measured again.
    /// </summary>
    [Theory]
    [InlineData(250)]
    [InlineData(400)]
    public void A_round_trip_longer_than_twice_the_first_guess_is_learned(int latency)
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        using var guest = new NetPeer(0, seed: 2) { Sim = new NetSim(new NetSim.Spec(LatencyMs: latency), 3) };
        var link = guest.Connect(new IPEndPoint(IPAddress.Loopback, host.Port));
        var sw = Stopwatch.StartNew();
        while (!link.Open && sw.ElapsedMilliseconds < 5_000) { host.Poll(); guest.Poll(); Thread.Sleep(2); }
        Assert.True(link.Open);
        var clock = Stopwatch.StartNew();
        int i = 0;
        while (clock.ElapsedMilliseconds < 6_000)
        {
            if (clock.ElapsedMilliseconds / 300 >= i) guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).I32(i++).ToArray());
            host.Poll(); guest.Poll(); Thread.Sleep(5);
        }
        Assert.True(link.RoundTripsMeasured >= 5, $"{link.RoundTripsMeasured} measured of {i} sent: {RoundTrips.Line("to the host", link, link.StartedMs + 6_000)}");
        Assert.InRange(link.RoundTripMs, latency * 0.7f, latency + 100);
        Assert.InRange(link.RoundTripLeastMs, latency - 10, latency + 50);
    }

    /// <summary>
    /// **The line, and when it is said**: the smoothed figure, the least and the most measured and how many, over how long; none
    /// measured yet said so; said a minute after the connection began and each minute after, by the transport's clock.
    /// </summary>
    [Fact]
    public void The_round_trips_line_says_the_smoothed_the_least_and_the_most_and_is_said_once_a_minute()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        using var guest = new NetPeer(0, seed: 2) { Sim = null };
        var link = guest.Connect(new IPEndPoint(IPAddress.Loopback, host.Port));
        Assert.Matches(@"^net: round trip to the host \(127\.0\.0\.1:\d+\): none measured yet over \d+ s$", RoundTrips.Line("to the host", link, link.StartedMs + 3_000));
        var sw = Stopwatch.StartNew();
        while (!link.Open && sw.ElapsedMilliseconds < 5_000) { host.Poll(); guest.Poll(); Thread.Sleep(1); }
        Assert.True(link.Open);
        for (int i = 0; i < 20 && link.RoundTripsMeasured < 3; i++)
        {
            guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).I32(i).ToArray());
            for (int t = 0; t < 30; t++) { host.Poll(); guest.Poll(); Thread.Sleep(2); }
        }
        Assert.True(link.RoundTripsMeasured >= 3, $"{link.RoundTripsMeasured} measured");
        Assert.True(link.RoundTripLeastMs <= link.RoundTripMostMs);
        string line = RoundTrips.Line("to the host", link, link.StartedMs + 312_000, "; the session ended");
        Assert.Matches(@"^net: round trip to the host \(127\.0\.0\.1:\d+\): \d+ ms smoothed, least \d+ ms, most \d+ ms, \d+ measured over 5 min 12 s; the session ended$", line);
        long said = 0;
        Assert.False(RoundTrips.Due(link, link.StartedMs + 59_999, ref said));
        Assert.True(RoundTrips.Due(link, link.StartedMs + 60_000, ref said));
        Assert.False(RoundTrips.Due(link, link.StartedMs + 119_999, ref said));
        Assert.True(RoundTrips.Due(link, link.StartedMs + 120_000, ref said));
    }
}
