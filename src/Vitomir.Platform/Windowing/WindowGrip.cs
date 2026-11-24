// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The grip a window with no title bar is moved by (ours, *"the borderless
// windowed mode needs a way to relocate the window"*, and *"invisible unless it's close"*). 1.1 has
// nothing like it: it has no borderless window. These are the rules — where the grip is and when it
// shows; `Vitomir.Platform.Render/Windowing/WindowFrameHost.cs` asks SDL and the system, and the engine's
// `Ui/WindowGripMarks.cs` (its boxes) and `Ui/Passes/CursorPass.cs` draw it.
namespace Vitomir.Platform.Windowing;

/// <summary>
/// The grip: a small square in the window's top right corner, shown only in a window with no title
/// bar and only while the pointer is near the window's top edge; held and dragged, it moves the window
/// (the system does the moving: the square is the window's one draggable spot).
/// </summary>
public static class WindowGrip
{
    /// <summary>The grip's size and its distance from the top and the right edge, in window pixels.</summary>
    public const int Width = 34, Height = 24, Margin = 6;

    /// <summary>How near the top edge the pointer must be for the grip to show, in window pixels.</summary>
    public const int NearRows = 56;

    /// <summary>Whether the grip is drawn this frame (set by the host once a frame).</summary>
    public static bool Shown { get; set; }

    /// <summary>Whether the pointer is on the grip (set by the host): it is drawn brighter, and the game's own cursor is not drawn — the system's shows there.</summary>
    public static bool Over { get; set; }

    /// <summary>The grip's rectangle in a window <paramref name="width"/> wide, in window pixels from the top left.</summary>
    public static (int X, int Y, int W, int H) Rect(int width) => (width - Margin - Width, Margin, Width, Height);

    /// <summary>Whether a window point is on the grip.</summary>
    public static bool On(int x, int y, int width)
    {
        var r = Rect(width);
        return x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
    }

    /// <summary>Whether the pointer is near enough for the grip to show: inside the window and within <see cref="NearRows"/> of its top.</summary>
    public static bool Near(int x, int y, int width, int height) =>
        x >= 0 && x < width && y >= 0 && y < Math.Min(NearRows, height);
}
