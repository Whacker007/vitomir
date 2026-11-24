// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How the interface's 640x480 sheet is laid on the picture (ours): 1.1's stretch, fitted in its own
// proportions, or (the default) in 16:9's proportions on every picture, the panes above
// the status bar centred in the rows above it.
using Vitomir.Engine.Diagnostics;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>How the interface's sheet is laid on the picture.</summary>
public enum UiLayoutMode
{
    /// <summary>1.1's: the 640x480 sheet pulled to the picture's shape, each axis by itself.</summary>
    Stretch,
    /// <summary>Ours: the sheet in its own proportions, as large as fits the picture, centred.</summary>
    Fit,
    /// <summary>
    /// Ours, the default: **the 16:9 stretch**, on every picture: a sheet
    /// unit is 4 wide by 3 tall whatever the picture's shape. On a 16:9 picture it is <see cref="Stretch"/>, to the
    /// float; on a wider one the sheet is as tall as the picture and 16:9's width, centred across; on a narrower one
    /// (4:3, 5:4, 16:10) it is as wide as the picture and 16:9's height, at the picture's bottom in play and centred
    /// where the sheet is the whole screen (<see cref="SheetSeat"/>); and the panes that sit
    /// above the status bar are centred in the rows above it there (<see cref="UiLayout.Panes(float, float)"/>).
    /// </summary>
    Wide,
    /// <summary>
    /// **A recording's only**: the 16:9 stretch as the earlier builds had it, 1.1's stretch on every picture up to
    /// 16:9 and <see cref="Wide"/> beyond. A tape those builds recorded says <c>layout=wide</c> and meant this, so it
    /// replays in it (<see cref="FromTape"/>); no setting names it and nothing records in it.
    /// </summary>
    WideOneOneBelow16By9,
    /// <summary>
    /// **A recording's only**: the 16:9 stretch as before, <see cref="Wide"/> but for the panes above the
    /// status bar, which ride the sheet wherever it sits (on the picture's bottom edge in play, the black above them)
    /// and are not centred above the bar. A tape an earlier build recorded says <c>layout=wide sheet=16x9</c> without
    /// <c>panes=centred</c> and meant this, so it replays in it (<see cref="UiLayout.FromTape"/>); no setting names
    /// it and nothing records in it. The same as <see cref="Wide"/> on a 16:9 picture and on a wider one.
    /// </summary>
    WidePanesOnSheet,
}

/// <summary>
/// Where a sheet shorter than the picture sits (ours, <see cref="UiLayoutMode.Wide"/> on a picture narrower than
/// 16:9, the only case there is): whoever owns the frame says which (<see cref="UiLayout.Seat"/>).
/// </summary>
public enum SheetSeat
{
    /// <summary>Wherever the world shows: the sheet on the picture's bottom edge, the status bar with it, the world above the bar.</summary>
    Bottom,
    /// <summary>
    /// Wherever the sheet is the whole screen (the front end, a film, a full-screen menu in a game): centred, an equal
    /// black band above and below.
    /// </summary>
    Centre,
}

/// <summary>
/// **The interface's sheet on the picture** (ours). 1.1 draws its whole interface in one 640x480 space and pulls that to the
/// screen's shape, both axes independently, so on a 16:9 picture every pane, portrait and
/// letter is a third wider than it was drawn. <see cref="UiLayoutMode.Fit"/> keeps the sheet's proportions:
/// one scale for both axes, the largest at which the sheet fits, and the sheet centred — on 16:9 a sheet of
/// 4:3 in the middle with the world seen either side of it. On a 4:3 picture the two are the same thing.
///
/// **Presentation, each machine's own**: no rule reads where a pane is drawn. But a click is placed by it, so
/// a recording carries the layout it was made in (<c>interface layout=…</c> on the tape) and replays in it;
/// a recording from before the line was written was made stretched.
///
/// Everything that maps the sheet to the picture or back asks here: the interface renderer's quads
/// (<c>UiCanvas.Begin</c>, the draw list's space), the party heads' and the portrait's viewports, and the pointer
/// (<see cref="StatusBar.FromWindow"/>). **Keep it that way** — a pane that works out its own place on the
/// screen is one this has to find later. The fitted sheet is the default
/// (it can be changed later); 1.1's stretch is the setting <c>[interface] layout =
/// stretch</c> or <c>--ui-layout stretch</c>.
///
/// **The default is <see cref="UiLayoutMode.Wide"/>**, the 16:9 stretch. 1.1's
/// status bar art is drawn for the stretch on a 16:9 screen (its discipline and potion circles are ovals about 3:4 in
/// sheet units and come out round when the sheet is widened by 4/3), so fitted on 16:9 the bar looked squashed from the
/// sides. Up to 16:9 the sheet is 1.1's stretch — 4:3, 16:10 and 16:9 then look as the GOG and Steam 1.1 show them —
/// and on a wider picture (21:9, a 7680x1440 Surround desktop) it is 16:9's stretch in the middle, the world either side
/// of it. <c>fit</c> and <c>stretch</c> stay as settings. **Code that branches asks what the map is on the picture**
/// (<see cref="Stretched"/>, <see cref="Overlays"/>), never the mode by itself.
///
/// **16:9's proportions on every picture.** *"I prefer the rounded
/// circles UI version … for anything that's a 4:3 resolution, is actually fit the stretched rounded interface version
/// to the bottom of the screen. It will be a bit shorter than in the base game because it's wider and not cover as much
/// of the screen, but that's fine."* On a picture narrower than 16:9 the wide sheet is the picture's width and
/// <c>width · 9/16</c> tall, so the circles are round there too; it sits at the bottom in play, the world drawn in all
/// the rows above the bar (<see cref="WorldShare"/>), and centred on black where the sheet is the whole screen, in a
/// game or not (<see cref="Seat"/>, <see cref="InGame"/>). 1.1's own oval look at 4:3 is the setting
/// <c>stretch</c> now.
///
/// **The panes above the bar are centred in the rows above it.** *"Having them bottomed on the
/// screen looks a bit odd to my eye. So have them centered relative to the empty space above the UI bar, and have the
/// background as black."* On that same narrower picture the status bar keeps the bottom edge, and the pack, the
/// character sheet, the quests, the disciplines, the vault, the shop and the advancement screen's panes have a map of
/// their own (<see cref="Panes(float, float)"/>): the sheet's, lifted so that a pane's art stands midway between the
/// picture's top and the bar's top edge. **Two maps, both decided here**; a pass or a hit test asks for the one its
/// pane lives in and does no arithmetic of its own.
/// </summary>
public static class UiLayout
{
    /// <summary>The settings file's section and key: <c>[interface] layout = wide | fit | stretch</c>.</summary>
    public const string Section = "interface", Key = "layout";

    /// <summary>The layout when nothing says otherwise: the 16:9 stretch (the fitted sheet before).</summary>
    public const UiLayoutMode Default = UiLayoutMode.Wide;

    /// <summary>The layout in force. <see cref="Default"/> unless something says otherwise.</summary>
    public static UiLayoutMode Mode { get; set; } = Default;

    /// <summary>
    /// Where a sheet shorter than the picture sits (<see cref="SheetSeat"/>). **One fact, said by whoever owns
    /// the frame**: at the bottom wherever the world shows, centred on black wherever the sheet is the whole screen,
    /// in a game or not. The front end's run and the film state say <see cref="SheetSeat.Centre"/> (no
    /// world); the game's run says, once a frame after its input and before anything of that frame is drawn,
    /// <see cref="InGame"/> of whether a whole-sheet screen is up. <see cref="On(float, float)"/> is the one place that
    /// turns it into a map, and it only matters where the sheet is shorter than the picture.
    /// </summary>
    public static SheetSeat Seat { get; set; } = SheetSeat.Bottom;

    /// <summary>
    /// **The seat of a game's frame** (*"I prefer b actually."*): <see cref="SheetSeat.Centre"/>
    /// while a screen that is the whole sheet is up (<see cref="FrontEnd.WholeSheet"/>: Options and its pages, the save
    /// and load screens, the map), as in the front end; <see cref="SheetSeat.Bottom"/> for everything that leaves the
    /// world or the status bar showing (the bar, the panes, the shop, the advancement screen, a conversation, the escape
    /// menu, a message box, the loading plaque over the last frame).
    /// </summary>
    public static SheetSeat InGame(bool wholeSheetScreen) => wholeSheetScreen ? SheetSeat.Centre : SheetSeat.Bottom;

    /// <summary>
    /// Whether a picture is no wider than 16:9 **to the pixel**: narrower than 16:9 (<see cref="Below16By9"/>) or 16:9,
    /// which is whether 16:9's width at this height (<c>height * 16 / 9</c>) falls short of the picture's by less than one pixel.
    /// In whole products, <c>width * 9 &lt; height * 16 + 9</c>, so 1920x1080, 2560x1440, 1600x900 and 1280x720 are
    /// exactly 16:9 and not a float's error either side of it (the products are integers far below 2^24). The pixel's
    /// allowance takes in 1366x768, sold as 16:9 and a third of a pixel wider either side: stretched, it is 1/2049 wider
    /// than 16:9, where centring would leave a third of a pixel each side and move 1.1's clicks. Past it the sheet no
    /// longer reaches the sides by at least half a pixel each side, which is where <see cref="Overlays"/> begins.
    /// </summary>
    public static bool UpTo16By9(float width, float height) => width * 9f < height * 16f + 9f;

    /// <summary>
    /// Whether a picture is narrower than 16:9 **by a row or more**, where the wide sheet is shorter than the
    /// picture: whether 16:9's height at this width (<c>width * 9 / 16</c>) falls short of the picture's by a whole
    /// pixel or more. In whole products, <c>height * 16 &gt;= width * 9 + 16</c>: 1024x768, 1280x1024, 1280x800 and
    /// 1680x1050 are, 1920x1080 and 1366x768 are not (neither this nor wider: 16:9 to the pixel, 1.1's stretch to the
    /// float), and 1360x768, sold as 16:9, is by three rows.
    /// </summary>
    public static bool Below16By9(float width, float height) => height * 16f >= width * 9f + 16f;

    /// <summary>
    /// The sheet on a picture: a sheet unit is <see cref="ScaleX"/> by <see cref="ScaleY"/> pixels, and the
    /// sheet's top left corner is at (<see cref="X"/>, <see cref="Y"/>) of the picture.
    /// </summary>
    public readonly record struct Map(float ScaleX, float ScaleY, float X, float Y)
    {
        /// <summary>The sheet's bottom edge on the picture: where a pane's y = 0 is.</summary>
        public float Bottom => Y + UiAssets.Height * ScaleY;
    }

    /// <summary>The sheet on a picture of this size, in the layout and the seat in force.</summary>
    public static Map On(float width, float height) => On(width, height, Mode, Seat);

    /// <summary>The same in a layout named, seated at the bottom (a test's, which puts none in force).</summary>
    public static Map On(float width, float height, UiLayoutMode mode) => On(width, height, mode, SheetSeat.Bottom);

    public static Map On(float width, float height, UiLayoutMode mode, SheetSeat seat)
    {
        width = Math.Max(1f, width);
        height = Math.Max(1f, height);
        float sx = width / UiAssets.Width, sy = height / UiAssets.Height;
        if (mode == UiLayoutMode.Stretch) return new Map(sx, sy, 0f, 0f);
        if (IsWide(mode))
        {
            // Narrower than 16:9: the sheet as wide as the picture and 16:9's height (width * 9/16),
            // so its unit is 4 by 3 as at 16:9: three quarters as tall as it is wide, exactly. What is left of the
            // picture's height is above the sheet in play and shared above and below it where it is the whole screen.
            if (Shorter(width, height, mode))
            {
                float tall = sx * 0.75f, room = height - UiAssets.Height * tall;
                return new Map(sx, tall, 0f, seat == SheetSeat.Centre ? room / 2f : room);
            }
            // 16:9 to the pixel (and, as recorded, everything narrower): 1.1's stretch, the same floats as
            // Stretch's, so everything drawn or clicked is the same.
            if (UpTo16By9(width, height)) return new Map(sx, sy, 0f, 0f);
            // Wider: the sheet as tall as the picture and 16:9's width (height * 16/9), centred across. Its unit is
            // (height * 16/9) / 640 = height / 360 pixels wide, worked out so (on 1440 rows, exactly 4).
            float wide = height / 360f;
            return new Map(wide, sy, (width - UiAssets.Width * wide) / 2f, 0f);
        }
        float s = Math.Min(sx, sy);
        return new Map(s, s, (width - UiAssets.Width * s) / 2f, (height - UiAssets.Height * s) / 2f);
    }

    /// <summary>The 16:9 stretch, as it is or as a recording has it (<see cref="UiLayoutMode.WideOneOneBelow16By9"/>, <see cref="UiLayoutMode.WidePanesOnSheet"/>).</summary>
    static bool IsWide(UiLayoutMode mode) => mode is UiLayoutMode.Wide or UiLayoutMode.WideOneOneBelow16By9 or UiLayoutMode.WidePanesOnSheet;

    /// <summary>
    /// Whether the sheet in this layout is **shorter than the picture**: the 16:9 stretch on a picture narrower than
    /// 16:9, and not as recorded.
    /// </summary>
    static bool Shorter(float width, float height, UiLayoutMode mode) =>
        mode is UiLayoutMode.Wide or UiLayoutMode.WidePanesOnSheet && Below16By9(Math.Max(1f, width), Math.Max(1f, height));

    /// <summary>
    /// **The lowest unit of a pane that sits above the status bar** (the sheet's units, y up): 128, the status bar's
    /// own top edge beside its message strip. Every such pane's art is the 352 units from there to the sheet's top
    /// (<c>inventoryPane</c>, <c>characterPane</c>, <c>questPane</c>, <c>disciplinePane</c>, <c>VaultPane</c>,
    /// <c>buySellPane</c>: six quads each, tiling 320 by 352), with one small piece more, 16 by 8 from 120 to 128, that
    /// fills the notch at the end of the bar's strip and is not counted here.
    /// </summary>
    public const float PaneBottom = 128f;

    /// <summary>
    /// **The top edge of the bar the panes stand above**, in the sheet's units up from its bottom (ours): the
    /// status bar's 128 (<see cref="PaneBottom"/>: a pane stands on it), or the advancement screen's own bar's 160,
    /// whose upper band lies over the panes' lowest 32 units on 1.1's sheet. **One fact, said by whoever owns the
    /// frame, with <see cref="Seat"/>** (<c>ClassicGame.SeatFrame</c>); <see cref="Panes(float, float)"/> is the one
    /// place that reads it, and it matters only where the sheet is shorter than the picture.
    /// </summary>
    public static float BarTop { get; set; } = PaneBottom;

    /// <summary>
    /// **The map of the panes that sit above the status bar** (ours): the pack, the character sheet
    /// and its portrait, the quests, the disciplines, the vault, the shop's pane and the advancement screen's two.
    /// **Everything of such a pane goes through it**: its pass's quads (<c>UiCanvas.BeginPanes</c>), the portrait's
    /// viewport, and the pointer on it (<see cref="StatusBar.PaneFromWindow(float, float, int, int)"/>). The status
    /// bar, the description boxes in it, the cursor and everything else keep the sheet's map
    /// (<see cref="On(float, float)"/>).
    /// <para>The sheet's own map everywhere but the 16:9 stretch on a picture narrower than 16:9 with the sheet on the
    /// bottom edge (in play). There the bar keeps the bottom edge and **a pane's 352 units of art are centred in the
    /// rows above the bar**: as many rows of black above the pane, to the picture's top, as below it, to the bar's top
    /// edge (<see cref="BarTop"/> units up the bottom-seated sheet). The same scale and the same columns as the
    /// sheet; only its top row moves, to <c>(height − (BarTop + 352) · ScaleY) / 2</c>. Above the status bar (128)
    /// that is <c>(height − 480 · ScaleY) / 2</c>, which is <see cref="SheetSeat.Centre"/>'s map to the float: at
    /// 1024x768, 96 rows above the pane and 96 below. Above the advancement screen's bar (160), 16 units higher.</para>
    /// <para>As an earlier build recorded it (<see cref="UiLayoutMode.WidePanesOnSheet"/>) the panes ride the sheet where it sits. With
    /// the sheet seated in the centre (a whole-sheet screen up, the front end's character screens) they ride it too.</para>
    /// </summary>
    public static Map Panes(float width, float height) => Panes(width, height, Mode, Seat, BarTop);

    /// <summary>The same above the status bar, in a layout and a seat named.</summary>
    public static Map Panes(float width, float height, UiLayoutMode mode, SheetSeat seat) => Panes(width, height, mode, seat, PaneBottom);

    public static Map Panes(float width, float height, UiLayoutMode mode, SheetSeat seat, float barTop)
    {
        var sheet = On(width, height, mode, seat);
        if (mode != UiLayoutMode.Wide || seat != SheetSeat.Bottom || !Shorter(width, height, mode)) return sheet;
        if (barTop == PaneBottom) return On(width, height, mode, SheetSeat.Centre);
        return sheet with { Y = (sheet.Bottom - (barTop + UiAssets.Height - PaneBottom) * sheet.ScaleY) / 2f };
    }

    /// <summary>
    /// Whether the panes' map is not the sheet's on this picture now (<see cref="Panes(float, float)"/>): the 16:9
    /// stretch on a picture narrower than 16:9, with the sheet at the bottom.
    /// </summary>
    public static bool PanesLifted(float width, float height) => Panes(width, height) != On(width, height);

    /// <summary>
    /// Whether <see cref="Seat"/> places the sheet on this picture at all: only where the sheet in the layout in force
    /// is shorter than the picture. Everywhere else the two seats are one map, and nothing that follows the seat (the
    /// black bands of a centred sheet, <c>MenuPass</c>) may draw.
    /// </summary>
    public static bool Seated(float width, float height) => Seated(width, height, Mode);

    public static bool Seated(float width, float height, UiLayoutMode mode) => Shorter(width, height, mode);

    /// <summary>
    /// **The share of the picture's height the world is drawn into, from the top**, when the sheet's own share is
    /// <paramref name="sheetShare"/> (<see cref="StatusBar.WorldShare"/> with the status bar up, 1 without it). Where the
    /// sheet is as tall as the picture that is the same number, to the float (1.1's, and every layout's until then).
    /// Where the sheet is shorter than the picture and sits at its bottom (the 16:9 stretch on a picture narrower than
    /// 16:9, in play) the world has **all the picture above the bar**: down to the same line of the sheet, which is
    /// further down the picture, so more rows than 1.1 shows there, at the same scale. Fitted on a picture taller than
    /// 4:3 the share stays the picture's, as it has been.
    /// </summary>
    public static float WorldShare(float sheetShare, float width, float height) => WorldShare(sheetShare, width, height, Mode, Seat);

    public static float WorldShare(float sheetShare, float width, float height, UiLayoutMode mode, SheetSeat seat)
    {
        if (sheetShare >= 1f || !Shorter(width, height, mode)) return sheetShare;
        var map = On(width, height, mode, seat);
        return (map.Y + sheetShare * UiAssets.Height * map.ScaleY) / Math.Max(1f, height);
    }

    /// <summary>
    /// Whether the sheet lies **over** the world and not beside it: wherever the sheet does not reach the picture's
    /// sides — fitted on a picture wider than 4:3, or the 16:9 stretch on a picture wider than 16:9 — so the status
    /// bar does not either. (Narrower than 16:9 the wide sheet reaches both sides, so not there: the bar's rows are the
    /// bar's, as in 1.1.) The world is then drawn to the picture's bottom edge, so what shows beside the bar is
    /// world. (1.1 gives the bar's rows to the bar and draws the world above them; where the sheet spans the
    /// picture, stretched, that is kept.)
    /// </summary>
    public static bool Overlays(float width, float height) => Overlays(width, height, Mode);

    public static bool Overlays(float width, float height, UiLayoutMode mode) =>
        mode != UiLayoutMode.Stretch && On(width, height, mode).X >= 0.5f;

    /// <summary>
    /// Whether the sheet on this picture is **1.1's stretch**, so 1.1's own pixel arithmetic applies to the letter
    /// (<see cref="StatusBar.FromWindow"/>, <see cref="CentreLook.At"/>): stretched, or the 16:9 stretch on a 16:9
    /// picture (to the pixel; as recorded, on every picture up to 16:9). Not on a narrower picture,
    /// where the wide sheet is shorter than the picture. Not fitted, even on a 4:3 picture where the map is the same:
    /// the fitted pointer keeps its own rounding there, as it has, so a recording made fitted replays as it
    /// was made.
    /// </summary>
    public static bool Stretched(float width, float height) => Stretched(width, height, Mode);

    public static bool Stretched(float width, float height, UiLayoutMode mode) =>
        mode == UiLayoutMode.Stretch
        || (IsWide(mode) && UpTo16By9(Math.Max(1f, width), Math.Max(1f, height)) && !Shorter(width, height, mode));

    /// <summary>
    /// A picture's pixel as a point of the sheet, y up from the sheet's bottom, not rounded: left of the sheet
    /// it is negative, right of it past 640. Stretched, it is 1.1's own conversion before its truncation.
    /// </summary>
    public static (float X, float Y) FromPicture(float px, float py, float width, float height) =>
        FromPicture(px, py, On(width, height));

    public static (float X, float Y) FromPicture(float px, float py, float width, float height, UiLayoutMode mode) =>
        FromPicture(px, py, On(width, height, mode));

    /// <summary>The same through a map already worked out (a layout and a seat named: <see cref="On(float, float, UiLayoutMode, SheetSeat)"/>).</summary>
    public static (float X, float Y) FromPicture(float px, float py, Map map) =>
        ((px - map.X) / map.ScaleX, (map.Bottom - py) / map.ScaleY);

    /// <summary>A point of the sheet as the picture's pixel (the other way).</summary>
    public static (float X, float Y) ToPicture(float x, float y, float width, float height) =>
        ToPicture(x, y, On(width, height));

    public static (float X, float Y) ToPicture(float x, float y, float width, float height, UiLayoutMode mode) =>
        ToPicture(x, y, On(width, height, mode));

    /// <summary>The same through a map already worked out.</summary>
    public static (float X, float Y) ToPicture(float x, float y, Map map) =>
        (map.X + x * map.ScaleX, map.Bottom - y * map.ScaleY);

    /// <summary>A layout by its name in the settings (<c>wide</c>, <c>fit</c>, <c>stretch</c>); null for anything else.</summary>
    public static UiLayoutMode? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "wide" => UiLayoutMode.Wide,
        "fit" => UiLayoutMode.Fit,
        "stretch" => UiLayoutMode.Stretch,
        _ => null,
    };

    public static string Name(UiLayoutMode mode) => mode switch
    {
        UiLayoutMode.Stretch => "stretch",
        UiLayoutMode.Wide or UiLayoutMode.WideOneOneBelow16By9 or UiLayoutMode.WidePanesOnSheet => "wide",
        _ => "fit",
    };

    /// <summary>
    /// The tape's second word for the 16:9 stretch: <c>sheet=16x9</c> after <c>layout=wide</c>, the sheet in
    /// 16:9's proportions on every picture. A tape that says <c>layout=wide</c> without it was recorded or
    /// Where a picture narrower than 16:9 had 1.1's stretch (<see cref="UiLayoutMode.WideOneOneBelow16By9"/>).
    /// </summary>
    public const string TapeSheetKey = "sheet", TapeSheet16By9 = "16x9";

    /// <summary>
    /// The tape's third word for the 16:9 stretch: <c>panes=centred</c> after <c>sheet=16x9</c>, the panes
    /// above the bar centred in the rows above it on a picture narrower than 16:9 (<see cref="Panes(float, float)"/>).
    /// A tape that says <c>sheet=16x9</c> without it was recorded, whose panes rode the bottom-seated sheet
    /// (<see cref="UiLayoutMode.WidePanesOnSheet"/>).
    /// </summary>
    public const string TapePanesKey = "panes", TapePanesCentred = "centred";

    /// <summary>
    /// A recording's words for the layout it is made in, for its <c>interface</c> line: <c>layout=…</c>, and for the
    /// 16:9 stretch <c>sheet=16x9</c> and <c>panes=centred</c>. A build earlier passes over the
    /// second word and replays such a tape in its own <c>wide</c>: the same thing on a 16:9 picture and on a wider one,
    /// 1.1's stretch on a narrower one, where this build's sheet is shorter (its clicks then land elsewhere). An earlier build
    /// passes over the third and replays with its panes on the bottom-seated sheet: the same on a 16:9 picture and
    /// wider, and on a narrower one wherever no pane is clicked.
    /// </summary>
    public static string[] TapeFields(UiLayoutMode mode) => mode switch
    {
        UiLayoutMode.Wide => [$"{Key}={Name(mode)}", $"{TapeSheetKey}={TapeSheet16By9}", $"{TapePanesKey}={TapePanesCentred}"],
        UiLayoutMode.WidePanesOnSheet => [$"{Key}={Name(mode)}", $"{TapeSheetKey}={TapeSheet16By9}"],
        _ => [$"{Key}={Name(mode)}"],
    };

    /// <summary>
    /// The layout a recording was made in, from its <c>interface</c> line's <c>layout</c> and <c>sheet</c> words (null
    /// for a word it does not have): **the old word read as it was meant**. <c>wide</c> with <c>sheet=16x9</c> is the
    /// 16:9 stretch as it is now; <c>wide</c> alone is the earlier builds' (<see cref="UiLayoutMode.WideOneOneBelow16By9"/>), the
    /// same on a 16:9 picture and wider, 1.1's stretch on a narrower one, so such a tape replays as it was recorded
    /// on every picture. Likewise <c>wide</c> with <c>sheet=16x9</c> and no <c>panes=centred</c> is the earlier build's
    /// (<see cref="UiLayoutMode.WidePanesOnSheet"/>: its panes on the bottom-seated sheet), and replays as it was recorded.
    /// Null for a layout word that is none of the three (the caller's default stands).
    /// </summary>
    public static UiLayoutMode? FromTape(string? layout, string? sheet, string? panes) => Parse(layout) switch
    {
        UiLayoutMode.Wide when sheet != TapeSheet16By9 => UiLayoutMode.WideOneOneBelow16By9,
        UiLayoutMode.Wide when panes != TapePanesCentred => UiLayoutMode.WidePanesOnSheet,
        var mode => mode,
    };

    /// <summary>The words a complaint names.</summary>
    const string Words = "wide, fit nor stretch";

    /// <summary>
    /// Reads the layout the player asked for and puts it in force: <c>--ui-layout</c> on the command line,
    /// else <c>[interface] layout</c> of the player's settings file, else the 16:9 stretch. **Only the program
    /// calls this.** A word that is none of the three is complained about and passed over.
    /// </summary>
    public static UiLayoutMode Configure(string? flag, Action<string>? complain = null, string? settingsPath = null) =>
        Mode = Asked(flag, complain, settingsPath);

    /// <summary>What <see cref="Configure"/> would put in force, and nothing put in force.</summary>
    public static UiLayoutMode Asked(string? flag, Action<string>? complain = null, string? settingsPath = null)
    {
        UiLayoutMode mode = Default;
        string path = settingsPath ?? Diag.UserSettingsPath;
        if (File.Exists(path) && IniFile.Read(path)[Section + "." + Key] is { Length: > 0 } kept)
        {
            if (Parse(kept) is { } m) mode = m;
            else complain?.Invoke($"[{Section}] {Key} = {kept} is neither {Words}; {Name(mode)}");
        }
        if (flag is not null)
        {
            if (Parse(flag) is { } m) mode = m;
            else complain?.Invoke($"--ui-layout {flag} is neither {Words}; left as {Name(mode)}");
        }
        return mode;
    }
}
