// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Every setting in force on OK, and kept**.
/// The Sound screen's Escape writes the volumes as its Exit does, so
/// the next run does not read the old ones back; what the screens and pages choose is kept for the session
/// (<see cref="MenuChoices"/>) and laid over each new run's settings, the command line's included; and a
/// value bar's slider stays where it is let go, under the pointer, rather than jumping back to
/// its truncated value — the brightness bar's few pixels to the left.
/// </summary>
public class OptionsApplyTests
{
    sealed record Rig(FrontEnd Menus, OptionsMenu Options, MenuChoices Choices, string Ini);

    static Rig Open(GameFiles files, GameOptions options)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateFrontEnd, Sound = _ => { } };
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-apply-{Guid.NewGuid():N}.ini");
        var choices = new MenuChoices();
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = ini, Choices = choices };
        screen.Fixes.SavePath = ini;
        menus.Options = screen;
        return new Rig(menus, screen, choices, ini);
    }

    /// <summary>A press on the slider <paramref name="grab"/> pixels into it, a move of the pointer to <paramref name="toX"/>, and the release.</summary>
    static void DragSlider(FrontEnd menus, MenuPane pane, MenuBar bar, string slider, int grab, float toX)
    {
        float y = pane.Element(slider)!.Pos[1] + 4;
        menus.PointerAt(bar.Element.Pos[0] + bar.SliderX + grab, y);
        menus.Frame(0);
        menus.LeftHeld = true;
        menus.Press();
        menus.PointerAt(toX, y);
        menus.Frame(0);
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(0);
    }

    [Fact]
    public void The_brightness_slider_stays_under_the_pointer_where_it_is_let_go_and_the_value_is_1_1s_truncation()
    {
        using var files = Install.Open();
        var rig = Open(files, new GameOptions { Gamma = 1.0f });
        rig.Options.Gamma = _ => { };
        rig.Options.Open();
        rig.Options.OpenDisplay();
        var pane = rig.Menus.Pane(OptionsMenu.DisplayFile);
        var bar = pane.Bars["opdisGammaScroll"];
        Assert.Equal(125, bar.Travel);                         // 138 less the 13-pixel slider: 15 steps of 8.33

        // Caught 6 pixels into the slider and let go with the pointer 100 pixels along the bar: the
        // slider's left edge is 94 along, under the pointer as it was caught — not the 91 of its value.
        DragSlider(rig.Menus, pane, bar, "opdisGammaSlider", grab: 6, toX: bar.Element.Pos[0] + 100);
        Assert.Equal(94, bar.SliderX);
        Assert.Equal(5 + (int)(94f / 125 * 15), bar.Value);  // 16: truncated as the slider does
        Assert.Equal(1.6f, rig.Options.Options.Gamma, 3);

        // Caught again where it now stands, it moves with the pointer pixel for pixel.
        DragSlider(rig.Menus, pane, bar, "opdisGammaSlider", grab: 3, toX: bar.Element.Pos[0] + 94 + 3 + 17);
        Assert.Equal(111, bar.SliderX);

        // The screen opened again draws it from the value, as 1.1's does.
        rig.Options.Key(pane, MenuKey.Escape);
        rig.Options.OpenDisplay();
        Assert.Equal((int)((bar.Value - 5) / 15f * 125), bar.SliderX);
        File.Delete(rig.Ini);
    }

    [Fact]
    public void The_volume_sliders_stay_where_they_are_let_go_too()
    {
        using var files = Install.Open();
        var rig = Open(files, new GameOptions { MusicVolume = 0.75f, EffectsVolume = 0.75f });
        rig.Options.MusicVolume = _ => { };
        rig.Options.Open();
        rig.Options.OpenSound();
        var pane = rig.Menus.Pane(OptionsMenu.SoundFile);
        var music = pane.Bars["opsoMusicScroll"];
        DragSlider(rig.Menus, pane, music, "opsoMusicScrollSlider", grab: 5, toX: music.Element.Pos[0] + 55);
        Assert.Equal(50, music.SliderX);
        Assert.Equal((int)(50f / 125 * 100), music.Value);   // 40
        File.Delete(rig.Ini);
    }

    [Fact]
    public void Escape_on_the_sound_screen_writes_the_volumes_as_exit_does()
    {
        using var files = Install.Open();
        var o = new GameOptions { MusicVolume = 0.75f, EffectsVolume = 0.75f };
        var rig = Open(files, o);
        rig.Options.Open();
        rig.Options.OpenSound();
        var pane = rig.Menus.Pane(OptionsMenu.SoundFile);
        rig.Options.Slid(pane, "opsoMusicScroll", 40);
        rig.Options.Key(pane, MenuKey.Escape);
        Assert.False(rig.Menus.IsUp(pane));
        Assert.Contains("musicVolume = 0.4", File.ReadAllText(rig.Ini));
        Assert.Equal(0.4f, rig.Choices.Options!.MusicVolume, 3);
        File.Delete(rig.Ini);
    }

    [Fact]
    public void A_brightness_kept_with_ok_outranks_the_gamma_flag_for_the_rest_of_the_session()
    {
        var choices = new MenuChoices();
        Assert.Equal(1.4f, choices.Brightness(1.4f, 1.0f));      // --gamma, nothing chosen yet
        Assert.Equal(1.0f, choices.Brightness(null, 1.0f));      // the settings'
        choices.ChoseGamma(1.2f);
        Assert.Equal(1.2f, choices.Brightness(1.4f, 1.0f));
    }

    [Fact]
    public void The_fixes_pages_choice_is_laid_over_the_next_runs_command_line()
    {
        var choices = new MenuChoices();
        var fixes = new Fixes { MoveSpeed = false };
        var qol = new Qol { Driving = false, DropCells = true };
        choices.Chose(fixes, qol);

        // The next run, started with `--fix movespeed --no-qol dropcells` carried over from the first.
        var nextFixes = new Fixes { MoveSpeed = true };
        var nextQol = new Qol { DropCells = false };
        choices.Over(nextFixes, nextQol);
        Assert.False(nextFixes.MoveSpeed);
        Assert.False(nextQol.Driving);
        Assert.True(nextQol.DropCells);
    }

    [Fact]
    public void The_options_chosen_are_the_next_runs_but_its_own_devmode_and_the_keys_chosen_too()
    {
        var choices = new MenuChoices();
        var read = new GameOptions { Flags = GameOptions.DevMode, MusicVolume = 0.2f };
        Assert.Same(read, choices.Over(read));                   // nothing chosen: what was read
        var keysRead = new KeyBindings();
        Assert.Same(keysRead, choices.Over(keysRead));

        choices.Chose(new GameOptions { Flags = GameOptions.NoBlood, MusicVolume = 0.6f });
        var now = choices.Over(read);
        Assert.Equal(GameOptions.NoBlood | GameOptions.DevMode, now.Flags);
        Assert.Equal(0.6f, now.MusicVolume);
        var none = choices.Over(new GameOptions());
        Assert.Equal(GameOptions.NoBlood, none.Flags);           // a run without --devmode has none

        var keys = new KeyBindings();
        keys.Set(KeyBindings.All.First(i => i.Defaults.Length > 0).Command, []);
        choices.Chose(keys);
        Assert.False(choices.Over(keysRead).AreDefaults);
    }
}
