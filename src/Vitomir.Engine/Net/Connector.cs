// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The connector seam** (ours; "the door left open for hole punching or a relay later"). How a
// machine comes to have its peer's address — and how a host makes itself reachable — is one interface, so that a way that
// needs a server of somebody's (a rendezvous for hole punching, a relay) can be put in its place without the game, the
// session or the transport changing. Today's one way is the direct one: the player types the host's address (or the LAN
// finder's row gives it), and the host asks its router (UPnP, NAT-PMP, PCP; PortMapping).
using System.Net;

namespace Vitomir.Engine.Net;

/// <summary>
/// **How a machine reaches the other**. The host <see cref="Offer"/>s itself (its port made reachable, the line
/// that says where players join); a player <see cref="Find"/>s the host from what it was given (an address typed, a code
/// a later way brings) and the transport knocks on what it answers; <see cref="BeforeKnock"/> is the moment a way that
/// opens a path first (hole punching: both ends sending to each other's outside address at once) does so on the game's
/// own socket. A relay would answer its own address from <see cref="Find"/> and forward the sealed datagrams, which it
/// cannot read (<see cref="NetSeal"/>).
/// </summary>
public interface IConnector
{
    /// <summary>Its name, for the console's lines (the direct way: "direct").</summary>
    string Name { get; }

    /// <summary>The host's side: make UDP <paramref name="port"/> reachable from outside, saying what came of it on <paramref name="log"/>.</summary>
    Task Offer(int port, Action<string> log);

    /// <summary>A player's side: where to knock for <paramref name="given"/> (an address, <c>host[:port]</c>), or null when it cannot be made sense of.</summary>
    IPEndPoint? Find(string given, int defaultPort = NetPeer.DefaultPort);

    /// <summary>Just before the transport knocks on <paramref name="host"/> from <paramref name="peer"/>'s socket (the direct way does nothing).</summary>
    void BeforeKnock(NetPeer peer, IPEndPoint host);
}

/// <summary>
/// **The direct way**: the address as typed (names looked up), and the host's router asked to map the port
/// (<see cref="PortMapping.MapInBackground"/>); no server of ours or anybody's.
/// </summary>
public sealed class DirectConnector : IConnector
{
    public string Name => "direct";

    public Task Offer(int port, Action<string> log) => PortMapping.MapInBackground(port, log);

    public IPEndPoint? Find(string given, int defaultPort = NetPeer.DefaultPort) => NetPeer.ParseAddress(given, defaultPort);

    public void BeforeKnock(NetPeer peer, IPEndPoint host) { }
}

/// <summary>The way in force: the direct one, until another is built and chosen.</summary>
public static class Connectors
{
    /// <summary>The connector every host and player uses.</summary>
    public static IConnector Current { get; set; } = new DirectConnector();
}
