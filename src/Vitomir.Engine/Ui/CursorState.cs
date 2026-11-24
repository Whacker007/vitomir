// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the cursor is this frame. The cursor's choice runs once per UI update, after the world
// pick has decided what is under the pointer and before anything is drawn, and it answers in a
// fixed order of precedence:
//
//   1. a **refusal** is still running — a quarter-second hold was set, and nothing overrides it;
//   2. something is **in hand** — the held item's own icon is the cursor, and the thing under the
//      pointer is ignored entirely (while an icon is up, it returns);
//   3. otherwise the thing under the pointer picks one of the eight — or, for a character on the
//      player's side, none, and the cursor stays what it was.
//
// **The hand belongs to the actor, not to the UI.** The hold writes the item into the actor
// and the actor into the item; the cursor reads it back through the actor's hand then
// the item's icon element. That is why a held item survives a level change: the pane
// is rebuilt but the actor is not, and the next frame's update finds the item still in his hand.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The cursor's state machine. Knows nothing about drawing: it answers which of the eight, or
/// which item's icon, and a renderer does the rest.
/// </summary>
public sealed class CursorState
{
    /// <summary>Which of the eight, when <see cref="Showing"/> is null.</summary>
    public CursorId Current { get; private set; } = CursorId.Default;

    /// <summary>The item on the cursor, drawn in place of a glyph. Null when there is none.</summary>
    public Item? Showing { get; private set; }

    /// <summary>When the refusal stops holding, in the same milliseconds the caller passes in.</summary>
    public long RefusedUntil { get; private set; }

    /// <summary>Set by whoever plays sound <see cref="Cursors.RefuseSound"/>; a test can watch it.</summary>
    public int Refusals { get; private set; }

    /// <summary>
    /// Show <see cref="CursorId.Negative"/> and hold it for
    /// <see cref="Cursors.RefuseMs"/>. It wins over everything, including a held item, until it
    /// runs out.
    /// </summary>
    public void Refuse(long nowMs)
    {
        Current = CursorId.Negative;
        Showing = null;
        RefusedUntil = nowMs + Cursors.RefuseMs;
        Refusals++;
        Audio?.Ui(Cursors.RefuseSound);                        // `NEGATIVE`, as an interface sound
    }

    /// <summary>Where the refusal's noise goes. Null is silence.</summary>
    public Audio.GameAudio? Audio { get; set; }

    /// <summary>
    /// One frame. <paramref name="held"/> is the actor's hand; <paramref name="context"/> is what
    /// <see cref="Highlight.Cursor"/> made of the thing under the pointer — null when the cursor choice's
    /// switch sets nothing (a friendly character), which **keeps the cursor it had**. A refusal that
    /// has run out puts the plain pointer back first, so a friend hovered then
    /// shows the plain one.
    /// </summary>
    public void Update(long nowMs, Item? held, CursorId? context)
    {
        if (nowMs < RefusedUntil)
        {
            Current = CursorId.Negative;
            Showing = null;
            return;
        }
        if (Current == CursorId.Negative) Current = CursorId.Default;

        if (held is not null && !held.Destroyed)
        {
            Showing = held;
            return;
        }

        Showing = null;
        if (context is { } id) Current = id;
    }

    /// <summary>
    /// Set one of the eight outright, which is what start-up does with
    /// <see cref="CursorId.Default"/>. It does not take a hold, so the next update replaces it.
    /// Every caller here that sets the OS cursor goes through the engine's own set-remembering-the-
    /// last-one and, to hand it back, its pop (which also drops the held
    /// icon) — a UI element checks whether it was the one showing on the cursor before
    /// restoring it, e.g. a drag source giving up. Nothing here keeps a "previous" cursor
    /// to restore: <see cref="Update"/> recomputes <see cref="Showing"/> every frame and
    /// <see cref="Current"/> whenever the switch has a case, so letting go of a held item is simply
    /// not showing it next frame.
    /// </summary>
    public void Set(CursorId id)
    {
        Current = id;
        Showing = null;
    }

    /// <summary>
    /// A discipline being dragged to a quick slot (the drag puts the discipline's icon element on
    /// the cursor): the icon's bitmap, 27 × 32, drawn in place of everything else until
    /// the drop lets it go. Null when nothing is being dragged.
    /// </summary>
    public string? Dragging { get; set; }

    /// <summary>The anchor for whatever is being shown — a carried icon is always centred.</summary>
    public CursorAnchor Anchor => Showing is null && Dragging is null ? Cursors.Anchor(Current) : CursorAnchor.Centre;

    /// <summary>
    /// The size the glyph is drawn at: 32 x 32 for one of the eight, and for a carried item the
    /// **icon element's own** size, which is its cell footprint — the icon builds that cursor from
    /// the element's width and height.
    /// </summary>
    public (float W, float H) Extent => Dragging is not null ? (EffectIcons.Width, EffectIcons.Height)
        : Showing is { } item
        ? (item.IconCells.Across * InventoryPane.CellWidth, item.IconCells.Down * InventoryPane.CellHeight)
        : (Cursors.Size, Cursors.Size);
}
