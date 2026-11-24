// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The single-player status bar, as state rather than as pixels: where each control sits this frame,
// whether it is up, what it holds, and what a click on it comes to. `Vitomir.Render.StatusBarPass`
// draws whatever this decides, so everything here is testable without a GPU — which matters,
// because the layout is 1.1's formula and a formula deserves a test.
//
// The whole file follows the original status bar: how the pane is built, how the rows are laid out,
// how the tracks are chosen and set, and the pane's own hit test. Each of those is described
// where it is used.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// A pane's control as the engine carries it: the `.nui` line it came from, where it has been moved
/// to, and the state the game has put in it. The engine keeps all of this on the control itself
/// (a position, a visible bit, a progress bar's value
/// and range), so this does too.
/// </summary>
public sealed class UiControl(NuiElement element)
{
    public NuiElement Element { get; } = element;
    public string Name => Element.Name;
    public string Type => Element.Type;

    /// <summary>Where the control is now — its declared `pos` until something moves it.</summary>
    public int X { get; set; } = element.Pos.Length > 0 ? element.Pos[0] : 0;
    public int Y { get; set; } = element.Pos.Length > 1 ? element.Pos[1] : 0;
    public int Width { get; } = element.Size.Length > 0 ? element.Size[0] : 0;
    public int Height { get; } = element.Size.Length > 1 ? element.Size[1] : 0;

    /// <summary>
    /// Whether the control is up. The engine spells this the other way round — the control's bit
    /// `0x200` means <i>hidden</i>, which is why the routine that sets it is Hide and the inline
    /// that clears it is Show. Everything a pane declares starts up.
    /// </summary>
    public bool Shown { get; set; } = true;

    /// <summary>A checkbox's state, which is what puts its `checkedbitmap` on screen.</summary>
    public bool Checked { get; set; }

    /// <summary>A button's state while the mouse is held on it, which puts its `depressed` art up.</summary>
    public bool Pressed { get; set; }

    /// <summary>Drawn greyed: up, but with nothing to do (ours; the multiplayer bar's Storyteller button in a game made without
    /// Storyteller moderation). Its click still says why.</summary>
    public bool Greyed { get; set; }

    /// <summary>The control's `flash` material, drawn over it while it blinks.</summary>
    public string? Flash => Element.Get("flash");

    /// <summary>Whether it is blinking at all (bit <c>0x200000</c>).</summary>
    public bool Blinking { get; private set; }
    /// <summary>Whether this is the half of the blink with the flash drawn.</summary>
    public bool FlashOn { get; private set; }
    int blinkUntil, blinkPeriod, blinkElapsed;

    /// <summary>
    /// Whether the control has a bitmap of its own, which is what a blink and a draw
    /// both test. A `bitmap` (or the art its state picks) gives it one; so does **flag 0x2**, with
    /// no file at all: the base setup copies the parent's artwork under the control into
    /// a texture of its own, and a checkbox keeps that copy as its unchecked
    /// look. Every button on the status bar carries flag 0x2.
    /// </summary>
    public bool HasBitmap => Art is not null || (Element.Flags & 0x2) != 0;

    /// <summary>
    /// Starts a blink: the flash on and off every <paramref name="periodMs"/> for
    /// <paramref name="forMs"/>. Only a control with a bitmap of its own and a `flash` can blink.
    /// The quest button has both whether its pane is up or not — its bitmap when shut is the copy of
    /// the bar's artwork that flag 0x2 makes — so it blinks with the pane closed, as a video of
    /// 1.1 shows. This used to read the other way: *"an unchecked pane button never
    /// blinks"*, which missed the copy.
    /// </summary>
    public void StartBlink(int periodMs, int forMs, int nowMs)
    {
        if (!HasBitmap || Flash is null || forMs == 0) return;
        blinkElapsed = 0;
        blinkPeriod = periodMs;
        blinkUntil = nowMs + forMs;
        FlashOn = true;
        Blinking = true;
    }

    /// <summary>Stops it.</summary>
    public void StopBlink()
    {
        if (!Blinking) return;
        FlashOn = false;
        Blinking = false;
    }

    /// <summary>A frame of the blink: the frame's time runs up, and past the period the flash flips.</summary>
    public void TickBlink(int nowMs, int frameMs)
    {
        if (!Blinking) return;
        if ((uint)nowMs < (uint)blinkUntil)
        {
            blinkElapsed += frameMs;
            if (blinkElapsed <= blinkPeriod) return;
            blinkElapsed = 0;
            FlashOn = !FlashOn;
        }
        else { FlashOn = false; Blinking = false; }
    }

    /// <summary>Which of an `imagearray`'s `image0`..`image9` is chosen.</summary>
    public int Image { get; set; }

    /// <summary>A progress bar's range. 0 to 100 is what the constructor leaves.</summary>
    public float Min { get; set; }
    /// <inheritdoc cref="Min"/>
    public float Max { get; set; } = 100f;
    /// <summary>A progress bar's value, clamped into the range the way 1.1 clamps it.</summary>
    public float Value { get; set; }

    /// <summary>How full the bar is, 0 to 1.</summary>
    public float Fraction => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0f, 1f) : 0f;

    /// <summary>Which party row this control belongs to, or −1. The engine keeps it at `control+4`.</summary>
    public int Row { get; set; } = -1;

    /// <summary>The character the row is about, when the row has one.</summary>
    public Actor? Actor { get; set; }

    /// <summary>The bitmap this control wants on screen now, or null for nothing of its own.</summary>
    public string? Art => Type switch
    {
        "imagearray" => Element.Get($"image{Image}") ?? Element.Get("image0"),
        "checkbox" when Checked => Element.Get("checkedbitmap"),
        "button" when Pressed => Element.Get("depressed"),
        _ => Element.Get("bitmap"),
    };

    /// <summary>
    /// The engine's own rect test, inclusive at both edges — so a control
    /// of width `w` answers for `w + 1` columns. Copied rather than corrected.
    /// </summary>
    public bool Contains(int px, int py) => X - Reach <= px && px <= X + Width + Reach && Y <= py && py <= Y + Height + ReachUp;

    /// <summary>
    /// How far past its sides (each) and its top the control answers a click, beyond its declared size: 0, as 1.1's, but for
    /// a portrait's hotspot under <c>partyorder</c> (<see cref="StatusBar.HeadReach"/>).
    /// </summary>
    public int Reach { get; set; }

    /// <inheritdoc cref="Reach"/>
    public int ReachUp { get; set; }

    /// <summary>Moves the control, the way 1.1 does, and reports the delta for its children.</summary>
    public (int Dx, int Dy) MoveTo(int x, int y)
    {
        var d = (x - X, y - Y);
        (X, Y) = (x, y);
        return d;
    }

    public void Move(int dx, int dy) { X += dx; Y += dy; }

    /// <summary>A progress bar's range and value together (`SetRange` then `SetValue`).</summary>
    public void Set(float min, float max, float value)
    {
        (Min, Max, Value) = (min, max, value);
        Shown = true;
    }
}

/// <summary>What a click on the bar came to: whether the world should be left alone, and what happened.</summary>
public readonly record struct UiClick(bool Consumed, string Did);

/// <summary>
/// `ui/statusBarSP.nui` with the game's state in it. Nothing here hard-codes a position: every
/// control is found by the name the pane file gives it, so a mod that moves one moves it here too.
/// </summary>
public sealed class StatusBar
{
    public const string PaneName = "statusBarSP";

    /// <summary>The four party slots the pane declares, which is `MAX_PARTY_SIZE`.</summary>
    public const int Rows = 4;

    /// <summary>
    /// One cell of any grid in the game, in the UI's 640x480 space. Every `icongrid` and `weargrid`
    /// in every shipped pane agrees on it: the backpack is `size=270,128 gridsize=10,4`, the body
    /// slot `54,96` over `2,3`, the neck `27,32` over `1,1`, and the status bar's quick-use strip
    /// `135,32` over `5,1`. **27 x 32, everywhere.**
    ///
    /// The art does not match it — `swordBroadIcon` is 64 x 128 for an item that is 2 x 3 cells,
    /// which is 32 x 42.7 a cell — so an icon is drawn **stretched into its cells** rather than at
    /// its own size. That falls out of the panes' own numbers rather than being a choice.
    /// </summary>
    public const int CellWidth = 27, CellHeight = 32;

    /// <summary>
    /// The row layout: the first row starts at <c>320 − width/2 − (count−1) × 40</c>
    /// and each row after it is 80 further right, which centres the block of rows on the screen. The
    /// indicator sits at y = 2 and the hotspot above it at y = 4. All four rows declare the same
    /// `pos` in the file precisely because this formula is what places them.
    /// </summary>
    public const int RowStep = 80, RowHalfStep = 40, RowY = 2, HotspotY = 4;

    /// <summary>
    /// **A portrait's click reaches its whole head** (ours, under <c>partyorder</c>): 1.1's hotspot is 64 x 70 from y = 4, but
    /// the head drawn over it stands up to y = 90 (the heads' viewport, <see cref="PartyHeads.ViewY"/>), so a click on the top
    /// of a head missed it. With the switch on, a hotspot answers up to y = 92, just under the bar's text box (y = 94), and
    /// 4 pixels further either side — still 8 short of the next row's. The ring round a carried portrait is drawn round the
    /// same, and the drop's places are measured by it. Whacker007, 2026-11-21: *"the area that the box covers to 'click' on
    /// the head needs to be a bit larger, mainly taller"*.
    /// </summary>
    public const int HeadReach = 4, HeadReachUp = 18;

    /// <summary>
    /// The row of the interface's 480 below which **the world is not drawn** while the bar is up,
    /// and the share of the height that leaves it.
    ///
    /// **Measured, not read.** Two 1.1 screenshots of the same spot, one with the bar and one
    /// without, taken at 1600x900: the whole picture sits **108.9 pixels higher** with the bar up,
    /// at **exactly the same scale** (a scale fit returns 1.00, a horizontal fit returns 0) and the
    /// two agree to within 0.8 grey levels everywhere above row 642. A pure translation with no
    /// scaling is what a shorter viewport gives when the pixels-per-unit is pinned by the width,
    /// which is the convention the head camera's projection states outright for the head camera. Solving for it:
    /// the viewport's centre moves from 450 to 341.1, so the viewport is 682.3 of 900 rows, which
    /// is **364 of 480**.
    ///
    /// **It also explains the other half of the evidence, which is why it is believed.** The head
    /// rectangle is the one place the bar's artwork is transparent, and in 1.1 what shows through
    /// it is black — not because anything paints it, but because the world stops 364 rows down
    /// and the frame's own clear is what is left. Two measurements from two different screenshots,
    /// one mechanism.
    ///
    /// **The consequence is not cosmetic**: with the bar up, 1.1 shows *less* of every scene than a
    /// full-window view does — a quarter of the height less, cropped evenly top and bottom rather
    /// than hidden behind the bar. This tree showed the whole window until it was measured.
    ///
    /// The number is 23 rows below the bar's own top edge (480 − 139 = 341), which nothing yet
    /// explains; the world's own canvas is set somewhere in the world's renderer and is **not** the
    /// camera class the interface uses — that class's `SetCanvas` has five callers and all
    /// five are in the interface and its heads. Reading it is on the deferred list.
    /// </summary>
    public const float WorldBottom = 364f;

    /// <inheritdoc cref="WorldBottom"/>
    public const float WorldShare = WorldBottom / UiAssets.Height;

    Location place;

    /// <summary>
    /// The place whose party the bar shows. It follows the party from level to level — the bar is
    /// the interface's and outlives every place, as the original's does.
    /// </summary>
    public Location Place
    {
        get => place;
        set { place = value; Update(); }
    }
    readonly List<UiControl> controls = [];
    readonly Dictionary<string, UiControl> byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Actor> party = [];
    readonly List<WorldThing> partyThings = [];
    /// <summary>Each row's controls, gathered once — the backdrop, everything parented to it, and the hotspot.</summary>
    readonly List<UiControl>[] rows = new List<UiControl>[Rows];

    public NuiPane Pane { get; }

    /// <summary>Every control the pane declares, in the order it declares them.</summary>
    public IReadOnlyList<UiControl> Controls => controls;

    /// <summary>The party as the bar shows it, in party-slot order (<see cref="Location.PartyMembers"/>).</summary>
    public IReadOnlyList<Actor> Party => party;

    /// <summary>
    /// The same party, as things rather than actors and in the same order. The heads need the thing
    /// — an <see cref="Actor"/> does not know which one it belongs to — and this is the list the
    /// engine's own `vuiHeads::Draw` walks (`rpgParty`'s array, which is also what lays the rows
    /// out), so the head over a row is the character on it by construction rather than by two
    /// separate walks agreeing.
    ///
    /// **It is the same list object every frame**, rewritten in place by <see cref="Update"/>, so a
    /// caller can hold it once and see the party change.
    /// </summary>
    public IReadOnlyList<WorldThing> PartyThings => partyThings;

    /// <summary>
    /// The character the bar calls selected — the party's selected member (`party+4`), whose row is
    /// lit and whose stance the radios show. The steered one except while a
    /// possession lasts.
    /// </summary>
    public Actor? Selected { get; private set; }

    /// <summary>
    /// The character being steered, whose belt the quick-use strip shows and whose disciplines the
    /// six slots hold: taking over a character refills the panes from it (the pack, the disciplines
    /// and the bar's slots), so a possessed body shows its own. In 1.1
    /// a possessed Cappadocian shows the gems and the blood potion she had picked
    /// up; earlier, here, the bar showed Christof's.
    /// </summary>
    public Actor? Steered { get; private set; }

    /// <summary>What the last click did, for a caller that wants to say so.</summary>
    public string? Last { get; private set; }

    /// <summary>An item in a grid cell: where to draw it, and what it is.</summary>
    /// <param name="X">The cell's left edge in the pane's space.</param>
    /// <param name="Y">Its bottom edge.</param>
    /// <param name="Item">What is in it, or **null** for an empty cell — the strip has all five.</param>
    public readonly record struct UiCell(int X, int Y, int Width, int Height, Rpg.Item? Item);

    /// <summary>
    /// **All five** quick-use cells (`sbQuickuse`, `5 x 1`), in order, each carrying whatever is in
    /// it or null. The strip is the simple case of a grid: every quick item is one cell, so the items in
    /// <see cref="Rpg.Slot.QuickUse"/> fill it in inventory order — and the inventory pushes on the
    /// front, so the most recently put there is leftmost. How the **backpack** packs items of
    /// different sizes into its 10 x 4 is a different problem and is not read.
    /// </summary>
    public IReadOnlyList<UiCell> QuickUse => quick;
    readonly List<UiCell> quick = [];

    /// <summary>A discipline's icon in one of the bar's slots.</summary>
    /// <param name="Slot">0..5 for the six, or −1 for `sbCurrentDiscipline`.</param>
    /// <param name="Art">Which of the discipline's three bitmaps this slot wants — see <see cref="Slots"/>.</param>
    public readonly record struct UiPower(int Slot, int X, int Y, int Width, int Height,
                                          Rpg.Discipline Discipline, string Art);

    /// <summary>
    /// The suffix on the bitmap the <b>current</b> discipline's own quick slot draws — a copy of the
    /// icon with a red selection ring round it, authored at exactly a slot's 27 x 32
    /// (the slot's draw, which picks it when the slot's discipline is the actor's
    /// current one). The engine spells it in capitals and the shipped files in lower case; the
    /// texture search does not care.
    /// </summary>
    public const string SelectedSuffix = "_R";

    /// <summary>
    /// The suffix on the bitmap `sbCurrentDiscipline` draws, authored at exactly that control's
    /// 40 x 48. The discipline code builds the name as `%s_L` from the `icon` column while it is
    /// reading `game.ndd`, and asserts on it — so every discipline is guaranteed to have one.
    /// </summary>
    public const string LargeSuffix = "_L";

    /// <summary>
    /// The six quick discipline slots plus the current one. Unlike the item grid these are not
    /// cells of anything: the pane gives each slot its own `pos` and `size`, and a discipline knows
    /// which slot it is in rather than the slot knowing what is in it — which is
    /// exactly how 1.1 fills them, by walking the character's list and indexing the controls.
    /// </summary>
    public IReadOnlyList<UiPower> Powers => powers;
    readonly List<UiPower> powers = [];

    public StatusBar(NuiPane pane, Location place)
    {
        Pane = pane;
        this.place = place;
        foreach (var element in pane.Elements)
        {
            var control = new UiControl(element);
            controls.Add(control);
            if (control.Name.Length > 0) byName[control.Name] = control;
        }
        for (int row = 0; row < Rows; row++)
        {
            string backdrop = $"statusIndicators{row}";
            rows[row] = [.. controls.Where(c =>
                c.Name.Equals(backdrop, StringComparison.OrdinalIgnoreCase)
                || c.Name.Equals($"statusHotspot{row}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Element.Get("parent"), backdrop, StringComparison.OrdinalIgnoreCase))];
        }
        Update();
    }

    public UiControl? this[string name] => byName.GetValueOrDefault(name);

    /// <summary>
    /// A window pixel in the pane's own space: 640 x 480 with the origin at the bottom left. The
    /// engine's own conversion is the pane's hit test, which subtracts the pane's position and flips y
    /// against **479** rather than 480 — that one-pixel detail is the engine's, not a slip.
    /// </summary>
    public static (int X, int Y) FromWindow(float px, float py, int width, int height) =>
        FromWindow(px, py, width, height, UiLayout.Mode, UiLayout.Seat);

    /// <summary>The same in a layout named, not the one in force (a test's, which puts none in force), seated at the bottom.</summary>
    public static (int X, int Y) FromWindow(float px, float py, int width, int height, UiLayoutMode mode) =>
        FromWindow(px, py, width, height, mode, SheetSeat.Bottom);

    /// <summary>The same in a layout and a seat named (<see cref="UiLayout.Seat"/>: where a sheet shorter than the picture sits).</summary>
    public static (int X, int Y) FromWindow(float px, float py, int width, int height, UiLayoutMode mode, SheetSeat seat)
    {
        // 1.1's own, wherever the sheet is 1.1's stretch over the picture: stretched, or the 16:9 stretch on a 16:9
        // picture (UiLayout.Stretched). Kept to the letter: the recordings' clicks rest on it.
        if (UiLayout.Stretched(width, height, mode))
            return ((int)(px * UiAssets.Width / Math.Max(1, width)),
                    (UiAssets.Height - 1) - (int)(py * UiAssets.Height / Math.Max(1, height)));
        // Ours: the sheet fitted in its own proportions, the 16:9 stretch centred on a wider picture, or
        // the 16:9 stretch shorter than a narrower one (UiLayout). The same arithmetic about the sheet's own
        // corner, rounded down and not towards zero, so that a pixel just left of the sheet is off it (−1) and one
        // above it is too (480 and up: in play, the world's).
        var map = UiLayout.On(width, height, mode, seat);
        return ((int)MathF.Floor((px - map.X) / map.ScaleX),
                (UiAssets.Height - 1) - (int)MathF.Floor((py - map.Y) / map.ScaleY));
    }

    /// <summary>
    /// **A window pixel in the space of the panes that sit above the status bar** (ours): the same
    /// conversion through the panes' own map (<see cref="UiLayout.Panes(float, float)"/>). The same unit as
    /// <see cref="FromWindow(float, float, int, int)"/> wherever the panes ride the sheet — every layout and picture but
    /// the 16:9 stretch narrower than 16:9 in play, where the panes are centred above the bar and the bar is not.
    /// **Every hit test of the pack, the character sheet, the quests, the disciplines, the vault and the shop takes
    /// this one; the status bar's, the menus' and the boxes' take the sheet's.**
    /// </summary>
    public static (int X, int Y) PaneFromWindow(float px, float py, int width, int height) =>
        PaneFromWindow(px, py, width, height, UiLayout.Mode, UiLayout.Seat, UiLayout.BarTop);

    /// <summary>The same above the status bar, in a layout and a seat named.</summary>
    public static (int X, int Y) PaneFromWindow(float px, float py, int width, int height, UiLayoutMode mode, SheetSeat seat) =>
        PaneFromWindow(px, py, width, height, mode, seat, UiLayout.PaneBottom);

    /// <summary>The same above a bar whose top edge is <paramref name="barTop"/> units up the sheet (<see cref="UiLayout.BarTop"/>).</summary>
    public static (int X, int Y) PaneFromWindow(float px, float py, int width, int height, UiLayoutMode mode, SheetSeat seat, float barTop)
    {
        // Wherever the panes ride the sheet, the sheet's own conversion to the letter (1.1's where the sheet is 1.1's).
        var panes = UiLayout.Panes(width, height, mode, seat, barTop);
        if (panes == UiLayout.On(width, height, mode, seat)) return FromWindow(px, py, width, height, mode, seat);
        // Lifted above the bar: the same arithmetic about the panes' own corner, rounded down.
        return ((int)MathF.Floor((px - panes.X) / panes.ScaleX),
                (UiAssets.Height - 1) - (int)MathF.Floor((py - panes.Y) / panes.ScaleY));
    }

    /// <summary>
    /// Whether the cursor is on this pane at all, which is the whole of the question "does the world
    /// get this click". The hit test walks the panes and tests **the quads** when a pane has any and
    /// its controls when it has none; `statusBarSP` has seven quads, so the bar's shape is those
    /// seven rectangles and nothing else — a click in the top corners of the bar's own row, above
    /// the 128-pixel art, reaches the ground, and that is correct.
    /// </summary>
    public bool Covers(int x, int y)
    {
        if (Pane.Quads.Count > 0)
            return Pane.Quads.Any(q => Rect(q.Pos, q.Size, x, y));
        return controls.Any(c => c.Shown && c.Contains(x, y));
    }

    static bool Rect(int[] pos, int[] size, int x, int y)
    {
        int px = pos.Length > 0 ? pos[0] : 0, py = pos.Length > 1 ? pos[1] : 0;
        int w = size.Length > 0 ? size[0] : 0, h = size.Length > 1 ? size[1] : 0;
        return px <= x && x <= px + w && py <= y && y <= py + h;
    }

    /// <summary>
    /// The control under the cursor. The engine reaches the same answer by walking the control tree
    /// depth first and letting the last match win (each match overwrites the one before), which puts
    /// a child ahead of its parent — a click on a status bar lands on the bar, not on the row behind
    /// it. Hidden controls are skipped, as there.
    /// </summary>
    public UiControl? At(int x, int y)
    {
        UiControl? found = null;
        foreach (var control in controls)
            if (control.Shown && control.Width > 0 && control.Height > 0 && control.Contains(x, y))
                found = control;
        return found;
    }

    /// <summary>
    /// A click in the pane's space. `Consumed` says the world must be left alone, which is true for
    /// anywhere on the bar whether or not a control was hit — the original decides that at the pane
    /// level, before any control sees the click (the input runs the world only when no pane is under
    /// the cursor), and it is true of the right button as well.
    /// </summary>
    public UiClick Click(int x, int y, InputModifier mods = InputModifier.None, bool right = false)
    {
        if (!Covers(x, y)) return new UiClick(false, "");
        (LastX, LastY) = (x, y);
        var hit = At(x, y);
        Last = hit is null ? "the bar, no control" : Act(hit, mods, right);
        return new UiClick(true, Last);
    }

    /// <summary>
    /// What the bar's five pane buttons do. In the original each one puts its pane up; here the
    /// panes live in the renderer, so whoever owns the bar supplies this and gets the control's
    /// name. Anything it declines still reports itself as unwired, which is how the log said what
    /// was missing in the first place.
    /// </summary>
    public Func<string, string?>? Panes { get; set; }

    /// <summary>
    /// **Right**-clicking a quick-use item uses it, which is what an icon's second binding does
    /// everywhere. The handler is given the item under the point and answers what happened, so the
    /// using itself — the item code's path, `Rpg.Items.Use` — stays out of the UI.
    /// </summary>
    public Func<Rpg.Item, string>? UseQuick { get; set; }

    /// <summary>
    /// **Left**-clicking a quick-use cell picks the item up, or puts down what is carried — because
    /// the belt is an ordinary icon grid. The status bar gives `sbQuickUse` grid id `0x200` and
    /// registers the inventory drop on binding 0, the same handler every inventory grid gets, so
    /// the icon click and the drop run on it exactly as they do on the pack.
    ///
    /// **This was wrong until then and it was wrong the same way the right-click was.** Batch
    /// An earlier pass could not find the strip's handler, wired a left click to *use*, and that invented a
    /// gesture the original does not have while losing the one it does. The handler was there; it
    /// is registered two lines after the grid id.
    /// </summary>
    public Func<int, string>? TakeQuick { get; set; }

    /// <summary>The cell index under a point in the bar's space, or -1.</summary>
    public int QuickCellAt(int x, int y)
    {
        for (int i = 0; i < quick.Count; i++)
            if (x >= quick[i].X && y >= quick[i].Y
                && x < quick[i].X + quick[i].Width && y < quick[i].Y + quick[i].Height)
                return i;
        return -1;
    }

    /// <summary>The quick-use item under a point in the bar's space, or null.</summary>
    public Rpg.Item? QuickAt(int x, int y)
    {
        foreach (var cell in quick)
            if (x >= cell.X && y >= cell.Y && x < cell.X + cell.Width && y < cell.Y + cell.Height)
                return cell.Item;
        return null;
    }

    /// <summary>The multiplayer bar's Storyteller button, the host's (set by the network game; null elsewhere).</summary>
    public Func<string>? Storyteller { get; set; }

    string Act(UiControl hit, InputModifier mods, bool right) => hit.Name.ToLowerInvariant() switch
    {
        // Either coterie modifier gives the stance to the steered character alone: the bar tests
        // all four of the engine's shift and control flags before choosing which form of the stance's set
        // to call.
        "sbattackradio" => Stance(PartyStance.Attack, One(mods)),
        "sbneutralradio" => Stance(PartyStance.Neutral, One(mods)),
        "sbdefenseradio" => Stance(PartyStance.Defense, One(mods)),
        // The regroup button is two commands. Plain, it is regroup; with
        // **shift** held, or on the right button — the two the engine binds to disperse — it is
        // disperse, which is the opposite and which the shipped manual never mentions at all.
        // The left release is regroup (or disperse with shift), registered on the button's
        // event 1; the right button is its event 2, disperse.
        "sbregroupbutton" => right || mods.HasFlag(InputModifier.Shift) ? Disperse() : Regroup(),
        // The message box's scroll bar, and the slider on it, which the bar owns: the left button
        // only — the right has nothing bound on either (the bar's class has no second press).
        "sbscrollbar" or "sbscrollslider" => right ? $"{hit.Name} (the right button does nothing on it)"
            : Messages is { } box ? box.Press(LastX, LastY) : "the message box has no log",
        // The text box itself has no click of its own (its press is the base control's,
        // and the bar registers nothing on it).
        "sbtextbox" => "the message box (it has no click)",
        // The multiplayer bar's Storyteller button, when the click reaches the bar rather than the Storyteller's own hook (a
        // pane up over the world): the same button (ours). And its head, which 1.1 hooks for the rollover alone.
        "sbstbutton" => Storyteller?.Invoke() ?? "the Storyteller button (a network game's host only)",
        "sbheadhotspot" => "the portrait (its rollover only; 1.1 gives it no click)",
        _ => Quick(hit, mods, right) ?? Power(hit, right) ?? Panes?.Invoke(hit.Name) ?? Hotspot(hit, mods, right)
               ?? Formation(hit) ?? Bound(hit) ?? $"{hit.Name} is not wired up yet",
    };

    /// <summary>
    /// **The player modules' interface bindings** in force (an element's name to one of Vitomir's own actions,
    /// <see cref="Platform.PlayerModules.InterfaceActions"/>; the later module's binding where two bind one element), set by whoever starts
    /// the game from its modules' layers. An element the bar answers itself never reaches them; a binding of an element 1.1's bars have
    /// is left out (<see cref="Platform.PlayerModules.InterfaceActions.Effective"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Platform.PlayerModules.InterfaceAction> Bindings { get; set; } =
        new Dictionary<string, Platform.PlayerModules.InterfaceAction>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The actions the bar cannot do itself (the advancement screen, the vault, the save and load screens, the escape menu), done by the
    /// game: given the action, answers what happened, or null when it is not one it does.
    /// </summary>
    public Func<Platform.PlayerModules.InterfaceAction, string?>? Perform { get; set; }

    /// <summary>
    /// A click on an element bound to an action: an action of one of the bar's own elements is that element's click (the regroup button's
    /// right click for disperse), a quick-use action the slot's item used, the rest the game's (<see cref="Perform"/>). Null when the
    /// element is bound to nothing.
    /// </summary>
    string? Bound(UiControl hit)
    {
        if (!Bindings.TryGetValue(hit.Name, out var action)) return null;
        string did = Do(action);
        return $"{hit.Name}: {action.Name}: {did}";
    }

    /// <summary>**Does one of Vitomir's interface actions** as the bar's element of it, the quick-use slot, or the game (<see cref="Perform"/>).</summary>
    public string Do(Platform.PlayerModules.InterfaceAction action)
    {
        if (action.BarElement is { } element)
            return element.ToLowerInvariant() switch
            {
                "sbattackradio" => Stance(PartyStance.Attack),
                "sbneutralradio" => Stance(PartyStance.Neutral),
                "sbdefenseradio" => Stance(PartyStance.Defense),
                "sbregroupbutton" => action.Right ? Disperse() : Regroup(),
                _ => Panes?.Invoke(element) ?? $"{element} is not wired up here",
            };
        if (action.Name.StartsWith("quick-use-", StringComparison.Ordinal) && int.TryParse(action.Name["quick-use-".Length..], out int n))
        {
            if (n < 1 || n > quick.Count || quick[n - 1].Item is not { } item) return $"quick-use slot {n} is empty";
            return UseQuick is { } use ? use(item) : $"{item.Name} would be used, but using is not wired up";
        }
        return Perform?.Invoke(action) ?? $"{action.Name} is not done here";
    }

    /// <summary>
    /// The discipline slots on the **press**: nothing yet. A slot acts on the release over the
    /// slot it was pressed on (the slot's event 1 — <see cref="SelectSlot"/>), so a
    /// press that turns into a drag does not select. `sbCurrentDiscipline` has no click at all, and
    /// the right button does nothing on either.
    /// </summary>
    string? Power(UiControl hit, bool right)
    {
        string name = hit.Name;
        if (name.Equals("sbCurrentDiscipline", StringComparison.OrdinalIgnoreCase)) return "the current discipline (it has no click)";
        if (PowerSlot(name) < 0) return null;
        return right ? $"{name} (the right button does nothing on a slot)" : $"{name} pressed";
    }

    /// <summary>The quick slot a control is (`sbDiscipline0` … `5`), or −1.</summary>
    static int PowerSlot(string name) =>
        name.StartsWith("sbDiscipline", StringComparison.OrdinalIgnoreCase)
        && name.Length == "sbDiscipline".Length + 1 && char.IsAsciiDigit(name[^1]) ? name[^1] - '0' : -1;

    /// <summary>The quick slot under a point in the bar's space, or −1.</summary>
    public int PowerSlotAt(int x, int y) => At(x, y) is { } hit ? PowerSlot(hit.Name) : -1;

    /// <summary>The discipline a quick slot shows, or null.</summary>
    public Rpg.Discipline? PowerIn(int slot) => powers.FirstOrDefault(p => p.Slot == slot).Discipline;

    /// <summary>
    /// The release over the slot that was pressed: nothing while advancing or on an empty
    /// slot; otherwise <c>DISCIPLINESELECT</c> — always, even for the one already selected — and the
    /// slot's discipline becomes the steered character's current one.
    /// </summary>
    public string SelectSlot(int slot, bool advancing = false)
    {
        if (advancing) return "nothing is selected while advancing";
        if (PowerIn(slot) is not { } d) return $"slot {slot} is empty";
        if (place.Steered is not { } steered) return "nobody is being steered";
        Audio.Noise.Ui(place, Audio.FoleyEvent.DisciplineSelect);
        Rpg.DisciplineBook.Select(steered, d);
        Update();
        return $"{d.Name} selected";
    }

    /// <summary>
    /// A discipline dropped on a quick slot: the one there is put out of the bar
    /// (slot −1), this one goes in — a discipline knows one slot, so its old one empties — and
    /// nothing is selected.
    /// </summary>
    public string Assign(int slot, Rpg.Discipline d)
    {
        if (slot < 0 || slot >= Rpg.DisciplineList.QuickSlots) return "not a quick slot";
        if (PowerIn(slot) is { } there && !ReferenceEquals(there, d)) there.QuickSlot = -1;
        d.QuickSlot = slot;
        Update();
        return $"{d.Name} into slot {slot}";
    }

    /// <summary>The red cross and its noise, for a click the bar refuses. Null is silence.</summary>
    public Action? Refuse { get; set; }

    static bool One(InputModifier mods) => (mods & (InputModifier.Shift | InputModifier.Ctrl)) != 0;

    /// <summary>
    /// A click on the quick-use strip. The bar knows which cell was hit; what *using* an item means
    /// is `Rpg.Items.Use`, handed in as <see cref="UseQuick"/> so the RPG layer is not reached from
    /// here. Without a handler the strip says so rather than pretending.
    /// </summary>
    string? Quick(UiControl hit, InputModifier mods, bool right)
    {
        if (!hit.Name.Equals("sbQuickUse", StringComparison.OrdinalIgnoreCase)) return null;

        if (right)
        {
            if (QuickAt(LastX, LastY) is not { } item) return "that quick slot is empty";
            return UseQuick is { } use ? use(item) : $"{item.Name} would be used, but using is not wired up";
        }

        // Left: the grid handler, the same one the pack gets.
        int cell = QuickCellAt(LastX, LastY);
        if (cell < 0) return "not over a quick slot";
        return TakeQuick is { } take ? take(cell) : "the quick strip is not wired up";
    }

    /// <summary>
    /// The message box and its scroll bar (`sbTextbox`, `sbScrollBar`, `sbScrollSlider`) — set by
    /// whoever has the log and the box's font, and kept up with the log by <see cref="Update"/>.
    /// </summary>
    public MessageBox? Messages { get; set; }

    /// <summary>
    /// The message box (<c>sbTextbox</c>) given its log, laid out with the font it is drawn in (the box's <c>font</c>, else
    /// <c>defaultFont10</c>), its scroll bar and slider: <see cref="Messages"/>, or null for no log or no box (ours; until
    /// the status bar pass's <c>Log</c> made it, a comparison with 1.1's section 1.3). CPU metrics only.
    /// </summary>
    public void ShowLog(MessageLog? log, UiAssets assets)
    {
        var box = this["sbTextbox"];
        var font = assets.Font(box?.Element.Get("font") ?? "defaultFont10");
        Messages = log is not null && box is not null ? new MessageBox(log, font, box, this["sbScrollBar"], this["sbScrollSlider"]) : null;
    }

    /// <summary>Where the last click landed, so a grid handler can find its own cell.</summary>
    public int LastX { get; private set; }
    public int LastY { get; private set; }

    /// <summary>
    /// The tall invisible control over a row's portrait. The manual gives it three jobs: a plain
    /// click <b>selects</b> that character, <b>ctrl</b>-click is the group toggle, and a right click
    /// uses the current discipline on them (which is how a fallen companion is Awakened). All three
    /// are bound.
    /// </summary>
    string? Hotspot(UiControl hit, InputModifier mods, bool right)
    {
        if (!hit.Name.StartsWith("statushotspot", StringComparison.OrdinalIgnoreCase)) return null;
        if (hit.Actor is not { } who) return "an empty party slot";
        if (mods.HasFlag(InputModifier.Ctrl)) return Toggle(who);
        if (right) return OnHead(who);
        return OnPortrait(who);
    }

    /// <summary>How close a member must be to be handed what is on the cursor.</summary>
    public const float GiveReach = 400f;

    /// <summary>
    /// A plain left click on a portrait. On the one already steered,
    /// nothing. With something on the cursor it is **handed over**: within 400 units it leaves the
    /// hand for their pack and makes its <c>wear</c> noise at them (event
    /// 0x14); when they have no room it goes back on the cursor with the red cross, and
    /// further away than that the red cross alone. With nothing on the cursor, the player takes
    /// that character over (<see cref="Location.Steer"/>).
    /// </summary>
    string OnPortrait(Actor who)
    {
        var member = partyThings.FirstOrDefault(t => ReferenceEquals(t.Actor, who));
        if (member is null) return $"{Name(who)} is not in the party";
        if (place.Steered is not { } steered) return place.Steer(member) ? $"now steering {Name(who)}" : $"cannot steer {Name(who)}";
        if (ReferenceEquals(steered, member)) return $"already steering {Name(who)}";
        if (steered.Actor?.Held is { } held)
        {
            if (System.Numerics.Vector3.Distance(steered.Position, member.Position) > GiveReach)
            {
                Refuse?.Invoke();
                return $"{Name(who)} is too far away to hand {held.Name} to";
            }
            Rpg.Ground.ClearHand(steered);
            if (Rpg.Stow.Put(member, held, steering: false) is Rpg.StowResult.Refused)
            {
                Rpg.Ground.Hand(steered, held);
                Refuse?.Invoke();
                return $"{Name(who)} has no room for {held.Name}";
            }
            Audio.Noise.Foley(held.Thing, Audio.FoleyEvent.Wear, member);
            Update();
            return $"handed {held.Name} to {Name(who)}";
        }
        bool ok = place.Steer(member);
        Update();
        return ok ? $"now steering {Name(who)}" : $"cannot steer {Name(who)}";
    }

    /// <summary>
    /// The right button on a party member's portrait: a scroll in the
    /// hand whose discipline may be cast at a **head** (`TARGET HEAD`, 0x80 — Awaken's) is read at
    /// that member; else the current discipline, when it is one of those, is cast at them with
    /// Space held; else the red cross. The cast's own answer is not looked at, and there is no
    /// frenzy gate here. Two gates a comparison with 1.1 found: **while a shop is up** the click does
    /// nothing (the portrait tests the shop's flag, as the pack's icons do), and **a scroll
    /// in the hand that names a discipline which cannot be cast at a head** is the red cross at once
    /// — the current discipline is only tried when the hand holds no such scroll.
    /// </summary>
    string OnHead(Actor who)
    {
        if (Shopping) return "nothing while a shop is up";
        if (place.Steered is not { Actor: { } me } steered) return "nobody is being steered";
        var member = partyThings.FirstOrDefault(t => ReferenceEquals(t.Actor, who));
        if (member is null) return $"{Name(who)} is not in the party";
        if (me.Held is { Type: Rpg.ItemType.Scroll } scroll && place.Powers?[scroll.ScrollDiscipline] is { } read)
        {
            if (!read.Has(Formats.DisciplineFlags.TargetHead))
            {
                Refuse?.Invoke();
                return $"{scroll.Name} cannot be read at {Name(who)}";
            }
            Rpg.Cast.Order(steered, member, null, null, InputModifier.None, scroll);
            return $"read {scroll.Name} at {Name(who)}";
        }
        if (me.Disciplines.Current is { } cur && cur.Definition.Has(Formats.DisciplineFlags.TargetHead))
        {
            Rpg.Cast.Order(steered, member, null, cur, InputModifier.Space);
            return $"{cur.Name} at {Name(who)}";
        }
        Refuse?.Invoke();
        return $"nothing to cast at {Name(who)}";
    }

    /// <summary>
    /// Regroup: everybody stops what they are doing and comes back
    /// into formation — the bit is set on <b>every</b> member, whatever it was. The button has a
    /// second form, disperse, which clears the bit on everybody instead; the engine
    /// reaches it with a held modifier and through the button's third callback, and <b>both are
    /// bound</b> in <see cref="Act"/> above — shift held, or the right button.
    /// <para>
    /// First the steered character calls it out — <c>vox_regroup</c> (order 0x84) when it
    /// can speak and is not dead — and then every member's orders are cancelled (below).
    /// </para>
    /// </summary>
    public string Regroup()
    {
        CallOut(Audio.FoleyEvent.VoxRegroup);
        CancelOrders();
        foreach (var member in party) member.Flags2 |= Actor.Flag2.InFormation;
        Update();
        return $"regroup: {party.Count} back in formation";
    }

    /// <summary>
    /// **Ctrl + left click on the ground** — the manual's *"Party Regroup to Location"* and
    /// `Readme.rtf`'s *"cancel all the actions of your coterie members and they will regroup with the
    /// leader"*. The ground click calls the order-cancel with its first argument set,
    /// The order cancel with 1, which cancels only the members **keeping formation** (flag `0x1000`),
    /// and then orders the walk as usual. No call-out and no change to the formation bits: the
    /// members, their orders gone, simply follow the leader to where he is now going.
    /// </summary>
    public string RegroupHere()
    {
        int n = CancelOrders(formationOnly: true);
        return $"regroup to location: {n} of {party.Count} in formation stopped what they were doing";
    }

    /// <summary>
    /// Disperse, the regroup button's other half: the formation bit is cleared on
    /// <b>every</b> member, so the whole coterie stops following at once. Reached with shift held or
    /// on the right button. It is not in the manual's control list — the manual describes leaving
    /// the group one character at a time, by the pip or by <b>G</b>. The steered character says
    /// <c>vox_stopfollow</c> (order 0x86) first, on the same conditions as the regroup's line, and
    /// the orders are cancelled the same way.
    /// </summary>
    public string Disperse()
    {
        CallOut(Audio.FoleyEvent.VoxStopFollow);
        CancelOrders();
        foreach (var member in party) member.Flags2 &= ~Actor.Flag2.InFormation;
        Update();
        return $"disperse: {party.Count} out of formation";
    }

    /// <summary>
    /// The steered character's line for the whole party: only when it can
    /// speak and is not dead (bit `0x10000000`).
    /// </summary>
    void CallOut(int voxEvent)
    {
        var steered = place.Minds?.Steered ?? place.Player?.Thing;
        if (steered?.Actor is { } a && a.Has(Actor.Flag.Dead)) return;
        if (Audio.Noise.CanSpeak(steered)) Audio.Noise.Foley(steered, voxEvent);
    }

    /// <summary>
    /// The order cancel with 0, the order-cancel regroup and disperse both run: every member's action is
    /// cancelled, unforced (nothing for one busy, dead or held) and its walk stopped, and a member whose mind is not in one
    /// of the four modes that must run out (frenzied, mesmerized, dazed, afraid — 9 to 12) and has
    /// neither of its mind's two hold bits (bits `0x4` and `0x10000000`) lets its
    /// target go and thinks again in a tenth of a second.
    /// <paramref name="formationOnly"/> is the first argument, which only the ground click sets:
    /// then a member out of formation is passed over. Returns how many were cancelled.
    /// </summary>
    int CancelOrders(bool formationOnly = false)
    {
        int now = place.Minds?.Now ?? 0, n = 0;
        foreach (var thing in partyThings)
        {
            if (formationOnly && thing.Actor?.Has2(Actor.Flag2.InFormation) != true) continue;
            n++;
            // **The unforced cancel**, not a bare clear: a member busy,
            // dead or held keeps its order (a swing runs out), and a feeder lets its victim go the
            // proper way. (A comparison with 1.1; Vitomir cleared the action outright.)
            Rpg.Actions.Cancel(thing);
            if (place.Player is { } p && ReferenceEquals(p.Thing, thing)) p.Stop();
            if (thing.Mind is not { } mind) continue;
            mind.Walker.Stop();
            bool holds = mind.Mode is Ai.AiMode.Frenzied or Ai.AiMode.Mesmerized or Ai.AiMode.Dazed or Ai.AiMode.Afraid
                         || (mind.Flags & 0x4) != 0 || (mind.Flags & 0x10000000) != 0;
            if (holds) continue;
            mind.Aim(null, now);
            mind.NextThink = now + 100;
        }
        return n;
    }

    /// <summary>
    /// Sets the party's stance. A stance is **per member**, and the button sets every
    /// member's unless a modifier is held and the steered character is the player (type
    /// 7), in which case only the steered character's. The three radios are then checked
    /// to match, which is how exactly one of them is always down.
    /// <para>
    /// Before any of that the steered character calls the order out — <c>vox_attackmode</c>,
    /// <c>vox_neutral</c> or <c>vox_defendmode</c> — when it can speak
    /// (<see cref="Audio.Noise.CanSpeak"/>). The one-member form has a companion answer with a
    /// <c>vox_…ok</c> line half the time, but only a member who is not the steered character
    /// — and the bar only ever hands it the steered character, so from the bar the answer
    /// is never heard. The plain form sets everyone without a word.
    /// </para>
    /// </summary>
    public string Stance(PartyStance stance, bool selectedOnly = false)
    {
        var steered = place.Minds?.Steered ?? place.Player?.Thing;
        if (Audio.Noise.CanSpeak(steered)) Audio.Noise.Foley(steered, Audio.FoleyEvent.VoxAttackMode + (int)stance);
        selectedOnly &= steered?.Type == ThingType.Player;
        if (selectedOnly && Selected is { } one) one.Stance = stance;
        else foreach (var member in party) member.Stance = stance;
        Update();
        return $"stance {stance.ToString().ToLowerInvariant()} for " +
               (selectedOnly ? Name(Selected) : $"the party ({party.Count})");
    }

    /// <summary>
    /// The stance keys — **A**, **N**, **E**: the whole party, and
    /// not a word from anyone. The radios follow, as they do for the button.
    /// </summary>
    public string StanceKey(PartyStance stance)
    {
        foreach (var member in party) member.Stance = stance;
        Update();
        return $"stance {stance.ToString().ToLowerInvariant()} for the party ({party.Count})";
    }

    /// <summary>
    /// A click on a row's pip — or on any of its five bars, because the pane gives all six the same
    /// handler (bound on each) — takes the character in and out of the
    /// formation (`SetInParty`, which flips bit `0x1000`). Six controls one
    /// pixel apart all doing this is the original's, not a mistake here.
    /// </summary>
    string? Formation(UiControl hit)
    {
        if (hit.Actor is not { } who) return null;
        if (hit.Type is not ("checkbox" or "progressbar")) return null;
        return Toggle(who);
    }

    /// <summary>
    /// In or out of the formation (`SetInParty`), which is what the pip shows and what the
    /// <b>G</b> key does to the steered character. Out of formation a companion stops following —
    /// the party think short-circuits before the follow on `InParty` — but goes on
    /// fighting and, on Attack stance, goes on hunting.
    /// </summary>
    public string Toggle(Actor who, bool silent = false)
    {
        who.Flags2 ^= Actor.Flag2.InFormation;
        bool joined = who.Has2(Actor.Flag2.InFormation);
        // The member answers for itself: `vox_startfollow` back into formation,
        // `vox_stopfollow` out of it (orders 0x85 and 0x86), when it can speak. Both the row click
        // and the **G** key pass "not silent", so both are heard. The steered
        // character is answered for too: this test is on the member, not on who is steering.
        if (!silent && ThingOf(who) is { } thing && Audio.Noise.CanSpeak(thing))
            Audio.Noise.Foley(thing, joined ? Audio.FoleyEvent.VoxStartFollow : Audio.FoleyEvent.VoxStopFollow);
        Update();
        return $"{Name(who)} {(joined ? "joins" : "leaves")} the formation";
    }

    /// <summary>The thing on the same row as an actor (the two lists are kept in step).</summary>
    WorldThing? ThingOf(Actor who)
    {
        for (int i = 0; i < party.Count && i < partyThings.Count; i++)
            if (ReferenceEquals(party[i], who)) return partyThings[i];
        return null;
    }

    static string Name(Actor? actor) =>
        actor is null ? "nobody" : actor.Name.Length > 0 ? actor.Name : actor.CastId.Length > 0 ? actor.CastId : "the character";

    /// <summary>
    /// Brings the whole bar up to date: who is in the party, where their rows go, which tracks each
    /// one shows and what is in them. This is the layout (when the party changes) and
    /// the refresh run back to back, which is what the layout itself does on its last
    /// line. 1.1's party-change hook calls the layout for the single-player bar only; here
    /// `Update` is called directly wherever the party changes.
    /// </summary>
    public void Update()
    {
        Messages?.Sync();
        party.Clear();
        partyThings.Clear();
        // The lit row is the party's selected member (which the party hands the bar) —
        // during a possession still the caster, since the possession's own hand-over
        // never touches the bar, and the body is not a party member to have a row.
        var steered = place.Selected;
        Selected = steered?.Actor;
        Steered = place.Steered?.Actor;
        // The rows in party-slot order — the order the engine's own roster keeps (the layout walks
        // `rpgParty`'s array) — with the steered character's row lit wherever it is. Earlier, the
        // steered one was put first, which only ever mattered once steering could change.
        // While a portrait is dragged (`PartyDrag`), the rows the party would have if it were let go — as long as they are
        // still the party's members; anything else and the drag's preview is dropped.
        IEnumerable<WorldThing> order = place.PartyMembers();
        if (Preview is { } preview)
        {
            var members = place.PartyMembers().ToList();
            if (preview.Count == members.Count && members.All(m => preview.Contains(m))) order = preview;
            else Preview = null;
        }
        foreach (var thing in order)
            if (thing.Actor is { } actor && !party.Contains(actor))
            {
                party.Add(actor);
                partyThings.Add(thing);
            }
        if (Selected is not null && steered is not null && !party.Contains(Selected))
        {
            party.Insert(0, Selected);
            partyThings.Insert(0, steered);
        }

        // The layout opens by hiding every row and clearing its character, so a row the party has
        // outgrown cannot keep drawing a stale one.
        for (int row = 0; row < Rows; row++)
        {
            foreach (var control in RowControls(row)) { control.Shown = false; control.Actor = null; control.Row = row; }
            Reset(row);
        }

        Radios();
        Buttons();
        Quick();
        Slots();

        int count = Math.Min(party.Count, Rows);
        if (count == 0) return;

        int width = this["statusIndicators0"]?.Width ?? 0;
        float x = UiAssets.Width / 2f - width / 2f - (count - 1) * RowHalfStep;
        for (int row = 0; row < count; row++, x += RowStep)
        {
            Row(row, party[row], (int)x, count);
        }
    }

    /// <summary>The quick strip's `gridsize` text as last read, and how many cells across it gave.</summary>
    string? quickSizeText;
    int quickAcross;

    /// <summary>
    /// Fills the quick-use strip from the steered character's own quick slots (<see cref="Steered"/>),
    /// which is the one whose items the player can actually reach.
    /// </summary>
    void Quick()
    {
        quick.Clear();
        if (this["sbQuickUse"] is not { } grid || Steered is null) return;
        // The grid's size is read from its text once, not every frame.
        string? sizeText = grid.Element.Get("gridsize");
        if (!string.Equals(sizeText, quickSizeText, StringComparison.Ordinal))
        {
            quickSizeText = sizeText;
            quickAcross = sizeText is { } g && NuiPane.Ints(g) is [var w, _, ..] ? w : 0;
        }
        int across = quickAcross;
        if (across <= 0) return;

        // **Every cell, whether or not something is in it, and each item in the cell its own record
        // names.** It used to pack the items left to right in inventory order and build a cell only
        // where one landed, which was wrong twice: an item that lives in slot 3 was drawn in slot 0,
        // and an **empty** strip had no cells at all — so there was nothing to drop onto and the bar
        // answered "not over a quick slot" for every click. Play found the second one: taking the
        // letter out, it could not be put back.
        for (int cell = 0; cell < across; cell++)
            quick.Add(new UiCell(grid.X + cell * CellWidth, grid.Y, CellWidth, CellHeight,
                                 At(Steered, cell)));

        static Rpg.Item? At(World.Actor who, int cell)
        {
            foreach (var item in who.Inventory.Items)
            {
                if (item.Location != Rpg.Slot.QuickUse || item.Icon.Length == 0 || item.Destroyed) continue;
                var (wide, _) = item.IconCells;
                if (cell >= item.Cell.X && cell < item.Cell.X + wide) return item;
            }
            return null;
        }
    }

    /// <summary>
    /// Fills the six discipline slots and the current one, the way 1.1 does: clear them all,
    /// then walk the character's own list and put each discipline in the slot it says it is in.
    /// </summary>
    /// <remarks>
    /// Which bitmap a slot draws is the other half of the slot's draw, and a discipline has <b>three</b>:
    /// the plain `icon`, `icon_R` and `icon_L`. `sbCurrentDiscipline` takes the large one; one of
    /// the six takes the ringed one when it holds the discipline the player has selected;
    /// everything else takes the plain one.
    /// </remarks>
    void Slots()
    {
        powers.Clear();
        if (Steered is null) return;
        var current = Steered.Disciplines.Current;
        foreach (var known in Steered.Disciplines.All)
        {
            if (known.QuickSlot < 0 || known.QuickSlot >= Rpg.DisciplineList.QuickSlots) continue;
            if (this[$"sbDiscipline{known.QuickSlot}"] is not { } slot) continue;
            string art = known.Definition.Icon + (ReferenceEquals(known, current) ? SelectedSuffix : "");
            powers.Add(new UiPower(known.QuickSlot, slot.X, slot.Y, slot.Width, slot.Height, known, art));
        }
        if (current is not null && this["sbCurrentDiscipline"] is { } box)
            powers.Add(new UiPower(-1, box.X, box.Y, box.Width, box.Height, current,
                                   current.Definition.Icon + LargeSuffix));
    }

    /// <summary>Everything the pane puts in one row: the backdrop, its children, and the hotspot.</summary>
    List<UiControl> RowControls(int row) => rows[row];

    /// <summary>Puts a row's controls back where the pane file declares them, before it is moved.</summary>
    void Reset(int row)
    {
        foreach (var control in RowControls(row))
        {
            var pos = control.Element.Pos;              // read from its text: once, not four times
            control.X = pos.Length > 0 ? pos[0] : 0;
            control.Y = pos.Length > 1 ? pos[1] : 0;
        }
    }

    void Row(int row, Actor actor, int x, int count)
    {
        // The backdrop moves and its children follow by the same delta (the move walks them), so the
        // bars keep the offsets the pane gives them inside the row.
        if (this[$"statusIndicators{row}"] is { } backdrop)
        {
            var (dx, dy) = backdrop.MoveTo(x, RowY);
            foreach (var child in RowControls(row))
                if (!ReferenceEquals(child, backdrop) && !child.Name.StartsWith("statusHotspot", StringComparison.OrdinalIgnoreCase))
                    child.Move(dx, dy);
            backdrop.Shown = true;
            backdrop.Actor = actor;
            // image1 is the row of the selected character — but only when there is somebody else to
            // tell it apart from. A party of one gets image0.
            backdrop.Image = ReferenceEquals(actor, Selected) && count > 1 ? 1 : 0;
        }
        if (this[$"statusHotspot{row}"] is { } hotspot)
        {
            hotspot.MoveTo(x, HotspotY);
            bool reach = (place?.Qol ?? Qol.Current).PartyOrder;
            (hotspot.Reach, hotspot.ReachUp) = reach ? (HeadReach, HeadReachUp) : (0, 0);
            hotspot.Shown = true;
            hotspot.Actor = actor;
        }

        foreach (var control in RowControls(row)) control.Actor = actor;

        // Health is the one track every character has.
        Bar($"statusHealth{row}", true, 0f, actor.MaxHealth, actor.Health);

        // The other two tracks are a pair of pairs sharing two rows of pixels, and `ACTOR_TYPE` is
        // what picks which of each pair is up: a human spends mana and has faith, and
        // everything else — vampire, ghoul and monster alike — has blood and can frenzy. Both of the
        // second tracks are measured against **100**, which is the progress bar's own constructed
        // range and is set explicitly for frenzy. 1.1 shows and updates the
        // matching track with its maximum on a stat change; Vitomir reads it live here.
        bool human = actor.Type == ActorType.Human;
        Bar($"statusMana{row}", human, 0f, actor.Stat[(int)ActorStat.ManaPool], actor.Stat[(int)ActorStat.Mana]);
        Bar($"statusFaith{row}", human, 0f, 100f, actor.Stat[(int)ActorStat.Faith]);
        Bar($"statusBlood{row}", !human, 0f, actor.Stat[(int)ActorStat.BloodPool], actor.Stat[(int)ActorStat.Blood]);
        Bar($"statusFrenzy{row}", !human, 0f, 100f, actor.Stat[(int)ActorStat.Frenzy]);

        // The pip follows `InParty`, which is the formation bit rather than party
        // membership: a companion can be told to stop keeping formation without leaving the party.
        if (this[$"statusParty{row}"] is { } pip)
        {
            pip.Shown = true;
            pip.Checked = actor.Has2(Actor.Flag2.InFormation);
        }
    }

    void Bar(string name, bool shown, float min, float max, float value)
    {
        if (this[name] is not { } bar) return;
        bar.Set(min, max, value);
        bar.Shown = shown;
    }

    /// <summary>
    /// Checks the one stance radio that matches the selected character's stance and clears the other
    /// two (called with `GetStance(selected)`), which is why one of the three
    /// is always down. Part of <see cref="Update"/>.
    /// </summary>
    void Radios()
    {
        var stance = Selected?.Stance ?? PartyStance.Neutral;
        Check("sbAttackRadio", stance == PartyStance.Attack);
        Check("sbNeutralRadio", stance == PartyStance.Neutral);
        Check("sbDefenseRadio", stance == PartyStance.Defense);
        void Check(string name, bool on) { if (this[name] is { } c) c.Checked = on; }
    }

    /// <summary>
    /// **The order a dragged portrait would leave the party in** (ours, <see cref="PartyDrag"/>): the rows, and the heads
    /// drawn in them, are laid out in it while it is set. Null (the default) lays them out in the party's own order. Set by
    /// whoever has the drag; dropped by <see cref="Update"/> once it is not the party's members any more.
    /// </summary>
    public IReadOnlyList<WorldThing>? Preview { get; set; }

    /// <summary>The member whose portrait is being dragged, which the bar rings (<see cref="CarriedLands"/> says in which colour).</summary>
    public WorldThing? Carrying { get; set; }

    /// <summary>Whether letting the carried portrait go where it is now would change the order: gold if so, grey if not.</summary>
    public bool CarriedLands { get; set; }

    /// <summary>
    /// The portraits' places, one a row in the party's order, left to right: each row's hotspot. Where a row goes depends
    /// only on how many there are, so a preview (<see cref="Preview"/>) does not move them.
    /// </summary>
    public IReadOnlyList<PartySlot> HeadSlots
    {
        get
        {
            var slots = new List<PartySlot>();
            for (int row = 0; row < Math.Min(party.Count, Rows); row++)
                if (this[$"statusHotspot{row}"] is { Shown: true } h) slots.Add(new PartySlot(h.X - h.Reach, h.Y, h.Width + 2 * h.Reach, h.Height + h.ReachUp));
            return slots;
        }
    }

    /// <summary>The row of the bar <paramref name="who"/> is drawn in, or -1.</summary>
    public int RowOf(WorldThing who) => partyThings.FindIndex(t => ReferenceEquals(t, who));

    /// <summary>The row whose portrait is at bar point (<paramref name="x"/>, <paramref name="y"/>), or -1.</summary>
    public int HeadAt(int x, int y)
    {
        for (int row = 0; row < Math.Min(party.Count, Rows); row++)
            if (this[$"statusHotspot{row}"] is { Shown: true, Actor: not null } h && h.Contains(x, y)) return row;
        return -1;
    }

    /// <summary>
    /// Whether the pane a button opens is currently up. Set by whoever owns the panes; the bar asks
    /// it once per <see cref="Update"/> and puts the button down while the answer is yes.
    /// </summary>
    public Func<string, bool>? PaneShown { get; set; }

    /// <summary>
    /// Whether a shop is up — the shop pane's show and
    /// hide set and clear it. A portrait's right click asks it.
    /// </summary>
    public bool Shopping { get; set; }

    /// <summary>
    /// The four pane buttons stay **down** while their pane is up, which is why the file declares
    /// them as `checkbox` rather than `button` and gives each one a `checkedbitmap` ending in
    /// `Dep` — depressed. Nothing had to be inferred: `spsbInventoryButtonDep.tga` is named in
    /// `statusBarSP.nui` and `UiControl.Art` has always preferred it when the control is checked;
    /// what was missing was anybody telling the control it was checked. The gap showed in
    /// play: when the panes are open, 1.1 darkens the buttons to show they are active.
    ///
    /// All four panes exist now. The map button (`sbMapButton`) is not in the list because its pane
    /// is not built.
    /// </summary>
    public static readonly string[] PaneButtons =
        ["sbInventoryButton", "sbCharacterButton", "sbQuestButton", "sbDisciplineButton"];

    /// <summary>
    /// What the bar's hide hides with bit 1: the five pane buttons, and the Storyteller's when the bar has
    /// one. The shop's show hook and the vault's opener both call it with 5 —
    /// bit 4 greys the pack's coin button too — and their closers undo it.
    /// </summary>
    public static readonly string[] ShopAndVaultHide =
        ["sbInventoryButton", "sbCharacterButton", "sbMapButton", "sbQuestButton", "sbDisciplineButton", "sbSTButton"];

    void Buttons()
    {
        foreach (var name in PaneButtons)
            if (this[name] is { } control) control.Checked = PaneShown?.Invoke(name) ?? false;
    }
}
