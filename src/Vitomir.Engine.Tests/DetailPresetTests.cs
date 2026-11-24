// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The renderer's detail presets (the renderer's table) and what the Advanced
/// screen shows of one: the four named ones as 1.1 sets them, the custom one out
/// of `masquerade.ini`'s `[renderCustom]`, the inverted `detailFlags` bits, the bar arithmetic — and the
/// screen filled from the level shown when it opens and again at each arrow, an arrow not counting as a
/// touch; and the touched flag's life (a comparison with 1.1): cleared by the Display
/// screen, kept by Cancel, put back by Escape.
/// </summary>
public class DetailPresetTests
{
    [Fact]
    public void Very_low_turns_most_things_off_through_inverted_bits_and_its_bars_are_the_constructors()
    {
        var c = DetailPresets.Controls(DetailPresets.Named[0]);        // detailFlags 0x39d
        Assert.Equal(1, c["opdaSLVertex"]);
        Assert.Equal(0, c["opdaSLLightmap"]);
        Assert.Equal(0, c["opdaLightblobs"]);
        Assert.Equal(0, c["opdaHWtrans"]);
        Assert.Equal(0, c["opdaReflections"]);
        Assert.Equal(1, c["opdaBilinear"]);
        Assert.Equal(0, c["opdaTrilinear"]);
        Assert.Equal(0, c["opdaStyledLights"]);
        Assert.Equal(0, c["opda3dHeads"]);
        Assert.Equal(1, c["opdaBit16"]);
        Assert.Equal((3, 0, 0, 0, 0, 3, 1, 2, 2),
                     (c["opdaMipSelectionScroll"], c["opdaNumDLScroll"], c["opdaNumShadowsScroll"], c["opdaShadDetScroll"],
                      c["opdaDecalsScroll"], c["opdaNumMipsScroll"], c["opdaModelLODScroll"], c["opdaHeadLODScroll"],
                      c["opdaParticleDetailScroll"]));
        var high = DetailPresets.Controls(DetailPresets.Named[3]);
        Assert.Equal((3, 1, 7, 10, 8, 10, 10), (high["opdaNumDLScroll"], high["opdaNumShadowsScroll"], high["opdaShadDetScroll"],
                      high["opdaDecalsScroll"], high["opdaModelLODScroll"], high["opdaHeadLODScroll"], high["opdaParticleDetailScroll"]));
        Assert.Equal(1, high["opda3dHeads"]);
    }

    [Fact]
    public void The_custom_preset_is_read_from_render_custom_over_a_copy_of_normal()
    {
        var ini = IniFile.Parse("[renderCustom]\ndetailFlags=32\nmodelLODIndex=9\nmaxMipSize=512\nmaxMips=6\n" +
                                "shadowDetailIndex=9\nmaxThingShadows=4\nmaxThingLights=8\nmaxDecals=450\n" +
                                "particleLOD=0000803FBF\nuiHeadLOD=0000803FBF\noptionFlags=2\n");
        var custom = DetailPresets.Custom(ini)!.Value;
        var c = DetailPresets.Controls(custom);
        Assert.Equal(1, c["opdaTrilinear"]);
        Assert.Equal(0, c["opdaBilinear"]);
        Assert.Equal(4, c["opdaMipSelectionScroll"]);
        Assert.Equal(5, c["opdaNumMipsScroll"]);
        Assert.Equal(7, c["opdaNumDLScroll"]);
        Assert.Equal(9, c["opdaDecalsScroll"]);
        Assert.Equal(1, c["opdaBit32"]);
        Assert.Null(DetailPresets.Custom(IniFile.Parse("[render]\ndetailFlags=0\n")));
        Assert.Equal(DetailPresets.Named[2], DetailPresets.Of(4));
    }

    [Fact]
    public void The_advanced_screen_opens_on_the_preset_shown_and_an_arrow_refills_it_without_making_it_custom()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var options = new GameOptions { DetailLevel = 0 };
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = Path.Combine(Path.GetTempPath(), $"vitomir-detail-{Guid.NewGuid():N}.ini") };
        menus.Options = screen;
        screen.Open();
        screen.OpenDisplay();
        screen.OpenAdvanced();
        var adv = menus.Pane(OptionsMenu.AdvancedFile);
        Assert.False(adv.Checks["opda3dHeads"]);                          // very low: 0x39d
        Assert.False(adv.Checks["opdaSLLightmap"]);
        Assert.Equal(1, adv.Bars["opdaModelLODScroll"].Value);
        var more = adv.Element("opdaDetailMore")!;
        menus.Drawn();
        menus.PointerAt(adv.X + more.Pos[0] + more.Size[0] / 2f, adv.Y + more.Pos[1] + more.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true; menus.Press(); menus.LeftHeld = false; menus.Release();
        menus.Frame(menus.NowMs);
        Assert.True(adv.Checks["opdaSLLightmap"]);                        // low: 0x210, lightmaps on and heads still off
        Assert.False(adv.Checks["opda3dHeads"]);
        Assert.Equal(3, adv.Bars["opdaModelLODScroll"].Value);
        Assert.Equal(text.Find("UI_DETAIL_LOW") ?? "UI_DETAIL_LOW", menus.Pane(OptionsMenu.DisplayFile).Texts["opdisDetailString"]);
        if (File.Exists(screen.SavePath)) File.Delete(screen.SavePath);
    }

    /// <summary>
    /// The touched flag is cleared when the Display screen opens, not the
    /// Advanced one, which only keeps what it was on the way in. So a touch that Cancel
    /// keeps still counts at a second visit: its OK reads every box and bar back
    /// — the Custom preset they were refilled from — though nothing was touched that time. Escape
    /// puts the flag back as it was on the way in, so after it the second visit's OK reads
    /// nothing.
    /// </summary>
    [Fact]
    public void A_touch_kept_by_Cancel_counts_at_the_next_visit_and_one_left_by_Escape_does_not()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var options = new GameOptions { DetailLevel = 0 };
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = Path.Combine(Path.GetTempPath(), $"vitomir-touch-{Guid.NewGuid():N}.ini") };
        menus.Options = screen;
        var adv = menus.Pane(OptionsMenu.AdvancedFile);
        void Touch() { adv.Check("opda3dHeads", true); screen.Checked(adv, "opda3dHeads"); }
        void Press(string button) => screen.Clicked(adv, adv.Element(button)!);
        screen.Open();

        // Touched, then Cancel: the touch is kept, and the untouched second visit's OK reads the bars.
        screen.OpenDisplay();
        screen.OpenAdvanced();
        Touch();
        Press("opdaCancelButton");
        Assert.Empty(options.Advanced);
        screen.OpenAdvanced();
        Press("opdaOkButton");
        Assert.NotEmpty(options.Advanced);
        Assert.Equal(text.Find("UI_DETAIL_CUSTOM") ?? "UI_DETAIL_CUSTOM", menus.Pane(OptionsMenu.DisplayFile).Texts["opdisDetailString"]);

        // Touched, then Escape: the flag is as it was on the way in (the Display screen cleared it).
        options.Advanced.Clear();
        screen.OpenDisplay();
        screen.OpenAdvanced();
        Touch();
        screen.Key(adv, MenuKey.Escape);
        screen.OpenAdvanced();
        Press("opdaOkButton");
        Assert.Empty(options.Advanced);
        if (File.Exists(screen.SavePath)) File.Delete(screen.SavePath);
    }

    /// <summary>
    /// The graphics settings are presentation, and
    /// Vitomir draws as 1.1 does with every one at its top — the maxed-out preset, every box on (the
    /// radio pairs on lightmaps, trilinear and 32 bits) and every bar at its last stop.
    /// </summary>
    [Fact]
    public void The_maxed_out_preset_is_every_box_on_and_every_bar_at_its_top()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var adv = menus.Pane(OptionsMenu.AdvancedFile);
        var c = DetailPresets.Controls(DetailPresets.Maxed);
        foreach (var box in new[] { "opdaSLLightmap", "opdaLightblobs", "opdaStyledLights", "opdaBit32", "opdaTrilinear",
                                    "opdaReflections", "opdaHWtrans", "opda3dHeads" })
            Assert.Equal(1, c[box]);
        foreach (var partner in new[] { "opdaSLVertex", "opdaBit16", "opdaBilinear" }) Assert.Equal(0, c[partner]);
        // Every bar at its top (the decals' 500 shows as 10, which the bar holds to its last stop, 9).
        foreach (var name in OptionsMenu.AdvancedBars)
            Assert.Equal(adv.Bars[name].Quanta, Math.Min(c[name], adv.Bars[name].Quanta));
        Assert.Equal((9, 8, 4, 9, 500, 0x200, 6, 1f, 1f),
                     (DetailPresets.Maxed.ModelLodIndex, DetailPresets.Maxed.MaxThingLights, DetailPresets.Maxed.MaxThingShadows,
                      DetailPresets.Maxed.ShadowDetailIndex, DetailPresets.Maxed.MaxDecals, DetailPresets.Maxed.MaxMipSize,
                      DetailPresets.Maxed.MaxMips, DetailPresets.Maxed.ParticleLod, DetailPresets.Maxed.HeadLod));
        Assert.True(DetailPresets.Maxed.Textures32);
    }

    /// <summary>
    /// Whatever the saved settings — very low, a `[renderCustom]` of nothing, the Advanced
    /// screen's own boxes and bars all at the bottom — what the renderer draws with is the maxed-out
    /// preset: the shadows, the lights, the decal table and the mirrors. The screens still read and
    /// keep the values (the tests above).
    /// </summary>
    [Fact]
    public void Whatever_the_saved_settings_say_the_renderer_draws_with_the_maxed_out_preset()
    {
        var was = GameOptions.Current;
        try
        {
            var dir = Directory.CreateTempSubdirectory("vitomir-ac1-").FullName;
            File.WriteAllText(Path.Combine(dir, "masquerade.ini"),
                "[render]\ncurDetailLevel=4\n[renderCustom]\ndetailFlags=925\nmodelLODIndex=0\nmaxThingLights=1\n" +
                "shadowDetailIndex=0\nshadowMode=0\nmaxThingShadows=0\nmaxDecals=0\nmaxMipSize=32\nmaxMips=1\n");
            File.WriteAllText(Path.Combine(dir, "vitomir.ini"),
                "[options]\nadv.opdaReflections = 0\nadv.opdaShadDetScroll = 0\nadv.opdaNumShadowsScroll = 0\n" +
                "adv.opdaDecalsScroll = 0\nadv.opdaNumDLScroll = 0\n");
            var o = GameOptions.Parse(dir, dir, Path.Combine(dir, "vitomir.ini"));
            Assert.Equal(4, o.DetailLevel);                                  // read and kept, as 1.1's screens would show them
            Assert.Equal(0, o.RenderCustom!.Value.MaxDecals);
            Assert.Equal(0, o.Advanced["opdaReflections"]);
            GameOptions.Current = o;

            Assert.Equal(new ShadowSettings(3, 1f, 4f, 6000f, 3000f, 4), ShadowConsole.Live());
            Assert.Equal(8, new Vitomir.Engine.Scene.ThingLighting().MaxLights);
            Assert.Equal(8, new Shadows().MaxLights);
            Assert.Equal(500, Vitomir.Engine.Explosions.Decals.Rendered);
            Assert.True(DetailPresets.Reflections);

            // And the same for each named level.
            for (int level = 0; level <= 4; level++)
            {
                GameOptions.Current = new GameOptions { DetailLevel = level };
                Assert.Equal(ShadowSettings.Rendered, ShadowConsole.Live());
                Assert.Equal(500, Vitomir.Engine.Explosions.Decals.Rendered);
            }
            Directory.Delete(dir, recursive: true);
        }
        finally { GameOptions.Current = was; }
    }
}
