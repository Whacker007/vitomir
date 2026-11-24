// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>
/// A screen at work: its tree of pieces bound to its data, laid out for a
/// picture's size and the player's scale, drawn into a <see cref="DrawList"/>, and worked by the pointer and the
/// keyboard. The pointer is tested against the same rectangles the screen was drawn from; the keyboard's focus moves
/// through the pieces that are worked (Up and Down to the nearest above or below, Tab and Shift+Tab in order), Enter
/// works the one focused, Escape fires the screen's <see cref="Back"/>. What the pieces ask for gathers in
/// <see cref="Actions"/> for the host. A layout is a pure function of the size, the scale, the file and the data.
/// </summary>
public sealed class Screen
{
    readonly List<Piece> worked = [], pointed = [];
    readonly List<UiAction> actions = [];
    readonly ScreenLoader loader;
    Vector2 pointer = new(float.NegativeInfinity);
    bool pointerDown;
    // A press on a drag handle: where it was, and the row's list and index; a drag once the pointer has moved far enough.
    Vector2 pressAt;
    (ListPiece List, int Index)? pressRow;
    // The focus to put on a row moved by a key or a drop, at the next layout (its list, its index, the focused piece's place in the row).
    (ListPiece List, int Index, int Within)? follow;

    /// <summary>How far the pointer must move, pressed on a drag handle, before the row is dragged (pixels).</summary>
    public const float DragStartPixels = 5f;

    /// <summary>The row being dragged, or null (<see cref="DragState"/>); the drop line is drawn where it would land.</summary>
    public DragState? Drag { get; private set; }

    /// <summary>The drop line's colour while a row is dragged (a look sets its own).</summary>
    public Rgba DropLineColour { get; set; } = Theme.Colours["gold"];

    internal Screen(string name, ScreenData data, ScreenLoader loader)
    {
        Name = name;
        Data = data;
        this.loader = loader;
    }

    public string Name { get; }
    public string Title { get; internal set; } = "";
    /// <summary>The file's own note about the screen, or empty.</summary>
    public string Note { get; internal set; } = "";
    public ScreenData Data { get; }
    public Piece Root { get; internal set; } = new Nothing();
    /// <summary>The action Escape fires (none: Escape does nothing here).</summary>
    public string? Back { get; internal set; }
    /// <summary>The colour laid over the picture behind the screen (a theme colour's name or <c>#rrggbbaa</c>).</summary>
    public string Veil { get; internal set; } = "none";

    /// <summary>The look its pieces are drawn in (<see cref="ISkin"/>), or null: the theme's.</summary>
    public ISkin? Skin { get; set; }

    /// <summary>
    /// Whether the focus is to be shown where a look marks it apart from the pointer's hover (the launcher's focus rings): true once a
    /// key has moved or worked the focus, false again when the pointer moves or presses. The theme's own looks do not read it.
    /// </summary>
    public bool FocusShown { get; set; }

    /// <summary>Where the pointer is, in units (negative infinity before it has moved over the screen).</summary>
    public Vector2 Pointer => pointer;

    public UiMetrics Metrics { get; private set; }
    public Piece? Focus { get; private set; }
    public Piece? Hover { get; private set; }
    public Piece? PressedPiece { get; private set; }

    /// <summary>The scrolling area whose bar is held by the pointer, and how far below the thumb's top it took it (units); or null.</summary>
    (Scroll Area, float Grip)? barHeld;

    /// <summary>What the pieces have asked for since the host last took them.</summary>
    public IReadOnlyList<UiAction> Actions => actions;

    /// <summary>The actions asked for, oldest first; the list is emptied.</summary>
    public List<UiAction> TakeActions()
    {
        var taken = new List<UiAction>(actions);
        actions.Clear();
        return taken;
    }

    internal void Fire(UiAction a) => actions.Add(a);

    /// <summary>A colour by a theme name or <c>#rrggbb[aa]</c>; nothing (transparent) for an empty or unknown one.</summary>
    public static Rgba Colour(string? name) =>
        string.IsNullOrEmpty(name) ? default : Theme.Colours.TryGetValue(name, out var c) ? c : Rgba.Parse(name) ?? default;

    // ---- layout and drawing ----

    /// <summary>Binds the pieces to the data again (a list grows or shrinks with its items) and lays the screen out on <paramref name="metrics"/>.</summary>
    public void Layout(UiMetrics metrics)
    {
        Metrics = metrics;
        Root.Parent = null;
        Root.Bind(Data, this);
        Root.Measure(new Vector2(metrics.Width, metrics.Height));
        Root.Arrange(new UiRect(0f, 0f, metrics.Width, metrics.Height));
        worked.Clear();
        pointed.Clear();
        foreach (var p in Root.Walk())
            if (p.Interactive && p.Shown && Visible(p))
            {
                pointed.Add(p);
                if (p.FocusStop) worked.Add(p);
            }
        if (follow is { } f)
        {
            // A row moved by a key or a drop: the focus goes with it, to the same piece of the row at its new place.
            follow = null;
            if (f.Index >= 0 && f.Index < f.List.Children.Count && f.List.Children[f.Index].Walk().ElementAtOrDefault(f.Within) is { } moved && worked.Contains(moved))
                Focus = moved;
        }
        if (Focus is not null && !worked.Contains(Focus)) Focus = null;
        if (Hover is not null && !pointed.Contains(Hover)) Hover = null;
        if (PressedPiece is not null && !pointed.Contains(PressedPiece)) { PressedPiece = null; Drag = null; pressRow = null; }
        Focus ??= worked.Count > 0 ? worked[0] : null;
    }

    /// <summary>Whether every piece above <paramref name="p"/> is shown.</summary>
    static bool Visible(Piece p)
    {
        for (var a = p.Parent; a is not null; a = a.Parent) if (!a.Shown) return false;
        return true;
    }

    /// <summary>The screen into <paramref name="list"/> (begun at the metrics' size): the veil, then the pieces back to front.</summary>
    public void Draw(DrawList list)
    {
        list.Begin(Metrics.PixelWidth, Metrics.PixelHeight);
        var p = new Painter(list, Metrics, this);
        list.Solid(new PixelRect(0, 0, Metrics.PixelWidth, Metrics.PixelHeight), Colour(Veil));
        if (Root.Shown) Root.Paint(p);
        if (Drag is { } d) DrawDrop(p, d);
    }

    /// <summary>
    /// The drag's marks, over the screen: the dragged row washed, and the drop line (4 units, the list's width) where it would land, both
    /// inside the scrolling area the list is in.
    /// </summary>
    void DrawDrop(Painter p, DragState d)
    {
        Scroll? area = null;
        for (var a = d.List.Parent; a is not null && area is null; a = a.Parent) area = a as Scroll;
        if (area is not null) p.PushClip(area.Rect);
        if (d.From >= 0 && d.From < d.List.Children.Count)
            p.Solid(d.List.Children[d.From].Rect, DropLineColour with { A = 0.12f });
        var r = d.List.Rect;
        p.Solid(new UiRect(r.X, d.LineY - 2f, r.Width, 4f), DropLineColour);
        if (area is not null) p.PopClip();
    }

    /// <summary>The first piece with <paramref name="id"/>, or null.</summary>
    public Piece? Find(string id)
    {
        foreach (var p in Root.Walk()) if (p.Id == id) return p;
        return null;
    }

    /// <summary>The pieces the focus can stop at, in order.</summary>
    public IReadOnlyList<Piece> Worked => worked;

    // ---- the pointer ----

    /// <summary>The piece worked at (<paramref name="px"/>, <paramref name="py"/>) in the picture's pixels, seen through every scrolling area above it; or null.</summary>
    public Piece? HitTest(float px, float py)
    {
        float u = Metrics.Pixel;
        return u > 0f ? Hit(Root, px / u, py / u) : null;
    }

    static Piece? Hit(Piece p, float x, float y)
    {
        if (!p.Shown) return null;
        if (p is Scroll && !p.Rect.Contains(x, y)) return null;
        for (int i = p.Children.Count - 1; i >= 0; i--)
            if (Hit(p.Children[i], x, y) is { } found) return found;
        return p.Interactive && p.Rect.Contains(x, y) ? p : null;
    }

    /// <summary>The innermost scrolling area at a point (units), or null.</summary>
    static Scroll? FindIn(Piece p, float x, float y)
    {
        if (!p.Shown) return null;
        if (p is Scroll && !p.Rect.Contains(x, y)) return null;
        for (int i = p.Children.Count - 1; i >= 0; i--)
            if (FindIn(p.Children[i], x, y) is { } s) return s;
        return p is Scroll s2 && s2.Rect.Contains(x, y) ? s2 : null;
    }

    /// <summary>The pointer moved to (<paramref name="px"/>, <paramref name="py"/>) in the picture's pixels: the piece under it is hovered and takes the focus; a pressed slider follows.</summary>
    public void PointerMove(float px, float py)
    {
        float u = Metrics.Pixel;
        if (u <= 0f) return;
        var was = pointer;
        pointer = new Vector2(px / u, py / u);
        if (pointer != was) FocusShown = false;
        if (pointerDown && barHeld is { } bar)
        {
            bar.Area.ThumbTo(pointer.Y - bar.Grip);
            Relayout();
            return;
        }
        Hover = HitTest(px, py);
        if (pointerDown && pressRow is { } row && PressedPiece is DragHandle handle)
        {
            if (Drag is null && Vector2.Distance(pointer, pressAt) * u < DragStartPixels) return;
            Drag = DropAt(row.List, handle, row.Index, pointer.Y);
            return;
        }
        if (pointerDown && PressedPiece is not null) PressedPiece.PointerDrag(this, pointer.X);
        else if (Hover is not null && Hover.FocusStop) Focus = Hover;
    }

    /// <summary>
    /// Where a row of <paramref name="list"/> dragged from <paramref name="from"/> lands with the pointer at <paramref name="y"/> (units): the
    /// gap between the rows nearest it (before the first row whose middle is below it), and the index the row then has.
    /// </summary>
    static DragState DropAt(ListPiece list, DragHandle handle, int from, float y)
    {
        var rows = list.Children;
        int slot = 0;
        while (slot < rows.Count && y > rows[slot].Rect.Y + rows[slot].Rect.Height * 0.5f) slot++;
        float line = rows.Count == 0 ? list.Rect.Y
            : slot == 0 ? rows[0].Rect.Y - list.Gap * 0.5f
            : slot == rows.Count ? rows[^1].Rect.Bottom + list.Gap * 0.5f
            : (rows[slot - 1].Rect.Bottom + rows[slot].Rect.Y) * 0.5f;
        int to = slot > from ? slot - 1 : slot;
        return new DragState(list, handle, from, to, line);
    }

    /// <summary>The row of a reordering list <paramref name="p"/> is in: the list and the row's index; or null.</summary>
    static (ListPiece List, int Index, Piece Row)? RowOf(Piece p)
    {
        for (var a = p; a.Parent is not null; a = a.Parent)
            if (a.Parent is ListPiece { Reorder: not null } list)
                return (list, list.Children.IndexOf(a), a);
        return null;
    }

    /// <summary>A reordering list's row moved from <paramref name="from"/> to <paramref name="to"/>: its action fired, and the focus to follow it.</summary>
    void Reordered(ListPiece list, int from, int to, int within)
    {
        if (from == to || from < 0 || from >= list.Children.Count || to < 0 || to >= list.Children.Count) return;
        var row = list.Children[from];
        Fire(new UiAction(list.Reorder!, $"{from} {to}", row.Scope, row));
        follow = (list, to, within);
    }

    /// <summary>The pointer's button went down (only the first, the left, works pieces).</summary>
    public void PointerDown()
    {
        pointerDown = true;
        FocusShown = false;
        PressedPiece = Hover;
        Drag = null;
        pressRow = null;
        barHeld = null;
        // The bar of a scrolling area: its thumb held where it was taken, or, beside it, the thumb brought under the pointer and held.
        if (FindIn(Root, pointer.X, pointer.Y) is { } area && area.OnBar(pointer.X, pointer.Y))
        {
            var thumb = area.Thumb;
            if (pointer.Y < thumb.Y || pointer.Y > thumb.Bottom)
            {
                area.ThumbTo(pointer.Y - thumb.Height * 0.5f);
                Relayout();
            }
            barHeld = (area, pointer.Y - area.Thumb.Y);
            PressedPiece = null;
            return;
        }
        if (Hover is null) return;
        if (Hover is DragHandle && RowOf(Hover) is { } row)
        {
            pressAt = pointer;
            pressRow = (row.List, row.Index);
            return;
        }
        if (Hover.FocusStop) Focus = Hover;
        Hover.PointerDown(this, pointer.X);
    }

    /// <summary>The pointer's button came up: a press and release on the same piece is a click.</summary>
    public void PointerUp()
    {
        pointerDown = false;
        barHeld = null;
        var pressed = PressedPiece;
        PressedPiece = null;
        pressRow = null;
        if (Drag is { } d)
        {
            // A drop: the row moved to where the line is; the focus goes with it, to its first piece the keyboard stops at.
            Drag = null;
            var row = d.List.Children[d.From];
            int within = row.Walk().Select((piece, i) => (piece, i)).FirstOrDefault(x => x.piece.FocusStop && x.piece.Shown).i;
            Reordered(d.List, d.From, d.To, within);
            return;
        }
        if (pressed is not null && pressed == Hover && pressed.ClicksOnRelease) pressed.Activate(this);
    }

    /// <summary>The wheel turned (<paramref name="notches"/> up positive): the scrolling area under the pointer moves.</summary>
    public void Wheel(float notches)
    {
        var s = FindIn(Root, pointer.X, pointer.Y) ?? FirstScroll();
        if (s is null) return;
        s.Offset = Math.Clamp(s.Offset - notches * s.Step, 0f, s.MaxOffset);
        Relayout();
    }

    Scroll? FirstScroll()
    {
        foreach (var p in Root.Walk()) if (p is Scroll s && p.Shown) return s;
        return null;
    }

    void Relayout()
    {
        if (Metrics.PixelHeight > 0) Layout(Metrics);
        if (!float.IsNegativeInfinity(pointer.X)) Hover = HitTest(pointer.X * Metrics.Pixel, pointer.Y * Metrics.Pixel);
    }

    // ---- the keyboard ----

    /// <summary>
    /// A key: Up and Down move the focus to the nearest piece above or below, Left and Right work a slider or a toggle (else move
    /// across), and all four step through a radio group while one of its radios has the focus; Tab and Shift+Tab go in order, a radio
    /// group one stop (at its selected radio); Space works the focused piece; Enter works the focused piece, or the screen's default
    /// button when the focus is on a checkbox or a radio (or nowhere); Escape works the screen's cancel button, else fires
    /// <see cref="Back"/>; Alt+Up and Alt+Down move the row of a reordering list the focus is in (<see cref="ListPiece.Reorder"/>).
    /// </summary>
    public void Key(UiKey key)
    {
        if (key != UiKey.Escape) FocusShown = true;
        switch (key)
        {
            case UiKey.Escape:
                if (Shown<Button>(b => b.IsCancel) is { } cancel) { cancel.Activate(this); return; }
                if (!string.IsNullOrEmpty(Back)) Fire(new UiAction(Back, "", Data, null));
                return;
            case UiKey.Enter:
                if (Focus is Choice or null && Shown<Button>(b => b.IsDefault) is { } chosen) { chosen.Activate(this); return; }
                Focus?.Activate(this);
                return;
            case UiKey.Space:
                Focus?.Activate(this);
                return;
            case UiKey.Tab: MoveInOrder(+1); return;
            case UiKey.BackTab: MoveInOrder(-1); return;
            case UiKey.Up:
                if (Focus is Radio up && StepRadio(up, -1)) return;
                MoveToward(0, -1);
                return;
            case UiKey.Down:
                if (Focus is Radio down && StepRadio(down, +1)) return;
                MoveToward(0, +1);
                return;
            case UiKey.Left:
                if (Focus?.Nudge(this, -1) != true) MoveToward(-1, 0);
                return;
            case UiKey.Right:
                if (Focus?.Nudge(this, +1) != true) MoveToward(+1, 0);
                return;
            case UiKey.AltUp or UiKey.AltDown:
                // Alt+Up and Alt+Down on a piece of a reordering list's row: the row a place up or down, the focus with it.
                if (Focus is not null && RowOf(Focus) is { } row)
                {
                    int within = row.Row.Walk().TakeWhile(p => p != Focus).Count();
                    Reordered(row.List, row.Index, row.Index + (key == UiKey.AltUp ? -1 : 1), within);
                }
                return;
        }
    }

    /// <summary>The first worked piece of a kind that answers <paramref name="which"/> (a shown, enabled default or cancel button), or null.</summary>
    T? Shown<T>(Func<T, bool> which) where T : Piece
    {
        foreach (var p in worked) if (p is T t && which(t)) return t;
        return null;
    }

    /// <summary>The radios of <paramref name="group"/> that can be worked now, in the file's order.</summary>
    public IReadOnlyList<Radio> GroupOf(string group) => [.. worked.OfType<Radio>().Where(r => r.Group == group)];

    /// <summary>
    /// The arrow keys in a radio group: the focus to the next (+1) or the previous (−1) radio of <paramref name="from"/>'s group, round
    /// its ends, and that one selected. False when the group has no other radio to go to.
    /// </summary>
    public bool StepRadio(Radio from, int direction)
    {
        var group = GroupOf(from.Group);
        int at = -1;
        for (int i = 0; i < group.Count; i++) if (ReferenceEquals(group[i], from)) at = i;
        if (at < 0 || group.Count < 2) return false;
        var next = group[((at + direction) % group.Count + group.Count) % group.Count];
        SetFocus(next);
        next.Select(this);
        return true;
    }

    /// <summary>Whether Tab stops at <paramref name="p"/>: every worked piece but a radio that is not its group's selected one (the first when none is).</summary>
    bool TabStop(Piece p)
    {
        if (p is not Radio r) return true;
        var group = GroupOf(r.Group);
        var stop = group.FirstOrDefault(g => g.Checked) ?? group.FirstOrDefault();
        return ReferenceEquals(stop, r);
    }

    /// <summary>Gives the focus to <paramref name="p"/> (it must be one of <see cref="Worked"/>), scrolling it into sight.</summary>
    public void SetFocus(Piece? p)
    {
        Focus = p;
        if (p is not null) Reveal(p);
    }

    void MoveInOrder(int step)
    {
        if (worked.Count == 0) return;
        int at = Focus is null ? -1 : worked.IndexOf(Focus);
        // From the focus, round the order to the next piece Tab stops at (a radio group once); none but the focus: it stays.
        string? leaving = Focus is Radio r ? r.Group : null;
        for (int k = 1; k <= worked.Count; k++)
        {
            int i = at < 0 ? (step > 0 ? k - 1 : worked.Count - k) : ((at + step * k) % worked.Count + worked.Count) % worked.Count;
            var p = worked[i];
            if (p is Radio g && g.Group == leaving) continue;
            if (!TabStop(p)) continue;
            SetFocus(p);
            return;
        }
    }

    /// <summary>To the nearest worked piece in a direction: the least distance along it, then across it; none there, nothing moves (Up and Down then go in order).</summary>
    void MoveToward(int dx, int dy)
    {
        if (Focus is null) { MoveInOrder(+1); return; }
        var from = Focus.Rect;
        var c = new Vector2(from.X + from.Width * 0.5f, from.Y + from.Height * 0.5f);
        Piece? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var p in worked)
        {
            if (p == Focus) continue;
            var r = p.Rect;
            var pc = new Vector2(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f);
            float along = dx != 0 ? (pc.X - c.X) * dx : (pc.Y - c.Y) * dy;
            if (along <= 1f) continue;
            // Across: how far apart they are the other way, nothing when they overlap that way.
            float across = dx != 0
                ? MathF.Max(0f, MathF.Max(r.Y - from.Bottom, from.Y - r.Bottom))
                : MathF.Max(0f, MathF.Max(r.X - from.Right, from.X - r.Right));
            float centreAcross = dx != 0 ? MathF.Abs(pc.Y - c.Y) : MathF.Abs(pc.X - c.X);
            float score = along + across * 4f + centreAcross * 0.25f;
            if (score < bestScore) { bestScore = score; best = p; }
        }
        if (best is not null) SetFocus(best);
        else if (dy != 0) MoveInOrder(dy);
    }

    /// <summary>Scrolls the area <paramref name="p"/> is in so that it is seen whole — its list's whole row when it is in one.</summary>
    void Reveal(Piece p)
    {
        var shown = p.Rect;
        for (var a = p.Parent; a is not null; a = a.Parent)
        {
            if (a.Parent is ListPiece) shown = a.Rect;
            if (a is Scroll s)
            {
                float before = s.Offset;
                s.Reveal(shown);
                if (s.Offset != before) Relayout();
                return;
            }
        }
    }
}
