// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the window came to after a change of mode, said in full, and a full screen checked to be one
// (ours; `Platform.Windowing.WindowReport` has the rules and the report that set them). This
// file is the part that asks SDL — and, on Windows, the system itself, since what SDL believes about a
// window and what the window is are the two things that report could not tell apart. Nothing of the
// Windows part can be run in the cloud.
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// After every change of window mode the program makes — the start, Alt+Enter, the Display screen —
/// <see cref="After"/> writes the <c>Window</c> lines of <see cref="WindowReport.Lines"/> to the log: the
/// mode asked for, the mode SDL reports, the window's flags, place, size and size in pixels, and its
/// display's bounds and usable bounds; on Windows also the system's own rectangle and style for the
/// window, its monitor's, where the task bar is and whether it is above the window. A second later
/// <see cref="Frame"/> says what has changed since and checks a full screen (<see cref="WindowReport.Fault"/>):
/// one that is not flagged full screen or does not stand on its display's whole bounds is said, and left.
/// <see cref="Hear"/> names SDL's own news of the displays and of the window entering and leaving full
/// screen, a line each.
/// </summary>
static unsafe class WindowReportHost
{
    /// <summary>How long after a change the window is looked at again (ours): a change of video mode on several screens takes its time.</summary>
    const long CheckMs = 1000;
    /// <summary>How long after a change SDL's news of the displays brings the full lines again (ours).</summary>
    const long RecentMs = 10000;

    static string what = "", asked = "", said = "";
    static bool wantFull;
    static long checkAt = -1;

    static SDL_Window* Window => FullScreenHost.Window;

    /// <summary>
    /// A change the program just made: <paramref name="change"/> names it (<c>the Display screen's
    /// change</c>), <paramref name="askedFor"/> is what was asked (<c>full screen at 2560x1440</c>),
    /// <paramref name="full"/> whether that is a full screen and <paramref name="mode"/> its mode
    /// (null: the desktop's own size). The lines are written now and the check is set.
    /// </summary>
    public static void After(string change, string askedFor, bool full, ScreenMode? mode)
    {
        if (Window == null || WindowRun.Replaying) return;
        (what, asked, wantFull) = (change, askedFor, full);
        checkAt = Environment.TickCount64 + CheckMs;
        (_, said) = Write($"after {what}", null);
    }

    /// <summary>Once a frame: a second after a change, what is different by then, and the check.</summary>
    public static void Frame()
    {
        if (checkAt < 0 || Window == null || Environment.TickCount64 < checkAt) return;
        var flags = SDL_GetWindowFlags(Window);
        // Minimised (Alt+Tab out of a video mode) or hidden: nothing to look at until it is back.
        if ((flags & (SDL_WindowFlags.SDL_WINDOW_MINIMIZED | SDL_WindowFlags.SDL_WINDOW_HIDDEN)) != 0) { checkAt = Environment.TickCount64 + CheckMs; return; }
        checkAt = -1;
        var (facts, _) = Write($"a second after {what}", said);
        if (WindowReport.Fault(wantFull, facts) is { } fault) FullScreenHost.Say($"window: full screen is not right — {fault}; left as it is");
    }

    /// <summary>SDL's news of the displays, and of the window entering or leaving full screen: named, and for the displays the lines again while a change is recent.</summary>
    public static void Hear(in SDL_Event e)
    {
        string? news = e.Type switch
        {
            SDL_EventType.SDL_EVENT_DISPLAY_ADDED => "a display was added",
            SDL_EventType.SDL_EVENT_DISPLAY_REMOVED => "a display was removed",
            SDL_EventType.SDL_EVENT_DISPLAY_MOVED => "a display moved",
            SDL_EventType.SDL_EVENT_DISPLAY_DESKTOP_MODE_CHANGED => "a display's desktop mode changed",
            SDL_EventType.SDL_EVENT_DISPLAY_CURRENT_MODE_CHANGED => "a display's current mode changed",
            SDL_EventType.SDL_EVENT_DISPLAY_USABLE_BOUNDS_CHANGED => "a display's usable bounds changed (the task bar)",
            SDL_EventType.SDL_EVENT_WINDOW_ENTER_FULLSCREEN => "the window entered full screen",
            SDL_EventType.SDL_EVENT_WINDOW_LEAVE_FULLSCREEN => "the window left full screen",
            SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_CHANGED => "the window is on another display",
            SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED => "the window was minimised",
            SDL_EventType.SDL_EVENT_WINDOW_RESTORED => "the window was restored",
            _ => null,
        };
        if (news is null || Window == null || WindowRun.Replaying) return;
        bool display = e.Type is >= SDL_EventType.SDL_EVENT_DISPLAY_FIRST and <= SDL_EventType.SDL_EVENT_DISPLAY_LAST;
        FullScreenHost.Say($"window: SDL says {news}" + (display ? $" (display {(uint)e.display.displayID}, {e.display.data1}, {e.display.data2})" : ""));
    }

    /// <summary>
    /// The lines written, the facts they were written from, and the lines without their moment. Given
    /// <paramref name="before"/> — the lines of the report before — only what differs is written, in one
    /// line when nothing does: a change of mode that has settled says so in a line, not in five again.
    /// What was last drawn is always said, the frame after a change being the news.
    /// </summary>
    static (WindowFacts Facts, string Said) Write(string when, string? before)
    {
        var facts = Facts();
        var lines = WindowReport.Lines("", asked, facts).ToList();
        if (SystemOwn("") is { } system) lines.Add(system);
        string now = string.Join("\n", lines);
        if (before == now) FullScreenHost.Say($"window: [{when}] the window, its display and the task bar as they were");
        else
        {
            var was = before?.Split('\n') ?? [];
            foreach (var line in lines.Where(l => !was.Contains(l))) FullScreenHost.Say(line.Replace("window: [] ", $"window: [{when}] "));
        }
        FullScreenHost.Say($"window: [{when}] last drawn: {PictureHost.Drawn}");
        return (facts, now);
    }

    static readonly (SDL_WindowFlags Flag, string Name)[] FlagNames =
    [
        (SDL_WindowFlags.SDL_WINDOW_FULLSCREEN, "fullscreen"), (SDL_WindowFlags.SDL_WINDOW_HIDDEN, "hidden"),
        (SDL_WindowFlags.SDL_WINDOW_BORDERLESS, "borderless"), (SDL_WindowFlags.SDL_WINDOW_RESIZABLE, "resizable"),
        (SDL_WindowFlags.SDL_WINDOW_MINIMIZED, "minimized"), (SDL_WindowFlags.SDL_WINDOW_MAXIMIZED, "maximized"),
        (SDL_WindowFlags.SDL_WINDOW_OCCLUDED, "occluded"), (SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS, "input focus"),
        (SDL_WindowFlags.SDL_WINDOW_MOUSE_FOCUS, "mouse focus"), (SDL_WindowFlags.SDL_WINDOW_MOUSE_GRABBED, "mouse grabbed"),
        (SDL_WindowFlags.SDL_WINDOW_MOUSE_RELATIVE_MODE, "relative mouse"), (SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP, "always on top"),
        (SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY, "high pixel density"),
    ];

    /// <summary>The window as SDL reports it now.</summary>
    public static WindowFacts Facts()
    {
        var window = Window;
        var flags = SDL_GetWindowFlags(window);
        int x = 0, y = 0, w = 0, h = 0, pw = 0, ph = 0;
        SDL_GetWindowPosition(window, &x, &y);
        SDL_GetWindowSize(window, &w, &h);
        SDL_GetWindowSizeInPixels(window, &pw, &ph);
        var own = SDL_GetWindowFullscreenMode(window);
        var display = SDL_GetDisplayForWindow(window);
        SDL_Rect b, u;
        DisplayRect? bounds = SDL_GetDisplayBounds(display, &b) ? new DisplayRect(b.x, b.y, b.w, b.h) : null;
        DisplayRect? usable = SDL_GetDisplayUsableBounds(display, &u) ? new DisplayRect(u.x, u.y, u.w, u.h) : null;
        var current = SDL_GetCurrentDisplayMode(display);
        var desktop = SDL_GetDesktopDisplayMode(display);
        return new WindowFacts(
            (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0,
            string.Join(", ", FlagNames.Where(f => (flags & f.Flag) != 0).Select(f => f.Name)),
            new DisplayRect(x, y, w, h), new ScreenMode(pw, ph),
            own == null ? null : new ScreenMode(own->w, own->h),
            $"{SDL_GetDisplayName(display) ?? ""} (display {(uint)display})", bounds, usable,
            current == null ? null : new ScreenMode(current->w, current->h),
            desktop == null ? null : new ScreenMode(desktop->w, desktop->h));
    }

    // ---- Windows: what the system itself says of the window ----

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    const int GwlStyle = -16, GwlExStyle = -20;
    const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")] static extern int GetWindowRect(nint hwnd, Rect* rect);
    [DllImport("user32.dll")] static extern int GetClientRect(nint hwnd, Rect* rect);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] static extern int GetMonitorInfo(nint monitor, MonitorInfo* info);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] static extern nint FindWindow(string windowClass, string? title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);

    static string Show(Rect r) => $"{r.Right - r.Left}x{r.Bottom - r.Top} at {r.Left},{r.Top}";

    /// <summary>
    /// On Windows, one more line from the system: the window's rectangle and its inside's size, its
    /// style words, whether it is the foreground window, its monitor's rectangle and work area, and the
    /// task bar's rectangle (<c>Shell_TrayWnd</c>). Null elsewhere, or when the system will not say.
    /// </summary>
    static string? SystemOwn(string when)
    {
        if (!OperatingSystem.IsWindows() || Window == null) return null;
        try
        {
            nint hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(Window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, 0);
            if (hwnd == 0) return null;
            Rect outer = default, inner = default, bar = default;
            GetWindowRect(hwnd, &outer);
            GetClientRect(hwnd, &inner);
            long style = GetWindowLongPtr(hwnd, GwlStyle), extended = GetWindowLongPtr(hwnd, GwlExStyle);
            var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
            nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            string on = monitor != 0 && GetMonitorInfo(monitor, &info) != 0 ? $"its monitor {Show(info.Monitor)}, work area {Show(info.Work)}" : "its monitor not given";
            nint tray = FindWindow("Shell_TrayWnd", null);
            string taskBar = tray != 0 && GetWindowRect(tray, &bar) != 0
                ? $"the task bar {Show(bar)}, {(OnTopHost.Above(hwnd, tray) ? "above the window" : "under the window")}" : "no task bar found";
            return $"window: [{when}] the system's own: the window {Show(outer)}, its inside {inner.Right - inner.Left}x{inner.Bottom - inner.Top}, "
                   + $"style 0x{style & 0xffffffff:x8}, extended 0x{extended & 0xffffffff:x8}, {(GetForegroundWindow() == hwnd ? "the foreground window" : "not the foreground window")}; {on}; {taskBar}";
        }
        catch (Exception ex)      // never run before a Windows desktop: whatever goes wrong here costs one log line, not the game
        {
            return $"window: [{when}] the system would not be asked ({ex.Message})";
        }
    }
}
