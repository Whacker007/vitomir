// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the game's window goes when Vitomir starts (ours, "it should
// remember the game window size and position as well. Will need a safeguard in place if the resolution
// is different and/or the window position would be off the display"). 1.1 has nothing like it: its
// window is the video mode, full screen or at the mode's size. The part that touches SDL is
// `Vitomir.Platform.Render/Windowing/WindowPlaceHost.cs`; everything here is arithmetic, so the safeguard can be tested without
// a display.
using System.Globalization;

namespace Vitomir.Platform.Windowing;

/// <summary>A rectangle in the desktop's own coordinates (y counting down, as SDL's are).</summary>
public readonly record struct DisplayRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public long Area => Width <= 0 || Height <= 0 ? 0 : (long)Width * Height;

    /// <summary>How much of this rectangle lies inside <paramref name="other"/>, in square pixels.</summary>
    public long Overlap(DisplayRect other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        long h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }

    public override string ToString() => $"{Width}x{Height} at {X},{Y}";
}

/// <summary>
/// A display as SDL reports it: its name (<c>SDL_GetDisplayName</c>), its usable area — the desktop
/// less the task bar and the like (<c>SDL_GetDisplayUsableBounds</c>) — and whether it is the primary one.
/// </summary>
public sealed record DisplayArea(string Name, DisplayRect Usable, bool Primary);

/// <summary>
/// What is kept of the window: its size and the position of its inside (SDL's window position is the
/// client area's, below the title bar), the name of the display it was on, and whether it was maximised.
/// The size and position are the window's *normal* ones — what it goes back to when it is no longer
/// maximised — because a maximised window's own are the display's.
/// </summary>
public sealed record WindowPlace(DisplayRect Rect, string Display, bool Maximised);

/// <summary>Why the window went where it did.</summary>
public enum PlaceVerdict
{
    /// <summary>Where it was left.</summary>
    Restored,
    /// <summary>Nothing kept (a first start, or a <c>[window]</c> section that does not read): the fallback.</summary>
    NoneSaved,
    /// <summary>The display it was on is not connected now: the fallback.</summary>
    DisplayGone,
    /// <summary>It would not fit its display's usable area (the display's resolution went down): the fallback.</summary>
    TooBig,
    /// <summary>Its title bar, or too much of it, would be off every display: the fallback.</summary>
    OffDisplay,
    /// <summary>
    /// Its title bar would be above the top of its display, the rest of it fitting: moved down onto the display, its size
    /// and its place across kept (ours, a window with a title bar put at a borderless window's place, a
    /// 2560x1440 at 2560,0 kept borderless, stood with its bar off the top of the screen and looked borderless).
    /// </summary>
    MovedDown,
}

/// <summary>Where the window is put: its inside's size and position, and whether it is then maximised.</summary>
/// <param name="Rect">Null only when SDL reported no display at all: the window is made at the default size and the system places it.</param>
public readonly record struct Placement(DisplayRect? Rect, int Width, int Height, bool Maximised, PlaceVerdict Verdict);

/// <summary>
/// **The window's size and position, kept over starts (ours).** Kept in the <see cref="Section"/>
/// section of the player's own <c>vitomir.ini</c> — written when the window is moved or resized and when
/// the game closes — and put back at start, unless the run was given <c>--size</c> (a harness or a test)
/// or is a replay (whose tape's <c>interactive … size=</c> line sizes the window).
/// <para>**The safeguard.** What was kept is used only when all of these hold; otherwise the window
/// falls back (<see cref="Fallback"/>) to the default size, shrunk to fit if it must, centred on the
/// primary display:</para>
/// <list type="bullet">
/// <item>the display it was on (by name) is connected;</item>
/// <item>the window, title bar included, fits inside that display's usable area — so a display whose
/// resolution went down since, or whose task bar grew, gets the fallback rather than a window bigger
/// than itself;</item>
/// <item>it is **mostly on**: the whole height of its title bar is on a display, over at least half
/// of the title bar's width (so it can be grabbed and moved), and at least
/// <see cref="MinShownShare"/> of the window, title bar included, is on the displays' usable areas.</item>
/// </list>
/// One that fits but whose title bar would be above the top of its display is **moved down** onto it, its
/// size and its place across kept, when it is then mostly on (<see cref="PlaceVerdict.MovedDown"/>); until
/// Once such a window fell back. A window that was maximised is maximised again unless its display is gone.
/// <para>The same safeguard is asked of the window the run had before full screen, when Alt+Enter or the
/// Display screen brings it back with its title bar (<see cref="ForTitled"/>, <see cref="WindowInRun"/>).</para>
/// </summary>
public static class WindowPlacement
{
    /// <summary>The section in <c>vitomir.ini</c> (ours).</summary>
    public const string Section = "window";

    /// <summary>
    /// The title bar's height when the system will not say (SDL reports 0 borders for a window not yet
    /// shown on some systems, and always on the offscreen driver): about Windows 10's at 100% scaling.
    /// </summary>
    public const int AssumedTitleBar = 31;

    /// <summary>How much of the window, title bar included, must be on the displays: half (ours).</summary>
    public const double MinShownShare = 0.5;

    /// <summary>The smallest size kept; anything smaller reads as nothing kept (ours).</summary>
    public const int MinWidth = 320, MinHeight = 200;

    /// <summary>The window and its title bar together, from its inside's rectangle.</summary>
    public static DisplayRect Frame(DisplayRect inside, int titleBar) =>
        new(inside.X, inside.Y - titleBar, inside.Width, inside.Height + titleBar);

    /// <summary>
    /// Whether a window whose inside is <paramref name="inside"/> fits inside <paramref name="usable"/>,
    /// its title bar included.
    /// </summary>
    public static bool Fits(DisplayRect inside, DisplayRect usable, int titleBar) =>
        inside.Width <= usable.Width && inside.Height + titleBar <= usable.Height;

    /// <summary>
    /// Whether the window is **mostly on** the displays: its title bar's whole height on one display
    /// over at least half the bar's width, and at least <see cref="MinShownShare"/> of the window (title
    /// bar included) on their usable areas. Displays do not overlap, so their shares add up.
    /// </summary>
    public static bool MostlyOn(DisplayRect inside, IReadOnlyList<DisplayArea> displays, int titleBar)
    {
        var frame = Frame(inside, titleBar);
        var bar = new DisplayRect(frame.X, frame.Y, frame.Width, Math.Max(1, titleBar));
        long barShown = 0, shown = 0;
        foreach (var d in displays)
        {
            if (bar.Y >= d.Usable.Y && bar.Bottom <= d.Usable.Bottom) barShown += bar.Overlap(d.Usable);
            shown += frame.Overlap(d.Usable);
        }
        return barShown * 2 >= bar.Area && shown >= frame.Area * MinShownShare;
    }

    /// <summary>
    /// The fallback: <paramref name="width"/> x <paramref name="height"/> (1600x900 unless the run said
    /// otherwise), shrunk to fit the primary display's usable area with its title bar and keeping its
    /// shape, and centred there. With no display flagged primary, the first; with none at all, the
    /// default size and no position.
    /// </summary>
    public static Placement Fallback(IReadOnlyList<DisplayArea> displays, int width, int height, int titleBar,
                                     PlaceVerdict verdict, bool maximised = false)
    {
        var primary = displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();
        if (primary is null) return new Placement(null, width, height, maximised, verdict);
        var u = primary.Usable;
        double scale = Math.Min(1.0, Math.Min(u.Width / (double)width, (u.Height - titleBar) / (double)height));
        int w = Math.Max(1, (int)Math.Floor(width * scale)), h = Math.Max(1, (int)Math.Floor(height * scale));
        int x = u.X + (u.Width - w) / 2;
        int y = u.Y + titleBar + (u.Height - titleBar - h) / 2;
        return new Placement(new DisplayRect(x, y, w, h), w, h, maximised, verdict);
    }

    /// <summary>
    /// Where the window goes: <paramref name="saved"/> when the safeguard passes it, the
    /// <see cref="Fallback"/> otherwise (see the class). <paramref name="titleBar"/> is the title bar's
    /// height; 0 or less means unknown and <see cref="AssumedTitleBar"/> is used.
    /// </summary>
    /// <param name="borderless">The window is kept without its title bar (<c>[window] borderless</c>): then it has
    /// none to fit, and a window of the display's whole height fits it (ours, until then a borderless
    /// 2560x1440 window on a 1440-high display was "too big" by an assumed title bar, put at 1600x900 and kept so).</param>
    public static Placement Resolve(WindowPlace? saved, IReadOnlyList<DisplayArea> displays,
                                    int defaultWidth, int defaultHeight, int titleBar, bool borderless = false)
    {
        if (borderless) titleBar = 0;
        else if (titleBar <= 0) titleBar = AssumedTitleBar;
        if (saved is null) return Fallback(displays, defaultWidth, defaultHeight, titleBar, PlaceVerdict.NoneSaved);
        var on = displays.Where(d => string.Equals(d.Name, saved.Display, StringComparison.Ordinal)).ToList();
        if (on.Count == 0) return Fallback(displays, defaultWidth, defaultHeight, titleBar, PlaceVerdict.DisplayGone);
        return Check(saved.Rect, on, displays, defaultWidth, defaultHeight, titleBar, saved.Maximised);
    }

    /// <summary>
    /// The safeguard for a window with its title bar at <paramref name="inside"/> in this run (ours): where
    /// Alt+Enter, or the Display screen leaving full screen, brings the window back to. As <see cref="Resolve"/>,
    /// with the displays named <paramref name="display"/> — every display when none has that name (SDL named none
    /// when it was noted), since the window has not moved to another machine within a run.
    /// </summary>
    public static Placement ForTitled(DisplayRect inside, string display, IReadOnlyList<DisplayArea> displays,
                                      int defaultWidth, int defaultHeight, int titleBar, bool maximised)
    {
        if (titleBar <= 0) titleBar = AssumedTitleBar;
        var on = displays.Where(d => string.Equals(d.Name, display, StringComparison.Ordinal)).ToList();
        return Check(inside, on.Count > 0 ? on : displays, displays, defaultWidth, defaultHeight, titleBar, maximised);
    }

    /// <summary>The checks past the display's name: it fits one of <paramref name="on"/>; it is mostly on, or is once moved down; else the fallback.</summary>
    static Placement Check(DisplayRect r, IReadOnlyList<DisplayArea> on, IReadOnlyList<DisplayArea> displays,
                           int defaultWidth, int defaultHeight, int titleBar, bool maximised)
    {
        // Two displays of one name (two of the same monitor): it is enough that it fits either.
        if (!on.Any(d => Fits(r, d.Usable, titleBar)))
            return Fallback(displays, defaultWidth, defaultHeight, titleBar, PlaceVerdict.TooBig, maximised);
        if (MostlyOn(r, displays, titleBar)) return new Placement(r, r.Width, r.Height, maximised, PlaceVerdict.Restored);
        if (MovedDown(r, on, titleBar) is { } down && MostlyOn(down, displays, titleBar))
            return new Placement(down, down.Width, down.Height, maximised, PlaceVerdict.MovedDown);
        return Fallback(displays, defaultWidth, defaultHeight, titleBar, PlaceVerdict.OffDisplay, maximised);
    }

    /// <summary>
    /// <paramref name="inside"/> moved down so that its title bar's top is the top of the usable area of the display
    /// it is most on (of <paramref name="on"/>, one it fits), when the bar is above that top now; null when it is not,
    /// or it is on none of them. Only down, and only that far: the place across is kept.
    /// </summary>
    public static DisplayRect? MovedDown(DisplayRect inside, IReadOnlyList<DisplayArea> on, int titleBar)
    {
        DisplayArea? best = null;
        long most = 0;
        foreach (var d in on)
        {
            long shown = inside.Overlap(d.Usable);
            if (shown > most && Fits(inside, d.Usable, titleBar)) (best, most) = (d, shown);
        }
        if (best is null || inside.Y - titleBar >= best.Usable.Y) return null;
        return inside with { Y = best.Usable.Y + titleBar };
    }

    /// <summary>
    /// Where the Display screen's OK puts a window of <paramref name="size"/> (ours, a rule of its own):
    /// across, in the middle of <paramref name="area"/>; down, with no title bar (<paramref name="bare"/>) in the middle
    /// of the display's whole bounds, and with one its inside in the middle of the usable area but never higher than
    /// <see cref="AssumedTitleBar"/> below its top, so the bar is on the display. On a three-screen desktop (7467x1440
    /// usable) a window of 1920x1080 is at 2773,180, its bar from 149.
    /// </summary>
    public static DisplayRect DisplayScreenPlace(DisplayRect area, ScreenMode size, bool bare) =>
        new(area.X + Math.Max(0, (area.Width - size.Width) / 2),
            bare ? area.Y + Math.Max(0, (area.Height - size.Height) / 2) : Math.Max(area.Y + AssumedTitleBar, area.Y + (area.Height - size.Height) / 2),
            size.Width, size.Height);

    /// <summary>What <paramref name="keys"/> (its <see cref="Section"/>) keep, or null when it keeps nothing usable.</summary>
    public static WindowPlace? Read(WindowKeys keys)
    {
        string? Get(string key) => keys[key];
        if (!Int(Get("x"), out int x) || !Int(Get("y"), out int y)
            || !Int(Get("width"), out int w) || !Int(Get("height"), out int h)) return null;
        if (w < MinWidth || h < MinHeight) return null;
        return new WindowPlace(new DisplayRect(x, y, w, h), Get("display") ?? "", WindowKeys.IsTrue(Get("maximised")));

        static bool Int(string? s, out int v) =>
            int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>The keys written for <paramref name="place"/>, in the order they are written.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Values(WindowPlace place)
    {
        var inv = CultureInfo.InvariantCulture;
        yield return new("x", place.Rect.X.ToString(inv));
        yield return new("y", place.Rect.Y.ToString(inv));
        yield return new("width", place.Rect.Width.ToString(inv));
        yield return new("height", place.Rect.Height.ToString(inv));
        yield return new("display", place.Display);
        yield return new("maximised", place.Maximised ? "1" : "0");
    }

    /// <summary>Keeps <paramref name="place"/> in <paramref name="settings"/>, the file's other lines left as they are.</summary>
    public static void Write(IWindowSettings settings, WindowPlace place) => settings.Write(Values(place));

    /// <summary>
    /// What happened, for the console. <paramref name="keptBorderless"/>: the place kept is a borderless window's and
    /// this window has its title bar (<c>--window</c>), which is said.
    /// </summary>
    public static string Describe(Placement p, WindowPlace? saved, bool keptBorderless = false) =>
        Said(p, saved) + (keptBorderless && saved is not null ? "; the place kept is a borderless window's, and this window has its title bar" : "");

    static string Said(Placement p, WindowPlace? saved) => p.Verdict switch
    {
        PlaceVerdict.Restored => $"window: as it was left, {p.Rect}{(p.Maximised ? ", maximised" : "")} on {saved!.Display}",
        PlaceVerdict.MovedDown => $"window: {saved!.Rect} would have its title bar above the top of {saved.Display}; moved down onto it, {p.Rect}{(p.Maximised ? ", maximised" : "")}",
        PlaceVerdict.NoneSaved => $"window: {p.Width}x{p.Height}{(p.Rect is { } r ? $" at {r.X},{r.Y}" : "")} (nothing kept yet)",
        PlaceVerdict.DisplayGone => $"window: the display it was on ({saved!.Display}) is not connected; {p.Width}x{p.Height} on the primary",
        PlaceVerdict.TooBig => $"window: {saved!.Rect.Width}x{saved.Rect.Height} does not fit {saved.Display} now; {p.Width}x{p.Height} on the primary",
        _ => $"window: {saved!.Rect} would be off the display; {p.Width}x{p.Height} on the primary",
    };
}
