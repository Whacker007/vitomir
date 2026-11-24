// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The front end's panes, drawn: whatever `Vitomir.Engine.Ui.FrontEnd` has up, in its order, with each
// control in the state the front end keeps — a button's picture for its state word, the text list's
// rows and slider, the text boxes and the edit field with its caret — then the floating texts, and
// on top of everything the title screen or the "loading" plaque when either is asked for.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the front end's menus over the finished frame.</summary>
public sealed class MenuPass(UiCanvas ui, FrontEnd menus) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public FrontEnd Menus { get; } = menus;

    /// <summary>The title screen (`ui/TitleScreen.nui`), drawn while the start-up runs.</summary>
    public bool Title { get; set; }

    /// <summary>The loading plaque (`ui/LoadScreen.nui`, 160 x 64 at 240,145), drawn once before a level loads.</summary>
    public bool Loading { get; set; }

    /// <summary>
    /// **A pane's draw hook**: what is drawn right after that pane, or null. 1.1's pane draw
    /// calls the hook after the pane's own controls, and the panes are drawn one after another, the top layer then the on-top
    /// list — so what a hook draws lies over its own pane and **under every pane drawn after it**, a dialog among
    /// them. The character screens' portrait (the hook of `mpPlayerSelect` and of `mpPlayerCreate`) is one: until
    /// Once it was a pass of its own after every pane, and the model stood over the Internet button's address box.
    /// </summary>
    public Func<MenuPane, IUiPass?>? Hook { get; set; }

    /// <summary>
    /// **Pictures a pane's own controls need that this pass does not draw** — the check boxes and static pictures of the
    /// multiplayer screens (`MpMenus.Checkboxes`) and of the Storyteller's panes (`Storyteller.Checkboxes`), in the 640 x 480
    /// space: drawn right after that pane, with its controls (1.1's pane draw draws every control of the pane
    /// before the next pane), so a dialog over the pane covers them. Earlier, they were drawn by a pass of their
    /// own after every pane, and `mpcgPWProtectCheck` stood over a message box on `mpCreateGame` (found by the earlier build's menus).
    /// </summary>
    public Func<MenuPane, IEnumerable<(string Art, float X, float Y, float W, float H)>>? Pictures { get; set; }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (width <= 0 || height <= 0) return;
        ui.Begin(width, height);
        Bands(width, height);
        if (Title) Quads(Menus.Pane(FrontEnd.TitleFile).Nui);
        foreach (var pane in Menus.Visible())
        {
            DrawPane(pane);
            if (Pictures?.Invoke(pane) is { } pictures)
                foreach (var (art, x, y, w, h) in pictures) ui.Draw(art, x, y, w, h);
            // The pane's draw hook, right after its own controls and before the next pane (the pane draw's last call).
            if (Hook?.Invoke(pane) is { } hook)
            {
                hook.Emit(width, height, camera, sim);
                ui.Begin(width, height);                // the hook may have drawn through another map (the portrait's, the panes')
            }
        }
        foreach (var c in Menus.Floating.Up)
        {
            if (!Menus.CaptionDrawn(c)) continue;
            var font = ui.Assets.Font(c.Font);
            if (c.Ours) { OurCaption(c, font); continue; }
            ui.Text(font, c.X, c.PenYAt(font.LineHeight, Menus.NowMs), c.Text, new Tint(1f, 1f, 1f, c.Alpha(Menus.NowMs)));
        }
        if (Menus.Tip is { } tip && !Loading) DrawTip(tip.Text, tip.X, tip.Y);
        if (Loading)
        {
            // In our lettering the plaque's word is ours: the pane drawn whole, its word with it.
            var plaque = Menus.Pane(FrontEnd.LoadScreenFile);
            if (plaque.Lettered) DrawPane(plaque);
            else Quads(plaque.Nui);
        }
    }

    /// <summary>
    /// **Wherever a whole-sheet screen leaves picture, the picture is black** (ours; for the bands
    /// above and below a centred sheet; for every side). While a screen that is the whole sheet is
    /// up (<see cref="FrontEnd.WholeSheet"/>: Options and its pages, the save and load screens, the map, the front
    /// end's own), or the sheet is seated in the centre (<see cref="UiLayout.Seat"/>: the front end's run), whatever a
    /// game's frame drew outside the sheet is covered before the screen is drawn: above and below a sheet shorter than
    /// the picture (the 16:9 stretch narrower than 16:9; fitted on a picture taller than 4:3), and **either side of one
    /// narrower than it** (the 16:9 stretch wider than 16:9; fitted wider than 4:3). The save and load screens leave
    /// the world drawn behind them (1.1's state 3), and until then it showed either side of them on a wide picture,
    /// where Options and the map (state 4, no world) had black. Each band goes a unit under the sheet's edge, which
    /// the screen's own art then covers. Nothing where the sheet covers the picture (1.1's stretch; the 16:9 stretch
    /// at 16:9), and black on black in the front end.
    /// </summary>
    void Bands(int width, int height)
    {
        bool centred = UiLayout.Seat == SheetSeat.Centre && UiLayout.Seated(width, height);
        if (!centred && !Menus.WholeSheet) return;
        var map = UiLayout.On(width, height);
        float above = map.Y / map.ScaleY, below = (height - map.Bottom) / map.ScaleY;
        float left = map.X / map.ScaleX, right = (width - map.X) / map.ScaleX - UiAssets.Width;
        var black = new Tint(0f, 0f, 0f);
        if (above > 0f) ui.Box(0f, UiAssets.Height - 1f, UiAssets.Width, above + 1f, black);
        if (below > 0f) ui.Box(0f, -below, UiAssets.Width, below + 1f, black);
        // Either side, the picture's whole height (the corners too, where there are bands above and below as well).
        float bottom = -MathF.Max(0f, below), tall = UiAssets.Height + MathF.Max(0f, above) + MathF.Max(0f, below);
        if (left > 0f) ui.Box(-left, bottom, left + 1f, tall, black);
        if (right > 0f) ui.Box(UiAssets.Width - 1f, bottom, right + 1f, tall, black);
    }

    /// <summary>
    /// **The rows above a bottom-seated sheet, black while a dialog against its top edge is up** (ours).
    /// The escape menu's ankh is cut flat at the sheet's top in the art itself; on 1.1's
    /// screen, and on the 16:9 stretch at 16:9 and wider, that is the picture's own top edge. On a picture narrower than
    /// 16:9 the sheet sits at the bottom with the world above it, and the cut showed against the world. While
    /// such a dialog is up (<see cref="FrontEnd.DialogAgainstTop"/>) the rows above the sheet are black. It is drawn
    /// first of the interface (the game puts it ahead of the bar and the panes), so a pane centred above the bar
    /// (<see cref="UiLayout.Panes(float, float)"/>) stays drawn over it; the world behind the menu inside the sheet
    /// still shows. Nothing anywhere else: the sheet seated in the centre has its own bands (<see cref="Bands"/>).
    /// </summary>
    public IUiPass TopBand => topBand ??= new TopBandPass(this);

    IUiPass? topBand;

    sealed class TopBandPass(MenuPass menu) : IUiPass
    {
        public UiCanvas Ui => menu.Ui;

        public void Emit(int width, int height, FreeCamera camera, Simulation sim)
        {
            if (width <= 0 || height <= 0 || UiLayout.Seat != SheetSeat.Bottom || !UiLayout.Seated(width, height)) return;
            if (!menu.Menus.DialogAgainstTop) return;
            menu.Ui.Begin(width, height);
            var map = UiLayout.On(width, height);
            float above = map.Y / map.ScaleY;
            if (above > 0f) menu.Ui.Box(0f, UiAssets.Height - 1f, UiAssets.Width, above + 1f, new Tint(0f, 0f, 0f));
        }
    }

    void Quads(NuiPane nui)
    {
        float px = nui.Pos.Length > 1 ? nui.Pos[0] : 0, py = nui.Pos.Length > 1 ? nui.Pos[1] : 0;
        foreach (var q in nui.Quads) ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);
    }

    void DrawPane(MenuPane pane)
    {
        Quads(pane.Nui);
        float px = pane.X, py = pane.Y;
        // A pane in our lettering has every word in our faces (ours, MenuText); any other, 1.1's bitmap faces.
        bool lettered = pane.Lettered;
        foreach (var e in pane.Nui.Elements)
        {
            if (pane.Hidden.Contains(e.Name) || e.Pos.Length < 2 || e.Size.Length < 2) continue;
            var (x, y) = pane.At(e);
            switch (e.Type)
            {
                case "button":
                    if (Picture(e, pane.Buttons.GetValueOrDefault(e.Name)) is { } art) ui.Draw(art, x, y, e.Size[0], e.Size[1]);
                    else if (pane.Texts.TryGetValue(e.Name, out var caption) && !pane.Worded.Contains(e.Name))
                    {
                        if (lettered) DrawOurCaption(e, caption, pane.Buttons.GetValueOrDefault(e.Name), x, y);
                        else DrawCaption(e, caption, pane.Buttons.GetValueOrDefault(e.Name), x, y);
                    }
                    break;
                case "imagearray" when pane.Images.TryGetValue(e.Name, out var images) && images.Shown is { } picture:
                    ui.Draw(picture, x, y, e.Size[0], e.Size[1]);
                    break;
                case "checkbox" when pane.Checks.TryGetValue(e.Name, out bool on):
                {
                    // The checked or plain bitmap (the box's set swaps between them); a locked box
                    // of ours is drawn at half strength, the way the game greys what cannot be used.
                    string? box = on ? e.Get("checkedbitmap") : e.Get("bitmap");
                    if (box is not null)
                        ui.Draw(box, x, y, e.Size[0], e.Size[1], pane.Locked.Contains(e.Name) ? new Tint(1f, 1f, 1f, 0.5f) : Tint.White);
                    break;
                }
                case "scrollslider" when e.Get("bitmap") is { } knob
                                         && pane.Bars.Values.FirstOrDefault(b => ReferenceEquals(b.Slider, e)) is { } valued:
                    ui.Draw(knob, px + valued.Element.Pos[0] + valued.SliderX, py + valued.Element.Pos[1], e.Size[0], e.Size[1]);
                    break;
                case "textlistbox" when pane.Lists.TryGetValue(e.Name, out var list):
                    if (lettered) DrawOurList(e, list, x, y);
                    else DrawList(list, x, y);
                    break;
                case "scrollslider" when e.Get("bitmap") is { } knob
                                         && pane.Nui.Elements.FirstOrDefault(b => b.Type == "scrollbar"
                                             && e.Name.Equals(b.Get("slider"), StringComparison.OrdinalIgnoreCase)) is { } bar
                                         && pane.Lists.Values.FirstOrDefault(l => bar.Name.Equals(l.Element.Get("vscrollbar"),
                                             StringComparison.OrdinalIgnoreCase)) is { } scrolled:
                    ui.Draw(knob, px + bar.Pos[0], py + scrolled.SliderY(bar, e), e.Size[0], e.Size[1]);
                    break;
                case "scrollslider" when e.Get("bitmap") is { } knob && pane.Lift.ContainsKey(e.Name):
                    ui.Draw(knob, x, y, e.Size[0], e.Size[1]);                  // one its page moves itself (ours: the Fixes page's)
                    break;
                case "textbox" when pane.Texts.TryGetValue(e.Name, out var said) && said.Length > 0:
                    if (lettered) DrawOurTextBox(pane, e, said, x, y);
                    else DrawTextBox(e, said, x, y);
                    break;
                case MenuLettering.WordsType:
                    DrawWords(pane, e, x, y);
                    break;
                case "edit" when lettered && pane.Edits.TryGetValue(e.Name, out var ours):
                    DrawOurEdit(e, ours, x, y);
                    break;
                case "edit" when pane.Edits.TryGetValue(e.Name, out var edit):
                {
                    var font = edit.Font;
                    // Left and against the top, slid left by the field's own offset, cut at its edges.
                    ui.TextClipped(font, x + edit.Offset, y + e.Size[1] - font.LineHeight, edit.Shown, Tint.White,
                                   x, y, x + e.Size[0], y + e.Size[1]);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A button's picture for its state (one bitmap a state): the state's own bitmap, else the
    /// `bitmap=` one — and nothing at all for a button without a `bitmap=` in state 0, which is
    /// why the escape menu's words are the pane's art rather than its buttons'.
    /// </summary>
    public static string? Picture(NuiElement button, ButtonState state)
    {
        string? normal = button.Get("bitmap");
        return state switch
        {
            ButtonState.Rollover => button.Get("rollover") ?? normal,
            ButtonState.Pressed => button.Get("depressed") ?? normal,
            ButtonState.Greyed => button.Get("greyed") ?? normal,
            _ => normal,
        };
    }

    /// <summary>
    /// A button of Vitomir's own with no art in any state (the Fixes and Keys buttons, when 1.1's menus
    /// wear 1.1's own lettering; ours draws its words as a `words` element and skips this): its
    /// caption centred in the button's own font, white, yellow under the pointer and red while held —
    /// the text list's three colours, so it lights the way the game's lists do.
    /// </summary>
    void DrawCaption(NuiElement e, string caption, ButtonState state, float x, float y)
    {
        var font = ui.Assets.Font(MenuPane.FontOf(e));
        uint colour = state switch
        {
            ButtonState.Pressed => MenuList.CurrentColour,
            ButtonState.Rollover => MenuList.HoverColour,
            _ => MenuList.RowColour,
        };
        float tx = x + (e.Size[0] - font.Measure(caption)) / 2f, ty = y + (e.Size[1] - font.LineHeight) / 2f;
        ui.Text(font, tx, ty, caption, Tint.From(colour));
    }

    void DrawList(MenuList list, float x, float y)
    {
        for (int i = 0; i < list.Rows.Count; i++)
        {
            if (!list.Visible(i)) continue;
            uint colour = i == list.Current ? MenuList.CurrentColour
                        : i == list.Hovered ? MenuList.HoverColour : list.Rows[i].Colour;
            foreach (var (text, lx, ly) in list.LinesOf(i))
                ui.TextClipped(list.Font, x + lx, y + ly, text, Tint.From(colour), x, y, x + list.Width, y + list.Height);
        }
    }

    /// <summary>The tip box's colours: a dark panel with a thin gold rim, its words a warm white (ours).</summary>
    static readonly Tint TipPanel = new(0.05f, 0.04f, 0.03f, 0.94f), TipRim = new(0.72f, 0.58f, 0.30f), TipWords = new(0.93f, 0.89f, 0.80f);

    /// <summary>
    /// A control's tip by the pointer (ours, <see cref="MenuTips"/>): its words wrapped to <see cref="MenuTips.Width"/> in
    /// <c>defaultFont10</c> on a dark panel with a gold rim, below and right of the pointer, moved to stay on the sheet.
    /// </summary>
    void DrawTip(string text, float px, float py)
    {
        var font = ui.Assets.Font("defaultfont10");
        var lines = TextBoxText.Lines(text, true, MenuTips.Width, c => font[c].Advance);
        if (lines.Count == 0) return;
        int pad = MenuTips.Pad, line = font.LineHeight + 1;
        float w = lines.Max(l => (float)font.Measure(l)) + 2 * pad, h = lines.Count * line - 1 + 2 * pad;
        float x = px + 14f, top = py - 18f;
        if (x + w > UiAssets.Width - 2) x = px - w - 6f;
        float y = top - h;
        if (y < 2f) y = py + 18f;
        x = Math.Clamp(x, 2f, UiAssets.Width - 2f - w);
        y = Math.Clamp(y, 2f, UiAssets.Height - 2f - h);
        ui.Box(x - 1f, y - 1f, w + 2f, h + 2f, TipRim);
        ui.Box(x, y, w, h, TipPanel);
        for (int k = 0; k < lines.Count; k++)
            ui.Text(font, x + pad, y + h - pad - (k + 1) * line + 1, lines[k], TipWords);
    }

    void DrawTextBox(NuiElement box, string text, float x, float y)
    {
        var font = ui.Assets.Font(MenuPane.FontOf(box));
        var flags = NuiPane.TextBox(box);
        // Streamer mode: an address, the user's folder or a secret in a text box is hidden as it is drawn.
        text = Vitomir.Platform.Presentation.StreamerMode.Shown(text);
        var lines = TextBoxText.Lines(text, flags.HasFlag(NuiPane.TextBoxFlags.WordWrap), box.Size[0], c => font[c].Advance);
        var tint = CharacterPass.TextColour(box);
        // Our pages' hint boxes leave room between lines (`spacing`, ours); 1.1's own boxes have none.
        int spacing = int.TryParse(box.Get("spacing"), out int sp) ? sp : 0;
        for (int k = 0; k < lines.Count; k++)
        {
            var (tx, ty) = NuiPane.TextBoxLine(box, box.Size[0], box.Size[1], font.Measure(lines[k]), font.LineHeight, k, lines.Count, spacing);
            ui.TextClipped(font, x + tx, y + ty, lines[k], tint, x, y, x + box.Size[0], y + box.Size[1]);
        }
    }

    // ---- our lettering (ours, MenuLettering) ------------------------------------------------------------------

    /// <summary>
    /// A control's text in our faces (ours, <see cref="MenuText"/>), its em scaled from <c>defaultFont10</c> by the
    /// line height of the control's own face of 1.1's, so a larger face of 1.1's gives larger letters.
    /// </summary>
    float EmOf(NuiElement e, MenuText.Kind kind)
    {
        float baseLine = ui.Assets.Font("defaultfont10").LineHeight;
        float line = ui.Assets.Font(MenuPane.FontOf(e)).LineHeight;
        return MenuText.EmOf(kind) * (baseLine > 0 ? line / baseLine : 1f);
    }

    /// <summary>
    /// A one-line text's em, made smaller when the line would not fit its box at its own (our faces are wider than 1.1's
    /// bitmap ones), so it is never cut; never below four fifths of it.
    /// </summary>
    static float Fitted(MenuText.Kind kind, string text, float em, float width)
    {
        float lw = MenuText.Measure(kind, text, em);
        return lw > width && lw > 0f ? em * Math.Max(0.8f, width / lw) : em;
    }

    /// <summary>A line of our text from its left end at a baseline, cut at a box.</summary>
    void OurLine(MenuText.Kind kind, string text, float em, float x, float baseline, Tint fill, float left, float bottom, float right, float top)
    {
        foreach (var run in MenuText.Runs(kind, text, em))
            x = ui.LettersClipped(run.Face, x, baseline, run.Em, run.Text, fill, TextLook, left, bottom, right, top);
    }

    /// <summary>Text on a panel in our faces: a thin dark rim and a soft shadow, so it reads on 1.1's dark boxes as its own text did.</summary>
    static readonly LetterStyle TextLook = new(Outline: Rgb(14, 10, 6), OutlineWidth: 0.03f,
                                               Shadow: new Tint(0f, 0f, 0f, 0.55f), ShadowX: 0.04f, ShadowY: 0.05f, ShadowSoftness: 0.05f);

    /// <summary>
    /// A text box in our faces: its lines broken as 1.1 breaks them but measured in ours, placed as 1.1 places a box's
    /// lines (across: left or centred; down: from the top, or the block in the middle), a one-line box's text with its
    /// middle on the box's (<see cref="MenuText.NameMiddle"/>).
    /// </summary>
    void DrawOurTextBox(MenuPane pane, NuiElement box, string text, float x, float y)
    {
        var kind = MenuText.KindOf(box);
        float em = EmOf(box, kind);
        var flags = NuiPane.TextBox(box);
        float w = box.Size[0], h = box.Size[1];
        bool wrap = flags.HasFlag(NuiPane.TextBoxFlags.WordWrap);
        if (!wrap) em = Fitted(kind, text, em, w);
        var lines = MenuText.Lines(kind, text, wrap, w, em);
        var tint = CharacterPass.TextColour(box);
        float pitch = kind == MenuText.Kind.Sentence ? em * MenuText.SentencePitch : em * 0.92f;
        int spacing = int.TryParse(box.Get("spacing"), out int sp) ? sp : 0;
        float block = lines.Count * pitch + Math.Max(0, lines.Count - 1) * spacing;
        // The block's top, as 1.1 places it: in the middle of the box, against its foot (a quarter line up), or against its top.
        float top = flags.HasFlag(NuiPane.TextBoxFlags.VCentre) ? y + (h + block) / 2f
                  : flags.HasFlag(NuiPane.TextBoxFlags.FromBottom) ? y + pitch / 4f + block
                  : y + h;
        float firstMiddle = top - pitch / 2f;
        for (int k = 0; k < lines.Count; k++)
        {
            float lw = MenuText.Measure(kind, lines[k], em);
            float lx = flags.HasFlag(NuiPane.TextBoxFlags.Centre) ? x + (w - lw) / 2f : x;
            float baseline = firstMiddle - k * (pitch + spacing) - em * MenuText.MiddleOf(kind);
            OurLine(kind, lines[k], em, lx, baseline, tint, x, y, x + w, y + h);
        }
    }

    /// <summary>
    /// An edit field in our faces (ours): what is typed, as a name, left in the field and its middle on the
    /// field's, slid left as 1.1 slides it when it is longer than the field so its end and the caret show, cut at the
    /// field's edges.
    /// </summary>
    void DrawOurEdit(NuiElement e, MenuEdit edit, float x, float y)
    {
        const MenuText.Kind kind = MenuText.Kind.Name;
        float em = EmOf(e, kind);
        string shown = edit.Shown;
        float w = e.Size[0], h = e.Size[1];
        float lw = MenuText.Measure(kind, shown, em);
        float slide = lw > w ? w - lw : 0f;
        OurLine(kind, shown, em, x + slide, y + h / 2f - em * MenuText.MiddleOf(kind), Tint.White, x, y, x + w, y + h);
    }

    /// <summary>
    /// A caption of the credits in our faces (ours): a heading gold and larger, a name bone with 1.1's dark glow,
    /// centred across the sheet, rising and fading as 1.1's do.
    /// </summary>
    void OurCaption(Caption c, NfdFont font)
    {
        bool title = c.Font.Equals(FrontEnd.CreditsTitleFont, StringComparison.OrdinalIgnoreCase);
        float em = title ? MenuLettering.TitleEm * 1.5f : MenuLettering.TitleEm * 1.15f;
        float alpha = c.Alpha(Menus.NowMs);
        float baseline = c.PenYAt(font.LineHeight, Menus.NowMs) + font.LineHeight / 2f - em * MenuText.NameMiddle;
        float x = UiAssets.Width / 2f - MenuText.Measure(MenuText.Kind.Name, c.Text, em) / 2f;
        var fill = title ? Gold with { A = alpha } : Beige with { A = alpha };
        foreach (var run in MenuText.Runs(MenuText.Kind.Name, c.Text, em))
            x = ui.Letters(run.Face, x, baseline, run.Em, run.Text, fill, title ? PanelLook : MenuLook);
    }

    /// <summary>A button's caption of ours in our faces: centred, in the list's colours as <see cref="DrawCaption"/> has them.</summary>
    void DrawOurCaption(NuiElement e, string caption, ButtonState state, float x, float y)
    {
        uint colour = state switch
        {
            ButtonState.Pressed => MenuList.CurrentColour,
            ButtonState.Rollover => MenuList.HoverColour,
            _ => MenuList.RowColour,
        };
        var kind = MenuText.KindOf(e);
        float em = Fitted(kind, caption, EmOf(e, kind), e.Size[0] - 2f);
        float lw = MenuText.Measure(kind, caption, em);
        float baseline = y + e.Size[1] / 2f - em * MenuText.MiddleOf(kind);
        OurLine(kind, caption, em, x + (e.Size[0] - lw) / 2f, baseline, Tint.From(colour), x, y, x + e.Size[0], y + e.Size[1]);
    }

    /// <summary>
    /// A text list in our faces: 1.1's rows where 1.1 lays them (so a click finds the same row), each line's text set in
    /// the middle of the line 1.1's face gave it, in the list's colours, cut at the box.
    /// </summary>
    void DrawOurList(NuiElement e, MenuList list, float x, float y)
    {
        var kind = MenuText.KindOf(e);
        float em = EmOf(e, kind);
        float line = list.Font.LineHeight;
        for (int i = 0; i < list.Rows.Count; i++)
        {
            if (!list.Visible(i)) continue;
            uint colour = i == list.Current ? MenuList.CurrentColour
                        : i == list.Hovered ? MenuList.HoverColour : list.Rows[i].Colour;
            foreach (var (text, lx, ly) in list.LinesOf(i))
                OurLine(kind, text, em, x + lx, y + ly + line / 2f - em * MenuText.MiddleOf(kind), Tint.From(colour),
                        x, y, x + list.Width, y + list.Height);
        }
    }

    /// <summary>1.1's colours for a menu word, read off its buttons' pictures: at rest, under the pointer, pressed.</summary>
    static readonly Tint Beige = Rgb(210, 184, 163), Red = Rgb(168, 0, 1), White = Rgb(250, 250, 250);
    /// <summary>1.1's gold, read off its panels' words.</summary>
    static readonly Tint Gold = Rgb(222, 186, 92);

    static Tint Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A menu word: a soft dark glow round it and a soft shadow under it, as 1.1's pictured words have.</summary>
    static readonly LetterStyle MenuLook = new(Glow: new Tint(0f, 0f, 0f, 0.70f), GlowReach: 0.16f,
                                               Shadow: new Tint(0f, 0f, 0f, 0.55f), ShadowX: 0.04f, ShadowY: 0.05f, ShadowSoftness: 0.05f);
    /// <summary>A panel's word: a thin dark rim and a shadow, as 1.1's gold labels have.</summary>
    static readonly LetterStyle PanelLook = new(Outline: Rgb(18, 12, 6), OutlineWidth: 0.035f,
                                                Shadow: new Tint(0f, 0f, 0f, 0.6f), ShadowX: 0.05f, ShadowY: 0.06f, ShadowSoftness: 0.06f);

    /// <summary>
    /// A word of ours (<see cref="MenuLettering.Words"/>): its lower case in Vitomir Uncial, a capital in Vitomir Capitals,
    /// in its style — and, for a button's word, in the button's colour of the moment (hidden with the button).
    /// </summary>
    void DrawWords(MenuPane pane, Vitomir.Formats.NuiElement e, float x, float y)
    {
        var w = MenuLettering.Read(e);
        if (w.Text.Length == 0) return;
        Tint fill = w.Style == MenuLettering.MenuStyle ? Beige : Gold;
        if (w.Button is { } button)
        {
            if (pane.Hidden.Contains(button)) return;
            fill = pane.Buttons.GetValueOrDefault(button) switch
            {
                ButtonState.Rollover => Red,
                ButtonState.Pressed => White,
                ButtonState.Greyed => Beige with { A = 0.45f },
                _ => fill,
            };
        }
        var look = w.Style == MenuLettering.MenuStyle ? MenuLook : PanelLook;
        // A word's place is in fractions of a unit (pane.At's corner is whole units): the pane's place, the word's, its lift.
        x = pane.X + w.X;
        y = pane.Y + w.Baseline + pane.Lift.GetValueOrDefault(e.Name);
        float left = w.Align == "left" ? x : x - MenuLettering.Measure(w.Text, w.Em) / 2f;
        foreach (var (run, capitals) in MenuLettering.Runs(w.Text))
            left = capitals
                ? ui.Letters(Lettering.Load(Lettering.Capitals), left, y, w.Em * MenuLettering.CapitalScale, run, fill, look)
                : ui.Letters(Lettering.Load(Lettering.Uncial), left, y, w.Em, run, fill, look);
    }
}
