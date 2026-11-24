// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>
/// Pieces one after another down (a column) or across (a row), <see cref="Gap"/> units apart inside
/// <see cref="Padding"/>; across the other way each sits by <see cref="Across"/>. A piece whose size along the line is
/// <c>fill</c> shares what room the others leave.
/// </summary>
public class Stack : Piece
{
    public bool Down { get; init; } = true;
    public float Gap { get; set; }
    public Edges Padding { get; set; }
    public Align Across { get; set; } = Align.Stretch;

    static float Along(Vector2 v, bool down) => down ? v.Y : v.X;
    static float Cross(Vector2 v, bool down) => down ? v.X : v.Y;
    static Vector2 Make(float along, float cross, bool down) => down ? new Vector2(cross, along) : new Vector2(along, cross);
    SizeKind AlongKind(Piece p) => Down ? p.Height.Kind : p.Width.Kind;

    protected override Vector2 MeasureContent(Vector2 room)
    {
        var inner = new Vector2(room.X - Padding.Horizontal, room.Y - Padding.Vertical);
        float along = 0, cross = 0;
        int shown = 0, fills = 0;
        bool share = !Down && !float.IsInfinity(inner.X);
        foreach (var c in Children)
        {
            if (!c.Shown) continue;
            shown++;
            // A row's fill ones are measured after the others, in the room they leave (as Arrange gives it): a wrapping text there
            // then takes the height it is drawn at.
            if (share && c.Width.Kind == SizeKind.Fill) { fills++; continue; }
            // Each is offered the whole line across, and along it what the others have not taken (infinite: content).
            var s = c.Measure(Make(float.PositiveInfinity, Cross(inner, Down), Down));
            along += Along(s, Down);
            cross = MathF.Max(cross, Cross(s, Down));
        }
        float gaps = Gap * Math.Max(0, shown - 1);
        if (fills > 0)
        {
            float each = MathF.Max(0f, (inner.X - along - gaps) / fills);
            foreach (var c in Children)
            {
                if (!c.Shown || c.Width.Kind != SizeKind.Fill) continue;
                var s = c.Measure(new Vector2(Math.Clamp(each, c.MinWidth, c.MaxWidth), inner.Y));
                along += s.X;
                cross = MathF.Max(cross, s.Y);
            }
        }
        along += gaps;
        var size = Make(along, cross, Down);
        return new Vector2(size.X + Padding.Horizontal, size.Y + Padding.Vertical);
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        var inner = rect.Inset(Padding);
        float room = Down ? inner.Height : inner.Width;
        float crossRoom = Down ? inner.Width : inner.Height;
        // Those that are not fill take what they want; the fill ones share the rest.
        float taken = 0;
        int fills = 0, shown = 0;
        Span<float> sizes = Children.Count <= 64 ? stackalloc float[Children.Count] : new float[Children.Count];
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Shown) continue;
            shown++;
            if (AlongKind(c) == SizeKind.Fill) { fills++; continue; }
            sizes[i] = Along(c.Measure(Make(float.PositiveInfinity, crossRoom, Down)), Down);
            taken += sizes[i];
        }
        float gaps = Gap * Math.Max(0, shown - 1);
        float share = fills > 0 ? MathF.Max(0f, (room - taken - gaps) / fills) : 0f;
        float at = Down ? inner.Y : inner.X;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Shown) continue;
            float along = AlongKind(c) == SizeKind.Fill ? Math.Clamp(share, Down ? c.MinHeight : c.MinWidth, Down ? c.MaxHeight : c.MaxWidth) : sizes[i];
            var want = c.Measure(Make(along, crossRoom, Down));
            float cross = Across == Align.Stretch && (Down ? c.Width.Kind : c.Height.Kind) != SizeKind.Fixed
                ? Math.Clamp(crossRoom, Down ? c.MinWidth : c.MinHeight, Down ? c.MaxWidth : c.MaxHeight)
                : MathF.Min(Cross(want, Down), crossRoom);
            float crossStart = Down ? inner.X : inner.Y;
            float offset = Across switch
            {
                Align.Centre => (crossRoom - cross) * 0.5f,
                Align.End => crossRoom - cross,
                Align.Stretch => (crossRoom - cross) * 0.5f,
                _ => 0f,
            };
            c.Arrange(Down ? new UiRect(crossStart + offset, at, cross, along) : new UiRect(at, crossStart + offset, along, cross));
            at += along + Gap;
        }
    }
}

/// <summary>Pieces laid over one another in the same rectangle, the last on top.</summary>
public sealed class Layer : Piece
{
    protected override Vector2 MeasureContent(Vector2 room)
    {
        var size = Vector2.Zero;
        foreach (var c in Children) if (c.Shown) size = Vector2.Max(size, c.Measure(room));
        return size;
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        foreach (var c in Children) if (c.Shown) c.Arrange(rect);
    }
}

/// <summary>
/// A piece held to an edge, a corner or the middle of the room it is given (by default the whole screen), kept
/// <see cref="Margin"/> units in from the edges and moved by <see cref="Offset"/>. Its piece is as big as it wants,
/// or all the room when it fills.
/// </summary>
public sealed class Anchor : Piece
{
    public Anchor()
    {
        Width = SizeSpec.Fill;
        Height = SizeSpec.Fill;
    }

    /// <summary>Across and down: start (left, top), centre or end (right, bottom).</summary>
    public Align AcrossAt { get; set; } = Align.Centre;
    public Align DownAt { get; set; } = Align.Centre;
    public float Margin { get; set; }
    public Vector2 Offset { get; set; }

    protected override Vector2 MeasureContent(Vector2 room)
    {
        var size = Vector2.Zero;
        foreach (var c in Children) if (c.Shown) size = Vector2.Max(size, c.Measure(room - new Vector2(2 * Margin)));
        return size + new Vector2(2 * Margin);
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        var room = rect.Inset(Margin);
        foreach (var c in Children)
        {
            if (!c.Shown) continue;
            var s = c.Measure(new Vector2(room.Width, room.Height));
            s = Vector2.Min(s, new Vector2(room.Width, room.Height));
            float x = Place(AcrossAt, room.X, room.Width, s.X) + Offset.X;
            float y = Place(DownAt, room.Y, room.Height, s.Y) + Offset.Y;
            c.Arrange(new UiRect(x, y, s.X, s.Y));
        }
    }

    static float Place(Align a, float start, float room, float size) => a switch
    {
        Align.Start => start,
        Align.End => start + room - size,
        _ => start + (room - size) * 0.5f,
    };
}

/// <summary>Room and nothing in it: a space between pieces (<c>fill</c> to push the pieces after it to the far end).</summary>
public sealed class Space : Piece
{
}

/// <summary>
/// A panel: a dark field, framed (a bronze edge and a fine line inside it, small squares at the corners) or plain,
/// holding one piece inside its <see cref="Padding"/>. Its fill may be named from the data (<c>"{rowFill}"</c>).
/// </summary>
public sealed class Panel : Piece
{
    public string Fill { get; set; } = "panel";
    public string? Edge { get; set; }
    public bool Framed { get; set; }
    public Edges Padding { get; set; }

    protected override Vector2 MeasureContent(Vector2 room)
    {
        var inner = new Vector2(room.X - Padding.Horizontal, room.Y - Padding.Vertical);
        var size = Vector2.Zero;
        foreach (var c in Children) if (c.Shown) size = Vector2.Max(size, c.Measure(inner));
        return size + new Vector2(Padding.Horizontal, Padding.Vertical);
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        var inner = rect.Inset(Padding);
        foreach (var c in Children) if (c.Shown) c.Arrange(inner);
    }

    public override void Draw(Painter p)
    {
        var fill = Screen.Colour(Scope.Fill(Fill));
        p.Solid(Rect, fill);
        if (Framed)
        {
            p.Edge(Rect, 2f, Theme.Colours["panelEdge"]);
            p.Edge(Rect.Inset(8f), 1f, Theme.Colours["panelInner"]);
            const float corner = 10f;
            var gold = Theme.Colours["gold"].WithAlpha(0.85f);
            p.Solid(new UiRect(Rect.X - 3f, Rect.Y - 3f, corner, corner), gold);
            p.Solid(new UiRect(Rect.Right - corner + 3f, Rect.Y - 3f, corner, corner), gold);
            p.Solid(new UiRect(Rect.X - 3f, Rect.Bottom - corner + 3f, corner, corner), gold);
            p.Solid(new UiRect(Rect.Right - corner + 3f, Rect.Bottom - corner + 3f, corner, corner), gold);
        }
        else if (Edge is not null) p.Edge(Rect, 1f, Screen.Colour(Scope.Fill(Edge)));
        base.Draw(p);
    }
}

/// <summary>
/// A scrolling area: its piece laid out as tall as it wants and seen through this rectangle, moved by the wheel, by
/// the focus going to a piece out of sight, or by its bar. A thin bar at the right shows where the view is when it does
/// not all fit; its thumb can be dragged, and a press on the bar beside the thumb takes the thumb there.
/// </summary>
public sealed class Scroll : Piece
{
    public Scroll()
    {
        Width = SizeSpec.Fill;
        Height = SizeSpec.Fill;
    }

    /// <summary>How far down the view has moved, in units.</summary>
    public float Offset { get; set; }
    /// <summary>How tall what it holds is, in units (the last layout's).</summary>
    public float ContentHeight { get; private set; }
    /// <summary>Units the view moves for a turn of the wheel.</summary>
    public float Step { get; set; } = 60f;
    /// <summary>The room kept at the right for the bar.</summary>
    public const float BarRoom = 18f;

    public float MaxOffset => MathF.Max(0f, ContentHeight - Rect.Height);

    protected override Vector2 MeasureContent(Vector2 room)
    {
        var size = Vector2.Zero;
        foreach (var c in Children) if (c.Shown) size = Vector2.Max(size, c.Measure(new Vector2(room.X - BarRoom, float.PositiveInfinity)));
        return size + new Vector2(BarRoom, 0f);
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        float content = 0;
        foreach (var c in Children) if (c.Shown) content = MathF.Max(content, c.Measure(new Vector2(rect.Width - BarRoom, float.PositiveInfinity)).Y);
        ContentHeight = content;
        Offset = Math.Clamp(Offset, 0f, MaxOffset);
        foreach (var c in Children)
            if (c.Shown) c.Arrange(new UiRect(rect.X, rect.Y - Offset, rect.Width - BarRoom, content));
    }

    /// <summary>Moves the view so that <paramref name="r"/> is in it.</summary>
    public void Reveal(UiRect r)
    {
        if (r.Y < Rect.Y) Offset -= Rect.Y - r.Y;
        else if (r.Bottom > Rect.Bottom) Offset += r.Bottom - Rect.Bottom;
        Offset = Math.Clamp(Offset, 0f, MaxOffset);
    }

    /// <summary>The thumb's height on the bar, in units.</summary>
    float ThumbHeight => MathF.Max(40f, Rect.Height * Rect.Height / MathF.Max(ContentHeight, 1f));

    /// <summary>The thumb: where the view is, as tall as the share of the whole it shows.</summary>
    public UiRect Thumb => new(Rect.Right - BarRoom, Rect.Y + (Rect.Height - ThumbHeight) * (MaxOffset > 0f ? Offset / MaxOffset : 0f), BarRoom, ThumbHeight);

    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>), in units, is on the bar (its whole room at the right) of an area that scrolls.</summary>
    public bool OnBar(float x, float y) => MaxOffset > 0f && Rect.Contains(x, y) && x >= Rect.Right - BarRoom;

    /// <summary>The view moved so that the thumb's top is at <paramref name="top"/> (units), within the bar.</summary>
    public void ThumbTo(float top)
    {
        float run = Rect.Height - ThumbHeight;
        Offset = run > 0f ? Math.Clamp((top - Rect.Y) / run, 0f, 1f) * MaxOffset : 0f;
    }

    public override void Draw(Painter p)
    {
        p.PushClip(Rect);
        base.Draw(p);
        p.PopClip();
        if (MaxOffset <= 0f) return;
        var track = new UiRect(Rect.Right - 8f, Rect.Y, 4f, Rect.Height);
        p.Solid(track, Theme.Colours["track"]);
        var thumb = Thumb;
        p.Solid(new UiRect(track.X - 1f, thumb.Y, 6f, thumb.Height), Theme.Colours["gold"].WithAlpha(0.7f));
    }
}

/// <summary>
/// A list bound to data: one copy of its <see cref="Template"/> for each item of
/// the data's list <see cref="Items"/>, down a column, so it grows and shrinks with the data. Rows may be striped.
/// </summary>
public sealed class ListPiece : Stack
{
    /// <summary>The name of the data's list.</summary>
    public string Items { get; set; } = "";
    /// <summary>Makes one row's pieces (the file's template, read again for each row).</summary>
    public Func<Screen, Piece>? Template { get; set; }
    /// <summary>The colours of alternate rows, or none.</summary>
    public string[] Stripes { get; set; } = [];
    /// <summary>
    /// The action that reorders the list's items, or null: its rows can then be dragged by a <c>handle</c> in them and moved by Alt+Up and
    /// Alt+Down on a piece of a row (<see cref="Screen.Drag"/>, <see cref="Screen.Key"/>); the action is fired with the argument
    /// <c>"&lt;from&gt; &lt;to&gt;"</c> (the row's index and the index it is to have) and the moved row's item as its scope.
    /// </summary>
    public string? Reorder { get; set; }

    List<ScreenData>? boundTo;
    int boundCount = -1;

    public override void Bind(ScreenData scope, Screen screen)
    {
        Scope = scope;
        var items = scope.Items(Items);
        if (!ReferenceEquals(items, boundTo) || items.Count != boundCount || Children.Count != items.Count)
        {
            Children.Clear();
            if (Template is not null)
                foreach (var _ in items) Children.Add(Template(screen));
            boundTo = items;
            boundCount = items.Count;
        }
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Parent = this;
            Children[i].Bind(items[i], screen);
        }
    }

    public override void Draw(Painter p)
    {
        if (Stripes.Length > 0)
            for (int i = 0; i < Children.Count; i++)
                if (Children[i].Shown) p.Solid(Children[i].Rect, Screen.Colour(Stripes[i % Stripes.Length]));
        base.Draw(p);
    }
}
