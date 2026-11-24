// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window the run had before full screen (ours). On a three-screen desktop the Display
// screen made a window of 1920x1080 with its title bar, Alt+Enter went full screen, and Alt+Enter again went to
// borderless windowed instead: the window came back at the place kept from the start (a borderless 2560x1440 at
// 2560,0), its title bar off the top of the screen. The Display screen's change had not been noted (its window's
// events are not heard on the front end), and the way back took the place noted last. The part that asks SDL is
// `Vitomir.Platform.Render/Windowing/WindowPlaceHost.cs`; everything here is rules, so it is tested without a display.

namespace Vitomir.Platform.Windowing;

/// <summary>What the window is now, as SDL says: its inside's rectangle (null when SDL would not give it, or it is not a plain window), its state and the name of its display.</summary>
public readonly record struct WindowNow(DisplayRect? Rect, bool Full, bool Maximised, bool Minimised, bool Borderless, string Display);

/// <summary>Where the window goes back to out of full screen: its inside (null: left where SDL puts it), its title bar or none, maximised or not, and why.</summary>
public readonly record struct WindowBack(DisplayRect? Rect, bool Bordered, bool Maximised, PlaceVerdict Verdict);

/// <summary>
/// **The window the run had before full screen** (ours; first the rectangle was kept, then the rest): its size and place
/// (the normal ones, not a maximised window's), whether it had its title bar, whether it was maximised, and its
/// display. Noted whenever the window is looked at while it is a window (<see cref="Note"/>) — after the start,
/// after a move or a resize that is heard, after the Display screen's change, and just before full screen is
/// entered — and never while it is full screen, so it is what Alt+Enter, or the Display screen leaving full screen,
/// brings back (<see cref="Back"/>). Kept in the run whether or not the run writes <c>[window]</c>.
/// </summary>
public sealed class WindowInRun
{
    /// <summary>The window's normal size and place (not maximised, not minimised, not full screen), once it has had one.</summary>
    public DisplayRect? Normal { get; private set; }
    /// <summary>Whether it was maximised when it was last a window.</summary>
    public bool Maximised { get; private set; }
    /// <summary>Whether it had its title bar when it was last a window.</summary>
    public bool Bordered { get; private set; } = true;
    /// <summary>The name of the display it was last on ("" until one is named).</summary>
    public string Display { get; private set; } = "";

    /// <summary>What the start put: the place (null when the system places it) and whether maximised and titled.</summary>
    public void Start(DisplayRect? rect, bool maximised, bool bordered)
    {
        if (rect is { } r) Normal = r;
        Maximised = maximised;
        Bordered = bordered;
    }

    /// <summary>
    /// Notes the window as it is now: nothing while it is minimised; its state (maximised, titled) only while it is a
    /// window; its rectangle only while it is a plain window; its display whenever it is named. True once a normal
    /// rectangle is known.
    /// </summary>
    public bool Note(WindowNow now)
    {
        if (now.Minimised) return false;
        if (!now.Full)
        {
            Maximised = now.Maximised;
            Bordered = !now.Borderless;
            if (!now.Maximised && now.Rect is { } r) Normal = r;
        }
        if (now.Display.Length > 0) Display = now.Display;
        return Normal is not null;
    }

    /// <summary>
    /// Whether the Display screen's window row and size (<paramref name="bordered"/>, <paramref name="size"/>) is the
    /// window noted — its kind and its size — so that leaving full screen for it goes back to its place.
    /// </summary>
    public bool Is(bool bordered, ScreenMode size) =>
        Normal is { } r && Bordered == bordered && r.Width == size.Width && r.Height == size.Height;

    /// <summary>
    /// Where the window goes back to: the rectangle noted, with its title bar or without and maximised as it was. A
    /// window with its title bar is first asked of the safeguard (<see cref="WindowPlacement.ForTitled"/>): one whose
    /// bar would be off its display is moved down onto it, or — when it does not fit with the bar — given the
    /// fallback (<paramref name="fallbackWidth"/> x <paramref name="fallbackHeight"/>, the run's default size, centred
    /// on the primary display). A window without one is put back as it was. Nothing noted: left where SDL puts it.
    /// </summary>
    public WindowBack Back(IReadOnlyList<DisplayArea> displays, int titleBar, int fallbackWidth, int fallbackHeight)
    {
        if (Normal is not { } r) return new WindowBack(null, Bordered, Maximised, PlaceVerdict.NoneSaved);
        if (!Bordered || displays.Count == 0) return new WindowBack(r, Bordered, Maximised, PlaceVerdict.Restored);
        var p = WindowPlacement.ForTitled(r, Display, displays, fallbackWidth, fallbackHeight, titleBar, Maximised);
        return new WindowBack(p.Rect, true, p.Maximised, p.Verdict);
    }

    /// <summary>What the way back did, for the log, when it is not the window as it was; null when it is.</summary>
    public string? Describe(WindowBack back) => back.Verdict switch
    {
        PlaceVerdict.MovedDown => $"window: back from full screen its title bar would have been above the top of its display; moved down onto it, {back.Rect}",
        PlaceVerdict.TooBig => $"window: back from full screen {Normal?.Width}x{Normal?.Height} does not fit its display with a title bar; {back.Rect}",
        PlaceVerdict.OffDisplay => $"window: back from full screen {Normal} would be off the display; {back.Rect}",
        _ => null,
    };
}
