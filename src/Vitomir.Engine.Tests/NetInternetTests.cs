// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The network game made internet-ready**:
/// the network-conditions simulator, the sealed session (a tampered datagram and one sent again
/// dropped, a wrong password refused and the password never on the wire, an older build's knock answered with
/// UI_NET_BADPROTOCOL), the join's check of the build and the data, the per-address limit, the keep-alive under a load
/// longer than the timeout, and the tape's mask of the password's proof.
/// </summary>
public class NetInternetTests
{
    static IPEndPoint Local(NetPeer peer) => new(IPAddress.Loopback, peer.Port);

    static bool Until(Func<bool> done, int ms, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    // ---- the simulator ----------------------------------------------------------------------------

    [Fact]
    public void The_simulators_spec_reads_every_word_and_refuses_what_it_does_not_know()
    {
        var spec = NetSim.Parse("latency=80,jitter=20,loss=2,reorder=1,dup=0.5,kbps=1000,seed=7", out var error);
        Assert.Null(error);
        Assert.Equal(new NetSim.Spec(80, 20, 2, 1, 0.5, 1000, 7), spec);
        Assert.Null(NetSim.Parse("latency=80,lag=3", out error));
        Assert.Contains("lag=3", error);
        Assert.Null(NetSim.Parse("loss=120", out error));
        Assert.Null(NetSim.Parse("latency=-5", out error));
        Assert.Equal(new NetSim.Spec(LatencyMs: 50), NetSim.Parse(" latency = 50 ", out _));
    }

    [Fact]
    public void The_simulator_holds_each_datagram_back_and_loses_its_share_from_its_seed()
    {
        static (int Got, long Lost) Run(int seed)
        {
            // On the simulator's turn clock (10 ms a turn), not the wall clock: a machine busy with a full test run
            // took longer than half the latency to send the thousand, and the first came due before the check.
            var sim = new NetSim(new NetSim.Spec(LatencyMs: 40, Loss: 10, FrameMs: 10), seed);
            var to = new IPEndPoint(IPAddress.Loopback, 9);
            for (int i = 0; i < 1000; i++) sim.Out([(byte)i], to);
            sim.Turn();
            Assert.False(sim.DueOut(out _, out _));                  // nothing before half the latency (10 of 20 ms)
            sim.Turn();
            int got = 0;
            while (sim.DueOut(out _, out _)) got++;
            return (got, sim.Lost);
        }
        var a = Run(3);
        var b = Run(3);
        Assert.Equal(a, b);                                          // seeded: the same internet each run
        Assert.Equal(1000, a.Got + a.Lost);
        Assert.InRange(a.Lost, 60, 140);                             // ten in a hundred
    }

    // A round trip held to a bound at the wall clock: a timing test, run alone (a full parallel run had one at 476 ms).
    [Fact, Trait("Category", "Timing")]
    public void A_round_trip_through_the_simulator_is_its_latency_longer()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        using var guest = new NetPeer(0, seed: 2) { Sim = new NetSim(new NetSim.Spec(LatencyMs: 120), 1) };
        var link = guest.Connect(Local(host));
        Assert.True(Until(() => link.Open, 5000, () => host.Poll(), () => guest.Poll()));
        Connection? far = null;
        var got = new List<long>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long sent = 0;
        void Host()
        {
            foreach (var e in host.Poll())
            {
                if (e.Kind == NetEventKind.Connected) far = e.Connection;
                if (e.Kind == NetEventKind.Message && far is not null) host.Send(far, Channel.Reliable, e.Data);
            }
        }
        void Guest()
        {
            foreach (var e in guest.Poll()) if (e.Kind == NetEventKind.Message) got.Add(clock.ElapsedMilliseconds - sent);
        }
        Assert.True(Until(() => far is not null || host.Connections.Any(), 3000, Host, Guest));
        far ??= host.Connections.First();
        for (int i = 0; i < 5; i++)
        {
            int had = got.Count;
            sent = clock.ElapsedMilliseconds;
            guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).I32(i).ToArray());
            Assert.True(Until(() => got.Count > had, 3000, Host, Guest));
        }
        Assert.All(got, ms => Assert.InRange(ms, 115, 400));
    }

    // ---- the sealed session -------------------------------------------------------------------------

    /// <summary>A raw socket between a guest and a host: everything the guest sends goes through it, and the test may tamper.</summary>
    sealed class Middle : IDisposable
    {
        public readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public IPEndPoint? Guest;
        public readonly IPEndPoint Host;
        public readonly List<byte[]> FromGuest = [];
        public Middle(NetPeer host)
        {
            Socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Socket.Blocking = false;
            Host = Local(host);
        }
        public IPEndPoint At => (IPEndPoint)Socket.LocalEndPoint!;
        public void Pass(Func<byte[], byte[]?>? change = null)
        {
            var buffer = new byte[65536];
            while (Socket.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n = Socket.ReceiveFrom(buffer, ref from);
                var data = buffer[..n];
                var f = (IPEndPoint)from;
                if (f.Port == Host.Port) { if (Guest is not null) Socket.SendTo(data, Guest); continue; }
                Guest = f;
                FromGuest.Add(data);
                if ((change is null ? data : change(data)) is { } passed) Socket.SendTo(passed, Host);
            }
        }
        public void Dispose() => Socket.Dispose();
    }

    [Fact]
    public void A_tampered_datagram_and_one_sent_again_are_dropped_and_counted_and_the_rest_arrive()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        using var guest = new NetPeer(0, seed: 2) { Sim = null };
        using var middle = new Middle(host);
        var link = guest.Connect(middle.At);
        var got = new List<int>();
        void Host() { foreach (var e in host.Poll()) if (e.Kind == NetEventKind.Message) got.Add(new NetReader(e.Data).I32()); }
        Assert.True(Until(() => link.Open, 5000, Host, () => guest.Poll(), () => middle.Pass()));
        // One message, tampered with on the way (a byte of its sealed body turned over): dropped, never delivered.
        guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).I32(1).ToArray());
        bool tampered = false;
        byte[]? good = null;
        Assert.True(Until(() => got.Count == 1, 5000, Host, () => guest.Poll(), () => middle.Pass(d =>
        {
            if (d[2] != 7 || d.Length < 40) return d;
            if (!tampered) { tampered = true; var bad = (byte[])d.Clone(); bad[20] ^= 0x40; return bad; }
            good ??= d;
            return d;
        })));
        Assert.True(tampered);
        Assert.True(host.Stats.Unsealed >= 1, $"{host.Stats}");
        Assert.Equal([1], got);
        // A datagram recorded and sent again: its number was taken, so it is dropped and counted, and nothing doubles.
        long replayed = host.Stats.Replayed;
        for (int i = 0; i < 3; i++) middle.Socket.SendTo(good!, middle.Host);
        Assert.True(Until(() => host.Stats.Replayed >= replayed + 3, 3000, Host, () => guest.Poll()));
        Assert.Equal([1], got);
        // A plain data packet (as the wire's version 1 sent them) on the connection's token: not taken.
        var plain = new byte[15 + 7 + 5];
        plain[0] = NetPeer.Magic; plain[1] = NetPeer.Version; plain[2] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(3), link.Token);
        long unsealed = host.Stats.Unsealed;
        middle.Socket.SendTo(plain, middle.Host);
        Assert.True(Until(() => host.Stats.Unsealed > unsealed, 3000, Host));
        Assert.Equal([1], got);
        Assert.True(link.Open);
    }

    [Fact]
    public void Nothing_of_the_messages_crosses_the_wire_in_the_clear()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        using var guest = new NetPeer(0, seed: 2) { Sim = null };
        using var middle = new Middle(host);
        var link = guest.Connect(middle.At);
        int got = 0;
        void Host() { foreach (var e in host.Poll()) if (e.Kind == NetEventKind.Message) got++; }
        Assert.True(Until(() => link.Open, 5000, Host, () => guest.Poll(), () => middle.Pass()));
        var line = "Zq7secretWord: the plain words of a chat line"u8.ToArray();
        guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).Str(System.Text.Encoding.UTF8.GetString(line)).ToArray());
        Assert.True(Until(() => got == 1, 5000, Host, () => guest.Poll(), () => middle.Pass()));
        Assert.DoesNotContain(middle.FromGuest, d => d.AsSpan().IndexOf("Zq7secret"u8) >= 0);
        Assert.Contains(middle.FromGuest, d => d[2] == 7);
    }

    static GameSettings Game(string password = "") => new() { Name = "Test", Level = "P1_HAVN", Password = password, MaxPlayers = 4 };

    static (string? Reason, string? Refusal, GameClient Client) JoinWith(GameServer server, string password, NetIdentity? identity = null, Action<GameClient>? also = null)
    {
        var client = new GameClient(new NetPeer(0, seed: 5), Local(server.Peer), new NetCharacter { Name = "Anna", Template = "MP_Serena" }, password)
        { Identity = identity ?? NetIdentity.Unstated };
        int ms = 0;
        Assert.True(Until(() => client.State is ClientState.Room or ClientState.Gone or ClientState.Loading, 15000,
                          () => server.Poll(ms += 10), () => client.Poll(0.01f)));
        also?.Invoke(client);
        return (client.State == ClientState.Gone ? client.Reason : null, client.Refusal, client);
    }

    [Fact]
    public void The_right_password_joins_a_wrong_one_is_1_1s_refusal_and_the_password_never_crosses_the_wire()
    {
        using var server = new GameServer(Game("Lantern7"), new NetPeer(0, seed: 1)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        var (reason, _, client) = JoinWith(server, "Lantern7");
        Assert.Null(reason);
        Assert.Equal(ClientState.Room, client.State);
        client.Dispose();
        var (wrong, _, other) = JoinWith(server, "lantern7");
        Assert.Equal("UI_NET_WRONGGAMEPASSWORD", wrong);
        other.Dispose();
        var (none, _, third) = JoinWith(server, "");
        Assert.Equal("UI_NET_WRONGGAMEPASSWORD", none);
        third.Dispose();
    }

    [Fact]
    public void A_password_proof_is_good_for_its_own_session_alone()
    {
        var a = new byte[32];
        var b = new byte[32];
        a[0] = 1;
        b[0] = 2;
        var proof = PasswordProof.Make("Lantern7", a);
        Assert.Equal(PasswordProof.Length, proof.Length);
        Assert.True(PasswordProof.Holds("Lantern7", a, proof));
        Assert.False(PasswordProof.Holds("Lantern7", b, proof));
        Assert.False(PasswordProof.Holds("Lantern8", a, proof));
        Assert.Empty(PasswordProof.Make("", a));
        Assert.False(PasswordProof.Holds("Lantern7", a, []));
    }

    [Fact]
    public void Another_build_or_other_data_is_refused_with_a_line_that_says_which_differs()
    {
        var mine = new NetIdentity("aaaa000011112222", "Vitomir Platform 2.0", "0123456789abcdef", 4102, "GOG");
        using var server = new GameServer(Game(), new NetPeer(0, seed: 1)) { Qol = new Qol(), Fixes = new Fixes(), Identity = mine };
        server.Start();
        var (reason, refusal, c1) = JoinWith(server, "", mine with { Build = "bbbb000011112222", BuildName = "Vitomir Platform 1.9" });
        Assert.Equal("UI_NET_BADPROTOCOL", reason);
        Assert.Contains("another Vitomir build", refusal);
        Assert.Contains("Vitomir Platform 2.0", refusal);
        Assert.Contains("Vitomir Platform 1.9", refusal);
        c1.Dispose();
        var (data, line, c2) = JoinWith(server, "", mine with { Data = "fedcba9876543210", DataFiles = 4103, Store = "Steam" });
        Assert.Equal("UI_NET_BADPROTOCOL", data);
        Assert.Contains("other VTMR data", line);
        Assert.Contains("0123456789abcdef", line);
        Assert.Contains("Steam", line);
        c2.Dispose();
        var (same, _, c3) = JoinWith(server, "", mine);
        Assert.Null(same);
        c3.Dispose();
    }

    [Fact]
    public void Other_rules_are_refused_with_a_line_that_names_them()
    {
        var mine = new NetIdentity("aaaa000011112222", "Vitomir Platform 2.0", "0123456789abcdef", 4102, "GOG");
        using var server = new GameServer(Game(), new NetPeer(0, seed: 1)) { Qol = new Qol(), Fixes = new Fixes(), Identity = mine };
        server.Start();
        var (reason, refusal, c1) = JoinWith(server, "", mine with { Rules = "1122334455667788:Combat.CriticalMargin,Feeding.FeedTickMs" });
        Assert.Equal("UI_NET_BADPROTOCOL", reason);
        Assert.Contains("other rules", refusal);
        Assert.Contains("Combat.CriticalMargin, Feeding.FeedTickMs", refusal);
        Assert.Contains("1.1's", refusal);
        c1.Dispose();
        var (same, _, c2) = JoinWith(server, "", mine);
        Assert.Null(same);
        c2.Dispose();
    }

    [Fact]
    public void An_older_builds_knock_meets_1_1s_bad_protocol_in_its_own_wire()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        var lines = new List<string>();
        host.Log = lines.Add;
        using var raw = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        raw.ReceiveTimeout = 2000;
        var to = Local(host);
        // The wire's version 1, as the transport writes it: a knock, then the Hello in reliable message 0.
        var knock = new byte[7] { NetPeer.Magic, NetPeer.OldVersion, 1, 0x34, 0x12, 0, 0 };
        raw.SendTo(knock, to);
        host.Poll();
        var buffer = new byte[2048];
        int n = raw.Receive(buffer);
        Assert.Equal([NetPeer.Magic, NetPeer.OldVersion, 2, 0x34, 0x12, 0, 0], buffer[..n]);
        var hello = new NetWriter(Msg.Hello).I32(19).ToArray();
        var data = new byte[15 + 7 + hello.Length];
        (data[0], data[1], data[2]) = (NetPeer.Magic, NetPeer.OldVersion, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(3), 0x1234);
        data[15] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), (ushort)hello.Length);
        hello.CopyTo(data, 22);
        raw.SendTo(data, to);
        host.Poll();
        n = raw.Receive(buffer);
        Assert.Equal(4, buffer[2]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16)));            // reliable message 0
        var refused = new NetReader(buffer[22..n]);
        Assert.Equal(Msg.Refused, refused.Kind);
        Assert.Equal("UI_NET_BADPROTOCOL", refused.Str());
        Assert.Equal(1, host.Stats.OldWire);
        Assert.Contains(lines, l => l.Contains("UI_NET_BADPROTOCOL") && l.Contains("older"));
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void One_address_makes_only_so_many_connections_and_the_console_is_told_once()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, Sim = null };
        var lines = new List<string>();
        host.Log = lines.Add;
        var guests = Enumerable.Range(0, 12).Select(i => new NetPeer(0, seed: 10 + i) { Sim = null }).ToList();
        try
        {
            var links = guests.Select(g => g.Connect(Local(host))).ToList();
            Until(() => false, 1500, [() => host.Poll(), .. guests.Select(g => (Action)(() => g.Poll()))]);
            Assert.Equal(NetPeer.KnocksPerAddress, host.Connections.Count());
            Assert.Equal(NetPeer.KnocksPerAddress, links.Count(l => l.Open));
            Assert.True(host.Stats.Limited > 0);
            Assert.Single(lines, l => l.Contains("more joins than one address may"));
        }
        finally { foreach (var g in guests) g.Dispose(); }
    }

    [Fact]
    public void A_port_another_program_holds_is_said_plainly_with_the_port_and_the_switch()
    {
        using var holder = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)holder.Client.LocalEndPoint!).Port;
        var taken = Assert.Throws<PortTakenException>(() => new NetPeer(port));
        Assert.Equal(port, taken.Port);
        Assert.Contains($"UDP port {port}", taken.Message);
        Assert.Contains("--port", taken.Message);
        Assert.IsAssignableFrom<SocketException>(taken);            // the menus' own refusal box takes it as any socket's
    }

    // ---- the router ---------------------------------------------------------------------------------------

    [Fact]
    public void Nat_pmp_and_pcp_packets_are_written_and_read_as_their_rfcs_lay_them_out()
    {
        Assert.Equal(new byte[] { 0, 0 }, NatPmp.AddressRequest());
        Assert.Equal(new byte[] { 0, 1, 0, 0, 0x13, 0x82, 0x13, 0x82, 0, 0, 0x1c, 0x20 }, NatPmp.MapRequest(4994));
        var address = new byte[] { 0, 128, 0, 0, 0, 0, 0, 9, 81, 2, 69, 160 };
        Assert.Equal(IPAddress.Parse("81.2.69.160"), NatPmp.ReadAddress(address));
        address[3] = 3;
        Assert.Null(NatPmp.ReadAddress(address));
        var map = new byte[] { 0, 129, 0, 0, 0, 0, 0, 9, 0x13, 0x82, 0x13, 0x83, 0, 0, 0x1c, 0x20 };
        Assert.Equal((4995, 7200u), NatPmp.ReadMap(map, out int ok));
        Assert.Equal(0, ok);
        map[3] = 2;
        Assert.Null(NatPmp.ReadMap(map, out int refused));
        Assert.Equal(2, refused);
        var nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var pcp = NatPmp.PcpMap(IPAddress.Parse("192.168.1.20"), 4994, nonce);
        Assert.Equal(60, pcp.Length);
        Assert.Equal(new byte[] { 2, 1 }, pcp[..2]);
        Assert.Equal(IPAddress.Parse("::ffff:192.168.1.20").GetAddressBytes(), pcp[8..24]);
        Assert.Equal(nonce, pcp[24..36]);
        Assert.Equal(17, pcp[36]);
        // The router's answer: the same nonce, the outside port and address.
        var answer = (byte[])pcp.Clone();
        answer[1] = 0x81;
        answer[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(42), 40000);
        IPAddress.Parse("::ffff:81.2.69.160").GetAddressBytes().CopyTo(answer, 44);
        Assert.Equal((IPAddress.Parse("81.2.69.160"), 40000, 7200u), NatPmp.ReadPcpMap(answer, nonce, out _));
        Assert.Null(NatPmp.ReadPcpMap(answer, new byte[12], out _));                    // another's nonce
        Assert.Null(NatPmp.ReadPcpMap(new byte[] { 0, 128, 0, 1 }, nonce, out int version));
        Assert.Equal(1, version);                                                       // a NAT-PMP router's "unsupported version"
    }

    [Fact]
    public void The_router_lines_say_which_way_mapped_it_and_where_players_join_or_the_forward_and_the_overlay()
    {
        var upnp = new PortMapping.Result(false, null, "no UPnP router answered", "UPnP", 4994);
        var pmp = new PortMapping.Result(true, IPAddress.Parse("81.2.69.160"), "mapped", "NAT-PMP", 4995);
        Assert.Equal("the router mapped UDP 4994 by NAT-PMP: players outside join at 81.2.69.160:4995", PortMapping.Summary(4994, [upnp, pmp]));
        string none = PortMapping.Summary(4994, [upnp, pmp with { Mapped = false, External = null }]);
        Assert.Contains("no router mapped UDP 4994", none);
        Assert.Contains("forward UDP 4994", none);
        Assert.Contains("Tailscale or ZeroTier", none);
        Assert.Contains("carrier-grade", PortMapping.Summary(4994, [pmp with { External = IPAddress.Parse("100.70.1.2") }]));
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, NetPeer.DefaultPort), Connectors.Current.Find("127.0.0.1"));
        Assert.Equal("direct", Connectors.Current.Name);
    }

    // ---- the keep-alive -------------------------------------------------------------------------------

    /// <summary>
    /// The keep-alive at the simulator's settings and (players far away) at a round trip of 250 ms with the same jitter and
    /// loss: a player whose first load outlasts the transport's ten seconds of silence stays joined.
    /// </summary>
    [Theory]
    [InlineData(80)]
    [InlineData(250)]
    public void A_player_loading_longer_than_the_timeout_stays_joined_under_the_simulator(int latency)
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        var spec = new NetSim.Spec(LatencyMs: latency, JitterMs: 20, Loss: 2, Reorder: 1, Dup: 0.5, Kbps: 1000);
        using var server = new GameServer(settings, new NetPeer(0, seed: 1) { Sim = new NetSim(spec, 1) }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var loaded = false;
        var client = new GameClient(new NetPeer(0, seed: 2) { Sim = new NetSim(spec, 2) }, Local(server.Peer), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        // The host runs on its own thread meanwhile, as it does on its own machine; the player's load (GameClient.Started,
        // the host's answer to its Start) sleeps two seconds past the transport's ten.
        using var stop = new CancellationTokenSource();
        int ms = 0;
        var hostLoop = new Thread(() => { while (!stop.IsCancellationRequested) { lock (server) server.Poll(ms += 5); Thread.Sleep(5); } });
        try
        {
            client.Started += () => { Thread.Sleep(NetPeer.TimeoutMs + 2_000); loaded = true; };
            hostLoop.Start();
            Assert.True(Until(() => loaded || client.State == ClientState.Gone, 40_000, () => client.Poll(0.01f)), $"the load never ended: {client.State}");
            Assert.True(loaded);
            Assert.NotEqual(ClientState.Gone, client.State);
            // After the load the wire is read again: both ends still have the connection, and the player comes in.
            var mine = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, player: "MP_Serena", entrance: settings.Entrance);
            mine.Session = new Session { Here = mine };
            client.Enter(mine, mine.Player?.Thing);
            Assert.True(Until(() => client.AvatarGuid != 0 || client.State == ClientState.Gone, 20_000, () => client.Poll(0.01f)), $"never placed: {client.State}");
            Assert.Equal(ClientState.Playing, client.State);
            lock (server) Assert.Contains(server.Players, p => p.Name == "Anna" && p.Avatar is not null);
        }
        finally
        {
            stop.Cancel();
            if (hostLoop.IsAlive) hostLoop.Join();
            client.Dispose();
        }
    }

    /// <summary>
    /// **A player whose machine stalls in the game's room** past the transport's ten seconds — a slow machine did in the room of
    /// a game under way, and the host gave it up while it was still choosing — stays joined: the wire is kept
    /// alive from the welcome (<see cref="GameClient"/>), and after the stall its character is chosen and accepted as ever.
    /// </summary>
    [Fact]
    public void A_player_stalled_in_the_room_longer_than_the_timeout_stays_joined_and_chooses_after()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        using var server = new GameServer(settings, new NetPeer(0, seed: 1)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        using var stop = new CancellationTokenSource();
        int ms = 0;
        var hostLoop = new Thread(() => { while (!stop.IsCancellationRequested) { lock (server) server.Poll(ms += 5); Thread.Sleep(5); } });
        var client = new GameClient(new NetPeer(0, seed: 2), Local(server.Peer), me: null) { StartsItself = false };
        try
        {
            hostLoop.Start();
            Assert.True(Until(() => client.State is ClientState.Room or ClientState.Gone, 10_000, () => client.Poll(0.01f)), $"never in the room: {client.State}");
            Assert.Equal(ClientState.Room, client.State);
            Thread.Sleep(NetPeer.TimeoutMs + 2_000);                  // the stalled frame: nothing polled on this machine
            client.Poll(0.01f);
            Assert.Equal(ClientState.Room, client.State);
            lock (server) Assert.Contains(server.Players, p => !p.Host && p.Link is { Closed: false });   // the host has not given it up
            client.Choose(new NetCharacter { Name = "Bob", Template = "MP_Serena" });
            Assert.True(Until(() => client.Accepted || client.State == ClientState.Gone, 10_000, () => client.Poll(0.01f)), $"never accepted: {client.State}");
            Assert.True(client.Accepted);
        }
        finally
        {
            stop.Cancel();
            if (hostLoop.IsAlive) hostLoop.Join();
            client.Dispose();
        }
    }

    // ---- the tape ---------------------------------------------------------------------------------------

    [Fact]
    public void A_taped_hello_has_its_password_proof_masked_and_nothing_else_changed()
    {
        var proof = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var hello = new NetWriter(Msg.Hello).I32(Protocol.Version).Bytes(proof).Str("build").ToArray();
        static byte[] Packet(uint seq, byte[] record)
        {
            var p = new byte[15 + 7 + record.Length];
            (p[0], p[1], p[2]) = (NetPeer.Magic, NetPeer.Version, 0x84);                                   // an opened data packet
            p[15] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), seq);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(20), (ushort)record.Length);
            record.CopyTo(p, 22);
            return p;
        }
        var taped = NetPeer.Masked(Packet(0, hello), 22 + hello.Length);
        Assert.Equal(new byte[32], taped.AsSpan(22 + NetPeer.HelloProofAt + 1, 32).ToArray());
        Assert.Equal(hello[..(NetPeer.HelloProofAt + 1)], taped.AsSpan(22, NetPeer.HelloProofAt + 1).ToArray());
        Assert.Equal(hello[(NetPeer.HelloProofAt + 33)..], taped.AsSpan(22 + NetPeer.HelloProofAt + 33).ToArray());
        // Any other record is as it was: a Hello-like message later on, and a chat line.
        var later = Packet(3, hello);
        Assert.Equal(later, NetPeer.Masked(later, later.Length));
        var say = Packet(0, new NetWriter(Msg.Say).Str("hello").ToArray());
        Assert.Equal(say, NetPeer.Masked(say, say.Length));
    }
}
