// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window's size and position kept over starts (ours; `Platform.Windowing.WindowPlacement` has the rules and
// the safeguard). This file is the part that asks SDL: the displays, the window's borders, where the
// window is now, and the events that say it moved.
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

using LoopInputKind = Vitomir.Platform.Modules.LoopInputKind;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// Makes the game's window where it was left (<see cref="Open"/>), notices it moving or being resized
/// (<see cref="Hear"/>), and writes where it is to the player's own <c>vitomir.ini</c> once it has
/// stayed put for a moment (<see cref="Tick"/>) and when the game closes (<see cref="Save"/>).
/// Off for a run given <c>--size</c> and for a replay: such a window is made as asked and never kept;
/// a run given <c>--window</c> and the harness's runs put it where it was left and write nothing
/// (<see cref="FullScreen.Start"/>). While the window is full screen its place is not noted: the one
/// noted last as a window is where it goes back to (<see cref="BackToPlace"/>, <see cref="WindowInRun"/>), noted
/// too after the Display screen's change and just before full screen is entered (<see cref="Note"/>), since
/// the front end's loop does not hand the window's events here.
/// </summary>
static unsafe class WindowPlaceHost
{
    /// <summary>How long the window must stay put after a move or a resize before it is written (ours): a drag sends many.</summary>
    const long SettleMs = 500;

    static SDL_Window* window;
    static bool on;
    static long pendingSince = -1;
    /// <summary>The window the run had before full screen: its normal size and place, its title bar or none, maximised or not, its display.</summary>
    static readonly WindowInRun place = new();
    /// <summary>The window's place when it was last a plain window (not maximised, not full screen), if it has been one.</summary>
    public static DisplayRect? Normal => place.Normal;
    /// <summary>The run's default size (1600x900 unless the run said otherwise): the safeguard's fallback on the way back from full screen.</summary>
    static int defaultWidth = 1600, defaultHeight = 900;
    static WindowPlace? written;

    /// <summary>
    /// The window, made resizable at the kept size and position when <paramref name="start"/> says to
    /// restore it — the safeguard deciding (<see cref="WindowPlacement.Resolve"/>) — or at
    /// <paramref name="width"/> x <paramref name="height"/> where the system puts it when not; then full
    /// screen, before it is first shown, when <paramref name="start"/> says so (<see cref="FullScreenHost"/>).
    /// Its place is written as it changes only when <see cref="WindowStart.Keep"/>.
    /// </summary>
    public static SDL_Window* Open(string title, int width, int height, WindowStart start)
    {
        on = start.Keep;
        (defaultWidth, defaultHeight) = (width, height);
        if (start.Borderless is { } inside)
        {
            // Ours: no title bar, centred on its display's whole bounds, never kept (BorderlessHost).
            window = BorderlessHost.Open(title, inside, start.Why);
            if (window == null) return null;
            FullScreenHost.Attach(window, start);
            WindowFrameHost.Attach(window);   // the grip moves it, as any window with no title bar
            WindowFrameHost.Hold();
            Look();   // where Alt+Enter brings it back to (BackToPlace)
            return window;
        }
        // Made hidden, so it is never seen at the wrong place or size: the borders are asked of the real
        // window first, and full screen is asked for before it shows.
        window = SDL_CreateWindow(title, width, height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (window == null) return null;
        WindowIcon.Set(window);
        FullScreenHost.Attach(window, start);
        WindowFrameHost.Attach(window);
        Placement? placed = null;
        WindowPlace? saved = null;
        // A window kept without its title bar is made so again (ours, `[window] borderless`) — but not for
        // `--window`, which is a window with its title bar whatever was kept (`WindowStart.Titled`): until
        // Once it read the key, was made borderless and passed the safeguard as one.
        bool keptBare = start.Restore && FullScreen.ReadBorderless(WindowRun.Settings.Read());
        bool bare = keptBare && !start.Titled;
        if (start.Restore)
        {
            int top = 0, left = 0, bottom = 0, right = 0;
            SDL_GetWindowBordersSize(window, &top, &left, &bottom, &right);
            var settings = WindowRun.Settings.Read();
            saved = WindowPlacement.Read(settings);
            // A window with its title bar is asked to fit with one, the place kept a borderless window's or not.
            var p = WindowPlacement.Resolve(saved, Displays(), width, height, top, bare);
            placed = p;
            FullScreenHost.Say(WindowPlacement.Describe(p, saved, keptBorderless: keptBare && !bare));
            if (p.Width != width || p.Height != height) SDL_SetWindowSize(window, p.Width, p.Height);
            if (p.Rect is { } r) SDL_SetWindowPosition(window, r.X, r.Y);
            place.Start(p.Rect, p.Maximised, !bare);
        }
        // Full screen on the display the window was put on (the kept one, else the primary), before it is
        // shown; a window maximised as it was left is maximised again when full screen ends (BackToPlace).
        if (bare) SDL_SetWindowBordered(window, false);
        bool full = start.Full && FullScreenHost.Enter(start.Mode) != FullScreenLanded.Window;
        SDL_ShowWindow(window);
        if (placed is { Maximised: true } && !full) SDL_MaximizeWindow(window);
        SDL_SyncWindow(window);
        if (full) FullScreenHost.Pixels();
        written = placed is { Verdict: PlaceVerdict.Restored } ? saved : null;
        WindowFrameHost.Hold();     // the size it has is the size it keeps: its edges do not drag
        Look();
        // What the window came to, in the log, and a full screen checked a second on (WindowReportHost): a player's run, or any full screen.
        if (start.Keep || full)
            WindowReportHost.After("the start", full ? $"full screen at {FullScreenHost.Words(start.Mode)} ({start.Why})" : $"a window ({start.Why})", full, full ? start.Mode : null);
        return window;
    }

    /// <summary>
    /// Out of full screen: the window the run had before it (<see cref="WindowInRun"/>) — its place and size, its
    /// title bar or none, and maximised if it was: the window noted last as a window, which full screen never
    /// overwrites (<see cref="Look"/>). One with its title bar is asked of the safeguard first
    /// (<see cref="WindowInRun.Back"/>): its bar is never left off its display.
    /// </summary>
    public static void BackToPlace()
    {
        if (window == null) return;
        var flags = SDL_GetWindowFlags(window);
        bool bareNow = (flags & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) != 0;
        if (bareNow == place.Bordered)
        {
            SDL_SetWindowBordered(window, place.Bordered);
            SDL_SyncWindow(window);
        }
        int top = 0, left = 0, bottom = 0, right = 0;
        if (place.Bordered) SDL_GetWindowBordersSize(window, &top, &left, &bottom, &right);
        var back = place.Back(Displays(), top, defaultWidth, defaultHeight);
        if ((flags & SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) == 0 && back.Rect is { } r)
        {
            SDL_SetWindowSize(window, r.Width, r.Height);
            SDL_SetWindowPosition(window, r.X, r.Y);
        }
        if (back.Maximised && (flags & SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) == 0) SDL_MaximizeWindow(window);
        SDL_SyncWindow(window);
        if (place.Describe(back) is { } line) FullScreenHost.Say(line);
        WindowFrameHost.Hold();
        Look();
    }

    /// <summary>
    /// Whether the Display screen's window row at <paramref name="size"/> (with its title bar unless
    /// <paramref name="bare"/>) is the window the run had before full screen, so that leaving full screen for it goes
    /// back to its place (<see cref="BackToPlace"/>) rather than to the middle of the display.
    /// </summary>
    public static bool IsNoted(bool bare, ScreenMode size) => place.Is(!bare, size);

    /// <summary>
    /// Notes the window as it is now, as the run's (<see cref="WindowInRun.Note"/>): after the Display screen's change
    /// and just before full screen is entered, as a heard move or resize does; nothing while it is full screen.
    /// </summary>
    public static void Note() => Look();

    /// <summary>Every connected display, by SDL (<c>SDL_GetDisplays</c>, <c>SDL_GetDisplayUsableBounds</c>).</summary>
    static List<DisplayArea> Displays()
    {
        var list = new List<DisplayArea>();
        int count = 0;
        var ids = SDL_GetDisplays(&count);
        if (ids == null) return list;
        var primary = SDL_GetPrimaryDisplay();
        for (int i = 0; i < count; i++)
        {
            SDL_Rect u;
            if (!SDL_GetDisplayUsableBounds(ids[i], &u)) continue;
            list.Add(new DisplayArea(SDL_GetDisplayName(ids[i]) ?? "", new DisplayRect(u.x, u.y, u.w, u.h), ids[i] == primary));
        }
        SDL_free(ids);
        return list;
    }

    /// <summary>
    /// Notes where the window is now (<see cref="WindowInRun.Note"/>): its normal rectangle when it is neither maximised
    /// nor minimised, whether it is maximised and has its title bar while it is a window, and its display. Full screen
    /// is not the window's place: what it goes back to is what was noted before it.
    /// </summary>
    static bool Look()
    {
        if (window == null) return false;
        var flags = SDL_GetWindowFlags(window);
        bool min = (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;
        bool full = (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0, max = (flags & SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0;
        DisplayRect? rect = null;
        int x, y, w, h;
        if (!min && !full && !max && SDL_GetWindowPosition(window, &x, &y) && SDL_GetWindowSize(window, &w, &h)) rect = new DisplayRect(x, y, w, h);
        string name = min ? "" : SDL_GetDisplayName(SDL_GetDisplayForWindow(window)) ?? "";
        return place.Note(new WindowNow(rect, full, max, min, (flags & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) != 0, name));
    }

    /// <summary>A window event: moved, resized, maximised or restored starts the wait before it is written (in a window not kept, only noted).</summary>
    public static void Hear(in Vitomir.Platform.Modules.LoopInput e)
    {
        if (!on)
        {
            // Not kept, but still noted: it is where Alt+Enter brings the window back to (BackToPlace).
            if (e.Kind is LoopInputKind.Moved or LoopInputKind.Resized) Look();
            return;
        }
        switch (e.Kind)
        {
            case LoopInputKind.Moved or LoopInputKind.Resized or LoopInputKind.Maximized or LoopInputKind.Restored
                or LoopInputKind.DisplayChanged:
                pendingSince = Environment.TickCount64;
                break;
            case LoopInputKind.Quit or LoopInputKind.CloseRequested:
                Save();
                break;
        }
    }

    /// <summary>Once a frame: writes the window's place when it has stayed put for <see cref="SettleMs"/> since it last moved.</summary>
    public static void Tick()
    {
        BorderlessHost.Tick();                                         // on top while it has the focus (ours)
        if (!on || pendingSince < 0 || Environment.TickCount64 - pendingSince < SettleMs) return;
        pendingSince = -1;
        Save();
    }

    /// <summary>Writes where the window is (only what changed; nothing at all for a window not kept).</summary>
    public static void Save()
    {
        if (!on || window == null) return;
        pendingSince = -1;
        if (!Look() || place.Normal is not { } r) return;
        var kept = new WindowPlace(r, place.Display, place.Maximised);
        if (kept == written) return;
        try { WindowPlacement.Write(WindowRun.Settings, kept); written = kept; }
        catch (IOException ex) { FullScreenHost.Say($"window: not kept ({ex.Message})"); }
        catch (UnauthorizedAccessException ex) { FullScreenHost.Say($"window: not kept ({ex.Message})"); }
    }
}
