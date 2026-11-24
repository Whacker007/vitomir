// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Our lettering on 1.1's screens** (<see cref="MenuLettering"/>): 1.1's option screens and
/// our two pages with every word drawn by the engine in our own lettering, 1.1's words taken off its art from the
/// player's own copy, 1.1's buttons keeping their places and clicks without their pictures — and the changes asked for:
/// a keybinds row on the Options screen, its rows evenly spaced, four bars on the Gameplay screen, no keys button on
/// the Controls screen, our pages' titles and buttons in our words.
/// </summary>
public class MenuLetteringTests
{
    sealed record Rig(FrontEnd Menus, OptionsMenu Options, UiAssets Assets);

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = FrontEnd.StateFrontEnd, Dress = (nui, file) => MenuLettering.Dress(assets, nui, file),
        };
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-letters-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, new GameOptions()) { SavePath = ini };
        screen.Fixes.SavePath = ini;
        screen.Keys.SavePath = ini;
        menus.Options = screen;
        return new Rig(menus, screen, assets);
    }

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

    static IEnumerable<NuiElement> Words(MenuPane pane) => pane.Nui.Elements.Where(e => e.Type == MenuLettering.WordsType);

    [Fact]
    public void The_options_screen_has_seven_rows_35_apart_with_keybinds_under_controls_and_fixes_above_done()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        var pane = rig.Menus.Pane(OptionsMenu.OptionsFile);
        Assert.True(pane.Lettered);
        Assert.Equal(["game", "controls", "keybinds", "display", "sound", "fixes", "done"],
                     Words(pane).Select(w => MenuLettering.Read(w).Text));
        var baselines = Words(pane).Select(w => MenuLettering.Read(w).Baseline).ToList();
        Assert.Equal(MenuLettering.OptionsFirst, baselines[0]);
        Assert.All(Enumerable.Range(1, baselines.Count - 1), i => Assert.Equal(MenuLettering.OptionsPitch, baselines[i - 1] - baselines[i]));
        // The window's lower crossbar (sheet 167) falls between fixes and done, through no word.
        var (low, high) = MenuLettering.Ink("fixes", MenuLettering.MenuEm);
        Assert.True(baselines[5] + low > 167f && baselines[6] + MenuLettering.Ink("done", MenuLettering.MenuEm).Top < 166f);
        // Every button is where its word is, without 1.1's pictures, and a word follows its button.
        foreach (var (word, button) in MenuLettering.OptionsRows)
        {
            var b = pane.Element(button)!;
            Assert.Null(b.Get("bitmap"));
            Assert.Null(b.Get("rollover"));
            var w = MenuLettering.Read(Words(pane).Single(e => e.Get("button") == button));
            Assert.Equal(word, w.Text);
            Assert.InRange(w.Baseline, b.Pos[1], b.Pos[1] + b.Size[1]);
            Assert.InRange(w.X, b.Pos[0], b.Pos[0] + b.Size[0]);
        }
        // Their art: 1.1's tiles with its words taken off, under names of their own.
        Assert.Contains(pane.Nui.Quads, q => q.Material.StartsWith("lettered/options/", StringComparison.Ordinal));
        Assert.All(pane.Nui.Quads.Where(q => q.Material.StartsWith("lettered/")), q => Assert.NotNull(rig.Assets.Texture(q.Material)));
        // The raw pane, the install's, is not touched (the front end dressed a copy).
        Assert.DoesNotContain(rig.Assets.Pane(OptionsMenu.OptionsFile).Elements, e => e.Type == MenuLettering.WordsType);
    }

    [Fact]
    public void Keybinds_opens_the_key_page_over_the_options_screen_and_fixes_the_fixes_page()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        var options = rig.Menus.Pane(OptionsMenu.OptionsFile);
        Click(rig.Menus, options, OptionsMenu.KeybindsButton);
        var keys = rig.Options.Keys.Pane;
        Assert.True(rig.Menus.IsUp(keys));
        Assert.True(keys.Lettered);
        rig.Menus.Key(MenuKey.Escape);
        Assert.True(rig.Menus.IsUp(options));
        Click(rig.Menus, options, OptionsMenu.FixesButton);
        Assert.True(rig.Menus.IsUp(rig.Options.Fixes.Pane));
        rig.Menus.Key(MenuKey.Escape);
        Click(rig.Menus, options, "opdonebutton");
        Assert.False(rig.Options.IsOpen);
    }

    /// <summary>
    /// 1.1's words are gone from its art: on the Options screen no bone-coloured letter is left in the column of words
    /// (1.1's at rest are about 210, 184, 163), where the install's own art has thousands.
    /// </summary>
    [Fact]
    public void The_options_screens_words_are_taken_off_its_art()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        int Bone(NuiPane nui, bool withButtons)
        {
            var art = LiftedArt.Of(assets, nui);
            if (withButtons)
                foreach (var e in nui.Elements.Where(e => e.Type == "button" && e.Get("bitmap") is not null))
                    art.Paste(assets.Texture(e.Get("bitmap")!)!, e.Pos[0], e.Pos[1], e.Size[0], e.Size[1]);
            int n = 0;
            for (int y = 100; y < 310; y++)
                for (int x = 230; x < 410; x++)
                {
                    int d = (y * art.Width + x) * 3;
                    if (art.Rgb[d] > 150 && art.Rgb[d + 1] > 130 && art.Rgb[d + 2] > 110) n++;
                }
            return n;
        }
        var raw = assets.Pane(OptionsMenu.OptionsFile);
        int before = Bone(raw, true);
        Assert.True(before > 500, $"1.1's own words are bone-coloured letters in that column: {before} found");
        var dressed = MenuLettering.Dress(assets, raw.Copy(), OptionsMenu.OptionsFile);
        int after = Bone(dressed, false);
        Assert.True(after < 20, $"{after} bone-coloured pixels left of {before}");
    }

    [Fact]
    public void The_gameplay_screen_has_four_bars_each_with_its_box_and_ok_takes_ours()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.OpenGameplay();
        var pane = rig.Menus.Pane(OptionsMenu.GameplayFile);
        Assert.True(pane.Lettered);
        int[] boxes = [.. MenuLettering.GameplayRows.Select(r => pane.Element(r.Box)!.Pos[1])];
        Assert.Equal([381, 356, 331, 306], boxes);                                         // 1.1's two and ours, 25 apart
        Assert.Equal(["game options", "no blood", "subtitling", "confirm quick load", "always run", "cancel", "ok"],
                     Words(pane).Select(w => MenuLettering.Read(w).Text));
        Assert.Null(pane.Element(AlwaysRunOption.Label));                                  // 1.1's lettering's text boxes are not added
        Assert.Null(pane.Element(QuickLoadConfirm.Label));
        Assert.Null(pane.Element("opgpokbutton")!.Get("rollover"));
        // Ticked and OK'd: the options take the boxes.
        Click(rig.Menus, pane, AlwaysRunOption.Box);
        Click(rig.Menus, pane, QuickLoadConfirm.Box);
        Click(rig.Menus, pane, "opgpokbutton");
        Assert.True(rig.Options.Options.AlwaysRun);
        Assert.True(rig.Options.Options.ConfirmQuickLoad);
    }

    [Fact]
    public void The_controls_screen_keeps_1_1_s_rows_in_our_words_and_has_no_keys_button()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.OpenControls();
        var pane = rig.Menus.Pane(OptionsMenu.ControlsFile);
        Assert.Null(pane.Element(OptionsMenu.KeysButton));                                 // the key page is on the Options screen
        var texts = Words(pane).Select(w => MenuLettering.Read(w).Text).ToList();
        Assert.Contains("control options", texts);
        Assert.Contains("coterie AI options", texts);
        Assert.Contains("auto discipline casting", texts);
        Assert.All(MenuLettering.ControlRows, r => Assert.Contains(r.Word, texts));
        // A row's word sits on its bar, its ink in the bar's middle (even space above and below it).
        foreach (var (word, box) in MenuLettering.ControlRows)
        {
            var w = MenuLettering.Read(Words(pane).Single(e => MenuLettering.Read(e).Text == word));
            var (low, high) = MenuLettering.Ink(word, w.Em);
            float centre = pane.Element(box)!.Pos[1] + MenuLettering.ControlBarCentre;
            Assert.Equal(centre, w.Baseline + (low + high) / 2f, 0.01f);
        }
    }

    [Fact]
    public void Our_pages_titles_and_buttons_are_our_words_and_the_key_pages_three_are_centred_evenly()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.Fixes.Open();
        var fixes = rig.Options.Fixes.Pane;
        Assert.Equal(["vitomir fixes", "cancel", "ok"], Words(fixes).Select(w => MenuLettering.Read(w).Text));
        Assert.DoesNotContain(fixes.Nui.Quads, q => q.Material.StartsWith("consoleline"));
        Assert.Null(fixes.Element("fxtitle"));
        rig.Options.Fixes.Cancel();
        rig.Options.Keys.Open();
        var keys = rig.Options.Keys.Pane;
        var row = Words(keys).Where(w => w.Get("button") is not null).Select(MenuLettering.Read).ToList();
        Assert.Equal(["defaults", "cancel", "ok"], row.Select(w => w.Text));
        float Left(MenuLettering.Placed w) => w.X - MenuLettering.Measure(w.Text, w.Em) / 2f;
        float Right(MenuLettering.Placed w) => w.X + MenuLettering.Measure(w.Text, w.Em) / 2f;
        Assert.Equal(MenuLettering.KeysGap, Left(row[1]) - Right(row[0]), 0.01f);
        Assert.Equal(MenuLettering.KeysGap, Left(row[2]) - Right(row[1]), 0.01f);
        Assert.Equal(320f, (Left(row[0]) + Right(row[2])) / 2f, 0.01f);
        Assert.Contains(KeysPage.DefaultsButton, keys.Worded);                                // drawn by its word, not a caption
    }

    [Fact]
    public void In_1_1_s_lettering_nothing_is_dressed()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var screen = new OptionsMenu(menus, text.Find, new GameOptions());
        menus.Options = screen;
        screen.Open();
        var pane = menus.Pane(OptionsMenu.OptionsFile);
        Assert.False(pane.Lettered);
        Assert.NotNull(pane.Element("opdonebutton")!.Get("bitmap"));
        Assert.Equal(OptionsMenu.FixesCaption, pane.Texts[OptionsMenu.FixesButton]);
        Assert.Equal("fixes", OptionsMenu.FixesCaption);
    }

    [Fact]
    public void A_line_is_cut_into_the_uncials_runs_and_the_capitals_and_set_in_a_box_by_its_ink()
    {
        Assert.Equal([("coterie ", false), ("AI", true), (" options", false)], MenuLettering.Runs("coterie AI options"));
        Assert.Equal(MenuLetteringMode.Ours, MenuLettering.Parse("ours"));
        Assert.Equal(MenuLetteringMode.Original, MenuLettering.Parse("1.1"));
        Assert.Null(MenuLettering.Parse("fancy"));
        Assert.Equal("1.1", MenuLettering.Word(MenuLetteringMode.Original));
        // "control options": its l and t over the x-height, its p under the line; centred by that ink, not its x-height.
        var (low, high) = MenuLettering.Ink("control options", MenuLettering.TitleEm);
        Assert.True(low < -1f && high > MenuLettering.TitleEm * 0.6f);
        float baseline = MenuLettering.Centred("control options", MenuLettering.TitleEm, MenuLettering.PlateCentre);
        Assert.Equal(MenuLettering.PlateCentre - baseline - low, baseline + high - MenuLettering.PlateCentre, 0.01f);
        // A capital's run is wider than nothing, and a line's width the sum of its runs.
        Assert.True(MenuLettering.Measure("AI", 13.5f) > 0f);
        Assert.Equal(MenuLettering.Measure("coterie ", 13.5f) + MenuLettering.Measure("AI", 13.5f) + MenuLettering.Measure(" options", 13.5f),
                     MenuLettering.Measure("coterie AI options", 13.5f), 0.01f);
    }
}
