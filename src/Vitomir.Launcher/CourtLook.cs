// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's screen drawn in the Court of Night kit: its pictures sliced, tiled and stretched, its states.
using System.Numerics;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Render;

namespace Vitomir.Launcher;

/// <summary>
/// **The launcher's look** (<see cref="ISkin"/>): every piece of <c>Screens/launcher.json</c> drawn with the Court of Night kit
/// (<see cref="CourtKit"/>), in the kit's order: a framed panel is the kit's frame and field (<c>panel</c>, nine-sliced), its title
/// band velvet (<c>velvet</c> tiled, <c>velvet_shade</c> stretched over it) holding the page's name as a picture, a frieze
/// (<c>frieze</c>, tiled across) under the band and over the notice's action band (the kit's <c>band_dark</c>), what it holds,
/// and its four corner knots (<c>corner_plate</c>) last; the notice's panel over the kit's scrim. Buttons are <c>button_*</c>,
/// a row's small buttons <c>button_quiet_*</c>, the left menu's tabs <c>nav_selected</c>, <c>nav_hover</c> and <c>nav_focused</c> (the
/// 8-wide mark beside the chosen one in <c>mark_selected</c>), the folder box <c>field_*</c>, the scroll bar <c>scroll_track</c> and
/// <c>scroll_thumb_*</c>, the progress <c>meter_track</c> with <c>meter_fill</c> inset 2, a toggle with its options side by side one <c>toggle_*</c>
/// segment for each option, 2 apart, a checkbox and a radio <c>checkbox_*</c> and <c>radio_*</c>, a list's stripes the kit's <c>band</c> and <c>bandAlt</c>, the copy in use <c>row_chosen</c>;
/// each state's words in its <c>text_rgb</c>. Behind it all the background picture, covering the window; the title is the
/// wordmark's picture. The pieces' places are the layout's: the look draws into the rectangles the screen laid out.
/// <para>Every picture is sent once to the list it is drawn into (<see cref="DrawList.Resources"/>, mipmapped: the art is painted at
/// twice the laid-out size and drawn smaller), its colour carried into its transparent pixels first so that its smaller copies
/// keep no dark fringe; the renderer blends them over what is under them by their alpha (straight).</para>
/// </summary>
public sealed class CourtLook : ISkin
{
    readonly CourtKit kit;
    readonly Dictionary<string, Art> art = new(StringComparer.Ordinal);
    DrawList? sentTo;

    sealed record Art(int Id, int Width, int Height, byte[] Rgba);

    /// <summary>The page names drawn as pictures, by the page's key: every page's.</summary>
    public static readonly IReadOnlyList<string> PageNames = [.. Launcher.Pages];

    /// <summary>The kit's pieces this look draws.</summary>
    public static readonly IReadOnlyList<string> KitPieces =
    [
        "panel", "corner_plate", "frieze", "velvet", "velvet_shade",
        "button_normal", "button_hover", "button_pressed", "button_disabled", "button_focused",
        "button_quiet_normal", "button_quiet_hover", "button_quiet_pressed", "button_quiet_disabled", "button_quiet_focused",
        "nav_selected", "nav_hover", "nav_focused",
        "field_normal", "field_hover", "field_focused", "field_disabled", "field_error",
        "scroll_track", "scroll_thumb_normal", "scroll_thumb_hover", "scroll_thumb_pressed",
        "meter_track", "meter_fill",
        "toggle_on", "toggle_off", "toggle_off_hover", "toggle_on_disabled", "toggle_off_disabled",
        "row_chosen",
        "row_focused",
        "checkbox_off", "checkbox_off_hover", "checkbox_off_focused", "checkbox_off_pressed", "checkbox_off_disabled",
        "checkbox_on", "checkbox_on_hover", "checkbox_on_focused", "checkbox_on_pressed", "checkbox_on_disabled",
        "radio_off", "radio_off_hover", "radio_off_focused", "radio_off_pressed", "radio_off_disabled",
        "radio_on", "radio_on_hover", "radio_on_focused", "radio_on_pressed", "radio_on_disabled",
        // The toggle switch, for a setting that acts at once: the Setup page's shortcuts.
        "switch_off", "switch_off_hover", "switch_off_focused", "switch_off_disabled",
        "switch_on", "switch_on_hover", "switch_on_focused", "switch_on_disabled",
    ];

    /// <summary>How far outside a button's rectangle its picture reaches (the room its focus ring is painted in), units.</summary>
    const float ButtonMargin = 5f;
    /// <summary>A tab's words, in from its left edge, units.</summary>
    const float TabInset = 28f;
    /// <summary>
    /// The page names' letter band, laid out 56 units high (of the title band's 60): each picture is 216 px tall at 4x, the 192 px band
    /// with 12 px above and below it, so the picture is 63 units and stands 3.5 above the band.
    /// </summary>
    const float PageNameBand = 56f, PageNamePicture = PageNameBand * 216f / 192f, PageNameAbove = PageNameBand * 12f / 192f;
    /// <summary>The scroll bar's width (its pictures are twice that), units.</summary>
    const float BarWidth = 12f;

    /// <summary>The launcher's look, made once (its pictures decoded once a process).</summary>
    public static CourtLook Court => court.Value;
    static readonly Lazy<CourtLook> court = new(() => new CourtLook(CourtKit.Court));

    public CourtLook(CourtKit kit)
    {
        this.kit = kit;
        int id = 1;
        foreach (var name in KitPieces) art[name] = Load($"court/{kit.Piece(name).File}", id++);
        art["background"] = Load("launcher_background.png", id++);
        art["wordmark"] = Load("vitomir_wordmark.png", id++);
        foreach (var page in PageNames) art["page:" + page] = Load($"page-names/{page}.png", id++);
    }

    /// <summary>The pictures it draws from, by name (a kit piece's, <c>background</c>, <c>wordmark</c>, <c>page:setup</c> …).</summary>
    public IReadOnlyCollection<string> Pictures => art.Keys;

    /// <summary>A picture's id in the draw list, by its name.</summary>
    public int PictureId(string name) => art[name].Id;

    static Art Load(string resource, int id)
    {
        using var s = typeof(CourtLook).Assembly.GetManifestResourceStream("Launcher/" + resource)
                      ?? throw new InvalidOperationException($"the launcher's picture Launcher/{resource} is not in vitomir-launcher");
        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        var rgba = Png.Decode(bytes, out int w, out int h) ?? throw new InvalidDataException($"Launcher/{resource} does not decode");
        Bleed(rgba, w, h);
        return new Art(id, w, h, rgba);
    }

    /// <summary>
    /// Each wholly transparent pixel given the colour of the nearest pixel that is not (alpha kept at 0), so a picture's smaller
    /// copies, made by averaging, keep no dark fringe at its edges.
    /// </summary>
    internal static void Bleed(byte[] rgba, int w, int h)
    {
        var queue = new Queue<int>();
        var done = new bool[w * h];
        for (int i = 0; i < w * h; i++)
            if (rgba[4 * i + 3] > 0) { done[i] = true; queue.Enqueue(i); }
        if (queue.Count == 0 || queue.Count == w * h) return;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int n = ny * w + nx;
                if (done[n]) continue;
                done[n] = true;
                rgba[4 * n] = rgba[4 * i];
                rgba[4 * n + 1] = rgba[4 * i + 1];
                rgba[4 * n + 2] = rgba[4 * i + 2];
                queue.Enqueue(n);
            }
        }
    }

    /// <summary>The pictures sent to <paramref name="list"/> once (again when the list is another).</summary>
    void Send(DrawList list)
    {
        if (ReferenceEquals(sentTo, list)) return;
        foreach (var a in art.Values) list.Resources.Picture(new DrawPicture(a.Id, a.Width, a.Height, a.Rgba, Mipmaps: true));
        sentTo = list;
    }

    // ---- the pieces ----------------------------------------------------------------------------------------------------------

    public bool Draw(Painter p, Piece piece)
    {
        Send(p.List);
        switch (piece)
        {
            case Panel panel: DrawPanel(p, panel); return true;
            case Button button: DrawButton(p, button); return true;
            case Toggle toggle: DrawToggle(p, toggle); return true;
            case Choice choice: DrawChoice(p, choice); return true;
            case Meter meter: DrawMeter(p, meter); return true;
            case Scroll scroll: DrawScroll(p, scroll); return true;
            case ListPiece list when list.Stripes.Length > 0:
                for (int i = 0; i < list.Children.Count; i++)
                    if (list.Children[i].Shown) p.Solid(list.Children[i].Rect, Colour(list.Stripes[i % list.Stripes.Length]));
                list.DrawChildren(p);
                return true;
            case TextBox box: DrawTextBox(p, box); return true;
            case ReadyMark { Mark: "open" or "closed" } chevron:
                {
                    // A row's chevron: quiet (the muted words), the gold while its row's toggle is under the pointer or has the focus.
                    bool lit = false;
                    for (var a = chevron.Parent; a is not null && !lit; a = a.Parent)
                        if (a is Choice toggle) { lit = p.Hovered(toggle) || Ring(p, toggle); break; }
                    chevron.Draw(p, kit.Colour(lit ? "text_gold" : "text_muted"));
                    return true;
                }
            case ReadyMark mark:
                // A readiness item's mark in the kit's colours: ready the words' own, not ready the warning red, a warning the amber, waiting muted.
                mark.Draw(p, kit.Colour(mark.Mark switch { "failed" => "text_warn", "warning" => "text_caution", "waiting" => "text_muted", _ => "text" }));
                return true;
            case DragHandle handle:
                // A row's grip: three bars in the kit's muted words, its gold while the pointer is over it or the row is dragged.
                DragHandle.DrawBars(p, handle, kit.Colour(p.Hovered(handle) || p.Screen.Drag?.Handle == handle ? "text_gold" : "text_muted"));
                return true;
            case TextPiece { StyleName: "title" } title:
                {
                    // The wordmark's picture in the box the title's words would have had, its height, from its left.
                    var a = art["wordmark"];
                    float h = title.Rect.Height;
                    Stretch(p, "wordmark", new UiRect(title.Rect.X, title.Rect.Y, h * a.Width / a.Height, h));
                    return true;
                }
            case TextPiece { Id: "pageTitle" } heading when art.ContainsKey("page:" + heading.Scope.Text("page")):
                {
                    // The page's name as a picture, its letter band laid out 56 high (of the band's 60) in the middle of the title band.
                    string name = "page:" + heading.Scope.Text("page");
                    var a = art[name];
                    float w = PageNamePicture * a.Width / a.Height;
                    var r = heading.Rect;
                    Stretch(p, name, new UiRect(r.X, r.Y + (r.Height - PageNameBand) * 0.5f - PageNameAbove, w, PageNamePicture));
                    return true;
                }
        }
        return false;
    }

    void DrawPanel(Painter p, Panel panel)
    {
        var r = panel.Rect;
        switch (panel.Id)
        {
            case "backdrop":
                Cover(p, "background", r);
                return;
            case "pageBand" or "noticeBand":
                // The title band: velvet tiled, the light from above stretched over it.
                Tile(p, "velvet", r, across: true, down: true);
                Stretch(p, "velvet_shade", r);
                panel.DrawChildren(p);
                return;
            case "pageFrieze" or "noticeFrieze" or "noticeFrieze2":
                Tile(p, "frieze", r, across: true, down: false);
                return;
            case "noticeActions":
                p.Solid(r, kit.Colour("band_dark"));
                panel.DrawChildren(p);
                return;
        }
        if (panel.Framed)
        {
            // The notice stands over the kit's scrim; a framed panel is the frame and field, what it holds, its four knots last.
            if (panel.Id == "noticePanel") p.Solid(new UiRect(0f, 0f, p.Metrics.Width, p.Metrics.Height), kit.Colour("scrim"));
            Nine(p, "panel", r);
            panel.DrawChildren(p);
            var plate = kit.Piece("corner_plate");
            float z = plate.Width / CourtKit.Scale;
            foreach (var (x, y) in new[] { (r.X, r.Y), (r.Right - z, r.Y), (r.X, r.Bottom - z), (r.Right - z, r.Bottom - z) })
                Stretch(p, "corner_plate", new UiRect(x, y, z, z));
            return;
        }
        // A plain panel: its fill in the kit's colours (a stripe, the nav's mark, a rule); the chosen row's edge and wash.
        p.Solid(r, Colour(panel.Scope.Fill(panel.Fill)));
        if (panel.Edge is not null && panel.Scope.Fill(panel.Edge) is var edge)
        {
            if (edge == "chosenEdge") Nine(p, "row_chosen", r);
            else if (edge == "rule") p.Edge(r, 1.5f, kit.Colour("rule"));          // a tag's, the details panel's fine frame
        }
        panel.DrawChildren(p);
        // A row of a list the keyboard walks (the copies of the game, Another folder, the modules), chosen or not, with the focus in it.
        if (KeyboardRow(panel) && p.Screen.FocusShown && p.Screen.Focus is { } focus && Within(focus, panel)) Nine(p, "row_focused", r);
    }

    /// <summary>Whether <paramref name="panel"/> is a row of a list the keyboard walks row by row (its focus drawn as <c>row_focused</c>).</summary>
    static bool KeyboardRow(Panel panel) => panel.Id == "otherRow" || panel.Parent is ListPiece { Id: "installs" or "moduleRows" };

    static bool Within(Piece piece, Piece row)
    {
        for (var a = piece; a is not null; a = a.Parent) if (a == row) return true;
        return false;
    }

    /// <summary>A colour the layout names, in the kit's: its stripes, its rule, the chosen tab's mark; the chosen row's fill is its picture's wash.</summary>
    Rgba Colour(string name) => name switch
    {
        "band" => kit.Colour("band"),
        "bandAlt" => kit.Colour("bandAlt"),
        "rule" => kit.Colour("rule"),
        "chosenEdge" => kit.Colour("mark_selected"),
        "chosen" or "none" or "" => default,
        _ => Screen.Colour(name),
    };

    void DrawButton(Painter p, Button b)
    {
        bool hovered = p.Hovered(b), pressed = p.Pressed(b), focused = Ring(p, b);
        string text = b.Scope.Fill(b.Text);
        if (b.StyleName == "tab")
        {
            string? ground = b.Selected ? "nav_selected" : hovered || pressed ? "nav_hover" : null;
            if (ground is not null) Nine(p, ground, b.Rect);
            if (focused && !hovered) Nine(p, "nav_focused", b.Rect);
            var nav = kit.Piece("nav_selected");
            var colour = b.Selected ? nav.Text : hovered || pressed ? kit.Piece("nav_hover").Text : nav.TextUnselected;
            p.Text(text, b.Style, new UiRect(b.Rect.X + TabInset, b.Rect.Y, MathF.Max(0f, b.Rect.Width - TabInset), b.Rect.Height), TextAlign.Start, colour);
            return;
        }
        // A row's small button, a quiet one (Quit) and a notice's button that is not its default now are the kit's quiet ones; the
        // default the main one. A quiet button keeps its style's size.
        string kind = b.StyleName == "buttonSmall" || b.Quiet || b.DefaultWhen is not null && !b.IsDefault ? "button_quiet" : "button";
        string state = !b.Enabled ? "disabled" : pressed ? "pressed" : hovered ? "hover" : focused ? "focused" : "normal";
        var piece = kind + "_" + state;
        if (b.IconDirection != 0)
        {
            // A row's Move up and Move down: small and quiet, no plate (they read as order, not as a command of the page): the arrow in
            // the muted words, the gold under the pointer, the quiet button's disabled words when it cannot move; the focus a fine ring.
            if (focused && !hovered) p.Edge(b.Rect, 2f, kit.Colour("mark_selected"));
            var arrow = !b.Enabled ? kit.Piece("button_quiet_disabled").Text ?? kit.Colour("text_muted") : hovered || pressed ? kit.Colour("text_gold") : kit.Colour("text_muted");
            p.ArrowUpDown(b, b.IconDirection, arrow);
            return;
        }
        Nine(p, piece, Outset(b.Rect, ButtonMargin));
        p.Text(text, b.Style, b.Rect, TextAlign.Centre, kit.Piece(piece).Text);
    }

    void DrawToggle(Painter p, Toggle t)
    {
        if (!t.Segments) { t.Draw(p); return; }
        // One segment for each option, side by side, 2 apart: the chosen one on, the one under the pointer lit.
        const float gap = 2f;
        int n = Math.Max(1, t.Options.Length);
        float w = (t.Rect.Width - gap * (n - 1)) / n;
        string now = t.Scope.Text(t.Field);
        int under = p.Hovered(t) ? t.SegmentAt(p.Screen.Pointer.X) : -1;
        bool focused = Ring(p, t) && !p.Hovered(t);
        for (int i = 0; i < t.Options.Length; i++)
        {
            var r = new UiRect(t.Rect.X + i * (w + gap), t.Rect.Y, w, t.Rect.Height);
            bool on = t.Options[i] == now;
            string piece = !t.Enabled ? (on ? "toggle_on_disabled" : "toggle_off_disabled")
                : on ? "toggle_on" : i == under || focused ? "toggle_off_hover" : "toggle_off";
            Nine(p, piece, r);
            p.Text(t.Options[i], t.Style, r, TextAlign.Centre, kit.Piece(piece).Text);
        }
    }

    /// <summary>
    /// A checkbox, a radio or a switch: its mark, the kit's <c>checkbox_*</c>, <c>radio_*</c> or <c>switch_*</c> (on or off; disabled, else focused when the keyboard
    /// put the focus there, which wins over the pointer's hover, else hover, else plain), stretched over the mark's square; its label in
    /// that state's <c>text_rgb</c>; what it holds in its own styles, or all in the disabled colour while it is disabled.
    /// </summary>
    void DrawChoice(Painter p, Choice c)
    {
        string kind = c is Radio ? "radio" : c is Switch ? "switch" : "checkbox";
        // Disabled over all; then pressed (while the pointer's button is held; a switch has no pressed picture, it acts at once),
        // focused (a key put the focus there), hover, plain.
        string state = !c.Enabled ? "_disabled" : p.Pressed(c) && c is not Switch ? "_pressed" : Ring(p, c) ? "_focused" : p.Hovered(c) ? "_hover" : "";
        string name = kind + (c.Checked ? "_on" : "_off") + state;
        if (c.MarkSize > 0f) Stretch(p, name, c.MarkRect);
        var colour = kit.Piece(name).Text;
        c.DrawLabel(p, colour);
        var was = p.WordsColour;
        if (!c.Enabled) p.WordsColour = colour;
        c.DrawChildren(p);
        p.WordsColour = was;
    }

    void DrawMeter(Painter p, Meter m)
    {
        Nine(p, "meter_track", m.Rect);
        float share = m.Share;
        if (share <= 0f) return;
        var inner = m.Rect.Inset(2f);
        Nine(p, "meter_fill", inner with { Width = inner.Width * share });
    }

    void DrawScroll(Painter p, Scroll s)
    {
        p.PushClip(s.Rect);
        s.DrawChildren(p);
        p.PopClip();
        if (s.MaxOffset <= 0f) return;
        // The bar in the room kept for it at the right: the track down it, the pointed thumb where the view is.
        var track = new UiRect(s.Rect.Right - Scroll.BarRoom + (Scroll.BarRoom - BarWidth) * 0.5f, s.Rect.Y, BarWidth, s.Rect.Height);
        Nine(p, "scroll_track", track);
        float h = MathF.Max(40f, s.Rect.Height * s.Rect.Height / s.ContentHeight);
        float y = s.Rect.Y + (s.Rect.Height - h) * (s.Offset / s.MaxOffset);
        var pointer = p.Screen.Pointer;
        bool over = pointer.X >= track.X - 4f && pointer.X < track.Right + 4f && pointer.Y >= s.Rect.Y && pointer.Y < s.Rect.Bottom;
        Nine(p, over ? "scroll_thumb_hover" : "scroll_thumb_normal", new UiRect(track.X, y, BarWidth, h));
    }

    void DrawTextBox(Painter p, TextBox box)
    {
        bool typing = box.Typing;
        string state = !box.Enabled ? "disabled" : box.Error ? "error" : typing || Ring(p, box) ? "focused" : p.Hovered(box) ? "hover" : "normal";
        var piece = kit.Piece("field_" + state);
        Nine(p, "field_" + state, box.Rect);
        const float pad = 12f;
        var inner = new UiRect(box.Rect.X + pad, box.Rect.Y, MathF.Max(0f, box.Rect.Width - 2 * pad), box.Rect.Height);
        string text = box.Scope.Text(box.Field);
        p.PushClip(inner);
        if (text.Length == 0 && !typing) p.Text(box.Placeholder, box.Style, inner, TextAlign.Start, piece.Placeholder);
        else
        {
            float width = Letters.Measure(box.Style.Lettering, text, box.Style.Em);
            float caret = typing ? 6f : 0f;
            // Longer than the box: its end shown (where one types), the start cut off at the left.
            var at = width + caret > inner.Width ? inner with { X = inner.Right - width - caret, Width = width + caret } : inner;
            p.Text(text, box.Style, at, TextAlign.Start, piece.Text);
            if (typing) p.Solid(new UiRect(at.X + width + 2f, box.Rect.Y + box.Rect.Height * 0.2f, 3f, box.Rect.Height * 0.6f), piece.Text ?? kit.Colour("text"));
        }
        p.PopClip();
    }

    /// <summary>Whether the piece has the keyboard's focus and it is to be shown (a key moved it there: <see cref="Screen.FocusShown"/>).</summary>
    static bool Ring(Painter p, Piece piece) => p.Focused(piece) && p.Screen.FocusShown;

    static UiRect Outset(UiRect r, float by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    // ---- the pictures, in the picture's pixels -------------------------------------------------------------------------------

    static float Px(float units, Painter p) => MathF.Round(units * p.Metrics.Pixel);

    void Quad(Painter p, string name, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Rgba? tint = null)
    {
        if (x1 <= x0 || y1 <= y0) return;
        p.List.Picture(new PictureQuad(art[name].Id, x0, y0, x1 - x0, y1 - y0, u0, v0, u1, v1, tint ?? new Rgba(1f, 1f, 1f)));
    }

    /// <summary>A picture stretched over a rectangle (its edges on whole pixels).</summary>
    void Stretch(Painter p, string name, UiRect r) => Quad(p, name, Px(r.X, p), Px(r.Y, p), Px(r.Right, p), Px(r.Bottom, p), 0f, 0f, 1f, 1f);

    /// <summary>
    /// A kit piece nine-sliced over a rectangle: its corners at their size (the picture's slices halved, in units), its edges
    /// stretched along, its middle stretched both ways; corners too big for the rectangle shrunk to fit. Each cut on a whole pixel.
    /// </summary>
    void Nine(Painter p, string name, UiRect r)
    {
        var k = kit.Piece(name);
        float pixel = p.Metrics.Pixel;
        float x0 = Px(r.X, p), x3 = Px(r.Right, p), y0 = Px(r.Y, p), y3 = Px(r.Bottom, p);
        float l = k.Left / CourtKit.Scale * pixel, rt = k.Right / CourtKit.Scale * pixel, t = k.Top / CourtKit.Scale * pixel, b = k.Bottom / CourtKit.Scale * pixel;
        if (l + rt > x3 - x0 && l + rt > 0f) { float f = (x3 - x0) / (l + rt); l *= f; rt *= f; }
        if (t + b > y3 - y0 && t + b > 0f) { float f = (y3 - y0) / (t + b); t *= f; b *= f; }
        float x1 = MathF.Round(x0 + l), x2 = MathF.Round(x3 - rt), y1 = MathF.Round(y0 + t), y2 = MathF.Round(y3 - b);
        Span<float> xs = [x0, x1, x2, x3], ys = [y0, y1, y2, y3];
        Span<float> us = [0f, k.Left / (float)k.Width, 1f - k.Right / (float)k.Width, 1f];
        Span<float> vs = [0f, k.Top / (float)k.Height, 1f - k.Bottom / (float)k.Height, 1f];
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
                Quad(p, name, xs[i], ys[j], xs[i + 1], ys[j + 1], us[i], vs[j], us[i + 1], vs[j + 1]);
    }

    /// <summary>A kit piece tiled at its size across a rectangle (and down it), the last tile cut at the rectangle's edge; not down: stretched to its height.</summary>
    void Tile(Painter p, string name, UiRect r, bool across, bool down)
    {
        var k = kit.Piece(name);
        float pixel = p.Metrics.Pixel;
        float x0 = Px(r.X, p), x1 = Px(r.Right, p), y0 = Px(r.Y, p), y1 = Px(r.Bottom, p);
        float tw = across ? k.Width / CourtKit.Scale * pixel : x1 - x0, th = down ? k.Height / CourtKit.Scale * pixel : y1 - y0;
        if (tw < 1f || th < 1f) return;
        for (float y = y0; y < y1; y += th)
        {
            float yb = MathF.Min(y + th, y1), v1 = (yb - y) / th;
            for (float x = x0; x < x1; x += tw)
            {
                float xb = MathF.Min(x + tw, x1);
                Quad(p, name, x, y, xb, yb, 0f, 0f, (xb - x) / tw, v1);
            }
        }
    }

    /// <summary>A picture covering a rectangle in its own proportions, cut evenly at the sides that overflow (the background).</summary>
    void Cover(Painter p, string name, UiRect r)
    {
        var a = art[name];
        float x0 = Px(r.X, p), x1 = Px(r.Right, p), y0 = Px(r.Y, p), y1 = Px(r.Bottom, p);
        float w = x1 - x0, h = y1 - y0;
        if (w <= 0f || h <= 0f) return;
        float k = MathF.Max(w / a.Width, h / a.Height);
        float su = w / (a.Width * k), sv = h / (a.Height * k);
        Quad(p, name, x0, y0, x1, y1, (1f - su) * 0.5f, (1f - sv) * 0.5f, (1f + su) * 0.5f, (1f + sv) * 0.5f);
    }
}
