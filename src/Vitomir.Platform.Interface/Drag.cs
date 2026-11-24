// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>
/// **A list row's drag handle** (<c>"type": "handle"</c>): the grip at a row's left by which the pointer drags the row to another place
/// in its list, when the list reorders (<see cref="ListPiece.Reorder"/>). The pointer works it (it is hovered, pressed and dragged); the
/// keyboard passes it over (<see cref="FocusStop"/>), as Alt+Up and Alt+Down move the focused row instead. A press and release on it
/// without a drag does nothing. Drawn as three short bars, lit while the pointer is over it.
/// </summary>
public sealed class DragHandle : Piece
{
    /// <summary>The bars' colour (a theme colour's name or <c>#rrggbbaa</c>).</summary>
    public string Colour { get; set; } = "muted";

    public override bool Interactive => true;
    public override bool FocusStop => false;
    public override bool ClicksOnRelease => false;

    protected override Vector2 MeasureContent(Vector2 room) => new(28f, 32f);

    public override void Draw(Painter p) => DrawBars(p, this, Screen.Colour(p.Hovered(this) || p.Screen.Drag?.Handle == this ? "gold" : Colour));

    /// <summary>The grip's three bars, centred in the handle's rectangle, in <paramref name="colour"/> (a look's own colours use it too).</summary>
    public static void DrawBars(Painter p, Piece handle, Rgba colour)
    {
        var r = handle.Rect;
        float w = MathF.Min(18f, r.Width), h = 3f, gap = 4f;
        float x = r.X + (r.Width - w) * 0.5f, y = r.Y + (r.Height - (3 * h + 2 * gap)) * 0.5f;
        for (int i = 0; i < 3; i++) p.Solid(new UiRect(x, y + i * (h + gap), w, h), colour);
    }
}

/// <summary>
/// **A row being dragged** (<see cref="Screen.Drag"/>): the list, the handle it was taken by, the row's index (<see cref="From"/>), the
/// index it would have if dropped now (<see cref="To"/>), and where the drop line is drawn (<see cref="LineY"/>, units: between the rows
/// it would land between).
/// </summary>
public sealed record DragState(ListPiece List, DragHandle Handle, int From, int To, float LineY);
