// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window's frame (ours, 1.1 has no window to speak of — it is full screen unless `-window`,
// and that window cannot be resized). Two things:
//   * the window's size cannot be dragged in windowed mode; the only way to change the resolution
//     is that menu, with the one exception of the maximise button. The window keeps
//     SDL's resizable flag (without it there is no maximise button), and on Windows its frame's
//     sizing style is taken off, which leaves the button and takes the dragging edges. Whatever the
//     system, a window found at a size nobody chose is put back to the size it was given.
//   * *"the borderless windowed mode needs a way to relocate the window"* — the grip
//     (`Platform.Windowing.WindowGrip`): the window's one draggable spot, by SDL's hit test.
// None of the Windows part can be run in the cloud; every step says what it did in the log.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

static unsafe class WindowFrameHost
{
    static SDL_Window* window;
    /// <summary>The size the window was given — at start, by the Display screen, or coming back from full screen: the one it is put back to.</summary>
    static ScreenMode? held;
    /// <summary>How many frames running the window was put back and did not stay: after three, the size it has is the one held.</summary>
    static int tries;
    static bool saidGrip;

    /// <summary>The window, once made: its hit test set (the grip).</summary>
    public static void Attach(SDL_Window* on)
    {
        window = on;
        if (window == null) return;
        if (!SDL_SetWindowHitTest(window, &HitTest, 0))
            FullScreenHost.Say($"window: no hit test, so no grip to move a window with no title bar ({SDL_GetError()})");
    }

    static bool Plain(SDL_WindowFlags flags) =>
        (flags & (SDL_WindowFlags.SDL_WINDOW_FULLSCREEN | SDL_WindowFlags.SDL_WINDOW_MAXIMIZED | SDL_WindowFlags.SDL_WINDOW_MINIMIZED)) == 0;

    static bool Bare(SDL_WindowFlags flags) => Plain(flags) && (flags & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) != 0;

    /// <summary>SDL's hit test: the grip's square of a window with no title bar is draggable; everything else is the window's own.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static SDL_HitTestResult HitTest(SDL_Window* win, SDL_Point* at, nint data)
    {
        if (win == null || at == null || WindowRun.Replaying || !Bare(SDL_GetWindowFlags(win))) return SDL_HitTestResult.SDL_HITTEST_NORMAL;
        int w = 0, h = 0;
        SDL_GetWindowSize(win, &w, &h);
        return WindowGrip.On(at->x, at->y, w) ? SDL_HitTestResult.SDL_HITTEST_DRAGGABLE : SDL_HitTestResult.SDL_HITTEST_NORMAL;
    }

    /// <summary>
    /// The window's size now taken as the one it was given, and its edges made not to drag. Called
    /// after every change of size or kind the program itself makes (start, the Display screen,
    /// leaving full screen).
    /// </summary>
    public static void Hold()
    {
        if (window == null || WindowRun.Replaying) return;
        var flags = SDL_GetWindowFlags(window);
        if ((flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0) return;
        if (Plain(flags))
        {
            int w = 0, h = 0;
            SDL_GetWindowSize(window, &w, &h);
            held = new ScreenMode(w, h);
            tries = 0;
        }
        FixEdges();
    }

    /// <summary>
    /// Once a frame: the grip shown or not, and a window dragged to a size of its own put back —
    /// and, the frame's other window business, the picture's pointer and the full screen's check.
    /// The pointer is asked of the system, not of the window's events: over the grip the events are
    /// the system's.
    /// </summary>
    public static void Frame()
    {
        WindowGrip.Shown = WindowGrip.Over = false;
        PictureHost.Frame();            // the pointer kept inside the picture in borderless full screen
        WindowReportHost.Frame();       // a full screen looked at again a second after it was asked for
        OnTopHost.Frame();              // a full screen at a mode of its own kept above the task bar
        if (window == null || WindowRun.Replaying) return;
        var flags = SDL_GetWindowFlags(window);
        if (!Plain(flags)) return;
        int w = 0, h = 0, x = 0, y = 0;
        float gx = 0, gy = 0;
        SDL_GetWindowSize(window, &w, &h);
        var buttons = SDL_GetGlobalMouseState(&gx, &gy);
        if (Bare(flags) && SDL_GetWindowPosition(window, &x, &y))
        {
            int px = (int)gx - x, py = (int)gy - y;
            bool focus = (flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
            WindowGrip.Shown = focus && WindowGrip.Near(px, py, w, h);
            WindowGrip.Over = WindowGrip.Shown && WindowGrip.On(px, py, w);
            if (WindowGrip.Over && !saidGrip) { saidGrip = true; FullScreenHost.Say("window: the pointer is on the grip (hold and drag it to move the window)"); }
        }
        // Put back: not while a button is down (a drag still going), and not for ever.
        held ??= new ScreenMode(w, h);                    // first seen as a plain window (it started maximised)
        if (held is not { } size || (w, h) == (size.Width, size.Height)) { tries = 0; return; }
        if ((buttons & SDL_MouseButtonFlags.SDL_BUTTON_LMASK) != 0) return;
        if (tries >= 3)
        {
            FullScreenHost.Say($"window: it will not stay at {size}; left at {w}x{h}");
            held = new ScreenMode(w, h);
            tries = 0;
            return;
        }
        tries++;
        FullScreenHost.Say($"window: found at {w}x{h}, a size nobody chose; put back to {size} (the Display screen changes it)");
        SDL_SetWindowSize(window, size.Width, size.Height);
        SDL_SyncWindow(window);
    }

    // ---- Windows: the frame's sizing style ----

    const int GwlStyle = -16;
    const long WsThickFrame = 0x00040000;
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] static extern int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    static bool saidEdges;

    /// <summary>
    /// On Windows, the sizing style (<c>WS_THICKFRAME</c>) off the window: its edges no longer drag
    /// and the maximise button stays. SDL puts the style back whenever it rebuilds the window's
    /// style (a title bar on or off, full screen left), so this is asked again after each. The frame
    /// is a little thinner without it, so the size is given again afterwards.
    /// </summary>
    static void FixEdges()
    {
        if (!OperatingSystem.IsWindows() || window == null) return;
        try
        {
            nint hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, 0);
            if (hwnd == 0) return;
            long style = GetWindowLongPtr(hwnd, GwlStyle);
            if ((style & WsThickFrame) == 0) return;
            SetWindowLongPtr(hwnd, GwlStyle, (nint)(style & ~WsThickFrame));
            SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            if (held is { } size && Plain(SDL_GetWindowFlags(window)))
            {
                SDL_SetWindowSize(window, size.Width, size.Height);
                SDL_SyncWindow(window);
            }
            if (!saidEdges) { saidEdges = true; FullScreenHost.Say("window: its edges do not drag (the sizing style is off its frame; the maximise button stays)"); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            FullScreenHost.Say($"window: its edges could not be fixed ({ex.Message}); a dragged size is put back instead");
        }
    }
}
