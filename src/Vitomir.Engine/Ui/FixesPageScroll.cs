// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Fixes page's scroll (ours): the list outgrew its box at twenty-two rows, so it scrolls, a
// row at a time, behind the load screen's own scroll bar — the page's frame is the load screen's, whose
// art has the bar's channel beside the list box. The bar is the ui library's (`ScrollBar`).
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

public sealed partial class FixesPage
{
    /// <summary>How many rows the list is scrolled by: the first row on show (ours).</summary>
    public int Scroll { get; private set; }

    /// <summary>The furthest the list scrolls: the first row from which the last is on show (none when they all fit).</summary>
    public int ScrollEnd
    {
        get
        {
            int s = 0;
            while (s < PageRows.Count - 1 && !Fits(PageRows.Count - 1, s)) s++;
            return s;
        }
    }

    ScrollBar? bar;

    /// <summary>The page's scroll bar and slider (the library's), made from the pane the first time.</summary>
    public ScrollBar? Bar => bar ??= ScrollBar.Of(Pane.Nui, BarName);

    /// <summary>Whether a row is in the box at the current scroll.</summary>
    public bool OnShow(int index) => Fits(index, Scroll);

    /// <summary>
    /// The list scrolled to <paramref name="rows"/> (held to 0 .. <see cref="ScrollEnd"/>): the rows in
    /// the box raised into it by the scroll's height (<see cref="MenuPane.Lift"/>) and shown, the rest
    /// taken off the pane, so they are neither drawn nor under the pointer — and the slider put where the
    /// bar's value now says: at the top of its track for the top of the list, at the
    /// foot for the end.
    /// </summary>
    public void ScrollTo(int rows)
    {
        Scroll = Math.Clamp(rows, 0, ScrollEnd);
        if (Bar is { } b)
        {
            b.SetRange(-ScrollEnd, 0);                                      // the value is minus the scroll, as a text box's
            b.SetValue(-Scroll);
        }
        Lay();
    }

    /// <summary>The rows and the slider where <see cref="Scroll"/> and the bar say.</summary>
    void Lay()
    {
        var pane = Pane;
        int i = 0;
        foreach (var row in PageRows)
        {
            bool on = OnShow(i++);
            foreach (var id in row.Kind == RowKind.Heading ? [row.Id] : new[] { row.Id, row.Id + "label" })
            {
                pane.Lift[id] = LiftAt(Scroll);
                if (on) pane.Hidden.Remove(id); else pane.Hidden.Add(id);
            }
        }
        if (Bar is { } b && pane.Element(SliderName) is { } slider)
            pane.Lift[SliderName] = b.Slider.Y - slider.Pos[1];
    }

    /// <summary>Scrolls just far enough that a row (by its control's name) is in the box — for a caller that wants it seen.</summary>
    public void Reveal(string id)
    {
        int index = PageRows.Select((r, n) => (r, n)).FirstOrDefault(p => p.r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).n;
        if (index < Scroll) ScrollTo(index);
        else if (!Fits(index, Scroll))
        {
            int s = Scroll;
            while (s < index && !Fits(index, s)) s++;
            ScrollTo(s);
        }
    }

    /// <summary>
    /// The left button down on the page's scroll bar (from the front end's press, with the pointer at
    /// pane-local <paramref name="y"/>): on the track, the list pages a box's worth towards the end the
    /// press is on (as the load screen's list pages); on the slider, it is caught —
    /// when the list has somewhere to scroll. Answers whether the press was the bar's.
    /// </summary>
    public bool Press(NuiElement control, float y)
    {
        if (Bar is not { } b) return false;
        if (control.Name.Equals(SliderName, StringComparison.OrdinalIgnoreCase))
        {
            if (ScrollEnd > 0) b.Take(y);
            return true;
        }
        if (!control.Name.Equals(BarName, StringComparison.OrdinalIgnoreCase)) return false;
        // Above the slider pages towards the top of the list, below it towards the end.
        ScrollTo(Scroll + (b.PageAt(y) < 0 ? -ShownAt(Scroll) : ShownAt(Scroll)));
        return true;
    }

    /// <summary>
    /// Each frame, with the pointer at pane-local <paramref name="y"/>: a held slider follows it along
    /// its track, and the list scrolls to the row its place says — the slider
    /// staying where the pointer put it, not snapping to the row, until it is let go.
    /// </summary>
    public void Drag(float y)
    {
        if (Bar is not { Held: true } b || !b.DragTo(y)) return;
        Scroll = Math.Clamp(-b.Value, 0, ScrollEnd);
        Lay();
    }

    /// <summary>The button up: a held slider is let go where it is.</summary>
    public void Release() => Bar?.Release();

    /// <summary>The wheel anywhere on the page: a box's worth a notch-burst, up for a positive turn (as a list).</summary>
    public void Wheel(int turn)
    {
        if (turn != 0) ScrollTo(Scroll + (turn > 0 ? -ShownAt(Scroll) : ShownAt(Scroll)));
    }
}
