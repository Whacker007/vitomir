// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The multiplayer wire and the session over it, with a host and one or two
/// players in one process on localhost: the reliable channel keeps its promise when a third of the
/// datagrams are thrown away, a silent peer is given up, the chat reads its prefixes the way 1.1's
/// host does, and — against the install — a player joins, is given a character in the
/// host's world, walks it by clicking, and is seen walking by the other player.
/// </summary>
public class NetTests
{
    static IPEndPoint Local(NetPeer peer) => new(IPAddress.Loopback, peer.Port);

    /// <summary>
    /// Waits until <paramref name="frames"/> of a game's 30 a second have passed on the wall clock since <paramref name="since"/> began:
    /// for a test whose frames must keep a game's pace because something in it counts real time, as a held key's push does
    /// (<see cref="GameClient.Drive"/> sends it again every <see cref="GameClient.DriveResendMs"/> of the wall clock).
    /// </summary>
    static void Paced(System.Diagnostics.Stopwatch since, int frames)
    {
        long due = frames * 1000L / 30;
        while (since.ElapsedMilliseconds < due) Thread.Sleep((int)Math.Clamp(due - since.ElapsedMilliseconds, 1, 33));
    }

    /// <summary>Polls both ends until the condition holds or two seconds of real time pass.</summary>
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard, not a speed test (levels fork, 4 s was too few on a busy laptop)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(2);
        }
        return done();
    }

    [Fact]
    public void Reliable_messages_arrive_once_and_in_order_through_heavy_loss()
    {
        using var host = new NetPeer(0, seed: 1) { Listening = true, DropRate = 0.3 };
        using var guest = new NetPeer(0, seed: 2) { DropRate = 0.3 };
        var link = guest.Connect(Local(host));
        var got = new List<int>();
        Connection? far = null;
        void Host()
        {
            foreach (var e in host.Poll())
                if (e.Kind == NetEventKind.Message) got.Add(new NetReader(e.Data).I32());
                else if (e.Kind == NetEventKind.Connected) far = e.Connection;
        }
        Assert.True(Until(() => link.Open, Host, () => guest.Poll()), "never connected");
        for (int i = 0; i < 200; i++) guest.Send(link, Channel.Reliable, new NetWriter(Msg.Say).I32(i).ToArray());
        Assert.True(Until(() => got.Count == 200, Host, () => guest.Poll()), $"only {got.Count} of 200 arrived");
        Assert.Equal(Enumerable.Range(0, 200), got);
        Assert.True(Until(() => link.Unacknowledged == 0, Host, () => guest.Poll()), "never all acknowledged");
        Assert.NotNull(far);
    }

    [Fact]
    public void A_peer_that_goes_quiet_is_given_up_after_ten_seconds()
    {
        long now = 0;
        using var host = new NetPeer(0, () => now) { Listening = true };
        using var guest = new NetPeer(0, () => now);
        var link = guest.Connect(Local(host));
        Assert.True(Until(() => link.Open, () => host.Poll(), () => guest.Poll()));
        // The host stops answering; the guest's clock runs on.
        var gone = new List<NetEvent>();
        now += NetPeer.TimeoutMs + 1;
        gone.AddRange(guest.Poll());
        Assert.Contains(gone, e => e.Kind == NetEventKind.Disconnected && e.Reason == "timeout");
        Assert.True(link.Closed);
    }

    [Fact]
    public void Knocking_on_a_port_nobody_holds_gives_up_with_1_1s_bad_address()
    {
        long now = 0;
        using var guest = new NetPeer(0, () => now);
        var link = guest.Connect(new IPEndPoint(IPAddress.Loopback, 9));
        guest.Poll();
        now += NetPeer.ConnectTimeoutMs + 1;
        var events = guest.Poll();
        Assert.Contains(events, e => e.Kind == NetEventKind.Disconnected && e.Reason == "UI_NET_BADADDR");
    }

    [Fact]
    public void The_host_reads_a_chat_line_by_its_first_character()
    {
        var names = new List<string?> { "Server", "Anna", "Bob" };
        ChatLine P(string s, int from = 1) => NetChat.Parse(s, from, names, inGame: true, _ => 4);
        Assert.Equal(ChatKind.Say, P("hello").Kind);
        Assert.Equal((ChatKind.Shout, "run!"), (P("!run!").Kind, P("!run!").Text));
        Assert.Equal(ChatKind.Emote, P(":bow").Kind);
        Assert.Equal((ChatKind.Whisper, 0), (P(".psst").Kind, P(".psst").To));
        Assert.Equal((ChatKind.Whisper, 2, "hi there"), (P("/bob hi there").Kind, P("/bob hi there").To, P("/bob hi there").Text));
        Assert.Equal(-2, P("/carol hi").To);                         // UI_NET_WHISPERNOTFOUND
        Assert.Equal(ChatKind.Team, P(",left flank").Kind);
        Assert.Equal(ChatKind.Say, P(";narration").Kind);            // only the server narrates
        Assert.Equal(ChatKind.Narrate, P(";narration", from: 0).Kind);
        // `%n` rolls n ten-sided dice, at most twenty: the stub die always shows 5.
        Assert.Equal((ChatKind.Roll, "5, 5, 5"), (P("%3").Kind, P("%3").Text));
        Assert.Equal(20, P("%99").Text.Split(", ").Length);
        // In the lobby every line is a lobby line.
        Assert.Equal(ChatKind.Lobby, NetChat.Parse("!hi", 1, names, inGame: false, _ => 0).Kind);
    }

    [Fact]
    public void A_chat_line_is_shown_in_1_1s_words()
    {
        string? Find(string k) => k switch
        {
            "MP_SAY" => "%S says '%T'",
            "MP_WHISPERALT" => "%S whispers to %R, '%T'",
            "MP_ROLLS" => "%S rolls: %T",
            _ => null,
        };
        Assert.Equal("Anna says 'hello'", NetChat.Show(ChatKind.Say, "Anna", "hello", null, Find));
        Assert.Equal("Anna whispers to Bob, 'psst'", NetChat.Show(ChatKind.Whisper, "Anna", "psst", "Bob", Find));
        Assert.Equal("Anna rolls: 3, 9", NetChat.Show(ChatKind.Roll, "Anna", "3, 9", null, Find));
        Assert.Equal("Anna bows deeply", NetChat.Show(ChatKind.Emote, "Anna", "bows deeply", null, Find));
    }

    [Fact]
    public void An_address_is_read_with_or_without_its_port()
    {
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.20"), NetPeer.DefaultPort), NetPeer.ParseAddress("192.168.1.20"));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5000), NetPeer.ParseAddress(" 10.0.0.2:5000 "));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("::1"), 4994), NetPeer.ParseAddress("[::1]:4994"));
        Assert.Null(NetPeer.ParseAddress(""));
    }

    [Fact]
    public void An_outside_address_that_is_itself_private_or_carrier_grade_is_told_apart()
    {
        // The CGNAT check: the router's own outside address in RFC 1918 or RFC 6598's 100.64/10
        // means a second NAT that no mapping can open.
        Assert.True(PortMapping.Shared(IPAddress.Parse("100.72.3.4")));
        Assert.True(PortMapping.Shared(IPAddress.Parse("192.168.0.10")));
        Assert.True(PortMapping.Shared(IPAddress.Parse("172.20.1.1")));
        Assert.True(PortMapping.Shared(IPAddress.Parse("10.1.2.3")));
        Assert.False(PortMapping.Shared(IPAddress.Parse("100.128.0.1")));
        Assert.False(PortMapping.Shared(IPAddress.Parse("81.2.69.160")));
    }

    [Fact]
    public void A_gateway_description_gives_its_WAN_connections_control_address()
    {
        const string description = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device><deviceList><device><deviceList><device>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                    <controlURL>/upnp/control/WANIPConn1</controlURL>
                  </service>
                </serviceList>
              </device></deviceList></device></deviceList></device>
            </root>
            """;
        var found = PortMapping.ControlUrl(description, new Uri("http://192.168.1.1:5000/rootDesc.xml"));
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", found?.Service);
        Assert.Equal("http://192.168.1.1:5000/upnp/control/WANIPConn1", found?.Url.ToString());
        Assert.Equal("http://192.168.1.1:5000/rootDesc.xml",
                     PortMapping.Header("HTTP/1.1 200 OK\r\nlocation: http://192.168.1.1:5000/rootDesc.xml\r\n", "LOCATION"));
    }

    // ---- against the install --------------------------------------------------------------------

    sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Location Place;
        public required Simulation Sim;
        public int Ms;
        public void Step(float seconds = 1f / 30)
        {
            Sim.Step(seconds);
            Ms += (int)(seconds * 1000);
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
        public readonly List<string> Heard = [];
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    /// <summary>The Dark Ages Inn salon (`MP_DAInn`): one small level, no scene, no script — the quickest real game there is.</summary>
    static (GameSettings Settings, string Level) Salon(Vfs.GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var s = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        return (s, s.Level);
    }

    static Game Host(Vfs.GameFiles files, GameSettings settings, Qol? qol = null, Fixes? fixes = null)
    {
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0)) { Qol = qol ?? new Qol(), Fixes = fixes ?? new Fixes() };
        server.Start();
        server.Enter(place, null);
        return new Game { Server = server, Place = place, Sim = new Simulation(place) };
    }

    static Player Join(Vfs.GameFiles files, Game game, string name, string template)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }),
        };
        var strings = new Ui.Strings(files);
        p.Client.Find = strings.Find;
        p.Client.Chatted += (line, _, _) => p.Heard.Add(line);
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: name == "" ? null : template,
                                    entrance: s.Entrance);
            p.Sim = new Simulation(p.Place);
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    [Fact]
    public void A_player_joins_is_given_a_character_and_walks_it_by_clicking()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        using var game = Host(files, settings);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.State == ClientState.Playing && anna.Client.Avatar is not null,
                          () => game.Step(), () => anna.Step()), $"never played: {anna.Client.State} {anna.Client.Reason}");

        // The host made her character, from her template, with her name, and it is hers on her machine.
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.Equal("MP_Serena", hers.Template);
        Assert.Equal("Anna", hers.Actor!.Name);
        Assert.Same(anna.Place!.Player!.Thing, anna.Client.Avatar);

        // A click on the floor ten paces away: her machine sends the move order, the host walks her.
        var me = anna.Place.Player!;
        var from = hers.Position;
        var there = FloorNear(game.Place, from, 200f);
        Assert.True(me.WalkTo(there, anna.Place.Walk!.Find(there, 256f)));
        // Her own walk is predicted on her machine (OwnWalk), so she walks here at once too; the host still walks her.
        Assert.True(me.Walking);
        Assert.True(Until(() => Vector3.Distance(hers.Position, from) > 50f, () => game.Step(), () => anna.Step()),
                    "the host never walked her");
        // And her machine sees her go.
        Assert.True(Until(() => Vector3.Distance(me.Thing.Position, from) > 50f, () => game.Step(), () => anna.Step()),
                    "her own machine never saw her move");

        // Something put in her pack on the host shows in her pack on her machine (read-only).
        var monocle = game.Place.Make("AnezkaLetter")!;
        monocle.Item!.Location = Rpg.Slot.Inventory;
        monocle.Owner = hers;
        hers.Actor!.Inventory.Add(monocle.Item);
        Assert.True(Until(() => me.Thing.Actor!.Inventory.Items.Any(i => i.Thing?.Template == "AnezkaLetter"),
                          () => game.Step(), () => anna.Step()), "the pack never followed");
    }

    /// <summary>
    /// **A player can drive** (only the host could; on Erik's machine only clicks walked).
    /// `[qol] driving` is ours, and 1.1's network has nothing for it: the player's machine sends its
    /// push (`GameClient.Drive`) and the host walks the character with it, as its own keyboard walks
    /// its own; a push that stops coming stops the character.
    /// </summary>
    [Fact]
    public void A_player_drives_their_character_with_the_keyboard_and_the_host_walks_it()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        using var game = Host(files, settings);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.State == ClientState.Playing && anna.Client.Avatar is not null,
                          () => game.Step(), () => anna.Step()), $"never played: {anna.Client.State} {anna.Client.Reason}");
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        var from = hers.Position;

        // Two seconds of W held, the push sent as the frames go by.
        var push = new Vector2(1f, 0f);
        // (Held until she has gone 40, at most twice as long: under the simulator a share of the pushes is lost on the way)
        // The frames keep a game's pace on the wall clock. Her machine sends the push again every 100 ms of the wall clock
        // and the host holds one for 300 ms of its own frames; run as fast as they go (about 3 ms a frame) a push held only
        // 10 frames of every 30, and one lost under the simulator left one hold in 60 frames, 38 units (the cloud's an earlier build run).
        var paced = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120 && (i < 60 || Vector3.Distance(hers.Position, from) <= 40f); i++)
        {
            anna.Client.Drive(push, run: false);
            game.Step();
            anna.Step();
            Paced(paced, i + 1);
        }
        float moved = Vector3.Distance(hers.Position, from);
        Assert.True(moved > 40f, $"the host moved her only {moved:0.#}");
        // And she walks as she goes, not stands: her mind's own follow, with no route of its own, put her back to standing
        // every frame before the push walked her again — her walk started over each frame, so she slid, on the host and on
        // every machine that saw her (seen in play). A driven walker's follow leaves the walk to the push.
        var walker = game.Place.WalkerFor(hers)!;
        Assert.True(walker.Driven);
        walker.Follow(1f / 30f);
        Assert.Equal(walker.WalkSpeed, walker.Speed);

        // Let go: nothing more is sent, and she stops once the push goes stale.
        anna.Client.Drive(Vector2.Zero, run: false);
        for (int i = 0; i < 20; i++) { game.Step(); anna.Step(); }
        var stopped = hers.Position;
        for (int i = 0; i < 30; i++) { game.Step(); anna.Step(); }
        Assert.True(Vector3.Distance(hers.Position, stopped) < 1f, "she kept going after the keys were let go");
        Assert.False(walker.Driven);
        Assert.Equal(0f, walker.Speed);
    }

    /// <summary>
    /// **Whose switch it is**: the ones that change the game are the host's and
    /// reach every player in the welcome — here the host has driving off and bodycollision on — while
    /// the purely visual ones and the controls stay each machine's own and are not sent at all. And
    /// the host's `driving` governs: with it off, a player's push walks nobody.
    /// </summary>
    [Fact]
    public void The_host_s_gameplay_switches_reach_the_players_and_their_own_visual_ones_stay_theirs()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        var hostQol = new Qol { Driving = false, Camera = false };
        var hostFixes = new Fixes { BodyCollision = true, GlowColours = true };
        using var game = Host(files, settings, hostQol, hostFixes);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.State == ClientState.Playing && anna.Client.Avatar is not null,
                          () => game.Step(), () => anna.Step()), $"never played: {anna.Client.State} {anna.Client.Reason}");

        var sent = anna.Client.FromHost;
        Assert.Contains(new HostSwitches.Setting("qol", "driving", false), sent);
        Assert.Contains(new HostSwitches.Setting("fixes", "bodycollision", true), sent);
        Assert.DoesNotContain(sent, s => s.Name is "camera" or "glowcolours" or "keys" or "soakpie" or "matchedgait" or "movespeed" or "alttab" or "winkey" or "liverollover");

        // Laid over a player's own: the host's gameplay switches win, the player's own visual ones stay.
        var mineQol = new Qol();                        // everything on, as a player's default
        var mineFixes = new Fixes { MoveSpeed = true, MatchedGait = true };   // the pair: matchedgait refines movespeed
        HostSwitches.Apply(sent, mineFixes, mineQol);
        Assert.False(mineQol.Driving);
        Assert.True(mineFixes.BodyCollision);
        Assert.True(mineQol.Camera);                    // the host's is off; the player's own stays on
        Assert.True(mineFixes.MatchedGait);
        Assert.False(mineFixes.GlowColours);            // the host's is on; the player's own stays off

        // And the host's driving switch governs its own walking of the player's character.
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        var from = hers.Position;
        for (int i = 0; i < 60; i++)
        {
            anna.Client.Drive(new Vector2(1f, 0f), run: false);
            game.Step();
            anna.Step();
            Thread.Sleep(2);
        }
        Assert.True(Vector3.Distance(hers.Position, from) < 1f, "the host walked her with its driving switch off");
    }

    /// <summary>
    /// What a host sends is laid over by name, and only a switch the registry declares the host's: `all` is no
    /// switch's name (it had been honoured, flipping the machine's own with the rest), a machine's own is left
    /// alone and so is a name this build does not have. And the pair is settled afterwards: a combination the
    /// registry refuses does not stay in force because a host's switches arrived.
    /// </summary>
    [Fact]
    public void A_host_s_switch_is_laid_over_by_its_own_name_only_and_the_result_is_settled()
    {
        var fixes = new Fixes { GlowColours = true };
        var qol = new Qol();
        HostSwitches.Apply([new("fixes", "all", true), new("qol", "all", false), new("fixes", "nosuchfix", true), new("qol", "camera", false),
                            new("repairs", "droplanding", false), new("fixes", "GunReach", true)], fixes, qol);
        Assert.Equal("glowcolours, gunreach", fixes.ToString());
        Assert.Equal(new Qol().ToString(), qol.ToString());

        // A player's own pair the registry refuses (set by hand here; the settings chain no longer produces one).
        var odd = new Fixes { MatchedGait = true };
        HostSwitches.Apply([new("fixes", "gunreach", true)], odd, new Qol());
        Assert.Equal("gunreach", odd.ToString());
    }

    /// <summary>Every switch of both registers is governed one way or the other, the visual ones by each machine.</summary>
    [Fact]
    public void Only_the_visual_switches_and_the_controls_are_each_machine_s_own()
    {
        var machine = new Fixes().All.Select(f => ("fixes", f.Name)).Concat(new Qol().All.Select(q => ("qol", q.Name)))
                                 .Where(k => HostSwitches.Of(k.Item1, k.Name) == HostSwitches.Governor.Machine)
                                 .Select(k => k.Name).Order().ToArray();
        Assert.Equal(["alttab", "camera", "filmvolume", "freecamera", "glowcolours", "keys", "liverollover", "matchedgait", "movespeed", "relock", "skipopening", "skipstoryfilms", "soakpie", "whisperecho", "winkey"], machine);
    }

    [Fact]
    public void Two_players_see_each_other_and_hear_each_other()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        using var game = Host(files, settings);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));

        // Each machine has the other's character, made from its template.
        int bobs = game.Server.Players.Single(p => p.Name == "Bob").Avatar!.Guid;
        Assert.True(Until(() => anna.Client.Mirror!.Find(bobs) is { InWorld: true }, All), "Anna never saw Bob");
        Assert.Equal("MP_Erik", anna.Client.Mirror!.Find(bobs)!.Template);

        // A plain line reaches both, in 1.1's words; a whisper only its two ends.
        anna.Client.Say("hello");
        // (Any line of his, not his first: under the simulator another line of the room may come first)
        Assert.True(Until(() => bob.Heard.Any(h => h.Contains("hello")), All));
        Assert.Contains("Anna", bob.Heard.First(h => h.Contains("hello")));
        bob.Client.Say("/anna meet me upstairs");
        Assert.True(Until(() => anna.Heard.Any(h => h.Contains("upstairs")) && bob.Heard.Any(h => h.Contains("upstairs")), All));   // and the sender's own copy

        // The same name twice is refused in 1.1's words.
        using var again = Join(files, game, "Anna", "MP_Kazi");
        Assert.True(Until(() => again.Client.State == ClientState.Gone, All, () => again.Step()));
        Assert.Equal("UI_NET_JOINDUPLICATE", again.Client.Reason);
    }

    [Fact]
    public void A_locked_game_turns_newcomers_away()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        using var game = Host(files, settings with { Flags = settings.Flags | GameFlags.Locked });
        using var late = Join(files, game, "Late", "MP_Kazi");
        Assert.True(Until(() => late.Client.State == ClientState.Gone, () => game.Step(), () => late.Step()));
        Assert.Equal("UI_NET_JOINCLOSED", late.Client.Reason);
    }

    [Fact]
    public void When_the_host_travels_every_player_loads_the_new_place_and_is_made_again_there()
    {
        using var files = Install.Open();
        var (settings, _) = Salon(files);
        using var game = Host(files, settings);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        Assert.True(Until(() => anna.Client.Avatar is not null, () => game.Step(), () => anna.Step()));

        // The chronicle takes the game to Temesvar's streets (1.1's messages 0x3e/0x3f).
        var street = Location.Load(files, "p1_nqtr", scene: "NQTR_CTD_1_1", entrance: 0);
        game.Place = street;
        game.Sim = new Simulation(street);
        game.Server.Travel(street, null);
        Assert.True(Until(() => anna.Place?.Name.Equals("p1_nqtr", StringComparison.OrdinalIgnoreCase) == true && anna.Client.Avatar is not null,
                          () => game.Step(), () => anna.Step()), $"never followed: {anna.Client.State}, in {anna.Place?.Name}");
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.Same(street, hers.Place);
        Assert.Equal("p1_nqtr", game.Server.Settings.Level, ignoreCase: true);
    }

    /// <summary>A walkable point about <paramref name="distance"/> from <paramref name="from"/>.</summary>
    static Vector3 FloorNear(Location place, Vector3 from, float distance)
    {
        var map = place.Walk!;
        for (int a = 0; a < 16; a++)
        {
            float r = a * MathF.PI / 8;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * distance;
            int node = map.Find(p, 256f);
            if (map.IsGround(node) && map.Nodes[node].Contains(new Vector2(p.X, p.Y))) return p with { Z = map.Nodes[node].Height(p.X, p.Y) };
        }
        return from;
    }
}
