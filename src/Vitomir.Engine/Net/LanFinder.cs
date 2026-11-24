// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Finding the games on the LAN: 1.1's LAN lobby lists them (`mpChat` mode 1, the rows `"%s (%d)"`) by broadcasting on its one socket, UDP 4994, with SO_BROADCAST on. The
// query and its answer here are ours; the port and the broadcast are 1.1's. 1.1's own pump
// every 2 s also broadcasts this player's own presence (message 0x4e) and reads it back into the
// room's player list; Vitomir's room list is the room's (`LanRoom`) — this
// class answers only the games query and answer (0x4d).
using System.Net;

namespace Vitomir.Engine.Net;

/// <summary>A game found on the LAN, and where it answered from.</summary>
public sealed record FoundGame(GameInfo Info, IPEndPoint Host);

/// <summary>Asks the LAN who is hosting, now and then, and keeps the answers.</summary>
public sealed class LanFinder : IDisposable
{
    readonly NetPeer peer;
    readonly int port;
    readonly Dictionary<string, (FoundGame Game, long Seen)> found = [];
    long nextQuery, nextSweep;

    /// <summary>How often the LAN is asked (ours; 1.1's host announces every 2 s).</summary>
    public const int QueryMs = 2000;
    /// <summary>How long a game is believed unheard (`seen + 8000 &lt; now`), looked at once a second.</summary>
    public const int ForgetMs = 8000, SweepMs = 1000;

    /// <summary>The clock the finder's timers run on, in milliseconds; a test turns it by hand.</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    public LanFinder(int port = NetPeer.DefaultPort, NetPeer? peer = null)
    {
        this.port = port;
        this.peer = peer ?? new NetPeer();
    }

    /// <summary>
    /// Also asked directly, beside the broadcast: this machine itself, so a host and a player on one
    /// laptop find each other even where the network will not loop a broadcast back (ours).
    /// </summary>
    public List<IPEndPoint> AlsoAsk { get; } = [];

    /// <summary>The games heard from lately, newest answer kept.</summary>
    public IReadOnlyList<FoundGame> Games => [.. found.Values.Select(v => v.Game).OrderBy(g => g.Info.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>One turn: a query when it is time, and every answer read.</summary>
    public void Poll()
    {
        long now = Clock();
        if (now >= nextQuery)
        {
            nextQuery = now + QueryMs;
            var q = new NetWriter(Msg.Query).I32(Protocol.Version).ToArray();
            peer.SendDatagram(new IPEndPoint(IPAddress.Broadcast, port), q);
            peer.SendDatagram(new IPEndPoint(IPAddress.Loopback, port), q);
            foreach (var extra in AlsoAsk) peer.SendDatagram(extra, q);
        }
        foreach (var e in peer.Poll())
        {
            if (e.Kind != NetEventKind.Datagram || e.From is null || GameInfo.Read(e.Data) is not { } info) continue;
            var host = new IPEndPoint(e.From.Address, info.Port);
            // One game answers the broadcast and the direct query both, from two addresses: it is
            // kept once, by what it says it is, at the first address it answered from. 1.1's own
            // announcement handler adds or refreshes the game the same way and redraws
            // the games list "%s (%d)" on the spot; here the list is redrawn from `Games` instead.
            string key = $"{info.Name}|{info.Chronicle}|{info.Port}";
            var at = found.TryGetValue(key, out var seen) ? seen.Game.Host : host;
            found[key] = (new FoundGame(info, at), now);
        }
        // 1.1 forgets, once a second, a game gone quiet for 8 s, and deselects it
        // if it was the chosen one; here it is dropped from `found`, and the lobby's chosen row follows
        // the list (`MpMenus`).
        if (now < nextSweep) return;
        nextSweep = now + SweepMs;
        foreach (var stale in found.Where(kv => kv.Value.Seen + ForgetMs < now).Select(kv => kv.Key).ToList())
            found.Remove(stale);
    }

    public void Dispose() => peer.Dispose();
}
