// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A network game inside the ordinary game run: the host's or a player's
// session tied to the world `Program.cs` builds, the chat typed and shown, the multiplayer escape
// menu, and — for the host — the Storyteller's seat. `Program.cs` calls in at four places: where the
// place is chosen, once the world is built, once a frame, and for each input event.
//
//   --host [port]            host a game in this window, playing in it (the host is slot 0)
//   --join <host[:port]>     join a game (1.1's LAN or Internet join; the address replaces WON)
//   --name <text>            the player's name     --player-template <t>   the character's model
//   --mp-chronicle <name>    (host) the chronicle — see NetRuns for the rest of the game's options
//   --st [boxes]             (host) take the Storyteller's seat at once, with these panes up (for a still)
//   --net-say <line>         say this line as soon as the game is joined (a headless test)
//   (--mp-game is the run's own relaunch into a network game's next place, never typed)
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Classic;
using Vitomir.Platform.Modules;

namespace Vitomir;

public sealed unsafe class NetPlay : IDisposable
{
    /// <summary>
    /// A session the front end made (the lobby's host or join) and handed to the game run it starts —
    /// the one thing that outlives a run, because the connections must (ours).
    /// </summary>
    public static NetPlay? Carried { get; set; }

    /// <summary>
    /// Whether this run plays a network game — which puts the host's saves in the network family
    /// (`MP<n>`, 1.1's `SaveGames\MP<n>` while a session is up).
    /// </summary>
    public static bool Active { get; private set; }

    public GameServer? Server { get; private init; }
    public GameClient? Client { get; private init; }
    /// <summary>This machine's character, as it was brought and — after each advancement and on leaving — as it now stands (<see cref="Keep"/>).</summary>
    public required NetCharacter Me { get; set; }
    public required GameSettings Settings { get; init; }
    /// <summary>Whether the game's key hook is not reading the chat's keys now (<see cref="ChatKeys.Read"/>): not the game camera, a dialog, the vault, the advancement screen, a shop, a conversation.</summary>
    public Func<bool>? Busy { get; set; }
    /// <summary>The game's advancement screen, for the Storyteller's Advancement button.</summary>
    public Action<WorldThing>? Advance
    {
        get => advance;
        set { advance = value; if (Story is not null) Story.Advance = Walk; }
    }
    Action<WorldThing>? advance;

    /// <summary>
    /// The host's advancement walk: the screen opened on slot 0's character with every
    /// player's to follow (<see cref="WalkParty"/>) — the Storyteller's Advancement button and a
    /// script's `Advance(0)`.
    /// </summary>
    void Walk(WorldThing me)
    {
        walking = true;
        try { advance?.Invoke(me); }
        finally { walking = false; }
    }
    bool walking;

    /// <summary>This machine's rules for an advancement screen in this game.</summary>
    public Engine.Rpg.NetAdvanceRules AdvanceRules() =>
        new(Moderated: (Server?.Flags ?? Client?.Settings?.Flags ?? GameFlags.None).HasFlag(GameFlags.Moderated),
            Host: IsHost, Seated: Server?.Storyteller == true, Mine: Client?.Avatar ?? Server?.Slots[0]?.Avatar,
            Lowering: () => (InputTape.Mods() & LoopMods.Shift) != 0);

    /// <summary>Whom an advancement opened now walks, when it is the host's walk: slot 0's character and then each player's; else null (the party's).</summary>
    public IReadOnlyList<WorldThing>? WalkParty(WorldThing asked) =>
        walking && Server is { } server && ReferenceEquals(asked, server.Slots[0]?.Avatar) ? server.Texts.WalkList() : null;
    /// <summary>`--st [boxes]`: take the Storyteller's seat at once with these panes up, for a headless still (ours).</summary>
    public string? StoryShot { get; set; }
    /// <summary>A line to say as soon as the game is joined (`--net-say`, for a headless test; ours).</summary>
    public string? SayOnJoin { get; set; }
    /// <summary>Why the session could not start, in the string table's words.</summary>
    public string? Failed { get; private set; }

    public bool IsClient => Client is not null;
    public bool IsHost => Server is not null;
    public bool Paused => Server?.Paused ?? Client?.Paused ?? false;
    /// <summary>
    /// Where this machine plays: the host's word for it — a player's machine takes the place it was last
    /// sent (`Msg.Travel`: the whole game's journey, or this player's alone), not the lobby's.
    /// </summary>
    GameSettings Where => Client?.Settings ?? Settings;
    public string Level => Where.Level;
    public string? Scene => Where.Scene.Length > 0 ? Where.Scene : null;
    public int Entrance => Where.Entrance;
    public string Template => Me.Template;

    /// <summary>The chat box's lines (`sbChatbox`, 100 of them).</summary>
    public MessageLog Chat { get; } = new();
    /// <summary>What is being typed into `sbChatEdit`, or null.</summary>
    public string? Typing { get; private set; }

    /// <summary>The Storyteller's seat — the host's alone ("Only the server can switch to Storyteller mode.").</summary>
    public Storyteller? Story { get; private set; }

    /// <summary>
    /// The Storyteller's camera gone with a body it carried to another place, set
    /// by the windowed host's run; the Storyteller hands it the body (<see cref="Storyteller.Follow"/>).
    /// </summary>
    public StoryView? View { get; set; }

    bool Follow(WorldThing body) => View?.Follow(body) == true;

    Location? place;
    MessageBox? chatBox;
    bool wired;
    (Strings Strings, UiCanvas Ui, Session Game, GameFiles Files, Chronicle? Chronicle, Scripting.CodexScenes? Scenes,
     StatusBarPass Hud, Func<WorldThing?> Hovered)? storyParts;

    /// <summary>
    /// A player's machine told by the host that the game has moved (`Msg.Travel`): the run ends and
    /// the next one loads the new place with this session carried over, as a load does.
    /// </summary>
    public bool WantsReload => Client is { State: ClientState.Loading } && place is not null && !left;

    /// <summary>
    /// The host's game has moved (the chronicle's travel, `Program.cs`'s `Arrived`): the session
    /// follows it, and a Storyteller in the seat gets up and sits down again in the new place.
    /// </summary>
    public void Travelled(Location went, WorldThing? me)
    {
        if (Server is not { } server) return;
        bool seated = Story?.On == true;
        if (seated) Story!.Leave();
        var left = place;
        place = went;
        // The host's own exit, teleporter or Storyteller's jump takes the host alone (slot 0's move)
        // — every other player stays wherever they are, and those in the place it left play
        // on, its scripts running for them; a script's jump or the advance takes everybody.
        if (server.Journey(went, me, alone) && left is not null) storyParts?.Scenes?.KeepRunning(left);
        if (storyParts is { } p && pass is not null)
        {
            Story = new Storyteller(server, went, p.Strings, p.Ui.Assets, p.Game, p.Files, p.Chronicle, p.Scenes, p.Hud, p.Hovered) { Advance = advance, Follow = Follow, Camera = () => View?.Camera };
            pass.Panes = new MenuPass(p.Ui, Story.Menus) { Pictures = Story.Checkboxes };   // each pane's boxes right after it
            pass.Extra = null;
            if (seated) Story.Take();
        }
    }
    Session? session;
    bool alone;

    /// <summary>
    /// The journey the chronicle has pending, looked at just before it is taken (`Program.cs`): the
    /// host's own exit, teleporter or Storyteller's jump goes alone (`GameServer.Alone`), anything else takes the whole game.
    /// </summary>
    public void Journey(SequenceRequest? pending) => alone = Server?.Alone(pending) == true;
    Func<string, string?> find = _ => null;
    NetPass? pass;
    FrontEnd? menus;
    int caretAt;

    /// <summary>A session the lobby made, for the game run it starts.</summary>
    public static NetPlay From(MpHandoff h) => new() { Me = h.Me, Settings = h.Settings, Server = h.Server, Client = h.Client };

    /// <summary>The session the command line (or the front end) asks for, or null for single player.</summary>
    /// <summary>Whether this run hosts a network game — `--host`, or a game the multiplayer menus handed it as its host — before <see cref="Begin"/>.</summary>
    public static bool Hosting(string[] args) => args.Contains("--host") || Carried?.Server is not null;

    public static NetPlay? Begin(string[] args, GameFiles files, ChronicleSet chronicles)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
        }
        if (Carried is { } carried) { Carried = null; Active = true; return carried; }
        var play = Open(args, files, chronicles, Arg);
        Active = play is { Failed: null };
        if (play is not null)
        {
            play.SayOnJoin = Arg("--net-say");
            if (args.Contains("--st")) play.StoryShot = Arg("--st") ?? "";
        }
        return play;
    }

    static NetPlay? Open(string[] args, GameFiles files, ChronicleSet chronicles, Func<string, string?> Arg)
    {
        bool host = args.Contains("--host"), join = args.Contains("--join");
        if (!host && !join) return null;
        var strings = new Strings(files);
        var me = new NetCharacter
        {
            Name = NetCharacter.CleanName(Arg("--name") ?? Environment.UserName),
            Template = Arg("--player-template") ?? "MP_Christof_DA",
        };
        // A character kept under that name comes as it was left — its sheet, disciplines and pack
        // (ours: the front end's character select is 1.1's only way in); `--player-template` still
        // picks the model.
        if (new Crypt(InputTape.CryptRead(CryptFolder, me.Name)).All().FirstOrDefault(c => c.Name.Equals(me.Name, StringComparison.OrdinalIgnoreCase)) is { } kept)   // a tape's
        {
            me = Arg("--player-template") is { } model ? kept with { Template = model } : kept;
            Console.WriteLine($"net: {me.Name} is the character kept in {CryptFolder}{(me.Saved is null ? "" : ", with its sheet")}");
        }
        if (host)
        {
            var settings = NetRuns.Settings(args, chronicles, me.Name);
            if (settings is null) return new NetPlay { Me = me, Settings = new GameSettings(), Failed = "no such chronicle" };
            // `--load <save>` with `--host`: the game is made as mpCreateGame's Load Saved makes it —
            // at the chronicle's revive location, so every player's machine
            // loads that place — and only from a network game's save (NetSaves.Start).
            if (Arg("--load") is { } loadPath && Engine.Save.SaveArchive.Read(ResolveSave(loadPath)) is { } saved)
            {
                if (Engine.Net.NetSaves.Start(saved, chronicles, out string why) is not { } start)
                    return new NetPlay { Me = me, Settings = new GameSettings(), Failed = why };
                settings = Engine.Net.NetSaves.Settings(settings, saved, start);
            }
            int port = int.TryParse(Arg("--host"), out var p) ? p : int.TryParse(Arg("--port"), out var p2) ? p2 : NetPeer.DefaultPort;
            NetPeer hostPeer;
            try { hostPeer = new NetPeer(port); }
            catch (PortTakenException taken)
            {
                // One plain line, in the log as on the console, and the run ends (2) instead of throwing.
                NetRuns.Said($"net: {taken.Message}");
                return new NetPlay { Me = me, Settings = new GameSettings(), Failed = taken.Message };
            }
            var server = new GameServer(settings, hostPeer, me, Vitomir.Engine.Ai.Chance.Game?.NetDice.Next() ?? 1) { Find = strings.Find, Identity = NetIdentity.Of(files) };   // the dice: the launch's; the build and data a join must match
            Console.WriteLine($"net: this host: {server.Identity}");
            server.Log += Console.WriteLine;
            server.Start();
            if (!args.Contains("--no-upnp")) _ = Connectors.Current.Offer(server.Peer.Port, Console.WriteLine);   // the router asked (the connector seam)
            Console.WriteLine($"net: hosting \"{settings.Name}\" on UDP {server.Peer.Port} — {settings.Chronicle} at {settings.Level}");
            return new NetPlay { Me = me, Settings = settings, Server = server };
        }
        Vitomir.Platform.Presentation.StreamerMode.Hide(Arg("--join"));   // the host's name as given is hidden on a stream, as an address is
        var address = Connectors.Current.Find(Arg("--join") ?? "");
        if (address is null) return new NetPlay { Me = me, Settings = new GameSettings(), Failed = "--join wants host[:port]" };
        var client = new GameClient(new NetPeer(0), address, me, Arg("--password") ?? "") { Find = strings.Find, Identity = NetIdentity.Of(files) };
        Console.WriteLine($"net: this machine: {client.Identity}");
        client.Log += Console.WriteLine;
        Console.WriteLine($"net: {me.Name} ({me.Template}) joining {address}");
        return Join(client, me, strings);
    }

    /// <summary>A save named on the command line: a path as given, or a bare name in the save folder (as `Program.ResolveSave`).</summary>
    static string ResolveSave(string name) =>
        File.Exists(name) || Directory.Exists(name) || name.IndexOfAny(['/', '\\']) >= 0 ? name
        : Path.Combine(InputTape.SaveFolder(),                       // the player's, or a replay's own (TapeSaves)
                       name.EndsWith(Engine.Save.SaveArchive.Extension, StringComparison.OrdinalIgnoreCase) ? name : name + Engine.Save.SaveArchive.Extension);

    /// <summary>
    /// Waits for the host to let the player in and start the game — the network load screen's wait —
    /// pumping the wire; a game still forming is waited on.
    /// </summary>
    public static NetPlay Join(GameClient client, NetCharacter me, Strings strings)
    {
        // The host's switches that change the game are in force here while this machine is in its game
        // (Engine.Net.HostSwitches); a welcome the lobby already took is laid over at once.
        client.SwitchesArrived += HostSwitches.Install;
        if (client.FromHost.Count > 0) HostSwitches.Install(client.FromHost);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool said = false;
        while (client.State is ClientState.Connecting or ClientState.Room)
        {
            client.Poll(0.01f);
            if (client.State == ClientState.Room && !said) { Console.WriteLine("net: in the game's room; waiting for the host to start"); said = true; }
            if (client.State == ClientState.Connecting && clock.ElapsedMilliseconds > 15_000) break;
            Thread.Sleep(10);
        }
        var play = new NetPlay { Me = me, Settings = client.Settings ?? new GameSettings(), Client = client };
        if (client.State == ClientState.Gone || client.Settings is null)
            play.Failed = strings.Find(client.Reason ?? "UI_NET_JOINFAILED") ?? client.Reason ?? "the join failed";
        return play;
    }

    /// <summary>
    /// The world is built: the session tied to it, its lines routed to the message box and the chat
    /// box, and the multiplayer bar's chat controls handed to the pass.
    /// </summary>
    public void Attach(Location location, Session game, WorldThing? me, Strings strings, StatusBarPass hud, NetPass netPass,
                       FrontEnd gameMenus, Scripting.CodexScenes? scenes, UiCanvas ui, GameFiles files, Chronicle? chronicle,
                       Func<WorldThing?> hovered)
    {
        storyParts = (strings, ui, game, files, chronicle, scenes, hud, hovered);
        place = location;
        session = game;
        find = strings.Find;
        pass = netPass;
        menus = gameMenus;
        Chat.Text = strings;
        netPass.Enabled = true;
        if (hud.Bar["sbChatbox"] is { } box)
            netPass.Chat = chatBox = new MessageBox(Chat, ui.Assets.Font(box.Element.Get("font") ?? NetPass.ChatFont), box,
                                                    hud.Bar["sbChatScrollBar"], hud.Bar["sbChatScrollSlider"]);
        netPass.Edit = hud.Bar["sbChatEdit"];
        // 1.1 does not pause for its menus in a network game, and has its own escape menu.
        gameMenus.SinglePlayer = false;
        gameMenus.EscapeMenu = "escapeMenuMP";
        gameMenus.OtherClick += EscapeClick;
        gameMenus.OtherKey += EscapeKey;
        if (Server is { } server)
        {
            if (!wired)
            {
                server.Posted += (text, colour) => session?.Messages.Add(text, colour);
                server.Heard += (line, text) => Chat.Add(text, NetChat.Colour(line.Kind));
                server.HostChat += (text, colour) => Chat.Add(text, colour);          // the host's box alone
            }
            server.Enter(location, me);
            if (scenes is not null) Scripting.NetNatives.Register(scenes, server);
            Story = new Storyteller(server, location, strings, ui.Assets, game, files, chronicle, scenes, hud, hovered) { Advance = Walk, Follow = Follow, Camera = () => View?.Camera };
            if (!wired) server.Texts.WalkAsked += () => { if (server.Slots[0]?.Avatar is { } mine) Walk(mine); };
            // The panes' check boxes drawn right after their own pane, so a pane or a dialog over it covers them:
            // until then a pass of their own after every pane.
            netPass.Panes = new MenuPass(ui, Story.Menus) { Pictures = Story.Checkboxes };
            netPass.Extra = null;
            if (StoryShot is not null) Story.Shot(StoryShot.Length > 0 ? StoryShot : null);
        }
        if (Client is { } client)
        {
            // The Storyteller's button is the server's alone (hidden on a client).
            if (hud.Bar["sbSTButton"] is { } st) st.Shown = false;
            if (!wired)
            {
                client.Posted += (text, colour) => session?.Messages.Add(text, colour);
                client.Chatted += (text, colour, _) => Chat.Add(text, colour);
                // `Advance(player)` on the host opens the advancement screen here.
                client.Texts.AdvanceAsked += body =>
                {
                    Console.WriteLine($"  advance: the host asks, {body.Actor?.SpendableExperience} to spend");
                    advance?.Invoke(body);
                };
            }
            // A player's machine is not awakened, so the effects the host puts on characters are
            // looked up in a table of its own (`ClientSeen`).
            client.Seen.Effects ??= location.Effects ?? new Engine.Rpg.EffectDefinitions(files);
            // The host's pane — the load screen or the pause menu (Engine.Net.NetPane).
            netPass.Extra = () => client.Pane.Quads(n => { try { return ui.Assets.Pane(n); } catch (Exception) { return null; } });
            client.Enter(location, me);
        }
        if (!wired) Chat.Add(find(Server is not null ? "UI_NET_YOUAREHOSTING" : "UI_NET_YOUJOINEDGAME") ?? "", 0xffffff00u);
        wired = true;
        saying = SayOnJoin;                     // said on the first frame, once the run has wired the host's places
    }
    string? saying;

    Engine.Rpg.Conversations? talk;
    ConversationPane? talkPane;
    readonly SubtitleClock hostHeard = new();

    /// <summary>
    /// This machine's conversation, camera and subtitle pane, for what the host sends a player (1.1's
    /// <see cref="ClientScenes"/>): the conversation a script on the host starts for
    /// this player runs here, the cutscene shots and paths it names this player for move this camera,
    /// and the lines of other players' conversations nearby come up as subtitles. On the host, the
    /// lines its own character overhears (the copy to the server).
    /// </summary>
    public void Scenes(Engine.Rpg.Conversations conversations, WorldCamera camera, ConversationPane pane, Func<string, string?> words)
    {
        talk = conversations;
        talkPane = pane;
        if (Client is { } client)
        {
            client.Scenes.Talk = conversations;
            client.Scenes.Camera = camera;
            client.Scenes.Words = words;
            client.Scenes.Names = key => find(key) ?? key;
            client.Scenes.Subtitled += text => { pane.Say(text); Console.WriteLine($"  overheard: {text}"); };
        }
        if (Server is { } server)
            server.Overheard += (spoken, name, id) =>
            {
                string text = ClientScenes.Overheard(spoken, name, id, key => find(key) ?? key, words);
                hostHeard.Add(text, place?.Milliseconds ?? 0);
                pane.Say(text);
                Console.WriteLine($"  overheard: {text}");
            };
    }

    /// <summary>
    /// This machine's read pane, quest log and discipline table, for what a script on the host shows
    /// this player (<see cref="ClientTexts"/>): a text the host opens for them opens here, and its close
    /// goes back for the script's `textended`; the host's quests are kept in this log.
    /// </summary>
    public void Texts(Func<string, bool> open, Engine.Ui.ReadPane? pane, Engine.Rpg.QuestLog quests, Engine.Rpg.DisciplineDefinitions powers)
    {
        if (Client is not { } client) return;
        client.Texts.Open = name =>
        {
            bool opened = open(name);
            Console.WriteLine($"  text: the host opens {name}{(opened ? "" : " — not here, or the pane is up")}");
            return opened;
        };
        client.Texts.Quests = quests;
        client.Powers = powers;
        if (pane is not null) pane.Closed = client.Texts.Closed;
    }

    /// <summary>
    /// An advancement screen on this machine has closed on <paramref name="body"/>: a
    /// player's machine sends the choices to the host; and this machine's own
    /// character is written back.
    /// </summary>
    public void Advanced(WorldThing body)
    {
        if (Client is { } client)
        {
            if (!ReferenceEquals(body, client.Avatar)) return;
            client.Texts.Advanced(body);
        }
        else if (Server is { } server && !ReferenceEquals(body, server.Slots[0]?.Avatar))
        {
            // The host's walk moving on from a player's character: its numbers go to its machine and
            // its own screen opens there.
            server.Texts.Advance(body.Guid);
            return;
        }
        Keep("advanced");
    }

    /// <summary>The characters' folder — the front end's (`MpMenus`, `Crypt/` beside the saves).</summary>
    public static string CryptFolder => Path.Combine(Engine.Diagnostics.Diag.UserDataFolder, "Crypt");

    /// <summary>
    /// This machine's character written back to its file as it now stands — stats, experience,
    /// disciplines and pack (as 1.1 does, after every advancement and when the player leaves).
    /// **Not built** of the leave's first steps: the revive, clearing the effects
    /// and putting the hand's item away.
    /// </summary>
    public void Keep(string why)
    {
        var body = Client?.Avatar ?? Server?.Slots[0]?.Avatar;
        if (body?.Actor is null || Me.Name.Length == 0 || (Server is not null && !Server.HostPlays)) return;
        Me = Me.Keep(body);
        try
        {
            new Crypt(InputTape.SavePath(CryptFolder)).Save(Me);          // a replay writes a scratch copy, not the player's
            Console.WriteLine($"net: {Me.Name} written back ({why}) — {Me.Saved!.Spendable} to spend, "
                            + $"{Me.Saved.Disciplines.Count} disciplines, {Me.Saved.Pack.Count} items");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.WriteLine($"net: {Me.Name} not written — {e.Message}"); }
    }

    /// <summary>
    /// One frame of the network: the wire read and written, the world eased. <paramref name="real"/>
    /// makes a headless settle wait for real time, because the other end runs on it.
    /// </summary>
    public void Frame(float seconds, int nowMs, bool real = false)
    {
        if (saying is { Length: > 0 } line) { saying = null; Send(line); }
        if (left) return;
        if (Server is { } host) host.Places.Step(host.Paused ? 0f : seconds);   // the places players went to alone (HostPlaces)
        Server?.Poll(nowMs);
        Client?.Poll(seconds);
        Story?.Frame(nowMs);
        // An overheard subtitle comes down when its time is up, unless a conversation has the pane.
        bool heardDone = (Client?.Scenes.SubtitleDone() ?? false) | hostHeard.Done(place?.Milliseconds ?? 0);
        if (heardDone && talk?.Current is not { Running: true }) talkPane?.Reset();
        if (pass is not null)
        {
            // The Storyteller's bar has no chat box: the chat is the multiplayer bar's.
            pass.Chat = Story?.On == true ? null : chatBox;
            pass.Typing = Typing;
            if (Typing is not null && nowMs >= caretAt) { pass.CaretOn = !pass.CaretOn; caretAt = nowMs + 500; }
        }
        if (real) Thread.Sleep((int)(seconds * 1000));
    }

    /// <summary>
    /// An input event, before the game sees it: while the chat line has the keyboard every key is its;
    /// otherwise a click on the edit line starts typing, and so do 1.1's three keys
    /// (<see cref="ChatKeys"/>): Enter, Shift+; with ":" and / with "/", each with the interface
    /// sound 0, while the key hook reads them (<see cref="Busy"/> is its gates). The Storyteller's panes take their clicks too.
    /// </summary>
    internal bool Event(in LoopInput e, IClassicHost host)
    {
        int w = 1, h = 1;
        if (host.HasWindow) host.PictureSize(&w, &h);
        if (Typing is not null)
        {
            if (e.Kind == LoopInputKind.KeyDown) openedByKey = null;
            switch (e.Kind)
            {
                case LoopInputKind.KeyDown:
                    // Ctrl+V and Ctrl+C, ours and always on (Engine.Ui.TypedLine).
                    if (ClipboardKeys.Is(e, LoopKey.V)) Typing = TypedLine.Paste(Typing, ClipboardKeys.Clip(), NetChat.MaxLength);
                    else if (ClipboardKeys.Is(e, LoopKey.C)) ClipboardKeys.Copy(TypedLine.Copied(Typing, TypedLine.Newest(session?.Messages)));
                    else if (e.Key is LoopKey.Return or LoopKey.KeypadEnter) { Send(Typing); StopTyping(host); }
                    else if (e.Key == LoopKey.Escape) StopTyping(host);          // the chat line shut
                    else if (e.Key == LoopKey.Backspace && Typing.Length > 0) Typing = Typing[..^1];
                    return true;
                case LoopInputKind.KeyUp:
                    return true;
                case LoopInputKind.Text when openedByKey is { } key
                                                           && e.Text is ";" or ":" or "/" && key.Length > 0:
                    openedByKey = null;                      // ours: the opening key's own character is not typed twice
                    return true;
                case LoopInputKind.Text:
                    foreach (char c in e.Text)
                        if (!char.IsControl(c) && Typing.Length < NetChat.MaxLength) Typing += c;
                    return true;
            }
        }
        // The game's menu first when it is up: the escape menu is a dialog (StoryClick.Route).
        if (Story?.Event(e, w, h, menuUp: menus is { Up: true }) == true) return true;
        if (menus is { Up: true }) return false;
        if (e.Kind == LoopInputKind.KeyDown && !e.Repeat && Busy?.Invoke() != true
            && ChatKeys.Opens(ChatKeyOf(e)) is { } opening)
        {
            Engine.Audio.Noise.Ui(place, ChatKeys.Sound);                            // interface sound 0
            StartTyping(host, opening);
            openedByKey = opening;
            return true;
        }
        // The host's \ and. (Engine.Net.SeatKeys): the sound, then the seat's.
        if (e.Kind == LoopInputKind.KeyDown && !e.Repeat && Story is { } seat
            && SeatKeys.Read(Server is not null, Busy?.Invoke() != true) && SeatKeyOf(e) is var sk and not SeatKey.None)
        {
            Engine.Audio.Noise.Ui(place, SeatKeys.Sound);                            // interface sound 0
            seat.Key(sk);
        }
        if (e.Kind == LoopInputKind.ButtonDown && e.Code == LoopButton.Left && pass?.Edit is { } edit)
        {
            var (x, y) = StatusBar.FromWindow(e.X, e.Y, w, h);
            if (edit.Contains(x, y)) { StartTyping(host); return true; }
        }
        return false;
    }

    /// <summary>Which of 1.1's chat keys a press is (DirectInput's scan codes, so SDL's physical keys).</summary>
    static ChatKey ChatKeyOf(in LoopInput e) =>
        ChatKeys.Of(enter: e.Code == LoopScancode.Return,
                    semicolon: e.Code == LoopScancode.Semicolon,
                    slash: e.Code == LoopScancode.Slash,
                    shift: (e.Mods & LoopMods.Shift) != 0);

    /// <summary>Which of the host's keys a press is (`DIK_BACKSLASH`, `DIK_PERIOD` — SDL's physical keys).</summary>
    static SeatKey SeatKeyOf(in LoopInput e) =>
        SeatKeys.Of(backslash: e.Code == LoopScancode.Backslash, period: e.Code == LoopScancode.Period);

    /// <summary>What a key opened the line with, until the next key: its own character, should it still come as text, is dropped (ours).</summary>
    string? openedByKey;

    void StartTyping(IClassicHost host, string opening = "")
    {
        Typing = opening;
        if (host.HasWindow) host.TextInput(true);
    }

    void StopTyping(IClassicHost host)
    {
        Typing = null;
        if (host.HasWindow) host.TextInput(false);
    }

    /// <summary>A line typed: the host says it itself; a player sends it. A `*` line is a console command, run here — not built.</summary>
    public void Send(string line)
    {
        if (line.Length == 0) return;
        if (line[0] == '*') { session?.Messages.Add($"console: {line[1..]} (the console is not built)"); return; }
        if (Story?.Command(line) is { } answer) { Console.WriteLine(answer); session?.Messages.Add(answer.Trim()); return; }
        // The tester's log bundle (item 6, ours): /bundle on the chat line, a host's or a player's; the line says where it is.
        if (line.Trim().Equals("/bundle", StringComparison.OrdinalIgnoreCase))
        {
            string made = Engine.Net.LogBundle.MakeHere(Server?.Identity ?? Client?.Identity);
            Console.WriteLine(made);
            session?.Messages.Add(made);
            return;
        }
        if (Server is { } host && NetSeenConsole.Command(host, line, text => { Console.WriteLine(text); session?.Messages.Add(text.Trim()); })) return;
        if (Server is { } s) s.Say(line);
        else Client?.Say(line);
    }

    // ---- the multiplayer escape menu (`escapeMenuMP`) --------------------------------------------

    /// <summary>
    /// Its five buttons (`NetEscape`): Resume, Options (the Options screen, not paused), Quit (leaves the game), Save — greyed on a
    /// player's machine, the host's to use — and Revive, for the dead (1.1's message 0x49).
    /// </summary>
    bool EscapeClick(MenuPane pane, Formats.NuiElement button) =>
        menus is not null && NetEscape.Click(menus, pane, button, IsHost, Revive, () => { Leave(); menus.AskMainMenu(withCredits: false); });

    bool EscapeKey(MenuPane pane, MenuKey key)
    {
        if (!pane.File.Equals("escapeMenuMP", StringComparison.OrdinalIgnoreCase) || menus is null) return false;
        if (key == MenuKey.Escape) menus.PutEscapeAway();
        return true;
    }

    /// <summary>The greys the escape menu shows: Save on a player's machine, Revive unless dead.</summary>
    public void EscapeShown(WorldThing? me)
    {
        if (menus is null) return;
        var pane = menus.Pane("escapeMenuMP");
        pane.Grey("emmpSaveButton", !IsHost);
        pane.Grey("emmpReviveButton", me?.Actor?.Dead != true);
    }

    void Revive()
    {
        if (Client is { } c) c.Revive();
        else if (Server?.Slots[0]?.Avatar is { Actor.Dead: true } body) Engine.Rpg.Damage.Revive(body, 1f, 1f, sound: true);
    }

    /// <summary>
    /// Leave Game: "You have left the game." and the connection closed — the character first made
    /// whole and at rest and then written back (<see cref="NetArms.TidyForLeaving"/>).
    /// </summary>
    public void Leave()
    {
        if (left) return;
        if ((Client?.Avatar ?? (Server is { HostPlays: true } s ? s.Slots[0]?.Avatar : null)) is { } body) NetArms.TidyForLeaving(body);
        Keep("left the game");
        left = true;
        Client?.Leave();
        Server?.Dispose();
    }
    bool left;

    public void Dispose()
    {
        if (place is not null) NetOrders.Detach(place);
        // A session handed to the next run (a journey on a player's machine) is not closed.
        if (ReferenceEquals(Carried, this)) return;
        Active = false;
        if (Client is not null) HostSwitches.Leave();          // the player's own switches are theirs again
        if (left) return;
        Keep("the game is over");
        left = true;
        Client?.Dispose();
        Server?.Dispose();
    }
}
