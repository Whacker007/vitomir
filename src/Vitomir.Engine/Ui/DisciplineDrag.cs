// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A discipline taken to a quick slot, 1.1's way. It is a **drag**, and only a drag: the icon goes on
// the cursor when a held left button starts to move and comes off it when that button is let go.
//
// The pieces, in the order they happen:
//
//   1. **The press.** 1.1's input poll adds up the mouse's own motion while the
//      left button is held (zeroed while it is up).
//   2. **The drag begins** once either sum is past 2: the drag flag goes on and stays on,
//      and the drag-start flag is on for that one poll only. The frame's input turns that into the interface's
//      event 6, which the interface hands to the hovered control — only
//      while the pointer is on a pane. The frame runs the poll and the input before
//      the interface's own frame, so the
//      hovered control is the one the pointer was on **before** the move that made the drag: a
//      quick flick off a slot still lifts that slot.
//   3. **The lift.** A discipline slot's lift handler (the `disciplineslot` class's):
//      when the advancement screen is not up, the slot is not the large current one
//      (kind 2, `sbCurrentDiscipline`), it holds a discipline and nothing is on the
//      cursor, the discipline's icon element goes on the cursor. The
//      pane's five rows (kind 0) and the bar's six quick slots (kind 1) all lift.
//   4. **The drop.** Letting go of a drag sets the drop flag, and the input raises event 7
//      at once, pane or no pane: the carried icon fires its callback 7, which
//      the discipline code bound to the drop handler — a quick slot under the pointer takes it, and
//      anywhere else it goes back; the cursor is itself again either way.
//   5. **Then the release** (event 1), after the drop in the same poll: the control that was pressed
//      hears it only when the pointer is back on it, so a drag that ends where it began selects.
//
// So there is **no carry-and-click**: nothing in 1.1 puts a discipline on the cursor except the drag's
// start, and every drag ends in a drop. Nothing else takes it off either — no right button, no Escape
// — except the discipline being let go under it. A comparison with 1.1 note that 1.1 "carries the
// icon and drops it with the next click" read the drop handler's first assert (the cursor's icon is the element
// whose callback runs) as a click; the callback is the drop's.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The left button's drag, as far as a discipline is concerned: the threshold that makes a press a
/// drag, the lift on the drag's start and the drop on its end (event 7 →
/// the drop handler). What a drop does to the bar is <see cref="StatusBar.Assign"/>.
/// </summary>
public sealed class DisciplineDrag
{
    /// <summary>
    /// How far the mouse moves, either way, before a held press is a drag (the sums of its
    /// motion are compared with 3). 1.1 counts the mouse's own DirectInput counts; Vitomir counts the
    /// pointer's window pixels, the nearest thing it has — a count is a pixel at Windows' default
    /// pointer speed (ours).
    /// </summary>
    public const float Threshold = 3f;

    bool held;
    float fromX, fromY, lastX, lastY;

    /// <summary>Whether the held button has become a drag: once, until the release.</summary>
    public bool Moving { get; private set; }

    /// <summary>The discipline whose icon is on the cursor, or null.</summary>
    public Discipline? Carried { get; private set; }

    /// <summary>The left button went down at window pixel (<paramref name="x"/>, <paramref name="y"/>): the motion sums start from nothing.</summary>
    public void Press(float x, float y)
    {
        held = true;
        Moving = false;
        (fromX, fromY) = (lastX, lastY) = (x, y);
    }

    /// <summary>
    /// The pointer moved to window pixel (<paramref name="x"/>, <paramref name="y"/>). On the one move
    /// that makes the press a drag (event 6), the slot the pointer was on before this move is asked
    /// for its discipline — <paramref name="under"/>, given a window pixel, answers for a pane row or
    /// a quick slot and null for anything else, `sbCurrentDiscipline` included — and it is lifted onto
    /// the cursor when the slot's lift handler allows. Answers the discipline lifted, or null. 1.1's "before" is the
    /// last frame's pointer; Vitomir's is the last motion's, which is the same or a little later (ours).
    /// </summary>
    /// <param name="advancing">The advancement screen is up: nothing lifts.</param>
    /// <param name="cursorBusy">Something is on the cursor already — a held item's icon.</param>
    public Discipline? Move(float x, float y, Func<float, float, Discipline?> under, bool advancing, bool cursorBusy)
    {
        var (wasX, wasY) = (lastX, lastY);
        (lastX, lastY) = (x, y);
        if (!held || Moving) return null;
        if (MathF.Abs(x - fromX) < Threshold && MathF.Abs(y - fromY) < Threshold) return null;
        Moving = true;
        if (advancing || cursorBusy || Carried is not null || under(wasX, wasY) is not { } d) return null;
        return Carried = d;
    }

    /// <summary>
    /// The left button let go: the drop (event 7, only after a drag) hands back what was carried, for
    /// the quick slot under the pointer to take, and the cursor is empty again. A press
    /// that never moved three pixels drops nothing — a click is a click.
    /// </summary>
    public Discipline? Release()
    {
        var dropped = Moving ? Carried : null;
        held = false;
        Moving = false;
        Carried = null;
        return dropped;
    }

    /// <summary>
    /// A discipline let go while its icon is on the cursor (heard through
    /// <see cref="DisciplineBook.LetGo"/>): the cursor is itself again, so the drag's drop
    /// (<see cref="Release"/>) has nothing to drop. 1.1 compares the cursor's icon with the icon of
    /// the discipline's **definition**, which every instance of it shares:
    /// a temporary Awe let go takes the player's own Awe off the cursor too. Nothing else about the
    /// drag changes — the button is still down and the motion is still a drag. Answers whether it was
    /// on the cursor, for the caller to put the cursor back.
    /// </summary>
    public bool LetGo(Discipline d)
    {
        if (Carried is not { } c || !ReferenceEquals(c, d) && !ReferenceEquals(c.Definition, d.Definition)) return false;
        Carried = null;
        return true;
    }
}
