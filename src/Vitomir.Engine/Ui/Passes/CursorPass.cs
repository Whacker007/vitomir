// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The cursor, drawn. `Vitomir.Engine.Ui.CursorState` decides *what* it is; this puts it on screen.
//
// **It goes last.** The UI's whole draw makes the cursor's draw its final call —
// after the world, after every pane. So this pass is appended after all the others and nothing can
// cover it.
//
// The quad is drawn **white**: the draw passes 255 for all four vertex colours with no path that
// changes them, so neither a glyph nor a carried item is ever tinted. The glow the base game puts
// on a hovered object is on the object's own model (the hover writes the colour
// into its model), not on the cursor.
using Vitomir.Engine.World;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the current cursor — one of the eight glyphs, or the item in hand.</summary>
public sealed class CursorPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>The state machine. Null draws nothing at all.</summary>
    public CursorState? State { get; set; }

    /// <summary>
    /// Where the pointer is, in the `.nui` 640 x 480 space with y counting up. It starts in the
    /// middle of the screen rather than at the origin, because the cursor's set-up pushes a
    /// limit rectangle at start-up and the cursor is clamped into it — so the engine's cursor
    /// is never at a corner it was not moved to.
    /// </summary>
    public float X { get; set; } = UiAssets.Width / 2f;
    public float Y { get; set; } = UiAssets.Height / 2f;

    /// <summary>Off while the game has no pointer of its own to draw — a cutscene, or the free camera.</summary>
    public bool Enabled { get; set; } = true;

    public CursorPass(UiCanvas ui, CursorState? state = null)
    {
        this.ui = ui;
        State = state;
    }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (width <= 0 || height <= 0) return;
        // The grip of a window with no title bar (ours, Engine.Ui.WindowGrip), under the cursor;
        // with the pointer on it the system's own cursor shows, so the game's is not drawn.
        if (WindowGrip.Shown)
        {
            ui.Begin(width, height);
            foreach (var b in WindowGripMarks.Boxes(width, height))
                ui.Box(b.X, b.Y, b.W, b.H, b.Mark ? new Tint(0.92f, 0.92f, 0.92f, WindowGrip.Over ? 1f : 0.8f)
                                                  : new Tint(0f, 0f, 0f, WindowGrip.Over ? 0.8f : 0.5f));
            if (WindowGrip.Over) return;
        }
        if (!Enabled || State is not { } state) return;
        ui.Begin(width, height);

        var (w, h) = state.Extent;
        var (x, y) = Cursors.Corner(state.Anchor, X, Y, w, h);

        if (state.Dragging is { } icon)
            ui.Icon(icon, x, y, w, h);
        else if (state.Showing is { } item)
        {
            // A carried item is its inventory icon at 1:1, the same blit the grid uses —
            // hands the icon element's own bitmap to the cursor factory unchanged.
            if (!ui.Icon(item.Icon, x, y, w, h)) ui.Box(x, y, w, h, new Tint(0.7f, 0.7f, 0.8f, 0.5f));
        }
        else
        {
            ui.Draw(Cursors.Name(state.Current), x, y, w, h);
        }
    }
}
