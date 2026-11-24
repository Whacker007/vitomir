// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The lobby's two lower lists: the game pane —
/// in a game, forming or under way and who is in it from slot 1; in the room, the chosen row's game
/// with its name, chronicle and options, which the LAN answer now carries as 1.1's
/// announcement does — and the LAN room's players (every lobby says it is there
/// every 2 s, message 0x4e; one unheard for 8 s is forgotten, the rest close up, counted from
/// 0). The room's tests are two or three lobbies in one process on one port, as the talk's test is, and
/// skip where the network does not deliver a broadcast; their clocks are turned by hand.
/// </summary>
public class LobbyPaneTests
{
    /// <summary>The keys themselves, so a line's shape is pinned without the install's words.</summary>
    static string Keys(string key) => key;

    static int RoomPort() => 46000 + Environment.ProcessId % 1000;

    [Fact]
    public void The_game_pane_in_a_game_says_whether_it_is_forming_and_the_players_are_listed_from_1()
    {
        // Ours: the players in the room's players' box, a slot still choosing said so, one without the chronicle marked.
        Assert.Equal(["UI_NET_PLAYERSINGAME", "UI_NET_PLAYER 1 : Anna", "UI_NET_PLAYER 2 : choosing a character", "UI_NET_PLAYER 3 : Cecilie (no chronicle)"],
                     LobbyGamePane.Players([(2, "Cecilie", true), (0, "Anna", false), (1, "", false)], Keys));
        // The game pane: forming or under way, then the game as the join screen describes one.
        Assert.Equal(["UI_NET_FORMING", "UI_NET_GAMENAME Anna's game", "UI_NET_CHRONICLE: MP_DAInn"],
                     LobbyGamePane.InGame(true, new GameInfo("Anna's game", "MP_DAInn", 1, 4, false, 4994, GameFlags.CoOp), Keys));
        Assert.Equal(["UI_NET_INPROGRESS"], LobbyGamePane.InGame(false, null, Keys));
    }

    /// <summary>
    /// The options: password, Storyteller, closed, and "combative" when the Co-op flag 0x100
    /// is clear — each followed by a space, the last one's kept; none, no options line.
    /// </summary>
    [Fact]
    public void The_chosen_game_is_described_by_name_chronicle_and_options()
    {
        var game = new GameInfo("Midnight at the Inn", "MP_DAInn", 1, 4, false, 4994,
                                GameFlags.Password | GameFlags.Moderated | GameFlags.Locked | GameFlags.AutoStoryteller);
        Assert.Equal(["UI_NET_GAMENAME Midnight at the Inn", "UI_NET_CHRONICLE: MP_DAInn",
                      "UI_MPCREATEGAME_OPTIONS: UI_MPCREATEGAME_PW UI_NET_STORYTELLER UI_NET_CLOSED UI_NET_COMBATIVE "],
                     LobbyGamePane.Chosen(game, Keys));
        Assert.Equal(2, LobbyGamePane.Chosen(game with { Flags = GameFlags.CoOp }, Keys).Count);   // co-op, nothing else: no options line
        Assert.Empty(LobbyGamePane.Chosen(null, Keys));
    }

    /// <summary>A row chosen: a row of the list is its game; no row, or one past the end, is none.</summary>
    [Fact]
    public void A_games_list_row_chosen_is_that_game_and_no_row_is_none()
    {
        var at = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 4994);
        FoundGame[] games = [new(new GameInfo("A", "", 1, 4, false, 4994), at), new(new GameInfo("B", "", 1, 4, false, 4994), at)];
        Assert.Equal("B", LobbyGamePane.Choose(games, 1)?.Info.Name);
        Assert.Null(LobbyGamePane.Choose(games, -1));
        Assert.Null(LobbyGamePane.Choose(games, 2));
    }

    /// <summary>
    /// The wire (protocol 14): the host's answer carries the game's flags — 1.1's announcement carries
    /// the record's options — so a finder's chosen game shows its options; and a
    /// game gone quiet is forgotten after 8 s, looked at once a second, which
    /// leaves nothing chosen.
    /// </summary>
    [Fact]
    public void A_found_game_carries_its_flags_and_is_forgotten_after_8_s()
    {
        var flags = GameFlags.Password | GameFlags.Moderated;
        var host = new GameServer(new GameSettings { Name = "Midnight at the Inn", Chronicle = "MP_DAInn", Flags = flags },
                                  new NetPeer(0), new NetCharacter { Name = "Server" });
        long now = 0;
        using var finder = new LanFinder(host.Peer.Port) { Clock = () => now };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 4000 && finder.Games.Count == 0) { host.Poll((int)sw.ElapsedMilliseconds); finder.Poll(); Thread.Sleep(5); }
        var found = Assert.Single(finder.Games);
        Assert.Equal(flags, found.Info.Flags);
        Assert.Equal(["UI_NET_GAMENAME Midnight at the Inn", "UI_NET_CHRONICLE: MP_DAInn",
                      "UI_MPCREATEGAME_OPTIONS: UI_MPCREATEGAME_PW UI_NET_STORYTELLER UI_NET_COMBATIVE "],
                     LobbyGamePane.Chosen(LobbyGamePane.Choose(finder.Games, 0)?.Info, Keys));

        host.Dispose();
        for (now = 1000; now <= 8000; now += 1000) finder.Poll();
        Assert.Single(finder.Games);                                   // heard at 0: 0 + 8000 < 8000 is false
        now = 9000;
        finder.Poll();
        Assert.Empty(finder.Games);
        Assert.Empty(LobbyGamePane.Chosen(LobbyGamePane.Choose(finder.Games, 0)?.Info, Keys));
    }

    /// <summary>
    /// The room's players: two lobbies on one machine — one address, two tokens
    /// (ours) — each hear the other and themselves, and list them in the order first heard as
    /// "UI_NET_PLAYER %d: %s" from 0. A player who renames is the same row, renamed.
    /// </summary>
    [Fact]
    public void Two_lobbies_in_the_LAN_room_list_each_other_counted_from_0()
    {
        long now = 0;
        using var anna = new LanRoom(RoomPort()) { Clock = () => now, Name = "Anna" };
        using var bob = new LanRoom(RoomPort()) { Clock = () => now, Name = "Bob" };
        Assert.SkipUnless(anna.Listening && bob.Listening, "the room's port is taken");
        anna.Poll();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000 && (anna.Players.Count < 2 || bob.Players.Count < 2)) { bob.Poll(); anna.Poll(); Thread.Sleep(5); }
        Assert.SkipWhen(bob.Players.Count == 0, "this network does not deliver a broadcast");
        Assert.Equal(["UI_NET_PLAYER 0: Anna", "UI_NET_PLAYER 1: Bob"], LanRoom.Rows(bob.Players, Keys));
        Assert.Equal(["Anna", "Bob"], anna.Players.Order());

        bob.Name = "Robert";
        now = LanRoom.PresenceMs;
        sw.Restart();
        while (sw.ElapsedMilliseconds < 2000 && !anna.Players.Contains("Robert")) { bob.Poll(); anna.Poll(); Thread.Sleep(5); }
        Assert.Equal(2, anna.Players.Count);
        Assert.Contains("Robert", anna.Players);
    }

    /// <summary>
    /// The stale: once a second, a player not heard for 8 s is forgotten — at exactly 8 s it is
    /// still there — and the rest close up in order, the list counted from 0 again.
    /// </summary>
    [Fact]
    public void A_player_unheard_for_8_s_is_forgotten_and_the_rest_close_up()
    {
        long now = 0;
        using var anna = new LanRoom(RoomPort() + 2) { Clock = () => now, Name = "Anna" };
        using var bob = new LanRoom(RoomPort() + 2) { Clock = () => now, Name = "Bob" };
        using var cecilie = new LanRoom(RoomPort() + 2) { Clock = () => now, Name = "Cecilie" };
        Assert.SkipUnless(anna.Listening && bob.Listening && cecilie.Listening, "the room's port is taken");
        anna.Poll(); bob.Poll(); cecilie.Poll();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000 && anna.Players.Count < 3) { anna.Poll(); Thread.Sleep(5); }
        Assert.SkipWhen(anna.Players.Count == 0, "this network does not deliver a broadcast");
        Assert.Equal(["Anna", "Bob", "Cecilie"], anna.Players);

        // Bob goes quiet; the others go on saying they are there every 2 s.
        void Step()
        {
            anna.Poll(); cecilie.Poll();
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 40) { anna.Poll(); Thread.Sleep(5); }
        }
        for (now = 1000; now <= 8000; now += 1000) Step();
        Assert.Contains("Bob", anna.Players);                            // 0 + 8000 < 8000 is false
        now = 9000;
        Step();
        Assert.Equal(["UI_NET_PLAYER 0: Anna", "UI_NET_PLAYER 1: Cecilie"], LanRoom.Rows(anna.Players, Keys));
    }

    /// <summary>
    /// The lobby itself (`MpMenus`, the harness's `--menu-open mplobby`): the chosen row's game is
    /// described in `mpChatGameStatus` in the install's words, choosing the other row describes the
    /// other game, and the room's players are in `mpChatRoomStatus`;
    /// in a game's room the pane says it is forming and what the game is, and the players' box who is in it.
    /// </summary>
    [Fact]
    public void The_lobby_describes_the_chosen_game_and_lists_the_room()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mp = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false };
            mp.Shot("mplobby");
            mp.Frame(0);
            var chat = menus.Pane(MpMenus.ChatFile);
            string T(string key) => text.Find(key) ?? key;
            var pane = chat.Lists["mpChatGameStatus"].Rows.Select(r => r.Text).ToList();
            Assert.Equal($"{T("UI_NET_GAMENAME")} Midnight at the Inn", pane[0]);
            Assert.Equal($"{T("UI_NET_CHRONICLE")}: {T("MP_DAInn")}", pane[1]);
            Assert.NotEqual("MP_DAInn", T("MP_DAInn"));                   // the chronicle by its name, not its file
            Assert.Equal($"{T("UI_MPCREATEGAME_OPTIONS")}: {T("UI_MPCREATEGAME_PW")} {T("UI_NET_STORYTELLER")} {T("UI_NET_CLOSED")} {T("UI_NET_COMBATIVE")} ", pane[2]);

            chat.Lists["mpChatGame"].Select(0);
            mp.Frame(0);
            pane = [.. chat.Lists["mpChatGameStatus"].Rows.Select(r => r.Text)];
            Assert.Equal($"{T("UI_NET_GAMENAME")} Anna's game", pane[0]);
            Assert.Equal(2, pane.Count);                                  // co-op, no password: no options

            Assert.Equal([$"{T("UI_NET_PLAYER")} 0: Anna", $"{T("UI_NET_PLAYER")} 1: Bob", $"{T("UI_NET_PLAYER")} 2: Cecilie"],
                         chat.Lists["mpChatRoomStatus"].Rows.Select(r => r.Text));

            var room = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false };
            room.Shot("mpgameroom");
            room.Frame(0);
            Assert.Equal([T("UI_NET_FORMING"), $"{T("UI_NET_GAMENAME")} Anna's game", $"{T("UI_NET_CHRONICLE")}: {T("MP_CurseDarkness")}"],
                         chat.Lists["mpChatGameStatus"].Rows.Select(r => r.Text));
            Assert.Equal([T("UI_NET_PLAYERSINGAME"), $"{T("UI_NET_PLAYER")} 1 : Anna", $"{T("UI_NET_PLAYER")} 2 : {MpSetup.Choosing}",
                          $"{T("UI_NET_PLAYER")} 3 : Cecilie {MpSetup.NoChronicle}"],
                         chat.Lists["mpChatRoomStatus"].Rows.Select(r => r.Text));
        }
        finally
        {
            if (Directory.Exists(crypt)) Directory.Delete(crypt, recursive: true);
        }
    }

    /// <summary>
    /// The room's line (kind 8): the sender is found in the room's players — 1.1 by its address,
    /// Vitomir by its token — and the line shown as `<name> text` with **the list's** name for it; a line from a
    /// sender not on the list is dropped, and so is one from a player forgotten after 8 s unheard. Datagrams are
    /// handed to the room as its pump reads them, so no broadcast is needed.
    /// </summary>
    [Fact]
    public void A_room_line_from_a_sender_not_on_the_room_s_list_is_dropped_and_one_on_it_takes_the_list_s_name()
    {
        long now = 0;
        using var room = new LanRoom(RoomPort() + 4) { Clock = () => now };
        var heard = new List<string>();
        room.Hear(LanRoom.Saying(7, "anyone for the inn?"), now, heard);
        Assert.Empty(heard);                                                     // not on the list: dropped

        room.Hear(LanRoom.Presence("Anna", 7), now, heard);
        room.Hear(LanRoom.Saying(7, "anyone for the inn?"), now, heard);
        Assert.Equal(["<Anna> anyone for the inn?"], heard);
        room.Hear(LanRoom.Saying(8, "me!"), now, heard);                         // somebody else, never heard: dropped
        Assert.Single(heard);

        room.Hear(LanRoom.Presence("Anka", 7), now, heard);                      // renamed: the list's name is the line's
        room.Hear(LanRoom.Saying(7, "still here"), now, heard);
        Assert.Equal("<Anka> still here", heard[^1]);

        now = LanRoom.ForgetMs + 1;                                              // unheard for more than 8 s: forgotten
        room.Poll();
        Assert.Empty(room.Players);
        room.Hear(LanRoom.Saying(7, "hello?"), now, heard);
        Assert.Equal(2, heard.Count);
    }
}
