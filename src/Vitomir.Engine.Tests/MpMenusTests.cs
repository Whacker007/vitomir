// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Multiplayer's screens and the Storyteller's powers, driven the way the
/// mouse drives them: the main menu's Multiplayer opens `mpSelect`; the character screens offer
/// `mpcharacters.txt`'s nineteen models and the clans a player may choose, and keep what is made; the
/// create screen lists the nine multiplayer chronicles and makes a host; and the Storyteller possesses,
/// deletes, turns people friendly and hostile, makes things and reads the chronicle's lists — each
/// against 1.1's rules and in its words.
/// </summary>
public class MpMenusTests
{
    sealed record Rig(FrontEnd Menus, MpMenus Mp, string Crypt) : IDisposable
    {
        public void Dispose()
        {
            Mp.Server?.Dispose();
            Mp.Client?.Dispose();
            if (Directory.Exists(Crypt)) Directory.Delete(Crypt, recursive: true);
        }
    }

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        var mp = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false };
        return new Rig(menus, mp, crypt);
    }

    static void Click(FrontEnd menus, string file, string control)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(control)!;
        menus.PointerAt(pane.X + e.Pos[0] + e.Size[0] / 2f, pane.Y + e.Pos[1] + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    [Fact]
    public void The_main_menus_multiplayer_opens_the_network_selection_screen()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Menus.OpenMainMenu();
        Click(rig.Menus, FrontEnd.MainMenuFile, rig.Menus.Pane(FrontEnd.MainMenuFile).Nui.Elements.First(e => e.Get("buttonvalue") == "2").Name);
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(MpMenus.SelectFile)));
        // Main Menu (buttonvalue 3) goes back.
        Click(rig.Menus, MpMenus.SelectFile, "mpSelectMainMenu");
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MainMenuFile)));
    }

    /// <summary>
    /// **Join Game** (ours; 1.1's LAN) opens the join screen — 1.1's LAN room, mode 1 — with no character asked for: Back,
    /// Join Game and By Address… shown, Create Game, Player Setup, Leave Game and Start hidden.
    /// </summary>
    [Fact]
    public void Join_Game_opens_the_join_screen_and_asks_for_no_character()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        rig.Mp.Open();
        Click(rig.Menus, MpMenus.SelectFile, "mpSelectInternet");
        Assert.Equal(1, rig.Mp.Mode);
        Assert.False(rig.Menus.Pane(MpMenus.PlayerSelectFile).Shown);
        var chat = rig.Menus.Pane(MpMenus.ChatFile);
        Assert.True(rig.Menus.IsUp(chat));
        Assert.Equal("LAN Chat", chat.Texts["mpChatTitle"]);
        foreach (var shown in new[] { "mpChatJoinGameButton", "mpChatLogOutButton", "mpChatChangeRoomButton" }) Assert.DoesNotContain(shown, chat.Hidden);
        foreach (var hidden in new[] { "mpChatCreateGameButton", "mpChatPlayerSetupButton", "mpChatStartButton", "mpChatLeaveGameButton" }) Assert.Contains(hidden, chat.Hidden);
        // Back (Log Out in 1.1) is the Host/Join screen again.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatLogOutButton");
        Assert.False(chat.Shown);
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(MpMenus.SelectFile)));
    }

    [Fact]
    public void A_character_is_made_from_the_nineteen_models_and_kept_in_the_crypt()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        Assert.Equal(19, rig.Mp.Models().Count);                    // misc/mpcharacters.txt
        // The clans a player may choose leave out the ones flagged "not selectable in MP".
        Assert.All(rig.Mp.ChoosableClans(), c => Assert.Equal(0u, c.Flags & 1));
        rig.Mp.OpenPlayerSelect();                                   // nobody kept: the create screen at once
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        Assert.True(rig.Menus.IsUp(create));
        Assert.True(rig.Mp.Checked(MpMenus.PlayerCreateFile, "mppcKindredCheck"));    // Kindred to start
        Assert.Equal("Kindred", create.Texts["mppcType"]);

        // No name: refused with UI_MPPC_NONAME.
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MessageBoxFile)));
        Assert.Equal("Please enter a player name.", rig.Menus.Pane(FrontEnd.MessageBoxFile).Texts["mbTextBox"]);
        rig.Menus.Key(MenuKey.Enter);

        // A human has no clan and no generation.
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcHumanCheck");
        Assert.Equal("N/A", create.Texts["mppcClan"]);
        create.Edits["mppcName"].Set("Anna");
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.Equal("Anna", rig.Mp.Me!.Name);
        Assert.Equal(ActorType.Human, rig.Mp.Me.Type);
        Assert.Equal(200, rig.Mp.Me.Cash);                           // Create's purse
        Assert.True(new Crypt(rig.Crypt).Has("Anna"));
    }

    /// <summary>
    /// Create opens the advancement screen on the new character: the character
    /// is written only when it closes, with what was spent, and its sheet keeps the
    /// template's kit for the host to dress it in.
    /// </summary>
    [Fact]
    public void Create_opens_the_advancement_and_the_character_is_written_as_it_leaves_it()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var powers = new DisciplineDefinitions(files);
        (NetCharacter Made, Action<NetCharacter> Then)? asked = null;
        rig.Mp.AdvanceNew = (made, then) => asked = (made, then);
        rig.Mp.OpenPlayerSelect();                                   // nobody kept: the create screen at once
        rig.Menus.Pane(MpMenus.PlayerCreateFile).Edits["mppcName"].Set("Bela");
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.NotNull(asked);
        Assert.False(new Crypt(rig.Crypt).Has("Bela"));                // not before the screen closes

        var body = rig.Mp.NewBody(asked.Value.Made, powers)!;
        var me = body.Actor!;
        Assert.Equal("Bela", me.Name);
        Assert.Equal(200, me.Cash);
        // Create's set-up: a Kindred knows feed and bloodhealing, and her clan's first
        // disciplines wait to be learnt.
        Assert.Equal(0, me.Disciplines["feed"]!.Level);
        Assert.Equal(0, me.Disciplines["bloodhealing"]!.Level);
        Assert.Contains(me.Disciplines.All, d => d.Level == -1);
        long had = me.SpendableExperience;
        var advance = new Advancement(body, [body], powers);
        float strength = me.BaseStat[0];
        Assert.True(advance.BuyStat(0));
        Assert.Equal(strength + 1, me.BaseStat[0]);

        asked.Value.Then(asked.Value.Made with { Saved = NetSheet.From(body) with { Kit = true, Effects = null } });
        var kept = new Crypt(rig.Crypt).All().Single(c => c.Name == "Bela");
        Assert.True(kept.Saved!.Kit);
        Assert.Equal(strength + 1, kept.Saved.BaseStats[0]);
        Assert.True(kept.Saved.Spendable < had);
        Assert.Equal("Bela", rig.Mp.Me!.Name);
    }

    [Fact]
    public void The_create_screen_lists_the_nine_multiplayer_chronicles_and_makes_a_host()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Mp.Shot("mpcreategame");
        var pane = rig.Menus.Pane(MpMenus.CreateGameFile);
        var list = pane.Lists["mpcgChronicleList"];
        Assert.Equal(9, list.Rows.Count);
        Assert.Contains(list.Rows, r => r.Text == "To Curse the Darkness");
        Assert.Equal("Anna's game", pane.Edits["mpcgNameEdit"].Text);            // UI_NET_DEFAULTGAME
        Assert.True(rig.Mp.Checked(MpMenus.CreateGameFile, "mpcgAutoStoryteller"));

        // Two passwords that differ are refused (UI_NET_PWMISMATCH).
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgPWProtectCheck");
        pane.Edits["mpcgPW"].Set("one");
        pane.Edits["mpcgPWConfirm"].Set("two");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.Equal("Passwords do not match.", rig.Menus.Pane(FrontEnd.MessageBoxFile).Texts["mbTextBox"]);
        rig.Menus.Key(MenuKey.Enter);
        Assert.Null(rig.Mp.Server);

        // Unticking the last Storyteller box ticks it back; moderation on; created.
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgAutoStoryteller");
        Assert.True(rig.Mp.Checked(MpMenus.CreateGameFile, "mpcgAutoStoryteller"));
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgSTmoderation");
        pane.Edits["mpcgPWConfirm"].Set("one");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.NotNull(rig.Mp.Server);
        Assert.Equal(4, rig.Mp.Mode);                                          // the host's game room
        // Ours: 1.1's hosting line, which it never shows (it writes to a null output), in the game's chat.
        Assert.Equal(new Strings(files)["UI_NET_YOUAREHOSTING"] + " 'Anna's game'.", rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatGame"].Rows[0].Text);
        var flags = rig.Mp.Server!.Flags;
        Assert.True(flags.HasFlag(GameFlags.Moderated) && flags.HasFlag(GameFlags.AutoStoryteller) && flags.HasFlag(GameFlags.Password));
        Assert.Equal("MP_CurseDarkness", rig.Mp.Server.Settings.Chronicle);
        // And Start hands the game over.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatStartButton");
        Assert.NotNull(rig.Mp.Handoff?.Server);
    }

    /// <summary>
    /// **The most players** (ours, <see cref="MostPlayersOption"/>): the create screen's field shows 1.1's
    /// eight, takes digits only, refuses anything but 2 to 8 in Create Game's own box; chosen 3 (clicked and typed as the other
    /// fields are), the game made allows three — the host and two players — and a fourth is refused with 1.1's
    /// <c>UI_NET_JOINFULL</c>; the choice is kept in the player's <c>vitomir.ini</c> (<c>[multiplayer] mostplayers</c>) and shown by the
    /// screen the next time it opens.
    /// </summary>
    [Fact]
    public void The_create_screens_most_players_is_eight_keeps_its_choice_and_a_join_past_it_is_refused()
    {
        using var files = Install.Open();
        string ini = Path.Combine(Path.GetTempPath(), $"vitomir-most-players-{Guid.NewGuid():N}.ini");
        var joined = new List<GameClient>();
        try
        {
            using (var rig = Open(files))
            {
                rig.Mp.SettingsPath = ini;
                rig.Mp.Shot("mpcreategame");
                var pane = rig.Menus.Pane(MpMenus.CreateGameFile);
                var field = pane.Edits[MostPlayersOption.Edit];
                Assert.Equal("8", field.Text);                                          // 1.1's eight

                // A choice past eight is refused in Create Game's box, as a missing name is.
                void Choose(char c)
                {
                    Click(rig.Menus, MpMenus.CreateGameFile, MostPlayersOption.Edit);
                    Assert.Equal(MostPlayersOption.Edit, pane.Focus, ignoreCase: true);
                    rig.Menus.Type('\b');
                    rig.Menus.Type('x');                                                // digits only (1.1's isdigit field)
                    rig.Menus.Type(c);
                }
                Choose('9');
                Assert.Equal("9", field.Text);
                Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
                Assert.Equal(MostPlayersOption.Refusal, rig.Menus.Pane(FrontEnd.MessageBoxFile).Texts["mbTextBox"]);
                rig.Menus.Key(MenuKey.Enter);
                Assert.Null(rig.Mp.Server);

                Choose('3');
                Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
                var server = rig.Mp.Server;
                Assert.NotNull(server);
                Assert.Equal(3, server!.Settings.MaxPlayers);
                Assert.Equal(3, MostPlayersOption.Read(ini));
                Assert.Contains("mostplayers = 3", File.ReadAllText(ini), StringComparison.Ordinal);

                // Two players join the host's room; the fourth is refused, the room being full.
                var strings = new Strings(files);
                GameClient Knock(string name) => new(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                                     new NetCharacter { Name = name, Template = "MP_Erik" }) { Find = strings.Find };
                int ms = 0;
                bool Run(GameClient client, Func<bool> until)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 8000 && !until())
                    {
                        ms += 33;
                        server.Poll(ms);
                        foreach (var c in joined) c.Poll(0.033f);
                        client.Poll(0.033f);
                        Thread.Sleep(2);
                    }
                    return until();
                }
                foreach (var name in new[] { "Bob", "Cleo" })
                {
                    var client = Knock(name);
                    Assert.True(Run(client, () => client.State != ClientState.Connecting), name + " had no answer");
                    Assert.Equal(ClientState.Room, client.State);
                    joined.Add(client);
                }
                var fourth = Knock("Dora");
                joined.Add(fourth);
                Assert.True(Run(fourth, () => fourth.State == ClientState.Gone));
                Assert.Equal("UI_NET_JOINFULL", fourth.Reason);
                Assert.False(string.IsNullOrEmpty(strings.Find("UI_NET_JOINFULL")));   // 1.1's own words for it
            }
            // Opened again (another run), the screen shows the choice kept.
            using var again = Open(files);
            again.Mp.SettingsPath = ini;
            again.Mp.Shot("mpcreategame");
            Assert.Equal("3", again.Menus.Pane(MpMenus.CreateGameFile).Edits[MostPlayersOption.Edit].Text);
        }
        finally
        {
            foreach (var c in joined) c.Dispose();
            File.Delete(ini);
        }
    }

    /// <summary>
    /// Create Game tests the name's length alone: an empty one is refused with
    /// UI_NET_PLEASEENTERNAME, but a name of spaces is a name, and the game keeps it as typed
    /// (a comparison with 1.1; Vitomir trimmed it and refused it until then).
    /// </summary>
    [Fact]
    public void A_game_name_of_spaces_is_taken_as_it_is_and_only_an_empty_one_is_refused()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Mp.Shot("mpcreategame");
        var pane = rig.Menus.Pane(MpMenus.CreateGameFile);
        pane.Edits["mpcgNameEdit"].Set("");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.Null(rig.Mp.Server);
        rig.Menus.Key(MenuKey.Enter);
        pane.Edits["mpcgNameEdit"].Set("   ");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.NotNull(rig.Mp.Server);
        Assert.Equal("   ", rig.Mp.Server!.Settings.Name);
    }

    /// <summary>
    /// The character select's rows: <c>"%d. %s (%s)"</c> — the number from 1, the name,
    /// the clan's name through the string table (a human's is the `human` clan) — and the
    /// row chosen is the character in use's, its name compared without regard to case; when no row
    /// has it, none is chosen; and Enter on the screen is OK (a comparison with 1.1).
    /// </summary>
    [Fact]
    public void The_select_list_numbers_each_character_with_its_clan_and_chooses_the_one_in_use_by_name()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var crypt = new Crypt(rig.Crypt);
        crypt.Save(new NetCharacter { Name = "Anna", Template = "MP_Serena", Type = ActorType.Vampire, Clan = "brujah" });
        crypt.Save(new NetCharacter { Name = "Bob", Template = "MP_Serena", Type = ActorType.Human });
        rig.Mp.OpenPlayerSelect();
        var list = rig.Menus.Pane(MpMenus.PlayerSelectFile).Lists["mppsPlayerList"];
        Assert.Equal(["1. Anna (Brujah)", "2. Bob (Human)"], list.Rows.Select(r => r.Text));
        Assert.Equal(0, list.Current);                                   // nobody in use: the first

        list.Select(1);
        rig.Menus.Drawn();                                               // a key hook waits a frame
        rig.Menus.Key(MenuKey.Enter);                                    // Enter is OK
        Assert.Equal("Bob", rig.Mp.Me?.Name);
        // Bob's file taken away first, then "bob" saved: on Windows the two names are one file, so the other
        // order deleted the new one too (found on the laptop).
        crypt.Delete("Bob");
        crypt.Save(new NetCharacter { Name = "bob", Template = "MP_Serena", Type = ActorType.Human });
        rig.Mp.OpenPlayerSelect();
        Assert.Equal(1, list.Current);                                   // "bob" is Bob's row (__strcmpi)

        crypt.Delete("bob");
        rig.Mp.OpenPlayerSelect();
        Assert.Equal(-1, list.Current);                                  // no row has the name: none chosen
    }

    [Fact]
    public void The_LAN_room_finds_a_game_on_this_machine_and_joins_it()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        using var host = new GameServer(GameSettings.For(inn, "Midnight at the Inn", GameFlags.AutoStoryteller),
                                        new NetPeer(0), new NetCharacter { Name = "Server" });
        rig.Mp.Port = host.Peer.Port;                  // the LAN query goes to the host's port (1.1's 4994)
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        rig.Mp.Open();
        Click(rig.Menus, MpMenus.SelectFile, "mpSelectInternet");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var games = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatGame"];
        while (sw.ElapsedMilliseconds < 4000 && games.Rows.Count == 0) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        // The row is the game's name and how many are in it (`"%s (%d)"`).
        Assert.Equal("Midnight at the Inn (1)", games.Rows.Single().Text);
        // Nothing is chosen until the row is clicked (`NetInterfaceLeftoverTests`).
        Assert.Equal(-1, games.Current);
        var chat = rig.Menus.Pane(MpMenus.ChatFile);
        var list = chat.Element("mpChatGame")!;
        for (float y = chat.Y + list.Pos[1]; y < chat.Y + list.Pos[1] + list.Size[1] && games.Current < 0; y += 1f)
        {
            rig.Menus.PointerAt(chat.X + list.Pos[0] + list.Size[0] / 2f, y);
            rig.Menus.Frame(rig.Menus.NowMs);
            if (games.Hovered == 0) { rig.Menus.LeftHeld = true; rig.Menus.Press(); rig.Menus.LeftHeld = false; rig.Menus.Release(); }
        }
        Assert.Equal(0, games.Current);

        Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        while (sw.ElapsedMilliseconds < 8000 && rig.Mp.Mode != 2) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(2, rig.Mp.Mode);                                          // the game's room, as a player
        Assert.DoesNotContain(host.Players, p => p.Name == "Anna");            // still choosing
        // Player Setup in the room: the one kept, chosen with OK, brought to the host.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatPlayerSetupButton");
        Assert.Equal("Anna", rig.Menus.Pane(MpMenus.PlayerSelectFile).Texts["mppsName"]);
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        while (sw.ElapsedMilliseconds < 8000 && rig.Mp.Client is { Accepted: false }) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Contains(host.Players, p => p.Name == "Anna");
        // The host starts: nothing comes to the player until the host's place is up, and Start stays greyed.
        var start = rig.Menus.Pane(MpMenus.ChatFile);
        Assert.Equal(ButtonState.Greyed, start.Buttons["mpChatStartButton"]);
        host.Start();
        for (int i = 0; i < 40; i++) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Null(rig.Mp.Handoff);
        Assert.Equal(ButtonState.Greyed, start.Buttons["mpChatStartButton"]);
        // The host's place loaded: the game is under way, Start lit, and pressed it lets her in.
        host.Enter(Location.Load(files, "P1_INN4", sky: false), null);
        while (sw.ElapsedMilliseconds < 12000 && start.Buttons["mpChatStartButton"] == ButtonState.Greyed) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(ButtonState.Normal, start.Buttons["mpChatStartButton"]);
        Assert.Null(rig.Mp.Handoff);
        Click(rig.Menus, MpMenus.ChatFile, "mpChatStartButton");
        while (sw.ElapsedMilliseconds < 12000 && rig.Mp.Handoff is null) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.NotNull(rig.Mp.Handoff?.Client);
        Assert.Equal("P1_INN4", rig.Mp.Handoff!.Settings.Level);
    }

    /// <summary>
    /// **The LAN room never takes the finder's port** (from a Windows run, where the test
    /// above found no game). The room binds the game's port plus one with SO_REUSEADDR so two games on
    /// one machine share it; on Windows such a socket takes over a port another already holds, and
    /// Windows gives free ports out in order, so the room could land on the finder's own port and
    /// swallow the host's answer. Here the room is put on the finder's port on purpose: the finder
    /// must still hear the game (`NetPeer`'s exclusive bind on Windows; elsewhere the system refuses
    /// the room's bind by itself).
    /// </summary>
    [Fact]
    public void The_LAN_room_never_takes_the_finders_port()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        using var host = new GameServer(GameSettings.For(inn, "Midnight at the Inn", GameFlags.AutoStoryteller),
                                        new NetPeer(0), new NetCharacter { Name = "Server" });
        var peer = new NetPeer(0);
        using var finder = new LanFinder(host.Peer.Port, peer);
        using var room = new LanRoom(peer.Port - 1);                            // the room's port is the finder's
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 4000 && finder.Games.Count == 0) { host.Poll((int)sw.ElapsedMilliseconds); finder.Poll(); Thread.Sleep(5); }
        Assert.Equal("Midnight at the Inn (1)", Assert.Single(finder.Games).Info.Row);
    }

    // ---- the Storyteller ----------------------------------------------------------------------------

    static (GameServer Server, Location Place, StoryPowers Powers) Seat(GameFiles files, string? player = "MP_Serena")
    {
        var inn = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(inn, "Test", GameFlags.Moderated);
        var place = Location.Load(files, settings.Level, scene: settings.Scene, player: player, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new Rpg.EffectDefinitions(files), new Rpg.DisciplineDefinitions(files));
        var server = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = "Anna", Template = player ?? "" });
        server.Enter(place, place.Player?.Thing);
        return (server, place, new StoryPowers(server, place, new Strings(files)));
    }

    [Fact]
    public void The_Storyteller_possesses_a_character_and_gives_the_body_back()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        var knight = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null);
        string said = st.Possess(knight);
        Assert.StartsWith("Possessing", said);                                  // UI_STSBPOSSESS
        Assert.Same(knight, place.Steered);
        Assert.Same(knight, server.Possessed);
        // Again: back to the host's own character.
        st.Possess(knight);
        Assert.Null(server.Possessed);
        Assert.Same(place.Player!.Thing, place.Steered);
    }

    /// <summary>
    /// **Possess refuses 1.1's three minds**: a pet, a body a possession holds
    /// already and a mind itself possessing (mode 13) — and takes a frenzied, afraid or
    /// mesmerised one, which an earlier build refused instead.
    /// </summary>
    [Fact]
    public void Possess_refuses_a_pet_a_possessed_body_and_a_possessor_and_takes_a_frenzied_or_afraid_one()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        var one = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null);
        var mind = one.Mind!;
        uint flags = mind.Flags;
        var home = mind.Mode;

        mind.Flags = flags | Ai.Mind.FlagPet;
        Assert.Equal("", st.Possess(one));
        mind.Flags = flags | Ai.Mind.FlagPossessed;
        Assert.Equal("", st.Possess(one));
        mind.Flags = flags;
        mind.Enter(Ai.AiMode.Possessing, andBase: false);
        Assert.Equal("", st.Possess(one));
        Assert.Null(server.Possessed);
        Assert.Same(place.Player!.Thing, place.Steered);

        foreach (var mode in new[] { Ai.AiMode.Frenzied, Ai.AiMode.Afraid, Ai.AiMode.Mesmerized })
        {
            mind.Enter(mode, andBase: false);
            Assert.StartsWith("Possessing", st.Possess(one));
            Assert.Same(one, server.Possessed);
            st.Possess(one);                                                     // and let go
            Assert.Null(server.Possessed);
        }
        mind.Enter(home, andBase: false);
    }

    /// <summary>
    /// **A second Possess steers the host's own character, not the ghost** (the button's other branch:
    /// `*`, the local player): after Set Ghost, a possession asked again goes back to
    /// the host's character.
    /// </summary>
    [Fact]
    public void Possess_asked_again_steers_the_host_s_own_character_even_after_Set_Ghost()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        var knight = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null);
        st.SetGhost(knight);
        Assert.Same(st.Ghost, place.Steered);
        st.Possess(knight);
        Assert.Same(knight, place.Steered);
        st.Possess(knight);
        Assert.Null(server.Possessed);
        Assert.Same(place.Player!.Thing, place.Steered);
    }

    [Fact]
    public void The_Storyteller_cannot_delete_a_player_or_nothing()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        Assert.Equal("No selection to delete.", st.Delete(null));
        Assert.Equal("Can't delete an active player.", st.Delete(place.Player!.Thing));
        var someone = place.Things.First(t => t.Type == ThingType.Actor);
        Assert.Equal("", st.Delete(someone));
        Assert.False(someone.InWorld);
    }

    [Fact]
    public void Talk_neutral_enemy_goes_round_the_three_on_the_actor_flags()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        var a = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null);
        var seen = new List<StoryPowers.Disposition>();
        for (int i = 0; i < 3; i++)
        {
            st.CycleDisposition(a);
            seen.Add(StoryPowers.DispositionOf(a.Actor!));
        }
        Assert.Equal(3, seen.Distinct().Count());
        // Enemy is neither flag; talk is 0x80; neutral 0x40000.
        st.CycleDisposition(a);
        while (StoryPowers.DispositionOf(a.Actor!) != StoryPowers.Disposition.Enemy) st.CycleDisposition(a);
        Assert.Equal(0u, a.Actor!.Flags & (Actor.Flag.TalkTo | Actor.Flag.Neutral));
    }

    [Fact]
    public void The_Storyteller_makes_a_thing_where_it_stands_and_kills_and_revives()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files);
        using var _ = server;
        // UI_STOPCREATEDOBJECT's blank is a lower-case `%s`, so the template's name is itself looked
        // up — and `Rat` is a string key: "Ghoul Rat".
        string said = st.Create("Rat");
        Assert.Equal("Created object 'Ghoul Rat'", said);
        var rat = st.Target!;
        Assert.Equal("Rat", rat.Template);
        Assert.True(Vector3.Distance(rat.Position, place.Steered!.Position) < 1f);
        Assert.Equal("Couldn't create object 'NoSuchThing'", st.Create("NoSuchThing"));
        st.Kill(rat);
        Assert.True(rat.Actor!.Dead);
        Assert.Equal("", st.Revive(rat));
        Assert.False(rat.Actor.Dead);
        // A living character is revived all the same — healed to full; only a
        // thing that is no character hears "not dead".
        rat.Actor.Health = rat.Actor.MaxHealth / 3f;
        Assert.Equal("", st.Revive(rat));
        Assert.Equal(rat.Actor.MaxHealth, rat.Actor.Health);
        var prop = place.Things.First(t => t.Type == ThingType.Prop);
        Assert.Equal("Selection is not a dead actor.", st.Revive(prop));
    }

    [Fact]
    public void The_chronicles_lists_are_read_for_the_scene_and_object_panes()
    {
        using var files = Install.Open();
        var ctd = ChronicleSet.Load(files)["MP_CurseDarkness"];
        var steps = StoryPowers.Steps(files, ctd);
        Assert.Equal([(2, 1), (3, 1), (5, 1)], steps.Select(s => (s.Scene, s.Subscene)));
        var quick = StoryPowers.ShortList(files, ctd);
        Assert.Contains("Melmoth", quick);
        Assert.Contains("GoldCoins", quick);
        Assert.Equal(23, quick.Count);
    }

    [Fact]
    public void A_paused_game_refuses_orders_and_a_kicked_player_is_told_so()
    {
        using var files = Install.Open();
        var (server, place, st) = Seat(files, player: null);
        using var _ = server;
        var sim = new Simulation(place);
        var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                    new NetCharacter { Name = "Bob", Template = "MP_Erik" }) { Find = new Strings(files).Find };
        var posted = new List<string>();
        client.Posted += (t, _) => posted.Add(t);
        Location? mine = null;
        client.Started += () =>
        {
            mine = Location.Load(files, client.Settings!.Level, scene: client.Settings.Scene, player: "MP_Erik", entrance: client.Settings.Entrance);
            client.Enter(mine, mine.Player!.Thing);
        };
        int ms = 0;
        bool Run(Func<bool> until)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000 && !until()) { ms += 33; sim.Step(0.033f); server.Poll(ms); client.Poll(0.033f); Thread.Sleep(2); }
            return until();
        }
        Assert.True(Run(() => client.Avatar is not null));
        st.Pause(true);
        Assert.True(Run(() => client.Paused));
        mine!.Player!.WalkTo(mine.Player.Position + new Vector3(100, 0, 0), mine.Walk!.Find(mine.Player.Position, 256f));
        Assert.True(Run(() => posted.Contains("Game paused.")));
        server.Kick(1);
        Assert.True(Run(() => client.State == ClientState.Gone));
        Assert.Equal("UI_STCPKICKBUTTON", client.Reason);
        client.Dispose();
    }

    /// <summary>
    /// The LAN room's own talk: a line said in one lobby is broadcast,
    /// and every lobby in the room — the speaker's own included, as 1.1's one socket hears its own
    /// broadcast — shows it as `<name> text` (kind 8, `"<%s> %s"`; Vitomir wrote
    /// `name: text` until then).
    /// </summary>
    [Fact]
    public void Two_lobbies_in_the_LAN_room_hear_each_other()
    {
        int port = 47000 + Environment.ProcessId % 1000;
        using var anna = new LanRoom(port);
        using var bob = new LanRoom(port);
        Assert.SkipUnless(anna.Listening && bob.Listening, "the room's port is taken");
        anna.Name = "Anna";
        var heard = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Her presence first: a line from a sender not on the room's list is dropped.
        while (sw.ElapsedMilliseconds < 2000 && !bob.Players.Contains("Anna")) { anna.Poll(); bob.Poll(); Thread.Sleep(5); }
        anna.Say("anyone for the inn?");
        sw.Restart();
        while (sw.ElapsedMilliseconds < 2000 && heard.Count == 0) { heard.AddRange(bob.Poll()); Thread.Sleep(5); }
        Assert.SkipWhen(heard.Count == 0, "this network does not deliver a broadcast");
        Assert.Equal("<Anna> anyone for the inn?", heard[0]);
        Assert.Contains("<Anna> anyone for the inn?", anna.Poll());
    }

    /// <summary>
    /// Load Saved: the load screen over the
    /// network saves (`MP<n>`, not the single-player `SP<n>`), and Load makes the game — from the create
    /// screen's name and boxes — **at the chronicle's revive location and entrance** (the start, then the
    /// arrival), wherever the save was taken; Start hands it over with the save. A save taken in *To Curse the
    /// Darkness*' cottage is hosted at the inn.
    /// </summary>
    [Fact]
    public void Load_Saved_hosts_a_network_save_at_the_chronicle_s_revive_location()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        int revive = story.Data.ReviveLocation, cottage = 2;
        Assert.NotEqual(revive, cottage);
        var slots = new Save.SaveSlotFiles(Path.Combine(rig.Crypt, "Saves"), Save.SaveSlotFiles.Network);
        Directory.CreateDirectory(slots.Folder);
        var saved = new Save.SaveGame { Chronicle = new Save.ChronicleSave { Name = story.Name, Current = story.NameOf(cottage) } };
        saved.Header.Description = "The cottage (00:01:02)";
        saved.Header.Location = story.NameOf(cottage);
        saved.World[story.NameOf(cottage)] = new Save.LocationSave { Level = story.At(cottage)!.LevelName, Entrance = 0 };
        Save.SaveArchive.Write(saved, slots.PathOf(3));
        Assert.EndsWith("MP3" + Save.SaveArchive.Extension, slots.PathOf(3));
        rig.Mp.NetSaves = slots;

        rig.Mp.Shot("mpcreategame");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgLoadSavedButton");
        var load = rig.Menus.Pane(FrontEnd.LoadFile);
        Assert.True(rig.Menus.IsUp(load));
        Assert.Contains(load.Lists["spLoadList"].Rows, r => r.Text.Contains("The cottage (00:01:02)"));
        Click(rig.Menus, FrontEnd.LoadFile, "spLoadLoadButton");
        rig.Mp.Frame(0);
        Assert.NotNull(rig.Mp.Server);
        Assert.Equal(story.At(revive)!.LevelName, rig.Mp.Server!.Settings.Level, ignoreCase: true);
        Assert.Equal(story.Data.ReviveEntrance, rig.Mp.Server.Settings.Entrance);
        Assert.Equal("MP_CurseDarkness", rig.Mp.Server.Settings.Chronicle);
        Assert.Equal(MenuRequest.None, rig.Menus.TakeRequest().Request);        // the load was the lobby's, not a single-player load
        Click(rig.Menus, MpMenus.ChatFile, "mpChatStartButton");
        Assert.Equal(slots.PathOf(3), rig.Mp.Handoff?.LoadPath);
    }

    /// <summary>
    /// **A single-player save is not hosted**: 1.1's every save path is `SaveGames\MP<n>` while a
    /// network session is up, so Load Saved reaches only the saves a network game
    /// wrote. One that names a single-player chronicle — a *Prague* save put among them — is refused with
    /// the create screen's error, and no game is made.
    /// </summary>
    [Fact]
    public void Load_Saved_refuses_a_single_player_game_s_save()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var slots = new Save.SaveSlotFiles(Path.Combine(rig.Crypt, "Saves"), Save.SaveSlotFiles.Network);
        Directory.CreateDirectory(slots.Folder);
        var saved = new Save.SaveGame { Chronicle = new Save.ChronicleSave { Name = "Prague", Current = "Monastery" } };
        saved.Header.Description = "Monastery 1 (01:28:05)";
        saved.Header.Location = "Monastery";
        Save.SaveArchive.Write(saved, slots.PathOf(1));
        rig.Mp.NetSaves = slots;

        rig.Mp.Shot("mpcreategame");
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgLoadSavedButton");
        Click(rig.Menus, FrontEnd.LoadFile, "spLoadLoadButton");
        rig.Mp.Frame(0);
        Assert.Null(rig.Mp.Server);
        Assert.Null(NetSaves.Start(saved, ChronicleSet.Load(files), out string why));
        Assert.Contains("single-player", why);
    }
}
