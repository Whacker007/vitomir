// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The LAN room's own talk — `mpChat` mode 1's left-hand list, before anybody is in a game. In 1.1 a line
// typed in `mpChatRoomEdit` goes to the chat's send, which in LAN mode makes it one
// `sendto` of the message, as a broadcast, on the lobby's one socket (UDP 4994, SO_BROADCAST);
// every machine in the room reads it off the same socket, and the lobby's pump (kind 8)
// shows it in the room's own list as `"<%s> %s"` in the list's own colour
//  — not the in-game lobby line's `"%s: %s"` in 0xffddddff.
// **Ours**: the room talks on its own socket one port above the game's (4995 for 4994), bound with
// address reuse, so that every lobby window on a machine hears the room and a game hosted on the
// machine keeps its own port to itself; the datagram's bytes are ours too.
//
// The room's players: 1.1's lobby pump broadcasts this player's name every
// 2 s (message 0x4e) on the same socket, and every machine in the room — this one too, hearing its own
// broadcast — puts it in the room's player list (found by the sender's address, else added at
// the end; the time heard kept), which `mpChatRoomStatus` shows as "UI_NET_PLAYER %d: %s" counted from
// 0. Once a second a player not heard for 8 s is forgotten, the rest closed up in order and
// the list redrawn. Here the presence goes on the room's own socket like its talk, and (ours) a player
// is known by a token its room drew rather than by its address, so that two lobbies on one machine —
// one address — are two players, and a machine heard over two networks is still one. The presence is
// also sent to this machine directly (ours), so a lobby is on its own list where the network does not
// loop a broadcast back; the token makes the two copies one.
using System.Net;
using System.Net.Sockets;

namespace Vitomir.Engine.Net;

/// <summary>The LAN room's talk: a line said is broadcast, and every line the room broadcasts is heard.</summary>
public sealed class LanRoom : IDisposable
{
    readonly Socket socket;
    readonly byte[] buffer = new byte[NetPeer.MaxMessage + 64];
    readonly int port;

    /// <summary>A line's longest, as the edit line takes it (120 characters, `NetChat.MaxLength`).</summary>
    public const int MaxLength = NetChat.MaxLength;

    /// <param name="gamePort">The game's port (4994); the room talks one above it (ours).</param>
    public LanRoom(int gamePort = NetPeer.DefaultPort)
    {
        port = gamePort + 1;
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;
        socket.Blocking = false;
        try { socket.Bind(new IPEndPoint(IPAddress.Any, port)); Listening = true; }
        catch (SocketException) { Listening = false; }
    }

    /// <summary>The clock the room's timers run on, in milliseconds; a test turns it by hand.</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    /// <summary>
    /// This player's name as the room is told it (the lobby's copy of the player's name); null says nothing — 1.1's session starts only once a character is chosen.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>How often this player says it is there (the next 2,000 ms after the last).</summary>
    public const int PresenceMs = 2000;
    /// <summary>How long a player is believed unheard (`seen + 8000 &lt; now`), and how often that is looked at (every 1,000 ms).</summary>
    public const int ForgetMs = 8000, SweepMs = 1000;
    /// <summary>The room's list holds at most 512 (no more are added at index 0x1ff and above).</summary>
    public const int MaxPlayers = 0x200;

    /// <summary>Ours: who this lobby is to the room (1.1 knows a player by its address alone, one lobby a machine).</summary>
    readonly int token = Random.Shared.Next(1, int.MaxValue);
    readonly List<(int Token, string Name, long Seen)> players = [];
    long nextPresence, nextSweep;

    /// <summary>The room's players, in the order first heard (added at the end; the gaps closed up in order).</summary>
    public IReadOnlyList<string> Players => [.. players.Select(p => p.Name)];

    /// <summary>Bumped whenever <see cref="Players"/> changes, so the list is redrawn only then (1.1 redraws on a change too).</summary>
    public int PlayersChanged { get; private set; }

    /// <summary>The room's list as `mpChatRoomStatus` shows it: `"%s %d: %s"` — `UI_NET_PLAYER`, the index from 0, the name.</summary>
    public static List<string> Rows(IReadOnlyList<string> names, Func<string, string> text)
    {
        var rows = new List<string>(names.Count);
        for (int i = 0; i < names.Count; i++) rows.Add($"{text("UI_NET_PLAYER")} {i}: {names[i]}");
        return rows;
    }

    /// <summary>This player's presence, when it is time (every 2 s, only with a name).</summary>
    void SayHere(long now)
    {
        if (Name is null || now < nextPresence) return;
        nextPresence = now + PresenceMs;
        var data = Presence(Name, token);
        foreach (var to in (IPAddress[])[IPAddress.Broadcast, IPAddress.Loopback])
            try { socket.SendTo(data, new IPEndPoint(to, port)); }
            catch (SocketException) { }
    }

    /// <summary>A player's presence heard: the one already listed (by its token, ours; 1.1's by its address) is refreshed, else added at the end.</summary>
    void Heard(int theirs, string name, long now)
    {
        int at = players.FindIndex(p => p.Token == theirs);
        if (at < 0)
        {
            if (players.Count >= MaxPlayers) return;
            players.Add((theirs, name, now));
            PlayersChanged++;
            return;
        }
        if (players[at].Name != name) PlayersChanged++;
        players[at] = (theirs, name, now);
    }

    /// <summary>Once a second, every player unheard for 8 s forgotten, the rest kept in order.</summary>
    void Sweep(long now)
    {
        if (now < nextSweep) return;
        nextSweep = now + SweepMs;
        if (players.RemoveAll(p => p.Seen + ForgetMs < now) > 0) PlayersChanged++;
    }

    /// <summary>Whether the room's port could be had; a machine that cannot bind it still speaks, but hears nothing.</summary>
    public bool Listening { get; }

    /// <summary>
    /// Says a line to the room: broadcast, so the speaker hears it too, as 1.1's own socket does. The line carries no
    /// name: the room knows its speaker by where it came from (<see cref="Hear"/>).
    /// </summary>
    public void Say(string text)
    {
        if (text.Length == 0) return;
        if (text.Length > MaxLength) text = text[..MaxLength];
        try { socket.SendTo(Saying(token, text), new IPEndPoint(IPAddress.Broadcast, port)); }
        catch (SocketException) { }
    }

    /// <summary>A player's presence as the room hears it: its name, at most 31 characters (32 bytes with the nul), and its token (ours).</summary>
    public static byte[] Presence(string name, int token) =>
        new NetWriter(Msg.RoomHere).I32(Protocol.Version).Str(name.Length > 31 ? name[..31] : name).I32(token).ToArray();

    /// <summary>A line said as the room hears it: who said it — the speaker's token, 1.1's the sender's address — and the text.</summary>
    public static byte[] Saying(int token, string text) =>
        new NetWriter(Msg.RoomSay).I32(Protocol.Version).I32(token).Str(text).ToArray();

    /// <summary>
    /// One datagram as the room reads it (the lobby's pump): a player's presence (<see cref="Presence"/>) or a
    /// line (<see cref="Saying"/>). The line's sender is found in the room's players — 1.1 by its address,
    /// Vitomir by its token — and the line shown as `<name> text` with the
    /// name **from the list**; a line from a sender not on the list is dropped. So a lobby's first line before its presence is heard (every 2 s) is lost, as in 1.1, and so is one
    /// from a player forgotten after 8 s unheard. Anything else, or of another protocol, is ignored.
    /// </summary>
    public void Hear(byte[] datagram, long now, List<string> heard)
    {
        try
        {
            var r = new NetReader(datagram);
            if (r.I32() != Protocol.Version) return;
            if (r.Kind == Msg.RoomSay)
            {
                int theirs = r.I32();
                string text = r.Str();
                int at = players.FindIndex(p => p.Token == theirs);
                if (at >= 0) heard.Add(Line(players[at].Name, text));
            }
            else if (r.Kind == Msg.RoomHere)
            {
                string name = r.Str();
                Heard(r.I32(), name, now);
            }
        }
        catch (EndOfStreamException) { }
    }

    /// <summary>
    /// One turn of the room: this player's presence when it is due, every datagram read — a
    /// line said, or a player there — and the stale players forgotten. Returns every
    /// line heard since the last call, each as the room's list shows it (<see cref="Line"/>).
    /// </summary>
    public List<string> Poll()
    {
        long now = Clock();
        SayHere(now);
        var heard = new List<string>();
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (Listening)
        {
            int n;
            try { n = socket.ReceiveFrom(buffer, ref from); }
            catch (SocketException) { break; }
            Hear(buffer[..n], now, heard);
        }
        Sweep(now);
        return heard;
    }

    /// <summary>
    /// A line of the room's talk as the room's list shows it: `<name> text` (`"<%s> %s"`), the
    /// name the room's player list has for the sender (<see cref="Hear"/>).
    /// </summary>
    public static string Line(string name, string text) => $"<{name}> {text}";

    public void Dispose() => socket.Dispose();
}
