// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The multiplayer flow of ours (protocol 23): `mpSelect`'s Host Game makes the game at once and lands its host in the
/// game's room; Join Game's join screen joins by address and says within a few seconds, naming the address, when nobody
/// answers; and in the room each player chooses a character (a duplicate refused there in 1.1's words), the host chooses
/// the chronicle (Game Setup) and everybody sees it, and Start is greyed until every player has a character and the chronicle.
/// </summary>
public class MpRoomTests
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

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        string settings = Path.Combine(Path.GetTempPath(), $"vitomir-mproom-{Guid.NewGuid():N}.ini");
        var mp = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false, SettingsPath = settings };
        return new Rig(menus, mp, crypt, settings, text);
    }

    static void Click(FrontEnd menus, string file, string control)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);
        menus.PointerAt(x + e.Size[0] / 2f, y + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    static bool Pump(Rig rig, Func<bool> until, int ms = 6000, params GameClient[] clients)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms && !until())
        {
            rig.Mp.Frame((int)sw.ElapsedMilliseconds);
            foreach (var c in clients) c.Poll(0.03f);
            Thread.Sleep(3);
        }
        return until();
    }

    static List<string> Rows(Rig rig, string list) => [.. rig.Menus.Pane(MpMenus.ChatFile).Lists[list].Rows.Select(r => r.Text)];

    /// <summary>
    /// **Host Game** makes the game at once — with the settings Game Setup last kept (here a chronicle, a name and the
    /// options written in the player's file) — and the host lands in its room as host (mode 4): Game Setup, Player Setup,
    /// Leave Game and Start shown, Start greyed while the host has no character. Player Setup over the room: the empty folder's
    /// create screen, a character made and taken by OK, brought to the host's own game — Start lit, and it hands the game over.
    /// </summary>
    [Fact]
    public void Host_Game_lands_in_the_room_and_the_host_chooses_its_character_there()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        File.WriteAllText(rig.Settings, "[multiplayer]\ngamename = The kept game\nchronicle = MP_DAInn\ngameoptions = 500\n");
        rig.Mp.Open();
        Click(rig.Menus, MpMenus.SelectFile, "mpSelectLan");           // 1.1's LAN button, the first: Host Game
        Assert.Equal(4, rig.Mp.Mode);
        var server = rig.Mp.Server!;
        Assert.Equal("The kept game", server.Settings.Name);
        Assert.Equal("MP_DAInn", server.Settings.Chronicle);
        Assert.Equal(GameFlags.CoOp | GameFlags.Moderated, server.Flags);                // as kept (one Storyteller box is ticked)
        Assert.Equal("", server.Settings.Password);
        var chat = rig.Menus.Pane(MpMenus.ChatFile);
        Assert.True(rig.Menus.IsUp(chat));
        Assert.False(rig.Menus.Pane(MpMenus.SelectFile).Shown);
        foreach (var shown in new[] { "mpChatCreateGameButton", "mpChatPlayerSetupButton", "mpChatLeaveGameButton", "mpChatStartButton" })
            Assert.DoesNotContain(shown, chat.Hidden);
        foreach (var hidden in new[] { "mpChatJoinGameButton", "mpChatChangeRoomButton", "mpChatLogOutButton" }) Assert.Contains(hidden, chat.Hidden);
        rig.Mp.Frame(0);
        Assert.Equal(ButtonState.Greyed, chat.Buttons["mpChatStartButton"]);
        Assert.Contains(MpSetup.ChooseLine, Rows(rig, "mpChatRoom"));
        Assert.Contains($"{rig.Text["UI_NET_PLAYER"]} 1 : {MpSetup.Choosing}", Rows(rig, "mpChatRoomStatus"));
        Click(rig.Menus, MpMenus.ChatFile, "mpChatStartButton");
        Assert.Null(rig.Mp.Handoff);

        // Player Setup: an empty folder, the create screen at once; made, then OK on the select screen.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatPlayerSetupButton");
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        Assert.True(rig.Menus.IsUp(create));
        create.Edits["mppcName"].Set("Anna");
        Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcCreateButton");
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(MpMenus.PlayerSelectFile)));
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        Assert.True(rig.Menus.IsUp(chat));
        Assert.Equal("Anna", server.Players.Single().Name);
        rig.Mp.Frame(0);
        Assert.Contains($"{rig.Text["UI_NET_PLAYER"]} 1 : Anna", Rows(rig, "mpChatRoomStatus"));
        Assert.Equal(ButtonState.Normal, chat.Buttons["mpChatStartButton"]);
        Click(rig.Menus, MpMenus.ChatFile, "mpChatStartButton");
        Assert.Equal("Anna", rig.Mp.Handoff?.Me.Name);
        Assert.Same(server, rig.Mp.Handoff!.Server);
    }

    /// <summary>
    /// **By Address…** on the join screen: the box is empty with nothing kept (never the loopback by itself); a join to a closed
    /// port is refused **within about five seconds** in a box naming the address (ours), the join screen left as it was; the
    /// address is kept and shown the next time. Leave Game and Back go to the Host/Join screen.
    /// </summary>
    [Fact]
    public void A_join_by_address_nobody_answers_is_refused_within_seconds_naming_the_address()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        int closed;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) closed = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        rig.Mp.OpenJoin(lan: false);
        Click(rig.Menus, MpMenus.ChatFile, "mpChatChangeRoomButton");      // By Address…
        var box = rig.Menus.Pane(FrontEnd.InputBoxFile);
        Assert.True(rig.Menus.IsUp(box));
        Assert.Equal(MpMenus.AddressTitle, box.Texts["ibTitle"]);
        Assert.Equal("", box.Edits["ibEdit"].Text);
        box.Edits["ibEdit"].Set($"127.0.0.1:{closed}");
        var sw = Stopwatch.StartNew();
        Click(rig.Menus, FrontEnd.InputBoxFile, FrontEnd.InputOk);
        Assert.NotNull(rig.Mp.Client);
        var said = rig.Menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(Pump(rig, () => said.Shown, ms: 9000));
        Assert.InRange(sw.ElapsedMilliseconds, 0, 7000);                   // the transport's five seconds, and a frame or two
        Assert.Equal(rig.Text["UI_NET_CONNECTIONERROR"], said.Texts["mbTitle"]);
        Assert.Equal($"No game answered at 127.0.0.1:{closed}.", said.Texts["mbTextBox"]);
        Assert.Null(rig.Mp.Client);
        Assert.Equal(1, rig.Mp.Mode);
        Assert.Equal($"127.0.0.1:{closed}", MpSetup.LastAddress(rig.Settings));
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Enter);
        Click(rig.Menus, MpMenus.ChatFile, "mpChatChangeRoomButton");
        Assert.Equal($"127.0.0.1:{closed}", box.Edits["ibEdit"].Text);    // the last one used
    }

    /// <summary>
    /// **The room**: a player knocks with no character and is listed as choosing one; Start greyed. A character with the
    /// host's name is refused in 1.1's words (`UI_NET_JOINDUPLICATE`); another is accepted, and everybody's list names it —
    /// Start lit. **Game Setup** changes the chronicle: every player is sent it, the room's game pane names it on both machines,
    /// and a player whose install lacks it says so and is not ready (Start greyed, the host's list marking it), until the host
    /// chooses one it has.
    /// </summary>
    [Fact]
    public void In_the_room_a_duplicate_is_refused_and_Game_Setup_changes_the_chronicle_for_everyone()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        rig.Mp.HostGame();
        Click(rig.Menus, MpMenus.ChatFile, "mpChatPlayerSetupButton");
        Click(rig.Menus, MpMenus.PlayerSelectFile, "mppsOkButton");
        var server = rig.Mp.Server!;
        Assert.Equal("Anna", server.Players.Single().Name);
        string first = server.Settings.Chronicle;
        var chat = rig.Menus.Pane(MpMenus.ChatFile);

        // Bob's machine: no character at the knock, and an install that lacks the Inn's chronicle.
        var answers = new List<string?>();
        using var bob = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port), null)
        {
            Find = rig.Text.Find, StartsItself = false, HasChronicle = name => !name.Equals("MP_DAInn", StringComparison.OrdinalIgnoreCase),
        };
        bob.Chosen += answers.Add;
        Assert.True(Pump(rig, () => bob.State == ClientState.Room && server.Players.Count() == 2, clients: bob));
        Assert.True(Pump(rig, () => Rows(rig, "mpChatRoomStatus").Contains($"{rig.Text["UI_NET_PLAYER"]} 2 : {MpSetup.Choosing}"), clients: bob));
        Assert.Equal(ButtonState.Greyed, chat.Buttons["mpChatStartButton"]);

        // A duplicate of the host's name: refused in the room, 1.1's words.
        bob.Choose(new NetCharacter { Name = "Anna", Template = "MP_Erik" });
        Assert.True(Pump(rig, () => answers.Count == 1, clients: bob));
        Assert.Equal("UI_NET_JOINDUPLICATE", answers[0]);
        Assert.False(bob.Accepted);
        Assert.Equal(ClientState.Room, bob.State);
        bob.Choose(new NetCharacter { Name = "Bob", Template = "MP_Erik" });
        Assert.True(Pump(rig, () => answers.Count == 2, clients: bob));
        Assert.Null(answers[1]);
        Assert.True(bob.Accepted);
        Assert.True(Pump(rig, () => bob.Players.Any(p => p.Name == "Bob") && chat.Buttons["mpChatStartButton"] == ButtonState.Normal, clients: bob));
        Assert.Contains($"{rig.Text["UI_NET_PLAYER"]} 2 : Bob", Rows(rig, "mpChatRoomStatus"));
        Assert.Contains(Rows(rig, "mpChatGame"), l => l.StartsWith("Bob ", StringComparison.Ordinal));   // "Bob has joined the game."

        // Game Setup: Midnight at the Inn chosen, OK.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatCreateGameButton");
        var setup = rig.Menus.Pane(MpMenus.CreateGameFile);
        Assert.True(rig.Menus.IsUp(setup));
        var list = setup.Lists["mpcgChronicleList"];
        int inn = list.Rows.ToList().FindIndex(r => r.Text == rig.Text["MP_DAInn"]);
        Assert.True(inn >= 0);
        list.Select(inn);
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.False(setup.Shown);
        Assert.Equal("MP_DAInn", server.Settings.Chronicle);
        Assert.True(Pump(rig, () => bob.Settings?.Chronicle == "MP_DAInn" && !server.ReadyToStart, clients: bob));
        Assert.False(bob.ChronicleHere);
        rig.Mp.Frame(0);
        Assert.Contains($"{rig.Text["UI_NET_CHRONICLE"]}: {rig.Text["MP_DAInn"]}", Rows(rig, "mpChatGameStatus"));
        Assert.Contains($"{rig.Text["UI_NET_PLAYER"]} 2 : Bob {MpSetup.NoChronicle}", Rows(rig, "mpChatRoomStatus"));
        Assert.Equal(ButtonState.Greyed, chat.Buttons["mpChatStartButton"]);
        Assert.Equal("MP_DAInn", MpSetup.Kept(rig.Settings, [.. ChronicleSet.Load(files).All], "x").Chronicle);   // kept for the next Host Game

        // Back to the first chronicle: Bob has it, and Start is lit again.
        Click(rig.Menus, MpMenus.ChatFile, "mpChatCreateGameButton");
        list.Select(list.Rows.ToList().FindIndex(r => r.Text == rig.Text[first]));
        Click(rig.Menus, MpMenus.CreateGameFile, "mpcgCreateGameButton");
        Assert.True(Pump(rig, () => bob.Settings?.Chronicle == first && server.ReadyToStart, clients: bob));
        Assert.True(bob.ChronicleHere);
        rig.Mp.Frame(0);
        Assert.Equal(ButtonState.Normal, chat.Buttons["mpChatStartButton"]);
    }
}
