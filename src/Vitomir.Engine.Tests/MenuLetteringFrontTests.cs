// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **1.1's front-end menus in our lettering**: the main menu, single player, the escape menus, load and
/// save, the message and input boxes, the credits, the multiplayer screens and the loading plaque, every word of 1.1's
/// taken off its art and written in ours where 1.1 had it, its buttons in their places.
/// </summary>
public class MenuLetteringFrontTests
{
    static FrontEnd Menus(UiAssets assets, Strings text) => new(n => assets.Pane(n), n => assets.Font(n), text.Find)
    {
        State = FrontEnd.StateFrontEnd, Dress = (nui, file) => MenuLettering.Dress(assets, nui, file),
    };

    static List<string> Texts(MenuPane pane) =>
        [.. pane.Nui.Elements.Where(e => e.Type == MenuLettering.WordsType).Select(e => MenuLettering.Read(e).Text).Where(t => t.Length > 0)];

    static void Click(FrontEnd menus, MenuPane pane, string control)
    {
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

    /// <summary>Each front-end pane, its words in ours (a two-line button's two lines), and its buttons that had 1.1's words.</summary>
    public static IEnumerable<object[]> Panes() =>
    [
        [FrontEnd.MainMenuFile, new[] { "single player", "multiplayer", "options", "credits", "quit" }],
        [FrontEnd.SpFile, new[] { "new game", "load game", "main menu" }],
        [FrontEnd.EscapeFile, new[] { "resume", "load", "save", "options", "quit" }],
        ["escapeMenuMP", new[] { "resume", "save", "revive", "options", "quit" }],
        [FrontEnd.LoadFile, new[] { "load game", "cancel", "load" }],
        [FrontEnd.SaveFile, new[] { "save game", "cancel", "save" }],
        [FrontEnd.MessageBoxFile, new[] { "ok", "cancel" }],
        [FrontEnd.InputBoxFile, new[] { "ok", "cancel" }],
        [MpMenus.SelectFile, new[] { "host game", "join game", "main menu" }],
        [MpMenus.CreateGameFile, new[] { "game setup", "game name", "chronicles", "cancel", "ok", "load", "saved" }],
        [MpMenus.PlayerCreateFile, new[] { "character creation", "kindred", "generation", "create", "cancel" }],
        [MpMenus.PlayerSelectFile, new[] { "character selection", "player", "experience", "create new", "ok" }],
        [MpMenus.ChatFile, new[] { "players in room", "available games", "by", "address", "back", "game", "setup", "start" }],
        [FrontEnd.LoadScreenFile, new[] { "loading" }],
    ];

    [Theory]
    [MemberData(nameof(Panes))]
    public void Each_front_end_pane_has_its_words_in_ours_and_its_buttons_keep_their_places_without_1_1_s_pictures(string file, string[] words)
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var raw = assets.Pane(file);
        var dressed = MenuLettering.Dress(assets, raw.Copy(), file);
        var pane = new MenuPane(dressed, file, n => assets.Font(n));
        Assert.True(pane.Lettered, file);
        var texts = Texts(pane);
        Assert.All(words, w => Assert.Contains(w, texts));
        foreach (var b in dressed.Elements.Where(e => e.Type == "button" && pane.Worded.Contains(e.Name)))
        {
            var was = raw.Elements.Single(e => e.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(was.Pos, b.Pos);                                          // the click lands where it did
            Assert.Equal(was.Size, b.Size);
            Assert.Null(b.Get("bitmap"));
            Assert.Null(b.Get("rollover"));
        }
        // Their art is 1.1's with its words taken off, under names of their own.
        Assert.Contains(dressed.Quads, q => q.Material.StartsWith("lettered/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_main_menu_s_words_are_taken_off_its_art_and_ours_set_where_1_1_s_were()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var raw = assets.Pane(FrontEnd.MainMenuFile);
        int Bone(NuiPane nui, bool withButtons)
        {
            var art = LiftedArt.Of(assets, nui);
            if (withButtons)
                foreach (var e in nui.Elements.Where(e => e.Type == "button" && e.Get("bitmap") is not null))
                    art.Paste(assets.Texture(e.Get("bitmap")!)!, e.Pos[0], e.Pos[1], e.Size[0], e.Size[1]);
            int n = 0;
            for (int y = 120; y < 320; y++)
                for (int x = 230; x < 410; x++)
                {
                    int d = (y * art.Width + x) * 3;
                    if (art.Rgb[d] > 150 && art.Rgb[d + 1] > 130 && art.Rgb[d + 2] > 110) n++;
                }
            return n;
        }
        int before = Bone(raw, true);
        Assert.True(before > 500, $"1.1's own words are bone-coloured letters: {before} found");
        var dressed = MenuLettering.Dress(assets, raw.Copy(), FrontEnd.MainMenuFile);
        int after = Bone(dressed, false);
        Assert.True(after < before / 20, $"{after} bone-coloured pixels left of {before}");
        // Each word's ink in the middle of 1.1's: options where 1.1's options was, its button's middle across.
        var options = MenuLettering.Read(dressed.Elements.Single(e => e.Get("button") == "mmoptionsbutton"));
        var (bottom, top) = MenuLettering.Ink("options", options.Em);
        Assert.InRange(options.Baseline + (bottom + top) / 2f, 256f, 260f);                // 1.1's ink: rows 211 to 232
        Assert.InRange(options.X, 318f, 322f);
    }

    [Fact]
    public void The_menus_still_work_with_our_words_on_them()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = Menus(assets, text);
        menus.OpenMainMenu();
        var main = menus.Pane(FrontEnd.MainMenuFile);
        Assert.True(main.Lettered);
        Click(menus, main, "mmsingleplayerbutton");
        var sp = menus.Pane(FrontEnd.SpFile);
        Assert.True(sp.Shown);
        Click(menus, sp, "spmainmenubutton");
        Assert.True(menus.Pane(FrontEnd.MainMenuFile).Shown);
        Assert.False(sp.Shown);
        // A message box's ok answers it.
        int answered = -1;
        menus.Message("Warning!", "The previous save will be overwritten.", true, true, a => answered = a);
        var box = menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(box.Lettered);
        Click(menus, box, "mbokbutton");
        Assert.NotEqual(-1, answered);
    }

    [Fact]
    public void The_credits_rise_in_our_faces_and_in_1_1_s_lettering_in_its_own()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        foreach (bool ours in new[] { true, false })
        {
            var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
            {
                State = FrontEnd.StateFrontEnd,
                Dress = ours ? (nui, file) => MenuLettering.Dress(assets, nui, file) : null,
                Credits = () => [new CreditLine("CREDITS_TITLE_1", "Vampire"), new CreditLine("CREDITS_1", "Somebody")],
            };
            menus.OpenCredits();
            for (int t = 0; t < 4000; t += 100) menus.Frame(t);
            Assert.NotEmpty(menus.Floating.Up);
            Assert.All(menus.Floating.Up, c => Assert.Equal(ours, c.Ours));
        }
    }

    [Fact]
    public void In_1_1_s_lettering_the_front_end_menus_are_1_1_s()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateFrontEnd };
        menus.OpenMainMenu();
        var main = menus.Pane(FrontEnd.MainMenuFile);
        Assert.False(main.Lettered);
        Assert.Equal("mmsingleplayer", main.Element("mmsingleplayerbutton")!.Get("bitmap"), ignoreCase: true);
    }
}
