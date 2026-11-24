// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A click in the 16:9 stretch lands on the installed panes' controls**: on pictures wider than 16:9
/// — 2560x1080, 3440x1440 and a 7680x1440 desktop — at 16:9 itself, and on pictures narrower
/// than it, 1024x768 and 1280x800, where the sheet is shorter than the picture. A window pixel in the middle of a control
/// goes through <see cref="StatusBar.FromWindow(float, float, int, int, UiLayoutMode, SheetSeat)"/> to that control: each
/// discipline slot on the status bar, the pack's coin button and every cell of its grids, and a menu's OK button — in play
/// (the sheet at the picture's bottom) and, for the menu, in the front end too (the sheet centred). The layout and the seat
/// are named on each call; none is put in force.
/// </summary>
public class WideClickTests
{
    public static TheoryData<int, int> Pictures => new() { { 2560, 1080 }, { 3440, 1440 }, { 7680, 1440 }, { 1920, 1080 }, { 1024, 768 }, { 1280, 800 } };

    /// <summary>
    /// The window pixel at a point of the sheet (y up from its bottom), in the 16:9 stretch: the pixel whose corner is
    /// nearest the point, since a pixel is read by its top left corner (<see cref="StatusBar.FromWindow(float, float, int, int)"/>,
    /// as 1.1 reads it). On a 1024x768 picture a unit is 1.2 rows tall, and the pixel that merely holds a unit's middle
    /// can have its corner in the unit above.
    /// </summary>
    static (int X, int Y) Pixel(float x, float y, int w, int h, SheetSeat seat = SheetSeat.Bottom)
    {
        var (px, py) = UiLayout.ToPicture(x, y, UiLayout.On(w, h, UiLayoutMode.Wide, seat));
        return ((int)MathF.Round(px), (int)MathF.Round(py));
    }

    static (int X, int Y) Sheet((int X, int Y) pixel, int w, int h, SheetSeat seat = SheetSeat.Bottom) =>
        StatusBar.FromWindow(pixel.X, pixel.Y, w, h, UiLayoutMode.Wide, seat);

    [Theory]
    [MemberData(nameof(Pictures))]
    public void A_click_on_a_discipline_slot_is_that_slot(int w, int h)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        for (int slot = 0; slot < 6; slot++)
        {
            var c = bar[$"sbDiscipline{slot}"]!;
            var at = Sheet(Pixel(c.X + c.Width / 2f, c.Y + c.Height / 2f, w, h), w, h);
            Assert.Equal(slot, bar.PowerSlotAt(at.X, at.Y));
        }
        // Beside the sheet (beyond 16:9) the bottom rows are the world's: no control of the bar answers there.
        if (!UiLayout.UpTo16By9(w, h))
        {
            var left = StatusBar.FromWindow(5, h - 5, w, h, UiLayoutMode.Wide);
            var right = StatusBar.FromWindow(w - 5, h - 5, w, h, UiLayoutMode.Wide);
            Assert.False(bar.Covers(left.X, left.Y));
            Assert.False(bar.Covers(right.X, right.Y));
            Assert.Null(bar.At(left.X, left.Y));
        }
        // Above the sheet (narrower than 16:9, the sheet at the bottom) the rows are the world's: the pointer is off the
        // sheet there (480 and up), so neither the bar nor the pack answers, right down to the row above the sheet's top.
        if (UiLayout.Below16By9(w, h))
        {
            var map = UiLayout.On(w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
            var pack = new InventoryPane(new UiAssets(files).Pane(InventoryPane.PaneName));
            foreach (int row in new[] { 0, 5, (int)map.Y / 2, (int)MathF.Ceiling(map.Y) - 1 })
                foreach (int column in new[] { 5, w / 2, w - 5 })
                {
                    var above = StatusBar.FromWindow(column, row, w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
                    Assert.True(above.Y >= UiAssets.Height, $"{column},{row} is unit {above}");
                    Assert.False(bar.Covers(above.X, above.Y));
                    Assert.Null(bar.At(above.X, above.Y));
                    Assert.Equal(-1, bar.PowerSlotAt(above.X, above.Y));
                    Assert.Null(pack.At(above.X, above.Y));
                    Assert.Null(pack.ButtonAt(above.X, above.Y));
                }
            // And the world is drawn there: its rows reach down past the sheet's top to the bar.
            int rows = (int)MathF.Round(h * UiLayout.WorldShare(StatusBar.WorldShare, w, h, UiLayoutMode.Wide, SheetSeat.Bottom));
            Assert.True(Pick.OnWorld(map.Y / 2f, h, rows));
            Assert.True(rows > map.Y + 1f);
        }
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void A_click_on_the_pack_is_the_coin_button_or_the_cell_under_it(int w, int h)
    {
        using var files = Install.Open();
        var pack = new InventoryPane(new UiAssets(files).Pane(InventoryPane.PaneName));
        var coin = Sheet(Pixel(573f, 458f, w, h), w, h);                   // the coin button, 560,442 26x32
        Assert.Equal("invgoldbutton", pack.ButtonAt(coin.X, coin.Y));
        Assert.True(DropCash.At(pack, coin.X, coin.Y));
        int cells = 0;
        for (int y = 0; y < UiAssets.Height; y += 4)
            for (int x = 0; x < UiAssets.Width; x += 4)
            {
                if (pack.At(x, y) is not { } spot) continue;
                var at = Sheet(Pixel(x + 0.5f, y + 0.5f, w, h), w, h);
                Assert.Equal((x, y), at);
                var back = pack.At(at.X, at.Y);
                Assert.Equal((spot.Grid, spot.X, spot.Y), (back!.Value.Grid, back.Value.X, back.Value.Y));
                cells++;
            }
        Assert.True(cells > 500, $"{cells} points of the pack's grids");
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void A_click_on_a_menu_s_ok_button_presses_it(int w, int h)
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-wideclick-{Guid.NewGuid():N}.ini");
        var options = new GameOptions();
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = ini };
        menus.Options = screen;
        screen.Open();
        screen.OpenGameplay();
        var pane = menus.Pane(OptionsMenu.GameplayFile);
        pane.Check(AlwaysRunOption.Box, true);
        var ok = pane.Element("opgpOKButton")!;
        var (x, y) = pane.At(ok);
        // Over the world in play (the sheet at the bottom) and in the front end (centred): the same control either way.
        var at = Sheet(Pixel(x + ok.Size[0] / 2f, y + ok.Size[1] / 2f, w, h), w, h);
        Assert.Equal(at, Sheet(Pixel(x + ok.Size[0] / 2f, y + ok.Size[1] / 2f, w, h, SheetSeat.Centre), w, h, SheetSeat.Centre));
        if (UiLayout.Below16By9(w, h))
            Assert.NotEqual(Pixel(x, y, w, h), Pixel(x, y, w, h, SheetSeat.Centre));
        menus.PointerAt(at.X, at.Y);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        Assert.True(options.AlwaysRun);
        File.Delete(ini);
    }
}
