// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>How a piece's width or height is chosen: from what it holds, a number of units, or all the room it is given.</summary>
public enum SizeKind : byte { Auto = 0, Fixed = 1, Fill = 2 }

public readonly record struct SizeSpec(SizeKind Kind, float Units)
{
    public static readonly SizeSpec Auto = new(SizeKind.Auto, 0f);
    public static SizeSpec Fixed(float units) => new(SizeKind.Fixed, units);
    public static readonly SizeSpec Fill = new(SizeKind.Fill, 0f);
}

/// <summary>Where pieces sit across a column or a row, or where an anchor puts its piece.</summary>
public enum Align : byte { Start = 0, Centre = 1, End = 2, Stretch = 3 }

/// <summary>
/// One piece of a screen: a container that lays pieces out, or a piece that shows
/// something or is worked. A piece is measured (what size it wants in the room it is offered), then arranged (given
/// its rectangle), then drawn; the pointer and the keyboard reach it through the same rectangles. Every size is in
/// units of a 1440-high screen. New kinds of piece are added by registration (<see cref="PieceTypes"/>).
/// </summary>
public abstract class Piece
{
    /// <summary>The kind's name in the files (<c>column</c>, <c>button</c>, …).</summary>
    public string Type { get; internal set; } = "";
    /// <summary>The piece's own name in its file, for the host and the tests to find it by.</summary>
    public string? Id { get; set; }

    public SizeSpec Width { get; set; } = SizeSpec.Auto;
    public SizeSpec Height { get; set; } = SizeSpec.Auto;
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }
    public float MaxWidth { get; set; } = float.PositiveInfinity;
    public float MaxHeight { get; set; } = float.PositiveInfinity;

    /// <summary>A name in the data that must be true for the piece to be shown and laid out (none: always).</summary>
    public string? VisibleWhen { get; set; }

    /// <summary>The data the piece's names are looked up in (a list's item, or the screen's).</summary>
    public ScreenData Scope { get; internal set; } = new();

    public Piece? Parent { get; internal set; }

    /// <summary>Where the last layout put the piece, in units.</summary>
    public UiRect Rect { get; internal set; }

    /// <summary>The pieces this one holds, in order (drawn first to last, so the last is on top).</summary>
    public List<Piece> Children { get; } = [];

    /// <summary>Whether the keyboard's focus stops here and the pointer works it.</summary>
    public virtual bool Interactive => false;

    /// <summary>Whether the keyboard's focus stops here (every piece the pointer works, but a list row's drag handle, which only the pointer works).</summary>
    public virtual bool FocusStop => Interactive;

    public bool Shown => VisibleWhen is null || Scope.Flag(VisibleWhen);

    /// <summary>Sets the data the piece and those it holds look names up in (a list makes its items' pieces here).</summary>
    public virtual void Bind(ScreenData scope, Screen screen)
    {
        Scope = scope;
        foreach (var c in Children)
        {
            c.Parent = this;
            c.Bind(scope, screen);
        }
    }

    // ---- layout ----

    /// <summary>The size the piece wants when offered <paramref name="room"/> (either may be infinite): its own sizes, else what it holds, kept within its least and most.</summary>
    public Vector2 Measure(Vector2 room)
    {
        if (!Shown) return Vector2.Zero;
        bool needContent = Width.Kind == SizeKind.Auto || Height.Kind == SizeKind.Auto
                           || (Width.Kind == SizeKind.Fill && float.IsInfinity(room.X)) || (Height.Kind == SizeKind.Fill && float.IsInfinity(room.Y));
        float offerW = Width.Kind == SizeKind.Fixed ? Width.Units : MathF.Min(room.X, MaxWidth);
        float offerH = Height.Kind == SizeKind.Fixed ? Height.Units : MathF.Min(room.Y, MaxHeight);
        var content = needContent ? MeasureContent(new Vector2(offerW, offerH)) : Vector2.Zero;
        float w = Width.Kind switch
        {
            SizeKind.Fixed => Width.Units,
            SizeKind.Fill when !float.IsInfinity(room.X) => room.X,
            _ => content.X,
        };
        float h = Height.Kind switch
        {
            SizeKind.Fixed => Height.Units,
            SizeKind.Fill when !float.IsInfinity(room.Y) => room.Y,
            _ => content.Y,
        };
        return new Vector2(Math.Clamp(w, MinWidth, MathF.Max(MinWidth, MaxWidth)), Math.Clamp(h, MinHeight, MathF.Max(MinHeight, MaxHeight)));
    }

    /// <summary>What the piece's contents want, offered <paramref name="room"/>.</summary>
    protected virtual Vector2 MeasureContent(Vector2 room) => Vector2.Zero;

    /// <summary>Gives the piece its rectangle and lays out what it holds inside it.</summary>
    public virtual void Arrange(UiRect rect) => Rect = rect;

    // ---- drawing ----

    public virtual void Draw(Painter p) => DrawChildren(p);

    /// <summary>The pieces this one holds, each drawn in turn (through the screen's look when it has one).</summary>
    public void DrawChildren(Painter p)
    {
        foreach (var c in Children)
            if (c.Shown) c.Paint(p);
    }

    /// <summary>The piece drawn: by the screen's look (<see cref="Screen.Skin"/>) when it dresses this piece, else by its own <see cref="Draw"/>.</summary>
    public void Paint(Painter p)
    {
        if (p.Skin is { } skin && skin.Draw(p, this)) return;
        Draw(p);
    }

    // ---- being worked ----

    /// <summary>A click on it, or Enter while it has the focus.</summary>
    public virtual void Activate(Screen screen) { }

    /// <summary>Left (−1) or Right (+1) while it has the focus; false when it does not take them (the focus moves instead).</summary>
    public virtual bool Nudge(Screen screen, int direction) => false;

    /// <summary>The pointer pressed on it (<paramref name="x"/> in units), and then moved while pressed.</summary>
    public virtual void PointerDown(Screen screen, float x) { }
    public virtual void PointerDrag(Screen screen, float x) { }

    /// <summary>Whether a press and a release on it count as a click (a slider is worked by the press itself).</summary>
    public virtual bool ClicksOnRelease => true;

    /// <summary>Every piece in the tree under this one, this one first, in order.</summary>
    public IEnumerable<Piece> Walk()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.Walk()) yield return d;
    }

    public override string ToString() => $"{Type}{(Id is null ? "" : "#" + Id)} {Rect}";
}

/// <summary>
/// **A look** a screen's pieces may be drawn in instead of the theme's plain rectangles (the launcher's kit of pictures): asked
/// first for every piece the screen draws (<see cref="Piece.Paint"/>); it draws the piece its own way, with what the piece holds
/// (<see cref="Piece.DrawChildren"/>), and answers true, or answers false and the piece draws itself as always. A screen with no
/// look (every screen of the game) draws exactly as it did.
/// </summary>
public interface ISkin
{
    bool Draw(Painter p, Piece piece);
}

/// <summary>
/// Turns a screen's pieces into its <see cref="DrawList"/>: units to the picture's pixels, words into runs of each face,
/// the clip of every scrolling area. Knows which piece has the focus and which is pressed, for their looks.
/// </summary>
public sealed class Painter
{
    readonly List<FaceRun> runs = [];
    readonly Stack<UiRect> clips = new();

    public Painter(DrawList list, UiMetrics metrics, Screen screen)
    {
        List = list;
        Metrics = metrics;
        Screen = screen;
    }

    public DrawList List { get; }
    public UiMetrics Metrics { get; }
    public Screen Screen { get; }

    public bool Focused(Piece p) => Screen.Focus == p;
    public bool Pressed(Piece p) => Screen.PressedPiece == p && Screen.Hover == p;
    public bool Hovered(Piece p) => Screen.Hover == p;

    /// <summary>The look the screen's pieces are drawn in (<see cref="Screen.Skin"/>), or null for the theme's.</summary>
    public ISkin? Skin => Screen.Skin;

    /// <summary>
    /// A colour every line of words drawn takes while it is set, in place of its style's (a disabled checkbox's label, which holds
    /// lines of its own, dimmed together); a colour a caller passes itself still wins. Null, as it always is unless set: the styles'.
    /// </summary>
    public Rgba? WordsColour { get; set; }

    public void Solid(UiRect r, Rgba colour) => List.Solid(Metrics.ToPixels(r), colour);

    /// <summary>A frame's edge, <paramref name="width"/> units wide, inside <paramref name="r"/>; each side at least a pixel.</summary>
    public void Edge(UiRect r, float width, Rgba colour)
    {
        var px = Metrics.ToPixels(r);
        int t = Math.Max(1, (int)MathF.Round(width * Metrics.Pixel));
        if (px.Width <= 2 * t || px.Height <= 2 * t) { List.Solid(px, colour); return; }
        List.Solid(new PixelRect(px.X, px.Y, px.Width, t), colour);
        List.Solid(new PixelRect(px.X, px.Bottom - t, px.Width, t), colour);
        List.Solid(new PixelRect(px.X, px.Y + t, t, px.Height - 2 * t), colour);
        List.Solid(new PixelRect(px.Right - t, px.Y + t, t, px.Height - 2 * t), colour);
    }

    /// <summary>
    /// A line of words set in <paramref name="box"/>: across by <paramref name="align"/>, its baseline where the face's
    /// letters sit in the box's middle (or at <paramref name="baseline"/> units from the top when given).
    /// </summary>
    public void Text(string text, TextStyle style, UiRect box, TextAlign align, Rgba? colour = null, TextLook? look = null, float? baseline = null)
    {
        if (text.Length == 0) return;
        float em = style.Em;
        float width = Letters.Measure(style.Lettering, text, em);
        float x = align switch
        {
            TextAlign.Centre => box.X + (box.Width - width) * 0.5f,
            TextAlign.End => box.Right - width,
            _ => box.X,
        };
        float y = baseline is { } b ? box.Y + b : Letters.Baseline(style.Lettering, em, box.Y + box.Height * 0.5f);
        float px = Metrics.Pixel;
        var pen = new Vector2(x * px, MathF.Round(y * px));
        runs.Clear();
        Letters.Runs(style.Lettering, text, runs);
        foreach (var r in runs)
        {
            float size = em * r.EmScale * px;
            List.Text(r.Text, r.Face, size, pen, colour ?? WordsColour ?? style.Colour, look ?? style.Look, align);
            pen.X += Face.Get(r.Face).Measure(r.Text) * size;
        }
    }

    /// <summary>
    /// A small arrow head pointing left (−1) or right (+1), its point at (<paramref name="x"/>, <paramref name="y"/>)
    /// and <paramref name="size"/> units long, built of upright strips a pixel wide (rectangles are all the draw list has).
    /// </summary>
    public void Arrow(float x, float y, float size, int direction, Rgba colour)
    {
        float px = Metrics.Pixel;
        int length = Math.Max(2, (int)MathF.Round(size * px));
        int tipX = (int)MathF.Round(x * px), midY = (int)MathF.Round(y * px);
        for (int i = 0; i < length; i++)
        {
            int half = (int)MathF.Round(i * 0.9f) + 1;
            int col = direction < 0 ? tipX + i : tipX - i - 1;
            List.Solid(new PixelRect(col, midY - half, 1, 2 * half), colour);
        }
    }

    /// <summary>
    /// An arrow head pointing up (−1) or down (+1) in the middle of <paramref name="p"/>'s rectangle, about a third of its height, built of
    /// level strips a pixel high (an icon button's Move up and Move down).
    /// </summary>
    public void ArrowUpDown(Piece p, int direction, Rgba colour)
    {
        float px = Metrics.Pixel;
        var r = p.Rect;
        int height = Math.Max(3, (int)MathF.Round(MathF.Min(r.Width, r.Height) * 0.3f * px));
        int midX = (int)MathF.Round((r.X + r.Width * 0.5f) * px), top = (int)MathF.Round((r.Y + r.Height * 0.5f) * px) - height / 2;
        for (int i = 0; i < height; i++)
        {
            int half = (int)MathF.Round(i * 0.95f) + 1;
            int row = direction < 0 ? top + i : top + height - 1 - i;
            List.Solid(new PixelRect(midX - half, row, 2 * half, 1), colour);
        }
    }

    public void PushClip(UiRect r)
    {
        var clip = clips.Count > 0 ? r.Intersect(clips.Peek()) : r;
        clips.Push(clip);
        List.PushClip(Metrics.ToPixels(clip));
    }

    public void PopClip()
    {
        clips.Pop();
        List.PopClip();
    }
}
