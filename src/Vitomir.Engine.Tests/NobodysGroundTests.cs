// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A click off the world's view is not the world's**. A pointer event that no
/// pane takes is the world's only on the columns and rows the world is drawn into that frame
/// (<see cref="WorldFrame.Shows(float, float, int, int, float, bool, ViewColumns, UiLayoutMode, SheetSeat)"/>, the one
/// test the hover, the click, the cast and the held walk all ask); anywhere else the picture shows no world and the
/// event is nobody's. These go through that test with the installed panes: where the sheet is the picture (1.1's
/// stretch, the 16:9 stretch at 16:9) nobody's ground is exactly what lies under a pane's own quads, which 1.1 gives
/// to the pane, and the clear corners above the status bar's shoulders stay the world's; beside an open
/// pane on a picture wider than the sheet, and above and below one on a picture taller than it (where a pane is
/// centred in the rows above the bar), the black is nobody's and the same pixel with the pane closed is
/// the world's. The layout and the seat are named on each call; none is put in
/// force. <see cref="NobodysGroundRunTests"/> is the same through the program.
/// </summary>
public class NobodysGroundTests
{
    /// <summary>The game's lens across (halved for a pane), for the columns' magnification, which no test here reads.</summary>
    const float Lens = 80f;

    static readonly ViewColumns Whole = WorldView.Columns(barUp: true, leftPane: false, rightPane: false, Lens);
    static readonly ViewColumns LeftPane = WorldView.Columns(barUp: true, leftPane: true, rightPane: false, Lens);
    static readonly ViewColumns RightPane = WorldView.Columns(barUp: true, leftPane: false, rightPane: true, Lens);
    static readonly ViewColumns BothPanes = WorldView.Columns(barUp: true, leftPane: true, rightPane: true, Lens);

    /// <summary>The share of the picture's height the world has with the status bar up, as the frame asks for it (<see cref="WorldFrame.Asked"/>).</summary>
    static float BarUp(int w, int h, UiLayoutMode mode) => UiLayout.WorldShare(StatusBar.WorldShare, w, h, mode, SheetSeat.Bottom);

    static bool Shows(float x, float y, int w, int h, UiLayoutMode mode, ViewColumns columns, bool cell = false) =>
        WorldFrame.Shows(x, y, w, h, BarUp(w, h, mode), cell, columns, mode, SheetSeat.Bottom);

    /// <summary>1.1's own test of the pane under the cursor: on one of its quads, both edges counted.</summary>
    static bool Quads(NuiPane pane, int x, int y)
    {
        int px = pane.Pos.Length > 1 ? pane.Pos[0] : 0, py = pane.Pos.Length > 1 ? pane.Pos[1] : 0;
        foreach (var q in pane.Quads)
            if (x >= px + q.Pos[0] && x <= px + q.Pos[0] + q.Size[0] && y >= py + q.Pos[1] && y <= py + q.Pos[1] + q.Size[1]) return true;
        return false;
    }

    /// <summary>The middle of a unit of the sheet (y up) as a pixel of the picture.</summary>
    static (float X, float Y) Middle(int x, int y, int w, int h, UiLayoutMode mode) =>
        UiLayout.ToPicture(x + 0.5f, y + 0.5f, UiLayout.On(w, h, mode, SheetSeat.Bottom));

    /// <summary>
    /// **The black above an open pane, as the layout has it**: the picture's rows from <c>Top</c>, <c>Rows</c> of them,
    /// that lie above the pane in its own half of the columns. A pane's art reaches the top of the sheet in the panes'
    /// own map (<see cref="UiLayout.Panes(float, float, UiLayoutMode, SheetSeat)"/>), so they are the rows above that:
    /// none wherever the sheet is as tall as the picture.
    /// </summary>
    internal static (int Top, int Rows) BlackAbove(int w, int h, UiLayoutMode mode) =>
        (0, (int)MathF.Floor(UiLayout.Panes(w, h, mode, SheetSeat.Bottom).Y));

    /// <summary>
    /// **The black below an open pane and above the status bar** (a pane centred in the rows above
    /// the bar), in the column of the sheet's unit <paramref name="unitX"/>: the picture's rows under the pane's art
    /// there (128 units up the panes' map; 120 where its small bottom piece hangs, 131 to 147 and 493 to 509 across)
    /// and over the bar's (128 units up the sheet's; 139 under its message strip, 144 to 496 across). None wherever
    /// the panes ride the sheet, where a pane stands on the bar.
    /// </summary>
    internal static (int Top, int Rows) BlackBelow(int w, int h, UiLayoutMode mode, int unitX)
    {
        bool piece = unitX is >= 130 and <= 148 or >= 492 and <= 510, strip = unitX is >= 143 and <= 497;
        float under = UiLayout.ToPicture(0f, piece ? 120f : 128f, UiLayout.Panes(w, h, mode, SheetSeat.Bottom)).Y;
        float bar = UiLayout.ToPicture(0f, strip ? 140f : 129f, UiLayout.On(w, h, mode, SheetSeat.Bottom)).Y;
        int top = (int)MathF.Ceiling(under);
        return (top, Math.Max(0, (int)MathF.Ceiling(bar) - top));
    }

    public static TheoryData<int, int, UiLayoutMode> SheetIsThePicture => new()
    {
        { 640, 480, UiLayoutMode.Stretch }, { 1024, 768, UiLayoutMode.Stretch }, { 1600, 900, UiLayoutMode.Stretch },
        { 1280, 720, UiLayoutMode.Wide }, { 1366, 768, UiLayoutMode.Wide }, { 1920, 1080, UiLayoutMode.Wide },
        { 1024, 768, UiLayoutMode.WideOneOneBelow16By9 },
    };

    /// <summary>
    /// **1.1's behaviour kept where the sheet is the picture.** With the bar up and any of the panes open, every pixel
    /// of the picture is the world's view or lies under a quad of the bar or of an open pane — so the only clicks the
    /// rule takes from the world there are ones 1.1 gives to a pane — and on the world's view the rule is the rows and
    /// columns test the hover asked before it. The bar's own shape is its seven quads (<see cref="StatusBar.Covers"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(SheetIsThePicture))]
    public void Where_the_sheet_is_the_picture_nobody_s_ground_is_under_a_pane_s_quads(int w, int h, UiLayoutMode mode)
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var bar = assets.Pane(StatusBar.PaneName);
        Assert.True(UiLayout.Stretched(w, h, mode));
        Assert.False(UiLayout.Overlays(w, h, mode));
        int rows = (int)MathF.Round(h * StatusBar.WorldShare);
        var cases = new (ViewColumns Columns, string[] Open)[]
        {
            (Whole, []),
            (LeftPane, [CharacterPane.PaneName]), (LeftPane, [QuestPane.PaneName]), (LeftPane, ["VaultPane"]),
            (RightPane, [InventoryPane.PaneName]), (RightPane, [DisciplinePane.PaneName]),
            (BothPanes, [CharacterPane.PaneName, InventoryPane.PaneName]), (BothPanes, [QuestPane.PaneName, DisciplinePane.PaneName]),
        };
        int step = w > 1100 ? 3 : 1;
        foreach (var (columns, open) in cases)
        {
            var panes = open.Select(assets.Pane).ToArray();
            int worlds = 0, nobodys = 0;
            for (int py = 0; py < h; py += step)
                for (int px = 0; px < w; px += step)
                {
                    bool shows = Shows(px, py, w, h, mode, columns);
                    // The test the hover asked earlier: 1.1's rows, across the pane's columns.
                    var (x0, cols) = columns.Pixels(w);
                    Assert.Equal(columns.Drawn && Pick.OnWorld(py, h, rows) && px >= x0 && px < x0 + cols, shows);
                    if (shows) { worlds++; continue; }
                    nobodys++;
                    var unit = StatusBar.FromWindow(px, py, w, h, mode);
                    Assert.True(Quads(bar, unit.X, unit.Y) || panes.Any(p => Quads(p, unit.X, unit.Y)),
                                $"{px},{py} (unit {unit}) of {w}x{h} with {string.Join('+', open)} open is neither the world's nor under a pane's quad");
                }
            Assert.True(nobodys > 0);
            Assert.Equal(columns.Drawn, worlds > 0);
        }
    }

    /// <summary>
    /// **The clear corners above the bar's shoulders are the world's, as in 1.1**:
    /// the bar's art is 128 units tall with a strip of 11 more over its middle (144 to 496), so
    /// beside the strip no quad covers, and the world is drawn there (its last row is 116 units up). Nothing else of
    /// the bar's rows is clear: the party window is under the bar's quads, whose art is see-through there.
    /// </summary>
    [Theory]
    [MemberData(nameof(SheetIsThePicture))]
    public void The_clear_corners_above_the_bar_s_shoulders_stay_the_world_s(int w, int h, UiLayoutMode mode)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        // The bar's shape, whole: under 129 units every column is covered; from 129 to 139 only the strip; above, none.
        for (int y = 0; y < UiAssets.Height; y++)
            for (int x = 0; x < UiAssets.Width; x++)
            {
                Assert.Equal(Quads(bar.Pane, x, y), bar.Covers(x, y));
                Assert.Equal(y <= 128 || y <= 139 && x >= 144 && x <= 496, bar.Covers(x, y));
            }
        foreach (int y in new[] { 129, 134, 139 })
            foreach (int x in new[] { 0, 70, 143, 497, 560, 639 })
            {
                var (px, py) = Middle(x, y, w, h, mode);
                Assert.Equal((x, y), StatusBar.FromWindow(px, py, w, h, mode));
                Assert.False(bar.Covers(x, y));
                Assert.True(Shows(px, py, w, h, mode, Whole), $"the corner {x},{y} at {px:0},{py:0} of {w}x{h}");
                // With a pane up on that side the corner is under the pane's lowest quads; on the other side, the world's still.
                Assert.Equal(x < 320, Shows(px, py, w, h, mode, RightPane));
                Assert.Equal(x >= 320, Shows(px, py, w, h, mode, LeftPane));
            }
        // The party window (the heads' hole in the art) and the bar's own rows below the world's last are the bar's.
        foreach (var (x, y) in new[] { (320, 60), (200, 30), (5, 100), (635, 3) })
        {
            var (px, py) = Middle(x, y, w, h, mode);
            Assert.True(bar.Covers(x, y));
            Assert.False(Shows(px, py, w, h, mode, Whole));
        }
    }

    public static TheoryData<int, int, UiLayoutMode> WiderThanTheSheet => new()
    {
        { 2560, 1080, UiLayoutMode.Wide }, { 3440, 1440, UiLayoutMode.Wide }, { 7680, 1440, UiLayoutMode.Wide },
        { 2560, 1080, UiLayoutMode.Fit }, { 1920, 1080, UiLayoutMode.Fit },
    };

    /// <summary>
    /// **Beside an open pane on a picture wider than the sheet** (the 16:9 stretch beyond 16:9, a 7680x1440 one; the
    /// fitted sheet beyond 4:3): the pane blanks its half of the picture's columns and sits at the sheet's middle, so
    /// the strip beyond it is black. A pointer there is off the sheet (no pane's) and off the world's view: nobody's.
    /// The same pixel with the pane closed is the world's, down to the picture's bottom edge beside the bar.
    /// </summary>
    [Theory]
    [MemberData(nameof(WiderThanTheSheet))]
    public void Beside_an_open_pane_on_a_wider_picture_is_nobody_s_and_with_it_closed_the_world_s(int w, int h, UiLayoutMode mode)
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var all = new[] { StatusBar.PaneName, CharacterPane.PaneName, QuestPane.PaneName, InventoryPane.PaneName, DisciplinePane.PaneName }
                  .Select(assets.Pane).ToArray();
        var map = UiLayout.On(w, h, mode, SheetSeat.Bottom);
        Assert.True(UiLayout.Overlays(w, h, mode));
        float sheetLeft = map.X, sheetRight = map.X + UiAssets.Width * map.ScaleX;
        int strips = 0;
        for (int py = 0; py < h; py += 7)
            for (int px = 0; px < w; px += 5)
            {
                bool beside = px < sheetLeft || px >= sheetRight;
                if (!beside) continue;
                strips++;
                // Off the sheet: no pane's and no control's, whatever is open — but for the one unit past the sheet's right
                // edge, which 1.1's own test of a quad counts as on it (both edges), so a pane there still answers.
                var unit = StatusBar.FromWindow(px, py, w, h, mode);
                Assert.True(unit.X < 0 || unit.X >= UiAssets.Width);
                if (unit.X != UiAssets.Width) Assert.DoesNotContain(all, p => Quads(p, unit.X, unit.Y));
                // No pane up: the world's, every row of it (carried on down beside the bar, which lies over it).
                Assert.True(Shows(px, py, w, h, mode, Whole), $"{px},{py} of {w}x{h} {mode}");
                // A pane on that side: its half of the columns is blanked, and beyond the pane nothing is drawn.
                Assert.Equal(px < sheetLeft, Shows(px, py, w, h, mode, RightPane));
                Assert.Equal(px >= sheetRight, Shows(px, py, w, h, mode, LeftPane));
                Assert.False(Shows(px, py, w, h, mode, BothPanes));
            }
        Assert.True(strips > 1000);
        // Inside the sheet's columns the bar's rows are the bar's and its black, not the world's; above them, the world's.
        int rows = (int)MathF.Round(h * StatusBar.WorldShare);
        float middle = (sheetLeft + sheetRight) / 2f;
        Assert.True(Shows(middle, rows - 1, w, h, mode, Whole));
        Assert.False(Shows(middle, rows, w, h, mode, Whole));
        Assert.False(Shows(sheetLeft, h - 1, w, h, mode, Whole));
        Assert.True(Shows(sheetLeft - 1f, h - 1, w, h, mode, Whole));
        Assert.False(Shows(sheetRight - 1f, h - 1, w, h, mode, Whole));
        Assert.True(Shows(MathF.Ceiling(sheetRight), h - 1, w, h, mode, Whole));
        // A cell level's world is not carried on under the bar: beside it the picture is black there, and nobody's.
        Assert.False(Shows(sheetLeft - 1f, h - 1, w, h, mode, Whole, cell: true));
        Assert.True(Shows(sheetLeft - 1f, rows - 1, w, h, mode, Whole, cell: true));
        // And with no status bar the world has the whole picture.
        Assert.True(WorldFrame.Shows(middle, h - 1, w, h, 1f, false, ViewColumns.Whole, mode, SheetSeat.Bottom));
    }

    public static TheoryData<int, int> TallerThanTheSheet => new() { { 1024, 768 }, { 1280, 800 }, { 1280, 1024 }, { 1680, 1050 } };

    /// <summary>
    /// **Above and below an open pane on a picture narrower than 16:9** in the 16:9 stretch: the status bar stands on
    /// the picture's bottom edge and the world has every row above it, and a pane is centred in those rows,
    /// so its half of the columns has a black band above it and one below, between it and the bar.
    /// A pointer there is on no pane (off the top of the panes' map, or under its art) and on no quad of the bar, and
    /// off the world's view: nobody's. With the pane closed the same pixel is the world's; so is the other half
    /// throughout. The two bands are as tall as each other, to a row.
    /// </summary>
    [Theory]
    [MemberData(nameof(TallerThanTheSheet))]
    public void Above_and_below_an_open_pane_on_a_narrower_picture_is_nobody_s_and_with_it_closed_the_world_s(int w, int h)
    {
        const UiLayoutMode mode = UiLayoutMode.Wide;
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var bar = assets.Pane(StatusBar.PaneName);
        var panes = new[] { CharacterPane.PaneName, QuestPane.PaneName, InventoryPane.PaneName, DisciplinePane.PaneName, "VaultPane", BuySellPane.PaneName }
                    .Select(assets.Pane).ToArray();
        Assert.False(UiLayout.Overlays(w, h, mode));
        Assert.True(UiLayout.Panes(w, h, mode, SheetSeat.Bottom) != UiLayout.On(w, h, mode, SheetSeat.Bottom));
        var above = BlackAbove(w, h, mode);
        Assert.True(above.Rows >= 8 && BlackBelow(w, h, mode, 60).Rows >= 8, $"{above.Rows} rows above and {BlackBelow(w, h, mode, 60).Rows} below at {w}x{h}");
        Assert.True(BlackBelow(w, h, mode, 60).Rows > BlackBelow(w, h, mode, 320).Rows);
        int seen = 0;
        for (int px = 0; px < w; px += 5)
            foreach (var band in new[] { above, BlackBelow(w, h, mode, StatusBar.FromWindow(px, 0, w, h, mode, SheetSeat.Bottom).X) })
                for (int py = band.Top; py < band.Top + band.Rows; py += 3)
                {
                    seen++;
                    // On no pane, through the panes' own map — but for the one unit above the sheet's top edge there,
                    // which 1.1's own test of a quad counts as on it (both edges) — and on no quad of the bar.
                    var unit = StatusBar.PaneFromWindow(px, py, w, h, mode, SheetSeat.Bottom);
                    if (unit.Y != UiAssets.Height) Assert.DoesNotContain(panes, p => Quads(p, unit.X, unit.Y));
                    var onBar = StatusBar.FromWindow(px, py, w, h, mode, SheetSeat.Bottom);
                    Assert.False(Quads(bar, onBar.X, onBar.Y), $"{px},{py} is unit {onBar} of the bar's map");
                    Assert.True(Shows(px, py, w, h, mode, Whole));
                    Assert.Equal(px < w / 2, Shows(px, py, w, h, mode, RightPane));
                    Assert.Equal(px >= w / 2, Shows(px, py, w, h, mode, LeftPane));
                    Assert.False(Shows(px, py, w, h, mode, BothPanes));
                }
        Assert.True(seen > 1000);
        // The pane's art (128 to 480 of the panes' map) between the two, and as many rows of black above it as below
        // it down to the bar's top edge (128 of the sheet's map): equal to a row.
        var paneMap = UiLayout.Panes(w, h, mode, SheetSeat.Bottom);
        var barMap = UiLayout.On(w, h, mode, SheetSeat.Bottom);
        float paneTop = UiLayout.ToPicture(0f, UiAssets.Height, paneMap).Y, paneBottom = UiLayout.ToPicture(0f, UiLayout.PaneBottom, paneMap).Y;
        float barTop = UiLayout.ToPicture(0f, UiLayout.PaneBottom, barMap).Y;
        Assert.True(MathF.Abs(paneTop - (barTop - paneBottom)) < 1f, $"{paneTop} rows above the pane, {barTop - paneBottom} below");
        foreach (var pane in panes)
        {
            Assert.Equal(UiLayout.PaneBottom, pane.Quads.Where(q => q.Size[0] > 16).Min(q => (float)q.Pos[1]));
            Assert.Equal(UiAssets.Height, pane.Quads.Max(q => q.Pos[1] + q.Size[1]));
        }
        // The world's rows reach down past the pane to the bar's line, and stop there: below it the bar's quads.
        int rows = (int)MathF.Round(h * BarUp(w, h, mode));
        Assert.True(rows > paneBottom && rows < h);
        Assert.True(Shows(5, rows - 1, w, h, mode, Whole));
        Assert.False(Shows(5, rows, w, h, mode, Whole));
        var under = StatusBar.FromWindow(5, rows, w, h, mode, SheetSeat.Bottom);
        Assert.True(Quads(bar, under.X, under.Y));
        // As an earlier build recorded it the panes ride the bottom-seated sheet: the black is all above them, none below.
        Assert.Equal(UiLayout.On(w, h, UiLayoutMode.WidePanesOnSheet, SheetSeat.Bottom), UiLayout.Panes(w, h, UiLayoutMode.WidePanesOnSheet, SheetSeat.Bottom));
        Assert.Equal(0, BlackBelow(w, h, UiLayoutMode.WidePanesOnSheet, 60).Rows);
        Assert.Equal((int)MathF.Floor(barMap.Y), BlackAbove(w, h, UiLayoutMode.WidePanesOnSheet).Rows);
    }

    /// <summary>
    /// Off the picture, and with no world drawn at all (a pane each side, or state 4: the shop, the advancement screen,
    /// the map, Options), nothing is the world's.
    /// </summary>
    [Fact]
    public void Off_the_picture_and_with_no_world_drawn_nothing_is_the_world_s()
    {
        foreach (var mode in new[] { UiLayoutMode.Stretch, UiLayoutMode.Wide, UiLayoutMode.Fit })
            foreach (var (w, h) in new[] { (640, 480), (1024, 768), (1920, 1080), (2560, 1080) })
            {
                Assert.True(Shows(w / 2f, 10f, w, h, mode, Whole));
                Assert.False(Shows(-1f, 10f, w, h, mode, Whole));
                Assert.False(Shows(w, 10f, w, h, mode, Whole));
                Assert.False(Shows(w / 2f, -1f, w, h, mode, Whole));
                Assert.False(WorldFrame.Shows(w / 2f, h, w, h, 1f, false, ViewColumns.Whole, mode, SheetSeat.Bottom));
                Assert.True(WorldFrame.Shows(w / 2f, h - 1, w, h, 1f, false, ViewColumns.Whole, mode, SheetSeat.Bottom));
                foreach (float x in new[] { 0f, w / 4f, w / 2f, w - 1f })
                    foreach (float y in new[] { 0f, h / 3f, h - 1f })
                    {
                        Assert.False(Shows(x, y, w, h, mode, BothPanes));
                        Assert.False(Shows(x, y, w, h, mode, new ViewColumns(0f, 0f, 1f)));
                    }
            }
    }
}

/// <summary>
/// **The same through the program**: a game recorded with <c>--no-scripts</c> and <c>--pointer-log</c>, its
/// clicks given as window pixels. With a pane open, a pixel of the black its side of the picture is left with — beside
/// the pane on a picture wider than the sheet, above it on one taller — is hovered, clicked and right-clicked: the
/// hover, the click and the cast each say it is nobody's, and nothing is ordered, walked or cast. The pane closed, the
/// same pixel is the world's to the hover and to the click, and the click walks there. The recording then replays the
/// same. Where the black above and below a pane is comes from the layout (<see cref="NobodysGroundTests.BlackAbove"/>,
/// <see cref="NobodysGroundTests.BlackBelow"/>), not from fixed rows; only the column is chosen by hand.
/// Replays: <c>VITOMIR_NO_REPLAYS=1</c> leaves them out.
/// </summary>
public class NobodysGroundRunTests
{
    [Theory]
    // Beside the open pack (the right-hand pane) on a picture wider than the sheet: beyond the pane, the picture's right.
    [InlineData(2560, 1080, "wide", "I", 2400, 400)]
    [InlineData(7680, 1440, "wide", "I", 6800, 240)]
    [InlineData(2560, 1080, "fit", "I", 2400, 400)]
    // Above the open character sheet (the left-hand pane) on a picture narrower than 16:9, and below it, between it
    // and the bar (the pane centred in the rows above the bar): the rows the layout gives.
    [InlineData(1024, 768, "wide", "C", 256, -1)]
    [InlineData(1280, 800, "wide", "C", 320, -1)]
    [InlineData(1024, 768, "wide", "C", 256, -2)]
    [InlineData(1280, 800, "wide", "C", 320, -2)]
    public void A_click_off_the_world_s_view_orders_nothing_and_with_the_pane_closed_it_walks(
        int w, int h, string layout, string pane, int x, int y)
    {
        var (app, game) = ReplayBaselineTests.Needs();
        var mode = UiLayout.Parse(layout)!.Value;
        if (y < 0)
        {
            var black = y == -1 ? NobodysGroundTests.BlackAbove(w, h, mode)
                      : NobodysGroundTests.BlackBelow(w, h, mode, StatusBar.FromWindow(x, 0, w, h, mode, SheetSeat.Bottom).X);
            Assert.True(black.Rows >= 8, $"{black.Rows} rows of black {(y == -1 ? "above" : "below")} a pane at {w}x{h}");
            y = black.Top + black.Rows / 2;
        }
        // The pixel is off the sheet (no pane's), and off the world's view with that pane up; with none up, on it.
        var columns = WorldView.Columns(barUp: true, leftPane: pane == "C", rightPane: pane == "I", 80f);
        float share = UiLayout.WorldShare(StatusBar.WorldShare, w, h, mode, SheetSeat.Bottom);
        Assert.False(WorldFrame.Shows(x, y, w, h, share, false, columns, mode, SheetSeat.Bottom));
        Assert.True(WorldFrame.Shows(x, y, w, h, share, false, ViewColumns.Whole, mode, SheetSeat.Bottom));
        var unit = StatusBar.PaneFromWindow(x, y, w, h, mode, SheetSeat.Bottom);
        Assert.True(unit.X < 0 || unit.X > UiAssets.Width || unit.Y > UiAssets.Height || unit.Y < 128, $"{x},{y} is unit {unit} of the panes' map");

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-nobodys-{Guid.NewGuid():N}")).FullName;
        try
        {
            string at = $"{x},{y}";
            // The game camera is put looking down the alley's length from above at frame 30 (the harness's switches), so
            // the street lies under every pixel of the world's view, the picture's far sides and top rows included.
            string steps = $"wait:30;key:{pane};wait:8;move:{at};wait:3;{at};wait:6;r:{at};wait:6;key:{pane};wait:8;{at};wait:25";
            int frames = 10 + steps.Split(';').Sum(s => s.StartsWith("wait:", StringComparison.Ordinal) ? int.Parse(s[5..]) : 1) + 5;
            string tape = Path.Combine(dir, "nobodys.tape");
            var env = new Dictionary<string, string> { ["VITOMIR_USERDATA"] = Path.Combine(dir, "user") };
            Directory.CreateDirectory(env["VITOMIR_USERDATA"]);
            var (code, output) = ReplayBaselineTests.RunWith(env, app, game,
                "--level", "P1_OLDT", "--player", "--hud", "--no-scripts", "--mute", "--frame-ms", "33", "--seed", "1",
                "--size", $"{w}x{h}", "--ui-layout", layout, "--frames", $"{frames}", "--no-log-file", "--pointer-log",
                "--camera-pitch", "-70", "--camera-yaw", "270", "--ui-click", steps, "--record", tape);
            Assert.True(code == 0, output);

            // What the pointer and the world said, in order: the lines of the pointer's log at this pixel, and every
            // order of the left button's ("click:") and the right's ("cast:").
            var said = output.Split('\n').Select(l => l.Trim())
                             .Where(l => l.StartsWith("pointer:", StringComparison.Ordinal) && l.Contains($" {at} ", StringComparison.Ordinal)
                                         || l.StartsWith("click:", StringComparison.Ordinal) || l.StartsWith("cast:", StringComparison.Ordinal)
                                         || l.StartsWith("inv:", StringComparison.Ordinal))
                             .ToList();
            // A line said twice running is one thing said once: the hover is said again whenever the pointer comes back
            // to the pixel, and on a real desktop the window can be sent a motion of the system's own pointer between the
            // harness's moves (seen on a Windows PC: "hover 0,0" in the middle, then this pixel's hover once more).
            said = said.Where((l, i) => i == 0 || l != said[i - 1]).ToList();
            // And a hover said again after a click or a cast, the same as the hover before it, is the pointer come back to the
            // pixel, not a new verdict (a Windows PC: the player's own pointer crossed the window between the click and the cast).
            string? lastHover = null;
            said = said.Where(l =>
            {
                if (!l.StartsWith("pointer: hover ", StringComparison.Ordinal)) return true;
                bool again = l == lastHover;
                lastHover = l;
                return !again;
            }).ToList();
            string all = string.Join(" | ", said);
            Assert.True(said.Count == 6, $"{all}\n{output}");
            // The pane open: the hover, the click and the cast agree that it is nobody's, and nothing follows any of them.
            Assert.Equal($"pointer: hover {at} is nobody's", said[0]);
            Assert.Equal($"pointer: click {at} is nobody's", said[1]);
            Assert.Equal($"pointer: cast {at} is nobody's", said[2]);
            // The pane closed: the hover and the click agree that it is the world's, and the click walks.
            Assert.Equal($"pointer: hover {at} is the world's", said[3]);
            Assert.Equal($"pointer: click {at} is the world's", said[4]);
            Assert.True(said[5].StartsWith("click: walking to", StringComparison.Ordinal)
                        || said[5].StartsWith("click: running to", StringComparison.Ordinal), all);

            // And it replays the same.
            var (checkedCode, checking) = ReplayBaselineTests.RunWith(env, app, game, "--replay-check", tape, "--mute", "--no-log-file");
            Assert.True(checkedCode == 0 && checking.Contains("replay-check: the same"), ReplayBaselineTests.Checked(checking));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
