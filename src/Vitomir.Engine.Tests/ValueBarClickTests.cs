// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A click on a value bar's track** (read in a comparison with 1.1's helpers): the scroll bar's release
/// handler (the bar's click method) steps a quantized horizontal bar one stop toward the
/// pointer — up right of the slider, down left of it — and then tells the bar's owner,
/// which on the Advanced Display screen makes the level Custom. Once a click on the
/// track of the Advanced screen's bars did nothing in Vitomir; only the slider's drag moved them.
/// </summary>
public class ValueBarClickTests
{
    [Fact]
    public void A_click_right_of_the_slider_steps_a_quantized_bar_up_and_left_of_it_down()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var options = new GameOptions { DetailLevel = 0 };
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = Path.Combine(Path.GetTempPath(), $"vitomir-bar-{Guid.NewGuid():N}.ini") };
        menus.Options = screen;
        screen.Open();
        screen.OpenDisplay();
        screen.OpenAdvanced();
        var adv = menus.Pane(OptionsMenu.AdvancedFile);
        var bar = adv.Bars["opdaModelLODScroll"];
        Assert.Equal(1, bar.Value);                                       // very low
        menus.Drawn();

        void ClickTrack(float x)
        {
            menus.PointerAt(adv.X + bar.Element.Pos[0] + x, adv.Y + bar.Element.Pos[1] + bar.Element.Size[1] / 2f);
            menus.Frame(menus.NowMs);
            menus.LeftHeld = true; menus.Press(); menus.LeftHeld = false; menus.Release();
            menus.Frame(menus.NowMs);
        }

        ClickTrack(bar.Element.Size[0] - 2);                              // the track's right end
        Assert.Equal(2, bar.Value);
        Assert.Equal(text.Find("UI_DETAIL_CUSTOM") ?? "UI_DETAIL_CUSTOM", adv.Texts["opdaDetailString"]);
        ClickTrack(bar.Element.Size[0] - 2);
        Assert.Equal(3, bar.Value);
        ClickTrack(1);                                                    // left of the slider: down
        Assert.Equal(2, bar.Value);
        if (File.Exists(screen.SavePath)) File.Delete(screen.SavePath);
    }
}
