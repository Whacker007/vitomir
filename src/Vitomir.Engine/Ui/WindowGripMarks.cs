// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The grip's marks in the interface's space (ours). The grip's rules — where it is, when it shows — are the
// platform's (`Vitomir.Platform/Windowing/WindowGrip.cs`), with the window code that asks SDL; how it is drawn
// in 1.1's interface (`UiLayout`'s space) stays the engine's, beside `Passes/CursorPass.cs`, which draws it.
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Ui;

/// <summary>The grip of a window with no title bar (<see cref="WindowGrip"/>) as boxes of 1.1's interface (split from <c>WindowGrip</c>).</summary>
public static class WindowGripMarks
{
    /// <summary>
    /// The grip's boxes in the interface's 640 x 480 space (origin bottom left), for a window of
    /// <paramref name="width"/> by <paramref name="height"/>: its backing, then the four-way cross
    /// that says "move".
    /// </summary>
    public static IEnumerable<(float X, float Y, float W, float H, bool Mark)> Boxes(int width, int height)
    {
        var r = WindowGrip.Rect(width);
        // The window's own pixels, whatever the sheet's layout (UiLayout): the grip belongs to the window.
        var map = UiLayout.On(width, height);
        (float, float, float, float, bool) Box(float x, float y, float w, float h, bool mark) =>
            ((x - map.X) / map.ScaleX, (map.Bottom - (y + h)) / map.ScaleY, w / map.ScaleX, h / map.ScaleY, mark);
        yield return Box(r.X, r.Y, r.W, r.H, false);
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        yield return Box(cx - 9, cy - 1, 18, 2, true);     // across
        yield return Box(cx - 1, cy - 8, 2, 16, true);     // up and down
        yield return Box(cx - 12, cy - 3, 3, 6, true);     // the four heads
        yield return Box(cx + 9, cy - 3, 3, 6, true);
        yield return Box(cx - 3, cy - 11, 6, 3, true);
        yield return Box(cx - 3, cy + 8, 6, 3, true);
    }
}
