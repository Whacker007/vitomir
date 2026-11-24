// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The multiplayer front end: `mpSelect` (Host Game, Join Game, back), the join screen and the game's room (the lobby
// `mpChat` in its modes), the character screens `mpPlayerSelect` and `mpPlayerCreate` over the room, `mpCreateGame` as
// the host's Game Setup, and the wait on `NetworkLoadScreen`. The panes are 1.1's; the way through them is ours: 1.1
// chose Internet (WON, long gone) or LAN first, then the character, then a room — Vitomir hosts or joins first, and the
// character and the chronicle are chosen in the room (protocol 23). The join screen keeps 1.1's LAN room and its
// broadcast discovery, and joins by address too.
//
// The front end (`FrontEnd`) keeps the panes, the layers and the input; this class answers their
// clicks and keys through `FrontEnd.OtherClick` and `OtherKey`, and keeps the check boxes, which the
// front end's own menus never needed.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>What the lobby hands to the game run when a game starts: the session, and who the player is.</summary>
public sealed record MpHandoff(GameServer? Server, GameClient? Client, NetCharacter Me, GameSettings Settings)
{
    /// <summary>The network save the host's game starts from (mpCreateGame's Load Saved), or null for the chronicle's start.</summary>
    public string? LoadPath { get; init; }
}

public sealed class MpMenus
{
    public const string SelectFile = "mpSelect", ChatFile = "mpChat", CreateGameFile = "mpCreateGame",
                        PlayerCreateFile = "mpPlayerCreate", PlayerSelectFile = "mpPlayerSelect",
                        LoadFile = "NetworkLoadScreen";

    readonly FrontEnd menus;
    readonly GameFiles files;
    readonly Func<string, string?> find;
    readonly Crypt crypt;
    readonly ChronicleSet chronicles;
    /// <summary>The check boxes' state, by pane and box — lower case, as the pane reader keeps names.</summary>
    readonly CheckBoxes checks = new();
    IReadOnlyList<NetCharacter> characters = [];
    List<FoundGame> games = [];
    LanFinder? finder;
    /// <summary>The LAN room's own talk (`Net.LanRoom`), while the lobby is in the LAN room.</summary>
    LanRoom? room;
    NetCharacter? creating;
    int lastModel = -1, lastClan = -1, lastPlayer = -1;

    /// <summary>The lobby's mode: 1 the join screen (1.1's LAN room), 2 in a game's room as a player, 4 as its host.</summary>
    public int Mode { get; private set; }
    /// <summary>The chosen character (the select screen's OK), or null before one is chosen; in a room, the one brought to the host.</summary>
    public NetCharacter? Me { get; private set; }
    public GameServer? Server { get; private set; }
    public GameClient? Client { get; private set; }
    /// <summary>A game about to start: the host pressed Start, or a player's host did.</summary>
    public MpHandoff? Handoff { get; private set; }
    /// <summary>The port a game is hosted on (1.1's 4994).</summary>
    public int Port { get; set; } = NetPeer.DefaultPort;
    /// <summary>Whether a new game asks the router to map its port (UPnP).</summary>
    public bool Upnp { get; set; } = true;
    /// <summary>The console's account.</summary>
    public event Action<string>? Log;
    /// <summary>
    /// The player's own settings file, where the last character is kept (`LastPlayer`, 1.1's `lastplayer`);
    /// null remembers nobody — what a test leaves it, so it never touches the player's real file.
    /// </summary>
    public string? SettingsPath { get; set; }

    /// <summary>
    /// The advancement screen after Create, when the front end has one: given the
    /// new character and what to do with it once advanced —
    /// write it and go on. Null writes it at once.
    /// </summary>
    public Action<NetCharacter, Action<NetCharacter>>? AdvanceNew { get; set; }

    /// <summary>
    /// A new character as a thing of its own, for the advancement screen after Create — there is no
    /// world in the front end (ours; 1.1 builds the actor from the template): the template's
    /// actor with its `.NPC` sheet and its clan's record, what the create screen chose (name, type, clan,
    /// purse), and the disciplines its AI file gives (as a mind's set-up would). Then Create's
    /// own set-up (<see cref="MpCreateSetUp"/>): the clans' groups, the Caitiff's every group, the
    /// Ventrue's and Giovanni's Blood Rating, the flags, the born effects by type, feed and bloodhealing.
    /// </summary>
    public WorldThing? NewBody(NetCharacter c, DisciplineDefinitions powers)
    {
        if (Preview(c) is not { } actor) return null;
        var thing = new WorldThing { Index = -1, Guid = 1, Scene = new NsdThing(), Template = c.Template, Type = ThingType.Player, Actor = actor };
        c.Apply(thing);
        actor.IsPlayerThing = true;
        actor.ClanRecord = Clans.For(files)[actor.Clan];
        if (actor.AiFile.Length > 0 && new Ai.AiClasses(files).Get(actor.AiFile) is { } ai)
            foreach (var line in ai.Disciplines) DisciplineBook.SetLevel(thing, powers, line.Name, line.Level);
        MpCreateSetUp.Apply(thing, powers, effects ??= new EffectDefinitions(files));
        return thing;
    }

    EffectDefinitions? effects;

    /// <param name="cryptFolder">Where the characters are kept (`Crypt/` beside the saves).</param>
    public MpMenus(FrontEnd menus, GameFiles files, Func<string, string?> find, string cryptFolder)
    {
        this.menus = menus;
        this.files = files;
        this.find = find;
        crypt = new Crypt(cryptFolder);
        chronicles = ChronicleSet.Load(files);
        menus.OtherClick += Click;
        menus.OtherKey += Key;
        menus.Multiplayer = Open;
    }

    string Text(string key) => find(key) ?? key;

    // ---- mpSelect ------------------------------------------------------------------------------

    /// <summary>Request 11: the network selection screen — Host Game, Join Game, Main Menu (ours; 1.1's LAN, Internet and Main Menu).</summary>
    public void Open()
    {
        menus.Floating.Clear();
        menus.Show(menus.Pane(SelectFile));
    }

    bool Click(MenuPane pane, NuiElement e)
    {
        string name = e.Name.ToLowerInvariant();
        switch (pane.File)
        {
            case SelectFile:
                // Ours: 1.1's LAN (buttonvalue 2, the first button) is Host Game, its Internet (1, WON's login) Join Game;
                // Main Menu (3) is 1.1's.
                switch (e.Get("buttonvalue"))
                {
                    case "2": HostGame(); break;
                    case "1": menus.Hide(pane); OpenJoin(); break;
                    default: menus.Hide(pane); menus.OpenMainMenu(); break;
                }
                return true;
            case PlayerSelectFile: PlayerSelectClick(name); return true;
            case PlayerCreateFile: PlayerCreateClick(pane, e, name); return true;
            case ChatFile: LobbyClick(name); return true;
            case CreateGameFile: CreateGameClick(pane, e, name); return true;
        }
        return false;
    }

    bool Key(MenuPane pane, MenuKey key)
    {
        switch (pane.File)
        {
            case SelectFile when key == MenuKey.Escape: menus.Hide(pane); menus.OpenMainMenu(); return true;
            case PlayerCreateFile:
                // Enter in the name box creates; Escape cancels.
                if (key == MenuKey.Enter) Create(); else ClosePlayerCreate();
                return true;
            case PlayerSelectFile:
                // Enter (either one) is OK (the key hook), `lastplayer` written with it; 1.1 answers
                // no other key here — Escape back to the room is ours.
                if (key == MenuKey.Enter) PlayerSelectClick("mppsokbutton");
                else if (key == MenuKey.Escape) menus.PopLayer();
                return true;
            case CreateGameFile:
                if (key == MenuKey.Enter) CreateGame(); else CloseCreateGame();
                return true;
            case ChatFile when key == MenuKey.Enter:
                // Enter in either edit line says it: the room's or the game's.
                var edit = pane.Focus is { } f && pane.Edits.TryGetValue(f, out var ed) ? ed : null;
                if (edit is not null && edit.Text.Length > 0)
                {
                    SayInLobby(edit.Text);
                    edit.Set("");
                }
                return true;
            case ChatFile: return true;
        }
        return false;
    }

    /// <summary>The address box's title (ours: 1.1 has no words for an address, its Internet was WON).</summary>
    public const string AddressTitle = "Host Address (host:port)";

    /// <summary>
    /// **By Address…** on the join screen (ours): the input box asks for <c>host:port</c> (the port 4994 when left off), showing
    /// the address last joined by (<see cref="MpSetup.LastAddress"/>) or nothing — never the loopback by itself. Its Cancel (or
    /// Escape) leaves the join screen as it was; a bad address is said in 1.1's `UI_NET_BADADDR` box; a good one is kept and
    /// joined (<see cref="Join"/>), and a join nobody answers is said within the transport's five seconds, naming the address.
    /// </summary>
    void AskAddress()
    {
        menus.Input(AddressTitle, MpSetup.LastAddress(SettingsPath), 64, (result, text) =>
        {
            if (result != 1) return;
            Vitomir.Platform.Presentation.StreamerMode.Hide(text);      // a host's name as typed is hidden on a stream, as an address is
            if (Net.Connectors.Current.Find(text, Port) is not { } host)
            {
                menus.Message(Text("UI_NET_CONNECTIONERROR"), Text("UI_NET_BADADDR"), ok: true, cancel: false);
                return;
            }
            if (SettingsPath is { } kept) MpSetup.KeepAddress(kept, text.Trim());
            byAddress = true;
            Join(host);
        }, address: true);
    }

    // ---- the character: mpPlayerSelect and mpPlayerCreate ----------------------------------------

    /// <summary>
    /// `mpPlayerSelect`: every kept character, the chosen one described — with nobody in use,
    /// the last one taken into the lobby (`LastPlayer`). **An empty folder opens the
    /// create screen at once** over it (the list counting none); the create
    /// screen's Cancel comes back to the empty list (<paramref name="emptyOpensCreate"/> false) — which, with nobody to
    /// show, gives way to the lobby on the next frame (<c>FollowCharacterScreens</c>).
    /// </summary>
    public void OpenPlayerSelect(bool emptyOpensCreate = true)
    {
        var pane = menus.Pane(PlayerSelectFile);
        characters = crypt.All();
        var list = pane.Lists["mppsPlayerList"];
        list.Clear();
        // Each row "%d. %s (%s)", the one in use chosen by its name (`PlayerSelectRows`); with
        // nobody in use, the one kept as `lastplayer`, else the first (`LastPlayer`).
        for (int i = 0; i < characters.Count; i++)
        {
            var c = characters[i];
            string clan = PlayerSelectRows.ClanKey(c, c.Type != ActorType.Human && c.Clan.Length == 0 ? Preview(c)?.Clan : null);
            list.Add(PlayerSelectRows.Row(i + 1, c.Name, clan.Length == 0 ? "" : Text(clan)), i);
        }
        int at = Me is null ? LastPlayer.Row(characters, SettingsPath is { } kept ? LastPlayer.Read(kept) : "")
                            : PlayerSelectRows.Chosen(characters, Me);
        if (at >= 0) list.Select(at);
        lastPlayer = -1;
        Describe(pane, "mpps", at >= 0 ? characters[at] : Me);
        menus.PushLayer();
        menus.Show(pane);
        if (characters.Count == 0 && emptyOpensCreate) OpenPlayerCreate();
    }

    void PlayerSelectClick(string name)
    {
        var pane = menus.Pane(PlayerSelectFile);
        int chosen = pane.Lists["mppsPlayerList"].CurrentData;
        switch (name)
        {
            case "mppscreatenewbutton": OpenPlayerCreate(); break;    // opens the character-create screen
            case "mppsokbutton" when chosen >= 0 && chosen < characters.Count:
                Me = characters[chosen];
                // OK and Enter keep the character in use as `lastplayer`.
                if (SettingsPath is { } settings) LastPlayer.Write(settings, Me.Name);
                menus.PopLayer();
                Log?.Invoke($"mp: playing as {Me.Name} ({Me.Template})");
                Bring();
                break;
            case "mppsokbutton":
                // OK with no row: 1.1's OK checks nothing — it hides the screen and goes on to the lobby
                // whoever is loaded; here, back to the room with the character as it was.
                menus.PopLayer();
                break;
            // Delete: asks UI_MPPS_DELETE ("Permanently delete player '%s'?") in a UI_WARNING box.
            case "mppsdeleteplayerbutton" when chosen >= 0 && chosen < characters.Count:
                var doomed = characters[chosen];
                menus.Message(Text("UI_WARNING"), Strings.Format(find, "UI_MPPS_DELETE", "%s" + doomed.Name),
                              ok: true, cancel: true, r =>
                {
                    if (r != 1) return;
                    // Confirmed: drops the loaded character, deletes the file, reopens the select screen.
                    crypt.Delete(doomed.Name);
                    if (Me?.Name == doomed.Name) Me = null;
                    menus.PopLayer();
                    OpenPlayerSelect();
                });
                break;
        }
    }

    /// <summary>
    /// The six text boxes: name, type, clan, status, generation, experience. Serves both the create
    /// screen's five-box refresh on a clan change and the select screen's six-box fill.
    /// </summary>
    void Describe(MenuPane pane, string prefix, NetCharacter? c)
    {
        foreach (var box in new[] { "Name", "Type", "Clan", "Status", "Generation", "Experience" })
            pane.Texts[prefix + box] = "";
        if (c is null) return;
        var actor = Preview(c);
        pane.Texts[prefix + "Name"] = c.Name;
        pane.Texts[prefix + "Type"] = Text(c.Type switch
        {
            ActorType.Ghoul => "UI_GENERAL_GHOUL", ActorType.Human => "UI_GENERAL_HUMAN", _ => "UI_GENERAL_VAMPIRE",
        });
        bool human = c.Type == ActorType.Human;
        // The create screen's clan list: only shown
        // for a human, choosable otherwise (`CreateSummary`).
        if (pane.Lists.TryGetValue(prefix + "ClanList", out var clans)) CreateSummary.ClanList(clans, c.Type);
        pane.Texts[prefix + "Clan"] = human ? Text("UI_GENERAL_NOTAPPLICABLE") : ClanName(c.Clan.Length > 0 ? c.Clan : actor?.Clan ?? "");
        pane.Texts[prefix + "Generation"] = human ? Text("UI_GENERAL_NOTAPPLICABLE")
            : ((int)(actor?.Stat[(int)ActorStat.Generation] ?? 0)).ToString();
        if (actor is not null)
        {
            actor.Type = c.Type;
            pane.Texts[prefix + "Status"] = Text(CharacterPane.RankKey(actor));
            pane.Texts[prefix + "Experience"] = actor.Experience.ToString();
        }
    }

    /// <summary>
    /// The character the template makes, with its `.NPC` sheet on it — the create screen builds one
    /// from the chosen template to show.
    /// </summary>
    Actor? Preview(NetCharacter c)
    {
        var templates = TemplatesOnce();
        if (!templates.Has(c.Template) || Actor.From(templates.Resolve(c.Template)) is not { } actor) return null;
        if (actor.RpgStats.Length > 0 && files.TryRead(NpcFile.PathFor(actor.RpgStats), out var data))
            CharacterSheets.Read(actor, NpcFile.Read(data, actor.RpgStats));     // the reset, then the sheet
        if (c.Experience > 0) actor.Experience = c.Experience;
        return actor;
    }

    TemplateSet? templateSet;
    TemplateSet TemplatesOnce() => templateSet ??= new TemplateSet(files);

    static string ClanName(string clan) => clan.Length == 0 ? "" : char.ToUpperInvariant(clan[0]) + clan[1..];

    /// <summary>The models on offer: `misc/mpcharacters.txt`'s lines, a template and its string key.</summary>
    public List<(string Template, string Key)> Models()
    {
        var found = new List<(string, string)>();
        if (!files.TryRead("misc/mpcharacters.txt", out var data)) return found;
        foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' || line.StartsWith("//")) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && TemplatesOnce().Has(parts[0])) found.Add((parts[0], parts[1]));
        }
        return found;
    }

    /// <summary>
    /// The clans a player may choose: every one not flagged 0x1, "not selectable in MP", **in the clan table's order** (the screen
    /// walks the clan list: <see cref="Clans.Listed"/>, the file backwards) — Caitiff, Ventrue, Tremere, Malkavian,
    /// Giovanni, Toreador, Nosferatu, Gangrel, Cappadocian, Brujah, the first chosen. Earlier, Vitomir sorted them by name,
    /// Brujah first (1.1's screen shows the difference).
    /// </summary>
    public List<Clan> ChoosableClans() => [.. Clans.For(files).Listed.Where(c => (c.Flags & 0x1) == 0)];

    /// <summary>`mpPlayerCreate`: the model and clan lists filled, Kindred ticked, a new character begun.</summary>
    public void OpenPlayerCreate()
    {
        var pane = menus.Pane(PlayerCreateFile);
        var models = pane.Lists["mppcModelList"];
        models.Clear();
        var offered = Models();
        for (int i = 0; i < offered.Count; i++) models.Add(Text(offered[i].Key), i);
        var clans = pane.Lists["mppcClanList"];
        clans.Clear();
        var choosable = ChoosableClans();
        for (int i = 0; i < choosable.Count; i++) clans.Add(ClanName(choosable[i].Name), i);
        models.Select(0);
        clans.Select(0);
        pane.Edits["mppcName"].Limit(NetCharacter.NameLimit);
        pane.Edits["mppcName"].Set("");
        creating = new NetCharacter
        {
            Template = offered.FirstOrDefault().Template ?? "MP_Christof_DA",
            Clan = choosable.FirstOrDefault()?.Name ?? "",
        };
        SetType(ActorType.Vampire);
        lastModel = 0;
        lastClan = 0;
        menus.PushLayer();
        menus.Show(pane);
        Describe(pane, "mppc", creating);
    }

    void SetType(ActorType type)
    {
        creating = creating! with { Type = type };
        checks[(PlayerCreateFile, "mppcKindredCheck")] = type == ActorType.Vampire;
        checks[(PlayerCreateFile, "mppcGhoulCheck")] = type == ActorType.Ghoul;
        checks[(PlayerCreateFile, "mppcHumanCheck")] = type == ActorType.Human;
    }

    void PlayerCreateClick(MenuPane pane, NuiElement e, string name)
    {
        switch (name)
        {
            case "mppckindredcheck": SetType(ActorType.Vampire); break;
            case "mppcghoulcheck": SetType(ActorType.Ghoul); break;
            case "mppchumancheck": SetType(ActorType.Human); break;
            case "mppccreatebutton": Create(); return;
            case "mppccancelbutton": ClosePlayerCreate(); return;
        }
        Describe(pane, "mppc", creating);
    }

    /// <summary>
    /// Create: no name is refused with `UI_MPPC_NONAME`; a name already kept asks
    /// `UI_MPPC_OVERWRITE`; otherwise the character is kept and chosen. (1.1 then opens the
    /// advancement screen on it — not hooked up here.)
    /// </summary>
    void Create()
    {
        if (creating is null) return;
        var pane = menus.Pane(PlayerCreateFile);
        string name = NetCharacter.CleanName(pane.Edits["mppcName"].Text);
        if (name.Length == 0)
        {
            menus.Message(Text("UI_GENERAL_ERROR"), Text("UI_MPPC_NONAME"), ok: true, cancel: false);
            return;
        }
        var made = creating with { Name = name, Clan = creating.Type == ActorType.Human ? "" : creating.Clan, Cash = Rules.GameRules.Current.Items.NewNetCharacterCash };
        void Keep()
        {
            creating = null;
            menus.PopLayer();                              // the create screen
            if (menus.IsUp(menus.Pane(PlayerSelectFile))) menus.PopLayer();   // and the select under it: the new one is chosen
            // The advancement screen opens on the new character, and its close
            // writes it and goes back. Without one, it is written now.
            advancing = made;                              // loaded while the advancement is up
            if (AdvanceNew is { } advance) advance(made, Kept);
            else Kept(made);
        }
        void Kept(NetCharacter final)
        {
            advancing = null;
            crypt.Save(final);
            Me = final;                                    // loaded, and chosen on the select screen; brought by its OK
            Log?.Invoke($"mp: {final.Name} created — {final.Template}, {final.Type}, {(final.Clan.Length > 0 ? final.Clan : "no clan")}"
                        + (final.Saved is { } s ? $", {s.Spendable} left to spend" : ""));
            // **Back to the select screen on the new character** (after Create on LAN, the
            // character selection screen with the new one listed, chosen and shown): it is the one loaded, and the select's OK
            // takes it — writes it as `lastplayer` and brings it to the room's host (<see cref="Bring"/>). Earlier, Vitomir closed
            // the select screen with the create screen, took the new one as chosen and wrote `lastplayer` here.
            OpenPlayerSelect(emptyOpensCreate: false);
        }
        if (crypt.Has(name))
            menus.Message(Text("UI_WARNING"), Text("UI_MPPC_OVERWRITE"), ok: true, cancel: true, r => { if (r == 1) Keep(); });
        else Keep();
    }

    void ClosePlayerCreate()
    {
        creating = null;
        menus.PopLayer();
        if (menus.IsUp(menus.Pane(PlayerSelectFile)))
        {
            menus.PopLayer();
            OpenPlayerSelect(emptyOpensCreate: false);     // Cancel leaves the select as it was
        }
    }

    // ---- the join screen and the game's room: mpChat ---------------------------------------------

    /// <summary>
    /// **Join Game** (ours; 1.1's LAN room, mode 1, made the join screen): the games found on the local network by 1.1's broadcast
    /// discovery on the right, the LAN room's talk on the left; **Join Game** joins the chosen row, **By Address…** (1.1's Change
    /// Room button, WON's) asks for an address, **Back** (its Log Out) goes back to `mpSelect`. No character is asked for here: it
    /// is chosen in the game's room. <paramref name="lan"/> false opens no socket (a headless still, a test).
    /// </summary>
    public void OpenJoin(bool lan = true)
    {
        ShowLobby();
        var pane = menus.Pane(ChatFile);
        pane.Lists["mpChatRoom"].Add(Text("UI_NET_WELCOME"), -1, MessageLog.Yellow);   // in yellow (colour 0xffffff00, as in 1.1)
        SetMode(1);
        if (lan)
        {
            // The room first: its port (the game's plus one) is fixed, and a finder made before it may
            // be handed that very port by a system that gives ports out in order (Windows).
            room ??= new LanRoom(Port);
            finder ??= new LanFinder(Port);
            Log?.Invoke("mp: the join screen — looking for games on the local network");
        }
    }

    /// <summary>The lobby's pane shown afresh: every list cleared, the outputs only shown.</summary>
    void ShowLobby()
    {
        var pane = menus.Pane(ChatFile);
        menus.Show(pane);
        foreach (var l in pane.Lists.Values) l.Clear();
        games = [];
        foreach (var output in (string[])["mpChatRoom", "mpChatRoomStatus", "mpChatGameStatus"])
            if (pane.Lists.TryGetValue(output, out var shown)) shown.Inert = true;    // only shown
    }

    /// <summary>
    /// The lobby's modes: which buttons show, what the title says, which list is the room's
    /// talk and which the game's. The join screen (1) shows Back, Join Game and By Address…; a game's room (2, 4) Leave
    /// Game, Start and Player Setup, and the host's (4) Game Setup (1.1's Create Game button) too.
    /// </summary>
    void SetMode(int mode)
    {
        bool changed = Mode != mode;                             // the lobby acts on a change of mode only
        Mode = mode;
        var pane = menus.Pane(ChatFile);
        pane.Hidden.Clear();
        string[] join = ["mpChatChangeRoomButton", "mpChatLogOutButton", "mpChatJoinGameButton"];
        string[] game = ["mpChatLeaveGameButton", "mpChatStartButton", "mpChatPlayerSetupButton"];
        foreach (var b in mode == 1 ? game : join) pane.Hidden.Add(b);
        if (mode != 4) pane.Hidden.Add("mpChatCreateGameButton");    // Game Setup is the host's
        // The "game chat" title swaps in over "available games" in a game (the pane's own comment).
        if (mode == 1) pane.Hidden.Add("mpChatGameChatTitle");
        Title();
        // Start greyed until every player is ready (`LobbyStart`).
        pane.Grey("mpChatStartButton", !StartLit);
        // The games list cleared in every mode, shown only in a game and choosable
        // in the join screen (`LobbyGamesList`); the join screen's is drawn again from the next answer.
        if (!changed) return;
        pane.Lists["mpChatGame"].Clear();
        pane.Lists["mpChatGame"].Inert = LobbyGamesList.InertIn(mode);
        if (mode == 1) games = [];
    }

    void Title() => menus.Pane(ChatFile).Texts["mpChatTitle"] = Mode == 1 ? Text("UI_NET_LANCHAT")
        : $"{Text("UI_NET_INGAME")} {Server?.Settings.Name ?? Client?.Settings?.Name ?? shotInfo?.Name ?? ""}";

    void LobbyClick(string name)
    {
        var pane = menus.Pane(ChatFile);
        switch (name)
        {
            case "mpchatlogoutbutton" when Mode == 1:
                // Back (Log Out in 1.1): the join screen closed, back to the Host/Join screen.
                CloseLobby();
                Open();
                break;
            case "mpchatjoingamebutton" when Mode == 1:
            {
                // Join Game: the chosen row of the games found. The list's
                // select hook also makes it the game the game pane describes
                // (`GamePane`).
                int row = pane.Lists["mpChatGame"].CurrentData;
                if (row >= 0 && row < games.Count) JoinChosen(games[row]);
                break;
            }
            case "mpchatchangeroombutton" when Mode == 1: AskAddress(); break;     // By Address… (ours)
            case "mpchatcreategamebutton" when Mode == 4: OpenCreateGame(); break;  // Game Setup (ours)
            case "mpchatplayersetupbutton" when Mode is 2 or 4: OpenPlayerSelect(); break;   // Player Setup, over the room
            case "mpchatleavegamebutton" when Mode is 2 or 4:
                // Leave Game: the game left (the host's ended), back to the Host/Join screen.
                Client?.Leave();
                CloseLobby();
                Open();
                break;
            case "mpchatstartbutton" when Mode == 2 && Client is { CanStart: true }:
                // A player's Start: asks the host to be let in (`LobbyStart`).
                Client.AskIn();
                break;
            case "mpchatstartbutton" when Mode == 4 && Server is { ReadyToStart: true, Slots: [{ HasCharacter: true } me, ..] }:
                // Start: the host's game begins; each player comes in by its own Start.
                Server.Start();
                Handoff = new MpHandoff(Server, null, me.Character, Server.Settings) { LoadPath = savedPath };
                break;
        }
    }

    /// <summary>Whether Start is lit: the host's once every player is ready, a player's once it may be let in (`LobbyStart`).</summary>
    bool StartLit => LobbyStart.Lit(Mode == 4, StartStage, Server?.ReadyToStart ?? Client?.CanStart ?? shotReady);

    /// <summary>
    /// **The character brought to the room's host** (the select screen's OK, ours): the host's own is checked by its server and a
    /// player's sent to its host, and a refusal — 1.1's `UI_MPPC_NONAME`, `UI_NET_JOINDUPLICATE`, `UI_NET_BADTEMPLATE` — is a line
    /// in the room, the character in use before it kept. Outside a room nothing is sent.
    /// </summary>
    void Bring()
    {
        if (Me is not { } me) return;
        if (Mode == 4 && Server is { } server)
        {
            if (server.ChooseHost(me) is { } refused)
            {
                Line(Text(refused));
                Me = server.Slots[0] is { HasCharacter: true } kept ? kept.Character : null;
            }
        }
        else if (Mode == 2 && Client is { State: ClientState.Room } client) client.Choose(me);
    }

    /// <summary>A line in the room's list (`mpChatRoom`), where 1.1 puts a refusal of the game's.</summary>
    void Line(string text) => menus.Pane(ChatFile).Lists["mpChatRoom"].Add(text, -1);

    /// <summary>The character Create made, while its advancement is up (still loaded in 1.1, ours).</summary>
    NetCharacter? advancing;

    /// <summary>
    /// Join Game on a found game (`LobbyJoin`): its protocol and its chronicle checked, each refusal
    /// in a `UI_NET_CONNECTIONERROR` box; a password game's password asked for in a box of stars, whose OK
    /// joins with it; any other joined at once.
    /// </summary>
    void JoinChosen(FoundGame game)
    {
        byAddress = false;
        if (LobbyJoin.Check(game.Info, chronicles) is { } refused)
        {
            menus.Message(Text("UI_NET_CONNECTIONERROR"), Text(refused), ok: true, cancel: false);
            return;
        }
        if (LobbyJoin.AsksPassword(game.Info)) AskPassword(game.Host);
        else Join(game.Host);
    }

    /// <summary>`UI_NET_ENTERGAMEPASSWORD` in the input box, stars for what is typed; OK joins with it.</summary>
    void AskPassword(System.Net.IPEndPoint host) =>
        menus.Input(Text("UI_NET_ENTERGAMEPASSWORD"), "", LobbyJoin.PasswordLimit,
                    (result, text) => { if (result == 1) Join(host, text); }, password: true);

    /// <summary>Joined by the address box (ours), and with no password tried yet: a password refusal asks for one.</summary>
    bool byAddress;
    string triedPassword = "";
    System.Net.IPEndPoint? joining;

    /// <summary>
    /// The join: the knock carries no character (protocol 23) — the character already chosen in this run, if any, goes with
    /// the welcome, and otherwise the player chooses one in the room (Player Setup). The player's machine says whether it
    /// has the game's chronicle.
    /// </summary>
    void Join(System.Net.IPEndPoint host, string password = "")
    {
        triedPassword = password;
        joining = host;
        Client?.Dispose();
        Client = new GameClient(new NetPeer(0), host, Me, password)
        {
            Find = find, StartsItself = false, Identity = Net.NetIdentity.Of(files),   // in by Start; the build and data
            HasChronicle = name => chronicles[name] is not null,
        };
        Client.Log += s => Log?.Invoke(s);
        Line(Text("UI_NET_CONNECTING"));
        // The chat display's pane in a game is the game's chat (`mpChatGame` in
        // modes 2 and 4); the game pane below it is the game pane's draw alone.
        var client = Client;
        // The client's own going-line is left to the frame, which shows a refusal once, in a box or in the room.
        Client.Posted += (text, _) => { if (client.State != ClientState.Gone) menus.Pane(ChatFile).Lists[Mode == 1 ? "mpChatRoom" : "mpChatGame"].Add(text, -1); };
        Client.Chatted += (text, _, _) => menus.Pane(ChatFile).Lists["mpChatGame"].Add(text, -1);
        Client.Chosen += refused =>
        {
            if (refused is null) return;
            Line(Text(refused));                                // 1.1's refusal, a line in the room
            Me = client.Me;                                     // the character the host has, if any
        };
        Client.SettingsChanged += () => { Title(); if (!client.ChronicleHere) Line(MpSetup.MissingChronicle(Text(client.Settings!.Chronicle))); };
        Client.Started += () => Handoff = new MpHandoff(null, client, client.Me!, client.Settings!);
        Log?.Invoke($"mp: joining {host}");
    }

    void SayInLobby(string text)
    {
        if (Server is not null) Server.Say(text);
        else if (Client is { State: not ClientState.Gone }) Client.Say(text);
        // The LAN room's own talk: broadcast, and heard back like everyone's.
        else if (room is not null) room.Say(text);
        else Line(LanRoom.Line(Me?.Name ?? "?", text));
    }

    void CloseLobby()
    {
        StopHostKeepAlive();
        Client?.Dispose();
        Client = null;
        Server?.Dispose();
        Server = null;
        finder?.Dispose();
        finder = null;
        room?.Dispose();
        room = null;
        savedPath = null;
        menus.ClearLayers();
    }

    // ---- Host Game ------------------------------------------------------------------------------

    /// <summary>
    /// **Host Game** (ours): the game made at once with the settings kept from the last Game Setup (<see cref="MpSetup.Kept"/>)
    /// or 1.1's create screen's defaults, the port mapped by the router (UPnP, NAT-PMP), and the host in its game's room
    /// (mode 4) — where it chooses its character (Player Setup) and may change the game (Game Setup) before Start. A port
    /// another program holds is said in 1.1's `UI_NET_CREATEGAMEERROR` box over `mpSelect`.
    /// </summary>
    public void HostGame() => Host(MpSetup.Kept(SettingsPath, Offered(), DefaultName()));

    string DefaultName() => MpSetup.DefaultName(find, Me?.Name ?? (SettingsPath is { } kept && LastPlayer.Read(kept) is { Length: > 0 } last ? last : "Host"));

    List<Chronicle> Offered() => [.. chronicles.All.Where(c => c.Kind.HasFlag(NscChronicleFlags.Multiplayer))];

    /// <summary>The host's keep-alive while it is in its room (<see cref="Host(GameSettings)"/>), let go when the room is.</summary>
    IDisposable? hostKeepAlive;

    void StopHostKeepAlive()
    {
        hostKeepAlive?.Dispose();
        hostKeepAlive = null;
    }

    bool Host(GameSettings settings)
    {
        try
        {
            Server = new GameServer(settings, new NetPeer(Port), Me, Vitomir.Engine.Ai.Chance.Game?.NetDice.Next() ?? 1, plays: true)
                { Find = find, Identity = Net.NetIdentity.Of(files) };   // the dice: the launch's; the build and data
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // A port another program holds is said plainly (PortTakenException: the port, --port), in the log as in the box.
            Log?.Invoke($"mp: {ex.Message}");
            menus.Message(Text("UI_NET_CREATEGAMEERROR"), ex.Message, ok: true, cancel: false);
            return false;
        }
        Server.Log += s => Log?.Invoke(s);
        // The host's wire kept alive while it is in its room (ours), as a player's is from the welcome (GameClient): a stalled
        // frame here — a slow machine's, as on a player's — must not cost it the players already in.
        hostKeepAlive?.Dispose();
        hostKeepAlive = Server.Peer.KeepAliveWhile();
        Server.Posted += (text, _) => menus.Pane(ChatFile).Lists["mpChatGame"].Add(text, -1);   // the lobby's chat output in a game
        Server.Heard += (_, text) => menus.Pane(ChatFile).Lists["mpChatGame"].Add(text, -1);
        Server.HostChat += (text, _) => menus.Pane(ChatFile).Lists["mpChatGame"].Add(text, -1);   // with no bar up, the room's list
        Log?.Invoke($"mp: hosting \"{settings.Name}\" — {settings.Chronicle} on UDP {Server.Peer.Port}");
        if (Upnp) _ = Net.Connectors.Current.Offer(Server.Peer.Port, line => Log?.Invoke(line));   // the router asked (the connector seam)
        menus.Hide(menus.Pane(SelectFile));
        ShowLobby();
        SetMode(4);
        // Ours ("Log messages are good to keep"): 1.1's own line, which it writes to the room's null
        // chat output and never shows, shown here in the game's chat (`LobbyLines.Hosting`).
        menus.Pane(ChatFile).Lists["mpChatGame"].Add(LobbyLines.Hosting(Server.Settings.Name, Text), -1);
        if (Me is null) Line(MpSetup.ChooseLine);
        return true;
    }

    // ---- mpCreateGame, the host's Game Setup ------------------------------------------------------

    List<Chronicle> offered = [];

    /// <summary>
    /// **Game Setup** (ours; 1.1's `mpCreateGame`, opened by Create Game in its rooms): the game's name, every multiplayer
    /// chronicle by its name (bit 2 of its flags), the password and the four boxes, and ours, the most players
    /// (<see cref="MostPlayersOption"/>) — filled from the game as it stands (or, with none, as Host Game would make it), its
    /// OK (1.1's Create Game button) changing the game for everybody in the room. 1.1 fills the screen afresh each time:
    /// `UI_NET_DEFAULTGAME`, the first chronicle, no password, Auto Storyteller alone.
    /// </summary>
    public void OpenCreateGame()
    {
        var pane = menus.Pane(CreateGameFile);
        var now = Server?.Settings ?? MpSetup.Kept(SettingsPath, Offered(), DefaultName());
        pane.Edits["mpcgNameEdit"].Limit(32);
        pane.Edits["mpcgNameEdit"].Set(now.Name);
        pane.Edits["mpcgPW"].Limit(32);
        pane.Edits["mpcgPW"].Set(now.Password);
        pane.Edits["mpcgPWConfirm"].Limit(32);
        pane.Edits["mpcgPWConfirm"].Set(now.Password);
        pane.Edits["mpcgPW"].Password = pane.Edits["mpcgPWConfirm"].Password = true;   // style bit 0: stars (EditMask)
        MostPlayersOption.Show(pane, now.MaxPlayers);
        var list = pane.Lists["mpcgChronicleList"];
        list.Clear();
        offered = Offered();
        for (int i = 0; i < offered.Count; i++) list.Add(Text(offered[i].Name), i);
        list.Select(Math.Max(0, offered.FindIndex(c => c.Name.Equals(now.Chronicle, StringComparison.OrdinalIgnoreCase))));
        checks[(CreateGameFile, "mpcgPWProtectCheck")] = now.Password.Length > 0;
        checks[(CreateGameFile, "mpcgCoOpCombative")] = now.Flags.HasFlag(GameFlags.CoOp);
        checks[(CreateGameFile, "mpcgTrackHumanity")] = now.Flags.HasFlag(GameFlags.TrackHumanity);
        checks[(CreateGameFile, "mpcgSTmoderation")] = now.Flags.HasFlag(GameFlags.Moderated);
        checks[(CreateGameFile, "mpcgAutoStoryteller")] = now.Flags.HasFlag(GameFlags.AutoStoryteller);
        menus.PushLayer();
        menus.Show(pane);
        pane.Focus = "mpcgNameEdit";
    }

    void CreateGameClick(MenuPane pane, NuiElement e, string name)
    {
        if (e.Type == "checkbox")
        {
            var key = (CreateGameFile, e.Name);
            checks[key] = !checks.GetValueOrDefault(key);
            // With neither Storyteller box ticked the frame hook ticks one back.
            if (!checks.GetValueOrDefault((CreateGameFile, "mpcgSTmoderation")) && !checks.GetValueOrDefault((CreateGameFile, "mpcgAutoStoryteller")))
                checks[(CreateGameFile, "mpcgAutoStoryteller")] = true;
            return;
        }
        switch (name)
        {
            case "mpcgcreategamebutton": CreateGame(); break;
            case "mpcgcancelbutton": CloseCreateGame(); break;
            case "mpcgloadsavedbutton":
                // Load Saved: the load screen, over the network saves.
                if (NetSaves is null) break;
                var keep = menus.SaveSlots;
                menus.SaveSlots = NetSaves;
                menus.OpenLoad();
                menus.SaveSlots = keep;
                loadingSaved = true;
                break;
        }
    }

    /// <summary>The network saves (`MP<n>`), which mpCreateGame's Load Saved lists; null hides nothing but loads nothing.</summary>
    public Save.SaveSlotFiles? NetSaves { get; set; }
    /// <summary>The load screen is up for Load Saved, and its Load is ours.</summary>
    bool loadingSaved;
    /// <summary>The save the game made by Load Saved starts from.</summary>
    string? savedPath;

    /// <summary>
    /// Load Saved's Load (on a network session): the save read and the game set
    /// from the create screen as its OK sets it — its name, password and boxes — but
    /// at the chronicle's revive location, which is where every player's machine is
    /// sent (the place the save was taken in). The save fixes the chronicle.
    /// </summary>
    void LoadSaved(int slot)
    {
        if (NetSaves is null) return;
        string path = NetSaves.PathOf(slot);
        Save.SaveGame saved;
        try { saved = Save.SaveArchive.Read(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            menus.Message(Text("UI_NET_CREATEGAMEERROR"), e.Message, ok: true, cancel: false);
            return;
        }
        // At the chronicle's revive location, and only a network game's save.
        if (Net.NetSaves.Start(saved, chronicles, out string why) is not { } start)
        {
            menus.Message(Text("UI_NET_CREATEGAMEERROR"), why, ok: true, cancel: false);
            return;
        }
        CreateGame(s => Net.NetSaves.Settings(s, saved, start), path);
    }

    /// <summary>
    /// Game Setup's OK (1.1's Create Game): a name is required (`UI_NET_PLEASEENTERNAME`), the two passwords must
    /// match (`UI_NET_PWMISMATCH`), the most players must be 2 to 8 (ours, <see cref="MostPlayersOption"/>; the
    /// game's limit), the four boxes become the game's flags — and the host's game is changed for everybody in the room
    /// (<see cref="GameServer.Change"/>), or, with no game yet, made (<see cref="Host"/>). What was set is kept for the next
    /// Host Game (<see cref="MpSetup.Keep"/>, never the password). <paramref name="from"/> puts it where a save was taken (Load Saved).
    /// </summary>
    void CreateGame(Func<GameSettings, GameSettings>? from = null, string? save = null)
    {
        var pane = menus.Pane(CreateGameFile);
        // The length alone is tested, so a name of spaces is a name, and it is kept as typed
        // (Vitomir trimmed it and refused it until then).
        string name = pane.Edits["mpcgNameEdit"].Text;
        if (name.Length == 0)
        {
            menus.Message(Text("UI_NET_CREATEGAMEERROR"), Text("UI_NET_PLEASEENTERNAME"), ok: true, cancel: false);
            return;
        }
        string password = "";
        if (checks.GetValueOrDefault((CreateGameFile, "mpcgPWProtectCheck")) && pane.Edits["mpcgPW"].Text.Length > 0)
        {
            if (pane.Edits["mpcgPW"].Text != pane.Edits["mpcgPWConfirm"].Text)
            {
                menus.Message(Text("UI_NET_CREATEGAMEERROR"), Text("UI_NET_PWMISMATCH"), ok: true, cancel: false);
                return;
            }
            password = pane.Edits["mpcgPW"].Text;
        }
        // Ours: the most players, 2 to 8, refused in the same box as a missing name.
        if (!MostPlayersOption.Parse(pane.Edits[MostPlayersOption.Edit].Text, out int most))
        {
            menus.Message(Text("UI_NET_CREATEGAMEERROR"), MostPlayersOption.Refusal, ok: true, cancel: false);
            return;
        }
        int row = pane.Lists["mpcgChronicleList"].CurrentData;
        if (row < 0 || row >= offered.Count) return;
        var flags = GameFlags.None;
        if (checks.GetValueOrDefault((CreateGameFile, "mpcgCoOpCombative"))) flags |= GameFlags.CoOp;
        if (checks.GetValueOrDefault((CreateGameFile, "mpcgTrackHumanity"))) flags |= GameFlags.TrackHumanity;
        if (checks.GetValueOrDefault((CreateGameFile, "mpcgSTmoderation"))) flags |= GameFlags.Moderated;
        if (checks.GetValueOrDefault((CreateGameFile, "mpcgAutoStoryteller"))) flags |= GameFlags.AutoStoryteller;
        var settings = GameSettings.For(offered[row], name, flags, password, most);
        if (from is not null) settings = from(settings);
        if (Server is { } server)
        {
            // The game's own flags the Storyteller sets (the locks) are kept; the boxes are the screen's.
            server.Change(settings with { Flags = settings.Flags | (server.Flags & ~(MpSetup.Boxes | GameFlags.Password)) });
            Title();
            CloseCreateGame();
        }
        else
        {
            // No game yet (a headless still's screen, a test): the game is made with what was set.
            CloseCreateGame();
            if (!Host(settings)) return;
        }
        savedPath = save;
        if (SettingsPath is { } kept) MpSetup.Keep(kept, settings);   // the last game set, for the next Host Game (ours)
        Log?.Invoke($"mp: the game set — \"{name}\", {offered[row].Name}{(save is null ? "" : ", from " + Path.GetFileName(save))}");
    }

    void CloseCreateGame() => menus.PopLayer();

    // ---- every frame -----------------------------------------------------------------------------

    /// <summary>
    /// One frame: the network pumped (the LAN's answers, the game's messages), the lists refreshed —
    /// the games found (`"%s (%d)"`), the players in the game — and the character screens' lists
    /// followed as the player moves along them.
    /// </summary>
    public void Frame(int nowMs)
    {
        if (finder is not null && Mode == 1)
        {
            finder.Poll();
            var now = finder.Games.ToList();
            if (!now.Select(g => g.Info.Row + g.Host).SequenceEqual(games.Select(g => g.Info.Row + g.Host)))
            {
                // Redraws the games list "%s (%d)", a choice kept only when there was one.
                LobbyGamesList.Redraw(menus.Pane(ChatFile).Lists["mpChatGame"], games, now);
                games = now;
            }
        }
        // Load Saved's list: its Load is ours (a network save to host from), its Cancel just closes it.
        if (loadingSaved)
        {
            if (menus.Request == MenuRequest.Restore) { var (_, slot) = menus.TakeRequest(); loadingSaved = false; LoadSaved(slot); }
            else if (!menus.IsUp(menus.Pane(FrontEnd.LoadFile))) loadingSaved = false;
        }
        // The LAN room's talk is heard on the join screen, `<name> text` in the list's own colour (message kind 8); this
        // player says it is there every 2 s once it has a name (message 0x4e) — the character's, else the last one kept.
        if (room is not null)
        {
            room.Name = Me?.Name ?? (SettingsPath is { } kept && LastPlayer.Read(kept) is { Length: > 0 } last ? last : null);
            foreach (var line in room.Poll())
                if (Mode == 1) Line(line);
        }
        Server?.Poll(nowMs);
        if (Client is { } c)
        {
            c.Poll(0.03f);
            if (c.State == ClientState.Room && Mode != 2)
            {
                SetMode(2);
                // The host's answer: "You have joined the game …" in the game's chat (`LobbyLines`).
                menus.Pane(ChatFile).Lists["mpChatGame"].Add(LobbyLines.Joined(c.Settings?.Name ?? "", Text), -1);
                if (c.Me is null) Line(MpSetup.ChooseLine);
                if (!c.ChronicleHere) Line(MpSetup.MissingChronicle(Text(c.Settings?.Chronicle ?? "")));
            }
            if (c.State == ClientState.Gone)
            {
                // A refused join (`LobbyJoin`): the session's own refusals in an error box, the
                // game's answer's as a line in the room (`mpChatRoom`); a knock nobody answered (ours) names the address.
                string reason = c.Reason ?? "UI_NET_JOINFAILED";
                Client = null;
                SetMode(1);
                if (reason == "UI_NET_WRONGGAMEPASSWORD" && byAddress && triedPassword.Length == 0 && joining is { } again)
                    AskPassword(again);     // ours: an address has no game record to say it wants a password
                else if (reason == "UI_NET_BADADDR" && joining is { } silent)
                {
                    Vitomir.Platform.Presentation.StreamerMode.Hide(silent.ToString());
                    Log?.Invoke($"mp: {MpSetup.NoAnswer(silent)}");
                    menus.Message(Text("UI_NET_CONNECTIONERROR"), MpSetup.NoAnswer(silent), ok: true, cancel: false);
                }
                else if (LobbyJoin.Box(reason) is { } box)
                    menus.Message(Text(box.Title), Text(box.Text), ok: true, cancel: false);
                else Line(Text(reason));
                // What differs, when the host refused this machine's build or data (NetIdentity), under 1.1's line.
                if (c.Refusal is { } differs) Line(differs);
                byAddress = false;
            }
        }
        if (Mode is 2 or 4)
        {
            Title();                                                 // the game's name, which Game Setup may change
            LobbyStart.Show(menus.Pane(ChatFile), StartLit);         // each frame
        }
        StatusPanes();
        FollowCharacterScreens();
    }

    /// <summary>The session's stage as this machine has it (the player's, from the host; a headless still's own).</summary>
    byte StartStage => Client?.Stage ?? shotStage;
    /// <summary>The stage a headless still in a game shows (`--menu-open mpgameunderway`).</summary>
    byte shotStage;
    /// <summary>Whether a headless still's room is ready to start.</summary>
    bool shotReady;

    /// <summary>The LAN room's players as a headless still shows them (`--menu-open mplobby`), with no room open.</summary>
    List<string>? shotRoom;
    /// <summary>A game's players (an empty name still choosing) and whether it is forming, for a headless still in a game (`--menu-open mpgameroom`).</summary>
    (bool Forming, List<(int Slot, string Name, bool Missing)> Players)? shotGame;
    /// <summary>The game a headless still's room is in.</summary>
    GameInfo? shotInfo;

    /// <summary>
    /// The lobby's two lower lists, drawn from the state and rewritten only when a line differs. On the join screen
    /// `mpChatRoomStatus` is the LAN room's players as 1.1 draws them and `mpChatGameStatus` the chosen row's game; in a
    /// game's room (ours) `mpChatRoomStatus` ("players in room") is the game's players, each by its character's name or
    /// *choosing a character*, and `mpChatGameStatus` whether the game is forming or under way and what it is — its name,
    /// chronicle and options, which the host's Game Setup may change.
    /// </summary>
    void StatusPanes()
    {
        var pane = menus.Pane(ChatFile);
        List<string>? players = null, lines = null;
        if (Mode == 1)
        {
            if ((room?.Players ?? shotRoom) is { } names) players = LanRoom.Rows(names, Text);
            lines = LobbyGamePane.Chosen(LobbyGamePane.Choose(games, pane.Lists["mpChatGame"].CurrentData)?.Info, Text);
        }
        else if (Server is { } server)
        {
            players = LobbyGamePane.Players(server.Players.Select(p => (p.Slot, p.Character.Name, !p.HasChronicle)), Text);
            lines = LobbyGamePane.InGame(!server.Started, Info(server.Settings), Text);
        }
        else if (Client is { Settings: { } settings } client)
        {
            players = LobbyGamePane.Players(client.Players.Select(p => (p.Slot, p.Name, p.Slot == client.Slot && !client.ChronicleHere)), Text);
            lines = LobbyGamePane.InGame(client.Stage != Net.SessionStage.UnderWay, Info(settings), Text);   // the stage
        }
        else if (shotGame is { } shot)
        {
            players = LobbyGamePane.Players(shot.Players, Text);
            lines = LobbyGamePane.InGame(shot.Forming, shotInfo, Text);
        }
        if (players is not null) Redraw(pane.Lists["mpChatRoomStatus"], players);
        if (lines is not null) Redraw(pane.Lists["mpChatGameStatus"], lines);

        static GameInfo Info(GameSettings s) => new(s.Name, s.Chronicle, 0, s.MaxPlayers, false, 0, s.Flags);

        static void Redraw(MenuList list, List<string> rows)
        {
            if (list.Rows.Select(r => r.Text).SequenceEqual(rows)) return;
            list.Clear();
            foreach (var r in rows) list.Add(r, -1);
        }
    }

    /// <summary>
    /// The character the portrait window shows: the one being made on
    /// the create screen, over the select screen's chosen one; null when neither screen is up.
    /// </summary>
    public string? PortraitTemplate
    {
        get
        {
            if (creating is not null && menus.IsUp(menus.Pane(PlayerCreateFile))) return creating.Template;
            var select = menus.Pane(PlayerSelectFile);
            if (!menus.IsUp(select)) return null;
            return characters.ElementAtOrDefault(select.Lists["mppsPlayerList"].CurrentData)?.Template;
        }
    }

    /// <summary>
    /// The pane whose draw hook draws the portrait (the same hook on both screens;
    /// <see cref="MenuPass.Hook"/>): the create screen while it is up, else the select screen while it is up, else none — the
    /// same choice as <see cref="PortraitTemplate"/>. The model is drawn right after that pane, so a dialog over it (the
    /// Internet button's address box, a message box) covers it, as in 1.1.
    /// </summary>
    public bool DrawsPortrait(MenuPane pane)
    {
        var create = menus.Pane(PlayerCreateFile);
        if (creating is not null && menus.IsUp(create)) return ReferenceEquals(pane, create);
        var select = menus.Pane(PlayerSelectFile);
        return ReferenceEquals(pane, select) && menus.IsUp(select);
    }

    void FollowCharacterScreens()
    {
        var select = menus.Pane(PlayerSelectFile);
        // **No character, no select screen** (the portrait's draw hook, the select screen's too: with nobody loaded
        // and the select pane made, it pops the layer and goes on to the lobby). An empty folder's create screen given up —
        // Cancel or Escape put back the character in use before it, nobody — so gives the select screen up too, and the
        // room is left as it was (ours: 1.1's lobby with nobody loaded goes to the main menu; the room waits for a character).
        if (menus.IsUp(select) && Me is null && characters.ElementAtOrDefault(select.Lists["mppsPlayerList"].CurrentData) is null)
        {
            menus.PopLayer();
            Log?.Invoke("mp: no character chosen — the select screen put away");
        }
        if (menus.IsUp(select))
        {
            int at = select.Lists["mppsPlayerList"].CurrentData;
            // The character list's select: loads the chosen character and fills the six boxes.
            if (at != lastPlayer) { lastPlayer = at; Describe(select, "mpps", characters.ElementAtOrDefault(at)); }
        }
        var create = menus.Pane(PlayerCreateFile);
        if (creating is not null && menus.IsUp(create))
        {
            int model = create.Lists["mppcModelList"].CurrentData, clan = create.Lists["mppcClanList"].CurrentData;
            if (model != lastModel || clan != lastClan)
            {
                lastModel = model;
                lastClan = clan;
                var models = Models();
                var clans = ChoosableClans();
                // The clan list's select: sets the new character's clan and
                // refreshes the five text boxes below.
                creating = creating with
                {
                    Template = model >= 0 && model < models.Count ? models[model].Template : creating.Template,
                    Clan = clan >= 0 && clan < clans.Count ? clans[clan].Name : creating.Clan,
                };
                Describe(create, "mppc", creating);
            }
        }
    }

    /// <summary>
    /// The check boxes to draw: each box's `checkedbitmap` when ticked, its `bitmap` otherwise, for
    /// every pane of these that is up.
    /// </summary>
    public IEnumerable<(string Art, float X, float Y, float W, float H)> Checkboxes() => menus.Visible().SelectMany(Checkboxes);

    /// <summary>
    /// One pane's boxes and pictures (<see cref="Checkboxes()"/>), for the front end's pass to draw right after that pane
    /// (<see cref="MenuPass.Pictures"/>), so a dialog over the pane covers them as 1.1's pane order does.
    /// </summary>
    public IEnumerable<(string Art, float X, float Y, float W, float H)> Checkboxes(MenuPane pane)
    {
        if (menus.Options?.Owns(pane) == true) yield break;   // the Options screens' and the Fixes page's boxes are the front end's pass's
        foreach (var e in pane.Nui.Elements.Where(e => e.Type is "checkbox" or "static" && e.Pos.Length > 1 && e.Size.Length > 1
                                                       && !pane.Hidden.Contains(e.Name)))
        {
            if (e.Type == "static")
            {
                // A picture on the pane (`mpChatGameChatTitle`); the front end's pass draws none.
                if (e.Get("bitmap") is { Length: > 0 } picture)
                    yield return (picture, pane.X + e.Pos[0], pane.Y + e.Pos[1], e.Size[0], e.Size[1]);
                continue;
            }
            bool on = checks.GetValueOrDefault((pane.File, e.Name));
            if ((on ? e.Get("checkedbitmap") : e.Get("bitmap")) is { Length: > 0 } art)
                yield return (art, pane.X + e.Pos[0], pane.Y + e.Pos[1], e.Size[0], e.Size[1]);
        }
    }

    /// <summary>Whether a box is ticked (for the tests and the harness).</summary>
    public bool Checked(string pane, string box) => checks.GetValueOrDefault((pane, box));

    /// <summary>Check-box states keyed case-insensitively on both halves.</summary>
    sealed class CheckBoxes
    {
        readonly Dictionary<string, bool> on = new(StringComparer.OrdinalIgnoreCase);
        static string Key((string Pane, string Box) k) => k.Pane + "/" + k.Box;
        public bool this[(string Pane, string Box) k] { get => on.GetValueOrDefault(Key(k)); set => on[Key(k)] = value; }
        public bool GetValueOrDefault((string Pane, string Box) k) => on.GetValueOrDefault(Key(k));
    }

    /// <summary>Takes the hand-off: the lobby lets go of the session it gives the game.</summary>
    public MpHandoff? TakeHandoff()
    {
        var h = Handoff;
        if (h is null) return null;
        Handoff = null;
        StopHostKeepAlive();                                    // the game polls the host's wire from here
        Server = null;
        Client = null;
        finder?.Dispose();
        finder = null;
        room?.Dispose();
        room = null;
        return h;
    }

    /// <summary>For a headless still: one of the screens opened with made-up contents (`--menu-open mp…`).</summary>
    public void Shot(string? what)
    {
        switch (what?.ToLowerInvariant())
        {
            case "mpselect": menus.Hide(menus.Pane(FrontEnd.MainMenuFile)); Open(); break;
            case "mplobby":
                // The join screen: two games found, the second chosen, and the LAN room's three players.
                menus.Hide(menus.Pane(FrontEnd.MainMenuFile));
                Me = new NetCharacter { Name = "Anna", Template = "MP_Serena" };
                OpenJoin(lan: false);
                var here = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, NetPeer.DefaultPort);
                games =
                [
                    new FoundGame(new GameInfo("Anna's game", "MP_CurseDarkness", 2, 4, false, NetPeer.DefaultPort, GameFlags.AutoStoryteller | GameFlags.CoOp), here),
                    new FoundGame(new GameInfo("Midnight at the Inn", "MP_DAInn", 1, 4, false, NetPeer.DefaultPort,
                                               GameFlags.Password | GameFlags.Moderated | GameFlags.Locked), here),
                ];
                var gameRows = menus.Pane(ChatFile).Lists["mpChatGame"];
                for (int i = 0; i < games.Count; i++) gameRows.Add(games[i].Info.Row, i);
                gameRows.Select(1);
                shotRoom = ["Anna", "Bob", "Cecilie"];
                Line(LanRoom.Line("Bob", "anyone for Temesvar?"));
                break;
            case "mpjoinpassword":
                // Join Game on the password game: its password asked for in a box of stars.
                Shot("mplobby");
                AskPassword(games[1].Host);
                menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"].Set("garlic");
                break;
            case "mpjoinaddress":
                // By Address…: the address box, empty (nothing kept), an address typed.
                Shot("mplobby");
                AskAddress();
                menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"].Set("192.168.1.20:4994");
                break;
            case "mpnoanswer":
                // A join nobody answered, said within five seconds.
                Shot("mplobby");
                menus.Message(Text("UI_NET_CONNECTIONERROR"),
                              MpSetup.NoAnswer(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.20"), NetPeer.DefaultPort)), ok: true, cancel: false);
                break;
            case "mplobbyfound":
                // The same two games, nobody having clicked a row: none chosen, the game pane empty.
                Shot("mplobby");
                LobbyGamesList.Redraw(menus.Pane(ChatFile).Lists["mpChatGame"], [], games);
                break;
            case "mpplayercreatehuman":
                // Human ticked: the clan list only shown, the clan and generation "not applicable".
                Shot("mpplayercreate");
                SetType(ActorType.Human);
                Describe(menus.Pane(PlayerCreateFile), "mppc", creating);
                break;
            case "mpgameroom":
                // The host's room: Anna has her character, Bob is still choosing, Cecilie's install lacks the chronicle — Start greyed.
                ShotRoom("Anna", 4, ready: false, forming: true);
                shotGame = (true, [(0, "Anna", false), (1, "", false), (2, "Cecilie", true)]);
                menus.Pane(ChatFile).Lists["mpChatGame"].Add(LobbyLines.Hosting("Anna's game", Text), -1);   // ours
                menus.Pane(ChatFile).Lists["mpChatGame"].Add("Cecilie says 'which chronicle is this?'", -1);
                break;
            case "mpgameready":
                // The host's room with everybody ready: Start lit.
                ShotRoom("Anna", 4, ready: true, forming: true);
                shotGame = (true, [(0, "Anna", false), (1, "Bob", false)]);
                menus.Pane(ChatFile).Lists["mpChatGame"].Add(LobbyLines.Hosting("Anna's game", Text), -1);
                menus.Pane(ChatFile).Lists["mpChatGame"].Add("Bob says 'ready when you are'", -1);
                break;
            case "mpgameunderway":
                // A player who has joined a game under way, with a character: "in progress", Start lit to be let in.
                ShotRoom("Bob", 2, ready: true, forming: false);
                shotStage = Net.SessionStage.UnderWay;
                shotGame = (false, [(0, "Anna", false), (1, "Bob", false)]);
                menus.Pane(ChatFile).Lists["mpChatGame"].Add(LobbyLines.Joined("Anna's game", Text), -1);
                break;
            case "mpgameforming":
                // The same player in a game still forming: Start greyed.
                Shot("mpgameunderway");
                shotStage = Net.SessionStage.Forming;
                shotReady = false;
                shotGame = (true, [(0, "Anna", false), (1, "Bob", false)]);
                break;
            case "mpcreategame":
                // Game Setup over the host's room.
                ShotRoom("Anna", 4, ready: false, forming: true);
                shotGame = (true, [(0, "Anna", false)]);
                OpenCreateGame();
                break;
            case "mpplayercreate":
                ShotRoom("Anna", 4, ready: false, forming: true);
                OpenPlayerCreate();
                menus.Pane(PlayerCreateFile).Edits["mppcName"].Set("Anna");
                break;
            case "mpplayerselect":
                // Player Setup over the room: the select screen (an empty folder: the create screen over it).
                ShotRoom(null, 4, ready: false, forming: true);
                OpenPlayerSelect();
                break;
            case "mpload":
                menus.ClearLayers();
                menus.Show(menus.Pane(LoadFile));
                break;
        }
    }

    /// <summary>A headless still's room: the lobby's pane in a game's mode, with nobody connected.</summary>
    void ShotRoom(string? me, int mode, bool ready, bool forming)
    {
        menus.Hide(menus.Pane(FrontEnd.MainMenuFile));
        Me = me is null ? null : new NetCharacter { Name = me, Template = "MP_Serena" };
        shotInfo = new GameInfo("Anna's game", "MP_CurseDarkness", 2, 4, !forming, NetPeer.DefaultPort, GameFlags.AutoStoryteller | GameFlags.CoOp);
        shotReady = ready;
        ShowLobby();
        SetMode(mode);
    }
}
