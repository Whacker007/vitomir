// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The picture's rectangle in borderless full screen (ours; nothing of 1.1's, whose window is its video
// mode). A desktop can be 7680x1440 — three screens the system sees as one display — and borderless
// full screen drew across all three. So:
//   -    the picture — the world and the interface together — takes the shape of the size picked on the
//        Display screen, as large as fits the desktop, centred, black everywhere else; the size picked
//        stays the size the world is drawn at;
//   -    the pointer is kept inside the picture while the window has the keyboard.
// Everything here is rules, so they can be tested without a display; the SDL part is
// `Vitomir.Platform.Render/Windowing/PictureHost.cs`.
namespace Vitomir.Platform.Windowing;

/// <summary>The picture's place in its window: its top left corner and its size, in the window's own units.</summary>
public readonly record struct PictureRect(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height} at {X},{Y}";
}

/// <summary>
/// **The picture in borderless full screen (ours).** The window is always the desktop's size; the
/// picture is the part of it the game is drawn in and reads the pointer in. Inside the picture
/// everything is as if the window were the picture: the world's projection, the interface's layout, every
/// pointer coordinate, and what a screenshot saves.
/// <list type="bullet">
/// <item><see cref="RectOn"/>: the shape (width to height) of the size picked, made as large as fits —
/// it fills the height or the width, whichever limits — and centred. One rule on any screen: a 5:4 size
/// on a 16:9 screen has bars at its sides, a 16:9 size on a 5:4 screen bars above and below.</item>
/// <item><see cref="WorldSizeIn"/>: the size picked is still the size the world is drawn at, stretched
/// over the picture, while the interface is drawn at the picture's own pixels.</item>
/// <item><see cref="FromWindow"/>, <see cref="ToWindow"/> and <see cref="On"/>: a window pixel as the
/// picture's, the way back, and whether a pixel is on the picture at all — a click or a wheel turn on a
/// bar does nothing.</item>
/// </list>
/// </summary>
public static class Picture
{
    /// <summary>The largest window a replay nobody watches is shown in (ours): its picture fitted inside, keeping its shape.</summary>
    public const int ShrunkWidth = 640, ShrunkHeight = 480;

    /// <summary>
    /// The window a replay's picture of <paramref name="w"/> x <paramref name="h"/> is shown in when nobody watches
    /// it (ours): the picture is still drawn at its recording's size, which is what the
    /// game reads, and only shown this small. Never larger than the picture.
    /// </summary>
    public static (int Width, int Height) ShrunkWindow(int w, int h)
    {
        double s = Math.Min(1.0, Math.Min(ShrunkWidth / (double)Math.Max(1, w), ShrunkHeight / (double)Math.Max(1, h)));
        return (Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)));
    }

    /// <summary>
    /// The picture's rectangle on a window of <paramref name="window"/> (borderless full screen: the
    /// desktop's size) for the size <paramref name="picked"/>: the whole window when nothing is picked
    /// (<c>desktop</c>) or the two have the same shape; otherwise the picked size's shape at the
    /// window's whole height (a window wider than the shape: bars at the sides) or whole width (a
    /// window taller than it: bars above and below), the other side rounded to the nearest pixel, in
    /// the middle — an odd pixel left over goes to the right or the bottom bar.
    /// </summary>
    public static PictureRect RectOn(ScreenMode window, ScreenMode? picked)
    {
        var whole = new PictureRect(0, 0, window.Width, window.Height);
        if (picked is not { Width: > 0, Height: > 0 } p || window.Width <= 0 || window.Height <= 0) return whole;
        long wide = (long)window.Width * p.Height, tall = (long)window.Height * p.Width;
        if (wide == tall) return whole;
        if (wide > tall)
        {
            int w = Math.Clamp((int)((tall + p.Height / 2) / p.Height), 1, window.Width);
            return new PictureRect((window.Width - w) / 2, 0, w, window.Height);
        }
        int h = Math.Clamp((int)((wide + p.Width / 2) / p.Width), 1, window.Height);
        return new PictureRect(0, (window.Height - h) / 2, window.Width, h);
    }

    /// <summary>Whether the picture leaves any of a window of <paramref name="window"/> black.</summary>
    public static bool HasBars(ScreenMode window, PictureRect picture) =>
        picture.Width != window.Width || picture.Height != window.Height;

    /// <summary>
    /// The size the world is drawn at inside <paramref name="picture"/> when it is not the picture's
    /// own: <paramref name="picked"/> when it is no larger than the picture either way and not its
    /// size; null — the picture's own pixels, nothing stretched — for the picture's size, a larger one,
    /// or none.
    /// </summary>
    public static ScreenMode? WorldSizeIn(PictureRect picture, ScreenMode? picked) =>
        picked is { } p && p.Width <= picture.Width && p.Height <= picture.Height
        && (p.Width, p.Height) != (picture.Width, picture.Height) ? p : null;

    /// <summary>Whether the window's pixel (<paramref name="x"/>, <paramref name="y"/>) is on the picture and not on a bar.</summary>
    public static bool On(PictureRect picture, float x, float y) =>
        x >= picture.X && y >= picture.Y && x < picture.X + picture.Width && y < picture.Y + picture.Height;

    /// <summary>
    /// A window pixel as the picture's: the bar's offset taken off, and a pixel on a bar held to the
    /// picture's nearest edge — where the pointer is while it is kept inside, and where the
    /// game's own cursor waits when it is not.
    /// </summary>
    public static (float X, float Y) FromWindow(PictureRect picture, float x, float y) =>
        (Math.Clamp(x - picture.X, 0f, Math.Max(0, picture.Width - 1)), Math.Clamp(y - picture.Y, 0f, Math.Max(0, picture.Height - 1)));

    /// <summary>A picture pixel as the window's: the bar's offset put back on.</summary>
    public static (float X, float Y) ToWindow(PictureRect picture, float x, float y) => (x + picture.X, y + picture.Y);

    /// <summary>What is said in the log when the picture changes.</summary>
    public static string Describe(ScreenMode window, PictureRect picture, ScreenMode? world) =>
        (HasBars(window, picture)
            ? $"window: the picture is {picture} of the window's {window}, black around it"
            : $"window: the picture is the whole window, {window}")
        + (world is { } w ? $"; the world is drawn at {w} and stretched over it, the interface at the picture's own size"
                          : "; the world is drawn at the picture's own size");
}
