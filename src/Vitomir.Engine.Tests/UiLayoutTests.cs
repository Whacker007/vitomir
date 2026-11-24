// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The interface's sheet on the picture (ours, `UiLayout`): 1.1's stretch, the fitted sheet and the 16:9
/// stretch (the default; in 16:9's proportions on every picture), the pointer's way back, and finer
/// art beside the original. None of these puts a layout or a seat in force — both are statics every pane reads, and
/// other tests run beside these — so each names the layout it is about, and the seat where a sheet is shorter than
/// its picture.
/// </summary>
public class UiLayoutTests
{
    [Fact]
    public void Stretched_is_each_axis_by_itself_and_the_whole_picture()
    {
        var map = UiLayout.On(1280, 720, UiLayoutMode.Stretch);
        Assert.Equal(new UiLayout.Map(2f, 1.5f, 0f, 0f), map);
        Assert.Equal(720f, map.Bottom);
    }

    [Theory]
    [InlineData(1280, 720, 1.5f, 160f, 0f)]        // 16:9: the sheet in the middle, the world either side
    [InlineData(2560, 1440, 3f, 320f, 0f)]         // the big PC's picture
    [InlineData(2560, 1080, 2.25f, 560f, 0f)]      // 21:9
    [InlineData(640, 960, 1f, 0f, 240f)]           // taller than the sheet: room above and below
    [InlineData(1024, 768, 1.6f, 0f, 0f)]          // 4:3: the picture, as stretched
    public void Fitted_is_one_scale_the_largest_that_fits_and_centred(int w, int h, float scale, float x, float y)
    {
        var map = UiLayout.On(w, h, UiLayoutMode.Fit);
        Assert.Equal(scale, map.ScaleX, 4);
        Assert.Equal(scale, map.ScaleY, 4);
        Assert.Equal(x, map.X, 3);
        Assert.Equal(y, map.Y, 3);
        Assert.Equal(h - y, map.Bottom, 3);
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(800, 600)]
    [InlineData(1024, 768)]
    [InlineData(1600, 1200)]
    public void On_a_picture_of_the_sheets_own_shape_the_two_are_the_same(int w, int h)
    {
        var fit = UiLayout.On(w, h, UiLayoutMode.Fit);
        var stretch = UiLayout.On(w, h, UiLayoutMode.Stretch);
        Assert.Equal(stretch.ScaleX, fit.ScaleX, 5);
        Assert.Equal(stretch.ScaleY, fit.ScaleY, 5);
        Assert.Equal(0f, fit.X, 3);
        Assert.Equal(0f, fit.Y, 3);
    }

    [Fact]
    public void The_16_by_9_stretch_is_the_default()
    {
        // It follows the fitted sheet. The program alone changes it
        // (`Configure`); a recording puts its own in force, so the default is read where both take it from.
        Assert.Equal(UiLayoutMode.Wide, UiLayout.Default);
        Assert.Equal(UiLayout.Default, UiLayout.Asked(null, null, Path.Combine(Path.GetTempPath(), "vitomir-none-" + Guid.NewGuid().ToString("N") + ".ini")));
    }

    [Fact]
    public void A_pixel_and_its_point_of_the_sheet_go_both_ways()
    {
        const UiLayoutMode fit = UiLayoutMode.Fit;
        // 1280x720 fitted: 1.5 pixels a unit, the sheet from column 160 to 1120.
        var (x, y) = UiLayout.FromPicture(160f, 720f, 1280, 720, fit);
        Assert.Equal(0f, x, 3);
        Assert.Equal(0f, y, 3);
        (x, y) = UiLayout.FromPicture(1120f, 0f, 1280, 720, fit);
        Assert.Equal(640f, x, 3);
        Assert.Equal(480f, y, 3);
        (x, y) = UiLayout.FromPicture(0f, 360f, 1280, 720, fit);                    // beside the sheet: left of it
        Assert.True(x < 0f);
        var (px, py) = UiLayout.ToPicture(320f, 240f, 1280, 720, fit);
        Assert.Equal(640f, px, 3);
        Assert.Equal(360f, py, 3);
    }

    [Fact]
    public void The_pointer_reads_the_fitted_sheet_and_is_off_it_beside_it()
    {
        const UiLayoutMode fit = UiLayoutMode.Fit;
        // The sheet's corners: its bottom left unit is (0, 0), its top right (639, 479).
        Assert.Equal((0, 0), StatusBar.FromWindow(160, 719, 1280, 720, fit));
        Assert.Equal((639, 479), StatusBar.FromWindow(1119, 0, 1280, 720, fit));
        Assert.Equal((320, 239), StatusBar.FromWindow(640, 360, 1280, 720, fit));
        // Beside the sheet a pointer is off it, on the side it is on — so no pane answers for it and the world does.
        Assert.True(StatusBar.FromWindow(159, 700, 1280, 720, fit).X < 0);
        Assert.True(StatusBar.FromWindow(1120, 700, 1280, 720, fit).X >= UiAssets.Width);
        // A unit is a pixel and a half wide here: the pixels 160 and 161 are unit 0's, 162 is unit 1's.
        Assert.Equal(0, StatusBar.FromWindow(161, 400, 1280, 720, fit).X);
        Assert.Equal(1, StatusBar.FromWindow(162, 400, 1280, 720, fit).X);
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(800, 600)]
    [InlineData(1024, 768)]
    public void On_the_sheets_own_shape_the_pointer_is_where_1_1_has_it(int w, int h)
    {
        // 1.1's conversion, to the letter (StatusBar.FromWindow's stretch branch): the fitted one may not
        // move a click by a unit on the pictures 1.1 was made for, nor may the 16:9 stretch as before recorded
        // it there (a tape of theirs; the wide sheet is shorter than a 4:3 picture, below).
        for (int py = 0; py < h; py += 7)
            for (int px = 0; px < w; px += 5)
            {
                var old = ((int)(px * (float)UiAssets.Width / w), (UiAssets.Height - 1) - (int)(py * (float)UiAssets.Height / h));
                Assert.Equal(old, StatusBar.FromWindow(px, py, w, h, UiLayoutMode.Fit));
                Assert.Equal(old, StatusBar.FromWindow(px, py, w, h, UiLayoutMode.WideOneOneBelow16By9));
                Assert.Equal(old, StatusBar.FromWindow(px, py, w, h, UiLayoutMode.Stretch));
            }
    }

    [Fact]
    public void The_sheet_lies_over_the_world_only_fitted_on_a_wider_picture()
    {
        const UiLayoutMode fit = UiLayoutMode.Fit;
        Assert.True(UiLayout.Overlays(1280, 720, fit));
        Assert.True(UiLayout.Overlays(2560, 1440, fit));
        Assert.False(UiLayout.Overlays(1024, 768, fit));      // the bar reaches the sides
        Assert.False(UiLayout.Overlays(640, 960, fit));       // room above and below, none beside
        Assert.False(UiLayout.Overlays(2560, 1080, UiLayoutMode.Stretch));
        Assert.False(UiLayout.Overlays(7680, 1440, UiLayoutMode.Stretch));
    }

    // ---- The 16:9 stretch ----

    public static TheoryData<int, int> At16By9 => new()
    {
        { 1280, 720 },         // 16:9, exactly
        { 1600, 900 },         // the laptop
        { 1920, 1080 },
        { 2560, 1440 },        // the big PC's window
        { 3840, 2160 },
        { 1366, 768 },         // sold as 16:9, a third of a pixel wider either side: less than a pixel, so stretched
    };

    public static TheoryData<int, int> Below16By9 => new()
    {
        { 640, 480 },
        { 1024, 768 },         // 4:3
        { 1280, 1024 },        // 5:4, narrower than 4:3
        { 1280, 800 },         // 16:10
        { 1440, 900 },
        { 1680, 1050 },
        { 1920, 1200 },
        { 1360, 768 },         // sold as 16:9, three rows taller than it
    };

    static void IsTheStretchExactly(int w, int h, UiLayoutMode mode)
    {
        Assert.True(UiLayout.UpTo16By9(w, h));
        // The very same floats as 1.1's stretch, so nothing drawn or clicked moves by a bit, wherever the sheet is seated.
        foreach (var seat in Enum.GetValues<SheetSeat>())
            Assert.Equal(UiLayout.On(w, h, UiLayoutMode.Stretch), UiLayout.On(w, h, mode, seat));
        Assert.False(UiLayout.Overlays(w, h, mode));
        Assert.True(UiLayout.Stretched(w, h, mode));
        Assert.Equal(StatusBar.WorldShare, UiLayout.WorldShare(StatusBar.WorldShare, w, h, mode, SheetSeat.Bottom));
        for (int py = 0; py < h; py += 13)
            for (int px = 0; px < w; px += 11)
            {
                Assert.Equal(StatusBar.FromWindow(px, py, w, h, UiLayoutMode.Stretch), StatusBar.FromWindow(px, py, w, h, mode));
                Assert.Equal(StatusBar.FromWindow(px, py, w, h, UiLayoutMode.Stretch), StatusBar.FromWindow(px, py, w, h, mode, SheetSeat.Centre));
                Assert.Equal(UiLayout.FromPicture(px, py, w, h, UiLayoutMode.Stretch), UiLayout.FromPicture(px, py, w, h, mode));
            }
    }

    [Theory]
    [MemberData(nameof(At16By9))]
    public void At_16_by_9_it_is_the_stretch_exactly(int w, int h)
    {
        Assert.False(UiLayout.Below16By9(w, h));
        IsTheStretchExactly(w, h, UiLayoutMode.Wide);
        // And what an earlier build drew there, to the float: a tape of the earlier builds' and one of ours agree on a 16:9 picture.
        Assert.Equal(UiLayout.On(w, h, UiLayoutMode.WideOneOneBelow16By9), UiLayout.On(w, h, UiLayoutMode.Wide));
    }

    [Theory]
    [MemberData(nameof(At16By9))]
    [MemberData(nameof(Below16By9))]
    public void As_recorded_before_the_16_by_9_sheet_everything_up_to_16_by_9_is_the_stretch_exactly(int w, int h) =>
        IsTheStretchExactly(w, h, UiLayoutMode.WideOneOneBelow16By9);

    // ---- 16:9's proportions on every picture ----

    [Theory]
    [MemberData(nameof(Below16By9))]
    public void Narrower_than_16_by_9_the_sheet_is_the_pictures_width_and_16_by_9s_height(int w, int h)
    {
        const UiLayoutMode wide = UiLayoutMode.Wide;
        Assert.True(UiLayout.Below16By9(w, h));
        Assert.True(UiLayout.UpTo16By9(w, h));
        float tall = w * 9f / 16f;
        var bottom = UiLayout.On(w, h, wide, SheetSeat.Bottom);
        var centre = UiLayout.On(w, h, wide, SheetSeat.Centre);
        Assert.Equal(bottom, UiLayout.On(w, h, wide));                       // a layout named with no seat: in play
        foreach (var map in new[] { bottom, centre })
        {
            Assert.Equal(w / 640f, map.ScaleX);
            Assert.Equal(tall / 480f, map.ScaleY, 5);
            Assert.Equal(0f, map.X);
            // A sheet unit is 4 wide by 3 tall on the picture's pixels, exactly: 1.1's ovals come out round, as at 16:9.
            Assert.Equal(map.ScaleX * 0.75f, map.ScaleY);
            Assert.Equal(tall, map.Bottom - map.Y, 3);
        }
        // In play the sheet stands on the picture's bottom edge, all the room above it.
        Assert.Equal(h - tall, bottom.Y, 3);
        Assert.Equal(h, bottom.Bottom, 3);
        // Where the sheet is the whole screen it is centred: as much above as below.
        Assert.Equal((h - tall) / 2f, centre.Y, 3);
        Assert.Equal(h - centre.Bottom, centre.Y, 3);
        // It reaches both sides, so the bar's rows are the bar's (no world beside it), and it is not 1.1's stretch.
        Assert.False(UiLayout.Overlays(w, h, wide));
        Assert.False(UiLayout.Stretched(w, h, wide));
        Assert.NotEqual(UiLayout.On(w, h, UiLayoutMode.Stretch), bottom);
        // Fitted and stretched there are what they were.
        Assert.Equal(new UiLayout.Map(w / 640f, h / 480f, 0f, 0f), UiLayout.On(w, h, UiLayoutMode.Stretch, SheetSeat.Centre));
        Assert.Equal(UiLayout.On(w, h, UiLayoutMode.Fit), UiLayout.On(w, h, UiLayoutMode.Fit, SheetSeat.Centre));
    }

    [Fact]
    public void On_a_4_by_3_picture_the_sheet_is_576_rows_of_768()
    {
        Assert.Equal(new UiLayout.Map(1.6f, 1.2f, 0f, 192f), UiLayout.On(1024, 768, UiLayoutMode.Wide, SheetSeat.Bottom));
        Assert.Equal(new UiLayout.Map(1.6f, 1.2f, 0f, 96f), UiLayout.On(1024, 768, UiLayoutMode.Wide, SheetSeat.Centre));
        Assert.Equal(new UiLayout.Map(2f, 1.5f, 0f, 304f), UiLayout.On(1280, 1024, UiLayoutMode.Wide, SheetSeat.Bottom));
        Assert.Equal(new UiLayout.Map(2f, 1.5f, 0f, 80f), UiLayout.On(1280, 800, UiLayoutMode.Wide, SheetSeat.Bottom));
        Assert.Equal(new UiLayout.Map(2.625f, 1.96875f, 0f, 52.5f), UiLayout.On(1680, 1050, UiLayoutMode.Wide, SheetSeat.Centre));
        Assert.Equal(new UiLayout.Map(3f, 2.25f, 0f, 120f), UiLayout.On(1920, 1200, UiLayoutMode.Wide, SheetSeat.Bottom));
    }

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1081, true)]         // one row taller than 16:9: a sheet 1080 rows tall
    [InlineData(1366, 768, false)]         // a third of a pixel wider than 16:9 either side: 16:9 to the pixel
    [InlineData(1360, 768, true)]          // three rows taller
    [InlineData(1921, 1080, false)]        // wider
    [InlineData(2560, 1080, false)]
    [InlineData(1024, 768, true)]
    public void Narrower_than_16_by_9_is_by_a_whole_row(int w, int h, bool below)
    {
        Assert.Equal(below, UiLayout.Below16By9(w, h));
        // Never both narrower and wider: 16:9 to the pixel is neither.
        Assert.False(UiLayout.Below16By9(w, h) && !UiLayout.UpTo16By9(w, h));
    }

    [Theory]
    [MemberData(nameof(Below16By9))]
    public void Narrower_than_16_by_9_the_pointer_goes_both_ways_and_is_off_the_sheet_above_it(int w, int h)
    {
        const UiLayoutMode wide = UiLayoutMode.Wide;
        foreach (var seat in Enum.GetValues<SheetSeat>())
        {
            var map = UiLayout.On(w, h, wide, seat);
            int top = (int)MathF.Ceiling(map.Y), bottom = (int)MathF.Ceiling(map.Bottom);
            // The sheet's corners: its bottom left unit (0, 0), its top right (639, 479) — or the unit a pixel row
            // reaches where a row is more than a unit tall (640x480: three rows to four units).
            int skipped = (int)(1f / map.ScaleY);
            var low = StatusBar.FromWindow(0, bottom - 1, w, h, wide, seat);
            var high = StatusBar.FromWindow(w - 1, top, w, h, wide, seat);
            Assert.Equal((0, 639), (low.X, high.X));
            Assert.InRange(low.Y, 0, skipped);
            Assert.InRange(high.Y, 479 - skipped, 479);
            // Above the sheet the pointer is off it (480 and up): in play no pane answers there and the world does.
            Assert.True(StatusBar.FromWindow(w / 2, top - 1, w, h, wide, seat).Y >= UiAssets.Height);
            Assert.True(StatusBar.FromWindow(w / 2, 0, w, h, wide, seat).Y >= UiAssets.Height);
            // Below a centred one it is off it too, the other way; one at the bottom ends with the picture.
            if (seat == SheetSeat.Centre) Assert.True(StatusBar.FromWindow(w / 2, h - 1, w, h, wide, seat).Y < 0);
            else Assert.InRange(StatusBar.FromWindow(w / 2, h - 1, w, h, wide, seat).Y, 0, skipped);
            // Every sheet point to the picture and back is itself, and every pixel's unit holds the pixel.
            for (float x = 0f; x <= 640f; x += 12.5f)
                for (float y = 0f; y <= 480f; y += 7.5f)
                {
                    var (px, py) = UiLayout.ToPicture(x, y, map);
                    var (bx, by) = UiLayout.FromPicture(px, py, map);
                    Assert.Equal(x, bx, 3);
                    Assert.Equal(y, by, 3);
                    Assert.InRange(py, map.Y - 0.001f, map.Bottom + 0.001f);
                }
            for (int py = top; py < bottom; py += 17)
                for (int px = 0; px < w; px += 23)
                {
                    var (ux, uy) = StatusBar.FromWindow(px, py, w, h, wide, seat);
                    Assert.InRange(ux, 0, 639);
                    Assert.InRange(uy, 0, 479);
                    // The pixel lies in its unit: across from the unit's left edge, down from its top edge.
                    Assert.InRange(px - ux * map.ScaleX, -0.001f, map.ScaleX);
                    Assert.InRange(py - (map.Y + (479 - uy) * map.ScaleY), -0.001f, map.ScaleY);
                }
        }
    }

    [Theory]
    [InlineData(1024, 768, 629)]           // 192 rows above a sheet of 576, and 364/480 of that: 628.8 (1.1's stretch: 582)
    [InlineData(1280, 1024, 850)]          // 304 + 546 (1.1's: 777)
    [InlineData(1280, 800, 626)]           // 80 + 546 (1.1's: 607)
    [InlineData(1680, 1050, 822)]          // 105 + 716.6 (1.1's: 796)
    [InlineData(1920, 1200, 939)]          // 120 + 819 (1.1's: 910)
    public void In_play_the_world_has_all_the_picture_above_the_bar(int w, int h, int rows)
    {
        const UiLayoutMode wide = UiLayoutMode.Wide;
        float share = UiLayout.WorldShare(StatusBar.WorldShare, w, h, wide, SheetSeat.Bottom);
        Assert.Equal(rows, (int)MathF.Round(h * share));
        // The world's last row is the sheet's line 364 from its top, where 1.1's world stops in the sheet (WorldBottom).
        var map = UiLayout.On(w, h, wide, SheetSeat.Bottom);
        Assert.Equal(map.Y + StatusBar.WorldBottom * map.ScaleY, h * share, 2);
        // More rows than 1.1 shows on that picture, and with the bar down the whole of it, as ever.
        Assert.True(share > StatusBar.WorldShare);
        Assert.Equal(1f, UiLayout.WorldShare(1f, w, h, wide, SheetSeat.Bottom));
        // Stretched, fitted and as recorded: 1.1's share of the picture, the same float.
        foreach (var mode in new[] { UiLayoutMode.Stretch, UiLayoutMode.Fit, UiLayoutMode.WideOneOneBelow16By9 })
            Assert.Equal(StatusBar.WorldShare, UiLayout.WorldShare(StatusBar.WorldShare, w, h, mode, SheetSeat.Bottom));
    }

    [Fact]
    public void The_seat_places_the_sheet_only_where_it_is_shorter_than_the_picture()
    {
        // The 16:9 stretch narrower than 16:9, and nowhere else: there alone do the two seats give two maps, and there
        // alone may what follows the seat (a centred sheet's black bands) draw anything.
        foreach (var (w, h) in new[] { (640, 480), (1024, 768), (1280, 1024), (1280, 800), (1680, 1050), (1920, 1080), (1366, 768), (2560, 1080), (7680, 1440), (640, 960) })
            foreach (var mode in Enum.GetValues<UiLayoutMode>())
            {
                bool two = UiLayout.On(w, h, mode, SheetSeat.Bottom) != UiLayout.On(w, h, mode, SheetSeat.Centre);
                Assert.Equal(two, UiLayout.Seated(w, h, mode));
                Assert.Equal(mode is UiLayoutMode.Wide or UiLayoutMode.WidePanesOnSheet && UiLayout.Below16By9(w, h), two);
                // The panes above the bar: centred in the rows above it where the sheet is shorter,
                // whichever seat the sheet has; as recorded, on the sheet where it sits; the sheet's own map
                // wherever the seats are one.
                foreach (var seat in new[] { SheetSeat.Bottom, SheetSeat.Centre })
                {
                    var panes = UiLayout.Panes(w, h, mode, seat);
                    Assert.Equal(mode == UiLayoutMode.WidePanesOnSheet ? UiLayout.On(w, h, mode, seat) : UiLayout.On(w, h, mode, SheetSeat.Centre), panes);
                    bool lifted = mode == UiLayoutMode.Wide && seat == SheetSeat.Bottom && two;
                    Assert.Equal(lifted, panes != UiLayout.On(w, h, mode, seat));
                    for (int py = 0; py < h; py += 53)
                        for (int px = 0; px < w; px += 97)
                            Assert.Equal(StatusBar.FromWindow(px, py, w, h, mode, lifted ? SheetSeat.Centre : seat), StatusBar.PaneFromWindow(px, py, w, h, mode, seat));
                    // Above the advancement screen's taller bar the panes move only where they are lifted at all.
                    Assert.Equal(lifted, UiLayout.Panes(w, h, mode, seat, 160f) != panes);
                }
            }
        // A pane's 352 units of art (128 to 480) stand midway between the picture's top and the bar's top edge, which is
        // 128 units up the bottom-seated sheet: as many rows above the pane as below it, to the float.
        foreach (var (w, h) in new[] { (1024, 768), (1280, 1024), (1280, 800), (1680, 1050), (1360, 768) })
        {
            var bar = UiLayout.On(w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
            var panes = UiLayout.Panes(w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
            float top = UiLayout.ToPicture(0f, UiAssets.Height, panes).Y, bottom = UiLayout.ToPicture(0f, UiLayout.PaneBottom, panes).Y;
            float barTop = UiLayout.ToPicture(0f, UiLayout.PaneBottom, bar).Y;
            Assert.Equal(top, barTop - bottom, 3);
            Assert.Equal((h - UiAssets.Height * bar.ScaleY) / 2f, top, 3);
            Assert.Equal((bar.ScaleX, bar.ScaleY, bar.X), (panes.ScaleX, panes.ScaleY, panes.X));
            // The advancement screen's bar is 160 units tall: the same 352 units centred in the rows above that.
            var advancing = UiLayout.Panes(w, h, UiLayoutMode.Wide, SheetSeat.Bottom, 160f);
            float advTop = UiLayout.ToPicture(0f, UiAssets.Height, advancing).Y, advBottom = UiLayout.ToPicture(0f, UiLayout.PaneBottom, advancing).Y;
            Assert.Equal(advTop, UiLayout.ToPicture(0f, 160f, bar).Y - advBottom, 3);
            Assert.Equal(top - 16f * bar.ScaleY, advTop, 3);
            var unit = StatusBar.PaneFromWindow(w / 4, (int)advTop + 5, w, h, UiLayoutMode.Wide, SheetSeat.Bottom, 160f);
            Assert.InRange(unit.Y, UiAssets.Height - 1 - (int)(5f / bar.ScaleY) - 1, UiAssets.Height - 1);
        }
        // Fitted on a picture taller than 4:3 the sheet has room above and below too, and it is centred whoever asks.
        Assert.Equal(new UiLayout.Map(1f, 1f, 0f, 240f), UiLayout.On(640, 960, UiLayoutMode.Fit, SheetSeat.Bottom));
        Assert.False(UiLayout.Seated(640, 960, UiLayoutMode.Fit));
    }

    public static TheoryData<int, int> Beyond16By9 => new() { { 1921, 1080 }, { 2560, 1080 }, { 3440, 1440 }, { 5120, 1440 }, { 7680, 1440 } };

    [Theory]
    [MemberData(nameof(At16By9))]
    [MemberData(nameof(Beyond16By9))]
    public void At_16_by_9_and_wider_it_is_the_wide_panes_to_the_float_wherever_it_is_seated(int w, int h)
    {
        // The earlier build's own arithmetic, written out: 1.1's stretch at 16:9, the sheet as tall as the picture and 16:9's width beyond.
        float sx = w / 640f, sy = h / 480f, unit = h / 360f;
        var was = UiLayout.UpTo16By9(w, h) ? new UiLayout.Map(sx, sy, 0f, 0f) : new UiLayout.Map(unit, sy, (w - 640f * unit) / 2f, 0f);
        foreach (var seat in Enum.GetValues<SheetSeat>())
        {
            Assert.Equal(was, UiLayout.On(w, h, UiLayoutMode.Wide, seat));
            Assert.Equal(was, UiLayout.On(w, h, UiLayoutMode.WideOneOneBelow16By9, seat));
            Assert.Equal(StatusBar.WorldShare, UiLayout.WorldShare(StatusBar.WorldShare, w, h, UiLayoutMode.Wide, seat));
            for (int py = 0; py < h; py += 37)
                for (int px = 0; px < w; px += 41)
                    Assert.Equal(StatusBar.FromWindow(px, py, w, h, UiLayoutMode.WideOneOneBelow16By9), StatusBar.FromWindow(px, py, w, h, UiLayoutMode.Wide, seat));
        }
        Assert.Equal(UiLayout.Stretched(w, h, UiLayoutMode.WideOneOneBelow16By9), UiLayout.Stretched(w, h, UiLayoutMode.Wide));
        Assert.Equal(UiLayout.Overlays(w, h, UiLayoutMode.WideOneOneBelow16By9), UiLayout.Overlays(w, h, UiLayoutMode.Wide));
    }

    [Fact]
    public void A_recording_says_which_wide_it_was_made_in_and_the_old_word_is_read_as_it_was_meant()
    {
        // Ours: `layout=wide sheet=16x9`. A build before it passes over the second word and reads its own wide.
        // And `panes=centred`: the panes above the bar centred in the rows above it; an earlier build passes over that one.
        Assert.Equal(["layout=wide", "sheet=16x9", "panes=centred"], UiLayout.TapeFields(UiLayoutMode.Wide));
        Assert.Equal(["layout=fit"], UiLayout.TapeFields(UiLayoutMode.Fit));
        Assert.Equal(["layout=stretch"], UiLayout.TapeFields(UiLayoutMode.Stretch));
        Assert.Equal(UiLayoutMode.Wide, UiLayout.FromTape("wide", "16x9", "centred"));
        // The earlier build's tape: `sheet=16x9` and no more, which meant the panes on the bottom-seated sheet (read as it was meant).
        Assert.Equal(UiLayoutMode.WidePanesOnSheet, UiLayout.FromTape("wide", "16x9", null));
        Assert.Equal(["layout=wide", "sheet=16x9"], UiLayout.TapeFields(UiLayoutMode.WidePanesOnSheet));
        // The earlier builds' tape: the word alone, which meant 1.1's stretch on a picture narrower than 16:9.
        Assert.Equal(UiLayoutMode.WideOneOneBelow16By9, UiLayout.FromTape("wide", null, null));
        Assert.Equal(UiLayout.On(1024, 768, UiLayoutMode.Stretch), UiLayout.On(1024, 768, UiLayout.FromTape("wide", null, null)!.Value));
        Assert.Equal(["layout=wide"], UiLayout.TapeFields(UiLayoutMode.WideOneOneBelow16By9));
        Assert.Equal(UiLayoutMode.Fit, UiLayout.FromTape("fit", null, null));
        Assert.Equal(UiLayoutMode.Stretch, UiLayout.FromTape("stretch", null, null));
        Assert.Null(UiLayout.FromTape(null, null, null));
        Assert.Null(UiLayout.FromTape("sideways", "16x9", "centred"));
        foreach (var mode in Enum.GetValues<UiLayoutMode>())
        {
            string? Word(string key) => UiLayout.TapeFields(mode).Select(f => f.Split('=')).FirstOrDefault(f => f[0] == key)?[1];
            Assert.Equal(mode, UiLayout.FromTape(Word(UiLayout.Key), Word(UiLayout.TapeSheetKey), Word(UiLayout.TapePanesKey)));
        }
        // No setting names the recording's own: the player's word `wide` is the 16:9 stretch as it is now.
        Assert.Equal(UiLayoutMode.Wide, UiLayout.Parse("wide"));
        Assert.Equal("wide", UiLayout.Name(UiLayoutMode.WideOneOneBelow16By9));
        Assert.Equal("wide", UiLayout.Name(UiLayoutMode.WidePanesOnSheet));
    }

    [Theory]
    [InlineData(1921, 1080, 1f)]                   // a whole pixel past 16:9: centred, half a pixel each side
    [InlineData(1922, 1080, 2f)]
    [InlineData(2560, 1080, 2560 - 1920)]          // 21:9: the sheet 1920 wide, 320 columns of world each side
    [InlineData(3440, 1440, 3440 - 2560)]          // 21:9 ultra-wide: 2560 wide, 440 each side
    [InlineData(5120, 1440, 5120 - 2560)]          // 32:9
    [InlineData(7680, 1440, 7680 - 2560)]          // a three-screen desktop (5.33:1): 2560 wide, centred
    public void Wider_than_16_by_9_it_is_16_by_9s_stretch_centred(int w, int h, float beside)
    {
        var map = UiLayout.On(w, h, UiLayoutMode.Wide);
        Assert.False(UiLayout.UpTo16By9(w, h));
        Assert.Equal(h / 480f, map.ScaleY);
        Assert.Equal(h * 16f / 9f / 640f, map.ScaleX, 4);
        Assert.Equal(beside / 2f, map.X, 2);
        Assert.Equal(0f, map.Y);
        Assert.Equal(h, map.Bottom, 3);
        Assert.Equal(map.X + 640f * map.ScaleX, w - beside / 2f, 2);          // as far in from the right edge as from the left
        // A sheet unit is 4/3 as wide as tall on the picture's pixels: 1.1's ovals come out round, as on a 16:9 screen.
        Assert.Equal(4f / 3f, map.ScaleX / map.ScaleY, 4);
        Assert.True(UiLayout.Overlays(w, h, UiLayoutMode.Wide));
        Assert.False(UiLayout.Stretched(w, h, UiLayoutMode.Wide));
    }

    [Fact]
    public void On_the_big_desktop_the_sheet_is_2560_wide_in_the_middle()
    {
        Assert.Equal(new UiLayout.Map(4f, 3f, 2560f, 0f), UiLayout.On(7680, 1440, UiLayoutMode.Wide));
        Assert.Equal(new UiLayout.Map(4f, 3f, 440f, 0f), UiLayout.On(3440, 1440, UiLayoutMode.Wide));
        Assert.Equal(new UiLayout.Map(3f, 2.25f, 320f, 0f), UiLayout.On(2560, 1080, UiLayoutMode.Wide));
    }

    [Theory]
    [InlineData(2560, 1080)]
    [InlineData(3440, 1440)]
    [InlineData(7680, 1440)]
    public void Wider_than_16_by_9_the_pointer_goes_both_ways_and_is_off_the_sheet_beside_it(int w, int h)
    {
        const UiLayoutMode wide = UiLayoutMode.Wide;
        var map = UiLayout.On(w, h, wide);
        int left = (int)map.X, right = (int)(map.X + 640f * map.ScaleX);
        // The sheet's corners: its bottom left unit (0, 0), its top right (639, 479).
        Assert.Equal((0, 0), StatusBar.FromWindow(left, h - 1, w, h, wide));
        Assert.Equal((639, 479), StatusBar.FromWindow(right - 1, 0, w, h, wide));
        Assert.Equal((320, 239), StatusBar.FromWindow(w / 2, h / 2, w, h, wide));
        // Beside it the pointer is off the sheet, on its own side: the world answers there.
        Assert.True(StatusBar.FromWindow(left - 1, h - 10, w, h, wide).X < 0);
        Assert.True(StatusBar.FromWindow(right, h - 10, w, h, wide).X >= UiAssets.Width);
        Assert.True(StatusBar.FromWindow(0, h - 10, w, h, wide).X < 0);
        Assert.True(StatusBar.FromWindow(w - 1, h - 10, w, h, wide).X >= UiAssets.Width);
        // Every sheet point to the picture and back is itself, and every pixel's unit holds the pixel.
        for (float x = 0f; x <= 640f; x += 12.5f)
            for (float y = 0f; y <= 480f; y += 7.5f)
            {
                var (px, py) = UiLayout.ToPicture(x, y, w, h, wide);
                var (bx, by) = UiLayout.FromPicture(px, py, w, h, wide);
                Assert.Equal(x, bx, 3);
                Assert.Equal(y, by, 3);
            }
        for (int py = 0; py < h; py += 17)
            for (int px = left; px < right; px += 23)
            {
                var (ux, uy) = StatusBar.FromWindow(px, py, w, h, wide);
                Assert.InRange(ux, 0, 639);
                Assert.InRange(uy, 0, 479);
                // The pixel lies in its unit: across from the unit's left edge, down from its top edge.
                Assert.InRange(px - (map.X + ux * map.ScaleX), -0.001f, map.ScaleX);
                Assert.InRange(py - (479 - uy) * map.ScaleY, -0.001f, map.ScaleY);
            }
    }

    [Fact]
    public void A_layout_is_asked_for_by_name_the_command_line_over_the_settings_file()
    {
        Assert.Equal(UiLayoutMode.Fit, UiLayout.Parse(" Fit "));
        Assert.Equal(UiLayoutMode.Stretch, UiLayout.Parse("STRETCH"));
        Assert.Equal(UiLayoutMode.Wide, UiLayout.Parse("Wide"));
        Assert.Null(UiLayout.Parse("sideways"));
        Assert.Equal("stretch", UiLayout.Name(UiLayoutMode.Stretch));
        Assert.Equal("fit", UiLayout.Name(UiLayoutMode.Fit));
        Assert.Equal("wide", UiLayout.Name(UiLayoutMode.Wide));
        // Every layout a player can ask for by its own name (the recording's own, WideOneOneBelow16By9, is none of them).
        foreach (var m in new[] { UiLayoutMode.Wide, UiLayoutMode.Fit, UiLayoutMode.Stretch }) Assert.Equal(m, UiLayout.Parse(UiLayout.Name(m)));

        string dir = Path.Combine(Path.GetTempPath(), "vitomir-uilayout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string ini = Path.Combine(dir, "vitomir.ini"), none = Path.Combine(dir, "none.ini");
            var said = new List<string>();
            Assert.Equal(UiLayoutMode.Wide, UiLayout.Asked(null, said.Add, none));                // nothing said: the 16:9 stretch
            Assert.Equal(UiLayoutMode.Stretch, UiLayout.Asked("stretch", said.Add, none));
            File.WriteAllText(ini, "[interface]\nlayout = stretch\n");
            Assert.Equal(UiLayoutMode.Stretch, UiLayout.Asked(null, said.Add, ini));              // the settings file
            Assert.Equal(UiLayoutMode.Fit, UiLayout.Asked("fit", said.Add, ini));                 // the command line over it
            Assert.Empty(said);
            Assert.Equal(UiLayoutMode.Stretch, UiLayout.Asked("sideways", said.Add, ini));        // a wrong word: passed over, and said
            Assert.Single(said);
            Assert.Contains("wide, fit nor stretch", said[0]);
            File.WriteAllText(ini, "[interface]\nlayout = sideways\n");
            Assert.Equal(UiLayoutMode.Wide, UiLayout.Asked(null, said.Add, ini));
            Assert.Equal(2, said.Count);
            File.WriteAllText(ini, "[interface]\nlayout = wide\n");
            Assert.Equal(UiLayoutMode.Wide, UiLayout.Asked(null, said.Add, ini));
            Assert.Equal(UiLayoutMode.Fit, UiLayout.Asked("fit", said.Add, ini));
            File.WriteAllText(ini, "[interface]\nlayout = fit\n");
            Assert.Equal(UiLayoutMode.Wide, UiLayout.Asked("wide", said.Add, ini));
            Assert.Equal(2, said.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>An uncompressed 32-bit picture of one colour, as 1.1's own interface art is stored.</summary>
    static byte[] Tga(int width, int height, byte shade)
    {
        var data = new byte[18 + width * height * 4];
        data[2] = 2;
        data[12] = (byte)width; data[13] = (byte)(width >> 8);
        data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = 32; data[17] = 8;
        for (int i = 18; i < data.Length; i += 4) (data[i], data[i + 1], data[i + 2], data[i + 3]) = (shade, shade, shade, 255);
        return data;
    }

    [Fact]
    public void Finer_art_is_a_file_of_the_same_name_at_exactly_its_multiple_of_the_size()
    {
        string? install = GameFiles.Locate();
        Assert.SkipWhen(install is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-finer-" + Guid.NewGuid().ToString("N"));
        string materials = Path.Combine(dir, "ui", "materials");
        Directory.CreateDirectory(materials);
        try
        {
            int w, h, w2, h2, w3, h3;
            using (var plain = new GameFiles(install!))
            {
                var assets = new UiAssets(plain);
                var original = assets.Texture("StatusBarSP_01")!;
                (w, h) = (original.Width, original.Height);
                (w2, h2) = (assets.Texture("StatusBarSP_02")!.Width, assets.Texture("StatusBarSP_02")!.Height);
                (w3, h3) = (assets.Texture("StatusBarSP_03")!.Width, assets.Texture("StatusBarSP_03")!.Height);
                Assert.Null(assets.Finer("StatusBarSP_01"));                   // 1.1's own data has none
                Assert.Empty(assets.FinerRefused);
            }
            File.WriteAllBytes(Path.Combine(materials, "StatusBarSP_01@4x.tga"), Tga(w * 4, h * 4, 40));
            File.WriteAllBytes(Path.Combine(materials, "StatusBarSP_01@2x.tga"), Tga(w * 2, h * 2, 80));
            File.WriteAllBytes(Path.Combine(materials, "StatusBarSP_02@2x.tga"), Tga(w2 * 2 - 1, h2 * 2, 80));   // not twice the size
            File.WriteAllBytes(Path.Combine(materials, "StatusBarSP_03@3x.tga"), Tga(w3 * 3, h3 * 3, 120));
            using var files = new GameFiles(install!, [dir]);
            var over = new UiAssets(files);
            var four = over.Finer("StatusBarSP_01");
            Assert.NotNull(four);
            Assert.Equal((4, w * 4, h * 4), (four.Value.Scale, four.Value.Image.Width, four.Value.Image.Height));   // the largest wins
            Assert.Equal(3, over.Finer("StatusBarSP_03.tga")!.Value.Scale);    // named with its extension too
            // The original is still what a pane is laid out from.
            Assert.Equal((w, h), (over.Texture("StatusBarSP_01")!.Width, over.Texture("StatusBarSP_01")!.Height));
            // A picture that is not the multiple its name says is refused, said once, and the original drawn.
            Assert.Null(over.Finer("StatusBarSP_02"));
            Assert.Null(over.Finer("StatusBarSP_02"));
            Assert.Single(over.FinerRefused);
            Assert.Contains("StatusBarSP_02@2x.tga", over.FinerRefused[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
