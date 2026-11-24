// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's side of a network game: joining (1.1's 0x3b and its answer 0x3c),
// the room while it forms, the world the host sends, the orders sent back and the chat
// out. 1.1's is over DirectPlay; ours over `NetPeer`.
using System.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public enum ClientState
{
    /// <summary>Knocking; no answer yet.</summary>
    Connecting,
    /// <summary>In the game's room, waiting for the host to start (mpChat mode 2).</summary>
    Room,
    /// <summary>The host has started: load the place, then <see cref="GameClient.Enter"/>.</summary>
    Loading,
    /// <summary>In the world.</summary>
    Playing,
    /// <summary>Refused, kicked, or the host has gone; <see cref="GameClient.Reason"/> says which, in 1.1's words.</summary>
    Gone,
}

/// <summary>A player's session with a host.</summary>
public sealed partial class GameClient : IOrderSink, IDisposable
{
    readonly NetPeer peer;
    readonly Connection link;
    readonly string password;
    readonly Dictionary<int, PlayerInfo> players = [];
    WorldMirror? mirror;
    WorldThing? local;

    /// <param name="me">
    /// The character to bring, sent straight after the welcome (the command line's `--join`, the tests); null for the lobby's
    /// join, whose player chooses one in the room (<see cref="Choose"/>).
    /// </param>
    public GameClient(NetPeer peer, IPEndPoint host, NetCharacter? me, string password = "")
    {
        this.peer = peer;
        Me = me;
        this.password = password;
        peer.Log ??= line => Log?.Invoke(line);                          // the transport's own lines
        Connectors.Current.BeforeKnock(peer, host);                      // the connector seam: a path opened first, when a way needs one
        link = peer.Connect(host);
        Scenes = new ClientScenes(m => { if (State == ClientState.Playing) peer.Send(link, Channel.Reliable, m); });
        Texts = new ClientTexts(m => { if (State == ClientState.Playing) peer.Send(link, Channel.Reliable, m); }, () => AvatarGuid);
        Seen = new ClientSeen(m => { if (State == ClientState.Playing) peer.Send(link, Channel.Reliable, m); }, () => AvatarGuid) { Log = l => Log?.Invoke(l) };
        Shops = new ClientShops(m => { if (State == ClientState.Playing) peer.Send(link, Channel.Reliable, m); }, () => Avatar);
        Arms = new ClientArms(this, m => { if (State == ClientState.Playing) peer.Send(link, Channel.Reliable, m); });
        Own = new OwnWalk(() => Avatar) { Log = l => Log?.Invoke(Logged(l)) };
        Strikes = new OwnStrike(() => Avatar, Own, () => mirror, () => Paused);
        Casts = new OwnCast(() => Avatar, Own, Strikes, () => mirror, () => Paused) { Log = l => Log?.Invoke(l) };
    }

    /// <summary>Her own walk, predicted on this machine (<see cref="OwnWalk"/>): on unless the developer's <c>--no-predict</c>.</summary>
    public OwnWalk Own { get; }

    /// <summary>The start of her own swing, predicted on this machine (the second part; <see cref="OwnStrike"/>): on with her walk's.</summary>
    public OwnStrike Strikes { get; }

    /// <summary>The start of her own discipline, predicted on this machine (<see cref="OwnCast"/>): on with her walk's.</summary>
    public OwnCast Casts { get; }

    /// <summary>What this machine sees of the others: the host's kit, effects, flags, blows, sounds and shots (`ClientSeen`).</summary>
    public ClientSeen Seen { get; }

    /// <summary>The read pane, the quests and the advancement screen a script on the host shows this player.</summary>
    public ClientTexts Texts { get; }

    /// <summary>A shop the host opens on this machine (1.1's 0x42 / 0x43).</summary>
    public ClientShops Shops { get; }

    /// <summary>The network arms of the single-player pieces here (the throw replayed, money dropped, a conversation left by a journey).</summary>
    public ClientArms Arms { get; }

    /// <summary>The discipline table, for the disciplines the host's sheet names (null: the place's own, when it was awakened with one).</summary>
    public Rpg.DisciplineDefinitions? Powers { get; set; }

    /// <summary>The conversations and cutscenes the host sends this player.</summary>
    public ClientScenes Scenes { get; }

    /// <summary>The character this machine brings: the one last asked for (<see cref="Choose"/>), the host's answer in <see cref="Accepted"/>; null while none is chosen.</summary>
    public NetCharacter? Me { get; private set; }
    /// <summary>This machine's build and data, which the Hello carries for the host's check (<see cref="NetIdentity"/>).</summary>
    public NetIdentity Identity { get; init; } = NetIdentity.Unstated;
    /// <summary>What the host said differs when it refused this machine for its build or its data, or null.</summary>
    public string? Refusal { get; private set; }
    public ClientState State { get; private set; } = ClientState.Connecting;
    /// <summary>Why the client is <see cref="ClientState.Gone"/>: a `UI_NET_*` key.</summary>
    public string? Reason { get; private set; }
    public GameSettings? Settings { get; private set; }

    /// <summary>
    /// The host's switches that change the game (<see cref="HostSwitches"/>), as it last sent them;
    /// empty until the welcome. <see cref="SwitchesArrived"/> is told each time, and the game lays them
    /// over its own (<see cref="HostSwitches.Install"/>); a test reads them here.
    /// </summary>
    public IReadOnlyList<HostSwitches.Setting> FromHost { get; private set; } = [];
    /// <inheritdoc cref="FromHost"/>
    public event Action<IReadOnlyList<HostSwitches.Setting>>? SwitchesArrived;

    void Switches(IReadOnlyList<HostSwitches.Setting> fromHost)
    {
        FromHost = fromHost;
        SwitchesArrived?.Invoke(fromHost);
    }
    /// <summary>This player's slot (0 is the host's).</summary>
    public int Slot { get; private set; } = -1;
    public IReadOnlyCollection<PlayerInfo> Players => players.Values;
    /// <summary>The host's GUID for this player's character, once it has one.</summary>
    public int AvatarGuid => players.TryGetValue(Slot, out var me) ? me.Avatar : 0;
    /// <summary>This machine's thing standing for this player's character.</summary>
    public WorldThing? Avatar => AvatarGuid != 0 ? mirror?.Find(AvatarGuid) : null;
    public WorldMirror? Mirror => mirror;
    public bool Paused { get; private set; }
    /// <summary>The host's pane on this machine — the load screen or the pause menu (1.1's 0x48).</summary>
    public NetPane Pane { get; } = new();
    /// <summary>The round trip to the host (1.1's "Ping reply: %d msec").</summary>
    public float RoundTripMs => link.RoundTripMs;

    /// <summary>A chat line to show, in words, with its colour; and the speaker's thing, whose lips move.</summary>
    public event Action<string, uint, WorldThing?>? Chatted;
    /// <summary>The host asked this machine to open the haven vault's pane (<see cref="Msg.Vault"/>, <see cref="StoryVault.Asked"/>).</summary>
    public event Action? VaultAsked;
    /// <summary>A line for the message box, in words, with its colour.</summary>
    public event Action<string, uint>? Posted;
    /// <summary>The host has started the game: load the place now.</summary>
    public event Action? Started;
    /// <summary>The console's account.</summary>
    public event Action<string>? Log;

    /// <summary>The string table (null answers keys with themselves).</summary>
    public Func<string, string?> Find { get; set; } = _ => null;
    /// <summary>This machine's QoL switches for what it alone decides (`whisperecho`); null reads <see cref="World.Qol.Current"/> at each use, a test passes its own.</summary>
    public World.Qol? Qol { get; init; }

    /// <summary>
    /// The place is loaded (with the level and scene the host named): this machine's world is tied to
    /// the host's, orders from it go to the host, and the host is asked for the rest (1.1
    /// unloads what it no longer needs and tells the host it is there).
    /// <paramref name="localPlayer"/> is the character the harness built, which stands for this
    /// player's — the host's own GUID for it comes with the world.
    /// </summary>
    public void Enter(Location place, WorldThing? localPlayer)
    {
        place.RenumberScene(Settings?.SceneGuid ?? 0);                 // the host's numbers (LocationGuids.cs); none before the welcome
        mirror = new WorldMirror(place) { Own = Own };
        Own.Attach(mirror);
        Strikes.Attach();
        Casts.Attach();
        local = localPlayer;
        if (local?.Actor is { } held) held.Flags |= Actor.Flag.AiPaused;
        NetOrders.Attach(place, NetMode.Client, this);
        StopKeepAlive();
        State = ClientState.Playing;
        Scenes.Enter(mirror, localPlayer);
        Seen.Enter(mirror);
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.Ready).ToArray());
        if (AvatarGuid != 0 && local is not null) mirror.Bind(AvatarGuid, local);
    }

    /// <summary>One turn: the wire read, every message applied, and the world eased toward the host's.</summary>
    public void Poll(float seconds)
    {
        foreach (var e in peer.Poll())
        {
            if (!ReferenceEquals(e.Connection, link)) continue;
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                {
                    // The password proved over the session and never sent (PasswordProof), and this machine's build and data
                    // (NetIdentity); the proof's place is fixed (NetPeer.HelloProofAt), for the tape's mask. The character is
                    // chosen in the room (protocol 23; GameClient.Room.cs).
                    var w = new NetWriter(Msg.Hello).I32(Protocol.Version).Bytes(PasswordProof.Make(password, link.Binding));
                    Identity.Write(w);
                    peer.Send(link, Channel.Reliable, w.ToArray());
                    break;
                }
                case NetEventKind.Disconnected:
                    Gone(e.Reason is "bye" ? Reason ?? "UI_NET_DISCONNECTED"
                         : e.Reason is "UI_NET_BADADDR" ? "UI_NET_BADADDR" : "UI_NET_DISCONNECTED");
                    break;
                case NetEventKind.Message:
                    // Whatever the host sends that does not read is dropped and counted, never thrown out of the loop;
                    // a host that keeps sending it is left (NetPeer.Nonsense).
                    try { Receive(new NetReader(e.Data)); }
                    catch (Exception x) when (x is not OutOfMemoryException)
                    {
                        if (peer.Nonsense(link, $"{(e.Data.Length > 0 ? ((Msg)e.Data[0]).ToString() : "an empty message")}: {x.GetType().Name}"))
                            Log?.Invoke($"net: a message from the host dropped, it does not read ({(e.Data.Length > 0 ? (Msg)e.Data[0] : "empty")}, {x.GetType().Name}: {x.Message.Split('\n')[0]})");
                    }
                    break;
            }
        }
        // The round trip to the host, said once a minute while joined (RoundTrips).
        if (State != ClientState.Gone && link is { Open: true, Closed: false } && RoundTrips.Due(link, peer.LastPolledMs, ref roundTripSaid))
            Log?.Invoke(RoundTrips.Note(RoundTripKey, RoundTrips.Line("to the host", link, peer.LastPolledMs)));
        mirror?.Step(seconds);
        Own.Step(seconds);
        Strikes.Step();
        Casts.Step();
    }

    /// <summary>A line of her own walk's account in the game's own log too, as the round trip's (<see cref="RoundTrips.Note"/>): a window has no console.</summary>
    static string Logged(string line)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diagnostics.Diag.For("Vitomir.Net"), "{Line}", line);
        return line;
    }

    /// <summary>When the round trip to the host was last said (the transport's clock), 0 before.</summary>
    long roundTripSaid;

    /// <summary>The key of this machine's round trip's line.</summary>
    string RoundTripKey => $"player: {Me?.Name ?? "slot " + Slot} to the host";

    void Receive(NetReader r)
    {
        switch (r.Kind)
        {
            case Msg.Welcome:
            {
                Slot = r.U8();   // moves its own slot record into the one the join's answer gave it
                byte stage = r.U8();   // the session's stage (message 0x33's first byte; GameClient.LetIn.cs)
                Settings = GameSettings.Read(r);
                int n = r.U8();
                for (int i = 0; i < n; i++) { var p = PlayerInfo.Read(r); players[p.Slot] = p; }
                Switches(HostSwitches.Read(r));
                State = ClientState.Room;
                // The wire kept alive from the welcome too, not only through the load (ours): a room's frame can stall this
                // machine past the transport's ten seconds — a slow machine's did as it came to choose its character, and the host
                // gave it up meanwhile. Kept on through the load; let go once in (Enter) or gone.
                keepAlive ??= peer.KeepAliveWhile();
                Log?.Invoke($"net: joined {Settings.Name} as slot {Slot}{(stage == SessionStage.UnderWay ? ", in progress" : "")}");
                Posted?.Invoke(Find("UI_NET_YOUJOINEDGAME") ?? "UI_NET_YOUJOINEDGAME", 0xffffffffu);
                Welcomed();      // the character brought, and whether this install has the chronicle (GameClient.Room.cs)
                Staged(stage);   // in only by Start, not at once
                break;
            }
            case Msg.Refused:
            {
                string key = r.Str();
                // What differs, when the build or the data does (NetIdentity.Differs), said under 1.1's own line.
                if (r.More && r.Str() is { Length: > 0 } detail) { Refusal = detail; Log?.Invoke($"net: {detail}"); }
                Gone(key);
                if (Refusal is not null) Posted?.Invoke(Refusal, 0xffff4040u);
                peer.Disconnect(link, Reason!);
                break;
            }
            case Msg.Start:
                LetInAnswered(r);   // the answer to this machine's Start
                break;
            case Msg.Stage:
                Staged(r.U8());     // message 0x33
                break;
            case Msg.Travel:
            {
                // The game has moved: this machine's world is let go and the new place is to be loaded.
                Settings = GameSettings.Read(r);
                Switches(HostSwitches.Read(r));
                Arms.Travelling(Scenes.Talk);
                if (mirror is not null) NetOrders.Detach(mirror.Place);
                mirror = null;
                Own.Attach(null);
                local = null;
                Scenes.Leave();
                foreach (var key in players.Keys.ToList()) players[key] = players[key] with { Avatar = 0 };
                State = ClientState.Room;
                Log?.Invoke($"net: the game moves to {Settings.Level}");
                Begin();
                break;
            }
            case Msg.Bye:
                Reason = r.Str();
                Gone(Reason);
                peer.Disconnect(link, Reason);
                break;
            // Handler of message 0x32: makes the other player's character from its
            // template ("mp_christof_da" when unknown), names it, sets its place and type — split
            // here between this player record and the `Made` that spawns the thing itself.
            case Msg.Joined:
            {
                var p = PlayerInfo.Read(r);
                players[p.Slot] = p;
                if (p.Slot == Slot && p.Avatar != 0 && local is not null) mirror?.Bind(p.Avatar, local);
                NamePlayersCharacter(p.Avatar);   // by the player's name
                break;
            }
            case Msg.Left:
            {
                int slot = r.U8();
                string key = r.Str();
                players.Remove(slot);
                break;
            }
            case Msg.Chat:
            {
                var kind = (ChatKind)r.U8();
                r.U8();
                string speaker = r.Str(), text = r.Str(), to = r.Str();
                int guid = r.I32();
                var who = guid != 0 ? mirror?.Find(guid) : null;
                // A whisper's copy to its sender (it names the one whispered to) is the QoL `whisperecho`'s, this
                // machine's own: off, 1.1's silence (a whisper goes to its recipient only).
                if (kind == ChatKind.Whisper && to.Length > 0 && !(Qol ?? World.Qol.Current).WhisperEcho) break;
                // An emote's gesture on this machine (`ChatGesture`), else the speaker's lips,
                // the talk this machine's own.
                if (!ChatGesture.Play(who, kind, text, Find, who?.Place?.Motions)
                    && who is { Type: ThingType.Actor or ThingType.Player, Place: { } seen })
                    Anim.ChatLips.Start(who, seen.Milliseconds, seen.Motions);
                Chatted?.Invoke(NetChat.Show(kind, speaker, text, to.Length > 0 ? to : null, Find), NetChat.Colour(kind), who);
                if (ChatNoise.For(kind) is int noise) Audio.Noise.Ui(mirror?.Place, noise);   // the chat noise, rolled
                break;
            }
            case Msg.Vault:
                VaultAsked?.Invoke();
                break;
            case Msg.Post:
            {
                string key = r.Str(), args = r.Str();
                uint colour = r.U32();
                string who = r.More ? r.Str() : "";
                string text = key.Length == 0 ? args : args.Length == 0 ? Find(key) ?? key : Ui.Strings.Format(Find, key, args);
                // A room line names its player first — "Bob has joined the game." ("%s %s").
                Posted?.Invoke(who.Length > 0 ? $"{who} {text}" : text, colour);
                break;
            }
            case Msg.Made:
            {
                var m = ThingMade.Read(r);
                if (mirror is null) break;
                // This player's own character is the local one the harness built, not a second copy.
                if (m.Guid == AvatarGuid && local is not null) { mirror.Bind(m.Guid, local); mirror.Made(m); }
                else if (mirror.Made(m) is null) Log?.Invoke($"net: the host made a {m.Template} this install has no template for");
                NamePlayersCharacter(m.Guid);
                break;
            }
            case Msg.Removed:
                mirror?.Removed(r.I32());
                break;
            case Msg.Fired:
                mirror?.Fired(ThingFired.Read(r));
                break;
            case Msg.Snapshot:
            {
                int at = r.I32();
                int n = r.U16();
                var states = new ThingState[n];
                for (int i = 0; i < n; i++) states[i] = ThingState.Read(r);
                mirror?.Apply(at, states);
                break;
            }
            case Msg.Sheet:
                Sheet(r);
                break;
            case Msg.Taken:                                                     // a numbered order of hers answered; or cast
            {
                ushort number = r.U16();
                var result = (Rpg.OrderResult)r.U8();
                int hostMs = r.I32();
                // A cast's answer says why, her recast clock left and how long it holds her there (protocol 22).
                var why = r.More ? (Rpg.CastWhy)r.U8() : Rpg.CastWhy.Begun;
                int recastLeft = r.More ? r.I32() : -1, busy = r.More ? r.I32() : 0;
                Own.Taken(number, result, hostMs);
                Strikes.Taken(number, result);
                Casts.Taken(number, result, why, recastLeft, busy, hostMs);
                break;
            }
            case Msg.Pause:
            {
                bool on = r.Bool();
                int pane = r.More ? r.U8() : NetPane.Pause;
                string? was = Pane.File;
                if (Pane.Take(pane, on))                                        // The one pane (NetPane)
                    Log?.Invoke($"net: the host's pane {(on ? $"{Pane.File} up" : $"{was} down")}");
                if (pane != NetPane.Load) Paused = on;                          // ours: the world freezes for the pause
                break;
            }
            case Msg.VaultMove: VaultMoves.Moved(this, r); break;
            case Msg.VaultItems: VaultMoves.Contents(this, r); break;           // ours: her vault's contents
            case Msg.Converse: Scenes.Converse(r); break;
            case Msg.Overheard: Scenes.Overheard(r); break;
            case Msg.CameraShot: Scenes.Shot(r); break;
            case Msg.CameraRelease: Scenes.Released(); break;
            case Msg.CameraPath: Scenes.Path(r); break;
            case Msg.Text: Texts.Text(r); break;
            case Msg.QuestLoaded: Texts.QuestLoaded(r); break;
            case Msg.QuestFlags: Texts.QuestFlags(r); break;
            case Msg.QuestList: Texts.QuestList(r); break;
            case Msg.Advance: Texts.Advance(r, mirror); break;
            case >= Msg.Kit and <= Msg.Shot: Seen.Receive(r); break;
            case Msg.Shop: Shops.Opened(r, mirror); break;
            case Msg.ShopDeal: Shops.Answered(r, mirror); break;
            case Msg.Purse: Shops.Purse(r, mirror); break;
            case Msg.Thrown: Arms.Thrown(r, mirror); break;
            case Msg.Went: Went(r); break;
            case Msg.Struck: Arms.Struck(r, mirror); break;
            case Msg.Attached or Msg.Named or Msg.Gibbed or Msg.Beheaded or Msg.Revived: Seen.Receive(r); break;
            case Msg.Chose or Msg.Setup: InRoom(r); break;     // the game's room (GameClient.Room.cs)
        }
    }

    /// <summary>
    /// This player's own character, whole — so the status bar and the sheet show the host's numbers.
    /// Handler of message 0x36 on the player's machine: health, blood, maxima and the
    /// nineteen stats.
    /// </summary>
    void Sheet(NetReader r)
    {
        int guid = r.I32();
        var a = mirror?.Find(guid)?.Actor;
        float health = r.F32(), max = r.F32();
        int xp = r.I32(), spend = r.I32();
        uint flags = r.U32();
        var type = (ActorType)r.U8();
        string clan = r.Str(), name = r.Str();
        int n = r.U8();
        var stats = new float[n];
        for (int i = 0; i < n; i++) stats[i] = r.F32();
        var carried = new List<(int Guid, string Template, int Slot, short X, short Y, int Amount)>();
        if (r.More)
        {
            int count = r.U16();                                        // protocol 15: all of it
            for (int i = 0; i < count; i++) carried.Add((r.I32(), r.Str(), r.I32(), r.I16(), r.I16(), r.I32()));
        }
        float[]? bases = null;
        var known = new List<SheetDiscipline>();
        if (r.More)
        {
            bases = new float[r.U8()];
            for (int i = 0; i < bases.Length; i++) bases[i] = r.F32();
            for (int i = r.U8(); i > 0; i--) known.Add(new SheetDiscipline(r.Str(), (sbyte)r.U8(), r.I16()));    // a level of −1 is the byte 0xff
        }
        // Protocol 22: the host's time it was made, and each discipline's recast clock left, for her own cast's start
        // predicted here (OwnCast).
        int sheetMs = int.MinValue;
        var recasts = new List<(string, int)>();
        if (r.More)
        {
            sheetMs = r.I32();
            for (int i = 0; i < known.Count && r.More; i++) recasts.Add((known[i].Name, r.I32()));
        }
        if (a is null) return;
        Carried(mirror!.Find(guid)!, carried);
        // While this machine's advancement screen is up, the numbers being spent are its own — 1.1's
        // too: the sheet message sets nothing while the screen's subject is set.
        if (Texts.Advancing && guid == AvatarGuid) return;
        if (bases is not null)
            for (int i = 0; i < Math.Min(bases.Length, Actor.Stats); i++) a.BaseStat[i] = bases[i];
        if (known.Count > 0 && mirror!.Find(guid) is { } body && (Powers ?? mirror.Place.Powers) is { } powers)
            foreach (var d in Enumerable.Reverse(known))
            {
                if (powers[d.Name] is not { } def) continue;
                var have = a.Disciplines.Of(def) ?? Rpg.DisciplineBook.Make(body, def);
                if (have is null) continue;
                if (have.Level != d.Level) Rpg.DisciplineBook.SetLevel(body, powers, d.Name, d.Level);
                have.QuickSlot = d.QuickSlot;
            }
        a.Health = health;
        a.MaxHealth = max;
        a.Experience = xp;
        a.SpendableExperience = spend;
        a.Flags = flags | Actor.Flag.AiPaused;
        a.Type = type;
        a.Clan = clan;
        a.Name = name;
        for (int i = 0; i < Math.Min(n, Actor.Stats); i++) a.Stat[i] = stats[i];
        if (guid == AvatarGuid && sheetMs != int.MinValue) Casts.Sheet(sheetMs, recasts);
    }

    /// <summary>
    /// The pack as the host has it: each item found (or made) on this machine and put in its slot and
    /// cell. **Read-only** — moving things about in the pack on this machine is not sent to the host
    /// yet (1.1's 0x13 … 0x17), and the next sheet puts them back.
    /// </summary>
    void Carried(WorldThing me, List<(int Guid, string Template, int Slot, short X, short Y, int Amount)> carried)
    {
        if (me.Actor is not { } a || mirror is null) return;
        var now = new List<Rpg.Item>();
        foreach (var c in carried)
        {
            var t = mirror.Find(c.Guid);
            if (t is null)
            {
                t = mirror.Place.Restore(c.Template, c.Guid, null);
                if (t is null) continue;
                mirror.Bind(c.Guid, t);
            }
            // The utility item worn in the off hand (a torch) stays in the world as the host's does (the kit's own
            // rule, ClientSeen.Kit): earlier the sheet took it out every half second, and a sheet after its Made for good.
            bool wornUtility = t.Item is { Type: Rpg.ItemType.Utility } && (c.Slot & Rpg.Slot.Worn) != 0;
            if (t.InWorld && !wornUtility) mirror.Place.Withdraw(t);
            if (t.Item is not { } item) continue;
            item.Location = c.Slot;
            item.Cell = (c.X, c.Y);
            item.Amount = c.Amount;
            t.Owner = me;
            now.Add(item);
        }
        // The vault is not in the sheet: what is in it stays, kept by its own messages (VaultMoves).
        var vault = a.Inventory.Items.Where(i => i.Location == Rpg.Slot.Vault && !now.Contains(i)).ToList();
        if (now.Concat(vault).SequenceEqual(a.Inventory.Items)) return;
        foreach (var old in a.Inventory.Items.ToList()) a.Inventory.Remove(old);
        for (int i = vault.Count - 1; i >= 0; i--) a.Inventory.Add(vault[i]);
        for (int i = now.Count - 1; i >= 0; i--) a.Inventory.Add(now[i]);        // Add pushes on the front
    }

    // The place to load has come — the answer to this machine's Start or the whole game's
    // journey — and `State` moves to `Loading`.
    void Begin()
    {
        if (State is ClientState.Loading or ClientState.Playing) return;
        State = ClientState.Loading;
        // The wire kept alive while this machine loads the place (a first load compiles 1.1's scripts, past the
        // transport's ten seconds), until it is in (Enter) or gone; already on when it comes from the room.
        keepAlive ??= peer.KeepAliveWhile();
        Started?.Invoke();
    }
    IDisposable? keepAlive;

    void StopKeepAlive()
    {
        keepAlive?.Dispose();
        keepAlive = null;
    }

    void Gone(string reason)
    {
        if (State == ClientState.Gone) return;
        StopKeepAlive();
        // The round trip to the host, as this machine leaves the game (or is dropped), when it was ever joined.
        if (link.Open || link.RoundTripsMeasured > 0)
            Log?.Invoke(RoundTrips.Note(RoundTripKey, RoundTrips.Line("to the host", link, peer.LastPolledMs, $"; out of the game ({Reason ?? reason})")));
        // What her own walk's prediction did here, as this machine leaves.
        if (Own.On && Own.Done is { } done && done.Predicted + done.Drives > 0) Log?.Invoke(Logged(Own.Line()));
        State = ClientState.Gone;
        Reason ??= reason;
        if (mirror is not null) NetOrders.Detach(mirror.Place);
        Log?.Invoke($"net: out of the game — {Reason}");
        Posted?.Invoke(Find(Reason) ?? Reason, 0xffff4040u);
    }

    /// <summary>A typed line: sent as it is; the host decides what it means.</summary>
    public void Say(string line)
    {
        if (line.Length == 0 || State is ClientState.Connecting or ClientState.Gone) return;
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.Say).Str(line.Length > NetChat.MaxLength ? line[..NetChat.MaxLength] : line).ToArray());
    }

    /// <summary>
    /// An order from this machine's order functions (<see cref="NetOrders.Forward"/>), with its GUIDs
    /// turned into the host's. The aim goes unreliably, as 1.1's does (order 0x1e).
    /// </summary>
    public void Send(NetOrder order)
    {
        if (mirror is null || State != ClientState.Playing) return;
        int actor = order.Actor != 0 && mirror.Place.Thing(order.Actor) is { } a ? mirror.HostGuid(a) : 0;
        int target = order.Target != 0 && mirror.Place.Thing(order.Target) is { } t ? mirror.HostGuid(t) : 0;
        int weapon = order.Weapon != 0 && mirror.Place.Thing(order.Weapon) is { } wp ? mirror.HostGuid(wp) : 0;
        if (actor == 0) return;
        // Her own orders numbered while her walk is predicted here (OwnWalk), for the host to answer.
        ushort seq = Own.On && order.Kind != OrderKind.Aim && mirror.Place.Thing(order.Actor) is { } who && Own.Is(who) ? Own.Numbered(order.Kind) : (ushort)0;
        // One that may keep her busy on the host is waited for before a cast of hers is guessed (OwnCast).
        if (order.Kind is not (OrderKind.Move or OrderKind.Stop)) Casts.Sent(seq, null, 0f, mirror.Place.Milliseconds);
        var w = new NetWriter(Msg.Order);
        (order with { Actor = actor, Target = target, Weapon = weapon, Seq = seq }).Write(w);
        peer.Send(link, order.Kind == OrderKind.Aim ? Channel.Unreliable : Channel.Reliable, w.ToArray());
    }

    /// <summary>How often a held push is sent again (ours), well inside the host's <see cref="GameServer.DriveStaleMs"/>.</summary>
    public const int DriveResendMs = 100;

    /// <summary>
    /// How many times more a changed push is sent, a frame apart, while her keyboard walk is predicted here (the same message):
    /// a release lost on the way would leave the host walking her on for <see cref="GameServer.DriveStaleMs"/> after she stood here,
    /// a turn lost for <see cref="DriveResendMs"/>. Three sends of each, so one in some hundred thousand at 2 % loss.
    /// </summary>
    public const int DriveRepeats = 2;
    System.Numerics.Vector2 lastDrive;
    bool lastRun;
    long lastDriveMs = long.MinValue;
    int driveRepeats;

    /// <summary>
    /// The keyboard's push on this machine's character, in world x and y (`[qol] driving`, ours): sent
    /// unreliably when it changes and every <see cref="DriveResendMs"/> while it is held; the host
    /// walks the character (`GameServer`) and the world comes back in the snapshots.
    /// </summary>
    public void Drive(System.Numerics.Vector2 push, bool run) => Drive(push, run, null, 0f);

    /// <summary>
    /// The push as <see cref="Drive(System.Numerics.Vector2, bool)"/> sends it, and with her own walker, <paramref name="walker"/>,
    /// her keyboard walk predicted here for this frame's <paramref name="seconds"/> (<see cref="OwnWalk.Drive"/>): she moves at once and
    /// the host's snapshots correct her. Answers whether this machine moved her.
    /// </summary>
    public bool Drive(System.Numerics.Vector2 push, bool run, Player? walker, float seconds)
    {
        if (State != ClientState.Playing || AvatarGuid == 0) return false;
        bool moved = walker is not null && Own.Drive(walker, push, run, seconds, Paused, Strikes.Pending || Casts.Waiting, RoundTripMs);
        long now = NetTape.Clock(Environment.TickCount64);           // a replay's tape, when one is in
        bool moving = push.LengthSquared() > 1e-6f;
        bool changed = push != lastDrive || run != lastRun;
        if (changed) driveRepeats = Own.Drives ? DriveRepeats : 0;
        else if (driveRepeats > 0) driveRepeats--;
        else if (!moving || now - lastDriveMs < DriveResendMs) return moved;
        lastDrive = push;
        lastRun = run;
        lastDriveMs = now;
        peer.Send(link, Channel.Unreliable,
                  new NetWriter(Msg.Drive).I32(AvatarGuid).F32(push.X).F32(push.Y).Bool(run).ToArray());
        return moved;
    }

    /// <summary>A discipline cast from this machine's cast order (<see cref="NetOrders.ForwardCast"/>), its GUIDs the host's (1.1's 0x18).</summary>
    public void Send(NetCast cast)
    {
        if (mirror is null || State != ClientState.Playing) return;
        int H(int g) => g != 0 && mirror.Place.Thing(g) is { } t ? mirror.HostGuid(t) : 0;
        int caster = H(cast.Caster);
        if (caster == 0) return;
        // Her own casts numbered while her walk is predicted here (OwnCast), for the host to answer; from her orders' count.
        ushort seq = 0;
        if (Own.On && mirror.Place.Thing(cast.Caster) is { } who && Own.Is(who))
        {
            seq = Own.NumberedCast();
            var power = cast.Scroll == 0 ? who.Actor?.Disciplines[cast.Discipline] : null;
            Casts.Sent(seq, cast.Discipline, power is null ? 0f : Rpg.Cast.Cost(who, power), mirror.Place.Milliseconds);
        }
        var w = new NetWriter(Msg.Cast);
        (cast with { Caster = caster, Target = H(cast.Target), Scroll = H(cast.Scroll), Seq = seq }).Write(w);
        peer.Send(link, Channel.Reliable, w.ToArray());
    }

    /// <summary>A move in this player's pack (<see cref="NetOrders.TellPack"/>), its GUIDs the host's (1.1's 0x13 … 0x17).</summary>
    public void Send(PackGesture move)
    {
        if (mirror is null || State != ClientState.Playing) return;
        int H(int g) => g != 0 && mirror.Place.Thing(g) is { } t ? mirror.HostGuid(t) : 0;
        int actor = H(move.Actor);
        if (actor == 0) return;
        var w = new NetWriter(Msg.Pack);
        (move with { Actor = actor, Item = H(move.Item) }).Write(w);
        peer.Send(link, Channel.Reliable, w.ToArray());
    }

    /// <summary>The escape menu's Revive (`emmpReviveButton`): 1.1's message 0x49.</summary>
    public void Revive() => peer.Send(link, Channel.Reliable, new NetWriter(Msg.Revive).ToArray());

    /// <summary>Leave Game: "You have left the game."</summary>
    public void Leave()
    {
        if (State == ClientState.Gone) return;
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.Bye).Str("UI_NET_HASLEFT").ToArray());
        peer.Poll();
        peer.Disconnect(link, "UI_NET_YOULEFTGAME");
        Gone("UI_NET_YOULEFTGAME");
    }

    public void Dispose()
    {
        Leave();
        peer.Dispose();
    }
}
