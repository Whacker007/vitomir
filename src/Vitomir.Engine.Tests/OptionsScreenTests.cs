// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The Options screen and its sub-screens as they are in
/// 1.1: how each opens and closes, which box stands for which bit, the value bars'
/// arithmetic, where the settings are written — and Vitomir's own Fixes page, which must offer
/// every switch the registers have and write the sections they are read from. Driven the way the
/// mouse drives them, through the front end's own press and release.
/// </summary>
public class OptionsScreenTests
{
    sealed record Rig(FrontEnd Menus, OptionsMenu Options, List<int> Sounds, string Ini);

    static Rig Open(GameFiles files, GameOptions? options = null, int state = FrontEnd.StateFrontEnd)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var sounds = new List<int>();
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = state, Sound = sounds.Add };
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-options-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, options ?? new GameOptions()) { SavePath = ini, Choices = new MenuChoices() };
        screen.Fixes.SavePath = ini;
        menus.Options = screen;
        return new Rig(menus, screen, sounds, ini);
    }

    static (float X, float Y) Middle(MenuPane pane, string control)
    {
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);
        return (x + e.Size[0] / 2f, y + e.Size[1] / 2f);
    }

    static void Click(FrontEnd menus, MenuPane pane, string control)
    {
        var (x, y) = Middle(pane, control);
        menus.PointerAt(x, y);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    [Fact]
    public void The_main_menus_options_button_opens_the_options_screen_on_a_layer_of_its_own_and_done_closes_it()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Menus.OpenMainMenu();
        Click(rig.Menus, rig.Menus.Pane(FrontEnd.MainMenuFile), "mmOptionsButton");
        var options = rig.Menus.Pane(OptionsMenu.OptionsFile);
        Assert.True(rig.Menus.IsUp(options));
        Assert.False(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MainMenuFile)));   // covered by the pushed layer
        Click(rig.Menus, options, "opDoneButton");
        Assert.False(rig.Menus.IsUp(options));
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(FrontEnd.MainMenuFile)));
        File.Delete(rig.Ini);
    }

    [Fact]
    public void From_the_escape_menu_options_pauses_the_game_sets_state_four_and_escape_gives_both_back()
    {
        using var files = Install.Open();
        var rig = Open(files, state: FrontEnd.StateGame);
        rig.Menus.OpenEscape();
        rig.Menus.Drawn();
        Click(rig.Menus, rig.Menus.Pane(FrontEnd.EscapeFile), "emOptionsButton");
        Assert.True(rig.Options.IsOpen);
        Assert.True(rig.Menus.Paused);
        Assert.Equal(FrontEnd.StateOptions, rig.Menus.State);   // The game not drawn
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Escape);
        Assert.False(rig.Options.IsOpen);
        Assert.False(rig.Menus.Paused);
        Assert.Equal(FrontEnd.StateGame, rig.Menus.State);
        Assert.False(rig.Menus.Up);
    }

    [Fact]
    public void The_gameplay_boxes_are_no_blood_and_subtitles_and_ok_writes_them_back()
    {
        using var files = Install.Open();
        var o = new GameOptions();
        o.Set(GameOptions.ForceSubtitles, true);
        var rig = Open(files, o);
        rig.Options.Open();
        rig.Options.OpenGameplay();
        var pane = rig.Menus.Pane(OptionsMenu.GameplayFile);
        Assert.False(pane.Checks["opgpBloodOn"]);      // checked means NoBlood (the page reads bit 2)
        Assert.True(pane.Checks["opgpSubtitling"]);
        Click(rig.Menus, pane, "opgpBloodOn");          // a checkbox acts on the press
        Assert.True(pane.Checks["opgpBloodOn"]);
        Assert.False(o.Has(GameOptions.NoBlood));       // nothing kept until OK
        Click(rig.Menus, pane, "opgpOKButton");
        Assert.True(o.Has(GameOptions.NoBlood));
        Assert.False(rig.Menus.IsUp(pane));
        Assert.True(rig.Menus.IsUp(rig.Menus.Pane(OptionsMenu.OptionsFile)));
        var saved = IniFile.Read(rig.Ini);
        Assert.Equal("1", saved["options.NoBlood"]);
        Assert.Equal("1", saved["options.ForceSubtitles"]);
        File.Delete(rig.Ini);
    }

    [Fact]
    public void A_locked_build_hides_the_blood_box()
    {
        using var files = Install.Open();
        var o = new GameOptions();
        o.Set(GameOptions.NoBlood | GameOptions.BloodLocked, true);
        var rig = Open(files, o);
        rig.Options.Open();
        rig.Options.OpenGameplay();
        Assert.Contains("opgpBloodOn", rig.Menus.Pane(OptionsMenu.GameplayFile).Hidden);
    }

    [Fact]
    public void Cancel_on_the_controls_screen_keeps_nothing_and_ok_folds_the_ai_boxes_into_the_party_flags()
    {
        using var files = Install.Open();
        var o = new GameOptions { Ai = GameOptions.CastHealing | GameOptions.CastBuff | GameOptions.CastDamage | GameOptions.CastSpecial };
        var rig = Open(files, o);
        rig.Options.Open();
        rig.Options.OpenControls();
        var pane = rig.Menus.Pane(OptionsMenu.ControlsFile);
        Click(rig.Menus, pane, "opConQuickDisc");
        Click(rig.Menus, pane, "opConCancelButton");
        Assert.False(o.HasAi(GameOptions.UseQuickDisc));

        rig.Options.OpenControls();
        Assert.False(pane.Checks["opConQuickDisc"]);    // the show hook puts the boxes back from the word
        Click(rig.Menus, pane, "opConQuickDisc");
        Click(rig.Menus, pane, "opConHealing");         // untick: the party may no longer heal
        Click(rig.Menus, pane, "opConOKButton");
        Assert.True(o.HasAi(GameOptions.UseQuickDisc));
        uint flags = o.FoldInto(AiMainFlag.StopPeds);
        Assert.Equal(AiMainFlag.StopPeds | AiMainFlag.QuickDisciplinesOnly | AiMainFlag.NoPartyCastHeal, flags);
        File.Delete(rig.Ini);
    }

    /// <summary>
    /// **Ours:** the Display screen's device list is the four window modes. It opens
    /// on the one in use with that mode's video modes beside it; a press on another row lists that
    /// row's modes; OK makes the row and the mode picked the window's at once and asks for no
    /// restart; OK with nothing changed asks for nothing.
    /// </summary>
    [Fact]
    public void The_device_list_is_the_four_window_modes_each_with_its_own_video_modes()
    {
        using var files = Install.Open();
        var rig = Open(files);
        int mode = FullScreen.ModeFull;
        var size = new VideoMode(1600, 900, 32);
        var asked = new List<(int Row, VideoMode Mode)>();
        VideoMode[] window = [new(800, 600, 32), new(1280, 720, 32)], screen = [new(1280, 720, 32), new(1600, 900, 32)];
        rig.Options.Devices = () => FullScreen.WindowModes;
        rig.Options.CurrentDevice = () => mode;
        rig.Options.ModesFor = row => row switch
        {
            FullScreen.ModeFull => screen,
            FullScreen.ModeBorderless => [new(1600, 900, 32)],
            _ => window,
        };
        rig.Options.CurrentModeFor = row => row == mode ? size : row == FullScreen.ModeFull || row == FullScreen.ModeBorderless ? new(1600, 900, 32) : window[^1];
        rig.Options.SetDisplay = (row, m) => { asked.Add((row, m)); mode = row; size = m; return true; };
        rig.Options.Gamma = _ => { };
        rig.Options.Open();

        rig.Options.OpenDisplay();
        var pane = rig.Menus.Pane(OptionsMenu.DisplayFile);
        var devices = pane.Lists["opdisDeviceList"];
        var modes = pane.Lists["opdisVideoModeList"];
        Assert.Equal(4, devices.Rows.Count);
        Assert.Equal(FullScreen.ModeFull, devices.CurrentData);
        Assert.Equal(["1280 x 720 32 BPP", "1600 x 900 32 BPP"], modes.Rows.Select(r => r.Text));
        Assert.Equal(1, modes.CurrentData);

        // Another row picked: its own modes beside it, the one it would open at chosen.
        devices.Select(FullScreen.ModeWindow);
        rig.Options.Picked(pane, "opdisDeviceList");
        Assert.Equal(["800 x 600 32 BPP", "1280 x 720 32 BPP"], modes.Rows.Select(r => r.Text));
        Assert.Equal(1, modes.CurrentData);
        devices.Select(FullScreen.ModeBorderless);
        rig.Options.Picked(pane, "opdisDeviceList");
        Assert.Single(modes.Rows);
        devices.Select(FullScreen.ModeBorderlessWindow);
        rig.Options.Picked(pane, "opdisDeviceList");
        modes.Select(0);
        Click(rig.Menus, pane, "opdisOkButton");
        Assert.Equal([(FullScreen.ModeBorderlessWindow, new VideoMode(800, 600, 32))], asked);
        Assert.False(rig.Menus.IsUp(pane));                         // closed, no restart box

        // Opened again and OK with nothing changed: nothing asked.
        rig.Options.OpenDisplay();
        Assert.Equal(FullScreen.ModeBorderlessWindow, devices.CurrentData);
        Assert.Equal(0, modes.CurrentData);
        Click(rig.Menus, pane, "opdisOkButton");
        Assert.Single(asked);

        // Only the size changed: the same row, the new size.
        rig.Options.OpenDisplay();
        modes.Select(1);
        Click(rig.Menus, pane, "opdisOkButton");
        Assert.Equal((FullScreen.ModeBorderlessWindow, new VideoMode(1280, 720, 32)), asked[^1]);

        // The rules under it.
        Assert.Equal(FullScreen.ModeWindow, FullScreen.WindowModeOf(full: false, hasMode: true));
        Assert.Equal(FullScreen.ModeBorderlessWindow, FullScreen.WindowModeOf(full: false, hasMode: false, borderless: true));
        Assert.Equal(FullScreen.ModeBorderless, FullScreen.WindowModeOf(full: true, hasMode: false));
        Assert.Equal(FullScreen.ModeFull, FullScreen.WindowModeOf(full: true, hasMode: true));
        Assert.Equal(new FullScreenSetting(true, null), FullScreen.SettingFor(FullScreen.ModeBorderless, new ScreenMode(1280, 720)));
        Assert.Equal(new FullScreenSetting(true, new ScreenMode(1280, 720)), FullScreen.SettingFor(FullScreen.ModeFull, new ScreenMode(1280, 720)));
        Assert.Equal(new FullScreenSetting(true, FullScreen.DefaultMode), FullScreen.SettingFor(FullScreen.ModeFull, null));
        Assert.False(FullScreen.SettingFor(FullScreen.ModeWindow, null).On);
        Assert.False(FullScreen.SettingFor(FullScreen.ModeBorderlessWindow, null).On);
        // A 1600x900 laptop: a window with a title bar has 1584x829 to itself, one without the lot.
        Assert.Equal([new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1366, 768), new(1436, 808)],
                     FullScreen.SizesThatFit(new ScreenMode(1584, 829), new ScreenMode(1436, 808)));
        Assert.Equal(new ScreenMode(1600, 900), FullScreen.SizesThatFit(new ScreenMode(1600, 900), null)[^1]);
        Assert.DoesNotContain(new ScreenMode(1920, 1080), FullScreen.SizesThatFit(new ScreenMode(1600, 900), null));
        File.Delete(rig.Ini);
    }

    [Fact]
    public void The_gamma_bar_runs_from_five_to_twenty_tenths_and_cancel_puts_the_old_brightness_back()
    {
        using var files = Install.Open();
        var o = new GameOptions { Gamma = 1.0f };
        var rig = Open(files, o);
        var seen = new List<float>();
        rig.Options.Gamma = seen.Add;
        rig.Options.Open();
        rig.Options.OpenDisplay();
        var pane = rig.Menus.Pane(OptionsMenu.DisplayFile);
        var bar = pane.Bars["opdisGammaScroll"];
        Assert.Equal((5, 20, 10), (bar.Min, bar.Max, bar.Value));
        // Drag the slider to the right-hand end: 2.0, live.
        var slider = pane.Element("opdisGammaSlider")!;
        rig.Menus.PointerAt(bar.Element.Pos[0] + bar.SliderX + 2, slider.Pos[1] + 4);
        rig.Menus.Frame(0);
        rig.Menus.LeftHeld = true;
        rig.Menus.Press();
        rig.Menus.PointerAt(bar.Element.Pos[0] + bar.Element.Size[0] + 40, slider.Pos[1] + 4);
        rig.Menus.Frame(0);
        rig.Menus.LeftHeld = false;
        rig.Menus.Release();
        Assert.Equal(20, bar.Value);
        Assert.Equal(2.0f, seen[^1]);
        Click(rig.Menus, pane, "opDisCancelButton");
        Assert.Equal(1.0f, o.Gamma);
        Assert.Equal(1.0f, seen[^1]);
    }

    [Fact]
    public void A_quantized_bar_moves_one_stop_at_a_time_toward_the_pointer()
    {
        var bar = new MenuBar(
            new Vitomir.Formats.NuiElement("scrollbar", new Dictionary<string, string> { ["name"] = "b", ["pos"] = "0,0", ["size"] = "138,17", ["quanta"] = "9", ["vertical"] = "0" }, 0),
            new Vitomir.Formats.NuiElement("scrollslider", new Dictionary<string, string> { ["name"] = "s", ["pos"] = "0,0", ["size"] = "13,17" }, 0));
        bar.Set(9);
        Assert.Equal(125, bar.Travel);
        Assert.Equal(125, bar.SliderX);            // Travel / quanta × stop
        Assert.False(bar.Drag(0, 0));              // far to the left, over no neighbouring stop: nothing
        Assert.Equal(9, bar.Value);
        Assert.True(bar.Drag(115, 0));             // over stop 8's slot (111..124): one step
        Assert.Equal(8, bar.Value);
        Assert.False(bar.Drag(115, 0));            // still there: stop 8 is where it is
        Assert.True(bar.Drag(100, 0));             // over stop 7's (97..110)
        Assert.Equal(7, bar.Value);
    }

    [Fact]
    public void The_sound_bars_act_as_they_move_and_the_effects_bar_plays_a_sample()
    {
        using var files = Install.Open();
        var o = new GameOptions { MusicVolume = 0.75f, EffectsVolume = 0.75f };
        var rig = Open(files, o);
        var music = new List<float>();
        rig.Options.MusicVolume = music.Add;
        rig.Options.Open();
        rig.Options.OpenSound();
        var pane = rig.Menus.Pane(OptionsMenu.SoundFile);
        Assert.Equal(75, pane.Bars["opsoMusicScroll"].Value);
        Assert.True(pane.Checks["opso2speakers"]);
        rig.Options.Slid(pane, "opsoMusicScroll", 40);
        Assert.Equal(0.4f, music[^1], 3);
        rig.Sounds.Clear();
        rig.Options.Slid(pane, "opsoSoundScroll", 10);
        Assert.Equal(0.1f, o.EffectsVolume, 3);
        Assert.Equal([OptionsMenu.SampleSound], rig.Sounds);
        Click(rig.Menus, pane, "opsoHeadphones");    // a radio: the other three go out
        Assert.Equal([false, false, true, false], OptionsMenu.SpeakerBoxes.Select(b => pane.Checks[b]));
        Assert.Equal(2, o.SpeakerType);
        File.Delete(rig.Ini);
    }

    /// <summary>
    /// Every switch of the two registers a player sets is on the page, scrolled to where it is drawn; **the repairs are
    /// not** (<i>"let's just remove the 'always applied repairs' section. People can read about
    /// those in the notes, they can't be changed anyway"</i>).
    /// </summary>
    [Fact]
    public void The_fixes_page_offers_every_fix_and_quality_of_life_switch_and_no_repair()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.Fixes.Open();
        var pane = rig.Options.Fixes.Pane;
        foreach (var f in new Fixes().All) Assert.False(pane.Checks["fxfix_" + f.Name]);
        foreach (var q in new Qol().All)
        {
            Assert.Equal(q.On, pane.Checks["fxqol_" + q.Name]);                // on, but `winkey`
            rig.Options.Fixes.Reveal("fxqol_" + q.Name);                       // below the box until scrolled to
            Assert.Same(pane.Element("fxqol_" + q.Name), pane.ControlAt(Middle(pane, "fxqol_" + q.Name).X, Middle(pane, "fxqol_" + q.Name).Y));
        }
        foreach (var r in Repairs.All) Assert.Null(pane.Element("fxrep_" + r.Name));
    }

    [Fact]
    public void The_fixes_page_writes_the_fixes_and_qol_sections_the_ini_is_read_from()
    {
        using var files = Install.Open();
        var rig = Open(files);
        Fixes? gotFixes = null;
        Qol? gotQol = null;
        rig.Options.Fixes.Changed += (f, q) => (gotFixes, gotQol) = (f, q);
        rig.Options.Open();
        rig.Options.Fixes.Open();
        var pane = rig.Options.Fixes.Pane;
        // `matchedgait` refines `movespeed`: its box is locked until that one is ticked, so
        // a click on it first does nothing, and after it the two are written together.
        Click(rig.Menus, pane, "fxfix_matchedgait");
        Assert.False(pane.Checks["fxfix_matchedgait"]);
        Click(rig.Menus, pane, "fxfix_movespeed");
        Click(rig.Menus, pane, "fxfix_matchedgait");
        rig.Options.Fixes.Reveal("fxqol_driving");                              // below the box until scrolled to
        Click(rig.Menus, pane, "fxqol_driving");
        Click(rig.Menus, pane, "fxokbutton");
        Assert.True(gotFixes!.MatchedGait);
        Assert.True(gotFixes.MoveSpeed);
        Assert.False(gotQol!.Driving);
        // Read back through the registers' own parse, from exactly that file.
        var ini = IniFile.Read(rig.Ini);
        Assert.Equal("on", ini["fixes.matchedgait"]);
        Assert.Equal("on", ini["fixes.movespeed"]);
        Assert.Equal("off", ini["qol.driving"]);
        Assert.Equal("off", ini["fixes.gunreach"]);
        var read = Fixes.Parse([], Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings"), userPath: rig.Ini);
        Assert.True(read.MatchedGait && read.MoveSpeed);
        Assert.False(rig.Menus.IsUp(pane));
        File.Delete(rig.Ini);
    }

    [Fact]
    public void Writing_a_setting_changes_its_line_in_place_and_keeps_every_comment()
    {
        string before = "; mine\n[debug]\nlevel = Info\n\n[fixes]\n; the walk\nmatchedgait = off\n";
        string after = SettingsFile.Apply(before, "fixes", [new("matchedgait", "on"), new("movespeed", "on")]);
        Assert.Equal("; mine\n[debug]\nlevel = Info\n\n[fixes]\n; the walk\nmatchedgait = on\nmovespeed = on\n", after);
        string added = SettingsFile.Apply(after, "options", [new("gamma", "1.2")]);
        Assert.EndsWith("movespeed = on\n\n[options]\ngamma = 1.2\n", added);
    }

    [Fact]
    public void The_options_are_read_from_masquerade_ini_then_vitomir_ini_and_brightness_only_from_ours()
    {
        using var files = Install.Open();
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-opt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "masquerade.ini"),
            "[gameOptions]\nMousePitch=1\nCastBuff=1\n[mixer]\nmusicVolume=0000403F7F\n[render]\ngamma=3333B33F58\n");
        var o = GameOptions.Parse(dir, dir, Path.Combine(dir, "none.ini"));
        Assert.True(o.Has(GameOptions.MousePitch));
        Assert.True(o.HasAi(GameOptions.CastBuff));
        Assert.Equal(0.75f, o.MusicVolume);
        Assert.Equal(GameOptions.DefaultGamma, o.Gamma);   // 1.1's own 1.4 is not Vitomir's default
        File.WriteAllText(Path.Combine(dir, Diag.FileName), "[options]\nMousePitch = 0\ngamma = 1.3\n");
        o = GameOptions.Parse(dir, dir, Path.Combine(dir, "none.ini"));
        Assert.False(o.Has(GameOptions.MousePitch));
        Assert.Equal(1.3f, o.Gamma, 3);
        Directory.Delete(dir, true);
    }
}
