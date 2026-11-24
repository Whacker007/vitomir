// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What a remote sends, fuzzed**: a host and a player, each in a
/// place loaded from the install, sent thousands of seeded cases — random, truncated, oversized, replayed and reordered
/// datagrams from outside (raw sockets on loopback addresses of their own), and random, truncated and mutated messages of
/// every kind over a real sealed connection — and neither throws out of its loop, none of their turns hangs, and the one
/// player who plays fairly is still in the game at the end with every one of its chat lines heard, once, in order.
/// </summary>
public class NetFuzzTests
{
    const int Cases = 5000;

    static void Say(string line)
    {
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable("VITOMIR_FUZZ_REPORT") is { Length: > 0 } file) lock (Kinds) File.AppendAllText(file, line + Environment.NewLine);
    }
    static readonly Msg[] Kinds = Enum.GetValues<Msg>();

    static bool Until(Func<bool> done, int ms, params Action[] turns)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    /// <summary>A message of <paramref name="kind"/>: random bytes, or random fields (GUIDs of the place's things among them).</summary>
    static byte[] Message(Random rng, Msg kind, int[] guids)
    {
        if (rng.Next(4) == 0)
        {
            var junk = new byte[rng.Next(4) == 0 ? rng.Next(0, NetPeer.MaxMessage) : rng.Next(0, 40)];
            rng.NextBytes(junk);
            if (junk.Length > 0) junk[0] = (byte)kind;
            return junk;
        }
        var w = new NetWriter(kind);
        int fields = rng.Next(0, 24);
        for (int i = 0; i < fields; i++)
        {
            switch (rng.Next(11))
            {
                case 0: w.I32(guids.Length > 0 && rng.Next(2) == 0 ? guids[rng.Next(guids.Length)] : rng.Next()); break;
                case 1: w.I32(rng.Next(-3, 40)); break;
                case 2: w.U8((byte)rng.Next(256)); break;
                case 3: w.U16((ushort)rng.Next(rng.Next(2) == 0 ? 8 : 65536)); break;
                case 4: w.F32(rng.Next(5) switch { 0 => float.NaN, 1 => float.PositiveInfinity, 2 => -1e30f, _ => (float)(rng.NextDouble() * 2000 - 1000) }); break;
                case 5: w.Str(new string('x', rng.Next(rng.Next(8) == 0 ? 3000 : 12))); break;
                case 6: w.Str(rng.Next(3) switch { 0 => "MP_Serena", 1 => "ef_decreasefrenzy", _ => "../../../etc" }); break;
                case 7: w.Bool(rng.Next(2) == 0); break;
                case 8: w.U16(0xffff); break;
                case 9: w.U8(0xff); break;
                default: w.I32(int.MinValue); break;
            }
        }
        var message = w.ToArray();
        // Now and then cut short anywhere.
        if (rng.Next(4) == 0 && message.Length > 1) message = message[..rng.Next(1, message.Length)];
        return message;
    }

    /// <summary>A datagram from outside: random, a known one cut short or bit-flipped, oversized, or one of the known sent again.</summary>
    static byte[] Datagram(Random rng, List<byte[]> known)
    {
        switch (rng.Next(7))
        {
            case 0: { var b = new byte[rng.Next(0, 1600)]; rng.NextBytes(b); return b; }
            case 1: { var b = new byte[rng.Next(3, 120)]; rng.NextBytes(b); b[0] = NetPeer.Magic; b[1] = (byte)rng.Next(0, 4); b[2] = (byte)rng.Next(0, 10); return b; }
            case 2 when known.Count > 0: { var k = known[rng.Next(known.Count)]; return k[..rng.Next(0, k.Length)]; }
            case 3 when known.Count > 0: { var k = (byte[])known[rng.Next(known.Count)].Clone(); for (int i = rng.Next(1, 4); i > 0; i--) k[rng.Next(k.Length)] ^= (byte)(1 << rng.Next(8)); return k; }
            case 4: { var b = new byte[rng.Next(20) == 0 ? 60_000 : rng.Next(1200, 9000)]; rng.NextBytes(b); b[0] = NetPeer.Magic; b[1] = NetPeer.Version; b[2] = 7; return b; }
            case 5 when known.Count > 0: return known[rng.Next(known.Count)];
            default:
            {
                // A knock or a sealed packet on a token of its own, well formed but from nobody.
                var b = new byte[rng.Next(2) == 0 ? 7 + NetSeal.HalfLength : 40];
                rng.NextBytes(b);
                b[0] = NetPeer.Magic; b[1] = NetPeer.Version; b[2] = (byte)(b.Length > 40 ? 1 : 7);
                return b;
            }
        }
    }

    /// <summary>A host in the inn and one fair player in it; the fuzz then goes at both.</summary>
    sealed class Game : IDisposable
    {
        public readonly GameServer Server;
        public readonly GameClient Player;
        public readonly NetPeer PlayerPeer;
        public readonly Simulation Sim;
        public readonly Location Place;
        public int Ms;
        public readonly List<string> Heard = [];
        public readonly List<string> Lines = [];
        public string Last => string.Join(" | ", Lines.TakeLast(8));

        public Game(Vitomir.Engine.Vfs.GameFiles files)
        {
            var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
            var settings = GameSettings.For(inn, "Fuzzed", GameFlags.AutoStoryteller);
            // A playing host, whose own seat hears the fair player's chat (GameServer.Heard).
            Place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, player: "MP_Christof_DA", entrance: settings.Entrance);
            Place.Session = new Session { Here = Place };
            Server = new GameServer(settings, new NetPeer(0, seed: 1) { KnocksAllowed = 1000 }, new NetCharacter { Name = "Hosta", Template = "MP_Christof_DA" }) { Qol = new Qol(), Fixes = new Fixes() };
            Server.Log += l => { lock (Lines) Lines.Add("host " + l); };
            Server.Start();
            Server.Enter(Place, Place.Player?.Thing);
            Sim = new Simulation(Place);
            PlayerPeer = new NetPeer(0, seed: 2);
            Player = new GameClient(PlayerPeer, new IPEndPoint(IPAddress.Loopback, Server.Peer.Port), new NetCharacter { Name = "Fair", Template = "MP_Serena" })
            { Find = new Vitomir.Engine.Ui.Strings(files).Find };
            Player.Log += l => { lock (Lines) Lines.Add("player " + l); };
            // The fair player's own shouts come back to it from the host (every player hears a shout).
            Player.Chatted += (text, _, _) => Heard.Add(text);
            Player.Started += () =>
            {
                var s = Player.Settings!;
                var mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
                mine.Session = new Session { Here = mine };
                Player.Enter(mine, mine.Player?.Thing);
            };
            Assert.True(Until(() => Player.AvatarGuid != 0 && Player.Mirror is not null, 30_000, Turn), "the fair player never came in");
        }

        /// <summary>One turn of both, each timed: none may hang.</summary>
        public void Turn()
        {
            var sw = Stopwatch.StartNew();
            Sim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
            Player.Poll(1f / 30);
            Assert.True(sw.ElapsedMilliseconds < 5_000, $"a turn took {sw.ElapsedMilliseconds} ms");
        }

        public void Dispose()
        {
            Player.Dispose();
            Server.Dispose();
        }
    }

    [Fact]
    public void Datagrams_from_outside_never_crash_or_hang_a_host_or_a_player()
    {
        using var files = Install.Open();
        using var game = new Game(files);
        var rng = new Random(14);
        var sockets = Enumerable.Range(2, 6).Select(i =>
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(IPAddress.Parse($"127.0.0.{i}"), 0));
            s.Blocking = false;
            return s;
        }).ToList();
        try
        {
            var host = new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port);
            var player = new IPEndPoint(IPAddress.Loopback, game.PlayerPeer.Port);
            var known = new List<byte[]>();
            var buffer = new byte[65536];
            int said = 0;
            for (int i = 0; i < Cases; i++)
            {
                var s = sockets[rng.Next(sockets.Count)];
                var d = Datagram(rng, known);
                try { s.SendTo(d, rng.Next(3) == 0 ? player : host); } catch (SocketException) { }
                // What the host answers (an accept, a refusal) becomes known, to be sent again, cut and flipped.
                while (s.Available > 0) { int n = s.Receive(buffer); if (known.Count < 200) known.Add(buffer[..n]); }
                if (i % 50 == 0) game.Player.Say($"!line {said++}");
                if (i % 2 == 0) game.Turn();
            }
            Assert.True(Until(() => game.Heard.Count(h => h.Contains("line ")) == said, 20_000, game.Turn), $"heard {game.Heard.Count(h => h.Contains("line "))} of {said}; the player {game.Player.State} {game.Player.Reason}; host {game.Server.Peer.Stats}; player {game.PlayerPeer.Stats}; {game.Last}");
            Assert.Equal(Enumerable.Range(0, said).Select(n => $"line {n}"), game.Heard.Where(h => h.Contains("line ")).Select(h => h[h.IndexOf("line ")..].TrimEnd('\'', '"')));
            Assert.Equal(ClientState.Playing, game.Player.State);
            Say($"host: {game.Server.Peer.Stats}");
            Say($"player: {game.PlayerPeer.Stats}");
            Assert.True(game.Server.Peer.Stats.Junk + game.Server.Peer.Stats.Unsealed + game.Server.Peer.Stats.Limited > Cases / 3);
        }
        finally { foreach (var s in sockets) s.Dispose(); }
    }

    [Fact]
    public void Messages_of_every_kind_sent_to_a_host_never_crash_or_hang_it()
    {
        using var files = Install.Open();
        using var game = new Game(files);
        var rng = new Random(15);
        // A rogue player of its own: joined fairly, then sending nonsense of every kind. When the host closes it for
        // nonsense, another joins in its place.
        GameClient? rogue = null;
        NetPeer? peer = null;
        int closed = 0, sent = 0;
        void Join()
        {
            rogue?.Dispose();
            peer = new NetPeer(0, seed: rng.Next());
            rogue = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port), new NetCharacter { Name = $"Rogue{closed}", Template = "MP_Serena" }) { StartsItself = false };
            Assert.True(Until(() => rogue.State is ClientState.Room or ClientState.Gone, 10_000, game.Turn, () => rogue.Poll(0.03f)));
        }
        Join();
        var guids = game.Place.Things.Select(t => t.Guid).Concat([game.Player.AvatarGuid]).ToArray();
        int said = 0;
        for (int i = 0; i < Cases; i++)
        {
            if (rogue!.State == ClientState.Gone || peer!.Connections.FirstOrDefault() is not { } link) { closed++; Join(); continue; }
            var kind = rng.Next(10) == 0 ? (Msg)rng.Next(256) : Kinds[i % Kinds.Length];
            // A rogue's own goodbye ends it by the rules (and a new one joins); left out so that each lives to be closed for nonsense.
            if (kind == Msg.Bye) continue;
            var message = Message(rng, kind, guids);
            if (message.Length > NetPeer.MaxMessage || rng.Next(2) == 0) peer.Send(link, Channel.Reliable, message);
            else peer.Send(link, Channel.Unreliable, message);
            sent++;
            if (i % 60 == 0) game.Player.Say($"!line {said++}");
            if (i % 5 == 0) { game.Turn(); rogue.Poll(0.03f); }
        }
        Assert.True(Until(() => game.Heard.Count(h => h.Contains("line ")) >= said, 20_000, game.Turn), $"heard {game.Heard.Count(h => h.Contains("line "))} of {said}; the player {game.Player.State} {game.Player.Reason}; host {game.Server.Peer.Stats}; {game.Last}");
        Assert.Equal(ClientState.Playing, game.Player.State);
        Assert.Contains(game.Server.Players, p => p.Name == "Fair" && p.Avatar is not null);
        Assert.True(game.Server.Peer.Stats.ClosedForNonsense > 0, $"{game.Server.Peer.Stats}");
        Say($"{sent} messages of {Kinds.Length} kinds and others, {closed} rogues closed; host: {game.Server.Peer.Stats}");
        rogue?.Dispose();
    }

    [Fact]
    public void Messages_of_every_kind_sent_to_a_player_never_crash_or_hang_it()
    {
        using var files = Install.Open();
        using var game = new Game(files);
        var rng = new Random(16);
        game.PlayerPeer.NonsenseAllowed = int.MaxValue;              // every case at the one player (the closing is the host test's)
        var guids = game.Place.Things.Select(t => t.Guid).Concat([game.Player.AvatarGuid]).ToArray();
        int sent = 0;
        for (int i = 0; i < Cases; i++)
        {
            // The host's own transport, to the player's connection, so every message comes sealed and in order.
            if (game.Server.Peer.Connections.FirstOrDefault() is not { } link) { Say($"no connection after {i} cases"); break; }
            var kind = rng.Next(10) == 0 ? (Msg)rng.Next(256) : Kinds[i % Kinds.Length];
            // Not the session's own end: a Bye, a Refused or a Travel ends the fair game by its rules, which is not a crash.
            if (kind is Msg.Bye or Msg.Refused or Msg.Travel) continue;
            var message = Message(rng, kind, guids);
            game.Server.Peer.Send(link, message.Length > NetPeer.MaxMessage || rng.Next(2) == 0 ? Channel.Reliable : Channel.Unreliable, message);
            sent++;
            if (i % 5 == 0) game.Turn();
        }
        for (int i = 0; i < 60; i++) game.Turn();
        Say($"{sent} messages sent to the player; its state {game.Player.State}; player: {game.PlayerPeer.Stats}");
        // It never threw and never hung (each turn timed), and every case reached it.
        Assert.True(sent > Cases * 9 / 10, $"{sent} sent");
        Assert.True(game.PlayerPeer.Stats.BadMessages > 0);
    }
}

