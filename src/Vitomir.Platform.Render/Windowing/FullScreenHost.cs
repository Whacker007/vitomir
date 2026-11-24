// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Full screen (1.1 starts full screen unless `-window`; the rest is ours — `Platform.Windowing.FullScreen`
// has the rules). This file is the part that asks SDL: the display's modes, the change of mode, the way
// back to the window, and Alt+Enter. Every SDL call here fails soft: a false answer is said on the console
// and the window is kept as it was (the cloud's offscreen driver offers no modes at all).
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// Makes the window full screen at start when <see cref="FullScreen.Start"/> says so (from
/// <see cref="WindowPlaceHost.Open"/>, before the window is first shown), toggles it on Alt+Enter
/// (<see cref="Toggle"/>, from <c>KeyInput</c>), and changes the mode for the Display screen
/// (<see cref="SetDisplay"/>). What changes is written to <c>[window]</c> at once — only in a run whose
/// window is kept (<see cref="WindowStart.Keep"/>). In a replay the toggle is heard and does nothing: the
/// tape's own resize events size the replay's window as the recording's went. It also knows the size
/// picked for borderless full screen (<see cref="Picked"/>), whose shape is the picture's
/// (<c>PictureHost</c>), and after every change it hands the window to <c>WindowReportHost</c>, which
/// says in the log what the window then is and checks that a full screen is one.
/// </summary>
static unsafe class FullScreenHost
{
    static SDL_Window* window;
    static bool keep;
    /// <summary>The mode asked for when full screen is next entered: the ini's, the run's, or the Display screen's last (null: the desktop's).</summary>
    static ScreenMode? wanted = FullScreen.Default.Mode;

    /// <summary>The game's window, once made.</summary>
    public static SDL_Window* Window => window;

    /// <summary>Whether the window is full screen now (SDL's flag).</summary>
    public static bool IsFull => window != null && (SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;

    /// <summary>
    /// The start for this run: the ini's <c>[window]</c> and the flags (<see cref="FullScreen.Start"/>).
    /// <paramref name="sizeGiven"/> is <c>--size</c>'s <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    public static WindowStart Plan(bool sizeGiven, int width, int height, bool windowFlag, bool fullscreenFlag, bool harness, string? borderless = null)
    {
        var ini = WindowRun.Settings.Read();
        var kept = FullScreen.Read(ini);
        var start = FullScreen.Start(kept, WindowRun.Replaying, sizeGiven ? new ScreenMode(width, height) : null, harness, windowFlag, fullscreenFlag);
        wanted = start.Mode ?? (start.Full ? null : kept.Mode);
        lastExclusive = kept.Mode;
        worldSize = FullScreen.ReadWorldSize(ini);
        // Ours: `[window] borderless = WxH` and `--borderless`, over all of that when they apply (Borderless.Start).
        return BorderlessHost.Plan(start, ini, borderless, sizeGiven, windowFlag, fullscreenFlag);
    }

    /// <summary>The window, and whether what happens to it is written (<see cref="WindowStart.Keep"/>).</summary>
    public static void Attach(SDL_Window* on, WindowStart start)
    {
        window = on;
        keep = start.Keep;
        if (start.Full) Say($"window: full screen ({start.Why})");
    }

    /// <summary>
    /// Full screen at <paramref name="mode"/> — the closest the window's display offers
    /// (<c>SDL_GetClosestFullscreenDisplayMode</c>, any refresh, high density allowed), an exclusive change
    /// of mode as 1.1's — or, with <paramref name="mode"/> null or no mode close enough or the change
    /// refused, borderless at the desktop's size; and failing that, the window as it was. Says which.
    /// </summary>
    public static FullScreenLanded Enter(ScreenMode? mode)
    {
        if (window == null) return FullScreenLanded.Window;
        var display = SDL_GetDisplayForWindow(window);
        string name = SDL_GetDisplayName(display) ?? "the display";
        if (mode is { } m)
        {
            SDL_DisplayMode closest;
            if (!SDL_GetClosestFullscreenDisplayMode(display, m.Width, m.Height, 0f, true, &closest)) { }   // said below, with the desktop
            else if (!SDL_SetWindowFullscreenMode(window, &closest))
                Say($"window: the mode {closest.w}x{closest.h} was refused ({SDL_GetError()})");
            else if (!SDL_SetWindowFullscreen(window, true))
                Say($"window: full screen at {closest.w}x{closest.h} was refused ({SDL_GetError()})");
            else
            {
                var got = new ScreenMode(closest.w, closest.h);
                Say(FullScreen.Describe(FullScreenLanded.Exclusive, m, got, name) + Density(closest.pixel_density));
                return FullScreenLanded.Exclusive;
            }
        }
        if (SDL_SetWindowFullscreenMode(window, null) && SDL_SetWindowFullscreen(window, true))
        {
            var desk = SDL_GetDesktopDisplayMode(display);
            ScreenMode? got = desk == null ? null : new ScreenMode(desk->w, desk->h);
            Say(FullScreen.Describe(FullScreenLanded.Desktop, mode, got, name));
            return FullScreenLanded.Desktop;
        }
        Say($"window: full screen at the desktop's size was refused too ({SDL_GetError()}); a window");
        SDL_SetWindowFullscreen(window, false);
        return FullScreenLanded.Window;
    }

    /// <summary>A mode in words for the log: the size, or the desktop's own.</summary>
    public static string Words(ScreenMode? mode) => mode is { } m ? m.ToString() : "the desktop's own size";

    static string Density(float d) => d is 0f or 1f ? "" : $", {d:0.##} pixels a point";

    /// <summary>
    /// Alt+Enter (the <c>fullscreen</c> command, ours): full screen at the mode wanted, or back to the
    /// window the run had before it — its place and size, its title bar or none, and maximised if it was
    /// (<see cref="WindowPlaceHost.BackToPlace"/>), the window noted just before full screen is entered.
    /// The new state is written at once. Nothing happens in a replay but the line on the console.
    /// </summary>
    public static void Toggle()
    {
        if (window == null) return;
        if (WindowRun.Replaying) { Say("window: Alt+Enter heard; a replay stays a window (its tape's resizes size it)"); return; }
        bool on;
        if (IsFull)
        {
            if (!SDL_SetWindowFullscreen(window, false)) { Say($"window: leaving full screen was refused ({SDL_GetError()})"); return; }
            SDL_SyncWindow(window);
            WindowPlaceHost.BackToPlace();
            Say("window: a window again");
            on = false;
        }
        else
        {
            WindowPlaceHost.Note();     // the window as it is now is what Alt+Enter brings back
            var landed = Enter(wanted);
            SDL_SyncWindow(window);
            if (landed == FullScreenLanded.Window) return;
            on = true;
        }
        Pixels();
        // The mode Alt+Enter goes to is kept with the state: a file that kept the state alone would read as one from before (1600x900).
        Write(() => { FullScreen.WriteOn(WindowRun.Settings, on); FullScreen.WriteMode(WindowRun.Settings, wanted); });
        WindowReportHost.After("Alt+Enter", on ? $"full screen at {Words(wanted)}" : "a window", on, on ? wanted : null);
    }

    /// <summary>The window's size in pixels after a change, said when it is not its size (a display scaled above 100%).</summary>
    public static void Pixels()
    {
        if (window == null) return;
        int w, h, pw, ph;
        if (!SDL_GetWindowSize(window, &w, &h) || !SDL_GetWindowSizeInPixels(window, &pw, &ph)) return;
        Say((w, h) == (pw, ph) ? $"window: {w}x{h}" : $"window: {w}x{h}, drawn at {pw}x{ph} pixels");
    }

    /// <summary>
    /// The Display screen's list while full screen: the display's own modes (<c>SDL_GetFullscreenDisplayModes</c>)
    /// once each by size, with the one in use; null while a window, when the screen lists window sizes.
    /// </summary>
    public static List<ScreenMode>? Modes()
    {
        if (!IsFull) return null;
        var display = SDL_GetDisplayForWindow(window);
        var offered = new List<ScreenMode>();
        int count = 0;
        var modes = SDL_GetFullscreenDisplayModes(display, &count);
        if (modes != null)
        {
            for (int i = 0; i < count; i++) offered.Add(new ScreenMode(modes[i]->w, modes[i]->h));
            SDL_free(modes);
        }
        return FullScreen.ModeList(offered, Current());
    }

    /// <summary>The mode in use while full screen: the window's own mode, or the desktop's when it has none.</summary>
    public static ScreenMode Current()
    {
        var mode = SDL_GetWindowFullscreenMode(window);
        if (mode == null) mode = SDL_GetDesktopDisplayMode(SDL_GetDisplayForWindow(window));
        if (mode != null) return new ScreenMode(mode->w, mode->h);
        int w, h;
        SDL_GetWindowSize(window, &w, &h);
        return new ScreenMode(w, h);
    }

    /// <summary>
    /// The Display screen's OK while full screen: the chosen mode (the closest the display has) made the
    /// window's and kept as <c>fullscreenmode</c>. False when SDL would have none of it: the old mode stays.
    /// </summary>
    public static bool SetMode(int width, int height)
    {
        if (window == null) return false;
        var m = new ScreenMode(width, height);
        SDL_DisplayMode closest;
        if (!SDL_GetClosestFullscreenDisplayMode(SDL_GetDisplayForWindow(window), width, height, 0f, true, &closest)
            || !SDL_SetWindowFullscreenMode(window, &closest))
        {
            Say($"window: the mode {m} was refused ({SDL_GetError()})");
            return false;
        }
        if (!StillFull(new ScreenMode(closest.w, closest.h))) return false;
        wanted = new ScreenMode(closest.w, closest.h);
        lastExclusive = wanted;
        Say($"window: full screen at {wanted}");
        Pixels();
        Write(() => FullScreen.WriteMode(WindowRun.Settings, wanted));
        WindowReportHost.After("the Display screen's change of mode", $"full screen at {m}", true, wanted);
        return true;
    }

    /// <summary>
    /// After a change of mode made where the window stands (<c>SDL_SetWindowFullscreenMode</c> on a
    /// window that is full screen): whether it is still full screen. SDL answers true to such a change
    /// even when it gave up on it, and giving up leaves full screen (its <c>SDL_UpdateFullscreenMode</c>
    /// ends in <c>SDL_FULLSCREEN_OP_LEAVE</c> when the mode or the window's move is refused) — which is a
    /// full-screen window left at a window's size and place with the log saying otherwise. Found so, it
    /// is said and full screen is asked for afresh at <paramref name="mode"/>; false when that fails too.
    /// </summary>
    static bool StillFull(ScreenMode? mode)
    {
        SDL_SyncWindow(window);
        if (IsFull) return true;
        Say($"window: SDL left full screen while changing the mode (its last word: {SDL_GetError()}); full screen asked for afresh");
        bool full = Enter(mode) != FullScreenLanded.Window;
        SDL_SyncWindow(window);
        return full;
    }

    /// <summary>The last exclusive mode this run knew: the ini's, or the one the Display screen last set. What the Full screen row goes to.</summary>
    static ScreenMode? lastExclusive;

    /// <summary>The size picked for borderless full screen (<c>[window] worldsize</c>; null: the desktop's own): the picture's shape and the world's size.</summary>
    static ScreenMode? worldSize;
    static (ScreenMode, PictureRect, ScreenMode?)? saidPicture;

    /// <summary>
    /// The size picked while it shapes the picture: the window is in borderless full screen, a size is
    /// picked that fits the desktop (<see cref="FullScreen.WorldSizeOn"/> — one that does not, in a file
    /// from a larger desktop, counts as none, and the Display screen shows the desktop's own), and the
    /// run is not a replay (whose window is a plain one the picture's size). Null otherwise.
    /// </summary>
    public static ScreenMode? Picked() =>
        window != null && worldSize is not null && !WindowRun.Replaying && WindowMode() == FullScreen.ModeBorderless
        && Desktop() is { } desk ? FullScreen.WorldSizeOn(desk, worldSize) : null;

    /// <summary>
    /// The size the world is drawn at this frame when it is not the picture's own (ours):
    /// in borderless full screen with a size picked that is smaller than the picture
    /// (<see cref="Picture.WorldSizeIn"/>); null otherwise. The picture and the world's size are said in
    /// the log when either changes.
    /// </summary>
    public static (int Width, int Height)? WorldSize()
    {
        (ScreenMode, PictureRect, ScreenMode?)? now = null;
        int pw = 0, ph = 0;
        if (Picked() is { } picked && SDL_GetWindowSizeInPixels(window, &pw, &ph))
        {
            var size = new ScreenMode(pw, ph);
            var picture = Picture.RectOn(size, picked);
            var world = Picture.WorldSizeIn(picture, picked);
            if (Picture.HasBars(size, picture) || world is not null) now = (size, picture, world);
        }
        if (now != saidPicture)
        {
            saidPicture = now;
            Say(now is { } n ? Picture.Describe(n.Item1, n.Item2, n.Item3) : "window: the picture is the whole window; the world is drawn at the window's own size");
        }
        return now is { Item3: { } w } ? (w.Width, w.Height) : null;
    }

    /// <summary>Which of <see cref="FullScreen.WindowModes"/> the window is in now.</summary>
    public static int WindowMode() =>
        window == null ? FullScreen.ModeWindow
        : FullScreen.WindowModeOf(IsFull, SDL_GetWindowFullscreenMode(window) != null,
                                  (SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) != 0);

    /// <summary>The desktop's own mode on the window's display: what borderless full screen is at.</summary>
    public static ScreenMode? Desktop()
    {
        if (window == null) return null;
        var desk = SDL_GetDesktopDisplayMode(SDL_GetDisplayForWindow(window));
        return desk == null ? null : new ScreenMode(desk->w, desk->h);
    }

    /// <summary>The window's size now.</summary>
    static ScreenMode Size()
    {
        int w = 0, h = 0;
        SDL_GetWindowSize(window, &w, &h);
        return new ScreenMode(w, h);
    }

    /// <summary>The size the window had when it was last a window (its kept place), or its size now.</summary>
    static ScreenMode WindowSize() =>
        IsFull && WindowPlaceHost.Normal is { } kept ? new ScreenMode(kept.Width, kept.Height) : Size();

    /// <summary>
    /// The usable part of the window's display **as the desktop** (<see cref="DesktopRoom.UsableOnDesktop"/>):
    /// SDL's bounds and usable bounds are the current video mode's, which in full screen at a mode of its
    /// own is not the desktop a window will find itself on. Null when SDL will not give the bounds.
    /// </summary>
    static DisplayRect? UsableDesktop()
    {
        SDL_Rect b, u;
        var display = SDL_GetDisplayForWindow(window);
        if (Desktop() is not { } desk || !SDL_GetDisplayBounds(display, &b) || !SDL_GetDisplayUsableBounds(display, &u)) return null;
        return DesktopRoom.UsableOnDesktop(desk, new DisplayRect(b.x, b.y, b.w, b.h), new DisplayRect(u.x, u.y, u.w, u.h));
    }

    /// <summary>The whole of the window's display as the desktop: its place and the desktop's own size.</summary>
    static DisplayRect? WholeDesktop()
    {
        SDL_Rect b;
        return Desktop() is { } desk && SDL_GetDisplayBounds(SDL_GetDisplayForWindow(window), &b) ? new DisplayRect(b.x, b.y, desk.Width, desk.Height) : null;
    }

    /// <summary>
    /// The room a window with a title bar has on its display: the desktop less the task bar
    /// (<see cref="UsableDesktop"/>) and less the window's own frame (<c>SDL_GetWindowBordersSize</c>;
    /// 8 a side and 31 on top when it cannot be asked, Windows' usual).
    /// </summary>
    static ScreenMode RoomForWindow()
    {
        if (UsableDesktop() is not { } u) return Desktop() ?? FullScreen.DefaultMode;
        int top = 31, left = 8, bottom = 8, right = 8, t, l, b, r;
        if ((SDL_GetWindowFlags(window) & (SDL_WindowFlags.SDL_WINDOW_BORDERLESS | SDL_WindowFlags.SDL_WINDOW_FULLSCREEN)) == 0
            && SDL_GetWindowBordersSize(window, &t, &l, &b, &r)) (top, left, bottom, right) = (t, l, b, r);
        return new ScreenMode(u.Width - left - right, u.Height - top - bottom);
    }

    /// <summary>The display's own full-screen modes, once each by size, with <paramref name="with"/> among them.</summary>
    static List<ScreenMode> DisplayModes(ScreenMode with)
    {
        var offered = new List<ScreenMode>();
        int count = 0;
        var modes = SDL_GetFullscreenDisplayModes(SDL_GetDisplayForWindow(window), &count);
        if (modes != null)
        {
            for (int i = 0; i < count; i++) offered.Add(new ScreenMode(modes[i]->w, modes[i]->h));
            SDL_free(modes);
        }
        return FullScreen.ModeList(offered, with);
    }

    /// <summary>
    /// The video modes a window-mode row offers (ours): a window's sizes that fit the display
    /// (<see cref="FullScreen.SizesThatFit"/> — less the task bar and the frame with a title bar, the
    /// whole desktop without); for borderless full screen the same sizes up to the desktop's own, each
    /// the picture's shape and the world's size; the display's own modes for full screen.
    /// </summary>
    public static List<ScreenMode> ModesFor(int row)
    {
        if (window == null) return [];
        int now = WindowMode();
        var desk = Desktop() ?? FullScreen.DefaultMode;
        return row switch
        {
            FullScreen.ModeWindow => FullScreen.SizesThatFit(RoomForWindow(), now == row ? Size() : null),
            FullScreen.ModeBorderlessWindow => FullScreen.SizesThatFit(desk, now == row ? Size() : null),
            // The desktop's own and the standard sizes it has room for, with the size picked when it is one of neither (written by hand).
            FullScreen.ModeBorderless => FullScreen.WorldSizeOn(desk, worldSize) is { } picked
                ? [.. FullScreen.SizesThatFit(desk, desk).Append(picked).Distinct().OrderBy(m => m.Width).ThenBy(m => m.Height)]
                : FullScreen.SizesThatFit(desk, desk),
            _ => DisplayModes(CurrentFor(row)),
        };
    }

    /// <summary>The mode a row is at now, or would open at: the window's size (the last it had, fitted), the desktop's, the last exclusive mode.</summary>
    public static ScreenMode CurrentFor(int row)
    {
        if (window == null) return FullScreen.DefaultMode;
        int now = WindowMode();
        var desk = Desktop() ?? FullScreen.DefaultMode;
        switch (row)
        {
            case FullScreen.ModeBorderless: return FullScreen.WorldSizeOn(desk, worldSize) ?? desk;
            case FullScreen.ModeFull: return now == row ? Current() : lastExclusive ?? desk;   // no mode known: the desktop's own
            default:
                if (now == row) return Size();
                var room = row == FullScreen.ModeWindow ? RoomForWindow() : desk;
                var last = WindowSize();
                if (last.Width <= room.Width && last.Height <= room.Height && FullScreen.WindowSizes.Contains(last)) return last;
                var fits = FullScreen.SizesThatFit(room, null);
                return fits.Count > 0 ? fits[^1] : last;
        }
    }

    /// <summary>
    /// The Display screen's OK (ours): the window made the row's kind at the mode picked.
    /// A window gets or loses its title bar and takes the size — one without a title bar put in the
    /// middle of its display; borderless full screen is the desktop's size, the picture the shape of
    /// the size picked and the world drawn at that size (<see cref="Picked"/>,
    /// <see cref="WorldSize"/>, <c>PictureHost</c>); full screen takes the mode (the closest the display has). Between the two kinds of full screen the window never
    /// leaves full screen. Everything is written at once and said in the log. False when nothing
    /// changed: refused, or a replay. The window it makes is noted as the run's window (it was not, and
    /// Alt+Enter brought back the one before); a row and size that are the window the run had before full screen
    /// go back to its place (<see cref="WindowPlaceHost.IsNoted"/>), as Alt+Enter does, rather than to the middle.
    /// </summary>
    public static bool SetDisplay(int row, ScreenMode mode)
    {
        if (window == null) return false;
        if (WindowRun.Replaying) { Say("window: a change of window mode heard; a replay stays a window"); return false; }
        int was = WindowMode();
        Say($"window: the Display screen asks for {FullScreen.WindowModes[row].ToLowerInvariant()} at {mode} (from {FullScreen.WindowModes[was].ToLowerInvariant()} at {CurrentFor(was)})");
        var setting = FullScreen.SettingFor(row, row == FullScreen.ModeFull ? mode : lastExclusive ?? wanted);
        if (row is FullScreen.ModeWindow or FullScreen.ModeBorderlessWindow)
        {
            bool bare = row == FullScreen.ModeBorderlessWindow;
            // Where it will go, asked before a video mode is left: just after, the display's usable bounds are stale (DesktopRoom).
            var room = bare ? WholeDesktop() : UsableDesktop();
            bool back = IsFull && WindowPlaceHost.IsNoted(bare, mode);
            if (IsFull)
            {
                if (!SDL_SetWindowFullscreen(window, false)) { Say($"window: leaving full screen was refused ({SDL_GetError()})"); return false; }
                SDL_SyncWindow(window);
            }
            if (back)
            {
                // The window the run had before full screen, at its size: where it was, as Alt+Enter.
                WindowPlaceHost.BackToPlace();
                Say($"window: {(bare ? "a window with no title bar" : "a window")} at {Size()}, where it was before full screen");
            }
            else
            {
                SDL_SetWindowBordered(window, !bare);
                SDL_SyncWindow(window);
                if (!SDL_SetWindowSize(window, mode.Width, mode.Height)) Say($"window: the size {mode} was refused ({SDL_GetError()})");
                if (room is { } area)
                {
                    var at = WindowPlacement.DisplayScreenPlace(area, mode, bare);
                    SDL_SetWindowPosition(window, at.X, at.Y);
                }
                SDL_SyncWindow(window);
                WindowFrameHost.Hold();     // this size is the one it keeps; its edges do not drag
                WindowPlaceHost.Note();     // the run's window now, what Alt+Enter comes back to
                Say($"window: {(bare ? "a window with no title bar" : "a window")} at {Size()}");
            }
            Write(() => FullScreen.WriteBorderless(WindowRun.Settings, bare));
        }
        else if (IsFull)
        {
            bool ok;
            if (setting.Mode is { } m)
            {
                SDL_DisplayMode closest;
                ok = SDL_GetClosestFullscreenDisplayMode(SDL_GetDisplayForWindow(window), m.Width, m.Height, 0f, true, &closest)
                     && SDL_SetWindowFullscreenMode(window, &closest);
                if (ok) { lastExclusive = new ScreenMode(closest.w, closest.h); Say($"window: full screen at {lastExclusive}"); }
            }
            else
            {
                ok = SDL_SetWindowFullscreenMode(window, null);
                if (ok) Say($"window: borderless full screen at the desktop's {Desktop()}");
            }
            if (!ok) { Say($"window: the change was refused ({SDL_GetError()})"); return false; }
            if (!StillFull(setting.Mode)) return false;
            wanted = setting.Mode;
        }
        else
        {
            WindowPlaceHost.Note();     // the window as it is now is what leaving full screen brings back
            var landed = Enter(setting.Mode);
            SDL_SyncWindow(window);
            if (landed == FullScreenLanded.Window) return false;
            wanted = setting.Mode;
            if (landed == FullScreenLanded.Exclusive) lastExclusive = setting.Mode;
        }
        if (row == FullScreen.ModeBorderless)
        {
            worldSize = Desktop() is { } desk ? FullScreen.WorldSizeOn(desk, mode) : null;
            Write(() => FullScreen.WriteWorldSize(WindowRun.Settings, worldSize));
        }
        PictureHost.Changed();      // the picture may be another size with the window's as it was: heard as a resize
        Pixels();
        // The mode is kept whichever row it is — for a window, the one Alt+Enter goes to — so the file never keeps the state alone.
        Write(() => { FullScreen.WriteOn(WindowRun.Settings, setting.On); FullScreen.WriteMode(WindowRun.Settings, setting.On ? setting.Mode : wanted); });
        WindowReportHost.After("the Display screen's change", $"{FullScreen.WindowModes[row].ToLowerInvariant()} at {mode}", setting.On, setting.On ? setting.Mode : null);
        return true;
    }

    /// <summary>
    /// What the window does, said where it can be read: the console, and — made Vitomir a
    /// windowed program with no console of its own — the log (the game's, <see cref="WindowRun.Say"/>).
    /// </summary>
    public static void Say(string line) => WindowRun.Say(line);

    static void Write(Action write)
    {
        if (!keep) return;
        try { write(); }
        catch (IOException ex) { Say($"window: not kept ({ex.Message})"); }
        catch (UnauthorizedAccessException ex) { Say($"window: not kept ({ex.Message})"); }
    }
}
