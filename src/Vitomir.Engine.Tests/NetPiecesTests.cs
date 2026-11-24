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
/// **A reliable message of any size** (ours): the
/// transport cuts a long reliable message into pieces, sends them as reliable messages of their own — at most a
/// window's worth unacknowledged — and joins them again at the far end, so nothing above it has a ceiling but the
/// bound (<see cref="NetPeer.MaxReliableMessage"/>), which is refused where the message is sent and, should pieces
/// past it arrive, where they are received. 1.1 never needs this: its join sends each item by itself.
/// And against the install, the join it was built for: a character with two hundred items in her pack
/// and belt and a hundred in her kept vault joins whole, her sheet brings all she carries to her machine, and her
/// vault's hundred reach it as the host opens it.
/// </summary>
public class NetPiecesTests
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

    static byte[] Noise(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A host and a guest connected over loopback, what reaches the host gathered.</summary>
    sealed class Pair : IDisposable
    {
        public readonly NetPeer Host, Guest;
        public readonly Connection Link;
        public readonly List<byte[]> Got = [];
        public readonly List<NetEvent> HostEvents = [];

        public Pair(double drop = 0)
        {
            Host = new NetPeer(0, seed: 1) { Listening = true, DropRate = drop };
            Guest = new NetPeer(0, seed: 2) { DropRate = drop };
            Link = Guest.Connect(Local(Host));
            Assert.True(Until(() => Link.Open, 4000, PollHost, () => Guest.Poll()), "never connected");
        }

        public void PollHost()
        {
            foreach (var e in Host.Poll())
            {
                HostEvents.Add(e);
                if (e.Kind == NetEventKind.Message) Got.Add(e.Data);
            }
        }

        public bool Run(Func<bool> done, int ms = 10_000) => Until(done, ms, PollHost, () => Guest.Poll());

        public void Dispose() { Guest.Dispose(); Host.Dispose(); }
    }

    [Fact]
    public void A_300_KB_reliable_message_arrives_whole_and_in_its_place_among_small_ones()
    {
        using var pair = new Pair();
        var big = Noise(300 * 1024, 7);
        pair.Guest.Send(pair.Link, Channel.Reliable, new NetWriter(Msg.Say).I32(1).ToArray());
        pair.Guest.Send(pair.Link, Channel.Reliable, big);
        pair.Guest.Send(pair.Link, Channel.Reliable, new NetWriter(Msg.Say).I32(2).ToArray());
        // Two small ones and the big one's pieces, each numbered: 300 KB is 264 pieces of 1,168 bytes.
        Assert.Equal(2 + (big.Length + NetPeer.MaxMessage - 1) / NetPeer.MaxMessage, pair.Link.Unacknowledged);
        Assert.True(pair.Run(() => pair.Got.Count == 3), $"only {pair.Got.Count} of 3 arrived");
        Assert.Equal(1, new NetReader(pair.Got[0]).I32());
        Assert.Equal(big, pair.Got[1]);
        Assert.Equal(2, new NetReader(pair.Got[2]).I32());
        Assert.True(pair.Run(() => pair.Link.Unacknowledged == 0), "never all acknowledged");
    }

    [Fact]
    public void A_long_message_goes_out_a_window_at_a_time()
    {
        using var pair = new Pair();
        pair.Guest.Send(pair.Link, Channel.Reliable, Noise(500 * 1024, 3));
        pair.Guest.Poll();                                                   // the host not listening yet
        Assert.InRange(pair.Link.InFlight, NetPeer.Window - NetPeer.MaxMessage, NetPeer.Window);
        pair.Guest.Poll();                                                   // nothing acknowledged: nothing more goes
        Assert.InRange(pair.Link.InFlight, NetPeer.Window - NetPeer.MaxMessage, NetPeer.Window);
        Assert.True(pair.Run(() => pair.Got.Count == 1), "never arrived");
        Assert.Equal(500 * 1024, pair.Got[0].Length);
        // A message bigger than the window alone still goes when nothing else is in flight.
        Assert.True(pair.Run(() => pair.Link.InFlight == 0));
    }

    [Fact]
    public void Pieces_lost_and_resent_still_make_their_messages_whole_and_in_order()
    {
        using var pair = new Pair(drop: 0.3);
        var sent = new List<byte[]>();
        for (int i = 0; i < 6; i++)
        {
            var m = i % 2 == 0 ? new NetWriter(Msg.Say).I32(i).ToArray() : Noise(20_000 + i * 7_001, i);
            sent.Add(m);
            pair.Guest.Send(pair.Link, Channel.Reliable, m);
        }
        Assert.True(pair.Run(() => pair.Got.Count == sent.Count, 20_000), $"only {pair.Got.Count} of {sent.Count} arrived");
        for (int i = 0; i < sent.Count; i++) Assert.Equal(sent[i], pair.Got[i]);
        Assert.True(pair.Run(() => pair.Link.Unacknowledged == 0, 20_000), "never all acknowledged");
    }

    [Fact]
    public void A_message_past_the_bound_is_refused_where_it_is_sent_and_one_at_the_bound_goes()
    {
        using var pair = new Pair();
        Assert.Throws<ArgumentException>(() => pair.Guest.Send(pair.Link, Channel.Reliable, new byte[NetPeer.MaxReliableMessage + 1]));
        Assert.Equal(0, pair.Link.Unacknowledged);                          // nothing queued of it
        Assert.Throws<ArgumentException>(() => pair.Guest.Send(pair.Link, Channel.Unreliable, new byte[NetPeer.MaxMessage + 1]));
        var most = Noise(NetPeer.MaxReliableMessage, 11);
        pair.Guest.Send(pair.Link, Channel.Reliable, most);
        Assert.True(pair.Run(() => pair.Got.Count == 1, 20_000), "the largest message never arrived");
        Assert.Equal(most, pair.Got[0]);
        Assert.True(pair.Link.Open);
    }

    [Fact]
    public void Pieces_past_the_bound_arriving_close_the_connection_and_the_sender_is_told_why()
    {
        // A peer that does not keep the bound, written by hand: 1 MiB and a piece more, all marked "more follows" —
        // sealed as the wire's version 2 wants (the knock carries its half of the exchange, the answer the host's).
        // A wire written by hand against the transport itself: no simulator between them (VITOMIR_NET_SIM's runs too).
        using var host = new NetPeer(0) { Listening = true, Sim = null };
        using var raw = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        raw.ReceiveTimeout = 2000;
        var to = new IPEndPoint(IPAddress.Loopback, host.Port);
        const uint token = 0x1234;
        using var seal = new NetSeal(connecting: true);
        var events = new List<NetEvent>();
        var connect = new byte[7 + NetSeal.HalfLength];
        connect[0] = NetPeer.Magic; connect[1] = NetPeer.Version; connect[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(connect.AsSpan(3), token);
        seal.Half.CopyTo(connect, 7);
        raw.SendTo(connect, to);
        Assert.True(Until(() => events.Any(e => e.Kind == NetEventKind.Connected), 4000, () => events.AddRange(host.Poll())));
        var buffer = new byte[2048];
        int got = raw.Receive(buffer);
        Assert.Equal(2, buffer[2]);
        Assert.True(seal.Join(buffer.AsSpan(7, got - 7), token));
        var piece = new byte[NetPeer.MaxMessage];
        int pieces = NetPeer.MaxReliableMessage / NetPeer.MaxMessage + 2;
        var sealedPacket = new byte[2048];
        for (uint seq = 0; seq < pieces; seq++)
        {
            var packet = new byte[15 + 7 + piece.Length];
            packet[0] = NetPeer.Magic; packet[1] = NetPeer.Version; packet[2] = 4;                // data
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), token);
            packet[15] = (byte)Channel.Reliable;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), seq);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), (ushort)(piece.Length | 0x8000));
            int n = seal.Seal(packet, sealedPacket, 7);
            raw.SendTo(sealedPacket.AsSpan(0, n), SocketFlags.None, to);
            if (seq % 16 == 15) events.AddRange(host.Poll());
        }
        Assert.True(Until(() => events.Any(e => e.Kind == NetEventKind.Disconnected), 4000, () => events.AddRange(host.Poll())),
                    "the host kept the connection");
        var gone = events.Single(e => e.Kind == NetEventKind.Disconnected);
        Assert.Equal(NetPeer.TooBig, gone.Reason);
        Assert.DoesNotContain(events, e => e.Kind == NetEventKind.Message);
        Assert.Empty(host.Connections);
        // The far end is told, with the handshake's refusal, sealed: its token and the reason.
        byte[]? refusal = null;
        var opened = new byte[2048];
        while (refusal is null)
        {
            int n = raw.Receive(buffer);
            if (n < 7 || buffer[2] != 7) continue;
            int m = seal.Open(buffer.AsSpan(0, n), opened, out _);
            if (m >= 7 && opened[2] == 3) refusal = opened[..m];
        }
        Assert.Equal(token, BinaryPrimitives.ReadUInt32LittleEndian(refusal.AsSpan(3)));
        Assert.Equal(NetPeer.TooBig, System.Text.Encoding.UTF8.GetString(refusal.AsSpan(7)));
    }

    [Fact]
    public void A_join_past_the_bound_is_refused_on_the_joining_machine_with_a_line_and_one_under_it_goes()
    {
        var me = new NetCharacter { Name = "Anna", Template = "MP_Serena" };
        string? said = null;
        Assert.Equal("UI_NET_DISCONNECTED", NetJoin.Refused(new byte[NetPeer.MaxReliableMessage + 1], me, l => said = l));
        Assert.Contains("Anna", said);
        said = null;
        Assert.Null(NetJoin.Refused(new byte[NetPeer.MaxReliableMessage], me, l => said = l));
        Assert.Null(said);
    }

    // ---- against the install: the join an earlier wire could not carry ----

    sealed class Two : IDisposable
    {
        public required GameServer Server;
        public required Location Host;
        public required Simulation HostSim;
        public required GameClient Client;
        public Location? Mine;
        public Simulation? MySim;
        public int Ms;
        public readonly List<string> Said = [];
        public void Step()
        {
            HostSim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
            Client.Poll(1f / 30);
            MySim?.Step(1f / 30);
        }
        public void Dispose() { Client.Dispose(); Server.Dispose(); }
    }

    static Two Start(Vfs.GameFiles files, NetCharacter me)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0));
        server.Start();
        server.Enter(place, null);
        var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port), me);
        var two = new Two { Server = server, Host = place, HostSim = new Simulation(place), Client = client };
        server.Log += two.Said.Add;
        client.Log += two.Said.Add;
        client.Started += () =>
        {
            var s = client.Settings!;
            two.Mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: me.Template, entrance: s.Entrance);
            two.MySim = new Simulation(two.Mine);
            client.Enter(two.Mine, two.Mine.Player?.Thing);
        };
        Assert.True(Until(() => client.Avatar is not null, 20_000, two.Step), "never played");
        return two;
    }

    [Fact]
    public void A_joiner_with_200_items_in_pack_and_belt_and_a_100_item_kept_vault_joins_whole()
    {
        using var files = Install.Open();
        var pack = Enumerable.Range(0, 160).Select(i => new SheetItem("AnezkaLetter", Slot.Inventory, i % 16, i / 16, 1));
        var belt = Enumerable.Range(0, 40).Select(i => new SheetItem("AnezkaLetter", Slot.QuickUse, i, 0, 1));
        var vault = Enumerable.Range(0, 100).Select(i => new SheetItem("AnezkaLetter", Slot.Vault, i % 10, i / 10, 2));
        var me = new NetCharacter
        {
            Name = "Anna", Template = "MP_Serena",
            Saved = new NetSheet { Stats = new float[19], BaseStats = new float[19], Pack = [.. pack, .. belt, .. vault] },
        };
        var hello = new NetWriter(Msg.Hello).I32(Protocol.Version);
        me.Write(hello);
        Assert.True(hello.Str("").ToArray().Length > 5 * NetPeer.MaxMessage, "the first join's join would have cut it");

        using var game = Start(files, me);
        Assert.Contains(game.Said, l => l.Contains("300 items"));
        Assert.DoesNotContain(game.Said, l => l.Contains("left out") || l.Contains("too much"));
        var hers = game.Server.Players.Single(p => !p.Host).Avatar!.Actor!;
        Assert.Equal(160, hers.Inventory.Items.Count(i => i.Location == Slot.Inventory));
        Assert.Equal(40, hers.Inventory.Items.Count(i => i.Location == Slot.QuickUse));
        Assert.Equal(100, hers.Inventory.Items.Count(i => i.Location == Slot.Vault && i.Amount == 2));

        // Her sheet brings everything she carries to her machine — the earlier wire carried twenty-four.
        var mine = game.Client.Avatar!.Actor!;
        Assert.True(Until(() => mine.Inventory.Items.Count(i => i.Location != Slot.Vault) == 200, 10_000, game.Step),
                    $"her machine has {mine.Inventory.Items.Count(i => i.Location != Slot.Vault)} of her 200");
        Assert.Contains(mine.Inventory.Items, i => i.Location == Slot.QuickUse && i.Cell == (39, 0));
        Assert.Contains(mine.Inventory.Items, i => i.Location == Slot.Inventory && i.Cell == (15, 9));

        // And the vault's hundred, in one message, as the host opens it there.
        int asked = 0;
        game.Client.VaultAsked += () => asked++;
        game.Server.SendVault(game.Server.Players.Single(p => !p.Host));
        Assert.True(Until(() => asked == 1, 10_000, game.Step));
        Assert.Equal(100, mine.Inventory.Items.Count(i => i.Location == Slot.Vault));
        Assert.Contains(mine.Inventory.Items, i => i.Location == Slot.Vault && i.Cell == (9, 9) && i.Amount == 2);
    }
}
