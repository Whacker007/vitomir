// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The status bar, drawn. Every decision about *what* the bar shows — where the rows go, which
// tracks a character has, which button is down — belongs to `Vitomir.Engine.Ui.StatusBar`, which is
// testable without a GPU. This file is the part that needs one: it walks the pane's quads and the
// controls that state has placed, and puts their bitmaps on screen.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own status bar over the finished frame while <see cref="Enabled"/>.</summary>
public sealed class StatusBarPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;
    readonly NfdFontRef font;

    /// <summary>The bar's state: what to draw, and what a click on it does.</summary>
    public StatusBar Bar { get; }

    /// <summary>Off by default; `--hud` starts it on and F4 toggles it in play.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the pointer is in the bar's space, or null under a menu: the quick-use item under it draws
    /// its red-outlined `_R` picture, as a pack icon does (the strip's icons are the same
    /// icon elements, placed by the same refill). The frame loop keeps it.
    /// </summary>
    public (int X, int Y)? Pointer { get; set; }

    // The message box's log and layout are the bar's (StatusBar.ShowLog): the pass draws what Bar.Messages holds.

    /// <summary>Names the pane asked for that the install has not got. Reported once, not per frame.</summary>
    public IReadOnlyList<string> Missing => ui.Assets.Missing;

    public const string PaneName = StatusBar.PaneName;

    /// <summary>A font and the box it is drawn in, resolved once.</summary>
    readonly record struct NfdFontRef(Vitomir.Formats.NfdFont Font, UiControl? Box);

    /// <param name="paneName">The bar's pane: `statusBarSP`, or `statusBarMP` in a network game.</param>
    public StatusBarPass(UiCanvas ui, Location place, string paneName = PaneName)
    {
        this.ui = ui;
        rows = new RowsPass(this);
        Bar = new StatusBar(ui.Assets.Pane(paneName), place);
        var box = Bar["sbTextbox"];
        font = new NfdFontRef(ui.Assets.Font(box?.Element.Get("font") ?? "defaultFont10"), box);
    }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || width <= 0 || height <= 0) return;
        Bar.Update();
        ui.Begin(width, height);

        // **Nothing paints the head rectangle black, and nothing should.** It is the one place in
        // the whole bar where the artwork is transparent — `statusBarSP.nui` declares no quad or
        // element across it and `StatusBarSP_01`, `_02` and `_03` are alpha 0 over every row of it,
        // with no other transparent pixel anywhere in the bar. What shows through it is the frame's
        // own black, because **the world is not drawn that far down**: see `StatusBar.WorldBottom`,
        // which is measured off two 1.1 screenshots and reproduces the original's bar-on/bar-off
        // shift to a seventh of a pixel. A black quad here would paint over the same pixels and
        // hide it if that ever broke.

        // **Ours, and only when the world is drawn under the bar** — wherever the sheet does not reach the
        // picture's sides: fitted on a picture wider than 4:3, or the 16:9 stretch on one wider than 16:9
        // (`UiLayout.Overlays`; FrameRenderer carries the world on down so that what shows beside the bar is world). Then the frame's own black is no longer there, and the bar's rows of the sheet are made black
        // first, which is the same picture inside the sheet's width as 1.1's.
        if (UiLayout.Overlays(width, height))
            ui.Box(0f, 0f, UiAssets.Width, UiAssets.Height - StatusBar.WorldBottom, new Tint(0f, 0f, 0f));   // the sheet's rows count up from its bottom

        // The bar itself: seven quads that tile the bottom of the 640x480 space.
        foreach (var q in Bar.Pane.Quads)
            ui.Draw(q.Material, q.Pos[0], q.Pos[1], q.Size[0], q.Size[1]);

        // Then every control that is up, in the order the pane declares them — which puts a row's
        // backdrop under the bars the pane parents to it. The **party rows** are held back: see
        // <see cref="Rows"/>.
        foreach (var control in Bar.Controls)
        {
            if (!control.Shown || control.Row >= 0 || control.Width <= 0 || control.Height <= 0) continue;
            if (control.Type == "progressbar") Fill(control);
            else if (control.Art is { Length: > 0 } art)
                ui.Draw(art, control.X, control.Y, control.Width, control.Height, control.Greyed ? GreyedTint : null);
            // A blinking control has its `flash` drawn over it on the lit half.
            if (control.FlashOn && control.Flash is { } flash)
                ui.Draw(flash, control.X, control.Y, control.Width, control.Height);
        }

        // The quick-use strip, on top of the bar's own artwork. The item grid's own draw is not
        // read, so this is the provisional one: stretched into the cell the way it always was, and
        // moved so the artwork rather than the padded canvas is centred (`UiCanvas.Fit`).
        // The one under the pointer draws its `_R`; with none in the install 1.1 draws nothing,
        // and the repair `Repairs.HoverPicture` its plain picture.
        foreach (var cell in Bar.QuickUse)
            if (cell.Item is { } held)
            {
                bool hovered = Pointer is var (px, py) && ReferenceEquals(Bar.QuickAt(px, py), held);
                if (!ui.Fit(hovered ? held.Icon + InventoryPane.HoverSuffix : held.Icon, cell.X, cell.Y, cell.Width, cell.Height) && hovered)
                    ui.Fit(held.Icon, cell.X, cell.Y, cell.Width, cell.Height);
            }

        // The six discipline slots and the current one. Each has three bitmaps in the install and
        // the slot chooses which — plain, `_R` for the selected one, `_L` for the big box — so if a
        // variant is missing (a mod, a partial install) the plain one still goes up.
        foreach (var power in Bar.Powers)
            if (!ui.Icon(power.Art, power.X, power.Y, power.Width, power.Height))
                ui.Icon(power.Discipline.Definition.Icon, power.X, power.Y, power.Width, power.Height);

        Messages();
    }

    /// <summary>
    /// The four party rows — each one's backdrop, its health, blood, mana, frenzy and faith bars,
    /// and its formation pip — drawn as a pass of their own so they can go **over** the heads.
    ///
    /// **This order is matched by eye and disagrees with the one reading of the original.**
    /// `uiMenu::Draw` walks the pane's control chain twice
    /// and only then calls the pane's hook, which is what draws the heads — that
    /// says rows first, heads over them. A 1.1 screenshot says the opposite, and not ambiguously:
    /// Christof's crimson collar runs from y 738 to y 898 while his health bar sits at 877..881
    /// **on top of it**, so the bar is drawn after the model rather than the model being cut off
    /// above it. Something in those two passes is not yet understood. The screenshot wins under
    /// *faithful is what the player experiences*, and finding it is on the deferred list.
    /// </summary>
    public IUiPass Rows => rows;

    readonly RowsPass rows;

    sealed class RowsPass(StatusBarPass bar) : IUiPass
    {
        public UiCanvas Ui => bar.ui;

        public void Emit(int width, int height, FreeCamera camera, Simulation sim)
        {
            if (!bar.Enabled || width <= 0 || height <= 0) return;
            var ui = bar.ui;
            ui.Begin(width, height);
            foreach (var control in bar.Bar.Controls)
            {
                if (!control.Shown || control.Row < 0 || control.Width <= 0 || control.Height <= 0) continue;
                if (control.Type == "progressbar") bar.Fill(control);
                else if (control.Art is { Length: > 0 } art)
                    ui.Draw(art, control.X, control.Y, control.Width, control.Height);
            }
            bar.Ring();
        }
    }

    /// <summary>The ring round a dragged portrait (ours, <see cref="PartyDrag"/>) — gold where it would land, grey where it would not.</summary>
    public const float RingWidth = 2f;
    public static readonly Tint Lands = new(1f, 0.82f, 0.25f), Stays = new(0.55f, 0.55f, 0.55f);

    /// <summary>A greyed control's tint (ours): its art at under half its brightness.</summary>
    public static readonly Tint GreyedTint = new(0.42f, 0.42f, 0.42f);

    void Ring()
    {
        if (Bar.Carrying is not { } carried || Bar.RowOf(carried) is var row && row < 0) return;
        if (Bar[$"statusHotspot{row}"] is not { Shown: true } h) return;
        var tint = Bar.CarriedLands ? Lands : Stays;
        float w = RingWidth;
        int x = h.X - h.Reach, width = h.Width + 2 * h.Reach, height = h.Height + h.ReachUp;   // what the click reaches
        ui.Box(x, h.Y, width, w, tint);
        ui.Box(x, h.Y + height - w, width, w, tint);
        ui.Box(x, h.Y, w, height, tint);
        ui.Box(x + width - w, h.Y, w, height, tint);
    }

    /// <summary>One progress bar, filled left to right by its own `fillbitmap`.</summary>
    void Fill(UiControl bar)
    {
        if (bar.Element.Get("fillbitmap") is not { } fill) return;
        float f = bar.Fraction;
        if (f <= 0f) return;
        ui.Sub(fill, bar.X, bar.Y, bar.Width * f, bar.Height, 0f, 0f, f, 1f);
    }

    /// <summary>
    /// The message box, `textboxflags=0x04`: left-aligned, broken between words at the box's width,
    /// the first line said against the top and each later one below it, lifted as far as
    /// <see cref="MessageBox.Scroll"/> says — to the newest whenever a line arrives, anywhere the
    /// player scrolls it otherwise — and cut at the box's edges, because the original writes the text
    /// into the box's own surface. Each line is in the colour it was posted in; one posted
    /// without a colour takes the box's own, 240 grey. **The pen starts at the box's left edge**: the
    /// draw's x is the box's inset plus its sideways shift, and nothing
    /// sets either on this box — the constructor and the reset zero them and
    /// `sbTextbox` has no attribute that reaches them. Earlier, this drew two pixels in, by eye.
    /// </summary>
    void Messages() => DrawMessages();

    /// <summary>The message box's text alone, for the advancement bar's copy of it (the box is moved onto `statusBarADV`).</summary>
    public void DrawMessages()
    {
        if (Bar.Messages is not { } box) return;
        var at = box.Box;
        foreach (var (text, y, colour) in box.Drawn())
            ui.TextClipped(font.Font, at.X, at.Y + y, text, ReadPass.Colour(colour),
                           at.X, at.Y, at.X + at.Width, at.Y + at.Height);
    }
}
