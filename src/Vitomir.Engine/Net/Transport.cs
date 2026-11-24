// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The wire under multiplayer: one UDP socket, and on top of it a thin reliable, ordered channel and
// an unreliable one. **All of this is ours**.
//
// 1.1 carried its game over DirectPlay (an `IDirectPlay4` object made with
// `CoCreateInstance`; a host joined by IP), and its LAN lobby over the one plain WSOCK32
// socket it opens, on **UDP 4994** with broadcast on. DirectPlay is long gone, so this
// file is the replacement for both: the same port, the same one socket, a broadcast query for the
// LAN, and the delivery guarantees DirectPlay gave for free written out by hand.
//
// It is internet-grade: every connection is sealed (NetSeal: an ECDH exchange in the
// knock and its answer, AES-GCM on every datagram after), everything a remote sends is read within bounds and what does
// not read is dropped and counted (Stats), a peer that keeps sending nonsense is closed, knocks are limited per address,
// nothing a remote sends makes this end hold more than a bound, and the wire's version is 2 (an older build's knock is
// answered in its own version 1 with 1.1's UI_NET_BADPROTOCOL). The network-conditions simulator (NetSim) sits between
// the socket and all of this, both ways.
//
// Nothing here runs on another thread but the keep-alive (KeepAlive, a heartbeat while this end is busy loading,
// under the one lock everything here takes). `Poll` reads whatever has arrived and writes whatever is due, and it is
// called from the game's own loop — so the simulation never sees a message arrive in the middle of a step, which is the
// rule for everything off the sim thread.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Vitomir.Engine.Net;

/// <summary>Which channel a message goes on.</summary>
public enum Channel : byte
{
    /// <summary>Superseded by the next one: positions, headings, animations. May be lost or reordered.</summary>
    Unreliable = 0,
    /// <summary>Arrives, once, in the order sent: joins, chat, things made and removed, orders.</summary>
    Reliable = 1,
}

/// <summary>One end of a connection as the transport keeps it.</summary>
public sealed partial class Connection
{
    internal Connection(IPEndPoint remote, uint token, bool outgoing, long now)
    {
        Remote = remote;
        Token = token;
        Outgoing = outgoing;
        LastReceived = now;
        Started = now;
        Seal = new NetSeal(outgoing);
    }

    /// <summary>Where the other end is.</summary>
    public IPEndPoint Remote { get; }
    /// <summary>The number the connecting side chose, which names this connection in every packet.</summary>
    public uint Token { get; }
    /// <summary>Whether this end asked for the connection (a client) rather than accepted it.</summary>
    public bool Outgoing { get; }
    /// <summary>Whether the handshake has finished.</summary>
    public bool Open { get; internal set; }
    /// <summary>Whether it has gone, and why.</summary>
    public bool Closed { get; internal set; }
    public string? Reason { get; internal set; }
    /// <summary>The smoothed round trip, in milliseconds (ours: the usual 7/8 average).</summary>
    public float RoundTripMs { get; internal set; } = 100f;

    /// <summary>
    /// **The round trips measured on it** (players play from far away): how many (each a reliable message sent once and
    /// acknowledged, Karn's rule), and the least and the most of them, since the connection began; 0 when none yet. The smoothed figure
    /// starts at 100 ms before the first, so these are what the wire did.
    /// </summary>
    public int RoundTripsMeasured { get; private set; }
    public float RoundTripLeastMs { get; private set; }
    public float RoundTripMostMs { get; private set; }

    /// <summary>
    /// **The resend's back-off** (Karn's algorithm, RFC 6298 section 5.5): 1, or 2, 4, 8 after resends that found no answer,
    /// kept for the messages sent after them until a round trip is measured again. Without it a wire whose round trip is longer than
    /// twice the first guess (100 ms, so 200 ms and more: a player on another continent) had every reliable message sent twice, so none
    /// was ever measured (Karn's rule) and the guess never grew.
    /// </summary>
    internal int Backoff = 1;

    /// <summary>The most the back-off grows to.</summary>
    internal const int MostBackoff = 8;

    /// <summary>One round trip measured (milliseconds), into the smoothed figure and the least and the most.</summary>
    internal void Measured(float ms)
    {
        Backoff = 1;
        // The first measured is taken whole (RFC 6298 section 2.2), the rest averaged in by an eighth: the first guess (100 ms)
        // is a guess, and on a far wire an eighth at a time took a dozen messages to leave it.
        RoundTripMs = RoundTripsMeasured == 0 ? ms : RoundTripMs * 0.875f + ms * 0.125f;
        RoundTripLeastMs = RoundTripsMeasured == 0 ? ms : Math.Min(RoundTripLeastMs, ms);
        RoundTripMostMs = RoundTripsMeasured == 0 ? ms : Math.Max(RoundTripMostMs, ms);
        RoundTripsMeasured++;
    }

    /// <summary>When it began, on its transport's clock (milliseconds).</summary>
    public long StartedMs => Started;
    /// <summary>Whatever the session layer wants to hang on it (the player's slot).</summary>
    public object? Tag { get; set; }

    /// <summary>The connection's key exchange and seals (<see cref="NetSeal"/>).</summary>
    internal NetSeal Seal { get; }

    /// <summary>
    /// What names this session (<see cref="NetSeal.Binding"/>): the same at both ends once the handshake is done, the
    /// password's proof made over it (<see cref="PasswordProof"/>). Empty before.
    /// </summary>
    public byte[] Binding => Seal.Binding;

    internal long Started, LastReceived, LastSent, LastConnectSent;
    internal uint SendNext;                       // the next reliable sequence number to use
    internal uint RecvNext;                       // everything below this has been delivered
    internal readonly SortedDictionary<uint, Queued> Pending = [];
    internal readonly Dictionary<uint, (byte[] Data, bool More)> Early = [];   // a piece's flag kept with it
    internal int EarlyBytes;                      // what Early holds, kept under NetPeer.MaxEarlyBytes
    internal readonly Queue<byte[]> Unreliable = new();
    internal bool AckOwed;
    internal byte[]? Knock;                       // this end's knock (a player) or answer (the host), sent again as it is
    internal byte[] KnockHalf = [];               // the host's: the half the knock carried, so a knock again is known
    internal long NonsenseSince;                  // when the nonsense counted began
    internal int Nonsense;                        // how much in that window
    internal string? CloseReason;                 // closed once what it was sent is acknowledged (DisconnectWhenDelivered)
    internal long CloseBy;                        // or at this time, whatever came back

    internal sealed class Queued(byte[] data, bool more = false)
    {
        public readonly byte[] Data = data;
        public readonly bool More = more;          // a piece of a longer message, more following (Transport.Pieces.cs)
        public long SentAt;
        public int Sends;
    }

    /// <summary>How many reliable messages are waiting for their acknowledgement.</summary>
    public int Unacknowledged => Pending.Count;

    public override string ToString() => $"{Remote} ({(Open ? "open" : Closed ? "closed" : "connecting")})";
}

/// <summary>What <see cref="NetPeer.Poll"/> reports.</summary>
public readonly record struct NetEvent(NetEventKind Kind, Connection? Connection, byte[] Data, IPEndPoint? From = null)
{
    public string? Reason => Connection?.Reason;
}

public enum NetEventKind
{
    /// <summary>A connection finished its handshake.</summary>
    Connected,
    /// <summary>A connection went: a goodbye, a timeout, or a refusal.</summary>
    Disconnected,
    /// <summary>A message arrived on a connection.</summary>
    Message,
    /// <summary>A datagram from outside any connection: a LAN query, or the answer to one.</summary>
    Datagram,
}

/// <summary>
/// What a transport has had and dropped (ours): every datagram read and sent, and each kind of thing a remote sent
/// that was not taken — the tests' and the trace's counts, never a reason to throw.
/// </summary>
public sealed class NetStats
{
    public long DatagramsIn, DatagramsOut, BytesIn, BytesOut;
    /// <summary>Too short, not ours, a kind not known, a body that does not read: dropped.</summary>
    public long Junk;
    /// <summary>A sealed datagram that did not open (no such connection, a wrong key, tampered) or a plain one where only a sealed one may be.</summary>
    public long Unsealed;
    /// <summary>A sealed datagram whose packet number was taken before (sent again by somebody on the path).</summary>
    public long Replayed;
    /// <summary>Knocks and datagrams past an address's limit, and knocks past the connections' bound.</summary>
    public long Limited;
    /// <summary>Records of an opened datagram that do not read, or that would hold more than a bound.</summary>
    public long BadRecords;
    /// <summary>Messages the session layer could not read (<see cref="NetPeer.Nonsense"/>).</summary>
    public long BadMessages;
    /// <summary>Knocks of an older Vitomir answered with 1.1's UI_NET_BADPROTOCOL (the wire's version 1).</summary>
    public long OldWire;
    /// <summary>Connections closed for nonsense.</summary>
    public long ClosedForNonsense;

    public override string ToString() =>
        $"{DatagramsIn} in ({BytesIn:N0} B), {DatagramsOut} out ({BytesOut:N0} B); dropped: {Junk} junk, {Unsealed} unsealed, {Replayed} replayed, " +
        $"{Limited} past a limit, {BadRecords} bad records, {BadMessages} bad messages; {OldWire} older builds refused, {ClosedForNonsense} closed for nonsense";
}

/// <summary>
/// One UDP socket and every connection over it — the host's with its clients, or a client's with
/// the host. **Ours** throughout; see the file's header for what it stands in for.
/// </summary>
public sealed partial class NetPeer : IDisposable
{
    /// <summary>1.1's own port (`htons(0x1382)`), kept for continuity.</summary>
    public const int DefaultPort = 4994;
    /// <summary>
    /// The first two bytes of every datagram of ours, and the wire's version: 2, the sealed session (1 before;
    /// an older build's knock is answered in 1 with UI_NET_BADPROTOCOL, <see cref="OldWire"/>).
    /// </summary>
    public const byte Magic = 0x56, Version = 2;
    /// <summary>The wire's version earlier, whose knocks are answered with the refusal (<see cref="OldWire"/>).</summary>
    public const byte OldVersion = 1;
    /// <summary>
    /// The largest datagram sent: 1,216 bytes (1, the seal adds <see cref="NetSeal.Overhead"/>), under IPv6's
    /// smallest path (1,280 less 48 bytes of headers) and so under any Ethernet path, a tunnel's (an overlay network's) among them.
    /// </summary>
    public const int MaxDatagram = 1216;
    /// <summary>The largest plain packet: what sealing makes a datagram of at most <see cref="MaxDatagram"/>.</summary>
    const int MaxPlain = MaxDatagram - NetSeal.Overhead;
    /// <summary>
    /// The largest one unreliable message may be, and one piece of a reliable one (a longer reliable message
    /// goes in pieces, up to <see cref="MaxReliableMessage"/>): 1,168 bytes, as it was before the seal. 1.1's own limit is
    /// 255 bytes (its send's assert).
    /// </summary>
    public const int MaxMessage = 1168;
    /// <summary>Silence after which a connection is given up (ours).</summary>
    public const int TimeoutMs = 10_000;
    /// <summary>How long a connection attempt keeps knocking (ours).</summary>
    public const int ConnectTimeoutMs = 5_000;
    /// <summary>The longest either end goes without sending something, so the other knows it is alive (ours).</summary>
    public const int HeartbeatMs = 250;

    /// <summary>
    /// The most reliable messages held for a gap before them (ours): a record numbered further past the next one
    /// expected is not taken (nor acknowledged; it comes again). The window (64 KB) is a few hundred messages at most.
    /// </summary>
    public const int MaxEarly = 4096;
    /// <summary>The most bytes held for a gap (ours): 2 MiB, two of the largest reliable messages.</summary>
    public const int MaxEarlyBytes = 2 << 20;
    /// <summary>The most connections a host keeps, open or knocking (ours; eight seats).</summary>
    public const int MaxConnections = 32;
    /// <summary>New connections one address may make in <see cref="LimitWindowMs"/> (ours).</summary>
    public const int KnocksPerAddress = 6;
    /// <summary>Datagrams outside a connection (a LAN query, a knock of an older build) one address may send in <see cref="LimitWindowMs"/> (ours).</summary>
    public const int DatagramsPerAddress = 40;
    /// <summary>The window the per-address limits count in (ours).</summary>
    public const int LimitWindowMs = 10_000;
    /// <summary>Nonsense a connection may send in <see cref="NonsenseWindowMs"/> before it is closed (ours; "tolerate brief nonsense").</summary>
    public const int NonsenseLimit = 20;
    /// <summary>The window nonsense counts in (ours).</summary>
    public const int NonsenseWindowMs = 10_000;
    /// <summary>Why a connection was closed for nonsense (both ends' <see cref="NetEventKind.Disconnected"/> reason).</summary>
    public const string NonsenseReason = "nonsense";

    // The wire's kinds; a sealed one opens to the kind it carries with Opened set (never on the wire itself).
    enum Packet : byte { Connect = 1, Accept = 2, Refuse = 3, Data = 4, Bye = 5, Datagram = 6, Sealed = 7 }
    const byte Opened = 0x80;

    readonly Socket socket;
    readonly Dictionary<(IPEndPoint, uint), Connection> connections = [];
    readonly List<NetEvent> events = [];
    readonly Func<long> clock;

    /// <summary>
    /// The transport's clock as its last <see cref="Poll"/> read it (what the round trips' lines are timed by; reading it again
    /// would put a clock reading on a tape that the replay must find there).
    /// </summary>
    public long LastPolledMs { get; private set; }
    readonly byte[] receiveBuffer = new byte[65536];
    readonly byte[] rawBuffer = new byte[65536];
    readonly Random random;
    readonly object gate = new();
    readonly Dictionary<IPAddress, (long Since, int Knocks, int Datagrams, bool Told)> limits = [];

    /// <summary>
    /// Opens the socket. <paramref name="port"/> 0 takes any free port (a client); a host binds its
    /// game port. Dual-stack where the machine has IPv6, IPv4 alone where it does not. The
    /// network-conditions simulator in force (<see cref="NetSim.Default"/>) is this transport's from the start, unless it is given
    /// a <paramref name="clock"/> of its own.
    /// </summary>
    public NetPeer(int port = 0, Func<long>? clock = null, int? seed = null)
    {
        var own = clock ?? (() => Environment.TickCount64);
        this.clock = () => NetTape.Clock(own());                     // a replay's tape, when one is in
        random = seed is { } s ? new Random(s) : NetTape.Generator();
        // A transport on a clock of its own (a test of the transport's own timers) takes no simulator from the environment.
        Sim = clock is null ? NetSim.ForNewPeer() : null;
        Socket? made = null;
        try
        {
            made = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
            Exclusive(made);
            made.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }
        catch (SocketException first)
        {
            made?.Dispose();
            made = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Exclusive(made);
            try { made.Bind(new IPEndPoint(IPAddress.Any, port)); }
            catch (SocketException e)
            {
                made.Dispose();
                // A port another program holds is said plainly, not thrown as 10013.
                if (port != 0 && (PortTakenException.Taken(e) || PortTakenException.Taken(first))) throw new PortTakenException(port, e);
                throw;
            }
        }
        socket = made;
        socket.Blocking = false;
        try { socket.EnableBroadcast = true; } catch (SocketException) { }
        // Windows reports an ICMP "port unreachable" as a reset on the next receive, which would
        // throw out of a perfectly good server loop because one client went away.
        if (OperatingSystem.IsWindows())
            try { socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null); } catch (SocketException) { }
    }

    /// <summary>
    /// **A game socket keeps its port to itself** (ours, found in a Windows run). On Windows
    /// a socket bound with SO_REUSEADDR takes over a port another socket already holds, and the LAN
    /// room binds that way so that two games on one machine can share it (`LanRoom`). Windows also
    /// hands out free ports in order, so the room — the game's port plus one — could land on the port
    /// a LAN finder had just been given and swallow every answer meant for it. SO_EXCLUSIVEADDRUSE
    /// stops that: the room then cannot bind and goes deaf, and the game's traffic is untouched.
    /// Other systems do not let a reusing socket take a port its holder did not share.
    /// </summary>
    static void Exclusive(Socket socket)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { socket.ExclusiveAddressUse = true; } catch (SocketException) { }
    }

    /// <summary>The port the socket is bound to.</summary>
    public int Port => ((IPEndPoint)socket.LocalEndPoint!).Port;

    /// <summary>
    /// A share of outgoing datagrams to throw away, for tests of the reliable channel (ours; 0 in
    /// play).
    /// </summary>
    public double DropRate { get; set; }

    /// <summary>The network-conditions simulator between this transport and its socket (<see cref="NetSim"/>), or null.</summary>
    public NetSim? Sim { get; set; }

    /// <summary>What this transport has had and dropped.</summary>
    public NetStats Stats { get; } = new();

    /// <summary>The transport's own lines: an address past its limit, an older build refused, a peer closed for nonsense.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>A line of the transport's own, on its log and on the network trace (<see cref="NetTrace"/>).</summary>
    void Note(string line)
    {
        Log?.Invoke(line);
        NetTrace.Line(line);
    }
    long lastTrace = long.MinValue;

    /// <summary>Whether this end accepts connections (a host).</summary>
    public bool Listening { get; set; }

    /// <summary>Nonsense a connection may send in <see cref="NonsenseWindowMs"/> before it is closed (<see cref="NonsenseLimit"/>, a test's own).</summary>
    public int NonsenseAllowed { get; set; } = NonsenseLimit;

    /// <summary>New connections one address may make in <see cref="LimitWindowMs"/> (<see cref="KnocksPerAddress"/>, a test's own).</summary>
    public int KnocksAllowed { get; set; } = KnocksPerAddress;

    /// <summary>Every live connection.</summary>
    public IEnumerable<Connection> Connections
    {
        get { lock (gate) return [.. connections.Values.Where(c => !c.Closed)]; }
    }

    /// <summary>
    /// Starts connecting to a host; the answer comes back through <see cref="Poll"/>. The knock carries this end's half of
    /// the key exchange (<see cref="NetSeal"/>), the same each time it is sent.
    /// </summary>
    public Connection Connect(IPEndPoint host)
    {
        lock (gate)
        {
            var remote = Normalize(host);
            uint token = (uint)random.Next(1, int.MaxValue);
            var c = new Connection(remote, token, outgoing: true, clock());
            var knock = new byte[7 + NetSeal.HalfLength];
            Header(knock, Packet.Connect);
            BinaryPrimitives.WriteUInt32LittleEndian(knock.AsSpan(3), token);
            c.Seal.Half.CopyTo(knock, 7);
            c.Knock = knock;
            connections[(remote, token)] = c;
            SendConnect(c);
            return c;
        }
    }

    /// <summary>
    /// Queues a message on a connection. Reliable ones are numbered here, a long one in pieces
    /// (<see cref="QueueReliable"/>).
    /// </summary>
    public void Send(Connection c, Channel channel, byte[] message)
    {
        lock (gate)
        {
            if (c.Closed) return;
            if (channel == Channel.Reliable) { QueueReliable(c, message); return; }
            if (message.Length > MaxMessage) throw new ArgumentException($"a {message.Length}-byte message is over the {MaxMessage}-byte limit");
            c.Unreliable.Enqueue(message);
        }
    }

    /// <summary>
    /// Sends what is queued on every open connection now, reading nothing (ours) — for a host about to be
    /// busy a while, as while it loads a place with the load screen up on the players' machines.
    /// </summary>
    public void Flush()
    {
        lock (gate)
        {
            long now = clock();
            foreach (var c in connections.Values) if (c.Open && !c.Closed) Flush(c, now);
            Pump();
            // Under the simulator: what it holds on its way out goes now, as a real wire would carry it while this end
            // is busy and not turning; the far end's own simulator, if any, still holds it back on its way in.
            if (Sim is not null) while (Sim.Held > 0 && Sim.DueOutNow(out var data, out var to)) SendTo(to, data);
        }
    }

    /// <summary>Sends a datagram outside any connection — a LAN query or its answer (plain).</summary>
    public void SendDatagram(IPEndPoint to, byte[] payload)
    {
        lock (gate)
        {
            var packet = new byte[3 + payload.Length];
            Header(packet, Packet.Datagram);
            payload.CopyTo(packet, 3);
            Raw(Normalize(to), packet);
            Pump();
        }
    }

    /// <summary>Says goodbye (three times, for luck) and forgets the connection.</summary>
    /// <summary>
    /// **A goodbye that arrives first** (found under the simulator: a kick's reason lost or overtaken by the goodbye, the
    /// kicked player told only "disconnected"): the connection is closed (<see cref="Disconnect"/>) once every reliable message sent
    /// on it is acknowledged, or after <paramref name="withinMs"/> whatever came back; until then it is kept and read as before.
    /// </summary>
    public void DisconnectWhenDelivered(Connection c, string reason, int withinMs = 2000)
    {
        lock (gate)
        {
            if (c.Closed) return;
            c.CloseReason = reason;
            c.CloseBy = clock() + withinMs;
            Flush(c, clock());
            Pump();
        }
    }

    public void Disconnect(Connection c, string reason = "bye")
    {
        lock (gate)
        {
            if (c.Closed) return;
            var bye = new byte[7];
            Header(bye, Packet.Bye);
            BinaryPrimitives.WriteUInt32LittleEndian(bye.AsSpan(3), c.Token);
            for (int i = 0; i < 3; i++) Sealed(c, bye);
            Close(c, reason, report: false);
            Pump();
        }
    }

    /// <summary>
    /// One turn of the wire: everything that has arrived read and dispatched, every resend and
    /// heartbeat that is due written, and every connection that has gone quiet closed. Returns what
    /// happened, in the order it happened.
    /// </summary>
    public IReadOnlyList<NetEvent> Poll()
    {
        lock (gate)
        {
            Sim?.Turn();
            // What happened between two turns (a connection closed for nonsense the session layer reported) is
            // this turn's too: the list is emptied as it is handed out, not as a turn begins.
            long now = clock();
            LastPolledMs = now;
            Receive(now);
            foreach (var c in connections.Values.ToList())
            {
                if (c.Closed) continue;
                if (!c.Open && c.Outgoing)
                {
                    if (now - c.Started > ConnectTimeoutMs) { Close(c, "UI_NET_BADADDR", report: true); continue; }
                    if (now - c.LastConnectSent >= HeartbeatMs) SendConnect(c);
                    continue;
                }
                if (now - c.LastReceived > TimeoutMs) { Close(c, "timeout", report: true); continue; }
                if (c.CloseReason is { } going && (c.Pending.Count == 0 || now >= c.CloseBy)) { Disconnect(c, going); continue; }
                Flush(c, now);
            }
            foreach (var key in connections.Where(kv => kv.Value.Closed).Select(kv => kv.Key).ToList())
            {
                connections[key].Seal.Dispose();
                connections.Remove(key);
            }
            Pump();
            NetEvent[] happened = [.. events];
            events.Clear();
            // The trace: each connection made and gone, and every ten seconds each one's round trip and the counts.
            if (NetTrace.Folder is not null)
            {
                foreach (var e in happened)
                    if (e.Kind is NetEventKind.Connected or NetEventKind.Disconnected)
                        NetTrace.Line($"port {Port}: {e.Connection!.Remote} {(e.Kind == NetEventKind.Connected ? "connected (sealed)" : "gone: " + e.Reason)}");
                if (now - lastTrace >= 10_000)
                {
                    lastTrace = now;
                    NetTrace.Line($"port {Port}: {Stats}{(Sim is { } sim ? $"; simulated {sim.Settings}: {sim.Lost} lost, {sim.Doubled} doubled, {sim.Overtaken} overtaken" : "")}");
                    foreach (var c in connections.Values.Where(c => !c.Closed))
                        NetTrace.Line($"port {Port}:   {c}: round trip {c.RoundTripMs:0} ms, {c.Unacknowledged} unacknowledged, {c.InFlight} bytes in flight");
                }
            }
            return happened;
        }
    }

    /// <summary>
    /// **A heartbeat while this end is busy** (ours; a finding of the hosted runs): an empty data packet on every open
    /// connection — its acknowledgements as they stand — so the far end hears this one while it loads a place and does
    /// not give it up after <see cref="TimeoutMs"/>. Reads no clock and no datagram (a tape's order is the game loop's),
    /// moves no timer of the connection's, and sends nothing new on the wire (the heartbeat <see cref="Flush(Connection, long)"/>
    /// sends after <see cref="HeartbeatMs"/> of silence). Safe from another thread (<see cref="KeepAliveWhile"/>).
    /// </summary>
    public void KeepAlive()
    {
        lock (gate)
        {
            foreach (var c in connections.Values)
            {
                if (!c.Open || c.Closed) continue;
                var packet = new byte[15];
                StartData(c, packet);
                Sealed(c, packet);
            }
            Pump();
            // The simulator's held datagrams on their way in are taken off the socket as they arrive, so its delay is
            // counted from then and not from the end of the load; not under a tape being replayed, whose datagrams are the tape's.
            // A recording too. Earlier, a tape of either kind kept them on the socket, so a recording player's first turn
            // after a load longer than the timeout put every datagram of the load into the simulator at once, read none of them
            // (each held its way's delay from then), found the host silent since before the load and gave it up (ClassicNetworkTests'
            // dedicated host under VITOMIR_NET_SIM, whose player records: "UI_NET_DISCONNECTED" right after the load).
            if (Sim is not null && NetTape.Tape is not { Replaying: true }) DrainIntoSim();
        }
    }

    /// <summary>
    /// <see cref="KeepAlive"/> every <see cref="HeartbeatMs"/> on a thread of its own until the answer is disposed: a
    /// player's machine from the host's answer to its Start until its place is loaded (<see cref="GameClient"/>).
    /// </summary>
    public IDisposable KeepAliveWhile()
    {
        var stop = new CancellationTokenSource();
        var thread = new Thread(() =>
        {
            while (!stop.Token.WaitHandle.WaitOne(HeartbeatMs))
            {
                try { KeepAlive(); }
                catch (ObjectDisposedException) { return; }
            }
        }) { IsBackground = true, Name = "net keep-alive" };
        thread.Start();
        return new Stopper(stop, thread);
    }

    sealed class Stopper(CancellationTokenSource stop, Thread thread) : IDisposable
    {
        public void Dispose()
        {
            if (stop.IsCancellationRequested) return;
            stop.Cancel();
            thread.Join(1000);
            stop.Dispose();
        }
    }

    /// <summary>
    /// **The session layer could not read a message**: counted, and the connection closed when it keeps sending
    /// such — <see cref="NonsenseLimit"/> in <see cref="NonsenseWindowMs"/> ("tolerate brief nonsense") — the far end
    /// told <see cref="NonsenseReason"/> and a line on the transport's <see cref="Log"/>.
    /// </summary>
    /// <returns>Whether it was the first in its window (the caller's line said once, not twenty times).</returns>
    public bool Nonsense(Connection c, string what)
    {
        lock (gate)
        {
            Stats.BadMessages++;
            return Counted(c, clock(), what);
        }
    }

    bool Counted(Connection c, long now, string what)
    {
        if (c.Closed) return false;
        if (now - c.NonsenseSince > NonsenseWindowMs || c.Nonsense == 0) { c.NonsenseSince = now; c.Nonsense = 0; }
        if (++c.Nonsense <= NonsenseAllowed) return c.Nonsense == 1;
        Stats.ClosedForNonsense++;
        Note($"net: closed the connection from {c.Remote}: it kept sending what does not read ({c.Nonsense} in {NonsenseWindowMs / 1000} s, the last {what})");
        Refuse(c, NonsenseReason);
        return false;
    }

    /// <summary>
    /// One datagram from the socket, or false when none waits. **Not by <c>Socket.Available</c>** (the fuzz's
    /// finding): it is the size of the next datagram, 0 for an empty one, so one empty datagram from anybody left every
    /// later one unread and the game deaf until it timed out. The socket does not block: an empty queue is WouldBlock.
    /// </summary>
    bool SocketRead(byte[] buffer, out int length, out EndPoint from)
    {
        from = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        length = 0;
        while (true)
        {
            try
            {
                length = socket.ReceiveFrom(buffer, ref from);
                return true;
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize)
            {
                if (e.SocketErrorCode == SocketError.WouldBlock) return false;
            }
        }
    }

    /// <summary>Every datagram waiting on the socket handed to the simulator's way in.</summary>
    void DrainIntoSim()
    {
        while (SocketRead(rawBuffer, out int n, out var from)) Sim!.In(rawBuffer.AsSpan(0, n), (IPEndPoint)from);
    }

    /// <summary>One datagram as it arrives at this end: the socket's, or the simulator's when one is in and its time has come.</summary>
    bool RawRead(byte[] buffer, out int length, out EndPoint from)
    {
        if (Sim is null) return SocketRead(buffer, out length, out from);
        DrainIntoSim();
        if (Sim.DueIn(out var data, out var peer))
        {
            data.CopyTo(buffer, 0);
            (length, from) = (data.Length, peer);
            return true;
        }
        (length, from) = (0, new IPEndPoint(IPAddress.Any, 0));
        return false;
    }

    /// <summary>
    /// **The next datagram this end takes, opened** (what a tape records, <see cref="NetTape"/>): read, counted, and
    /// a sealed one opened into the plain packet it carries (its kind marked opened); a datagram of the older wire
    /// answered (<see cref="OldWire"/>); anything else that is not ours, too short, a kind not known, or a plain data or
    /// goodbye packet (which only comes sealed), dropped and counted. False when none waits.
    /// </summary>
    bool ReadOpened(byte[] buffer, out int length, out EndPoint from)
    {
        while (true)
        {
            if (!RawRead(rawBuffer, out int n, out from)) { length = 0; return false; }
            Stats.DatagramsIn++;
            Stats.BytesIn += n;
            var raw = rawBuffer.AsSpan(0, n);
            if (n < 3 || raw[0] != Magic) { Stats.Junk++; continue; }
            var remote = Normalize((IPEndPoint)from);
            if (raw[1] == OldVersion) { OldWire(raw, remote); continue; }
            if (raw[1] != Version) { Stats.Junk++; continue; }
            switch ((Packet)raw[2])
            {
                case Packet.Sealed:
                {
                    if (n < NetSeal.HeaderLength + 1 + 16) { Stats.Junk++; continue; }
                    uint token = BinaryPrimitives.ReadUInt32LittleEndian(raw[3..]);
                    if (!connections.TryGetValue((remote, token), out var c) || c.Closed || !c.Seal.Ready) { Stats.Unsealed++; continue; }
                    int opened = c.Seal.Open(raw, buffer, out bool again);
                    if (opened < 0)
                    {
                        if (again) Stats.Replayed++; else Stats.Unsealed++;
                        continue;
                    }
                    if (buffer[2] is not ((byte)Packet.Data or (byte)Packet.Bye or (byte)Packet.Refuse)) { Stats.Junk++; Counted(c, clock(), "an unknown kind of packet"); continue; }
                    buffer[2] |= Opened;
                    length = opened;
                    return true;
                }
                case Packet.Connect or Packet.Accept or Packet.Refuse or Packet.Datagram:
                    raw.CopyTo(buffer);
                    length = n;
                    return true;
                case Packet.Data or Packet.Bye:
                    Stats.Unsealed++;
                    continue;
                default:
                    Stats.Junk++;
                    continue;
            }
        }
    }

    /// <summary>
    /// What a tape writes of a datagram taken: the datagram as it was opened, but **the password's proof
    /// masked** — the first reliable record of a connection when it is a Hello (<see cref="Msg.Hello"/>, the proof
    /// after its protocol: <see cref="HelloProofAt"/>) has the proof's bytes zeroed, so no tape holds anything made from the
    /// password. A replay takes the host's recorded answer to the check (<see cref="NetTape.WrongPassword"/>).
    /// </summary>
    public static byte[] Masked(byte[] buffer, int length)
    {
        var copy = buffer.AsSpan(0, length).ToArray();
        if (length < 15 + 7 || copy[2] != (Opened | (byte)Packet.Data)) return copy;
        var body = copy.AsSpan(15);
        int at = 0;
        while (at + 3 <= body.Length)
        {
            var channel = (Channel)body[at++];
            uint seq = 0;
            if (channel == Channel.Reliable)
            {
                if (at + 4 > body.Length) break;
                seq = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
                at += 4;
            }
            else if (channel != Channel.Unreliable) break;
            if (at + 2 > body.Length) break;
            int n = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]) & ~PieceBit;
            at += 2;
            if (at + n > body.Length) break;
            var record = body.Slice(at, n);
            if (channel == Channel.Reliable && seq == 0 && n >= HelloProofAt + 1 && record[0] == (byte)Msg.Hello)
            {
                int proof = Math.Min(record[HelloProofAt], n - HelloProofAt - 1);
                record.Slice(HelloProofAt + 1, proof).Clear();
            }
            at += n;
        }
        return copy;
    }

    /// <summary>Where a Hello's proof is: its length's byte, after the kind and the protocol's four.</summary>
    public const int HelloProofAt = 5;

    void Receive(long now)
    {
        while (true)
        {
            int n;
            EndPoint from;
            if (NetTape.Tape is { } tape) { if (!tape.Receive(socket, receiveBuffer, ReadOpened, Masked, out n, out from)) return; }
            else if (!ReadOpened(receiveBuffer, out n, out from)) return;
            Dispatch(receiveBuffer.AsSpan(0, n), Normalize((IPEndPoint)from), now);
        }
    }

    /// <summary>One opened datagram (or a tape's) dealt with; nothing in it is trusted, every read in bounds.</summary>
    void Dispatch(ReadOnlySpan<byte> packet, IPEndPoint remote, long now)
    {
        if (packet.Length < 3) { Stats.Junk++; return; }
        var span = packet[3..];
        switch (packet[2])
        {
            case (byte)Packet.Datagram:
                if (!Allowed(remote, now, knock: false)) return;
                events.Add(new NetEvent(NetEventKind.Datagram, null, span.ToArray(), remote));
                break;
            case (byte)Packet.Connect when Listening:
            {
                if (span.Length < 4 + NetSeal.HalfLength) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (connections.TryGetValue((remote, token), out var c))
                {
                    // The knock again (its answer lost): answered again as it was, if it is the same knock.
                    if (c.Closed || c.Outgoing || !span.Slice(4, NetSeal.HalfLength).SequenceEqual(c.KnockHalf)) { Stats.Junk++; return; }
                    c.LastReceived = now;
                    Raw(remote, c.Knock!);
                    return;
                }
                if (connections.Count(kv => !kv.Value.Closed) >= MaxConnections) { Stats.Limited++; return; }
                if (!Allowed(remote, now, knock: true)) return;
                c = new Connection(remote, token, outgoing: false, now);
                if (!c.Seal.Join(span.Slice(4, NetSeal.HalfLength), token)) { c.Seal.Dispose(); Stats.Junk++; return; }
                c.KnockHalf = span.Slice(4, NetSeal.HalfLength).ToArray();
                var accept = new byte[7 + NetSeal.HalfLength];
                Header(accept, Packet.Accept);
                BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(3), token);
                c.Seal.Half.CopyTo(accept, 7);
                c.Knock = accept;
                c.Open = true;
                connections[(remote, token)] = c;
                events.Add(new NetEvent(NetEventKind.Connected, c, []));
                Raw(remote, accept);
                break;
            }
            case (byte)Packet.Accept:
            {
                if (span.Length < 4 + NetSeal.HalfLength) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (!connections.TryGetValue((remote, token), out var c) || !c.Outgoing || c.Open || c.Closed) return;
                if (!c.Seal.Join(span.Slice(4, NetSeal.HalfLength), token)) { Stats.Junk++; return; }
                c.Open = true;
                c.LastReceived = now;
                events.Add(new NetEvent(NetEventKind.Connected, c, []));
                break;
            }
            case (byte)Packet.Refuse:
            {
                // Plain: only a knock still waiting is refused so (an open connection's refusal comes sealed).
                if (span.Length < 4) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (connections.TryGetValue((remote, token), out var c) && c.Outgoing && !c.Open)
                    Close(c, Reason(span[4..]), report: true);
                break;
            }
            case Opened | (byte)Packet.Refuse:
            {
                if (span.Length < 4) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (connections.TryGetValue((remote, token), out var c)) Close(c, Reason(span[4..]), report: true);
                break;
            }
            case Opened | (byte)Packet.Bye:
            {
                if (span.Length < 4) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (connections.TryGetValue((remote, token), out var c)) Close(c, "bye", report: true);
                break;
            }
            case Opened | (byte)Packet.Data:
            {
                if (span.Length < 12) { Stats.Junk++; return; }
                uint token = BinaryPrimitives.ReadUInt32LittleEndian(span);
                if (!connections.TryGetValue((remote, token), out var c) || c.Closed) break;
                if (!c.Open && c.Outgoing)
                {
                    // The accept was lost and the host is already talking: that is an accept too. (Under a tape, whose
                    // datagrams come opened; on the wire a sealed one cannot open before the accept made the keys.)
                    c.Open = true;
                    events.Add(new NetEvent(NetEventKind.Connected, c, []));
                }
                c.LastReceived = now;
                Acknowledged(c, BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
                             BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), now);
                Messages(c, span[12..], now);
                break;
            }
            default:
                Stats.Junk++;
                break;
        }
    }

    /// <summary>A refusal's reason: at most 200 bytes of UTF-8 (a remote's text is bounded).</summary>
    static string Reason(ReadOnlySpan<byte> bytes) => System.Text.Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 200)]);

    /// <summary>
    /// **An address's limits** (ours): at most <see cref="KnocksPerAddress"/> new connections and
    /// <see cref="DatagramsPerAddress"/> datagrams outside one in <see cref="LimitWindowMs"/>; past either, what it sends
    /// is dropped and counted, and the console told once a window. The table of addresses is itself bounded.
    /// </summary>
    bool Allowed(IPEndPoint remote, long now, bool knock)
    {
        var address = remote.Address;
        if (!limits.TryGetValue(address, out var l) || now - l.Since > LimitWindowMs) l = (now, 0, 0, false);
        if (knock) l.Knocks++; else l.Datagrams++;
        bool over = knock ? l.Knocks > KnocksAllowed : l.Datagrams > DatagramsPerAddress;
        if (over)
        {
            Stats.Limited++;
            if (!l.Told) Note($"net: {address} sends more {(knock ? "joins" : "datagrams")} than one address may in {LimitWindowMs / 1000} s: the rest are dropped");
            l.Told = true;
        }
        if (limits.Count >= 1024 && !limits.ContainsKey(address))
            foreach (var old in limits.Where(kv => now - kv.Value.Since > LimitWindowMs).Select(kv => kv.Key).ToList()) limits.Remove(old);
        if (limits.Count < 4096 || limits.ContainsKey(address)) limits[address] = l;
        return !over;
    }

    /// <summary>
    /// **An older Vitomir's knock**: the wire's version 1, before the seal. A host answers it in version 1 — its
    /// knock accepted, and its Hello (reliable message 0) answered with message 0 of its own, 1.1's refusal
    /// <c>UI_NET_BADPROTOCOL</c> — so the older build says what 1.1 says to a mismatched join. Nothing is kept: each of its
    /// datagrams is answered as it comes, within the address's limit, and the console told once.
    /// </summary>
    void OldWire(ReadOnlySpan<byte> raw, IPEndPoint remote)
    {
        if (!Listening || raw.Length < 7) { Stats.Junk++; return; }
        if (!Allowed(remote, clock(), knock: false)) return;
        uint token = BinaryPrimitives.ReadUInt32LittleEndian(raw[3..]);
        switch ((Packet)raw[2])
        {
            case Packet.Connect:
            {
                Stats.OldWire++;
                var accept = new byte[7];
                (accept[0], accept[1], accept[2]) = (Magic, OldVersion, (byte)Packet.Accept);
                BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(3), token);
                Raw(remote, accept);
                if (oldTold.Add(remote.Address))
                    Note($"net: a join from {remote} refused — UI_NET_BADPROTOCOL: its Vitomir is older than this host's (its wire is version {OldVersion}, this one's {Version})");
                break;
            }
            case Packet.Data when raw.Length >= 15:
            {
                var refusal = new NetWriter(Msg.Refused).Str("UI_NET_BADPROTOCOL").ToArray();
                var data = new byte[15 + 7 + refusal.Length];
                (data[0], data[1], data[2]) = (Magic, OldVersion, (byte)Packet.Data);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(3), token);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(7), 1);
                data[15] = (byte)Channel.Reliable;
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), (ushort)refusal.Length);
                refusal.CopyTo(data, 22);
                Raw(remote, data);
                break;
            }
            default:
                Stats.Junk++;
                break;
        }
    }
    readonly HashSet<IPAddress> oldTold = [];

    /// <summary>The other end has everything below <paramref name="next"/>, and the ones its bits name after it.</summary>
    static void Acknowledged(Connection c, uint next, uint bits, long now)
    {
        if (c.Pending.Count == 0) return;
        foreach (var seq in c.Pending.Keys.ToList())
        {
            bool have = seq < next || (seq > next && seq - next - 1 < 32 && (bits & (1u << (int)(seq - next - 1))) != 0);
            if (!have) continue;
            var o = c.Pending[seq];
            // Karn's rule: only a message sent once says anything about the round trip; and only one that was sent.
            if (o.Sends == 1) c.Measured(Math.Clamp(now - o.SentAt, 0, TimeoutMs));
            if (o.Sends == 0) continue;
            c.Pending.Remove(seq);
        }
    }

    void Messages(Connection c, ReadOnlySpan<byte> body, long now)
    {
        int at = 0;
        while (at + 3 <= body.Length)
        {
            var channel = (Channel)body[at++];
            if (channel is not (Channel.Reliable or Channel.Unreliable)) { Stats.BadRecords++; Counted(c, now, "a record of no channel"); return; }
            uint seq = 0;
            if (channel == Channel.Reliable)
            {
                if (at + 4 > body.Length) { Stats.BadRecords++; Counted(c, now, "a record cut short"); return; }
                seq = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
                at += 4;
            }
            if (at + 2 > body.Length) { Stats.BadRecords++; Counted(c, now, "a record cut short"); return; }
            int length = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
            bool more = channel == Channel.Reliable && (length & PieceBit) != 0;         // a piece, more following
            length &= ~PieceBit;
            at += 2;
            if (at + length > body.Length || length > MaxMessage) { Stats.BadRecords++; Counted(c, now, "a record past its datagram"); return; }
            var record = body.Slice(at, length);
            at += length;
            if (channel == Channel.Unreliable)
            {
                if (length > 0) events.Add(new NetEvent(NetEventKind.Message, c, record.ToArray()));
                continue;
            }
            // Reliable: owed an ack whatever it is, delivered only in order and only once; held for a gap only so far
            // ahead and so much, past which it is not taken and comes again.
            if (seq < c.RecvNext || c.Early.ContainsKey(seq)) { c.AckOwed = true; continue; }
            if (seq != c.RecvNext)
            {
                if (seq - c.RecvNext >= MaxEarly || c.EarlyBytes + length > MaxEarlyBytes) { Stats.BadRecords++; continue; }
                c.AckOwed = true;
                c.Early[seq] = (record.ToArray(), more);
                c.EarlyBytes += length;
                continue;
            }
            c.AckOwed = true;
            c.RecvNext++;
            if (!Deliver(c, record.ToArray(), more)) return;
            while (c.Early.Remove(c.RecvNext, out var next))
            {
                c.EarlyBytes -= next.Data.Length;
                c.RecvNext++;
                if (!Deliver(c, next.Data, next.More)) return;
            }
        }
        if (at != body.Length) { Stats.BadRecords++; Counted(c, now, "a record cut short"); }
    }

    /// <summary>
    /// Writes what is due on a connection: resends first (a message unacknowledged for two round
    /// trips, at least 100 ms and at most a second), then new reliable messages, then the
    /// unreliable queue, packed into datagrams; and an empty one when nothing else has gone for a
    /// heartbeat, which carries the acknowledgements. Each datagram sealed.
    /// </summary>
    void Flush(Connection c, long now)
    {
        // The back-off's multiple of the resend's time (Karn's algorithm), at most half the time-out.
        long resend = Math.Min(Math.Clamp((long)(c.RoundTripMs * 2f), 100, 1000) * c.Backoff, TimeoutMs / 2);
        var packet = new byte[MaxPlain];
        int length = StartData(c, packet);
        bool any = false, resent = false;
        int flying = InFlight(c);
        foreach (var (seq, o) in c.Pending)
        {
            if (o.Sends > 0 && now - o.SentAt < resend) continue;
            resent |= o.Sends > 0;
            if (o.Sends == 0 && !Room(ref flying, o.Data.Length)) break;               // the window full (Transport.Pieces.cs)
            int need = 1 + 4 + 2 + o.Data.Length;
            if (length + need > MaxPlain)
            {
                Sealed(c, packet.AsSpan(0, length));
                length = StartData(c, packet);
            }
            packet[length++] = (byte)Channel.Reliable;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(length), seq);
            length += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(length), (ushort)(o.Data.Length | (o.More ? PieceBit : 0)));
            length += 2;
            o.Data.CopyTo(packet, length);
            length += o.Data.Length;
            o.SentAt = now;
            o.Sends++;
            any = true;
        }
        if (resent) c.Backoff = Math.Min(c.Backoff * 2, Connection.MostBackoff);
        while (c.Unreliable.TryDequeue(out var u))
        {
            int need = 1 + 2 + u.Length;
            if (length + need > MaxPlain)
            {
                Sealed(c, packet.AsSpan(0, length));
                length = StartData(c, packet);
            }
            packet[length++] = (byte)Channel.Unreliable;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(length), (ushort)u.Length);
            length += 2;
            u.CopyTo(packet, length);
            length += u.Length;
            any = true;
        }
        if (any || c.AckOwed || now - c.LastSent >= HeartbeatMs)
        {
            Sealed(c, packet.AsSpan(0, length));
            c.LastSent = now;
            c.AckOwed = false;
        }
    }

    /// <summary>A data packet's head: the token, then what this end has had — the next it expects and a bit for each of the 32 after.</summary>
    static int StartData(Connection c, byte[] packet)
    {
        Header(packet, Packet.Data);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), c.Token);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), c.RecvNext);
        uint bits = 0;
        if (c.Early.Count > 0)
            for (int i = 0; i < 32; i++)
                if (c.Early.ContainsKey(c.RecvNext + 1 + (uint)i)) bits |= 1u << i;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11), bits);
        return 15;
    }

    void SendConnect(Connection c)
    {
        Raw(c.Remote, c.Knock!);
        c.LastConnectSent = clock();
    }

    void Close(Connection c, string reason, bool report)
    {
        if (c.Closed) return;
        c.Closed = true;
        c.Open = false;
        c.Reason = reason;
        if (report) events.Add(new NetEvent(NetEventKind.Disconnected, c, []));
    }

    static void Header(Span<byte> packet, Packet type)
    {
        packet[0] = Magic;
        packet[1] = Version;
        packet[2] = (byte)type;
    }

    /// <summary>A plain packet sealed and sent: nothing goes before the keys are made (a knock not yet answered).</summary>
    void Sealed(Connection c, ReadOnlySpan<byte> plain)
    {
        if (!c.Seal.Ready) return;
        var sealedPacket = new byte[plain.Length + NetSeal.Overhead];
        int n = c.Seal.Seal(plain, sealedPacket, (byte)Packet.Sealed);
        Raw(c.Remote, sealedPacket.AsSpan(0, n));
    }

    void Raw(IPEndPoint to, ReadOnlySpan<byte> packet)
    {
        if (DropRate > 0 && random.NextDouble() < DropRate) return;
        Stats.DatagramsOut++;
        Stats.BytesOut += packet.Length;
        if (Sim is not null) { Sim.Out(packet, to); return; }
        SendTo(to, packet);
    }

    void SendTo(IPEndPoint to, ReadOnlySpan<byte> packet)
    {
        try
        {
            var target = socket.AddressFamily == AddressFamily.InterNetworkV6 && to.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(to.Address.MapToIPv6(), to.Port) : to;
            socket.SendTo(packet, SocketFlags.None, target);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>The simulator's datagrams whose time has come on their way out, sent.</summary>
    void Pump()
    {
        if (Sim is null) return;
        while (Sim.DueOut(out var data, out var to)) SendTo(to, data);
    }

    /// <summary>An IPv4 address seen through a dual-stack socket, put back as IPv4, so one host has one key.</summary>
    static IPEndPoint Normalize(IPEndPoint e) =>
        e.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(e.Address.MapToIPv4(), e.Port) : e;

    /// <summary>
    /// Reads <c>host</c>, <c>host:port</c>, <c>[v6]:port</c> or a bare IPv6 address, the port 4994
    /// when none is given. Names are looked up. Null when it cannot be made sense of.
    /// </summary>
    public static IPEndPoint? ParseAddress(string text, int defaultPort = DefaultPort)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (IPEndPoint.TryParse(text, out var direct))
            return direct.Port == 0 ? new IPEndPoint(direct.Address, defaultPort) : direct;
        string host = text;
        int port = defaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], out var p))
        {
            host = text[..colon];
            port = p;
        }
        if (IPAddress.TryParse(host, out var ip)) return new IPEndPoint(ip, port);
        try
        {
            var found = Dns.GetHostAddresses(host);
            var pick = found.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? found.FirstOrDefault();
            return pick is null ? null : new IPEndPoint(pick, port);
        }
        catch (SocketException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var c in connections.Values.Where(c => !c.Closed).ToList()) Disconnect(c, "closing");
            // What the simulator still holds goes now, so a goodbye is not lost with it.
            if (Sim is not null) while (Sim.Held > 0 && Sim.DueOutNow(out var data, out var to)) SendTo(to, data);
            foreach (var c in connections.Values) c.Seal.Dispose();
            socket.Dispose();
        }
    }
}

/// <summary>
/// **The game's UDP port is held by another program** (a second host started while the first still
/// ran, and the second died with an unhandled <c>SocketException (10013)</c>). Thrown by <see cref="NetPeer"/>'s
/// constructor in place of the socket's own exception, with the one plain line the host says (<see cref="Message"/>): the
/// port, that another program holds it, and <c>--port</c> to choose another. A <see cref="SocketException"/> still, so the
/// menus' own refusal (1.1's <c>UI_NET_CREATEGAMEERROR</c> box) shows the line as it shows any socket's.
/// </summary>
public sealed class PortTakenException(int port, SocketException inner) : SocketException(inner.ErrorCode)
{
    /// <summary>The port asked for.</summary>
    public int Port { get; } = port;

    public override string Message =>
        $"UDP port {Port} is already held by another program (another Vitomir host?): close it, or choose another port with --port";

    /// <summary>Whether a bind failed because the port is in use: EADDRINUSE, or Windows's WSAEACCES (10013) for a port held exclusively.</summary>
    public static bool Taken(SocketException e) => e.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied;
}
