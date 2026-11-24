// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What the sealed session costs** (item 4: "measure the cost per datagram and per frame and report it"): a timing test
/// (left out of the plain run, run by the timing command), each figure the best of five runs. Per datagram: sealing and
/// opening one (AES-GCM, the header authenticated) of a small order's size and of a full snapshot's. Per join: the key exchange
/// (ECDH P-256, both halves) and the password's proof (PBKDF2-SHA256, <see cref="PasswordProof.Iterations"/> rounds, made at
/// each end). Per frame: a host's turn with four players, each sent and sending two datagrams a frame (the network pair's tapes
/// read about two a frame each way), sealed and opened.
/// </summary>
public sealed class NetSealCostTests(ITestOutputHelper output)
{
    const int Runs = 5;

    static double Best(Func<double> run)
    {
        double best = double.MaxValue;
        for (int r = 0; r < Runs; r++) best = Math.Min(best, run());
        return best;
    }

    static (NetSeal Player, NetSeal Host) Pair()
    {
        var player = new NetSeal(connecting: true);
        var host = new NetSeal(connecting: false);
        Assert.True(host.Join(player.Half, 0x1234));
        Assert.True(player.Join(host.Half, 0x1234));
        return (player, host);
    }

    static double PerDatagram(NetSeal from, NetSeal to, int body, int n)
    {
        var plain = new byte[7 + body];
        plain[0] = NetPeer.Magic;
        plain[1] = NetPeer.Version;
        plain[2] = 4;
        Random.Shared.NextBytes(plain.AsSpan(7));
        var sealedPacket = new byte[plain.Length + NetSeal.Overhead];
        var opened = new byte[plain.Length];
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            int length = from.Seal(plain, sealedPacket, 7);
            if (to.Open(sealedPacket.AsSpan(0, length), opened, out _) != plain.Length) throw new InvalidOperationException("a datagram did not open");
        }
        return clock.Elapsed.TotalMicroseconds / n;
    }

    [Fact, Trait("Category", "Timing")]
    public void Sealing_costs_microseconds_a_datagram_and_a_join_tens_of_milliseconds()
    {
        var (player, host) = Pair();
        using var a = player;
        using var b = host;
        PerDatagram(player, host, 1200, 2000);
        double small = Best(() => PerDatagram(player, host, 60, 20000));
        double large = Best(() => PerDatagram(host, player, 1200, 20000));
        double frame = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int f = 0; f < 300; f++)
            {
                PerDatagram(host, player, 600, 8);
                PerDatagram(player, host, 60, 8);
            }
            return clock.Elapsed.TotalMicroseconds / 300;
        });
        double exchange = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                var (p, h) = Pair();
                p.Dispose();
                h.Dispose();
            }
            return clock.Elapsed.TotalMilliseconds / 20;
        });
        double proof = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            PasswordProof.Make("a password", new byte[32]);
            return clock.Elapsed.TotalMilliseconds;
        });
        output.WriteLine($"the sealed session (each the best of {Runs} runs): a datagram sealed and opened, a 60-byte order {small:0.00} µs, " +
                         $"a 1200-byte snapshot {large:0.00} µs; a host's frame with four players (8 datagrams of 600 bytes out, 8 of 60 in, each sealed and opened) {frame:0.0} µs; " +
                         $"a join's key exchange (both halves) {exchange:0.00} ms; a password's proof {proof:0.0} ms at each end; the wire's overhead {NetSeal.Overhead} bytes a datagram");
        Assert.True(small < 50, $"{small:0.00} µs");
        Assert.True(large < 200, $"{large:0.00} µs");
        Assert.True(frame < 2000, $"{frame:0.0} µs");
    }
}
