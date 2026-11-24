// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's side of a network game: the eight slots, the joins and leaves, the chat, the orders
// carried out, and the world sent. 1.1's (the join, the chat and
// the orders) is over DirectPlay; this one is ours over `NetPeer`, doing the same
// things in the same words — the refusals and the room's messages are 1.1's own string keys.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>One person in the game.</summary>
public sealed partial class NetPlayer
{
    public required int Slot { get; init; }
    public required NetCharacter Character { get; set; }
    /// <summary>The connection to their machine; null for the host's own seat.</summary>
    public Connection? Link { get; init; }
    /// <summary>Their character in the host's world, once they have one.</summary>
    public WorldThing? Avatar { get; set; }
    /// <summary>Their machine has the place loaded and is being sent the world.</summary>
    public bool Ready { get; set; }
    public bool Host => Link is null;
    /// <summary>The player's name as the room and the log say it: its character's, or (ours) "Player n" while it is still choosing one.</summary>
    public string Name => HasCharacter ? Character.Name : $"Player {Slot + 1}";
    /// <summary>
    /// Whether the player has a character the host accepted (ours, protocol 23: the knock carries none, and a player chooses
    /// one in the room, <see cref="Msg.Choose"/>); a slot's character has no name until then.
    /// </summary>
    public bool HasCharacter => Character.Name.Length > 0;

    /// <summary>When their round trip was last said in the log (<see cref="RoundTrips"/>; the transport's clock), 0 before.</summary>
    internal long RoundTripSaid;

    /// <summary>The key of their round trip's line: the host's side, their slot and name.</summary>
    internal string RoundTripKey => $"host, slot {Slot}: {Name}";

    /// <summary>
    /// The conversation a script started for this player, which their machine is running (kept in the
    /// player's slot; `HostScenes`). It runs nothing here: it is what the script's callbacks
    /// are wired to, fired as the player's machine reports.
    /// </summary>
    public Rpg.Conversation? Talking { get; set; }
    /// <summary>The two things a script's `SetupCutscene` named for this player's camera, sent with the next shot (kept in their camera record).</summary>
    public int CutA { get; set; }
    /// <inheritdoc cref="CutA"/>
    public int CutB { get; set; }
    /// <summary>The script's `pathended`, for the camera path playing on this player's machine (kept in their camera record).</summary>
    public Action? PathEnded { get; set; }
    /// <summary>The host's hands for this player's pack, which make the moves their machine sends (`HostPack`).</summary>
    public Ui.InventoryHands? Hands { get; set; }

    /// <summary>The keyboard's push their machine last sent (`Msg.Drive`), in world x and y, at most one long.</summary>
    public System.Numerics.Vector2 DrivePush { get; set; }
    /// <summary>Whether that push is a run (shift held).</summary>
    public bool DriveRun { get; set; }
    /// <summary>When the push came (the host's clock); a push not renewed goes stale (<see cref="GameServer.DriveStaleMs"/>).</summary>
    public int DriveAtMs { get; set; }
    /// <summary>Whether the host is walking the character by that push, so that letting go stops it once.</summary>
    public bool Driving { get; set; }

    /// <summary>
    /// The player has joined and their character has not yet arrived anywhere: the first character
    /// made for them carries 1.1's "just joined" bit (`flags2 | 0x400`), which the arrival
    /// turns into the scripts' `joined` and clears.
    /// </summary>
    public bool Joining { get; set; } = true;

    // 1.1 sends a slot's network name as its own message (1.1's 0x4f), redrawn by the room
    // when it comes); here `Name` rides along on `Welcome`/`Joined` instead.
    public PlayerInfo Info(bool storyteller) =>
        new(Slot, Character.Name, Character.Template, Avatar?.Guid ?? 0, Host, Host && storyteller);
}

/// <summary>The host.</summary>
public sealed partial class GameServer : IDisposable
{
    readonly NetPeer peer;
    readonly NetPlayer?[] slots = new NetPlayer?[GameSettings.Slots];
    readonly Random dice;
    WorldWatch? watch;
    int nextSnapshot, nextSheet;

    /// <summary>
    /// The "just joined" bit 1.1's join sets on the character it makes for a player (bit 0x400
    /// of its second flags word). The player's first arrival tests it, fires every script's `joined` with the character's GUID and clears it.
    /// </summary>
    public const uint JoinedFlag2 = 0x400;

    /// <summary>How often the world is sent (ours: ten times a second).</summary>
    public const int SnapshotMs = 100;

    /// <summary>
    /// How often this host sends the world (<see cref="SnapshotMs"/>; twenty a second, 50, was measured against it:
    /// <c>NetRateTests</c>).
    /// </summary>
    public int SnapshotEveryMs { get; set; } = SnapshotMs;

    /// <summary>How many times this host has sent the world (the measured rate).</summary>
    public int SnapshotsSent { get; private set; }

    /// <summary>
    /// Whether the snapshots keep their interval whatever the frame's length (measured, <c>NetRateTests</c>): **on**, the
    /// default, so ten a second is ten. Off is the earlier schedule, each interval counted from the frame that sent the last (7.6 to
    /// 8.9 a second at 33 ms frames), kept for the measure's comparison.
    /// </summary>
    public bool Steady { get; set; } = true;
    /// <summary>How often each player's own sheet is sent (ours).</summary>
    public const int SheetMs = 500;

    /// <summary>This host's build and data, which every joining machine's must be (<see cref="NetIdentity"/>).</summary>
    public NetIdentity Identity { get; init; } = NetIdentity.Unstated;

    /// <param name="host">The host's own character, or null for a server nobody plays on (`--headless-server`).</param>
    /// <param name="plays">
    /// Whether the host plays: by default when it has a character. The lobby's Host Game makes a game whose host plays but has
    /// chosen nobody yet (ours): its slot's character has no name until <see cref="ChooseHost"/>.
    /// </param>
    public GameServer(GameSettings settings, NetPeer peer, NetCharacter? host = null, int seed = 1, bool? plays = null)
    {
        Settings = settings;
        this.peer = peer;
        peer.Listening = true;
        peer.Log ??= line => Log?.Invoke(line);                          // the transport's own lines
        dice = new Random(seed);
        HostPlays = plays ?? host is not null;
        slots[0] = new NetPlayer { Slot = 0, Character = host ?? new NetCharacter { Name = HostPlays ? "" : "Server", Template = HostPlays ? "" : "MP_Christof_DA" } };
        Scenes = new HostScenes(this);
        Texts = new HostTexts(this);
        Shops = new HostShops(this);
        Arms = new HostArms(this);
    }

    /// <summary>The network arms of the single-player pieces (the throw, dropped money; `Net.NetArms`).</summary>
    public HostArms Arms { get; }

    /// <summary>The shops a script opens for a player, on their machine (1.1's 0x42 / 0x43).</summary>
    public HostShops Shops { get; }

    /// <summary>The read pane, the message lines, the quests and the advancement a script shows one player.</summary>
    public HostTexts Texts { get; }

    /// <summary>The Storyteller's scene index, which the scene pane greys by and the scripts' `GetNSSIndex`/`SetNSSIndex` read and move.</summary>
    public SceneIndex SceneIndex { get; } = new();

    /// <summary>
    /// A player's character has arrived in the host's place and their machine has been sent the world
    /// (the end of the arrival): the scripts hear `joined(guid)` when it is the player's first arrival
    /// since joining (the second argument — the "just joined" bit, cleared here) and then
    /// `beginscene(guid, 0)`, every script of the place and the chronicle's.
    /// </summary>
    public event Action<NetPlayer, bool>? Entered;

    /// <summary>
    /// A player is leaving or has dropped: the scripts hear
    /// `disconnected(guid)` while their character is still in the place. No shipped script listens.
    /// Their teleporter pairs are shut (`HostPlaces`).
    /// </summary>
    public event Action<NetPlayer>? Departing;

    /// <summary>The conversations and cutscenes a script starts for a player, sent to their machine.</summary>
    public HostScenes Scenes { get; }

    /// <summary>What each machine sees of the others in the host's place: kit, effects, flags, blows, sounds, shots (`HostSeen`).</summary>
    public HostSeen? Seen { get; private set; }

    /// <summary>
    /// A line of a player's conversation the host's own character stands near enough to overhear
    /// (the copy the player's machine sends the host): whether it is a spoken line (else a reply), the name to show
    /// and the line's id.
    /// </summary>
    public event Action<bool, string, string>? Overheard;
    internal void HearLine(bool spoken, string name, string id) => Overheard?.Invoke(spoken, name, id);
    internal void Reliable(NetPlayer p, byte[] message) => Send(p, Channel.Reliable, message);
    internal void Note(string line) => Log?.Invoke(line);

    public NetPeer Peer => peer;
    public GameSettings Settings { get; private set; }
    public GameFlags Flags
    {
        get => Settings.Flags;
        set => Settings = Settings with { Flags = value };
    }
    /// <summary>Whether the host has a character of their own in the world (a listen server).</summary>
    public bool HostPlays { get; }
    /// <summary>The slots, the host's first (eight of them).</summary>
    public IReadOnlyList<NetPlayer?> Slots => slots;
    public IEnumerable<NetPlayer> Players => slots.OfType<NetPlayer>();
    public bool Started { get; private set; }
    /// <summary>The host's world, once the game is under way.</summary>
    public Location? Place { get; private set; }
    /// <summary>The game pause — the Storyteller's, for everyone.</summary>
    public bool Paused { get; private set; }
    /// <summary>Whether the host sits in the Storyteller's seat.</summary>
    public bool Storyteller { get; set; }
    /// <summary>The body the Storyteller has possessed, which answers its player `UI_NET_POSSESSED`.</summary>
    public WorldThing? Possessed { get; set; }
    /// <summary>The host's clock, ms, which the snapshots carry.</summary>
    public int NowMs { get; private set; }

    /// <summary>A line for the host's own message box, in words, and its colour.</summary>
    public event Action<string, uint>? Posted;
    /// <summary>A chat line for the host's own chat box, already shown in words.</summary>
    public event Action<ChatLine, string>? Heard;
    /// <summary>A line only the host's own chat box shows, in words, and its colour (<see cref="HostLine"/>).</summary>
    public event Action<string, uint>? HostChat;
    /// <summary>The console's account of what the host is doing.</summary>
    public event Action<string>? Log;
    /// <summary>Somebody's character came into the host's world.</summary>
    public event Action<NetPlayer>? Arrived;

    /// <summary>The string table, for showing chat on the host (null answers keys with themselves).</summary>
    public Func<string, string?> Find { get; set; } = _ => null;

    /// <summary>
    /// The host's Fixes and quality-of-life switches, which every player's machine takes for the ones
    /// that change the game (<see cref="HostSwitches"/>). Null reads the process's own
    /// (<see cref="World.Fixes.Current"/>, <see cref="World.Qol.Current"/>) at each use, which is what the
    /// game does; a test passes its own.
    /// </summary>
    public World.Fixes? Fixes { get; init; }
    /// <inheritdoc cref="Fixes"/>
    public World.Qol? Qol { get; init; }
    World.Fixes FixesInForce => Fixes ?? World.Fixes.Current;
    World.Qol QolInForce => Qol ?? World.Qol.Current;

    // ---- the game's life ------------------------------------------------------------------------

    /// <summary>
    /// Start: the host's game begins. Nobody else is sent anything yet: when the
    /// host's place is loaded (<see cref="Enter"/>) the players are told the game is under way, and each comes in by
    /// its own Start (GameServer.LetIn.cs, until then Vitomir sent everybody in the room the place at once).
    /// </summary>
    public void Start()
    {
        if (Started) return;
        Started = true;
        Log?.Invoke($"net: the game has started — {Settings.Level}{(Settings.Scene.Length > 0 ? "/" + Settings.Scene : "")}");
    }

    /// <summary>
    /// The host's world is up: it is watched from now on, and the host's own character (when it has
    /// one) takes slot 0. Everyone already waiting is sent it.
    /// </summary>
    public void Enter(Location place, WorldThing? hostAvatar)
    {
        Started = true;
        Place = place;
        Settings = Settings with { SceneGuid = place.SceneGuids.First };   // where the host's numbers start
        watch = new WorldWatch(place);
        Places.SetHome(place, watch);                               // the host's own place (GameServer.Places.cs)
        NetOrders.Attach(place, NetMode.Server);
        SceneRoutes.Attach(place, Scenes);
        NetAwards.Attach(place, this);
        Seen = HostSeen.Attach(place, this);
        if (slots[0] is { } me)
        {
            me.Avatar = hostAvatar;
            me.Ready = true;
            // The host's own character comes into the game with its sheet and kit, once.
            if (hostAvatar is not null)
            {
                if (me.Joining) me.Character.Apply(hostAvatar, place);
                else me.Character.Apply(hostAvatar);
                NetJoin.Teams(hostAvatar, 0, Flags);                    // the host's own is team 0
            }
            me.Joining = false;
        }
        foreach (var p in Players.Where(p => p.Ready && !p.Host).ToList()) Catch(p);
        UnderWay();                                                 // the stage to everybody
    }

    /// <summary>
    /// The whole game moves to another place (the chronicle's travel on the host; 1.1's messages 0x3e
    /// The host's world is the new place, every other player's
    /// character is taken out of the old one, and every machine is told to load the new one — each
    /// player's character is made again there when their machine says it is ready.
    /// </summary>
    public void Travel(Location next, WorldThing? hostAvatar)
    {
        if (Place is { } old)
        {
            NetOrders.Detach(old);
            SceneRoutes.Detach(old);
            NetAwards.Detach(old);
            HostSeen.Detach(old);
            foreach (var p in Players.Where(p => !p.Host))
                if (p.Avatar is { InWorld: true } body && body.Place is { } there)       // wherever each one is (HostPlaces)
                {
                    // **Ours**: 1.1's character goes with its player; here it is made again at the
                    // arrival, so it takes its sheet and pack along as it stands now.
                    p.Character = p.Character.Keep(body);
                    there.Remove(body, drop: false);
                }
        }
        string scene = next.SceneFile.Equals(next.Name, StringComparison.OrdinalIgnoreCase) ? "" : next.SceneFile;
        Settings = Settings with { Level = next.Name, Scene = scene, Entrance = Math.Max(0, next.Entrance), SceneGuid = next.SceneGuids.First };
        Place = next;
        watch = new WorldWatch(next);
        Places.SetHome(next, watch);
        NetOrders.Attach(next, NetMode.Server);
        SceneRoutes.Attach(next, Scenes);
        NetAwards.Attach(next, this);
        Seen = HostSeen.Attach(next, this);
        Possessed = null;
        foreach (var p in Players)
        {
            // A conversation does not travel (its speakers belong to the place being left). A player's
            // is kept until their machine reports it abandoned (the conversation's client arm, `Net.ClientArms`),
            // so the script hears `convended(aborted)`; one never reported is let go at their arrival.
            if (p.Host) p.Talking = null;
            p.PathEnded = null;
            if (p.Host) { p.Avatar = hostAvatar; p.Ready = true; if (hostAvatar is not null) { p.Character.Apply(hostAvatar); NetJoin.Teams(hostAvatar, 0, Flags); } continue; }
            if (!p.LetIn) continue;                                 // a player still in the room stays there (only a slot in state 2 goes)
            p.Avatar = null;
            p.Ready = false;
            p.Place = next;
            p.Entrance = -1;
            var w = new NetWriter(Msg.Travel);
            Settings.Write(w);
            HostSwitches.Write(w, FixesInForce, QolInForce);      // again, in case the host's page changed them
            Send(p, Channel.Reliable, w.ToArray());
        }
        Log?.Invoke($"net: the game moves to {next.Name}{(scene.Length > 0 ? "/" + scene : "")} at entrance {Settings.Entrance}");
    }

    /// <summary>
    /// One turn: the wire read and every message dealt with; then, when it is time, what came and went
    /// and the snapshot sent to everyone playing.
    /// </summary>
    public void Poll(int nowMs)
    {
        NowMs = nowMs;
        foreach (var e in peer.Poll())
        {
            switch (e.Kind)
            {
                case NetEventKind.Datagram when e.From is not null:
                    Query(e.From, e.Data);
                    break;
                case NetEventKind.Message when e.Connection is not null:
                    // Whatever a player's machine sends that does not read is dropped and counted, never thrown out of
                    // the host's loop; a machine that keeps sending it is closed (NetPeer.Nonsense).
                    try { Receive(e.Connection, new NetReader(e.Data)); }
                    catch (Exception x) when (x is not OutOfMemoryException)
                    {
                        string kind = e.Data.Length > 0 ? ((Msg)e.Data[0]).ToString() : "empty";
                        if (peer.Nonsense(e.Connection, $"{kind}: {x.GetType().Name}"))
                            Log?.Invoke($"net: a message from {e.Connection.Remote} dropped, it does not read ({kind}, {x.GetType().Name}: {x.Message.Split('\n')[0]})");
                    }
                    break;
                case NetEventKind.Disconnected when e.Connection is not null:
                    if (e.Connection.Tag is NetPlayer gone) Leave(gone, "UI_NET_HASDISCONNECTED");
                    break;
            }
        }
        // Each joined player's round trip, said once a minute (RoundTrips).
        foreach (var p in Players)
            if (p.Link is { Open: true, Closed: false } link && RoundTrips.Due(link, peer.LastPolledMs, ref p.RoundTripSaid))
                Log?.Invoke(RoundTrips.Note(p.RoundTripKey, RoundTrips.Line($"to {p.Name}", link, peer.LastPolledMs)));
        float stepped = lastPollMs < 0 ? 0f : (nowMs - lastPollMs) / 1000f;
        lastPollMs = nowMs;
        if (Place is not null && stepped > 0f)
            foreach (var p in Players) if (PlaceOf(p) is { } driven) Steer(driven, p, stepped);
        if (watch is null || Place is null) return;
        if (nowMs >= nextSnapshot)
        {
            // Steady (measured): the next one due an interval after this one was due, not after the frame that sent it, so the
            // rate is the interval's whatever the frame's length (a frame's 33 ms made the earlier "ten a second" 7.6); a host that has
            // fallen a whole interval behind starts again from now.
            nextSnapshot = Steady && nowMs - nextSnapshot < SnapshotEveryMs ? nextSnapshot + SnapshotEveryMs : nowMs + SnapshotEveryMs;
            // Each place's world to the players in it, the host's own first; and what each
            // machine sees of the others there (HostSeen, one for each of the host's places).
            SnapshotsSent++;
            foreach (var h in Places.All.ToArray())
            {
                var (at, seen) = (h.Place, h.Watch);
                // 1.1 announces a pick-up (message 0xb) and a drop
                // at a point (message 0x1c) as their own events; here
                // an item leaving the world is simply what `removed` carries (its pack shows in the next
                // Sheet) and one appearing is what `made` carries below.
                var (made, removed) = seen.Changes();
                foreach (var m in made) InPlace(at, Channel.Reliable, Made(m));
                foreach (var f in seen.Fired) { var w = new NetWriter(Msg.Fired); f.Write(w); InPlace(at, Channel.Reliable, w.ToArray()); }
                foreach (var m in MadeHere.Take(at)) InPlace(at, Channel.Reliable, m);     // a gib kill before its body goes
                foreach (var g in removed) InPlace(at, Channel.Reliable, new NetWriter(Msg.Removed).I32(g).ToArray());
                foreach (var (m, only) in HostSeen.For(at)?.Changes() ?? [])
                    if (only is null) InPlace(at, Channel.Reliable, m); else Send(only, Channel.Reliable, m);
                if (In(at).Any(p => p.Ready && p.Link is not null))
                    foreach (var part in seen.Snapshot(nowMs)) InPlace(at, Channel.Unreliable, part);
            }
        }
        if (nowMs >= nextSheet)
        {
            nextSheet = nowMs + SheetMs;
            foreach (var p in Players.Where(p => p.Ready && p.Link is not null && p.Avatar is not null))
                Send(p, Channel.Reliable, Sheet(p.Avatar!, NowMs));
            Shops.Purses();
        }
    }

    // ---- messages -------------------------------------------------------------------------------

    void Receive(Connection c, NetReader r)
    {
        var who = c.Tag as NetPlayer;
        switch (r.Kind)
        {
            case Msg.Hello: Hello(c, r); break;
            case Msg.Bye when who is not null: Leave(who, "UI_NET_HASDISCONNECTED"); peer.Disconnect(c); break;   // the host has one line for a going
            case Msg.Ready when who is not null: Ready(who); break;
            case Msg.LetIn when who is not null: AnswerStart(who); break;       // a player's Start
            case Msg.Say when who is not null: Say(who, r.Str()); break;
            case Msg.Order when who is not null: Order(who, NetOrder.Read(r)); break;
            case Msg.Drive when who is not null: Drive(who, r); break;
            case Msg.Revive when who is not null: Revive(who); break;
            case Msg.Cast when who is not null: Cast(who, NetCast.Read(r)); break;
            case Msg.Pack when who is not null: Pack(who, PackGesture.Read(r)); break;
            case Msg.VaultMove when who is not null: VaultMoved(who, VaultMove.Read(r)); break;   // GameServer.Vault.cs
            case Msg.Said when who is not null: Scenes.Said(who, r); break;
            case Msg.PathEnded when who is not null: Scenes.PathEnded(who); break;
            case Msg.TextEnded when who is not null: Texts.Closed(r); break;
            case Msg.Advanced when who is not null: Texts.Answered(who, r); break;
            case Msg.EffectCancel when who is not null: Seen?.Cancel(who, r); break;
            case Msg.ShopDeal when who is not null: Shops.Deal(who, r); break;
            case Msg.DropCash when who is not null: Arms.DropCash(who, r); break;
            case Msg.Choose or Msg.HasChronicle when who is not null: Received(who, r.Kind, r); break;   // the room (GameServer.Room.cs)
        }
    }

    /// <summary>
    /// A join (1.1's message 0x3b) and its answer (0x3c): the protocol, this machine's build and data, the lock, the password
    /// and the room are checked, in that order, and a refusal names 1.1's own reason; otherwise the newcomer takes the first
    /// free slot. **Ours (protocol 23)**: the knock carries no character — 1.1's join carries one and checks its name and template
    /// there — so the newcomer is in the room with a slot whose character has no name, and the name's and the template's checks
    /// are made when it chooses one there (<see cref="Choose"/>, GameServer.Room.cs), where the room hears `UI_NET_HASJOINED`.
    /// </summary>
    void Hello(Connection c, NetReader r)
    {
        if (c.Tag is NetPlayer) return;
        int protocol = r.I32();
        if (protocol != Protocol.Version)
        {
            // Nothing past the protocol is read from a Hello of another protocol (its layout is another's).
            peer.Send(c, Channel.Reliable, new NetWriter(Msg.Refused).Str("UI_NET_BADPROTOCOL").Str("").ToArray());
            Log?.Invoke($"net: a join from {c.Remote} refused — UI_NET_BADPROTOCOL (its protocol {protocol}, this host's {Protocol.Version})");
            return;
        }
        byte[] proof = r.Bytes(PasswordProof.Length);                       // proved, never sent (PasswordProof)
        var identity = NetIdentity.Read(r);
        string? differs = NetTape.Differs(identity.Differs(Identity));      // the build, then the data (NetIdentity); a replay takes the tape's answer
        string? refusal =
            differs is not null ? "UI_NET_BADPROTOCOL"
            : Flags.HasFlag(GameFlags.Locked) ? "UI_NET_JOINCLOSED"
            : Settings.Password.Length > 0 && NetTape.WrongPassword(!PasswordProof.Holds(Settings.Password, c.Binding, proof)) ? "UI_NET_WRONGGAMEPASSWORD"   // the tape's answer in a replay
            : Players.Count() >= Math.Clamp(Settings.MaxPlayers, 1, GameSettings.Slots) ? "UI_NET_JOINFULL"
            : null;
        if (refusal is not null)
        {
            peer.Send(c, Channel.Reliable, new NetWriter(Msg.Refused).Str(refusal).Str(differs ?? "").ToArray());
            Log?.Invoke($"net: a join from {c.Remote} refused — {refusal}{(differs is null ? "" : ": " + differs)}");
            return;
        }
        int slot = Array.FindIndex(slots, s => s is null);
        var player = new NetPlayer { Slot = slot, Character = new NetCharacter { Name = "", Template = "" }, Link = c };
        slots[slot] = player;
        c.Tag = player;
        var w = new NetWriter(Msg.Welcome).U8((byte)slot).U8(Stage);  // the stage, 0x33's first byte
        Settings.Write(w);                                          // the place comes with the let-in (GameServer.LetIn.cs)
        // 1.1 re-sends the whole slot table (0x33: each slot's state and GUID) and every name (0x4f)
        // to all or one; here the newcomer's `Welcome` carries every slot's `PlayerInfo`
        // (state, GUID and name together) at once, and `Joined`/`Left` carry later changes.
        var others = Players.ToList();
        w.U8((byte)others.Count);
        foreach (var p in others) p.Info(Storyteller).Write(w);
        HostSwitches.Write(w, FixesInForce, QolInForce);          // the switches that change the game are the host's
        peer.Send(c, Channel.Reliable, w.ToArray());
        foreach (var p in Players.Where(p => p != player)) Send(p, Channel.Reliable, Joined(player));   // a slot still choosing
        Log?.Invoke($"net: a player joined from {c.Remote}, slot {slot}, choosing a character");
    }

    /// <summary>
    /// A player's machine has the place loaded: their character is made, and they are sent the world.
    /// Marks the slot as arrived (state 3) during the newcomer sync.
    /// </summary>
    void Ready(NetPlayer p)
    {
        if (Place is null) { p.Ready = true; return; }       // sent the world when the host's is up
        if (p.Ready) return;
        p.Ready = true;
        Catch(p);
    }

    /// <summary>Makes a newcomer's character and sends them everything they need to join the world in progress.</summary>
    void Catch(NetPlayer p)
    {
        if (p.Link is not null && Newcomer(p)) return;                // to the revive location first
        if (PlaceOf(p) is not { } place || Places.Of(place)?.Watch is not { } seen || p.Link is null) return;
        if (p.Avatar is null || !p.Avatar.InWorld)
        {
            p.Avatar = SpawnFor(p);
            if (p.Avatar is not null) Arrived?.Invoke(p);
        }
        // Who is who first, so the newcomer knows its own character when it is announced.
        foreach (var other in Players) Send(p, Channel.Reliable, Joined(other, p));
        var (made, removed) = seen.CatchUp();
        foreach (var g in removed) Send(p, Channel.Reliable, new NetWriter(Msg.Removed).I32(g).ToArray());
        foreach (var m in made) Send(p, Channel.Reliable, Made(m));
        // Then what they hold and wear, their flags and effects (`HostSeen.CatchUp`).
        foreach (var m in (HostSeen.For(PlaceOf(p)) ?? Seen)?.CatchUp(p) ?? []) Send(p, Channel.Reliable, m);
        if (p.Avatar is { } a)
        {
            // Everyone else learns whose the new character is.
            foreach (var other in Players.Where(o => o != p)) Send(other, Channel.Reliable, Joined(p, other));
            Send(p, Channel.Reliable, Sheet(a, NowMs));
        }
        if (Paused) Send(p, Channel.Reliable, new NetWriter(Msg.Pause).Bool(true).U8(NetPane.Pause).ToArray());
        Texts.QuestList(p);
        // Last, the scripts (the arrival's end): `joined` for a player's first arrival, then `beginscene`
        // with their GUID — after everything above, so what a script opens for them finds their
        // machine with the world and its own character.
        if (p.Avatar is { Actor: { } arrived })
        {
            bool first = (arrived.Flags2 & JoinedFlag2) != 0;
            arrived.Flags2 &= ~JoinedFlag2;
            p.Joining = false;
            Places.Within(place, () => Entered?.Invoke(p, first));   // that place's scripts (HostPlaces)
        }
    }

    /// <summary>
    /// The character a player brings, made in the host's world at the arrival's start for their slot
    /// (four starts per entrance, `Location.Arrivals`; past the fourth they share). **Ours**: its mind is
    /// held (`THING_AF_AIPAUSED`) so the host's AI does not walk it about between its player's orders —
    /// what 1.1 does to keep a network player's mind still is not read.
    /// </summary>
    WorldThing? SpawnFor(NetPlayer p)
    {
        if (PlaceOf(p) is not { } place) return null;
        p.Place = place;
        // The entrance they are arriving at (kept in the slot), whatever entrance the place was built at.
        var starts = HostPlaces.Starts(place, p.Entrance);
        Vector3 at = place.StartPosition;
        float yaw = place.StartYaw;
        if (starts.Length > 0)
        {
            var s = starts[p.Slot % starts.Length];
            at = s.Position;
            yaw = MathF.Atan2(s.Rotation[1], s.Rotation[0]) * (180f / MathF.PI);
        }
        var made = place.Place(p.Character.Template, at, new Vector3(0, 0, yaw), place.At(at, 0));
        if (made is null)
        {
            Log?.Invoke($"net: no template {p.Character.Template} to make {p.Name} from");
            return null;
        }
        // The template's sheet first — its bounds, kit and disciplines — and then, for a character that
        // has played before, its own numbers, disciplines and pack over it (NetCharacter.Saved).
        if (Outfit.Dress(place, made) is { } dressed) Log?.Invoke($"net: {p.Name}'s sheet — {dressed}");
        p.Character.Apply(made, place);
        NetJoin.Teams(made, p.Slot, Flags);                         // the slot's team, or 0 in co-op
        if (p.Character.Saved is { } saved)
            Log?.Invoke($"net: {p.Name}'s own sheet — {saved.Disciplines.Count} disciplines, {saved.Pack.Count} items, {saved.Spendable} to spend");
        if (made.Actor is { } actor)
        {
            actor.Flags |= Actor.Flag.AiPaused;
            if (p.Joining) actor.Flags2 |= JoinedFlag2;
        }
        place.WalkerFor(made)?.Ground();
        Places.Arriving(p, made);                                   // beside a teleporter's end (HostPlaces)
        Log?.Invoke($"net: {p.Name} is {made.Template} (thing {made.Guid}) at {at.X:0},{at.Y:0},{at.Z:0}");
        return made;
    }

    void Leave(NetPlayer p, string key)
    {
        if (slots[p.Slot] != p) return;
        Departing?.Invoke(p);
        slots[p.Slot] = null;
        if (p.Avatar is { InWorld: true } body && body.Place is { } place) place.Remove(body, drop: false);
        foreach (var other in Players.Where(o => o.Link is not null))
            Send(other, Channel.Reliable, new NetWriter(Msg.Left).U8((byte)p.Slot).Str(key).ToArray());
        Room(key, p.Name, players: NetJoin.LeftForPlayers);        // the others say "has left"
        string how = key == "UI_NET_HASLEFT" ? "left" : "disconnected";
        if (p.Link is { } gone) Log?.Invoke(RoundTrips.Note(p.RoundTripKey, RoundTrips.Line($"to {p.Name}", gone, peer.LastPolledMs, $"; {p.Name} {how}")));
        Log?.Invoke($"net: {p.Name} {how}");
    }

    /// <summary>Kick: the player is sent away and their slot freed.</summary>
    public void Kick(int slot)
    {
        if (slot <= 0 || slot >= slots.Length || slots[slot] is not { Link: { } link } p) return;
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.Bye).Str("UI_STCPKICKBUTTON").ToArray());
        Leave(p, "UI_NET_HASLEFT");
        // The goodbye arrives before the connection goes (a lost or overtaken reason left the kicked player told only
        // "disconnected"): closed once it is acknowledged, or in two seconds; nothing it sends meanwhile is any player's.
        link.Tag = null;
        peer.DisconnectWhenDelivered(link, "kicked");
    }

    // ---- chat -----------------------------------------------------------------------------------

    /// <summary>The host's own line, as a player in slot 0 types it.</summary>
    public void Say(string typed) { if (slots[0] is { } me) Say(me, typed); }

    /// <summary>
    /// A typed line (1.1's message 7 read on the host): its kind from its first character — a lobby line when
    /// the speaker is a player with no character playing — and then sent to whoever should hear it
    /// (<see cref="ChatHearing"/>). A possessed character's player is not heard
    /// (<see cref="StoryChat.TriedToSay"/> on the host's box, `MP_SAYFAILED` to them). The host's machine shows
    /// the line, and moves the speaker's lips on its copy, only when it is one of those who hear it (the
    /// send to slot 0 is the host's own show); the moderated host's own copies go to its box (<see cref="StoryChat.HostCopy"/>).
    /// </summary>
    void Say(NetPlayer from, string typed)
    {
        if (typed.Length == 0) return;
        if (!from.Host && Possessed is not null && ReferenceEquals(Possessed, from.Avatar))
        {
            ShowHost(StoryChat.TriedToSay(from.Name, typed));                     // The host's box only
            Post(from, "MP_SAYFAILED", null, 0xffffffffu);
            return;
        }
        var names = slots.Select(s => s?.Name).ToList();
        bool playing = from.Host || from.Avatar is { InWorld: true };
        var line = NetChat.Parse(typed, from.Slot, names, Started && playing, n => dice.Next(n));
        if (line.To == -2)
        {
            Post(from, "UI_NET_WHISPERNOTFOUND", null, 0xffff4040u);
            return;
        }
        var speaker = ChatEar.Of(from, PlaceOf(from), Started);
        string? recipient = line.Kind == ChatKind.Whisper && line.To >= 0 ? slots[line.To]?.Name : null;
        bool echo = ChatHearing.Echoes(line, from.Slot, Flags);
        foreach (var p in Players)
        {
            bool hears = ChatHearing.Hears(line, speaker, ChatEar.Of(p, PlaceOf(p), Started), Flags);
            bool own = echo && p.Slot == from.Slot;                                // the sender's copy (QoL `whisperecho`)
            if (!hears && !own) continue;
            string? to = own ? recipient : null;
            if (p.Link is null)
            {
                if (own && !QolInForce.WhisperEcho) continue;
                Heard?.Invoke(line, NetChat.Show(line.Kind, from.Name, line.Text, to, Find));
                if (ChatNoise.For(line.Kind) is int noise) Audio.Noise.Ui(Place, noise);          // the chat noise, rolled
                // An emote's gesture plays here, as the host shows the line (`ChatGesture`);
                // any other line moves the speaker's lips (for 3,000 ms, `Anim.ChatLips`).
                if (!ChatGesture.Play(from.Avatar, line.Kind, line.Text, Find, PlaceOf(from)?.Motions)
                    && from.Avatar is { Type: ThingType.Actor or ThingType.Player } lips && PlaceOf(from) is { } at)
                    Anim.ChatLips.Start(lips, at.Milliseconds, at.Motions);
            }
            else
                Send(p, Channel.Reliable, new NetWriter(Msg.Chat).U8((byte)line.Kind).U8((byte)from.Slot)
                    .Str(from.Name).Str(line.Text).Str(to ?? "").I32(from.Avatar?.Guid ?? 0).ToArray());
        }
        int? hostTeam = from.Slot == 0 ? speaker.Team : slots[0]?.Avatar is { InWorld: true, Actor: { } host } ? host.Team : null;
        if (StoryChat.HostCopy(line, from.Name, speaker.Team, recipient, hostTeam, Flags) is { } copy)
            ShowHost(copy);
    }

    /// <summary>A line for the host's chat box alone.</summary>
    void ShowHost(HostLine line) => HostChat?.Invoke(Text(line.Key, line.Args), line.Colour);

    // ---- orders ---------------------------------------------------------------------------------

    /// <summary>
    /// An order: only for the sender's own character, and never while the game is paused
    /// (`UI_STSBPAUSE`) or the Storyteller has the body (`UI_NET_POSSESSED`). Then the same order
    /// function a click on this machine would call.
    /// </summary>
    void Order(NetPlayer from, NetOrder order)
    {
        if (PlaceOf(from) is not { } place || from.Avatar is not { } me || order.Actor != me.Guid) { Answer(from, order, OrderResult.Refused); return; }
        if (ReferenceEquals(Possessed, me)) { Post(from, "UI_NET_POSSESSED", null, 0xffffffffu); Answer(from, order, OrderResult.Refused); return; }
        if (Paused) { Post(from, "UI_STSBPAUSE", null, 0xffffffffu); Answer(from, order, OrderResult.Refused); return; }
        var result = OrderResult.Refused;
        Places.Within(place, () => result = ServerOrders.Apply(place, me, order, NowMs));
        Answer(from, order, result);
        Log?.Invoke($"net: {from.Name} {order.Kind}{(order.Target != 0 ? $" on {order.Target}" : "")} — {result}");
    }

    /// <summary>
    /// A numbered order answered to its sender (protocol 21; <see cref="Msg.Taken"/>): its number, what came of it and this
    /// host's time (the snapshots' clock), for her machine's own walk (<see cref="OwnWalk"/>). An order with no number is not.
    /// </summary>
    void Answer(NetPlayer to, NetOrder order, OrderResult result)
    {
        if (order.Seq == 0) return;
        Send(to, Channel.Reliable, new NetWriter(Msg.Taken).U16(order.Seq).U8((byte)result).I32(NowMs).ToArray());
    }

    /// <summary>
    /// A numbered cast answered to its sender (protocol 22; <see cref="Msg.Taken"/>, the orders' answer with three more): its
    /// number, what came of it, this host's time, **why** (<see cref="CastWhy"/>), the discipline's recast clock left (−1: none, a
    /// scroll's or no such discipline) and how long the cast holds her (0: fired at once, or not begun) — for her own cast's start
    /// predicted on her machine (<see cref="OwnCast"/>). A cast with no number is not answered, as 1.1's is not.
    /// </summary>
    void Answer(NetPlayer to, NetCast cast, OrderResult result, CastWhy why)
    {
        if (cast.Seq == 0) return;
        int recast = -1, busy = 0;
        if (to.Avatar is { Actor: { } a, Place: { } place } && cast.Scroll == 0)
        {
            int now = place.Milliseconds;
            if (a.Disciplines[cast.Discipline] is { } d) recast = RecastLeft(d, now);
            if (result == OrderResult.Started && why == CastWhy.Begun && a.Action == ActionMode.Cast) busy = Math.Max(0, a.BusyUntil - now);
        }
        Send(to, Channel.Reliable, new NetWriter(Msg.Taken).U16(cast.Seq).U8((byte)result).I32(NowMs)
            .U8((byte)why).I32(recast).I32(busy).ToArray());
    }

    /// <summary>How long a discipline's recast clock has to run at <paramref name="now"/> (the place's milliseconds), 0 when it is over.</summary>
    static int RecastLeft(Discipline d, int now) => (uint)now < (uint)d.RecastUntil ? d.RecastUntil - now : 0;

    // ---- driving (ours) ----------------------------------------------------------------------------

    /// <summary>How long a player's push lasts without being renewed (ours): their machine sends it ten times a second while a key is held.</summary>
    public const int DriveStaleMs = 300;
    int lastPollMs = -1;

    /// <summary>
    /// **A player driving their own character** (`[qol] driving` — ours; 1.1 has no keyboard walk, so
    /// its network has nothing to carry one). The push is kept as it comes, their own character only,
    /// and never longer than a full push.
    /// </summary>
    void Drive(NetPlayer from, NetReader r)
    {
        int actor = r.I32();
        var push = new System.Numerics.Vector2(r.F32(), r.F32());
        bool run = r.Bool();
        if (from.Avatar is not { } me || actor != me.Guid) return;
        if (!float.IsFinite(push.X) || !float.IsFinite(push.Y)) return;
        from.DrivePush = push.LengthSquared() > 1f ? System.Numerics.Vector2.Normalize(push) : push;
        from.DriveRun = run;
        from.DriveAtMs = NowMs;
    }

    /// <summary>
    /// Each turn, a driving player's character is walked by their push as the host's own keyboard walks
    /// the host's (the same `Player.Move`, the same rule that nothing moves while busy or held) — and
    /// stopped once when the push ends or goes stale. Refused as an order is: not while paused, not
    /// while the Storyteller has the body, not for the dead.
    /// </summary>
    void Steer(Location place, NetPlayer p, float seconds)
    {
        if (p.Link is null || p.Avatar is not { Actor: { } actor } me || place.WalkerFor(me) is not { } walker) return;
        bool held = p.DrivePush.LengthSquared() > 1e-6f && NowMs - p.DriveAtMs <= DriveStaleMs
                    && QolInForce.Driving                          // the host's switch (HostSwitches)
                    && !Paused && !ReferenceEquals(Possessed, me) && !actor.Dead;
        if (!held)
        {
            walker.Driven = false;                                // its own follow stands it again (Player.Follow)
            if (p.Driving && !walker.Walking) walker.Move(System.Numerics.Vector2.Zero, seconds);
            p.Driving = false;
            return;
        }
        walker.Driven = true;                                     // the push's walk, not put back to standing each frame
        if (!Actions.ReadyToMove(me)) { walker.Move(System.Numerics.Vector2.Zero, seconds); return; }
        walker.Running = p.DriveRun;
        walker.Move(p.DrivePush, seconds);                       // the keyboard cancels a route
        p.Driving = true;
    }

    /// <summary>
    /// A discipline cast (1.1's 0x18): only the sender's own character (ours), and **silently refused
    /// while it is the host's steered body** — the Storyteller's possessed one. Unlike an
    /// order 1.1 answers nothing and does not ask about the pause: a cast during the Storyteller's
    /// pause is taken (<see cref="NetStoryCast.Refused"/>).
    /// </summary>
    void Cast(NetPlayer from, NetCast cast)
    {
        if (PlaceOf(from) is not { } place || from.Avatar is not { } me || cast.Caster != me.Guid) { Answer(from, cast, OrderResult.Refused, CastWhy.Unknown); return; }
        if (NetStoryCast.Refused(me, Possessed)) { Answer(from, cast, OrderResult.Refused, CastWhy.Possessed); return; }
        var result = OrderResult.Refused;
        var why = CastWhy.Unknown;
        Places.Within(place, () => result = ServerOrders.Cast(place, me, cast, out why));
        Answer(from, cast, result, why);                                     // after any message the cast itself sent
        Log?.Invoke($"net: {from.Name} casts {cast.Discipline}{(cast.Target != 0 ? $" on {cast.Target}" : "")} — {result}{(why != CastWhy.Begun ? $" ({why})" : "")}");
    }

    /// <summary>
    /// A move in a player's own pack (1.1's 0x13 … 0x17): theirs only, and silently refused while the Storyteller has
    /// the body — not for the pause (<see cref="PackRefusal"/>).
    /// </summary>
    void Pack(NetPlayer from, PackGesture move)
    {
        if (PlaceOf(from) is not { } place || from.Avatar is not { } me || move.Actor != me.Guid) return;
        if (PackRefusal.Refused(move.Kind, me, Possessed)) return;
        Places.Within(place, () =>
        {
            string said = HostPack.Apply(place, from, move, NowMs, Shops.OpenFor(from) is not null, QolInForce);                // made whether or not anybody listens
            Log?.Invoke($"net: {from.Name}'s pack, {move.Kind} — {said}");
        });
    }

    /// <summary>
    /// A player asks to come back (1.1's message 0x49): dead, or with humanity at nothing
    /// (`stats[7] == 0.0`), the character is revived at full health and blood and
    /// moved to the chronicle's revive location (GameServer.Places.cs).
    /// </summary>
    void Revive(NetPlayer p)
    {
        if (p.Avatar is not { Actor: { } a } body || !(a.Dead || a.Stat[(int)ActorStat.Humanity] == 0f)) return;
        Damage.Revive(body, 1f, 1f, sound: true);
        Log?.Invoke($"net: {p.Name} revived");
        Places.Within(body.Place, () => RevivedAt(p, body));
    }

    // ---- the Storyteller's powers that go through the host --------------------------------------

    /// <summary>The pause for everyone: `UI_STSBPAUSE` or `UI_STSBUNPAUSE`, and message 0x48 to all.</summary>
    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        Everyone(Channel.Reliable, new NetWriter(Msg.Pause).Bool(paused).U8(NetPane.Pause).ToArray());   // the pause pane, on or off
        Room(paused ? "UI_STSBPAUSE" : "UI_STSBUNPAUSE", null);
    }

    /// <summary>A line posted to everybody's message box (and the host's).</summary>
    public void Announce(string key, string? args = null, uint colour = 0xffffffffu)
    {
        Posted?.Invoke(Text(key, args), colour);
        foreach (var p in Players.Where(p => p.Link is not null)) Post(p, key, args, colour);
    }

    // ---- the LAN --------------------------------------------------------------------------------

    /// <summary>
    /// A LAN query answered with the game's row (the lobby's list, `"%s (%d)"`). 1.1's host
    /// re-announces itself every 2 s instead (from the network pump); answering a query
    /// (ours) does the same job without the repeated broadcast.
    /// </summary>
    void Query(IPEndPoint from, byte[] data)
    {
        try
        {
            var r = new NetReader(data);
            if (r.Kind != Msg.Query) return;
            r.I32();        // any protocol: the game is listed, and Join Game refuses a stranger's
        }
        catch (EndOfStreamException) { return; }
        var info = new GameInfo(Settings.Name, Settings.Chronicle, Players.Count(), Settings.MaxPlayers, Started, peer.Port, Flags);
        peer.SendDatagram(from, info.ToArray());
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>A line to the whole room: `"%s %s"` — a name and the key's text — as 1.1's join writes `UI_NET_HASJOINED`.</summary>
    void Room(string key, string? name, string? args = null, uint colour = 0xffffffffu, string? players = null)
    {
        string text = name is null ? Text(key, args) : $"{name} {Find(key) ?? key}";
        Posted?.Invoke(text, colour);
        // The key goes, not the words: each machine says it in its own string table.
        foreach (var p in Players.Where(p => p.Link is not null))
            Send(p, Channel.Reliable, new NetWriter(Msg.Post).Str(players ?? key).Str(args ?? "").U32(colour).Str(name ?? "").ToArray());
    }

    void Post(NetPlayer p, string key, string? args, uint colour)
    {
        if (p.Link is null) { Posted?.Invoke(Text(key, args), colour); return; }
        Send(p, Channel.Reliable, new NetWriter(Msg.Post).Str(key).Str(args ?? "").U32(colour).Str("").ToArray());
    }

    string Text(string key, string? args) => args is null ? Find(key) ?? key : Ui.Strings.Format(Find, key, args);

    // Host → all or a newcomer: a slot's player (1.1's message 0x32 — slot, state,
    // character name, network name, GUID, place, flags, type).
    byte[] Joined(NetPlayer p)
    {
        var w = new NetWriter(Msg.Joined);
        p.Info(Storyteller).Write(w);
        return w.ToArray();
    }

    static byte[] Made(ThingMade m)
    {
        var w = new NetWriter(Msg.Made);
        m.Write(w);
        return w.ToArray();
    }

    /// <summary>
    /// A player's own character in full: health, experience and the nineteen stats; and (protocol 22) this host's time
    /// <paramref name="hostMs"/> (the snapshots' clock) and each discipline's recast clock left, for her own cast's start predicted
    /// on her machine (<see cref="OwnCast"/>).
    /// </summary>
    internal static byte[] Sheet(WorldThing avatar, int hostMs)
    {
        var a = avatar.Actor!;
        var w = new NetWriter(Msg.Sheet).I32(avatar.Guid).F32(a.Health).F32(a.MaxHealth)
            .I32((int)Math.Min(a.Experience, int.MaxValue)).I32((int)Math.Min(a.SpendableExperience, int.MaxValue))
            .U32(a.Flags).U8((byte)a.Type).Str(a.Clan).Str(a.Name);
        w.U8(Actor.Stats);
        for (int i = 0; i < Actor.Stats; i++) w.F32(a.Stat[i]);
        // What the character carries, so its pack shows it: each item's GUID, template, place in the
        // pack (slot and cell) and count — all of it (the transport sends a long message in pieces).
        var carried = a.Inventory.Items.Where(i => i.Thing is not null && i.Location != Slot.Vault).Take(ushort.MaxValue).ToList();   // the vault apart (Msg.VaultItems)
        w.U16((ushort)carried.Count);
        foreach (var item in carried)
            w.I32(item.Thing!.Guid).Str(item.Thing.Template).I32(item.Location).I16((short)item.Cell.X).I16((short)item.Cell.Y).I32(item.Amount);
        // Protocol 4: the base stats and the disciplines, so the player's own advancement screen
        // (on their machine) prices and shows what the host's character has.
        w.U8(Actor.Stats);
        for (int i = 0; i < Actor.Stats; i++) w.F32(a.BaseStat[i]);
        var known = a.Disciplines.All.Where(d => (d.Flags & Discipline.Temporary) == 0).Take(64).ToList();
        w.U8((byte)known.Count);
        foreach (var d in known) w.Str(d.Definition.Name).U8((byte)d.Level).I16((short)d.QuickSlot);
        w.I32(hostMs);
        int now = avatar.Place?.Milliseconds ?? 0;
        foreach (var d in known) w.I32(RecastLeft(d, now));
        return w.ToArray();
    }

    void Everyone(Channel channel, byte[] message)
    {
        foreach (var p in Players.Where(p => p.Ready && p.Link is not null)) Send(p, channel, message);
    }

    /// <summary>The haven vault's pane opened on <paramref name="p"/>'s machine (reliable; <see cref="StoryVault.Hand"/>).</summary>
    public void SendVault(NetPlayer p)
    {
        SendVaultItems(p);                                                          // ours: what it holds first (GameServer.Vault.cs)
        Send(p, Channel.Reliable, new NetWriter(Msg.Vault).ToArray());
    }

    void Send(NetPlayer p, Channel channel, byte[] message)
    {
        if (p.Link is { Closed: false } link) peer.Send(link, channel, message);
    }

    public void Dispose()
    {
        if (Place is not null) { NetOrders.Detach(Place); SceneRoutes.Detach(Place); HostSeen.Detach(Place); }
        // The round trip of each player still in, as the session ends.
        foreach (var p in Players)
            if (p.Link is { Closed: false } link) Log?.Invoke(RoundTrips.Note(p.RoundTripKey, RoundTrips.Line($"to {p.Name}", link, peer.LastPolledMs, "; the session ended")));
        foreach (var p in Players.Where(p => p.Link is not null))
            Send(p, Channel.Reliable, new NetWriter(Msg.Bye).Str("UI_NET_DISCONNECTED").ToArray());
        peer.Poll();
        peer.Dispose();
    }
}
