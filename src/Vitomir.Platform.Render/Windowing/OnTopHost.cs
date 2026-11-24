// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A full screen at a video mode of its own kept above the task bar (ours; the rule and what set it are
// `Platform.Windowing.DesktopRoom.StaysOnTop`). Windows only, and nothing of it can be run in the cloud.
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// Once a frame (<see cref="Frame"/>): while the window is full screen at a mode of its own and has the
/// keyboard it is made topmost (<c>SDL_SetWindowAlwaysOnTop</c>), and four times a second it is looked
/// for under the task bar (<see cref="Above"/>) and lifted over it again (<c>SetWindowPos</c>) — the
/// task bar is topmost too, and a change of video mode moves it a moment after the window has come to
/// the front, which is how it came to lie over the game on a desktop. Without the keyboard
/// (Alt+Tab), or out of that kind of full screen, it is an ordinary window again. Each change is said in
/// the log, a lift the first few times.
/// </summary>
static unsafe class OnTopHost
{
    /// <summary>How often a topmost window is looked for under the task bar (ours).</summary>
    const long LookMs = 250;
    /// <summary>How many lifts are said in the log before it stops saying them (ours).</summary>
    const int SaidLifts = 5;

    static bool on, refused;
    static long lookAt;
    static int lifts;

    public static void Frame()
    {
        var window = FullScreenHost.Window;
        if (window == null || !OperatingSystem.IsWindows() || WindowRun.Replaying) return;
        var flags = SDL_GetWindowFlags(window);
        bool want = DesktopRoom.StaysOnTop(
            (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0, SDL_GetWindowFullscreenMode(window) != null,
            (flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0 && (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) == 0);
        if (want != on)
        {
            if (!SDL_SetWindowAlwaysOnTop(window, want))
            {
                if (!refused) FullScreenHost.Say($"window: could not be {(want ? "kept above" : "let down from above")} the other windows ({SDL_GetError()})");
                refused = true;
                return;
            }
            on = want;
            lookAt = 0;
            FullScreenHost.Say(on ? "window: kept above the task bar while it is full screen at a mode of its own and has the keyboard"
                                  : "window: no longer kept above the other windows");
        }
        if (!on || Environment.TickCount64 < lookAt) return;
        lookAt = Environment.TickCount64 + LookMs;
        try
        {
            nint hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, 0);
            nint tray = hwnd == 0 ? 0 : FindWindow("Shell_TrayWnd", null);
            if (tray == 0 || !Above(hwnd, tray)) return;
            SetWindowPos(hwnd, Topmost, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
            if (lifts++ < SaidLifts) FullScreenHost.Say("window: the task bar had come above the window; the window is lifted over it"
                                                        + (lifts == SaidLifts ? " (said no more)" : ""));
        }
        catch (Exception ex)          // never run before a Windows desktop: a failure here costs a log line, not the game
        {
            if (!refused) FullScreenHost.Say($"window: the task bar could not be looked for ({ex.Message})");
            refused = true;
            on = false;
        }
    }

    /// <summary>Whether <paramref name="other"/> is above <paramref name="hwnd"/> in the system's order of windows (the first 500 above it are looked at).</summary>
    public static bool Above(nint hwnd, nint other)
    {
        if (hwnd == 0 || other == 0) return false;
        nint w = GetWindow(hwnd, Previous);
        for (int i = 0; w != 0 && i < 500; i++, w = GetWindow(w, Previous))
            if (w == other) return true;
        return false;
    }

    const uint Previous = 3;                       // GW_HWNDPREV: the window above
    const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;
    static readonly nint Topmost = -1;             // HWND_TOPMOST

    [DllImport("user32.dll")] static extern nint GetWindow(nint hwnd, uint which);
    [DllImport("user32.dll")] static extern int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] static extern nint FindWindow(string windowClass, string? title);
}
