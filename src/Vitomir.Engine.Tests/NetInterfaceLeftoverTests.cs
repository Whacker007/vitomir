// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Multiplayer's interface and routing leftovers**: the lobby's
/// games list left unchosen until a row is clicked, a chosen game followed as the list changes, and the
/// list only shown in a game; the create screen's clan list only shown for a human;
/// the network bar's portrait and the Storyteller's object list describing
/// what is under the pointer; a player's pack moves taken in the pause and refused only for the possessed body
/// (…); and an emote's gesture played on each machine that shows the line, its word matched as `_strnicmp`
/// matches it. The rules are pinned without the install; the rest plays a real salon on this machine.
/// </summary>
public class NetInterfaceLeftoverTests
{
    static readonly IPEndPoint A = new(IPAddress.Loopback, 5001), B = new(IPAddress.Loopback, 5002), C = new(IPAddress.Loopback, 5003);
    static FoundGame G(string name, IPEndPoint at, byte players = 1) => new(new GameInfo(name, "", players, 4, false, at.Port), at);

    [Fact]
    public void Nothing_chosen_stays_unchosen_and_a_chosen_game_is_followed_or_handed_to_the_one_after_it()
    {
        FoundGame[] was = [G("Anna's", A), G("Bob's", B), G("Cecilie's", C)];
        // Nobody clicked: however the list changes, nothing is chosen (the chosen row stays −1).
        Assert.Equal(-1, LobbyGamesList.Keep([], [G("Anna's", A)], -1));
        Assert.Equal(-1, LobbyGamesList.Keep(was, [G("Anna's", A, 2), was[1], was[2]], -1));
        // Chosen: the same game, wherever it now stands (an update, or a new game sorted in before it).
        Assert.Equal(1, LobbyGamesList.Keep(was, [G("Anna's", A, 3), was[1], was[2]], 1));
        Assert.Equal(2, LobbyGamesList.Keep(was, [G("Aaron's", new IPEndPoint(IPAddress.Loopback, 5009)), was[0], was[1]], 1));
        // The chosen game gone: the one after it (the index chosen again); the last gone: nothing.
        Assert.Equal(1, LobbyGamesList.Keep(was, [was[0], was[2]], 1));
        Assert.Equal(-1, LobbyGamesList.Keep(was, [was[0], was[1]], 2));
        Assert.Equal(0, LobbyGamesList.Keep(was, [was[1], was[2]], 1));                    // one before it gone
        // Shown only in a game (2, 4: the game's chat), choosable in the rooms (1, 3).
        Assert.True(LobbyGamesList.InertIn(2) && LobbyGamesList.InertIn(4));
        Assert.False(LobbyGamesList.InertIn(1) || LobbyGamesList.InertIn(3));
    }

    [Fact]
    public void The_games_list_is_only_shown_in_a_game_and_choosable_again_in_the_room()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Mp.Shot("mpgameroom");                                                        // the host's own game, mode 4
        var list = rig.Menus.Pane(MpMenus.ChatFile).Lists["mpChatGame"];
        Assert.True(list.Inert);
        rig.Mp.Shot("mplobby");                                                           // back to the join screen
        Assert.False(list.Inert);
        Assert.Equal(1, list.Current);                                                    // the still's own choice
    }

    [Fact]
    public void A_game_found_is_not_chosen_until_its_row_is_clicked_and_then_the_game_pane_describes_it()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        using var host = new GameServer(GameSettings.For(inn, "Midnight at the Inn", GameFlags.AutoStoryteller),
                                        new NetPeer(0), new NetCharacter { Name = "Server" });
        rig.Mp.Port = host.Peer.Port;
        new Crypt(rig.Crypt).Save(new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        rig.Mp.Open();
        MpMenusRig.Click(rig.Menus, MpMenus.SelectFile, "mpSelectInternet");            // Join Game: the join screen
        var chat = rig.Menus.Pane(MpMenus.ChatFile);
        var games = chat.Lists["mpChatGame"];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000 && games.Rows.Count == 0) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Single(games.Rows);                                                        // the waits are hang guards, not speed tests
        for (int i = 0; i < 20; i++) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(-1, games.Current);                                                  // 1.1 leaves it unchosen
        Assert.Empty(chat.Lists["mpChatGameStatus"].Rows);                                // and the game pane empty
        // Join with nothing chosen does nothing (the chosen row's game).
        MpMenusRig.Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        for (int i = 0; i < 20; i++) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(1, rig.Mp.Mode);
        // The row clicked: chosen, and described.
        MpMenusRig.ClickRow(rig.Menus, MpMenus.ChatFile, "mpChatGame", 0);
        rig.Mp.Frame(0);
        Assert.Equal(0, games.Current);
        Assert.Contains(chat.Lists["mpChatGameStatus"].Rows, r => r.Text.Contains("Midnight at the Inn"));
        // Heard again, the game stays chosen.
        for (int i = 0; i < 40; i++) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(0, games.Current);
        // Joined: the list is the game's chat, only shown, and says so (the hosting line is 1.1's dead one).
        MpMenusRig.Click(rig.Menus, MpMenus.ChatFile, "mpChatJoinGameButton");
        while (sw.ElapsedMilliseconds < 30000 && rig.Mp.Mode != 2) { host.Poll((int)sw.ElapsedMilliseconds); rig.Mp.Frame(0); Thread.Sleep(5); }
        Assert.Equal(2, rig.Mp.Mode);
        Assert.True(games.Inert);
        Assert.Equal(new Strings(files)["UI_NET_YOUJOINEDGAME"] + " Midnight at the Inn", games.Rows[0].Text);
    }

    [Fact]
    public void A_human_leaves_the_clan_list_only_shown_and_a_vampire_makes_it_choosable_again()
    {
        using var files = Install.Open();
        using var rig = Open(files);
        rig.Mp.OpenPlayerCreate();
        var create = rig.Menus.Pane(MpMenus.PlayerCreateFile);
        var clans = create.Lists["mppcClanList"];
        Assert.False(clans.Inert);
        Assert.Equal(0, clans.Current);
        MpMenusRig.Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcHumanCheck");
        Assert.True(clans.Inert);
        Assert.Equal(-1, clans.Current);                                                  // its choice forgotten
        Assert.Equal(new Strings(files)["UI_GENERAL_NOTAPPLICABLE"], create.Texts["mppcClan"]);
        MpMenusRig.Click(rig.Menus, MpMenus.PlayerCreateFile, "mppcKindredCheck");
        Assert.False(clans.Inert);
        Assert.Equal(-1, clans.Current);                                                  // nothing chosen until clicked
        Assert.NotEqual(new Strings(files)["UI_GENERAL_NOTAPPLICABLE"], create.Texts["mppcClan"]);   // the clan it kept
    }

    [Fact]
    public void The_network_bars_portrait_names_a_steered_player_and_says_health_blood_and_frenzy()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        var place = Location.Load(files, "P1_INN4", sky: false, player: "MP_Serena");
        var serena = place.Steered!;
        Assert.Equal(ThingType.Player, serena.Type);
        var bar = new StatusBar(new UiAssets(files).Pane("statusBarMP"), place);
        var head = bar[NetRollovers.HeadHotspot]!;
        // Below the three bars that lie over its top (they are later in the pane, so they win the hit).
        int x = head.X + head.Width / 2, y = head.Y + head.Height - 10;
        Assert.Equal(NetRollovers.HeadHotspot, bar.At(x, y)?.Name, ignoreCase: true);
        var said = IconRollover.Under(null, bar, x, y, serena, text, null)!.Value;
        var row = IconRollover.PartyRow(serena, text)!.Value;
        Assert.Equal(row.Small.Select(l => l.Text), said.Small.Select(l => l.Text));       // mode 1: name, then the line
        Assert.Equal(2, said.Small.Count);
        Assert.Empty(said.Large);
        Assert.Equal(("head", serena), said.Key);
        // A player's character goes by its player's name in a network game (from the join).
        serena.Actor!.Name = "Anna";
        Assert.Equal("Anna", IconRollover.Under(null, bar, x, y, serena, text, null)!.Value.Small[0].Text);
        // Anybody else steered — a townsman the Storyteller has — has no box (the type 7 test).
        var townsman = new WorldThing { Index = -1, Guid = 9, Scene = new Formats.NsdThing(), Template = "t", DescriptionId = "",
                                         Type = ThingType.Actor, Actor = new Actor { Health = 50f } };
        Assert.Null(IconRollover.Under(null, bar, x, y, townsman, text, null));
    }

    [Fact]
    public void The_Storytellers_object_list_names_the_hovered_template_by_its_description_else_its_name()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        var templates = new TemplateSet(files);
        string? described = null, unknown = null, bare = null;
        foreach (var name in templates.Names.Order(StringComparer.Ordinal))
        {
            var tags = templates.Resolve(name);
            string id = tags.TryGetValue(Tags.DescriptionId, out var d) ? d.AsString() : "";
            if (id.Length == 0) bare ??= name;
            else if (text.Find(id) is { Length: > 0 }) described ??= name;
            else unknown ??= name;
        }
        Assert.NotNull(described);
        Assert.NotNull(bare);
        string key = templates.Resolve(described!)[Tags.DescriptionId].AsString();
        Assert.Equal(text.Find(key), NetRollovers.TemplateLine(described!, templates, text.Find));   // key 82
        Assert.Equal(bare, NetRollovers.TemplateLine(bare!, templates, text.Find));                  // none: the name
        if (unknown is not null)                                                                     // a key the table lacks: itself
            Assert.Equal(templates.Resolve(unknown)[Tags.DescriptionId].AsString(), NetRollovers.TemplateLine(unknown, templates, text.Find));

        // The list: a hovered row says its template in the small box alone; no row, the boxes down.
        var assets = new UiAssets(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var list = menus.Pane("stObjectPane").Lists["stopListBox"];
        string[] rows = [bare!, described!];
        for (int i = 0; i < rows.Length; i++) list.Add(rows[i], i);
        Assert.Null(NetRollovers.Object(list, rows, templates, text.Find));
        Assert.True(HoverRow(list, 1), "no point of the list hovers its second row");
        var said = NetRollovers.Object(list, rows, templates, text.Find)!.Value;
        Assert.Equal(text.Find(key), said.Small.Single().Text);
        Assert.Empty(said.Large);
        Assert.True(said.LargeNow);
    }

    [Fact]
    public void Only_a_use_escapes_the_possessed_bodys_refusal_and_the_pause_refuses_nothing()
    {
        var anna = new WorldThing { Index = -1, Guid = 1, Scene = new Formats.NsdThing(), Template = "a", DescriptionId = "", Type = ThingType.Player };
        var bob = new WorldThing { Index = -1, Guid = 2, Scene = new Formats.NsdThing(), Template = "b", DescriptionId = "", Type = ThingType.Player };
        foreach (var kind in new[] { PackKind.Click, PackKind.ClickIn, PackKind.QuickEquip, PackKind.PutBack })
        {
            Assert.True(PackRefusal.Refused(kind, anna, anna));
            Assert.False(PackRefusal.Refused(kind, anna, bob));
            Assert.False(PackRefusal.Refused(kind, anna, null));
        }
        Assert.False(PackRefusal.Refused(PackKind.Use, anna, anna));                      // the host's use tests nothing
    }

    [Fact]
    public void An_emotes_word_need_only_begin_the_line_case_aside()
    {
        static string? Words(string key) => key switch { "MP_EMOTE_BOW" => "bow", "MP_EMOTE_WAVE" => "wave", _ => null };
        Assert.Equal(0xbf, NetChat.EmoteMode("bow", Words));
        Assert.Equal(0xbf, NetChat.EmoteMode("BOW deeply", Words));                       // `_strnicmp` over "bow"
        Assert.Equal(0xbf, NetChat.EmoteMode("bowling", Words));
        Assert.Equal(0xc2, NetChat.EmoteMode("waves", Words));
        Assert.Equal(-1, NetChat.EmoteMode(" bow", Words));                               // no trimming
        Assert.Equal(-1, NetChat.EmoteMode("bo", Words));
        Assert.Equal(-1, NetChat.EmoteMode("grins", Words));
        Assert.False(ChatGesture.Play(null, ChatKind.Say, "bow", Words, null));           // only an emote is a gesture
        Assert.True(ChatGesture.Play(null, ChatKind.Emote, "bow", Words, null));          // a gesture, speaker or not: no lips
    }

    // ---- a real game ------------------------------------------------------------------------------

    [Fact]
    public void A_pack_move_is_taken_in_the_pause_and_refused_in_silence_while_the_Storyteller_has_the_body()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller, hostBody: false);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void All() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, All), "Anna never played");
        var log = new List<string>();
        game.Server.Log += line => { lock (log) log.Add(line); };
        int Moves() { lock (log) return log.Count(l => l.Contains("Anna's pack")); }
        var hers = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        var putBack = new PackGesture(anna.Client.Avatar!.Guid, PackKind.PutBack);

        game.Server.SetPaused(true);
        Assert.True(Until(() => anna.Client.Paused, All));
        anna.Client.Send(putBack);
        Assert.True(Until(() => Moves() == 1, All), "a pack move in the pause was refused");     // no pause test

        game.Server.SetPaused(false);
        game.Server.Possessed = hers;                                                            // the host's steered body
        for (int i = 0; i < 10; i++) All();
        anna.Client.Send(putBack);
        for (int i = 0; i < 60; i++) All();
        Assert.Equal(1, Moves());                                                                // refused
        anna.Client.Send(putBack with { Kind = PackKind.Use });
        Assert.True(Until(() => Moves() == 2, All), "a use was refused");                        // No test
    }

    [Fact]
    public void An_emotes_gesture_plays_on_each_machine_that_shows_the_line_and_never_rides_a_snapshot()
    {
        using var files = Install.Open();
        using var game = Host(files, GameFlags.AutoStoryteller, hostBody: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All));
        var annaOnHost = game.Server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.True(Until(() => bob.Client.Mirror!.Find(annaOnHost.Guid) is { InWorld: true }, All), "Bob never saw Anna");
        var annaOnBobs = bob.Client.Mirror!.Find(annaOnHost.Guid)!;
        string wave = new Strings(files)["MP_EMOTE_WAVE"];
        static bool Waving(WorldThing t) => t.Animation?.Action is { } a && a.Mode.Index == 0xc2 && ChatLips.Local(a);

        anna.Client.Say(":" + wave);
        // Each machine that shows the line plays it itself, marked its own (so no snapshot carries or stops it).
        // Each seen waving at some turn, not all three at once (under the simulator the line reaches each machine at its own time).
        bool onHost = false, onBobs = false, onHers = false;
        Assert.True(Until(() => (onHost |= Waving(annaOnHost)) & (onBobs |= Waving(annaOnBobs)) & (onHers |= Waving(anna.Client.Avatar!)), All),
                    "the wave was not played on every machine that showed the line");
        Assert.False(annaOnBobs.Actor!.Has2(Actor.Flag2.Speaking));                              // a gesture moves no lips
    }

    // ---- the rig ----------------------------------------------------------------------------------

    /// <summary>Hovers a row of <paramref name="list"/> by walking the pointer down its box (the list's own hover).</summary>
    static bool HoverRow(MenuList list, int row)
    {
        for (float y = 0; y <= list.Height; y += 1f)
        {
            list.HoverAt(y);
            if (list.Hovered == row) return true;
        }
        return false;
    }

    sealed record MpRig(FrontEnd Menus, MpMenus Mp, string Crypt) : IDisposable
    {
        public void Dispose()
        {
            Mp.Server?.Dispose();
            Mp.Client?.Dispose();
            if (Directory.Exists(Crypt)) Directory.Delete(Crypt, recursive: true);
        }
    }

    static MpRig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        return new MpRig(menus, new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false }, crypt);
    }

    static class MpMenusRig
    {
        public static void Click(FrontEnd menus, string file, string control)
        {
            var pane = menus.Pane(file);
            var e = pane.Element(control)!;
            At(menus, pane.X + e.Pos[0] + e.Size[0] / 2f, pane.Y + e.Pos[1] + e.Size[1] / 2f);
        }

        /// <summary>A click on a list's row: the pointer walked down the list until the row is hovered, then pressed.</summary>
        public static void ClickRow(FrontEnd menus, string file, string listName, int row)
        {
            var pane = menus.Pane(file);
            var e = pane.Element(listName)!;
            var list = pane.Lists[listName];
            for (float y = pane.Y + e.Pos[1]; y < pane.Y + e.Pos[1] + e.Size[1]; y += 1f)
            {
                menus.PointerAt(pane.X + e.Pos[0] + e.Size[0] / 2f, y);
                menus.Frame(menus.NowMs);
                if (list.Hovered == row) { At(menus, pane.X + e.Pos[0] + e.Size[0] / 2f, y); return; }
            }
            Assert.Fail($"no point of {listName} hovers row {row}");
        }

        static void At(FrontEnd menus, float x, float y)
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
    }

    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)                                            // a hang guard, not a speed test
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Simulation Sim;
        public int Ms;
        public void Step()
        {
            Sim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Simulation? Sim;
        public void Step()
        {
            Client.Poll(1f / 30);
            Sim?.Step(1f / 30);
        }
        public void Dispose() => Client.Dispose();
    }

    /// <summary>The Dark Ages Inn salon (`MP_DAInn`), hosted by Hank (`MP_Kazi`) in person or by a host with no body.</summary>
    static Game Host(GameFiles files, GameFlags flags, bool hostBody)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Hank's game", flags);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null,
                                  player: hostBody ? "MP_Kazi" : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0), hostBody ? new NetCharacter { Name = "Hank", Template = "MP_Kazi" } : null)
            { Qol = new Qol(), Fixes = new Fixes(), Find = new Strings(files).Find };
        server.Start();
        server.Enter(place, hostBody ? place.Player?.Thing : null);
        return new Game { Server = server, Sim = new Simulation(place) };
    }

    static Player Join(GameFiles files, Game game, string name, string template)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }) { Qol = new Qol() },
        };
        p.Client.Find = new Strings(files).Find;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            var place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
            p.Sim = new Simulation(place);
            p.Client.Enter(place, place.Player?.Thing);
        };
        return p;
    }
}
