// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The borderless window, for the platform: the arithmetic
// and the rules of the centred window, kept here (not in the program) so that they are tested without a display.
using System.Globalization;

namespace Vitomir.Platform.Presentation;

/// <summary>A rectangle of the desktop in the system's window coordinates: its corner and its size.</summary>
public readonly record struct DisplayRect(int X, int Y, int Width, int Height)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height} at {X},{Y}");
}

/// <summary>
/// <b>The centred window (ours; the faithful program's, taken into the platform).</b>
/// A desktop may be one wide display (several screens the system presents as one, 7680x1440), with the task bar at
/// one side; the game is then wanted 2560x1440, windowed and centred on the display, whatever the task bar's place
/// and width, as it was asked of the faithful program
/// (on top of the rest). So the window has no title bar, its inside is the size asked, and it stands at
/// the centre of the display's <b>whole</b> bounds (SDL's <c>SDL_GetDisplayBounds</c>, the task bar's part
/// included, not its usable bounds) — exactly over his middle screen — and it is kept above every other
/// window, the task bar included, while it has the input focus (<see cref="OnTop"/>). The part that asks SDL is
/// the program's (<c>Vitomir.Platform.Host.WindowKeeper</c>).
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// A window <paramref name="width"/> by <paramref name="height"/> at the centre of <paramref name="bounds"/>,
    /// the display's whole bounds; a size larger than the display, either way, is cut to the display's. On
    /// 7680x1440 a 2560x1440 window is at 2560,0.
    /// </summary>
    public static DisplayRect Centre(DisplayRect bounds, int width, int height)
    {
        int w = Math.Min(width, bounds.Width), h = Math.Min(height, bounds.Height);
        return new DisplayRect(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Whether a window with no title bar is kept above every other window — the task bar included — given
    /// whether it has the input focus: on top while it has it, not when it has not, so another program brought
    /// forward with Alt+Tab is not covered by the game.
    /// </summary>
    public static bool OnTop(bool focused) => focused;

    /// <summary>The log's line for a centred window made: where it went, and why it is smaller than asked when the display is.</summary>
    public static string Describe(int width, int height, DisplayRect placed, DisplayRect display, string displayName)
    {
        string on = displayName.Length > 0 ? $" on {displayName}" : "";
        string where = string.Create(CultureInfo.InvariantCulture, $"at {placed.X},{placed.Y}{on} ({display.Width}x{display.Height} at {display.X},{display.Y})");
        return (placed.Width, placed.Height) == (width, height)
            ? $"window: centred, no title bar, {width}x{height} {where}"
            : $"window: centred, no title bar, {placed.Width}x{placed.Height} {where}, the display being smaller than the {width}x{height} asked";
    }
}
