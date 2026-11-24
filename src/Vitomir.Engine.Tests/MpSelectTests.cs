// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The multiplayer screens as in 1.1
/// (by design): the select screen's empty folder opening the create screen and its
/// `lastplayer` (written by OK and Enter) — kept in a
/// settings file each test names, never the player's own; Create's set-up of a new character;
/// Join Game's protocol and chronicle checks, its password box and its error boxes against
/// the game's refusals that stay lines in the room; and the network escape
/// menu's Options, which does not pause.
/// </summary>
public class MpSelectTests
{
    sealed record Rig(FrontEnd Menus, MpMenus Mp, string Crypt, string Settings, Strings Text) : IDisposable
    {
        public void Dispose()
        {
            Mp.Server?.Dispose();
            Mp.Client?.Dispose();
            if (Directory.Exists(Crypt)) Directory.Delete(Crypt, recursive: true);
            if (File.Exists(Settings)) File.Delete(Settings);
        }
    }

    /// <summary>A rig whose settings file is a fresh temporary path, not yet written — never the player's own.</summary>
    static Rig Open(GameFiles files, string? crypt = null, bool remember = true)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        crypt ??= Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        string settings = Path.Combine(Path.GetTempPath(), $"vitomir-mpsel-{Guid.NewGuid():N}.ini");
        var mp = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false, SettingsPath = remember ? settings : null };
        return new Rig(menus, mp, crypt, settings, text);
    }

    static void Click(FrontEnd menus, string file, string control)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);
        Press(menus, x + e.Size[0] / 2f, y + e.Size[1] / 2f);
    }

    static void Press(FrontEnd menus, float x, float y)
    {
        menus.PointerAt(x, y);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    /// <summary>A list's row clicked: the pointer walked down the list until the row is hovered.</summary>
    static void ClickRow(FrontEnd menus, string file, string listName, int row)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(listName)!;
        var list = pane.Lists[listName];
        for (float y = pane.Y + e.Pos[1]; y < pane.Y + e.Pos[1] + e.Size[1]; y += 1f)
        {
            menus.PointerAt(pane.X + e.Pos[0] + e.Size[0] / 2f, y);
            menus.Frame(menus.NowMs);
            if (list.Hovered == row) { Press(menus, pane.X + e.Pos[0] + e.Size[0] / 2f, y); return; }
        }
        Assert.Fail($"no point of {listName} hovers row {row}");
    }

    // ---- the select screen -------------------------------------------------------------------------

    /// <summary>The host's own room (Host Game), and Player Setup over it — an empty folder opening the create screen at once.</summary>
    static void OpenRoom(Rig rig)
    {
        rig.Mp.HostGame();
        Assert.Equal(4, rig.Mp.Mode);
        rig.Mp.OpenPlayerSelect();
    }

    [Fact]
    public void Player_setup_then_create_new_player_then_cancel_comes_back_to_the_select_screen_on_the_character_in_use()
    {
        // As in 1.1: from the room with test1 saved and in use, Player Setup, Create New Player,
        // Cancel: the character selection screen, test1 listed and shown — the character in use is put
        // back, so the select screen stays (it finds someone loaded).
        using var files = Install.Open();
        using var rig = Open(files);
        OpenRoom(rig);
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        var select = rig.Menus.Pane(MpMenus.PlayerSelectFile);
        var lobby = rig.Menus.Pane(MpMenus.ChatFile);
        create.Edits["mppcName"].Set("test1");
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        rig.Mp.Frame(rig.Menus.NowMs);
        Assert.True(rig.Menus.IsUp(lobby));
        Assert.Equal("test1", rig.Mp.Me?.Name);
        Assert.Equal("test1", rig.Mp.Server!.Players.Single().Name);            // brought to the host's own game

        Click(rig.Menus, MpMenus.ChatFile, "mpChatPlayerSetupButton");
        Assert.True(rig.Menus.IsUp(select));
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsCreateNewButton");
        Assert.True(rig.Menus.IsUp(create));
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCancelButton");
        rig.Mp.Frame(rig.Menus.NowMs);
        Assert.False(create.Shown);
        Assert.True(rig.Menus.IsUp(select));
        Assert.Single(select.Lists["mppsPlayerList"].Rows);
        Assert.Equal(0, select.Lists["mppsPlayerList"].CurrentData);
        Assert.Equal("test1", select.Texts["mppsName"]);
        Assert.Equal("test1", rig.Mp.Me?.Name);
        Assert.True(rig.Mp.DrawsPortrait(select));
        Assert.True(lobby.Shown);
    }

    [Fact]
    public void While_Create_s_advancement_is_up_the_new_character_is_loaded_and_the_lobby_stays()
    {
        // The advancement opens on the new character, which is the one loaded until it
        // is written — so the lobby's frame does not take the player to the main menu meanwhile.
        using var files = Install.Open();
        using var rig = Open(files);
        Action<NetCharacter>? finish = null;
        NetCharacter? made = null;
        rig.Mp.AdvanceNew = (c, then) => { made = c; finish = then; };
        OpenRoom(rig);
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        create.Edits["mppcName"].Set("Anna");
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.NotNull(finish);
        Assert.Null(rig.Mp.Me);
        rig.Mp.Frame(rig.Menus.NowMs);
        Assert.True(rig.Menus.Pane(MpMenus.ChatFile).Shown);
        finish!(made!);
        Assert.Equal("Anna", rig.Mp.Me?.Name);
        rig.Mp.Frame(rig.Menus.NowMs);
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(MpMenus.PlayerSelectFile)));     // back to the select on her
        Assert.True(rig.Menus.Pane(MpMenus.ChatFile).Shown);
    }

    /// <summary>A key for the active pane, a frame after whatever was just shown (a key hook does not run on the frame its pane went up).</summary>
    static void Key(FrontEnd menus, MenuKey key)
    {
        menus.Frame(menus.NowMs);
        menus.Drawn();
        menus.Key(key);
    }

    [Fact]
    public void The_portrait_is_drawn_right_after_its_pane_so_a_dialog_over_it_covers_it()
    {
        // 1.1's pane draw calls the pane's hook after its controls, and the panes are drawn in turn: the model
        // under a dialog over its screen — here Create's own refusal of no name (UI_MPPC_NONAME). Earlier,
        // Vitomir drew it after every pane, over the box (over the Internet button's address box).
        using var files = Install.Open();
        using var rig = Open(files);
        OpenRoom(rig);
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MessageBoxFile)));
        var order = new List<string>();
        var ui = new UiCanvas(new UiAssets(files));
        var pass = new MenuPass(ui, rig.Menus) { Hook = p => { order.Add(p.File); return rig.Mp.DrawsPortrait(p) ? new Marker(ui, order) : null; } };
        pass.Emit(1280, 720, null!, null!);
        Assert.Equal([MpMenus.PlayerCreateFile, "portrait", FrontEnd.MessageBoxFile], order);
    }

    [Fact]
    public void A_panes_check_boxes_are_drawn_right_after_it_so_a_dialog_over_it_covers_them()
    {
        // 1.1 draws a pane's check boxes with its other controls before the next pane: mpCreateGame's
        // password box under a message box over the screen. Earlier, Vitomir drew every pane's boxes after all the panes.
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Mp.Shot("mpcreategame");
        rig.Menus.Message("Error", "words", ok: true, cancel: false);
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MessageBoxFile)));
        var order = new List<string>();
        var ui = new UiCanvas(new UiAssets(files));
        var pass = new MenuPass(ui, rig.Menus)
        {
            Pictures = p =>
            {
                var boxes = rig.Mp.Checkboxes(p).ToList();
                if (boxes.Count > 0) order.Add("boxes of " + p.File);
                return boxes;
            },
            Hook = p => { order.Add(p.File); return null; },
        };
        pass.Emit(1280, 720, null!, null!);
        int boxes = order.IndexOf("boxes of " + MpMenus.CreateGameFile), box = order.IndexOf(FrontEnd.MessageBoxFile);
        Assert.True(boxes >= 0 && box > boxes, string.Join(", ", order));
        Assert.Equal(boxes + 1, order.IndexOf(MpMenus.CreateGameFile));      // right after its own pane's controls, before its hook
        var created = rig.Menus.Pane(MpMenus.CreateGameFile);
        Assert.Contains(rig.Mp.Checkboxes(created), b => created.Element("mpcgPWProtectCheck") is { } e && b.X == created.X + e.Pos[0] && b.Y == created.Y + e.Pos[1]);
        Assert.Empty(rig.Mp.Checkboxes(rig.Menus.Pane(FrontEnd.MessageBoxFile)));
        Assert.Equal(rig.Menus.Visible().SelectMany(rig.Mp.Checkboxes).Count(), rig.Mp.Checkboxes().Count());
    }

    sealed class Marker(UiCanvas ui, List<string> order) : IUiPass
    {
        public UiCanvas Ui => ui;
        public void Emit(int width, int height, FreeCamera camera, Simulation sim) => order.Add("portrait");
    }

    [Fact]
    public void The_create_screen_lists_the_clans_in_the_clan_table_s_order_Caitiff_first_and_chosen()
    {
        // The create screen walks the clan table, which the loader builds at its head: the file
        // backwards. 1.1's screen: Caitiff, Ventrue, … Brujah, Caitiff chosen and described.
        using var files = Install.Open();
        using var rig = Open(files);
        Assert.Equal(["caitiff", "ventrue", "tremere", "malkavian", "giovanni", "toreador", "nosferatu", "gangrel", "cappadocian", "brujah"],
                     rig.Mp.ChoosableClans().Select(c => c.Name));
        OpenRoom(rig);
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        var clans = create.Lists["mppcClanList"];
        Assert.Equal(10, clans.Rows.Count);
        Assert.Equal(0, clans.Current);
        Assert.Equal(clans.Rows[0].Text, create.Texts["mppcClan"]);
        Assert.Equal("Caitiff", clans.Rows[0].Text);
    }

    [Fact]
    public void With_nobody_in_use_the_select_chooses_the_last_player_and_ok_and_enter_keep_the_one_taken()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var crypt = new Crypt(rig.Crypt);
        crypt.Save(new NetCharacter { Name = "Anna", Template = "MP_Serena", Type = ActorType.Vampire, Clan = "brujah" });
        crypt.Save(new NetCharacter { Name = "Bob", Template = "MP_Serena", Type = ActorType.Human });
        // The player's own file, with other lines in it: `lastplayer` names Bob in another case (the screen loads
        // the file and chooses the loaded one's row by `__strcmpi`).
        File.WriteAllText(rig.Settings, "; mine\n[options]\nsound = 3\n[multiplayer]\nlastplayer = bob\n");
        rig.Mp.OpenPlayerSelect();
        var pane = rig.Menus.Pane(MpMenus.PlayerSelectFile);
        var list = pane.Lists["mppsPlayerList"];
        Assert.Equal(1, list.Current);
        Assert.Equal("Bob", pane.Texts["mppsName"]);
        Assert.Null(rig.Mp.Me);                                        // taken only by OK

        // OK on Anna: she is in use and kept, the file's other lines left as they were.
        list.Select(0);
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        Assert.Equal("Anna", rig.Mp.Me?.Name);
        Assert.Equal("Anna", LastPlayer.Read(rig.Settings));
        string written = File.ReadAllText(rig.Settings);
        Assert.Contains("; mine", written);
        Assert.Contains("sound = 3", written);

        // Opened again with Anna in use: her row, whatever is kept (the one in use described).
        File.WriteAllText(rig.Settings, "[multiplayer]\nlastplayer = Bob\n");
        rig.Mp.OpenPlayerSelect();
        Assert.Equal(0, list.Current);
        // Enter is OK and keeps Bob (the key hook).
        list.Select(1);
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Enter);
        Assert.Equal("Bob", rig.Mp.Me?.Name);
        Assert.Equal("Bob", LastPlayer.Read(rig.Settings));
        // Escape back to the one chosen is ours ("keep escape"), and writes nothing.
        File.Delete(rig.Settings);
        rig.Mp.OpenPlayerSelect();
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Escape);
        Assert.False(pane.Shown);
        Assert.False(File.Exists(rig.Settings));

        // A second run: a remembered name nobody has is the first row (row 0 chosen).
        using var again = Open(files, rig.Crypt);
        File.WriteAllText(again.Settings, "[multiplayer]\nlastplayer = Cecilie\n");
        again.Mp.OpenPlayerSelect();
        Assert.Equal(0, again.Menus.Pane(MpMenus.PlayerSelectFile).Lists["mppsPlayerList"].Current);
    }

    /// <summary>
    /// The screens remember nobody until they are given a settings file, so no test — and no run that did not
    /// ask — reads or writes the player's own `vitomir.ini`.
    /// </summary>
    [Fact]
    public void The_screens_remember_nobody_without_a_settings_file_of_their_own()
    {
        using var files = Install.Open();
        using var rig = Open(files, remember: false);
        Assert.Null(rig.Mp.SettingsPath);
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Bob", Template = "MP_Serena" });
        rig.Mp.OpenPlayerSelect();
        Assert.Equal(0, rig.Menus.Pane(MpMenus.PlayerSelectFile).Lists["mppsPlayerList"].Current);   // nobody kept: the first
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        Assert.Equal("Anna", rig.Mp.Me?.Name);
        Assert.False(File.Exists(rig.Settings));
    }

    [Fact]
    public void The_last_players_row_is_found_without_regard_to_case_and_the_first_stands_in_for_a_stranger()
    {
        NetCharacter[] listed = [new() { Name = "Anna" }, new() { Name = "Bob" }];
        Assert.Equal(1, LastPlayer.Row(listed, "BOB"));
        Assert.Equal(0, LastPlayer.Row(listed, "Cecilie"));
        Assert.Equal(0, LastPlayer.Row(listed, ""));
        Assert.Equal(-1, LastPlayer.Row([], "Anna"));
        Assert.Equal("", LastPlayer.Read(Path.Combine(Path.GetTempPath(), $"vitomir-none-{Guid.NewGuid():N}.ini")));
    }

    // ---- a new character -------------------------------------------------------------------

    [Fact]
    public void Create_gives_the_caitiff_every_open_groups_first_and_the_ventrue_and_giovanni_their_blood()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var powers = new DisciplineDefinitions(files);
        WorldThing Made(ActorType type, string clan) =>
            rig.Mp.NewBody(new NetCharacter { Name = "X", Template = "MP_Serena", Type = type, Clan = clan }, powers)!;

        // The Caitiff (clan flag 0x1): the first of every group not flagged 0x1 made known.
        var caitiff = Made(ActorType.Vampire, "caitiff").Actor!;
        foreach (var group in powers.Groups!.All)
        {
            var first = powers.FirstInGroup(group.Name);
            if (first is null || first.Name is "feed" or "bloodhealing") continue;
            if ((group.Flags & MpCreateSetUp.ClanOnlyGroup) == 0) Assert.NotNull(caitiff.Disciplines.Of(first));
            else Assert.Null(caitiff.Disciplines.Of(first));
        }
        Assert.Contains(powers.Groups.All, g => (g.Flags & MpCreateSetUp.ClanOnlyGroup) != 0);   // Mortis, Thaumaturgy, …
        // Every new character tracks humanity (0x8000); a Brujah is not a fast drinker and keeps her Blood Rating.
        Assert.True(caitiff.Has(Actor.Flag.HumanityTrack));
        var brujah = Made(ActorType.Vampire, "brujah").Actor!;
        Assert.False(brujah.Has(Actor.Flag.FastDrinker));
        Assert.Null(brujah.Disciplines.Of(powers.FirstInGroup("DGRP_Animalism")!));        // not the Caitiff's rule

        // Clan 14, the Ventrue: base Blood Rating 0.6; clan 7, the Giovanni: 0.75 and a fast drinker.
        var ventrue = Made(ActorType.Vampire, "ventrue").Actor!;
        Assert.Equal(0.6f, ventrue.BaseStat[(int)ActorStat.BloodRating]);
        Assert.False(ventrue.Has(Actor.Flag.FastDrinker));
        var giovanni = Made(ActorType.Vampire, "giovanni").Actor!;
        Assert.Equal(0.75f, giovanni.BaseStat[(int)ActorStat.BloodRating]);
        Assert.True(giovanni.Has(Actor.Flag.FastDrinker));
    }

    [Fact]
    public void Create_puts_back_the_born_effects_of_the_type_chosen()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var powers = new DisciplineDefinitions(files);
        var kindred = rig.Mp.NewBody(new NetCharacter { Name = "K", Template = "MP_Serena", Type = ActorType.Vampire, Clan = "brujah" }, powers)!;
        var ghoul = rig.Mp.NewBody(new NetCharacter { Name = "G", Template = "MP_Serena", Type = ActorType.Ghoul, Clan = "brujah" }, powers)!;
        var human = rig.Mp.NewBody(new NetCharacter { Name = "H", Template = "MP_Serena", Type = ActorType.Human }, powers)!;
        // A vampire's and a ghoul's frenzy fades, a human's mana and blood come back.
        Assert.NotNull(kindred.Effects.Find("ef_decreasefrenzy"));
        Assert.Null(kindred.Effects.Find("ef_increasemanaot"));
        Assert.NotNull(ghoul.Effects.Find("ef_decreasefrenzy"));
        Assert.Equal(0, ghoul.Actor!.Disciplines["feed"]!.Level);          // and feed and bloodhealing learnt
        Assert.NotNull(human.Effects.Find("ef_increasemanaot"));
        Assert.NotNull(human.Effects.Find("ef_increasebloodot"));
        Assert.Null(human.Effects.Find("ef_decreasefrenzy"));
        Assert.Null(human.Actor!.Disciplines["feed"]);
        Assert.Equal("human", human.Actor.Clan);
    }

    // ---- joining ------------------------------------------------------------------------------------

    [Fact]
    public void Join_game_refuses_a_stranger_protocol_and_an_unknown_chronicle_before_connecting()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        var inn = new GameInfo("Midnight at the Inn", "MP_DAInn", 1, 4, false, 4994);
        Assert.Null(LobbyJoin.Check(inn, chronicles));
        // A game of another protocol is still listed (the answer read whatever its version) and refused at Join.
        var old = GameInfo.Read((inn with { ProtocolVersion = Protocol.Version - 1 }).ToArray())!;
        Assert.Equal(Protocol.Version - 1, old.ProtocolVersion);
        Assert.Equal("UI_NET_BADPROTOCOL", LobbyJoin.Check(old, chronicles));
        Assert.Equal("UI_NET_BADCHRON", LobbyJoin.Check(inn with { Chronicle = "MP_Nowhere" }, chronicles));
        Assert.Equal("UI_NET_BADPROTOCOL", LobbyJoin.Check(old with { Chronicle = "MP_Nowhere" }, chronicles));   // the protocol first
        // The session's refusals come in a box; the game's answer's are lines in the room.
        Assert.Equal(("UI_NET_JOINGAMEERROR", "UI_NET_WRONGGAMEPASSWORD"), LobbyJoin.Box("UI_NET_WRONGGAMEPASSWORD"));
        Assert.Equal(("UI_NET_CONNECTIONERROR", "UI_NET_BADADDR"), LobbyJoin.Box("UI_NET_BADADDR"));
        Assert.Equal(("UI_NET_CONNECTIONERROR", "UI_NET_NONEWPLAYERS"), LobbyJoin.Box("UI_NET_NONEWPLAYERS"));
        foreach (var line in new[] { "UI_NET_JOINFULL", "UI_NET_JOINCLOSED", "UI_NET_JOINDUPLICATE", "UI_NET_BADTEMPLATE", "UI_NET_JOINFAILED" })
            Assert.Null(LobbyJoin.Box(line));
    }

    /// <summary>Anna in the LAN room, the host's game found and its row clicked.</summary>
    static (Rig Rig, GameServer Host) Found(GameFiles files, GameSettings settings, string hostName = "Server")
    {
        var host = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = hostName });
        var rig = Open(files);
        rig.Mp.Port = host.Peer.Port;                  // the LAN query goes to the host's port (1.1's 4994)
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        rig.Mp.Open();
        Click(rig.Menus, MpMenus.SelectFile, "mpSelectInternet");                   // Join Game: no character asked for
        Assert.False(rig.Menus.Pane(MpMenus.PlayerSelectFile).Shown);
        var games = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatGame"];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 4000 && games.Rows.Count == 0) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Single(games.Rows);
        ClickRow(rig.Menus, MpMenus.ChatFile, "mpChatGame", 0);
        return (rig, host);
    }

    /// <summary>In the game's room: Player Setup, the one kept chosen with OK, and brought to the host.</summary>
    static void Bring(Rig rig, GameServer host)
    {
        var room = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatRoom"];
        int lines = room.Rows.Count;
        Click(rig.Menus, MpMenus.ChatFile, "mpChatPlayerSetupButton");
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        Pump(rig, host, () => rig.Mp.Client is not { Accepted: false } || room.Rows.Count > lines);   // accepted, or refused in a line
    }

    static void Pump(Rig rig, GameServer host, Func<bool> until, int ms = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms && !until()) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
    }

    [Fact]
    public void A_password_game_asks_for_the_password_in_a_box_and_a_wrong_one_comes_back_in_an_error_box()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var (rig, host) = Found(files, GameSettings.For(inn, "Midnight at the Inn", GameFlags.AutoStoryteller, password: "garlic"));
        using var _ = rig;
        using var __ = host;
        var box = rig.Menus.Pane(FrontEnd.InputBoxFile);
        var message = rig.Menus.Pane(FrontEnd.MessageBoxFile);
        var room = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatRoom"];

        // Join Game on a game flagged 0x4: UI_NET_ENTERGAMEPASSWORD, typed as stars, nothing sent yet.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        Assert.True(rig.Menus.IsUp(box));
        Assert.Equal(rig.Text["UI_NET_ENTERGAMEPASSWORD"], box.Texts["ibTitle"]);
        Assert.True(box.Edits["ibEdit"].Password);
        Assert.Null(rig.Mp.Client);

        // A wrong one (refusal −4): UI_NET_WRONGGAMEPASSWORD under UI_NET_JOINGAMEERROR, in a box.
        box.Edits["ibEdit"].Set("onion");
        Click(rig.Menus, FrontEnd.InputBoxFile, FrontEnd.InputOk);
        Pump(rig, host, () => message.Shown);
        Assert.True(message.Shown);
        Assert.Equal(rig.Text["UI_NET_JOINGAMEERROR"], message.Texts["mbTitle"]);
        Assert.Equal(rig.Text["UI_NET_WRONGGAMEPASSWORD"], message.Texts["mbTextBox"]);
        Assert.DoesNotContain(room.Rows, r => r.Text == rig.Text["UI_NET_WRONGGAMEPASSWORD"]);   // not a line in the room
        Assert.Equal(1, rig.Mp.Mode);
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Enter);

        // Cancel sends nothing; the right one joins.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        Assert.False(box.Edits["ibEdit"].Text.Length > 0);
        Click(rig.Menus, FrontEnd.InputBoxFile, FrontEnd.InputCancel);
        Assert.Null(rig.Mp.Client);
        Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        box.Edits["ibEdit"].Set("garlic");
        Click(rig.Menus, FrontEnd.InputBoxFile, FrontEnd.InputOk);
        Pump(rig, host, () => rig.Mp.Mode == 2);
        Assert.Equal(2, rig.Mp.Mode);
        Bring(rig, host);
        Assert.Contains(host.Players, p => p.Name == "Anna");
        // A password box's stars are its own: the next input box shows what is typed.
        Assert.True(rig.Menus.Input("t", "", 8));
        Assert.False(box.Edits["ibEdit"].Password);
    }

    [Fact]
    public void An_unknown_chronicle_is_refused_in_a_box_and_a_refusal_in_the_hosts_answer_stays_a_line_in_the_room()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var message = (FrontEnd f) => f.Pane(FrontEnd.MessageBoxFile);
        {
            var (rig, host) = Found(files, GameSettings.For(inn, "Elsewhere", GameFlags.AutoStoryteller) with { Chronicle = "MP_Nowhere" });
            using var _ = rig;
            using var __ = host;
            Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
            Assert.True(message(rig.Menus).Shown);                            // no chronicle file is found
            Assert.Equal(rig.Text["UI_NET_CONNECTIONERROR"], message(rig.Menus).Texts["mbTitle"]);
            Assert.Equal(rig.Text["UI_NET_BADCHRON"], message(rig.Menus).Texts["mbTextBox"]);
            Assert.Null(rig.Mp.Client);                                        // nothing sent
        }
        {
            // The host's own character is Anna: the host refuses a second when she is brought to the room, a line in the room,
            // and the player stays in the room to choose another.
            var (rig, host) = Found(files, GameSettings.For(inn, "Midnight at the Inn", GameFlags.AutoStoryteller), hostName: "Anna");
            using var _ = rig;
            using var __ = host;
            var room = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatRoom"];
            Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
            Pump(rig, host, () => rig.Mp.Mode == 2);
            Bring(rig, host);
            Pump(rig, host, () => room.Rows.Any(r => r.Text == rig.Text["UI_NET_JOINDUPLICATE"]));
            Assert.Contains(room.Rows, r => r.Text == rig.Text["UI_NET_JOINDUPLICATE"]);
            Assert.False(message(rig.Menus).Shown);
            Assert.Equal(2, rig.Mp.Mode);
            Assert.False(rig.Mp.Client!.Accepted);
            Assert.Null(rig.Mp.Me);
        }
    }

    // ---- the room's line and the network escape menu ------------------------------------------------

    [Fact]
    public void The_LAN_rooms_line_is_the_name_in_angle_brackets_then_the_words()
    {
        Assert.Equal("<Anna> anyone for the inn?", LanRoom.Line("Anna", "anyone for the inn?"));   // "<%s> %s"
    }

    [Fact]
    public void The_network_escape_menus_options_opens_the_options_screen_and_the_game_goes_on()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = FrontEnd.StateGame, SinglePlayer = false, EscapeMenu = NetEscape.File,
        };
        string ini = Path.Combine(Path.GetTempPath(), $"vitomir-mpsel-options-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, new GameOptions()) { SavePath = ini };
        screen.Fixes.SavePath = ini;
        menus.Options = screen;
        bool quit = false;
        menus.OtherClick += (pane, button) => NetEscape.Click(menus, pane, button, isHost: false, () => { }, () => quit = true);
        menus.OpenEscape();
        menus.Drawn();
        Assert.True(menus.IsUp(menus.Pane(NetEscape.File)));
        Click(menus, NetEscape.File, "emOptionsButton");
        // The same screen, the menu put away, and in a network game nothing paused.
        Assert.True(screen.IsOpen);
        Assert.True(menus.IsUp(menus.Pane(OptionsMenu.OptionsFile)));
        Assert.False(menus.Pane(NetEscape.File).Shown);
        Assert.False(menus.Paused);
        Click(menus, OptionsMenu.OptionsFile, "opDoneButton");
        Assert.False(screen.IsOpen);
        Assert.False(menus.Paused);
        Assert.False(quit);
        if (File.Exists(ini)) File.Delete(ini);
    }
}
